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
/// Fenêtre d'échange joueur↔joueur (Vague 2 — lot trade) — layout
/// natif <c>TradeWnd.layout</c> (FrameWindow « Root/TradeWnd » :
/// <c>selfBorder</c> 8× MySlot + GlodEdit/SilverEdit + LockOnMyImage,
/// <c>targetBorder</c> 8× TargetSlot + TargetNameText/GoldText,
/// LockBtn/OkBtn/CancelBtn).
///
/// Réseau (binds paresseux <see cref="SessionCarry.Session"/> — même
/// limite documentée que <see cref="PartyHud"/> : en --live direct la
/// session reste privée à GameScene → binds absents, démo ok) :
///   - S→C <b>331</b> invite (popup) · <b>332</b> démarrage ·
///     <b>333/335</b> add item (+fail) · <b>334</b> remove ·
///     <b>336/337</b> or (+fail) · <b>338</b> confirm · <b>339</b>
///     cancel · <b>340</b> complete · <b>353</b> reset · <b>394</b>
///     ready · <b>395</b> unconfirm ;
///   - C→S : <b>36</b> request (cible = joueur ciblé, barre d'action
///     sous la target frame) · <b>37</b> accept · <b>38</b> add
///     (drop {bag,slot} depuis l'inventaire) · <b>39</b> remove
///     (clic droit sur ma cellule) · <b>40</b> or · <b>41</b> lock ·
///     <b>91</b> ready (Ok) · <b>42</b> cancel.
///
/// État partagé : <see cref="TradeState"/> — complétion posée par le
/// 340 (émis aux deux au settle) ou déduite du double verrouillage
/// (voir l'en-tête de ZoneTradeMessages). Drag &amp; drop identique à
/// l'inventaire : payload <c>{bag, slot}</c> accepté en cible sur
/// les cellules de MA colonne.
/// </summary>
public partial class TradeWindow : Control
{
    private Control? _window;
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private HudRoot? _hud;
    private readonly ConcurrentQueue<Action> _pending = new();

    // Colonnes d'offres (8 cellules chacune comme le natif).
    private readonly List<TradeCell> _myCells = new();
    private readonly List<TradeCell> _partnerCells = new();
    private LineEdit? _goldEdit;
    private Label? _myName, _partnerName, _partnerGold;
    private Control? _myLockImg, _partnerLockImg;
    private BaseButton? _lockBtn, _okBtn, _cancelBtn;
    private Label? _statusLabel;
    private PanelContainer? _invitePopup;
    private Label? _inviteLabel;
    private HBoxContainer? _targetBar;
    private float _closeCountdown = -1f;

