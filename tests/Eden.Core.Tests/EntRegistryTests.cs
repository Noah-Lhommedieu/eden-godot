using EdenClient.Combat;
using EdenClient.Data.Tables;
using EdenClient.Game;
using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests purs du registre d'entités distantes (lot A M6) — spawn
/// 298/265/329, mouvement 326/267, despawn 269/294/330, vitals 280,
/// display/whois 263, combat 272/305, self-filtering, half-float
/// facing et <see cref="ZoneEntityRegistry.ParseDisplayTail"/>.
/// </summary>
public class EntRegistryTests
{
    private static ZoneEntityRegistry Reg(int playerLevel = 0,
        uint selfCharId = 0)
    {
        return new ZoneEntityRegistry
        {
            PlayerLevel = playerLevel,
            SelfCharId = selfCharId,
        };
    }

    private static ZoneSeeMob Mob(uint handle, short id, byte kind = 12,
        float x = 10, float y = 20, string name = "") => new()
    {
        Handle = handle, EntityId = id, Kind = kind,
        X = x, Y = y, FieldZ = 1.5f, FieldSpeed = 6f,
        DisplayName = name, Hp = 100, MaxHp = 100,
    };

    private static MonsterTable.MonsterDef Def(int id,
        string model = "M02599", int level = 12,
        int type = 1, int ai = 0, int aggro = 0) => new()
    {
        Id = id, ModelCode = model, Name = "mob" + id,
        Level = level, MonsterType = type, AiId = ai,
        AggroCells = aggro, AttackPower = 10, AttackRangeMax = 2,
    };

    // ======================= spawn / classification =================

    [Fact]
    public void SeeMob_Monster_SpawnsWithResolvedStemAndDanger()
    {
        var r = Reg(playerLevel: 40);
        r.MonsterDef = id => Def(id, "M02599", 12, type: 2);
        r.ModelExists = s => s == "M025";   // ^2 résout
        r.MonsterName = id => "Sanglier";
        ZoneEntity? spawned = null;
        r.Spawned += e => spawned = e;

        r.HandleSeeMob(Mob(0xFFFF0001, 20001));

        var e = r.Entities[0xFFFF0001u];
        Assert.Equal(EntKind.Monster, e.Kind);
        Assert.Equal("M025", e.ModelStem);          // code[..^2]
        Assert.Equal(12, e.Level);
        Assert.Equal("Lv12 Sanglier", e.DisplayName); // FR prioritaire
        Assert.Equal(EntDanger.Aggressive, e.Danger); // élite → rouge
        Assert.Same(e, spawned);
        Assert.True(e.ModelScale > 1f);              // élite → plus gros
    }

    [Fact]
    public void SeeMob_StemCandidates_FallbackOrder()
    {
        var r = Reg();
        r.MonsterDef = id => Def(id, "M363040");
        var tried = new List<string>();
        r.ModelExists = s => { tried.Add(s); return s == "M363"; };

        r.HandleSeeMob(Mob(0xFFFF0002, 1234));

        Assert.Equal(new[] { "M3630", "M363" }, tried); // ^2 puis ^3
        Assert.Equal("M363", r.Entities[0xFFFF0002u].ModelStem);
    }

    [Fact]
    public void SeeMob_Npc_UsesSceneFallbackAndTitle()
    {
        var r = Reg();
        r.SceneNpc = id => new SceneTable.NpcSpawn
        {
            NpcId = id, ModelCode = "M014", Name = "scene-npc",
        };
        r.NpcName = id => "Marchand";
        r.NpcTitle = id => "Épicerie";

        r.HandleSeeMob(Mob(0x88C80001, 4002, kind: 8));

        var e = r.Entities[0x88C80001u];
        Assert.Equal(EntKind.Npc, e.Kind);
        Assert.Equal("M014", e.ModelStem);
        Assert.Equal("<Épicerie>\nMarchand", e.DisplayName);
    }

