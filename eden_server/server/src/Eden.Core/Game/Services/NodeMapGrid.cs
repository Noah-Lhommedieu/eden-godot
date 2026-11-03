using System.Buffers.Binary;
using System.Text;

namespace Eden.Core.Game.Services;

/// <summary>
/// Carte d'obstacles native <c>Data/NodeMap/Sxxx.smp</c> (« murs
/// invisibles »). Même format et même sémantique que le client
/// (<c>eden_godot/godot_project/scripts/world/NodeMapFile.cs</c>) :
/// <c>u16 6 "SMP2.0"</c>, <c>u16 len + nom</c>, <c>u32 0</c>,
/// <c>u16 w, u16 h, u32 n=w*h</c>, puis une grille 2w x 2h de cases de
/// 0,5 u, 2 bits par case (MSB d'abord, ligne = 2w cases) ; bit0 =
/// obstacle, bit1 = couche secondaire non bloquante. Hors grille = libre.
/// </summary>
public sealed class NodeMapGrid
{
    private readonly byte[] _cells;
    private readonly int _cols, _rows;

    private NodeMapGrid(byte[] cells, int w, int h)
    {
        _cells = cells; Width = w; Height = h; _cols = w * 2; _rows = h * 2;
    }

    public int Width { get; }
    public int Height { get; }

    public static NodeMapGrid? Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (IOException) { return null; }
    }

    public static NodeMapGrid? Parse(byte[] d)
    {
        if (d.Length < 12) return null;
        if (BinaryPrimitives.ReadUInt16LittleEndian(d) != 6 ||
            Encoding.ASCII.GetString(d, 2, 6) != "SMP2.0") return null;
        var nl = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(8));
        var o = 10 + nl + 4;
        if (d.Length < o + 8) return null;
        int w = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
        int h = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o + 2));
        var n = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o + 4));
        if (w <= 0 || h <= 0 || n != w * h || d.Length < o + 8 + n)
            return null;
        return new NodeMapGrid(d.AsSpan(o + 8, n).ToArray(), w, h);
    }

    /// <summary>Vrai si (x, y) monde Eden est dans une case obstacle.</summary>
    public bool IsBlocked(float x, float y)
    {
        var cx = (int)MathF.Floor(x * 2f);
        var cy = (int)MathF.Floor(y * 2f);
        if (cx < 0 || cy < 0 || cx >= _cols || cy >= _rows) return false;
        var i = cy * _cols + cx;
        var c = (_cells[i >> 2] >> (6 - 2 * (i & 3))) & 3;
        return c > 0 && (c & 1) != 0;
    }
}
