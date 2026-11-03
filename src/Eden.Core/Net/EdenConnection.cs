using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

namespace EdenClient.Net;

/// <summary>
/// Connexion TCP au serveur Eden — transport piloté par
/// <see cref="Transport"/> :
///
///   <b>modern</b> (défaut) : TLS 1.3 via <see cref="SslStream"/>
///   (<c>AuthenticateAsClientAsync</c>). Validation : pinning SHA-256
///   <see cref="TlsThumbprint"/> (dev, miroir du
///   <c>EDEN_TLS_THUMBPRINT</c> serveur), ou validation chaîne
///   standard en prod ; <see cref="TlsInsecure"/> accepte tout
///   certificat (flag dev, warning).
///
///   <b>legacy</b> (<c>transport=legacy</c> dans client.cfg — repli
///   quand le serveur tourne en <c>EDEN_TRANSPORT=legacy</c>) :
///   handshake lapis historique
///     S→C : [u32 n_len LE][u32 e_len LE][n big-endian][e big-endian]
///     C→S : 256 octets = RSA PKCS#1 v1.5 type 2 de la clé RC4 session
///     (5 octets), puis RC4 continu par direction.
///
/// Trames dans les deux modes : [varint len en clair][payload],
/// payload = [u16 NCID][champs] (RC4 en legacy, chiffré TLS en
/// modern).
/// </summary>
public sealed class EdenConnection : IAsyncDisposable
{
    /// <summary>Une trame décodée : NCID + payload complet (NCID inclus).</summary>
    public sealed record Frame(ushort Ncid, byte[] Payload);

