using Eden.Core.Game.Vending;
using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>
/// Codec des paquets de l'étal joueur (market booth) — NCIDs 71-80
/// entrants et 375-379 sortants. Les classes générées de
/// <c>ZoneMessages.g.cs</c> modélisent ces paquets en champs fixes
/// et ne peuvent pas représenter les vecteurs parallèles natifs :
/// le flow intercepte donc le payload brut et passe par ce codec.
///
/// Source de preuve : zone/decompiled.c —
/// <c>CNE_CZ_ZoneServerVendorSellItem::Deserialize</c>,
/// <c>...VendorBuy::Deserialize</c>,
/// <c>CNC_CZ_ClientVendorUpdateView::Serialize</c> (@0x8134xxx),
/// <c>ClientVendorClose/Status/CloseView::Serialize</c>
/// (@0x81355b0/@0x8135xxx/@0x8132ae0).
/// </summary>
public static class VendorPackets
{
    /// <summary>Une ligne de marchandise du paquet d'ouverture.</summary>
    /// <param name="Container">s16 — vecteur 1 : container_index de
    /// l'item à vendre (BagId chez nous).</param>
    /// <param name="Index">s16 — vecteur 2 : loc index (slot).</param>
    /// <param name="Loc">s16 — vecteur 3 : loc attendu côté client
    /// (validation natif seule, non recopiée dans le record).</param>
    /// <param name="Price">i32 — vecteur 4 : prix du lot.</param>
    public readonly record struct SellEntry(short Container,
        short Index, short Loc, int Price);

    /// <summary>Une ligne d'achat du paquet 76.</summary>
    /// <param name="Quantity">s16 — quantité demandée (le natif
    /// traite le lot complet ; le flux valide qty == count).</param>
    /// <param name="Index">u8 — index de la marchandise dans la map
    /// de l'étal.</param>
    public readonly record struct BuyLine(short Quantity, byte Index);

    /// <summary>Résultat du parse 77.</summary>
    public sealed record SellRequest(int RegionType, string Title,
        SellEntry[] Entries);

    /// <summary>Résultat du parse 76.</summary>
    public sealed record BuyRequest(int SellerId, BuyLine[] Lines);

