using EdenClient.Vfs;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// <see cref="DdsInfo"/> sur les vraies textures S002
/// (<c>map/texture/s0024102.dds</c> = DXT1 128×128,
/// <c>s0024104.dds</c> = DXT5 128×128 — vérifié par hex) + headers
/// synthétiques. Skippés si le corpus est absent (même pattern que
/// <see cref="MapChainM3Tests"/>).
/// </summary>
public class DdsInfoTests
{
    private static string? FindCorpus()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
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

    /// <summary>Header DDS minimal valide (fourCC donné).</summary>
    private static byte[] MakeHeader(int w, int h, uint fourCc,
        int mips = 1)
    {
        var d = new byte[0x80];
        BitConverter.GetBytes(0x20534444u).CopyTo(d, 0);   // "DDS "
        BitConverter.GetBytes(124u).CopyTo(d, 0x04);       // header size
        BitConverter.GetBytes(h).CopyTo(d, 0x0c);
        BitConverter.GetBytes(w).CopyTo(d, 0x10);
        BitConverter.GetBytes(mips).CopyTo(d, 0x1c);
        BitConverter.GetBytes(32u).CopyTo(d, 0x4c);        // ddspf size
        BitConverter.GetBytes(4u).CopyTo(d, 0x50);         // DDPF_FOURCC
        BitConverter.GetBytes(fourCc).CopyTo(d, 0x54);
        return d;
    }

    [Fact]
    public void S0024102_Dxt1_128()
    {
        var root = FindCorpus();
        if (root == null) return;
        var p = EdenVfs.ResolveInsensitive(root,
            "map/texture/s0024102.dds");
        Assert.NotNull(p);
        var ok = DdsInfo.TryParse(File.ReadAllBytes(p!), out var dds);
        Assert.True(ok);
        Assert.Equal(128, dds.Width);
        Assert.Equal(128, dds.Height);
        Assert.Equal(DdsInfo.DdsFormat.Bc1, dds.Format);
        Assert.Equal(0x80, dds.DataOffset);
        // 128×128 DXT1 = 8192 o de payload mip 0 (8320 - 128 header).
        Assert.Equal(8192, dds.MipSize(128, 128));
    }

    [Fact]
    public void S0024104_Dxt5_128()
    {
        var root = FindCorpus();
        if (root == null) return;
        var p = EdenVfs.ResolveInsensitive(root,
            "map/texture/s0024104.dds");
        Assert.NotNull(p);
        var ok = DdsInfo.TryParse(File.ReadAllBytes(p!), out var dds);
        Assert.True(ok);
        Assert.Equal(128, dds.Width);
        Assert.Equal(128, dds.Height);
        Assert.Equal(DdsInfo.DdsFormat.Bc3, dds.Format);
        // 128×128 DXT5 = 16384 o de payload mip 0 (16512 - 128).
        Assert.Equal(16384, dds.MipSize(128, 128));
    }

    [Fact]
    public void Synthetic_MipsOffsets()
    {
        var d = MakeHeader(64, 32, 0x35545844, mips: 4);   // DXT5
        Assert.True(DdsInfo.TryParse(d, out var dds));
        Assert.Equal(64, dds.Width);
        Assert.Equal(32, dds.Height);
        Assert.Equal(4, dds.MipLevels);
        Assert.Equal(DdsInfo.DdsFormat.Bc3, dds.Format);
        // mip0 = 16×8 blocs ×16 = 2048 ; mip1 = 8×4×16 = 512 …
        Assert.Equal(2048, dds.MipSize(64, 32));
        Assert.Equal(0x80, dds.MipOffset(0));
        Assert.Equal(0x80 + 2048, dds.MipOffset(1));
        Assert.Equal(0x80 + 2048 + 512, dds.MipOffset(2));
        Assert.Equal(256, dds.RowPitch(64)); // 16 blocs × 16 o
    }

    [Fact]
    public void Synthetic_Rejets()
    {
        Assert.False(DdsInfo.TryParse(new byte[0x40], out _));
        var d = MakeHeader(64, 32, 0x35545844);
        d[0] = (byte)'X';                                 // magic cassé
        Assert.False(DdsInfo.TryParse(d, out _));
        d = MakeHeader(64, 32, 0x35545844);
        BitConverter.GetBytes(0x31544e41u).CopyTo(d, 0x54); // "ANT1" inconnu
        Assert.False(DdsInfo.TryParse(d, out _));
        // RGBA 32 bits, masque R = 0x000000ff → Rgba32.
        d = MakeHeader(4, 4, 0);
        BitConverter.GetBytes(0x41u).CopyTo(d, 0x50);      // RGB|ALPHAPIXELS
        BitConverter.GetBytes(32u).CopyTo(d, 0x58);
        BitConverter.GetBytes(0x000000ffu).CopyTo(d, 0x5c);
        Assert.True(DdsInfo.TryParse(d, out var dds));
        Assert.Equal(DdsInfo.DdsFormat.Rgba32, dds.Format);
        Assert.Equal(64, dds.MipSize(4, 4));
    }
}
