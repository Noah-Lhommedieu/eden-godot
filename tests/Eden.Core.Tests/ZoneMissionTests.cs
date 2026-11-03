using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lot S3+S5 : codecs mission (51 GiveUp, 87 ops),
/// marqueurs NPC (345), fiche quête (388 ops) et décodage des
/// objectifs PMS (clés mid×1e6+type×1e4+obj — formule native
/// QuestEngine.Objectives).
/// </summary>
public class ZoneMissionTests
{
    private static byte[] Hex(string h) =>
        Convert.FromHexString(h.Replace(" ", ""));

    private static byte[] Enc(NetMessage m)
    {
        var w = new BinWriter();
        m.Serialize(w);
        return w.ToArray();
    }

    // ---------- 51 GiveUpMission ----------

    [Fact]
    public void GiveUpMission_Wire()
    {
        // [s16 mid] — TeamHandlers.cs:118 serveur.
        Assert.Equal(Hex("f401"),
            Enc(new ZoneGiveUpMission { MissionId = 500 }));
    }

    // ---------- 87 MissionListOp ops ----------

    [Fact]
    public void MissionListOp_AcceptWire()
    {
        // Capture native « 0200 f4010000 ffff » = op 2, mid 500,
        // npcUid 0xffff — reproduite octet par octet.
        var w = new BinWriter();
        new ZoneMissionListOp { Field1 = 2, Field2 = 500, Field3 = 0xFFFF }
            .Serialize(w);
        Assert.Equal(Hex("0200 f4010000 ffff"), w.ToArray());
    }

    [Fact]
    public void MissionListOps_Constantes()
    {
        Assert.Equal((short)1, MissionListOps.Accept);
        Assert.Equal((short)2, MissionListOps.Reward);
        Assert.Equal((short)3, MissionListOps.OnGoing);
        Assert.Equal((short)4, MissionListOps.Finish);
        Assert.Equal((short)9, MissionListOps.PickAvailable);
        Assert.Equal((short)1, MissionSheetOps.Open);
        Assert.Equal((short)2, MissionSheetOps.Reward);
        Assert.Equal((short)4, MissionSheetOps.Finish);
    }

    // ---------- 345 UpdateNPCSymbols ----------

    [Fact]
    public void UpdateNpcSymbols_Parse()
    {
        // [s16 n=2][i32 50003][s16 1][s16 0][i32 50004][s16 2][s16 0]
        // (passthru ZoneEntryFlow.Mission.cs:215 — 2e s16 ajouté = 0).
        var m = new ZoneUpdateNpcSymbols();
        m.Deserialize(new BinReader(Hex(
            "0200 53c30000 0100 0000 54c30000 0200 0000")));
        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(50003, m.Entries[0].NpcUid);
        Assert.Equal((short)1, m.Entries[0].Sym1);
        Assert.Equal((short)0, m.Entries[0].Sym2);
        Assert.Equal(50004, m.Entries[1].NpcUid);
        Assert.Equal((short)2, m.Entries[1].Sym1);
    }

    [Fact]
    public void UpdateNpcSymbols_RoundTrip()
    {
        var m = new ZoneUpdateNpcSymbols();
        m.Entries.Add(new ZoneUpdateNpcSymbols.NpcSymbol(50003, 1, 0));
        m.Entries.Add(new ZoneUpdateNpcSymbols.NpcSymbol(50010, 3, 0));
        var clone = new ZoneUpdateNpcSymbols();
        clone.Deserialize(new BinReader(Enc(m)));
        Assert.Equal(m.Entries, clone.Entries);
    }

    // ---------- 388 EnterMissionList (déjà codé — sémantique ops) --

    [Fact]
    public void EnterMissionList_OfferWire()
    {
        // Capture « 0200 f4030000 54c30000 0000 » : op=2 reward,
        // arg=0x3f4, npcUid 50004 (Mission.Commands.cs:234).
        var m = new ZoneEnterMissionList();
        m.Deserialize(new BinReader(Hex("0200 f4030000 54c30000 0000")));
        Assert.Equal((short)2, m.Op);
        Assert.Equal(0x3f4, m.Arg);
        Assert.Equal(50004, m.NpcUid);
    }

