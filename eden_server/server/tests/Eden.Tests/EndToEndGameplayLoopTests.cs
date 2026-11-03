using System.Net.Sockets;
using Eden.Core.Database.Entities;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Messages.Login;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Networking.Messages.Zone;
using Eden.Core.Networking.Session;
using Eden.Login.Handlers;
using Eden.Ticket.Services;
using Eden.World.Handlers;
using Eden.Zone.Handlers;
using Eden.Zone.Spatial;
using Xunit;

namespace Eden.Tests;

public class EndToEndGameplayLoopTests
{
    [Fact]
    public async Task Test_FullGameplayLoop_Authentication_To_ZoneMovement()
    {
        // 1. Initialisation des composants & dépôts
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 100,
                Username = "HeroPlayer",
                Password = "SecretPassword",
                WorldServerId = 1010
            }
        };

        var fakeWorldRepo = new FakeWorldRepository
        {
            Worlds = new List<WorldServerEntry>
            {
                new() { WorldId = 1010, WorldName = "EdenMain", IpAddress = "127.0.0.1", Port = 5200 }
            }
        };

        var fakeCharRepo = new FakeCharacterRepository();
        var ticketManager = new TicketManager(TimeSpan.FromMinutes(5));

        // 2. ÉTAPE 1: Authentification sur Eden.Login
        var loginHandlers = new LoginPacketHandlers(fakeAccountRepo, fakeWorldRepo, maxAttemptsPerMinute: int.MaxValue);

        var loginListener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        loginListener.Start();
        var loginPort = ((System.Net.IPEndPoint)loginListener.LocalEndpoint).Port;

        var loginClientTask = loginListener.AcceptTcpClientAsync();
        using var clientToLogin = new TcpClient();
        await clientToLogin.ConnectAsync(System.Net.IPAddress.Loopback, loginPort);
        using var loginSessionClient = await loginClientTask;
        loginListener.Stop();

        await using var loginSession = new NetSession(loginSessionClient);

        // Client envoie login request (layout natif : str username, str md5, str version)
        var loginReq = new CNC_CL_ServerLoginAccount { Field1 = "HeroPlayer", Field2 = "SecretPassword" };
        await loginHandlers.HandleLoginAccountAsync(loginSession, loginReq, CancellationToken.None);

        Assert.True(loginSession.IsAuthenticated);
        Assert.Equal(100, loginSession.AccountId);

        // Client sélectionne le monde
        var worldTicket = ticketManager.Issue(
            (uint)loginSession.AccountId!.Value,
            0x7F000001, 0x7F000001, 5200).ToBytes();
        Assert.Equal(22, worldTicket.Length);

        // 3. ÉTAPE 2: Connexion à Eden.World & Création/Sélection de Personnage
        var worldHandlers = new WorldPacketHandlers(fakeCharRepo, fakeAccountRepo, "127.0.0.1", 5201);

        var worldListener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        worldListener.Start();
        var worldPort = ((System.Net.IPEndPoint)worldListener.LocalEndpoint).Port;

        var worldClientTask = worldListener.AcceptTcpClientAsync();
        using var clientToWorld = new TcpClient();
        await clientToWorld.ConnectAsync(System.Net.IPAddress.Loopback, worldPort);
        using var worldSessionClient = await worldClientTask;
        worldListener.Stop();

        await using var worldSession = new NetSession(worldSessionClient);

        // Validation du ticket sur TicketServer
        Assert.True(ticketManager.ValidateAndConsume(worldTicket));
        var accId = (int)loginSession.AccountId!.Value;
        worldSession.AccountId = accId;
        worldSession.IsAuthenticated = true;

        // Création de personnage
        var createCharReq = new CreateCharacterRequest
        {
            GivenName = "Elysia",
            ClassId = 1, // Guerrier
            RaceId = 1,  // Humain
            GenderId = 2 // Femme
        };
        await worldHandlers.HandleCreateCharacterRequestAsync(worldSession, createCharReq, CancellationToken.None);

        var characters = await fakeCharRepo.GetCharacterSummariesByAccountIdAsync(accId);
        Assert.Single(characters);
        var hero = characters[0];
        Assert.Equal("Elysia", hero.Name);

        // Sélection du personnage -> Ticket de Zone émis
        var zoneTicket = ticketManager.Issue(
            (uint)hero.CharacterId,
            0x7F000001, 0x7F000001, 5201).ToBytes();
        Assert.Equal(22, zoneTicket.Length);

        // 4. ÉTAPE 3: Entrée dans Eden.Zone & Déplacement Spatial
        var zoneHandlers = new ZonePacketHandlers(fakeCharRepo, nodeId: 5);
        var spatialGrid = new SpatialGrid(cellSize: 50.0f);

        var zoneListener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        zoneListener.Start();
        var zonePort = ((System.Net.IPEndPoint)zoneListener.LocalEndpoint).Port;

        var zoneClientTask = zoneListener.AcceptTcpClientAsync();
        using var clientToZone = new TcpClient();
        await clientToZone.ConnectAsync(System.Net.IPAddress.Loopback, zonePort);
        using var zoneSessionClient = await zoneClientTask;
        zoneListener.Stop();

        await using var zoneSession = new NetSession(zoneSessionClient);

        // Validation du ticket zone
        Assert.True(ticketManager.ValidateAndConsume(zoneTicket));
        zoneSession.AccountId = accId;
        zoneSession.CharacterId = hero.CharacterId;
        zoneSession.IsAuthenticated = true;

        // Entrée en jeu et déplacement
        spatialGrid.UpdateEntityPosition(hero.CharacterId, "Elysia", 100.0f, 200.0f);
        await zoneHandlers.HandlePlayerMoveAsync(zoneSession, 100.0, 200.0, 10.0, 0.0, CancellationToken.None);

        var nearbyEntities = spatialGrid.GetNearbyEntityIds(100.0f, 200.0f);
        Assert.Contains(hero.CharacterId, nearbyEntities);

        // Vérification de la persistance finale
        var dbChar = await fakeCharRepo.GetCharacterByIdAsync(hero.CharacterId);
        Assert.NotNull(dbChar);
        Assert.Equal(100.0, dbChar.PositionX);
        Assert.Equal(200.0, dbChar.PositionY);
    }
}
