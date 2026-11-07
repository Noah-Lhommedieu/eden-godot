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
//     (u16 +0x58, u32 +0x5c, 6×u16 +0x60..+0x6a = 18B, ordre groupé
//     [t1][t2][t3][l1][l2][l3]) ; queue fixe
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

/// <summary>Champs optionnels d'un record item — contenu du mask
/// conditionnel (sockets, combos, due_date) et de la queue fixe
/// 34B/16B (train, combos 10-11, gold/magicbox enchants). Tous ces
/// champs étaient lus puis jetés avant ; les fenêtres craft et les
/// tooltips en ont besoin.</summary>
public sealed record ItemMeta
{
    /// <summary>Gemmes incrustées par socket (bits0-5, sentinelle −1).</summary>
    public short[] Embedded { get; set; } = [-1, -1, -1, -1, -1, -1];
    /// <summary>Qté incrustée (bit6 — même valeur que Amount).</summary>
    public short EmbeddedAmount { get; set; }
    /// <summary>Combo principal (bit7).</summary>
    public short Combo { get; set; }
    /// <summary>Combos additionnels : [0..8] du mask (bits10-18 inv /
    /// 9-17 equip), [9..10] de la queue fixe.</summary>
    public ushort[] ExtraCombos { get; set; } = new ushort[11];
    /// <summary>Date d'expiration (bit21 inv / bit20 equip, i64).</summary>
    public long DueDate { get; set; }
    /// <summary>Timestamp de création (bit8 inv, u32).</summary>
    public uint CreateTime { get; set; }
    /// <summary>CEquipTrainAttrs : niveau/xp + 3 slots type/niveau.</summary>
    public ushort TrainLevel { get; set; }
    public uint TrainExp { get; set; }
    public ushort[] TrainSlotType { get; set; } = new ushort[3];
    public ushort[] TrainSlotLevel { get; set; } = new ushort[3];
    /// <summary>Enchant or (clone+0x6c, queue fixe).</summary>
    public ushort GoldEnchant { get; set; }
    /// <summary>Enchants magic-box (vecteur clone+0x70, 3×s16).</summary>
    public short[] MagicBoxEnchants { get; set; } = new short[3];
    /// <summary>Énergie magic-box (clone+0x7c).</summary>
    public ushort MagicBoxEnergy { get; set; }
    /// <summary>Niveau d'étoile magic-box (clone+0x7e).</summary>
    public ushort MagicBoxStarLevel { get; set; }
}

