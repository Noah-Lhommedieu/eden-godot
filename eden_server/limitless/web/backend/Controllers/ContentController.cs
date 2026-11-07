using System.Data;
using Dapper;
using Limitless.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Limitless.Api.Controllers;

/// <summary>
/// Wiki et édition du contenu STATIQUE (base <c>eden_content</c>, PLAN-ACTION-3 lot D). Lecture publique, limitée par
/// défaut au contenu « présent en jeu ≤ Lv90 » (<c>in_scope</c>) ; édition réservée au rôle Admin (par COMPTE), chaque
/// modification est inscrite dans <c>history.change</c> dans la même transaction et peut être annulée.
/// </summary>
[ApiController]
[Route("api/content")]
public sealed class ContentController : ControllerBase
{
    private readonly Db _db;
    public ContentController(Db db) => _db = db;

    /// <summary>Entités du wiki : table, clé, colonne de recherche, colonnes renvoyées en liste, colonnes modifiables.</summary>
    private sealed record Kind(string Table, string Key, string Name, string[] ListCols, string[] Editable);

    private static readonly Dictionary<string, Kind> Kinds = new()
    {
        ["items"] = new("item.item", "item_id", "name_fr", new[] { "item_id", "name_fr", "item_kind", "required_level", "buy_price_silver", "in_scope" },
            new[] { "name_fr", "required_level", "max_level", "buy_price_silver" }),
        ["monsters"] = new("creature.monster", "monster_id", "name_fr", new[] { "monster_id", "name_fr", "level", "monster_type", "hp", "exp_reward", "in_scope" },
            new[] { "name_fr" }),
        ["npcs"] = new("npc.npc", "npc_id", "name_fr", new[] { "npc_id", "name_fr", "npc_type", "level", "in_scope" }, new[] { "name_fr" }),
        ["quests"] = new("quest.quest", "quest_id", "name_fr", new[] { "quest_id", "name_fr", "min_level", "category", "is_daily", "in_scope" },
            new[] { "name_fr" }),
        ["maps"] = new("map.map", "node_id", "name_fr", new[] { "node_id", "name_fr", "node_type", "map_kind", "min_level", "in_scope" },
            new[] { "name_fr", "min_level" }),
        ["recipes"] = new("craft.recipe", "recipe_id", "product_name_fr", new[] { "recipe_id", "product_name_fr", "plan_name_fr", "building_level", "success_percent" },
            new[] { "success_percent", "building_level" }),
    };

    /// <summary>GET /api/content/kinds — types d'entités et compteurs (total / dans le périmètre).</summary>
    [HttpGet("kinds")]
    public async Task<IActionResult> Counts()
    {
        using var c = _db.Content();
        var res = new List<object>();
        foreach (var (name, k) in Kinds)
        {
            var hasScope = k.ListCols.Contains("in_scope");
            var total = await c.ExecuteScalarAsync<int>($"SELECT count(*) FROM {k.Table}");
            var inScope = hasScope ? await c.ExecuteScalarAsync<int>($"SELECT count(*) FROM {k.Table} WHERE in_scope") : total;
            res.Add(new { kind = name, total, inScope });
        }
        return Ok(res);
    }

    /// <summary>GET /api/content/{kind}?q=&amp;all=true&amp;page=&amp;size= — liste paginée (recherche par nom, périmètre par défaut).</summary>
    [HttpGet("{kind}")]
    public async Task<IActionResult> List(string kind, [FromQuery] string? q, [FromQuery] bool all = false,
        [FromQuery] int page = 1, [FromQuery] int size = 50, [FromQuery] string? lang = null)
    {
        if (!Kinds.TryGetValue(kind, out var k)) return NotFound(new { error = $"type '{kind}' inconnu" });
        size = Math.Clamp(size, 1, 200); page = Math.Max(1, page);
        var where = new List<string>();
        if (!all && k.ListCols.Contains("in_scope")) where.Add("in_scope");
        if (!string.IsNullOrWhiteSpace(q)) where.Add($"({k.Name} ILIKE @q OR {k.Key}::text = @exact)");
        var w = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        using var c = _db.Content();
        var p = new { q = $"%{q}%", exact = q ?? "", size, off = (page - 1) * size };
        var total = await c.ExecuteScalarAsync<int>($"SELECT count(*) FROM {k.Table} {w}", p);
        // Anglais : seulement si demandé ; le FR reste la valeur de référence, `name_en` est null (repli FR) tant que la traduction manque.
        var en = lang == "en" && k.Name == "name_fr"
            ? $", (SELECT value FROM text.translation t WHERE t.entity_type = '{kind.TrimEnd('s')}' AND t.entity_id = {k.Key} AND t.field = 'name' AND t.lang = 'en') AS name_en" : "";
        var rows = await c.QueryAsync($"SELECT {string.Join(",", k.ListCols)}{en} FROM {k.Table} {w} ORDER BY {k.Key} LIMIT @size OFFSET @off", p);
        return Ok(new { total, page, size, lang = lang == "en" && en.Length > 0 ? "en" : "fr", items = rows });
    }

