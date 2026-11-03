using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace EdenClient.World;

/// <summary>
/// Parseur du format <c>.fsm</c> (terrain + herbe + objets d'une map
/// Eden Eternal, moteur lapis/HighLand). Spécification :
/// <c>docs/FSM-FORMAT.md</c> — validée par hex sur S009 (V20100824) et
/// S001 (V20100329), corrections du 24/10/2026 incluses :
///   - record LAY0 +0x0C = kind de tuile (0 terrain, 1 eau) ;
///   - records kind=0 d'abord (x-lent / y-rapide : index =
///     ix*tilesPerRow+iy), records kind=1 ensuite (mêmes origines) ;
///   - positions de vertices = coordonnées monde ABSOLUES ;
///   - GRL = u32 typeId + char modelName[40].
///
/// Classe PURE (aucune dépendance Godot) — testable dans NetTests.
/// Le fichier est chargé en mémoire une fois (~15-20 Mo) et les tuiles
/// sont des VUES paresseuses sur le buffer : vertices et splatmaps ne
/// sont pas matérialisés en objets managés.
/// </summary>
public sealed class FsmFile
{
    /// <summary>Taille de l'en-tête de fichier (« FSM V20xxxxxxx »).</summary>
    public const int HeaderSize = 0x58;
    /// <summary>Tag + size (size inclut les 8 o de l'en-tête).</summary>
    public const int ChunkHeaderSize = 8;
    /// <summary>En-tête LAY0 complet (chunk header + tilesX/Y/scale/count).</summary>
    public const int Lay0HeaderSize = 0x18;
    /// <summary>Taille d'un record tuile.</summary>
    public const int TileRecordSize = 0x1abf4;
    /// <summary>Stride d'un vertex {pos3, normal3, color4}.</summary>
    public const int VertexStride = 0x28;
    /// <summary>Offset des vertices dans un record tuile.</summary>
    public const int TileVerticesOffset = 0x10;
    /// <summary>Offset de la splatmap 128×128 RGBA dans un record.</summary>
    public const int TileSplatOffset = 0xaa58;
    /// <summary>Octets de la splatmap.</summary>
    public const int SplatSize = 128 * 128 * 4;
    /// <summary>Offset du nom du DarkMap (lightmap) de la tuile : chaîne
    /// NUL-terminée de 0x20 o (« 22DarkMap » → Dark/22DarkMap.dds), le
    /// « gap » qui suit la splatmap (rf_game FUN_00b02100 : concatène
    /// « Dark » + ce nom).</summary>
    public const int TileDarkMapOffset = 0x1aa58;
    /// <summary>Offset du tableau des 5 TexSlots dans un record.</summary>
    public const int TileTexSlotsOffset = 0x1aa78;
    /// <summary>Nombre de TexSlots par tuile (0=base, 1-4=blend).</summary>
    public const int TexSlotCount = 5;
    /// <summary>Stride d'un TexSlot.</summary>
    public const int TexSlotStride = 0x4c;
    /// <summary>Taille d'un record GRL (typeId u32 + name[40]).</summary>
    public const int GrassTypeRecordSize = 44;
    /// <summary>Taille d'un record GRS.</summary>
    public const int GrassInstanceRecordSize = 32;
    /// <summary>Taille d'un record OBJ.</summary>
    public const int SceneObjectRecordSize = 92;

    private readonly byte[] _data;
    private readonly List<TerrainTile> _tiles;
    private readonly Dictionary<(int X, int Y), TerrainTile> _terrainTiles;

    private FsmFile(byte[] data, string version, uint formatRevision,
        TerrainHeader header, List<TerrainTile> tiles,
        IReadOnlyList<GrassType> grassTypes,
        IReadOnlyList<GrassInstance> grassInstances,
        IReadOnlyList<SceneObject> sceneObjects)
    {
        _data = data;
        Version = version;
        FormatRevision = formatRevision;
        Header = header;
        _tiles = tiles;
        _terrainTiles = new Dictionary<(int, int), TerrainTile>();
        foreach (var t in tiles)
            if (t.Kind == TileKindTerrain)
                _terrainTiles[(t.TileX, t.TileY)] = t;
        WeldSplatEdges();
        GrassTypes = grassTypes;
        GrassInstances = grassInstances;
        SceneObjects = sceneObjects;
    }

