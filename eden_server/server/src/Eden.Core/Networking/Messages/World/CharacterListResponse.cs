namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Réponse contenant la liste des personnages du compte envoyée par le WorldServer.
/// Reversé depuis lapis::CNC_CW_ServerSendCharacters (opcode 11).
/// </summary>
public sealed class CharacterListResponse : NetCommand
{
    public override int Opcode => 11;
    public override string Name => "CNC_CW_ServerSendCharacters";

    public List<CharacterSummary> Characters { get; set; } = new();

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteInt(Characters.Count);
        foreach (var character in Characters)
        {
            character.Serialize(stream);
        }
    }

    public override void Deserialize(InBinStream stream)
    {
        var count = stream.ReadInt();
        Characters = new List<CharacterSummary>(count);
        for (var i = 0; i < count; i++)
        {
            var character = new CharacterSummary();
            character.Deserialize(stream);
            Characters.Add(character);
        }
    }
}
