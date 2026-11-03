using Eden.Core.Networking.Messages.Generated;

namespace Eden.Zone.Codecs;

/// <summary>
/// Mot d'état <c>CLifeEntityFlags</c> (natif <c>char+0x3a</c>, lu
/// côté client dans <c>actor+0x3c</c>) porté <b>entier</b> par chaque
/// NCID 302 <c>CNC_CZ_ClientFlagModify</c> — le client écrase son mot
/// local avec la valeur reçue, ce n'est jamais un delta.
///
/// Wire (corps 9B, preuve <c>CStateController::NodifyFlagChange</c>
/// zone @0x084e3c00 — <c>Serialize</c> = <c>[u32 handle(char+0x34)]
/// [u32 mot(char+0x3a)][u8 seq(char+0x280)]</c>, émis
/// BroadCastToCanSee + NotifyClient ; le codec généré éclate le mot
/// en <c>[u16 flags][u8 sel][u8 op]</c> little-endian :
/// sel = bits16-23, op = bits24-31) :
///
/// Bits prouvés du mot (index de flag = id d'état — ligne -1
/// <c>0,1,…,18</c> de <c>S_StateDependence.ini</c> chargée par
/// <c>CStateDBO::GetAllStatesFlagSetting</c> @0x0836fa40 ; noms dans
/// <c>T_StateDependence.ini</c>) :
///   bit0  0x00000001 = state 0  « Dead »
///   bit9  0x00000200 = state 9  « mounted » — posé par
///         <c>CStateController::Do(9)</c> @0x084e3dd0 via
///         <c>LifeEntityDo(entity,9)</c> dans
///         <c>CEC_Ride::SetState</c> @0x0854f330 (qui teste
///         <c>(byte)char+0x3b &amp; 2</c> comme marqueur « déjà
///         monté »). C'est CE bit que le client teste
///         (<c>player+0x3c bit9|bit17</c>) pour sélectionner les
///         records de strafe monture (pivot ±90°).
///   bit13 0x00002000 = chaîne d'action (posé au 1er cast, purgé à
///         la sortie de combat — captures zsniff).
///   bit17 0x00020000 = state 17 « changing form » (strafes monture
///         aussi côté client).
///   bit18 0x00040000 = state 18 « in battle » — le « sel=4 » des
///         captures combat.
///   bit24 0x01000000 = « vrai personnage » (op=1 constant sur
///         toutes les émissions joueur, base 0x01000000 du 302
///         post-migration ; les mobs émettent op=0).
/// </summary>
public static class StateFlagWord
{
    /// <summary>Mot de base d'un personnage joueur — bit24 « vrai
    /// perso » (op=1 des captures + du 302 post-migration).</summary>
    public const uint PlayerBase = 0x01000000;
    /// <summary>bit9 — state 9 « mounted » (ride).</summary>
    public const uint Mounted = 0x00000200;
    /// <summary>bit0 — state 0 « Dead ».</summary>
    public const uint Dead = 0x00000001;
    /// <summary>bit13 — chaîne d'action (1er cast).</summary>
    public const uint ActionChain = 0x00002000;
    /// <summary>bit18 — state 18 « in battle » (sel=4).</summary>
    public const uint InBattle = 0x00040000;

    /// <summary>Recompose le mot d'état courant d'un joueur depuis
    /// les flags suivis côté serveur — l'équivalent de la lecture de
    /// <c>char+0x3a</c> par <c>NodifyFlagChange</c> natif.</summary>
    public static uint Compose(bool dead, bool mounted,
        bool inBattle, bool actionChain) =>
        PlayerBase
        | (dead ? Dead : 0u)
        | (mounted ? Mounted : 0u)
        | (inBattle ? InBattle : 0u)
        | (actionChain ? ActionChain : 0u);

    /// <summary>Instancie le 302 wire — mapping mot → champs du codec
    /// généré : <c>[u32 handle][u16 flags=bits0-15][u8 sel=bits16-23]
    /// [u8 op=bits24-31][u8 seq]</c> (<c>seq</c> = compteur
    /// <c>char+0x280</c> natif, incrémenté à chaque 302 —
    /// EntryState.NextAction côté serveur).</summary>
    public static CNC_CZ_ClientFlagModify BuildCommand(uint handle,
        uint word, byte seq) => new()
    {
        Handle = handle,
        Flags = (ushort)(word & 0xffff),      // bits 0-15 (ride bit9)
        Selector = (byte)(word >> 16),        // bits 16-23 (battle bit18)
        Operation = (byte)(word >> 24),       // bits 24-31 (« perso »)
        ActionId = seq,                       // u8 : seq char+0x280
    };
}
