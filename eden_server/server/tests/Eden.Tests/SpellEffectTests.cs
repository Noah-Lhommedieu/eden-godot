using Eden.Core.Game.Services;
using Eden.Zone;
using Eden.Zone.Combat.SpellEffects;
using Xunit;

namespace Eden.Tests;

/// <summary>Effets de sorts : sélection des dissipations
/// (<c>CEC_EnchantKill*::Execute</c> @0x0852ea60/@0x0852eed0/
/// @0x0852dc90), AttrHateReset (@0x08507570) et exécuteur générique.
/// </summary>
public sealed class SpellEffectTests
{
    private static ZoneLiveWorld.LiveMob Mob() => new()
    {
        Handle = 1, MobId = 1, NodeId = 1, Hp = 10, MaxHp = 10,
        X = 10, Y = 0,
    };

    private sealed class FakeHost : ISpellEffectHost
    {
        public (float x, float z)? Caster;
        public (float x, float y)? MobPos;
        public List<int> Removed = new();
        public List<(int eid, int rest)> MobApplied = new();
        public List<(int eid, int rest)> CasterApplied = new();
        public List<(int eid, int rest)> PlayersApplied = new();
        public List<ZoneLiveWorld.LiveMob> Near = new();
        public Task MoveCasterAsync(float x, float z)
        { Caster = (x, z); return Task.CompletedTask; }
        public Task MoveMobAsync(ZoneLiveWorld.LiveMob m, float x, float y)
        { MobPos = (x, y); return Task.CompletedTask; }
        public Task RemoveMobEnchantAsync(ZoneLiveWorld.LiveMob m, int eid)
        { Removed.Add(eid); return Task.CompletedTask; }
        public Task ApplyEnchantToMobAsync(ZoneLiveWorld.LiveMob m,
            int eid, int rest, int level)
        { MobApplied.Add((eid, rest)); return Task.CompletedTask; }
        public Task ApplyEnchantToCasterAsync(int eid, int rest,
            int level)
        { CasterApplied.Add((eid, rest)); return Task.CompletedTask; }
        public List<bool> PlayersFoes = new();
        public Task<int> ApplyEnchantToPlayersNearAsync(float x,
            float z, float radius, int eid, int rest, int level,
            bool foes = false)
        { PlayersApplied.Add((eid, rest)); PlayersFoes.Add(foes);
            return Task.FromResult(1); }
        public IEnumerable<ZoneLiveWorld.LiveMob> MobsNear(float x,
            float z, float radius) => Near;
        public List<(int hp, int mp)> Healed = new();
        public Task HealCasterAsync(int hp, int mp)
        { Healed.Add((hp, mp)); return Task.CompletedTask; }
        public List<int> Effects = new();
        public Task PlayEffectAsync(float a0, float a1, float a2,
            int a3, float a4, uint handle = 0)
        { Effects.Add((int)a0); return Task.CompletedTask; }
        public List<(int mobHandle, int sid)> CounterCasts = new();
        public Task MobCounterCastAsync(ZoneLiveWorld.LiveMob m,
            int sid)
        { CounterCasts.Add(((int)m.Handle, sid));
            return Task.CompletedTask; }
    }

    [Fact]
    public void RulesFor_1821_DebuffTypeFromA2A3()
    {
        // Données réelles 52029 : `1821 | | |100|1` → type 2, 100 %, 1.
        var r = EnchantDispel.RulesFor(1821, new double[] { 0, 0, 100, 1, 0, 0 });
        var rule = Assert.Single(r);
        Assert.Equal(new DispelRule(100, 1, 2, false, false, 0, 0), rule);
    }

    [Fact]
    public void RulesFor_1824_Force_TypeDebuff()
    {
        var r = EnchantDispel.RulesFor(1824, new double[] { 100, 1, 0, 0, 0, 0 });
        Assert.Equal(new DispelRule(100, 1, 2, true, false, 0, 0),
            Assert.Single(r));
    }

    [Fact]
    public void Pick_OnlyDebuffsDispellableVisible()
    {
        var rule = new DispelRule(100, 5, 2, false, false, 0, 0);
        var pool = new[]
        {
            new DispelCandidate(100, true, false, false),   // ok
            new DispelCandidate(101, false, false, false),  // buff
            new DispelCandidate(102, true, true, false),    // indissipable
            new DispelCandidate(103, true, false, true),    // sans icône
            new DispelCandidate(55500, true, false, false), // id protégé
        };
        var got = EnchantDispel.Pick(rule, pool, () => 0, n => 0);
        Assert.Equal(new[] { 100 }, got);
    }

