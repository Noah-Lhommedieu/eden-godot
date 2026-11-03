using System.Net.Sockets;
using Eden.Core.Networking.Session;
using Eden.Mission.Handlers;
using Eden.Ticket.Services;
using Xunit;

namespace Eden.Tests;

public class TicketMissionTests
{
    [Fact]
    public void Test_TicketManager_Issue_And_Validate_Success()
    {
        var manager = new TicketManager(TimeSpan.FromSeconds(30));
        var ticket = manager.Issue(id: 42, clientIp: 0x7F000001,
            serverIp: 0x7F000001, port: 5201);

        Assert.NotNull(ticket);
        Assert.Equal(22, ticket.ToBytes().Length);
        Assert.Equal(42u, ticket.Id);

        Assert.True(manager.ValidateAndConsume(ticket.ToBytes()));
    }

    [Fact]
    public void Test_TicketManager_ReplayAttack_Prevented()
    {
        var manager = new TicketManager(TimeSpan.FromSeconds(30));
        var ticket = manager.Issue(id: 99, clientIp: 0x7F000001,
            serverIp: 0x7F000001, port: 5201).ToBytes();

        // Premier claim -> Succès
        Assert.True(manager.ValidateAndConsume(ticket));

        // Deuxième claim (rejeu) -> Refusé
        Assert.False(manager.ValidateAndConsume(ticket));
    }

    [Fact]
    public void Test_TicketManager_ExpiredTicket_Rejected()
    {
        var manager = new TicketManager(TimeSpan.FromMilliseconds(10));
        var ticket = manager.Issue(id: 10, clientIp: 0x7F000001,
            serverIp: 0x7F000001, port: 5201).ToBytes();

        Thread.Sleep(50); // Attend expiration

        Assert.False(manager.ValidateAndConsume(ticket));
    }

    [Fact]
    public async Task Test_MissionPacketHandlers_Mailbox_Send_And_Receive()
    {
        var handlers = new MissionPacketHandlers();

        var mailId = await handlers.SendMailAsync(
            senderId: 1,
            senderName: "PlayerA",
            receiverId: 2,
            title: "Bienvenue sur Eden",
            content: "Voici ton cadeau de bienvenue !",
            gold: 500
        );

        Assert.True(mailId > 0);

        var inbox = await handlers.GetInboxAsync(receiverId: 2);
        Assert.Single(inbox);
        Assert.Equal("Bienvenue sur Eden", inbox[0].Title);
        Assert.Equal(500, inbox[0].AttachedGold);
    }

    [Fact]
    public async Task Test_MissionPacketHandlers_GuildBroadcast()
    {
        var handlers = new MissionPacketHandlers();

        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var clientTask = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var serverClient = await clientTask;
        listener.Stop();

        await using var session = new NetSession(serverClient)
        {
            AccountId = 1,
            CharacterId = 1,
            IsAuthenticated = true
        };

        handlers.SubscribeGuildChannel(guildId: 10, session);

        // Diffuse un message de guilde
        await handlers.BroadcastGuildMessageAsync(guildId: 10, senderName: "GuildMaster", message: "Rassemblement donjon à 21h !");
    }
}
