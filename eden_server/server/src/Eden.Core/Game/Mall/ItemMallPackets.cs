using Eden.Core.Networking;

namespace Eden.Core.Game.Mall;

/// <summary>
/// Builders des paquets item mall dont le format natif ne correspond
/// PAS aux classes générées (les records sont des vecteurs parallèles
/// sérialisés en records contigus — désasm des <c>::Serialize</c> dans
/// re/ghidra/world/decompiled.c).
///
/// 62 CNC_CW_ClientItemMallList (@0x80…21310) :
///   [u16 version][u16 count]
///   count × { s16 group_key(=money_unit*100+item_group), s16 index,
///             u16 item_id, u16 item_num,
///             u32 point, u32 special_price, u16 num_limit,
///             i64 item_color, i32 not_sell_date(epoch),
///             str fortune_bag, str note, u16 level_limit,
///             i32 sell_date(epoch) }
///   Batching natif : nouveau paquet quand count &gt; 19 ou
///   bytes accumulés (36 + len(fortune_bag) + len(note)) &gt; 4999.
///
/// 79 CNC_CW_ClientItemMallLimit (@0x8062640) :
///   [u16 version][u16 count]
///   count × { s16 group_key(=money_unit*100+item_group), s16 index,
///             u16 item_id, u16 item_num, u16 sell_amount }
///
/// ZW 24 CNC_ZW_ZoneServerItemMallReceieveAttempt (@0x80dd370 W→Z) :
///   [u32 receipt_id][u32 char_id][u16 item_id][u16 qty][i64 item_color]
///
/// ZW 12 CNC_ZW_WorldServerItemMallReceieveAttempt (@0x82a…Z→W) :
///   [s16 result][u32 receipt_id][u32 char_id][u32 serial][u32 create_time]
/// </summary>
public static class ItemMallPackets
{
    /// <summary>Construit les paquets 62 — batching natif
    /// (≤ 20 records ou 4999 octets de charge par paquet).</summary>
    public static IEnumerable<byte[]> BuildCatalogBatches(
        ItemMallCatalog catalog)
    {
        var batch = new List<ItemMallEntry>(20);
        var bytes = 0;
        foreach (var e in catalog.Entries)
        {
            batch.Add(e);
            // Accumulation native : 0x24 fixe + longueurs des 2 strings.
            bytes += 0x24 + e.FortuneBag.Length + e.Note.Length;
            if (batch.Count >= 20 || bytes > 4999)
            {
                yield return SerializeCatalog(catalog.Version, batch);
                batch = new List<ItemMallEntry>(20);
                bytes = 0;
            }
        }
        if (batch.Count > 0)
            yield return SerializeCatalog(catalog.Version, batch);
    }

    /// <summary>Body d'un paquet 62 pour un lot d'entrées.</summary>
    private static byte[] SerializeCatalog(ushort version,
        IReadOnlyList<ItemMallEntry> batch)
    {
        var s = new OutBinStream();
        // u16 = ItemMallVersion (this+4 natif).
        s.WriteShort((short)version);
        // u16 = nombre de records (taille du vecteur +0x8).
        s.WriteShort((short)batch.Count);
        foreach (var e in batch)
        {
            // s16 = clé de la map externe AllItemMallItem :
            // money_unit*100 + item_group (sVar10 du load natif). Le
            // client en retire mu=key/100, group=key%100 — écrire le
            // group brut donnait mu=0 → onglets AP/bonus tous vides
            // (bug live « item mall + loyalty vides »).
            s.WriteShort((short)(e.MoneyUnit * 100 + e.Group));
            // s16 item_index (clé de map interne).
            s.WriteShort((short)e.Index);
            // u16 item_id (struct +0x2).
            s.WriteShort((short)e.ItemId);
            // u16 item_num — quantité par paquet (struct +0x4, 0→1).
            s.WriteShort((short)e.ItemNum);
            // u32 point — prix plein (struct +0x8).
            s.WriteInt((int)e.Point);
            // u32 special_price — prix promo (struct +0xc).
            s.WriteInt((int)e.SpecialPrice);
            // u16 num_limit — limite/compte (struct +0x10, groupe 8 seul).
            s.WriteShort((short)e.NumLimit);
            // i64 item_color (struct +0x14).
            s.WriteLong(e.ItemColor);
            // i32 not_sell_date epoch (struct +0x1c, fin de vente).
            s.WriteInt(e.NotSellDate);
            // str fortune_bag (struct +0x20).
            s.WriteString(e.FortuneBag);
            // str note (struct +0x24).
            s.WriteString(e.Note);
            // u16 level_limit (struct +0x2c).
            s.WriteShort((short)e.LevelLimit);
            // i32 sell_date epoch (struct +0x30, 0 = déjà en vente).
            s.WriteInt(e.SellDate);
        }
        return s.ToArray();
    }

    /// <summary>Body du paquet 79 — limites d'achat du compte.</summary>
    public static byte[] BuildLimit(ushort version,
        IReadOnlyList<ItemMallLimitRow> rows)
    {
        var s = new OutBinStream();
        // u16 = ItemMallVersion (this+4 = piVar12[1] natif).
        s.WriteShort((short)version);
        // u16 = nombre de records.
        s.WriteShort((short)rows.Count);
        foreach (var r in rows)
        {
            // s16 = clé encodée money_unit*100 + item_group — même
            // encodage que le 62 (GetAllItemMallLimit natif :
            // local_1e = local_18*100 + local_14).
            s.WriteShort((short)(r.MoneyUnit * 100 + r.ItemGroup));
            // s16 item_index.
            s.WriteShort((short)r.ItemIndex);
            // u16 item_id.
            s.WriteShort((short)r.ItemId);
            // u16 item_num.
            s.WriteShort((short)r.ItemNum);
            // u16 sell_amount — déjà acheté par ce compte.
            s.WriteShort((short)r.SellAmount);
        }
        return s.ToArray();
    }

    /// <summary>Body du ZW 24 — ordre de livraison monde→zone.</summary>
    public static byte[] BuildZwDelivery(uint receiptId, uint charId,
        int itemId, int itemQuantity, long itemColor)
    {
        var s = new OutBinStream();
        // u32 receipt/attempt id (this+0x14 natif).
        s.WriteInt((int)receiptId);
        // u32 char_id destinataire (this+0x18).
        s.WriteInt((int)charId);
        // u16 item_id (this+0x1c).
        s.WriteShort((short)itemId);
        // u16 item_quantity (this+0x1e).
        s.WriteShort((short)itemQuantity);
        // i64 item_color (this+0x20).
        s.WriteLong(itemColor);
        return s.ToArray();
    }

    /// <summary>Parse du ZW 12 — résultat de livraison zone→monde :
    /// retourne (result, receiptId, charId, serial, createTime).</summary>
    public static (short Result, uint ReceiptId, uint CharId,
        uint Serial, uint CreateTime)? ParseZwDeliveryResult(
        ReadOnlySpan<byte> body)
    {
        // s16 result | u32 receipt | u32 charId | u32 serial | u32 createTime.
        if (body.Length < 18) return null;
        return (BitConverter.ToInt16(body),
            BitConverter.ToUInt32(body[2..]),
            BitConverter.ToUInt32(body[6..]),
            BitConverter.ToUInt32(body[10..]),
            BitConverter.ToUInt32(body[14..]));
    }
}
