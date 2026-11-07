using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages sociaux — party/team (zone) + amis/famille (monde).
//
// Sources de preuve (détails dans docs/archive/temp-plans/2026-10-25-s9-s10-*) :
//   - C→S zone team : désasm Deserialize natif du binaire zone —
//     29 TeamInvite @0x8290780 `[u32][str][u8][u8][s16]` (Execute
//     @0x8290400 : charId=0 → lookup par nom — mais le serveur C#
//     TeamHandlers.cs:22 exige le charId de la cible),
//     30 TeamInviteAnswer @0x82922d0 `[s16 type][i32 inviter][u8][s16]`
//     (type=1 → accept, résolution par paire inviter/cible),
//     28 TeamLeave @0x8290fe0 `[u32]`, 31 TeamInviteCancel vide.
//   - S→C zone team : ZoneMessages.g.cs serveur + code émetteur
//     (Handlers/TeamHandlers.cs, ZoneEntryFlow.Team.cs) —
//     324 = BroadcastTeamInfo (flags bit0=leader, M2=classId,
//     M3=mapId, M4=online ; le dernier i32 de l'entrée = HP courant).
//   - S→C/C→S monde amis : server/…/Game/Social/SocialPackets.cs
//     (désasm world @0x80cc120) — 52 chunké à 10, record 13 champs.
//   - Guilde = « famille » native : 50 QueryFamilyInfo reply
//     (FamilyPackets.cs, désasm Serialize @0x80ceb10) — type<2 →
//     [u16 worldId][i32 famId][i32 beCreateTime][str name]… ;
//     type=2 → pas de famille.

// ============================================================
// Zone C→S — team
// ============================================================

/// <summary>C→S NCID 28 — TeamLeave `[u32 charId]` : quitter l'équipe
/// (le serveur C# ignore la valeur et fait partir l'émetteur ;
/// natif : kick si leader). Désasm Deserialize @0x8290fe0.</summary>
public sealed class ZoneTeamLeave : NetMessage
{
    public override int Opcode => 28;
    public override string Name => "Zone.ZoneServerTeamLeave";
    /// <summary>u32 — charId (self ; kick = id du membre).</summary>
    public uint CharId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt((int)CharId);
    public override void Deserialize(BinReader s) => CharId = s.ReadUInt();
}

/// <summary>C→S NCID 29 — TeamInvite
/// `[u32 charId][str name][u8 level][u8 classId][s16 flags]`.
/// Désasm Deserialize @0x8290780. Le serveur C# résout la cible par
/// CharId (le nom est informatif — rempli depuis l'entité visée).</summary>
public sealed class ZoneTeamInvite : NetMessage
{
    public override int Opcode => 29;
    public override string Name => "Zone.ZoneServerTeamInvite";
    /// <summary>u32 — charId de la cible (requis côté serveur C#).</summary>
    public uint CharId { get; set; }
    /// <summary>str — nom de la cible (le natif s'en sert si id=0).</summary>
    public string PlayerName { get; set; } = "";
    /// <summary>u8 — niveau de la cible (informatif).</summary>
    public byte Level { get; set; }
    /// <summary>u8 — classe de la cible (informatif).</summary>
    public byte ClassId { get; set; }
    /// <summary>s16 — flags natifs (0 observé).</summary>
    public short Flags { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);
        s.WriteString(PlayerName);
        s.WriteByte(Level);
        s.WriteByte(ClassId);
        s.WriteShort(Flags);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadUInt();
        PlayerName = s.ReadString();
        Level = s.ReadByte();
        ClassId = s.ReadByte();
        Flags = s.ReadShort();
    }
}

/// <summary>C→S NCID 30 — TeamInviteAnswer
/// `[s16 type][i32 inviterId][u8 accepted][s16]` — désasm @0x82922d0 :
/// +0x12 (1er s16) = type de réponse (1 = accept), +0x14 = charId de
/// l'inviteur, +0x18 = bool. Pas d'id d'invitation : la résolution se
/// fait par paire (inviteur, cible) — TeamHandlers.cs:39-49.</summary>
public sealed class ZoneTeamInviteAnswer : NetMessage
{
    public override int Opcode => 30;
    public override string Name => "Zone.ZoneServerTeamInviteAnswer";
    /// <summary>s16 — type de réponse (1 = accept, 0 = decline).</summary>
    public short ReplyType { get; set; }
    /// <summary>i32 — charId de l'inviteur (champ +0x14 du 318).</summary>
    public int InviterId { get; set; }
    /// <summary>u8 — bool natif (redondant avec ReplyType).</summary>
    public byte Accepted { get; set; }
    /// <summary>s16 — suffixe (0).</summary>
    public short Tail { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(ReplyType);
        s.WriteInt(InviterId);
        s.WriteByte(Accepted);
        s.WriteShort(Tail);
    }
    public override void Deserialize(BinReader s)
    {
        ReplyType = s.ReadShort();
        InviterId = s.ReadInt();
        Accepted = s.ReadByte();
        Tail = s.ReadShort();
    }
}

/// <summary>C→S NCID 31 — TeamInviteCancel (vide) : annule l'invitation
/// sortante pendante — désasm natif : aucun champ.</summary>
public sealed class ZoneTeamInviteCancel : NetMessage
{
    public override int Opcode => 31;
    public override string Name => "Zone.ZoneServerTeamInviteCancel";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 86 — TeamRadarAttention `[s16 type][s16 x]
/// [s16 z]` : ping radar d'équipe. Le serveur rebroadcaste les 3 s16
/// tel quel en 387 aux membres du même node (MiscHandlers.cs:682,
/// désasm natif CNE_CZ_ZoneServerTeamRadarAttention::Execute).</summary>
public sealed class ZoneTeamRadarAttention : NetMessage
{
    public override int Opcode => 86;
    public override string Name => "Zone.ZoneServerTeamRadarAttention";
    /// <summary>s16 — type de ping (1 = attention point).</summary>
    public short Kind { get; set; }
    /// <summary>s16 — coord x monde Eden.</summary>
    public short X { get; set; }
    /// <summary>s16 — coord z monde Eden.</summary>
    public short Z { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Kind); s.WriteShort(X); s.WriteShort(Z);
    }
    public override void Deserialize(BinReader s)
    {
        Kind = s.ReadShort(); X = s.ReadShort(); Z = s.ReadShort();
    }
}

/// <summary>C→S NCID 90 — TeamChangeLeader `[i32 charId]` : transfert
/// du lead à un membre en ligne (TeamHandlers.cs, champ Value du
/// codec serveur CNE_CZ_ZoneServerTeamChangeLeader).</summary>
public sealed class ZoneTeamChangeLeader : NetMessage
{
    public override int Opcode => 90;
    public override string Name => "Zone.ZoneServerTeamChangeLeader";
    /// <summary>i32 — charId du nouveau leader (doit être membre).</summary>
    public int CharId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(CharId);
    public override void Deserialize(BinReader s) => CharId = s.ReadInt();
}

// ============================================================
// Zone S→C — team
// ============================================================

/// <summary>S→C NCID 317 — ClientTeamLeaved `[s16][u8]` : diffusé à
/// tous les membres quand l'un part (le serveur C# émet 0,0 —
/// TeamHandlers.cs:70). Sémantique retenue : le roster est vidé (le
/// 324 suivant le repeuple si on est encore groupé).</summary>
public sealed class ZoneTeamLeaved : NetMessage
{
    public override int Opcode => 317;
    public override string Name => "Zone.ClientTeamLeaved";
    public short Field1 { get; set; }
    public byte Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1); s.WriteByte(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort(); Field2 = s.ReadByte();
    }
}

/// <summary>S→C NCID 318 — ClientTeamInviteNotice
/// `[i32 inviterCharId][str inviterName][s16×4]` — rempli par le
/// serveur avec (inviteur, nom) ; les 4 s16 sont non documentés
/// (.g.cs Field3..6 — niveau/classe supposés).</summary>
public sealed class ZoneTeamInviteNotice : NetMessage
{
    public override int Opcode => 318;
    public override string Name => "Zone.ClientTeamInviteNotice";
    /// <summary>i32 — charId de l'inviteur (renvoyé dans le 30).</summary>
    public int InviterId { get; set; }
    /// <summary>str — nom de l'inviteur affiché dans le popup.</summary>
    public string InviterName { get; set; } = "";
    public short F3 { get; set; }
    public short F4 { get; set; }
    public short F5 { get; set; }
    public short F6 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(InviterId);
        s.WriteString(InviterName);
        s.WriteShort(F3); s.WriteShort(F4); s.WriteShort(F5);
        s.WriteShort(F6);
    }
    public override void Deserialize(BinReader s)
    {
        InviterId = s.ReadInt();
        InviterName = s.ReadString();
        F3 = s.ReadShort(); F4 = s.ReadShort(); F5 = s.ReadShort();
        F6 = s.ReadShort();
    }
}

/// <summary>S→C NCID 319 — ClientTeamInviteResult
/// `[str][str][s16][u8]` — résultat d'une invite (noms + code).
/// Affiché en message système (champs non sémantifiés).</summary>
public sealed class ZoneTeamInviteResult : NetMessage
{
    public override int Opcode => 319;
    public override string Name => "Zone.ClientTeamInviteResult";
    public string F1 { get; set; } = "";
    public string F2 { get; set; } = "";
    public short F3 { get; set; }
    public byte F4 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteString(F1); s.WriteString(F2);
        s.WriteShort(F3); s.WriteByte(F4);
    }
    public override void Deserialize(BinReader s)
    {
        F1 = s.ReadString(); F2 = s.ReadString();
        F3 = s.ReadShort(); F4 = s.ReadByte();
    }
}

