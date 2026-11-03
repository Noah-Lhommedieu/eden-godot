using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lot S1/S4/S7 — layouts wire des messages de cast, trésor
/// et deltas d'inventaire ajoutés dans
/// <c>scripts/protocol/ZoneCombatMessages.cs</c> (preuves :
/// ZoneMessages.g.cs serveur + handlers Eden.Zone).
/// </summary>
public class CombatCastTests
{
    // ---------- C→S ----------

    [Fact]
    public void SpellCastToLife_WireLayout()
    {
        // 9 : [u16 spell][u32 cible] — parse serveur ToUInt16@0/ToUInt32@2.
        var m = new ZoneSpellCastToLife
        {
            SpellId = unchecked((ushort)50022), TargetId = 0xFFFFA123,
        };
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        Assert.Equal(new byte[]
        {
            0x09, 0x00, 0x66, 0xC3, 0x23, 0xA1, 0xFF, 0xFF,
        }, w.ToArray());
        var back = new ZoneSpellCastToLife();
        back.Deserialize(new BinReader(w.ToArray()[2..]));
        Assert.Equal((ushort)50022, back.SpellId);
        Assert.Equal(0xFFFFA123u, back.TargetId);
    }

    [Fact]
    public void SpellBreak_EmptyBody()
    {
        // 13 : corps vide (g.cs — aucun champ).
        var m = new ZoneSpellBreak();
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        Assert.Equal(new byte[] { 0x0D, 0x00 }, w.ToArray());
    }

    [Fact]
    public void SpellCastToLocation_WireLayout()
    {
        // 16 : [u16 spell][i32][f32 x][f32 z] — 14B (parse réel
        // serveur : f32@6 et f32@10).
        var m = new ZoneSpellCastToLocation
        {
            SpellId = unchecked((ushort)50126), Target = 0, X = 12.5f, Z = -3.25f,
        };
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(16, b.Length);
        Assert.Equal(0x10, b[0]);
        Assert.Equal((ushort)50126, BitConverter.ToUInt16(b, 2));
        Assert.Equal(12.5f, BitConverter.ToSingle(b, 8));
        Assert.Equal(-3.25f, BitConverter.ToSingle(b, 12));
    }

    [Fact]
    public void SpellCastToNowhere_WireLayout()
    {
        // 17 : [u16 spell][f32 dir] — 6B (parse serveur HandleCastNowhere).
        var m = new ZoneSpellCastToNowhere { SpellId = unchecked((ushort)59558), Direction = 1.5f };
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(8, b.Length);
        Assert.Equal((ushort)59558, BitConverter.ToUInt16(b, 2));
        Assert.Equal(1.5f, BitConverter.ToSingle(b, 4));
    }

    [Fact]
    public void SpellCastToNearby_WireLayout()
    {
        // 59 : même forme [u16][f32].
        var m = new ZoneSpellCastToNearby { SpellId = unchecked((ushort)7), Value = 0.25f };
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(8, b.Length);
        Assert.Equal(0x3B, b[0]);
        Assert.Equal(0.25f, BitConverter.ToSingle(b, 4));
    }

    [Fact]
    public void TreasurePickup_WireLayout()
    {
        // 35 : [u32 id] — 4B.
        var m = new ZoneTreasurePickup { TreasureId = 0x00123456 };
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        Assert.Equal(new byte[]
            { 0x23, 0x00, 0x56, 0x34, 0x12, 0x00 }, w.ToArray());
    }

    // ---------- S→C ----------

    [Fact]
    public void ClientSpellCast_RoundTrip()
    {
        // 278 : [s16][i32 caster][i32 cible][u8][f32][f32] — 19B.
        var w = new BinWriter();
        w.WriteShort(unchecked((short)50126));
        w.WriteInt(200000002);
        w.WriteInt(unchecked((int)0xFFFFA001));
        w.WriteByte(1);
        w.WriteFloat(179.33f);
        w.WriteFloat(284.27f);
        var m = new ZoneClientSpellCast();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(unchecked((short)50126), m.SpellId);
        Assert.Equal(200000002, m.Caster);
        Assert.Equal(unchecked((int)0xFFFFA001), m.Target);
        Assert.Equal(1, m.Flag);
        Assert.Equal(179.33f, m.X);
        var w2 = new BinWriter();
        m.Serialize(w2);
        Assert.Equal(w.ToArray(), w2.ToArray());
    }

