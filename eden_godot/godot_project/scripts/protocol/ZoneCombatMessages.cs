using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages de combat/sorts + ramassage de trésors du ZoneServer (:5201).
// Lot S1 — « plan de secours ».
//
// Sources de preuve (toutes serveur, LECTURE SEULE) :
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs — layouts reversés
//     (CNE_CZ_ZoneServer* = C→S, CNC_CZ_Client* = S→C) ;
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Combat.Spells.cs,
//     ZoneEntryFlow.Combat.Spells.Resolution.cs (parsing C→S réel et
//     émission 279/278/281/287/288/289) ;
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Treasure.cs (35/386) ;
//   - $EDEN/server/src/Eden.Zone/Combat/NativeCombatFormulas.cs
//     (SpellHitFlags du 281) ;
//   - $EDEN/docs/generated/FEATURE-MATRIX.md — statuts GOLDEN/DESASM.

// ---------- C→S : lancement de sorts ----------

/// <summary>C→S NCID 9 — SpellCastToLife <c>[u16 spellId][u32 target]</c>
/// : lance un sort sur une entité (sight-handle mob, charId joueur/self).
/// DESASM — <c>CNE_CZ_ZoneServerSpellCastToLife</c> (champs short,uint)
/// + parse serveur <c>HandleSpellHitAsync</c> (u16@0, u32@2, 6B min).
/// spellId = spellmaster du 311 (<see cref="KnownSpell.SpellMaster"/>).</summary>
public sealed class ZoneSpellCastToLife : NetMessage
{
    public override int Opcode => 9;
    public override string Name => "Zone.SpellCastToLife";
    /// <summary>u16 — id du sort (spellmaster, S_Spell/T_Spell).</summary>
    public ushort SpellId { get; set; }
    /// <summary>u32 — sight-handle de la cible.</summary>
    public uint TargetId { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteUShort(SpellId);           // u16 — id sort
        s.WriteUInt(TargetId);            // u32 — handle cible
    }
    public override void Deserialize(BinReader s)
    {
        SpellId = s.ReadUShort();
        TargetId = s.ReadUInt();
    }
}

