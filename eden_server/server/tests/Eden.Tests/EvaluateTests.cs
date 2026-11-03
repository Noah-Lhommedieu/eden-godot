using Eden.Core.Data.Parsers;
using Eden.Core.Game.Evaluate;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions du sous-système « gladiator trial » (CEvaluate natif) :
/// parser S_RewardScore (DSL KillMonster/TeamDieNum/PassTime/TotalScore),
/// domaine EvaluateInstance (compteurs, End, LotteryOpen), wires 607
/// types 0-3 (EvaluatePackets — Serialize natif @0x80846b0).
/// </summary>
public class EvaluateTests
{
    /// <summary>Ligne S_RewardScore de test — même structure que la
    /// def 655 réelle (catégories kill 4/6, TeamDieNum 1, plages de
    /// temps décroissantes, grades C→S+, flag 0, items pondérés).</summary>
    private const string SampleIni =
        "|V.1|87|\n" +
        "1|655|63399|#:KillMonster 4 32 RewardScore + 482:" +
        "KillMonster 6 2 RewardScore + 361:TeamDieNum 1 RewardScore + 401|" +
        "#:PassTime >= 0 <= 180 RewardScore + 1604:" +
        "PassTime > 180 <= 210 RewardScore + 802:" +
        "PassTime > 210 <= 270 RewardScore + 401:" +
        "PassTime > 270 <= 360 RewardScore + 201|" +
        "#:TotalScore >= 0 < 2807 Evaluate C 0 0 0:" +
        "TotalScore >= 2807 < 3208 Evaluate C+ 0 0 0:" +
        "TotalScore >= 4010 < 4411 Evaluate A 1 0 0:" +
        "TotalScore >= 5213 <= 99999 Evaluate S+ 3 0 0|" +
        "0|51322|dagger|1|2.52|51324|sword|1|2.52|" +
        "40176|crystal|1|9|51341|seal|1|22.16|856|tool|1|15|" +
        "29104|stone|1|22.16\n";

    private static RewardScoreParser.EvaluateDef ParseSample()
    {
        var path = Path.Combine(Path.GetTempPath(),
            Guid.NewGuid() + ".ini");
        File.WriteAllText(path, SampleIni);
        return RewardScoreParser.Parse(path)[655];
    }

    private static EvaluateInstance NewInstance(
        RewardScoreParser.EvaluateDef def, params int[] members) =>
        new(1, def, members);

    // ---------- Parser ----------

    [Fact]
    public void Parse_DslBuckets()
    {
        var def = ParseSample();
        Assert.Equal(63399, def.BossDefId);
        // KillMonster 4 → elite 32, KillMonster 6 → boss 2,
        // TeamDieNum 1 → die <1.
        Assert.Single(def.EliteRules);
        Assert.Equal(32, def.EliteRules[0].Threshold);
        Assert.Equal(482, def.EliteRules[0].Score);
        Assert.Single(def.BossRules);
        Assert.Equal(2, def.BossRules[0].Threshold);
        Assert.Single(def.DieRules);
        Assert.Empty(def.MonRules);
        // Caps = seuils max (natif GetKillMon/Elite/Boss/TeamDie).
        Assert.Equal(1, def.MaxDeaths);
        Assert.Equal(0, def.MaxMon);
        Assert.Equal(32, def.MaxElite);
        Assert.Equal(2, def.MaxBoss);
    }

    [Fact]
    public void Parse_TimeRulesAndGrades()
    {
        var def = ParseSample();
        Assert.Equal(4, def.TimeRules.Count);
        Assert.Equal(1604, def.TimeBonusScore(120));
        Assert.Equal(802, def.TimeBonusScore(200));
        Assert.Equal(201, def.TimeBonusScore(300));
        Assert.Equal(0, def.TimeBonusScore(999));
        Assert.Equal(4, def.Grades.Count);
        Assert.Equal("C", def.GetGrade(100)!.Name);
        Assert.Equal(0, def.GetGrade(100)!.Opens);
        Assert.Equal("A", def.GetGrade(4200)!.Name);
        Assert.Equal(1, def.GetGrade(4200)!.Opens);
        Assert.Equal("S+", def.GetGrade(6000)!.Name);
        Assert.Equal(3, def.GetGrade(6000)!.Opens);
        Assert.Equal(6, def.RewardItems.Count);
    }

