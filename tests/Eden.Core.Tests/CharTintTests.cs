using EdenClient.Data;
using EdenClient.Game;
using EdenClient.Vfs;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// <see cref="CharTint"/> + <see cref="DdsDecode"/> — teinte des
/// masques rouges perso (peau S*/#X000, cheveux #X1xx) et décode
/// BC1/BC3 → RGBA8. Formules calquées sur le CharVisual de référence
/// (out = teinte·(floor+(1−floor)·r/rMax), cheveux plancher 0.7).
/// Les tests corpus sont skippés si client_assets est absent.
/// </summary>
[Xunit.Collection("SharedCoreState")]
public class CharTintTests
{
    // -------------------- classification / formules --------------------

    [Fact]
    public void ForShape_ClassifiesLikeReference()
    {
        Assert.Equal(CharTint.Kind.Skin, CharTint.ForShape("SG202"));
        Assert.Equal(CharTint.Kind.Skin, CharTint.ForShape("SB202"));
        Assert.Equal(CharTint.Kind.Skin, CharTint.ForShape("SL999"));
        Assert.Equal(CharTint.Kind.Hair, CharTint.ForShape("#G100"));
        Assert.Equal(CharTint.Kind.Hair, CharTint.ForShape("#B110"));
        Assert.Equal(CharTint.Kind.None, CharTint.ForShape("#G202"));
        Assert.Equal(CharTint.Kind.None, CharTint.ForShape("#GFACE"));
        Assert.Equal(CharTint.Kind.None, CharTint.ForShape("#G000"));
        Assert.Equal(CharTint.Kind.None, CharTint.ForShape("G202"));
    }

    [Fact]
    public void BodyCode_InverseOfBodyLetter()
    {
        Assert.Equal(0, CharTint.BodyCode('g'));
        Assert.Equal(1, CharTint.BodyCode('b'));
        Assert.Equal(2, CharTint.BodyCode('m'));
        Assert.Equal(4, CharTint.BodyCode('f'));
        Assert.Equal(6, CharTint.BodyCode('k'));
        Assert.Equal(8, CharTint.BodyCode('l'));
        Assert.Equal(9, CharTint.BodyCode('c'));
        Assert.Equal(0, CharTint.BodyCode('z'));   // repli « g »
    }

    [Fact]
    public void IsOutfit_Pattern()
    {
        Assert.True(CharTint.IsOutfit("#G202"));
        Assert.True(CharTint.IsOutfit("#B2110"));
        Assert.False(CharTint.IsOutfit("#G100"));
        Assert.False(CharTint.IsOutfit("SG202"));
    }

    [Fact]
    public void FaceCandidates_OrderAndFallback()
    {
        // Peau 0 : fichier sans lettre préféré (artwork complet),
        // puis variante A — port de FaceTexture référence.
        var c0 = CharTint.FaceTextureCandidates('g', 0, 0);
        Assert.Equal(new[] { "GFACES01.dds", "GFACES01A.dds" }, c0);
        // Peau 1 → variante B (masque) puis replis.
        var c1 = CharTint.FaceTextureCandidates('g', 0, 1);
        Assert.Equal(
            new[] { "GFACES01B.dds", "GFACES01.dds", "GFACES01A.dds" },
            c1);
        // Style 3 → FACES04.
        Assert.Equal("BFACES04.dds",
            CharTint.FaceTextureCandidates('b', 3, 0)[0]);
    }

    [Fact]
    public void ArtworkForMask_SwapsSuffix()
    {
        Assert.Equal("G202AS01A.dds",
            CharTint.ArtworkForMask("G202AS01B.dds"));
        Assert.Equal("G2100S01A.DDS",
            CharTint.ArtworkForMask("G2100S01B.DDS"));
        Assert.Null(CharTint.ArtworkForMask("GX01AS01A.dds"));
    }

    // -------------------- teinte RGBA8 --------------------

    [Fact]
    public void Apply_NormalizesByRedMax()
    {
        // rMax=200 : px0 → k=1 (teinte pleine), px1 → k=0.5.
        var px = new byte[] { 200, 0, 0, 255, 100, 0, 0, 255 };
        CharTint.Apply(px, 0.5f, 0.4f, 0.3f);
        Assert.Equal(new byte[] { 127, 102, 76, 255, 63, 51, 38, 255 },
            px);
    }

    [Fact]
    public void Apply_FloorKeepsShade()
    {
        // Zone noire du masque + plancher 0.7 → ombrage teinté (cap
        // interne des cheveux), pas noir pur.
        var px = new byte[] { 0, 0, 0, 255 };
        CharTint.Apply(px, 1f, 1f, 1f, CharTint.HairFloor);
        Assert.Equal(178, px[0]);
        Assert.Equal(255, px[3]);   // alpha préservé
    }

    // -------------------- décode BC --------------------

    /// <summary>Bloc BC1 4×4 : c0/c1 RGB565 + 16 indices 2 bits.</summary>
    private static byte[] Bc1Block(ushort c0, ushort c1, uint lut)
    {
        var b = new byte[8];
        BitConverter.GetBytes(c0).CopyTo(b, 0);
        BitConverter.GetBytes(c1).CopyTo(b, 2);
        BitConverter.GetBytes(lut).CopyTo(b, 4);
        return b;
    }

