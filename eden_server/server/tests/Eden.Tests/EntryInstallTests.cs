using Eden.Core.Game.Classes;
using Eden.Core.Game.Stats;
using Eden.Core.Game.Work;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>Phase 2 — install atomique du chargement d'entrée
/// hors-boucle : <see cref="ZoneEntryFlow.AbsorbEntryFields"/> est la
/// SEULE copie scratch → état live ; le test épingle les champs
/// transportés (identité, vitals, arme, works, recommandées…) et le
/// detachment des collections copiées par contenu — une mutation du
/// scratch après install ne doit pas fuiter dans l'état live.</summary>
public class EntryInstallTests
{
    private static ZoneEntryFlow.EntryState Scratch() => new()
    {
        CharId = 42, CharName = "héro",
        NodeId = 12, PosX = 100f, PosZ = -50f, Facing = 1.5f,
        RaceId = 2, GenderId = 1, ClassId = 5, Level = 30,
        ClassLevel = 12, ActiveTitle = 7, Experience = 123456,
        Gold = 777, Privilege = 2, AccountId = 9,
        InstanceGroup = -42,
        PlayerHp = 111, PlayerMp = 55, PlayerMaxHp = 200,
        PlayerMaxMp = 90, WeaponItemId = 1234, WeaponKind = 0x11,
        WeaponOffKind = 0x22, AttackRange = 6.5f,
        WeaponResolved = true, ActCounter = 17,
        ClosetIndex = 3, HelmetColor = 0xAA, ClothesColor = 0xBB,
        CloakColor = 0xCC, VisitedFamilyInstanceId = 88,
        CrossZoneNode = 4, CrossZoneX = 1f, CrossZoneY = 2f,
        ReviveAreaId = 9, LastNormalAreaId = 8, LifeSkill = 4,
        RecommendTime = 4242, DailyDayIndex = 6,
        DailyLoginTime = 111, DailyCooldown = 222,
        DailyAwardDays = 0xF0, DailyAwardRecords = 0x0F,
        DailyLoginStatus = 1, DailySessionDelta = 3,
        DailySessionFlag = true, DailyLoaded = true,
        DailyEnabled = true, WorkOpStarted = true,
        DestinyVit = 66, AchievementTotalPoint = 900,
        CollectionPoints = 33, Vis4ad = 4,
        LifelongGold = 55, LifelongDay = 20,
    };

    [Fact]
    public void Absorb_CopiesAllScalarFields()
    {
        var sc = Scratch();
        var st = new ZoneEntryFlow.EntryState();
        ZoneEntryFlow.AbsorbEntryFields(st, sc);
        Assert.Equal(42u, st.CharId);
        Assert.Equal("héro", st.CharName);
        Assert.Equal(12, st.NodeId);
        Assert.Equal(100f, st.PosX);
        Assert.Equal(-50f, st.PosZ);
        Assert.Equal(2, st.RaceId);
        Assert.Equal(5, st.ClassId);
        Assert.Equal(30, st.Level);
        Assert.Equal(12, st.ClassLevel);
        Assert.Equal(7, st.ActiveTitle);
        Assert.Equal(123456L, st.Experience);
        Assert.Equal(777, st.Gold);
        Assert.Equal(9, st.AccountId);
        Assert.Equal(-42, st.InstanceGroup);
        Assert.Equal(111, st.PlayerHp);
        Assert.Equal(55, st.PlayerMp);
        Assert.Equal(200, st.PlayerMaxHp);
        Assert.Equal(90, st.PlayerMaxMp);
        Assert.Equal(1234, st.WeaponItemId);
        Assert.Equal(0x11, st.WeaponKind);
        Assert.Equal(0x22, st.WeaponOffKind);
        Assert.Equal(6.5f, st.AttackRange);
        Assert.True(st.WeaponResolved);
        // Le serial d'action consommé par les 299 hors-boucle doit
        // survivre à l'install — le prochain NextAction live ne
        // réutilise pas un numéro déjà émis.
        Assert.Equal(17, st.ActCounter);
        Assert.Equal((byte)3, st.ClosetIndex);
        Assert.Equal(0xAAul, st.HelmetColor);
        Assert.Equal(88, st.VisitedFamilyInstanceId);
        Assert.Equal(4, st.CrossZoneNode);
        Assert.Equal(9, st.ReviveAreaId);
        Assert.Equal(4, st.LifeSkill);
        Assert.True(st.DailyLoaded);
        Assert.True(st.DailyEnabled);
        Assert.True(st.DailySessionFlag);
        Assert.Equal(0xF0ul, st.DailyAwardDays);
        Assert.True(st.WorkOpStarted);
        Assert.Equal(66, st.DestinyVit);
        Assert.Equal(900L, st.AchievementTotalPoint);
        Assert.Equal(33, st.CollectionPoints);
        Assert.Equal(4, st.Vis4ad);
        Assert.Equal(55, st.LifelongGold);
        Assert.Equal(20, st.LifelongDay);
        Assert.Equal(4242, st.RecommendTime);
    }

