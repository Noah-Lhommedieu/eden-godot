using Eden.Core.Networking;

namespace Eden.Core.Networking.Messages.Login;

/// <summary>
/// Descripteur d'état d'un serveur de monde envoyé au client dans la liste des serveurs.
/// Reversé depuis lapis::SWorldServerStates et corrélé avec la table 'worlds' de PostgreSQL.
/// </summary>
public class WorldServerEntry
{
    public short WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public short Port { get; set; }
    public int Population { get; set; }
    public int MaxNum { get; set; }
    public short Status { get; set; }
    public short CharacterCount { get; set; } // Nombre de personnages du joueur sur ce monde (issu de character_number)
    public short State { get; set; }          // 0 = Normal, 1 = Nouveau, 2 = Occupé, 3 = Plein
    public short ShowOrder { get; set; }
    public short Ping { get; set; }

    public void Serialize(OutBinStream stream)
    {
        stream.WriteShort(WorldId);
        stream.WriteString(WorldName);
        stream.WriteString(IpAddress);
        stream.WriteShort(Port);
        stream.WriteInt(Population);
        stream.WriteShort(Status);
        stream.WriteShort(CharacterCount);
        stream.WriteShort(State);
        stream.WriteShort(ShowOrder);
        stream.WriteShort(Ping);
    }

    public void Deserialize(InBinStream stream)
    {
        WorldId = stream.ReadShort();
        WorldName = stream.ReadString();
        IpAddress = stream.ReadString();
        Port = stream.ReadShort();
        Population = stream.ReadInt();
        Status = stream.ReadShort();
        CharacterCount = stream.ReadShort();
        State = stream.ReadShort();
        ShowOrder = stream.ReadShort();
        Ping = stream.ReadShort();
    }
}

/// <summary>
/// Alias pour compatibilité ascendante avec l'ancien nom de classe.
/// </summary>
public class WorldServerState : WorldServerEntry
{
}