    /// <summary>
    /// Parse <c>CNE_CZ_ZoneServerVendorSellItem</c> (77) —
    /// <c>[i32 regionType][str title][u8 n][n×(s16,s16,s16,i32)]</c>.
    /// Le désasm du Deserialize lit 4 <c>vector</c> : deux de s16
    /// (containers, indices), un de s16 (locs) et un de i32 (prix) —
    /// entrelacés n × (2+2+2+4) = 10 octets.
    /// </summary>
    public static SellRequest? ParseSellItem(byte[] payload)
    {
        try
        {
            var r = new InBinStream(payload);
            var regionType = r.ReadInt();       // i32 — type région
                                                // (600-609 pour stall)
            var title = r.ReadString();         // str — titre d'étal
            var n = r.ReadByte();               // u8 — nb de lignes
            var entries = new SellEntry[n];
            for (var i = 0; i < n; i++)
                entries[i] = new SellEntry(
                    r.ReadShort(),              // s16 — container
                    r.ReadShort(),              // s16 — index (slot)
                    r.ReadShort(),              // s16 — loc attendu
                    r.ReadInt());               // i32 — prix du lot
            return new SellRequest(regionType, title, entries);
        }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>
    /// Parse <c>CNE_CZ_ZoneServerVendorBuy</c> (76) —
    /// <c>[i32 sellerId][u16 n][n×(s16 qty, u8 idx)]</c>
    /// (Deserialize désassemblé : un seul passage par ligne).
    /// </summary>
    public static BuyRequest? ParseBuy(byte[] payload)
    {
        try
        {
            var r = new InBinStream(payload);
            var sellerId = r.ReadInt();         // i32 — charId vendeur
            var n = r.ReadUShort();             // u16 — nb de lignes
            var lines = new BuyLine[n];
            for (var i = 0; i < n; i++)
                lines[i] = new BuyLine(
                    r.ReadShort(),              // s16 — quantité
                    r.ReadByte());              // u8  — index marchandise
            return new BuyRequest(sellerId, lines);
        }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>Parse <c>AskVendorContainer</c> (72) —
    /// <c>[i32 targetCharId]</c>.</summary>
    public static int? ParseAskContainer(byte[] payload)
    {
        try { return new InBinStream(payload).ReadInt(); }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>Parse <c>VendorClose</c> (74) — le Deserialize natif
    /// lit DEUX u8 mais n'utilise que le premier (serialisé à la
    /// fermeture dans le 375) : <c>[u8 closeMode][u8 ignored]</c>.</summary>
    public static byte? ParseClose(byte[] payload)
    {
        try { return new InBinStream(payload).ReadByte(); }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>Parse <c>VendorCloseView</c> (75) —
    /// <c>[i32 sellerId]</c>.</summary>
    public static int? ParseCloseView(byte[] payload)
    {
        try { return new InBinStream(payload).ReadInt(); }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>377 <c>CNC_CZ_ClientVendorStatus</c> — Serialize
    /// @0x8135xxx : <c>[u8 action][u8 detail]</c>.</summary>
    public static byte[] BuildStatus(byte action, byte detail) =>
        new[] { action, detail };

    /// <summary>376 <c>CNC_CZ_ClientVendorCloseView</c> —
    /// <c>[i32 sellerId]</c> (@0x8132ae0). Émis aux viewers quand le
    /// vendeur ferme (avec le texte 0xf07).</summary>
    public static byte[] BuildCloseView(int sellerId)
    {
        var b = new byte[4];
        BitConverter.TryWriteBytes(b, sellerId);   // i32 — charId vendeur
        return b;
    }

    /// <summary>375 <c>CNC_CZ_ClientVendorClose</c> —
    /// <c>[i32 charId][u8 closeMode]</c> (@0x81355b0 : i32 puis
    /// char). Émis au vendeur après fermeture ; closeMode = le u8 du
    /// paquet entrant (74).</summary>
    public static byte[] BuildClose(int charId, byte closeMode)
    {
        var b = new byte[5];
        BitConverter.TryWriteBytes(b, charId);     // i32 — charId vendeur
        b[4] = closeMode;                          // u8 — mode de fermeture
        return b;
    }

    /// <summary>379 <c>CNC_CZ_ClientVendorContainer</c> —
    /// <c>[i32 targetId][u8 count]</c> (le ctor natif
    /// <c>(i32, u8)</c> n'est jamais instancié dans la zone exportée
    /// = chemin mort côté vendeur, mais l'exécution
    /// AskVendorContainer l'émet sur target-miss). Émis seulement en
    /// échec cible, count 0.</summary>
    public static byte[] BuildContainerEmpty(int targetId)
    {
        var b = new byte[5];
        BitConverter.TryWriteBytes(b, targetId);   // i32 — cible
        // b[4] = 0 — u8 : vide
        return b;
    }

    /// <summary>
    /// 378 <c>CNC_CZ_ClientVendorUpdateView</c> — Serialize
    /// @0x8134xxx : <c>[i32 sellerId][str title][u8 n]</c> puis n
    /// records de 96 octets (noeud map natif, clé u8 triée). Chaque
    /// champ miroire <c>SStallGoodsInfo</c> rempli par
    /// <c>VendorSellItem</c> ; l'ordre du wire est celui du Serialize
    /// — y compris les deux groupes non contigus : les slots de
    /// train (clone+0x60..0x6a) sont émis dans l'ordre permuté
    /// 0,3,1,4,2,5 et les extra-combos 9/10 arrivent APRÈS, juste
    /// avant les enchantements magic-box.
    /// </summary>
    public static byte[] BuildUpdateView(int sellerId, string title,
        IEnumerable<StallService.StallGood> goods)
    {
        var s = new OutBinStream();
        s.WriteInt(sellerId);                     // i32 — charId vendeur
        s.WriteString(title);                     // str — titre d'étal
        var list = goods is ICollection<StallService.StallGood> c
            ? c : goods.ToList();
        s.WriteByte((byte)list.Count);            // u8 — nb d'entrées
        foreach (var g in list)
        {
            s.WriteByte(g.Index);                 // u8  — noeud+0x10 clé map
            s.WriteInt(g.ItemId);                 // u32 — +0x14 item_id
            s.WriteInt(g.RequiredLevel);          // i32 — +0x18 itemdata+0x3e
            s.WriteShort(g.Container);            // s16 — +0x1c container
            s.WriteShort(g.Slot);                 // s16 — +0x1e index
            s.WriteInt(g.LocIndex);               // i32 — +0x20 clone+0xc
            s.WriteInt(g.LocExtra);               // i32 — +0x24 clone+0xe
            s.WriteShort((short)Math.Clamp(g.Durability,
                short.MinValue, short.MaxValue)); // u16 — +0x28 clone+0x1c
            s.WriteShort(g.Combo);                // s16 — +0x2a clone+0x10
            s.WriteByte((byte)(g.FlagLow3 & 7));  // u8  — +0x2c clone+0x24&7
            for (var i = 0; i < 6; i++)
                s.WriteShort(g.EmbeddedIds[i]);   // s16×6 — +0x2e embedded
            for (var i = 0; i < 9; i++)
                s.WriteShort(g.ExtraCombos[i]);   // u16×9 — +0x3a extra[0..8]
            s.WriteInt(g.Price);                  // i32 — +0x50 prix du lot
            s.WriteShort(g.Quality);              // s16 — +0x54 clone+0x2c
            s.WriteShort(g.Witchcraft);           // s16 — +0x56 clone+0x4c
            s.WriteShort(g.Clone2e);              // s16 — +0x58 clone+0x2e
            s.WriteShort(g.TrainLevel);           // u16 — +0x5c clone+0x58
            s.WriteInt(unchecked((int)g.DueDate));// u32 — +0x60 clone+0x5c
            // Slots de train — ordre natif permuté (Serialize émet
            // les noeuds +0x64,+0x6a,+0x66,+0x6c,+0x68,+0x6e =
            // clone+0x60,0x66,0x62,0x68,0x64,0x6a).
            s.WriteShort(g.TrainSlots[0]);        // u16 — +0x64 ← clone+0x60
            s.WriteShort(g.TrainSlots[3]);        // u16 — +0x6a ← clone+0x66
            s.WriteShort(g.TrainSlots[1]);        // u16 — +0x66 ← clone+0x62
            s.WriteShort(g.TrainSlots[4]);        // u16 — +0x6c ← clone+0x68
            s.WriteShort(g.TrainSlots[2]);        // u16 — +0x68 ← clone+0x64
            s.WriteShort(g.TrainSlots[5]);        // u16 — +0x6e ← clone+0x6a
            s.WriteShort(g.ExtraCombos[9]);       // u16 — +0x4c extra[9]
            s.WriteShort(g.ExtraCombos[10]);      // u16 — +0x4e extra[10]
            for (var i = 0; i < 3; i++)
                s.WriteShort(g.MagicBox[i]);      // s16×3 — +0x70 clone+0x70
            s.WriteShort((short)Math.Clamp(g.MagicBoxEnergy,
                short.MinValue, short.MaxValue)); // u16 — +0x76 clone+0x7c
            s.WriteShort((short)Math.Clamp(g.MagicBoxStar,
                short.MinValue, short.MaxValue)); // u16 — +0x78 clone+0x7e
        }
        return s.ToArray();
    }

    /// <summary>Longueur exacte d'un record de marchandise —
    /// 1+4+4+2+2+4+4+2+2+1+12+18+4 +2+2+2+2+4+12+4+6+2+2 =
    /// 98 octets (Serialize complet : les derniers u16 à +0x76/0x78
    /// terminent le record).</summary>
    public const int GoodsRecordBytes = 98;
}