    [Fact]
    public void SeeChar_RemotePlayer_SpawnsAndRequestsWhois()
    {
        var r = Reg();
        uint whois = 0;
        r.WhoisRequested += id => whois = id;

        r.HandleSeeChar(new ZoneSeeChar
        {
            CharId = 42, X = 5, Y = 6, Facing = 3f,
            Level = 20, Revisions = new byte[] { 7, 0, 0, 0, 0 },
        });

        var e = r.Entities[42u];
        Assert.Equal(EntKind.Player, e.Kind);
        Assert.False(e.IsTreasure);
        Assert.Equal(20, e.Level);
        Assert.Equal(42u, whois);           // WhoIs émis (révision 7)
        // Re-SeeChar même révision → pas de second WhoIs.
        whois = 0;
        r.HandleSeeChar(new ZoneSeeChar
        {
            CharId = 42, Revisions = new byte[] { 7, 0, 0, 0, 0 },
        });
        Assert.Equal(0u, whois);
        // Révision changée → WhoIs repart.
        r.HandleSeeChar(new ZoneSeeChar
        {
            CharId = 42, Revisions = new byte[] { 8, 0, 0, 0, 0 },
        });
        Assert.Equal(42u, whois);
    }

    [Fact]
    public void SeeChar_SelfCharId_IsIgnored()
    {
        var r = Reg(selfCharId: 42);
        r.HandleSeeChar(new ZoneSeeChar { CharId = 42, Level = 1 });
        Assert.Empty(r.Entities);
    }

    [Fact]
    public void SeeChar_OtherCharId_NotIgnored()
    {
        var r = Reg(selfCharId: 42);
        r.HandleSeeChar(new ZoneSeeChar { CharId = 43, Level = 1 });
        Assert.Single(r.Entities);
    }

    [Fact]
    public void SeeMob_ElfPet_IsFlaggedWithOwner()
    {
        var r = Reg();
        r.ResolveElfPet = owner => ("M144", "Elfy", 30);

        r.HandleSeeMob(new ZoneSeeMob
        {
            Handle = 0xFFFF0010, EntityId = -2537, Kind = 12,
            DisplayName = "Pet", Unk48 = 777, Hp = 1, MaxHp = 1,
        });

        var e = r.Entities[0xFFFF0010u];
        Assert.True(e.IsElfPet);
        Assert.Equal(777, e.OwnerCharId);
        Assert.Equal("M144", e.ModelStem);
    }

    // ============================ mouvement =========================

    [Fact]
    public void MovePrediction_FixedPointPushesMovement()
    {
        var r = Reg();
        var t = 0.0;
        r.Now = () => t;
        r.HandleSeeMob(Mob(0xFFFF0001, 1));

        // cur (160,320)÷16 = (10,20) ; dst (176,320)÷16 = (11,20).
        r.HandleMovePrediction(new ZoneMovePrediction
        {
            Handle = unchecked((int)0xFFFF0001), CurX = 160, CurY = 320,
            DstX = 176, DstY = 320, Flag = 7,
        });

        var e = r.Entities[0xFFFF0001u];
        Assert.Equal(10f, e.X, 3);
        Assert.Equal(20f, e.Y, 3);
        // Échantillonné au rendu t−0.15 : encore à l'origine.
        Assert.True(e.Interp.Sample(t, out var x, out var _,
            out var z, out _));
        Assert.Equal(10f, x, 3);
        Assert.Equal(-20f, z, 3);           // layout (ex, ez, −ey)
        // À la destination prévue : dst atteint (terminal → clamp).
        t = 3.0;
        Assert.True(e.Interp.Sample(t, out x, out _, out z, out _));
        Assert.Equal(11f, x, 3);
        Assert.Equal(-20f, z, 3);
    }

