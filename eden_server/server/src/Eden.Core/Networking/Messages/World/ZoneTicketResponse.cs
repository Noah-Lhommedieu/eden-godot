namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Réponse contenant le ticket d'accès et l'adresse IP / Port du ZoneServer vers lequel le joueur est redirigé.
/// Reversé depuis lapis::CNC_CW_ClientReceiveTicketToZoneServer (opcode 17).
/// </summary>
public sealed class ZoneTicketResponse : NetCommand
{
    public const int TicketLength = 22;

    public override int Opcode => 17;
    public override string Name => "CNC_CW_ClientReceiveTicketToZoneServer";

    public string ZoneServerIp { get; set; } = string.Empty;
    public int ZoneServerPort { get; set; }
    public byte[] ZoneTicket { get; set; } = new byte[TicketLength];

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteString(ZoneServerIp);
        stream.WriteInt(ZoneServerPort);
        stream.WriteBytes(ZoneTicket, TicketLength);
    }

    public override void Deserialize(InBinStream stream)
    {
        ZoneServerIp = stream.ReadString();
        ZoneServerPort = stream.ReadInt();
        ZoneTicket = stream.ReadBytes(TicketLength);
    }
}
