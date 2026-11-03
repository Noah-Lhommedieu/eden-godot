using System.Net;
using System.Net.Sockets;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// File d'envoi <see cref="NetSession"/> : un writer par session,
/// RC4 appliqué dans la boucle de drainage (ordre keystream = ordre
/// réel d'écriture), backpressure borné → fermeture sur client lent.
/// Vérifié sur socket loopback réel.
/// </summary>
public class SendQueueTests
{
    /// <summary>Accepte une connexion loopback, lit `expected` octets
    /// et les renvoie (RC4 déchiffré côté appelant).</summary>
    private static async Task<(NetSession session, Socket peer,
        Task<byte[]> readTask)> PairAsync(int expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptSocketAsync();
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var peer = await acceptTask;
        var session = new NetSession(client);
        var readTask = Task.Run(async () =>
        {
            var buf = new byte[expected];
            var got = 0;
            while (got < expected)
            {
                var n = await peer.ReceiveAsync(
                    buf.AsMemory(got));
                if (n <= 0) break;
                got += n;
            }
            return buf;
        });
        return (session, peer, readTask);
    }

    /// <summary>Les trames arrivent dans l'ordre d'enqueue et le RC4
    /// (stream continu) déchiffre chaque payload correctement — preuve
    /// que le chiffrement suit l'ordre réel du fil, pas l'ordre
    /// d'appel concurrent.</summary>
    [Fact]
    public async Task SendQueue_EncryptsInWireOrder()
    {
        // 3 paquets envoyés de tâches concurrentes — l'ordre concret
        // importe peu, ce qui compte : le keystream reste cohérent.
        var session2Key = new byte[] { 1, 2, 3, 4, 5 };
        var (session, peer, readTask) = await PairAsync(
            3 * (1 + 2 + 4)); // 3× [varint len][u16 ncid][i32]
        session.InstallTestCipher(new Rc4(session2Key));
        var expected = 3;
        for (var i = 0; i < expected; i++)
            await session.SendPacketAsync((ushort)(500 + i),
                BitConverter.GetBytes(i));

        var wire = await readTask.WaitAsync(TimeSpan.FromSeconds(5));
        var dec = new Rc4(session2Key);
        var bodies = new List<byte[]>();
        var off = 0;
        while (off < wire.Length)
        {
            Assert.True(VarintCodec.TryReadVarint(wire.AsSpan(off),
                uint.MaxValue, out var len, out var hdr));
            var payload = wire[(off + hdr)..(off + hdr + (int)len)];
            dec.Process(payload.AsSpan());
            bodies.Add(payload);
            off += hdr + (int)len;
        }
        Assert.Equal(expected, bodies.Count);
        // Les 3 NCIDs 500/501/502 ressortent déchiffrés (ordre quelconque
        // admis : la file est FIFO mais l'enqueue était séquentiel).
        var ncids = bodies.Select(b => b[0] | (b[1] << 8)).ToList();
        Assert.Equal(new[] { 500, 501, 502 }, ncids);
        Assert.Equal(0, BitConverter.ToInt32(bodies[0], 2));
        await session.DisposeAsync();
        peer.Dispose();
    }

    /// <summary>Sans crypto configurée, la trame part en clair
    /// (comportement ZONE_CRYPTO=0 préservé par la file).</summary>
    [Fact]
    public async Task SendQueue_Plaintext_WhenNoCipher()
    {
        var (session, peer, readTask) = await PairAsync(1 + 2);
        await session.SendPacketAsync(42, Array.Empty<byte>());
        var wire = await readTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 2, 42, 0 }, wire);
        await session.DisposeAsync();
        peer.Dispose();
    }

    /// <summary>
    /// Mode <c>DropOnFullQueue</c> (sessions zone) : file pleine → la
    /// trame est abandonnée immédiatement et la session fermée, jamais
    /// d'attente <see cref="NetSession.SendTimeout"/> — c'est le fix du
    /// livelock mesuré le 2026-10-26 (ZoneLoop figée ~6 min à envoyer
    /// des announces sight vers ~70 clients morts à file pleine).
    /// Le peer ne lit jamais et le buffer d'envoi est minuscule pour
    /// saturer la file de façon déterministe.
    /// </summary>
    [Fact]
    public async Task SendQueue_DropOnFullQueue_ClosesWithoutBlocking()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptSocketAsync();
        var client = new TcpClient
        {
            // Buffer noyau minuscule → le drain bloque vite sur la
            // socket et la file applicative se remplit de façon fiable.
            SendBufferSize = 2048,
        };
        await client.ConnectAsync(IPAddress.Loopback, port);
        var peer = await acceptTask; // jamais lu — simule un client mort
        var session = new NetSession(client) { DropOnFullQueue = true };

        var payload = new byte[4096];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dropAt = -1;
        for (var i = 0; i < NetSession.SendQueueCapacity + 64; i++)
        {
            await session.SendPacketAsync(500, payload);
            if (dropAt < 0 && session.CloseReason != null) dropAt = i;
        }
        sw.Stop();

        // La file a dû saturer (1,3 Mo de trames >> 4 Ko de buffer noyau
        // + 256 trames en file), et jamais une seule attente 5 s.
        Assert.True(dropAt >= 0, "la file d'envoi devait saturer");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4),
            $"envois bloquants malgré DropOnFullQueue ({sw.Elapsed})");
        Assert.Contains("backlog", session.CloseReason);

        // Un envoi post-saturation reste instantané — la boucle acteur
        // ne peut plus être parquée par un destinataire mort.
        var post = System.Diagnostics.Stopwatch.StartNew();
        await session.SendPacketAsync(501, payload);
        post.Stop();
        Assert.True(post.Elapsed < TimeSpan.FromMilliseconds(500),
            $"envoi post-drop trop long ({post.Elapsed})");

        await session.DisposeAsync();
        peer.Dispose();
    }
}
