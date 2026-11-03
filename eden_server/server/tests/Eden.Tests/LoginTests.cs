using System.Net.Sockets;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Messages.Login;
using Eden.Core.Networking.Session;
using Eden.Core.Security;
using Eden.Login.Handlers;
using Xunit;

namespace Eden.Tests;

public class FakeAccountRepository : IAccountRepository
{
    public Account? CurrentAccount { get; set; }

    /// <summary>UPDATEs <c>password_hash</c> observés (rehash-at-login) :
    /// (accountId, PHC écrit).</summary>
    public List<(int AccountId, string PasswordHash)> PasswordHashWrites { get; } = new();

    public Task<Account?> GetAccountByUsernameAsync(string username)
    {
        if (CurrentAccount != null && string.Equals(CurrentAccount.Username, username, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Account?>(CurrentAccount);
        return Task.FromResult<Account?>(null);
    }

    public Task<Account?> GetAccountByIdAsync(int accountId)
    {
        if (CurrentAccount != null && CurrentAccount.Id == accountId)
            return Task.FromResult<Account?>(CurrentAccount);
        return Task.FromResult<Account?>(null);
    }

    public Task<int> CreateAccountAsync(string username, string password, string realName = "")
    {
        return Task.FromResult(1);
    }

    public Task SaveLastWorldAsync(int accountId, short worldId)
    {
        return Task.CompletedTask;
    }

    public Task UpdateAccountStateAsync(int accountId, int state)
    {
        return Task.CompletedTask;
    }

    public Task UpdatePasswordHashAsync(int accountId, string passwordHash)
    {
        PasswordHashWrites.Add((accountId, passwordHash));
        if (CurrentAccount?.Id == accountId)
            CurrentAccount.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }
}

public class FakeWorldRepository : IWorldRepository
{
    public List<WorldServerEntry> Worlds { get; set; } = new();

    public Task<IReadOnlyList<WorldServerEntity>> GetAllWorldsAsync()
    {
        IReadOnlyList<WorldServerEntity> list = new List<WorldServerEntity>();
        return Task.FromResult(list);
    }

    public Task<WorldServerEntity?> GetWorldByIdAsync(int worldId)
    {
        return Task.FromResult<WorldServerEntity?>(null);
    }

    public Task<List<WorldServerEntry>> GetWorldStatesForAccountAsync(int accountId)
    {
        return Task.FromResult(Worlds);
    }

    public Task UpdateWorldPopulationAsync(int worldId, int onlineUsers)
    {
        return Task.CompletedTask;
    }
}

public class LoginTests
{
    // ------------------------------------------------------------------
    // Round-trip des messages CNC_CL_* réels (layout natif LoginServer)
    // ------------------------------------------------------------------

    [Fact]
    public void Test_ClientHello_Serialization()
    {
        var hello = new CNC_CL_ClientHello { Value = 9.9f };
        var outStream = new OutBinStream();
        hello.Serialize(outStream);

        var readHello = new CNC_CL_ClientHello();
        readHello.Deserialize(new InBinStream(outStream.ToArray()));

        Assert.Equal(3, hello.Opcode);
        Assert.Equal(9.9f, readHello.Value);
    }

    [Fact]
    public void Test_ServerLoginAccount_Serialization()
    {
        var request = new CNC_CL_ServerLoginAccount
        {
            Field1 = "TestPlayer",
            Field2 = "SecretPassword123",
            Field3 = "9.9"
        };

        var outStream = new OutBinStream();
        request.Serialize(outStream);

        var readRequest = new CNC_CL_ServerLoginAccount();
        readRequest.Deserialize(new InBinStream(outStream.ToArray()));

        Assert.Equal(1, request.Opcode);
        Assert.Equal("TestPlayer", readRequest.Field1);
        Assert.Equal("SecretPassword123", readRequest.Field2);
        Assert.Equal("9.9", readRequest.Field3);
    }

    [Fact]
    public void Test_ClientLoginFail_Serialization()
    {
        var fail = new CNC_CL_ClientLoginFail { Value = 2 };
        var outStream = new OutBinStream();
        fail.Serialize(outStream);

        var received = new CNC_CL_ClientLoginFail();
        received.Deserialize(new InBinStream(outStream.ToArray()));

        Assert.Equal(4, fail.Opcode);
        Assert.Equal(2, received.Value);
    }

    [Fact]
    public void Test_ClientGetWorldServers_LoopSerialization()
    {
        var response = new CNC_CL_ClientGetWorldServers { LastWorldId = 1010 };
        response.Worlds.Add(new CNC_CL_ClientGetWorldServers.Entry
        {
            Id = 1010,
            Name = "Ch01",
            Ip = "127.0.0.1",
            Port = 5200,
            Online = 42,
            MaxNum = 150,
            State = 1,
            ShowOrder = 1,
            Flags = 0,
            CharacterCount = 2,
        });
        response.Worlds.Add(new CNC_CL_ClientGetWorldServers.Entry
        {
            Id = 1020,
            Name = "Ch02",
            Ip = "127.0.0.1",
            Port = 5201,
            Online = 0,
            MaxNum = 300,
            State = 0,
            ShowOrder = 0,
            Flags = 0,
            CharacterCount = 0,
        });

        var outStream = new OutBinStream();
        response.Serialize(outStream);

        var read = new CNC_CL_ClientGetWorldServers();
        read.Deserialize(new InBinStream(outStream.ToArray()));

        Assert.Equal(5, response.Opcode);
        Assert.Equal(2, read.Worlds.Count);
        Assert.Equal("Ch01", read.Worlds[0].Name);
        Assert.Equal(42, read.Worlds[0].Online);
        Assert.Equal((short)150, read.Worlds[0].MaxNum);
        Assert.Equal((short)1020, read.Worlds[1].Id);
        Assert.Equal((short)1010, read.LastWorldId);
    }

    [Fact]
    public void Test_ClientReceiveTicket_22Bytes_Serialization()
    {
        var ticketBytes = new byte[22];
        for (var i = 0; i < 22; i++) ticketBytes[i] = (byte)(i + 1);

        var response = new CNC_CL_ClientReceiveTicketToWorldServer { Ticket = ticketBytes };
        var outStream = new OutBinStream();
        response.Serialize(outStream);

        // Le ticket tient exactement 22 octets bruts, sans préfixe de longueur.
        Assert.Equal(22, outStream.ToArray().Length);

        var received = new CNC_CL_ClientReceiveTicketToWorldServer();
        received.Deserialize(new InBinStream(outStream.ToArray()));

        Assert.Equal(6, response.Opcode);
        Assert.Equal(ticketBytes, received.Ticket);
    }

    // ------------------------------------------------------------------
    // Handlers du POC login
    // ------------------------------------------------------------------

    [Fact]
    public async Task Test_LoginAccountHandler_Authentication_Success()
    {
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 10,
                Username = "player1",
                Password = "validpassword",
                WorldServerId = 1010
            }
        };

        var fakeWorldRepo = new FakeWorldRepository
        {
            Worlds = new List<WorldServerEntry>
            {
                new() { WorldId = 1010, WorldName = "World1", IpAddress = "127.0.0.1", Port = 5200 }
            }
        };

        var handlers = new LoginPacketHandlers(fakeAccountRepo, fakeWorldRepo, maxAttemptsPerMinute: int.MaxValue);

        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var clientTask = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var serverClient = await clientTask;
        listener.Stop();

        await using var session = new NetSession(serverClient);

        var loginReq = new CNC_CL_ServerLoginAccount
        {
            Field1 = "player1",
            Field2 = "validpassword"
        };

        await handlers.HandleLoginAccountAsync(session, loginReq, CancellationToken.None);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(10, session.AccountId);
        Assert.Equal("player1", session.Username);
    }

