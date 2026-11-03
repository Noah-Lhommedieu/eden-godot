using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EdenClient.Vfs;
using ZstdSharp;

namespace EdenPack;

/// <summary>
/// Écriture d'un pack EPK v1 — spec figée : docs/packaging/EPK-FORMAT.md.
/// Sérialisation partagée avec le client via les sources liées
/// (<see cref="EpkIndex"/>), zéro réimplémentation du format.
/// </summary>
internal static class EpkWriter
{
    /// <summary>Options crypto du build — null = pack en clair non
    /// signé (comportement historique, bit-à-bit identique).</summary>
    internal sealed class CryptoSpec
    {
        /// <summary>ENCRYPTED : les trois requis (keyfile A/B/kdata).</summary>
        public byte[]? PartA, PartB, Kdata;
        /// <summary>SIGNED : clé privée ECDSA P-256 DER.</summary>
        public byte[]? SignKeyDer;
        /// <summary>keyId forcé (sinon dérivé de la clé publique).</summary>
        public uint? SignKeyId { get; set; }
    }

    /// <summary>Mode natif (ADR-0005) : liste de fichiers imposée par
    /// l'appelant, politique de codec par fichier et contrôle de
    /// provenance. Null = comportement historique inchangé.</summary>
    internal sealed class NativeHooks
    {
        /// <summary>(chemin disque, chemin EPK) — remplace le scan.</summary>
        public List<(string abs, string rel)> Files = new();
        /// <summary>true = codec raw forcé (pas d'essai zstd).</summary>
        public Func<string, byte[], bool>? ForceRaw;
        /// <summary>Appelé pour chaque fichier avec son contenu et son
        /// sha256 ; lève pour faire échouer le build.</summary>
        public Action<string, string, byte[], byte[]>? OnFile;
    }

    /// <summary>Fichiers parasites OS exclus par défaut (à tout niveau).</summary>
    private static readonly string[] DefaultExcludes =
        { "**/desktop.ini", "**/thumbs.db", "**/.ds_store" };

    public static void Build(string srcDir, string outPath, int level,
        ulong packVersion, Guid? packId, List<string> userExcludes,
        string? prevPath, List<string>? includes = null,
        CryptoSpec? crypto = null, NativeHooks? native = null)
    {
        var root = Path.GetFullPath(srcDir);
        if (native == null && !Directory.Exists(root))
            throw new DirectoryNotFoundException(
                $"srcdir introuvable : {srcDir}");

        var patterns = DefaultExcludes
            .Concat(userExcludes.Select(EpkFormat.NormalizePath))
            .ToList();
        var incPatterns = (includes ?? new List<string>())
            .Select(EpkFormat.NormalizePath).ToList();

        // Exclusion à dernière-règle-gagnante (sémantique gitignore) :
        // un motif « !glob » ré-inclut (ex : exclure tous les .glb
        // fat sauf les .slim.glb). Les DefaultExcludes passent en
        // premier → négables par l'utilisateur aussi.
        // Les motifs sans joker (listes générées type tex-baked —
        // 30k+ chemins exacts) vont en lookup O(1) indexé par leur
        // rang ; les globs restent en boucle. Rang le plus grand
        // qui matche décide.
        var exact = new Dictionary<string, (int idx, bool neg)>();
        var globs = new List<(int idx, bool neg, string pat)>();
        for (var i = 0; i < patterns.Count; i++)
        {
            var p = patterns[i];
            var neg = p.StartsWith('!');
            var pat = neg ? p[1..] : p;
            if (pat.IndexOfAny(new[] { '*', '?' }) < 0)
                exact[pat] = (i, neg);
            else
                globs.Add((i, neg, pat));
        }
        bool Excluded(string rel)
        {
            var best = -1; var excl = false;
            if (exact.TryGetValue(rel, out var x))
            { best = x.idx; excl = !x.neg; }
            foreach (var (idx, neg, pat) in globs)
                if (idx > best && GlobMatch(pat, rel))
                { best = idx; excl = !neg; }
            return excl;
        }

        // Scan récursif → chemins relatifs normalisés. ReparsePoint
        // (symlinks) ignorés : un pack ne contient que des fichiers
        // réels — sinon gamedata/assets (symlink dev → 27 Go) serait
        // traversé et dupliqué dans chaque pack.
        // --include : seules les entrées matchées entrent.
        var files = new List<(string abs, string rel)>();
        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        if (native != null)
            files.AddRange(native.Files);
        else
        foreach (var f in Directory.EnumerateFiles(root, "*", enumOpts))
        {
            var rel = EpkFormat.NormalizePath(
                Path.GetRelativePath(root, f));
            var wanted = (incPatterns.Count == 0 ||
                          incPatterns.Any(p => GlobMatch(p, rel)))
                         && !Excluded(rel);
            if (wanted)
                files.Add((f, rel));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.rel, b.rel));
        for (var i = 1; i < files.Count; i++)
            if (files[i].rel == files[i - 1].rel)
                throw new InvalidDataException(
                    $"collision de chemin normalisé : {files[i].rel} " +
                    $"({files[i - 1].abs} vs {files[i].abs})");

