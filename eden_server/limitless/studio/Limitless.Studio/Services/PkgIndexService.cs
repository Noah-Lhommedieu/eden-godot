using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Buffers.Binary;

namespace Limitless.Studio.Services;

/// <summary>Index `pkg.idx` Reforged — table de records de 592 o
/// (0x250) mappant nom de fichier → (pkg#, offset, taille) :
/// <code>
/// +0x000 u32  tag (0x2080 — seed de hachage)
/// +0x004 char nom[260]   (null-terminé)
/// +0x108 char dossier[260] (ex. "UI\itemicon\")
/// +0x210 u32  numéro du pkg (pkgNNN.pkg)
/// +0x21c u32  offset dans le pkg
/// +0x224 u32  taille comprimée (zlib)
/// </code>
/// Chaque tranche pkg[offset..+size] est un asset zlib ; le flux
/// décompressé est le fichier brut (DDS/KFM/PNG…). Découvert en
/// lisant pkg.idx @0x1e2bfac (Q00134.dds → pkg269@0x270324).</summary>
public sealed class PkgIndexService
{
    public record Entry(int Pkg, int Offset, int Size, string Dir);

    private readonly Dictionary<string, Entry> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _pkgDir;
    private bool _loaded;

    public PkgIndexService(string pkgDir) => _pkgDir = pkgDir;

    public int Count => _byName.Count;

    /// <summary>Tous les noms de fichiers indexés.</summary>
    public IEnumerable<string> Names() => _byName.Keys;

    /// <summary>Résolution légère — l'entrée sans décompresser
    /// l'asset.</summary>
    public bool TryGetEntry(string name, out Entry e)
    { Load(); return _byName.TryGetValue(name, out e!); }

    /// <summary>Charge pkg.idx en mémoire (records de 0x250 o).
    /// Le fichier commence par une table de hachage (0x15C o sur le
    /// client Reforged) — le premier record n'est PAS à l'offset 0.
    /// On détecte l'alignement en cherchant le plus petit offset où
    /// 3 records consécutifs portent un nom ASCII imprimable.
    /// Le nom du fichier sert de clé (insensible à la casse).</summary>
    public void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var idx = Path.Combine(_pkgDir, "pkg.idx");
        if (!File.Exists(idx)) return;
        var d = File.ReadAllBytes(idx);
        const int rec = 0x250;
        var start = FindRecordStart(d, rec);
        if (start < 0) return;
        for (var p = start; p + rec <= d.Length; p += rec)
        {
            var name = ReadStr(d, p + 0x004, 260);
            if (name.Length == 0) continue;
            var dir = ReadStr(d, p + 0x108, 260);
            var pkg = BinaryPrimitives.ReadInt32LittleEndian(
                d.AsSpan(p + 0x210));
            var off = BinaryPrimitives.ReadInt32LittleEndian(
                d.AsSpan(p + 0x21c));
            var sz = BinaryPrimitives.ReadInt32LittleEndian(
                d.AsSpan(p + 0x224));
            if (pkg > 0 && sz > 0)
                _byName[name] = new Entry(pkg, off, sz, dir);
        }
    }

    /// <summary>Offset du premier record : le plus petit `s` tel que
    /// les champs nom à s, s+0x250, s+0x500 sont des chaînes ASCII
    /// null-terminées plausibles. `internal` — réutilisé par
    /// PkgWriter pour le patch en place.</summary>
    internal static int FindRecordStart(byte[] d, int rec)
    {
        for (var s = 0; s + 2 * rec + 8 <= d.Length && s < rec; s += 4)
        {
            if (LooksLikeName(d, s + 4) &&
                LooksLikeName(d, s + rec + 4) &&
                LooksLikeName(d, s + 2 * rec + 4))
                return s;
        }
        return -1;
    }

    /// <summary>Nom de record plausible : ASCII imprimable non vide,
    /// null-terminé dans les 260 octets du champ.</summary>
    private static bool LooksLikeName(byte[] d, int off)
    {
        var n = 0;
        for (var i = off; i < d.Length && n < 260; i++, n++)
        {
            var b = d[i];
            if (b == 0) return n > 0;
            if (b < 0x20 || b > 0x7E) return false;
        }
        return false;
    }

    private static string ReadStr(byte[] d, int off, int max)
    {
        var end = Array.IndexOf(d, (byte)0, off, max);
        if (end < 0) end = off + max;
        return Encoding.ASCII.GetString(d, off, end - off);
    }

    /// <summary>Résout un nom de fichier → l'asset brut
    /// décompressé (DDS/KFM/PNG…), ou null.</summary>
    public byte[]? ReadAsset(string fileName)
    {
        Load();
        if (!_byName.TryGetValue(fileName, out var e)) return null;
        var path = Path.Combine(_pkgDir, $"pkg{e.Pkg:D3}.pkg");
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            fs.Seek(e.Offset, SeekOrigin.Begin);
            var comp = new byte[e.Size];
            if (fs.Read(comp, 0, e.Size) != e.Size) return null;
            using var z = new ZLibStream(new MemoryStream(comp),
                CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            z.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch { return null; }
    }
}
