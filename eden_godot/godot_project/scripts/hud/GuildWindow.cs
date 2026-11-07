using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Ui;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre de guilde (phase C1) — touche <b>G</b>. Reconstruite sur les
/// layouts natifs EDENFR : <c>familywnd_na</c> (FrameWindow +
/// TabControl, panes injectés au runtime comme le client natif) avec
///   - onglet Guilde : <c>familyinfownd_na</c> (fiche + MCL membres +
///     boutons Notice/Recruit/Donate/LevelUp/Emblem/Exit/Disband/CmtNew/
///     FamilyList/EnterTerritory) ;
///   - onglet Messages : <c>familymessagewnd_na</c> (MCL board) ;
///   - onglet Rangs : <c>familyorderwnd_na</c> (7 rangs EmText,
///     SetJobBtn → <c>familyjobnamedlg</c>, Privilege →
///     <c>familysetprivilegedlg</c>) ;
///   - onglet Ville : <c>familyadministration</c> (slots House0-2,
///     réparations, taxe, NPCList) — données via 132 → 487.
/// Dialogues natifs : <c>familydonatedlg</c>, <c>familybulletindlg</c>,
/// <c>familycommentdlg</c>, <c>familyjobnamedlg</c>,
/// <c>familysetprivilegedlg</c>, <c>cnfamilyemblem</c>,
/// <c>familyfindingwind</c>+panes (guilde hors guilde).
///
/// Données : <see cref="GuildState"/> alimenté par la pompe monde de
/// <see cref="SocialWindow"/> ; émissions uniquement + trame zone
/// <b>346</b> pour le self.
///
/// Wire (preuves : codecs serveur + désasm cités en commentaire) :
///   - monde 9 <see cref="WorldFamilyOp"/> — invite 0x01, accept 0x03,
///     reject 0x04, quit 0x05, kick 0x06, leader 0x07, disband 0x08,
///     editinfo 0x0a, cadretitle 0x0b, setcadre 0x0c, setprivilege
///     0x0e, add/del message 0x0f/0x11, comment 0x13, taxes
///     0x14/0x15, refresh level 0x16, emblem data 0x46, recruit
///     0x4e/0x4f/0x50 ;
///   - monde 10 <see cref="WorldQueryFamilyInfo"/>, 73 emblem ;
///   - zone 49 create, 81 update, 82 emblem, 159 donate,
///     173 instance create, 176 town enter, 132 instance data → 487.
/// </summary>
public partial class GuildWindow : Control
{
    private Control? _win;
    private TabContainer? _tabs;
    private Control? _infoPane;
    private Control? _msgPane;
    private Control? _orderPane;
    private Control? _adminPane;
    private Control? _noGuild;
    private Control? _findWin;
    private Control? _dlgDonate;
    private Control? _dlgBulletin;
    private Control? _dlgComment;
    private Control? _dlgJob;
    private Control? _dlgPriv;
    private Control? _dlgEmblem;
    private Control? _dlgTownCreate;
    private Panel? _invitePopup;
    private Label? _inviteLabel;
    private PopupMenu? _memberMenu;
    private PopupMenu? _statueMenu;
    private PopupMenu? _boardMenu;
    private MclList? _memberMcl;
    private MclList? _boardMcl;
    private bool _nativeOk;

    private EdenSession? _session;
    private bool _zoneBound;
    private HudRoot? _hud;
    private readonly ConcurrentQueue<Action> _pending = new();

    /// <summary>Construit la fenêtre (appelée par MinimapPanel —
    /// même convention que <see cref="SocialWindow"/>).</summary>
    public void Build(HudRoot hud)
    {
        _hud = hud;
        _win = hud.Build("familywnd_na.layout");
        if (_win != null)
        {
            _nativeOk = true;
            _win.Name = "GuildPanel"; // ToggleByName (CityBtn)
            AddChild(_win);
            _win.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            _win.Visible = false;
            DragHelper.EnsureTitleBar(_win,
                UiTextEn.Translate("Guild"));
            DragHelper.Persist(_win, "guild");
            _tabs = FindLeaf<TabContainer>(_win, "TabControl");
            BuildInfoPane();
            BuildMsgPane();
            BuildOrderPane();
            BuildAdminPane();
        }
        else
        {
            BuildFallbackPanel();
        }
        BuildNoGuildView();
        BuildInvitePopup();
        BuildMemberMenu();
        GuildState.Changed += Refresh;
        MouseFilter = MouseFilterEnum.Ignore;
        Refresh();
    }

    // ------------------------------------------------------------------
    // Panes natifs
    // ------------------------------------------------------------------

    /// <summary>Injecte un pane natif dans le TabControl — comme le
    /// client natif qui ajoute les DefaultWindow au runtime.</summary>
    private void InjectPane(string layout, string tabName)
    {
        if (_tabs == null) return;
        var pane = _hud?.Build(layout);
        if (pane == null) return;
        pane.Name = UiTextEn.Translate(tabName);
        _tabs.AddChild(pane);
    }

    /// <summary>Onglet Guilde — <c>familyinfownd_na</c> : fiche,
    /// bulletin, MCL membres, boutons natifs.</summary>
    private void BuildInfoPane()
    {
        InjectPane("familyinfownd_na.layout", "Guild");
        _infoPane = _tabs?.GetChildCount() > 0
            ? _tabs.GetChild<Control>(0) : null;
        if (_infoPane == null) return;
        _memberMcl = FindLeaf<MclList>(_infoPane, "ListBox");
        _memberMcl?.SetColumns(new MclColumn[]
        {
            new(UiTextEn.Translate("Name"), 0f, 150f, 0),
            new(UiTextEn.Translate("Class"), 0f, 90f, 1),
            new("Lv", 0f, 40f, 2),
            new(UiTextEn.Translate("Rank"), 0f, 110f, 3),
            new(UiTextEn.Translate("Contrib."), 0f, 70f, 4),
        });
        _memberMcl?.RowSelected += _ => { };   // sélection simple
        var online = FindLeaf<BaseButton>(_infoPane, "OnlineBtn");
        if (online != null) online.Toggled += _ => RenderMembers();
        WireInfoButtons(_infoPane);
        StyleInfoPane(_infoPane);
    }

    private static readonly Color Ink = new(0.12f, 0.08f, 0.2f);

    /// <summary>Texte sombre sur les cadres clairs du layout (les
    /// labels héritent d'un blanc illisible sur parchemin).</summary>
    private static void Darken(Node root, params string[] pathParts)
    {
        foreach (var n in root.FindChildren("*", "Control", true, false))
        {
            var nm = n.Name.ToString();
            if (!pathParts.Any(pp => nm.Contains(pp))) continue;
            if (n is Label l)
            {
                l.AddThemeColorOverride("font_color", Ink);
                l.AddThemeConstantOverride("outline_size", 0);
            }
            else if (n is RichTextLabel r)
                r.AddThemeColorOverride("default_color", Ink);
        }
    }

