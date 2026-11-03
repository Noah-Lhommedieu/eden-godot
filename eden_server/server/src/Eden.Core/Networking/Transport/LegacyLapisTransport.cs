using System.Net.Sockets;
using System.Security.Cryptography;

namespace Eden.Core.Networking.Transport;

/// <summary>
/// Transport lapis historique (CMSTCPChannel / CMSTCPConnection dans
/// les binaires serveur) — handshake confirmé par désassemblage :
/// <code>
///   1. Serveur -> client : clé publique RSA-2048 brute
///      [u32 n_len][u32 e_len][n (BE)][e (BE)]   (MakeRSAKeyData @0x8685610)
///   2. Client -> serveur : RSA_size octets = RSA_public_encrypt(clé RC4 5 octets)
///      (ReceiveRC4Key @0x868ca40 : RSA_private_decrypt attendu = 5 octets)
///   3. Paquets : [u16 len en clair][RC4(u16 NCID + champs)]
///      len = taille du payload chiffré, RC4 stream continu par direction.
/// </code>
/// <paramref name="encryptPayloads"/>=false reproduit le lien
/// Zone↔Mission : la clé RC4 est échangée puis ignorée — le trafic
/// reste en clair (vérifié live, voir MissionSession).
/// </summary>
public sealed class LegacyLapisTransport : ITransport
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly RSA? _serverKey;
    private readonly bool _isServer;
    private readonly bool _encryptPayloads;
    private readonly TimeSpan _handshakeTimeout;
    private Rc4? _sendCipher;
    private Rc4? _recvCipher;

    /// <summary>Délai max du handshake côté serveur (anti-flood : un
    /// pair qui récupère la clé RSA puis stalle tiendrait sinon un
    /// thread de lecture indéfiniment). <c>EDEN_HANDSHAKE_TIMEOUT_SEC</c>,
    /// défaut 10 s.</summary>
    private static TimeSpan DefaultHandshakeTimeout =>
        TimeSpan.FromSeconds(EdenConfig.HandshakeTimeoutSec is { } h
            && h > 0 ? h : 10);

    /// <summary>
    /// <paramref name="serverKey"/> : paire RSA-2048 serveur (requis
    /// en mode <paramref name="isServer"/>, ignoré côté client — la
    /// clé publique est lue dans le blob reçu).
    /// <paramref name="encryptPayloads"/> : false = handshake fait mais
    /// trafic en clair (lien ZM).
    /// </summary>
    public LegacyLapisTransport(TcpClient tcpClient, RSA? serverKey = null,
        bool isServer = true, bool encryptPayloads = true,
        TimeSpan? handshakeTimeout = null)
    {
        if (isServer && serverKey == null)
            throw new ArgumentNullException(nameof(serverKey));
        _tcpClient = tcpClient;
        _serverKey = serverKey;
        _isServer = isServer;
        _encryptPayloads = encryptPayloads;
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        // Même réglage que l'ancien ctor NetSession(TcpClient) — les
        // accept-paths migraient par là (NoDelay + keep-alive borné).
        TcpSocketTuning.Apply(tcpClient);
        _stream = tcpClient.GetStream();
    }

    /// <inheritdoc/>
    public string RemoteEndPoint =>
        _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

    /// <inheritdoc/>
    public bool IsEncrypted => _sendCipher != null;

    /// <inheritdoc/>
    public bool HasSendTransform => _sendCipher != null;

    /// <inheritdoc/>
    public void TransformSend(Span<byte> p) => _sendCipher?.Process(p);

    /// <inheritdoc/>
    public void TransformRecv(Span<byte> p) => _recvCipher?.Process(p);

    /// <inheritdoc/>
    public ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct) =>
        _stream.ReadAsync(b, ct);

    /// <inheritdoc/>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> b,
        CancellationToken ct) =>
        _stream.WriteAsync(b, ct);

    /// <inheritdoc/>
    public Task StartAsync(ITransportContext ctx, CancellationToken ct) =>
        _isServer ? ServerHandshakeAsync(ctx, ct)
                  : ClientHandshakeAsync(ctx, ct);

    // ------------------------------------------------------------------
    // Handshake côté serveur : blob RSA puis clé RC4 chiffrée du pair.
    // ------------------------------------------------------------------

    /// <summary>Envoie la clé publique RSA (format MakeRSAKeyData
    /// [u32 nlen][u32 elen][n][e]) puis lit exactement
    /// <c>modulus.Length</c> octets chiffrés RSA et déchiffre PKCS#1
    /// (attendu : 5 octets — le natif rejette via 'cmp $0x5, %eax').
    /// Mode <c>encryptPayloads:false</c> (ZM) : le blob réponse est lu
    /// puis ignoré — trafic clair.</summary>
    private async Task ServerHandshakeAsync(ITransportContext ctx,
        CancellationToken ct)
    {
        var p = _serverKey!.ExportParameters(false);
        var n = p.Modulus!;
        var e = p.Exponent!;
        var blob = new byte[8 + n.Length + e.Length];
        BitConverter.GetBytes((uint)n.Length).CopyTo(blob, 0); // u32 : taille modulus
        BitConverter.GetBytes((uint)e.Length).CopyTo(blob, 4); // u32 : taille exposant
        n.CopyTo(blob, 8);                                     // n (BE)
        e.CopyTo(blob, 8 + n.Length);                          // e (BE)
        await ctx.SendRawAsync(blob, ct).ConfigureAwait(false);

        // Timeout handshake borné (anti-flood : pair qui ne finit
        // jamais l'échange RSA/RC4). Lié au CT appelant pour rester
        // annulable — une expiration propre au timeout est traduite
        // en TimeoutException (CloseReason côté NetSession).
        var encrypted = new byte[n.Length];
        using var hsCts = CancellationTokenSource
            .CreateLinkedTokenSource(ct);
        hsCts.CancelAfter(_handshakeTimeout);
        try
        {
            await ReadExactAsync(ctx, encrypted, hsCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"handshake timeout ({_handshakeTimeout.TotalSeconds}s)");
        }
        // ZM : la clé est échangée mais le trafic reste clair — blob
        // lu puis ignoré, jamais déchiffré.
        if (!_encryptPayloads) return;
        byte[] key;
        try
        {
            key = _serverKey.Decrypt(encrypted,
                RSAEncryptionPadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            // Blob RSA malformé (pair hostile/flood) — rejet propre.
            throw new InvalidDataException("clé RC4 indéchiffrable");
        }
        finally
        {
            Array.Clear(encrypted);
        }
        if (key.Length != 5)
            throw new InvalidDataException(
                $"clé RC4 inattendue: {key.Length} octets (attendu 5)");
        InstallCiphers(key);
    }

    // ------------------------------------------------------------------
    // Handshake côté client : lit le blob RSA, renvoie la clé RC4.
    // ------------------------------------------------------------------

    /// <summary>Lit le blob RSA du serveur, génère la clé RC4 5 octets,
    /// la chiffre RSA PKCS#1 v1.5 et la renvoie. Pas de timeout propre
    /// (les liens clients ont toujours fonctionné sur le CT appelant).</summary>
    private async Task ClientHandshakeAsync(ITransportContext ctx,
        CancellationToken ct)
    {
        // 1. blob RSA du serveur : [u32 nlen][u32 elen][n][e]
        var header = new byte[8];
        await ReadExactAsync(ctx, header, ct).ConfigureAwait(false);
        var nlen = BitConverter.ToInt32(header, 0);
        var elen = BitConverter.ToInt32(header, 4);
        if (nlen is < 1 or > 4096 || elen is < 1 or > 64)
            throw new InvalidDataException(
                $"blob RSA invalide n={nlen} e={elen}");
        var blob = new byte[nlen + elen];
        await ReadExactAsync(ctx, blob, ct).ConfigureAwait(false);

        // 2. clé RC4 5 octets chiffrée RSA PKCS#1 v1.5 (00 02 PS 00 msg)
        var key = RandomNumberGenerator.GetBytes(5);
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = blob[..nlen],
            Exponent = blob[nlen..(nlen + elen)],
        });
        await ctx.SendRawAsync(
            rsa.Encrypt(key, RSAEncryptionPadding.Pkcs1), ct)
            .ConfigureAwait(false);
        if (_encryptPayloads) InstallCiphers(key);
    }

    /// <summary>Installe les deux états RC4 (même clé pour
    /// envoi/réception, fidèle à RC4_set_key @0x868cb93/cbb3).</summary>
    private void InstallCiphers(byte[] key)
    {
        _sendCipher = new Rc4(key);
        _recvCipher = new Rc4(key);
    }

    /// <summary>Remplit <paramref name="buf"/> via
    /// <see cref="ITransportContext.ReadRawAsync"/> — EOF avant la fin
    /// = <see cref="EndOfStreamException"/>.</summary>
    private static async Task ReadExactAsync(ITransportContext ctx,
        Memory<byte> buf, CancellationToken ct)
    {
        var got = 0;
        while (got < buf.Length)
        {
            var r = await ctx.ReadRawAsync(buf[got..], ct)
                .ConfigureAwait(false);
            if (r <= 0)
                throw new EndOfStreamException(
                    "EOF pendant le handshake RSA/RC4");
            got += r;
        }
    }

    /// <inheritdoc/>
    public void Abort()
    {
        try
        {
            _tcpClient.Client.Shutdown(SocketShutdown.Both);
        }
        catch (Exception)
        {
            // Socket déjà clos — la boucle de lecture conclura.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _tcpClient.Dispose();
    }
}