/// <summary>Un membre d'équipe — entrée des trames 324/325
/// (`ClientTeamAllMembersInfo`/`ClientTeamNewMemberInfo`).</summary>
public sealed class TeamMember
{
    /// <summary>s16 — index d'affichage dans le roster.</summary>
    public short Slot;
    /// <summary>s16 — bit0 = leader (BroadcastTeamInfo serveur).</summary>
    public short Flags;
    /// <summary>u32 — charId du membre.</summary>
    public uint CharId;
    /// <summary>str — nom du personnage.</summary>
    public string CharName = "";
    /// <summary>i64 — exp courante (champ natif non renommé).</summary>
    public long Exp;
    /// <summary>i64 — exp max (idem).</summary>
    public long ExpMax;
    /// <summary>i32 — niveau.</summary>
    public int Level;
    /// <summary>i32 — HP courant (pas de max prouvé — la barre du HUD
    /// est relative au max observé, voir <see cref="PartyState"/>).</summary>
    public int Hp;
    /// <summary>s16 — non identifié (0).</summary>
    public short M1;
    /// <summary>s16 — classId (TeamHandlers.cs:376).</summary>
    public short ClassId;
    /// <summary>s16 — mapId/node du membre (TeamHandlers.cs:377).</summary>
    public short MapId;
    /// <summary>u16 — non identifié (0).</summary>
    public ushort U1;
    /// <summary>s16 — online (1) / offline (0) — TeamHandlers.cs:378.</summary>
    public short Online;
    /// <summary>str — nom de guilde du membre.</summary>
    public string GuildName = "";
}

/// <summary>S→C NCID 324 — ClientTeamAllMembersInfo : composition
/// complète `[s16 n][n×entrée][s16 teamMode]` — DESASM
/// (NCIDS.md) + remplissage TeamHandlers.BroadcastTeamInfo.</summary>
public sealed class ZoneTeamAllMembersInfo : NetMessage
{
    public override int Opcode => 324;
    public override string Name => "Zone.ClientTeamAllMembersInfo";
    /// <summary>Membres dans l'ordre du wire.</summary>
    public List<TeamMember> Members { get; } = new();
    /// <summary>s16 — règle de loot (1-3, textes 208/207/2156).</summary>
    public short TeamMode { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Members.Count);
        foreach (var e in Members) WriteMember(s, e);
        s.WriteShort(TeamMode);
    }
    public override void Deserialize(BinReader s)
    {
        Members.Clear();
        var n = s.ReadShort();
        for (var i = 0; i < n; i++) Members.Add(ReadMember(s));
        TeamMode = s.Remaining >= 2 ? s.ReadShort() : (short)0;
    }

    /// <summary>Record membre partagé 324/325 (ordre wire natif).</summary>
    internal static void WriteMember(BinWriter s, TeamMember e)
    {
        s.WriteShort(e.Slot);
        s.WriteShort(e.Flags);
        s.WriteInt((int)e.CharId);
        s.WriteString(e.CharName);
        s.WriteLong(e.Exp);
        s.WriteLong(e.ExpMax);
        s.WriteInt(e.Level);
        s.WriteInt(e.Hp);
        s.WriteShort(e.M1);
        s.WriteShort(e.ClassId);
        s.WriteShort(e.MapId);
        s.WriteShort((short)e.U1);
        s.WriteShort(e.Online);
        s.WriteString(e.GuildName);
    }

    /// <summary>Parse un record membre (tolérant aux fins de
    /// chaîne tronquées — marque le membre offline).</summary>
    internal static TeamMember ReadMember(BinReader s)
    {
        var e = new TeamMember
        {
            Slot = s.ReadShort(),
            Flags = s.ReadShort(),
            CharId = s.ReadUInt(),
            CharName = s.ReadString(),
            Exp = s.ReadLong(),
            ExpMax = s.ReadLong(),
            Level = s.ReadInt(),
            Hp = s.ReadInt(),
            M1 = s.ReadShort(),
            ClassId = s.ReadShort(),
            MapId = s.ReadShort(),
            U1 = s.ReadUShort(),
            Online = s.ReadShort(),
            GuildName = s.Remaining >= 2 ? s.ReadString() : "",
        };
        return e;
    }
}

/// <summary>S→C NCID 325 — ClientTeamNewMemberInfo : un membre rejoint
/// — même layout de record que 324 (.g.cs : mêmes types C++ à un i64→i32
/// près sur les champs 5-6 ; le serveur C# n'émet que le 324, le 325
/// est toléré best-effort).</summary>
public sealed class ZoneTeamNewMemberInfo : NetMessage
{
    public override int Opcode => 325;
    public override string Name => "Zone.ClientTeamNewMemberInfo";
    /// <summary>Membre reçu (parsé avec le lecteur du 324 — les deux
    /// i64 sont lus i64 ; si le natif émet i32×2 le parse résiduel
    /// est simplement décalé, le champ restant str le tolère).</summary>
    public TeamMember Member { get; private set; } = new();
    public override void Serialize(BinWriter s) =>
        ZoneTeamAllMembersInfo.WriteMember(s, Member);
    public override void Deserialize(BinReader s) =>
        Member = ZoneTeamAllMembersInfo.ReadMember(s);
}

/// <summary>S→C NCID 381 — ClientTeamInfo `[s16 type][s16 valeur]` :
/// mise à jour d'un champ de l'équipe (9 = loot rule, 10 = roll rule
/// — NotifyTeamInfoAsync serveur, désasm RecvWorldTeamInfo).</summary>
public sealed class ZoneTeamInfo : NetMessage
{
    public override int Opcode => 381;
    public override string Name => "Zone.ClientTeamInfo";
    /// <summary>s16 — champ modifié (9=loot rule, 10=roll rule).</summary>
    public short Kind { get; set; }
    /// <summary>s16 — nouvelle valeur.</summary>
    public short Value { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Kind); s.WriteShort(Value);
    }
    public override void Deserialize(BinReader s)
    {
        Kind = s.ReadShort(); Value = s.ReadShort();
    }
}

/// <summary>S→C NCID 392 — ClientTeamMemberPosition
/// `[i32 charId][s16 nodeId][f32 x][f32 z]` — une trame par coéquipier
/// migré (ZoneEntryFlow.Team.cs, miroir NotifyTeamMemberPos natif
/// @0x80fc7c0). Positions monde Eden.</summary>
public sealed class ZoneTeamMemberPosition : NetMessage
{
    public override int Opcode => 392;
    public override string Name => "Zone.ClientTeamMemberPosition";
    /// <summary>i32 — charId du membre.</summary>
    public int CharId { get; set; }
    /// <summary>s16 — node où il se trouve.</summary>
    public short NodeId { get; set; }
    /// <summary>f32 — position x Eden.</summary>
    public float X { get; set; }
    /// <summary>f32 — position z/y Eden.</summary>
    public float Y { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteShort(NodeId);
        s.WriteFloat(X);
        s.WriteFloat(Y);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        NodeId = s.ReadShort();
        X = s.ReadFloat();
        Y = s.ReadFloat();
    }
}

// ============================================================
// Monde C→S — amis
// ============================================================

/// <summary>C→S monde NCID 12 — ServerAddFriend `[str name][s16 type]`
/// : type 1 = ajouter en ami, 0 = blacklist (FriendHandlers.cs:52 —
/// TC_AddFriend natif).</summary>
public sealed class WorldAddFriend : NetMessage
{
    public override int Opcode => 12;
    public override string Name => "World.ServerAddFriend";
    /// <summary>str — nom du personnage visé.</summary>
    public string TargetName { get; set; } = "";
    /// <summary>s16 — 1 = ami, 0 = blacklist.</summary>
    public short FriendType { get; set; } = 1;
    public override void Serialize(BinWriter s)
    {
        s.WriteString(TargetName); s.WriteShort(FriendType);
    }
    public override void Deserialize(BinReader s)
    {
        TargetName = s.ReadString(); FriendType = s.ReadShort();
    }
}

/// <summary>C→S monde NCID 14 — ServerFriendInvitation
/// `[i32 fromId][s16 accept]` : réponse à une invitation d'ami
/// (FriendHandlers.cs:115 — accept ≠0 → mutuel 3/3).</summary>
public sealed class WorldFriendInvitationAnswer : NetMessage
{
    public override int Opcode => 14;
    public override string Name => "World.ServerFriendInvitation";
    /// <summary>i32 — charId de l'inviteur.</summary>
    public int InviterId { get; set; }
    /// <summary>s16 — ≠0 accepte, 0 refuse.</summary>
    public short Accept { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(InviterId); s.WriteShort(Accept);
    }
    public override void Deserialize(BinReader s)
    {
        InviterId = s.ReadInt(); Accept = s.ReadShort();
    }
}

/// <summary>C→S monde NCID 22 — ServerRemoveFriend `[i32 charId]` :
/// retire la relation bilatérale (FriendHandlers.cs).</summary>
public sealed class WorldRemoveFriend : NetMessage
{
    public override int Opcode => 22;
    public override string Name => "World.ServerRemoveFriend";
    /// <summary>i32 — charId du contact à retirer.</summary>
    public int CharId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(CharId);
    public override void Deserialize(BinReader s) => CharId = s.ReadInt();
}

// ============================================================
// Monde S→C — amis / guilde
// ============================================================

/// <summary>Un contact ami — record du paquet monde 52
/// (SocialPackets.FriendEntry, désasm Serialize @0x80cc120).</summary>
public sealed record FriendEntry(
    /// <summary>i32 — charId de l'ami.</summary>
    int CharId,
    /// <summary>str — given_name.</summary>
    string Name,
    /// <summary>s16 — friend_type : 1 ajouté, 2 m'a-ajouté, 3 mutuel.</summary>
    short FriendType,
    /// <summary>s16 — niveau (−1 si non visible).</summary>
    short Level,
    /// <summary>s16 — node courant (−1 inconnu).</summary>
    short NodeId,
    /// <summary>s16 — race_id.</summary>
    short RaceId,
    /// <summary>s16 — class_id.</summary>
    short ClassId,
    /// <summary>s16 — class_lv.</summary>
    short ClassLv,
    /// <summary>str — nom de guilde (family).</summary>
    string FamilyName,
    /// <summary>u8 — en ligne (1/0).</summary>
    byte Online,
    /// <summary>u16 — serveur (0xffff offline).</summary>
    ushort ServerId,
    /// <summary>u8 — état brut (0xff = offline/absent).</summary>
    byte State,
    /// <summary>u8 — carte publique.</summary>
    byte IsPublic);

