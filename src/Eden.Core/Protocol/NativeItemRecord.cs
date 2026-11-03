using EdenClient.Net;

namespace EdenClient.Protocol;

// Lecteur partagé des « item-records » natifs lapis — les records
// équipement/inventaire ont le même squelette `[id][flags][str maker]
// [u24 mask][champs conditionnels][tail]` mais TROIS variantes de
// layout cohabitent sur le fil :
//
//   - Inventory (274/311/312/358, WriteItem @0x80e3xxx) :
//     `[u16 itemId][s16 dur][s16 maxDur][str][u8 flags][u24 mask]
//     [cond][34B tail]` — mask : bits0-5 s16 embedded, bit6 s16 amount,
//     bit7 s16 combo, bit8 u32 create_time, bit9 u16 +0x1c,
//     bits10-18 9×u16 extra combos, bit19 s16 strengthen,
//     bit20 s16 +0x2e, bit21 i64 due_date.
//   - EquipView (276 body, WriteEquipViewItemBody @0x843exxx) :
//     `[u8 flags][str][u24 mask][cond][34B tail]` — mask : bits0-5 s16,
//     bit6 s16 amount(+0x4c), bit7 s16 combo(+0x10), bit8 u16 +0x1c,
//     bits9-17 9×u16 combos, bit18 u16 strengthen(+0x2c),
//     bit19 s16 +0x2e, bit20 i64 due(+0x50). (Pas de bit21 émis.)
//   - UpdateEquip (282, Serialize @0x82xxxxx) :
//     `[u16 itemId][u8 flags][str][u24 mask][cond][16B tail]` —
//     même ordre mask que EquipView + bit21 = bloc CEquipTrainAttrs
//     (u16 +0x58, u32 +0x5c, 6×u16 +0x60..+0x6a = 16B) ; queue fixe
//     16B [ec10][ec11][gold][3×s16 magicbox][energy][starlevel].
//
// Sources : $EDEN/server/src/Eden.Zone/Codecs/NativeItemCodec.cs
// (désasm prouvé byte-à-byte + golden ncid_311/358.json).

/// <summary>Variante de layout du record item sur le fil.</summary>
public enum NativeItemLayout
{
    /// <summary>312/311/358/274 — WriteItem, queue 34B.</summary>
    Inventory,
    /// <summary>276 — WriteEquipViewItemBody, queue 34B.</summary>
    EquipView,
    /// <summary>282 — record UpdateEquipment, queue 16B.</summary>
    UpdateEquip,
    /// <summary>424 — conteneur d'équipement elf : corps WriteItem
    /// SANS la queue fixe 34B (NativeElfCodec.BuildEnterNodeElfContainer
    /// serveur — WriteItem natif tronqué avant la queue).</summary>
    ElfContainer,
}

/// <summary>Un record item lu sur le fil — champs utiles au HUD.</summary>
public sealed record ItemRecord(
    int ItemId, int Durability, int MaxDurability, string Maker,
    byte Flags, uint Mask, int Amount, int Strengthen);

/// <summary>Lecture conservative des item-records (try/false, jamais
/// d'exception sur un corps tronqué).</summary>
public static class NativeItemRecord
{
    /// <summary>Lit un record au format <paramref name="layout"/> ;
    /// <see langword="false"/> si le corps s'arrête au milieu.</summary>
    public static bool TryRead(BinReader r, NativeItemLayout layout,
        out ItemRecord item)
    {
        item = new ItemRecord(0, 0, 0, "", 0, 0, 0, 0);
        if (layout == NativeItemLayout.UpdateEquip)
            return TryReadUpdateEquip(r, out item);
        if (layout == NativeItemLayout.EquipView)
            return TryReadEquipView(r, out item);
        if (layout == NativeItemLayout.ElfContainer)
            return TryReadElfContainerItem(r, out item);
        return TryReadInventory(r, out item);
    }

