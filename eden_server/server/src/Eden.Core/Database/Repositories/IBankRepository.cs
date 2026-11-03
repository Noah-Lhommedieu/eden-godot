using Eden.Core.Game.Bank;

namespace Eden.Core.Database.Repositories;

public interface IBankRepository
{
    Task<int?> GetAvailableSlotAsync(int characterId, int maxSlots = 50);
    Task<BankItemEntity?> GetBankItemAsync(int characterId, int slot);
    Task<IReadOnlyList<BankItemEntity>> GetBankItemsByCharacterIdAsync(int characterId);
    Task AddBankItemAsync(BankItemEntity item);
    Task<bool> RemoveBankItemAsync(int characterId, int slot);
    Task<bool> UpdateBankItemAsync(BankItemEntity item);
    /// <summary>`player_characters.expand_storage_cnt` — expansions
    /// payées de la banque (natif : taille de stockage par joueur).</summary>
    Task<int> GetStorageExpandCountAsync(int characterId);
    /// <summary>Écrit `expand_storage_cnt` — `CPlayerFactory::
    /// ExpandStorage` incrémente `char+0x77c` après débit de l'or
    /// (sauvegardé via `SavePlayerCharacter "gold expand_storage_cnt"`).</summary>
    Task SetStorageExpandCountAsync(int characterId, int count);
}