    [Fact]
    public void Parse_RealFile()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "server_us", "Data", "db_decoded", "S_RewardScore.ini");
        if (!File.Exists(path)) return;   // fixture absente en CI
        var defs = RewardScoreParser.Parse(path);
        Assert.True(defs.Count >= 20);    // 20 defs natives 655-674
        var d655 = defs[655];
        Assert.Equal(63399, d655.BossDefId);
        Assert.Equal(0, d655.Flag);       // tirage sans remise
        Assert.Equal(14, d655.RewardItems.Count);
        Assert.True(d655.RewardItems.All(r => r.Weight > 0));
        // Toutes les defs réelles doivent produire 5 boîtes.
        foreach (var def in defs.Values)
            Assert.True(def.DrawRewards(5, new Random(1)).Count == 5,
                $"def {def.Id} node {def.NodeTid} < 5 boîtes");
    }

    // ---------- Tirage ----------

    [Fact]
    public void DrawRewards_Flag0_Unique()
    {
        var def = ParseSample();          // flag = 0 → sans remise
        var drawn = def.DrawRewards(5, new Random(7));
        Assert.Equal(5, drawn.Count);
        Assert.Equal(5, drawn.Select(r => r.ItemId).Distinct().Count());
    }

    // ---------- Compteurs / End ----------

    [Fact]
    public void GainKillScore_BucketsAndCaps()
    {
        var def = ParseSample();
        var ev = NewInstance(def, 1001);
        // cat 3/4 → elite (cap 32), cat 5/6 → boss (cap 2),
        // cat 2 → mon (cap 0 → clampé à 0 mais score brut compte).
        Assert.True(ev.GainKillScore(4, 100));
        Assert.Equal(1, ev.EliteKills);
        Assert.True(ev.GainKillScore(5, 200));
        Assert.Equal(1, ev.BossKills);
        for (var i = 0; i < 5; i++) ev.GainKillScore(6, 10);
        Assert.Equal(2, ev.BossKills);    // cappé à MaxBoss=2
        Assert.True(ev.GainKillScore(2, 50));
        Assert.Equal(0, ev.MonKills);     // cappé à MaxMon=0
        Assert.Equal(100 + 200 + 50 + 50, ev.RawScore); // Σ exp brute
        Assert.False(ev.GainKillScore(9, 10));  // type inconnu ignoré
        Assert.False(ev.GainKillScore(4, 0));   // score 0 → return natif
    }

    [Fact]
    public void CharacterDie_Capped()
    {
        var def = ParseSample();          // MaxDeaths = 1
        var ev = NewInstance(def, 1001);
        ev.CharacterDie();
        ev.CharacterDie();
        ev.CharacterDie();
        Assert.Equal(1, ev.Deaths);       // cappé au seuil DSL
    }

    /// <summary>Instance "S+" — 30 kills normaux ×100 + 32 elite ×10
    /// + 2 boss ×20, 0 mort, 120 s → total = 401 + 482 + 361 +
    /// (3000+320+40) + 1604 = 6208 → grade S+ (opens 3).</summary>
    private static EvaluateInstance EndedInstance(
        RewardScoreParser.EvaluateDef def, params int[] members)
    {
        var ev = NewInstance(def, members);
        for (var i = 0; i < 30; i++) ev.GainKillScore(2, 100);
        for (var i = 0; i < 32; i++) ev.GainKillScore(4, 10);
        for (var i = 0; i < 2; i++) ev.GainKillScore(6, 20);
        ev.End(120, new Random(3));
        return ev;
    }

    [Fact]
    public void End_FormulaAndBoxes()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001, 1002);
        Assert.Equal(120, ev.ElapsedSeconds);
        // total = mon*2 + die + elite + boss + raw + time
        //       = 0 + 401 + 482 + 361 + 3360 + 1604 = 6208.
        Assert.Equal(6208, ev.TotalScore);
        Assert.Equal("S+", ev.Grade!.Name);
        Assert.Equal(3, ev.Grade.Opens);
        Assert.Equal(2, ev.Boxes.Count);
        Assert.All(ev.Boxes.Values, b => Assert.Equal(5, b.Count));
    }

    // ---------- LotteryOpen ----------

    [Fact]
    public void Lottery_PickDecrements()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001);      // grade S+ → 3 opens
        var fx = ev.LotteryOpen(1001, 0, _ => true);
        var pick = Assert.Single(fx);
        Assert.Equal(EvaluateInstance.LotteryEffectKind.Pick, pick.Kind);
        Assert.Equal(2, pick.Opens);            // 3 → 2
        Assert.Equal(0, pick.Index);
        Assert.True(pick.ItemId > 0);
        // Boîte déjà ouverte → re-pick = no-op natif (chosen ≥ 0).
        Assert.Empty(ev.LotteryOpen(1001, 0, _ => true));
    }

    [Fact]
    public void Lottery_PickExhausted_Error1fab()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001);
        ev.LotteryOf(1001).Opens = 0;           // force épuisement
        var fx = ev.LotteryOpen(1001, 2, _ => true);
        var err = Assert.Single(fx);
        Assert.Equal(EvaluateInstance.LotteryEffectKind.Error, err.Kind);
        Assert.Equal(0x1fab, err.ErrorCode);
    }

    [Fact]
    public void Lottery_BuyFlow()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001);   // grade S+ : cost=0
        var info = ev.LotteryOf(1001);
        info.Opens = 0; info.Buys = 2;
        // cost 0 → erreur 0x232 natif (GainGold jamais appelé).
        var charged = 0;
        var fx = ev.LotteryOpen(1001, 5, a => { charged = a; return true; });
        var err = Assert.Single(fx);
        Assert.Equal(0x232, err.ErrorCode);
        Assert.Equal(0, charged);         // rien débité
        Assert.Equal(2, info.Buys);
    }

    [Fact]
    public void Lottery_BuyChargesAndGrants()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001);
        // Grade forcé APRÈS End (il écrase via GetGrade) : cost=2 →
        // 2000 or par achat, opens 1, buys 1 — puis LotteryOf recrée
        // l'état depuis ce grade.
        SetGrade(ev, new RewardScoreParser.GradeRule
        {
            Range = new RewardScoreParser.RangeRule(1, 0, 2, 99999),
            Name = "S+", Opens = 1, Buys = 1, Cost = 2,
        });
        ev.Lottery.Remove(1001);          // force la relecture du grade
        var charged = 0;
        var fx = ev.LotteryOpen(1001, 5, a => { charged = a; return true; });
        Assert.Equal(2000, charged);
        var res = Assert.Single(fx);
        Assert.Equal(EvaluateInstance.LotteryEffectKind.BuyResult,
            res.Kind);
        Assert.Equal(2, res.Opens);       // 1 grade + 1 achetée
        Assert.Equal(6, res.Index);       // buys==0 → sentinelle 6
    }

    /// <summary>Injecte un grade dans l'instance (setter privé via
    /// réflexion — End appelle Def.GetGrade ; ce helper permet de
    /// forcer opens/buys/cost pour tester la loterie).</summary>
    private static void SetGrade(EvaluateInstance ev,
        RewardScoreParser.GradeRule grade)
    {
        var field = typeof(EvaluateInstance).GetProperty("Grade")!;
        field.SetValue(ev, grade);
    }

    [Fact]
    public void Lottery_ClaimFinalizesAndListsUnopened()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001);
        ev.LotteryOf(1001).Opens = 1;     // 1 ouverture offerte
        // Ouvre la boîte 0 puis claim -1.
        var pick = ev.LotteryOpen(1001, 0, _ => true);
        Assert.Contains(pick, f =>
            f.Kind == EvaluateInstance.LotteryEffectKind.Pick);
        var fx = ev.LotteryOpen(1001, -1, _ => true);
        Assert.Equal(EvaluateInstance.LotteryEffectKind.Finalize,
            fx[0].Kind);
        Assert.Equal(-1, fx[0].Index);
        // Les 4 boîtes non ouvertes sont listées (itemId, index).
        Assert.Equal(4, fx.Count(f =>
            f.Kind == EvaluateInstance.LotteryEffectKind.Unopened));
        Assert.Equal(new[] { 1, 2, 3, 4 },
            fx.Where(f =>
                f.Kind == EvaluateInstance.LotteryEffectKind.Unopened)
              .Select(f => f.Index).OrderBy(i => i).ToArray());
        // Un item gagné en attente de mail.
        Assert.Single(ev.ClaimWonItems(1001));
    }

    [Fact]
    public void Lottery_AutoOpen7()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001);
        var info = ev.LotteryOf(1001);
        info.Opens = 2;
        var fx = ev.LotteryOpen(1001, 7, _ => true);
        Assert.Empty(fx);                 // auto-open = pas de réponse
        Assert.Equal(0, info.Opens);
        Assert.Equal(2, ev.Won[1001].Count);      // 2 boîtes ouvertes
        Assert.Equal(2, ev.Boxes[1001].Count(b => b.Chosen >= 0));
    }

    // ---------- Wire 607 ----------

    [Fact]
    public void BuildStatus_Wire()
    {
        var body = EvaluatePackets.BuildStatus(1, 10800);
        Assert.Equal(12, body.Length);
        Assert.Equal(0, BitConverter.ToInt32(body, 0));     // type 0
        Assert.Equal(1, BitConverter.ToInt32(body, 4));     // status
        Assert.Equal(10800, BitConverter.ToInt32(body, 8)); // sec
        // Reset CharacterLeave : [0][-1][0].
        var rst = EvaluatePackets.BuildStatus(-1, 0);
        Assert.Equal(-1, BitConverter.ToInt32(rst, 4));
    }

    [Fact]
    public void BuildCounters_Wire()
    {
        var def = ParseSample();
        var ev = NewInstance(def, 1001);
        ev.GainKillScore(4, 100);
        ev.CharacterDie();
        var b = EvaluatePackets.BuildCounters(ev);
        Assert.Equal(40, b.Length);       // type + 9 i32
        Assert.Equal(1, BitConverter.ToInt32(b, 0));
        Assert.Equal(100, BitConverter.ToInt32(b, 4));   // raw
        Assert.Equal(1, BitConverter.ToInt32(b, 8));     // deaths
        Assert.Equal(0, BitConverter.ToInt32(b, 12));    // mon
        Assert.Equal(1, BitConverter.ToInt32(b, 16));    // elite
        Assert.Equal(0, BitConverter.ToInt32(b, 20));    // boss
        Assert.Equal(1, BitConverter.ToInt32(b, 24));    // maxDeaths
        Assert.Equal(0, BitConverter.ToInt32(b, 28));    // maxMon
        Assert.Equal(32, BitConverter.ToInt32(b, 32));   // maxElite
        Assert.Equal(2, BitConverter.ToInt32(b, 36));    // maxBoss
    }

    [Fact]
    public void BuildLottery_Wire()
    {
        // [3][opens][itemId][count][index] — sentinelle reset -2.
        var b = EvaluatePackets.BuildLottery(2, 51322, 1, 3);
        Assert.Equal(20, b.Length);
        Assert.Equal(3, BitConverter.ToInt32(b, 0));
        Assert.Equal(2, BitConverter.ToInt32(b, 4));
        Assert.Equal(51322, BitConverter.ToInt32(b, 8));
        Assert.Equal(1, BitConverter.ToInt32(b, 12));
        Assert.Equal(3, BitConverter.ToInt32(b, 16));
        var rst = EvaluatePackets.BuildLottery(0, 0, 0, -2);
        Assert.Equal(-2, BitConverter.ToInt32(rst, 16));
    }

    [Fact]
    public void BuildResult_Wire()
    {
        var def = ParseSample();
        var ev = EndedInstance(def, 1001);
        var b = EvaluatePackets.BuildResult(ev);
        Assert.Equal(2, BitConverter.ToInt32(b, 0));        // type 2
        Assert.Equal(120, BitConverter.ToInt32(b, 4));      // elapsed
        Assert.Equal(1604, BitConverter.ToInt32(b, 8));     // timeBonus
        Assert.Equal(3360, BitConverter.ToInt32(b, 60));    // rawScore
        Assert.Equal(6208, BitConverter.ToInt32(b, 64));    // totalScore
        // grade string à +68 : [u16 len][chars] puis 3×i32.
        var gradeLen = BitConverter.ToUInt16(b, 68);
        Assert.Equal("S+", System.Text.Encoding.ASCII
            .GetString(b, 70, gradeLen));
        var off = 70 + gradeLen;
        Assert.Equal(3, BitConverter.ToInt32(b, off));      // opens
        Assert.Equal(0, BitConverter.ToInt32(b, off + 4));  // buys
        Assert.Equal(0, BitConverter.ToInt32(b, off + 8));  // cost
    }
}
