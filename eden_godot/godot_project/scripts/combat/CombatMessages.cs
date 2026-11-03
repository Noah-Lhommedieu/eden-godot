using EdenClient.Net;

namespace EdenClient.Combat;

// Messages combat du ZoneServer (:5201).
// Preuves : $EDEN/server/.../Generated/ZoneMessages.g.cs
// (CNE_CZ_ZoneServerMeleeAttack / CNC_CZ_ClientMeleeAttack),
// eden_server ZoneEntryFlow.Combat.Mob.cs (capture native
// « client→serveur 0500 <mob> 00 ») et CombatHandlers.cs.

/// <summary>
/// C→S NCID 5 — MeleeAttack <c>[i32 targetSightHandle][u8 type]</c>.
/// Le client natif renvoie ce paquet à chaque tick de swing
/// d'auto-attaque (même hors de portée — le serveur marque alors
/// l'état combat sans émettre de coup). <c>handle=0 ou type=2</c> =
/// arrêt d'attaque.
/// </summary>
public sealed class ZoneMeleeAttack : NetMessage
{
    public override int Opcode => 5;
    public override string Name => "Zone.MeleeAttack";
    /// <summary>Sight-handle de la cible (0xFFFFxxxx mob) ; 0 = stop.</summary>
    public int TargetId { get; set; }
    /// <summary>0 = swing courant ; 2 = arrêt demandé.</summary>
    public byte AttackSlot { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(TargetId);
        s.WriteByte(AttackSlot);
    }
    public override void Deserialize(BinReader s)
    {
        TargetId = s.ReadInt();
        AttackSlot = s.ReadByte();
    }
}

/// <summary>
/// S→C NCID 272 — ClientMeleeAttack
/// <c>[u32 attacker][u32 target][i32 dmg][i32 type][i32 motionKfId]</c>.
/// Émis par le serveur à chaque coup résolu (attacker = charId joueur
/// ou handle mob). field5 = kfId d'attaque du pool KFM natif —
/// l'identifiant ne mappe pas (encore) vers nos noms d'anims :
/// HYPOTHESIS on joue « attackNN »/« hurtNN » génériques.
/// </summary>
public sealed class ZoneClientMeleeAttack : NetMessage
{
    public override int Opcode => 272;
    public override string Name => "Zone.ClientMeleeAttack";
    /// <summary>Attaquant (charId joueur ou sight-handle mob).</summary>
    public uint Attacker { get; set; }
    /// <summary>Cible (charId joueur ou sight-handle mob).</summary>
    public uint Target { get; set; }
    /// <summary>Dégâts infligés (0 si miss type 8).</summary>
    public int Damage { get; set; }
    /// <summary>Mot-drapeau : 1 hit, 3 premier coup, 5 coup fatal
    /// (twin −1), 8 miss, +0x10 block/0x100 parry.</summary>
    public int HitType { get; set; }
    /// <summary>kfId de motion d'attaque natif (1421 = attack12 arc,
    /// 1021/1022 = attack01/02 m025 ; −1 = twin fatal).</summary>
    public int MotionKfId { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Attacker);
        s.WriteInt((int)Target);
        s.WriteInt(Damage);
        s.WriteInt(HitType);
        s.WriteInt(MotionKfId);
    }
    public override void Deserialize(BinReader s)
    {
        Attacker = (uint)s.ReadInt();
        Target = (uint)s.ReadInt();
        Damage = s.ReadInt();
        HitType = s.ReadInt();
        MotionKfId = s.ReadInt();
    }
}

/// <summary>
/// S→C NCID 305 — ClientDie <c>[u32 victime][u32 tueur][u8 flags]</c>.
/// Preuve : <c>$EDEN/server/…/ZoneEntryFlow.Combat.Death.cs</c> —
/// « 305 ClientDie [u32 victime][u32 tueur][u8 flags] — ordre natif
/// du ctor CCharacter::Die (piVar[1]=uid victime, piVar[2]=handle
/// tueur) » ; émis après le 272 fatal, ~5 s avant le 269 de despawn
/// du cadavre (délai natif qui laisse l'anim death se jouer).
/// </summary>
public sealed class ZoneClientDie : NetMessage
{
    public override int Opcode => 305;
    public override string Name => "Zone.ClientDie";
    /// <summary>Sight-handle de la victime (mob 0xFFFFxxxx, charId…).</summary>
    public uint Victim { get; set; }
    /// <summary>Handle du tueur.</summary>
    public uint Killer { get; set; }
    /// <summary>u8 — flags (0 observé).</summary>
    public byte Flags { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Victim);
        s.WriteInt((int)Killer);
        s.WriteByte(Flags);
    }
    public override void Deserialize(BinReader s)
    {
        Victim = (uint)s.ReadInt();
        Killer = (uint)s.ReadInt();
        Flags = s.ReadByte();
    }
}

