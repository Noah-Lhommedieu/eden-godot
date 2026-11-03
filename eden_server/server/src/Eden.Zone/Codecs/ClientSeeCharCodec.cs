using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>
/// Builder du corps de <c>CNC_CZ_ClientSeeChar</c> (NCID 265) —
/// présentation complète d'un personnage entrant dans la vue d'un
/// observateur. La classe générée <c>CNC_CZ_ClientSeeChar</c> de
/// <c>ZoneMessages.g.cs</c> est <b>inutilisable</b> : l'extracteur a
/// aplati le <c>PutBinary(4)</c> des flags d'état (natif
/// <c>char+0x3a</c>) et tronqué les deux <c>longlong</c> HP en i32 —
/// ce codec dédié reproduit le Serialize natif octet par octet.
///
/// Preuve : <c>CNC_CZ_ClientSeeChar::Serialize</c> zone @0x0819ec20
/// + ctor @0x0819e8e0 (offsets des champs lus sur
/// <c>CCharacter</c>). Layout — 96 octets hors chaîne étal :
/// <code>
/// +0  u32  charId          (char+0x34 — sert aussi de handle sight)
/// +4  f32  x               (char+0x28)
/// +8  f32  z               (char+0x2c)
/// +12 f32  dir             (char+0x58)
/// +16 f32  moveSpeed       (vcall+0xd8 GetMovement)
/// +20 u8   atkSpdLv×10/15  (vcall+0x114 GetAttackSpeed(0))
/// +21 u8   idem pour main gauche (GetAttackSpeed(1))
/// +22 4B   CLifeEntityFlags bruts (PutBinary de char+0x3a, voir
///          <see cref="LifeFlags"/> pour le bitset)
/// +26 u16  level           (char+0x470)
/// +28 u16  classLevel      (char+0x47e)
/// +30 i64  hp              (char+0x60)
/// +38 i64  maxHp           (vcall+0xe0)
/// +46 i32  maxHp           (char+0x70 — doublon i32 natif)
/// +50 i32  maxMp           (vcall+0xe4)
/// +54 u8   modelRev        (char+0x4dc — révision d'apparence,
///                          le client rafraîchit si différente)
/// +55 u8   enchantRev      (char+0x280)
/// +56 u8   doRev           (char+0x4dd)
/// +57 u8   familyRev       (char+0x4de)
/// +58 u8   appellationRev  (char+0x4df)
/// +59 s16  castingSpell    (id du sort en cours, 0 sinon)
/// +61 str  stallTitle      (char+0x7ec — titre d'étal, u16+bytes)
/// +~  s16  territoryTeam   (char+0xb74 — 0xffff hors guerre)
/// +~  f32  floatHeight     (char+0x308 — saut/flottement)
/// +~  f32  floatRemain     (vcall+0x1a4 — durée restante)
/// +~  f32  floatDuration   (char+0x30c)
/// +~  s16  floatState      (char+0x314)
/// +~  s16  versus          (char+0xd8c)
/// +~  i32  versusTeam      (char+0xdb0)
/// </code>
/// </summary>
public static class ClientSeeCharCodec
{
    /// <summary>Snapshot des valeurs à sérialiser — découplé
    /// d'<c>EntryState</c> pour rester testable hors boucle.</summary>
    public readonly record struct SeeCharSnapshot(
        uint CharId, float X, float Z, float Facing, float MoveSpeed,
        byte AttackSpeedLevel, byte[] Flags,
        int Level, int ClassLevel, long Hp, long MaxHp, int MaxMp,
        byte ModelRev, short CastingSpell, string StallTitle);

