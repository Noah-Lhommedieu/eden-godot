namespace Eden.Core.Database.Repositories;

/// <summary>Recette métier apprise — ligne native `player_life_skill`
/// (player_id, life_skill_id, exp, due_date). Miroir de la hashtable
/// char+0xb7c (CLifeSkill : [u32 exp][u32 due]).</summary>
public sealed class PlayerLifeSkillEntity
{
    public int PlayerId { get; set; }
    public int LifeSkillId { get; set; }
    public int Exp { get; set; }
    public int DueDate { get; set; }
}

/// <summary>Accès à `player_life_skill` — les commandes DBA natives
/// (InsertLifeSkill/DeleteLifeSkill/ReplaceLifeSkill/UpdateLifeSkillExp)
/// arrivent au même effet sur cette table.</summary>
public interface ILifeSkillRepository
{
    /// <summary>Recettes apprises du personnage (chargement entrée).</summary>
    Task<List<PlayerLifeSkillEntity>> GetByPlayerAsync(int playerId);

    /// <summary>InsertLifeSkill — `INSERT … (exp=0, due=0)`.</summary>
    Task InsertAsync(int playerId, int lifeSkillId);

    /// <summary>Insert groupé des recettes manquantes en UNE requête
    /// (`INSERT … SELECT unnest(@ids)` + `ON CONFLICT DO NOTHING` sur
    /// la PK player_id+life_skill_id) — remplace la boucle
    /// d'`InsertAsync` du seed d'entrée (LoadLifeSkill natif :
    /// recettes de base col11==1 auto-apprises).</summary>
    Task InsertManyAsync(int playerId, IEnumerable<int> lifeSkillIds);

    /// <summary>UpdateLifeSkillExp — `UPDATE … SET exp, due_date`.</summary>
    Task UpdateExpAsync(int playerId, int lifeSkillId, int exp, int dueDate);

    /// <summary>ReplaceLifeSkill — maîtrise : remplace la recette
    /// apprise par la suivante (exp=0).</summary>
    Task ReplaceAsync(int playerId, int oldId, int newId);

    /// <summary>DeleteLifeSkill — oubli manuel (ForgetLifeSkill).</summary>
    Task DeleteAsync(int playerId, int lifeSkillId);
}
