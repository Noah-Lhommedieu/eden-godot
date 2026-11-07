using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>Dialogue générique pour les paquets « ouvrir un
/// service » S→C jusque-là sans fenêtre dédiée : magicbox (612/613),
/// instance (564/575), loterie (448/511/436-438), battlefield
/// (462-466), room/hôtel (520+). Affiche titre + champs reçus et
/// notifie le chat — la donnée brute reste dans
/// <see cref="MiscPackets.Latest"/> pour un futur HUD dédié.</summary>
public partial class FeatureDialogWindow : Control
{
    private Panel? _panel;
    private Label? _title, _body;

    public override void _Ready()
    {
        // Dialogue passe-plat : pas de layout dédié — chrome natif
        // (EnsureTitleBar → ApplyNativeChrome) sur un panneau plat.
        _panel = new Panel { Name = "__featdlg" };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _panel.Position = new Vector2(-190, -110);
        _panel.Size = new Vector2(380, 220);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.09f, 0.96f),
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, "");
        DragHelper.Persist(_panel, "featuredlg");

        _title = new Label { Text = UiTextEn.Translate("Service") };
        _title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.OffsetTop = 30;
        _title.AddThemeFontSizeOverride("font_size", 15);
        _title.AddThemeColorOverride("font_color",
            new Color(0.75f, 0.85f, 0.95f));
        _panel.AddChild(_title);

        _body = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _body.Position = new Vector2(16, 56);
        _body.Size = new Vector2(348, 120);
        _panel.AddChild(_body);

        var close = new Button { Text = UiTextEn.Translate("Close") };
        close.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        close.Position = new Vector2(-45, -36);
        close.Size = new Vector2(90, 26);
        close.Pressed += () => Visible = false;
        _panel.AddChild(close);

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    /// <summary>Ouvre la fenêtre sur les paquets « dialog » et
    /// journalise les résultats associés au chat.</summary>
    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            // MagicBox (612/613), loterie (448/511/595/596),
            // instance/recrutement (RecruitWindow) et hôtel
            // (HotelWindow — 539) ont des fenêtres dédiées.
            // 0xe (tour du ciel → TowerWindow) et 0x12 (épreuve →
            // EvaluateWindow) ont leurs fenêtres natives dédiées.
            case ZoneClientOpenAreaDialog a
                when a.Field1 != 0xe && a.Field1 != 0x12:
                Show(UiTextEn.Translate("Service local"),
                    string.Format(UiTextEn.Translate("Area dialog {0}/{1}."), a.Field1, a.Field2));
                break;
            case ZoneClientOpenChurchDialog:
                Show(UiTextEn.Translate("Church"), UiTextEn.Translate("Ceremony available here."));
                break;
            // PaySystem (602) a sa fenêtre dédiée
            // (PaySystemWindow — lot D5) ; le résultat 604 + les
            // textes 316 restent affichés par le chat.
            case ZoneClientPayEvent p:
                Show(UiTextEn.Translate("Shop"), string.Format(UiTextEn.Translate("Shop event {0}/{1}."), p.Field1, p.Field2));
                break;
            case ZoneClientOpenMissionShop s:
                Show(UiTextEn.Translate("Mission shop"), string.Format(UiTextEn.Translate("Shop {0}/{1}."), s.Field1, s.Field2));
                break;
            // Strengthen (432-437) a sa fenêtre dédiée
            // (StrengthenWindow — anclume native, lot B4).
        }
    }

    private void Show(string title, string body)
    {
        if (_title != null) _title.Text = title;
        if (_body != null) _body.Text = body;
        Visible = true;
    }

}
