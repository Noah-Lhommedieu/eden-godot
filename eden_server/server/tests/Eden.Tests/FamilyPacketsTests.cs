using Xunit;
using Eden.Core.Game.Family;

namespace Eden.Tests;

/// <summary>
/// Tests byte-par-byte des trames famille monde→client —
/// formats prouvés par désassemblage :
/// <list type="bullet">
/// <item>46 <c>ClientUpdateFamilyMember</c> — Serialize @ world
/// 0x80635d0, remplissage <c>UpdateFamilyMemberProxy</c> @0x8051860.</item>
/// <item>48 <c>ClientFamilyInvitation</c> — [i32 0][str][str]
/// (<c>CGlobalInfo::FamilyInvitation</c>).</item>
/// <item>50 <c>ClientQueryFamilyInfo</c> — Serialize @0x80ceb10 :
/// type 2 = famille absente ; 5/6/7 = champ unique ; type &lt;2 →
/// bloc de 19 champs ; type 8 → [u16 n] records.</item>
/// <item>74 <c>ClientFamilyEmblem</c> — [i32][i32][u16 len][blob].</item>
/// </list>
/// </summary>
public class FamilyPacketsTests
{
    private static Family MakeFamily() => new()
    {
        Id = 7, Name = "EdenGuild", LeaderId = 42, LeaderName = "Leader",
        Level = 3, Exp = 2500, Exploit = 900, Emblem = 12, Uniform = 5,
        Info = "infos publiques", Treasury = 100000, TaxRate = 5,
        BusinessTax = 7, ElfkingTid = 77, SpellPoint = 33,
        HouseOpen = 1, Recruit = 1, Honor = 500, BeCreateTime = 1700000000,
    };

    private static FamilyMemberSummary MakeMember() => new()
    {
        CharacterId = 42, Name = "Leader", Level = 60, NodeId = 1,
        RaceId = 3, ClassId = 9, ClassLevel = 55, Gender = 1,
        Online = true, AddFamilyTime = 1700000100,
        LastSavingTime = 1700000200, SelfComment = "moi",
        FamilyComment = "cadre", ContributionPoint = 321, Rank = 1,
    };

    /// <summary>NCID 47 — [u8 op] (Execute natif renvoie la sous-op
    /// en échec).</summary>
    [Fact]
    public void OpException_IsSingleByte()
        => Assert.Equal(new byte[] { 0x05 }, FamilyPackets.FamilyOpException(5));

    /// <summary>NCID 48 — [i32 0][str inviter][str famille].</summary>
    [Fact]
    public void Invitation_HasReservedZeroThenStrings()
    {
        var b = FamilyPackets.FamilyInvitation("Alice", "EdenGuild");
        // i32 0
        Assert.Equal(0, BitConverter.ToInt32(b, 0));
        // str = [u16 len][bytes]
        var off = 4;
        var l1 = BitConverter.ToUInt16(b, off);
        Assert.Equal("Alice", System.Text.Encoding.UTF8.GetString(b, off + 2, l1));
        off += 2 + l1;
        var l2 = BitConverter.ToUInt16(b, off);
        Assert.Equal("EdenGuild", System.Text.Encoding.UTF8.GetString(b, off + 2, l2));
        Assert.Equal(off + 2 + l2, b.Length);
    }

    /// <summary>NCID 50 type 2 — famille absente : [u8 2] seul
    /// (Serialize natif : WriteByte(this+5) puis return).</summary>
    [Fact]
    public void QueryFamilyInfo_AbsentFamily_IsType2Only()
        => Assert.Equal(new byte[] { 2 }, FamilyPackets.QueryFamilyInfo(null, 0, 1));

