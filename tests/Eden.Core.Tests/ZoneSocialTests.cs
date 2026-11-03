using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages sociaux (lot S9) — party zone (28/29/30/31 C→S,
/// 317/318/319/324/325/381/392 S→C) et amis/guilde monde (12/14/22 C→S,
/// 50/52/53/61 S→C). Formats d'après les désasm natifs cités dans
/// <c>ZoneSocialMessages.cs</c> (zone @0x8290780/0x82922d0/0x8290fe0,
/// SocialPackets/FamilyPackets désasm world).
/// </summary>
public class ZoneSocialTests
{
    private static T RoundTrip<T>(T msg) where T : NetMessage, new()
    {
        var w = new BinWriter();
        msg.Serialize(w);
        var back = new T();
        back.Deserialize(new BinReader(w.ToArray()));
        return back;
    }

    // ---------- Zone C→S ----------

    [Fact]
    public void TeamLeave_Wire()
    {
        var w = new BinWriter();
        new ZoneTeamLeave { CharId = 200000002 }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(4, b.Length);
        Assert.Equal(200000002, BitConverter.ToInt32(b));
    }

    [Fact]
    public void TeamInvite_Wire()
    {
        // [u32][str][u8][u8][s16] — désasm Deserialize @0x8290780.
        var w = new BinWriter();
        new ZoneTeamInvite
        {
            CharId = 42, PlayerName = "abc", Level = 12,
            ClassId = 3, Flags = 7,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(4 + 2 + 3 + 1 + 1 + 2, b.Length);
        var back = new ZoneTeamInvite();
        back.Deserialize(new BinReader(b));
        Assert.Equal(42u, back.CharId);
        Assert.Equal("abc", back.PlayerName);
        Assert.Equal(12, back.Level);
        Assert.Equal(3, back.ClassId);
        Assert.Equal(7, back.Flags);
    }

    [Fact]
    public void TeamInviteAnswer_Accept_Wire()
    {
        // Accept : [s16 type=1][i32 inviter][u8 1][s16 0].
        var w = new BinWriter();
        new ZoneTeamInviteAnswer
        {
            ReplyType = 1, InviterId = 200000001, Accepted = 1,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(9, b.Length);
        Assert.Equal(1, BitConverter.ToInt16(b));
        Assert.Equal(200000001, BitConverter.ToInt32(b, 2));
        Assert.Equal(1, b[6]);
        var back = new ZoneTeamInviteAnswer();
        back.Deserialize(new BinReader(b));
        Assert.Equal(1, back.ReplyType);
        Assert.Equal(200000001, back.InviterId);
        Assert.Equal(1, back.Accepted);
    }

    [Fact]
    public void TeamInviteCancel_Empty()
    {
        var w = new BinWriter();
        new ZoneTeamInviteCancel().Serialize(w);
        Assert.Empty(w.ToArray());
    }

    // ---------- Zone S→C ----------

    [Fact]
    public void TeamInviteNotice_Parse()
    {
        var w = new BinWriter();
        w.WriteInt(200000001);          // i32 inviteur
        w.WriteString("inviter");       // str nom
        w.WriteShort(1); w.WriteShort(2);
        w.WriteShort(3); w.WriteShort(4);
        var m = new ZoneTeamInviteNotice();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000001, m.InviterId);
        Assert.Equal("inviter", m.InviterName);
    }

    [Fact]
    public void TeamAllMembersInfo_Parse()
    {
        // Corps façon BroadcastTeamInfo serveur : 2 membres + mode 1.
        var w = new BinWriter();
        w.WriteShort(2);
        foreach (var (slot, cid, name, lv, hp, flags, online) in
                 new[] { ((short)0, 200000001u, "lead", 10, 500, (short)1, (short)1),
                         ((short)1, 200000002u, "mate", 12, 800, (short)0, (short)1) })
        {
            w.WriteShort(slot);
            w.WriteShort(flags);
            w.WriteInt((int)cid);
            w.WriteString(name);
            w.WriteLong(0); w.WriteLong(0);
            w.WriteInt(lv); w.WriteInt(hp);
            w.WriteShort(0);            // M1
            w.WriteShort(2);            // classId
            w.WriteShort(2);            // mapId
            w.WriteShort(0);            // U1
            w.WriteShort(online);       // online
            w.WriteString("GuildX");
        }
        w.WriteShort(1);                // teamMode
        var m = new ZoneTeamAllMembersInfo();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(2, m.Members.Count);
        Assert.Equal(200000001u, m.Members[0].CharId);
        Assert.Equal("lead", m.Members[0].CharName);
        Assert.Equal(1, m.Members[0].Flags & 1);   // leader
        Assert.Equal(800, m.Members[1].Hp);
        Assert.Equal("GuildX", m.Members[1].GuildName);
        Assert.Equal(1, m.TeamMode);
    }

    [Fact]
    public void TeamMemberPosition_Parse()
    {
        var w = new BinWriter();
        w.WriteInt(200000002);
        w.WriteShort(2);
        w.WriteFloat(179.5f);
        w.WriteFloat(284.25f);
        var m = new ZoneTeamMemberPosition();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000002, m.CharId);
        Assert.Equal(2, m.NodeId);
        Assert.Equal(179.5f, m.X);
        Assert.Equal(284.25f, m.Y);
    }

    // ---------- PartyState ----------

    [Fact]
    public void PartyState_ApplyAll_And_Leave()
    {
        PartyState.Clear();
        var w = new BinWriter();
        w.WriteShort(1);
        w.WriteShort(0); w.WriteShort(1);          // slot, flags=leader
        w.WriteInt(200000001); w.WriteString("me");
        w.WriteLong(0); w.WriteLong(0);
        w.WriteInt(5); w.WriteInt(300);
        w.WriteShort(0); w.WriteShort(1); w.WriteShort(2);
        w.WriteShort(0); w.WriteShort(1);
        w.WriteString("G");
        w.WriteShort(1);
        var m = new ZoneTeamAllMembersInfo();
        m.Deserialize(new BinReader(w.ToArray()));
        PartyState.ApplyAll(m);
        Assert.True(PartyState.InTeam);
        Assert.Equal(200000001u, PartyState.LeaderId);
        Assert.Equal(300, PartyState.MaxHpSeen[200000001u]);
        PartyState.ApplyLeaved();
        Assert.False(PartyState.InTeam);
    }

    // ---------- Monde ----------

    [Fact]
    public void WorldAddFriend_Wire()
    {
        var w = new BinWriter();
        new WorldAddFriend { TargetName = "bob", FriendType = 1 }
            .Serialize(w);
        var b = w.ToArray();
        Assert.Equal(2 + 3 + 2, b.Length);
        var back = new WorldAddFriend();
        back.Deserialize(new BinReader(b));
        Assert.Equal("bob", back.TargetName);
        Assert.Equal(1, back.FriendType);
    }

    [Fact]
    public void WorldUpdateFriendMember_Parse()
    {
        // Corps façon SocialPackets.BuildFriendMemberPackets :
        // [u16 worldId][s16 n][n×rec].
        var w = new BinWriter();
        w.WriteShort(1);                // worldServerId
        w.WriteShort(1);                // 1 record
        w.WriteInt(200000002);
        w.WriteString("alice");
        w.WriteShort(3);                // friend_type mutuel
        w.WriteShort(20);               // level
        w.WriteShort(2);                // node
        w.WriteShort(0);                // race
        w.WriteShort(2);                // class
        w.WriteShort(5);                // class_lv
        w.WriteString("GuildA");
        w.WriteByte(1);                 // online
        w.WriteShort(1);                // serverId
        w.WriteByte(0);                 // state
        w.WriteByte(1);                 // is_public
        var m = new WorldUpdateFriendMember();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Single(m.Entries);
        var e = m.Entries[0];
        Assert.Equal(200000002, e.CharId);
        Assert.Equal("alice", e.Name);
        Assert.Equal(3, e.FriendType);
        Assert.Equal("GuildA", e.FamilyName);
        Assert.Equal(1, e.Online);
    }

    [Fact]
    public void WorldFamilyInfo_GuildName()
    {
        // type 0 : [u8 0][u16 world][i32 famId][i32 time][str name]…
        var w = new BinWriter();
        w.WriteByte(0);
        w.WriteShort(1);
        w.WriteInt(7);
        w.WriteInt(123456);
        w.WriteString("MyGuild");
        w.WriteByte(3);                 // lv
        var m = new WorldFamilyInfo();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(7, m.FamilyId);
        Assert.Equal("MyGuild", m.FamilyName);
        Assert.Equal(3, m.Level);

        // type 2 = pas de famille.
        var w2 = new BinWriter();
        w2.WriteByte(2);
        var m2 = new WorldFamilyInfo();
        m2.Deserialize(new BinReader(w2.ToArray()));
        Assert.Equal("", m2.FamilyName);
    }

    [Fact]
    public void WorldBlackList_Parse()
    {
        var w = new BinWriter();
        w.WriteByte(1);
        w.WriteShort(1);
        w.WriteInt(99);
        w.WriteString("bad");
        var m = new WorldUpdateBlackList();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Single(m.Entries);
        Assert.Equal((99, "bad"), m.Entries[0]);
    }

    // ---------- Guilde monde C→S (vague 1-A) ----------

    [Fact]
    public void WorldFamilyOp_Invite_Wire()
    {
        // NCID 9 [u8 op][i32 a][i32 b][str s1][str s2] — op 0x01
        // invite par nom (FamilyHandlers.cs:77-99).
        var w = new BinWriter();
        WorldFamilyOp.OfStr(FamilyOps.Invite, "bob").Serialize(w);
        var b = w.ToArray();
        Assert.Equal(0x01, b[0]);
        Assert.Equal(1 + 4 + 4 + 2 + 3 + 2, b.Length);
        var back = new WorldFamilyOp();
        back.Deserialize(new BinReader(b));
        Assert.Equal(FamilyOps.Invite, back.Op);
        Assert.Equal("bob", back.S1);
    }

    [Fact]
    public void WorldFamilyOp_Kick_Wire()
    {
        var w = new BinWriter();
        WorldFamilyOp.Of(FamilyOps.Kick, 200000002).Serialize(w);
        var b = w.ToArray();
        Assert.Equal(FamilyOps.Kick, b[0]);
        Assert.Equal(200000002, BitConverter.ToInt32(b, 1));
        var back = new WorldFamilyOp();
        back.Deserialize(new BinReader(b));
        Assert.Equal(200000002, back.A);
    }

    [Fact]
    public void WorldFamilyOp_SetCadre_Wire()
    {
        // op 0x0c : a = charId cible, b = rang cadre 0..7.
        var back = RoundTrip(
            WorldFamilyOp.Of(FamilyOps.SetCadre, 200000003, 4));
        Assert.Equal(200000003, back.A);
        Assert.Equal(4, back.B);
    }

    [Fact]
    public void WorldFamilyOp_AddMessage_Wire()
    {
        // op 0x0f : s1 = titre, s2 = contenu (message-board).
        var back = RoundTrip(WorldFamilyOp.OfStr(
            FamilyOps.AddMessage, "titre", "contenu"));
        Assert.Equal("titre", back.S1);
        Assert.Equal("contenu", back.S2);
    }

    [Fact]
    public void WorldQueryFamilyEmblem_Wire()
    {
        var back = RoundTrip(new WorldQueryFamilyEmblem
        {
            FamilyId = 900001, Field2 = 42,
        });
        Assert.Equal(900001, back.FamilyId);
        Assert.Equal(42, back.Field2);
    }

    // ---------- Guilde monde S→C ----------

    [Fact]
    public void WorldFamilyInfo_Type0_FullBlock()
    {
        // Type 0 complet : bloc famille 19 champs + u16 membres(0)
        // + u16 titres + u16 privilèges (FamilyPackets.WriteFamilyBlock).
        var w = new BinWriter();
        w.WriteByte(0);                  // type
        w.WriteShort(1);                 // worldId
        w.WriteInt(900001);              // family id
        w.WriteInt(1_700_000_000);       // create_time
        w.WriteString("TestGuildHL");    // name
        w.WriteByte(10);                 // lv
        w.WriteInt(12345);               // exp
        w.WriteInt(200000002);           // leader
        w.WriteInt(999);                 // exploit
        w.WriteInt(7);                   // emblem
        w.WriteShort(-1);                // uniform
        w.WriteString("Guilde de test HL"); // info
        w.WriteInt(999999999);           // treasury
        w.WriteShort(3); w.WriteShort(7);// tax/business
        w.WriteInt(0); w.WriteShort(0);  // elfking/spell
        w.WriteByte(0);                  // u8 recrut flag
        w.WriteShort(501);               // house_open
        w.WriteShort(1);                 // recruit
        w.WriteInt(42);                  // honor
        w.WriteShort(0);                 // u16 membres
        w.WriteShort(2);                 // u16 titres
        w.WriteString("Member"); w.WriteString("Leader");
        w.WriteShort(2);                 // u16 privilèges
        w.WriteInt(1); w.WriteInt(2);
        var m = new WorldFamilyInfo();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0, m.Type);
        Assert.Equal(900001, m.FamilyId);
        Assert.Equal("TestGuildHL", m.FamilyName);
        Assert.Equal(10, m.Level);
        Assert.Equal(200000002, m.LeaderId);
        Assert.Equal(999999999, m.Treasury);
        Assert.Equal("Guilde de test HL", m.Info);
        Assert.Equal(501, m.HouseOpen);
        Assert.Equal(1, m.Recruit);
        Assert.Equal(42, m.Honor);
        Assert.Equal(new[] { "Member", "Leader" }, m.Titles);
        Assert.Equal(new[] { 1, 2 }, m.Privileges);
    }

    /// <summary>Écrit un record membre variante type-8 (queryMode) —
    /// miroir de <c>WorldFamilyMemberUpdate.WriteMember</c>.</summary>
    private static void WriteMemberT8(BinWriter w, int id, string name,
        short lv, short cls, byte online, int contrib)
    {
        w.WriteInt(id);                  // charId
        w.WriteInt(1_699_000_000);       // addFamilyTime (t8 : après id)
        w.WriteString(name);
        w.WriteShort(lv);                // level
        w.WriteShort(2);                 // nodeId
        w.WriteShort(cls);               // classId (pas de race en t8)
        w.WriteShort(lv);                // classLv
        w.WriteByte(online);             // online
        w.WriteShort(0);                 // u16 +0x1a
        w.WriteByte(0);                  // gender
        w.WriteShort(0);                 // u16 +0x1c
        w.WriteInt(0);                   // i32 +0x20
        w.WriteInt(1_700_000_000);       // lastSaving
        w.WriteString("");               // selfComment
        w.WriteString("");               // familyComment
        w.WriteInt(contrib);             // contribution (t8 : fin)
    }

    [Fact]
    public void WorldFamilyInfo_Type8_Members()
    {
        var w = new BinWriter();
        w.WriteByte(8);                  // type 8 = chunk membres
        w.WriteShort(2);                 // u16 n
        WriteMemberT8(w, 200000001, "gmchar", 100, 4, 1, 0);
        WriteMemberT8(w, 200000002, "test1", 30, 5, 1, 120);
        var m = new WorldFamilyInfo();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(8, m.Type);
        Assert.Equal(2, m.Members.Count);
        var a = m.Members[0];
        Assert.Equal(200000001, a.CharId);
        Assert.Equal("gmchar", a.Name);
        Assert.Equal(100, a.Level);
        Assert.Equal(4, a.ClassId);
        Assert.Equal(1, a.Online);
        var b = m.Members[1];
        Assert.Equal("test1", b.Name);
        Assert.Equal(5, b.ClassId);
        Assert.Equal(120, b.Contribution);
    }

    [Fact]
    public void WorldFamilyInfo_Type5_Treasury()
    {
        var w = new BinWriter();
        w.WriteByte(5);
        w.WriteInt(777);
        var m = new WorldFamilyInfo();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(777, m.Treasury);
    }

    [Fact]
    public void WorldFamilyMemberUpdate_Parse46()
    {
        // NCID 46 : record queryMode=false — race entre node/class,
        // addFamilyTime + contribution en fin.
        var w = new BinWriter();
        w.WriteInt(200000003);           // charId
        w.WriteString("test2");
        w.WriteShort(30);                // level
        w.WriteShort(2);                 // nodeId
        w.WriteShort(0);                 // raceId (46 uniquement)
        w.WriteShort(3);                 // classId
        w.WriteShort(30);                // classLv
        w.WriteByte(1);                  // online
        w.WriteShort(0);
        w.WriteByte(1);                  // gender
        w.WriteShort(0);
        w.WriteInt(0);
        w.WriteInt(1_700_000_000);       // lastSaving
        w.WriteString("hello");          // selfComment
        w.WriteString("note");           // familyComment
        w.WriteInt(1_699_500_000);       // addFamilyTime (fin, 46)
        w.WriteInt(55);                  // contribution (fin, 46)
        var m = new WorldFamilyMemberUpdate();
        m.Deserialize(new BinReader(w.ToArray()));
        var g = m.Member;
        Assert.Equal(200000003, g.CharId);
        Assert.Equal("test2", g.Name);
        Assert.Equal(3, g.ClassId);
        Assert.Equal(1, g.Online);
        Assert.Equal("hello", g.SelfComment);
        Assert.Equal(1_699_500_000, g.AddFamilyTime);
        Assert.Equal(55, g.Contribution);
    }

    [Fact]
    public void WorldFamilyOpException_Parse()
    {
        var w = new BinWriter();
        w.WriteByte(0x0a);
        var m = new WorldFamilyOpException();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0x0a, m.Op);
    }

    [Fact]
    public void WorldFamilyInvitation_Parse()
    {
        // [i32 inviterCharId][str inviter][str family] — natif émet
        // le charId réel ; le serveur C# émet 0 (écart documenté).
        var w = new BinWriter();
        w.WriteInt(200000002);
        w.WriteString("test1");
        w.WriteString("TestGuildHL");
        var m = new WorldFamilyInvitation();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000002, m.InviterId);
        Assert.Equal("test1", m.InviterName);
        Assert.Equal("TestGuildHL", m.FamilyName);
    }

    [Fact]
    public void WorldFamilySchedule_Parse()
    {
        // [u8 type][u16 n][n×(s16 id][i32 date][str][str][str])]
        // [u8 flag][s16 state].
        var w = new BinWriter();
        w.WriteByte(0);
        w.WriteShort(1);
        w.WriteShort(7);                 // message_id
        w.WriteInt(1_700_000_100);       // create_date
        w.WriteString("Titre"); w.WriteString("Contenu");
        w.WriteString("auteur");
        w.WriteByte(0);
        w.WriteShort(3);                 // state
        var m = new WorldFamilySchedule();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Single(m.Entries);
        Assert.Equal(7, m.Entries[0].MessageId);
        Assert.Equal("Titre", m.Entries[0].Title);
        Assert.Equal("auteur", m.Entries[0].PlayerName);
        Assert.Equal(3, m.Flag);
    }

    [Fact]
    public void WorldFamilyLogUpdate_Parse()
    {
        // [s16 type][s16 state] puis 6 vecteurs parallèles [u16 n]…
        var w = new BinWriter();
        w.WriteShort(1); w.WriteShort(0);
        w.WriteShort(2);                 // types
        w.WriteShort(10); w.WriteShort(11);
        w.WriteShort(2);                 // ids
        w.WriteInt(100); w.WriteInt(101);
        w.WriteShort(2);                 // dates
        w.WriteInt(1_700_000_000); w.WriteInt(1_700_000_500);
        w.WriteShort(2);                 // paras
        w.WriteInt(200000002); w.WriteInt(200000003);
        w.WriteShort(2);                 // s1
        w.WriteString("test1"); w.WriteString("test2");
        w.WriteShort(2);                 // s2
        w.WriteString("join"); w.WriteString("leave");
        var m = new WorldFamilyLogUpdate();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(10, m.Entries[0].Type);
        Assert.Equal(100, m.Entries[0].LogId);
        Assert.Equal("test1", m.Entries[0].Str1);
        Assert.Equal("leave", m.Entries[1].Str2);
    }

    [Fact]
    public void WorldFamilyEmblem_Parse()
    {
        var w = new BinWriter();
        w.WriteInt(900001);
        w.WriteInt(7);
        w.WriteShort(3);
        w.WriteBytes(new byte[] { 1, 2, 3 });
        var m = new WorldFamilyEmblem();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(900001, m.FamilyId);
        Assert.Equal(7, m.Field2);
        Assert.Equal(new byte[] { 1, 2, 3 }, m.Data);
    }

    [Fact]
    public void WorldFamilyRecruitList_Parse()
    {
        // [s16 n][n×(i32][str][str][s16][s16][s16])][s16 page][s16 total]
        var w = new BinWriter();
        w.WriteShort(1);
        w.WriteInt(900002);
        w.WriteString("TestGuild2");
        w.WriteString("test3");
        w.WriteShort(2);                 // membres
        w.WriteShort(10);                // lv
        w.WriteShort(1);                 // recruit
        w.WriteShort(0);                 // page
        w.WriteShort(1);                 // total
        var m = new WorldFamilyRecruitList();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Single(m.Entries);
        var e = m.Entries[0];
        Assert.Equal(900002, e.FamilyId);
        Assert.Equal("TestGuild2", e.Name);
        Assert.Equal("test3", e.LeaderName);
        Assert.Equal(1, m.Total);
    }

    // ---------- Guilde zone ----------

    [Fact]
    public void ZoneFamilyCreate_Wire()
    {
        var back = RoundTrip(new ZoneFamilyCreate { Value = "NewG" });
        Assert.Equal("NewG", back.Value);
    }

    [Fact]
    public void ZoneUpdateFamily_Wire()
    {
        var w = new BinWriter();
        new ZoneUpdateFamily { CharId = 200000002 }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(4, b.Length);
        Assert.Equal(200000002, BitConverter.ToInt32(b));
    }

    [Fact]
    public void ZoneSetFamilyEmblem_Wire()
    {
        var back = RoundTrip(new ZoneSetFamilyEmblem { EmblemId = 12 });
        Assert.Equal(12, back.EmblemId);
    }

    [Fact]
    public void ZoneFamilyDonateMoney_Wire()
    {
        var back = RoundTrip(new ZoneFamilyDonateMoney { Value = 5000 });
        Assert.Equal(5000, back.Value);
    }

    [Fact]
    public void ZoneClientChangeFamily_Parse()
    {
        // [u32 charId][i32 familyId][u8 flag][str name][i32 rank]
        // [s16 emblem][u8 revision] — ZoneEntryFlow.Family.cs:127-144.
        var w = new BinWriter();
        w.WriteInt(200000002);
        w.WriteInt(900001);
        w.WriteByte(0);
        w.WriteString("TestGuildHL");
        w.WriteInt(-1);                  // rank −1 = membre
        w.WriteShort(7);                 // emblem
        w.WriteByte(2);                  // revision
        var m = new ZoneClientChangeFamily();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000002u, m.CharId);
        Assert.Equal(900001, m.FamilyId);
        Assert.Equal("TestGuildHL", m.FamilyName);
        Assert.Equal(-1, m.Rank);
        Assert.Equal(7, m.Emblem);
        Assert.Equal(2, m.Revision);
    }

    // ---------- GuildState ----------

    [Fact]
    public void GuildState_ApplyInfo_Sequence()
    {
        GuildState.Clear();
        try
        {
            // Type 0 → fiche complète.
            var w = new BinWriter();
            w.WriteByte(0);
            w.WriteShort(1);
            w.WriteInt(900001);
            w.WriteInt(1_700_000_000);
            w.WriteString("TestGuildHL");
            w.WriteByte(10);
            w.WriteInt(0);
            w.WriteInt(200000002);
            w.WriteInt(0); w.WriteInt(7);
            w.WriteShort(-1);
            w.WriteString("info");
            w.WriteInt(999999999);
            w.WriteShort(3); w.WriteShort(7);
            w.WriteInt(0); w.WriteShort(0);
            w.WriteByte(0);
            w.WriteShort(501); w.WriteShort(1);
            w.WriteInt(42);
            w.WriteShort(0); w.WriteShort(0); w.WriteShort(0);
            var t0 = new WorldFamilyInfo();
            t0.Deserialize(new BinReader(w.ToArray()));
            GuildState.ApplyInfo(t0);
            Assert.True(GuildState.InGuild);
            Assert.Equal("TestGuildHL", GuildState.Name);
            Assert.True(GuildState.IsLeader(200000002));

            // Chunk type 8 → 1 membre.
            var w8 = new BinWriter();
            w8.WriteByte(8);
            w8.WriteShort(1);
            WriteMemberT8(w8, 200000002, "test1", 30, 5, 1, 9);
            var t8 = new WorldFamilyInfo();
            t8.Deserialize(new BinReader(w8.ToArray()));
            GuildState.ApplyInfo(t8);
            Assert.Single(GuildState.Members);
            Assert.Equal("test1", GuildState.Members[200000002].Name);

            // 49 → tout vide.
            GuildState.ApplyQuit();
            Assert.False(GuildState.InGuild);
            Assert.Empty(GuildState.Members);
        }
        finally { GuildState.Clear(); }
    }

    [Fact]
    public void GuildState_ApplyZoneChange_SelfOnly()
    {
        GuildState.Clear();
        try
        {
            var w = new BinWriter();
            new ZoneClientChangeFamily
            {
                CharId = 200000002, FamilyId = 900001,
                FamilyName = "TestGuildHL", Rank = 3, Emblem = 7,
                Revision = 5,
            }.Serialize(w);
            var m = new ZoneClientChangeFamily();
            m.Deserialize(new BinReader(w.ToArray()));
            GuildState.ApplyZoneChange(m, 200000003); // autre → ignoré
            Assert.Equal(-1, GuildState.SelfRank);
            GuildState.ApplyZoneChange(m, 200000002); // soi
            Assert.Equal(3, GuildState.SelfRank);
            Assert.Equal(5, GuildState.SelfRevision);
            Assert.Equal("TestGuildHL", GuildState.Name);
        }
        finally { GuildState.Clear(); }
    }

    [Fact]
    public void GuildState_Invitation_Quit()
    {
        GuildState.Clear();
        try
        {
            var inv = new WorldFamilyInvitation
            {
                InviterId = 200000002, InviterName = "test1",
                FamilyName = "TestGuildHL",
            };
            GuildState.ApplyInvitation(inv);
            Assert.NotNull(GuildState.PendingInvite);
            Assert.Equal(200000002,
                GuildState.PendingInvite!.Value.InviterId);
            GuildState.PendingInvite = null;
            Assert.Null(GuildState.PendingInvite);
        }
        finally { GuildState.Clear(); }
    }
}
