using System.Text;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Format .smp (murs invisibles) — buffer synthétique.</summary>
public class NodeMapTests
{
    private static byte[] Build(int w, int h, Action<byte[]> fill)
    {
        var cells = new byte[w * h];
        fill(cells);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)6); bw.Write(Encoding.ASCII.GetBytes("SMP2.0"));
        bw.Write((ushort)8); bw.Write(Encoding.ASCII.GetBytes("S999.smp"));
        bw.Write(0);
        bw.Write((ushort)w); bw.Write((ushort)h); bw.Write(cells.Length);
        bw.Write(cells);
        return ms.ToArray();
    }

    [Fact]
    public void BlockedCellMsbFirstAndOutOfRange()
    {
        // 4×4 unités = 8×8 cases = 2 octets par ligne. Ligne 2
        // (y 1.0-1.5), case 1 (x 0.5-1.0) = valeur 1 → octet 0x10.
        var data = Build(4, 4, c => c[2 * 2 + 0] = 0x10);
        var m = NodeMapFile.Parse(data);
        Assert.NotNull(m);
        Assert.True(m!.IsBlocked(0.6f, 1.1f));
        Assert.False(m.IsBlocked(0.1f, 1.1f));
        Assert.False(m.IsBlocked(0.6f, 0.1f));
        Assert.False(m.IsBlocked(-5f, 1f));
    }

    [Fact]
    public void BridgeLayerValue2IsWalkable()
    {
        var m = NodeMapFile.Parse(Build(4, 4, c => c[0] = 0x80))!;
        Assert.Equal(2, m.Cell(0.1f, 0.1f));
        Assert.False(m.IsBlocked(0.1f, 0.1f));
    }
}
