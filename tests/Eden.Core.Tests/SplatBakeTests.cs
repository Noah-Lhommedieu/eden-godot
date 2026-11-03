using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

public class SplatBakeTests
{
    private static SplatBake.Layer Flat(byte r, byte g, byte b)
    {
        var d = new byte[4 * 4 * 4];
        for (var i = 0; i < d.Length; i += 4)
        { d[i] = r; d[i + 1] = g; d[i + 2] = b; d[i + 3] = 255; }
        return new SplatBake.Layer(new() { (d, 4, 4) }, 1f);
    }

    private static float[] White(int w, int h)
    {
        var v = new float[w * h * 4];
        Array.Fill(v, 1f);
        return v;
    }

    private static byte[] Splat(byte r, byte g, byte b, byte a)
    {
        var s = new byte[128 * 128 * 4];
        for (var i = 0; i < s.Length; i += 4)
        { s[i] = r; s[i + 1] = g; s[i + 2] = b; s[i + 3] = a; }
        return s;
    }

    private static SplatBake.Layer[] Five() => new[]
    {
        Flat(255, 0, 0), Flat(0, 255, 0), Flat(0, 0, 255),
        Flat(255, 255, 0), Flat(0, 255, 255),
    };

    [Fact]
    public void ZeroSplat_ShowsBaseLayer()
    {
        var o = SplatBake.Bake(Splat(0, 0, 0, 0), 128, White(3, 3), 3, 3,
            Five(), 0, 0, 8);
        Assert.Equal(255, o[0]); Assert.Equal(0, o[1]); Assert.Equal(0, o[2]);
    }

    [Theory]
    [InlineData(255, 0, 0, 0, 0, 255, 0)]   // R -> layer1 (green)
    [InlineData(0, 255, 0, 0, 0, 0, 255)]   // G -> layer2 (blue)
    [InlineData(0, 0, 255, 0, 255, 255, 0)] // B -> layer3
    [InlineData(0, 0, 0, 255, 0, 255, 255)] // A -> layer4
    public void Channel_MapsToSlot(byte r, byte g, byte b, byte a,
        byte er, byte eg, byte eb)
    {
        var o = SplatBake.Bake(Splat(r, g, b, a), 128, White(3, 3), 3, 3,
            Five(), 0, 0, 8);
        Assert.Equal(er, o[0]); Assert.Equal(eg, o[1]); Assert.Equal(eb, o[2]);
    }

    [Fact]
    public void OverSum_IsRenormalized_VertexColorMultiplies()
    {
        var vc = White(3, 3);
        for (var i = 0; i < vc.Length; i++) vc[i] = 0.5f;
        var o = SplatBake.Bake(Splat(255, 255, 0, 0), 128, vc, 3, 3,
            Five(), 0, 0, 8);
        // 50/50 vert+bleu linéaire ×0.5 : valeurs égales sur G et B.
        Assert.Equal(o[1], o[2]);
        Assert.Equal(0, o[0]);
        Assert.InRange(o[1], 100, 140); // 0.25 lin ≈ sRGB 137
    }

    [Fact]
    public void MeshUv_EdgesAreTexelCenters()
    {
        Assert.Equal(0.5f / 512, SplatBake.MeshUv(0, 32, 512), 6);
        Assert.Equal(511.5f / 512, SplatBake.MeshUv(32, 32, 512), 6);
    }
}

public class DdsBc2Tests
{
    [Fact]
    public void Bc2_DecodesAlphaAndColor()
    {
        // 1 bloc : alpha 0xF partout sauf px0 = 0 ; c0=c1=rouge 565.
        var blk = new byte[16];
        for (var i = 0; i < 8; i++) blk[i] = 0xFF;
        blk[0] = 0xF0;
        blk[8] = 0x00; blk[9] = 0xF8; blk[10] = 0x00; blk[11] = 0xF8;
        var dds = new byte[128 + 16];
        System.Text.Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
        BitConverter.GetBytes(124).CopyTo(dds, 4);
        BitConverter.GetBytes(4).CopyTo(dds, 12);
        BitConverter.GetBytes(4).CopyTo(dds, 16);
        BitConverter.GetBytes(1).CopyTo(dds, 28);
        BitConverter.GetBytes(32).CopyTo(dds, 76);
        BitConverter.GetBytes(4).CopyTo(dds, 80);
        System.Text.Encoding.ASCII.GetBytes("DXT3").CopyTo(dds, 84);
        blk.CopyTo(dds, 128);
        Assert.True(DdsInfo.TryParse(dds, out var info));
        var o = DdsDecode.Mip0ToRgba8(dds, info)!;
        Assert.Equal(255, o[0]); Assert.Equal(0, o[1]);
        Assert.Equal(0, o[3]); Assert.Equal(255, o[7]);
    }
}

public class WaterBakeTests
{
    [Fact]
    public void Alpha_FollowsVertexAlpha_AndTint()
    {
        var d = new byte[4 * 4 * 4];
        Array.Fill(d, (byte)255);
        var lay = new SplatBake.Layer(new() { (d, 4, 4) }, 1f);
        var vc = new float[3 * 3 * 4];
        for (var i = 0; i < vc.Length; i += 4)
        { vc[i] = vc[i + 1] = vc[i + 2] = 1f; vc[i + 3] = 0.5f; }
        var o = WaterBake.Bake(lay, null, vc, 3, 3, 0, 0, 8,
            1f, 1f, 1f, 0.68f);
        Assert.Equal((byte)(0.34f * 255 + 0.5f), o[3]);
        Assert.Equal(255, o[0]);
    }
}

public class DdsLum8Tests
{
    [Fact]
    public void Lum8_DecodesToGray()
    {
        var dds = new byte[128 + 4];
        System.Text.Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
        BitConverter.GetBytes(124).CopyTo(dds, 4);
        BitConverter.GetBytes(2).CopyTo(dds, 12);
        BitConverter.GetBytes(2).CopyTo(dds, 16);
        BitConverter.GetBytes(1).CopyTo(dds, 28);
        BitConverter.GetBytes(32).CopyTo(dds, 76);
        BitConverter.GetBytes(0x20000).CopyTo(dds, 80);
        BitConverter.GetBytes(8).CopyTo(dds, 88);
        BitConverter.GetBytes(0xff).CopyTo(dds, 92);
        dds[128] = 10; dds[129] = 20; dds[130] = 30; dds[131] = 40;
        Assert.True(DdsInfo.TryParse(dds, out var info));
        var o = DdsDecode.Mip0ToRgba8(dds, info)!;
        Assert.Equal(30, o[8]); Assert.Equal(30, o[10]); Assert.Equal(255, o[11]);
    }
}
