using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EdenClient.Pkg;

/// <summary>Entrée de pkg.idx (record de 592 octets).</summary>
public sealed record PkgEntry(int Index, int Pkg, uint Offset, uint CompressedSize, uint Size, ulong Flags, uint Crc, string Path);

/// <summary>
/// Lecteur de pkg.idx + pkgNNN.pkg du client EDENFR. Source d'IMPORT uniquement (ADR-0005) :
/// le client lui-même ne lit jamais de .pkg. Format : docs/formats/PKG-FORMAT.md.
/// </summary>
public sealed class PkgArchive
{
    const int HeaderSize = 292, RecordSize = 592;
    const ulong FlagRaw = 0x40;

    readonly string _dir;
    public IReadOnlyList<PkgEntry> Entries { get; }

    public PkgArchive(string pkgDir)
    {
        _dir = pkgDir;
        Entries = ParseIndex(File.ReadAllBytes(System.IO.Path.Combine(pkgDir, "pkg.idx")));
    }

    public static List<PkgEntry> ParseIndex(ReadOnlySpan<byte> d)
    {
        var list = new List<PkgEntry>();
        for (int i = 0, n = (d.Length - HeaderSize) / RecordSize; i < n; i++)
        {
            var r = d.Slice(HeaderSize + i * RecordSize, RecordSize);
            string n2 = CString(r.Slice(60, 260)), n1 = CString(r.Slice(320, 260));
            list.Add(new PkgEntry(i,
                Pkg: (int)BinaryPrimitives.ReadUInt32LittleEndian(r[584..]),
                Offset: BinaryPrimitives.ReadUInt32LittleEndian(r[4..]),
                CompressedSize: BinaryPrimitives.ReadUInt32LittleEndian(r[12..]),
                Size: BinaryPrimitives.ReadUInt32LittleEndian(r[56..]),
                Flags: BinaryPrimitives.ReadUInt64LittleEndian(r[24..]),
                Crc: (uint)BinaryPrimitives.ReadUInt64LittleEndian(r[16..]),
                Path: SafePath(n1 + n2)));
        }
        return list;
    }

    static string CString(ReadOnlySpan<byte> b)
    {
        int z = b.IndexOf((byte)0);
        return Encoding.Latin1.GetString(z < 0 ? b : b[..z]);
    }

    /// <summary>`\` → `/`, UIData → uidata, rejet de `..`.</summary>
    public static string SafePath(string raw)
    {
        var parts = raw.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToArray();
        if (parts.Length == 0 || parts.Contains("..")) throw new InvalidDataException($"chemin dangereux dans l'index : {raw}");
        if (parts[0].Equals("uidata", StringComparison.OrdinalIgnoreCase)) parts[0] = "uidata";
        return string.Join('/', parts);
    }

    /// <summary>Contenu décompressé de l'entrée, vérifié contre la taille et le CRC32C de l'index.</summary>
    public byte[] Read(PkgEntry e)
    {
        byte[] raw = new byte[e.CompressedSize];
        using (var f = File.OpenRead(System.IO.Path.Combine(_dir, $"pkg{e.Pkg:D3}.pkg")))
        {
            f.Seek(e.Offset, SeekOrigin.Begin);
            f.ReadExactly(raw);
        }
        byte[] data = Unpack(raw, e);
        if (ManifestCrc.Compute(data) != e.Crc) throw new InvalidDataException($"CRC32C différent : {e.Path}");
        return data;
    }

    static byte[] Unpack(byte[] raw, PkgEntry e)
    {
        if ((e.Flags & FlagRaw) != 0) return raw;
        // zsize == size n'indique PAS un contenu brut (S1762108.ifl : 28 o compressés en 28 o).
        try
        {
            if (raw.Length >= 2 && raw[0] == 0x78)
            {
                using var z = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
                var o = new byte[e.Size]; z.ReadExactly(o); return o;
            }
        }
        catch (InvalidDataException) { /* tombe sur inflate64 */ }
        return Inflate64.Decompress(raw.AsSpan(2), (int)e.Size);
    }
}
