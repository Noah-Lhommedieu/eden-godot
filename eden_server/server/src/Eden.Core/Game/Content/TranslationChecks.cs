using System.Text.RegularExpressions;

namespace Eden.Core.Game.Content;

/// <summary>Un défaut détecté dans une traduction.</summary>
public sealed record TranslationIssue(string Code, string Message);

/// <summary>
/// Contrôles automatiques d'une traduction (PLAN-ACTION-3 §6.3) : codes de mise en forme conservés (<c>$N$</c>, <c>#c…#</c>, <c>%d</c>,
/// <c>{0}</c>, retours ligne), aucun idéogramme résiduel, longueur maximale, termes du glossaire respectés. Règle UNIQUE, partagée par
/// l'import, l'admin et les tests.
/// </summary>
public static class TranslationChecks
{
    private static readonly Regex Tokens = new(@"\$[A-Za-z0-9]\$|#c[0-9A-Fa-f]*#|#n|%[sd]|\{\d+\}|\n", RegexOptions.Compiled);
    private static readonly Regex Cjk = new(@"[㐀-鿿豈-﫿]", RegexOptions.Compiled);

    public static IReadOnlyList<TranslationIssue> Check(string fr, string en, int maxLength = 80,
        IReadOnlyDictionary<string, string>? glossary = null)
    {
        var issues = new List<TranslationIssue>();
        if (string.IsNullOrWhiteSpace(en)) { issues.Add(new("empty", "traduction vide")); return issues; }
        var frTokens = Tokens.Matches(fr).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var enTokens = Tokens.Matches(en).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (!frTokens.SequenceEqual(enTokens)) issues.Add(new("tokens", $"codes de mise en forme différents : FR [{string.Join(' ', frTokens)}] / EN [{string.Join(' ', enTokens)}]"));
        if (Cjk.IsMatch(en)) issues.Add(new("cjk", "idéogrammes chinois résiduels"));
        if (en.Length > maxLength) issues.Add(new("too_long", $"{en.Length} caractères > {maxLength}"));
        if (en != en.Trim()) issues.Add(new("whitespace", "espaces en début ou fin"));
        if (glossary != null)
            foreach (var (frTerm, enTerm) in glossary)
                if (fr.Contains(frTerm, StringComparison.Ordinal) && !en.Contains(enTerm, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new("glossary", $"« {frTerm} » doit devenir « {enTerm} »"));
        return issues;
    }

    /// <summary>Empreinte du texte FR (16 hex de SHA-256) : sert à détecter qu'une traduction est périmée.</summary>
    public static string FrHash(string fr) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fr))).ToLowerInvariant()[..16];
}
