using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Repères d'objectif de quête sur la minimap / la Region Map (plan
/// 3.2.2 + 3.3.5). Règle utilisateur : AUCUN mob en rouge en général ;
/// le rouge n'apparaît que pour les objectifs liés à des mobs (kill,
/// collect sur mob) après un CLIC DROIT sur la ligne d'objectif du
/// <see cref="QuestTracker"/>. Les objectifs PNJ sont repérés en jaune.
///
/// Les cibles sont résolues sur TOUTES les scènes
/// (<see cref="SceneIndex"/>) : <see cref="Mark.NodeId"/> distinct du
/// node courant = cible inter-monde (voir <see cref="QuestRoute"/>).
/// </summary>
public static class QuestObjectiveMarks
{
    /// <summary>Repère : position sol Eden (x, y) sur un node.</summary>
    public sealed record Mark(int NodeId, float X, float Y, string Label,
        bool Mob, int TemplateId);

    private static volatile IReadOnlyList<Mark> _marks =
        Array.Empty<Mark>();
    private static string _key = "";
    private static int _version;

    /// <summary>Repères actifs (liste immuable, échangée atomiquement).</summary>
    public static IReadOnlyList<Mark> Marks => _marks;

    /// <summary>Incrémenté à chaque changement (les panneaux comparent).</summary>
    public static int Version => _version;

    /// <summary>Ids de modèles de mobs ciblés (affichage rouge des
    /// entités vivantes correspondantes).</summary>
    public static IReadOnlySet<int> MobIds { get; private set; } =
        new HashSet<int>();

    /// <summary>Clic droit sur un objectif : bascule les repères de cet
    /// objectif (même ligne = efface). Les tables texte/quête sont lues
    /// ici (thread principal) ; seul le balayage des scènes part en
    /// tâche de fond, publication atomique.</summary>
    public static void Toggle(int mid, string label)
    {
        var key = $"{mid}|{label}";
        if (key == _key)
        {
            Clear();
            return;
        }
        _key = key;
        Publish(Array.Empty<Mark>());
        var targets = Targets(mid, label);
        _ = Task.Run(() =>
        {
            try
            {
                var marks = Scan(targets);
                if (_key == key) Publish(marks);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[quest-marks] {e.Message}");
            }
        });
    }

    public static void Clear()
    {
        _key = "";
        Publish(Array.Empty<Mark>());
    }

    private static void Publish(IReadOnlyList<Mark> marks)
    {
        MobIds = marks.Where(m => m.Mob).Select(m => m.TemplateId)
            .ToHashSet();
        _marks = marks;
        Interlocked.Increment(ref _version);
    }

    /// <summary>Cible logique d'un objectif : mob ou PNJ (id gabarit) ou
    /// POINT monde explicite (<paramref name="Node"/> ≥ 0 — aire de
    /// voyage, site d'usage d'un item de quête positionnel).</summary>
    public sealed record Target(bool Mob, int Id, string Name,
        int Node = -1, float X = 0, float Y = 0);

    /// <summary>Cibles d'un (quête, libellé affiché). Le libellé du
    /// tracker est « {Label} n/req » — l'objectif PMS est retrouvé par
    /// préfixe.</summary>
    public static List<Target> Targets(int mid, string label)
    {
        var res = new List<Target>();
        void Mob(int id) => res.Add(new(true, id,
            TextResolver.MonsterNameShort(id) ?? $"#{id}"));
        void Npc(int id) => res.Add(new(false, id,
            TextResolver.NpcName(id) ?? $"#{id}"));
        void Point(int node, (float X, float Y)? p, string name)
        {
            if (p is { } pt) res.Add(new(false, 0, name, node, pt.X, pt.Y));
        }
        var objs = MissionObjectives.ForMission(mid);
        var obj = objs.FirstOrDefault(o => label.StartsWith(o.Label,
            StringComparison.Ordinal));
        var submit = MissionTable.Get(mid)?.SubmitNpc ?? 0;
        if (obj == null)
        {
            // « Return to X » / description libre : PNJ de rendu.
            if (submit != 0) Npc(submit);
            return res;
        }
        var (_, kind, objId) = MissionObjectives.DecodePmsKey(obj.PmsKey);
        switch (kind)
        {
            case MissionObjectives.Kind.Kill:
            case MissionObjectives.Kind.Treasure:
                Mob(objId);
                break;
            case MissionObjectives.Kind.NpcTalk:
            case MissionObjectives.Kind.NpcItem:
            case MissionObjectives.Kind.MultiTalk:
            case MissionObjectives.Kind.ItemGiven:
                Npc(objId);
                break;
            case MissionObjectives.Kind.AreaTravel:
                // AreaTravel node×100+keyId : l'event_area de la gateway
                // de clé keyId dans la scène du node (plan 01 étape 3).
                if (SceneIndex.Get(objId / 100) is { } asc)
                    Point(objId / 100, QuestObjectiveLogic
                        .AreaTravelPoint(asc, objId % 100), obj.Label);
                break;
            case MissionObjectives.Kind.ItemGot:
            case MissionObjectives.Kind.ItemCollect:
                // « Collect » : item de quête à usage positionnel
                // (SummonInEventArea node/clé de position) → point de la
                // scène ; sinon item lâché par des mobs — les objectifs
                // kill/treasure de la même quête désignent les mobs ;
                // à défaut, le PNJ de rendu.
                if (UseSiteTargets(obj.ItemId, obj.Label) is { Count: > 0 } site)
                {
                    res.AddRange(site);
                    break;
                }
                var any = false;
                foreach (var o2 in objs)
                {
                    var (_, k2, id2) = MissionObjectives
                        .DecodePmsKey(o2.PmsKey);
                    if (k2 is MissionObjectives.Kind.Kill
                        or MissionObjectives.Kind.Treasure)
                    {
                        Mob(id2);
                        any = true;
                    }
                }
                if (!any && submit != 0) Npc(submit);
                break;
        }
        return res;
    }

