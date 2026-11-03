using Eden.Core.Game.Services;
using Eden.Core.Networking;

namespace Eden.Core.Game.Colosseum;

/// <summary>
/// Codecs dédiés des réponses Colosseum client (NCIDs 524-530,
/// 552, 587) — formats prouvés par les <c>Serialize</c> natifs du
/// serveur zone. Les classes générées `*.g.cs` de ces NCIDs sont
/// aplaties par le générateur (format variable) et inutilisables.
/// <para>Preuves (zone `decompiled.c`) :
/// <list type="bullet">
///   <item>524 <c>CNC_CZ_ClientEnterGladiatorTeamCreation::Serialize</c>
///   : [i32][s16].</item>
///   <item>525 <c>CNC_CZ_ClientGladiatorTeamInfo::Serialize</c> :
///   [i32][i32][u32][u8].</item>
///   <item>526 <c>CNC_CZ_ClientDisbandGladiatorTeam::Serialize</c> :
///   [i32].</item>
///   <item>527 <c>CNC_CZ_ClientEnterColosseum::Serialize</c> : [u8]
///   (1 = match prêt, le client affiche la notif d'entrée —
///   <c>Mission_TC_EnterColosseum</c> @0x83b61c0 pose +4=1).</item>
///   <item>528 <c>CNC_CZ_ClientColosseumJoinResult::Serialize</c> :
///   [s16] (1 échec, 5 scène interdite).</item>
///   <item>529 <c>CNC_CZ_ClientColosseumTeamInfo::Serialize</c> :
///   [s16 cat][u16 nTeams]{s16 teamId, s16 nMembres, ×membre
///   [i32 charId][u8 heroFeature][s16 level][u8 +0x47c][s16
///   SLV +0x47e][str nom][i32 +0xbb8][u16 +0xbc0][u8 alive]}
///   [i8][i32][i32] — champs par <c>CColosseum::TeamInfoQuery</c>
///   @0x8402… zone.</item>
///   <item>530 <c>CNC_CZ_ClientColosseumLeaveJoinResult::Serialize</c>
///   : [i32 charId][s16 result].</item>
///   <item>552 <c>CNC_CZ_ClientColosseumModeQueryResult::Serialize</c>
///   : [s16 mode].</item>
///   <item>587 <c>CNC_CZ_ClientColosseumQuery::Serialize</c> :
///   [s16][u16 n i32][u16 n i32][u16 n i32][u16 n u32][u16 n u8]
///   [s16][u16 n s16][u16 n s16] — construit par
///   <c>TC_ColosseumDataQuery</c> mission @0x81fa770.</item>
/// </list></para>
/// </summary>
public static class ColosseumPackets
{
    // NCIDs client (ncid_map).
    public const int NcidEnterGladiatorTeamCreation = 524;
    public const int NcidGladiatorTeamInfo = 525;
    public const int NcidDisbandGladiatorTeam = 526;
    public const int NcidEnterColosseum = 527;
    public const int NcidJoinResult = 528;
    public const int NcidTeamInfo = 529;
    public const int NcidLeaveJoinResult = 530;
    public const int NcidModeQueryResult = 552;
    public const int NcidColosseumQuery = 587;

    /// <summary>524 `EnterGladiatorTeamCreation` [i32][s16] —
    /// système gladiator legacy (les NCIDs entrants 196/197 sont des
    /// no-ops natifs ; émis pour complétude).</summary>
    public static byte[] EnterGladiatorTeamCreation(int v1, short v2)
    {
        var w = new OutBinStream();
        w.WriteInt(v1);           // i32 — param natif +4
        w.WriteShort(v2);        // s16 — param natif +8
        return w.ToArray();
    }

    /// <summary>525 `GladiatorTeamInfo` [i32][i32][u32][u8].</summary>
    public static byte[] GladiatorTeamInfo(int v1, int v2, uint v3,
        byte v4)
    {
        var w = new OutBinStream();
        w.WriteInt(v1);           // i32
        w.WriteInt(v2);           // i32
        w.WriteInt(unchecked((int)v3)); // u32
        w.WriteByte(v4);          // u8
        return w.ToArray();
    }

    /// <summary>526 `DisbandGladiatorTeam` [i32].</summary>
    public static byte[] DisbandGladiatorTeam(int v1)
    {
        var w = new OutBinStream();
        w.WriteInt(v1);           // i32
        return w.ToArray();
    }

    /// <summary>527 `EnterColosseum` [u8] — notif d'entrée de match
    /// (le natif émet 1 dans <c>Mission_TC_EnterColosseum</c> avant
    /// la téléportation <c>CEventEnterColosseum</c>).</summary>
    public static byte[] EnterColosseum(byte flag)
    {
        var w = new OutBinStream();
        w.WriteByte(flag);       // u8 — 1 = entrée imminente
        return w.ToArray();
    }

    /// <summary>528 `JoinResult` [s16 result] — voir
    /// <see cref="ColosseumService.JoinCode"/>.</summary>
    public static byte[] JoinResult(short result)
    {
        var w = new OutBinStream();
        w.WriteShort(result);    // s16 — 0 ok / 1 échec / 5 scène
        return w.ToArray();
    }

    /// <summary>Un membre d'équipe du 529
    /// (<c>CColosseum::TeamInfoQuery</c> : un enregistrement par
    /// charId du camp, chars résolus via
    /// <c>CZoneServer::GetCharacter</c>).</summary>
    public sealed record TeamMember(int CharId, byte HeroFeature,
        short Level, byte Field47c, short ClassLevel, string Name,
        int Score, ushort FieldBc0, byte Alive);

