using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// A6.5 — Archive (« trophy rack » partagé compte, wiki EN Archive).
/// Ouverte depuis le bouton « Archive » du sac (<see cref="InventoryWindow"/>)
/// ou <c>--hud-open=ArchiveWnd</c>. Grille paginée de toutes les cartes
/// archivables (<see cref="CollectionTable"/>) : possédées = pleine
/// couleur, manquantes = assombries. Tout passe par le serveur :
/// <list type="bullet">
/// <item>ouverture → C→S 152 QueryCollection → S→C 472 type 0
/// (liste + points) ;</item>
/// <item>drag d'un item du sac sur la grille → C→S 150 SaveCollection
/// (flag 0, <c>[s16 container][s16 loc]</c>) → 472 type 1 ;</item>
/// <item>clic droit sur une carte possédée → C→S 151 GetCollection
/// (<c>[s16 item_id]</c>) → 472 type 2 + 274 (item rendu au sac).</item>
/// </list>
/// Pied : points totaux + prochain palier de récompense
/// (<c>c_collectionpoint</c> → T_Enchant).
/// </summary>
public partial class ArchiveWindow : Control
{
    // Layout natif CollectionWnd_Ver2 (628×544) : arbre de catégories +
    // zone dépôt à gauche, filtres/grille/recherche en haut à droite,
    // rapport (carte choisie, points, prochaine récompense) en bas à droite.
    private const int Columns = 9, Cell = 40;

    private Panel? _win;
    private GridContainer? _grid;
    private Tree? _tree;
    private Label? _points, _rate, _title;
    private RichTextLabel? _report;
    private LineEdit? _search;
    private CheckBox? _cbGame, _cbMall, _cbOwned, _cbRev;
    private Control? _useSlot;
    private string _cat = "";   // "" = toutes
    private int _selected;
    private EdenSession? _session;

    private static CheckBox Check(Panel win, string text, bool on, int x, int y,
        Action changed)
    {
        var c = new CheckBox
        {
            Text = UiTextEn.Translate(text), ButtonPressed = on,
            FocusMode = FocusModeEnum.None,
        };
        c.AddThemeFontSizeOverride("font_size", 11);
        HudRoot.Pin(c, 0, 0, 0, 0, x, y, x + 200, y + 20);
        c.Toggled += _ => changed();
        win.AddChild(c);
        return c;
    }

    private static Panel Zone(Panel win, int x0, int y0, int x1, int y1)
    {
        var p = new Panel { MouseFilter = MouseFilterEnum.Pass };
        p.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        HudRoot.Pin(p, 0, 0, 0, 0, x0, y0, x1, y1);
        win.AddChild(p);
        return p;
    }

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        if (hud == null) return;
        CollectionTable.PathProvider ??= AssetCatalog.ResolveRawAsset;
        _win = EeSkin.MakeWindow(hud, UiTextEn.Translate("Archive"),
            300, 70, 628, 544);
        _win.Name = "ArchiveWndPanel";
        AddChild(_win);
        HudWindows.Register(_win);
        DragHelper.Persist(_win, "archive");

        // ---- Gauche : arbre des catégories + points ----
        Zone(_win, 8, 34, 190, 454);
        _title = new Label
        {
            Text = UiTextEn.Translate("Archive Categories"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        EeSkin.Shadowed(_title, 12, Color.Color8(240, 230, 190));
        HudRoot.Pin(_title, 0, 0, 0, 0, 16, 40, 170, 60);
        _win.AddChild(_title);
        _tree = new Tree { HideRoot = true, SelectMode = Tree.SelectModeEnum.Single };
        _tree.AddThemeFontSizeOverride("font_size", 11);
        HudRoot.Pin(_tree, 0, 0, 0, 0, 14, 64, 184, 412);
        _tree.ItemSelected += () =>
        {
            _cat = _tree.GetSelected()?.GetMetadata(0).AsString() ?? "";
            Refresh();
        };
        _win.AddChild(_tree);
        var pts = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("Archive Points"),
            FocusMode = FocusModeEnum.None,
        }, 11);
        HudRoot.Pin(pts, 0, 0, 0, 0, 90, 418, 184, 444);
        pts.Pressed += RefreshFooter;
        _win.AddChild(pts);

