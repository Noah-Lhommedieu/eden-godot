using System.Reflection;

namespace Eden.Core.Game.Foundation;

/// <summary>Langue d'affichage. Le français est la référence ; l'anglais est
/// un overlay dérivé (repli sur le français si absent).</summary>
public enum GameLanguage { Fr, En }

/// <summary>
/// Catalogue code → texte FR/EN, chargé depuis la ressource embarquée
/// <c>errors.tsv</c> (puis, plus tard, depuis la base de contenu).
/// </summary>
public sealed class ErrorCatalog
{
    private readonly Dictionary<string, (string Fr, string? En)> _texts;

    public ErrorCatalog(IEnumerable<string> tsvLines)
    {
        _texts = new(StringComparer.Ordinal);
        foreach (var raw in tsvLines)
        {
            if (raw.Length == 0 || raw[0] == '#') continue;
            var p = raw.Split('\t');
            if (p.Length < 2 || p[0].Length == 0) continue;
            _texts[p[0]] = (p[1], p.Length > 2 && p[2].Length > 0 ? p[2] : null);
        }
    }

    public IEnumerable<string> Codes => _texts.Keys;

    public bool Has(string code) => _texts.ContainsKey(code);

    /// <summary>Texte pour la langue demandée ; code inconnu ⇒ le code lui-même
    /// (jamais de chaîne vide : un refus reste visible et diagnosticable).</summary>
    public string Format(GameError error, GameLanguage lang = GameLanguage.Fr)
    {
        if (!_texts.TryGetValue(error.Code, out var t)) return error.Code;
        var text = lang == GameLanguage.En && t.En != null ? t.En : t.Fr;
        if (error.Args is not { Count: > 0 }) return text;
        for (var i = 0; i < error.Args.Count; i++)
            text = text.Replace("{" + i + "}", error.Args[i]?.ToString() ?? "");
        return text;
    }

    /// <summary>Catalogue embarqué dans l'assembly.</summary>
    public static ErrorCatalog LoadEmbedded()
    {
        var asm = typeof(ErrorCatalog).Assembly;
        var name = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("errors.tsv", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return new ErrorCatalog(r.ReadToEnd().Split('\n')
            .Select(l => l.TrimEnd('\r')));
    }
}
