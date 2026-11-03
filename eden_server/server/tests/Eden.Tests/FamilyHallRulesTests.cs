using Eden.Core.Data.Parsers;
using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests des règles du hall de famille et du codec 484 — formats et
/// mécaniques prouvés par désassemblage :
/// <list type="bullet">
/// <item><c>SFamily::CheckFamilyItemDelete</c> @0x8465fd0 —
/// housekeeper indélétable en base+1, bâtiment exige des slots liés
/// quasi vides.</item>
/// <item><c>SFamily::CheckNpcSizeLimit</c> @0x8464ea0 — caps
/// HouseNpcLimit/ProduceNpcLimit/MallNpcLimit (.rodata
/// 0x888fb52/6e/8a).</item>
/// <item><c>SFamily::GetNpcPositionByNpcLoc</c> @0x84662b0 — aires
/// 0x32-0x38 + offsets 4PHouse rotatés.</item>
/// <item>484 <c>CNC_CZ_ClientFamilyContainerData</c> — Serialize
/// @0x81e7b40 + ctor @0x8578f20 (OpenShop).</item>
/// </list>
/// </summary>
public class FamilyHallRulesTests
{
    private static GameDataService.BuildingDef Def(short type = 4,
        int npcItem = 0) => new(type, 10, 1000, "H00101", 3, 0,
        npcItem);

    private static FamilyBuildingItem It(short loc, short itemType,
        short itemTid = 5000, int tid = 100, short level = 3) => new()
    {
        ItemTid = itemTid, Tid = tid, ItemType = itemType, Loc = loc,
        Level = level,
    };

    private static GameDataService.BuildingDef? GetDef(int tid)
        => Def();

    // --- CheckFamilyItemDelete ------------------------------------

    [Fact]
    public void Delete_HolyLoc_AlwaysOk()
        => Assert.True(FamilyHallRules.CheckFamilyItemDelete(
            new Dictionary<short, FamilyBuildingItem>(), 0x33,
            GetDef));


    [Fact]
    public void Delete_EmptyLoc_Ok()
    {
        var c0 = new Dictionary<short, FamilyBuildingItem>
            { [20] = It(20, 0x29) };
        Assert.True(FamilyHallRules.CheckFamilyItemDelete(c0, (short)21,
            GetDef));

    }

    [Fact]
    public void Delete_HouseKeeper_AtBasePlus1_Refused()
    {
        // Bâtiment en 20 (type 4, npc item 35001) + housekeeper
        // item_tid 35001 en 21 → indélétable.
        var c0 = new Dictionary<short, FamilyBuildingItem>
        {
            [20] = It(20, 0x29, tid: 100),
            [21] = It(21, 0x2a, itemTid: (short)30000),
        };
        Assert.False(FamilyHallRules.CheckFamilyItemDelete(c0,
            (short)21, t => Def(4, 30000)));
        // … mais un autre NPC à la même loc est retirable.
        Assert.True(FamilyHallRules.CheckFamilyItemDelete(c0, (short)21,
            t => Def(4, 30002)));
        // … et le housekeeper à une autre loc aussi.
        Assert.True(FamilyHallRules.CheckFamilyItemDelete(
            new Dictionary<short, FamilyBuildingItem>
            {
                [20] = It(20, 0x29, tid: 100),
                [22] = It(22, 0x2a, itemTid: (short)30000),
            }, (short)22, t => Def(4, 30000)));
    }

    [Fact]
    public void Delete_Building_RequiresNearlyEmptyGroup()
    {
        var c0 = new Dictionary<short, FamilyBuildingItem>
        {
            [20] = It(20, 0x29, tid: 100),
            [21] = It(21, 0x2a),
            [22] = It(22, 0x2a),
        };
        // 2 items attachés > cap 1 → refus.
        Assert.False(FamilyHallRules.CheckFamilyItemDelete(c0, (short)20,
            GetDef));

        c0.Remove(22);
        Assert.True(FamilyHallRules.CheckFamilyItemDelete(c0, (short)20,
            GetDef));

    }

    // --- CheckNpcSizeLimit -----------------------------------------

    [Fact]
    public void NpcLimit_ProduceBuilding_Level3()
    {
        // ProduceNpcLimit[level-1=2] = 4 → 4 NPCs max dans le groupe.
        var c0 = new Dictionary<short, FamilyBuildingItem>
            { [20] = It(20, 0x29, tid: 100, level: 3) };
        for (short l = 21; l <= 24; l++) c0[l] = It(l, 0x2a);
        // 4 NPCs présents → le 5e est refusé.
        Assert.False(FamilyHallRules.CheckNpcSizeLimit(c0, (short)25,
            t => Def(4)));
        c0.Remove(24);
        Assert.True(FamilyHallRules.CheckNpcSizeLimit(c0, (short)24,
            t => Def(4)));
    }

    [Fact]
    public void NpcLimit_GroupBaseAndHoly_AlwaysOk()
    {
        var c0 = new Dictionary<short, FamilyBuildingItem>();
        Assert.True(FamilyHallRules.CheckNpcSizeLimit(c0, (short)20, GetDef));

        Assert.True(FamilyHallRules.CheckNpcSizeLimit(c0, 0x33, GetDef));

    }

    // --- CheckItemLoc ----------------------------------------------

