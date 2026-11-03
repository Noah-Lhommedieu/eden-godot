using System.Text;
using Xunit;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;

namespace Eden.Tests;

public class MyCryptTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";
    private static readonly string SClassPath = DbDir + "/S_Class.ini";
    private static readonly string SMonsterPath = DbDir + "/S_Monster.ini";

    static MyCryptTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void DecodeFile_S_Class_StartsWithHeader()
    {
        var decoded = MyCrypt.DecodeFile(SClassPath);
        var text = Encoding.GetEncoding("Big5").GetString(decoded);
        Assert.StartsWith("|V.6|65|", text);
    }

    [Fact]
    public void DecodeFile_NoEncodeMagic_ReturnsRaw()
    {
        var raw = new byte[] { 1, 2, 3 };
        Assert.Same(raw, MyCrypt.DecodeFile(raw));
    }

    [Fact]
    public void EncodeDecode_RoundTrip()
    {
        var plain = Encoding.Latin1.GetBytes("|V.6|65|\r\n1|Warrior|test data");
        var cipher = MyCrypt.CryptEncode(plain, MyCrypt.Password);
        var decoded = MyCrypt.CryptDecode(cipher, MyCrypt.Password);
        Assert.Equal(plain, decoded);
    }

    [Fact]
    public void DecodeFile_S_Monster_StartsWithHeader()
    {
        var decoded = MyCrypt.DecodeFile(SMonsterPath);
        var text = Encoding.GetEncoding("Big5").GetString(decoded);
        Assert.StartsWith("|V.6|62|", text);
    }

    [Fact]
    public void ParseS_Class_Loads305ClassDefinitions()
    {
        var defs = ClassStatsParser.ParseClassDefinitions(SClassPath);
        Assert.Equal(305, defs.Count);
    }

    [Fact]
    public void ParseS_Class_WarriorHasCorrectStats()
    {
        var defs = ClassStatsParser.ParseClassDefinitions(SClassPath);
        var warrior = defs.Find(d => d.Id == 1);
        Assert.NotNull(warrior);
        Assert.Equal("J009", warrior!.ClassCode);
        // Warrior: STR% eleve (col15=123), INT% bas (col16=69)
        Assert.Equal(123, warrior.StatPercents[2]);
        Assert.Equal(69, warrior.StatPercents[3]);
        // 10 paliers de progression
        Assert.Equal(10, warrior.LevelProgression.Count);
        Assert.Equal(1, warrior.LevelProgression[0].Level);
        Assert.Equal(90, warrior.LevelProgression[9].Level);
    }

    [Fact]
    public void ParseS_Class_FiveRaceVariantsExist()
    {
        var defs = ClassStatsParser.ParseClassDefinitions(SClassPath);
        // Meme classe, 5 races: 1, 101, 201, 301, 401
        Assert.Equal(5, defs.Count(d => d.BaseClassIndex == 1));
        Assert.Equal(5, defs.Count(d => d.BaseClassIndex == 61));
    }

    [Fact]
    public void ParseS_Class_RacialVariantsHaveDifferentStats()
    {
        var defs = ClassStatsParser.ParseClassDefinitions(SClassPath);
        var human = defs.Find(d => d.Id == 1)!;
        var zumi = defs.Find(d => d.Id == 101)!;
        // Les pourcentages de stats different selon la race
        Assert.NotEqual(human.StatPercents, zumi.StatPercents);
    }

    [Fact]
    public void ParseS_Class_ClassSpellIdsAreLoaded()
    {
        var defs = ClassStatsParser.ParseClassDefinitions(SClassPath);
        var warrior = defs.Find(d => d.Id == 1)!;
        Assert.Contains(50201, warrior.ClassSpellIds);
        // c22-29 = certificats par palier (CClassData+0x44, vector[8])
        Assert.Equal(new[] { 33272, 33273, 33274, 34170, 34171,
            34172, 0, 0 }, warrior.CertificateIds);
        // c49-54 = talents AA (CClassData+0x68) — jamais octroyés
        // par GetClassSpell → exclus de ClassSpellIds
        Assert.DoesNotContain(warrior.TalentIds.Where(t => t > 0),
            t => warrior.ClassSpellIds.Contains(t));
    }
}
