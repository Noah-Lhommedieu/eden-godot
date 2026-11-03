using System.Text;

namespace EdenClient.Data;

/// <summary>
/// Parser générique pour les tables de données Eden (.ini).
///
/// Formats supportés après décodage MyCrypt :
///   - Pipe :   "|V.x|N|\r\n" puis lignes "field|field|...|\r\n"
///   - Comma :  "V.x,N,\r\n" puis lignes "field,field,...,\r\n"
///   - Count :  "N|\r\n&lt;index colonnes&gt;|\r\n&lt;rows&gt;" (fichiers T_*)
///
/// Le header indique la version du format et le nombre de colonnes.
///
/// Port de <c>$EDEN/server/src/Eden.Core/Data/Parsers/IniTable.cs</c>
/// avec une amélioration côté client : le format Count accumule les
/// lignes jusqu'à N+1 cellules — les champs texte des T_* contiennent
/// des CRLF embarqués (preuve : <c>t_npc.ini</c> « &lt;Father
/// Christmas&gt;\nSanta », <c>t_item.ini</c> descriptions multi-lignes,
/// <c>T_Class.ini</c> « Class Requirement:\nBeginning Job »). Le port
/// serveur éclatait ligne par ligne car il ne lisait que la colonne
/// nom ; le client a besoin du texte complet.
/// </summary>
public sealed class IniTable
{
    /// <summary>Version du format (« V.x » pour pipe/comma, « N| »
    /// brut pour Count).</summary>
    public string Version { get; }

    /// <summary>Nombre de colonnes déclaré par le header (0 si absent).</summary>
    public int ColumnCount { get; }

    /// <summary>Séparateur du fichier ('|' ou ',').</summary>
    public char Separator { get; }

    /// <summary>Lignes de données — chaque row est le tableau des
    /// cellules éclatées sur le séparateur.</summary>
    public IReadOnlyList<string[]> Rows { get; }

    private IniTable(string version, int columnCount, char separator,
        IReadOnlyList<string[]> rows)
    {
        Version = version;
        ColumnCount = columnCount;
        Separator = separator;
        Rows = rows;
    }

    /// <summary>Parse un fichier .ini (déjà décodé ou en clair).</summary>
    public static IniTable Parse(byte[] data, Encoding? encoding = null)
    {
        encoding ??= EdenEncoding.Big5;
        var text = encoding.GetString(data);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return ParseLines(lines);
    }

    /// <summary>Parse un fichier .ini en appliquant MyCrypt si nécessaire
    /// (header "&lt;encode&gt;"). Les .ini extraits des pkg Reforged sont
    /// en clair — DecodeFile les retourne tels quels.
    ///
    /// Encodage : sniff UTF-8 strict — les variantes localisées
    /// (lang/&lt;xx&gt;/) sont en UTF-8 ; un fichier qui décode en
    /// UTF-8 sans faute est lu en UTF-8 (ASCII pur inclus — même
    /// résultat qu'en Big5). Seul un flux réellement Big5 (octets
    /// invalides en UTF-8) retombe sur
    /// <see cref="EdenEncoding.TableFallback"/> : CP1252 pour les
    /// <c>t_*.ini</c> EDENFR, sinon <paramref name="encoding"/> / Big5.</summary>
    public static IniTable ParseFile(string path, Encoding? encoding = null)
    {
        // Forme bakée préférée si présente (&lt;f&gt;.ini.inib — voir
        // TableBin / edenpack bake-tables) : pas de parse ni de décodage au
        // premier accès ; le repli .ini reste toujours disponible.
        var inib = Vfs.EdenVfs.ReadAllBytes(path + ".inib");
        if (inib != null)
        {
            var b = TableBin.Read(inib);
            return new IniTable(b.Version, b.ColCount, b.Sep, b.Rows);
        }
        var data = MyCrypt.DecodeFile(path);
        Encoding enc;
        try
        {
            new UTF8Encoding(false, true).GetCharCount(data);
            enc = Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            enc = EdenEncoding.TableFallback(path, encoding);
        }
        return Parse(data, enc);
    }

    private static IniTable ParseLines(string[] lines)
    {
        if (lines.Length == 0)
            return new IniTable(string.Empty, 0, '|', Array.Empty<string[]>());

        var header = lines[0];

        // Format "|V.x|N|"
        if (header.StartsWith("|V.", StringComparison.Ordinal))
        {
            var parts = header.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var version = parts.Length > 0 ? parts[0] : string.Empty;
            var cols = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
            return BuildRows(lines, version, cols, '|');
        }

        // Format "V.x,N," ou "V.x|N|"
        if (header.StartsWith("V.", StringComparison.Ordinal))
        {
            var sep = header.Contains(',') ? ',' : '|';
            var parts = header.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            var version = parts.Length > 0 ? parts[0] : string.Empty;
            var cols = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
            return BuildRows(lines, version, cols, sep);
        }

        // Format "N|\r\n<colonnes>|\r\n<rows>" (fichiers T_*)
        if (int.TryParse(header.TrimEnd('|'), out var colCount))
        {
            var rows = new List<string[]>();
            var pending = new List<string>();
            // lines[1] = index des colonnes, rows à partir de lines[2]
            for (var i = 2; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]) && pending.Count == 0)
                    continue;
                if (IsRecordBoundary(pending, lines[i], '|'))
                {
                    rows.Add(NormalizeCountRow(
                        string.Join("\r\n", pending).Split('|'), colCount));
                    pending.Clear();
                }
                pending.Add(lines[i]);

