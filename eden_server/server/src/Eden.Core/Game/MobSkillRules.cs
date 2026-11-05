using Eden.Core.Data;
using Eden.Core.Data.Parsers;

namespace Eden.Core.Game;

/// <summary>Choix des sorts d'un mob — port de <c>CAI::DetermineAttackMode</c>
/// (zone @0x084d8a60) et du chargement des vecteurs de sorts
/// (<c>GetAllMonsterTemplate</c> @0x08367910, <c>CNode::BornMonster</c>
/// @0x084b02b0). Deux familles (définitions propriétaire, definitions.md) :
/// <list type="bullet">
/// <item><b>Normal skills</b> : <c>S_Monster</c> cols 44-46 (vecteur
/// template +0x108, 3 sorts), tirés au hasard avec la chance de
/// <c>S_Ai</c> col 6/8/10 (octets +9/+0xb/+0xd).</item>
/// <item><b>Countdown skills</b> : <c>S_Ai</c> cols 15/17/19 (shorts
/// +0x1a/+0x1e/+0x22 → vecteur +0x114), lancés dès que possible quand
/// PV% ≤ seuil <c>S_Ai</c> col 5/7/9 (octets +8/+0xa/+0xc) ; le sort du
/// palier relance après le délai col 14/16/18 (shorts +0x18/+0x1c/+0x20,
/// secondes). Palier 1 : PV% ≤ col5 et &gt; col7 ; palier 2 : ≤ col7 et
/// &gt; col9 ; palier 3 : ≤ col9. Au-dessus de col5 : aucun sort.</item>
/// </list>
/// Un palier k autorise les sorts normaux d'indice ≤ k−1, essayés du plus
/// haut au plus bas (indice 2 → chance col 10, 1 → col 8, 0 → col 6).</summary>
public static class MobSkillRules
{
    /// <summary>Colonnes S_Monster des 3 sorts normaux.</summary>
    public const int NormalFirstCol = 44;

    public readonly record struct Pick(int SpellId, bool Countdown, int Slot);

    /// <summary>Sorts normaux (cols 44-46 de S_Monster), 0 = vide.</summary>
    public static int[] NormalSpells(MonsterParser.MonsterDef def)
    {
        var r = new int[3];
        for (var i = 0; i < 3; i++)
            r[i] = Math.Max(0, IniTable.GetInt(def.Raw, NormalFirstCol + i));
        return r;
    }

    private static int V(ProgressionParsers.AiProfile p, int col) =>
        col < p.Values.Length ? p.Values[col] : 0;

    /// <summary>Sorts à seuil du profil (cols 15/17/19), 0 = vide.</summary>
    public static int[] CountdownSpells(ProgressionParsers.AiProfile? p) =>
        p == null ? new int[3]
            : new[] { V(p, 15), V(p, 17), V(p, 19) };

    /// <summary>Seuils PV% des 3 paliers (cols 5/7/9).</summary>
    public static int[] CountdownThresholds(ProgressionParsers.AiProfile? p) =>
        p == null ? new int[3] : new[] { V(p, 5), V(p, 7), V(p, 9) };

    /// <summary>Délais de relance en secondes (cols 14/16/18).</summary>
    public static int[] CountdownCooldowns(ProgressionParsers.AiProfile? p) =>
        p == null ? new int[3] : new[] { V(p, 14), V(p, 16), V(p, 18) };

    /// <summary>Chances % des sorts normaux (cols 6/8/10).</summary>
    public static int[] NormalChances(ProgressionParsers.AiProfile? p) =>
        p == null ? new int[3] : new[] { V(p, 6), V(p, 8), V(p, 10) };

    /// <summary>Le mob suit le modèle natif à deux familles : au moins un
    /// sort normal (cols 44-46) ou à seuil (S_Ai). Sinon le serveur garde
    /// l'ancien tirage sur les références S_Monster 40-49.</summary>
    public static bool UsesNativeSkills(MonsterParser.MonsterDef def,
        ProgressionParsers.AiProfile? p) =>
        NormalSpells(def).Any(s => s > 0) ||
        CountdownSpells(p).Any(s => s > 0);

    /// <summary>Palier courant 0..3 (0 = PV% au-dessus de col5 : aucun
    /// sort). <paramref name="hpPct"/> en pourcentage 0..100.</summary>
    public static int Stage(ProgressionParsers.AiProfile? p, float hpPct)
    {
        if (p == null) return 0;
        var t = CountdownThresholds(p);
        if (hpPct > t[0]) return 0;
        if (hpPct > t[1]) return 1;
        if (hpPct > t[2]) return 2;
        return 3;
    }

    /// <summary>Choisit le sort du tick. <paramref name="lastCountdown"/> =
    /// dernier lancer de chaque palier (UTC, défaut = jamais). Retourne
    /// null si rien à lancer ce tick.</summary>
    public static Pick? Choose(MonsterParser.MonsterDef def,
        ProgressionParsers.AiProfile? profile, float hpPct, DateTime now,
        DateTime[] lastCountdown, Random rng)
    {
        var stage = Stage(profile, hpPct);
        if (stage == 0) return null;
        var cd = CountdownSpells(profile);
        var cool = CountdownCooldowns(profile);
        var slot = stage - 1;
        if (cd[slot] > 0 &&
            (lastCountdown[slot] == default ||
             now - lastCountdown[slot] >= TimeSpan.FromSeconds(cool[slot])))
            return new Pick(cd[slot], true, slot);
        var normal = NormalSpells(def);
        var chance = NormalChances(profile);
        for (var i = stage - 1; i >= 0; i--)
            if (normal[i] > 0 && rng.Next(100) < chance[i])
                return new Pick(normal[i], false, i);
        return null;
    }
}
