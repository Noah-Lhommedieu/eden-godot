using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Plan futur-feature/12 : texte des annonces monde
/// world boss / mutants (316 ClientTextOutputIndex) FR/EN.</summary>
public class WorldBossTextTests
{
    private static string? Monster(int id) => id switch
    {
        58001 => "Justin Allspore",
        60761 => "Posa",
        _ => null,
    };
    private static string? Spell(int id) => id == 59247 ? "Fatal Countdown" : null;
    private static string? Idx(int i, string a) => i switch
    {
        1647 => "%s1 will explode when the countdown ends!".Replace("%s1", a),
        6044 => "The Power of Destruction has infected X!",
        _ => null,
    };
    private static string Tr(string s) => s;

    private static string? C(short i, string a, Func<string, string>? tr = null) =>
        WorldBossText.Compose(i, a, Monster, Spell, Idx, tr ?? Tr);

    [Fact]
    public void Kill_UsesMonsterNameAndKiller() =>
        Assert.Equal("Justin Allspore has been slain by Bob!",
            C(WorldBossText.ExtKillAnnounce, "58001\tBob"));

    [Fact]
    public void Kill_UnknownMonster_FallsBackToId() =>
        Assert.Equal("#1 has been slain by Bob!",
            C(WorldBossText.ExtKillAnnounce, "1\tBob"));

    [Fact]
    public void MutantSpawn_AndKilled()
    {
        Assert.Equal(
            "A mutant monster, Posa, has appeared somewhere in the world!",
            C(WorldBossText.ExtMutantSpawn, "60761"));
        Assert.Equal("The mutant monster Posa has been defeated by Eve!",
            C(WorldBossText.ExtMutantKilled, "60761\tEve"));
    }

    [Fact]
    public void Templates_AreTranslatedThroughUiTextEn()
    {
        var t = C(WorldBossText.ExtKillAnnounce, "58001\tBob",
            s => s == WorldBossText.KillTemplate ? "{0} a été terrassé par {1} !" : s);
        Assert.Equal("Justin Allspore a été terrassé par Bob !", t);
    }

    [Fact]
    public void Countdown_ResolvesSpellIdToName_OrKeepsFreeText()
    {
        Assert.Equal("Fatal Countdown will explode when the countdown ends!",
            C(WorldBossText.CountdownIndex, "59247"));
        Assert.Equal("Boom will explode when the countdown ends!",
            C(WorldBossText.CountdownIndex, "Boom"));
    }

    [Fact]
    public void NativeIndex_PassesThrough_AndUnknownIsNull()
    {
        Assert.Equal("The Power of Destruction has infected X!",
            C(6044, ""));
        Assert.Null(C(9999, ""));
    }

    [Theory]
    [InlineData((short)6044, true)]
    [InlineData((short)6053, true)]
    [InlineData((short)6054, false)]
    [InlineData((short)1647, true)]
    [InlineData((short)30001, true)]
    [InlineData((short)30003, true)]
    [InlineData((short)1524, false)]
    public void IsAnnouncement(short idx, bool expected) =>
        Assert.Equal(expected, WorldBossText.IsAnnouncement(idx));

    [Fact]
    public void ExtensionRange_MatchesServerConstants()
    {
        Assert.True(WorldBossText.IsExtension(30001));
        Assert.True(WorldBossText.IsExtension(30003));
        Assert.False(WorldBossText.IsExtension(6044));
    }

    /// <summary>Chaque gabarit EN a son entrée FR dans le TSV (règle
    /// FR/EN obligatoire) — y compris les textes du courrier mutant.</summary>
    [Fact]
    public void AllTemplates_HaveFrenchEntries()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? tsv = null;
        while (dir != null && tsv == null)
        {
            var c = Path.Combine(dir.FullName, "gamedata",
                "ui_text_en_fr.tsv");
            if (File.Exists(c)) tsv = c;
            dir = dir.Parent;
        }
        Assert.NotNull(tsv);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in File.ReadLines(tsv!)
                     .Select(l => l.Split('\t')).Where(p => p.Length >= 2))
            keys[p[0]] = p[1];
        foreach (var k in new[]
        {
            WorldBossText.KillTemplate, WorldBossText.MutantSpawnTemplate,
            WorldBossText.MutantKilledTemplate, "Mutant Expedition",
            "Mutant hunt reward",
            "Thank you for taking on the mutant monster. Here are your Broken Red Crystal Shards.",
        })
        {
            Assert.True(keys.TryGetValue(k, out var fr), $"manque FR : {k}");
            Assert.False(string.IsNullOrWhiteSpace(fr));
            Assert.NotEqual(k, fr);
        }
        // Placeholders conservés ({0}/{1}).
        Assert.Contains("{0}", keys[WorldBossText.KillTemplate]);
        Assert.Contains("{1}", keys[WorldBossText.KillTemplate]);
    }
}
