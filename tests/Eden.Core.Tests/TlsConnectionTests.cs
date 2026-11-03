using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EdenClient.Net;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Phase 4-c : chemin « modern » d'<see cref="EdenConnection"/> —
/// handshake TLS 1.3 (SslStream) contre un mini-serveur de test
/// loopback, pinning SHA-256 (tls_thumbprint), accept-any dev
/// (tls_insecure) et rejet d'une empreinte erronée en
/// <see cref="AuthenticationException"/> typée pour l'UI.
/// </summary>
public class TlsConnectionTests
{
    /// <summary>Cert dev auto-signé « CN=eden-dev » (SAN localhost /
    /// 127.0.0.1 / ::1) — même recette que le
    /// TlsCertificateProvider serveur.</summary>
    private static X509Certificate2 NewDevCert()
    {
        var rsa = RSA.Create(2048); // la clé appartient au cert retourné
        var req = new CertificateRequest("CN=eden-dev", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(san.Build());
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
    }

    /// <summary>Mini-serveur TLS 1.3 : accepte une connexion,
    /// authentifie, puis exécute <paramref name="serve"/> sur le
    /// <see cref="SslStream"/> négocié (echo, etc.).</summary>
    private static async Task<TlsServer> StartTlsServerAsync(
        X509Certificate2 cert, Func<SslStream, Task> serve)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var task = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            var ssl = new SslStream(tcp.GetStream());
            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert,
                    EnabledSslProtocols = SslProtocols.Tls13,
                }, CancellationToken.None);
            try { await serve(ssl); }
            catch (Exception ex)
            {
                // Diag : sinon une faute du stub est masquée en EOF
                // opaque côté client.
                await Console.Error.WriteLineAsync(
                    $"[stub] {ex.GetType().Name} : {ex.Message}");
                throw;
            }
        });
        return new TlsServer(listener, port, task);
    }

    private sealed record TlsServer(TcpListener Listener, int Port,
        Task Task) : IDisposable
    {
        public void Dispose() => Listener.Stop();
    }

    /// <summary>Sert le gate de version moderne côté mini-serveur :
    /// lit la trame VersionHello du client
    /// (<c>[varint len][u16 0xFFFF][corps]</c>) puis renvoie
    /// VersionAck (<c>[varint len][u16 0xFFFE]['EDN1'][status=0]
    /// [str vide]</c>). Sans réponse, le handshake client attend
    /// 10 s puis ferme — les tests TLS doivent passer par là depuis
    /// l'introduction du gate de version.</summary>
    private static async Task ServeVersionAckAsync(SslStream ssl)
    {
        var hdr = new byte[VarintCodec.MaxEncodedLength];
        var got = 0;
        uint len;
        while (true)
        {
            got += await ssl.ReadAsync(hdr.AsMemory(got, 1));
            if (VarintCodec.TryReadVarint(hdr.AsSpan(0, got),
                    1024 * 1024, out len, out _))
                break;
        }
        var payload = new byte[len];
        var pgot = 0;
        while (pgot < payload.Length)
            pgot += await ssl.ReadAsync(payload.AsMemory(pgot));

        var body = new BinWriter();
        body.WriteUInt(VersionProtocol.Magic);
        body.WriteByte(0);          // status accepté
        body.WriteString("");       // serverHashHex vide
        var reply = new BinWriter();
        reply.WriteUShort(VersionProtocol.NcidAck);
        reply.WriteBytes(body.ToArray());
        var payload2 = reply.ToArray();
        var frame = new byte[VarintCodec.MaxEncodedLength +
            payload2.Length];
        var n = VarintCodec.WriteVarint(frame, (uint)payload2.Length);
        payload2.CopyTo(frame.AsSpan(n));
        await ssl.WriteAsync(frame.AsMemory(0, n + payload2.Length));
    }

    /// <summary>Pin correct (avec séparateurs « : » — normalisation)
    /// → handshake TLS 1.3 OK, trame echo aller-retour intacte,
    /// aucune clé RC4 installée.</summary>
    [Fact]
    public async Task ModernTransport_TlsPinned_EchoRoundTrip()
    {
        using var cert = NewDevCert();
        // Echo : renvoyer telle quelle la trame [varint len][payload].
        using var server = await StartTlsServerAsync(cert,
            async ssl =>
            {
                await ServeVersionAckAsync(ssl); // gate de version
                var buf = new byte[5]; // 04 | 2A 00 01 02
                var got = 0;
                while (got < buf.Length)
                    got += await ssl.ReadAsync(
                        buf.AsMemory(got, buf.Length - got));
                await ssl.WriteAsync(buf);
            });
        await using var conn = new EdenConnection
        {
            // Empreinte formatée « AA:BB:… » — le callback doit la
            // normaliser avant comparaison.
            TlsThumbprint = string.Join(":",
                cert.GetCertHashString(HashAlgorithmName.SHA256)
                    .Chunk(2).Select(c => new string(c))),
            // VersionCheck actif : ServeVersionAckAsync répond au
            // gate — le round-trip couvre donc aussi le handshake de
            // version post-TLS.
        };
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        await conn.ConnectAsync("127.0.0.1", server.Port, cts.Token);
        Assert.Null(conn.SessionKey); // pas de RC4 en modern

        await conn.SendPayloadAsync([0x2A, 0x00, 0x01, 0x02], cts.Token);
        var frame = await conn.ReadFrameAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(frame);
        Assert.Equal((ushort)0x002A, frame!.Ncid);
        Assert.Equal([0x2A, 0x00, 0x01, 0x02], frame.Payload);
        await server.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>Empreinte attendue ≠ cert présenté → le handshake
    /// échoue en <see cref="AuthenticationException"/> (typée — c'est
    /// ce que remontent les handlers d'erreur UI existants).</summary>
    [Fact]
    public async Task ModernTransport_PinMismatch_Throws()
    {
        using var cert = NewDevCert();
        using var server = await StartTlsServerAsync(cert,
            _ => Task.CompletedTask);
        await using var conn = new EdenConnection
        {
            TlsThumbprint = new string('0', 64), // SHA-256 hex ≠ cert
        };
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            conn.ConnectAsync("127.0.0.1", server.Port, cts.Token));
    }

    /// <summary><c>tls_insecure=1</c> : cert auto-signé accepté —
    /// chemin dev explicite (la prod passe par la chaîne standard qui
    /// refuserait ce cert).</summary>
    [Fact]
    public async Task ModernTransport_Insecure_AcceptsSelfSigned()
    {
        using var cert = NewDevCert();
        using var server = await StartTlsServerAsync(cert,
            _ => Task.CompletedTask);
        await using var conn = new EdenConnection
        {
            TlsInsecure = true,
            // Stub sans gate — sinon le handshake de version expire
            // en attendant une réponse qui ne viendra pas.
            VersionCheck = false,
        };
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        await conn.ConnectAsync("127.0.0.1", server.Port, cts.Token);
        Assert.True(conn.IsConnected);
    }

    /// <summary>Normalisation d'empreinte côté client : identique au
    /// helper serveur — « aa:bb », « AA BB », « AABB » équivalents.</summary>
    [Fact]
    public void Thumbprint_Normalizes()
    {
        Assert.Equal("AABBCC",
            EdenConnection.NormalizeThumbprint("aa:bb cc"));
        Assert.Equal("0123456789ABCDEF",
            EdenConnection.NormalizeThumbprint("01-23-45-67-89-ab-cd-ef"));
    }
}
