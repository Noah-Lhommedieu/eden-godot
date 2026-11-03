using System.Buffers.Binary;

namespace EdenClient.World;

/// <summary>
/// Lecteur d'en-tête <c>.dds</c> (DirectDraw Surface) — le format de
/// texture natif d'Eden (map/texture/*.dds, interfaces, icônes).
/// Format : magic <c>"DDS "</c> puis DDS_HEADER 124 o (taille @0x04,
/// flags @0x08, hauteur @0x0C, largeur @0x10, mipcount @0x1C, ddspf
/// 32 o @0x4C : size/flags/fourCC/bitcount/masques RGBA), données à
/// 0x80 (ou 0x94 avec l'en-tête DX10 quand fourCC == "DX10").
/// Validé par hex sur S002 : s0024102.dds = DXT1 128×128,
/// s0024104.dds = DXT5 128×128.
/// Classe PURE (aucune dépendance moteur) — testable dans NetTests.
/// </summary>
public sealed class DdsInfo
{
    /// <summary>Format du payload déduit de ddspf.</summary>
    public enum DdsFormat
    {
        /// <summary>Non reconnu (masques/bits inattendus).</summary>
        Unknown = 0,
        /// <summary>DXT1 — blocs 4×4 de 8 o (RGB 565 + 1 bit alpha).</summary>
        Bc1,
        /// <summary>DXT3 — blocs 4×4 de 16 o (alpha explicite 4 bpp).</summary>
        Bc2,
        /// <summary>DXT5 — blocs 4×4 de 16 o (alpha interpolé).</summary>
        Bc3,
        /// <summary>ATI1/BC4U — blocs 4×4 de 8 o (un canal).</summary>
        Bc4,
        /// <summary>ATI2/BC5U — blocs 4×4 de 16 o (deux canaux, normales).</summary>
        Bc5,
        /// <summary>RGBA 32 bits non compressé (masques R→A).</summary>
        Rgba32,
        /// <summary>BGRA 32 bits non compressé.</summary>
        Bgra32,
        /// <summary>Luminance 8 bits (caustiques d'eau caustNN).</summary>
        Lum8,
    }

    /// <summary>Largeur du mip 0 (pixels).</summary>
    public int Width { get; private init; }
    /// <summary>Hauteur du mip 0 (pixels).</summary>
    public int Height { get; private init; }
    /// <summary>Nombre de niveaux mip (1 si non mipmappé).</summary>
    public int MipLevels { get; private init; }
    /// <summary>Offset des données du mip 0 (0x80, ou 0x94 en DX10).</summary>
    public int DataOffset { get; private init; }
    /// <summary>Format du payload.</summary>
    public DdsFormat Format { get; private init; }

    private const int DdpfFourCc = 0x4;
    private const int DdpfRgb = 0x40;

    /// <summary>Octets d'un bloc (BC) ou d'un texel (RGBA).</summary>
    public int BlockBytes => Format switch
    {
        DdsFormat.Bc1 or DdsFormat.Bc4 => 8,
        DdsFormat.Bc2 or DdsFormat.Bc3 or DdsFormat.Bc5 => 16,
        DdsFormat.Rgba32 or DdsFormat.Bgra32 => 4,
        DdsFormat.Lum8 => 1,
        _ => 0,
    };

    /// <summary>Taille d'un niveau mip de dimensions (w, h).</summary>
    public int MipSize(int w, int h)
    {
        if (Format is DdsFormat.Rgba32 or DdsFormat.Bgra32)
            return w * h * 4;
        if (Format == DdsFormat.Lum8)
            return w * h;
        var bw = System.Math.Max(1, (w + 3) >> 2);
        var bh = System.Math.Max(1, (h + 3) >> 2);
        return bw * bh * BlockBytes;
    }

    /// <summary>Octets d'une ligne de blocs (RowPitch GPU).</summary>
    public int RowPitch(int w)
    {
        if (Format is DdsFormat.Rgba32 or DdsFormat.Bgra32)
            return w * 4;
        if (Format == DdsFormat.Lum8)
            return w;
        return System.Math.Max(1, (w + 3) >> 2) * BlockBytes;
    }

