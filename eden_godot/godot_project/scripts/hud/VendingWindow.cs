using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtres d'étal joueur (Vague 2 — player vending / market booth)
/// — distinctes de la boutique PNJ <see cref="ShopWindow"/>.
///
/// Deux modes exclusifs montés sur le même composant :
///
///   - <b>Achat</b> (<c>StallBuy.layout</c>, panneau interne
///     « StallBuyPanel ») : vue de l'étal d'un autre joueur — titre,
///     liste des marchandises (icône, nom, prix du lot, quantité),
///     bouton Acheter → C→S <b>76</b> VendorBuy (lot entier : qty =
///     quantité du lot — le serveur rejette tout achat partiel),
///     bouton Fermer → C→S <b>75</b> VendorCloseView.
///   - <b>Vente</b> (<c>StallUI.layout</c>, panneau « StallPanel » —
///     basculé par le bouton Work du menu bas-droite) : staging des
///     items du sac (drop {bag,slot} sur la case « Slot », prix via
///     goldedit), titre d'étal (EditBox), bouton StallBtn → C→S
///     <b>77</b> VendorSellItem `[i32 région][str titre][u8 n]
///     [n×(s16 cont,s16 slot,s16 loc,i32 prix)]` ; l'étal posé se
///     ferme par StallBtn → C→S <b>74</b> VendorClose.
///
/// Réseau (binds paresseux SessionCarry — même limite documentée que
/// <see cref="PartyHud"/>) : S→C <b>375</b> close · <b>376</b>
/// close-view · <b>377</b> status · <b>378</b> update-view · <b>379</b>
/// container(échec). La région d'étal (600-609, node S001) est résolue
/// via <see cref="MinimapPanel.StallRegionAtSelf"/> (CheckStallRegion
/// natif) — hors aire, le serveur rejette silencieusement le 77.
///
/// Ouverture de la vue d'un étal : <see cref="RequestStallView"/>
/// (bouton « Stall » de la barre d'action cible de TradeWindow, ou
/// n'importe quel appelant) envoie le 72 — la réponse 378 ouvre le
/// panneau achat ; cibler un joueur qui ne vend pas est un no-op
/// silencieux côté serveur.
/// </summary>
public partial class VendingWindow : Control
{
    private EdenSession? _session;
    private HudRoot? _hud;
    private readonly ConcurrentQueue<Action> _pending = new();

    // --- panneau achat (StallBuy) ---
    private Control? _buyWnd;
    private ItemList? _buyList;
    private Label? _buyTitle, _buyGold;
    private BaseButton? _buyBtn, _buyCloseBtn;
    private int _buySel = -1;

    // --- panneau vente (StallUI) ---
    private Control? _sellWnd;
    private StallDropCell? _dropCell;
    private LineEdit? _priceEdit, _priceEdit2;
    private LineEdit? _titleEdit;
    private TextEdit? _titleEditMulti;
    private ItemList? _sellList;
    private Label? _sellGold, _sellStatus;
    private BaseButton? _stageBtn, _unstageBtn, _stallBtn;
    private InventorySlot? _pendingItem;   // item droppé en attente de prix

    /// <summary>Session liée — exposée à
    /// <see cref="RequestStallView"/> (appel statique depuis la barre
    /// d'action cible).</summary>
    private static EdenSession? _sharedSession;

    public override void _Ready()
    {
        _hud = HudExtensions.FindHud();
        BuildBuyWindow();
        BuildSellWindow();
        // Racine toujours visible : transparente FullRect Ignore —
        // chaque panneau gère sa propre visibilité (Vue achat = 378,
        // StallPanel = bouton Work).
        Visible = true;
        VendingState.Changed += Refresh;
        PlayerBag.Changed += OnBagChanged;
        PlayerVitals.GoldChanged += RefreshGold;
        VendingState.SelfCharId =
            (uint)(SessionCarry.Character?.Id ?? 0);
    }

    public override void _ExitTree()
    {
        VendingState.Changed -= Refresh;
        PlayerBag.Changed -= OnBagChanged;
        PlayerVitals.GoldChanged -= RefreshGold;
        base._ExitTree();
    }

    /// <summary>Sac muté : la liste de staging n'est reconstruite que
    /// si le panneau vente est visible — la réouverture du StallPanel
    /// rappelle <see cref="RefreshSellList"/> (VisibilityChanged).</summary>
    private void OnBagChanged()
    {
        if (_sellWnd?.IsVisibleInTree() == true) RefreshSellList();
    }