/// <summary>C→S NCID 13 — SpellBreak : annule le cast en cours.
/// DESASM — corps VIDE (<c>CNE_CZ_ZoneServerSpellBreak</c>, aucun champ ;
/// réponse = 287 ClientSpellBreak).</summary>
public sealed class ZoneSpellBreak : NetMessage
{
    public override int Opcode => 13;
    public override string Name => "Zone.SpellBreak";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 16 — SpellCastToLocation
/// <c>[u16 spell][i32 cible][f32 x][f32 z]</c> (14 octets) : sort de zone
/// visant un point au sol. Preuve = le parse réel du serveur
/// (<c>HandleCastLocationAsync</c> : u16@0, f32@6, f32@10, corps ≥ 14B) —
/// le .g.cs aplati (short,float,float) ne colle pas ; commentaire
/// dispatch : « [u16 spell][i32 target][f32 x][f32 z] ».</summary>
public sealed class ZoneSpellCastToLocation : NetMessage
{
    public override int Opcode => 16;
    public override string Name => "Zone.SpellCastToLocation";
    /// <summary>u16 — id du sort.</summary>
    public ushort SpellId { get; set; }
    /// <summary>i32 — champ intermédiaire natif (0 envoyé).</summary>
    public int Target { get; set; }
    /// <summary>f32 — x au sol Eden du point visé.</summary>
    public float X { get; set; }
    /// <summary>f32 — y au sol Eden (z monde) du point visé.</summary>
    public float Z { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteUShort(SpellId);           // u16 — id sort
        s.WriteInt(Target);               // i32 — slot cible (0)
        s.WriteFloat(X);                  // f32 — x sol
        s.WriteFloat(Z);                  // f32 — z sol
    }
    public override void Deserialize(BinReader s)
    {
        SpellId = s.ReadUShort();
        Target = s.ReadInt();
        X = s.ReadFloat();
        Z = s.ReadFloat();
    }
}

/// <summary>C→S NCID 17 — SpellCastToNowhere <c>[u16 spell][f32 dir]</c>
/// (6 octets) : sort sans cible (buff self, saut, invocation…) lancé
/// dans la direction courante. DESASM — parse serveur
/// <c>HandleCastNowhereAsync</c> (« wire natif désasm :
/// [s16 spell][f32 direction] », u16@0 + f32@2).</summary>
public sealed class ZoneSpellCastToNowhere : NetMessage
{
    public override int Opcode => 17;
    public override string Name => "Zone.SpellCastToNowhere";
    /// <summary>u16 — id du sort.</summary>
    public ushort SpellId { get; set; }
    /// <summary>f32 — direction de cast (radians, repère Eden).</summary>
    public float Direction { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteUShort(SpellId);           // u16 — id sort
        s.WriteFloat(Direction);          // f32 — orientation
    }
    public override void Deserialize(BinReader s)
    {
        SpellId = s.ReadUShort();
        Direction = s.ReadFloat();
    }
}

/// <summary>C→S NCID 59 — SpellCastToNearby <c>[u16 spell][f32 param]</c>
/// (6 octets) : variante « autour de soi » — même handler serveur que
/// CastToNowhere (case 17/59 partagé). DESASM.</summary>
public sealed class ZoneSpellCastToNearby : NetMessage
{
    public override int Opcode => 59;
    public override string Name => "Zone.SpellCastToNearby";
    /// <summary>u16 — id du sort.</summary>
    public ushort SpellId { get; set; }
    /// <summary>f32 — paramètre natif (direction).</summary>
    public float Value { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteUShort(SpellId);           // u16 — id sort
        s.WriteFloat(Value);              // f32 — direction/param
    }
    public override void Deserialize(BinReader s)
    {
        SpellId = s.ReadUShort();
        Value = s.ReadFloat();
    }
}

// ---------- C→S : trésors au sol ----------

/// <summary>C→S NCID 35 — TreasurePickup <c>[u32 id]</c> : ramasse un
/// trésor au sol. DESASM — <c>CNE_CZ_ZoneServerTreasurePickup</c>
/// (champ uint unique) ; règles serveur
/// <c>HandleTreasurePickupAsync</c> : portée 4 u (CloseEnough natif),
/// fenêtre propriétaire, inventaire plein → l'item reste au sol.</summary>
public sealed class ZoneTreasurePickup : NetMessage
{
    public override int Opcode => 35;
    public override string Name => "Zone.TreasurePickup";
    /// <summary>u32 — id du trésor (handle du 329 SeeTreasure).</summary>
    public uint TreasureId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteUInt(TreasureId);
    public override void Deserialize(BinReader s) => TreasureId = s.ReadUInt();
}

// ---------- S→C : cycle de cast ----------

/// <summary>S→C NCID 278 — SpellCast : un AUTRE joueur lance un sort
/// (broadcast CanSee — le lanceur ne reçoit jamais son propre 278, cf.
/// <c>CastSequenceAsync</c> serveur). DESASM.
/// <c>[s16 spell][i32 caster][i32 cible][u8 flag][f32 x][f32 z]</c>.</summary>
public sealed class ZoneClientSpellCast : NetMessage
{
    public override int Opcode => 278;
    public override string Name => "Zone.ClientSpellCast";
    /// <summary>s16 — id du sort.</summary>
    public short SpellId { get; set; }
    /// <summary>i32 — charId du lanceur.</summary>
    public int Caster { get; set; }
    /// <summary>i32 — handle de la cible (0 = cast non ciblé).</summary>
    public int Target { get; set; }
    /// <summary>u8 — flag natif (1 sur cast ciblé).</summary>
    public byte Flag { get; set; }
    /// <summary>f32 — x du lanceur (ou point visé).</summary>
    public float X { get; set; }
    /// <summary>f32 — z du lanceur (ou point visé).</summary>
    public float Z { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(SpellId);            // s16 — id sort
        s.WriteInt(Caster);               // i32 — lanceur
        s.WriteInt(Target);               // i32 — cible
        s.WriteByte(Flag);                // u8 — flag
        s.WriteFloat(X);                  // f32 — x
        s.WriteFloat(Z);                  // f32 — z
    }
    public override void Deserialize(BinReader s)
    {
        SpellId = s.ReadShort();
        Caster = s.ReadInt();
        Target = s.ReadInt();
        Flag = s.ReadByte();
        X = s.ReadFloat();
        Z = s.ReadFloat();
    }
}

/// <summary>S→C NCID 279 — SpellCastOK <c>[s16 spell][i32 castDs]
/// [i32 cible][u8 ok]</c> : accusé de cast envoyé au lanceur.
/// GOLDEN. <c>castDs</c> = temps de cast en décisecondes
/// (NotifyCastToSelf natif → GetCastTime) ; <c>ok</c>=0 = refus
/// (cast déjà en cours — le cast précédent continue).</summary>
public sealed class ZoneClientSpellCastOK : NetMessage
{
    public override int Opcode => 279;
    public override string Name => "Zone.ClientSpellCastOK";
    /// <summary>s16 — id du sort.</summary>
    public short SpellId { get; set; }
    /// <summary>i32 — durée du cast en décisecondes (ds/10 = s).</summary>
    public int CastTimeDs { get; set; }
    /// <summary>i32 — handle de la cible.</summary>
    public int Target { get; set; }
    /// <summary>u8 — 1 = cast accepté, 0 = refusé.</summary>
    public byte Ok { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(SpellId);            // s16 — id sort
        s.WriteInt(CastTimeDs);           // i32 — cast time (ds)
        s.WriteInt(Target);               // i32 — cible
        s.WriteByte(Ok);                  // u8  — succès/refus
    }
    public override void Deserialize(BinReader s)
    {
        SpellId = s.ReadShort();
        CastTimeDs = s.ReadInt();
        Target = s.ReadInt();
        Ok = s.ReadByte();
    }
}

/// <summary>S→C NCID 281 — SpellHitToLife <c>[s16 spell][i32 victime]
/// [i32 caster][i32 amount][i32 flags]</c> : impact d'un sort.
/// GOLDEN — corps 18B (<c>SendSpellHitAsync</c> serveur).
/// <paramref name="Flags"/> = <see cref="SpellHitConst"/> : 1 hit,
/// 8 miss, 0x10 block, 0x200 restore (soin, vert), 0x200000 crit.</summary>
public sealed class ZoneClientSpellHitToLife : NetMessage
{
    public override int Opcode => 281;
    public override string Name => "Zone.ClientSpellHitToLife";
    /// <summary>s16 — id du sort.</summary>
    public short SpellId { get; set; }
    /// <summary>i32 — handle de la victime/soigné.</summary>
    public int Target { get; set; }
    /// <summary>i32 — handle du lanceur.</summary>
    public int Caster { get; set; }
    /// <summary>i32 — montant (dégâts ou soin).</summary>
    public int Amount { get; set; }
    /// <summary>i32 — flags résultat (<see cref="SpellHitConst"/>).</summary>
    public int Flags { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(SpellId);            // s16 — id sort
        s.WriteInt(Target);               // i32 — victime
        s.WriteInt(Caster);               // i32 — lanceur
        s.WriteInt(Amount);               // i32 — montant
        s.WriteInt(Flags);                // i32 — flags
    }
    public override void Deserialize(BinReader s)
    {
        SpellId = s.ReadShort();
        Target = s.ReadInt();
        Caster = s.ReadInt();
        Amount = s.ReadInt();
        Flags = s.ReadInt();
    }
}

/// <summary>S→C NCID 287 — SpellBreak <c>[i32 caster][s16 spell][u8 reason]</c>
/// (7 octets) : cast interrompu (déplacement, annulation…). DESASM —
/// <c>BreakCastAsync</c> serveur + Serialize natif @0x81e97f0.</summary>
public sealed class ZoneClientSpellBreak : NetMessage
{
    public override int Opcode => 287;
    public override string Name => "Zone.ClientSpellBreak";
    /// <summary>i32 — handle du lanceur interrompu.</summary>
    public int Caster { get; set; }
    /// <summary>s16 — sort cassé.</summary>
    public short SpellId { get; set; }
    /// <summary>u8 — raison native (0 partout en pratique).</summary>
    public byte Reason { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Caster);               // i32 — lanceur
        s.WriteShort(SpellId);            // s16 — sort
        s.WriteByte(Reason);              // u8  — raison
    }
    public override void Deserialize(BinReader s)
    {
        Caster = s.ReadInt();
        SpellId = s.ReadShort();
        Reason = s.ReadByte();
    }
}

