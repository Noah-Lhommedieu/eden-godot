using System.Net;
using System.Net.Sockets;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Transport;
using Xunit;
using static Eden.Core.Networking.VersionProtocol;

namespace Eden.Tests;

/// <summary>
/// Incrément (d) phase 4 : <see cref="VersionGate"/> — barrière de
/// version post-handshake sur les accept-paths client-facing.
/// Vérifié sur paires de sockets loopback réelles : une
/// <see cref="NetSession"/> serveur sur <see cref="TcpTransport"/> (le
/// gate est indépendant du chiffrement — l'appelant le pose seulement
/// en modern) face à un client brut qui écrit les trames.
/// </summary>
public class VersionGateTests
{
    private static readonly byte[] ServerHash =
        Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly string ServerHashHex =
        Convert.ToHexString(ServerHash).ToLowerInvariant();

    /// <summary>Options de gate standard pour les tests : proto
    /// serveur 1.0, min client 1.0, pas de borne de version app,
    /// manifest = <see cref="ServerHash"/>, non-strict.
    /// <paramref name="noManifest"/> = serveur sans manifest local —
    /// la comparaison doit être sautée.</summary>
    private static VersionGate.Options Opt(
        ProtocolVersion? serverProto = null,
        ProtocolVersion? minProto = null,
        Version? minClientVersion = null,
        byte[]? manifest = null,
        bool strict = false,
        bool noManifest = false) => new(
        serverProto ?? new ProtocolVersion(1, 0),
        minProto ?? new ProtocolVersion(1, 0),
        minClientVersion,
        noManifest ? null : manifest ?? ServerHash,
        strict);

