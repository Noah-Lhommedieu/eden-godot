using System;
using System.Collections.Generic;
using System.IO;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser de <c>S_LifeSkill.ini</c> (|V.1|36|, 594 recettes) — les
/// recettes de métiers natifs chargées par
/// <c>GameData::CLifeSkillQuery::Deserialize</c> (zone @0x86058d0).
///
/// Layout des colonnes (vérifié sur le désasm — offsets CLifeSkillData) :
///   [0]  Id (u16 — clé QueryLifeSkill)
///   [1]  GroupType (+0x04 — 1 = analyse joaillerie, 2-5 = professions)
///   [2]  ItemId (+0x08 — sortie produite / item consommé en analyse)
///   [3]  Nom (Big5)
///   [4]  Count s16 (+0x10 — qté produite / qté consommée du slot)
///   [5]  ReqLevel s16 (+0x12 — niveau perso requis)
///   [6]  ExpBase (+0x14)
///   [7]  ExpVar  (+0x18 — gain = base-var+rand(2*var+1) si base≥var)
///   [8]  ExpCap  (+0x1c — seuil de maîtrise)
///   [9]  Gold    (+0x20 — coût de production)
///   [10] NextRecipeId (+0x24 — cible du LevelUp)
///   [11] PrevRecipeId (+0x28 — chaîne avant, Forget exige ≥2)
///   [12-20] ≤3 triplets matériaux (id, nom, qté) → vector SStuff +0x2c
///   [21..]   triplets résultats (id, nom, poids) → map pondérée
///            cumulative +0x3c (poids cumulé → id recette).
/// </summary>
public static class LifeSkillParser
{
    /// <summary>Recette métier (miroir de GameData::CLifeSkillData).</summary>
    public sealed class Recipe
    {
        public int Id;
        /// <summary>Groupe métier natif (CLifeSkillData+0x04) :
        /// 1 = analyse joaillerie, 2-5 = professions de production.</summary>
        public int Group;
        /// <summary>Item produit (production) ou item source consommé
        /// (analyse) — CLifeSkillData+0x08.</summary>
        public int ItemId;
        public string Name = string.Empty;
        /// <summary>Quantité produite / consommée — +0x10.</summary>
        public int Count;
        /// <summary>Niveau de perso requis — +0x12 (Produce exige aussi
        /// niveau ≥ 30 en dur natif).</summary>
        public int ReqLevel;
        public int ExpBase;    // +0x14
        public int ExpVar;     // +0x18
        public int ExpCap;     // +0x1c
        public int Gold;       // +0x20 — coût GainGold(-x)
        /// <summary>Recette suivante de la chaîne (col10, +0x24) —
        /// apprise par LevelUpLifeSkill quand la maîtrise est atteinte.</summary>
        public int NextId;
        /// <summary>Recette précédente (col11, +0x28) — ForgetLifeSkill
        /// l'exige ≥ 2 et remet son exp à 0.</summary>
        public int PrevId;
        /// <summary>Matériaux requis (+0x2c, jusqu'à 3 paires
        /// itemId/qté — la colonne "nom" est sautée par le natif).</summary>
        public List<(int ItemId, int Qty)> Materials { get; } = new();
        /// <summary>Résultats pondérés (+0x3c — clé = poids cumulé,
        /// valeur = id de recette apprise à la maîtrise/analyse).</summary>
        public List<(int CumulWeight, int RecipeId)> Results { get; } = new();
        /// <summary>Poids total (+0x50 natif) — borne du
        /// GetShortRand de sélection.</summary>
        public int TotalWeight;
    }

    /// <summary>Parse le fichier — tolère les lignes incomplètes.</summary>
    public static List<Recipe> Parse(string path)
    {
        var list = new List<Recipe>();
        if (!File.Exists(path)) return list;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("|V")) continue;
            var c = line.Split('|');
            if (c.Length < 6 || !int.TryParse(c[0], out var id)) continue;
            var r = new Recipe
            {
                Id = id,
                Group = Int(c, 1),
                ItemId = Int(c, 2),
                Name = c.Length > 3 ? c[3] : string.Empty,
                Count = Int(c, 4),
                ReqLevel = Int(c, 5),
                ExpBase = Int(c, 6),
                ExpVar = Int(c, 7),
                ExpCap = Int(c, 8),
                Gold = Int(c, 9),
                NextId = Int(c, 10),
                PrevId = Int(c, 11),
            };
            // 12..20 : jusqu'à 3 matériaux (id, nom, qté) — id 0 = vide.
            for (var i = 0; i < 3; i++)
            {
                var b = 12 + i * 3;
                if (b + 2 >= c.Length) break;
                var matId = Int(c, b);
                var qty = Int(c, b + 2);
                if (matId != 0) r.Materials.Add((matId, qty));
            }
            // 21.. : résultats (id, nom, poids) — poids cumulés en clé
            // (map native : borne haute pondérée pour le pick aléatoire).
            var cumul = 0;
            for (var b = 21; b + 2 < c.Length; b += 3)
            {
                var resId = Int(c, b);
                var weight = Int(c, b + 2);
                cumul += weight;
                if (resId != 0) r.Results.Add((cumul, resId));
            }
            r.TotalWeight = cumul;
            list.Add(r);
        }
        return list;
    }

    private static int Int(string[] c, int i) =>
        i < c.Length && int.TryParse(c[i], out var v) ? v : 0;
}
