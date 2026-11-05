using System.Text;
using System.Text.RegularExpressions;

namespace EdenClient.Data;

/// <summary>
/// Traduction EN des chaînes littérales chinoises embarquées dans les
/// propriétés <c>Text</c>/<c>Tooltip</c> des <c>.layout</c> CEGUI.
///
/// Le client natif écrase ces textes au <c>setText(index)</c> depuis le
/// code fenêtre (table <c>C_TextIndex.ini</c> remplacée en mémoire par
/// la variante anglaise <c>T_/t_textindex.ini</c>) — le layout ne sert
/// que de gabarit. Sans code fenêtre natif, on reproduit l'effet par
/// correspondance de contenu : le dictionnaire
/// <c>gamedata/ui_text_en.tsv</c> est généré par
/// <c>tools/gen_ui_text.py</c> (alignement des tables textindex +
/// dictionnaire manuel <c>tools/ui_text_overrides.tsv</c>).
///
/// Clé = forme canonique du texte (<see cref="Canon"/> : espaces
/// Unicode réduites à une espace simple + trim). Un texte absent du
/// dictionnaire est renvoyé tel quel (repli neutre).
/// </summary>
public static class UiTextEn
{
    private static readonly object _lock = new();
    private static Dictionary<string, string>? _map;

    /// <summary>Résolution du TSV — branchée par la couche Godot
    /// (GameScene) sur <c>DataPaths.ResolveFile</c>. En dehors du
    /// moteur (NetTests sans GodotSharp) le défaut cherche
    /// <c>gamedata/ui_text_en.tsv</c> en remontant depuis le CWD.</summary>
    public static Func<string, string?>? ResolveFile;

    private static string? DefaultResolve(string relPath)
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "gamedata",
                relPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    /// <summary>Traduit <paramref name="text"/> s'il figure dans le
    /// dictionnaire ; renvoie l'entrée inchangée sinon. En langue
    /// non-EN, les chaînes anglaises en dur sont aussi traduites via
    /// <c>ui_text_en_&lt;lang&gt;.tsv</c> (alignement textindex).</summary>
    public static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var map = Ensure();
        var key = Canon(text);
        if (map != null && map.TryGetValue(key, out var en))
            return en;
        if (TextResolver.Language != "en" &&
            EnsureEn().TryGetValue(key, out var fr))
            return fr;
        return text;
    }

    private static readonly object _lockEn = new();
    // Publié atomiquement une fois rempli (jamais modifié ensuite) : lecture sans verrou sûre.
    private static volatile Dictionary<string, string> _mapEn = new();

    /// <summary>Forme canonique commune générateur/runtime : toute
    /// suite d'espaces Unicode (\n, \r, \t, \v, \u3000 fullwidth…)
    /// → une espace simple, trim en bord.</summary>
    public static string Canon(string s) =>
        s.Length == 0 ? s : WhitespaceRx().Replace(s, " ").Trim();

    private static Regex WhitespaceRx() => _wsRx ??= new(@"\s+",
        RegexOptions.Compiled);
    private static Regex? _wsRx;

    /// <summary>Chargement paresseux du TSV
    /// <c>ui_text_en_&lt;lang&gt;.tsv</c> (clé = anglais canonique) —
    /// chaînes hardcodées du code C#. Vide si absent.</summary>
    private static Dictionary<string, string> EnsureEn()
    {
        if (_enTried) return _mapEn;
        lock (_lockEn)
        {
            if (_enTried) return _mapEn;
            var path = (ResolveFile ?? DefaultResolve)(
                $"ui_text_en_{TextResolver.Language}.tsv");
            var en = new Dictionary<string, string>();
            try
            {
                if (path != null)
                    foreach (var raw in Vfs.EdenVfs.ReadLines(path))
                    {
                        var line = raw;
                        if (line.Length == 0 || line[0] == '#') continue;
                        var tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        // Les clés TSV portent l'espace finale de la
                        // chaîne source (« Archive points: ») alors que
                        // Translate cherche Canon(text) — canoniser à
                        // l'indexation (idempotent si déjà canonique).
                        en[Canon(line[..tab])] =
                            line[(tab + 1)..].Replace("\\n", "\n");
                    }
            }
            catch (Exception) { /* dictionnaire vide = repli neutre */ }
            _mapEn = en;
            _enTried = true;
            return en;
        }
    }
    private static volatile bool _enTried;

    /// <summary>Chargement paresseux du TSV
    /// <c>ui_text_en.tsv</c> (clé canonique \t anglais). Échec →
    /// dictionnaire vide mis en cache.</summary>
    private static Dictionary<string, string>? Ensure()
    {
        if (_map != null) return _map;
        lock (_lock)
        {
            if (_map != null) return _map;
            var map = new Dictionary<string, string>();
            var tsv = $"ui_text_{TextResolver.Language}.tsv";
            var path = (ResolveFile ?? DefaultResolve)(tsv);
            // EDENFR pur : jamais de repli sur le dictionnaire anglais.
            if (path == null && !TextResolver.NativeOnly)
                path = (ResolveFile ?? DefaultResolve)("ui_text_en.tsv");
            try
            {
                if (path != null)
                {
                    foreach (var raw in Vfs.EdenVfs.ReadLines(path))
                    {
                        var line = raw;
                        if (line.Length == 0 || line[0] == '#')
                            continue;
                        var tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        var key = Canon(line[..tab]);
                        var en = line[(tab + 1)..].Replace("\\n", "\n");
                        map.TryAdd(key, en);
                    }
                }
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke($"[uitext] {e.Message}");
            }
            _map = map;
            return _map;
        }
    }

    private static volatile HashSet<string>? _targets;

    /// <summary><paramref name="text"/> est une <i>valeur</i> du
    /// dictionnaire courant — utilisé par AutoTranslate pour ne pas
    /// marquer « miss » un label posé déjà traduit par le code.</summary>
    public static bool IsTranslation(string text)
    {
        var t = _targets;
        if (t == null)
        {
            var map = Ensure() ?? new Dictionary<string, string>();
            t = new HashSet<string>(map.Values.Concat(EnsureEn().Values).Select(Canon));
            _targets = t;
        }
        return t.Contains(Canon(text));
    }

    /// <summary>Invalidation du cache (tests).</summary>
    public static void Reset()
    {
        lock (_lock) { _map = null; _targets = null; }
        lock (_lockEn) { _mapEn = new(); _enTried = false; }
    }
}
