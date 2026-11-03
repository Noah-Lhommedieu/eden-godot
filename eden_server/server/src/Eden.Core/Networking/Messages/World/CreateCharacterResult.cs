namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Résultat de création de personnage
/// </summary>
public sealed class CreateCharacterResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public int? CharacterId { get; set; }
}
