using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre banque (lot S7 + vague 2) — layout natif
/// <c>StorageUI.layout</c> (FrameWindow + conteneurs d'onglets
/// bag0..bag4) ou repli en code.
///
/// Ouverture : <see cref="StorageState.Changed"/> (357 EnterStorage
/// via NPC → le client demande 62 GetStorageData puis 358 remplit).
/// Contenu :
///   - un <b>onglet par conteneur</b> (<see cref="StorageState.Containers"/>)
///     avec <b>grille de slots</b> (capacité du 358, icône itemicon,
///     quantité stack, tooltip <see cref="ItemTooltip"/>) — les locs
///     banque sont GLOBALES (le serveur swap/dépose par loc ;
///     l'onglet n'est qu'un groupage container_index) ;
///   - <b>drag-drop bidirectionnel</b> (flux natif NCID 7
///     SwitchItemLocation type 4 « STO ») : sac → cellule banque =
///     dépôt (stack entier, auto-merge serveur), cellule banque →
///     panneau sac = retrait, cellule → cellule = swap de locs ;
///   - <b>double-clic</b> : retrait/dépôt via les commandes
///     historiques 61 <see cref="ZoneStorageWithdraw"/> /
///     60 <see cref="ZoneStorageDeposit"/> (chemin prouvé conservé) ;
///   - <b>extension</b> : bouton « Étendre » → NCID 69
///     <see cref="ZoneStorageExpand"/> (réponse 373 — bindée dans
///     <c>GameScene</c>, +20 slots, max 2, prix S_Parameter) ;
///   - or affiché : <see cref="PlayerVitals.Gold"/> (la banque ne
///     stocke pas d'or propre — aucun NCID or n'existe pour le
///     stockage : prouvé à l'absence dans les handlers serveur).
///
/// Le serveur renvoie un 358 complet après chaque dépôt/retrait/swap
/// → la fenêtre se rafraîchit via <see cref="StorageState.Changed"/>.
/// </summary>
public partial class BankWindow : Control
{
    private const int Columns = 10;

