using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre de guilde (vague 1-A) — touche <b>G</b> : 3 onglets
/// (Infos / Membres / Annonce) façon <c>FamilyInfoWnd_NA.layout</c>
/// natif, style sombre Reforged, position persistée via
/// <see cref="DragHelper"/>.
///
/// Données : <see cref="GuildState"/> est alimenté par la pompe monde
/// de <see cref="SocialWindow"/> (un seul lecteur du canal monde —
/// trames 46-51/68/74/80) ; ici on ne fait qu'<b>émettre</b> sur les
/// deux canaux + écouter la trame zone <b>346</b>
/// (<see cref="ZoneClientChangeFamily"/>) pour le rang/nom self.
///
/// Envois éprouvés côté serveur (preuves dans les codecs) :
///   - monde 9 <see cref="WorldFamilyOp"/> — invite 0x01, accept 0x03,
///     reject 0x04, quit 0x05, kick 0x06, leader 0x07, disband 0x08,
///     editinfo 0x0a, setcadre 0x0c, add/del message 0x0f/0x11,
///     recruit 0x4e/0x4f/0x50 ;
///   - monde 10 <see cref="WorldQueryFamilyInfo"/> (Value=1 requis) ;
///   - monde 73 <see cref="WorldQueryFamilyEmblem"/> ;
///   - zone 49 <see cref="ZoneFamilyCreate"/> (création, 50 000 or),
///     81 <see cref="ZoneUpdateFamily"/> (refresh self → 346),
///     82 <see cref="ZoneSetFamilyEmblem"/>,
///     159 <see cref="ZoneFamilyDonateMoney"/>.
///
/// Résiduels serveur documentés : le 48 émet inviterId=0 (l'accept
/// 0x03 exige l'id réel → refus 47 attendu tant que le serveur n'est
/// pas corrigé) ; le rang des membres non-leader n'est pas sur le
/// wire (affichage Leader/Member/rang self).
/// </summary>
public partial class GuildWindow : Control
{
    private Panel? _panel;
    private TabContainer? _tabs;
    private Label? _infoName;
    private Label? _infoStats;
    private Label? _infoCreated;
    private Label? _infoTreasury;
    private TextEdit? _infoText;
    private ItemList? _memberList;
    private ItemList? _logList;
    private ItemList? _boardList;
    private ItemList? _recruitList;
    private LineEdit? _inviteEdit;
    private LineEdit? _createEdit;
    private LineEdit? _donateEdit;
    private LineEdit? _emblemEdit;
    private LineEdit? _boardTitle;
    private TextEdit? _boardContent;
    private CheckBox? _onlineOnly;
    private OptionButton? _rankChoice;
    private Control? _guildView;
    private Control? _noGuildView;
    private Panel? _invitePopup;
    private Label? _inviteLabel;
    private HBoxContainer? _whisperRow;
    private LineEdit? _whisperEdit;
    private string _whisperTarget = "";

    private EdenSession? _session;
    private bool _zoneBound;
    private HudRoot? _hud;
    private HBoxContainer? _inviteRow;
    private Control? _natFam;
    private readonly ConcurrentQueue<Action> _pending = new();

    /// <summary>Construit la fenêtre (appelée par MinimapPanel avec le
    /// HudRoot monté — même convention que <see cref="SocialWindow"/>).</summary>
    public void Build(HudRoot hud)
    {
        _hud = hud;
        _panel = new Panel { Name = "GuildPanel" };
        HudRoot.Pin(_panel, 0.5f, 0.5f, 0.5f, 0.5f, -390, -350, 390, 350);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.122f, 0.117f, 0.104f, 0.96f),
            BorderColor = new Color(0.4f, 0.5f, 0.75f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        _panel.Visible = false;
        AddChild(_panel);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        v.OffsetLeft = 10; v.OffsetRight = -10;
        v.OffsetTop = 30; v.OffsetBottom = -8;
        _panel.AddChild(v);

        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        v.AddChild(_tabs);
        _tabs.AddChild(BuildInfosTab());
        _tabs.AddChild(BuildMembersTab());
        _tabs.AddChild(BuildAnnounceTab());
        _tabs.AddChild(BuildRecruitTab());

        BuildInvitePopup();
        BuildWhisperRow(v);

        DragHelper.EnsureTitleBar(_panel, "Guild");
        DragHelper.Persist(_panel, "guild");
        GuildState.Changed += Refresh;
        MouseFilter = MouseFilterEnum.Ignore;
        Refresh();
    }

    // ------------------------------------------------------------------
    // Onglets
    // ------------------------------------------------------------------

