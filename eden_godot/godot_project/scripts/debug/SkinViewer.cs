using Godot;
using EdenClient.Game;
using EdenClient.Data;
using System.Globalization;

namespace EdenClient.Debugging;

/// <summary>Visionneuse de skins (outil QA, voir docs/tools/skin-viewer.md) : choisir corps
/// (race/genre), classe + style de tenue, coiffure, visage, couleurs peau/cheveux/yeux
/// (sélecteurs natifs C_CharColor), gains de teinte ; rendu par le même
/// <see cref="CharVisual.BuildBody"/> que le jeu. Lancement : <c>tools/skinview.sh</c>.
/// Args (après <c>--</c>) : --body=0|1|2|4|6|8|9 --class=N --style=N --hair=N --face=N
/// --hc=N --sc=N --ec=N --ec2=N --yaw=deg --dist=m --skin-gain=f --face-gain=f
/// --all-bodies (7 corps côte à côte) --no-ui --bg=r,g,b --shot=png (capture puis quit).</summary>
public partial class SkinViewer : Node3D
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    private static readonly (int Code, string Label)[] Bodies =
    {
        (0, "0 g - Humain F"), (1, "1 b - Humain M"), (2, "2 m - Zumi"), (4, "4 f - Anuran"),
        (6, "6 k - Ursun"), (8, "8 l - Halfkin F"), (9, "9 c - Halfkin M"),
    };

    private int _body, _cls = 1, _style, _hair = 5, _face, _hc, _sc, _ec, _ec2;
    private float _yaw = 180f, _dist = 4.4f, _camY = 1.0f;
    private bool _allBodies, _noUi;
    private string? _shot;
    private int _frames, _shotAt = 150;
    private bool _dirty = true;
    private Node3D _stage = null!;
    private Camera3D _cam = null!;
    private WorldEnvironment _we = null!;
    private Label _info = null!;
    private ColorRect _swatchSkin = null!, _swatchHair = null!, _swatchEye = null!;
    private readonly Dictionary<string, SpinBox> _spins = new();
    private OptionButton _bodyOpt = null!;
    private bool _dragging;

    public override void _Ready()
    {
        TextResolver.ResolveFile ??= rel =>
            AssetCatalog.ResolveRawAsset(rel) ?? DataPaths.ResolveFile(rel);
        var bg = new Godot.Color(0.30f, 0.32f, 0.36f);
        var sunE = 1.4f; var ambE = 0.55f;
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            var kv = a.Split('=', 2);
            var v = kv.Length > 1 ? kv[1] : "";
            switch (kv[0])
            {
                case "--body": _body = int.Parse(v); break;
                case "--class": _cls = int.Parse(v); break;
                case "--style": _style = int.Parse(v); break;
                case "--hair": _hair = int.Parse(v); break;
                case "--face": _face = int.Parse(v); break;
                case "--hc": _hc = int.Parse(v); break;
                case "--sc": _sc = int.Parse(v); break;
                case "--ec": _ec = int.Parse(v); break;
                case "--ec2": _ec2 = int.Parse(v); break;
                case "--yaw": _yaw = float.Parse(v, CI); break;
                case "--dist": _dist = float.Parse(v, CI); break;
                case "--cam-y": _camY = float.Parse(v, CI); break;
                case "--sun": sunE = float.Parse(v, CI); break;
                case "--amb": ambE = float.Parse(v, CI); break;
                case "--skin-gain": CharVisual.SkinGain = float.Parse(v, CI); break;
                case "--face-gain": CharVisual.FaceSkinGain = float.Parse(v, CI); break;
                case "--all-bodies": _allBodies = true; break;
                case "--no-ui": _noUi = true; break;
                case "--shot-frames": _shotAt = int.Parse(v); break;
                case "--shot": _shot = v.Length > 0 ? v : "/tmp/skinviewer.png"; break;
                case "--bg":
                    var p = v.Split(',');
                    bg = new Godot.Color(float.Parse(p[0], CI), float.Parse(p[1], CI), float.Parse(p[2], CI));
                    break;
            }
        }
        if (_allBodies) _dist = Math.Max(_dist, 11f);

        _we = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = bg,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Godot.Color(1f, 1f, 1f),
                AmbientLightEnergy = ambE,
            },
        };
        AddChild(_we);
        var sun = new DirectionalLight3D { LightEnergy = sunE, ShadowEnabled = true };
        AddChild(sun);
        sun.LookAtFromPosition(Vector3.Zero, new Vector3(-0.35f, -0.8f, -0.45f));
        sun.RotationDegrees = new Vector3(-50f, 25f, 0f);
        _stage = new Node3D();
        AddChild(_stage);
        _cam = new Camera3D { Fov = 30f, Far = 100f, Current = true };
        AddChild(_cam);
        if (!_noUi) BuildUi();
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Layer = 50 };
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(8, 8) };
        layer.AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        _bodyOpt = new OptionButton();
        foreach (var b in Bodies) _bodyOpt.AddItem(b.Label);
        _bodyOpt.Select(Math.Max(0, Array.FindIndex(Bodies, b => b.Code == _body)));
        _bodyOpt.ItemSelected += i => { _body = Bodies[(int)i].Code; _dirty = true; };
        AddRow(box, "Corps (race/genre)", _bodyOpt);
        Spin(box, "Classe (0=base)", "class", 0, 60, _cls, v => _cls = v);
        Spin(box, "Style tenue", "style", 0, 8, _style, v => _style = v);
        Spin(box, "Coiffure", "hair", 0, 63, _hair, v => _hair = v);
        Spin(box, "Visage", "face", 0, 63, _face, v => _face = v);
        Spin(box, "Peau (sel.)", "sc", 0, 63, _sc, v => _sc = v);
        Spin(box, "Cheveux (sel.)", "hc", 0, 63, _hc, v => _hc = v);
        Spin(box, "Yeux 1 (sel.)", "ec", 0, 63, _ec, v => _ec = v);
        Spin(box, "Yeux 2 (sel.)", "ec2", 0, 63, _ec2, v => _ec2 = v);
        SpinD(box, "Gain peau corps", "sg", 0.5, 4, CharVisual.SkinGain, v => CharVisual.SkinGain = (float)v, 0.1);
        SpinD(box, "Gain peau visage", "fg", 0.5, 4, CharVisual.FaceSkinGain, v => CharVisual.FaceSkinGain = (float)v, 0.1);
        var sw = new HBoxContainer();
        _swatchSkin = Swatch(sw); _swatchHair = Swatch(sw); _swatchEye = Swatch(sw);
        AddRow(box, "Peau/cheveux/yeux", sw);
        var all = new CheckBox { Text = "7 corps côte à côte", ButtonPressed = _allBodies };
        all.Toggled += on => { _allBodies = on; _dist = on ? 11f : 4.4f; _dirty = true; };
        box.AddChild(all);
        _info = new Label();
        box.AddChild(_info);
        box.AddChild(new Label { Text = "Souris : glisser = tourner, molette = zoom" });
    }

    private static ColorRect Swatch(Control parent)
    {
        var c = new ColorRect { CustomMinimumSize = new Vector2(48, 20) };
        parent.AddChild(c);
        return c;
    }

    private static void AddRow(Container parent, string label, Control ctl)
    {
        var h = new HBoxContainer();
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(150, 0) });
        h.AddChild(ctl);
        parent.AddChild(h);
    }

    private void Spin(Container parent, string label, string key, double min, double max, double val,
        Action<int> set, double step = 1)
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = val };
        s.ValueChanged += d => { set((int)Math.Round(d)); _dirty = true; };
        _spins[key] = s;
        AddRow(parent, label, s);
    }

    private void SpinD(Container parent, string label, string key, double min, double max, double val,
        Action<double> set, double step)
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = val };
        s.ValueChanged += d => { set(d); _dirty = true; };
        _spins[key] = s;
        AddRow(parent, label, s);
    }

    private float _height = 1.7f;

    private static Aabb BodyBounds(Node3D body)
    {
        Aabb? acc = null;
        foreach (var m in body.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (!m.IsVisibleInTree() && !m.Visible) continue;
            var a = m.GlobalTransform * m.GetAabb();
            acc = acc == null ? a : acc.Value.Merge(a);
        }
        return acc ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    private void Rebuild()
    {
        foreach (var c in _stage.GetChildren()) c.QueueFree();
        var codes = _allBodies ? Bodies.Select(b => b.Code).ToArray() : new[] { _body };
        var n = codes.Length;
        for (var i = 0; i < n; i++)
        {
            var app = Protocol.AppearanceCodec.Encode(new Protocol.AppearanceCodec.Appearance(
                BodyCode: codes[i], HairStyle: _hair, FaceStyle: _face, HairColor: _hc,
                SkinColor: _sc, EyesColor: _ec, EyesColor2: _ec2));
            var body = CharVisual.BuildBody(app, null, null, classId: _cls, style: _style);
            if (body == null) { GD.Print($"[skinviewer] corps {codes[i]} introuvable"); continue; }
            // GLB Z-up natif : même orientation que EntityRegistry.SetVisual ; pivot = yaw.
            body.RotationDegrees = new Vector3(-90f, 180f, 0f);
            var pivot = new Node3D { Position = new Vector3((i - (n - 1) / 2f) * 1.4f, 0, 0),
                RotationDegrees = new Vector3(0, _yaw, 0) };
            pivot.AddChild(body);
            _stage.AddChild(pivot);
            var bb = BodyBounds(body);
            GD.Print($"[skinviewer] corps {codes[i]} bbox={bb.Position}+{bb.Size}");
        }
        var bc = _allBodies ? 0 : _body;
        var sk = CharColorTable.SkinColor(bc, _sc);
        var hr = CharColorTable.HairTint(bc, _hc);
        var ey = CharColorTable.EyesTint(_ec);
        if (_swatchSkin != null) { _swatchSkin.Color = sk; _swatchHair.Color = hr; _swatchEye.Color = ey; }
        var msg = $"corps={(_allBodies ? "tous" : bc.ToString())} classe={_cls}/{_style} coif={_hair} visage={_face} " +
                  $"peau=({sk.R:F2},{sk.G:F2},{sk.B:F2}) cheveux=({hr.R:F2},{hr.G:F2},{hr.B:F2}) " +
                  $"yeux=({ey.R:F2},{ey.G:F2},{ey.B:F2}) gain={CharVisual.SkinGain:F1}/{CharVisual.FaceSkinGain:F1}";
        GD.Print("[skinviewer] " + msg);
        if (_info != null) _info.Text = msg.Replace(" cheveux", "\ncheveux");
        _dirty = false;
    }

    public override void _Process(double delta)
    {
        if (_dirty) Rebuild();
        var y = _camY;
        _cam.Position = new Vector3(0, y, _dist);
        _cam.LookAt(new Vector3(0, y - 0.05f, 0));
        if (_shot != null && ++_frames == _shotAt)
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(_shot);
            GD.Print($"[skinviewer] shot {_shot}");
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _dist = Math.Max(0.8f, _dist * 0.92f); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _dist = Math.Min(30f, _dist * 1.08f); break;
            case InputEventMouseMotion mm when _dragging:
                _yaw += mm.Relative.X * 0.5f;
                foreach (var c in _stage.GetChildren())
                    if (c is Node3D n3 && !n3.IsQueuedForDeletion()) n3.RotationDegrees = new Vector3(0, _yaw, 0);
                break;
        }
    }
}
