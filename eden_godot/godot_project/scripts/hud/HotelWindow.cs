using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using System.Collections.Generic;

namespace EdenClient.Hud;

/// <summary>Hôtel / chambres joueurs — 539 ouvre le hall, 537
/// <see cref="ZoneClientSearchRoomResult"/> alimente la liste,
/// 522/525 données de chambre. « Entrer » envoie C→S 207
/// <see cref="ZoneServerEnterRoom"/> [i32 roomId][s16 type].</summary>
public partial class HotelWindow : Control
{
    private Control? _panel;
    private ItemList? _list;
    private Label? _info;
    private readonly List<int> _roomIds = new();

    public override void _Ready()
    {
        // Layout natif HotelWnd (MemberList + recherche) ; repli
        // panneau plat sinon. La liste de recherche perso prend la
        // place de la MemberList native (même rôle).
        var built = HudExtensions.BuildNativeWindow(this,
            "HotelWnd.layout", "hotel", new Vector2(460, 300));
        var sz = built?.CustomMinimumSize ?? new Vector2(460, 300);
        if (built != null)
        {
            _panel = built;
            _panel.Name = "__hotel";
            // Boutons natifs : EnterBtn entre dans la salle (même
            // wire que l'ancien bouton) ; les autres fonctions
            // (loyer, visite, privilèges) n'ont pas de paquet câblé.
            if (TopLeftVitals.Find<BaseButton>(_panel, "EnterBtn")
                    is { } enterBtn)
                enterBtn.Pressed += OnEnter;
        }
        else
        {
        var p = new Panel { Name = "__hotel" };
        _panel = p;
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-230, -150);
        p.Size = new Vector2(460, 300);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.06f, 0.08f, 0.97f),
            BorderColor = new Color(0.555f, 0.533f, 0.475f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Hotel — rooms");
        DragHelper.Persist(p, "hotel");
        }

        _list = new ItemList();
        _list.Position = new Vector2(14, 42);
        _list.Size = new Vector2(sz.X - 28, sz.Y - 140);
        _panel.AddChild(_list);

        _info = new Label { Text = "" };
        _info.Position = new Vector2(14, sz.Y - 92);
        _info.Size = new Vector2(sz.X - 28, 40);
        _panel.AddChild(_info);

        if (built == null)
        {
            var enter = new Button { Text = UiTextEn.Translate("Enter") };
            enter.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
            enter.Position = new Vector2(-150, -36);
            enter.Size = new Vector2(120, 28);
            enter.Pressed += OnEnter;
            _panel.AddChild(enter);
        }

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    /// <summary>Contenu factice pour capture (--hud-open hors
    /// session).</summary>
    public void EnsureDemo()
    {
        _roomIds.Clear(); _roomIds.AddRange(new[] { 7, 12 });
        _list?.Clear();
        _list?.AddItem(UiTextEn.Translate("Sunny Room — 3/8"));
        _list?.AddItem(UiTextEn.Translate("Guild Hall — 12/20"));
        if (_info != null)
            _info.Text = UiTextEn.Translate(
                "Pick a room and press Enter.");
        Visible = true;
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
            case ZoneClientOpenHotel:
                Visible = true;
                _list?.Clear(); _roomIds.Clear();
                break;
            case ZoneClientSearchRoomResult r:
                Visible = true;
                foreach (var e in r.Entries)
                {
                    _roomIds.Add(e.RoomId);
                    _list?.AddItem(
                        $"{e.RoomName} — {e.MemberCount}/{e.MaxMembers}");
                }
                break;
            case ZoneClientRoomData d:
                if (_info != null) _info.Text =
                    $"Chambre {d.Field1} — {d.Field2}…";
                break;
        }
    }

    private void OnEnter()
    {
        var sel = _list?.GetSelectedItems();
        if (sel == null || sel.Length == 0 || sel[0] >= _roomIds.Count)
            return;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        var task = s.SendZoneAsync(new ZoneServerEnterRoom
        {
            Field1 = _roomIds[sel[0]], Field2 = 0,
        }, System.Threading.CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[hotel] enter : {t.Exception}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }
}