/// <summary>Un record item lu sur le fil — champs utiles au HUD.</summary>
public sealed record ItemRecord(
    int ItemId, int Durability, int MaxDurability, string Maker,
    byte Flags, uint Mask, int Amount, int Strengthen)
{
    /// <summary>Champs optionnels décodés (sockets/combos/magicbox) —
    /// toujours présent après un parse réussi.</summary>
    public ItemMeta Meta { get; init; } = new();
}

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
        var (amount, strengthen, meta) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.Inventory);
        ReadTail(r.ReadBytes(34), meta, train: true);
        item = new ItemRecord(itemId, dur, maxDur, "", flags,
            (uint)mask, amount, strengthen) { Meta = meta };
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
        var (amount, strengthen, meta) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.Inventory);
        ReadTail(r.ReadBytes(34), meta, train: true);
        item = new PutItemResult(itemId, bag, slot, dur, maxDur,
            flags, amount, strengthen) { Meta = meta };
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
        var (amount, strengthen, meta) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.Inventory);
        item = new ItemRecord(itemId, dur, maxDur, "", flags,
            (uint)mask, amount, strengthen) { Meta = meta };
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
        var (amount, strengthen, meta) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.EquipView);
        ReadTail(r.ReadBytes(34), meta, train: true);
        item = new ItemRecord(0, 0, 0, "", flags,
            (uint)mask, amount, strengthen) { Meta = meta };
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
        var (amount, strengthen, meta) =
            ReadConditional(r.ReadBytes(need), mask,
                NativeItemLayout.UpdateEquip);
        ReadTail(r.ReadBytes(16), meta, train: false);
        item = new ItemRecord(itemId, 0, 0, "", flags,
            (uint)mask, amount, strengthen) { Meta = meta };
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
            if ((mask & 0x200000) != 0) n += 18;// bit21 : CEquipTrainAttrs
                                                // (u16+u32+6×u16 = 18B,
                                                // ordre MÉMOIRE groupé
                                                // [t1][t2][t3][l1][l2][l3])
        }
        return n;
    }

    /// <summary>Extrait amount/strengthen + tous les champs
    /// conditionnels en marchant le mask dans l'ordre du layout
    /// (mêmes conventions que <see cref="ConditionalBytes"/>) :
    /// sockets incrustées, combos, create_time, due_date, bloc
    /// CEquipTrainAttrs du UpdateEquip.</summary>
    private static (int Amount, int Strengthen, ItemMeta Meta)
        ReadConditional(byte[] buf, int mask, NativeItemLayout layout)
    {
        var pos = 0;
        var meta = new ItemMeta();
        int amount = 0, strengthen = 0;
        short S16()
        {
            var v = pos + 2 <= buf.Length
                ? BitConverter.ToInt16(buf, pos) : (short)0;
            pos += 2; return v;
        }
        ushort U16() => (ushort)S16();
        uint U32()
        {
            var v = pos + 4 <= buf.Length
                ? BitConverter.ToUInt32(buf, pos) : 0u;
            pos += 4; return v;
        }
        long I64()
        {
            var v = pos + 8 <= buf.Length
                ? BitConverter.ToInt64(buf, pos) : 0L;
            pos += 8; return v;
        }
        for (var i = 0; i < 6; i++)             // bits0-5 : s16 embedded
            if ((mask & (1 << i)) != 0) meta.Embedded[i] = S16();
        if ((mask & 0x40) != 0)                 // bit6 : s16 embedded_amount
        {
            var a = S16();
            amount = a;
            meta.EmbeddedAmount = a;
        }
        if ((mask & 0x80) != 0) meta.Combo = S16();  // bit7 : combo
        if (layout == NativeItemLayout.Inventory)
        {
            if ((mask & 0x100) != 0)            // bit8 : create_time
                meta.CreateTime = U32();
            if ((mask & 0x200) != 0) pos += 2;  // bit9 : +0x1c
            for (var i = 10; i <= 18; i++)      // bits10-18 : u16 combos
                if ((mask & (1 << i)) != 0)
                    meta.ExtraCombos[i - 10] = U16();
            if ((mask & 0x80000) != 0)          // bit19 : strengthen
                strengthen = S16();
            if ((mask & 0x100000) != 0) pos += 2; // bit20 : +0x2e
            if ((mask & 0x200000) != 0)         // bit21 : i64 due_date
                meta.DueDate = I64();
        }
        else
        {
            if ((mask & 0x100) != 0) pos += 2;  // bit8 : +0x1c
            for (var i = 9; i <= 17; i++)       // bits9-17 : u16 combos
                if ((mask & (1 << i)) != 0)
                    meta.ExtraCombos[i - 9] = U16();
            if ((mask & 0x40000) != 0)          // bit18 : strengthen u16
                strengthen = S16();
            if ((mask & 0x80000) != 0) pos += 2; // bit19 : +0x2e
            if ((mask & 0x100000) != 0)         // bit20 : i64 due
                meta.DueDate = I64();
            if ((mask & 0x200000) != 0)         // bit21 : train 18B ordre
                ReadTrainBlock(buf, ref pos, meta,  // groupé (mémoire)
                    grouped: true);
        }
        return (amount, strengthen, meta);
    }

    /// <summary>Bloc CEquipTrainAttrs — 18B : <c>[u16 level][u32 exp]</c>
    /// puis les 3 slots. Ordre <b>entrelacé</b> <c>[t1][l1][t2][l2]
    /// [t3][l3]</c> dans la queue 34B (274/312/358/276) ; ordre
    /// <b>groupé</b> mémoire <c>[t1][t2][t3][l1][l2][l3]</c> dans le
    /// conditionnel bit21 du UpdateEquip (Serialize 282 @0x81f7bxx —
    /// preuve NativeItemCodec serveur).</summary>
    private static void ReadTrainBlock(byte[] buf, ref int pos,
        ItemMeta meta, bool grouped = false)
    {
        if (pos + 18 > buf.Length) { pos = buf.Length; return; }
        meta.TrainLevel = BitConverter.ToUInt16(buf, pos); pos += 2;
        meta.TrainExp = BitConverter.ToUInt32(buf, pos); pos += 4;
        for (var i = 0; i < 3; i++)
        {
            meta.TrainSlotType[i] = BitConverter.ToUInt16(buf, pos);
            pos += 2;
            if (!grouped)
            {
                meta.TrainSlotLevel[i] = BitConverter.ToUInt16(buf, pos);
                pos += 2;
            }
        }
        if (grouped)                                // niveaux après types
            for (var i = 0; i < 3; i++)
            {
                meta.TrainSlotLevel[i] = BitConverter.ToUInt16(buf, pos);
                pos += 2;
            }
    }

    /// <summary>Queue fixe des records item — 34B (Inventory/EquipView :
    /// [train 18B][ec10][ec11][gold][mb×3][energy][star]) ou 16B
    /// (UpdateEquip : sans le bloc train — il est dans le mask bit21).</summary>
    private static void ReadTail(byte[] buf, ItemMeta meta, bool train)
    {
        var pos = 0;
        if (train)
        {
            if (buf.Length < 34) return;
            ReadTrainBlock(buf, ref pos, meta);         // 18B
        }
        else if (buf.Length < 16) return;
        if (pos + 16 > buf.Length) return;
        meta.ExtraCombos[9] = BitConverter.ToUInt16(buf, pos); pos += 2;
        meta.ExtraCombos[10] = BitConverter.ToUInt16(buf, pos); pos += 2;
        meta.GoldEnchant = BitConverter.ToUInt16(buf, pos); pos += 2;
        for (var i = 0; i < 3; i++)
        {
            meta.MagicBoxEnchants[i] = BitConverter.ToInt16(buf, pos);
            pos += 2;
        }
        meta.MagicBoxEnergy = BitConverter.ToUInt16(buf, pos); pos += 2;
        meta.MagicBoxStarLevel = BitConverter.ToUInt16(buf, pos); pos += 2;
    }
}
