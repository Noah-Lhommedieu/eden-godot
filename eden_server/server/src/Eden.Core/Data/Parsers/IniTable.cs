using Eden.Core.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser generique pour les tables de donnees Eden (.ini).
///
/// Formats supportes apres decodage MyCrypt:
///   - Pipe:   "|V.x|N|\r\n" puis lignes "field|field|...|\r\n"
///   - Comma:  "V.x,N,\r\n" puis lignes "field,field,...,\r\n"
///   - Count:  "N|\r\n<colonnes>|\r\n<rows>" (fichiers T_*)
///
/// Le header indique la version du format et le nombre de colonnes.
/// </summary>
public sealed class IniTable
{
    static IniTable()
    {
        // Big5/GBK non natifs en .NET Core — provider requis avant tout GetEncoding
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string Version { get; }
    public int ColumnCount { get; }
    public char Separator { get; }
    public IReadOnlyList<string[]> Rows { get; }

    private IniTable(string version, int columnCount, char separator, IReadOnlyList<string[]> rows)
    {
        Version = version;
        ColumnCount = columnCount;
        Separator = separator;
        Rows = rows;
    }

    /// <summary>Parse un fichier .ini (deja decode ou en clair).</summary>
    public static IniTable Parse(byte[] data, Encoding? encoding = null)
    {
        encoding ??= EdenEncoding.Big5;
        var text = encoding.GetString(data);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return ParseLines(lines);
    }

    /// <summary>Parse un fichier .ini en appliquant MyCrypt si necessaire.</summary>
    public static IniTable ParseFile(string path, Encoding? encoding = null)
    {
        return Parse(MyCrypt.DecodeFile(path), encoding);
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

        // Format "N|\r\n<colonnes>|\r\n<rows>" (T_* files)
        if (int.TryParse(header.TrimEnd('|'), out var colCount))
        {
            var rows = new List<string[]>();
            // line[1] = index des colonnes, rows a partir de line[2]
            for (var i = 2; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                rows.Add(lines[i].Split('|'));
            }
            return new IniTable(header, colCount, '|', rows);
        }

        return new IniTable(header, 0, '|', Array.Empty<string[]>());
    }

    private static IniTable BuildRows(string[] lines, string version, int cols, char sep)
    {
        var rows = new List<string[]>();
        var pending = new List<string>();

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) && pending.Count == 0) continue;
            // Frontiere de record : une ligne entrante «<id><sep>» alors que
            // le record en cours a deja debute par un id — les tables natives
            // omettent les colonnes vides de fin (ex. lignes de 35 cellules
            // pour 78 declarees dans c_item.ini). Sans ce test la ligne courte
            // absorbait le record suivant (13 ids perdus cote S_* ; ~36 000
            // cote client c_*). Vraies continuations (texte, $N$, «0||...»
            // sans id propre) non matchees → multi-lignes preserve.
            if (pending.Count > 0
                && IsRecordStart(pending[0], sep)
                && IsRecordStart(lines[i], sep))
            {
                rows.Add(string.Join("\r\n", pending).Split(sep));
                pending.Clear();
            }
            pending.Add(lines[i]);

            // Les champs texte peuvent contenir des CRLF embeddes (ex: prerequis
            // multi-lignes dans S_Class.ini). Un record est complet quand son
            // nombre de champs atteint cols+1 (le trailing sep laisse un champ vide).
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

    /// <summary>Un record debute par «<id><sep>» : la ligne commence par
    /// des chiffres ASCII suivis du separateur.</summary>
    private static bool IsRecordStart(string line, char sep)
    {
        var i = 0;
        while (i < line.Length && line[i] >= '0' && line[i] <= '9') i++;
        return i > 0 && i < line.Length && line[i] == sep;
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

    /// <summary>Parse un champ float invariant (0 si vide/invalide) —
    /// les taux des tables natives sont des entiers/pourcentages
    /// écrits en décimal ASCII.</summary>
    public static float GetFloat(string[] row, int col) =>
        col < row.Length && float.TryParse(row[col],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : 0f;
}
