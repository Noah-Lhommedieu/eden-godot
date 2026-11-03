using System.Buffers.Binary;
using System.Security.Cryptography;
using EdenClient.Vfs;
using Microsoft.Win32.SafeHandles;
using ZstdSharp;

namespace EdenPack;

/// <summary>
/// Distribution des packs — diff/apply/install (étape 5).
/// Les blobs sont recopiés byte-à-byte par ranges
/// (offset/blobSize de l'index source) : aucune recompression ni
/// rechiffrement — un fichier inchangé reste bit-identique.
/// Limite : diff/apply entre packs ENCRYPTED suppose le même
/// Kdata des deux côtés (flux release normal — keyEpoch partagé) ;
/// un changement de Kdata exige un build complet.
/// </summary>
internal static class PackOps
{
    // ---------- diff ----------

    /// <summary>patch.epk = pack normal contenant uniquement les
    /// entrées dont sha256 OU cryptoRev diffèrent vs old (ou qui en
    /// sont absentes) ; blobs copiés depuis new.epk. Le patch hérite
    /// flags ENCRYPTED + bloc wrap de new ; jamais SIGNED (l'index
    /// du patch n'est pas celui signé).</summary>
    public static void Diff(string oldPath, string newPath,
        string outPath)
    {
        using var oldR = EpkReader.Open(oldPath);
        using var newR = EpkReader.Open(newPath);
        var items = new List<(EpkEntry e, string src)>();
        foreach (var p in newR.Paths)
        {
            if (!newR.TryGetEntry(p, out var e)) continue;
            if (e.Codec == EpkFormat.BlobCodec.Tombstone) continue;
            if (oldR.TryGetEntry(p, out var o)
                && o.Sha256.AsSpan().SequenceEqual(e.Sha256)
                && o.CryptoRev == e.CryptoRev)
                continue;
            items.Add((e, newPath));
        }
        // Suppressions : présentes dans old, absentes de new →
        // entrée Tombstone (sans payload) qui ombre la base.
        var dels = 0;
        foreach (var p in oldR.Paths)
        {
            if (!oldR.TryGetEntry(p, out var o)
                || o.Codec == EpkFormat.BlobCodec.Tombstone
                || newR.TryGetEntry(p, out _)) continue;
            items.Add((o with
            {
                Codec = EpkFormat.BlobCodec.Tombstone,
                BlobOffset = 0, BlobSize = 0, USize = 0,
            }, oldPath));
            dels++;
        }
        WriteRawPack(outPath, items,
            EpkFormat.PackFlags.IndexZstd |
                (newR.Flags & EpkFormat.PackFlags.Encrypted),
            newR.Flags.HasFlag(EpkFormat.PackFlags.Encrypted)
                ? newR.WrapBlock.ToArray() : null,
            newR.PackVersion, newR.PackId, newR.ChunkShift);
        Console.Out.WriteLine(
            $"diff : {items.Count - dels} changée(s) + " +
            $"{dels} suppression(s) -> {outPath}");
    }

    // ---------- apply ----------

    /// <summary>out.epk = base ∪ patch (le patch gagne chaque
    /// conflit). Blobs recopiés par ranges, index fusionné trié.
    /// Header : packVersion + packId du patch (état le plus récent).</summary>
    public static void Apply(string basePath, string patchPath,
        string outPath, byte[]? signKeyDer = null, uint? signKeyId = null)
    {
        using var baseR = EpkReader.Open(basePath);
        using var patchR = EpkReader.Open(patchPath);
        if (baseR.Flags.HasFlag(EpkFormat.PackFlags.Encrypted)
            != patchR.Flags.HasFlag(EpkFormat.PackFlags.Encrypted))
            throw new InvalidDataException(
                "apply : domaines crypto mélangés " +
                "(base chiffrée ≠ patch ou inverse)");
        var merged = new SortedDictionary<string, (EpkEntry, string)>(
            StringComparer.Ordinal);
        foreach (var p in baseR.Paths)
            if (baseR.TryGetEntry(p, out var e)
                && e.Codec != EpkFormat.BlobCodec.Tombstone)
                merged[p] = (e, basePath);
        foreach (var p in patchR.Paths)
            if (patchR.TryGetEntry(p, out var e))
            {
                if (e.Codec == EpkFormat.BlobCodec.Tombstone)
                    merged.Remove(p);       // suppression du patch
                else
                    merged[p] = (e, patchPath);   // patch gagne
            }
        WriteRawPack(outPath, merged.Values.ToList(),
            EpkFormat.PackFlags.IndexZstd |
                (patchR.Flags & EpkFormat.PackFlags.Encrypted) |
                (signKeyDer != null ? EpkFormat.PackFlags.Signed : 0),
            patchR.Flags.HasFlag(EpkFormat.PackFlags.Encrypted)
                ? patchR.WrapBlock.ToArray() : null,
            patchR.PackVersion, patchR.PackId, patchR.ChunkShift,
            signKeyDer, signKeyId);
        Console.Out.WriteLine(
            $"apply : {merged.Count} entrée(s) fusionnée(s) -> " +
            outPath);
    }

