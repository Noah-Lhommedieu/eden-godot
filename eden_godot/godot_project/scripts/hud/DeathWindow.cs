using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre de mort (lot S1) — « Vous êtes mort » + bouton
/// Réapparaître. Ouverte par l'event <see cref="PlayerVitals.Died"/>
/// (305 ClientDie du self) et refermée par
/// <see cref="PlayerVitals.Revived"/> (306 ClientRevive).
///
/// Le bouton envoie NCID 20 <see cref="ZoneReviveRequest"/>
/// <c>[i32 type=0]</c> (point de résurrection — même octet que le
/// helper <c>GameScene.SendReviveAsync</c>). L'aire liée du 531
/// (<see cref="ZoneClientReviveInfo"/>, GOLDEN) est affichée si connue.
/// </summary>
public partial class DeathWindow : Control
{
    private Control? _panel;
    private Label? _area;

    public override void _Ready()
    {
        // Layout natif ReviveMenuUI : NearbyBtn = retour au point
        // de rez (type 0), DirectBtn = sur place (type 1, cash),
        // ItemmallBtn = objet Phoenix (type 5), RefuseBtn = ferme.
        _panel = HudExtensions.BuildNativeWindow(this,
            "ReviveMenuUI.layout", "death", new Vector2(320, 200));
        if (_panel != null)
        {
            _panel.Name = "__death";
            _area = new Label { Text = UiTextEn.Translate("You died") };
            _area.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            _area.HorizontalAlignment = HorizontalAlignment.Center;
            _area.OffsetTop = 40;
            _area.AddThemeFontSizeOverride("font_size", 12);
            _area.AddThemeColorOverride("font_color",
                new Color(0.95f, 0.5f, 0.45f));
            _panel.AddChild(_area);

            Wire("NearbyBtn", 0);
            Wire("DirectBtn", 1);
            Wire("ItemmallBtn", 5);
            if (TopLeftVitals.Find<BaseButton>(_panel, "RefuseBtn")
                    is { } refuse)
                refuse.Pressed += () => Visible = false;
            if (TopLeftVitals.Find<BaseButton>(_panel, "AcceptBtn")
                    is { } accept)
                accept.Pressed += () => SendRevive(0);
            if (TopLeftVitals.Find<BaseButton>(_panel, "HelpBtn")
                    is { } help)
                help.Visible = false;
        }
        else
        {
        var p = new Panel { Name = "__death" };
        _panel = p;
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-150, -80);
        p.Size = new Vector2(300, 160);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.04f, 0.05f, 0.95f),
            BorderColor = new Color(0.6f, 0.3f, 0.3f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        });
        AddChild(p);
        DragHelper.EnableBackgroundDrag(p);
        DragHelper.Persist(p, "death");

        var title = new Label { Text = UiTextEn.Translate("You died") };
        title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.OffsetTop = 18;
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color",
            new Color(0.95f, 0.5f, 0.45f));
        p.AddChild(title);

        _area = new Label { Text = "" };
        _area.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _area.HorizontalAlignment = HorizontalAlignment.Center;
        _area.OffsetTop = 52;
        _area.AddThemeFontSizeOverride("font_size", 10);
        _area.AddThemeColorOverride("font_color",
            new Color(0.8f, 0.8f, 0.85f));
        p.AddChild(_area);

        var revive = new Button { Text = UiTextEn.Translate("Respawn") };
        revive.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        revive.Position = new Vector2(-70, -46);
        revive.Size = new Vector2(140, 34);
        revive.Pressed += OnRevive;
        p.AddChild(revive);
        }

        Visible = false;
        PlayerVitals.Died += OnDied;
        PlayerVitals.Revived += OnRevived;
    }

    private void Wire(string node, int reviveType)
    {
        if (TopLeftVitals.Find<BaseButton>(_panel!, node)
                is { } b)
            b.Pressed += () => SendRevive(reviveType);
    }

    private void SendRevive(int reviveType)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(new ZoneReviveRequest
                { ReviveType = reviveType },
                CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[death] revive : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            Visible = false;
        }
    }

    public override void _ExitTree()
    {
        PlayerVitals.Died -= OnDied;
        PlayerVitals.Revived -= OnRevived;
        base._ExitTree();
    }

    /// <summary>État factice pour capture (--hud-open hors
    /// session) — le libellé natif « …casts Revive on you »
    /// suffit, pas de texte additionnel.</summary>
    public void EnsureDemo() => Visible = true;

    /// <summary>305 self — montre la fenêtre (thread principal).</summary>
    private void OnDied(uint killer)
    {
        if (_area != null) _area.Text = "";
        Visible = true;
    }

    /// <summary>306 self — referme.</summary>
    private void OnRevived() => Visible = false;

    /// <summary>Bouton repli → NCID 20 Revive [i32 0].</summary>
    private void OnRevive() => SendRevive(0);
}
