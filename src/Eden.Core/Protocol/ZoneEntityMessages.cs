using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages d'entités du ZoneServer (:5201) — cycle de vie des objets
// en vue (spawn/move/despawn/attrs).
//
// Sources de preuve notées par classe : classe .g.cs du serveur C#
//   ($EDEN/server/src/Eden.Core/Networking/Messages/Generated/
//   ZoneMessages.g.cs), statut dans docs/generated/FEATURE-MATRIX.md,
//   trames hex de captures/zsniff_cs_entry_success.bin (natif) et
//   docs/netsmoke_trace.log (serveur C#).
//
// Conventions terrain (vérifiées sur captures) :
//   - positions réseau : x, y = plan du sol Eden (Z-up) ; conversion
//     vers Godot via WorldConv.ToGodot(x, z_hauteur, -y… voir callsites).
//   - sight-handles : joueurs = charId, NPC = 0x88C8xxxx,
//     mobs = 0xFFFFxxxx, trésors = id table.
//   - 326 encode les positions au sol ×16 (u16) et la direction en
//     demi-flottant IEEE 754 (u16).

/// <summary>S→C NCID 264 — MoveChar [f32 x][f32 y] : position au sol.
/// GOLDEN (matrix). CNC_CZ_ClientMoveChar — zsniff : E 264 suit chaque
/// MoveSelf émis (écho self, pas de handle). zsniff :
/// 0801 6974a343 06459843.</summary>
public sealed class ZoneMoveChar : NetMessage
{
    public override int Opcode => 264;
    public override string Name => "Zone.ClientMoveChar";
    /// <summary>Position au sol Eden (x).</summary>
    public float X { get; set; }
    /// <summary>Position au sol Eden (y — 2e coordonnée terrain).</summary>
    public float Y { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteFloat(X);
        s.WriteFloat(Y);
    }
    public override void Deserialize(BinReader s)
    {
        X = s.ReadFloat();
        Y = s.ReadFloat();
    }
}

