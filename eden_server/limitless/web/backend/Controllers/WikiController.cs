using Limitless.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Limitless.Api.Controllers;

/// <summary>Wiki — datasets exportés par tools/export_web_data.py
/// (JSON versionnés, numérique serveur + libellés EN). Endpoints
/// paginés : liste par dataset, détail par id, métadonnées.</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class WikiController : ControllerBase
{
    private readonly GameDataStore _data;
    public WikiController(GameDataStore data) => _data = data;

    /// <summary>GET /api/wiki/datasets — liste des datasets + compteurs
    /// (le rail gauche du wiki l'utilise).</summary>
    [HttpGet("datasets")]
    public IActionResult Datasets() => Ok(_data.Datasets());

    /// <summary>GET /api/wiki/{kind}?q=&f=…&f=…&page=&size= — page
    /// de résultats {total,page,size,items}. `f` est répétable (ET
    /// logique) ; chaque filtre = "champ[:op]:valeur" —
    /// scalaire (« color:yellow »), comparaison (« level&gt;50 »,
    /// « level&lt;=30 ») ou chemin dans un tableau
    /// (« maps.name », « drops.id », « bpDrops.classGroup »,
    /// « usedInCrafts.* », « materials.id »). Exemples :
    /// classes?f=color:yellow&f=awakened:true&f=charLevel>=65
    /// monsters?f=type:3&f=level>60&f=maps.name:S001&f=drops.id:32631
    /// certificates?f=classId:17&f=stat:P-ATK
    /// items?f=category:Mount  /  crafts?f=materials.id:44025</summary>
    [HttpGet("{kind}")]
    public IActionResult List(string kind,
        [FromQuery] string? q,
        [FromQuery(Name = "f")] string[]? f,
        [FromQuery] int page = 1,
        [FromQuery] int size = 60)
    {
        var ds = _data.Get(kind);
        if (ds == null)
            return NotFound(new { error = $"dataset '{kind}' inconnu" });
        return Ok(_data.Query(kind, q, f, page, size));
    }

    /// <summary>GET /api/wiki/{kind}/{id} — détail d'une entrée
    /// (id, ou classId pour le dataset talents).</summary>
    [HttpGet("{kind}/{id:int}")]
    public IActionResult Detail(string kind, int id)
    {
        var row = _data.Detail(kind, id);
        if (row == null)
            return NotFound(new { error = $"{kind} #{id} introuvable" });
        return Ok(row.Value);
    }
}
