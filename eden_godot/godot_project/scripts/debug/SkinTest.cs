using Godot;
using EdenClient.Game;
using EdenClient.Hud;

namespace EdenClient.Debugging;

/// <summary>Sonde visuelle skinning : charge des GLB personnages/mobs sur
/// fond neutre, joue une anim et capture — validation A1-A4 sans le bruit
/// de la map. Usage :
/// <code>godot --path . scenes/skin_test.tscn -- \
///   --model=char/model/B --anim=stand01 --shot=/tmp/skin.png</code>
/// Options : --model=&lt;rel&gt; (répétable), --anim=&lt;name|none&gt;,
/// --animfile=&lt;anims/rel&gt; (forcé, sinon stem déduit du nom du modèle),
/// --shot=&lt;png&gt; (capture ~150 frames puis quit), --t=&lt;sec&gt;.
/// </summary>
public partial class SkinTest : Node3D
{
    private int _shotFrames = -1;
    private string _shotPath = "/tmp/skin_test.png";
    private float _elapsed;
    private bool _probeBones;
    private int _frames;
    private readonly List<(Skeleton3D skel, int bone)> _probeList = new();
    private readonly Dictionary<string, Vector3> _probeT0 = new();

    /// <summary>Dump worldpos d'os clés (tête/mains/pieds) — à t≈0 et
    /// en continu pour vérifier que l'anim bouge vraiment les bones.</summary>
    private void ProbeBones()
    {
        foreach (var sk in this.FindChildren("*", "Skeleton3D", true, false)
                     .Cast<Skeleton3D>())
            for (var i = 0; i < sk.GetBoneCount(); i++)
            {
                var n = sk.GetBoneName(i);
                if (n is "Bip01 Head" or "Bip01 L Hand" or "Bip01 R Hand"
                    or "Bip01 L Foot" or "Bip01 R Foot" or "Bip01" or "Bip01 Spine")
                    _probeList.Add((sk, i));
            }
        foreach (var (sk, i) in _probeList)
        {
            var wp = (sk.GlobalTransform * sk.GetBoneGlobalPose(i)).Origin;
            _probeT0[sk.GetBoneName(i)] = wp;
            GD.Print($"[probe] t0 {sk.GetBoneName(i)} -> {wp}");
        }
    }

    /// <summary>Monture + cavalier (même assemblage que MissionBridge.SetRideModel :
    /// RideFit + RideSeat sur le nœud L17) ; l'anim est jouée sur la monture, le cavalier suit.</summary>
    private void BuildRider(int rideKey, string letter, string anim, float yaw)
    {
        var bodyCode = char.ToLowerInvariant(letter[0]) switch
        {
            'b' => 1, 'm' => 2, 'f' => 4, 'k' => 6, 'l' => 8, 'c' => 9, _ => 0,
        };
        var body = CharVisual.BuildBody(Protocol.AppearanceCodec.Encode(
            new Protocol.AppearanceCodec.Appearance(BodyCode: bodyCode, HairStyle: 5,
                FaceStyle: 0, HairColor: 0, SkinColor: 0, EyesColor: 0, EyesColor2: 0)));
        var mount = CharVisual.BuildRideVisual(rideKey);
        if (body == null || mount == null)
        {
            GD.Print($"[skintest] ride {rideKey} : body={body != null} mount={mount != null}");
            return;
        }
        var holder = new Node3D { Name = "Model", RotationDegrees = new Vector3(0f, yaw - 180f, 0f) };
        AddChild(holder);
        body.RotationDegrees = new Vector3(-90f, 180f, 0f);
        holder.AddChild(body);
        var mountNode = new Node3D { Name = "_mount" };
        mountNode.AddChild(mount);
        holder.AddChild(mountNode);
        var fit = RideFit.Apply(rideKey, mountNode, mount);
        body.Position = new Vector3(0f, fit.Lift, fit.BackZ);
        var onL17 = RideFit.Follow(mountNode, mount, body);
        var ma = CharVisual.MountAnimator(mount);
        var ra = body.FindChildren("*", "", true, false).OfType<EntityAnimator>().FirstOrDefault();
        if (ra != null) ra.RideMount = ma;
        if (anim != "none") ma?.PlayLoop(anim);
        GD.Print($"[skintest] ride key={rideKey} L17={onL17} mountAnim={ma != null} riderAnim={ra != null}");
    }