    [Fact]
    public async Task Test_LoginAccountHandler_Authentication_Md5Password()
    {
        // Le client natif envoie md5(password) — vérifié via
        // account_login() / tb_user.pwd dans le dump PostgreSQL.
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 2,
                Username = "admin",
                Password = "adminmdp",
                WorldServerId = 1010
            }
        };

        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);

        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var clientTask = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var serverClient = await clientTask;
        listener.Stop();

        await using var session = new NetSession(serverClient);

        var loginReq = new CNC_CL_ServerLoginAccount
        {
            Field1 = "admin",
            Field2 = "17c725af38777cf3ac4bf34e39f44caa" // md5('adminmdp')
        };

        await handlers.HandleLoginAccountAsync(session, loginReq, CancellationToken.None);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(2, session.AccountId);
    }

    [Fact]
    public async Task Test_LoginAccountHandler_Authentication_Failure()
    {
        var handlers = new LoginPacketHandlers(new FakeAccountRepository(), new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);

        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var clientTask = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var serverClient = await clientTask;
        listener.Stop();

        await using var session = new NetSession(serverClient);

        var loginReq = new CNC_CL_ServerLoginAccount
        {
            Field1 = "nobody",
            Field2 = "wrong"
        };

        await handlers.HandleLoginAccountAsync(session, loginReq, CancellationToken.None);

        Assert.False(session.IsAuthenticated);
        Assert.Null(session.AccountId);
    }

    // ------------------------------------------------------------------
    // Argon2id — PHC verify, rehash-at-login, transition legacy
    // ------------------------------------------------------------------

    /// <summary>Paire TCP loopback → NetSession côté serveur (même motif
    /// que les tests ci-dessus, factorisé). L'appelant dispose les deux.</summary>
    private static async Task<(NetSession Session, TcpClient Client)> NewSessionAsync()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var accept = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        var serverClient = await accept;
        listener.Stop();
        return (new NetSession(serverClient), client);
    }

    [Fact]
    public async Task Test_LoginAccountHandler_Argon2Hash_Success()
    {
        // Compte migré : password_hash = PHC argon2id du credential wire
        // md5('adminmdp') — la forme envoyée par le client Godot.
        var wire = PasswordHasher.Md5Hex("adminmdp");
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 2,
                Username = "admin",
                Password = "adminmdp",
                PasswordHash = PasswordHasher.HashWireCredential(wire),
                WorldServerId = 1010
            }
        };
        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);
        var (session, client) = await NewSessionAsync();
        await using var _s = session;
        using var _c = client;

        await handlers.HandleLoginAccountAsync(session,
            new CNC_CL_ServerLoginAccount { Field1 = "admin", Field2 = wire },
            CancellationToken.None);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(2, session.AccountId);
        // Déjà argon2id → aucun rehash déclenché.
        Assert.Empty(fakeAccountRepo.PasswordHashWrites);
    }

    [Fact]
    public async Task Test_LoginAccountHandler_LegacyMd5_TriggersRehash()
    {
        // Legacy : password stocké plaintext, login envoie md5 — succès
        // legacy puis UPDATE password_hash (rehash-at-login).
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 2,
                Username = "admin",
                Password = "adminmdp",
                WorldServerId = 1010
            }
        };
        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);
        var (session, client) = await NewSessionAsync();
        await using var _s = session;
        using var _c = client;

        await handlers.HandleLoginAccountAsync(session,
            new CNC_CL_ServerLoginAccount
            {
                Field1 = "admin",
                Field2 = "17c725af38777cf3ac4bf34e39f44caa" // md5('adminmdp')
            }, CancellationToken.None);

        Assert.True(session.IsAuthenticated);
        var write = Assert.Single(fakeAccountRepo.PasswordHashWrites);
        Assert.Equal(2, write.AccountId);
        Assert.True(PasswordHasher.IsArgon2Phc(write.PasswordHash));
        Assert.Contains("m=65536,t=3,p=1", write.PasswordHash);
        // Le PHC écrit vérifie le credential wire — le prochain login
        // passera par Verify.
        Assert.True(PasswordHasher.Verify(
            PasswordHasher.Md5Hex("adminmdp"), write.PasswordHash));
        Assert.Equal(write.PasswordHash, fakeAccountRepo.CurrentAccount!.PasswordHash);
    }

    [Fact]
    public async Task Test_LoginAccountHandler_LegacyPlaintextWire_RehashCoversMd5()
    {
        // Émetteur plaintext : reçu == password stocké. Le rehash doit
        // couvrir la forme canonique md5('secret') pour que le login
        // Godot suivant (qui envoie md5) passe par Verify.
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 10,
                Username = "player1",
                Password = "validpassword",
                WorldServerId = 1010
            }
        };
        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);
        var (session, client) = await NewSessionAsync();
        await using var _s = session;
        using var _c = client;

        await handlers.HandleLoginAccountAsync(session,
            new CNC_CL_ServerLoginAccount
            {
                Field1 = "player1",
                Field2 = "validpassword"   // plaintext, pas md5
            }, CancellationToken.None);

        Assert.True(session.IsAuthenticated);
        var write = Assert.Single(fakeAccountRepo.PasswordHashWrites);
        Assert.True(PasswordHasher.Verify(
            PasswordHasher.Md5Hex("validpassword"), write.PasswordHash));
    }

    [Fact]
    public async Task Test_LoginAccountHandler_PlaintextWire_AgainstMd5Phc()
    {
        // Réciproque : compte migré via Godot (PHC de md5('pw')), un
        // émetteur plaintext envoie 'pw' → canonicalisé en md5 → Verify ok.
        var wire = PasswordHasher.Md5Hex("pw");
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 3,
                Username = "tooltest",
                PasswordHash = PasswordHasher.HashWireCredential(wire),
                WorldServerId = 1010
            }
        };
        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);
        var (session, client) = await NewSessionAsync();
        await using var _s = session;
        using var _c = client;

        await handlers.HandleLoginAccountAsync(session,
            new CNC_CL_ServerLoginAccount { Field1 = "tooltest", Field2 = "pw" },
            CancellationToken.None);

        Assert.True(session.IsAuthenticated);
        Assert.Empty(fakeAccountRepo.PasswordHashWrites);
    }

    [Fact]
    public async Task Test_LoginAccountHandler_Argon2Hash_WrongPassword()
    {
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 2,
                Username = "admin",
                Password = "adminmdp",
                PasswordHash = PasswordHasher.HashWireCredential(
                    PasswordHasher.Md5Hex("adminmdp")),
                WorldServerId = 1010
            }
        };
        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);
        var (session, client) = await NewSessionAsync();
        await using var _s = session;
        using var _c = client;

        await handlers.HandleLoginAccountAsync(session,
            new CNC_CL_ServerLoginAccount
            {
                Field1 = "admin",
                Field2 = PasswordHasher.Md5Hex("mauvais")
            }, CancellationToken.None);

        Assert.False(session.IsAuthenticated);
        Assert.Empty(fakeAccountRepo.PasswordHashWrites);
    }

    [Fact]
    public async Task Test_LoginAccountHandler_MalformedHash_LegacyFallback()
    {
        // password_hash rempli mais non-PHC (donnée corrompue) : pas de
        // crash — chemin legacy, puis réécriture d'un PHC propre.
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 4,
                Username = "webtest1",
                Password = "secret1",
                PasswordHash = "nimporte-quoi-pas-phc",
                WorldServerId = 1010
            }
        };
        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);
        var (session, client) = await NewSessionAsync();
        await using var _s = session;
        using var _c = client;

        await handlers.HandleLoginAccountAsync(session,
            new CNC_CL_ServerLoginAccount
            {
                Field1 = "webtest1",
                Field2 = PasswordHasher.Md5Hex("secret1")
            }, CancellationToken.None);

        Assert.True(session.IsAuthenticated);
        var write = Assert.Single(fakeAccountRepo.PasswordHashWrites);
        Assert.True(PasswordHasher.IsArgon2Phc(write.PasswordHash));
    }

    [Fact]
    public async Task Test_LoginAccountHandler_Argon2Hash_PlaintextStoredIgnored()
    {
        // Sécurité : une fois password_hash rempli, la colonne legacy
        // `password` ne doit PLUS authentifier (sinon le hash ne sert à
        // rien) — même si `password` == reçu.
        var fakeAccountRepo = new FakeAccountRepository
        {
            CurrentAccount = new Account
            {
                Id = 2,
                Username = "admin",
                Password = "99999999999999999999999999999999", // piège
                PasswordHash = PasswordHasher.HashWireCredential(
                    PasswordHasher.Md5Hex("adminmdp")),
                WorldServerId = 1010
            }
        };
        var handlers = new LoginPacketHandlers(fakeAccountRepo, new FakeWorldRepository(), maxAttemptsPerMinute: int.MaxValue);
        var (session, client) = await NewSessionAsync();
        await using var _s = session;
        using var _c = client;

        // Le "mot de passe" legacy stocké ne vaut plus rien.
        await handlers.HandleLoginAccountAsync(session,
            new CNC_CL_ServerLoginAccount
            {
                Field1 = "admin",
                Field2 = "99999999999999999999999999999999"
            }, CancellationToken.None);

        Assert.False(session.IsAuthenticated);
        Assert.Empty(fakeAccountRepo.PasswordHashWrites);
    }
}