/// <summary>S→C NCID 288 — SpellBlastOK : départ du « blast » d'un
/// sort (visuel d'impact/serveur-side anim). GOLDEN — layout g.cs
/// (<c>CNC_CZ_ClientSpellBlastOK</c>, ~39B).</summary>
public sealed class ZoneClientSpellBlastOK : NetMessage
{
    public override int Opcode => 288;
    public override string Name => "Zone.ClientSpellBlastOK";
    /// <summary>u32 — serial de blast (incrémenté de 11).</summary>
    public uint Serial { get; set; }
    /// <summary>i32 — handle victime (0xFFFFxxxx / charId).</summary>
    public int VictimHandle { get; set; }
    /// <summary>s16 — id du sort.</summary>
    public short SpellId { get; set; }
    /// <summary>i32 — puissance (S_Spell col27, down-time ds).</summary>
    public int Power { get; set; }
    /// <summary>f32 — position d'impact.</summary>
    public float X { get; set; }
    /// <summary>f32 — position d'impact.</summary>
    public float Y { get; set; }
    /// <summary>f32 — position d'impact.</summary>
    public float Z { get; set; }
    /// <summary>i32 — id de blast (groupe motion natif).</summary>
    public int BlastId { get; set; }
    /// <summary>u16 — compte du 1er vecteur de cibles (0 en pratique).</summary>
    public ushort TargetCount { get; set; }
    /// <summary>u16 — compte du 2e vecteur (0 en pratique).</summary>
    public ushort TargetCount2 { get; set; }
    /// <summary>u8 — flag terminal (0 capturé).</summary>
    public byte Flag { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteUInt(Serial);              // u32 — serial
        s.WriteInt(VictimHandle);         // i32 — victime
        s.WriteShort(SpellId);            // s16 — sort
        s.WriteInt(Power);                // i32 — power
        s.WriteFloat(X);                  // f32 — x
        s.WriteFloat(Y);                  // f32 — y
        s.WriteFloat(Z);                  // f32 — z
        s.WriteInt(BlastId);              // i32 — blastId
        s.WriteShort((short)TargetCount);  // u16 — n cibles 1
        s.WriteShort((short)TargetCount2); // u16 — n cibles 2
        s.WriteByte(Flag);                // u8  — flag
    }
    public override void Deserialize(BinReader s)
    {
        Serial = s.ReadUInt();
        VictimHandle = s.ReadInt();
        SpellId = s.ReadShort();
        Power = s.ReadInt();
        X = s.ReadFloat();
        Y = s.ReadFloat();
        Z = s.ReadFloat();
        BlastId = s.ReadInt();
        TargetCount = s.ReadUShort();
        TargetCount2 = s.ReadUShort();
        Flag = s.ReadByte();
    }
}

