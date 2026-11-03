using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>Dialogue de duel 1v1 — 451 <see cref="ZoneClientDuelRequest"/>
/// affiche « X vous défie » + Accepter/Refuser (C→S 136
/// <see cref="ZoneDuelRequestAnswer"/>) ; 453 début → bannière ;
/// 454 résultat + 452 réponse → chat. Déplaçable (DragHelper).</summary>
public partial class DuelDialog : Control
{
    private Control? _panel;
    private Label? _info;
    private int _requesterId;
    /// <summary>--duel-test=accept : répond 136 (accept) à la première
    /// 451 reçue, sans interaction.</summary>
    public static bool AutoAccept;

    public override void _Ready()
    {
        // Layout natif DuelRequestWnd (Msg + OKBtn/CloseBtn) ;
        // repli panneau plat si uidata absent.
        var built = HudExtensions.BuildNativeWindow(this,
            "DuelRequestWnd.layout", "duel", new Vector2(320, 140));
        if (built != null)
        {
            _panel = built;
            built.Name = "__duel";
            _info = TopLeftVitals.Find<Label>(built, "Msg") ??
                _info;
            // TextColours du layout = noir opaque (fond clair en
            // natif) — illisible sur le thème sombre ; centré
            // vertical pour rester sur la zone sombre du cadre.
            _info?.AddThemeColorOverride("font_color",
                new Color(0.95f, 0.92f, 0.85f));
            if (_info != null)
                _info.VerticalAlignment = VerticalAlignment.Center;
            if (TopLeftVitals.Find<BaseButton>(built, "OKBtn")
                    is { } ok)
                ok.Pressed += () => Answer(1);
            if (TopLeftVitals.Find<BaseButton>(built, "CloseBtn")
                    is { } no)
                no.Pressed += () => Answer(0);
            if (_info == null)
            {
                // Le StaticText natif n'a pas survécu au mapping —
                // label propre sous la barre de titre.
                _info = new Label
                {
                    Text = "",
                    AutowrapMode = TextServer.AutowrapMode.Word,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _info.SetAnchorsAndOffsetsPreset(
                    LayoutPreset.FullRect);
                _info.OffsetTop = 30;
                _info.OffsetBottom = -50;
                built.AddChild(_info);
            }
            Visible = false;
            MiscPackets.Received += OnPacket;
            return;
        }
        var p = new Panel { Name = "__duel" };
        _panel = p;
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-160, -70);
        p.Size = new Vector2(320, 140);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.05f, 0.08f, 0.96f),
            BorderColor = new Color(0.7f, 0.55f, 0.2f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, "Duel");
        DragHelper.Persist(_panel, "duel");

        _info = new Label { Text = "" };
        _info.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _info.HorizontalAlignment = HorizontalAlignment.Center;
        _info.OffsetTop = 46;
        _info.AutowrapMode = TextServer.AutowrapMode.Word;
        _panel.AddChild(_info);

        var accept = new Button { Text = UiTextEn.Translate("Accept") };
        accept.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        accept.Position = new Vector2(-150, -42);
        accept.Size = new Vector2(140, 30);
        accept.Pressed += () => Answer(1);
        _panel.AddChild(accept);

        var refuse = new Button { Text = UiTextEn.Translate("Decline") };
        refuse.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        refuse.Position = new Vector2(10, -42);
        refuse.Size = new Vector2(140, 30);
        refuse.Pressed += () => Answer(0);
        _panel.AddChild(refuse);

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    /// <summary>Contenu factice pour capture (--hud-open hors
    /// session).</summary>
    public void EnsureDemo()
    {
        _requesterId = 4242;
        if (_info != null)
            _info.Text = UiTextEn.Translate("Hero challenges you to a duel!");
        Visible = true;
    }

    /// <summary>451 demande, 452 réponse, 453 début, 454 fin —
    /// tout sur le thread principal (event <see cref="MiscPackets.Received"/>).</summary>
    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientDuelRequest r:
                _requesterId = r.Field1;
                if (_info != null) _info.Text =
                    UiTextEn.Translate($"{r.Field2} challenges you to a duel!");
                if (HudPrefs.NoDuel) { Answer(0); break; }
                if (AutoAccept) { Answer(1); break; }
                Visible = true;
                break;
            case ZoneClientDuelRequestResult rr:
                (GetParent() as HudRoot)?.AddChat(7, "",
                    $"Duel : {rr.Field1} {rr.Field2} ({rr.Field3})");
                break;
            case ZoneClientDuelBegin b:
                (GetParent() as HudRoot)?.ShowScreenMessage("Le duel commence !");
                Visible = false;
                break;
            case ZoneClientDuelResult z:
                (GetParent() as HudRoot)?.AddChat(7, "",
                    $"Fin du duel : {z.Field2} vs {z.Field3}");
                Visible = false;
                break;
        }
    }

    /// <summary>Bouton → C→S 136 [s16 accept][i32 demandeur].</summary>
    private void Answer(short accept)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(new ZoneDuelRequestAnswer
            {
                Accept = accept, RequesterId = _requesterId,
            }, System.Threading.CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[duel] answer : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
        else GD.PrintErr("[duel] answer ignorée — session zone absente");
        Visible = false;
    }
}
