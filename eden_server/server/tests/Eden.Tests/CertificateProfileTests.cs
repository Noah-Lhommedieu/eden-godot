using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Classes;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Eden.Zone.Combat;
using Xunit;
using Xunit.Abstractions;

namespace Eden.Tests;

/// <summary>
/// Chaîne « certificat posé → valeur affichée dans le Profil » : pour CHAQUE
/// certificat de classe (c_class cols 22-29), la contribution du stage
/// « licenses » est versée dans le snapshot comme le fait
/// <c>ProfileStatEmitter.SendAsync</c> (ApplyContribution + formules natives)
/// et le vecteur des valeurs du 296/493 doit changer — sauf les effets
/// documentés sans champ de profil (aggro, coût PM, hooks de combat).
/// </summary>
public class CertificateProfileTests(ITestOutputHelper output)
{
    private static readonly GameDataService Data = Load();

    private static GameDataService Load()
    {
        var gd = new GameDataService(TestPaths.RepoRoot + "/server_us/Data/db");
        gd.LoadAll();
        return gd;
    }

    private sealed class MemRepo : ILicenseRepository
    {
        public readonly List<LicenseEntity> Rows = new();
        public Task<IReadOnlyList<LicenseEntity>> GetLicensesAsync(int playerId)
            => Task.FromResult<IReadOnlyList<LicenseEntity>>(Rows.ToList());
        public Task AddLicenseAsync(int playerId, short itemId, short loc)
        { Rows.Add(new LicenseEntity { PlayerId = playerId, ItemId = itemId, Loc = loc }); return Task.CompletedTask; }
        public Task SetLicenseLocAsync(int playerId, short itemId, short loc) => Task.CompletedTask;
        public Task RemoveLicenseAsync(int playerId, short itemId) => Task.CompletedTask;
    }

    /// <summary>Vecteur des valeurs de profil (296 + 493 + vitals) pour un
    /// perso lv 100 guerrier avec la contribution donnée.</summary>
    private static double[] Profile(StatContribution contrib)
    {
        var lvl = Data.Levels.First(l => l.Level == 100);
        var cls = ClassSkillTable.ClassFor(Data.Classes, 1, 1);
        var pct = cls?.StatPercents ?? new int[9];
        int P(int i) => i < pct.Length && pct[i] > 0 ? pct[i] : 100;
        var eq = new EquipmentStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new int[9]);
        var snap = CombatantSnapshot.FromPlayer(100, lvl, cls?.StatPercents,
            eq, null, null, false, 1);
        snap.ApplyContribution(contrib);
        var p = NativeCombatFormulas.ResolveParameters(Data.Parameters);
        double R(StatIndex s, int v, int pc) => StatPipeline.ResolveStat(contrib, s, v, P(pc));
        var v = new List<double>
        {
            R(StatIndex.Str, lvl.Str, 4), R(StatIndex.Int, lvl.Int, 6),
            R(StatIndex.Agi, lvl.Con, 5), R(StatIndex.Wis, lvl.Wis, 7),
            R(StatIndex.Lck, lvl.Agi, 8),
            R(StatIndex.Hp, lvl.MaxHp, 2), R(StatIndex.Mp, lvl.MaxMp, 3),
            NativeCombatFormulas.MeleeDamage(snap, p),
            NativeCombatFormulas.MagicDamage(snap, p),
            NativeCombatFormulas.PhysicalDefence(snap),
            NativeCombatFormulas.ParryRate(snap, p), NativeCombatFormulas.BlockRate(snap, p),
            NativeCombatFormulas.PhysicalHitRate(snap, p), NativeCombatFormulas.PhysicalDodgeRate(snap, p),
            NativeCombatFormulas.PhysicalCriticalRate(snap, p), NativeCombatFormulas.PhysicalCriticalDamage(snap, p),
            NativeCombatFormulas.MagicCriticalRate(snap, p), NativeCombatFormulas.MagicCriticalDamage(snap, p),
            NativeCombatFormulas.AttackSpeedLevelRate(snap, p), NativeCombatFormulas.CastSpeedLevelRate(snap, p),
            NativeCombatFormulas.Movement(snap), snap.BonusAttackSpeedPercent, snap.BonusCastSpeedPercent,
            NativeCombatFormulas.DefenceReduce(snap, p), NativeCombatFormulas.MovementRate(snap),
            NativeCombatFormulas.CriDmgReduce(snap),
            NativeCombatFormulas.HitLv(snap, p), NativeCombatFormulas.CriLv(snap, p),
            NativeCombatFormulas.MCriLv(snap, p), NativeCombatFormulas.DodgeLv(snap, p),
            NativeCombatFormulas.ParryLv(snap, p), NativeCombatFormulas.BlockLv(snap, p),
            NativeCombatFormulas.AttackSpeedLv(snap, p), NativeCombatFormulas.MagicSpeedLv(snap, p),
        };
        v.AddRange(contrib.ResistAdds.Select(x => (double)x));
        return v.ToArray();
    }

    /// <summary>Effets de certificats SANS champ de profil (documentés dans
    /// certificate-stats.md) : aggro (2122), coût PM (2015), procs/hooks
    /// de combat (2031/32, 2034, 2055, 2061, 2145, 2151).</summary>
    private static readonly HashSet<int> NoProfileField = new()
    {
        1031, 2031, 2032, 2034, 2055, 2061, 2122, 2015, 2145, 2151,
    };

    [Fact]
    public async Task EveryCertificate_ChangesAProfileValue_OrIsDocumentedNoProfile()
    {
        var svc = new EnchantService(Data.Enchants);
        var certItems = Data.Classes.SelectMany(c => c.CertificateIds)
            .Where(id => id > 0).Distinct().ToList();
        Assert.True(certItems.Count > 50);
        var baseline = Profile(new StatContribution());
        var silent = new List<string>();
        var ok = 0;
        var documented = 0;
        foreach (var itemId in certItems)
        {
            var repo = new MemRepo();
            await repo.AddLicenseAsync(1, unchecked((short)itemId), 1);
            var lic = new LicenseService(repo, Data.Items.ToDictionary(i => i.Id),
                Data.EquipSetPool!, svc, Data.EnchantDescriptions);
            await lic.LoadAsync(1);
            var with = Profile(lic.ProvideStats(1));
            if (!with.SequenceEqual(baseline)) { ok++; continue; }
            var def = Data.Items.FirstOrDefault(i => i.Id == itemId);
            var cmds = def == null ? new() : svc.GetPassiveCommands(def.UseEnchantId).ToList();
            // Blocs tous déclenchés (col40) : proc de combat, pas un passif.
            var procOnly = cmds.Count == 0 && def != null &&
                svc.GetUseCommands(def.UseEnchantId).Count > 0;
            if (procOnly || cmds.Count > 0 && cmds.All(c => NoProfileField.Contains(c.CommandId))) { documented++; continue; }
            silent.Add($"{itemId}->{def?.UseEnchantId}:" + string.Join(",", cmds.Select(c => c.CommandId)));
        }
        output.WriteLine($"{ok}/{certItems.Count} certificats modifient le profil, " +
            $"{documented} sans champ de profil (documenté), {silent.Count} muets");
        Assert.True(silent.Count == 0, "certificats sans effet sur le profil : " + string.Join(" ", silent));
    }
}