    [Fact]
    public void Pick_Force_IgnoresUndispellableBit()
    {
        var rule = new DispelRule(100, 5, 2, true, false, 0, 0);
        var pool = new[] { new DispelCandidate(102, true, true, false) };
        Assert.Equal(new[] { 102 },
            EnchantDispel.Pick(rule, pool, () => 0, n => 0));
    }

    [Fact]
    public void Pick_ChanceRollFailure_Nothing()
    {
        var rule = new DispelRule(30, 1, 2, false, false, 0, 0);
        var pool = new[] { new DispelCandidate(100, true, false, false) };
        Assert.Empty(EnchantDispel.Pick(rule, pool, () => 30, n => 0));
    }

    [Fact]
    public void Pick_CountTruncates()
    {
        var rule = new DispelRule(100, 2, 2, false, false, 0, 0);
        var pool = Enumerable.Range(100, 5)
            .Select(i => new DispelCandidate(i, true, false, false));
        Assert.Equal(2, EnchantDispel.Pick(rule, pool, () => 0, n => 0).Count);
    }

    [Theory]
    [InlineData(55000, false)]
    [InlineData(55001, true)]
    [InlineData(56000, true)]
    [InlineData(56001, false)]
    [InlineData(60001, true)]
    [InlineData(61000, true)]
    [InlineData(61001, false)]
    public void ProtectedIdRanges(int id, bool prot)
        => Assert.Equal(prot, EnchantDispel.IsProtectedId(id));

    [Fact]
    public void KillList_KeepsNonZeroIds()
        => Assert.Equal(new[] { 61056, 7 },
            EnchantDispel.KillList(new double[] { 61056, 0, 7, 0, 0, 0 }));

    [Fact]
    public async Task HateReset_SetsAllTo1_AndForcesCaster()
    {
        var mob = Mob();
        mob.Hate[5] = 900;
        mob.Hate[6] = 40;
        var ex = SpellEffectExecutor.CreateDefault();
        var ctx = new SpellEffectContext(77, 0, 0, mob, 1);
        Assert.True(await ex.TryExecuteAsync(new FakeHost(), ctx,
            new EnchantService.UseCommand(1120, new double[6])));
        Assert.Equal(1, mob.Hate[5]);
        Assert.Equal(1, mob.Hate[6]);
        Assert.Equal(1, mob.Hate[77]);
        Assert.Equal(77, mob.TargetCharId);
    }

    [Fact]
    public async Task HateReset_DeadMob_NoEffect()
    {
        var mob = Mob();
        mob.Alive = false;
        mob.Hate[5] = 900;
        await new AttrHateResetHandler().ExecuteAsync(new FakeHost(),
            new SpellEffectContext(77, 0, 0, mob, 1),
            new EnchantService.UseCommand(1120, new double[6]));
        Assert.Equal(900, mob.Hate[5]);
    }

    [Fact]
    public async Task DistanceGoto_Zero_ChargesCasterToContact()
    {
        var host = new FakeHost();
        var ex = SpellEffectExecutor.CreateDefault();
        await ex.TryExecuteAsync(host, new SpellEffectContext(1, 0, 0, Mob(), 1),
            new EnchantService.UseCommand(1802, new double[6]));
        Assert.Equal(8.5f, host.Caster!.Value.x, 3);
    }

    [Fact]
    public async Task DistanceGoto_Positive_KnocksBackMob()
    {
        var host = new FakeHost();
        var mob = Mob();
        await new DistanceGotoHandler().ExecuteAsync(host,
            new SpellEffectContext(1, 0, 0, mob, 1),
            new EnchantService.UseCommand(1809,
                new double[] { 3, 0, 0, 0, 0, 0 }));
        Assert.Equal(13f, mob.X, 3);
        Assert.NotNull(host.MobPos);
    }

    private static EnchantService RealEnchants()
        => EnchantService.LoadFromDataDir(
            TestPaths.RepoRoot + "/server_us/Data/db_decoded");