    [Fact]
    public void ClientSpellCastOK_RoundTrip()
    {
        // 279 : [s16 spell][i32 castDs][i32 cible][u8 ok] — 11B GOLDEN.
        var w = new BinWriter();
        w.WriteShort(unchecked((short)50022));
        w.WriteInt(12);                 // 1.2 s
        w.WriteInt(unchecked((int)0xFFFFA123));
        w.WriteByte(1);
        var m = new ZoneClientSpellCastOK();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(unchecked((short)50022), m.SpellId);
        Assert.Equal(12, m.CastTimeDs);
        Assert.Equal(unchecked((int)0xFFFFA123), m.Target);
        Assert.Equal(1, m.Ok);
        var w2 = new BinWriter();
        m.Serialize(w2);
        Assert.Equal(w.ToArray(), w2.ToArray());
    }

    [Fact]
    public void ClientSpellHitToLife_RoundTrip()
    {
        // 281 : [s16][i32 victime][i32 caster][i32 amount][i32 flags].
        var w = new BinWriter();
        w.WriteShort(unchecked((short)50028));
        w.WriteInt(200000002);
        w.WriteInt(200000002);
        w.WriteInt(317);
        w.WriteInt(SpellHitConst.Restore | SpellHitConst.Crit);
        var m = new ZoneClientSpellHitToLife();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(unchecked((short)50028), m.SpellId);
        Assert.Equal(317, m.Amount);
        Assert.Equal(SpellHitConst.Restore | SpellHitConst.Crit, m.Flags);
        var w2 = new BinWriter();
        m.Serialize(w2);
        Assert.Equal(w.ToArray(), w2.ToArray());
    }