    [Fact]
    public void Bc1_SolidRed()
    {
        var dds = new byte[0x80 + 8];
        BitConverter.GetBytes(0x20534444u).CopyTo(dds, 0);
        BitConverter.GetBytes(124u).CopyTo(dds, 0x04);
        BitConverter.GetBytes(4).CopyTo(dds, 0x0c);     // h
        BitConverter.GetBytes(4).CopyTo(dds, 0x10);     // w
        BitConverter.GetBytes(1).CopyTo(dds, 0x1c);     // mips
        BitConverter.GetBytes(32u).CopyTo(dds, 0x4c);
        BitConverter.GetBytes(4u).CopyTo(dds, 0x50);    // FOURCC
        BitConverter.GetBytes(0x31545844u).CopyTo(dds, 0x54); // DXT1
        Bc1Block(0xF800, 0, 0).CopyTo(dds, 0x80);       // rouge 565
        Assert.True(DdsInfo.TryParse(dds, out var info));
        var rgba = DdsDecode.Mip0ToRgba8(dds, info);
        Assert.NotNull(rgba);
        Assert.Equal(64, rgba!.Length);
        for (var i = 0; i < 64; i += 4)
        {
            Assert.Equal(255, rgba[i]);
            Assert.Equal(0, rgba[i + 1]);
            Assert.Equal(0, rgba[i + 2]);
            Assert.Equal(255, rgba[i + 3]);
        }
    }

    [Fact]
    public void Bc1_OneBitAlpha()
    {
        // c0 ≤ c1 → mode 1-bit : index 3 = transparent.
        var dds = new byte[0x80 + 8];
        BitConverter.GetBytes(0x20534444u).CopyTo(dds, 0);
        BitConverter.GetBytes(124u).CopyTo(dds, 0x04);
        BitConverter.GetBytes(4).CopyTo(dds, 0x0c);
        BitConverter.GetBytes(4).CopyTo(dds, 0x10);
        BitConverter.GetBytes(1).CopyTo(dds, 0x1c);
        BitConverter.GetBytes(32u).CopyTo(dds, 0x4c);
        BitConverter.GetBytes(4u).CopyTo(dds, 0x50);
        BitConverter.GetBytes(0x31545844u).CopyTo(dds, 0x54);
        // c0=0x0000 (noir), c1=0xF800 (rouge), tous index 3.
        Bc1Block(0, 0xF800, 0xFFFFFFFF).CopyTo(dds, 0x80);
        Assert.True(DdsInfo.TryParse(dds, out var info));
        var rgba = DdsDecode.Mip0ToRgba8(dds, info);
        Assert.NotNull(rgba);
        for (var i = 0; i < 64; i += 4)
            Assert.Equal(0, rgba![i + 3]);
    }

    [Fact]
    public void Mips_ChainHalves()
    {
        var rgba = new byte[4 * 4 * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 200; rgba[i + 1] = 100;
            rgba[i + 2] = 50; rgba[i + 3] = 255;
        }
        var chain = DdsDecode.RgbaMips(rgba, 4, 4);
        Assert.Equal(3, chain.Count);          // 4 → 2 → 1
        Assert.Equal((4, 4), (chain[0].W, chain[0].H));
        Assert.Equal((2, 2), (chain[1].W, chain[1].H));
        Assert.Equal((1, 1), (chain[2].W, chain[2].H));
        Assert.Equal(200, chain[2].Data[0]);   // moyenne uniforme
    }

    // -------------------- vraies données corpus --------------------

    private static string? FindCorpus()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_CORPUS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "client_assets");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    private static byte[]? LoadRgba(string rel)
    {
        var root = FindCorpus();
        if (root == null) return null;
        var p = EdenVfs.ResolveInsensitive(root, rel);
        if (p == null) return null;
        var bytes = File.ReadAllBytes(p);
        return DdsInfo.TryParse(bytes, out var info)
            ? DdsDecode.Mip0ToRgba8(bytes, info)
            : null;
    }

    [Fact]
    public void RealSkinMask_IsRedDominant_ThenTints()
    {
        var rgba = LoadRgba("char/texture/gx01as01a.dds");
        if (rgba == null) return;
        long r = 0, g = 0, b = 0;
        var n = rgba.Length / 4;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2];
        }
        // Masque peau = canal R (rouge quasi pur — vérifié fr-full).
        Assert.True(r > 4 * Math.Max(g, b),
            $"gx01as01a attendu masque rouge, moyennes r={r / n} " +
            $"g={g / n} b={b / n}");
        // Teinte starter (sel 0 → id 201 SKIN_TONE_1 si la table est
        // résolue, carnation sinon) : plus rouge→peau, pas rouge.
        // CharColorTable fige sa table au premier appel — on lie le
        // resolver corpus AVANT pour ne pas polluer CharColorTests
        // (même collection SharedCoreState, ordre non garanti).
        var corpus = FindCorpus()!;
        TextResolver.ResolveFile =
            NetTests.IniTableTests.CaseInsensitiveResolver(corpus);
        var tint = CharTint.Color(CharTint.Kind.Skin,
            CharTint.BodyCode('g'), 0);
        CharTint.Apply(rgba, tint.R, tint.G, tint.B);
        long tr = 0, tg = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            tr += rgba[i]; tg += rgba[i + 1];
        }
        Assert.True(tg > tr / 2,
            $"après teinte attendu g≈0.7·r, moyennes r={tr / n} " +
            $"g={tg / n}");
    }

    [Fact]
    public void RealHairMask_Decodes()
    {
        var rgba = LoadRgba("char/texture/G1H00S01.dds");
        if (rgba == null) return;
        Assert.Equal(1024 * 1024 * 4, rgba.Length);
    }

    [Fact]
    public void RealBc3Mask_Decodes()
    {
        // fx01as01a (Anuran) = DXT3 — couvre le chemin BC3.
        var rgba = LoadRgba("char/texture/fx01as01a.dds");
        if (rgba == null) return;
        Assert.Equal(1024 * 1024 * 4, rgba.Length);
    }
}
