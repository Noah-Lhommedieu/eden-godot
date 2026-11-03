namespace EdenClient.World;

/// <summary>
/// A* 8-directions sur la grille .smp dilatée (<see cref="NodeMapFile.
/// Dilated"/>), cases de 0.5 u. Classe PURE (testable hors Godot).
/// Coordonnées Eden (x, y) en entrée/sortie ; waypoints = centres de
/// case <c>(cx+0.5)/2</c>. Heuristique octile (admissible en 8-dir ;
/// le Manhattan du plan surestimerait les diagonales). Pas de coupe
/// de coin en diagonale. Chemin lissé par ligne de vue sur la grille.
/// </summary>
public static class Pathfinder
{
    /// <summary>Budget d'expansion (cases) avant abandon.</summary>
    public const int MaxExpand = 600_000;

    /// <summary>Chemin de (sx,sy) à (ex,ey) ; waypoints successifs
    /// SANS le départ, le dernier = destination (ou case libre la plus
    /// proche si la destination est bloquée). Null si aucun chemin.
    /// </summary>
    public static List<(float X, float Y)>? Find(NodeMapFile map,
        float sx, float sy, float ex, float ey,
        ISet<int>? extraBlocked = null)
    {
        var cols = map.Cols; var rows = map.Rows;
        var grid = map.Dilated();
        if (extraBlocked is { Count: > 0 })
        {
            // Obstacles dynamiques (cases où le perso s'est coincé) :
            // copie locale, la grille partagée reste intacte.
            grid = (bool[])grid.Clone();
            foreach (var c in extraBlocked)
                if (c >= 0 && c < grid.Length) grid[c] = true;
        }
        var s = ClampCell(cols, rows, (int)MathF.Floor(sx * 2f),
            (int)MathF.Floor(sy * 2f));
        var g = ClampCell(cols, rows, (int)MathF.Floor(ex * 2f),
            (int)MathF.Floor(ey * 2f));
        var exact = !grid[g.y * cols + g.x];
        if (!exact)
        {
            // Cible dans un obstacle : case libre la plus proche ET
            // atteignable depuis le départ (même composante connexe).
            var near = NearestReachable(map, grid, s, g, 12);
            if (near == null) return null;
            g = near.Value;
        }
        if (s == g)
            return new() { exact ? (ex, ey) : Center(g.x, g.y) };

        // Départ dans la bande dilatée (collé à un mur) : on laisse
        // sortir par les cases non brutes à ≤ 2 cases du départ.
        var escape = new HashSet<int>();
        if (grid[s.y * cols + s.x])
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    var qx = s.x + dx; var qy = s.y + dy;
                    if (qx >= 0 && qy >= 0 && qx < cols && qy < rows &&
                        !map.IsCellBlocked(qx, qy))
                        escape.Add(qy * cols + qx);
                }