    /// <summary>529 `ColosseumTeamInfo` — composition des équipes du
    /// match du demandeur :
    /// <code>
    /// [s16 categorie]
    /// [u16 nTeams] ×team : [s16 teamId][s16 nMembres]
    ///                    ×membre : [i32 charId][u8 hero][s16 level]
    ///                              [u8 +0x47c][s16 SLV][str nom]
    ///                              [i32 +0xbb8][u16 +0xbc0][u8 alive]
    /// [i8 flag][i32][i32]
    /// </code>
    /// </summary>
    public static byte[] TeamInfo(short category,
        IReadOnlyList<(short TeamId, IReadOnlyList<TeamMember> Members)>
            teams, sbyte tailFlag = 0, int tail1 = 0, int tail2 = 0)
    {
        var w = new OutBinStream();
        w.WriteShort(category);                  // s16 — catégorie
        w.WriteShort((short)teams.Count);        // u16 — nTeams
        foreach (var (teamId, members) in teams)
        {
            w.WriteShort(teamId);                // s16 — id d'équipe
            w.WriteShort((short)members.Count);  // s16 — n membres
            foreach (var m in members)
            {
                w.WriteInt(m.CharId);          // i32 — char+0x34
                w.WriteByte(m.HeroFeature);    // u8 — GetHeroFeature
                w.WriteShort(m.Level);       // s16 — char+0x470
                w.WriteByte(m.Field47c);     // u8 — char+0x47c
                w.WriteShort(m.ClassLevel);  // s16 — char+0x47e
                w.WriteString(m.Name);       // str — char+0x324
                w.WriteInt(m.Score);         // i32 — char+0xbb8
                w.WriteShort(unchecked((short)m.FieldBc0)); // u16 — char+0xbc0
                w.WriteByte(m.Alive);        // u8 — IsAlive
            }
        }
        w.WriteByte(unchecked((byte)tailFlag)); // i8 — flag final
        w.WriteInt(tail1);                       // i32
        w.WriteInt(tail2);                       // i32
        return w.ToArray();
    }

    /// <summary>530 `LeaveJoinResult` [i32 charId][s16 result] —
    /// émis par <c>CNE_ZMis_ZoneServerColosseumLeaveJoinResult::
    /// Execute</c> zone @0x83… et par le gate « en match » du 201
    /// (result 0).</summary>
    public static byte[] LeaveJoinResult(int charId, short result)
    {
        var w = new OutBinStream();
        w.WriteInt(charId);       // i32 — char concerné
        w.WriteShort(result);     // s16 — code retour
        return w.ToArray();
    }

    /// <summary>552 `ModeQueryResult` [s16 mode] —
    /// <c>CColosseumFactory::ModeQueryResult</c> zone @0x8059f30 :
    /// recopie de factory+4 (schedId courant 0-3).</summary>
    public static byte[] ModeQueryResult(short mode)
    {
        var w = new OutBinStream();
        w.WriteShort(mode);       // s16 — mode/schedId courant
        return w.ToArray();
    }

    /// <summary>587 `ColosseumQuery` — calendrier des créneaux
    /// colosseum du jour (<c>TC_ColosseumDataQuery</c> @0x81fa770) :
    /// <code>
    /// [s16 type=0]
    /// [u16 n][i32 schedType]×n      (200-203)
    /// [u16 n][i32 beginEpoch]×n     (timelocal jour + beginHHMM)
    /// [u16 n][i32 endEpoch]×n
    /// [u16 n][u32 extraTime]×n      (now si le créneau est actif,
    ///                                sinon 0 — natif timelocal
    ///                                de l'heure courante)
    /// [u16 n][u8 actif]×n           (1 si créneau == round courant)
    /// [s16 0]                       (piVar9+0x44 — non écrit par le
    ///                                TC natif, reste 0)
    /// [u16 n][s16 beginHHMM]×n
    /// [u16 n][s16 endHHMM]×n
    /// </code>
    /// Chaque entrée = un créneau schedule 200-203 dont le jour
    /// (entry+0x16) == jour courant.</summary>
    public static byte[] ColosseumQuery(
        IReadOnlyList<ColosseumService.TodaySlot> slots)
    {
        var w = new OutBinStream();
        w.WriteShort(0);                          // s16 — en-tête
        w.WriteShort((short)slots.Count);            // u16 n
        foreach (var s in slots) w.WriteInt(s.SchedType); // i32 200-203
        w.WriteShort((short)slots.Count);
        foreach (var s in slots) w.WriteInt(s.BeginEpoch);   // i32 epoch
        w.WriteShort((short)slots.Count);
        foreach (var s in slots) w.WriteInt(s.EndEpoch);     // i32 epoch
        w.WriteShort((short)slots.Count);
        // u32 — now si actif (natif pousse timelocal du moment pour
        // le créneau courant), sinon 0.
        var now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var s in slots) w.WriteInt(s.Active
            ? unchecked((int)now) : 0);
        w.WriteShort((short)slots.Count);
        foreach (var s in slots) w.WriteByte((byte)(s.Active ? 1 : 0));
        w.WriteShort(0);                          // s16 — piVar9+0x44
        w.WriteShort((short)slots.Count);
        foreach (var s in slots) w.WriteShort(s.BeginHhmm); // s16 HHMM
        w.WriteShort((short)slots.Count);
        foreach (var s in slots) w.WriteShort(s.EndHhmm);   // s16 HHMM
        return w.ToArray();
    }
}
