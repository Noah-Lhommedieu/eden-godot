using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages « succès / titres / calendrier de connexion » du
// ZoneServer — lot Vague 1-D (Phase 6).
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Achievement.cs —
//     wire 480 commenté d'après le Serialize natif zone @0x08209080.
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Titles.cs — NCIDs
//     93/94 C→S et 396/397/398 S→C (CPlayerFactory::
//     SetPresentAppellation @0x080f2400, AddAppellation @0x08103a40).
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.DailyAward.cs +
//     Codecs/DailyAwardPackets.cs — 258 C→S (GOLDEN) et corps 615
//     de 26 octets (Serialize natif @0x81e9650, la classe générée
//     aplatie 16B est incorrecte d'où le codec dédié).
//   - docs/generated/ncid_map.json pour les numéros NCID.

// ------------------------- S→C : succès -------------------------

/// <summary>S→C NCID 480 — ClientAchievementUpdate.
/// <c>[u8 kind]</c> puis :
///   kinds 0/1 → <c>[u16 n][i32 achievementId][u32 points]×n</c>
///     (0 = synchro initiale des succès COMPLÉTÉS — paquets de 315
///     envoyés au login par SendAllAchievement ; 1 = upsert {id→points
///     cumulés} à chaque grant notifié) ;
///   kinds 2/3 → <c>[u32 total]</c> (cumuls catégoriels — 2 = groupes
///     101-108/201-202 « points chasse », 3 = groupes 501-506 « points
///     ville », <c>CumulativeAchievement</c> natif) ;
///   kinds 4/5 → <c>[u16 n][i32 id]×n</c> (succès « town » mensuels /
///     liste d'ids divers — non émis par le serveur C# actuel) ;
///   kind 6 (EXTENSION Eden, absente du natif) → même corps que 0/1 mais
///     <c>[i32 id][u32 success_num]</c> : compteurs des succès non
///     complétés (colonne success_num de la base).</summary>
public sealed class ZoneAchievementUpdate : NetMessage
{
    public override int Opcode => 480;
    public override string Name => "Zone.ClientAchievementUpdate";

    /// <summary>u8 — type de trame (0/1 paires, 2/3 total, 4/5 ids).</summary>
    public byte Kind { get; set; }

    /// <summary>Entrées {achievementId, points cumulés} — kinds 0/1 ;
    /// {id, success_num} pour le kind 6.</summary>
    public List<(int Id, uint Points)> Entries { get; } = new();

    /// <summary>Total cumulé — kinds 2/3.</summary>
    public uint Total { get; set; }

    /// <summary>Ids seuls — kinds 4/5.</summary>
    public List<int> Ids { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Kind);
        if (Kind == 0 || Kind == 1 || Kind == 6)
        {
            s.WriteShort((short)Entries.Count);
            foreach (var (id, pts) in Entries)
            {
                s.WriteInt(id);
                s.WriteUInt(pts);
            }
        }
        else if (Kind == 2 || Kind == 3)
        {
            s.WriteUInt(Total);
        }
        else if (Kind == 4 || Kind == 5)
        {
            s.WriteShort((short)Ids.Count);
            foreach (var id in Ids)
                s.WriteInt(id);
        }
    }

    public override void Deserialize(BinReader s)
    {
        Kind = s.ReadByte();
        Entries.Clear();
        Ids.Clear();
        if (Kind == 0 || Kind == 1 || Kind == 6)
        {
            var count = s.ReadUShort();
            for (var i = 0; i < count; i++)
                Entries.Add((s.ReadInt(), s.ReadUInt()));
        }
        else if (Kind == 2 || Kind == 3)
        {
            Total = s.ReadUInt();
        }
        else if (Kind == 4 || Kind == 5)
        {
            var count = s.ReadUShort();
            for (var i = 0; i < count; i++)
                Ids.Add(s.ReadInt());
        }
    }
}

// ------------------------- S→C / C→S : titres (appellations) ------

/// <summary>S→C NCID 396 — ClientChangeAppellation : titre actif d'un
/// personnage visible, broadcast aux voyants (natif
/// BroadCastToCanSee). <c>[i32 charId][i32 titleId][u8 seq]</c>.</summary>
public sealed class ZoneChangeAppellation : NetMessage
{
    public override int Opcode => 396;
    public override string Name => "Zone.ClientChangeAppellation";
    /// <summary>i32 — charId du personnage dont le titre change.</summary>
    public int CharId { get; set; }
    /// <summary>i32 — nouveau titre (0 = retiré).</summary>
    public int TitleId { get; set; }
    /// <summary>u8 — séquence (char+0x4df natif).</summary>
    public byte Seq { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteInt(TitleId);
        s.WriteByte(Seq);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        TitleId = s.ReadInt();
        Seq = s.ReadByte();
    }
}

