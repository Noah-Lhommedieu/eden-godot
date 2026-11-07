using System;

namespace Eden.Core.Game.Family;

/// <summary>
/// Règles pures du cycle de vie des bâtiments de production dans les
/// villes de famille (lot E4) — portage des mécanismes natifs :
///
/// <list type="bullet">
/// <item>Usure des stations — <c>ToCombine</c>/<c>ToCollect</c> (zone
/// ~0x8163c00/0x8166fxx) : les recettes <b>non instantanées</b> usent
/// au <b>démarrage</b> de l'ordre (`rand%100 &lt; chance`) ;
/// <c>EndCombine</c>/<c>EndCollect</c>/<c>EndSummon</c> : les recettes
/// <b>instantanées</b> usent à la <b>fin</b> (`rand &lt;= chance`,
/// rand ∈ [0,99] via <c>CRandom::GetShortRand(100)</c>).</item>
/// <item>EXP de bâtiment — <c>SFamily::GainBuildingExpByItemID</c>
/// (zone) → <c>CFamily::UpdateBuildingExp</c> (mission) :
/// `gain = (int)(extBuildingRate × recipeBldExp × 0.01) + recipeBldExp`
/// (<c>DAT_087cc690</c> = 0.01, EndCombine @0x08165510).</item>
/// <item>Level-up — seuil <c>c_level[level+1]</c> cols 12-17 indexé par
/// le type de station (S_Item col9 = itemdef+0x28 ∈ 0x18..0x1d) ;
/// franchissement → `exp -= seuil`, `tid = template suivant`,
/// `level = next.Level`, exige `level &lt; family.Level`.</item>
/// <item><c>CFamily::CheckBuildingLevelUp</c> (mission @0x081b1f40,
/// appelé par <c>SetLevel</c>) : re-évalue les 0x29 dont l'EXP bankée
/// atteint le seuil, et synchronise `tid/level` quand la ligne
/// `level+1` n'existe plus (plafond de table) mais qu'un template
/// suivant existe.</item>
/// <item>Taxe de ville — <c>CMiningShop::OpenShop</c> (zone
/// @0x0857a080) : op+0x18 = <c>family+0x32</c> (BusinessTax) quand le
/// joueur visite la ville d'une AUTRE famille ; coût =
/// `gold + (int)(rate × gold × 0.01 + 0.5)` (CheckToCombine zone
/// @~227940).</item>
/// <item>due_date — <c>CFamily::InsertItemToBuilding</c> (mission
/// @0x081b0d80) : items 0x2a/0x52 avec durée d'item
/// (CItemData+0x70 = <c>DurationSeconds</c>) → `now + durée`, sinon 0 ;
/// les <c>OpenShop</c> des stations refusent un NPC expiré
/// (texte 0xc56).</item>
/// </list>
/// </summary>
public static class FamilyBuildingWorkRules
{
    /// <summary>Type d'item du conteneur 0 pour les bâtiments de
    /// production (clone zone +0x86 / mission +0x88 = 0x29).</summary>
    public const int BuildingItemType = 0x29;

    /// <summary>Type de station minimal (S_Item col9 pour les plans
    /// kind 0x29 : ferme 24=0x18 … mare magique 29=0x1d). Natif :
    /// `switch(itemdef+0x28 - 0x18)` dans UpdateBuildingExp.</summary>
    public const int StationTypeMin = 0x18;
    /// <summary>Type de station maximal (0x1d = mare magique).</summary>
    public const int StationTypeMax = 0x1d;

    /// <summary>Vrai si <paramref name="stationType"/> est un type de
    /// station de production (0x18..0x1d).</summary>
    public static bool IsStationType(int stationType) =>
        stationType is >= StationTypeMin and <= StationTypeMax;

    /// <summary>Index de seuil dans les cols 12-17 de c_level pour un
    /// type de station (ferme→0 … mare→5).</summary>
    public static int StationIndex(int stationType) =>
        stationType - StationTypeMin;

    /// <summary>Dé d'usure natif — <c>CRandom::GetShortRand(100)</c>
    /// ∈ [0,99]. <paramref name="inclusive"/> = la forme de fin d'ordre
    /// (`rand &lt;= chance` dans End* : le natif saute l'usure quand
    /// `chance &lt; rand`) ; sinon la forme de démarrage
    /// (`rand &lt; chance` dans ToCombine/ToCollect).</summary>
    public static bool RollWear(Random rng, int chance, bool inclusive)
    {
        var roll = rng.Next(100);
        return inclusive ? roll <= chance : roll < chance;
    }

    /// <summary>Gain d'EXP de bâtiment à la fin d'un ordre — formule
    /// native <c>EndCombine</c> : <c>(int)((float)(extBuildingRate ×
    /// recipeBldExp) × 0.01f) + recipeBldExp</c>. extBuildingRate est
    /// le bonus du booster (kinds 0x1f44/0x1f48 — absents des données
    /// EDENFR → 0 en pratique).</summary>
    public static int BuildingExpGain(int extBuildingRate,
        int recipeBuildingExp) =>
        (int)(extBuildingRate * recipeBuildingExp * 0.01f)
        + recipeBuildingExp;

