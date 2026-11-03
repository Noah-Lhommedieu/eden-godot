using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone.Combat;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions du chemin d'éveil — parsers `S_DestinyRoad.ini` /
/// `S_DestinyEvent.ini` (CDestinyRoadQuery::Deserialize @0x085d7880,
/// CDestinyEventQuery::Deserialize @0x085d6000), layout wire 534
/// (SDestinyRoad::Serialize @0x0863b750) et formule
/// `CCharFormula::DesDeclineRate` @0x083f4db0.
/// </summary>
public class DestinyRoadTests
{
    private static readonly string DecodedDir =
        TestPaths.RepoRoot + "/server_us/Data/db_decoded";

    // ------------------------- parsers -------------------------

    [Fact]
    public void ParseRoads_RoutesAndCells()
    {
        var roads = DestinyDataParser.ParseRoads(
            Path.Combine(DecodedDir, "S_DestinyRoad.ini"));
        // 12 routes natives — une par tranche de niveau.
        Assert.True(roads.Count >= 10);
        var r1 = roads.First(r => r.Id == 1);
        Assert.Equal(0, r1.Tier);
        Assert.Equal("BackDrop01", r1.Backdrop);
        Assert.Equal(39, r1.Cells.Length);
        // Route 1 : groupes 1-3 dans les cases + terminus 140.
        Assert.Equal(140, r1.Cells[^1]);
        Assert.All(r1.Cells[..^1],
            g => Assert.InRange(g, 1, 1000));
    }

    [Fact]
    public void ParseEvents_TypesAndGroups()
    {
        var events = DestinyDataParser.ParseEvents(
            Path.Combine(DecodedDir, "S_DestinyEvent.ini"));
        Assert.True(events.Count > 200);
        // Types natifs présents dans la data : 0,1,2,3,5,6
        // (relevé RE — types 4/7 définis en code, absents des data).
        Assert.Contains(events, e => e.Type == 1 && e.Value > 0);
        Assert.Contains(events, e => e.Type == 2 && e.Value > 0);
        Assert.Contains(events, e => e.Type == 3 && e.Value > 0);
        Assert.Contains(events, e => e.Type == 5 && e.Value > 0);
        Assert.Contains(events, e => e.Type == 6 && e.Value > 0 &&
            e.Param2 > 0);
        var groups = DestinyDataParser.BuildEventGroupMap(events);
        Assert.NotEmpty(groups);
        // Chaque groupe référencé par une case de route doit exister.
        var roads = DestinyDataParser.ParseRoads(
            Path.Combine(DecodedDir, "S_DestinyRoad.ini"));
        foreach (var g in roads.SelectMany(r => r.Cells)
                     .Where(g => g > 0).Distinct())
            Assert.True(groups.ContainsKey(g),
                $"groupe {g} sans events");
    }

    [Fact]
    public void BuildRoadLevelMap_TierLookup()
    {
        var roads = DestinyDataParser.ParseRoads(
            Path.Combine(DecodedDir, "S_DestinyRoad.ini"));
        var map = DestinyDataParser.BuildRoadLevelMap(roads);
        Assert.NotEmpty(map);
        // Tier 0 exigé (route 1) — les routes haut-tier restent
        // exclusives du tirage (lower_bound natif).
        Assert.True(map.ContainsKey(0));
        Assert.Contains(1, map[0]);
    }

    // ------------------------- wire 534 -------------------------

    [Fact]
    public void DestinyRoad_SerializeMatchesNativeLayout()
    {
        // SDestinyRoad::Serialize natif : [s16 road][s16 type]
        // [s16 step][s16 dice][i32 exp_buff][s16 dice_point]
        // [s16 func][u8 flag][u16 n][n×s16].
        var slots = new short[39];
        for (var i = 0; i < 39; i++) slots[i] = (short)(100 + i);
        var cmd = new CNC_CZ_ClientPlayerDestinyRoad
        {
            RoadId = 3, RoadType = 2, Step = 7, RollsLeft = 9,
            DpPool = 5000, MovePoints = 4, FunctionState = 0b101,
            RerollFlag = 1, Slots = slots,
        };
        var stream = new Eden.Core.Networking.OutBinStream();
        cmd.Serialize(stream);
        var b = stream.ToArray();
        var p = 0;
        Assert.Equal(3, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(2, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(7, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(9, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(5000, BitConverter.ToInt32(b, p)); p += 4;
        Assert.Equal(4, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(0b101, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(1, b[p]); p++;
        Assert.Equal(39, BitConverter.ToUInt16(b, p)); p += 2;
        for (var i = 0; i < 39; i++)
        {
            Assert.Equal(100 + i, BitConverter.ToInt16(b, p));
            p += 2;
        }
        Assert.Equal(b.Length, p);
    }

    // ------------------------- DesDeclineRate -------------------------

    private static NativeCombatFormulas.Params P =>
        NativeCombatFormulas.ResolveParameters(
            new ParameterParser.ParameterSet(new Dictionary<int, float>
            {
                [576] = 17f, [577] = 20f,
            }, new Dictionary<int, string>()));

    [Fact]
    public void DesDeclineRate_NativeBrackets()
    {
        // Égal → 1.0 (retour direct natif).
        Assert.Equal(1f,
            NativeCombatFormulas.DesDeclineRate(50, 50, P));
        // diff=1 → (100-100)/p = 0 → 1.0.
        Assert.Equal(1f,
            NativeCombatFormulas.DesDeclineRate(50, 49, P));
        // Mob plus fort, diff=2, p576=17 : (200-100)/17 ≈ 5.88 → 6
        // → (100-6)/100 = 0.94.
        Assert.Equal(0.94f,
            NativeCombatFormulas.DesDeclineRate(50, 52, P), 2);
        // Mob plus faible, diff=5, p577=20 : (500-100)/20 = 20
        // → 0.80.
        Assert.Equal(0.80f,
            NativeCombatFormulas.DesDeclineRate(50, 45, P), 2);
        // Très grand écart → clamp 100 → 0.
        Assert.Equal(0f,
            NativeCombatFormulas.DesDeclineRate(50, 1, P));
    }
}
