using Xunit;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Gateway;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Messages.Login;
using Eden.Core.Networking.Messages.World;

namespace Eden.Tests;

public class ProtocolAndSerializationTests
{
    [Fact]
    public void BinStream_ShouldSerializeAndDeserializeAllPrimitives()
    {
        var outStream = new OutBinStream();
        outStream.WriteByte(42);
        outStream.WriteShort(1234);
        outStream.WriteInt(987654);
        outStream.WriteLong(12345678901234L);
        outStream.WriteFloat(9.9f);
        outStream.WriteDouble(3.1415926535);
        outStream.WriteString("EdenEternal2026");

        var bytes = outStream.ToArray();
        var inStream = new InBinStream(bytes);

        Assert.Equal(42, inStream.ReadByte());
        Assert.Equal(1234, inStream.ReadShort());
        Assert.Equal(987654, inStream.ReadInt());
        Assert.Equal(12345678901234L, inStream.ReadLong());
        Assert.Equal(9.9f, inStream.ReadFloat());
        Assert.Equal(3.1415926535, inStream.ReadDouble());
        Assert.Equal("EdenEternal2026", inStream.ReadString());
        Assert.Equal(0, inStream.Remaining);
    }

    [Fact]
    public void HelloHandshake_ShouldSerializeFloatVersionExactly()
    {
        // CNC_CL_ClientHello (38) — float 9.9, envoyé par le serveur natif.
        var clientHello = new CNC_CL_ClientHello { Value = 9.9f };
        var outStream = new OutBinStream();
        clientHello.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var received = new CNC_CL_ClientHello();
        received.Deserialize(inStream);

        Assert.Equal(9.9f, received.Value);

        // CNC_CL_ServerHello (35) — string, envoyé par le client.
        var serverHello = new CNC_CL_ServerHello { Value = "HELLO SERVER" };
        var outServer = new OutBinStream();
        serverHello.Serialize(outServer);

        var inServer = new InBinStream(outServer.ToArray());
        var receivedServer = new CNC_CL_ServerHello();
        receivedServer.Deserialize(inServer);

        Assert.Equal("HELLO SERVER", receivedServer.Value);
    }

    [Fact]
    public void LoginAccount_ShouldSerializeUsernameAndPassword()
    {
        var login = new CNC_CL_ServerLoginAccount
        {
            Field1 = "test123",
            Field2 = "cc03e747a6afbbcbf8be7668acfebee5", // md5("test123")
            Field3 = "v1"
        };

        var outStream = new OutBinStream();
        login.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var received = new CNC_CL_ServerLoginAccount();
        received.Deserialize(inStream);

        Assert.Equal("test123", received.Field1);
        Assert.Equal("cc03e747a6afbbcbf8be7668acfebee5", received.Field2);
        Assert.Equal("v1", received.Field3);
    }

    [Fact]
    public void LoginFailure_ShouldSerializeErrorCode()
    {
        var fail = new CNC_CL_ClientLoginFail { Value = 2 };
        var outStream = new OutBinStream();
        fail.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var received = new CNC_CL_ClientLoginFail();
        received.Deserialize(inStream);

        Assert.Equal(2, received.Value);
    }

    /// <summary>DoWeaponUnEquip @0x8092e70 emits an additional 277 for
    /// each item moved automatically from equipment to an open inventory
    /// bag; verify its source/destination order and 13-byte body.</summary>
    [Fact]
    public void AutoUnequip277_UsesResolvedInventoryDestination()
    {
        var packet = new CNC_CZ_ClientSwitchItemLocationOK
        {
            Field1 = 1, Field2 = 10, Field3 = 0,
            Field4 = 0, Field5 = 2, Field6 = 1, Field7 = 1,
        };
        var output = new OutBinStream();
        packet.Serialize(output);

        Assert.Equal(new byte[]
        {
            1, 0, 10, 0, 0, 0, 0, 0, 2, 0, 1, 0, 1,
        }, output.ToArray());

        var input = new InBinStream(output.ToArray());
        var decoded = new CNC_CZ_ClientSwitchItemLocationOK();
        decoded.Deserialize(input);
        Assert.Equal((short)10, decoded.Field2);
        Assert.Equal((short)2, decoded.Field5);
        Assert.Equal((short)1, decoded.Field6);
        Assert.Equal((byte)1, decoded.Field7);
        Assert.Equal(0, input.Remaining);
    }

