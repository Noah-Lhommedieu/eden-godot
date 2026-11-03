using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class DungeonTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static DungeonTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseBattlefield_LoadsAll()
    {
        var defs = BattlefieldParser.Parse(DbDir + "/S_Battlefield.ini");
        Assert.NotEmpty(defs);
        var b = defs[0];
        Assert.Equal(1, b.Id);
        Assert.Equal(3, b.RequiredPlayers);
        Assert.Equal(16756, b.MapId);
        Assert.Equal(5, b.MinLevel);
        Assert.Equal(29000, b.RewardTiers1[0]);
    }

    [Fact]
    public void Dungeon_EnterRequiresLevelAndSize()
    {
        var defs = BattlefieldParser.Parse(DbDir + "/S_Battlefield.ini");
        var svc = new DungeonService(defs);
        Assert.False(svc.CanEnter(1, 1, 3));   // level < MinLevel(5)
        Assert.True(svc.CanEnter(1, 10, 3));
        Assert.False(svc.CanEnter(1, 10, 4));  // trop de joueurs
        var inst = svc.Enter(1, new[] { 1, 2, 3 }, 10);
        Assert.NotNull(inst);
        Assert.Equal(DungeonService.DungeonState.InProgress, inst!.State);
        var rewards = svc.Complete(inst.InstanceId, 0);
        Assert.Equal(29000, rewards[0]);
    }
}
