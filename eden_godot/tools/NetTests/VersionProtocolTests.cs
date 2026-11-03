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
/// Phase 4-d : handshake de version post-TLS côté client —
/// construction octet-par-octet du VersionHello (0xFFFF), décodage
/// des réponses 0xFFFE/0xFFFD, <see cref="UpdateRequiredException"/>
/// typée sur rejet, repli tolérant vers un serveur sans gate, et
/// déterminisme de <see cref="ManifestHash"/>.
/// </summary>
public class VersionProtocolTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(),
        $"eden-client-manifest-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    // ------------------------------------------------------------------
    // Codec pur
    // ------------------------------------------------------------------

    /// <summary>La trame hello produit exactement les octets du plan :
    /// [u16 0xFFFF LE][« EDN1 »][01 00][str version][str hash] —
    /// str = u16 len LE + octets.</summary>
    [Fact]
    public void HelloFrame_ExactWireBytes()
    {
        var frame = VersionProtocol.BuildHelloFrame("002.003.05.67",
            "aabbcc");
        var expected = new byte[]
        {
            0xFF, 0xFF,                   // NCID 0xFFFF LE
            0x45, 0x44, 0x4E, 0x31,       // « EDN1 »
            0x01, 0x00,                   // proto 1.0
            0x0D, 0x00,                   // str len 13
            0x30, 0x30, 0x32, 0x2E, 0x30, 0x30, 0x33, 0x2E, 0x30,
            0x35, 0x2E, 0x36, 0x37,       // « 002.003.05.67 »
            0x06, 0x00,                   // str len 6
            0x61, 0x61, 0x62, 0x62, 0x63, 0x63, // « aabbcc »
        };
        Assert.Equal(expected, frame);
    }

    /// <summary>Ack 0xFFFE : status 0 + hash serveur → Accepted.</summary>
    [Fact]
    public void ParseReply_Ack()
    {
        var w = new BinWriter();
        w.WriteUShort(VersionProtocol.NcidAck);
        w.WriteUInt(VersionProtocol.Magic);
        w.WriteByte(0);
        w.WriteString("deadbeef");
        var reply = VersionProtocol.ParseReply(w.ToArray());
        Assert.NotNull(reply);
        Assert.True(reply!.Accepted);
        Assert.Equal("deadbeef", reply.ServerManifestHashHex);
    }

    /// <summary>Reject 0xFFFD : reason + hash + version minimale →
    /// tous les champs décodés.</summary>
    [Fact]
    public void ParseReply_Reject()
    {
        var w = new BinWriter();
        w.WriteUShort(VersionProtocol.NcidReject);
        w.WriteUInt(VersionProtocol.Magic);
        w.WriteByte((byte)VersionProtocol.RejectReason.UpdateRequired);
        w.WriteString("cafe");
        w.WriteString("003.000.00.00");
        var reply = VersionProtocol.ParseReply(w.ToArray());
        Assert.NotNull(reply);
        Assert.False(reply!.Accepted);
        Assert.Equal(VersionProtocol.RejectReason.UpdateRequired,
            reply.Reason);
        Assert.Equal("cafe", reply.ServerManifestHashHex);
        Assert.Equal("003.000.00.00", reply.MinClientVersion);
    }

    /// <summary>Trame d'un autre NCID (ex. ClientHello) → null :
    /// l'appelant la traite comme un paquet métier.</summary>
    [Fact]
    public void ParseReply_NonVersionNcid_ReturnsNull() =>
        Assert.Null(VersionProtocol.ParseReply([0x03, 0x00, 0x00]));

    /// <summary>NCID de version mais magic faux →
    /// <see cref="InvalidDataException"/> (désync — jamais admis).</summary>
    [Fact]
    public void ParseReply_BadMagic_Throws()
    {
        var w = new BinWriter();
        w.WriteUShort(VersionProtocol.NcidAck);
        w.WriteUInt(0xDEADBEEF);
        w.WriteByte(0);
        Assert.Throws<InvalidDataException>(
            () => VersionProtocol.ParseReply(w.ToArray()));
    }

    // ------------------------------------------------------------------
    // Aller-retour TLS réel avec un stub gate
    // ------------------------------------------------------------------

    private static X509Certificate2 NewDevCert()
    {
        var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=eden-dev", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
    }

    private static async Task<(int port, Task task)> StubGateAsync(
        Func<SslStream, Task> serve)
    {
        var cert = NewDevCert();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var task = Task.Run(async () =>
        {
            using (listener)
            {
                using var tcp = await listener.AcceptTcpClientAsync();
                var ssl = new SslStream(tcp.GetStream());
                await ssl.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
                        EnabledSslProtocols = SslProtocols.Tls13,
                    });
                await serve(ssl);
            }
        });
        return (port, task);
    }

    /// <summary>Lit UNE trame [varint len][payload] du stream TLS.</summary>
    private static async Task<byte[]> ReadFrameAsync(SslStream ssl)
    {
        var lenBuf = new byte[VarintCodec.MaxEncodedLength];
        var got = 0;
        uint len;
        while (true)
        {
            var n = await ssl.ReadAsync(lenBuf.AsMemory(got, 1));
            if (n == 0) throw new EndOfStreamException();
            got += n;
            if (VarintCodec.TryReadVarint(lenBuf.AsSpan(0, got),
                    uint.MaxValue, out len, out _))
                break;
        }
        var payload = new byte[len];
        var read = 0;
        while (read < (int)len)
            read += await ssl.ReadAsync(
                payload.AsMemory(read, (int)len - read));
        return payload;
    }

    private static Task WriteFrameAsync(SslStream ssl, byte[] payload)
    {
        var frame = new byte[VarintCodec.MaxEncodedLength +
            payload.Length];
        var hdrLen = VarintCodec.WriteVarint(frame, (uint)payload.Length);
        payload.CopyTo(frame, hdrLen);
        return ssl.WriteAsync(frame.AsMemory(0,
            hdrLen + payload.Length)).AsTask();
    }

    /// <summary>Le stub lit le hello, le valide (magic + NCID) puis
    /// répond Ack → <see cref="EdenConnection.ConnectAsync"/> réussit
    /// et la connexion reste utilisable.</summary>
    [Fact]
    public async Task VersionHandshake_Ack_ConnectSucceeds()
    {
        var seenHello = new TaskCompletionSource<byte[]>();
        var (port, server) = await StubGateAsync(async ssl =>
        {
            var hello = await ReadFrameAsync(ssl);
            seenHello.SetResult(hello);
            var w = new BinWriter();
            w.WriteUShort(VersionProtocol.NcidAck);
            w.WriteUInt(VersionProtocol.Magic);
            w.WriteByte(0);
            w.WriteString("0123");
            await WriteFrameAsync(ssl, w.ToArray());
            // Laisser la connexion ouverte le temps que le client
            // fasse ses asserts puis dispose (200 ms >> assertions).
            await Task.Delay(200);
        });

        await using var conn = new EdenConnection { TlsInsecure = true };
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        await conn.ConnectAsync("127.0.0.1", port, cts.Token);
        Assert.True(conn.IsConnected);

        var hello = await seenHello.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        // Le stub a vu le VersionHello complet en première trame.
        Assert.Equal(0xFFFF, hello[0] | (hello[1] << 8));
        Assert.Equal(new byte[] { 0x45, 0x44, 0x4E, 0x31 },
            hello[2..6]);
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Le stub répond VersionReject(UpdateRequired, min
    /// « 003.0 ») → <see cref="ConnectAsync"/> lève
    /// <see cref="UpdateRequiredException"/> portant la borne et le
    /// hash serveur — exactement ce que l'UI affiche.</summary>
    [Fact]
    public async Task VersionHandshake_Reject_ThrowsUpdateRequired()
    {
        var (port, server) = await StubGateAsync(async ssl =>
        {
            await ReadFrameAsync(ssl); // hello — ignoré
            var w = new BinWriter();
            w.WriteUShort(VersionProtocol.NcidReject);
            w.WriteUInt(VersionProtocol.Magic);
            w.WriteByte((byte)
                VersionProtocol.RejectReason.UpdateRequired);
            w.WriteString("aabb");
            w.WriteString("003.000.00.00");
            await WriteFrameAsync(ssl, w.ToArray());
        });

        await using var conn = new EdenConnection { TlsInsecure = true };
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        var ex = await Assert.ThrowsAsync<UpdateRequiredException>(
            () => conn.ConnectAsync("127.0.0.1", port, cts.Token));
        Assert.Equal("003.000.00.00", ex.MinClientVersion);
        Assert.Equal("aabb", ex.ServerManifestHash);
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Serveur sans gate (pousse directement une trame métier
    /// en premier) → la trame est repoussée dans le canal et la
    /// connexion continue — repli tolérant.</summary>
    [Fact]
    public async Task VersionHandshake_NoGate_ForwardsFirstFrame()
    {
        var (port, server) = await StubGateAsync(async ssl =>
        {
            // « Serveur » naïf : il lit le hello sans le comprendre
            // puis pousse sa trame métier (NCID 0x0003, corps 2 oct.).
            await ReadFrameAsync(ssl);
            await WriteFrameAsync(ssl, [0x03, 0x00, 0xAA, 0xBB]);
            await Task.Delay(50);
        });

        await using var conn = new EdenConnection { TlsInsecure = true };
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        await conn.ConnectAsync("127.0.0.1", port, cts.Token);
        var frame = await conn.ReadFrameAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        Assert.Equal((ushort)0x0003, frame!.Ncid);
        Assert.Equal([0x03, 0x00, 0xAA, 0xBB], frame.Payload);
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary><c>VersionCheck=false</c> : aucune trame de version
    /// n'est émise — la première trame du client est son paquet
    /// métier.</summary>
    [Fact]
    public async Task VersionCheck_Disabled_NoHelloSent()
    {
        var first = new TaskCompletionSource<byte[]>();
        var (port, server) = await StubGateAsync(async ssl =>
        {
            first.SetResult(await ReadFrameAsync(ssl));
        });

        await using var conn = new EdenConnection
        {
            TlsInsecure = true,
            VersionCheck = false,
        };
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        await conn.ConnectAsync("127.0.0.1", port, cts.Token);
        await conn.SendPayloadAsync([0x2A, 0x00, 0x01], cts.Token);
        var frame = await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([0x2A, 0x00, 0x01], frame);
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ------------------------------------------------------------------
    // ManifestHash
    // ------------------------------------------------------------------

    private void WriteIni(string rel, string content)
    {
        var path = Path.Combine(_tmpDir,
            rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Deux calculs du même corpus → même hex (tri Ordinal
    /// indépendant de l'ordre d'énumération FS).</summary>
    [Fact]
    public void ManifestHash_Deterministic()
    {
        WriteIni("C_Item.ini", "1|sword|");
        WriteIni("sub/C_Spell.ini", "5|fire|");
        var a = ManifestHash.Compute(_tmpDir)!;
        var b = ManifestHash.Compute(_tmpDir)!;
        Assert.Equal(a.Hex, b.Hex);
        Assert.Equal(2, a.FileCount);
        Assert.Equal(64, a.Hex.Length);
    }

    /// <summary>Corpus modifié → empreinte différente.</summary>
    [Fact]
    public void ManifestHash_ContentChange_DifferentHash()
    {
        WriteIni("C_Item.ini", "1|sword|");
        var a = ManifestHash.Compute(_tmpDir)!.Hex;
        WriteIni("C_Item.ini", "1|axe|");
        Assert.NotEqual(a, ManifestHash.Compute(_tmpDir)!.Hex);
    }

    /// <summary>Répertoire absent/vide → null (hash vide dans le
    /// hello — le serveur non-strict l'admet).</summary>
    [Fact]
    public void ManifestHash_MissingDir_Null()
    {
        Assert.Null(ManifestHash.Compute(
            Path.Combine(_tmpDir, "nope")));
        Directory.CreateDirectory(_tmpDir);
        WriteIni("readme.txt", "x");
        Assert.Null(ManifestHash.Compute(_tmpDir));
    }
}