    /// <summary>Offset absolu du niveau mip `level` dans le fichier.</summary>
    public int MipOffset(int level)
    {
        var off = DataOffset;
        var w = Width;
        var h = Height;
        for (var i = 0; i < level; i++)
        {
            off += MipSize(w, h);
            w = System.Math.Max(1, w >> 1);
            h = System.Math.Max(1, h >> 1);
        }
        return off;
    }

    /// <summary>Parse l'en-tête d'un buffer .dds ; false si magic
    /// absent, header tronqué ou format non supporté.</summary>
    public static bool TryParse(byte[] d, out DdsInfo info)
    {
        info = new DdsInfo();
        if (d.Length < 0x80 ||
            BinaryPrimitives.ReadUInt32LittleEndian(d) != 0x20534444 ||
            BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x04)) != 124 ||
            BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x4c)) != 32)
            return false;

        var h = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x0c));
        var w = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x10));
        var mips = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x1c));
        var pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x50));
        var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x54));
        var bitCount = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x58));
        var rMask = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x5c));

        if (w <= 0 || h <= 0) return false;
        var offset = 0x80;
        DdsFormat fmt;
        if ((pfFlags & DdpfFourCc) != 0)
        {
            fmt = fourCc switch
            {
                0x31545844 => DdsFormat.Bc1, // "DXT1"
                0x33545844 => DdsFormat.Bc2, // "DXT3"
                0x35545844 => DdsFormat.Bc3, // "DXT5"
                0x31495441 => DdsFormat.Bc4, // "ATI1"
                0x55344342 => DdsFormat.Bc4, // "BC4U"
                0x32495441 => DdsFormat.Bc5, // "ATI2"
                0x55354342 => DdsFormat.Bc5, // "BC5U"
                // En-tête DX10 : dxgi format @0x80 après le header.
                0x30315844 => Dx10(d, out fmt, out offset),
                _ => DdsFormat.Unknown,
            };
        }
        else if ((pfFlags & DdpfRgb) != 0 && bitCount == 32)
        {
            // R en premier octet → RGBA ; sinon BGRA (ordre DirectDraw).
            fmt = rMask == 0x000000ff ? DdsFormat.Rgba32 : DdsFormat.Bgra32;
        }
        else if ((pfFlags & 0x20000) != 0 && bitCount == 8)
            fmt = DdsFormat.Lum8;
        else return false;
        if (fmt == DdsFormat.Unknown) return false;

        info = new DdsInfo
        {
            Width = w,
            Height = h,
            MipLevels = System.Math.Max(1, mips),
            DataOffset = offset,
            Format = fmt,
        };
        return true;
    }

    /// <summary>fourCC "DX10" : DXGI format @0x80 (71-73 = BC1-3,
    /// 80-83 = BC4/BC5). Données à 0x94.</summary>
    private static DdsFormat Dx10(byte[] d, out DdsFormat fmt,
        out int offset)
    {
        offset = 0x94;
        if (d.Length < 0x84)
        {
            fmt = DdsFormat.Unknown;
            return fmt;
        }
        var dxgi = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x80));
        fmt = dxgi switch
        {
            71 or 72 => DdsFormat.Bc1,   // BC1_UNORM(_SRGB)
            74 or 75 => DdsFormat.Bc2,   // BC2_UNORM(_SRGB)
            77 or 78 => DdsFormat.Bc3,   // BC3_UNORM(_SRGB)
            80 or 81 => DdsFormat.Bc4,   // BC4_UNORM/SNORM
            83 or 84 => DdsFormat.Bc5,   // BC5_UNORM/SNORM
            28 or 87 => DdsFormat.Rgba32,// R8G8B8A8_UNORM(_SRGB)
            _ => DdsFormat.Unknown,
        };
        return fmt;
    }
}
