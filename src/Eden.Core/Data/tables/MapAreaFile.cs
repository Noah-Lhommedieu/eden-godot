namespace EdenClient.Data.Tables;

/// <summary>
/// <c>map/model/Sxxx/Sxxx.area</c> — dimensions de la grille d'une map.
/// Fichier texte de 3 lignes, ex. S001 :
/// <code>
/// 10 10      ← largeur hauteur en cellules
/// 0 0        ← (voir HYPOTHESIS)
/// 10 10      ← (voir HYPOTHESIS)
/// </code>
///
/// PROUVÉ — ligne 1 : le client lit <c>sscanf("%hd %hd")</c> sur la
/// première ligne et stocke <c>w×96.0</c>, <c>h×96.0</c> comme taille
/// monde de la map (rf_game <c>FUN_006eb940</c> @0x006eb940,
/// multiplicateur <c>DAT_00f05424</c> = 96.0f lu en .rdata @0x00f05424).
/// S001 → 10×10 cellules = 960×960 unités monde.
///
/// HYPOTHESIS — lignes 2 et 3 : rectangle de tuiles valides
/// <c>x0 z0</c> / <c>x1 z1</c> (toutes les données réelles ont 0 0 et
/// les dims de la ligne 1 → très probablement la zone remplie de la
/// grille, utilisée pour borner les itérations de tuiles). Le désasm
/// ne lit que la première ligne dans FUN_006eb940 ; un second lecteur
/// des lignes 2/3 n'a pas été localisé — à confirmer.
/// </summary>
public sealed class MapAreaFile
{
    /// <summary>Facteur cellule→unités monde — DAT_00f05424 natif.</summary>
    public const float CellSize = 96.0f;

    /// <summary>Largeur en cellules (ligne 1, champ 1).</summary>
    public int GridWidth { get; init; }
    /// <summary>Hauteur en cellules (ligne 1, champ 2).</summary>
    public int GridHeight { get; init; }
    /// <summary>Coin min du rectangle de tuiles — HYPOTHESIS (ligne 2).</summary>
    public (int X0, int Z0) RectMin { get; init; }
    /// <summary>Coin max du rectangle de tuiles — HYPOTHESIS (ligne 3).</summary>
    public (int X1, int Z1) RectMax { get; init; }

    /// <summary>Largeur monde = GridWidth × <see cref="CellSize"/>.</summary>
    public float WorldWidth => GridWidth * CellSize;
    /// <summary>Hauteur monde = GridHeight × <see cref="CellSize"/>.</summary>
    public float WorldHeight => GridHeight * CellSize;

    /// <summary>Parse un fichier .area (3 lignes « n n », ASCII).</summary>
    public static MapAreaFile Load(string path)
    {
        var lines = Vfs.EdenVfs.ReadAllLines(path);
        return new MapAreaFile
        {
            GridWidth = Field(lines, 0, 0),
            GridHeight = Field(lines, 0, 1),
            RectMin = (Field(lines, 1, 0), Field(lines, 1, 1)),
            RectMax = (Field(lines, 2, 0), Field(lines, 2, 1)),
        };
    }

    /// <summary>Champ `col` de la ligne `line` (« w h »), 0 si absent.</summary>
    private static int Field(string[] lines, int line, int col)
    {
        if (line >= lines.Length) return 0;
        var parts = lines[line].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return col < parts.Length && int.TryParse(parts[col], out var v) ? v : 0;
    }
}
