// Copyright (c) 2026 Noah Lhommedieu. MIT License.

namespace EdenClient.World;

/// <summary>
/// Bake CPU d'une frame d'eau .fsm (kind=1) — équivalent pixel du
/// <c>water.gdshader</c> de référence :
///   base = texture slot0 à UV monde × uvScale ; caustique = frame .ifl
///   courante à UV × 0.6 ; c = mix(base, base·caust·1.7, mix) ;
///   RGB = c · teinte · couleur-vertex ; A = teinte.a · alpha-vertex
///   (fade de rive baké dans l'attribut #3).
/// Une texture par (tuile, frame) — le flipbook est piloté côté moteur
/// (swap de texture à fps). Drift UV et onde vertex : non reproduits
/// (matériau Flax sans nœud temps — voir STATUS). Classe PURE.
/// </summary>
public static class WaterBake
{
    /// <summary>RGBA8 sRGB size×size d'une tuile eau pour une frame.</summary>
    public static byte[] Bake(SplatBake.Layer baseTex,
        SplatBake.Layer? caustic, float[] vcol, int gridW, int gridH,
        int tileX, int tileY, int size, float tintR, float tintG,
        float tintB, float tintA, float uvScale = 6f,
        float causticMix = 0.55f)
    {
        var o = new byte[size * size * 4];
        var bl = baseTex with { UvScale = 1f };
        var cl = caustic is { } c ? c with { UvScale = 1f } : (SplatBake.Layer?)null;
        Parallel.For(0, size, py =>
        {
            var v = (float)py / (size - 1);
            for (var px = 0; px < size; px++)
            {
                var u = (float)px / (size - 1);
                var wu = (tileX + u) * uvScale;
                var wv = (tileY + v) * uvScale;
                SplatBake.SampleLinear(bl, wu, wv, size,
                    out var r, out var g, out var b);
                if (cl is { } cc)
                {
                    SplatBake.SampleLinear(cc, wu * 0.6f, wv * 0.6f,
                        size, out var cr, out var cg, out var cb);
                    r = r + (r * cr * 1.7f - r) * causticMix;
                    g = g + (g * cg * 1.7f - g) * causticMix;
                    b = b + (b * cb * 1.7f - b) * causticMix;
                }
                VColor(vcol, gridW, gridH, u, v, out var vr, out var vg,
                    out var vb, out var va);
                var i = (py * size + px) * 4;
                o[i] = SplatBake.EncodeSrgb(r * tintR * vr);
                o[i + 1] = SplatBake.EncodeSrgb(g * tintG * vg);
                o[i + 2] = SplatBake.EncodeSrgb(b * tintB * vb);
                o[i + 3] = (byte)(Math.Clamp(tintA * va, 0f, 1f) * 255f + 0.5f);
            }
        });
        return o;
    }

    private static void VColor(float[] c, int gw, int gh, float u,
        float v, out float r, out float g, out float b, out float a)
    {
        var fx = Math.Clamp(u, 0f, 1f) * (gw - 1);
        var fy = Math.Clamp(v, 0f, 1f) * (gh - 1);
        var x0 = Math.Min((int)fx, gw - 2);
        var y0 = Math.Min((int)fy, gh - 2);
        var tx = fx - x0;
        var ty = fy - y0;
        r = g = b = a = 0f;
        for (var j = 0; j < 4; j++)
        {
            var xx = x0 + (j & 1);
            var yy = y0 + (j >> 1);
            var w = ((j & 1) == 0 ? 1 - tx : tx) *
                    ((j >> 1) == 0 ? 1 - ty : ty);
            var i = (yy * gw + xx) * 4;
            r += c[i] * w; g += c[i + 1] * w;
            b += c[i + 2] * w; a += c[i + 3] * w;
        }
    }
}
