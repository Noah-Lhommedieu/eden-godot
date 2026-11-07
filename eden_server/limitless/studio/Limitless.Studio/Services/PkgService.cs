using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;

namespace Limitless.Studio.Services;

/// <summary>Lecteur d'assets Reforged .pkg — chaque `pkgNNN.pkg` est
/// un fichier unique compressé zlib (magic 0x789c) contenant
/// directement un asset (DDS/TGA/PNG/modèle). Le NNN du nom de
/// fichier est l'id d'asset.</summary>
public static class PkgService
{
    /// <summary>Formats reconnus par leur magic.</summary>
    public enum AssetFormat { Unknown, Dds, Tga, Png, Binary }

    /// <summary>Décompresse un .pkg → octets bruts de l'asset.</summary>
    public static byte[]? Read(string path)
    {
        try
        {
            var raw = File.ReadAllBytes(path);
            using var ms = new MemoryStream(raw);
            // zlib : 2 octets d'en-tête (78 9c) puis deflate.
            using var z = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            z.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Détecte le format de l'asset par son magic.</summary>
    public static AssetFormat Detect(byte[] d)
    {
        if (d.Length >= 4 && d[0] == 'D' && d[1] == 'D' &&
            d[2] == 'S' && d[3] == ' ') return AssetFormat.Dds;
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 'P' &&
            d[2] == 'N' && d[3] == 'G') return AssetFormat.Png;
        if (d.Length >= 2 && d[0] == 0 && d[1] == 0) return AssetFormat.Tga;
        return AssetFormat.Binary;
    }

    /// <summary>Liste les .pkg d'un dossier → (id, chemin).</summary>
    public static IEnumerable<(int Id, string Path)> Enumerate(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "pkg*.pkg"))
        {
            var n = Path.GetFileNameWithoutExtension(f);
            var idStr = new string(n.SkipWhile(
                c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(idStr, out var id))
                yield return (id, f);
        }
    }
}
