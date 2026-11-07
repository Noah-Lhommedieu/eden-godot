using Godot;
using EdenClient.Ui;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre hôtel des ventes (vague 1-C) — layout natif
/// <c>AuctionWnd.layout</c> quand uidata est présent, sinon fenêtre
/// sombre en code.
///
/// Ouverture : 430 <see cref="ZoneAuctionHouse"/> (service NPC type 18
/// → <see cref="AuctionState.Opened"/>). La fenêtre envoie alors le
/// 114 <see cref="ZoneAuctionBuyQuery"/> (catalogue → 425) et le 116
/// <see cref="ZoneAuctionSellQuery"/> (mes ventes → 428).
///
/// Contenu (format serveur réel — MakeAuctionEntry ne remplit que
/// AuctionId/ItemId/Price/SellerId/ExpireTime, cf. ShopHandlers.cs:469) :
///   - onglet <b>Parcourir</b> : annonces actives (icône itemicon,
///     nom résolu t_item — le champ ItemName du record reste vide
///     côté serveur C#, prix, vendeur, temps restant) + recherche
///     par nom LOCALE (le serveur ignore les filtres du 114) +
///     « Acheter » (115 → 426, livraison par courrier). Aucun bouton
///     d'enchère progressive : BuyAsync règle BuyoutPrice ?? Price,
///     le protocole ne propose pas de bid.
///   - onglet <b>Mes ventes</b> : annonces du joueur (428) +
///     « Annuler » (118 → 427, item rendu par courrier) + formulaire
///     de mise en vente (117) : slot de drop {bag,slot} depuis
///     l'inventaire, prix, buyout optionnel (durée 48 h fixe et
///     frais dépôt 5 % gérés serveur).
///
/// Aucune mutation UI hors thread principal : les binds
/// <c>GameScene</c> alimentent <see cref="AuctionState"/>.
/// </summary>
public partial class AuctionWindow : Control
{
    private Control? _window;
    private TabContainer? _tabs;
    private LineEdit? _search;
    private VBoxContainer? _buyList, _sellList;
    private Label? _status, _sellStatus;
    private SellDropSlot? _sellSlot;
    private LineEdit? _priceEdit, _buyoutEdit;
    private Button? _buyBtn, _cancelBtn;
    private int _selBuyId = -1, _selSellId = -1;