        var n = cols * rows;
        var gs = new float[n];
        Array.Fill(gs, float.MaxValue);
        var from = new int[n];
        var closed = new bool[n];
        var open = new PriorityQueue<int, float>();
        var si = s.y * cols + s.x; var gi = g.y * cols + g.x;
        gs[si] = 0; from[si] = -1;
        open.Enqueue(si, H(s.x, s.y, g.x, g.y));
        var expanded = 0;
        const float D = 1.41421356f;
        bool Blk(int i) => grid[i] && !escape.Contains(i);
        while (open.TryDequeue(out var cur, out _))
        {
            if (closed[cur]) continue;
            closed[cur] = true;
            if (cur == gi) break;
            if (++expanded > MaxExpand) return null;
            var cx = cur % cols; var cy = cur / cols;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = cx + dx; var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows)
                        continue;
                    var ni = ny * cols + nx;
                    if (closed[ni] || (grid[ni] && !escape.Contains(ni)))
                        continue;
                    if (dx != 0 && dy != 0 &&
                        (Blk(cy * cols + nx) || Blk(ny * cols + cx)))
                        continue;   // pas de coupe de coin
                    var t = gs[cur] + (dx != 0 && dy != 0 ? D : 1f);
                    if (t >= gs[ni]) continue;
                    gs[ni] = t; from[ni] = cur;
                    open.Enqueue(ni, t + H(nx, ny, g.x, g.y));
                }
        }
        if (!closed[gi]) return null;

        var cells = new List<(int x, int y)>();
        for (var i = gi; i != -1; i = from[i])
            cells.Add((i % cols, i / cols));
        cells.Reverse();                       // départ → but
        // Lissage : plus loin possible en ligne de vue.
        var path = new List<(int x, int y)>();
        var a = 0;
        while (a < cells.Count - 1)
        {
            var b = cells.Count - 1;
            while (b > a + 1 &&
                   !LineFree(grid, cols, cells[a], cells[b])) b--;
            path.Add(cells[b]);
            a = b;
        }
        var res = new List<(float, float)>(path.Count);
        foreach (var c in path) res.Add(Center(c.x, c.y));
        if (exact) res[^1] = (ex, ey);
        return res;
    }

    /// <summary>Vrai si la case de (ex,ey) est dans la même composante
    /// connexe (grille dilatée) que celle de (sx,sy), ou si une case
    /// libre atteignable existe à ≤ 12 cases de (ex,ey).</summary>
    public static bool Reachable(NodeMapFile map, float sx, float sy,
        float ex, float ey, ISet<int>? extraBlocked = null)
        => Find(map, sx, sy, ex, ey, extraBlocked) != null;

    /// <summary>Indice de case (cy*Cols+cx) contenant (x, y) Eden.</summary>
    public static int CellIndex(NodeMapFile map, float x, float y)
    {
        var c = ClampCell(map.Cols, map.Rows, (int)MathF.Floor(x * 2f),
            (int)MathF.Floor(y * 2f));
        return c.y * map.Cols + c.x;
    }

    /// <summary>Flood-fill 8-dir (sans coupe de coin) depuis le départ ;
    /// retourne la case atteignable la plus proche du but dans le rayon
    /// <paramref name="maxR"/>, null si aucune.</summary>
    private static (int x, int y)? NearestReachable(NodeMapFile map,
        bool[] grid, (int x, int y) s, (int x, int y) g, int maxR)
    {
        var cols = map.Cols; var rows = map.Rows;
        var escape = new HashSet<int>();
        if (grid[s.y * cols + s.x])
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    var qx = s.x + dx; var qy = s.y + dy;
                    if (qx >= 0 && qy >= 0 && qx < cols && qy < rows &&
                        !map.IsCellBlocked(qx, qy))
                        escape.Add(qy * cols + qx);
                }
        bool Blk(int i) => grid[i] && !escape.Contains(i);
        var seen = new bool[cols * rows];
        var q = new Queue<int>();
        var si = s.y * cols + s.x;
        seen[si] = true; q.Enqueue(si);
        (int x, int y)? best = null; var bd = int.MaxValue;
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            var cx = cur % cols; var cy = cur / cols;
            if (!grid[cur] || escape.Contains(cur))
            {
                var ddx = cx - g.x; var ddy = cy - g.y;
                if (Math.Abs(ddx) <= maxR && Math.Abs(ddy) <= maxR)
                {
                    var d = ddx * ddx + ddy * ddy;
                    if (d < bd && !grid[cur]) { bd = d; best = (cx, cy); }
                }
            }
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = cx + dx; var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows)
                        continue;
                    var ni = ny * cols + nx;
                    if (seen[ni] || Blk(ni)) continue;
                    if (dx != 0 && dy != 0 &&
                        (Blk(cy * cols + nx) || Blk(ny * cols + cx)))
                        continue;
                    seen[ni] = true; q.Enqueue(ni);
                }
        }
        return best;
    }

    private static (float, float) Center(int cx, int cy)
        => ((cx + 0.5f) / 2f, (cy + 0.5f) / 2f);

    private static float H(int x, int y, int gx, int gy)
    {
        var dx = Math.Abs(x - gx); var dy = Math.Abs(y - gy);
        return Math.Max(dx, dy) + 0.41421356f * Math.Min(dx, dy);
    }

    private static (int x, int y) ClampCell(int cols, int rows,
        int x, int y)
        => (Math.Clamp(x, 0, cols - 1), Math.Clamp(y, 0, rows - 1));

    private static (int x, int y)? NearestFree(bool[] grid, int cols,
        int rows, int x, int y, int maxR)
    {
        for (var r = 1; r <= maxR; r++)
        {
            (int x, int y)? best = null; var bd = int.MaxValue;
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                        continue;
                    var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows ||
                        grid[ny * cols + nx]) continue;
                    var d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; best = (nx, ny); }
                }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>Segment de cases (Bresenham) sans case dilatée, coins
    /// compris.</summary>
    private static bool LineFree(bool[] grid, int cols,
        (int x, int y) p, (int x, int y) q)
    {
        int x = p.x, y = p.y;
        var dx = Math.Abs(q.x - x); var dy = -Math.Abs(q.y - y);
        var sx = x < q.x ? 1 : -1; var sy = y < q.y ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            if (grid[y * cols + x]) return false;
            if (x == q.x && y == q.y) return true;
            var e2 = 2 * err;
            var px = x; var py = y;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
            if (x != px && y != py &&
                (grid[py * cols + x] || grid[y * cols + px]))
                return false;       // diagonale coupant un coin
        }
    }
}
