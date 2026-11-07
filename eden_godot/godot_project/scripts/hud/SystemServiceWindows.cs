using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

// Fenêtres légères des systèmes couverts par la vague complétude —
// chaque fenêtre lit les paquets parsés via MiscPackets.Received et
// envoie les senders prouvés quand la sémantique est connue.

/// <summary>Base commune : panneau centré déplaçable + titre +
/// zone de texte + bouton fermer.</summary>
public abstract partial class ServicePanel : Control
{
    protected Control? Panel;
    protected Label? Body;

    /// <summary>Construit le panneau standard (à appeler dans
    /// _Ready). Si <paramref name="layout"/> est fourni et que le
    /// builder CEGUI le résout, la fenêtre native (chrome + titre +
    /// bouton × + drag) remplace le panneau générique — repli
    /// identique à avant sinon.</summary>
    protected void BuildPanel(string name, string title, int w = 420,
        int h = 260, string? layout = null)
    {
        var built = layout == null ? null
            : HudExtensions.BuildNativeWindow(this, layout, name,
                new Vector2(w, h));
        if (built != null)
        {
            Panel = built;
            built.Name = name;
        }
        else
        {
            var p = new Panel { Name = name };
            p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            p.Position = new Vector2(-w / 2, -h / 2);
            p.Size = new Vector2(w, h);
            p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.07f, 0.06f, 0.09f, 0.97f),
                BorderColor = new Color(0.537f, 0.517f, 0.46f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            AddChild(p);
            Panel = p;
            DragHelper.EnsureTitleBar(p, title);
            DragHelper.Persist(p, name);
            var close = new Button { Text = UiTextEn.Translate("Close") };
            close.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
            close.Position = new Vector2(-45, -34);
            close.Size = new Vector2(90, 26);
            close.Pressed += () => Visible = false;
            p.AddChild(close);
        }

        Body = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        Body.Position = new Vector2(14, 40);
        Body.Size = new Vector2(
            Mathf.Max(Panel!.Size.X - 28, 60),
            Mathf.Max(Panel.Size.Y - 90, 30));
        Panel.AddChild(Body);

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    protected abstract void OnPacket(NetMessage m);

    protected void Show(string text)
    {
        if (Body != null) Body.Text = text;
        Visible = true;
    }

    protected void Chat(string t) =>
        (GetParent() as HudRoot)?.AddChat(7, "", t);

    protected static void Send(NetMessage m)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(m,
                System.Threading.CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[svc] send : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}

/// <summary>Guerre de territoire — lot C4 (priorité basse).
/// Layout natif <c>familybattlejoinwnd.layout</c>
/// (« Root/FamilyBattleJoinWnd » : liste ScrollablePane
/// <c>TerritoryList/ListPane</c>, <c>TimeTableBtn</c> → calendrier
/// <c>territoryschedulewnd.layout</c>, <c>RankBtn</c>).
///
/// Wire (preuves : <c>ZoneEntryFlow.Territory.cs</c>,
/// <c>FamilyBattlePackets.TerritoryQuery</c>) :
///   - ouverture → <b>189</b> C→S <c>[]</c> → <b>515</b>
///     <see cref="ZoneClientTerritoryQuery"/> (8 vecteurs : ids,
///     créneaux, propriétaires, noms, niveaux, time, états, flags) ;
///   - double-clic sur un territoire → <b>190</b>
///     <c>[i32 terrId][s16 type]</c> (0 = guerre, 1 = family battle) ;
///     terrId −1 = quitter ;
///   - <b>516</b> <c>[s16 type 0-2][u32 remaining][i32 ownFamily]
///     [u16 camp]</c> = compte à rebours de la guerre en cours.</summary>
public partial class TerritoryWindow : ServicePanel
{
    private readonly List<ZoneClientTerritoryQuery.Entry> _entries =
        new();
    private readonly List<ZoneClientTerritoryQuery.Schedule>
        _schedules = new();
    private ItemList? _list;
    private Control? _schedWnd;
    private ItemList? _schedList;

    public override void _Ready()
    {
        BuildPanel("__territory", UiTextEn.Translate("Territory war"),
            w: 640, h: 480, layout: "familybattlejoinwnd.layout");
        if (Panel == null) return;
        // familybattlejoinwnd est un DefaultWindow : sans barre de titre
        // native on pose la barre commune (drag + croix).
        DragHelper.EnsureTitleBar(Panel,
            UiTextEn.Translate("Territory war"));
        // La barre de titre commune recouvre les 30 px du haut où le
        // layout place ses libellés/boutons : on agrandit vers le haut.
        Panel.OffsetTop -= 30;
        foreach (var ch in Panel.GetChildren())
            if (ch is Control cc && cc.Name.ToString().StartsWith("Root"))
                cc.OffsetTop += 30;
        // Gabarits « x… » du layout remplacés par des libellés réels.
        SetLabel("MapNameLab", "Territoires de guerre");
        SetLabel("FamilyNameLab", "Guilde");
        SetLabel("FamilyName", "—");
        SetLabel("IntroText",
            "Double-cliquez sur un territoire pour vous inscrire à la " +
            "guerre.");
        var pane = FindLeaf<Control>(Panel, "ListPane");
        if (pane != null)
        {
            _list = new ItemList
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                MaxTextLines = 2,
            };
            _list.AddThemeFontSizeOverride("font_size", 11);
            _list.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            pane.AddChild(_list);
            _list.ItemActivated += JoinSelected;
        }
        if (FindLeaf<BaseButton>(Panel, "TimeTableBtn") is { } tb)
            tb.Pressed += ShowSchedule;
        FillList();
    }

    /// <summary>Ouverture live : affiche + requête 189.</summary>
    public void Open()
    {
        Visible = true;
        if (Panel != null) Panel.Visible = true;
        Send(new ZoneServerTerritoryQuery());
    }

    private void SetLabel(string leaf, string text)
    {
        if (Panel == null) return;
        if (FindLeaf<Label>(Panel, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(Panel, leaf) is { } r)
            r.Text = text;
    }

    /// <summary>Contenu factice pour capture (--hud-open hors
    /// session) — les vraies lignes viennent du 515.</summary>
    public void EnsureDemo()
    {
        if (_entries.Count == 0)
            _entries.Add(new ZoneClientTerritoryQuery.Entry(1, 0,
                UiTextEn.Translate("Crystal Utopia"), 0, 0, 0, 0));
        FillList();
        Visible = true;
        if (Panel != null) Panel.Visible = true;
    }

    private void JoinSelected(long index)
    {
        var i = (int)index;
        if (i < 0 || i >= _entries.Count) return;
        Send(new ZoneServerTerritoryJoin
            { Field1 = _entries[i].Id, Field2 = 0 });
    }

    private void FillList()
    {
        if (_list == null) return;
        _list.Clear();
        foreach (var e in _entries)
        {
            // Phase serveur : 0 aucune, 1 préparation (15 min avant),
            // 2 guerre en cours.
            var owner = e.OwnFamilyId != 0
                ? $"guilde n°{e.OwnFamilyId}" : "libre";
            var state = e.State switch
            {
                1 => "préparation",
                2 => "guerre en cours",
                _ => "paix",
            };
            _list.AddItem(e.OwnFamilyId != 0
                ? $"{TerritoryName(e.Name)} — {owner} — {state}"
                : $"{TerritoryName(e.Name)} — {state}");
        }
        if (_entries.Count == 0)
            _list.AddItem("Aucune donnée de territoire.");
    }

    /// <summary>TimeTableBtn → calendrier des guerres
    /// (<c>territoryschedulewnd</c>, ListPane remplie des créneaux
    /// du 515 vec2).</summary>
    private void ShowSchedule()
    {
        if (_schedWnd == null)
        {
            _schedWnd = HudExtensions.FindHud()
                ?.Build("territoryschedulewnd.layout");
            if (_schedWnd == null) return;
            _schedWnd.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            Panel?.AddChild(_schedWnd);
            DragHelper.EnsureTitleBar(_schedWnd,
                UiTextEn.Translate("Schedule"));
            if (FindLeaf<BaseButton>(_schedWnd, "FilterCBX") is { } fcb)
                fcb.Toggled += _ => FillSchedule();
            var pane = FindLeaf<Control>(_schedWnd, "ListPane");
            if (pane != null)
            {
                _schedList = new ItemList
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill,
                };
                _schedList.SetAnchorsAndOffsetsPreset(
                    LayoutPreset.FullRect);
                pane.AddChild(_schedList);
            }
        }
        FillSchedule();
        _schedWnd.Visible = true;
    }

    /// <summary>Créneaux du 515 ; la case « filtrer les guerres déjà
    /// passées » (FilterCBX) masque ceux dont la fin est écoulée.</summary>
    private void FillSchedule()
    {
        if (_schedList == null) return;
        _schedList.Clear();
        var hide = _schedWnd != null &&
            FindLeaf<BaseButton>(_schedWnd, "FilterCBX")
                is { ButtonPressed: true };
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var s in _schedules)
        {
            if (hide && s.End < now) continue;
            _schedList.AddItem($"Guerre {s.Type} : {FmtEpoch(s.Begin)} → " +
                $"{FmtEpoch(s.End)}");
        }
    }