    /// <summary>Onglet Infos — fiche guilde (nom, niveau, chef, membres,
    /// trésorerie…) ou vue « sans guilde » (création zone 49).</summary>
    private Control BuildInfosTab()
    {
        var tab = new VBoxContainer { Name = UiTextEn.Translate("Infos") };
        tab.AddThemeConstantOverride("separation", 4);

        _noGuildView = new VBoxContainer();
        _noGuildView.AddThemeConstantOverride("separation", 4);
        _noGuildView.AddChild(new Label
        {
            Text = UiTextEn.Translate("You are not in a guild."),
            Modulate = new Color(1, 1, 1, 0.6f),
        });
        var createBar = new HBoxContainer();
        _createEdit = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("guild name… (costs 50 000 gold)"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        createBar.AddChild(_createEdit);
        var createBtn = new Button
        {
            Text = UiTextEn.Translate("Create"),
            TooltipText = "ZoneServerFamilyCreate (zone 49) — 50 000 gold",
        };
        createBtn.Pressed += CreateGuild;
        createBar.AddChild(createBtn);
        _noGuildView.AddChild(createBar);
        tab.AddChild(_noGuildView);

        _guildView = new VBoxContainer();
        _guildView.AddThemeConstantOverride("separation", 4);
        _infoName = new Label();
        _infoName.AddThemeColorOverride("font_color",
            new Color(0.55f, 0.9f, 0.55f));
        _guildView.AddChild(_infoName);
        _infoStats = new Label();
        _guildView.AddChild(_infoStats);
        _infoCreated = new Label();
        _infoCreated.AddThemeFontSizeOverride("font_size", 11);
        _guildView.AddChild(_infoCreated);
        _infoTreasury = new Label();
        _guildView.AddChild(_infoTreasury);

        // Actions membre : invite (0x01), don (zone 159), quitter
        // (0x05), dissoudre (0x08 — chef).
        var act = new HBoxContainer();
        _inviteEdit = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("invite player…"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _inviteEdit.TextSubmitted += Invite;
        act.AddChild(_inviteEdit);
        var invBtn = new Button { Text = UiTextEn.Translate("Invite"), TooltipText = UiTextEn.Translate("Invite a player to the guild") };
        invBtn.Pressed += () => Invite(_inviteEdit.Text);
        act.AddChild(invBtn);
        _donateEdit = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("donate…"),
            CustomMinimumSize = new Vector2(70, 0),
        };
        _donateEdit.TextSubmitted += Donate;
        act.AddChild(_donateEdit);
        var donBtn = new Button { Text = UiTextEn.Translate("Donate"), TooltipText = "zone 159" };
        donBtn.Pressed += () => Donate(_donateEdit.Text);
        act.AddChild(donBtn);
        _guildView.AddChild(act);
        _inviteRow = act;

        var act2 = new HBoxContainer();
        var leaveBtn = new Button { Text = UiTextEn.Translate("Leave"), TooltipText = UiTextEn.Translate("Leave the guild") };
        leaveBtn.Pressed += () =>
            FireWorld(WorldFamilyOp.Simple(FamilyOps.Quit));
        act2.AddChild(leaveBtn);
        var disbandBtn = new Button
        {
            Text = UiTextEn.Translate("Disband"),
            TooltipText = UiTextEn.Translate("Disband the guild (leader only)"),
        };
        disbandBtn.Pressed += () =>
            FireWorld(WorldFamilyOp.Simple(FamilyOps.Disband));
        act2.AddChild(disbandBtn);
        var recruitBtn = new Button
        {
            Text = UiTextEn.Translate("Recruit"),
            TooltipText = UiTextEn.Translate("Open or close recruitment (leader)"),
        };
        recruitBtn.Pressed += () => FireWorld(
            WorldFamilyOp.Of(FamilyOps.SetRecruit,
                GuildState.Recruit > 0 ? 0 : 1));
        act2.AddChild(recruitBtn);
        _emblemEdit = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("emblem id"),
            CustomMinimumSize = new Vector2(60, 0),
        };
        act2.AddChild(_emblemEdit);
        var embBtn = new Button
        {
            Text = UiTextEn.Translate("Emblem"),
            TooltipText = "zone 82 — chef uniquement",
        };
        embBtn.Pressed += SetEmblem;
        act2.AddChild(embBtn);
        var refBtn = new Button { Text = UiTextEn.Translate("Refresh"), TooltipText = "monde 10 + zone 81" };
        refBtn.Pressed += RequestData;
        act2.AddChild(refBtn);
        _guildView.AddChild(act2);

        _guildView.AddChild(new Label { Text = UiTextEn.Translate("Log") });
        var logScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 110),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _logList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        logScroll.AddChild(_logList);
        _guildView.AddChild(logScroll);
        tab.AddChild(_guildView);
        TryNativeFamily();
        return tab;
    }

    /// <summary>Onglet Membres — liste (online, nom, classe, niveau,
    /// rang, contribution) + actions contextuelles.</summary>
    private Control BuildMembersTab()
    {
        var tab = new VBoxContainer { Name = UiTextEn.Translate("Members") };
        tab.AddThemeConstantOverride("separation", 4);
        _onlineOnly = new CheckBox
        {
            Text = UiTextEn.Translate("Online only"),
            TooltipText = "Case native « online » de FamilyInfoWnd",
        };
        _onlineOnly.Toggled += _ => RenderMembers();
        tab.AddChild(_onlineOnly);
        _memberList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
        };
        tab.AddChild(_memberList);

        var act = new HBoxContainer();
        var wBtn = new Button { Text = UiTextEn.Translate("Whisper") };
        wBtn.Pressed += WhisperToSelected;
        act.AddChild(wBtn);
        var kickBtn = new Button { Text = UiTextEn.Translate("Kick"), TooltipText = UiTextEn.Translate("Kick the selected member") };
        kickBtn.Pressed += () => OpOnSelected(FamilyOps.Kick);
        act.AddChild(kickBtn);
        var leadBtn = new Button
        {
            Text = UiTextEn.Translate("Make leader"),
            TooltipText = UiTextEn.Translate("Hand over leadership (leader only)"),
        };
        leadBtn.Pressed += () => OpOnSelected(FamilyOps.SelectLeader);
        act.AddChild(leadBtn);
        _rankChoice = new OptionButton();
        for (var i = 0; i <= 7; i++) _rankChoice.AddItem(UiTextEn.Translate($"rank {i}"), i);
        _rankChoice.Selected = 0;
        _rankChoice.TooltipText = UiTextEn.Translate("Rank 0-7");
        act.AddChild(_rankChoice);
        var cadreBtn = new Button
        {
            Text = UiTextEn.Translate("Set rank"),
            TooltipText = UiTextEn.Translate("Set the selected member's rank (privilege required)"),
        };
        cadreBtn.Pressed += SetCadreSelected;
        act.AddChild(cadreBtn);
        tab.AddChild(act);
        return tab;
    }

    /// <summary>Onglet Annonce — édition de l'info publique (0x0a) +
    /// message-board (0x0f/0x11, listé par le 51).</summary>
    private Control BuildAnnounceTab()
    {
        var tab = new VBoxContainer { Name = UiTextEn.Translate("Announce") };
        tab.AddThemeConstantOverride("separation", 4);
        tab.AddChild(new Label { Text = UiTextEn.Translate("Guild info (public)") });
        _infoText = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 70),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        tab.AddChild(_infoText);
        var saveInfo = new Button
        {
            Text = UiTextEn.Translate("Save announcement"),
            TooltipText = UiTextEn.Translate("Save the guild announcement (privilege required)"),
        };
        saveInfo.Pressed += () => FireWorld(
            WorldFamilyOp.OfStr(FamilyOps.EditInfo, _infoText?.Text ?? ""));
        tab.AddChild(saveInfo);

        tab.AddChild(new Label { Text = UiTextEn.Translate("Message board (51)") });
        _boardList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
        };
        _boardList.ItemSelected += ShowBoardEntry;
        tab.AddChild(_boardList);

        var postBar = new HBoxContainer();
        _boardTitle = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("title…"),
            CustomMinimumSize = new Vector2(140, 0),
        };
        postBar.AddChild(_boardTitle);
        _boardContent = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 40),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        postBar.AddChild(_boardContent);
        var postBtns = new VBoxContainer();
        var postBtn = new Button { Text = UiTextEn.Translate("Post"), TooltipText = UiTextEn.Translate("Post the message") };
        postBtn.Pressed += PostBoard;
        postBtns.AddChild(postBtn);
        var delBtn = new Button
        {
            Text = UiTextEn.Translate("Delete"),
            TooltipText = UiTextEn.Translate("Delete the message (privilege required)"),
        };
        delBtn.Pressed += DeleteBoard;
        postBtns.AddChild(delBtn);
        postBar.AddChild(postBtns);
        tab.AddChild(postBar);
        return tab;
    }

    /// <summary>Onglet Recrutement — liste des guildes en recrutement
    /// (80 via op 0x4f) + adhésion (op 0x50). Utile hors guilde.</summary>
    private Control BuildRecruitTab()
    {
        var tab = new VBoxContainer { Name = UiTextEn.Translate("Recruit") };
        tab.AddThemeConstantOverride("separation", 4);
        var bar = new HBoxContainer();
        var refBtn = new Button
        {
            Text = UiTextEn.Translate("Refresh list"),
            TooltipText = UiTextEn.Translate("Reload the first page"),
        };
        refBtn.Pressed += () =>
            FireWorld(WorldFamilyOp.Of(FamilyOps.RecruitPage, 0));
        bar.AddChild(refBtn);
        tab.AddChild(bar);
        _recruitList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
        };
        tab.AddChild(_recruitList);
        var joinBtn = new Button
        {
            Text = UiTextEn.Translate("Join selected"),
            TooltipText = UiTextEn.Translate("Join the selected guild (only if you have no guild)"),
        };
        joinBtn.Pressed += JoinSelected;
        tab.AddChild(joinBtn);
        return tab;
    }

    /// <summary>Popup d'invitation guilde (48) — accept 0x03 /
    /// reject 0x04. NOTE : le serveur C# émet inviterId=0 → l'accept
    /// est refusé 47 tant que le serveur ne renvoie pas l'id réel
    /// (résiduel documenté).</summary>
    private void BuildInvitePopup()
    {
        _invitePopup = new Panel { Name = "GuildInvitePopup" };
        _invitePopup.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.183f, 0.176f, 0.157f, 0.98f),
            BorderColor = new Color(0.55f, 0.75f, 0.45f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        HudRoot.Pin(_invitePopup, 0.5f, 0f, 0.5f, 0f, -160, 90, 160, 150);
        _invitePopup.Visible = false;
        var h = new HBoxContainer();
        h.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        h.Alignment = BoxContainer.AlignmentMode.Center;
        _inviteLabel = new Label();
        h.AddChild(_inviteLabel);
        var ok = new Button { Text = "✓", TooltipText = UiTextEn.Translate("Accept") };
        ok.Pressed += () => AnswerInvite(1);
        h.AddChild(ok);
        var no = new Button { Text = "×", TooltipText = UiTextEn.Translate("Decline") };
        no.Pressed += () => AnswerInvite(0);
        h.AddChild(no);
        _invitePopup.AddChild(h);
        AddChild(_invitePopup);
    }

    /// <summary>Ligne whisper (membres) — zone 34 canal 1.</summary>
    private void BuildWhisperRow(VBoxContainer v)
    {
        _whisperRow = new HBoxContainer { Visible = false };
        _whisperEdit = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("whisper…"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _whisperEdit.TextSubmitted += SendWhisper;
        _whisperRow.AddChild(_whisperEdit);
        var wSend = new Button { Text = "▶", TooltipText = UiTextEn.Translate("Send whisper") };
        wSend.Pressed += () => SendWhisper(_whisperEdit.Text);
        _whisperRow.AddChild(wSend);
        v.AddChild(_whisperRow);
    }

    // ------------------------------------------------------------------
    // Cycle de vie / réseau
    // ------------------------------------------------------------------

    /// <summary>Toggle (touche G) — ignorée si le chat a le focus.</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo ||
            k.Keycode != Key.G)
            return;
        if (HudExtensions.FindHud()?.ChatHasFocus == true) return;
        if (_panel != null)
        {
            _panel.Visible = !_panel.Visible;
            if (_panel.Visible) RequestData();
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Exposé pour <c>--hud-open=guild</c>/menu : force la
    /// visibilité + rafraîchit les données.</summary>
    public void Open()
    {
        if (_panel == null) return;
        _panel.Visible = true;
        RequestData();
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        if (!_zoneBound && _session?.Zone?.IsConnected == true)
            BindZone(_session!);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { GD.PrintErr($"[guild] {ex}"); }
        }
        MockIfAsked();
    }

    public override void _ExitTree() => GuildState.Changed -= Refresh;

    private void BindSession(EdenSession s)
    {
        _session = s;
        RequestData();
    }

    /// <summary>Lie le 346 zone (self change) et demande un refresh
    /// immédiat via le 81 (le monde a pu pousser la famille avant que
    /// la fenêtre existe — le 10 complet est aussi renvoyé).</summary>
    private void BindZone(EdenSession s)
    {
        _zoneBound = true;
        var selfId = SessionCarry.Character?.Id ?? 0;
        s.OnZone(346, r =>
        {
            var m = new ZoneClientChangeFamily();
            m.Deserialize(r);
            _pending.Enqueue(() => GuildState.ApplyZoneChange(m, selfId));
        });
        if (selfId > 0)
            FireZone(new ZoneUpdateFamily { CharId = (uint)selfId });
        GD.Print("[guild] bind zone 346 + requête 81/10 envoyées");
    }

    /// <summary>Requêtes de données : monde 10 (fiche+membres+board+
    /// journal) — la pompe de SocialWindow les dispatchera.</summary>
    private void RequestData()
    {
        FireWorld(new WorldQueryFamilyInfo { Value = 1 });
        var selfId = SessionCarry.Character?.Id ?? 0;
        if (selfId > 0)
            FireZone(new ZoneUpdateFamily { CharId = (uint)selfId });
    }

    private void FireWorld(NetMessage msg)
    {
        var s = _session;
        if (s?.World?.IsConnected != true) return;
        var send = s.SendWorldAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[guild] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void FireZone(NetMessage msg)
    {
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        var send = s.SendZoneAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[guild] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------------------------------------------------
    // Actions C→S
    // ------------------------------------------------------------------

    private void Invite(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        _inviteEdit?.Clear();
        FireWorld(WorldFamilyOp.OfStr(FamilyOps.Invite, name));
        _hud?.Chat?.AddSystem(UiTextEn.Translate($"Guild invite → «{name}»."));
    }

    private void CreateGuild()
    {
        var name = _createEdit?.Text.Trim() ?? "";
        if (name.Length == 0) return;
        // Zone 49 [str] — relayé monde (ZW 143) ; 50 000 or débités
        // par la zone si la création aboutit (push ZW 32 → 346).
        FireZone(new ZoneFamilyCreate { Value = name });
        _hud?.Chat?.AddSystem(UiTextEn.Translate($"Guild creation «{name}»… (50 000 gold)"));
    }

    private void Donate(string text)
    {
        if (!int.TryParse(text.Trim(), out var amount) || amount <= 0)
            return;
        _donateEdit?.Clear();
        FireZone(new ZoneFamilyDonateMoney { Value = amount });
    }

    private void SetEmblem()
    {
        if (!short.TryParse(_emblemEdit?.Text.Trim() ?? "", out var id))
            return;
        FireZone(new ZoneSetFamilyEmblem { EmblemId = id });
    }

    /// <summary>charId du membre sélectionné (0 si aucun).</summary>
    private int SelectedCharId()
    {
        if (_memberList == null) return 0;
        var sel = _memberList.GetSelectedItems();
        return sel.Length > 0 ? _memberList.GetItemMetadata(sel[0])
            .AsInt32() : 0;
    }

    private void OpOnSelected(byte op)
    {
        var id = SelectedCharId();
        if (id == 0) return;
        FireWorld(WorldFamilyOp.Of(op, id));
    }

    private void SetCadreSelected()
    {
        var id = SelectedCharId();
        if (id == 0 || _rankChoice == null) return;
        FireWorld(WorldFamilyOp.Of(FamilyOps.SetCadre, id,
            _rankChoice.GetSelectedId()));
    }

    private void WhisperToSelected()
    {
        var id = SelectedCharId();
        if (id == 0 || !GuildState.Members.TryGetValue(id, out var m))
            return;
        _whisperTarget = m.Name;
        if (_whisperRow != null) _whisperRow.Visible = true;
        if (_whisperEdit != null)
        {
            _whisperEdit.PlaceholderText = UiTextEn.Translate($"to {m.Name}…");
            _whisperEdit.GrabFocus();
        }
    }

    private void SendWhisper(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || _whisperTarget.Length == 0) return;
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        var send = s.SendZoneAsync(new ZoneChannelMsg
        {
            Channel = 1,
            Target = _whisperTarget,
            Message = text,
        }, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[guild] whisper : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
        _hud?.Chat?.AddLocalEcho(1, $"to {_whisperTarget}: {text}");
        _whisperEdit?.Clear();
        if (_whisperRow != null) _whisperRow.Visible = false;
    }

    private void AnswerInvite(int accept)
    {
        if (GuildState.PendingInvite is not { } inv) return;
        FireWorld(WorldFamilyOp.Of(
            accept != 0 ? FamilyOps.Accept : FamilyOps.Reject,
            inv.InviterId));
        GuildState.PendingInvite = null;
        if (_invitePopup != null) _invitePopup.Visible = false;
        _hud?.Chat?.AddSystem(accept != 0
            ? UiTextEn.Translate("Guild invite accepted.")
            : UiTextEn.Translate("Guild invite declined."));
    }

    private void ShowBoardEntry(long index)
    {
        if (index < 0 || index >= GuildState.Schedule.Count) return;
        var m = GuildState.Schedule[(int)index];
        if (_boardTitle != null) _boardTitle.Text = m.Title;
        if (_boardContent != null) _boardContent.Text = m.Content;
    }

    private void PostBoard()
    {
        var title = _boardTitle?.Text.Trim() ?? "";
        var content = _boardContent?.Text.Trim() ?? "";
        if (title.Length == 0 && content.Length == 0) return;
        FireWorld(WorldFamilyOp.OfStr(FamilyOps.AddMessage,
            title, content));
        _boardTitle?.Clear();
        _boardContent?.Clear();
    }

    private void DeleteBoard()
    {
        var sel = _boardList?.GetSelectedItems();
        if (sel == null || sel.Length == 0) return;
        var m = GuildState.Schedule[(int)sel[0]];
        FireWorld(WorldFamilyOp.Of(FamilyOps.DelMessage, m.MessageId));
    }

    private void JoinSelected()
    {
        var sel = _recruitList?.GetSelectedItems();
        if (sel == null || sel.Length == 0) return;
        var famId = _recruitList!.GetItemMetadata(sel[0]).AsInt32();
        FireWorld(WorldFamilyOp.Of(FamilyOps.RecruitJoin, famId));
    }

    // ------------------------------------------------------------------
    // Rendu
    // ------------------------------------------------------------------

    private bool _loggedFiche;
    private int _loggedMembers;
    private void Refresh()
    {
        var inGuild = GuildState.InGuild;
        if (!_loggedFiche && inGuild)
        {
            _loggedFiche = true;
            GD.Print($"[guild] fiche reçue : <{GuildState.Name}> " +
                $"Lv{GuildState.Level}");
        }
        if (_loggedMembers != GuildState.Members.Count)
        {
            _loggedMembers = GuildState.Members.Count;
            GD.Print($"[guild] membres : {_loggedMembers}");
        }
        if (_guildView != null) _guildView.Visible = inGuild;
        if (_noGuildView != null) _noGuildView.Visible = !inGuild;
        RenderInfos();
        RenderMembers();
        RenderBoard();
        RenderRecruit();
        if (_invitePopup != null && GuildState.PendingInvite is { } inv)
        {
            _inviteLabel!.Text =
                UiTextEn.Translate($"{inv.Inviter} invites you to <{inv.Family}>");
            _invitePopup.Visible = true;
        }
        else if (_invitePopup != null)
            _invitePopup.Visible = false;
    }

    /// <summary>Fiche guilde native (FamilyInfoWnd_NA) à la place des
    /// libellés codés main ; l'ancienne rangée « invite » reste dessous.</summary>
    private void TryNativeFamily()
    {
        var nat = _hud?.Build("FamilyInfoWnd_NA.layout");
        if (nat == null || _guildView == null) return;
        foreach (var c in _guildView.GetChildren())
            if (c is Control cc && cc != _inviteRow) cc.Visible = false;
        nat.CustomMinimumSize = new Vector2(760, 622);
        _guildView.AddChild(nat);
        _guildView.MoveChild(nat, 0);
        _natFam = nat;
        T("NameBackGrand_FamilyBor_FamilyLevel");
        void Btn(string leaf, Action a)
        {
            if (TopLeftVitals.Find<BaseButton>(nat, leaf) is { } b)
                b.Pressed += a;
        }
        Btn("ListBackgrand_ExitBtn", () =>
            FireWorld(WorldFamilyOp.Simple(FamilyOps.Quit)));
        Btn("ListBackgrand_DisbandBtn", () =>
            FireWorld(WorldFamilyOp.Simple(FamilyOps.Disband)));
        Btn("ListBackgrand_RecruitBtn", () => FireWorld(
            WorldFamilyOp.Of(FamilyOps.SetRecruit,
                GuildState.Recruit > 0 ? 0 : 1)));
        Btn("ListBackgrand_DonateBtn", () =>
        {
            if (_donateEdit != null) Donate(_donateEdit.Text);
        });
        Btn("ListBackgrand_EmblemBtn", SetEmblem);
        if (TopLeftVitals.Find<BaseButton>(nat, "ListBackgrand_LevelUpBtn")
                is { } lu) { lu.Disabled = true; lu.TooltipText = UiTextEn.Translate("Not available yet"); }
        // Libellés EN des boutons natifs (placeholders « 1234 » sinon).
        foreach (var (leaf, txt) in new[]
                 { ("ListBackgrand_NoticeBtn", UiTextEn.Translate("Edit Msg")),
                   ("ListBackgrand_RecruitBtn", UiTextEn.Translate("Recruit")),
                   ("ListBackgrand_DonateBtn", UiTextEn.Translate("Donate")),
                   ("ListBackgrand_LevelUpBtn", UiTextEn.Translate("Level Up")),
                   ("ListBackgrand_EmblemBtn", UiTextEn.Translate("Emblem")),
                   ("ListBackgrand_EmblemTestBtn", UiTextEn.Translate("Preview Emblem")),
                   ("ListBackgrand_ExitBtn", UiTextEn.Translate("Leave Guild")),
                   ("ListBackgrand_DisbandBtn", UiTextEn.Translate("Disband")),
                   ("ListBackgrand_CmtNew", UiTextEn.Translate("Comment")),
                   ("FamilyBor_FamilyListBtn", UiTextEn.Translate("Guild List")),
                   ("FamilyBor_EnterTerritoryBtn", UiTextEn.Translate("Enter Guild Town")),
                   ("FamilyBor_FamilyMission01", UiTextEn.Translate("Guild Task - Field")),
                   ("FamilyBor_FamilyMission02", UiTextEn.Translate("Guild Task - Sub")) })
            if (TopLeftVitals.Find<Button>(nat, leaf) is { } b) b.Text = txt;
        var ink = new Color(0.12f, 0.11f, 0.1f);
        foreach (var l in nat.FindChildren("*FamilyBor_*", "Label", true, false)
                     .Cast<Label>())
            l.AddThemeColorOverride("font_color", ink);
        if (TopLeftVitals.Find<ItemList>(nat, "ListBackgrand_ListBox") is { } lb)
        {
            lb.AddThemeColorOverride("font_color", ink);
            _natMembers = lb;
        }
    }

    private ItemList? _natMembers;

    private Label? T(string leaf) =>
        _natFam == null ? null : TopLeftVitals.Find<Label>(_natFam, leaf);

    private void RenderNativeFamily()
    {
        if (_natFam == null) return;
        var selfId = SessionCarry.Character?.Id ?? 0;
        var leaderName = GuildState.Members
            .TryGetValue(GuildState.LeaderId, out var lm)
            ? lm.Name : $"#{GuildState.LeaderId}";
        void Set(string leaf, string text)
        {
            if (T(leaf) is { } l) l.Text = text;
        }
        Set("FamilyNameText", $"<{GuildState.Name}>");
        Set("FamilyBor_FamilyLevel", $"Guild Lv. {GuildState.Level}");
        Set("FamilyBor_FamilyLeader", UiTextEn.Translate($"Leader: {leaderName}"));
        Set("FamilyBor_FamilyMember", UiTextEn.Translate($"Members: {GuildState.Members.Count}"));
        Set("FamilyBor_Reputation", UiTextEn.Translate($"Honor: {GuildState.Honor}"));
        Set("GoldValue", $"{GuildState.Treasury}");
        Set("FamilyBor_MemberTax", $"Member Tax: {GuildState.TaxRate}%");
        Set("FamilyBor_BusinessTax", $"Business Tax: {GuildState.BusinessTax}%");
        Set("BorText_Bulletin", GuildState.Info);
        if (_natMembers != null)
        {
            _natMembers.Clear();
            foreach (var m in GuildState.Members.Values.OrderBy(x =>
                         x.CharId == GuildState.LeaderId ? 0 : 1))
                _natMembers.AddItem(
                    UiTextEn.Translate($"{m.Name}   Lv {m.Level}   {RankLabel(m, selfId)}"));
        }
    }

    private void RenderInfos()
    {
        if (!GuildState.InGuild) return;
        RenderNativeFamily();
        var selfId = SessionCarry.Character?.Id ?? 0;
        if (_infoName != null)
            _infoName.Text = UiTextEn.Translate($"<{GuildState.Name}>  Lv{GuildState.Level}");
        var leaderName = GuildState.Members
            .TryGetValue(GuildState.LeaderId, out var lm)
            ? lm.Name : $"#{GuildState.LeaderId}";
        if (_infoStats != null)
            _infoStats.Text =
                UiTextEn.Translate($"Leader: {leaderName}    ") +
                UiTextEn.Translate($"Members: {GuildState.Members.Count}    ") +
                UiTextEn.Translate($"Exp: {GuildState.Exp}    ") +
                UiTextEn.Translate($"Honor: {GuildState.Honor}    ") +
                UiTextEn.Translate($"Emblem: {GuildState.Emblem}    ") +
                UiTextEn.Translate($"Recruit: {(GuildState.Recruit > 0 ? "open" : "closed")}") +
                (GuildState.SelfRank >= 0
                    ? UiTextEn.Translate($"    You: rank {GuildState.SelfRank}") : "");
        if (_infoCreated != null)
            _infoCreated.Text =
                UiTextEn.Translate($"Created: {(GuildState.CreateTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(
                        GuildState.CreateTime).ToString("yyyy-MM-dd")
                    : "—")}    ") +
                UiTextEn.Translate($"Hall: {(GuildState.HouseOpen > 0
                    ? $"S{GuildState.HouseOpen:D3}" : "—")}    ") +
                UiTextEn.Translate($"Tax: {GuildState.TaxRate}%/{GuildState.BusinessTax}%");
        if (_infoTreasury != null)
            _infoTreasury.Text =
                UiTextEn.Translate($"Treasury: {GuildState.Treasury} gold    ") +
                (GuildState.IsLeader(selfId) ? UiTextEn.Translate("(you are the leader)")
                    : "");
        if (_infoText != null && !_infoText.HasFocus())
            _infoText.Text = GuildState.Info;
        if (_logList != null)
        {
            _logList.Clear();
            foreach (var l in GuildState.Logs)
            {
                var date = l.Date > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(l.Date)
                        .ToString("MM-dd") : "??";
                _logList.AddItem($"[{date}] {l.Str1} {l.Str2} " +
                    $"(t{l.Type} #{l.LogId})");
            }
            if (GuildState.Logs.Count == 0)
                _logList.AddItem(UiTextEn.Translate("(empty)"));
        }
    }

    /// <summary>Rang affiché : Leader via leader_id ; titre cadre
    /// (rangs 0-7 → Titles[rank+2]) ; self via le 346 ; sinon Member —
    /// le wire ne porte pas le rang des autres membres.</summary>
    private static string RankLabel(GuildMember m, int selfId)
    {
        if (m.CharId == GuildState.LeaderId)
            return GuildState.Titles[1].Length > 0
                ? GuildState.Titles[1] : "Leader";
        if (m.CharId == selfId && GuildState.SelfRank >= 0)
        {
            var t = GuildState.SelfRank + 2;
            return t < GuildState.Titles.Length &&
                   GuildState.Titles[t].Length > 0
                ? GuildState.Titles[t] : $"Rank {GuildState.SelfRank}";
        }
        return GuildState.Titles[0].Length > 0
            ? GuildState.Titles[0] : "Member";
    }

    private void RenderMembers()
    {
        if (_memberList == null) return;
        var selfId = SessionCarry.Character?.Id ?? 0;
        var onlineOnly = _onlineOnly?.ButtonPressed == true;
        _memberList.Clear();
        foreach (var m in GuildState.Members.Values
                     .OrderByDescending(x => x.Online)
                     .ThenByDescending(x => x.CharId == GuildState.LeaderId)
                     .ThenBy(x => x.Name))
        {
            if (onlineOnly && m.Online == 0) continue;
            var cls = TextResolver.ClassName(m.ClassId)
                ?? $"C{m.ClassId}";
            var line = $"{(m.Online != 0 ? "●" : "○")} {m.Name}" +
                $" — Lv{m.Level} {cls}" +
                $" — {RankLabel(m, selfId)}" +
                (m.Contribution > 0 ? $" (+{m.Contribution})" : "");
            var idx = _memberList.AddItem(line);
            _memberList.SetItemMetadata(idx, m.CharId);
            if (m.Online == 0)
                _memberList.SetItemCustomFgColor(idx,
                    new Color(1, 1, 1, 0.45f));
            _memberList.SetItemTooltip(idx,
                $"{m.Name} — node {m.NodeId}, {cls} lv{m.ClassLevel}" +
                (m.SelfComment.Length > 0
                    ? $"\n«{m.SelfComment}»" : "") +
                (m.FamilyComment.Length > 0
                    ? $"\nnote: {m.FamilyComment}" : ""));
        }
        if (_memberList.ItemCount == 0)
            _memberList.AddItem(GuildState.InGuild
                ? UiTextEn.Translate("(no member data — press Refresh)") : UiTextEn.Translate("(not in a guild)"));
    }

    private void RenderBoard()
    {
        if (_boardList == null) return;
        _boardList.Clear();
        foreach (var m in GuildState.Schedule)
        {
            var date = m.CreateDate > 0
                ? DateTimeOffset.FromUnixTimeSeconds(m.CreateDate)
                    .ToString("MM-dd") : "??";
            _boardList.AddItem($"[{date}] {m.Title} — {m.PlayerName}");
        }
        if (GuildState.Schedule.Count == 0)
            _boardList.AddItem(UiTextEn.Translate("(empty board)"));
    }

    private void RenderRecruit()
    {
        if (_recruitList == null) return;
        _recruitList.Clear();
        foreach (var e in GuildState.RecruitEntries)
        {
            var idx = _recruitList.AddItem(
                UiTextEn.Translate($"<{e.Name}> Lv{e.Level} — {e.MemberCount} members") +
                UiTextEn.Translate($" — leader {e.LeaderName}"));
            _recruitList.SetItemMetadata(idx, e.FamilyId);
        }
        if (GuildState.RecruitEntries.Count == 0)
            _recruitList.AddItem(UiTextEn.Translate("(no recruiting guild — press Refresh)"));
    }

    // ------------------------------------------------------------------
    // Mock (--mock-guild : preuve visuelle hors-ligne)
    // ------------------------------------------------------------------

    private bool _mocked;
    private void MockIfAsked()
    {
        if (_mocked) return;
        _mocked = true;
        if (!System.Array.Exists(
                OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
                    .ToArray(), a => a == "--mock-guild"))
            return;
        var w = new BinWriter();
        w.WriteByte(0);                       // u8 type 0
        w.WriteShort(1);                      // u16 worldId
        w.WriteInt(900001);                   // famId
        w.WriteInt(1_700_000_000);            // create_time
        w.WriteString("EdenDev");             // name
        w.WriteByte(7);                       // lv
        w.WriteInt(4200);                     // exp
        w.WriteInt(200000001);                // leader
        w.WriteInt(9000);                     // exploit
        w.WriteInt(3);                        // emblem
        w.WriteShort(-1);                     // uniform
        w.WriteString("Bienvenue chez EdenDev — annonce de test.");
        w.WriteInt(500000);                   // treasury
        w.WriteShort(5); w.WriteShort(10);    // taxes
        w.WriteInt(0); w.WriteShort(0);       // elfking/spell
        w.WriteByte(0);                       // recruit flag
        w.WriteShort(502);                    // house_open
        w.WriteShort(1);                      // recruit
        w.WriteInt(77);                       // honor
        w.WriteShort(0);                      // u16 n membres
        w.WriteShort(3);                      // u16 3 titres
        w.WriteString("Membre"); w.WriteString("Chef");
        w.WriteString("Officier");
        w.WriteShort(0);                      // u16 0 privilèges
        var info = new WorldFamilyInfo();
        info.Deserialize(new BinReader(w.ToArray()));
        GuildState.ApplyInfo(info);
        // Deux membres factices via le vrai handler 50-type-8.
        var w2 = new BinWriter();
        w2.WriteByte(8);                      // u8 type 8
        w2.WriteShort(2);                     // u16 2 membres
        WriteMockMember(w2, 200000001, "GM-Alice", 100, 4, 1, 5);
        WriteMockMember(w2, 200000002, "bob", 30, 5, 1, 120);
        var chunk = new WorldFamilyInfo();
        chunk.Deserialize(new BinReader(w2.ToArray()));
        GuildState.ApplyInfo(chunk);
        GuildState.Schedule.Add(new GuildBoardMessage(
            1, 1_700_000_100, "Bienvenue",
            "Message board de test.", "GM-Alice"));
        GuildState.Logs.Add(new GuildLogEntry(
            1, 42, 1_700_000_000, 200000002, "bob", "join"));
        if (_panel != null) _panel.Visible = true;
        Refresh();
    }

    /// <summary>Écrit un record membre type-8 factice (layout
    /// queryMode — <see cref="WorldFamilyMemberUpdate.WriteMember"/>).</summary>
    private static void WriteMockMember(BinWriter w, int id, string name,
        short lv, short cls, byte online, int contrib)
    {
        w.WriteInt(id);                       // i32 charId
        w.WriteInt(1_699_999_999);            // i32 addFamilyTime
        w.WriteString(name);                  // str name
        w.WriteShort(lv);                     // s16 level
        w.WriteShort(2);                      // s16 nodeId
        w.WriteShort(cls);                    // s16 classId
        w.WriteShort(lv);                     // s16 classLv
        w.WriteByte(online);                  // u8 online
        w.WriteShort(0);                      // u16 f1a
        w.WriteByte(0);                       // u8 gender
        w.WriteShort(0);                      // u16 f1c
        w.WriteInt(0);                        // i32 f20
        w.WriteInt(1_700_000_000);            // i32 lastSaving
        w.WriteString("");                    // str selfComment
        w.WriteString("");                    // str familyComment
        w.WriteInt(contrib);                  // i32 contribution
    }
}
