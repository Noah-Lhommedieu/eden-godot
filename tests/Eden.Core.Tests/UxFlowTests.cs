using EdenClient;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Session;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>ClientConfig — format key=value de gamedata/client.cfg.</summary>
public class ClientConfigTests
{
    [Fact]
    public void DefaultsWhenEmpty()
    {
        var c = ClientConfig.Parse("");
        Assert.Equal("127.0.0.1", c.Host);
        Assert.Equal(6543, c.LoginPort);
        Assert.Equal(5200, c.WorldPort);
        Assert.Equal(5201, c.ZonePort);
    }

    [Fact]
    public void ParsesKeysAndComments()
    {
        var c = ClientConfig.Parse(
            "# commentaire\n; autre\n host = 192.168.1.10 \n" +
            "LOGIN_PORT=7000\nworld_port=5300\naccount=test123\n");
        Assert.Equal("192.168.1.10", c.Host);
        Assert.Equal(7000, c.LoginPort);
        Assert.Equal(5300, c.WorldPort);
        Assert.Equal("7000", c.Get("login_port")); // clé brute conservée
        Assert.Equal("test123", c.Account);
        Assert.Equal("192.168.1.10", c.Get("HOST")); // insensible casse
    }
}

/// <summary>AppearanceCodec — miroir AppearanceBlob serveur
/// (bitfields natifs CreateCharacterIntoDB).</summary>
public class AppearanceCodecTests
{
    [Fact]
    public void EncodeDecodeRoundTrip()
    {
        var a = new AppearanceCodec.Appearance(
            BodyCode: 4, HairStyle: 37, FaceStyle: 51, HairColor: 22,
            SkinColor: 33, EyesColor: 44, EyesColor2: 17);
        var blob = AppearanceCodec.Encode(a);
        Assert.Equal(32, blob.Length);
        Assert.Equal(a, AppearanceCodec.Decode(blob));
    }

    [Fact]
    public void BodyCodeNibble()
    {
        var blob = AppearanceCodec.Encode(
            new AppearanceCodec.Appearance(BodyCode: 9, 0, 0, 0, 0, 0, 0));
        Assert.Equal(9, blob[28] >> 4);
        Assert.Equal(9, AppearanceCodec.Decode(blob).BodyCode);
    }
}

/// <summary>CharModelSpec — code corps → lettre GLB (switch natif
/// rf_game FUN_0073ddc0 : 1→b, 2→m, 4→f, 6→k, 8→l, 9→c, sinon g).</summary>
public class CharModelSpecTests
{
    [Theory]
    [InlineData(0, "g")]
    [InlineData(1, "b")]
    [InlineData(2, "m")]
    [InlineData(4, "f")]
    [InlineData(6, "k")]
    [InlineData(8, "l")]
    [InlineData(9, "c")]
    [InlineData(3, "g")]   // code inconnu → défaut natif
    [InlineData(15, "g")]
    public void BodyLetterMatchesNative(int code, string letter)
        => Assert.Equal(letter, CharModelSpec.BodyLetter(code));

    [Fact]
    public void BodyStemFromBlob()
    {
        var blob = AppearanceCodec.Encode(
            new AppearanceCodec.Appearance(BodyCode: 6, 0, 0, 0, 0, 0, 0));
        Assert.Equal("k", CharModelSpec.BodyStem(blob));
        Assert.Equal("char/model/k", CharModelSpec.BodyModelPath(blob));
        Assert.Equal("g", CharModelSpec.BodyStem(null));
    }
}

/// <summary>Round-trips wire des nouveaux messages monde (NCID 32/4/5/6/7
/// + S→C 39/41) — mêmes layouts que les .g.cs serveur.</summary>
public class WorldCharMgmtMessageTests
{
    private static byte[] RoundTrip<T>(T msg) where T : NetMessage
        => msg.ToPayload();

    private static T Parse<T>(byte[] payload) where T : NetMessage, new()
    {
        var m = new T();
        var r = new BinReader(payload);
        r.ReadUShort(); // NCID
        m.Deserialize(r);
        return m;
    }

    [Fact]
    public void CreateNameCheck()
    {
        // Big5 wire : ASCII seulement pour le round-trip exact.
        var m = Parse<WorldCreateNameCheck>(RoundTrip(
            new WorldCreateNameCheck { Value = "heros123" }));
        Assert.Equal(32, m.Opcode);
        Assert.Equal("heros123", m.Value);
    }

    [Fact]
    public void VerifyNameResult()
    {
        var m = Parse<WorldVerifyNameResult>(RoundTrip(
            new WorldVerifyNameResult { Code = 3, Flag = 0 }));
        Assert.Equal(39, m.Opcode);
        Assert.Equal((short)3, m.Code);
    }

    [Fact]
    public void CreateCharacterName()
    {
        var m = Parse<WorldCreateCharacterName>(RoundTrip(
            new WorldCreateCharacterName { Value = "abc", Field2 = "", Field3 = 0 }));
        Assert.Equal(4, m.Opcode);
        Assert.Equal("abc", m.Value);
    }

    [Fact]
    public void CreateCharacterData()
    {
        var app = AppearanceCodec.Encode(
            new AppearanceCodec.Appearance(2, 5, 6, 7, 8, 9, 10));
        var m = Parse<WorldCreateCharacterData>(RoundTrip(
            new WorldCreateCharacterData
            { Race = 1, ClassId = 3, Flag = 0, Appearance = app }));
        Assert.Equal(5, m.Opcode);
        Assert.Equal((byte)1, m.Race);
        Assert.Equal((byte)3, m.ClassId);
        Assert.Equal(app, m.Appearance);
    }

    [Fact]
    public void RemoveCharacter()
    {
        var m = Parse<WorldRemoveCharacter>(RoundTrip(
            new WorldRemoveCharacter { CharId = 200000002, CharName = "test1" }));
        Assert.Equal(6, m.Opcode);
        Assert.Equal(200000002, m.CharId);
        Assert.Equal("test1", m.CharName);
    }

    [Fact]
    public void CharacterSummaryCarriesAppearance()
    {
        var app = new byte[32];
        app[28] = 0x40; // bodyCode 4 → f
        var c = new CharacterSummary(1, "x", 1, 60, 2, 1, 0, app);
        Assert.Equal("f", CharModelSpec.BodyStem(c.Appearance));
    }
}
