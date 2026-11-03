using System.Net.Sockets;
using Eden.Core.Database.Entities;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Zone;
using Eden.Core.Networking.Session;
using Eden.Zone.Combat;
using Eden.Zone.Handlers;
using Eden.Zone.Spatial;
using Xunit;

namespace Eden.Tests;

public class ZoneTests
{
    [Fact]
    public void Test_ZoneMoveMessage_Serialization()
    {
        var msg = new ZoneMoveMessage
        {
            EntityId = 1001,
            PositionX = 123.45f,
            PositionY = 67.89f,
            PositionZ = 10.0f,
            FacingDirection = 1.57f,
            MoveSpeed = 6.0f
        };

        var outStream = new OutBinStream();
        msg.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var readMsg = new ZoneMoveMessage();
        readMsg.Deserialize(inStream);

        Assert.Equal(1001, readMsg.EntityId);
        Assert.Equal(123.45f, readMsg.PositionX, 2);
        Assert.Equal(67.89f, readMsg.PositionY, 2);
        Assert.Equal(10.0f, readMsg.PositionZ, 2);
    }

    [Fact]
    public void Test_ZoneDamageMessage_Serialization()
    {
        var msg = new ZoneDamageMessage
        {
            AttackerEntityId = 10,
            TargetEntityId = 20,
            DamageAmount = 250,
            IsCritical = true,
            IsDodged = false,
            RemainingHp = 750
        };

        var outStream = new OutBinStream();
        msg.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var readMsg = new ZoneDamageMessage();
        readMsg.Deserialize(inStream);

        Assert.Equal(10, readMsg.AttackerEntityId);
        Assert.Equal(20, readMsg.TargetEntityId);
        Assert.Equal(250, readMsg.DamageAmount);
        Assert.True(readMsg.IsCritical);
        Assert.False(readMsg.IsDodged);
        Assert.Equal(750, readMsg.RemainingHp);
    }

    [Fact]
    public void Test_SpatialGrid_AOI_Visibility()
    {
        var grid = new SpatialGrid(cellSize: 50.0f);

        // Joueur 1 à (10, 10) -> Cell (0, 0)
        grid.UpdateEntityPosition(1, "Player1", 10.0f, 10.0f);

        // Joueur 2 à (40, 40) -> Cell (0, 0) (même cellule)
        grid.UpdateEntityPosition(2, "Player2", 40.0f, 40.0f);

        // Joueur 3 à (80, 80) -> Cell (1, 1) (cellule voisine)
        grid.UpdateEntityPosition(3, "Player3", 80.0f, 80.0f);

        // Joueur 4 à (500, 500) -> Cell (10, 10) (lointaine)
        grid.UpdateEntityPosition(4, "Player4", 500.0f, 500.0f);

        var nearby = grid.GetNearbyEntityIds(10.0f, 10.0f);

        Assert.Contains(1, nearby);
        Assert.Contains(2, nearby);
        Assert.Contains(3, nearby);
        Assert.DoesNotContain(4, nearby);
    }

    [Fact]
    public void Test_CombatEngine_Damage_Calculation()
    {
        var fakeRng = new Random(42);
        var result = CombatEngine.CalculateDamage(
            attackerAttack: 100,
            targetDefense: 40,
            targetCurrentHp: 500,
            critRate: 0.0,
            dodgeRate: 0.0,
            rng: fakeRng
        );

        Assert.True(result.Damage > 0);
        Assert.False(result.IsDodged);
        Assert.Equal(500 - result.Damage, result.NewHp);
    }

    [Fact]
    public void Test_CombatEngine_ExperienceReward()
    {
        var expSameLevel = CombatEngine.CalculateExperienceReward(10, 10);
        Assert.Equal(1000L, expSameLevel);

        var expHigherLevel = CombatEngine.CalculateExperienceReward(12, 10);
        Assert.Equal(1240L, expHigherLevel);
    }

    [Fact]
    public async Task Test_ZonePacketHandlers_PlayerMove_PersistsCoordinates()
    {
        var charRepo = new FakeCharacterRepository();
        var charId = await charRepo.CreateCharacterAsync(new PlayerCharacterEntity
        {
            AccountId = 1,
            GivenName = "Adventurer",
            NodeId = 5,
            PositionX = 0,
            PositionY = 0,
            PositionZ = 0
        });

        var handlers = new ZonePacketHandlers(charRepo, nodeId: 5);

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
            CharacterId = charId,
            IsAuthenticated = true
        };

        await handlers.HandlePlayerMoveAsync(session, 150.5, 220.0, 10.0, 3.14, CancellationToken.None);

        var updated = await charRepo.GetCharacterByIdAsync(charId);
        Assert.NotNull(updated);
        Assert.Equal(150.5, updated.PositionX);
        Assert.Equal(220.0, updated.PositionY);
        Assert.Equal(10.0, updated.PositionZ);
    }
}