        // ---- Gauche bas : dépôt (slot + bouton) ----
        Zone(_win, 8, 460, 190, 536);
        var slot = new DropArea(OnDeposit);
        slot.AddThemeStyleboxOverride("panel", EeSkin.GreyPanel(90));
        HudRoot.Pin(slot, 0, 0, 0, 0, 16, 468, 56, 508);
        _win.AddChild(slot);
        _useSlot = slot;
        var dep = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("Archive"),
            FocusMode = FocusModeEnum.None,
            TooltipText = UiTextEn.Translate("Drag an item onto the slot, or drop it on the grid"),
        }, 11);
        HudRoot.Pin(dep, 0, 0, 0, 0, 62, 468, 150, 490);
        dep.Pressed += () =>
        {
            if (InventoryWindow.TakeHeld() is { } h) OnDeposit(h.Bag, h.Slot);
        };
        _win.AddChild(dep);
        var prompt = new Label
        {
            Text = UiTextEn.Translate("Right-click a card to take it out"),
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EeSkin.Shadowed(prompt, 10, Color.Color8(180, 180, 172));
        HudRoot.Pin(prompt, 0, 0, 0, 0, 14, 510, 186, 534);
        _win.AddChild(prompt);

        // ---- Haut droite : filtres, taux, grille, recherche ----
        Zone(_win, 198, 34, 620, 354);
        Action chg = () => Refresh();
        _cbGame = Check(_win, "Show in-game treasures only", true, 206, 40, chg);
        _cbMall = Check(_win, "Show mall treasures only", true, 206, 60, chg);
        _cbOwned = Check(_win, "Show collected only", false, 420, 40, chg);
        _cbRev = Check(_win, "Reverse order", false, 420, 60, chg);
        _rate = new Label { MouseFilter = MouseFilterEnum.Ignore };
        EeSkin.Shadowed(_rate, 12, Color.Color8(240, 200, 70));
        HudRoot.Pin(_rate, 0, 0, 0, 0, 206, 84, 460, 102);
        _win.AddChild(_rate);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        HudRoot.Pin(scroll, 0, 0, 0, 0, 204, 106, 614, 318);
        _win.AddChild(scroll);
        var dropGrid = new DropArea(OnDeposit)
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        dropGrid.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        scroll.AddChild(dropGrid);
        _grid = new GridContainer { Columns = Columns };
        _grid.AddThemeConstantOverride("h_separation", 3);
        _grid.AddThemeConstantOverride("v_separation", 3);
        _grid.MouseFilter = MouseFilterEnum.Pass;
        dropGrid.AddChild(_grid);
        var sl = new Label
        {
            Text = UiTextEn.Translate("Search:"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        EeSkin.Shadowed(sl, 11, Color.Color8(222, 220, 212));
        HudRoot.Pin(sl, 0, 0, 0, 0, 206, 326, 262, 346);
        _win.AddChild(sl);
        _search = new LineEdit();
        HudRoot.Pin(_search, 0, 0, 0, 0, 264, 324, 556, 348);
        _search.TextChanged += _ => Refresh();
        _win.AddChild(_search);
        var clr = EeSkin.Grey(new Button { Text = "x", FocusMode = FocusModeEnum.None }, 11);
        HudRoot.Pin(clr, 0, 0, 0, 0, 562, 324, 588, 348);
        clr.Pressed += () => { _search.Text = ""; Refresh(); };
        _win.AddChild(clr);

        // ---- Bas droite : rapport ----
        Zone(_win, 198, 360, 620, 536);
        _points = new Label { MouseFilter = MouseFilterEnum.Ignore };
        EeSkin.Shadowed(_points, 13, Color.Color8(240, 200, 70));
        HudRoot.Pin(_points, 0, 0, 0, 0, 208, 366, 612, 386);
        _win.AddChild(_points);
        _report = new RichTextLabel
        {
            BbcodeEnabled = true, ScrollActive = true,
        };
        _report.AddThemeFontSizeOverride("normal_font_size", 11);
        _report.AddThemeFontSizeOverride("bold_font_size", 11);
        HudRoot.Pin(_report, 0, 0, 0, 0, 208, 390, 612, 530);
        _win.AddChild(_report);

        _win.Visible = false;
        _win.VisibilityChanged += () =>
        {
            if (_win.Visible) { Query(); BuildTree(); Refresh(); }
        };
        PlayerProfileState.Changed += OnProfileChanged;
        PlayerBag.Changed += OnBagChanged;
    }

    public override void _ExitTree()
    {
        PlayerProfileState.Changed -= OnProfileChanged;
        PlayerBag.Changed -= OnBagChanged;
        base._ExitTree();
    }

    /// <summary>Sac muté : la grille de cartes (jusqu'à 1200 cellules
    /// + tooltips) n'est reconstruite que fenêtre ouverte — la
    /// réouverture relance Query+BuildTree+Refresh
    /// (<c>_win.VisibilityChanged</c>).</summary>
    private void OnBagChanged()
    {
        if (_win?.IsVisibleInTree() == true) Refresh();
    }

    private void OnProfileChanged(string section)
    {
        if (section == "collection") Refresh();
    }

    // ------------------------------------------------------------------
    // Réseau
    // ------------------------------------------------------------------

    private void Send(NetMessage msg)
    {
        _session ??= SessionCarry.Session;
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        var t = s.SendZoneAsync(msg, CancellationToken.None);
        _ = t.ContinueWith(
            x => GD.PrintErr($"[archive] {msg.Name} : {x.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void Query() => Send(new ZoneServerQueryCollection());

    private void OnDeposit(int bag, int slot)
    {
        // Flag 0 = carte unique : [s16 container][s16 loc] (Field2/3).
        Send(new ZoneServerSaveCollection
        {
            Field1 = 0, Field2 = (ushort)bag, Field3 = (short)slot,
        });
    }

    private void OnWithdraw(int itemId) =>
        Send(new ZoneServerGetCollection { Value = (short)itemId });

    // ------------------------------------------------------------------
    // Affichage
    // ------------------------------------------------------------------

    private static HashSet<int> Owned() => PlayerProfileState.ArchiveOwned();

    /// <summary>Col3 de c_collection = onglet natif du trésor ; 5 =
    /// montures (les 229 cartes à enchant 2134 y sont toutes, plus le
    /// Pégase Sacré sans enchant).</summary>
    private const int MountTab = 5;

    private static List<CollectionTable.Card>? _extra;
    private static HashSet<int>? _extraIds;
    private static List<CollectionTable.Card> _extraWeapons = new();
    private static HashSet<int> _extraWeaponIds = new();

    /// <summary>Objets de monture (enchant 2134 → rideKey) ABSENTS de
    /// c_collection : le client natif ne peut pas les archiver (ex.
    /// Renard blanc du blizzard légendaire 48813). Listés à part dans
    /// la feuille diagnostic « Hors archive » du groupe Special Items
    /// pour ne pas les perdre de vue ; jamais comptés dans le taux de
    /// collection ni dans les compteurs des feuilles natives.</summary>
    private static List<CollectionTable.Card> ExtraCards()
    {
        if (_extra != null) return _extra;
        var inArchive = CollectionTable.Cards().Select(c => c.Id).ToHashSet();
        _extra = InventoryWindow.AllDefs()
            .Where(kv => !inArchive.Contains(kv.Key) &&
                         EnchantTable.RideKeyOf(kv.Value.UseEnchantId) > 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => new CollectionTable.Card
            {
                Id = kv.Key, Special = true, Tab = MountTab,
                NameBig5 = kv.Value.Name,
            }).ToList();
        _extraIds = _extra.Select(c => c.Id).ToHashSet();
        // Armes (kinds 17-34) de c_item/c_itemmall ABSENTES de c_collection :
        // le natif ne peut pas les archiver (ni le serveur) ; listées à part
        // dans « Armes > Hors archive » pour prouver qu'aucune arme n'est
        // oubliée (diagnostic, jamais comptées dans le taux).
        _extraWeapons = InventoryWindow.AllDefs()
            .Where(kv => !inArchive.Contains(kv.Key) &&
                         kv.Value.Kind is >= 17 and <= 34 &&
                         !_extraIds.Contains(kv.Key))
            .OrderBy(kv => kv.Value.Kind).ThenBy(kv => kv.Key)
            .Select(kv => new CollectionTable.Card
            {
                Id = kv.Key, Special = false, Tab = -1,
                NameBig5 = kv.Value.Name,
            }).ToList();
        _extraWeaponIds = _extraWeapons.Select(c => c.Id).ToHashSet();
        _extra.AddRange(_extraWeapons);
        return _extra;
    }

    private static HashSet<int> ExtraMounts()
    {
        ExtraCards();
        return _extraIds!;
    }

    /// <summary>Cartes de c_collection + objets de monture hors
    /// archive (affichage seulement).</summary>
    private static IEnumerable<CollectionTable.Card> Shown() =>
        CollectionTable.Cards().Concat(ExtraCards());

    private static string NameOf(CollectionTable.Card c) =>
        CollectionTable.DisplayName(c, TextResolver.ItemName(c.Id));

    /// <summary>Clé de catégorie diagnostic des montures hors archive
    /// (feuille ajoutée sous « Special Items », cf.
    /// <see cref="ExtraCards"/>).</summary>
    private const string ExtraLeafKey = "g3|x5";
    private const string ExtraWeaponKey = "g1|x0";

    /// <summary>Clé de catégorie d'une carte — « g{groupe}|t{onglet} »
    /// suivant l'arbre natif (<see cref="CollectionTree"/>) : la col3
    /// de c_collection est la clé de rangement, pas le kind ni le slot
    /// c_item — les libellés d'emplacement ordinaires (Legs, Hands…)
    /// ne s'appliquent pas aux costumes/montures/familiers.</summary>
    private static string CategoryOf(CollectionTable.Card c) =>
        ExtraMounts().Contains(c.Id) ? ExtraLeafKey
        : _extraWeaponIds.Contains(c.Id) ? ExtraWeaponKey
                                     : CollectionTree.KeyOf(c.Tab);

    /// <summary>Sélectionne une famille dans l'arbre des catégories —
    /// exposé pour <c>--hud-open=archive:&lt;fam&gt;</c> (clé EN stable :
    /// « Mounts », « Weapons »… indépendante de la langue d'affichage).
    /// Accepte les clés internes (« g1 », « g1|t6 », « t31 ») et les
    /// anciens noms de famille/feuille.
    /// Différé : le sélecteur doit courir après BuildTree (déclenché par
    /// le passage en visibilité), sinon la sélection « All » écrase _cat.</summary>
    public void SelectCategory(string cat) =>
        CallDeferred(nameof(DeferredSelectCategory), cat);

    /// <summary>Alias historiques/conviviaux → clé « g{g}|t{tab} ».</summary>
    private static readonly Dictionary<string, string> CatAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["Appearance"] = "g0", ["Costumes"] = "g0",
        ["Weapon"] = "g1", ["Weapons"] = "g1",
        ["Armor"] = "g2", ["Accessories"] = "g2|t26",
        ["Special Items"] = "g3", ["Special"] = "g3",
        ["Mounts"] = "g3|t5", ["Pets"] = "g3|t29",
        ["Arcane Box"] = "g3|t30", ["Other Items"] = "g3|t32",
        ["Daggers"] = "g1|t6", ["Rapiers"] = "g1|t7",
        ["1H Swords"] = "g1|t8", ["Maces"] = "g1|t9",
        ["Cestus"] = "g1|t10", ["Clubs"] = "g1|t11",
        ["Katanas"] = "g1|t12", ["Greatswords"] = "g1|t13",
        ["Axes"] = "g1|t14", ["Pikes"] = "g1|t15",
        ["Hammers"] = "g1|t16", ["Bows"] = "g1|t17",
        ["Artillery Guns"] = "g1|t18", ["Guitars"] = "g1|t19",
        ["Grimoires"] = "g1|t20", ["Staff"] = "g1|t21",
        ["Shields"] = "g1|t22", ["Sickleshot"] = "g1|t31",
        ["Capes"] = "g2|t27", ["Trophies"] = "g2|t28",
        ["Not in archive"] = ExtraLeafKey,
        ["Weapons not in archive"] = ExtraWeaponKey,
    };

    private void DeferredSelectCategory(string cat)
    {
        var root = _tree?.GetRoot();
        if (root == null) return;
        var key = cat;
        if (CatAliases.TryGetValue(cat, out var alias)) key = alias;
        else if (cat.StartsWith('t') &&
                 int.TryParse(cat[1..], out var tab) &&
                 CollectionTree.OfTab(tab) is { } l)
            key = CollectionTree.KeyOf(l.Tab);
        if (FindByKey(root.GetFirstChild(), key) is { } it)
        {
            it.Select(0);   // déclenche ItemSelected → _cat + Refresh
            _tree?.ScrollToItem(it);
            return;
        }
        GD.PrintErr($"[hud] archive:{cat} — famille inconnue");
    }

    private static TreeItem? FindByKey(TreeItem? it, string cat)
    {
        for (; it != null; it = it.GetNext())
        {
            if (string.Equals(it.GetMetadata(0).AsString(), cat,
                    StringComparison.OrdinalIgnoreCase)) return it;
            if (FindByKey(it.GetFirstChild(), cat) is { } f) return f;
        }
        return null;
    }

    /// <summary>Libellé d'une feuille : t_textindex natif
    /// (FR — « Coiffe », « Poignards »…), repli nom EN canonique.</summary>
    private static string LeafLabel(CollectionTree.Leaf l) =>
        TextResolver.TextIndex(l.TextIndex) ?? l.EnName;

    private void BuildTree()
    {
        if (_tree == null) return;
        _tree.Clear();
        var counts = Shown().GroupBy(CategoryOf)
            .ToDictionary(g => g.Key, g => g.Count());
        var root = _tree.CreateItem();
        var all = _tree.CreateItem(root);
        all.SetText(0, UiTextEn.Translate("All") + $" ({Shown().Count()})");
        all.SetMetadata(0, "");
        // Arbre natif : 4 groupes × feuilles onglet (CollectionTree) ;
        // le clic groupe filtre toutes ses feuilles par préfixe.
        for (var g = 0; g < CollectionTree.GroupEnNames.Length; g++)
        {
            var leaves = CollectionTree.Leaves.Where(l => l.Group == g);
            var fam = _tree.CreateItem(root);
            var gKey = $"g{g}";
            var gCount = leaves.Sum(l =>
                counts.GetValueOrDefault(CollectionTree.KeyOf(l.Tab), 0));
            var gLabel = TextResolver.TextIndex(
                CollectionTree.GroupTextIndex[g]) ??
                UiTextEn.Translate(CollectionTree.GroupEnNames[g]);
            fam.SetText(0, gLabel + $" ({gCount})");
            fam.SetMetadata(0, gKey);
            foreach (var l in leaves)
            {
                var key = CollectionTree.KeyOf(l.Tab);
                var it = _tree.CreateItem(fam);
                it.SetText(0, LeafLabel(l) +
                    $" ({counts.GetValueOrDefault(key, 0)})");
                it.SetMetadata(0, key);
            }
            // Feuille diagnostic : montures de objets ABSENTES de
            // c_collection (non archivables — FIX-F), rangée sous
            // « Montures » du groupe Special Items.
            if (g == 1 && counts.GetValueOrDefault(ExtraWeaponKey, 0) > 0)
            {
                var it = _tree.CreateItem(fam);
                it.SetText(0, "Hors archive" +
                    $" ({counts[ExtraWeaponKey]})");
                it.SetMetadata(0, ExtraWeaponKey);
            }
            if (g == 3 && counts.GetValueOrDefault(ExtraLeafKey, 0) > 0)
            {
                var it = _tree.CreateItem(fam);
                it.SetText(0, UiTextEn.Translate("Not in archive") +
                    $" ({counts[ExtraLeafKey]})");
                it.SetMetadata(0, ExtraLeafKey);
            }
        }
        var mounts = counts.GetValueOrDefault("g3|t5", 0);
        GD.Print($"[archive] cartes={CollectionTable.Cards().Count} " +
            $"montures={mounts} dont hors-archive={ExtraCards().Count}");
        // --hud-dump : sortie texte des feuilles + comptes — preuve
        // « table vs UI » consultable en headless (le PNG reste la
        // preuve visuelle en session graphique).
        if (OS.GetCmdlineUserArgs().Contains("--hud-dump") ||
            OS.GetCmdlineArgs().Contains("--hud-dump"))
            DumpCategories(counts);
        all.Select(0);
    }

    /// <summary>Imprime chaque feuille « clé → libellé (comptes) » —
    /// les comptes viennent de <see cref="CategoryOf"/> sur
    /// <see cref="Shown"/>, donc de la table, pas de l'affichage.</summary>
    private static void DumpCategories(Dictionary<string, int> counts)
    {
        for (var g = 0; g < CollectionTree.GroupEnNames.Length; g++)
        {
            var leaves = CollectionTree.Leaves.Where(l => l.Group == g);
            var gLabel = TextResolver.TextIndex(
                CollectionTree.GroupTextIndex[g]) ??
                CollectionTree.GroupEnNames[g];
            var gCount = leaves.Sum(l =>
                counts.GetValueOrDefault(CollectionTree.KeyOf(l.Tab), 0));
            GD.Print($"[archive-cat] g{g} {gLabel} ({gCount})");
            foreach (var l in leaves)
                GD.Print($"[archive-cat]   g{g}|t{l.Tab} " +
                    $"{LeafLabel(l)} " +
                    $"({counts.GetValueOrDefault(CollectionTree.KeyOf(l.Tab), 0)})");
        }
        if (counts.GetValueOrDefault(ExtraWeaponKey, 0) > 0)
            GD.Print($"[archive-cat]   {ExtraWeaponKey} Armes hors archive " +
                $"({counts[ExtraWeaponKey]})");
        if (counts.GetValueOrDefault(ExtraLeafKey, 0) > 0)
            GD.Print($"[archive-cat]   {ExtraLeafKey} Not in archive " +
                $"({counts[ExtraLeafKey]})");
    }

    private void Refresh()
    {
        if (_grid == null || _win == null || !GodotObject.IsInstanceValid(_win))
            return;
        var owned = Owned();
        var q = (_search?.Text ?? "").Trim();
        var game = _cbGame?.ButtonPressed ?? true;
        var mall = _cbMall?.ButtonPressed ?? true;
        var onlyOwned = _cbOwned?.ButtonPressed ?? false;
        var all = Shown().Where(c =>
            (_cat.Length == 0 && !CategoryOf(c).Contains("|x") ||
             CategoryOf(c) == _cat ||
             // clic sur un GROUPE : feuilles natives seulement (les
             // feuilles diagnostic « x* » restent hors des comptes).
             CategoryOf(c).StartsWith(_cat + "|") &&
             !CategoryOf(c).Contains("|x")) &&
            ((c.Special && mall) || (!c.Special && game)) &&
            (!onlyOwned || owned.Contains(c.Id)));
        if (q.Length > 0)
            all = all.Where(c => NameOf(c).Contains(q,
                StringComparison.OrdinalIgnoreCase));
        var list = all.ToList();
        if (_cbRev?.ButtonPressed == true) list.Reverse();
        if (OS.GetCmdlineUserArgs().Contains("--hud-dump") ||
            OS.GetCmdlineArgs().Contains("--hud-dump"))
            GD.Print($"[archive-view] cat='{_cat}' affichées={list.Count} " +
                $"premiers={string.Join(',', list.Take(8).Select(c => c.Id))}");
        var total = CollectionTable.Cards().Count;
        if (_rate != null)
            _rate.Text = UiTextEn.Translate("Collection rate: ") +
                (total == 0 ? "0.0" : (owned.Count * 100.0 / total).ToString("0.0")) +
                $"%  ({owned.Count}/{total})";

        foreach (var ch in _grid.GetChildren()) ch.QueueFree();
        // ~8000 cartes FR : cap haut pour que la vue « All » montre
        // aussi les armes/armures après les costumes (l'ancien cap
        // 1200 coupait la grille au milieu de l'onglet dos-costumes —
        // « très peu d'armes » venait de là autant que des filtres).
        foreach (var c in list.Take(8192))
        {
            var has = owned.Contains(c.Id);
            var cell = new Panel
            {
                CustomMinimumSize = new Vector2(Cell, Cell),
                MouseFilter = MouseFilterEnum.Pass,
            };
            cell.AddThemeStyleboxOverride("panel", EeSkin.GreyPanel(90));
            var def = InventoryWindow.LookupDef(c.Id);
            var icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                Modulate = has ? Colors.White : new Color(1, 1, 1, 0.28f),
            };
            if (def is { Code.Length: > 0 })
                icon.Texture = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            cell.AddChild(icon);
            var id = c.Id;
            ItemTooltip.BindCustom(cell, () => ItemTooltip.BuildForId(id));
            cell.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true } mb) return;
                // CTRL+clic gauche = panneau de simulation d'utilisation
                // (même comportement que l'inventaire), sans sélectionner.
                if (mb.ButtonIndex == MouseButton.Left && mb.CtrlPressed)
                    ItemPreviewWindow.Preview(id);
                else if (mb.ButtonIndex == MouseButton.Left) { _selected = id; RefreshFooter(); }
                else if (mb.ButtonIndex == MouseButton.Right)
                {
                    if (Owned().Contains(id))
                        ItemPreviewWindow.ShowMenu(cell, id,
                            UiTextEn.Translate("Take out"), () => OnWithdraw(id));
                    else ItemPreviewWindow.Preview(id);
                }
            };
            _grid.AddChild(cell);
        }
        RefreshFooter();
    }

    private void RefreshFooter()
    {
        if (_points == null || _report == null) return;
        var owned = Owned();
        _points.Text = UiTextEn.Translate("Archive points: ") +
            PlayerProfileState.CollectionPoints;
        var sb = new System.Text.StringBuilder();
        if (_selected != 0 &&
            Shown().FirstOrDefault(c => c.Id == _selected) is { } card)
        {
            var has = owned.Contains(card.Id);
            sb.Append("[b]").Append(NameOf(card)).Append("[/b]  ")
              .Append(card.Points).Append(' ')
              .Append(UiTextEn.Translate("points")).Append("  ")
              .Append(has ? "[color=#8fe080]" + UiTextEn.Translate("Archived") + "[/color]"
                          : "[color=#e08080]" + UiTextEn.Translate("Not archived") + "[/color]");
            var src = ItemSources.Of(card.Id);
            sb.Append("\n[b]").Append(UiTextEn.Translate("How to obtain")).Append(":[/b] ")
              .Append(src.Count == 0 ? "—" : string.Join(" ; ", src.Take(6)));
            sb.Append("\n");
        }
        var gp = CollectionTable.GroupPoints(owned);
        CollectionTable.Award? best = null;
        double bestRatio = -1;
        foreach (var a in CollectionTable.Awards())
        {
            if (a.Enchant == 0 || a.Conds.Count == 0) continue;
            var ok = a.Conds.All(c => gp.GetValueOrDefault(c.Key) >= c.Threshold);
            if (ok) continue;
            var ratio = a.Conds.Average(c => Math.Min(1.0,
                gp.GetValueOrDefault(c.Key) / (double)Math.Max(1, c.Threshold)));
            if (ratio > bestRatio) { bestRatio = ratio; best = a; }
        }
        if (best == null)
            sb.Append(UiTextEn.Translate("All archive rewards reached."));
        else
        {
            var prog = string.Join(", ", best.Conds.Select(c =>
                $"{gp.GetValueOrDefault(c.Key)}/{c.Threshold}"));
            var desc = EnchantTable.DescriptionOf(best.Enchant) ?? "";
            sb.Append("[b]").Append(UiTextEn.Translate("Next reward: ")).Append("[/b]")
              .Append(EnchantTable.NameOf(best.Enchant)).Append(" (").Append(prog).Append(')');
            if (desc.Length > 0) sb.Append('\n').Append(desc);
        }
        _report.Text = sb.ToString();
    }

    /// <summary>Cible de drop : item du sac glissé (payload {bag, slot,
    /// src:"inv"}) → dépôt.</summary>
    private sealed partial class DropArea : PanelContainer
    {
        private readonly Action<int, int> _onDrop;
        public DropArea(Action<int, int> onDrop) => _onDrop = onDrop;

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            return d.ContainsKey("bag") && d.ContainsKey("slot") &&
                d.ContainsKey("itemId") &&
                CollectionTable.Cards().Any(c => c.Id == d["itemId"].AsInt32());
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            _onDrop(d["bag"].AsInt32(), d["slot"].AsInt32());
        }
    }
}
