using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Game;
using Xunit;

namespace Eden.Tests;

/// <summary>Normal / Countdown skills d'un mob — port de
/// <c>CAI::DetermineAttackMode</c> @0x084d8a60 (voir MobSkillRules).</summary>
public class MobSkillRulesTests
{
    static MobSkillRulesTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // S_Ai ai=14 : seuils 100/70/35, chances 40/40/40, cooldown 12 s,
    // sorts à seuil 51023 51023 51023.
    private static ProgressionParsers.AiProfile Ai() => new()
    {
        Id = 14,
        Values = new[] { 14, 0, 0, 0, 0, 100, 40, 70, 40, 35, 40, 9001, 9002,
            0, 12, 51023, 12, 51024, 12, 51025 },
    };

    private static MonsterParser.MonsterDef Def()
    {
        var raw = new string[50];
        Array.Fill(raw, "");
        raw[44] = "51001"; raw[45] = "51002"; raw[46] = "51003";
        return new MonsterParser.MonsterDef { Id = 1, Raw = raw };
    }

    [Theory]
    [InlineData(100, 1)] [InlineData(71, 1)] [InlineData(70, 2)]
    [InlineData(36, 2)] [InlineData(35, 3)] [InlineData(5, 3)]
    public void Stage_FollowsHpThresholds(float pct, int stage) =>
        Assert.Equal(stage, MobSkillRules.Stage(Ai(), pct));

    [Fact]
    public void Countdown_FirstCastAsSoonAsStageReached_ThenCooldown()
    {
        var last = new DateTime[3];
        var now = DateTime.UtcNow;
        var p = MobSkillRules.Choose(Def(), Ai(), 65, now, last, new Random(1));
        Assert.Equal(new MobSkillRules.Pick(51024, true, 1), p);
        last[1] = now;
        // dans le cooldown : plus de countdown (seulement du normal)
        for (var i = 0; i < 50; i++)
        {
            var q = MobSkillRules.Choose(Def(), Ai(), 65,
                now.AddSeconds(5), last, new Random(i));
            Assert.True(q == null || !q.Value.Countdown);
        }
        // cooldown écoulé : relance
        Assert.True(MobSkillRules.Choose(Def(), Ai(), 65,
            now.AddSeconds(13), last, new Random(2))!.Value.Countdown);
    }

    [Fact]
    public void NormalSkills_OnlyUnlockedStagesAndChance()
    {
        var last = new DateTime[3] { DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow };
        var seen = new HashSet<int>();
        for (var i = 0; i < 400; i++)
            if (MobSkillRules.Choose(Def(), Ai(), 90, DateTime.UtcNow, last,
                    new Random(i)) is { } p) seen.Add(p.SpellId);
        Assert.Equal(new HashSet<int> { 51001 }, seen);   // palier 1 : sort 0 seul
    }

    [Fact]
    public void AboveFirstThreshold_NothingCast()
    {
        var ai = Ai(); ai.Values[5] = 50;
        Assert.Null(MobSkillRules.Choose(Def(), ai, 80, DateTime.UtcNow,
            new DateTime[3], new Random(1)));
    }

    [Fact]
    public void NativeProfiles_Exist_InData()
    {
        var path = TestPaths.RepoRoot + "/server_us/Data/db/S_Ai.ini";
        var ai = ProgressionParsers.ParseAiProfiles(path).First(p => p.Id == 14);
        Assert.Equal(new[] { 51023, 51023, 51023 },
            MobSkillRules.CountdownSpells(ai));
        Assert.Equal(new[] { 100, 70, 35 }, MobSkillRules.CountdownThresholds(ai));
    }
}
