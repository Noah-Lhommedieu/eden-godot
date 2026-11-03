namespace Eden.Core.Database.Entities;

/// <summary>
/// Compteur du nombre de personnages possédés par un compte sur chaque serveur de monde.
/// Stocké dans 'edenfr_account' (table 'character_number').
/// </summary>
public sealed class CharacterNumberEntity
{
    public int AccountId { get; set; }
    public int WorldId { get; set; }
    public int CharacterCount { get; set; }
}
