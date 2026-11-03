using Godot;
using EdenClient.Data;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre « Couple / mariage » — Vague 2, structure inspirée du
/// <c>LoverTab</c> natif de <c>CommunityWnd.layout</c> (fiche
/// partenaire, détails du lien, actions, 4 slots de skills couple)
/// et de <c>Wedding.layout</c> (liste des cérémonies).
///
/// Données : <see cref="LoverState"/> alimenté par
/// <see cref="LoverBridge"/> (475-479/597/599).
///
/// Limite serveur actuelle : les réponses 475/476/477/478/479 ne
/// sont pas émises par SocialHandlers — la fiche n'est donc remplie
/// qu'au fil des updates reçus (597/599 + état local des actions
/// émises). Les 4 slots « skills couple » restent vides tant qu'aucun
/// champ du 477 ne les décrit — affichés grisés avec la mention.
///
/// Actions C→S via délégués branchés par le bridge :
/// <see cref="InviteSender"/> (154 [u32][str][s16]),
/// <see cref="MessageSender"/> (155 [str]),
/// <see cref="QueryInfoSender"/> (156), <see cref="BreakSender"/>
/// (157), <see cref="MarrySender"/> (249 [s16][s16]),
/// <see cref="WeddingsSender"/> (251).
/// </summary>
public partial class CoupleWindow : Control
{
    /// <summary>Dernière instance construite (bridge + --hud-open).</summary>
    public static CoupleWindow? Instance { get; private set; }

    /// <summary>État couple partagé.</summary>
    public readonly LoverState State = new();

    /// <summary>NCID 154 propose/accepte [u32 charId][str name][s16].</summary>
    public Func<uint, string, Task>? InviteSender;
    /// <summary>NCID 155 message couple [str].</summary>
    public Func<string, Task>? MessageSender;
    /// <summary>NCID 156 query fiche (corps vide).</summary>
    public Func<Task>? QueryInfoSender;
    /// <summary>NCID 157 rupture (corps vide).</summary>
    public Func<Task>? BreakSender;
    /// <summary>NCID 249 op mariage [s16][s16].</summary>
    public Func<short, Task>? MarrySender;
    /// <summary>NCID 251 query cérémonies → 599.</summary>
    public Func<Task>? WeddingsSender;

    /// <summary>Résolution « joueur ciblé » pour Propose : retourne
    /// (charId, nom) de la cible si c'est un joueur — branché par le
    /// bridge sur <see cref="TargetFrame"/> + registry.</summary>
    public Func<(uint CharId, string Name)?>? TargetResolver;

    private Panel? _panel;
    private Label? _partner, _status, _detail, _skills, _weddings;
    private LineEdit? _msgEdit;
    private Button? _propose, _break, _marry, _refresh, _wedBtn;
    private PanelContainer? _invitePopup;
    private Label? _inviteLabel;
    private ZoneLoverInvite? _pendingInvite;

