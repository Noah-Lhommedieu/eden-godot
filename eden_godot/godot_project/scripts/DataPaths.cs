using Godot;

namespace EdenClient;

/// <summary>
/// Résolution centralisée des racines de données runtime — le client
/// lit des fichiers HORS du pck (uidata/, data/, map/, assets/
/// convertis, client.cfg) qui doivent être trouvables aussi bien en
/// dev (symlink <c>raw_assets/</c> à la racine du portage) que dans un
/// build exporté (<c>&lt;exe&gt;/gamedata/</c>).
///
/// Ordre de recherche des <b>racines candidates</b> (chaque racine
/// contient directement <c>uidata/</c>, <c>data/</c>, <c>map/</c>,
/// <c>assets/</c>… ou un sous-ensemble) :
///   1. <c>$EDEN_DATA_DIR</c> — la racine elle-même ou son enfant
///      <c>raw_assets/</c> s'il existe ;
///   2. <c>&lt;dossier de l'exécutable&gt;/gamedata</c> — package
///      produit par <c>tools/package_dist.sh</c> ;
///   3. dev : remontée depuis <c>res://</c> (= godot_project/) et le
///      CWD à la recherche d'un dossier <c>raw_assets/</c> — le symlink
///      du portage est trouvé au premier cran (<c>res://../raw_assets</c>) ;
///   4. <c>res://gamedata</c> — dossier du projet embarqué dans le pck.
///
/// Les assets convertis (<c>assets/textures|models|anims</c>) relèvent
/// de <see cref="AssetCatalog.AssetsRoot"/> qui ajoute
/// <c>res://assets</c> à la chaîne (dossier <c>.gdignore</c>'d du
/// projet — présent sur disque en dev, absent du pck).
/// </summary>
public static class DataPaths
{
    private static IReadOnlyList<string>? _roots;

    /// <summary>Dossier contenant l'exécutable — en export c'est là que
    /// vit <c>gamedata/</c>. En éditeur c'est le binaire Godot (le
    /// candidat <c>gamedata</c> n'existe alors pas et est ignoré).</summary>
    public static string ExeDir { get; } = ComputeExeDir();

    /// <summary>Invalide le cache des racines (tests).</summary>
    public static void Reset() => _roots = null;

    /// <summary>Racines candidates existantes, par priorité
    /// décroissante (chemins absolus, dédupliquées).</summary>
    public static IReadOnlyList<string> CandidateDataRoots() =>
        _roots ??= BuildCandidates();

    /// <summary>Première racine candidate contenant le chemin relatif
    /// <paramref name="relPath"/> (fichier ou dossier, résolution
    /// insensible à la casse). Retourne la racine, pas le chemin
    /// complet — utiliser <see cref="ResolveFile"/> pour le fichier.
    /// En mode pack-seul : la pseudo-racine « epk:// » (Path.Combine
    /// la prolonge en URI valide).</summary>
    public static string? RootContaining(string relPath)
    {
        foreach (var root in CandidateDataRoots())
        {
            var combined = Path.Combine(root,
                relPath.Replace('/', Path.DirectorySeparatorChar));
            if (AssetCatalog.ResolveFile(combined) != null)
                return root;
        }
        return Vfs.EdenVfs.PackResolve(relPath) != null
            ? Vfs.EdenVfs.Scheme : null;
    }

    /// <summary>Première racine candidate ressemblant à une racine de
    /// données brutes (contient <c>map/</c>, <c>data/</c> ou
    /// <c>uidata/</c>) — null si aucune. En mode pack-seul : la
    /// pseudo-racine « epk:// ».</summary>
    public static string? FindDataRoot()
    {
        foreach (var root in CandidateDataRoots())
        {
            if (Directory.Exists(Path.Combine(root, "map")) ||
                Directory.Exists(Path.Combine(root, "data")) ||
                Directory.Exists(Path.Combine(root, "uidata")))
                return root;
        }
        return Vfs.EdenVfs.PackExists("map") ||
               Vfs.EdenVfs.PackExists("data") ||
               Vfs.EdenVfs.PackExists("uidata")
            ? Vfs.EdenVfs.Scheme : null;
    }

    /// <summary>Premier sous-dossier <paramref name="name"/> trouvé
    /// sous les racines candidates (chemin absolu du sous-dossier,
    /// insensible à la casse) — ex. <c>FindSubdir("uidata")</c>.</summary>
    public static string? FindSubdir(string name)
    {
        var root = RootContaining(name);
        return root == null ? null
            : AssetCatalog.ResolveFile(Path.Combine(root, name));
    }

    /// <summary>Résout un chemin relatif sous les racines candidates
    /// (insensible à la casse, première racine gagnante) — ex.
    /// <c>ResolveFile("client.cfg")</c> ou
    /// <c>ResolveFile("uidata/imagesets/Login.png")</c>. Retourne un
    /// chemin absolu ou une URI « epk://… » quand le fichier n'existe
    /// que dans un pack ; en mode loose-first un hit pack n'arrête
    /// pas le scan — un loose d'une racine suivante écrase.</summary>
    public static string? ResolveFile(string relPath)
    {
        var rel = relPath.Replace('/', Path.DirectorySeparatorChar);
        string? epk = null;
        foreach (var root in CandidateDataRoots())
        {
            var hit = AssetCatalog.ResolveFile(Path.Combine(root, rel));
            if (hit == null) continue;
            if (!Vfs.EdenVfs.IsEpk(hit) || !Vfs.EdenVfs.LooseAllowed)
                return hit;
            epk ??= hit;
        }
        // Aucune racine loose ne contient le fichier : le pack, ou
        // un pack seul sans racines candidates du tout.
        return epk ?? Vfs.EdenVfs.PackResolve(relPath);
    }

    // ------------------------------------------------------------------

    private static List<string> BuildCandidates()
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrEmpty(p) || !Directory.Exists(p))
                return;
            var full = Path.GetFullPath(p);
            if (!list.Contains(full))
                list.Add(full);
        }

        // 1. $EDEN_DATA_DIR — la racine contient soit raw_assets/,
        //    soit directement uidata/, data/, map/, assets/.
        var env = System.Environment.GetEnvironmentVariable(
            "EDEN_DATA_DIR");
        if (!string.IsNullOrEmpty(env))
        {
            Add(Path.Combine(env, "raw_assets"));
            Add(env);
        }

        // 2. <exe>/gamedata — package tools/package_dist.sh.
        Add(Path.Combine(ExeDir, "gamedata"));

        // 3. dev : raw_assets/ en remontant depuis res:// (le symlink
        //    du portage est à res://../raw_assets) et depuis le CWD.
        foreach (var start in new[] { ResRoot(), SafeCwd() })
        {
            var dir = start;
            for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                Add(Path.Combine(dir, "raw_assets"));
                dir = Path.GetDirectoryName(dir);
            }
        }

        // 4. res://gamedata — dossier projet (embarqué dans le pck en
        //    export ; hors pck GlobalizePath donne le chemin disque).
        Add(Path.Combine(ResRoot(), "gamedata"));
        return list;
    }

    private static string ComputeExeDir()
    {
        try
        {
            var d = Path.GetDirectoryName(OS.GetExecutablePath());
            if (!string.IsNullOrEmpty(d))
                return d;
        }
        catch (Exception) { /* hors runtime Godot */ }
        return AppContext.BaseDirectory;
    }

    /// <summary>Chemin absolu de <c>res://</c> ("" hors Godot).</summary>
    private static string ResRoot()
    {
        try
        {
            return ProjectSettings.GlobalizePath("res://");
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeCwd()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