/// <summary>S→C NCID 289 — SpellBlastToLife : blast sur une entité.
/// DESASM — layout g.cs
/// <c>[u32][s16][i32][f32×3][i32][f32×3][i32][i32]</c> (~46B).</summary>
public sealed class ZoneClientSpellBlastToLife : NetMessage
{
    public override int Opcode => 289;
    public override string Name => "Zone.ClientSpellBlastToLife";
    /// <summary>u32 — serial.</summary>
    public uint Serial { get; set; }
    /// <summary>s16 — id du sort.</summary>
    public short SpellId { get; set; }
    /// <summary>i32 — handle victime.</summary>
    public int VictimHandle { get; set; }
    /// <summary>f32 — position.</summary>
    public float X { get; set; }
    /// <summary>f32 — position.</summary>
    public float Y { get; set; }
    /// <summary>f32 — position.</summary>
    public float Z { get; set; }
    /// <summary>i32 — blastId.</summary>
    public int BlastId { get; set; }
    /// <summary>f32 — champs natifs (puissance/direction).</summary>
    public float Field8 { get; set; }
    /// <summary>f32 — champ natif.</summary>
    public float Field9 { get; set; }
    /// <summary>f32 — champ natif.</summary>
    public float Field10 { get; set; }
    /// <summary>i32 — champ natif.</summary>
    public int Field11 { get; set; }
    /// <summary>i32 — champ natif.</summary>
    public int Field12 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteUInt(Serial);
        s.WriteShort(SpellId);
        s.WriteInt(VictimHandle);
        s.WriteFloat(X);
        s.WriteFloat(Y);
        s.WriteFloat(Z);
        s.WriteInt(BlastId);
        s.WriteFloat(Field8);
        s.WriteFloat(Field9);
        s.WriteFloat(Field10);
        s.WriteInt(Field11);
        s.WriteInt(Field12);
    }
    public override void Deserialize(BinReader s)
    {
        Serial = s.ReadUInt();
        SpellId = s.ReadShort();
        VictimHandle = s.ReadInt();
        X = s.ReadFloat();
        Y = s.ReadFloat();
        Z = s.ReadFloat();
        BlastId = s.ReadInt();
        Field8 = s.ReadFloat();
        Field9 = s.ReadFloat();
        Field10 = s.ReadFloat();
        Field11 = s.ReadInt();
        Field12 = s.ReadInt();
    }
}

