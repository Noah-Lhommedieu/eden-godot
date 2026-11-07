using System.Text.RegularExpressions;

namespace Eden.Tests.Architecture;

/// <summary>
/// Règles d'architecture « mécaniques » (PLAN-ACTION-3 §10 Q9). Les règles sont
/// des fonctions pures sur du texte pour être testables seules ; la baseline
/// (<c>Data/architecture-baseline.tsv</c>) fige l'existant : elle ne peut que
/// diminuer.
/// </summary>
public static class ArchitectureScanner
{
    public const int MaxLines = 400;

    private static readonly Regex EmptyCatch = new(
        @"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.Compiled);
    private static readonly Regex DirectRandom = new(
        @"Random\.Shared|new\s+Random\s*\(", RegexOptions.Compiled);
    private static readonly Regex DirectNow = new(
        @"DateTime(Offset)?\.(Now|UtcNow)", RegexOptions.Compiled);

    public static int LineCount(string text) => text.Split('\n').Length;
    public static int CountEmptyCatch(string text) => EmptyCatch.Matches(text).Count;
    public static int CountDirectRandom(string text) => DirectRandom.Matches(text).Count;
    public static int CountDirectNow(string text) => DirectNow.Matches(text).Count;

    /// <summary>Racine du dépôt serveur (dossier contenant Eden.sln).</summary>
    public static string ServerRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Eden.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Eden.sln introuvable");
    }

    /// <summary>Fichiers C# du code de jeu (hors générés, obj, bin).</summary>
    public static IEnumerable<(string Rel, string Text)> SourceFiles()
    {
        var root = ServerRoot();
        var src = Path.Combine(root, "src");
        foreach (var f in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.EndsWith(".g.cs"))
                continue;
            yield return (rel, File.ReadAllText(f));
        }
    }

    /// <summary>Fichiers où l'accès direct à l'heure/au hasard est légitime.</summary>
    public static bool IsFoundation(string rel) =>
        rel.Contains("/Game/Foundation/");

    public static bool IsZoneEntryFlow(string rel) =>
        Path.GetFileName(rel).StartsWith("ZoneEntryFlow", StringComparison.Ordinal);
}

/// <summary>Baseline : (règle, chemin) → valeur maximale tolérée.</summary>
public sealed class ArchitectureBaseline
{
    private readonly Dictionary<(string Rule, string Path), int> _v = new();

    public static string FilePath() =>
        Path.Combine(AppContext.BaseDirectory, "Data", "architecture-baseline.tsv");

    public static string SourceFilePath() =>
        Path.Combine(ArchitectureScanner.ServerRoot(), "tests", "Eden.Tests",
            "Data", "architecture-baseline.tsv");

    public static ArchitectureBaseline Load()
    {
        var b = new ArchitectureBaseline();
        foreach (var l in File.ReadAllLines(FilePath()))
        {
            if (l.Length == 0 || l[0] == '#') continue;
            var p = l.Split('\t');
            b._v[(p[0], p[1])] = int.Parse(p[2]);
        }
        return b;
    }

    /// <summary>Plafond toléré (0 si le fichier n'est pas dans la baseline).</summary>
    public int Allowed(string rule, string path) =>
        _v.TryGetValue((rule, path), out var n) ? n : 0;

    public static void Write(IEnumerable<(string Rule, string Path, int Value)> rows)
    {
        var lines = new List<string>
        {
            "# Baseline d'architecture — ne peut que DIMINUER (PLAN-ACTION-3 §10 Q9).",
            "# Régénérer (seulement pour baisser) : EDEN_ARCH_REGENERATE=1 dotnet test --filter ArchitectureTests",
            "# règle\tchemin\tvaleur",
        };
        lines.AddRange(rows.OrderBy(r => r.Rule).ThenBy(r => r.Path)
            .Select(r => $"{r.Rule}\t{r.Path}\t{r.Value}"));
        File.WriteAllLines(SourceFilePath(), lines);
    }
}
