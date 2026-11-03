namespace EdenClient.Data.Tables;

/// <summary>
/// Compétences et arbre de talents d'une classe, déduits des tables
/// EDENFR <c>c_class.ini</c>, <c>c_spell.ini</c> et <c>c_talent.ini</c>
/// (aucune liste en dur).
///
/// Règle serveur (zone.c : <c>GetClassSpell</c> / <c>GetCommonSpell</c>,
/// reprise dans <c>ClassSkillTable.GrantableSpells</c>) :
///   - « Class Skills » (payés en CP) = vecteur de sorts de la classe
///     (<see cref="ClassTable.ClassDef.ClassSpellIds"/>) ∪ sorts à masque
///     mono-classe (bit classe-1), hors passifs (famille 7) ;
///   - « Reg. Skills » (payés en or, col41 = 1) = sorts dont le masque
///     (col23) contient le bit de la classe de base. Les classes éveil
///     (21-58, 60, 61) n'ont aucun bit propre dans ces masques : elles
///     gardent les Reg. Skills de leur classe de base.
///
/// Colonnes c_spell.ini fr-full (V.7, 44 colonnes — une de moins que le
/// S_Spell V.8 du serveur) : 1 icône, 17 famille, 22 niveau de classe
/// requis, 23 masque de classes, 41 monnaie (1 or / 9 CP), 42 base du
/// coût de montée.
/// </summary>
public static class ClassSkillTree
{
    public sealed class Skill
    {
        public int Id { get; init; }
        public string IconCode { get; init; } = "";
        public int Family { get; init; }
        public int RequiredLevel { get; init; }
        public ulong Mask { get; init; }
        /// <summary>1 = or (Reg.), 9 = points de classe, 0 = non payant.</summary>
        public int Pay { get; init; }
        public int UpgradeBase { get; init; }
    }

    private static Dictionary<int, Skill>? _spells;
    private static Dictionary<int, ClassTable.ClassDef>? _classes;
    private static Dictionary<int, TalentTable.Slot?[]>? _talents;
    private static readonly object _lock = new();

    /// <summary>Charge les trois tables depuis des chemins explicites
    /// (tests) — remplace tout chargement précédent.</summary>
    public static void Load(string spellPath, string classPath,
        string talentPath)
    {
        lock (_lock)
        {
            _spells = LoadSpells(spellPath);
            _classes = ClassTable.Load(classPath);
            _talents = TalentTable.Load(talentPath);
        }
    }

    /// <summary>Oublie les tables chargées (tests).</summary>
    public static void Reset()
    {
        lock (_lock) { _spells = null; _classes = null; _talents = null; }
    }

    /// <summary>Journal injecté par le HUD (<c>GD.Print</c>) — ce fichier
    /// est aussi compilé hors moteur par NetTests.</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Charge à la demande via <paramref name="resolve"/>
    /// (nom relatif → chemin) ; no-op si tout est déjà chargé. KB-15 : une
    /// table non résolue n'est PAS mise en cache (l'ancien dict vide
    /// définitif donnait des grilles vides pour toute la session) — elle
    /// reste nulle, un log le dit, et la prochaine ouverture réessaie.</summary>
    public static void EnsureLoaded(Func<string, string?> resolve)
    {
        lock (_lock)
        {
            if (_spells != null && _classes != null && _talents != null)
                return;
            if (_spells == null)
            {
                var sp = resolve("data/db/c_spell.ini");
                if (sp != null) _spells = LoadSpells(sp);
            }
            if (_classes == null)
            {
                var cl = resolve("data/db/c_class.ini");
                if (cl != null) _classes = ClassTable.Load(cl);
            }
            if (_talents == null)
            {
                var ta = resolve("data/db/c_talent.ini");
                if (ta != null) _talents = TalentTable.Load(ta);
            }
            if (_spells == null || _classes == null || _talents == null)
                Log?.Invoke("[skill] ClassSkillTree : table non résolue " +
                    $"(c_spell={_spells != null}, c_class={_classes != null}, " +
                    $"c_talent={_talents != null}) — réessai à la prochaine " +
                    "ouverture");
            else
                Log?.Invoke($"[skill] ClassSkillTree : {_spells.Count} sorts, " +
                    $"{_classes.Count} classes, {_talents.Count} lignes talents");
        }
    }