    /// <summary>Dispel mob — enchant réel 52029 « dissipation »
    /// (`1821 |||100|1` = retire 1 debuff à 100 %) : le handler lit le
    /// registre du mob, filtre buff/debuff via la vraie table et retire
    /// l'entrée.</summary>
    [Fact]
    public async Task MobDispel_1821_RemovesDebuffFromRegistry()
    {
        var enchants = RealEnchants();
        var mob = Mob();
        // 52026 = debuff réel (c7=2) — dissipable ; 52105 = buff
        // réel (c7=1) — épargné par le filtre type 2.
        mob.Enchants[52026] = new ZoneLiveWorld.LiveMob.MobEnchant
            { EffectId = 52026, RestSeconds = 30 };
        mob.Enchants[52105] = new ZoneLiveWorld.LiveMob.MobEnchant
            { EffectId = 52105, RestSeconds = 30 };
        var host = new FakeHost();
        await new MobEnchantDispelHandler(enchants).ExecuteAsync(host,
            new SpellEffectContext(1, 0, 0, mob, 52029),
            new EnchantService.UseCommand(1821,
                new double[] { 0, 0, 100, 1, 0, 0 }));
        Assert.Equal(new[] { 52026 }, host.Removed);
    }

    /// <summary>EnchantCancel 1822 — données réelles 49854
    /// (`163|163|2|165|165|2` = types 163 et 165) : le mob ne perd que
    /// les enchants des types ciblés (Enlarge 49804 type 163 ;
    /// un debuff type 0 est épargné).</summary>
    [Fact]
    public async Task MobDispel_1822_RemovesByTypeRange()
    {
        var enchants = RealEnchants();
        var mob = Mob();
        mob.Enchants[49804] = new ZoneLiveWorld.LiveMob.MobEnchant
            { EffectId = 49804, RestSeconds = 30 };  // type 163
        mob.Enchants[52026] = new ZoneLiveWorld.LiveMob.MobEnchant
            { EffectId = 52026, RestSeconds = 30 };  // type 0
        var host = new FakeHost();
        await new MobEnchantDispelHandler(enchants).ExecuteAsync(host,
            new SpellEffectContext(1, 0, 0, mob, 49854),
            new EnchantService.UseCommand(1822,
                new double[] { 163, 163, 2, 165, 165, 2 }));
        Assert.Equal(new[] { 49804 }, host.Removed);
    }

    /// <summary>EnchantKills 1823 — les ids listés sont retirés même
    /// sans icône (comportement natif du KillList).</summary>
    [Fact]
    public async Task MobDispel_1823_RemovesListedIds()
    {
        var mob = Mob();
        mob.Enchants[52026] = new ZoneLiveWorld.LiveMob.MobEnchant
            { EffectId = 52026, Hidden = true };
        var host = new FakeHost();
        await new MobEnchantDispelHandler(RealEnchants()).ExecuteAsync(
            host, new SpellEffectContext(1, 0, 0, mob, 1),
            new EnchantService.UseCommand(1823,
                new double[] { 52026, 0, 0, 0, 0, 0 }));
        Assert.Equal(new[] { 52026 }, host.Removed);
    }

    /// <summary>Effect2Target 7003 — mode 2 = cible courante : le
    /// sous-effet se pose sur le mob (donnée réelle `2|?|r|eid|dur`).
    /// </summary>
    [Fact]
    public async Task Effect2Target_Mode2_AppliesToMob()
    {
        var host = new FakeHost();
        var mob = Mob();
        await new Effect2TargetHandler().ExecuteAsync(host,
            new SpellEffectContext(1, 0, 0, mob, 11164),
            new EnchantService.UseCommand(7003,
                new double[] { 2, 1, 30, 11163, 300, 0 }));
        // arg4 = durée en dixièmes de seconde (EnchantRestSeconds).
        Assert.Equal(new[] { (11163, 30) }, host.MobApplied);
        Assert.Empty(host.CasterApplied);
    }

