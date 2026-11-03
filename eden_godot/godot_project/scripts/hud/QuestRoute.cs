using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;

namespace EdenClient.Hud;

/// <summary>
/// 3.3.5 — clic gauche sur un objectif de quête atteignable à pied :
/// auto-follow, y compris d'un monde à l'autre. Les nodes sont reliés
/// par les aires GATEWAY des scènes (<c>[gateway_area]</c> → script
/// <c>[event]</c> → <c>Event_Transport &lt;node&gt;</c>, même source que
/// le serveur <c>HandleNodeExitAsync</c>). Le trajet = BFS sur le
/// graphe des nodes « marchables » (ville/champ/bourg — pas de donjon
/// intermédiaire) ; chaque saut marche jusqu'au centre de l'aire de
/// portail (la détection <see cref="MissionBridge"/> déclenche le
/// CheckNodeExit), puis à l'arrivée (<see cref="PortalArrival"/>) on
/// enchaîne sur le saut suivant ou la cible finale.
/// </summary>
public static class QuestRoute
{
    private sealed record Edge(int From, int To, float X, float Y);

    private static Dictionary<int, List<Edge>>? _graph;
    private static readonly object _lock = new();
    private static HashSet<int>? _walkable;

    // Route active : sauts restants + cible finale.
    private static readonly Queue<Edge> _hops = new();
    private static (int Node, float X, float Y)? _final;

    /// <summary>Route en cours (pour les diagnostics).</summary>
    public static bool Active => _final != null;

    /// <summary>Graphe des portails de TOUTES les scènes (parse
    /// paresseux, thread-safe — appelé hors thread principal).</summary>
    private static Dictionary<int, List<Edge>> Graph()
    {
        lock (_lock)
        {
            if (_graph != null) return _graph;
            var g = new Dictionary<int, List<Edge>>();
            foreach (var (node, sc) in SceneIndex.All())
            {
                foreach (var gw in sc.Gateways)
                {
                    var area = sc.EventAreas
                        .FirstOrDefault(a => a.Id == gw.EventAreaId);
                    if (area == null) continue;
                    // Même résolution que le serveur : col2 (script) sauf
                    // -1/0 → col4 (keyId) = id d'événement.
                    var ev = gw.ScriptEventId is -1 or 0
                        ? gw.KeyId : gw.ScriptEventId;
                    if (!sc.Events.TryGetValue(ev, out var e) ||
                        e.TransportNode <= 0)
                        continue;
                    if (!g.TryGetValue(node, out var l))
                        g[node] = l = new List<Edge>();
                    l.Add(new Edge(node, e.TransportNode, area.X, area.Y));
                }
            }
            return _graph = g;
        }
    }

    /// <summary>Nodes marchables (c_node.ini col3 : 1 champ, 3 ville,
    /// 4 bourg) — les donjons ne servent pas de relais.</summary>
    private static HashSet<int> Walkable()
    {
        if (_walkable != null) return _walkable;
        var set = new HashSet<int>();
        var path = AssetCatalog.ResolveRawAsset("data/db/c_node.ini");
        if (path != null)
        {
            foreach (var line in Vfs.EdenVfs.ReadLines(path,
                         System.Text.Encoding.Latin1).Skip(1))
            {
                var p = line.TrimEnd('\r').Split('|');
                if (p.Length > 4 && int.TryParse(p[0], out var id) &&
                    p[3] is "1" or "3" or "4")
                    set.Add(id);
            }
        }
        return _walkable = set;
    }