    [Fact]
    public void WorldServersList_ShouldSerializeWorldPropertiesCorrectly()
    {
        var packet = new CNC_CL_ClientGetWorldServers { LastWorldId = 1010 };
        packet.Worlds.Add(new CNC_CL_ClientGetWorldServers.Entry
        {
            Id = 1010,
            Name = "EternityT",
            Ip = "127.0.0.1",
            Port = 6544,
            Online = 25,
            MaxNum = 150,
            State = 0,
            ShowOrder = 1,
            Flags = 0,
            CharacterCount = 2
        });

        var outStream = new OutBinStream();
        packet.Serialize(outStream);

        var bytes = outStream.ToArray();
        Assert.True(bytes.Length > 20);

        var inStream = new InBinStream(bytes);
        Assert.Equal(1, inStream.ReadShort()); // count
        Assert.Equal(1010, inStream.ReadShort()); // WorldId
        Assert.Equal("EternityT", inStream.ReadString()); // WorldName
        Assert.Equal("127.0.0.1", inStream.ReadString()); // IpAddress
        Assert.Equal(6544, inStream.ReadShort()); // Port
        Assert.Equal(25, inStream.ReadInt()); // Online
        Assert.Equal(150, inStream.ReadShort()); // MaxNum
        Assert.Equal(0, inStream.ReadShort()); // State
        Assert.Equal(1, inStream.ReadShort()); // ShowOrder
        Assert.Equal(0, inStream.ReadShort()); // Flags
        Assert.Equal(2, inStream.ReadShort()); // CharacterCount
        Assert.Equal(1010, inStream.ReadShort()); // LastWorldId
        Assert.Equal(0, inStream.Remaining);
    }

    [Fact]
    public void SessionTicket_ShouldSerializeExact22Bytes()
    {
        var ticketData = new byte[22];
        for (byte i = 0; i < 22; i++) ticketData[i] = (byte)(i + 10);

        var ticket = new CNC_CL_ClientReceiveTicketToWorldServer { Ticket = ticketData };
        var outStream = new OutBinStream();
        ticket.Serialize(outStream);

        var bytes = outStream.ToArray();
        Assert.Equal(22, bytes.Length);

        var inStream = new InBinStream(bytes);
        var received = new CNC_CL_ClientReceiveTicketToWorldServer();
        received.Deserialize(inStream);

        Assert.Equal(ticketData, received.Ticket);
    }

    [Fact]
    public void CharacterMessages_ShouldSerializeAndDeserializeCorrectly()
    {
        // 1. Character Summary in CharacterListResponse
        var charSummary = new CharacterSummary
        {
            CharacterId = 100,
            Name = "EdenWarrior",
            Level = 50,
            ClassId = 1,
            RaceId = 2,
            GenderId = 1,
            HairStyle = 3,
            FaceStyle = 2,
            HairColor = 4,
            SkinColor = 1,
            NodeId = 5
        };

        var charListResp = new CharacterListResponse { Characters = new List<CharacterSummary> { charSummary } };
        var outList = new OutBinStream();
        charListResp.Serialize(outList);

        var inList = new InBinStream(outList.ToArray());
        var receivedList = new CharacterListResponse();
        receivedList.Deserialize(inList);

        Assert.Single(receivedList.Characters);
        Assert.Equal("EdenWarrior", receivedList.Characters[0].Name);
        Assert.Equal(50, receivedList.Characters[0].Level);

        // 2. CreateCharacterRequest
        var createReq = new CreateCharacterRequest
        {
            GivenName = "MageHero",
            ClassId = 3,
            RaceId = 1,
            GenderId = 2,
            HairStyle = 1,
            FaceStyle = 1,
            HairColor = 2,
            SkinColor = 1
        };
        // TODO: Implement Serialize/Deserialize for CreateCharacterRequest
        /*
        var outCreate = new OutBinStream();
        createReq.Serialize(outCreate);

        var inCreate = new InBinStream(outCreate.ToArray());
        var receivedCreate = new CreateCharacterRequest();
        receivedCreate.Deserialize(inCreate);

        Assert.Equal("MageHero", receivedCreate.GivenName);
        Assert.Equal(3, receivedCreate.ClassId);
        */

        // 3. ZoneTicketResponse
        var zoneTicket = new ZoneTicketResponse
        {
            ZoneServerIp = "192.168.1.50",
            ZoneServerPort = 5500,
            ZoneTicket = new byte[22]
        };
        var outZone = new OutBinStream();
        zoneTicket.Serialize(outZone);

        var inZone = new InBinStream(outZone.ToArray());
        var receivedZone = new ZoneTicketResponse();
        receivedZone.Deserialize(inZone);

        Assert.Equal("192.168.1.50", receivedZone.ZoneServerIp);
        Assert.Equal(5500, receivedZone.ZoneServerPort);
        Assert.Equal(22, receivedZone.ZoneTicket.Length);
    }

