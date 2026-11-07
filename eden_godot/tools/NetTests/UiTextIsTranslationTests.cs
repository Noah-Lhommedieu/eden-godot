using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

/// <summary><see cref="UiTextEn.IsTranslation"/> : les valeurs FR des
/// deux TSV (layout + chaînes hardcodées) doivent être reconnues pour
/// qu'AutoTranslate ne les signale pas en miss.</summary>
[Collection("SharedResolver")]
public class UiTextIsTranslationTests
{
    private static string FindProject()
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10; i++)
        {
            var p = Path.Combine(dir, "godot_project");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        throw new DirectoryNotFoundException("godot_project");
    }

    [Fact]
    public void FrenchValuesAreTranslationsNotMisses()
    {
        var dir = FindProject();
        UiTextEn.ResolveFile = rel =>
            Path.Combine(dir, "gamedata", rel);
        TextResolver.SetLanguage("fr");
        UiTextEn.Reset();
        try
        {
            // Valeur de ui_text_fr.tsv (labels de layout).
            Assert.True(UiTextEn.IsTranslation("Archives"));
            // Valeur de ui_text_en_fr.tsv (chaîne hardcodée C#).
            Assert.True(UiTextEn.IsTranslation("Catégories d'archive"));
            Assert.True(UiTextEn.IsTranslation("Points d'archive"));
            // Chaîne absente des deux dictionnaires → vrai miss.
            Assert.False(UiTextEn.IsTranslation(
                "Some Untranslated Label"));
        }
        finally
        {
            UiTextEn.ResolveFile = null;
            TextResolver.SetLanguage("en");
        }
    }
}
