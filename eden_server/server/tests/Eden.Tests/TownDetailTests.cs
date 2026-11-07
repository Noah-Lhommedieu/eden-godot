using Eden.Core.Game.Family;
using Eden.Zone.Features.TownDetail;

namespace Eden.Tests;

public class TownDetailTests
{
    private static FamilyBuildingItem Row(short loc, short type, int tid) =>
        new() { Loc = loc, ItemType = type, Tid = tid };

    [Fact]
    public void Build_ListsPlacedNpcs_WithRoles_AndToolVendors()
    {
        var rows = new[]
        {
            Row(0, 0x29, 101),                 // ferme
            Row(1, 0x2a, 53008),               // housekeeper (loc % 10 == 1)
            Row(2, 0x2a, 53009),               // PNJ de service
            Row(20, 0x29, 218),                // marché
            Row(22, 0x2a, 53003),              // commissaire-priseur
        };
        var b = TownDetailHandler.Build(5001, rows);
        using var r = new BinaryReader(new MemoryStream(b));
        Assert.Equal(5001, r.ReadInt32());
        var n = r.ReadUInt16();
        Assert.Equal(3 + 3, n);                // 3 PNJ posés + 3 vendeurs d'outils
        var list = Enumerable.Range(0, n).Select(_ => (Tid: r.ReadInt32(), Loc: r.ReadInt16(), Role: r.ReadByte())).ToList();
        Assert.Contains((53008, (short)1, (byte)1), list);
        Assert.Contains((53009, (short)2, (byte)2), list);
        Assert.Contains((53003, (short)22, (byte)2), list);
        Assert.Equal(3, list.Count(x => x.Role == 3));
    }

    [Fact]
    public void Build_EmptyTown_HasNoNpc()
    {
        var b = TownDetailHandler.Build(7, Array.Empty<FamilyBuildingItem>());
        Assert.Equal(6, b.Length);             // familyId + compteur 0
    }

    [Fact]
    public void Opcode_IsCustom9064() => Assert.Equal(9064, new CNE_CZ_ZoneServerTownDetail().Opcode);
}
