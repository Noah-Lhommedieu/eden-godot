using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre courrier (vague 1-C) — layout natif <c>MailWnd.layout</c>
/// quand uidata est présent, sinon fenêtre sombre en code.
///
/// Ouverture : 399 <see cref="ZoneMailOpenBoxNotify"/> (service NPC
/// type 7 → <see cref="MailState.Opened"/>) — la fenêtre acquitte par
/// 95 <see cref="ZoneMailOpenBox"/> (op=7) puis demande la liste par
/// 96 <see cref="ZoneMailQuery"/> ; la zone relaie à la mission et
/// renvoie 400 <see cref="ZoneMailList"/>.
///
/// Contenu (format serveur réel, pas de champ inventé) :
///   - liste : expéditeur, sujet, date d'envoi, état lu/non-lu
///     (bit0 du flag), icône PJ quand <see cref="MailEntry.
///     AttachItemId"/> ou <see cref="MailEntry.AttachGold"/> non nul ;
///   - lecture : corps (<see cref="MailEntry.Content"/>), date
///     d'expiration (ExpireTime unix), PJ avec icône native
///     (itemicon/{code}) + tooltip <see cref="ItemTooltip"/> ;
///   - actions prouvées serveur : prendre item (100), prendre or
///     (101), supprimer (98), renvoyer (102), répondre/écrire (99
///     avec PJ item drag-drop {bag,slot} + or + COD),
///     « tout prendre » (100 lot mailId=0 + ids).
///
/// Aucune mutation UI hors thread principal : l'état est alimenté par
/// les binds <c>GameScene</c> via <see cref="MailState"/> ; la fenêtre
/// se contente de lire.
/// </summary>
public partial class MailWindow : Control
{
    private Control? _window;
    private TabContainer? _tabs;
    private VBoxContainer? _list;
    private Label? _status;
    // --- panneau lecture ---
    private PanelContainer? _readPane;
    private Label? _readSubject, _readFrom, _readDate, _readExpire;
    private Label? _readBody;
    private TextureRect? _attIcon;
    private Label? _attLabel;
    private Button? _takeItemBtn, _takeGoldBtn, _replyBtn, _returnBtn,
        _deleteBtn;
    // --- panneau écrire ---
    private LineEdit? _toEdit, _subEdit, _goldEdit, _codEdit;
    private TextEdit? _bodyEdit;
    private MailAttachSlot? _attachSlot;
    private Label? _sendStatus;

