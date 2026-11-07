using Eden.Core.Data.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Eden.Core.Game.Services;

/// <summary>Miroir de <c>lapis::CItemFactory::RandItemCombo</c>
/// (zone @0x080a5480) et du sélecteur de produit
/// <c>SelectWorkItemCombo</c> (@0x080a69f0) — le roll de combos posé
/// sur les items fraîchement créés (produits work <c>EndCombine</c>
/// @0x081657b5, drops <c>DiceItemCombo</c>).
///
/// Séquence native prouvée :
/// <list type="number">
/// <item>Candidats : kind (CItemData+0x20) dans la liste col3 et
/// RequiredLevel (CItemLimit+0x22) dans [col5,col6]. La col4
/// (<see cref="CraftRecipeParser.ItemCombo.AttrType"/>, &lt;4 requis)
/// donne le pool : 0→« General », 1→« PartKnock » (noms du DebugLog
/// natif), 3→collectée mais jamais tirée ; 2 → seulement si
/// quality==6 (pool à tirage unique + vecteur tier2) ; sinon ignoré.</item>
/// <item>Si la pool0 OU la pool1 est vide → « get_combos empty! » et
/// sortie sans aucun combo (avant même le switch qualité).</item>
/// <item>Nombre de tirages par qualité (switch param_1) :
/// q0→(1,1) ; q2→(2,2) ; q3,q5→(1,0) ; q4,q6→(3,3) ; autres→0.
/// Pour q6, UN tirage supplémentaire dans la pool tier2 (sans flag
/// ni retrait).</item>
/// <item>Chaque tirage pool0/pool1 : <c>random() % totalCumul</c>
/// (total figé avant la boucle) → premier cumul &gt; roll. Trouvé =
/// tirage consommé ; flag col9 (+0x1b) == 0 → nœud effacé (sans
/// remise, clés cumulatives inchangées) ; raté (roll ≥ cumul max
/// restant) = rejoué sans consommer.</item>
/// <item>Si aucun tirage : <c>param_4</c> et le vecteur restent à 0.
/// Sinon extras = ids tirés dans l'ordre (pool0 puis pool1 puis
/// tier2), complétés à 11 par 0 ; <c>param_4</c> = le **groupe**
/// (col7, +0x18) dominant — première clé ayant le max de tirages.</item>
/// </list></summary>
public static class ItemComboRollService
{
    /// <summary>Roll natif complet. Retourne (mainCombo,
    /// extras[11]) — tous 0 si aucun candidat.</summary>
    /// <param name="quality">Qualité instance (clone+0x2c / CItem+0xf4,
    /// 0-6 — produits work frais = 0).</param>
    /// <param name="kind">Type d'item (CItemData+0x20 = S_Item col8).</param>
    /// <param name="requiredLevel">Niveau requis (CItemLimit+0x22).</param>
    /// <param name="combos">Table S_ItemCombo.</param>
    /// <param name="excludedIds">Set natif param_7 (ids +0xb8 exclus
    /// lorsqu'il est non vide — vide pour SelectWorkItemCombo).</param>
    /// <param name="next">RNG injectable : entier dans [0,total[.</param>
    public static (int Main, int[] Extras) Roll(int quality, int kind,
        int requiredLevel, IReadOnlyList<CraftRecipeParser.ItemCombo> combos,
        IReadOnlySet<int>? excludedIds = null,
        Func<int, int>? next = null)
    {
        var rng = next ?? (t => Random.Shared.Next(t));
        var extras = new int[11];

        // Pools par tier : [0]=« General », [1]=« PartKnock »,
        // [2]=tier2 (q6 uniquement), [3]=tier3 (collectée mais
        // jamais tirée — comme le natif).
        var pools = new List<(CraftRecipeParser.ItemCombo C, int Cum)>[4];
        var totals = new int[4];
        var tier2 = new List<CraftRecipeParser.ItemCombo>();
        for (var i = 0; i < 4; i++)
            pools[i] = new List<(CraftRecipeParser.ItemCombo, int)>();

        foreach (var c in combos)
        {
            var tier = c.AttrType;
            if (tier < 0 || tier > 3) continue;
            if (c.ApplicableItemTypes.Count == 0 ||
                !c.ApplicableItemTypes.Contains(kind)) continue;
            if (requiredLevel < c.MinLevel || requiredLevel > c.MaxLevel)
                continue;
            if (tier == 2)
            {
                if (quality != 6) continue;
                tier2.Add(c);
                if (excludedIds == null || excludedIds.Count == 0 ||
                    excludedIds.Contains(c.Id))
                {
                    totals[2] += c.Value;
                    pools[2].Add((c, totals[2]));
                }
            }
            else
            {
                totals[tier] += c.Value;
                // natif : cum égal (poids 0) → écrasement de l'entrée.
                var pool = pools[tier];
                if (pool.Count > 0 && pool[^1].Cum == totals[tier])
                    pool[^1] = (c, totals[tier]);
                else
                    pool.Add((c, totals[tier]));
            }
        }

        // Fallback natif : q6 & pool2 vide → réinsérer la liste tier2.
        if (quality == 6 && pools[2].Count == 0 && tier2.Count > 0)
            foreach (var c in tier2)
            {
                totals[2] += c.Value;
                pools[2].Add((c, totals[2]));
            }

        // Natif : pool0 (« General ») OU pool1 (« PartKnock ») vide →
        // DebugLog « get_combos empty! » et sortie sans aucun combo,
        // quel que soit le nombre de tirages demandé.
        if (pools[0].Count == 0 || pools[1].Count == 0)
            return (0, extras);

        var (n0, n1) = quality switch
        {
            0 => (1, 1),
            2 => (2, 2),
            3 or 5 => (1, 0),
            4 or 6 => (3, 3),
            _ => (0, 0),
        };

        var picked = new List<CraftRecipeParser.ItemCombo>();
        Pick(pools[0], totals[0], n0, rng, picked);
        Pick(pools[1], totals[1], n1, rng, picked);
        // Pool tier2 (q6) : UN seul tirage, roll % total, premier
        // cum > roll — pas de flag ni de retrait dans le natif.
        if (quality == 6 && pools[2].Count > 0 && totals[2] > 0)
        {
            var roll = rng(totals[2]);
            var idx = pools[2].FindIndex(e => roll < e.Cum);
            if (idx >= 0) picked.Add(pools[2][idx].C);
        }

        if (picked.Count == 0) return (0, extras);

        for (var i = 0; i < picked.Count && i < extras.Length; i++)
            extras[i] = picked[i].Id;

        // Groupe dominant (col7) → combo principal. Égalité → le plus
        // petit tier (ordre croissant du map natif).
        var main = picked.GroupBy(c => c.Tier)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First().Key;
        return (main, extras);
    }

