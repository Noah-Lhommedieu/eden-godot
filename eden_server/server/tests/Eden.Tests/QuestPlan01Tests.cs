using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Mission.Services;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>Plan futur-feature/01 — items de quête positionnels
/// (SummonInEventArea), objectif CheckBuff (escorte), partage de quête
/// (op 6/7), symboles NPC 345. Données réelles server_us + règles
/// pures ; les chemins DB (FMS 3↔4) sont couverts via les règles et les
/// gardes sans DB du moteur.</summary>
public class QuestPlan01Tests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";
    private static readonly string SceneDir =
        TestPaths.RepoRoot + "/server_us/Data/scene";

    private static readonly Lazy<GameDataService> Gd = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var g = new GameDataService(DbDir);
        g.LoadAll();
        return g;
    });

    private static QuestEngine NewEngine() =>
        new(Gd.Value, new MissionDb(null!), DbDir);

    private static int Col(MissionParser.MissionDef m, int i) =>
        i < m.Raw.Length && int.TryParse(m.Raw[i], out var v) ? v : 0;

    // ------------------------------------------------ SummonInEventArea

    /// <summary>Sound Wave Device (item 32990) → enchant 53031 →
    /// commande 1075 « 58334,1,10,160 » (mob, compte, node, clé de
    /// position) — preuve data du décodage de Init @0x0855a590.</summary>
    [Fact]
    public void SoundWaveDevice_DecodesToSummonSpec()
    {
        var item = Gd.Value.Items.First(i => i.Id == 32990);
        Assert.Equal(53031, item.UseEnchantId);
        var cmds = new EnchantService(Gd.Value.Enchants)
            .GetUseCommands(53031);
        var c = Assert.Single(cmds, x => x.CommandId == 1075);
        Assert.Equal("SummonInEventArea",
            EnchantCommands.Names[c.CommandId]);
        var spec = QuestItemRules.ParseSummon(
            c.Args.Select(v => (int)v).ToArray());
        Assert.NotNull(spec);
        Assert.Equal(58334, spec!.MobId);
        Assert.Equal(1, spec.Count);
        Assert.Equal(10, spec.NodeId);
        Assert.Equal(160, spec.PositionKey);
    }

    [Fact]
    public void ParseSummon_RejectsMobZero_AndClampsCount()
    {
        Assert.Null(QuestItemRules.ParseSummon(new[] { 0, 1 }));
        Assert.Equal(10, QuestItemRules.ParseSummon(
            new[] { 5, 99 })!.Count);
        Assert.Equal(1, QuestItemRules.ParseSummon(
            new[] { 5, 0 })!.Count);
        Assert.Equal(0, QuestItemRules.ParseSummon(
            new[] { 5, 1 })!.NodeId);   // 1074 : pas de node → lanceur
    }

    [Fact]
    public void CheckSummon_NodeAndPositionRules()
    {
        var spec = new QuestItemRules.SummonSpec(58334, 1, 10, 160, 0, 0);
        Assert.Equal(QuestItemRules.Refusal.WrongNode,
            QuestItemRules.CheckSummon(spec, 11, true));
        Assert.Equal(QuestItemRules.Refusal.PositionMissing,
            QuestItemRules.CheckSummon(spec, 10, false));
        Assert.Equal(QuestItemRules.Refusal.None,
            QuestItemRules.CheckSummon(spec, 10, true));
        // node 0 (1074) : aucune contrainte.
        Assert.Equal(QuestItemRules.Refusal.None,
            QuestItemRules.CheckSummon(
                spec with { NodeId = 0 }, 99, false));
        Assert.Equal(0x5c0, QuestItemRules.TextWrongNode);
    }

    /// <summary>La clé 160 de S010 est la ligne [position] « (355.6,
    /// 341.8) » — le « (349,341) » du wiki.</summary>
    [Fact]
    public void Position160_OfScene10_ResolvesNearWikiCoords()
    {
        var scene = SceneParser.Parse(Path.Combine(SceneDir, "S010.ini"));
        var p = QuestItemRules.ResolvePosition(scene, 160);
        Assert.NotNull(p);
        Assert.InRange(p!.Value.X, 349f, 360f);
        Assert.InRange(p.Value.Y, 335f, 345f);
        Assert.Null(QuestItemRules.ResolvePosition(scene, 99999));
        Assert.Null(QuestItemRules.ResolvePosition(null, 160));
        Assert.Null(QuestItemRules.ResolvePosition(scene, 0));
    }

    [Fact]
    public void CountLiveSummons_CountsOnlyOwnLiveMobs()
    {
        ZoneLiveWorld.LiveMob Mob(uint h, int owner, bool alive) => new()
        {
            Handle = h, MobId = 58334, NodeId = 10,
            SummonedByCharId = owner, Alive = alive,
        };
        var mobs = new[] { Mob(1, 7, true), Mob(2, 7, false),
            Mob(3, 8, true) };
        Assert.Equal(1, QuestItemRules.CountLiveSummons(mobs, 7, 58334));
        Assert.Equal(0, QuestItemRules.CountLiveSummons(mobs, 7, 1));
    }

    // ------------------------------------------------------- CheckBuff

    [Fact]
    public void BuffRules_NativeComparison()
    {
        Assert.True(QuestBuffRules.Satisfied(1, 1, 1));
        Assert.False(QuestBuffRules.Satisfied(1, 1, 0));
        Assert.True(QuestBuffRules.Satisfied(2, 3, 3));
        Assert.False(QuestBuffRules.Satisfied(2, 3, 4));
        Assert.True(QuestBuffRules.Satisfied(3, 10, 10));
        Assert.False(QuestBuffRules.Satisfied(3, 10, 9));
        Assert.True(QuestBuffRules.Satisfied(4, 1, 0));
        Assert.False(QuestBuffRules.Satisfied(4, 1, 1));
        Assert.False(QuestBuffRules.Satisfied(9, 1, 1));
        Assert.True(QuestBuffRules.IsTrackedId(45001));
        Assert.True(QuestBuffRules.IsTrackedId(59999));
        Assert.False(QuestBuffRules.IsTrackedId(45000));
        Assert.False(QuestBuffRules.IsTrackedId(60000));
    }

    /// <summary>Mission 2990 (escorte de Mossia-like) :
    /// « NPCTalk 53539 7321 : CheckBuff 58813 1 1 ».</summary>
    [Fact]
    public void Mission2990_HasCheckBuffObjective_AndTracked()
    {
        var m = Gd.Value.Missions.First(x => x.Id == 2990);
        var o = Assert.Single(QuestBuffRules.Objectives(m));
        Assert.Equal(new QuestBuffRules.BuffObjective(58813, 1, 1), o);
        Assert.False(QuestBuffRules.AllSatisfied(m,
            new Dictionary<int, int>()));
        Assert.True(QuestBuffRules.AllSatisfied(m,
            new Dictionary<int, int> { [58813] = 1 }));
        Assert.Contains(58813,
            QuestBuffRules.TrackedEnchants(Gd.Value.Missions));
        // Les 131 CheckBuff réels portent tous un enchant dans la plage.
        var tracked = QuestBuffRules.TrackedEnchants(Gd.Value.Missions);
        Assert.True(tracked.Count >= 100);
    }

    [Fact]
    public async Task Engine_CheckMissionEnchant_UpdatesMirror_AndGatesCompletion()
    {
        var engine = NewEngine();
        var s = new MissionSession(null!, null, null!);
        var c = engine.ContextFor(4242, 90);
        var m = Gd.Value.Missions.First(x => x.Id == 2990);
        // Sans buff : l'objectif n'est pas rempli (aucun accès DB —
        // 2990 n'a pas d'objectif inventaire).
        Assert.False(await engine.AllItemsDoneAsync(4242, m));
        await engine.OnCheckMissionEnchantAsync(s, 4242, 58813, 1);
        Assert.Equal(1, c.Buffs[58813]);
        Assert.True(await engine.AllItemsDoneAsync(4242, m));
        await engine.OnCheckMissionEnchantAsync(s, 4242, 58813, 0);
        Assert.False(c.Buffs.ContainsKey(58813));
        Assert.False(await engine.AllItemsDoneAsync(4242, m));
        // Enchant hors plage native : ignoré.
        await engine.OnCheckMissionEnchantAsync(s, 4242, 100, 3);
        Assert.False(c.Buffs.ContainsKey(100));
    }

    // ----------------------------------------------------------- Share

    [Fact]
    public void ShareRules_NativeOrder_AndTexts()
    {
        var V = QuestShareRules.Verdict.Ok;
        Assert.Equal(QuestShareRules.Verdict.NonShareable,
            QuestShareRules.Check(2, 0, 0, true));
        Assert.Equal(QuestShareRules.Verdict.Full,
            QuestShareRules.Check(0, 25, 0, true));
        Assert.Equal(V, QuestShareRules.Check(0, 24, 0, true));
        Assert.Equal(QuestShareRules.Verdict.OnQuest,
            QuestShareRules.Check(0, 0, 3, true));
        Assert.Equal(QuestShareRules.Verdict.OnQuest,
            QuestShareRules.Check(0, 0, 4, true));
        Assert.Equal(QuestShareRules.Verdict.Completed,
            QuestShareRules.Check(0, 0, 1, true));
        Assert.Equal(QuestShareRules.Verdict.NotApproved,
            QuestShareRules.Check(0, 0, 0, false));
        Assert.Equal(V, QuestShareRules.Check(0, 0, 0, true));
        Assert.Equal(0x61d, QuestShareRules.TextOf(
            QuestShareRules.Verdict.NonShareable));
        Assert.Equal(0xa03, QuestShareRules.TextOf(
            QuestShareRules.Verdict.Full));
        Assert.Equal(0, QuestShareRules.TextOf(V));
    }

    [Fact]
    public void ShareRegistry_LifeCycle()
    {
        var r = new MissionShareRegistry();
        Assert.False(r.Start(1, 10, 500, Array.Empty<int>()));
        Assert.False(r.Start(1, 10, 500, new[] { 10 }));   // soi seul
        Assert.True(r.Start(1, 10, 500, new[] { 10, 11, 12 }));
        Assert.True(r.IsActive(1));
        Assert.Equal(500, r.ActiveMid(1));
        Assert.Equal(10, r.SharerOf(1));
        Assert.False(r.Start(1, 11, 600, new[] { 10 }));   // déjà actif
        Assert.Null(r.Answer(1, 99, out _));               // pas attendu
        var a = r.Answer(1, 11, out var fin);
        Assert.Equal((10, 500), a);
        Assert.False(fin);
        r.Answer(1, 12, out fin);
        Assert.True(fin);
        Assert.False(r.IsActive(1));
        Assert.Equal(0, r.ActiveMid(1));
    }

    [Fact]
    public void ShareRegistry_ClearChar_RemovesSharerAndPending()
    {
        var r = new MissionShareRegistry();
        r.Start(1, 10, 500, new[] { 11, 12 });
        r.ClearChar(11);
        Assert.True(r.IsActive(1));
        r.ClearChar(10);
        Assert.False(r.IsActive(1));
    }

    [Fact]
    public void Engine_ShareVerdict_UsesRealMissions()
    {
        var engine = NewEngine();
        // Quête partageable (col6 bit1 clair) dont les conditions se
        // limitent à If_FMS/If_CharLevel ≤ 90 — satisfaites ci-dessous.
        var plain = Gd.Value.Missions.First(m =>
            m.CompleteLimit == 0 && (m.Field6 & 2) == 0 &&
            Col(m, 8) <= 90 && Col(m, 11) > 0 &&
            m.Conditions.All(c =>
                (c.Name == "If_FMS" && c.Args.Count >= 2 &&
                 c.Args[1] == "done") ||
                (c.Name == "If_CharLevel" && c.Args.Count >= 2 &&
                 int.TryParse(c.Args[1], out var lv) && lv <= 90)));
        var locked = Gd.Value.Missions.First(m => (m.Field6 & 2) != 0);
        var t = engine.ContextFor(5001, 90);
        foreach (var c in plain.Conditions.Where(x => x.Name == "If_FMS"))
            t.Completed.Add(int.Parse(c.Args[0]));
        Assert.Equal(QuestShareRules.Verdict.Ok,
            engine.ShareVerdict(t, plain));
        Assert.Equal(QuestShareRules.Verdict.NonShareable,
            engine.ShareVerdict(t, locked));
        t.FMS[plain.Id] = 3;
        Assert.Equal(QuestShareRules.Verdict.OnQuest,
            engine.ShareVerdict(t, plain));
        t.FMS[plain.Id] = 1;
        Assert.Equal(QuestShareRules.Verdict.Completed,
            engine.ShareVerdict(t, plain));
        t.FMS.Remove(plain.Id);
        for (var i = 0; i < 25; i++)
            t.Active[900000 + i] = new QuestEngine.ActiveQuest();
        Assert.Equal(QuestShareRules.Verdict.Full,
            engine.ShareVerdict(t, plain));
    }

    // --------------------------------------------------------- Symbols

    [Fact]
    public void SymbolRules_PriorityReadyAvailableInProgress()
    {
        var map = new Dictionary<int, short>();
        QuestSymbolRules.Add(map, 50001, QuestSymbolRules.InProgress);
        QuestSymbolRules.Add(map, 50001, QuestSymbolRules.Available);
        Assert.Equal(QuestSymbolRules.Available, map[50001]);
        QuestSymbolRules.Add(map, 50001, QuestSymbolRules.Ready);
        Assert.Equal(QuestSymbolRules.Ready, map[50001]);
        QuestSymbolRules.Add(map, 50001, QuestSymbolRules.InProgress);
        Assert.Equal(QuestSymbolRules.Ready, map[50001]);
        QuestSymbolRules.Add(map, 0, QuestSymbolRules.Ready);   // ignoré
        Assert.Single(map);
    }

    [Fact]
    public void Engine_NpcSymbols_AvailableThenReady()
    {
        var engine = NewEngine();
        var m = Gd.Value.Missions.First(x =>
            x.Conditions.Count == 0 && x.CompleteLimit == 0 &&
            Col(x, 8) <= 1 && Col(x, 11) > 0 && Col(x, 17) > 0 &&
            Col(x, 11) != Col(x, 17));
        var c = engine.ContextFor(6001, 90);
        var before = engine.ComputeNpcSymbols(c);
        Assert.True(before.ContainsKey(Col(m, 11)));           // « ! »
        c.Active[m.Id] = new QuestEngine.ActiveQuest { Status = 2 };
        var after = engine.ComputeNpcSymbols(c);
        Assert.Equal(QuestSymbolRules.Ready, after[Col(m, 17)]); // « ? »
    }

    /// <summary>Wire 65 (natif Deserialize @0x081192e0) :
    /// [i32 charUid][s16 node][u16 n][n×(i32 npc, s16 sym)].</summary>
    [Fact]
    public async Task Engine_PushNpcSymbols_EmitsNative65Wire()
    {
        var engine = NewEngine();
        var s = new MissionSession(null!, null, null!);
        (ushort Ncid, byte[] Body)? got = null;
        s.Tap = (n, b) => { got = (n, b); return Task.CompletedTask; };
        var c = engine.ContextFor(7001, 90, nodeId: 3);
        await engine.PushNpcSymbolsAsync(s, c);
        Assert.NotNull(got);
        Assert.Equal(65, got!.Value.Ncid);
        var b = got.Value.Body;
        Assert.Equal(7001, BitConverter.ToInt32(b, 0));
        Assert.Equal(3, BitConverter.ToInt16(b, 4));
        var n = BitConverter.ToUInt16(b, 6);
        Assert.True(n > 0);
        Assert.Equal(8 + n * 6, b.Length);
    }

    [Fact]
    public void NpcSymbolRelay_Converts65To345_AndFiltersByScene()
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(77));        // charUid
        b.AddRange(BitConverter.GetBytes((short)3));  // node
        b.AddRange(BitConverter.GetBytes((ushort)2)); // n
        b.AddRange(BitConverter.GetBytes(50001));
        b.AddRange(BitConverter.GetBytes((short)1));
        b.AddRange(BitConverter.GetBytes(50999));
        b.AddRange(BitConverter.GetBytes((short)2));
        var all = NpcSymbolRelay.ToClient345(b.ToArray(), null)!;
        Assert.Equal(2, BitConverter.ToInt16(all, 0));
        Assert.Equal(2 + 2 * 8, all.Length);
        Assert.Equal(50001, BitConverter.ToInt32(all, 2));
        Assert.Equal(1, BitConverter.ToInt16(all, 6));
        Assert.Equal(0, BitConverter.ToInt16(all, 8));   // sym2
        var one = NpcSymbolRelay.ToClient345(b.ToArray(),
            new HashSet<int> { 50999 })!;
        Assert.Equal(1, BitConverter.ToInt16(one, 0));
        Assert.Equal(50999, BitConverter.ToInt32(one, 2));
        Assert.Equal(2, BitConverter.ToInt16(one, 6));
        Assert.Null(NpcSymbolRelay.ToClient345(new byte[4], null));
    }
}
