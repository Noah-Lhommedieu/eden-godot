using System.Text.RegularExpressions;

namespace EdenClient.Data;

/// <summary>
/// A5.6 : classement des succès légendaires d'après les tables du wiki
/// (<c>knowledge/wiki-tables/M/Legendary_Achievements.md</c>, données
/// embarquées dans <c>LegendaryWikiData.cs</c>) au lieu d'une regex sur
/// le texte EN.
///
/// Le wiki liste chaque palier (Lv1-30/50/65/80/90) comme un succès à part
/// avec des noms différents de ceux des tables du jeu ; la jointure se
/// fait donc par (famille d'objectif, niveau) : le texte EN de
/// <c>T_Achievement</c> (« Achieve LV1~65 orange grade dagger crafting… »)
/// est réduit à un nom wiki (« Daggers ») par <see cref="Synonyms"/>,
/// puis la ligne wiki <c>(Daggers, 65)</c> donne la colonne (stats /
/// armes / montures &amp; costumes), l'objectif canonique et la
/// récompense. Périmètre : Eden de base = lvl ≤ 90, tout ce qui vise
/// plus haut est hors périmètre.
/// </summary>
public static partial class LegendaryWiki
{
    /// <summary>Une ligne du wiki. Kind 1 stats, 2 armes, 3 montures et
    /// costumes ; <c>Objectives</c> = lignes « Required » (plusieurs pour
    /// les montures) ; <c>Total</c> = bonus cumulé de la famille (renseigné
    /// sur le 1er palier seulement).</summary>
    public sealed record Row(int Kind, string Name, string Noun,
        int MaxLevel, string Objectives, string Reward, string Total);

    /// <summary>Niveau max du périmètre (Eden de base).</summary>
    public const int ScopeMaxLevel = 90;

    /// <summary>Résultat du classement d'un succès du jeu.</summary>
    public sealed record Result(int Kind, int Level, string Noun,
        Row? Wiki)
    {
        public bool OutOfScope => LegendaryWiki.OutOfScope(Level);
    }

    public static IReadOnlyList<Row> All => Rows;

    public static bool OutOfScope(int level) => level > ScopeMaxLevel;

    /// <summary>Texte EN de l'objectif → nom de famille wiki. Ordre =
    /// priorité (le cuir et le tissu avant « armor » générique).</summary>
    private static readonly (Regex Rx, string Noun)[] Synonyms =
    {
        (R(@"rare monster"), "Rare Enemies"),
        (R(@"\bloot\b"), "Trophies"),
        (R(@"summoning crop"), "Summoning Resources"),
        (R(@"mining crop"), "Mining Resources"),
        (R(@"planting crop|farming"), "Farming Resources"),
        (R(@"\bgems?\b"), "Gems"),
        (R(@"\bnecklace"), "Necklaces"),
        (R(@"\brings?\b"), "Rings"),
        (R(@"scroll|\brunes?\b"), "Runes"),
        (R(@"\bcapes?\b"), "Capes"),
        (R(@"leather|light armo"), "Light Armour"),
        (R(@"cloth"), "Cloth Armour"),
        (R(@"heavy armo|\barmou?r\b"), "Heavy Armour"),
        (R(@"dagger"), "Daggers"),
        (R(@"rapier"), "Rapiers"),
        (R(@"taishou|tachi|katana"), "Katanas"),
        (R(@"\bsword"), "Swords"),
        (R(@"\bmace"), "Maces"),
        (R(@"glove|cestus|fist"), "Cestus"),
        (R(@"\bclub"), "Clubs"),
        (R(@"shield"), "Shields"),
        (R(@"\baxe"), "Axes"),
        (R(@"hammer"), "Hammers"),
        (R(@"\bbow\b"), "Bows"),
        (R(@"artillery|\bgun"), "Artillery Guns"),
        (R(@"guitar"), "Guitars"),
        (R(@"\bbook|grimoire"), "Grimoires"),
        (R(@"\bstaff|\bstaves"), "Staffs"),
        (R(@"spear|sickle|pike"), "Pikes"),
    };

