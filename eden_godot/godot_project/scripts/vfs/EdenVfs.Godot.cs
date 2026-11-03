#if GODOT
using Godot;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EdenClient.Vfs;

/// <summary>
/// Parties Godot de <see cref="EdenVfs"/> — décodage
/// <see cref="Image"/> depuis un buffer pack, et matérialisation
/// sous <c>user://cache/epk/</c> pour les API moteur qui exigent un
/// vrai chemin fichier (<c>FontFile.LoadDynamicFont</c>, le
/// <c>basePath</c> des GLB à textures externes — le corpus n'est
/// PAS embarqué : images en URI « ../../../textures/… »).
/// Exclu de NetTests via #if GODOT (pas de GodotSharp).
/// </summary>
public static partial class EdenVfs
{
    /// <summary>Image par chemin lâche ou URI pack — loose →
    /// <see cref="Image.LoadFromFile"/> direct (évite la copie) ;
    /// pack → ReadAllBytes puis decode selon l'extension.</summary>
    public static Image? LoadImage(string path)
    {
        if (!IsEpk(path) && LooseReadable(path) && File.Exists(path))
            return Image.LoadFromFile(path);
        var bytes = ReadAllBytes(path);
        if (bytes == null) return null;
        var img = new Image();
        var err = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => img.LoadPngFromBuffer(bytes),
            ".webp" => img.LoadWebpFromBuffer(bytes),
            ".jpg" or ".jpeg" => img.LoadJpgFromBuffer(bytes),
            ".bmp" => img.LoadBmpFromBuffer(bytes),
            ".tga" => img.LoadTgaFromBuffer(bytes),
            ".dds" => img.LoadDdsFromBuffer(bytes),
            _ => Error.FileUnrecognized,
        };
        return err == Error.Ok && !img.IsEmpty() ? img : null;
    }

    /// <summary>Chemin lâche correspondant à une clé pack
    /// <paramref name="rel"/> (« assets/… » sous AssetsRoot, autre
    /// sous une racine candidate) — null si absent du disque.</summary>
    private static string? LoosePathFor(string rel)
    {
        if (!LooseAllowed) return null;
        var local = rel.Replace('/', Path.DirectorySeparatorChar);
        if (rel.StartsWith("assets/", StringComparison.Ordinal))
        {
            var p = Path.Combine(AssetCatalog.AssetsRoot, local[7..]);
            if (File.Exists(p) || Directory.Exists(p)) return p;
        }
        foreach (var root in DataRoots())
        {
            var p = Path.Combine(root, local);
            if (File.Exists(p) || Directory.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Cible cache d'une clé pack matérialisée.</summary>
    private static string CachePath(string rel) =>
        Path.Combine(
            ProjectSettings.GlobalizePath("user://cache/epk"),
            rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Rend un contenu accessible en vrai chemin fichier :
    /// non-pack → inchangé ; epk:// → extraction sous
    /// user://cache/epk/&lt;rel&gt; (écriture atomique tmp+rename).
    /// Pour les API moteur à chemin uniquement — le reste passe par
    /// les buffers/streams.</summary>
    public static string? Materialize(string path)
    {
        if (!IsEpk(path)) return path;
        var rel = NormalizeRel(EpkRel(path));
        return ExtractTo(rel, CachePath(rel));
    }

    /// <summary>Extrait l'entrée pack <paramref name="rel"/> vers
    /// <paramref name="dst"/> (atomique tmp+rename) — dst libre pour
    /// les URI GLB dont la casse d'origine doit être préservée.</summary>
    private static string? ExtractTo(string rel, string dst)
    {
        if (File.Exists(dst)) return dst;
        var bytes = PackBytes(rel);
        if (bytes == null) return null;
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(dst)!);
                var tmp = dst + "." +
                    Guid.NewGuid().ToString("N")[..8] + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, dst, true);
            }
            catch (Exception)
            {
                return File.Exists(dst) ? dst : null;
            }
        }
        return dst;
    }

    /// <summary>Résout les segments « . »/« .. » d'une clé
    /// relative (URI d'images GLB « ../../../textures/… »).</summary>
    public static string NormalizeRel(string rel)
    {
        var segs = rel.Replace('\\', '/').Split('/');
        var stack = new List<string>(segs.Length);
        foreach (var s in segs)
        {
            if (s.Length == 0 || s == ".") continue;
            if (s == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(s);
        }
        return string.Join('/', stack);
    }
}
#endif
