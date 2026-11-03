namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Requête envoyée par le client pour entrer en jeu avec le personnage sélectionné.
/// Reversé depuis lapis::CNC_CW_SelectCharacter (opcode 16).
/// </summary>
public sealed class SelectCharacterRequest : NetCommand
{
    public override int Opcode => 16;
    public override string Name => "CNC_CW_SelectCharacter";

    public int CharacterId { get; set; }

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteInt(CharacterId);
    }

    public override void Deserialize(InBinStream stream)
    {
        CharacterId = stream.ReadInt();
    }
}
