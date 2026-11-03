using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;

namespace EdenClient.Hud;

/// <summary>
/// Carte régionale « Region Map - &lt;zone&gt; » (ref carte.png, touche
/// M) — construite en code avec <see cref="EeSkin"/> :
///
///   - carte de la map courante (<c>UI/map/S###m</c>, repli <c>s</c>)
///     avec PNJ, monstres, portails et joueur (positions sol Eden de la
///     scène <c>data/scene/sNNN.ini</c> via <see cref="MinimapPanel"/>) ;
///     clic gauche sur la carte = déplacement auto vers ce point ;
///   - « Search Listing » : PNJ puis monstres de la map — sélection =
///     repère clignotant, double-clic = déplacement auto ;
///   - bas : World Map (fenêtre monde), Mainland, Regional, « Coord: »
///     (point survolé, sinon joueur), « Icon Setting » ;
///   - fenêtre « City Features » : filtres des calques de marqueurs.
/// </summary>
public partial class RegionMapWindow : Control
{
    private HudRoot? _hud;
    private Panel? _win, _legend;
    private Label? _zone, _coord;
    private TextureRect? _map;
    private Tree? _list;
    private Control? _player;
    private string _builtFor = "";
    private Vector2? _hover;
    private Control? _highlight;
    private double _blink;

    /// <summary>Calques filtrables (City Features).</summary>
    private readonly Dictionary<string, List<Control>> _layers = new()
    {
        ["npc"] = new(), ["quest"] = new(), ["portal"] = new(),
    };
    private readonly Dictionary<string, bool> _shown = new()
    {
        ["npc"] = true, ["quest"] = true, ["portal"] = true,
        ["player"] = true,
    };

    private const int MapPx = 410;

    public void Build(HudRoot hud)
    {
        _hud = hud;
        _win = EeSkin.MakeWindow(hud, "Region Map", 50, 40, 656, 520);
        AddChild(_win);

        var mapBox = Box(_win, 8, 24, 424, 476, EeSkin.InsetBox());
        _zone = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        HudRoot.Pin(_zone, 0, 0, 1, 0, 0, 2, 0, 22);
        EeSkin.Shadowed(_zone, 12);
        mapBox.AddChild(_zone);
        var clip = new Control { ClipContents = true };
        HudRoot.Pin(clip, 0, 0, 0, 0, 3, 30, 3 + MapPx, 30 + MapPx);
        mapBox.AddChild(clip);
        _map = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = new Vector2(MapPx, MapPx),
            MouseFilter = MouseFilterEnum.Stop,
        };
        _map.GuiInput += OnMapInput;
        _map.MouseExited += () => _hover = null;
        clip.AddChild(_map);

        // Search Listing.
        var listBox = Box(_win, 430, 24, 648, 476, EeSkin.InsetBox());
        var lh = new Label
        {
            Text = UiTextEn.Translate("Search Listing"), HorizontalAlignment = HorizontalAlignment.Center,
        };
        HudRoot.Pin(lh, 0, 0, 1, 0, 0, 2, 0, 22);
        EeSkin.Shadowed(lh, 12);
        listBox.AddChild(lh);
        var parch = Box(listBox, 4, 24, 214, 448, EeSkin.ParchmentBox());
        _list = new Tree { HideRoot = true };
        HudRoot.Pin(_list, 0, 0, 1, 1, 2, 2, -2, -2);
        _list.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _list.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _list.AddThemeStyleboxOverride("selected", new StyleBoxFlat
            { BgColor = Color.Color8(196, 210, 232) });
        _list.AddThemeStyleboxOverride("selected_focus", new StyleBoxFlat
            { BgColor = Color.Color8(196, 210, 232) });
        _list.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
        _list.AddThemeColorOverride("font_selected_color", Color.Color8(20, 20, 18));
        _list.AddThemeFontSizeOverride("font_size", 11);
        _list.ItemSelected += OnListSelected;
        _list.ItemActivated += OnListActivated;
        parch.AddChild(_list);

