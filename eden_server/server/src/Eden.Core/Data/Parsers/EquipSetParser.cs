using System.Collections.Generic;
using System.Linq;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_EquipSet.ini — sets d'équipement ET combos de
/// certificats (même table native).
///
/// Format décodé (MyCrypt) : `|V.1|18|`, 404 lignes.
/// Mapping établi par `GameData::CEquipSetQuery::Deserialize`
/// (@0x85b2xxx, zone/decompiled.c ~1142152) :
///   [0]  id du set
///   [1]  nom (Big5)
///   [2]  scalaire inutilisé en pratique (vide sur les 404 lignes)
///   [3..13] listes « ; » d'ids d'items — 11 vecteurs u16
///          (+0xc..+0x84 ; pour un set d'armure : alternatives par
///          pièce ; pour un combo de certs : ids valides par slot)
///   [14..18] ids scalaires u16 — vecteur +0x90 « récompense »
///          indexé par **taille du set - 2** : c14 → 2 pièces,
///          c15 → 3, c16 → 4, c17 → 5 (InitEquipSetID @0x859abb0).
///
/// `InitEquipSetID` énumère les sous-ensembles : pour chaque set,
/// `EquipSetPool[{set trié d'item ids}] = (rewardItem, enchant)` où
/// `enchant = CItemData+0x60` (= col23, `UseEnchantId`) de
/// l'item récompense — interrogé par `QueryEquipSetEnchant`
/// (vtable +0x188) depuis `InvokeSpellStonesSet` (NCID 183
/// QueryLicenseSet) pour les combos de certificats.
/// </summary>
public static class EquipSetParser
{
    /// <summary>Une ligne S_EquipSet : listes d'alternatives par
    /// position + items récompense indexés par taille.</summary>
    public sealed class EquipSetDef
    {
        public int Id { get; set; }
        /// <summary>Colonnes c2..c13 non vides, chacune une liste
        /// d'ids d'items valides pour cette position.</summary>
        public List<ushort[]> PositionLists { get; } = new();
        /// <summary>Items récompense c14..c18 — index = taille du
        /// set - 2 (0 → combo/set de 2, 3 → 5 pièces).</summary>
        public ushort[] RewardItems { get; } = new ushort[5];
    }

    /// <summary>Clé de lookup canonique : ids triés joints — le pool
    /// natif est un `std::set&lt;u16&gt;` donc l'ordre d'envoi client
    /// importe peu, on trie toujours.</summary>
    public static string SetKey(IEnumerable<ushort> ids) =>
        string.Join(',', ids.OrderBy(i => i));

    /// <summary>Pool natif `EquipSetPool` — lookup paresseux par
    /// ligne : le natif pré-énumère chaque produit cartésien dans un
    /// `map&lt;set,u64&gt;` (`InitEquipSetID` @0x859abb0), mais pour les
    /// combos de certificats (24 ids par liste) cela génère des
    /// centaines de milliers d'entrées — on garde les lignes et on
    /// teste l'appartenance au moment du query, ce qui est strictement
    /// équivalent : un set correspond à une ligne ssi il existe une
    /// assignation de chaque id à une position distincte dont la
    /// liste le contient (le `std::set` natif dédupplique — les ids
    /// d'un set valide sont toujours distincts).
    /// </summary>
    public sealed class EquipSetPool
    {
        /// <summary>Ligne compilée : ≤5 listes de positions (le natif
        /// n'énumère que +0xc..+0x3c) + récompenses indexées
        /// taille-2 résolues en (itemId, enchantId).</summary>
        private sealed class CompiledRow
        {
            public required ushort[][] Lists;
            /// <summary>Enchant par taille de set (index taille-2,
            /// 0 = non défini → pas de combo de cette taille).</summary>
            public required (int ItemId, int EnchantId)[] Rewards;
        }

        private readonly List<CompiledRow> _rows = new();

        public EquipSetPool(IEnumerable<EquipSetDef> defs,
            IReadOnlyDictionary<int, int> itemEnchants)
        {
            foreach (var def in defs)
            {
                var lists = def.PositionLists.Take(5)
                    .Where(l => l.Length > 0).ToArray();
                if (lists.Length < 2) continue;
                var rewards = new (int, int)[5];
                var any = false;
                for (var k = 2; k <= lists.Length && k - 2 < 5; k++)
                {
                    var item = def.RewardItems[k - 2];
                    if (item == 0) continue;
                    var enchant = itemEnchants.TryGetValue(item,
                        out var e) ? e : 0;
                    if (enchant == 0) continue;
                    rewards[k - 2] = (item, enchant);
                    any = true;
                }
                if (any)
                    _rows.Add(new CompiledRow { Lists = lists,
                        Rewards = rewards });
            }
        }

