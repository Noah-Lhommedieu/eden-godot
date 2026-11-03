using Eden.Core.Data.Parsers;
using Eden.Core.Game.WorldBoss;
using Eden.Core.Networking;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>Plan futur-feature/12 — world boss, 70-S, mutants :
/// parsing fidèle des colonnes d'annonce (GetAllRebornMonsterInfo
/// @0x083688a0), registre, statut de respawn, planificateur de
/// mutants, annonces 316 et courriers.</summary>
public class WorldBossPlan12Tests
{
    private static readonly string SceneDir = FindSceneDir();

    private static string FindSceneDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "server_us/Data/scene");
            if (Directory.Exists(c)) return c;
            var c2 = Path.Combine(dir.FullName, "../server_us/Data/scene");
            if (Directory.Exists(c2)) return Path.GetFullPath(c2);
            dir = dir.Parent;
        }
        return TestPaths.RepoRoot + "/server_us/Data/scene";
    }

    private static SceneParser.Scene Scene(string code) =>
        SceneParser.Parse(Path.Combine(SceneDir, code + ".ini"));

    // ------------------------------------------------ parsing scène

    [Fact]
    public void RebornTail_70S_Line_ReadsFlagAndTextIndex()
    {
        // S628 id 74 : …,M001,nom,0,0,0,1,6044,0,0,0,0, (25 champs)
        var f = "74,60770,69,1,28800,137.497,116.352,4.71239,311,0,0,0,0,M001,n,0,0,0,1,6044,0,0,0,0,"
            .Split(',');
        Assert.Equal(25, f.Length);
        var t = SceneParser.ParseRebornTail(f);
        Assert.Equal(1, t.Flag);
        Assert.Equal(6044, t.TextId);
        Assert.Empty(t.Extra);
    }

    [Fact]
    public void RebornTail_WorldBoss_Line_HasNoAnnounce()
    {
        var f = "23,58001,70,1,7200,381.546,105.001,1.5708,0,0,0,0,1,M025,n,0,0,0,0,0,0,0,0,0,"
            .Split(',');
        var t = SceneParser.ParseRebornTail(f);
        Assert.Equal(0, t.Flag);
        Assert.Equal(0, t.TextId);
    }

    [Fact]
    public void RebornTail_ShortRows_AreSafe()
    {
        // < 14 champs : aucune queue ; 14-17 : pas de couple d'annonce.
        Assert.Equal(0, SceneParser.ParseRebornTail(
            "1,2,3,4,5,6,7,8,9,10,11,12,13".Split(',')).TextId);
        Assert.Equal(0, SceneParser.ParseRebornTail(
            "1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17".Split(',')).TextId);
    }

    [Fact]
    public void RebornTail_ExtraShorts_OnlyNonZero()
    {
        var f = "1,2,3,4,5,6,7,8,9,10,11,12,13,M,n,0,0,0,0,0,7,0,9,0,".Split(',');
        var t = SceneParser.ParseRebornTail(f);
        Assert.Equal(new short[] { 7, 9 }, t.Extra);
    }

    [Fact]
    public void Scene_S628_EnergyCollectors_AnnounceTextAndDormant()
    {
        var sc = Scene("S628");
        var rows = sc.Monsters
            .Where(m => m.MonsterId is >= 60770 and <= 60779).ToList();
        Assert.Equal(10, rows.Count);
        Assert.All(rows, m =>
        {
            Assert.False(m.InitiallyActive);      // dormante
            Assert.Equal(1, m.AnnounceFlag);
            Assert.Equal(28800, m.RespawnSeconds);
        });
        // 6044..6053 une fois chacun.
        Assert.Equal(Enumerable.Range(6044, 10).ToList(),
            rows.Select(m => m.AnnounceTextId).OrderBy(i => i).ToList());
    }

    [Fact]
    public void Scene_FixedBosses_MatchCatalogRespawnAndNoAnnounce()
    {
        foreach (var b in WorldBossCatalog.FixedBosses)
        {
            var sc = Scene($"S{b.NodeId:D3}");
            var rows = sc.Monsters.Where(m => m.MonsterId == b.MobId).ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, m =>
            {
                Assert.Equal(b.RespawnSeconds, m.RespawnSeconds);
                Assert.True(m.InitiallyActive);   // né au boot
                Assert.Equal(0, m.AnnounceTextId); // pas d'annonce native
            });
        }
    }

    // ------------------------------------------------ registre

    [Fact]
    public void Catalog_Has26FixedBosses_And_Excludes58015()
    {
        Assert.Equal(26, WorldBossCatalog.FixedBosses.Count);
        Assert.False(WorldBossCatalog.IsWorldBoss(58015));
        Assert.False(WorldBossCatalog.IsWorldBoss(58021));
        Assert.True(WorldBossCatalog.IsWorldBoss(58001));
        Assert.True(WorldBossCatalog.IsWorldBoss(58030));
        Assert.Equal(3600, WorldBossCatalog.Find(58018)!.RespawnSeconds);
        Assert.Equal(25, WorldBossCatalog.Find(58018)!.NodeId);
    }

    [Theory]
    [InlineData(58001, BossKind.WorldBoss)]
    [InlineData(58015, BossKind.None)]
    [InlineData(60770, BossKind.EnergyCollector)]
    [InlineData(60779, BossKind.EnergyCollector)]
    [InlineData(60780, BossKind.None)]
    [InlineData(60761, BossKind.Mutant)]
    [InlineData(60766, BossKind.Mutant)]
    [InlineData(60767, BossKind.None)]
    [InlineData(1001, BossKind.None)]
    public void KindOf(int id, BossKind kind) =>
        Assert.Equal(kind, WorldBossCatalog.KindOf(id));

    [Fact]
    public void Mutants_SpotsAreDistinctPerRegion_AndNodesMatchBossRegions()
    {
        // Régions = nodes des boss Sabaster/Kison/Silin/Ryloth/Gilz/Kekabu.
        foreach (var n in WorldBossCatalog.MutantRegionNodes)
            Assert.Contains(WorldBossCatalog.FixedBosses,
                b => b.NodeId == n);
        for (var r = 0; r < 6; r++)
        {
            var spots = Enumerable.Range(0, 6)
                .Select(m => WorldBossCatalog.MutantSpot(m, r)).ToList();
            Assert.Equal(6, spots.Distinct().Count());
        }
        // Valeur du guide : Huroc en Plaines dorées = (306, 61).
        Assert.Equal((306f, 61f), WorldBossCatalog.MutantSpot(5, 4));
    }

    [Theory]
    [InlineData(60761, 1)]
    [InlineData(60763, 2)]
    [InlineData(60766, 3)]
    [InlineData(58001, 0)]
    public void MutantShardAmount_ByLevel(int id, int expected) =>
        Assert.Equal(expected, WorldBossCatalog.MutantShardAmount(id));

    // ------------------------------------------------ statut

    [Fact]
    public void BuildStatus_AliveDeadUnloaded()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var rows = WorldBossCatalog.BuildStatus(new (int, bool, DateTime?)[]
        {
            (58001, true, null),
            (58002, false, t),
            // Luisaka ×3 : une instance vivante suffit.
            (58018, false, t.AddMinutes(5)),
            (58018, true, null),
            // Zorna ×3 : échéance la plus proche.
            (58019, false, t.AddMinutes(9)),
            (58019, false, t.AddMinutes(2)),
        });
        Assert.Equal(26, rows.Count);
        Assert.Equal(WorldBossCatalog.BossState.Alive,
            rows.Single(r => r.MobId == 58001).State);
        var dead = rows.Single(r => r.MobId == 58002);
        Assert.Equal(WorldBossCatalog.BossState.Dead, dead.State);
        Assert.Equal(t, dead.RespawnAt);
        Assert.Equal(WorldBossCatalog.BossState.Alive,
            rows.Single(r => r.MobId == 58018).State);
        Assert.Equal(t.AddMinutes(2), rows.Single(r => r.MobId == 58019).RespawnAt);
        Assert.Equal(WorldBossCatalog.BossState.Unloaded,
            rows.Single(r => r.MobId == 58030).State);
    }

    [Fact]
    public void StatusLines_ShowCountdown()
    {
        var now = DateTime.UtcNow;
        var lines = ZoneEntryFlow.WorldBossStatusLines(new[]
        {
            new WorldBossCatalog.StatusRow(58001, 2,
                WorldBossCatalog.BossState.Dead, now.AddSeconds(90)),
            new WorldBossCatalog.StatusRow(58002, 3,
                WorldBossCatalog.BossState.Unloaded, null),
        }, now).ToList();
        Assert.Contains("retour dans 90 s", lines[0]);
        Assert.Contains("non chargé", lines[1]);
    }

    // ------------------------------------------------ monde vivant

    [Fact]
    public void Live_WorldBossStatus_TracksDeathAndRespawn()
    {
        var live = new ZoneLiveWorld();
        var boss = live.SpawnHostileMob(2, 0, 58001, 0, 1, 1, 0, 0);
        // SpawnHostileMob pose un OwnerCharId (cible) → exclu du statut ;
        // on neutralise comme un spawn de scène.
        boss.OwnerCharId = 0;
        Assert.True(boss.IsWorldBoss);
        Assert.Equal(WorldBossCatalog.BossState.Alive,
            live.WorldBossStatus().Single(r => r.MobId == 58001).State);

        boss.MarkDead();
        boss.RespawnAt = DateTime.UtcNow.AddSeconds(7200);
        var dead = live.WorldBossStatus().Single(r => r.MobId == 58001);
        Assert.Equal(WorldBossCatalog.BossState.Dead, dead.State);
        Assert.Equal(boss.RespawnAt, dead.RespawnAt);

        live.Respawn(boss);
        Assert.Null(boss.RespawnAt);
        Assert.Equal(WorldBossCatalog.BossState.Alive,
            live.WorldBossStatus().Single(r => r.MobId == 58001).State);
    }

    [Fact]
    public void Live_SpawnGroup_CarriesAnnounceTextId()
    {
        var sc = Scene("S628");
        var spawn = sc.Monsters.First(m => m.MonsterId == 60770);
        var live = new ZoneLiveWorld();
        var ents = live.SpawnGroup(628, 0, spawn);
        var mob = live.GetMob(ents.Single().Handle)!;
        Assert.Equal(6044, mob.AnnounceTextId);
        Assert.Equal(BossKind.EnergyCollector, mob.Kind);
        Assert.Equal(28800, mob.RespawnSeconds);
    }

    [Fact]
    public void Live_EnsureNodePopulated_S002_TagsWorldBoss()
    {
        var live = new ZoneLiveWorld();
        live.EnsureNodePopulated(2, 0, Scene("S002"));
        var boss = live.MobsOf(2, 0).Single(m => m.MobId == 58001);
        Assert.True(boss.IsWorldBoss);
        Assert.Equal(7200, boss.RespawnSeconds);
        Assert.Equal(0, boss.AnnounceTextId);
    }

    [Fact]
    public void Live_SpawnDynamic_NoRespawn_Expires_AndRemovable()
    {
        var live = new ZoneLiveWorld
        {
            MonsterDefs = new Dictionary<int, MonsterParser.MonsterDef>
            {
                [60761] = new() { Id = 60761, HP = 5000, Level = 65 },
            },
        };
        Assert.Null(live.SpawnDynamic(6, 0, 99999, 1, 1, 0, DateTime.UtcNow));
        var exp = DateTime.UtcNow.AddMinutes(60);
        var (mob, ent) = live.SpawnDynamic(6, 0, 60761, 185, 330, 0, exp)!.Value;
        Assert.True(mob.NoRespawn);
        Assert.Equal(exp, mob.ExpireAt);
        Assert.Equal(BossKind.Mutant, mob.Kind);
        Assert.Equal(5000, mob.MaxHp);
        Assert.Equal(185f, ent.X);
        Assert.Same(mob, live.GetMob(mob.Handle));
        Assert.Single(live.MobsOf(6, 0));
        live.Remove(mob);
        Assert.Null(live.GetMob(mob.Handle));
        Assert.Empty(live.MobsOf(6, 0));
    }

    // ------------------------------------------------ planificateur

    private static MutantScheduler Sched(int seed, out DateTime t0,
        MutantScheduler.Options? o = null)
    {
        t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new MutantScheduler(o ?? MutantScheduler.Options.Default,
            new Random(seed), t0);
    }

    [Fact]
    public void Scheduler_NothingBeforeFirstDue_ThenSpawnsWithinBounds()
    {
        var s = Sched(1, out var t0);
        Assert.Empty(s.Tick(t0));
        var all = new List<MutantScheduler.Action>();
        // Dans l'attente max (90 min) au moins un mutant est dû.
        for (var m = 0; m <= 90; m += 5)
            all.AddRange(s.Tick(t0.AddMinutes(m)).Where(a =>
                a.Kind == MutantScheduler.ActionKind.Spawn));
        Assert.NotEmpty(all);
        Assert.All(all, a =>
        {
            Assert.Equal(MutantScheduler.ActionKind.Spawn, a.Kind);
            Assert.Contains(a.MutantMobId, WorldBossCatalog.MutantIds);
            Assert.Contains(a.NodeId, WorldBossCatalog.MutantRegionNodes);
            var ri = WorldBossCatalog.MutantRegionNodes.ToList()
                .IndexOf(a.NodeId);
            var mi = WorldBossCatalog.MutantIdIndex(a.MutantMobId);
            Assert.Equal(WorldBossCatalog.MutantSpot(mi, ri), (a.X, a.Y));
        });
    }

    [Fact]
    public void Scheduler_RespectsMaxConcurrent()
    {
        var o = new MutantScheduler.Options(TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2), TimeSpan.FromHours(5), 2);
        var s = Sched(7, out var t0, o);
        s.Tick(t0.AddMinutes(10));
        Assert.Equal(2, s.Active.Count);
        s.Tick(t0.AddMinutes(20));
        Assert.Equal(2, s.Active.Count);
    }

    [Fact]
    public void Scheduler_ExpiresThenReschedules()
    {
        var o = new MutantScheduler.Options(TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30), 6);
        var s = Sched(3, out var t0, o);
        var spawned = s.Tick(t0.AddMinutes(25));
        Assert.NotEmpty(spawned);
        var first = spawned[0];
        var later = s.Tick(t0.AddMinutes(25 + 31));
        Assert.Contains(later, a =>
            a.Kind == MutantScheduler.ActionKind.Expire &&
            a.MutantMobId == first.MutantMobId);
        // Replanifié dans le futur (≥ MinInterval après l'expiration).
        var mi = WorldBossCatalog.MutantIdIndex(first.MutantMobId);
        Assert.True(s.NextSpawnAt(mi) >= t0.AddMinutes(25 + 31 + 10));
    }

    [Fact]
    public void Scheduler_KilledMutantWaitsMinInterval()
    {
        var o = new MutantScheduler.Options(TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(60), TimeSpan.FromHours(2), 6);
        var s = Sched(5, out var t0, o);
        var sp = s.Tick(t0.AddMinutes(65)).First();
        var now = t0.AddMinutes(70);
        s.NotifyKilled(sp.MutantMobId, now);
        var mi = WorldBossCatalog.MutantIdIndex(sp.MutantMobId);
        Assert.DoesNotContain(s.Active, a => a.MutantMobId == sp.MutantMobId);
        Assert.InRange(s.NextSpawnAt(mi), now.AddMinutes(30), now.AddMinutes(60));
        Assert.DoesNotContain(s.Tick(now.AddMinutes(29)),
            a => a.MutantMobId == sp.MutantMobId);
    }

    [Fact]
    public void Scheduler_ForceSpawn_SpecificRegion_AndNoDouble()
    {
        var s = Sched(9, out var t0);
        var a = s.ForceSpawn(60765, 2, t0)!;
        Assert.Equal(60765, a.MutantMobId);
        Assert.Equal(9, a.NodeId);            // région 2 = Volcan Avila (S009)
        Assert.Equal((520f, 490f), (a.X, a.Y));
        Assert.Null(s.ForceSpawn(60765, 0, t0));   // déjà actif
        Assert.Null(s.ForceSpawn(1234, 0, t0));    // pas un mutant
    }

    [Fact]
    public void Scheduler_SameSeed_SameSequence()
    {
        var a = Sched(42, out var t0);
        var b = Sched(42, out _);
        for (var m = 0; m < 300; m += 7)
            Assert.Equal(
                a.Tick(t0.AddMinutes(m)).Select(x => (x.Kind, x.MutantMobId, x.NodeId)),
                b.Tick(t0.AddMinutes(m)).Select(x => (x.Kind, x.MutantMobId, x.NodeId)));
    }

    // ------------------------------------------------ annonces 316

    [Fact]
    public void Announce_Spawn_UsesNativeTextIndex()
    {
        Assert.Null(WorldBossAnnouncements.Spawn(0));
        var m = WorldBossAnnouncements.Spawn(6044)!;
        Assert.Equal(316, m.Opcode);
        Assert.Equal((short)6044, m.Field1);
        var os = new OutBinStream();
        m.Serialize(os);
        Assert.Equal(new byte[] { 0x9C, 0x17 }, os.ToArray().Take(2).ToArray());
    }

    [Fact]
    public void Announce_Kill_OnlyFixedBosses()
    {
        Assert.Null(WorldBossAnnouncements.Kill(1001, "Bob"));
        Assert.Null(WorldBossAnnouncements.Kill(58015, "Bob"));
        var m = WorldBossAnnouncements.Kill(58001, "Bob")!;
        Assert.Equal(WorldBossCatalog.ExtKillAnnounce, m.Field1);
        Assert.Equal("58001\tBob", m.Field2);
    }

    [Fact]
    public void Announce_Mutant_SpawnAndKilled()
    {
        Assert.Null(WorldBossAnnouncements.MutantSpawn(58001));
        Assert.Equal("60761",
            WorldBossAnnouncements.MutantSpawn(60761)!.Field2);
        var k = WorldBossAnnouncements.MutantKilled(60764, "Eve")!;
        Assert.Equal(WorldBossCatalog.ExtMutantKilled, k.Field1);
        Assert.Equal("60764\tEve", k.Field2);
    }

    [Fact]
    public void Announce_CastCountdown_ThresholdAndBossOnly()
    {
        Assert.Null(WorldBossAnnouncements.CastCountdown(58001, "1", 2999, 3000));
        Assert.Null(WorldBossAnnouncements.CastCountdown(1001, "1", 9000, 3000));
        var m = WorldBossAnnouncements.CastCountdown(58001, "59247", 5000, 3000)!;
        Assert.Equal((short)1647, m.Field1);
        Assert.Equal("59247", m.Field2);
    }

    [Fact]
    public void MutantRewards_OnePerHitter_AmountByLevel()
    {
        var r = WorldBossAnnouncements.MutantRewards(60765, new[] { 5, 6, 5 });
        Assert.Equal(2, r.Count);
        Assert.All(r, x =>
        {
            Assert.Equal(38992, x.ItemId);
            Assert.Equal(3, x.Amount);
        });
        Assert.Empty(WorldBossAnnouncements.MutantRewards(58001, new[] { 5 }));
        Assert.Empty(WorldBossAnnouncements.MutantRewards(60761, Array.Empty<int>()));
    }

    [Fact]
    public void MutantOptions_FromConfig_ClampsAndOrders()
    {
        var o = ZoneEntryFlow.MutantOptionsFromConfig();
        Assert.True(o.MaxInterval >= o.MinInterval);
        Assert.True(o.MaxConcurrent >= 1);
    }
}
