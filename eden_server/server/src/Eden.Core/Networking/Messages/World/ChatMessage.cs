namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Types de canaux de discussion en jeu.
/// </summary>
public enum ChatChannelType : short
{
    Normal = 0,
    Whisper = 1,
    Party = 2,
    Guild = 3,
    Shout = 4,
    World = 5,
    System = 6
}

/// <summary>
/// Message de chat textuel échangé entre le client et le WorldServer.
/// Reversé depuis lapis::CNC_CW_ClientChannelMsg (opcode 20).
/// </summary>
public sealed class ChatMessage : NetCommand
{
    public override int Opcode => 20;
    public override string Name => "CNC_CW_ClientChannelMsg";

    public short Channel { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string Message
    {
        get => MessageText;
        set => MessageText = value;
    }
    public int TargetCharacterId { get; set; }

    public ChatChannelType ChannelType
    {
        get => (ChatChannelType)Channel;
        set => Channel = (short)value;
    }

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteShort(Channel);
        stream.WriteString(SenderName);
        stream.WriteString(MessageText);
        stream.WriteInt(TargetCharacterId);
    }

    public override void Deserialize(InBinStream stream)
    {
        Channel = stream.ReadShort();
        SenderName = stream.ReadString();
        MessageText = stream.ReadString();
        TargetCharacterId = stream.ReadInt();
    }
}