        /// <summary>Lookup natif `QueryEquipSetEnchant` : enchant du
        /// combo si le set exact (taille + membres) correspond à une
        /// ligne.</summary>
        public int QueryEnchant(IReadOnlyCollection<ushort> set)
            => Query(set).EnchantId;

        /// <summary>Lookup natif `QueryEquipSetItem` : item
        /// récompense du set (le « cristal » 38xxx affiché).</summary>
        public int QueryItem(IReadOnlyCollection<ushort> set)
            => Query(set).ItemId;

        /// <summary>Résolution commune — le set doit avoir une taille
        /// récompensée ET une assignation positionnelle complète
        /// (produit cartésien dédupliqué du natif).</summary>
        private (int ItemId, int EnchantId) Query(
            IReadOnlyCollection<ushort> set)
        {
            var k = set.Count;
            if (k < 2 || k - 2 >= 5) return (0, 0);
            var ids = set.Distinct().ToArray();
            if (ids.Length != k) return (0, 0);   // doublons → pas de set
            foreach (var row in _rows)
            {
                var (item, enchant) = row.Rewards[k - 2];
                if (enchant == 0) continue;
                if (Matchable(row.Lists, ids))
                    return (item, enchant);
            }
            return (0, 0);
        }

        /// <summary>Assignation bipartite : chaque id du set à une
        /// position distincte dont la liste le contient — équivalent
        /// exact du produit cartésien + `std::set` du natif (≤5
        /// positions, backtracking trivial).</summary>
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
                if (used[p] || !lists[p].Contains(ids[depth]))
                    continue;
                used[p] = true;
                if (Match(lists, ids, used, depth + 1)) return true;
                used[p] = false;
            }
            return false;
        }
    }

    /// <summary>Parse S_EquipSet.ini → toutes les lignes (sets
    /// d'équipement + combos de certificats).</summary>
    public static List<EquipSetDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var defs = new List<EquipSetDef>();
        foreach (var row in table.Rows)
        {
            if (!int.TryParse(row[0], out var id)) continue;
            var def = new EquipSetDef { Id = id };
            // c3..c13 : listes « ; » d'ids par position (+0xc..+0x84,
            // 11 vecteurs) — c2 est un scalaire vide en pratique ;
            // vérifié sur les données : set armure 1 → c3-c7,
            // combo certs 79 → c3-c6, 84 → c3-c5, 89 → c3-c4.
            for (var i = 3; i <= 13 && i < row.Length; i++)
            {
                var ids = ParseIds(row[i]);
                if (ids.Length > 0) def.PositionLists.Add(ids);
                // le natif pousse une position par colonne, vide ou
                // non — on conserve la position pour le produit.
                else def.PositionLists.Add(System.Array.Empty<ushort>());
            }
            // c14..c18 : récompenses indexées taille-2..6 (+0x90)
            for (var i = 0; i < 5; i++)
                def.RewardItems[i] = (ushort)IniTable.GetInt(row, 14 + i);
            // Retire les positions vides de fin (une ligne combo de
            // 2 slots n'a que 2 listes significatives).
            while (def.PositionLists.Count > 0 &&
                   def.PositionLists[^1].Length == 0)
                def.PositionLists.RemoveAt(def.PositionLists.Count - 1);
            defs.Add(def);
        }
        return defs;
    }

    /// <summary>Construit le pool complet à partir des items (pour
    /// résoudre l'enchant de chaque item récompense).</summary>
    public static EquipSetPool BuildPool(IEnumerable<EquipSetDef> defs,
        IReadOnlyDictionary<int, int> itemEnchants)
        => new(defs, itemEnchants);

    private static ushort[] ParseIds(string cell)
    {
        if (string.IsNullOrEmpty(cell)) return System.Array.Empty<ushort>();
        return cell.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => ushort.TryParse(s.Trim(), out var v) ? v : (ushort)0)
            .Where(v => v != 0).ToArray();
    }
}