        // cryptoRevs de la release précédente (--prev).
        Dictionary<ulong, (byte[] sha, uint rev)>? prev = null;
        if (prevPath != null)
        {
            using var old = EpkReader.Open(prevPath);
            prev = new Dictionary<ulong, (byte[], uint)>(old.Paths.Count);
            foreach (var p in old.Paths)
                if (old.TryGetEntry(p, out var e))
                    prev[e.FileId] = (e.Sha256, e.CryptoRev);
        }

        // Flags + crypto : le packId (salt HKDF du Kwrap) doit être
        // fixé AVANT le wrap — résolu ici, réécrit dans le header.
        var packIdBytes = (packId ?? Guid.NewGuid()).ToByteArray();
        var flags = EpkFormat.PackFlags.IndexZstd;
        EpkCrypto.EpkEncryptor? encryptor = null;
        byte[]? wrapBlock = null;
        if (crypto?.Kdata != null)
        {
            if (crypto.PartA == null || crypto.PartB == null)
                throw new InvalidDataException(
                    "--encrypt : parts A= et B= requises dans le " +
                    "keyfile");
            flags |= EpkFormat.PackFlags.Encrypted;
            wrapBlock = EpkCrypto.WrapKdata(crypto.Kdata,
                crypto.PartA, crypto.PartB, packIdBytes);
            encryptor = EpkCrypto.MakeEncryptor(crypto.Kdata);
        }
        if (crypto?.SignKeyDer != null)
            flags |= EpkFormat.PackFlags.Signed;
        var chunkSize = 1 << EpkFormat.DefaultChunkShift;
        var entries = new List<EpkEntry>(files.Count);
        var report = new Report();

        Directory.CreateDirectory(
            Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        // Écriture via tmp + rename atomique : un build interrompu
        // ne doit jamais laisser un .epk tronqué en place (un VFS le
        // monterait sans savoir qu'il est partiel).
        var tmpPath = outPath + ".building";
        using var fs = File.Create(tmpPath);
        fs.Position = EpkFormat.BlobBase(flags);
        using var comp = new Compressor(level);
        var head8 = new byte[EpkFormat.ChunkHeaderSize];
        var done = 0;

        foreach (var (abs, rel) in files)
        {
            var data = File.ReadAllBytes(abs);
            var sha = SHA256.HashData(data);
            native?.OnFile?.Invoke(rel, abs, data, sha);
            var forceRaw = native?.ForceRaw?.Invoke(rel, data) ?? false;
            var fileId = EpkFormat.Fnv1a64(rel);

            var rev = 0u;
            if (prev != null && prev.TryGetValue(fileId, out var o))
            {
                if (o.sha.AsSpan().SequenceEqual(sha))
                {
                    rev = o.rev;
                }
                else
                {
                    if (o.rev == uint.MaxValue)
                        throw new InvalidDataException(
                            $"{rel} : cryptoRev épuisé, contenu modifié " +
                            "impossible sans keyEpoch");
                    rev = o.rev + 1;
                }
            }

            var blobOff = (ulong)fs.Position;
            long blobSize = 0;
            var codec = EpkFormat.BlobCodec.Raw;
            if (data.Length > 0)
            {
                // Chaque chunk = frame zstd indépendante (random access).
                var nChunks = (data.Length - 1) / chunkSize + 1;
                var frames = new (byte[] buf, int len)[nChunks];
                long compTotal = 0;
                for (var c = 0; c < nChunks && !forceRaw; c++)
                {
                    var off = c * chunkSize;
                    var n = Math.Min(chunkSize, data.Length - off);
                    var buf = new byte[Compressor.GetCompressBound(n)];
                    var len = comp.Wrap(data.AsSpan(off, n), buf);
                    frames[c] = (buf, len);
                    compTotal += len;
                }
                // Spec : raw si zstd ne gagne pas (>2 % d'expansion).
                if (!forceRaw && compTotal <= data.Length * 1.02)
                    codec = EpkFormat.BlobCodec.Zstd;
                // Entrée partielle pour l'encryptor (seuls FileId/
                // CryptoRev entrent dans la dérivation Kfile).
                var encEntry = new EpkEntry(rel, fileId, blobOff, 0,
                    (ulong)data.Length, rev, codec,
                    EpkFormat.TexCodec.Raw, sha);
                for (var c = 0; c < nChunks; c++)
                {
                    var off = c * chunkSize;
                    var uSize = Math.Min(chunkSize, data.Length - off);
                    ReadOnlySpan<byte> payload =
                        codec == EpkFormat.BlobCodec.Zstd
                            ? frames[c].buf.AsSpan(0, frames[c].len)
                            : data.AsSpan(off, uSize);
                    // ENCRYPTED : payload stocké = cipher‖tag16
                    // (cSize inclut le tag) ; sinon span brut.
                    var stored = encryptor != null
                        ? encryptor(in encEntry, c, uSize, payload)
                        : null;
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        head8, (uint)(stored?.Length
                            ?? payload.Length));
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        head8.AsSpan(4), (uint)uSize);
                    fs.Write(head8);
                    if (stored != null) fs.Write(stored);
                    else fs.Write(payload);
                    blobSize += EpkFormat.ChunkHeaderSize
                        + (stored?.Length ?? payload.Length);
                }
            }

