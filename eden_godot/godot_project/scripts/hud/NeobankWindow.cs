using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// « Neobank » — la banque étendue du mod Reforged NeoInventoryBank.dll
/// (<c>re/ghidra/rf_neobank</c>), greffée sur le même protocole que la
/// banque classique (357→62→358 / dépôt 60 / retrait 61 —
/// <see cref="StorageState"/>). <b>Aucun NCID nouveau</b> : l'audit du
/// binaire montre une extension UI pure (aucune chaîne réseau) — les
/// données restent celles du storage natif, aucun handler serveur
/// requis.
///
/// Éléments reproduits d'après les preuves du binaire (layout XML
/// embarqué + strings.txt) :
///   - fenêtre <c>StorageUI2.layout</c> (<c>Root/StorageUI</c>,
///     onglets <c>bag0..bag4</c> = conteneurs banque) redimensionnée à
///     la grille — la DLL génère son contenu dynamiquement ;
///   - grille <c>Columns × Rows</c> (ini : Columns 16..32 défaut 16,
///     Rows 1..20 défaut 8) de slots <c>bag%u</c> — indexés par slot
///     banque, pages <c>Previous / Page "x/y" / Next</c> (ScrollMode 1)
///     ou défilement (ScrollMode 0) ;
///   - <c>neo/Bank/Footer</c> : boutons <c>Columns</c>/<c>Rows</c>/
///     <c>Mode</c> cyclant les réglages (persistés user://neo_bank.cfg) ;
///   - recherche <c>neo/Bank/Query</c> (nom ou « ID: n », Escape =
///     effacer — tooltip natif) + <c>SearchHint</c> + <c>HoverId</c>
///     (id de l'item survolé) ;
///   - menu contextuel <c>neo/Bank/Popup</c> (Choice0..19 natifs) :
///     Retirer / Verrouiller / Annuler ;
///   - verrous clients <c>neo/LockedPool</c> : cadre doré FFFFFF60
///     par slot (<see cref="NeobankLocks"/>) — pur UX, les verrouillés
///     refusent le retrait d'un clic.
///
/// Colonne « Inventory » à droite = extension locale (la DLL met la
/// version étendue du sac dans Root/Bag, fenêtre séparée) — nécessaire
/// au dépôt puisque notre inventaire n'est pas néo-amélioré.
/// Bascule classique ↔ neobank : <see cref="NeobankSettings.Enabled"/>
/// partagé avec <see cref="BankWindow"/>.
/// </summary>
public partial class NeobankWindow : Control
{
    private Control? _window;
    private GridContainer? _grid;
    private Control? _gridWrap;
    private ScrollContainer? _scroll;
    private VBoxContainer? _bagList;
    private Label? _pageLbl;
    private Label? _hoverId;
    private Label? _hint;
    private Label? _gold;
    private LineEdit? _query;
    private PopupMenu? _popup;
    private Button? _colsBtn;
    private Button? _rowsBtn;
    private Button? _modeBtn;

    private int _container;
    private int _page;
    private InventorySlot? _sel;
    private InventorySlot? _selBag;
    private string _filter = "";
    private bool _built;
    private bool _demo;
    private List<StorageContainer> _demoContainers = new();

    public override void _Ready()
    {
        NeobankSettings.Load();
        NeobankLocks.Load();
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("StorageUI2.layout")
            ?? hud?.Build("StorageUI.layout");
        if (_window == null)
            BuildFallback();
        else
            AddChild(_window);
        BuildContent();
        LayoutWindow();
        DragHelper.EnsureTitleBar(_window ?? this, "Bank+");
        DragHelper.Persist(_window ?? this, "neobank");
        Visible = false;
        StorageState.Changed += OnStorageChanged;
        PlayerBag.Changed += RefreshBag;
        NeobankLocks.Changed += RefreshGrid;
        PlayerVitals.GoldChanged += RefreshGold;
    }

    public override void _ExitTree()
    {
        StorageState.Changed -= OnStorageChanged;
        PlayerBag.Changed -= RefreshBag;
        NeobankLocks.Changed -= RefreshGrid;
        PlayerVitals.GoldChanged -= RefreshGold;
        base._ExitTree();
    }

