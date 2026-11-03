using EdenClient.Data.Tables;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Journal de quêtes : spécialisation, niveau et « quêtes
/// manquées » (futur-feature/22, retour utilisateur 2026-10-31).</summary>
public class QuestCatalogTests
{
    private static MissionTable.MissionDef Def(int id, int limit = 0,
        int reqLv = 0, string group = "", string cat = "",
        params (string Name, string[] Args)[] conds)
    {
        var d = new MissionTable.MissionDef
        {
            Id = id, CompleteLimit = limit, ReqLevel = reqLv,
            GiverNpc = 50000 + id,
            Group = group, Category = cat,
        };
        foreach (var (n, a) in conds)
        {
            var c = new MissionTable.ScriptCmd { Name = n };
            c.Args.AddRange(a);
            d.Conditions.Add(c);
        }
        return d;
    }

    [Theory]
    [InlineData("每日任務", "每日任務", 0x10, QuestKind.Daily)]
    [InlineData("阿洛斯平原", "S036，主線", 0, QuestKind.Main)]
    [InlineData("熱風谷", "S032，重複", 0, QuestKind.Repeat)]
    [InlineData("特殊任務", "特殊任務", 0, QuestKind.Special)]
    [InlineData("公會任務", "", 0, QuestKind.Guild)]
    [InlineData("職業覺醒任務", "武鬥覺醒", 0, QuestKind.Awakening)]
    [InlineData("大草原", "S010，單次", 0, QuestKind.Single)]
    [InlineData("", "", 0, QuestKind.Other)]
    public void KindOf_FromBig5Columns(string g, string c, int f,
        QuestKind k) => Assert.Equal(k, QuestCatalog.KindOf(g, c, f));

    [Fact]
    public void LevelOf_PrefersCondition_ThenReq_ThenTitle()
    {
        var d = Def(1, conds: ("If_CharLevel", new[] { ">=", "31" }));
        Assert.Equal(31, QuestCatalog.LevelOf(d, "Nv. 08 X"));
        Assert.Equal(12, QuestCatalog.LevelOf(Def(2, reqLv: 12), "Nv. 08 X"));
        Assert.Equal(8, QuestCatalog.LevelOf(Def(3), "Nv. 08 Départ"));
        Assert.Equal(0, QuestCatalog.LevelOf(Def(4), "Sans niveau"));
    }

    [Fact]
    public void IsAvailable_Rules()
    {
        var fms = new Dictionary<int, int> { [10] = 1, [11] = 3, [13] = 4 };
        int F(int id) => fms.GetValueOrDefault(id);
        // jamais prise, niveau OK
        Assert.True(QuestCatalog.IsAvailable(Def(20), 30, F));
        // en cours → non
        Assert.False(QuestCatalog.IsAvailable(Def(11), 30, F));
        Assert.False(QuestCatalog.IsAvailable(Def(13), 30, F));
        // unique déjà terminée → non ; quotidienne (limite>0) → oui
        Assert.False(QuestCatalog.IsAvailable(Def(10), 30, F));
        Assert.True(QuestCatalog.IsAvailable(Def(10, limit: 1), 30, F));
        // niveau trop bas
        Assert.False(QuestCatalog.IsAvailable(Def(21, reqLv: 40), 30, F));
        Assert.True(QuestCatalog.IsAvailable(Def(21, reqLv: 40), 40, F));
        // prérequis If_FMS done
        var chain = Def(22, conds: ("If_FMS", new[] { "10", "done" }));
        Assert.True(QuestCatalog.IsAvailable(chain, 30, F));
        var chain2 = Def(23, conds: ("If_FMS", new[] { "99", "done" }));
        Assert.False(QuestCatalog.IsAvailable(chain2, 30, F));
        // If_CharLevel
        var lv = Def(24, conds: ("If_CharLevel", new[] { ">=", "50" }));
        Assert.False(QuestCatalog.IsAvailable(lv, 49, F));
        Assert.True(QuestCatalog.IsAvailable(lv, 50, F));
    }

