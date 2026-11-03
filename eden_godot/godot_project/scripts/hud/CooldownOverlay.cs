using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Overlay de cooldown d'un slot (inventaire, barre de raccourcis) :
/// balayage sombre proportionnel au temps restant + secondes, alimenté
/// par <see cref="CooldownTracker"/> (NCID 368). <see cref="Shake"/>
/// fait vibrer le slot hôte à l'usage.
/// </summary>
public partial class CooldownOverlay : Control
{
    /// <summary>QA : <c>--cd-frac=&lt;0..1&gt;</c> fige le balayage de
    /// tous les overlays à la fraction donnée (captures 25/50/75 %).</summary>
    private static readonly double? ForceFrac = ParseFracArg();

    private static double? ParseFracArg()
    {
        const string p = "--cd-frac=";
        var args = OS.GetCmdlineUserArgs();
        if (args.Length == 0) args = OS.GetCmdlineArgs();
        foreach (var a in args)
            if (a.StartsWith(p) && double.TryParse(a[p.Length..],
                    CultureInfo.InvariantCulture, out var f))
                return Math.Clamp(f, 0, 1);
        return null;
    }

    /// <summary>Source du cooldown : (restant ms, total ms) ou null si libre.</summary>
    public Func<(long Remain, long Total)?>? Source { get; set; }

    private readonly Label _label = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        MouseFilter = MouseFilterEnum.Ignore,
    };
    private double _frac;

    /// <summary>Attache un overlay plein cadre à <paramref name="host"/>.</summary>
    public static CooldownOverlay Attach(Control host)
    {
        var o = new CooldownOverlay { MouseFilter = MouseFilterEnum.Ignore };
        o.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        host.AddChild(o);
        return o;
    }

    public override void _Ready()
    {
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _label.AddThemeFontSizeOverride("font_size", 12);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (ForceFrac is { } ff)
        {
            Visible = true;
            _frac = ff;
            _label.Text = "";
            QueueRedraw();
            return;
        }
        var c = Source?.Invoke();
        if (c is not { Remain: > 0 } v)
        {
            if (Visible) Visible = false;
            return;
        }
        Visible = true;
        _frac = v.Total > 0 ? Math.Clamp((double)v.Remain / v.Total, 0, 1) : 1;
        _label.Text = v.Remain >= 10000
            ? $"{v.Remain / 60000 + 1}m"
            : $"{v.Remain / 1000.0:0.#}";
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Secteur sombre qui se vide dans le sens horaire (départ en
        // haut). Le front glisse le long du périmètre à débit d'aire
        // constant : la zone couverte vaut _frac × aire du slot à tout
        // instant — le balayage à angle uniforme d'avant « engloutissait
        // » chaque coin (4 accélérations par tour = « vagues » ressenties).
        var w = Size.X;
        var h = Size.Y;
        if (w <= 0 || h <= 0) return;
        var cx = w / 2;
        // Aire couverte visée et aires cumulées au franchissement de
        // chaque coin (éventail depuis le centre : chaque segment de
        // périmètre balayé ajoute longueur × apothème / 2).
        var target = (float)_frac * w * h;
        var a1 = w * h / 8f;                        // coin haut-droit
        var a2 = 3 * w * h / 8f;                    // coin bas-droit
        var a3 = 5 * w * h / 8f;                    // coin bas-gauche
        var a4 = 7 * w * h / 8f;                    // coin haut-gauche
        var pts = new List<Vector2>
        {
            new(cx, h / 2),                         // centre (éventail)
            new(cx, 0f),                            // départ : milieu du haut
        };
        Vector2 front;
        if (target <= a1)
        {
            front = new Vector2(cx + target * 4f / h, 0);
        }
        else if (target <= a2)
        {
            pts.Add(new Vector2(w, 0));
            front = new Vector2(w, (target - a1) * 4f / w);
        }
        else if (target <= a3)
        {
            pts.Add(new Vector2(w, 0));
            pts.Add(new Vector2(w, h));
            front = new Vector2(w - (target - a2) * 4f / h, h);
        }
        else if (target <= a4)
        {
            pts.Add(new Vector2(w, 0));
            pts.Add(new Vector2(w, h));
            pts.Add(new Vector2(0, h));
            front = new Vector2(0, h - (target - a3) * 4f / w);
        }
        else
        {
            pts.Add(new Vector2(w, 0));
            pts.Add(new Vector2(w, h));
            pts.Add(new Vector2(0, h));
            pts.Add(new Vector2(0, 0));
            front = new Vector2((target - a4) * 4f / h, 0);
        }
        pts.Add(front);
        DrawColoredPolygon(pts.ToArray(), new Color(0f, 0f, 0f, 0.62f));
    }

    /// <summary>Contrôles « contenu » (icône, quantité) qui vibrent ;
    /// null = l'icône de l'hôte TextureRect (copie temporaire).</summary>
    public Control[]? ShakeTargets { get; set; }

    private Tween? _shake;
    private Vector2[]? _rest;
    private TextureRect? _ghost;

    /// <summary>Vibration légère du CONTENU du slot en diagonale
    /// haut-droite ; le slot lui-même ne bouge pas.</summary>
    public void Shake()
    {
        if (GetParent() is not Control host) return;
        if (_ghost != null && GodotObject.IsInstanceValid(_ghost))
        {
            _ghost.QueueFree();
            host.SelfModulate = Colors.White;
        }
        _ghost = null;
        var targets = ShakeTargets;
        TextureRect? ghost = null;
        if (targets == null)
        {
            if (host is not TextureRect { Texture: { } tex } tr) return;
            ghost = new TextureRect
            {
                Texture = tex, ExpandMode = tr.ExpandMode,
                StretchMode = tr.StretchMode,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            ghost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            host.AddChild(ghost);
            host.MoveChild(ghost, GetIndex());
            host.SelfModulate = new Color(1, 1, 1, 0);
            targets = new Control[] { ghost };
            _ghost = ghost;
        }
        if (_shake?.IsRunning() == true && _rest != null)
            for (var i = 0; i < targets.Length && i < _rest.Length; i++)
                targets[i].Position = _rest[i];
        _shake?.Kill();
        var rest = new Vector2[targets.Length];
        for (var i = 0; i < targets.Length; i++) rest[i] = targets[i].Position;
        _rest = ghost == null ? rest : null;
        var t = _shake = host.CreateTween();
        var amps = new[] { 2.5f, 0f, 1.5f, 0f, 0.8f, 0f };
        foreach (var a in amps)
        {
            var d = new Vector2(a, -a);
            t.TweenCallback(Callable.From(() =>
            {
                for (var i = 0; i < targets.Length; i++)
                    targets[i].Position = rest[i] + d;
            }));
            t.TweenInterval(0.035);
        }
        t.TweenCallback(Callable.From(() =>
        {
            if (ghost != null)
            {
                if (GodotObject.IsInstanceValid(ghost)) ghost.QueueFree();
                if (GodotObject.IsInstanceValid(host))
                    host.SelfModulate = Colors.White;
            }
            else
                for (var i = 0; i < targets.Length; i++)
                    targets[i].Position = rest[i];
        }));
    }
}
