namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Réponse envoyée par le WorldServer suite à une demande de suppression de personnage.
/// Reversé depuis lapis::CNC_CW_DeleteCharacterResult (opcode 15).
/// </summary>
public sealed class DeleteCharacterResponse : NetCommand
{
    public override int Opcode => 15;
    public override string Name => "CNC_CW_DeleteCharacterResult";

    public bool Success { get; set; }
    public int CharacterId { get; set; }
    public string Message { get; set; } = string.Empty;

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteByte(Success ? (byte)1 : (byte)0);
        stream.WriteInt(CharacterId);
        stream.WriteString(Message);
    }

    public override void Deserialize(InBinStream stream)
    {
        Success = stream.ReadByte() == 1;
        CharacterId = stream.ReadInt();
        Message = stream.ReadString();
    }
}
