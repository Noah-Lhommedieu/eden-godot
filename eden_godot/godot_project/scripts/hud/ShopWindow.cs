using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre boutique PNJ (lot S4) — layout natif <c>ShopWnd.layout</c>
/// (FrameWindow « Root/shop » + TabControl : onglet « 商品 » = acheter,
/// onglet « 買回 » = rachat natif, réutilisé ici pour la vente).
///
/// Ouverture : <see cref="ShopState.Changed"/> (304 EnterShop →
/// catalogue C_Store parsé). Contenu :
///   - <b>Acheter</b> : liste des <see cref="ShopState.Entries"/>
///     (icône <c>UI/itemicon/&lt;code&gt;.png</c>, nom EN t_item, prix
///     c_item.SellPrice) + or du joueur (<see cref="PlayerVitals.Gold"/>)
///     — bouton Acheter → NCID 22 <see cref="ZoneBuyItems"/> ;
///   - <b>Vendre</b> : items du sac (<see cref="PlayerBag"/>) →
///     bouton Vendre → NCID 23 <see cref="ZoneSellItems"/> ;
///   - fermeture → NCID 63 LeaveShop + <see cref="ShopState.Close"/>.
///
/// Session : <see cref="SessionCarry.Session"/> (envoi direct — même
/// octets que les helpers GameScene BuyShopItemAsync/SellItemAsync/
/// LeaveShopAsync ; en --live direct pas de session → fenêtre mock).
/// </summary>
public partial class ShopWindow : Control
{
    private Control? _window;
    private TabContainer? _tabs;
    private VBoxContainer? _buyList;
    private VBoxContainer? _sellList;
    private Label? _gold;
    private int _selBuy = -1;
    private InventorySlot? _selSell;

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("ShopWnd.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -230, -200, 230, 200);
            var close = TopLeftVitals.Find<BaseButton>(_window,
                "closeBtn");
            if (close != null) close.Pressed += Close;
        }
        BuildContent();
        DragHelper.EnsureTitleBar(_window ?? this, UiTextEn.Translate("Shop"));
        DragHelper.Persist(_window ?? this, "shop");
        Visible = false;
        ShopState.Changed += OnShopChanged;
        PlayerVitals.GoldChanged += RefreshGold;
        PlayerBag.Changed += RefreshSell;
        MiscPackets.Received += OnMiscPacket;
    }

    public override void _ExitTree()
    {
        ShopState.Changed -= OnShopChanged;
        PlayerVitals.GoldChanged -= RefreshGold;
        PlayerBag.Changed -= RefreshSell;
        MiscPackets.Received -= OnMiscPacket;
        base._ExitTree();
    }

    // ------------------------------------------------------------------

    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        // Onglets natifs : « Tabsell » = marchandise (achat),
        // « TabBuy » = rachat natif — on le détourne pour la vente.
        _tabs = host.FindChildren("*", "TabContainer", true, false)
            .Cast<TabContainer>().FirstOrDefault();
        var buyPane = TopLeftVitals.Find<Control>(host, "Tabsell");
        var sellPane = TopLeftVitals.Find<Control>(host, "TabBuy");
        if (buyPane != null) buyPane.Name = UiTextEn.Translate("Buy");
        if (sellPane != null)
        {
            sellPane.Name = UiTextEn.Translate("Sell");
            sellPane.Visible = true;   // natif : Visible=False
        }
        _buyList = MakeList(buyPane ?? host);
        _sellList = MakeList(sellPane ?? host);

        // Or du joueur : le label « Coin » natif est remplacé.
        _gold = TopLeftVitals.Find<Label>(host, "Coin");
        if (_gold != null)
            _gold.Text = "";
        var coinText = TopLeftVitals.Find<Label>(host, "CoinText");
        if (coinText != null) coinText.Text = UiTextEn.Translate("Gold");

        // Bouton acheter sur le panneau achat, vendre sur l'autre.
        var buyBtn = MakeButton(buyPane ?? host, UiTextEn.Translate("Buy"));
        buyBtn.Pressed += DoBuy;
        var sellBtn = MakeButton(sellPane ?? host, UiTextEn.Translate("Sell"));
        sellBtn.Pressed += DoSell;
        // Le bouton natif « buy » du TabBuy s'il existe → idem.
        var nativeBuy = TopLeftVitals.Find<BaseButton>(host, "buy");
        if (nativeBuy != null) nativeBuy.Pressed += DoBuy;
        var nativeSell = TopLeftVitals.Find<BaseButton>(host, "SellAllBtn");
        if (nativeSell != null) nativeSell.Pressed += DoSell;

        // Boutons natifs de réparation (onglet Tabsell — forgeron) :
        // fixAll → 45 (coût total) → 342 → confirme → 46 ;
        // fixone → 47 sur l'item sélectionné → 343 → confirme → 48.
        // Ils sont re-parentés sur le bord bas de la fenêtre : dans
        // leur onglet natif ils restent cachés derrière le volet.
        var hw = host.Size.X;
        var hh = host.Size.Y;
        foreach (var (leaf, x) in new[] { ("fixoneBtn", -200),
                                        ("fixAllBtn", -160) })
            if (TopLeftVitals.Find<BaseButton>(host, leaf) is { } fb)
            {
                fb.GetParent()?.RemoveChild(fb);
                host.AddChild(fb);
                // Position absolue (ancres neutres) — les ancres
                // natives étaient relatives au volet d'onglet.
                fb.AnchorLeft = fb.AnchorRight = 0;
                fb.AnchorTop = fb.AnchorBottom = 0;
                fb.Position = new Vector2(hw + x, hh - 46);
                fb.Size = new Vector2(32, 32);
                fb.Visible = true;
                if (leaf == "fixAllBtn") fb.Pressed += AskRepairAll;
                else fb.Pressed += AskRepairOne;
            }

        // Bouton fermer de repli si pas de closeBtn natif.
        var cb = TopLeftVitals.Find<BaseButton>(host, "closeBtn");
        if (cb == null)
        {
            var x = new Button { Text = "×" };
            x.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
            x.Position = new Vector2(-26, 4);
            x.Size = new Vector2(22, 20);
            x.Pressed += Close;
            host.AddChild(x);
        }
        RefreshGold();
    }

    private VBoxContainer MakeList(Control pane)
    {
        var scroll = new ScrollContainer { Name = "__scroll" };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetTop = 30;
        scroll.OffsetBottom = -46;
        scroll.OffsetLeft = 8;
        scroll.OffsetRight = -8;
        scroll.MouseFilter = MouseFilterEnum.Stop;
        pane.AddChild(scroll);
        var list = new VBoxContainer { Name = "__list" };
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        list.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(list);
        return list;
    }

    private Button MakeButton(Control pane, string label)
    {
        var b = new Button { Name = "__action", Text = label };
        b.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        b.Position = new Vector2(-110, -38);
        b.Size = new Vector2(100, 30);
        pane.AddChild(b);
        return b;
    }

    /// <summary>304 reçu/fermeture → montre ou remplit la fenêtre.</summary>
    private void OnShopChanged()
    {
        if (!ShopState.IsOpen)
        {
            Visible = false;
            return;
        }
        RefreshBuy();
        RefreshSell();
        RefreshGold();
        Visible = true;
    }

    // ------------------------------------------------------------------
    // Acheter
    // ------------------------------------------------------------------

    private void RefreshBuy()
    {
        if (_buyList == null) return;
        foreach (var c in _buyList.GetChildren()) c.QueueFree();
        _selBuy = -1;
        foreach (var e in ShopState.Entries)
        {
            var row = new Button
            {
                Text = $"  {e.Name}   —   {e.Price} " + UiTextEn.Translate("g"),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 26),
            };
            row.AddThemeFontSizeOverride("font_size", 11);
            var idx = e.Index;
            var itemId = e.ItemId;
            row.Pressed += () => { _selBuy = idx; };
            var def = ItemTableCache.Get(itemId);
            if (def != null && def.Code.Length > 0)
            {
                var tex = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
                if (tex != null) row.Icon = tex;
            }
            _buyList.AddChild(row);
        }
        if (ShopState.Entries.Count == 0)
            _buyList.AddChild(new Label { Text = UiTextEn.Translate("(empty)") });
    }

    /// <summary>NCID 22 BuyItems [u16 storeSlot][u16 count][u8 buyback].</summary>
    private void DoBuy()
    {
        if (_selBuy < 0 || !ShopState.IsOpen) return;
        Send(new ZoneBuyItems
        {
            ShopIndex = (ushort)_selBuy,
            ItemIndex = 1,             // quantité
            BuyBack = 0,
        });
    }

    // ------------------------------------------------------------------
    // Vendre
    // ------------------------------------------------------------------

    private void RefreshSell()
    {
        if (_sellList == null) return;
        foreach (var c in _sellList.GetChildren()) c.QueueFree();
        _selSell = null;
        foreach (var it in PlayerBag.Items)
        {
            var name = TextResolver.ItemName(it.ItemId)
                ?? $"#{it.ItemId}";
            var def = ItemTableCache.Get(it.ItemId);
            var price = def?.SellPrice ?? 0;
            var qty = it.Amount > 1 ? $" ×{it.Amount}" : "";
            var row = new Button
            {
                Text = $"  {name}{qty}   —   {price} " + UiTextEn.Translate("g"),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 26),
            };
            row.AddThemeFontSizeOverride("font_size", 11);
            var slot = it;
            row.Pressed += () => { _selSell = slot; };
            _sellList.AddChild(row);
        }
        if (PlayerBag.Items.Count == 0)
            _sellList.AddChild(new Label { Text = UiTextEn.Translate("(empty bag)") });
    }

    /// <summary>NCID 23 SellItems [i32 1][u16 slot][u16 count]
    /// [u16 container].</summary>
    private void DoSell()
    {
        if (_selSell is not { } it || !ShopState.IsOpen) return;
        var m = new ZoneSellItems();
        m.Items.Add(((ushort)it.Slot,
            (ushort)Math.Max(it.Amount, 1), (ushort)it.Bag));
        Send(m);
    }

    // ------------------------------------------------------------------
    // Réparation (boutons natifs fixAllBtn/fixoneBtn du ShopWnd)
    // ------------------------------------------------------------------

    /// <summary>Item de la réparation en attente de confirmation —
    /// null = « Repair All » (46), sinon item précis (48).</summary>
    private InventorySlot? _repairItem;

    private void AskRepairAll() =>
        Send(new ZoneServerAskRepairEquipGold());

    /// <summary>« Fix one » répare l'item sélectionné dans la liste
    /// de vente (le curseur-marteau natif choisit l'item — chez nous
    /// la sélection existante fait office). Sans sélection, on
    /// demande quand même le coût total pour ne pas laisser le
    /// bouton muet.</summary>
    private void AskRepairOne()
    {
        if (_selSell is { } it)
            Send(new ZoneServerAskRepairOneItemGold
            {
                Field1 = (byte)it.Bag, Field3 = (short)it.Slot,
            });
        else
            AskRepairAll();
    }

    /// <summary>342/343 = coûts de réparation → popup de
    /// confirmation avant d'envoyer 46/48.</summary>
    private void OnMiscPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientAskRepairEquipGold q:
                _repairItem = null;
                ConfirmRepair($"Repair ALL equipment for {q.Value:N0} g ?");
                break;
            case ZoneClientAskRepairOneItemGold q:
                _repairItem = _selSell;
                ConfirmRepair(
                    $"Repair this item for {q.Field3:N0} g ?");
                break;
        }
    }

    private void ConfirmRepair(string text)
    {
        var dlg = new ConfirmationDialog
        {
            DialogText = text,
            OkButtonText = UiTextEn.Translate("Repair"),
            CancelButtonText = UiTextEn.Translate("Cancel"),
        };
        dlg.Confirmed += () =>
        {
            if (_repairItem is { } it)
                Send(new ZoneServerRepairOneItem
                {
                    Field1 = (byte)it.Bag, Field3 = (short)it.Slot,
                });
            else
                Send(new ZoneServerRepairEquipment());
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void RefreshGold()
    {
        if (_gold != null) _gold.Text = $"{PlayerVitals.Gold:N0}";
    }

    /// <summary>Fermeture : NCID 63 LeaveShop + état local.</summary>
    private void Close()
    {
        ShopState.Close();
        Send(ZoneEmptyRequest.LeaveShop);
        Visible = false;
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[shop] pas de session — {msg.Name}");
            return;
        }
        var task = s.SendZoneAsync(msg, CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[shop] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Repli sans uidata : panneau centré générique.</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "ShopWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-230, -200);
        p.Size = new Vector2(460, 400);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
        var title = DragHelper.EnsureTitleBar(p, UiTextEn.Translate("Shop"));
        _tabs = new TabContainer();
        _tabs.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _tabs.OffsetTop = 26;
        p.AddChild(_tabs);
        var buy = new Control { Name = UiTextEn.Translate("Buy") };
        var sell = new Control { Name = UiTextEn.Translate("Sell") };
        _tabs.AddChild(buy);
        _tabs.AddChild(sell);
        _buyList = MakeList(buy);
        _sellList = MakeList(sell);
        var gold = new Label { Name = "__gold" };
        gold.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        gold.Position = new Vector2(10, -30);
        p.AddChild(gold);
        _gold = gold;
        var bb = MakeButton(buy, UiTextEn.Translate("Buy"));
        bb.Pressed += DoBuy;
        var sb = MakeButton(sell, UiTextEn.Translate("Sell"));
        sb.Pressed += DoSell;
        var x = new Button { Text = "×" };
        x.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        x.Position = new Vector2(-26, 2);
        x.Size = new Vector2(22, 20);
        x.Pressed += Close;
        p.AddChild(x);
        RefreshGold();
    }
}
