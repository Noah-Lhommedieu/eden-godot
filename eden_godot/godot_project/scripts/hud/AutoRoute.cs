using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>
/// Déplacement automatique du joueur (point &amp; click programmatique
/// via <see cref="PlayerController.SetMoveTarget"/>) — utilisé par la
/// Region Map (double-clic d'une entrée « Search Listing ») et par les
/// objectifs cliquables du <see cref="QuestTracker"/>
/// (hud-interfaces.md : auto-follow vers npc / mob / coordonnée).
/// Les cibles viennent de la scène de la map courante
/// (<see cref="MinimapPanel.Scene"/>).
/// </summary>
public static class AutoRoute
{
    private static PlayerController? Player(Node from) =>
        from.GetTree().Root.FindChild("Player", true, false)
            as PlayerController;

    // Obstacles dynamiques : cases (cy*Cols+cx) où le perso s'est
    // coincé pendant la route courante (collision physique absente de
    // la grille .smp). Vidée à chaque nouvelle destination.
    private static readonly HashSet<int> _blacklist = new();
    private static (float X, float Y)? _dest;
    private static int _retries;
    private const int MaxRetries = 3;

    /// <summary>Cases en liste noire (tests/diagnostic).</summary>
    public static int BlacklistCount => _blacklist.Count;

    /// <summary>Marche vers le point sol Eden (x, y).</summary>
    public static bool ToEden(Node from, float ex, float ey)
        => ToEden(from, ex, ey, false);

    private static bool ToEden(Node from, float ex, float ey, bool replan)
    {
        var p = Player(from);
        if (p == null) return false;
        if (!replan)
        {
            _blacklist.Clear();
            _retries = 0;
        }
        _dest = (ex, ey);
        p.Stuck = (pos, dir) => OnStuck(p, pos, dir);
        var g = WorldConv.ToGodot(ex, ey, 0f);
        var dst = new Vector3(g.X, p.GlobalPosition.Y, g.Z);
        var path = p.Planner?.Invoke(p.GlobalPosition, dst);
        if (path is { Count: > 0 })
        {
            p.SetMovePath(path);
            GD.Print($"[route] → ({ex:0},{ey:0}) A* {path.Count} wp" +
                     (replan ? $" (replan {_retries}/{MaxRetries}, " +
                               $"{_blacklist.Count} cases noires)" : ""));
        }
        else if (p.Planner != null)
        {
            // Grille présente mais aucun chemin : destination
            // inatteignable à pied (mur, falaise, autre étage). Avant :
            // ligne droite → le perso se jetait contre le décor.
            GD.Print($"[route] ({ex:0},{ey:0}) inatteignable à pied");
            _dest = null;
            if (replan) p.ClearMoveTarget();
            HudExtensions.FindHud()?.Chat?.AddSystem(UiTextEn.Translate(
                "That destination cannot be reached on foot."));
            return false;
        }
        else
        {
            p.SetMoveTarget(dst);   // pas de grille : marche directe
            GD.Print($"[route] → ({ex:0},{ey:0}) direct");
        }
        return true;
    }

    /// <summary>Un chemin à pied existe-t-il vers (ex, ey) ? Sans grille
    /// de planification : vrai (marche directe).</summary>
    public static bool CanReach(Node from, float ex, float ey)
    {
        var p = Player(from);
        if (p?.Planner == null) return p != null;
        var g = WorldConv.ToGodot(ex, ey, 0f);
        var dst = new Vector3(g.X, p.GlobalPosition.Y, g.Z);
        var path = p.Planner(p.GlobalPosition, dst);
        return path is { Count: > 0 };
    }

    /// <summary>Le perso ne progresse plus sur sa route : marque les
    /// cases devant lui (rayon croissant) comme obstacles, puis replanifie
    /// vers la même destination ; abandon propre après
    /// <see cref="MaxRetries"/> essais.</summary>
    private static void OnStuck(PlayerController p, Vector3 pos,
        Vector3 dir)
    {
        if (_dest is not { } d || !GodotObject.IsInstanceValid(p))
            return;
        _retries++;
        if (_retries > MaxRetries)
        {
            GD.Print($"[route] abandon : bloqué {_retries - 1}× " +
                     $"({_blacklist.Count} cases noires)");
            Cancel(p);
            HudExtensions.FindHud()?.Chat?.AddSystem(UiTextEn.Translate(
                "The way is blocked. Auto-move cancelled."));
            return;
        }
        MarkAhead(p, pos, dir, _retries);
        ToEden(p, d.X, d.Y, true);
    }

