using EdenClient.Native;
using EdenClient.World;
using Godot;
using System.Text;
using GMatrix = System.Numerics.Matrix4x4;

namespace EdenClient.Debugging;

/// <summary>
/// Banc d'essai de NativeFx sans NifScene : construit un arbre minimal depuis un NifFile, y attache
/// NativeFx, capture. Scène : <c>scenes/fx_demo.tscn</c> (voir tools/capture-fx.sh).
/// <code>
/// --fx-demo=effect/model/s10121.nif[,autre.nif]   NIF(s) relatifs à corpus/fr-full
/// --t=1.2        temps d'effet simulé avant la capture (pas fixe 1/30)
/// --dist=4 --yaw=20 --pitch=-15 --target=0,1,0 --scale=1   caméra / échelle
/// --shot=/chemin.png
/// --fx-sweep=out.tsv   balayage headless de TOUS les NIF à particules (2 s simulées, runtime Godot)
/// </code>
/// </summary>
public partial class NativeFxDemo : Node3D
{
    string? _shot;
    float _until = 1.5f;
    int _wait;
    readonly List<NativeFxHost> _hosts = new();
    int _frames;

    static string CorpusRoot() => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../corpus/fr-full"));

    public override void _Ready()
    {
        var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
        string Arg(string k, string d = "") => args.FirstOrDefault(a => a.StartsWith(k + "="))?[(k.Length + 1)..] ?? d;
        float F(string k, float d) => float.TryParse(Arg(k), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;

        var sweep = Arg("--fx-sweep");
        if (sweep != "") { Sweep(sweep, args); return; }

        _until = F("--t", 1.5f);
        _shot = args.Any(a => a.StartsWith("--shot")) ? Arg("--shot", "/tmp/fxdemo.png") : null;
        float dist = F("--dist", -1f), yaw = F("--yaw", 20f), pitch = F("--pitch", -15f), scale = F("--scale", 1f);
        var tgArg = Arg("--target");
        var tg = (tgArg == "" ? "0,0,1" : tgArg).Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.1f, 0.11f, 0.14f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
        };

        var list = Arg("--fx-demo").Split(',', StringSplitOptions.RemoveEmptyEntries);
        float x = 0;
        float radius = 0.5f; var center = new Godot.Vector3(0, 0, 1);
        foreach (var rel in list)
        {
            NifFile nif; Node3D root;
            if (args.Contains("--fx-native"))
            {
                // chemin de production : NifScene via AssetCatalog.LoadNativeScene (-90° X appliqué par un parent)
                var scene = AssetCatalog.LoadNativeScene(rel[..^4], out var n0);
                if (scene == null || n0 == null) { GD.Print($"[fxdemo] {rel}: LoadNativeScene null"); continue; }
                nif = n0; root = new Node3D { RotationDegrees = new Godot.Vector3(-90, 0, 0) };
                root.AddChild(scene);
                scene.SetMeta("nif_block", -999);
            }
            else
            {
                nif = NifFile.Load(System.IO.Path.Combine(CorpusRoot(), rel));
                root = DemoTree.Build(nif);
            }
            if (list.Length == 1)
            {
                // cadrage auto : boîte des géométries (repère NIF) ou, à défaut, sphère de bornes des particules
                float mx = 0; var c = System.Numerics.Vector3.Zero; int n = 0;
                foreach (var sh in nif.Shapes) foreach (var v in sh.Vertices) { mx = Math.Max(mx, v.Length()); c += v; n++; }
                if (mx <= 0) foreach (var ps in nif.ParticleSystems) if (ps.Data != null) mx = Math.Max(mx, ps.Data.BoundRadius);
                if (mx > 0) radius = mx;
                GD.Print($"[fxdemo] framing radius={radius:0.0}");
                center = new Godot.Vector3(0, 0, Math.Clamp(radius * 0.35f, 0, radius));
            }
            root.Position = new Godot.Vector3(x, 0, 0);
            root.Scale = Godot.Vector3.One * scale;
            AddChild(root);
            var host = NativeFx.Attach(root, nif, new NativeFxOptions { PauseWhenHidden = false, HandleBillboards = System.Environment.GetEnvironmentVariable("EDEN_NATIVE_BB") != "1", TexPrefix = rel.Split('/')[0] + "/texture/" });
            host.Paused = true;               // pas fixe piloté par _Process ci-dessous
            _hosts.Add(host);
            GD.Print($"[fxdemo] {rel}: ps={host.Report.ParticleSystems} geoms={host.Report.Geoms} " +
                     $"drivers={string.Join(",", host.Report.Drivers.Select(k => k.Key + k.Value))} notes={string.Join(";", host.Report.Notes)}");
            x += radius * 2.2f;
        }
        if (dist < 0) dist = radius * 2.4f;
        if (tgArg == "") tg = new[] { center.X, center.Y, center.Z };
        if (args.Contains("--floor"))
        {
            var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Godot.Vector2(radius * 6, radius * 6) } };
            ((PlaneMesh)floor.Mesh).Material = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.24f, 0.2f) };
            AddChild(floor);
        }
        var cam = new Camera3D { Current = true, Far = 5000f, Fov = 50f, Environment = env };
        AddChild(cam);
        var t = new Godot.Vector3(tg[0], tg[2], -tg[1]);   // cible en repère NIF (x,y,z) -> Godot
        var dir = new Godot.Vector3(0, 0, 1).Rotated(Godot.Vector3.Right, Mathf.DegToRad(pitch)).Rotated(Godot.Vector3.Up, Mathf.DegToRad(yaw));
        cam.Position = t + dir * dist;
        cam.LookAt(t, Godot.Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (_hosts.Count == 0) return;
        _frames++;
        if (_hosts[0].Time < _until)
        {
            foreach (var h in _hosts) h.Step(1f / 30f);
            return;
        }
        if (_shot == null) return;
        if (++_wait < 4) return;
        RenderingServer.ForceDraw();
        GetViewport().GetTexture()?.GetImage()?.SavePng(_shot);
        GD.Print($"[fxdemo] shot {_shot} t={_hosts[0].Time:0.00} particles={string.Join("/", _hosts.Select(h => h.ParticleCount))}");
        GetTree().Quit();
    }

    void Sweep(string outPath, string[] args)
    {
        var root = CorpusRoot();
        var files = System.IO.Directory.EnumerateFiles(root, "*.nif", System.IO.SearchOption.AllDirectories).Order().ToArray();
        var sb = new StringBuilder("nif\tps\tgeoms\tparticles_2s\tdrivers\terrors\tnotes\n");
        int ok = 0, fail = 0, withPs = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var f in files)
        {
            var rel = System.IO.Path.GetRelativePath(root, f).Replace('\\', '/');
            string err = "";
            try
            {
                var nif = NifFile.Load(f);
                if (nif.ParticleSystems.Count == 0) continue;
                withPs++;
                var tree = DemoTree.Build(nif);
                AddChild(tree);
                var host = NativeFx.Attach(tree, nif, new NativeFxOptions { PauseWhenHidden = false });
                for (int i = 0; i < 60; i++) host.Step(1f / 30f);
                sb.AppendLine(string.Join('\t', rel, host.Report.ParticleSystems, host.Report.Geoms, host.ParticleCount,
                    string.Join(",", host.Report.Drivers.OrderBy(k => k.Key).Select(k => k.Key + k.Value)), "",
                    string.Join(";", host.Report.Notes)));
                tree.QueueFree(); ok++;
            }
            catch (Exception e)
            {
                err = e.GetType().Name + ":" + e.Message.Replace('\t', ' ').Replace('\n', ' ');
                sb.AppendLine($"{rel}\t0\t0\t0\t\t{err}\t"); fail++;
            }
        }
        System.IO.File.WriteAllText(outPath, sb.ToString());
        GD.Print($"[fxsweep] psfiles={withPs} ok={ok} fail={fail} {sw.Elapsed.TotalSeconds:0}s -> {outPath}");
        GetTree().Quit();
    }
}