    private Control? _window;
    private TabContainer? _tabs;
    private GridContainer? _bagGrid;
    private Label? _gold;
    private bool _demo;

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("StorageUI.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -300, -230, 300, 230);
            var close = TopLeftVitals.Find<BaseButton>(_window,
                "closeBtn");
            if (close != null) close.Pressed += Close;
            var rent = TopLeftVitals.Find<BaseButton>(_window,
                "rentBtn");
            if (rent != null) rent.Visible = false;
        }
        BuildContent();
        DragHelper.EnsureTitleBar(_window ?? this, "Bank");
        DragHelper.Persist(_window ?? this, "bank");
        Visible = false;
        StorageState.Changed += OnStorageChanged;
        PlayerVitals.GoldChanged += RefreshGold;
        PlayerBag.Changed += OnBagChanged;
    }

    public override void _ExitTree()
    {
        StorageState.Changed -= OnStorageChanged;
        PlayerVitals.GoldChanged -= RefreshGold;
        PlayerBag.Changed -= OnBagChanged;
        base._ExitTree();
    }

    /// <summary>Sac muté : le panneau n'est reconstruit que si la
    /// fenêtre est visible — à l'ouverture,
    /// <see cref="OnStorageChanged"/>/<see cref="EnsureDemo"/>
    /// appellent <see cref="RefreshBag"/> qui relit
    /// <see cref="PlayerBag.Items"/>.</summary>
    private void OnBagChanged()
    {
        if (IsVisibleInTree()) RefreshBag();
    }

    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        // Colonne banque (onglets conteneurs) à gauche, sac à droite.
        var split = new HBoxContainer { Name = "__split" };
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        split.OffsetTop = 30;
        split.OffsetLeft = 10;
        split.OffsetRight = -10;
        split.OffsetBottom = -46;
        split.MouseFilter = MouseFilterEnum.Stop;
        host.AddChild(split);

        _tabs = new TabContainer { Name = "__banktabs" };
        _tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        split.AddChild(_tabs);

        var right = new VBoxContainer { Name = "__bagpane" };
        right.CustomMinimumSize = new Vector2(180, 0);
        split.AddChild(right);
        right.AddChild(new Label { Text = UiTextEn.Translate("Inventory") });
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(scroll);
        _bagGrid = new GridContainer { Name = "__bag", Columns = 4 };
        _bagGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _bagGrid.AddThemeConstantOverride("h_separation", 2);
        _bagGrid.AddThemeConstantOverride("v_separation", 2);
        scroll.AddChild(_bagGrid);
        var hint = new Label { Text = UiTextEn.Translate("Drag → bank = deposit") };
        hint.AddThemeFontSizeOverride("font_size", 10);
        right.AddChild(hint);

        // Barre basse : expansion (+20 slots, max 2 — NCID 69/373)
        // + or du joueur (contexte — pas d'or propre à la banque).
        var exp = new Button { Text = UiTextEn.Translate("Expand +20") };
        exp.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        exp.Position = new Vector2(10, -38);
        exp.Size = new Vector2(120, 30);
        exp.TooltipText = "Agrandir le coffre (+20 slots, max 2) — " +
            "débité de l'or du joueur (NCID 69)";
        exp.Pressed += DoExpand;
        host.AddChild(exp);

        // Bascule vers la vue étendue « neobank » (Reforged
        // NeoInventoryBank — même protocole, UI colonnes/pages/
        // recherche/verrous). Persisté dans neo_bank.cfg.
        var neo = new Button { Text = UiTextEn.Translate("Neo+") };
        neo.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        neo.Position = new Vector2(140, -38);
        neo.Size = new Vector2(60, 30);
        neo.TooltipText = UiTextEn.Translate("Extended bank view (Reforged neobank) — ") +
            UiTextEn.Translate("same protocol, grid/search/locks UI");
        neo.Pressed += () =>
        {
            NeobankSettings.Enabled = true;
            NeobankSettings.Save();
            StorageState.NotifyChanged();
        };
        host.AddChild(neo);

        _gold = new Label { Name = "__gold" };
        _gold.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        _gold.Position = new Vector2(-220, -34);
        _gold.Size = new Vector2(210, 24);
        _gold.HorizontalAlignment = HorizontalAlignment.Right;
        host.AddChild(_gold);
        RefreshGold();
    }

    /// <summary>357/358 reçus → montre et remplit les onglets — sauf
    /// si la vue étendue neobank est active
    /// (<see cref="NeobankSettings.Enabled"/>, bouton « Neo »
    /// ci-dessous) : <see cref="NeobankWindow"/> écoute le même
    /// événement et prend le relais sur le même protocole.</summary>
    private void OnStorageChanged()
    {
        if (!StorageState.IsOpen)
        {
            if (!_demo) Visible = false;
            return;
        }
        if (NeobankSettings.Enabled)
        {
            _demo = false;
            Visible = false; // la vue néo affiche le même storage
            return;
        }
        _demo = false;
        RefreshBank();
        RefreshBag();
        RefreshGold();
        Visible = true;
    }

    // ------------------------------------------------------------------
    // Banque (onglets = conteneurs du 358 — grille de locs)
    // ------------------------------------------------------------------

    private void RefreshBank()
    {
        if (_tabs == null) return;
        foreach (var c in _tabs.GetChildren()) c.QueueFree();
        var conts = StorageState.Containers;
        for (var ci = 0; ci < conts.Count; ci++)
            _tabs.AddChild(BuildTabGrid(conts[ci], ci,
                $"Storage {ci + 1}"));
        if (conts.Count == 0)
        {
            var p = new Control { Name = "Tab0" };
            p.AddChild(new Label { Text = UiTextEn.Translate("Bank empty") });
            _tabs.AddChild(p);
        }
    }

    /// <summary>Grille de <paramref name="cont"/>.Capacity cellules
    /// d'un onglet — chaque cellule indexe sa loc globale ; items
    /// posés par <c>InventorySlot.Slot</c> (= loc).</summary>
    private Control BuildTabGrid(StorageContainer cont, int contIdx,
        string title)
    {
        var scroll = new ScrollContainer { Name = title };
        var grid = new GridContainer { Columns = Columns };
        grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        scroll.AddChild(grid);
        var items = new Dictionary<int, InventorySlot>();
        foreach (var it in cont.Items)
            items[it.Slot] = it;
        var slotTex = HudExtensions.FindHud()
            ?.Region("main", "FunctionSlot");
        var cap = Math.Max((int)cont.Capacity, 1);
        for (var loc = 0; loc < cap; loc++)
        {
            var cell = new StorageItemCell("bank", contIdx, loc, slotTex);
            cell.SetItem(items.GetValueOrDefault(loc));
            cell.DoubleClicked += OnBankCellDoubleClick;
            cell.ItemDropped += OnBankCellDrop;
            grid.AddChild(cell);
        }
        return scroll;
    }

    /// <summary>Double-clic cellule banque → retrait direct (61 —
    /// chemin prouvé conservé ; le drag utilise NCID 7).</summary>
    private void OnBankCellDoubleClick(StorageItemCell cell)
    {
        if (cell.Item is not { } it || !StorageState.IsOpen) return;
        var count = StorageItemCell.StackCount(it,
            ItemTableCache.Get(it.ItemId));
        Send(new ZoneStorageWithdraw
        {
            Field1 = StorageState.NpcUid,
            Field2 = (ushort)cell.ContIndex,
            Field3 = (short)it.Slot,
            Field4 = (short)count,
        });
    }

    /// <summary>Drop sur une cellule banque :
    ///   - payload "bag" → NCID 7 (0,slot,sac)→(4,−1,−1) dépôt stack ;
    ///   - payload "bank" autre loc → NCID 7 (4,src,srcCont)→
    ///     (4,dst,dstCont) swap de locs.</summary>
    private void OnBankCellDrop(StorageItemCell cell,
        Godot.Collections.Dictionary d)
    {
        if (!StorageState.IsOpen) return;
        var kind = d.ContainsKey("kind") ? d["kind"].AsString() : "bag";
        var slot = d["slot"].AsInt32();
        if (kind == "bag")
        {
            var bag = d.ContainsKey("bag") ? d["bag"].AsInt32()
                : d.ContainsKey("cont") ? d["cont"].AsInt32() : 0;
            Send(ZoneBankActions.DepositRequest(bag, slot));
            GD.Print($"[bank] drag dépôt (sac{bag},slot{slot}) → 7");
        }
        else if (kind == "bank")
        {
            var srcCont = d["cont"].AsInt32();
            if (srcCont == cell.ContIndex && slot == cell.Loc) return;
            Send(ZoneBankActions.MoveRequest(slot, srcCont,
                cell.Loc, cell.ContIndex));
            GD.Print($"[bank] swap loc {slot}→{cell.Loc} → 7");
        }
        // kind="fchest" : pas de bridge banque↔famille natif — ignoré.
    }

    /// <summary>Drop sur une cellule du panneau sac → retrait banque
    /// (4→0) ; les drops "bag" sur le sac sont ignorés (le tri
    /// intra-sac reste à la fenêtre inventaire).</summary>
    private void OnBagCellDrop(StorageItemCell cell,
        Godot.Collections.Dictionary d)
    {
        if (!StorageState.IsOpen) return;
        var kind = d.ContainsKey("kind") ? d["kind"].AsString() : "bag";
        if (kind != "bank") return;
        Send(ZoneBankActions.WithdrawRequest(d["slot"].AsInt32(),
            d["cont"].AsInt32()));
        GD.Print($"[bank] drag retrait loc {d["slot"]} → 7");
    }

    /// <summary>Double-clic item sac → dépôt direct (60 —
    /// <c>[i32 npc][u16 sac][s16 slot][s16 count]</c> — le serveur
    /// borne au stack réel).</summary>
    private void OnBagCellDoubleClick(StorageItemCell cell)
    {
        if (cell.Item is not { } it || !StorageState.IsOpen) return;
        var count = StorageItemCell.StackCount(it,
            ItemTableCache.Get(it.ItemId));
        Send(new ZoneStorageDeposit
        {
            Field1 = StorageState.NpcUid,
            Field2 = (ushort)it.Bag,
            Field3 = (short)it.Slot,
            Field4 = (short)count,
        });
    }

    // ------------------------------------------------------------------
    // Sac (panneau droit — sources de dépôt + cible de retrait)
    // ------------------------------------------------------------------

    private void RefreshBag()
    {
        if (_bagGrid == null) return;
        foreach (var c in _bagGrid.GetChildren()) c.QueueFree();
        var slotTex = HudExtensions.FindHud()
            ?.Region("main", "FunctionSlot");
        foreach (var it in PlayerBag.Items)
            AddBagCell(it, slotTex);
        // Cellules vides : cible de drop « retrait vers le sac »
        // (le serveur résout le premier slot libre — loc non
        // significative, on envoie −1 au handler via le payload).
        for (var i = 0; i < 8; i++)
        {
            var cell = new StorageItemCell("bag", 0, -1, slotTex);
            cell.SetItem(null);
            cell.ItemDropped += OnBagCellDrop;
            _bagGrid.AddChild(cell);
        }
        if (PlayerBag.Items.Count == 0)
            _bagGrid.AddChild(new Label { Text = UiTextEn.Translate("(empty bag)") });
    }

    private void AddBagCell(InventorySlot it, Texture2D? slotTex)
    {
        var cell = new StorageItemCell("bag", it.Bag, it.Slot, slotTex);
        cell.SetItem(it);
        cell.DoubleClicked += OnBagCellDoubleClick;
        cell.ItemDropped += OnBagCellDrop;
        _bagGrid!.AddChild(cell);
    }

    // ------------------------------------------------------------------
    // Extension / or / divers
    // ------------------------------------------------------------------

    /// <summary>NCID 69 StorageExpand (corps vide) — le serveur
    /// débite l'or et répond 373 (0 ok / 1 cap / 2 or) + texte ;
    /// <c>GameScene</c> re-demande 62 au succès pour rafraîchir la
    /// capacité affichée.</summary>
    private void DoExpand()
    {
        if (!StorageState.IsOpen) return;
        Send(new ZoneStorageExpand());
        GD.Print("[bank] expansion demandée (69)");
    }

    private void RefreshGold()
    {
        if (_gold != null)
            _gold.Text = UiTextEn.Translate($"Gold : {PlayerVitals.Gold:N0}");
    }

    /// <summary>Données de démo pour <c>--hud-open=bank</c> hors
    /// session — onglets factices remplis de quelques items connus
    /// (la grille reste interactive mais les envois sont sans
    /// session → warning logué).</summary>
    public void EnsureDemo()
    {
        if (StorageState.IsOpen) { Visible = true; return; }
        _demo = true;
        if (_tabs != null)
        {
            foreach (var c in _tabs.GetChildren()) c.QueueFree();
            var items = new List<InventorySlot>
            {
                new(0, 0, 30240, 1, 1, 0, 0, 0),
                new(0, 1, 31899, 75, 75, 0, 0, 0),
                new(0, 7, 11205, 12, 1, 0, 0, 0),
            };
            _tabs.AddChild(BuildTabGrid(
                new StorageContainer(40, 0, items), 0, "Storage 1"));
            _tabs.AddChild(BuildTabGrid(
                new StorageContainer(40, 0, new List<InventorySlot>()),
                1, "Storage 2"));
        }
        RefreshBag();
        RefreshGold();
        Visible = true;
    }

    private void Close()
    {
        _demo = false;
        StorageState.Close();
        Visible = false;
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[bank] pas de session — {msg.Name}");
            return;
        }
        var task = s.SendZoneAsync(msg, CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[bank] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void BuildFallback()
    {
        var p = new Panel { Name = "StorageUI" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-300, -230);
        p.Size = new Vector2(600, 460);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Bank");
        _window = p;
    }
}