    private static string FmtEpoch(int epoch) =>
        DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime
            .ToString("dd/MM HH:mm");

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientTerritoryQuery q:
                _entries.Clear();
                _entries.AddRange(q.Entries);
                _schedules.Clear();
                _schedules.AddRange(q.Schedules);
                FillList();
                Visible = true;
                break;
            case ZoneClientTerritoryInfo i:
                // [s16 type 0-2][u32 remaining][i32 family][u16 camp]
                Chat($"Guerre de territoire : {PhaseLabel(i.Field1)} — " +
                    $"{i.Field2 / 60} min restantes.");
                break;
            case ZoneClientTerritoryEnd:
                Chat(UiTextEn.Translate("Territory finished."));
                break;
            case ZoneClientTerritoryKilledInfo k:
                Chat($"Frag territoire : {k.Field1}");
                break;
        }
    }

    /// <summary>Noms de territoires natifs (« 灰岩山爭奪戰 » = guerre
    /// de la map ; noms de maps FR via docs/reference/maps.tsv).</summary>
    private static string TerritoryName(string name)
    {
        const string suffix = "爭奪戰";
        if (!name.EndsWith(suffix)) return UiTextEn.Translate(name);
        var fr = name[..^suffix.Length] switch
        {
            "灰岩山" => "des Monts Calcaires",
            "寧靜之丘" => "de la Colline Tranquille",
            "彩瀑森林" => "de la Forêt Arc-en-Ciel",
            "戈德山域" => "des Monts Arides",
            "冷風海灣" => "de la Baie Bélouga",
            "帝王谷" => "de la Vallée des Rois",
            "阿維拉火山" => "du Volcan Avila",
            "大草原" => "des Plaines Dorées",
            "無盡沙漠" => "des Terres des Dunes Éternelles",
            "古老溼地" => "des Bourbes",
            _ => null,
        };
        return fr != null ? $"Guerre {fr}" : name;
    }

    private static string PhaseLabel(short type) => type switch
    {
        0 => "préparation",
        1 => "en cours",
        _ => "clôture",
    };

    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        var q = new Queue<Node>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            // Noms CEGUI aplatis (« Root_A_B_Leaf ») : suffixe exact.
            var nm = n.Name.ToString();
            if (n is T t && (nm == leaf || nm.EndsWith("_" + leaf)))
                return t;
            foreach (var c in n.GetChildren()) q.Enqueue(c);
        }
        return null;
    }
}

