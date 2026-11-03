using Godot;
using EdenClient.Game;

namespace EdenClient.Screens;

/// <summary>
/// Isolated portrait-stage renderer for character selection and
/// creation. The avatar gets its own close camera and transparent
/// viewport over the native map artwork; the distant world camera from
/// startsetting.ini is not suitable for a character portrait.
/// </summary>
public partial class CharacterPreview : SubViewportContainer
{
    private readonly SubViewport _viewport;
    private readonly Node3D _holder;
    private readonly Camera3D _camera;
    private float _yaw;

    /// <summary>Creates the portrait viewport, camera, lighting and
    /// transparent model holder.</summary>
    public CharacterPreview()
    {
        Name = "CharacterPreview";
        MouseFilter = MouseFilterEnum.Ignore;
        Stretch = true;
        StretchShrink = 1;

        _viewport = new SubViewport
        {
            Name = "AvatarViewport",
            Size = new Vector2I(800, 900),
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);

        var stage = new Node3D { Name = "Stage" };
        _viewport.AddChild(stage);
        stage.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.92f, 0.9f, 0.86f),
                AmbientLightEnergy = 0.8f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        stage.AddChild(new DirectionalLight3D
        {
            Name = "Key",
            RotationDegrees = new Vector3(-28f, -25f, 0f),
            LightColor = new Color(1f, 0.95f, 0.86f),
            LightEnergy = 1.2f,
            ShadowEnabled = true,
        });
        stage.AddChild(new DirectionalLight3D
        {
            Name = "Fill",
            RotationDegrees = new Vector3(-16f, 145f, 0f),
            LightColor = new Color(0.66f, 0.76f, 1f),
            LightEnergy = 0.45f,
            ShadowEnabled = false,
        });

        _camera = new Camera3D
        {
            Name = "PortraitCamera",
            Current = true,
            Fov = 31f,
            Position = new Vector3(0f, 1.05f, 4.8f),
        };
        stage.AddChild(_camera);

        _holder = new Node3D { Name = "Avatar" };
        stage.AddChild(_holder);
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _camera.LookAt(new Vector3(0f, 0.9f, 0f), Vector3.Up);
    }

    /// <summary>Root of the currently rendered character.</summary>
    public Node3D Holder => _holder;

    /// <summary>Rebuilds the preview from appearance and real worn
    /// items. Missing appearance shows no fabricated race/gear.
    /// Le pipeline GLB (proto parse + anims) tourne en tâche de fond —
    /// ~28 s mesurés à froid sur le méga-GLB + 88 Mo d'anims ; l'écran
    /// doit s'afficher instantanément, l'avatar pop quand prêt.</summary>
    private int _buildGen;

    /// <summary>Vrai quand le corps construit en fond a été attaché —
    /// utilisé par les captures QA pour attendre l'avatar réel.</summary>
    public bool AvatarReady { get; private set; }

    public void Show(byte[]? appearance,
        IReadOnlyDictionary<int, int>? worn = null,
        Func<int, (string Code, string ModelCode)?>? itemLookup = null)
    {
        var gen = ++_buildGen;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var t = EdenClient.Perf.PerfTrace.Mark();
            var body = CharVisual.BuildBody(appearance);
            EdenClient.Perf.PerfTrace.Report("  preview BuildBody", t);
            if (body == null) return;
            // Character meshes are native Z-up and face +Z after conversion.
            body.RotationDegrees = new Vector3(-90f, 0f, 0f);
            if (appearance is { Length: >= 32 })
            {
                t = EdenClient.Perf.PerfTrace.Mark();
                _ = CharDressing.Apply(body, appearance, worn, itemLookup);
                EdenClient.Perf.PerfTrace.Report("  preview dressing", t);
            }
            CharVisual.PlayIdle(body);
            // Attache sur le thread principal ; un Show plus récent
            // invalide ce résultat (clic rapide entre persos).
            Callable.From(() => AttachBuilt(gen, body)).CallDeferred();
        });
    }

    private void AttachBuilt(int gen, Node3D? body)
    {
        if (gen != _buildGen || body == null ||
            !GodotObject.IsInstanceValid(_holder)) return;
        AvatarReady = false;
        foreach (var child in _holder.GetChildren())
            child.QueueFree();
        _yaw = 0f;
        _holder.Rotation = Vector3.Zero;
        _holder.AddChild(body);
        AvatarReady = true;
        EdenClient.Perf.PerfTrace.Abs("preview : avatar attaché");
    }

    /// <summary>Turns the avatar in the portrait view.</summary>
    public void Rotate(float radians) => _yaw += radians;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _holder.Rotation = new Vector3(0f, _yaw, 0f);
    }
}