/// <summary>S→C monde NCID 52 — ClientUpdateFriendMember :
/// `[u16 worldServerId][s16 n][n×record]` — liste complète chunkée à
/// 10 (le client concatène les chunks).</summary>
public sealed class WorldUpdateFriendMember : NetMessage
{
    public override int Opcode => 52;
    public override string Name => "World.ClientUpdateFriendMember";
    /// <summary>u16 — id du monde émetteur (constante serveur).</summary>
    public ushort WorldServerId { get; set; }
    /// <summary>Records de CE chunk.</summary>
    public List<FriendEntry> Entries { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)WorldServerId);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) WriteEntry(s, e);
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        WorldServerId = s.ReadUShort();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            Entries.Add(ReadEntry(s));
    }

    private static void WriteEntry(BinWriter s, FriendEntry e)
    {
        s.WriteInt(e.CharId);
        s.WriteString(e.Name);
        s.WriteShort(e.FriendType);
        s.WriteShort(e.Level);
        s.WriteShort(e.NodeId);
        s.WriteShort(e.RaceId);
        s.WriteShort(e.ClassId);
        s.WriteShort(e.ClassLv);
        s.WriteString(e.FamilyName);
        s.WriteByte(e.Online);
        s.WriteShort((short)e.ServerId);
        s.WriteByte(e.State);
        s.WriteByte(e.IsPublic);
    }

    private static FriendEntry ReadEntry(BinReader s) => new(
        CharId: s.ReadInt(),
        Name: s.ReadString(),
        FriendType: s.ReadShort(),
        Level: s.ReadShort(),
        NodeId: s.ReadShort(),
        RaceId: s.ReadShort(),
        ClassId: s.ReadShort(),
        ClassLv: s.ReadShort(),
        FamilyName: s.ReadString(),
        Online: s.ReadByte(),
        ServerId: s.ReadUShort(),
        State: s.ReadByte(),
        IsPublic: s.ReadByte());
}

/// <summary>S→C monde NCID 61 — ClientUpdateBlackList :
/// `[u8 flag][s16 n][n×(i32 charId, str name)]` (SocialPackets —
/// désasm world ; le flag natif vaut 1).</summary>
public sealed class WorldUpdateBlackList : NetMessage
{
    public override int Opcode => 61;
    public override string Name => "World.ClientUpdateBlackList";
    /// <summary>u8 — flag natif (1).</summary>
    public byte Flag { get; set; }
    /// <summary>Entrées (charId, nom).</summary>
    public List<(int CharId, string Name)> Entries { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Flag);
        s.WriteShort((short)Entries.Count);
        foreach (var (id, name) in Entries)
        {
            s.WriteInt(id);
            s.WriteString(name);
        }
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        Flag = s.ReadByte();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            Entries.Add((s.ReadInt(), s.ReadString()));
    }
}

/// <summary>S→C monde NCID 53 — ClientFriendInvitation
/// `[i32][str]` — notification d'invitation (non émise par le
/// serveur C# ; tolérée pour compat).</summary>
public sealed class WorldFriendInvitationNotice : NetMessage
{
    public override int Opcode => 53;
    public override string Name => "World.ClientFriendInvitation";
    /// <summary>i32 — champ natif (0 chez FamilyInvitation).</summary>
    public int F1 { get; set; }
    /// <summary>str — nom de l'inviteur.</summary>
    public string InviterName { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(F1); s.WriteString(InviterName);
    }
    public override void Deserialize(BinReader s)
    {
        F1 = s.ReadInt(); InviterName = s.ReadString();
    }
}

/// <summary>S→C monde NCID 50 — ClientQueryFamilyInfo : résumé de la
/// guilde (« famille » native). Format prouvé par
/// FamilyPackets.QueryFamilyInfo (désasm Serialize @0x80ceb10 +
/// Execute @0x80e7480) :
/// `[u8 type]` — type 2 = pas de famille ; type 5 `[i32 treasury]` ;
/// type 6 `[i32 elfking]` ; type 7 `[s16 spell]` ; sinon
/// `[u16 worldId]` (sauf type 8), puis pour type&lt;2 le bloc famille
/// de 19 champs (voir <see cref="GuildState"/> pour la sémantique),
/// puis `[u16 n]` + records membres si type=8, puis titres si
/// type∈{0,3} (`[u16 n][n×str]`), puis privilèges si type∈{0,4}
/// (`[u16 n][n×i32]`). Parse tolérant aux tronquations.</summary>
public sealed class WorldFamilyInfo : NetMessage
{
    public override int Opcode => 50;
    public override string Name => "World.ClientQueryFamilyInfo";
    /// <summary>u8 — sous-type natif (0=résumé complet, 2=absente,
    /// 3=titres, 4=privilèges, 5=treasury, 8=chunk membres).</summary>
    public byte Type { get; set; }
    /// <summary>u16 — WorldServerID natif (types ≠ 8).</summary>
    public ushort WorldServerId { get; set; }
    /// <summary>i32 — id de famille (0 si absente).</summary>
    public int FamilyId { get; set; }
    /// <summary>str — nom de guilde ("" si absente).</summary>
    public string FamilyName { get; set; } = "";
    /// <summary>u8 — niveau de guilde (bloc type&lt;2).</summary>
    public byte Level { get; set; }
    /// <summary>i32 — be_create_time (epoch).</summary>
    public int CreateTime { get; set; }
    /// <summary>i32 — exp.</summary>
    public int Exp { get; set; }
    /// <summary>i32 — leader_id.</summary>
    public int LeaderId { get; set; }
    /// <summary>i32 — exploit.</summary>
    public int Exploit { get; set; }
    /// <summary>i32 — id d'emblème.</summary>
    public int Emblem { get; set; } = -1;
    /// <summary>s16 — uniform.</summary>
    public short Uniform { get; set; }
    /// <summary>str — info/annonce publique.</summary>
    public string Info { get; set; } = "";
    /// <summary>i32 — trésorerie (bloc ou type 5).</summary>
    public int Treasury { get; set; }
    /// <summary>s16 — taxe membre.</summary>
    public short TaxRate { get; set; }
    /// <summary>s16 — taxe commerce.</summary>
    public short BusinessTax { get; set; }
    /// <summary>s16 — house_open.</summary>
    public short HouseOpen { get; set; }
    /// <summary>s16 — état recrutement (fam+0x3c).</summary>
    public short Recruit { get; set; }
    /// <summary>i32 — honor.</summary>
    public int Honor { get; set; }
    /// <summary>Membres du chunk (type 8 uniquement).</summary>
    public List<GuildMember> Members { get; } = new();
    /// <summary>Titres (types 0/3 — member/leader/cadre_2..8).</summary>
    public List<string> Titles { get; } = new();
    /// <summary>Privilèges (types 0/4 — 14 valeurs).</summary>
    public List<int> Privileges { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Type);
        switch (Type)
        {
            case 2: return;                      // famille absente
            case 5: s.WriteInt(Treasury); return;
            case 6: s.WriteInt(0); return;       // elfking_tid
            case 7: s.WriteShort(0); return;     // spell_point
        }
        if (Type != 8) s.WriteShort((short)WorldServerId); // u16 worldId
        if (Type < 2) WriteFamilyBlock(s);
        s.WriteShort((short)(Type == 8 ? Members.Count : 0)); // u16 n
        if (Type == 8)
            foreach (var m in Members)
                WorldFamilyMemberUpdate.WriteMember(s, m, queryMode: true);
        if (Type is 0 or 3)
        {
            s.WriteShort((short)Titles.Count);
            foreach (var t in Titles) s.WriteString(t);
        }
        else s.WriteShort(0);
        if (Type is 0 or 4)
        {
            s.WriteShort((short)Privileges.Count);
            foreach (var p in Privileges) s.WriteInt(p);
        }
        else s.WriteShort(0);
    }

    /// <summary>Bloc famille type&lt;2 — 19 champs dans l'ordre du
    /// Serialize natif (FamilyPackets.WriteFamilyBlock).</summary>
    private void WriteFamilyBlock(BinWriter s)
    {
        s.WriteInt(FamilyId);       // i32 — family id
        s.WriteInt(CreateTime);     // i32 — be_create_time
        s.WriteString(FamilyName);  // str — name
        s.WriteByte(Level);         // u8 — lv
        s.WriteInt(Exp);            // i32 — exp
        s.WriteInt(LeaderId);       // i32 — leader_id
        s.WriteInt(Exploit);        // i32 — exploit
        s.WriteInt(Emblem);         // i32 — emblem
        s.WriteShort(Uniform);      // s16 — uniform
        s.WriteString(Info);        // str — info
        s.WriteInt(Treasury);       // i32 — treasury
        s.WriteShort(TaxRate);      // s16 — tax_rate
        s.WriteShort(BusinessTax);  // s16 — business_tax
        s.WriteInt(0);              // i32 — elfking_tid
        s.WriteShort(0);            // s16 — spell_point
        s.WriteByte(0);             // u8 — état recrutement (fam+0x4c)
        s.WriteShort(HouseOpen);    // s16 — house_open
        s.WriteShort(Recruit);      // s16 — recruit
        s.WriteInt(Honor);          // i32 — honor
    }

    /// <summary>Parse tolérant — lit tous les champs disponibles ;
    /// les tronquations laissent les valeurs par défaut.</summary>
    public override void Deserialize(BinReader s)
    {
        Type = s.ReadByte();
        switch (Type)
        {
            case 2: return;
            case 5:
                if (s.Remaining >= 4) Treasury = s.ReadInt();
                return;
            case 6:
                if (s.Remaining >= 4) s.ReadInt();   // elfking
                return;
            case 7:
                if (s.Remaining >= 2) s.ReadShort(); // spell_point
                return;
        }
        if (Type != 8 && s.Remaining >= 2)
            WorldServerId = s.ReadUShort();
        if (Type < 2) ReadFamilyBlock(s);
        // [u16 n] membres (type 8) — 0 sinon.
        var n = s.Remaining >= 2 ? s.ReadShort() : (short)0;
        if (Type == 8)
        {
            Members.Clear();
            for (var i = 0; i < n && s.Remaining >= 4; i++)
                Members.Add(WorldFamilyMemberUpdate.ReadMember(s,
                    queryMode: true));
        }
        // Titres (types 0/3) — [u16 n][n×str].
        var nt = s.Remaining >= 2 ? s.ReadShort() : (short)0;
        Titles.Clear();
        for (var i = 0; i < nt && s.Remaining >= 2; i++)
            Titles.Add(s.ReadString());
        // Privilèges (types 0/4) — [u16 n][n×i32].
        var np = s.Remaining >= 2 ? s.ReadShort() : (short)0;
        Privileges.Clear();
        for (var i = 0; i < np && s.Remaining >= 4; i++)
            Privileges.Add(s.ReadInt());
    }

    /// <summary>Lit le bloc famille type&lt;2 (19 champs natifs) —
    /// tolérant : chaque champ n'est lu que s'il reste assez.</summary>
    private void ReadFamilyBlock(BinReader s)
    {
        if (s.Remaining < 8) return;
        FamilyId = s.ReadInt();
        CreateTime = s.ReadInt();
        FamilyName = s.Remaining >= 2 ? s.ReadString() : "";
        if (s.Remaining < 1) return;
        Level = s.ReadByte();
        if (s.Remaining < 4) return;
        Exp = s.ReadInt();
        if (s.Remaining < 4) return;
        LeaderId = s.ReadInt();
        if (s.Remaining < 4) return;
        Exploit = s.ReadInt();
        if (s.Remaining < 4) return;
        Emblem = s.ReadInt();
        if (s.Remaining < 2) return;
        Uniform = s.ReadShort();
        Info = s.Remaining >= 2 ? s.ReadString() : "";
        if (s.Remaining < 4) return;
        Treasury = s.ReadInt();
        if (s.Remaining < 4) return;
        TaxRate = s.ReadShort();
        BusinessTax = s.ReadShort();
        s.ReadInt();                              // i32 — elfking_tid
        if (s.Remaining < 2) return;
        s.ReadShort();                            // s16 — spell_point
        if (s.Remaining < 1) return;
        s.ReadByte();                             // u8 — recrut. fam+0x4c
        if (s.Remaining < 2) return;
        HouseOpen = s.ReadShort();
        if (s.Remaining < 2) return;
        Recruit = s.ReadShort();
        if (s.Remaining < 4) return;
        Honor = s.ReadInt();
    }
}

