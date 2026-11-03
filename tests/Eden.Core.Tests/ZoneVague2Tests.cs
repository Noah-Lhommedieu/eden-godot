using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lot Vague 2 — enchants/statuts (299/300/301/315/372/
/// 445/446 + C→S 18/85/127/128), chemin d'éveil + fortune
/// (534/535/589/590/557 + C→S 203/204/223) et couple/mariage
/// (475-479/597/599 + C→S 154-157/249/251).
///
/// Corps prouvés côté serveur : ZoneEntryFlow.Status.cs /
/// DisplayChar.cs / Destiny.cs (désasm @0x81e9840, @0x81e98d0,
/// @0x822fb70, DestinyRoadAction @0x810d610, SDestinyRoad::Serialize
/// @0x863b750), WeddingPackets.cs (boucle 599 relue byte-à-byte) et
/// ZoneMessages.g.cs — voir les en-têtes des fichiers codecs.
/// </summary>
[Xunit.Collection("SharedCoreState")]
public class ZoneVague2Tests
{
    private static byte[] RoundTrip(NetMessage m)
    {
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        return w.ToArray();
    }

    private static T Parse<T>(byte[] payload) where T : NetMessage, new()
    {
        var m = new T();
        var r = new BinReader(payload);
        r.ReadUShort(); // NCID
        m.Deserialize(r);
        return m;
    }

    // ============================ enchants =========================

    [Fact]
    public void EnchantAdd_Body23Bytes_RoundTrip()
    {
        // [i32 handle][s16 effect][i32 rest][u8 serial][s16 stack]
        // [i32 flags][s16 level][i32 counter2] — 4+2+4+1+2+4+2+4 = 23.
        var m = new ZoneEnchantAdd
        {
            Handle = 200000042, EffectId = 10005, RestSeconds = 3600,
            Serial = 7, StackCount = 3, FlagBits = 0, Level = 11,
            Counter2 = 0,
        };
        var bytes = RoundTrip(m);
        Assert.Equal(2 + 23, bytes.Length);
        Assert.Equal(0x2B, bytes[0]);           // 299 = 0x012B
        Assert.Equal(0x01, bytes[1]);
        var p = Parse<ZoneEnchantAdd>(bytes);
        Assert.Equal(200000042, p.Handle);
        Assert.Equal(10005, p.EffectId);
        Assert.Equal(3600, p.RestSeconds);
        Assert.Equal(3, p.StackCount);
        Assert.Equal(11, p.Level);
    }

    [Fact]
    public void EnchantRemove_Body15Bytes_RoundTrip()
    {
        // [i32][s16][u8][i32][i32] — 15 (Serialize natif @0x81e98d0).
        var m = new ZoneEnchantRemove
        { Handle = 200000042, EffectId = 10005, Act = 1 };
        var bytes = RoundTrip(m);
        Assert.Equal(2 + 15, bytes.Length);
        var p = Parse<ZoneEnchantRemove>(bytes);
        Assert.Equal(10005, p.EffectId);
        Assert.Equal(1, p.Act);
    }

    [Fact]
    public void EnchantUpdate_SnapshotEntries_RoundTrip()
    {
        // [i32 handle][u8 n][n×14B][i32 flags][u8 rev].
        var m = new ZoneEnchantUpdate { Handle = 200000042, Flags = 2,
            Revision = 9 };
        m.Entries.Add(new ZoneEnchantUpdate.Entry
        {
            EffectId = 10005, CasterId = 200000042, RestSeconds = 300,
            Level = 4,
        });
        m.Entries.Add(new ZoneEnchantUpdate.Entry
        {
            EffectId = 10017, CasterId = 200000100, RestSeconds = -1,
            Level = 1, F4 = 0, Note = 0,
        });
        var bytes = RoundTrip(m);
        var p = Parse<ZoneEnchantUpdate>(bytes);
        Assert.Equal(200000042, p.Handle);
        Assert.Equal(2, p.Entries.Count);
        Assert.Equal(10017, p.Entries[1].EffectId);
        Assert.Equal(-1, p.Entries[1].RestSeconds);
        Assert.Equal(2, p.Flags);
        Assert.Equal(9, p.Revision);
    }

    [Fact]
    public void EnchantReset_ParsesHandle()
    {
        var w = new BinWriter();
        w.WriteUShort(315);
        w.WriteShort(0);
        w.WriteInt(200000042);
        var p = Parse<ZoneEnchantReset>(w.ToArray());
        Assert.Equal(200000042, p.Handle);
    }

    [Fact]
    public void TeamMemberEnchant_LoopBody_RoundTrip()
    {
        // [s16 serial][i32 member][u8 n][n×(s16 s16 s16 i32)].
        var m = new ZoneTeamMemberEnchant
        { Serial = 3, MemberCharId = 200000100 };
        m.Entries.Add(new ZoneTeamMemberEnchant.Entry
        { EffectId = 10005, Counter = 0, Level = 2, RestSeconds = 600 });
        m.Entries.Add(new ZoneTeamMemberEnchant.Entry
        { EffectId = 10017, Counter = 1, Level = 5, RestSeconds = 30 });
        var p = Parse<ZoneTeamMemberEnchant>(RoundTrip(m));
        Assert.Equal(200000100, p.MemberCharId);
        Assert.Equal(2, p.Entries.Count);
        Assert.Equal(10017, p.Entries[1].EffectId);
        Assert.Equal(30, p.Entries[1].RestSeconds);
    }

    [Fact]
    public void EnchantPause_Start_RoundTrip()
    {
        var m = new ZoneEnchantPause
        { Handle = 42, EffectId = 10005, Flag = 1, Field4 = 999 };
        var p = Parse<ZoneEnchantPause>(RoundTrip(m));
        Assert.Equal(999, p.Field4);
        var s = new ZoneEnchantStart
        { Handle = 42, EffectId = 10005 };
        var sp = Parse<ZoneEnchantStart>(RoundTrip(s));
        Assert.Equal(10005, sp.EffectId);
    }

    [Fact]
    public void EnchantRequests_ClientToServer_Serialize()
    {
        // 18 [u32], 85/127/128 [s16].
        var w = new BinWriter();
        new ZoneEnchantUpdateRequest { TargetHandle = 0xFFFFA001 }
            .Serialize(w);
        var b = w.ToArray();
        Assert.Equal(4, b.Length);
        w = new BinWriter();
        new ZoneEnchantCancel { EffectId = 10005 }.Serialize(w);
        Assert.Equal(2, w.ToArray().Length);
        w = new BinWriter();
        new ZoneEnchantPauseRequest { EffectId = 7 }.Serialize(w);
        Assert.Equal(2, w.ToArray().Length);
        w = new BinWriter();
        new ZoneEnchantStartRequest { EffectId = 7 }.Serialize(w);
        Assert.Equal(2, w.ToArray().Length);
    }

    [Fact]
    public void EntityBuffs_AddRemoveUpdate_Countdown()
    {
        EntityBuffs.ClearAll();
        var h = 777001;
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        { Handle = h, EffectId = 100, RestSeconds = 60, StackCount = 2 });
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        { Handle = h, EffectId = 200, RestSeconds = -1 });
        var snap = EntityBuffs.Snapshot(h);
        Assert.Equal(2, snap.Count);
        // Remplacement par même effectId → pas de doublon.
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        { Handle = h, EffectId = 100, RestSeconds = 30 });
        snap = EntityBuffs.Snapshot(h);
        Assert.Equal(2, snap.Count);
        Assert.True(snap.First(b => b.EffectId == 100).Remaining <= 30);
        // Permanent → −1.
        Assert.Equal(-1,
            snap.First(b => b.EffectId == 200).Remaining);
        EntityBuffs.ApplyRemove(new ZoneEnchantRemove
        { Handle = h, EffectId = 200 });
        Assert.Single(EntityBuffs.Snapshot(h));
        // 301 remplace tout.
        var up = new ZoneEnchantUpdate { Handle = h };
        up.Entries.Add(new ZoneEnchantUpdate.Entry
        { EffectId = 300, RestSeconds = 5 });
        EntityBuffs.ApplyUpdate(up);
        snap = EntityBuffs.Snapshot(h);
        Assert.Single(snap);
        Assert.Equal(300, snap[0].EffectId);
        // Pause → décompte figé.
        EntityBuffs.ApplyPause(h, 300, true);
        var rem = EntityBuffs.Snapshot(h)[0].Remaining;
        System.Threading.Thread.Sleep(30);
        Assert.Equal(rem, EntityBuffs.Snapshot(h)[0].Remaining);
        EntityBuffs.ClearAll();
    }

    // ============================ destin ===========================

    [Fact]
    public void PlayerDestinyRoad_39Slots_RoundTrip()
    {
        // [s16×4][i32][s16×2][u8][u16 n][n×s16] — 39 cases.
        var m = new ZonePlayerDestinyRoad
        {
            RoadId = 4, RoadType = 1, Step = 7, RollsLeft = 3,
            DpPool = 8400, MovePoints = 4, FunctionState = 0x2a,
            RerollFlag = 0,
        };
        for (var i = 0; i < 39; i++) m.Slots.Add((short)(i + 1));
        var bytes = RoundTrip(m);
        var p = Parse<ZonePlayerDestinyRoad>(bytes);
        Assert.Equal(4, p.RoadId);
        Assert.Equal(7, p.Step);
        Assert.Equal(8400, p.DpPool);
        Assert.Equal(39, p.Slots.Count);
        Assert.Equal(39, p.Slots[38]);
    }

    [Fact]
    public void PlayerDestinyRoad_ZeroBlock_ResetsState()
    {
        var st = new DestinyState();
        var road = new ZonePlayerDestinyRoad { RoadId = 4 };
        for (var i = 0; i < 39; i++) road.Slots.Add(1);
        st.Apply(road);
        Assert.NotNull(st.Current);
        // Bloc tout-à-zéro = fin de route (serveur : reset post-op4
        // dernière case).
        var zero = new ZonePlayerDestinyRoad { RoadId = 0 };
        for (var i = 0; i < 39; i++) zero.Slots.Add(0);
        st.Apply(zero);
        Assert.Null(st.Current);
    }

    [Fact]
    public void UpdateDestinyRoad_AtomicFields()
    {
        var st = new DestinyState();
        var road = new ZonePlayerDestinyRoad
        { RoadId = 1, Step = 0, RollsLeft = 5, DpPool = 100 };
        for (var i = 0; i < 39; i++) road.Slots.Add(10);
        st.Apply(road);
        st.Apply(new ZoneUpdateDestinyRoad { Type = 0, V1 = 12 });
        Assert.Equal(12, st.Current!.Step);
        st.Apply(new ZoneUpdateDestinyRoad { Type = 3, V1 = 6 });
        Assert.Equal(6, st.Current!.MovePoints);
        st.Apply(new ZoneUpdateDestinyRoad
        { Type = 5, V1 = 99, V2 = 12 });
        Assert.Equal(99, st.Current!.Slots[12]);
        st.Apply(new ZoneUpdateDestinyRoad { Type = 6, V1 = 1 });
        Assert.True(st.Current!.Reroll);
        // Type 7 = vitalité hors-route.
        st.Apply(new ZoneUpdateDestinyRoad { Type = 7, V1 = 620 });
        Assert.Equal(620, st.DestinyVit);
        // 589 avant toute route → ignoré sauf type 7.
        var st2 = new DestinyState();
        st2.Apply(new ZoneUpdateDestinyRoad { Type = 0, V1 = 5 });
        Assert.Null(st2.Current);
    }

    [Fact]
    public void DestinyRoadEvent_AndFailure_RoundTrip()
    {
        var ev = Parse<ZonePlayerDestinyRoadEvent>(
            RoundTrip(new ZonePlayerDestinyRoadEvent { EventId = 42 }));
        Assert.Equal(42, ev.EventId);
        var fail = Parse<ZoneDestinyDiceFailed>(
            RoundTrip(new ZoneDestinyDiceFailed { Op = 4 }));
        Assert.Equal(4, fail.Op);
        var st = new DestinyState();
        short failed = -1;
        int eventId = -1;
        st.OpFailed += op => failed = op;
        st.EventReceived += id => eventId = id;
        st.Apply(fail);
        st.Apply(ev);
        Assert.Equal(4, failed);
        Assert.Equal(42, eventId);
    }

    [Fact]
    public void FortuneBagCounter_List_RoundTrip()
    {
        var m = new ZoneUpdateFortuneBagCounter();
        m.Entries.Add((1001, 2));
        m.Entries.Add((2045, 7));
        var p = Parse<ZoneUpdateFortuneBagCounter>(RoundTrip(m));
        Assert.Equal(2, p.Entries.Count);
        Assert.Equal((1001, 2), p.Entries[0]);
        Assert.Equal((2045, 7), p.Entries[1]);
        var st = new DestinyState();
        st.Apply(p);
        Assert.Equal(7, st.FortuneBags[2045]);
    }

    [Fact]
    public void DestinyDice_Ops_Serialize()
    {
        // 203 [s16 op][s16 p2][s16 p3] — op4 avance, op3 p2=fonction.
        var w = new BinWriter();
        new ZoneDestinyDice { Op = 3, P2 = 2 }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(6, b.Length);
        var r = new BinReader(b);
        Assert.Equal(3, r.ReadShort());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        // 204/223 corps vide.
        w = new BinWriter();
        new ZoneQueryDestinyRoad().Serialize(w);
        Assert.Empty(w.ToArray());
        w = new BinWriter();
        new ZoneQueryFortuneBagCounter().Serialize(w);
        Assert.Empty(w.ToArray());
    }

    // ============================ couple ===========================

    [Fact]
    public void LoverInvite_RoundTrip()
    {
        // 475 [i32 charId][str name][s16].
        var m = new ZoneLoverInvite
        { CharId = 200000010, PeerName = "Admin", Field3 = 0 };
        var p = Parse<ZoneLoverInvite>(RoundTrip(m));
        Assert.Equal(200000010, p.CharId);
        Assert.Equal("Admin", p.PeerName);
    }

    [Fact]
    public void LoverInfo_FourteenFields_RoundTrip()
    {
        var m = new ZoneLoverInfo
        {
            Field1 = 200000042, Field2 = 7, Field3 = 12,
            Field4 = "Rosemary", Field5 = "label", Field6 = 1700000,
            Field7 = 5200, Field8 = 1, Field9 = 2, Field10 = 3,
            Field11 = 4, Field12 = "hi", Field13 = 5, Field14 = 1,
        };
        var p = Parse<ZoneLoverInfo>(RoundTrip(m));
        Assert.Equal(200000042, p.Field1);
        Assert.Equal(7, p.Field2);
        Assert.Equal("Rosemary", p.Field4);
        Assert.Equal("hi", p.Field12);
        Assert.Equal(1, p.Field14);
    }

    [Fact]
    public void LoverBreak_Position_MarryOp_RoundTrip()
    {
        var brk = Parse<ZoneLoverBreak>(
            RoundTrip(new ZoneLoverBreak { Value = 7 }));
        Assert.Equal(7, brk.Value);
        var pos = Parse<ZoneLoverPosition>(RoundTrip(new ZoneLoverPosition
        { CharId = 42, SceneId = 2, X = 100.5f, Y = -30.25f }));
        Assert.Equal(2, pos.SceneId);
        Assert.Equal(100.5f, pos.X);
        Assert.Equal(-30.25f, pos.Y);
        var op = Parse<ZoneMarryOperation>(
            RoundTrip(new ZoneMarryOperation { Value = 1 }));
        Assert.Equal(1, op.Value);
    }

    [Fact]
    public void WeddingsInfo_LoopBody_RoundTrip()
    {
        // 599 [u8 n][n×(i32][str][str][u8][i32)] — boucle serveur
        // prouvée WeddingPackets.cs.
        var m = new ZoneWeddingsInfo();
        m.Entries.Add(new ZoneWeddingsInfo.Entry
        {
            LoveId = 7, Name1 = "test1", Name2 = "Rosemary",
            Status = 2, LeftTime = 1830,
        });
        m.Entries.Add(new ZoneWeddingsInfo.Entry
        {
            LoveId = 9, Name1 = "a", Name2 = "b",
            Status = 0, LeftTime = 0,
        });
        var p = Parse<ZoneWeddingsInfo>(RoundTrip(m));
        Assert.Equal(2, p.Entries.Count);
        Assert.Equal(7, p.Entries[0].LoveId);
        Assert.Equal("Rosemary", p.Entries[0].Name2);
        Assert.Equal(1830, p.Entries[0].LeftTime);
        // Repli un-octet serveur (mission absente → n=0).
        var w = new BinWriter();
        w.WriteUShort(599);
        w.WriteByte(0);
        Assert.Empty(Parse<ZoneWeddingsInfo>(w.ToArray()).Entries);
    }

    [Fact]
    public void LoverRequests_ClientToServer_Serialize()
    {
        // 154 [u32][str][s16], 155 [str], 156/157 vide,
        // 249 [s16][s16], 251 vide.
        var w = new BinWriter();
        new ZoneLoverInviteRequest
        { CharId = 200000010, PeerName = "Admin" }.Serialize(w);
        var r = new BinReader(w.ToArray());
        Assert.Equal(200000010u, r.ReadUInt());
        Assert.Equal("Admin", r.ReadString());
        Assert.Equal(0, r.ReadShort());
        w = new BinWriter();
        // Big5 wire : ASCII conservé (le « ♥ » sortirait en « ? »).
        new ZoneLoverMessage { Value = "see you" }.Serialize(w);
        Assert.Equal("see you",
            new BinReader(w.ToArray()).ReadString());
        w = new BinWriter();
        new ZoneQueryLoverInfo().Serialize(w);
        Assert.Empty(w.ToArray());
        w = new BinWriter();
        new ZoneLoverBreakRequest().Serialize(w);
        Assert.Empty(w.ToArray());
        w = new BinWriter();
        new ZoneMarryOpRequest { Op = 1 }.Serialize(w);
        Assert.Equal(4, w.ToArray().Length);
        w = new BinWriter();
        new ZoneWeddingInfoQuery().Serialize(w);
        Assert.Empty(w.ToArray());
    }

    [Fact]
    public void LoverState_InviteInfoBreak_Lifecycle()
    {
        var st = new LoverState();
        ZoneLoverInvite? invite = null;
        st.InviteReceived += m => invite = m;
        st.Apply(new ZoneLoverInvite
        { CharId = 1, PeerName = "x" });
        Assert.NotNull(invite);
        st.Apply(new ZoneLoverInfo
        { Field1 = 200000042, Field2 = 7, Field4 = "Rosemary",
            Field14 = 0 });
        Assert.Equal(200000042, st.PartnerCharId);
        Assert.Equal("Rosemary", st.PartnerName);
        Assert.False(st.Married);
        // 597 op1 succès → marié.
        st.Apply(new ZoneMarryOperation { Value = 1 });
        Assert.True(st.Married);
        // 478 → célibataire.
        st.Apply(new ZoneLoverBreak { Value = 7 });
        Assert.Equal(0, st.PartnerCharId);
        Assert.False(st.Married);
    }
}
