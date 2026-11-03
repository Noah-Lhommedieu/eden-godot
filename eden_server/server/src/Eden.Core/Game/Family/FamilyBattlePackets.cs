using Eden.Core.Networking;

namespace Eden.Core.Game.Family;

/// <summary>
/// Codecs dédiés territoire/bataille de famille zone → client. Les
/// classes générées (<c>ZoneMessages.g.cs</c>) ont aplati ces formats
/// VARIABLES — elles ne sont pas utilisées. Preuves natives :
/// <list type="bullet">
///   <item><c>CNC_CZ_ClientFamilyBattleInfo</c> (561) — Serialize zone
///   <c>@0x081e8580</c> : [s16 type] ; type 0 = [u16 n](str nom,
///   i32 énergie)×n ; type 1 = +[i32 phase] ; type 3 = [i32 3] +
///   3×[i32][s16][i32] (camps). Remplissage mission :
///   <c>TC_FamilyBattleJudgeQuery @0x820a1b0</c> (ranks) et
///   <c>TC_FamilyBattleCheckJoin @0x81f51e0</c> (phase).</item>
///   <item><c>CNC_CZ_ClientTerritoryQuery</c> (515) — Serialize zone
///   <c>@0x082043d0</c> : [s16 en-tête][u16 n ids territoires]
///   [u16 m triplets schedule (type, begin, end)][u16 n own_fam]
///   [u16 n noms][u16 n niveaux famille][u16 n times u32]
///   [u16 n états s16][u16 n flags u8]. Remplissage mission :
///   <c>TC_TerritoryDataQuery @0x8211860</c> — le s16 d'en-tête est lu
///   d'un local jamais initialisé (ebp-0x144) → on émet 0.</item>
/// </list>
/// </summary>
public static class FamilyBattlePackets
{
    /// <summary>NCID zone→client des infos bataille de famille.</summary>
    public const int NcidFamilyBattleInfo = 561;

    /// <summary>NCID zone→client de la requête territoires (données
    /// du panneau guerre de territoire + fenêtres événements).</summary>
    public const int NcidTerritoryQuery = 515;

    /// <summary>561 type 0 — liste de classement des rounds
    /// d'élimination : `[s16 0][u16 n](str nom_famille, i32 énergie)×n`.
    /// Le natif itère la map <c>FamilyBattleRanks</c> (phase→vector de
    /// <c>familybattle_ranks</c>) et écrit name+0x10 (=colonne
    /// <c>energy</c>).</summary>
    public static byte[] FamilyBattleRanks(
        IReadOnlyList<(string Name, int Energy)> ranks)
    {
        var s = new OutBinStream();
        s.WriteShort(0);                    // s16 — type 0 = classement
        s.WriteShort((short)ranks.Count);   // u16 — nombre d'entrées
        foreach (var (name, energy) in ranks)
        {
            s.WriteString(name);            // str — nom de famille (Big5)
            s.WriteInt(energy);             // i32 — énergie de la phase
        }
        return s.ToArray();
    }

    /// <summary>561 type 1 — phase courante de la bataille de famille
    /// du joueur : `[s16 1][i32 phase]` (phase = battle+4 natif,
    /// 0 = pas de bataille active pour cette famille).</summary>
    public static byte[] FamilyBattlePhase(int phase)
    {
        var s = new OutBinStream();
        s.WriteShort(1);                    // s16 — type 1 = phase join
        s.WriteInt(phase);                  // i32 — phase de la battle
        return s.ToArray();
    }

    /// <summary>561 type 3 — état des 3 camps du match :
    /// `[s16 3][i32 3]` puis 3 triplets `[i32][s16][i32]` (format
    /// natif Serialize @0x81e8580 — émis après résolution d'un round).
    /// </summary>
    public static byte[] FamilyBattleCamps(
        IReadOnlyList<(int A, short B, int C)> camps)
    {
        var s = new OutBinStream();
        s.WriteShort(3);                    // s16 — type 3 = camps
        s.WriteInt(3);                      // i32 — littéral natif 3
        foreach (var (a, b, c) in camps)
        {
            s.WriteInt(a);                  // i32 — champ camp A
            s.WriteShort(b);                // s16 — champ camp B
            s.WriteInt(c);                  // i32 — champ camp C
        }
        return s.ToArray();
    }

    /// <summary>Une entrée de territoire pour le 515 — vecteurs
    /// parallèles (<c>CTerritory</c> : +4 id, +0x14 own_fam, +0x18 nom,
    /// +8 état, +0x24 flag ; time = +0x1c/+0x20/PrepareBattleTime
    /// selon l'état ; level = (char)fam+0x18 du propriétaire ou 0).
    /// </summary>
    public sealed record TerritoryEntry(int Id, int OwnFamilyId,
        string Name, short OwnerLevel, uint Time, short State,
        byte Flag);

    /// <summary>Une entrée de la map horaire du 515 : triplet
    /// <c>[i32 schedType][i32 begin][i32 end]</c> en epochs unix
    /// (remplie pour les types 501/502/601-603 et les guerres
    /// planifiées).</summary>
    public sealed record TerritoryScheduleEntry(int ScheduleType,
        int Begin, int End);

    /// <summary>515 — réponse complète de la requête territoire.
    /// Wire natif : [s16 0] puis les 8 vecteurs parallèles (entrées
    /// ordonnées par id croissant — map native).</summary>
    public static byte[] TerritoryQuery(
        IReadOnlyList<TerritoryEntry> entries,
        IReadOnlyList<TerritoryScheduleEntry> schedules)
    {
        var s = new OutBinStream();
        // s16 — le natif lit un local jamais initialisé ici
        // (TC_TerritoryDataQuery, [ebp-0x144]) : on émet 0.
        s.WriteShort(0);

        // u16 n + i32 ids territoire (ordre croissant de la map)
        s.WriteShort((short)entries.Count);
        foreach (var e in entries) s.WriteInt(e.Id);

        // u16 m + triplets i32 : (schedType, beginEpoch, endEpoch)
        s.WriteShort((short)schedules.Count);
        foreach (var sc in schedules)
        {
            s.WriteInt(sc.ScheduleType);    // i32 — clé schedule
            s.WriteInt(sc.Begin);           // i32 — début epoch
            s.WriteInt(sc.End);             // i32 — fin epoch
        }

        // u16 n + i32 own_family_id (0 = territoire libre)
        s.WriteShort((short)entries.Count);
        foreach (var e in entries) s.WriteInt(e.OwnFamilyId);

        // u16 n + str noms (Big5, depuis S_Territory)
        s.WriteShort((short)entries.Count);
        foreach (var e in entries) s.WriteString(e.Name);

        // u16 n + s16 niveau de la famille propriétaire (0 si libre)
        s.WriteShort((short)entries.Count);
        foreach (var e in entries) s.WriteShort(e.OwnerLevel);

        // u16 n + u32 time (début/fin/prepare selon l'état natif)
        s.WriteShort((short)entries.Count);
        foreach (var e in entries) s.WriteInt((int)e.Time);

        // u16 n + s16 état du territoire (CTerritory+8)
        s.WriteShort((short)entries.Count);
        foreach (var e in entries) s.WriteShort(e.State);

        // u16 n + u8 flag divers (CTerritory+0x24 ; pour les entrées
        // versus = Versus+0xc==id, pour BR = ouvert)
        s.WriteShort((short)entries.Count);
        foreach (var e in entries) s.WriteByte(e.Flag);

        return s.ToArray();
    }
}
