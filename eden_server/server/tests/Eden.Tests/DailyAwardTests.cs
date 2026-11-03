using Eden.Core.Data.Parsers;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions calendrier de connexion — parser
/// <c>S_DailyAward.ini</c> et wire 615
/// <c>CNC_CZ_ClientUpdateDailyAwards</c> :
/// <c>[s16 day][i32 online][i64 days][i64 records][s16 type]
/// [s16 flag]</c> (Serialize natif @0x81e9650 — la classe générée
/// aplatie en 16B est fausse).
/// </summary>
public class DailyAwardTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(),
            Guid.NewGuid() + ".ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Build615_WireLayout()
    {
        var body = DailyAwardPackets.Build(
            dayIndex: 3, onlineSeconds: 1200,
            awardDays: 0b101, awardRecords: 0b001,
            type: 1, flag: 1);
        Assert.Equal(26, body.Length);
        Assert.Equal(3, BitConverter.ToInt16(body, 0));   // day_index
        Assert.Equal(1200, BitConverter.ToInt32(body, 2)); // online s
        Assert.Equal(0b101L, BitConverter.ToInt64(body, 6));
        Assert.Equal(0b001L, BitConverter.ToInt64(body, 14));
        Assert.Equal(1, BitConverter.ToInt16(body, 22)); // type
        Assert.Equal(1, BitConverter.ToInt16(body, 24)); // flag +0x1e
    }

    [Fact]
    public void Build615_DisabledType4()
    {
        // Système désactivé → tous les champs à 0, type 4, flag 1
        // (Execute natif @0x8246930).
        var body = DailyAwardPackets.Build(0, 0, 0, 0, 4);
        Assert.Equal(4, BitConverter.ToInt16(body, 22));
        Assert.Equal(1, BitConverter.ToInt16(body, 24));
    }

    [Fact]
    public void ParseDailyAward_Columns()
    {
        // [day][item][amount][?][required_s][?] — champs natifs
        // def+4 item, def+8 amount, def+0xc required.
        var path = WriteTemp(
            "|V.4|6|\n" +
            "1|51464|1|1|600|2|\n" +
            "2|51465|3|1|1800|2|\n");
        var defs = DailyAwardParser.Parse(path);
        Assert.Equal(2, defs.Count);
        Assert.Equal(51464, defs[1].ItemId);
        Assert.Equal(1, defs[1].Amount);
        Assert.Equal(600, defs[1].RequiredSeconds);
        Assert.Equal(51465, defs[2].ItemId);
        Assert.Equal(1800, defs[2].RequiredSeconds);
    }
}
