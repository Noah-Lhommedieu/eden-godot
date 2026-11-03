using Godot;
using EdenClient.Game;

namespace EdenClient.Debugging;

/// <summary>Matrice de test anims joueurs : les 7 corps (b/g/m/f/k/l/c =
/// race x sexe, <see cref="CharModelSpec.BodyLetter"/>) en rangée, chaque
/// phase joue l'anim réellement choisie par
/// <see cref="DirectionalLocomotion"/> pour un record donné (avant, A, D,
/// arrière, diagonales, saut, idle) avec le cap du corps natif. Logue
/// <c>[matrix] SHOT phase=… t=…</c> aux instants de capture (Flameshot
/// externe) et un rapport durée/pistes par corps.
/// Usage : <c>godot --path godot_project scenes/anim_matrix.tscn</c>.</summary>
public partial class AnimMatrix : Node3D
{
    private static readonly (string Label, int Record)[] Phases =
    {
        ("idle", -1), ("W avant", DirectionalLocomotion.Fwd),
        ("A gauche", DirectionalLocomotion.Left),
        ("D droite", DirectionalLocomotion.Right),
        ("S arriere", DirectionalLocomotion.Back),
        ("W+A", DirectionalLocomotion.FwdLeft),
        ("W+D", DirectionalLocomotion.FwdRight),
        ("S+A", DirectionalLocomotion.BackLeft),
        ("S+D", DirectionalLocomotion.BackRight),
        ("saut", -2),
    };
    private const float PhaseLen = 2.4f;
    private readonly List<(string Letter, Node3D Holder,
        EntityAnimator? Anim)> _bodies = new();
    private Label3D? _hud;
    private int _phase = -1;
    private float _t;
    private bool _shot1, _shot2, _mobsOnly;