    // ---------- install ----------

    /// <summary>Installe un SET de packs de manière atomique :
    /// copie dans .tmp-&lt;ver&gt;/, vérif rapide (open header+index
    /// + sha256 fichier), .epk déplacés à la racine, puis rename
    /// atomique de current.json EN DERNIER — le pointeur est
    /// l'activation : une interruption avant ce rename laisse
    /// l'ancien current.json intact et les nouveaux .epk inertes à
    /// la racine ; après, le nouveau set est déjà complet sur disque.
    /// Purge ensuite : conserve le nouveau set ∪ le set N-1 listé
    /// par l'ancien current.json.</summary>
    public static void Install(string srcDir, string gamedataDir,
        byte[]? signKeyDer = null, uint? signKeyId = null)
    {
        var src = Path.GetFullPath(srcDir);
        var dst = Path.GetFullPath(gamedataDir);
        var packs = Directory.GetFiles(src, "*.epk")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (packs.Count == 0)
            throw new InvalidDataException(
                $"aucun *.epk dans {srcDir}");
        Directory.CreateDirectory(dst);

        // Ancien manifest lu AVANT écrasement = rétention N-1.
        var prev = EpkManifest.TryRead(
            Path.Combine(dst, "current.json"));

        // version du set = max packVersion (l'ouverture valide
        // header+index).
        var version = 0L;
        foreach (var f in packs)
        {
            using var r = EpkReader.Open(f);
            version = Math.Max(version, (long)r.PackVersion);
        }

        var tmp = Path.Combine(dst, $".tmp-{version}");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);

        var man = new EpkManifest
        {
            Version = (int)Math.Min(int.MaxValue, version),
            Generated = DateTime.UtcNow.ToString("o"),
        };
        foreach (var f in packs)
        {
            var file = Path.GetFileName(f);
            var t = Path.Combine(tmp, file);
            using (var ws = new FileStream(t, FileMode.Create,
                       FileAccess.Write, FileShare.None))
            {
                using var rs = File.OpenRead(f);
                rs.CopyTo(ws);
                ws.Flush(true);   // fsync best-effort
            }
            // Vérif rapide : digest du fichier copié tel qu'écrit +
            // header+index relus sur la copie.
            byte[] sha;
            using (var check = File.OpenRead(t))
                sha = SHA256.HashData(check);
            using (var r = EpkReader.Open(t))
            {
                if (r.Paths.Count == 0)
                    throw new InvalidDataException(
                        $"{file} : pack vide refusé");
                // Pack en clair : aucun tag GCM, le reader ne contrôle
                // pas les blobs -> sha256 de CHAQUE entrée ici, sinon
                // un octet falsifié passerait à l'installation.
                if (!r.Flags.HasFlag(EpkFormat.PackFlags.Encrypted))
                    foreach (var p in r.Paths)
                    {
                        r.TryGetEntry(p, out var e);
                        if (e.Codec == EpkFormat.BlobCodec.Tombstone)
                            continue;
                        var d = r.ReadAllBytes(p) ?? Array.Empty<byte>();
                        if (!SHA256.HashData(d).AsSpan()
                                .SequenceEqual(e.Sha256))
                            throw new InvalidDataException(
                                $"{file} : {p} sha256 incorrect, " +
                                "installation refusée");
                    }
            }
            man.Packs.Add(new EpkManifest.PackRef
            {
                Name = Path.GetFileNameWithoutExtension(file),
                File = file,
                Sha256 = EpkCrypto.ToHex(sha),
            });
        }

        // Manifest écrit dans .tmp puis fsync (sera renommé à la fin).
        // --sign-key : signature ECDSA sur la forme canonique.
        var manTmp = Path.Combine(tmp, "current.json");
        if (signKeyDer != null)
        {
            var (keyId, sig) = EpkCrypto.SignIndex(signKeyDer,
                EpkManifest.Serialize(man));
            man.KeyId = signKeyId ?? keyId;
            man.Sig = sig;
        }
        File.WriteAllBytes(manTmp, EpkManifest.SerializeSigned(man));
        using (var ms = File.Open(manTmp, FileMode.Open,
                   FileAccess.ReadWrite))
            ms.Flush(true);

        // .epk à la racine (même volume → rename par fichier,
        // overwrite). Inertes tant que le pointeur n'a pas basculé.
        foreach (var p in man.Packs)
            File.Move(Path.Combine(tmp, p.File),
                Path.Combine(dst, p.File), true);