    [Fact]
    public void Absorb_CopiesCollectionsByContent_NotAlias()
    {
        var sc = Scratch();
        sc.LifeSkillLearned[11] = (5, 9);
        sc.FortuneCounters[2] = 7;
        sc.RecommendProgress[101] = 55u;
        sc.RecommendFinished[101] = 1;
        sc.RecommendTracing.Add(101);
        sc.CollectedItemIds.Add(4444);
        sc.CollectionGroupPoints[1000] = 12;
        sc.CustomClassSlots.Add(new CustomClassSlot());
        sc.Lifelong[3] = new Eden.Core.Database.Repositories
            .LifelongCounters { DailyMonsterKill = 8 };
        sc.Works = new[] { new WorkSlot { WorkNum = 0, Running = true } };
        var st = new ZoneEntryFlow.EntryState();
        st.RecommendTracing.Add(999); // doit être remplacé, pas fusionné
        ZoneEntryFlow.AbsorbEntryFields(st, sc);

        Assert.Equal((5, 9), st.LifeSkillLearned[11]);
        Assert.Equal((short)7, st.FortuneCounters[2]);
        Assert.Equal(55u, st.RecommendProgress[101]);
        Assert.Equal(1, st.RecommendFinished[101]);
        Assert.Equal(new List<ushort> { 101 }, st.RecommendTracing);
        Assert.Contains(4444, st.CollectedItemIds);
        Assert.Equal(12, st.CollectionGroupPoints[1000]);
        Assert.Single(st.CustomClassSlots);
        Assert.Equal(8, st.Lifelong[3].DailyMonsterKill);
        Assert.Single(st.Works);

        // Detachment : muter le scratch post-install ne doit pas
        // apparaître dans l'état live (les dicts sont copiés par
        // contenu, pas aliasés).
        sc.LifeSkillLearned[22] = (1, 1);
        sc.RecommendProgress[202] = 1u;
        sc.RecommendTracing.Add(202);
        sc.FortuneCounters[9] = 9;
        Assert.False(st.LifeSkillLearned.ContainsKey(22));
        Assert.False(st.RecommendProgress.ContainsKey(202));
        Assert.DoesNotContain((ushort)202, st.RecommendTracing);
        Assert.False(st.FortuneCounters.ContainsKey(9));
    }

    [Fact]
    public void EntryBundle_Emitted_StartsFalse()
        => Assert.False(new ZoneEntryFlow.EntryBundle().Emitted);

    [Fact]
    public void Absorb_CopiesPreloadedLazyStates()
    {
        // Champs « lazy » préchargés par le prefetch d'entrée — les
        // objets sont transférés par référence (scratch mort après
        // install) et le guide tuto copié par contenu ; les gates
        // ??=/Loaded des handlers sautent alors leur requête live.
        var sc = Scratch();
        sc.BattlefieldStats = new int[26];
        sc.BattlefieldStats[3] = 77;
        sc.NodeTimes = new Dictionary<int, int> { [512] = 2 };
        sc.NewPlayerGuide[5] = (3, 0);
        sc.NewPlayerGuide[9] = (1, 2);
        sc.NewPlayerGuideLoaded = true;
        var st = new ZoneEntryFlow.EntryState();
        ZoneEntryFlow.AbsorbEntryFields(st, sc);

        Assert.Same(sc.BattlefieldStats, st.BattlefieldStats);
        Assert.Equal(77, st.BattlefieldStats![3]);
        Assert.Same(sc.NodeTimes, st.NodeTimes);
        Assert.Equal(2, st.NodeTimes![512]);
        Assert.True(st.NewPlayerGuideLoaded);
        Assert.Equal((3, 0), st.NewPlayerGuide[5]);
        Assert.Equal((1, 2), st.NewPlayerGuide[9]);

        // Detachment du guide : une entrée scratch post-install ne
        // doit pas fuiter dans l'état live.
        sc.NewPlayerGuide[12] = (1, 0);
        Assert.False(st.NewPlayerGuide.ContainsKey(12));
    }

    [Fact]
    public void Absorb_NullPreloadedLazyStates_StayNull()
    {
        // Prefetch dégradé (table en échec) → les champs lazy restent
        // null/false et les handlers retombent sur la requête live.
        var st = new ZoneEntryFlow.EntryState();
        st.BattlefieldStats = new int[26];
        st.NodeTimes = new Dictionary<int, int> { [1] = 1 };
        st.NewPlayerGuideLoaded = true;
        ZoneEntryFlow.AbsorbEntryFields(st, Scratch());
        Assert.Null(st.BattlefieldStats);
        Assert.Null(st.NodeTimes);
        Assert.False(st.NewPlayerGuideLoaded);
    }

    [Fact]
    public void StatFetch_KeyPresent_NullValue_IsAnAnswer()
    {
        // Une clé fournie à null = « service absent lu hors-boucle » —
        // l'étape doit la traiter comme une réponse, pas une absence.
        var fetch = new StatFetch(new Dictionary<string, object?>
        {
            ["limitless.perks"] = null,
            ["appellation"] = 12,
        });
        Assert.True(fetch.TryGet<object>("limitless.perks", out var p));
        Assert.Null(p);
        Assert.True(fetch.TryGet<int>("appellation", out var a));
        Assert.Equal(12, a);
        Assert.False(fetch.TryGet<int>("char", out _));
    }
}