    public override void _Ready()
    {
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
            { RotationDegrees = new Vector3(-50f, -30f, 0f),
              LightEnergy = 1.1f });
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh
            {
                Size = new Vector2(40f, 40f),
                Material = new StandardMaterial3D
                    { AlbedoColor = new Color(0.45f, 0.47f, 0.42f) },
            },
        };
        AddChild(ground);

        if (OS.GetCmdlineUserArgs().Contains("--mobs"))
        {
            // Test d'axe avant des modèles de MOBS : même transform que
            // EntityRegistry.SetVisual (-90,180) + cap π → doivent faire
            // face à la caméra comme les corps joueurs.
            var stems = new[] { "M025", "M168", "M287", "M017", "M162",
                "M001", "M012" };
            var mx = -1.5f * (stems.Length - 1) / 2f;
            foreach (var st in stems)
            {
                var g = AssetCatalog.TryGetGlb($"monster/model/{st}",
                    out var gp) ? AssetCatalog.LoadGlbNode(gp) : null;
                if (g == null) { mx += 1.5f; continue; }
                g.RotationDegrees = new Vector3(-90f, 180f, 0f);
                var h = new Node3D { Position = new Vector3(mx, 0f, 0f),
                    Rotation = new Vector3(0f, Mathf.Pi, 0f) };
                h.AddChild(g);
                AddChild(h);
                EntityAnimator.Attach(g, $"monster/animation/{st}");
                // Normalise la taille (certains mobs sont énormes).
                var bb = new Aabb(); var first = true;
                foreach (var mi in g.FindChildren("*", "MeshInstance3D",
                             true, false).Cast<MeshInstance3D>())
                {
                    var a = mi.GlobalTransform * mi.GetAabb();
                    bb = first ? a : bb.Merge(a); first = false;
                }
                var md = Mathf.Max(bb.Size.X, Mathf.Max(bb.Size.Y, bb.Size.Z));
                if (md > 0.01f) h.Scale = Vector3.One * (1.3f / md);
                AddChild(new Label3D { Text = st, FontSize = 64,
                    PixelSize = 0.004f, Position = new Vector3(mx, 0.05f, 0.5f),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true });
                mx += 1.5f;
            }
            var cam2 = new Camera3D { Current = true, Far = 200f, Fov = 50f };
            AddChild(cam2);
            cam2.Position = new Vector3(0f, 1.5f, 6.2f);
            cam2.LookAt(new Vector3(0f, 0.85f, 0f), Vector3.Up);
            _mobsOnly = true;
            return;
        }
        var codes = new[] { 1, 0, 2, 4, 6, 8, 9 };
        var x = -1.5f * (codes.Length - 1) / 2f;
        foreach (var code in codes)
        {
            var letter = CharModelSpec.BodyLetter(code);
            var app = Protocol.AppearanceCodec.Encode(
                new Protocol.AppearanceCodec.Appearance(
                    BodyCode: code, HairStyle: 5, FaceStyle: 0,
                    HairColor: 0, SkinColor: 0, EyesColor: 0,
                    EyesColor2: 0));
            var body = CharVisual.BuildBody(app);
            if (body == null)
            {
                GD.Print($"[matrix] {letter} : BuildBody null");
                x += 1.5f;
                continue;
            }
            body.RotationDegrees = new Vector3(-90f, 180f, 0f); // comme en jeu
            var holder = new Node3D { Position = new Vector3(x, 0f, 0f) };
            holder.AddChild(body);
            AddChild(holder);
            var an = body.GetChildren().OfType<EntityAnimator>()
                .FirstOrDefault();
            if (an != null) an.AutoLocomotion = false;
            _bodies.Add((letter, holder, an));
            var lbl = new Label3D
            {
                Text = letter.ToUpperInvariant(), FontSize = 64,
                PixelSize = 0.004f, Position = new Vector3(x, 0.05f, 0.5f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
            };
            AddChild(lbl);
            // Rapport : durées/pistes des clips de locomotion.
            var rep = letter + ":";
            if (an?.Player is { } pl)
                foreach (var n in new[] { "stand01", "move01", "move02",
                    "move03", "move04", "jump01" })
                    rep += pl.HasAnimation(n)
                        ? $" {n}={pl.GetAnimation(n).Length:F2}s/" +
                          $"{pl.GetAnimation(n).GetTrackCount()}t"
                        : $" {n}=ABSENT";
            else rep += " PAS D'ANIMATOR";
            GD.Print("[matrix] clips " + rep);
            x += 1.5f;
        }
        _hud = new Label3D
        {
            FontSize = 72, PixelSize = 0.005f,
            Position = new Vector3(0f, 2.3f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        };
        AddChild(_hud);
        var cam = new Camera3D { Current = true, Far = 200f, Fov = 50f };
        AddChild(cam);
        cam.Position = new Vector3(0f, 1.5f, 6.2f);
        cam.LookAt(new Vector3(0f, 0.85f, 0f), Vector3.Up);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_mobsOnly)
        {
            if (_t > 3f && !_shot1) { _shot1 = true; GD.Print("[matrix] SHOT mobs"); }
            if (_t > 4f) { GD.Print("[matrix] DONE"); GetTree().Quit(); }
            return;
        }
        if (_phase < 0 || _t >= PhaseLen)
        {
            _phase++;
            _t = 0f; _shot1 = _shot2 = false;
            if (_phase >= Phases.Length)
            {
                GD.Print("[matrix] DONE");
                GetTree().Quit();
                return;
            }
            StartPhase();
        }
        if (!_shot1 && _t > 1.0f)
        {
            _shot1 = true;
            GD.Print($"[matrix] SHOT {_phase}a");
        }
        if (!_shot2 && _t > 1.35f)
        {
            _shot2 = true;
            GD.Print($"[matrix] SHOT {_phase}b");
        }
    }

    private void StartPhase()
    {
        var (label, rec) = Phases[_phase];
        var anim = rec switch
        {
            -1 => "stand01", -2 => "jump01",
            _ => DirectionalLocomotion.AnimFor(rec, false),
        };
        // Face caméra (+Z) = yaw 180 ; décalage de cap natif aux
        // diagonales (FaceOffset, + = gauche du perso).
        var off = rec >= 0 ? DirectionalLocomotion.FaceOffset(rec, false)
            : 0f;
        _hud!.Text = $"{label}  ->  {anim}" +
            (rec >= 0 ? $"  (rec {rec}, cap {Mathf.RadToDeg(off):F0})"
                : "");
        GD.Print($"[matrix] PHASE {_phase} {label} anim={anim} " +
                 $"capOffset={Mathf.RadToDeg(off):F0}");
        foreach (var (letter, holder, an) in _bodies)
        {
            holder.Rotation = new Vector3(0f, Mathf.Pi + off, 0f); // avant du perso = -Z : pi = face caméra
            if (an == null) continue;
            if (rec == -2) an.PlayOnce(anim); else an.PlayLoop(anim);
            if (!(an.Has(anim)))
                GD.Print($"[matrix] MANQUE {letter} {anim}");
        }
    }
}