    // ---------- 391 kind=0/kind=1 (sémantique des champs) ----------

    [Fact]
    public void UpdateMissionState_Kind0_Etat()
    {
        // [u8 0][i32 mid][s16 fms][s16 progress] — SendStateAsync.
        var m = new ZoneUpdateMissionState();
        m.Deserialize(new BinReader(Hex("00 f4010000 0300 0000")));
        Assert.Equal(0, m.Op);
        Assert.Equal(500u, m.Key);            // mid
        Assert.Equal(3, m.MissionId);         // FMS 3 = en cours
        Assert.Equal(0, m.State);             // progress
    }

    [Fact]
    public void UpdateMissionState_Kind1_Pms()
    {
        // [u8 1][u32 pmsKey][u16 count][s16 0] — SendPmsAsync.
        var m = new ZoneUpdateMissionState();
        var pmsKey = 500u * 1_000_000u + 10_000u + 3u;
        var w = new BinWriter();
        w.WriteByte(1); w.WriteUInt(pmsKey);
        w.WriteUShort(2); w.WriteShort(0);
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(1, m.Op);
        Assert.Equal(pmsKey, m.Key);
        Assert.Equal(2, m.MissionId);         // count
    }

    // ---------- Clés PMS (formule native) ----------

    [Fact]
    public void DecodePmsKey_Kill()
    {
        // MonsterKilled mob 55003 → key = mid×1e6 + 10000 + (55003−55000).
        var key = 500u * 1_000_000u + 10_000u + 3u;
        var (mid, kind, objId) = MissionObjectives.DecodePmsKey(key);
        Assert.Equal(500, mid);
        Assert.Equal(MissionObjectives.Kind.Kill, kind);
        Assert.Equal(55003, objId);
    }

    [Fact]
    public void DecodePmsKey_NpcTalk()
    {
        // NPCTalk npc 50002 → key = mid×1e6 + 50000 + (50002−50000).
        var key = 1010u * 1_000_000u + 50_000u + 2u;
        var (mid, kind, objId) = MissionObjectives.DecodePmsKey(key);
        Assert.Equal(1010, mid);
        Assert.Equal(MissionObjectives.Kind.NpcTalk, kind);
        Assert.Equal(50002, objId);
    }

    [Fact]
    public void DecodePmsKey_AreaTravel()
    {
        // AreaTravel aire node×100+key — GetAreaTravelPMS :
        // (area%100 − 0x32) + (area/100)×10 ; aire 2*100+51=251 →
        // (51−50)+(2)×10 = 21.
        var key = 1200u * 1_000_000u + 70_000u + 21u;
        var (mid, kind, objId) = MissionObjectives.DecodePmsKey(key);
        Assert.Equal(1200, mid);
        Assert.Equal(MissionObjectives.Kind.AreaTravel, kind);
        Assert.Equal(251, objId);
    }

    [Fact]
    public void DecodePmsKey_Invalide()
    {
        var (mid, kind, _) = MissionObjectives.DecodePmsKey(0);
        Assert.Equal(0, mid);
        Assert.Equal(MissionObjectives.Kind.Unknown, kind);
    }

    // ---------- Table c_mission (si raw_assets dispo) ----------

    [Fact]
    public void MissionTable_L01_DonneurAndrew()
    {
        // c_mission : 1009 = « L01. Awakening I », étapes 10091/10092
        // (cols 14/19), NPC de remise 50003 (col 17 → dialogue 4003).
        var m = MissionTable.Get(1009);
        if (m == null) return;      // table absente (env CI)
        Assert.Equal(10091, m.GiverStepId);
        Assert.Equal(10092, m.SubmitStepId);
        Assert.Equal(50003, m.SubmitNpc);
        Assert.NotEmpty(m.Actions);
    }

    [Fact]
    public void MissionObjectives_L01_PmsKeys()
    {
        var objs = MissionObjectives.ForMission(1009);
        if (objs.Count == 0) return; // table absente
        // Toutes les clés portent le préfixe mid×1e6.
        Assert.All(objs, o => Assert.Equal(1009,
            (int)(o.PmsKey / 1_000_000u)));
    }
}
