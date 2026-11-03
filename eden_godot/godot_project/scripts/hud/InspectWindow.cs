using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>Inspection d'équipement — réponses 460
/// <see cref="ZoneClientLookEquip"/> (joueur ciblé) et 571
/// <see cref="ZoneClientPVPRankLookEquip"/> (classement PvP).
/// Liste les itemUid reçus (noms via ItemTable quand dispo).</summary>
public partial class InspectWindow : Control
{
    private Panel? _panel;
    private ItemList? _list;
    private Label? _title;

    public override void _Ready()
    {
        _panel = new Panel { Name = "__inspect" };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _panel.Position = new Vector2(-160, -140);
        _panel.Size = new Vector2(320, 280);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.97f),
            BorderColor = new Color(0.5f, 0.55f, 0.45f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnableBackgroundDrag(_panel);
        DragHelper.Persist(_panel, "inspect");

        _title = new Label { Text = UiTextEn.Translate("Inspection") };
        _title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.OffsetTop = 10;
        _title.AddThemeFontSizeOverride("font_size", 15);
        _panel.AddChild(_title);

        _list = new ItemList();
        _list.Position = new Vector2(12, 40);
        _list.Size = new Vector2(296, 190);
        _panel.AddChild(_list);

        var close = new Button { Text = UiTextEn.Translate("Close") };
        close.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        close.Position = new Vector2(-45, -34);
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

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientLookEquip e:
                Fill($"{e.CharId}'s equipment", e.ItemUids);
                break;
            case ZoneClientPVPRankLookEquip p:
                Fill($"PvP equipment — {p.PlayerName}", p.ItemUids);
                break;
        }
    }

    private void Fill(string title,
        System.Collections.Generic.List<ushort> uids)
    {
        if (_title != null) _title.Text = title;
        _list?.Clear();
        foreach (var uid in uids)
            _list?.AddItem(UiTextEn.Translate($"Item #{uid}"));
        Visible = true;
    }
}