/// <summary>Colosseum / gladiateurs — 527 entrée, 528 résultat join,
/// 529 team info, 530 leave/join, 450-452 équipe gladiateur ;
/// entrée via C→S 199 <see cref="ZoneServerColosseumJoinRequest"/>.</summary>
public partial class ColosseumWindow : ServicePanel
{
    public override void _Ready()
    {
        BuildPanel("__colosseum", UiTextEn.Translate("Colosseum"),
            layout: "ArenaMain.layout");
        var row = new HBoxContainer();
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        row.Position = new Vector2(-160, -34);
        Panel!.AddChild(row);
        var join = new Button { Text = UiTextEn.Translate("Join queue") };
        join.Pressed += () => Send(new ZoneServerColosseumJoinRequest());
        row.AddChild(join);
        var leave = new Button { Text = UiTextEn.Translate("Leave queue") };
        leave.Pressed += () => Send(new ZoneServerColosseumLeaveJoin());
        row.AddChild(leave);
        var team = new Button { Text = UiTextEn.Translate("Team info") };
        team.Pressed += () => Send(new ZoneServerColosseumTeamQuery());
        row.AddChild(team);
    }

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Gladiator arena\n\nMap : Arena of Souls\n") +
            UiTextEn.Translate("Queue : 12 waiting — 3v3\n\n") +
            UiTextEn.Translate("Join the queue to enter."));

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientEnterColosseum e:
                Show(UiTextEn.Translate($"Colosseum entry ({e.Value}) — join the queue."));
                break;
            case ZoneClientColosseumJoinResult j:
                Chat(UiTextEn.Translate($"Colosseum join: {j.Value}"));
                break;
            case ZoneClientColosseumTeamInfo t:
                Show(UiTextEn.Translate($"Team: {t.Field1} {t.Field2} {t.Field3}"));
                break;
            case ZoneClientGladiatorTeamInfo g:
                // 525 = push de données (evalId/points/gold assignés à
                // l'entrée d'un trial ou par la DBA) — pas un ordre
                // d'ouverture : on rafraîchit seulement si le panneau
                // est déjà visible.
                if (Visible && Body != null)
                    Body.Text = UiTextEn.Translate(
                        $"Gladiators: {g.Field1} {g.Field2} {g.Field3}");
                break;
            case ZoneClientDisbandGladiatorTeam:
                Chat(UiTextEn.Translate("Gladiator team disbanded."));
                break;
        }
    }
}

