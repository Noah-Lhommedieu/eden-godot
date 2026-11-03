using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre inventaire « Backpack » (ref shot_0017, Reforged) —
/// construite en code avec <see cref="EeSkin"/> : grille 22×15 de
/// cases fleur-de-lys natives, icône <c>UI/itemicon/&lt;code&gt;.png</c>
/// (code = col1 c_item.ini via <see cref="ItemTable"/>), tooltip riche
/// (<see cref="ItemTooltip"/>).
///
/// Données : <see cref="NativeInventoryParser"/> (corps 312 prouvé
/// byte-à-byte côté serveur) puis deltas live bindés paresseusement
/// sur <see cref="SessionCarry.Session"/> (même pattern que
/// <see cref="HudNetBridge"/>) :
///   - 274 PutItemIntoInventory → ajout/remplacement d'un slot ;
///   - 275 ClientDropItem (type 0) → retrait d'un slot ;
///   - 291 ModifyItemDurability → count/durabilité d'un slot ;
///   - 277 SwitchItemLocationOK → ack du move : le serveur n'émet
///     PAS de 274/275 pour un move 0→0 — on applique localement avec
///     les dst RÉSOLUS de l'ack (jamais de −1, cf. handler natif) ;
///   - 303 ClientItemUse → ack d'usage (log).
///
/// Interactions (lot S2) :
///   - double-clic item équipable → NCID 7 (0,slot,sac)→(1,loc,0)
///     puis NCID 8 mask → confirmation 282 ;
///   - double-clic consommable (UseType 1/2) → NCID 19 ItemUse ;
///   - drag d'une cellule → drop sur une autre → NCID 7 move 0→0 ;
///   - Maj+drop sur stack &gt;1 → popup split → NCID 84 DivideItem ;
///   - drop hors grille → rien (pas de drop au sol — le NCID 6 natif
///     DÉTRUIT l'item, non câblé par sécurité) ;
/// </summary>
public partial class InventoryWindow : Control
{
    private const int Columns = 10;
    private const int CellTotal = 420;
    /// <summary>Sac de base (serveur : GetBagCapacity ?? 32).</summary>
    private const int BaseCapacity = 32;
    private const int WinW = 420;
    private const int CellSize = 34;

    private GridContainer? _grid;
    private Control? _window;
    /// <summary>Panneau visible (registre <see cref="HudWindows"/>).</summary>
    public Control? Window => _window;
    private HudRoot? _hud;
    private static Dictionary<int, ItemTable.ItemDef>? _items;
    private readonly List<ItemCell> _cells = new();

    /// <summary>Onglet courant du bandeau catégorie (ref).</summary>
    private enum BagTab { All, Potions, Gear, Town, Special }
    private BagTab _tab = BagTab.All;
    /// <summary>Sac affiché (Slot0-5 natifs = sac principal +
    /// extensions) — les onglets filtrent dans ce sac.</summary>
    private int _bagView;
    private Label? _goldText;
    private Label? _silverText;
    private Label? _giftText;
    private Label? _scrapText;
    private readonly List<Button> _tabBtns = new();
    private Panel? _gridBox, _money;
    private ScrollContainer? _scroll;
    private readonly List<Panel> _bagSlots = new();
    private readonly List<TextureButton> _funcBtns = new();
    private readonly List<Button> _cfgBtns = new();
    private Label? _cfgLabel;
    private int _cols = 10, _rowsShown = 4;

    /// <summary>État connu des slots : (sac, slot) → item — alimenté
    /// par le 312 (<see cref="SetItems"/>) puis les deltas 274/275/291
    /// et l'application locale du 277.</summary>
    private readonly Dictionary<(int Bag, int Slot), InventorySlot>
        _slots = new();

    /// <summary>État partagé (sac, slot) → item — lecture seule pour les
    /// panneaux hors inventaire (bouton « Use » du tracker).</summary>
    internal static IReadOnlyDictionary<(int Bag, int Slot), InventorySlot>
        SharedSlots => _sharedSlots;

    /// <summary>Levé (thread principal) quand le contenu des sacs change.</summary>
    internal static event Action? SlotsChanged;

    /// <summary>Lecture seule de l'état connu — sonde <c>--inv-test</c>.</summary>
    internal IReadOnlyDictionary<(int Bag, int Slot), InventorySlot>
        KnownSlots => _slots;

    /// <summary>Déséquipe la loc (7 puis 8) — sonde ; même requête que
    /// <see cref="EquipSlotsPanel"/> (double-clic sur le slot).</summary>
    internal void UnequipDirect(int loc)
    {
        Send(ZoneItemActions.UnequipRequest(loc));
        Send(new ZoneUpdateEquipment { UpdateFlags = 1u << loc });
    }

    /// <summary>Split sans popup (même NCID 84 que le popup) — sonde.</summary>
    internal void SplitDirect(int srcBag, int srcSlot, int dstSlot,
        int count) => Send(new ZoneDivideItem
    {
        SrcSlot = (short)srcSlot, SrcBag = (short)srcBag,
        DstSlot = (short)dstSlot, DstBag = 0,
        Count = (short)count, Kind = 0,
    });

    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();