/// <summary>S→C NCID 265 — SeeChar : personnage entrant en vue.
/// PASSTHRU (matrix — le .g.cs est inutilisable : PutBinary aplati,
/// HP tronqués) ; layout = codec serveur ClientSeeCharCodec.cs
/// (preuve désasm Serialize @0x0819ec20 / ctor @0x0819e8e0) :
/// [u32 charId][f32 x][f32 y][f32 dir][f32 moveSpeed][u8 atkSpd R]
/// [u8 atkSpd L][4B lifeFlags][u16 level][u16 classLevel][i64 hp]
/// [i64 maxHp][i32 maxHp][i32 maxMp][u8×5 révisions][s16 sort]
/// [str étal][s16 teamTerritoire][f32][f32][f32][s16][s16][i32].</summary>
public sealed class ZoneSeeChar : NetMessage
{
    public override int Opcode => 265;
    public override string Name => "Zone.ClientSeeChar";
    /// <summary>+0 u32 — charId, sert aussi de sight-handle.</summary>
    public uint CharId { get; set; }
    /// <summary>+4 f32 — position sol x.</summary>
    public float X { get; set; }
    /// <summary>+8 f32 — position sol y.</summary>
    public float Y { get; set; }
    /// <summary>+12 f32 — direction (radians).</summary>
    public float Facing { get; set; }
    /// <summary>+16 f32 — vitesse de déplacement.</summary>
    public float MoveSpeed { get; set; }
    /// <summary>+20 u8 — niveau vitesse d'attaque main droite.</summary>
    public byte AtkSpeedR { get; set; }
    /// <summary>+21 u8 — main gauche.</summary>
    public byte AtkSpeedL { get; set; }
    /// <summary>+22 4B — CLifeEntityFlags bruts (bitset état).</summary>
    public byte[] LifeFlags { get; set; } = new byte[4];
    /// <summary>+26 u16 — niveau personnage.</summary>
    public ushort Level { get; set; }
    /// <summary>+28 u16 — niveau de classe.</summary>
    public ushort ClassLevel { get; set; }
    /// <summary>+30 i64 — HP courant.</summary>
    public long Hp { get; set; }
    /// <summary>+38 i64 — HP max.</summary>
    public long MaxHp { get; set; }
    /// <summary>+46 i32 — HP max (doublon natif i32).</summary>
    public int MaxHp32 { get; set; }
    /// <summary>+50 i32 — MP max.</summary>
    public int MaxMp { get; set; }
    /// <summary>+54..+58 u8 ×5 — révisions modèle/enchant/do/famille/
    /// appellation.</summary>
    public byte[] Revisions { get; set; } = new byte[5];
    /// <summary>+59 s16 — id du sort en cours (0 sinon).</summary>
    public short CastingSpell { get; set; }
    /// <summary>+61 str — titre d'étal joueur.</summary>
    public string StallTitle { get; set; } = "";
    /// <summary>équipe guerre territoire (0xffff hors guerre).</summary>
    public short TerritoryTeam { get; set; }
    /// <summary>hauteur de saut/flottement.</summary>
    public float FloatHeight { get; set; }
    /// <summary>durée restante de flottement.</summary>
    public float FloatRemain { get; set; }
    /// <summary>durée totale de flottement.</summary>
    public float FloatDuration { get; set; }
    /// <summary>état de flottement.</summary>
    public short FloatState { get; set; }
    /// <summary>char+0xd8c versus.</summary>
    public short Versus { get; set; }
    /// <summary>char+0xdb0 équipe versus.</summary>
    public int VersusTeam { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);
        s.WriteFloat(X);
        s.WriteFloat(Y);
        s.WriteFloat(Facing);
        s.WriteFloat(MoveSpeed);
        s.WriteByte(AtkSpeedR);
        s.WriteByte(AtkSpeedL);
        s.WriteBytes(LifeFlags);
        s.WriteShort((short)Level);
        s.WriteShort((short)ClassLevel);
        s.WriteLong(Hp);
        s.WriteLong(MaxHp);
        s.WriteInt(MaxHp32);
        s.WriteInt(MaxMp);
        s.WriteBytes(Revisions);
        s.WriteShort(CastingSpell);
        s.WriteString(StallTitle);
        s.WriteShort(TerritoryTeam);
        s.WriteFloat(FloatHeight);
        s.WriteFloat(FloatRemain);
        s.WriteFloat(FloatDuration);
        s.WriteShort(FloatState);
        s.WriteShort(Versus);
        s.WriteInt(VersusTeam);
    }

    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadUInt();
        X = s.ReadFloat();
        Y = s.ReadFloat();
        Facing = s.ReadFloat();
        MoveSpeed = s.ReadFloat();
        AtkSpeedR = s.ReadByte();
        AtkSpeedL = s.ReadByte();
        LifeFlags = s.ReadBytes(4);
        Level = s.ReadUShort();
        ClassLevel = s.ReadUShort();
        Hp = s.ReadLong();
        MaxHp = s.ReadLong();
        MaxHp32 = s.ReadInt();
        MaxMp = s.ReadInt();
        Revisions = s.ReadBytes(5);
        CastingSpell = s.ReadShort();
        StallTitle = s.ReadString();
        TerritoryTeam = s.ReadShort();
        FloatHeight = s.ReadFloat();
        FloatRemain = s.ReadFloat();
        FloatDuration = s.ReadFloat();
        FloatState = s.ReadShort();
        Versus = s.ReadShort();
        VersusTeam = s.ReadInt();
    }
}

/// <summary>S→C NCID 269 — LifeEradicate [i32 handle] : l'entité
/// quitte la vue (despawn). DESASM (matrix).
/// CNC_CZ_ClientLifeEradicate.</summary>
public sealed class ZoneLifeEradicate : NetMessage
{
    public override int Opcode => 269;
    public override string Name => "Zone.ClientLifeEradicate";
    /// <summary>Sight-handle de l'entité retirée.</summary>
    public int Handle { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Handle);
    public override void Deserialize(BinReader s) => Handle = s.ReadInt();
}