// ---------- S→C : divers combat/service ----------

/// <summary>S→C NCID 386 — TreasurePickupOK <c>[u32 charId][u16 itemId]
/// [s16 count][s16 0]</c> (10 octets) : confirmation de ramassage.
/// DESASM — <c>HandleTreasurePickupAsync</c> (Field4 laissé à 0).</summary>
public sealed class ZoneClientTreasurePickupOK : NetMessage
{
    public override int Opcode => 386;
    public override string Name => "Zone.ClientTreasurePickupOK";
    /// <summary>u32 — charId du ramasseur.</summary>
    public uint CharId { get; set; }
    /// <summary>u16 — id de l'item ramassé.</summary>
    public ushort ItemId { get; set; }
    /// <summary>s16 — quantité.</summary>
    public short Count { get; set; }
    /// <summary>s16 — champ terminal (0).</summary>
    public short Field4 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteUInt(CharId);              // u32 — ramasseur
        s.WriteUShort(ItemId);            // u16 — item
        s.WriteShort(Count);              // s16 — count
        s.WriteShort(Field4);             // s16 — terminal
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadUInt();
        ItemId = s.ReadUShort();
        Count = s.ReadShort();
        if (s.Remaining >= 2) Field4 = s.ReadShort();
    }
}

/// <summary>S→C NCID 410 — CommandFailReturn <c>[i32 ncid][i32 param]</c>
/// : refus générique d'une commande (le serveur l'utilise pour les
/// casts refusés : cooldown, cible morte, silence, MP insuffisants…).
/// DESASM — <c>CNC_CZ_ClientCommandFailReturn</c>.</summary>
public sealed class ZoneClientCommandFailReturn : NetMessage
{
    public override int Opcode => 410;
    public override string Name => "Zone.ClientCommandFailReturn";
    /// <summary>i32 — NCID de la commande refusée (9/16/17/59…).</summary>
    public int FailedOpcode { get; set; }
    /// <summary>i32 — paramètre (spellId ou 0).</summary>
    public int Param { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(FailedOpcode);
        s.WriteInt(Param);
    }
    public override void Deserialize(BinReader s)
    {
        FailedOpcode = s.ReadInt();
        Param = s.ReadInt();
    }
}

