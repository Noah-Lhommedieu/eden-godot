using Eden.Core.Game.Colosseum;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests des codecs Colosseum (524-530/552/587) et de la logique
/// <see cref="ColosseumService"/> — formats prouvés par les
/// <c>Serialize</c> natifs zone + <c>TC_ColosseumDataQuery</c>
/// mission @0x81fa770 + gates de
/// <c>CNE_CZ_ZoneServerColosseumJoinRequest::Execute</c>
/// @0x8244d30.
/// </summary>
public class ColosseumTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);

    // ---------- Réponses plates ----------

    /// <summary>527 `EnterColosseum` [u8] — émis à 1 par
    /// Mission_TC_EnterColosseum @0x83b61c0.</summary>
    [Fact]
    public void EnterColosseum_Wire()
    {
        var b = ColosseumPackets.EnterColosseum(1);
        Assert.Single(b);
        Assert.Equal(1, b[0]);
    }

    /// <summary>528 `JoinResult` [s16] — codes natifs 1/5.</summary>
    [Fact]
    public void JoinResult_Wire()
    {
        var r = new InBinStream(ColosseumPackets.JoinResult(5));
        Assert.Equal(5, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    /// <summary>530 `LeaveJoinResult` [i32 charId][s16].</summary>
    [Fact]
    public void LeaveJoinResult_Wire()
    {
        var r = new InBinStream(
            ColosseumPackets.LeaveJoinResult(42, 0));
        Assert.Equal(42, r.ReadInt());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    /// <summary>552 `ModeQueryResult` [s16 mode] — recopie de
    /// factory+4 (schedId 0-3).</summary>
    [Fact]
    public void ModeQueryResult_Wire()
    {
        var r = new InBinStream(ColosseumPackets.ModeQueryResult(3));
        Assert.Equal(3, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    // ---------- 529 — TeamInfo ----------

    /// <summary>529 : [s16 cat][u16 nTeams]{s16 id, s16 n,
    /// ×membre i32/u8/s16/u8/s16/str/i32/u16/u8}[i8][i32][i32]
    /// — <c>CColosseum::TeamInfoQuery</c> @0x8402….</summary>
    [Fact]
    public void TeamInfo_Wire()
    {
        var members = new List<ColosseumPackets.TeamMember>
        {
            new(100, 1, 45, 2, 60, "Hero", 7, 3, 1),
            new(101, 0, 40, 0, 55, "Foe", 0, 0, 0),
        };
        var b = ColosseumPackets.TeamInfo(4,
            [(7, members), (9, members)], tailFlag: 1,
            tail1: 300, tail2: 900);
        var r = new InBinStream(b);
        Assert.Equal(4, r.ReadShort());            // catégorie
        Assert.Equal(2, r.ReadShort());            // nTeams
        for (var t = 0; t < 2; t++)
        {
            Assert.Equal(t == 0 ? 7 : 9, r.ReadShort()); // teamId
            Assert.Equal(2, r.ReadShort());        // n membres
            for (var m = 0; m < 2; m++)
            {
                Assert.Equal(100 + m, r.ReadInt());    // charId
                r.ReadByte();                           // hero
                r.ReadShort();                          // level
                r.ReadByte();                           // +0x47c
                r.ReadShort();                          // SLV
                r.ReadString();                         // nom
                r.ReadInt();                            // +0xbb8
                r.ReadUShort();                         // +0xbc0
                r.ReadByte();                           // alive
            }
        }
        Assert.Equal(1, r.ReadByte());             // i8 flag
        Assert.Equal(300, r.ReadInt());            // i32
        Assert.Equal(900, r.ReadInt());            // i32
        Assert.Equal(0, r.Remaining);
    }

    // ---------- 587 — ColosseumQuery ----------

    /// <summary>587 : en-tête s16 + 5 vecteurs i32/u32/u8
    /// parallèles + s16 + 2 vecteurs s16 — ordre natif de
    /// <c>TC_ColosseumDataQuery</c>.</summary>
    [Fact]
    public void ColosseumQuery_Wire()
    {
        var slots = new List<ColosseumService.TodaySlot>
        {
            new(200, 1000, 2000, 1200, 1400, true),
            new(202, 3000, 4000, 1800, 2000, false),
        };
        var r = new InBinStream(ColosseumPackets.ColosseumQuery(slots));
        Assert.Equal(0, r.ReadShort());            // s16 en-tête
        Assert.Equal(2, r.ReadShort());            // u16 n types
        Assert.Equal(200, r.ReadInt());
        Assert.Equal(202, r.ReadInt());
        Assert.Equal(2, r.ReadShort());            // n begin epochs
        Assert.Equal(1000, r.ReadInt());
        Assert.Equal(3000, r.ReadInt());
        Assert.Equal(2, r.ReadShort());            // n end epochs
        Assert.Equal(2000, r.ReadInt());
        Assert.Equal(4000, r.ReadInt());
        Assert.Equal(2, r.ReadShort());            // n extraTime
        Assert.True(r.ReadInt() > 0);              // actif → now
        Assert.Equal(0, r.ReadInt());              // inactif → 0
        Assert.Equal(2, r.ReadShort());            // n flag actif
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadShort());            // s16 piVar9+0x44
        Assert.Equal(2, r.ReadShort());            // n begin HHMM
        Assert.Equal(1200, r.ReadShort());
        Assert.Equal(1800, r.ReadShort());
        Assert.Equal(2, r.ReadShort());            // n end HHMM
        Assert.Equal(1400, r.ReadShort());
        Assert.Equal(2000, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    // ---------- ColosseumService : mode / catégories / file ----------

    /// <summary>Une entrée de schedule couvrant `now` ouvre le mode
    /// correspondant (sched 202 → mode Twenty).</summary>
    [Fact]
    public void RefreshMode_ActiveWindow_OpensMode()
    {
        var wday = (int)Now.DayOfWeek;
        var hhmm = Now.Hour * 100 + Now.Minute;
        var svc = new ColosseumService();
        svc.RefreshMode(new Dictionary<int,
            List<GameDataService.ScheduleEntry>>
        {
            [202] = [new(wday, hhmm - 30, hhmm + 30)],
        }, Now);
        Assert.True(svc.IsOpen);
        Assert.Equal(2, svc.Mode);
    }

    /// <summary>Aucun créneau actif → fermé ; le mode garde sa
    /// dernière valeur (factory+4 persiste nativement).</summary>
    [Fact]
    public void RefreshMode_NoWindow_Closed()
    {
        var svc = new ColosseumService();
        svc.RefreshMode(new Dictionary<int,
            List<GameDataService.ScheduleEntry>>(), Now);
        Assert.False(svc.IsOpen);
        Assert.Equal(0, svc.Mode);          // init native = 0
    }

    /// <summary>Priorité TuneMode : ClassPK (203) évalué en dernier
    /// écrase les autres modes actifs.</summary>
    [Fact]
    public void RefreshMode_Priority_ClassPkWins()
    {
        var wday = (int)Now.DayOfWeek;
        var hhmm = Now.Hour * 100 + Now.Minute;
        var svc = new ColosseumService();
        svc.RefreshMode(new Dictionary<int,
            List<GameDataService.ScheduleEntry>>
        {
            [200] = [new(wday, hhmm - 30, hhmm + 30)],
            [203] = [new(wday, hhmm - 30, hhmm + 30)],
        }, Now);
        Assert.Equal(3, svc.Mode);
    }

    /// <summary>GetCategory natif : schedId 1 → 1 ; sinon level/10
    /// plafonné 8 (level &gt; 80 → 8).</summary>
    [Fact]
    public void CategoryOf_LevelBracket()
    {
        var svc = new ColosseumService();
        // mode 0 (défaut) → tranches de niveau.
        Assert.Equal(3, svc.CategoryOf(30, 1));
        Assert.Equal(8, svc.CategoryOf(85, 1));
        Assert.Equal(0, svc.CategoryOf(5, 1));
    }

    /// <summary>Join : le premier inscrit attend ; le second forme
    /// un match 1v1 et les deux quittent la file.</summary>
    [Fact]
    public void Join_SecondInQueue_FormsMatch()
    {
        var svc = new ColosseumService();
        Assert.Null(svc.Join(1, 40, 40));
        Assert.True(svc.InQueue(1));
        var formed = svc.Join(2, 40, 40);
        Assert.NotNull(formed);
        Assert.Equal([1, 2], formed);
        Assert.False(svc.InQueue(1));
        Assert.NotNull(svc.MatchOf(1));
        Assert.NotNull(svc.MatchOf(2));
    }

    /// <summary>Leave retire de la file ; CharacterLeave retire et
    /// dissout le match (CharacterLeave natif).</summary>
    [Fact]
    public void Leave_And_CharacterLeave()
    {
        var svc = new ColosseumService();
        svc.Join(1, 40, 40);
        Assert.True(svc.Leave(1));
        Assert.False(svc.InQueue(1));
        svc.Join(3, 40, 40);
        svc.Join(4, 40, 40);
        var m = svc.MatchOf(3)!;
        svc.CharacterLeave(3);
        Assert.Null(svc.MatchOf(3));
        Assert.Null(svc.MatchOf(4));   // match dissous
    }

    /// <summary>TodaySlots : seules les entrées du jour courant
    /// (entry+0x16 == wday natif) sont émises, avec le flag actif
    /// sur le créneau du mode courant.</summary>
    [Fact]
    public void TodaySlots_FiltersToday()
    {
        var wday = (int)Now.DayOfWeek;
        var other = (wday + 3) % 7;
        var svc = new ColosseumService();
        var scheds = new Dictionary<int,
            List<GameDataService.ScheduleEntry>>
        {
            [200] = [new(wday, 1200, 1400), new(other, 0, 2359)],
            [201] = [new(other, 0, 2359)],   // pas aujourd'hui
        };
        var slots = svc.TodaySlots(scheds, Now);
        Assert.Single(slots);
        Assert.Equal(200, slots[0].SchedType);
        Assert.Equal(1200, slots[0].BeginHhmm);
        Assert.False(slots[0].Active);     // pas ouvert sans RefreshMode
    }
}
