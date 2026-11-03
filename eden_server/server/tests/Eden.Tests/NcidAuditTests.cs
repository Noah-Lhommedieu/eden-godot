using Xunit;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Tests;

/// <summary>
/// Tests byte-level des NCIDs audités : layouts mail par lot
/// (Deserialize natifs @0x8263ec0 / @0x8264ec0), bag duedate
/// (CPlayerFactory::SetBagDueDate @0x81133f0 → 389), CoRideInvite
/// (Execute @0x8246130), position de recrutement
/// (CCharacter::ChangePositionV2 @0x840b190 → SSearchTeamMemberDataV2
/// +0x12, CTeamMember::GetTeamMemberData mission @0x81c8b40).
/// </summary>
public class NcidAuditTests
{
    private static byte[] Ser(NetCommand cmd)
    {
        var s = new OutBinStream();
        cmd.Serialize(s);
        return s.ToArray();
    }

    // ── 98 MailDelete : [i32 mailId][u16 n][n×i32] ─────────────

    [Fact]
    public void MailDelete_Single_WireLayout()
    {
        // mailId=42, lot vide : 4+2 octets
        var bytes = new byte[] { 42, 0, 0, 0, 0, 0 };
        var m = new CNE_CZ_ZoneServerMailDelete();
        m.Deserialize(new InBinStream(bytes));
        Assert.Equal(42, m.MailId);
        Assert.Empty(m.MailIds);
    }

    [Fact]
    public void MailDelete_Batch_WireLayout()
    {
        // mailId=0, n=3, ids 7/8/9 — chemin MailPatchDelete natif
        var bytes = new byte[] {
            0, 0, 0, 0,          // i32 mailId = 0 → lot
            3, 0,                // u16 n = 3
            7, 0, 0, 0,
            8, 0, 0, 0,
            9, 0, 0, 0,
        };
        var m = new CNE_CZ_ZoneServerMailDelete();
        m.Deserialize(new InBinStream(bytes));
        Assert.Equal(0, m.MailId);
        Assert.Equal(new[] { 7, 8, 9 }, m.MailIds);
        // round-trip identique
        Assert.Equal(bytes, Ser(m));
    }

    // ── 100 MailGetItem : [i32 mailId][u16 n][n×i32][s16] ──────

    [Fact]
    public void MailGetItem_Single_WireLayout()
    {
        var bytes = new byte[] {
            0x39, 0x30, 0, 0,    // i32 mailId = 12345
            0, 0,                // u16 n = 0
            5, 0,                // s16 freeNeeded = 5
        };
        var m = new CNE_CZ_ZoneServerMailGetItem();
        m.Deserialize(new InBinStream(bytes));
        Assert.Equal(12345, m.MailId);
        Assert.Empty(m.MailIds);
        Assert.Equal(5, m.FreeNeeded);
        Assert.Equal(bytes, Ser(m));
    }

    [Fact]
    public void MailGetItem_Batch_WireLayout()
    {
        var bytes = new byte[] {
            0, 0, 0, 0,          // i32 mailId = 0 → lot
            2, 0,                // u16 n = 2
            11, 0, 0, 0,
            12, 0, 0, 0,
            9, 0,                // s16 freeNeeded
        };
        var m = new CNE_CZ_ZoneServerMailGetItem();
        m.Deserialize(new InBinStream(bytes));
        Assert.Equal(new[] { 11, 12 }, m.MailIds);
        Assert.Equal(9, m.FreeNeeded);
        Assert.Equal(bytes, Ser(m));
    }

    // ── 89/389 BagDueDate ──────────────────────────────────────

    [Fact]
    public void ClientBagDueDate_WireLayout()
    {
        // 389 [s16 bag][i32 unix] — ClientBagDueDate natif, champs
        // short,int (Serialize @0x80cxxxx, désasm SetBagDueDate).
        var bytes = Ser(new CNC_CZ_ClientBagDueDate
        {
            Field1 = 2,
            Field2 = 0x67000000,
        });
        Assert.Equal(6, bytes.Length);
        Assert.Equal(2, bytes[0]);
        Assert.Equal(0x67000000, bytes[2] | (bytes[3] << 8) |
            (bytes[4] << 16) | (bytes[5] << 24));
    }

    [Fact]
    public void BagDueDate_In_WireLayout()
    {
        // 89 [s16 bag][u32 secondes] — Execute lit +0x12 / +0x14.
        var m = new CNE_CZ_ZoneServerBagDueDate();
        m.Deserialize(new InBinStream(
            new byte[] { 1, 0, 0x80, 0x51, 0x01, 0 }));
        Assert.Equal(1, m.Field1);
        Assert.Equal(86400u, m.Field2);
    }

