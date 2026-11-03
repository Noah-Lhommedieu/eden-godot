namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/C_EquipSet.ini</c> — sets d'équipement ET combos de
/// certificats (même table native, identique à S_EquipSet.ini
/// serveur — spécification <c>EquipSetParser</c>,
/// <c>GameData::CEquipSetQuery::Deserialize</c> @0x85b2xxx).
///
/// Colonnes : c0 id, c1 nom Big5, c2 vide, c3..c13 listes « ; »
/// d'ids par position, c14..c18 items récompense indexés par
/// taille-2 (c14 → set de 2, c15 → 3…).
///
/// Combos de certificats : lignes 79-93 — 5 branches (défense,
/// mêlée, distance, soin, magie) × 3 tailles (4/3/2 certs). Les
/// ids 33xxx des 4-listes se répartissent sur les positions ;
/// l'item récompense 38xxx porte l'enchant du bonus
/// (<c>ItemDef.UseEnchantId</c>) — ex. « 2H Weapon Master »
/// ATK &amp; M-ATK +16%.
/// </summary>
public static class EquipSetTable
{
    /// <summary>Ligne compilée : listes par position (≤5) + item
    /// récompense par taille (index taille-2).</summary>
    public sealed class Row
    {
        public required ushort[][] Lists;
        public required int[] Rewards;   // taille-2 → itemId
    }

    private static readonly object _lock = new();
    private static List<Row>? _rows;

    /// <summary>Item récompense du set exact (0 si aucun ne
    /// correspond) — lookup natif <c>QueryEquipSetItem</c>.</summary>
    public static int QueryItem(IReadOnlyCollection<ushort> set)
    {
        var k = set.Count;
        if (k < 2 || k - 2 >= 5) return 0;
        var ids = set.Distinct().ToArray();
        if (ids.Length != k) return 0;   // doublons → pas de set
        foreach (var row in Ensure())
        {
            var item = row.Rewards[k - 2];
            if (item != 0 && Matchable(row.Lists, ids))
                return item;
        }
        return 0;
    }

    /// <summary>Meilleur combo d'un groupe de certificats posés en
    /// une moitié (≤4) : le plus grand sous-ensemble formant un set,
    /// taille décroissante puis ordre des lignes (la priorité des
    /// branches du wiki — défense&gt;mêlée&gt;distance&gt;soin&gt;magie
    /// — est l'ordre des lignes 79-93). Retourne les ids membres
    /// triés (clé pour le 183) ou null.</summary>
    public static List<ushort>? BestCombo(ushort[] certs)
    {
        for (var size = Math.Min(4, certs.Length); size >= 2; size--)
            foreach (var sub in Subsets(certs, size))
                if (QueryItem(sub) != 0)
                    return sub.OrderBy(i => i).ToList();
        return null;
    }

    /// <summary>Sous-ensembles de taille <paramref name="size"/> dans
    /// l'ordre des indices (≤11 itérations pour 4 éléments).</summary>
    private static IEnumerable<List<ushort>> Subsets(ushort[] certs,
        int size)
    {
        var idx = new int[size];
        for (var i = 0; i < size; i++) idx[i] = i;
        while (true)
        {
            yield return idx.Select(i => certs[i]).ToList();
            var p = size - 1;
            while (p >= 0 && idx[p] == certs.Length - size + p) p--;
            if (p < 0) yield break;
            idx[p]++;
            for (var i = p + 1; i < size; i++) idx[i] = idx[i - 1] + 1;
        }
    }

    /// <summary>Assignation bipartite : chaque id du set à une
    /// position distincte dont la liste le contient — miroir de
    /// <c>EquipSetParser.Match</c> serveur.</summary>
    private static bool Matchable(ushort[][] lists, ushort[] ids)
    {
        var used = new bool[lists.Length];
        return Match(lists, ids, used, 0);
    }

    private static bool Match(ushort[][] lists, ushort[] ids,
        bool[] used, int depth)
    {
        if (depth == ids.Length) return true;
        for (var p = 0; p < lists.Length; p++)
        {
            if (used[p] || !lists[p].Contains(ids[depth])) continue;
            used[p] = true;
            if (Match(lists, ids, used, depth + 1)) return true;
            used[p] = false;
        }
        return false;
    }

    /// <summary>Table chargée une fois (immuable ; double-check lock
    /// comme EnchantTable).</summary>
    private static List<Row> Ensure()
    {
        if (_rows != null) return _rows;
        lock (_lock)
        {
            if (_rows != null) return _rows;
            try
            {
                var p = TextResolver.ResolveFile?.Invoke(
                    "data/db/C_EquipSet.ini");
                _rows = p != null ? Load(p) : new();
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke(
                    $"[equipset] C_EquipSet.ini : {e.Message}");
                _rows = new();
            }
            return _rows;
        }
    }

    /// <summary>Parse C_EquipSet.ini → lignes compilées (≥2 positions
    /// non vides, ≥1 récompense définie).</summary>
    public static List<Row> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var rows = new List<Row>();
        foreach (var r in table.Rows)
        {
            if (r.Length < 15 || !int.TryParse(r[0], out _)) continue;
            var lists = new List<ushort[]>();
            for (var i = 3; i <= 13 && i < r.Length; i++)
                lists.Add(ParseIds(r[i]));
            while (lists.Count > 0 && lists[^1].Length == 0)
                lists.RemoveAt(lists.Count - 1);
            if (lists.Count < 2) continue;
            var rewards = new int[5];
            var any = false;
            for (var i = 0; i < 5; i++)
            {
                rewards[i] = IniTable.GetInt(r, 14 + i);
                any |= rewards[i] != 0;
            }
            if (any)
                rows.Add(new Row
                    { Lists = lists.ToArray(), Rewards = rewards });
        }
        return rows;
    }

    private static ushort[] ParseIds(string cell) =>
        string.IsNullOrEmpty(cell) ? Array.Empty<ushort>()
            : cell.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => ushort.TryParse(s.Trim(), out var v)
                    ? v : (ushort)0)
                .Where(v => v != 0).ToArray();
}
