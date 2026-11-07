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

/// <summary>
/// Opcode 9060 S→C — offre d'entrée de donjon (custom Reborn, hors
/// protocole natif) : le joueur est dans l'aire du portail d'un donjon à
/// deux intensités. Corps `[s16 soloNode][s16 multiNode][u8 soloLeft]
/// [u8 multiLeft][u8 maxParJour][u8 illimité]` — le choix revient par
/// <see cref="CNE_CZ_ZoneServerDungeonEnter"/>.
/// </summary>
public sealed class CNC_CZ_ClientDungeonOffer : NetCommand
{
    public override int Opcode => 9060;
    public override string Name => "CNC_CZ_ClientDungeonOffer";
    public short SoloNode { get; set; }
    public short MultiNode { get; set; }
    public byte SoloLeft { get; set; }
    public byte MultiLeft { get; set; }
    public byte Max { get; set; }
    public bool Unlimited { get; set; }

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteShort(SoloNode);
        stream.WriteShort(MultiNode);
        stream.WriteByte(SoloLeft);
        stream.WriteByte(MultiLeft);
        stream.WriteByte(Max);
        stream.WriteBool(Unlimited);
    }

    public override void Deserialize(InBinStream stream)
    {
        SoloNode = stream.ReadShort();
        MultiNode = stream.ReadShort();
        SoloLeft = stream.ReadByte();
        MultiLeft = stream.ReadByte();
        Max = stream.ReadByte();
        Unlimited = stream.ReadByte() != 0;
    }
}

/// <summary>
/// Opcode 9061 C→S — choix d'intensité : `[s16 node]` (node solo ou
/// multi de l'offre reçue). Le serveur revalide tout (position dans le
/// portail, niveau, entrées du jour).
/// </summary>
public sealed class CNE_CZ_ZoneServerDungeonEnter : NetCommand
{
    public override int Opcode => 9061;
    public override string Name => "CNE_CZ_ZoneServerDungeonEnter";
    public short Node { get; set; }
    public override void Serialize(OutBinStream stream) =>
        stream.WriteShort(Node);
    public override void Deserialize(InBinStream stream) =>
        Node = stream.ReadShort();
}

/// <summary>
/// Opcode 9062 S→C — portails de donjon du node courant (custom Reborn),
/// envoyé après MigrationDone : `[u8 n]` puis n × `[f32 x][f32 y][f32 z]
/// [f32 rayon]` (repère scène). Le client y pose l'animation de portail.
/// </summary>
public sealed class CNC_CZ_ClientDungeonPortals : NetCommand
{
    public override int Opcode => 9062;
    public override string Name => "CNC_CZ_ClientDungeonPortals";
    public List<(float X, float Y, float Z, float R)> Portals { get; } = new();

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteByte((byte)Portals.Count);
        foreach (var (x, y, z, r) in Portals)
        {
            stream.WriteFloat(x); stream.WriteFloat(y);
            stream.WriteFloat(z); stream.WriteFloat(r);
        }
    }

    public override void Deserialize(InBinStream stream)
    {
        Portals.Clear();
        var n = stream.ReadByte();
        for (var i = 0; i < n; i++)
            Portals.Add((stream.ReadFloat(), stream.ReadFloat(),
                stream.ReadFloat(), stream.ReadFloat()));
    }
}
