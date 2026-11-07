using System;
using System.Buffers.Binary;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Limitless.Studio.Services;

/// <summary>Décodeur DDS pour les icônes d'items Reforged —
/// textures 64×64 non compressées (fourcc=0), pixels 16 bpp
/// (R5G6B5 ou A1R5G5B5 selon les masques du header), ou 32 bpp
/// BGRA. Sortie = WriteableBitmap Avalonia (BGRA8888).</summary>
public static class DdsDecoder
{
    /// <summary>Convertit les octets DDS en bitmap, ou null si le
    /// format n'est pas supporté (DXT compressé).</summary>
    public static Bitmap? Decode(byte[] d)
    {
        if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' ||
            d[2] != 'S' || d[3] != ' ') return null;
        var h = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(12));
        var w = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(16));
        var fourcc = BinaryPrimitives.ReadUInt32LittleEndian(
            d.AsSpan(84));
        if (fourcc != 0) return null;              // compressé
        var pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(
            d.AsSpan(80));
        var bpp = BinaryPrimitives.ReadUInt32LittleEndian(
            d.AsSpan(88));
        var rMask = BinaryPrimitives.ReadUInt32LittleEndian(
            d.AsSpan(92));
        var gMask = BinaryPrimitives.ReadUInt32LittleEndian(
            d.AsSpan(96));
        var bMask = BinaryPrimitives.ReadUInt32LittleEndian(
            d.AsSpan(100));
        var aMask = (pfFlags & 0x1) != 0
            ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(104))
            : 0u;

        var px = d.AsSpan(128);
        var bgra = new byte[w * h * 4];
        if (bpp == 32)
        {
            for (var i = 0; i < w * h; i++)
            {
                var v = BinaryPrimitives.ReadUInt32LittleEndian(
                    px.Slice(i * 4));
                bgra[i * 4 + 0] = Chan(v, bMask);
                bgra[i * 4 + 1] = Chan(v, gMask);
                bgra[i * 4 + 2] = Chan(v, rMask);
                bgra[i * 4 + 3] = aMask != 0 ? Chan(v, aMask) : (byte)255;
            }
        }
        else if (bpp == 16)
        {
            for (var i = 0; i < w * h; i++)
            {
                var v = BinaryPrimitives.ReadUInt16LittleEndian(
                    px.Slice(i * 2));
                bgra[i * 4 + 0] = Chan(v, bMask);
                bgra[i * 4 + 1] = Chan(v, gMask);
                bgra[i * 4 + 2] = Chan(v, rMask);
                bgra[i * 4 + 3] = aMask != 0 ? Chan(v, aMask) : (byte)255;
            }
        }
        else return null;

        var bmp = new WriteableBitmap(
            new Avalonia.PixelSize(w, h),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bmp.Lock())
            System.Runtime.InteropServices.Marshal.Copy(
                bgra, 0, fb.Address, bgra.Length);
        return bmp;
    }

    /// <summary>Extrait le canal masqué et le normalise en 8 bits.</summary>
    private static byte Chan(uint v, uint mask)
    {
        if (mask == 0) return 0;
        var shift = 0;
        var m = mask;
        while ((m & 1) == 0) { m >>= 1; shift++; }
        var raw = (v & mask) >> shift;
        var max = m;                       // ex. 0x1f, 0x3f, 0xff
        return (byte)(raw * 255 / max);
    }
}
