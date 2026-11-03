using System.Text;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>A* sur la grille .smp dilatée (carte synthétique 20×20 u).</summary>
public class PathfinderTests
{
    private const int W = 20;
    private const int Cols = W * 2;

    private static NodeMapFile Map(Func<int, int, bool> obstacle)
    {
        var cells = new byte[W * W];
        for (var y = 0; y < Cols; y++)
            for (var x = 0; x < Cols; x++)
                if (obstacle(x, y))
                {
                    var i = y * Cols + x;
                    cells[i >> 2] |= (byte)(1 << (6 - 2 * (i & 3)));
                }
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)6); bw.Write(Encoding.ASCII.GetBytes("SMP2.0"));
        bw.Write((ushort)8); bw.Write(Encoding.ASCII.GetBytes("S999.smp"));
        bw.Write(0);
        bw.Write((ushort)W); bw.Write((ushort)W); bw.Write(cells.Length);
        bw.Write(cells);
        return NodeMapFile.Parse(ms.ToArray())!;
    }

    [Fact]
    public void DilationIsOneCellEightNeighbour()
    {
        var m = Map((x, y) => x == 10 && y == 10);
        var d = m.Dilated();
        for (var y = 8; y <= 12; y++)
            for (var x = 8; x <= 12; x++)
                Assert.Equal(Math.Abs(x - 10) <= 1 && Math.Abs(y - 10) <= 1,
                    d[y * Cols + x]);
    }

    [Fact]
    public void OpenField_StraightLine()
    {
        var m = Map((_, _) => false);
        var p = Pathfinder.Find(m, 2f, 2f, 15f, 15f)!;
        Assert.Single(p);
        Assert.Equal((15f, 15f), p[0]);
    }

    [Fact]
    public void Wall_RoutesThroughGap()
    {
        // Mur vertical x=20 (case), y 0..33 ; trou y>=34 (unités 17+).
        var m = Map((x, y) => x == 20 && y < 34);
        var p = Pathfinder.Find(m, 3f, 5f, 17f, 5f)!;
        Assert.NotNull(p);
        Assert.True(p.Count >= 2);
        Assert.Equal((17f, 5f), p[^1]);
        // Aucun waypoint dans une case dilatée ; segments sans mur.
        var d = m.Dilated();
        var prev = (X: 3f, Y: 5f);
        foreach (var w in p)
        {
            Assert.False(d[(int)(w.Y * 2) * Cols + (int)(w.X * 2)]);
            for (var t = 0f; t <= 1f; t += 0.01f)
            {
                var x = prev.X + (w.X - prev.X) * t;
                var y = prev.Y + (w.Y - prev.Y) * t;
                Assert.False(m.IsBlocked(x, y));
            }
            prev = w;
        }
        Assert.Contains(p, w => w.Y >= 16f);   // passe par le trou
    }

    [Fact]
    public void ClosedWall_NoPath()
    {
        var m = Map((x, _) => x == 20);
        Assert.Null(Pathfinder.Find(m, 3f, 5f, 17f, 5f));
    }

    [Fact]
    public void BlockedGoal_SnapsToNearestFree()
    {
        var m = Map((x, y) => x >= 28 && x <= 31 && y >= 28 && y <= 31);
        var p = Pathfinder.Find(m, 2f, 2f, 15f, 15f)!;
        Assert.NotNull(p);
        var d = m.Dilated();
        var l = p[^1];
        Assert.False(d[(int)(l.Y * 2) * Cols + (int)(l.X * 2)]);
    }

    [Fact]
    public void Island_GoalInOtherComponent_NoPath()
    {
        // Anneau fermé autour de (15,15) : intérieur libre mais isolé.
        var m = Map((x, y) =>
            (x == 24 || x == 36 || y == 24 || y == 36) &&
            x >= 24 && x <= 36 && y >= 24 && y <= 36);
        Assert.Null(Pathfinder.Find(m, 2f, 2f, 15f, 15f));
        Assert.False(Pathfinder.Reachable(m, 2f, 2f, 15f, 15f));
        Assert.True(Pathfinder.Reachable(m, 2f, 2f, 5f, 15f));
        // Depuis l'intérieur, l'extérieur est inatteignable aussi.
        Assert.Null(Pathfinder.Find(m, 15f, 15f, 2f, 2f));
    }

    [Fact]
    public void BlockedGoalInsideIsland_NotSnappedAcrossWall()
    {
        // Cible dans un îlot fermé (poche libre au cœur d'un bloc
        // plein) : depuis l'extérieur, aucun snap vers la poche
        // (autre composante) — la case atteignable la plus proche est
        // à l'extérieur du bloc.
        var m = Map((x, y) =>
            (x >= 24 && x <= 36 && y >= 24 && y <= 36) &&
            !(x >= 28 && x <= 32 && y >= 28 && y <= 32) ||
            (x == 30 && y == 30));
        var p = Pathfinder.Find(m, 2f, 2f, 15f, 15f)!;
        Assert.NotNull(p);
        var l = p[^1];
        Assert.False(l.X >= 12f && l.X <= 18f && l.Y >= 12f && l.Y <= 18f);
        // Départ dans la poche : bloc plein autour -> rien d'atteignable.
        Assert.Null(Pathfinder.Find(m, 14.2f, 14.2f, 2f, 2f));
    }

    [Fact]
    public void ExtraBlocked_ForcesReplanAround()
    {
        // Ouvert : ligne droite. Case noire sur la ligne -> contourne
        // (ou autre chemin), jamais par la case noire.
        var m = Map((_, _) => false);
        var direct = Pathfinder.Find(m, 2f, 10f, 17f, 10f)!;
        Assert.Single(direct);
        var bl = new HashSet<int>();
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -1; dx <= 1; dx++)
                bl.Add((20 + dy) * Cols + 20 + dx);
        var p = Pathfinder.Find(m, 2f, 10f, 17f, 10f, bl)!;
        Assert.NotNull(p);
        Assert.True(p.Count >= 2);
        Assert.Equal((17f, 10f), p[^1]);
        var prev = (X: 2f, Y: 10f);
        foreach (var w in p)
        {
            for (var t = 0f; t <= 1f; t += 0.01f)
            {
                var x = prev.X + (w.X - prev.X) * t;
                var y = prev.Y + (w.Y - prev.Y) * t;
                Assert.DoesNotContain(
                    Pathfinder.CellIndex(m, x, y), bl);
            }
            prev = w;
        }
    }

    [Fact]
    public void ExtraBlocked_FullWall_NoPath()
    {
        var m = Map((_, _) => false);
        var bl = new HashSet<int>();
        for (var y = 0; y < Cols; y++) bl.Add(y * Cols + 20);
        Assert.Null(Pathfinder.Find(m, 3f, 5f, 17f, 5f, bl));
        Assert.NotNull(Pathfinder.Find(m, 3f, 5f, 8f, 5f, bl));
    }
}
