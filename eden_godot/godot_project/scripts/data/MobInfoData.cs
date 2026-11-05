using System.Globalization;
using EdenClient.Data.Tables;

namespace EdenClient.Data;

/// <summary>
/// Fiche d'un monstre pour le panneau « Inspecter » (3 onglets Stats / Drops /
/// Skills), construite uniquement depuis les tables natives du client :
/// <c>c_monster</c> (stats, sorts normaux), <c>c_ai</c> (sorts à seuil),
/// <c>c_dropitem</c> (pools Normal / Break Point), <c>c_classdropitem</c>
/// (butin de classe). Code pur (compilé aussi par NetTests).
///
/// Colonnes (preuves : <c>CMonsterQuery::Deserialize</c> @0x0860d790,
/// <c>GetAllMonsterTemplate</c> @0x08367910, <c>CAI::DetermineAttackMode</c>
/// @0x084d8a60, <c>CAiQuery::Deserialize</c> @0x085b5c30) :
/// c_monster 14 attaque, 18 déf phys, 19 précision, 20 esquive, 21/22 crit +
/// dégâts crit phys, 23/24 idem magique, 25-33 résistances, 44-46 sorts
/// normaux ; c_ai 5/7/9 seuils PV %, 6/8/10 chances des sorts normaux,
/// 14/16/18 délais, 15/17/19 sorts à seuil.
/// </summary>
public static class MobInfoData
{
    /// <summary>Une ligne de statistique (libellé FR, valeur texte).</summary>
    public sealed record StatLine(string Label, string Value);

    /// <summary>Un item de drop.</summary>
    public sealed record DropLine(int ItemId, int Quantity, double ChancePct);

    /// <summary>Un sort du mob. <see cref="Detail"/> : « Chance : 25 % »
    /// (normal — probabilité de cast c_ai cols 6/8/10) ou
    /// « PV ≤ 70 % · relance 12 s » (à seuil).</summary>
    public sealed record SkillLine(int SpellId, string Detail);

    /// <summary>Fiche complète.</summary>
    public sealed class Sheet
    {
        public int MobId { get; init; }
        public string Name { get; init; } = "";
        public int Level { get; init; }
        public List<StatLine> Stats { get; } = new();
        public List<DropLine> NormalDrops { get; } = new();
        public List<DropLine> BreakDrops { get; } = new();
        public List<DropLine> ClassDrops { get; } = new();
        public List<SkillLine> NormalSkills { get; } = new();
        public List<SkillLine> CountdownSkills { get; } = new();
    }

    // --- libellés FR (definitions.md) ---------------------------------
    public const string TabStats = "Stats", TabDrops = "Drops", TabSkills = "Skills";
    public const string CatNormal = "Normal";
    public const string CatBreak = "Point de rupture";
    public const string CatClass = "Butin de classe";
    public const string CatNormalSkills = "Compétences normales";
    public const string CatCountdownSkills = "Compétences à seuil";

    private static readonly string[] Elements =
        { "Entaille", "Frappe", "Percée", "Feu", "Glace", "Foudre",
          "Nature", "Lumière", "Ténèbres" };

    private static Dictionary<int, string[]>? _monsters;
    private static Dictionary<int, string[]>? _ai;
    private static Dictionary<int, string[]>? _drops;
    private static Dictionary<int, string[]>? _classDrops;
    private static readonly object _lock = new();

    /// <summary>Tables en mémoire (tests) — remplace tout chargement.</summary>
    public static void LoadFrom(string monsterPath, string aiPath,
        string dropPath, string classDropPath)
    {
        lock (_lock)
        {
            _monsters = Rows(monsterPath);
            _ai = Rows(aiPath);
            _drops = Rows(dropPath);
            _classDrops = Rows(classDropPath);
        }
    }

    private static Dictionary<int, string[]> Rows(string path)
    {
        var d = new Dictionary<int, string[]>();
        foreach (var row in IniTable.ParseFile(path, EdenEncoding.Big5).Rows)
            if (row.Length > 2 && int.TryParse(row[0], out var id))
                d[id] = row;
        return d;
    }

    private static void EnsureLoaded()
    {
        lock (_lock)
        {
            Dictionary<int, string[]>? Load(string name)
            {
                var p = TextResolver.ResolveFile?.Invoke("data/db/" + name);
                return p == null ? null : Rows(p);
            }
            _monsters ??= Load("c_monster.ini");
            _ai ??= Load("c_ai.ini");
            _drops ??= Load("c_dropitem.ini");
            _classDrops ??= Load("c_classdropitem.ini");
        }
    }

    /// <summary>Fiche du monstre <paramref name="mobId"/>, null si inconnu.</summary>
    public static Sheet? Build(int mobId)
    {
        EnsureLoaded();
        if (_monsters == null || !_monsters.TryGetValue(mobId, out var r))
            return null;
        var sheet = new Sheet
        {
            MobId = mobId,
            Name = TextResolver.MonsterName(mobId) ?? IniTable.GetStr(r, 2),
            Level = IniTable.GetInt(r, 4),
        };
        FillStats(sheet, r);
        FillDrops(sheet, mobId, r);
        FillSkills(sheet, r);
        return sheet;
    }

    private static string N(long v) => v.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

