// Copyright (c) 2026 Noah Lhommedieu. MIT License.

namespace EdenClient.World;

/// <summary>
/// Bake CPU d'une tuile terrain splat 5 couches en une texture RGBA8
/// sRGB unique — équivalent pixel du <c>terrain_splat.gdshader</c> de
/// référence :
///   w0 = clamp(1 − R − G − B − A), poids R,G,B,A → couches 1..4
///   (HYPOTHESIS canal→slot, validée au rendu vs Godot), renormalisé
///   si Σ &gt; 1 ; blend en espace LINÉAIRE ; × couleur vertex bakée
///   (éclairage) interpolée bilinéairement sur la grille.
/// UV couche = (tileX + u, tileY + v) × uvScale (continu monde), splat
/// échantillonnée bilinéaire clampée à (u, v). Les pixels i couvrent
/// u = i/(N−1) : les bords de tuile tombent sur les centres de texel,
/// donc deux tuiles voisines se rejoignent sans couture (UV mesh =
/// <see cref="MeshUv"/>). Classe PURE — testable sans moteur.
/// </summary>
public static class SplatBake
{
    private static readonly float[] ToLinear = BuildToLinear();
    private static readonly byte[] ToSrgb = BuildToSrgb();
    private const int SrgbLut = 4096;

    /// <summary>Une couche : mips RGBA8 sRGB (mip0 en tête) + uvScale.</summary>
    public readonly record struct Layer(
        List<(byte[] Data, int W, int H)>? Mips, float UvScale);

    /// <summary>UV mesh (0..1 local) du vertex d'indice <paramref name="i"/>
    /// sur <paramref name="cells"/> cellules, pour une bake de
    /// <paramref name="size"/> pixels : centre de texel exact.</summary>
    public static float MeshUv(int i, int cells, int size) =>
        (0.5f + (float)i / cells * (size - 1)) / size;

    /// <param name="splat">128×128 RGBA8 (poids bruts, non sRGB).</param>
    /// <param name="vcol">Couleur vertex RGBA f32 [gridW×gridH×4].</param>
    /// <param name="layers">5 couches (Mips null → gris moyen).</param>
    /// <returns>RGBA8 sRGB size×size, alpha 255.</returns>
    public static byte[] Bake(ReadOnlySpan<byte> splat, int splatSize,
        float[] vcol, int gridW, int gridH, Layer[] layers,
        int tileX, int tileY, int size)
    {
        var splatArr = splat.ToArray();
        var outBuf = new byte[size * size * 4];
        Parallel.For(0, size, py =>
        {
            var v = (float)py / (size - 1);
            for (var px = 0; px < size; px++)
            {
                var u = (float)px / (size - 1);
                SampleSplat(splatArr, splatSize, u, v, out var s0,
                    out var s1, out var s2, out var s3);
                var w0 = Math.Clamp(1f - s0 - s1 - s2 - s3, 0f, 1f);
                var tot = w0 + s0 + s1 + s2 + s3;
                var inv = tot > 1f ? 1f / tot : 1f;
                float r = 0, g = 0, b = 0;
                Acc(layers[0], w0 * inv, tileX + u, tileY + v,
                    size, ref r, ref g, ref b);
                Acc(layers[1], s0 * inv, tileX + u, tileY + v,
                    size, ref r, ref g, ref b);
                Acc(layers[2], s1 * inv, tileX + u, tileY + v,
                    size, ref r, ref g, ref b);
                Acc(layers[3], s2 * inv, tileX + u, tileY + v,
                    size, ref r, ref g, ref b);
                Acc(layers[4], s3 * inv, tileX + u, tileY + v,
                    size, ref r, ref g, ref b);
                VColor(vcol, gridW, gridH, u, v, out var cr, out var cg,
                    out var cb);
                var o = (py * size + px) * 4;
                outBuf[o] = Enc(r * cr);
                outBuf[o + 1] = Enc(g * cg);
                outBuf[o + 2] = Enc(b * cb);
                outBuf[o + 3] = 255;
            }
        });
        return outBuf;
    }

