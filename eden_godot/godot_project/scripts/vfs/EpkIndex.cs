using System.Buffers.Binary;

namespace EdenClient.Vfs;

/// <summary>
/// Entrée d'index EPK v1 (88 octets sérialisés — voir
/// docs/packaging/EPK-FORMAT.md). Le sha256 porte sur le contenu en clair :
/// il lie les blobs à l'index signé même pour les packs non chiffrés.
/// </summary>
public readonly record struct EpkEntry(
    string Path,
    ulong FileId,
    ulong BlobOffset,
    ulong BlobSize,
    ulong USize,
    uint CryptoRev,
    EpkFormat.BlobCodec Codec,
    EpkFormat.TexCodec TexCodec,
    byte[] Sha256)
{
    public int ChunkCount(int chunkShift) =>
        USize == 0 ? 0 : (int)((USize - 1) >> chunkShift) + 1;
}

/// <summary>
/// Sérialisation de l'index EPK (table de chaînes + enregistrements
/// fixes triés). Une seule implémentation partagée par l'écrivain
/// (edenpack) et le lecteur (client) — zéro dérive de format.
/// </summary>
public static class EpkIndex
{
    /// <summary>Sérialise l'index complet. <paramref name="entries"/>
    /// doit être trié par chemin normalisé (déterminisme).</summary>
    public static byte[] Serialize(IReadOnlyList<EpkEntry> entries)
    {
        // Table de chaînes : chemins UTF-8 NUL-terminés, dans l'ordre.
        using var strs = new MemoryStream();
        var pathOffs = new uint[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            pathOffs[i] = (uint)strs.Position;
            var bytes = System.Text.Encoding.UTF8.GetBytes(entries[i].Path);
            strs.Write(bytes);
            strs.WriteByte(0);
        }
        var strTable = strs.ToArray();

        var size = 4 + strTable.Length +
                   entries.Count * EpkFormat.IndexEntrySize;
        var buf = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(buf,
            (uint)strTable.Length);
        strTable.CopyTo(buf, 4);
        var rec = buf.AsSpan(4 + strTable.Length);
        for (var i = 0; i < entries.Count; i++)
        {
            WriteEntry(rec, entries[i], pathOffs[i]);
            rec = rec[EpkFormat.IndexEntrySize..];
        }
        return buf;
    }

    /// <summary>Désérialise l'index (décompressé) en liste ordonnée.</summary>
    public static List<EpkEntry> Parse(ReadOnlySpan<byte> index)
    {
        var strSize = BinaryPrimitives.ReadUInt32LittleEndian(index);
        var strTable = index.Slice(4, (int)strSize);
        var rec = index.Slice(4 + (int)strSize);
        if (rec.Length % EpkFormat.IndexEntrySize != 0)
            throw new InvalidDataException(
                $"index EPK tronqué ({rec.Length} o de records)");
        var list = new List<EpkEntry>(
            rec.Length / EpkFormat.IndexEntrySize);
        while (rec.Length >= EpkFormat.IndexEntrySize)
        {
            list.Add(ReadEntry(rec, strTable));
            rec = rec[EpkFormat.IndexEntrySize..];
        }
        return list;
    }

    private static void WriteEntry(Span<byte> dst, in EpkEntry e,
        uint pathOff)
    {
        dst.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(dst, pathOff);
        // +4 flags (réservé 0)
        BinaryPrimitives.WriteUInt64LittleEndian(dst[8..], e.FileId);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[16..], e.BlobOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[24..], e.BlobSize);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[32..], e.USize);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[40..], e.CryptoRev);
        dst[44] = (byte)e.Codec;
        dst[45] = (byte)e.TexCodec;
        // +46 reserved u16
        e.Sha256.CopyTo(dst[48..80]);
        // +80 reserved u8[8]
    }

    private static EpkEntry ReadEntry(ReadOnlySpan<byte> rec,
        ReadOnlySpan<byte> strTable)
    {
        var pathOff = (int)BinaryPrimitives
            .ReadUInt32LittleEndian(rec);
        var end = strTable[pathOff..].IndexOf((byte)0);
        var path = System.Text.Encoding.UTF8.GetString(
            strTable.Slice(pathOff, end));
        return new EpkEntry(
            Path: path,
            FileId: BinaryPrimitives.ReadUInt64LittleEndian(rec[8..]),
            BlobOffset: BinaryPrimitives
                .ReadUInt64LittleEndian(rec[16..]),
            BlobSize: BinaryPrimitives
                .ReadUInt64LittleEndian(rec[24..]),
            USize: BinaryPrimitives
                .ReadUInt64LittleEndian(rec[32..]),
            CryptoRev: BinaryPrimitives
                .ReadUInt32LittleEndian(rec[40..]),
            Codec: (EpkFormat.BlobCodec)rec[44],
            TexCodec: (EpkFormat.TexCodec)rec[45],
            Sha256: rec.Slice(48, 32).ToArray());
    }
}