    // ── 202 CoRideInvite [u32 cible][u8 op] ────────────────────

    [Fact]
    public void CoRideInvite_WireLayout()
    {
        var m = new CNE_CZ_ZoneServerCoRideInvite();
        m.Deserialize(new InBinStream(
            new byte[] { 0x78, 0x56, 0x34, 0x12, 0x00 }));
        Assert.Equal(0x12345678u, m.Field1);   // u32 : uid cible
        Assert.Equal(0, m.Field2);             // u8  : op invite
        // op 8/9 = UnCoRide (dispatch Execute natif)
        m = new CNE_CZ_ZoneServerCoRideInvite();
        m.Deserialize(new InBinStream(
            new byte[] { 0, 0, 0, 0, 0x08 }));
        Assert.Equal(8, m.Field2);
    }

    // ── 238 ChangePositionV2 → position recrutement ────────────

    [Fact]
    public void SetRecruitPosition_NativeRules()
    {
        var svc = new TeamService();
        // Défaut natif = 2 (init ExecutionerData +0x87e).
        Assert.Equal(2, svc.GetRecruitPosition(1234));
        // Positions valides < 3 (ChangePositionV2 : (ushort)pos < 3).
        Assert.True(svc.SetRecruitPosition(1234, 0));
        Assert.Equal(0, svc.GetRecruitPosition(1234));
        Assert.True(svc.SetRecruitPosition(1234, 1));
        // No-op si inchangée (désasm : test +0xd88 avant écriture).
        Assert.False(svc.SetRecruitPosition(1234, 1));
        // >= 3 refusé.
        Assert.False(svc.SetRecruitPosition(1234, 3));
        Assert.False(svc.SetRecruitPosition(1234, -1));
        Assert.Equal(1, svc.GetRecruitPosition(1234));
        // ClearRecruit purge aussi la position.
        svc.ClearRecruit(1234);
        Assert.Equal(2, svc.GetRecruitPosition(1234));
    }

    // ── 86 TeamRadarAttention relai (3×s16 préservés) ──────────

    [Fact]
    public void TeamRadarAttention_WireLayout()
    {
        var m = new CNE_CZ_ZoneServerTeamRadarAttention();
        m.Deserialize(new InBinStream(
            new byte[] { 0x10, 0x00, 0x20, 0x00, 0x30, 0x00 }));
        Assert.Equal(0x10, m.Field1);
        Assert.Equal(0x20, m.Field2);
        Assert.Equal(0x30, m.Field3);
    }

    // ── Panneau d'affichage (139 write / 457-459 réponses) ──────