    /// <summary>Mode 1 = lanceur ; mode 4 ennemi = cercle de mobs
    /// autour de la cible (rayon arg2 × 0,5 — Init @0x08526300).</summary>
    [Fact]
    public async Task Effect2Target_Modes1And4()
    {
        var host = new FakeHost();
        var mob = Mob();
        var ctx = new SpellEffectContext(1, 0, 0, mob, 1);
        // mode 1 → caster (10016 réel : `1|1|1|10007|-2`)
        await new Effect2TargetHandler().ExecuteAsync(host, ctx,
            new EnchantService.UseCommand(7003,
                new double[] { 1, 1, 1, 10007, -2, 0 }));
        Assert.Equal(new[] { (10007, -2) }, host.CasterApplied);
        // mode 4 arg1=1 → mobs autour de la cible (10064 réel :
        // `4|1|300|10063|50`)
        var m2 = Mob(); m2.Handle = 2;
        host.Near.Add(m2);
        await new Effect2TargetHandler().ExecuteAsync(host, ctx,
            new EnchantService.UseCommand(7003,
                new double[] { 4, 1, 300, 10063, 50, 0 }));
        Assert.Equal(new[] { (10063, 5) }, host.MobApplied);
        // mode 4 arg1=1 (ennemi) → touche AUSSI les joueurs hostiles
        // au lanceur (IsMyEnemy natif — contexte PvP partagé).
        Assert.Equal(new[] { true }, host.PlayersFoes);
        // mode 5 arg1=0 → joueurs autour du lanceur (côté ami)
        await new Effect2TargetHandler().ExecuteAsync(host, ctx,
            new EnchantService.UseCommand(7003,
                new double[] { 5, 0, 10, 11175, 300, 0 }));
        Assert.Equal(new[] { (10063, 5), (11175, 30) },
            host.PlayersApplied);
        Assert.Equal(new[] { true, false }, host.PlayersFoes);
    }

    /// <summary>AttrHateHot 2121 — `mob.hate[lanceur] += arg0`
    /// (CEC_AttrHateHot @0x08507aa0) : apaisement -450 puis provoc
    /// +100 cumulent sur la même entrée.</summary>
    [Fact]
    public async Task AttrHateHot_AddsCasterHateDelta()
    {
        var host = new FakeHost();
        var mob = Mob();
        var ctx = new SpellEffectContext(77, 0, 0, mob, 52099);
        var h = new AttrHateHotHandler();
        await h.ExecuteAsync(host, ctx,
            new EnchantService.UseCommand(2121,
                new double[] { -450, 0, 0, 0, 0, 0 }));
        Assert.Equal(-450, mob.Hate[77]);
        await h.ExecuteAsync(host, ctx,
            new EnchantService.UseCommand(2119,
                new double[] { 100, 0, 0, 0, 0, 0 }));
        Assert.Equal(-350, mob.Hate[77]);
    }

    /// <summary>HealPlus 2047 voie mob — type 4/5 = soin flat des
    /// vitals du LANCEUR ; types 0-3 = no-op (taux spellref).</summary>
    [Fact]
    public async Task HealPlus_Type4_HealsCasterHp()
    {
        var host = new FakeHost();
        var ctx = new SpellEffectContext(1, 0, 0, Mob(), 1);
        await new HealPlusHandler().ExecuteAsync(host, ctx,
            new EnchantService.UseCommand(2047,
                new double[] { 4, 500, 0, 0, 0, 0 }));
        Assert.Equal(new[] { (500, 0) }, host.Healed);
        // type 1 (MP taux) = no-op — toute la donnée réelle est `[1]`.
        await new HealPlusHandler().ExecuteAsync(host, ctx,
            new EnchantService.UseCommand(2047,
                new double[] { 1, 0, 0, 0, 0, 0 }));
        Assert.Single(host.Healed);
    }

    /// <summary>SpellAttack 1999 — la cible mob recaste arg0 vers
    /// l'attaquant.</summary>
    [Fact]
    public async Task SpellAttack_MobCounterCasts()
    {
        var host = new FakeHost();
        var mob = Mob(); mob.Handle = 0x42;
        await new SpellAttackHandler().ExecuteAsync(host,
            new SpellEffectContext(1, 0, 0, mob, 1),
            new EnchantService.UseCommand(1999,
                new double[] { 58640, 0, 0, 0, 0, 0 }));
        Assert.Equal(new[] { (0x42, 58640) }, host.CounterCasts);
    }

