using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre sociale (S9) — touche <b>O</b> : nom de guilde (read-only,
/// détails dans <see cref="GuildWindow"/> touche G), liste d'amis et
/// blacklist, whisper par contact.
///
/// Données — connexion <b>monde</b> (le serveur pousse les listes en
/// dehors du flux zone) : les trames sont routées par
/// <see cref="EdenSession.OnWorld"/>, alimenté par la boucle unique
/// <see cref="EdenSession.WorldListenLoopAsync"/> lancée par
/// GameScene — UN SEUL lecteur sur la connexion monde pendant le jeu
/// (les handlers parsent hors-thread puis postent dans la file
/// principale <see cref="_pending"/>).
///
/// Trames routées (formats prouvés — voir codecs
/// <see cref="Protocol.WorldUpdateFriendMember"/> etc.) :
///   - <b>52</b> ClientUpdateFriendMember → liste d'amis
///     (chunks concaténés — champs level/node/class/guild/online) ;
///   - <b>61</b> ClientUpdateBlackList → blacklist ;
///   - <b>50</b> ClientQueryFamilyInfo → nom de guilde +
///     <see cref="GuildState"/> complet (types 0/8…) ;
///   - <b>53</b> ClientFriendInvitation → invite d'ami entrante ;
///   - <b>46/47/48/49/51/68/74/80</b> famille monde →
///     <see cref="GuildState"/> (consommé par
///     <see cref="GuildWindow"/>).
///
/// Envois monde : 12 AddFriend `[str][s16]` (1=ami, 0=blacklist),
/// 22 RemoveFriend `[i32]`, 14 FriendInvitation `[i32][s16]`,
/// 13 QueryFriendInfo (re-requête à l'ouverture — la liste est aussi
/// poussée à l'entrée monde, avant que la fenêtre existe),
/// 10 QueryFamilyInfo `[u8 1]` (1 requis côté serveur).
///
/// Ouverture : touche O (<see cref="_UnhandledKeyInput"/>) — ignorée
/// pendant la saisie chat (<see cref="HudRoot.ChatHasFocus"/>).
/// </summary>
public partial class SocialWindow : Control
{
    private Control? _panel;
    private Label? _guildLabel;
    private ItemList? _friendList;
    private ItemList? _blockList;
    // communitywnd utilise des MultiColumnList natives (MclList) —
    // mêmes données, remplies en colonnes quand le layout est monté.
    private MclList? _friendMcl;
    private MclList? _blockMcl;
    private LineEdit? _addEdit;
    private HBoxContainer? _whisperRow;
    private HBoxContainer? _inviteRow;
    private LineEdit? _whisperEdit;
    private string _whisperTarget = "";

    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();
    private HudRoot? _hud;

    public void Build(HudRoot hud)
    {
        _hud = hud;
        // Cadre natif CommunityWnd (onglets Friend/Search/Team/Lover —
        // shot de réf. social) ; la liste d'amis native est un
        // MultiColumnList → ItemList Godot (une colonne de texte
        // formaté, charId en métadonnée). Repli = ancien panneau.
        _panel = hud.Build("CommunityWnd.layout");
        if (_panel != null)
            BuildNative();
        else
            BuildFallback();
        DragHelper.EnsureTitleBar(_panel!, "Social");
        DragHelper.Persist(_panel!, "social");
        SocialState.Changed += Refresh;
        MouseFilter = MouseFilterEnum.Ignore;
        Refresh();
    }

    /// <summary>Montage sur le cadre natif : listes Friend/Block,
    /// boutons AddFriend/Block (popup nom) et bandeau whisper.</summary>
    private void BuildNative()
    {
        _panel!.Name = "SocialPanel";   // clé de ToggleByName (CommuBtn)
        AddChild(_panel!);
        HudRoot.Pin(_panel!, 0.5f, 0.5f, 0.5f, 0.5f,
            -330, -245, 330, 245);
        _panel!.Visible = false;
        _friendList = TopLeftVitals.Find<ItemList>(_panel, "FriendList");
        _blockList = TopLeftVitals.Find<ItemList>(_panel, "BlockList");
        _friendMcl = TopLeftVitals.Find<MclList>(_panel, "FriendList");
        _blockMcl = TopLeftVitals.Find<MclList>(_panel, "BlockList");
        _guildLabel = TopLeftVitals.Find<Label>(_panel,
            "FriendListText");
        if (_friendList != null)
            _friendList.ItemSelected += _ => RefreshWhisperTarget();
        if (_friendMcl != null)
            _friendMcl.RowSelected += _ => RefreshWhisperTarget();
        if (_blockList != null)
            _blockList.ItemActivated += idx =>
            {
                var id = (int)_blockList
                    .GetItemMetadata((int)idx).AsInt64();
                RemoveFriend(id);          // déblocage (monde 22)
            };
        if (_blockMcl != null)
            _blockMcl.RowActivated += row =>
            {
                if (_blockMcl.RowTag(row) is long lid)
                    RemoveFriend((int)lid);
            };

        var addBtn = TopLeftVitals.Find<Button>(_panel,
            "AddFriendBtn");
        if (addBtn != null)
        {
            addBtn.Text = UiTextEn.Translate("Add Friend");
            addBtn.Pressed += () => AskName(1);
        }
        var blkBtn = TopLeftVitals.Find<Button>(_panel, "BlockBtn");
        if (blkBtn != null)
        {
            blkBtn.Text = UiTextEn.Translate("Block");
            blkBtn.Pressed += () => AskName(0);
        }
        var editCard = TopLeftVitals.Find<Control>(_panel, "EditCardBtn");
        if (editCard != null) editCard.Visible = false;  //名片 non porté

        // Bandeau actions en bas du cadre : invite + whisper / remove /
        // couple.
        var bar = new HBoxContainer { Name = "SocialActionBar" };
        bar.AddThemeConstantOverride("separation", 4);
        HudRoot.Pin(bar, 0, 1, 1, 1, 10, -30, -10, -6);
        _panel.AddChild(bar);
        _inviteRow = new HBoxContainer();
        _inviteRow.Visible = false;
        bar.AddChild(_inviteRow);
        _whisperRow = new HBoxContainer();
        _whisperRow.Visible = false;
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
        bar.AddChild(_whisperRow);
        var wBtn = new Button
        {
            Text = UiTextEn.Translate("Whisper"),
            TooltipText = UiTextEn.Translate("Whisper selected friend"),
        };
        wBtn.Pressed += () => RefreshWhisperTarget(force: true);
        bar.AddChild(wBtn);
        var rmBtn = new Button
        {
            Text = UiTextEn.Translate("Remove"),
            TooltipText = UiTextEn.Translate("Remove selected friend"),
        };
        rmBtn.Pressed += RemoveSelected;
        bar.AddChild(rmBtn);
        var coupleBtn = new Button
        {
            Text = "♥",
            TooltipText = UiTextEn.Translate("Couple / wedding window"),
            CustomMinimumSize = new Vector2(30, 0),
        };
        coupleBtn.Pressed += () => CoupleWindow.Instance?.Toggle();
        bar.AddChild(coupleBtn);
        Ui.CeguiToGodot.SkinUnstyled(_panel);
        StyleNative();
    }

    /// <summary>M16 : listes parchemin à texte sombre, boutons gris,
    /// libellés de debug du layout masqués (ref CommunityPanel.png).</summary>
    private void StyleNative()
    {
        if (_panel == null) return;
        var ink = new Color(0.12f, 0.11f, 0.1f);
        foreach (var l in new[] { _friendList, _blockList })
        {
            if (l == null) continue;
            l.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
            var sel = new StyleBoxFlat { BgColor = new Color(0.72f, 0.71f, 0.68f) };
            l.AddThemeStyleboxOverride("selected", sel);
            l.AddThemeStyleboxOverride("selected_focus", sel);
            foreach (var c in new[] { "font_color", "font_hovered_color",
                         "font_selected_color" })
                l.AddThemeColorOverride(c, ink);
        }
        foreach (var b in _panel.FindChildren("*", "Button", true, false)
                     .Cast<Button>())
            if (b.GetParent()?.Name != "TitleBar") EeSkin.Grey(b, 11);
        foreach (var lb in _panel.FindChildren("*", "Label", true, false)
                     .Cast<Label>())
            if (lb.Text.Contains("TabControl") || lb.Text.StartsWith("Root_") || lb.Text == "Community")
                lb.Visible = false;
    }

    /// <summary>Popup nom pour AddFriend(type 1)/Block(type 0) —
    /// remplace le mini-dialogue natif.</summary>
    private void AskName(short type)
    {
        var dlg = new AcceptDialog
        {
            Title = type == 1 ? UiTextEn.Translate("Add Friend") : UiTextEn.Translate("Block Player"),
            OkButtonText = type == 1 ? UiTextEn.Translate("Add") : UiTextEn.Translate("Block"),
        };
        var ed = new LineEdit { PlaceholderText = UiTextEn.Translate("character name…") };
        dlg.AddChild(ed);
        dlg.RegisterTextEnter(ed);
        dlg.Confirmed += () => AddFriend(ed.Text, type);
        dlg.CloseRequested += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered(new Vector2I(280, 90));
        ed.GrabFocus();
    }

    /// <summary>Ami sélectionné dans la liste (charId en métadonnée).</summary>
    private FriendEntry? SelectedFriend()
    {
        int id;
        if (_friendMcl != null)
        {
            if (_friendMcl.RowTag(_friendMcl.SelectedRow) is long lid)
                id = (int)lid;
            else
                return null;
        }
        else
        {
            var sel = _friendList?.GetSelectedItems();
            if (sel == null || sel.Length == 0) return null;
            id = (int)(_friendList!.GetItemMetadata(sel[0]).AsInt64());
        }
        return SocialState.Friends.TryGetValue(id, out var e) ? e : null;
    }

    private void RefreshWhisperTarget(bool force = false)
    {
        var e = SelectedFriend();
        if (e == null && !force) return;
        if (e != null) _whisperTarget = e.Name;
        if (_whisperRow != null) _whisperRow.Visible = e != null;
        if (_whisperEdit != null && e != null)
            _whisperEdit.PlaceholderText = UiTextEn.Translate("to %s1…").Replace("%s1", e.Name);
    }

    private void RemoveSelected()
    {
        if (SelectedFriend() is { } e) RemoveFriend(e.CharId);
    }

    /// <summary>Repli sans uidata : l'ancien panneau simple.</summary>
    private void BuildFallback()
    {
        _panel = new Panel { Name = "SocialPanel" };
        HudRoot.Pin(_panel, 0.5f, 0.5f, 0.5f, 0.5f, -190, -200, 190, 200);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.122f, 0.117f, 0.104f, 0.95f),
            BorderColor = new Color(0.4f, 0.5f, 0.75f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        _panel.Visible = false;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        // Insets manuels (anciennes marges du PanelContainer) + place
        // pour la barre de titre en haut.
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        v.OffsetLeft = 10; v.OffsetRight = -10;
        v.OffsetTop = 30; v.OffsetBottom = -8;
        _panel.AddChild(v);

        _guildLabel = new Label { Text = UiTextEn.Translate("Friend List") };
        _guildLabel.AddThemeColorOverride("font_color",
            new Color(0.55f, 0.9f, 0.55f));
        v.AddChild(_guildLabel);

        _inviteRow = new HBoxContainer();
        _inviteRow.Visible = false;
        v.AddChild(_inviteRow);

        // Liste d'amis.
        v.AddChild(new Label { Text = UiTextEn.Translate("Friends") });
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 200),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _friendList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _friendList.ItemSelected += _ => RefreshWhisperTarget(true);
        scroll.AddChild(_friendList);
        v.AddChild(scroll);
        var rmSel = new Button { Text = UiTextEn.Translate("Remove selected") };
        rmSel.Pressed += RemoveSelected;
        v.AddChild(rmSel);

        // Ligne whisper (cachée tant qu'aucun contact n'est choisi) :
        // envoie un ZoneChannelMsg canal 1 avec la cible —
        // ZoneEntryFlow.Chat.cs (mono-zone : écho 327).
        _whisperRow = new HBoxContainer();
        _whisperRow.Visible = false;
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

        // Ajout d'ami (nom → NCID 12).
        var addBar = new HBoxContainer();
        _addEdit = new LineEdit
        {
            PlaceholderText = UiTextEn.Translate("friend name…"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _addEdit.TextSubmitted += name => AddFriend(name, 1);
        addBar.AddChild(_addEdit);
        var addBtn = new Button { Text = UiTextEn.Translate("Add"), TooltipText = UiTextEn.Translate("Add friend") };
        addBtn.Pressed += () => AddFriend(_addEdit.Text, 1);
        addBar.AddChild(addBtn);
        var blkBtn = new Button
        {
            Text = UiTextEn.Translate("Block"),
            TooltipText = UiTextEn.Translate(
                "Blacklist this name"),
        };
        blkBtn.Pressed += () => AddFriend(_addEdit.Text, 0);
        addBar.AddChild(blkBtn);
        // Vague 2 : accès rapide à la fenêtre couple/mariage
        // (le LoverTab du CommunityWnd natif).
        var coupleBtn = new Button
        {
            Text = "♥",
            TooltipText = UiTextEn.Translate("Couple / wedding window"),
            CustomMinimumSize = new Vector2(30, 0),
        };
        coupleBtn.Pressed += () => CoupleWindow.Instance?.Toggle();
        addBar.AddChild(coupleBtn);
        v.AddChild(addBar);

        // Blacklist.
        v.AddChild(new Label { Text = UiTextEn.Translate("Blocked") });
        _blockList = new ItemList { CustomMinimumSize = new Vector2(0, 60) };
        v.AddChild(_blockList);

        AddChild(_panel);
    }

    /// <summary>Bascule la fenêtre social (action eden_social,
    /// défaut O — dispatchée par SettingsApplier).</summary>
    public void TogglePanel()
    {
        if (_panel != null)
        {
            _panel.Visible = !_panel.Visible;
            if (_panel.Visible) RequestLists();
        }
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } s)
            BindSession(s);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { GD.PrintErr($"[social] {ex}"); }
        }
        MockIfAsked();
    }

    /// <summary><c>--mock-social</c> : ouvre la fenêtre une fois avec
    /// guilde + amis factices (captures hors-ligne).</summary>
    private bool _mocked;
    private void MockIfAsked()
    {
        if (_mocked) return;
        _mocked = true;
        if (!System.Array.Exists(
                OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
                    .ToArray(), a => a == "--mock-social"))
            return;
        SocialState.GuildName = "EdenDev";
        SocialState.GuildLevel = 3;
        SocialState.Friends[200000002] = new FriendEntry(
            200000002, "alice", 3, 20, 2, 0, 2, 5, "EdenDev",
            1, 1, 0, 1);
        SocialState.Friends[200000003] = new FriendEntry(
            200000003, "bob", 1, 14, 9, 0, 1, 3, "",
            0, 0xffff, 0xff, 1);
        SocialState.BlackList[77] = "spammer77";
        if (_panel != null) _panel.Visible = true;
        Refresh();
    }

    public override void _ExitTree()
    {
        SocialState.Changed -= Refresh;
    }

    // ------------------------------------------------------------------
    // Binds monde : trames push de la connexion world (52/61/50/53 +
    // guilde) routées par EdenSession.OnWorld — la lecture physique est
    // mutualisée dans WorldListenLoopAsync (GameScene), il n'y a plus
    // de pompe privée ici (deux lecteurs se voleraient les trames).
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        BindWorldHandlers(s);
        // Le serveur a peut-être déjà poussé les listes avant que la
        // fenêtre existe (entrée monde) → on les re-demande.
        RequestLists();
        GD.Print("[social] binds monde posés + requêtes 10/13 envoyées");
    }

    /// <summary>Handlers <see cref="EdenSession.OnWorld"/> — mêmes
    /// NCIDs que l'ancienne pompe privée : parse sur le thread réseau,
    /// mutation postée dans <see cref="_pending"/>.</summary>
    private void BindWorldHandlers(EdenSession s)
    {
        s.OnWorld(52, r =>
        {
            var m = new WorldUpdateFriendMember();
            m.Deserialize(r);
            _pending.Enqueue(() => SocialState.ApplyFriendChunk(m));
        });
        s.OnWorld(61, r =>
        {
            var m = new WorldUpdateBlackList();
            m.Deserialize(r);
            _pending.Enqueue(() => SocialState.ApplyBlackList(m));
        });
        s.OnWorld(50, r =>
        {
            var m = new WorldFamilyInfo();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                SocialState.ApplyFamilyInfo(m);
                GuildState.ApplyInfo(m);
            });
        });
        s.OnWorld(53, r =>
        {
            var m = new WorldFriendInvitationNotice();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                SocialState.PendingFriendInvite =
                    (m.F1, m.InviterName);
                _hud?.Chat?.AddSystem(
                    UiTextEn.Translate("%s1 invites you as friend.").Replace("%s1", m.InviterName));
            });
        });
        // --- Famille/guilde → GuildState (GuildWindow) ---
        s.OnWorld(46, r =>
        {
            var m = new WorldFamilyMemberUpdate();
            m.Deserialize(r);
            _pending.Enqueue(() => GuildState.ApplyMemberUpdate(m));
        });
        s.OnWorld(47, r =>
        {
            var m = new WorldFamilyOpException();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                GuildState.ApplyOpError(m);
                _hud?.Chat?.AddSystem(
                    UiTextEn.Translate("Guild operation 0x%s1 refused.").Replace("%s1", m.Op.ToString("x2")));
            });
        });
        s.OnWorld(48, r =>
        {
            var m = new WorldFamilyInvitation();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                GuildState.ApplyInvitation(m);
                _hud?.Chat?.AddSystem(
                    UiTextEn.Translate("%s1 invites you to join").Replace("%s1", m.InviterName) + " " +
                    $"<{m.FamilyName}>.");
            });
        });
        s.OnWorld(49, _ =>
        {
            _pending.Enqueue(() =>
            {
                GuildState.ApplyQuit();
                _hud?.Chat?.AddSystem(UiTextEn.Translate("You left the guild."));
            });
        });
        s.OnWorld(51, r =>
        {
            var m = new WorldFamilySchedule();
            m.Deserialize(r);
            _pending.Enqueue(() => GuildState.ApplySchedule(m));
        });
        s.OnWorld(68, r =>
        {
            var m = new WorldFamilyLogUpdate();
            m.Deserialize(r);
            _pending.Enqueue(() => GuildState.ApplyLog(m));
        });
        s.OnWorld(74, r =>
        {
            var m = new WorldFamilyEmblem();
            m.Deserialize(r);
            _pending.Enqueue(() => GuildState.ApplyEmblem(m));
        });
        s.OnWorld(80, r =>
        {
            var m = new WorldFamilyRecruitList();
            m.Deserialize(r);
            _pending.Enqueue(() => GuildState.ApplyRecruitList(m));
        });
    }

    /// <summary>Requêtes monde : liste d'amis (13) + fiche guilde
    /// complète (10 — <b>Value ≠ 0 requis</b> : le serveur ignore 0,
    /// FamilyHandlers.HandleQueryAsync:525 → type-0 + membres type-8 +
    /// agenda 51 + journal 68 ; mitm : 0a00 01).</summary>
    private void RequestLists()
    {
        if (_session?.World?.IsConnected != true) return;
        FireWorld(new WorldQueryFriendInfo());
        FireWorld(new WorldQueryFamilyInfo { Value = 1 });
    }

    private void FireWorld(NetMessage msg)
    {
        var s = _session;
        if (s?.World?.IsConnected != true) return;
        var send = s.SendWorldAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[social] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------------------------------------------------
    // Actions C→S monde
    // ------------------------------------------------------------------

    private void AddFriend(string name, short type)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        _addEdit?.Clear();
        FireWorld(new WorldAddFriend { TargetName = name, FriendType = type });
        _hud?.Chat?.AddSystem(type == 0
            ? UiTextEn.Translate("Blocked «%s1».").Replace("%s1", name)
            : UiTextEn.Translate("Friend request: «%s1».").Replace("%s1", name));
    }

    private void RemoveFriend(int charId)
    {
        FireWorld(new WorldRemoveFriend { CharId = charId });
    }

    // ------------------------------------------------------------------
    // Rendu
    // ------------------------------------------------------------------

    private void Refresh()
    {
        if (_guildLabel != null)
            _guildLabel.Text = SocialState.GuildName.Length > 0
                ? UiTextEn.Translate("Friend List — <%s1>").Replace("%s1", SocialState.GuildName) +
                  (SocialState.GuildLevel > 0
                      ? "  " + UiTextEn.Translate("Lv%s1").Replace("%s1", SocialState.GuildLevel.ToString()) : "")
                : UiTextEn.Translate("Friend List");

        // Invitation d'ami pendante (53) → ligne ✓/× en bandeau.
        if (_inviteRow != null)
        {
            foreach (var c in _inviteRow.GetChildren()) c.QueueFree();
            if (SocialState.PendingFriendInvite is { } inv)
            {
                _inviteRow.AddChild(new Label
                {
                    Text = UiTextEn.Translate("%s1 invites you").Replace("%s1", inv.Name),
                    Modulate = new Color(0.85f, 0.5f, 0.9f),
                });
                var ok = new Button { Text = "✓", TooltipText = UiTextEn.Translate("Accept") };
                ok.Pressed += () => AnswerFriendInvite(inv, 1);
                _inviteRow.AddChild(ok);
                var no = new Button { Text = "×", TooltipText = UiTextEn.Translate("Decline") };
                no.Pressed += () => AnswerFriendInvite(inv, 0);
                _inviteRow.AddChild(no);
            }
            _inviteRow.Visible = SocialState.PendingFriendInvite != null;
        }

        if (_friendList != null)
        {
            _friendList.Clear();
            foreach (var e in SocialState.Friends.Values
                         .OrderByDescending(f => f.Online)
                         .ThenBy(f => f.Name))
            {
                var i = _friendList.AddItem(FriendLine(e));
                _friendList.SetItemMetadata(i, e.CharId);
                _friendList.SetItemCustomFgColor(i, e.Online != 0
                    ? new Color(0.55f, 1f, 0.6f)
                    : new Color(0.55f, 0.55f, 0.6f));
            }
        }
        if (_friendMcl != null)
        {
            _friendMcl.ClearRows();
            foreach (var e in SocialState.Friends.Values
                         .OrderByDescending(f => f.Online)
                         .ThenBy(f => f.Name))
            {
                var row = _friendMcl.AddRow(FriendCols(e), tag:
                    (long)e.CharId);
                var ink = e.Online != 0
                    ? new Color(0.55f, 1f, 0.6f)
                    : new Color(0.55f, 0.55f, 0.6f);
                for (var c = 0; c < 6; c++)
                    _friendMcl.SetCellColor(row, c, ink);
            }
        }
        if (_blockList != null)
        {
            _blockList.Clear();
            foreach (var (id, name) in SocialState.BlackList)
            {
                var i = _blockList.AddItem(name);
                _blockList.SetItemMetadata(i, id);
                _blockList.SetItemTooltip(i, "Double-click to unblock");
                _blockList.SetItemCustomFgColor(i,
                    new Color(1f, 0.6f, 0.6f));
            }
        }
        if (_blockMcl != null)
        {
            _blockMcl.ClearRows();
            foreach (var (id, name) in SocialState.BlackList)
            {
                var row = _blockMcl.AddRow([name],
                    tag: (long)id);
                _blockMcl.SetCellColor(row, 0, new Color(1f, 0.6f, 0.6f));
            }
        }
    }

    /// <summary>Ligne texte d'un ami : nom, Lv, classe, lieu
    /// (t_node) — les colonnes natives (分流/角色名稱/等級/職業/
    /// 所在地點) aplaties, ItemList étant mono-colonne.</summary>
    private static string FriendLine(FriendEntry e)
    {
        var zone = e.NodeId > 0
            ? TextResolver.NodeName(e.NodeId) ?? $"S{e.NodeId:D3}" : "";
        var cls = e.ClassId > 0
            ? TextResolver.ClassName(e.ClassId) ?? "" : "";
        var rel = e.FriendType switch
        {
            3 => "",
            1 => " " + UiTextEn.Translate("(invited)"),
            2 => " " + UiTextEn.Translate("(invites you)"),
            _ => "",
        };
        return $"{(e.Name.Length > 0 ? e.Name : $"#{e.CharId}")}" +
               (e.Level > 0 ? $"  Lv{e.Level}" : "") +
               (cls.Length > 0 ? $"  {cls}" : "") +
               (zone.Length > 0 ? $"  {zone}" : "") + rel;
    }

    /// <summary>Cellules MCL natives d'un ami : 分流/角色名稱/等級/
    /// 職業/所在地點/名片 (canal, nom, niveau, classe, lieu, carte).</summary>
    private static string[] FriendCols(FriendEntry e)
    {
        var zone = e.NodeId > 0
            ? TextResolver.NodeName(e.NodeId) ?? $"S{e.NodeId:D3}" : "";
        var cls = e.ClassId > 0
            ? TextResolver.ClassName(e.ClassId) ?? "" : "";
        var chan = e.ServerId != 0xffff ? $"{e.ServerId}" : "";
        return [chan, e.Name.Length > 0 ? e.Name : $"#{e.CharId}",
            e.Level > 0 ? $"{e.Level}" : "", cls, zone, ""];
    }

    /// <summary>Envoie le whisper saisi (zone 34, canal 1 peer) à la
    /// cible choisie — puis referme la ligne.</summary>
    private void SendWhisper(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || _whisperTarget.Length == 0) return;
        var s = _session;
        if (s?.Zone?.IsConnected != true) return;
        var send = s.SendZoneAsync(new ZoneChannelMsg
        {
            Channel = 1,                      // u8 — peer/whisper
            Target = _whisperTarget,          // str — destinataire
            Message = text,                   // str — texte
        }, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[social] whisper : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
        _hud?.Chat?.AddLocalEcho(1, UiTextEn.Translate("to %s1: %s2").Replace("%s1", _whisperTarget).Replace("%s2", text));
        _whisperEdit?.Clear();
        if (_whisperRow != null) _whisperRow.Visible = false;
    }

    /// <summary>Répond à l'invitation d'ami (53) — monde 14
    /// `[i32 inviter][s16 accept]`.</summary>
    private void AnswerFriendInvite((int Id, string Name) inv, short accept)
    {
        FireWorld(new WorldFriendInvitationAnswer
        {
            InviterId = inv.Id, Accept = accept,
        });
        SocialState.PendingFriendInvite = null;
        _hud?.Chat?.AddSystem(accept != 0
            ? UiTextEn.Translate("Friend «%s1» accepted.").Replace("%s1", inv.Name)
            : UiTextEn.Translate("Friend invite declined."));
        Refresh();
    }
}