// ============================================================
// État partagé (pur — testable hors Godot)
// ============================================================

/// <summary>
/// Roster d'équipe courant — alimenté par les trames zone 324/325/317
/// et les positions 392. Pur C# : appliqué sur le thread principal par
/// <c>PartyHud</c>/<c>MinimapPanel</c>. HP max non prouvé sur le fil →
/// on retient le plus grand HP vu par membre pour la barre.
/// </summary>
public static class PartyState
{
    /// <summary>Membres par charId.</summary>
    public static readonly Dictionary<uint, TeamMember> Members = new();
    /// <summary>Positions des coéquipiers (392) : charId → (node,x,y).</summary>
    public static readonly Dictionary<int, (short Node, float X, float Y)>
        Positions = new();
    /// <summary>Max HP observé par membre (pour la barre).</summary>
    public static readonly Dictionary<uint, int> MaxHpSeen = new();
    /// <summary>s16 — teamMode/loot rule du dernier 324 ou 381.</summary>
    public static short TeamMode;
    /// <summary>s16 — roll rule (seuil qualité 0-2) du dernier 381.</summary>
    public static short RollRule;
    /// <summary>Invitation entrante pendante (318) — (inviterId, nom).</summary>
    public static (int InviterId, string Name)? PendingInvite;
    /// <summary>Invitation sortante pendante (on attend le 324/319).</summary>
    public static string OutgoingInviteName = "";
    /// <summary>Dernier ping radar 387 (kind,x,z) + nombre reçus.</summary>
    public static (short Kind, short X, short Z)? RadarPing;
    public static int RadarPingCount;
    /// <summary>Notifié à chaque mutation (thread principal).</summary>
    public static event Action? Changed;

    /// <summary>true = on est groupé.</summary>
    public static bool InTeam => Members.Count > 0;
    /// <summary>charId du leader (flags bit0), 0 si inconnu.</summary>
    public static uint LeaderId
    {
        get
        {
            foreach (var kv in Members)
                if ((kv.Value.Flags & 1) != 0) return kv.Key;
            return 0;
        }
    }

    /// <summary>324 : remplace le roster.</summary>
    public static void ApplyAll(ZoneTeamAllMembersInfo m)
    {
        Members.Clear();
        foreach (var e in m.Members)
        {
            Members[e.CharId] = e;
            if (e.Hp > 0 && (!MaxHpSeen.TryGetValue(e.CharId, out var mx)
                             || e.Hp > mx))
                MaxHpSeen[e.CharId] = e.Hp;
        }
        TeamMode = m.TeamMode;
        Changed?.Invoke();
    }

    /// <summary>325 : un membre rejoint (ou maj slot).</summary>
    public static void ApplyNew(ZoneTeamNewMemberInfo m)
    {
        Members[m.Member.CharId] = m.Member;
        Changed?.Invoke();
    }

    /// <summary>381 : un champ team (9 = loot rule, 10 = roll rule).</summary>
    public static void ApplyTeamInfo(ZoneTeamInfo m)
    {
        if (m.Kind == 9) TeamMode = m.Value;
        else if (m.Kind == 10) RollRule = m.Value;
        Changed?.Invoke();
    }

    /// <summary>317 : équipe quittée/disband — le wire ne dit pas qui
    /// (le serveur C# émet 0,0 à tous) → on vide tout.</summary>
    public static void ApplyLeaved()
    {
        Members.Clear();
        Positions.Clear();
        PendingInvite = null;
        OutgoingInviteName = "";
        RadarPing = null;
        Changed?.Invoke();
    }

    /// <summary>392 : position d'un coéquipier.</summary>
    public static void ApplyPosition(ZoneTeamMemberPosition m)
    {
        Positions[m.CharId] = (m.NodeId, m.X, m.Y);
        Changed?.Invoke();
    }

    /// <summary>387 : ping radar d'équipe (echo du 86) — codec dans
    /// ZoneMiscMessages (Field1/2/3 = type/x/z).</summary>
    public static void ApplyRadarNotice(ZoneClientTeamRadarAttention m)
    {
        RadarPing = (m.Field1, m.Field2, m.Field3);
        RadarPingCount++;
        Changed?.Invoke();
    }

    /// <summary>318 : invitation entrante.</summary>
    public static void ApplyInviteNotice(ZoneTeamInviteNotice m)
    {
        PendingInvite = (m.InviterId, m.InviterName);
        Changed?.Invoke();
    }

    /// <summary>Reset complet (déconnexion/changement de node).</summary>
    public static void Clear()
    {
        Members.Clear();
        Positions.Clear();
        MaxHpSeen.Clear();
        PendingInvite = null;
        OutgoingInviteName = "";
        RadarPing = null;
        TeamMode = 0;
        RollRule = 0;
        Changed?.Invoke();
    }
}

/// <summary>
/// État social monde — liste d'amis (52 concaténé par chunks), blacklist
/// (61), nom de guilde (50 type&lt;2 ou 324 entrée self). Pur C#.
/// </summary>
public static class SocialState
{
    /// <summary>Amis par charId (friend_type 1/2/3).</summary>
    public static readonly Dictionary<int, FriendEntry> Friends = new();
    /// <summary>Blacklist par charId.</summary>
    public static readonly Dictionary<int, string> BlackList = new();
    /// <summary>Nom de guilde du perso ("" = aucune/inconnue).</summary>
    public static string GuildName = "";
    /// <summary>Niveau de guilde (0 inconnu).</summary>
    public static int GuildLevel;
    /// <summary>Invitation d'ami entrante (53) — (inviterId, nom).</summary>
    public static (int Id, string Name)? PendingFriendInvite;
    /// <summary>Notifié à chaque mutation (thread principal).</summary>
    public static event Action? Changed;

    /// <summary>52 : fusionne le chunk reçu dans la liste (le serveur
    /// réémet la liste complète chunkée à chaque mutation — on prend
    /// le chunk comme mise à jour par charId).</summary>
    public static void ApplyFriendChunk(WorldUpdateFriendMember m)
    {
        foreach (var e in m.Entries)
            Friends[e.CharId] = e;
        Changed?.Invoke();
    }

    /// <summary>61 : remplace la blacklist.</summary>
    public static void ApplyBlackList(WorldUpdateBlackList m)
    {
        BlackList.Clear();
        foreach (var (id, name) in m.Entries) BlackList[id] = name;
        Changed?.Invoke();
    }

    /// <summary>50 : nom de guilde (type&lt;2 porte le bloc famille ;
    /// type 2 = pas de famille → vide).</summary>
    public static void ApplyFamilyInfo(WorldFamilyInfo m)
    {
        if (m.Type == 2)
        {
            GuildName = "";
            GuildLevel = 0;
        }
        else if (m.Type < 2 && m.FamilyName.Length > 0)
        {
            GuildName = m.FamilyName;
            GuildLevel = m.Level;
        }
        else return;
        Changed?.Invoke();
    }

    /// <summary>Nom de guilde depuis le roster d'équipe (324 — champ
    /// GuildName de l'entrée self).</summary>
    public static void ApplyTeamGuild(string name)
    {
        if (name.Length == 0 || name == GuildName) return;
        GuildName = name;
        Changed?.Invoke();
    }

    /// <summary>Reset complet.</summary>
    public static void Clear()
    {
        Friends.Clear();
        BlackList.Clear();
        GuildName = "";
        GuildLevel = 0;
        PendingFriendInvite = null;
        Changed?.Invoke();
    }
}