    /// <summary>DistanceSummon 1807 — le mob est téléporté au contact
    /// du lanceur quand la distance dépasse la marge.</summary>
    [Fact]
    public async Task DistanceSummon_PullsMobToCasterContact()
    {
        var host = new FakeHost();
        var mob = Mob(); mob.X = 20f; mob.Y = 0f;
        await new DistanceSummonHandler().ExecuteAsync(host,
            new SpellEffectContext(1, 0, 0, mob, 1),
            new EnchantService.UseCommand(1807, new double[6]));
        Assert.Equal(1.5f, mob.X, 3);          // ramené au contact
        Assert.Equal(0f, mob.Y, 3);
        Assert.NotNull(host.MobPos);           // move émis
        // Déjà au contact → aucune téléportation.
        var m2 = Mob(); m2.X = 1f; m2.Y = 0f;
        await new DistanceSummonHandler().ExecuteAsync(host,
            new SpellEffectContext(1, 0, 0, m2, 1),
            new EnchantService.UseCommand(1807, new double[6]));
        Assert.Equal(1f, m2.X, 3);
    }

    /// <summary>PlayEffect 1913 + tags registre/formule — no-op sans
    /// log.</summary>
    [Fact]
    public async Task PlayEffect_AndRegistryAttrs()
    {
        var host = new FakeHost();
        var ex = SpellEffectExecutor.CreateDefault();
        var ctx = new SpellEffectContext(1, 0, 0, Mob(), 1);
        await ex.TryExecuteAsync(host, ctx,
            new EnchantService.UseCommand(1913,
                new double[] { 99112, 4, 413, 0, 0, 0 }));
        Assert.Equal(new[] { 99112 }, host.Effects);
        foreach (var id in new[] { 2055, 3001, 3002, 1997, 2033, 2034 })
            Assert.True(await ex.TryExecuteAsync(host, ctx,
                new EnchantService.UseCommand(id, new double[6])));
    }

    /// <summary>FindByType — même type (col38) : grade supérieur ou
    /// niveau supérieur à grade égal refuse le nouvel enchant ; sinon
    /// l'existant est remplacé (CEnchantMgr::Add @0x08569a50).</summary>
    [Fact]
    public void TypeAddAllowed_GradeAndLevelRule()
    {
        var en = RealEnchants();
        // type=1 : 59319=grade1, 40631=grade2, 40673=grade3, 49482=g2.
        Assert.True(en.TypeAddAllowed(40631, 1,
            new Dictionary<int, int> { [59319] = 1 }, out var rep));
        Assert.Equal(new[] { 59319 }, rep);          // g2 > g1 → remplace
        Assert.False(en.TypeAddAllowed(59319, 1,
            new Dictionary<int, int> { [40673] = 1 }, out _)); // g1 < g3
        Assert.False(en.TypeAddAllowed(40631, 1,
            new Dictionary<int, int> { [49482] = 3 }, out _)); // g2=, lvl1<3
        Assert.True(en.TypeAddAllowed(40631, 4,
            new Dictionary<int, int> { [49482] = 3 }, out _)); // g2=, lvl4>3
        // type 0 (col38 vide) → jamais de conflit.
        Assert.True(en.TypeAddAllowed(58447, 1,
            new Dictionary<int, int> { [59319] = 9 }, out _));
        // type différent → aucun remplacement.
        Assert.True(en.TypeAddAllowed(58454, 1,
            new Dictionary<int, int> { [59319] = 1 }, out var rep2));
        Assert.Empty(rep2);
    }

    /// <summary>Checks 3101-3103/2137 — Execute natif = `return 1` /
    /// booléen, aucune mutation : l'exécuteur les reconnaît sans rien
    /// faire.</summary>
    [Fact]
    public async Task EnchantChecks_AreHandledNoOps()
    {
        var host = new FakeHost();
        var ex = SpellEffectExecutor.CreateDefault();
        var ctx = new SpellEffectContext(1, 0, 0, Mob(), 1);
        foreach (var id in new[] { 3101, 3102, 3103, 2137 })
            Assert.True(await ex.TryExecuteAsync(host, ctx,
                new EnchantService.UseCommand(id, new double[6])));
        Assert.Empty(host.Removed);
        Assert.Empty(host.MobApplied);
        Assert.Empty(host.CasterApplied);
    }

    [Fact]
    public async Task Executor_UnknownCommand_ReturnsFalse()
        => Assert.False(await SpellEffectExecutor.CreateDefault().TryExecuteAsync(
            new FakeHost(), new SpellEffectContext(1, 0, 0, Mob(), 1),
            new EnchantService.UseCommand(9999, new double[6])));
}
