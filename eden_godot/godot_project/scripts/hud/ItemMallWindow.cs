using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre Item Mall (boutique cash) — layout natif
/// <c>ItemMall.layout</c> (FrameWindow <c>Root/ItemMal</c> ~762×546)
/// quand uidata est présent, sinon repli en code.
///
/// Structure natifs réutilisée (rf_game strings + layout) :
///   - <c>tab</c> TabControl 3 onglets (Global/Team/forever) — mappés
///     ici sur les <b>money_units</b> 1/2/3 (AP/bonus/forever gold) :
///     3 onglets ↔ 3 devises ↔ 3 soldes du bas (moneyText/
///     BonusmoneyText/ForeverGoldText) — <i>inférence documentée</i>,
///     la DLL native ne documente pas la correspondance ;
///   - <c>panel/list</c> Listbox haut-gauche = catégories
///     (<c>st1</c> « choose ») — alimentée par les <c>item_group</c>
///     distincts du catalogue pour l'unité courante ;
///   - <c>panel/itembox</c> Listbox bas-gauche = items à réclamer
///     (reçus 57 — clic = réclamer, <c>getallBtn</c> = tout) ;
///   - <c>panel/box</c> = grille d'articles paginée (boutons natifs
///     mostback/back/pageText/next/mostnext sous <c>box/list</c>),
///     une ligne <c>ItemMaillItem.layout</c> par article (icône, nom,
///     prix/promo, buyBtn/GiftBtn/detailBtn — même gabarit que le
///     client natif) ;
///   - <c>panel/toplist</c> = « hot » (st3) — promos du catalogue
///     (special_price ≠ 0) ;
///   - barre du bas : getallBtn, moneyText/BonusmoneyText/
///     ForeverGoldText, closeBtn.
///
/// Réseau (canal <b>monde</b> — voir ZoneItemMallMessages.cs pour les
/// preuves de formats) : à l'ouverture on envoie CW 25 (soldes) +
/// 24 (catalogue) + 15 (réclamables) ; 56 vrai → 17 (liste reçus) ;
/// achat = CW 23 <c>[s16 mu*100+grp][s16 idx][s16 amount][u16 version]
/// [str][str recipient][str]</c> ; réclamation = CW 18 [u32 receipt].
/// Le CZ 70 (marqueur natif « entrée UI réclamation ») est posé à
/// l'ouverture quand la zone est connectée (inférence documentée —
/// serveur : ShopOpType 0x2f).
///
/// Audit rf_itemmall : la DLL Reforged n'a PAS de protocole — elle
/// patche le chemin d'achat natif (pacing/shader-cache) ; le wire
/// reste le CW natif ci-dessus.
/// </summary>
public partial class ItemMallWindow : Control
{
    private Control? _window;
    private TabContainer? _tabs;
    private ItemList? _categories;
    private ItemList? _receivables;
    private VBoxContainer? _itemRows;
    private VBoxContainer? _hotRows;
    private Label? _pageText;
    private Label? _moneyText;
    private Label? _bonusText;
    private Label? _goldText;
    private Label? _status;
    private LineEdit? _giftEdit;

    private int _moneyUnit = 1;   // onglet tab → unité 1/2/3
    private int _group = -1;      // -1 = toutes catégories
    private int _page;
    private const int RowsPerPage = 8; // lignes ItemMaillItem (~52 px)

    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();
    private HudRoot? _hud;

