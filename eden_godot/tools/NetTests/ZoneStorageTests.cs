using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages banque étendue + coffre de famille
/// (<see cref="ZoneStorageMessages"/>). Formats prouvés :
/// <see cref="ZoneFamilyStorageData"/> = emit natif
/// <c>CFamilyStorageShop::OpenShop</c> serveur
/// (<c>FamilyStoragePackets.cs</c> — record item FIXE sans mask),
/// C→S 177/178 = 4×s16 (<c>InventoryHandlers.cs</c>),
/// 69/373 expansion (<c>ClientStorageExpandResult</c>).
/// </summary>
public class ZoneStorageTests
{
    // ---------- C→S ----------

    [Fact]
    public void FamilyStoragePut_Wire8B()
    {
        // [s16 coffre][s16 loc][s16 sac][s16 slot] — le serveur lit
        // Field1=index, Field2=loc (-1 auto), Field3/4 = sac/slot.
        var w = new BinWriter();
        new ZoneFamilyStoragePut
        {
            StorageIndex = 2, Loc = -1, Bag = 0, Slot = 7,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(8, b.Length);
        var m = new ZoneFamilyStoragePut();
        m.Deserialize(new BinReader(b));
        Assert.Equal(2, m.StorageIndex);
        Assert.Equal(-1, m.Loc);
        Assert.Equal(0, m.Bag);
        Assert.Equal(7, m.Slot);
    }

    [Fact]
    public void FamilyStorageGet_Wire8B()
    {
        var w = new BinWriter();
        new ZoneFamilyStorageGet
        {
            StorageIndex = 1, Loc = 12, Bag = -1, Slot = -1,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(8, b.Length);
        var m = new ZoneFamilyStorageGet();
        m.Deserialize(new BinReader(b));
        Assert.Equal(1, m.StorageIndex);
        Assert.Equal(12, m.Loc);
        Assert.Equal(-1, m.Bag);
        Assert.Equal(-1, m.Slot);
    }

    [Fact]
    public void QueryLog_And_Expand_EmptyBodies()
    {
        var w1 = new BinWriter();
        new ZoneFamilyStorageQueryLog().Serialize(w1);
        Assert.Empty(w1.ToArray());
        var w2 = new BinWriter();
        new ZoneStorageExpand().Serialize(w2);
        Assert.Empty(w2.ToArray());
    }

    [Fact]
    public void BankActionHelpers_TypesAndFields()
    {
        var dep = ZoneBankActions.DepositRequest(0, 5);
        Assert.Equal(0, dep.SrcType);
        Assert.Equal(5, dep.SrcSlot);
        Assert.Equal(0, dep.SrcIndex);
        Assert.Equal(4, dep.DstType);
        Assert.Equal(-1, dep.DstSlot);

        var wd = ZoneBankActions.WithdrawRequest(9, 0);
        Assert.Equal(4, wd.SrcType);
        Assert.Equal(9, wd.SrcSlot);
        Assert.Equal(0, wd.SrcIndex);
        Assert.Equal(0, wd.DstType);
        Assert.Equal(-1, wd.DstSlot);

        var mv = ZoneBankActions.MoveRequest(3, 0, 20, 1);
        Assert.Equal(4, mv.SrcType);
        Assert.Equal(3, mv.SrcSlot);
        Assert.Equal(0, mv.SrcIndex);
        Assert.Equal(4, mv.DstType);
        Assert.Equal(20, mv.DstSlot);
        Assert.Equal(1, mv.DstIndex);
    }

    // ---------- S→C ----------

    [Fact]
    public void EnterFamilyStorage_I32()
    {
        var w = new BinWriter();
        w.WriteInt(0x11223344);
        var m = new ZoneEnterFamilyStorage();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0x11223344, m.NpcUid);
    }

    [Fact]
    public void StorageExpandResult_S16()
    {
        foreach (var (val, ok) in new[] { ((short)0, true),
                     ((short)1, false), ((short)2, false) })
        {
            var w = new BinWriter();
            w.WriteShort(val);
            var m = new ZoneStorageExpandResult();
            m.Deserialize(new BinReader(w.ToArray()));
            Assert.Equal(val, m.Result);
            Assert.Equal(ok, m.Ok);
        }
    }

    [Fact]
    public void FamilyStorageDelete_4B()
    {
        var w = new BinWriter();
        w.WriteShort(2);
        w.WriteShort(39);
        var m = new ZoneFamilyStorageDelete();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(2, m.StorageIndex);
        Assert.Equal(39, m.Loc);
    }

    [Fact]
    public void FamilyStorageLog_Entries()
    {
        var w = new BinWriter();
        w.WriteShort(2);                    // u16 — 2 entrées
        w.WriteShort(0);
        w.WriteString("Storage Insert-ID:1");
        w.WriteShort(1);
        w.WriteString("Delete-ID:2");
        var m = new ZoneFamilyStorageLog();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(0, m.Entries[0].Action);
        Assert.Equal("Storage Insert-ID:1", m.Entries[0].Detail);
        Assert.Equal(1, m.Entries[1].Action);
    }

    /// <summary>Écrit un record item « coffre famille » au format fixe
    /// <c>FamilyStoragePackets.WriteStorageItem</c> serveur (93 B +
    /// chaîne maker, aucun mask).</summary>
    private static void WriteFixedRecord(BinWriter w, ushort itemId,
        short dur, short maxDur, byte flags, short amount,
        short strengthen)
    {
        w.WriteShort((short)itemId);      // u16 — itemdata+4
        w.WriteShort(dur);                // s16 — +0xc dur
        w.WriteShort(maxDur);             // s16 — +0xe maxdur
        w.WriteString("");                // str — maker
        w.WriteByte(flags);               // u8 — +0x24 flags
        for (var i = 0; i < 6; i++)
            w.WriteShort(-1);             // 6×s16 — embedded ids
        w.WriteShort(amount);             // s16 — +0x4c emb_amount
        w.WriteInt(1700000000);           // u32 — create_time
        w.WriteShort(0);                  // u16 — +0x1c
        w.WriteShort(0);                  // s16 — combo_id
        for (var i = 0; i < 9; i++)
            w.WriteShort(0);              // 9×u16 — extra combos
        w.WriteShort(strengthen);         // s16 — strengthen
        w.WriteShort(0);                  // s16 — plus_param
        w.WriteLong(0);                   // i64 — due_date
        w.WriteShort(0);                  // u16 — train_lvl
        w.WriteInt(0);                    // u32 — train_exp
        for (var i = 0; i < 6; i++)
            w.WriteShort(0);              // 6×u16 — train slots
        w.WriteShort(0);                  // u16 — ec10
        w.WriteShort(0);                  // u16 — ec11
        w.WriteShort(0);                  // u16 — gold_ench
        for (var i = 0; i < 3; i++)
            w.WriteShort(0);              // 3×s16 — mb_ench
        w.WriteShort(0);                  // u16 — mb_energy
        w.WriteShort(0);                  // u16 — mb_star
    }

    [Fact]
    public void FamilyStorageData_ParsesFixedRecords()
    {
        // Corps 508 : [s16 0][s16 index][u16 n]{[s16 loc][record]}[s16 0]
        var w = new BinWriter();
        w.WriteShort(0);                  // s16 — tête (0 natif)
        w.WriteShort(1);                  // s16 — coffre 1
        w.WriteShort(2);                  // u16 — 2 items
        w.WriteShort(3);                  // s16 — loc 3
        WriteFixedRecord(w, 30240, 1, 1, 0x08, 0, 5);
        w.WriteShort(9);                  // s16 — loc 9
        WriteFixedRecord(w, 31899, 42, 42, 0x40, 0, 0);
        w.WriteShort(0);                  // s16 — queue (0 natif)
        var m = new ZoneFamilyStorageData();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(1, m.StorageIndex);
        Assert.Equal(2, m.Items.Count);
        var a = m.Items[0];
        Assert.Equal(1, a.Bag);           // cont = coffre
        Assert.Equal(3, a.Slot);          // loc
        Assert.Equal(30240, a.ItemId);
        Assert.Equal(1, a.Durability);
        Assert.Equal(5, a.Strengthen);
        var c = m.Items[1];
        Assert.Equal(9, c.Slot);
        Assert.Equal(31899, c.ItemId);
        Assert.Equal(42, c.Durability);   // count stack
        Assert.Equal(0x40, c.Flags);      // bound
    }

    [Fact]
    public void FamilyStoragePutNotify_ParsesRecord()
    {
        var w = new BinWriter();
        w.WriteShort(0);                  // s16 — coffre 0
        w.WriteShort(17);                 // s16 — loc 17
        WriteFixedRecord(w, 11205, 12, 1, 0, 0, 0);
        var m = new ZoneFamilyStoragePutNotify();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0, m.StorageIndex);
        Assert.Equal(17, m.Loc);
        Assert.NotNull(m.Item);
        Assert.Equal(11205, m.Item!.ItemId);
        Assert.Equal(12, m.Item.Durability);
    }

