using Dapper;
using Npgsql;

namespace Eden.Core.Database;

/// <summary>
/// Allocateur global de serials d'items — séquence PostgreSQL
/// <c>item_serial</c> (migration <c>Database/012-item-serial-sequence.sql</c>).
/// Les tables d'instanciation d'items (inventory1/2, equipment1/2,
/// storage1/2, mailitem, family_storage…) n'ont PAS de séquence
/// propre ; le serveur natif partage un espace d'ids unique entre
/// tous les conteneurs (désasm zone, init CItemDBO) — un serial
/// n'est jamais réutilisé entre sacs, banque, mail ou équipement.
///
/// <c>nextval</c> remplace l'ancien compteur seedé max(id)+1 +
/// Interlocked : celui-ci ne coordonnait qu'un seul processus — la
/// zone ET la mission allouant dans le même espace, les serials se
/// télescopaient (collisions PK sur inventory1/mailitem). Coût :
/// un aller-retour DB par appel — assumé, les appels sont déjà des
/// chemins DB et non des hot loops.
/// </summary>
public static class ItemSerialAllocator
{
    /// <summary>Nom de la séquence partagée (migration 012 —
    /// seedée à max(id)+1 des tables
    /// <see cref="Repositories.InventoryRepository.ItemSerialTables"/>).</summary>
    internal const string SequenceName = "item_serial";

    /// <summary>Prochain serial d'item — atomique et cross-process.</summary>
    public static async Task<int> NextAsync(NpgsqlDataSource db)
    {
        using var connection = db.CreateConnection();
        return await NextAsync(connection, null);
    }

    /// <summary>Variante pour un appelant déjà dans une connexion /
    /// transaction. Un serial consommé puis rollback laisse un trou
    /// d'ids — sans conséquence (le natif aussi sauté des serials).</summary>
    public static async Task<int> NextAsync(NpgsqlConnection connection,
        NpgsqlTransaction? tx = null)
        // nextval rend bigint — cast borné : les colonnes id sont int4.
        => checked((int)await connection.ExecuteScalarAsync<long>(
            $"SELECT nextval('{SequenceName}')", transaction: tx));
}