    [Fact]
    public void ItemLoc_Rules()
    {
        var c0 = new Dictionary<short, FamilyBuildingItem>
            { [20] = It(20, 0x29) };
        Assert.True(FamilyHallRules.CheckItemLoc((short)0x2a, (short)21, c0));
        // Pas de bâtiment à la base 30 → NPC refusé.
        Assert.False(FamilyHallRules.CheckItemLoc((short)0x2a, (short)31, c0));
        // Bâtiment uniquement sur une base de groupe.
        Assert.True(FamilyHallRules.CheckItemLoc((short)0x29, (short)30, c0));
        Assert.False(FamilyHallRules.CheckItemLoc((short)0x29, (short)31, c0));
        // Holy loc toujours valide.
        Assert.True(FamilyHallRules.CheckItemLoc((short)0x16, (short)0x33, c0));
    }

    // --- ResolveNpcPosition -----------------------------------------

    [Fact]
    public void PositionArea_Mapping()
    {
        Assert.Equal(0x32, FamilyHallRules.PositionArea((short)100));
        Assert.Equal(0x38, FamilyHallRules.PositionArea((short)0x33));
        Assert.Equal(0x33, FamilyHallRules.PositionArea((short)5));
        Assert.Equal(0x34, FamilyHallRules.PositionArea((short)15));
        Assert.Equal(0x37, FamilyHallRules.PositionArea((short)45));
    }

    [Fact]
    public void ResolveNpcPosition_RotatesOffsets()
    {
        // Base (100,50,z=2,facing π/2) + slot 1 offset (dx=1,dy=0,df=0)
        // → x = 100 + 1·cos(π/2) = 100, y = 50 − 1·sin(π/2) = 49.
        var offsets = new List<(float, float, float)> { (1, 0, 0) };
        var p = FamilyHallRules.ResolveNpcPosition(100, 50, 2, MathF.PI / 2, (short)1, offsets);
        Assert.NotNull(p);
        Assert.Equal(100f, p.Value.X, 3);
        Assert.Equal(49f, p.Value.Y, 3);
        Assert.Equal(MathF.PI / 2, p.Value.Facing, 3);
        // Loc de groupe (20) → slot 1 du même tableau.
        var p2 = FamilyHallRules.ResolveNpcPosition(0, 0, 0, 0, (short)20,
            offsets);
        Assert.NotNull(p2);
        Assert.Equal(1f, p2.Value.X, 3);
        // Slot hors table → null.
        Assert.Null(FamilyHallRules.ResolveNpcPosition(0, 0, 0, 0, (short)5,
            offsets));
    }

    // --- Statues (161) ------------------------------------------------

    [Fact]
    public void StatueModel_ForPosition()
    {
        Assert.Equal(54901, FamilyHallRules.StatueModelForPos(1));
        Assert.Equal(54904, FamilyHallRules.StatueModelForPos(4));
        Assert.True(FamilyHallRules.IsStatueTemplate(54901));
        Assert.True(FamilyHallRules.IsStatueTemplate(54905));
        Assert.False(FamilyHallRules.IsStatueTemplate(53002));
        Assert.False(FamilyHallRules.IsStatueTemplate(54906));
    }

    [Fact]
    public void BuildStatueCsv_NativeLayout()
        => Assert.Equal("Nom,1,2,3,1,4000,10,5,0",
            FamilyHallRules.BuildStatueCsv("Nom", 1, 2, 3, 1, 4000,
                10, 5, 0));

    // --- Nodes d'instance de hall (501/502) --------------------------

    [Fact]
    public void FamilyInstanceNodes_AreRegistered_Type4()
    {
        // S_Node.ini ne porte que les nodes 1-37 : 501/502 sont les
        // nœuds de hall choisis par CFamily::InstanceCreate natif
        // (mission @0x81bf660 : type1→0x1f5, type0→0x1f6) — le gate
        // IsInFamilyInstance exige le type 4 (zone @0x84098d0).
        var nodes = new List<GameDataParsers.NodeDef>();
        GameDataService.EnsureFamilyInstanceNodes(nodes);
        var n501 = Assert.Single(nodes, n => n.Id == 501);
        var n502 = Assert.Single(nodes, n => n.Id == 502);
        Assert.Equal(4, n501.Type);
        Assert.Equal(4, n502.Type);
        Assert.Equal("S501", n501.MapCode);
        Assert.Equal("S502", n502.MapCode);
        // Idempotent — pas de doublon si les données en portent déjà.
        GameDataService.EnsureFamilyInstanceNodes(nodes);
        Assert.Equal(2, nodes.Count);
    }

    // --- Codec 484 ---------------------------------------------------

    [Fact]
    public void FamilyContainerData_WireLayout()
    {
        var body = FamilyPackets.FamilyContainerData((short)1, (short)2,
            new List<FamilyBuildingItem>
            {
                It(21, 0x2a, itemTid: (short)30000, tid: 30007, level: (short)2),
            });
        var r = new InBinStream(body);
        Assert.Equal(1, r.ReadShort());          // type (OpenShop)
        Assert.Equal(2, r.ReadShort());          // container_index
        Assert.Equal(1, r.ReadShort());          // u16 count
        Assert.Equal(21, r.ReadShort());         // loc
        Assert.Equal(30000, r.ReadInt());        // item_tid (zext)
        Assert.Equal(30007, r.ReadInt());        // tid
        Assert.Equal(0x2a, r.ReadShort());       // item_type
        Assert.Equal(0, r.ReadShort());          // durability
        Assert.Equal(2, r.ReadShort());          // level
        Assert.Equal(0, r.ReadInt());            // exp
        Assert.Equal(0, r.ReadInt());            // due_date
        Assert.Equal(0, r.ReadInt());            // item+0xa0
    }
}
