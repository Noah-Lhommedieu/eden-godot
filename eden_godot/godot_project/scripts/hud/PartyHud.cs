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
/// HUD groupe (S9) — liste des membres sous les vitals haut-gauche
/// (nom + niveau + barre HP + badge leader), boutons d'invite et popup
/// d'invitation entrante.
///
/// Réseau (binds paresseux <see cref="SessionCarry.Session"/> — même
/// limite documentée que <see cref="HudNetBridge"/> : en --live direct
/// la session reste privée à GameScene → HUD non alimenté, mock ok) :
///   - S→C <b>324</b> roster complet → <see cref="PartyState"/> ;
///     <b>325</b> membre rejoint ; <b>317</b> équipe quittée ;
///     <b>318</b> invite entrante (popup) ; <b>319</b> résultat → chat ;
///     <b>381</b> champ team → chat.
///   - C→S : <b>29</b> TeamInvite (charId résolu via la cible courante
///     <see cref="TargetFrame.TargetHandle"/> ou le registre d'entités
///     — handle=charId pour les joueurs), <b>30</b> InviteAnswer,
///     <b>28</b> TeamLeave, <b>31</b> InviteCancel.
///
/// HP : le wire 324 ne porte qu'un i32 HP courant — la barre est
/// relative au max observé par membre (<see cref="PartyState.MaxHpSeen"/>),
/// documenté dans le plan du lot.
/// </summary>
public partial class PartyHud : Control
{
    private PanelContainer? _panel;
    private VBoxContainer? _rows;
    private PopupMenu? _menu;
    private TeamMember? _menuMember;
    private PanelContainer? _invitePopup;
    private Label? _inviteLabel;
    private EdenSession? _session;
    private EntityRegistry? _registry;
    private readonly ConcurrentQueue<Action> _pending = new();
    private HudRoot? _hud;