    // --- chemin natif (auctionwnd + auctionbuywnd + auctionsellwnd) ---
    private bool _native;
    private const int PageSize = 20;
    private int _page;
    private int _cat;                       // 0 tout 1 armes 2 armures 3 conso 4 autre
    private Control? _buyPane, _sellPane;
    private MclList? _nBuyMcl, _nSellMcl;
    private Tree? _nTree;
    private Label? _nPageText, _nBuyStatus, _nSellStatus, _nSlotName;
    private BaseButton? _nBuyBtn, _nSellBtn, _nCancelBtn;
    private LineEdit? _nSearch, _nMinGold, _nMinSilver, _nMaxGold,
        _nMaxSilver, _nMinLv, _nMaxLv, _nPriceGold, _nPriceSilver;
    private Label? _nTaxGold, _nTaxSilver;
    private readonly List<(BaseButton Btn, int[] Tiers)> _nQual = new();
    private ItemSlotCell? _nSlot;

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("auctionwnd.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            // Géométrie native : UnifiedAreaRect 728×498 (min 630×460).
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -364, -249, 364, 249);
            _native = BuildNative(hud!);
            if (!_native)
            {
                var close = TopLeftVitals.Find<BaseButton>(_window,
                    "closeBtn");
                if (close != null) close.Pressed += Close;
            }
        }
        if (!_native) BuildContent();
        DragHelper.EnsureTitleBar(_window ?? this, "Auction House");
        DragHelper.Persist(_window ?? this, "auction");
        Visible = false;
        AuctionState.Changed += OnAuctionChanged;
        AuctionState.Opened += OnOpened;
        VisibilityChanged += OnVisChanged;
        // --auction-drive : sonde de validation live (cf. --mail-drive)
        // — rejoue les chemins des clics : achat (115) → mise en
        // vente d'un vrai item du sac (117) → re-liste ventes (116) →
        // annulation (118) → requêtes finales (114/116). Mutations
        // réelles sur les données seed — réservé à la validation.
        var driveArgs = OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs());
        if (driveArgs.Any(a => a == "--auction-drive"))
            AddChild(new DriveProbe(this));
    }

    public override void _ExitTree()
    {
        AuctionState.Changed -= OnAuctionChanged;
        AuctionState.Opened -= OnOpened;
        PlayerVitals.GoldChanged -= RefreshMoney;
        base._ExitTree();
    }

    private void OnOpened() => Visible = true;

    /// <summary>Quand la fenêtre devient visible : interroge le
    /// serveur (114 + 116) si une session zone existe.</summary>
    private void OnVisChanged()
    {
        if (!IsVisibleInTree()) return;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true) QueryAll();
        RefreshLists();
    }

    private void OnAuctionChanged()
    {
        if (!Visible && AuctionState.IsOpen) Visible = true;
        if (!AuctionState.IsOpen) { Visible = false; return; }
        RefreshLists();
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        _tabs = new TabContainer { Name = "__auctiontabs" };
        _tabs.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _tabs.OffsetLeft = 8; _tabs.OffsetTop = 30;
        _tabs.OffsetRight = -8; _tabs.OffsetBottom = -40;
        _tabs.MouseFilter = MouseFilterEnum.Stop;
        host.AddChild(_tabs);

        BuildBuyTab();
        BuildSellTab();

        _status = new Label { Name = "__aucstatus" };
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        _status.Position = new Vector2(10, -34);
        _status.Size = new Vector2(500, 26);
        _status.AddThemeFontSizeOverride("font_size", 10);
        host.AddChild(_status);
    }

    /// <summary>Onglet « Parcourir » — catalogue 425 + achat 115.</summary>
    private void BuildBuyTab()
    {
        var page = new VBoxContainer { Name = "Browse" };
        _tabs!.AddChild(page);

        var top = new HBoxContainer();
        page.AddChild(top);
        top.AddChild(new Label
        {
            Text = UiTextEn.Translate("Search:"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _search = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("item name (local filter — the server ") +
                UiTextEn.Translate("ignores the 114 filters)"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _search.TextChanged += _ => RefreshLists();
        top.AddChild(_search);
        var refresh = new Button { Text = UiTextEn.Translate("Refresh") };
        refresh.Pressed += QueryAll;
        top.AddChild(refresh);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        page.AddChild(scroll);
        _buyList = new VBoxContainer { Name = "__buylist" };
        _buyList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_buyList);

        _buyBtn = new Button { Text = UiTextEn.Translate("Buy (listed price)") };
        _buyBtn.Pressed += BuySelected;
        _buyBtn.Visible = false;
        page.AddChild(_buyBtn);
    }

    /// <summary>Onglet « Mes ventes » — liste 428 + annulation 118 +
    /// formulaire de mise en vente 117.</summary>
    private void BuildSellTab()
    {
        var page = new HBoxContainer { Name = "My Sales" };
        _tabs!.AddChild(page);

        var left = new VBoxContainer();
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.AddChild(left);
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        left.AddChild(scroll);
        _sellList = new VBoxContainer { Name = "__selllist" };
        _sellList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_sellList);
        _cancelBtn = new Button { Text = UiTextEn.Translate("Cancel listing") };
        _cancelBtn.Pressed += CancelSelected;
        _cancelBtn.Visible = false;
        left.AddChild(_cancelBtn);

        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(260, 0);
        page.AddChild(right);
        right.AddChild(new Label { Text = UiTextEn.Translate("New listing") });
        var row = new HBoxContainer();
        right.AddChild(row);
        row.AddChild(new Label
        {
            Text = UiTextEn.Translate("Item:"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _sellSlot = new SellDropSlot();
        row.AddChild(_sellSlot);
        row.AddChild(new Label
        {
            Text = UiTextEn.Translate("(drag from bag)"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        right.AddChild(MakeRow(UiTextEn.Translate("Price:"),
            _priceEdit = new LineEdit { PlaceholderText = UiTextEn.Translate("gold") }));
        right.AddChild(MakeRow(UiTextEn.Translate("Buyout:"),
            _buyoutEdit = new LineEdit
            { PlaceholderText = UiTextEn.Translate("0 = no buyout") }));
        _sellStatus = new Label { Text = "" };
        _sellStatus.AddThemeFontSizeOverride("font_size", 10);
        right.AddChild(_sellStatus);
        var sell = new Button { Text = UiTextEn.Translate("Sell (48 h, 5% fee)") };
        sell.Pressed += SellSelected;
        right.AddChild(sell);
    }

    private static HBoxContainer MakeRow(string label, Control field)
    {
        var row = new HBoxContainer();
        var l = new Label
        {
            Text = label,
            CustomMinimumSize = new Vector2(110, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(l);
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(field);
        return row;
    }

    // ------------------------------------------------------------------
    // Listes
    // ------------------------------------------------------------------

    private void RefreshLists()
    {
        if (_native) { RefreshNative(); return; }
        RefreshBuy();
        RefreshSell();
    }

    private void RefreshBuy()
    {
        if (_buyList == null) return;
        foreach (var c in _buyList.GetChildren()) c.QueueFree();
        var q = (_search?.Text ?? "").Trim();
        var entries = AuctionState.BuyList;
        var shown = 0;
        foreach (var e in entries)
        {
            var name = ResolveName(e);
            if (q.Length > 0 && !name.Contains(q,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            shown++;
            _buyList.AddChild(MakeEntryRow(e, name, sel =>
            {
                _selBuyId = sel;
                UpdateButtons();
            }, _selBuyId));
        }
        if (shown == 0)
            _buyList.AddChild(new Label
            {
                Text = q.Length > 0 ? UiTextEn.Translate("(no results for this name)")
                    : UiTextEn.Translate("(no active listing)"),
            });
        if (_status != null)
            _status.Text = UiTextEn.Translate("%s1 listing(s) on sale — ").Replace("%s1", entries.Count.ToString()) +
                UiTextEn.Translate("purchase delivered by mail");
        UpdateButtons();
    }

    private void RefreshSell()
    {
        if (_sellList == null) return;
        foreach (var c in _sellList.GetChildren()) c.QueueFree();
        var entries = AuctionState.SellList;
        foreach (var e in entries)
            _sellList.AddChild(MakeEntryRow(e, ResolveName(e), sel =>
            {
                _selSellId = sel;
                UpdateButtons();
            }, _selSellId));
        if (entries.Count == 0)
            _sellList.AddChild(new Label
            { Text = UiTextEn.Translate("(no listing in progress)") });
        UpdateButtons();
    }

    /// <summary>Ligne d'annonce : icône + nom + prix + vendeur +
    /// temps restant (ExpireTime unix). Tooltip riche item.</summary>
    private Control MakeEntryRow(AuctionEntry e, string name,
        Action<int> onSel, int selId)
    {
        var row = new Button
        {
            Text = "  " + UiTextEn.Translate("%s1 — %s2 g").Replace("%s1", name).Replace("%s2", e.Price.ToString("N0")) + "   " +
                   $"({TimeLeft(e.ExpireTime)})",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 40),
        };
        row.AddThemeFontSizeOverride("font_size", 11);
        var def = ItemTableCache.Get(e.ItemId);
        if (def != null && def.Code.Length > 0)
        {
            var tex = AssetCatalog.GetTexture(
                $"UI/itemicon/{def.Code}.png");
            if (tex != null) row.Icon = tex;
        }
        if (e.AuctionId == selId)
            row.AddThemeColorOverride("font_color",
                new Color(1f, 0.92f, 0.6f));
        row.TooltipText = name;
        // Tooltip riche via un InventorySlot fabriqué (seul l'id
        // compte pour les stats t_item).
        var id = e.AuctionId;
        row.Pressed += () => onSel(id);
        return row;
    }

    /// <summary>Nom affiché : t_item FR en priorité — le champ wire est
    /// la clé LIKE côté serveur (Maker+nom c_item Big5, chinois). On
    /// conserve juste son suffixe « +N » de renforcement.</summary>
    private static string ResolveName(AuctionEntry e)
    {
        var name = TextResolver.ItemName(e.ItemId)
            ?? ItemTableCache.Get(e.ItemId)?.Name;
        if (name == null)
            return e.ItemName.Length > 0 ? e.ItemName : $"#{e.ItemId}";
        var plus = e.ItemName.LastIndexOf('+');
        return plus >= 0 ? name + e.ItemName[plus..] : name;
    }

    private static string TimeLeft(long expireUnix)
    {
        if (expireUnix <= 0) return "";
        var left = TimeSpan.FromSeconds(expireUnix -
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (left.TotalSeconds <= 0) return UiTextEn.Translate("expired");
        return left.TotalHours >= 1
            ? $"{(int)left.TotalHours}h{left.Minutes:D2}"
            : $"{left.Minutes}min";
    }

    private void UpdateButtons()
    {
        if (_buyBtn != null)
        {
            var e = AuctionState.BuyList
                .FirstOrDefault(x => x.AuctionId == _selBuyId);
            _buyBtn.Visible = e != null;
            if (e != null)
                _buyBtn.Text = UiTextEn.Translate("Buy %s1 —").Replace("%s1", ResolveName(e)) + " " +
                    $"{e.Price:N0}g";
        }
        if (_cancelBtn != null)
            _cancelBtn.Visible = AuctionState.SellList
                .Any(x => x.AuctionId == _selSellId);
    }

    // ------------------------------------------------------------------
    // Actions (114-118)
    // ------------------------------------------------------------------

    /// <summary>114 + 116 — interroge catalogue et mes ventes.</summary>
    private void QueryAll()
    {
        Send(new ZoneAuctionBuyQuery());
        Send(new ZoneAuctionSellQuery());
    }

    /// <summary>115 — achat direct (livraison par courrier).</summary>
    private void BuySelected()
    {
        if (_selBuyId < 0) return;
        AuctionState.PendingBuyId = _selBuyId;
        Send(new ZoneAuctionBuy { AuctionId = _selBuyId });
    }

    /// <summary>118 — annule une de mes annonces (retour courrier).</summary>
    private void CancelSelected()
    {
        if (_selSellId < 0) return;
        AuctionState.PendingCancelId = _selSellId;
        Send(new ZoneAuctionCancel { AuctionId = _selSellId });
    }

    /// <summary>117 — met en vente l'item déposé dans le slot.</summary>
    private void SellSelected()
    {
        var it = _native ? _nSlot?.Item : _sellSlot?.Item;
        if (it == null || (!_native && _priceEdit == null))
        {
            SetSellStatus(UiTextEn.Translate("Drop an item from your bag."));
            return;
        }
        var price = _native ? NativePrice() : ParseInt(_priceEdit!.Text);
        if (price < 1)
        {
            SetSellStatus(UiTextEn.Translate("Invalid price."));
            return;
        }
        Send(new ZoneAuctionSell
        {
            Bag = (short)it.Bag,
            Slot = (short)it.Slot,
            Price = price,
            Buyout = _native ? 0 : ParseInt(_buyoutEdit?.Text),
        });
        SetSellStatus(UiTextEn.Translate("Listing… (response 429)"));
    }

    private void SetSellStatus(string t)
    {
        if (_sellStatus != null) _sellStatus.Text = t;
        if (_nSellStatus != null) _nSellStatus.Text = t;
    }

    /// <summary>Sonde --auction-drive : pose item + prix comme le
    /// ferait un drop + saisie (chemin natif ou legacy).</summary>
    private void ProbeSetSell(InventorySlot it, int price, int buyout)
    {
        if (_native)
        {
            _nSlot?.SetItem(it);
            if (_nPriceGold != null) _nPriceGold.Text = (price / 1000).ToString();
            if (_nPriceSilver != null) _nPriceSilver.Text = (price % 1000).ToString();
            return;
        }
        _sellSlot?.SetItem(it);
        if (_priceEdit != null) _priceEdit.Text = price.ToString();
        if (_buyoutEdit != null) _buyoutEdit.Text = buyout.ToString();
    }

    private static int ParseInt(string? t) =>
        int.TryParse((t ?? "").Trim(), out var v) ? Math.Max(v, 0) : 0;

    private void Close()
    {
        AuctionState.Close();
        Visible = false;
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[auction] pas de session — {msg.Name}");
            return;
        }
        var task = s.SendZoneAsync(msg, CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[auction] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Données de démonstration hors-serveur (--hud-open sans
    /// session) : peuple l'état pour les captures de validation.</summary>
    public void EnsureDemo()
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            // Live : ouvre sans injecter de données factices — la
            // fenêtre affichera les vraies listes 425/428 (déjà
            // requêtées à l'apparition).
            AuctionState.OnOpenHouse(-1);
            return;
        }
        if (AuctionState.BuyList.Count > 0 ||
            AuctionState.SellList.Count > 0) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        AuctionState.SetMock(
            new AuctionEntry[]
            {
                new()
                {
                    AuctionId = 501, ItemId = 100151002,
                    ItemName = "Aspen Wand", Price = 12500,
                    SellerId = 42, ExpireTime = now + 36 * 3600,
                },
                new()
                {
                    AuctionId = 502, ItemId = 100201003,
                    ItemName = "Feral Hide Boots", Price = 890,
                    SellerId = 77, ExpireTime = now + 11 * 3600,
                },
                new()
                {
                    AuctionId = 503, ItemId = 100310104,
                    ItemName = "Potion of Focus ×10", Price = 3400,
                    SellerId = 12, ExpireTime = now + 47 * 3600,
                },
            },
            new AuctionEntry[]
            {
                new()
                {
                    AuctionId = 510, ItemId = 100120055,
                    ItemName = "Copper Ore", Price = 150,
                    SellerId = 1, ExpireTime = now + 20 * 3600,
                },
            });
    }


    // ------------------------------------------------------------------
    // Chemin natif — auctionwnd (TabControl) + auctionbuywnd +
    // auctionsellwnd injectés comme le client natif
    // ------------------------------------------------------------------

    private static Control? Leaf(Node? root, string leaf)
    {
        if (root == null) return null;
        foreach (var n in root.FindChildren("*", "Control", true, false))
        {
            var nm = n.Name.ToString();
            if (nm == leaf || nm.EndsWith("_" + leaf) ||
                nm.EndsWith("/" + leaf))
                return n as Control;
        }
        return null;
    }

    private static T? Leaf<T>(Node? root, string leaf) where T : class =>
        Leaf(root, leaf) as T;

    private static void SetT(Node? root, string leaf, string text)
    {
        switch (Leaf(root, leaf))
        {
            case Label l: l.Text = text; break;
            case Button bb: bb.Text = text; break;
            case LineEdit e: e.Text = text; break;
        }
    }

    private bool BuildNative(HudRoot hud)
    {
        var tabs = _window!.FindChildren("*", "TabContainer", true, false)
            .FirstOrDefault() as TabContainer;
        _buyPane = hud.Build("auctionbuywnd.layout");
        _sellPane = hud.Build("auctionsellwnd.layout");
        if (tabs == null || _buyPane == null || _sellPane == null)
        {
            _buyPane?.QueueFree(); _sellPane?.QueueFree();
            _buyPane = _sellPane = null;
            return false;
        }
        _buyPane.Name = UiTextEn.Translate("Buy");
        _sellPane.Name = UiTextEn.Translate("Sell");
        tabs.AddChild(_buyPane);
        tabs.AddChild(_sellPane);
        tabs.SetTabTitle(0, UiTextEn.Translate("Buy"));
        tabs.SetTabTitle(1, UiTextEn.Translate("Sell"));
        // Anciennes sous-fenêtres éventuelles du TabControl natif.
        foreach (var c in tabs.GetChildren())
            if (c != _buyPane && c != _sellPane && c is Control)
                c.QueueFree();

        BuildNativeBuy(_buyPane);
        BuildNativeSell(_sellPane);
        PlayerVitals.GoldChanged += RefreshMoney;
        RefreshMoney();
        return true;
    }

    private Label MakeStatus(Control host, float l, float t, float r,
        float b)
    {
        var lb = new Label { Name = "__aucstatus" };
        lb.AddThemeFontSizeOverride("font_size", 10);
        lb.ClipText = true;
        lb.MouseFilter = MouseFilterEnum.Ignore;
        lb.AnchorLeft = l; lb.AnchorTop = t;
        lb.AnchorRight = r; lb.AnchorBottom = b;
        lb.OffsetLeft = lb.OffsetTop = lb.OffsetRight = lb.OffsetBottom = 0;
        lb.VerticalAlignment = VerticalAlignment.Center;
        host.AddChild(lb);
        return lb;
    }

    private void BuildNativeBuy(Control p)
    {
        var bg = Leaf(p, "ItemListBG") ?? p;
        SetT(Leaf(p, "ItemListBG"), "TitleText",
            UiTextEn.Translate("Auction listings"));
        var sb = Leaf(p, "searchBG");
        SetT(sb, "TitleText", UiTextEn.Translate("Search items"));
        SetT(sb, "mark", UiTextEn.Translate("Quality"));
        SetT(sb, "MiniTitleText", UiTextEn.Translate("Minimum"));
        SetT(sb, "MaxTitleText", UiTextEn.Translate("Maximum"));
        SetT(sb, "titleText", UiTextEn.Translate("Level requirement"));
        SetT(sb, "Title", UiTextEn.Translate("Name"));
        SetT(sb, "researchBtn", UiTextEn.Translate("Search"));
        SetT(bg, "buyBtn", UiTextEn.Translate("Buy item"));
        SetT(bg, "closeBtn", UiTextEn.Translate("Close"));
        SetT(bg, "SilverText", "");
        SetT(bg, "PageText", "1/1");
        // Fond pâle des cadres border1-4 / arbre : texte sombre.
        var dark = new Color(0.16f, 0.10f, 0.26f);
        foreach (var lf in new[] { "mark", "MiniTitleText", "MaxTitleText",
            "titleText", "lv1", "lv2", "sign", "Title" })
            if (Leaf(sb, lf) is Label dl)
                dl.AddThemeColorOverride("font_color", dark);

        // Qualités — palette native Blanc/Vert/Bleu/Orange/Or/Violet
        // (tiers t_item : 0-1 blanc, 2 vert, 4 bleu, 3 orange, 5 or, 6 violet).
        void Q(string leaf, string fr, params int[] tiers)
        {
            if (Leaf(sb, leaf) is not BaseButton b) return;
            if (b is Button bt) bt.Text = UiTextEn.Translate(fr);
            b.ButtonPressed = false;
            b.Toggled += _ => { _page = 0; RefreshNative(); };
            _nQual.Add((b, tiers));
        }
        Q("WhitBtn", "White", 0, 1);
        Q("GreenBtn", "Green", 2);
        Q("buleBtn", "Blue", 4);
        Q("OrageBtn", "Orange", 3);
        Q("GoldBtn", "Gold", 5);
        Q("purpleBtn", "Purple", 6);

        _nMinGold = Leaf<LineEdit>(sb, "MiniGoldEdit");
        _nMinSilver = Leaf<LineEdit>(sb, "MiniSilverEdit");
        _nMaxGold = Leaf<LineEdit>(sb, "MaxGoldEdit");
        _nMaxSilver = Leaf<LineEdit>(sb, "MaxSilverText");
        _nMinLv = Leaf<LineEdit>(sb, "MniiLvEdit");
        _nMaxLv = Leaf<LineEdit>(sb, "MaxLvEdit");
        _nSearch = Leaf<LineEdit>(sb, "searchEdit");
        foreach (var e in new[] { _nMinGold, _nMinSilver, _nMaxGold,
            _nMaxSilver, _nMinLv, _nMaxLv })
        {
            if (e == null) continue;
            e.Text = "";
            e.AddThemeFontSizeOverride("font_size", 9);
            e.TextSubmitted += _ => ApplySearch();
        }
        if (_nSearch != null)
        {
            _nSearch.Text = "";
            _nSearch.PlaceholderText = UiTextEn.Translate("Item name");
            _nSearch.TextSubmitted += _ => ApplySearch();
        }
        if (Leaf<BaseButton>(sb, "researchBtn") is { } rs)
            rs.Pressed += ApplySearch;

        // Arbre des catégories (ItemListBG/Tree) — filtre local.
        _nTree = Leaf<Tree>(bg, "Tree");
        if (_nTree != null)
        {
            _nTree.Columns = 1;
            _nTree.HideRoot = true;
            foreach (var cn in new[] { "font_color", "font_selected_color",
                "font_hovered_color", "font_hovered_selected_color" })
                _nTree.AddThemeColorOverride(cn, dark);
            var root = _nTree.CreateItem();
            TreeItem? first = null;
            foreach (var (id, key) in new[] { (0, "All"), (1, "Weapons"),
                (2, "Armor"), (3, "Consumables"), (4, "Other") })
            {
                var it = _nTree.CreateItem(root);
                it.SetText(0, UiTextEn.Translate(key));
                it.SetMetadata(0, id);
                first ??= it;
            }
            first?.Select(0);
            _nTree.ItemSelected += () =>
            {
                var sel = _nTree.GetSelected();
                if (sel == null) return;
                _cat = sel.GetMetadata(0).AsInt32();
                _page = 0;
                RefreshNative();
            };
        }

        _nBuyMcl = Leaf<MclList>(bg, "listbox");
        _nBuyMcl?.SetColumns(new MclColumn[]
        {
            new("", 0.13f, 0f, 0),
            new(UiTextEn.Translate("Name"), 0.44f, 0f, 1),
            new("Niv.", 0.17f, 0f, 2),
            new("Prix", 0.26f, 0f, 3),
        });
        if (_nBuyMcl != null)
            _nBuyMcl.RowSelected += row =>
            {
                _selBuyId = (_nBuyMcl.RowTag(row) as AuctionEntry)
                    ?.AuctionId ?? -1;
                UpdateNativeButtons();
            };

        _nPageText = Leaf<Label>(bg, "PageText");
        void Pg(string leaf, Func<int> target)
        {
            if (Leaf<BaseButton>(bg, leaf) is { } b)
                b.Pressed += () => { _page = target(); RefreshNative(); };
        }
        Pg("LLBtn", () => 0);
        Pg("LBtn", () => _page - 1);
        Pg("RBtn", () => _page + 1);
        Pg("RRBtn", () => int.MaxValue / 2);   // borné au rafraîchissement

        _nBuyBtn = Leaf<BaseButton>(bg, "buyBtn");
        if (_nBuyBtn != null) _nBuyBtn.Pressed += BuySelected;
        if (Leaf<BaseButton>(bg, "closeBtn") is { } c) c.Pressed += Close;
        _nBuyStatus = MakeStatus(bg, 0.30f, 0.905f, 0.60f, 0.98f);
    }

    private void BuildNativeSell(Control p)
    {
        var bg = Leaf(p, "backgrand") ?? p;
        var form = Leaf(bg, "border");
        SetT(bg, "titile4", UiTextEn.Translate("New listing"));
        SetT(bg, "title1", UiTextEn.Translate("Items on auction"));
        SetT(bg, "silverText", "");
        SetT(bg, "goldText", "0");
        SetT(bg, "closeBTn", UiTextEn.Translate("Close"));
        SetT(bg, "cancleBtn", UiTextEn.Translate("Cancel listing"));
        SetT(form, "titile3", UiTextEn.Translate("Selling price"));
        SetT(form, "title2", UiTextEn.Translate("Deposit fee"));
        SetT(form, "SellBtn", UiTextEn.Translate("Sell item"));
        SetT(form, "SlotName", "");
        SetT(form, "taxGold", "0");
        SetT(form, "taxsilver", "0");
        _nSlotName = Leaf<Label>(form, "SlotName");
        _nTaxGold = Leaf<Label>(form, "taxGold");
        _nTaxSilver = Leaf<Label>(form, "taxsilver");
        _nPriceGold = Leaf<LineEdit>(form, "goldedit");
        _nPriceSilver = Leaf<LineEdit>(form, "silveredit");
        foreach (var e in new[] { _nPriceGold, _nPriceSilver })
            if (e != null)
            {
                e.Text = "";
                e.AddThemeFontSizeOverride("font_size", 9);
                e.TextChanged += _ => UpdateTax();
            }

        _nSlot = Leaf<ItemSlotCell>(form, "slot");
        if (_nSlot != null)
        {
            _nSlot.Clear();
            _nSlot.ItemDropped += (cell, d) =>
            {
                var it = InventoryWindow.ItemAt(
                    d.ContainsKey("bag") ? d["bag"].AsInt32() : 0,
                    d["slot"].AsInt32());
                cell.SetItem(it);
                UpdateSlotName();
            };
            _nSlot.RightClicked += cl => { cl.Clear(); UpdateSlotName(); };
        }

        _nSellMcl = Leaf<MclList>(bg, "list");
        _nSellMcl?.SetColumns(new MclColumn[]
        {
            new("", 0.10f, 0f, 0),
            new(UiTextEn.Translate("Name"), 0.38f, 0f, 1),
            new("Niv.", 0.12f, 0f, 2),
            new("Durée", 0.12f, 0f, 3),
            new("Prix", 0.28f, 0f, 4),
        });
        if (_nSellMcl != null)
            _nSellMcl.RowSelected += row =>
            {
                _selSellId = (_nSellMcl.RowTag(row) as AuctionEntry)
                    ?.AuctionId ?? -1;
                UpdateNativeButtons();
            };

        _nSellBtn = Leaf<BaseButton>(form, "SellBtn");
        if (_nSellBtn != null) _nSellBtn.Pressed += SellSelected;
        _nCancelBtn = Leaf<BaseButton>(bg, "cancleBtn");
        if (_nCancelBtn != null) _nCancelBtn.Pressed += CancelSelected;
        if (Leaf<BaseButton>(bg, "closeBTn") is { } c) c.Pressed += Close;
        _nSellStatus = MakeStatus(bg, 0.20f, 0.905f, 0.70f, 0.98f);
    }

    private static (string Gold, string Silver) Money(long m) =>
        ($"{m / 1000:#,0}", $"{m % 1000}");

    private static string MoneyText(long m)
    {
        var (g, s) = Money(m);
        return m / 1000 > 0
            ? UiTextEn.Translate("%s1 Gold %s2 Silver")
                .Replace("%s1", g).Replace("%s2", s)
            : UiTextEn.Translate("%s1 Silver").Replace("%s1", s);
    }

    /// <summary>Or du joueur (BorderTextLab goldText) + or de l'onglet
    /// achat (GoldText) ; l'argent est affiché dans silverText.</summary>
    private void RefreshMoney()
    {
        if (!_native) return;
        var (g, s) = Money(PlayerVitals.Gold);
        SetT(_buyPane, "GoldText", g);
        SetT(_buyPane, "SilverText", s);
        SetT(_sellPane, "goldText", g);
        SetT(_sellPane, "silverText", s);
    }

    private int NativePrice()
    {
        var g = ParseInt(_nPriceGold?.Text);
        var s = ParseInt(_nPriceSilver?.Text);
        return (int)Math.Min((long)g * 1000 + s, int.MaxValue);
    }

    private void UpdateTax()
    {
        var tax = (int)(NativePrice() * 5L / 100);
        if (_nTaxGold != null) _nTaxGold.Text = $"{tax / 1000:#,0}";
        if (_nTaxSilver != null) _nTaxSilver.Text = $"{tax % 1000}";
    }

    private void UpdateSlotName()
    {
        if (_nSlotName == null) return;
        var it = _nSlot?.Item;
        if (it == null || it.ItemId <= 0) { _nSlotName.Text = ""; return; }
        _nSlotName.Text = TextResolver.ItemName(it.ItemId)
            ?? ItemTableCache.Get(it.ItemId)?.Name ?? $"#{it.ItemId}";
        var q = ItemTableCache.Get(it.ItemId)?.Quality ?? 0;
        _nSlotName.AddThemeColorOverride("font_color",
            ItemTooltip.QualityColor(q));
    }

    private void ApplySearch() { _page = 0; RefreshNative(); }

    private static bool InCategory(int cat, ItemTable.ItemDef? d)
    {
        if (cat == 0) return true;
        if (d == null) return cat == 4;
        var k = d.Kind;
        var weapon = (k >= 0x11 && k <= 0x1e) || (k >= 0x3d && k <= 0x42);
        return cat switch
        {
            1 => weapon,
            2 => !weapon && d.EquipLoc != 0,
            3 => d.UseType is 1 or 2,
            _ => !weapon && d.EquipLoc == 0 && d.UseType is not (1 or 2),
        };
    }

    private IEnumerable<AuctionEntry> FilteredBuy()
    {
        var q = (_nSearch?.Text ?? "").Trim();
        long min = ParseInt(_nMinGold?.Text) * 1000L + ParseInt(_nMinSilver?.Text);
        long max = ParseInt(_nMaxGold?.Text) * 1000L + ParseInt(_nMaxSilver?.Text);
        int lmin = ParseInt(_nMinLv?.Text), lmax = ParseInt(_nMaxLv?.Text);
        var tiers = _nQual.Where(x => x.Btn.ButtonPressed)
            .SelectMany(x => x.Tiers).ToHashSet();
        foreach (var e in AuctionState.BuyList)
        {
            var d = ItemTableCache.Get(e.ItemId);
            if (!InCategory(_cat, d)) continue;
            if (q.Length > 0 && !ResolveName(e).Contains(q,
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (min > 0 && e.Price < min) continue;
            if (max > 0 && e.Price > max) continue;
            var lv = d?.RequiredLevel ?? 0;
            if (lmin > 0 && lv < lmin) continue;
            if (lmax > 0 && lv > lmax) continue;
            if (tiers.Count > 0 &&
                !tiers.Contains(ItemTipFormat.QualityTier(d?.Quality ?? 0)))
                continue;
            yield return e;
        }
    }

    private void FillRow(MclList m, AuctionEntry e, bool withTime)
    {
        var d = ItemTableCache.Get(e.ItemId);
        Texture2D? icon = d != null && d.Code.Length > 0
            ? AssetCatalog.GetTexture($"UI/itemicon/{d.Code}.png") : null;
        var cells = withTime
            ? new[] { "", ResolveName(e), (d?.RequiredLevel ?? 0).ToString(),
                TimeLeft(e.ExpireTime), MoneyText(e.Price) }
            : new[] { "", ResolveName(e), (d?.RequiredLevel ?? 0).ToString(),
                MoneyText(e.Price) };
        var row = m.AddRow(cells, icon, e);
        m.SetCellColor(row, 1,
            ItemTooltip.QualityColor(d?.Quality ?? 0));
    }

    private void RefreshNative()
    {
        if (_nBuyMcl != null)
        {
            var all = FilteredBuy().ToList();
            var pages = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
            _page = Math.Clamp(_page, 0, pages - 1);
            _nBuyMcl.ClearRows();
            foreach (var e in all.Skip(_page * PageSize).Take(PageSize))
                FillRow(_nBuyMcl, e, false);
            if (_nPageText != null) _nPageText.Text = $"{_page + 1}/{pages}";
            if (_nBuyStatus != null)
                _nBuyStatus.Text = all.Count == 0
                    ? UiTextEn.Translate("(no active listing)")
                    : UiTextEn.Translate("%s1 listing(s) on sale — ")
                        .Replace("%s1", all.Count.ToString()) +
                      UiTextEn.Translate("purchase delivered by mail");
            if (!all.Any(x => x.AuctionId == _selBuyId)) _selBuyId = -1;
        }
        if (_nSellMcl != null)
        {
            _nSellMcl.ClearRows();
            foreach (var e in AuctionState.SellList)
                FillRow(_nSellMcl, e, true);
            if (!AuctionState.SellList.Any(x => x.AuctionId == _selSellId))
                _selSellId = -1;
        }
        UpdateNativeButtons();
    }

    private void UpdateNativeButtons()
    {
        if (_nBuyBtn != null) _nBuyBtn.Disabled = _selBuyId < 0;
        if (_nCancelBtn != null) _nCancelBtn.Disabled = _selSellId < 0;
    }

    private void BuildFallback()
    {
        var p = new Panel { Name = "AuctionWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-350, -240);
        p.Size = new Vector2(700, 480);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Auction House");
        _window = p;
    }

    /// <summary>Slot de drop pour l'objet à vendre — même payload
    /// {bag,slot} que les ItemCell de l'inventaire ; clic droit =
    /// vider.</summary>
    private partial class SellDropSlot : PanelContainer
    {
        private readonly TextureRect _icon;
        private readonly Label _lbl;
        /// <summary>Item déposé (null = aucun).</summary>
        public InventorySlot? Item { get; private set; }

        /// <summary>Positionne l'item sans glisser-déposer (sonde
        /// <c>--auction-drive</c> de validation live).</summary>
        internal void SetItem(InventorySlot? it)
        { Item = it; Refresh(); }

        public SellDropSlot()
        {
            CustomMinimumSize = new Vector2(44, 44);
            AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.189f, 0.181f, 0.161f),
                BorderColor = new Color(0.503f, 0.483f, 0.43f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_icon);
            _lbl = new Label { Text = "+" };
            _lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _lbl.HorizontalAlignment = HorizontalAlignment.Center;
            _lbl.VerticalAlignment = VerticalAlignment.Center;
            AddChild(_lbl);
        }

        public override bool _CanDropData(Vector2 at, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            return d.ContainsKey("bag") && d.ContainsKey("slot");
        }

        public override void _DropData(Vector2 at, Variant data)
        {
            var d = data.AsGodotDictionary();
            Item = InventoryWindow.ItemAt(
                d["bag"].AsInt32(), d["slot"].AsInt32());
            Refresh();
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { Pressed: true,
                ButtonIndex: MouseButton.Right })
            {
                Item = null;
                Refresh();
                AcceptEvent();
            }
        }

        private void Refresh()
        {
            if (Item is { } it && it.ItemId > 0)
            {
                var def = ItemTableCache.Get(it.ItemId);
                _icon.Texture = def != null && def.Code.Length > 0
                    ? AssetCatalog.GetTexture(
                        $"UI/itemicon/{def.Code}.png")
                    : null;
                _lbl.Text = "";
                ItemTooltip.Bind(this, () => Item);
            }
            else
            {
                _icon.Texture = null;
                _lbl.Text = "+";
                TooltipText = "";
            }
        }
    }

    /// <summary>Sonde <c>--auction-drive</c> : rejoue la chaîne des
    /// actions utilisateur sur les vraies annonces — achat d'une
    /// annonce d'un autre vendeur (115), mise en vente du premier
    /// item du sac (117), re-liste de mes ventes (116), annulation
    /// de la première de mes annonces (118), puis requêtes finales
    /// (114/116). Chaque étape appelle la même méthode que le bouton
    /// UI correspondant : paquets strictement identiques à un clic.</summary>
    private sealed partial class DriveProbe : Node
    {
        private readonly AuctionWindow _w;
        private bool _fired;
        private int _retries;

        public DriveProbe(AuctionWindow w) { _w = w; }

        public override void _Ready()
        {
            AuctionState.Changed += OnChanged;
            // Re-émet 114 tant que le catalogue est vide (max 8×) —
            // tolère une réponse arrivée avant l'entrée complète.
            Later(Retry, 2.0);
        }

        /// <summary>Re-émet 114 tant que le catalogue est vide.</summary>
        private void Retry()
        {
            if (_fired || AuctionState.BuyList.Count > 0
                || _retries++ >= 8) return;
            GD.Print("[auction-drive] catalogue vide — " +
                "nouvelle requête 114");
            _w.Send(new ZoneAuctionBuyQuery());
            Later(Retry, 2.0);
        }

        public override void _ExitTree()
            => AuctionState.Changed -= OnChanged;

        private void OnChanged()
        {
            if (_fired || AuctionState.BuyList.Count == 0) return;
            _fired = true;
            Later(StepBuy, 0.5);
        }

        /// <summary>Étape 1 : achat d'une annonce d'un autre joueur
        /// (115) — l'item est livré par courrier côté serveur. La
        /// préférence va à un item_id ≤ 32767 : la colonne native
        /// player_mail.item_id est smallint (constaté en live —
        /// SettleAuctionPurchaseAsync plante au-delà).</summary>
        private void StepBuy()
        {
            var self = SessionCarry.Session?.SelectedCharId ?? 0;
            var e = AuctionState.BuyList.FirstOrDefault(
                        x => x.SellerId != self && x.ItemId <= 32767)
                    ?? AuctionState.BuyList.FirstOrDefault(
                        x => x.SellerId != self)
                    ?? AuctionState.BuyList[^1];
            GD.Print($"[auction-drive] achat annonce #{e.AuctionId} " +
                     $"item={e.ItemId} prix={e.Price}");
            _w._selBuyId = e.AuctionId;
            _w.BuySelected();                       // 115
            _sellTries = 0;
            Later(StepSell, 0.9);
        }

        /// <summary>Candidats de vente restants (items du sac).</summary>
        private List<InventorySlot>? _sellCandidates;
        private int _sellTries;

        /// <summary>Étape 2 : mise en vente (117) — essaie les items
        /// du sac jusqu'à un 429 res=0 (les items liés sont refusés
        /// silencieusement côté serveur — constaté en live).</summary>
        private void StepSell()
        {
            _sellCandidates ??= PlayerBag.Items
                .Where(i => i.ItemId > 0).ToList();
            var it = _sellCandidates.Count > _sellTries
                ? _sellCandidates[_sellTries++]
                : null;
            if (it == null)
            {
                GD.Print("[auction-drive] aucun item vendable — " +
                    "vente sautée");
                Later(StepCancel, 0.2);
                return;
            }
            AuctionState.LastSellResult = -1;
            _w.ProbeSetSell(it, 777, 888);
            GD.Print($"[auction-drive] vente item={it.ItemId} " +
                     $"bag={it.Bag} slot={it.Slot} " +
                     $"(essai {_sellTries})");
            _w.SellSelected();                      // 117
            // 429 attendu ~0.8 s ; sans lui → item refusé → suivant.
            Later(() =>
            {
                if (AuctionState.LastSellResult == 0)
                {
                    GD.Print($"[auction-drive] vente acceptée " +
                        $"(429 res=0, item={it.ItemId})");
                    Later(StepCancel, 0.5);
                }
                else
                {
                    GD.Print($"[auction-drive] item={it.ItemId} " +
                        "refusé/silence — essai suivant");
                    StepSell();
                }
            }, 0.9);
        }

        /// <summary>Étape 3 : re-liste mes ventes (116) puis annule la
        /// première (118) — l'item revient par courrier.</summary>
        private void StepCancel()
        {
            _w.Send(new ZoneAuctionSellQuery());    // 116
            Later(() =>
            {
                var e = AuctionState.SellList.FirstOrDefault();
                if (e == null)
                {
                    GD.Print("[auction-drive] aucune vente à " +
                        "annuler — étape sautée");
                    Later(StepEnd, 0.2);
                    return;
                }
                GD.Print($"[auction-drive] annulation annonce " +
                    $"#{e.AuctionId} item={e.ItemId}");
                _w._selSellId = e.AuctionId;
                _w.CancelSelected();                // 118
                Later(StepEnd, 0.9);
            }, 0.8);
        }

        /// <summary>Étape 4 : requêtes finales pour refléter l'état
        /// serveur après mutations.</summary>
        private void StepEnd()
        {
            _w.QueryAll();                          // 114 + 116
            GD.Print("[auction-drive] séquence terminée");
        }

        /// <summary>Enchaîne une étape après un délai (thread
        /// principal via SceneTreeTimer).</summary>
        private void Later(Action step, double sec)
            => GetTree().CreateTimer(sec).Timeout += step;
    }
}