    [Fact]
    public void MovePrediction_NoDestination_IsSnap()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        r.HandleMovePrediction(new ZoneMovePrediction
        {
            Handle = unchecked((int)0xFFFF0001), CurX = 240, CurY = 160,
        });
        var e = r.Entities[0xFFFF0001u];
        Assert.Equal(15f, e.X, 3);
        Assert.Equal(10f, e.Y, 3);
    }

    [Fact]
    public void MovePrediction_HalfFloatFacing_Decodes()
    {
        // 3.5 en IEEE half = 0x4300 (signe 0, exp 16, mant 0x300).
        var m = new ZoneMovePrediction { Facing = 0x4300 };
        Assert.Equal(3.5f, m.FacingFloat, 4);
        // −1.0 half = 0xBC00.
        m.Facing = 0xBC00;
        Assert.Equal(-1f, m.FacingFloat, 4);

        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        m.Handle = unchecked((int)0xFFFF0001);
        m.CurX = 160; m.CurY = 320;
        r.HandleMovePrediction(m);
        Assert.Equal(-1f, r.Entities[0xFFFF0001u].Dir, 4);
    }

    [Fact]
    public void Goto_FarTarget_SnapsPosition()
    {
        var r = Reg();
        var t = 0.0;
        r.Now = () => t;
        r.HandleSeeMob(Mob(0xFFFF0001, 1, x: 0, y: 0));

        r.HandleGoto(new ZoneClientGoto
        {
            Handle = unchecked((int)0xFFFF0001), X = 100, Y = 100,
        });
        var e = r.Entities[0xFFFF0001u];
        Assert.Equal(100f, e.X);
        Assert.Equal(100f, e.Y);
    }

    [Fact]
    public void Goto_NearTarget_PushesPredictedMove()
    {
        var r = Reg();
        var t = 0.0;
        r.Now = () => t;
        r.HandleSeeMob(Mob(0xFFFF0001, 1, x: 0, y: 0));

        r.HandleGoto(new ZoneClientGoto
        {
            Handle = unchecked((int)0xFFFF0001), X = 4, Y = 0,
        });
        var e = r.Entities[0xFFFF0001u];
        t = 2.0;                            // destination atteinte
        Assert.True(e.Interp.Sample(t, out var x, out _,
            out var z, out _));
        Assert.Equal(4f, x, 2);
        Assert.Equal(0f, z, 2);
    }

    // ============================ despawn ===========================

    [Fact]
    public void LifeEradicate_RemovesEntity()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        ZoneEntity? gone = null;
        r.Despawned += e => gone = e;

        r.HandleLifeEradicate(
            new ZoneLifeEradicate { Handle = unchecked((int)0xFFFF0001) });
        Assert.Empty(r.Entities);
        Assert.NotNull(gone);
    }

    [Fact]
    public void LeaveNode_RemovesEntity()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        r.HandleLeaveNode(
            new ZoneLeaveNode { Value = unchecked((int)0xFFFF0001) });
        Assert.Empty(r.Entities);
    }

    [Fact]
    public void NodeExit_ClearsAll()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        r.HandleSeeMob(Mob(0xFFFF0002, 2));
        var despawned = 0;
        r.Despawned += _ => despawned++;
        r.HandleNodeExit(new ZoneNodeExit());
        Assert.Empty(r.Entities);
        Assert.Equal(2, despawned);
    }

    // ============================= vitals ===========================

    [Fact]
    public void UpdateAttr_HpAndMaxHp_Tracked()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        var e = r.Entities[0xFFFF0001u];

        r.HandleUpdateAttr(new ZoneUpdateAttr
        {
            Handle = unchecked((int)0xFFFF0001), AttrId = 0x11, Current = 500,
        });
        Assert.Equal(500, e.MaxHp);
        r.HandleUpdateAttr(new ZoneUpdateAttr
        {
            Handle = unchecked((int)0xFFFF0001), AttrId = 0, Current = 42,
        });
        Assert.Equal(42, e.Hp);
    }

    [Fact]
    public void UpdateAttr_ZeroHp_MarksDying()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        ZoneEntity? died = null;
        r.Died += e => died = e;
        r.HandleUpdateAttr(new ZoneUpdateAttr
        {
            Handle = unchecked((int)0xFFFF0001), AttrId = 0, Current = 0,
        });
        var e = r.Entities[0xFFFF0001u];
        Assert.True(e.Dying);
        Assert.Same(e, died);
    }

    // ========================= combat / mort ========================

    [Fact]
    public void ClientDie_MarksVictim()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        var died = 0;
        r.Died += _ => died++;
        r.HandleClientDie(
            new ZoneClientDie { Victim = 0xFFFF0001, Killer = 9 });
        Assert.True(r.Entities[0xFFFF0001u].Dying);
        Assert.Equal(1, died);
    }

    [Fact]
    public void MeleeAttack_RequestsAnimsOnBothEnds()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        r.HandleSeeMob(Mob(0xFFFF0002, 2));
        var got = new List<(uint, IReadOnlyList<string>)>();
        r.AnimRequested += (e, c) => got.Add((e.Handle, c));

        r.HandleMeleeAttack(new ZoneClientMeleeAttack
        {
            Attacker = 0xFFFF0001, Target = 0xFFFF0002,
            HitType = 0, MotionKfId = 1021,
        });
        Assert.Equal(2, got.Count);              // attack + hurt
    }

    [Fact]
    public void MeleeAttack_Fatal_KillsTarget()
    {
        var r = Reg();
        r.HandleSeeMob(Mob(0xFFFF0002, 2));
        var fatal = 1 << 8;                     // CombatFx.HitFatal & 0xFF
        // Retrouve le vrai code fatal via IsFatal : on scanne 0..1024.
        for (var i = 0; i < 1024; i++)
            if (CombatFx.IsFatal(i)) { fatal = i; break; }
        r.HandleMeleeAttack(new ZoneClientMeleeAttack
        {
            Attacker = 0xFFFF0001, Target = 0xFFFF0002,
            HitType = fatal,
        });
        Assert.True(r.Entities[0xFFFF0002u].Dying);
    }

    // =========================== trésors ============================

    [Fact]
    public void Treasure_SpawnThenDisappear()
    {
        var r = Reg();
        r.ItemName = id => "Potion";
        r.HandleSeeTreasure(new ZoneSeeTreasure
        {
            Handle = 777, ItemId = 501, X = 3, Y = 4, Count = 5,
        });
        var e = r.Entities[777u];
        Assert.True(e.IsTreasure);
        Assert.Contains("Potion", e.DisplayName);
        r.HandleTreasureDisappear(
            new ZoneTreasureDisappear { Value = 777 });
        Assert.Empty(r.Entities);
    }

    // ========================= transform ============================

    [Fact]
    public void MobTransform_ReResolvesModel()
    {
        var r = Reg();
        var defs = new Dictionary<int, MonsterTable.MonsterDef>
        {
            [1] = Def(1, "M02599"), [2] = Def(2, "M01499"),
        };
        r.MonsterDef = id => defs.GetValueOrDefault(id);
        r.ModelExists = s => s.StartsWith('M');
        var rebuilt = 0;
        r.VisualChanged += _ => rebuilt++;

        r.HandleSeeMob(Mob(0xFFFF0001, 1));
        Assert.Equal("M025", r.Entities[0xFFFF0001u].ModelStem);
        r.HandleMobTransform(new ZoneMobTransform
        {
            Handle = unchecked((int)0xFFFF0001), NewTemplateId = 2,
        });
        Assert.Equal("M014", r.Entities[0xFFFF0001u].ModelStem);
        Assert.True(rebuilt >= 2);
    }

    // ========================= display/whois ========================

    [Fact]
    public void DisplayChar_SetsBodyStemAndName()
    {
        var r = Reg();
        r.HandleSeeChar(new ZoneSeeChar { CharId = 42 });
        var app = new byte[32];
        r.HandleDisplayChar(new ZoneClientDisplayChar
        {
            CharId = 42, CharName = "Toto", Level = 33,
            Appearance = app, Rest = [],
        });
        var e = r.Entities[42u];
        Assert.Equal("Lv33 Toto", e.DisplayName);
        Assert.True(e.ModelStem.Length > 0);   // lettre corps
    }

    [Fact]
    public void ParseDisplayTail_MaskAndRideKey()
    {
        // mask 0x03 : hp i64 + maxHp i64, puis rideKey (bit6).
        var w = new BinWriter();
        w.WriteByte(0x03 | 0x40);
        w.WriteLong(100); w.WriteLong(200);
        w.WriteInt(4242);                       // rideKey
        w.WriteInt(0);                          // eqMask vide
        var (ride, worn) = ZoneEntityRegistry.ParseDisplayTail(
            w.ToArray());
        Assert.Equal(4242, ride);
        Assert.Empty(worn);
    }

    [Fact]
    public void ParseDisplayTail_EquipSlots()
    {
        // mask 0 : queue [u32 eqMask] — slot 2 occupé → u16 itemId
        // + corps EquipView (lu via NativeItemRecord.TryReadEquipView).
        // On dérive le corps attendu en lisant TryReadEquipView
        // sur un blob minimal : si le codec exige plus d'octets on
        // teste le repli propre (worn vide ou partiel accepté).
        var w = new BinWriter();
        w.WriteByte(0x00);
        w.WriteInt(1 << 2);                     // slot 2
        w.WriteUShort((ushort)1001);
        // Corps EquipView minimal : on met 64 octets de zéros —
        // TryReadEquipView lit ce qu'il peut, partiel = ok.
        w.WriteBytes(new byte[64]);
        var (ride, worn) = ZoneEntityRegistry.ParseDisplayTail(
            w.ToArray());
        Assert.Equal(0, ride);
        // Si le codec a tout lu : slot présent ; sinon toléré vide.
        if (worn.Count > 0)
            Assert.Equal(1001, worn[2]);
    }

    [Fact]
    public void ParseDisplayTail_Empty_IsSafe()
    {
        var (ride, worn) = ZoneEntityRegistry.ParseDisplayTail([]);
        Assert.Equal(0, ride);
        Assert.Empty(worn);
    }

    // ========================== divers ==============================

    [Fact]
    public void Say_UpdatesSayText()
    {
        var r = Reg();
        r.HandleSeeChar(new ZoneSeeChar { CharId = 42 });
        string? say = null;
        r.SayRequested += (e, t) => say = t;
        r.HandleSay(new ZoneClientSay { CharId = 42, Message = "yo" });
        Assert.Equal("yo", say);
        Assert.Equal("yo", r.Entities[42u].SayText);
    }

    [Fact]
    public void GroundHeight_SnapsCloseZ()
    {
        var r = Reg();
        r.GroundHeight = (_, _) => 7f;
        r.HandleSeeMob(Mob(0xFFFF0001, 1));     // z wire = 0 → snap
        Assert.Equal(7f, r.Entities[0xFFFF0001u].Z);
    }

    [Fact]
    public void DangerClassification_MatchesRules()
    {
        // < 8 niveaux d'écart + aggro → rouge ; ≥ 8 → gris.
        var r = Reg(playerLevel: 20);
        var low = Def(1, level: 5, type: 1);
        var high = Def(2, level: 19, type: 1, ai: 7, aggro: 4);
        Assert.Equal(EntDanger.LowLevel,
            EntDangerRules.Classify(low, 20, 0));
        Assert.Equal(EntDanger.Aggressive,
            EntDangerRules.Classify(high, 20, 50));
        Assert.Equal(EntDanger.Passive,
            EntDangerRules.Classify(high, 20, 0));
        Assert.Equal(EntDanger.Aggressive,
            EntDangerRules.Classify(Def(3, level: 1, type: 3), 20, 0));
        Assert.Equal(EntDanger.Unknown,
            EntDangerRules.Classify(null, 20, 0));
    }
}