/// <summary>S→C NCID 531 — ReviveInfo <c>[i32 area]</c> : zone de
/// réapparition liée au perso (SetReviveAreaID natif) — émis à la mort
/// (comptes à rebours/options du dialogue de respawn).
/// GOLDEN — <c>ZoneEntryFlow.Combat.Death.cs</c>.</summary>
public sealed class ZoneClientReviveInfo : NetMessage
{
    public override int Opcode => 531;
    public override string Name => "Zone.ClientReviveInfo";
    /// <summary>i32 — id d'aire de réapparition.</summary>
    public int Area { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Area);
    public override void Deserialize(BinReader s) => Area = s.ReadInt();
}

/// <summary>S→C NCID 274 — PutItemIntoInventory : un item entre dans
/// le sac. Codec serveur <c>NativeItemCodec.BuildPutItem</c> (désasm
/// <c>CItemFactory::PutIntoContainer</c> @0x809ea00, 54B) :
/// <c>[u16 itemId][s16 bag][s16 slot][s16 durability/count]
/// [s16 maxDurability][u16 strlen+str][u8 flags][u24 mask][…]</c>.
/// On ne lit que la tête (id/bag/slot/count) pour rafraîchir le sac.</summary>
public sealed class ZonePutItem : NetMessage
{
    public override int Opcode => 274;
    public override string Name => "Zone.ClientPutItemIntoInventory";
    /// <summary>u16 — id de l'item.</summary>
    public ushort ItemId { get; set; }
    /// <summary>s16 — index du conteneur (bag).</summary>
    public short Bag { get; set; }
    /// <summary>s16 — slot dans le sac.</summary>
    public short Slot { get; set; }
    /// <summary>s16 — durabilité courante (count pour les stacks).</summary>
    public short Durability { get; set; }
    /// <summary>s16 — durabilité max courante.</summary>
    public short MaxDurability { get; set; }
    /// <summary>u8 — flags (bits 0-2 sockets, bit3 identifié).</summary>
    public byte Flags { get; set; }
    /// <summary>s16 — strengthen (dans le 3e mot masqué — best effort,
    /// 0 si non relu).</summary>
    public short Strengthen { get; set; }
    /// <summary>s16 — quantité stack (bit6 du mask ; ≈ Durability sur
    /// les empilables — approximation suffisante pour l'UI).</summary>
    public int Amount => Durability;
    public override void Serialize(BinWriter s)
    {
        s.WriteUShort(ItemId);            // u16 — id item
        s.WriteShort(Bag);                // s16 — conteneur
        s.WriteShort(Slot);               // s16 — slot
        s.WriteShort(Durability);         // s16 — dur/count
        s.WriteShort(MaxDurability);      // s16 — max dur
        s.WriteShort(0);                  // u16 — str vide
        s.WriteByte(Flags);               // u8  — flags
        s.WriteBytes(new byte[] { 0, 1, 0 });   // u24 mask (0x000100)
        s.WriteInt(0);                    // u32 — create_time
        s.WriteBytes(new byte[34]);       // 34B — queue native
    }
    public override void Deserialize(BinReader s)
    {
        ItemId = s.ReadUShort();
        Bag = s.ReadShort();
        Slot = s.ReadShort();
        Durability = s.ReadShort();
        MaxDurability = s.ReadShort();
        var slen = s.Remaining >= 2 ? s.ReadUShort() : 0;
        if (slen > 0 && s.Remaining >= slen) s.ReadBytes(slen);
        if (s.Remaining >= 1) Flags = s.ReadByte();
        // Le reste (mask u24 + champs masqués + queue 34B) est brut.
    }
}

