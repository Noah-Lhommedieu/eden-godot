using System.Globalization;
using EdenClient.Protocol;

namespace EdenClient.Windows;

/// <summary>Ton d'une cellule de stat : « Up » = au-dessus de la base
/// (vert), « Atk » = rouge (P-ATK/M-ATK), « Def » = vert (DEF).</summary>
public enum StatTone { Normal, Up, Atk, Def }

/// <summary>Cellule calculée : texte + ton.</summary>
public readonly record struct StatCell(string Text, StatTone Tone = StatTone.Normal);

/// <summary>
/// Calcul pur de la fenêtre « Informations du personnage » (port
/// <c>ProfileWindow.RefreshStats</c>) : à partir du 296 (stats
/// totales), du 492 (stats de base) et du 493 (paliers de niveau),
/// produit les cellules par clé (« str », « acc », « acc% »…).
/// Calibré sur ref-shots/profile.png : ACC 122 / 4.29 %, CRIT 124.7 /
/// 5.63 %, EVA 174 / 8.07 %, Parry 135 / 2.4 %, Block 151 / 0.98 %.
/// </summary>
public static class ProfileView
{
    /// <summary>Index 492 (Str, Int, Agi, Wis, Lck) par clé.</summary>
    public static readonly (string Key, int Idx)[] CoreIndex =
        { ("str", 0), ("int", 1), ("agi", 2), ("wis", 3), ("lck", 4) };

    /// <summary>Noms de résistances — ordre d'affichage natif (même
    /// ordre que <c>PlayerStats.Resists</c>).</summary>
    public static readonly string[] ResistKeys =
    {
        "res_slash", "res_strike", "res_pierce", "res_fire",
        "res_lightning", "res_ice", "res_nature", "res_holy", "res_dark",
    };

    /// <summary>Noms de races natives (1 Human … 5 Halfkin).</summary>
    public static readonly string[] RaceNames =
        { "?", "Human", "Zumi", "Anuran", "Ursun", "Halfkin" };

    /// <summary>Format compact « 0.## » invariant.</summary>
    public static string F(float v) =>
        v.ToString("0.##", CultureInfo.InvariantCulture);

    private static int Rate(PlayerStats s, int i) =>
        i < s.Rates.Length ? s.Rates[i] : 0;

    /// <summary>Calcule toutes les cellules ; <c>null</c> stats → « - ».</summary>
    public static Dictionary<string, StatCell> Compute(PlayerStats? s,
        int[]? baseStats, float[]? lv)
    {
        var d = new Dictionary<string, StatCell>();
        string[] keys =
        {
            "str", "agi", "int", "wis", "lck", "acc", "move", "patk",
            "aspd", "crit", "critdmg", "matk", "cspd", "mcrit",
            "mcritdmg", "def", "eva", "parry", "block", "pheal", "gheal",
        };
        if (s == null)
        {
            foreach (var k in keys) d[k] = new StatCell("-");
            return d;
        }
        StatTone Up(int cur, int idx) =>
            baseStats != null && idx < baseStats.Length &&
            cur > baseStats[idx] ? StatTone.Up : StatTone.Normal;
        d["str"] = new($"{s.Str}", Up(s.Str, 0));
        d["agi"] = new($"{s.Agi}", Up(s.Agi, 2));
        d["int"] = new($"{s.Int}", Up(s.Int, 1));
        d["wis"] = new($"{s.Wis}", Up(s.Wis, 3));
        d["lck"] = new($"{s.Lck}", Up(s.Lck, 4));
        string L(int i, int raw) =>
            lv != null && i < lv.Length ? F(lv[i]) : $"{raw}";
        d["acc"] = new(L(0, Rate(s, 2)));
        d["move"] = new(s.Speeds.Length > 4 ? F(s.Speeds[4]) : "-");
        d["patk"] = new($"{s.PhysicalAttack}", StatTone.Atk);
        d["aspd"] = new(lv != null && lv.Length > 12 ? F(lv[12])
            : F(s.AttackSpeedRate));
        d["crit"] = new(L(1, Rate(s, 4)));
        d["critdmg"] = new(F(Rate(s, 5) / 100f));
        d["matk"] = new($"{s.MagicAttack}", StatTone.Atk);
        d["cspd"] = new(lv != null && lv.Length > 13 ? F(lv[13])
            : F(s.MagicSpeedRate));
        d["mcrit"] = new(L(2, Rate(s, 6)));
        d["mcritdmg"] = new(F(Rate(s, 7) / 100f));
        d["def"] = new($"{s.PhysicalDefence}", StatTone.Def);
        d["eva"] = new(L(3, Rate(s, 3)));
        d["parry"] = new(L(4, Rate(s, 0)));
        d["block"] = new(L(5, Rate(s, 1)));
        d["pheal"] = new("-");
        d["gheal"] = new("-");
        // Colonne % : taux 296 (s16 ×100 : 429 → 4.29 %).
        void Pct(string k, float v) => d[k + "%"] = new($"{F(v)}%");
        Pct("acc", Rate(s, 2) / 100f);
        Pct("crit", Rate(s, 4) / 100f);
        Pct("critdmg", Rate(s, 5));
        Pct("mcrit", Rate(s, 6) / 100f);
        Pct("mcritdmg", Rate(s, 7));
        Pct("eva", Rate(s, 3) / 100f);
        Pct("parry", Rate(s, 0) / 100f);
        Pct("block", Rate(s, 1) / 100f);
        if (s.Speeds.Length > 4) Pct("move", s.Speeds[4]);
        return d;
    }

    /// <summary>Résistances « 0 » si absentes (9 valeurs).</summary>
    public static string[] Resists(PlayerStats? s)
    {
        var r = new string[9];
        for (var i = 0; i < 9; i++)
            r[i] = s != null && i < s.Resists.Length
                ? s.Resists[i].ToString() : "0";
        return r;
    }

    /// <summary>« Nom — LV n » de l'en-tête.</summary>
    public static string Race(int raceId) =>
        raceId > 0 && raceId < RaceNames.Length ? RaceNames[raceId] : "?";

    /// <summary>« total = naturel + bonus » (survol des 5 stats).</summary>
    public static string SplitHint(int total, int? natural) =>
        natural is { } n ? $"{total} = {n} + {total - n}" : $"{total}";
}