/// <summary>Arbre minimal NifFile -> Node3D (repère NIF Z-up sous une racine tournée de -90° sur X).
/// Remplacé par NifScene en production ; suit son contrat (méta « nif_block »).</summary>
internal static class DemoTree
{
    public static Node3D Build(NifFile nif)
    {
        var root = new Node3D { Name = "FxRoot", RotationDegrees = new Godot.Vector3(-90, 0, 0) };
        var byBlock = new Dictionary<int, Node3D>();
        foreach (var n in nif.Nodes)
        {
            var nd = new Node3D { Name = string.IsNullOrEmpty(n.Name) ? "n" + n.Block : n.Name, Transform = NativeFx.ToTransform(n.BindLocal) };
            nd.SetMeta("nif_block", n.Block);
            byBlock[n.Block] = nd;
        }
        foreach (var n in nif.Nodes)
            (byBlock.GetValueOrDefault(n.ParentBlock) ?? root).AddChild(byBlock[n.Block]);

        foreach (var sh in nif.Shapes)
        {
            if (sh.IsHelper || sh.Node == null || sh.Indices.Length < 3) continue;
            GMatrix.Invert(sh.World, out var inv);
            var v = sh.Vertices.Select(p => { var q = System.Numerics.Vector3.Transform(p, inv); return new Godot.Vector3(q.X, q.Y, q.Z); }).ToArray();
            var idx = new int[sh.Indices.Length];
            for (int i = 0; i + 2 < idx.Length; i += 3)
            { idx[i] = sh.Indices[i]; idx[i + 1] = sh.Indices[i + 2]; idx[i + 2] = sh.Indices[i + 1]; }  // CCW NIF -> CW Godot
            var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = v;
            arr[(int)Mesh.ArrayType.Index] = idx;
            if (sh.Normals is { } nn && nn.Length == v.Length)
                arr[(int)Mesh.ArrayType.Normal] = nn.Select(p => { var q = System.Numerics.Vector3.TransformNormal(p, inv); return new Godot.Vector3(q.X, q.Y, q.Z); }).ToArray();
            if (sh.Uvs is { } uv && uv.Length == v.Length)
                arr[(int)Mesh.ArrayType.TexUV] = uv.Select(p => new Godot.Vector2(p.X, 1f - p.Y)).ToArray();
            if (sh.Colors is { } cc && cc.Length == v.Length)
                arr[(int)Mesh.ArrayType.Color] = cc.Select(p => new Color(p.X, p.Y, p.Z, p.W)).ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            var mi = new MeshInstance3D { Name = string.IsNullOrEmpty(sh.Name) ? "g" + sh.BlockIndex : sh.Name, Mesh = mesh, Transform = NativeFx.ToTransform(sh.Node.BindLocal), Visible = !sh.Node.Hidden };
            mi.SetMeta("nif_block", sh.BlockIndex);
            (byBlock.GetValueOrDefault(sh.Node.ParentBlock) ?? root).AddChild(mi);
            byBlock[sh.BlockIndex] = mi;
        }
        return root;
    }
}
