using System.Text;

namespace Limitless.Studio.Services;

/// <summary>Reconstruction d'une ligne .ini à partir de valeurs de
/// colonnes — préserve les séparateurs d'origine ('|' ET ',' sont
/// tous deux des séparateurs pour IniService.Rows) : le texte entre
/// deux séparateurs est remplacé par la valeur, les colonnes
/// supplémentaires sont ajoutées en '|'.</summary>
public static class IniColumns
{
    /// <summary>Remplace les champs de `raw` par `values` en gardant
    /// chaque séparateur en place. Si `values` est plus long que le
    /// nombre de champs d'origine, les extras sont ajoutés avec '|'.</summary>
    public static string ReplaceFields(string raw, string[] values)
    {
        var sb = new StringBuilder(raw.Length + 16);
        var i = 0;
        var pos = 0;
        while (pos <= raw.Length)
        {
            var sep = raw.IndexOfAny(SepChars, pos);
            var end = sep < 0 ? raw.Length : sep;
            if (i < values.Length)
            { sb.Append(values[i]); i++; }
            else
                sb.Append(raw, pos, end - pos);
            if (sep < 0) break;
            sb.Append(raw[sep]);
            pos = sep + 1;
        }
        while (i < values.Length)
        { sb.Append('|').Append(values[i]); i++; }
        return sb.ToString();
    }

    private static readonly char[] SepChars = { '|', ',' };
}