    /// <summary>A6.4 : les texels de bord de deux splatmaps voisines
    /// divergent (15 paires sur Tranquill Hill) → ligne de texture à la
    /// frontière de tuile. On remplace les deux rangées/colonnes
    /// jumelles par leur moyenne (col 127 de (x,y) ↔ col 0 de (x+1,y) ;
    /// row 127 de (x,y) ↔ row 0 de (x,y+1) ; row 0 = y local bas).
    /// Modifie le buffer en place, une fois au chargement.</summary>
    private void WeldSplatEdges()
    {
        const int N = 128;
        foreach (var ((tx, ty), a) in _terrainTiles)
        {
            if (_terrainTiles.TryGetValue((tx + 1, ty), out var r))
                for (var i = 0; i < N; i++)
                    AvgTexel(a.SplatSpan, (i * N + (N - 1)) * 4,
                        r.SplatSpan, (i * N) * 4);
            if (_terrainTiles.TryGetValue((tx, ty + 1), out var u))
                for (var i = 0; i < N; i++)
                    AvgTexel(a.SplatSpan, ((N - 1) * N + i) * 4,
                        u.SplatSpan, i * 4);
        }
    }

    private static void AvgTexel(Span<byte> a, int ia, Span<byte> b, int ib)
    {
        for (var c = 0; c < 4; c++)
        {
            var m = (byte)((a[ia + c] + b[ib + c] + 1) >> 1);
            a[ia + c] = m;
            b[ib + c] = m;
        }
    }

    /// <summary>Tag de version ASCII (« V20100329 » / « V20100824 »).</summary>
    public string Version { get; }
    /// <summary>u32 @0x54 — révision du format (2 = V20100329,
    /// 3 = V20100824 — vérifié sur 5 maps).</summary>
    public uint FormatRevision { get; }
    /// <summary>En-tête du chunk LAY0.</summary>
    public TerrainHeader Header { get; }
    /// <summary>Tous les records tuile (terrain kind=0 puis eau kind=1).</summary>
    public IReadOnlyList<TerrainTile> Tiles => _tiles;
    /// <summary>Types d'herbe (chunk GRL).</summary>
    public IReadOnlyList<GrassType> GrassTypes { get; }
    /// <summary>Instances d'herbe (chunk GRS).</summary>
    public IReadOnlyList<GrassInstance> GrassInstances { get; }
    /// <summary>Objets de scène (chunk OBJ).</summary>
    public IReadOnlyList<SceneObject> SceneObjects { get; }

    /// <summary>Nombre de tuiles par rangée — dérivé de
    /// <c>(tilesX-1)/(gridW-1)</c> (9 pour S009, 10 pour S001).</summary>
    public int TilesPerRow { get; private set; }
    /// <summary>Taille monde d'une tuile = (gridW-1)*cellSize (64 u).</summary>
    public float TileWorldSize { get; private set; }

    /// <summary>Octets non framés en fin de fichier (variante EDENFR V7 —
    /// voir <c>docs/flax/V7-TAIL-NOTES.md</c> ; 0 sur les fsm à
    /// chunks).</summary>
    public int TrailingBytes { get; private set; }

    /// <summary>Offset dans le buffer où commence la queue non framée.</summary>
    private int _trailingOffset;

    /// <summary>Vue brute sur la queue non framée (vide hors variantes V7) —
    /// l'analyse M4 consomme ce span sans recopier le fichier.</summary>
    public ReadOnlySpan<byte> TrailingSpan =>
        _data.AsSpan(_trailingOffset, TrailingBytes);