    /// <summary>Sélecteur de produit work — <c>SelectWorkItemCombo</c>
    /// natif : équipement seulement (kind 1..0x22, ≠0x10 ; le kind 0x10
    /// est exclu par le call site EndCombine), qualité 0.</summary>
    public static bool IsWorkComboEligible(int kind)
        => kind is >= 1 and <= 0x22 && kind != 0x10;

    /// <summary>Tirage dans une pool — miroir du while natif :
    /// roll % total (fixe), premier cum &gt; roll ; trouvé = pick
    /// consommé (quel que soit flag) ; flag==0 → nœud effacé (les clés
    /// cumulatives restantes ne bougent pas) ; raté (roll ≥ max cum
    /// restant) = tirage rejoué sans consommer d'itération.</summary>
    private static void Pick(
        List<(CraftRecipeParser.ItemCombo C, int Cum)> pool,
        int total, int count, Func<int, int> rng,
        List<CraftRecipeParser.ItemCombo> picked)
    {
        var n = 0;
        while (pool.Count > 0 && n < count && total > 0)
        {
            var roll = rng(total);
            var idx = pool.FindIndex(e => roll < e.Cum);
            if (idx < 0) continue; // raté → re-roll, pas consommé
            var c = pool[idx].C;
            picked.Add(c);
            n++;
            if (c.Flag == 0) pool.RemoveAt(idx);
        }
    }
}