            entries.Add(new EpkEntry(
                Path: rel, FileId: fileId, BlobOffset: blobOff,
                BlobSize: (ulong)blobSize, USize: (ulong)data.Length,
                CryptoRev: rev, Codec: codec,
                TexCodec: EpkFormat.TexCodec.Raw, Sha256: sha));
            report.Add(rel, data.Length, blobSize, native != null);

            if (++done % 2000 == 0)
                Console.Error.WriteLine(
                    $"edenpack: {done}/{files.Count} fichiers…");
        }

        // Invariant spec : jamais deux contenus sous (fileId, cryptoRev)
        // — même rev que l'ancien index avec sha différent = refus.
        if (prev != null)
            foreach (var e in entries)
                if (prev.TryGetValue(e.FileId, out var o)
                    && o.rev == e.CryptoRev
                    && !o.sha.AsSpan().SequenceEqual(e.Sha256))
                    throw new InvalidDataException(
                        $"invariant cryptoRev violé sur {e.Path} : rev " +
                        $"{e.CryptoRev} déjà publié avec un autre sha256");

        // Index : trié par chemin, sérialisé par le noyau partagé,
        // compressé zstd (INDEX_ZSTD), en fin de fichier.
        var indexBuf = EpkIndex.Serialize(entries);
        var indexC = comp.Wrap(indexBuf);
        var indexOff = (ulong)fs.Position;
        fs.Write(indexC);

        // Header 80o — le reader lit indexUSize en u64 @56 (les offsets
        // 52/56/60/64 de la spec sont décalés ; entryCount @64, min @68,
        // réservé 72..79 = 8o).
        var head = new byte[EpkFormat.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(head, EpkFormat.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4),
            EpkFormat.FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8),
            (uint)flags);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12),
            EpkFormat.DefaultChunkShift);
        BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(16),
            packVersion);
        packIdBytes.CopyTo(head, 24);
        BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(40),
            indexOff);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(48),
            (uint)indexC.Length);          // indexCSize u64 @48
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(56),
            (uint)indexBuf.Length);        // indexUSize u64 @56
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(64),
            (uint)entries.Count);          // entryCount
        // +68 minClientVersion = 0, +72 réservé = zéros.

        // Signature = sur EXACTEMENT les octets de SignedRegion du
        // reader : header ‖ wrap ‖ index tel qu'écrit (compressé).
        byte[]? sigBlock = null;
        if (crypto?.SignKeyDer != null)
        {
            var wrapLen = wrapBlock?.Length ?? 0;
            var region = new byte[EpkFormat.HeaderSize + wrapLen
                + indexC.Length];
            head.CopyTo(region, 0);
            if (wrapBlock != null)
                wrapBlock.CopyTo(region, EpkFormat.HeaderSize);
            ((ReadOnlySpan<byte>)indexC).CopyTo(
                region.AsSpan(EpkFormat.HeaderSize + wrapLen));
            var (keyId, sig) = EpkCrypto.SignIndex(
                crypto.SignKeyDer, region);
            if (crypto.SignKeyId.HasValue) keyId = crypto.SignKeyId.Value;
            sigBlock = new byte[EpkFormat.SigBlockSize];
            BinaryPrimitives.WriteUInt32LittleEndian(sigBlock, keyId);
            sig.CopyTo(sigBlock, 4);
        }

        fs.Position = 0;
        fs.Write(head);
        if (wrapBlock != null)
        {
            fs.Position = EpkFormat.HeaderSize;
            fs.Write(wrapBlock);
        }
        if (sigBlock != null)
        {
            fs.Position = EpkFormat.HeaderSize
                + (wrapBlock != null ? EpkFormat.WrapBlockSize : 0);
            fs.Write(sigBlock);
        }
        fs.Flush();
        fs.Dispose();
        File.Move(tmpPath, outPath, true);

        report.Summary(files.Count, new FileInfo(outPath).Length,
            indexBuf.Length, indexC.Length);
        var text = report.Render(Path.GetFileName(outPath));
        Console.Out.Write(text);
        File.WriteAllText(outPath + ".report.txt", text);
    }

    /// <summary>Glob minimal : '*' dans un segment (ne traverse pas
    /// '/'), '**' traverse des segments entiers (0..n). Pattern et
    /// chemin déjà normalisés (minuscules, '/').</summary>
    private static bool GlobMatch(string pattern, string path) =>
        MatchSeg(pattern.Split('/'), 0, path.Split('/'), 0);

    private static bool MatchSeg(string[] p, int pi, string[] s, int si)
    {
        if (pi == p.Length) return si == s.Length;
        if (p[pi] == "**")
        {
            for (var k = si; k <= s.Length; k++)
                if (MatchSeg(p, pi + 1, s, k))
                    return true;
            return false;
        }
        return si < s.Length && MatchOne(p[pi], s[si])
            && MatchSeg(p, pi + 1, s, si + 1);
    }

    /// <summary>Wildcard '*' seul dans un segment (itératif, backtrack
    /// sur la dernière étoile).</summary>
    private static bool MatchOne(string pat, string str)
    {
        int pi = 0, si = 0, star = -1, ss = 0;
        while (si < str.Length)
        {
            if (pi < pat.Length && pat[pi] == str[si]) { pi++; si++; }
            else if (pi < pat.Length && pat[pi] == '*')
            { star = pi++; ss = si; }
            else if (star >= 0) { pi = star + 1; si = ++ss; }
            else return false;
        }
        while (pi < pat.Length && pat[pi] == '*') pi++;
        return pi == pat.Length;
    }

    /// <summary>Rapport de poids officiel : par dossier de 1er niveau
    /// (uidata/data/map/assets/autres) puis par extension dans
    /// assets/.</summary>
    private sealed class Report
    {
        private sealed class Acc
        {
            public int Count; public long Src; public long Stored;
        }

        private static readonly string[] TopDirs =
            { "uidata", "data", "map", "assets", "autres" };
        private readonly Dictionary<string, Acc> _dirs = new();
        private readonly Dictionary<string, Acc> _assetExt = new();
        private string _summary = "";

        private Acc Get(Dictionary<string, Acc> d, string k) =>
            d.TryGetValue(k, out var a) ? a : d[k] = new Acc();

        public void Add(string rel, long src, long stored,
            bool byTop = false)
        {
            var first = rel.Split('/')[0];
            var dir = byTop || Array.IndexOf(TopDirs, first) >= 0
                ? first : "autres";
            var a = Get(_dirs, dir);
            a.Count++; a.Src += src; a.Stored += stored;
            if (dir == "assets")
            {
                var e = Get(_assetExt, Path.GetExtension(rel));
                e.Count++; e.Src += src; e.Stored += stored;
            }
        }

        public void Summary(int count, long epkSize, int indexU,
            int indexC) =>
            _summary =
                $"entrées : {count}   epk : {Mio(epkSize)}   " +
                $"index : {Mio(indexU)} -> {Mio(indexC)} (zstd)";

        private static string Mio(long b) =>
            $"{b / 1048576.0:F1} Mio";

        private static string Row(string name, Acc a) =>
            $"  {name,-10} {a.Count,8} fichiers   {Mio(a.Src),11} -> " +
            $"{Mio(a.Stored),11}   " +
            $"{(a.Src > 0 ? 100.0 * a.Stored / a.Src : 0),5:F1} %";

        public string Render(string outName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"edenpack build — {outName}");
            sb.AppendLine(_summary);
            sb.AppendLine("par dossier de 1er niveau :");
            foreach (var d in TopDirs.Concat(_dirs.Keys
                         .Except(TopDirs).OrderBy(x => x,
                             StringComparer.Ordinal)))
            {
                if (!_dirs.TryGetValue(d, out var a)) continue;
                sb.AppendLine(Row(d + '/', a));
                if (d != "assets") continue;
                foreach (var kv in _assetExt
                             .OrderByDescending(x => x.Value.Stored))
                    sb.AppendLine(Row("  " +
                        (kv.Key.Length > 0 ? kv.Key : "(sans ext)"),
                        kv.Value));
            }
            return sb.ToString();
        }
    }
}
