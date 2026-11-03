using System.Text.RegularExpressions;

namespace EdenClient.Ui;

/// <summary>Noyau pur : le serveur compose certains messages système
/// avec des noms d'objet Big5 (c_item col7 : « 獲得 … », « %s x %d »).
/// Le client remplace chaque suite de caractères CJK par le nom FR
/// (t_item) quand elle est connue ; le reste du texte est intact.</summary>
public static class ChatLocalizer
{
    private static readonly Regex Cjk = new(
        @"[　-〿㐀-鿿＀-￯]+", RegexOptions.Compiled);

    /// <summary>true si le texte contient du CJK.</summary>
    public static bool HasCjk(string? text) =>
        !string.IsNullOrEmpty(text) && Cjk.IsMatch(text);

    /// <param name="text">texte reçu</param>
    /// <param name="frByCjk">suite CJK → texte FR, null si inconnue</param>
    public static string Localize(string text, Func<string, string?> frByCjk)
    {
        if (!HasCjk(text)) return text;
        return Cjk.Replace(text, m => frByCjk(m.Value) ?? m.Value);
    }
}
