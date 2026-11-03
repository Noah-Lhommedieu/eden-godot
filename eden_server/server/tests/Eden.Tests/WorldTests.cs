using System.Net.Sockets;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Networking.Session;
using Eden.World.Handlers;
using Xunit;

namespace Eden.Tests;

public class FakeCharacterRepository : ICharacterRepository
{
    public List<PlayerCharacterEntity> Characters { get; } = new();

    public Task<IReadOnlyList<CharacterSummary>> GetCharacterSummariesByAccountIdAsync(int accountId)
    {
        var summaries = Characters
            .Where(c => c.AccountId == accountId)
            .Select(c => new CharacterSummary
            {
                CharacterId = c.Id,
                Name = c.GivenName,
                ClassId = c.ClassId,
                RaceId = c.RaceId,
                GenderId = c.GenderId,
                Level = c.Level,
                HairStyle = c.HairStyle,
                FaceStyle = c.FaceStyle,
                HairColor = c.HairColor,
                SkinColor = c.SkinColor,
                NodeId = c.NodeId
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<CharacterSummary>>(summaries);
    }

    public Task<PlayerCharacterEntity?> GetCharacterByIdAsync(int characterId)
    {
        return Task.FromResult(Characters.FirstOrDefault(c => c.Id == characterId));
    }

    public Task<PlayerCharacterEntity?> GetCharacterByNameAsync(string characterName)
    {
        return Task.FromResult(Characters.FirstOrDefault(c => string.Equals(c.GivenName, characterName, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<int> CreateCharacterAsync(PlayerCharacterEntity character)
    {
        var id = Characters.Count + 1;
        character.Id = id;
        Characters.Add(character);
        return Task.FromResult(id);
    }

    public Task<bool> DeleteCharacterAsync(int characterId, int accountId = 0)
    {
        var charEntity = Characters.FirstOrDefault(c => c.Id == characterId && (accountId == 0 || c.AccountId == accountId));
        if (charEntity != null)
        {
            Characters.Remove(charEntity);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task UpdateCoordinatesAsync(int characterId, int nodeId, double x, double y, double z, double dir)
    {
        var ch = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ch != null)
        {
            ch.NodeId = nodeId;
            ch.PositionX = x;
            ch.PositionY = y;
            ch.PositionZ = z;
            ch.FacingDirection = dir;
        }
        return Task.CompletedTask;
    }

    public Task UpdateStatsAsync(int characterId, int hp, int mp, long exp, int level)
    {
        var ch = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ch != null)
        {
            ch.CurrentHp = hp;
            ch.CurrentMp = mp;
            ch.Experience = exp;
            ch.Level = level;
        }
        return Task.CompletedTask;
    }

    public Task<long> GetGoldAsync(int characterId)
        => Task.FromResult((long)(Characters.FirstOrDefault(c => c.Id == characterId)?.Gold ?? 0));

    public Task SetSkillPointAsync(int characterId, int skillPoint)
        => Task.CompletedTask;

    public Task UpdateExpAsync(int characterId, long exp, int level)
    {
        var ch = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ch != null) { ch.Experience = exp; ch.Level = level; }
        return Task.CompletedTask;
    }

    public Task<long> GetExpAsync(int characterId)
        => Task.FromResult(Characters.FirstOrDefault(c => c.Id == characterId)?.Experience ?? 0L);

    public Task SetGoldAsync(int characterId, long gold)
    {
        var ch = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ch != null) ch.Gold = (int)gold;
        return Task.CompletedTask;
    }

    public Task<long> AddGoldAsync(int characterId, long amount)
    {
        var ch = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ch == null) return Task.FromResult(0L);
        ch.Gold = (int)Math.Clamp((long)ch.Gold + amount, 0, 999_999_999);
        return Task.FromResult((long)ch.Gold);
    }

    public Task<IReadOnlyList<(short Type, int Total)>> GetSpecialCoinsAsync(int characterId)
        => Task.FromResult<IReadOnlyList<(short, int)>>([]);

    public Task UpdateCharacterLocationAndStatsAsync(int characterId, int nodeId, double x, double y, double z, double dir, int hp, int mp)
    {
        var ch = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ch != null)
        {
            ch.NodeId = nodeId;
            ch.PositionX = x;
            ch.PositionY = y;
            ch.PositionZ = z;
            ch.FacingDirection = dir;
            ch.CurrentHp = hp;
            ch.CurrentMp = mp;
        }
        return Task.CompletedTask;
    }
}

public class WorldTests
{
    [Fact]
    public void Test_CharacterListResponse_Serialization()
    {
        var response = new CharacterListResponse
        {
            Characters = new List<CharacterSummary>
            {
                new()
                {
                    CharacterId = 1,
                    Name = "HeroWarrior",
                    ClassId = 1,
                    RaceId = 1,
                    GenderId = 1,
                    Level = 10,
                    HairStyle = 2,
                    FaceStyle = 3,
                    HairColor = 4,
                    SkinColor = 1,
                    NodeId = 5
                }
            }
        };

        var outStream = new OutBinStream();
        response.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var readResponse = new CharacterListResponse();
        readResponse.Deserialize(inStream);

        Assert.Single(readResponse.Characters);
        Assert.Equal("HeroWarrior", readResponse.Characters[0].Name);
        Assert.Equal(10, readResponse.Characters[0].Level);
    }

    [Fact]
    public void Test_ZoneTicketResponse_Serialization()
    {
        var ticket = new byte[22];
        for (int i = 0; i < 22; i++) ticket[i] = (byte)(i + 10);

        var response = new ZoneTicketResponse
        {
            ZoneServerIp = "192.168.1.100",
            ZoneServerPort = 5201,
            ZoneTicket = ticket
        };

        var outStream = new OutBinStream();
        response.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var readResponse = new ZoneTicketResponse();
        readResponse.Deserialize(inStream);

        Assert.Equal("192.168.1.100", readResponse.ZoneServerIp);
        Assert.Equal(5201, readResponse.ZoneServerPort);
        Assert.Equal(22, readResponse.ZoneTicket.Length);
        Assert.Equal(ticket, readResponse.ZoneTicket);
    }

    [Fact]
    public async Task Test_WorldPacketHandlers_CreateCharacter_And_List()
    {
        var charRepo = new FakeCharacterRepository();
        var accRepo = new FakeAccountRepository();
        var handlers = new WorldPacketHandlers(charRepo, accRepo, "127.0.0.1", 5201);

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
            IsAuthenticated = true
        };

        var createReq = new CreateCharacterRequest
        {
            GivenName = "SirLancelot",
            ClassId = 1,
            RaceId = 1,
            GenderId = 1,
            HairStyle = 1,
            FaceStyle = 1,
            HairColor = 1,
            SkinColor = 1
        };

        await handlers.HandleCreateCharacterRequestAsync(session, createReq, CancellationToken.None);

        Assert.Single(charRepo.Characters);
        Assert.Equal("SirLancelot", charRepo.Characters[0].GivenName);

        var list = await charRepo.GetCharacterSummariesByAccountIdAsync(1);
        Assert.Single(list);
        Assert.Equal("SirLancelot", list[0].Name);
    }

    [Fact]
    public async Task Test_WorldPacketHandlers_SelectCharacter_IssuesZoneTicket()
    {
        var charRepo = new FakeCharacterRepository();
        var accRepo = new FakeAccountRepository();
        var handlers = new WorldPacketHandlers(charRepo, accRepo, "10.0.0.1", 5201);

        var charId = await charRepo.CreateCharacterAsync(new PlayerCharacterEntity
        {
            AccountId = 2,
            GivenName = "MagePro",
            ClassId = 2,
            Level = 5,
            NodeId = 5
        });

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
            AccountId = 2,
            IsAuthenticated = true
        };

        var selectReq = new SelectCharacterRequest { CharacterId = charId };
        await handlers.HandleSelectCharacterRequestAsync(session, selectReq, CancellationToken.None);

        Assert.Equal(charId, session.CharacterId);
    }
}