    private static void ShrinkButton(Control pane, string leaf,
        string? text = null)
    {
        if (FindLeaf<Button>(pane, leaf) is not { } b) return;
        if (text != null) b.Text = text;
        b.ClipText = true;
        b.ClipContents = true;
        b.AddThemeFontSizeOverride("font_size", 10);
        // Texte clair : le skin pose un texte sombre sur fond violet.
        var light = new Color(0.96f, 0.93f, 1f);
        foreach (var k in new[] { "font_color", "font_hover_color",
                     "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(k, light);
    }

    private void StyleInfoPane(Control pane)
    {
        Darken(pane, "FamilyBor", "BorText");
        // Annonce : parchemin clair + texte sombre (zone grise vide).
        if (FindLeaf<TextEdit>(pane, "BulletinEdit") is { } bed)
        {
            var bg = new StyleBoxFlat { BgColor = new Color(0.97f, 0.95f, 0.99f) };
            bed.AddThemeStyleboxOverride("normal", bg);
            bed.AddThemeStyleboxOverride("read_only", bg);
            bed.AddThemeColorOverride("font_color", Ink);
            bed.AddThemeColorOverride("font_readonly_color", Ink);
        }
        // Boutons courts, libellés FR (les natifs « 城鎮列表 »/« 進入領地 »
        // débordaient et se recouvraient).
        ShrinkButton(pane, "FamilyListBtn", "Villes de guilde");
        ShrinkButton(pane, "EnterTerritoryBtn", "Entrer en ville");
        ShrinkButton(pane, "LevelUpBtn", "Niveau sup.");
        ShrinkButton(pane, "EmblemBtn", "Emblème");
        ShrinkButton(pane, "EmblemTestBtn", "Aperçu");
        ShrinkButton(pane, "CmtNew", "Commentaire");
        foreach (var l in new[] { "NoticeBtn", "RecruitBtn", "DonateBtn",
                     "ExitBtn", "DisbandBtn" })
            ShrinkButton(pane, l);
        if (FindLeaf<CheckBox>(pane, "OnlineBtn") is { } ob)
        {
            ob.Text = "Afficher les hors-ligne";
            ob.AddThemeFontSizeOverride("font_size", 10);
        }
        // Quêtes de guilde (_na) : aucun paquet connu → boutons morts
        // masqués plutôt que 3 libellés EN collés en bas à gauche.
        foreach (var l in new[] { "FamilyMission01", "FamilyMission02",
                     "FmailyFix" })
            if (FindLeaf<Control>(pane, l) is { } m) m.Visible = false;
        _memberMcl?.AddThemeColorOverride("title_button_color", Ink);
    }

    private void WireInfoButtons(Control pane)
    {
        void Btn(string leaf, Action a)
        {
            if (FindLeaf<BaseButton>(pane, leaf) is { } b)
                b.Pressed += a;
        }
        Btn("NoticeBtn", () => ShowBulletinDlg());
        Btn("RecruitBtn", () => FireWorld(
            WorldFamilyOp.Of(FamilyOps.SetRecruit,
                GuildState.Recruit > 0 ? 0 : 1)));
        Btn("DonateBtn", () => ShowDonateDlg());
        Btn("LevelUpBtn", () => FireWorld(
            WorldFamilyOp.Simple(FamilyOps.RefreshLevel)));
        Btn("EmblemBtn", () => ShowEmblemDlg());
        Btn("EmblemTestBtn", () => ShowEmblemDlg());
        Btn("ExitBtn", () => Confirm(
            UiTextEn.Translate("Leave the guild?"),
            () => FireWorld(WorldFamilyOp.Simple(FamilyOps.Quit))));
        Btn("DisbandBtn", () => Confirm(
            UiTextEn.Translate("Disband the guild? This cannot be undone."),
            () => FireWorld(WorldFamilyOp.Simple(FamilyOps.Disband))));
        // Le layout _na laisse « 1234 » sur ces trois boutons (libellé
        // posé au runtime par le client natif).
        SetBtnText(pane, "LevelUpBtn", UiTextEn.Translate("Guild level up"));
        SetBtnText(pane, "EmblemBtn", UiTextEn.Translate("Emblem"));
        SetBtnText(pane, "EmblemTestBtn", UiTextEn.Translate("Preview emblem"));
        Btn("CmtNew", () => ShowCommentDlg());
        Btn("FamilyListBtn", () =>
        {
            // « Villes de guilde » : liste des villes (familytownlistwnd).
            if (GetTree().CurrentScene.FindChild("TownListWindow", true,
                    false) is TownListWindow tl) tl.Open();
            else ShowFindWindow();
        });
        Btn("EnterTerritoryBtn", () =>
        {
            if (GuildState.FamilyId <= 0) return;
            // Natif FamilyInfoWnd @0x00a98b10 : bouton grisé si
            // family+0xd8 (house_open) == 0 ; libellé « Ville guilde »
            // (texte 0x2f) hors ville, « Quitter ville » (0xb53)
            // quand le node courant est de type 4 (FUN_0071ef30).
            if (GuildState.HouseOpen <= 0) return;
            var leave = InGuildHall();
            // 176 : familyId = −1 quitte le hall (serveur
            // HandleFamilyTownEnterAsync), sinon visite la ville.
            var fam = leave ? -1 : GuildState.FamilyId;
            GD.Print($"[vdg-panel] EnterTerritoryBtn → 176 fam={fam} " +
                $"(houseOpen={GuildState.HouseOpen} inHall={leave})");
            FireZone(new ZoneServerFamilyTownEnter { Value = fam });
        });
        UpdateTerritoryBtn();
    }

    /// <summary>Vrai si le perso est dans un hall de guilde
    /// (<c>Map_S501</c>/<c>Map_S502</c>, enfants du GameScene — même
    /// détection que <c>FamilyHallBuildings.WatchHallMap</c> ; natif :
    /// type du node courant == 4).</summary>
    private bool InGuildHall()
    {
        var scene = GetTree()?.CurrentScene;
        if (scene == null) return false;
        foreach (var child in scene.GetChildren())
        {
            var n = child.Name.ToString();
            if (n.Equals("Map_S501", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Map_S502", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool _lastTerritoryLeave;

    /// <summary>État du bouton « Ville guilde / Quitter ville » —
    /// désactivé sans ville (house_open == 0), libellé selon le
    /// node (natif @0x00a98b10).</summary>
    private void UpdateTerritoryBtn()
    {
        if (_infoPane == null ||
            FindLeaf<Button>(_infoPane, "EnterTerritoryBtn")
                is not { } b) return;
        var hasTown = GuildState.HouseOpen > 0;
        var leave = hasTown && InGuildHall();
        b.Disabled = !hasTown;
        b.Text = leave ? "Quitter ville" : "Entrer en ville";
        b.TooltipText = hasTown ? ""
            : "Votre guilde n'a pas de ville de guilde.";
        if (leave != _lastTerritoryLeave)
        {
            _lastTerritoryLeave = leave;
            GD.Print($"[vdg-panel] bouton ville : " +
                $"{(leave ? "Quitter ville" : "Entrer en ville")}");
        }
    }

    /// <summary>Onglet Messages — <c>familymessagewnd_na</c> : MCL du
    /// board + MessageBtn → dialogue de rédaction.</summary>
    private void BuildMsgPane()
    {
        InjectPane("familymessagewnd_na.layout", "Messages");
        _msgPane = _tabs?.GetChildCount() > 1
            ? _tabs.GetChild<Control>(1) : null;
        if (_msgPane == null) return;
        _boardMcl = FindLeaf<MclList>(_msgPane, "MultList");
        _boardMcl?.SetColumns(new MclColumn[]
        {
            new(UiTextEn.Translate("Date"), 0f, 70f, 0),
            new(UiTextEn.Translate("Title"), 0f, 260f, 1),
            new(UiTextEn.Translate("Author"), 0f, 120f, 2),
        });
        if (FindLeaf<BaseButton>(_msgPane, "MessageBtn") is { } b)
            b.Pressed += () => ShowBulletinDlg(board: true);
        _boardMcl?.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true,
                ButtonIndex: MouseButton.Right })
                ShowBoardMenu();
        };
    }

    /// <summary>Onglet Rangs — <c>familyorderwnd_na</c> : 7 rangs
    /// (EmText1-7 → Titles[2..8]), SetJobBtn → jobnamedlg,
    /// Privilege Btn → setprivilegedlg.</summary>
    private void BuildOrderPane()
    {
        InjectPane("familyorderwnd_na.layout", "Ranks");
        _orderPane = _tabs?.GetChildCount() > 2
            ? _tabs.GetChild<Control>(2) : null;
        if (_orderPane == null) return;
        if (FindLeaf<BaseButton>(_orderPane, "SetJobBtn") is { } j)
            j.Pressed += ShowJobNameDlg;
        Darken(_orderPane, "JobBackgrand_Border", "Privilege_Border");
        SetT(_orderPane, "TempText",
            UiTextEn.Translate("Town events"));
        // Le bouton « Privilege » ouvre l'éditeur de privilèges.
        foreach (var n in _orderPane.FindChildren("*Btn",
                     "BaseButton", true, false))
            if (n.Name.ToString().EndsWith("Privilege_Btn") ||
                n.Name.ToString() == "Btn")
                ((BaseButton)n).Pressed += ShowPrivilegeDlg;
    }

    /// <summary>Onglet Ville — <c>familyadministration</c> : bâtiments
    /// du hall (132 → 487), réparations (serveur E4 : désactivées),
    /// taxe commerciale.</summary>
    private void BuildAdminPane()
    {
        InjectPane("familyadministration.layout", "Town");
        _adminPane = _tabs?.GetChildCount() > 3
            ? _tabs.GetChild<Control>(3) : null;
        if (_adminPane == null) return;
        var npc = FindLeaf<MclList>(_adminPane, "NPCList");
        npc?.SetColumns(new MclColumn[]
        {
            new(UiTextEn.Translate("NPC"), 0f, 180f, 0),
            new(UiTextEn.Translate("Loc"), 0f, 60f, 1),
        });
        if (FindLeaf<BaseButton>(_adminPane, "BusinessTax") is { } t)
            t.Pressed += ShowTaxDlg;
        // Sélection du bâtiment à réparer : clic sur Slot{i} mémorise
        // son Loc ; Repair → 180 [loc][0], RepairAll → [0][1].
        for (var i = 0; i < 3; i++)
        {
            var idx = i;
            var slot = ItemSlotCell.Find(_adminPane,
                $"Root/FamilyAdministrationWnd/Block2/House/Slot{i}");
            if (slot == null) continue;
            _adminSlots.Add(slot);
            slot.Clicked += _ =>
            {
                if (idx < _adminBuildings.Count)
                {
                    _repairLoc = _adminBuildings[idx].Loc;
                    for (var j = 0; j < _adminSlots.Count; j++)
                        _adminSlots[j].Modulate =
                            j == idx ? new Color(1.3f, 1.2f, 0.7f)
                                     : Colors.White;
                }
            };
        }
        if (FindLeaf<BaseButton>(_adminPane, "Repair") is { } rep)
            rep.Pressed += () =>
            {
                if (_repairLoc >= 0)
                    FireZone(new ZoneServerFamilyBuildingRepair
                        { Loc = _repairLoc, RepairAll = 0 });
            };
        if (FindLeaf<BaseButton>(_adminPane, "RepairAll") is { } all)
            all.Pressed += () =>
                FireZone(new ZoneServerFamilyBuildingRepair
                    { Loc = 0, RepairAll = 1 });
        // Données de l'instance : 132 → 487.
        MiscPackets.Received += OnMisc;
        ResetAdminPlaceholders();
        Callable.From(FitAdminTexts).CallDeferred();
    }

    /// <summary>Le layout porte du texte maquette (« L09.煉金小屋 »,
    /// « 毀損 : 100 / 999 », « 999999 »…) : on le vide dès la
    /// construction pour qu'une carte sans bâtiment soit vide.</summary>
    private void ResetAdminPlaceholders()
    {
        if (_adminPane == null) return;
        SetT(_adminPane, "FamilyName", "");
        for (var i = 0; i < 3; i++)
        {
            foreach (var l in new[] { "Name", "Durability", "ExpText",
                         "GoldTesxt", "SliverText", "MaintenanceCosts" })
                SetT(_adminPane, $"{l}{i}", "");
            // Pas de coût dans le 487 : icônes/étiquette de coût masquées.
            foreach (var l in new[] { "GoldIcon", "SliverIcon",
                         "MaintenanceCosts", "ExpBar" })
                if (FindLeaf<Control>(_adminPane, $"{l}{i}") is { } c)
                    c.Visible = false;
        }
        // Liste de PNJ : pas de donnée serveur — cadre vide masqué.
        if (FindLeaf<Control>(_adminPane, "NPCList") is { } npc)
        {
            npc.Visible = false;
            if (npc.GetParent() is Control par && par != _adminPane &&
                par.Name.ToString().Contains("Block3"))
                par.Visible = false;
        }
    }

    /// <summary>Contraint les textes des cartes dans leur carte
    /// (clip + police réduite) et décale « TaxText » à droite du
    /// bouton « Taxes » qu'il chevauchait.</summary>
    private void FitAdminTexts()
    {
        if (_adminPane == null || !IsInstanceValid(_adminPane)) return;
        for (var i = 0; i < 3; i++)
        {
            var card = FindLeaf<Control>(_adminPane, $"House{i}");
            if (card == null) continue;
            var cr = card.GetGlobalRect();
            foreach (var l in new[] { "Name", "Durability", "ExpText" })
            {
                if (FindLeaf<Control>(_adminPane, $"{l}{i}") is not
                    { } c) continue;
                c.ClipContents = true;
                if (c is Label lb)
                {
                    lb.ClipText = true;
                    lb.AutowrapMode = TextServer.AutowrapMode.Off;
                    lb.AddThemeFontSizeOverride("font_size", 10);
                }
                var g = c.GlobalPosition;
                var left = Mathf.Max(g.X, cr.Position.X + 4);
                var right = Mathf.Min(g.X + c.Size.X, cr.End.X - 4);
                if (right - left < 20) continue;
                c.GlobalPosition = new Vector2(left, g.Y);
                c.Size = new Vector2(right - left, c.Size.Y);
            }
        }
        var btn = FindLeaf<BaseButton>(_adminPane, "BusinessTax");
        var tax = FindLeaf<Control>(_adminPane, "TaxText");
        if (btn != null && tax != null)
        {
            btn.ClipContents = true;
            var minX = btn.GetGlobalRect().End.X + 6;
            var g = tax.GlobalPosition;
            if (g.X < minX)
            {
                var right = g.X + tax.Size.X;
                tax.GlobalPosition = new Vector2(minX, g.Y);
                tax.Size = new Vector2(Mathf.Max(20, right - minX),
                    tax.Size.Y);
            }
            tax.ClipContents = true;
            if (tax is Label tl)
                tl.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }

    // ------------------------------------------------------------------
    // Paquets zone propres à la fenêtre
    // ------------------------------------------------------------------

    private readonly List<ItemSlotCell> _adminSlots = new();
    private readonly List<ZoneClientFamilyInstanceData.Building>
        _adminBuildings = new();
    private short _repairLoc = -1;

    private void OnMisc(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientFamilyInstanceData d:
                RenderAdmin(d);
                break;
            // Bureau de création de guilde (service PNJ type …) —
            // le natif ouvre directement la fenêtre famille.
            case ZoneClientEnterFamilyCreation:
                Open();
                break;
            // Bureau « ville de guilde » (service PNJ type 63,
            // SVC35/SVC49) — dialogue d'achat → C→S 173.
            case ZoneClientEnterFamilyInstanceCreation:
                ShowTownCreateDlg();
                break;
        }
    }

    /// <summary>Menu contextuel clic droit sur un membre du MCL :
    /// chuchoter / expulser / passer chef / changer de rang /
    /// commentaire — ops monde 9 éprouvées.</summary>
    private void BuildMemberMenu()
    {
        _memberMenu = new PopupMenu();
        _memberMenu.AddItem(UiTextEn.Translate("Whisper"), 1);
        _memberMenu.AddItem(UiTextEn.Translate("Kick"), 2);
        _memberMenu.AddItem(UiTextEn.Translate("Make leader"), 3);
        var rankSub = new PopupMenu { Name = "rank" };
        for (var i = 0; i <= 7; i++)
            rankSub.AddItem(RankName(i), 100 + i);
        rankSub.IdPressed += id =>
        {
            var cid = SelectedMember();
            if (cid != 0)
                FireWorld(WorldFamilyOp.Of(FamilyOps.SetCadre, cid,
                    (int)id - 100));
        };
        _memberMenu.AddChild(rankSub);
        _memberMenu.AddSubmenuNodeItem(UiTextEn.Translate("Set rank"),
            rankSub, 4);
        _memberMenu.AddItem(UiTextEn.Translate("Comment"), 5);
        // Statues de joueurs (zone 161 — chef seul, dans son hall) :
        // pose sur les piédestaux 1-4 (≤2 si guilde < niv 6, gate
        // serveur) ou retrait de la statue du membre (position 0,
        // nom = PlayerName persisté). rot/frame = action_id/frame_key
        // de la pose — 0 = pose debout par défaut.
        _statueMenu = new PopupMenu { Name = "statue" };
        for (var i = 1; i <= 4; i++)
            _statueMenu.AddItem(
                UiTextEn.Translate($"Set Statue No. {i}"), i);
        _statueMenu.AddItem(
            UiTextEn.Translate("Remove the Statue?"), 0);
        _statueMenu.IdPressed += id =>
        {
            var cid = SelectedMember();
            if (cid == 0) return;
            var name = GuildState.Members.TryGetValue(cid, out var mm)
                ? mm.Name : "";
            FireZone(new ZoneServerFamilyStatue
            {
                Field1 = cid, Field2 = (int)id, Field3 = name,
            });
        };
        _memberMenu.AddChild(_statueMenu);
        _memberMenu.AddSubmenuNodeItem(
            UiTextEn.Translate("Erect Statue"), _statueMenu, 6);
        _memberMenu.IdPressed += id =>
        {
            var cid = SelectedMember();
            if (cid == 0) return;
            switch (id)
            {
                case 1: WhisperTo(cid); break;
                case 2: FireWorld(
                    WorldFamilyOp.Of(FamilyOps.Kick, cid)); break;
                case 3: FireWorld(
                    WorldFamilyOp.Of(FamilyOps.SelectLeader, cid));
                    break;
                case 5: ShowCommentDlg(cid); break;
            }
        };
        AddChild(_memberMenu);
        _memberMcl?.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true,
                ButtonIndex: MouseButton.Right } ev &&
                SelectedMember() != 0)
            {
                // L'entrée statue n'a de sens que pour le chef
                // (gate serveur 0x382) — items 0-4 visibles à tous.
                _memberMenu.SetItemDisabled(5, !GuildState.IsLeader(
                    SessionCarry.Character?.Id ?? 0));
                // Titres de rang à jour (le chef peut les renommer).
                if (_memberMenu.GetNodeOrNull<PopupMenu>("rank") is { } rs)
                    for (var ri = 0; ri < rs.ItemCount; ri++)
                        rs.SetItemText(ri, RankName(ri));
                if (_statueMenu != null && GuildState.Level < 6)
                    for (var i = 2; i <= 3; i++)
                        _statueMenu.SetItemDisabled(i, true);
                else if (_statueMenu != null)
                    for (var i = 0; i <= 3; i++)
                        _statueMenu.SetItemDisabled(i, false);
                _memberMenu.Popup(
                    new Rect2I((Vector2I)ev.GlobalPosition,
                        Vector2I.Zero));
            }
        };
    }

