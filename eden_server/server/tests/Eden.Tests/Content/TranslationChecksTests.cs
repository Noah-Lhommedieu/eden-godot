using Eden.Core.Game.Content;

namespace Eden.Tests.Content;

/// <summary>Contrôles de traduction (E) et nom de contenu inutilisable (C3).</summary>
public class TranslationChecksTests
{
    private static readonly Dictionary<string, string> Glossary = new() { ["Épreuve"] = "Trial" };

    [Theory]
    [InlineData("Épée $7$longue", "Long sword $7$")]
    [InlineData("Soin #cFF0000#fort#n", "Strong #cFF0000#heal#n")]
    [InlineData("Gagnez %d pièces", "Earn %d coins")]
    [InlineData("Ligne 1\nLigne 2", "Line 1\nLine 2")]
    public void SameTokens_Pass(string fr, string en) => Assert.Empty(TranslationChecks.Check(fr, en));

    [Fact] public void MissingOrExtraToken_IsReported()
    {
        Assert.Contains(TranslationChecks.Check("Gagnez %d pièces", "Earn coins"), i => i.Code == "tokens");
        Assert.Contains(TranslationChecks.Check("Gagnez", "Earn %d"), i => i.Code == "tokens");
    }

    [Fact] public void ResidualCjk_Empty_TooLong_AndSpaces_AreReported()
    {
        Assert.Contains(TranslationChecks.Check("Épée", "Sword 劍"), i => i.Code == "cjk");
        Assert.Contains(TranslationChecks.Check("Épée", "  "), i => i.Code == "empty");
        Assert.Contains(TranslationChecks.Check("Épée", new string('a', 81)), i => i.Code == "too_long");
        Assert.Contains(TranslationChecks.Check("Épée", " Sword"), i => i.Code == "whitespace");
    }

    [Fact] public void Glossary_IsEnforced()
    {
        Assert.Contains(TranslationChecks.Check("Épreuve : Angor", "Challenge: Angor", 80, Glossary), i => i.Code == "glossary");
        Assert.Empty(TranslationChecks.Check("Épreuve : Angor", "Trial: Angor", 80, Glossary));
    }

    [Fact] public void FrHash_ChangesWhenFrChanges()
    {
        Assert.Equal(TranslationChecks.FrHash("Épée"), TranslationChecks.FrHash("Épée"));
        Assert.NotEqual(TranslationChecks.FrHash("Épée"), TranslationChecks.FrHash("Epée"));
        Assert.Equal(16, TranslationChecks.FrHash("x").Length);
    }

    [Theory]
    [InlineData("Class Awakening Dungeon (Place Holder)", true)] [InlineData("Test Cottage : Océan", true)] [InlineData("職業聖殿", true)]
    [InlineData("", true)] [InlineData("Monts Calcaires", false)] [InlineData("Épée de feu", false)]
    public void PlaceholderNames(string name, bool expected) => Assert.Equal(expected, ContentScope.IsPlaceholderName(name));
}
