using Eden.Core.Networking;

namespace Eden.Zone.Handlers;

/// <summary>
/// Opcode 623 `CNE_CZ_ZoneServerUseSkillPointMax` — paquet CUSTOM du
/// client Reforged (feature `btn_max` de `d3d9.dll`, activée par
/// `eden_patch.ini [Features] btn_max=1`).
///
/// Preuve : `my_max_click_handler` dans
/// `EdenReforged/d3d9.dll @0x6d1cff40` — le mod lit le skill_id
/// (objet skill +0x1c4), réécrit le mot NCID du paquet sortant
/// vanilla à `0x26f` (623) puis appelle le MÊME sender que le NCID 12
/// `CNE_CZ_ZoneServerUseSkillPoint` (RVA 0x6cc430). Le payload est
/// donc identique : `[s16 spellId]` — « monte ce sort au niveau max
/// possible selon l'or (pay=1) ou les CP (pay=9) du joueur ».
///
/// ⚠ Collision protocolaire : l'opcode 623 est `CNE_T_ClientGetTicket`
/// côté ticket (généré), jamais enregistré sur le dispatcher zone —
/// le slot est libre côté zone.
/// </summary>
public sealed class CNE_CZ_ZoneServerUseSkillPointMax : NetCommand
{
    /// <inheritdoc/>
    public override int Opcode => 623;

    /// <inheritdoc/>
    public override string Name => "CNE_CZ_ZoneServerUseSkillPointMax";

    /// <summary>Identifiant spellmaster du sort à monter (s16, même
    /// champ que `CNE_CZ_ZoneServerUseSkillPoint.SpellId`).</summary>
    public short SpellId { get; set; }

    /// <inheritdoc/>
    public override void Serialize(OutBinStream stream)
        => stream.WriteShort(SpellId);

    /// <inheritdoc/>
    public override void Deserialize(InBinStream stream)
        => SpellId = stream.ReadShort();
}

/// <summary>
/// Opcode 9057 S→C — pong custom Reborn (hors protocole natif), renvoyé
/// à chaque Echo (C→S 57) pour que le client mesure son RTT (overlay
/// ping, plan A4.4). Corps vide.
/// </summary>
public sealed class CNC_CZ_ClientEchoPong : NetCommand
{
    /// <inheritdoc/>
    public override int Opcode => 9057;

    /// <inheritdoc/>
    public override string Name => "CNC_CZ_ClientEchoPong";

    /// <inheritdoc/>
    public override void Serialize(OutBinStream stream) { }

    /// <inheritdoc/>
    public override void Deserialize(InBinStream stream) { }
}

/// <summary>
/// Opcode 9058 S→C — trait héroïque du perso (create_flag DB), custom
/// Reborn (plan A4.5). [u8 trait] : 0 = aucun, 1..8 = enchant 52000+n
/// (icône NatrueIamge0n).
/// </summary>
public sealed class CNC_CZ_ClientHeroTrait : NetCommand
{
    /// <inheritdoc/>
    public override int Opcode => 9058;

    /// <inheritdoc/>
    public override string Name => "CNC_CZ_ClientHeroTrait";

    /// <summary>0 = aucun, 1..8.</summary>
    public byte Trait { get; set; }

    /// <inheritdoc/>
    public override void Serialize(OutBinStream stream)
        => stream.WriteByte(Trait);

    /// <inheritdoc/>
    public override void Deserialize(InBinStream stream)
        => Trait = stream.ReadByte();
}