    // ------------------------------------------------------------------
    // Binds NCID (thread réseau → file principale)
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        _sharedSession = s;
        // Mutations VendingState déplacées sur les binds précoces de
        // HudExtensions (appliquées au fil réseau — voir le commentaire
        // en tête de fichier) ; il ne reste ici que l'affichage chat.
        s.OnZone(375, r =>
        {
            var m = new ZoneClientVendorClose();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                if (m.CharId != 0)
                    _hud?.Chat?.AddSystem(UiTextEn.Translate("Stall closed."));
            });
        });
        s.OnZone(376, r =>
        {
            var m = new ZoneClientVendorCloseView();
            m.Deserialize(r);
            _pending.Enqueue(() => _hud?.Chat?.AddSystem(
                UiTextEn.Translate("The seller has closed the stand.")));
        });
        s.OnZone(377, r =>
        {
            var m = new ZoneClientVendorStatus();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                var msg = StatusText(m);
                if (msg != null) _hud?.Chat?.AddSystem(msg);
            });
        });
        s.OnZone(379, r =>
        {
            var m = new ZoneClientVendorContainer();
            m.Deserialize(r);
            _pending.Enqueue(() =>
                _hud?.Chat?.AddSystem(UiTextEn.Translate("No stall on this player.")));
        });
        GD.Print("[vending] binds 375-379 posés");
    }

    /// <summary>Texte d'erreur d'un statut 377 (codes natifs —
    /// VendorPackets / VendorSellItem désasm).</summary>
    private static string? StatusText(ZoneClientVendorStatus m)
    {
        if (m.Action == 1) return null;      // [1][2] succès — rien
        return m.Detail switch
        {
            0x11 => "Stall: invalid state (dead/busy/already vending).",
            0x12 => "Stall: too soon — wait ~1.5 s.",
            0x13 => "Stall: stalls are only allowed on map S001.",
            0x14 => "Stall: title must not be empty.",
            0x15 => "Stall: duplicate item in the list.",
            0x16 => "Stall: too many free (price 0) goods.",
            _ => $"Stall: refused (0x{m.Detail:x2}).",
        };
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { GD.PrintErr($"[vending] {ex}"); }
        }
    }

    // ------------------------------------------------------------------
    // Accès public
    // ------------------------------------------------------------------

    /// <summary>Demande la vue de l'étal d'un joueur — envoie 72
    /// AskVendorContainer `[i32 charId]` ; la réponse 378 ouvre le
    /// panneau achat. Silencieux si la cible ne vend pas
    /// (HandleAskVendorContainerAsync : stall==null → retour).</summary>
    public static void RequestStallView(int sellerCharId)
    {
        var s = _sharedSession ?? SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        VendingState.MarkViewRequested(sellerCharId);
        var send = s.SendZoneAsync(
            new ZoneAskVendorContainer { Value = sellerCharId },
            CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[vending] ask 72 : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Injecte un étal factice (vue achat + staging vente)
    /// pour les captures hors-ligne — pas une preuve live.</summary>
    public void EnsureDemo()
    {
        var m = new ZoneClientVendorUpdateView
        {
            SellerId = 777, Title = UiTextEn.Translate("Demo Stall — broken prices"),
        };
        m.Goods.Add(new VendorGood
        {
            Index = 0, ItemId = 11001, Price = 12500,
            EmbeddedAmount = 1,
        });
        m.Goods.Add(new VendorGood
        {
            Index = 1, ItemId = 11002, Price = 800,
            EmbeddedAmount = 20,
        });
        m.Goods.Add(new VendorGood
        {
            Index = 2, ItemId = 11003, Price = 999999,
            EmbeddedAmount = 1,
        });
        VendingState.MarkViewRequested(777);
        VendingState.ApplyUpdateView(m);
        // Côté vente : stage les 2 premiers items du sac (s'il y en a).
        var i = 0;
        foreach (var it in PlayerBag.Items)
        {
            if (i >= 2) break;
            if (it.ItemId == 0) continue;
            VendingState.StageItem(it, 1000 * (i + 1));
            i++;
        }
        VendingState.MyTitle = UiTextEn.Translate("Demo Stall — broken prices");
        Visible = true;
        if (_sellWnd != null) _sellWnd.Visible = true;
    }

    // ------------------------------------------------------------------
    // Panneau achat (StallBuy.layout / repli)
    // ------------------------------------------------------------------

    private void BuildBuyWindow()
    {
        _buyWnd = _hud?.Build("StallBuy.layout");
        if (_buyWnd != null)
        {
            _buyWnd.Name = "StallBuyPanel";
            AddChild(_buyWnd);
            HudRoot.Pin(_buyWnd, 0.5f, 0.5f, 0.5f, 0.5f,
                -167, -229, 167, 229);
        }
        else
        {
            _buyWnd = BuildBuyFallback();
        }
        _buyTitle = TopLeftVitals.Find<Label>(_buyWnd, "TitleText");
        _buyGold = TopLeftVitals.Find<Label>(_buyWnd, "GoldText");
        var silver = TopLeftVitals.Find<Label>(_buyWnd, "SilverText");
        if (silver != null) silver.Visible = false;
        _buyBtn = TopLeftVitals.Find<BaseButton>(_buyWnd, "BuyBtn");
        _buyCloseBtn =
            TopLeftVitals.Find<BaseButton>(_buyWnd, "CloseBtn");
        _buyList = FindItemList(_buyWnd);
        if (_buyBtn != null) _buyBtn.Pressed += DoBuy;
        if (_buyCloseBtn != null) _buyCloseBtn.Pressed += DoCloseView;
        if (_buyList != null)
        {
            _buyList.ItemSelected += i => _buySel = (int)i;
            _buyList.ItemActivated += i => { _buySel = (int)i; DoBuy(); };
        }
        DragHelper.EnsureTitleBar(_buyWnd, UiTextEn.Translate("Stall"));
        DragHelper.Persist(_buyWnd, "stallbuy");
        _buyWnd.Visible = false;
        // × de la fenêtre achat = fermer la vue côté serveur (75),
        // comme le bouton CloseBtn natif.
        _buyWnd.VisibilityChanged += () =>
        {
            if (!_buyWnd.Visible && VendingState.Viewing)
                DoCloseView();
        };
    }

    /// <summary>Premier ItemList du panneau (Listbox natif).</summary>
    private static ItemList? FindItemList(Control? root) =>
        root?.FindChildren("*", "ItemList", true, false)
            .Cast<ItemList>().FirstOrDefault();

    private Control BuildBuyFallback()
    {
        var p = new Panel { Name = "StallBuyPanel" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-167, -200);
        p.Size = new Vector2(334, 400);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        var title = new Label
        {
            Name = "TitleText",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        title.OffsetTop = 6;
        p.AddChild(title);
        var list = new ItemList { Name = "Listbox" };
        list.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        list.OffsetTop = 30;
        list.OffsetBottom = -76;
        list.OffsetLeft = 8;
        list.OffsetRight = -8;
        p.AddChild(list);
        var gold = new Label { Name = "GoldText" };
        gold.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        gold.Position = new Vector2(10, -66);
        p.AddChild(gold);
        var buy = new Button { Name = "BuyBtn", Text = UiTextEn.Translate("Buy") };
        buy.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        buy.Position = new Vector2(60, -44);
        buy.Size = new Vector2(90, 30);
        p.AddChild(buy);
        var close = new Button { Name = "CloseBtn", Text = UiTextEn.Translate("Close") };
        close.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        close.Position = new Vector2(-150, -44);
        close.Size = new Vector2(90, 30);
        p.AddChild(close);
        return p;
    }

    // ------------------------------------------------------------------
    // Panneau vente (StallUI.layout / repli)
    // ------------------------------------------------------------------

    private void BuildSellWindow()
    {
        _sellWnd = _hud?.Build("StallUI.layout");
        if (_sellWnd != null)
        {
            _sellWnd.Name = "StallPanel";
            AddChild(_sellWnd);
            HudRoot.Pin(_sellWnd, 0.5f, 0.5f, 0.5f, 0.5f,
                -287, -255, 287, 255);
        }
        else
        {
            _sellWnd = BuildSellFallback();
        }
        _priceEdit = TopLeftVitals.Find<LineEdit>(_sellWnd, "goldedit");
        _priceEdit2 =
            TopLeftVitals.Find<LineEdit>(_sellWnd, "goldedit1");
        if (_priceEdit2 != null)
        {
            _priceEdit2.Editable = false;
            _priceEdit2.Text = "0";
        }
        // Titre : EditBox natif = MultiLineEditbox (TextEdit Godot)
        // ou LineEdit selon le mapping effectif.
        var editBox =
            TopLeftVitals.Find<Control>(_sellWnd, "EditBox");
        _titleEdit = editBox as LineEdit;
        _titleEditMulti = editBox as TextEdit;
        _titleEdit ??= editBox?
            .FindChildren("*", "LineEdit", true, false)
            .Cast<LineEdit>().FirstOrDefault();
        _titleEditMulti ??= editBox?
            .FindChildren("*", "TextEdit", true, false)
            .Cast<TextEdit>().FirstOrDefault();
        _sellList = FindItemList(_sellWnd);
        _sellGold = TopLeftVitals.Find<Label>(_sellWnd, "goldText");
        var silverTxt =
            TopLeftVitals.Find<Label>(_sellWnd, "silverText");
        if (silverTxt != null) silverTxt.Visible = false;
        _stageBtn =
            TopLeftVitals.Find<BaseButton>(_sellWnd, "SetProductBtn");
        _unstageBtn =
            TopLeftVitals.Find<BaseButton>(_sellWnd, "OutBtn");
        _stallBtn =
            TopLeftVitals.Find<BaseButton>(_sellWnd, "StallBtn");
        var outBtn2 =
            TopLeftVitals.Find<BaseButton>(_sellWnd, "HelpBtn");
        if (outBtn2 != null) outBtn2.Visible = false;
        // Case « Slot » (SlotImage natif) : overlay drop inventaire.
        var slot = TopLeftVitals.Find<Control>(_sellWnd, "Slot");
        if (slot != null)
        {
            _dropCell = new StallDropCell();
            slot.AddChild(_dropCell);
            _dropCell.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _dropCell.ItemDropped += OnStagedDrop;
            _dropCell.Cleared += () => _pendingItem = null;
        }
        if (_stageBtn != null) _stageBtn.Pressed += DoStage;
        if (_unstageBtn != null) _unstageBtn.Pressed += DoUnstage;
        if (_stallBtn != null) _stallBtn.Pressed += DoStall;
        // Statut/erreur local (le natif n'en a pas).
        _sellStatus = new Label
        {
            Name = "__status",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _sellStatus.SetAnchorsAndOffsetsPreset(
            LayoutPreset.BottomWide);
        _sellStatus.OffsetTop = -20;
        _sellStatus.OffsetBottom = -4;
        _sellStatus.AddThemeFontSizeOverride("font_size", 10);
        _sellStatus.AddThemeColorOverride("font_color",
            new Color(1f, 0.7f, 0.5f));
        _sellWnd.AddChild(_sellStatus);
        DragHelper.EnsureTitleBar(_sellWnd, "My Stall");
        DragHelper.Persist(_sellWnd, "stallsell");
        _sellWnd.Visible = false;
        // Réaffichage via le bouton Work (pas de changement d'état —
        // on repeint quand même liste + or).
        _sellWnd.VisibilityChanged += () =>
        {
            if (_sellWnd.Visible)
            {
                RefreshGold();
                RefreshSellList();
            }
        };
    }

    private Control BuildSellFallback()
    {
        var p = new Panel { Name = "StallPanel" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-230, -220);
        p.Size = new Vector2(460, 440);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        // Titre d'étal.
        var tRow = new HBoxContainer();
        tRow.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        tRow.OffsetTop = 30;
        tRow.OffsetLeft = 10;
        tRow.OffsetRight = -10;
        tRow.AddChild(new Label { Text = UiTextEn.Translate("Stall name:") });
        var te = new LineEdit
        {
            Name = "EditBox",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PlaceholderText = UiTextEn.Translate("My stall…"),
            MaxLength = 60,
        };
        tRow.AddChild(te);
        p.AddChild(tRow);
        // Ligne item+prix.
        var sRow = new HBoxContainer();
        sRow.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        sRow.OffsetTop = 60;
        sRow.OffsetLeft = 10;
        sRow.OffsetRight = -10;
        var slot = new Control
        {
            Name = "Slot",
            CustomMinimumSize = new Vector2(44, 44),
        };
        sRow.AddChild(slot);
        sRow.AddChild(new Label { Text = UiTextEn.Translate("Price:") });
        var pe = new LineEdit
        {
            Name = "goldedit",
            CustomMinimumSize = new Vector2(110, 0),
            PlaceholderText = "0",
        };
        sRow.AddChild(pe);
        var pe2 = new LineEdit { Name = "goldedit1", Text = "0" };
        pe2.CustomMinimumSize = new Vector2(50, 0);
        sRow.AddChild(pe2);
        var add = new Button
        { Name = "SetProductBtn", Text = UiTextEn.Translate("Put on sale") };
        sRow.AddChild(add);
        p.AddChild(sRow);
        // Liste des marchandises.
        var list = new ItemList { Name = "list" };
        list.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        list.OffsetTop = 110;
        list.OffsetBottom = -76;
        list.OffsetLeft = 10;
        list.OffsetRight = -10;
        p.AddChild(list);
        var gold = new Label { Name = "goldText" };
        gold.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        gold.Position = new Vector2(10, -66);
        p.AddChild(gold);
        var un = new Button { Name = "OutBtn", Text = UiTextEn.Translate("Remove") };
        un.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        un.Position = new Vector2(60, -44);
        un.Size = new Vector2(90, 30);
        p.AddChild(un);
        var go = new Button { Name = "StallBtn", Text = UiTextEn.Translate("Open stall") };
        go.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        go.Position = new Vector2(-160, -44);
        go.Size = new Vector2(110, 30);
        p.AddChild(go);
        return p;
    }

    // ------------------------------------------------------------------
    // Actions C→S
    // ------------------------------------------------------------------

    private void Send(NetMessage msg)
    {
        if (_session?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[vending] pas de session — {msg.Name}");
            return;
        }
        var send = _session.SendZoneAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[vending] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>76 — achat de la ligne sélectionnée (lot entier ;
    /// le serveur valide qty == quantity du slot live).</summary>
    private void DoBuy()
    {
        if (_buySel < 0 || _buySel >= VendingState.Goods.Count) return;
        var g = VendingState.Goods[_buySel];
        var m = new ZoneVendorBuy { SellerId = VendingState.SellerId };
        m.Lines.Add(new ZoneVendorBuy.BuyLine(
            (short)g.LotQuantity, g.Index));
        Send(m);
        _buySel = -1;
    }

    /// <summary>75 — ferme la vue d'étal (reset op serveur ; pas de
    /// réponse attendue).</summary>
    private void DoCloseView()
    {
        if (VendingState.Viewing)
            Send(new ZoneVendorCloseView
            { SellerId = VendingState.SellerId });
        VendingState.CloseView();
    }

    /// <summary>Item droppé sur la case de mise en vente — gardé en
    /// attente jusqu'au prix (SetProductBtn).</summary>
    private void OnStagedDrop(int bag, int slot)
    {
        _pendingItem = PlayerBag.Items.FirstOrDefault(
            x => x.Bag == bag && x.Slot == slot);
    }

    /// <summary>SetProductBtn — stage l'item droppé avec le prix
    /// saisi (goldedit = prix du lot ; goldedit1 ignoré — monnaie
    /// plate côté serveur).</summary>
    private void DoStage()
    {
        if (_pendingItem is not { } it)
        {
            SetSellStatus(UiTextEn.Translate("Drop an item on the slot first."));
            return;
        }
        var price = ParsePrice();
        if (price < 0 || price > 999_999_999)
        {
            SetSellStatus(UiTextEn.Translate("Bad price (0..999999999)."));
            return;
        }
        if (VendingState.StagedItems.Any(
                e => e.Container == it.Bag && e.Slot == it.Slot))
        {
            SetSellStatus(UiTextEn.Translate("Item already on sale."));
            return;
        }
        VendingState.StageItem(it, price);
        _pendingItem = null;
        _dropCell?.ClearIcon();
        SetSellStatus("");
        RefreshSellList();
    }

    /// <summary>OutBtn — retire la ligne sélectionnée du staging.
    /// (Si l'étal est déjà ouvert, le retrait réel exige un nouveau
    /// 77 — refusé [0][0x11] « busy » par le serveur C# actuel ;
    /// le natif l'autorisait via ClearVendorInfo.)</summary>
    private void DoUnstage()
    {
        var sel = _sellList?.GetSelectedItems();
        if (sel == null || sel.Length == 0) return;
        VendingState.UnstageItem(sel[0]);
        if (VendingState.Selling)
        {
            // Re-remplissage : le serveur C# refuse (IsVending→0x11)
            // — on tente quand même, le statut affichera l'échec.
            SendSellItem();
        }
        RefreshSellList();
    }

    /// <summary>StallBtn — ouvre l'étal (77) ou le ferme (74) selon
    /// l'état courant.</summary>
    private void DoStall()
    {
        if (VendingState.Selling)
        {
            Send(new ZoneVendorClose { Mode = 0, Ignored = 0 });
            return;
        }
        SendSellItem();
    }

    /// <summary>Construit et envoie le 77 : région résolue par la
    /// position self (CheckStallRegion natif — S001 seulement), titre,
    /// entrées = staging complet (le serveur remplace tout).</summary>
    private void SendSellItem()
    {
        var title = CurrentTitle();
        if (title.Length == 0)
        {
            SetSellStatus(UiTextEn.Translate("Enter a stall name first."));
            return;
        }
        if (VendingState.StagedItems.Count == 0)
        {
            SetSellStatus(UiTextEn.Translate("No item staged for sale."));
            return;
        }
        var reg = _hud?.Minimap?.StallRegionAtSelf();
        if (reg == null)
        {
            SetSellStatus(
                UiTextEn.Translate("Not in a stall area (S001 market place)."));
            return;
        }
        var m = new ZoneVendorSellItem
        {
            RegionType = reg.Value.RegionType,
            Title = title,
        };
        foreach (var e in VendingState.StagedItems)
            m.Entries.Add(new ZoneVendorSellItem.SellEntry(
                e.Container, e.Slot,
                (short)Math.Max(1, e.Quantity), e.Price));
        VendingState.MyTitle = title;
        VendingState.MarkSellRequested();
        Send(m);
        SetSellStatus(UiTextEn.Translate("Stall request sent…"));
    }

    /// <summary>Titre saisi (EditBox LineEdit ou TextEdit natif) —
    /// borné à 90 octets Big5 (serveur : MaxTitleBytes).</summary>
    private string CurrentTitle()
    {
        var t = _titleEdit?.Text ?? _titleEditMulti?.Text ?? "";
        t = t.Trim();
        // 90 octets Big5 max — on borne en chars (ASCII ≤ bytes).
        return t.Length > 90 ? t[..90] : t;
    }

    private int ParsePrice()
    {
        var t = _priceEdit?.Text.Trim() ?? "";
        return int.TryParse(t, out var v) ? v : 0;
    }

    private void SetSellStatus(string text)
    {
        if (_sellStatus != null) _sellStatus.Text = text;
    }

    // ------------------------------------------------------------------
    // Rendu
    // ------------------------------------------------------------------

    /// <summary>Dispatch mode achat / vente selon
    /// <see cref="VendingState"/>.</summary>
    private void Refresh()
    {
        RefreshGold();
        // Achat : vue d'un étal TIERS (pas le mien).
        if (_buyWnd != null)
            _buyWnd.Visible = VendingState.Viewing &&
                !VendingState.IsSelfStall;
        // Vente : visible si le panneau était ouvert (l'utilisateur
        // l'a ouvert via Work) ou si mon étal est posé.
        if (_sellWnd != null && VendingState.Selling)
            _sellWnd.Visible = true;
        if (_buyWnd?.Visible == true) RefreshBuyList();
        if (_sellWnd?.Visible == true) RefreshSellList();
    }

    /// <summary>Remplit la liste des marchandises de l'étal regardé.</summary>
    private void RefreshBuyList()
    {
        if (_buyList == null) return;
        _buyList.Clear();
        if (_buyTitle != null)
            _buyTitle.Text = VendingState.Title;
        foreach (var g in VendingState.Goods)
        {
            var name = TextResolver.ItemName((int)g.ItemId)
                ?? $"#{g.ItemId}";
            var qty = g.LotQuantity > 1 ? $" ×{g.LotQuantity}" : "";
            var def = InventoryWindow.LookupDef((int)g.ItemId);
            Texture2D? tex = null;
            if (def != null && def.Code.Length > 0)
                tex = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
            var idx = _buyList.AddItem(
                $"  {name}{qty}   —   {g.Price:N0} g",
                icon: tex);
            _buyList.SetItemTooltipEnabled(idx, true);
            _buyList.SetItemTooltip(idx,
                $"{name}{qty}\nPrice: {g.Price:N0} g (whole lot)");
        }
        if (VendingState.Goods.Count == 0)
            _buyList.AddItem(UiTextEn.Translate("(stall empty)"));
    }

    /// <summary>Remplit la liste de staging / mes marchandises.</summary>
    private void RefreshSellList()
    {
        if (_sellList == null) return;
        _sellList.Clear();
        var rows = VendingState.StagedItems;
        foreach (var e in rows)
        {
            var name = TextResolver.ItemName(e.ItemId) ?? $"#{e.ItemId}";
            var qty = e.Quantity > 1 ? $" ×{e.Quantity}" : "";
            var def = InventoryWindow.LookupDef(e.ItemId);
            Texture2D? tex = null;
            if (def != null && def.Code.Length > 0)
                tex = AssetCatalog.GetTexture(
                    $"UI/itemicon/{def.Code}.png");
            var idx = _sellList.AddItem(
                $"  {name}{qty}   —   {e.Price:N0} g", icon: tex);
            _sellList.SetItemTooltip(idx,
                $"{name}{qty}\nPrice: {e.Price:N0} g (whole lot)");
        }
        if (rows.Count == 0)
            _sellList.AddItem(UiTextEn.Translate("(drop items from your bag)"));
        // Bouton principal : Open ↔ Close selon l'état serveur.
        if (_stallBtn is Button b)
            b.Text = VendingState.Selling ? UiTextEn.Translate("Close stall") : UiTextEn.Translate("Open stall");
        // Statut courant (377) — succès [1][2] → étal posé.
        if (VendingState.Selling)
            SetSellStatus(UiTextEn.Translate("Stall open — items stay in your bag."));
        else if (VendingState.LastStatus is { Action: 0 } st)
            SetSellStatus($"Refused (0x{st.Detail:x2}).");
    }

    private void RefreshGold()
    {
        var txt = $"{PlayerVitals.Gold:N0}";
        if (_buyGold != null) _buyGold.Text = txt;
        if (_sellGold != null) _sellGold.Text = txt;
    }

    // ------------------------------------------------------------------
    // Cellule drop (case « Slot » du StallUI) — accepte {bag, slot}
    // d'inventaire et affiche l'icône de l'item en attente de prix.
    // ------------------------------------------------------------------

    private sealed partial class StallDropCell : Panel
    {
        /// <summary>Drop inventaire {bag, slot} reçu.</summary>
        public event Action<int, int>? ItemDropped;
        /// <summary>Icône retirée (clic droit sur la case).</summary>
        public event Action? Cleared;
        private readonly TextureRect _icon;
        private int _itemId;

        public StallDropCell()
        {
            MouseFilter = MouseFilterEnum.Stop;
            AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.116f, 0.112f, 0.1f, 0.35f),
            });
            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode =
                    TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_icon);
        }

        /// <summary>Montre l'item droppé.</summary>
        private void SetItem(InventorySlot it)
        {
            _itemId = it.ItemId;
            var def = InventoryWindow.LookupDef(it.ItemId);
            _icon.Texture = def != null && def.Code.Length > 0
                ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png")
                : null;
            ItemTooltip.BindId(this, () => _itemId);
        }

        /// <summary>Case vidée.</summary>
        public void ClearIcon()
        {
            _itemId = 0;
            _icon.Texture = null;
        }

        public override bool _CanDropData(Vector2 atPosition,
            Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary)
                return false;
            var d = data.AsGodotDictionary();
            return d.ContainsKey("bag") && d.ContainsKey("slot");
        }

        public override void _DropData(Vector2 atPosition,
            Variant data)
        {
            var d = data.AsGodotDictionary();
            var bag = d["bag"].AsInt32();
            var slot = d["slot"].AsInt32();
            if (PlayerBag.Items.FirstOrDefault(
                    x => x.Bag == bag && x.Slot == slot) is { } it)
                SetItem(it);
            ItemDropped?.Invoke(bag, slot);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton
                {
                    Pressed: true, ButtonIndex: MouseButton.Right,
                } && _itemId != 0)
            {
                ClearIcon();
                Cleared?.Invoke();
                AcceptEvent();
            }
        }
    }
}
