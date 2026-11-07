using Limitless.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Limitless.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RankingsController : ControllerBase
{
    private readonly RankingsService _r;
    public RankingsController(RankingsService r) => _r = r;

    /// <summary>Top exp Personnage (level + exp — fndb1.player_characters).</summary>
    [HttpGet("level")]
    public Task<IReadOnlyList<RankingsService.RankRow>> Level() => _r.TopLevelAsync();

    /// <summary>Top exp de Classe (class_level + class_exp —
    /// fndb1.player_classes). Les 3 EXP sont distinctes : Personnage,
    /// Classe, Ultime (Limitless).</summary>
    [HttpGet("class")]
    public Task<IReadOnlyList<RankingsService.RankRow>> ClassExp()
        => _r.TopClassExpAsync();

    [HttpGet("achievements")]
    public Task<IReadOnlyList<RankingsService.RankRow>> Achievements()
        => _r.TopAchievementsAsync();

    [HttpGet("pvp")]
    public Task<IReadOnlyList<RankingsService.RankRow>> Pvp()
        => _r.TopPvpAsync();

    [HttpGet("limitless")]
    public Task<IReadOnlyList<RankingsService.RankRow>> Limitless() => _r.TopLimitlessAsync();
}
