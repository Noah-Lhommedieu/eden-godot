using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Eden.Core;
using Eden.Core.Networking;
using Eden.Core.Networking.Transport;

namespace Eden.Bots;

/// <summary>Transport client zone/ticket — désormais un simple
/// enrobage de l'<see cref="ITransport"/> de prod (phase 4-c) :
/// <list type="bullet">
/// <item>« modern » (défaut) : <see cref="TlsTransport"/> TLS 1.3 —
/// validation = pinning SHA-256 (--tls-thumbprint ou env
/// <c>EDEN_TLS_THUMBPRINT</c>), accept-any dev (--tls-insecure), ou
/// la résolution standard des liens internes
/// (<see cref="TransportFactory.LinkClientOptions"/>) qui pint le
/// cert local partagé quand il existe.</item>
/// <item>« legacy » : <see cref="LegacyLapisTransport"/> — handshake
/// RSA [u32 nLen][u32 eLen][n][e] + réponse PKCS#1 v1.5 chiffrant la
/// clé RC4 5B, puis trames [varint len][RC4 payload].</item>
/// </list>
/// Le chiffrement sortant reste sérialisé pour que le keystream RC4
/// suive l'ordre réel du fil (no-op sous TLS).</summary>
public sealed class BotTransport : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private ITransport? _transport;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>Connexion + handshake de transport.
    /// <paramref name="transport"/> = « modern » (TLS 1.3, défaut) ou
    /// « legacy » (RSA/RC4) ; <paramref name="tlsThumbprint"/> pinne
    /// le cert serveur (SHA-256 hex) ; <paramref name="tlsInsecure"/>
    /// accepte tout cert (dev).</summary>
    public static async Task<BotTransport> ConnectAsync(string host,
        int port, CancellationToken ct, string transport = "modern",
        string? tlsThumbprint = null, bool tlsInsecure = false)
    {
        var t = new BotTransport();
        await t._tcp.ConnectAsync(host, port, ct);
        t._transport = CreateTransport(t._tcp, host, transport,
            tlsThumbprint, tlsInsecure);
        await t._transport.StartAsync(
            new DirectTransportContext(t._transport), ct);
        return t;
    }

    /// <summary>Choix du transport selon le mode demandé — miroir du
    /// comportement serveur (<c>EDEN_TRANSPORT</c>) pour que les bots
    /// exercent exactement le chemin wire du client Godot.</summary>
    private static ITransport CreateTransport(TcpClient tcp,
        string host, string transport, string? tlsThumbprint,
        bool tlsInsecure)
    {
        if (string.Equals(transport, "legacy",
                StringComparison.OrdinalIgnoreCase))
            return new LegacyLapisTransport(tcp, isServer: false);
        return new TlsTransport(tcp, isServer: false,
            clientOptions: ClientOptions(host, tlsThumbprint,
                tlsInsecure));
    }

    /// <summary>Validation TLS côté bot : --tls-insecure → accept-any
    /// averti ; --tls-thumbprint / EDEN_TLS_THUMBPRINT → pinning
    /// SHA-256 ; sinon la résolution standard des liens internes
    /// (cert local partagé pinné quand présent).</summary>
    private static SslClientAuthenticationOptions ClientOptions(
        string host, string? tlsThumbprint, bool tlsInsecure)
    {
        if (tlsThumbprint == null && !tlsInsecure)
            return TransportFactory.LinkClientOptions(host);
        RemoteCertificateValidationCallback validation = tlsInsecure
            ? AcceptInsecure
            : (_, cert, _, _) =>
                cert is X509Certificate2 c2 &&
                TlsCertificateProvider.MatchesThumbprint(c2,
                    TlsCertificateProvider.NormalizeThumbprint(
                        tlsThumbprint!));
        return new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls13,
            RemoteCertificateValidationCallback = validation,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
    }

    /// <summary>Accept-any --tls-insecure : warning une fois par run.</summary>
    private static bool AcceptInsecure(object sender,
        X509Certificate? cert, X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (Interlocked.Exchange(ref _insecureWarned, 1) == 0)
            Console.Error.WriteLine(
                "[bots] --tls-insecure : validation du certificat " +
                "serveur désactivée (dev)");
        return true;
    }
    private static int _insecureWarned;

    /// <summary>Envoie une trame complète `[varint len][payload]`
    /// (payload inclut le NCID little-endian en tête ; RC4 en legacy,
    /// intact sous TLS).</summary>
    public async Task SendPayloadAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length < 2 || payload.Length > EdenConfig.MaxFrameSize)
            throw new ArgumentOutOfRangeException(nameof(payload));
        var transport = _transport
            ?? throw new InvalidOperationException("non connecté");
        await _sendGate.WaitAsync(ct);
        try
        {
            var frame = new byte[VarintCodec.MaxEncodedLength +
                payload.Length];
            var hdrLen = VarintCodec.WriteVarint(frame,
                (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, frame, hdrLen, payload.Length);
            transport.TransformSend(frame.AsSpan(hdrLen, payload.Length));
            await transport.WriteAsync(
                frame.AsMemory(0, hdrLen + payload.Length), ct);
        }
        finally { _sendGate.Release(); }
    }

    public Task SendAsync(ushort ncid, ReadOnlyMemory<byte> body,
        CancellationToken ct)
    {
        var p = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(p, ncid);
        body.CopyTo(p.AsMemory(2));
        return SendPayloadAsync(p, ct);
    }

    /// <summary>Handshake de version post-transport (phase 4d —
    /// transport modern seulement ; en legacy le gate est absent) :
    /// envoie le VersionHello (NCID 0xFFFF) et lit la réponse —
    /// 0xFFFE ack ou 0xFFFD rejet. <paramref name="manifestHashHex"/>
    /// null = manifest depuis EdenConfig.GameDataDir s'il est résolu,
    /// sinon chaîne vide.</summary>
    /// <returns>La trame de réponse brute (NCID inclus) — le caller la
    /// décode via <see cref="VersionProtocol.ParseReply"/>.</returns>
    public async Task<byte[]> VersionHandshakeAsync(string clientVersion,
        string? manifestHashHex, CancellationToken ct)
    {
        manifestHashHex ??= Eden.Core.Data.GameDataManifest
            .Current?.Hex ?? "";
        var body = VersionProtocol.BuildHello(
            new VersionProtocol.ProtocolVersion(1, 0), clientVersion,
            manifestHashHex);
        var frame = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame,
            VersionProtocol.NcidHello);
        body.CopyTo(frame.AsSpan(2));
        await SendPayloadAsync(frame, ct);
        return await ReceiveAsync(ct)
            ?? throw new EndOfStreamException(
                "fermé avant la réponse de version");
    }

    /// <summary>Lit une trame et retourne le payload déchiffré
    /// (NCID inclus). Retourne null sur fermeture propre.</summary>
    public async Task<byte[]?> ReceiveAsync(CancellationToken ct)
    {
        var transport = _transport
            ?? throw new InvalidOperationException("non connecté");
        // Préfixe varint lu octet par octet (≤5) — un premier octet
        // jamais reçu = fermeture propre ; EOF en plein préfixe ou
        // varint malformé = erreur (désync), comme la lecture de
        // payload qui suit.
        var lenBuf = new byte[VarintCodec.MaxEncodedLength];
        var got = 0;
        uint len;
        while (true)
        {
            int n;
            try { n = await transport.ReadAsync(lenBuf.AsMemory(got, 1), ct); }
            catch (EndOfStreamException) { return null; }
            if (n <= 0)
            {
                if (got == 0) return null;
                throw new EndOfStreamException(
                    "connexion fermée dans un préfixe varint");
            }
            got += n;
            if (VarintCodec.TryReadVarint(lenBuf.AsSpan(0, got),
                    (uint)EdenConfig.MaxFrameSize, out len, out _))
                break;
        }
        if (len < 2) throw new InvalidDataException($"trame courte len={len}");
        var payload = await ReadExactAsync(transport, (int)len, ct);
        transport.TransformRecv(payload);
        return payload;
    }

    private static async Task<byte[]> ReadExactAsync(ITransport t,
        int len, CancellationToken ct)
    {
        var b = new byte[len];
        var got = 0;
        while (got < len)
        {
            var n = await t.ReadAsync(b.AsMemory(got, len - got), ct);
            if (n <= 0)
                throw new EndOfStreamException(
                    "connexion fermée dans un payload");
            got += n;
        }
        return b;
    }

    public async ValueTask DisposeAsync()
    {
        _sendGate.Dispose();
        if (_transport != null)
            try { await _transport.DisposeAsync(); } catch { }
        _tcp.Close();
    }
}
