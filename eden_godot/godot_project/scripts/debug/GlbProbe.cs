using Godot;

namespace EdenClient.Debugging;

/// <summary>Sonde headless : charge des GLB et liste matériaux/textures.</summary>
public partial class GlbProbe : Node3D
{
    public override void _Ready()
    {
        // --glb=<rel> : sonde un seul GLB + AABB monde (diag H1).
        // --glb-dump : liste nom+AABB monde de chaque mesh (layout S218).
        string? only = null;
        var dump = false;
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--glb=")) only = a[6..];
            if (a == "--glb-dump") dump = true;
        }
        var rels = only != null
            ? new[] { only }
            : new[]
            {
                "map/model/object/UB124", "map/model/object/UE013",
                "char/model/B", "monster/model/M025", "char/model/B1001",
            };
        foreach (var rel in rels)
        {
            if (!AssetCatalog.TryGetGlb(rel, out var p))
            {
                GD.Print($"[probe] {rel} : GLB absent");
                continue;
            }
            var node = AssetCatalog.LoadGlbNode(p);
            GD.Print($"[probe] === {rel} ({p})");
            int mats = 0, texed = 0, nullTex = 0;
            node?.PropagateCall("get_class", []);
            var stack = new System.Collections.Generic.Stack<Node>();
            if (node != null) stack.Push(node);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n is MeshInstance3D mi && mi.Mesh != null)
                {
                    for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    {
                        var m = mi.GetSurfaceOverrideMaterial(i)
                            ?? mi.Mesh.SurfaceGetMaterial(i);
                        mats++;
                        if (m is StandardMaterial3D sm)
                        {
                            if (sm.AlbedoTexture != null) texed++;
                            else { nullTex++; }
                        }
                    }
                }
                foreach (var c in n.GetChildren()) stack.Push(c);
            }
            GD.Print($"[probe] {rel}: surfaces={mats} texed={texed} noTex={nullTex}");
            if (only != null && node != null)
            {
                // AABB monde (le nœud doit être dans l'arbre pour que
                // les transforms globaux soient à jour).
                AddChild(node);
                if (dump)
                {
                    // Même repère que TempleBackdrop : parent −90°/X,
                    // offset sol −160,44.
                    var holder = new Node3D
                    {
                        RotationDegrees = World.WorldConv.EdenRootEulerDeg,
                        Position = new Vector3(0f, -160.44f, 0f),
                    };
                    RemoveChild(node);
                    AddChild(holder);
                    holder.AddChild(node);
                    DumpMeshes(node);
                    // --ray=x,y,z,tx,ty,tz : meshes dont l'AABB monde
                    // croise le rayon caméra→cible (bloqueurs de vue).
                    foreach (var a2 in OS.GetCmdlineArgs().Concat(
                                 OS.GetCmdlineUserArgs()))
                        if (a2.StartsWith("--ray=",
                                StringComparison.Ordinal))
                        {
                            var f = a2[6..].Split(',').Select(s2 =>
                                float.Parse(s2,
                                    System.Globalization.CultureInfo
                                        .InvariantCulture)).ToArray();
                            if (f.Length >= 6)
                                RayBlockers(node,
                                    new Vector3(f[0], f[1], f[2]),
                                    new Vector3(f[3], f[4], f[5]));
                        }
                }
                var aabb = WorldAabb(node);
                GD.Print($"[probe] world-aabb {aabb}");
                // Proximité du point Select_pos natif (318,613,5 en
                // coords Z-up → Godot (318,5,-613)).
                var sel = new Vector3(318f, 5f, -613f);
                var near = CountNear(node, sel, 30f);
                GD.Print($"[probe] meshes à ≤30 u de Select_pos : {near}");
            }
            node?.QueueFree();
        }
        GetTree().Quit();
    }

    private static Aabb WorldAabb(Node3D root)
    {
        var total = new Aabb();
        var first = true;
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
            {
                var a = mi.GetAabb();
                var corners = new Vector3[8];
                for (var i = 0; i < 8; i++)
                    corners[i] = a.GetEndpoint(i);
                foreach (var c in corners)
                {
                    var w = mi.GlobalTransform * c;
                    total = first ? new Aabb(w, Vector3.Zero)
                                  : total.Expand(w);
                    first = false;
                }
            }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        return total;
    }

    private static void DumpMeshes(Node root)
    {
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
            {
                var a = mi.GetAabb();
                var t = mi.GlobalTransform;
                var c = t * a.GetCenter();
                var s = a.Size * 0.5f;
                var texNames = new System.Text.StringBuilder();
                for (var si = 0; si < mi.Mesh.GetSurfaceCount(); si++)
                {
                    var m = mi.GetSurfaceOverrideMaterial(si)
                            ?? mi.Mesh.SurfaceGetMaterial(si);
                    if (m is StandardMaterial3D sm)
                    {
                        var t2 = sm.AlbedoTexture;
                        var tn = t2?.ResourceName;
                        if (string.IsNullOrEmpty(tn))
                            tn = t2?.ResourcePath ?? m.ResourceName;
                        if (!string.IsNullOrEmpty(tn))
                            texNames.Append(tn).Append(';');
                    }
                }
                GD.Print($"[dump] {mi.Name} c=({c.X:F0},{c.Y:F0}," +
                    $"{c.Z:F0}) sz=({s.X * 2:F0},{s.Y * 2:F0}," +
                    $"{s.Z * 2:F0}) tex={texNames}");
            }
            foreach (var ch in n.GetChildren()) stack.Push(ch);
        }
    }

    /// <summary>Liste les meshes dont l'AABB monde croise le segment
    /// caméra→cible — le bloqueur de la vue demandée.</summary>
    private static void RayBlockers(Node root, Vector3 from, Vector3 to)
    {
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
            {
                var a = mi.GetAabb();
                var t = mi.GlobalTransform;
                var hit = false;
                for (var s = 0f; s <= 1f && !hit; s += 0.02f)
                {
                    var p = from + (to - from) * s;
                    // AABB local → monde approximatif via coins.
                    var local = t.AffineInverse() * p;
                    if (a.HasPoint(local)) hit = true;
                }
                if (hit)
                {
                    var c = t * a.GetCenter();
                    GD.Print($"[block] {mi.Name} c=({c.X:F0},{c.Y:F0}," +
                        $"{c.Z:F0})");
                }
            }
            foreach (var ch in n.GetChildren()) stack.Push(ch);
        }
    }

    /// <summary>Hauteur du sol : y du centroïde le plus BAS parmi les
    /// faces non verticales (|ny|&gt;0,25 — marches/pentes incluses)
    /// à moins de 10 u de la colonne demandée.</summary>
    private static float FloorFaceY(Node root, Vector2 col)
    {
        var best = float.PositiveInfinity;
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
            {
                var faces = mi.Mesh.GetFaces();
                var t = mi.GlobalTransform;
                for (var i = 0; i + 2 < faces.Length; i += 3)
                {
                    var a = t * faces[i];
                    var b = t * faces[i + 1];
                    var c = t * faces[i + 2];
                    var nrm = (b - a).Cross(c - a);
                    if (nrm.LengthSquared() < 0.001f) continue;
                    nrm = nrm.Normalized();
                    var cx = (a.X + b.X + c.X) / 3f;
                    var cz = (a.Z + b.Z + c.Z) / 3f;
                    var cy = (a.Y + b.Y + c.Y) / 3f;
                    if (cy > 40f || Mathf.Abs(nrm.Y) < 0.25f) continue;
                    if (Mathf.Abs(cx - col.X) < 10f &&
                        Mathf.Abs(cz - col.Y) < 10f)
                    {
                        if (cy < best) best = cy;
                    }
                }
            }
            foreach (var ch in n.GetChildren()) stack.Push(ch);
        }
        return best;
    }

    private static int CountNear(Node3D root, Vector3 p, float radius)
    {
        var count = 0;
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null &&
                mi.GlobalPosition.DistanceTo(p) < radius)
                count++;
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        return count;
    }
}