// ============================================================
// Guilde (« famille » native) — monde 9/10/73 C→S,
// 46/47/48/49/50/51/68/74/80 S→C ; zone 49/81/82/159 C→S, 346/565 S→C.
//
// Sources de preuve (toutes citées par champ) :
//   - server/src/Eden.World/Handlers/FamilyHandlers.cs (dispatch des
//     22 sous-ops du 9 + HandleQueryAsync du 10) — miroir du désasm
//     natif CNE_CW_ServerFamilyOp::Execute @ world 0x80d9ea0.
//   - server/src/Eden.Core/Game/Family/FamilyPackets.cs (builders
//     46/47/48/49/50/51/68/74/80, désasm world cité en en-tête de
//     fichier : Serialize 50 @0x80ceb10, 46 @0x80635d0, 68 @0x80cf9ac,
//     74 ctor ; 80 Serialize @0x8062d40 + rf_game Deserialize
//     @0xcd9d90).
//   - server/src/Eden.Zone/Handlers/FamilyHandlers.cs +
//     ZoneEntryFlow.Family.cs (81 → 346 ; 346 rempli par
//     EmitChangeFamilyAsync) ; SocialHandlers.cs (49 FamilyCreate,
//     159 FamilyDonateMoney, 82 SetFamilyEmblem — classes
//     ZoneMessages.g.cs CNE_CZ_ZoneServer{FamilyCreate,
//     FamilyDonateMoney, SetFamilyEmblem}).
//   - 565 ClientR25UpdateFamily : ZoneEntryFlow.Family.cs
//     EmitR25UpdateFamilyAsync (CForceTeam::R25UpdateFamily natif
//     @0x0846d160).
// ============================================================

// ---------- Monde C→S ----------

/// <summary>Sous-ops natifs du <see cref="WorldFamilyOp"/> (NCID 9) —
/// switch du handler monde (FamilyHandlers.cs:77-99) calqué sur
/// <c>CNE_CW_ServerFamilyOp::Execute @0x80d9ea0</c>.</summary>
public static class FamilyOps
{
    public const byte Invite = 0x01;          // s1 = nom de la cible
    public const byte CancelInvites = 0x02;   // — (annule mes invites)
    public const byte Accept = 0x03;          // a = charId de l'inviteur
    public const byte Reject = 0x04;          // a = charId de l'inviteur
    public const byte Quit = 0x05;            // —
    public const byte Kick = 0x06;            // a = charId cible
    public const byte SelectLeader = 0x07;    // a = charId cible
    public const byte Disband = 0x08;         // —
    public const byte EditInfo = 0x0a;        // s1 = texte d'annonce
    public const byte CadreTitle = 0x0b;      // a = index 2..8, s1 = titre
    public const byte SetCadre = 0x0c;        // a = charId, b = rang 0..7
    public const byte SetPrivilege = 0x0e;    // a = index 1..14, b = valeur
    public const byte AddMessage = 0x0f;      // s1 = titre, s2 = contenu
    public const byte DelMessage = 0x11;      // a = message_id
    public const byte ReadMessage = 0x12;     // a = état de lecture
    public const byte EditComment = 0x13;     // a = charId, s1 = commentaire
    public const byte SetTaxRate = 0x14;      // a = valeur
    public const byte SetBusinessTax = 0x15;  // a = valeur
    public const byte RefreshLevel = 0x16;    // —
    public const byte SetEmblemData = 0x46;   // s1 = blob (latin1)
    public const byte SetRecruit = 0x4e;      // a = flag
    public const byte RecruitPage = 0x4f;     // a = page (s16)
    public const byte RecruitJoin = 0x50;     // a = family id
}

/// <summary>C→S monde NCID 9 — ServerFamilyOp
/// `[u8 op][i32 a][i32 b][str s1][str s2]` : toutes les opérations de
/// guilde (invitation, kick, rangs, annonce, message-board…). Layout :
/// <c>CNE_CW_ServerFamilyOp</c> (WorldMessages.g.cs, désasm Deserialize
/// natif) ; sémantique des champs par op dans <see cref="FamilyOps"/> —
/// FamilyHandlers.cs:70-99.</summary>
public sealed class WorldFamilyOp : NetMessage
{
    public override int Opcode => 9;
    public override string Name => "World.ServerFamilyOp";
    /// <summary>u8 — sous-op (<see cref="FamilyOps"/>).</summary>
    public byte Op { get; set; }
    /// <summary>i32 — paramètre entier 1 (charId / index / page / taxe).</summary>
    public int A { get; set; }
    /// <summary>i32 — paramètre entier 2 (rang / valeur privilège).</summary>
    public int B { get; set; }
    /// <summary>str — paramètre chaîne 1 (nom, info, titre).</summary>
    public string S1 { get; set; } = "";
    /// <summary>str — paramètre chaîne 2 (contenu message-board).</summary>
    public string S2 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Op);       // u8 — sous-op
        s.WriteInt(A);         // i32 — param 1
        s.WriteInt(B);         // i32 — param 2
        s.WriteString(S1);     // str — param texte 1
        s.WriteString(S2);     // str — param texte 2
    }
    public override void Deserialize(BinReader s)
    {
        Op = s.ReadByte();
        A = s.ReadInt();
        B = s.ReadInt();
        S1 = s.ReadString();
        S2 = s.ReadString();
    }

    /// <summary>Fabrique une op simple sans paramètres (quitter,
    /// dissoudre, annuler les invites, refresh niveau).</summary>
    public static WorldFamilyOp Simple(byte op) => new() { Op = op };
    /// <summary>Fabrique une op à un paramètre entier (kick, leader,
    /// cadre, taxe, page, join…).</summary>
    public static WorldFamilyOp Of(byte op, int a, int b = 0) =>
        new() { Op = op, A = a, B = b };
    /// <summary>Fabrique une op à paramètres texte (invite, editinfo,
    /// titre cadre, message-board, commentaire).</summary>
    public static WorldFamilyOp OfStr(byte op, string s1,
        string s2 = "", int a = 0) =>
        new() { Op = op, A = a, S1 = s1, S2 = s2 };
}

/// <summary>C→S monde NCID 73 — ServerQueryFamilyEmblem
/// `[i32 familyId][i32]` : demande le blob d'emblème → réponse 74.
/// CNE_CW_ServerQueryFamilyEmblem (WorldMessages.g.cs,
/// FamilyHandlers.cs:55-61).</summary>
public sealed class WorldQueryFamilyEmblem : NetMessage
{
    public override int Opcode => 73;
    public override string Name => "World.ServerQueryFamilyEmblem";
    /// <summary>i32 — famille ciblée.</summary>
    public int FamilyId { get; set; }
    /// <summary>i32 — champ requête (renvoyé tel quel dans le 74).</summary>
    public int Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(FamilyId); s.WriteInt(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        FamilyId = s.ReadInt(); Field2 = s.ReadInt();
    }
}

// ---------- Monde S→C ----------

/// <summary>Un membre de guilde — record partagé par 46
/// (<c>ClientUpdateFamilyMember</c>) et 50-type-8
/// (<c>ClientQueryFamilyInfo</c>). Layout
/// <c>SCharacter</c> du proxy natif — FamilyPackets.WriteMemberRecord
/// (désasm Serialize 46 @0x80635d0). Différences des deux trames :
/// 46 → [race entre node/class][addFamilyTime+contribution en fin] ;
/// 50-8 → [addFamilyTime juste après charId][pas de race]
/// [contribution en fin seul]. <b>Pas de rang sur le wire</b> — le
/// rang n'est connu que pour soi (zone 346) et le chef
/// (leader_id du bloc famille).</summary>
public sealed class GuildMember
{
    /// <summary>i32 — char_id.</summary>
    public int CharId;
    /// <summary>str — given_name.</summary>
    public string Name = "";
    /// <summary>s16 — level.</summary>
    public short Level;
    /// <summary>s16 — node_id courant (0/−1 hors zone).</summary>
    public short NodeId;
    /// <summary>s16 — race_id (46 uniquement ; −1 en type 8).</summary>
    public short RaceId = -1;
    /// <summary>s16 — class_id.</summary>
    public short ClassId;
    /// <summary>s16 — class_lv.</summary>
    public short ClassLevel;
    /// <summary>u8 — en ligne.</summary>
    public byte Online;
    /// <summary>u8 — gender_id.</summary>
    public byte Gender;
    /// <summary>i32 — last_saving_time (epoch).</summary>
    public int LastSavingTime;
    /// <summary>str — self_comment.</summary>
    public string SelfComment = "";
    /// <summary>str — family_comment (note d'officier, op 0x13).</summary>
    public string FamilyComment = "";
    /// <summary>i32 — add_family_time (epoch d'adhésion).</summary>
    public int AddFamilyTime;
    /// <summary>i32 — family_contribution_point.</summary>
    public int Contribution;
    /// <summary>Rang cadre connu : non transmis par le serveur dans le
    /// record — renseigné seulement pour soi (346) / le chef
    /// (leader_id).</summary>
    public int Rank;
}

/// <summary>S→C monde NCID 46 — ClientUpdateFamilyMember : record
/// complet d'un membre (join/maj/kick — poussé à tous les membres en
/// ligne). Parse : FamilyPackets.UpdateFamilyMember (queryMode=false).
/// Sémantique serveur C# : <c>Online=0</c> = le membre a quitté
/// (LeaveFamilyAsync — pas d'émission à la simple déconnexion).</summary>
public sealed class WorldFamilyMemberUpdate : NetMessage
{
    public override int Opcode => 46;
    public override string Name => "World.ClientUpdateFamilyMember";
    /// <summary>Record du membre concerné.</summary>
    public GuildMember Member { get; private set; } = new();

    public override void Serialize(BinWriter s) => WriteMember(s, Member, queryMode: false);
    public override void Deserialize(BinReader s) =>
        Member = ReadMember(s, queryMode: false);