    public void Build(HudRoot hud)
    {
        _hud = hud;
        _panel = new PanelContainer { Name = "PartyPanel" };
        // Cadres natifs TeamWndFrame (même habillage que les vitals)
        // empilés sous la grille 1-12 des vitals, sans fond.
        HudRoot.Pin(_panel, 0, 0, 0, 0, 4, 132, 274, 132);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _panel.Visible = false;
        _rows = new VBoxContainer { Name = "Rows" };
        _rows.AddThemeConstantOverride("separation", 0);
        _panel.AddChild(_rows);

        // Clic droit sur un membre : actions contextuelles.
        _menu = new PopupMenu { Name = "PartyMenu" };
        _menu.IdPressed += OnMenu;
        AddChild(_menu);

        AddChild(_panel);

        // Popup d'invitation entrante (318).
        _invitePopup = new PanelContainer { Name = "PartyInvitePopup" };
        HudRoot.Pin(_invitePopup, 0.5f, 0.35f, 0.5f, 0.35f,
            -110, -40, 110, 40);
        _invitePopup.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.155f, 0.149f, 0.133f, 0.95f),
            BorderColor = new Color(0.5f, 0.6f, 0.9f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var pv = new VBoxContainer();
        _inviteLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        pv.AddChild(_inviteLabel);
        var pb = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        var yes = new Button { Text = UiTextEn.Translate("Accept") };
        yes.Pressed += () => AnswerInvite(true);
        var no = new Button { Text = UiTextEn.Translate("Decline") };
        no.Pressed += () => AnswerInvite(false);
        pb.AddChild(yes); pb.AddChild(no);
        pv.AddChild(pb);
        _invitePopup.AddChild(pv);
        _invitePopup.Visible = false;
        AddChild(_invitePopup);

        PartyState.Changed += Refresh;
        // Vague 2 : icônes de statut des membres (372 → EntityBuffs)
        // — repeint les lignes à chaque snapshot reçu.
        EntityBuffs.Changed += Refresh;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    // ------------------------------------------------------------------
    // Binds NCID (thread réseau → file principale)
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(324, r =>
        {
            var m = new ZoneTeamAllMembersInfo();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                PartyState.ApplyAll(m);
                var selfId = SessionCarry.Character?.Id ?? 0;
                foreach (var e in m.Members)
                    if (e.CharId == selfId && e.GuildName.Length > 0)
                        SocialState.ApplyTeamGuild(e.GuildName);
            });
        });
        s.OnZone(325, r =>
        {
            var m = new ZoneTeamNewMemberInfo();
            m.Deserialize(r);
            _pending.Enqueue(() => PartyState.ApplyNew(m));
        });
        s.OnZone(317, r =>
        {
            var m = new ZoneTeamLeaved();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                PartyState.ApplyLeaved();
                _hud?.Chat?.AddSystem(UiTextEn.Translate("You left the party."));
            });
        });
        s.OnZone(318, r =>
        {
            var m = new ZoneTeamInviteNotice();
            m.Deserialize(r);
            _pending.Enqueue(() => PartyState.ApplyInviteNotice(m));
        });
        s.OnZone(319, r =>
        {
            var m = new ZoneTeamInviteResult();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                PartyState.OutgoingInviteName = "";
                var txt = (m.F2.Length > 0 ? m.F2 : m.F1);
                if (txt.Length > 0)
                    _hud?.Chat?.AddSystem(UiTextEn.Translate($"Party invite: {txt}"));
            });
        });
        s.OnZone(381, r =>
        {
            var m = new ZoneTeamInfo();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                PartyState.ApplyTeamInfo(m);
                var what = m.Kind switch
                {
                    9 => "loot rule",
                    10 => "roll rule",
                    _ => $"field {m.Kind}",
                };
                _hud?.Chat?.AddSystem(UiTextEn.Translate($"Party {what} = {m.Value}"));
            });
        });
        GD.Print("[party] binds 317/318/319/324/325/381 posés");
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
            catch (Exception ex) { GD.PrintErr($"[party] {ex}"); }
        }
        // Popup invite : reflète PendingInvite.
        if (HudPrefs.NoParty && PartyState.PendingInvite.HasValue)
            AnswerInvite(false);
        var inv = PartyState.PendingInvite;
        if (_invitePopup != null && _invitePopup.Visible != inv.HasValue)
            _invitePopup.Visible = inv.HasValue;
        if (inv.HasValue && _inviteLabel != null)
            _inviteLabel.Text = UiTextEn.Translate($"{inv.Value.Name} invites you\nto a party");
        // Capture différée d'une frame : QueueFree/re-layout sont
        // asynchrones → shooter au tick suivant la mutation.
        if (_pendingShot != null)
        {
            var suf = _pendingShot; _pendingShot = null;
            Shot(suf);
        }
        // HP des membres : le 280 alimente déjà le registre par
        // handle (=charId joueur) — recopie sans re-parser le wire.
        _hpT += delta;
        if (_hpT >= 0.5) { _hpT = 0; SyncMemberVitals(); }
        MockIfAsked();
        TickPartyTest(delta);
    }

    private double _hpT;

    /// <summary>Recopie les HP/MaxHp du registre d'entités (280 →
    /// <see cref="EntityRegistry.HandleUpdateAttr"/>) dans le roster —
    /// barre membre vivante pour les coéquipiers visibles.</summary>
    private void SyncMemberVitals()
    {
        if (_registry == null || !PartyState.InTeam) return;
        var dirty = false;
        foreach (var m in PartyState.Members.Values)
        {
            if (!_registry.Entities.TryGetValue(m.CharId, out var e) ||
                !GodotObject.IsInstanceValid(e) || e.Hp == m.Hp)
                continue;
            m.Hp = (int)e.Hp;
            if (e.MaxHp > PartyState.MaxHpSeen
                    .GetValueOrDefault(m.CharId))
                PartyState.MaxHpSeen[m.CharId] = (int)e.MaxHp;
            dirty = true;
        }
        if (dirty) Refresh();
    }

    /// <summary><c>--mock-party</c> : remplit le roster une fois avec
    /// 3 membres factices (captures hors-ligne).</summary>
    private bool _mocked;
    private void MockIfAsked()
    {
        if (_mocked) return;
        _mocked = true;
        if (!System.Array.Exists(
                OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
                    .ToArray(), a => a == "--mock-party"))
            return;
        var m = new ZoneTeamAllMembersInfo();
        m.Members.Add(new TeamMember
        {
            Slot = 0, Flags = 1, CharId = 200000001, CharName = "test1",
            Level = 12, Hp = 860, Online = 1, ClassId = 2, MapId = 2,
            GuildName = "EdenDev",
        });
        m.Members.Add(new TeamMember
        {
            Slot = 1, Flags = 0, CharId = 200000002, CharName = "mate2",
            Level = 11, Hp = 430, Online = 1, ClassId = 5, MapId = 2,
        });
        m.Members.Add(new TeamMember
        {
            Slot = 2, Flags = 0, CharId = 200000003, CharName = "mate3",
            Level = 15, Hp = 0, Online = 0, ClassId = 1, MapId = 9,
        });
        PartyState.MaxHpSeen[200000001u] = 860;
        PartyState.MaxHpSeen[200000002u] = 1200;
        PartyState.MaxHpSeen[200000003u] = 1500;
        PartyState.ApplyAll(m);
    }

    public override void _ExitTree()
    {
        PartyState.Changed -= Refresh;
        EntityBuffs.Changed -= Refresh;
    }

    // ------------------------------------------------------------------
    // Actions C→S
    // ------------------------------------------------------------------

    private void Send(NetMessage msg)
    {
        if (_session?.Zone?.IsConnected != true) return;
        var send = _session.SendZoneAsync(msg, CancellationToken.None);
        _ = send.ContinueWith(t =>
            GD.PrintErr($"[party] send {msg.Name} : {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Invite la cible courante (target frame) si c'est un
    /// joueur — kind 0 / entityId 0 / handle=charId.</summary>
    private void InviteCurrentTarget()
    {
        var handle = _hud?.Target?.TargetHandle ?? 0;
        if (handle == 0)
        {
            _hud?.Chat?.AddSystem(UiTextEn.Translate("No target to invite."));
            return;
        }
        var name = ResolvePlayerName(handle) ?? $"#{handle}";
        SendInvite(handle, name);
    }

    private void SendInvite(uint charId, string name)
    {
        PartyState.OutgoingInviteName = name;
        // Level/ClassId informatifs — 0 si inconnus (toléré : le
        // serveur n'utilise que CharId).
        Send(new ZoneTeamInvite
        {
            CharId = charId,
            PlayerName = name,
            Level = 0,
            ClassId = 0,
            Flags = 0,
        });
        _hud?.Chat?.AddSystem(UiTextEn.Translate($"Party invite sent to {name}."));
    }

    private void AnswerInvite(bool accept)
    {
        var inv = PartyState.PendingInvite;
        PartyState.PendingInvite = null;
        if (_invitePopup != null) _invitePopup.Visible = false;
        if (!inv.HasValue) return;
        Send(new ZoneTeamInviteAnswer
        {
            ReplyType = (short)(accept ? 1 : 0),  // +0x12 natif : 1=accept
            InviterId = inv.Value.InviterId,      // +0x14 : charId inviteur
            Accepted = (byte)(accept ? 1 : 0),    // +0x18 : bool natif
            Tail = 0,
        });
        _hud?.Chat?.AddSystem(accept
            ? UiTextEn.Translate($"Accepted {inv.Value.Name}'s invite.")
            : UiTextEn.Translate($"Declined {inv.Value.Name}'s invite."));
    }

    private void LeaveTeam()
    {
        if (!PartyState.InTeam) return;
        var selfId = (uint)(SessionCarry.Character?.Id ?? 0);
        Send(new ZoneTeamLeave { CharId = selfId });
        // Le 317 serveur confirme et vide le roster — UI optimiste.
        PartyState.ApplyLeaved();
    }

    // ------------------------------------------------------------------
    // Rendu
    // ------------------------------------------------------------------

    /// <summary>Peint un cadre natif par membre (self inclus — le 324
    /// serveur liste tous les membres).</summary>
    private void Refresh()
    {
        if (_rows == null || _panel == null) return;
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        var members = PartyState.Members.Values
            .OrderBy(m => m.Slot).ToList();
        foreach (var m in members)
            if (MakeRow(m) is { } row) _rows.AddChild(row);
        _panel.Visible = PartyState.InTeam;
    }

    private const int FrameH = 92;

    /// <summary>Un membre = <c>TeamWndFrame.layout</c> : niveau,
    /// couronne chef, nom, HP (relatif au max observé), MP pleine
    /// (non transmise), 10 icônes d'enchants (372). Grisé hors ligne.
    /// Clic droit → menu Whisper / Kick / Leave.</summary>
    private Control? MakeRow(TeamMember m)
    {
        var host = new Control
        {
            CustomMinimumSize = new Vector2(270, FrameH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        var frame = _hud?.Build("TeamWndFrame.layout");
        if (frame == null) return null;
        host.AddChild(frame);
        HudRoot.Pin(frame, 0, 0, 0, 0, 0, 0, 270, 99);
        var name = m.CharName.Length > 0 ? m.CharName : $"#{m.CharId}";
        if (TopLeftVitals.Find<Label>(frame, "NameText") is { } nl)
        {
            nl.Text = name;
            EeSkin.Shadowed(nl, 12);
            nl.HorizontalAlignment = HorizontalAlignment.Center;
        }
        if (TopLeftVitals.Find<Label>(frame, "LVText") is { } ll)
        {
            ll.Text = $"{m.Level}";
            EeSkin.Shadowed(ll, 12, Color.Color8(225, 225, 220));
        }
        if (TopLeftVitals.Find<Label>(frame, "ClassLvText") is { } cl)
            cl.Text = "";
        if (TopLeftVitals.Find<Control>(frame, "LeaderIcon") is { } li)
            li.Visible = (m.Flags & 1) != 0;
        if (TopLeftVitals.Find<Control>(frame, "PetBG") is { } pet)
            pet.Visible = false;
        // PositionIcon natif : visible quand la position du membre est
        // connue (392) — clic gauche = ping radar 86 de sa position,
        // clic droit = menu du membre.
        if (TopLeftVitals.Find<Control>(frame, "PositionIcon") is { } pi)
        {
            var hasPos = PartyState.Positions
                .ContainsKey((int)m.CharId);
            pi.Visible = hasPos;
            if (hasPos)
            {
                pi.TooltipText = UiTextEn.Translate("Ping member position");
                pi.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton mb || !mb.Pressed)
                        return;
                    if (mb.ButtonIndex == MouseButton.Left)
                        PingMemberPosition(m);
                    else if (mb.ButtonIndex == MouseButton.Right)
                        OpenMenu(m, mb.GlobalPosition);
                };
            }
        }
        var maxHp = PartyState.MaxHpSeen.GetValueOrDefault(m.CharId, m.Hp);
        if (TopLeftVitals.Find<ProgressBar>(frame, "HpBar") is { } hp)
        {
            hp.AddThemeStyleboxOverride("fill", EeSkin.HpFill);
            hp.Value = maxHp > 0 ? Math.Clamp((double)m.Hp / maxHp, 0, 1) : 0;
            if (TopLeftVitals.Find<Label>(hp, "text") is { } t)
            {
                t.Text = maxHp > 0 ? $"{m.Hp}/{maxHp}" : "";
                EeSkin.Shadowed(t, 10);
                t.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
        if (TopLeftVitals.Find<ProgressBar>(frame, "MpBar") is { } mp)
        {
            mp.AddThemeStyleboxOverride("fill", EeSkin.MpFill);
            mp.Value = 1;
            if (TopLeftVitals.Find<Label>(mp, "text") is { } t) t.Text = "";
        }
        // Enchants du membre (372 → EntityBuffs) sur buff1-10 natifs.
        var buffs = EntityBuffs.Snapshot((int)m.CharId);
        for (var i = 0; i < 10; i++)
        {
            if (TopLeftVitals.Find<Control>(frame, $"buff{i + 1}") is not
                { } slot) continue;
            if (TopLeftVitals.Find<Label>(slot, "Text") is { } bt)
                bt.Text = "";
            var b = i < buffs.Count ? buffs[i] : null;
            slot.Visible = b != null;
            if (b == null) continue;
            var code = Data.Tables.EnchantTable.Get(b.EffectId)?.Code;
            var icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            if (code is { Length: > 0 })
                icon.Texture = AssetCatalog.GetTexture($"UI/skillicon/{code}.png")
                    ?? AssetCatalog.GetTexture($"UI/itemicon/{code}.png")
                    ?? AssetCatalog.GetTexture($"UI/uiicon/{code}.png");
            if (Data.Tables.EnchantTable.Get(b.EffectId) is { IsBuff: false })
                icon.Modulate = new Color(1f, 0.55f, 0.55f);
            slot.TooltipText =
                $"{Data.Tables.EnchantTable.NameOf(b.EffectId)}  " +
                $"({(int)b.Remaining / 60}:{(int)b.Remaining % 60:D2})";
            slot.AddChild(icon);
        }
        if (m.Online == 0) host.Modulate = new Color(1, 1, 1, 0.45f);
        host.TooltipText = m.GuildName.Length > 0
            ? UiTextEn.Translate($"{name}  <{m.GuildName}>  map S{m.MapId:D3}")
            : UiTextEn.Translate($"{name}  map S{m.MapId:D3}");
        host.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed &&
                mb.ButtonIndex == MouseButton.Right)
                OpenMenu(m, mb.GlobalPosition);
        };
        return host;
    }

    private const int MenuWhisper = 0, MenuKick = 1, MenuLeave = 2,
        MenuInvite = 3, MenuPromote = 4;

    /// <summary>Menu contextuel d'un membre (hud-interfaces.md) :
    /// message privé, exclusion si on est chef, quitter (soi).</summary>
    private void OpenMenu(TeamMember m, Vector2 at)
    {
        if (_menu == null) return;
        _menuMember = m;
        _menu.Clear();
        var selfId = (uint)(SessionCarry.Character?.Id ?? 0);
        var self = m.CharId == selfId;
        if (!self)
        {
            _menu.AddItem(UiTextEn.Translate("Whisper"), MenuWhisper);
            if (PartyState.LeaderId == selfId && selfId != 0)
            {
                _menu.AddItem(UiTextEn.Translate("Promote leader"), MenuPromote);
                _menu.AddItem(UiTextEn.Translate("Kick"), MenuKick);
            }
        }
        else
        {
            _menu.AddItem(UiTextEn.Translate("Invite target"), MenuInvite);
            _menu.AddItem(UiTextEn.Translate("Leave party"), MenuLeave);
        }
        _menu.Position = (Vector2I)at;
        _menu.Popup();
    }

    private void OnMenu(long id)
    {
        var m = _menuMember;
        if (m == null) return;
        switch ((int)id)
        {
            case MenuWhisper:
                _hud?.Chat?.StartWhisper(m.CharName);
                break;
            case MenuKick:
                // Natif : TeamLeave avec l'id du membre = exclusion.
                Send(new ZoneTeamLeave { CharId = m.CharId });
                break;
            case MenuPromote:
                Send(new ZoneTeamChangeLeader
                    { CharId = (int)m.CharId });
                break;
            case MenuLeave:
                LeaveTeam();
                break;
            case MenuInvite:
                InviteCurrentTarget();
                break;
        }
    }

    // ------------------------------------------------------------------
    // Résolution de nom → charId via le registre d'entités
    // ------------------------------------------------------------------

    private string? ResolvePlayerName(uint handle)
    {
        if (_registry != null &&
            _registry.Entities.TryGetValue(handle, out var e) &&
            GodotObject.IsInstanceValid(e) && e.Kind == 0 &&
            e.EntityId == 0)
            return e.DisplayName;
        return null;
    }

    /// <summary>Clic sur <c>PositionIcon</c> : ping radar 86 à la
    /// dernière position connue du membre (392). Rebroadcastée en 387
    /// aux membres du même node.</summary>
    private void PingMemberPosition(TeamMember m)
    {
        if (!PartyState.Positions.TryGetValue((int)m.CharId, out var p))
        {
            _hud?.Chat?.AddSystem(UiTextEn.Translate("Member position unknown."));
            return;
        }
        Send(new ZoneTeamRadarAttention
            { Kind = 1, X = (short)p.X, Z = (short)p.Y });
        _hud?.Chat?.AddSystem(
            UiTextEn.Translate($"Ping: {m.CharName} @{(int)p.X},{(int)p.Y}"));
    }

    // ------------------------------------------------------------------
    // Sonde --party-test : cycle équipe complet sur paquets réels.
    //   --party-test --party-peer=<charId> [--party-shot=<base>]
    // invite → roster (shot) → ping 86→387 → kick (menu réel) →
    // re-invite → promote (menu réel, shot) → leave → 317.
    // ------------------------------------------------------------------

    private int _testStep = -1, _testPeer, _pingCount;
    private double _testT;
    private string _testShot = "";
    private string? _pendingShot;
    private bool _testInit;

    private void Next(int step) { _testStep = step; _testT = 0; }

    private void Fail(string why)
    {
        GD.PrintErr($"[party-test] FAIL step{_testStep}: {why}");
        _testStep = -1;
    }

    /// <summary>Exerce le vrai chemin menu (ouverture popup + signal
    /// id_pressed) — <paramref name="charId"/> 0 = soi.</summary>
    private bool MenuAct(uint charId, int menuId)
    {
        var cid = charId > 0 ? charId
            : (uint)(SessionCarry.Character?.Id ?? 0);
        if (!PartyState.Members.TryGetValue(cid, out var m)) return false;
        OpenMenu(m, Vector2.Zero);
        _menu?.EmitSignal(PopupMenu.SignalName.IdPressed, menuId);
        _menu?.Hide();
        return true;
    }

    private void Shot(string suffix)
    {
        if (_testShot.Length == 0) return;
        RenderingServer.ForceDraw();
        var img = GetViewport().GetTexture()?.GetImage();
        var root = System.IO.Path.IsPathRooted(_testShot)
            ? _testShot : System.IO.Path.GetFullPath(_testShot);
        var err = img?.SavePng($"{root}_{suffix}.png")
            ?? Error.FileNotFound;
        GD.Print($"[party-shot] {root}_{suffix}.png err={err}");
    }

    private void TickPartyTest(double delta)
    {
        if (_testStep < 0 && _testInit) return;
        if (!_testInit)
        {
            _testInit = true;
            _testStep = -1;
            var args = OS.GetCmdlineArgs()
                .Concat(OS.GetCmdlineUserArgs()).ToArray();
            if (!args.Contains("--party-test")) return;
            var peer = args.FirstOrDefault(
                a => a.StartsWith("--party-peer="));
            _testShot = args.FirstOrDefault(
                a => a.StartsWith("--party-shot="))
                ?.Split('=', 2)[1] ?? "";
            if (peer == null ||
                !int.TryParse(peer.Split('=', 2)[1], out _testPeer))
            {
                GD.PrintErr("[party-test] --party-peer=<charId> requis");
                return;
            }
            GD.Print($"[party-test] peer={_testPeer} shot='{_testShot}'");
            _testStep = 0;
        }
        _testT += delta;
        switch (_testStep)
        {
            case 0: // session établie + 4 s → invite
                if (_session == null || _testT < 4) return;
                SendInvite((uint)_testPeer, $"peer{_testPeer}");
                Next(1);
                break;
            case 1: // roster reçu (324/325)
                if (PartyState.Members.Count >= 2)
                { _pendingShot = "roster"; Next(2); }
                else if (_testT > 15) Fail("roster jamais reçu");
                break;
            case 2: // ping radar sur notre position
                if (_testT < 1) return;
                var sp = _hud?.Minimap?.SelfPos ?? Vector2.Zero;
                _pingCount = PartyState.RadarPingCount;
                Send(new ZoneTeamRadarAttention
                    { Kind = 1, X = (short)sp.X, Z = (short)sp.Y });
                Next(3);
                break;
            case 3: // 387 attendu (echo serveur)
                if (PartyState.RadarPingCount > _pingCount) Next(4);
                else if (_testT > 8) Fail("387 jamais reçu");
                break;
            case 4: // kick du peer via le menu réel (28 ≠self)
                if (_testT < 1) return;
                if (!MenuAct((uint)_testPeer, MenuKick))
                { Fail("peer absent du roster"); return; }
                Next(5);
                break;
            case 5: // roster retombé à 1 (324 post-kick)
                if (PartyState.Members.Count <= 1) Next(6);
                else if (_testT > 8) Fail("kick sans effet");
                break;
            case 6: // re-invite
                if (_testT < 0.5) return;
                SendInvite((uint)_testPeer, $"peer{_testPeer}");
                Next(7);
                break;
            case 7: // roster 2 à nouveau
                if (PartyState.Members.Count >= 2) Next(8);
                else if (_testT > 15) Fail("re-invite sans roster");
                break;
            case 8: // promote le peer via le menu réel (90)
                if (_testT < 1) return;
                if (!MenuAct((uint)_testPeer, MenuPromote))
                { Fail("peer absent pour promote"); return; }
                Next(9);
                break;
            case 9: // lead transféré (324 refresh flags)
                if (PartyState.LeaderId == (uint)_testPeer)
                { _pendingShot = "leader"; Next(10); }
                else if (_testT > 8) Fail("lead non transféré");
                break;
            case 10: // quitter soi-même (28 self)
                if (_testT < 1) return;
                MenuAct(0, MenuLeave);
                Next(11);
                break;
            case 11: // 317 reçu → équipe vide
                if (!PartyState.InTeam)
                { GD.Print("[party-test] done"); _testStep = -1; }
                else if (_testT > 8) Fail("leave sans 317");
                break;
        }
    }
}