    public override void _Ready()
    {
        _hud = HudExtensions.FindHud();
        _window = _hud?.Build("ItemMall.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            // UnifiedAreaRect natif {{0.0566,0},{0.0872,0},{+762},{+546}}
            // — on recentre sur le viewport Godot.
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -381, -273, 381, 273);
            var close = TopLeftVitals.Find<BaseButton>(_window,
                "closeBtn");
            if (close != null) close.Pressed += Close;
        }
        BuildContent();
        DragHelper.EnsureTitleBar(_window ?? this, "Item Mall");
        DragHelper.Persist(_window ?? this, "itemmall");
        Visible = false;
        ItemMallState.Changed += RefreshAll;
    }

    public override void _ExitTree()
    {
        ItemMallState.Changed -= RefreshAll;
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            { GD.PrintErr($"[itemmall] {ex}"); }
        }
    }

    // ------------------------------------------------------------------
    // Montage du contenu dans le chrome natif (ou le repli)
    // ------------------------------------------------------------------

    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        // Le contenu natif vit sous Root_ItemMal_VigourTab_panel.
        var panel = TopLeftVitals.Find<Control>(host, "VigourTab_panel");
        var content = panel ?? host;

        // ---- libellés / soldes natifs ----
        SetLabelText(host, "st1", "Category");
        SetLabelText(host, "st2", "Items");
        SetLabelText(host, "st3", "Hot");
        _moneyText = TopLeftVitals.Find<Label>(host, "moneyText");
        _bonusText = TopLeftVitals.Find<Label>(host, "BonusmoneyText");
        _goldText = TopLeftVitals.Find<Label>(host, "ForeverGoldText");

        // ---- onglets : money_unit = index+1 (inférence documentée) ----
        _tabs = TopLeftVitals.Find<TabContainer>(host, "ItemMal_tab");
        // M16 : libellés des onglets (ref : Item Mall | Loyalty Shop).
        if (_tabs != null)
        {
            var names = new[] { "Item Mall", "Loyalty Shop", "Special" };
            for (var i = 0; i < _tabs.GetTabCount(); i++)
                _tabs.SetTabTitle(i, UiTextEn.Translate(names[Math.Min(i, names.Length - 1)]));
        }
        if (_tabs != null)
            _tabs.TabChanged += t =>
            {
                _moneyUnit = (int)t + 1;
                _group = -1;
                _page = 0;
                RefreshAll();
            };

        // ---- catégories : la Listbox native panel/list ----
        _categories = TopLeftVitals.Find<ItemList>(host, "panel_list");
        if (_categories == null)
        {
            _categories = new ItemList { Name = "__categories" };
            AnchorRect(_categories, content,
                0.01626f, 0.09292f, 0.176152f, 0.4469f);
            content.AddChild(_categories);
        }
        _categories.AddThemeFontSizeOverride("font_size", 10);
        _categories.ItemSelected += OnCategorySelected;
        // Libellés de colonne native « Global/Team/forever » parasites.
        foreach (var n0 in host.FindChildren("*", "Label", true, false))
            if (n0 is Label l0 && (l0.Text is "Global" or "Team" or "forever"
                    || l0.Text == UiTextEn.Translate("Global")
                    || l0.Text == UiTextEn.Translate("Team")
                    || l0.Text == UiTextEn.Translate("forever")))
                l0.Visible = false;

        // ---- réclamables : la Listbox native panel/itembox ----
        _receivables = TopLeftVitals.Find<ItemList>(host, "panel_itembox");
        if (_receivables != null)
            // Cache les 15 SlotImage vides sp1..sp15 du layout.
            foreach (var c in _receivables.GetChildren())
                if (c is CanvasItem ci) ci.Visible = false;
        if (_receivables == null)
        {
            _receivables = new ItemList { Name = "__receivables" };
            AnchorRect(_receivables, content,
                0.0162602f, 0.473451f, 0.176152f, 0.893805f);
            content.AddChild(_receivables);
        }
        _receivables.IconMode = ItemList.IconModeEnum.Left;
        _receivables.MaxColumns = 1;
        _receivables.AddThemeFontSizeOverride("font_size", 9);
        _receivables.ItemSelected += OnReceiptSelected;
        _receivables.TooltipText =
            UiTextEn.Translate("Purchased items waiting — click to claim.");

        // ---- grille d'articles : dans la Border « box » (milieu) ----
        var box = TopLeftVitals.Find<Control>(host, "panel_box");
        var rowsHost = box ?? content;
        _itemRows = new VBoxContainer { Name = "__mallrows" };
        if (box != null)
            AnchorRect(_itemRows, rowsHost, 0.01f, 0.01f, 0.99f, 0.92f);
        else
            AnchorRect(_itemRows, rowsHost, 0.19f, 0.09f, 0.71f, 0.86f);
        _itemRows.AddThemeConstantOverride("separation", 2);
        rowsHost.AddChild(_itemRows);

        // ---- pagination native sous box/list ----
        // Les feuilles sont cherchées par motif « *leaf* » : il faut
        // le préfixe box_list_ pour distinguer backBtn/nextBtn de
        // mostbackBtn/mostnextBtn (sinon le motif « backBtn » tombe
        // sur mostbackBtn, déclaré avant dans le layout).
        _pageText = TopLeftVitals.Find<Label>(host, "pageText");
        WirePageBtn(host, "box_list_mostbackBtn", () => _page = 0);
        WirePageBtn(host, "box_list_backBtn", () => _page--);
        WirePageBtn(host, "box_list_nextBtn", () => _page++);
        WirePageBtn(host, "box_list_mostnextBtn",
            () => _page = int.MaxValue);

        // ---- « hot » : promos dans la Border toplist (droite) ----
        var top = TopLeftVitals.Find<Control>(host, "toplist");
        _hotRows = new VBoxContainer { Name = "__mallhot" };
        if (top != null)
        {
            AnchorRect(_hotRows, top, 0.04f, 0.04f, 0.96f, 0.96f);
            top.AddChild(_hotRows);
        }
        else
        {
            AnchorRect(_hotRows, content, 0.72f, 0.09f, 0.99f, 0.89f);
            content.AddChild(_hotRows);
        }
        _hotRows.AddThemeConstantOverride("separation", 2);

        // ---- boutons bas de fenêtre ----
        var getall = TopLeftVitals.Find<BaseButton>(host, "getallBtn");
        if (getall != null)
        {
            if (getall is Button gb) gb.Text = UiTextEn.Translate("Claim All");
            getall.Pressed += ClaimAll;
        }
        // passBtn (mot de passe boutique) / saveBtn (recharge) : aucun
        // paquet serveur câblé → masqués plutôt que des boutons morts.
        foreach (var dead in new[] { "passBtn", "saveBtn" })
            if (TopLeftVitals.Find<Control>(host, dead) is { } db0)
                db0.Visible = false;
        // getBtn natif = réclame le reçu sélectionné dans itembox.
        var get = TopLeftVitals.Find<BaseButton>(host, "getBtn");
        if (get != null)
        {
            if (get is Button gb2) gb2.Text = UiTextEn.Translate("Claim");
            get.Pressed += () =>
            {
                if (_receivables == null) return;
                var sel = _receivables.GetSelectedItems();
                if (sel.Length > 0) OnReceiptSelected(sel[0]);
            };
        }
        // Gift : ligne de saisie du destinataire (cadeau → Field6 du
        // 23) affichée sous la zone articles.
        _giftEdit = new LineEdit
        {
            Name = "__gift",
            PlaceholderText = UiTextEn.Translate("gift to: character name…"),
            Visible = false,
            CustomMinimumSize = new Vector2(0, 22),
        };
        _giftEdit.AddThemeFontSizeOverride("font_size", 11);
        if (box != null)
        {
            AnchorRect(_giftEdit, box, 0.02f, 0.93f, 0.30f, 0.985f);
            box.AddChild(_giftEdit);
        }

        // ---- statut (bas droite, avant closeBtn) ----
        _status = new Label
        {
            Name = "__mallstatus",
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _status.AddThemeFontSizeOverride("font_size", 10);
        AnchorRect(_status, content, 0.47f, 0.92f, 0.86f, 0.98f);
        content.AddChild(_status);

        RefreshAll();
    }

    private void BuildFallback()
    {
        var p = new Panel { Name = "Root_ItemMal" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-381, -273);
        p.Size = new Vector2(762, 546);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Item Mall");
        var close = new Button { Text = UiTextEn.Translate("Close") };
        close.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        close.Position = new Vector2(-100, -40);
        close.Size = new Vector2(88, 28);
        close.Pressed += Close;
        p.AddChild(close);
        _window = p;
    }

    /// <summary>Ancre proportionnelle relative — les UAR du layout
    /// natif sont exprimés en fractions du parent.</summary>
    private static void AnchorRect(Control c, Control parent,
        float l, float t, float r, float b)
    {
        c.AnchorLeft = l;
        c.AnchorTop = t;
        c.AnchorRight = r;
        c.AnchorBottom = b;
        c.OffsetLeft = c.OffsetTop = c.OffsetRight = c.OffsetBottom = 0;
        c.MouseFilter = Control.MouseFilterEnum.Stop;
    }

    private static void SetLabelText(Control host, string leaf,
        string text)
    {
        if (TopLeftVitals.Find<Label>(host, leaf) is { } l)
            l.Text = text;
    }

    /// <summary>Écrit un texte dans une sous-fenêtre d'une ligne
    /// <c>ItemMaillItem</c> (StaticText natifs → Label).</summary>
    private static void SetRowText(Control row, string leaf,
        string text)
    {
        if (TopLeftVitals.Find<Label>(row, leaf) is { } l)
            l.Text = text;
    }

    private void WirePageBtn(Control host, string leaf, Action act)
    {
        var b = TopLeftVitals.Find<BaseButton>(host, leaf);
        if (b != null) b.Pressed += () => { act(); ClampPage(); RefreshGrid(); };
    }

    // ------------------------------------------------------------------
    // Rafraîchissement
    // ------------------------------------------------------------------

    private void RefreshAll()
    {
        RefreshBalances();
        RefreshCategories();
        RefreshGrid();
        RefreshReceipts();
        RefreshHot();
        if (_status != null) _status.Text = ItemMallState.LastStatus;
    }

    private void RefreshBalances()
    {
        if (_moneyText != null)
            _moneyText.Text = $"{ItemMallState.Points}";
        if (_bonusText != null)
            _bonusText.Text = $"{ItemMallState.Bonus}";
        if (_goldText != null)
            // money_unit 3 n'a pas de solde dédié serveur (rabattu
            // sur pvalues — ItemMallHandlers.cs:176-178).
            _goldText.Text = "—";
    }

    private List<ItemMallEntry> Filtered()
    {
        var q = ItemMallState.Entries.Values
            .Where(e => e.MoneyUnit == _moneyUnit);
        if (_group >= 0)
            q = q.Where(e => e.Group == _group);
        return q.OrderBy(e => e.Group).ThenBy(e => e.Index).ToList();
    }

    private void RefreshCategories()
    {
        if (_categories == null) return;
        var groups = ItemMallState.Entries.Values
            .Where(e => e.MoneyUnit == _moneyUnit)
            .Select(e => e.Group)
            .Distinct().OrderBy(g => g).ToList();
        _categories.Clear();
        _categories.AddItem(UiTextEn.Translate("All"));
        foreach (var g in groups)
            _categories.AddItem(UiTextEn.Translate(CategoryName(g)));
        // Sélection courante → index (0 = All).
        var sel = _group < 0 ? 0 : groups.IndexOf(_group) + 1;
        if (sel >= 0 && sel < _categories.ItemCount)
            _categories.Select(sel);
    }

    /// <summary>Noms de catégories — pas de table de noms groupes
    /// prouvée côté client ; 8 = groupe « limited » natif
    /// (num_limit/compte — ItemMallHandlers.cs:192), 50/99 usuels
    /// « special » du seed server_us.</summary>
    private static string CategoryName(int g) => g switch
    {
        // Ordre de la ref ItemMallPanel.png (index de groupe inféré).
        1 => "Featured", 2 => "Crafting Items", 3 => "Pets & Mounts",
        4 => "Gear", 5 => "Boosts", 6 => "Fashion", 7 => "Colorful Dye",
        8 => "Limited Item",
        50 => "Special",
        99 => "Special",
        _ => $"Group {g}",
    };

    private void OnCategorySelected(long index)
    {
        var groups = ItemMallState.Entries.Values
            .Where(e => e.MoneyUnit == _moneyUnit)
            .Select(e => e.Group)
            .Distinct().OrderBy(g => g).ToList();
        _group = index <= 0 ? -1 : groups[(int)index - 1];
        _page = 0;
        RefreshGrid();
    }

    private void ClampPage()
    {
        var items = Filtered();
        var maxPage = Math.Max(0,
            (items.Count - 1) / RowsPerPage);
        _page = Math.Clamp(_page, 0, maxPage);
    }

    /// <summary>Lignes d'articles — gabarit <c>ItemMaillItem.layout</c>
    /// quand disponible (icône/nom/prix/promo/buy/gift/detail natifs),
    /// sinon ligne en code.</summary>
    private void RefreshGrid()
    {
        if (_itemRows == null) return;
        foreach (var c in _itemRows.GetChildren()) c.QueueFree();
        var items = Filtered();
        ClampPage();
        var pageItems = items.Skip(_page * RowsPerPage)
            .Take(RowsPerPage).ToList();
        var maxPage = Math.Max(1,
            (items.Count + RowsPerPage - 1) / RowsPerPage);
        if (_pageText != null)
            _pageText.Text = $"{_page + 1}/{maxPage}";
        foreach (var e in pageItems)
            _itemRows.AddChild(BuildEntryRow(e));
        if (pageItems.Count == 0)
            _itemRows.AddChild(new Label
            {
                Text = ItemMallState.Version == 0
                    ? UiTextEn.Translate("Catalog pending — see status below.")
                    : UiTextEn.Translate("(no items in this category)"),
            });
    }

    private Control BuildEntryRow(ItemMallEntry e)
    {
        var row = _hud?.Build("ItemMaillItem.layout");
        if (row == null) return BuildEntryRowFallback(e);

        // itemslot : SlotImage → TextureRect — pose la texture item.
        var slot = TopLeftVitals.Find<TextureRect>(row, "itemslot");
        if (slot != null)
        {
            var tex = ItemTexture(e.ItemId);
            if (tex != null) slot.Texture = tex;
        }
        SetRowText(row, "ItemDeatilText",
            MallName(e.ItemId));
        // Prix : plein (barré si promo) + promo.
        var hasPromo = e.SpecialPrice != 0 &&
            e.SpecialPrice < e.Point;
        SetRowText(row, "ItemPointext",
            hasPromo ? $"{e.Point}" : $"{e.EffectivePrice}");
        var del = TopLeftVitals.Find<CanvasItem>(row,
            "ItemPointext_delete");
        if (del != null) del.Visible = hasPromo;
        var spec = TopLeftVitals.Find<CanvasItem>(row, "SpecilIcon");
        if (spec != null) spec.Visible = hasPromo;
        SetRowText(row, "SpecilPointext",
            hasPromo ? $"{e.SpecialPrice}" : "");
        // Tag promo natif (rouge, sur le slot) : texte statique
        // table UI +0x14 — visible seulement si prix promo.
        SetRowText(row, "specilText",
            hasPromo ? UiTextEn.Translate("Promo") : "");
        var sell = TopLeftVitals.Find<Label>(row, "SellDateText");
        if (sell != null)
        {
            // « en vente » (cas normal) chevauchait le nom : on ne
            // montre que les états exceptionnels (début/fin de vente).
            var st = SellText(e);
            sell.Text = st == UiTextEn.Translate("on sale") ? "" : st;
        }

        var buy = TopLeftVitals.Find<BaseButton>(row, "buyBtn");
        if (buy != null)
        {
            if (buy is Button bb) { bb.Text = UiTextEn.Translate("Buy"); EeSkin.Grey(bb, 11); }
            buy.TooltipText = BuyTooltip(e);
            buy.Pressed += () => Buy(e, gift: false);
        }
        var gift = TopLeftVitals.Find<BaseButton>(row, "GiftBtn");
        if (gift != null)
        {
            if (gift is Button gb) { gb.Text = UiTextEn.Translate("Gift"); EeSkin.Grey(gb, 11); }
            gift.TooltipText = UiTextEn.Translate("Buy as gift — set the recipient name ") +
                UiTextEn.Translate("in the 'gift to' box first.");
            gift.Pressed += () => Buy(e, gift: true);
        }
        var detail = TopLeftVitals.Find<BaseButton>(row, "detailBtn");
        if (detail != null)
        {
            if (detail is Button db) { db.Text = UiTextEn.Translate("Stack"); EeSkin.Grey(db, 11); }
            detail.TooltipText = UiTextEn.Translate("Details");
            detail.Pressed += () => ShowDetail(e);
        }
        return row;
    }

    /// <summary>Repli ligne en code si ItemMaillItem.layout absent.</summary>
    private Control BuildEntryRowFallback(ItemMallEntry e)
    {
        var row = new HBoxContainer
        {
            Name = "__mrow",
            CustomMinimumSize = new Vector2(0, 40),
        };
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(36, 36),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        var tex = ItemTexture(e.ItemId);
        if (tex != null) icon.Texture = tex;
        row.AddChild(icon);
        var name = MallName(e.ItemId);
        var qty = e.ItemNum > 1 ? $" ×{e.ItemNum}" : "";
        var lbl = new Label
        {
            Text = $"{name}{qty}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = BuyTooltip(e),
        };
        lbl.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(lbl);
        var hasPromo = e.SpecialPrice != 0 && e.SpecialPrice < e.Point;
        var price = new Label
        {
            Text = hasPromo
                ? $"~{e.Point}~ {e.SpecialPrice}"
                : $"{e.EffectivePrice}",
            TooltipText = SellText(e),
        };
        price.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(price);
        var buy = new Button { Text = UiTextEn.Translate("Buy"), TooltipText = BuyTooltip(e) };
        buy.Pressed += () => Buy(e, gift: false);
        row.AddChild(buy);
        var gift = new Button
        {
            Text = UiTextEn.Translate("Gift"),
            TooltipText = UiTextEn.Translate("Buy as gift — set 'gift to' box first."),
        };
        gift.Pressed += () => Buy(e, gift: true);
        row.AddChild(gift);
        return row;
    }

    private void RefreshReceipts()
    {
        if (_receivables == null) return;
        _receivables.Clear();
        foreach (var r in ItemMallState.Receipts)
        {
            var name = MallName(r.ItemId);
            var i = _receivables.AddItem(
                $"  {name} ×{r.Quantity} — {r.PurchaseTime}");
            _receivables.SetItemTooltip(i,
                $"receipt #{r.Id} — click to claim (CW 18)");
            var tex = ItemTexture(r.ItemId);
            if (tex != null) _receivables.SetItemIcon(i, tex);
        }
        if (ItemMallState.Receipts.Count == 0)
            _receivables.AddItem(ItemMallState.AnythingReceivable
                ? UiTextEn.Translate("  (list pending…)")
                : UiTextEn.Translate("  (nothing to claim)"));
    }

    private void RefreshHot()
    {
        if (_hotRows == null) return;
        foreach (var c in _hotRows.GetChildren()) c.QueueFree();
        // « hot » = entrées en promo du filtre courant (inférence —
        // le critère natif du toplist n'est pas documenté par la DLL).
        var hot = Filtered().Where(e => e.SpecialPrice != 0 &&
            e.SpecialPrice < e.Point).Take(6).ToList();
        foreach (var e in hot)
        {
            var name = MallName(e.ItemId);
            var b = new Button
            {
                Text = $"{name}\n{e.SpecialPrice} " + UiTextEn.Translate("pts"),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 44),
            };
            b.AddThemeFontSizeOverride("font_size", 10);
            b.ClipText = true;
            var dark = new Color(0.16f, 0.1f, 0.05f);
            foreach (var c0 in new[] { "font_color", "font_hover_color",
                         "font_pressed_color", "font_focus_color" })
                b.AddThemeColorOverride(c0, dark);
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.93f, 0.9f, 0.84f),
                BorderColor = new Color(0.55f, 0.45f, 0.3f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            };
            b.AddThemeStyleboxOverride("normal", sb);
            b.AddThemeStyleboxOverride("hover", sb);
            b.AddThemeStyleboxOverride("pressed", sb);
            var entry = e;
            b.Pressed += () =>
            {
                _group = entry.Group;
                _page = 0;
                RefreshCategories();
                RefreshGrid();
            };
            _hotRows.AddChild(b);
        }
        if (hot.Count == 0)
            _hotRows.AddChild(new Label
            {
                Text = UiTextEn.Translate("(no sales)"),
                CustomMinimumSize = new Vector2(0, 20),
            });
    }

    // ------------------------------------------------------------------
    // Actions C→W
    // ------------------------------------------------------------------

    private void OnReceiptSelected(long index)
    {
        if (index < 0 || index >= ItemMallState.Receipts.Count) return;
        var receipt = ItemMallState.Receipts[(int)index];
        SendWorld(new WorldItemMallReceiveAttempt
        { ReceiptId = receipt.Id });
        ItemMallState.SetStatus(UiTextEn.Translate("Claiming #%s1…").Replace("%s1", receipt.Id.ToString()));
    }

    private void ClaimAll()
    {
        foreach (var r in ItemMallState.Receipts.ToList())
            SendWorld(new WorldItemMallReceiveAttempt
            { ReceiptId = r.Id });
        if (ItemMallState.Receipts.Count > 0)
            ItemMallState.SetStatus(
                UiTextEn.Translate("Claiming %s1 item(s)…").Replace("%s1", ItemMallState.Receipts.Count.ToString()));
    }

    /// <summary>NCID 23 — achat. gift=true → Field6 = nom du
    /// destinataire (mail_name côté serveur).</summary>
    private void Buy(ItemMallEntry e, bool gift)
    {
        var recipient = "";
        if (gift)
        {
            if (_giftEdit != null && !_giftEdit.Visible)
            {
                _giftEdit.Visible = true;
                ItemMallState.SetStatus(
                    UiTextEn.Translate("Type the recipient name then click Gift again."));
                return;
            }
            recipient = _giftEdit?.Text.Trim() ?? "";
            if (recipient.Length == 0)
            {
                ItemMallState.SetStatus(UiTextEn.Translate("Gift needs a recipient name."));
                return;
            }
        }
        SendWorld(new WorldItemMallBuyRequest
        {
            Key = (short)(e.MoneyUnit * 100 + e.Group),
            Index = (short)e.Index,
            Amount = 1,
            Version = ItemMallState.Version,
            Recipient = recipient,
        });
        ItemMallState.SetStatus(
            UiTextEn.Translate("Buying %s1…").Replace("%s1", MallName(e.ItemId)));
    }

    private void ShowDetail(ItemMallEntry e)
    {
        var parts = new List<string>
        {
            $"{MallName(e.ItemId)} — " +
            UiTextEn.Translate("group %s1 idx %s2 mu %s3").Replace("%s1", e.Group.ToString()).Replace("%s2", e.Index.ToString()).Replace("%s3", e.MoneyUnit.ToString()),
            UiTextEn.Translate("price %s1 promo %s2 qty/pack %s3 lvl≥%s4").Replace("%s1", e.Point.ToString()).Replace("%s2", e.SpecialPrice.ToString()).Replace("%s3", e.ItemNum.ToString()).Replace("%s4", e.LevelLimit.ToString()),
        };
        if (e.NumLimit > 0)
            parts.Add(UiTextEn.Translate("limit %s1/account — bought %s2").Replace("%s1", e.NumLimit.ToString()).Replace("%s2", e.Sold.ToString()));
        if (e.Note.Length > 0) parts.Add(UiTextEn.Translate("note: %s1").Replace("%s1", e.Note));
        if (e.FortuneBag.Length > 0)
            parts.Add(UiTextEn.Translate("fortune bag: %s1").Replace("%s1", e.FortuneBag));
        var d = TextResolver.ItemDescription(e.ItemId);
        if (d != null) parts.Add(d);
        ItemMallState.SetStatus(string.Join(" | ", parts));
    }

    private string BuyTooltip(ItemMallEntry e)
    {
        var lim = e.NumLimit > 0
            ? " " + UiTextEn.Translate("— limit %s1 (bought %s2)").Replace("%s1", e.NumLimit.ToString()).Replace("%s2", e.Sold.ToString()) : "";
        var lvl = e.LevelLimit > 0 ? " " + UiTextEn.Translate("— lvl %s1+").Replace("%s1", e.LevelLimit.ToString()) : "";
        var d = TextResolver.ItemDescription(e.ItemId);
        return $"{SellText(e)}{lim}{lvl}" +
            (d != null ? $"\n{d}" : "");
    }

    private static string SellText(ItemMallEntry e)
    {
        // sell_date/not_sell_date sont des epochs (serveur
        // ItemMallPackets) — 0 = toujours en vente.
        if (e.SellDate != 0 &&
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() < e.SellDate)
            return UiTextEn.Translate("not yet on sale");
        if (e.NotSellDate != 0)
            return UiTextEn.Translate("sale ends %s1").Replace("%s1",
                DateTimeOffset.FromUnixTimeSeconds(e.NotSellDate).ToString("yyyy-MM-dd"));
        return UiTextEn.Translate("on sale");
    }

    /// <summary>Nom d'article : table texte (t_itemmall/t_item), sinon
    /// la définition c_item/c_itemmall, sinon « Objet #id » ; passé par
    /// la table EN→FR (la table FR garde quelques noms anglais).</summary>
    private static string MallName(int itemId)
    {
        var n = TextResolver.ItemName(itemId);
        if (string.IsNullOrWhiteSpace(n))
            n = InventoryWindow.LookupDef(itemId)?.Name;
        if (string.IsNullOrWhiteSpace(n))
            return UiTextEn.Translate("Item") + $" #{itemId}";
        return UiTextEn.Translate(n);
    }

    private static Texture2D? ItemTexture(int itemId)
    {
        var def = ItemTableCache.Get(itemId);
        if (def == null || def.Code.Length == 0) return null;
        return AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png");
    }

    // ------------------------------------------------------------------
    // Ouverture / session
    // ------------------------------------------------------------------

    /// <summary>Ouvre/ferme la fenêtre (menu ItemMallBtn).</summary>
    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    /// <summary>Montre la fenêtre et lance la séquence d'ouverture
    /// native : CW 25 (soldes) + 24 (catalogue) + 15 (réclamables),
    /// puis CZ 70 (marqueur réclamation) si la zone est connectée.</summary>
    public void Open()
    {
        Visible = true;
        var s = SessionCarry.Session;
        if (s?.World?.IsConnected == true)
        {
            SendWorld(new WorldItemMallPointInfoQuery());
            SendWorld(new WorldItemMallListQuery
            { Version = ItemMallState.Version });
            SendWorld(new WorldItemMallAnythingQuery());
            // CZ 70 — marqueur natif « UI réclamation ouverte »
            // (ShopOpType 0x2f, annulation = succès côté serveur).
            if (s.Zone?.IsConnected == true)
                _ = s.SendZoneAsync(new ZoneItemMallReceivableEnter(),
                    CancellationToken.None).ContinueWith(t =>
                        GD.PrintErr($"[itemmall] CZ70 : {t.Exception}"),
                        TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            EnsureDemo();
            ItemMallState.SetStatus(
                UiTextEn.Translate("Offline — demo catalog (world connection required)."));
        }
        RefreshAll();
    }

    private void Close()
    {
        Visible = false;
        if (_giftEdit != null) _giftEdit.Visible = false;
    }

    /// <summary>Les binds monde 56/57/58/62/63/64/79 sont posés en
    /// précoce dans <see cref="HudExtensions"/> (mutation immédiate
    /// côté réseau via les <c>Apply*Data</c> + <c>RaiseChanged</c>
    /// posté sur le thread principal) — posés ici ils arrivaient
    /// après le premier catalogue et les paquets tombaient en
    /// « non géré ».</summary>
    private void BindSession(EdenSession s)
    {
        _session = s;
    }

    private void SendWorld(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.World?.IsConnected != true)
        {
            GD.PushWarning($"[itemmall] pas de connexion monde — {msg.Name}");
            return;
        }
        var task = s.SendWorldAsync(msg, CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[itemmall] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------------------------------------------------
    // Démo hors-session (--hud-open=itemmall, captures)
    // ------------------------------------------------------------------

    /// <summary>Injecte un catalogue de démonstration si l'état est
    /// vide — utilisé par les captures hors-ligne. Ne fait rien si un
    /// vrai catalogue a déjà été reçu.</summary>
    public void EnsureDemo()
    {
        if (ItemMallState.Entries.Count > 0 ||
            ItemMallState.Points > 0 || ItemMallState.Bonus > 0)
            return;
        var list = new WorldItemMallList { Version = 1 };
        var ids = new[]
        {
            51773, 51689, 51664, 51772, 44982, 40194,
            40193, 40192, 42753, 40001, 40002, 63761,
        };
        var i = 0;
        foreach (var mu in new[] { 1, 2, 3 })
        foreach (var g in new[] { 1, 3, 7, 8 })
        {
            var id = ids[i++ % ids.Length];
            list.Entries.Add(new ItemMallEntry
            {
                Key = mu * 100 + g,
                Index = (i % 5) + 1,
                ItemId = id,
                ItemNum = (i % 3) + 1,
                Point = (uint)(10 + i * 17),
                SpecialPrice = i % 4 == 0 ? (uint)(5 + i * 9) : 0u,
                NumLimit = g == 8 ? (i % 4) + 1 : 0,
                LevelLimit = (short)(i % 4 == 0 ? 20 : 0),
                Note = g == 8 ? "limited stock" : "",
            });
        }
        ItemMallState.ApplyListData(list);
        ItemMallState.ApplyPointsData(1500, 260);
        ItemMallState.ApplyReceiptsData(new WorldItemMallReceivableList());
        ItemMallState.RaiseChanged();
    }
}