/// <summary>S→C NCID 275 — DropItem <c>[s16 contType][s16 slot]
/// [s16 contIdx]</c> : le slot du sac est vidé. DESASM —
/// <c>CNC_CZ_ClientDropItem</c> (3×s16), usage serveur
/// InventoryHandlers (contType=0, slot, contIdx=bag).</summary>
public sealed class ZoneDropItem : NetMessage
{
    public override int Opcode => 275;
    public override string Name => "Zone.ClientDropItem";
    /// <summary>s16 — type de conteneur (0 = sac).</summary>
    public short ContainerType { get; set; }
    /// <summary>s16 — slot vidé.</summary>
    public short Slot { get; set; }
    /// <summary>s16 — index du conteneur (bag).</summary>
    public short ContainerIndex { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(ContainerType);
        s.WriteShort(Slot);
        s.WriteShort(ContainerIndex);
    }
    public override void Deserialize(BinReader s)
    {
        ContainerType = s.ReadShort();
        Slot = s.ReadShort();
        ContainerIndex = s.ReadShort();
    }
}

/// <summary>S→C NCID 291 — ModifyItemDurability <c>[s16 contType]
/// [s16 slot][s16 count][s16 maxDur][s16 contIdx][s16 0]</c> :
/// quantité/durabilité d'un slot modifiée (merge, shrink de stack).
/// DESASM — usage serveur InventoryHandlers.</summary>
public sealed class ZoneModifyItemSlot : NetMessage
{
    public override int Opcode => 291;
    public override string Name => "Zone.ClientModifyItemDurability";
    /// <summary>s16 — type de conteneur (0 = sac).</summary>
    public short ContainerType { get; set; }
    /// <summary>s16 — slot modifié.</summary>
    public short Slot { get; set; }
    /// <summary>s16 — nouvelle quantité restante.</summary>
    public short Count { get; set; }
    /// <summary>s16 — durabilité max (item+0xe).</summary>
    public short MaxDurability { get; set; }
    /// <summary>s16 — index du conteneur (bag).</summary>
    public short ContainerIndex { get; set; }
    /// <summary>s16 — champ terminal natif.</summary>
    public short Field6 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(ContainerType);
        s.WriteShort(Slot);
        s.WriteShort(Count);
        s.WriteShort(MaxDurability);
        s.WriteShort(ContainerIndex);
        s.WriteShort(Field6);
    }
    public override void Deserialize(BinReader s)
    {
        ContainerType = s.ReadShort();
        Slot = s.ReadShort();
        Count = s.ReadShort();
        MaxDurability = s.ReadShort();
        ContainerIndex = s.ReadShort();
        if (s.Remaining >= 2) Field6 = s.ReadShort();
    }
}

/// <summary>Flags du 5e champ i32 de <see cref="ZoneClientSpellHitToLife"/>
/// (281) — miroir de <c>SpellHitFlags</c> (NativeCombatFormulas.cs
/// serveur, désasm HitXSkill).</summary>
public static class SpellHitConst
{
    /// <summary>Coup touché.</summary>
    public const int Hit = 1;
    /// <summary>Raté.</summary>
    public const int Miss = 8;
    /// <summary>Blocage.</summary>
    public const int Block = 0x10;
    /// <summary>Restauration (soin — affichage vert natif).</summary>
    public const int Restore = 0x200;
    /// <summary>Critique (dégâts ET soins).</summary>
    public const int Crit = 0x200000;
}
