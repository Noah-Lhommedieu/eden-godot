using Xunit;
using Npgsql;
using Eden.Core.Game.Limitless;

namespace Eden.Tests;

/// <summary>Tests du .ini des récompenses du Limitless Pass
/// (<c>limitless_pass.ini</c> — format docs/limitless/LIMITLESS_PASS_INI.md)
/// : parsing des sections [level]/[reward], fusion dans les paliers
/// exposés au client (NCID 2033) et ids synthétiques négatifs des
/// perks ini (non journalisées — FK limitless_perks.id).</summary>
public class LimitlessPassIniTests
{
    /// <summary>Service adossé à un data source jamais ouvert —
    /// LoadPassIni/GetPassTiers sont purement fichier+mémoire.</summary>
    private static LimitlessService NewService(string iniPath)
    {
        var ds = NpgsqlDataSource.Create(
            "Host=localhost;Database=x;Username=x;Password=x");
        return new LimitlessService(ds, iniPath);
    }

    private static string WriteTempIni(string content)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"llpass_{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void LoadPassIni_ParsesRewardsAndLevels()
    {
        var path = WriteTempIni(
            "# commentaire\n" +
            "[level]\n" +
            "1 = 100\n" +
            "2 = 400\n" +
            "[reward]\n" +
            "1 | max_hp | flat | 250 | Max HP +250\n" +
            "2 | drop_rate | percent | 10 | Drop Rate +10%\n");
        try
        {
            var svc = NewService(path);
            svc.LoadPassIni();
            var tiers = svc.GetPassTiers();
            Assert.Equal(2, tiers.Count);
            Assert.Equal(1, tiers[0].Level);
            Assert.Equal(100, tiers[0].ExpReq);
            var perk = Assert.Single(tiers[0].Rewards);
            Assert.Equal("max_hp", perk.Code);
            Assert.Equal("Max HP +250", perk.Name);
            Assert.Equal(250, perk.Value);
            Assert.Equal("flat", perk.ValueKind);
            Assert.True(perk.Enabled);
            Assert.True(perk.Id < 0); // id synthétique — pas de journal BDD
            Assert.Equal("drop_rate",
                Assert.Single(tiers[1].Rewards).Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadPassIni_SkipsInvalidLines()
    {
        var path = WriteTempIni(
            "[level]\n" +
            "pas de egal ici\n" +
            "-3 = 100\n" +
            "5 = 500\n" +
            "[reward]\n" +
            "2 | drop_rate | bogus | 10 | bad kind\n" +
            "| max_hp | flat | 1 | bad level\n" +
            "3 | craft_time | percent | -20 | Craft -20%\n" +
            "4 | | flat | 1 | code vide\n");
        try
        {
            var svc = NewService(path);
            svc.LoadPassIni();
            var tiers = svc.GetPassTiers();
            // Seuls le palier 5 (level) et le reward du palier 3 sont
            // valides ; le reward 3 crée un palier affiché ? Non —
            // les paliers viennent de la courbe [level] uniquement.
            var t5 = Assert.Single(tiers);
            Assert.Equal(5, t5.Level);
            Assert.Equal(500, t5.ExpReq);
            Assert.Empty(t5.Rewards);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadPassIni_MissingFileIsTolerated()
    {
        var svc = NewService(Path.Combine(Path.GetTempPath(),
            $"llpass_absent_{Guid.NewGuid():N}.ini"));
        svc.LoadPassIni(); // ne doit pas lever
        Assert.Empty(svc.GetPassTiers());
    }

    /// <summary>SanitizeLabel (NCID 2033) : la police CEGUI du
    /// client ne rend que l'ASCII — les symboles typographiques des
    /// libellés BDD/ini sont convertis en équivalents ASCII lisibles
    /// (×→x) plutôt qu'en '?' — régression « Drop ?2 » du lot 10 —
    /// et les lettres accentuées perdent leur diacritique.
    /// (docs/limitless/LIMITLESS_ENCODING.md)</summary>
    [Theory]
    [InlineData("Drop ×2", "Drop x2")]
    [InlineData("Exp Ultime ×2", "Exp Ultime x2")]
    [InlineData("Vitesse +10%", "Move speed +10%")]
    [InlineData("Cast -10%", "Casting time -10%")]
    [InlineData("PV max +500", "PV max +500")]
    [InlineData("Accélération …", "Acceleration ...")]
    [InlineData("Bonus –10% — «x»", "Bonus -10% - \"x\"")]
    [InlineData("", "-")]
    public void SanitizeLabel_ProducesAscii(string input,
        string expected)
    {
        var flat = LimitlessService.SanitizeLabel(input);
        Assert.Equal(expected, flat);
        Assert.All(flat, ch => Assert.True(ch < 0x80,
            $"non-ASCII '{ch}' (U+{(int)ch:X4}) dans « {flat} »"));
    }

    [Fact]
    public void LoadPassIni_LabelMayContainPipes()
    {
        var path = WriteTempIni(
            "[level]\n7 = 900\n" +
            "[reward]\n" +
            "7 | move_speed_pct | percent | 5 | A | B | C\n");
        try
        {
            var svc = NewService(path);
            svc.LoadPassIni();
            // Tout après le 4e '|' est le libellé (doc : peut contenir
            // des '|').
            var tier = Assert.Single(svc.GetPassTiers());
            Assert.Equal("A | B | C",
                Assert.Single(tier.Rewards).Name);
        }
        finally { File.Delete(path); }
    }
}
