using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages de services/dialogue/vitals/équipement —
/// bytes RÉELS des goldens serveur
/// (<c>$EDEN/server/tests/Eden.Tests/Data/golden/ncid_*.json</c>,
/// extraits des captures natives zsniff).
/// </summary>
public class ZoneServicesTests
{
    private static byte[] Hex(string h) => Convert.FromHexString(h);

    // ---------- 285 DisplayDialogView (dialogue NPC) ----------

    [Fact]
    public void DisplayDialogView_Golden1()
    {
        // zsniff_cs : ctx=4003, 1 entrée (displayId -3, « 1009 », f0),
        // type 1, npcUid 0xc353.
        var m = new ZoneDisplayDialogView();
        m.Deserialize(new BinReader(Hex(
            "a30f00000100fdffffff04003130303900000153c30000")));
        Assert.Equal(4003, m.ContextId);
        Assert.Single(m.Entries);
        Assert.Equal(-3, m.Entries[0].DisplayId);
        Assert.Equal("1009", m.Entries[0].EntryName);
        Assert.Equal(0, m.Entries[0].Flag);
        Assert.Equal(1, m.ViewType);
        Assert.Equal(0xc353, m.NpcUid);
    }

    [Fact]
    public void DisplayDialogView_Golden2_DeuxEntrees()
    {
        // zsniff_natif : ctx=4004, 2 entrées, type 0, npcUid 0xc354.
        var m = new ZoneDisplayDialogView();
        m.Deserialize(new BinReader(Hex(
            "a40f00000200fdffffff0400313031320000ee110000000000000154c30000")));
        Assert.Equal(4004, m.ContextId);
        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(-3, m.Entries[0].DisplayId);
        Assert.Equal("1012", m.Entries[0].EntryName);
        Assert.Equal(0, m.Entries[0].Flag);
        Assert.Equal(0x11ee, m.Entries[1].DisplayId);
        Assert.Equal("", m.Entries[1].EntryName);
        Assert.Equal(0, m.Entries[1].Flag);
        Assert.Equal(1, m.ViewType);
        Assert.Equal(0xc354, m.NpcUid);
    }

    [Fact]
    public void ReplyDialog_GoldenEncode()
    {
        // golden D : « 0000 » = option index 0.
        var w = new BinWriter();
        new ZoneReplyDialog { DialogId = 0 }.Serialize(w);
        Assert.Equal(Hex("0000"), w.ToArray());
        w = new BinWriter();
        new ZoneReplyDialog { DialogId = 2 }.Serialize(w);
        Assert.Equal(Hex("0200"), w.ToArray());
    }

    // ---------- 311 EnterNodeEquipAndSpell ----------

    [Fact]
    public void EquipAndSpell_Golden_10Items23Sorts()
    {
        // zsniff_cs 562B : conteneur 0 (mask 0xb4 → 4 items), conteneur 1
        // vide, conteneur 2 (mask 0x34 → 3 items), conteneur 3 (mask
        // 0x34 → 3), conteneurs 4-5 vides, puis 23 sorts.
        var (equipped, spells) = EquipAndSpellParser.Parse(Hex(Golden311));
        Assert.Equal(10, equipped.Count);
        // Conteneur 0 : locs 2,4,5,7 (bits de 0xb4).
        var c0 = equipped.Where(e => e.Container == 0).ToList();
        Assert.Equal(new[] { 2, 4, 5, 7 }, c0.Select(e => e.Loc).ToArray());
        Assert.Equal(0x7cc3, c0[0].ItemId);      // 31939 — dur 120/120
        Assert.Equal(120, c0[0].Durability);
        Assert.Equal(0x48, c0[0].Flags);         // socketé
        // Conteneur 2 : locs 2,4,5 — items 0x7cf5/6/7 dur 80-50.
        var c2 = equipped.Where(e => e.Container == 2).ToList();
        Assert.Equal(3, c2.Count);
        Assert.Equal(0x7cf5, c2[0].ItemId);
        // Conteneur 3 : items 0x5d31/2/3.
        var c3 = equipped.Where(e => e.Container == 3).ToList();
        Assert.Equal(3, c3.Count);
        Assert.Equal(0x5d31, c3[0].ItemId);
        // Sorts : 23 connus, spellmaster u16 ≥ 50000, niveau 1.
        Assert.Equal(23, spells.Count);
        Assert.Equal(0xc351, spells[0].SpellMaster);
        Assert.Equal(1, spells[0].Level);
        // Les ids spell dépassent 32767 (u16) — 0xe6dd/0xe6de en queue.
        Assert.Contains(spells, s => s.SpellMaster == 0xe6dd);
        Assert.Contains(spells, s => s.SpellMaster == 0xe6de);
    }

