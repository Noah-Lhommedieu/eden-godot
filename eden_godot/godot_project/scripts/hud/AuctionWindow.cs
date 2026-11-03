using Godot;
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

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("AuctionWnd.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -350, -240, 350, 240);
            var close = TopLeftVitals.Find<BaseButton>(_window,
                "closeBtn");
            if (close != null) close.Pressed += Close;
        }
        BuildContent();
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
            _status.Text = UiTextEn.Translate($"{entries.Count} listing(s) on sale — ") +
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
            Text = UiTextEn.Translate($"  {name}  —  {e.Price:N0} or   ") +
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

    private static string ResolveName(AuctionEntry e) =>
        e.ItemName.Length > 0 ? e.ItemName
        : TextResolver.ItemName(e.ItemId)
        ?? ItemTableCache.Get(e.ItemId)?.Name ?? $"#{e.ItemId}";

    private static string TimeLeft(long expireUnix)
    {
        if (expireUnix <= 0) return "";
        var left = TimeSpan.FromSeconds(expireUnix -
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (left.TotalSeconds <= 0) return "expired";
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
                _buyBtn.Text = UiTextEn.Translate($"Buy {ResolveName(e)} — ") +
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
        var it = _sellSlot?.Item;
        if (it == null || _priceEdit == null)
        {
            if (_sellStatus != null)
                _sellStatus.Text = UiTextEn.Translate("Drop an item from your bag.");
            return;
        }
        var price = ParseInt(_priceEdit.Text);
        if (price < 1)
        {
            if (_sellStatus != null)
                _sellStatus.Text = UiTextEn.Translate("Invalid price.");
            return;
        }
        Send(new ZoneAuctionSell
        {
            Bag = (short)it.Bag,
            Slot = (short)it.Slot,
            Price = price,
            Buyout = ParseInt(_buyoutEdit?.Text),
        });
        if (_sellStatus != null)
            _sellStatus.Text = "Listing… (response 429)";
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
        if (s?.Zone?.IsConnected == true) return;
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
            _w._sellSlot?.SetItem(it);
            if (_w._priceEdit != null) _w._priceEdit.Text = "777";
            if (_w._buyoutEdit != null) _w._buyoutEdit.Text = "888";
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
