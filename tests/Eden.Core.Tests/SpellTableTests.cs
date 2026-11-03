using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// <see cref="SpellTable"/> — colonnes de <c>c_spell.ini</c> portées du
/// SpellCatalog de référence (icône col 1, type de cible col 18).
/// </summary>
[Xunit.Collection("SharedCoreState")]
public class SpellTableTests
{
    [Fact]
    public void Load_ParsesIconAndTarget()
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            $"c_spell_{Guid.NewGuid():N}.ini");
        try
        {
            var cells = new string[45];
            cells[0] = "50011"; cells[1] = "J001"; cells[18] = "2";
            var cells2 = new string[45];
            cells2[0] = "50012"; cells2[1] = "J002"; cells2[18] = "1";
            File.WriteAllText(tmp,
                "|V.7|45|\r\n" +
                string.Join('|', cells) + "|\r\n" +
                string.Join('|', cells2) + "|\r\n");
            var defs = SpellTable.Load(tmp);
            Assert.Equal(2, defs.Count);
            Assert.Equal("J001", defs[50011].IconCode);
            Assert.Equal(2, defs[50011].TargetType);
            Assert.Equal("J002", defs[50012].IconCode);
            Assert.Equal(1, defs[50012].TargetType);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RealFile_CSpell_Loads()
    {
        var root = FindRawAssets();
        if (root == null) return;   // assets absents — test ignoré
        var defs = SpellTable.Load(
            Path.Combine(root, "data/db/c_spell.ini"));
        // La queue du fichier utilise un format « # »/multi-ligne que
        // l'en-tête |V|N| ne déclare pas — le parseur garde la partie
        // saine (même comportement que le SpellCatalog de référence).
        Assert.True(defs.Count >= 400, $"spells={defs.Count}");
        // 50001 : icône J001 (première ligne du fichier FR).
        Assert.Equal("J001", defs[50001].IconCode);
        Assert.True(defs[50001].TargetType > 0);
        Assert.Equal("E0048", defs[50058].IconCode);
        Assert.Equal(1, defs[50058].TargetType);
    }

    private static string? FindRawAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "client_assets");
            if (!Directory.Exists(candidate))
                candidate = Path.Combine(dir.FullName, "raw_assets");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
