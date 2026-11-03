using Godot;
using EdenClient.Session;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// A4.4 — overlay FPS + ping (coin haut-droit, à gauche de la minimap).
/// Visible seulement si <see cref="HudPrefs.ShowPerf"/>
/// (<c>ui.show_perf</c>). Ping = RTT Echo→pong lissé
/// (<see cref="EdenSession.PingMs"/>), « -- » hors ligne. Couleurs
/// seuil : vert / jaune / rouge.
/// </summary>
public partial class PerfOverlay : Label
{
    private double _acc;

    public void Build()
    {
        Name = "PerfOverlay";
        MouseFilter = MouseFilterEnum.Ignore;
        HorizontalAlignment = HorizontalAlignment.Right;
        HudRoot.Pin(this, 1, 0, 1, 0, -420, 4, -276, 24);
        EeSkin.Shadowed(this, 12);
        Visible = HudPrefs.ShowPerf;
        HudPrefs.Changed += OnPrefs;
    }

    public override void _ExitTree()
    {
        HudPrefs.Changed -= OnPrefs;
        base._ExitTree();
    }

    private void OnPrefs()
    {
        Visible = HudPrefs.ShowPerf;
        if (Visible) Refresh();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _acc += delta;
        if (_acc < 0.5) return;
        _acc = 0;
        Refresh();
    }

    private void Refresh()
    {
        var fps = (int)Engine.GetFramesPerSecond();
        var ping = EdenSession.PingMs;
        Text = ping < 0 ? $"{fps} FPS  --ms" : $"{fps} FPS  {ping:0}ms";
        var worst = Math.Max(
            fps >= 50 ? 0 : fps >= 30 ? 1 : 2,
            ping < 0 ? 0 : ping < 80 ? 0 : ping < 160 ? 1 : 2);
        AddThemeColorOverride("font_color", worst switch
        {
            0 => new Color(0.45f, 1f, 0.45f),
            1 => new Color(1f, 0.9f, 0.3f),
            _ => new Color(1f, 0.4f, 0.35f),
        });
    }
}
