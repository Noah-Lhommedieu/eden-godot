using Eden.Core.Game.Inventory;
using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>
/// Codecs du coffre de famille zone → client — formats prouvés par
/// désassemblage du zone natif (`lapis::CNC_CZ_ClientFamilyStorage*`).
/// Contrairement au record inventaire (<see cref="NativeItemCodec"/>),
/// le record item des coffres est à <b>layout fixe</b> : aucun mask
/// u24, tous les champs sont émis.
/// <list type="bullet">
///   <item>504 <c>ClientFamilyStorageDeleteItem</c> —
///   <c>Serialize @0x08200xxx</c> : <c>[s16 index][s16 loc]</c>.</item>
///   <item>505 <c>ClientFamilyStoragePutItem</c> —
///   <c>Serialize @0x08200xxx</c> : <c>[s16 index][s16 loc]</c> +
///   record fixe (ci-dessous). Les champs du paquet sont copiés d'un
///   CCloneItem par <c>CNE_ZMis_ZoneServerFamilyStorageUpdate::Execute
///   </c> @0x083188e0.</item>
///   <item>506 <c>ClientFamilyStorageLog</c> —
///   <c>[u16 n]{[s16 log_type][str log]}</c>.</item>
///   <item>508 <c>ClientFamilyStorageData</c> —
///   <c>Serialize @0x08200600</c> + emit `CFamilyStorageShop::OpenShop`
///   @0x08579440 (le s16 de tête/queue `+4` est explicitement 0) :
///   <c>[s16 0][s16 index][u16 n]{[s16 loc][record]}[s16 0]</c>,
///   un paquet par coffre non vide.</item>
/// </list>
/// Record item fixe (offsets CCloneItem entre parenthèses) :
/// <c>[u16 itemdata+4][s16 +0xc dur][s16 +0xe maxdur][str +0x20 maker]
/// [u8 +0x24 flags][6×s16 vec+0x40 embedded][s16 +0x4c emb_amount]
/// [u32 +0x18 create_time][u16 +0x1c][s16 +0x10 combo_id]
/// [9×u16 vec+0x34 ec1-9][s16 +0x2c strengthen][s16 +0x2e plus_param]
/// [i64 +0x50 due_date][u16 +0x58 train_lvl][u32 +0x5c train_exp]
/// [u16 +0x60 s1t][u16 +0x66 s1l][u16 +0x62 s2t][u16 +0x68 s2l]
/// [u16 +0x64 s3t][u16 +0x6a s3l][u16 vec34[9] ec10][u16 vec34[10] ec11]
/// [u16 +0x6c gold_ench][3×s16 vec+0x70 mb_ench]
/// [u16 +0x7c mb_energy][u16 +0x7e mb_star]</c>
/// </summary>
public static class FamilyStoragePackets
{
    /// <summary>NCID suppression d'item du coffre.</summary>
    public const ushort NcidDeleteItem = 504;
    /// <summary>NCID ajout d'item au coffre.</summary>
    public const ushort NcidPutItem = 505;
    /// <summary>NCID ligne de journal du coffre.</summary>
    public const ushort NcidStorageLog = 506;
    /// <summary>NCID dump complet d'un coffre.</summary>
    public const ushort NcidStorageData = 508;

    /// <summary>504 — suppression du slot <paramref name="loc"/> du
    /// coffre <paramref name="index"/>.</summary>
    public static byte[] BuildDeleteItem(int index, int loc)
    {
        var s = new OutBinStream();
        s.WriteShort((short)index);   // s16 — index du coffre (0-2)
        s.WriteShort((short)loc);     // s16 — slot supprimé
        return s.ToArray();
    }

    /// <summary>505 — item déposé au slot <paramref name="item"/>.Slot
    /// du coffre <paramref name="index"/> (record fixe, sans le loc
    /// préfixe qui est le champ s16 d'en-tête).</summary>
    public static byte[] BuildPutItem(int index, ItemEntity item)
    {
        var s = new OutBinStream();
        s.WriteShort((short)index);     // s16 — index du coffre
        s.WriteShort((short)item.Slot); // s16 — slot de destination
        WriteStorageItem(s, item);
        return s.ToArray();
    }

    /// <summary>506 — lignes de journal {log_type, log}.</summary>
    public static byte[] BuildStorageLog(
        IReadOnlyList<(short Type, string Log)> entries)
    {
        var s = new OutBinStream();
        s.WriteShort((short)entries.Count);  // u16 — nb d'entrées
        foreach (var (type, log) in entries)
        {
            s.WriteShort(type);              // s16 — log_type
            s.WriteString(log);              // str — texte du journal
        }
        return s.ToArray();
    }

