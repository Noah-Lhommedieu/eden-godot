namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Réponse envoyée par le WorldServer suite à une tentative de création de personnage.
/// Reversé depuis lapis::CNC_CW_CreateCharacterResult (opcode 13).
/// </summary>
public sealed class CreateCharacterResponse : NetCommand
{
    public override int Opcode => 13;
    public override string Name => "CNC_CW_CreateCharacterResult";

    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public CharacterSummary? Character { get; set; }

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteByte(Success ? (byte)1 : (byte)0);
        stream.WriteString(ErrorMessage);
        if (Success && Character != null)
        {
            stream.WriteByte(1);
            Character.Serialize(stream);
        }
        else
        {
            stream.WriteByte(0);
        }
    }

    public override void Deserialize(InBinStream stream)
    {
        Success = stream.ReadByte() == 1;
        ErrorMessage = stream.ReadString();
        var hasChar = stream.ReadByte() == 1;
        if (hasChar)
        {
            Character = new CharacterSummary();
            Character.Deserialize(stream);
        }
    }
}
