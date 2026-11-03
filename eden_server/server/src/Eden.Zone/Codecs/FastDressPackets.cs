using Eden.Core.Game.Inventory;
using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>
/// Codec des paquets de tenue rapide — NCIDs 593 (liste complète à
/// l'entrée) et 594 (mise à jour/mode). Les classes générées de
/// <c>ZoneMessages.g.cs</c> ont aplati le vecteur de records en
/// champs fixes et ne peuvent pas représenter le format réel :
/// on sérialise ici à partir du désasm natif.
///
/// Source de preuve : zone/decompiled.c —
/// <c>CNC_CZ_ClientEnterNodeFastDress::Serialize</c> @0x08232e70 :
/// <c>[u16 n][n×(6×i32)]</c> ;
/// <c>CNC_CZ_ClientUpdateFastDressData::Serialize</c> @0x08232e90 :
/// <c>[s16 mode]</c> puis, si mode ∉ {1,7,8},
/// <c>[s16 n][i32 page][n×(6×i32)]</c>.
/// Record wire (champs 1..6 de SFastDressInfo, +0x18..+0x2c) :
/// <c>item_id, container_index, from_loc, from_index, page,
/// tar_loc</c> — uid(+0x14) et flag(+0x30) internes.
/// </summary>
public static class FastDressPackets
{
    /// <summary>593 — liste complète des records LIVE à l'entrée de
    /// la fenêtre (toutes pages, records présents dans la map live —
    /// les 24×3 lignes seedées sont toutes émises).</summary>
    public static byte[] BuildEnterNode(
        IEnumerable<FastDressRecord> records)
    {
        var list = records.ToList();
        var w = new OutBinStream();
        w.WriteShort((short)list.Count);   // u16 — nb de records
        foreach (var r in list) WriteRecord(w, r);
        return w.ToArray();
    }

    /// <summary>594 — mise à jour de page. <paramref name="mode"/> =
    /// code natif (+0x14 du paquet) : 0=Save OK, 1=échec bind,
    /// 2=erreur (refresh working), 3=page revertée (live),
    /// 4=records working modifiés, 5=record retiré, 7/8=erreurs
    /// d'apply. Modes 1/7/8 → mode seul (le natif saute le bloc —
    /// <c>CNC_CZ_ClientUpdateFastDressData::Serialize</c>).</summary>
    public static byte[] BuildUpdate(short mode, int page,
        IEnumerable<FastDressRecord>? records)
    {
        var w = new OutBinStream();
        w.WriteShort(mode);                // s16 — mode (ctor +0x14)
        if (mode is 1 or 7 or 8) return w.ToArray();
        var list = records?.ToList() ?? [];
        w.WriteShort((short)list.Count);   // s16 — nb de records
        w.WriteInt(page);                  // i32 — page (ctor +0x10)
        foreach (var r in list) WriteRecord(w, r);
        return w.ToArray();
    }

    /// <summary>Un record wire — les 6 i32 sérialisés du
    /// `SFastDressInfo` natif (ordre du Serialize).</summary>
    private static void WriteRecord(OutBinStream w, FastDressRecord r)
    {
        w.WriteInt(r.ItemId);        // +0x18 item_id
        w.WriteInt(r.ContainerType); // +0x1c container_index (type)
        w.WriteInt(r.FromLoc);       // +0x20 from_loc (slot)
        w.WriteInt(r.FromIndex);     // +0x24 from_index (conteneur)
        w.WriteInt(r.Page);          // +0x28 page
        w.WriteInt(r.TarLoc);        // +0x2c tar_loc (clé dress)
    }
}