    /// <summary>Menu contextuel board : supprimer (op 0x11).</summary>
    private void ShowBoardMenu()
    {
        var sel = _boardMcl?.SelectedRow ?? -1;
        if (sel < 0 || sel >= GuildState.Schedule.Count) return;
        _boardMenu ??= new PopupMenu();
        _boardMenu.Clear();
        _boardMenu.AddItem(UiTextEn.Translate("Delete"), 1);
        ResetIdPressed(_boardMenu);
        _boardMenu.IdPressed += _ => FireWorld(WorldFamilyOp.Of(
            FamilyOps.DelMessage,
            GuildState.Schedule[sel].MessageId));
        if (!_boardMenu.IsInsideTree()) AddChild(_boardMenu);
        _boardMenu.Popup(new Rect2I(
            (Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
    }

    /// <summary>Déconnecte toutes les cibles « pressed » d'un
    /// bouton (dialogue réutilisé par plusieurs actions).</summary>
    private static void ResetPressed(BaseButton b)
    {
        foreach (var c in b.GetSignalConnectionList("pressed"))
            b.Disconnect("pressed", (Callable)c["callable"]);
    }

    private static void ResetIdPressed(PopupMenu m)
    {
        foreach (var c in m.GetSignalConnectionList("id_pressed"))
            m.Disconnect("id_pressed", (Callable)c["callable"]);
    }

    /// <summary>charId du membre sélectionné dans le MCL.</summary>
    private int SelectedMember() =>
        _memberMcl?.SelectedRow is int r && r >= 0 &&
        r < _memberOrder.Count ? _memberOrder[r] : 0;

    private readonly List<int> _memberOrder = new();

    // ------------------------------------------------------------------
    // Dialogues natifs
    // ------------------------------------------------------------------

    /// <summary>familydonatedlg — or + argent → zone 159.</summary>
    private void ShowDonateDlg()
    {
        _dlgDonate ??= BuildDlg("familydonatedlg.layout",
            "donate", "Donate to the guild",
            "Amount to donate (gold / silver)");
        // Champs natifs (FUN_00a9abc0) : EditBox = or (6 chiffres),
        // EditBox2 = argent (3 chiffres), filtre « [0-9]* ».
        foreach (var (leaf, max) in new[] { ("EditBox", 6),
                     ("EditBox2", 3) })
            if (FindLeaf<LineEdit>(_dlgDonate!, leaf) is { } de)
            {
                de.Text = "0";
                de.MaxLength = max;
                de.SelectAllOnFocus = true;
                de.TextChanged += t =>
                {
                    var d = new string(t.Where(char.IsAsciiDigit)
                        .ToArray());
                    if (d != t)
                    {
                        de.Text = d;
                        de.CaretColumn = d.Length;
                    }
                };
            }
        var ok = FindLeaf<BaseButton>(_dlgDonate!, "OKBtn");
        if (ok != null)
        {
            ResetPressed(ok);
            ok.Pressed += () =>
            {
                var g = GetDlgInt(_dlgDonate!, "EditBox");
                var sv = Math.Min(999,
                    GetDlgInt(_dlgDonate!, "EditBox2"));
                // Unité native : 1 or = 1000 argent (FAQ wiki, statue 500 po =
                // FamilyDonateMoney −500000) ; le serveur crédite tel quel.
                long amount = g * 1000L + sv;
                // Le serveur refuse si l'or du joueur < amount (0x388).
                var wallet = PlayerVitals.Gold;
                if (amount > wallet) amount = wallet;
                GD.Print($"[guilddlg] don {amount} (portefeuille " +
                         $"{wallet})");
                if (amount > 0)
                    FireZone(new ZoneFamilyDonateMoney
                        { Value = (int)Math.Min(amount, int.MaxValue) });
                _dlgDonate.Visible = false;
            };
        }
        // Montant par défaut = 0 à chaque ouverture.
        foreach (var leaf in new[] { "EditBox", "EditBox2" })
            if (FindLeaf<LineEdit>(_dlgDonate!, leaf) is { } d0)
                d0.Text = "0";
        OpenDlg(_dlgDonate!);
    }

    /// <summary>familybulletindlg — annonce (0x0a) ou message board
    /// (0x0f titre+contenu via MultEdit).</summary>
    private void ShowBulletinDlg(bool board = false)
    {
        _dlgBulletin ??= BuildDlg("familybulletindlg.layout",
            "bulletin", board ? "Guild message board" : "Guild notice",
            board ? "Title and content of the message"
                  : "Guild notice text");
        // Le dialogue est partagé annonce / message : titre propre.
        SetDlgTitle(_dlgBulletin,
            board ? "Guild message board" : "Guild notice");
        // Annonce : texte seul (multiligne) ; message : titre + corps.
        // EditBox (titre) a un rect natif dégénéré (1 px de haut) :
        // en mode message on lui donne une vraie ligne au-dessus du
        // corps.
        if (FindLeaf<LineEdit>(_dlgBulletin, "EditBox") is { } tb)
        {
            tb.Visible = board;
            tb.Text = "";
            tb.PlaceholderText = UiTextEn.Translate("Title");
            tb.AnchorLeft = 0.0769f; tb.AnchorRight = 0.923f;
            tb.AnchorTop = 0.278f; tb.AnchorBottom = 0.37f;
            tb.OffsetLeft = tb.OffsetRight = 0;
            tb.OffsetTop = tb.OffsetBottom = 0;
        }
        if (FindLeaf<TextEdit>(_dlgBulletin, "MultEdit") is { } me)
        {
            me.Text = board ? "" : GuildState.Info;
            me.AnchorTop = board ? 0.39f : 0.278f;
            me.OffsetTop = 0;
        }
        var ok = FindLeaf<BaseButton>(_dlgBulletin!, "OKBtn");
        if (ok != null)
        {
            ResetPressed(ok);
            ok.Pressed += () =>
            {
                var title = FindLeaf<LineEdit>(_dlgBulletin!,
                    "EditBox")?.Text.Trim() ?? "";
                var body = FindLeaf<TextEdit>(_dlgBulletin!,
                    "MultEdit")?.Text.Trim() ?? "";
                // MaxTextLength natif du MultEdit = 240.
                if (body.Length > 240) body = body[..240];
                if (board)
                {
                    if (title.Length > 0 || body.Length > 0)
                        FireWorld(WorldFamilyOp.OfStr(
                            FamilyOps.AddMessage, title, body));
                }
                else if (body.Length > 0 || title.Length > 0)
                {
                    FireWorld(WorldFamilyOp.OfStr(
                        FamilyOps.EditInfo,
                        body.Length > 0 ? body : title));
                }
                _dlgBulletin.Visible = false;
            };
        }
        OpenDlg(_dlgBulletin!);
    }

    /// <summary>familycommentdlg — commentaire de membre (0x13) ;
    /// charId=0 → self.</summary>
    private void ShowCommentDlg(int charId = 0)
    {
        _dlgComment ??= BuildDlg("familycommentdlg.layout",
            "comment", "Member comment", "Enter a comment");
        if (FindLeaf<LineEdit>(_dlgComment, "EditBox") is { } ce)
        {
            ce.Text = "";
            ce.PlaceholderText = UiTextEn.Translate("Enter a comment");
        }
        var ok = FindLeaf<BaseButton>(_dlgComment!, "OKBtn");
        if (ok != null)
        {
            ResetPressed(ok);
            ok.Pressed += () =>
            {
                var text = FindLeaf<LineEdit>(_dlgComment!,
                    "EditBox")?.Text.Trim() ?? "";
                var target = charId != 0 ? charId
                    : SessionCarry.Character?.Id ?? 0;
                if (target != 0)
                    FireWorld(WorldFamilyOp.OfStr(
                        FamilyOps.EditComment, text, a: target));
                _dlgComment.Visible = false;
            };
        }
        OpenDlg(_dlgComment!);
    }

    /// <summary>familyjobnamedlg — 7 titres de rang (EmText1-7 →
    /// CadreTitle 0x0b index 2..8).</summary>
    private void ShowJobNameDlg()
    {
        _dlgJob ??= BuildDlg("familyjobnamedlg.layout", "jobname",
            "Rank names", "Name of each rank");
        // Pré-remplit les 7 edits avec les titres courants.
        for (var i = 0; i < 7; i++)
        {
            var ed = FindLeaf<LineEdit>(_dlgJob!, $"EditBox{i + 1}");
            if (ed != null)
            {
                var t = i + 2;
                ed.Text = t < GuildState.Titles.Length
                    ? GuildState.Titles[t] : "";
                ed.PlaceholderText = RankName(i);
            }
        }
        var ok = FindLeaf<BaseButton>(_dlgJob!, "OKBtn");
        if (ok != null)
        {
            ResetPressed(ok);
            ok.Pressed += () =>
            {
                for (var i = 0; i < 7; i++)
                {
                    var t = FindLeaf<LineEdit>(_dlgJob!,
                        $"EditBox{i + 1}")?.Text.Trim() ?? "";
                    FireWorld(WorldFamilyOp.OfStr(
                        FamilyOps.CadreTitle, t, a: i + 2));
                }
                _dlgJob.Visible = false;
            };
        }
        OpenDlg(_dlgJob!);
    }

    /// <summary>familysetprivilegedlg — index de privilège (1-14) +
    /// 7 icônes de rang → SetPrivilege 0x0e (a=index, b=masque de
    /// rangs — sémantique du b non prouvée à 100 %, cf. K2 §1).</summary>
    private void ShowPrivilegeDlg()
    {
        _dlgPriv ??= BuildDlg("familysetprivilegedlg.layout",
            "privilege", "Rank privileges",
            "Privilege number (1-14), then click the ranks allowed");
        var ok = FindLeaf<BaseButton>(_dlgPriv!, "OKBtn");
        if (ok != null)
        {
            ResetPressed(ok);
            ok.Pressed += () =>
            {
                var idx = GetDlgInt(_dlgPriv!, "EditBox");
                if (idx is >= 1 and <= 14)
                    FireWorld(WorldFamilyOp.Of(
                        FamilyOps.SetPrivilege, idx, _privMask));
                _dlgPriv.Visible = false;
            };
        }
        if (FindLeaf<LineEdit>(_dlgPriv!, "EditBox") is { } pe)
        {
            pe.Text = "";
            pe.PlaceholderText = "1-14";
        }
        // Icônes 1-7 = bascule des rangs dans le masque.
        _privMask = 0;
        for (var i = 0; i < 7; i++)
        {
            var bit = 1 << i;
            if (FindLeaf<TextureRect>(_dlgPriv!, $"Icon{i + 1}")
                    is { } ic)
            {
                ic.Modulate = Colors.White;
                ic.MouseFilter = MouseFilterEnum.Stop;
                var i2 = i;
                ic.GuiInput += e =>
                {
                    if (e is InputEventMouseButton
                        { Pressed: true, ButtonIndex:
                          MouseButton.Left })
                    {
                        _privMask ^= 1 << i2;
                        ic.Modulate = (_privMask & bit) != 0
                            ? new Color(1, 1, 0.6f) : Colors.White;
                    }
                };
            }
        }
        OpenDlg(_dlgPriv!);
    }
    private int _privMask;

    /// <summary>cnfamilyemblem — aperçu + envoi zone 82.</summary>
    private void ShowEmblemDlg()
    {
        _dlgEmblem ??= BuildDlg("cnfamilyemblem.layout", "emblem",
            "Guild emblem");
        // Règles natives du layout (CN, non traduites en FR).
        SetT(_dlgEmblem, "MsgText", UiTextEn.Translate(
            "Emblem rules: 1. The image must be a 32x32 bmp file. " +
            "2. The emblem cannot be set again within 60 minutes, but " +
            "it can be previewed. 3. A preview only checks that the " +
            "image is valid; it does not set the emblem."));
        SetBtnText(_dlgEmblem!, "EmblemBtn",
            UiTextEn.Translate("Set emblem"));
        SetBtnText(_dlgEmblem!, "EmblemTestBtn",
            UiTextEn.Translate("Preview emblem"));
        if (FindLeaf<BaseButton>(_dlgEmblem!, "EmblemBtn") is { } e)
        {
            ResetPressed(e);
            e.Pressed += () =>
            {
                // L'éditeur natif produit un blob emblem (op 0x46) ;
                // sans éditeur graphique porté on expose l'id zone 82.
                FireWorld(new WorldQueryFamilyEmblem
                { FamilyId = GuildState.FamilyId, Field2 = 1 });
                _dlgEmblem.Visible = false;
            };
        }
        OpenDlg(_dlgEmblem!);
    }

    private OptionButton? _townType;

    /// <summary>Achat de ville de guilde — ouvert par 501
    /// (<c>ClientEnterFamilyInstanceCreation</c>, service PNJ).
    /// Layout natif <c>familyappwnd</c> (« 建鎮申請書 » : choix de la
    /// région dans le Combobox, conditions, coût). Combobox : forêt →
    /// node 501 (type 1), montagne → node 502 (type 0), +variante VIP
    /// (vip u8). Envoie zone 173
    /// <see cref="ZoneServerFamilyInstanceCreate"/>.</summary>
    private void ShowTownCreateDlg()
    {
        if (_dlgTownCreate == null)
        {
            _dlgTownCreate = BuildDlg("familyappwnd.layout", "towncreate",
                "Town founding application");
            _townType = FindLeaf<OptionButton>(_dlgTownCreate, "Combobox");
            if (_townType != null)
            {
                _townType.Clear();
                _townType.AddItem(UiTextEn.Translate("Forest sanctuary"));
                _townType.AddItem(UiTextEn.Translate("Mountain sanctuary"));
                _townType.AddItem(
                    UiTextEn.Translate("Forest sanctuary (VIP)"));
                _townType.AddItem(
                    UiTextEn.Translate("Mountain sanctuary (VIP)"));
            }
            // Conditions / coût : règles serveur (FamilyInstanceCreation :
            // trésorerie ≥ 50 or, ou 1 000 or en VIP).
            SetT(_dlgTownCreate, "CreateText", UiTextEn.Translate(
                "Cost: 50 gold from the guild treasury (VIP: 1,000 gold)."));
            if (FindLeaf<TextEdit>(_dlgTownCreate, "Edit") is { } ed)
            {
                // Conditions du guide « Guilde et ville de guilde »
                // (fondation) + gates serveur 173 (0x599/0x598/0x59a/
                // 0x6ae) et monde (30 membres, cap de villes).
                ed.Text = "Conditions : être le chef de guilde, " +
                    "niveau 30 minimum ; guilde de niveau 3 minimum " +
                    "avec au moins 30 membres ; la trésorerie doit " +
                    "contenir la somme. Pour garder la ville : " +
                    "30 membres minimum et 10 connexions par semaine.";
                ed.Editable = false;
            }
        }
        if (FindLeaf<BaseButton>(_dlgTownCreate, "OKBtn") is { } ok)
        {
            ResetPressed(ok);
            ok.Pressed += () =>
            {
                // FamilyService.InstanceNode : type 1 → 501 (forêt),
                // 0 → 502 (montagne).
                var sel = _townType?.Selected ?? 0;
                var type = (short)(sel % 2 == 0 ? 1 : 0);
                var vip = (byte)(sel >= 2 ? 1 : 0);
                FireZone(new ZoneServerFamilyInstanceCreate
                    { Field1 = type, Field2 = vip });
                _dlgTownCreate.Visible = false;
            };
        }
        OpenDlg(_dlgTownCreate!);
    }

    /// <summary>Taxe commerciale — petit dialogue ad hoc
    /// (pas de layout natif dédié) → 0x15.</summary>
    private void ShowTaxDlg()
    {
        var dlg = new ConfirmationDialog
        {
            Title = UiTextEn.Translate("Business tax (%)"),
            OkButtonText = UiTextEn.Translate("Confirm"),
            CancelButtonText = UiTextEn.Translate("Cancel"),
            Exclusive = true,
        };
        var ed = new LineEdit { Text = $"{GuildState.BusinessTax}" };
        dlg.AddChild(ed);
        dlg.RegisterTextEnter(ed);
        dlg.Confirmed += () =>
        {
            if (int.TryParse(ed.Text, out var v))
                FireWorld(WorldFamilyOp.Of(FamilyOps.SetBusinessTax, v));
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    /// <summary>familyfindingwind — liste des guildes qui recrutent
    /// (0x4f) + Joinbutton (0x50), FindingTownbutton → Town List.</summary>
    private void ShowFindWindow()
    {
        if (_findWin == null)
        {
            _findWin = _hud?.Build("familyfindingwind.layout");
            if (_findWin == null) return;
            AddChild(_findWin);
            _findWin.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            DragHelper.EnsureTitleBar(_findWin,
                UiTextEn.Translate("Find a guild"));
            var pane = _hud?.Build("familyfindinginfo.layout");
            if (pane != null &&
                FindLeaf<TabContainer>(_findWin, "TabControl")
                    is { } t)
            {
                pane.Name = UiTextEn.Translate("Guilds");
                t.AddChild(pane);
                var join = FindLeaf<BaseButton>(pane, "Joinbutton");
                if (join != null) join.Pressed += JoinSelected;
                var close = FindLeaf<BaseButton>(pane, "Closebutton");
                if (close != null)
                    close.Pressed += () => _findWin.Visible = false;
                var town = FindLeaf<BaseButton>(pane,
                    "FindingTownbutton");
                if (town != null) town.Pressed += () =>
                {
                    if (GetTree().CurrentScene.FindChild(
                            "TownListWindow", true, false)
                            is TownListWindow tl)
                        tl.Open();
                };
                // Pagination native : 20/page côté monde, page 0-based.
                void Page(int p) => FireWorld(
                    WorldFamilyOp.Of(FamilyOps.RecruitPage, p));
                int Last() => Math.Max(0,
                    (GuildState.RecruitTotal + 19) / 20 - 1);
                if (FindLeaf<BaseButton>(pane, "mostbackBtn") is { } mb)
                    mb.Pressed += () => Page(0);
                if (FindLeaf<BaseButton>(pane, "backBtn") is { } bb)
                    bb.Pressed += () =>
                        Page(Math.Max(0, GuildState.RecruitPage - 1));
                if (FindLeaf<BaseButton>(pane, "nextBtn") is { } nb)
                    nb.Pressed += () =>
                        Page(Math.Min(Last(), GuildState.RecruitPage + 1));
                if (FindLeaf<BaseButton>(pane, "mostnextBtn") is { } mn)
                    mn.Pressed += () => Page(Last());
                _findPage = FindLeaf<Label>(pane, "pageText");
            }
            var mcl = FindLeaf<MclList>(pane ?? _findWin, "MultiList");
            if (mcl != null)
            {
                mcl.SetColumns(new MclColumn[]
                {
                    new(UiTextEn.Translate("Guild"), 0f, 150f, 0),
                    new(UiTextEn.Translate("Leader"), 0f, 120f, 1),
                    new(UiTextEn.Translate("Members"), 0f, 70f, 2),
                    new("Lv", 0f, 40f, 3),
                });
                _findMcl = mcl;
            }
        }
        foreach (var d in _dlgs) d.Visible = false;
        HudWindows.Open(_findWin);
        _ = CenterDlg(_findWin);
        FireWorld(WorldFamilyOp.Of(FamilyOps.RecruitPage, 0));
    }
    private MclList? _findMcl;
    private Label? _findPage;
    private readonly List<GuildRecruitEntry> _findEntries = new();
    private Control? _appDlg;
    private int _appFamilyId;

    /// <summary>Candidature à une guilde : petit dialogue commun
    /// (<c>commondlg</c>, message libre) → op 0x50
    /// <c>[a=familyId][s1=message]</c> — le message est porté par s1
    /// comme le client natif (le serveur l'ignore pour l'instant).
    /// (<c>familyappwnd</c> est la fiche de fondation de ville, pas la
    /// candidature.)</summary>
    private void JoinSelected()
    {
        var r = _findMcl?.SelectedRow ?? -1;
        if (r < 0 || r >= _findEntries.Count) return;
        var e = _findEntries[r];
        _appFamilyId = e.FamilyId;
        if (_appDlg == null)
        {
            _appDlg = BuildDlg("commondlg.layout", "application",
                "Guild application");
            if (FindLeaf<BaseButton>(_appDlg, "OKBtn") is { } ok)
                ok.Pressed += SendApp;
        }
        SetT(_appDlg, "MsgText", $"Candidature à <{e.Name}> — message :");
        if (FindLeaf<LineEdit>(_appDlg, "EditBox") is { } ed) ed.Text = "";
        OpenDlg(_appDlg!);
    }

    private void SendApp()
    {
        var msg = FindLeaf<LineEdit>(_appDlg!, "EditBox")?.Text ?? "";
        FireWorld(WorldFamilyOp.OfStr(FamilyOps.RecruitJoin, msg,
            a: _appFamilyId));
        _appDlg!.Visible = false;
    }

    /// <summary>Dialogues natifs construits (un seul visible à la
    /// fois : <see cref="OpenDlg"/>).</summary>
    private readonly List<Control> _dlgs = new();

    /// <summary>Construit un dialogue natif : titre FR, libellés FR
    /// des boutons (absents des layouts : posés au runtime par le
    /// client natif), contrôles interactifs remontés au-dessus du
    /// cadre (AlwaysOnTop CEGUI), enregistré pour Échap / la croix.
    /// Titre + message passent par <see cref="UiTextEn.Translate"/>.</summary>
    private Control BuildDlg(string layout, string key,
        string? title = null, string? msg = null)
    {
        var dlg = _hud?.Build(layout) ?? new Panel();
        dlg.Name = $"GuildDlg_{key}";
        AddChild(dlg);
        dlg.Visible = false;
        DragHelper.EnsureTitleBar(dlg, title ?? key);
        HudWindows.Register(dlg);
        if (msg != null) SetT(dlg, "MsgText", UiTextEn.Translate(msg));
        // CEGUI AlwaysOnTop : dans Godot, l'ordre des enfants décide —
        // le cadre « static/border » (déclaré après) recouvrait le
        // texte, les champs et les boutons.
        foreach (var n in new[] { "MsgText", "EditBox", "EditBox1",
                     "EditBox2", "EditBox3", "EditBox4", "EditBox5",
                     "EditBox6", "EditBox7", "MultEdit", "GoldImg1",
                     "GoldImg2", "Image1", "Image2", "Image3", "Image4",
                     "Image5", "Image6", "Image7", "OKBtn", "CancelBtn",
                     "CloseBtn" })
            if (dlg.FindChild(n, true, false) is Control c &&
                c.GetParent() is { } par)
                par.MoveChild(c, par.GetChildCount() - 1);
        // Boutons : OK = Confirmer, Close = Annuler. CancelBtn est le
        // doublon recouvrant du CloseBtn (même rectangle, layouts
        // communs) : caché.
        SetBtnText(dlg, "OKBtn", UiTextEn.Translate("Confirm"));
        SetBtnText(dlg, "CloseBtn", UiTextEn.Translate("Cancel"));
        SetBtnText(dlg, "CancelBtn", UiTextEn.Translate("Cancel"));
        if (FindLeaf<BaseButton>(dlg, "CancelBtn") is { } dup &&
            FindLeaf<BaseButton>(dlg, "CloseBtn") != null)
            dup.Visible = false;
        DragHelper.Persist(dlg, $"guild-{key}");
        var close = FindLeaf<BaseButton>(dlg, "CloseBtn")
            ?? FindLeaf<BaseButton>(dlg, "CancelBtn");
        if (close != null)
            close.Pressed += () => dlg.Visible = false;
        _dlgs.Add(dlg);
        return dlg;
    }

    /// <summary>Affiche un dialogue en exclusif (les autres
    /// sous-dialogues se ferment), au premier plan (Échap le
    /// ferme), centré dans la fenêtre de jeu.</summary>
    private void OpenDlg(Control dlg)
    {
        foreach (var d in _dlgs)
            if (d != dlg && GodotObject.IsInstanceValid(d))
                d.Visible = false;
        HudWindows.Open(dlg);
        _ = CenterDlg(dlg);
        GD.Print($"[guilddlg] ouvre {dlg.Name}");
    }

    private static void SetDlgTitle(Control dlg, string text)
    {
        var bar = dlg.FindChildren("*", "", true, false)
            .OfType<CeguiTitleBar>().FirstOrDefault();
        if (bar?.FindChildren("*", "Label", true, false)
                .OfType<Label>().FirstOrDefault() is { } l)
            l.Text = UiTextEn.Translate(text);
    }

    private async Task CenterDlg(Control dlg)
    {
        // Taille/ancres valides à la frame suivante ; après le
        // recadrage différé de HudWindows.Register.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(dlg) || !dlg.Visible) return;
        var vp = dlg.GetViewport().GetVisibleRect().Size;
        dlg.GlobalPosition = ((vp - dlg.Size) / 2f).Floor();
    }

    private static int GetDlgInt(Control dlg, string leaf) =>
        int.TryParse(FindLeaf<LineEdit>(dlg, leaf)?.Text.Trim(),
            out var v) ? v : 0;

    // ------------------------------------------------------------------
    // Vue « sans guilde » (création 49 + recrutement 0x4f/0x50)
    // ------------------------------------------------------------------

    private LineEdit? _createEdit;

    private void BuildNoGuildView()
    {
        _noGuild = new Panel();
        _noGuild.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.122f, 0.117f, 0.104f, 0.96f),
            BorderColor = new Color(0.4f, 0.5f, 0.75f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        HudRoot.Pin(_noGuild, 0.5f, 0.5f, 0.5f, 0.5f,
            -200, -80, 200, 80);
        _noGuild.Visible = false;
        AddChild(_noGuild);
        var v = new VBoxContainer();
        v.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        v.OffsetLeft = 10; v.OffsetRight = -10;
        v.OffsetTop = 10; v.OffsetBottom = -10;
        _noGuild.AddChild(v);
        v.AddChild(new Label
        {
            Text = UiTextEn.Translate("You are not in a guild."),
        });
        var row = new HBoxContainer();
        _createEdit = new LineEdit
        {
            Name = "GuildNameEdit",
            PlaceholderText = UiTextEn.Translate(
                "guild name… (5 gold)"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(_createEdit);
        var c = new Button { Name = "GuildCreateBtn",
            Text = UiTextEn.Translate("Create") };
        c.Pressed += CreateGuild;
        row.AddChild(c);
        var f = new Button { Name = "FindGuildBtn",
            Text = UiTextEn.Translate("Find guild") };
        f.Pressed += ShowFindWindow;
        row.AddChild(f);
        v.AddChild(row);
    }

    /// <summary>Panel de repli si les layouts uidata sont absents.</summary>
    private void BuildFallbackPanel()
    {
        _win = new Panel { Name = "GuildPanel" };
        HudRoot.Pin(_win, 0.5f, 0.5f, 0.5f, 0.5f, -390, -350, 390, 350);
        _win.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.122f, 0.117f, 0.104f, 0.96f),
            BorderColor = new Color(0.4f, 0.5f, 0.75f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        _win.Visible = false;
        AddChild(_win);
        DragHelper.EnsureTitleBar(_win, UiTextEn.Translate("Guild"));
        DragHelper.Persist(_win, "guild");
        _tabs = new TabContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _tabs.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _tabs.OffsetTop = 30;
        _win.AddChild(_tabs);
    }

    /// <summary>Popup d'invitation guilde (48) — accept 0x03 /
    /// reject 0x04.</summary>
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
        HudRoot.Pin(_invitePopup, 0.5f, 0f, 0.5f, 0f,
            -230, 90, 230, 150);
        _invitePopup.Visible = false;
        var h = new HBoxContainer();
        h.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        h.Alignment = BoxContainer.AlignmentMode.Center;
        _inviteLabel = new Label();
        h.AddChild(_inviteLabel);
        var ok = new Button { Text = UiTextEn.Translate("Accept") };
        ok.Pressed += () => AnswerInvite(1);
        h.AddChild(ok);
        var no = new Button { Text = UiTextEn.Translate("Decline") };
        no.Pressed += () => AnswerInvite(0);
        h.AddChild(no);
        _invitePopup.AddChild(h);
        AddChild(_invitePopup);
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
        if (_win != null)
        {
            _win.Visible = !_win.Visible;
            if (_win.Visible)
            {
                SelectGuildTab();
                RequestData();
            }
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Exposé pour <c>--hud-open=guild</c>/menu : force la
    /// visibilité + rafraîchit les données.</summary>
    /// <summary>Onglet Guilde (0) à chaque ouverture : immédiat puis
    /// différé (un rebuild/refresh de layout le ramenait sur Ville).</summary>
    private void SelectGuildTab()
    {
        if (_tabs == null) return;
        _tabs.CurrentTab = 0;
        // Les refresh de layout (Visible=true sur un volet) rebasculent
        // l'onglet : on le maintient 1,5 s après l'ouverture.
        _forceTabUntilMs = Time.GetTicksMsec() + 1500;
    }

    private ulong _forceTabUntilMs;
    private bool _winWasOpen;

    public void Open()
    {
        if (_win == null) return;
        _win.Visible = true;
        SelectGuildTab();
        RequestData();
    }

    public override void _Process(double delta)
    {
        // Fenêtre mère fermée (Échap, croix, G) : ses sous-dialogues
        // se ferment avec elle (ville/candidature restent : ouverts
        // par un PNJ ou la recherche, indépendants de la fiche).
        var winOpen = _win?.Visible == true;
        if (_winWasOpen && !winOpen)
            foreach (var d in _dlgs)
                if (d != _dlgTownCreate && d != _appDlg) d.Visible = false;
        _winWasOpen = winOpen;
        if (_forceTabUntilMs != 0 && _tabs != null)
        {
            if (Time.GetTicksMsec() < _forceTabUntilMs)
            {
                if (_tabs.CurrentTab != 0) _tabs.CurrentTab = 0;
            }
            else _forceTabUntilMs = 0;
        }
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
        if (_win?.Visible == true &&
            Time.GetTicksMsec() >= _nextTerritoryPollMs)
        {
            _nextTerritoryPollMs = Time.GetTicksMsec() + 500;
            UpdateTerritoryBtn();
        }
    }

    private ulong _nextTerritoryPollMs;

    public override void _ExitTree()
    {
        GuildState.Changed -= Refresh;
        MiscPackets.Received -= OnMisc;
        base._ExitTree();
    }

    private void BindSession(EdenSession s)
    {
        _session = s;
        RequestData();
    }

    /// <summary>Lie le 346 zone (self change) et demande un refresh
    /// immédiat via le 81.</summary>
    private void BindZone(EdenSession s)
    {
        _zoneBound = true;
        var selfId = SessionCarry.Character?.Id ?? 0;
        s.OnZone(346, r =>
        {
            var m = new ZoneClientChangeFamily();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                var wasIn = GuildState.InGuild;
                GuildState.ApplyZoneChange(m, selfId);
                if (!wasIn && GuildState.InGuild)
                    RequestData();
            });
        });
        if (selfId > 0)
            FireZone(new ZoneUpdateFamily { CharId = (uint)selfId });
        GD.Print("[guild] bind zone 346 + requête 81/10 envoyées");
    }

    /// <summary>Requêtes de données : monde 10 + zone 81 + 132
    /// (données instance pour l'onglet Ville).</summary>
    private void RequestData()
    {
        FireWorld(new WorldQueryFamilyInfo { Value = 1 });
        var selfId = SessionCarry.Character?.Id ?? 0;
        if (selfId > 0)
            FireZone(new ZoneUpdateFamily { CharId = (uint)selfId });
        if (GuildState.HouseOpen > 0)
            FireZone(new ZoneServerFamilyInstanceGetData());
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

    private void CreateGuild()
    {
        var name = _createEdit?.Text.Trim() ?? "";
        if (name.Length == 0) return;
        FireZone(new ZoneFamilyCreate { Value = name });
        _hud?.Chat?.AddSystem(
            $"Création de la guilde «{name}»… (5 po)");
    }

    private void WhisperTo(int charId)
    {
        if (!GuildState.Members.TryGetValue(charId, out var m)) return;
        _hud?.Chat?.StartWhisper(m.Name);
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
        if (_noGuild != null)
            _noGuild.Visible = _win?.Visible == true && !inGuild;
        if (_tabs != null)
            foreach (var t in _tabs.GetChildren())
                if (t is Control c) c.Visible = inGuild;
        RenderInfo();
        RenderMembers();
        RenderBoard();
        RenderOrder();
        RenderFind();
        if (_invitePopup != null && GuildState.PendingInvite is { } inv)
        {
            _inviteLabel!.Text =
                $"{inv.Inviter} vous invite à rejoindre <{inv.Family}>";
            _invitePopup.Visible = true;
        }
        else if (_invitePopup != null)
            _invitePopup.Visible = false;
    }

    private static void SetBtnText(Control pane, string leaf, string text)
    {
        if (FindLeaf<Button>(pane, leaf) is { } b) b.Text = text;
    }

    /// <summary>Confirmation modale avant une action destructive
    /// (quitter / dissoudre la guilde).</summary>
    private void Confirm(string msg, Action onYes)
    {
        var dlg = new ConfirmationDialog
        {
            DialogText = msg,
            Title = UiTextEn.Translate("Confirmation"),
            OkButtonText = UiTextEn.Translate("Confirm"),
            CancelButtonText = UiTextEn.Translate("Cancel"),
            Exclusive = true,
        };
        dlg.Confirmed += () => { onYes(); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void SetT(Control? pane, string leaf, string text)
    {
        if (pane == null) return;
        if (FindLeaf<Label>(pane, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(pane, leaf) is { } r)
            r.Text = text;
    }

    private void RenderInfo()
    {
        if (_infoPane == null || !GuildState.InGuild) return;
        var leaderName = GuildState.Members
            .TryGetValue(GuildState.LeaderId, out var lm)
            ? lm.Name : $"#{GuildState.LeaderId}";
        SetT(_infoPane, "FamilyNameText", $"<{GuildState.Name}>");
        UpdateTerritoryBtn();
        // Texte dynamique : écrit en français directement (Translate
        // ne reconnaît pas une chaîne déjà interpolée).
        var online = GuildState.Members.Values.Count(x => x.Online != 0);
        SetT(_adminPane, "FamilyName", $"<{GuildState.Name}>");
        SetT(_infoPane, "GoodCommentText", "");
        SetT(_msgPane, "Space", $"Espace restant : " +
            $"{Math.Max(0, 99 - GuildState.Schedule.Count)} messages");
        SetT(_adminPane, "TaxText",
            $"Taxe commerciale : {GuildState.BusinessTax} %");
        SetT(_infoPane, "FamilyLevel", $"Niveau : {GuildState.Level}");
        SetT(_infoPane, "FamilyLeader", $"Chef : {leaderName}");
        SetT(_infoPane, "FamilyMember",
            $"Membres : {GuildState.Members.Count} ({online} en ligne)");
        SetT(_infoPane, "Reputation", $"Honneur : {GuildState.Honor}");
        // Trésorerie : 1000 argent = 1 or.
        SetT(_infoPane, "GoldValue", $"{GuildState.Treasury / 1000}");
        SetT(_infoPane, "SilverValue",
            $"{GuildState.Treasury % 1000}");
        SetT(_infoPane, "MemberTax",
            $"Taxe des membres : {GuildState.TaxRate} %");
        SetT(_infoPane, "BusinessTax",
            $"Taxe commerciale : {GuildState.BusinessTax} %");
        // Bulletin : l'annonce vit dans l'éditeur multiligne natif
        // (BulletinEdit) ; « Bulletin » n'est que son titre.
        if (FindLeaf<TextEdit>(_infoPane, "BulletinEdit") is { } be)
        {
            be.Text = GuildState.Info.Length > 0
                ? GuildState.Info : "Aucune annonce.";
            be.Editable = false;   // édition via NoticeBtn (op 0x0a)
        }
        else if (FindLeaf<RichTextLabel>(_infoPane, "BulletinEdit")
                     is { } br)
            br.Text = GuildState.Info;
    }

    /// <summary>Rang affiché : leader via leader_id ; titre cadre
    /// (rangs 0-7 → Titles[rank+2]) ; self via le 346 ; sinon
    /// Member — le wire ne porte pas le rang des autres.</summary>
    private static string RankLabel(GuildMember m, int selfId)
    {
        if (m.CharId == GuildState.LeaderId)
            return GuildState.Titles[1].Length > 0
                ? GuildState.Titles[1] : UiTextEn.Translate("Leader");
        if (m.CharId == selfId && GuildState.SelfRank >= 0)
        {
            var t = GuildState.SelfRank + 2;
            return t < GuildState.Titles.Length &&
                   GuildState.Titles[t].Length > 0
                ? GuildState.Titles[t] : RankName(GuildState.SelfRank);
        }
        return GuildState.Titles[0].Length > 0
            ? GuildState.Titles[0] : UiTextEn.Translate("Member");
    }

    /// <summary>Nom d'un rang cadre 0-7 : titre défini par le chef
    /// (Titles[rang+2]) sinon « Grade N ».</summary>
    private static string RankName(int rank)
    {
        var t = rank + 2;
        var titles = GuildState.Titles;
        return titles != null && t >= 0 && t < titles.Length &&
               !string.IsNullOrEmpty(titles[t])
            ? titles[t] : $"Rang {rank}";
    }

    private void RenderMembers()
    {
        if (_memberMcl == null) return;
        var selfId = SessionCarry.Character?.Id ?? 0;
        // « Afficher les hors-ligne » : coché = tout montrer ; décoché
        // = seulement les connectés (le chef/soi reste toujours listé).
        var onlineOnly =
            FindLeaf<BaseButton>(_infoPane!, "OnlineBtn")
                ?.ButtonPressed != true;
        _memberMcl.ClearRows();
        _memberOrder.Clear();
        foreach (var m in GuildState.Members.Values
                     .OrderByDescending(x => x.Online)
                     .ThenByDescending(x => x.CharId == GuildState.LeaderId)
                     .ThenBy(x => x.Name))
        {
            if (onlineOnly && m.Online == 0 && m.CharId != selfId)
                continue;
            var cls = TextResolver.ClassName(m.ClassId)
                ?? $"C{m.ClassId}";
            var row = _memberMcl.AddRow(new[]
            {
                $"{(m.Online != 0 ? "●" : "○")} {m.Name}",
                cls, $"{m.Level}", RankLabel(m, selfId),
                m.Contribution > 0 ? $"{m.Contribution}" : "",
            });
            if (m.Online == 0)
                for (var ci = 0; ci < 5; ci++)
                    _memberMcl.SetCellColor(row, ci,
                        new Color(1, 1, 1, 0.45f));
            _memberOrder.Add(m.CharId);
        }
    }

    private void RenderBoard()
    {
        if (_boardMcl == null) return;
        _boardMcl.ClearRows();
        foreach (var m in GuildState.Schedule)
        {
            var date = m.CreateDate > 0
                ? DateTimeOffset.FromUnixTimeSeconds(m.CreateDate)
                    .ToString("MM-dd") : "??";
            _boardMcl.AddRow(new[] { date, m.Title, m.PlayerName });
        }
    }

    /// <summary>Onglet Rangs — EmText1-7 = titres des rangs
    /// (Titles[2..8]).</summary>
    private void RenderOrder()
    {
        if (_orderPane == null) return;
        for (var i = 0; i < 7; i++)
            SetT(_orderPane, $"EmText{i + 1}_Text", RankName(i));
        // Titres de section « x… » sans donnée serveur (classements
        // mensuels de guilde) : on vide le gabarit « 第99名 ».
        SetT(_orderPane, "TitleText1", "");
        SetT(_orderPane, "TitleText2", "");
        // Historique de guilde (68) dans EternalListbox.
        if (FindLeaf<ItemList>(_orderPane, "EternalListbox") is { } hist)
        {
            hist.Clear();
            foreach (var e in GuildState.Logs)
            {
                var when = e.Date > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(e.Date)
                        .LocalDateTime.ToString("dd/MM HH:mm") : "";
                var what = $"{e.Str1} {e.Str2}".Trim();
                hist.AddItem(
                    $"{when} {(what.Length > 0 ? what : "#" + e.Type)}"
                        .Trim());
            }
        }
    }

    /// <summary>Onglet Ville — bâtiments du hall (487).</summary>
    private void RenderAdmin(ZoneClientFamilyInstanceData d)
    {
        if (_adminPane == null) return;
        SetT(_adminPane, "FamilyName", $"<{d.FamilyName}>");
        _adminBuildings.Clear();
        _adminBuildings.AddRange(d.Buildings);
        for (var i = 0; i < 3; i++)
        {
            var slot = ItemSlotCell.Find(_adminPane,
                $"Root/FamilyAdministrationWnd/Block2/House/Slot{i}");
            if (i >= d.Buildings.Count)
            {
                slot?.Clear();
                SetT(_adminPane, $"Name{i}", "");
                SetT(_adminPane, $"Durability{i}", "");
                SetT(_adminPane, $"ExpText{i}", "");
                if (FindLeaf<Control>(_adminPane, $"ExpBar{i}") is { } eb0)
                    eb0.Visible = false;
                continue;
            }
            if (FindLeaf<Control>(_adminPane, $"ExpBar{i}") is { } eb1)
                eb1.Visible = true;
            var b = d.Buildings[i];
            slot?.SetItemId(b.ItemTid, 1);
            SetT(_adminPane, $"Name{i}",
                $"L{b.Level:D2}." + (TextResolver.ItemName(b.ItemTid)
                    ?? $"#{b.ItemTid}"));
            SetT(_adminPane, $"Durability{i}",
                $"Durabilité : {b.Durability}");
            SetT(_adminPane, $"ExpText{i}", $"EXP {b.Exp}");
            if (FindLeaf<ProgressBar>(_adminPane, $"ExpBar{i}")
                    is { } bar)
            {
                bar.MaxValue = Math.Max(b.Exp, 1);
                bar.Value = b.Exp;
            }
        }
        FillBuildingList(d);
        Callable.From(FitAdminTexts).CallDeferred();
    }

    private VBoxContainer? _adminList;

    /// <summary>Block1/DataBorder/Pane : liste texte des bâtiments.</summary>
    private void FillBuildingList(ZoneClientFamilyInstanceData d)
    {
        if (_adminPane == null ||
            FindLeaf<ScrollContainer>(_adminPane, "DataBorder_Pane") is not { } pane)
            return;
        _adminList ??= new VBoxContainer
        {
            Name = "__buildings",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        if (_adminList.GetParent() == null) pane.AddChild(_adminList);
        foreach (var c in _adminList.GetChildren()) c.QueueFree();
        foreach (var b in d.Buildings)
            _adminList.AddChild(new Label
            {
                Text = $"Niv. {b.Level} " + (TextResolver.ItemName(b.ItemTid)
                    ?? $"#{b.ItemTid}"),
            });
    }

    private void RenderFind()
    {
        if (_findMcl == null) return;
        _findMcl.ClearRows();
        _findEntries.Clear();
        foreach (var e in GuildState.RecruitEntries)
        {
            _findMcl.AddRow(new[]
            {
                $"<{e.Name}>", e.LeaderName, $"{e.MemberCount}",
                $"{e.Level}",
            });
            _findEntries.Add(e);
        }
        if (_findPage != null)
        {
            var pages = Math.Max(1,
                (GuildState.RecruitTotal + 19) / 20);
            _findPage.Text =
                $"{GuildState.RecruitPage + 1} / {pages}";
        }
    }

    // ------------------------------------------------------------------
    // Helpers layout
    // ------------------------------------------------------------------

    private static T? FindLeaf<T>(Node? root, string leaf)
        where T : class
    {
        // Parcours manuel + test de type C# : FindChildren(typeName)
        // ne reconnaît pas les classes script non [GlobalClass]
        // (MclList…) → la liste des membres restait introuvable/vide.
        if (root == null) return null;
        foreach (var c in root.GetChildren())
        {
            if (c is T t && c.Name.ToString().EndsWith(leaf)) return t;
            if (FindLeaf<T>(c, leaf) is { } deep) return deep;
        }
        return null;
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
        var m = new WorldFamilyInfo
        {
            Type = 0, WorldServerId = 1, FamilyId = 900001,
            FamilyName = "EdenDev", Level = 7,
            CreateTime = 1_700_000_000, Exp = 4200,
            LeaderId = 200000001, Exploit = 9000, Emblem = 3,
            Uniform = -1,
            Info = "Bienvenue chez EdenDev — annonce de test.",
            Treasury = 500000, TaxRate = 5, BusinessTax = 10,
            HouseOpen = 502, Recruit = 0, Honor = 0,
        };
        m.Titles.AddRange(new[]
        {
            "Membre", "Chef", "Aîné", "Élite", "Vétéran",
            "Officier", "Conseiller", "Recrue", "Novice",
        });
        GuildState.ApplyInfo(m);
        var chunk = new WorldFamilyInfo { Type = 8 };
        chunk.Members.Add(FakeMember(200000001, "Devin", 1, 35, 1));
        chunk.Members.Add(FakeMember(200000002, "Aria", 4, 28, 0));
        GuildState.ApplyInfo(chunk);
    }

    /// <summary>Écrit un record membre type-8 factice (layout
    /// 51 membre : [i32 char][str name][u8 class][u8 rank]
    /// [s16 node][s16 lv][s16 clv][i32 contrib][u8 online]
    /// [str comment][str fcomment]).</summary>
    private static GuildMember FakeMember(int id, string name,
        byte cls, short lv, int online)
    {
        var m = new GuildMember { CharId = id };
        m.Name = name; m.ClassId = cls; m.Level = lv;
        m.ClassLevel = lv; m.Online = (byte)online;
        return m;
    }
}
