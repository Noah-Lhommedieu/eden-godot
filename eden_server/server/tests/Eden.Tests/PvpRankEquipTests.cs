using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>Wire du 571 `CNC_CZ_ClientPVPRankLookEquip` — preuve :
/// lapis::CNC_CZ_ClientPVPRankLookEquip::Serialize @0x8205af0 et
/// Execute @0x826edb0 (zone/decompiled.c).</summary>
public class PvpRankEquipTests
{
    /// <summary>Header + vecs + masks — positions exactes du
    /// sérialiseur natif.</summary>
    [Fact]
    public void Build571_HeaderVecsAndMasks()
    {
        var app = new byte[0x20];
        app[0] = 0xAA;
        var set0 = new[]
        {
            new ItemEntity { Slot = 2, ItemId = 1234, Durability = 80,
                MaxDurability = 100, EnchantLevel = 5 },
        };
        var set1 = new[]
        {
            new ItemEntity { Slot = 0, ItemId = 5678,
                MagicBoxEnergy = 9, MagicBoxStarLevel = 2 },
        };
        var body = PvpRankEquipPackets.Build571(
            196612, "Test", classId: 3, charLevel: 50,
            classLevel: 42, heroFeature: 0, classLevelScore: 903,
            pageFlag: 0, appearance: app, set0, set1);

        var p = 0;
        Assert.Equal(196612, BitConverter.ToInt32(body, p)); p += 4;
        var nl = BitConverter.ToUInt16(body, p); p += 2;
        Assert.Equal(4, nl);
        Assert.Equal("Test",
            System.Text.Encoding.ASCII.GetString(body, p, nl));
        p += nl;
        Assert.Equal(3, BitConverter.ToUInt16(body, p)); p += 2; // class
        Assert.Equal(50, BitConverter.ToUInt16(body, p)); p += 2; // lv
        Assert.Equal(42, BitConverter.ToUInt16(body, p)); p += 2; // clv
        Assert.Equal(0, BitConverter.ToUInt16(body, p)); p += 2;  // hero
        Assert.Equal(903, BitConverter.ToInt32(body, p)); p += 4; // score
        Assert.Equal(0, body[p]); p += 1;                         // page
        Assert.Equal(0xAA, body[p]); p += 0x20;                   // app 32B
        // Vec clone+0xc — slots 0..2, item au loc 2 → dur 80.
        var nv = BitConverter.ToUInt16(body, p); p += 2;
        Assert.Equal(3, nv);
        Assert.Equal(0, BitConverter.ToInt16(body, p));
        Assert.Equal(0, BitConverter.ToInt16(body, p + 2));
        Assert.Equal(80, BitConverter.ToInt16(body, p + 4));
        p += nv * 2;
        // Vec clone+0xe — max_dur 100 au loc 2.
        nv = BitConverter.ToUInt16(body, p); p += 2;
        Assert.Equal(3, nv);
        Assert.Equal(100, BitConverter.ToInt16(body, p + 4));
        p += nv * 2;
        // Mask conteneur 0 = bit loc 2.
        Assert.Equal(4u, BitConverter.ToUInt32(body, p)); p += 4;
        // Record chaîné : [u16 id][u8 flags][u16 len str][u24 mask].
        Assert.Equal(1234, BitConverter.ToUInt16(body, p)); p += 2;
        Assert.Equal(0, body[p]); p += 1;                  // flags (&7)
        Assert.Equal(0, BitConverter.ToUInt16(body, p)); p += 2; // strlen
        // Mask u24 : seul bit18 (enchant) levé → 00 00 04.
        Assert.Equal(0, body[p]);
        Assert.Equal(0, body[p + 1]);
        Assert.Equal(4, body[p + 2]); p += 3;
        // bit18 → u16 strengthen = 5.
        Assert.Equal(5, BitConverter.ToUInt16(body, p)); p += 2;
        // Queue fixe 34B (toutes zéro ici — pas de train/mb/combos).
        for (var i = 0; i < 34; i++) Assert.Equal(0, body[p + i]);
        p += 34;
        // Mask conteneur 1 = bit loc 0.
        Assert.Equal(1u, BitConverter.ToUInt32(body, p)); p += 4;
        // Record léger 20B : id + i64 due + 3×s16 + u16 + u16.
        Assert.Equal(5678, BitConverter.ToUInt16(body, p)); p += 2;
        Assert.Equal(0L, BitConverter.ToInt64(body, p)); p += 8;
        Assert.Equal(0, BitConverter.ToInt16(body, p)); p += 2;
        Assert.Equal(0, BitConverter.ToInt16(body, p)); p += 2;
        Assert.Equal(0, BitConverter.ToInt16(body, p)); p += 2;
        Assert.Equal(9, BitConverter.ToUInt16(body, p)); p += 2;
        Assert.Equal(2, BitConverter.ToUInt16(body, p)); p += 2;
        Assert.Equal(body.Length, p);
    }

    /// <summary>Chaînage des champs du record : bits0-5 = embedded
    /// (sent. -1), bit6 amount, bit7 combo, bit20 i64 due_date —
    /// champs émis dans l'ordre natif uniquement si ≠ sentinelle.</summary>
    [Fact]
    public void WriteEquipViewItem_ChainedFieldsOrdered()
    {
        var item = new ItemEntity
        {
            ItemId = 42,
            EmbeddedId1 = 100, EmbeddedId3 = 300,   // bits 0, 2
            EmbeddedAmount = 7,                      // bit6
            Combo = 55,                              // bit7
            EnchantLevel = 9,                        // bit18
            DueDate = 0x11223344,                    // bit20
        };
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        NativeItemCodec.WriteEquipViewItem(w, item);
        var b = ms.ToArray();

        var p = 0;
        Assert.Equal(42, BitConverter.ToUInt16(b, p)); p += 2;
        Assert.Equal(7, b[p]); p += 1;        // flags = EmbeddedAmount&7
        Assert.Equal(0, BitConverter.ToUInt16(b, p)); p += 2; // strlen
        // Mask : bits0|2|6|7|18|20 = 0b101.0000.0000.1100.0101
        //      = 0x1400C5.
        Assert.Equal(0xC5, b[p]);
        Assert.Equal(0x00, b[p + 1]);
        Assert.Equal(0x14, b[p + 2]); p += 3;
        // Ordre : s16 emb1, s16 emb3, s16 amount, s16 combo,
        // 9×u16 extras (aucun), u16 enchant, i64 due.
        Assert.Equal(100, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(300, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(7, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(55, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(9, BitConverter.ToUInt16(b, p)); p += 2;
        Assert.Equal(0x11223344L, BitConverter.ToInt64(b, p));
        p += 8;
        Assert.Equal(b.Length, p + 34);       // queue fixe 34B
    }
}
