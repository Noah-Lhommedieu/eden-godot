using Dapper;
using Limitless.Api.Data;
using Limitless.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace Limitless.Api.Controllers;

/// <summary>Progression des persos du compte — succès, quêtes,
/// talents par personnage (dashboard « mes avancées »). Les noms EN
/// sont résolus via les datasets GameDataStore (succès/missions/
/// talents — jamais d'id nu quand un libellé existe).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ProgressController : ControllerBase
{
    private readonly Db _db;
    private readonly GameDataStore _data;
    public ProgressController(Db db, GameDataStore data)
    { _db = db; _data = data; }

    private int AccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Libellé EN d'une entrée de dataset (null si absent
    /// ou non traduit).</summary>
    private string? NameOf(string kind, int id)
    {
        var row = _data.Detail(kind, id);
        if (row is { } el &&
            el.TryGetProperty("name", out var n) &&
            n.ValueKind == JsonValueKind.String)
            return n.GetString();
        return null;
    }

    /// <summary>Succès/achievements d'un perso — union des 3 tables
    /// natives player_characters_achievement{,1,2} (la zone écrit
    /// dans les trois selon la plage d'ids). Nom EN joint.</summary>
    [HttpGet("achievements/{charId}")]
    public async Task<IActionResult> Achievements(int charId)
    {
        try
        {
            using var db = _db.Game();
            var rows = await db.QueryAsync<(int AchievementId,
                int Point, int SuccessNum)>(
                "SELECT achievement_id AS AchievementId, " +
                "MAX(point) AS Point, MAX(success_num) AS SuccessNum " +
                "FROM (SELECT achievement_id, point, success_num " +
                "      FROM player_characters_achievement " +
                "      WHERE player_id=@c " +
                "      UNION ALL " +
                "      SELECT achievement_id, point, success_num " +
                "      FROM player_characters_achievement1 " +
                "      WHERE player_id=@c " +
                "      UNION ALL " +
                "      SELECT achievement_id, point, success_num " +
                "      FROM player_characters_achievement2 " +
                "      WHERE player_id=@c) u " +
                "GROUP BY achievement_id ORDER BY achievement_id",
                new { c = charId });
            return Ok(rows.Select(r => new
            {
                achievement_id = r.AchievementId,
                point = r.Point,
                success_num = r.SuccessNum,
                name = NameOf("achievements", r.AchievementId),
            }));
        }
        catch { return Ok(Array.Empty<object>()); }
    }

    /// <summary>Quêtes en cours/terminées d'un perso — nom EN de la
    /// mission joint (S_Mission ↔ t_mission).</summary>
    [HttpGet("quests/{charId}")]
    public async Task<IActionResult> Quests(int charId)
    {
        try
        {
            using var db = _db.Game();
            var rows = await db.QueryAsync<(int QuestId, int Status)>(
                "SELECT index AS QuestId, state AS Status " +
                "FROM personal_mission_states " +
                "WHERE id=@c ORDER BY index",
                new { c = charId });
            return Ok(rows.Select(r => new
            {
                quest_id = r.QuestId,
                status = r.Status,
                name = NameOf("missions", r.QuestId),
            }));
        }
        catch { return Ok(Array.Empty<object>()); }
    }

    /// <summary>« Talents » d'un perso — DEUX choses distinctes :
    /// 1. effects : points investis dans l'arbre AA de chaque classe
    ///    (table native advanced_ability {player_id,effect_id,point},
    ///    effect = enchant de talent — nom/desc résolus via
    ///    talents.json) ;
    /// 2. pools : aa_point disponibles par classe
    ///    (player_classlist, +1 par niveau de classe).
    /// NB : les « stats passives » gagnées tous les 5 niveaux de
    /// classe ne sont PAS des talents — ce sont les rankBuffs du
    /// dataset classes (S_ClassRankBuff).</summary>
    [HttpGet("talents/{charId}")]
    public async Task<IActionResult> Talents(int charId)
    {
        try
        {
            using var db = _db.Game();
            var effects = await db.QueryAsync<(int EffectId,
                int Point)>(
                "SELECT effect_id AS EffectId, point AS Point " +
                "FROM advanced_ability WHERE player_id=@c",
                new { c = charId });
            var pools = await db.QueryAsync<(int ClassId, int Level,
                int AaPoint)>(
                "SELECT class AS ClassId, level AS Level, " +
                "aa_point AS AaPoint FROM player_classlist " +
                "WHERE player_id=@c ORDER BY class",
                new { c = charId });
            var names = TalentEffectIndex();
            return Ok(new
            {
                effects = effects.Select(e => new
                {
                    effect_id = e.EffectId,
                    points = e.Point,
                    class_id = names.TryGetValue(e.EffectId,
                        out var info) ? info.ClassId : (int?)null,
                    class_name = info.ClassName,
                    name = info.Name,
                    desc = info.Desc,
                }),
                pools = pools.Select(p => new
                {
                    class_id = p.ClassId,
                    class_level = p.Level,
                    aa_point = p.AaPoint,
                    class_name = NameOf("classes", p.ClassId),
                }),
            });
        }
        catch { return Ok(Array.Empty<object>()); }
    }

    private Dictionary<int, (int ClassId, string? ClassName,
        string? Name, string? Desc)>? _talentFx;

    /// <summary>Index effectId → (classe, nom EN) construit une fois
    /// depuis talents.json (chaque slot de chaque arbre).</summary>
    private Dictionary<int, (int ClassId, string? ClassName,
        string? Name, string? Desc)> TalentEffectIndex()
    {
        if (_talentFx != null) return _talentFx;
        var map = new Dictionary<int, (int, string?, string?,
            string?)>();
        var ds = _data.Get("talents");
        if (ds != null)
            foreach (var cls in ds.Rows)
            {
                var cid = cls.GetProperty("classId").GetInt32();
                var cname = cls.TryGetProperty("className", out var cn)
                    && cn.ValueKind == JsonValueKind.String
                    ? cn.GetString() : null;
                if (!cls.TryGetProperty("trees", out var trees))
                    continue;
                foreach (var tree in trees.EnumerateArray())
                    foreach (var s in tree.GetProperty("slots")
                                 .EnumerateArray())
                    {
                        var eff = s.GetProperty("effectId").GetInt32();
                        map.TryAdd(eff, (cid, cname,
                            s.TryGetProperty("name", out var n) &&
                            n.ValueKind == JsonValueKind.String
                                ? n.GetString() : null,
                            s.TryGetProperty("desc", out var d) &&
                            d.ValueKind == JsonValueKind.String
                                ? d.GetString() : null));
                    }
            }
        _talentFx = map;
        return map;
    }
}
