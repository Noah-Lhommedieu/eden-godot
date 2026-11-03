using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Transport;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Incrément (a) phase 4 : l'abstraction <see cref="ITransport"/>
/// reproduit le wire lapis à l'identique — handshake RSA blob →
/// clé RC4 5 octets, trames [varint len][RC4 payload], variantes
/// serveur/client et mode clair ZM (encryptPayloads:false).
/// Vérifié sur paires de sockets loopback réelles + FakeTransport
/// in-memory pour NetSession(ITransport).
/// </summary>
public class TransportTests
{
    /// <summary>Paire de TcpClient connectés en loopback
    /// (listener → accept).</summary>
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

    /// <summary>Lit exactement <paramref name="n"/> octets du
    /// transport (timeout 5 s).</summary>
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

    /// <summary>Handshake serveur+client RSA/RC4 end-to-end : les deux
    /// transports convergent sur la même clé — l'aller et le retour
    /// chiffrés RC4 ressortent identiques au clair des deux côtés.</summary>
    [Fact]
    public async Task LegacyHandshake_ServerClient_Rc4RoundTrip()
    {
        var (client, serverTcp) = await SocketPairAsync();
        using var rsa = RSA.Create(2048);
        await using var server = new LegacyLapisTransport(serverTcp, rsa);
        await using var clientT = new LegacyLapisTransport(client,
            isServer: false);

        await Task.WhenAll(
            server.StartAsync(new DirectTransportContext(server),
                CancellationToken.None),
            clientT.StartAsync(new DirectTransportContext(clientT),
                CancellationToken.None));
        Assert.True(server.IsEncrypted);
        Assert.True(server.HasSendTransform);
        Assert.True(clientT.IsEncrypted);

        // client → serveur : payload transformé → TransformRecv rend le clair.
        var msg = new byte[] { 0x2A, 0x00, 1, 2, 3 };
        var wire = msg.ToArray();
        clientT.TransformSend(wire);
        Assert.NotEqual(msg, wire); // le fil est bien chiffré
        await clientT.WriteAsync(wire, CancellationToken.None);
        var got = await ReadExactAsync(server, msg.Length);
        Assert.Equal(wire, got); // l'octet sur le fil = le chiffré
        server.TransformRecv(got);
        Assert.Equal(msg, got);

        // serveur → client : même clé, sens inverse.
        var back = new byte[] { 9, 8, 7, 6 };
        var wire2 = back.ToArray();
        server.TransformSend(wire2);
        await server.WriteAsync(wire2, CancellationToken.None);
        var got2 = await ReadExactAsync(clientT, back.Length);
        clientT.TransformRecv(got2);
        Assert.Equal(back, got2);
    }

    /// <summary><c>encryptPayloads:false</c> (lien ZM) : le handshake
    /// RSA a lieu mais les transforms restent no-op — les trames
    /// passent en clair.</summary>
    [Fact]
    public async Task LegacyHandshake_PlainPayloads_TransformsNoOp()
    {
        var (client, serverTcp) = await SocketPairAsync();
        using var rsa = RSA.Create(2048);
        await using var server = new LegacyLapisTransport(serverTcp, rsa,
            encryptPayloads: false);
        await using var clientT = new LegacyLapisTransport(client,
            isServer: false, encryptPayloads: false);

        await Task.WhenAll(
            server.StartAsync(new DirectTransportContext(server),
                CancellationToken.None),
            clientT.StartAsync(new DirectTransportContext(clientT),
                CancellationToken.None));
        Assert.False(server.IsEncrypted);
        Assert.False(server.HasSendTransform);

        // TransformSend est un no-op : l'octet émis = l'octet clair.
        var msg = new byte[] { 1, 2, 3 };
        clientT.TransformSend(msg);
        Assert.Equal(new byte[] { 1, 2, 3 }, msg);
        await clientT.WriteAsync(msg, CancellationToken.None);
        var got = await ReadExactAsync(server, msg.Length);
        server.TransformRecv(got);
        Assert.Equal(new byte[] { 1, 2, 3 }, got);
    }

