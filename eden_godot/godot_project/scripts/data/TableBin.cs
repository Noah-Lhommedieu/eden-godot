using System.Buffers.Binary;
using System.Text;

namespace EdenClient.Data;

/// <summary>
/// Format binaire baké « ITB1 » pour les tables .ini — produit par
/// <c>edenpack bake-tables</c>, relu en quasi-zéro-parse au premier chargement.
///
/// Layout (little-endian) :
/// <code>
///   u32 magic "ITB1"      ; u32 version (=1)
///   u32 rowCount          ; u32 colCount     (header ini, 0 si absent)
///   u8  sep ('|' ou ',')  ; u8  reserved[7]
///   u32 versionStrLen + utf8   (chaîne Version du header ini)
///   u32 indexOff          ; u32 indexSize (= rowCount * 12)
///   [rows]   row := u32 cellCount + cellCount × (u32 utf8len + utf8)
///   [index]  rowCount × (u64 keyHash FNV-1a de cellule[0] + u32 rowOff)
///            trié par (keyHash, rowOff) — les rowOff croissants
///            préservent l'ordre d'itération ini entre ids identiques.
/// </code>
///
/// Toutes les chaînes sont en UTF-8 : le décodage Big5/UTF-8 est fait
/// UNE FOIS au bake (<see cref="IniTable.ParseFile"/>), le runtime ne
/// décode plus jamais.
///
/// <see cref="Read"/> ne désérialise que les métadonnées + l'index
/// (~1 Mo pour 80k rows) — les cellules sont relues à la demande, donc
/// un FindById précoce ne parse pas la table entière.
/// </summary>
public static class TableBin
{
    /// <summary>Version du format ITB1.</summary>
    public const uint FormatVersion = 1;

    // "ITB1" lu comme u32 little-endian ('I'=0x49 en poids faible).
    private const uint MagicLE = 0x31425449;

    // Garde-fou corruption : aucune table réelle ne dépasse ~100
    // colonnes ; au-delà = fichier corrompu, pas une allocation légitime.
    private const int MaxCellCount = 4096;

    /// <summary>Écrit <paramref name="table"/> au format ITB1.</summary>
    public static void Write(Stream output, IniTable table)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        var versionBytes = Encoding.UTF8.GetBytes(table.Version ?? string.Empty);
        w.Write(MagicLE);
        w.Write(FormatVersion);
        w.Write(table.Rows.Count);
        w.Write(table.ColumnCount);
        w.Write((byte)table.Separator);
        w.Write(new byte[7]);
        w.Write(versionBytes.Length);
        w.Write(versionBytes);
        var indexPosPos = ms.Position;   // patché une fois l'index écrit
        w.Write(0u); // indexOff
        w.Write(0u); // indexSize

