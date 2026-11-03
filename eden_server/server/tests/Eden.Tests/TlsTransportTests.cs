using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Transport;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Incrément (c) phase 4 : <see cref="TlsTransport"/> (TLS 1.3 via
/// <see cref="SslStream"/>) + <see cref="TlsCertificateProvider"/>
/// (cert dev auto-signé persistant) + pinning des liens internes
/// (<see cref="TransportFactory.LinkClientOptions"/>).
/// Vérifié sur paires de sockets loopback réelles, cert généré en
/// répertoire temporaire (jamais le certs/ du repo).
/// </summary>
public class TlsTransportTests : IDisposable
{
    private readonly string _tmpDir =
        Path.Combine(Path.GetTempPath(),
            $"eden-tls-tests-{Guid.NewGuid():N}");

    public void Dispose() =>
        Directory.Delete(_tmpDir, recursive: true);

    /// <summary>Paire de TcpClient connectés en loopback
    /// (listener → accept) — même helper que TransportTests.</summary>
    private static async Task<(TcpClient client, TcpClient server)>
        SocketPairAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var server = await acceptTask;
        return (client, server);
    }

    /// <summary>Génère (ou recharge) le cert dev de test — répertoire
    /// temporaire dédié à ce run de tests.</summary>
    private X509Certificate2 NewCert() =>
        TlsCertificateProvider.LoadOrCreate(
            Path.Combine(_tmpDir, "eden-dev.pfx"));

    /// <summary>Options serveur TLS 1.3 sur le cert de test.</summary>
    private static SslServerAuthenticationOptions ServerOptions(
        X509Certificate2 cert) => new()
        {
            ServerCertificate = cert,
            EnabledSslProtocols = SslProtocols.Tls13,
        };

    /// <summary>Options client TLS 1.3 qui pinne
    /// <paramref name="expectedThumbprint"/> (null → accept tout :
    /// utilisé pour le chemin « insecure » et le test TLS 1.2).</summary>
    private static SslClientAuthenticationOptions ClientOptions(
        string? expectedThumbprint,
        SslProtocols protocols = SslProtocols.Tls13) => new()
        {
            TargetHost = "127.0.0.1",
            EnabledSslProtocols = protocols,
            CertificateRevocationCheckMode =
                X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
                expectedThumbprint == null ||
                cert is X509Certificate2 c2 &&
                TlsCertificateProvider.MatchesThumbprint(c2,
                    expectedThumbprint),
        };

    private static async Task<byte[]> ReadExactAsync(ITransport t, int n)
    {
        var buf = new byte[n];
        var got = 0;
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));
        while (got < n)
        {
            var r = await t.ReadAsync(buf.AsMemory(got, n - got),
                cts.Token);
            if (r <= 0) throw new EndOfStreamException();
            got += r;
        }
        return buf;
    }

    // ---------------------------------------------------------------
    // Certificat dev auto-signé persistant
    // ---------------------------------------------------------------

    /// <summary>Le PFX persiste sur disque : deux chargements rendent
    /// le même certificat — l'empreinte SHA-256 (celle que les
    /// clients/liens pinnent) est stable entre les boots.</summary>
    [Fact]
    public void CertSelfSigned_ThumbprintStableAcrossLoads()
    {
        var path = Path.Combine(_tmpDir, "eden-dev.pfx");
        var first = TlsCertificateProvider.LoadOrCreate(path);
        Assert.True(File.Exists(path));
        var second = TlsCertificateProvider.LoadOrCreate(path);
        Assert.Equal(TlsCertificateProvider.ThumbprintOf(first),
            TlsCertificateProvider.ThumbprintOf(second));
        // SAN attendu : CN=eden-dev couvre localhost/127.0.0.1/::1.
        Assert.Equal("CN=eden-dev", first.Subject);
    }

    /// <summary>Normalisation d'empreinte : « aa:bb », « AA BB » et
    /// « AABB » sont équivalents.</summary>
    [Fact]
    public void Thumbprint_Normalization()
    {
        Assert.Equal("AABBCC",
            TlsCertificateProvider.NormalizeThumbprint("aa:bb cc"));
        var cert = NewCert();
        var raw = TlsCertificateProvider.ThumbprintOf(cert);
        Assert.True(TlsCertificateProvider.MatchesThumbprint(cert,
            raw.ToLowerInvariant()));
        Assert.False(TlsCertificateProvider.MatchesThumbprint(cert,
            new string('0', raw.Length)));
    }

    // ---------------------------------------------------------------
    // Handshake TLS 1.3 end-to-end
    // ---------------------------------------------------------------

    /// <summary>Handshake serveur+client TLS 1.3 (pin de l'empreinte)
    /// puis écho des deux côtés : IsEncrypted s'allume, les octets
    /// passent, les transforms restent no-op.</summary>
    [Fact]
    public async Task TlsHandshake_Tls13_Pinned_EchoBothWays()
    {
        var cert = NewCert();
        var thumb = TlsCertificateProvider.ThumbprintOf(cert);
        var (client, serverTcp) = await SocketPairAsync();
        await using var server = new TlsTransport(serverTcp,
            isServer: true, serverOptions: ServerOptions(cert));
        await using var clientT = new TlsTransport(client,
            isServer: false,
            clientOptions: ClientOptions(thumb));

        await Task.WhenAll(
            server.StartAsync(new DirectTransportContext(server),
                CancellationToken.None),
            clientT.StartAsync(new DirectTransportContext(clientT),
                CancellationToken.None));
        Assert.True(server.IsEncrypted);
        Assert.True(clientT.IsEncrypted);
        Assert.False(server.HasSendTransform);

        // client → serveur
        var msg = new byte[] { 0x2A, 0x00, 1, 2, 3 };
        await clientT.WriteAsync(msg, CancellationToken.None);
        var got = await ReadExactAsync(server, msg.Length);
        Assert.Equal(msg, got);

        // serveur → client
        var back = new byte[] { 9, 8, 7, 6 };
        await server.WriteAsync(back, CancellationToken.None);
        var got2 = await ReadExactAsync(clientT, back.Length);
        Assert.Equal(back, got2);
    }

    /// <summary>Pinning : une empreinte attendue qui ne correspond pas
    /// au cert présenté fait échouer l'authentification cliente
    /// (<see cref="AuthenticationException"/>).</summary>
    [Fact]
    public async Task TlsHandshake_PinMismatch_Rejected()
    {
        var cert = NewCert();
        var wrong = new string('0', 64); // SHA-256 hex ≠ cert
        var (client, serverTcp) = await SocketPairAsync();
        await using var server = new TlsTransport(serverTcp,
            isServer: true, serverOptions: ServerOptions(cert));
        await using var clientT = new TlsTransport(client,
            isServer: false,
            clientOptions: ClientOptions(wrong));

        var serverStart = server.StartAsync(
            new DirectTransportContext(server), CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            clientT.StartAsync(new DirectTransportContext(clientT),
                CancellationToken.None));
        // TLS 1.3 : le serveur peut légitimement conclure son propre
        // handshake (Finished émis) avant de recevoir l'alerte du
        // client — on ne lui impose donc rien, juste la terminaison.
        try { await serverStart.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { /* alerte reçue → échec serveur, attendu aussi */ }
    }

    /// <summary>Un client limité à TLS 1.2 est refusé : le serveur
    /// impose <see cref="SslProtocols.Tls13"/> — la négociation
    /// échoue des deux côtés.</summary>
    [Fact]
    public async Task TlsHandshake_Tls12Client_Refused()
    {
        var cert = NewCert();
        var (client, serverTcp) = await SocketPairAsync();
        await using var server = new TlsTransport(serverTcp,
            isServer: true, serverOptions: ServerOptions(cert));
        await using var clientT = new TlsTransport(client,
            isServer: false,
            clientOptions: ClientOptions(null,
                SslProtocols.Tls12));

        var serverStart = server.StartAsync(
            new DirectTransportContext(server), CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            clientT.StartAsync(new DirectTransportContext(clientT),
                CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => serverStart);
        Assert.False(clientT.IsEncrypted);
        Assert.False(server.IsEncrypted);
    }

    /// <summary>NetSession sur TlsTransport : StartTransportAsync fait
    /// le handshake TLS puis SendPacketAsync émet
    /// [varint len][u16 ncid + payload] en clair applicatif (le
    /// chiffrement est sous la couche — aucun transform).</summary>
    [Fact]
    public async Task NetSession_OverTlsTransport_Frames()
    {
        var cert = NewCert();
        var thumb = TlsCertificateProvider.ThumbprintOf(cert);
        var (client, serverTcp) = await SocketPairAsync();
        await using var session = new NetSession(new TlsTransport(
            serverTcp, isServer: true,
            serverOptions: ServerOptions(cert)));
        await using var clientT = new TlsTransport(client,
            isServer: false,
            clientOptions: ClientOptions(thumb));

        var serverStart = session.StartTransportAsync();
        await clientT.StartAsync(new DirectTransportContext(clientT),
            CancellationToken.None);
        Assert.True(await serverStart);
        Assert.True(session.CryptoReady);

        await session.SendPacketAsync(42, [1, 2]);
        // Préfixe varint lu octet par octet (4 → 1 octet).
        var hdr = new List<byte>();
        uint len;
        while (true)
        {
            hdr.AddRange(await ReadExactAsync(clientT, 1));
            if (VarintCodec.TryReadVarint(hdr.ToArray(),
                    uint.MaxValue, out len, out _))
                break;
        }
        Assert.Equal(4u, len);
        var payload = await ReadExactAsync(clientT, (int)len);
        // Aucun TransformRecv à appliquer : le payload est déjà clair.
        Assert.Equal(new byte[] { 42, 0, 1, 2 }, payload);
    }

    /// <summary>Un pair qui ouvre le TCP puis ne répond jamais au
    /// ClientHello est rejeté par le timeout —
    /// <see cref="NetSession.StartTransportAsync"/> rend false avec
    /// « handshake timeout » en <see cref="NetSession.CloseReason"/>.</summary>
    [Fact]
    public async Task NetSession_TlsHandshakeTimeout_CloseReason()
    {
        var cert = NewCert();
        var (client, serverTcp) = await SocketPairAsync();
        await using var session = new NetSession(new TlsTransport(
            serverTcp, isServer: true,
            serverOptions: ServerOptions(cert),
            handshakeTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.False(await session.StartTransportAsync());
        Assert.Contains("handshake timeout", session.CloseReason);
        client.Dispose();
    }

    // ---------------------------------------------------------------
    // Validation des liens internes (EDEN_LINK_TLS=pin|insecure)
    // ---------------------------------------------------------------

    /// <summary>Mode pin explicite : le callback n'accepte que
    /// l'empreinte attendue ; mode insecure : tout certificat passe ;
    /// pin sans empreinte disponible = erreur de config immédiate.</summary>
    [Fact]
    public void LinkClientOptions_PinInsecureModes()
    {
        var cert = NewCert();
        var thumb = TlsCertificateProvider.ThumbprintOf(cert);

        var pin = TransportFactory.LinkClientOptions("h", "pin", thumb);
        Assert.True(pin.RemoteCertificateValidationCallback!(
            new object(), cert, null, SslPolicyErrors.None));
        Assert.False(pin.RemoteCertificateValidationCallback!(
            new object(), NewCertOther(), null, SslPolicyErrors.None));

        var auto = TransportFactory.LinkClientOptions("h", null, thumb);
        Assert.False(auto.RemoteCertificateValidationCallback!(
            new object(), NewCertOther(), null, SslPolicyErrors.None));

        var insecure = TransportFactory.LinkClientOptions(
            "h", "insecure", null);
        Assert.True(insecure.RemoteCertificateValidationCallback!(
            new object(), NewCertOther(), null,
            SslPolicyErrors.RemoteCertificateChainErrors));

        Assert.Throws<InvalidOperationException>(() =>
            TransportFactory.LinkClientOptions("h", "pin", null));

        Assert.Equal(SslProtocols.Tls13, pin.EnabledSslProtocols);
    }

    /// <summary>Un autre cert auto-signé (empreinte différente) —
    /// généré dans un sous-répertoire du tmp de test.</summary>
    private X509Certificate2 NewCertOther() =>
        TlsCertificateProvider.LoadOrCreate(
            Path.Combine(_tmpDir, "other", "eden-dev.pfx"));
}
