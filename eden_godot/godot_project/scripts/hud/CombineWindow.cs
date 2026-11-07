using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Station de combinaison — layout natif <c>combinewnd.layout</c>.
///
/// Wire (preuves : désasm CItemFactory::CheckCombineItem @0x809b570
/// / CombineItem @0x809b8e0 / SimpleAction @0x8115a10, portés dans
/// <c>eden_server/ZoneEntryFlow.CombineStation.cs</c>) :
///   - Ouverture : PlayerAction 14 (« Fusion » du sac) ou service
///     NPC 9 natif → S→C <b>432</b> ClientEnterStrengthen
///     <c>[i32 npcUid][s16 10][u16][s16 -1][s16]</c>.
///   - <b>170</b> C→S PlayerWork <c>[u32 recipeId][s16 catBag]
///     [s16 catSlot][s16 boostBag][s16 boostSlot]</c> — recipeId =
///     recette S_FamilyCombine catégorie 6 à 4 matériaux dont le
///     matériau[0] est le catalyseur déposé (unique : 146 recettes,
///     146 catalyseurs distincts — résolution déterministe).
///   - <b>433</b> S→C cast <c>[s16 10][s16 0x28][s16 0]</c>
///     (0x28×100 ms = 4 s — barre gérée par CraftWindow).
///   - <b>434</b> S→C cancel <c>[s16 10]</c>.
///   - <b>435</b> S→C résultat <c>[s16 10][s16 r]</c> : 1 succès,
///     0 échec (matériaux consommés dans les deux cas).
///   - <b>120</b> C→S StrengthenClose <c>[s16 10]</c> à la fermeture.
///
/// Slots natifs : <c>UseItemSlot</c> (catalyseur = matériau[0]),
/// <c>ItemmallSlot</c> (booster kind 0x1f41 — aucun dans les
/// données EDENFR, le serveur rejette tout autre id),
/// <c>StuffItemSlot0-3</c> + textes (les 4 matériaux, affichage),
/// <c>ResultItem</c> + <c>GoldText</c>/<c>SliverText</c> +
/// <c>NeedTimeText</c> (coût col9, cast 4 s).
/// </summary>
public partial class CombineWindow : Control
{
    private Control? _window;
    private ItemSlotCell? _catalyst, _booster;
    private readonly ItemSlotCell?[] _stuff = new ItemSlotCell?[4];

    // Sélections déposées (bag/slot dans l'inventaire).
    private int _catBag = -1, _catSlot = -1;
    private int _boostBag = -1, _boostSlot = -1;
    private CraftTable.CombineRecipe? _recipe;
    private bool _opOpen, _casting;

    private static Dictionary<int, CraftTable.CombineRecipe>? _combine;

    /// <summary>Kind du booster natif (CChanceItem +0x122 = 0x1f41).</summary>
    private const int BoosterKind = 0x1f41;