/// <summary>S→C NCID 267 — ClientGoto [i32 handle][f32 x][f32 y][u8] :
/// déplacement forcé/réponse à TargetUpdate (zsniff : E 267 après
/// D 124 — 0b01 0d93c988 aceca143 7d2f9743 6a).
/// CNC_CZ_ClientGoto.</summary>
public sealed class ZoneClientGoto : NetMessage
{
    public override int Opcode => 267;
    public override string Name => "Zone.ClientGoto";
    /// <summary>Handle de l'entité déplacée.</summary>
    public int Handle { get; set; }
    /// <summary>Position sol x.</summary>
    public float X { get; set; }
    /// <summary>Position sol y.</summary>
    public float Y { get; set; }
    /// <summary>u8 — flag/mode (0x6a capturé).</summary>
    public byte Flag { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteFloat(X);
        s.WriteFloat(Y);
        s.WriteByte(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        X = s.ReadFloat();
        Y = s.ReadFloat();
        Flag = s.ReadByte();
    }
}

/// <summary>S→C NCID 271 — DisplayMonster : changement d'owner d'un
/// mob. GOLDEN. Corps variable : [u32 mobHandle][u8 count][s16×count
/// enchants][u8 ownerGen][i32 ownerHandle] — Serialize natif
/// @0x81e96d0. zsniff : 0f01 faf4ffff 00 01 04c2eb0b.</summary>
public sealed class ZoneDisplayMonster : NetMessage
{
    public override int Opcode => 271;
    public override string Name => "Zone.ClientDisplayMonster";
    /// <summary>Sight-handle du mob.</summary>
    public uint MobHandle { get; set; }
    /// <summary>Ids s16 des enchants modifiés (souvent vide).</summary>
    public short[] EnchantIds { get; set; } = [];
    /// <summary>Compteur de génération d'owner.</summary>
    public byte OwnerGeneration { get; set; }
    /// <summary>Handle du nouvel owner (charId attaquant).</summary>
    public int OwnerHandle { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)MobHandle);
        s.WriteByte((byte)EnchantIds.Length);
        foreach (var e in EnchantIds) s.WriteShort(e);
        s.WriteByte(OwnerGeneration);
        s.WriteInt(OwnerHandle);
    }
    public override void Deserialize(BinReader s)
    {
        MobHandle = s.ReadUInt();
        var count = s.ReadByte();
        EnchantIds = new short[count];
        for (var i = 0; i < count; i++) EnchantIds[i] = s.ReadShort();
        OwnerGeneration = s.ReadByte();
        OwnerHandle = s.ReadInt();
    }
}

/// <summary>S→C NCID 280 — UpdateAttr [i32 handle][s16 attrId]
/// [i32 cur][i64 max] : update d'attribut d'une entité
/// (attrId 0 = HP — le serveur l'envoie à l'entrée en vue).
/// GOLDEN. CNC_CZ_ClientUpdateAttr — zsniff :
/// 1801 02c2eb0b 0000 78010000 7801000000000000.</summary>
public sealed class ZoneUpdateAttr : NetMessage
{
    public override int Opcode => 280;
    public override string Name => "Zone.ClientUpdateAttr";
    /// <summary>Handle de l'entité.</summary>
    public int Handle { get; set; }
    /// <summary>Id d'attribut (0 = HP).</summary>
    public short AttrId { get; set; }
    /// <summary>Valeur courante.</summary>
    public int Current { get; set; }
    /// <summary>Valeur max (i64).</summary>
    public long Max { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(AttrId);
        s.WriteInt(Current);
        s.WriteLong(Max);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        AttrId = s.ReadShort();
        Current = s.ReadInt();
        Max = s.ReadLong();
    }
}

/// <summary>S→C NCID 293 — NodeExit [u8][u8] : sortie de node
/// (pré-migration). GOLDEN. CNC_CZ_ClientNodeExit.</summary>
public sealed class ZoneNodeExit : NetMessage
{
    public override int Opcode => 293;
    public override string Name => "Zone.ClientNodeExit";
    public byte Field1 { get; set; }
    public byte Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteByte(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadByte();
    }
}

/// <summary>S→C NCID 294 — LeaveNode [i32] : sortie de node d'une
/// entité. NON PROUVE (matrix) — pas observé dans les captures
/// (despawn réel = 269). CNC_CZ_ClientLeaveNode.</summary>
public sealed class ZoneLeaveNode : NetMessage
{
    public override int Opcode => 294;
    public override string Name => "Zone.ClientLeaveNode";
    /// <summary>HYPOTHESIS: handle de l'entité retirée.</summary>
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

/// <summary>S→C NCID 296 — AttrRefresh : bloc d'attributs du
/// personnage self à l'entrée (stats courantes). GOLDEN.
/// CNC_CZ_ClientAttrRefresh — zsniff : corps 96B
/// (2801 0c00 0a00 … netsmoke ligne 296).</summary>
public sealed class ZoneAttrRefresh : NetMessage
{
    public override int Opcode => 296;
    public override string Name => "Zone.ClientAttrRefresh";
    /// <summary>14 × s16 — attributs de base (rôle exact non prouvé
    /// champ par champ).</summary>
    public short[] Fields1to14 { get; set; } = new short[14];
    /// <summary>4 × i32.</summary>
    public int[] Fields15to18 { get; set; } = new int[4];
    /// <summary>2 × f32.</summary>
    public float[] Fields19to20 { get; set; } = new float[2];
    /// <summary>1 × i32.</summary>
    public int Field21 { get; set; }
    /// <summary>8 × s16.</summary>
    public short[] Fields22to29 { get; set; } = new short[8];
    /// <summary>6 × f32 — positions/vecteurs en queue (corps 96B).</summary>
    public float[] Fields30to35 { get; set; } = new float[6];

