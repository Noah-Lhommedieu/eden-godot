using Eden.Core.Database.Entities;
using Eden.Core.Networking.Messages.World;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Interface d'accès aux personnages joueurs.
/// </summary>
public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterSummary>> GetCharacterSummariesByAccountIdAsync(int accountId);
    Task<PlayerCharacterEntity?> GetCharacterByIdAsync(int characterId);
    Task<PlayerCharacterEntity?> GetCharacterByNameAsync(string characterName);
    Task<int> CreateCharacterAsync(PlayerCharacterEntity character);
    Task<bool> DeleteCharacterAsync(int characterId, int accountId = 0);
    Task UpdateCoordinatesAsync(int characterId, int nodeId, double x, double y, double z, double dir);
    Task UpdateStatsAsync(int characterId, int hp, int mp, long exp, int level);
    /// <summary>Solde d'or du personnage.</summary>
    Task<long> GetGoldAsync(int characterId);
    /// <summary>Fixe le solde d'or (vente/achat/trade).</summary>
    Task SetGoldAsync(int characterId, long gold);
    /// <summary>Ajoute de l'or (atomique) — natif
    /// `CCharacter::GainGold` @0x842a7f0 : `char+0x4b0 += n` borné à
    /// [0, 999999999]. Retourne le nouveau total.</summary>
    Task<long> AddGoldAsync(int characterId, long amount);
    /// <summary>Met à jour l'XP et le niveau (ne touche pas HP/MP).</summary>
    Task UpdateExpAsync(int characterId, long exp, int level);

    /// <summary>Positionne les points de classe/talents (skill_point).</summary>
    Task SetSkillPointAsync(int characterId, int skillPoint);
    /// <summary>Lit l'XP courante du personnage.</summary>
    Task<long> GetExpAsync(int characterId);
    Task UpdateCharacterLocationAndStatsAsync(int characterId, int nodeId, double x, double y, double z, double dir, int hp, int mp);
    /// <summary>Mérites spéciaux (table `special_coins`) —
    /// couples (type 0-11, total), colonne coinNN ↔ type NN-1.</summary>
    Task<IReadOnlyList<(short Type, int Total)>> GetSpecialCoinsAsync(int characterId);
}
