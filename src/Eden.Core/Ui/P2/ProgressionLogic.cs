using System.Text;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Ui.P2;

/// <summary>Route du destin (plateau 39 cases) — textes et activation
/// des boutons. Les opérations réseau suivent <c>ZoneDestinyDice</c> :
/// 0 créer, 1 lancer, 3 fonction/choix, 4 avancer.</summary>
public static class DestinyView
{
    public const int Create = 0, Roll = 1, Pick = 3, Go = 4;

    public static readonly string[] FunctionNames =
        { "±1", "×2", "+3", "−4", "=5", "1-6" };

    public static string Vit(int v) => v < 0 ? "Vit ?" : $"Vit {v}";
    public static string Dp(DestinyState.Route? r) => $"PD {r?.DpPool ?? 0}";
    public static string Dice(DestinyState.Route? r) => $"Dés {r?.RollsLeft ?? 0}";
    public static string Move(DestinyState.Route? r) =>
        r is { MovePoints: > 0 } ? $"Pas {r.MovePoints}" : "Pas -";

    public static bool CanRoll(DestinyState.Route? r) =>
        r != null && r.RollsLeft > 0 && r.MovePoints == 0;

    public static bool CanGo(DestinyState.Route? r) =>
        r != null && r.MovePoints > 0;

    public static string Fortune(IReadOnlyDictionary<int, int> bags) =>
        bags.Count == 0 ? "Sacs de fortune : —"
            : "Sacs : " + string.Join(" ", bags.Select(kv => $"{kv.Key}×{kv.Value}"));

    public static string EventLine(string text) => "➤ " + text;

    public static string FailedText(short op) => op switch
    {
        0 => "Création de la route impossible (100 vitalité requis).",
        1 => "Lancer impossible.",
        4 => "Déplacement impossible.",
        _ => $"Opération destin {op} refusée.",
    };

    /// <summary>Texte d'infobulle d'une case (événement).</summary>
    public static string CellTooltip(DestinyTable.EventDef def, string name) =>
        $"{name}  ({DestinyTable.TypeName(def.Type)}" +
        (def.Type == 7 ? $" +{def.Value} dés" : def.Value != 0 ? $" {def.Value}" : "") + ")";

    /// <summary>Case courante (index 0..38) : <c>Step</c> borné.</summary>
    public static int CurrentCell(DestinyState.Route? r) =>
        r == null ? -1 : Math.Clamp(r.Step, 0, 38);
}

/// <summary>Succès : arbre de groupes, filtres, progression, récompense.</summary>
public sealed class AchievementView
{
    /// <summary>-1 tous, -2 centaine (voir <see cref="Hundred"/>), sinon groupe.</summary>
    public int FilterGroup { get; set; } = -1;
    public int Hundred { get; set; }

    public static string HundredName(int h) => h <= 2 ? "Conquête de monstres"
        : h == 5 ? "Succès de ville" : $"Catégorie {h}xx";

    public sealed record Node(string Label, int Group, int Hundred);

    /// <summary>Arbre : « Tous » puis une entrée par centaine suivie de ses groupes.</summary>
    public static List<Node> BuildTree(IEnumerable<AchievementTable.AchievementDef> defs,
        Func<int, string> groupName)
    {
        var list = defs.ToList();
        var tree = new List<Node> { new($"Tous ({list.Count})", -1, 0) };
        var byH = list.GroupBy(d => d.GroupKey / 100).OrderBy(g => g.Key);
        foreach (var h in byH)
        {
            tree.Add(new Node($"{HundredName(h.Key)} ({h.Count()})", -2, h.Key));
            foreach (var g in h.GroupBy(d => d.GroupKey).OrderBy(g => g.Key))
                tree.Add(new Node($"   {groupName(g.Key)} ({g.Count()})", g.Key, h.Key));
        }
        return tree;
    }

    public bool Matches(AchievementTable.AchievementDef d) => FilterGroup switch
    {
        -1 => true,
        -2 => d.GroupKey / 100 == Hundred,
        _ => d.GroupKey == FilterGroup,
    };

    /// <summary>Lignes filtrées : accomplis d'abord, puis groupe/tri/id.</summary>
    public List<AchievementTable.AchievementDef> Rows(
        IEnumerable<AchievementTable.AchievementDef> defs,
        Func<int, bool> completed) =>
        defs.Where(Matches)
            .OrderBy(d => completed(d.Id) ? 0 : 1)
            .ThenBy(d => d.GroupKey).ThenBy(d => d.SortIndex).ThenBy(d => d.Id)
            .ToList();

