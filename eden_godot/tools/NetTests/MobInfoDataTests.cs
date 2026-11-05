using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Fiche « Inspecter » d'un mob : stats, 3 catégories de drops,
/// 2 familles de sorts (Normal / à seuil) lues depuis corpus/fr-full.</summary>
public class MobInfoDataTests
{
    private static string Db(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "corpus", "fr-full", "data", "db", name);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(name);
    }

    static MobInfoDataTests()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        MobInfoData.LoadFrom(Db("c_monster.ini"), Db("c_ai.ini"),
            Db("c_dropitem.ini"), Db("c_classdropitem.ini"));
    }

    [Fact]
    public void Stats_HaveMiniFormat()
    {
        var s = MobInfoData.Build(40055)!;       // champignoïde de la mine
        var labels = s.Stats.Select(x => x.Label).ToList();
        foreach (var l in new[] { "PV", "PM", "BP", "P-ATK", "M-ATK", "Esquive",
                     "Précision", "Taux de crit. phy.", "Dégâts crit. mag.",
                     "Déf. phy.", "Parade" })
            Assert.Contains(l, labels);
        Assert.Equal("10", s.Stats.First(x => x.Label == "Dégâts crit. phy.")
            .Value.Replace(" %", ""));
    }

    [Fact]
    public void NormalSkills_FromColumns44To46()
    {
        var s = MobInfoData.Build(40055)!;
        Assert.Equal(new[] { 51001 }, s.NormalSkills.Select(x => x.SpellId));
    }

    /// <summary>Libellé de la probabilité de cast normale (c_ai
    /// cols 6/8/10) : « Chance : N % » — pas « N % » nu.</summary>
    [Fact]
    public void NormalSkills_ChanceLabel()
    {
        var s = MobInfoData.Build(40055)!;
        Assert.All(s.NormalSkills,
            x => Assert.Matches(@"^Chance : \d+ %$", x.Detail));
    }

    [Fact]
    public void CountdownSkills_FromAiProfile()
    {
        var withCd = Enumerable.Range(20000, 45000).Select(MobInfoData.Build)
            .FirstOrDefault(x => x is { CountdownSkills.Count: > 0 });
        Assert.NotNull(withCd);
        Assert.Matches(@"^PV ≤ \d+ % · relance \d+ s$",
            withCd!.CountdownSkills[0].Detail);
        Assert.All(withCd.CountdownSkills, c => Assert.True(c.SpellId > 0));
    }

    [Fact]
    public void Drops_ThreeCategories_BreakOnlyWhenBreakable()
    {
        // Herak 58471 : 2 pools + mask Slash|Lightning
        var s = MobInfoData.Build(58471)!;
        Assert.NotEmpty(s.NormalDrops);
        Assert.NotEmpty(s.BreakDrops);
        Assert.All(s.BreakDrops, d => Assert.True(d.ChancePct > 0));
    }

    [Fact]
    public void ClassDrops_ResolvedThroughMonsterReferences()
    {
        // un mob à butin de classe dont la table existe dans c_classdropitem
        var anyClass = Enumerable.Range(20000, 40000)
            .Select(MobInfoData.Build).Where(x => x != null)
            .Any(x => x!.ClassDrops.Count > 0);
        Assert.True(anyClass);
    }
}