        var entries = new List<(ulong hash, uint off)>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var key = row.Length > 0 ? row[0] : string.Empty;
            entries.Add((Fnv1a(Encoding.UTF8.GetBytes(key ?? string.Empty)),
                (uint)ms.Position));
            w.Write(row.Length);
            foreach (var cell in row)
            {
                var b = Encoding.UTF8.GetBytes(cell ?? string.Empty);
                w.Write(b.Length);
                w.Write(b);
            }
        }

        entries.Sort(static (a, b) => a.hash != b.hash
            ? a.hash.CompareTo(b.hash) : a.off.CompareTo(b.off));
        var indexOff = (uint)ms.Position;
        foreach (var e in entries)
        {
            w.Write(e.hash);
            w.Write(e.off);
        }
        var indexSize = (uint)(ms.Position - indexOff);

        ms.Position = indexPosPos;
        w.Write(indexOff);
        w.Write(indexSize);
        w.Flush();

        ms.Position = 0;
        ms.CopyTo(output);
        output.Flush();
    }

    /// <summary>
    /// Lit un .inib : valide magic/version/bornes, charge métadonnées +
    /// index. Les cellules restent dans le buffer et ne sont décodées
    /// qu'à l'itération de <see cref="Baked.Rows"/> ou à FindById.
    /// Un fichier corrompu lève <see cref="InvalidDataException"/>.
    /// </summary>
    public static Baked Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 36 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != MagicLE)
            throw Corrupt("magic ITB1 absent ou fichier tronqué");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (version != FormatVersion)
            throw Corrupt($"version ITB{version} non supportée");

        var rowCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        var colCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        var sep = (char)data[16];
        var vlen = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24));
        if (rowCount < 0 || colCount < 0 || vlen < 0)
            throw Corrupt("header négatif");
        if (28L + vlen + 8 > data.Length)
            throw Corrupt("chaîne de version tronquée");
        var versionStr = Encoding.UTF8.GetString(data, 28, vlen);

        var pos = 28 + vlen;
        var indexOff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
        var indexSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
        var dataStart = (uint)(pos + 8);
        if (indexSize != (long)rowCount * 12 ||
            indexOff < dataStart || (long)indexOff + indexSize > data.Length)
            throw Corrupt("index hors bornes");

        var entries = new (ulong hash, uint off)[rowCount];
        var p = (int)indexOff;
        for (var i = 0; i < rowCount; i++)
        {
            var h = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(p));
            var o = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 8));
            // Chaque row a au moins son u32 cellCount avant l'index.
            if (o < dataStart || o + 4 > indexOff)
                throw Corrupt("rowOff hors bornes");
            entries[i] = (h, o);
            p += 12;
        }
        Array.Sort(entries, static (a, b) => a.hash != b.hash
            ? a.hash.CompareTo(b.hash) : a.off.CompareTo(b.off));

        return new Baked(data, versionStr, colCount, sep, entries, (int)indexOff);
    }

    private static InvalidDataException Corrupt(string detail) =>
        new($"fichier .inib corrompu : {detail}");

    /// <summary>FNV-1a 64 bits sur les octets UTF-8 de la clé.</summary>
    internal static ulong Fnv1a(ReadOnlySpan<byte> s)
    {
        var h = 14695981039346656037UL;
        foreach (var b in s)
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h;
    }

    /// <summary>
    /// Table bakée chargée en mémoire — métadonnées + index, cellules
    /// relues paresseusement depuis le buffer d'origine.
    /// </summary>
    public sealed class Baked
    {
        private readonly byte[] _data;
        private readonly ulong[] _hashes;     // triés (recherche binaire)
        private readonly uint[] _offs;        // parallèle à _hashes
        private readonly uint[] _rowOffsets;  // triés par offset = ordre ini
        private readonly int _dataEnd;        // indexOff — borne des rows

        internal Baked(byte[] data, string version, int colCount, char sep,
            (ulong hash, uint off)[] sorted, int dataEnd)
        {
            _data = data;
            _dataEnd = dataEnd;
            Version = version;
            ColCount = colCount;
            Sep = sep;
            _hashes = new ulong[sorted.Length];
            _offs = new uint[sorted.Length];
            _rowOffsets = new uint[sorted.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                _hashes[i] = sorted[i].hash;
                _offs[i] = sorted[i].off;
                _rowOffsets[i] = sorted[i].off;
            }
            Array.Sort(_rowOffsets);
            Rows = new LazyRows(this);
        }

        /// <summary>Version du header ini (« V.x », « N| »…).</summary>
        public string Version { get; }

        /// <summary>Nombre de colonnes déclaré (0 si absent).</summary>
        public int ColCount { get; }

        /// <summary>Séparateur d'origine ('|' ou ',').</summary>
        public char Sep { get; }

        /// <summary>Rows dans l'ordre ini — relues à la demande.</summary>
        public IReadOnlyList<string[]> Rows { get; }

        /// <summary>Ligne par id (colonne 0) — comme IniTable.FindById.</summary>
        public string[]? FindById(int id) => FindById(id.ToString());

        /// <summary>
        /// Recherche binaire sur l'index ; en cas de hash identiques,
        /// balaye dans l'ordre ini et vérifie la clé réelle (collision
        /// FNV) — le premier match correspond au FindById linéaire.
        /// </summary>
        public string[]? FindById(string id)
        {
            var h = Fnv1a(Encoding.UTF8.GetBytes(id));
            var i = Array.BinarySearch(_hashes, h);
            if (i < 0) return null;
            while (i > 0 && _hashes[i - 1] == h) i--; // premier du groupe
            for (; i < _hashes.Length && _hashes[i] == h; i++)
            {
                var row = ReadRow(_offs[i]);
                if ((row.Length > 0 ? row[0] : string.Empty) == id)
                    return row;
            }
            return null;
        }

        /// <summary>Relit une row depuis le buffer (borné par l'index).</summary>
        internal string[] ReadRow(uint off)
        {
            var pos = (int)off;
            var cellCount =
                BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(pos));
            pos += 4;
            if (cellCount < 0 || cellCount > MaxCellCount)
                throw Corrupt($"cellCount {cellCount} invraisemblable @0x{off:x}");
            var row = new string[cellCount];
            for (var c = 0; c < cellCount; c++)
            {
                if (pos + 4 > _dataEnd)
                    throw Corrupt("cellule tronquée");
                var len = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(pos));
                pos += 4;
                if (len < 0 || pos + len > _dataEnd)
                    throw Corrupt("cellule hors bornes");
                row[c] = Encoding.UTF8.GetString(_data, pos, len);
                pos += len;
            }
            return row;
        }

        private sealed class LazyRows : IReadOnlyList<string[]>
        {
            private readonly Baked _b;
            internal LazyRows(Baked b) => _b = b;
            public int Count => _b._rowOffsets.Length;
            public string[] this[int i] => _b.ReadRow(_b._rowOffsets[i]);

            public IEnumerator<string[]> GetEnumerator()
            {
                for (var i = 0; i < Count; i++)
                    yield return this[i];
            }

            System.Collections.IEnumerator
                System.Collections.IEnumerable.GetEnumerator() =>
                GetEnumerator();
        }
    }
}
