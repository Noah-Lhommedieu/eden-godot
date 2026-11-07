using System.Text;
using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Ville de guilde : peuplement des bâtiments (housekeeper + PNJ de
/// service) et aires de récolte physiques. Les tests « données »
/// vérifient les ids cités dans docs/temp-plans/2026-11-06-vdg-ressources.md
/// contre les tables réellement chargées.
/// </summary>
public class FamilyCityRulesTests
{
    static FamilyCityRulesTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly Lazy<GameDataService> Data = new(() =>
    {
        var gd = new GameDataService(
            TestPaths.RepoRoot + "/server_us/Data/db_decoded");
        gd.LoadAll();
        return gd;
    });

    private static FamilyBuildingItem Bld(short loc, int tid, short lvl)
        => new() { Loc = loc, Tid = tid, ItemType = 0x29, Level = lvl };

    [Fact]
    public void NpcCap_FollowsProduceNpcLimit()
    {
        Assert.Equal(0, FamilyCityRules.NpcCap(FamilyCityRules.TypeMine, 1));
        Assert.Equal(4, FamilyCityRules.NpcCap(FamilyCityRules.TypeMine, 3));
        Assert.Equal(8, FamilyCityRules.NpcCap(FamilyCityRules.TypeFarm, 9));
        Assert.Equal(0, FamilyCityRules.NpcCap(2, 5));   // hôtel : autre table
    }

    [Fact]
    public void ServiceType_MatchesBuildingType()
    {
        Assert.Equal(13, FamilyCityRules.ServiceTypeOf(4));
        Assert.Equal(12, FamilyCityRules.ServiceTypeOf(5));
        Assert.Equal(14, FamilyCityRules.ServiceTypeOf(6));
        Assert.Equal(0, FamilyCityRules.ServiceTypeOf(10));
    }

    [Fact]
    public void PlanPopulate_AddsHousekeeperThenStationNpcsUpToCap()
    {
        // Mine niveau 3 (cap 4) + housekeeper 35012.
        var defs = new Dictionary<int, GameDataService.BuildingDef>
        {
            [102] = new(5, 10, 1000, "H01101", 3, 0, 35012),
        };
        var c0 = new Dictionary<short, FamilyBuildingItem>
            { [10] = Bld(10, 102, 3) };
        var plan = FamilyCityRules.PlanPopulate(c0,
            t => defs.GetValueOrDefault(t), _ => 53018, n => 40000 + n % 100);
        Assert.Equal(new short[] { 11, 12, 13, 14, 15 },
            plan.Select(p => p.Loc).ToArray());
        Assert.Equal(53018, plan[0].Tid);               // housekeeper base+1
        Assert.Equal(new[] { 53019, 53020, 53021, 53022 },
            plan.Skip(1).Select(p => p.Tid).ToArray());
        Assert.All(plan, p => Assert.Equal(0x2a, p.ItemType));
    }

    [Fact]
    public void PlanPopulate_NeverOverwritesNorDuplicates()
    {
        var defs = new Dictionary<int, GameDataService.BuildingDef>
        {
            [101] = new(4, 10, 1000, "H01001", 3, 0, 35002),
        };
        var c0 = new Dictionary<short, FamilyBuildingItem>
        {
            [0] = Bld(0, 101, 3),
            [1] = new() { Loc = 1, Tid = 53008, ItemType = 0x2a },
            [2] = new() { Loc = 2, Tid = 53009, ItemType = 0x2a },
        };
        var plan = FamilyCityRules.PlanPopulate(c0,
            t => defs.GetValueOrDefault(t), _ => 53008, _ => 39049);
        // 53008 (housekeeper) et 53009 déjà là : restent 53010, 53011,
        // 53012 aux slots 3, 4, 5.
        Assert.Equal(new[] { (short)3, (short)4, (short)5 },
            plan.Select(p => p.Loc).ToArray());
        Assert.Equal(new[] { 53010, 53011, 53012 },
            plan.Select(p => p.Tid));
    }

    [Fact]
    public void GatherKeys_AreSixtyToSixtyFour()
    {
        Assert.True(FamilyCityRules.IsGatherKey(60));
        Assert.True(FamilyCityRules.IsGatherKey(64));
        Assert.False(FamilyCityRules.IsGatherKey(100));   // sortie de ville
        Assert.False(FamilyCityRules.IsGatherKey(51));    // AreaTravel
    }

    // --- données réelles ------------------------------------------

    [Fact]
    public void Data_ProductionBuildingsHaveHousekeeperNpc()
    {
        var gd = Data.Value;
        foreach (var type in new[] { 4, 5, 6, 7, 8, 9 })
        {
            var def = gd.Buildings.Values.First(b => b.Type == type);
            Assert.True(def.NpcItemTid > 0, $"type {type} sans NpcItemTid");
            var item = gd.Items.First(i => i.Id == def.NpcItemTid);
            Assert.True(item.FamilyNpcTid > 0,
                $"type {type} item {item.Id} sans col35 (tid PNJ)");
            Assert.Contains(gd.Npcs, n => n.Id == item.FamilyNpcTid);
        }
    }

    [Fact]
    public void Data_StationNpcsExistAndMatchServiceType()
    {
        var gd = Data.Value;
        foreach (var type in new[] { 4, 5, 6, 7, 8, 9 })
        {
            var want = FamilyCityRules.ServiceTypeOf(type);
            var npcs = FamilyCityRules.StationNpcsFor(type, 6);
            Assert.NotEmpty(npcs);
            // Le housekeeper du bâtiment EST le PNJ d'assignation de
            // rang 1 : il porte le service 12-17 du type.
            var hkDef = gd.Buildings.Values.First(b => b.Type == type);
            var hkTid = gd.Items.First(i => i.Id == hkDef.NpcItemTid)
                .FamilyNpcTid;
            var hk = gd.Npcs.First(n => n.Id == hkTid);
            Assert.Contains(hk.Services, s => s.Type == want);
            Assert.DoesNotContain(hkTid, npcs);
            // Tous existent.
            foreach (var id in npcs)
                Assert.Contains(gd.Npcs, n => n.Id == id);
        }
    }

    [Fact]
    public void Data_ToolVendorsAndGatherAreasExist()
    {
        var gd = Data.Value;
        foreach (var a in FamilyCityRules.GatherAreas)
            Assert.Contains(gd.Npcs, n => n.Id == a.VendorNpcTid);
        // Les 3 outils de base sont restreints aux nodes 501/502 et à
        // la clé d'aire de leur zone (col32 « 501 502;60|61|62 »).
        var pick = gd.Items.First(i => i.Id == 36819);
        Assert.Contains(501, pick.UseAreaNodeIds);
        Assert.Contains(502, pick.UseAreaNodeIds);
        Assert.Equal(60, pick.UseAreaSubType);
        Assert.Equal(61, gd.Items.First(i => i.Id == 36821).UseAreaSubType);
        Assert.Equal(62, gd.Items.First(i => i.Id == 36823).UseAreaSubType);
    }

    [Theory]
    [InlineData("S501")]
    [InlineData("S502")]
    public void Data_HallScenesDeclareGatherAreasAndBuildingSlots(string code)
    {
        var scene = Eden.Core.Data.Parsers.SceneParser.Parse(
            Path.Combine(TestPaths.ServerUsData, "scene", code + ".ini"));
        // 3 aires de récolte actives : keyId 60/61/62 ↔ event_area GATEWAY.
        foreach (var a in FamilyCityRules.GatherAreas)
        {
            var gw = Assert.Single(scene.Gateways, g => g.KeyId == a.KeyId);
            Assert.Contains(scene.EventAreas, e => e.Id == gw.EventAreaId);
        }
        // Aires [position] : 0x32 manoir, 0x33-0x37 bâtiments, 0x38 sacré.
        for (var area = 0x32; area <= 0x38; area++)
            Assert.True(scene.PositionGroups.ContainsKey(area),
                $"{code} : aire [position] {area:X} absente");
    }

    [Fact]
    public void Market_HostsAuctioneerBankerAndMerchant_UpToMallCap()
    {
        Assert.Equal(0, FamilyCityRules.NpcCap(FamilyCityRules.TypeMarket, 3));
        Assert.Equal(1, FamilyCityRules.NpcCap(FamilyCityRules.TypeMarket, 4));
        Assert.Equal(3, FamilyCityRules.NpcCap(FamilyCityRules.TypeMarket, 12));
        Assert.Equal(new[] { 53003, 53004, 53625 },
            FamilyCityRules.StationNpcsFor(FamilyCityRules.TypeMarket, 12));
        Assert.Equal(new[] { 53003 }, FamilyCityRules.StationNpcsFor(FamilyCityRules.TypeMarket, 4));
    }

    [Fact]
    public void Hotel_HasNoNpc_BecauseFrenchDataHasNoHotelContract() =>
        Assert.Empty(FamilyCityRules.StationNpcsFor(FamilyCityRules.TypeHotel, 12));

    /// <summary>Preuve données FR : chaque PNJ du marché a un contrat kind 42 (c_item col35).</summary>
    [Fact]
    public void RealCorpus_MarketNpcsHaveFrenchContracts()
    {
        var f = Path.Combine(TestPaths.RepoRoot, "..", "corpus", "fr-full", "data", "db", "c_item.ini");
        if (!File.Exists(f)) return;
        var raw = System.Text.Encoding.GetEncoding("Big5").GetString(File.ReadAllBytes(f));
        var contractNpcs = raw.Split('\n').Select(l => l.Split('|'))
            .Where(p => p.Length > 35 && p[8] == "42" && int.TryParse(p[35], out _))
            .Select(p => int.Parse(p[35])).ToHashSet();
        foreach (var npc in FamilyCityRules.StationNpcsFor(FamilyCityRules.TypeMarket, 12))
            Assert.Contains(npc, contractNpcs);
    }
}
