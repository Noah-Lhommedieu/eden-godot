using System.Text;
using Eden.Core.Game.Services;

namespace Eden.Zone.Codecs;

/// <summary>
/// NCID 512 `CNC_CZ_ClientBuyBackList` — liste des items rachetables
/// (vendus à un shop PNJ cette session).
///
/// Preuve : `CNC_CZ_ClientBuyBackList::Serialize` zone @0x8344xxx
/// (relu byte-par-byte) + remplissage mission `TC_*BuyBackList`
/// (TextCommands.cc, ~0x8098xxx) qui mappe chaque vecteur au record
/// SItemData gardé en mémoire (pas de table SQL — Temp UID session).
///
/// Wire : `[u8 flag][u16 n]` puis n records de 36 champs SoA +
/// `s16` terminal (+0x17c, émis à -1 par le natif à l'ouverture).
/// flag observé = 3 (chemins « Open BuyBack UI » et relais ZMis).
/// </summary>
public static class BuybackPackets
{
    /// <summary>Flag émis par le natif (piVar13[1] = 3 dans le handler
    /// texte « Open BuyBack UI ») — fenêtre ouverte, liste VIDE : le
    /// handler client (game_.exe FUN_00a7aa50) saute tout le
    /// remplissage quand flag==3.</summary>
    public const byte NativeFlag = 3;

    /// <summary>Flag de la liste remplie — le serveur mission appelle
    /// <c>BuybackListUpdateItems('\x01')</c> (@0x8098170, appelé par
    /// TC_SellItem et la query d'ouverture), la zone relaie ce flag
    /// tel quel dans le 512 (CNE_ZMis_ZoneServerBuyBackList::Execute
    /// @0x830c970 : flag = this+0x12). Le client ne remplit la fenêtre
    /// que pour flag!=3.</summary>
    public const byte ContentFlag = 1;

    /// <summary>Flag « effacer une row » — émis par
    /// <c>CNE_ZMis_ZoneServerBuyBackGetItem::CostMoney</c> zone
    /// @0x830b630 après un rachat réussi : objet flag=2
    /// (<c>*(u8*)(cmd+4)=2</c>), count=0, et l'index racheté dans le
    /// s16 terminal (<c>*(u16*)(cmd+0x17c)=index</c>). Le client
    /// (FUN_00a7aa50) : flag==2 → erase de ses deux vecteurs de rows
    /// à l'index lu en cmd+0x208 (ce s16 terminal).</summary>
    public const byte RemoveFlag = 2;

    /// <summary>
    /// Construit le corps du 512. `entries` dans l'ordre deque natif
    /// (plus ancien → plus récent).
    /// </summary>
    public static byte[] BuildClientBuyBackList(byte flag,
        IReadOnlyList<ShopService.BuybackEntry> entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, true);

        w.Write(flag);                    // u8 — flag (3 natif)
        w.Write((ushort)entries.Count);   // u16 — nb d'entrées

        foreach (var e in entries)
        {
            var i = e.Item;
            // 1 — u16 item_id (SItemData+0xc).
            w.Write((ushort)i.ItemId);
            // 2 — s16 durability/count (record +0xe).
            w.Write((short)i.Durability);
            // 3 — s16 identify (record +0x10 — 0 = identifié).
            w.Write((short)0);
            // 4 — s16 (record +0x34 — champ non résolu, 0 courant).
            w.Write((short)0);
            // 5 — s16 bind (record +0x48 — colonne `bind`).
            w.Write((short)(i.IsBound ? 1 : 0));
            // 6 — u8 embedded_amount (record +0x1a).
            w.Write((byte)Math.Clamp(i.EmbeddedAmount, 0, 6));
            // 7-12 — 6×s16 embedded_id1-6 (record +0x1c→s16[6]).
            for (var s = 0; s < 6; s++)
                w.Write((short)i.GetEmbeddedId(s));
            // 13 — s16 witchcraft (record +0x28 — colonne `witchcraft`).
            w.Write((short)i.Witchcraft);
            // 14 — string maker (record +0x14 — nom du crafteur).
            WriteString(w, "");
            // 15-25 — 11×s16 extra_combo_id1-11 (record +0x38→s16[11]) ;
            // notre modèle en porte 9 → 2 zéros finaux.
            for (var c = 0; c < 11; c++)
                w.Write((short)(c < i.ExtraCombos.Length
                    ? i.ExtraCombos[c] : 0));
            // 26 — s16 strengthen (record +0x44).
            w.Write((short)i.EnchantLevel);
            // 27 — s16 cur_maxdurability (record +0x46).
            w.Write((short)i.MaxDurability);
            // 28 — i64 due_date (record +0x4c).
            w.Write((long)i.DueDate);
            // 29 — CEquipTrainAttrs (record +0x58, 20 o) — wire :
            //   s16 train_level, u32 train_exp, puis 6×s16 dans
            //   l'ordre +8,+14,+10,+16,+12,+18 (train_slot1-3
            //   type/level — entrelacés). Non trackés → 0.
            w.Write((short)0);       // s16  +0x00 train_level
            w.Write(0u);             // u32  +0x04 train_exp
            w.Write((short)0);       // s16  +0x08 train_slot1_type
            w.Write((short)0);       // s16  +0x0e train_slot3_level
            w.Write((short)0);       // s16  +0x0a train_slot1_level
            w.Write((short)0);       // s16  +0x10 train_slot3_type
            w.Write((short)0);       // s16  +0x0c train_slot2_level
            w.Write((short)0);       // s16  +0x12 train_slot2_type
            // 30 — s16 (record +0x54).
            w.Write((short)0);
            // 31 — u32 prix de rachat (deque prix parallèle native =
            // total_price payé à la vente).
            w.Write((uint)Math.Max(e.PaidPrice, 0));
            // 32 — s16 (record +0x6c).
            w.Write((short)0);
            // 33-34 — 2×s16 magicbox_enchant1-2 (record +0x70→s16[3] ;
            // le natif zone ne forwarde que 36 des 37 vecteurs
            // mission — 3e élément absorbé, non résolu).
            w.Write((short)i.MagicBoxEnchant1);
            w.Write((short)i.MagicBoxEnchant2);
            // 35-36 — 2×u16 (records +0x7c/+0x7e — color/starlevel ?).
            w.Write((ushort)i.MagicBoxEnergy);
            w.Write((ushort)i.MagicBoxStarLevel);
        }

        // s16 terminal (+0x17c) — -1 natif (piVar13[0x5f] = 0xffff).
        w.Write((short)-1);
        return ms.ToArray();
    }

    /// <summary>
    /// Corps du 512 « rachat effectué » — flag=2, aucune entrée, le
    /// s16 terminal porte l'index racheté (désasm CostMoney @0x830b630
    /// : count reste 0, cmd+0x17c = buyback_index reçu du client).
    /// Le client retire la row correspondante de sa liste ; sans ce
    /// paquet la row fantôme reste cliquable.
    /// </summary>
    public static byte[] BuildClientBuyBackRemove(int removedIndex)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, true);

        w.Write(RemoveFlag);              // u8 — flag=2 (effacement)
        w.Write((ushort)0);               // u16 — 0 entrée
        w.Write((short)removedIndex);     // s16 terminal — index effacé
        return ms.ToArray();
    }

    /// <summary>String BinStream : u16 len + bytes (pas de nul).</summary>
    private static void WriteString(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write((ushort)b.Length);
        w.Write(b);
    }
}
