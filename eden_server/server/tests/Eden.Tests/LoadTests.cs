using System.Net;
using System.Net.Sockets;
using Eden.Core.Networking.Session;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Test de charge concurrence — le critère Phase 2 : un client qui ne
/// lit plus ne doit pas bloquer les autres sessions (backpressure
/// par session, files d'envoi indépendantes).
/// </summary>
public class LoadTests
{
    private static async Task<(NetSession session, Socket peer)>
        PairAsync(int sendBuffer = 0)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptSocketAsync();
        var client = new TcpClient();
        if (sendBuffer > 0) client.SendBufferSize = sendBuffer;
        await client.ConnectAsync(IPAddress.Loopback, port);
        var peer = await acceptTask;
        return (new NetSession(client), peer);
    }

    /// <summary>N sessions rapides + 1 session dont le peer ne lit
    /// jamais. Le buffer kernel de la lente est volontairement minime
    /// pour saturer sa file en ~256 trames. Prouve que les senders des
    /// autres sessions ne subissent aucun head-of-line blocking.</summary>
    [Fact]
    public async Task SlowClient_DoesNotBlockOtherSessions()
    {
        const int fast = 4;
        var fastSessions = new List<(NetSession s, Socket p)>();
        for (var i = 0; i < fast; i++)
            fastSessions.Add(await PairAsync());
        // Buffer d'envoi minuscule : le drain de la session lente se
        // bloque vite côté socket, sa file (256) se remplit, ses
        // producteurs se bloquent — les autres sessions doivent
        // continuer à débiter.
        var (slow, slowPeer) = await PairAsync(sendBuffer: 1024);

        // Saturation de la file de la session lente — le enqueue
        // finit par se bloquer (c'est le backpressure voulu).
        var slowSend = Task.Run(async () =>
        {
            for (var i = 0; i < 300; i++)
                await slow.SendPacketAsync((ushort)(600 + i),
                    new byte[64]);
        });

        // Les 4 sessions rapides débitent 20 trames chacune,
        // relues côté peer.
        var fastTasks = fastSessions.Select(async pair =>
        {
            var (s, p) = pair;
            for (var i = 0; i < 20; i++)
                await s.SendPacketAsync((ushort)(700 + i),
                    new byte[16]);
            // Relire exactement 20 trames (varint len + payload —
            // len=18 → 1 octet LEB128).
            var buf = new byte[20 * (1 + 2 + 16)];
            var got = 0;
            while (got < buf.Length)
            {
                var n = await p.ReceiveAsync(buf.AsMemory(got));
                if (n <= 0) break;
                got += n;
            }
            Assert.Equal(buf.Length, got);
        }).ToList();

        // Critère : les rapides terminent malgré la lente saturée.
        await Task.WhenAll(fastTasks)
            .WaitAsync(TimeSpan.FromSeconds(10));

        // La session lente est encore engorgée ou s'est fermée
        // proprement — jamais bloquante pour le reste.
        var slowDone = slowSend.IsCompleted;
        if (!slowDone)
            await slow.DisposeAsync();
        try { await slowSend.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch { /* file complétée à la fermeture */ }

        slowPeer.Dispose();
        foreach (var (s, p) in fastSessions)
        {
            await s.DisposeAsync();
            p.Dispose();
        }
    }
}
