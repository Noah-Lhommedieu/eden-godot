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
    private Control? _storage;
    private ItemList? _list;
    private Label? _info;
    private readonly List<int> _roomIds = new();
    private readonly List<(int Id, string Text)> _rooms = new();
    private LineEdit? _searchBox;

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
        // Layout natif : la liste prend le rect de MemberList
        // (0.018..0.606 x 0.127..0.855), le statut la bande basse.
        var member = built != null
            ? TopLeftVitals.Find<Control>(_panel, "MemberList") : null;
        if (member != null)
        {
            _list.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            member.AddChild(_list);
        }
        else
        {
            _list.Position = new Vector2(14, 42);
            _list.Size = new Vector2(sz.X - 28, sz.Y - 140);
            _panel.AddChild(_list);
        }

        _info = new Label { Text = "" };
        var bg = built != null
            ? TopLeftVitals.Find<Control>(_panel, "Background") : null;
        if (bg != null)
        {
            _info.AnchorLeft = 0.018f; _info.AnchorRight = 0.606f;
            _info.AnchorTop = 0.865f; _info.AnchorBottom = 0.99f;
            _info.OffsetLeft = _info.OffsetRight = 0;
            _info.OffsetTop = _info.OffsetBottom = 0;
            bg.AddChild(_info);
        }
        else
        {
            _info.Position = new Vector2(14, sz.Y - 92);
            _info.Size = new Vector2(sz.X - 28, 40);
            _panel.AddChild(_info);
        }

        // Recherche de résident : aucun paquet de recherche câblé côté
        // client → filtre local sur la liste reçue (537).
        if (built != null)
        {
            _searchBox = TopLeftVitals.Find<LineEdit>(_panel, "SearchBox");
            if (_searchBox != null)
                _searchBox.TextSubmitted += _ => RebuildList();
            if (TopLeftVitals.Find<BaseButton>(_panel, "SearchBtn")
                    is { } sb)
                sb.Pressed += RebuildList;
        }

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
        _rooms.Clear();
        _rooms.Add((7, UiTextEn.Translate("Sunny Room — 3/8")));
        _rooms.Add((12, UiTextEn.Translate("Guild Hall — 12/20")));
        RebuildList();
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
            case ZoneClientOpenHotel o:
                // hotelType 0 = "room_storage" : fenêtre native
                // hotelstoragewnd (TabControl+listbox des items de
                // chambre — contenu non implémenté serveur, vide).
                if (o.Field1 == 0) OpenStorage(); else Visible = true;
                _list?.Clear(); _roomIds.Clear(); _rooms.Clear();
                break;
            case ZoneClientSearchRoomResult r:
                Visible = true;
                foreach (var e in r.Entries)
                {
                    _rooms.Add((e.RoomId,
                        $"{e.RoomName} — {e.MemberCount}/{e.MaxMembers}"));
                }
                RebuildList();
                break;
            case ZoneClientRoomData d:
                if (_info != null) _info.Text =
                    $"Chambre {d.Field1} — {d.Field2}…";
                break;
        }
    }

    private void RebuildList()
    {
        _list?.Clear(); _roomIds.Clear();
        var q = _searchBox?.Text.Trim() ?? "";
        foreach (var (id, text) in _rooms)
        {
            if (q.Length > 0 && !text.Contains(q,
                    System.StringComparison.OrdinalIgnoreCase)) continue;
            _roomIds.Add(id);
            _list?.AddItem(text);
        }
    }

    /// <summary>Ouvre le panneau de stockage de chambre natif
    /// (<c>hotelstoragewnd.layout</c> — Root/HotelStorage). Monté
    /// paresseusement ; pas de contenu serveur pour l'instant.</summary>
    private void OpenStorage()
    {
        if (_storage == null)
        {
            _storage = HudExtensions.FindHud()
                ?.Build("hotelstoragewnd.layout");
            if (_storage != null)
            {
                AddChild(_storage);
                HudExtensions.CenterNative(_storage,
                    new Vector2(420, 380));
                DragHelper.EnsureTitleBar(_storage,
                    UiTextEn.Translate("Room storage"));
                DragHelper.Persist(_storage, "hotelstorage");
            }
        }
        if (_storage != null) _storage.Visible = true;
        else Visible = true; // layout absent → hall en repli
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