    /// <summary>Tuile de terrain (kind=0) couvrant la cellule grille
    /// (ix, iy), ou null (eau seule / hors grille).</summary>
    public TerrainTile? GetTerrainTile(int ix, int iy) =>
        _terrainTiles.TryGetValue((ix, iy), out var t) ? t : null;

    /// <summary>Charge un fichier .fsm en mémoire.</summary>
    public static FsmFile Load(string path) => Parse(
        Vfs.EdenVfs.ReadAllBytes(path)
        ?? throw new FileNotFoundException(path));

    /// <summary>Parse un buffer .fsm.</summary>
    public static FsmFile Parse(byte[] data)
    {
        if (data.Length < HeaderSize ||
            data[0] != 'F' || data[1] != 'S' || data[2] != 'M' || data[3] != ' ')
            throw new InvalidDataException("Pas un fichier FSM (magic absent)");
        var version = ReadAscii(data, 4, 9).TrimEnd('\0');
        var revision = BinaryPrimitives.ReadUInt32LittleEndian(
            data.AsSpan(0x54));

        TerrainHeader header = default;
        var tiles = new List<TerrainTile>();
        var grassTypes = new List<GrassType>();
        var grassInstances = new List<GrassInstance>();
        var sceneObjects = new List<SceneObject>();
        var tilesPerRow = 0;
        var tileWorldSize = 0f;
        var trailingBytes = 0;
        var trailingOffset = 0;

        var pos = HeaderSize;
        while (pos + ChunkHeaderSize <= data.Length)
        {
            var tag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(pos + 4));
            if (size < ChunkHeaderSize || pos + size > data.Length)
            {
                // Tag connu + taille invalide = corruption réelle ;
                // tag inconnu = queue V7 EDENFR (ci-dessous).
                if (tag is TagLay0 or TagGrl or TagGrs or TagObj)
                    throw new InvalidDataException(
                        $"Chunk 0x{tag:x8} @0x{pos:x} : size 0x{size:x} invalide");
                // Variante EDENFR V7 : ~15 fsm (s002, s003, s004, s005,
                // s010, s011, s021, s030, s032, s051, s054, s119, s225,
                // s307, s501) ont une queue brute non framée après LAY0
                // — équivalent GRL/GRS/OBJ sérialisé sans tags (le même
                // fsm Reforged a les chunks à la même taille). Terrain
                // chargé ; la queue est conservée pour décodage M4.
                trailingBytes = data.Length - pos;
                trailingOffset = pos;
                break;
            }
            switch (tag)
            {
                case TagLay0:
                    header = ParseLay0(data, pos, size, tiles);
                    // tilesPerRow déduit du 1er record : vertices absolus.
                    if (tiles.Count > 0)
                    {
                        var cellsPerTile = tiles[0].GridW - 1;
                        tilesPerRow = cellsPerTile > 0
                            ? (int)((header.GridVerticesX - 1) / cellsPerTile)
                            : 0;
                        tileWorldSize = cellsPerTile * tiles[0].CellSize;
                    }
                    break;
                case TagGrl:
                    ParseCounted(data, pos, size, GrassTypeRecordSize,
                        grassTypes, GrassType.Read);
                    break;
                case TagGrs:
                    ParseCounted(data, pos, size, GrassInstanceRecordSize,
                        grassInstances, GrassInstance.Read);
                    break;
                case TagObj:
                    ParseCounted(data, pos, size, SceneObjectRecordSize,
                        sceneObjects, SceneObject.Read);
                    break;
                // chunks inconnus : skippés (spec §2)
            }
            pos += size;
        }

