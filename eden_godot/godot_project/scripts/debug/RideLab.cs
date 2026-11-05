using System.Globalization;
using System.Text;
using EdenClient.Game;
using EdenClient.Hud;
using Godot;

namespace EdenClient.Debugging;

/// <summary>Banc monture + cavalier sur fond neutre (scène
/// <c>ride_lab.tscn</c>), même assemblage que le jeu
/// (<c>CharVisual.BuildRideVisual</c>, <c>RideFit</c>, <c>RideSeat</c> sur
/// L17, <c>EntityAnimator</c> cavalier piloté par celui de la monture) ;
/// les touches sont simulées comme <c>PlayerController</c> : record
/// natif → <c>DirectionalLocomotion.AnimFor/FaceOffset</c>.
/// <code>godot --path . res://scenes/ride_lab.tscn -- --rides=14010,41010
///   --script=STAND:1,W:1,A:1,D:1,S:1,J:1.2,ATK:1,CAST:1,CAST3:1
///   --shots=/tmp/lab --cam=120 [--rider=B] [--sweep=/tmp/sweep.tsv]</code>
/// Un fichier <c>--rides-file=docs/reports/FIX-F-montures-hors-archive.tsv</c>
/// (colonne <c>rideKey</c>) remplace <c>--rides</c>. Avec <c>--sweep</c> :
/// une ligne TSV par monture (mesh, textures, anims, FX, cavalier) et une
/// capture « marche » ; sans, deux captures par pas.</summary>
public partial class RideLab : Node3D
{
    private readonly List<int> _keys = new();
    private (string Name, float Secs)[] _steps = Array.Empty<(string, float)>();
    private string _dir = "/tmp/ride-lab";
    private string? _sweep;
    private float _camYaw = 120f, _camPitch = 10f, _camDist = 7f, _camH = 1.5f;
    private string _rider = "B";
    private int _ki = -1, _si;
    private float _t;
    private int _shots;
    private float _wait;
    private Node3D? _holder, _body, _mount;
    private EntityAnimator? _ma, _ra;
    private Camera3D? _cam;
    private readonly StringBuilder _tsv = new();
    private readonly List<string> _notes = new();
    private readonly HashSet<string> _seenAnims = new();
    private readonly List<string> _stepLog = new();