    public override void Serialize(BinWriter s)
    {
        foreach (var v in Fields1to14) s.WriteShort(v);
        foreach (var v in Fields15to18) s.WriteInt(v);
        foreach (var v in Fields19to20) s.WriteFloat(v);
        s.WriteInt(Field21);
        foreach (var v in Fields22to29) s.WriteShort(v);
        foreach (var v in Fields30to35) s.WriteFloat(v);
    }

    public override void Deserialize(BinReader s)
    {
        Fields1to14 = new short[14];
        for (var i = 0; i < 14; i++) Fields1to14[i] = s.ReadShort();
        Fields15to18 = new int[4];
        for (var i = 0; i < 4; i++) Fields15to18[i] = s.ReadInt();
        Fields19to20 = new float[2];
        for (var i = 0; i < 2; i++) Fields19to20[i] = s.ReadFloat();
        Field21 = s.ReadInt();
        Fields22to29 = new short[8];
        for (var i = 0; i < 8; i++) Fields22to29[i] = s.ReadShort();
        Fields30to35 = new float[6];
        for (var i = 0; i < 6; i++) Fields30to35[i] = s.ReadFloat();
    }
}

/// <summary>S→C NCID 283 — ModelModify : changement d'apparence d'une
/// entité. GOLDEN. CNC_CZ_ClientModelModify : [i32 handle][u8]
/// [i32][u16 n][n×u32][u16 m][m×u32]. zsniff :
/// 1b01 02c2eb0b 00 00400000 0100 3b000000 0000.</summary>
public sealed class ZoneModelModify : NetMessage
{
    public override int Opcode => 283;
    public override string Name => "Zone.ClientModelModify";
    /// <summary>Handle de l'entité.</summary>
    public int Handle { get; set; }
    /// <summary>u8 — sous-opération.</summary>
    public byte Field2 { get; set; }
    /// <summary>i32 — paramètre (0x4000 capturé).</summary>
    public int Field3 { get; set; }
    /// <summary>Premier vecteur u32 (natif this+0x10).</summary>
    public List<uint> Field4 { get; } = new();
    /// <summary>Second vecteur u32 (natif this+0x18).</summary>
    public List<uint> Field5 { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteByte(Field2);
        s.WriteInt(Field3);
        s.WriteShort((short)Field4.Count);
        foreach (var v in Field4) s.WriteInt((int)v);
        s.WriteShort((short)Field5.Count);
        foreach (var v in Field5) s.WriteInt((int)v);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        Field2 = s.ReadByte();
        Field3 = s.ReadInt();
        Field4.Clear();
        Field5.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++) Field4.Add(s.ReadUInt());
        n = s.ReadUShort();
        for (var i = 0; i < n; i++) Field5.Add(s.ReadUInt());
    }
}

/// <summary>S→C NCID 298 — SeeMob : mob/NPC entrant en vue.
/// GOLDEN (matrix — format à masque prouvé byte-à-byte).
/// CNC_CZ_ClientSeeMob : en-tête fixe puis masque u16 + champs
/// OPTIONNELS écrits si != défaut (cascade iVar7/local_1c du
/// désassemblage natif Serialize @0x81ff690/zone).
///
/// Layout fixe (ctor @0x849df90 + Serialize) :
///   [u32 handle][s16 entityId][u8 flag0]
///   [f32 x][f32 y][f32 dir][f32 moveSpeed]
///   [u8 kind : 8=NPC / 12=monstre][4B lifeFlags][str displayName]
///   [u16 mask][champs optionnels]
/// NOTE : le 3e float natif = direction (ent+0x58), le 4e = vitesse
/// (vcall+0xd8). Le serveur C# y écrit (e.Z, facing) — voir les
/// commentaires des champs.
/// zsniff NPC : 2a01 0d93c988 53c3 00 aceca143 7d2f9743 ce537b40
/// 00000000 08 00000000 0000 573d … (len=77)
/// zsniff mob : 2a01 0080ffff c1da 00 00cf1743 aa668743 00000000
/// 7ff80a40 0c … (len=76)</summary>
public sealed class ZoneSeeMob : NetMessage
{
    public override int Opcode => 298;
    public override string Name => "Zone.ClientSeeMob";