        var fsm = new FsmFile(data, version, revision, header, tiles,
            grassTypes, grassInstances, sceneObjects)
        {
            TilesPerRow = tilesPerRow,
            TileWorldSize = tileWorldSize,
            TrailingBytes = trailingBytes,
            _trailingOffset = trailingOffset,
        };
        return fsm;
    }

    // ------------------------------------------------------------------

    private const uint TagLay0 = 0x3059414c; // "LAY0"
    private const uint TagGrl = 0x204c5247;  // "GRL "
    private const uint TagGrs = 0x20535247;  // "GRS "
    private const uint TagObj = 0x204a424f;  // "OBJ "

    /// <summary>Kind de tuile : terrain.</summary>
    public const uint TileKindTerrain = 0;
    /// <summary>Kind de tuile : eau (splat nulle, textures
    /// water_env/waterani — vérifié S009 : 53/53).</summary>
    public const uint TileKindWater = 1;

    private static TerrainHeader ParseLay0(byte[] data, int pos, int size,
        List<TerrainTile> tiles)
    {
        var h = new TerrainHeader
        {
            GridVerticesX = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(pos + 0x08)),
            GridVerticesY = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(pos + 0x0c)),
            Scale = BinaryPrimitives.ReadSingleLittleEndian(
                data.AsSpan(pos + 0x10)),
            RecordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(pos + 0x14)),
        };
        var expected = Lay0HeaderSize + (long)h.RecordCount * TileRecordSize;
        if (expected != size)
            throw new InvalidDataException(
                $"LAY0 : {h.RecordCount} records attendus, size=0x{size:x} " +
                $"≠ 0x{expected:x}");
        // Variante EDENFR V7 : le RecordCount englobe des blocs NON-tuile
        // (darkmaps/props — ex. ~222 Ko après la 28e tuile de S002) qui
        // décalent la grille des records. On avance à la fouille : un
        // record tuile valide consomme 0x1abf4 o, sinon on saute 4 o.
        // Les tuiles vérifiées sont 33×33, cellule == Scale, origine
        // multiple de 64 dans les bornes monde (49 tuiles S002 : 48
        // terrain + 1 eau) — les records 21×21 et autres contenus des
        // blocs sont ignorés (lot props M4).
        var rec = pos + Lay0HeaderSize;
        var end = pos + size;
        // Grille régulière (fsm non V7 — cas général, tuiles d'eau
        // comprises) : lecture séquentielle historique. La fouille
        // ci-dessous, plus stricte (33×33, origine alignée), écartait
        // les tuiles d'eau de S001.
        if (IsRegularGrid(data, rec, h.RecordCount))
        {
            for (var k = 0; k < h.RecordCount; k++, rec += TileRecordSize)
                tiles.Add(new TerrainTile(k, rec, data));
            return h;
        }
        var i = 0;
        while (rec + 0x10 <= end)
        {
            if (IsTileRecord(data, rec, h))
            {
                tiles.Add(new TerrainTile(i++, rec, data));
                rec += TileRecordSize;
            }
            else
            {
                rec += 4;
            }
        }
        return h;
    }

    /// <summary>Vrai si <paramref name="off"/> pointe sur un en-tête de
    /// record tuile plausible : grille 33×33, cellule == Scale du chunk,
    /// kind ∈ {0,1} et vertex 0 (origine absolue) aligné sur 64 dans les
    /// bornes monde. Écarte les blocs non-tuile des fsm V7 EDENFR.</summary>
    /// <summary>Vrai si chaque record à pas fixe a un en-tête de grille
    /// plausible (dimensions 1..256, kind terrain/eau) — faux sur les
    /// fsm V7 EDENFR dont les blocs non-tuile décalent la grille.</summary>
    private static bool IsRegularGrid(byte[] data, int first, int count)
    {
        for (var k = 0; k < count; k++)
        {
            var s = data.AsSpan(first + k * TileRecordSize);
            var gw = BinaryPrimitives.ReadUInt32LittleEndian(s);
            var gh = BinaryPrimitives.ReadUInt32LittleEndian(s[4..]);
            var kind = BinaryPrimitives.ReadUInt32LittleEndian(s[0x0c..]);
            if (gw is 0 or > 256 || gh is 0 or > 256 || kind > TileKindWater)
                return false;
        }
        return true;
    }

    private static bool IsTileRecord(byte[] data, int off, in TerrainHeader h)
    {
        var s = data.AsSpan(off);
        var gw = BinaryPrimitives.ReadUInt32LittleEndian(s);
        var gh = BinaryPrimitives.ReadUInt32LittleEndian(s[4..]);
        var cell = BinaryPrimitives.ReadSingleLittleEndian(s[8..]);
        var kind = BinaryPrimitives.ReadUInt32LittleEndian(s[0x0c..]);
        if (gw != 33 || gh != 33 || kind > TileKindWater ||
            MathF.Abs(cell - h.Scale) > 0.01f)
            return false;
        var ox = BinaryPrimitives.ReadSingleLittleEndian(s[0x10..]);
        var oy = BinaryPrimitives.ReadSingleLittleEndian(s[0x14..]);
        const float TileSpan = 64f;
        return ox >= 0 && oy >= 0 && ox <= h.WorldSizeX &&
               oy <= h.WorldSizeY &&
               MathF.Abs(ox % TileSpan) < 0.001f &&
               MathF.Abs(oy % TileSpan) < 0.001f;
    }

    /// <summary>Lecteur d'un record sur span (Func ne prend pas de
    /// ref struct en paramètre générique).</summary>
    private delegate T RecordReader<out T>(ReadOnlySpan<byte> s);

    private static void ParseCounted<T>(byte[] data, int pos, int size,
        int recordSize, List<T> dest, RecordReader<T> read)
    {
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            data.AsSpan(pos + 8));
        var expected = ChunkHeaderSize + 4 + (long)count * recordSize;
        if (expected != size)
            throw new InvalidDataException(
                $"Chunk @0x{pos:x} : count={count} → size 0x{expected:x} " +
                $"≠ 0x{size:x}");
        var off = pos + ChunkHeaderSize + 4;
        for (var i = 0; i < count; i++, off += recordSize)
            dest.Add(read(data.AsSpan(off, recordSize)));
    }

    /// <summary>Chaîne ASCII NUL-terminée dans un buffer fixe.</summary>
    public static string ReadAscii(byte[] data, int off, int maxLen)
    {
        var end = off;
        var limit = Math.Min(off + maxLen, data.Length);
        while (end < limit && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, off, end - off);
    }
}

