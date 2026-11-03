using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lot S6+S8 — classes/races + ordres de travail :
/// codecs wire prouvés (serveur .g.cs + WorkPackets.cs + captures),
/// tables client (C_Races, C_ClassRankBuff, C_FamilyCollect,
/// c_familycombine) et spec corps race↔code (CharCreateBodySpec).
/// </summary>
public class ZoneLotS6S8Tests
{
    private static byte[] RoundTrip(NetMessage m)
    {
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        return w.ToArray();
    }

    private static T Parse<T>(byte[] payload) where T : NetMessage, new()
    {
        var m = new T();
        var r = new BinReader(payload);
        r.ReadUShort(); // NCID
        m.Deserialize(r);
        return m;
    }

    // ------------------------- classes (S6) -------------------------

    [Fact]
    public void ChangeClass_WireIsSingleByte()
    {
        // CNE_CZ_ZoneServerChangeClass — désasm Deserialize : [u8].
        var bytes = RoundTrip(new ZoneChangeClass { ClassId = 19 });
        Assert.Equal(new byte[] { 0xA8, 0x00, 0x13 }, bytes);
        Assert.Equal((byte)19,
            Parse<ZoneChangeClass>(bytes).ClassId);
    }

    [Fact]
    public void PlayerClassList_ParsesGoldenLayout()
    {
        // [u8 ff][u8 0][u16 2] puis entrées 26B :
        // [u8 classId][u8 lv][u8 style][i32 exp][i32 points]
        // [u8 aa][u16 lic×7] — format capture zsniff 17:22:16.
        var w = new BinWriter();
        w.WriteByte(0xff); w.WriteByte(0);
        w.WriteShort(2);
        w.WriteByte(1); w.WriteByte(14); w.WriteByte(0);
        w.WriteInt(90); w.WriteInt(7); w.WriteByte(2);
        for (var i = 0; i < 7; i++) w.WriteShort(0);
        w.WriteByte(19); w.WriteByte(0); w.WriteByte(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteByte(1);
        for (var i = 0; i < 7; i++) w.WriteShort(0);
        var body = w.ToArray();
        var msg = new ZonePlayerClassList();
        msg.Deserialize(new BinReader(body));
        Assert.Equal(0xff, msg.HeaderFlag1);
        Assert.Equal(2, msg.Entries.Count);
        Assert.Equal(1, msg.Entries[0].ClassId);
        Assert.Equal(14, msg.Entries[0].ClassLevel);
        Assert.Equal(90, msg.Entries[0].ClassExp);
        Assert.Equal(2, msg.Entries[0].AaPoint);
        Assert.Equal(19, msg.Entries[1].ClassId);
        // Round-trip octet par octet.
        var re = RoundTrip(msg);
        Assert.Equal(body, re[2..]);
    }

    [Fact]
    public void ChangeClassShortcut_ParsesMaskBars()
    {
        // [u8 1][u16 mask 0b101][i32 A][i32 B][u16 1][i32 extra]
        var w = new BinWriter();
        w.WriteByte(1);
        w.WriteShort(0b0000_0000_0000_0101);
        w.WriteInt(111); w.WriteInt(222);
        w.WriteShort(1); w.WriteInt(42);
        var msg = new ZoneChangeClassShortcut();
        msg.Deserialize(new BinReader(w.ToArray()));
        Assert.Single(msg.Bars);
        Assert.Equal(111, msg.Bars[0][0]);
        Assert.Equal(-1, msg.Bars[0][1]);
        Assert.Equal(222, msg.Bars[0][2]);
        Assert.Equal(new[] { 42 }, msg.Extras);
        Assert.Equal(w.ToArray(), RoundTrip(msg)[2..]);
    }

    // ------------------------- corps/race (S6) -------------------------

    [Fact]
    public void BodySpec_ProvenCodes()
    {
        // Codes natifs acceptés : {0,1,2,4,6,8,9} (masque 0x317|0x40).
        Assert.Equal(0, CharCreateBodySpec.BodyCode(1, false)); // g ♀
        Assert.Equal(1, CharCreateBodySpec.BodyCode(1, true));  // b ♂
        Assert.Equal(2, CharCreateBodySpec.BodyCode(2, false)); // Zumi
        Assert.Equal(2, CharCreateBodySpec.BodyCode(2, true));  // unique
        Assert.Equal(4, CharCreateBodySpec.BodyCode(3, true));  // Anuran
        Assert.Equal(6, CharCreateBodySpec.BodyCode(4, false)); // Ursun
        Assert.Equal(8, CharCreateBodySpec.BodyCode(5, false)); // l ♀
        Assert.Equal(9, CharCreateBodySpec.BodyCode(5, true));  // c ♂
    }

    [Fact]
    public void BodySpec_BoundsUrsunTighter()
    {
        var u = CharCreateBodySpec.BoundsFor(6);
        Assert.Equal(7, u.HairMax);
        Assert.Equal(1, u.FaceMax);
        Assert.Equal(9, u.HairColorMax);
        Assert.Equal(10, u.SkinMax);
        var h = CharCreateBodySpec.BoundsFor(1);
        Assert.Equal(10, h.HairMax);
        Assert.Equal(4, h.FaceMax);
        Assert.Equal(14, h.HairColorMax);
        Assert.Equal(9, h.SkinMax);
        Assert.Equal(14, h.EyesMax);
    }

    [Fact]
    public void BodyCode_LandsInBlobNibble()
    {
        // app[28]>>4 = code corps — encodage AppearanceCodec.
        var blob = AppearanceCodec.Encode(new AppearanceCodec.Appearance(
            BodyCode: 2, HairStyle: 3, FaceStyle: 1, HairColor: 4,
            SkinColor: 2, EyesColor: 5, EyesColor2: 0));
        Assert.Equal(2, blob[28] >> 4);
        Assert.Equal(2, AppearanceCodec.Decode(blob).BodyCode);
    }

    // ------------------------- works (S8) -------------------------

    [Fact]
    public void EnterWorks_ParsesLayout()
    {
        // [i32 npcUid][s16 workType][s16 bld][s16 ?][u16 0]
        var w = new BinWriter();
        w.WriteInt(unchecked((int)0x88C81234)); w.WriteShort(12);
        w.WriteShort(0); w.WriteShort(0); w.WriteShort(0);
        var m = new ZoneEnterWorks();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(unchecked((int)0x88C81234), m.NpcUid);
        Assert.Equal(12, (int)m.WorkType);
    }

    [Fact]
    public void PlayerWork_WireLayout()
    {
        // C→S [u32 workId][s16 catBag][s16 catSlot][s16 -1][s16 -1]
        var bytes = RoundTrip(new ZonePlayerWork
        {
            WorkId = 21, CatalystBag = 0, CatalystSlot = 7,
        });
        Assert.Equal(new byte[]
        {
            0xAA, 0x00,                         // NCID 170
            0x15, 0x00, 0x00, 0x00,             // u32 workId
            0x00, 0x00,                         // s16 bag
            0x07, 0x00,                         // s16 slot
            0xFF, 0xFF, 0xFF, 0xFF,             // boost -1,-1
        }, bytes);
    }

    [Fact]
    public void WorkResultOK_WireIsShort()
    {
        var bytes = RoundTrip(new ZonePlayerWorkResultOK { WorkNum = 3 });
        Assert.Equal(new byte[] { 0xAB, 0x00, 0x03, 0x00 }, bytes);
    }

    [Fact]
    public void WorkUpdate_ParsesAggregatedOrders()
    {
        // [u16 1][s16 num][s16 type][i32 workId][i32 event][str fm]
        var w = new BinWriter();
        w.WriteShort(1);
        w.WriteShort(4); w.WriteShort(12); w.WriteInt(21);
        w.WriteInt(204); w.WriteString("guilde");
        var m = new ZonePlayerWorkUpdate();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Single(m.Orders);
        Assert.Equal(4, m.Orders[0].WorkNum);
        Assert.Equal(21, m.Orders[0].WorkId);
        Assert.Equal("guilde", m.Orders[0].FmName);
        Assert.Equal(w.ToArray(), RoundTrip(m)[2..]);
    }

    [Fact]
    public void WorkResult_ParsesProducts()
    {
        // [i32 workId][s16 type][s16 num][str fm][u16 1][u16 13][i32×13]
        var w = new BinWriter();
        w.WriteInt(21); w.WriteShort(15); w.WriteShort(2);
        w.WriteString("");
        w.WriteShort(1);
        w.WriteShort(13);
        w.WriteInt(30033); w.WriteInt(19); w.WriteInt(0); w.WriteInt(0);
        for (var i = 0; i < 9; i++) w.WriteInt(0);
        var m = new ZonePlayerWorkResult();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.False(m.Failed);
        Assert.Single(m.Products);
        Assert.Equal(30033, m.Products[0].ItemId);
        Assert.Equal(19, m.Products[0].Amount);
    }

    [Fact]
    public void WorkResult_NegativeIdMeansFailure()
    {
        var w = new BinWriter();
        w.WriteInt(-21); w.WriteShort(15); w.WriteShort(2);
        w.WriteString(""); w.WriteShort(0);
        var m = new ZonePlayerWorkResult();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.True(m.Failed);
        Assert.Empty(m.Products);
    }

    [Fact]
    public void WorkResultEnd_WireIsShort()
    {
        var w = new BinWriter(); w.WriteShort(7);
        var m = new ZonePlayerWorkResultEnd();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(7, m.WorkNum);
    }

    [Fact]
    public void StrengthenCasting_Parses()
    {
        var w = new BinWriter();
        w.WriteShort(12); w.WriteShort(100); w.WriteShort(0);
        var m = new ZoneStrengthenCasting();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(12, m.WorkType);
        Assert.Equal(100, m.DurationUnits);
    }

    // ------------------------- tables -------------------------

    [Fact]
    public void CraftTable_CollectParses()
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            $"fc_{Guid.NewGuid():N}.ini");
        File.WriteAllText(tmp,
            "|V.2|47|\r\n" +
            "21|0|30136|PlanMine|1|1|1|2|2723|0|20|100|1|122|" +
            "30001||10|100|" + new string('|', 28) + "\r\n");
        try
        {
            var t = CraftTable.LoadCollect(tmp);
            Assert.Single(t);
            var r = t[21];
            Assert.Equal(0, r.Category);
            Assert.Equal(30136, r.CatalystItemId);
            Assert.Equal("PlanMine", r.Name);
            Assert.Equal(2723, r.Exp);
            Assert.Equal(20, r.DurationMin);
            Assert.Equal(122, r.Gold);
            Assert.Single(r.Products);
            Assert.Equal(30001, r.Products[0].ItemId);
            Assert.Equal(10, r.Products[0].Qty);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CraftTable_CombineParses()
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            $"cb_{Guid.NewGuid():N}.ini");
        File.WriteAllText(tmp,
            "|V.1|44|\r\n" +
            "21|3|3|5|9|1|50|100|1|1513|30937|Amulette|1|100||" +
            "31736|Parchemin|1|30033|Poussière|19|30040|Gemme|6|" +
            new string('|', 15) + "\r\n");
        try
        {
            var t = CraftTable.LoadCombine(tmp);
            Assert.Single(t);
            var r = t[21];
            Assert.Equal(3, r.Category);
            Assert.True(r.Instant);
            Assert.Equal(1513, r.Gold);
            Assert.Equal(30937, r.ProductItemId);
            Assert.Equal("Amulette", r.ProductName);
            Assert.Equal(100, r.SuccessRate);
            Assert.Equal(3, r.Materials.Count);
            Assert.Equal(31736, r.Materials[0].ItemId);
            Assert.Equal(19, r.Materials[1].Qty);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RaceTable_ParsesCRaces()
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            $"cr_{Guid.NewGuid():N}.ini");
        File.WriteAllText(tmp, "V.1,4,\r\n1,人,12,,\r\n",
            EdenEncoding.Big5);
        try
        {
            var t = RaceTable.LoadRaces(tmp);
            Assert.Single(t);
            Assert.Equal("人", t[1].Name);
            Assert.Equal(12, t[1].Param);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ClassRankTable_ParsesSixFieldGroups()
    {
        // |V.3|134| : col0 classId, col1 bis, puis groupes de 6
        // {seuil,?,score,?,enchant,?} — miroir parser serveur.
        var tmp = Path.Combine(Path.GetTempPath(),
            $"crb_{Guid.NewGuid():N}.ini");
        File.WriteAllText(tmp,
            "|V.3|134|\r\n1|1|" +
            "41||33|||45492|74||48|||45493|" +
            new string('|', 100) + "\r\n");
        try
        {
            var t = ClassRankTable.Load(tmp);
            Assert.Single(t);
            Assert.Equal(1, t[1].ClassId);
            Assert.Equal(2, t[1].Tiers.Count);
            Assert.Equal(45492, t[1].Tiers[0].EnchantId);
            Assert.Equal(45493, t[1].Tiers[1].EnchantId);
            Assert.Equal(1, t[1].Tiers[0].Rank);
        }
        finally { File.Delete(tmp); }
    }
}