    /// <summary>Repère du site d'usage de l'item d'un objectif (colonne
    /// 23 de c_item → enchant → bloc SummonInEventArea node/clé) —
    /// vide si l'item n'a pas d'usage positionnel.</summary>
    private static List<Target> UseSiteTargets(int itemId, string label)
    {
        var res = new List<Target>();
        if (itemId <= 0) return res;
        var def = InventoryWindow.LookupDef(itemId);
        if (def == null || def.UseEnchantId <= 0) return res;
        var raw = EnchantLookup(def.UseEnchantId);
        if (raw == null ||
            QuestObjectiveLogic.UseSiteOf(raw) is not { } site) return res;
        if (SceneIndex.Get(site.Node) is { } sc &&
            QuestObjectiveLogic.UseSitePoint(sc, site.PosKey) is { } p)
            res.Add(new(false, 0, label, site.Node, p.X, p.Y));
        return res;
    }

    private static Dictionary<int, EnchantTable.EnchantDef>? _enchants;

    /// <summary>Cellules brutes c_enchant d'un id (chargement paresseux).</summary>
    private static string[]? EnchantLookup(int id)
    {
        if (_enchants == null)
        {
            var path = AssetCatalog.ResolveRawAsset("data/db/c_enchant.ini");
            try
            {
                _enchants = path != null ? EnchantTable.Load(path)
                    : new Dictionary<int, EnchantTable.EnchantDef>();
            }
            catch { _enchants = new(); }
        }
        return _enchants.TryGetValue(id, out var d) ? d.Raw : null;
    }

    /// <summary>Positions de ces cibles sur toutes les scènes.</summary>
    public static List<Mark> Scan(IEnumerable<Target> targets)
    {
        var res = new List<Mark>();
        var list = targets.ToList();
        if (list.Count == 0) return res;
        // Points explicites (aire de voyage, site d'item) — pas de scan.
        foreach (var t in list.Where(t => t.Node >= 0))
            res.Add(new Mark(t.Node, t.X, t.Y, t.Name, false, 0));
        foreach (var (node, sc) in SceneIndex.All())
        {
            foreach (var t in list.Where(t => t.Node < 0))
            {
                if (t.Mob)
                {
                    foreach (var m in sc.Monsters)
                        if (m.MonsterId == t.Id)
                            res.Add(new Mark(node, m.X, m.Y, t.Name,
                                true, t.Id));
                }
                else
                {
                    foreach (var n in sc.Npcs)
                        if (n.NpcId == t.Id)
                            res.Add(new Mark(node, n.X, n.Y, t.Name,
                                false, t.Id));
                }
            }
        }
        return res;
    }
}

/// <summary>Index paresseux de toutes les scènes <c>data/scene/sNNN.ini</c>
/// (spawns PNJ/mobs + portails de chaque node) — partagé par les
/// repères de quête et le routage inter-monde.</summary>
public static class SceneIndex
{
    private static readonly ConcurrentDictionary<int, SceneTable.Scene?>
        _cache = new();
    private static List<int>? _ids;
    private static readonly object _lock = new();

    /// <summary>Scène d'un node (null si absente) — parse à la demande.</summary>
    public static SceneTable.Scene? Get(int nodeId) =>
        _cache.GetOrAdd(nodeId, id =>
        {
            var path = AssetCatalog.ResolveRawAsset(
                $"data/scene/s{id:D3}.ini");
            if (path == null) return null;
            try { return SceneTable.Load(path); }
            catch { return null; }
        });

    /// <summary>Tous les nodes ayant une scène.</summary>
    public static IEnumerable<(int Node, SceneTable.Scene Scene)> All()
    {
        List<int> ids;
        lock (_lock)
        {
            _ids ??= ListNodeIds();
            ids = _ids;
        }
        foreach (var id in ids)
            if (Get(id) is { } sc) yield return (id, sc);
    }

    private static List<int> ListNodeIds()
    {
        var res = new List<int>();
        var dir = AssetCatalog.ResolveRawAsset("data/scene");
        if (dir == null) return res;
        foreach (var f in Vfs.EdenVfs.ListDir(dir))
        {
            if (!f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                continue;
            var n = Path.GetFileNameWithoutExtension(f);
            if (n.Length > 1 && (n[0] == 's' || n[0] == 'S') &&
                int.TryParse(n.AsSpan(1), out var id))
                res.Add(id);
        }
        res.Sort();
        return res;
    }
}
