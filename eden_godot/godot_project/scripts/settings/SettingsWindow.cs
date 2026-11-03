using EdenClient.Data;
using EdenClient.Hud;
using Godot;

namespace EdenClient.Settings;

/// <summary>
/// Fenêtre « Paramètres » : panneau HUD standard (même chrome que la
/// mini-carte & co : <see cref="Hud.DragHelper.EnsureTitleBar"/> +
/// <see cref="Hud.EeSkin"/> thémé, drag, Échap, position persistée),
/// hébergé par <see cref="SettingsApplier"/> dans son CanvasLayer.
/// Contenu entièrement construit en code : 5 onglets
/// (Display / Audio / Hotkeys / UI Settings / Preferences) +
/// barre Default / Confirm / Cancel. Toutes les couleurs viennent de
/// <see cref="Hud.EeSkin.Palette"/> (aucune couleur en dur).
///
/// Chaque widget écrit dans <see cref="SettingsApplier.S"/> puis
/// appelle <see cref="SettingsApplier.ApplyAndSave"/> — application
/// à chaud + persistance immédiate (<c>gamedata/settings.cfg</c>).
/// L'onglet Touches passe en mode capture : clic sur le bouton de
/// binding → <see cref="TryCaptureRemap"/> (appelé par l'autoload en
/// <c>_Input</c>) assigne la prochaine touche pressée ; Échap annule.
/// </summary>
public partial class SettingsWindow : Panel
{
    private ClientSettings S => SettingsApplier.Instance!.S;

    private TabContainer? _tabs;
    private bool _sync;

    // --- capture de remap ---
    private string? _captureAction;
    private Button? _captureBtn;
    private readonly Dictionary<string, Button> _bindButtons = new();

    // --- widgets graphismes (refresh après preset) ---
    private CheckBox? _ckShadows, _ckBloom, _ckDof, _ckSsao, _ckSsil,
        _ckFog, _ckGrass, _ckParticles;
    private OptionButton? _optShadowSize, _optAa, _optTexQ;
    private HSlider? _slViewDist;
    private Label? _lblViewDist;
    private OptionButton? _optPreset;
    private OptionButton? _optModels, _optOutline;
    private HSlider? _slCharDraw, _slMaxFx, _slBgFps;
    private Label? _lblCharDraw, _lblMaxFx, _lblBgFps;
    private CheckBox? _ckFxDeath, _ckFxWater, _ckFx3D, _ckFxPhoto,
        _ckFxGlare;

    private const int WinW = 440, WinH = 600;

    /// <summary>Le HUD (chrome natif) était-il présent à la
    /// construction ? Sert à reconstruire la fenêtre quand on passe
    /// de l'écran login au jeu (cadre natif disponible).</summary>
    public bool BuiltWithHud { get; private set; }

    public override void _Ready()
    {
        Name = "SettingsWnd";
        Visible = false;
        // Ref ParameterPanel : x 469, y 66 en 1280×720 (élargi : les
        // libellés FR dépassaient les 340 px d'origine).
        Hud.HudRoot.Pin(this, 0, 0, 0, 0, 420, 50, 420 + WinW, 50 + WinH);
        var hud = Hud.HudExtensions.FindHud();
        BuiltWithHud = hud?.Tex != null;
        // Hors HUD (login) : cadre de repli aux couleurs de la palette
        // (FillClientArea lit le stylebox "panel" pour ses marges).
        AddThemeStyleboxOverride("panel", FallbackFrame());
        Hud.DragHelper.EnsureTitleBar(this, "Settings");
        Hud.DragHelper.FillClientArea(this, Hud.EeSkin.WindowBody);
        Hud.DragHelper.Persist(this, "settings");
        Theme = Hud.EeSkin.PanelTheme(hud);
        // Les initialiseurs de widgets émettent toggled/value_changed
        // — sous _sync sinon la construction écrirait des défauts
        // dans S (preset→Custom) et déclencherait ApplyAndSave.
        _sync = true;
        Build();
        RefreshAll();   // resync sur les valeurs chargées + _sync=false
        ApplyContrast();
        VisibilityChanged += () => { if (Visible) ApplyContrast(); };
    }

