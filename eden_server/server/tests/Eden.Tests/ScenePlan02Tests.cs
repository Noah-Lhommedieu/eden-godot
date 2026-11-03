using Eden.Core.Data.Parsers;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>Plan futur-feature/02 — scènes de quête : règles pures des
/// verbes d'événement (BuffCheck/6P, ScreenMessage, verrou
/// MonsterAction), décodage des commandes texte mission→zone, état du
/// monde vivant (NPC mobile, verrou d'IA) et données réelles des
/// scènes (verbes présents, args texte conservés).</summary>
public class ScenePlan02Tests
{
    private static readonly string SceneDir =
        TestPaths.RepoRoot + "/server_us/Data/scene";

    // ---------------------------------------------------- conditions

    /// <summary>Event_If_ScreenMessage = vrai quels que soient les args
    /// (corps natif @0x080db5f0 : *result = 1).</summary>
    [Fact]
    public void ScreenMessageCond_AlwaysTrue() =>
        Assert.True(SceneEventRules.ScreenMessageCond());

    /// <summary>BuffCheck : 11 = porté, 12 = absent, autre = faux.</summary>
    [Theory]
    [InlineData(true, 11, true)]
    [InlineData(false, 11, false)]
    [InlineData(true, 12, false)]
    [InlineData(false, 12, true)]
    [InlineData(true, 13, false)]
    [InlineData(false, 0, false)]
    public void BuffPresence_Modes(bool has, int mode, bool expected) =>
        Assert.Equal(expected, SceneEventRules.BuffPresence(has, mode));

    /// <summary>BuffCheck6P compare les piles (0 si absent) — ops
    /// 1=≥ 2=≤ 3== 4=≠ ; exemples des scripts S001 (« 13 1 1 » =
    /// ≥1 pile ; « 13 3 1 » = exactement 1 ; « 13 2 0 » = aucune).</summary>
    [Theory]
    [InlineData(0, 1, 1, false)]   // absent, ≥1 → faux
    [InlineData(1, 1, 1, true)]
    [InlineData(3, 3, 1, false)]   // == 1
    [InlineData(1, 3, 1, true)]
    [InlineData(0, 2, 0, true)]    // ≤0 → absent ok
    [InlineData(2, 2, 0, false)]
    [InlineData(0, 4, 0, false)]   // ≠0
    [InlineData(5, 4, 0, true)]
    [InlineData(-3, 3, 0, true)]   // pile négative bornée à 0
    public void BuffStack_ComparesStacks(int stack, int op, int val,
        bool expected) =>
        Assert.Equal(expected, SceneEventRules.BuffStack(stack, op, val));

    [Theory]
    [InlineData(1, 5, 5, true)]
    [InlineData(1, 4, 5, false)]
    [InlineData(2, 4, 5, true)]
    [InlineData(3, 5, 5, true)]
    [InlineData(4, 5, 5, false)]
    [InlineData(9, 5, 5, false)]   // op inconnu
    public void Compare_NativeOps(int op, int lhs, int rhs, bool expected) =>
        Assert.Equal(expected, SceneEventRules.Compare(lhs, op, rhs));

    // ------------------------------------------------- verrou d'action

    /// <summary>La durée de motion connue l'emporte sur « Time » ;
    /// sans motion c'est Time ; bornée.</summary>
    [Theory]
    [InlineData(5, 0, 5.0)]
    [InlineData(5, 15, 1.5)]    // motion 1,5 s
    [InlineData(-2, 0, 0.0)]
    [InlineData(9999, 0, SceneEventRules.MaxActionLockSeconds)]
    public void ActionLockSeconds_MotionWinsThenTime(int time, int endDs,
        double expected) =>
        Assert.Equal(expected,
            SceneEventRules.ActionLockSeconds(time, endDs), 6);

    /// <summary>Le verrou fige le mob jusqu'à EventActionUntil.</summary>
    [Fact]
    public void LiveMob_EventActionLock_ExpiresWithClock()
    {
        var live = new ZoneLiveWorld();
        var m = live.SpawnHostileMob(1, 0, 1001, 0, 10, 0, 10, 0);
        var now = DateTime.UtcNow;
        Assert.False(m.EventActionLocked(now));
        m.EventActionUntil = now.AddSeconds(3);
        Assert.True(m.EventActionLocked(now));
        Assert.True(m.EventActionLocked(now.AddSeconds(2.9)));
        Assert.False(m.EventActionLocked(now.AddSeconds(3.1)));
    }

    // ------------------------------------------------ commandes texte

