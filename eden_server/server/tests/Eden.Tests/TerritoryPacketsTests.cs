using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests des codecs territoire/bataille de famille et de la logique
/// <see cref="TerritoryWarService"/> — formats prouvés par
/// désassemblage :
/// <list type="bullet">
/// <item>561 <c>CNC_CZ_ClientFamilyBattleInfo</c> — Serialize zone
/// @0x81e8580 : [s16 type] + corps variable (ranks / phase / camps).</item>
/// <item>515 <c>CNC_CZ_ClientTerritoryQuery</c> — Serialize zone
/// @0x82043d0 : s16 en-tête + 8 vecteurs parallèles + map de triplets
/// de schedules.</item>
/// <item>Phases via <c>S_Schedule</c> + caps du Join natif
/// (<c>CTerritory::Join</c> mission @0x81dadb0 : CharMaxNum=350,
/// FB_CharMaxNum=50, .data 0x85a56cc/d).</item>
/// </list>
/// </summary>
public class TerritoryPacketsTests
{
    // ---------- 561 — CNC_CZ_ClientFamilyBattleInfo ----------

    /// <summary>Type 0 : [s16 0][u16 n](str nom, i32 énergie)×n —
    /// itération FamilyBattleRanks de TC_FamilyBattleJudgeQuery
    /// @0x820a1b0.</summary>
    [Fact]
    public void BattleRanks_Type0_Wire()
    {
        var b = FamilyBattlePackets.FamilyBattleRanks(
            [("Alpha", 500), ("Beta", 250)]);
        var r = new InBinStream(b);
        Assert.Equal(0, r.ReadShort());          // type 0
        Assert.Equal(2, r.ReadShort());          // n=2
        Assert.Equal("Alpha", r.ReadString());   // nom famille
        Assert.Equal(500, r.ReadInt());          // énergie
        Assert.Equal("Beta", r.ReadString());
        Assert.Equal(250, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    /// <summary>Type 1 : [s16 1][i32 phase] — phase de la battle de
    /// la famille (TC_FamilyBattleCheckJoin @0x81f51e0).</summary>
    [Fact]
    public void BattlePhase_Type1_Wire()
    {
        var b = FamilyBattlePackets.FamilyBattlePhase(2);
        var r = new InBinStream(b);
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(2, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    /// <summary>Type 3 : [s16 3][i32 3] + 3 triplets i32/s16/i32.</summary>
    [Fact]
    public void BattleCamps_Type3_Wire()
    {
        var b = FamilyBattlePackets.FamilyBattleCamps(
            [(10, (short)1, 100), (20, (short)2, 200),
             (30, (short)3, 300)]);
        var r = new InBinStream(b);
        Assert.Equal(3, r.ReadShort());          // type 3
        Assert.Equal(3, r.ReadInt());            // littéral natif 3
        Assert.Equal(10, r.ReadInt());           // camp 0 : a
        Assert.Equal(1, r.ReadShort());          //         b
        Assert.Equal(100, r.ReadInt());          //         c
        Assert.Equal(20, r.ReadInt());           // camp 1 : a
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(200, r.ReadInt());
        Assert.Equal(30, r.ReadInt());           // camp 2 : a
        Assert.Equal(3, r.ReadShort());
        Assert.Equal(300, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    // ---------- 515 — CNC_CZ_ClientTerritoryQuery ----------

    /// <summary>Paquet vide : [s16 0] + 8 compteurs u16 à zéro
    /// (18 octets).</summary>
    [Fact]
    public void TerritoryQuery_Empty()
    {
        var b = FamilyBattlePackets.TerritoryQuery([], []);
        Assert.Equal(18, b.Length);
        Assert.All(b, x => Assert.Equal(0, x));
    }

    /// <summary>Ordre natif des vecteurs : ids, map schedules,
    /// own_fam, noms, niveaux, times, états, flags.</summary>
    [Fact]
    public void TerritoryQuery_Vectors_InNativeOrder()
    {
        var b = FamilyBattlePackets.TerritoryQuery(
            [new FamilyBattlePackets.TerritoryEntry(
                Id: 2, OwnFamilyId: 42, Name: "War", OwnerLevel: 5,
                Time: 1700000000, State: 1, Flag: 3)],
            [new FamilyBattlePackets.TerritoryScheduleEntry(
                ScheduleType: 1, Begin: 1000, End: 2000)]);
        var r = new InBinStream(b);
        Assert.Equal(0, r.ReadShort());          // s16 en-tête = 0
        Assert.Equal(1, r.ReadShort());          // n ids
        Assert.Equal(2, r.ReadInt());            // territory_id
        Assert.Equal(1, r.ReadShort());          // m schedules
        Assert.Equal(1, r.ReadInt());            // schedType
        Assert.Equal(1000, r.ReadInt());         // begin
        Assert.Equal(2000, r.ReadInt());         // end
        Assert.Equal(1, r.ReadShort());          // n own_fam
        Assert.Equal(42, r.ReadInt());
        Assert.Equal(1, r.ReadShort());          // n noms
        Assert.Equal("War", r.ReadString());
        Assert.Equal(1, r.ReadShort());          // n niveaux
        Assert.Equal(5, r.ReadShort());
        Assert.Equal(1, r.ReadShort());          // n times
        Assert.Equal(unchecked((int)1700000000u), r.ReadInt());
        Assert.Equal(1, r.ReadShort());          // n états
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(1, r.ReadShort());          // n flags
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    // ---------- TerritoryWarService — phases et join ----------

    private static TerritoryWarService Loaded()
    {
        var svc = new TerritoryWarService();
        svc.LoadTerritories(
            new Dictionary<int, GameDataService.TerritoryDef>
            {
                [2] = new(2, "War", 2, 2),
                [12] = new(12, "FB", 301, 12),
            },
            [(2, 42, 3)]);
        return svc;
    }

    /// <summary>GetScheduleTypeByTerritoryType natif : 1-15 → type-1,
    /// 301-315 → identique, sinon -1.</summary>
    [Fact]
    public void ScheduleType_Mapping()
    {
        Assert.Equal(1,
            TerritoryWarService.ScheduleTypeForTerritoryType(2));
        Assert.Equal(301,
            TerritoryWarService.ScheduleTypeForTerritoryType(301));
        Assert.Equal(-1,
            TerritoryWarService.ScheduleTypeForTerritoryType(0));
        Assert.Equal(-1,
            TerritoryWarService.ScheduleTypeForTerritoryType(500));
    }

    /// <summary>La famille propriétaire est inscrite défenseure au
    /// chargement (territory_war.own_family_id).</summary>
    [Fact]
    public void Load_OwnerIsDefender()
    {
        var svc = Loaded();
        var t = svc.GetTerritory(2)!;
        Assert.Equal(42, t.OwnFamilyId);
        Assert.Equal(3, t.OwnTimes);
        Assert.Contains(42, t.DefenderFamilies);
    }

    /// <summary>Join natif : famille défenseure → camp 1 ; famille
    /// non inscrite → équilibrage vers le camp le moins peuplé ;
    /// AlreadyIn si le perso est déjà au sol.</summary>
    [Fact]
    public void Join_Camps_AndCaps()
    {
        var svc = Loaded();
        var t = svc.GetTerritory(2)!;
        t.Phase = 2;
        // Famille propriétaire → défenseur (camp 1).
        Assert.Equal(TerritoryWarService.JoinResult.Success,
            svc.TryJoin(t, 100, 42, out var camp));
        Assert.Equal(1, camp);
        // Même perso → AlreadyIn.
        Assert.Equal(TerritoryWarService.JoinResult.AlreadyIn,
            svc.TryJoin(t, 100, 42, out _));
        // Famille inconnue : 1 défenseur vs 0 attaquant inscrit →
        // l'équilibrage vise le camp ayant le moins de FAMILLES
        // (+0x40 vs +0x58) → attaquant (camp 2).
        Assert.Equal(TerritoryWarService.JoinResult.Success,
            svc.TryJoin(t, 200, 77, out camp));
        Assert.Equal(2, camp);
        // Leave retire le perso.
        Assert.Same(t, svc.Leave(100));
        Assert.Null(svc.Leave(999));
    }

    /// <summary>GetFamilyBattle : battle type 301-315 dont un camp
    /// contient la famille et phase ∈{1,2}.</summary>
    [Fact]
    public void GetFamilyBattle_RequiresPhaseAndCamp()
    {
        var svc = Loaded();
        svc.RegisterFamily(12, 55, defender: true);
        var t = svc.GetTerritory(12)!;
        Assert.Null(svc.GetFamilyBattle(55));    // phase 0 → pas de battle
        t.Phase = 1;
        Assert.Same(t, svc.GetFamilyBattle(55));
        Assert.Null(svc.GetFamilyBattle(66));    // pas inscrite
    }

    // ---------- Cycle de guerre (532/547/562/566 + kills) ----------

    /// <summary>RecordKill : compteurs par camp (terr+0x30/+0x34
    /// natifs), kills perso (char+0xb9c) et score famille sont
    /// incrémentés — base des émissions 532/562/566.</summary>
    [Fact]
    public void RecordKill_CampsCharsAndFamilies()
    {
        var svc = Loaded();
        var t = svc.GetTerritory(2)!;
        svc.TryJoin(t, 100, 42, out var c1);
        svc.TryJoin(t, 200, 77, out var c2);
        svc.NoteJoinInfo(t, 100, c1, "Alice", 42, "OwnerFam");
        svc.NoteJoinInfo(t, 200, c2, "Bob", 77, "ChallFam");
        // Kill défenseur (camp 1) : +0x30 incrémente, score famille 42.
        Assert.Equal(1, svc.RecordKill(t, 100, 1));
        Assert.Equal(1, t.DefenderKills);
        Assert.Equal(0, t.AttackerKills);
        Assert.Equal(1, t.CharKills[100]);
        Assert.Equal(1, t.FamilyScores[42].Score);
        Assert.Equal(1, t.FamilyScores[42].Side);
        // Deux kills attaquants.
        svc.RecordKill(t, 200, 2);
        Assert.Equal(2, svc.RecordKill(t, 200, 2));
        Assert.Equal(2, t.AttackerKills);
        Assert.Equal(2, t.CharKills[200]);
        Assert.Equal(2, t.FamilyScores[77].Score);
    }

    /// <summary>Leaderboard 562 : tri par kills décroissants, rank
    /// séquentiel 1-based, les persos à 0 kill exclus.</summary>
    [Fact]
    public void BuildKillRank_Ordered()
    {
        var svc = Loaded();
        var t = svc.GetTerritory(2)!;
        svc.NoteJoinInfo(t, 100, 1, "Alice", 42, "OwnerFam");
        svc.NoteJoinInfo(t, 200, 2, "Bob", 77, "ChallFam");
        svc.NoteJoinInfo(t, 300, 2, "Cara", 77, "ChallFam");
        t.CharKills[100] = 1;
        t.CharKills[200] = 3;
        t.CharKills[300] = 2;
        var rank = svc.BuildKillRank(t);
        Assert.Equal(3, rank.Count);
        Assert.Equal(("Bob", "ChallFam", (short)3, (short)1),
            rank[0]);
        Assert.Equal(("Cara", "ChallFam", (short)2, (short)2),
            rank[1]);
        Assert.Equal(("Alice", "OwnerFam", (short)1, (short)3),
            rank[2]);
    }

    /// <summary>Vainqueur = famille au meilleur score du camp gagnant
    /// (défenseur en cas d'égalité → fallback famille propriétaire).</summary>
    [Fact]
    public void ResolveWinner_TopFamilyOfWinningSide()
    {
        var svc = Loaded();
        var t = svc.GetTerritory(2)!;
        svc.NoteJoinInfo(t, 100, 1, "Alice", 42, "OwnerFam");
        svc.NoteJoinInfo(t, 200, 2, "Bob", 77, "ChallFam");
        // Attaquants gagnent 2-1, famille 77 a marqué.
        t.DefenderKills = 1; t.AttackerKills = 2;
        t.FamilyScores[77] = ("ChallFam", 2, 2);
        Assert.Equal(77, svc.ResolveWinnerFamily(t));
        // Égalité → camp défenseur, meilleure famille défensive (ou
        // la propriétaire 42 par défaut).
        t.AttackerKills = 1;
        Assert.Equal(42, svc.ResolveWinnerFamily(t));
    }

    /// <summary>Transition active→fin : RefreshPhases pose
    /// ClosePending et conserve les données pour les émissions ;
    /// FinalizeClose vide tout après.</summary>
    [Fact]
    public void ClosePending_KeepsDataUntilFinalize()
    {
        var svc = Loaded();
        var t = svc.GetTerritory(2)!;
        svc.NoteJoinInfo(t, 100, 1, "Alice", 42, "OwnerFam");
        t.CharKills[100] = 5;
        // Pas de schedule chargé → phase 0 au refresh : active→fin.
        t.Phase = 2; t.BattleNodeId = 301;
        var schedules = new Dictionary<int,
            List<GameDataService.ScheduleEntry>>();
        svc.RefreshPhases(schedules, DateTimeOffset.UtcNow);
        Assert.True(t.ClosePending);
        Assert.Equal(5, t.CharKills[100]);      // données conservées
        svc.FinalizeClose(t);
        Assert.False(t.ClosePending);
        Assert.Empty(t.CharKills);
        Assert.Empty(t.DefenderChars);
        Assert.Equal(0, t.BattleNodeId);
    }

    /// <summary>Wire 516 : [s16 type][i32 remaining][i32 own_fam]
    /// [u16 camp] — format prouvé par SendTerritoryData @0x08424ab0
    /// et TerritoryWarStart @0x08158c90.</summary>
    [Fact]
    public void TerritoryInfo_516_Wire()
    {
        var pkt = new CNC_CZ_ClientTerritoryInfo
        {
            Field1 = 1, Field2 = 1800, Field3 = 42, Field4 = 2,
        };
        var w = new OutBinStream();
        pkt.Serialize(w);
        var r = new InBinStream(w.ToArray());
        Assert.Equal(1, r.ReadShort());          // type 1 = guerre
        Assert.Equal(1800, r.ReadInt());         // remaining
        Assert.Equal(42, r.ReadInt());           // own_family
        Assert.Equal(2, r.ReadShort());          // camp
    }

    /// <summary>Wire 532 : [i32 killsA][i32 killsB][s16][s16]
    /// [s16 ownKills] — emit CEventTerritoryKilledInfo @0x0866e6e0.</summary>
    [Fact]
    public void TerritoryKilledInfo_532_Wire()
    {
        var pkt = new CNC_CZ_ClientTerritoryKilledInfo
        {
            Field1 = 7, Field2 = 12, Field3 = 0, Field4 = 0,
            Field5 = 3,
        };
        var w = new OutBinStream();
        pkt.Serialize(w);
        var r = new InBinStream(w.ToArray());
        Assert.Equal(7, r.ReadInt());            // kills camp A
        Assert.Equal(12, r.ReadInt());           // kills camp B
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(3, r.ReadShort());          // kills perso
    }

    /// <summary>547 corps vide — CEventTerritoryClose::Execute
    /// @0x0819c240.</summary>
    [Fact]
    public void TerritoryEnd_547_Empty()
    {
        var pkt = new CNC_CZ_ClientTerritoryEnd();
        var w = new OutBinStream();
        pkt.Serialize(w);
        Assert.Empty(w.ToArray());
    }
}