    [Fact]
    public void FamilyStorageData_Truncated_NoThrow()
    {
        // Corps tronqué : doit parser sans exception (lecteur
        // conservateur — faux corps réseau ne crashent pas le HUD).
        var m = new ZoneFamilyStorageData();
        m.Deserialize(new BinReader(new byte[] { 0, 0, 1 }));
        Assert.Empty(m.Items);
    }

    // ---------- état agrégé ----------

    [Fact]
    public void FamilyStorageState_Flow()
    {
        FamilyStorageState.Close();
        FamilyStorageState.OnEnter(new ZoneEnterFamilyStorage
        { NpcUid = 42 });
        Assert.True(FamilyStorageState.IsOpen);
        Assert.Equal(42, FamilyStorageState.NpcUid);

        var data = new ZoneFamilyStorageData();
        data.StorageIndex = 0;
        data.Items.Add(new InventorySlot(0, 4, 30240, 1, 1, 0, 0, 0));
        FamilyStorageState.OnData(data);
        Assert.NotNull(FamilyStorageState.Chest(0));
        Assert.Equal(30240, FamilyStorageState.Chest(0)![4].ItemId);

        var del = new ZoneFamilyStorageDelete
        { StorageIndex = 0, Loc = 4 };
        FamilyStorageState.OnDelete(del);
        Assert.Empty(FamilyStorageState.Chest(0)!);

        FamilyStorageState.Close();
        Assert.False(FamilyStorageState.IsOpen);
        Assert.Empty(FamilyStorageState.Log);
    }
}