    /// <summary>Record 46 (queryMode=false) ou 50-type-8
    /// (queryMode=true) — voir <see cref="GuildMember"/> pour les
    /// différences de layout. Tolérant aux trames tronquées (lit ce
    /// qui reste).</summary>
    internal static void WriteMember(BinWriter s, GuildMember m,
        bool queryMode)
    {
        s.WriteInt(m.CharId);                    // i32 — char_id
        if (queryMode)
            s.WriteInt(m.AddFamilyTime);         // i32 — add_family_time (t8)
        s.WriteString(m.Name);                   // str — given_name
        s.WriteShort(m.Level);                   // s16 — level
        s.WriteShort(m.NodeId);                  // s16 — node_id
        if (!queryMode)
            s.WriteShort(m.RaceId);              // s16 — race_id (46)
        s.WriteShort(m.ClassId);                 // s16 — class_id
        s.WriteShort(m.ClassLevel);              // s16 — class_lv
        s.WriteByte(m.Online);                   // u8 — online
        s.WriteShort(0);                         // u16 — SChar+0x1a (non prouvé)
        s.WriteByte(m.Gender);                   // u8 — gender_id
        s.WriteShort(0);                         // u16 — SChar+0x1c (non prouvé)
        s.WriteInt(0);                           // i32 — SChar+0x20 (non prouvé)
        s.WriteInt(m.LastSavingTime);            // i32 — last_saving_time
        s.WriteString(m.SelfComment);            // str — self_comment
        s.WriteString(m.FamilyComment);          // str — family_comment
        if (queryMode)
            s.WriteInt(m.Contribution);          // i32 — contribution (t8)
        else
        {
            s.WriteInt(m.AddFamilyTime);         // i32 — add_family_time (46)
            s.WriteInt(m.Contribution);          // i32 — contribution (46)
        }
    }

    /// <summary>Lit un record membre — <paramref name="queryMode"/>
    /// = variante 50-type-8 (addFamilyTime en tête, pas de race,
    /// contribution seule en fin). Tolérant aux tronquations : les
    /// champs manquants restent à leur défaut.</summary>
    internal static GuildMember ReadMember(BinReader s, bool queryMode)
    {
        var m = new GuildMember();
        if (s.Remaining < 4) return m;
        m.CharId = s.ReadInt();
        if (queryMode)
        {
            if (s.Remaining < 4) return m;
            m.AddFamilyTime = s.ReadInt();
        }
        if (s.Remaining < 2) return m;
        m.Name = s.ReadString();
        if (s.Remaining < 6) return m;
        m.Level = s.ReadShort();
        m.NodeId = s.ReadShort();
        if (!queryMode)
            m.RaceId = s.ReadShort();
        m.ClassId = s.ReadShort();
        if (s.Remaining < 2) return m;
        m.ClassLevel = s.ReadShort();
        if (s.Remaining < 6) return m;
        m.Online = s.ReadByte();
        s.ReadShort();                           // u16 — SChar+0x1a ignoré
        m.Gender = s.ReadByte();
        s.ReadShort();                           // u16 — SChar+0x1c ignoré
        if (s.Remaining < 8) return m;
        s.ReadInt();                             // i32 — SChar+0x20 ignoré
        m.LastSavingTime = s.ReadInt();
        if (s.Remaining < 2) return m;
        m.SelfComment = s.ReadString();
        if (s.Remaining < 2) return m;
        m.FamilyComment = s.ReadString();
        if (queryMode)
        {
            if (s.Remaining < 4) return m;
            m.Contribution = s.ReadInt();
        }
        else
        {
            if (s.Remaining < 8) return m;
            m.AddFamilyTime = s.ReadInt();
            m.Contribution = s.ReadInt();
        }
        return m;
    }
}

/// <summary>S→C monde NCID 47 — ClientFamilyOpException `[u8 op]` :
/// la sous-op du 9 a échoué (privilège, cible, état).
/// FamilyPackets.FamilyOpException.</summary>
public sealed class WorldFamilyOpException : NetMessage
{
    public override int Opcode => 47;
    public override string Name => "World.ClientFamilyOpException";
    /// <summary>u8 — sous-op du 9 en échec.</summary>
    public byte Op { get; set; }
    public override void Serialize(BinWriter s) => s.WriteByte(Op);
    public override void Deserialize(BinReader s) => Op = s.ReadByte();
}

/// <summary>S→C monde NCID 48 — ClientFamilyInvitation
/// `[i32 inviterCharId][str inviterName][str familyName]`.
/// <b>Écart serveur</b> : le natif remplit le i32 du charId de
/// l'inviteur (ctor @0x08062070 — `*piVar10` = SCharacter.char_id,
/// émis @0x080523c6) ; le serveur C# émet 0
/// (FamilyPackets.FamilyInvitation) — l'accept (op 0x03) exige
/// pourtant l'id exact, donc l'accept est actuellement voué à
/// l'échec 47. Documenté : on renvoie le champ reçu.</summary>
public sealed class WorldFamilyInvitation : NetMessage
{
    public override int Opcode => 48;
    public override string Name => "World.ClientFamilyInvitation";
    /// <summary>i32 — charId de l'inviteur (0 côté serveur C#).</summary>
    public int InviterId { get; set; }
    /// <summary>str — nom de l'inviteur.</summary>
    public string InviterName { get; set; } = "";
    /// <summary>str — nom de la guilde.</summary>
    public string FamilyName { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(InviterId);
        s.WriteString(InviterName);
        s.WriteString(FamilyName);
    }
    public override void Deserialize(BinReader s)
    {
        InviterId = s.ReadInt();
        InviterName = s.ReadString();
        FamilyName = s.ReadString();
    }
}

/// <summary>S→C monde NCID 49 — ClientFamilyQuit (vide) : sortie
/// volontaire, kick ou dissolution → vider l'état guilde.</summary>
public sealed class WorldFamilyQuit : NetMessage
{
    public override int Opcode => 49;
    public override string Name => "World.ClientFamilyQuit";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>Message du board de guilde — entrée du 51
/// (family_msgboard + schedule partagent le vecteur natif) :
/// `[s16 messageId][i32 createDate][str title][str content]
/// [str playerName]` — FamilyPackets.FamilySchedule.</summary>
public sealed record GuildBoardMessage(
    /// <summary>s16 — message_id.</summary>
    short MessageId,
    /// <summary>i32 — create_date (epoch).</summary>
    int CreateDate,
    /// <summary>str — titre.</summary>
    string Title,
    /// <summary>str — contenu.</summary>
    string Content,
    /// <summary>str — auteur.</summary>
    string PlayerName);

/// <summary>S→C monde NCID 51 — ClientFamilySchedule : agenda +
/// message-board de guilde. <b>Wire maison</b>
/// (FamilyPackets.FamilySchedule) : `[u8 type][u16 n][n×record]
/// [u8 flag][s16 état]` — le format natif (Serialize monde
/// @0x080cd…) est par type (1=ajout 1 record+i32, 2=del s16,
/// 3=liste, 5=solo) ; on émet toujours la liste complète, Type/Flag
/// sont désérialisés mais non consommés (ApplySchedule = clear+fill).</summary>
public sealed class WorldFamilySchedule : NetMessage
{
    public override int Opcode => 51;
    public override string Name => "World.ClientFamilySchedule";
    /// <summary>u8 — type (0 = liste agenda/board).</summary>
    public byte Type { get; set; }
    /// <summary>Entrées agenda/message-board.</summary>
    public List<GuildBoardMessage> Entries { get; } = new();
    /// <summary>s16 — état natif de fin (msgboard_state).</summary>
    public short Flag { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Type);                        // u8 — type
        s.WriteShort((short)Entries.Count);       // u16 — nb entrées
        foreach (var m in Entries)
        {
            s.WriteShort(m.MessageId);            // s16 — message_id
            s.WriteInt(m.CreateDate);             // i32 — create_date
            s.WriteString(m.Title);               // str — title
            s.WriteString(m.Content);             // str — content
            s.WriteString(m.PlayerName);          // str — player_name
        }
        s.WriteByte(0);                           // u8 — flag natif (+0xe = 0)
        s.WriteShort(Flag);                       // s16 — état
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        Type = s.ReadByte();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 6; i++)
        {
            var id = s.ReadShort();
            var date = s.ReadInt();
            var title = s.ReadString();
            var content = s.Remaining >= 2 ? s.ReadString() : "";
            var player = s.Remaining >= 2 ? s.ReadString() : "";
            Entries.Add(new GuildBoardMessage(id, date, title, content,
                player));
        }
        if (s.Remaining >= 1) s.ReadByte();       // u8 — flag natif
        if (s.Remaining >= 2) Flag = s.ReadShort();
    }
}

/// <summary>Entrée du journal de guilde (68) — vecteurs parallèles
/// natifs : type d'événement + identifiant + date + paramètre
/// numérique + deux paramètres texte.</summary>
public sealed record GuildLogEntry(
    /// <summary>s16 — type d'événement (1 = join/leave natif).</summary>
    short Type,
    /// <summary>i32 — log_id.</summary>
    int LogId,
    /// <summary>i32 — date (epoch).</summary>
    int Date,
    /// <summary>i32 — paramètre (charId, montant…).</summary>
    int Para,
    /// <summary>str — paramètre texte 1 (nom…).</summary>
    string Str1,
    /// <summary>str — paramètre texte 2.</summary>
    string Str2);

/// <summary>S→C monde NCID 68 — ClientFamilyLogUpdate : journal de
/// guilde `[s16 type][s16 state]` puis <b>6 vecteurs parallèles</b>
/// chacun préfixé `[u16 n]` : (s16 log_type, i32 log_id, i32 date,
/// i32 para, str s1, str s2) — FamilyPackets.FamilyLogUpdate,
/// désasm Serialize @0x80cf9ac.</summary>
public sealed class WorldFamilyLogUpdate : NetMessage
{
    public override int Opcode => 68;
    public override string Name => "World.ClientFamilyLogUpdate";
    /// <summary>s16 — type de log.</summary>
    public short Type { get; set; }
    /// <summary>s16 — état (msgboard_state).</summary>
    public short State { get; set; }
    /// <summary>Entrées reconstruites depuis les 6 vecteurs.</summary>
    public List<GuildLogEntry> Entries { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Type);
        s.WriteShort(State);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) s.WriteShort(e.Type);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) s.WriteInt(e.LogId);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) s.WriteInt(e.Date);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) s.WriteInt(e.Para);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) s.WriteString(e.Str1);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) s.WriteString(e.Str2);
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        Type = s.ReadShort();
        State = s.ReadShort();
        // 6 vecteurs parallèles — lecture défensive : si un vecteur
        // est absent le parse s'arrête (les tronquations ne font
        // pas crasher le HUD).
        var types = ReadShortVec(s);
        var ids = ReadIntVec(s);
        var dates = ReadIntVec(s);
        var paras = ReadIntVec(s);
        var s1s = ReadStrVec(s);
        var s2s = ReadStrVec(s);
        var n = types.Count;
        for (var i = 0; i < n; i++)
            Entries.Add(new GuildLogEntry(types[i],
                i < ids.Count ? ids[i] : 0,
                i < dates.Count ? dates[i] : 0,
                i < paras.Count ? paras[i] : 0,
                i < s1s.Count ? s1s[i] : "",
                i < s2s.Count ? s2s[i] : ""));
    }

    private static List<short> ReadShortVec(BinReader s)
    {
        var l = new List<short>();
        if (s.Remaining < 2) return l;
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 2; i++) l.Add(s.ReadShort());
        return l;
    }
    private static List<int> ReadIntVec(BinReader s)
    {
        var l = new List<int>();
        if (s.Remaining < 2) return l;
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++) l.Add(s.ReadInt());
        return l;
    }
    private static List<string> ReadStrVec(BinReader s)
    {
        var l = new List<string>();
        if (s.Remaining < 2) return l;
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 2; i++) l.Add(s.ReadString());
        return l;
    }
}