    public static (int Done, int Total) Progress(
        AchievementTable.AchievementDef d, Func<int, bool> completed,
        Func<int, uint> counter)
    {
        var done = completed(d.Id);
        if (d.TargetIds.Length > 0)
            return (done ? d.TargetIds.Length
                : AchievementProgress.CountDone(d.TargetIds, completed),
                d.TargetIds.Length);
        if (done) return (d.NeedNum, d.NeedNum);
        return ((int)Math.Min(counter(d.Id), (uint)d.NeedNum), d.NeedNum);
    }

    public static string Totals(long total, uint hunt, uint town) =>
        $"Total : {total} pts\nChasse : {hunt} pts\nVille : {town} pts";

    public static string Header(int rows, int complete) =>
        $"{rows} succès — {complete} accomplis";

    public static string Reward(AchievementTable.AchievementDef d,
        Func<int, string?> titleName, Func<int, string> itemName)
    {
        var parts = new List<string>();
        if (d.TitleCol9 != 0 && titleName(d.TitleCol9) is { } t)
            parts.Add("Titre " + t);
        if (d.ItemCol10 != 0)
            parts.Add("Objet " + itemName(d.ItemCol10));
        return parts.Count == 0 ? "" : "→ " + string.Join(", ", parts);
    }

    public static string RowText(string name, int done, int total, int points,
        bool completed) =>
        $"{(completed ? "✓ " : "  ")}{name}  {done}/{total}  {points} pts";
}

/// <summary>Couple (Lover) — libellés.</summary>
public static class CoupleView
{
    public static string Partner(LoverState s) =>
        s.PartnerCharId != 0
            ? "Partenaire : " + (s.PartnerName.Length > 0 ? s.PartnerName
                : $"n°{s.PartnerCharId}")
            : "Partenaire : —";

    public static string Status(LoverState s) =>
        s.PartnerCharId == 0 ? "Célibataire" : s.Married ? "Marié(e) ♥" : "En couple ♥";

    public static string Invite(string peer) =>
        $"{peer} vous propose de former un couple.\nAccepter ?";

    public static string Weddings(IReadOnlyList<ZoneWeddingsInfo.Entry> es) =>
        es.Count == 0 ? "Aucune cérémonie prévue."
            : string.Join("\n", es.Select((e, i) => $"Cérémonie {i + 1}"));
}

/// <summary>Communauté : amis / liste noire.</summary>
public static class SocialView
{
    public static string Header(string guild, int level) =>
        guild.Length > 0
            ? $"Liste d'amis — <{guild}>" + (level > 0 ? $"  Niv {level}" : "")
            : "Liste d'amis";

    public static string FriendType(short t) => t switch
    {
        1 => "ajouté", 2 => "vous a ajouté", 3 => "mutuel", _ => "",
    };

    /// <summary>Ordre : en ligne d'abord, puis nom.</summary>
    public static List<FriendEntry> Sorted(IEnumerable<FriendEntry> fs) =>
        fs.OrderByDescending(f => f.Online != 0)
          .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static string FriendRow(FriendEntry f, Func<int, string?> className) =>
        $"{(f.Online != 0 ? "●" : "○")} {f.Name}  Niv {f.Level}" +
        (className(f.ClassId) is { } c ? $"  {c}" : "") +
        (f.FamilyName.Length > 0 ? $"  <{f.FamilyName}>" : "");

    public static string InviteText(string name) =>
        $"{name} souhaite vous ajouter à sa liste d'amis.";
}

/// <summary>Statistiques avancées (formules serveur 32002).</summary>
public static class StatsView
{
    public static readonly (string Key, string Label, string Section)[] Docs =
    {
        ("P-ATK", "ATQ physique", "Offensive"),
        ("M-ATK", "ATQ magique", "Offensive"),
        ("Weapon Variance", "Variance d'arme", "Offensive"),
        ("Hit Rate", "Précision", "Précision et esquive"),
        ("Dodge Rate", "Esquive", "Précision et esquive"),
        ("Critical Rate", "Taux critique", "Coups critiques"),
        ("Magic Critical Rate", "Taux critique magique", "Coups critiques"),
        ("Critical Damage", "Dégâts critiques", "Coups critiques"),
        ("Magic Critical Damage", "Dégâts critiques magiques", "Coups critiques"),
        ("Critical Damage Reduction", "Réduction des dégâts critiques", "Défense"),
        ("Parry Rate", "Parade", "Défense"),
        ("Block Rate", "Blocage", "Défense"),
        ("Damage Reduction", "Réduction de dégâts", "Défense"),
        ("Attack Speed", "Vitesse d'attaque", "Vitesse"),
        ("Cast Speed", "Vitesse d'incantation", "Vitesse"),
        ("Move Speed", "Vitesse de déplacement", "Vitesse"),
    };