/// <summary>S→C NCID 397 — ClientUpdateAppellation : titre actif du
/// joueur (NotifyClient après un 93 accepté). <c>[i32 titleId]</c>.</summary>
public sealed class ZoneUpdateAppellation : NetMessage
{
    public override int Opcode => 397;
    public override string Name => "Zone.ClientUpdateAppellation";
    /// <summary>i32 — titre actif (0 = aucun).</summary>
    public int TitleId { get; set; }

    public override void Serialize(BinWriter s) => s.WriteInt(TitleId);
    public override void Deserialize(BinReader s) =>
        TitleId = s.ReadInt();
}

/// <summary>S→C NCID 398 — ClientAddAppellation : titre nouvellement
/// obtenu (INSERT player_appellation natif — quête, succès, récompense).
/// <c>[i32 titleId]</c>.</summary>
public sealed class ZoneAddAppellation : NetMessage
{
    public override int Opcode => 398;
    public override string Name => "Zone.ClientAddAppellation";
    /// <summary>i32 — titre ajouté à la liste des possédés.</summary>
    public int TitleId { get; set; }

    public override void Serialize(BinWriter s) => s.WriteInt(TitleId);
    public override void Deserialize(BinReader s) =>
        TitleId = s.ReadInt();
}

/// <summary>C→S NCID 93 — ZoneServerUpdateAppellation : pose ou retire
/// le titre actif. <c>[u16 titleId]</c> (0 = retirer ; le serveur
/// rejette silencieusement les ids non possédés —
/// HandleSetAppellationAsync, natif SetPresentAppellation @0x080f2400).</summary>
public sealed class ZoneSetAppellation : NetMessage
{
    public override int Opcode => 93;
    public override string Name => "Zone.ZoneServerUpdateAppellation";
    /// <summary>u16 — titre à activer (0 = retirer).</summary>
    public ushort TitleId { get; set; }

    public override void Serialize(BinWriter s) =>
        s.WriteUShort(TitleId);
    public override void Deserialize(BinReader s) =>
        TitleId = s.ReadUShort();
}

/// <summary>C→S NCID 94 — ZoneServerQueryAppellation : demande le
/// titre actif d'un personnage du même node. <c>[u32 charId]</c> →
/// réponse 396 + 280 attr 0x2c (HandleQueryAppellationAsync).</summary>
public sealed class ZoneQueryAppellation : NetMessage
{
    public override int Opcode => 94;
    public override string Name => "Zone.ZoneServerQueryAppellation";
    /// <summary>u32 — charId cible.</summary>
    public uint CharId { get; set; }

    public override void Serialize(BinWriter s) =>
        s.WriteUInt(CharId);
    public override void Deserialize(BinReader s) =>
        CharId = s.ReadUInt();
}

// ------------------------- daily award ---------------------------

// NCID 258 DailyAwardOP : codec déjà présent dans ZoneMessages.cs
// (ZoneDailyAwardOp, zsniff `0201 0000 02000000` — [s16 op][u32
// param], Field1 = op : 0 check de jour, 1 requête d'état → 615
// type 1, 2 claim du jour Field2 → 615 type 2 ; Field1=0/Field2=2 →
// réponse 615 type 5). Ne pas redéfinir ici (collision CS0101).

/// <summary>S→C NCID 615 — ClientUpdateDailyAwards, corps dédié de
/// 26 octets (DailyAwardPackets.cs serveur, Serialize natif @0x81e9650) :
/// <c>[s16 dayIndex][i32 onlineSeconds][i64 awardDays][i64 awardRecords]
/// [s16 type][s16 flag]</c>. type : 0/5 réponse au check de jour,
/// 1 = état courant, 2 = récompense réclamée, 4 = système désactivé.</summary>
public sealed class ZoneUpdateDailyAwards : NetMessage
{
    public override int Opcode => 615;
    public override string Name => "Zone.ClientUpdateDailyAwards";

    /// <summary>s16 — index du jour courant (0-based, borne 0x1c).</summary>
    public short DayIndex { get; set; }
    /// <summary>i32 — temps en ligne cumulé (cooldown + delta session).</summary>
    public int OnlineSeconds { get; set; }
    /// <summary>i64 — bitmask des jours complétés (bit jour-1).</summary>
    public long AwardDays { get; set; }
    /// <summary>i64 — bitmask des jours déjà réclamés (bit jour-1).</summary>
    public long AwardRecords { get; set; }
    /// <summary>s16 — type de trame (0/5/1/2/4).</summary>
    public short Kind { get; set; }
    /// <summary>s16 — flag +0x1e natif (statut login du jour).</summary>
    public short Flag { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(DayIndex);
        s.WriteInt(OnlineSeconds);
        s.WriteLong(AwardDays);
        s.WriteLong(AwardRecords);
        s.WriteShort(Kind);
        s.WriteShort(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        DayIndex = s.ReadShort();
        OnlineSeconds = s.ReadInt();
        AwardDays = s.ReadLong();
        AwardRecords = s.ReadLong();
        Kind = s.ReadShort();
        Flag = s.ReadShort();
    }
}
