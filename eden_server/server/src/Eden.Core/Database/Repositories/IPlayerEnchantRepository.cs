namespace Eden.Core.Database.Repositories;

/// <summary>Ligne persistée d'un enchant actif — table native
/// `player_enchants` (cols : player_id, effect_id, caster_id,
/// rest_time, factor, counter, sets, note, stack_count, level).
/// Miroir des logs natifs `Insert/Update/Delete Enchant
/// caster_id=%d effect_id=%hd rest_time=%d factor=%d counter=%d
/// sets=%s stack=%hd level=%hd`.</summary>
public sealed class PlayerEnchantEntity
{
    public int PlayerId { get; set; }
    /// <summary>Id S_Enchant (u16 natif — peut dépasser 32767, la
    /// colonne est integer donc pas de normalisation requise).</summary>
    public int EffectId { get; set; }
    /// <summary>charId du lanceur (0 = effet système/item).</summary>
    public int CasterId { get; set; }
    /// <summary>Temps restant en secondes (-1/-2 = infini natif).</summary>
    public int RestTime { get; set; }
    /// <summary>Facteur générique de l'enchant (param_5 du ctor
    /// CEnchant natif — ex. valeur d'upgrade du sort).</summary>
    public int Factor { get; set; }
    /// <summary>Compteur interne (CEnchant+0x34 — bits de pause
    /// inclus).</summary>
    public int Counter { get; set; }
    /// <summary>Chaîne « sets » native (CEnchant+0x28 — flags
    /// textuels, ex. "pause"). Vide si aucun.</summary>
    public string Sets { get; set; } = string.Empty;
    /// <summary>Nombre de stacks de l'icône (u16 affiché).</summary>
    public int StackCount { get; set; } = 1;
    /// <summary>Niveau de l'enchant (palier du sort lanceur).</summary>
    public int Level { get; set; } = 1;
}

/// <summary>Accès à `player_enchants` — persistance native des
/// statuts actifs (buffs, HoT, invincibilité de migration) pour
/// restauration à la reconnexion.</summary>
public interface IPlayerEnchantRepository
{
    /// <summary>Tous les enchants actifs du joueur.</summary>
    Task<IReadOnlyList<PlayerEnchantEntity>> GetEnchantsAsync(int playerId);
    /// <summary>Insert/update natif — upsert par (player, effect) :
    /// rafraîchit rest_time/factor/counter/sets/stack/level.</summary>
    Task UpsertEnchantAsync(PlayerEnchantEntity e);
    /// <summary>`Delete Enchant` natif — retire un statut expiré ou
    /// annulé.</summary>
    Task DeleteEnchantAsync(int playerId, int effectId);
    /// <summary>Purge totale (mort/nettoyage entrée).</summary>
    Task DeleteAllAsync(int playerId);
}
