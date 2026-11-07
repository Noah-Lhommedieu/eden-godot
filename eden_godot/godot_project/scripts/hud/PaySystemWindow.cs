using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// PaySystem — échanges « pay » d'items de quête contre récompenses
/// (lot D5). Layout natif <c>paysystemwnd.layout</c>
/// (« /Root/PaySystem », FrameWindow) :
///   - <c>static1</c> : ListText/ContentText + pane de description +
///     <c>SelectList</c> = liste des événements (filtrée par le
///     shopId du paquet — <i>inférence</i> : type col1 de
///     c_paysystem == shopId, le serveur n'émet pas la liste).
///   - <c>static2</c> : <c>PayBorder/Up</c> = 4 slots de coûts
///     (SlotImage→ItemSlotCell, ItemName1-4, Amount1-4), volet
///     <c>AwardBorder/Down</c> = récompenses, <c>PayButton</c>
///     déclenche, <c>ChancelButton</c> ferme.
///
/// Wire : ouverture <c>ZoneClientOpenPaySystem</c> (602)
/// <c>[i32 npcUid][u16 shopId]</c> ; achat =
/// <c>ZoneServerPayEventSelect</c> (253) <c>[u32 eventId]
/// [s16 choose]</c> — choose = index dans ChooseItem de l'award_cmd
/// (-1 quand l'événement n'a pas de récompense au choix — preuve
/// serveur : CheckChooseItem). Réponses : 604 PayEventOK + textes
/// 316 (affichés par le chat générique).
///
/// Données : <c>PaySystemTable</c> (c_paysystem.ini + t_paysystem
/// EDENFR). Quantités possédées depuis <c>PlayerBag</c>.
/// </summary>
public partial class PaySystemWindow : Control
{
    private Control? _window;
    private ItemList? _list;
    private Control? _desc;
    private ItemSlotCell?[] _slots = new ItemSlotCell?[4];
    private Control?[] _names = new Control?[4];
    private Control?[] _amounts = new Control?[4];
    private Control? _award;
    private Button? _pay;
    private List<PaySystemTable.Def>? _events;
    private List<PaySystemTable.Def> _shown = new();
    private int _selected = -1;
    private static List<PaySystemTable.Def>? _table;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
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
        if (m is ZoneClientOpenPaySystem o) Open((int)o.Field2);
    }

    /// <summary>shopId du 602 → sous-liste des événements de ce
    /// type (inférence — repli « tous » si aucun ne matche, pour ne
    /// pas rendre le service inerte sur un shop inconnu).</summary>
    public void Open(int shopId)
    {
        Ensure();
        _shown = _events!.Where(e => e.Type == shopId).ToList();
        if (_shown.Count == 0) _shown = _events!;
        _selected = -1;
        _window!.Visible = true;
        Visible = true;
        FillList();
    }

    // ------------------------- construction -------------------------

    private void Ensure()
    {
        _table ??= Load();
        _events = _table;
        if (_window != null) return;
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("paysystemwnd.layout");
        if (_window == null)
        {
            _window = new PanelContainer
            { CustomMinimumSize = new Vector2(420, 300) };
            ((PanelContainer)_window).AddChild(new Label
            { Text = "paysystemwnd.layout introuvable" });
        }
        AddChild(_window);
        HudExtensions.CenterNative(_window,
            new Vector2(820, 590));
        _window.Visible = false;
        DragHelper.EnsureTitleBar(_window,
            UiTextEn.Translate("PaySystem"));
        DragHelper.Persist(_window, "paysystem");

        _list = FindLeaf<ItemList>(_window, "SelectList");
        _list?.ItemSelected += i => Select((int)i);
        _desc = FindLeaf<ScrollContainer>(_window, "ScrollablePane");
        _award = FindLeaf<ScrollContainer>(_window, "Down");
        for (var i = 0; i < 4; i++)
        {
            _slots[i] = Cell(_window, $"Slot{i + 1}");
            _names[i] = Leaf(_window, $"ItemName{i + 1}");
            _amounts[i] = Leaf(_window, $"Amount{i + 1}");
        }
        _pay = FindLeaf<Button>(_window, "PayButton");
        if (_pay != null) _pay.Pressed += OnPay;
        var cancel = FindLeaf<Button>(_window, "ChancelButton");
        if (cancel != null)
            cancel.Pressed += () => _window.Visible = false;
        if (_desc is ScrollContainer sc1)
            sc1.AddChild(new RichTextLabel { FitContent = true });
        // Libellés natifs Big5 du layout (« x繳交列表 »…) — le client
        // natif les écrase via setText(textindex) ; on pose l'équivalent
        // FR (inférence de libellé — l'id textindex exact n'est pas
        // documenté).
        SetText(Leaf(_window, "ListText"),
            UiTextEn.Translate("Exchange list"));
        SetText(Leaf(_window, "ContentText"),
            UiTextEn.Translate("Details"));
        SetText(Leaf(_window, "PayItemText"),
            UiTextEn.Translate("Required items"));
        SetText(Leaf(_window, "PayText"),
            UiTextEn.Translate("Reward"));
        if (_pay != null) _pay.Text = UiTextEn.Translate("Pay");
        if (cancel != null)
            cancel.Text = UiTextEn.Translate("Close");
        // Fenêtre neuve = neutre : maquette (一二三四五 / 999) vidée.
        for (var i = 0; i < 4; i++)
        { SetText(_names[i], ""); SetText(_amounts[i], ""); }
    }

    private void FillList()
    {
        if (_list == null) return;
        _list.Clear();
        foreach (var e in _shown) _list.AddItem(e.Name);
    }

    private void Select(int i)
    {
        _selected = i;
        var e = _shown[i];
        SetText(_desc, e.Desc.Length > 0 ? e.Desc : e.Name);
        // Coûts : 4 slots fixes natifs.
        for (var s = 0; s < 4; s++)
        {
            var cost = e.Cost[s];
            var has = cost.ItemId != 0
                ? PlayerBag.Items
                    .Where(it => it.ItemId == cost.ItemId)
                    .Sum(it => it.Amount)
                : 0;
            _slots[s]?.SetItemId(cost.ItemId,
                Math.Max(cost.Count, 1));
            SetText(_names[s], cost.ItemId == 0 ? ""
                : TextResolver.ItemName(cost.ItemId)
                  ?? $"#{cost.ItemId}");
            SetText(_amounts[s], cost.ItemId == 0 ? ""
                : $"{has}/{cost.Count}");
        }
        // Récompenses : items fixes + or + choix.
        var aw = new List<string>();
        foreach (var (id, n) in e.GiveItems)
            aw.Add($"{TextResolver.ItemName(id) ?? $"#{id}"} ×{n}");
        if (e.GiveGold > 0)
            aw.Add($"{e.GiveGold} "
                + UiTextEn.Translate("gold"));
        foreach (var (id, n) in e.ChooseItems)
            aw.Add(UiTextEn.Translate("Choice") + " : "
                + $"{TextResolver.ItemName(id) ?? $"#{id}"} ×{n}");
        SetText(_award, aw.Count > 0 ? string.Join("\n", aw)
            : UiTextEn.Translate("None"));
    }

    private void OnPay()
    {
        if (_selected < 0 || _selected >= _shown.Count) return;
        var e = _shown[_selected];
        // choose = index de la récompense au choix ; -1 si la liste
        // est vide (CheckChooseItem natif : liste vide → pas de choix).
        short choose = e.ChooseItems.Count > 0 ? (short)0 : (short)-1;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning("[paysystem] pas de session");
            return;
        }
        var task = s.SendZoneAsync(new ZoneServerPayEventSelect
            { Field1 = (uint)e.Id, Field2 = choose },
            CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[paysystem] envoi 253 : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------- helpers -------------------------

    private static List<PaySystemTable.Def> Load()
    {
        var p = AssetCatalog.ResolveRawAsset("data/db/c_paysystem.ini");
        return p != null ? PaySystemTable.Load(p) : new();
    }

    private static void SetText(Control? c, string text)
    {
        if (c is Label l) l.Text = text;
        else if (c is RichTextLabel r) r.Text = text;
        else if (c is ScrollContainer sc
                 && sc.GetChildCount() > 0
                 && sc.GetChild(0) is RichTextLabel inner)
            inner.Text = text;
    }

    private static Control? Leaf(Node root, string leaf)
    {
        foreach (var n in root.FindChildren("*", "", true, false))
            if (n.Name.ToString().EndsWith(leaf))
                return n as Control;
        return null;
    }

    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        foreach (var n in root.FindChildren("*", "", true, false))
            if (n is T t && n.Name.ToString().EndsWith(leaf))
                return t;
        return null;
    }

    private static ItemSlotCell? Cell(Node root, string leaf)  // suffixe

    {
        foreach (var n in root.FindChildren("*", "", true, false))
            if (n is ItemSlotCell c
                && n.Name.ToString().EndsWith(leaf)) return c;
        return null;
    }
}
