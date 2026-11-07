using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.World;

/// <summary>
/// Rendu 3D des bâtiments du hall de famille (S501/S502) — le client
/// natif choisit le modèle maison via <c>S_Buliding → 4PHouse.ini →
/// house/model/*.nif</c> et l'ancre via les records [position] de la
/// scène (loc → aire 0x33 + loc/10, maison de hall = aire 0x32) —
/// mêmes règles que <c>CZoneServer::GetBuildingPositionByBuildingLoc
/// /GetNpcPositionByNpcLoc</c> côté serveur.
///
/// Données : à l'entrée d'un hall on envoie
/// <see cref="ZoneServerFamilyInstanceGetData"/> (132) ; la réponse 487
/// porte le niveau de la famille (modèle du manoir) et le conteneur 0
/// (bâtiments posés, item_type 0x29, tid = clé S_Buliding). Toute mise
/// à jour du conteneur (484/485/486) redéclenche une requête — la
/// relecture complète évite les deltas fragiles.
///
/// Les nœuds sont parentés sous le « World » (rot −90°/X) du map root
/// courant : les modèles NIF restent en coords natives Z-up et la
/// position s'écrit en coords Eden (x,y,z), le facing en rotation Z.
/// </summary>
public partial class FamilyHallBuildings : Node3D
{
    private readonly ConcurrentQueue<Action> _pending = new();
    private EdenSession? _session;
    private Node? _hallMap;
    private bool _requested;

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[hallbld] dispatch : {ex}");
            }
        }
        WatchHallMap();
    }

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(487, r =>
        {
            var m = new ZoneClientFamilyInstanceData();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                _requested = false;   // armé pour le refresh suivant
                OnInstanceData(m);
            });
        });
        // 484 = refresh conteneur (réponse à la pose), 485/486 =
        // delete/put broadcast aux membres → relecture complète
        // (force : une pose peut précéder la première réponse 487).
        foreach (var ncid in new[] { 484, 485, 486 })
            s.OnZone(ncid, _ =>
                _pending.Enqueue(() => RequestIfInHall(true)));
        // --hall-enter=<famId> : diag — envoie le 176 (visite du hall)
        // quelques secondes après l'entrée en zone, comme le bouton
        // « EnterTown » de la liste des villes.
        var enterArg = OS.GetCmdlineUserArgs().FirstOrDefault(
            a => a.StartsWith("--hall-enter="));
        if (enterArg != null &&
            int.TryParse(enterArg[13..], out var famId))
            GetTree().CreateTimer(5.0).Timeout += () =>
            {
                _ = s.SendZoneAsync(new ZoneServerFamilyTownEnter
                    { Value = famId }, CancellationToken.None);
                GD.Print($"[hallbld] 176 envoyé fam={famId}");
            };
    }

    /// <summary>Détecte l'entrée/sortie de hall via le map root
    /// (<c>Map_S501</c>/<c>Map_S502</c>, enfants du GameScene parent).</summary>
    private void WatchHallMap()
    {
        Node? map = null;
        var parent = GetParent();
        if (parent != null)
            foreach (var child in parent.GetChildren())
            {
                var n = child.Name.ToString();
                if (n.Equals("Map_S501", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Map_S502", StringComparison.OrdinalIgnoreCase))
                { map = child; break; }
            }
        if (map == _hallMap) return;
        _hallMap = map;
        _requested = false;
        // --hall-test : banc de rendu hors session — manoir niveau 5 +
        // un exemplaire de chaque modèle de bâtiment (locs 0,10,…
        // couvrant les aires 51+ en ordre croissant).
        if (_hallMap != null && _session == null &&
            OS.GetCmdlineUserArgs().Any(a => a == "--hall-test" ||
                a.StartsWith("--hall-test=")))
        {
            var world = _hallMap.FindChild("World", false, false)
                as Node3D;
            if (world != null)
            {
                var demo = new List<ZoneClientFamilyInstanceData.Building>();
                short loc = 0;
                foreach (var t in new[] { 101, 102, 103, 104, 105, 106 })
                { demo.Add(new(loc, 0, t, 0x29, 0, 0, 0)); loc += 10; }
                Rebuild(world, HallNodeId(), 5, demo);
            }
            return;
        }
        RequestIfInHall();
        // --hall-cmd=<texte> : diag — commande GM (« //tp x z »…) envoyée
        // via ZoneTextCommand 3 s après l'apparition de la map hall.
        var cmdArg = _hallMap != null ? OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--hall-cmd=")) : null;
        if (cmdArg != null)
        {
            var text = cmdArg[11..];
            var s = _session;
            GetTree().CreateTimer(3.0).Timeout += () =>
            {
                _ = s?.SendZoneAsync(new ZoneTextCommand { Text = text },
                    CancellationToken.None);
                GD.Print($"[hallbld] cmd envoyée : {text}");
            };
        }
    }

    private void RequestIfInHall(bool force = false)
    {
        if (_hallMap == null || _session == null ||
            (_requested && !force)) return;
        _requested = true;
        try
        {
            _ = _session.SendZoneAsync(
                new ZoneServerFamilyInstanceGetData(),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[hallbld] send 132 : {ex.Message}");
        }
    }

    private int HallNodeId() =>
        _hallMap!.Name.ToString().EndsWith("S502",
            StringComparison.OrdinalIgnoreCase) ? 502 : 501;

    /// <summary>Réponse 487 — reconstruit le manoir de hall + les
    /// bâtiments du conteneur 0 (sous « World » du map root).</summary>
    private void OnInstanceData(ZoneClientFamilyInstanceData m)
    {
        var world = _hallMap?.FindChild("World", false, false) as Node3D;
        if (world == null) return;
        Rebuild(world, HallNodeId(), m.Level, m.Buildings);
    }

    private static void Rebuild(Node3D world, int nodeId, short level,
        IEnumerable<ZoneClientFamilyInstanceData.Building> items)
    {
        var scenePath = (TextResolver.ResolveFile ??
            StoreTable.ResolveDefault)(
            $"data/scene/s{nodeId}.ini");
        if (scenePath == null) return;
        var scene = SceneTable.Load(scenePath);

        // Un seul groupe de rendu — recréé à chaque rafraîchissement
        // (pose/retrait, changement de niveau).
        var old = world.FindChild("HallBuildings", false, false);
        old?.QueueFree();
        var root = new Node3D { Name = "HallBuildings" };
        world.AddChild(root);

        // Le manoir de hall : loc de base 100 + chaînage par niveau
        // (SpawnFamilyHallNpcsAsync serveur). Aire 0x32 (loc 100).
        if (HouseTable.ResolveHallBuilding(nodeId, level) is
                { } hallDef &&
            HouseTable.GetHouse(hallDef.Code) is { } hall)
            SpawnHouse(root, scene, HallSlotArea(100), hall.Model,
                "HallManor");

        // Bâtiments posés (item_type 0x29) — loc → aire 0x33 + loc/10.
        var nBld = 0;
        var mapItems = new List<(string, int, float, float)>();
        if (HouseTable.ResolveHallBuilding(nodeId, level) is { } mdef &&
            scene.PositionGroups.TryGetValue(HallSlotArea(100), out var mp) &&
            mp.Count > 0)
            mapItems.Add((HallMapInfo.TypeName(1), level, mp[0].Item1,
                mp[0].Item2));
        foreach (var b in items)
        {
            if (b.ItemType != 0x29 || b.Loc < 0) continue;
            var def = HouseTable.GetBuilding(b.Tid);
            var house = def == null ? null : HouseTable.GetHouse(def.Code);
            if (def == null || house == null || house.Model.Length == 0)
            {
                GD.Print($"[hallbld] bâtiment loc={b.Loc} tid={b.Tid} " +
                         "ignoré : template/modèle introuvable");
                continue;
            }
            if (SpawnHouse(root, scene, HallSlotArea(b.Loc), house.Model,
                    $"HallB_{b.Loc}"))
            {
                nBld++;
                if (scene.PositionGroups.TryGetValue(HallSlotArea(b.Loc),
                        out var bp) && bp.Count > 0)
                    mapItems.Add((HallMapInfo.TypeName(def.Type),
                        b.Level > 0 ? b.Level : def.Level, bp[0].Item1,
                        bp[0].Item2));
            }
        }
        GD.Print($"[hallbld] node={nodeId} niveau={level} : manoir + " +
                 $"{nBld} bâtiment(s) posé(s) sur " +
                 $"{items.Count(i => i.ItemType == 0x29)} reçu(s)");
        HallMapInfo.Set(nodeId, mapItems);
        SpawnGatherMarkers(root, scene, nodeId);
    }

    /// <summary>Repères des aires de récolte physiques — event_area
    /// GATEWAY dont la keyId ([gateway_area] col4) vaut 60/61/62. Le
    /// natif n'a pas de « nœud » : l'outil (pioche / gants / boule de
    /// cristal, S_Item col32 « 501 502;60|61|62 ») s'utilise en clic
    /// droit DANS l'aire (CEC_AttrGainCollectItem::CanExecute
    /// @0x085056b0). Ces anneaux + piliers ne sont qu'une aide
    /// visuelle (non natifs) pour retrouver les aires dans la ville.</summary>
    private static void SpawnGatherMarkers(Node3D root,
        SceneTable.Scene scene, int nodeId)
    {
        var defs = new (int Key, string Label, Color Col)[]
        {
            (60, "Mine de cristal\n(pioche)", new Color(0.4f, 0.8f, 1f)),
            (61, "Champ\n(gants)", new Color(0.5f, 1f, 0.4f)),
            (62, "Fissure de mana\n(boule de cristal)",
                new Color(0.8f, 0.5f, 1f)),
        };
        foreach (var (key, label, col) in defs)
        {
            var gw = scene.Gateways.FirstOrDefault(g => g.KeyId == key);
            var ev = gw == null ? null : scene.EventAreas
                .FirstOrDefault(e => e.Id == gw.EventAreaId);
            if (ev == null)
            {
                GD.Print($"[hallbld] aire de récolte key={key} absente " +
                         $"de la scène S{nodeId}");
                continue;
            }
            var holder = new Node3D
            {
                Name = $"Gather_{key}",
                Position = new Vector3(ev.X, ev.Y, ev.Z),
            };
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(col.R, col.G, col.B, 0.55f),
            };
            // Repère « Z-up » : Torus/Cylinder ont leur axe en Y local,
            // +90° en X le couche sur Z (anneau dans le plan XY).
            holder.AddChild(new MeshInstance3D
            {
                Name = "Ring",
                Mesh = new TorusMesh
                {
                    InnerRadius = MathF.Max(ev.Radius - 0.5f, 0.1f),
                    OuterRadius = ev.Radius, Rings = 64, RingSegments = 6,
                },
                MaterialOverride = mat,
                Rotation = new Vector3(Mathf.Pi / 2f, 0f, 0f),
                Position = new Vector3(0f, 0f, 0.3f),
            });
            holder.AddChild(new MeshInstance3D
            {
                Name = "Pillar",
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.2f, BottomRadius = 0.2f, Height = 10f,
                },
                MaterialOverride = mat,
                Rotation = new Vector3(Mathf.Pi / 2f, 0f, 0f),
                Position = new Vector3(0f, 0f, 5f),
            });
            holder.AddChild(new Label3D
            {
                Name = "Label",
                Text = label,
                Modulate = col,
                FontSize = 64,
                PixelSize = 0.02f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Position = new Vector3(0f, 0f, 11.5f),
            });
            root.AddChild(holder);
            GD.Print($"[hallbld] aire de récolte key={key} « " +
                     $"{label.Replace('\n', ' ')} » @({ev.X:F1},{ev.Y:F1}," +
                     $"{ev.Z:F1}) r={ev.Radius:F1}");
        }
    }

    private static Aabb AabbOf(Node3D n)
    {
        var box = new Aabb();
        var first = true;
        foreach (var mi in n.FindChildren("*", "MeshInstance3D",
                     true, false).Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            var b = mi.GetAabb();
            var t = mi.GlobalTransform;
            var corners = new Vector3[8];
            for (var i = 0; i < 8; i++)
                corners[i] = t * b.GetEndpoint(i);
            foreach (var c in corners)
                if (first) { box = new Aabb(c, Vector3.Zero); first = false; }
                else box = box.Expand(c);
        }
        return box;
    }

    /// <summary>loc conteneur → aire [position] de la scène —
    /// <c>GetBuildingLocByTargetLoc</c> natif : loc 100 → aire 0x32,
    /// loc L → 0x33 + L/10.</summary>
    private static int HallSlotArea(short loc) =>
        loc == 100 ? 0x32 : 0x33 + loc / 10;

    /// <summary>Instancie le NIF maison au premier record [position]
    /// de l'aire — position/facing natifs (coords Eden sous « World »).</summary>
    private static bool SpawnHouse(Node3D root, SceneTable.Scene scene,
        int area, string model, string name)
    {
        if (!scene.PositionGroups.TryGetValue(area, out var pos) ||
            pos.Count == 0)
        {
            GD.Print($"[hallbld] {name} : aire [position] {area:X} " +
                     "absente de la scène");
            return false;
        }
        var (x, y, z, face) = pos[0];
        // --hall-test=offNN : décale la maison de NN unités en x —
        // pour distinguer à l'écran les modèles spawnés des structures
        // cuites dans la map.
        var offArg = OS.GetCmdlineUserArgs().FirstOrDefault(
            a => a.StartsWith("--hall-test=off"));
        if (offArg != null &&
            float.TryParse(offArg[15..], out var off))
            x += off;
        var node = AssetCatalog.LoadNativeScene(
            $"house/model/{model}", out _);
        if (node == null)
        {
            GD.Print($"[hallbld] modèle absent house/model/{model}");
            return false;
        }
        GD.Print($"[hallbld] +{name} {model} aire={area:X} " +
                 $"@({x:F1},{y:F1},{z:F1}) facing={face:F3}");
        node.Name = name;
        // Orientation : le facing de l'aire [position] est un « dir »
        // Eden, et la maison tourne comme le repère de ses slots NPC —
        // SFamily::GetNpcPositionByNpcLoc @0x84662b0 :
        //   x = bx + dx·cos f + dy·sin f ; y = by + dy·cos f − dx·sin f
        // soit une rotation de −f autour de Z dans le repère natif
        // Z-up du modèle (4PHouse : les slots sont à −Y de la façade,
        // donc la façade regarde −Y à f = 0). ToYaw(f) = π − f (repère
        // des PERSONNAGES, face +Y natif) retournait la maison de 180° ;
        // preuve : aires 52/53 (x≈207, bord est) f=π/2 → façade vers −X
        // (centre), aire 54 (x≈47) f=3π/2 → façade vers +X, aire 55
        // (51,78) f=5π/4 → façade vers le centre (+,+).
        var holder = new Node3D
        {
            Name = $"{name}_h",
            Position = new Vector3(x, y, z),
            Rotation = new Vector3(0f, 0f, -face),
        };
        holder.AddChild(node);
        root.AddChild(holder);
        if (OS.GetCmdlineUserArgs().Contains("--hall-test"))
        {
            var aabb = AabbOf(node);
            GD.Print($"[hallbld]   → global={holder.GlobalPosition} " +
                     $"aabb={aabb.Position}→{aabb.End} " +
                     $"meshes={node.FindChildren("*", "MeshInstance3D",
                         true, false).Count}");
        }
        return true;
    }
}
