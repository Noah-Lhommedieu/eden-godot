using Godot;
using EdenClient.Data;

namespace EdenClient.Screens;

/// <summary>Écran de boot : logo « EDEN LIMITLESS » + barre de
/// progression pendant le préchargement du contexte UI, puis login.</summary>
public partial class BootScreen : Control
{
    private ProgressBar? _bar;
    private Label? _status;
    private double _t;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = new Color(0.08f, 0.09f, 0.14f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var title = new Label
        {
            Text = UiTextEn.Translate("EDEN LIMITLESS"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        title.AddThemeFontSizeOverride("font_size", 64);
        title.AddThemeColorOverride("font_color",
            new Color(0.85f, 0.9f, 1f));
        title.OffsetTop = -140;
        title.OffsetLeft = -300; title.OffsetRight = 300;
        AddChild(title);

        _status = new Label
        {
            Text = UiTextEn.Translate("Loading UI resources…"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _status.OffsetTop = -20;
        _status.OffsetLeft = -300; _status.OffsetRight = 300;
        _status.AddThemeFontSizeOverride("font_size", 16);
        AddChild(_status);

        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = 0 };
        _bar.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _bar.CustomMinimumSize = new Vector2(420, 18);
        _bar.OffsetLeft = -210; _bar.OffsetRight = 210;
        _bar.OffsetTop = 20; _bar.OffsetBottom = 38;
        AddChild(_bar);
    }

    public override void _Process(double delta)
    {
        if (ScreenManager.ScreenOverrideRequested)
        {
            // ScreenManager replaces this boot panel as soon as the
            // asynchronous CEGUI context is ready. Do not race it by
            // forcing LoginScreen after 0.85 s.
            return;
        }
        _t += delta;
        if (_bar != null)
            _bar.Value = Math.Min(1.0, _t / 0.8);
        // On attend la fin du chargement async du contexte UI
        // (Task.Run dans ScreenManager) avant de passer au login.
        if (_t >= 0.85 && GetParent() is ScreenManager mgr && mgr.UiReady)
        {
            SetProcess(false);
            mgr.GoLogin();
        }
    }
}
