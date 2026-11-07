using System.IO;
using System.Linq;
using System.Text;

namespace Limitless.Studio.Services;

/// <summary>Écriture .ini Big5 — remplace une ligne par son id
/// en préservant le format (séparateur | ou ,, encodage Big5).</summary>
public sealed class IniWriter
{
    private static Encoding Big5()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("big5");
    }

    /// <summary>Remplace la ligne dont le 1er champ == id par `newLine`.
    /// Retourne true si la ligne a été trouvée et réécrite.</summary>
    public bool ReplaceRow(string path, int id, string newLine)
    {
        if (!File.Exists(path)) return false;
        var enc = Big5();
        var lines = File.ReadAllLines(path, enc).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (string.IsNullOrWhiteSpace(l) || !char.IsDigit(l[0]))
                continue;
            var first = l.Split('|', ',')[0];
            if (first == id.ToString())
            {
                lines[i] = newLine;
                File.WriteAllLines(path, lines, enc);
                return true;
            }
        }
        return false;
    }

    /// <summary>Remplace la N-ième ligne de DONNÉES (même filtre que
    /// IniService.Rows : non vide, 1er char = chiffre) par `newLine`.
    /// Pour les fichiers sans id significatif en col0.</summary>
    public bool ReplaceRowByIndex(string path, int dataIndex,
        string newLine)
    {
        if (!File.Exists(path)) return false;
        var enc = Big5();
        var lines = File.ReadAllLines(path, enc).ToList();
        var n = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (string.IsNullOrWhiteSpace(l) || !char.IsDigit(l[0]))
                continue;
            if (n++ == dataIndex)
            {
                lines[i] = newLine;
                File.WriteAllLines(path, lines, enc);
                return true;
            }
        }
        return false;
    }

    /// <summary>Remplace le N-ième ENREGISTREMENT (compté comme
    /// IniService.Records — records multi-lignes pour les fichiers à
    /// en-tête `|V.x|N|`) par `newRaw`, qui peut contenir des '\n'
    /// (réécrit en plusieurs lignes physiques).</summary>
    public bool ReplaceRecordByIndex(string path, int dataIndex,
        string newRaw)
    {
        return ReplaceRecord(path,
            span => dataIndex == span.Index, newRaw);
    }

    /// <summary>Remplace l'enregistrement dont le 1er champ == id.</summary>
    public bool ReplaceRecord(string path, int id, string newRaw)
    {
        return ReplaceRecord(path,
            span => span.Cols.Length > 0
                && span.Cols[0] == id.ToString(), newRaw);
    }

    /// <summary>Énumération des spans de lignes physiques par
    /// enregistrement — même accumulation qu'IniService.Records.</summary>
    private readonly record struct RecordSpan(int Index, int Start,
        int End, string[] Cols);

    /// <summary>Parcourt les records, remplace le span de lignes du
    /// premier qui satisfait `match` par `newRaw`.</summary>
    private bool ReplaceRecord(string path,
        System.Func<RecordSpan, bool> match, string newRaw)
    {
        if (!File.Exists(path)) return false;
        var enc = Big5();
        var lines = File.ReadAllLines(path, enc).ToList();
        if (lines.Count == 0) return false;
        var (expected, sep, hasHeader) =
            IniService.Header(lines[0]);
        var n = 0;
        var i = hasHeader ? 1 : 0;
        while (i < lines.Count)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) { i++; continue; }
            if (!hasHeader && !char.IsDigit(lines[i][0]))
            { i++; continue; }
            // Span du record : accumule jusqu'à expected+1 champs.
            var start = i;
            var end = i;
            var fields = expected > 0
                ? lines[i].Split(sep).Length : int.MaxValue;
            while (fields < expected + 1 && end + 1 < lines.Count)
            {
                end++;
                fields += lines[end].Split(sep).Length;
            }
            var cols = string.Join('\n',
                    lines.GetRange(start, end - start + 1))
                .Split(sep);
            var span = new RecordSpan(n++, start, end, cols);
            if (match(span))
            {
                lines.RemoveRange(start, end - start + 1);
                lines.InsertRange(start, newRaw.Split('\n'));
                File.WriteAllLines(path, lines, enc);
                return true;
            }
            i = end + 1;
        }
        return false;
    }
}