/// <summary>S→C monde NCID 74 — ClientFamilyEmblem : blob d'emblème
/// `[i32 familyId][i32][u16 len][u8×len]` — réponse au 73 /
/// push après op 0x46 (FamilyPackets.FamilyEmblem).</summary>
public sealed class WorldFamilyEmblem : NetMessage
{
    public override int Opcode => 74;
    public override string Name => "World.ClientFamilyEmblem";
    /// <summary>i32 — family_id.</summary>
    public int FamilyId { get; set; }
    /// <summary>i32 — champ requête renvoyé.</summary>
    public int Field2 { get; set; }
    /// <summary>Blob d'emblème (u8×len).</summary>
    public byte[] Data { get; set; } = [];

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(FamilyId);
        s.WriteInt(Field2);
        s.WriteShort((short)Data.Length);
        s.WriteBytes(Data);
    }
    public override void Deserialize(BinReader s)
    {
        FamilyId = s.ReadInt();
        Field2 = s.ReadInt();
        var len = s.Remaining >= 2 ? s.ReadShort() : (short)0;
        Data = len > 0 && s.Remaining >= len ? s.ReadBytes(len) : [];
    }
}

/// <summary>Entrée de la liste de recrutement (80).</summary>
public sealed record GuildRecruitEntry(
    /// <summary>i32 — family id.</summary>
    int FamilyId,
    /// <summary>str — nom de guilde.</summary>
    string Name,
    /// <summary>str — nom du chef.</summary>
    string LeaderName,
    /// <summary>s16 — nombre de membres.</summary>
    short MemberCount,
    /// <summary>s16 — niveau.</summary>
    short Level,
    /// <summary>s16 — type de recrutement (fam+0x3c).</summary>
    short Recruit);

/// <summary>S→C monde NCID 80 — ClientQueryFamilyRecruitList :
/// `[s16 n][n×(i32 id][str name][str leader][s16 membres][s16 lv]
/// [s16 recruit)][s16 page][s16 total]` — FamilyPackets.
/// QueryFamilyRecruitList (Serialize @0x8062d40 + Deserialize client
/// rf_game @0xcd9d90).</summary>
public sealed class WorldFamilyRecruitList : NetMessage
{
    public override int Opcode => 80;
    public override string Name => "World.ClientQueryFamilyRecruitList";
    /// <summary>Familles de la page.</summary>
    public List<GuildRecruitEntry> Entries { get; } = new();
    /// <summary>s16 — page effective renvoyée par le serveur.</summary>
    public short Page { get; set; }
    /// <summary>s16 — total de familles recruteuses.</summary>
    public short Total { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries)
        {
            s.WriteInt(e.FamilyId);
            s.WriteString(e.Name);
            s.WriteString(e.LeaderName);
            s.WriteShort(e.MemberCount);
            s.WriteShort(e.Level);
            s.WriteShort(e.Recruit);
        }
        s.WriteShort(Page);
        s.WriteShort(Total);
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
        {
            var id = s.ReadInt();
            var name = s.ReadString();
            var leader = s.Remaining >= 2 ? s.ReadString() : "";
            if (s.Remaining < 6) break;
            var members = s.ReadShort();
            var level = s.ReadShort();
            var recruit = s.ReadShort();
            Entries.Add(new GuildRecruitEntry(id, name, leader,
                members, level, recruit));
        }
        if (s.Remaining >= 2) Page = s.ReadShort();
        if (s.Remaining >= 2) Total = s.ReadShort();
    }
}

// ---------- Zone C→S / S→C ----------

/// <summary>C→S zone NCID 49 — ZoneServerFamilyCreate `[str name]` :
/// création de guilde (50 000 or débités par la zone au retour du
/// push monde — ZoneEntryFlow.Family.DebitFamilyCreationGoldAsync).
/// CNE_CZ_ZoneServerFamilyCreate (ZoneMessages.g.cs +
/// SocialHandlers.cs:284-292).</summary>
public sealed class ZoneFamilyCreate : NetMessage
{
    public override int Opcode => 49;
    public override string Name => "Zone.ZoneServerFamilyCreate";
    /// <summary>str — nom de guilde demandé.</summary>
    public string Value { get; set; } = "";
    public override void Serialize(BinWriter s) => s.WriteString(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadString();
}

/// <summary>C→S zone NCID 81 — ZoneServerUpdateFamily `[u32 charId]` :
/// demande le 346 d'un perso en ligne (soi-même au login).
/// CNE_CZ_ZoneServerUpdateFamily (ZoneMessages.g.cs +
/// Handlers/FamilyHandlers.cs:252-280).</summary>
public sealed class ZoneUpdateFamily : NetMessage
{
    public override int Opcode => 81;
    public override string Name => "Zone.ZoneServerUpdateFamily";
    /// <summary>u32 — charId cible (soi pour son propre état).</summary>
    public uint CharId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt((int)CharId);
    public override void Deserialize(BinReader s) => CharId = s.ReadUInt();
}

/// <summary>C→S zone NCID 82 — ZoneServerSetFamilyEmblem `[s16][s16]` :
/// choix de l'id d'emblème (chef uniquement — SocialHandlers.cs:
/// le monde écrit `family.emblem`). CNE_CZ_ZoneServerSetFamilyEmblem.</summary>
public sealed class ZoneSetFamilyEmblem : NetMessage
{
    public override int Opcode => 82;
    public override string Name => "Zone.ZoneServerSetFamilyEmblem";
    /// <summary>s16 — id d'emblème.</summary>
    public short EmblemId { get; set; }
    /// <summary>s16 — champ natif réservé (0).</summary>
    public short Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(EmblemId); s.WriteShort(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        EmblemId = s.ReadShort(); Field2 = s.ReadShort();
    }
}

/// <summary>C→S zone NCID 159 — ZoneServerFamilyDonateMoney `[i32]` :
/// don à la trésorerie (SocialHandlers.cs:297-313 — vérifications
/// or/plafond natives, erreur texte 0x388/0x370).</summary>
public sealed class ZoneFamilyDonateMoney : NetMessage
{
    public override int Opcode => 159;
    public override string Name => "Zone.ZoneServerFamilyDonateMoney";
    /// <summary>i32 — montant du don.</summary>
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

/// <summary>S→C zone NCID 346 — ClientChangeFamily
/// `[u32 charId][i32 familyId][u8 flag][str familyName][i32 rank]
/// [s16 emblem][u8 revision]` — changement d'affectation guilde d'un
/// perso (push ZW ChangeFamily → EmitChangeFamilyAsync,
/// ZoneEntryFlow.Family.cs:127-144 ; aussi réponse au 81).</summary>
public sealed class ZoneClientChangeFamily : NetMessage
{
    public override int Opcode => 346;
    public override string Name => "Zone.ClientChangeFamily";
    /// <summary>u32 — charId ciblé.</summary>
    public uint CharId { get; set; }
    /// <summary>i32 — family_id (0 = hors guilde).</summary>
    public int FamilyId { get; set; }
    /// <summary>u8 — flag famille (fam+0x18 natif, 0).</summary>
    public byte Flag { get; set; }
    /// <summary>str — nom de famille.</summary>
    public string FamilyName { get; set; } = "";
    /// <summary>i32 — rang 0-7 du membre (−1 hors guilde).</summary>
    public int Rank { get; set; }
    /// <summary>s16 — id d'emblème (−1 défaut).</summary>
    public short Emblem { get; set; }
    /// <summary>u8 — révision d'affectation (char+0x4de).</summary>
    public byte Revision { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);
        s.WriteInt(FamilyId);
        s.WriteByte(Flag);
        s.WriteString(FamilyName);
        s.WriteInt(Rank);
        s.WriteShort(Emblem);
        s.WriteByte(Revision);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadUInt();
        FamilyId = s.ReadInt();
        Flag = s.ReadByte();
        FamilyName = s.ReadString();
        Rank = s.ReadInt();
        Emblem = s.ReadShort();
        Revision = s.ReadByte();
    }
}

/// <summary>S→C zone NCID 565 — ClientR25UpdateFamily
/// `[s16 memberIndex][str familyName]` : nom de guilde d'un
/// coéquipier après changement (CForceTeam::R25UpdateFamily natif —
/// ZoneEntryFlow.Family.EmitR25UpdateFamilyAsync).</summary>
public sealed class ZoneR25UpdateFamily : NetMessage
{
    public override int Opcode => 565;
    public override string Name => "Zone.ClientR25UpdateFamily";
    /// <summary>s16 — slot du membre dans la team.</summary>
    public short MemberIndex { get; set; }
    /// <summary>str — nouveau nom de famille.</summary>
    public string FamilyName { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(MemberIndex); s.WriteString(FamilyName);
    }
    public override void Deserialize(BinReader s)
    {
        MemberIndex = s.ReadShort(); FamilyName = s.ReadString();
    }
}

// ============================================================
// État guilde (pur — testable hors Godot)
// ============================================================

/// <summary>
/// État guilde courant — alimenté par les trames monde 46-51/68/74/80
/// (via la pompe de <c>SocialWindow</c>) et la trame zone 346 (via
/// <c>GuildWindow</c>). Pur C#, mutations postées sur le thread
/// principal. Sémantique serveur C# documentée par champ.
/// </summary>
public static class GuildState
{
    // --- bloc famille (50 type<2, FamilyPackets.WriteFamilyBlock) ---
    /// <summary>i32 — id de famille (0 = hors guilde).</summary>
    public static int FamilyId;
    /// <summary>str — nom de guilde.</summary>
    public static string Name = "";
    /// <summary>u8 — niveau.</summary>
    public static byte Level;
    /// <summary>i32 — be_create_time (epoch).</summary>
    public static int CreateTime;
    /// <summary>i32 — exp.</summary>
    public static int Exp;
    /// <summary>i32 — leader_id.</summary>
    public static int LeaderId;
    /// <summary>i32 — exploit (points d'exploit natifs).</summary>
    public static int Exploit;
    /// <summary>i32 — id d'emblème (−1 défaut).</summary>
    public static int Emblem = -1;
    /// <summary>s16 — uniform.</summary>
    public static short Uniform;
    /// <summary>str — info/annonce publique (éditée par op 0x0a).</summary>
    public static string Info = "";
    /// <summary>i32 — trésorerie.</summary>
    public static int Treasury;
    /// <summary>s16 — taxe membre.</summary>
    public static short TaxRate;
    /// <summary>s16 — taxe commerce.</summary>
    public static short BusinessTax;
    /// <summary>s16 — house_open (0/501/502).</summary>
    public static short HouseOpen;
    /// <summary>s16 — recruit (état recrutement).</summary>
    public static short Recruit;
    /// <summary>i32 — honor.</summary>
    public static int Honor;
    /// <summary>Titres des 9 rangs (member/leader/cadre_2..8 —
    /// 50 type 0/3).</summary>
    public static readonly string[] Titles = Enumerable.Repeat("", 9).ToArray();
    /// <summary>14 privilèges (1=channel…14=storage03 — 50 type 0/4).</summary>
    public static readonly int[] Privileges = new int[14];

