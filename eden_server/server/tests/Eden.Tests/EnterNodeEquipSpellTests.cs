using System.Collections.Generic;
using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>Codec NCID 311 ClientEnterNodeEquipAndSpell — structure
/// byte-level vérifiée sur la capture native test3 (Serialize
/// désassemblé) : [u8 nContainers] puis par conteneur 0..5
/// [u8 key][u32 slotMask][records WriteItem], puis [s16 n][sorts].</summary>
public class EnterNodeEquipSpellTests
{
    private static List<ItemEntity> Test3Equipment() => new()
    {
        new ItemEntity { ItemId = 31939, BagId = 0, Slot = 2,
            Durability = 120, MaxDurability = 120, Identify = 1 },
        new ItemEntity { ItemId = 31940, BagId = 0, Slot = 4,
            Durability = 75, MaxDurability = 75, Identify = 1 },
        new ItemEntity { ItemId = 31941, BagId = 0, Slot = 5,
            Durability = 75, MaxDurability = 75, Identify = 1 },
        new ItemEntity { ItemId = 31899, BagId = 0, Slot = 7,
            Durability = 75, MaxDurability = 75, Identify = 1 },
        new ItemEntity { ItemId = 31989, BagId = 2, Slot = 2,
            Durability = 80, MaxDurability = 80, Identify = 1 },
        new ItemEntity { ItemId = 31990, BagId = 2, Slot = 4,
            Durability = 50, MaxDurability = 50, Identify = 1 },
        new ItemEntity { ItemId = 31991, BagId = 2, Slot = 5,
            Durability = 50, MaxDurability = 50, Identify = 1 },
        new ItemEntity { ItemId = 23857, BagId = 3, Slot = 2,
            Durability = 80, MaxDurability = 80, Identify = 1 },
        new ItemEntity { ItemId = 23858, BagId = 3, Slot = 4,
            Durability = 50, MaxDurability = 50, Identify = 1 },
        new ItemEntity { ItemId = 23859, BagId = 3, Slot = 5,
            Durability = 50, MaxDurability = 50, Identify = 1 },
    };

    [Fact]
    public void EquipAndSpell_BucketsMatchNativeCapture()
    {
        var body = NativeItemCodec.BuildEnterNodeEquipAndSpell(
            Test3Equipment(), new List<(short, short)>());
        var pos = 0;
        Assert.Equal(6, body[pos++]);            // u8 — 6 conteneurs
        // Masks capturés : 0→0xb4 (locs 2,4,5,7), 2→0x34, 3→0x34.
        var wantMasks = new uint[] { 0xb4, 0, 0x34, 0x34, 0, 0 };
        var wantIds = new int[] {
            31939, 31940, 31941, 31899,
            31989, 31990, 31991,
            23857, 23858, 23859,
        };
        var wantDur = new int[] {
            120, 75, 75, 75, 80, 50, 50, 80, 50, 50,
        };
        var idAt = 0;
        for (var key = 0; key < 6; key++)
        {
            Assert.Equal(key, body[pos]);        // u8 — clé conteneur
            var mask = System.BitConverter.ToUInt32(body, pos + 1);
            Assert.Equal(wantMasks[key], mask);  // u32 — bitmap locs
            pos += 5;
            for (var loc = 0; loc < 32; loc++)
            {
                if ((mask & (1u << loc)) == 0) continue;
                var itemId = System.BitConverter.ToUInt16(body, pos);
                Assert.Equal(wantIds[idAt], itemId);
                Assert.Equal((short)wantDur[idAt],
                    System.BitConverter.ToInt16(body, pos + 2));
                Assert.Equal((short)wantDur[idAt],
                    System.BitConverter.ToInt16(body, pos + 4));
                var slen = System.BitConverter.ToUInt16(body, pos + 6);
                Assert.Equal(0, slen);           // maker vide
                var flags = body[pos + 8 + slen];
                Assert.Equal(0, flags & 7);      // pas de socket
                Assert.NotEqual(0, flags & 8);   // identifié
                pos += 12 + slen + 34;
                idAt++;
            }
        }
        Assert.Equal(10, idAt);
        // Queue sorts : [s16 0] — aucun sort fourni.
        Assert.Equal(pos + 2, body.Length);
        Assert.Equal(0, System.BitConverter.ToInt16(body, pos));
    }

    [Fact]
    public void EquipAndSpell_SpellTailIsTriples()
    {
        var spells = new List<(short, short)> {
            (unchecked((short)0xc351), 1), (unchecked((short)0xc35b), 2),
        };
        var body = NativeItemCodec.BuildEnterNodeEquipAndSpell(
            new List<ItemEntity>(), spells);
        // [u8 6] + 6×[u8 key][u32 0] + [s16 2] + 2×[s16 sm][u8 order]
        Assert.Equal(1 + 30 + 2 + 6, body.Length);
        Assert.Equal(2, System.BitConverter.ToInt16(body, 31));
        Assert.Equal(unchecked((short)0xc351),
            System.BitConverter.ToInt16(body, 33));
        Assert.Equal(1, body[35]);
        Assert.Equal(unchecked((short)0xc35b),
            System.BitConverter.ToInt16(body, 36));
        Assert.Equal(2, body[38]);
    }

    [Fact]
    public void EquipAndSpell_LocOutOfMaskRangeIsSkipped()
    {
        // Ligne héritée à loc=35 (avant le reroutage type35→loc10) :
        // ne rentre pas dans le mask u32 — ignorée proprement.
        var items = new List<ItemEntity> {
            new ItemEntity { ItemId = 23860, BagId = 0, Slot = 35,
                Durability = 10, MaxDurability = 10 },
        };
        var body = NativeItemCodec.BuildEnterNodeEquipAndSpell(
            items, new List<(short, short)>());
        Assert.Equal(33, body.Length);
        for (var k = 0; k < 6; k++)
            Assert.Equal(0u, System.BitConverter.ToUInt32(body, 2 + k * 5));
    }
}