    /// <summary>Record WriteItem (312/311/358 et corps 274 après son
    /// propre préfixe) : `[u16 id][s16 dur][s16 maxDur][str][u8 flags]
    /// [u24 mask][cond][34B]`.</summary>
    public static bool TryReadInventory(BinReader r, out ItemRecord item)
    {
        item = new ItemRecord(0, 0, 0, "", 0, 0, 0, 0);
        if (r.Remaining < 9) return false;
        var itemId = r.ReadUShort();            // u16 — id d'item
        var dur = r.ReadShort();                // s16 — durabilité cur
        var maxDur = r.ReadShort();             // s16 — durabilité max
        var makerLen = r.ReadUShort();          // str — nom du maker
        if (r.Remaining < makerLen + 1 + 3) return false;
        r.ReadBytes(makerLen);
        var flags = r.ReadByte();               // u8 — flags clone+0x24
        var mask = ReadU24(r);                  // u24 — mask champs cond.
        var need = ConditionalBytes(mask, NativeItemLayout.Inventory);
        if (r.Remaining < need + 34) return false;
        var (amount, strengthen) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.Inventory);
        r.ReadBytes(34);                        // queue fixe 34B
        item = new ItemRecord(itemId, dur, maxDur, "", flags,
            (uint)mask, amount, strengthen);
        return true;
    }

    /// <summary>Record du 274 PutItemIntoInventory (BuildPutItem —
    /// désasm PutIntoContainer @0x809ea00) : `[u16 itemId][s16 sac]
    /// [s16 loc]` puis le corps Inventory sans l'id (`[s16 dur]
    /// [s16 maxDur][str][u8 flags][u24 mask][cond][34B]`).</summary>
    public static bool TryReadPutItem(BinReader r, out PutItemResult item)
    {
        item = new PutItemResult(0, 0, 0, 0, 0, 0, 0, 0);
        if (r.Remaining < 15) return false;
        var itemId = r.ReadUShort();            // u16 — id d'item
        var bag = r.ReadShort();                // s16 — container_index
        var slot = r.ReadShort();               // s16 — loc dans le sac
        var dur = r.ReadShort();                // s16 — clone+0xc (count)
        var maxDur = r.ReadShort();             // s16 — cur_maxdurability
        var makerLen = r.ReadUShort();          // str — nom du maker
        if (r.Remaining < makerLen + 1 + 3) return false;
        r.ReadBytes(makerLen);
        var flags = r.ReadByte();               // u8 — flags clone+0x24
        var mask = ReadU24(r);
        var need = ConditionalBytes(mask, NativeItemLayout.Inventory);
        if (r.Remaining < need + 34) return false;
        var (amount, strengthen) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.Inventory);
        r.ReadBytes(34);                        // queue fixe 34B
        item = new PutItemResult(itemId, bag, slot, dur, maxDur,
            flags, amount, strengthen);
        return true;
    }

    /// <summary>Record item du conteneur elf (424) : corps WriteItem
    /// SANS la queue fixe 34B — la boucle native s'arrête après le
    /// mask+conditionnels (NativeElfCodec serveur, WriteItem
    /// emitTail:false).</summary>
    public static bool TryReadElfContainerItem(BinReader r,
        out ItemRecord item)
    {
        item = new ItemRecord(0, 0, 0, "", 0, 0, 0, 0);
        if (r.Remaining < 9) return false;
        var itemId = r.ReadUShort();            // u16 — id d'item
        var dur = r.ReadShort();                // s16 — durabilité cur
        var maxDur = r.ReadShort();             // s16 — durabilité max
        var makerLen = r.ReadUShort();          // str — nom du maker
        if (r.Remaining < makerLen + 1 + 3) return false;
        r.ReadBytes(makerLen);
        var flags = r.ReadByte();               // u8 — flags clone+0x24
        var mask = ReadU24(r);                  // u24 — mask champs cond.
        var need = ConditionalBytes(mask, NativeItemLayout.Inventory);
        if (r.Remaining < need) return false;
        var (amount, strengthen) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.Inventory);
        item = new ItemRecord(itemId, dur, maxDur, "", flags,
            (uint)mask, amount, strengthen);
        return true;
    }

    /// <summary>Corps EquipView (276, vue d'équipement observateurs) :
    /// `[u8 flags][str][u24 mask][cond][34B]` — l'itemId est dans
    /// l'en-tête du paquet, pas ici.</summary>
    public static bool TryReadEquipView(BinReader r, out ItemRecord item)
    {
        item = new ItemRecord(0, 0, 0, "", 0, 0, 0, 0);
        if (r.Remaining < 6) return false;
        var flags = r.ReadByte();               // u8 — sockets (0xff vide)
        var makerLen = r.ReadUShort();          // str — maker (vide natif)
        if (r.Remaining < makerLen + 3) return false;
        r.ReadBytes(makerLen);
        var mask = ReadU24(r);
        var need = ConditionalBytes(mask, NativeItemLayout.EquipView);
        if (r.Remaining < need + 34) return false;
        var (amount, strengthen) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.EquipView);
        r.ReadBytes(34);
        item = new ItemRecord(0, 0, 0, "", flags,
            (uint)mask, amount, strengthen);
        return true;
    }

    /// <summary>Record du 282 UpdateEquipment :
    /// `[u16 id][u8 flags][str][u24 mask][cond][16B]`.</summary>
    public static bool TryReadUpdateEquip(BinReader r, out ItemRecord item)
    {
        item = new ItemRecord(0, 0, 0, "", 0, 0, 0, 0);
        if (r.Remaining < 8) return false;
        var itemId = r.ReadUShort();            // u16 — id d'item
        var flags = r.ReadByte();               // u8 — sockets
        var makerLen = r.ReadUShort();
        if (r.Remaining < makerLen + 3) return false;
        r.ReadBytes(makerLen);
        var mask = ReadU24(r);
        var need = ConditionalBytes(mask, NativeItemLayout.UpdateEquip);
        if (r.Remaining < need + 16) return false;
        var (amount, strengthen) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.UpdateEquip);
        r.ReadBytes(16);                        // queue fixe 16B
        item = new ItemRecord(itemId, 0, 0, "", flags,
            (uint)mask, amount, strengthen);
        return true;
    }

    /// <summary>u24 little-endian (3 octets).</summary>
    private static int ReadU24(BinReader r) =>
        r.ReadByte() | r.ReadByte() << 8 | r.ReadByte() << 16;

    /// <summary>Nombre d'octets des champs conditionnels selon le mask
    /// et la variante (ordre natif — cf. en-tête de fichier).</summary>
    private static int ConditionalBytes(int mask, NativeItemLayout layout)
    {
        var n = 0;
        for (var i = 0; i < 6; i++)             // bits0-5 : s16 embedded
            if ((mask & (1 << i)) != 0) n += 2;
        if ((mask & 0x40) != 0) n += 2;         // bit6 : s16 amount
        if ((mask & 0x80) != 0) n += 2;         // bit7 : s16 combo
        if (layout == NativeItemLayout.Inventory)
        {
            if ((mask & 0x100) != 0) n += 4;    // bit8 : u32 create_time
            if ((mask & 0x200) != 0) n += 2;    // bit9 : u16 +0x1c
            for (var i = 10; i <= 18; i++)      // bits10-18 : u16 combos
                if ((mask & (1 << i)) != 0) n += 2;
            if ((mask & 0x80000) != 0) n += 2;  // bit19 : s16 strengthen
            if ((mask & 0x100000) != 0) n += 2; // bit20 : s16 +0x2e
            if ((mask & 0x200000) != 0) n += 8; // bit21 : i64 due
        }
        else
        {
            if ((mask & 0x100) != 0) n += 2;    // bit8 : u16 +0x1c
            for (var i = 9; i <= 17; i++)       // bits9-17 : u16 combos
                if ((mask & (1 << i)) != 0) n += 2;
            if ((mask & 0x40000) != 0) n += 2;  // bit18 : u16 strengthen
            if ((mask & 0x80000) != 0) n += 2;  // bit19 : s16 +0x2e
            if ((mask & 0x100000) != 0) n += 8; // bit20 : i64 due
            if ((mask & 0x200000) != 0) n += 16;// bit21 : CEquipTrainAttrs
        }
        return n;
    }

    /// <summary>Extrait amount/strengthen en marchant le mask dans
    /// l'ordre du layout (mêmes conventions que
    /// <see cref="ConditionalBytes"/>).</summary>
    private static (int Amount, int Strengthen) ReadConditional(
        byte[] buf, int mask, NativeItemLayout layout)
    {
        var pos = 0;
        int amount = 0, strengthen = 0;
        int S16()
        {
            var v = pos + 2 <= buf.Length
                ? BitConverter.ToInt16(buf, pos) : (short)0;
            pos += 2; return v;
        }
        for (var i = 0; i < 6; i++)             // bits0-5 : s16 embedded
            if ((mask & (1 << i)) != 0) pos += 2;
        if ((mask & 0x40) != 0) amount = S16(); // bit6 : amount
        if ((mask & 0x80) != 0) pos += 2;       // bit7 : combo
        if (layout == NativeItemLayout.Inventory)
        {
            if ((mask & 0x100) != 0) pos += 4;  // bit8 : create_time
            if ((mask & 0x200) != 0) pos += 2;  // bit9 : +0x1c
            for (var i = 10; i <= 18; i++)
                if ((mask & (1 << i)) != 0) pos += 2;
            if ((mask & 0x80000) != 0)          // bit19 : strengthen
                strengthen = S16();
            if ((mask & 0x100000) != 0) pos += 2;
            // bit21 : i64 due — skip (8B)
        }
        else
        {
            if ((mask & 0x100) != 0) pos += 2;  // bit8 : +0x1c
            for (var i = 9; i <= 17; i++)
                if ((mask & (1 << i)) != 0) pos += 2;
            if ((mask & 0x40000) != 0)          // bit18 : strengthen u16
                strengthen = S16();
            if ((mask & 0x80000) != 0) pos += 2;
            // bit20 : i64 due — skip ; bit21 : train block — skip
        }
        return (amount, strengthen);
    }
}