    [Fact]
    public void ParseTextCommand_BornNpcByTID()
    {
        var c = SceneEventRules.ParseTextCommand(
            "BornNpcByTID NpcTID= 52062 NodeID= 601 PositionID= 165")!;
        Assert.Equal("BornNpcByTID", c.Verb);
        Assert.Equal(52062, c.Int("NpcTID"));
        Assert.Equal(601, c.Int("NodeID"));
        Assert.Equal(165, c.Int("PositionID"));
    }

    [Fact]
    public void ParseTextCommand_MonsterActionByHandle_AllNativeKeys()
    {
        var c = SceneEventRules.ParseTextCommand(
            "MonsterActionByHandle Handle= 71 NodeID= 1 Time= 3 " +
            "ActionID= 1 DlgID= 102628 Trigger_Id= 4242")!;
        Assert.Equal(71, c.Int("Handle"));
        Assert.Equal(3, c.Int("Time"));
        Assert.Equal(1, c.Int("ActionID"));
        Assert.Equal(102628, c.Int("DlgID"));
        Assert.Equal(4242, c.Int("Trigger_Id"));
    }

    [Fact]
    public void ParseTextCommand_PlaySoundString_AndGluedKeys()
    {
        var c = SceneEventRules.ParseTextCommand(
            "PlaySound NodeID= 1 FileName= s99111 PositionID= 168")!;
        Assert.Equal("s99111", c.Str("FileName"));
        var g = SceneEventRules.ParseTextCommand("KillNpcByTID NpcTID=7 NodeID=2")!;
        Assert.Equal(7, g.Int("NpcTID"));
        Assert.Equal(2, g.Int("NodeID"));
        Assert.Equal(0, g.Int("absent"));
        Assert.Null(SceneEventRules.ParseTextCommand("   "));
    }

    [Fact]
    public void ParseTextCommand_WalkExtension_Floats()
    {
        var c = SceneEventRules.ParseTextCommand(
            "NpcWalkToByTID NpcTID= 5 NodeID= 3 X= 12.5 Y= -7.25")!;
        Assert.Equal(12.5f, c.Float("X"));
        Assert.Equal(-7.25f, c.Float("Y"));
    }

    [Theory]
    [InlineData("s2341", 2341)]
    [InlineData("2341", 2341)]
    [InlineData("sXX", 0)]
    [InlineData("", 0)]
    public void EffectFileNo_ParsesMissionFileName(string f, int n) =>
        Assert.Equal(n, SceneEventRules.EffectFileNo(f));

    /// <summary>S176 : PositionID 54 = groupe [position] col7 54
    /// (« 第三道門擊破特效指定點 ») — pas une ligne event_area ; sans
    /// groupe on retombe sur la position du déclencheur.</summary>
    [Fact]
    public void ScenePoint_ResolvesPositionGroup_ElseFallback()
    {
        var sc = SceneParser.Parse(Path.Combine(SceneDir, "S176.ini"));
        var (x, y, z) = ZoneEntryFlow.ScenePoint(sc, 54, 1, 2);
        Assert.Equal(331f, x);
        Assert.Equal(310.749f, y, 2);
        Assert.Equal(168.891f, z, 2);
        Assert.Equal((1f, 2f, 0f), ZoneEntryFlow.ScenePoint(sc, 9999, 1, 2));
    }

    /// <summary>Events réels : PlayEffect (effet fichier position) et
    /// ItemCheck/BuffCheck6P existent dans le corpus S176/S178.</summary>
    [Fact]
    public void RealScene_S176_PlayEffectArgs()
    {
        var sc = SceneParser.Parse(Path.Combine(SceneDir, "S176.ini"));
        var fx = sc.Events.Values.SelectMany(e => e.Branches)
            .SelectMany(b => b.Actions)
            .First(a => a.Name == "Event_PlayEffect");
        Assert.Equal(new[] { 3, 2341, 54 }, fx.Args);
    }