    /// <summary>Coût en or d'un ordre avec taxe de ville — natif
    /// <c>CheckToCombine</c> : <c>tax = (int)(rate × gold × 0.01f +
    /// 0.5f)</c> puis <c>total = gold + tax</c>. Forme entière
    /// équivalente pour gold ≥ 0 : <c>gold + (gold×rate + 50)/100</c>.
    /// </summary>
    public static int TaxedGold(int gold, int taxRate) =>
        gold + (gold * taxRate + 50) / 100;

    /// <summary>due_date d'un item de hall à l'insertion — natif
    /// <c>InsertItemToBuilding</c> : conteneur 0 + types 0x2a/0x52 +
    /// durée d'item &gt; 0 → <c>now + durée</c> ; tout le reste
    /// (bâtiments 0x29 compris) → 0 (permanent).</summary>
    public static int DueDateFor(short itemType, int durationSeconds,
        long nowEpoch) =>
        itemType is 0x2a or 0x52 && durationSeconds > 0
            ? (int)(nowEpoch + durationSeconds)
            : 0;

    /// <summary>Vrai si le NPC de hall est expiré — check natif des
    /// <c>*Shop::OpenShop</c> : <c>due_date != 0 &amp;&amp;
    /// due_date &lt; now</c> → refus texte 0xc56.</summary>
    public static bool IsNpcExpired(int dueDate, long nowEpoch) =>
        dueDate != 0 && dueDate < nowEpoch;

    /// <summary>Résultat d'une application d'EXP de bâtiment —
    /// <see cref="NewLevel"/> = niveau effectif après évaluation
    /// (copie du `next.Level` natif), <see cref="Changed"/> = la ligne
    /// doit être persistée/repoussée.</summary>
    public readonly record struct BuildingExpResult(int Exp, int Tid,
        int Level, bool Changed, bool LeveledUp)
    {
        /// <summary>Aucun changement.</summary>
        public static BuildingExpResult None(int exp, int tid, int level)
            => new(exp, tid, level, false, false);
    }

    /// <summary><c>CFamily::UpdateBuildingExp</c> (mission) — ajoute
    /// <paramref name="gain"/> à l'EXP du bâtiment puis, si le seuil de
    /// <c>c_level[level+1]</c> est atteint et <c>level &lt;
    /// familyLevel</c>, soustrait UN seuil, monte au template suivant
    /// (<c>tid = next.Tid</c>, <c>level = next.Level</c>).</summary>
    /// <param name="threshold">Seuil c_level[level+1] pour le type de
    /// station (0 = inerte, natif sort sans rien écrire).</param>
    /// <param name="nextTid">Tid du template suivant de la chaîne
    /// (building+0x30 natif), <paramref name="nextLevel"/> son col4 ;
    /// nextTid == 0 = pas de template suivant → pas de level-up.</param>
    public static BuildingExpResult ApplyBuildingExp(int exp, int tid,
        int level, int familyLevel, int threshold, int gain,
        int nextTid, int nextLevel)
    {
        // Natif : seuil == 0 → la fonction retourne sans rien écrire
        // (le `uVar13 != 0` du guard UpdateBuildingExp).
        if (threshold <= 0) return BuildingExpResult.None(exp, tid, level);
        exp += gain;
        if (exp >= threshold && level < familyLevel && nextTid != 0)
            return new BuildingExpResult(exp - threshold, nextTid,
                nextLevel, true, true);
        return new BuildingExpResult(exp, tid, level, gain != 0, false);
    }

    /// <summary><c>CFamily::CheckBuildingLevelUp</c> (mission
    /// @0x081b1f40) — re-évaluation d'un bâtiment 0x29 après une hausse
    /// de niveau de la famille : si <c>c_level[level+1]</c> existe et
    /// seuil ≤ exp → rejoue UpdateBuildingExp(gain 0) ; si la ligne
    /// n'existe plus (level+1 hors table) et qu'un template suivant
    /// existe avec <c>tid != nextTid</c> → synchronise
    /// <c>tid/level</c> sur le template suivant.</summary>
    /// <param name="thresholdRowExists">Vrai si la ligne
    /// c_level[level+1] existe (le `else` natif synchronise alors
    /// tid/level quand la ligne manque).</param>
    public static BuildingExpResult EvaluateLevelUp(int exp, int tid,
        int level, int familyLevel, bool thresholdRowExists,
        int threshold, int nextTid, int nextLevel)
    {
        if (thresholdRowExists)
        {
            if (threshold > 0 && threshold <= exp)
                return ApplyBuildingExp(exp, tid, level, familyLevel,
                    threshold, 0, nextTid, nextLevel);
            return BuildingExpResult.None(exp, tid, level);
        }
        // Ligne level+1 absente (plafond de table) — le natif aligne
        // le bâtiment sur son template suivant si le tid traîne.
        if (nextTid != 0 && tid != nextTid)
            return new BuildingExpResult(exp, nextTid, nextLevel,
                true, false);
        return BuildingExpResult.None(exp, tid, level);
    }
}