    private static Dictionary<int, Skill> LoadSpells(string path)
    {
        var d = new Dictionary<int, Skill>();
        foreach (var row in IniTable.ParseFile(path).Rows)
        {
            var id = IniTable.GetInt(row, 0);
            if (id <= 0) continue;
            ulong.TryParse(IniTable.GetStr(row, 23), out var mask);
            d[id] = new Skill
            {
                Id = id,
                IconCode = IniTable.GetStr(row, 1),
                Family = IniTable.GetInt(row, 17),
                RequiredLevel = IniTable.GetInt(row, 22),
                Mask = mask,
                Pay = IniTable.GetInt(row, 41),
                UpgradeBase = IniTable.GetInt(row, 42),
            };
        }
        return d;
    }

    public static Skill? Get(int spellId) =>
        _spells != null && _spells.TryGetValue(spellId, out var s) ? s : null;

    /// <summary>Classe de base d'une classe éveil (21/22 → 1, 23/24 → 2 …
    /// 57/58 → 19, 60/61 → 59) ; une classe de base se renvoie elle-même.
    /// Classe 20 (Messie) et 59 (Juge) sont des bases.</summary>
    public static int BaseOf(int classId)
    {
        var local = classId % 100;
        var race = classId - local;
        if (local is >= 21 and <= 58) return race + (local - 19) / 2;
        if (local is 60 or 61) return race + 59;
        return classId;
    }

    /// <summary>Définition c_class d'une classe (id local 1-61).</summary>
    public static ClassTable.ClassDef? ClassDef(int classId) =>
        _classes != null &&
        (_classes.TryGetValue(classId, out var d) ||
         _classes.TryGetValue(classId % 100, out d)) ? d : null;

    /// <summary>« Class Skills » (CP) de la classe, dans l'ordre du
    /// vecteur c_class puis mono-masque ; hors passifs.</summary>
    public static List<Skill> ClassSkills(int classId)
    {
        var res = new List<Skill>();
        if (_spells == null) return res;
        var seen = new HashSet<int>();
        var def = ClassDef(classId);
        if (def != null)
            foreach (var id in def.ClassSpellIds)
                if (_spells.TryGetValue(id, out var s) && s.Family != 7 &&
                    seen.Add(id))
                    res.Add(s);
        var local = classId % 100;
        if (local is >= 1 and <= 63)
        {
            var bit = 1UL << (local - 1);
            foreach (var s in _spells.Values.OrderBy(x => x.Id))
                if (s.Mask == bit && s.Family != 7 && seen.Add(s.Id))
                    res.Add(s);
        }
        return res;
    }

    /// <summary>Sort racial (50372-50421 : 5 couleurs × 5 races × 2
    /// paliers) ; <paramref name="raceIndex"/> 0-4.</summary>
    public static bool IsRacial(int spellId, out int raceIndex)
    {
        raceIndex = -1;
        if (spellId < 50372 || spellId > 50421) return false;
        raceIndex = (spellId - 50372) % 10 / 2;
        return true;
    }

    /// <summary>« Reg. Skills » (or) de la classe : masque contenant le bit
    /// de la classe de base, hors sorts génériques (&gt; 30 classes) et
    /// hors sorts raciaux d'une autre race (<paramref name="raceIndex"/>
    /// &lt; 0 : race inconnue, aucun racial).</summary>
    public static List<Skill> RegSkills(int classId, int raceIndex)
    {
        var res = new List<Skill>();
        if (_spells == null) return res;
        var local = BaseOf(classId) % 100;
        if (local is < 1 or > 63) return res;
        var bit = 1UL << (local - 1);
        foreach (var s in _spells.Values.OrderBy(x => x.Id))
        {
            if (s.Pay != 1 || (s.Mask & bit) == 0 ||
                System.Numerics.BitOperations.PopCount(s.Mask) > 30)
                continue;
            if (IsRacial(s.Id, out var r) && r != raceIndex) continue;
            res.Add(s);
        }
        return res;
    }

    /// <summary>Arbre de talents (3 branches × 8 colonnes) de la classe :
    /// lignes <c>classe×10 + branche</c> de c_talent ; une classe éveil
    /// sans ligne propre (39, 40, 43…) reprend l'arbre de sa classe de
    /// base (mêmes talents dans c_class).</summary>
    public static TalentTable.Slot?[][]? Talents(int classId)
    {
        if (_talents == null) return null;
        var local = classId % 100;
        return TalentTable.Branches(_talents, local)
               ?? TalentTable.Branches(_talents, BaseOf(local));
    }
}
