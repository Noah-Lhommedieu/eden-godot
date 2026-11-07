using System.Net;
using System.Net.Sockets;
using Eden.Core.Networking.Session;

namespace Eden.Tests;

/// <summary>Envoi cosmétique non bloquant (lot N) : un témoin qui ne lit plus perd des déplacements mais n'est ni bloqué ni déconnecté.</summary>
public class CosmeticSendTests
{
    [Fact]
    public async Task SlowWitness_DropsCosmeticPackets_WithoutClosingOrBlocking()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        using var peer = new TcpClient(); await peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);   // ne lira jamais
        using var server = await accept; listener.Stop();
        await using var session = new NetSession(server);

        var payload = new byte[1000]; var accepted = 0; var dropped = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20_000; i++) { if (session.TrySendCosmeticPacket(326, payload)) accepted++; else dropped++; }

        Assert.True(sw.ElapsedMilliseconds < 2000, "l'envoi cosmétique ne doit jamais attendre");
        Assert.True(dropped > 0, "la file doit saturer et abandonner des mises à jour");
        Assert.True(accepted > 0);
        Assert.Null(session.CloseReason);
        Assert.True(session.QueuePressure <= 1.0);
    }

    [Fact]
    public async Task IdleSession_AcceptsCosmeticPackets()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        using var peer = new TcpClient(); await peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var server = await accept; listener.Stop();
        await using var session = new NetSession(server);
        Assert.True(session.TrySendCosmeticPacket(326, new byte[15]));
    }
}
