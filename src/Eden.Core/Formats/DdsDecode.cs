// Copyright (c) 2026 Noah Lhommedieu. MIT License.

namespace EdenClient.World;

/// <summary>
/// Décodeur CPU des DDS de masques perso (peau <c>&lt;L&gt;X01AS01A</c>,
/// cheveux <c>&lt;L&gt;1H*S01</c>, visage <c>&lt;L&gt;FACES*B</c> =
/// cartes d'intensité DXT1/DXT3 — formats relevés sur fr-full).
/// Sert à la teinte CharColor : le buffer RGBA8 est recoloré puis
/// ré-uploadé — le décodeur GPU natif ne lit pas les pixels.
/// Classe PURE — testable dans NetTests.
/// </summary>
public static class DdsDecode
{
    /// <summary>Mip 0 d'un DDS en RGBA8 (BC1, BC3, RGBA32, BGRA32) —
    /// null si le format n'est pas couvert ou le payload tronqué.</summary>
    public static byte[]? Mip0ToRgba8(byte[] dds, DdsInfo info)
    {
        if (info.Width <= 0 || info.Height <= 0 ||
            info.DataOffset + info.MipSize(info.Width, info.Height) >
                dds.Length)
            return null;
        var dst = new byte[info.Width * info.Height * 4];
        var src = dds.AsSpan(info.DataOffset);
        switch (info.Format)
        {
            case DdsInfo.DdsFormat.Bc1:
                Bc1(src, info.Width, info.Height, dst);
                return dst;
            case DdsInfo.DdsFormat.Lum8:
                for (var i = 0; i < info.Width * info.Height; i++)
                {
                    dst[i * 4] = dst[i * 4 + 1] = dst[i * 4 + 2] = src[i];
                    dst[i * 4 + 3] = 255;
                }
                return dst;
            case DdsInfo.DdsFormat.Bc2:
                Bc2(src, info.Width, info.Height, dst);
                return dst;
            case DdsInfo.DdsFormat.Bc3:
                Bc3(src, info.Width, info.Height, dst);
                return dst;
            case DdsInfo.DdsFormat.Rgba32:
                src[..dst.Length].CopyTo(dst);
                return dst;
            case DdsInfo.DdsFormat.Bgra32:
                for (var i = 0; i < dst.Length; i += 4)
                {
                    dst[i] = src[i + 2];
                    dst[i + 1] = src[i + 1];
                    dst[i + 2] = src[i];
                    dst[i + 3] = src[i + 3];
                }
                return dst;
            default:
                return null;
        }
    }

