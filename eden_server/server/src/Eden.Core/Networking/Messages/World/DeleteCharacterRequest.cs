namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Requête envoyée par le client pour supprimer un personnage.
/// Reversé depuis lapis::CNC_CW_DeleteCharacter (opcode 14).
/// </summary>
public sealed class DeleteCharacterRequest : NetCommand
{
    public override int Opcode => 14;
    public override string Name => "CNC_CW_DeleteCharacter";

    public int CharacterId { get; set; }
    public string SecurityPassword { get; set; } = string.Empty;

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteInt(CharacterId);
        stream.WriteString(SecurityPassword);
    }

    public override void Deserialize(InBinStream stream)
    {
        CharacterId = stream.ReadInt();
        SecurityPassword = stream.ReadString();
    }
}