    /// <summary>Plus court chemin de portails de <paramref name="from"/>
    /// à <paramref name="to"/> (null si injoignable à pied ; liste vide
    /// si même node). Le dernier saut peut entrer dans un node non
    /// marchable (donjon visé).</summary>
    private static List<Edge>? Path(int from, int to)
    {
        if (from == to) return new List<Edge>();
        var g = Graph();
        var walk = Walkable();
        var prev = new Dictionary<int, Edge>();
        var q = new Queue<int>();
        q.Enqueue(from);
        var seen = new HashSet<int> { from };
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            if (!g.TryGetValue(n, out var edges)) continue;
            foreach (var e in edges)
            {
                if (!seen.Add(e.To)) continue;
                prev[e.To] = e;
                if (e.To == to)
                {
                    var path = new List<Edge>();
                    for (var cur = to; cur != from; cur = prev[cur].From)
                        path.Add(prev[cur]);
                    path.Reverse();
                    return path;
                }
                // Relais : seulement des nodes marchables.
                if (walk.Contains(e.To)) q.Enqueue(e.To);
            }
        }
        return null;
    }

    /// <summary>Lance la route vers l'objectif (mid, libellé). Résolution
    /// (scan de toutes les scènes + BFS) hors thread principal, puis
    /// départ en différé sur le thread principal.</summary>
    public static void Go(Node from, int mid, string label)
    {
        var hud = HudExtensions.FindHud();
        var curNode = hud?.Minimap?.NodeId ?? 0;
        var cur = hud?.Minimap?.SelfPos ?? Vector2.Zero;
        var targets = QuestObjectiveMarks.Targets(mid, label);
        if (targets.Count == 0 || curNode == 0)
        {
            Fail(from, mid, label);
            return;
        }
        var tree = from;
        GD.Print($"[quest-route] objectif mid={mid} « {label} » node={curNode} " +
                 $"cibles={string.Join(",", targets.Select(t => (t.Mob ? "mob" : "npc") + t.Id))}");
        _ = Task.Run(() =>
        {
            try
            {
                var marks = QuestObjectiveMarks.Scan(targets);
                GD.Print($"[quest-route] {marks.Count} repère(s) : " +
                    string.Join(" ", marks.Take(8).Select(m =>
                        $"S{m.NodeId:D3}({m.X:0},{m.Y:0})")));
                // Candidats triés : même node d'abord (le plus proche),
                // sinon le moins de sauts. Le test d'atteignabilité à
                // pied (A*) se fait sur le thread principal.
                var cands = new List<(QuestObjectiveMarks.Mark M,
                    List<Edge> P, float Score)>();
                foreach (var m in marks)
                {
                    var path = Path(curNode, m.NodeId);
                    if (path == null) continue;
                    var d = new Vector2(m.X, m.Y).DistanceTo(cur);
                    cands.Add((m, path, path.Count * 100000f +
                        (path.Count == 0 ? d : 0f)));
                }
                cands.Sort((x, y) => x.Score.CompareTo(y.Score));
                Callable.From(() =>
                {
                    if (!GodotObject.IsInstanceValid(tree))
                        return;
                    foreach (var c in cands.Take(15))
                    {
                        // Premier segment à pied : portail (si sauts)
                        // ou cible finale. Inatteignable -> suivant.
                        var (tx, ty) = c.P.Count > 0
                            ? (c.P[0].X, c.P[0].Y) : (c.M.X, c.M.Y);
                        if (!AutoRoute.CanReach(tree, tx, ty))
                        {
                            GD.Print($"[quest-route] repère S{c.M.NodeId:D3}" +
                                $"({c.M.X:0},{c.M.Y:0}) inatteignable " +
                                $"à pied depuis ici -> suivant");
                            continue;
                        }
                        Begin(tree, c.M, c.P);
                        return;
                    }
                    Fail(from, mid, label);
                }).CallDeferred();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[quest-route] {e.Message}");
            }
        });
    }

    private static void Begin(Node from, QuestObjectiveMarks.Mark target,
        List<Edge> path)
    {
        _hops.Clear();
        foreach (var e in path) _hops.Enqueue(e);
        _final = (target.NodeId, target.X, target.Y);
        GD.Print($"[quest-route] → node {target.NodeId} " +
                 $"({target.X:0},{target.Y:0}) via {path.Count} portail(s) " +
                 string.Join(">", path.Select(p => $"{p.From}->{p.To}")));
        Step(from);
    }

    /// <summary>Étape courante : marche vers le prochain portail ou la
    /// cible finale du node courant.</summary>
    private static void Step(Node from)
    {
        if (_final is not { } fin) return;
        if (_hops.Count > 0)
        {
            var e = _hops.Peek();
            AutoRoute.ToEden(from, e.X, e.Y);
        }
        else
        {
            AutoRoute.ToEden(from, fin.X, fin.Y);
            _final = null;
        }
    }

    /// <summary>Appelé à chaque arrivée sur un node
    /// (<see cref="PortalArrival.Arrived"/>) : poursuit la route si
    /// le saut a abouti, l'abandonne si on est ailleurs que prévu.</summary>
    public static void OnArrived(Node from, int nodeId)
    {
        if (_final == null) return;
        if (_hops.Count == 0)
        {
            Step(from);
            return;
        }
        var e = _hops.Peek();
        if (nodeId == e.To)
        {
            _hops.Dequeue();
            Step(from);
        }
        else if (nodeId != e.From)
        {
            _hops.Clear();
            _final = null;   // téléporté ailleurs : route périmée
        }
    }

    /// <summary>Annule la route (clic sol, nouvelle commande…).</summary>
    public static void Cancel()
    {
        _hops.Clear();
        _final = null;
    }

    private static void Fail(Node from, int mid, string label)
    {
        // Repli historique : cible sur la map courante (scène) ou PNJ
        // de rendu ; sinon message.
        HudExtensions.FindHud()?.Chat?.AddSystem(UiTextEn.Translate(
            "The quest target cannot be reached on foot."));
        _ = mid; _ = label;
    }
}
