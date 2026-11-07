using System.IO;
using System.Collections.Generic;
using System.Text;

namespace Limitless.Studio.Services;

/// <summary>Lecture des .ini Big5 du jeu (S_Item/S_Monster) —
/// même encodage que le game-server. Deux niveaux :
/// <see cref="Rows"/> = une ligne physique (legacy), et
/// <see cref="Records"/> = un enregistrement complet, qui peut
/// couvrir plusieurs lignes physiques quand l'en-tête `|V.x|N|`
/// déclare N colonnes (S_Item : la description multi-lignes pousse
/// les colonnes stats sur la dernière ligne du record — même règle
/// que IniTable.BuildRows côté serveur).</summary>
public sealed class IniService
{
    /// <summary>Un enregistrement .ini complet — `Raw` peut contenir
    /// des '\n' quand le record couvre plusieurs lignes physiques
    /// (les sauts de ligne vivent DANS la valeur du champ texte).</summary>
    public sealed record IniRecord(string Raw, string[] Cols);

    static IniService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Encoding Big5() => Encoding.GetEncoding("big5");

    /// <summary>Lignes de données (id|name|…) d'un .ini — une entrée
    /// par ligne physique commençant par un chiffre.</summary>
    public IEnumerable<string[]> Rows(string path)
    {
        foreach (var r in RowsWithRaw(path)) yield return r.Cols;
    }

    /// <summary>Lignes de données avec la ligne brute d'origine —
    /// nécessaire pour réécrire en préservant les séparateurs ('|'
    /// et ',' coexistent comme séparateurs de colonnes).</summary>
    public IEnumerable<(string Raw, string[] Cols)> RowsWithRaw(
        string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var l in File.ReadLines(path, Big5()))
        {
            if (string.IsNullOrWhiteSpace(l) || !char.IsDigit(l[0]))
                continue;
            yield return (l, l.Split('|', ','));
        }
    }

    /// <summary>Enregistrements complets — si le fichier a un en-tête
    /// `|V.x|N|` ou `V.x,N,`, les lignes sont accumulées jusqu'à
    /// N+1 champs (séparateur déclaré) comme IniTable.BuildRows ;
    /// sinon, une entrée par ligne physique commençant par un
    /// chiffre.</summary>
    public IEnumerable<IniRecord> Records(string path)
    {
        if (!File.Exists(path)) yield break;
        var lines = File.ReadAllLines(path, Big5());
        if (lines.Length == 0) yield break;
        var (expected, sep, hasHeader) = Header(lines[0]);
        if (!hasHeader)
        {
            foreach (var (raw, cols) in RowsWithRaw(path))
                yield return new IniRecord(raw, cols);
            yield break;
        }
        var pending = new List<string>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])
                && pending.Count == 0)
                continue;
            pending.Add(lines[i]);
            var joined = string.Join('\n', pending);
            if (expected > 0
                && joined.Split(sep).Length < expected + 1)
                continue;   // record incomplet — attend la suite
            yield return new IniRecord(joined, joined.Split(sep));
            pending.Clear();
        }
        if (pending.Count > 0)
        {
            var j = string.Join('\n', pending);
            yield return new IniRecord(j, j.Split(sep));
        }
    }

    /// <summary>Détecte l'en-tête `|V.x|N|` / `V.x,N|` — retourne
    /// le nombre de colonnes attendu et le séparateur, ou
    /// `hasHeader=false` pour les fichiers sans en-tête V.</summary>
    internal static (int Expected, char Sep, bool HasHeader) Header(
        string firstLine)
    {
        if (firstLine.StartsWith("|V.",
                System.StringComparison.Ordinal))
        {
            var parts = firstLine.Split('|',
                System.StringSplitOptions.RemoveEmptyEntries);
            var n = parts.Length > 1
                && int.TryParse(parts[1], out var v) ? v : 0;
            return (n, '|', true);
        }
        if (firstLine.StartsWith("V.",
                System.StringComparison.Ordinal))
        {
            var sep = firstLine.Contains(',') ? ',' : '|';
            var parts = firstLine.Split(sep,
                System.StringSplitOptions.RemoveEmptyEntries);
            var n = parts.Length > 1
                && int.TryParse(parts[1], out var v) ? v : 0;
            return (n, sep, true);
        }
        return (0, '|', false);
    }
}