                // Même règle que BuildRows : un record est complet quand
                // il atteint colCount+1 cellules (le '|' final laisse
                // une cellule vide). Les champs texte T_* contiennent
                // des CRLF — la ligne seule ne suffit pas toujours.
                var joined = string.Join("\r\n", pending);
                var cells = joined.Split('|');
                if (colCount > 0 && cells.Length < colCount + 1)
                    continue; // record incomplet — attend la ligne suivante

                rows.Add(NormalizeCountRow(cells, colCount));
                pending.Clear();
            }

            if (pending.Count > 0)
                rows.Add(NormalizeCountRow(
                    string.Join("\r\n", pending).Split('|'), colCount));

            return new IniTable(header, colCount, '|', rows);
        }

        return new IniTable(header, 0, '|', Array.Empty<string[]>());
    }

    /// <summary>Un record débute par «&lt;id&gt;&lt;sep&gt;» : la ligne
    /// commence par des chiffres ASCII suivis du séparateur.</summary>
    private static bool IsRecordStart(string line, char sep)
    {
        var i = 0;
        while (i < line.Length && line[i] >= '0' && line[i] <= '9') i++;
        return i > 0 && i < line.Length && line[i] == sep;
    }

    /// <summary>Frontière de record : la ligne entrante débute un record
    /// alors que l'accumulation courante (déjà entamée par un début de
    /// record) n'a pas atteint le nombre de colonnes déclaré. Les tables
    /// natives omettent les colonnes vides de fin de ligne (ex.
    /// <c>c_item.ini</c> : lignes de 35 cellules pour 78 déclarées) —
    /// sans ce test, chaque ligne courte absorbait le record suivant
    /// (≈50 % des items invisibles pour <c>ItemTable</c>).</summary>
    private static bool IsRecordBoundary(
        List<string> pending, string nextLine, char sep)
        => pending.Count > 0
           && IsRecordStart(pending[0], sep)
           && IsRecordStart(nextLine, sep);

    /// <summary>Normalise une row Count à exactement colCount+1 cellules :
    /// si un champ texte contenait un '|' embarqué (cellules en trop),
    /// l'excédent est fusionné dans le DERNIER champ — best-effort, le
    /// texte reste sans perte (les ids/noms courts en tête sont fiables).
    /// </summary>
    private static string[] NormalizeCountRow(string[] cells, int colCount)
    {
        if (colCount <= 0 || cells.Length <= colCount + 1)
            return cells;
        var merged = new string[colCount + 1];
        Array.Copy(cells, merged, colCount);
        merged[colCount] = string.Join('|',
            cells[colCount..]).TrimEnd('|');
        return merged;
    }

    private static IniTable BuildRows(string[] lines, string version,
        int cols, char sep)
    {
        var rows = new List<string[]>();
        var pending = new List<string>();

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) && pending.Count == 0)
                continue;
            if (IsRecordBoundary(pending, lines[i], sep))
            {
                rows.Add(string.Join("\r\n", pending).Split(sep));
                pending.Clear();
            }
            pending.Add(lines[i]);

            // Les champs texte peuvent contenir des CRLF embarqués (ex:
            // descriptions multi-lignes dans C_Class.ini). Un record est
            // complet quand son nombre de champs atteint cols+1 (le
            // trailing sep laisse un champ vide).
            var joined = string.Join("\r\n", pending);
            if (cols > 0 && joined.Split(sep).Length < cols + 1)
                continue; // record incomplet — attend la ligne suivante

            rows.Add(joined.Split(sep));
            pending.Clear();
        }

        if (pending.Count > 0)
            rows.Add(string.Join("\r\n", pending).Split(sep));

        return new IniTable(version, cols, sep, rows);
    }

    /// <summary>Retourne une ligne par son ID (colonne 0).</summary>
    public string[]? FindById(int id)
    {
        var idStr = id.ToString();
        foreach (var row in Rows)
            if (row.Length > 0 && row[0] == idStr)
                return row;
        return null;
    }

    /// <summary>Parse un champ int (0 si vide/invalide).</summary>
    public static int GetInt(string[] row, int col) =>
        col < row.Length && int.TryParse(row[col], out var v) ? v : 0;

    /// <summary>Parse un champ long (0 si vide/invalide).</summary>
    public static long GetLong(string[] row, int col) =>
        col < row.Length && long.TryParse(row[col], out var v) ? v : 0;

    /// <summary>Retourne un champ string (vide si hors bornes).</summary>
    public static string GetStr(string[] row, int col) =>
        col < row.Length ? row[col] : string.Empty;

    /// <summary>Parse un champ float invariant (0 si vide/invalide).</summary>
    public static float GetFloat(string[] row, int col) =>
        col < row.Length && float.TryParse(row[col],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : 0f;
}