    /// <summary>Sight-handle (u32 — NPC 0x88C8xxxx / mob 0xFFFFxxxx).</summary>
    public uint Handle { get; set; }
    /// <summary>Template id (s16 — npcId c_npc.ini / monsterId
    /// c_monster.ini ; relu en u16 côté client natif).</summary>
    public short EntityId { get; set; }
    /// <summary>u8 natif +0x0a — 0 dans toutes les captures.</summary>
    public byte Flag0 { get; set; }
    /// <summary>Position sol x (natif +0x0c = ent+0x28).</summary>
    public float X { get; set; }
    /// <summary>Position sol y (natif +0x10 = ent+0x2c).</summary>
    public float Y { get; set; }
    /// <summary>Natif +0x14 = direction (ent+0x58, radians — capturé
    /// 3.927/5.498 = facing ini des NPCs). Le serveur C# y écrit e.Z
    /// pour ses propres spawns.</summary>
    public float FieldZ { get; set; }
    /// <summary>Natif +0x18 = vitesse de déplacement (vcall+0xd8 —
    /// GetMovement). Le serveur C# y écrit RoamSpeed/BattleSpeed du
    /// template (BuildSeeMob → Facing).</summary>
    public float FieldSpeed { get; set; }
    /// <summary>u8 kind : 8 = NPC, 12 = monstre (natif +0x1c).</summary>
    public byte Kind { get; set; }
    /// <summary>4 octets bruts CLifeEntityFlags (natif +0x1d PutBinary).
    /// </summary>
    public byte[] Raw4 { get; set; } = new byte[4];
    /// <summary>Nom affiché (str — vide pour mobs/NPC standards).</summary>
    public string DisplayName { get; set; } = "";

    // Champs optionnels pilotés par le masque — défauts = cascade
    // native ; les valeurs capturées sont notées par champ.
    /// <summary>HP courant (i64, bit0 — capturé : 10 NPC / ~200 mob).</summary>
    public long Hp { get; set; }
    /// <summary>HP max (i64, bit1 — défaut natif 11).</summary>
    public long MaxHp { get; set; } = 11;
    /// <summary>Natif +0x34 (i32, bit2 — capturé 10 NPC / 180 mob).</summary>
    public int Unk34 { get; set; } = 11;
    /// <summary>Natif +0x38 (i32, bit3 — cascade Unk34).</summary>
    public int Unk38 { get; set; } = 11;
    /// <summary>Natif +0x4c (i32, bit4 — capturé 100).</summary>
    public int Unk4C { get; set; }
    /// <summary>Natif +0x3c (i32, bit5).</summary>
    public int Unk3C { get; set; }
    /// <summary>Natif +0x44 (i32, bit6 — capturé -2 NPC / -1 mob).</summary>
    public int Unk44 { get; set; }
    /// <summary>Natif +0x48 (i32, bit7).</summary>
    public int Unk48 { get; set; }
    /// <summary>Natif +0x50 (u8, bit8 — défaut 1).</summary>
    public byte Unk50 { get; set; } = 1;
    /// <summary>Natif +0x40 (u8, bit9 — défaut 0x64).</summary>
    public byte Unk40 { get; set; } = 0x64;
    /// <summary>Natif +0x58 (f32, bit10 — défaut 100).</summary>
    public float Unk58 { get; set; } = 100f;
    /// <summary>Natif +0x5c (f32, bit11 — défaut 100).</summary>
    public float Unk5C { get; set; } = 100f;
    /// <summary>Natif +0x60 (f32, bit12 — défaut 100).</summary>
    public float Unk60 { get; set; } = 100f;
    /// <summary>Natif +0x64 (s16, bit13 — défaut 100).</summary>
    public short Unk64 { get; set; } = 100;