    [Fact]
    public void Missed_SortedByLevel_AndFiltered()
    {
        var all = new[]
        {
            Def(1, conds: ("If_CharLevel", new[] { ">=", "20" })),
            Def(2, conds: ("If_CharLevel", new[] { ">=", "5" })),
            Def(3, conds: ("If_CharLevel", new[] { ">=", "99" })),   // trop haut
            Def(4, conds: ("If_CharLevel", new[] { ">=", "5" })),
            Def(5),                                                   // pas de texte
        };
        var res = QuestCatalog.Missed(all, 30, _ => 0,
            id => id == 5 ? null : $"Nv. {id}");
        Assert.Equal(new[] { 2, 4, 1 }, res.Select(r => r.Def.Id));
        Assert.Equal(new[] { 5, 5, 20 }, res.Select(r => r.Level));
    }

    [Fact]
    public void Missed_AllowsUpToTwoLevelsAbove()
    {
        var all = new[]
        {
            Def(1, conds: ("If_CharLevel", new[] { ">=", "32" })),
            Def(2, conds: ("If_CharLevel", new[] { ">=", "33" })),
        };
        var res = QuestCatalog.Missed(all, 30, _ => 0, id => $"Nv. {id}");
        Assert.Equal(new[] { 1 }, res.Select(r => r.Def.Id));
    }

    [Fact]
    public void IsRepeatable_LimitOrDailyKind()
    {
        Assert.True(QuestCatalog.IsRepeatable(new MissionTable.MissionDef
        { Id = 1, CompleteLimit = 3 }));
        Assert.False(QuestCatalog.IsRepeatable(new MissionTable.MissionDef
        { Id = 2 }));
    }

    [Fact]
    public void RealData_Level30_HasMissedQuests_SortedAndEligible()
    {
        var all = MissionTable.All.ToList();
        if (all.Count < 100) return;            // table absente (CI)
        var res = QuestCatalog.Missed(all, 30, _ => 0, id => $"Nv. {id}");
        Assert.NotEmpty(res);
        // Missed tolère level+maxAbove (32 à niveau 30) par conception.
        Assert.All(res, r => Assert.True(r.Level <= 32));
        Assert.All(res, r => Assert.True(r.Def.ReqLevel <= 30));
        var lv = res.Select(r => r.Level).ToList();
        Assert.Equal(lv.OrderBy(x => x), lv);
        // un perso niveau 90 voit au moins autant de quêtes qu'un niveau 30
        var r90 = QuestCatalog.Missed(all, 90, _ => 0, id => $"Nv. {id}",
            max: 100000);
        Assert.True(r90.Count >= res.Count);
        // les quêtes terminées (unique) disparaissent
        var done = res.First().Def;
        if (done.CompleteLimit <= 0)
        {
            var again = QuestCatalog.Missed(all, 30,
                id => id == done.Id ? 1 : 0, id => $"Nv. {id}");
            Assert.DoesNotContain(again, r => r.Def.Id == done.Id);
        }
    }

    [Fact]
    public void Missed_SkipsNoGiverAndNoLevelTitle()
    {
        var noGiver = new MissionTable.MissionDef { Id = 7 };
        var ok = Def(8);
        var res = QuestCatalog.Missed(new[] { noGiver, ok }, 30, _ => 0,
            id => id == 8 ? "Nv. 8 Ok" : "Nv. 7 Sans PNJ");
        Assert.Equal(new[] { 8 }, res.Select(r => r.Def.Id));
        var res2 = QuestCatalog.Missed(new[] { ok }, 30, _ => 0,
            _ => "Certificat d'Eden : Expert");
        Assert.Empty(res2);
        Assert.True(QuestCatalog.HasLevelPrefix("Nv. 1. Votre"));
        Assert.True(QuestCatalog.HasLevelPrefix("L08. X"));
        Assert.False(QuestCatalog.HasLevelPrefix("Certificat d'Eden : 5"));
    }

    [Fact]
    public void RealData_KindsCoverMainDailyRepeatSpecial()
    {
        var all = MissionTable.All.ToList();
        if (all.Count < 100) return;
        var kinds = all.Select(m => QuestCatalog.KindOf(m.Group, m.Category,
            m.Flags)).Distinct().ToHashSet();
        Assert.Contains(QuestKind.Main, kinds);
        Assert.Contains(QuestKind.Daily, kinds);
        Assert.Contains(QuestKind.Repeat, kinds);
        Assert.Contains(QuestKind.Special, kinds);
    }
}