/// <summary>
/// C→S NCID 2 — WhoIs <c>[i32 charId][u8 flag]</c>. Inspect d'un
/// perso en vue : le serveur répond 263 ClientDisplayChar si
/// <c>flag != 0</c> (sinon seulement un 326 de position) — voir
/// <c>$EDEN/server/…/ZoneEntryFlow.DisplayChar.cs::HandleWhoIsAsync</c>
/// (désasm <c>CNE_CZ_ZoneServerWhoIs::Execute</c> @0x082a1340).
///
/// Usage client-reborn : le 265 SeeChar ne porte PAS le blob
/// d'apparence (96 o fixes — seulement modelRev +0x54) ; WhoIs est le
/// canal prouvé pour obtenir l'apparence 32 octets d'un joueur
/// distant. HYPOTHESIS : le client natif rafraîchit sur changement
/// de modelRev via un canal dédié non identifié — on envoie un WhoIs
/// par (charId, modelRev) vu.
/// </summary>
public sealed class ZoneWhoIs : NetMessage
{
    public override int Opcode => 2;
    public override string Name => "Zone.WhoIs";
    /// <summary>charId du personnage inspecté.</summary>
    public int CharId { get; set; }
    /// <summary>1 = envoyer la fiche complète (263), 0 = position seule.</summary>
    public byte Flag { get; set; } = 1;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteByte(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        Flag = s.ReadByte();
    }
}

/// <summary>
/// S→C NCID 263 — ClientDisplayChar : fiche complète d'un perso
/// (réponse WhoIs). Le client-reborn ne parse que la <b>tête fixe</b>
/// jusqu'au blob d'apparence — le reste est conservé brut :
/// <code>
/// [u32 charId][str nom][f32 x][f32 z][f32 dir][f32 moveSpeed]
/// [u8 atkSpdR][u8 atkSpdL][4B lifeFlags][u16 level][u8 classId]
/// [u16 classLevel][0x20 apparence(char+0x48e)]
/// </code>
/// Preuve : <c>$EDEN/server/…/Codecs/ClientDisplayCharCodec.cs</c>
/// (Serialize natif @0x081e03f0) — la queue est un record masqué
/// variable (attrs optionnels, 2 sets d'équipement, famille, monture,
  /// étal, versus…) que nous n'utilisons pas.
/// </summary>
public sealed class ZoneClientDisplayChar : NetMessage
{
    public override int Opcode => 263;
    public override string Name => "Zone.ClientDisplayChar";
    /// <summary>charId (sight-handle du perso).</summary>
    public uint CharId { get; set; }
    /// <summary>Nom du personnage.</summary>
    public string CharName { get; set; } = "";
    /// <summary>Niveau personnage (u16).</summary>
    public ushort Level { get; set; }
    /// <summary>ClassId (u8).</summary>
    public byte ClassId { get; set; }
    /// <summary>Blob apparence 32 octets (<see cref="AppearanceCodec"/>).</summary>
    public byte[] Appearance { get; set; } = new byte[32];
    /// <summary>Octets après la tête fixe (masque + champs variables)
    /// — conservés pour debug, non interprétés.</summary>
    public byte[] Rest { get; set; } = [];

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);          // +0 u32 charId
        s.WriteString(CharName);          // +4 str nom
        s.WriteFloat(0f);                 // f32 x
        s.WriteFloat(0f);                 // f32 z
        s.WriteFloat(0f);                 // f32 dir
        s.WriteFloat(0f);                 // f32 moveSpeed
        s.WriteByte(0);                   // u8 atkSpdR
        s.WriteByte(0);                   // u8 atkSpdL
        s.WriteBytes(new byte[4]);        // 4B CLifeEntityFlags
        s.WriteShort((short)Level);       // u16 level
        s.WriteByte(ClassId);             // u8 classId
        s.WriteShort(0);                  // u16 classLevel
        var app = Appearance;
        s.WriteBytes(app.AsSpan(0, Math.Min(0x20, app.Length))
            .ToArray());                  // 0x20 blob apparence
        for (var i = app.Length; i < 0x20; i++)
            s.WriteByte(0);               // pad si blob court
        s.WriteBytes(Rest);               // queue variable (re-passthrough)
    }

    public override void Deserialize(BinReader s)
    {
        CharId = (uint)s.ReadInt();
        CharName = s.ReadString();
        s.ReadFloat();                    // x
        s.ReadFloat();                    // z
        s.ReadFloat();                    // dir
        s.ReadFloat();                    // moveSpeed
        s.ReadByte();                     // atkSpdR
        s.ReadByte();                     // atkSpdL
        s.ReadBytes(4);                   // lifeFlags
        Level = s.ReadUShort();
        ClassId = s.ReadByte();
        s.ReadShort();                    // classLevel
        Appearance = s.ReadBytes(0x20);
        Rest = s.ReadBytes(s.Remaining);
    }
}