    // ------------------------------------------------------------------
    // Contenu — chrome natif + bandeau « neo/Bank/Footer » reproduit
    // ------------------------------------------------------------------

    private void BuildContent()
    {
        var host = _window;
        if (host == null || _built) return;
        _built = true;

        var close = TopLeftVitals.Find<BaseButton>(host, "closeBtn");
        if (close != null) close.Pressed += Close;
        var rent = TopLeftVitals.Find<BaseButton>(host, "rentBtn");
        if (rent != null) rent.Visible = false;

        // ---- onglets conteneurs : bag0..bag4 (SlotImage du layout) ----
        for (var i = 0; i < 5; i++)
        {
            var tab = TopLeftVitals.Find<Control>(host, $"bag{i}");
            if (tab == null) continue;
            var idx = i;
            tab.GuiInput += e =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed &&
                    mb.ButtonIndex == MouseButton.Left)
                {
                    _container = idx;
                    _page = 0;
                    RefreshGrid();
                }
            };
            // bouton invisible pour capter le clic sur la SlotImage
            var btn = new Button { Name = $"__tabbtn{i}" };
            btn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn.Modulate = new Color(1, 1, 1, 0.01f);
            btn.Pressed += () =>
            {
                _container = idx;
                _page = 0;
                RefreshGrid();
            };
            tab.AddChild(btn);
            tab.MouseFilter = MouseFilterEnum.Stop;
        }

        // ---- grille néo (zone backgrand, sous la bande d'onglets) ----
        _scroll = new ScrollContainer
        {
            Name = "neo_Bank_Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AnchorRect(_scroll, host, 0.01f, 0.10f, 0.82f, 0.66f);
        host.AddChild(_scroll);
        _gridWrap = new Control { Name = "neo_Bank_GridWrap" };
        _gridWrap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _gridWrap.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scroll.AddChild(_gridWrap);
        _grid = new GridContainer { Name = "neo_Bank_Grid" };
        _gridWrap.AddChild(_grid);

        // ---- inventaire joueur (extension locale — dépôt) ----
        var bagPane = new VBoxContainer { Name = "__bagpane" };
        AnchorRect(bagPane, host, 0.835f, 0.10f, 0.99f, 0.66f);
        host.AddChild(bagPane);
        var bagTitle = new Label { Text = UiTextEn.Translate("Inventory") };
        bagTitle.AddThemeFontSizeOverride("font_size", 11);
        bagPane.AddChild(bagTitle);
        var bagScroll = new ScrollContainer();
        bagScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        bagPane.AddChild(bagScroll);
        _bagList = new VBoxContainer();
        _bagList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bagScroll.AddChild(_bagList);

        // ---- footer « neo/Bank/Footer » ----
        var footer = new HBoxContainer { Name = "neo_Bank_Footer" };
        AnchorRect(footer, host, 0.01f, 0.70f, 0.99f, 0.78f);
        footer.AddThemeConstantOverride("separation", 4);
        host.AddChild(footer);
        _colsBtn = MkBtn(footer, "", () =>
        {
            NeobankSettings.CycleColumns();
            NeobankSettings.Save();
            LayoutWindow();
            RefreshGrid();
        });
        _rowsBtn = MkBtn(footer, "", () =>
        {
            NeobankSettings.CycleRows();
            NeobankSettings.Save();
            LayoutWindow();
            RefreshGrid();
        });
        _modeBtn = MkBtn(footer, "", () =>
        {
            NeobankSettings.ToggleMode();
            NeobankSettings.Save();
            RefreshGrid();
        });
        footer.AddChild(new Control
        { CustomMinimumSize = new Vector2(10, 0) });
        var prev = MkBtn(footer, "<", () => _page--);
        prev.Pressed += RefreshGrid;
        _pageLbl = new Label { Text = "1/1", Name = "neo_Bank_Page" };
        _pageLbl.AddThemeFontSizeOverride("font_size", 10);
        footer.AddChild(_pageLbl);
        var next = MkBtn(footer, ">", () => _page++);
        next.Pressed += RefreshGrid;
        var classic = MkBtn(footer, "Classic", () =>
        {
            NeobankSettings.Enabled = false;
            NeobankSettings.Save();
            StorageState.NotifyChanged(); // BankWindow reprend le relais
        });
        classic.TooltipText = UiTextEn.Translate("Switch back to the classic bank view.");

        // ---- ligne recherche : neo/Bank/Query + SearchHint + HoverId ----
        var search = new HBoxContainer { Name = "neo_Bank_QueryRow" };
        AnchorRect(search, host, 0.01f, 0.80f, 0.99f, 0.88f);
        search.AddThemeConstantOverride("separation", 4);
        host.AddChild(search);
        _query = new LineEdit
        {
            Name = "neo_Bank_Query",
            PlaceholderText = UiTextEn.Translate("Search by name or item ID..."),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = UiTextEn.Translate("Search by item name or ID. ") +
                UiTextEn.Translate("Press Escape to clear."),
        };
        _query.AddThemeFontSizeOverride("font_size", 11);
        _query.TextChanged += t => { _filter = t; RefreshGrid(); };
        _query.GuiInput += e =>
        {
            if (e is InputEventKey k && k.Pressed &&
                k.Keycode == Key.Escape)
            {
                _query.Text = "";
                _filter = "";
                _query.ReleaseFocus();
                RefreshGrid();
            }
        };
        search.AddChild(_query);
        _hint = new Label
        {
            Name = "neo_Bank_SearchHint",
            Text = "",
            CustomMinimumSize = new Vector2(90, 0),
        };
        _hint.AddThemeFontSizeOverride("font_size", 10);
        search.AddChild(_hint);
        _hoverId = new Label
        {
            Name = "neo_Bank_HoverId",
            Text = UiTextEn.Translate("Item ID"),
            CustomMinimumSize = new Vector2(70, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _hoverId.AddThemeFontSizeOverride("font_size", 10);
        search.AddChild(_hoverId);

        // ---- bas : or + statut ----
        _gold = new Label { Name = "__gold" };
        AnchorRect(_gold, host, 0.01f, 0.90f, 0.60f, 0.97f);
        _gold.AddThemeFontSizeOverride("font_size", 11);
        host.AddChild(_gold);

        // ---- popup contextuel néo (Choice0..19 de la DLL) ----
        _popup = new PopupMenu { Name = "neo_Bank_Popup" };
        _popup.AddItem(UiTextEn.Translate("Withdraw"), 0);   // Choice0
        _popup.AddItem(UiTextEn.Translate("Lock/Unlock"), 1); // Choice1
        _popup.AddItem(UiTextEn.Translate("Cancel"), 2);      // Choice2
        _popup.IdPressed += OnPopupChoice;
        host.AddChild(_popup);

        RefreshGold();
        RefreshFooter();
    }

    private Button MkBtn(Control parent, string text, Action act)
    {
        var b = new Button { Text = text };
        b.AddThemeFontSizeOverride("font_size", 10);
        b.Pressed += act;
        parent.AddChild(b);
        return b;
    }

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

    /// <summary>Redimensionne la fenêtre pour contenir la grille
    /// Columns×Rows (cellules 34 px comme les SlotImage natives) +
    /// onglets + footer — la DLL fait la même chose à chaud.</summary>
    private void LayoutWindow()
    {
        if (_window == null) return;
        var w = Math.Max(520, NeobankSettings.Columns * 36 + 200);
        var h = Math.Max(300, NeobankSettings.Rows * 36 + 150);
        HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
            -w / 2f, -h / 2f, w / 2f, h / 2f);
        RefreshFooter();
    }

    private void RefreshFooter()
    {
        if (_colsBtn != null)
            _colsBtn.Text = UiTextEn.Translate($"{NeobankSettings.Columns} cols");
        if (_rowsBtn != null)
            _rowsBtn.Text = UiTextEn.Translate($"{NeobankSettings.Rows} rows");
        if (_modeBtn != null)
            _modeBtn.Text = NeobankSettings.ScrollMode == 0
                ? UiTextEn.Translate("Use Pages") : UiTextEn.Translate("Use Scroll");
        if (_grid != null)
            _grid.Columns = NeobankSettings.Columns;
    }

    private void BuildFallback()
    {
        var p = new Panel { Name = "Root_StorageUI" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-300, -200);
        p.Size = new Vector2(600, 400);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
    }

    // ------------------------------------------------------------------
    // Données banque — même flux que BankWindow (357/358/62)
    // ------------------------------------------------------------------

    private void OnStorageChanged()
    {
        if (!StorageState.IsOpen || !NeobankSettings.Enabled)
        {
            if (!_demo) Visible = false;
            return;
        }
        _demo = false;
        _demoContainers.Clear();
        _container = Math.Clamp(_container, 0,
            Math.Max(0, StorageState.Containers.Count - 1));
        Visible = true;
        RefreshGrid();
        RefreshBag();
        RefreshGold();
    }

    private StorageContainer? Current()
    {
        var conts = _demo ? _demoContainers : StorageState.Containers;
        return _container < conts.Count ? conts[_container] : null;
    }

    /// <summary>Grille paginée (ScrollMode 1) ou défilante (0) —
    /// cells = slots banque, indexés par leur n° de slot natif.
    /// Recherche : surlignage vert des correspondances + hint « Match »
    /// (miroir des chaînes de la DLL).</summary>
    private void RefreshGrid()
    {
        if (_grid == null || _scroll == null) return;
        foreach (var c in _grid.GetChildren()) c.QueueFree();
        var cont = Current();
        var cap = (int)(cont?.Capacity ?? 0);
        NeobankLocks.Prune(_container, cap);

        // Mode 0 = défilement natif → toute la capacité dans le scroll ;
        // mode 1 = fenêtré pages.
        int first = 0, perPage = Math.Max(cap, 1);
        var perCfg = NeobankSettings.SlotsPerPage;
        var paged = NeobankSettings.ScrollMode != 0;
        if (paged) perPage = perCfg;
        var maxPage = Math.Max(1,
            (Math.Max(cap, 1) + perPage - 1) / perPage);
        _page = Math.Clamp(_page, 0, maxPage - 1);
        if (paged) first = _page * perPage;
        var count = paged
            ? Math.Min(perPage, Math.Max(0, cap - first))
            : cap;
        if (_pageLbl != null)
            _pageLbl.Text = $"{_page + 1}/{maxPage}";
        _scroll.Visible = count > 0;

        var bySlot = new Dictionary<int, InventorySlot>();
        if (cont != null)
            foreach (var it in cont.Items) bySlot[it.Slot] = it;

        var matches = 0;
        for (var i = 0; i < count; i++)
        {
            var slotIdx = first + i;
            bySlot.TryGetValue(slotIdx, out var it);
            var cell = BuildCell(slotIdx, it);
            _grid.AddChild(cell);
            if (it != null && Matches(it)) matches++;
        }
        if (count == 0)
            _grid.AddChild(new Label { Text = UiTextEn.Translate("(empty bank tab)") });
        if (_hint != null)
            _hint.Text = _filter.Length == 0
                ? ""
                : matches == 0 ? UiTextEn.Translate("no match") : UiTextEn.Translate($"Match: {matches}");
    }

    private bool Matches(InventorySlot it)
    {
        if (_filter.Length == 0) return true;
        var f = _filter.Trim();
        if (f.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
            f = f[3..].Trim();
        if (int.TryParse(f, out var id))
            return it.ItemId == id;
        var name = TextResolver.ItemName(it.ItemId) ?? "";
        return name.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cellule bag%u : icône + quantité + cadre doré LockedPool
    /// + surlignage recherche ; clic = sélection, double-clic = retrait,
    /// clic droit = popup néo.</summary>
    private Control BuildCell(int slotIdx, InventorySlot? it)
    {
        var cell = new Panel
        {
            Name = $"bag{slotIdx}",
            CustomMinimumSize = new Vector2(34, 34),
            TooltipText = it != null ? SlotTooltip(it) : "",
        };
        var locked = it != null && NeobankLocks.IsLocked(_container, slotIdx);
        var match = it != null && Matches(it);
        cell.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = it == null
                ? new Color(0.06f, 0.06f, 0.08f, 0.55f)
                : !match
                    ? new Color(0.10f, 0.10f, 0.12f, 0.85f)
                    : new Color(0.10f, 0.16f, 0.10f, 0.9f),
            BorderColor = locked
                ? new Color(1f, 1f, 0.376f)          // FFFFFF60 natif
                : match && _filter.Length > 0
                    ? new Color(0.3f, 0.9f, 0.4f)
                    : new Color(0.25f, 0.28f, 0.4f, 0.8f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        if (it != null)
        {
            var def = ItemTableCache.Get(it.ItemId);
            if (def != null && def.Code.Length > 0)
            {
                var tex = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
                if (tex != null)
                {
                    var tr = new TextureRect
                    {
                        Texture = tex,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum
                            .KeepAspectCentered,
                        MouseFilter = MouseFilterEnum.Ignore,
                    };
                    tr.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                    cell.AddChild(tr);
                }
            }
            if (it.Amount > 1)
            {
                var q = new Label
                {
                    Text = $"{it.Amount}",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                q.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                q.AddThemeFontSizeOverride("font_size", 9);
                cell.AddChild(q);
            }
            var slot = it;
            var idx = slotIdx;
            cell.MouseEntered += () =>
            {
                if (_hoverId != null) _hoverId.Text = UiTextEn.Translate($"ID: {slot.ItemId}");
            };
            cell.GuiInput += e =>
            {
                if (e is not InputEventMouseButton mb || !mb.Pressed)
                    return;
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    _sel = slot;
                    if (mb.DoubleClick) DoWithdraw();
                }
                else if (mb.ButtonIndex == MouseButton.Right)
                {
                    _sel = slot;
                    _selBag = null;
                    _popup?.SetItemDisabled(0, false);
                    _popup?.SetItemText(1, locked ? "Unlock" : UiTextEn.Translate("Lock"));
                    _popup?.Popup(new Rect2I(
                        (Vector2I)cell.GetGlobalPosition() +
                        new Vector2I(34, 0), Vector2I.Zero));
                }
            };
        }
        else
        {
            cell.GuiInput += e =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed &&
                    mb.ButtonIndex == MouseButton.Left)
                    _sel = null;
            };
        }
        return cell;
    }

    private static string SlotTooltip(InventorySlot it)
    {
        var name = TextResolver.ItemName(it.ItemId) ?? $"#{it.ItemId}";
        var def = ItemTableCache.Get(it.ItemId);
        var n = StorageItemCell.IsStackable(def)
            ? StorageItemCell.StackCount(it, def) : it.Amount;
        var qty = n > 1 ? $" ×{n}" : "";
        // Durabilité = équipement seulement (def col74) ; jamais un
        // compteur de pile.
        var dur = def is { MaxDurability: > 0 } && it.MaxDurability > 0
            ? $" — dur {it.Durability}/{it.MaxDurability}" : "";
        return $"{name}{qty}{dur}\nslot {it.Slot} — id {it.ItemId}";
    }

    /// <summary>Popup néo : 0 = retrait (CW 61), 1 = verrou
    /// LockedPool, 2 = annuler.</summary>
    private void OnPopupChoice(long id)
    {
        switch (id)
        {
            case 0: DoWithdraw(); break;
            case 1:
                if (_sel is { } it)
                    NeobankLocks.Toggle(_container, it.Slot);
                break;
            default: break;
        }
    }

    /// <summary>NCID 61 — même wire que BankWindow ; les slots
    /// verrouillés refusent le retrait (règle LockedPool).</summary>
    private void DoWithdraw()
    {
        if (_sel is not { } it || !StorageState.IsOpen) return;
        if (NeobankLocks.IsLocked(_container, it.Slot))
        {
            if (_hint != null) _hint.Text = UiTextEn.Translate("locked — unlock first");
            return;
        }
        Send(new ZoneStorageWithdraw
        {
            Field1 = StorageState.NpcUid,
            Field2 = (ushort)_container,
            Field3 = (short)it.Slot,
            Field4 = (short)Math.Max(it.Amount, 1),
        });
    }

    // ------------------------------------------------------------------
    // Sac → dépôt (même wire 60 que BankWindow)
    // ------------------------------------------------------------------

    private void RefreshBag()
    {
        if (_bagList == null) return;
        foreach (var c in _bagList.GetChildren()) c.QueueFree();
        _selBag = null;
        foreach (var it in PlayerBag.Items)
        {
            var name = TextResolver.ItemName(it.ItemId) ?? $"#{it.ItemId}";
            var qty = it.Amount > 1 ? $" ×{it.Amount}" : "";
            var row = new Button
            {
                Text = $"  {name}{qty}",
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 22),
            };
            row.AddThemeFontSizeOverride("font_size", 10);
            var slot = it;
            row.Pressed += () => { _selBag = slot; _sel = null; };
            row.GuiInput += e =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed &&
                    mb.ButtonIndex == MouseButton.Left && mb.DoubleClick)
                {
                    _selBag = slot;
                    DoDeposit();
                }
                else if (e is InputEventMouseButton mb2 && mb2.Pressed &&
                         mb2.ButtonIndex == MouseButton.Right)
                {
                    _selBag = slot;
                    _sel = null;
                    DoDeposit();
                }
            };
            _bagList.AddChild(row);
        }
        if (PlayerBag.Items.Count == 0)
            _bagList.AddChild(new Label { Text = UiTextEn.Translate("(empty)") });
    }

    /// <summary>NCID 60 — dépôt [i32 npc][u16 bag][s16 slot][s16 n].</summary>
    private void DoDeposit()
    {
        if (_selBag is not { } it || !StorageState.IsOpen) return;
        Send(new ZoneStorageDeposit
        {
            Field1 = StorageState.NpcUid,
            Field2 = (ushort)it.Bag,
            Field3 = (short)it.Slot,
            Field4 = (short)Math.Max(it.Amount, 1),
        });
    }

    private void RefreshGold()
    {
        if (_gold != null)
            _gold.Text = UiTextEn.Translate($"Gold : {PlayerVitals.Gold:N0}");
    }

    private void Close()
    {
        _demo = false;
        _demoContainers.Clear();
        if (StorageState.IsOpen) StorageState.Close();
        Visible = false;
    }

    /// <summary>Ouvre en mode démo (hors session — captures).</summary>
    public void Open()
    {
        if (!StorageState.IsOpen) EnsureDemo();
        Visible = true;
        RefreshGrid();
        RefreshBag();
        RefreshGold();
    }

    public void Toggle()
    {
        if (Visible) Close();
        else { NeobankSettings.Enabled = true; Open(); }
    }

    /// <summary>Données de démo pour <c>--hud-open=neobank</c> hors
    /// session — deux conteneurs factices remplissant la grille (les
    /// verrous/recherche/pages restent fonctionnels ; les envois
    /// 60/61 sont no-op sans session, warning logué).</summary>
    public void EnsureDemo()
    {
        if (StorageState.IsOpen) { Visible = true; return; }
        _demo = true;
        _demoContainers = new List<StorageContainer>
        {
            new(40, 0, new List<InventorySlot>
            {
                new(0, 0, 30240, 1, 1, 0, 1, 0),
                new(0, 1, 31899, 75, 75, 0, 64, 0),
                new(0, 5, 11205, 12, 1, 0, 12, 0),
                new(0, 20, 40194, 3, 1, 0, 3, 0),
                new(0, 33, 11204, 8, 1, 0, 8, 0),
            }),
            new(40, 0, new List<InventorySlot>()),
        };
        _container = 0;
        _page = 0;
        Visible = true;
        RefreshGrid();
        RefreshBag();
        RefreshGold();
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[neobank] pas de session — {msg.Name}");
            return;
        }
        var task = s.SendZoneAsync(msg, CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[neobank] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