    public override void _Ready()
    {
        string spec = "STAND:1,W:1.2,A:1.2,D:1.2,S:1.2,J:1.2";
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--rides="))
                foreach (var k in a[8..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    _keys.Add(int.Parse(k));
            else if (a.StartsWith("--rides-file=")) LoadKeys(a[13..]);
            else if (a.StartsWith("--script=")) spec = a[9..];
            else if (a.StartsWith("--shots=")) _dir = a[8..];
            else if (a.StartsWith("--sweep=")) _sweep = a[8..];
            else if (a.StartsWith("--rider=")) _rider = a[8..];
            else if (a.StartsWith("--burst=")) _burst = int.Parse(a[8..]);
            else if (a == "--probe") _probe = true;
            else if (a.StartsWith("--cam="))
            {
                var p = a[6..].Split(',');
                _camYaw = F(p, 0, _camYaw); _camPitch = F(p, 1, _camPitch);
                _camDist = F(p, 2, _camDist); _camH = F(p, 3, _camH);
            }
        }
        _steps = spec.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                var kv = p.Split(':');
                return (kv[0].ToUpperInvariant(), kv.Length > 1 ? F(kv, 1, 1f) : 1f);
            }).ToArray();
        // Sortie captures/sweep (artifacts|/tmp), pas une donnée jeu :
        // DirectoryInfo/StreamReader gardent VfsReadGuardTests à zéro pour
        // ce fichier (le TSV d'entrée et les PNG ne viennent pas du corpus).
        new System.IO.DirectoryInfo(_dir).Create();
        if (_keys.Count == 0) _keys.Add(14010);

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.32f, 0.36f, 0.42f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.9f, 0.9f, 0.9f),
                AmbientLightEnergy = 0.7f,
            },
        });
        AddChild(new DirectionalLight3D
            { RotationDegrees = new Vector3(-50f, -30f, 0f), LightEnergy = 1.1f });
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh
            {
                Size = new Vector2(60f, 60f),
                Material = new StandardMaterial3D
                    { AlbedoColor = new Color(0.45f, 0.47f, 0.42f) },
            },
        };
        AddChild(ground);
        _cam = new Camera3D { Current = true, Far = 200f };
        AddChild(_cam);
        if (_sweep != null)
            _tsv.AppendLine("key\tstem\tmesh\tnotex\tbones\tanim\thasL17\tfx\tstand01\tmove01\tmove02\tmove03\tmove04\tjump\tattack\tmagic01\tmagic03\trider\tstatus\tnotes");
    }

    private static float F(string[] t, int i, float d) => i < t.Length &&
        float.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : d;

    private void LoadKeys(string path)
    {
        using var rd = new System.IO.StreamReader(path);
        var lines = rd.ReadToEnd().Split('\n');
        var col = Array.IndexOf(lines[0].Split('\t'), "rideKey");
        var seen = new HashSet<int>();
        foreach (var l in lines.Skip(1))
            if (int.TryParse(l.Split('\t')[col], out var k) && seen.Add(k)) _keys.Add(k);
    }

    private void Clear()
    {
        _holder?.QueueFree();
        _holder = _body = _mount = null; _ma = _ra = null;
    }

    private bool Build(int key)
    {
        Clear();
        _notes.Clear(); _seenAnims.Clear();
        var bodyCode = char.ToLowerInvariant(_rider[0]) switch
        {
            'b' => 1, 'm' => 2, 'f' => 4, 'k' => 6, 'l' => 8, 'c' => 9, _ => 0,
        };
        _body = CharVisual.BuildBody(Protocol.AppearanceCodec.Encode(
            new Protocol.AppearanceCodec.Appearance(BodyCode: bodyCode, HairStyle: 5,
                FaceStyle: 0, HairColor: 0, SkinColor: 0, EyesColor: 0, EyesColor2: 0)));
        _mount = CharVisual.BuildRideVisual(key);
        if (_body == null || _mount == null) return false;
        // Holder = nœud « Model » du joueur (cap −Z).
        _holder = new Node3D { Name = "Model" };
        AddChild(_holder);
        _body.RotationDegrees = new Vector3(-90f, 180f, 0f);
        _holder.AddChild(_body);
        var mountNode = new Node3D { Name = "_mount" };
        mountNode.AddChild(_mount);
        _holder.AddChild(mountNode);
        var fit = RideFit.Apply(key, mountNode, _mount);
        _body.Position = new Vector3(0f, fit.Lift, fit.BackZ);
        var onL17 = RideFit.Follow(mountNode, _mount, _body);
        _ma = CharVisual.MountAnimator(_mount);
        _ra = _body.FindChildren("*", "", true, false).OfType<EntityAnimator>().FirstOrDefault();
        if (_ra != null) _ra.RideMount = _ma;
        if (!onL17) _notes.Add("noL17");
        _ma?.PlayLoop("stand01");
        if (_probe) DumpMeshes();
        FrameCamera();
        return true;
    }

    private void FrameCamera()
    {
        if (_mount == null || _cam == null) return;
        var top = World.RemoteEntity.VisualTopHeight(_mount);
        var h = Mathf.Max(top, 1.5f);
        // --cam dist = facteur de la hauteur visuelle (défaut 7 → ×1,1).
        var d = Mathf.Max(h * _camDist / 6f, 3f);
        GD.Print($"[ride-lab] cadrage top={top:0.00} dist={d:0.0}");
        var yaw = Mathf.DegToRad(_camYaw);
        // Cap du modèle = −Z ; yaw 90 = vue de côté, 180 = de face.
        var dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        var pitch = Mathf.DegToRad(_camPitch);
        _cam.Position = dir * d * Mathf.Cos(pitch) + new Vector3(0f, h * 0.5f + d * Mathf.Sin(pitch), 0f);
        _cam.LookAt(new Vector3(0f, h * 0.5f, 0f), Vector3.Up);
    }

    private void Enter(string name)
    {
        // Mime PlayerController._PhysicsProcess pour un enregistrement natif.
        int rec = name switch
        {
            "W" => DirectionalLocomotion.Fwd,
            "A" => DirectionalLocomotion.Left,
            "D" => DirectionalLocomotion.Right,
            "S" => DirectionalLocomotion.Back,
            "WA" => DirectionalLocomotion.FwdLeft,
            "WD" => DirectionalLocomotion.FwdRight,
            "SA" => DirectionalLocomotion.BackLeft,
            "SD" => DirectionalLocomotion.BackRight,
            _ => -1,
        };
        if (_ra == null || _ma == null || _holder == null) return;
        _ra.InputMoving = false; _ra.Airborne = false;
        _holder.Rotation = Vector3.Zero;
        _ma.MoveSpeedScale = _ra.MoveSpeedScale = 1f;
        if (rec >= 0)
        {
            var anim = DirectionalLocomotion.AnimFor(rec, true);
            _ra.MoveAnim = anim;
            _ma.MoveAnim = DirectionalLocomotion.MountAnimFor(rec);
            _holder.Rotation = new Vector3(0f, DirectionalLocomotion.FaceOffset(rec, true), 0f);
            _ra.InputMoving = true;
        }
        else switch (name)
        {
            case "M01": case "M02": case "M03": case "M04":
                _ma.MoveAnim = "move" + name[1..]; _ra.MoveAnim = DirectionalLocomotion.AnimFor(DirectionalLocomotion.Fwd);
                _ra.InputMoving = true; break;
            case "J": _ra.InputMoving = true; _ra.Airborne = true; break;
            case "ATK": _ra.PlayOnce("attack01"); break;
            case "ATK2": _ra.PlayOnce("attack02"); break;
            case "CAST": _ra.PlayOnce("magic01"); break;
            case "CAST2": _ra.PlayOnce("magic02"); break;
            case "CAST3": _ra.PlayOnce("magic03"); break;
            case "SHOOT": _ra.PlayOnce("shoot01"); break;
        }
    }

    public override void _Process(double delta)
    {
        if (_wait > 0f) { _wait -= (float)delta; return; }
        if (_ki < 0 || _si >= _steps.Length)
        {
            if (_ki >= 0) Finish();
            _ki++;
            if (_ki >= _keys.Count) { End(); return; }
            _si = 0; _t = 0f; _shots = 0;
            if (!Build(_keys[_ki]))
            {
                GD.Print($"[ride-lab] {_keys[_ki]} : BUILD ÉCHEC (mesh ou monture absente)");
                if (_sweep != null)
                    _tsv.AppendLine($"{_keys[_ki]}\t\t0\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\tFAIL\tbuild");
                _si = _steps.Length;
                return;
            }
            _wait = 0.8f;                       // chargement/FX
            _stepStarted = false;
            return;
        }
        if (!_stepStarted) { Enter(_steps[_si].Name); _stepStarted = true; _t = 0f; }
        _t += (float)delta;
        var st = _steps[_si];
        if (_sweep != null)
        {
            if (_t > st.Secs * 0.8f && _shots == 0)
            {
                Log(st.Name);
                if (st.Name == "W") Shoot("walk");
                _shots = 1;
            }
        }
        else if (_burst > 0)
        {
            if (_probe) Sample();
            if (_burstDone < _burst && _t > st.Secs * (_burstDone + 1f) / (_burst + 1f))
            {
                RenderingServer.ForceDraw();
                GetViewport().GetTexture().GetImage().SavePng(
                    $"{_dir}/{_keys[_ki]}-{_si:D2}-{st.Name}-f{_burstDone:D2}.png");
                _burstDone++;
            }
        }
        else
        {
            if (_shots == 0 && _t > st.Secs * 0.5f) { Shoot("a"); Log(st.Name); }
            else if (_shots == 1 && _t > st.Secs * 0.9f) Shoot("b");
        }
        if (_t < st.Secs) return;
        if (_probe) ReportProbe(st.Name);
        _burstDone = 0;
        _si++; _shots = 0; _stepStarted = false;
    }

    private bool _stepStarted;

    // --burst=N : N captures espacées sur toute la durée du pas (séquence
    // d'une même anim). --probe : trajectoire Z/Y des os (repère holder,
    // cap = −Z) pendant chaque pas de marche, résumé loggé en fin de pas.
    private int _burst;
    private bool _probe;
    private int _burstDone;
    private readonly List<(float t, Dictionary<string, Vector3> p)> _samples = new();

    private void DumpMeshes()
    {
        var hd = _mount!.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
        var inv = _holder!.GlobalTransform.AffineInverse();
        foreach (var mi in _mount.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            var chain = ""; for (Node? q = mi; q != null && q != _mount; q = q.GetParent()) chain += q.GetType().Name + ":" + q.Name + (q is BoneAttachment3D ba ? "[bone=" + ba.BoneName + "]" : "") + "<";
            GD.Print($"[ride-mesh] chain {chain}");
            for (var sf = 0; sf < (mi.Mesh?.GetSurfaceCount() ?? 0); sf++)
            {
                var sb = mi.Mesh!.SurfaceGetArrays(sf)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (sb.Length == 0) continue;
                var mn = sb[0]; var mx = sb[0]; foreach (var v in sb) { mn = mn.Min(v); mx = mx.Max(v); }
                var m = mi.GetActiveMaterial(sf);
                var tx = m is StandardMaterial3D sm ? sm.AlbedoTexture?.ResourcePath : (m as ShaderMaterial)?.GetShaderParameter("albedo_tex").Obj is Texture2D t2 ? t2.ResourcePath : "?";
                GD.Print($"[ride-surf] {mi.Name} s{sf} verts={sb.Length} tex={tx} meta={m?.HasMeta("nif_tex")} bind=({mn.X:0.00},{mn.Y:0.00},{mn.Z:0.00})-({mx.X:0.00},{mx.Y:0.00},{mx.Z:0.00})");
            }
            var bb = mi.GetAabb(); var c = inv * (mi.GlobalTransform * (bb.Position + bb.Size / 2));
            GD.Print($"[ride-mesh] {mi.Name} vis={mi.Visible} skin={(mi.Skin != null)} parent={mi.GetParent()?.Name} sz={bb.Size.X:0.00},{bb.Size.Y:0.00},{bb.Size.Z:0.00} c={c.X:0.00},{c.Y:0.00},{c.Z:0.00}");
        }
    }

    private void Sample()
    {
        if (_mount == null || _holder == null) return;
        var inv = _holder.GlobalTransform.AffineInverse();
        var d = new Dictionary<string, Vector3>();
        foreach (var n in _mount.FindChildren("*", "Skeleton3D", true, false))
            if (n is Skeleton3D sk)
                for (var i = 0; i < sk.GetBoneCount(); i++)
                    d[sk.GetBoneName(i)] = inv * (sk.GlobalTransform * sk.GetBoneGlobalPose(i)).Origin;
        if (_body != null)
            foreach (var n in _body.FindChildren("*", "Skeleton3D", true, false))
                if (n is Skeleton3D rk)
                    foreach (var bn in new[] { "Bip01 Head", "Bip01 R Hand", "Bip01 L Hand", "Bip01 Spine1" })
                        if (rk.FindBone(bn) is var bi and >= 0)
                            d["RIDER " + bn] = inv * (rk.GlobalTransform * rk.GetBoneGlobalPose(bi)).Origin;
        _samples.Add((_t, d));
    }

    private void ReportProbe(string step)
    {
        if (_samples.Count < 4) { _samples.Clear(); return; }
        var names = _samples[0].p.Keys.ToList();
        // os "bas" : Y min sur le cycle le plus faible, hors racine
        var low = names.Select(n => (n, ymin: _samples.Min(s => s.p[n].Y)))
            .OrderBy(x => x.ymin).Take(6).ToList();
        var head = names.FirstOrDefault(n => n.Contains("Head")) ?? "";
        var zs = $"key={_keys[_ki]} step={step} frames={_samples.Count}";
        if (head != "")
            zs += $" headZ-rootZ={(_samples[0].p[head].Z - _samples[0].p[names[0]].Z):0.00} (neg=tete vers -Z=avant)";
        GD.Print($"[ride-probe] {zs}");
        // Latéralité : os L/R dans le repère holder (cap −Z ⇒ gauche = −X).
        foreach (var n in names.Where(n => n.Contains(" L ") || n.Contains(" R ")).Take(40))
            if (n.EndsWith("Toe0") || n.EndsWith("Hand") || n.EndsWith("Foot") || n.EndsWith("Clavicle") || n.EndsWith("Thigh"))
                GD.Print($"[ride-probe]   lat {n} X={_samples[0].p[n].X:0.00}");
        // Verdict : sens « avant » = côté de la tête (ou, sans os tête, de
        // l'os le plus éloigné) ; en marche avant, les points d'appui
        // doivent reculer (dZ de même signe que −avant).
        float fwdSign = 0f, push = 0f, pushX = 0f;
        if (head != "") fwdSign = Math.Sign(_samples[0].p[head].Z - _samples[0].p[names[0]].Z);
        foreach (var (n, ymin) in low)
        {
            var ymax0 = _samples.Max(s => s.p[n].Y);
            var thr0 = ymin + (ymax0 - ymin) * 0.3f;
            for (var i = 1; i < _samples.Count; i++)
                if (_samples[i].p[n].Y <= thr0 && _samples[i - 1].p[n].Y <= thr0)
                {
                    push += _samples[i].p[n].Z - _samples[i - 1].p[n].Z;
                    pushX += _samples[i].p[n].X - _samples[i - 1].p[n].X;
                }
        }
        {
            // Latéral (repère holder : gauche = −X) : X moyen / min / max
            // de quelques os clés relativement à la racine, + dX du 1er au dernier quart.
            string R(string n)
            {
                if (!_samples[0].p.ContainsKey(n)) return "";
                var xs = _samples.Select(q => q.p[n].X - q.p[names[0]].X).ToList();
                var tl = xs.Skip((int)(xs.Count * 0.6)).ToList();
                return $" {n.Replace("Bip01 ", "")}[end={tl.Average():0.00} min={xs.Min():0.00} max={xs.Max():0.00}]";
            }
            var lat = "";
            foreach (var n in new[] { "Bip01 Head", "Bip01 Spine1", "Tail Bone 04", "Bip01 R Hand", "Bip01 L Hand", "RIDER Bip01 Head", "RIDER Bip01 R Hand", "RIDER Bip01 L Hand" }) lat += R(n);
            foreach (var tn in new[] { "RIDER Bip01 R Hand", "RIDER Bip01 Head", "Bip01 Head" })
                if (_samples[0].p.ContainsKey(tn))
                    GD.Print($"[ride-trace] key={_keys[_ki]} step={step} {tn} X(t): " + string.Join(" ",
                        Enumerable.Range(0, 12).Select(i => _samples[Math.Min(_samples.Count - 1, i * _samples.Count / 12)])
                            .Select(q => (q.p[tn].X - q.p[names[0]].X).ToString("0.00"))));
            GD.Print($"[ride-lat] key={_keys[_ki]} step={step} mountAnim={_ma?.Current} riderAnim={_ra?.Current}{lat}");
        }
        // Déplacement implicite en espace holder (cap −Z ; A/D montés
        // pivotent le holder ±90° ⇒ leur déplacement est −Z local).
        var travel = step switch
        {
            "W" or "A" or "D" or "WA" or "WD" or "M01" => new Vector3(0f, 0f, -1f),
            "S" or "SA" or "SD" or "M04" => new Vector3(0f, 0f, 1f),
            _ => Vector3.Zero,
        };
        var vel = new Vector3(pushX, 0f, push);
        var score = travel != Vector3.Zero ? vel.Dot(travel) : 0f;
        GD.Print($"[ride-verdict] key={_keys[_ki]} step={step} fwdSign={fwdSign} " +
            $"stancePush=({pushX:0.00},{push:0.00}) headDZ={(_samples[0].p.TryGetValue(head, out var hd) ? hd.Z - _samples[0].p[names[0]].Z : 0f):0.00} " +
            $"=> {(travel == Vector3.Zero ? "-" : score < -0.005 ? "OK-avant" : score > 0.005 ? "INVERSE" : "?nul")}");
        foreach (var (n, ymin) in low)
        {
            var ymax = _samples.Max(s => s.p[n].Y);
            var thr = ymin + (ymax - ymin) * 0.3f;
            float dz = 0; var c = 0;
            for (var i = 1; i < _samples.Count; i++)
                if (_samples[i].p[n].Y <= thr && _samples[i - 1].p[n].Y <= thr)
                { dz += _samples[i].p[n].Z - _samples[i - 1].p[n].Z; c++; }
            var line = string.Join(" ", _samples.Where((_, i) => i % Math.Max(1, _samples.Count / 8) == 0)
                .Select(s => $"{s.p[n].Z:0.00}/{s.p[n].Y:0.00}"));
            GD.Print($"[ride-probe]   {n} ymin={ymin:0.00} stance dZ={dz:0.000} over {c} steps (Z/Y: {line})");
        }
        // Diag vague de queue : trajectoires Y des os Bone## triés (EDEN_TAILWAVE=1)
        if (System.Environment.GetEnvironmentVariable("EDEN_TAILWAVE") == "1")
        {
            var bones = names.Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^Bone\d+$"))
                .OrderBy(n => int.Parse(n[4..])).ToList();
            GD.Print($"[tailwave] key={_keys[_ki]} step={step} frames={_samples.Count} bones={bones.Count}");
            foreach (var n in bones)
            {
                var ys = _samples.Select(s => s.p[n].Y.ToString("0.00"));
                GD.Print($"[tailwave]   {n} Y: {string.Join(" ", ys)}");
            }
        }
        _samples.Clear();
    }

    private void Log(string step)
    {
        var line = $"[ride-lab] key={_keys[_ki]} step={step} rider={_ra?.Current} mount={_ma?.Current} " +
                   $"yaw={Mathf.RadToDeg(_holder?.Rotation.Y ?? 0):0}";
        _stepLog.Add(line);
        GD.Print(line);
        if (_ma?.Current is { Length: > 0 } c) _seenAnims.Add(c);
    }

    private void Shoot(string tag)
    {
        _shots++;
        RenderingServer.ForceDraw();
        var name = _sweep != null ? $"{_dir}/{_keys[_ki]}.png"
            : $"{_dir}/{_keys[_ki]}-{_si:D2}-{_steps[_si].Name}-{tag}.png";
        GetViewport().GetTexture().GetImage().SavePng(name);
    }

    private void Finish()
    {
        if (_sweep == null || _mount == null) return;
        var key = _keys[_ki];
        var meshes = _mount.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList();
        var mesh = meshes.Count(m => m.Mesh != null && m.Mesh.GetSurfaceCount() > 0);
        // notex = surfaces qui référencent une texture (meta « nif_tex »
        // de NifMaterials.For, recopiée par ToonMaterializer) dont le
        // fichier n'a pas résolu — VRAI trou de données. Les meshes
        // cachés du NIF (helpers « Bone », « Editable Poly »,
        // flag NiAVObject::Hidden → Visible=false) et les surfaces
        // nativement non texturées (couleur diffuse/vertex, ex. glow
        // r015) ne sont PAS des trous : ignorés ici, comptés en untx.
        var notex = 0;
        var untx = 0;
        foreach (var m in meshes)
            for (var s = 0; s < (m.Mesh?.GetSurfaceCount() ?? 0); s++)
            {
                var mat = m.GetSurfaceOverrideMaterial(s) ?? m.GetActiveMaterial(s);
                if (mat == null) continue;
                var hasTex = mat switch
                {
                    StandardMaterial3D sm => sm.AlbedoTexture != null,
                    ShaderMaterial shm => shm
                        .GetShaderParameter("albedo_tex").Obj
                        is Texture2D,
                    _ => true,
                };
                if (mat.HasMeta("nif_tex"))
                {
                    if (!hasTex) notex++;
                }
                else if (!hasTex && m.IsVisibleInTree())
                    untx++;
            }
        var bones = _mount.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().Sum(s => s.GetBoneCount());
        string Y(string a) => _ma != null && _ma.Has(a) ? "1" : "0";
        var fx = _mount.FindChildren("*", "NativeFxHost", true, false).Count;
        var l17 = _mount.FindChild("L17", true, false) != null ? "1" : "0";
        // stand01 est l'idle minimale ; move01-04/jump/attack sont des
        // données par monture (ex. r711 « Tour à Canon » n'a aucune
        // anim de marche — monture-canon natif, pas un bug).
        var ok = mesh > 0 && _ma != null && _ma.Has("stand01");
        var riderOk = _ra != null && _ra.Has("ride01") && _ra.Has("ride04");
        var jump = _ma != null && (_ma.Has("jump01") || _ma.Has("jump02")) ? "1" : "0";
        var atk = _ma != null && (_ma.Has("attack01") || _ma.Has("attack02")) ? "1" : "0";
        var status = ok && riderOk && notex == 0 ? "OK" : "FAIL";
        if (untx > 0) _notes.Add($"untextured={untx}");
        if (_ma != null && !_ma.Has("move01")) _notes.Add("noMove");
        var seen = string.Join("|", _seenAnims);
        _tsv.AppendLine($"{key}\t{RideList.Stem($"r{key:D6}")}\t{mesh}\t{notex}\t{bones}\t{(_ma != null ? 1 : 0)}\t{l17}\t{fx}\t{Y("stand01")}\t{Y("move01")}\t{Y("move02")}\t{Y("move03")}\t{Y("move04")}\t{jump}\t{atk}\t{Y("magic01")}\t{Y("magic03")}\t{(riderOk ? 1 : 0)}\t{status}\t{string.Join(",", _notes)};played={seen}");
    }

    private void End()
    {
        if (_sweep != null)
        {
            System.IO.File.WriteAllText(_sweep, _tsv.ToString());
            GD.Print($"[ride-lab] sweep -> {_sweep}");
        }
        GD.Print("[ride-lab] fin");
        GetTree().Quit();
    }
}
