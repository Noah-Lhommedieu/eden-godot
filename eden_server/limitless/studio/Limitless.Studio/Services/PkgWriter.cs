using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Limitless.Studio.Services;

/// <summary>Édition d'assets dans les .pkg — « casser/remplacer »
/// les .pkg sans tout casser : on écrit l'asset modifié à la FIN
/// d'un pkg existant (ou d'un pkg « patch » dédié, ex. pkg999.pkg)
/// puis on met à jour le record pkg.idx <b>en place</b> (le record
/// garde sa taille de 0x250 — pkg#/offset/size changent, le reste
/// est préservé). Plus sûr qu'ajouter un doublon : pas de
/// dépendance aux sémantiques first/last-match du loader.</summary>
public sealed class PkgWriter
{
    private readonly string _pkgDir;
    private readonly PkgIndexService _idx;
    private readonly int _patchPkg;

    /// <summary>patchPkg = numéro du pkg cible (défaut 999 — créé
    /// si absent, hors de la plage vanilla pkg001-pkg315).</summary>
    public PkgWriter(string pkgDir, int patchPkg = 999)
    {
        _pkgDir = pkgDir;
        _patchPkg = patchPkg;
        _idx = new PkgIndexService(pkgDir);
    }

    /// <summary>Remplace l'asset `fileName` par `data` (brut, non
    /// compressé — la compression zlib est faite ici). Retourne le
    /// pkg et l'offset d'écriture.</summary>
    public (int Pkg, long Offset)? Replace(string fileName, byte[] data)
    {
        _idx.Load();
        if (!_idx.TryGetEntry(fileName, out var e)) return null;

        // 1. Compresser l'asset (zlib — même format que vanilla).
        var comp = Compress(data);

        // 2. Écrire en fin du pkg patch (créé si absent).
        var pkgPath = Path.Combine(_pkgDir, $"pkg{_patchPkg:D3}.pkg");
        long offset;
        using (var fs = new FileStream(pkgPath,
            FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            offset = fs.Length;
            fs.Seek(0, SeekOrigin.End);
            fs.Write(comp, 0, comp.Length);
        }

        // 3. Mettre à jour le record pkg.idx EN PLACE (le record
        // de `fileName` pointe désormais sur pkg999@offset).
        var idxPath = Path.Combine(_pkgDir, "pkg.idx");
        var recPos = FindRecordPos(idxPath, fileName);
        if (recPos < 0) return null;
        using (var fs = new FileStream(idxPath,
            FileMode.Open, FileAccess.Write))
        {
            Span<byte> buf = stackalloc byte[4];
            fs.Seek(recPos + 0x210, SeekOrigin.Begin);
            BinaryPrimitives.WriteInt32LittleEndian(buf, _patchPkg);
            fs.Write(buf);
            fs.Seek(recPos + 0x21c, SeekOrigin.Begin);
            BinaryPrimitives.WriteInt32LittleEndian(buf, (int)offset);
            fs.Write(buf);
            fs.Seek(recPos + 0x224, SeekOrigin.Begin);
            BinaryPrimitives.WriteInt32LittleEndian(buf, comp.Length);
            fs.Write(buf);
        }
        return (_patchPkg, offset);
    }

    /// <summary>Retrouve la position du record de `fileName` dans
    /// pkg.idx (scan linéaire par pas de 0x250).</summary>
    private static long FindRecordPos(string idxPath, string fileName)
    {
        var d = File.ReadAllBytes(idxPath);
        var name = System.Text.Encoding.ASCII.GetBytes(fileName);
        const int rec = 0x250;
        var start = PkgIndexService.FindRecordStart(d, rec);
        if (start < 0) return -1;
        for (var p = start; p + rec <= d.Length; p += rec)
        {
            var f = d.AsSpan(p + 4, name.Length);
            if (f.SequenceEqual(name) &&
                (p + 4 + name.Length >= d.Length ||
                 d[p + 4 + name.Length] == 0))
                return p;
        }
        return -1;
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        // zlib (78 9c) comme vanilla — ZLibStream écrit le wrapper.
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal,
            leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