        // ACTIVATION : rename atomique du pointeur seul.
        File.Move(manTmp, Path.Combine(dst, "current.json"), true);

        // Purge : tout .epk hors nouveau set ∪ set N-1.
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in man.Packs) keep.Add(p.File);
        if (prev != null)
            foreach (var p in prev.Packs) keep.Add(p.File);
        foreach (var f in Directory.GetFiles(dst, "*.epk"))
            if (!keep.Contains(Path.GetFileName(f)))
                File.Delete(f);
        Directory.Delete(tmp);
        Console.Out.WriteLine(
            $"install : {man.Packs.Count} pack(s), set v{man.Version} "
            + $"-> {dst} (current.json basculé)");
    }

    // ---------- écriture brute partagée ----------

    /// <summary>Écrit un pack en recopiant les blobs bruts des
    /// fichiers source aux ranges (BlobOffset, BlobSize) des
    /// entrées — offsets ré-émis séquentiels, index trié zstd.
    /// SIGNED seulement si signKeyDer est donné (apply re-signe l'index
    /// fusionné ; un patch brut n'est jamais signé).</summary>
    private static void WriteRawPack(string outPath,
        List<(EpkEntry e, string src)> items,
        EpkFormat.PackFlags flags, byte[]? wrapBlock,
        ulong packVersion, byte[] packId, int chunkShift,
        byte[]? signKeyDer = null, uint? signKeyId = null)
    {
        if (flags.HasFlag(EpkFormat.PackFlags.Encrypted)
            && wrapBlock == null)
            throw new InvalidDataException(
                "pack chiffré sans bloc wrap à recopier");
        items.Sort((a, b) => string.CompareOrdinal(a.e.Path, b.e.Path));
        var srcs = new Dictionary<string, SafeFileHandle>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(
                Path.GetFullPath(outPath))!);
            using var fs = File.Create(outPath);
            fs.Position = EpkFormat.BlobBase(flags);
            var entries = new List<EpkEntry>(items.Count);
            var buf = new byte[8 << 20];
            foreach (var (e, src) in items)
            {
                if (!srcs.TryGetValue(src, out var h))
                    srcs[src] = h = File.OpenHandle(src, FileMode.Open,
                        FileAccess.Read, FileShare.Read);
                var off = (ulong)fs.Position;
                var remaining = e.BlobSize;
                var pos = (long)e.BlobOffset;
                while (remaining > 0)
                {
                    var n = (int)Math.Min((long)buf.Length,
                        (long)remaining);
                    RandomAccess.Read(h, buf.AsSpan(0, n), pos);
                    fs.Write(buf, 0, n);
                    pos += n;
                    remaining -= (ulong)n;
                }
                entries.Add(e with { BlobOffset = off });
            }
            using var comp = new Compressor(19);
            var indexBuf = EpkIndex.Serialize(entries);
            var indexC = comp.Wrap(indexBuf);
            var indexOff = (ulong)fs.Position;
            fs.Write(indexC);

            var head = new byte[EpkFormat.HeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(head,
                EpkFormat.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4),
                EpkFormat.FormatVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8),
                (uint)flags);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12),
                (uint)chunkShift);
            BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(16),
                packVersion);
            packId.CopyTo(head, 24);
            BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(40),
                indexOff);
            BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(48),
                (ulong)indexC.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(56),
                (ulong)indexBuf.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(64),
                (uint)entries.Count);
            fs.Position = 0;
            fs.Write(head);
            if (wrapBlock != null)
            {
                fs.Position = EpkFormat.HeaderSize;
                fs.Write(wrapBlock);
            }
            if (signKeyDer != null)
            {
                // Même région signée que EpkWriter : header ‖ wrap ‖ index.
                var wrapLen = wrapBlock?.Length ?? 0;
                var region = new byte[EpkFormat.HeaderSize + wrapLen
                    + indexC.Length];
                head.CopyTo(region, 0);
                wrapBlock?.CopyTo(region, EpkFormat.HeaderSize);
                ((ReadOnlySpan<byte>)indexC).CopyTo(
                    region.AsSpan(EpkFormat.HeaderSize + wrapLen));
                var (keyId, sig) = EpkCrypto.SignIndex(signKeyDer, region);
                var sigBlock = new byte[EpkFormat.SigBlockSize];
                BinaryPrimitives.WriteUInt32LittleEndian(sigBlock,
                    signKeyId ?? keyId);
                sig.CopyTo(sigBlock, 4);
                fs.Position = EpkFormat.HeaderSize + wrapLen;
                fs.Write(sigBlock);
            }
            fs.Flush();
        }
        finally
        {
            foreach (var h in srcs.Values) h.Dispose();
        }
    }
}
