using System;
using System.IO;

namespace Limitless.Studio.Services;

/// <summary>Chemins du dépôt — racine trouvée par remontée depuis
/// le binaire/cwd jusqu'au dossier qui contient `server_us/Data` +
/// `AGENTS.md` (dépôt autonome, aucun chemin absolu en dur).</summary>
public static class GamePaths
{
    /// <summary>Racine du dépôt eden_server.</summary>
    public static string RepoRoot()
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
        return Directory.GetCurrentDirectory();
    }

    /// <summary>`server_us/Data/db_decoded` — .ini serveur Big5.</summary>
    public static string IniDir() =>
        Path.Combine(RepoRoot(), "server_us", "Data", "db_decoded");

    /// <summary>`reforged/EdenReforged` — racine du client extrait.</summary>
    public static string ClientDir() =>
        Path.Combine(RepoRoot(), "reforged", "EdenReforged");

    /// <summary>`reforged/EdenReforged/pkg` — archives pkg + pkg.idx.</summary>
    public static string PkgDir() =>
        Path.Combine(ClientDir(), "pkg");

    /// <summary>`reforged/EdenReforged/UI` — assets UI extraits
    /// en clair (icônes DDS/PNG).</summary>
    public static string UiDir() =>
        Path.Combine(ClientDir(), "UI");

    /// <summary>`reforged/EdenReforged/data/db` — tables client,
    /// dont les traductions EN `t_*.ini`.</summary>
    public static string ClientDbDir() =>
        Path.Combine(ClientDir(), "data", "db");
}