    /// <summary>Session serveur acceptée sur une paire loopback —
    /// retourne (socket client brut, session serveur).</summary>
    private static async Task<(TcpClient client, NetSession session)>
        PairAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, port);
        var session = new NetSession(new TcpTransport(await acceptTask));
        return (client, session);
    }

    /// <summary>Construit les octets fil d'une trame
    /// `[varint len][payload]`.</summary>
    private static byte[] FrameBytes(byte[] payload)
    {
        var frame = new byte[VarintCodec.MaxEncodedLength +
            payload.Length];
        var hdrLen = VarintCodec.WriteVarint(frame, (uint)payload.Length);
        payload.CopyTo(frame, hdrLen);
        return frame[..(hdrLen + payload.Length)];
    }

    /// <summary>Écrit une trame `[varint len][payload]` brute côté
    /// client.</summary>
    private static async Task WriteFrameAsync(TcpClient client,
        byte[] payload)
    {
        await client.GetStream().WriteAsync(FrameBytes(payload));
        await client.GetStream().FlushAsync();
    }

    /// <summary>Corps [u16 ncid][reste] d'une trame VersionHello.</summary>
    private static byte[] HelloPayload(
        string clientVersion = "002.003.05.67",
        string hashHex = "", ProtocolVersion? proto = null)
    {
        var p = proto ?? new ProtocolVersion(1, 0);
        var body = BuildHello(p, clientVersion, hashHex);
        var payload = new byte[2 + body.Length];
        BitConverter.GetBytes(NcidHello).CopyTo(payload, 0);
        body.CopyTo(payload, 2);
        return payload;
    }

    /// <summary>Écrit le hello complet (NCID + corps).</summary>
    private static Task WriteHelloAsync(TcpClient client,
        string clientVersion = "002.003.05.67",
        string hashHex = "", ProtocolVersion? proto = null) =>
        WriteFrameAsync(client,
            HelloPayload(clientVersion, hashHex, proto));

    /// <summary>Lit UNE trame du client test (timeout 5 s).</summary>
    private static async Task<byte[]> ReadFrameAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var lenBuf = new byte[VarintCodec.MaxEncodedLength];
        var got = 0;
        uint len;
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));
        while (true)
        {
            var n = await stream.ReadAsync(lenBuf.AsMemory(got, 1),
                cts.Token);
            if (n == 0) throw new EndOfStreamException();
            got += n;
            if (VarintCodec.TryReadVarint(lenBuf.AsSpan(0, got),
                    uint.MaxValue, out len, out _))
                break;
        }
        var payload = new byte[len];
        var read = 0;
        while (read < (int)len)
        {
            var n = await stream.ReadAsync(
                payload.AsMemory(read, (int)len - read), cts.Token);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
        return payload;
    }

    // ------------------------------------------------------------------
    // Parcours nominal
    // ------------------------------------------------------------------

    /// <summary>Hello valide (proto 1.0, version ≥ min, manifest =
    /// serveur) → Ack 0xFFFE + trames pipelinées rendues en leftover.</summary>
    [Fact]
    public async Task Gate_ValidHello_AckAndLeftovers()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        // Hello + trame métier pipelinée en UN send → livrés dans la
        // même lecture socket → le gate les voit dans un seul batch
        // (le leftover serait sinon lu par la lecture suivante).
        var biz = new byte[] { 0x2A, 0x00, 9, 9 };
        await client.GetStream().WriteAsync(
            FrameBytes(HelloPayload(hashHex: ServerHashHex))
                .Concat(FrameBytes(biz)).ToArray());
        await client.GetStream().FlushAsync();

        var gate = await VersionGate.HandshakeAsync(session, Opt(),
            CancellationToken.None);
        Assert.True(gate.Accepted);
        Assert.Equal("002.003.05.67", gate.Hello!.ClientVersion);
        Assert.Equal("manifest ok", gate.Reason);

        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.NotNull(reply);
        Assert.True(reply!.Accepted);
        Assert.Equal(ServerHashHex, reply.ServerManifestHashHex);

        // La trame métier pipelinée a été conservée.
        Assert.Single(gate.Remaining);
        Assert.Equal(biz, gate.Remaining[0]);
        client.Dispose();
    }

    /// <summary>Manifest différent en mode non-strict → Ack quand même
    /// (le mismatch est tracé, pas bloquant).</summary>
    [Fact]
    public async Task Gate_ManifestMismatch_NonStrict_Accepts()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        var other = new string('f', 64);
        await WriteHelloAsync(client, hashHex: other);

        var gate = await VersionGate.HandshakeAsync(session,
            Opt(strict: false), CancellationToken.None);
        Assert.True(gate.Accepted);
        Assert.Contains("mismatch", gate.Reason);
        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.True(reply!.Accepted);
        client.Dispose();
    }

    /// <summary>Manifest différent en mode strict → Reject
    /// UpdateRequired + close.</summary>
    [Fact]
    public async Task Gate_ManifestMismatch_Strict_Rejects()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        await WriteHelloAsync(client,
            hashHex: new string('f', 64));

        var gate = await VersionGate.HandshakeAsync(session,
            Opt(strict: true), CancellationToken.None);
        Assert.False(gate.Accepted);
        Assert.Contains("strict", gate.Reason);

        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.False(reply!.Accepted);
        Assert.Equal(RejectReason.UpdateRequired, reply.Reason);
        client.Dispose();
    }

    /// <summary>Manifest client absent + serveur sans manifest local →
    /// la comparaison est sautée, la session passe.</summary>
    [Fact]
    public async Task Gate_NoManifests_SkipsComparison()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        await WriteHelloAsync(client, hashHex: "");

        var gate = await VersionGate.HandshakeAsync(session,
            Opt(noManifest: true), CancellationToken.None);
        Assert.True(gate.Accepted);
        Assert.Contains("absent", gate.Reason);
        client.Dispose();
    }

    // ------------------------------------------------------------------
    // Rejets
    // ------------------------------------------------------------------

    /// <summary>Première trame = paquet métier (client pré-version) →
    /// Reject UpdateRequired, propre sur le fil.</summary>
    [Fact]
    public async Task Gate_FirstFrameNotHello_RejectsUpdateRequired()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        // NCID 0 + 22 octets = trame de ticket d'un vieux client.
        await WriteFrameAsync(client, new byte[24]);

        var gate = await VersionGate.HandshakeAsync(session, Opt(),
            CancellationToken.None);
        Assert.False(gate.Accepted);
        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.False(reply!.Accepted);
        Assert.Equal(RejectReason.UpdateRequired, reply.Reason);
        client.Dispose();
    }

    /// <summary>Trame 0xFFFF au magic incorrect → Reject Malformed.</summary>
    [Fact]
    public async Task Gate_BadMagic_RejectsMalformed()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        var body = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 1, 0 };
        var payload = new byte[2 + body.Length];
        BitConverter.GetBytes(NcidHello).CopyTo(payload, 0);
        body.CopyTo(payload, 2);
        await WriteFrameAsync(client, payload);

        var gate = await VersionGate.HandshakeAsync(session, Opt(),
            CancellationToken.None);
        Assert.False(gate.Accepted);
        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.Equal(RejectReason.Malformed, reply!.Reason);
        client.Dispose();
    }

    /// <summary>Protocole client &lt; min serveur → Reject
    /// UpdateRequired.</summary>
    [Fact]
    public async Task Gate_ProtoBelowMin_RejectsUpdateRequired()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        await WriteHelloAsync(client, hashHex: ServerHashHex,
            proto: new ProtocolVersion(1, 0));

        var gate = await VersionGate.HandshakeAsync(session,
            Opt(minProto: new ProtocolVersion(1, 1)),
            CancellationToken.None);
        Assert.False(gate.Accepted);
        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.Equal(RejectReason.UpdateRequired, reply!.Reason);
        client.Dispose();
    }

    /// <summary>Protocole client &gt; serveur → Reject TooNew.</summary>
    [Fact]
    public async Task Gate_ProtoTooNew_RejectsTooNew()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        await WriteHelloAsync(client, hashHex: ServerHashHex,
            proto: new ProtocolVersion(2, 0));

        var gate = await VersionGate.HandshakeAsync(session, Opt(),
            CancellationToken.None);
        Assert.False(gate.Accepted);
        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.Equal(RejectReason.TooNew, reply!.Reason);
        client.Dispose();
    }

    /// <summary>client_version &lt; EDEN_MIN_CLIENT_VERSION → Reject
    /// UpdateRequired portant la borne annoncée.</summary>
    [Fact]
    public async Task Gate_ClientVersionBelowMin_Rejects()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        await WriteHelloAsync(client, clientVersion: "002.003.05.67",
            hashHex: ServerHashHex);

        var gate = await VersionGate.HandshakeAsync(session,
            Opt(minClientVersion: new Version(3, 0)),
            CancellationToken.None);
        Assert.False(gate.Accepted);
        var reply = ParseReply(await ReadFrameAsync(client));
        Assert.Equal(RejectReason.UpdateRequired, reply!.Reason);
        Assert.Equal("3.0", reply.MinClientVersion);
        client.Dispose();
    }

    /// <summary>client_version ≥ borne → accepté (comparaison
    /// numérique : « 002.010 » &gt; « 002.003 »).</summary>
    [Fact]
    public async Task Gate_ClientVersionAtOrAboveMin_Accepts()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        await WriteHelloAsync(client, clientVersion: "002.010.00.00",
            hashHex: ServerHashHex);

        var gate = await VersionGate.HandshakeAsync(session,
            Opt(minClientVersion: new Version(2, 3)),
            CancellationToken.None);
        Assert.True(gate.Accepted);
        client.Dispose();
    }

    /// <summary>EOF avant toute trame → refus silencieux (la connexion
    /// est déjà morte, rien à répondre).</summary>
    [Fact]
    public async Task Gate_EofBeforeHello_Declines()
    {
        var (client, session) = await PairAsync();
        await using var _ = session;
        client.Client.Shutdown(SocketShutdown.Send); // FIN immédiat

        var gate = await VersionGate.HandshakeAsync(session, Opt(),
            CancellationToken.None);
        Assert.False(gate.Accepted);
        client.Dispose();
    }

    // ------------------------------------------------------------------
    // Codec
    // ------------------------------------------------------------------

    /// <summary>Codec : BuildHello → ParseHello round-trip exact.</summary>
    [Fact]
    public void Codec_HelloRoundTrip()
    {
        var body = BuildHello(new ProtocolVersion(1, 0),
            "002.003.05.67", ServerHashHex);
        var hello = ParseHello(body);
        Assert.NotNull(hello);
        Assert.Equal(new ProtocolVersion(1, 0), hello!.Proto);
        Assert.Equal("002.003.05.67", hello.ClientVersion);
        Assert.Equal(ServerHashHex, hello.ManifestHashHex);
    }

    /// <summary>Codec : ParseReply distingue Ack/Reject et extrait les
    /// champs (reason, hash serveur, borne min).</summary>
    [Fact]
    public void Codec_ReplyRoundTrip()
    {
        var ackBody = BuildAck(ServerHashHex);
        var ack = new byte[2 + ackBody.Length];
        BitConverter.GetBytes(NcidAck).CopyTo(ack, 0);
        ackBody.CopyTo(ack, 2);
        var r1 = ParseReply(ack);
        Assert.True(r1!.Accepted);
        Assert.Equal(ServerHashHex, r1.ServerManifestHashHex);

        var rejBody = BuildReject(RejectReason.TooNew, ServerHashHex,
            "003.000.00.00");
        var rej = new byte[2 + rejBody.Length];
        BitConverter.GetBytes(NcidReject).CopyTo(rej, 0);
        rejBody.CopyTo(rej, 2);
        var r2 = ParseReply(rej);
        Assert.False(r2!.Accepted);
        Assert.Equal(RejectReason.TooNew, r2.Reason);
        Assert.Equal("003.000.00.00", r2.MinClientVersion);

        // NCID autre → null (pas une réponse de version).
        Assert.Null(ParseReply([0x2A, 0x00]));
    }
}