    public static string Label(string key) =>
        Docs.FirstOrDefault(d => d.Key == key) is { Key: not null } d
            && d.Label != null ? d.Label : key;

    public static string Value(float v, string unit) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + unit;

    public static string Origin(byte origin, float gate) => origin switch
    {
        0 => "Équipement",
        2 => $"Proc {gate:0.##}%",
        _ => "Talent / Certificat / Buff",
    };

    /// <summary>Texte complet : formules par section puis multi-hit.</summary>
    public static string Render(ZoneAdvStatsInfo m, Func<int, string> enchantName)
    {
        var sb = new StringBuilder();
        var byKey = m.Formulas.ToDictionary(f => f.Key);
        string? last = null;
        foreach (var d in Docs)
        {
            if (!byKey.TryGetValue(d.Key, out var f)) continue;
            if (d.Section != last)
            {
                last = d.Section;
                sb.AppendLine().AppendLine($"[{d.Section}]");
            }
            sb.AppendLine($"{d.Label} = {Value(f.Value, f.Unit)}");
            sb.AppendLine($"   {f.Expr}");
        }
        foreach (var f in m.Formulas.Where(f => Docs.All(d => d.Key != f.Key)))
            sb.AppendLine($"{f.Key} = {Value(f.Value, f.Unit)}   {f.Expr}");
        sb.AppendLine().AppendLine("[Coups multiples]");
        if (m.Sources.Count == 0 || m.MaxHits == 0)
        {
            sb.AppendLine("Aucune source de coups multiples");
            return sb.ToString();
        }
        double expect = 0;
        foreach (var s in m.Sources)
        {
            sb.AppendLine($"{enchantName(s.EnchantId)}" +
                (s.Stacks > 1 ? $" x{s.Stacks}" : "") +
                $"  [{Origin(s.Origin, s.Gate)}]");
            sb.AppendLine($"   Double coup : {s.C1:0.##}% -> {s.F1:0.##}% dégâts");
            if (m.MaxHits >= 2 && s.C2 > 0)
                sb.AppendLine($"   Triple coup : {s.C2:0.##}% -> {s.F2:0.##}% dégâts");
            var gate = Math.Min(s.Gate, 100f) / 100.0;
            var p1 = Math.Min(s.C1, 100f) / 100.0 * gate;
            var p2 = m.MaxHits >= 2 ? p1 * Math.Min(s.C2, 100f) / 100.0 : 0;
            expect += p1 * s.F1 + p2 * s.F2;
        }
        sb.AppendLine($"Dégâts supplémentaires attendus par coup : +{expect:0.#}%  " +
            $"(x{m.ChanceMult:0.##} / x{m.DamageMult:0.##})");
        return sb.ToString();
    }
}

/// <summary>Expertise de classe (bonus de rang, 5 stats).</summary>
public static class ExpertiseView
{
    public static readonly string[] Labels = { "PV", "PM", "ATQ", "ATQ M.", "Soin" };

    public static string StatsText(int[] b, bool onlyNonZero = true)
    {
        var parts = new List<string>();
        for (var i = 0; i < Labels.Length && i < b.Length; i++)
            if (!onlyNonZero || b[i] != 0)
                parts.Add($"{Labels[i]} +{b[i]}");
        return string.Join("  ", parts);
    }

    public static int[] Delta(int[] cur, int[] prev)
    {
        var d = new int[cur.Length];
        for (var i = 0; i < d.Length; i++)
            d[i] = cur[i] - (i < prev.Length ? prev[i] : 0);
        return d;
    }

    public static string Title(string className, int rank) =>
        rank > 0 ? $"{className} — Bonus de compétence {rank}"
                 : $"{className} — Aucun bonus";

    public static string RowSub(int classLevel, int[] bonus)
    {
        var s = StatsText(bonus);
        return $"Niveau de classe {classLevel}" + (s.Length > 0 ? "\n" + s : "");
    }

    /// <summary>Détail des 18 paliers (niv 5..90) d'une classe.</summary>
    public static List<string> Ranks(int classId, int currentLevel, string className)
    {
        var rows = new List<string>();
        var prev = new int[Labels.Length];
        for (var rank = 1; rank <= 18; rank++)
        {
            var lv = rank * 5;
            var b = ClassRankBuffTable.Bonus(classId, lv);
            var gain = StatsText(Delta(b, prev));
            prev = b;
            rows.Add($"{(currentLevel >= lv ? "✓" : "·")} {className} — palier {rank} " +
                $"(niv {lv}) : {StatsText(b)}" +
                (gain.Length > 0 ? $"  [gain {gain}]" : ""));
        }
        return rows;
    }
}
