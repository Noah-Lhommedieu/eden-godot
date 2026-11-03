using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Animation « Switched Class to X » (ref ClassPanel-ChangementDeClasse /
/// shot_0056_000116) : deux gardiens ailés sombres à halo violet et
/// fissures orangées qui battent des ailes de part et d'autre du texte
/// central. Le modèle 3D natif (effet NIF) n'est pas rejoué : silhouette
/// dessinée en code (polygones d'ailes à plumes animées), même
/// composition/cadrage que la ref.
/// </summary>
public partial class ClassSwitchFx : Control
{
    private double _t;
    private readonly double _life;
    private readonly bool _mirror;

    private ClassSwitchFx(bool mirror, double life)
    {
        _mirror = mirror; _life = life;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(220, 170);
        Size = new Vector2(220, 170);
        PivotOffset = Size / 2;
    }

    /// <summary>Joue l'animation sous <paramref name="host"/> : deux
    /// gardiens de part et d'autre de la ligne de texte (y centré ≈ 40 %
    /// de la hauteur écran).</summary>
    public static void Play(Node host, string text = "", double seconds = 3.6)
    {
        var vp = host.GetViewport().GetVisibleRect().Size;
        var y = vp.Y * 0.40f - 85;
        var cx = vp.X / 2;
        var l = new ClassSwitchFx(false, seconds) { Name = "SwitchWingL" };
        var r = new ClassSwitchFx(true, seconds) { Name = "SwitchWingR" };
        l.Position = new Vector2(cx - 330 - 110, y);
        r.Position = new Vector2(cx + 330 - 110, y);
        if (text.Length > 0)
        {
            // Grand texte blanc contouré, par-dessus les fenêtres (ref :
            // « Switched Class to Thief » sur le panneau Classes).
            var lb = new Label
            {
                Text = text, Name = "SwitchText",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            lb.AddThemeFontSizeOverride("font_size", 38);
            lb.AddThemeColorOverride("font_color", Colors.White);
            lb.AddThemeColorOverride("font_outline_color",
                new Color(0.1f, 0.05f, 0.15f, 0.9f));
            lb.AddThemeConstantOverride("outline_size", 6);
            lb.Position = new Vector2(cx - 400, vp.Y * 0.40f - 26);
            lb.Size = new Vector2(800, 52);
            host.AddChild(lb);
            var tw = lb.CreateTween();
            tw.TweenInterval(seconds - 0.8);
            tw.TweenProperty(lb, "modulate:a", 0f, 0.8);
            tw.TweenCallback(Callable.From(lb.QueueFree));
        }
        host.AddChild(l);
        host.AddChild(r);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t >= _life) { QueueFree(); return; }
        var fade = _t < 0.3 ? _t / 0.3 : _t > _life - 0.8 ? (_life - _t) / 0.8 : 1;
        Modulate = new Color(1, 1, 1, (float)fade);
        // Léger vol stationnaire.
        Position = new Vector2(Position.X,
            Position.Y + (float)Math.Sin(_t * 3) * 0.25f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = new Vector2(110, 90);
        var flap = (float)Math.Sin(_t * 9);           // -1..1
        var sgn = _mirror ? -1f : 1f;
        var dark = new Color(0.09f, 0.06f, 0.11f);
        var edge = new Color(0.35f, 0.15f, 0.4f);
        var glow = new Color(1f, 0.5f, 0.12f, 0.9f);
        // Halo violet.
        DrawArc(c + new Vector2(0, -52), 20, 0, Mathf.Tau, 28,
            new Color(0.75f, 0.45f, 0.95f, 0.85f), 3);
        // Ailes : 2 côtés × 6 plumes en éventail, battement ±.
        foreach (var side in new[] { -1f, 1f })
        {
            var shoulder = c + new Vector2(side * 14, -20);
            for (var i = 0; i < 6; i++)
            {
                var ang = Mathf.DegToRad(-70 + i * 18 + flap * 26);
                var len = 92 - i * 7;
                var dir = new Vector2(side * Mathf.Cos(ang), Mathf.Sin(ang));
                var nrm = new Vector2(-dir.Y, dir.X) * side;
                var tip = shoulder + dir * len;
                var pts = new[]
                {
                    shoulder + nrm * 7,
                    shoulder + dir * len * 0.55f + nrm * 13,
                    tip,
                    shoulder + dir * len * 0.5f - nrm * 5,
                    shoulder - nrm * 6,
                };
                DrawColoredPolygon(pts, dark);
                DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[4] },
                    edge, 1.5f);
                DrawLine(shoulder + dir * len * 0.2f, tip - dir * 8,
                    new Color(glow.R, glow.G, glow.B, 0.55f), 1.2f);
            }
        }
        // Corps + tête + bec + yeux.
        DrawColoredPolygon(new[]
        {
            c + new Vector2(0, -34), c + new Vector2(18, -10),
            c + new Vector2(10, 38), c + new Vector2(0, 52),
            c + new Vector2(-10, 38), c + new Vector2(-18, -10),
        }, dark);
        DrawCircle(c + new Vector2(0, -40), 14, dark);
        DrawColoredPolygon(new[]
        {
            c + new Vector2(sgn * 8, -40), c + new Vector2(sgn * 24, -34),
            c + new Vector2(sgn * 8, -32),
        }, new Color(0.55f, 0.3f, 0.1f));
        DrawCircle(c + new Vector2(sgn * 4, -43), 2.5f, glow);
        DrawLine(c + new Vector2(0, -12), c + new Vector2(6, 22), glow, 1.5f);
        DrawLine(c + new Vector2(-6, 0), c + new Vector2(2, 30), glow, 1.2f);
    }
}