    public void Build(HudRoot hud)
    {
        _panel = new Panel { Name = "CouplePanel" };
        HudRoot.Pin(_panel, 0.5f, 0.5f, 0.5f, 0.5f,
            -170, -215, 170, 215);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.03f, 0.05f, 0.93f),
            BorderColor = new Color(0.75f, 0.35f, 0.45f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });

        var v = new VBoxContainer { Name = "Rows" };
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Insets + place pour la barre de titre (même convention que
        // SocialWindow — pas de Size sur des ancres 0-1).
        v.OffsetLeft = 12; v.OffsetRight = -12;
        v.OffsetTop = 30; v.OffsetBottom = -8;
        v.AddThemeConstantOverride("separation", 6);

        _partner = Add(v, UiTextEn.Translate("Partner: —"), 13);
        _status = Add(v, UiTextEn.Translate("Single"), 11);
        _detail = Add(v, "", 10);

        // Actions sur la cible courante (joueur) : propose 154.
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        _propose = new Button
        {
            Text = UiTextEn.Translate("♥ Propose"),
            TooltipText = UiTextEn.Translate("Propose a couple to the targeted player ") +
                UiTextEn.Translate("(mutual proposals form the couple)"),
        };
        _propose.Pressed += OnPropose;
        row.AddChild(_propose);
        _marry = new Button
        {
            Text = UiTextEn.Translate("Marry"),
            TooltipText = UiTextEn.Translate("Register a wedding ceremony (249 op1)"),
        };
        _marry.Pressed += () => Send(
            () => MarrySender?.Invoke(1), "249");
        row.AddChild(_marry);
        _break = new Button
        {
            Text = UiTextEn.Translate("Break"),
            TooltipText = UiTextEn.Translate("End the couple (157)"),
        };
        _break.Pressed += () => Send(() => BreakSender?.Invoke(), "157");
        row.AddChild(_break);
        v.AddChild(row);

        // Message couple (155) + requête fiche (156).
        var row2 = new HBoxContainer();
        row2.AddThemeConstantOverride("separation", 4);
        _msgEdit = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("couple message…"),
            CustomMinimumSize = new Vector2(170, 0),
        };
        _msgEdit.TextSubmitted += t =>
        {
            Send(() => MessageSender?.Invoke(t), "155");
            _msgEdit.Clear();
        };
        row2.AddChild(_msgEdit);
        var msgBtn = new Button { Text = UiTextEn.Translate("Send"),
            TooltipText = UiTextEn.Translate("Send couple message (155)") };
        msgBtn.Pressed += () =>
        {
            if (_msgEdit.Text.Length > 0)
            {
                Send(() => MessageSender?.Invoke(_msgEdit.Text), "155");
                _msgEdit.Clear();
            }
        };
        row2.AddChild(msgBtn);
        _refresh = new Button
        {
            Text = "⟳", TooltipText = UiTextEn.Translate("Query lover info (156)"),
            CustomMinimumSize = new Vector2(30, 0),
        };
        _refresh.Pressed += () =>
            Send(() => QueryInfoSender?.Invoke(), "156");
        row2.AddChild(_refresh);
        v.AddChild(row2);

        v.AddChild(new HSeparator());
        // Les 4 slots de skills couple du LoverTab natif — pas de
        // données serveur (477 non émis) → placeholders grisés.
        var skillsRow = new HBoxContainer();
        skillsRow.AddThemeConstantOverride("separation", 6);
        for (var i = 0; i < 4; i++)
        {
            var slot = new Panel
            {
                CustomMinimumSize = new Vector2(34, 34),
                TooltipText = UiTextEn.Translate("Couple skill slot (no server data)"),
            };
            slot.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.12f, 0.14f, 0.5f),
                BorderColor = new Color(0.4f, 0.3f, 0.35f, 0.6f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            skillsRow.AddChild(slot);
        }
        _skills = new Label { Text = UiTextEn.Translate("couple skills — no data") };
        _skills.AddThemeFontSizeOverride("font_size", 9);
        _skills.Modulate = new Color(1, 1, 1, 0.5f);
        var sk = new VBoxContainer();
        sk.AddChild(skillsRow);
        sk.AddChild(_skills);
        v.AddChild(sk);

        v.AddChild(new HSeparator());
        var wedRow = new HBoxContainer();
        _wedBtn = new Button
        {
            Text = UiTextEn.Translate("Weddings"), TooltipText =
                UiTextEn.Translate("Query scheduled ceremonies (251 → 599)"),
        };
        _wedBtn.Pressed += () =>
            Send(() => WeddingsSender?.Invoke(), "251");
        wedRow.AddChild(_wedBtn);
        v.AddChild(wedRow);
        _weddings = Add(v, UiTextEn.Translate("No ceremony scheduled."), 10);

        _panel.AddChild(v);
        _panel.Visible = false;
        AddChild(_panel);

        BuildInvitePopup();

        DragHelper.EnsureTitleBar(_panel, "Couple");
        DragHelper.Persist(_panel, "couple");

        Instance = this;
        State.Changed += Refresh;
        State.InviteReceived += OnInvite;
        Refresh();
    }

    private static Label Add(VBoxContainer v, string text, int size)
    {
        var l = new Label { Text = text, AutowrapMode =
            TextServer.AutowrapMode.Word };
        l.AddThemeFontSizeOverride("font_size", size);
        v.AddChild(l);
        return l;
    }

    /// <summary>Popup d'invitation (475) — acceptation = même 154 en
    /// retour (proposition mutuelle → couple formé).</summary>
    private void BuildInvitePopup()
    {
        _invitePopup = new PanelContainer
        {
            Name = "LoverInvitePopup",
            Visible = false,
        };
        HudRoot.Pin(_invitePopup, 0.5f, 0.4f, 0.5f, 0.4f,
            -130, -50, 130, 50);
        _invitePopup.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.04f, 0.06f, 0.96f),
            BorderColor = new Color(0.8f, 0.4f, 0.5f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var v = new VBoxContainer();
        _inviteLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        v.AddChild(_inviteLabel);
        var row = new HBoxContainer
        { Alignment = BoxContainer.AlignmentMode.Center };
        var yes = new Button { Text = UiTextEn.Translate("Accept") };
        yes.Pressed += AcceptInvite;
        var no = new Button { Text = UiTextEn.Translate("Decline") };
        no.Pressed += () => _invitePopup.Visible = false;
        row.AddChild(yes);
        row.AddChild(no);
        v.AddChild(row);
        _invitePopup.AddChild(v);
        AddChild(_invitePopup);
    }

    // ------------------------------------------------------------------
    // Rafraîchissement
    // ------------------------------------------------------------------

    /// <summary>Repeint la fiche depuis <see cref="State"/>.</summary>
    public void Refresh()
    {
        var s = State;
        if (_partner != null)
            _partner.Text = s.PartnerCharId != 0
                ? UiTextEn.Translate($"Partner: {PartnerLabel(s)}") : UiTextEn.Translate("Partner: —");
        if (_status != null)
            _status.Text = s.PartnerCharId != 0
                ? (s.Married ? UiTextEn.Translate("Married ♥") : UiTextEn.Translate("Couple ♥"))
                : UiTextEn.Translate("Single");
        if (_detail != null)
            _detail.Text = DetailText(s);
        if (_propose != null)
            _propose.Disabled = s.PartnerCharId != 0;
        if (_marry != null)
            _marry.Disabled = s.PartnerCharId == 0 || s.Married;
        if (_break != null)
            _break.Disabled = s.PartnerCharId == 0;
        if (_msgEdit != null)
            _msgEdit.Editable = s.PartnerCharId != 0;
        RefreshWeddings(s);
    }

    private static string PartnerLabel(LoverState s) =>
        s.PartnerName.Length > 0
            ? $"{s.PartnerName} #{s.PartnerCharId}"
            : $"#{s.PartnerCharId}";

    /// <summary>Lignes de détail du lien (champs bruts du 477 +
    /// résultat 597 + position 479 — affichage best-effort tant que
    /// la sémantique par champ n'est pas résolue côté serveur).</summary>
    private static string DetailText(LoverState s)
    {
        var parts = new List<string>();
        if (s.LoveId != 0) parts.Add($"love #{s.LoveId}");
        if (s.Info is { } i)
        {
            if (i.Field6 != 0)
                parts.Add($"since {Epoch(i.Field6)}");
            if (i.Field7 != 0) parts.Add($"affection {i.Field7}");
            if (i.Field12.Length > 0) parts.Add($"«{i.Field12}»");
        }
        if (s.PartnerPos is { } p)
            parts.Add($"map S{p.SceneId:D3} ({p.X:F0},{p.Y:F0})");
        if (s.LastMarryResult >= 0)
            parts.Add($"marry op → {s.LastMarryResult}");
        return string.Join("  ·  ", parts);
    }

    private static string Epoch(int v)
    {
        try { return DateTimeOffset.FromUnixTimeSeconds(v)
            .UtcDateTime.ToString("yyyy-MM-dd"); }
        catch { return $"{v}"; }
    }

    /// <summary>Liste des cérémonies du dernier 599.</summary>
    private void RefreshWeddings(LoverState s)
    {
        if (_weddings == null) return;
        if (s.Weddings.Count == 0)
        {
            _weddings.Text = UiTextEn.Translate("No ceremony scheduled.");
            return;
        }
        var lines = new List<string>();
        foreach (var w in s.Weddings)
        {
            var t = w.LeftTime >= 3600
                ? $"{w.LeftTime / 3600}h{w.LeftTime % 3600 / 60:D2}m"
                : $"{w.LeftTime / 60}:{w.LeftTime % 60:D2}";
            lines.Add($"{w.Name1} & {w.Name2} — step {w.Status}, " +
                $"{t} left (#{w.LoveId})");
        }
        _weddings.Text = string.Join("\n", lines);
    }

    // ------------------------------------------------------------------
    // Interactions
    // ------------------------------------------------------------------

    /// <summary>Propose à la cible joueur courante (154).</summary>
    private void OnPropose()
    {
        var t = TargetResolver?.Invoke();
        if (t == null)
        {
            ShowInviteless();
            return;
        }
        Send(() => InviteSender?.Invoke(t.Value.CharId, t.Value.Name),
            "154");
    }

    private void ShowInviteless()
    {
        if (_weddings != null)
            _weddings.Text =
                UiTextEn.Translate("Target a player first (propose needs the target).");
    }

    /// <summary>475 : un joueur nous propose → popup.</summary>
    private void OnInvite(ZoneLoverInvite m)
    {
        _pendingInvite = m;
        if (_inviteLabel != null)
            _inviteLabel.Text =
                UiTextEn.Translate($"{m.PeerName} proposes a couple.\nAccept?");
        if (_invitePopup != null) _invitePopup.Visible = true;
    }

    /// <summary>Acceptation = proposition mutuelle 154 au demandeur.</summary>
    private void AcceptInvite()
    {
        if (_invitePopup != null) _invitePopup.Visible = false;
        var inv = _pendingInvite;
        _pendingInvite = null;
        if (inv == null) return;
        Send(() => InviteSender?.Invoke(
            (uint)inv.CharId, inv.Name), "154");
    }

    /// <summary>Ouvre/ferme la fenêtre ; à l'ouverture, requêtes 156
    /// (fiche) et 251 (cérémonies) pour resynchroniser.</summary>
    public void Toggle()
    {
        if (_panel == null) return;
        _panel.Visible = !_panel.Visible;
        if (_panel.Visible)
        {
            Send(() => QueryInfoSender?.Invoke(), "156");
            Send(() => WeddingsSender?.Invoke(), "251");
        }
    }

    /// <summary>Ouvre explicitement.</summary>
    public void Open()
    {
        if (_panel != null && !_panel.Visible) Toggle();
    }

    private void Send(Func<Task?> sender, string tag)
    {
        var send = sender?.Invoke();
        if (send != null)
            _ = send.ContinueWith(t => GD.PrintErr(
                    $"[couple] {tag} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Données de démonstration pour --hud-open=couple hors
    /// session : état lié + une cérémonie factice + popup invite.</summary>
    public void EnsureDemo()
    {
        State.Apply(new ZoneLoverInfo
        {
            Field1 = 200000042, Field2 = 7,
            Field4 = "Rosemary",
            Field6 = (int)DateTimeOffset.UtcNow
                .AddDays(-40).ToUnixTimeSeconds(),
            Field7 = 5200, Field14 = 0,
        });
        var w = new ZoneWeddingsInfo();
        w.Entries.Add(new ZoneWeddingsInfo.Entry
        {
            LoveId = 7, Name1 = "test1", Name2 = "Rosemary",
            Status = 2, LeftTime = 1830,
        });
        State.Apply(w);
        State.Apply(new ZoneLoverInvite
        { CharId = 200000010, PeerName = "Admin" });
        Open();
    }
}