/// <summary>Recrutement d'instance — 553 résultat, 564 dialog ;
/// candidature via C→S 220 <see cref="ZoneServerRecruitInstance"/>.</summary>
public partial class RecruitWindow : ServicePanel
{
    private LineEdit? _nodes;
    private OptionButton? _role;

    public override void _Ready()
    {
        BuildPanel("__recruit", "Recrutement d'instance",
            layout: "Recruit.layout");
        var row = new HBoxContainer();
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        row.Position = new Vector2(-200, -34);
        Panel!.AddChild(row);
        _nodes = new LineEdit
        {
            PlaceholderText = "nodeIds (ex: 110,501)",
            CustomMinimumSize = new Vector2(130, 0),
        };
        row.AddChild(_nodes);
        _role = new OptionButton();
        _role.AddItem("Tank (0)"); _role.AddItem("Soin (1)");
        _role.AddItem("DD (2)");
        _role.Selected = 2;
        row.AddChild(_role);
        var join = new Button { Text = UiTextEn.Translate("Join") };
        join.Pressed += () => Send(new ZoneServerRecruitInstance
        {
            Nodes = ParseNodes(_nodes.Text), Role = (short)_role.Selected,
            Flag = 0,
        });
        row.AddChild(join);
        var leave = new Button { Text = UiTextEn.Translate("Leave") };
        leave.Pressed += () => Send(new ZoneServerRecruitInstance
            { Flag = 1 });
        row.AddChild(leave);
    }

    private static short[] ParseNodes(string? t) =>
        (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => short.TryParse(x.Trim(), out var v) ? v : (short)0)
            .Where(v => v > 0).Take(4).ToArray();

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Instance recruitment\n\n") +
            UiTextEn.Translate("Purpose : dungeon run\nDemand : lv 40+\n\n") +
            UiTextEn.Translate("Auto-join / start recruit via the checkboxes."));

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientOpenInstanceDialog d:
                Show(UiTextEn.Translate($"Instance creation: {d.Field1}/{d.Field2}"));
                break;
            case ZoneClientRecruitInstanceResult r:
                Show($"Recrutement : {r.Field1}/{r.Field2}");
                break;
        }
    }
}

/// <summary>Croisade de bataille — 617
/// <c>ClientOpenBattleCrusadeDialog</c> [i32 npcUid][u16 n1][n1×i32]
/// [u16 n2][n2×i32] ouvre <c>battlecrusadewnd.layout</c> ; le joueur
/// choisit une croisade dans le combobox puis JoinBtn → C→S 259
/// <see cref="ZoneServerBattleCrusadeJoin"/>. Le codec généré ne lit
/// que les champs fixes : la liste est relue ici via un handler
/// <c>OnZone(617)</c> dédié (le dispatch accepte plusieurs handlers).</summary>
public partial class CrusadeWindow : ServicePanel
{
    private OptionButton? _combo;
    private readonly List<int> _crusades = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action>
        _pending = new();
    private bool _bound;

    public override void _Ready()
    {
        BuildPanel("__crusade", "Bataille des croisades",
            w: 480, h: 420, layout: "battlecrusadewnd.layout");
        if (Panel == null) return;
        DragHelper.EnsureTitleBar(Panel,
            UiTextEn.Translate("Crusade battle"));
        _combo = FindLeaf<OptionButton>(Panel, "combobox");
        _combo?.ItemSelected += _ => ShowCrusadeInfo();
        if (FindLeaf<BaseButton>(Panel, "JoinBtn") is { } j)
            j.Pressed += () =>
            {
                var i = _combo?.Selected ?? -1;
                if (i >= 0 && i < _crusades.Count)
                    Send(new ZoneServerBattleCrusadeJoin
                        { Value = _crusades[i] });
            };
    }