    /// <summary>Sérialisation symétrique à la lecture : le masque est
    /// recalculé depuis les champs (cascade native reproduite).</summary>
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Handle);
        s.WriteShort(EntityId);
        s.WriteByte(Flag0);
        s.WriteFloat(X);
        s.WriteFloat(Y);
        s.WriteFloat(FieldZ);
        s.WriteFloat(FieldSpeed);
        s.WriteByte(Kind);
        s.WriteBytes(Raw4);
        s.WriteString(DisplayName ?? "");
        ushort mask = 0;
        int lc = 0xb, iv = Unk3C;
        if (Hp != 0) mask |= 1;
        if (MaxHp != 11) mask |= 2;
        if (Unk34 != lc) { mask |= 4; lc = Unk34; }
        if (Unk38 != lc) mask |= 8;
        lc = 0;
        if (Unk4C != 0) mask |= 16;
        if (iv != lc) { mask |= 32; iv = lc; }
        if (iv != Unk44) { mask |= 64; iv = lc; }
        if (iv != Unk48) mask |= 128;
        if (Unk50 != 1) mask |= 256;
        if (Unk40 != 0x64) mask |= 512;
        if (Unk58 != 100f) mask |= 1024;
        if (Unk5C != 100f) mask |= 2048;
        if (Unk60 != 100f) mask |= 4096;
        if (Unk64 != 100) mask |= 8192;
        s.WriteShort((short)mask);
        lc = 0xb; iv = Unk3C;
        if ((mask & 1) != 0) s.WriteLong(Hp);
        if ((mask & 2) != 0) s.WriteLong(MaxHp);
        if ((mask & 4) != 0) { s.WriteInt(Unk34); lc = Unk34; }
        if ((mask & 8) != 0) s.WriteInt(Unk38);
        lc = 0;
        if ((mask & 16) != 0) s.WriteInt(Unk4C);
        if ((mask & 32) != 0) { s.WriteInt(iv = Unk3C); iv = lc; }
        if ((mask & 64) != 0) { s.WriteInt(Unk44); iv = lc; }
        if ((mask & 128) != 0) s.WriteInt(Unk48);
        if ((mask & 256) != 0) s.WriteByte(Unk50);
        if ((mask & 512) != 0) s.WriteByte(Unk40);
        if ((mask & 1024) != 0) s.WriteFloat(Unk58);
        if ((mask & 2048) != 0) s.WriteFloat(Unk5C);
        if ((mask & 4096) != 0) s.WriteFloat(Unk60);
        if ((mask & 8192) != 0) s.WriteShort(Unk64);
    }

    /// <summary>Lecture : masque puis champs présents ; les absents
    /// prennent le défaut de la cascade native.</summary>
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadUInt();
        EntityId = s.ReadShort();
        Flag0 = s.ReadByte();
        X = s.ReadFloat();
        Y = s.ReadFloat();
        FieldZ = s.ReadFloat();
        FieldSpeed = s.ReadFloat();
        Kind = s.ReadByte();
        Raw4 = s.ReadBytes(4);
        DisplayName = s.ReadString();
        var mask = s.ReadUShort();
        Hp = (mask & 1) != 0 ? s.ReadLong() : 0;
        MaxHp = (mask & 2) != 0 ? s.ReadLong() : 11;
        int lc = 0xb;
        if ((mask & 4) != 0) { Unk34 = s.ReadInt(); lc = Unk34; }
        else Unk34 = lc;
        Unk38 = (mask & 8) != 0 ? s.ReadInt() : lc;
        lc = 0;
        Unk4C = (mask & 16) != 0 ? s.ReadInt() : 0;
        int iv;
        if ((mask & 32) != 0) { Unk3C = s.ReadInt(); iv = 0; }
        else { Unk3C = 0; iv = Unk3C; }
        Unk44 = (mask & 64) != 0 ? s.ReadInt() : iv;
        Unk48 = (mask & 128) != 0 ? s.ReadInt() : iv;
        Unk50 = (mask & 256) != 0 ? s.ReadByte() : (byte)1;
        Unk40 = (mask & 512) != 0 ? s.ReadByte() : (byte)0x64;
        Unk58 = (mask & 1024) != 0 ? s.ReadFloat() : 100f;
        Unk5C = (mask & 2048) != 0 ? s.ReadFloat() : 100f;
        Unk60 = (mask & 4096) != 0 ? s.ReadFloat() : 100f;
        Unk64 = (mask & 8192) != 0 ? s.ReadShort() : (short)100;
    }
}

/// <summary>S→C NCID 326 — MovePrediction : déplacement d'une entité
/// en vue. GOLDEN. CNC_CZ_ClientMovePrediction :
/// [i32 handle][u16 curX×16][u16 curY×16][u8 flag][u16 dir half]
/// [opt u16 dstX×16][u16 dstY×16] — corps 11B sans destination,
/// 15B avec (zsniff : 4601 0d93c988 3e14 e612 07 da43).</summary>
public sealed class ZoneMovePrediction : NetMessage
{
    public override int Opcode => 326;
    public override string Name => "Zone.ClientMovePrediction";
    /// <summary>Handle de l'entité (charId joueur, sight-handle mob).</summary>
    public int Handle { get; set; }
    /// <summary>Position sol x courante ×16.</summary>
    public ushort CurX { get; set; }
    /// <summary>Position sol y courante ×16.</summary>
    public ushort CurY { get; set; }
    /// <summary>Flags de déplacement (u8 — 0x07 capturé).</summary>
    public byte Flag { get; set; }
    /// <summary>Direction en demi-flottant IEEE 754 (u16 — conversion
    /// float→half natif : sign&lt;&lt;15 | (exp-0x70&amp;0x1f)&lt;&lt;10
    /// | mantissa&gt;&gt;13). Le bit0 pilote la marche/strafe.</summary>
    public ushort Facing { get; set; }
    /// <summary>Destination x ×16 — présent si l'entité bouge.</summary>
    public ushort? DstX { get; set; }
    /// <summary>Destination y ×16 — voir <see cref="DstX"/>.</summary>
    public ushort? DstY { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort((short)CurX);
        s.WriteShort((short)CurY);
        s.WriteByte(Flag);
        s.WriteShort((short)Facing);
        if (DstX.HasValue && DstY.HasValue)
        {
            s.WriteShort((short)DstX.Value);
            s.WriteShort((short)DstY.Value);
        }
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        CurX = s.ReadUShort();
        CurY = s.ReadUShort();
        Flag = s.ReadByte();
        Facing = s.ReadUShort();
        if (s.Remaining >= 4)
        {
            DstX = s.ReadUShort();
            DstY = s.ReadUShort();
        }
        else { DstX = null; DstY = null; }
    }

