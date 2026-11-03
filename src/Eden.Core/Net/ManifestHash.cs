using System.Security.Cryptography;
using System.Text;

namespace EdenClient.Net;

/// <summary>
/// Empreinte SHA-256 du corpus gamedata local du client — même
/// algorithme que <c>GameDataManifest</c> serveur
/// (<c>server/src/Eden.Core/Data/GameDataManifest.cs</c>) : tous les
/// <c>*.ini</c> du répertoire donné, récursivement, chemins relatifs
/// triés Ordinal normalisés « / » ;
/// <c>SHA-256( Σ utf8(relpath) + 0x00 + sha256(fichier) )</c>.
///
/// Le corpus client (tables <c>C_*.ini</c> de
/// <c>data/db/</c>) n'est PAS le même ensemble de fichiers que le
/// corpus serveur (<c>S_*.ini</c>) : en <c>EDEN_MANIFEST_STRICT=1</c>,
/// un déploiement doit pointer les deux côtés sur le même corpus pour
/// que l'égalité soit possible — limitation documentée ; en mode
/// non-strict l'écart est seulement logué (télémétrie de dérive).
/// </summary>
public static class ManifestHash
{
    /// <summary>Empreinte + nombre de fichiers couverts.</summary>
    public sealed record Result(byte[] Hash, int FileCount)
    {
        /// <summary>Hex minuscule — format véhiculé dans VersionHello.</summary>
        public string Hex => Convert.ToHexString(Hash).ToLowerInvariant();
    }

    /// <summary>Calcule le manifest d'un répertoire gamedata — null si
    /// le répertoire est absent ou vide de <c>*.ini</c>. Le dossier
    /// peut être une URI « epk://data/db » (mode pack-seul).</summary>
    public static Result? Compute(string directory)
    {
        if (string.IsNullOrEmpty(directory) ||
            !Vfs.EdenVfs.Exists(directory))
            return null;
        var root = (Vfs.EdenVfs.IsEpk(directory)
                ? directory
                : Path.GetFullPath(directory))
            .Replace('\\', '/').TrimEnd('/') + "/";
        var files = Vfs.EdenVfs.ListDir(directory, recursive: true)
            .Where(f => f.EndsWith(".ini",
                StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: f, Rel: RelOf(root, f)))
            .OrderBy(x => x.Rel, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0) return null;

        using var sha = SHA256.Create();
        foreach (var (path, rel) in files)
        {
            var pathBytes = Encoding.UTF8.GetBytes(rel);
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            sha.TransformBlock([0], 0, 1, null, 0);
            using var fs = Vfs.EdenVfs.OpenRead(path)
                           ?? throw new IOException(path);
            var fileHash = SHA256.HashData(fs);
            sha.TransformBlock(fileHash, 0, fileHash.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return new Result(sha.Hash!, files.Length);
    }

    /// <summary>Chemin relatif sous <paramref name="root"/> (séparateur
    /// '/' — loose abs ou URI epk:// indifféremment).</summary>
    private static string RelOf(string root, string path)
    {
        var p = path.Replace('\\', '/');
        return p.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? p[root.Length..] : p;
    }
}
