using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

public class NodeMapGridTests
{
    private static byte[] Build(int w, int h, Action<byte[]> fill)
    {
        var cells = new byte[w * h];
        fill(cells);
        var name = "S999.smp"u8.ToArray();
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((ushort)6); bw.Write("SMP2.0"u8.ToArray());
        bw.Write((ushort)name.Length); bw.Write(name);
        bw.Write(0u); bw.Write((ushort)w); bw.Write((ushort)h);
        bw.Write((uint)cells.Length); bw.Write(cells);
        return ms.ToArray();
    }

    [Fact]
    public void Parse_BlockedCellAndFreeCell()
    {
        // 2x2 -> grille 4x4 ; case (cx=1, cy=0) = obstacle (bit0 = 01).
        var g = NodeMapGrid.Parse(Build(2, 2, c => c[0] = 0b00_01_00_00));
        Assert.NotNull(g);
        Assert.True(g!.IsBlocked(0.6f, 0.2f));
        Assert.False(g.IsBlocked(0.1f, 0.2f));
        Assert.False(g.IsBlocked(5f, 5f));   // hors carte = libre
    }

    [Fact]
    public void Parse_SecondaryLayerDoesNotBlock()
    {
        var g = NodeMapGrid.Parse(Build(2, 2, c => c[0] = 0b10_00_00_00));
        Assert.False(g!.IsBlocked(0.1f, 0.1f));
    }

    [Fact]
    public void Parse_RejectsGarbage() =>
        Assert.Null(NodeMapGrid.Parse(new byte[20]));
}
