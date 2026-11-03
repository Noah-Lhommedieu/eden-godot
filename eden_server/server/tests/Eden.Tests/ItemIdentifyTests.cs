using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>Codec bit3 = identifié (clone+0x24 natif —
/// `CItemFactory::IdentifyItem` @0x809da40 : `clone+0x24 |= 8`)
/// + propagation du flag `identify` sur restore buyback.</summary>
public class ItemIdentifyTests
{
    [Fact]
    public void WriteItem_FlagByteCarriesIdentifyBit()
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        var identified = new ItemEntity { Identify = 1, EmbeddedAmount = 2 };
        NativeItemCodec.WriteItem(w, 1002, 80, 100, 0, 0, meta: identified);
        var bytes = ms.ToArray();
        // Byte 8 = flags (u16 id + 2×s16 + u16 strlen).
        // bits0-2 = socket count (2) | bit3 = identifié (8) = 0x0A.
        Assert.Equal(0x0A, bytes[8]);
    }

    [Fact]
    public void WriteItem_FlagByteClearWhenUnidentified()
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        var unidentified = new ItemEntity { Identify = 0, EmbeddedAmount = 0 };
        // flags=0x08 passé par l'appelant — meta.Identify=0 doit
        // l'emporter (item non identifié → bit3 effacé).
        NativeItemCodec.WriteItem(w, 1002, 80, 100, 0x08, 0,
            meta: unidentified);
        var bytes = ms.ToArray();
        Assert.Equal(0x00, bytes[8] & 0x08);
    }

    [Fact]
    public void BuildPutItem_FlagByteHonoursIdentify()
    {
        var meta = new ItemEntity { Identify = 0 };
        var body = NativeItemCodec.BuildPutItem(1002, 1, 2, 3, 4,
            0x08, 0, meta: meta);
        // offsets : u16 id + s16 bag + s16 slot + s16 dur + s16 maxdur
        // + u16 len = 12 → byte 12 = flags.
        Assert.Equal(0x00, body[12] & 0x08);
    }

    [Fact]
    public void BuildPutItem_NativeFieldOrder()
    {
        // Preuve : CItemFactory::PutIntoContainer @0x809ea00 — wire
        // [u16 id][s16 container][s16 loc][s16 dur][s16 cur_max].
        // Régression du bug live « item acheté invisible » (l'ancien
        // ordre [id][qty][qty][bag][slot] faisait lire bag=quantité
        // et durability=0 au client).
        var body = NativeItemCodec.BuildPutItem(1002, 1, 5, 7, 99,
            0x08, 0);
        Assert.Equal(1002, BitConverter.ToUInt16(body, 0));
        Assert.Equal(1, BitConverter.ToInt16(body, 2));   // container
        Assert.Equal(5, BitConverter.ToInt16(body, 4));   // loc
        Assert.Equal(7, BitConverter.ToInt16(body, 6));   // durability
        Assert.Equal(99, BitConverter.ToInt16(body, 8));  // cur_max
    }

    [Fact]
    public void BuildPutItem_EntityOverloadCarriesStackCount()
    {
        // Stack : durability = count (modèle natif clone+0xc) — un
        // achat de 5 potions doit afficher x5, pas x1.
        var stack = new ItemEntity
        {
            ItemId = 30001, BagId = 0, Slot = 9, Durability = 5,
            MaxDurability = 99, Identify = 1,
        };
        var body = NativeItemCodec.BuildPutItem(stack);
        Assert.Equal(30001, BitConverter.ToUInt16(body, 0));
        Assert.Equal(0, BitConverter.ToInt16(body, 2));
        Assert.Equal(9, BitConverter.ToInt16(body, 4));
        Assert.Equal(5, BitConverter.ToInt16(body, 6));
        Assert.Equal(99, BitConverter.ToInt16(body, 8));
    }

    [Fact]
    public async Task RestoreItem_PreservesIdentifyFlag()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = new Eden.Core.Game.Services.InventoryService(repo);
        var snapshot = new ItemEntity
        {
            CharacterId = 1, ItemId = 1002, Durability = 80,
            MaxDurability = 100, Identify = 0, EnchantLevel = 3,
        };
        var (ok, err, _, _) = await svc.RestoreItemAsync(1, snapshot, 1);
        Assert.True(ok, err);
        Assert.Equal(0, repo.Items.Single().Identify);
    }
}

/// <summary>Cooldowns d'items d'usage — map native
/// CCharacter+0x6a8 (CheckIsCoolDowning @0x8411480 /
/// AddCoolDownItem @0x84283c0) + corps du 368
/// ClientUpdateCoolDown (Serialize @0x8237d30).</summary>
public class ItemCooldownTests
{
    [Fact]
    public void IsCooling_TrueDuringWindow_FalseAfter()
    {
        var svc = new Eden.Zone.ItemCooldownService();
        svc.Arm(1, 10, 30001, 50);            // 50 ds = 5 s
        Assert.True(svc.IsCooling(1, 10));
        Assert.False(svc.IsCooling(1, 30));   // autre groupe libre
        Assert.False(svc.IsCooling(2, 10));   // autre perso libre
    }

    [Fact]
    public void Arm_SameGroup_ReplacesEntry()
    {
        var svc = new Eden.Zone.ItemCooldownService();
        svc.Arm(1, 10, 30001, 50);
        svc.Arm(1, 10, 30002, 300);
        var body = svc.BuildUpdateBody(1);
        Assert.Single(body.Entries);              // une seule entrée
        Assert.Equal((ushort)30002, body.Entries[0].Aux); // nouvel item
    }

    [Fact]
    public void BuildUpdateBody_NativeWireLayout()
    {
        // [u8 count]{u8 kind=0|s16 group|u16 itemId|i32 restant en
        // 1/10 s} — Serialize natif @0x8237d30.
        var svc = new Eden.Zone.ItemCooldownService();
        svc.Arm(7, 10, 30001, 50);            // 5 s
        svc.Arm(7, 30, 30002, 600);           // 60 s
        var msg = svc.BuildUpdateBody(7);
        Assert.Equal(2, msg.Entries.Count);
        Assert.Equal((byte)0, msg.Entries[0].Kind);  // kind 0 = groupe item
        Assert.Equal(10, msg.Entries[0].Id);
        Assert.Equal((ushort)30001, msg.Entries[0].Aux);
        Assert.InRange(msg.Entries[0].RemainingDs, 40, 50);
        Assert.Equal((byte)0, msg.Entries[1].Kind);
        Assert.Equal(30, msg.Entries[1].Id);
        Assert.Equal((ushort)30002, msg.Entries[1].Aux);
        Assert.InRange(msg.Entries[1].RemainingDs, 590, 600);
    }

    [Fact]
    public void BuildUpdateBody_ExpiredEntryPruned()
    {
        var svc = new Eden.Zone.ItemCooldownService();
        svc.Arm(1, 10, 30001, 0);                 // expiré d'office
        var msg = svc.BuildUpdateBody(1);
        Assert.Empty(msg.Entries);
    }
}
