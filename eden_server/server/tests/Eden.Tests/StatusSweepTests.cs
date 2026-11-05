using Eden.Core.Game.Services;
using Eden.Zone;
using EdenClient.Data.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Eden.Tests;

/// <summary>
/// Balayage de TOUS les sorts : un sort n'engendre un status que si son
/// enchant de résultat agit sur une durée (définition propriétaire,
/// <c>docs/knowledge/definitions.md</c>). Le classifieur
/// <see cref="StatusKinds"/> (partagé avec le client) doit coller aux
/// décisions du serveur (<c>ZoneEntryFlow.InstantOnlyCommands</c>).
/// </summary>
public class StatusSweepTests(ITestOutputHelper output)
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    private static readonly GameDataService Data = Load();

    private static GameDataService Load()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        return gd;
    }

    private static List<(int Id, double[] Args)> Cmds(EnchantService e, int id)
        => e.GetUseCommands(id).Select(c => (c.CommandId, c.Args)).ToList();

    [Fact]
    public void InstantCommandLists_ServerAndClientAreIdentical()
        => Assert.True(StatusKinds.InstantCommands.SetEquals(
            ZoneEntryFlow.InstantOnlyCommands));

    [Fact]
    public void EveryEnchant_NoneKind_MatchesServerInstantOnly()
    {
        var svc = new EnchantService(Data.Enchants);
        var bad = new List<int>();
        foreach (var e in Data.Enchants)
        {
            var cmds = Cmds(svc, e.Id);
            var instantOnly = cmds.Count == 0 ||
                cmds.All(c => ZoneEntryFlow.InstantOnlyCommands.Contains(c.Id));
            var none = StatusKinds.Classify(e.Id, cmds, true) == StatusKind.None;
            if (instantOnly != none) bad.Add(e.Id);
        }
        Assert.Empty(bad);
    }

    /// <summary>Parcourt chaque sort ayant un enchant de résultat et
    /// range le status qu'il créerait. Guérison (50028 → 52028, dispel
    /// 1824 seul) et tout sort instantané ne créent AUCUN status.</summary>
    [Fact]
    public void EverySpell_StatusCreatedOnlyForDurableEffects()
    {
        var svc = new EnchantService(Data.Enchants);
        var counts = new Dictionary<StatusKind, int>();
        var instantSpells = new List<int>();
        var stateSpells = new List<string>();
        foreach (var sp in Data.Spells.Where(s => s.ResultSpellId > 0))
        {
            var cmds = Cmds(svc, sp.ResultSpellId);
            var kind = StatusKinds.Classify(sp.ResultSpellId, cmds,
                !svc.IsDebuff(sp.ResultSpellId));
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            if (kind == StatusKind.None) instantSpells.Add(sp.Id);
            if (kind == StatusKind.State)
                stateSpells.Add($"{sp.Id}->{sp.ResultSpellId}:" +
                    string.Join(",", cmds.Select(c => c.Id)));
        }
        output.WriteLine("spells par catégorie : " + string.Join(", ",
            counts.Select(kv => $"{kv.Key}={kv.Value}")));
        output.WriteLine("états (échantillon) : " +
            string.Join(" ", stateSpells.Take(40)));
        // Guérison : sort 50028, enchant 52028 = dispel seul — le
        // classifieur client doit le rendre invisible (aucun status).
        Assert.Contains(50028, instantSpells);
        Assert.True(IsInstantOnlyEnchant(svc, 52028));
        Assert.Equal(StatusKind.None,
            StatusKinds.Classify(52028, Cmds(svc, 52028), true));
    }

    private static List<(int Id, double[] Args)> L(
        params (int Id, double[] Args)[] c) => c.ToList();

    private static bool IsInstantOnlyEnchant(EnchantService svc, int id)
    {
        var cmds = svc.GetUseCommands(id);
        return cmds.Count == 0 ||
            cmds.All(c => ZoneEntryFlow.InstantOnlyCommands.Contains(c.CommandId));
    }

    [Fact]
    public void BeginnerPortalState_IsAState_NotBuff()
    {
        var svc = new EnchantService(Data.Enchants);
        // État persistant (commande 2047|1 non instantanée, durée -2
        // posée par OnMigrationDone natif) : pas instantané, et classé
        // état — jamais buff ni invisible.
        Assert.False(IsInstantOnlyEnchant(svc, 55869));
        Assert.Equal(StatusKind.State,
            StatusKinds.Classify(55869, Cmds(svc, 55869), true));
    }

    /// <summary>Un enchant mêlant une commande instantanée et une
    /// commande durable (stat) n'est PAS instantané : le natif lui
    /// crée un CEnchant (`effet+0x12c != 0` dès qu'une commande est
    /// gérée) — le statut doit survivre côté serveur et être classé
    /// d'après la commande durable côté client.</summary>
    [Fact]
    public void MixedCommands_InstantPlusStat_KeepsStatus()
    {
        var mixed = L((1824, new double[6]),
                      (2001, new[] { 5.0, 0, 0, 0, 0, 0 }));
        // Côté serveur : au moins une commande non instantanée →
        // AddPlayerEnchantAsync crée le statut.
        Assert.False(mixed.All(c =>
            ZoneEntryFlow.InstantOnlyCommands.Contains(c.Id)));
        // Côté client : pas None, classé Buff par le signe du 2001.
        Assert.NotEqual(StatusKind.None,
            StatusKinds.Classify(1, mixed, true));
        Assert.Equal(StatusKind.Buff,
            StatusKinds.Classify(1, mixed, true));
    }

    [Theory]
    [InlineData(2083, 5.0, StatusKind.Buff)]    // ATQ % +5
    [InlineData(2083, -5.0, StatusKind.Nerf)]   // ATQ % -5
    public void StatSign_DecidesBuffOrNerf(int cmd, double v, StatusKind want)
        => Assert.Equal(want, StatusKinds.Classify(1,
            L((cmd, new[] { v, 0, 0, 0, 0, 0 })), true));

    [Fact]
    public void CastSpeedAndAttackSpeed_NegativeIsFaster_IsBuff()
    {
        Assert.Equal(StatusKind.Buff, StatusKinds.Classify(1,
            L((2052, new[] { -8.0, 0, 0, 0, 0, 0 })), false));
        Assert.Equal(StatusKind.Buff, StatusKinds.Classify(1,
            L((2053, new[] { -5.0, 0, 0, 0, 0, 0 })), false));
    }

    [Fact]
    public void RideCommand_IsState_EvenWithSpeedBonus()
        => Assert.Equal(StatusKind.State, StatusKinds.Classify(1,
            L((2134, new[] { 253020.0, 1, 1, 0, 0, 0 }),
              (2051, new[] { 55.0, 0, 0, 0, 0, 0 })), true));

    [Fact]
    public void DotOnly_IsState()
        => Assert.Equal(StatusKind.State, StatusKinds.Classify(1,
            L((3002, new[] { -50.0, 0, 0, 0, 0, 0 })), false));
}