    [Fact]
    public void GatewayMessages_ShouldSerializeAndDeserializeCorrectly()
    {
        var loginReq = new GatewayAccountLoginRequest
        {
            Username = "Player1",
            SessionToken = "TokenABC",
            ClientVersion = 990
        };

        var outReq = new OutBinStream();
        loginReq.Serialize(outReq);

        var inReq = new InBinStream(outReq.ToArray());
        var receivedReq = new GatewayAccountLoginRequest();
        receivedReq.Deserialize(inReq);

        Assert.Equal("Player1", receivedReq.Username);
        Assert.Equal("TokenABC", receivedReq.SessionToken);
        Assert.Equal(990, receivedReq.ClientVersion);

        var heartbeat = new GatewayHeartbeatMessage { Timestamp = 1234567890L };
        var outHb = new OutBinStream();
        heartbeat.Serialize(outHb);

        var inHb = new InBinStream(outHb.ToArray());
        var receivedHb = new GatewayHeartbeatMessage();
        receivedHb.Deserialize(inHb);

        Assert.Equal(1234567890L, receivedHb.Timestamp);
    }

    [Fact]
    public async Task PacketDispatcher_ShouldRouteRegisteredMessagesCorrectly()
    {
        var dispatcher = new PacketDispatcher();
        var handled = false;

        dispatcher.Register(
            () => new CNC_CL_ServerLoginAccount(),
            (session, msg, token) =>
            {
                if (msg.Field1 == "RoutedUser") handled = true;
                return Task.CompletedTask;
            });

        var sampleCmd = new CNC_CL_ServerLoginAccount { Field1 = "RoutedUser", Field2 = "Pass", Field3 = "Code" };
        var outStream = new OutBinStream();
        sampleCmd.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        // NCID natif login : ServerLoginAccount = 1 (table de commandes
        // native, cf. LoginMessages.g.cs — ancienne numérotation 36-41
        // obsolète).
        var result = await dispatcher.DispatchAsync(1, inStream, null!, CancellationToken.None);

        Assert.True(result);
        Assert.True(handled);
    }

    [Fact]
    public void ChatMessage_ShouldSerializeAndDeserializeChannels()
    {
        var chat = new ChatMessage
        {
            Channel = (short)ChatChannelType.Guild,
            SenderName = "GuildMaster",
            Message = "Bienvenue dans la guilde!",
            TargetCharacterId = 42
        };

        var outStream = new OutBinStream();
        chat.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var received = new ChatMessage();
        received.Deserialize(inStream);

        Assert.Equal(ChatChannelType.Guild, received.ChannelType);
        Assert.Equal("GuildMaster", received.SenderName);
        Assert.Equal("Bienvenue dans la guilde!", received.Message);
        Assert.Equal(42, received.TargetCharacterId);
    }

    [Fact]
    public void CreateCharacterResponse_ShouldSerializeCharacterSummaryNested()
    {
        var response = new CreateCharacterResponse
        {
            Success = true,
            ErrorMessage = string.Empty,
            Character = new CharacterSummary
            {
                CharacterId = 101,
                Name = "Knight",
                Level = 1,
                ClassId = 1,
                RaceId = 1,
                GenderId = 1,
                NodeId = 5
            }
        };

        var outStream = new OutBinStream();
        response.Serialize(outStream);

        var inStream = new InBinStream(outStream.ToArray());
        var received = new CreateCharacterResponse();
        received.Deserialize(inStream);

        Assert.True(received.Success);
        Assert.NotNull(received.Character);
        Assert.Equal(101, received.Character.CharacterId);
        Assert.Equal("Knight", received.Character.Name);
    }
}