    /// <summary>Calcule les 4 octets de <c>CLifeEntityFlags</c>
    /// (natif char+0x3a) — bitset d'état lu par le client, index de
    /// flag = id d'état (ligne -1 de <c>S_StateDependence.ini</c> =
    /// 0..18, chargée par <c>CStateDBO::GetAllStatesFlagSetting</c>
    /// @0x0836fa40 ; noms <c>T_StateDependence.ini</c>) :
    /// byte0 <c>+0x3a</c> : bit0 mort (state 0 Dead) ;
    /// byte1 <c>+0x3b</c> : bit1 <b>monté</b> (state 9 « mounted » =
    /// word 0x0200 — le bit ride que <c>CEC_Ride::SetState</c>
    /// @0x0854f930 teste via <c>(byte)char+0x3b &amp; 2</c> et que le
    /// client lit pour les records de strafe monture) ;
    /// bit2 = vrai personnage — c'est le gate natif d'émission du
    /// 265 (<c>(byte)+0x3b &amp; 4</c> testé dans BroadCastToCanSee
    /// @0x0805ddb1), bit7 mode transformé ; byte2 <c>+0x3c</c> :
    /// bit0 couple, bit2 mode, bit4 course (StateRun), bit7
    /// invincibilité post-migration ; byte3 <c>+0x3d</c> : bit1
    /// slayer/PK mode.
    /// ⚠ l'« elf invoqué » ne porte <b>aucun</b> bit de ce mot :
    /// <c>CKFElfManager::CallElf</c> @0x08072c30 applique les états
    /// (stealth 10…) au <c>CElfPet</c> — entité propre — jamais au
    /// char. Écrire un bit ici pour l'elf posait le bit9 « mounted »
    /// aux observateurs (bug réel, corrigé 2026-10-25).</summary>
    public static byte[] LifeFlags(bool dead, bool mounted,
        bool invincible)
        => new[]
        {
            (byte)(dead ? 0x01 : 0),
            (byte)((mounted ? 0x02 : 0) | 0x04),
            (byte)(invincible ? 0x80 : 0),
            (byte)0,
        };

    /// <summary>Sérialise le corps du 265 dans l'ordre strict du
    /// Serialize natif (chaque champ = son offset wire en
    /// commentaire ci-dessus).</summary>
    public static byte[] Build(in SeeCharSnapshot s)
    {
        var w = new OutBinStream();
        w.WriteInt((int)s.CharId);            // +0  u32 charId
        w.WriteFloat(s.X);                    // +4  f32 x
        w.WriteFloat(s.Z);                    // +8  f32 z
        w.WriteFloat(s.Facing);               // +12 f32 dir
        w.WriteFloat(s.MoveSpeed);            // +16 f32 GetMovement
        w.WriteByte(s.AttackSpeedLevel);      // +20 u8 atkSpd main droite
        w.WriteByte(s.AttackSpeedLevel);      // +21 u8 atkSpd main gauche
        w.WriteBytes(s.Flags);                // +22 4B CLifeEntityFlags
        w.WriteShort((short)s.Level);         // +26 u16 niveau
        w.WriteShort((short)s.ClassLevel);    // +28 u16 niveau de classe
        w.WriteLong(s.Hp);                    // +30 i64 HP
        w.WriteLong(s.MaxHp);                 // +38 i64 MaxHP
        w.WriteInt((int)s.MaxHp);             // +46 i32 MaxHP (doublon)
        w.WriteInt(s.MaxMp);                  // +50 i32 MaxMP
        w.WriteByte(s.ModelRev);              // +54 u8 révision modèle
        w.WriteByte(0);                       // +55 u8 révision enchants
        w.WriteByte(0);                       // +56 u8 révision Do
        w.WriteByte(0);                       // +57 u8 révision famille
        w.WriteByte(0);                       // +58 u8 révision titre
        w.WriteShort(s.CastingSpell);         // +59 s16 sort en cours
        w.WriteString(s.StallTitle);          // +61 str titre d'étal
        w.WriteShort(unchecked((short)0xffff)); // +~ s16 team guerre
                                              // territoire (0xffff =
                                              // hors guerre, défaut
                                              // natif char+0xb74)
        w.WriteFloat(0);                      // f32 hauteur saut
        w.WriteFloat(0);                      // f32 durée restante
        w.WriteFloat(0);                      // f32 durée flottement
        w.WriteShort(0);                      // s16 état flottement
        w.WriteShort(0);                      // s16 char+0xd8c versus
        w.WriteInt(0);                        // i32 char+0xdb0 team versus
        return w.ToArray();
    }
}