/// <summary>En-tête du chunk LAY0.</summary>
public struct TerrainHeader
{
    /// <summary>Vertices de la grille monde en X (289/321) —
    /// = tilesPerRow*(gridW-1)+1, PAS un nombre de tuiles.</summary>
    public uint GridVerticesX;
    /// <summary>Idem en Y.</summary>
    public uint GridVerticesY;
    /// <summary>Cellule de la grille (2.0 — identique au cellSize des records).</summary>
    public float Scale;
    /// <summary>Nombre de records tuile (134 S009 / 177 S001).</summary>
    public int RecordCount;
    /// <summary>Largeur monde ≈ (GridVerticesX-1)*Scale (576 u S009).</summary>
    public float WorldSizeX => (GridVerticesX - 1) * Scale;
    /// <summary>Hauteur monde ≈ (GridVerticesY-1)*Scale.</summary>
    public float WorldSizeY => (GridVerticesY - 1) * Scale;
}

/// <summary>
/// Vue paresseuse sur un record tuile (0x1abf4 o) — aucune copie :
/// vertices et splatmap restent dans le buffer du fichier.
/// </summary>
public sealed class TerrainTile
{
    private byte[] _data;
    private readonly int _offset;

    internal TerrainTile(int index, int offset, byte[] data)
    {
        Index = index;
        _offset = offset;
        _data = data;
        var s = data.AsSpan(offset);
        GridW = (int)BinaryPrimitives.ReadUInt32LittleEndian(s);
        GridH = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[4..]);
        CellSize = BinaryPrimitives.ReadSingleLittleEndian(s[8..]);
        Kind = BinaryPrimitives.ReadUInt32LittleEndian(s[0x0c..]);
        var v0 = GetVertex(0, 0);
        OriginX = v0.Position.X;
        OriginY = v0.Position.Y;
    }

    /// <summary>Index du record dans le chunk LAY0.</summary>
    public int Index { get; }
    /// <summary>Vertices en X (33 constaté).</summary>
    public int GridW { get; }
    /// <summary>Vertices en Y (33).</summary>
    public int GridH { get; }
    /// <summary>Espacement des vertices (2.0).</summary>
    public float CellSize { get; }
    /// <summary>0 = terrain, 1 = eau.</summary>
    public uint Kind { get; }
    /// <summary>Origine monde X (multiple de 64).</summary>
    public float OriginX { get; }
    /// <summary>Origine monde Y (multiple de 64).</summary>
    public float OriginY { get; }
    /// <summary>Index grille X de la tuile = round(OriginX / TileWorldSize).</summary>
    public int TileX => (int)MathF.Round(OriginX / ((GridW - 1) * CellSize));
    /// <summary>Index grille Y.</summary>
    public int TileY => (int)MathF.Round(OriginY / ((GridH - 1) * CellSize));

    /// <summary>Octets bruts du bloc vertices (GridW*GridH*0x28).</summary>
    public ReadOnlySpan<byte> VertexBytes => _data.AsSpan(
        _offset + FsmFile.TileVerticesOffset,
        GridW * GridH * FsmFile.VertexStride);
    /// <summary>Octets bruts de la splatmap 128×128 RGBA.</summary>
    public ReadOnlySpan<byte> SplatBytes => _data.AsSpan(
        _offset + FsmFile.TileSplatOffset, FsmFile.SplatSize);

    /// <summary>Nom (sans extension) de la lightmap « DarkMap » de la
    /// tuile, ou chaîne vide.</summary>
    public string DarkMapName
    {
        get
        {
            var s = _data.AsSpan(_offset + FsmFile.TileDarkMapOffset, 0x20);
            var n = s.IndexOf((byte)0);
            return System.Text.Encoding.ASCII.GetString(
                n < 0 ? s : s[..n]);
        }
    }

    internal Span<byte> SplatSpan => _data.AsSpan(
        _offset + FsmFile.TileSplatOffset, FsmFile.SplatSize);

    /// <summary>Vertex (ix, iy) — pos monde absolue, normale, couleur.</summary>
    public TerrainVertex GetVertex(int ix, int iy)
    {
        var off = _offset + FsmFile.TileVerticesOffset +
                  (iy * GridW + ix) * FsmFile.VertexStride;
        var s = _data.AsSpan(off, FsmFile.VertexStride);
        return new TerrainVertex
        {
            Position = new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(s),
                BinaryPrimitives.ReadSingleLittleEndian(s[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[8..])),
            Normal = new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(s[0x0c..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[0x10..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[0x14..])),
            Color = new Vector4(
                BinaryPrimitives.ReadSingleLittleEndian(s[0x18..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[0x1c..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[0x20..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[0x24..])),
        };
    }

    /// <summary>TexSlot i (0-4) — {used u8, nom char[67], u32, uvScale f32}.</summary>
    public TexSlot GetTexSlot(int i)
    {
        var off = _offset + FsmFile.TileTexSlotsOffset +
                  i * FsmFile.TexSlotStride;
        return new TexSlot
        {
            Used = _data[off] != 0,
            Name = FsmFile.ReadAscii(_data, off + 1, 0x43),
            Field44 = BinaryPrimitives.ReadUInt32LittleEndian(
                _data.AsSpan(off + 0x44)),
            UvScale = BinaryPrimitives.ReadSingleLittleEndian(
                _data.AsSpan(off + 0x48)),
        };
    }

    /// <summary>Hauteur bilinéaire aux coords monde (x, y) — le point
    /// doit être dans la tuile. NaN si hors limites locales.</summary>
    public float GetHeightLocal(float x, float y)
    {
        var lx = (x - OriginX) / CellSize;
        var ly = (y - OriginY) / CellSize;
        var x0 = (int)MathF.Floor(lx);
        var y0 = (int)MathF.Floor(ly);
        if (x0 < 0 || y0 < 0 || x0 + 1 >= GridW || y0 + 1 >= GridH)
        {
            // Bord pile : clamper sur le vertex le plus proche.
            var cx = Math.Clamp((int)MathF.Round(lx), 0, GridW - 1);
            var cy = Math.Clamp((int)MathF.Round(ly), 0, GridH - 1);
            return GetVertex(cx, cy).Position.Z;
        }
        var fx = lx - x0;
        var fy = ly - y0;
        var z00 = GetVertex(x0, y0).Position.Z;
        var z10 = GetVertex(x0 + 1, y0).Position.Z;
        var z01 = GetVertex(x0, y0 + 1).Position.Z;
        var z11 = GetVertex(x0 + 1, y0 + 1).Position.Z;
        return z00 * (1 - fx) * (1 - fy) + z10 * fx * (1 - fy) +
               z01 * (1 - fx) * fy + z11 * fx * fy;
    }
}

/// <summary>Vertex terrain : position monde, normale, couleur RGBA.</summary>
public struct TerrainVertex
{
    public Vector3 Position;
    /// <summary>Attr #2 — normale unitaire (|n|≈1 vérifié).</summary>
    public Vector3 Normal;
    /// <summary>Attr #3 — éclairage baké RGBA (confirmé : valeurs
    /// 0.4-1.0 non blanches sur 40+ maps — ombres portées/teinte
    /// froide B≥G≥R ; sur les tuiles eau, alpha ~0.3-1.0 = fade de
    /// rive).</summary>
    public Vector4 Color;
}

/// <summary>Slot de texture d'une tuile (0=base, 1-4=couches blend).</summary>
public struct TexSlot
{
    /// <summary>Slot utilisé.</summary>
    public bool Used;
    /// <summary>Nom du fichier (« mount_04.dds », « waterani_normal.ifl ») —
    /// résolu sous map/texture/.</summary>
    public string Name;
    /// <summary>u32 @+0x44 — toujours 0 sur les maps inspectées.</summary>
    public uint Field44;
    /// <summary>Facteur de tiling de la couche.</summary>
    public float UvScale;
}

/// <summary>Type d'herbe — record GRL (44 o) : typeId + nom de modèle.
/// Correction 24/10 : PAS « 8o handle + 32o params », c'est un nom
/// NUL-terminé de 40 o (« UE704M.nif », « UE704MN.nif »).</summary>
public readonly struct GrassType
{
    /// <summary>TypeId — jointure avec GrassInstance.TypeId.</summary>
    public uint TypeId { get; }
    /// <summary>Modèle NIF de l'herbe (char[40]).</summary>
    public string ModelName { get; }

    private GrassType(uint typeId, string modelName)
    {
        TypeId = typeId;
        ModelName = modelName;
    }

    internal static GrassType Read(ReadOnlySpan<byte> s) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(s),
        CStrings.ReadCString(s[4..]));
}

/// <summary>Instance d'herbe — record GRS (32 o).
/// Champs après position : scale + 3 paramètres dont la sémantique
/// exacte n'est pas prouvée (rotation/direction ? — spec §5).</summary>
public readonly struct GrassInstance
{
    /// <summary>TypeId → GrassType.</summary>
    public uint TypeId { get; }
    /// <summary>Position monde (x, y sol ; z hauteur).</summary>
    public Vector3 Position { get; }
    /// <summary>Champ f32 @+0x10 (~1.0-1.7 — échelle plausible).</summary>
    public float Scale { get; }
    /// <summary>u32 @+0x14 — 0 sur tous les échantillons.</summary>
    public uint Flags { get; }
    /// <summary>f32 @+0x18 (rotation/direction ? — non prouvé).</summary>
    public float ParamA { get; }
    /// <summary>f32 @+0x1c (rotation/direction ? — non prouvé).</summary>
    public float ParamB { get; }

    private GrassInstance(uint typeId, Vector3 pos, float scale,
        uint flags, float a, float b)
    {
        TypeId = typeId;
        Position = pos;
        Scale = scale;
        Flags = flags;
        ParamA = a;
        ParamB = b;
    }

    internal static GrassInstance Read(ReadOnlySpan<byte> s) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(s),
        new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(s[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(s[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(s[0x0c..])),
        BinaryPrimitives.ReadSingleLittleEndian(s[0x10..]),
        BinaryPrimitives.ReadUInt32LittleEndian(s[0x14..]),
        BinaryPrimitives.ReadSingleLittleEndian(s[0x18..]),
        BinaryPrimitives.ReadSingleLittleEndian(s[0x1c..]));
}

/// <summary>Objet de scène — record OBJ (92 o) : position, matrice 3×3
/// row-major, échelle, code modèle (→ map/model/object/&lt;name&gt;.nif).</summary>
public readonly struct SceneObject
{
    /// <summary>Position monde (x, y sol ; z hauteur).</summary>
    public Vector3 Position { get; }
    /// <summary>Rotation 3×3 row-major (m[row*3+col]) — monde Z-up.</summary>
    public Matrix3x3 Rotation { get; }
    /// <summary>Échelle uniforme (0 → 1.0).</summary>
    public float Scale { get; }
    /// <summary>Code modèle char[40] (« UE013 », « UB124 »).</summary>
    public string Name { get; }

    private SceneObject(Vector3 pos, Matrix3x3 rot, float scale, string name)
    {
        Position = pos;
        Rotation = rot;
        Scale = scale;
        Name = name;
    }

    internal static SceneObject Read(ReadOnlySpan<byte> s)
    {
        var rot = new Matrix3x3(
            F32(s, 0x0c), F32(s, 0x10), F32(s, 0x14),
            F32(s, 0x18), F32(s, 0x1c), F32(s, 0x20),
            F32(s, 0x24), F32(s, 0x28), F32(s, 0x2c));
        var scale = F32(s, 0x30);
        if (scale == 0f) scale = 1f;
        return new SceneObject(
            new Vector3(F32(s, 0), F32(s, 4), F32(s, 8)), rot, scale,
            CStrings.ReadCString(s[0x34..0x5c]));
    }

    private static float F32(ReadOnlySpan<byte> s, int off) =>
        BinaryPrimitives.ReadSingleLittleEndian(s[off..]);
}

/// <summary>Matrice 3×3 row-major minimale (pure, pas de dépendance).</summary>
public readonly struct Matrix3x3
{
    public readonly float M11, M12, M13, M21, M22, M23, M31, M32, M33;

    public Matrix3x3(float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
    {
        M11 = m11; M12 = m12; M13 = m13;
        M21 = m21; M22 = m22; M23 = m23;
        M31 = m31; M32 = m32; M33 = m33;
    }

    /// <summary>Axe local +X/Y/Z transformé — les axes sont les colonnes
    /// de la matrice row-major (<c>world = rotation * local</c>).</summary>
    public Vector3 Column0 => new(M11, M21, M31);
    public Vector3 Column1 => new(M12, M22, M32);
    public Vector3 Column2 => new(M13, M23, M33);

    /// <summary>Yaw autour de +Z (monde Z-up) si matrice = rotation pure
    /// dans le plan — atan2(m21, m11).</summary>
    public float YawZ => MathF.Atan2(M21, M11);

    /// <summary>Applique la matrice row-major à un point local.</summary>
    public Vector3 Transform(Vector3 v) =>
        Column0 * v.X + Column1 * v.Y + Column2 * v.Z;
}

file static class CStrings
{
    /// <summary>Chaîne ASCII NUL-terminée dans un buffer fixe (span).</summary>
    public static string ReadCString(ReadOnlySpan<byte> s)
    {
        var end = s.IndexOf((byte)0);
        if (end < 0) end = s.Length;
        return Encoding.ASCII.GetString(s[..end]);
    }
}
