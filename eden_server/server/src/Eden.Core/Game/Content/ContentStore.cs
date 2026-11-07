using System.Text.Json;
using Dapper;
using Npgsql;

namespace Eden.Core.Game.Content;

/// <summary>Un objet du contenu statique (table <c>item.item</c>).</summary>
public sealed record ContentItem(int ItemId, string NameFr, int ItemKind, int RequiredLevel, int MaxLevel,
    int BuyPriceSilver, string IconCode, bool InScope);

/// <summary>Accès au contenu statique : une seule interface, deux implémentations (PostgreSQL, export JSONL de secours).</summary>
public interface IContentStore
{
    Task<IReadOnlyList<ContentItem>> ItemsAsync();
}

/// <summary>Lecture de l'export de secours <c>content/export/item.item.jsonl</c> : démarrage sans base (lecture seule).</summary>
public sealed class JsonlContentStore : IContentStore
{
    private readonly string _dir;
    public JsonlContentStore(string exportDir) => _dir = exportDir;

    public async Task<IReadOnlyList<ContentItem>> ItemsAsync()
    {
        var res = new List<ContentItem>();
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(_dir, "item.item.jsonl")))
        {
            if (line.Length == 0) continue;
            using var d = JsonDocument.Parse(line); var r = d.RootElement;
            res.Add(new ContentItem(r.GetProperty("item_id").GetInt32(), r.GetProperty("name_fr").GetString()!,
                r.GetProperty("item_kind").GetInt32(), r.GetProperty("required_level").GetInt32(),
                r.GetProperty("max_level").GetInt32(), r.GetProperty("buy_price_silver").GetInt32(),
                r.GetProperty("icon_code").GetString()!, r.GetProperty("in_scope").GetBoolean()));
        }
        return res;
    }
}

/// <summary>
/// Contenu dans <c>eden_content</c>. Une édition d'objet (<see cref="EditItemAsync"/>) est inscrite dans
/// <c>history.change</c> dans la MÊME transaction ; <see cref="RevertAsync"/> écrit la modification inverse (tracée).
/// Seules les colonnes de <see cref="EditableItemFields"/> sont modifiables.
/// </summary>
public sealed class PgContentStore : IContentStore
{
    public static readonly IReadOnlySet<string> EditableItemFields =
        new HashSet<string> { "name_fr", "required_level", "max_level", "buy_price_silver" };

    private sealed record OldValue(string Name, string Old);
    private sealed record ChangeRow(int EntityId, string Field, string? Old, long? RevertedBy);

    private readonly NpgsqlDataSource _db;
    public PgContentStore(NpgsqlDataSource db) => _db = db;

    public async Task<IReadOnlyList<ContentItem>> ItemsAsync()
    {
        await using var c = await _db.OpenConnectionAsync();
        return (await c.QueryAsync<ContentItem>(
            "SELECT item_id AS ItemId, name_fr AS NameFr, item_kind AS ItemKind, required_level AS RequiredLevel, " +
            "max_level AS MaxLevel, buy_price_silver AS BuyPriceSilver, icon_code AS IconCode, in_scope AS InScope " +
            "FROM item.item ORDER BY item_id")).ToList();
    }

    /// <summary>Modifie un champ d'objet et inscrit le changement ; renvoie le <c>change_id</c>.</summary>
    public async Task<long> EditItemAsync(int itemId, string field, string newValue, string account, string? reason)
    {
        if (!EditableItemFields.Contains(field)) throw new ArgumentException($"champ non modifiable : {field}");
        await using var c = await _db.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync();
        var row = await c.QuerySingleOrDefaultAsync<OldValue>(
            $"SELECT name_fr AS Name, {field}::text AS Old FROM item.item WHERE item_id = @itemId FOR UPDATE", new { itemId }, tx)
            ?? throw new KeyNotFoundException($"objet {itemId} inconnu");
        await c.ExecuteAsync($"UPDATE item.item SET {field} = (@v)::{(field == "name_fr" ? "text" : "integer")} WHERE item_id = @itemId",
            new { v = newValue, itemId }, tx);
        var id = await c.ExecuteScalarAsync<long>(
            "INSERT INTO history.change(entity_type, entity_id, entity_name, field, old_value, new_value, edited_by_account, reason) " +
            "VALUES ('item', @itemId, @name, @field, @old, @newValue, @account, @reason) RETURNING change_id",
            new { itemId, name = row.Name, field, old = row.Old, newValue, account, reason }, tx);
        await tx.CommitAsync();
        return id;
    }

    /// <summary>Annule une modification : écrit la modification inverse (elle-même tracée) et marque l'originale.</summary>
    public async Task<long> RevertAsync(long changeId, string account)
    {
        await using var c = await _db.OpenConnectionAsync();
        var ch = await c.QuerySingleOrDefaultAsync<ChangeRow>(
            "SELECT entity_id AS EntityId, field AS Field, old_value AS Old, reverted_by AS RevertedBy FROM history.change " +
            "WHERE change_id = @changeId AND entity_type = 'item'", new { changeId })
            ?? throw new KeyNotFoundException($"modification {changeId} inconnue");
        if (ch.RevertedBy != null) throw new InvalidOperationException($"modification {changeId} déjà annulée");
        var inverse = await EditItemAsync(ch.EntityId, ch.Field, ch.Old ?? "", account, $"annulation de #{changeId}");
        await c.ExecuteAsync("UPDATE history.change SET reverted_by = @inverse WHERE change_id = @changeId", new { inverse, changeId });
        return inverse;
    }
}

/// <summary>
/// Ouvre le contenu : PostgreSQL si la base répond, sinon l'export JSONL de secours (lecture seule) — le jeu démarre
/// même base arrêtée. <see cref="UsedFallback"/> dit quelle source a été choisie.
/// </summary>
public static class ContentStoreFactory
{
    public static async Task<(IContentStore Store, bool UsedFallback)> OpenAsync(string? connection, string exportDir)
    {
        if (!string.IsNullOrWhiteSpace(connection))
        {
            try
            {
                var ds = NpgsqlDataSource.Create(connection);
                await using var c = await ds.OpenConnectionAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
                return (new PgContentStore(ds), false);
            }
            catch (Exception e) when (e is NpgsqlException or OperationCanceledException or System.Net.Sockets.SocketException)
            {
                Logging.EdenLog.Write($"[Content] base indisponible ({e.GetType().Name}) : démarrage depuis {exportDir}");
            }
        }
        return (new JsonlContentStore(exportDir), true);
    }
}