    /// <summary>Position courante en unités monde (÷16).</summary>
    public (float X, float Y) CurPos => (CurX / 16f, CurY / 16f);
    /// <summary>Destination en unités monde, ou null.</summary>
    public (float X, float Y)? DstPos =>
        DstX.HasValue && DstY.HasValue
            ? (DstX.Value / 16f, DstY.Value / 16f)
            : null;

    /// <summary>Décode la direction demi-flottant → f32.</summary>
    public float FacingFloat
    {
        get
        {
            var h = Facing;
            var sign = (h & 0x8000) != 0 ? -1f : 1f;
            var exp = (h >> 10) & 0x1f;
            var mant = h & 0x3ff;
            if (exp == 0) return sign * mant * (1f / 1024f) * 6.103515625e-5f;
            if (exp == 0x1f) return float.NaN; // inf/nan — pas de sens ici
            return sign * (1f + mant / 1024f) * MathF.Pow(2f, exp - 15);
        }
    }
}

/// <summary>S→C NCID 329 — SeeTreasure : trésor au sol en vue.
/// DESASM (matrix). CNC_CZ_ClientSeeTreasure.</summary>
public sealed class ZoneSeeTreasure : NetMessage
{
    public override int Opcode => 329;
    public override string Name => "Zone.ClientSeeTreasure";
    /// <summary>Handle/id du trésor.</summary>
    public uint Handle { get; set; }
    /// <summary>f32 ×4 — champs natifs positionnels (2 = x sol,
    /// 5 = y sol d'après le builder serveur).</summary>
    public float Field2 { get; set; }
    public float Field3 { get; set; }
    /// <summary>Position sol x (Field4 côté serveur : t.X).</summary>
    public float X { get; set; }
    /// <summary>Position sol y (Field5 : t.Z serveur).</summary>
    public float Y { get; set; }
    /// <summary>u16 — itemId.</summary>
    public ushort ItemId { get; set; }
    /// <summary>s16 — count.</summary>
    public short Count { get; set; }
    /// <summary>s16 — doublon count.</summary>
    public short Count2 { get; set; }
    /// <summary>s16.</summary>
    public short Field9 { get; set; }
    /// <summary>u8.</summary>
    public byte Field10 { get; set; }
    /// <summary>i32 — charId du propriétaire.</summary>
    public int OwnerCharId { get; set; }
    /// <summary>u8 — 0 fenêtre propriétaire, 1 public.</summary>
    public byte IsPublic { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Handle);
        s.WriteFloat(Field2);
        s.WriteFloat(Field3);
        s.WriteFloat(X);
        s.WriteFloat(Y);
        s.WriteShort((short)ItemId);
        s.WriteShort(Count);
        s.WriteShort(Count2);
        s.WriteShort(Field9);
        s.WriteByte(Field10);
        s.WriteInt(OwnerCharId);
        s.WriteByte(IsPublic);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadUInt();
        Field2 = s.ReadFloat();
        Field3 = s.ReadFloat();
        X = s.ReadFloat();
        Y = s.ReadFloat();
        ItemId = s.ReadUShort();
        Count = s.ReadShort();
        Count2 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadByte();
        OwnerCharId = s.ReadInt();
        IsPublic = s.ReadByte();
    }
}

/// <summary>S→C NCID 330 — TreasureDisappear [u32 id] : retrait d'un
/// trésor au sol. DESASM (matrix). CNC_CZ_ClientTreasureDisappear.</summary>
public sealed class ZoneTreasureDisappear : NetMessage
{
    public override int Opcode => 330;
    public override string Name => "Zone.ClientTreasureDisappear";
    /// <summary>Id du trésor retiré.</summary>
    public uint Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt((int)Value);
    public override void Deserialize(BinReader s) => Value = s.ReadUInt();
}