    public override void _Ready()
    {
        _hud = HudExtensions.FindHud();
        _window = _hud?.Build("TradeWnd.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            // Fenêtre native 232×466 — épinglée au centre.
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -116, -233, 116, 233);
        }
        BuildCells();
        WireNative();
        BuildInvitePopup();
        BuildTargetBar();
        DragHelper.EnsureTitleBar(_window ?? this, UiTextEn.Translate("Trade"));
        DragHelper.Persist(_window ?? this, "trade");
        Visible = false;
        TradeState.Changed += Refresh;
        PlayerBag.Changed += Refresh;
    }

    public override void _ExitTree()
    {
        TradeState.Changed -= Refresh;
        PlayerBag.Changed -= Refresh;
        base._ExitTree();
    }

    // ------------------------------------------------------------------
    // Binds NCID (thread réseau → file principale)
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(331, r =>
        {
            var m = new ZoneClientTradeRequest();
            m.Deserialize(r);
            _pending.Enqueue(() => TradeState.ApplyInvite(m));
        });
        s.OnZone(332, r =>
        {
            var m = new ZoneClientTradeStart();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                TradeState.ApplyStart(m);
                _hud?.Chat?.AddSystem(
                    UiTextEn.Translate($"Trade opened with {TradeState.PartnerName}."));
            });
        });
        s.OnZone(333, r =>
        {
            var m = new ZoneClientTradeAddItem();
            m.Deserialize(r);
            _pending.Enqueue(() => TradeState.ApplyAddItem(m));
        });
        s.OnZone(334, r =>
        {
            var m = new ZoneClientTradeRemoveItem();
            m.Deserialize(r);
            _pending.Enqueue(() => TradeState.ApplyRemoveItem(m));
        });
        s.OnZone(335, r =>
        {
            var m = new ZoneClientTradeAddItemFail();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                TradeState.RevertMyAdd(m);
                _hud?.Chat?.AddSystem(
                    UiTextEn.Translate($"Trade: add refused (code {m.Reason})."));
            });
        });
        s.OnZone(336, r =>
        {
            var m = new ZoneClientTradeModifyGold();
            m.Deserialize(r);
            _pending.Enqueue(() => TradeState.ApplyModifyGold(m));
        });
        s.OnZone(337, r =>
        {
            var m = new ZoneClientTradeModifyGoldFail();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                _hud?.Chat?.AddSystem(
                    UiTextEn.Translate("Trade: gold refused (insufficient funds?)."));
                Refresh();
            });
        });
        s.OnZone(338, r =>
        {
            var m = new ZoneClientTradeConfirm();
            m.Deserialize(r);
            _pending.Enqueue(TradeState.ApplyConfirm);
        });
        s.OnZone(339, r =>
        {
            var m = new ZoneClientTradeCancel();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                TradeState.ApplyCancel();
                _hud?.Chat?.AddSystem(UiTextEn.Translate("Trade cancelled."));
            });
        });
        s.OnZone(340, r =>
        {
            var m = new ZoneClientTradeComplete();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                TradeState.ApplyComplete();
                _hud?.Chat?.AddSystem(UiTextEn.Translate("Trade complete."));
            });
        });
        s.OnZone(353, r =>
        {
            var m = new ZoneClientTradeReset();
            m.Deserialize(r);
            _pending.Enqueue(TradeState.ApplyReset);
        });
        s.OnZone(394, r =>
        {
            var m = new ZoneClientTradeReady();
            m.Deserialize(r);
            _pending.Enqueue(TradeState.ApplyReady);
        });
        s.OnZone(395, r =>
        {
            var m = new ZoneClientTradeUnConfirm();
            m.Deserialize(r);
            _pending.Enqueue(TradeState.ApplyUnConfirm);
        });
        GD.Print("[trade] binds 331-340/353/394/395 posés");
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        _registry ??= GetTree().Root
            .FindChild("NetEntities", true, false) as EntityRegistry;
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { GD.PrintErr($"[trade] {ex}"); }
        }
        // Popup invite : reflète PendingInvite.
        if (HudPrefs.RefuseTrade && TradeState.PendingInvite.HasValue)
            AnswerInvite(false);
        var inv = TradeState.PendingInvite;
        if (_invitePopup != null &&
            _invitePopup.Visible != inv.HasValue)
            _invitePopup.Visible = inv.HasValue;
        if (inv.HasValue && _inviteLabel != null)
            _inviteLabel.Text =
                UiTextEn.Translate($"{inv.Value.Name}\nwants to trade with you");
        // Barre d'action cible : visible seulement quand la cible du
        // target frame est un joueur (kind 0 / entityId 0).
        if (_targetBar != null)
            _targetBar.Visible = CurrentTargetPlayer() != null &&
                !TradeState.IsOpen;
        // Compte à rebours de fermeture après complétion — à son
        // terme, l'état est clôturé (Close → Refresh → masquage).
        if (_closeCountdown >= 0f)
        {
            _closeCountdown -= (float)delta;
            if (_closeCountdown < 0f) TradeState.Close();
        }
    }

    // ------------------------------------------------------------------
    // Construction UI — cellules overlay sur les slots natifs
    // ------------------------------------------------------------------

    /// <summary>Pose 8 cellules overlay sur MySlot1..8 (drop cible
    /// + retrait) et TargetSlot1..8 (affichage seul). En repli,
    /// les conteneurs sont créés dans BuildFallback.</summary>
    private void BuildCells()
    {
        var host = _window;
        if (host == null) return;
        for (var i = 0; i < 8; i++)
        {
            var my = new TradeCell(i, mine: true);
            var tp = new TradeCell(i, mine: false);
            var myAnchor = TopLeftVitals.Find<Control>(
                host, $"MySlot{i + 1}");
            var tpAnchor = TopLeftVitals.Find<Control>(
                host, $"TargetSlot{i + 1}");
            (myAnchor ?? _window ?? (Control)this).AddChild(my);
            (tpAnchor ?? _window ?? (Control)this).AddChild(tp);
            my.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            tp.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            my.RemoveRequested += RemoveMyItem;
            my.ItemDropped += AddBagItem;
            _myCells.Add(my);
            _partnerCells.Add(tp);
        }
    }

    /// <summary>Rebranche les contrôles natifs du layout (boutons,
    /// edits, images de verrou, noms). En repli, les mêmes champs
    /// sont remplis par BuildFallback.</summary>
    private void WireNative()
    {
        var host = _window;
        if (host == null) return;
        _myName = TopLeftVitals.Find<Label>(host, "MyNameText");
        _partnerName = TopLeftVitals.Find<Label>(host, "TargetNameText");
        _myLockImg = TopLeftVitals.Find<Control>(host, "LockOnMyImage");
        _partnerLockImg =
            TopLeftVitals.Find<Control>(host, "LockTargetImage");
        _partnerGold = TopLeftVitals.Find<Label>(host, "GoldText");
        _lockBtn = TopLeftVitals.Find<BaseButton>(host, "LockBtn");
        _okBtn = TopLeftVitals.Find<BaseButton>(host, "OkBtn");
        _cancelBtn = TopLeftVitals.Find<BaseButton>(host, "CancelBtn");
        _goldEdit = TopLeftVitals.Find<LineEdit>(host, "GlodEdit");
        // Le natif a deux champs (or + argent) — le serveur C# traite
        // un seul montant entier : GlodEdit = montant, SilverEdit
        // désactivé pour éviter la confusion de conversion.
        var silver = TopLeftVitals.Find<LineEdit>(host, "SilverEdit");
        if (silver != null)
        {
            silver.Editable = false;
            silver.Text = "0";
        }
        var silverTxt =
            TopLeftVitals.Find<Label>(host, "SilverText");
        if (silverTxt != null) silverTxt.Visible = false;
        if (_lockBtn != null) _lockBtn.Pressed += DoLock;
        if (_okBtn != null) _okBtn.Pressed += DoReady;
        if (_cancelBtn != null) _cancelBtn.Pressed += DoCancel;
        if (_goldEdit != null)
            _goldEdit.TextSubmitted += _ => ApplyGoldEdit();
        // Le natif n'a pas de statut — un petit label bas de fenêtre.
        _statusLabel = new Label
        {
            Name = "__status",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _statusLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _statusLabel.OffsetTop = -24;
        _statusLabel.OffsetBottom = -6;
        _statusLabel.AddThemeFontSizeOverride("font_size", 10);
        host.AddChild(_statusLabel);
        // × de la barre titre : le bouton close de DragHelper masque
        // la FENÊTRE (_window), pas la racine — c'est donc son signal
        // qu'on écoute. Fermer la fenêtre d'un échange ouvert = cancel
        // (42) comme le natif, sinon l'état serveur resterait ouvert.
        host.VisibilityChanged += () =>
        {
            if (!host.Visible && TradeState.IsOpen &&
                _closeCountdown < 0f && !TradeState.Completed)
                DoCancel();
        };
    }

    // ------------------------------------------------------------------
    // Popup d'invitation + barre d'action cible
    // ------------------------------------------------------------------

    private void BuildInvitePopup()
    {
        _invitePopup = new PanelContainer
        { Name = "TradeInvitePopup" };
        HudRoot.Pin(_invitePopup, 0.5f, 0.35f, 0.5f, 0.35f,
            -110, -45, 110, 45);
        _invitePopup.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.155f, 0.149f, 0.133f, 0.95f),
            BorderColor = new Color(0.5f, 0.6f, 0.9f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var pv = new VBoxContainer();
        _inviteLabel = new Label
        { HorizontalAlignment = HorizontalAlignment.Center };
        pv.AddChild(_inviteLabel);
        var pb = new HBoxContainer
        { Alignment = BoxContainer.AlignmentMode.Center };
        var yes = new Button { Text = UiTextEn.Translate("Accept") };
        yes.Pressed += () => AnswerInvite(true);
        var no = new Button { Text = UiTextEn.Translate("Decline") };
        no.Pressed += () => AnswerInvite(false);
        pb.AddChild(yes);
        pb.AddChild(no);
        pv.AddChild(pb);
        _invitePopup.AddChild(pv);
        _invitePopup.Visible = false;
        AddChild(_invitePopup);
    }

    /// <summary>Mini-barre sous la target frame — actions joueur↔
    /// joueur quand la cible est un joueur : « Trade » (36) et
    /// « Stall » (72 AskVendorContainer, ouvre la vue d'étal si la
    /// cible en tient un — réponse 378 traitée par VendingWindow).
    /// Positionnée sous la target frame native (pin 0.5/0 —
    /// TargetInfoWnd occupe y 46..156).</summary>
    private void BuildTargetBar()
    {
        _targetBar = new HBoxContainer { Name = "TargetActionBar" };
        HudRoot.Pin(_targetBar, 0.5f, 0f, 0.5f, 0f, -70, 160, 70, 184);
        _targetBar.AddThemeConstantOverride("separation", 6);
        _targetBar.Alignment = BoxContainer.AlignmentMode.Center;
        var trade = new Button { Text = UiTextEn.Translate("Trade") };
        trade.AddThemeFontSizeOverride("font_size", 11);
        trade.Pressed += RequestTradeToTarget;
        var stall = new Button { Text = UiTextEn.Translate("Stall") };
        stall.AddThemeFontSizeOverride("font_size", 11);
        stall.Pressed += () =>
        {
            if (CurrentTargetPlayer() is { } p)
                VendingWindow.RequestStallView((int)p.CharId);
        };
        _targetBar.AddChild(trade);
        _targetBar.AddChild(stall);
        _targetBar.Visible = false;
        AddChild(_targetBar);
    }

    /// <summary>charId+nom du joueur ciblé (target frame — handle =
    /// charId pour les joueurs, kind 0 / entityId 0 du registre).</summary>
    private (uint CharId, string Name)? CurrentTargetPlayer()
    {
        var handle = _hud?.Target?.TargetHandle ?? 0;
        if (handle == 0) return null;
        if (_registry != null &&
            _registry.Entities.TryGetValue(handle, out var e) &&
            GodotObject.IsInstanceValid(e) && e.Kind == 0 &&
            e.EntityId == 0)
            return (handle, e.DisplayName);
        return null;
    }

    // ------------------------------------------------------------------
    // Actions C→S
    // ------------------------------------------------------------------

    private void Send(NetMessage msg)
    {
        if (_session?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[trade] pas de session — {msg.Name}");
            return;
        }
        var send = _session.SendZoneAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[trade] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>36 — demande d'échange vers le joueur ciblé.</summary>
    private void RequestTradeToTarget()
    {
        if (CurrentTargetPlayer() is not { } p)
        {
            _hud?.Chat?.AddSystem(UiTextEn.Translate("No player targeted to trade."));
            return;
        }
        TradeState.MarkOutgoingInvite(p.CharId, p.Name);
        Send(new ZoneTradeRequest { TargetCharId = p.CharId });
        _hud?.Chat?.AddSystem(UiTextEn.Translate($"Trade request sent to {p.Name}."));
    }

    /// <summary>Réponse à l'invite 331 : 37 accept / 42 refuse.</summary>
    private void AnswerInvite(bool accept)
    {
        var inv = TradeState.PendingInvite;
        TradeState.ClearInvite();
        if (_invitePopup != null) _invitePopup.Visible = false;
        if (!inv.HasValue) return;
        if (accept)
        {
            Send(new ZoneTradeRequestAccept
            { InitiatorCharId = inv.Value.CharId });
        }
        else
        {
            // Refus = cancel de la session créée au 36 côté serveur.
            Send(new ZoneTradeCancel
            { PartnerCharId = inv.Value.CharId, Reason = 0 });
        }
    }

    /// <summary>38 — item droppé sur une cellule de MA colonne :
    /// bag+slot du payload inventaire, tradeSlot = prochaine case
    /// libre, count = pile entière (le serveur C# lit F4).</summary>
    private void AddBagItem(int bag, int slot)
    {
        if (!TradeState.IsOpen || TradeState.SelfLocked) return;
        var it = PlayerBag.Items.FirstOrDefault(
            x => x.Bag == bag && x.Slot == slot);
        if (it == null || it.ItemId == 0) return;
        var index = TradeState.MyItems.Count;
        Send(new ZoneTradeAddItem
        {
            Bag = (ushort)it.Bag,
            Slot = (ushort)it.Slot,
            TradeSlot = (ushort)index,
            Count = (ushort)Math.Max(1, it.Amount),
        });
        // Optimiste : l'ACK 333 du serveur C# est vide — l'item est
        // affiché tout de suite ; un 335 le retirera.
        TradeState.AddMyItem(index, it);
    }

    /// <summary>39 — clic droit sur une cellule de mon offre :
    /// l'index = position dans ma liste (tradeIndex serveur).</summary>
    private void RemoveMyItem(int cellIndex)
    {
        if (!TradeState.IsOpen || TradeState.SelfLocked) return;
        if (cellIndex < 0 || cellIndex >= TradeState.MyItems.Count)
            return;
        Send(new ZoneTradeRemoveItem { Index = (ushort)cellIndex });
        TradeState.RemoveMyItem(cellIndex);
    }

    /// <summary>40 — or saisi : montant plat du serveur C#
    /// (F2 lu ; F1 = miroir natif du même montant).</summary>
    private void ApplyGoldEdit()
    {
        if (_goldEdit == null || !TradeState.IsOpen) return;
        var text = _goldEdit.Text.Trim();
        if (!uint.TryParse(text, out var gold))
        {
            _goldEdit.Text = TradeState.MyGold.ToString();
            return;
        }
        gold = Math.Min(gold, (uint)Math.Max(0, PlayerVitals.Gold));
        _goldEdit.Text = gold.ToString();
        Send(new ZoneTradeModifyGold { GoldMirror = gold, Gold = gold });
        TradeState.SetMyGold(gold);
    }

    /// <summary>41 — Lock : verrouille mon offre (écho 338 propre à
    /// l'émetteur → <see cref="TradeState.MarkConfirmSent"/>).</summary>
    private void DoLock()
    {
        if (!TradeState.IsOpen) return;
        TradeState.MarkConfirmSent();
        Send(new ZoneTradeConfirm());
    }

    /// <summary>91 — Ok : « prêt » final (confirmé côté serveur,
    /// pas d'écho → verrouillage optimiste).</summary>
    private void DoReady()
    {
        if (!TradeState.IsOpen) return;
        TradeState.MarkReadySent();
        Send(new ZoneTradeReady());
    }

    /// <summary>42 — Cancel : abandonne (items rendus par le serveur).</summary>
    private void DoCancel()
    {
        if (!TradeState.IsOpen) return;
        Send(new ZoneTradeCancel
        { PartnerCharId = TradeState.PartnerCharId, Reason = 0 });
        TradeState.Close();
        Visible = false;
    }

    // ------------------------------------------------------------------
    // Rendu
    // ------------------------------------------------------------------

    /// <summary>Repeint la fenêtre depuis <see cref="TradeState"/>.</summary>
    private void Refresh()
    {
        if (!TradeState.IsOpen)
        {
            // En cours de complétion : laisser visible le temps du
            // countdown (l'état n'est plus IsOpen qu'après Close()).
            if (_closeCountdown < 0f) Visible = false;
            return;
        }
        Visible = true;
        // Réaffiche la fenêtre si elle avait été masquée par × (le
        // × annule l'échange — un nouvel IsOpen = nouvelle session).
        if (_window != null) _window.Visible = true;
        if (_myName != null)
            _myName.Text = SessionCarry.Character?.Name ?? UiTextEn.Translate("Me");
        if (_partnerName != null)
            _partnerName.Text = TradeState.PartnerName;
        if (_partnerGold != null)
            _partnerGold.Text = $"{TradeState.PartnerGold:N0}";
        if (_myLockImg != null)
            _myLockImg.Visible = TradeState.SelfLocked;
        if (_partnerLockImg != null)
            _partnerLockImg.Visible =
                TradeState.PartnerLocked || TradeState.PartnerReady;
        // Cellules : ma colonne = TradeState.MyItems (+drop actif
        // tant que non verrouillé), partenaire = PartnerItems.
        for (var i = 0; i < _myCells.Count; i++)
        {
            var it = i < TradeState.MyItems.Count
                ? TradeState.MyItems[i] : null;
            _myCells[i].SetOffer(it, locked: TradeState.SelfLocked);
        }
        for (var i = 0; i < _partnerCells.Count; i++)
        {
            var it = i < TradeState.PartnerItems.Count
                ? TradeState.PartnerItems[i] : null;
            _partnerCells[i].SetOffer(it, locked: true);
        }
        if (_goldEdit != null && !_goldEdit.HasFocus())
            _goldEdit.Text = TradeState.MyGold.ToString();
        var goldEditable = !TradeState.SelfLocked;
        if (_goldEdit != null) _goldEdit.Editable = goldEditable;
        // Boutons : Lock dispo tant que je n'ai pas verrouillé ;
        // Ok idem ; Cancel toujours.
        if (_lockBtn != null) _lockBtn.Disabled = TradeState.SelfLocked;
        if (_okBtn != null) _okBtn.Disabled = TradeState.SelfReady;
        if (_statusLabel != null)
        {
            _statusLabel.Text = TradeState.Completed
                ? UiTextEn.Translate("Trade complete!")
                : TradeState.SelfLocked && !TradeState.PartnerLocked
                    ? UiTextEn.Translate("Waiting for partner…")
                    : TradeState.PartnerLocked && !TradeState.SelfLocked
                        ? UiTextEn.Translate("Partner locked — review then Lock/Ok")
                        : "";
        }
        // Auto-fermeture après complétion (pas de 340 sur le C#) —
        // le countdown déclenche Close() dans _Process.
        if (TradeState.Completed && _closeCountdown < 0f)
            _closeCountdown = 1.4f;
        else if (!TradeState.Completed)
            _closeCountdown = -1f;
    }

    // ------------------------------------------------------------------
    // Démo (--hud-open=trade) — données factices, pas une preuve live
    // ------------------------------------------------------------------

    /// <summary>Injecte une session d'échange factice pour les
    /// captures hors-ligne (diag — pas un état serveur réel).</summary>
    public void EnsureDemo()
    {
        TradeState.ApplyStart(new ZoneClientTradeStart
        { PartnerCharId = 4242, PartnerName = "DemoPlayer" });
        var bag = PlayerBag.Items;
        var i = 0;
        foreach (var it in bag)
        {
            if (i >= 3) break;
            if (it.ItemId == 0) continue;
            TradeState.AddMyItem(i, it);
            i++;
        }
        TradeState.SetMyGold(1500);
        TradeState.ApplyAddItem(new ZoneClientTradeAddItem
        {
            TradeSlot = 0, ItemId = 11001, Count = 1,
            ItemName = "Partner Sword",
        });
        TradeState.ApplyAddItem(new ZoneClientTradeAddItem
        {
            TradeSlot = 1, ItemId = 11002, Count = 5,
            ItemName = "Partner Potion ×5",
        });
        TradeState.ApplyModifyGold(new ZoneClientTradeModifyGold
        { Gold = 2000, GoldMirror = 2000 });
    }

    // ------------------------------------------------------------------
    // Cellule d'offre — icône + qté + tooltip ; drop cible (mienne)
    // et retrait clic droit.
    // ------------------------------------------------------------------

    private sealed partial class TradeCell : Panel
    {
        /// <summary>Clic droit → retrait de cette entrée (mienne) —
        /// argument = POSITION courante dans la liste (l'index de
        /// cellule, affichée à la position i de MyItems).</summary>
        public event Action<int>? RemoveRequested;
        /// <summary>Drop inventaire {bag, slot} reçu sur la cellule.</summary>
        public event Action<int, int>? ItemDropped;
        private readonly int _index;
        private readonly bool _mine;
        private readonly TextureRect _icon;
        private readonly Label _qty;
        private TradeOfferItem? _offer;
        private bool _locked;

        public TradeCell(int index, bool mine)
        {
            _index = index;
            _mine = mine;
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
            _qty = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _qty.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _qty.OffsetRight = -2;
            _qty.OffsetBottom = -1;
            _qty.AddThemeFontSizeOverride("font_size", 9);
            _qty.AddThemeColorOverride("font_color",
                new Color(1f, 0.95f, 0.6f));
            _qty.AddThemeColorOverride("font_outline_color",
                new Color(0f, 0f, 0f));
            _qty.AddThemeConstantOverride("outline_size", 3);
            AddChild(_qty);
        }

        /// <summary>Offre affichée (null = vide).</summary>
        public void SetOffer(TradeOfferItem? offer, bool locked)
        {
            _offer = offer;
            _locked = locked;
            Texture2D? tex = null;
            if (offer != null && offer.ItemId != 0)
            {
                var def = InventoryWindow.LookupDef(offer.ItemId);
                if (def != null && def.Code.Length > 0)
                    tex = AssetCatalog.GetTexture(
                        $"UI/itemicon/{def.Code}.png");
            }
            _icon.Texture = tex;
            _qty.Text = offer is { Count: > 1 }
                ? offer.Count.ToString() : "";
            ItemTooltip.Bind(this, () => _offer == null ? null
                : new InventorySlot(0, 0, _offer.ItemId, 0, 0, 0,
                    _offer.Count));
        }

        /// <summary>Drop cible : accepte le payload inventaire
        /// {bag, slot} — relayé à la fenêtre parente.</summary>
        public override bool _CanDropData(Vector2 atPosition,
            Variant data)
        {
            if (!_mine || _locked) return false;
            if (data.VariantType != Variant.Type.Dictionary)
                return false;
            var d = data.AsGodotDictionary();
            return d.ContainsKey("bag") && d.ContainsKey("slot");
        }

        public override void _DropData(Vector2 atPosition,
            Variant data)
        {
            var d = data.AsGodotDictionary();
            ItemDropped?.Invoke(d["bag"].AsInt32(),
                d["slot"].AsInt32());
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_mine && !_locked && _offer != null &&
                e is InputEventMouseButton
                {
                    Pressed: true,
                    ButtonIndex: MouseButton.Right,
                })
            {
                // Position courante dans ma liste d'offre (= index
                // de la cellule) — tradeIndex attendu par le serveur.
                RemoveRequested?.Invoke(_index);
                AcceptEvent();
            }
        }

    }

    // ------------------------------------------------------------------
    // Repli sans uidata : panneau deux colonnes générique.
    // ------------------------------------------------------------------

    private void BuildFallback()
    {
        var p = new Panel { Name = "TradeWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-240, -190);
        p.Size = new Vector2(480, 380);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
        var cols = new HBoxContainer();
        cols.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cols.OffsetTop = 28;
        cols.OffsetBottom = -70;
        cols.OffsetLeft = 10;
        cols.OffsetRight = -10;
        cols.AddThemeConstantOverride("separation", 12);
        p.AddChild(cols);

        Control MakeCol(string title, bool mine)
        {
            var box = new VBoxContainer();
            box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var head = new Label
            {
                Name = mine ? "MyNameText" : "TargetNameText",
                Text = title,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            box.AddChild(head);
            var grid = new GridContainer { Columns = 4 };
            grid.SizeFlagsVertical = SizeFlags.ExpandFill;
            for (var i = 0; i < 8; i++)
            {
                var anchor = new Control
                {
                    Name = (mine ? "MySlot" : "TargetSlot") + (i + 1),
                    CustomMinimumSize = new Vector2(44, 44),
                };
                grid.AddChild(anchor);
            }
            box.AddChild(grid);
            return box;
        }
        var myCol = MakeCol(UiTextEn.Translate("Me"), true);
        var tpCol = MakeCol(UiTextEn.Translate("Partner"), false);
        cols.AddChild(myCol);
        cols.AddChild(tpCol);
        // Ors + verrous.
        var lockMy = new Panel { Name = "LockOnMyImage" };
        lockMy.CustomMinimumSize = new Vector2(0, 4);
        lockMy.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        { BgColor = new Color(0.2f, 0.7f, 0.3f, 0.9f) });
        myCol.AddChild(lockMy);
        var lockTp = new Panel { Name = "LockTargetImage" };
        lockTp.CustomMinimumSize = new Vector2(0, 4);
        lockTp.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        { BgColor = new Color(0.2f, 0.7f, 0.3f, 0.9f) });
        tpCol.AddChild(lockTp);
        var goldRow = new HBoxContainer();
        myCol.AddChild(goldRow);
        goldRow.AddChild(new Label { Text = UiTextEn.Translate("Gold:") });
        var ge = new LineEdit { Name = "GlodEdit" };
        ge.CustomMinimumSize = new Vector2(90, 0);
        goldRow.AddChild(ge);
        var pg = new Label { Name = "GoldText", Text = "0" };
        tpCol.AddChild(pg);
        // Boutons.
        var row = new HBoxContainer
        { Alignment = BoxContainer.AlignmentMode.Center };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        row.OffsetTop = -62;
        row.OffsetBottom = -34;
        row.AddThemeConstantOverride("separation", 8);
        var ok = new Button { Name = "OkBtn", Text = UiTextEn.Translate("Ok") };
        var lk = new Button { Name = "LockBtn", Text = UiTextEn.Translate("Lock") };
        var cc = new Button { Name = "CancelBtn", Text = UiTextEn.Translate("Cancel") };
        row.AddChild(ok);
        row.AddChild(lk);
        row.AddChild(cc);
        p.AddChild(row);
    }
}
