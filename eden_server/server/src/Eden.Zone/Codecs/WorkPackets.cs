using Eden.Core.Game.Work;

namespace Eden.Zone.Codecs;

/// <summary>
/// Corps des paquets d'ordres de travail — sérialiseurs natifs
/// désassemblés (zone/decompiled.c) :
/// - <see cref="BuildWorkUpdate"/> = CNC_CZ_ClientPlayerWorkUpdate
///   (497) : <c>[u16 count] puis par entrée [s16 workNum][s16
///   workType][i32 workId][i32 remaining][string name]</c> — le
///   Serialize natif parcourt trois vecteurs parallèles remplis par
///   WorkCheckData/ToCollect (work_num s16, work_type s16,
///   work_id i32, temps restant i32, nom).
/// - <see cref="BuildWorkResult"/> = CNC_CZ_ClientPlayerWorkResult
///   (498) : <c>[i32 workId][s16 workType][s16 workNum][string
///   fmName][u16 vectorCount] puis par vecteur [u16 valueCount]
///   [i32 values…]</c> — chaque vecteur = un SWorkItem aplati par
///   CWork::GetWorkItemVector en 13 i32 :
///   [itemId, amount, socketAmount, combo, extraCombo1..9].
/// - 500 ClientPlayerWorkResultEnd = [s16 workNum] (classe générée).
/// - 499 ClientPlayerEvent = [s16][s16][i32][s16] (classe générée).
/// </summary>
public static class WorkPackets
{
    /// <summary>Entrée du 497 — un ordre en cours. Le 4e champ i32
    /// est l'enchant id (work+4 natif = work_event, 200+slot) — le
    /// client en déduit le temps restant via sa propre liste
    /// d'enchants.</summary>
    public readonly record struct WorkUpdateEntry(short WorkNum,
        short WorkType, int WorkId, int EventId, string Name);

    /// <summary>
    /// Corps 497 — [u16 count][par entrée : s16 num, s16 type,
    /// i32 id, i32 enchant/work_event, string fm_name]. Émis à
    /// l'armement (ToCollect/ToCombine — désasm @0x816ab26 : les
    /// vecteurs sont work_num/type/work_id/enchant(work+4)/
    /// fm_name(work+0x24)) et au WorkCheckData pour les ordres
    /// encore en cours.
    /// </summary>
    public static byte[] BuildWorkUpdate(
        IReadOnlyList<WorkUpdateEntry> entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)entries.Count);         // u16 — nb d'entrées
        foreach (var e in entries)
        {
            w.Write(e.WorkNum);                 // s16 — work_num (0-10)
            w.Write(e.WorkType);                // s16 — type 12-17
            w.Write(e.WorkId);                  // i32 — id de recette
            w.Write(e.EventId);                 // i32 — enchant (work+4)
            WriteString(w, e.Name);             // string — fm_name
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Corps 498 — [i32 workId][s16 workType][s16 workNum][string
    /// fmName][u16 count] puis [u16 len][i32 ×len] par produit.
    /// Le workId est négatif quand l'ordre a échoué (SaveWork(-id,0)
    /// natif — le client affiche l'échec), et count=0 en échec total.
    /// </summary>
    public static byte[] BuildWorkResult(int workId, short workType,
        short workNum, string fmName, IReadOnlyList<WorkProduct> products)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(workId);                        // i32 — id (−id = échec)
        w.Write(workType);                      // s16 — type 12-17
        w.Write(workNum);                       // s16 — work_num
        WriteString(w, fmName);                 // string — nom famille
        var filled = products.Where(p => !p.IsEmpty).ToList();
        w.Write((ushort)filled.Count);          // u16 — nb de vecteurs
        foreach (var p in filled)
        {
            w.Write((ushort)13);                // u16 — len du vecteur
            w.Write(p.ItemId);                  // i32  — item produit
            w.Write(p.Amount);                  // i32  — quantité
            w.Write(p.SocketAmount);            // i32  — sockets
            w.Write(p.Combo);                   // i32  — combo_id
            for (var e = 0; e < 9; e++)
                w.Write(p.ExtraCombos[e]);      // i32 — extra_combo N
        }
        return ms.ToArray();
    }

    /// <summary>String du protocole — [u16 len][chars Big5] comme
    /// OutBinStream::operator<<(string) natif (les noms de work
    /// chinois des .ini doivent passer en Big5).</summary>
    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Eden.Core.Data.EdenEncoding.Big5.GetBytes(
            s ?? string.Empty);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }
}