    private TcpClient? _tcp;
    private Stream? _stream;
    private Rc4? _sendCipher;
    private Rc4? _recvCipher;
    /// <summary>Sérialise les écritures : <see cref="SslStream"/> lève
    /// <see cref="NotSupportedException"/> si deux WriteAsync sont en
    /// vol, et en legacy deux trames pourraient s'entrelacer. Un seul
    /// write à la fois (l'ordre d'arrivée des envoyeurs n'est pas
    /// garanti mais chaque trame part entière).</summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Channel<Frame> _frames =
        Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions { SingleReader = true });
    private Task? _readLoop;
    private readonly byte[] _readBuf = new byte[8192];

    /// <summary>« modern » (TLS 1.3, défaut) ou « legacy »
    /// (handshake RSA/RC4 lapis — repli serveur
    /// <c>EDEN_TRANSPORT=legacy</c>).</summary>
    public string Transport { get; set; } = "modern";

    /// <summary>Empreinte SHA-256 hex du certificat serveur attendu
    /// (pinning dev — séparateurs « : »/espaces tolérés). Posée →
    /// prioritaire sur la validation chaîne standard.</summary>
    public string? TlsThumbprint { get; set; }

    /// <summary>Flag dev : accepte n'importe quel certificat serveur
    /// (<c>tls_insecure=1</c> — warning logué à l'usage). Ignoré si
    /// <see cref="TlsThumbprint"/> est posé.</summary>
    public bool TlsInsecure { get; set; }

    /// <summary>Clé RC4 de session (5 octets, mode legacy) — exposée
    /// pour les tests ; null en mode TLS.</summary>
    public byte[]? SessionKey { get; private set; }

    /// <summary>Version applicative annoncée dans le VersionHello
    /// (miroir <see cref="ClientConfig.ClientVersion"/>).</summary>
    public string ClientVersion { get; set; } = "002.003.05.67";

    /// <summary>Handshake de version post-TLS (phase 4d) — défaut
    /// activé : le client envoie VersionHello puis lit la réponse
    /// (<c>0xFFFE</c> ack / <c>0xFFFD</c> rejet →
    /// <see cref="UpdateRequiredException"/>). Une première trame
    /// d'un autre NCID = serveur sans gate (repli tolérant : la trame
    /// est remise au canal de réception). Jamais émis en legacy.</summary>
    public bool VersionCheck { get; set; } = true;

    /// <summary>Empreinte SHA-256 du manifest gamedata client (32
    /// octets) — null = calculée depuis <see cref="ManifestDir"/> ;
    /// les deux null → hello envoyé avec hash vide.</summary>
    public byte[]? ManifestHashOverride { get; set; }

    /// <summary>Répertoire du corpus <c>*.ini</c> client
    /// (ex. <c>data/db</c>) dont le manifest est haché — ignoré si
    /// <see cref="ManifestHash"/> est posé.</summary>
    public string? ManifestDir { get; set; }

    /// <summary>Fin du flux (fermeture distante ou erreur de lecture).</summary>
    public ChannelReader<Frame> Frames => _frames.Reader;

    public bool IsConnected => _tcp?.Connected ?? false;

    /// <summary>Vrai en mode « legacy » (RSA/RC4).</summary>
    private bool IsLegacy => string.Equals(Transport, "legacy",
        StringComparison.OrdinalIgnoreCase);

    /// <summary>TCP connect + handshake de transport (TLS 1.3 ou
    /// RSA→RC4 legacy), puis lance la boucle de réception.</summary>
    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);

        if (IsLegacy)
            await LegacyHandshakeAsync(ct).ConfigureAwait(false);
        else
        {
            await TlsHandshakeAsync(host, ct).ConfigureAwait(false);
            if (VersionCheck)
                await VersionHandshakeAsync(ct).ConfigureAwait(false);
        }
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Handshake TLS 1.3 : <see cref="SslStream"/> authentifié
    /// côté client. Une erreur d'authentification (cert refusé,
    /// empreinte non conforme, TLS trop ancien) remonte en
    /// <see cref="AuthenticationException"/> — exception typée que
    /// l'UI affiche déjà via ses handlers d'erreur.</summary>
    private async Task TlsHandshakeAsync(string host, CancellationToken ct)
    {
        var ssl = new SslStream(_tcp!.GetStream(),
            leaveInnerStreamOpen: false);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls13,
            RemoteCertificateValidationCallback =
                BuildValidationCallback(),
            // Le cert dev auto-signé n'a ni CRL ni OCSP — éviter les
            // lookups réseau (la prod peut laisser NoCheck aussi : la
            // validation revient alors à la chaîne standard).
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
        await ssl.AuthenticateAsClientAsync(options, ct)
            .ConfigureAwait(false);
        _stream = ssl;
    }

    /// <summary>Validation du cert serveur : pin SHA-256 si
    /// <see cref="TlsThumbprint"/>, accept-any averti si
    /// <see cref="TlsInsecure"/>, sinon chaîne standard.</summary>
    private RemoteCertificateValidationCallback BuildValidationCallback()
    {
        if (TlsThumbprint is { } pin)
        {
            var expected = NormalizeThumbprint(pin);
            return (_, cert, _, _) =>
                cert is X509Certificate2 c2 &&
                string.Equals(ThumbprintOf(c2), expected,
                    StringComparison.OrdinalIgnoreCase);
        }
        if (TlsInsecure)
        {
            Console.Error.WriteLine(
                "[tls] tls_insecure=1 — validation du certificat " +
                "serveur désactivée (dev)");
            return (_, _, _, _) => true;
        }
        return (_, _, _, errors) => errors == SslPolicyErrors.None;
    }

    /// <summary>Empreinte SHA-256 hex majuscule (sans séparateurs)
    /// d'un certificat — même format que le
    /// <c>[TLS] thumbprint=</c> logué par le serveur.</summary>
    internal static string ThumbprintOf(X509Certificate2 cert) =>
        cert.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>Normalise une empreinte saisie : retire tout ce qui
    /// n'est pas un chiffre hexadécimal et met en majuscules —
    /// « AA:BB », « aa bb » et « AABB » désignent la même.</summary>
    internal static string NormalizeThumbprint(string value)
    {
        var chars = new char[value.Length];
        var n = 0;
        foreach (var c in value)
            if (Uri.IsHexDigit(c))
                chars[n++] = char.ToUpperInvariant(c);
        return new string(chars, 0, n);
    }

    /// <summary>Handshake lapis legacy : blob RSA du serveur puis
    /// clé RC4 5 octets chiffrée PKCS#1 v1.5 (référence
    /// $EDEN/tools/eden_headless.py::handshake).</summary>
    private async Task LegacyHandshakeAsync(CancellationToken ct)
    {
        _stream = _tcp!.GetStream();

        // [u32 n_len LE][u32 e_len LE][n][e]
        var hdr = await ReadExactAsync(8, ct).ConfigureAwait(false);
        var nLen = BitConverter.ToInt32(hdr, 0);
        var eLen = BitConverter.ToInt32(hdr, 4);
        if (nLen <= 0 || nLen > 1024 || eLen <= 0 || eLen > 16)
            throw new InvalidDataException($"Handshake RSA invalide : n_len={nLen} e_len={eLen}");
        var modulus = await ReadExactAsync(nLen, ct).ConfigureAwait(false);
        var exponent = await ReadExactAsync(eLen, ct).ConfigureAwait(false);

        var key = RandomNumberGenerator.GetBytes(5);
        var blob = RsaEncryptPkcs1(key, modulus, exponent);
        await _stream.WriteAsync(blob, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        SessionKey = key;
        _sendCipher = new Rc4(key);
        _recvCipher = new Rc4(key);
    }

    /// <summary>Handshake de version post-TLS (phase 4d) : envoie
    /// VersionHello puis lit la réponse <i>synchroniquement</i> (avant
    /// le démarrage de <see cref="ReadLoopAsync"/>) — la première trame
    /// est le VersionAck/Reject du serveur, garanti par le gate qui la
    /// précède de tout autre push.
    /// <list type="bullet">
    /// <item>0xFFFE → handshake accepté.</item>
    /// <item>0xFFFD → <see cref="UpdateRequiredException"/> (UI :
    /// « mise à jour requise »).</item>
    /// <item>autre NCID → serveur sans gate
    /// (<c>EDEN_VERSION_CHECK=0</c>) : la trame est remise au canal et
    /// la connexion continue — repli tolérant.</item>
    /// </list></summary>
    private async Task VersionHandshakeAsync(CancellationToken ct)
    {
        var hash = ManifestHashOverride;
        if (hash == null && ManifestDir is { } dir)
            hash = ManifestHash.Compute(dir)?.Hash;
        var hashHex = hash != null
            ? Convert.ToHexString(hash).ToLowerInvariant()
            : "";
        await SendPayloadAsync(VersionProtocol.BuildHelloFrame(
            ClientVersion, hashHex), ct).ConfigureAwait(false);

        // Lit la trame de réponse directement sur le stream (la boucle
        // de lecture n'est pas encore démarrée — pas de course).
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var payload = await ReadFrameRawAsync(timeout.Token)
            .ConfigureAwait(false);
        var reply = VersionProtocol.ParseReply(payload);
        if (reply == null)
        {
            // Serveur sans gate — la trame lue est un paquet métier :
            // on la repousse dans le canal de réception et on continue.
            var ncid = (ushort)(payload[0] | (payload[1] << 8));
            await _frames.Writer.WriteAsync(
                new Frame(ncid, payload), ct).ConfigureAwait(false);
            return;
        }
        if (!reply.Accepted)
        {
            var detail = reply.Reason switch
            {
                VersionProtocol.RejectReason.UpdateRequired =>
                    "mise à jour requise",
                VersionProtocol.RejectReason.TooNew =>
                    "protocole plus récent que le serveur",
                _ => "hello de version malformé",
            };
            var min = reply.MinClientVersion.Length > 0
                ? $" (version ≥ {reply.MinClientVersion} requise)" : "";
            throw new UpdateRequiredException(
                $"version refusée par le serveur : {detail}{min}",
                reply.MinClientVersion, reply.ServerManifestHashHex);
        }
    }

    /// <summary>Lit UNE trame [varint len][payload] du stream —
    /// utilisée uniquement pendant le handshake de version (avant la
    /// boucle de réception ; pas de RC4 : modern = TLS).</summary>
    private async Task<byte[]> ReadFrameRawAsync(CancellationToken ct)
    {
        var lenBuf = new byte[VarintCodec.MaxEncodedLength];
        var got = 0;
        uint len;
        while (true)
        {
            var n = await _stream!.ReadAsync(
                lenBuf.AsMemory(got, 1), ct).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException(
                    "connexion fermée avant la réponse de version");
            got += n;
            if (VarintCodec.TryReadVarint(lenBuf.AsSpan(0, got),
                    1024 * 1024, out len, out _))
                break;
        }
        var payload = await ReadExactAsync((int)len, ct)
            .ConfigureAwait(false);
        if (payload.Length < 2)
            throw new InvalidDataException("trame de version tronquée");
        return payload;
    }

    /// <summary>RSA PKCS#1 v1.5 (type 2, padding aléatoire non-zero) —
    /// équivalent RSA_public_encrypt du client natif (legacy).</summary>
    private static byte[] RsaEncryptPkcs1(byte[] data, byte[] modulus, byte[] exponent)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
        return rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
    }

    /// <summary>Envoie un message : [varint len][u16 NCID + corps]
    /// (corps RC4 en legacy).</summary>
    public Task SendAsync(NetMessage message, CancellationToken ct = default)
        => SendPayloadAsync(message.ToPayload(), ct);

    /// <summary>Envoie un payload brut ([u16 NCID][corps] déjà
    /// assemblé) — préfixe varint LEB128 de la taille du payload, en
    /// clair comme l'ancien u16. Le corps n'est transformé qu'en
    /// mode legacy (RC4) ; en TLS il part tel quel dans le tunnel.</summary>
    public async Task SendPayloadAsync(byte[] payload, CancellationToken ct = default)
    {
        if (_stream == null)
            throw new InvalidOperationException("Connexion non établie");
        // Le chiffrement RC4 est stateful : la transformation ET le
        // write doivent rester sous le même verrou, sinon deux envois
        // concurrents chiffreraient dans le désordre (stream corrompu).
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var frame = new byte[
                VarintCodec.MaxEncodedLength + payload.Length];
            var hdrLen = VarintCodec.WriteVarint(
                frame, (uint)payload.Length);
            if (_sendCipher != null)
                _sendCipher.ProcessCopy(payload).CopyTo(frame, hdrLen);
            else
                payload.CopyTo(frame, hdrLen);
            await _stream.WriteAsync(
                frame.AsMemory(0, hdrLen + payload.Length), ct)
                .ConfigureAwait(false);
        }
        finally { _sendGate.Release(); }
    }

    /// <summary>Prochaine trame serveur (null si le flux est terminé).</summary>
    public async Task<Frame?> ReadFrameAsync(CancellationToken ct = default)
        => await _frames.Reader.ReadAsync(ct).ConfigureAwait(false);

    /// <summary>Prochaine trame serveur avec timeout (null au timeout).</summary>
    public async Task<Frame?> ReadFrameAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { return await _frames.Reader.ReadAsync(cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>Vidange non-bloquante : retourne toutes les trames
    /// reçues jusqu'à <paramref name="silence"/> de calme.</summary>
    public async Task<List<Frame>> DrainAsync(TimeSpan silence, CancellationToken ct = default)
    {
        var result = new List<Frame>();
        while (!ct.IsCancellationRequested)
        {
            var f = await ReadFrameAsync(silence).ConfigureAwait(false);
            if (f == null) break;
            result.Add(f);
        }
        return result;
    }

    private async Task ReadLoopAsync()
    {
        // Fenêtre glissante (port de PacketReceiveBuffer serveur) —
        // remplace le List<byte> dont RemoveRange déplaçait tout le
        // reste à chaque trame (O(n²) en burst).
        var pending = new ReceiveBuffer();
        try
        {
            while (true)
            {
                var n = await _stream!.ReadAsync(_readBuf).ConfigureAwait(false);
                if (n == 0) break; // fermeture distante
                pending.Append(_readBuf.AsSpan(0, n));
                // TryTakePacket : false = trame ou préfixe varint
                // incomplet (attendre) ; InvalidDataException =
                // malformé (len<2, overlong, >cap) → fin de connexion.
                while (pending.TryTakePacket(out var enc))
                {
                    // RC4 uniquement en legacy — en TLS le payload
                    // sort déjà déchiffré du tunnel.
                    var payload = _recvCipher?.ProcessCopy(enc) ?? enc;
                    var ncid = (ushort)(payload[0] | (payload[1] << 8));
                    await _frames.Writer.WriteAsync(
                        new Frame(ncid, payload)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
            // fermeture/erreur réseau ou trame malformée — on termine
            // le canal
        }
        finally
        {
            _frames.Writer.TryComplete();
        }
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await _stream!.ReadAsync(
                buf.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Connexion fermée pendant le handshake");
            read += n;
        }
        return buf;
    }

    public async ValueTask DisposeAsync()
    {
        try { _tcp?.Close(); } catch { }
        if (_readLoop != null)
        {
            try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
        }
        if (_stream != null)
            try { await _stream.DisposeAsync().ConfigureAwait(false); }
            catch { }
        _tcp?.Dispose();
    }
}