    /// <summary>NCID 50 types 5/6/7 — champ unique puis fin.</summary>
    [Fact]
    public void QueryFamilyInfo_Type5_6_7_AreSingleField()
    {
        var fam = MakeFamily();
        var b5 = FamilyPackets.QueryFamilyInfo(fam, 5, 1);
        Assert.Equal(5, b5[0]);
        Assert.Equal(fam.Treasury, BitConverter.ToInt32(b5, 1));
        Assert.Equal(5, b5.Length);

        var b6 = FamilyPackets.QueryFamilyInfo(fam, 6, 1);
        Assert.Equal(fam.ElfkingTid, BitConverter.ToInt32(b6, 1));
        Assert.Equal(5, b6.Length);

        var b7 = FamilyPackets.QueryFamilyInfo(fam, 7, 1);
        Assert.Equal(fam.SpellPoint, BitConverter.ToInt16(b7, 1));
        Assert.Equal(3, b7.Length);
    }

    /// <summary>NCID 50 type 0 — [u8 0][u16 world] + bloc famille
    /// + [u16 0 membres] + [u16 9] titres + [u16 14] privilèges.</summary>
    [Fact]
    public void QueryFamilyInfo_Type0_FullBlock()
    {
        var fam = MakeFamily();
        var b = FamilyPackets.QueryFamilyInfo(fam, 0, 1);
        Assert.Equal(0, b[0]);                       // u8 type
        Assert.Equal(1, BitConverter.ToUInt16(b, 1)); // u16 world id
        Assert.Equal(fam.Id, BitConverter.ToInt32(b, 3)); // i32 family id
        Assert.Equal(fam.BeCreateTime, BitConverter.ToInt32(b, 7));
    }

    /// <summary>NCID 50 type 8 — records membres sans world_id ni
    /// bloc famille ([u16 n] puis records
    /// {i32 id, i32 add_time, str, s16×4, u8, u16, u8, u16, i32, i32,
    /// str, str, i32 contrib}).</summary>
    [Fact]
    public void QueryFamilyInfo_Type8_MemberChunk()
    {
        var fam = MakeFamily();
        var m = MakeMember();
        var b = FamilyPackets.QueryFamilyInfo(fam, 8, 1,
            new List<FamilyMemberSummary> { m });
        Assert.Equal(8, b[0]);                        // u8 type
        Assert.Equal(1, BitConverter.ToUInt16(b, 1)); // u16 n=1 record
        var off = 3;
        Assert.Equal(m.CharacterId, BitConverter.ToInt32(b, off));
        Assert.Equal(m.AddFamilyTime, BitConverter.ToInt32(b, off + 4));
    }

    /// <summary>NCID 46 — record complet sans add_time en tête :
    /// {i32 id, str, s16 lv, s16 node, s16 race, s16 class,
    /// s16 class_lv, u8 online, u16, u8 gender, u16, i32,
    /// i32 last_saving, str, str, i32 add_time, u32 contrib}.</summary>
    [Fact]
    public void UpdateFamilyMember_RecordLayout()
    {
        var m = MakeMember();
        var b = FamilyPackets.UpdateFamilyMember(m);
        Assert.Equal(m.CharacterId, BitConverter.ToInt32(b, 0));
        var off = 4;
        var nameLen = BitConverter.ToUInt16(b, off);
        Assert.Equal("Leader", System.Text.Encoding.UTF8.GetString(b, off + 2, nameLen));
        off += 2 + nameLen;
        Assert.Equal(m.Level, BitConverter.ToInt16(b, off));      // s16 level
        Assert.Equal(m.NodeId, BitConverter.ToInt16(b, off + 2)); // s16 node
        Assert.Equal(m.RaceId, BitConverter.ToInt16(b, off + 4)); // s16 race
        Assert.Equal(m.ClassId, BitConverter.ToInt16(b, off + 6));
        Assert.Equal(m.ClassLevel, BitConverter.ToInt16(b, off + 8));
        Assert.Equal(1, b[off + 10]);                             // u8 online
        Assert.Equal(m.Gender, b[off + 13]);                      // u8 gender
    }

    /// <summary>NCID 74 — [i32 family][i32 champ][u16 len][blob].</summary>
    [Fact]
    public void FamilyEmblem_BlobFraming()
    {
        var blob = new byte[] { 1, 2, 3, 255 };
        var b = FamilyPackets.FamilyEmblem(7, 0, blob);
        Assert.Equal(7, BitConverter.ToInt32(b, 0));
        Assert.Equal(0, BitConverter.ToInt32(b, 4));
        Assert.Equal(4, BitConverter.ToUInt16(b, 8));
        Assert.Equal(blob, b[10..]);
        Assert.Equal(14, b.Length);
    }