        // Rangée du bas.
        var world = Btn(UiTextEn.Translate("World Map"), 8, 88, () => _hud?.WorldMap?.Toggle());
        var main = Btn(UiTextEn.Translate("Mainland"), 94, 174, null);
        var reg = Btn(UiTextEn.Translate("Regional"), 180, 260, null);
        reg.Disabled = false;
        reg.ToggleMode = true;
        reg.ButtonPressed = true;
        var cl = new Label { Text = UiTextEn.Translate("Coord:") };
        HudRoot.Pin(cl, 0, 0, 0, 0, 268, 484, 312, 506);
        EeSkin.Shadowed(cl, 11);
        _win.AddChild(cl);
        var cbox = Box(_win, 314, 484, 424, 506, EeSkin.InsetBox());
        _coord = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _coord.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        EeSkin.Shadowed(_coord, 11);
        cbox.AddChild(_coord);
        Btn(UiTextEn.Translate("Icon Setting"), 556, 648, () =>
        {
            if (_legend != null) _legend.Visible = !_legend.Visible;
        });
        _ = world; _ = main;

        BuildLegend(hud);
        DragHelper.Persist(_win, "regionmap");
        _win.Visible = false;
        _win.VisibilityChanged += () => { if (_win.Visible) Rebuild(); };
    }

    private Button Btn(string text, int l, int r, Action? act)
    {
        var b = EeSkin.Grey(new Button
        {
            Text = text, Disabled = act == null,
            FocusMode = FocusModeEnum.None,
            TooltipText = act == null ? UiTextEn.Translate("Not available yet") : "",
        }, 11);
        HudRoot.Pin(b, 0, 0, 0, 0, l, 484, r, 506);
        if (act != null) b.Pressed += act;
        _win!.AddChild(b);
        return b;
    }

    private static Panel Box(Control parent, int l, int t, int r, int b,
        StyleBox style)
    {
        var p = new Panel();
        HudRoot.Pin(p, 0, 0, 0, 0, l, t, r, b);
        p.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(p);
        return p;
    }

    /// <summary>« City Features » : légende + filtres des calques.</summary>
    private void BuildLegend(HudRoot hud)
    {
        _legend = EeSkin.MakeWindow(hud, "City Features", 712, 40, 220, 220);
        AddChild(_legend);
        var parch = Box(_legend, 8, 24, 212, 212, EeSkin.ParchmentBox());
        var rows = new (string Key, string Label, Color Col)[]
        {
            ("npc", UiTextEn.Translate("NPC"), Color.Color8(60, 200, 80)),
            ("quest", UiTextEn.Translate("Quest objective"), Color.Color8(220, 60, 50)),
            ("portal", UiTextEn.Translate("Portal"), Color.Color8(60, 200, 235)),
            ("player", UiTextEn.Translate("You"), Color.Color8(40, 110, 230)),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var (key, label, col) = rows[i];
            var dot = new ColorRect { Color = col };
            HudRoot.Pin(dot, 0, 0, 0, 0, 12, 14 + i * 28, 22, 24 + i * 28);
            parch.AddChild(dot);
            var cb = new CheckBox
            {
                Text = label, ButtonPressed = true,
                FocusMode = FocusModeEnum.None,
            };
            HudRoot.Pin(cb, 0, 0, 0, 0, 30, 8 + i * 28, 196, 30 + i * 28);
            cb.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
            cb.AddThemeColorOverride("font_pressed_color", Color.Color8(30, 30, 28));
            cb.AddThemeColorOverride("font_hover_color", Color.Color8(30, 30, 28));
            cb.AddThemeFontSizeOverride("font_size", 12);
            cb.Toggled += on =>
            {
                _shown[key] = on;
                ApplyFilters();
            };
            parch.AddChild(cb);
        }
        _legend.Visible = false;
    }

    // ------------------------------------------------------------

    private MinimapPanel? Mini => _hud?.Minimap;
    private float WorldSpan => Mini?.WorldSpan ?? MinimapPanel.WorldSize;

    /// <summary>Recharge carte, marqueurs et liste si la map a changé.</summary>
    private void Rebuild()
    {
        var mini = Mini;
        if (mini == null || _map == null || _list == null) return;
        var code = mini.MapCode;
        if (code == _builtFor) return;
        _builtFor = code;
        var name = mini.NodeId > 0
            ? TextResolver.NodeName(mini.NodeId) ?? code : code;
        if (_zone != null) _zone.Text = name;
        SetTitle(UiTextEn.Translate($"Region Map - {name}"));
        var mTex = AssetCatalog.GetTexture($"UI/map/{code}m.png");
        _isRegionTex = mTex != null;
        _map.Texture = mTex
            ?? AssetCatalog.GetTexture($"UI/map/{code}s.png");
        foreach (var layer in _layers.Values)
        {
            foreach (var c in layer)
                if (GodotObject.IsInstanceValid(c)) c.QueueFree();
            layer.Clear();
        }
        _list.Clear();
        var root = _list.CreateItem();
        var scene = mini.Scene;
        if (scene != null)
        {
            // M8 : PNJ groupés par fonction (catégorie c_npc) comme le
            // « Search Listing » de la ref ; une couleur par catégorie.
            var palette = new[]
            {
                Color.Color8(20, 130, 40), Color.Color8(180, 125, 0),
                Color.Color8(200, 80, 20), Color.Color8(30, 90, 210),
                Color.Color8(150, 40, 150), Color.Color8(0, 135, 135),
                Color.Color8(70, 70, 70),
            };
            var groups = scene.Npcs
                .GroupBy(n => NpcCategoryTable.Of(n.NpcId))
                .OrderBy(g => g.Key == "Other" ? 1 : 0).ThenBy(g => g.Key)
                .ToList();
            for (var gi = 0; gi < groups.Count; gi++)
            {
                var col = NpcCategoryTable.ColorOf(groups[gi].Key);
                var folder = _list.CreateItem(root);
                folder.SetText(0, $"{groups[gi].Key} ({groups[gi].Count()})");
                folder.SetCustomColor(0, col);
                folder.Collapsed = true;
                foreach (var n in groups[gi].OrderBy(AutoRoute.NpcLabel))
                {
                    var label = AutoRoute.NpcLabel(n);
                    var it = _list.CreateItem(folder);
                    it.SetText(0, "› " + label);
                    it.SetMetadata(0, AddMarker("npc", n.X, n.Y, label,
                        col, 6));
                }
            }
            foreach (var gw in scene.Gateways)
            {
                var a = scene.EventAreas.FirstOrDefault(e => e.Id == gw.EventAreaId);
                if (a != null)
                    AddMarker("portal", a.X, a.Y, UiTextEn.Translate("Portal"),
                        Color.Color8(60, 200, 235), 7);
            }
        }
        _player ??= MakeDot(Color.Color8(40, 110, 230), 9, UiTextEn.Translate("You"));
        _questVer = -1;   // repères de quête à reposer sur la nouvelle map
        ApplyFilters();
    }

    private int _questVer = -1;

    /// <summary>Repères d'objectif de quête (clic droit tracker) du
    /// node courant : mobs ROUGES, PNJ jaunes. Seul usage du rouge.</summary>
    private void RebuildQuestMarks()
    {
        var mini = Mini;
        if (mini == null || _map == null) return;
        _questVer = QuestObjectiveMarks.Version;
        var layer = _layers["quest"];
        foreach (var c in layer)
            if (GodotObject.IsInstanceValid(c)) c.QueueFree();
        layer.Clear();
        foreach (var mk in QuestObjectiveMarks.Marks)
        {
            if (mk.NodeId != mini.NodeId) continue;
            AddMarker("quest", mk.X, mk.Y, mk.Label,
                mk.Mob ? Color.Color8(235, 40, 30)
                       : Color.Color8(250, 215, 50), 7);
        }
        ApplyFilters();
    }

    private void SetTitle(string text)
    {
        if (_win == null) return;
        foreach (var l in _win.GetChildren().OfType<Label>())
        {
            l.Text = text;   // 1er Label direct = titre (EeSkin.MakeWindow)
            return;
        }
    }

    private Control MakeDot(Color col, int size, string tip)
    {
        var d = new ColorRect
        {
            Color = col, Size = new Vector2(size, size),
            TooltipText = tip, MouseFilter = MouseFilterEnum.Pass,
        };
        _map!.AddChild(d);
        return d;
    }

    private Control AddMarker(string layer, float ex, float ey, string tip,
        Color col, int size)
    {
        var d = MakeDot(col, size, tip);
        d.SetMeta("ex", ex);
        d.SetMeta("ey", ey);
        Place(d, ex, ey);
        _layers[layer].Add(d);
        return d;
    }

    /// <summary>Monde → [0,1]² de la texture affichée : <c>m</c> =
    /// recadrage exact du monde (y inversé) ; repli <c>s</c> = marge
    /// 100 px (<see cref="MinimapPanel.TexMargin"/>). Prouvé par
    /// corrélation m↔s sur S001/S002/S009 (plan 3.2.2).</summary>
    private Vector2 ToNorm(float ex, float ey) => _isRegionTex
        ? MinimapPanel.WorldToRegionNorm(ex, ey, WorldSpan)
        : Mini?.WorldToNorm(ex, ey) ?? Vector2.Zero;

    private bool _isRegionTex = true;

    private void Place(Control d, float ex, float ey)
    {
        if (_map == null) return;
        var s = _map.Size;
        var n = ToNorm(ex, ey);
        d.Position = new Vector2(n.X * s.X, n.Y * s.Y) - d.Size / 2;
    }

    private void ApplyFilters()
    {
        foreach (var (key, list) in _layers)
            foreach (var c in list)
                if (GodotObject.IsInstanceValid(c)) c.Visible = _shown[key];
        if (_player != null) _player.Visible = _shown["player"];
    }

    private Vector2 MapToEden(Vector2 local)
    {
        var s = _map!.Size;
        var n = new Vector2(local.X / s.X, local.Y / s.Y);
        return _isRegionTex
            ? MinimapPanel.RegionNormToWorld(n, WorldSpan)
            : Mini?.NormToWorld(n) ?? Vector2.Zero;
    }

    private void OnMapInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm) _hover = MapToEden(mm.Position);
        if (e is InputEventMouseButton { Pressed: true,
                ButtonIndex: MouseButton.Left } mb)
        {
            var p = MapToEden(mb.Position);
            AutoRoute.ToEden(this, p.X, p.Y);
        }
    }

    private void OnListSelected()
    {
        if (_highlight != null && GodotObject.IsInstanceValid(_highlight))
            _highlight.Modulate = Colors.White;
        _highlight = _list?.GetSelected()?.GetMetadata(0).As<Control>();
    }

    private void OnListActivated()
    {
        var c = _list?.GetSelected()?.GetMetadata(0).As<Control>();
        if (c != null && c.HasMeta("ex"))
            AutoRoute.ToEden(this, (float)c.GetMeta("ex"),
                (float)c.GetMeta("ey"));
    }

    public override void _Process(double delta)
    {
        if (_win is not { Visible: true }) return;
        Rebuild();
        var mini = Mini;
        if (_questVer != QuestObjectiveMarks.Version) RebuildQuestMarks();
        if (mini != null && _player != null)
            Place(_player, mini.SelfPos.X, mini.SelfPos.Y);
        var c = _hover ?? mini?.SelfPos ?? Vector2.Zero;
        if (_coord != null) _coord.Text = UiTextEn.Translate($"X:{(int)c.X} Y:{(int)c.Y}");
        // Repère sélectionné : clignote.
        _blink += delta;
        if (_highlight != null && GodotObject.IsInstanceValid(_highlight))
            _highlight.Modulate = new Color(1, 1, 1,
                (float)(0.35 + 0.65 * Math.Abs(Math.Sin(_blink * 5))));
    }

    public void Toggle() => HudWindows.Toggle(_win);

    /// <summary>Panneau visible (registre <see cref="HudWindows"/>).</summary>
    public Control? Window => _win;
}
