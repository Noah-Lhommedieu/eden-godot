using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace Limitless.Studio.Services;

/// <summary>Résolution icône → bitmap — priorité aux fichiers
/// extraits en clair sous `reforged/EdenReforged/UI/` (itemicon,
/// skillicon, uiicon), repli sur `pkg.idx`/pkgNNN. Les codes à
/// variante (`M312040`) sont dégradés en base (`M312`) en stripant
/// les chiffres de fin — même stratégie que ModelService.
/// Bitmaps mises en cache en mémoire.</summary>
public sealed class IconService
{
    private static readonly string[] Dirs =
        { "itemicon", "skillicon", "uiicon" };
    private static readonly string[] Exts =
        { ".dds", ".png", ".tga" };

    private readonly string _uiDir;
    private readonly PkgIndexService _pkg;
    private readonly Dictionary<string, Bitmap?> _cache = new();
    private readonly Dictionary<string, Dictionary<string, string>> _dirIndex = new();

    public IconService(string uiDir, string pkgDir)
    {
        _uiDir = uiDir;
        _pkg = new PkgIndexService(pkgDir);
    }

    /// <summary>La bitmap de l'icône `code`, ou null si introuvable
    /// / non décodable (DXT compressé). Résultat mis en cache.</summary>
    public Bitmap? Get(string code)
    {
        if (_cache.TryGetValue(code, out var b)) return b;
        var bmp = Load(code);
        _cache[code] = bmp;
        return bmp;
    }

    /// <summary>Résout `code` vers le fichier clair `UI/<dir>/`
    /// trouvé — retourne le dossier d'icônes ("itemicon"…) et le nom
    /// de fichier réel (la variante qui a matché). False si aucun
    /// fichier clair ne correspond (l'icône peut quand même exister
    /// dans un pkg — voir <see cref="TryLocate"/>).</summary>
    public bool TryResolve(string code, out string dirName,
        out string fileName)
    {
        foreach (var c in Variants(code))
            foreach (var d in Dirs)
            {
                var idx = DirIndex(d);
                foreach (var ext in Exts)
                    if (idx.TryGetValue(c + ext, out _))
                    {
                        dirName = d;
                        fileName = c + ext;
                        return true;
                    }
            }
        dirName = "";
        fileName = "";
        return false;
    }

    /// <summary>Variantes d'un code icône — exposé pour
    /// l'index entité→icône (même strip des chiffres de fin).</summary>
    public static IEnumerable<string> CodeVariants(string code) =>
        Variants(code);

    /// <summary>La bitmap est-elle absente parce que l'asset est
    /// introuvable (false) ou présent mais non décodable (true,
    /// `foundName` = le fichier trouvé) ?</summary>
    public bool TryLocate(string code, out string foundName)
    {
        var path = FindLoose(code);
        if (path != null)
        { foundName = Path.GetFileName(path); return true; }
        foreach (var c in Variants(code))
            foreach (var ext in Exts)
                if (_pkg.TryGetEntry(c + ext, out _))
                { foundName = c + ext; return true; }
        foundName = "";
        return false;
    }

    public int PkgCount { get { _pkg.Load(); return _pkg.Count; } }

    private Bitmap? Load(string code)
    {
        var path = FindLoose(code);
        if (path != null)
        {
            try { return ImageCodec.Decode(File.ReadAllBytes(path)); }
            catch { return null; }
        }
        foreach (var c in Variants(code))
        {
            foreach (var ext in Exts)
            {
                var asset = _pkg.ReadAsset(c + ext);
                if (asset == null) continue;
                var bmp = ImageCodec.Decode(asset);
                if (bmp != null) return bmp;
            }
        }
        return null;
    }

    /// <summary>Fichier clair `UI/<dir>/<variante>.<ext>` — index
    /// insensible à la casse construit par dossier.</summary>
    private string? FindLoose(string code)
    {
        foreach (var c in Variants(code))
            foreach (var dir in Dirs)
            {
                var idx = DirIndex(dir);
                foreach (var ext in Exts)
                    if (idx.TryGetValue(c + ext, out var p)) return p;
            }
        return null;
    }

    /// <summary>`code` puis variantes obtenues en retirant les
    /// chiffres de fin (4 max), ex. M312040 → M3120 → M312.</summary>
    private static IEnumerable<string> Variants(string code)
    {
        var c = code;
        for (var cut = 0; cut <= 4 && c.Length > 1; cut++)
        {
            yield return c;
            if (!char.IsDigit(c[^1])) yield break;
            c = c[..^1];
        }
    }

    private Dictionary<string, string> DirIndex(string dir)
    {
        if (_dirIndex.TryGetValue(dir, out var idx)) return idx;
        idx = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var full = Path.Combine(_uiDir, dir);
        if (Directory.Exists(full))
            foreach (var f in Directory.GetFiles(full))
                idx[Path.GetFileName(f)] = f;
        _dirIndex[dir] = idx;
        return idx;
    }
}
