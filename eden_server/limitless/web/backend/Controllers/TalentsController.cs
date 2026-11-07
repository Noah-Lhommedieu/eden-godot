using System.Text.Json;
using Limitless.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Limitless.Api.Controllers;

/// <summary>Arbres de talents — servis depuis talents.json (noms
/// d'effets EN via T_Enchant, classes EN via T_Class).</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class TalentsController : ControllerBase
{
    private readonly GameDataStore _data;
    public TalentsController(GameDataStore data) => _data = data;

    /// <summary>Règles natives du système de talents AA (constantes
    /// lapis — AddAAEffectPoint/GainAAPoint) : +1 point par niveau de
    /// classe, cap = niveau perso ≤ 100, coût par palier.</summary>
    [HttpGet("rules")]
    public IActionResult Rules() => Ok(new
    {
        pointsPerClassLevel = 1,
        maxCharacterLevel = 100,
        maxAaPoints = 120,
        // AARequestTable @0x888c140 : coût du palier N (index 0-20).
        costPerRank = new[] { 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2,
                              2, 2, 2, 3, 3, 3, 3, 3 },
        // AARequestTableTotal : dépense cumulée pour atteindre N pts.
        costTableTotal = new[] { 0, 1, 2, 3, 4, 5, 7, 9, 11, 13, 15,
                                 17, 19, 21, 23, 25, 28, 31, 34, 37,
                                 40 },
    });

    /// <summary>Dataset talents selon la variante demandée :
    /// « classic » = talents.json (S_Talent serveur) ; « reforged » =
    /// talents_reforged.json (C_Talent du client Reforged — les
    /// arbres réellement joués). Repli classic si la variante est
    /// absente.</summary>
    private string DatasetFor(string? variant) =>
        string.Equals(variant, "reforged", StringComparison.OrdinalIgnoreCase)
            ? "talents_reforged" : "talents";

    /// <summary>Liste des classes (nom EN + nb d'arbres) pour le
    /// sélecteur du simulateur — 61 entrées (20 classes de base +
    /// 40 éveils + Inquisitor). ?variant=classic|reforged.</summary>
    [HttpGet]
    public IActionResult All([FromQuery] string? variant)
    {
        var ds = _data.Get(DatasetFor(variant)) ?? _data.Get("talents");
        if (ds == null) return Ok(Array.Empty<object>());
        var list = ds.Rows.Select(r => new
        {
            classId = r.GetProperty("classId").GetInt32(),
            className = r.TryGetProperty("className", out var n) &&
                        n.ValueKind == JsonValueKind.String
                ? n.GetString() : null,
            trees = r.TryGetProperty("trees", out var t) &&
                    t.ValueKind == JsonValueKind.Array
                ? t.GetArrayLength() : 0,
        });
        return Ok(list);
    }

    /// <summary>Arbres complets d'une classe — {classId,className,
    /// trees:[{tree:1..3,slots:[{pos,effectId,name,desc,maxPts,
    /// preEffect,prePoints,tier,reqPoints,icon}]}]}.
    /// ?variant=classic|reforged.</summary>
    [HttpGet("{classId}")]
    public IActionResult Tree(int classId, [FromQuery] string? variant)
    {
        var t = _data.Detail(DatasetFor(variant), classId)
                ?? _data.Detail("talents", classId);
        return t == null ? NotFound() : Ok(t.Value);
    }
}