/// <summary>S→C NCID 380 — MobTransform [i32 handle][s16 newTemplateId]
/// : le mob change de modèle/template. NON PROUVE (matrix) — layout
/// du .g.cs, pas de capture. CNC_CZ_ClientMobTransform.</summary>
public sealed class ZoneMobTransform : NetMessage
{
    public override int Opcode => 380;
    public override string Name => "Zone.ClientMobTransform";
    /// <summary>Sight-handle du mob.</summary>
    public int Handle { get; set; }
    /// <summary>HYPOTHESIS: nouveau template monstre.</summary>
    public short NewTemplateId { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(NewTemplateId);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        NewTemplateId = s.ReadShort();
    }
}

/// <summary>S→C NCID 536 — UpdateZoneMonster [i32][s16][s16][f32][f32] :
/// mise à jour paramètres d'un mob de zone. NON PROUVE (matrix) —
/// layout du .g.cs, pas de capture. CNC_CZ_ClientUpdateZoneMonster.</summary>
public sealed class ZoneUpdateZoneMonster : NetMessage
{
    public override int Opcode => 536;
    public override string Name => "Zone.ClientUpdateZoneMonster";
    /// <summary>Sight-handle du mob.</summary>
    public int Handle { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public float Field4 { get; set; }
    public float Field5 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteFloat(Field4);
        s.WriteFloat(Field5);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadFloat();
        Field5 = s.ReadFloat();
    }
}

// ---------- S→C : données du joueur self à l'entrée ----------

/// <summary>S→C NCID 310 — EnterNodePlayerData : bloc complet du
/// personnage self (stats, flags, collections). INCOMPLET (matrix) —
/// le wire contient des BINARY à taille implicite et 4 collections
/// finales non codées dans le .g.cs.
/// On ne parse que le préfixe prouvé par capture : [u32 charId]
/// [str name] ; le reste est conservé brut.
/// zsniff/netsmoke : 3601 04c2eb0b 05007465737433 … (len=417).</summary>
public sealed class ZoneEnterNodePlayerData : NetMessage
{
    public override int Opcode => 310;
    public override string Name => "Zone.ClientEnterNodePlayerData";
    /// <summary>+0 u32 — charId du personnage.</summary>
    public uint CharId { get; set; }
    /// <summary>+4 str — nom du personnage.</summary>
    public string CharName { get; set; } = "";
    /// <summary>Reste du payload après le préfixe (format interne non
    /// décodé — collections natives).</summary>
    public byte[] Rest { get; set; } = [];
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);
        s.WriteString(CharName);
        s.WriteBytes(Rest);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadUInt();
        CharName = s.ReadString();
        Rest = s.ReadBytes(s.Remaining);
    }
}

/// <summary>S→C NCID 311 — EnterNodeEquipAndSpell : équipement +
/// sorts du personnage self. INCOMPLET (matrix) — conservé brut.
/// zsniff : 3801 + ~1240B de blobs équipement/sorts.</summary>
public sealed class ZoneEnterNodeEquipAndSpell : NetMessage
{
    public override int Opcode => 311;
    public override string Name => "Zone.ClientEnterNodeEquipAndSpell";
    /// <summary>Corps brut (format interne non décodé).</summary>
    public byte[] Body { get; set; } = [];
    public override void Serialize(BinWriter s) => s.WriteBytes(Body);
    public override void Deserialize(BinReader s) =>
        Body = s.ReadBytes(s.Remaining);
}

/// <summary>S→C NCID 312 — EnterNodeInventory : inventaire du
/// personnage self. INCOMPLET (matrix) — conservé brut.
/// netsmoke : 3801 0220… (len=1257).</summary>
public sealed class ZoneEnterNodeInventory : NetMessage
{
    public override int Opcode => 312;
    public override string Name => "Zone.ClientEnterNodeInventory";
    /// <summary>Corps brut (format interne non décodé).</summary>
    public byte[] Body { get; set; } = [];
    public override void Serialize(BinWriter s) => s.WriteBytes(Body);
    public override void Deserialize(BinReader s) =>
        Body = s.ReadBytes(s.Remaining);
}

// ---------- C→S compléments ----------

/// <summary>C→S NCID 3 — TextCommand [str] : commande texte/GM.
/// CNE_CZ_ZoneServerTextCommand.</summary>
public sealed class ZoneTextCommand : NetMessage
{
    public override int Opcode => 3;
    public override string Name => "Zone.ZoneServerTextCommand";
    public string Text { get; set; } = "";
    public override void Serialize(BinWriter s) => s.WriteString(Text);
    public override void Deserialize(BinReader s) => Text = s.ReadString();
}