    private static Regex R(string p) =>
        new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LvRange = new(
        @"LV\s*(\d+)\s*[~\-]\s*(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex LvSingle = new(
        @"LV\s*(\d+)", RegexOptions.IgnoreCase);

    /// <summary>Forme comparable d'un nom de famille (« Staff's » /
    /// « Staffs » / « Heavy Armours » ↔ « Heavy Armour »).</summary>
    public static string NounKey(string noun)
    {
        var k = new string(noun.ToLowerInvariant()
            .Where(char.IsLetter).ToArray());
        if (k.EndsWith("ies")) return k[..^3] + "y";
        return k.EndsWith('s') ? k[..^1] : k;
    }

    /// <summary>Lignes d'objectif d'un texte T_Achievement EN (avant le
    /// bloc « $12$ » de lore).</summary>
    public static string[] ObjectiveLines(string enText)
    {
        var head = enText.Split('$')[0];
        var cut = head.IndexOf("\n\n", StringComparison.Ordinal);
        if (cut >= 0) head = head[..cut];
        return head.Split('\n', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
    }

    /// <summary>Niveau visé (max sur les lignes) : « LV1~65 » → 65,
    /// « LV90 » → 90 ; 0 si absent.</summary>
    public static int LevelOf(IEnumerable<string> lines)
    {
        var lv = 0;
        foreach (var l in lines)
        {
            var m = LvRange.Match(l);
            if (m.Success) lv = Math.Max(lv, int.Parse(m.Groups[2].Value));
            else if ((m = LvSingle.Match(l)).Success)
                lv = Math.Max(lv, int.Parse(m.Groups[1].Value));
        }
        return lv;
    }

    /// <summary>Classe un succès légendaire du jeu. <paramref name="enText"/>
    /// = description EN complète ; <paramref name="hasItemReward"/> =
    /// récompense item (colonne 10 de c_achievement : monture/costume).
    /// </summary>
    public static Result Classify(string enText, bool hasItemReward)
    {
        var lines = ObjectiveLines(enText);
        var lv = LevelOf(lines);
        // Montures & costumes : récompense item ou objectifs multiples
        // (« Defeat Everyone » = rares + trophées…).
        if (hasItemReward || lines.Length >= 2)
            return new Result(3, lv, "", null);
        var noun = "";
        foreach (var (rx, n) in Synonyms)
            if (rx.IsMatch(lines.Length > 0 ? lines[0] : ""))
            { noun = n; break; }
        if (noun.Length == 0) return new Result(2, lv, "", null);
        var key = NounKey(noun);
        Row? row = null, any = null;
        foreach (var r in Rows)
        {
            if (r.Kind == 3 || NounKey(r.Noun) != key) continue;
            any ??= r;
            if (r.MaxLevel == lv) { row = r; break; }
        }
        return new Result(any?.Kind ?? 2, lv, noun, row);
    }
}

/// <summary>Progression d'un succès « groupe » : chaque cible (col13 de
/// c_achievement) est elle-même un succès à un seul déclenchement
/// (NeedNum=1 pour les 3 577 cibles) ; le groupe est complet quand
/// toutes ses cibles ont une ligne côté serveur — donc la progression
/// se déduit du 480 kind 0 (succès complétés) sans compteur
/// supplémentaire.</summary>
public static class AchievementProgress
{
    public static int[] ParseTargets(string col13) =>
        col13.Split(';', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0).ToArray();

    public static int CountDone(IReadOnlyList<int> targets,
        Func<int, bool> isDone)
    {
        var n = 0;
        foreach (var t in targets) if (isDone(t)) n++;
        return n;
    }

    /// <summary>« 12.50 » (pourcentage 2 décimales, 0 si vide).</summary>
    public static string Percent(int done, int total) =>
        (total <= 0 ? 0.0 : 100.0 * done / total).ToString("0.00",
            System.Globalization.CultureInfo.InvariantCulture);
}