    /// <summary>NCID 68 — [s16 type][s16 state] + 6 vecteurs
    /// comptés (s16 type, i32 id, i32 date, i32 para, str, str).</summary>
    [Fact]
    public void FamilyLogUpdate_ParallelVectors()
    {
        var logs = new List<FamilyLogEntry>
        {
            new() { LogId = 1, Type = 2, Date = 100, Para = 5,
                    Str1 = "a", Str2 = "b" },
        };
        var b = FamilyPackets.FamilyLogUpdate(1, 3, logs);
        Assert.Equal(1, BitConverter.ToInt16(b, 0));   // s16 type
        Assert.Equal(3, BitConverter.ToInt16(b, 2));   // s16 state
        Assert.Equal(1, BitConverter.ToUInt16(b, 4));  // u16 count (types)
        Assert.Equal(2, BitConverter.ToInt16(b, 6));   // s16 log_type[0]
    }

    /// <summary>NCID 51 — [u8 type][u16 n] + entrées
    /// (s16 id, i32 date, str×3) + [u8][s16].</summary>
    [Fact]
    public void FamilySchedule_EntryLayout()
    {
        var list = new List<FamilyMessage>
        {
            new() { MessageId = 4, CreateDate = 777, Title = "t",
                    Content = "c", PlayerName = "p" },
        };
        var b = FamilyPackets.FamilySchedule(0, 0, list);
        Assert.Equal(0, b[0]);                        // u8 type
        Assert.Equal(1, BitConverter.ToUInt16(b, 1)); // u16 n
        Assert.Equal(4, BitConverter.ToInt16(b, 3));  // s16 message_id
        Assert.Equal(777, BitConverter.ToInt32(b, 5));// i32 create_date
    }

    /// <summary>Chunking natif : les membres se découpent par 30
    /// (<c>Execute</c> flush à iVar22 &gt; 0x1d).</summary>
    [Fact]
    public void MemberChunkSize_Is30()
        => Assert.Equal(30, Family.MemberChunkSize);

    // ---- 487 ClientFamilyInstanceData — Serialize @0x81e82e0 (zone)
    // + construction dans FamilyInstanceGetData::Execute @0x8256ee0 :
    // [i32 family_id][str nom][s16 lv][s16 house_open][s16 conteneur=0]
    // [u16 n][n×(s16 loc, i32 item_tid, i32 tid, s16 item_type,
    // s16 durabilité, s16 niveau, u32 exp)] ----

    /// <summary>Header + vecteur vide — 4+2+2+2+2+2 + str.</summary>
    [Fact]
    public void InstanceData_EmptyVector()
    {
        var b = FamilyPackets.FamilyInstanceData(42, "Fam", 5, 501, []);
        var off = 0;
        Assert.Equal(42, BitConverter.ToInt32(b, off)); off += 4;
        var slen = BitConverter.ToUInt16(b, off);
        Assert.Equal("Fam",
            System.Text.Encoding.UTF8.GetString(b, off + 2, slen));
        off += 2 + slen;
        Assert.Equal(5, BitConverter.ToInt16(b, off));    // s16 lv
        Assert.Equal(501, BitConverter.ToInt16(b, off + 2)); // s16 house_open
        Assert.Equal(0, BitConverter.ToInt16(b, off + 4));   // s16 conteneur
        Assert.Equal(0, BitConverter.ToUInt16(b, off + 6));  // u16 n
        Assert.Equal(off + 8, b.Length);
    }