    public override void _Ready()
    {
        var models = new List<string>();
        string anim = "stand01";
        string? animFile = null;
        float delay = 1.0f;
        float yaw = 180f;
        float camDist = 3.2f;
        int rideKey = 0;
        string riderLetter = "B";
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--model=")) models.Add(a[8..]);
            else if (a.StartsWith("--anim=")) anim = a[7..];
            else if (a.StartsWith("--animfile=")) animFile = a[11..];
            else if (a.StartsWith("--t="))
                delay = float.Parse(a[4..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--yaw="))
                yaw = float.Parse(a[6..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--dist="))
                camDist = float.Parse(a[7..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--ride=")) rideKey = int.Parse(a[7..]);
            else if (a.StartsWith("--rider=")) riderLetter = a[8..];
            else if (a == "--probe") _probeBones = true;
            else if (a.StartsWith("--shot"))
            {
                _shotFrames = (int)(delay * 60) + 90;
                if (a.Contains('=')) _shotPath = a.Split('=', 2)[1];
            }
        }

        // Environnement neutre : gris moyen + soleil + sol discret.
        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.32f, 0.36f, 0.42f),
                AmbientLightSource =
                    Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.9f, 0.9f, 0.9f),
                AmbientLightEnergy = 0.7f,
            },
        };
        AddChild(env);
        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50f, -30f, 0f),
            LightEnergy = 1.1f,
        };
        AddChild(sun);
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40f, 40f) },
        };
        ((PlaneMesh)ground.Mesh).Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 0.47f, 0.42f),
        };
        AddChild(ground);

        var x = -1.2f * (models.Count - 1) / 2f;
        foreach (var rel in models)
        {
            Node3D? node;
            if (rel.StartsWith("char/model/", StringComparison.Ordinal))
            {
                // Même pipeline qu'en jeu : méga-modèle filtré (tenue
                // de base + visage + cheveux) par CharVisual.
                var letter = System.IO.Path.GetFileName(rel)[0];
                var bodyCode = char.ToLowerInvariant(letter) switch
                {
                    'b' => 1, 'm' => 2, 'f' => 4, 'k' => 6,
                    'l' => 8, 'c' => 9, _ => 0,
                };
                var app = Protocol.AppearanceCodec.Encode(
                    new Protocol.AppearanceCodec.Appearance(
                        BodyCode: bodyCode, HairStyle: 5, FaceStyle: 0,
                        HairColor: 0, SkinColor: 0, EyesColor: 0,
                        EyesColor2: 0));
                node = CharVisual.BuildBody(app);
                if (node == null)
                {
                    GD.Print($"[skintest] {rel} : BuildBody null");
                    continue;
                }
            }
            else
            {
                node = AssetCatalog.TryGetGlb(rel, out var p)
                    ? AssetCatalog.LoadGlbNode(p) : null;
                if (node == null)
                {
                    GD.Print($"[skintest] {rel} : GLB absent");
                    continue;
                }
                var stem = System.IO.Path.GetFileName(rel);
                var animPath = animFile ?? $"monster/animation/{stem}";
                var animator = EntityAnimator.Attach(node, animPath);
                if (animator != null && anim != "none")
                    animator.PlayLoop(anim);
            }
            node.Position = new Vector3(x, 0f, 0f);
            // Z-up natif -> Y-up Godot (même convention que le jeu) ;
            // +yaw pour tourner le modèle face caméra.
            node.RotationDegrees = new Vector3(-90f, yaw, 0f);
            AddChild(node);
            if (anim != "none")
            {
                var an = node.FindChildren("*", "",
                    true, false).OfType<EntityAnimator>().FirstOrDefault();
                if (anim.StartsWith("attack") || anim.StartsWith("hurt")) an?.PlayOnce(anim);
                else an?.PlayLoop(anim);
                if (an != null && !an.Has(anim)) GD.Print($"[skintest] anim absente : {anim}");
            }
            GD.Print($"[skintest] {rel} chargé, anim={anim}");
            x += 1.2f;
        }

        if (rideKey > 0) BuildRider(rideKey, riderLetter, anim, yaw);

        var cam = new Camera3D { Current = true, Far = 200f };
        AddChild(cam);
        cam.Position = new Vector3(0f, 1.15f, camDist);
        cam.LookAt(new Vector3(0f, 0.9f, 0f), Vector3.Up);

        if (_probeBones)
            ProbeBones();
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        _frames++;
        if (_probeBones && _frames % 40 == 0 && _probeList.Count > 0)
        {
            foreach (var (sk, i) in _probeList)
            {
                var wp = (sk.GlobalTransform * sk.GetBoneGlobalPose(i)).Origin;
                var n = sk.GetBoneName(i);
                GD.Print($"[probe] f{_frames} {n} -> {wp} " +
                    $"delta={(_probeT0.TryGetValue(n, out var p) ? wp - p : Vector3.Zero).Length():F3}");
            }
        }
        if (_shotFrames > 0 && --_shotFrames == 0)
        {
            RenderingServer.ForceDraw();
            GetViewport().GetTexture()?.GetImage()?.SavePng(_shotPath);
            GD.Print($"[skintest] shot -> {_shotPath} | {Native.KfSource.Stats()}");
            GetTree().Quit();
        }
    }
}
