using System.Buffers.Binary;
using System.Text;

namespace EdenClient.World;

/// <summary>
/// Carte d'obstacles <c>map/sknodemap/Sxxx.smp</c> (« murs invisibles »
/// natifs). Format établi par reverse (rf_game FUN_0050075x loader +
/// boucle de dilatation @1458900 de decompiled.c) et validé sur les
/// positions PNJ/monstres/renaissance des 290 scènes (95 % en case
/// libre) :
///   u16 len + "SMP2.0" ; u16 len + nom de fichier ; u32 0 ;
///   u16 w, u16 h (taille de la scène en unités monde) ; u32 n ;
///   n octets, n = w·h : grille de 2w × 2h cases (demi-unité), 2 bits
///   par case, MSB d'abord, ligne = 2w cases. Valeur 0 = libre,
///   bit0 (0x40 natif) = obstacle, bit1 (0x80) = couche secondaire
///   (tablier de pont…) non bloquante.
/// Le natif dilate d'1 case les obstacles (8-voisinage, boucle 3x3 de
/// rf_game decompiled.c @1458915-1458940, dans le loader FUN_00500750
/// : chaque case obstacle OU-e les 9 cases voisines dans une 2e grille
/// +0x54) pour le pathfinding : <see cref="Dilated"/> (utilisé par
/// <see cref="Pathfinder"/>). <see cref="IsBlocked"/> teste la case
/// brute (le point central du joueur).
/// Classe PURE (testable hors Godot).
/// </summary>
public sealed class NodeMapFile
{
    private readonly byte[] _cells;
    private readonly int _cols;
    private readonly int _rows;

    private NodeMapFile(byte[] cells, int w, int h)
    {
        _cells = cells;
        Width = w;
        Height = h;
        _cols = w * 2;
        _rows = h * 2;
    }

    /// <summary>Largeur de la scène (unités monde).</summary>
    public int Width { get; }
    /// <summary>Hauteur de la scène (unités monde).</summary>
    public int Height { get; }

    /// <summary>Charge un .smp ; null si le format n'est pas reconnu.</summary>
    public static NodeMapFile? Load(string path)
    {
        try
        {
            var b = Vfs.EdenVfs.ReadAllBytes(path);
            return b == null ? null : Parse(b);
        }
        catch (IOException) { return null; }
    }

    /// <summary>Parse un buffer .smp ; null si invalide.</summary>
    public static NodeMapFile? Parse(byte[] d)
    {
        if (d.Length < 12) return null;
        var l0 = BinaryPrimitives.ReadUInt16LittleEndian(d);
        if (l0 != 6 || Encoding.ASCII.GetString(d, 2, 6) != "SMP2.0")
            return null;
        var nl = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(8));
        var o = 10 + nl + 4;
        if (d.Length < o + 8) return null;
        int w = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
        int h = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o + 2));
        var n = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            d.AsSpan(o + 4));
        if (w <= 0 || h <= 0 || n != w * h || d.Length < o + 8 + n)
            return null;
        return new NodeMapFile(d.AsSpan(o + 8, n).ToArray(), w, h);
    }

    /// <summary>Valeur 2 bits de la case contenant (x, y) monde Eden ;
    /// -1 hors carte.</summary>
    public int Cell(float x, float y)
    {
        var cx = (int)MathF.Floor(x * 2f);
        var cy = (int)MathF.Floor(y * 2f);
        if (cx < 0 || cy < 0 || cx >= _cols || cy >= _rows) return -1;
        var i = cy * _cols + cx;
        return (_cells[i >> 2] >> (6 - 2 * (i & 3))) & 3;
    }

    /// <summary>Vrai si (x, y) monde Eden est un obstacle (mur
    /// invisible). Hors carte = non bloqué (le heightfield borne).</summary>
    public bool IsBlocked(float x, float y)
    {
        var c = Cell(x, y);
        return c > 0 && (c & 1) != 0;
    }

    /// <summary>Colonnes de la grille (2·largeur).</summary>
    public int Cols => _cols;
    /// <summary>Lignes de la grille (2·hauteur).</summary>
    public int Rows => _rows;

    /// <summary>Case brute (indices de case) obstacle ? Hors grille =
    /// false.</summary>
    public bool IsCellBlocked(int cx, int cy)
    {
        if (cx < 0 || cy < 0 || cx >= _cols || cy >= _rows) return false;
        var i = cy * _cols + cx;
        var c = (_cells[i >> 2] >> (6 - 2 * (i & 3))) & 3;
        return c > 0 && (c & 1) != 0;
    }

    private bool[]? _dilated;

    /// <summary>Grille d'obstacles dilatée d'1 case (8-voisinage),
    /// indexée <c>cy*Cols+cx</c> — équivalent de la 2e grille native.
    /// Calculée une fois (paresseux).</summary>
    public bool[] Dilated()
    {
        if (_dilated != null) return _dilated;
        var d = new bool[_cols * _rows];
        for (var y = 0; y < _rows; y++)
            for (var x = 0; x < _cols; x++)
            {
                if (!IsCellBlocked(x, y)) continue;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx; var ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < _cols &&
                            ny < _rows)
                            d[ny * _cols + nx] = true;
                    }
            }
        return _dilated = d;
    }
}