    private bool _native, _building;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        EnsureWindow();
        Visible = false;
        MiscPackets.Received += OnPacket;
        PlayerBag.Changed += OnBagChanged;
    }

    /// <summary>Le ctx uidata n'est pas prêt au _Ready du game.tscn
    /// (ZoneWindows chargé avec la scène) — construction paresseuse :
    /// on retente le layout natif à chaque ouverture tant que la
    /// fenêtre est le repli code.</summary>
    private void EnsureWindow()
    {
        if (_native) return;
        _building = true;
        var hud = HudExtensions.FindHud();
        var w = hud?.Build("combinewnd.layout");
        if (w == null)
        {
            if (_window == null) BuildFallback();
            _building = false;
            return;
        }
        _window?.QueueFree();
        _window = w;
        _native = true;
        AddChild(_window);
        HudExtensions.CenterNative(_window, new Vector2(370, 430));
        BindNative(_window);
        _window.Visible = false;
        _building = false;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        PlayerBag.Changed -= OnBagChanged;
        base._ExitTree();
    }

    /// <summary>Montre la fenêtre hors session (capture --hud-open).</summary>
    public void ShowDemo()
    {
        EnsureWindow();
        if (_window != null) { _window.Visible = true; HudWindows.Front(_window); }
        Visible = true;
    }

    // ------------------------- paquets -------------------------

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientEnterStrengthen e when e.Field2 == 10:
                _opOpen = true;
                EnsureWindow();
                EnsureTables();
                if (_window != null) { _window.Visible = true; HudWindows.Front(_window); }
                Visible = true;
                GD.Print("[verify] combine : 432 op=10 — station");
                break;
            case ZoneClientStrengthenItemCancel c when c.Value == 10:
                _casting = false;
                RefreshState();
                break;
            case ZoneClientStrengthenItemResult r when r.Field1 == 10:
                OnResult(r.Field2);
                break;
        }
    }

    /// <summary>435 [10][résultat] — 1 succès / 0 échec (le natif
    /// consomme les matériaux dans les deux cas).</summary>
    private void OnResult(int result)
    {
        _casting = false;
        var hud = GetParent() as HudRoot;
        hud?.AddChat(7, "", UiTextEn.Translate(result == 1
            ? "Fusion succeeded."
            : "Fusion failed."));
        RefreshState();
    }

    // ------------------------- envois -------------------------

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[combine] pas de session — {msg.Name}");
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[combine] {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>CommissionBtn → 170 PlayerWork.</summary>
    private void OnCommission()
    {
        if (_casting || _recipe == null || _catBag < 0) return;
        _casting = true;
        Send(new ZonePlayerWork
        {
            WorkId = (uint)_recipe.Id,
            CatalystBag = (short)_catBag,
            CatalystSlot = (short)_catSlot,
            BoostBag = (short)_boostBag,
            BoostSlot = (short)_boostSlot,
        });
        RefreshState();
    }

    private void OnVisibilityChanged()
    {
        // _window.Visible=false pendant la construction ne compte pas ;
        // et c'est la fenêtre native qui compte (this reste visible).
        if (_building || !_opOpen) return;
        if (_window == null || _window.Visible) return;
        _opOpen = false;
        Send(new ZoneServerStrengthenClose { Value = 10 });
    }

    // ------------------------- UI native -------------------------

    private void BindNative(Control w)
    {
        DragHelper.EnsureTitleBar(w, UiTextEn.Translate("Fusion"));
        DragHelper.Persist(w, "combine");
        w.VisibilityChanged += OnVisibilityChanged;
        // Les Static intermédiaires (UseItemBg, Itemmall/Bg) sont
        // aplatis par le builder : on cherche par nom de feuille.
        _catalyst = FindLeaf<ItemSlotCell>(w, "UseItemSlot");
        _booster = FindLeaf<ItemSlotCell>(w, "ItemmallSlot");
        for (var i = 0; i < 4; i++)
            _stuff[i] = FindLeaf<ItemSlotCell>(w, $"StuffItemSlot{i}");
        CraftWindow.PolishDetailStatics(w, false);
        BindDrops();
        var btn = FindLeaf<BaseButton>(w, "CommissionBtn");
        if (btn != null) btn.Pressed += OnCommission;
        var close = FindLeaf<BaseButton>(w, "CloseBtn");
        if (close != null) close.Pressed += () => w.Visible = false;
    }

    private void BindDrops()
    {
        if (_catalyst != null) _catalyst.ItemDropped += (_, d) =>
        {
            if (!d.ContainsKey("bag") || !d.ContainsKey("slot"))
                return;
            _catBag = d["bag"].AsInt32();
            _catSlot = d["slot"].AsInt32();
            var it = FindBag(_catBag, _catSlot);
            if (it != null) _catalyst.SetItem(it);
            RefreshState();
        };
        if (_booster != null) _booster.ItemDropped += (_, d) =>
        {
            if (!d.ContainsKey("bag") || !d.ContainsKey("slot"))
                return;
            var bag = d["bag"].AsInt32();
            var slot = d["slot"].AsInt32();
            var it = FindBag(bag, slot);
            // Booster CChanceItem kind 0x1f41 — tout autre item est
            // refusé côté serveur, on refuse tôt (natif : le slot ne
            // retient que les CChanceItem).
            if (it == null || it.ItemId != BoosterKind) return;
            _boostBag = bag;
            _boostSlot = slot;
            _booster.SetItem(it);
            RefreshState();
        };
    }

    /// <summary>Slot déposé toujours présent dans le sac ? Sinon on
    /// le vide (item déplacé/consommé).</summary>
    private void OnBagChanged()
    {
        if (_catBag >= 0 && FindBag(_catBag, _catSlot) == null)
        { _catBag = _catSlot = -1; _catalyst?.Clear(); }
        if (_boostBag >= 0 && FindBag(_boostBag, _boostSlot) == null)
        { _boostBag = _boostSlot = -1; _booster?.Clear(); }
        RefreshState();
    }

    private static InventorySlot? FindBag(int bag, int slot)
    {
        foreach (var it in PlayerBag.Items)
            if (it.Bag == bag && it.Slot == slot) return it;
        return null;
    }

    /// <summary>Recalcule la recette depuis le catalyseur déposé et
    /// remplit matériaux / produit / coût / temps.</summary>
    private void RefreshState()
    {
        _recipe = ResolveRecipe();
        FillRecipe();
        var btn = _window != null
            ? FindLeaf<BaseButton>(_window, "CommissionBtn") : null;
        if (btn != null)
            btn.Disabled = _recipe == null || _catBag < 0 || _casting;
    }

    /// <summary>Catalyseur → recette cat-6 à 4 matériaux dont
    /// matériau[0] = l'item déposé (CheckCombineItem natif).</summary>
    private CraftTable.CombineRecipe? ResolveRecipe()
    {
        var cat = _catBag >= 0 ? FindBag(_catBag, _catSlot) : null;
        if (cat == null || _combine == null) return null;
        foreach (var r in _combine.Values)
            if (r.Category == 6 && r.Materials.Count == 4 &&
                r.Materials[0].ItemId == cat.ItemId)
                return r;
        return null;
    }

    private void FillRecipe()
    {
        for (var i = 0; i < 4; i++)
        {
            var m = _recipe != null && i < _recipe.Materials.Count
                ? _recipe.Materials[i] : (0, "", 0);
            _stuff[i]?.SetItemId(m.Item1, Math.Max(m.Item3, 1));
            SetText($"StuffItemText{i}",
                m.Item2.Length > 0 ? m.Item2 : "");
            SetText($"StuffItemNumText{i}",
                m.Item3 > 0 ? $"{m.Item3}" : "");
        }
        var result = _window != null
            ? FindLeaf<ItemSlotCell>(_window, "ResultItem") : null;
        result?.SetItemId(_recipe?.ProductItemId ?? 0,
            Math.Max(_recipe?.ProductQty ?? 1, 1));
        SetText("ResultText", _recipe?.ProductName ?? "");
        SetText("GoldText",
            _recipe != null ? $"{_recipe.Gold / 1000}" : "0");
        SetText("SliverText",
            _recipe != null ? $"{_recipe.Gold % 1000}" : "0");
        // Cast fixe natif : 0x28 unités ×100 ms = 4 s.
        SetText("NeedTimeText", _recipe != null ? "4" : "--");
    }

    // ------------------------- tables / helpers -------------------------

    private static void EnsureTables()
    {
        if (_combine != null) return;
        var p = AssetCatalog.ResolveRawAsset(
            "data/db/c_familycombine.ini");
        _combine = p != null ? CraftTable.LoadCombine(p) : new();
    }

    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        // Pas de filtre « type » : FindChildren ne matche pas les
        // classes script C# (ItemSlotCell) — on filtre par cast.
        foreach (var n in root.FindChildren("*", "", true, false))
            if (n is T t && n.Name.ToString().EndsWith(leaf))
                return t;
        return null;
    }

    private void SetText(string leaf, string text)
    {
        if (_window == null) return;
        if (FindLeaf<Label>(_window, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_window, leaf) is { } r)
            r.Text = text;
    }

    /// <summary>Repli sans uidata : panneau minimal (slots code).</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "CombineWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-185, -215);
        p.Size = new Vector2(370, 430);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
        p.VisibilityChanged += OnVisibilityChanged;
        DragHelper.EnsureTitleBar(p, UiTextEn.Translate("Fusion"));
        DragHelper.Persist(p, "combine");
        var v = new VBoxContainer { Name = "__v" };
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        v.OffsetTop = 30;
        p.AddChild(v);
        _catalyst = new ItemSlotCell();
        _booster = new ItemSlotCell();
        var row = new HBoxContainer();
        row.AddChild(_catalyst);
        row.AddChild(_booster);
        v.AddChild(row);
        var stuff = new HBoxContainer();
        for (var i = 0; i < 4; i++)
        {
            _stuff[i] = new ItemSlotCell();
            stuff.AddChild(_stuff[i]);
        }
        v.AddChild(stuff);
        BindDrops();
        var btn = new Button { Text = UiTextEn.Translate("Fusion") };
        btn.Pressed += OnCommission;
        v.AddChild(btn);
    }
}