    public void Build(HudRoot hud)
    {
        _hud = hud;
        // Ref shot_0017 « Backpack » (Reforged) : fenêtre 837×718 posée
        // à x 193, onglets centrés, grille 22×15 sur parchemin, or en
        // bas-gauche, 6 sacs en bas-droite, rangée de fonctions. Le
        // layout natif inventorywnd (19×12, pré-Reforged) n'a plus la
        // bonne géométrie : construit en code avec les briques EeSkin.
        var win = EeSkin.MakeWindow(hud, UiTextEn.Translate("Backpack"), 193, 0, WinW, 510);
        _window = win;
        AddChild(win);

        // Onglets All / Potions / Gear / Town / Special (106 px, pas 116).
        var tabs = new (BagTab Tab, string Label)[]
        {
            (BagTab.All, UiTextEn.Translate("All")),
            (BagTab.Potions, UiTextEn.Translate("Potions")),
            (BagTab.Gear, UiTextEn.Translate("Gear")),
            (BagTab.Town, UiTextEn.Translate("Town")),
            (BagTab.Special, UiTextEn.Translate("Special")),
        };
        for (var i = 0; i < tabs.Length; i++)
        {
            var b = new Button
            {
                Text = tabs[i].Label, ToggleMode = true,
                FocusMode = FocusModeEnum.None,
            };
            HudRoot.Pin(b, 0, 0, 0, 0, 10 + i * 81, 30, 88 + i * 81, 56);
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeColorOverride("font_color", Color.Color8(236, 236, 232));
            b.AddThemeColorOverride("font_pressed_color", Colors.White);
            b.AddThemeColorOverride("font_hover_color", Colors.White);
            b.AddThemeStyleboxOverride("normal", EeSkin.PanelTab(false));
            b.AddThemeStyleboxOverride("hover", EeSkin.PanelTab(false));
            b.AddThemeStyleboxOverride("pressed", EeSkin.PanelTab(true));
            b.AddThemeStyleboxOverride("hover_pressed", EeSkin.PanelTab(true));
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            var t = tabs[i].Tab;
            b.Pressed += () => SetTab(t);
            _tabBtns.Add(b);
            win.AddChild(b);
        }
        _tabBtns[0].ButtonPressed = true;

        // Grille : cadre parchemin, cases fleur-de-lys 34 px (pas 36).
        // M3 : colonnes/lignes visibles configurables, défilement
        // case par case (une rangée par cran de molette).
        _gridBox = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _gridBox.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
        win.AddChild(_gridBox);
        _scroll = new ScrollContainer
        {
            Name = "BagScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        HudRoot.Pin(_scroll, 0, 0, 1, 1, 5, 5, -4, -4);
        _scroll.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true } wb &&
                wb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var step = CellSize + 2;
                var dir = wb.ButtonIndex == MouseButton.WheelUp ? -1 : 1;
                _scroll.ScrollVertical =
                    (_scroll.ScrollVertical / step + dir) * step;
                _scroll.AcceptEvent();
            }
        };
        _gridBox.AddChild(_scroll);
        _grid = MakeGrid();
        _grid.Columns = _cols;
        _scroll.AddChild(_grid);

        // Or (pièce dorée) + argent, encadré en creux.
        _money = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _money.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        win.AddChild(_money);
        _goldText = MoneyLabel(_money, 8, 84, Color.Color8(240, 170, 40));
        _silverText = MoneyLabel(_money, 92, 152, Color.Color8(200, 200, 205));
        PlayerVitals.GoldChanged += RefreshMoney;
        RefreshMoney();

        // Sélecteurs de sac (sac principal + extensions ; le serveur
        // n'accepte que les locs 1-4 — « Bag 5 » est caché).
        var empty = EeSkin.EmptySlot(hud);
        for (var i = 0; i < 6; i++)
        {
            var slot = new Panel
            {
                Visible = i != 5,
                TooltipText = i == 0 ? UiTextEn.Translate("Backpack") : UiTextEn.Translate($"Bag {i}"),
            };
            if (empty != null) slot.AddThemeStyleboxOverride("panel", empty);
            var bag = i;
            slot.GuiInput += e =>
            {
                if (e is InputEventMouseButton
                    { Pressed: true } mb)
                {
                    if (mb.ButtonIndex == MouseButton.Left)
                    {
                        // Déposer un sac (kind 43) sur un emplacement
                        // 1-4 = l'équiper (SwitchItemLocation 0→5).
                        if (Held is { } h && bag is >= 1 and <= 4 &&
                            _slots.TryGetValue(h, out var hit) &&
                            LookupDef(hit.ItemId)?.Kind == 0x2b)
                        {
                            Held = null;
                            Send(ZoneItemActions.BagEquipRequest(
                                h.Bag, h.Slot, bag));
                            return;
                        }
                        RefreshCells();
                    }
                    // Clic droit sur un sac équipé = le retirer (5→0 —
                    // le serveur exige le sac d'inventaire vide).
                    else if (mb.ButtonIndex == MouseButton.Right &&
                             Held == null && bag is >= 1 and <= 4 &&
                             NativeInventoryParser.EquippedBags
                                 .ContainsKey(bag))
                        Send(ZoneItemActions.BagUnequipRequest(bag));
                }
            };
            _bagSlots.Add(slot);
            win.AddChild(slot);
        }

        // Barre de fonctions à icônes natives (Radar.imageset « NA* ») :
        // Archive, Fortify, Fusion, Craft racial, Backpack manager,
        // Stall, Special currency, tri auto (ArrangeItem 260).
        var funcs = new (string Icon, string Tip, Action Act)[]
        {
            ("Collect", UiTextEn.Translate("Archive"), () => OpenArchive()),
            ("SF", UiTextEn.Translate("Fortify"), () => Notice(UiTextEn.Translate("Fortify: via NPC"))),
            ("Combo", UiTextEn.Translate("Fusion"), () => Notice(UiTextEn.Translate("Fusion: via NPC"))),
            ("LS", UiTextEn.Translate("Racial Craft"), () => _hud?.OpenWindow("craft")),
            ("Option", UiTextEn.Translate("Backpack manager"), () => ShowBagManager()),
            ("Shop", UiTextEn.Translate("Stall (become a merchant)"), ToggleStall),
            ("SpecialCoin", UiTextEn.Translate("Special currency"), () => Notice(
                UiTextEn.Translate($"Special currency: {PlayerCoins.WarCoin} war coins, ") +
                UiTextEn.Translate($"{PlayerCoins.AlpacaCoin} alpaca coins"))),
            ("BO", UiTextEn.Translate("Sort items"), SortBag),
        };
        foreach (var (icon, tip, act) in funcs)
        {
            var b = new TextureButton
            {
                TooltipText = tip,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                IgnoreTextureSize = true,
                FocusMode = FocusModeEnum.None,
            };
            if (hud.Tex != null)
            {
                b.TextureNormal = hud.Tex.GetRegion(
                    new CeguiImageRef("Radar", icon + "NAN"));
                b.TextureHover = hud.Tex.GetRegion(
                    new CeguiImageRef("Radar", icon + "NAH"));
                b.TexturePressed = hud.Tex.GetRegion(
                    new CeguiImageRef("Radar", icon + "NAP"));
            }
            b.Pressed += act;
            _funcBtns.Add(b);
            win.AddChild(b);
        }

        // Réglage lignes / colonnes visibles.
        _cfgLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
        EeSkin.Shadowed(_cfgLabel, 11, Color.Color8(200, 200, 195));
        win.AddChild(_cfgLabel);
        foreach (var (txt, dc, dr) in new[]
                 { ("Col −", -1, 0), ("Col +", 1, 0),
                   ("Row −", 0, -1), ("Row +", 0, 1) })
        {
            var b = EeSkin.Grey(new Button
            {
                Text = txt, FocusMode = FocusModeEnum.None,
            }, 10);
            var (c, r) = (dc, dr);
            b.Pressed += () => SetGridSize(_cols + c, _rowsShown + r);
            _cfgBtns.Add(b);
            win.AddChild(b);
        }
        // A6.6 — pages costume (conteneurs d'équipement 2-4).
        var outfit = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("Outfits"),
            FocusMode = FocusModeEnum.None,
            TooltipText = UiTextEn.Translate("Costume pages (armor-weight sets)"),
        }, 10);
        outfit.Pressed += () =>
        {
            if (GetTree().Root.FindChild("CostumeWndPanel", true, false)
                is Control cw) HudWindows.Toggle(cw);
        };
        _cfgBtns.Add(outfit);
        win.AddChild(outfit);
        LayoutWindow();

        var cfg = new ConfigFile();
        if (cfg.Load(CfgPath) == Error.Ok)
        {
            _cols = (int)cfg.GetValue("grid", "cols", 10);
            _rowsShown = (int)cfg.GetValue("grid", "rows", 4);
            if (_grid != null) _grid.Columns = _cols;
            LayoutWindow();
        }
        EnsureCells(hud);
        // Le parse c_item/c_itemmall (~9 Mo) était paresseux : le
        // premier loot le payait dans un abonné Changed (~1 s de
        // freeze mesuré). Pré-chauffé en tâche de fond au montage du
        // HUD — parse de fichiers pur, aucune mutation de scène (même
        // méthode que DataWarmup post-load).
        _ = Task.Run(EnsureItemTable);
        // Le 312 peut arriver avant le montage de la fenêtre
        // (SetItems alors perdu) : l'état canonique vit dans
        // PlayerBag — on s'y abonne et on le relit à l'ouverture,
        // comme Bank/Shop/Vending.
        PlayerBag.Changed += () => SetItems(PlayerBag.Items);
        SetItems(PlayerBag.Items);
        // Repeinte différée : une mutation reçue fenêtre cachée ne
        // reconstruit pas les 420 cellules (icônes + tooltips) — le
        // repaint part à la réouverture.
        win.VisibilityChanged += () =>
        {
            if (win.Visible && _repaintPending)
            {
                _repaintPending = false;
                RefreshCells();
            }
        };
        DragHelper.Persist(win, "backpack");
        win.Visible = false;
        MountProbe(hud);
    }

    /// <summary>Sondes L-items (<c>--tip-test=ids</c> [+ <c>--tip-shot</c>],
    /// <c>--bag-test=png</c>) — voir <see cref="BagTipProbe"/>.</summary>
    private static void MountProbe(HudRoot hud)
    {
        var ids = Array.Empty<int>();
        string tipShot = "", bagShot = "";
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--tip-test="))
                ids = a[11..].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToArray();
            else if (a.StartsWith("--tip-shot=")) tipShot = a[11..];
            else if (a.StartsWith("--bag-test=")) bagShot = a[11..];
        }
        if (ids.Length > 0 || bagShot.Length > 0)
            hud.AddChild(new BagTipProbe(hud, ids, tipShot, bagShot));
    }

    private const string CfgPath = "user://inventory_layout.cfg";

    private void SetGridSize(int cols, int rows)
    {
        _cols = Math.Clamp(cols, 5, 14);
        _rowsShown = Math.Clamp(rows, 2, 12);
        if (_grid != null) _grid.Columns = _cols;
        LayoutWindow();
        RefreshCells();
        var cf = new ConfigFile();
        cf.SetValue("grid", "cols", _cols);
        cf.SetValue("grid", "rows", _rowsShown);
        try { cf.Save(CfgPath); } catch { /* préférence facultative */ }
    }

    /// <summary>Repositionne fenêtre, grille et barres selon
    /// colonnes × lignes visibles.</summary>
    private void LayoutWindow()
    {
        if (_window == null || _gridBox == null) return;
        var w = Math.Max(420, 10 + _cols * (CellSize + 2) + 4 + 18 + 8);
        var gridH = _rowsShown * (CellSize + 2) + 10;
        var y0 = 57;
        var yMoney = y0 + gridH + 10;
        var yFunc = yMoney + 38;
        var yCfg = yFunc + 42;
        HudRoot.Pin(_window, 0, 0, 0, 0, 193, 0, 193 + w, yCfg + 34);
        HudRoot.Pin(_gridBox, 0, 0, 0, 0, 10, y0, w - 8, y0 + gridH);
        if (_money != null)
            HudRoot.Pin(_money, 0, 0, 0, 0, 19, yMoney, 179, yMoney + 28);
        for (var i = 0; i < _bagSlots.Count; i++)
            HudRoot.Pin(_bagSlots[i], 0, 0, 0, 0, w - 226 + i * 36, yMoney - 3,
                w - 192 + i * 36, yMoney + 31);
        var fx = 12;
        var fw = Math.Min(40, (w - 24) / Math.Max(1, _funcBtns.Count));
        foreach (var b in _funcBtns)
        {
            HudRoot.Pin(b, 0, 0, 0, 0, fx, yFunc, fx + fw - 3, yFunc + fw - 3);
            fx += fw;
        }
        if (_cfgLabel != null)
        {
            _cfgLabel.Text = $"{_cols} × {_rowsShown}";
            HudRoot.Pin(_cfgLabel, 0, 0, 0, 0, 12, yCfg + 4, 70, yCfg + 24);
        }
        var bx = 70;
        foreach (var b in _cfgBtns)
        {
            HudRoot.Pin(b, 0, 0, 0, 0, bx, yCfg, bx + 52, yCfg + 26);
            bx += 56;
        }
    }

    private void Notice(string text) => _hud?.Chat?.AddSystem(text);

    /// <summary>Tri auto : NCID 260 ArrangeItem [s16 sac][s16 0] ;
    /// la réponse 620 déclenche le rechargement du sac.</summary>
    public void SortCurrentBag() => SortBag();

    internal void SortBag()
    {
        // Vue unifiée : on trie le sac de base puis chaque extension équipée.
        Send(new ZoneArrangeItem { Bag = 0 });
        foreach (var b in new[] { 1, 2, 3, 4 })
            if (NativeInventoryParser.EquippedBags.ContainsKey(b))
                Send(new ZoneArrangeItem { Bag = (short)b });
    }

    private void ShowBagManager()
    {
        Notice(UiTextEn.Translate("Backpack manager: drop a bag item on a bag slot (right of the gold) to equip it."));
    }

    private void ToggleStall()
    {
        if (GetTree().Root.FindChild("StallPanel", true, false) is Control sp)
            sp.Visible = !sp.Visible;
    }

    /// <summary>Montant + pastille de monnaie à droite (ref « 550 ● »).</summary>
    private static Label MoneyLabel(Control box, int l, int r, Color coin)
    {
        var lab = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(lab, 0, 0, 0, 1, l, 0, r - 16, 0);
        EeSkin.Shadowed(lab, 12);
        box.AddChild(lab);
        var dot = new Label
        {
            Text = "●", VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(dot, 0, 0, 0, 1, r - 13, 0, r, 0);
        EeSkin.Shadowed(dot, 12, coin);
        box.AddChild(dot);
        return lab;
    }

    private void SetTab(BagTab tab)
    {
        _tab = tab;
        for (var i = 0; i < _tabBtns.Count; i++)
            _tabBtns[i].ButtonPressed =
                _tabBtns[i].Text == TabName(tab);
        RefreshCells();
    }

    private static string TabName(BagTab t) => t switch
    {
        BagTab.Potions => UiTextEn.Translate("Potions"),
        BagTab.Gear => UiTextEn.Translate("Gear"),
        BagTab.Town => UiTextEn.Translate("Town"),
        BagTab.Special => UiTextEn.Translate("Special"),
        _ => UiTextEn.Translate("All"),
    };

    /// <summary>Monnaie : or en entier + argent (reste /100) +
    /// points mall en « gift » (le « 550 🪙  6 💎 » du ref) —
    /// scrap laissé à 0 faute de compteur dédié.</summary>
    private void RefreshMoney()
    {
        var money = PlayerVitals.Gold;
        if (_goldText != null) _goldText.Text = $"{money / 100:#,0}";
        if (_silverText != null) _silverText.Text = $"{money % 100}";
        if (_giftText != null)
            _giftText.Text = $"{ItemMallState.Points}";
        if (_scrapText != null) _scrapText.Text = "0";
    }

    /// <summary>Filtre d'onglet — règles dérivées de c_item :
    /// Potions = UseType 1/2 (consommables), Gear = équipable
    /// joueur, Town/Special = approximation (désasm du filtre natif
    /// pas encore extrait — les items sans autre case atterrissent
    /// dans Special).</summary>
    private bool MatchesTab(InventorySlot it)
    {
        if (_tab == BagTab.All) return true;
        var def = LookupDef(it.ItemId);
        if (def == null) return _tab == BagTab.Special;
        return _tab switch
        {
            BagTab.Potions => def.UseType is 1 or 2,
            BagTab.Gear => def.EquipLoc != 0 ||
                ZoneItemActions.IsPlayerEquippableKind(def.Kind),
            // shortcut: le découpage natif Town/Special repose sur une
            // colonne c_item non identifiée — UseType 3/5/6 (items à
            // effet/elf) ≈ « Town » fonctionnel ; à confirmer par désasm
            // du filtre sac natif.
            BagTab.Town => def.UseType is 3 or 5 or 6,
            // Special = tout ce qui n'entre dans aucune autre case.
            _ => def.UseType is not (1 or 2 or 3 or 5 or 6) &&
                 def.EquipLoc == 0 &&
                 !ZoneItemActions.IsPlayerEquippableKind(def.Kind),
        };
    }

    // ------------------------------------------------------------------
    // Données
    // ------------------------------------------------------------------

    /// <summary>Item à un slot connu, null si vide/inconnu —
    /// partagé avec <see cref="EquipSlotsPanel"/>.</summary>
    public static InventorySlot? ItemAt(int bag, int slot) =>
        _sharedSlots.TryGetValue((bag, slot), out var it) ? it : null;

    /// <summary>Snapshot de tous les items de sac connus (catalyseur
    /// de fusion pet, etc.).</summary>
    public static List<InventorySlot> AllItems() =>
        _sharedSlots.Values.ToList();

    /// <summary>Vue partagée des slots (même source que les
    /// instances — il n'y a qu'une fenêtre inventaire en jeu).</summary>
    private static readonly Dictionary<(int, int), InventorySlot>
        _sharedSlots = new();

    /// <summary>Toutes les définitions c_item + c_itemmall (balayage
    /// des objets de monture hors c_collection — archive).</summary>
    public static IEnumerable<KeyValuePair<int, ItemTable.ItemDef>> AllDefs()
    {
        EnsureItemTable();
        return _items ?? Enumerable.Empty<KeyValuePair<int, ItemTable.ItemDef>>();
    }

    /// <summary>Définition c_item pour un id (null si table absente
    /// ou id inconnu) — table partagée avec EquipSlotsPanel.</summary>
    public static ItemTable.ItemDef? LookupDef(int itemId)
    {
        EnsureItemTable();
        return _items != null && _items.TryGetValue(itemId, out var d)
            ? d : null;
    }

    /// <summary>Tous les ids d'items connus (catalogues, aperçu A6.7) ;
    /// peut être appelé hors thread principal.</summary>
    internal static List<int> AllItemIds()
    {
        EnsureItemTable();
        return _items != null ? _items.Keys.ToList() : new List<int>();
    }

    /// <summary>c_item.ini + t_item.ini chargés à la demande (~9 Mo,
    /// parse paresseux). Interne → public pour EquipSlotsPanel.
    /// Thread-safe : la table est construite en local puis publiée —
    /// le pré-chauffage de <see cref="Build"/> tourne en tâche de fond
    /// pendant que le thread principal peut déjà lire.</summary>
    internal static void EnsureItemTable()
    {
        if (Volatile.Read(ref _items) != null) return;
        var path = AssetCatalog.ResolveRawAsset("data/db/c_item.ini");
        try
        {
            var items = path != null
                ? ItemTable.Load(path)
                : new Dictionary<int, ItemTable.ItemDef>();
            // Items du mall (costumes, montures, ids 40001+...) : seuls
            // dans c_itemmall.ini — sans eux ~60 % des cartes d'Archive et
            // les costumes d'inventaire n'avaient ni icône ni catégorie.
            var mall = AssetCatalog.ResolveRawAsset("data/db/c_itemmall.ini");
            if (mall != null)
                foreach (var (id, def) in ItemTable.Load(mall))
                    items.TryAdd(id, def);
            // Publication atomique : le dictionnaire est COMPLET avant
            // d'être visible (le cas échéant un second parse concurrent
            // produit un contenu identique — bénin).
            Volatile.Write(ref _items, items);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[hud] c_item.ini : {e.Message}");
            Volatile.Write(ref _items,
                new Dictionary<int, ItemTable.ItemDef>());
        }
        // Noms/descriptions EN : TextResolver (t_item.ini) — chargé
        // paresseusement au premier tooltip.
    }

    /// <summary>Remplit la grille depuis le 312 parsé (snapshot
    /// complet — bags &gt;0 connus mais seul le sac 0 est affiché).</summary>
    public void SetItems(IReadOnlyList<InventorySlot> items)
    {
        _slots.Clear();
        _sharedSlots.Clear();
        foreach (var it0 in items)
        {
            // Empilable : le 312 ne pose pas le bit6 — la quantité est la
            // durabilité (même convention que 274, ZonePutItem.Amount).
            var it = it0.Amount == 0 && it0.Durability > 1 &&
                     LookupDef(it0.ItemId) is { MaxStack: > 1 }
                ? it0 with { Amount = it0.Durability } : it0;
            _slots[(it.Bag, it.Slot)] = it;
            _sharedSlots[(it.Bag, it.Slot)] = it;
        }
        RepaintCells();
    }

    /// <summary>Un repaint des cellules est attendu à la prochaine
    /// ouverture (mutation reçue fenêtre cachée).</summary>
    private bool _repaintPending;

    /// <summary>Repeint la grille seulement si la fenêtre est visible —
    /// sinon marque un repaint pour la réouverture. Les dicts
    /// <see cref="_slots"/>/<see cref="_sharedSlots"/> sont déjà à jour
    /// (SharedSlots sert les compteurs du tracker de quêtes : il faut
    /// donc lever <see cref="SlotsChanged"/> dans les deux cas).</summary>
    private void RepaintCells()
    {
        if (_window?.IsVisibleInTree() == true)
        {
            _repaintPending = false;
            RefreshCells();
        }
        else
        {
            _repaintPending = true;
            SlotsChanged?.Invoke();
        }
    }

    /// <summary>Icône + tooltip du sac équipé sur chaque emplacement
    /// du porte-sacs (holder 0 lu du 312 / acks 277).</summary>
    private void RefreshBagSlots()
    {
        for (var i = 0; i < _bagSlots.Count; i++)
        {
            var slot = _bagSlots[i];
            var tr = slot.GetNodeOrNull<TextureRect>("bagicon");
            (int ItemId, int Capacity) eb = (0, 0);
            lock (NativeInventoryParser.EquippedBags)
                NativeInventoryParser.EquippedBags.TryGetValue(i, out eb);
            if (eb.ItemId == 0)
            {
                tr?.QueueFree();
                slot.TooltipText = i == 0
                    ? UiTextEn.Translate("Backpack")
                    : UiTextEn.Translate($"Bag {i}");
                continue;
            }
            if (tr == null)
            {
                tr = new TextureRect
                {
                    Name = "bagicon",
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum
                        .KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                slot.AddChild(tr);
                tr.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            }
            var def = LookupDef(eb.ItemId);
            tr.Texture = def is { Code.Length: > 0 }
                ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png")
                : null;
            slot.TooltipText =
                $"{def?.Name ?? $"#{eb.ItemId}"} — {eb.Capacity} " +
                UiTextEn.Translate("slots");
        }
    }

    /// <summary>Re-peint les cellules du sac 0 depuis l'état
    /// <see cref="_slots"/> — onglet All = position réelle ; onglets
    /// filtrés = items correspondants compactés (le slot réel reste
    /// porté par la cellule pour drag/drop/double-clic).</summary>
    private void RefreshCells()
    {
        SlotsChanged?.Invoke();
        EnsureItemTable();
        RefreshBagSlots();
        // Vue unifiée « Extension » : sac de base puis chaque sac équipé
        // (1-4) à la suite — les cases s'ajoutent, rien ne disparaît.
        var segs = new List<(int Bag, int Cap)>();
        for (var b = 0; b <= 4; b++)
        {
            int real;
            lock (NativeInventoryParser.Capacities)
                real = NativeInventoryParser.Capacities.GetValueOrDefault(b, 0);
            var maxUsed = _slots.Keys.Where(k => k.Item1 == b)
                .Select(k => k.Item2 + 1).DefaultIfEmpty(0).Max();
            var cap = b == 0 ? Math.Max(real > 0 ? real : BaseCapacity, maxUsed)
                             : Math.Max(real, maxUsed);
            if (cap > 0) segs.Add((b, cap));
        }
        if (_tab == BagTab.All)
        {
            var idx = 0;
            var usedTotal = segs.Sum(sg => sg.Cap);
            var shown = (usedTotal + _cols - 1) / _cols * _cols;
            foreach (var (bag, cap) in segs)
                for (var sl = 0; sl < cap && idx < _cells.Count; sl++, idx++)
                {
                    var cell = _cells[idx];
                    _slots.TryGetValue((bag, sl), out var it);
                    cell.Bag = bag;
                    cell.Slot = sl;
                    cell.Visible = true;
                    cell.Modulate = Colors.White;
                    cell.SetItem(it);
                }
            for (; idx < _cells.Count; idx++)
            {
                var cell = _cells[idx];
                cell.Bag = 0;
                cell.Slot = -1;
                cell.SetItem(null);
                cell.Visible = idx < shown;
                cell.Modulate = new Color(1, 1, 1, 0.35f);
            }
            return;
        }
        var i = 0;
        foreach (var it in _slots.Values
                     .OrderBy(s => s.Bag).ThenBy(s => s.Slot))
        {
            if (!MatchesTab(it)) continue;
            if (i >= _cells.Count) break;
            _cells[i].Bag = it.Bag;
            _cells[i].Slot = it.Slot;
            _cells[i].SetItem(it);
            i++;
        }
        for (; i < _cells.Count; i++)
        {
            _cells[i].Bag = 0;
            _cells[i].Slot = -1;
            _cells[i].SetItem(null);
        }
    }

    /// <summary>Applique un delta sur l'état local puis repeint la
    /// cellule (et le partage statique pour EquipSlotsPanel).</summary>
    private void MutateSlot(int bag, int slot, InventorySlot? value)
    {
        if (value == null)
        {
            _slots.Remove((bag, slot));
            _sharedSlots.Remove((bag, slot));
        }
        else
        {
            _slots[(bag, slot)] = value;
            _sharedSlots[(bag, slot)] = value;
        }
        // Re-peint complet : sous un onglet filtré la cellule ne
        // correspond plus forcément au même slot.
        RepaintCells();
    }

    // ------------------------------------------------------------------
    // Binding réseau paresseux (deltas live)
    // ------------------------------------------------------------------

    public override void _Process(double delta)
    {
        UpdateHeld();
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[inv] dispatch : {ex}");
            }
        }
    }

    /// <summary>Abonnements NCID — handlers sur le thread réseau,
    /// mutations repostées dans <see cref="_pending"/>.</summary>
    private void BindSession(EdenSession s)
    {
        _session = s;
        // 274 PutItemIntoInventory → ajout/remplacement de slot
        // (codec ZoneCombatMessages.cs — tête seule, suffisant).
        s.OnZone(274, r =>
        {
            var m = new ZonePutItem();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                // Amount = Durability n'a de sens que pour un empilable :
                // sur un équipement (arme/armure) c'est la durabilité
                // (sinon pastille « 100 » sur chaque pièce déséquipée).
                var stackable = LookupDef(m.ItemId) is { MaxStack: > 1 };
                MutateSlot(m.Bag, m.Slot,
                    new InventorySlot(m.Bag, m.Slot, m.ItemId,
                        m.Durability, m.MaxDurability, m.Flags,
                        stackable ? m.Amount : 0, m.Strengthen));
                PlayerBag.ApplyPutItem(m);
            });
        });
        // 275 ClientDropItem type 0 → slot vidé (type 1 = équipement,
        // géré par PlayerEquipment).
        s.OnZone(275, r =>
        {
            var m = new ZoneDropItem();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                if (m.ContainerType == 0)
                    MutateSlot(m.ContainerIndex, m.Slot, null);
                PlayerBag.ApplyDropItem(m);
            });
        });
        // 291 ModifyItemDurability → count/durabilité d'un slot.
        s.OnZone(291, r =>
        {
            var m = new ZoneModifyItemSlot();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                if (m.ContainerType == 0 &&
                    _slots.TryGetValue((m.ContainerIndex, m.Slot),
                        out var it))
                    MutateSlot(m.ContainerIndex, m.Slot, it with
                    {
                        Amount = m.Count,
                        MaxDurability = m.MaxDurability,
                    });
                PlayerBag.ApplyModifySlot(m);
            });
        });
        // 277 ack de SwitchItemLocation — application locale du move
        // (le serveur n'émet aucun 274/275 pour un déplacement 0→0 ;
        // les champs dst sont RÉSOLUS — jamais de −1).
        s.OnZone(277, r =>
        {
            var m = new ZoneSwitchItemLocationOk();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplySwitchAck(m));
        });
        // 620 ack de ArrangeItem : le serveur a trié le sac par ItemId
        // (stable) — on applique le même tri localement.
        s.OnZone(620, r =>
        {
            var m = new ZoneClientArrangeItemOK();
            m.Deserialize(r);
            _pending.Enqueue(() => ApplyArrange(m.Field2));
        });
        // 303 ack d'usage — feedback + log (l'effet HP/MP arrive via
        // les 280 bindés par GameScene).
        s.OnZone(303, r =>
        {
            var m = new ZoneClientItemUse();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                var name = m.ItemId != 0
                    ? TextResolver.ItemName(m.ItemId) ?? $"#{m.ItemId}"
                    : "item";
                GD.Print($"[live] ItemUse ack item={m.ItemId} " +
                         $"result={m.Result} ({name})");
            });
        });
        GD.Print("[inv] binds 274/275/277/291/303 posés");
    }

    /// <summary>Application locale d'un move confirmé (277 ok).
    /// Sac→sac : déplace l'item au dst résolu (swap si occupé —
    /// sémantique SwitchItemLocationAsync serveur). Vers équipement :
    /// retire du sac (l'état porté arrive par le 282). Depuis
    /// équipement : pose au dst résolu (loc vidé via 282 aussi).</summary>
    private void ApplyArrange(int bag)
    {
        var items = _slots.Values.Where(i => i.Bag == bag)
            .OrderBy(i => i.ItemId).ToList();
        foreach (var it in items)
        {
            _slots.Remove((bag, it.Slot));
            _sharedSlots.Remove((bag, it.Slot));
        }
        for (var i = 0; i < items.Count; i++)
        {
            var moved = items[i] with { Slot = i };
            _slots[(bag, i)] = moved;
            _sharedSlots[(bag, i)] = moved;
        }
        RefreshCells();
    }

    private void ApplySwitchAck(ZoneSwitchItemLocationOk m)
    {
        if (!m.Ok)
        {
            GD.Print($"[live] SwitchItem refusé " +
                $"({m.SrcType},{m.SrcIndex},{m.SrcSlot})" +
                $"->({m.DstType},{m.DstIndex},{m.DstSlot})");
            return;
        }
        // Sac → sac : move/swap local.
        if (m.SrcType == 0 && m.DstType == 0)
        {
            var src = _slots.GetValueOrDefault(
                (m.SrcIndex, m.SrcSlot));
            var dst = _slots.GetValueOrDefault(
                (m.DstIndex, m.DstSlot));
            if (src != null)
            {
                // L'InventorySlot porte son propre (Bag, Slot) : il
                // doit suivre le déplacement, sinon la cellule garde
                // l'ancien emplacement (drag suivant → mauvais slot).
                MutateSlot(m.DstIndex, m.DstSlot, src with
                    { Bag = m.DstIndex, Slot = m.DstSlot });
                MutateSlot(m.SrcIndex, m.SrcSlot, dst == null ? null
                    : dst with { Bag = m.SrcIndex, Slot = m.SrcSlot });
            }
        }
        // Gear familier (conteneur elf, type 6) : le modèle ElfBook
        // applique le move, le sac suit (occupant échangé).
        else if (m.SrcType == 6 || m.DstType == 6)
        {
            var sb = _slots.GetValueOrDefault((m.SrcIndex, m.SrcSlot));
            var db = _slots.GetValueOrDefault((m.DstIndex, m.DstSlot));
            var (nSrc, nDst) = ElfBook.Shared.ApplyPetSwitch(m.SrcType,
                m.SrcSlot, m.SrcIndex, m.DstType, m.DstSlot, m.DstIndex,
                sb?.ItemId ?? 0, db?.ItemId ?? 0);
            if (m.SrcType == 0)
                MutateSlot(m.SrcIndex, m.SrcSlot, nSrc == 0 ? null
                    : new InventorySlot(m.SrcIndex, m.SrcSlot, nSrc, 0, 0, 0));
            if (m.DstType == 0)
                MutateSlot(m.DstIndex, m.DstSlot, nDst == 0 ? null
                    : new InventorySlot(m.DstIndex, m.DstSlot, nDst, 0, 0, 0));
        }
        // Sac → équipement : le slot sac se vide (l'occupant de la loc
        // cible repart dans le sac au slot libéré — swap natif).
        else if (m.SrcType == 0 && m.DstType == 1)
        {
            // Si le serveur a renvoyé l'ancienne pièce, elle arrive
            // par un 274 — on ne fait que vider la source.
            // A6.6 : page costume (conteneurs 2-4) → état local dédié.
            if (m.DstIndex is >= 2 and <= 4 &&
                _slots.GetValueOrDefault((m.SrcIndex, m.SrcSlot)) is { } ci)
                PlayerEquipment.SetCostume(m.DstIndex, m.DstSlot, ci.ItemId);
            MutateSlot(m.SrcIndex, m.SrcSlot, null);
        }
        // Sac → porte-sacs : l'item-sac quitte son slot ; la capacité
        // du sac d'inventaire ouvert = celle du sac équipé.
        else if (m.SrcType == 0 && m.DstType == 5)
        {
            var bag = _slots.GetValueOrDefault((m.SrcIndex, m.SrcSlot));
            // Swap (emplacement déjà occupé) : le sac sortant reprend la
            // place du nouveau (serveur : MoveBagToInventoryAsync).
            (int ItemId, int Capacity) prev;
            lock (NativeInventoryParser.EquippedBags)
                NativeInventoryParser.EquippedBags.TryGetValue(
                    m.DstSlot, out prev);
            MutateSlot(m.SrcIndex, m.SrcSlot, prev.ItemId == 0 ? null
                : new InventorySlot(m.SrcIndex, m.SrcSlot, prev.ItemId,
                    0, 0, 0));
            var cap = bag != null
                ? LookupDef(bag.ItemId)?.BagCapacity ?? 0 : 0;
            lock (NativeInventoryParser.Capacities)
                NativeInventoryParser.Capacities[m.DstSlot] = cap;
            lock (NativeInventoryParser.EquippedBags)
                NativeInventoryParser.EquippedBags[m.DstSlot] =
                    (bag?.ItemId ?? 0, cap);
            RefreshCells();
        }
        // Porte-sacs → sac : capacité du sac d'inventaire remise à 0 ;
        // l'item-sac se pose au dst résolu (id relu de l'état holder).
        else if (m.SrcType == 5 && m.DstType == 0)
        {
            lock (NativeInventoryParser.Capacities)
                NativeInventoryParser.Capacities[m.SrcSlot] = 0;
            var bagItem = 0;
            lock (NativeInventoryParser.EquippedBags)
            {
                if (NativeInventoryParser.EquippedBags
                        .TryGetValue(m.SrcSlot, out var eb))
                    bagItem = eb.ItemId;
                NativeInventoryParser.EquippedBags.Remove(m.SrcSlot);
            }
            if (bagItem != 0)
                MutateSlot(m.DstIndex, m.DstSlot, new InventorySlot(
                    m.DstIndex, m.DstSlot, bagItem, 0, 0, 0));
            RefreshCells();
        }
        // Porte-sacs → porte-sacs : échange des locs ET des capacités.
        else if (m.SrcType == 5 && m.DstType == 5)
        {
            lock (NativeInventoryParser.Capacities)
            {
                var cs = NativeInventoryParser.Capacities
                    .GetValueOrDefault(m.SrcSlot);
                var cd = NativeInventoryParser.Capacities
                    .GetValueOrDefault(m.DstSlot);
                NativeInventoryParser.Capacities[m.SrcSlot] = cd;
                NativeInventoryParser.Capacities[m.DstSlot] = cs;
            }
            lock (NativeInventoryParser.EquippedBags)
            {
                var has = NativeInventoryParser.EquippedBags
                    .TryGetValue(m.SrcSlot, out var es);
                var had = NativeInventoryParser.EquippedBags
                    .TryGetValue(m.DstSlot, out var ed);
                if (had) NativeInventoryParser.EquippedBags[m.SrcSlot] = ed;
                else NativeInventoryParser.EquippedBags.Remove(m.SrcSlot);
                if (has) NativeInventoryParser.EquippedBags[m.DstSlot] = es;
                else NativeInventoryParser.EquippedBags.Remove(m.DstSlot);
            }
            RefreshCells();
        }
        // A6.6 : déséquipe une pièce de page costume (SrcIndex = page).
        else if (m.SrcType == 1 && m.DstType == 0 &&
                 m.SrcIndex is >= 2 and <= 4)
        {
            var cid = PlayerEquipment.CostumeAt(m.SrcIndex, m.SrcSlot);
            PlayerEquipment.SetCostume(m.SrcIndex, m.SrcSlot, 0);
            if (cid != 0)
                MutateSlot(m.DstIndex, m.DstSlot, new InventorySlot(
                    m.DstIndex, m.DstSlot, cid, 0, 0, 0));
        }
        // Équipement → sac : l'item déséquipé se pose au dst résolu.
        // Le 277 ne porte pas l'itemId : on le lit dans l'état porté
        // (PlayerEquipment.Worn, mis à jour par le 282 qui suit).
        else if (m.SrcType == 1 && m.DstType == 0 &&
                 PlayerEquipment.Worn.TryGetValue(m.SrcSlot, out var worn) &&
                 worn != 0)
        {
            MutateSlot(m.DstIndex, m.DstSlot, new InventorySlot(
                m.DstIndex, m.DstSlot, worn, 0, 0, 0));
            // Le loc n'est plus porté (set 0 seulement) : l'état porté
            // et le modèle 3D suivent — cas de l'auto-déséquipement
            // d'une arme (arc ↔ 1H), pour lequel aucun 282 ne suit.
            if (m.SrcIndex == 0) PlayerEquipment.RemoveWorn(m.SrcSlot);
        }
    }

    // ------------------------------------------------------------------
    // Interactions — envoi réseau
    // ------------------------------------------------------------------

    /// <summary>Envoie un message zone (erreurs loguées).</summary>
    public override void _Notification(int what)
    {
        // Drag d'un item de sac lâché sur aucune cible valide (hors
        // slot, hors fenêtre) → proposition de jeter = détruire (le
        // DropItem 6 natif érade l'item, pas de drop au sol).
        if (what != NotificationDragEnd) return;
        var vp = GetViewport();
        if (vp == null || vp.GuiIsDragSuccessful()) return;
        var data = vp.GuiGetDragData();
        if (data.VariantType != Variant.Type.Dictionary) return;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("src") || d["src"].AsString() != "inv") return;
        if (!IsVisibleInTree()) return;
        AskDiscard(d["bag"].AsInt32(), d["slot"].AsInt32(),
            d["itemId"].AsInt32());
    }

    private ConfirmationDialog? _discardDlg;

    private void AskDiscard(int bag, int slot, int itemId)
    {
        var def = LookupDef(itemId);
        var name = def?.Name is { Length: > 0 } n ? n : $"#{itemId}";
        _discardDlg?.QueueFree();
        var dlg = new ConfirmationDialog
        {
            Title = UiTextEn.Translate("Discard"),
            DialogText = UiTextEn.Translate("Discard %s1? The item will be destroyed.")
                .Replace("%s1", name),
            OkButtonText = UiTextEn.Translate("Discard"),
            CancelButtonText = UiTextEn.Translate("Cancel"),
            Exclusive = true,
        };
        dlg.Confirmed += () =>
        {
            Send(new ZoneServerDropItem
            {
                ContainerType = 0, Slot = (short)slot,
                ContainerIndex = (short)bag,
            });
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        _discardDlg = dlg;
        AddChild(dlg);
        dlg.PopupCentered();
    }

    /// <summary>A6.5 — bouton « Archive » : fenêtre ArchiveWindow
    /// (nœud monté par HudExtensions).</summary>
    private void OpenArchive()
    {
        if (GetTree().Root.FindChild("ArchiveWndPanel", true, false)
            is Control w)
            HudWindows.Toggle(w);
        else Notice(UiTextEn.Translate("Archive unavailable"));
    }

    /// <summary>Envoi depuis la sonde <see cref="BagTipProbe"/>.</summary>
    internal void SendProbe(NetMessage msg) => Send(msg);

    private void Send(NetMessage msg)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        var send = s.SendZoneAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(
            t => GD.PrintErr($"[inv] {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Double-clic cellule : équipe si équipable (NCID 7
    /// 0→1 + NCID 8 mask → 282), utilise si consommable
    /// (NCID 19 ItemUse).</summary>
    /// <summary>M3 : active (équipe / utilise) le 1er exemplaire de
    /// l'objet — raccourci de hotbar. false si plus aucun en sac.</summary>
    public bool UseByItemId(int itemId)
    {
        foreach (var ((bag, slot), it) in _slots)
            if (it.ItemId == itemId)
            {
                OnCellDoubleClick(bag, slot);
                return true;
            }
        return false;
    }

    /// <summary>Centre global de la cellule affichant (bag, slot) — sonde :
    /// injection d'un vrai clic (XTEST-like via Input.ParseInputEvent).</summary>
    internal Vector2? CellCenter(int bag, int slot) =>
        _cells.FirstOrDefault(c => c.Visible && c.Bag == bag &&
            c.Slot == slot) is { } cell
            ? cell.GetGlobalRect().GetCenter() : null;

    internal void OnCellDoubleClick(int bag, int slot)
    {
        if (!_slots.TryGetValue((bag, slot), out var it)) return;
        foreach (var c in _cells)
            if (c.Slot == slot && c.Bag == bag && c.Visible) c.Shake();
        var def = LookupDef(it.ItemId);
        if (def == null) return;
        // Équipable joueur : kinds 1-34/37-40 + loc résolue —
        // flux natif : SwitchItemLocation puis UpdateEquipment.
        if (ZoneItemActions.IsPlayerEquippableKind(def.Kind) &&
            ZoneItemActions.AllowedLocations(def.EquipLoc).Length > 0)
        {
            Send(ZoneItemActions.EquipRequest(bag, slot, def.EquipLoc,
                PlayerEquipment.Worn));
            Send(new ZoneUpdateEquipment
            {
                UpdateFlags = ZoneItemActions.LocMask(def.EquipLoc),
            });
            GD.Print($"[inv] équipe {it.ItemId} (sac{bag},slot{slot}) " +
                     $"type={def.EquipLoc}");
            return;
        }
        // Sac d'extension (kind 43, col28 = cases) : ni équipable joueur
        // (kind hors 1-34/37-40) ni UseType → ce double-clic / clic droit
        // ne faisait rien. Natif : SwitchItemLocation inventaire →
        // porte-sacs (0→5), premier emplacement 1-4 libre.
        if (def.Kind == 0x2b && def.BagCapacity > 0)
        {
            var holder = FirstFreeBagHolder();
            if (holder == 0)
            {
                Notice(UiTextEn.Translate("All bag slots are in use: drop the bag on a bag slot to swap it."));
                return;
            }
            Send(ZoneItemActions.BagEquipRequest(bag, slot, holder));
            GD.Print($"[inv] équipe le sac {it.ItemId} (sac{bag},slot{slot}) " +
                     $"-> porte-sacs {holder} (+{def.BagCapacity} cases)");
            return;
        }
        // Consommable (types d'usage 1/2 — potions, nourriture) ou
        // tout item avec type d'usage non nul : ItemUse natif.
        if (def.UseType != 0)
        {
            if (EdenClient.Combat.PlayerStateLocks.NoUseItem) return;
            Send(new ZoneItemUse
            {
                ContainerType = 0, ContainerIndex = (short)bag,
                Slot = (short)slot,
            });
            GD.Print($"[inv] utilise {it.ItemId} (sac{bag},slot{slot}) " +
                     $"useType={def.UseType}");
        }
    }

    /// <summary>Premier emplacement 1-4 du porte-sacs sans sac équipé
    /// (0 = tous pris ; le sac de base 0 n'est pas remplaçable).</summary>
    private static int FirstFreeBagHolder()
    {
        lock (NativeInventoryParser.EquippedBags)
            for (var l = 1; l <= 4; l++)
                if (!NativeInventoryParser.EquippedBags.ContainsKey(l))
                    return l;
        return 0;
    }

    /// <summary>Drop d'un item draggé sur la cellule
    /// <paramref name="dstSlot"/> du sac 0 : Shift = split
    /// (popup quantité → NCID 84), sinon move 0→0 (NCID 7).</summary>
    internal void OnCellDrop(int srcBag, int srcSlot, int dstBag, int dstSlot,
        bool split)
    {
        if (srcSlot == dstSlot && srcBag == dstBag) return;
        if (!_slots.TryGetValue((srcBag, srcSlot), out var it)) return;
        var amount = it.Amount > 0 ? it.Amount : 1;
        if (split && amount > 1)
        {
            ShowSplitPopup(srcBag, srcSlot, dstBag, dstSlot, amount);
            return;
        }
        Send(ZoneItemActions.MoveRequest(srcBag, srcSlot, dstBag,
            dstSlot));
    }

    // ---------- prendre / poser (clic gauche) ----------

    /// <summary>Objet pris en main (suit le curseur) — null = rien.</summary>
    public static (int Bag, int Slot)? Held { get; private set; }

    private TextureRect? _heldIcon;

    /// <summary>Annule la prise en main.</summary>
    public static void CancelHeld() => Held = null;

    /// <summary>Clic gauche relâché dans le vide (aucun Control sous le
    /// curseur) avec un objet en main : on le repose (même pattern que
    /// HotbarPanel._UnhandledInput).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (Held != null &&
            e is InputEventMouseButton { ButtonIndex: MouseButton.Left,
                Pressed: false } &&
            GetViewport().GuiGetHoveredControl() == null)
            CancelHeld();
    }

    /// <summary>Consomme l'objet en main (cible équipement) : renvoie
    /// (sac, slot) et vide la main.</summary>
    public static (int Bag, int Slot)? TakeHeld()
    {
        var h = Held;
        Held = null;
        return h;
    }

    private void OnCellClicked(ItemCell cell)
    {
        // A6.7 : CTRL+clic = prévisualisation (mannequin) sans prendre l'item.
        if (Held == null && Input.IsKeyPressed(Key.Ctrl) &&
            cell.Item is { ItemId: not 0 } pv)
        {
            ItemPreviewWindow.Preview(pv.ItemId);
            return;
        }
        if (Held is { } h)
        {
            if (cell.Slot < 0) return;
            Held = null;
            if (h.Bag == cell.Bag && h.Slot == cell.Slot) return;
            OnCellDrop(h.Bag, h.Slot, cell.Bag, cell.Slot,
                Input.IsKeyPressed(Key.Shift));
        }
        else if (cell.Item is { ItemId: not 0 } it)
            Held = (it.Bag, it.Slot);
    }

    private void OnCellRightClicked(ItemCell cell)
    {
        if (Held != null) { Held = null; return; }
        if (cell.Item is not { } it || it.ItemId == 0) return;
        // Clic droit natif = utiliser / équiper directement (Ctrl+clic
        // reste le chemin de prévisualisation). Un équipement
        // « Bind on Equip » non lié demande confirmation d'abord.
        var def = LookupDef(it.ItemId);
        if (def != null && ItemTipFormat.BindsOnEquip(def.Flags) &&
            !ItemTipFormat.IsBound(it.Flags))
            AskBind(it.Bag, it.Slot, it.ItemId);
        else
            OnCellDoubleClick(it.Bag, it.Slot);
    }

    private ConfirmationDialog? _bindDlg;

    private void AskBind(int bag, int slot, int itemId)
    {
        var def = LookupDef(itemId);
        var name = def?.Name is { Length: > 0 } n ? n : $"#{itemId}";
        _bindDlg?.QueueFree();
        var dlg = new ConfirmationDialog
        {
            Title = UiTextEn.Translate("Bind Equipment"),
            DialogText = UiTextEn.Translate(
                "Equip %s1? It will be bound to you.")
                .Replace("%s1", name),
            OkButtonText = UiTextEn.Translate("Equip"),
            CancelButtonText = UiTextEn.Translate("Cancel"),
            Exclusive = true,
        };
        dlg.Confirmed += () =>
        {
            OnCellDoubleClick(bag, slot);
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        _bindDlg = dlg;
        AddChild(dlg);
        dlg.PopupCentered();
    }

    /// <summary>Icône qui suit le curseur + grisé de la case source.</summary>
    private void UpdateHeld()
    {
        if (Held is { } h && (!_slots.ContainsKey((h.Bag, h.Slot)) ||
                              !IsVisibleInTree()))
            Held = null;
        foreach (var c in _cells)
            c.SetGhost(Held is { } hh && c.Bag == hh.Bag &&
                       c.Slot == hh.Slot);
        if (Held is not { } cur)
        {
            if (_heldIcon != null) _heldIcon.Visible = false;
            return;
        }
        if (_heldIcon == null)
        {
            _heldIcon = new TextureRect
            {
                TopLevel = true, ZIndex = 4000,
                MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(CellSize, CellSize),
                Size = new Vector2(CellSize, CellSize),
                Modulate = new Color(1, 1, 1, 0.9f),
            };
            AddChild(_heldIcon);
        }
        var def = LookupDef(_slots[(cur.Bag, cur.Slot)].ItemId);
        _heldIcon.Texture = def is { Code.Length: > 0 }
            ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png") : null;
        _heldIcon.Visible = true;
        _heldIcon.GlobalPosition = GetGlobalMousePosition() +
            new Vector2(6, 6);
    }

    // ---------- popup split ----------

    private Window? _splitPopup;

    /// <summary>Fenêtre « séparer le stack » : slider + bouton →
    /// NCID 84 DivideItem [srcSlot][srcSac][dstSlot][dstSac][qté][0].
    /// </summary>
    private void ShowSplitPopup(int srcBag, int srcSlot, int dstBag,
        int dstSlot, int amount)
    {
        _splitPopup?.QueueFree();
        var win = new Window
        {
            Title = UiTextEn.Translate("Split stack"),
            Size = new Vector2I(240, 90),
            Exclusive = true, Transient = true,
            Unresizable = true,
        };
        var vbox = new VBoxContainer();
        var slider = new HSlider
        {
            MinValue = 1, MaxValue = amount - 1,
            Value = amount / 2, Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var count = new Label { Text = $"{(int)slider.Value}",
            HorizontalAlignment = HorizontalAlignment.Center };
        slider.ValueChanged += v => count.Text = $"{(int)v}";
        var ok = new Button { Text = UiTextEn.Translate("Split") };
        ok.Pressed += () =>
        {
            Send(new ZoneDivideItem
            {
                SrcSlot = (short)srcSlot, SrcBag = (short)srcBag,
                DstSlot = (short)dstSlot, DstBag = (short)dstBag,
                Count = (short)slider.Value, Kind = 0,
            });
            win.QueueFree();
            _splitPopup = null;
        };
        var cancel = new Button { Text = UiTextEn.Translate("Cancel") };
        cancel.Pressed += () => { win.QueueFree(); _splitPopup = null; };
        win.CloseRequested += () => { win.QueueFree(); _splitPopup = null; };
        vbox.AddChild(slider);
        vbox.AddChild(count);
        var btns = new HBoxContainer();
        btns.AddChild(ok); btns.AddChild(cancel);
        vbox.AddChild(btns);
        win.AddChild(vbox);
        win.Position = (Vector2I)GetGlobalMousePosition();
        AddChild(win);
        win.Popup();
        _splitPopup = win;
    }

    // ------------------------------------------------------------------
    // Cellules
    // ------------------------------------------------------------------

    private static GridContainer MakeGrid()
    {
        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        return grid;
    }

    /// <summary>Construit les cellules interactives (drag source +
    /// drop cible + double-clic + tooltip riche).</summary>
    private void EnsureCells(HudRoot hud)
    {
        if (_grid == null || _cells.Count > 0) return;
        var empty = EeSkin.EmptySlot(hud);
        for (var i = 0; i < CellTotal; i++)
        {
            var cell = new ItemCell(i, empty)
            {
                CustomMinimumSize = new Vector2(CellSize, CellSize),
                // Logo épée natif des montures de combat (coin bas-gauche
                // de l'icône d'item ; atlas uidata/imagesets/main.png).
                RideBadge = hud.Tex?.GetRegion(
                    new CeguiImageRef("main", "AttackIcon")),
            };
            cell.Clicked += OnCellClicked;
            cell.RightClicked += OnCellRightClicked;
            cell.ItemDropped += OnCellDrop;
            _cells.Add(cell);
            _grid.AddChild(cell);
        }
    }

    /// <summary>
    /// Cellule d'inventaire : icône + quantité + double-clic
    /// (équipe/usage) + drag source ({bag,slot}) + drop cible +
    /// tooltip riche <see cref="ItemTooltip"/>.
    /// </summary>
    private sealed partial class ItemCell : Panel
    {
        /// <summary>Index de cellule dans la grille (position fixe).</summary>
        public int Index { get; }
        /// <summary>Slot réel du sac que la cellule adresse (−1 =
        /// cellule décorative d'un onglet filtré — drop refusé).</summary>
        public int Slot { get; set; } = -1;
        /// <summary>Sac que la cellule adresse (vue unifiée : sac de base
        /// puis extensions).</summary>
        public int Bag { get; set; }
        /// <summary>Drop d'un item → (srcSac, srcSlot, dstSac, dstSlot, split).</summary>
        public event Action<int, int, int, int, bool>? ItemDropped;

        private readonly TextureRect _icon;
        private readonly Label _qty;
        private TextureRect? _rideBadge;
        private InventorySlot? _item;

        /// <summary>Texture du logo épée (monture de combat), injectée
        /// par <see cref="EnsureCells"/> — null = badge désactivé.</summary>
        public Texture2D? RideBadge { get; set; }

        public ItemCell(int index, StyleBox? emptySlot)
        {
            Index = index;
            // Case fleur-de-lys native (ref Backpack).
            AddThemeStyleboxOverride("panel", emptySlot ?? new StyleBoxFlat
            {
                BgColor = Color.Color8(40, 39, 35),
            });
            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_icon);
            _qty = new Label
            {
                Text = "",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _qty.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _qty.OffsetRight = -2; _qty.OffsetBottom = -1;
            _qty.AddThemeFontSizeOverride("font_size", 11);
            _qty.AddThemeColorOverride("font_color", Colors.White);
            _qty.AddThemeColorOverride("font_outline_color",
                new Color(0f, 0f, 0f));
            _qty.AddThemeConstantOverride("outline_size", 3);
            AddChild(_qty);
            _cd = CooldownOverlay.Attach(this);
            _cd.ShakeTargets = new Control[] { _icon, _qty };
            _cd.Source = () =>
            {
                if (_item == null) return null;
                var d = LookupDef(_item.ItemId);
                if (d == null || d.CooldownDs <= 0) return null;
                return CooldownTracker.Item(d.CooldownGroup,
                    out var rem, out var tot) ? (rem, tot) : null;
            };
        }

        private readonly CooldownOverlay _cd;

        /// <summary>Vibre la cellule (retour visuel d'usage).</summary>
        public void Shake() => _cd.Shake();

        /// <summary>Item affiché (null = vide).</summary>
        public InventorySlot? Item => _item;

        public void SetItem(InventorySlot? it)
        {
            _item = it;
            Texture2D? tex = null;
            ItemTable.ItemDef? def = null;
            if (it != null && it.ItemId != 0)
            {
                def = LookupDef(it.ItemId);
                if (def != null && def.Code.Length > 0)
                    tex = AssetCatalog.GetTexture(
                        $"UI/itemicon/{def.Code}.png");
            }
            _icon.Texture = tex;
            _qty.Text = it is { Amount: > 1 }
                ? it.Amount.ToString() : "";
            // Badge épée natif : items « monture de combat » (enchant
            /// d'usage 2134 type 2, ref MountCombatRules.IsCombatRide).
            var combat = def != null &&
                Game.MountCombatRules.IsCombatRide(def.UseEnchantId);
            if (combat && RideBadge != null)
            {
                _rideBadge ??= new TextureRect
                {
                    Texture = RideBadge,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                if (_rideBadge.GetParent() == null) AddChild(_rideBadge);
                _rideBadge.SetAnchorsAndOffsetsPreset(
                    LayoutPreset.BottomLeft);
                var bs = CellSize * 0.42f;
                _rideBadge.CustomMinimumSize = new Vector2(bs, bs);
                _rideBadge.OffsetLeft = 1; _rideBadge.OffsetTop = -bs - 1;
                _rideBadge.OffsetRight = 1 + bs; _rideBadge.OffsetBottom = -1;
                _rideBadge.Visible = true;
            }
            else if (_rideBadge != null)
                _rideBadge.Visible = false;
            ItemTooltip.Bind(this, () => _item);
        }

        /// <summary>Clic gauche relâché sur la cellule (prendre/poser).</summary>
        public event Action<ItemCell>? Clicked;
        /// <summary>Clic droit sur la cellule.</summary>
        public event Action<ItemCell>? RightClicked;

        /// <summary>Atténue l'icône (objet en main).</summary>
        public void SetGhost(bool on) =>
            _icon.Modulate = new Color(1, 1, 1, on ? 0.35f : 1f);

        public override void _GuiInput(InputEvent e)
        {
            // Clic gauche (au relâché, après un éventuel drag) = prendre /
            // poser ; clic droit = utiliser / équiper (ou annuler la prise).
            if (e is InputEventMouseButton { Pressed: false,
                    ButtonIndex: MouseButton.Left })
            {
                Clicked?.Invoke(this);
                AcceptEvent();
            }
            else if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Right })
            {
                RightClicked?.Invoke(this);
                AcceptEvent();
            }
        }

        /// <summary>Source de drag : payload {bag, slot} + aperçu =
        /// icône de l'item.</summary>
        public override Variant _GetDragData(Vector2 atPosition)
        {
            GD.Print($"[dnd] ItemCell._GetDragData item={_item?.ItemId}");
            if (_item == null || _item.ItemId == 0) return default;
            var d = new Godot.Collections.Dictionary
            {
                ["bag"] = _item.Bag,
                ["slot"] = _item.Slot,
                ["itemId"] = _item.ItemId,   // M3 : drop vers la hotbar
                ["src"] = "inv",             // jeter si lâché hors cible
            };
            if (_icon.Texture != null)
            {
                var preview = new TextureRect
                {
                    Texture = _icon.Texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    CustomMinimumSize = new Vector2(CellSize, CellSize),
                };
                SetDragPreview(preview);
            }
            return d;
        }

        public override bool _CanDropData(Vector2 atPosition,
            Variant data)
        {
            if (Slot < 0) return false;   // cellule décorative filtrée
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            return d.ContainsKey("bag") && d.ContainsKey("slot");
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            CancelHeld();
            var split = Input.IsKeyPressed(Key.Shift);
            ItemDropped?.Invoke(d["bag"].AsInt32(),
                d["slot"].AsInt32(), Bag, Slot, split);
        }

    }
}
