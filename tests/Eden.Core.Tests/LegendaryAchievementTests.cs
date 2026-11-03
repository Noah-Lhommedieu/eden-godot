using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>A5.6 / A5.3 : classement légendaire par les tables wiki,
/// progression des succès groupe, compteurs 480 kind 6, split de stats.
/// </summary>
[Collection("PlayerAchievements")]
public class LegendaryAchievementTests
{
    [Theory]
    [InlineData("Achieve LV1~65 Rare monster crusade achievements\n\n$12$lore", 1, 65, "Rare Enemies")]
    [InlineData("Get LV1~30 loot achievements", 1, 30, "Trophies")]
    [InlineData("Achieve LV1~50 orange level Rapier crafting achievement", 2, 50, "Rapiers")]
    [InlineData("Achieve LV1~65 orange grade dagger crafting achievement", 2, 65, "Daggers")]
    [InlineData("Achieved the crafting achievement LV1~65 orange cape", 1, 65, "Capes")]
    [InlineData("crafting achievement LV1~50 orange cloth armor", 1, 50, "Cloth Armour")]
    [InlineData("Achieve LV1~30 orange level staff crafting achievement", 2, 30, "Staffs")]
    [InlineData("Achieve LV90 Orange Grade Spear and Sickle Making", 2, 90, "Pikes")]
    public void Classify_UsesWikiColumns(string en, int kind, int lv,
        string noun)
    {
        var r = LegendaryWiki.Classify(en, hasItemReward: false);
        Assert.Equal(kind, r.Kind);
        Assert.Equal(lv, r.Level);
        Assert.Equal(noun, r.Noun);
        Assert.False(r.OutOfScope);
    }

    [Fact]
    public void Classify_Tier_Matches_WikiRow()
    {
        var r = LegendaryWiki.Classify(
            "Achieve LV1~65 Rare monster crusade achievements", false);
        Assert.NotNull(r.Wiki);
        Assert.Equal("Can not lose anyone.", r.Wiki!.Name);
        Assert.Contains("EXP & CP +4%", r.Wiki.Reward);
    }

    [Fact]
    public void Classify_ItemReward_Or_MultiObjective_IsMountsAndCostumes()
    {
        Assert.Equal(3, LegendaryWiki.Classify(
            "Achieve LV1~65 orange grade armor production achievement",
            hasItemReward: true).Kind);
        Assert.Equal(3, LegendaryWiki.Classify(
            "Achieve LV1~65 Rare monster crusade achievements\n" +
            "Obtain LV1~65 loot achievements\n\n$12$lore", false).Kind);
    }

    [Fact]
    public void Scope_Is_Lv90_And_Wiki_FlagsLv95Rows()
    {
        Assert.False(LegendaryWiki.OutOfScope(90));
        Assert.True(LegendaryWiki.OutOfScope(95));
        var oos = LegendaryWiki.All.Where(r =>
            LegendaryWiki.OutOfScope(r.MaxLevel)).Select(r => r.Name)
            .ToList();
        Assert.Contains("Marathon Runner", oos);
        Assert.Contains("Unbreakable Guardian", oos);
        Assert.DoesNotContain("Treasure Hunter", oos);
        Assert.Equal(156, LegendaryWiki.All.Count);
    }

    [Fact]
    public void Targets_Progress()
    {
        var t = AchievementProgress.ParseTargets("57501;57502;57503;x;");
        Assert.Equal(new[] { 57501, 57502, 57503 }, t);
        var done = new HashSet<int> { 57501, 57503 };
        Assert.Equal(2, AchievementProgress.CountDone(t, done.Contains));
        Assert.Equal("66.67", AchievementProgress.Percent(2, 3));
        Assert.Equal("0.00", AchievementProgress.Percent(0, 0));
    }

    [Fact]
    public void StatSplit_NaturalPlusBonus()
    {
        Assert.Equal((100, 23), StatSplit.Split(123, 100));
        Assert.Equal((100, -5), StatSplit.Split(95, 100));
        Assert.Null(StatSplit.Split(123, null));
        Assert.Equal("123 = 100 + 23",
            StatSplit.Format("{0} = {1} + {2}", 123, 100));
        Assert.Equal("123", StatSplit.Format("{0} = {1} + {2}", 123, null));
    }

    [Fact]
    public void Kind6_Counters_RoundTrip_And_Cleared_On_Completion()
    {
        PlayerAchievements.Clear();
        var m = new ZoneAchievementUpdate { Kind = 6 };
        m.Entries.Add((90101, 7u));
        var w = new BinWriter();
        m.Serialize(w);
        var bytes = w.ToArray();
        Assert.Equal(1 + 2 + 8, bytes.Length);
        var back = new ZoneAchievementUpdate();
        back.Deserialize(new BinReader(bytes));
        Assert.Equal((byte)6, back.Kind);
        PlayerAchievements.Apply(back);
        Assert.Equal(7u, PlayerAchievements.CounterOf(90101));
        Assert.False(PlayerAchievements.IsCompleted(90101));
        var done = new ZoneAchievementUpdate { Kind = 1 };
        done.Entries.Add((90101, 5u));
        PlayerAchievements.Apply(done);
        Assert.Equal(0u, PlayerAchievements.CounterOf(90101));
        Assert.True(PlayerAchievements.IsCompleted(90101));
        PlayerAchievements.Clear();
    }

    /// <summary>Données réelles (dist/package-*) : les 94 légendaires
    /// à cibles se classent, aucun hors périmètre ; col9 résout dans
    /// T_Title. Ignoré si le dossier de données est absent.</summary>
    [Fact]
    public void RealData_LegendaryClassification()
    {
        var db = FindDb();
        if (db == null) return;
        var defs = AchievementTable.Load(Path.Combine(db, "c_achievement.ini"));
        var en = TextTable.Load(Path.Combine(db, "T_Achievement.ini"));
        var titles = TitleTable.Load(Path.Combine(db, "T_Title.ini"),
            Path.Combine(db, "c_title.ini"));
        var leg = defs.Values.Where(d => d.Id is >= 5001 and <= 5092 &&
            d.TargetIds.Length > 0).ToList();
        Assert.True(leg.Count >= 90);
        var counts = new int[4];
        foreach (var d in leg)
        {
            var text = en.TryGetValue(d.Id, out var t) ? t.Description : "";
            var r = LegendaryWiki.Classify(text, d.ItemCol10 != 0);
            counts[r.Kind]++;
            Assert.False(r.OutOfScope, $"{d.Id} lvl {r.Level}");
            Assert.True(titles.ContainsKey(d.TitleCol9), $"{d.Id} col9");
            // Les cibles sont des succès à 1 déclenchement.
            foreach (var tid in d.TargetIds.Take(3))
                Assert.Equal(1, defs[tid].NeedNum);
        }
        Assert.Equal(6, counts[3]);                // 5031-5036
        Assert.True(counts[1] > 30 && counts[2] > 30,
            $"stats={counts[1]} armes={counts[2]}");
    }

    private static string? FindDb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var pkg in new[] { "dist/package-linux", "dist/package-windows" })
            {
                var p = Path.Combine(dir.FullName, pkg, "gamedata/data/db");
                if (File.Exists(Path.Combine(p, "c_achievement.ini")))
                    return p;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