    private static void SampleSplat(byte[] s, int n, float u, float v,
        out float r, out float g, out float b, out float a)
    {
        var fx = Math.Clamp(u, 0f, 1f) * n - 0.5f;
        var fy = Math.Clamp(v, 0f, 1f) * n - 0.5f;
        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        r = g = b = a = 0f;
        for (var j = 0; j < 4; j++)
        {
            var xx = Math.Clamp(x0 + (j & 1), 0, n - 1);
            var yy = Math.Clamp(y0 + (j >> 1), 0, n - 1);
            var w = ((j & 1) == 0 ? 1 - tx : tx) *
                    ((j >> 1) == 0 ? 1 - ty : ty);
            var i = (yy * n + xx) * 4;
            r += s[i] * w; g += s[i + 1] * w;
            b += s[i + 2] * w; a += s[i + 3] * w;
        }
        const float k = 1f / 255f;
        r *= k; g *= k; b *= k; a *= k;
    }

    /// <summary>Échantillon linéaire (bilinéaire + mip adapté) d'une
    /// couche à l'UV monde (wu, wv) — partagé avec <see cref="WaterBake"/>.</summary>
    public static void SampleLinear(Layer l, float wu, float wv,
        int bakeSize, out float r, out float g, out float b)
    {
        r = g = b = 0f;
        Acc(l, 1f, wu, wv, bakeSize, ref r, ref g, ref b);
    }

    /// <summary>Linéaire → sRGB 8 bits.</summary>
    public static byte EncodeSrgb(float lin) => Enc(lin);

    private static void Acc(Layer l, float w, float wu, float wv,
        int size, ref float r, ref float g, ref float b)
    {
        if (w <= 0f)
            return;
        if (l.Mips == null || l.Mips.Count == 0)
        {
            r += 0.2f * w; g += 0.2f * w; b += 0.2f * w; // gris moyen lin.
            return;
        }
        // Mip dont la densité de texels ≈ densité de pixels de bake.
        var m0 = l.Mips[0];
        var ratio = Math.Max(m0.W, m0.H) * Math.Abs(l.UvScale) / size;
        var lod = ratio > 1f
            ? Math.Min((int)MathF.Round(MathF.Log2(ratio)),
                l.Mips.Count - 1)
            : 0;
        var (data, mw, mh) = l.Mips[lod];
        var fx = wu * l.UvScale * mw - 0.5f;
        var fy = wv * l.UvScale * mh - 0.5f;
        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        for (var j = 0; j < 4; j++)
        {
            var xx = Wrap(x0 + (j & 1), mw);
            var yy = Wrap(y0 + (j >> 1), mh);
            var ww = w * ((j & 1) == 0 ? 1 - tx : tx) *
                     ((j >> 1) == 0 ? 1 - ty : ty);
            var i = (yy * mw + xx) * 4;
            r += ToLinear[data[i]] * ww;
            g += ToLinear[data[i + 1]] * ww;
            b += ToLinear[data[i + 2]] * ww;
        }
    }

    private static int Wrap(int i, int n)
    {
        var m = i % n;
        return m < 0 ? m + n : m;
    }

    private static void VColor(float[] c, int gw, int gh, float u,
        float v, out float r, out float g, out float b)
    {
        var fx = Math.Clamp(u, 0f, 1f) * (gw - 1);
        var fy = Math.Clamp(v, 0f, 1f) * (gh - 1);
        var x0 = Math.Min((int)fx, gw - 2);
        var y0 = Math.Min((int)fy, gh - 2);
        var tx = fx - x0;
        var ty = fy - y0;
        r = g = b = 0f;
        for (var j = 0; j < 4; j++)
        {
            var xx = x0 + (j & 1);
            var yy = y0 + (j >> 1);
            var w = ((j & 1) == 0 ? 1 - tx : tx) *
                    ((j >> 1) == 0 ? 1 - ty : ty);
            var i = (yy * gw + xx) * 4;
            r += c[i] * w; g += c[i + 1] * w; b += c[i + 2] * w;
        }
    }

    private static byte Enc(float lin)
    {
        var i = (int)(Math.Clamp(lin, 0f, 1f) * (SrgbLut - 1) + 0.5f);
        return ToSrgb[i];
    }

    private static float[] BuildToLinear()
    {
        var t = new float[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255f;
            t[i] = c <= 0.04045f ? c / 12.92f
                : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return t;
    }

    private static byte[] BuildToSrgb()
    {
        var t = new byte[SrgbLut];
        for (var i = 0; i < SrgbLut; i++)
        {
            var l = i / (float)(SrgbLut - 1);
            var c = l <= 0.0031308f ? l * 12.92f
                : 1.055f * MathF.Pow(l, 1f / 2.4f) - 0.055f;
            t[i] = (byte)(Math.Clamp(c, 0f, 1f) * 255f + 0.5f);
        }
        return t;
    }
}