    /// <summary>GET /api/content/{kind}/{id} — fiche complète ; pour un objet : où l'obtenir, recettes, modifications récentes.</summary>
    [HttpGet("{kind}/{id:int}")]
    public async Task<IActionResult> Detail(string kind, int id)
    {
        if (!Kinds.TryGetValue(kind, out var k)) return NotFound(new { error = $"type '{kind}' inconnu" });
        using var c = _db.Content();
        var row = await c.QuerySingleOrDefaultAsync($"SELECT * FROM {k.Table} WHERE {k.Key} = @id", new { id });
        if (row == null) return NotFound(new { error = $"{kind} #{id} introuvable" });
        object? obtention = null, materials = null, drops = null, shop = null, rewards = null;
        if (kind == "items")
            obtention = (await c.QueryAsync("SELECT source_kind, source_id, source_name_fr, detail FROM item.v_item_obtention WHERE item_id = @id ORDER BY source_kind, source_id LIMIT 200", new { id })).ToList();
        if (kind == "recipes")
            materials = (await c.QueryAsync("SELECT position, item_id, item_name_fr, quantity FROM craft.recipe_material WHERE recipe_id = @id ORDER BY position", new { id })).ToList();
        if (kind == "monsters")
            drops = (await c.QueryAsync("SELECT pool, item_id, item_name_fr, quantity, chance_percent FROM creature.monster_drop WHERE monster_id = @id ORDER BY pool, position", new { id })).ToList();
        if (kind == "quests")
            rewards = (await c.QueryAsync("SELECT item_id, item_name_fr, quantity, is_choice FROM quest.quest_reward WHERE quest_id = @id ORDER BY position", new { id })).ToList();
        if (kind == "npcs")
            shop = (await c.QueryAsync("SELECT s.item_id, s.item_name_fr, s.stock_count FROM npc.shop_item s JOIN npc.npc n ON n.store_id = s.store_id WHERE n.npc_id = @id ORDER BY s.position", new { id })).ToList();
        var history = (await c.QueryAsync("SELECT change_id, field, old_value, new_value, edited_by_account, edited_at, reverted_by FROM history.change WHERE entity_type = @t AND entity_id = @id ORDER BY change_id DESC LIMIT 20",
            new { t = kind.TrimEnd('s'), id })).ToList();
        return Ok(new { row, editable = k.Editable, obtention, materials, drops, shop, rewards, history });
    }

    public record EditRequest(string Field, string Value, string? Reason);

