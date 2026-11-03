using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using ZstdSharp;

namespace EdenClient.Vfs;

/// <summary>
/// Lecteur EPK v1 — ouvre un pack, charge son index, sert les
/// entrées en lecture positionnelle thread-safe
/// (<see cref="System.IO.RandomAccess"/> sur UN handle unique).
/// Pur .NET — aucune dépendance Godot : compilé aussi par
/// tools/edenpack (verify/extract) et NetTests.
///
/// Crypto : le pack chiffré arrive en étape 5 — la couture est
/// <see cref="EpkDecryptor"/> (null = pack en clair, chemin v1).
/// </summary>
public sealed class EpkReader : IDisposable
{
    /// <summary>Décrypte le payload d'un chunk. Signature :
    /// (entry, chunkIdx, uSize, payloadChiffré, tagGcm) → clair
    /// (uSize = taille claire déclarée du chunk, entrée dans l'AAD).
    /// Injecté par la couche crypto (<see cref="EpkCrypto"/>) ; null
    /// tant que le pack n'est pas ENCRYPTED.</summary>
    public delegate byte[] EpkDecryptor(
        in EpkEntry entry, int chunkIdx, int uSize,
        ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag);

    private readonly SafeFileHandle _fh;
    private readonly Dictionary<string, EpkEntry> _entries;
    private readonly List<string> _paths;

    public EpkFormat.PackFlags Flags { get; }
    public ulong PackVersion { get; }
    public byte[] PackId { get; }
    public int ChunkShift { get; }
    public EpkDecryptor? Decryptor { get; set; }

    /// <summary>Zone couverte par la signature : header + crypto
    /// bloc si présent + index tel qu'écrit — le bloc signature
    /// lui-même n'en fait PAS partie.</summary>
    public ReadOnlyMemory<byte> SignedRegion { get; }
    /// <summary>Index brut tel qu'écrit (compressé si INDEX_ZSTD) —
    /// exposition pour verify/tamper tests.</summary>
    public ReadOnlyMemory<byte> RawIndex { get; }
    /// <summary>Bloc wrap 60o (ENCRYPTED) — pour UnwrapKdata.</summary>
    public ReadOnlyMemory<byte> WrapBlock { get; private set; }
    /// <summary>Signature ECDSA 64o du bloc signature (SIGNED).</summary>
    public byte[]? Signature { get; private set; }
    /// <summary>keyId du bloc signature (SIGNED).</summary>
    public uint KeyId { get; private set; }

    private EpkReader(SafeFileHandle fh,
        Dictionary<string, EpkEntry> entries, List<string> paths,
        EpkFormat.PackFlags flags, ulong packVersion, byte[] packId,
        int chunkShift, byte[] signedRegion, byte[] rawIndex)
    {
        _fh = fh;
        _entries = entries;
        _paths = paths;
        Flags = flags;
        PackVersion = packVersion;
        PackId = packId;
        ChunkShift = chunkShift;
        SignedRegion = signedRegion;
        RawIndex = rawIndex;
    }