    /// <summary>Membres par charId (50-type-8 complet + 46 upserts).</summary>
    public static readonly Dictionary<int, GuildMember> Members = new();
    /// <summary>Messages du board (51 — agenda + annonces).</summary>
    public static readonly List<GuildBoardMessage> Schedule = new();
    /// <summary>Journal (68).</summary>
    public static readonly List<GuildLogEntry> Logs = new();
    /// <summary>Liste de recrutement (80) — page courante.</summary>
    public static readonly List<GuildRecruitEntry> RecruitEntries = new();
    /// <summary>Page/total du dernier 80.</summary>
    public static short RecruitPage, RecruitTotal;
    /// <summary>Invitation guilde entrante (48) — inviterId (0 serveur
    /// C#), nom inviteur, nom guilde.</summary>
    public static (int InviterId, string Inviter, string Family)?
        PendingInvite;
    /// <summary>Dernière sous-op du 9 en échec (47) — 0 = aucune.</summary>
    public static byte LastOpError;
    /// <summary>Rang de soi (zone 346 — −1 hors guilde/inconnu).</summary>
    public static int SelfRank = -1;
    /// <summary>Révision d'affectation self (346 u8).</summary>
    public static byte SelfRevision;
    /// <summary>Blob d'emblème (74).</summary>
    public static byte[] EmblemData = [];
    /// <summary>Notifié à chaque mutation (thread principal).</summary>
    public static event Action? Changed;

    /// <summary>true = guilde connue.</summary>
    public static bool InGuild => FamilyId > 0;
    /// <summary>true = le perso est le chef (leader_id du bloc).</summary>
    public static bool IsLeader(int selfId) =>
        InGuild && LeaderId == selfId;

    /// <summary>50 : trame d'infos — type 2 → vide ; type &lt;2 →
    /// applique le bloc + titres + privilèges et vide le roster (la
    /// séquence complète = type 0 puis chunks type 8) ; types 3/4 →
    /// titres/privilèges seuls ; type 8 → chunk de membres ;
    /// 5/6/7 → champ seul.</summary>
    public static void ApplyInfo(WorldFamilyInfo m)
    {
        switch (m.Type)
        {
            case 2:
                ClearGuild();
                break;
            case 8:
                foreach (var e in m.Members)
                    Members[e.CharId] = e;
                break;
            case 5:
                Treasury = m.Treasury;
                break;
            case 6 or 7:
                break;   // elfking/spell_point — non affichés
            default:
                ApplyFamilyBlock(m);
                if (m.Type == 0)
                    Members.Clear();   // refresh complet = 0 + chunks 8
                if (m.Type is 0 or 3) ApplyTitles(m);
                if (m.Type is 0 or 4) ApplyPrivileges(m);
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Bloc famille type&lt;2 — 19 champs natifs (déjà
    /// tolérés partiels par <see cref="WorldFamilyInfo"/>).</summary>
    private static void ApplyFamilyBlock(WorldFamilyInfo m)
    {
        FamilyId = m.FamilyId;
        Name = m.FamilyName;
        Level = m.Level;
        CreateTime = m.CreateTime;
        Exp = m.Exp;
        LeaderId = m.LeaderId;
        Exploit = m.Exploit;
        Emblem = m.Emblem;
        Uniform = m.Uniform;
        Info = m.Info;
        Treasury = m.Treasury;
        TaxRate = m.TaxRate;
        BusinessTax = m.BusinessTax;
        HouseOpen = m.HouseOpen;
        Recruit = m.Recruit;
        Honor = m.Honor;
    }

    private static void ApplyTitles(WorldFamilyInfo m)
    {
        for (var i = 0; i < Titles.Length; i++)
            Titles[i] = i < m.Titles.Count ? m.Titles[i] : "";
    }
    private static void ApplyPrivileges(WorldFamilyInfo m)
    {
        for (var i = 0; i < Privileges.Length; i++)
            Privileges[i] = i < m.Privileges.Count ? m.Privileges[i] : 0;
    }

    /// <summary>46 : upsert membre ; <c>Online=0</c> = sortie
    /// (sémantique serveur C# — pas d'émission à la déconnexion).</summary>
    public static void ApplyMemberUpdate(WorldFamilyMemberUpdate m)
    {
        if (!m.Member.Equals(null) && m.Member.CharId != 0)
        {
            if (m.Member.Online == 0 && Members.ContainsKey(m.Member.CharId)
                && Members[m.Member.CharId].Online == 0)
                return; // déjà hors-ligne — pas un départ
            if (m.Member.Online == 0 && Members.Count > 0
                && !Members.ContainsKey(m.Member.CharId))
                return; // hors-ligne inconnu — ignoré
            Members[m.Member.CharId] = m.Member;
        }
        Changed?.Invoke();
    }

    /// <summary>51 : agenda/message-board.</summary>
    public static void ApplySchedule(WorldFamilySchedule m)
    {
        Schedule.Clear();
        Schedule.AddRange(m.Entries);
        Changed?.Invoke();
    }

    /// <summary>68 : journal.</summary>
    public static void ApplyLog(WorldFamilyLogUpdate m)
    {
        Logs.Clear();
        Logs.AddRange(m.Entries);
        Changed?.Invoke();
    }

    /// <summary>74 : blob d'emblème (si c'est notre famille).</summary>
    public static void ApplyEmblem(WorldFamilyEmblem m)
    {
        if (m.FamilyId != FamilyId) return;
        EmblemData = m.Data;
        Changed?.Invoke();
    }

    /// <summary>80 : page de recrutement.</summary>
    public static void ApplyRecruitList(WorldFamilyRecruitList m)
    {
        RecruitEntries.Clear();
        RecruitEntries.AddRange(m.Entries);
        RecruitPage = m.Page;
        RecruitTotal = m.Total;
        Changed?.Invoke();
    }

    /// <summary>47 : sous-op en échec.</summary>
    public static void ApplyOpError(WorldFamilyOpException m)
    {
        LastOpError = m.Op;
        Changed?.Invoke();
    }

    /// <summary>48 : invitation entrante.</summary>
    public static void ApplyInvitation(WorldFamilyInvitation m)
    {
        PendingInvite = (m.InviterId, m.InviterName, m.FamilyName);
        Changed?.Invoke();
    }

    /// <summary>346 zone : self change — nom/rang/révision ; un
    /// familyId=0 sur soi vide l'état (sortie constatée zone-side).</summary>
    public static void ApplyZoneChange(ZoneClientChangeFamily m,
        int selfId)
    {
        if ((int)m.CharId != selfId) return;
        SelfRank = m.Rank;
        SelfRevision = m.Revision;
        if (m.FamilyId == 0 && FamilyId != 0)
            ClearGuild();
        else if (m.FamilyId != 0)
        {
            FamilyId = m.FamilyId;
            var n = FamilyName(m);
            if (n.Length > 0) Name = n;
        }
        Changed?.Invoke();
        static string FamilyName(ZoneClientChangeFamily mm) =>
            mm.FamilyName.Length > 0 ? mm.FamilyName : "";
    }

    /// <summary>49 : sortie/kick/dissolution.</summary>
    public static void ApplyQuit() => ClearGuild();

    /// <summary>Vide l'état guilde (49 / 50-type-2 / 346 family=0).</summary>
    public static void ClearGuild()
    {
        FamilyId = 0; Name = ""; Level = 0; CreateTime = 0; Exp = 0;
        LeaderId = 0; Exploit = 0; Emblem = -1; Uniform = 0;
        Info = ""; Treasury = 0; TaxRate = 0; BusinessTax = 0;
        HouseOpen = 0; Recruit = 0; Honor = 0;
        Array.Clear(Titles); Array.Clear(Privileges);
        Members.Clear(); Schedule.Clear(); Logs.Clear();
        RecruitEntries.Clear(); RecruitPage = RecruitTotal = 0;
        PendingInvite = null; SelfRank = -1; SelfRevision = 0;
        EmblemData = [];
        Changed?.Invoke();
    }

    /// <summary>Reset complet (déconnexion) — comme ClearGuild +
    /// erreur d'op.</summary>
    public static void Clear()
    {
        ClearGuild();
        LastOpError = 0;
    }
}