    /// <summary>Deux bâtiments — chaque record = 7 champs dans
    /// l'ordre natif (loc, item_tid, tid, item_type, durabilité,
    /// niveau, exp).</summary>
    [Fact]
    public void InstanceData_RecordsInOrder()
    {
        var items = new List<FamilyBuildingItem>
        {
            new() { Loc = 3, ItemTid = 7001, Tid = 9001, ItemType = 2,
                Durability = 50, Level = 4, Exp = 777 },
            new() { Loc = 9, ItemTid = 7002, Tid = 9002, ItemType = 1,
                Durability = 0, Level = 1, Exp = 0 },
        };
        var b = FamilyPackets.FamilyInstanceData(8, "G", 3, 502, items);
        var off = 4 + 2 + 1 /* "G" */ + 6;              // header lu
        Assert.Equal(2, BitConverter.ToUInt16(b, off)); off += 2;
        // Record 0
        Assert.Equal(3, BitConverter.ToInt16(b, off));
        Assert.Equal(7001, BitConverter.ToInt32(b, off + 2));
        Assert.Equal(9001, BitConverter.ToInt32(b, off + 6));
        Assert.Equal(2, BitConverter.ToInt16(b, off + 10));
        Assert.Equal(50, BitConverter.ToInt16(b, off + 12));
        Assert.Equal(4, BitConverter.ToInt16(b, off + 14));
        Assert.Equal(777u, BitConverter.ToUInt32(b, off + 16));
        off += 20;
        // Record 1
        Assert.Equal(9, BitConverter.ToInt16(b, off));
        Assert.Equal(7002, BitConverter.ToInt32(b, off + 2));
        Assert.Equal(9002, BitConverter.ToInt32(b, off + 6));
        Assert.Equal(1, BitConverter.ToInt16(b, off + 10));
        Assert.Equal(0, BitConverter.ToInt16(b, off + 12));
        Assert.Equal(1, BitConverter.ToInt16(b, off + 14));
        Assert.Equal(0u, BitConverter.ToUInt32(b, off + 16));
        Assert.Equal(off + 20, b.Length);
    }

    /// <summary>item_tid s16 stocké émis en i32 signé — les valeurs
    /// > 32767 doivent conserver leur bit pattern (u16 → i32 positif
    /// natif : `*(ushort*)` lu sans signe puis poussé int).</summary>
    [Fact]
    public void InstanceData_ItemTidUnsignedWidening()
    {
        var items = new List<FamilyBuildingItem>
        {
            new() { Loc = 0, ItemTid = unchecked((short)0x8001),
                Tid = 1, ItemType = 0, Durability = 0, Level = 0,
                Exp = 0 },
        };
        var b = FamilyPackets.FamilyInstanceData(1, "F", 1, 501, items);
        // [i32 fam][u16 1]["F"][s16×3][u16 n=1] → record à off 15.
        Assert.Equal(0, BitConverter.ToInt16(b, 15));          // s16 loc
        // i32 élargi NON signé : 0x8001 = 32769 (natif lit *(ushort*)).
        Assert.Equal(0x8001, BitConverter.ToInt32(b, 17));
    }

    // ---- InstanceCreateGate — ordre natif CFamily::InstanceCreate
    // (mission @0x81bf660) ----

    private static Family HallReadyFamily() => new()
    {
        Id = 10, Name = "F", LeaderId = 42, Level = 4,
        Treasury = 2_000_000,
    };

    /// <summary>type 0 → node 502, type 1 → 501, autres → abandon
    /// silencieux (-1).</summary>
    [Fact]
    public void InstanceNode_TypeMapping()
    {
        Assert.Equal(502, FamilyService.InstanceNode(0));
        Assert.Equal(501, FamilyService.InstanceNode(1));
        Assert.Equal(-1, FamilyService.InstanceNode(2));
    }