    /// <summary>508 — contenu complet du coffre
    /// <paramref name="index"/> (un paquet par coffre non vide —
    /// OpenShop @0x08579440 n'émet que les coffres dont le compte
    /// d'items est ≠ 0).</summary>
    public static byte[] BuildStorageData(int index,
        IReadOnlyList<ItemEntity> items)
    {
        var s = new OutBinStream();
        s.WriteShort(0);                    // s16 +4 — toujours 0 (emit natif)
        s.WriteShort((short)index);         // s16 +6 — index du coffre
        s.WriteShort((short)items.Count);   // u16 +0x1c — nb d'items
        foreach (var item in items.OrderBy(i => i.Slot))
        {
            s.WriteShort((short)item.Slot); // s16 — loc (clé de la map)
            WriteStorageItem(s, item);
        }
        s.WriteShort(0);                    // s16 +4 — re-émis en queue
        return s.ToArray();
    }

    /// <summary>Record item fixe partagé 505/508 — ordre exact du
    /// sérialiseur natif (voir en-tête de classe).</summary>
    private static void WriteStorageItem(OutBinStream s, ItemEntity item)
    {
        s.WriteShort((short)item.ItemId);          // u16 — itemdata+4 (tid)
        s.WriteShort((short)item.Durability);      // s16 — clone+0xc
        s.WriteShort((short)item.MaxDurability);   // s16 — clone+0xe
        s.WriteString(item.Maker);                 // str — clone+0x20 maker
        // u8 — clone+0x24 : sockets ouverts (bits0-2) | identify bit3 |
        // bind bit6 — même convention que NativeItemCodec.WriteItem.
        var flags = (byte)(item.EmbeddedAmount & 7);
        if (item.Identify != 0) flags |= 8;
        if (item.IsBound) flags |= 0x40;
        s.WriteByte(flags);
        for (var i = 0; i < 6; i++)                // 6×s16 — vec+0x40
            s.WriteShort((short)item.GetEmbeddedId(i));
        s.WriteShort((short)item.EmbeddedAmount);  // s16 — clone+0x4c
        s.WriteInt(item.CreateTime);               // u32 — clone+0x18
        s.WriteShort(0);                           // u16 — clone+0x1c (non mappé DB)
        s.WriteShort((short)item.Combo);           // s16 — clone+0x10 combo_id
        for (var i = 0; i < 9; i++)                // 9×u16 — vec+0x34
            s.WriteShort((short)item.ExtraCombos[i]);
        s.WriteShort((short)item.EnchantLevel);    // s16 — +0x2c strengthen
        s.WriteShort((short)item.PlusParameter);   // s16 — clone+0x2e
        s.WriteLong(item.DueDate);                 // i64 — clone+0x50
        s.WriteShort((short)item.TrainLevel);      // u16 — attrs+0x58
        s.WriteInt((int)item.TrainExp);            // u32 — attrs+0x5c
        s.WriteShort((short)item.TrainSlot1Type);  // u16 — attrs+0x60
        s.WriteShort((short)item.TrainSlot1Level); // u16 — attrs+0x66
        s.WriteShort((short)item.TrainSlot2Type);  // u16 — attrs+0x62
        s.WriteShort((short)item.TrainSlot2Level); // u16 — attrs+0x68
        s.WriteShort((short)item.TrainSlot3Type);  // u16 — attrs+0x64
        s.WriteShort((short)item.TrainSlot3Level); // u16 — attrs+0x6a
        s.WriteShort((short)item.ExtraComboId10);  // u16 — vec+0x34[9]
        s.WriteShort((short)item.ExtraComboId11);  // u16 — vec+0x34[10]
        s.WriteShort((short)item.GoldEnchant);     // u16 — clone+0x6c
        s.WriteShort((short)item.MagicBoxEnchant1);// s16 — vec+0x70[0]
        s.WriteShort((short)item.MagicBoxEnchant2);// s16 — vec+0x70[1]
        s.WriteShort((short)item.MagicBoxEnchant3);// s16 — vec+0x70[2]
        s.WriteShort((short)item.MagicBoxEnergy);  // u16 — clone+0x7c
        s.WriteShort((short)item.MagicBoxStarLevel);// u16 — clone+0x7e
    }
}