    /// <summary>PUT /api/content/{kind}/{id} — modifie un champ autorisé et l'inscrit dans l'historique (Admin).</summary>
    [HttpPut("{kind}/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(string kind, int id, [FromBody] EditRequest req)
    {
        if (!Kinds.TryGetValue(kind, out var k)) return NotFound(new { error = $"type '{kind}' inconnu" });
        if (!k.Editable.Contains(req.Field)) return BadRequest(new { error = $"champ '{req.Field}' non modifiable" });
        var who = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        using var c = _db.Content(); c.Open();
        using var tx = c.BeginTransaction();
        var (changeId, err) = await Apply(c, tx, k, kind, id, req.Field, req.Value, who, req.Reason);
        if (err != null) return err.StartsWith("404") ? NotFound(new { error = err }) : BadRequest(new { error = err });
        tx.Commit();
        return Ok(new { changeId });
    }

    /// <summary>GET /api/content/history?kind=&amp;id= — journal des modifications (Admin).</summary>
    [HttpGet("history")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> History([FromQuery] string? kind, [FromQuery] int? id, [FromQuery] int limit = 100)
    {
        using var c = _db.Content();
        var rows = await c.QueryAsync(
            "SELECT change_id, entity_type, entity_id, entity_name, field, old_value, new_value, edited_by_account, edited_at, reason, reverted_by FROM history.change " +
            "WHERE (@t IS NULL OR entity_type = @t) AND (@id IS NULL OR entity_id = @id) ORDER BY change_id DESC LIMIT @limit",
            new { t = kind?.TrimEnd('s'), id, limit = Math.Clamp(limit, 1, 500) });
        return Ok(rows);
    }

    /// <summary>POST /api/content/history/{changeId}/revert — annule une modification (écrit l'inverse, tracé) (Admin).</summary>
    [HttpPost("history/{changeId:long}/revert")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Revert(long changeId)
    {
        var who = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        using var c = _db.Content(); c.Open();
        using var tx = c.BeginTransaction();
        var ch = await c.QuerySingleOrDefaultAsync<(string EntityType, int EntityId, string Field, string? Old, long? RevertedBy)?>(
            "SELECT entity_type AS EntityType, entity_id AS EntityId, field AS Field, old_value AS Old, reverted_by AS RevertedBy FROM history.change WHERE change_id = @changeId FOR UPDATE", new { changeId }, tx);
        if (ch == null) return NotFound(new { error = $"modification {changeId} inconnue" });
        if (ch.Value.RevertedBy != null) return Conflict(new { error = "déjà annulée" });
        var kind = Kinds.FirstOrDefault(kv => kv.Key.TrimEnd('s') == ch.Value.EntityType);
        if (kind.Value == null) return BadRequest(new { error = "type non géré" });
        var (inverse, err) = await Apply(c, tx, kind.Value, kind.Key, ch.Value.EntityId, ch.Value.Field, ch.Value.Old ?? "", who, $"annulation de #{changeId}");
        if (err != null) return BadRequest(new { error = err });
        await c.ExecuteAsync("UPDATE history.change SET reverted_by = @inverse WHERE change_id = @changeId", new { inverse, changeId }, tx);
        tx.Commit();
        return Ok(new { changeId = inverse });
    }


    public record DraftRequest(string Kind, int Id, string Field, string Value, string? Reason);

    /// <summary>POST /api/content/drafts — prépare une modification sans l'appliquer (Admin).</summary>
    [HttpPost("drafts")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddDraft([FromBody] DraftRequest r)
    {
        if (!Kinds.TryGetValue(r.Kind, out var k)) return NotFound(new { error = $"type '{r.Kind}' inconnu" });
        if (!k.Editable.Contains(r.Field)) return BadRequest(new { error = $"champ '{r.Field}' non modifiable" });
        if (!r.Field.EndsWith("_fr") && !int.TryParse(r.Value, out _)) return BadRequest(new { error = $"'{r.Field}' attend un nombre entier" });
        using var c = _db.Content();
        if (await c.ExecuteScalarAsync<int>($"SELECT count(*) FROM {k.Table} WHERE {k.Key} = @id", new { id = r.Id }) == 0)
            return NotFound(new { error = $"{r.Kind} #{r.Id} introuvable" });
        var id = await c.ExecuteScalarAsync<long>(
            "INSERT INTO history.draft(entity_type, entity_id, field, new_value, created_by, reason) VALUES (@t, @id, @field, @value, @who, @reason) RETURNING draft_id",
            new { t = r.Kind.TrimEnd('s'), id = r.Id, field = r.Field, value = r.Value, who = Who(), reason = r.Reason });
        return Ok(new { draftId = id });
    }

    /// <summary>GET /api/content/drafts — brouillons en attente avec le DIFF (valeur actuelle → valeur proposée) (Admin).</summary>
    [HttpGet("drafts")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Drafts()
    {
        using var c = _db.Content();
        var drafts = (await c.QueryAsync<(long DraftId, string EntityType, int EntityId, string Field, string NewValue, string CreatedBy, string? Reason)>(
            "SELECT draft_id AS DraftId, entity_type AS EntityType, entity_id AS EntityId, field AS Field, new_value AS NewValue, created_by AS CreatedBy, reason AS Reason FROM history.draft WHERE release_id IS NULL ORDER BY draft_id")).ToList();
        var res = new List<object>();
        foreach (var d in drafts)
        {
            var kv = Kinds.FirstOrDefault(x => x.Key.TrimEnd('s') == d.EntityType);
            var cur = kv.Value == null ? null : await c.ExecuteScalarAsync<string?>($"SELECT {d.Field}::text FROM {kv.Value.Table} WHERE {kv.Value.Key} = @id", new { id = d.EntityId });
            res.Add(new { d.DraftId, d.EntityType, d.EntityId, d.Field, current = cur, proposed = d.NewValue, d.CreatedBy, d.Reason, changed = cur != d.NewValue });
        }
        return Ok(res);
    }

    /// <summary>DELETE /api/content/drafts/{id} — retire un brouillon en attente (Admin).</summary>
    [HttpDelete("drafts/{draftId:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteDraft(long draftId)
    {
        using var c = _db.Content();
        var n = await c.ExecuteAsync("DELETE FROM history.draft WHERE draft_id = @draftId AND release_id IS NULL", new { draftId });
        return n == 0 ? NotFound(new { error = "brouillon introuvable ou déjà publié" }) : Ok();
    }

    public record ReleaseRequest(string? Note);

    /// <summary>POST /api/content/releases — publie TOUS les brouillons en attente en une release (une transaction) (Admin).</summary>
    [HttpPost("releases")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Publish([FromBody] ReleaseRequest r)
    {
        using var c = _db.Content(); c.Open();
        using var tx = c.BeginTransaction();
        var drafts = (await c.QueryAsync<(long DraftId, string EntityType, int EntityId, string Field, string NewValue, string? Reason)>(
            "SELECT draft_id AS DraftId, entity_type AS EntityType, entity_id AS EntityId, field AS Field, new_value AS NewValue, reason AS Reason FROM history.draft WHERE release_id IS NULL ORDER BY draft_id FOR UPDATE", transaction: tx)).ToList();
        if (drafts.Count == 0) return BadRequest(new { error = "aucun brouillon à publier" });
        var release = $"R-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        foreach (var d in drafts)
        {
            var kv = Kinds.First(x => x.Key.TrimEnd('s') == d.EntityType);
            var (_, err) = await Apply(c, tx, kv.Value, kv.Key, d.EntityId, d.Field, d.NewValue, Who(), d.Reason ?? r.Note, release);
            if (err != null) return BadRequest(new { error = $"brouillon #{d.DraftId} : {err}" });
        }
        await c.ExecuteAsync("UPDATE history.draft SET release_id = @release WHERE release_id IS NULL", new { release }, tx);
        tx.Commit();
        return Ok(new { releaseId = release, changes = drafts.Count });
    }

    /// <summary>GET /api/content/releases — releases publiées (Admin).</summary>
    [HttpGet("releases")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Releases()
    {
        using var c = _db.Content();
        return Ok(await c.QueryAsync("SELECT release_id, count(*) AS changes, min(edited_at) AS published_at, bool_and(reverted_by IS NOT NULL) AS reverted FROM history.change WHERE release_id IS NOT NULL GROUP BY release_id ORDER BY release_id DESC LIMIT 50"));
    }

    /// <summary>POST /api/content/releases/{id}/revert — annule toute une release (modifications inverses tracées, ordre inverse) (Admin).</summary>
    [HttpPost("releases/{releaseId}/revert")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RevertRelease(string releaseId)
    {
        using var c = _db.Content(); c.Open();
        using var tx = c.BeginTransaction();
        var ids = (await c.QueryAsync<long>("SELECT change_id FROM history.change WHERE release_id = @releaseId AND reverted_by IS NULL ORDER BY change_id DESC FOR UPDATE", new { releaseId }, tx)).ToList();
        if (ids.Count == 0) return NotFound(new { error = "release inconnue ou déjà annulée" });
        foreach (var id in ids)
        {
            var ch = await c.QuerySingleAsync<(string EntityType, int EntityId, string Field, string? Old)>(
                "SELECT entity_type AS EntityType, entity_id AS EntityId, field AS Field, old_value AS Old FROM history.change WHERE change_id = @id", new { id }, tx);
            var kv = Kinds.First(x => x.Key.TrimEnd('s') == ch.EntityType);
            var (inverse, err) = await Apply(c, tx, kv.Value, kv.Key, ch.EntityId, ch.Field, ch.Old ?? "", Who(), $"annulation de la release {releaseId}");
            if (err != null) return BadRequest(new { error = err });
            await c.ExecuteAsync("UPDATE history.change SET reverted_by = @inverse WHERE change_id = @id", new { inverse, id }, tx);
        }
        tx.Commit();
        return Ok(new { reverted = ids.Count });
    }

    private string Who() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";

    private static async Task<(long Id, string? Error)> Apply(IDbConnection c, IDbTransaction tx, Kind k, string kind, int id,
        string field, string value, string who, string? reason, string? releaseId = null)
    {
        var old = await c.QuerySingleOrDefaultAsync<(string Name, string Old)?>(
            $"SELECT {k.Name}::text AS Name, {field}::text AS Old FROM {k.Table} WHERE {k.Key} = @id FOR UPDATE", new { id }, tx);
        if (old == null) return (0, $"404 {kind} #{id} introuvable");
        var isText = field.EndsWith("_fr");
        if (!isText && !int.TryParse(value, out _)) return (0, $"'{field}' attend un nombre entier");
        await c.ExecuteAsync($"UPDATE {k.Table} SET {field} = (@v)::{(isText ? "text" : "integer")} WHERE {k.Key} = @id", new { v = value, id }, tx);
        var changeId = await c.ExecuteScalarAsync<long>(
            "INSERT INTO history.change(entity_type, entity_id, entity_name, field, old_value, new_value, edited_by_account, reason, release_id) " +
            "VALUES (@t, @id, @name, @field, @old, @value, @who, @reason, @releaseId) RETURNING change_id",
            new { t = kind.TrimEnd('s'), id, name = old.Value.Name, field, old = old.Value.Old, value, who, reason, releaseId }, tx);
        return (changeId, null);
    }
}