    /// <summary>Ouvre un pack : lit le header, charge l'index
    /// (zstd si flag), construit la table de lookup. La vérification
    /// de signature n'est PAS faite ici — elle relève de la couche
    /// appelante (EdenVfs au mount / edenpack verify).</summary>
    public static EpkReader Open(string path)
    {
        var fh = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.Read);
        try
        {
            var head = new byte[EpkFormat.HeaderSize];
            RandomAccess.Read(fh, head, 0);
            if (BinaryPrimitives.ReadUInt32LittleEndian(head)
                != EpkFormat.Magic)
                throw new InvalidDataException(
                    $"{path} : pas un EPK (magic)");
            if (BinaryPrimitives.ReadUInt32LittleEndian(head[4..])
                != EpkFormat.FormatVersion)
                throw new InvalidDataException(
                    $"{path} : formatVersion inconnue");
            var flags = (EpkFormat.PackFlags)BinaryPrimitives
                .ReadUInt32LittleEndian(head[8..]);
            var chunkShift = BinaryPrimitives
                .ReadInt32LittleEndian(head[12..]);
            var packVersion = BinaryPrimitives
                .ReadUInt64LittleEndian(head[16..]);
            var packId = head.AsSpan(24, 16).ToArray();
            var indexOff = (long)BinaryPrimitives
                .ReadUInt64LittleEndian(head[40..]);
            var indexCSize = BinaryPrimitives
                .ReadUInt64LittleEndian(head[48..]);
            var indexUSize = BinaryPrimitives
                .ReadUInt64LittleEndian(head[56..]);

            // Région signée = header + bloc wrap éventuel + index.
            // Le bloc signature (entre wrap et blobs) n'y figure pas.
            var wrapLen = flags.HasFlag(EpkFormat.PackFlags.Encrypted)
                ? EpkFormat.WrapBlockSize : 0;
            var signedRegion = new byte[EpkFormat.HeaderSize + wrapLen
                + (int)indexCSize];
            Array.Copy(head, signedRegion, EpkFormat.HeaderSize);
            byte[]? wrapBlock = null;
            if (wrapLen > 0)
            {
                wrapBlock = new byte[wrapLen];
                RandomAccess.Read(fh, wrapBlock, EpkFormat.HeaderSize);
                wrapBlock.CopyTo(signedRegion, EpkFormat.HeaderSize);
            }
            byte[]? signature = null;
            uint keyId = 0;
            if (flags.HasFlag(EpkFormat.PackFlags.Signed))
            {
                var sigBlk = new byte[EpkFormat.SigBlockSize];
                RandomAccess.Read(fh, sigBlk,
                    EpkFormat.HeaderSize + wrapLen);
                keyId = BinaryPrimitives
                    .ReadUInt32LittleEndian(sigBlk);
                signature = sigBlk[4..];
            }
            var rawIndex = new byte[indexCSize];
            RandomAccess.Read(fh, rawIndex, indexOff);
            rawIndex.CopyTo(signedRegion, EpkFormat.HeaderSize + wrapLen);

            byte[] indexBuf;
            if (flags.HasFlag(EpkFormat.PackFlags.IndexZstd))
            {
                indexBuf = new byte[indexUSize];
                using var d = new Decompressor();
                var n = d.Unwrap(rawIndex, indexBuf);
                if (n != (int)indexUSize)
                    throw new InvalidDataException(
                        $"{path} : index décompressé {n} ≠ {indexUSize}");
            }
            else
            {
                indexBuf = rawIndex;
            }
            var entries = EpkIndex.Parse(indexBuf);
            var dict = new Dictionary<string, EpkEntry>(entries.Count);
            var paths = new List<string>(entries.Count);
            foreach (var e in entries)
            {
                dict[e.Path] = e;   // dernier gagnant = overlay
                paths.Add(e.Path);
            }
            var r = new EpkReader(fh, dict, paths, flags, packVersion,
                packId, chunkShift, signedRegion, rawIndex);
            if (wrapBlock != null) r.WrapBlock = wrapBlock;
            r.Signature = signature;
            r.KeyId = keyId;
            return r;
        }
        catch
        {
            fh.Dispose();
            throw;
        }
    }

    /// <summary>Entrée par chemin (normalisé ou non — normalisation
    /// faite ici). false si absent du pack.</summary>
    public bool TryGetEntry(string relPath, out EpkEntry entry) =>
        _entries.TryGetValue(EpkFormat.NormalizePath(relPath),
            out entry);

    /// <summary>Tous les chemins du pack (ordre de l'index).</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>Chemins sous un préfixe de dossier ("uidata/",
    /// "data/db/") — remplace les EnumerateDirectories du mode loose.</summary>
    public IEnumerable<string> ListDir(string prefix)
    {
        var p = EpkFormat.NormalizePath(prefix);
        if (p.Length > 0 && !p.EndsWith('/')) p += '/';
        // L'index est trié : scan linéaire borné par le préfixe.
        // (~80k entrées — suffisant ; un trie serait du sur-design.)
        foreach (var path in _paths)
            if (path.StartsWith(p, StringComparison.Ordinal))
                yield return path;
    }

    /// <summary>Lit l'entrée entière en mémoire — UNE lecture
    /// positionnelle du blob puis décodage des chunks en mémoire
    /// (le pattern dominant : fsm/ini/png/glb = read-all).</summary>
    public byte[]? ReadAllBytes(string relPath)
    {
        if (!TryGetEntry(relPath, out var e)) return null;
        if (e.Codec == EpkFormat.BlobCodec.Tombstone) return null;
        if (e.USize == 0) return Array.Empty<byte>();
        var raw = new byte[e.BlobSize];
        RandomAccess.Read(_fh, raw, (long)e.BlobOffset);
        var dst = new byte[e.USize];
        DecodeChunks(e, raw, dst);
        if (!IsEncrypted) VerifySha(e, SHA256.HashData(dst));
        return dst;
    }

    /// <summary>Flux en lecture seule sur l'entrée (décodage à la
    /// demande chunk par chunk — gros fichiers). Non seekable.</summary>
    public Stream? OpenRead(string relPath)
    {
        if (!TryGetEntry(relPath, out var e)) return null;
        if (e.Codec == EpkFormat.BlobCodec.Tombstone) return null;
        return new EpkStream(_fh, e, ChunkShift, Decryptor, !IsEncrypted);
    }

    private bool IsEncrypted => Flags.HasFlag(EpkFormat.PackFlags.Encrypted);

    /// <summary>Pack en clair : le tag GCM n'existe pas, le sha256 de
    /// l'index (signé) est la seule garantie d'intégrité d'un blob.</summary>
    private static void VerifySha(in EpkEntry e, ReadOnlySpan<byte> actual)
    {
        if (!actual.SequenceEqual(e.Sha256))
            throw new InvalidDataException(
                $"{e.Path} : sha256 du blob ≠ index (pack falsifié)");
    }

    /// <summary>Décode le blob brut (lu en une fois) vers
    /// <paramref name="dst"/> (taille = USize). Positions recalculées
    /// en parcourant les headers de chunks.</summary>
    private void DecodeChunks(in EpkEntry e, ReadOnlySpan<byte> raw,
        Span<byte> dst)
    {
        var srcPos = 0;
        var dstPos = 0;
        var idx = 0;
        using var decomp = new Decompressor();
        while (dstPos < dst.Length)
        {
            if (srcPos + EpkFormat.ChunkHeaderSize > raw.Length)
                throw new InvalidDataException(
                    $"{e.Path} : blob tronqué");
            var cSize = BinaryPrimitives
                .ReadUInt32LittleEndian(raw[srcPos..]);
            var uSize = BinaryPrimitives
                .ReadInt32LittleEndian(raw[(srcPos + 4)..]);
            srcPos += EpkFormat.ChunkHeaderSize;
            var payload = raw.Slice(srcPos, (int)cSize);
            srcPos += (int)cSize;
            var cipherLen = (int)cSize;
            ReadOnlySpan<byte> tag = default;
            if (Flags.HasFlag(EpkFormat.PackFlags.Encrypted))
            {
                cipherLen -= EpkFormat.GcmTagSize;
                tag = payload.Slice(cipherLen, EpkFormat.GcmTagSize);
            }
            var outSpan = dst.Slice(dstPos, uSize);
            // stored = octets stockés du chunk (déchiffrés si ENCRYPTED,
            // encore compressés si codec=zstd).
            ReadOnlySpan<byte> stored;
            if (Flags.HasFlag(EpkFormat.PackFlags.Encrypted))
            {
                if (Decryptor == null)
                    throw new InvalidDataException(
                        $"{e.Path} : pack chiffré sans Decryptor");
                stored = Decryptor(e, idx, uSize,
                    payload[..cipherLen], tag);
            }
            else
            {
                stored = payload;
            }
            if (e.Codec == EpkFormat.BlobCodec.Zstd)
            {
                var n = decomp.Unwrap(stored, outSpan);
                if (n != uSize)
                    throw new InvalidDataException(
                        $"{e.Path} : chunk {idx} taille zstd {n} " +
                        $"≠ {uSize}");
            }
            else
            {
                stored.CopyTo(outSpan);
            }
            dstPos += uSize;
            idx++;
        }
    }

    /// <summary>Stream paresseux sur un blob (chunks à la demande).
    /// Utilise les mêmes RandomAccess positionnels — thread-safe sur
    /// le handle partagé.</summary>
    private sealed class EpkStream : Stream
    {
        private readonly SafeFileHandle _fh;
        private readonly EpkEntry _e;
        private readonly int _chunkShift;
        private readonly EpkDecryptor? _dec;
        private long _pos;          // position logique (clair)
        private int _chunkIdx = -1; // chunk courant décodé
        private byte[]? _chunk;     // clair du chunk courant
        private readonly Decompressor _decomp = new();
        private readonly IncrementalHash? _hash;   // pack en clair

        public EpkStream(SafeFileHandle fh, EpkEntry e, int chunkShift,
            EpkDecryptor? dec, bool verifySha)
        {
            _fh = fh; _e = e; _chunkShift = chunkShift; _dec = dec;
            if (verifySha)
                _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => (long)_e.USize;
        public override long Position
        {
            get => _pos;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_pos >= (long)_e.USize) return 0;
            var chunkSize = 1 << _chunkShift;
            var read = 0;
            while (read < buffer.Length && _pos < (long)_e.USize)
            {
                var want = (int)(_pos / chunkSize);
                if (want != _chunkIdx)
                    LoadChunk(want);
                var inChunk = (int)(_pos % chunkSize);
                var avail = _chunk!.Length - inChunk;
                var n = Math.Min(avail, buffer.Length - read);
                _chunk.AsSpan(inChunk, n).CopyTo(buffer[read..]);
                read += n;
                _pos += n;
            }
            if (_hash != null && _pos >= (long)_e.USize)
                VerifySha(_e, _hash.GetHashAndReset());
            return read;
        }

        /// <summary>Positionne sur le chunk i : localise son header
        /// en parcourant les headers précédents (random-access dans
        /// le flux de chunks de l'entrée).</summary>
        private void LoadChunk(int i)
        {
            var off = (long)_e.BlobOffset;
            var head = new byte[EpkFormat.ChunkHeaderSize];
            for (var c = 0; c <= i; c++)
            {
                RandomAccess.Read(_fh, head, off);
                var cSize = BinaryPrimitives
                    .ReadUInt32LittleEndian(head);
                var uSize = BinaryPrimitives
                    .ReadInt32LittleEndian(head[4..]);
                if (c == i)
                {
                    var payload = new byte[cSize];
                    RandomAccess.Read(_fh, payload, off + 8);
                    var cipherLen = (int)cSize;
                    ReadOnlySpan<byte> tag = default;
                    var enc = _dec != null;
                    if (enc)
                    {
                        cipherLen -= EpkFormat.GcmTagSize;
                        tag = payload.AsSpan(cipherLen,
                            EpkFormat.GcmTagSize);
                    }
                    byte[] body = payload;
                    if (enc)
                        body = _dec!(_e, i, uSize,
                            payload.AsSpan(0, cipherLen), tag);
                    _chunk = new byte[uSize];
                    if (_e.Codec == EpkFormat.BlobCodec.Zstd)
                    {
                        var n = _decomp.Unwrap(body, _chunk);
                        if (n != uSize)
                            throw new InvalidDataException(
                                $"{_e.Path} : chunk {i} taille {n} " +
                                $"≠ {uSize}");
                    }
                    else
                    {
                        body.AsSpan(0, uSize).CopyTo(_chunk);
                    }
                    _hash?.AppendData(_chunk);
                    _chunkIdx = i;
                    return;
                }
                off += EpkFormat.ChunkHeaderSize + (int)cSize;
            }
        }

        public override void Flush() { }
        public override long Seek(long o, SeekOrigin w) =>
            throw new NotSupportedException();
        public override void SetLength(long v) =>
            throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) =>
            throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            _decomp.Dispose();
            _hash?.Dispose();
            base.Dispose(disposing);
        }
    }

    public void Dispose() => _fh.Dispose();
}