    public override void _Process(double delta)
    {
        if (!_bound && SessionCarry.Session?.Zone?.IsConnected == true)
        {
            _bound = true;
            SessionCarry.Session!.OnZone(617, r =>
            {
                var ids = new List<int>();
                r.ReadInt();            // npcUid
                for (var list = 0; list < 2 && r.Remaining >= 2; list++)
                {
                    var n = r.ReadUShort();
                    for (var i = 0; i < n && r.Remaining >= 4; i++)
                        ids.Add(r.ReadInt());
                }
                _pending.Enqueue(() => Fill(ids));
            });
        }
        while (_pending.TryDequeue(out var act)) act();
    }

    private void Fill(List<int> ids)
    {
        _crusades.Clear();
        _crusades.AddRange(ids);
        _combo?.Clear();
        if (_crusades.Count == 0)
            _combo?.AddItem(UiTextEn.Translate("No crusade available"));
        else
            foreach (var id in _crusades)
                _combo?.AddItem($"Croisade n°{id}");
        ShowCrusadeInfo();
        Visible = true;
        if (Panel != null) Panel.Visible = true;
    }

    /// <summary>Panneau d'infos : le layout porte des gabarits
    /// (« 灰岩山時空戰場 », « 已開啟 », conditions 1-3). Seule la
    /// liste du 617 est connue : on n'invente ni nom ni conditions.</summary>
    private void ShowCrusadeInfo()
    {
        if (Panel == null) return;
        var i = _combo?.Selected ?? -1;
        var has = i >= 0 && i < _crusades.Count;
        SetLabel("CrusadeName", has ? $"Croisade n°{_crusades[i]}" : "—");
        SetLabel("CrusadeStatus", has ? "Ouverte" : "Fermée");
        for (var c = 1; c <= 3; c++) SetLabel($"conditions{c}", "");
    }

    private void SetLabel(string leaf, string text)
    {
        if (FindLeaf<Label>(Panel, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(Panel, leaf) is { } r)
            r.Text = text;
    }

    protected override void OnPacket(NetMessage m)
    {
        // Le 617 est relu par le handler dédié ; le message généré ne
        // sert qu'à ne pas laisser l'opcode « non géré ».
    }

    private static T? FindLeaf<T>(Node? root, string leaf)
        where T : class
    {
        if (root == null) return null;
        var q = new Queue<Node>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            // Noms CEGUI aplatis (« Root_A_B_Leaf ») : suffixe exact.
            var nm = n.Name.ToString();
            if (n is T t && (nm == leaf || nm.EndsWith("_" + leaf)))
                return t;
            foreach (var c in n.GetChildren()) q.Enqueue(c);
        }
        return null;
    }
}

/// <summary>Fast dress (tenues rapides) — 594 update ; applique via
/// C→S 244 <see cref="ZoneServerApplyFastDressData"/>.</summary>
public partial class FastDressWindow : ServicePanel
{
    public override void _Ready() =>
        BuildPanel("__fastdress", "Tenues rapides",
            layout: "DressRoomUI.layout");

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Fast dress\n\nSave and apply outfit presets ") +
            UiTextEn.Translate("from the dressing room."));

    protected override void OnPacket(NetMessage m)
    {
        if (m is ZoneClientUpdateFastDressData d)
            Show($"Tenue {d.Field1}/{d.Field2} — {d.Field3} {d.Field4}");
    }
}

/// <summary>Classements — 385 open exploit rank, 521 rank info,
/// 562 killed rank, monde 59.</summary>
public partial class RankWindow : ServicePanel
{
    public override void _Ready() =>
        BuildPanel("__rank", UiTextEn.Translate("Rankings"),
            layout: "RankMainWnd.layout");

    public void EnsureDemo() =>
        Show(UiTextEn.Translate("Rankings\n\n1. Player — 9 850 pts\n") +
            UiTextEn.Translate("2. Hero — 8 120 pts\n3. Knight — 7 400 pts"));

    protected override void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientOpenExploitRank:
                Show("Classement des exploits.");
                break;
            case ZoneClientRankInfo r:
                Show($"Rang : {r.Field2} — {r.Field1}/{r.Field3} " +
                    $"{r.Field4}");
                break;
            case ZoneClientFamilyBattleKilledRank k:
                var lines = new System.Text.StringBuilder(
                    "Frags famille :\n");
                foreach (var e in k.Entries)
                    lines.AppendLine(
                        $"{e.Rank}. {e.PlayerName} ({e.FamilyName}) " +
                        $"— {e.Kills}");
                Show(lines.ToString());
                break;
            case WorldClientQueryExploitRank w:
                Show(UiTextEn.Translate($"Exploit rank : {w.Field1} {w.Field2} {w.Field3}"));
                break;
        }
    }
}
