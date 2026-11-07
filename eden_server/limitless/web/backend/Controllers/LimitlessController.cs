using Limitless.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Limitless.Api.Controllers;

/// <summary>Exp Ultime — état du perso + classement (chantier 3).</summary>
[ApiController]
[Route("api/limitless")]
public sealed class LimitlessController : ControllerBase
{
    private readonly LimitlessService _svc;
    public LimitlessController(LimitlessService svc) => _svc = svc;

    /// <summary>GET /api/limitless/{charId} — exp/level/perks.</summary>
    [HttpGet("{charId:int}")]
    public async Task<IActionResult> Get(int charId)
    {
        var st = await _svc.GetAsync(charId);
        return st == null
            ? Ok(new { charId, exp = 0L, level = 0, perks = Array.Empty<object>() })
            : Ok(st);
    }

    /// <summary>GET /api/limitless/top — classement Exp Ultime.</summary>
    [HttpGet("top")]
    public async Task<IActionResult> Top() => Ok(await _svc.TopAsync());

    /// <summary>GET /api/limitless/perks — catalogue public des perks
    /// Limitless (wiki : liste des bonus par palier).</summary>
    [HttpGet("perks")]
    public async Task<IActionResult> Perks() =>
        Ok(await _svc.AllPerksAsync());

    /// <summary>GET /api/limitless/levels — coût en exp par niveau
    /// (courbe ll_level_cost, wiki + échelle du dashboard).</summary>
    [HttpGet("levels")]
    public async Task<IActionResult> Levels() =>
        Ok(await _svc.LevelCostsAsync());
}