    private static StyleBoxFlat FallbackFrame() => new()
    {
        BgColor = Hud.EeSkin.WindowBody,
        BorderColor = Hud.EeSkin.TitleBand,
        BorderWidthTop = 2, BorderWidthLeft = 2,
        BorderWidthRight = 2, BorderWidthBottom = 2,
        ContentMarginTop = 26, ContentMarginLeft = 3,
        ContentMarginRight = 3, ContentMarginBottom = 3,
    };

    // ------------------------------------------------------------------
    // Contraste thème (A2.1) : les couleurs de texte suivent la
    // luminance des fonds de la palette courante (Purple/Coffee…
    // assombrissent le parchemin → texte sombre illisible).

    private readonly List<Label> _muted = new(), _headers = new(),
        _warns = new();

    private static float Lum(Color c) =>
        0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    private static Color InkOn(Color bg) => Lum(bg) > 0.5f
        ? Color.Color8(30, 30, 28) : Color.Color8(240, 238, 230);

    /// <summary>Recalcule les couleurs de texte depuis
    /// <see cref="Hud.EeSkin.Palette"/> (appelé à l'ouverture et après
    /// chaque changement de thème).</summary>
    public void ApplyContrast()
    {
        var pal = Hud.EeSkin.Palette;
        var ink = InkOn(pal.Parchment);
        var dark = Lum(pal.Parchment) > 0.5f;
        RefreshChrome();
        var t = Theme;
        if (t == null) return;
        t.SetColor("font_color", "Label", ink);
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            t.SetColor("font_color", type, ink);
            t.SetColor("font_pressed_color", type, ink);
            t.SetColor("font_hover_color", type, ink);
            t.SetColor("font_hover_pressed_color", type, ink);
        }
        var btnInk = InkOn(pal.GreyBtn);
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            t.SetColor("font_color", type, btnInk);
            t.SetColor("font_hover_color", type, btnInk);
            t.SetColor("font_pressed_color", type, btnInk);
        }
        t.SetColor("font_selected_color", "TabContainer", InkOn(pal.TabSel));
        t.SetColor("font_unselected_color", "TabContainer", InkOn(pal.Tab));
        t.SetColor("font_hovered_color", "TabContainer", InkOn(pal.TabSel));
        var muted = new Color(ink, 0.8f);
        var head = dark ? Color.Color8(110, 70, 0) : Color.Color8(245, 205, 100);
        var warn = dark ? Color.Color8(170, 20, 20) : Color.Color8(255, 120, 110);
        foreach (var l in _muted) l.AddThemeColorOverride("font_color", muted);
        foreach (var l in _headers) l.AddThemeColorOverride("font_color", head);
        foreach (var l in _warns) l.AddThemeColorOverride("font_color", warn);
    }

    /// <summary>Re-skinne le cadre natif après un changement de thème
    /// (l'atlas du HUD a été échangé, le stylebox posé à la
    /// construction est périmé).</summary>
    private void RefreshChrome()
    {
        var hud = Hud.HudExtensions.FindHud();
        if (hud?.Ctx != null && hud.Tex != null)
            Hud.DragHelper.ApplyNativeChrome(this, hud.Ctx, hud.Tex);
    }

    /// <summary>Note de bas d'onglet (gris lisible selon thème).</summary>
    private Label Note(VBoxContainer box, string text, bool warn = false)
    {
        var l = new Label
        {
            Text = UiTextEn.Translate(text),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        (warn ? _warns : _muted).Add(l);
        box.AddChild(l);
        return l;
    }

    private void Build()
    {
        var root = new VBoxContainer { Name = "SettingsBody" };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Sous la barre de titre (≈ 26 px) et dans le cadre.
        root.OffsetLeft = 8; root.OffsetTop = 32;
        root.OffsetRight = -8; root.OffsetBottom = -8;
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(_tabs);

        // Onglets de la ref : Display (affichage + graphismes) / Audio /
        // Hotkeys / UI Settings.
        BuildDisplayTab();
        BuildGraphicsTab();
        BuildAudioTab();
        BuildKeysTab();
        BuildInterfaceTab();
        BuildPrefsTab();

        // Ref : « Default | Confirm | Cancel » centrés. Les réglages
        // s'appliquent à chaud ; Confirm enregistre et ferme.
        var bar = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        bar.AddThemeConstantOverride("separation", 10);
        root.AddChild(bar);
        var reset = new Button { Text = UiTextEn.Translate("Default"), CustomMinimumSize = new Vector2(88, 22) };
        reset.Pressed += () =>
        {
            SettingsApplier.Instance?.ResetToDefaults();
            RefreshAll();
        };
        bar.AddChild(reset);
        var apply = new Button { Text = UiTextEn.Translate("Confirm"), CustomMinimumSize = new Vector2(88, 22) };
        apply.Pressed += () =>
        {
            SettingsApplier.Instance?.ApplyAndSave();
            Hud.HudWindows.Close(this);
        };
        bar.AddChild(apply);
        var close = new Button { Text = UiTextEn.Translate("Cancel"), CustomMinimumSize = new Vector2(88, 22) };
        close.Pressed += () => Hud.HudWindows.Close(this);
        bar.AddChild(close);
    }

    /// <summary>Sélectionne un onglet (QA --settings-tab).</summary>
    public void SelectTab(int i)
    {
        if (_tabs != null && i >= 0 && i < _tabs.GetTabCount())
            _tabs.CurrentTab = i;
    }

    /// <summary>Resynchronise tous les widgets sur
    /// <see cref="SettingsApplier.S"/> (ouverture, reset).</summary>
    public void RefreshAll()
    {
        _sync = true;
        try
        {
            SyncDisplay();
            SyncGraphics();
            SyncInterface();
            SyncPrefs();
            RebuildKeyRows();
            SyncAudio();
        }
        finally { _sync = false; }
    }

    /// <summary>Validation centrale : écriture → apply + save (sauf
    /// pendant la resync).</summary>
    private void Changed()
    {
        if (_sync) return;
        SettingsApplier.Instance?.ApplyAndSave();
        ApplyContrast();
    }

    /// <summary>Exécute <paramref name="a"/> sous <see cref="_sync"/> —
    /// les affectations programmées (<c>Selected=</c>,
    /// <c>ButtonPressed=</c>) émettent leurs signaux ; sans le garde
    /// elles repassent par les handlers et marquent CUSTOM / bouclent
    /// (bug preset→CUSTOM constaté live).</summary>
    private void RunSynced(Action a)
    {
        _sync = true;
        try { a(); }
        finally { _sync = false; }
    }

    // ==================================================================
    // Helpers de construction

    private ScrollContainer Tab(string title)
    {
        var sc = new ScrollContainer
        {
            Name = title,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _tabs!.AddChild(sc);
        var margin = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 8);
        sc.AddChild(margin);
        var box = new VBoxContainer { Name = "Box" };
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddThemeConstantOverride("separation", 3);
        margin.AddChild(box);
        return sc;
    }

    private static VBoxContainer TabBox(ScrollContainer sc) =>
        (VBoxContainer)sc.GetChild(0).GetChild(0);

    /// <summary>Ligne libellé + champ.</summary>
    private static HBoxContainer Row(VBoxContainer parent, string label,
        Control field)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        // Libellé et champ se partagent la largeur (ratio 5:6, min 0) :
        // aucun contenu ne peut pousser la ligne hors du cadre.
        var l = new Label
        {
            Text = UiTextEn.Translate(label),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 5,
            CustomMinimumSize = new Vector2(0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
        };
        row.AddChild(l);
        if (field is OptionButton ob)
        {
            ob.ClipText = true;
            ob.CustomMinimumSize = new Vector2(0, 22);
        }
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        field.SizeFlagsStretchRatio = 6;
        row.AddChild(field);
        parent.AddChild(row);
        return row;
    }

    private OptionButton Opt(VBoxContainer parent, string label,
        string[] items, int selected, Action<int> onSel)
    {
        var o = new OptionButton();
        foreach (var it in items) o.AddItem(it);
        if (selected >= 0) o.Selected = selected;
        o.ItemSelected += i =>
        {
            if (!_sync) onSel((int)i);
        };
        Row(parent, label, o);
        return o;
    }

    private CheckBox Check(Control parent, string label,
        bool value, Action<bool> onToggle)
    {
        var c = new CheckBox
        {
            Text = label, ButtonPressed = value, ClipText = true,
            CustomMinimumSize = new Vector2(0, 20),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = label,
        };
        c.Toggled += v => { if (!_sync) onToggle(v); };
        parent.AddChild(c);
        return c;
    }

    private (HSlider, Label) SliderRow(VBoxContainer parent,
        string label, double min, double max, double step,
        double value, string fmt, Action<double> onChange)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        var sl = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 18),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var val = new Label
        {
            CustomMinimumSize = new Vector2(46, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        val.Text = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            fmt, value);
        sl.ValueChanged += v =>
        {
            val.Text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                fmt, v);
            if (!_sync) onChange(v);
        };
        box.AddChild(sl);
        box.AddChild(val);
        Row(parent, label, box);
        return (sl, val);
    }

    // ==================================================================
    // Onglet Affichage (D1)

    private OptionButton? _optRes, _optMode, _optFps;
    private VBoxContainer? _displayBox;
    private CheckBox? _ckVsync;

    private void BuildDisplayTab()
    {
        var box = TabBox(Tab("Display"));
        _displayBox = box;

        var resItems = ClientSettings.Resolutions
            .Select(r => $"{r.W}x{r.H}").ToArray();
        _optRes = Opt(box, "Resolution", resItems, -1, i =>
        {
            var (w, h) = ClientSettings.Resolutions[i];
            S.ResolutionWidth = w; S.ResolutionHeight = h;
            Changed();
        });

        _optMode = Opt(box, "Window",
            new[] { "Windowed", "Fullscreen", "Borderless windowed" },
            0, i =>
            {
                S.WindowMode = (EdenWindowMode)i;
                Changed();
            });

        _ckVsync = Check(box, "VSync",
            true, v => { S.VSync = v; Changed(); });

        _optFps = Opt(box, "Max FPS",
            new[] { "30", "60", "120", "Unlimited" }, 3, i =>
            {
                S.MaxFps = ClientSettings.FpsLimits[i];
                Changed();
            });
    }

    private void SyncDisplay()
    {
        var idx = System.Array.FindIndex(ClientSettings.Resolutions,
            r => r.W == S.ResolutionWidth && r.H == S.ResolutionHeight);
        if (_optRes != null)
        {
            if (idx < 0)
            {
                // Résolution courante hors liste : on l'ajoute (une
                // seule fois — recherche textuelle avant AddItem).
                var label =
                    $"{S.ResolutionWidth}x{S.ResolutionHeight}";
                for (var i = 0; i < _optRes.ItemCount; i++)
                    if (_optRes.GetItemText(i) == label)
                    { idx = i; break; }
                if (idx < 0)
                {
                    _optRes.AddItem(label);
                    idx = _optRes.ItemCount - 1;
                }
            }
            _optRes.Selected = idx;
        }
        if (_optMode != null) _optMode.Selected = (int)S.WindowMode;
        if (_ckVsync != null) _ckVsync.ButtonPressed = S.VSync;
        if (_optFps != null)
        {
            var fi = System.Array.IndexOf(
                ClientSettings.FpsLimits, S.MaxFps);
            _optFps.Selected =
                fi >= 0 ? fi : ClientSettings.FpsLimits.Length - 1;
        }
    }

    // ==================================================================
    // Onglet Graphismes (D1/D5)

    private void BuildGraphicsTab()
    {
        // Ref : la qualité graphique est dans l'onglet Display.
        var box = _displayBox ?? TabBox(Tab("Graphics"));

        _optPreset = Opt(box, "Optimize",
            new[] { "LOW", "MEDIUM", "HIGH", "ULTRA", "CUSTOM" },
            1, i =>
            {
                S.ApplyPreset((GraphicsPreset)i);
                RunSynced(SyncGraphics);
                Changed();
            });

        void Gfx(Action a)
        {
            a();
            S.MarkCustom();
            if (_optPreset != null)
                RunSynced(() => _optPreset.Selected =
                    (int)GraphicsPreset.Custom);
            Changed();
        }

        _ckShadows = Check(box, UiTextEn.Translate("Shadows"), true,
            v => Gfx(() => S.Shadows = v));
        _optShadowSize = Opt(box, "Shadow quality",
            new[] { "512", "1024", "2048", "4096" }, 1, i =>
                Gfx(() => S.ShadowMapSize = 512 << i));
        // Ref « Field » = distance de la scène (Camera3D.Far).
        (_slViewDist, _lblViewDist) = SliderRow(box,
            UiTextEn.Translate("Field"), 400, 4000, 50, 2000, "{0:0}",
            v => Gfx(() => S.ViewDistance = (float)v));
        (_slCharDraw, _lblCharDraw) = SliderRow(box,
            UiTextEn.Translate("Char Draw"), 5, 100, 5, 100, "{0:0}",
            v => Gfx(() => S.CharDraw = (int)v));
        (_slMaxFx, _lblMaxFx) = SliderRow(box,
            UiTextEn.Translate("Max FX"), 0, 60, 1, 60, "{0:0}",
            v => Gfx(() => S.MaxFx = (int)v));
        _optAa = Opt(box, "Anti-aliasing",
            new[] { "Off", "FXAA", "MSAA x2", "MSAA x4", "MSAA x8" },
            1, i => Gfx(() => S.Antialiasing = (AntialiasMode)i));
        _optModels = Opt(box, UiTextEn.Translate("Models"),
            new[] { UiTextEn.Translate("Low"), UiTextEn.Translate("High") },
            1, i => Gfx(() => S.ModelQuality = i));
        _optTexQ = Opt(box, UiTextEn.Translate("Textures"),
            new[] { UiTextEn.Translate("Low"), "Mid",
                    UiTextEn.Translate("High") }, 2, i =>
                Gfx(() => S.TextureQuality = i));
        _ckBloom = Check(box, UiTextEn.Translate("Glow FX"), false,
            v => Gfx(() => S.Bloom = v));
        (_slBgFps, _lblBgFps) = SliderRow(box,
            UiTextEn.Translate("Background"), 5, 60, 5, 30, "{0:0} fps",
            v => { S.BackgroundFps = (int)v; Changed(); });
        _optOutline = Opt(box, UiTextEn.Translate("Outline"),
            new[] { UiTextEn.Translate("Off"),
                    UiTextEn.Translate("Character"),
                    UiTextEn.Translate("All") }, 0, i =>
            {
                S.Outline = (OutlineMode)i;
                Changed();
            });
        _ckSsao = Check(box, "SSAO", false,
            v => Gfx(() => S.Ssao = v));
        _ckSsil = Check(box, "SSIL", false,
            v => Gfx(() => S.Ssil = v));
        _ckFog = Check(box, "Fog", true,
            v => Gfx(() => S.Fog = v));
        _ckGrass = Check(box, UiTextEn.Translate("Groundcover"), true,
            v => Gfx(() => S.Grass = v));
        _ckParticles = Check(box, "Particles", true,
            v => Gfx(() => S.Particles = v));

        // Special FX (grille 2 colonnes comme la ref).
        var g = PrefGrid(box, "Special FX");
        _ckFxDeath = Check(g, UiTextEn.Translate("Death"), true,
            v => { S.FxDeath = v; Changed(); });
        _ckFxPhoto = Check(g, UiTextEn.Translate("Photo"), false,
            v => { S.FxPhoto = v; Changed(); });
        _ckDof = Check(g, UiTextEn.Translate("Blur"), false,
            v => Gfx(() => S.Dof = v));
        _ckFxGlare = Check(g, UiTextEn.Translate("Glare"), false,
            v => { S.FxGlare = v; Changed(); });
        _ckFxWater = Check(g, UiTextEn.Translate("Water Wave"), true,
            v => { S.FxWater = v; Changed(); });
        _ckFx3D = Check(g, UiTextEn.Translate("3D Effect"), true,
            v => { S.Fx3D = v; Changed(); });
        foreach (var c in new[] { _ckFxDeath, _ckFxPhoto, _ckDof,
                     _ckFxGlare, _ckFxWater, _ckFx3D })
            c!.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Note(box, "Textures: anisotropic filtering applies now; asset " +
                  "resolution needs a restart.");
    }

    private void SyncGraphics()
    {
        if (_optPreset != null) _optPreset.Selected = (int)S.Preset;
        if (_ckShadows != null) _ckShadows.ButtonPressed = S.Shadows;
        if (_optShadowSize != null)
            _optShadowSize.Selected = S.ShadowMapSize switch
            {
                <= 512 => 0, 1024 => 1, 2048 => 2, _ => 3,
            };
        if (_slViewDist != null) _slViewDist.Value = S.ViewDistance;
        if (_lblViewDist != null)
            _lblViewDist.Text = S.ViewDistance.ToString("0",
                System.Globalization.CultureInfo.InvariantCulture);
        if (_optAa != null) _optAa.Selected = (int)S.Antialiasing;
        if (_ckBloom != null) _ckBloom.ButtonPressed = S.Bloom;
        if (_ckDof != null) _ckDof.ButtonPressed = S.Dof;
        if (_ckSsao != null) _ckSsao.ButtonPressed = S.Ssao;
        if (_ckSsil != null) _ckSsil.ButtonPressed = S.Ssil;
        if (_ckFog != null) _ckFog.ButtonPressed = S.Fog;
        if (_ckGrass != null) _ckGrass.ButtonPressed = S.Grass;
        if (_ckParticles != null)
            _ckParticles.ButtonPressed = S.Particles;
        if (_optTexQ != null) _optTexQ.Selected = S.TextureQuality;
        if (_optModels != null) _optModels.Selected = S.ModelQuality;
        if (_optOutline != null) _optOutline.Selected = (int)S.Outline;
        if (_slCharDraw != null) _slCharDraw.Value = S.CharDraw;
        if (_slMaxFx != null) _slMaxFx.Value = S.MaxFx;
        if (_slBgFps != null) _slBgFps.Value = S.BackgroundFps;
        if (_ckFxDeath != null) _ckFxDeath.ButtonPressed = S.FxDeath;
        if (_ckFxWater != null) _ckFxWater.ButtonPressed = S.FxWater;
        if (_ckFx3D != null) _ckFx3D.ButtonPressed = S.Fx3D;
        if (_ckFxPhoto != null) _ckFxPhoto.ButtonPressed = S.FxPhoto;
        if (_ckFxGlare != null) _ckFxGlare.ButtonPressed = S.FxGlare;
    }

    // ==================================================================
    // Onglet Interface (D3)

    private HSlider? _slUiScale, _slChatFont;
    private Label? _lblChatFont;
    private Label? _lblUiScale;
    private OptionButton? _optTheme, _optFont, _optFontSize, _optLang;

    private void BuildInterfaceTab()
    {
        var box = TabBox(Tab("UI Settings"));
        Note(box, "Avoid setting font size larger than UI", warn: true);

        (_slUiScale, _lblUiScale) = SliderRow(box,
            "UI Size", 0.5, 2.0, 0.05, 1.0, "{0:0.00}",
            v => { S.UiScale = (float)v; Changed(); });

        _optTheme = Opt(box, "UI Style",
            ClientSettings.ThemeNames, 0, i =>
            {
                S.Theme = ClientSettings.ThemeNames[i];
                Changed();
            });

        _optFont = Opt(box, "Game Font",
            FontChoices(), 0, i =>
            {
                var fonts = FontChoices();
                S.Font = i <= 0 ? "" : fonts[i];
                Changed();
            });

        var sizes = Enumerable.Range(10, 15)
            .Select(s => s.ToString()).ToArray();
        _optFontSize = Opt(box, "Font Size", sizes, 4, i =>
        {
            S.FontSize = 10 + i;
            Changed();
        });

        (_slChatFont, _lblChatFont) = SliderRow(box,
            UiTextEn.Translate("Chat Font Size"), 10, 20, 1,
            HudPrefs.ChatFontSize, "{0:0}",
            v => HudPrefs.ChatFontSize = (int)v);

        _optLang = Opt(box, "Language",
            new[] { "English", "Français" }, 0, i =>
            {
                S.Language = i == 1 ? "fr" : "en";
                Changed();
            });
    }

    /// <summary>Choix de police : « Default » (police Godot intégrée,
    /// <c>S.Font = ""</c>) en tête, puis les *.ttf sous
    /// res://ui_textures + uidata/fonts natif.</summary>
    private static string[] FontChoices()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
            { "FONT01" };
        void Scan(string dir)
        {
            var fs = dir.StartsWith("res://")
                ? ProjectSettings.GlobalizePath(dir) : dir;
            // Loose ou URI « epk://… » (uidata en mode pack-seul).
            foreach (var f in Vfs.EdenVfs.ListDir(fs, recursive: true))
                if (f.EndsWith(".ttf",
                        StringComparison.OrdinalIgnoreCase))
                    set.Add(Path.GetFileNameWithoutExtension(f));
        }
        Scan("res://ui_textures");
        var uidata = DataPaths.FindSubdir("uidata");
        if (uidata != null)
            Scan(Path.Combine(uidata, "fonts"));
        return new[] { "Default" }.Concat(set).ToArray();
    }

    private void SyncInterface()
    {
        if (_slUiScale != null) _slUiScale.Value = S.UiScale;
        if (_lblUiScale != null)
            _lblUiScale.Text = S.UiScale.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture);
        if (_optTheme != null)
            _optTheme.Selected = Math.Max(0,
                System.Array.FindIndex(ClientSettings.ThemeNames,
                    t => t.Equals(S.Theme,
                        StringComparison.OrdinalIgnoreCase)));
        if (_optFont != null)
        {
            var fonts = FontChoices();
            _optFont.Selected = S.Font.Length == 0 ? 0 : Math.Max(0,
                System.Array.FindIndex(fonts,
                    f => f.Equals(S.Font,
                        StringComparison.OrdinalIgnoreCase)));
        }
        if (_optFontSize != null)
            _optFontSize.Selected =
                Math.Clamp(S.FontSize - 10, 0, 14);
        if (_slChatFont != null) _slChatFont.Value = HudPrefs.ChatFontSize;
        if (_optLang != null)
            _optLang.Selected = S.Language == "fr" ? 1 : 0;
    }

    // ==================================================================
    // Onglet Touches (D2)

    private GridContainer? _keyGrid;

    private void BuildKeysTab()
    {
        var box = TabBox(Tab("Hotkeys"));
        _keyGrid = new GridContainer
        {
            Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _keyGrid.AddThemeConstantOverride("h_separation", 6);
        _keyGrid.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(_keyGrid);
        Note(box, "Click a key, then press the new key or mouse " +
                  "button (wheel ignored, Esc cancels).");
        Note(box, "Sit / Stand is shown on your character only (the " +
                  "server has no sit message).");
        RebuildKeyRows();
    }

    private void RebuildKeyRows()
    {
        if (_keyGrid == null) return;
        foreach (var c in _keyGrid.GetChildren())
        {
            _keyGrid.RemoveChild(c);
            c.QueueFree();
        }
        _bindButtons.Clear();
        foreach (var (action, label, defs) in ClientSettings.BindingDefs)
        {
            _keyGrid.AddChild(new Label
            {
                Text = UiTextEn.Translate(label),
                CustomMinimumSize = new Vector2(60, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
            });
            var b = new Button
            {
                Text = InputBindings.Describe(S.GetBinding(action)),
                CustomMinimumSize = new Vector2(120, 0),
                ClipText = true,
            };
            var a = action;
            b.Pressed += () => BeginCapture(a, b);
            _keyGrid.AddChild(b);
            _bindButtons[action] = b;

            var def = new Button
            {
                Text = UiTextEn.Translate("Default"),
                CustomMinimumSize = new Vector2(60, 0),
                TooltipText = $"{UiTextEn.Translate("Default")}: " +
                    $"{InputBindings.Describe(defs)}",
            };
            var defaults = defs;
            def.Pressed += () =>
            {
                S.SetBinding(a, defaults);
                InputBindings.Apply(S);
                _bindButtons[a].Text = InputBindings.Describe(defaults);
                Changed();
            };
            _keyGrid.AddChild(def);
        }
    }

    private void BeginCapture(string action, Button btn)
    {
        if (_captureBtn != null)
            _captureBtn.Text = InputBindings.Describe(
                S.GetBinding(_captureAction!));
        _captureAction = action;
        _captureBtn = btn;
        btn.Text = UiTextEn.Translate("… key ?");
    }

    /// <summary>Capture de remap — appelée par l'autoload en
    /// <c>_Input</c> pour tout événement pendant que la fenêtre est
    /// visible. true = événement consommé.</summary>
    /// <summary>Entrées reçues dans le viewport de la fenêtre —
    /// la capture est aussi tentée ici (l'autoload la fait de son
    /// côté). Échap ferme la fenêtre (hors capture).</summary>
    public override void _Input(InputEvent e)
    {
        if (TryCaptureRemap(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // Échap ferme la fenêtre (hors capture) — aussi à l'écran login
        // où HudRoot (qui gère Échap ailleurs) n'existe pas.
        if (Visible && _captureAction == null &&
            e is InputEventKey { Pressed: true, Echo: false } k &&
            k.Keycode == Key.Escape)
        {
            Hud.HudWindows.Close(this);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>true pendant qu'une capture de remap est armée.</summary>
    public bool CaptureArmed => _captureAction != null;

    public bool TryCaptureRemap(InputEvent e)
    {
        if (_captureAction == null) return false;
        if (e is InputEventKey { Pressed: true, Echo: false } k &&
            k.Keycode == Key.Escape)
        {
            CancelCapture();
            return true;
        }
        var pressed = e switch
        {
            InputEventKey { Pressed: true, Echo: false } => true,
            InputEventMouseButton { Pressed: true } mb =>
                mb.ButtonIndex is not MouseButton.WheelUp
                    and not MouseButton.WheelDown,
            _ => false,
        };
        if (!pressed) return false;
        var name = InputBindings.EventName(e);
        if (name == null) return false;
        var action = _captureAction;
        S.SetBinding(action, name);
        InputBindings.Apply(S);
        CancelCapture();
        _bindButtons[action].Text = name;
        Changed();
        return true;
    }

    private void CancelCapture()
    {
        if (_captureBtn != null && _captureAction != null)
            _captureBtn.Text = InputBindings.Describe(
                S.GetBinding(_captureAction));
        _captureAction = null;
        _captureBtn = null;
    }

    // ==================================================================
    // Onglet Audio (D4)

    private HSlider? _slMaster, _slMusic, _slFx;
    private CheckBox? _ckMute;

    private void BuildAudioTab()
    {
        var box = TabBox(Tab("Audio"));
        _ckMute = Check(box, UiTextEn.Translate("Mute"), false,
            v => { S.Mute = v; Changed(); });
        (_slMaster, _) = SliderRow(box, "Master",
            0, 100, 1, 100, "{0:0}%",
            v => { S.MasterVolume = (float)(v / 100.0); Changed(); });
        (_slMusic, _) = SliderRow(box, "Music",
            0, 100, 1, 80, "{0:0}%",
            v => { S.MusicVolume = (float)(v / 100.0); Changed(); });
        (_slFx, _) = SliderRow(box, "SFX",
            0, 100, 1, 80, "{0:0}%",
            v => { S.EffectsVolume = (float)(v / 100.0); Changed(); });
        Note(box, "Music/SFX buses are created on demand — no " +
                  "in-game sounds yet.");
    }

    private void SyncAudio()
    {
        if (_ckMute != null) _ckMute.ButtonPressed = S.Mute;
        if (_slMaster != null) _slMaster.Value = S.MasterVolume * 100;
        if (_slMusic != null) _slMusic.Value = S.MusicVolume * 100;
        if (_slFx != null) _slFx.Value = S.EffectsVolume * 100;
    }
}
