using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre boutique PNJ — layout natif <c>ShopWnd.layout</c>
/// (FrameWindow « Root/shop » + TabControl : « Tabsell » 商品 = articles
/// du marchand, « TabBuy » 買回 = file buyback) + template de ligne
/// <c>ShopItem.layout</c> (slot icône + ItemDeatilText + GlodText/si1 +
/// SilverText/si2) instancié par article.
///
/// Wire :
///   - 304 EnterShop → <see cref="ShopState"/> (catalogue C_Store) ;
///   - 22 <see cref="ZoneBuyItems"/> (achat, BuyBack=0) et rachat
///     (BuyBack=1, ShopIndex = index dans la file) ;
///   - 23 <see cref="ZoneSellItems"/> (vente — clic droit dans le sac
///     pendant que la boutique est ouverte, ou SellAllBtn = lot) ;
///   - 185 <see cref="ZoneBuyBackOpen"/> envoyé au changement d'onglet ;
///   - 512 <see cref="ZoneClientBuyBackList"/> →
///     <see cref="ShopState.Buyback"/> (append flag=1, retrait flag=2) ;
///   - 63 LeaveShop à la fermeture ; 45/47/342/343/46/48 réparations.
///
/// Session : <see cref="SessionCarry.Session"/> (envoi direct — même
/// octets que les helpers GameScene BuyShopItemAsync/SellItemAsync/
/// LeaveShopAsync ; en --live direct pas de session → fenêtre mock).
/// </summary>
public partial class ShopWindow : Control
{
    private Control? _window;
    private TabContainer? _tabs;
    private VBoxContainer? _buyRows;      // articles (Tabsell/border)
    private VBoxContainer? _bbRows;       // buyback (TabBuy/scrollpanel)
    private Control? _rowProto;           // clone de ShopItem.layout
    private Label? _gold;
    private int _selBuy = -1;
    private int _selBb = -1;

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
            _rowProto = hud?.Build("shopitem.layout");
        }
        BuildContent();
        DragHelper.EnsureTitleBar(_window ?? this, UiTextEn.Translate("Shop"));
        DragHelper.Persist(_window ?? this, "shop");
        Visible = false;
        ShopState.Changed += OnShopChanged;
        ShopState.BuybackChanged += OnBuybackChanged;
        PlayerVitals.GoldChanged += RefreshGold;
        MiscPackets.Received += OnMiscPacket;
    }

    public override void _ExitTree()
    {
        ShopState.Changed -= OnShopChanged;
        ShopState.BuybackChanged -= OnBuybackChanged;
        PlayerVitals.GoldChanged -= RefreshGold;
        MiscPackets.Received -= OnMiscPacket;
        base._ExitTree();
    }

    // ------------------------------------------------------------------

    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        _tabs = host.FindChildren("*", "TabContainer", true, false)
            .Cast<TabContainer>().FirstOrDefault();
        var buyPane = TopLeftVitals.Find<Control>(host, "Tabsell");
        var bbPane = TopLeftVitals.Find<Control>(host, "TabBuy");
        // Ne PAS forcer bbPane.Visible : le TabContainer gère la
        // visibilité des panes — un true permanent recouvrait la
        // liste d'articles du volet vente.
        // Bascule d'onglet → 185 : le serveur répond 304 (articles)
        // ou 512 flag=3 (rachat — les rows persistent côté client).
        if (_tabs != null && bbPane != null)
        {
            var bbIdx = bbPane.GetIndex();
            _tabs.TabChanged += t =>
            {
                if (!ShopState.IsOpen) return;
                Send(new ZoneBuyBackOpen
                    { Value = (byte)(t == bbIdx ? 1 : 0) });
            };
        }

        // Articles : host = « border » du Tabsell + scrollbar natif
        // (la barre Godot interne est masquée — look natif conservé).
        if (buyPane != null)
            _buyRows = MakeRowHost(buyPane);
        // Rachat : « scrollpanel » est déjà un ScrollContainer natif.
        if (bbPane != null)
        {
            var scroll = TopLeftVitals
                .Find<ScrollContainer>(bbPane, "scrollpanel");
            _bbRows = new VBoxContainer { Name = "__bbrows" };
            _bbRows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            (scroll ?? (Control)bbPane).AddChild(_bbRows);
        }

        // Or du joueur : label « Coin » natif.
        _gold = TopLeftVitals.Find<Label>(host, "Coin");
        if (_gold != null)
            _gold.Text = "";
        var coinText = TopLeftVitals.Find<Label>(host, "CoinText");
        if (coinText != null) coinText.Text = UiTextEn.Translate("Gold");

        // Boutons natifs : « buy » (TabBuy) = rachat ; « SellAllBtn »
        // (Tabsell) = vendre tout le vendable (confirmation).
        var nativeBuy = TopLeftVitals.Find<BaseButton>(host, "buy");
        if (nativeBuy != null) nativeBuy.Pressed += DoBuyBack;
        var nativeSell = TopLeftVitals.Find<BaseButton>(host, "SellAllBtn");
        if (nativeSell != null) nativeSell.Pressed += AskSellAll;

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

    /// <summary>Host de lignes « ShopItem » dans le volet « border »
    /// d'un onglet : ScrollContainer qui laisse la place à la
    /// scrollbar native (pilotée en miroir — look natif conservé).</summary>
    private static VBoxContainer? MakeRowHost(Control pane)
    {
        var border = TopLeftVitals.Find<Control>(pane, "border");
        if (border == null) return null;
        var scroll = new ScrollContainer { Name = "__scroll" };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var nbar = TopLeftVitals.Find<VScrollBar>(border, "scrollbar");
        scroll.OffsetRight = nbar != null
            ? -nbar.Size.X - 2 : -4;
        scroll.OffsetLeft = 4;
        scroll.OffsetTop = 4;
        scroll.OffsetBottom = -4;
        scroll.MouseFilter = MouseFilterEnum.Stop;
        border.AddChild(scroll);
        if (nbar != null)
        {
            scroll.VerticalScrollMode =
                ScrollContainer.ScrollMode.ShowNever;
            nbar.ValueChanged += v =>
                scroll.ScrollVertical = (int)v;
            var inner = scroll.GetVScrollBar();
            inner.ValueChanged += v =>
                nbar.SetValueNoSignal(v);
        }
        var rows = new VBoxContainer { Name = "__rows" };
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(rows);
        if (nbar != null)
            rows.Resized += () =>
            {
                var over = Math.Max(0, (int)(rows.Size.Y -
                    scroll.Size.Y));
                nbar.MaxValue = over;
                nbar.Page = scroll.Size.Y;
            };
        return rows;
    }

    /// <summary>Clone du template ShopItem (icon slot + nom +
    /// GlodText/SilverText + pièces si1/si2).</summary>
    private Control MakeShopRow(int itemId, int amount, string name,
        int priceCopper)
    {
        Control row;
        if (_rowProto != null)
        {
            row = (Control)_rowProto.Duplicate();
            row.CustomMinimumSize = new Vector2(0, 40);
            var slot = ItemSlotCell.Find(row, "Root/ShopItem/itemslot");
            slot?.SetItemId(itemId, amount);
            if (TopLeftVitals.Find<Label>(row, "ItemDeatilText")
                    is { } lab)
                lab.Text = name;
            var gold = priceCopper / 1000;
            var silver = priceCopper % 1000;
            if (TopLeftVitals.Find<Label>(row, "GlodText") is { } g)
                g.Text = gold > 0 ? $"{gold}" : "";
            if (TopLeftVitals.Find<Label>(row, "SilverText") is { } s)
                s.Text = gold > 0 || silver > 0 ? $"{silver}" : "0";
        }
        else
        {
            // Repli si shopitem.layout indisponible.
            row = new HBoxContainer
            {
                CustomMinimumSize = new Vector2(0, 30),
            };
            var def = ItemTableCache.Get(itemId);
            if (def is { Code.Length: > 0 })
            {
                var tex = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
                if (tex != null)
                    row.AddChild(new TextureRect
                    {
                        Texture = tex,
                        CustomMinimumSize = new Vector2(28, 28),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    });
            }
            row.AddChild(new Label
            {
                Text = amount > 1 ? $"{name} ×{amount}" : name,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            row.AddChild(new Label
            {
                Text = TransportWindow.FormatMoney(priceCopper),
            });
        }
        return row;
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
        RefreshBuyback();
        RefreshGold();
        Visible = true;
    }

    private void OnBuybackChanged()
    {
        if (IsVisibleInTree()) RefreshBuyback();
    }

    // ------------------------------------------------------------------
    // Acheter (articles du marchand)
    // ------------------------------------------------------------------

    private void RefreshBuy()
    {
        if (_buyRows == null) return;
        foreach (var c in _buyRows.GetChildren()) c.QueueFree();
        _selBuy = -1;
        foreach (var e in ShopState.Entries)
        {
            var idx = e.Index;
            var row = MakeShopRow(e.ItemId, e.Count, e.Name, e.Price);
            row.MouseFilter = MouseFilterEnum.Stop;
            row.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left } mb)
                {
                    _selBuy = idx;
                    if (mb.DoubleClick) DoBuy();
                    row.AcceptEvent();
                }
            };
            _buyRows.AddChild(row);
        }
        if (ShopState.Entries.Count == 0)
            _buyRows.AddChild(new Label
                { Text = UiTextEn.Translate("(empty)") });
    }

    /// <summary>NCID 22 BuyItems [u16 storeSlot][u16 count][u8 0].</summary>
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
    // Rachat (file buyback 512)
    // ------------------------------------------------------------------

    private void RefreshBuyback()
    {
        if (_bbRows == null) return;
        foreach (var c in _bbRows.GetChildren()) c.QueueFree();
        _selBb = -1;
        var list = ShopState.Buyback;
        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var name = TextResolver.ItemName(e.ItemId)
                ?? $"#{e.ItemId}";
            var idx = i;
            var row = MakeShopRow(e.ItemId, Math.Max(e.Count, 1),
                name, (int)e.Price);
            row.MouseFilter = MouseFilterEnum.Stop;
            row.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left } mb)
                {
                    _selBb = idx;
                    if (mb.DoubleClick) DoBuyBack();
                    row.AcceptEvent();
                }
            };
            _bbRows.AddChild(row);
        }
        if (list.Count == 0)
            _bbRows.AddChild(new Label
                { Text = UiTextEn.Translate("(nothing to buy back)") });
    }

    /// <summary>Rachat : NCID 22 BuyItems, BuyBack=1 et ShopIndex =
    /// index dans la file buyback (serveur : index de la deque).</summary>
    private void DoBuyBack()
    {
        if (_selBb < 0 || _selBb >= ShopState.Buyback.Count
            || !ShopState.IsOpen) return;
        Send(new ZoneBuyItems
        {
            ShopIndex = (ushort)_selBb,
            ItemIndex = 1,
            BuyBack = 1,
        });
    }

    // ------------------------------------------------------------------
    // Vente (SellAllBtn = lot ; le par-item se fait clic droit sac)
    // ------------------------------------------------------------------

    /// <summary>SellAllBtn natif : vend TOUS les items vendables du
    /// sac en un seul NCID 23 (confirmation d'abord — irréversible).</summary>
    private void AskSellAll()
    {
        if (!ShopState.IsOpen) return;
        var sellable = PlayerBag.Items
            .Where(it => (ItemTableCache.Get(it.ItemId)?.SellPrice ?? 0)
                > 0)
            .ToList();
        if (sellable.Count == 0) return;
        long total = 0;
        foreach (var it in sellable)
            total += (long)(ItemTableCache.Get(it.ItemId)!.SellPrice)
                * Math.Max(it.Amount, 1);
        var dlg = new ConfirmationDialog
        {
            DialogText = UiTextEn.Translate(
                "Sell ALL %s1 sellable items for ~%s2?")
                .Replace("%s1", sellable.Count.ToString())
                .Replace("%s2",
                    TransportWindow.FormatMoney((int)Math.Min(
                        total, int.MaxValue))),
            OkButtonText = UiTextEn.Translate("Sell"),
            CancelButtonText = UiTextEn.Translate("Cancel"),
        };
        dlg.Confirmed += () =>
        {
            var m = new ZoneSellItems();
            foreach (var it in sellable)
                m.Items.Add(((ushort)it.Slot,
                    (ushort)Math.Max(it.Amount, 1), (ushort)it.Bag));
            Send(m);
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    // ------------------------------------------------------------------
    // Réparation (boutons natifs fixAllBtn/fixoneBtn du ShopWnd)
    // ------------------------------------------------------------------

    /// <summary>Item de la réparation en attente de confirmation —
    /// null = « Repair All » (46), sinon item précis (48).</summary>
    private InventorySlot? _repairItem;

    private void AskRepairAll() =>
        Send(new ZoneServerAskRepairEquipGold());

    /// <summary>« Fix one » demande le coût total — le curseur-marteau
    /// natif choisit l'item ; sans sélection courante on garde le
    /// comportement « coût global » pour ne pas laisser le bouton muet.</summary>
    private void AskRepairOne() => AskRepairAll();

    /// <summary>342/343 = coûts de réparation → popup de
    /// confirmation avant d'envoyer 46/48 ; 512 = file buyback.</summary>
    private void OnMiscPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientAskRepairEquipGold q:
                _repairItem = null;
                ConfirmRepair(UiTextEn.Translate("Repair ALL equipment for %s1 g ?").Replace("%s1", q.Value.ToString("N0")));
                break;
            case ZoneClientAskRepairOneItemGold q:
                _repairItem = null;
                ConfirmRepair(
                    UiTextEn.Translate("Repair for %s1 g ?").Replace("%s1", q.Field3.ToString("N0")));
                break;
            case ZoneClientBuyBackList bb:
                ShopState.OnBuyBackList(bb);
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
        var bb = new Control { Name = UiTextEn.Translate("Buyback") };
        _tabs.AddChild(buy);
        _tabs.AddChild(bb);
        _buyRows = new VBoxContainer();
        buy.AddChild(_buyRows);
        _bbRows = new VBoxContainer();
        bb.AddChild(_bbRows);
        var gold = new Label { Name = "__gold" };
        gold.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        gold.Position = new Vector2(10, -30);
        p.AddChild(gold);
        _gold = gold;
        var x = new Button { Text = "×" };
        x.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        x.Position = new Vector2(-26, 2);
        x.Size = new Vector2(22, 20);
        x.Pressed += Close;
        p.AddChild(x);
        RefreshGold();
    }
}
