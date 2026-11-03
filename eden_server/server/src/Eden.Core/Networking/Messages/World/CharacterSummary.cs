using Eden.Core.Networking;

namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Résumé des informations d'un personnage pour l'écran de sélection de personnage.
/// </summary>
public sealed class CharacterSummary
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public int ClassId { get; set; }
    public int RaceId { get; set; }
    public int GenderId { get; set; }
    public int HairStyle { get; set; }
    public int FaceStyle { get; set; }
    public int HairColor { get; set; }
    public int SkinColor { get; set; }
    public int NodeId { get; set; }

    public void Serialize(OutBinStream stream)
    {
        stream.WriteInt(CharacterId);
        stream.WriteString(Name);
        stream.WriteInt(Level);
        stream.WriteInt(ClassId);
        stream.WriteInt(RaceId);
        stream.WriteInt(GenderId);
        stream.WriteInt(HairStyle);
        stream.WriteInt(FaceStyle);
        stream.WriteInt(HairColor);
        stream.WriteInt(SkinColor);
        stream.WriteInt(NodeId);
    }

    public void Deserialize(InBinStream stream)
    {
        CharacterId = stream.ReadInt();
        Name = stream.ReadString();
        Level = stream.ReadInt();
        ClassId = stream.ReadInt();
        RaceId = stream.ReadInt();
        GenderId = stream.ReadInt();
        HairStyle = stream.ReadInt();
        FaceStyle = stream.ReadInt();
        HairColor = stream.ReadInt();
        SkinColor = stream.ReadInt();
        NodeId = stream.ReadInt();
    }
}