    /// <summary>NetSession sur LegacyLapisTransport :
    /// StartTransportAsync fait le handshake puis SendPacketAsync émet
    /// [varint len][RC4(u16 ncid + payload)] — déchiffré côté client par
    /// le transform du transport pair.</summary>
    [Fact]
    public async Task NetSession_OverLegacyTransport_EncryptedFrames()
    {
        var (client, serverTcp) = await SocketPairAsync();
        using var rsa = RSA.Create(2048);
        await using var session = new NetSession(
            new LegacyLapisTransport(serverTcp, rsa));
        await using var clientT = new LegacyLapisTransport(client,
            isServer: false);

        var serverStart = session.StartTransportAsync();
        await clientT.StartAsync(new DirectTransportContext(clientT),
            CancellationToken.None);
        Assert.True(await serverStart);
        Assert.True(session.CryptoReady);

        await session.SendPacketAsync(42, [1, 2]);
        // Préfixe varint lu octet par octet jusqu'au groupe sans
        // continuation (4 → 1 octet ici).
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
        clientT.TransformRecv(payload);
        Assert.Equal(new byte[] { 42, 0, 1, 2 }, payload);
    }

    /// <summary>Handshake côté serveur seul : un pair qui ne répond
    /// jamais est rejeté par le timeout — StartTransportAsync rend
    /// false avec CloseReason, sans exception.</summary>
    [Fact]
    public async Task NetSession_HandshakeTimeout_ReturnsFalse()
    {
        var (client, serverTcp) = await SocketPairAsync();
        using var rsa = RSA.Create(2048);
        await using var session = new NetSession(
            new LegacyLapisTransport(serverTcp, rsa,
                handshakeTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.False(await session.StartTransportAsync());
        Assert.Contains("handshake timeout", session.CloseReason);
        client.Dispose();
    }

    /// <summary>FakeTransport in-memory : prouve que le drain de
    /// NetSession applique TransformSend au payload (offset≥0) puis
    /// écrit via WriteAsync — sans socket.</summary>
    [Fact]
    public async Task NetSession_FakeTransport_DrainTransformsPayload()
    {
        var fake = new FakeTransport { HasSendTransform = true };
        await using var session = new NetSession(fake);
        await session.SendPacketAsync(42, [7]);

        // Attente du drain (Task.Run interne) — bornée.
        for (var i = 0; i < 500 && fake.Written.Count < 4; i++)
            await Task.Delay(10);
        // [varint len=3][payload 3B transformé XOR 0xAA] :
        // 42^0xAA=0x80, 0^0xAA=0xAA, 7^0xAA=0xAD — len en clair.
        Assert.Equal(new byte[] { 3, 0x80, 0xAA, 0xAD },
            fake.Written);
        Assert.Equal(1, fake.TransformSendCalls);
    }

    /// <summary>FakeTransport in-memory sans transform : la trame
    /// part en clair (ZONE_CRYPTO=0 / TcpTransport).</summary>
    [Fact]
    public async Task NetSession_FakeTransport_PlainWhenNoTransform()
    {
        var fake = new FakeTransport();
        await using var session = new NetSession(fake);
        await session.SendPacketAsync(42, []);
        for (var i = 0; i < 500 && fake.Written.Count < 3; i++)
            await Task.Delay(10);
        Assert.Equal(new byte[] { 2, 42, 0 }, fake.Written);
        Assert.Equal(0, fake.TransformSendCalls);
    }

    /// <summary>Transport in-memory minimal pour NetSession : file
    /// d'entrée FIFO + capture de tout ce que le drain écrit.</summary>
    private sealed class FakeTransport : ITransport
    {
        /// <summary>Octets écrits par le drain (concaténés).</summary>
        public List<byte> Written { get; } = [];
        /// <summary>Octets bruts servis aux ReadAsync.</summary>
        public Queue<byte> Inbound { get; } = new();
        public int TransformSendCalls;
        public bool Aborted;

        public string RemoteEndPoint => "fake";
        public bool IsEncrypted => HasSendTransform;
        public bool HasSendTransform { get; set; }

        public Task StartAsync(ITransportContext ctx,
            CancellationToken ct) => Task.CompletedTask;

        public ValueTask<int> ReadAsync(Memory<byte> b,
            CancellationToken ct)
        {
            var n = Math.Min(b.Length, Inbound.Count);
            for (var i = 0; i < n; i++) b.Span[i] = Inbound.Dequeue();
            return new ValueTask<int>(n);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> b,
            CancellationToken ct)
        {
            Written.AddRange(b.ToArray());
            return ValueTask.CompletedTask;
        }

        /// <summary>Transform XOR 0xAA déterministe — repérable dans
        /// les octets capturés.</summary>
        public void TransformSend(Span<byte> p)
        {
            TransformSendCalls++;
            for (var i = 0; i < p.Length; i++) p[i] ^= 0xAA;
        }

        public void TransformRecv(Span<byte> p) { }
        public void Abort() => Aborted = true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
