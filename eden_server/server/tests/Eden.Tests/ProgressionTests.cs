using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

public class ProgressionTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db_decoded";

    [Fact]
    public void ParseLevels_LoadsRealTable()
    {
        var levels = ProgressionParsers.ParseLevels(DbDir + "/S_Level.ini");
        Assert.True(levels.Count >= 100);
        Assert.Equal(1, levels[0].Level);
        Assert.True(levels[0].ExpNeeded >= 0);
    }

    [Fact]
    public void ParseLevels_LoadsPetExpCurve()
    {
        // S_Level col 24 (0-indexée) → CLevelData+0x64, lue par
        // CLevelDBO::LoadPetExp (vec[key-1]) ; CKFElf::GainExp
        // consomme vec[level] = col24 de la ligne level+1.
        var levels = ProgressionParsers.ParseLevels(DbDir + "/S_Level.ini");
        var l2 = levels.First(l => l.Level == 2);
        var l3 = levels.First(l => l.Level == 3);
        var l5 = levels.First(l => l.Level == 5);
        Assert.Equal(32, l2.PetExp);
        Assert.Equal(48, l3.PetExp);
        Assert.Equal(123, l5.PetExp);
    }

    [Fact]
    public async Task GrantExp_LevelsUpOnThreshold()
    {
        var levels = ProgressionParsers.ParseLevels(DbDir + "/S_Level.ini");
        var repo = new FakeCharacterRepository();
        var ch = new Eden.Core.Database.Entities.PlayerCharacterEntity
        { Id = 1, Level = 1, Experience = 0 };
        repo.Characters.Add(ch);
        var svc = new ProgressionService(repo, levels);

        var level1Exp = svc.ExpForLevel(1);
        var (newLevel, gained) = await svc.GrantExpAsync(1, level1Exp, 1, 0);
        Assert.Equal(2, newLevel);
        Assert.Equal(1, gained);
    }

    [Fact]
    public async Task GrantExp_MultiLevelOnBigExp()
    {
        var levels = ProgressionParsers.ParseLevels(DbDir + "/S_Level.ini");
        var repo = new FakeCharacterRepository();
        repo.Characters.Add(new Eden.Core.Database.Entities.PlayerCharacterEntity
        { Id = 1, Level = 1, Experience = 0 });
        var svc = new ProgressionService(repo, levels);

        var bigExp = svc.ExpForLevel(1) + svc.ExpForLevel(2) + 10;
        var (newLevel, gained) = await svc.GrantExpAsync(1, bigExp, 1, 0);
        Assert.Equal(3, newLevel);
        Assert.Equal(2, gained);
    }

    [Fact]
    public async Task GrantExp_Level30()
    {
        var levels = ProgressionParsers.ParseLevels(DbDir + "/S_Level.ini");
        var repo = new FakeCharacterRepository();
        repo.Characters.Add(new Eden.Core.Database.Entities.PlayerCharacterEntity
        { Id = 1, Level = 30, Experience = 2500 });
        var svc = new ProgressionService(repo, levels);
        // Seuil lv31 = 731268 : 2500+750000=752500 → 1 seul level-up
        // (lv32 = 749284 > 21232 restants).
        var (newLevel, gained) = await svc.GrantExpAsync(1, 750000, 30, 2500);
        Assert.Equal(31, newLevel);
        Assert.Equal(1, gained);
    }

    [Fact]
    public async Task GrantExp_CapsAtMaxLevel()
    {
        var levels = ProgressionParsers.ParseLevels(DbDir + "/S_Level.ini");
        var repo = new FakeCharacterRepository();
        var maxLevel = levels[^1].Level;
        repo.Characters.Add(new Eden.Core.Database.Entities.PlayerCharacterEntity
        { Id = 1, Level = maxLevel, Experience = 0 });
        var svc = new ProgressionService(repo, levels);

        var (newLevel, gained) = await svc.GrantExpAsync(1, 999_999_999, maxLevel, 0);
        Assert.Equal(maxLevel, newLevel);
        Assert.Equal(0, gained);
    }
}