    /// <summary>Gate « déjà un hall » (house_open≠0) → 0x91a.</summary>
    [Fact]
    public void Gate_AlreadyOpen_91a()
    {
        var fam = HallReadyFamily();
        fam.HouseOpen = 501;
        FillMembers(fam, 30);
        Assert.Equal(0x91a, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 0));
    }

    /// <summary>Niveau perso ≤29 → 0x599 (exec+0x5c ≤ 0x1d natif).</summary>
    [Fact]
    public void Gate_LowLevel_599()
    {
        var fam = HallReadyFamily();
        FillMembers(fam, 30);
        Assert.Equal(0x599, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 29, 0));
    }

    /// <summary>Pas le chef → 0x598 (fam+0x10 ≠ charId).</summary>
    [Fact]
    public void Gate_NotLeader_598()
    {
        var fam = HallReadyFamily();
        FillMembers(fam, 30);
        Assert.Equal(0x598, FamilyService.InstanceCreateGate(
            fam, 43, 0, 0, 60, 0));
    }

    /// <summary>Niveau famille <3 → 0x59a.</summary>
    [Fact]
    public void Gate_FamilyLevel_59a()
    {
        var fam = HallReadyFamily();
        fam.Level = 2;
        FillMembers(fam, 30);
        Assert.Equal(0x59a, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 0));
    }

    /// <summary>Cap villes vip-0 : >49 → 0x59c ; vip 1 passe (le cap
    /// natif `_DAT_085b41f0` n'est testé que quand vip==0).</summary>
    [Fact]
    public void Gate_TownCap_59c_VipBypass()
    {
        var fam = HallReadyFamily();
        fam.Treasury = 20_000_000;
        FillMembers(fam, 30);
        Assert.Equal(0x59c, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 50));
        Assert.Equal(0, FamilyService.InstanceCreateGate(
            fam, 42, 1, 1, 60, 50));
    }

    /// <summary>Trésorerie insuffisante → 0x6ae (500 k / 10 M).</summary>
    [Fact]
    public void Gate_Treasury_6ae()
    {
        var fam = HallReadyFamily();
        FillMembers(fam, 30);
        fam.Treasury = 499_999;
        Assert.Equal(0x6ae, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 0));
        fam.Treasury = 500_000;
        Assert.Equal(0, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 0));
        fam.Treasury = 9_999_999;
        Assert.Equal(0x6ae, FamilyService.InstanceCreateGate(
            fam, 42, 1, 1, 60, 0));
    }

    /// <summary>Moins de 30 membres (fam+0x108) → 0x59b.</summary>
    [Fact]
    public void Gate_MemberCount_59b()
    {
        var fam = HallReadyFamily();
        FillMembers(fam, 29);
        Assert.Equal(0x59b, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 0));
    }

    /// <summary>Famille en recyclage (recycled_time≠0 —
    /// AllBeRecycledFamilyIDs) → 0xa4b.</summary>
    [Fact]
    public void Gate_Recycled_a4b()
    {
        var fam = HallReadyFamily();
        fam.RecycledTime = 123;
        FillMembers(fam, 30);
        Assert.Equal(0xa4b, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 0));
    }

    /// <summary>Chemin nominal : tous les gates passent → 0.</summary>
    [Fact]
    public void Gate_AllPass_0()
    {
        var fam = HallReadyFamily();
        FillMembers(fam, 30);
        Assert.Equal(0, FamilyService.InstanceCreateGate(
            fam, 42, 0, 0, 60, 10));
        fam.Treasury = 10_000_000;
        Assert.Equal(0, FamilyService.InstanceCreateGate(
            fam, 42, 1, 1, 60, 10));
    }

    /// <summary>Type hors {0,1} après tous les gates → abandon
    /// silencieux (-1, pas de texte natif).</summary>
    [Fact]
    public void Gate_BadType_Silent()
    {
        var fam = HallReadyFamily();
        FillMembers(fam, 30);
        Assert.Equal(-1, FamilyService.InstanceCreateGate(
            fam, 42, 3, 0, 60, 10));
    }

    /// <summary>Remplit la map membre (charIds 1000+ pour ne pas
    /// recouvrir le chef 42 — ajouté explicitement).</summary>
    private static void FillMembers(Family fam, int count)
    {
        fam.Members[42] = new FamilyMemberSummary
        {
            CharacterId = 42, Name = "Leader", Level = 60, Online = true,
        };
        for (var i = fam.Members.Count; i < count; i++)
            fam.Members[1000 + i] = new FamilyMemberSummary
            {
                CharacterId = 1000 + i, Name = $"M{i}", Level = 60,
            };
    }
}