    private int _selMailId = -1;

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("MailWnd.layout");
        if (_window == null)
            BuildFallback();
        else
        {
            AddChild(_window);
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -330, -230, 330, 230);
            var close = TopLeftVitals.Find<BaseButton>(_window,
                "closeBtn");
            if (close != null) close.Pressed += Close;
            // Le TabControl natif de MailWnd.layout est écrasé par
            // notre contenu — on le cache s'il existe.
            var nativeTabs = TopLeftVitals.Find<Control>(_window,
                "TabControl");
            if (nativeTabs != null) nativeTabs.Visible = false;
        }
        BuildContent();
        DragHelper.EnsureTitleBar(_window ?? this, "Mailbox");
        DragHelper.Persist(_window ?? this, "mail");
        Visible = false;
        MailState.Changed += OnMailChanged;
        MailState.Opened += OnOpened;
        MailState.NewMail += OnNewMail;
        VisibilityChanged += OnVisChanged;
        // --mail-drive : sonde de validation live (même esprit que
        // --drag-test / --input-test) — rejoue les chemins des clics
        // UI : sélection (97) → prise PJ/or (100/101) → renvoi (102)
        // → envoi (99) → suppression (98) → re-liste (96). Destructif
        // sur le courrier ciblé : réservé aux sessions de validation
        // sur données seed.
        var driveArgs = OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs());
        if (driveArgs.Any(a => a == "--mail-drive"))
            AddChild(new DriveProbe(this));
    }

    public override void _ExitTree()
    {
        MailState.Changed -= OnMailChanged;
        MailState.Opened -= OnOpened;
        MailState.NewMail -= OnNewMail;
        base._ExitTree();
    }

    /// <summary>Ouverture NPC (399) → montre + acquitte + requête.</summary>
    private void OnOpened() => Visible = true;

    /// <summary>404 — un courrier vient d'arriver : requête + notif.</summary>
    private void OnNewMail()
    {
        if (Visible) QueryList();
    }

    /// <summary>Ouverture manuelle (--hud-open, hotkey…) : acquitte
    /// la boîte (95 op=7) et demande la liste (96) si une session
    /// zone existe ; hors session la fenêtre montre juste l'état.</summary>
    private void OnVisChanged()
    {
        if (!IsVisibleInTree()) return;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            if (!MailState.IsOpen)
                Send(new ZoneMailOpenBox { Op = 7 });
            QueryList();
        }
        RefreshList();
    }

    private void OnMailChanged()
    {
        if (!Visible && MailState.IsOpen) Visible = true;
        if (!MailState.IsOpen) { Visible = false; return; }
        RefreshList();
        RefreshRead();
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        _tabs = new TabContainer { Name = "__mailtabs" };
        _tabs.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _tabs.OffsetLeft = 8; _tabs.OffsetTop = 30;
        _tabs.OffsetRight = -8; _tabs.OffsetBottom = -40;
        _tabs.MouseFilter = MouseFilterEnum.Stop;
        host.AddChild(_tabs);

        // Onglet 1 : boîte de réception.
        var inbox = new Control { Name = UiTextEn.Translate("Inbox") };
        _tabs.AddChild(inbox);
        var split = new HBoxContainer();
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        inbox.AddChild(split);

        var left = new VBoxContainer();
        left.CustomMinimumSize = new Vector2(240, 0);
        split.AddChild(left);
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        // M16 : liste parchemin (ref Mail.png).
        scroll.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
        left.AddChild(scroll);
        _list = new VBoxContainer { Name = "__maillist" };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_list);

        var btns = new HBoxContainer();
        left.AddChild(btns);
        var refresh = EeSkin.Grey(new Button { Text = UiTextEn.Translate("Refresh") });
        refresh.Pressed += QueryList;
        btns.AddChild(refresh);
        var takeAll = EeSkin.Grey(new Button { Text = UiTextEn.Translate("Collect All") });
        takeAll.Pressed += TakeAll;
        btns.AddChild(takeAll);

        // Panneau lecture (droite).
        _readPane = new PanelContainer();
        _readPane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _readPane.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = EeSkin.Parchment,
            BorderColor = EeSkin.DarkLine,
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        split.AddChild(_readPane);
        var rb = new VBoxContainer();
        _readPane.AddChild(rb);
        _readSubject = new Label { Text = "" };
        _readSubject.AddThemeFontSizeOverride("font_size", 13);
        _readSubject.AddThemeColorOverride("font_color",
            new Color(0.55f, 0.1f, 0.1f));
        rb.AddChild(_readSubject);
        var meta = new HBoxContainer();
        rb.AddChild(meta);
        _readFrom = new Label { Text = "" };
        _readFrom.AddThemeColorOverride("font_color", new Color(0.12f, 0.11f, 0.1f));
        _readFrom.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        meta.AddChild(_readFrom);
        _readDate = new Label { Text = "" };
        _readDate.AddThemeColorOverride("font_color", new Color(0.3f, 0.28f, 0.25f));
        meta.AddChild(_readDate);
        _readExpire = new Label { Text = "" };
        _readExpire.AddThemeColorOverride("font_color",
            new Color(0.9f, 0.55f, 0.55f));
        rb.AddChild(_readExpire);
        _readBody = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _readBody.AddThemeFontSizeOverride("font_size", 12);
        _readBody.AddThemeColorOverride("font_color", new Color(0.12f, 0.11f, 0.1f));
        rb.AddChild(_readBody);

        // Ligne pièce jointe.
        var att = new HBoxContainer();
        rb.AddChild(att);
        _attIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(34, 34),
            ExpandMode = TextureRect.ExpandModeEnum.KeepSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        att.AddChild(_attIcon);
        _attLabel = new Label { Text = "", SizeFlagsHorizontal =
            SizeFlags.ExpandFill, VerticalAlignment =
            VerticalAlignment.Center };
        att.AddChild(_attLabel);

        var acts = new HBoxContainer();
        rb.AddChild(acts);
        _takeItemBtn = Act(acts, "Take Item", TakeItem);
        _takeGoldBtn = Act(acts, "Take Gold", TakeGold);
        _replyBtn = Act(acts, "Reply", Reply);
        _returnBtn = Act(acts, "Return", ReturnMail);
        _deleteBtn = Act(acts, UiTextEn.Translate("Delete"), DeleteMail);

        // Onglet 2 : écrire.
        var comp = new ScrollContainer { Name = UiTextEn.Translate("Write") };
        _tabs.AddChild(comp);
        var cv = new VBoxContainer();
        cv.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        comp.AddChild(cv);
        cv.AddChild(MakeRow(UiTextEn.Translate("To:"), _toEdit = new LineEdit
        { PlaceholderText = UiTextEn.Translate("recipient name") }));
        cv.AddChild(MakeRow("Sujet :", _subEdit = new LineEdit()));
        cv.AddChild(new Label { Text = UiTextEn.Translate("Message :") });
        _bodyEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 120),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        cv.AddChild(_bodyEdit);

        var attRow = new HBoxContainer();
        cv.AddChild(attRow);
        attRow.AddChild(new Label
        {
            Text = UiTextEn.Translate("Attachment:"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _attachSlot = new MailAttachSlot();
        attRow.AddChild(_attachSlot);
        attRow.AddChild(new Label
        {
            Text = UiTextEn.Translate("(drag an item from the bag)"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (attRow.GetChild(attRow.GetChildCount() - 1) is Control hint)
            hint.AddThemeColorOverride("font_color",
                new Color(0.7f, 0.7f, 0.75f));
        cv.AddChild(MakeRow("Or joint :", _goldEdit = new LineEdit
        { PlaceholderText = "0" }));
        cv.AddChild(MakeRow("COD (contre-remb.) :",
            _codEdit = new LineEdit { PlaceholderText = "0" }));
        _sendStatus = new Label { Text = "" };
        cv.AddChild(_sendStatus);
        var send = new Button { Text = UiTextEn.Translate("Send") };
        send.Pressed += SendMail;
        cv.AddChild(send);

        _status = new Label { Name = "__mailstatus" };
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        _status.Position = new Vector2(10, -34);
        _status.Size = new Vector2(400, 26);
        _status.AddThemeFontSizeOverride("font_size", 10);
        host.AddChild(_status);
    }

    private static Button Act(Control parent, string text,
        Action onPress)
    {
        var b = EeSkin.Grey(new Button { Text = text });
        b.Pressed += onPress;
        parent.AddChild(b);
        return b;
    }

    private static HBoxContainer MakeRow(string label, Control field)
    {
        var row = new HBoxContainer();
        var l = new Label
        {
            Text = label,
            CustomMinimumSize = new Vector2(150, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(l);
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(field);
        return row;
    }

    // ------------------------------------------------------------------
    // Liste (400)
    // ------------------------------------------------------------------

    private void RefreshList()
    {
        if (_list == null) return;
        foreach (var c in _list.GetChildren()) c.QueueFree();
        var mails = MailState.Mails;
        if (_status != null)
            _status.Text = mails.Count == 0
                ? UiTextEn.Translate("Empty mailbox — mail listed by MailQuery (96)")
                : UiTextEn.Translate($"{mails.Count} mail(s)");
        if (mails.Count == 0)
        {
            _list.AddChild(new Label { Text = UiTextEn.Translate("(no mail)") });
            return;
        }
        foreach (var m in mails)
        {
            var date = m.SendTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(m.SendTime)
                    .LocalDateTime.ToString("dd/MM HH:mm")
                : "";
            var icon = m.HasAttachment ? " [PJ]" : "";
            var row = new Button
            {
                Text = $"{(m.IsUnread ? "◆ " : "◇ ")}{UiTextEn.Translate(m.SenderName)}" +
                       $" — {UiTextEn.Translate(m.Subject)}{icon}\n{date}",
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 40),
            };
            row.AddThemeFontSizeOverride("font_size", 11);
            var flat = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
            var hov = new StyleBoxFlat { BgColor = new Color(0.8f, 0.78f, 0.72f) };
            row.AddThemeStyleboxOverride("normal", flat);
            row.AddThemeStyleboxOverride("hover", hov);
            row.AddThemeStyleboxOverride("pressed", hov);
            row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            var ink = m.IsUnread ? new Color(0.45f, 0.2f, 0f)
                                 : new Color(0.12f, 0.11f, 0.1f);
            foreach (var c in new[] { "font_color", "font_hover_color",
                         "font_pressed_color", "font_focus_color" })
                row.AddThemeColorOverride(c, ink);
            var id = m.MailId;
            row.Pressed += () => SelectMail(id);
            _list.AddChild(row);
        }
    }

    /// <summary>Clic sur un courrier : sélection + 97 MailRead
    /// (marque lu côté serveur) ; le corps est déjà dans le 400.</summary>
    private void SelectMail(int mailId)
    {
        _selMailId = mailId;
        var e = MailState.Mails.FirstOrDefault(x => x.MailId == mailId);
        if (e == null) return;
        if (e.IsUnread)
        {
            Send(new ZoneMailRead { MailId = mailId });
            MailState.MarkRead(mailId);
        }
        RefreshRead();
    }

    private void RefreshRead()
    {
        var e = MailState.Mails.FirstOrDefault(x => x.MailId == _selMailId);
        if (_readSubject == null) return;
        if (e == null)
        {
            _readSubject.Text = UiTextEn.Translate("(select a mail)");
            if (_readFrom != null) _readFrom.Text = "";
            if (_readDate != null) _readDate.Text = "";
            if (_readExpire != null) _readExpire.Text = "";
            if (_readBody != null) _readBody.Text = "";
            if (_attIcon != null) _attIcon.Texture = null;
            if (_attLabel != null) _attLabel.Text = "";
            SetActs(false);
            return;
        }
        _readSubject.Text = UiTextEn.Translate(e.Subject);
        if (_readFrom != null)
            _readFrom.Text = UiTextEn.Translate("From: ") + UiTextEn.Translate(e.SenderName);
        if (_readDate != null)
            _readDate.Text = e.SendTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(e.SendTime)
                    .LocalDateTime.ToString("dd/MM/yyyy HH:mm")
                : "";
        if (_readExpire != null)
            _readExpire.Text = e.ExpireTime > 0
                ? "Expire le " + DateTimeOffset.FromUnixTimeSeconds(
                    e.ExpireTime).LocalDateTime.ToString("dd/MM/yyyy HH:mm")
                : "";
        if (_readBody != null) _readBody.Text = UiTextEn.Translate(e.Content);

        // Pièce jointe.
        if (_attIcon != null)
        {
            _attIcon.Texture = null;
            ItemTooltip.BindId(_attIcon, () => 0);
            if (e.AttachItemId > 0)
            {
                var def = ItemTableCache.Get(e.AttachItemId);
                if (def != null && def.Code.Length > 0)
                    _attIcon.Texture = AssetCatalog.GetTexture(
                        $"UI/itemicon/{def.Code}.png");
                var attId = e.AttachItemId;
                ItemTooltip.BindId(_attIcon, () => attId,
                    Math.Max(1, e.AttachQty));
                var qty = e.AttachQty > 1 ? $" ×{e.AttachQty}" : "";
                if (_attLabel != null)
                    _attLabel.Text = $"📦 {TextResolver.ItemName(
                        e.AttachItemId) ?? $"#{e.AttachItemId}"}{qty}";
            }
            else if (e.AttachGold > 0)
            {
                if (_attLabel != null)
                    _attLabel.Text = UiTextEn.Translate($"💰 {e.AttachGold:N0} or");
            }
            else if (_attLabel != null)
                _attLabel.Text = UiTextEn.Translate("(no attachment)");
        }
        SetActs(true, e);
    }

    private void SetActs(bool on, MailEntry? e = null)
    {
        if (_takeItemBtn != null)
            _takeItemBtn.Visible = on && e != null && e.AttachItemId > 0;
        if (_takeGoldBtn != null)
            _takeGoldBtn.Visible = on && e != null && e.AttachGold > 0;
        if (_replyBtn != null) _replyBtn.Visible = on;
        if (_returnBtn != null) _returnBtn.Visible = on;
        if (_deleteBtn != null) _deleteBtn.Visible = on;
    }

    // ------------------------------------------------------------------
    // Actions (95-102)
    // ------------------------------------------------------------------

    /// <summary>96 — requête de la liste (page 0, tous).</summary>
    private void QueryList() =>
        Send(new ZoneMailQuery { Page = 0, AcceptOnlyFriend = 0 });

    /// <summary>100 — prend la PJ item du courrier sélectionné.</summary>
    private void TakeItem()
    {
        if (_selMailId < 0) return;
        MailState.PendingGetItemId = _selMailId;
        Send(new ZoneMailGetItem { MailId = _selMailId, FreeNeeded = 1 });
    }

    /// <summary>101 — prend l'or joint.</summary>
    private void TakeGold()
    {
        if (_selMailId < 0) return;
        MailState.PendingGetGoldId = _selMailId;
        Send(new ZoneMailGetGold { MailId = _selMailId });
    }

    /// <summary>100 en lot — prend toutes les PJ item (mailId=0 +
    /// liste des ids qui ont un item joint).</summary>
    private void TakeAll()
    {
        var ids = MailState.Mails
            .Where(m => m.AttachItemId > 0).Select(m => m.MailId)
            .ToList();
        if (ids.Count == 0) return;
        MailState.PendingGetItemId = ids[0];
        var m = new ZoneMailGetItem { MailId = 0, FreeNeeded = 1 };
        m.Ids.AddRange(ids);
        Send(m);
    }

    /// <summary>Répondre : onglet écrire pré-rempli (To=sender).</summary>
    private void Reply()
    {
        var e = MailState.Mails.FirstOrDefault(x => x.MailId == _selMailId);
        if (e == null || _toEdit == null || _tabs == null) return;
        _toEdit.Text = e.SenderName;
        if (_subEdit != null)
            _subEdit.Text = e.Subject.StartsWith(UiTextEn.Translate("Re:"))
                ? e.Subject : UiTextEn.Translate($"Re: {e.Subject}");
        _tabs.CurrentTab = 1;
    }

    /// <summary>102 — renvoie le courrier à l'expéditeur.</summary>
    private void ReturnMail()
    {
        if (_selMailId < 0) return;
        MailState.PendingReturnId = _selMailId;
        Send(new ZoneMailReturn { MailId = _selMailId });
    }

    /// <summary>98 — supprime le courrier sélectionné.</summary>
    private void DeleteMail()
    {
        if (_selMailId < 0) return;
        Send(new ZoneMailDelete { MailId = _selMailId });
    }

    /// <summary>99 — envoi du courrier composé (PJ item via le slot
    /// de drop, or + COD depuis les champs).</summary>
    private void SendMail()
    {
        if (_toEdit == null || _subEdit == null || _bodyEdit == null)
            return;
        var to = _toEdit.Text.Trim();
        if (to.Length == 0)
        {
            if (_sendStatus != null)
                _sendStatus.Text = UiTextEn.Translate("Recipient missing.");
            return;
        }
        var att = _attachSlot?.Item;
        var msg = new ZoneMailSend
        {
            To = to,
            Title = _subEdit.Text.Trim(),
            Body = _bodyEdit.Text,
            ItemBag = (short)(att?.Bag ?? -1),
            ItemSlot = (short)(att?.Slot ?? -1),
            Gold = ParseInt(_goldEdit?.Text),
            CodAmount = ParseInt(_codEdit?.Text),
        };
        Send(msg);
        if (_sendStatus != null)
            _sendStatus.Text = UiTextEn.Translate($"Sending to {to}…");
    }

    private static int ParseInt(string? t) =>
        int.TryParse((t ?? "").Trim(), out var v) ? Math.Max(v, 0) : 0;

    private void Close()
    {
        MailState.Close();
        Visible = false;
    }

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[mail] pas de session — {msg.Name}");
            return;
        }
        var task = s.SendZoneAsync(msg, CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[mail] envoi {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Données de démonstration hors-serveur (--hud-open sans
    /// session) : peuple l'état pour les captures de validation.</summary>
    public void EnsureDemo()
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true) return;
        if (MailState.Mails.Count > 0) { MailState.SetMock(
            MailState.Mails); return; }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        MailState.SetMock(new MailEntry[]
        {
            new()
            {
                MailId = 101, SenderName = "System",
                Subject = "Welcome to Eden Eternal",
                SendTime = (int)(now - 3600), Flag = 0,
                Content = "Welcome, adventurer! This mail " +
                    "contains a starter item.",
                AttachItemId = 100151002, AttachQty = 1,
                ExpireTime = now + 86400 * 29,
            },
            new()
            {
                MailId = 102, SenderName = "Marlow",
                Subject = "Commande boutique",
                SendTime = (int)(now - 7200), Flag = 1,
                Content = "Merci pour votre achat.",
                AttachGold = 2500, ExpireTime = now + 86400 * 30,
            },
            new()
            {
                MailId = 103, SenderName = "Auction House",
                Subject = "Auction purchase",
                SendTime = (int)(now - 900), Flag = 1,
                Content = "Your auction purchase is " +
                    "attached to this mail.",
                AttachItemId = 100201003, AttachQty = 3,
                ExpireTime = now + 86400 * 29,
            },
        });
    }

    private void BuildFallback()
    {
        var p = new Panel { Name = "MailWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-330, -230);
        p.Size = new Vector2(660, 460);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Mailbox");
        _window = p;
    }

    /// <summary>Slot de drop pour la PJ du courrier — accepte le
    /// payload {bag,slot} des ItemCell de l'inventaire et affiche
    /// l'icône de l'item (clic droit = vider).</summary>
    private partial class MailAttachSlot : PanelContainer
    {
        private readonly TextureRect _icon;
        private readonly Label _lbl;
        /// <summary>Item attaché (null = aucun).</summary>
        public InventorySlot? Item { get; private set; }

        public MailAttachSlot()
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

    /// <summary>Sonde <c>--mail-drive</c> : rejoue la chaîne d'actions
    /// utilisateur sur le premier courrier avec PJ (sinon le premier)
    /// — sélection/lecture (97), prise PJ item (100) ou or (101),
    /// prise d'or du courrier suivant (97+101), renvoi (102), envoi
    /// d'un courrier de test à « test2 » (99), suppression (98), puis
    /// re-liste (96). Chaque étape emprunte la méthode du bouton
    /// correspondant : les paquets émis sont identiques à un clic.
    /// Destructif : le courrier ciblé est supprimé et un courrier
    /// marqué « returned » (données seed).</summary>
    private sealed partial class DriveProbe : Node
    {
        private readonly MailWindow _w;
        private bool _fired;
        private int _retries;
        private MailEntry? _goldMail;

        public DriveProbe(MailWindow w) { _w = w; }

        public override void _Ready()
        {
            MailState.Changed += OnChanged;
            // Relance la requête liste toutes les 2 s tant qu'elle est
            // vide — la réponse mission peut être droppée « sans
            // session » si la boîte est ouverte trop tôt après
            // l'entrée en zone (course constatée en live).
            Later(_ => Retry(), 2.0, null!);
        }

        /// <summary>Re-émet 96 tant que la boîte est vide (max 8
        /// tentatives).</summary>
        private void Retry()
        {
            if (_fired || MailState.Mails.Count > 0
                || _retries++ >= 8) return;
            GD.Print("[mail-drive] liste vide — nouvelle requête 96");
            _w.QueryList();
            Later(_ => Retry(), 2.0, null!);
        }

        public override void _ExitTree()
            => MailState.Changed -= OnChanged;

        private void OnChanged()
        {
            if (_fired || MailState.Mails.Count == 0) return;
            _fired = true;
            _goldMail = MailState.Mails.FirstOrDefault(
                x => x.AttachGold > 0);
            var m = MailState.Mails.FirstOrDefault(
                        x => x.AttachItemId > 0)
                    ?? _goldMail
                    ?? MailState.Mails[0];
            GD.Print($"[mail-drive] cible id={m.MailId} " +
                     $"«{m.Subject}» item={m.AttachItemId} " +
                     $"or={m.AttachGold}");
            _w.SelectMail(m.MailId);            // 97 — lecture
            Later(StepTake, 0.9, m);
        }

        /// <summary>Étape 2 : prise de la PJ (100 item / 101 or).</summary>
        private void StepTake(MailEntry m)
        {
            if (m.AttachItemId > 0)
            {
                GD.Print($"[mail-drive] prendre PJ item id={m.MailId}");
                _w.TakeItem();                  // 100
            }
            else if (m.AttachGold > 0)
            {
                GD.Print($"[mail-drive] prendre or id={m.MailId}");
                _w.TakeGold();                  // 101
            }
            Later(StepGold, 0.9, m);
        }

        /// <summary>Étape 3 : si un courrier à or distinct de la cible
        /// existe — lecture (97) + prise d'or (101) → réponse 406.</summary>
        private void StepGold(MailEntry m)
        {
            var g = _goldMail;
            if (g != null && g.MailId != m.MailId)
            {
                GD.Print($"[mail-drive] prendre or id={g.MailId}");
                _w.SelectMail(g.MailId);        // 97
                _w.TakeGold();                  // 101
            }
            Later(StepReturn, 0.9, m);
        }

        /// <summary>Étape 4 : renvoi (102) du premier courrier restant
        /// → réponse 409. Le serveur borne le renvoi à un UPDATE
        /// « returned = 1 » sur un courrier du destinataire —
        /// n'importe quel courrier de la boîte est une cible
        /// valide (MissionDb.MailReturnAsync).</summary>
        private void StepReturn(MailEntry m)
        {
            var r = MailState.Mails.FirstOrDefault(
                        x => x.MailId != m.MailId) ?? m;
            GD.Print($"[mail-drive] renvoi id={r.MailId}");
            _w.SelectMail(r.MailId);            // 97
            _w.ReturnMail();                    // 102
            Later(StepSend, 0.9, m);
        }

        /// <summary>Étape 5 : envoi d'un courrier de test (99) via le
        /// vrai chemin de composition (champs + SendMail).</summary>
        private void StepSend(MailEntry m)
        {
            if (_w._toEdit != null) _w._toEdit.Text = "test2";
            if (_w._subEdit != null)
                _w._subEdit.Text = "test mail-drive";
            if (_w._bodyEdit != null)
                _w._bodyEdit.Text = "Courrier de validation --mail-drive.";
            GD.Print("[mail-drive] envoi 99 → test2");
            _w.SendMail();                      // 99
            Later(StepDelete, 0.9, m);
        }

        /// <summary>Étape 6 : suppression du courrier lu (98) puis
        /// re-liste (96) pour constater l'état final.</summary>
        private void StepDelete(MailEntry m)
        {
            // Re-sélectionne la cible : les étapes intermédiaires
            // (or/renvoi) ont déplacé _selMailId sur un autre courrier.
            _w.SelectMail(m.MailId);            // 97
            GD.Print($"[mail-drive] supprimer id={m.MailId}");
            _w.DeleteMail();                    // 98
            Later(_ =>
            {
                _w.QueryList();                 // 96
                GD.Print("[mail-drive] séquence terminée");
            }, 0.9, m);
        }

        /// <summary>Enchaîne une étape après un délai (thread
        /// principal via SceneTreeTimer).</summary>
        private void Later(Action<MailEntry> step, double sec,
            MailEntry m)
        {
            GetTree().CreateTimer(sec).Timeout += () => step(m);
        }
    }
}