    // ---------- 358 GetStorageData (banque) ----------

    [Fact]
    public void GetStorageData_Golden_BanqueVide()
    {
        // zsniff_cs : type -1, 1 conteneur cap 32, bitmap 4B vide.
        var m = new ZoneClientGetStorageData();
        m.Deserialize(new BinReader(Hex("ffff01200000040000000000")));
        Assert.Equal(-1, m.DataType);
        Assert.Single(m.Containers);
        Assert.Equal(32, m.Containers[0].Capacity);
        Assert.Empty(m.Containers[0].Items);
    }

    // ---------- 305/306 mort & respawn ----------

    [Fact]
    public void Die_Golden()
    {
        // zsniff_cs : mob 0xfffff4fa tué par char 0x0bebc204.
        var m = new ZoneDie();
        m.Deserialize(new BinReader(Hex("faf4ffff04c2eb0b00")));
        Assert.Equal(0xfffff4fau, m.Handle);
        Assert.Equal(0x0bebc204u, m.Other);
        Assert.Equal(0, m.Flag);
    }

    [Fact]
    public void Revive_RoundTrip()
    {
        var w = new BinWriter();
        new ZoneRevive { Handle = 0x0bebc204, Field2 = 0, ReviveType = 1 }
            .Serialize(w);
        var m = new ZoneRevive();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0x0bebc204u, m.Handle);
        Assert.Equal(1, m.ReviveType);
    }

    // ---------- 492/493/328 stats ----------

    [Fact]
    public void BaseAttrRefresh_Golden()
    {
        // zsniff_cs 32B : stats 12,10,11,10,10 hp=60 mp=50 walk 7.0 run 11.0.
        var m = new ZoneBaseAttrRefresh();
        m.Deserialize(new BinReader(Hex(
            "0c000a000b000a000a003c000000320000000000e04000006041670196009600")));
        Assert.Equal(new short[] { 12, 10, 11, 10, 10 }, m.Stats);
        Assert.Equal(60, m.Hp);
        Assert.Equal(50, m.Mp);
        Assert.Equal(7f, m.Walk);
        Assert.Equal(14f, m.Run);
    }

    [Fact]
    public void AttrLevelRefresh_Golden_14Floats()
    {
        var m = new ZoneAttrLevelRefresh();
        m.Deserialize(new BinReader(Hex(
            "00002041000000410000204100006041333333410000404100002041" +
            "00000041000020410000604133333341000040410000e04000006041")));
        Assert.Equal(14, m.Fields.Length);
        Assert.Equal(10f, m.Fields[0]);          // 0x41200000
        Assert.Equal(8f, m.Fields[1]);           // 0x41000000
        Assert.Equal(7f, m.Fields[12]);          // 0x40e00000
        Assert.Equal(14f, m.Fields[13]);         // 0x41600000
    }

    [Fact]
    public void ModifyAttr_Golden()
    {
        // zsniff_cs : handle char, attr 1, val -17, i64 0.
        var m = new ZoneModifyAttr();
        m.Deserialize(new BinReader(Hex(
            "04c2eb0b0100efffffff0000000000000000")));
        Assert.Equal(0x0bebc204, m.Handle);
        Assert.Equal(1, m.AttrId);
        Assert.Equal(-17, m.Value);
        Assert.Equal(0, m.Value64);
    }

    // ---------- 296 mapping PlayerStats ----------

    [Fact]
    public void AttrRefresh296_MapStats()
    {
        // golden zsniff_cs : stats 12,10,14,14,10 ; bloc i32 =
        // [0][MaxMP=180][P-ATK=344][M-ATK=290] ; DEF=541 …
        var m = new ZoneAttrRefresh();
        m.Deserialize(new BinReader(Hex(
            "0c000a000e000e000a00000000000000000000000000000000000000" +
            "00000000b400000058010000220100000000a040000020411d020000" +
            "200359032e03dc0567039600f6039600cdcc16410000000000000000" +
            "5b6b514200000000e79c8b41")));
        PlayerVitals.ApplyAttrRefresh(m);
        var st = PlayerVitals.TotalStats!;
        Assert.Equal(12, st.Str);
        Assert.Equal(14, st.Agi);
        Assert.Equal(180, st.MaxMp);             // Fields15to18[1]
        Assert.Equal(344, st.PhysicalAttack);    // Fields15to18[2]
        Assert.Equal(290, st.MagicAttack);       // Fields15to18[3]
        Assert.Equal(541, st.PhysicalDefence);   // Field21
        Assert.Equal(9, st.Resists.Length);
    }

    // ---------- 282/276 équipement ----------

    [Fact]
    public void UpdateEquipment_2Locs()
    {
        // Corps construit selon BuildUpdateEquipment : charId, mask
        // locs 2+7, records minimaux (itemId+flags+str vide+mask 0+16B),
        // 0 hidden, queue s16+s8.
        var w = new BinWriter();
        w.WriteInt(0x0bebc204);
        w.WriteUInt((1u << 2) | (1u << 7));
        w.WriteShort(31939); w.WriteByte(0x48); w.WriteShort(0);
        w.WriteBytes(stackalloc byte[3]); w.WriteBytes(stackalloc byte[16]);
        w.WriteShort(0x7cf5); w.WriteByte(0x08); w.WriteShort(0);
        w.WriteBytes(stackalloc byte[3]); w.WriteBytes(stackalloc byte[16]);
        w.WriteInt(0); w.WriteShort(0); w.WriteByte(0);
        var m = new ZoneUpdateEquipmentView();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0x0bebc204, m.CharId);
        Assert.Equal(2, m.Items.Count);
        Assert.Equal(new[] { 2, 7 },
            m.Items.Select(i => i.Loc).ToArray());
        Assert.Equal(31939, m.Items[0].ItemId);
        Assert.Equal(0x7cf5, m.Items[1].ItemId);
        Assert.Empty(m.Hidden);
    }

    [Fact]
    public void ChangeEquip_SlotVide()
    {
        var w = new BinWriter();
        w.WriteInt(0x0bebc204); w.WriteShort(7); w.WriteShort(0);
        w.WriteShort(0); w.WriteByte(3);
        var m = new ZoneChangeEquip();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(7, m.Slot);
        Assert.Equal(0, m.ItemId);
        Assert.Null(m.Item);
    }

    // ---------- C→S encodes ----------

    [Fact]
    public void TalkToNpc_GoldenEncode()
    {
        // golden D : « 0d93c988 » = handle NPC 0x88c9930d.
        var w = new BinWriter();
        new ZoneTalkToNpc { NpcId = unchecked((int)0x88c9930d) }
            .Serialize(w);
        Assert.Equal(Hex("0d93c988"), w.ToArray());
    }

    [Fact]
    public void GetStorageData_GoldenEncode()
    {
        // golden D : « ffff » = -1.
        var w = new BinWriter();
        new ZoneGetStorageData { Value = -1 }.Serialize(w);
        Assert.Equal(Hex("ffff"), w.ToArray());
    }

    [Fact]
    public void BuyItems_Wire()
    {
        // wire natif ShopHandlers : [u16 storeSlot][u16 count][u8 0].
        var w = new BinWriter();
        new ZoneBuyItems { ShopIndex = 3, ItemIndex = 2 }.Serialize(w);
        Assert.Equal(Hex("03000200 00".Replace(" ", "")), w.ToArray());
    }

    [Fact]
    public void SellItems_Wire_Entrelace()
    {
        // [i32 1][u16 slot=5][u16 count=2][u16 container=1].
        var m = new ZoneSellItems();
        m.Items.Add((5, 2, 1));
        var w = new BinWriter();
        m.Serialize(w);
        Assert.Equal(Hex("01000000 0500 0200 0100".Replace(" ", "")),
            w.ToArray());
    }

    [Fact]
    public void Transport_Wire() =>
        Assert.Equal(Hex("0300"), RoundTripBytes(new ZoneTransport
        { Value = 3 }));

    [Fact]
    public void CheckNodeExit_Wire() =>
        Assert.Equal(Hex("0200 0a00".Replace(" ", "")),
            RoundTripBytes(new ZoneCheckNodeExit
            { NodeId = 2, PortalId = 10 }));

    [Fact]
    public void UpdateEquipmentReq_Wire() =>
        Assert.Equal(Hex("84000000".Replace(" ", "")),
            RoundTripBytes(new ZoneUpdateEquipment
            { UpdateFlags = 0x84 }));

    // ---------- états HUD ----------

    [Fact]
    public void DialogueState_PublieOptions()
    {
        var fired = 0;
        void On() => fired++;
        DialogueState.Changed += On;
        try
        {
            var m = new ZoneDisplayDialogView();
            m.Deserialize(new BinReader(Hex(
                "a30f00000100fdffffff04003130303900000153c30000")));
            DialogueState.OnDialogView(m);
            Assert.True(DialogueState.IsOpen);
            Assert.Equal(0xc353, DialogueState.NpcUid);
            Assert.Single(DialogueState.Options);
            Assert.Equal(1, fired);
            DialogueState.Close();
            Assert.False(DialogueState.IsOpen);
        }
        finally { DialogueState.Changed -= On; }
    }

    [Fact]
    public void PlayerVitals_UpdateAttr280()
    {
        PlayerVitals.SetVitals(10, 10, 10, 10);
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
        { Handle = 1, AttrId = 0, Current = 42, Max = 42 });
        Assert.Equal(42, PlayerVitals.Hp);
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
        { Handle = 1, AttrId = 0x11, Current = 200, Max = 200 });
        Assert.Equal(200, PlayerVitals.MaxHp);
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
        { Handle = 1, AttrId = 0x12, Current = 90, Max = 0 });
        Assert.Equal(90, PlayerVitals.MaxMp);
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
        { Handle = 1, AttrId = 0x02, Current = 1234 });
        Assert.Equal(1234, PlayerVitals.Gold);
    }

    [Fact]
    public void PlayerEquipment_ChangeEquip()
    {
        PlayerEquipment.ApplyEnterNode(new List<EquippedItem>
        {
            new(7, 0, 31939, 0x48, 0, 0),
        });
        Assert.Equal(31939, PlayerEquipment.Worn[7]);
        // 276 self : slot 7 → nouvel item.
        PlayerEquipment.ApplyChangeEquip(new ZoneChangeEquip
        { CharId = 0x0bebc204, Slot = 7, ItemId = 0x7cf5 },
            0x0bebc204);
        Assert.Equal(0x7cf5, PlayerEquipment.Worn[7]);
        // ItemId=0 → slot vidé.
        PlayerEquipment.ApplyChangeEquip(new ZoneChangeEquip
        { CharId = 0x0bebc204, Slot = 7, ItemId = 0 }, 0x0bebc204);
        Assert.False(PlayerEquipment.Worn.ContainsKey(7));
    }

    private static byte[] RoundTripBytes(NetMessage m)
    {
        var w = new BinWriter();
        m.Serialize(w);
        return w.ToArray();
    }

    /// <summary>Golden ncid_311.json case 1 (zsniff_cs_entry_success) —
    /// 562 octets : 4+0+3+3 items portés, 23 sorts.</summary>
    private const string Golden311 =
        "0600b4000000c37c780078000000480000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000c47c4b004b00000008000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000c57c4b004b00000048000000000000000000000000000000000000000000" +
        "000000000000000000000000000000009b7c4b004b0000004800000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000100" +
        "0000000234000000f57c50005000000048000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000f67c3200320000004800" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "00000000f77c3200320000004800000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000334000000315d50005000000048" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000325d32003200000048000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000335d3200320000004800000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0004000000000500000000170051c3015bc30160c30165c3016ac3016fc30176" +
        "c301e6c30119c4011ac40120c40125c4012bc40131c40137c40138c40139c401" +
        "5dc40132c50133c50138c701dde601dee601";
}
