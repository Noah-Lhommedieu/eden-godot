namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Requête envoyée par le client pour obtenir la liste de ses personnages sur le monde.
/// Reversé depuis lapis::CNC_CW_ClientGetCharacters (opcode 10).
/// </summary>
public sealed class CharacterListRequest : NetCommand
{
    public override int Opcode => 10;
    public override string Name => "CNC_CW_ClientGetCharacters";

    public int AccountId { get; set; }
    public byte[] SessionTicket { get; set; } = Array.Empty<byte>();

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteInt(AccountId);
        stream.WriteInt(SessionTicket.Length);
        if (SessionTicket.Length > 0)
        {
            stream.WriteBytes(SessionTicket, SessionTicket.Length);
        }
    }

    public override void Deserialize(InBinStream stream)
    {
        AccountId = stream.ReadInt();
        var ticketLength = stream.ReadInt();
        SessionTicket = ticketLength > 0 ? stream.ReadBytes(ticketLength) : Array.Empty<byte>();
    }
}
