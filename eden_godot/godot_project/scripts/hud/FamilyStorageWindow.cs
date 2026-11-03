using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Coffre de famille (vague 2) — layout natif
/// <c>FamilyStorageWnd.layout</c> (FrameWindow « Root/FamilyStorage »
/// + TabControl « Tab », pages 40 slots des FamilyStorageTab1/2/3)
/// ou repli en code.
///
/// Ouverture : <see cref="FamilyStorageState.Changed"/> — service NPC
/// type 19 « family_storage » → 507
/// <see cref="ZoneEnterFamilyStorage"/> puis un 508 par coffre non
/// vide (natif <c>CFamilyStorageShop::OpenShop</c>). Trois coffres
/// natifs (indices 0-2, 40 locs chacun), verrouillage par privilège
/// famille côté serveur (priv index+11 → lv ~3/5/6) — les 3 onglets
/// sont toujours affichés, le serveur refuse silencieusement un dépôt
/// non autorisé.
///
/// Interactions :
///   - <b>déposer</b> : drag d'un item du panneau sac vers une
///     cellule du coffre, ou double-clic sac → NCID 177
///     <see cref="ZoneFamilyStoragePut"/> (ligne entière — pas de
///     quantité) ; loc −1 = premier libre ;
///   - <b>retirer</b> : double-clic item coffre ou drag vers le
///     panneau sac → NCID 178 <see cref="ZoneFamilyStorageGet"/>
///     (bag/slot −1 = auto) ;
///   - <b>journal</b> : bouton « Journal » → NCID 179
///     <see cref="ZoneFamilyStorageQueryLog"/> → 506 affiché en bas.
///
/// Pas d'or dans le coffre famille : la trésorerie est le flux
/// guilde 159 (<see cref="GuildWindow"/>) — pas de dépôt d'or ici.
/// </summary>
public partial class FamilyStorageWindow : Control
{
    private const int Columns = 10;

