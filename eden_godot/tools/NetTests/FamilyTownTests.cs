using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages de ville de guilde (lot B6) : 502/503 en
/// vecteurs parallèles <c>[u16 n][elems]</c>, 174 query.
/// Wire prouvé côté serveur : FamilyPackets.FamilyTownQuery /
/// FamilyTownMemberQuery (ctor natif @0x8148b90).
/// </summary>
public class FamilyTownTests
{
    [Fact]
    public void TownQuery_502_VecteursParalleles()
    {
        // 2 familles : f1 (2 bâtiments), f2 (1 bâtiment).
        var w = new BinWriter();
        w.WriteShort(2);                        // vec0 ids
        w.WriteInt(10); w.WriteInt(20);
        w.WriteShort(2);                        // vec1 levels
        w.WriteShort(3); w.WriteShort(5);
        w.WriteShort(2);                        // vec2 names
        w.WriteString("Alpha"); w.WriteString("Beta");
        w.WriteShort(2);                        // vec3 leaders
        w.WriteString("ChefA"); w.WriteString("ChefB");
        w.WriteShort(2);                        // vec4 exp
        w.WriteInt(1000); w.WriteInt(2500);
        w.WriteShort(2);                        // vec5 tax
        w.WriteShort(4); w.WriteShort(7);
        w.WriteShort(2);                        // vec6 members
        w.WriteInt(12); w.WriteInt(30);
        w.WriteShort(2);                        // vec7 house_open
        w.WriteShort(1); w.WriteShort(1);
        w.WriteShort(2);                        // vec8 nb bât/fam
        w.WriteInt(2); w.WriteInt(1);
        w.WriteShort(3);                        // vec9 locs (total 3)
        w.WriteShort(1); w.WriteShort(2); w.WriteShort(3);
        w.WriteShort(3);                        // vec10 item_tids
        w.WriteInt(40001); w.WriteInt(40002); w.WriteInt(40003);
        w.WriteShort(3);                        // vec11 tids
        w.WriteInt(101); w.WriteInt(102); w.WriteInt(103);
        w.WriteShort(3);                        // vec12 item_types
        w.WriteShort(4); w.WriteShort(5); w.WriteShort(6);
        w.WriteShort(3);                        // vec13 durability
        w.WriteShort(0); w.WriteShort(0); w.WriteShort(0);
        w.WriteShort(3);                        // vec14 bld level
        w.WriteShort(2); w.WriteShort(3); w.WriteShort(1);
        w.WriteShort(3);                        // vec15 bld exp
        w.WriteInt(50); w.WriteInt(60); w.WriteInt(70);
        w.WriteShort(3);                        // vec16 due_date
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);

        var m = new ZoneClientFamilyTownQuery();
        m.Deserialize(new BinReader(w.ToArray()));

        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(10, m.Entries[0].FamilyId);
        Assert.Equal("Alpha", m.Entries[0].Name);
        Assert.Equal("ChefA", m.Entries[0].Leader);
        Assert.Equal(3, m.Entries[0].Level);
        Assert.Equal(4, m.Entries[0].BusinessTax);
        Assert.Equal(12, m.Entries[0].Members);
        Assert.Equal(2, m.Entries[0].Buildings.Count);
        Assert.Equal(40001, m.Entries[0].Buildings[0].ItemTid);
        Assert.Equal(2, m.Entries[0].Buildings[0].Level);
        Assert.Single(m.Entries[1].Buildings);
        Assert.Equal(40003, m.Entries[1].Buildings[0].ItemTid);
    }

    [Fact]
    public void TownMemberQuery_503_Vecteurs()
    {
        var w = new BinWriter();
        w.WriteShort(2);                        // charIds
        w.WriteInt(100); w.WriteInt(200);
        w.WriteShort(2);                        // names
        w.WriteString("Alice"); w.WriteString("Bob");
        w.WriteShort(2);                        // levels
        w.WriteShort(30); w.WriteShort(45);
        w.WriteShort(2);                        // ranks
        w.WriteByte(1); w.WriteByte(0);
        w.WriteShort(2);                        // nodeIds
        w.WriteShort(7); w.WriteShort(7);
        w.WriteShort(1);                        // titres famille
        w.WriteString("Maison Alpha");

        var m = new ZoneClientFamilyTownMemberQuery();
        m.Deserialize(new BinReader(w.ToArray()));

        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(100, m.Entries[0].CharacterId);
        Assert.Equal("Alice", m.Entries[0].Name);
        Assert.Equal(30, m.Entries[0].Level);
        Assert.Equal((byte)1, m.Entries[0].Rank);
        Assert.Equal(new[] { "Maison Alpha" }, m.Titles);
    }

    [Fact]
    public void TownQueryC2S_174_Layout()
    {
        // [s16 min][s16 max][i32 mask][str name]
        var w = new BinWriter();
        new ZoneServerFamilyTownQuery
        {
            Field1 = 10, Field2 = 60, Field3 = 0x3f,
            Field4 = "forge",
        }.Serialize(w);
        var m = new ZoneServerFamilyTownQuery();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(10, m.Field1);
        Assert.Equal(60, m.Field2);
        Assert.Equal(0x3f, m.Field3);
        Assert.Equal("forge", m.Field4);
    }
}

public class TerritoryTests
{
    /// <summary>515 — 8 vecteurs parallèles (lot C4) : 2 territoires,
    /// 1 créneau.</summary>
    [Fact]
    public void TerritoryQuery_515_VecteursParalleles()
    {
        var w = new BinWriter();
        w.WriteShort(0);                        // pad
        w.WriteShort(2);                        // vec1 ids
        w.WriteInt(7); w.WriteInt(9);
        w.WriteShort(1);                        // vec2 schedules
        w.WriteInt(1); w.WriteInt(1700000000); w.WriteInt(1700003600);
        w.WriteShort(2);                        // vec3 own_family_id
        w.WriteInt(42); w.WriteInt(0);
        w.WriteShort(2);                        // vec4 names
        w.WriteString("Aurea"); w.WriteString("Utopia");
        w.WriteShort(2);                        // vec5 owner levels
        w.WriteShort(6); w.WriteShort(0);
        w.WriteShort(2);                        // vec6 time
        w.WriteInt(0); w.WriteInt(3600);
        w.WriteShort(2);                        // vec7 state
        w.WriteShort(1); w.WriteShort(0);
        w.WriteShort(2);                        // vec8 flags
        w.WriteByte(1); w.WriteByte(0);

        var m = new ZoneClientTerritoryQuery();
        m.Deserialize(new BinReader(w.ToArray()));

        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(7, m.Entries[0].Id);
        Assert.Equal(42, m.Entries[0].OwnFamilyId);
        Assert.Equal("Aurea", m.Entries[0].Name);
        Assert.Equal(6, m.Entries[0].OwnerLevel);
        Assert.Equal(1, m.Entries[0].State);
        Assert.Equal(9, m.Entries[1].Id);
        Assert.Equal(0, m.Entries[1].OwnFamilyId);
        Assert.Single(m.Schedules);
        Assert.Equal(1700003600, m.Schedules[0].End);
    }
}
