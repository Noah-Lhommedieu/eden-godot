using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de talents de classe — correspond au systeme de talents/certificats.
/// Charge la table canonique (C_Talent <b>Reforged</b> quand elle est
/// disponible — le client joué —, S_Talent.ini en repli) et gère les
/// points de talent dépensés par les joueurs dans les arbres de leur
/// classe.
/// </summary>
public sealed class TalentService
{
    private readonly Dictionary<int, GameDataParsers.TalentTree> _trees;
    /// <summary>Toutes les dispositions d'arbres connues : [0] =
    /// table CANONIQUE = C_Talent.ini Reforged (la table que le client
    /// joué lit — 495/915 arbres diffèrent de S_Talent), puis les
    /// variantes restantes (S_Talent serveur = C_Talent du client
    /// projet). La validation SetAAPoint évalue les règles natives par
    /// variante et accepte si l'une d'elles passe : le paquet
    /// n'emporte qu'un effectId — impossible de savoir quelle table le
    /// client a lu, et les deux clients doivent rester jouables.</summary>
    private readonly List<Dictionary<int, GameDataParsers.TalentTree>>
        _layouts;
    private readonly Dictionary<int, Dictionary<int, int>> _spentByCharacter = new();

    public TalentService(IEnumerable<GameDataParsers.TalentTree> trees)
        : this(trees, null) { }

    private TalentService(IEnumerable<GameDataParsers.TalentTree> trees,
        IEnumerable<IEnumerable<GameDataParsers.TalentTree>>? alternates)
    {
        _trees = trees.ToDictionary(t => t.Id);
        _layouts = new List<Dictionary<int, GameDataParsers.TalentTree>>
            { _trees };
        if (alternates != null)
            foreach (var alt in alternates)
            {
                var map = alt.ToDictionary(t => t.Id);
                if (map.Count > 0) _layouts.Add(map);
            }
    }

    /// <summary>Table canonique : C_Talent.ini du client Reforged
    /// (<see cref="Data.ReforgedDataLocator"/> — env
    /// <c>EDEN_REFORGED_DB</c> ou probing
    /// <c>reforged/EdenReforged/data/db/C_Talent.ini</c> en remontant
    /// les parents de decodedDir/BaseDirectory). S_Talent.ini sert de
    /// <b>repli</b> si la table Reforged est absente, et reste
    /// chargée comme variante d'évaluation (le client projet lit une
    /// table identique à S_Talent — les deux clients restent
    /// jouables). <c>EDEN_TALENT_ALT</c> (chemins séparés par ';') et
    /// <c>&lt;decodedDir&gt;/C_Talent.ini</c> ajoutent d'autres
    /// variantes. Une variante identique à la canonique est ignorée
    /// (pas de double travail).</summary>
    public static TalentService LoadFromDataDir(string decodedDir)
    {
        var server = GameDataParsers.ParseTalents(
            Path.Combine(decodedDir, "S_Talent.ini"));
        var layouts = new List<List<GameDataParsers.TalentTree>>();
        var labels = new List<string>();
        void AddLayout(string path)
        {
            try
            {
                var parsed = GameDataParsers.ParseTalents(path);
                if (parsed.Count == 0
                    || layouts.Any(l => SameLayout(l, parsed)))
                    return;
                layouts.Add(parsed);
                labels.Add(path);
            }
            catch (Exception ex)
            {
                Eden.Core.Logging.EdenLog.Write(
                    $"[Talents] variante {path} ignorée : {ex.Message}");
            }
        }
        // 1. Canonique : la table lue par le client Reforged.
        var reforged = Data.ReforgedDataLocator.FindFile(
            "C_Talent.ini", decodedDir, AppContext.BaseDirectory);
        if (reforged != null) AddLayout(reforged);
        // 2. Variantes supplémentaires : env explicite puis copie
        //    éventuelle à côté des données décodées.
        var env = Environment.GetEnvironmentVariable("EDEN_TALENT_ALT");
        if (!string.IsNullOrWhiteSpace(env))
            foreach (var p in env.Split(';',
                         StringSplitOptions.RemoveEmptyEntries
                             | StringSplitOptions.TrimEntries))
                if (File.Exists(p)) AddLayout(p);
        var side = Path.Combine(decodedDir, "C_Talent.ini");
        if (File.Exists(side)) AddLayout(side);
        // 3. Repli canonique + variante : S_Talent.ini natif.
        if (layouts.Count == 0)
        {
            layouts.Add(server);
            Eden.Core.Logging.EdenLog.Write(
                "[Talents] canonique : S_Talent.ini " +
                "(table Reforged absente — repli)");
        }
        else if (!layouts.Any(l => SameLayout(l, server)))
            layouts.Add(server);
        for (var i = 0; i < labels.Count; i++)
            Eden.Core.Logging.EdenLog.Write(
                $"[Talents] {(i == 0 ? "canonique" : "variante")} " +
                $": {labels[i]} ({layouts[i].Count} arbres)");
        return new TalentService(layouts[0], layouts.Skip(1));
    }

