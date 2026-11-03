using Eden.Core.Networking;

namespace Eden.Core.Networking.Messages.Zone;

/// <summary>
/// Paquet d'application de dégâts et mise à jour de combat.
/// Reversé depuis lapis::CNC_CZ_ClientDamage.
/// </summary>
public sealed class ZoneDamageMessage : NetCommand
{
    public override int Opcode => 0x0302;
    public override string Name => "CNC_CZ_ClientDamage";

    public int AttackerEntityId { get; set; }
    public int TargetEntityId { get; set; }
    public int DamageAmount { get; set; }
    public bool IsCritical { get; set; }
    public bool IsDodged { get; set; }
    public int RemainingHp { get; set; }

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteInt(AttackerEntityId);
        stream.WriteInt(TargetEntityId);
        stream.WriteInt(DamageAmount);
        stream.WriteByte((byte)(IsCritical ? 1 : 0));
        stream.WriteByte((byte)(IsDodged ? 1 : 0));
        stream.WriteInt(RemainingHp);
    }

    public override void Deserialize(InBinStream stream)
    {
        AttackerEntityId = stream.ReadInt();
        TargetEntityId = stream.ReadInt();
        DamageAmount = stream.ReadInt();
        IsCritical = stream.ReadByte() != 0;
        IsDodged = stream.ReadByte() != 0;
        RemainingHp = stream.ReadInt();
    }
}