    [Fact]
    public void MessageWrite_In_WireLayout()
    {
        // 139 [str titre][str contenu] — Execute @0x8269960 lit
        // this+0x14 / +0x18 → les deux strings du 802.
        var t = System.Text.Encoding.ASCII.GetBytes("Salut");
        var c = System.Text.Encoding.ASCII.GetBytes("contenu ici");
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((ushort)t.Length));
        bytes.AddRange(t);
        bytes.AddRange(BitConverter.GetBytes((ushort)c.Length));
        bytes.AddRange(c);
        var m = new CNE_CZ_ZoneServerMessageWrite();
        m.Deserialize(new InBinStream(bytes.ToArray()));
        Assert.Equal("Salut", m.Field1);
        Assert.Equal("contenu ici", m.Field2);
        Assert.Equal(bytes.ToArray(), Ser(m));
    }

    [Fact]
    public void MessageWriteResult_WireLayout()
    {
        // 457 [s16 res] — 2 = écrit+purgé (Execute @0x8329xxx),
        // 3 = refus (carte 0x7ceb absente, Execute @0x8269960).
        var bytes = Ser(new CNC_CZ_ClientMessageWriteResult { Value = 2 });
        Assert.Equal(new byte[] { 2, 0 }, bytes);
    }

    [Fact]
    public void MessageRead_In_WireLayout()
    {
        // 140 [i32 id] → DBA "MessageRead id = %d".
        var m = new CNE_CZ_ZoneServerMessageRead();
        m.Deserialize(new InBinStream(
            new byte[] { 0x39, 0x30, 0, 0 }));
        Assert.Equal(12345, m.Value);
    }

    [Fact]
    public void MessageRead_Out_WireLayout()
    {
        // 458 [i32 id][str contenu][u8] — miroir du ZMis 741 body[6..].
        var m = new CNC_CZ_ClientMessageRead
        {
            Field1 = 7,
            Field2 = "texte du post",
            Field3 = 0,
        };
        var bytes = Ser(m);
        Assert.Equal(7, BitConverter.ToInt32(bytes, 0));
        Assert.Equal((ushort)13, BitConverter.ToUInt16(bytes, 4));
        Assert.Equal(0, bytes[^1]);
    }

    // ── 491 carte joueur : [i32][str][3×s16][str][12×s16][str]
    //    [bitvec i32][bitvec s16] ────────────────────────────────

    [Fact]
    public void ClientQueryPlayerCard_WireLayout()
    {
        var m = new CNC_CZ_ClientQueryPlayerCard
        {
            CharId = 0x1234,
            PlayerName = "Hero",
            Level = 60,
            ClassId = 7,
            ClassLv = 40,
            FamilyName = "Guilde",
            Intro = "salut",
        };
        m.Stats[0] = 1; m.Stats[11] = 2;
        var bytes = Ser(m);
        var o = 0;
        Assert.Equal(0x1234, BitConverter.ToInt32(bytes, o)); o += 4;
        var nlen = BitConverter.ToUInt16(bytes, o); o += 2 + nlen;
        Assert.Equal(4, nlen);                     // "Hero"
        Assert.Equal(60, BitConverter.ToInt16(bytes, o)); o += 2;
        Assert.Equal(7, BitConverter.ToInt16(bytes, o)); o += 2;
        Assert.Equal(40, BitConverter.ToInt16(bytes, o)); o += 2;
        var flen = BitConverter.ToUInt16(bytes, o); o += 2 + flen;
        Assert.Equal(6, flen);                     // "Guilde"
        Assert.Equal(1, BitConverter.ToInt16(bytes, o)); // stat0
        Assert.Equal(2, BitConverter.ToInt16(bytes, o + 22)); // stat11
        o += 24;
        var ilen = BitConverter.ToUInt16(bytes, o); o += 2 + ilen;
        Assert.Equal(5, ilen);                     // "salut"
        // 2 bitvecs vides → 4 octets de fin
        Assert.Equal(4, bytes.Length - o);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, o));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, o + 2));
    }

    [Fact]
    public void ClientQueryPlayerCard_BitVec()
    {
        // 33 éléments : groupe 32 (4B) + groupe 1 (1B). Bit i levé
        // si l'élément i ≠ 0 (lapis::operator<< CBinCaptain).
        var m = new CNC_CZ_ClientQueryPlayerCard
        { CollectionFlags = new int[33] };
        m.CollectionFlags[0] = 1; m.CollectionFlags[31] = 1;
        m.CollectionFlags[32] = 1;
        var bytes = Ser(m);
        // trailer : [u16 33][4B mask g1][1B mask g2][u16 0] = 9 octets
        var tail = bytes[^9..];
        Assert.Equal((ushort)33, BitConverter.ToUInt16(tail, 0));
        Assert.Equal(0x01, tail[2]);               // bit0
        Assert.Equal(0x80, tail[5]);               // bit31
        Assert.Equal(0x01, tail[6]);               // groupe2 bit0
        Assert.Equal((ushort)0, BitConverter.ToUInt16(tail, 7));
        // round-trip
        var d = new CNC_CZ_ClientQueryPlayerCard();
        d.Deserialize(new InBinStream(bytes));
        Assert.Equal(33, d.CollectionFlags.Length);
        Assert.Equal(1, d.CollectionFlags[0]);
        Assert.Equal(1, d.CollectionFlags[32]);
    }

    [Fact]
    public void PlayerCardUpdate_In_WireLayout()
    {
        // 169 [s16 op][s16 val][str] — op 1-12 stat, 0xd intro,
        // 0xe is_public (CPlayerFactory::PlayerCardUpdate).
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((short)0xd));
        bytes.AddRange(BitConverter.GetBytes((short)0));
        var s = System.Text.Encoding.ASCII.GetBytes("bio");
        bytes.AddRange(BitConverter.GetBytes((ushort)s.Length));
        bytes.AddRange(s);
        var m = new CNE_CZ_ZoneServerPlayerCardUpdate();
        m.Deserialize(new InBinStream(bytes.ToArray()));
        Assert.Equal(0xd, m.Field1);
        Assert.Equal("bio", m.Field3);
    }
}