    private static void FillStats(Sheet s, string[] r)
    {
        var mask = IniTable.GetInt(r, 10);
        var patience = BreakPointUi.ParsePatience(r);
        var canBreak = mask != 0 && patience > 0;
        var atk = IniTable.GetInt(r, 14);
        var l = s.Stats;
        l.Add(new("PV", N(IniTable.GetLong(r, 8))));
        l.Add(new("PM", N(IniTable.GetLong(r, 9))));
        l.Add(new("BP", canBreak ? N(patience) : "—"));
        // Un seul attribut d'attaque natif (col 14) : sorts et mêlée du
        // mob s'appuient dessus (CalculatePhysicalDamage / sorts mob).
        l.Add(new("P-ATK", N(atk)));
        l.Add(new("M-ATK", N(atk)));
        l.Add(new("Précision", N(IniTable.GetInt(r, 19))));
        l.Add(new("Esquive", N(IniTable.GetInt(r, 20))));
        l.Add(new("Taux de crit. phy.", N(IniTable.GetInt(r, 21))));
        l.Add(new("Taux de crit. mag.", N(IniTable.GetInt(r, 23))));
        l.Add(new("Dégâts crit. phy.", N(IniTable.GetInt(r, 22)) + " %"));
        l.Add(new("Dégâts crit. mag.", N(IniTable.GetInt(r, 24)) + " %"));
        l.Add(new("Déf. phy.", N(IniTable.GetInt(r, 18))));
        // def. magique : jamais écrite par la conversion native (0) ;
        // parade/blocage : pas de colonne monstre.
        l.Add(new("Déf. mag.", "0"));
        l.Add(new("Parade", "—"));
        if (canBreak)
        {
            var weak = new List<string>();
            for (var i = 0; i < 9; i++)
                if ((mask & (1 << i)) != 0) weak.Add(Elements[i]);
            l.Add(new("Faiblesses", string.Join(", ", weak)));
        }
        for (var i = 0; i < 9; i++)
        {
            var v = IniTable.GetInt(r, 25 + i);
            if (v != 0) l.Add(new("Rés. " + Elements[i], N(v)));
        }
    }

    // --- drops ---------------------------------------------------------

    /// <summary>Pool de <c>c_dropitem</c> : groupes de 4 champs
    /// <c>itemId | nom | quantité | chance %</c> dans [from, to) — même
    /// grammaire que le parseur serveur (<c>DropAndSpeedParsers.ParsePool</c>).</summary>
    internal static List<DropLine> ParsePool(string[] row, int from, int to)
    {
        var res = new List<DropLine>();
        var inv = CultureInfo.InvariantCulture;
        var i = from;
        to = Math.Min(to, row.Length);
        while (i + 3 < to)
        {
            if (row[i].Length == 0 && row[i + 1].Length == 0 &&
                row[i + 2].Length == 0 && row[i + 3].Length == 0) { i += 4; continue; }
            if (int.TryParse(row[i], out var item) && item > 0 &&
                double.TryParse(row[i + 2], NumberStyles.Float, inv, out var q) &&
                double.TryParse(row[i + 3], NumberStyles.Float, inv, out var rate) &&
                !int.TryParse(row[i + 1], out _))
            {
                res.Add(new DropLine(item, q > 0 ? (int)q : 1, rate));
                i += 4;
            }
            else i++;
        }
        return res;
    }

    /// <summary>Entrées d'une ligne c_classdropitem (chance|item|nom|qté
    /// répétés) — grammaire du parseur serveur.</summary>
    internal static List<DropLine> ParseClassDrop(string[] row)
    {
        var res = new List<DropLine>();
        for (var i = 1; i + 3 < row.Length; i++)
        {
            if (!int.TryParse(row[i], out var chance) || chance <= 0 || chance > 100000) continue;
            if (!int.TryParse(row[i + 1], out var item) || item <= 0) continue;
            if (int.TryParse(row[i + 2], out _)) continue;
            var q = int.TryParse(row[i + 3], out var qq) ? qq : 1;
            res.Add(new DropLine(item, Math.Max(1, q), Math.Min(100, chance)));
            i += 3;
        }
        return res;
    }

    private static void FillDrops(Sheet s, int mobId, string[] r)
    {
        if (_drops != null && _drops.TryGetValue(mobId, out var d))
        {
            s.NormalDrops.AddRange(ParsePool(d, 11, 91));
            s.BreakDrops.AddRange(ParsePool(d, 96, 176));
        }
        // Le break pool n'a de sens que pour un mob cassable.
        if (IniTable.GetInt(r, 10) == 0) s.BreakDrops.Clear();
        if (_classDrops == null) return;
        for (var c = 40; c <= 49 && c < r.Length; c++)
            if (IniTable.GetInt(r, c) is > 0 and var id &&
                _classDrops.TryGetValue(id, out var cd))
                s.ClassDrops.AddRange(ParseClassDrop(cd));
    }

    // --- skills --------------------------------------------------------

    private static int V(string[] ai, int col) => IniTable.GetInt(ai, col);

    private static void FillSkills(Sheet s, string[] r)
    {
        var aiId = IniTable.GetInt(r, 38);
        string[]? ai = null;
        _ai?.TryGetValue(aiId, out ai);
        var chance = ai == null ? new int[3] : new[] { V(ai, 6), V(ai, 8), V(ai, 10) };
        var normal = new[] { IniTable.GetInt(r, 44), IniTable.GetInt(r, 45),
            IniTable.GetInt(r, 46) };
        var seen = new HashSet<int>();
        for (var i = 0; i < 3; i++)
            if (normal[i] > 0 && seen.Add(normal[i]))
                s.NormalSkills.Add(new SkillLine(normal[i],
                    ai == null ? "" : $"Chance : {chance[i]} %"));
        if (ai == null) return;
        var th = new[] { V(ai, 5), V(ai, 7), V(ai, 9) };
        var cd = new[] { V(ai, 15), V(ai, 17), V(ai, 19) };
        var cool = new[] { V(ai, 14), V(ai, 16), V(ai, 18) };
        for (var i = 0; i < 3; i++)
            if (cd[i] > 0)
                s.CountdownSkills.Add(new SkillLine(cd[i],
                    $"PV ≤ {th[i]} % · relance {cool[i]} s"));
    }
}