    [Fact]
    public void ClientSpellBreak_RoundTrip()
    {
        // 287 : [i32 caster][s16 spell][u8 reason] — 7B.
        var w = new BinWriter();
        w.WriteInt(200000002);
        w.WriteShort(unchecked((short)50022));
        w.WriteByte(0);
        var m = new ZoneClientSpellBreak();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000002, m.Caster);
        Assert.Equal(unchecked((short)50022), m.SpellId);
        Assert.Equal(0, m.Reason);
        Assert.Equal(7, w.ToArray().Length);
    }

    [Fact]
    public void ClientSpellBlastOK_RoundTrip()
    {
        // 288 : [u32][i32][s16][i32][f32×3][i32][u16][u16][u8] — 35B.
        var m = new ZoneClientSpellBlastOK
        {
            Serial = 1, VictimHandle = 200000002, SpellId = unchecked((short)50022),
            Power = 20, X = 1f, Y = 2f, Z = 0f, BlastId = 3092,
            TargetCount = 0, TargetCount2 = 0, Flag = 0,
        };
        var w = new BinWriter();
        m.Serialize(w);
        var back = new ZoneClientSpellBlastOK();
        back.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(unchecked((short)50022), back.SpellId);
        Assert.Equal(3092, back.BlastId);
        Assert.Equal(35, w.ToArray().Length);
    }

    [Fact]
    public void ClientTreasurePickupOK_RoundTrip()
    {
        // 386 : [u32 char][u16 item][s16 count][s16] — 10B.
        var w = new BinWriter();
        w.WriteInt(200000002);
        w.WriteShort(1001);
        w.WriteShort(3);
        w.WriteShort(0);
        var m = new ZoneClientTreasurePickupOK();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000002u, m.CharId);
        Assert.Equal((ushort)1001, m.ItemId);
        Assert.Equal(3, m.Count);
    }

    [Fact]
    public void ClientCommandFailReturn_RoundTrip()
    {
        // 410 : [i32 ncid][i32 param].
        var w = new BinWriter();
        w.WriteInt(9);
        w.WriteInt(50022);
        var m = new ZoneClientCommandFailReturn();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(9, m.FailedOpcode);
        Assert.Equal(50022, m.Param);
    }

    [Fact]
    public void ClientReviveInfo_RoundTrip()
    {
        // 531 : [i32 area] — GOLDEN.
        var w = new BinWriter();
        w.WriteInt(7);
        var m = new ZoneClientReviveInfo();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(7, m.Area);
    }

    // ---------- Deltas sac (274/275/291) ----------

    [Fact]
    public void PutItem_ParsesHead()
    {
        // 274 : [u16 item][s16 bag][s16 slot][s16 count][s16 maxdur]…
        var w = new BinWriter();
        w.WriteShort(1001);   // itemId
        w.WriteShort(0);      // bag
        w.WriteShort(5);      // slot
        w.WriteShort(3);      // count
        w.WriteShort(100);    // maxdur
        w.WriteShort(0);      // str vide
        w.WriteByte(8);       // flags
        w.WriteBytes(new byte[3 + 4 + 34]);
        var m = new ZonePutItem();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal((ushort)1001, m.ItemId);
        Assert.Equal(0, m.Bag);
        Assert.Equal(5, m.Slot);
        Assert.Equal(3, m.Amount);
        Assert.Equal(100, m.MaxDurability);
    }

    [Fact]
    public void DropItem_WireLayout()
    {
        // 275 : [s16 contType][s16 slot][s16 contIdx].
        var w = new BinWriter();
        w.WriteShort(0); w.WriteShort(5); w.WriteShort(0);
        var m = new ZoneDropItem();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0, m.ContainerType);
        Assert.Equal(5, m.Slot);
        Assert.Equal(0, m.ContainerIndex);
    }

    [Fact]
    public void ModifyItemSlot_WireLayout()
    {
        // 291 : 6×s16 — contType, slot, count, maxDur, contIdx, f6.
        var w = new BinWriter();
        w.WriteShort(0); w.WriteShort(5); w.WriteShort(2);
        w.WriteShort(100); w.WriteShort(0); w.WriteShort(0);
        var m = new ZoneModifyItemSlot();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(5, m.Slot);
        Assert.Equal(2, m.Count);
    }

    [Fact]
    public void PlayerBag_DeltasApply()
    {
        // État partagé : 312 → 274 ajoute → 291 shrink → 275 vide.
        PlayerBag.ApplyEnterNode(new List<InventorySlot>
        {
            new(0, 0, 1001, 0, 0, 0, 2, 0),
        });
        PlayerBag.ApplyPutItem(new ZonePutItem
        {
            ItemId = 1002, Bag = 0, Slot = 1, Durability = 1,
            MaxDurability = 0,
        });
        Assert.Equal(2, PlayerBag.Items.Count);
        PlayerBag.ApplyModifySlot(new ZoneModifyItemSlot
        {
            ContainerType = 0, Slot = 0, Count = 1,
            MaxDurability = 0, ContainerIndex = 0,
        });
        Assert.Equal(1, PlayerBag.Items[0].Amount);
        PlayerBag.ApplyDropItem(new ZoneDropItem
        {
            ContainerType = 0, Slot = 1, ContainerIndex = 0,
        });
        Assert.Single(PlayerBag.Items);
        PlayerBag.Clear();
        Assert.Empty(PlayerBag.Items);
    }

    [Fact]
    public void PlayerBag_RaiseChangedFires()
    {
        // Hors moteur (pas de SceneTree), la levée reste synchrone :
        // le coalesce à une fois par frame n'existe que sous GODOT.
        var n = 0;
        void OnChanged() => n++;
        PlayerBag.Changed += OnChanged;
        try
        {
            PlayerBag.ApplyPutItem(new ZonePutItem
            {
                ItemId = 1003, Bag = 0, Slot = 2,
            });
            Assert.Equal(1, n);
            PlayerBag.RaiseChanged();
            Assert.Equal(2, n);
        }
        finally
        {
            PlayerBag.Changed -= OnChanged;
            PlayerBag.Clear();
        }
    }
}
