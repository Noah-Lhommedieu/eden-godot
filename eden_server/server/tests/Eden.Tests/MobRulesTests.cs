using Eden.Core.Data.Parsers;
using Eden.Core.Game.WorldBoss;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>Lot « règles mobs » (2026-11-04) — durée d'enchant en
/// dixièmes de seconde, portée de cast native CheckUseSpell,
/// régénération RecoverHP/MP, patrouilles/immobilité des world boss.</summary>
public class MobRulesTests
{
    // ---------------- durée de statut (S_Spell c41 en ds) ----------

    /// <summary>Bug live : un statut affiché 12 s durait ~90 s —
    /// c41 (dixièmes) était passé comme des secondes.
    /// Natif : CEnchantMgr::Add @0x856a059 → GetRestTime()/10.</summary>
    [Fact]
    public void EnchantRestSeconds_ConvertsDeciseconds()
    {
        Assert.Equal(12, ZoneEntryFlow.EnchantRestSeconds(120));
        Assert.Equal(90, ZoneEntryFlow.EnchantRestSeconds(900));
        Assert.Equal(1, ZoneEntryFlow.EnchantRestSeconds(1));
        // 0 ds → durée plancher 1 s (pas de statut instantané 0 s).
        Assert.Equal(1, ZoneEntryFlow.EnchantRestSeconds(0));
    }

    /// <summary>Sentinelles natives préservées : -9 durée propre de
    /// l'effet (icône cachée), -2 permanent, -1 pas d'enchant.</summary>
    [Theory]
    [InlineData(-9)]
    [InlineData(-2)]
    [InlineData(-1)]
    public void EnchantRestSeconds_KeepsSentinels(int ds) =>
        Assert.Equal(ds, ZoneEntryFlow.EnchantRestSeconds(ds));

    // ---------------- portée de cast du mob (CheckUseSpell) --------

    /// <summary>CAI::CheckUseSpell zone @0x084d91c0 : borne =
    /// SSpellData+0x40 (S_Spell col28 × 0,5) + rayonCaster + rayonCible
    /// + 0,5 (DAT_08894104 = -0,5f s'ajoute à la borne). Rayon mob =
    /// BoundInfo col7 (défaut 1,0), rayon joueur ≈ 1,2. col28 = 50 →
    /// 25 + 1,0 + 1,2 + 0,5 = 27,7 — plus de slack +4,5 ni repli 25 u
    /// (casts « de super loin »).</summary>
    [Fact]
    public void MobSpellCastRange_NativeScale()
    {
        var def = new SpellParser.SpellDef { Range = 50 };
        Assert.Equal(27.7f, ZoneEntryFlow.MobSpellCastRange(null, def));
        // Sort corps à corps (col28 petit) : quasi le contact —
        // 3 + 2,7 = 5,7 ≈ portée de riposte 6 u.
        var melee = new SpellParser.SpellDef { Range = 6 };
        Assert.Equal(5.7f, ZoneEntryFlow.MobSpellCastRange(null, melee));
        // Sans portée → borne = rayons + marge seuls (contact).
        Assert.Equal(2.7f, ZoneEntryFlow.MobSpellCastRange(null, null));
        Assert.Equal(2.7f,
            ZoneEntryFlow.MobSpellCastRange(null,
                new SpellParser.SpellDef { Range = 0 }));
        // Un gros modèle (BoundInfo) gagne son rayon réel.
        var big = new MonsterParser.MonsterDef { BoundRadius = 2f };
        Assert.Equal(28.7f, ZoneEntryFlow.MobSpellCastRange(big, def));
    }

    /// <summary>La borne ne dépend pas de la forme — CheckUseSpell
    /// compare la distance cible à +0x40 (col28 ×0,5) quelle que soit
    /// la classe CSpell. Un sort autocentré (Param1=0 → forme 3) avec
    /// col28 nul retombe donc sur la borne de contact.</summary>
    [Fact]
    public void MobSpellCastRange_ShapeIndifferent()
    {
        var selfNoRange = new SpellParser.SpellDef { Param1 = 0 };
        Assert.Equal(2.7f,
            ZoneEntryFlow.MobSpellCastRange(null, selfNoRange));
        var selfRanged = new SpellParser.SpellDef { Param1 = 0, Range = 20 };
        Assert.Equal(12.7f,
            ZoneEntryFlow.MobSpellCastRange(null, selfRanged));
    }

    /// <summary>Le gate refuse le cast hors borne et l'admet dedans —
    /// la branche « trop loin → poursuite » de CheckUseSpell.</summary>
    [Fact]
    public void MobSpellInCastRange_RefusesOutOfRange()
    {
        var def = new SpellParser.SpellDef { Range = 50 };  // borne 27,7
        Assert.True(ZoneEntryFlow.MobSpellInCastRange(null, def, 27.5 * 27.5));
        Assert.False(ZoneEntryFlow.MobSpellInCastRange(null, def, 28 * 28));
        // Un sort mêlée refusé à 7 u (portée riposte) — le mob doit
        // fermer au contact.
        var melee = new SpellParser.SpellDef { Range = 6 };  // borne 5,7
        Assert.False(ZoneEntryFlow.MobSpellInCastRange(null, melee, 36));
        Assert.True(ZoneEntryFlow.MobSpellInCastRange(null, melee, 5.6 * 5.6));
    }

    // ------------- premier hit avant cast (règle propriétaire) -----

