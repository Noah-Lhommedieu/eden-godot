using System.Threading.Tasks;
using Dapper;
using Limitless.Studio.Models;

namespace Limitless.Studio.Services;

/// <summary>Écriture des éditions — UPDATE des tables items/monstres
/// du jeu via Dapper (fndb1). Le .ini est la source maître en dev ;
/// l'écriture BDD n'est faite que pour les champs persistés.</summary>
public sealed class EditService
{
    private readonly GameDb _db;
    public EditService(GameDb db) => _db = db;

    /// <summary>Renomme un item en BDD (si la table/colonne existe —
    /// la V1 tolère l'absence : les .ini restent la source).</summary>
    public async Task<bool> RenameItemAsync(GameItem it)
    {
        try
        {
            using var d = _db.Open();
            var n = await d.ExecuteAsync(
                "UPDATE items SET name=@n WHERE item_id=@i",
                new { n = it.Name, i = it.Id });
            return n > 0;
        }
        catch { return false; }
    }

    public async Task<bool> RenameMonsterAsync(GameMonster m)
    {
        try
        {
            using var d = _db.Open();
            var n = await d.ExecuteAsync(
                "UPDATE monsters SET name=@n WHERE mob_id=@i",
                new { n = m.Name, i = m.Id });
            return n > 0;
        }
        catch { return false; }
    }
}
