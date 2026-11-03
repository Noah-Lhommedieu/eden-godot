using Godot;

namespace EdenClient.Scene;

/// <summary>
/// Overlay « mode cinéma » (plan futur-feature/02, étape 2) : deux
/// bandes noires (letterbox) animées par <see cref="CinemaTimeline"/>
/// et un bouton « Passer » (texture native <c>cutScenePlay</c> non
/// utilisée : seul asset UI cutscene, apparence non prouvée — bouton
/// texte FR/EN). Verrouille l'entrée joueur via
/// <see cref="CinemaTimeline.Active"/> (lu par PlayerController).
/// Pas de déclencheur serveur : aucun pont natif prouvé (NCID 499
/// inconnu) — déclenché localement (<c>--cinema-test</c>).
/// </summary>
public partial class CinemaMode : CanvasLayer
{
    private ColorRect? _top, _bottom;
    private Button? _skip;
    private const float BarRatio = 0.12f;

    public override void _Ready()
    {
        Layer = 90;
        _top = new ColorRect { Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        _bottom = new ColorRect { Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_top);
        AddChild(_bottom);
        _skip = new Button
        {
            Text = EdenClient.Data.UiTextEn.Translate("Skip"),
            Visible = false,
        };
        _skip.Pressed += () => CinemaTimeline.Shared.Skip();
        AddChild(_skip);
        Layout();
    }

    /// <summary>Démarre une scène de <paramref name="seconds"/> s.</summary>
    public void Begin(double seconds) =>
        CinemaTimeline.Shared.Begin(seconds);

    private void Layout()
    {
        if (_top == null || _bottom == null || _skip == null) return;
        var vp = GetViewport().GetVisibleRect().Size;
        var f = (float)CinemaTimeline.Shared.BarFraction;
        var bar = vp.Y * BarRatio * f;
        _top.Position = Vector2.Zero;
        _top.Size = new Vector2(vp.X, bar);
        _bottom.Position = new Vector2(0f, vp.Y - bar);
        _bottom.Size = new Vector2(vp.X, bar);
        _skip.Visible = CinemaTimeline.Shared.Active && f > 0.5f;
        _skip.Position = new Vector2(vp.X - _skip.Size.X - 24f,
            vp.Y - bar - _skip.Size.Y - 8f);
    }

    public override void _Process(double delta)
    {
        CinemaTimeline.Shared.Tick(delta);
        Layout();
    }
}