    /// <summary>HasLandedMelee : nouvel engagement (BeginCombat avec
    /// nouvelle cible / réaggro) → faux → gate du tick refuse le cast ;
    /// faux tant qu'aucun outcome non-miss n'a été enregistré.</summary>
    [Fact]
    public void LiveMob_HasLandedMelee_ResetPerEngagement()
    {
        var mob = new ZoneLiveWorld.LiveMob
        { Handle = 1, MobId = 1, NodeId = 2, Alive = true };
        mob.BeginCombat(10, 0, 0);
        Assert.False(mob.HasLandedMelee);       // combat 1 : pas encore frappé
        mob.HasLandedMelee = true;              // riposte non-miss posée
        mob.CancelCombat();                     // désaggro
        Assert.False(mob.HasLandedMelee);
        mob.BeginCombat(10, 0, 0);              // réaggro même joueur
        Assert.False(mob.HasLandedMelee);       // nouveau combat → requis à nouveau
    }

    /// <summary>La mort et le respawn remettent le flag à zéro — un
    /// mob qui réapparaît ne « se souvient » pas d'un ancien hit.</summary>
    [Fact]
    public void LiveMob_HasLandedMelee_ResetOnDeathAndRespawn()
    {
        var mob = new ZoneLiveWorld.LiveMob
        { Handle = 1, MobId = 1, NodeId = 2, Alive = true };
        mob.BeginCombat(10, 0, 0);
        mob.HasLandedMelee = true;
        mob.MarkDead();
        Assert.False(mob.HasLandedMelee);
        mob.ResetCombatForRespawn();
        Assert.False(mob.HasLandedMelee);
    }

    // ---------------- régénération (RecoverHP/MP) ------------------

    /// <summary>CCharFormula::RecoverHP @0x83f5b40 — (statLevel ×
    /// 0,05 + 20) × taux. Taux Regenerate @0x8100100 : assis 1,0,
    /// debout 0,3 (HP) / 0,5 (MP), combat 0,25 (MP seul).</summary>
    [Fact]
    public void RegenTick_NativeRates()
    {
        // Niveau 100, debout HP : (100×0,05+20)×0,3 = 25×0,3 = 7,5 → 8.
        Assert.Equal(8, ZoneEntryFlow.RegenTick(100, 20, 0.3));
        // Assis : 25×1,0 = 25.
        Assert.Equal(25, ZoneEntryFlow.RegenTick(100, 20, 1.0));
        // MP debout niveau-classe 50 : (50×0,05+40)×0,5 = 42,5×0,5 ≈ 21.
        Assert.Equal(21, ZoneEntryFlow.RegenTick(50, 40, 0.5));
        // Combat MP : (…+40)×0,25 ≈ 11.
        Assert.Equal(11, ZoneEntryFlow.RegenTick(50, 40, 0.25));
        // Bas niveau debout : le floor 20 domine → (0,05+20)×0,3 ≈ 6.
        Assert.Equal(6, ZoneEntryFlow.RegenTick(1, 20, 0.3));
        // Jamais 0 : le natif borne le résultat à ≥1.
        Assert.True(ZoneEntryFlow.RegenTick(1, 20, 0.25) >= 1);
    }

    // ---------------- patrouille / world boss ----------------------

    /// <summary>LiveMob porte les champs de patrouille — la route est
    /// résolue paresseusement (PatrolResolved) depuis Scene.Patrols
    /// via PatrolId (roam mode 1 natif, CAI::Init @0x84d26f0).</summary>
    [Fact]
    public void LiveMob_PatrolFields_DefaultToNone()
    {
        var mob = new ZoneLiveWorld.LiveMob
        { Handle = 1, MobId = 58001, NodeId = 2 };
        Assert.Null(mob.Patrol);
        Assert.Equal(0, mob.PatrolIndex);
        Assert.False(mob.PatrolResolved);
        Assert.True(mob.IsWorldBoss); // 58001 = boss fixe du catalogue
    }

    /// <summary>Chaque spawn de world boss porte un PatrolId (col2 de
    /// [reborn_monsters]) qui se résout dans [mob_patrol_mode] —
    /// preuve donnée : les 26 boss suivent un chemin tracé (roam
    /// mode 1 natif). Les routes à waypoint unique rendent le boss
    /// de fait immobile (il revient toujours au même point).</summary>
    [Fact]
    public void SceneData_WorldBosses_HavePatrolRoutes()
    {
        var dir = FindSceneDir();
        if (dir == null) return; // données natives absentes du CI
        var bossSpawns = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.ini"))
        {
            var scene = SceneParser.Parse(f);
            foreach (var m in scene.Monsters)
            {
                if (!WorldBossCatalog.IsWorldBoss(m.MonsterId))
                    continue;
                bossSpawns++;
                Assert.NotEqual(0, m.PatrolId);
                Assert.True(scene.Patrols.ContainsKey(m.PatrolId),
                    $"{f}: boss {m.MonsterId} patrol {m.PatrolId} " +
                    "sans route");
            }
        }
        // Garde-fou : les 26 boss doivent apparaître (S025/S027 en
        // comptent plusieurs lignes).
        Assert.True(bossSpawns >= 26, $"{bossSpawns} spawns boss");
    }

    /// <summary>Une route de scène expose des waypoints (X,Z) —
    /// consommés séquentiellement par le wander (SetNewGoalPatrol).</summary>
    [Fact]
    public void SceneData_PatrolRoutes_HaveWaypoints()
    {
        var dir = FindSceneDir();
        if (dir == null) return;
        var s001 = Path.Combine(dir, "S001.ini");
        if (!File.Exists(s001)) return;
        var scene = SceneParser.Parse(s001);
        Assert.NotEmpty(scene.Patrols);
        Assert.All(scene.Patrols.Values,
            r => Assert.NotEmpty(r.Waypoints));
    }

    private static string? FindSceneDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "server_us/Data/scene");
            if (Directory.Exists(c)) return c;
            dir = dir.Parent;
        }
        return null;
    }
}
