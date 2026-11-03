using System.Text;

namespace EdenClient.Vfs;

/// <summary>
/// Constantes et énumérations du format EPK v1 — contrat partagé
/// entre le client (lecture via <see cref="EpkReader"/>) et
/// tools/edenpack (écriture). Spec : docs/packaging/EPK-FORMAT.md.
/// Pur .NET — aucune dépendance Godot (testable sous NetTests).
/// </summary>
public static class EpkFormat
{
    /// <summary>"EPK1" little-endian.</summary>
    public const uint Magic = 0x314B5045;
    public const int FormatVersion = 1;
    public const int HeaderSize = 80;
    public const int IndexEntrySize = 88;
    /// <summary>Taille de chunk par défaut : 4 Mio décompressés.</summary>
    public const int DefaultChunkShift = 22;
    /// <summary>kdataWrapped 32 + nonce 12 + tag 16.</summary>
    public const int WrapBlockSize = 60;
    /// <summary>keyId u32 + signature ECDSA P-256 r‖s 64.</summary>
    public const int SigBlockSize = 68;
    /// <summary>Tag GCM inline en fin de chunk chiffré.</summary>
    public const int GcmTagSize = 16;
    /// <summary>Header de chunk : cSize u32 + uSize u32.</summary>
    public const int ChunkHeaderSize = 8;

    [Flags]
    public enum PackFlags : uint
    {
        Encrypted = 1,
        Signed = 2,
        IndexZstd = 4,
    }

    /// <summary>Codec de blob. <c>Tombstone</c> = entrée de
    /// suppression (patch) : pas de payload, BlobOffset/BlobSize/
    /// USize = 0 ; en overlay elle masque l'entrée du pack de base.</summary>
    public enum BlobCodec : byte { Raw = 0, Zstd = 1, Tombstone = 0xFF }

    /// <summary>Codec GPU pré-compressé du contenu (textures).</summary>
    public enum TexCodec : byte
    {
        Raw = 0, S3tc = 1, Bptc = 2, Etc2 = 3, Astc = 4, Webp = 5,
    }

    /// <summary>Chemin pack canonique : '/'-séparés, minuscules,
    /// sans séparateur de tête.</summary>
    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    /// <summary>fileId stable entre releases : FNV-1a-64 du chemin
    /// normalisé.</summary>
    public static ulong FileId(string relPath) =>
        Fnv1a64(NormalizePath(relPath));

    public static ulong Fnv1a64(string s)
    {
        const ulong prime = 1099511628211UL;
        var h = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(s))
            h = (h ^ b) * prime;
        return h;
    }

    /// <summary>offset absolu où commencent les blobs selon les flags
    /// (header + crypto bloc + sig bloc).</summary>
    public static int BlobBase(PackFlags flags)
    {
        var off = HeaderSize;
        if (flags.HasFlag(PackFlags.Encrypted)) off += WrapBlockSize;
        if (flags.HasFlag(PackFlags.Signed)) off += SigBlockSize;
        return off;
    }
}