    private Control? _window;
    private TabContainer? _tabs;
    private GridContainer? _bagGrid;
    private ScrollContainer? _logScroll;
    private VBoxContainer? _logList;
    private Button? _logBtn;
    private bool _demo;

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("FamilyStorageWnd.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            // Fenêtre native ~394×262 — élargie pour caser le panneau
            // sac à droite des 3 coffres (le contenu natif reste).
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -290, -200, 290, 210);
            // Le « Tab » CEGUI du layout est vide (les pages natives
            // sont des fichiers séparés) — on le remplace par notre
            // TabContainer de cellules.
            var nativeTab = TopLeftVitals.Find<Control>(_window, "Tab");
            if (nativeTab != null) nativeTab.Visible = false;
            var close = TopLeftVitals.Find<BaseButton>(_window,
                "closeBtn");
            if (close != null) close.Pressed += Close;
        }
        BuildContent();
        DragHelper.EnsureTitleBar(_window ?? this, "Family Storage");
        DragHelper.Persist(_window ?? this, "familystorage");
        Visible = false;
        FamilyStorageState.Changed += OnStorageChanged;
        PlayerBag.Changed += RefreshBag;
    }

    public override void _ExitTree()
    {
        FamilyStorageState.Changed -= OnStorageChanged;
        PlayerBag.Changed -= RefreshBag;
        base._ExitTree();
    }

    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        var split = new HBoxContainer { Name = "__split" };
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        split.OffsetTop = 30;
        split.OffsetLeft = 10;
        split.OffsetRight = -10;
        split.OffsetBottom = -78;
        split.MouseFilter = MouseFilterEnum.Stop;
        host.AddChild(split);

        _tabs = new TabContainer { Name = "__chesttabs" };
        _tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        split.AddChild(_tabs);

        var right = new VBoxContainer { Name = "__bagpane" };
        right.CustomMinimumSize = new Vector2(150, 0);
        split.AddChild(right);
        right.AddChild(new Label { Text = UiTextEn.Translate("Inventory") });
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(scroll);
        _bagGrid = new GridContainer { Name = "__bag", Columns = 3 };
        _bagGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _bagGrid.AddThemeConstantOverride("h_separation", 2);
        _bagGrid.AddThemeConstantOverride("v_separation", 2);
        scroll.AddChild(_bagGrid);
        var hint = new Label { Text = UiTextEn.Translate("Drag → chest = deposit") };
        hint.AddThemeFontSizeOverride("font_size", 10);
        right.AddChild(hint);

        // Barre basse : journal d'audit (179 → 506) + compte d'items.
        _logBtn = new Button { Text = UiTextEn.Translate("Log") };
        _logBtn.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        _logBtn.Position = new Vector2(10, -38);
        _logBtn.Size = new Vector2(100, 30);
        _logBtn.TooltipText = UiTextEn.Translate("Vault log");
        _logBtn.Pressed += ToggleLog;
        host.AddChild(_logBtn);

        _logScroll = new ScrollContainer { Name = "__log" };
        _logScroll.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _logScroll.OffsetTop = -74;
        _logScroll.OffsetLeft = 10;
        _logScroll.OffsetRight = -10;
        _logScroll.OffsetBottom = -44;
        _logScroll.Visible = false;
        _logList = new VBoxContainer { Name = "__loglist" };
        _logList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _logScroll.AddChild(_logList);
        host.AddChild(_logScroll);
    }

    /// <summary>507/508/504/505/506 → montre et remplit les coffres.</summary>
    private void OnStorageChanged()
    {
        if (!FamilyStorageState.IsOpen)
        {
            if (!_demo) Visible = false;
            return;
        }
        _demo = false;
        RefreshChests();
        RefreshBag();
        RefreshLog();
        Visible = true;
    }

    // ------------------------------------------------------------------
    // Coffres (3 × 40 locs)
    // ------------------------------------------------------------------

    private void RefreshChests()
    {
        if (_tabs == null) return;
        var cur = _tabs.CurrentTab;
        foreach (var c in _tabs.GetChildren()) c.QueueFree();
        for (var i = 0; i < FamilyStorageState.StorageCount; i++)
            _tabs.AddChild(BuildChestGrid(i,
                FamilyStorageState.Chest(i)));
        if (cur >= 0 && cur < FamilyStorageState.StorageCount)
            _tabs.CurrentTab = cur;
    }

    /// <summary>Grille de 40 cellules du coffre
    /// <paramref name="index"/> — items posés par loc.</summary>
    private Control BuildChestGrid(int index,
        IReadOnlyDictionary<int, InventorySlot>? items)
    {
        var scroll = new ScrollContainer
        {
            Name = $"Chest {index + 1}",
        };
        var grid = new GridContainer { Columns = Columns };
        grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        scroll.AddChild(grid);
        var slotTex = HudExtensions.FindHud()
            ?.Region("main", "FunctionSlot");
        for (var loc = 0; loc < FamilyStorageState.StorageCapacity; loc++)
        {
            var cell = new StorageItemCell("fchest", index, loc, slotTex);
            cell.SetItem(items?.GetValueOrDefault(loc));
            cell.DoubleClicked += OnChestCellDoubleClick;
            cell.ItemDropped += OnChestCellDrop;
            grid.AddChild(cell);
        }
        return scroll;
    }

    /// <summary>Double-clic item coffre → retrait (178 — bag/slot
    /// −1 = auto : sac 0, premier libre).</summary>
    private void OnChestCellDoubleClick(StorageItemCell cell)
    {
        if (cell.Item == null || !FamilyStorageState.IsOpen) return;
        Send(new ZoneFamilyStorageGet
        {
            StorageIndex = (short)cell.ContIndex,
            Loc = (short)cell.Loc,
            Bag = -1, Slot = -1,
        });
    }

    /// <summary>Drop sur une cellule coffre → dépôt à la loc visée
    /// (177). Un item « fchest » (même coffre) n'a pas de move —
    /// ignoré ; « bank » n'a pas de bridge non plus.</summary>
    private void OnChestCellDrop(StorageItemCell cell,
        Godot.Collections.Dictionary d)
    {
        if (!FamilyStorageState.IsOpen) return;
        var kind = d.ContainsKey("kind") ? d["kind"].AsString() : "bag";
        if (kind != "bag") return;
        var bag = d.ContainsKey("bag") ? d["bag"].AsInt32()
            : d.ContainsKey("cont") ? d["cont"].AsInt32() : 0;
        Send(new ZoneFamilyStoragePut
        {
            StorageIndex = (short)cell.ContIndex,
            Loc = (short)cell.Loc,
            Bag = (short)bag,
            Slot = (short)d["slot"].AsInt32(),
        });
        GD.Print($"[fstorage] dépôt (sac{bag},slot{d["slot"]}) → " +
            $"coffre {cell.ContIndex} loc {cell.Loc} (177)");
    }

    /// <summary>Drop d'un item coffre sur le panneau sac → retrait
    /// (178 auto). Les drops « bag » sur le sac sont ignorés.</summary>
    private void OnBagCellDrop(StorageItemCell cell,
        Godot.Collections.Dictionary d)
    {
        if (!FamilyStorageState.IsOpen) return;
        var kind = d.ContainsKey("kind") ? d["kind"].AsString() : "bag";
        if (kind != "fchest") return;
        Send(new ZoneFamilyStorageGet
        {
            StorageIndex = (short)d["cont"].AsInt32(),
            Loc = (short)d["slot"].AsInt32(),
            Bag = -1, Slot = -1,
        });
        GD.Print($"[fstorage] retrait coffre {d["cont"]} " +
            $"loc {d["slot"]} (178)");
    }

    /// <summary>Double-clic item sac → dépôt premier libre (177 —
    /// loc −1) dans l'onglet courant.</summary>
    private void OnBagCellDoubleClick(StorageItemCell cell)
    {
        if (cell.Item is not { } it || !FamilyStorageState.IsOpen)
            return;
        var index = _tabs?.CurrentTab ?? 0;
        Send(new ZoneFamilyStoragePut
        {
            StorageIndex = (short)index,
            Loc = -1,
            Bag = (short)it.Bag,
            Slot = (short)it.Slot,
        });
    }

    // ------------------------------------------------------------------
    // Sac (panneau droit) + journal
    // ------------------------------------------------------------------

    private void RefreshBag()
    {
        if (_bagGrid == null) return;
        foreach (var c in _bagGrid.GetChildren()) c.QueueFree();
        var slotTex = HudExtensions.FindHud()
            ?.Region("main", "FunctionSlot");
        foreach (var it in PlayerBag.Items)
        {
            var cell = new StorageItemCell("bag", it.Bag, it.Slot,
                slotTex);
            cell.SetItem(it);
            cell.DoubleClicked += OnBagCellDoubleClick;
            cell.ItemDropped += OnBagCellDrop;
            _bagGrid.AddChild(cell);
        }
        // Cibles de drop « retrait vers le sac » (auto −1/−1).
        for (var i = 0; i < 6; i++)
        {
            var cell = new StorageItemCell("bag", 0, -1, slotTex);
            cell.SetItem(null);
            cell.ItemDropped += OnBagCellDrop;
            _bagGrid.AddChild(cell);
        }
        if (PlayerBag.Items.Count == 0)
            _bagGrid.AddChild(new Label { Text = UiTextEn.Translate("(empty bag)") });
    }

    /// <summary>Bouton Journal : affiche/masque la liste ; à la
    /// première ouverture on demande le journal complet (179).</summary>
    private void ToggleLog()
    {
        if (_logScroll == null) return;
        _logScroll.Visible = !_logScroll.Visible;
        if (_logScroll.Visible)
        {
            RefreshLog();
            if (FamilyStorageState.IsOpen)
                Send(new ZoneFamilyStorageQueryLog());
        }
    }

    private void RefreshLog()
    {
        if (_logList == null) return;
        foreach (var c in _logList.GetChildren()) c.QueueFree();
        foreach (var e in FamilyStorageState.Log)
        {
            var l = new Label
            {
                Text = $"[{(e.Action == 0 ? "+" : "-")}] {e.Detail}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            l.AddThemeFontSizeOverride("font_size", 10);
            _logList.AddChild(l);
        }
        if (FamilyStorageState.Log.Count == 0)
            _logList.AddChild(new Label { Text = UiTextEn.Translate("(log empty)") });
    }

    /// <summary>Données de démo pour <c>--hud-open=familystorage</c>
    /// hors session — quelques items factices répartis sur les
    /// coffres.</summary>
    public void EnsureDemo()
    {
        if (FamilyStorageState.IsOpen) { Visible = true; return; }
        _demo = true;
        if (_tabs != null)
        {
            foreach (var c in _tabs.GetChildren()) c.QueueFree();
            var demo = new Dictionary<int, InventorySlot>
            {
                [0] = new(0, 0, 30240, 1, 1, 0, 0, 0),
                [5] = new(0, 5, 31899, 75, 75, 0, 0, 0),
                [12] = new(0, 12, 11205, 30, 1, 0, 0, 0),
            };
            _tabs.AddChild(BuildChestGrid(0, demo));
            _tabs.AddChild(BuildChestGrid(1,
                new Dictionary<int, InventorySlot>()));
            _tabs.AddChild(BuildChestGrid(2,
                new Dictionary<int, InventorySlot>()));
        }
        RefreshBag();
        Visible = true;
    }

    private void Close()
    {
        _demo = false;
        FamilyStorageState.Close();
        Visible = false;
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[fstorage] pas de session — {msg.Name}");
            return;
        }
        var task = s.SendZoneAsync(msg, CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[fstorage] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void BuildFallback()
    {
        var p = new Panel { Name = "FamilyStorage" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-290, -200);
        p.Size = new Vector2(580, 410);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Family Storage");
        _window = p;
    }
}
