namespace Eden.Core.Data;

/// <summary>
/// Localise la base de données extraite du client <b>Reforged</b>
/// (<c>reforged/EdenReforged/data/db/</c> à la racine historique, ou
/// <c>eden_server/reforged/EdenReforged/data/db/</c> dans le layout
/// <c>reverseden/portage</c> où l'oracle reste hors du projet copié).
///
/// Politique projet (lot 12, demande utilisateur) : le client joué est
/// Reforged — un client rééquilibré dont les tables C_*/c_* diffèrent
/// des S_* natives sur des milliers de lignes. La table que le client
/// affiche est la <b>référence canonique</b> pour les valeurs
/// gameplay : le serveur valide et applique avec la table que le
/// client lit (cf. docs/generated/TABLES-DIVERGENCE.md).
///
/// Résolution du dossier :
///   1. <c>EDEN_REFORGED_DB</c> — chemin direct vers le dossier db
///      (les jetons « off »/« none »/« - » DÉSACTIVENT l'overlay) ;
///   2. sinon remonte les parents de chaque ancre (decodedDir,
///      AppContext.BaseDirectory, répertoire courant) jusqu'à trouver
///      <c>reforged/EdenReforged/data/db</c> puis
///      <c>eden_server/reforged/EdenReforged/data/db</c> ;
///   3. absent → null (le serveur retombe sur les tables natives S_*).
/// </summary>
public static class ReforgedDataLocator
{
    /// <summary>Nom de la variable d'environnement forçant le
    /// dossier db Reforged.</summary>
    public const string EnvVar = "EDEN_REFORGED_DB";

    /// <summary>Chemin relatif depuis la racine historique du dépôt.</summary>
    public const string RepoRelative = "reforged/EdenReforged/data/db";

    /// <summary>Chemin relatif depuis la racine <c>reverseden</c> du
    /// layout portage : l'oracle Reforged vit dans
    /// <c>eden_server/</c>, en lecture seule, à côté de
    /// <c>portage/eden_server</c>.</summary>
    public const string PortageRelative =
        "eden_server/reforged/EdenReforged/data/db";

    private static readonly string[] RelativeCandidates =
        { RepoRelative, PortageRelative };

    /// <summary>Dossier db Reforged trouvé, sinon null. Les ancres
    /// typiques : le dossier de données décodées serveur
    /// (<c>server_us/Data/db_decoded</c>) et
    /// <see cref="AppContext.BaseDirectory"/>.</summary>
    public static string? FindDbDir(params string[] anchors)
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (IsDisableToken(env)) return null;
            return Directory.Exists(env) ? Path.GetFullPath(env) : null;
        }
        foreach (var anchor in anchors)
            foreach (var d in Climb(anchor))
                foreach (var relative in RelativeCandidates)
                {
                    var p = Path.Combine(d, relative);
                    if (Directory.Exists(p)) return p;
                }
        foreach (var d in Climb(Directory.GetCurrentDirectory()))
            foreach (var relative in RelativeCandidates)
            {
                var p = Path.Combine(d, relative);
                if (Directory.Exists(p)) return p;
            }
        return null;
    }

    /// <summary>Fichier <paramref name="fileName"/> dans la db
    /// Reforged, ou null si le dossier ou le fichier est absent.</summary>
    public static string? FindFile(string fileName,
        params string[] anchors)
    {
        var dir = FindDbDir(anchors);
        if (dir == null) return null;
        // Les noms de fichiers du client varient en casse
        // (c_enchant.ini vs C_Enchant.ini…) — comparaison
        // insensible à la casse sur le contenu du dossier.
        foreach (var f in Directory.EnumerateFiles(dir))
            if (string.Equals(Path.GetFileName(f), fileName,
                    StringComparison.OrdinalIgnoreCase))
                return f;
        return null;
    }

    /// <summary>« off »/« none »/« - » désactivent explicitement
    /// l'overlay Reforged (retour aux tables natives).</summary>
    private static bool IsDisableToken(string v)
        => v.Trim() is "off" or "none" or "-" or "0";

    /// <summary>Énumère <paramref name="dir"/> puis chaque parent
    /// jusqu'à la racine.</summary>
    private static IEnumerable<string> Climb(string dir)
    {
        for (var d = Path.GetFullPath(dir); d != null;
             d = Path.GetDirectoryName(d))
            yield return d;
    }
}