    /// <summary>Deux listes d'arbres sont identiques si chaque arbre
    /// porte les mêmes slots (mêmes 6 champs, même ordre).</summary>
    private static bool SameLayout(
        List<GameDataParsers.TalentTree> a,
        List<GameDataParsers.TalentTree> b)
    {
        var mb = b.ToDictionary(t => t.Id);
        if (a.Count != b.Count) return false;
        foreach (var t in a)
        {
            if (!mb.TryGetValue(t.Id, out var o)
                || o.Slots.Count != t.Slots.Count)
                return false;
            for (var i = 0; i < t.Slots.Count; i++)
                if (t.Slots[i].SpellId != o.Slots[i].SpellId
                    || !t.Slots[i].Params.SequenceEqual(
                        o.Slots[i].Params))
                    return false;
        }
        return true;
    }

    /// <summary>Arbre de la disposition PRINCIPALE (C_Talent Reforged
    /// — canonique ; S_Talent en repli quand Reforged est absent).</summary>
    public GameDataParsers.TalentTree? GetTree(int treeId)
        => _trees.TryGetValue(treeId, out var t) ? t : null;

    /// <summary>Les 3 branches (treeBase*10+{1,2,3}) de CHAQUE
    /// disposition chargée — la validation SetAAPoint évalue les règles
    /// natives par variante (gate de palier / pré-requis) et accepte si
    /// l'une valide : le client ne dit pas quelle table il a lue et
    /// client projet / Reforged divergent sur 495 arbres.</summary>
    public IEnumerable<IReadOnlyList<GameDataParsers.TalentTree>>
        BranchSets(int treeBase)
    {
        foreach (var layout in _layouts)
        {
            var set = new List<GameDataParsers.TalentTree>(3);
            for (var b = 1; b <= 3; b++)
                if (layout.TryGetValue(treeBase * 10 + b, out var t))
                    set.Add(t);
            if (set.Count > 0) yield return set;
        }
    }

    /// <summary>Union des spellIds des 3 branches de la classe sur
    /// TOUTES les dispositions — pour les filtres « appartient à
    /// l'arbre courant » (reset AA, application des mods de sorts).</summary>
    public HashSet<short> OwnedSpellIds(int treeBase)
    {
        var owned = new HashSet<short>();
        foreach (var set in BranchSets(treeBase))
            foreach (var tree in set)
                foreach (var s in tree.Slots)
                    owned.Add((short)s.SpellId);
        return owned;
    }

    /// <summary>Arbres disponibles pour une classe (par nom Big5).</summary>
    public IReadOnlyList<GameDataParsers.TalentTree> GetTreesForClass(string className)
        => _trees.Values.Where(t => t.ClassName == className).ToList();

    /// <summary>
    /// Depense des points dans un slot de talent.
    /// </summary>
    public bool SpendPoints(int characterId, int treeId, int slotIndex, int points)
    {
        var tree = GetTree(treeId);
        if (tree == null || slotIndex < 0 || slotIndex >= tree.Slots.Count)
            return false;
        var map = _spentByCharacter.TryGetValue(characterId, out var m)
            ? m : _spentByCharacter[characterId] = new Dictionary<int, int>();
        var key = treeId * 100 + slotIndex;
        map[key] = map.GetValueOrDefault(key) + points;
        return true;
    }

    /// <summary>
    /// Spells actifs issus des talents d'un joueur.
    /// </summary>
    public IReadOnlyList<int> GetActiveTalentSpells(int characterId)
    {
        if (!_spentByCharacter.TryGetValue(characterId, out var spent))
            return Array.Empty<int>();
        var spells = new List<int>();
        foreach (var (key, points) in spent)
        {
            if (points <= 0) continue;
            var treeId = key / 100;
            var slotIndex = key % 100;
            var tree = GetTree(treeId);
            if (tree != null && slotIndex < tree.Slots.Count)
                spells.Add(tree.Slots[slotIndex].SpellId);
        }
        return spells;
    }

    public int GetPointsSpent(int characterId, int treeId, int slotIndex)
        => _spentByCharacter.TryGetValue(characterId, out var m)
            ? m.GetValueOrDefault(treeId * 100 + slotIndex)
            : 0;
}
