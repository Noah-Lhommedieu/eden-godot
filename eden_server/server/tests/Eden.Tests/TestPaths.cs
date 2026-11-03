namespace Eden.Tests;

/// <summary>
/// Chemins du dépôt résolus à l'exécution (dépôt autonome, aucun
/// <c>/home/...</c> en dur). La racine est retrouvée en remontant depuis
/// le répertoire de sortie du test jusqu'au dossier qui contient
/// <c>server_us/Data</c> et <c>AGENTS.md</c>.
/// </summary>
internal static class TestPaths
{
    /// <summary>Racine du dépôt (contient <c>server_us/</c> et <c>AGENTS.md</c>).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary><c>server_us/Data</c> — données natives du serveur de référence.</summary>
    public static string ServerUsData { get; } =
        Path.Combine(RepoRoot, "server_us", "Data");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "server_us", "Data"))
                && File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Racine du dépôt introuvable (repère server_us/Data + AGENTS.md) " +
            $"en remontant depuis {AppContext.BaseDirectory}");
    }
}
