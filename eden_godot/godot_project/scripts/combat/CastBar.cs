using Godot;

namespace EdenClient.Combat;

/// <summary>
/// Barre de cast self (lot S1) — Control 2D monté sur le HUD via
/// <see cref="Hud.HudExtensions"/>, affiché bas-centre (au-dessus de
/// la hotbar) pendant le cast confirmé par le 279
/// <c>ClientSpellCastOK</c> (durée = castDs/10 — décisecondes
/// natives). Fermeture : fin de barre, 287 <c>ClientSpellBreak</c>
/// ou 281 <c>ClientSpellHitToLife</c> (via <see cref="SkillCast"/>).
/// </summary>
public partial class CastBar : Control
{
    /// <summary>Instance courante (consommée par SkillCast).</summary>
    public static CastBar? Instance { get; private set; }

    private ProgressBar? _bar;
    private Label? _label;
    private PanelContainer? _namePill;
    private Label? _nameLabel;
    private float _duration = 1f;
    private float _elapsed;
    private bool _running;
    // --cast-shot=<path> : capture à mi-cast (la barre est brève).
    private string? _castShotPath;
    private bool _castShotDone;

    public override void _Ready()
    {
        Instance = this;
        Visible = false;
        BuildContent();
        foreach (var args in new[] { OS.GetCmdlineArgs(),
                     OS.GetCmdlineUserArgs() })
            foreach (var a in args)
                if (a.StartsWith("--cast-shot="))
                    _castShotPath = a.Split('=', 2)[1];
    }

    /// <summary>Démarre la barre : <paramref name="seconds"/> = durée
    /// du cast (279 → castDs/10 ; repli 1 s si ≤ 0).</summary>
    public void BeginCast(float seconds, string? spellName = null)
    {
        if (_namePill != null && _nameLabel != null)
        {
            _nameLabel.Text = spellName ?? "";
            _namePill.Visible = !string.IsNullOrEmpty(spellName);
        }
        _duration = seconds > 0.05f ? seconds : 1f;
        _elapsed = 0f;
        _running = true;
        _awaitingEnd = false;
        _overdue = 0f;
        if (_label != null)
            _label.Text = Remaining(_duration);
        if (_bar != null)
        {
            _bar.MinValue = 0;
            _bar.MaxValue = _duration;
            _bar.Value = 0;
        }
        Visible = true;
    }

    /// <summary>Cast interrompu (287/410) — barre masquée sans
    /// « complétion ».</summary>
    public void CancelCast()
    {
        _running = false;
        _awaitingEnd = false;
        Visible = false;
    }

    /// <summary>Cast arrivé à terme (281 reçu) — barre pleine puis
    /// masquée.</summary>
    public void CompleteCast()
    {
        _elapsed = _duration;
        if (_bar != null) _bar.Value = _duration;
        _running = false;
        _awaitingEnd = false;
        Visible = false;
    }

    /// <summary>Temps restant « 0.9 » (ref : décimale, sans unité).</summary>
    private static string Remaining(float sec) =>
        Math.Max(0f, sec).ToString("0.0",
            System.Globalization.CultureInfo.InvariantCulture);

    // Après l'échéance locale : la barre reste visible, pleine — la fin
    // est serveur (281/287/410 ferment). Garde-fou +3 s : un paquet de
    // fin perdu ne laisse pas la barre ouverte indéfiniment.
    private bool _awaitingEnd;
    private float _overdue;
    private const float OverdueGraceSec = 3f;

    public override void _Process(double delta)
    {
        if (_awaitingEnd)
        {
            _overdue += (float)delta;
            if (_overdue >= OverdueGraceSec) CancelCast();
            return;
        }
        if (!_running) return;
        _elapsed += (float)delta;
        if (_bar != null) _bar.Value = _elapsed;
        if (_label != null)
            _label.Text = Remaining(_duration - _elapsed);
        if (!_castShotDone && _castShotPath != null &&
            _elapsed >= _duration * 0.5f)
        {
            _castShotDone = true;
            var p = System.IO.Path.IsPathRooted(_castShotPath)
                ? _castShotPath
                : System.IO.Path.GetFullPath(_castShotPath);
            RenderingServer.ForceDraw();
            var err = GetViewport().GetTexture()?.GetImage()
                ?.SavePng(p) ?? Error.FileNotFound;
            GD.Print($"[cast-shot] {p} err={err}");
        }
        if (_elapsed >= _duration)
        {
            _running = false;
            if (_bar != null) _bar.Value = _duration;
            if (_label != null) _label.Text = Remaining(0f);
            _overdue = 0f;
            _awaitingEnd = true;
        }
    }

    /// <summary>Ref TimedCastSkill.png : cadre orné DejaVu
    /// castingBar* (embouts losange), piste sombre, remplissage rose,
    /// 370×18 centré à H-170, temps restant au centre ; nom du sort
    /// (« Earth Shock ») dans une pastille sombre bordée centrée
    /// juste au-dessus de la barre.</summary>
    private void BuildContent()
    {
        var hud = GetParent() as Hud.HudRoot;
        var root = new Panel { Name = "__castbar", MouseFilter = MouseFilterEnum.Ignore };
        Hud.HudRoot.Pin(root, 0.5f, 1, 0.5f, 1, -185, -170, 185, -152);
        var frame = hud != null ? Hud.EeSkin.Nine(hud, "DejaVu",
            "castingBar", "", new[]
            {
                "LT", "T", "RT", "L", "C", "R", "LB", "B", "RB",
            }) : null;
        root.AddThemeStyleboxOverride("panel",
            frame ?? Hud.EeSkin.BarTrack());
        AddChild(root);

        _bar = new ProgressBar
        {
            Name = "__bar",
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Hud.HudRoot.Pin(_bar, 0, 0, 1, 1, 24, 4, -24, -4);
        _bar.AddThemeStyleboxOverride("background", Hud.EeSkin.BarTrack());
        _bar.AddThemeStyleboxOverride("fill", Hud.EeSkin.HpFill);
        root.AddChild(_bar);

        _label = new Label
        {
            Name = "__time",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Hud.EeSkin.Shadowed(_label, 12);
        root.AddChild(_label);

        _namePill = new PanelContainer
        {
            Name = "__spellname", Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var pill = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.06f, 0.92f),
            BorderColor = new Color(0.62f, 0.64f, 0.66f),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        pill.SetBorderWidthAll(1);
        pill.SetCornerRadiusAll(3);
        _namePill.AddThemeStyleboxOverride("panel", pill);
        _nameLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Hud.EeSkin.Shadowed(_nameLabel, 12);
        _namePill.AddChild(_nameLabel);
        // Centré sur la barre, 8 px au-dessus du cadre.
        _namePill.GrowHorizontal = GrowDirection.Both;
        _namePill.GrowVertical = GrowDirection.Begin;
        Hud.HudRoot.Pin(_namePill, 0.5f, 0, 0.5f, 0, 0, -8, 0, -8);
        root.AddChild(_namePill);
    }
}