    private static void MarkAhead(PlayerController p, Vector3 pos,
        Vector3 dir, int radius)
    {
        var map = p.NodeMapForRoute?.Invoke();
        if (map == null) return;
        var (ex, ey) = WorldConv.ToEdenGround(pos + dir * 0.9f);
        var cx = (int)MathF.Floor(ex * 2f);
        var cy = (int)MathF.Floor(ey * 2f);
        for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = cx + dx; var y = cy + dy;
                if (x >= 0 && y >= 0 && x < map.Cols && y < map.Rows)
                    _blacklist.Add(y * map.Cols + x);
            }
        GD.Print($"[route] case noire ({ex:0.0},{ey:0.0}) r={radius}");
    }

    /// <summary>Annule route auto + cible.</summary>
    public static void Cancel(PlayerController p)
    {
        _dest = null;
        p.ClearMoveTarget();
        p.SetMovePath(Array.Empty<Vector3>());
    }

    /// <summary>A* sur la grille .smp dilatée : waypoints Godot (sans le
    /// départ), null si pas de grille ou aucun chemin. Tient compte de
    /// la liste noire dynamique.</summary>
    public static List<Vector3>? Plan(NodeMapFile? map, Vector3 from,
        Vector3 to)
    {
        if (map == null) return null;
        var (sx, sy) = WorldConv.ToEdenGround(from);
        var (ex, ey) = WorldConv.ToEdenGround(to);
        var wps = Pathfinder.Find(map, sx, sy, ex, ey,
            _blacklist.Count > 0 ? _blacklist : null);
        if (wps == null) return null;
        var res = new List<Vector3>(wps.Count);
        foreach (var (x, y) in wps)
        {
            var g = WorldConv.ToGodot(x, y, 0f);
            res.Add(new Vector3(g.X, to.Y, g.Z));
        }
        return res;
    }

    /// <summary>Marche vers le PNJ <paramref name="npcId"/> s'il est
    /// placé sur la map courante.</summary>
    public static bool ToNpc(Node from, int npcId)
    {
        var scene = HudExtensions.FindHud()?.Minimap?.Scene;
        var n = scene?.Npcs.FirstOrDefault(x => x.NpcId == npcId);
        return n != null && ToEden(from, n.X, n.Y);
    }

    /// <summary>Objectif de quête : monstre dont le nom figure dans le
    /// libellé, sinon PNJ de rendu de la quête. Message chat si la
    /// cible n'est pas sur la map courante.</summary>
    public static bool ToMissionTarget(Node from, int mid, string label)
    {
        var hud = HudExtensions.FindHud();
        // 3.3.5 : objectif résolu (mob/PNJ sur n'importe quel node) →
        // route à pied, inter-monde compris (QuestRoute).
        if (QuestObjectiveMarks.Targets(mid, label).Count > 0)
        {
            QuestRoute.Go(from, mid, label);
            return true;
        }
        var scene = hud?.Minimap?.Scene;
        if (scene != null && label.Length > 0)
        {
            var mob = scene.Monsters.FirstOrDefault(m =>
                m.Name.Length > 2 &&
                label.Contains(m.Name, StringComparison.OrdinalIgnoreCase));
            if (mob != null) return ToEden(from, mob.X, mob.Y);
        }
        var npc = MissionTable.Get(mid)?.SubmitNpc ?? 0;
        if (npc != 0 && ToNpc(from, npc)) return true;
        hud?.Chat?.AddSystem(UiTextEn.Translate("The quest target is not on this map."));
        return false;
    }

    /// <summary>Nom affiché d'un PNJ de scène (« &lt;Titre&gt; Nom »).</summary>
    public static string NpcLabel(SceneTable.NpcSpawn n)
    {
        var name = TextResolver.NpcName(n.NpcId) ?? n.Name;
        var title = TextResolver.NpcTitle(n.NpcId);
        return title is { Length: > 0 } ? $"<{title}> {name}" : name;
    }
}
