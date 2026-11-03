using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

/// <summary>Commandes GM (B3) — dispatch <see cref="GmCommandService"/>
/// + gardes d'annulation d'enchant (B5) sur les vraies tables
/// S_RootCmds/S_Enchant.</summary>
public class GmCommandTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db_decoded";

    private static GmCommandService NewService() =>
        GmCommandService.LoadFromDataDir(DbDir);

    [Fact]
    public void RootCmds_LoadsAllNativeCommands()
    {
        var svc = NewService();
        // ~330 entrées S_RootCmds + S_GMToolCmds — inventaire réel.
        Assert.True(svc.CommandCount >= 300);
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsNull()
    {
        var svc = NewService();
        Assert.Null(svc.Execute("commande_inexistante_xyz 1 2", 4));
    }

    [Fact]
    public void Execute_AccessGate_BlocksLowPrivilege()
    {
        var svc = NewService();
        // `status` (accès 1) est refusé à un gmLevel 0.
        Assert.Null(svc.Execute("status", 0));
    }

    [Fact]
    public void Execute_KnownButUnimplemented_ExplicitMessage()
    {
        var svc = NewService();
        // Une commande ini sans handler → message « non supporté »
        // explicite (plus de TODO silencieux).
        var r = svc.Execute("change_weather 1", 4);
        Assert.NotNull(r);
        Assert.Contains("non supporté", r);
    }

    [Fact]
    public void ListCommands_MarksRegisteredAndLocalImplementations()
    {
        var svc = NewService();
        svc.RegisterAsync("clone_item",
            (ctx, args) => Task.FromResult<string?>("ok"));
        svc.MarkImplemented("self_use_effect");
        var list = svc.ListCommands();
        Assert.Contains(list, c => c.Name == "clone_item" && c.Implemented);
        Assert.Contains(list,
            c => c.Name == "self_use_effect" && c.Implemented);
        // Une commande ini sans implémentation reste marquée non.
        Assert.Contains(list,
            c => c.Name == "change_weather" && !c.Implemented);
    }

    [Fact]
    public void CanCancelByClient_MatchesNativeGuards()
    {
        var enchants = EnchantService.LoadFromDataDir(DbDir);
        // Datové (CancelPlayerEnchant @0x805fe00 : col7 bit0 « buff »
        // requis, col8 bit0x10 « can't abandon ») :
        // 52105 buff clerc c7=1 → annulable ; 52026 debuff c7=2 → non.
        Assert.True(enchants.CanCancelByClient(52105));
        Assert.False(enchants.CanCancelByClient(52026));
        // Id hors table → refus (silencieux natif).
        Assert.False(enchants.CanCancelByClient(999999));
    }

    [Fact]
    public void UseCommands_ParsesEnchantBlocks()
    {
        var enchants = EnchantService.LoadFromDataDir(DbDir);
        // Les montures portent la commande 2134 (Ride) — détection
        // utilisée par les gardes d'annulation et l'usage d'item.
        Assert.NotNull(enchants.GetRideInfo(58991));
        Assert.NotNull(enchants.GetRideInfo(55850));
    }

    [Fact]
    public async Task AddGoldAsync_FakeRepo_ClampsLikeNative()
    {
        // Sémantique GainGold natif : char+0x4b0 borné [0,999999999].
        var repo = new FakeCharacterRepository();
        repo.Characters.Add(new Eden.Core.Database.Entities
            .PlayerCharacterEntity { Id = 1, Gold = 100 });
        Assert.Equal(150, await repo.AddGoldAsync(1, 50));
        Assert.Equal(999_999_999, await repo.AddGoldAsync(1, 2_000_000_000));
        Assert.Equal(0, await repo.AddGoldAsync(1, -999_999_999));
    }
}
