using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// A6.6 — pages costume / ensembles d'armure : conteneurs d'équipement
/// 2, 3, 4 (<c>kind % 3</c> → page : cont2→0, cont3→2, cont4→1), locs
/// 1-5 (tête/torse/jambes/mains/pieds). Ouverte par le bouton « Outfits »
/// du sac. Équiper = drag d'un item du sac sur le slot → NCID 7
/// <c>(0,slot,sac)→(1,loc,page)</c> ; déséquiper = clic droit / double-clic
/// → NCID 7 <c>(1,loc,page)→(0,-1,-1)</c>. L'état local vient du 311
/// (entrée), du 277 (ack, appliqué par <see cref="InventoryWindow"/>) et du
/// 276 : <see cref="PlayerEquipment.CostumeAt"/>.
/// Les pages costume n'ont AUCUNE stat propre (RefreshEQsAttr les saute) ;
/// seuls les enchants (scrolls kind 79 / witchcraft) comptent — stage
/// serveur « costumes ».
/// </summary>
public partial class CostumeWindow : Control
{
    private static readonly (int Loc, string Label)[] Locs =
    {
        (1, "Head"), (2, "Body"), (3, "Legs"), (4, "Hands"), (5, "Feet"),
    };

    private Panel? _win;
    private int _page = 2;
    private readonly Dictionary<int, SlotCell> _cells = new();
    private readonly List<Button> _tabs = new();
    private EdenSession? _session;

    /// <summary>Résidu de <c>kind % 3</c> attendu par page (natif).</summary>
    public static int PageMod(int container) =>
        container == 2 ? 0 : container == 3 ? 2 : 1;

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        if (hud == null) return;
        _win = EeSkin.MakeWindow(hud, UiTextEn.Translate("Outfits"),
            420, 140, 270, 250);
        _win.Name = "CostumeWndPanel";
        AddChild(_win);
        HudWindows.Register(_win);
        DragHelper.Persist(_win, "costume");

        for (var i = 0; i < 3; i++)
        {
            var container = 2 + i;
            var b = EeSkin.Grey(new Button
            {
                Text = UiTextEn.Translate("Page") + " " + (i + 1),
                FocusMode = FocusModeEnum.None,
                ToggleMode = true,
            }, 11);
            HudRoot.Pin(b, 0, 0, 0, 0, 12 + i * 82, 34, 12 + i * 82 + 78, 58);
            b.Pressed += () => { _page = container; Refresh(); };
            _tabs.Add(b);
            _win.AddChild(b);
        }
        var x = 14;
        foreach (var (loc, label) in Locs)
        {
            var cell = new SlotCell(loc, OnDrop, OnUnequip);
            HudRoot.Pin(cell, 0, 0, 0, 0, x, 70, x + 44, 114);
            _win.AddChild(cell);
            var l = new Label
            {
                Text = UiTextEn.Translate(label),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            EeSkin.Shadowed(l, 10, Color.Color8(200, 200, 195));
            HudRoot.Pin(l, 0, 0, 0, 0, x - 4, 116, x + 48, 132);
            _win.AddChild(l);
            _cells[loc] = cell;
            x += 48;
        }
        var info = new Label
        {
            Text = UiTextEn.Translate("Costume pieces have no stats of their own; enchant scrolls applied to them add stats. Drag an item from your bag onto a slot."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        EeSkin.Shadowed(info, 10, Color.Color8(170, 170, 165));
        HudRoot.Pin(info, 0, 0, 1, 1, 12, 142, -12, -8);
        _win.AddChild(info);
        _win.Visible = false;
        _win.VisibilityChanged += () => { if (_win.Visible) Refresh(); };
        PlayerEquipment.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        PlayerEquipment.Changed -= Refresh;
        base._ExitTree();
    }

    private void Refresh()
    {
        if (_win == null || !GodotObject.IsInstanceValid(_win)) return;
        for (var i = 0; i < _tabs.Count; i++)
            _tabs[i].SetPressedNoSignal(_page == 2 + i);
        foreach (var (loc, cell) in _cells)
            cell.Set(_page, PlayerEquipment.CostumeAt(_page, loc));
    }

    private Task Send(NetMessage msg)
    {
        _session ??= SessionCarry.Session;
        var s = _session;
        if (s?.Zone?.IsConnected != true) return Task.CompletedTask;
        return s.SendZoneAsync(msg, CancellationToken.None).ContinueWith(
            t => GD.PrintErr($"[costume] {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnDrop(int loc, int bag, int slot)
    {
        _ = Send(new ZoneSwitchItemLocation
        {
            SrcType = 0, SrcSlot = (short)slot, SrcIndex = (short)bag,
            DstType = 1, DstSlot = (short)loc, DstIndex = (short)_page,
        });
    }

    private void OnUnequip(int loc) =>
        _ = Send(ZoneItemActions.UnequipRequest(loc, _page));

    /// <summary>Un slot : icône + cible de drop (garde kind%3/EquipLoc
    /// côté client, le serveur reste l'autorité).</summary>
    private sealed partial class SlotCell : Panel
    {
        private readonly int _loc;
        private readonly Action<int, int, int> _drop;
        private readonly Action<int> _unequip;
        private readonly TextureRect _icon = new()
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        private int _page, _itemId;

        public SlotCell(int loc, Action<int, int, int> drop,
            Action<int> unequip)
        {
            _loc = loc; _drop = drop; _unequip = unequip;
            AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
            _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_icon);
        }

        public void Set(int page, int itemId)
        {
            _page = page; _itemId = itemId;
            var def = itemId != 0 ? InventoryWindow.LookupDef(itemId) : null;
            _icon.Texture = def is { Code.Length: > 0 }
                ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png")
                : null;
            TooltipText = itemId != 0
                ? (TextResolver.ItemName(itemId) ?? $"#{itemId}") + "\n" +
                  UiTextEn.Translate("Right-click to unequip")
                : "";
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_itemId == 0) return;
            if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Right } or
                InputEventMouseButton { Pressed: true, DoubleClick: true,
                    ButtonIndex: MouseButton.Left })
            {
                _unequip(_loc);
                AcceptEvent();
            }
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            if (!d.ContainsKey("itemId") || !d.ContainsKey("bag")) return false;
            var def = InventoryWindow.LookupDef(d["itemId"].AsInt32());
            return def != null && def.EquipLoc == _loc &&
                def.Kind % 3 == PageMod(_page);
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            _drop(_loc, d["bag"].AsInt32(), d["slot"].AsInt32());
        }
    }
}