    /// <summary>Blob du MonsterSay : « DLG1 » + i32 LE ; vide sans id.</summary>
    [Fact]
    public void DialogueBlob_MagicPlusId()
    {
        var b = SceneEventRules.EncodeDialogueBlob(102628);
        Assert.Equal(8, b.Length);
        Assert.Equal("DLG1", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal(102628, BitConverter.ToInt32(b, 4));
        Assert.Empty(SceneEventRules.EncodeDialogueBlob(0));
        Assert.Empty(SceneEventRules.EncodeDialogueBlob(-5));
    }

    // ------------------------------------------------- marche scriptée

    [Fact]
    public void ClampWalk_ShortStepUntouched_LongStepCapped()
    {
        Assert.Equal((110f, 100f), SceneEventRules.ClampWalk(100, 100, 110, 100));
        var (x, y) = SceneEventRules.ClampWalk(0, 0, 4000, 0);
        Assert.Equal(SceneEventRules.MaxScriptedWalk, x, 3);
        Assert.Equal(0f, y, 3);
    }

    /// <summary>MoveNpc remplace l'entité de sight (nouvelle position,
    /// même handle/template) et refuse un handle monstre / inconnu.</summary>
    [Fact]
    public void LiveWorld_MoveNpc_ReplacesSightEntity()
    {
        var live = new ZoneLiveWorld();
        var npc = live.SpawnNpc(7, 0, 501, 10, 20, 0, 0);
        var mob = live.SpawnHostileMob(7, 0, 1001, 0, 5, 0, 5, 0);

        var old = live.MoveNpc(7, 0, npc.Handle, 30, 40, 1.5f);
        Assert.Equal((10f, 20f), old);
        var now = Assert.Single(live.NpcsOfTemplate(7, 0, 501));
        Assert.Equal(npc.Handle, now.Handle);
        Assert.Equal(30f, now.X);
        Assert.Equal(40f, now.Y);
        Assert.Equal(1.5f, now.Facing);
        Assert.Null(live.MoveNpc(7, 0, mob.Handle, 1, 1, 0));   // mob
        Assert.Null(live.MoveNpc(7, 0, 0xDEAD, 1, 1, 0));       // inconnu
        Assert.Null(live.MoveNpc(7, 9, npc.Handle, 1, 1, 0));   // autre instance
        Assert.Empty(live.NpcsOfTemplate(7, 0, 999));
    }

    /// <summary>NPC né par event puis tué : le template n'est plus listé
    /// (Event_BornNpc/KillNpc — l'acteur redevient NPC ordinaire quand
    /// il n'est pas tué).</summary>
    [Fact]
    public void LiveWorld_BornNpc_ThenKilled()
    {
        var live = new ZoneLiveWorld();
        live.SpawnNpc(7, 0, 501, 1, 1, 0, 0);
        Assert.Single(live.NpcsOfTemplate(7, 0, 501));
        Assert.Single(live.DespawnNpcs(7, 0, 501));
        Assert.Empty(live.NpcsOfTemplate(7, 0, 501));
    }

    // ------------------------------------------------- données réelles

    /// <summary>S001 : les verbes de texte (PlaySound s99111) gardent
    /// leur jeton brut dans Words (Args perd la chaîne) ; MonsterAction
    /// (groupe kf dlg time) est présent.</summary>
    [Fact]
    public void RealScene_S001_PlaySoundWords_AndMonsterAction()
    {
        var scene = SceneParser.Parse(Path.Combine(SceneDir, "S001.ini"));
        var cmds = scene.Events.Values
            .SelectMany(e => e.Branches)
            .SelectMany(b => b.Actions).ToList();
        var snd = cmds.First(c => c.Name == "Event_PlaySound");
        Assert.Equal("s99111", snd.Words[0]);
        Assert.Equal(168, snd.Args[1]);
        var act = cmds.First(c => c.Name == "Event_MonsterAction");
        Assert.True(act.Args.Length >= 4);   // grp kf dlg time
        Assert.True(act.Args[3] > 0);        // « Time » = secondes de verrou
    }

    /// <summary>Les scènes réelles n'utilisent que des verbes connus du
    /// moteur ou explicitement documentés — filet contre un verbe
    /// ignoré en silence.</summary>
    [Fact]
    public void RealScenes_VerbsAreKnownOrDocumented()
    {
        var known = new HashSet<string>
        {
            "Event_If_Level", "Event_If_EventState", "Event_If_Variable",
            "Event_If_Monster", "Event_If_RebornMonster",
            "Event_If_RebornMonsterSum", "Event_If_ScreenMessage",
            "Event_If_BuffCheck", "Event_If_BuffCheck6P",
            "Event_If_ItemCheck",
            "Event_Transport", "Event_ScreenMessage", "Event_RebornMonster",
            "Event_KillMonster", "Event_BornNpc", "Event_KillNpc",
            "Event_MonsterAction", "Event_GiveBuff", "Event_DeleteBuff",
            "Event_TriggerEvent", "Event_Variable", "Event_RanDomVariable",
            "Event_EventState", "Event_SetAchievement",
            "Event_SetAchievementNPC", "Event_PlaySound",
            "Event_ChannelMsg", "Event_PlayEffect",
        };
        var unknown = new SortedSet<string>();
        foreach (var f in Directory.GetFiles(SceneDir, "S*.ini"))
        {
            var sc = SceneParser.Parse(f);
            foreach (var b in sc.Events.Values.SelectMany(e => e.Branches))
                foreach (var c in b.Conds.Concat(b.Actions))
                    if (c.Name.StartsWith("Event_") &&
                        !known.Contains(c.Name)) unknown.Add(c.Name);
        }
        Assert.True(unknown.Count == 0,
            "verbes non gérés : " + string.Join(", ", unknown));
    }
}
