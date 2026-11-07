using Dapper;
using Limitless.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Limitless.Api.Controllers;

/// <summary>
/// Villes de guilde en direct (PLAN-ACTION-3 D4), LECTURE seule de <c>eden_live</c> : uniquement ce qu'un visiteur voit en ville
/// (nom, niveau, bâtiments posés). Aucune donnée privée (trésorerie, membres nominatifs, privilèges).
/// </summary>
[ApiController]
[Route("api/live/guilds")]
public sealed class LiveGuildsController : ControllerBase
{
    private readonly Db _db;
    public LiveGuildsController(Db db) => _db = db;

    /// <summary>GET /api/live/guilds?open=true — familles avec niveau, carte de la ville (0 = pas de ville), nombre de bâtiments et de membres.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool open = false, [FromQuery] int limit = 100)
    {
        using var c = _db.Live();
        var rows = await c.QueryAsync(
            "SELECT f.id AS id, f.name AS name, f.lv AS level, f.house_open AS town_node, " +
            "(SELECT count(*) FROM guild.family_building_container b WHERE b.family_id = f.id) AS buildings, " +
            "(SELECT count(*) FROM character.player_characters p WHERE p.family_id = f.id) AS members " +
            "FROM guild.family f WHERE f.id > 0 AND (@open = false OR f.house_open > 0) ORDER BY f.lv DESC, f.id LIMIT @limit",
            new { open, limit = Math.Clamp(limit, 1, 500) });
        return Ok(rows);
    }

    /// <summary>GET /api/live/guilds/{id} — fiche publique : niveau, bâtiments posés par type de conteneur.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        using var c = _db.Live();
        var f = await c.QuerySingleOrDefaultAsync("SELECT id, name, lv AS level, house_open AS town_node FROM guild.family WHERE id = @id", new { id });
        if (f == null) return NotFound(new { error = $"famille {id} introuvable" });
        var b = await c.QueryAsync("SELECT item_type, tid, level, loc FROM guild.family_building_container WHERE family_id = @id ORDER BY item_type, loc", new { id });
        return Ok(new { family = f, buildings = b });
    }
}
