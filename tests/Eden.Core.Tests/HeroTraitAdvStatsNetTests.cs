using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Session;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Wire custom Reborn : 9057 (pong RTT), 9058 (trait
/// héroïque), 32001/32002 (stats avancées). Les octets du 32002
/// sont le golden serveur (HeroTraitAndPhaseCTests.AdvStatsGoldenHex).
/// </summary>
public class HeroTraitAdvStatsNetTests
{
    private const string AdvStatsGoldenHex =
        "01020000C03F0000003F010005004D656C65650800352A32302B313030000048430300646D67010019790102000000C8420000F04100004842000070410000C841";

    [Fact]
    public void Ncids_Constants()
    {
        Assert.Equal(9057, EdenSession.EchoPongNcid);
        Assert.Equal(9058, HeroTraitWire.Ncid);
        Assert.Equal(32001, new ZoneAdvStatsQuery().Opcode);
        Assert.Equal(32002, new ZoneAdvStatsInfo().Opcode);
    }

    [Fact]
    public void Query32001_EmptyBody()
    {
        var w = new BinWriter();
        new ZoneAdvStatsQuery().Serialize(w);
        Assert.Empty(w.ToArray());
    }

    [Theory]
    [InlineData("00", 0)] [InlineData("01", 1)] [InlineData("08", 8)]
    [InlineData("09", 0)] [InlineData("FF", 0)]
    public void HeroTrait9058_Read(string hex, int expected)
        => Assert.Equal(expected, HeroTraitWire.Read(
            new BinReader(Convert.FromHexString(hex))));

    [Theory]
    [InlineData(-3, 0)] [InlineData(0, 0)] [InlineData(7, 7)]
    [InlineData(8, 7)] [InlineData(200, 7)]
    public void CreateFeature_ZeroBased_Clamped(int idx, int feature)
        => Assert.Equal((byte)feature, HeroTraitWire.FeatureFor(idx));

    [Fact]
    public void CreateData_Ncid5_CarriesFeatureByte()
    {
        var w = new BinWriter();
        new WorldCreateCharacterData
        {
            Race = 1, ClassId = 3, Flag = HeroTraitWire.FeatureFor(5),
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(3 + 32, b.Length);
        Assert.Equal(new byte[] { 1, 3, 5 }, b[..3]);
    }

    [Fact]
    public void AdvStats32002_ParsesServerGolden()
    {
        var m = new ZoneAdvStatsInfo();
        m.Deserialize(new BinReader(Convert.FromHexString(AdvStatsGoldenHex)));
        Assert.Equal(2, m.MaxHits);
        Assert.Equal(1.5f, m.ChanceMult);
        Assert.Equal(0.5f, m.DamageMult);
        var f = Assert.Single(m.Formulas);
        Assert.Equal(new ZoneAdvStatsInfo.Formula("Melee", "5*20+100",
            200f, "dmg"), f);
        var s = Assert.Single(m.Sources);
        Assert.Equal(31001, s.EnchantId);
        Assert.Equal(1, s.Origin);
        Assert.Equal(2, s.Stacks);
        Assert.Equal(100f, s.Gate);
        Assert.Equal((30f, 50f, 15f, 25f), (s.C1, s.F1, s.C2, s.F2));
    }
}
