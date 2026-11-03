namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Requête de création de personnage (Client → World)
/// Correspond à CNE_CW_ServerCreateCharacterData
/// </summary>
public sealed class CreateCharacterRequest
{
    public string GivenName { get; set; } = string.Empty;
    public byte SlotIndex { get; set; }
    public short RaceId { get; set; }
    public short GenderId { get; set; }
    public short ClassId { get; set; }
    public short FaceStyle { get; set; }
    public short HairStyle { get; set; }
    public short HairColor { get; set; }
    public short SkinColor { get; set; }
}