    /// <summary>Chaîne mip RGBA8 (box 2×2) à partir du mip 0 — pour
    /// l'upload GPU d'une texture recolorée (même convention que le
    /// <c>GenerateMipmaps</c> du client de référence).</summary>
    public static List<(byte[] Data, int W, int H)> RgbaMips(
        byte[] rgba, int w, int h)
    {
        var chain = new List<(byte[], int, int)> { (rgba, w, h) };
        while (w > 1 || h > 1)
        {
            var nw = Math.Max(1, w >> 1);
            var nh = Math.Max(1, h >> 1);
            var next = new byte[nw * nh * 4];
            for (var y = 0; y < nh; y++)
            for (var x = 0; x < nw; x++)
            {
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var dy = 0; dy < 2; dy++)
                for (var dx = 0; dx < 2; dx++)
                {
                    var sx = Math.Min(w - 1, x * 2 + dx);
                    var sy = Math.Min(h - 1, y * 2 + dy);
                    var i = (sy * w + sx) * 4;
                    r += rgba[i]; g += rgba[i + 1];
                    b += rgba[i + 2]; a += rgba[i + 3];
                    n++;
                }
                var o = (y * nw + x) * 4;
                next[o] = (byte)(r / n);
                next[o + 1] = (byte)(g / n);
                next[o + 2] = (byte)(b / n);
                next[o + 3] = (byte)(a / n);
            }
            chain.Add((next, nw, nh));
            rgba = next; w = nw; h = nh;
        }
        return chain;
    }

    /// <summary>Blocs BC1 (DXT1, 8 o/bloc 4×4) → RGBA8. Mode couleur
    /// (c0 &gt; c1) : 4 teintes opaques ; mode 1-bit (c0 ≤ c1) :
    /// index 3 = transparent — l'alpha est préservé pour le matériau
    /// alpha-scissor (masque capillaire).</summary>
    private static void Bc1(ReadOnlySpan<byte> src, int w, int h,
        byte[] dst)
    {
        var bw = (w + 3) >> 2;
        var bh = (h + 3) >> 2;
        Span<uint> pal = stackalloc uint[4];
        for (var by = 0; by < bh; by++)
        for (var bx = 0; bx < bw; bx++)
        {
            var b = src.Slice((by * bw + bx) * 8, 8);
            var c0 = b[0] | (b[1] << 8);
            var c1 = b[2] | (b[3] << 8);
            pal[0] = Rgb565(c0, 255);
            pal[1] = Rgb565(c1, 255);
            if (c0 > c1)
            {
                pal[2] = Mix(pal[0], pal[1], 2, 1);
                pal[3] = Mix(pal[0], pal[1], 1, 2);
            }
            else
            {
                pal[2] = Mix(pal[0], pal[1], 1, 1);
                pal[3] = 0;                    // alpha 0
            }
            var lut = (uint)(b[4] | (b[5] << 8) | (b[6] << 16) |
                             (b[7] << 24));
            for (var p = 0; p < 16; p++)
            {
                var x = bx * 4 + (p & 3);
                var y = by * 4 + (p >> 2);
                if (x >= w || y >= h) continue;
                var c = pal[(int)((lut >> (p * 2)) & 3)];
                var i = (y * w + x) * 4;
                dst[i] = (byte)(c >> 16);
                dst[i + 1] = (byte)(c >> 8);
                dst[i + 2] = (byte)c;
                dst[i + 3] = (byte)(c >> 24);
            }
        }
    }

    /// <summary>Blocs BC3 (DXT5, 16 o/bloc : alpha interpolé 8 o +
    /// couleur BC1 8 o en mode 4 teintes — jamais de transparence).</summary>
    /// <summary>Blocs BC2 (DXT3) : alpha explicite 4 bits + bloc
    /// couleur 4 teintes — terrains/props fr-full.</summary>
    private static void Bc2(ReadOnlySpan<byte> src, int w, int h,
        byte[] dst)
    {
        var bw = (w + 3) >> 2;
        var bh = (h + 3) >> 2;
        Span<uint> pal = stackalloc uint[4];
        for (var by = 0; by < bh; by++)
        for (var bx = 0; bx < bw; bx++)
        {
            var b = src.Slice((by * bw + bx) * 16, 16);
            var c0 = b[8] | (b[9] << 8);
            var c1 = b[10] | (b[11] << 8);
            pal[0] = Rgb565(c0, 0);
            pal[1] = Rgb565(c1, 0);
            pal[2] = Mix(pal[0], pal[1], 2, 1);
            pal[3] = Mix(pal[0], pal[1], 1, 2);
            var lut = (uint)(b[12] | (b[13] << 8) | (b[14] << 16) |
                             (b[15] << 24));
            for (var p = 0; p < 16; p++)
            {
                var x = bx * 4 + (p & 3);
                var y = by * 4 + (p >> 2);
                if (x >= w || y >= h) continue;
                var c = pal[(int)((lut >> (p * 2)) & 3)];
                var a4 = (b[p >> 1] >> ((p & 1) * 4)) & 15;
                var i = (y * w + x) * 4;
                dst[i] = (byte)(c >> 16);
                dst[i + 1] = (byte)(c >> 8);
                dst[i + 2] = (byte)c;
                dst[i + 3] = (byte)(a4 * 17);
            }
        }
    }

    private static void Bc3(ReadOnlySpan<byte> src, int w, int h,
        byte[] dst)
    {
        var bw = (w + 3) >> 2;
        var bh = (h + 3) >> 2;
        Span<byte> al = stackalloc byte[8];
        Span<uint> pal = stackalloc uint[4];
        for (var by = 0; by < bh; by++)
        for (var bx = 0; bx < bw; bx++)
        {
            var b = src.Slice((by * bw + bx) * 16, 16);
            // Alpha : a0/a1 + 16 indices 3 bits (little-endian 48 bits).
            al[0] = b[0]; al[1] = b[1];
            if (b[0] > b[1])
            {
                for (var i = 2; i < 8; i++)
                    al[i] = (byte)(((8 - i) * b[0] + (i - 1) * b[1]) / 7);
            }
            else
            {
                for (var i = 2; i < 6; i++)
                    al[i] = (byte)(((6 - i) * b[0] + (i - 1) * b[1]) / 5);
                al[6] = 0; al[7] = 255;
            }
            ulong aLut = 0;
            for (var i = 2; i < 8; i++)
                aLut |= (ulong)b[i] << ((i - 2) * 8);
            // Couleur : bloc BC1 toujours en mode 4 teintes.
            var c0 = b[8] | (b[9] << 8);
            var c1 = b[10] | (b[11] << 8);
            pal[0] = Rgb565(c0, 0);
            pal[1] = Rgb565(c1, 0);
            pal[2] = Mix(pal[0], pal[1], 2, 1);
            pal[3] = Mix(pal[0], pal[1], 1, 2);
            var lut = (uint)(b[12] | (b[13] << 8) | (b[14] << 16) |
                             (b[15] << 24));
            for (var p = 0; p < 16; p++)
            {
                var x = bx * 4 + (p & 3);
                var y = by * 4 + (p >> 2);
                if (x >= w || y >= h) continue;
                var c = pal[(int)((lut >> (p * 2)) & 3)];
                var i = (y * w + x) * 4;
                dst[i] = (byte)(c >> 16);
                dst[i + 1] = (byte)(c >> 8);
                dst[i + 2] = (byte)c;
                dst[i + 3] = al[(int)((aLut >> (p * 3)) & 7)];
            }
        }
    }

    /// <summary>RGB 565 → pixel empaqueté 0xAARRGGBB.</summary>
    private static uint Rgb565(int c, int a) =>
        (uint)(a << 24) | (uint)((((c >> 11) & 31) * 255 / 31) << 16) |
        (uint)((((c >> 5) & 63) * 255 / 63) << 8) |
        (uint)((c & 31) * 255 / 31);

    /// <summary>(c0·wa + c1·wb)/3 par canal — pixels 0x--RRGGBB
    /// (alpha ignoré : toujours 255 sauf pal[3] du mode 1-bit).</summary>
    private static uint Mix(uint c0, uint c1, int wa, int wb)
    {
        var r = (((c0 >> 16) & 255) * (uint)wa +
                 ((c1 >> 16) & 255) * (uint)wb) / 3;
        var g = (((c0 >> 8) & 255) * (uint)wa +
                 ((c1 >> 8) & 255) * (uint)wb) / 3;
        var b = ((c0 & 255) * (uint)wa + (c1 & 255) * (uint)wb) / 3;
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }
}
