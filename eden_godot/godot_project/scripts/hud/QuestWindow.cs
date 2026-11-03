using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Journal de quêtes « Quest » (ref QuestLog.png) — reconstruit sur
/// le layout natif <c>MissionLog.layout</c> (DefaultWindow 634×570 :
/// compteur « Quests: n/25 », case « Track All », arbre gauche, zone
/// détail + ScrollablePane d'objectifs, boutons Share/GiveUp/Exit).
/// Le DefaultWindow natif n'a pas de chrome : il est monté dans une
/// coquille fenêtrée au gabarit de la ref (645×620, titre « Quest »,
/// onglets Quest|Legend, cadre natif, drag, registre
/// <see cref="HudWindows"/>). Repli : habillage
/// <see cref="EeSkin"/> (<see cref="BuildFallback"/>) quand uidata
/// est absent.
///
/// Données : les mêmes NCID 348/391 que le tracker
/// (<see cref="ZoneMissionStates"/>) — titres/zone/description EN via
/// <see cref="TextResolver"/> (t_mission.ini col1/2/3). L'arbre groupe
/// « In Progress » (FMS 3/4 + registre) et « Completed » (FMS 1/5) ;
/// la sélection remplit le panneau de droite. Drop = abandon
/// (C→S 51) ; Share partage (C→S 87 op 6) ; Legend non câblé.
/// </summary>
public partial class QuestWindow : Control
{
    /// <summary>Émis quand le joueur demande l'abandon de la quête
    /// sélectionnée (bouton « GiveUpBtn » natif) → C→S 51.</summary>
    public event Action<int>? GiveUpRequested;

    /// <summary>Clic « Share » (quête en cours, en équipe) — le pont
    /// émet C→S 87 op 6 (<see cref="MissionListOps.ShareStart"/>).</summary>
    public event Action<int>? ShareRequested;

    /// <summary>Émis quand la liste de suivi change (shift+clic sur
    /// une quête ou « Track All ») → C→S 186 + miroir tracker. Le
    /// serveur persiste sans écho ; le 348 du login renvoie l'état.</summary>
    public event Action<List<int>>? TraceChanged;

    /// <summary>Envoi zone (branché par HudRoot) — NCID 160 du bouton
    /// « Go » quand « Use Portal Stone » est cochée.</summary>
    public Func<NetMessage, Task>? Send { get; set; }

    /// <summary>Clic « Go » sans pierre — auto-follow vers la cible
    /// (branché sur <see cref="AutoRoute.ToMissionTarget"/>).</summary>
    public event Action<int, string>? ObjectiveClicked;

    private Control? _window;
    private Tree? _tree;
    private Label? _title, _detail, _status, _portalUse;
    /// <summary>« Track All » : Label cliquable au repli, CheckBox
    /// native (<c>Control/traceBtn</c>) sur le layout.</summary>
    private Control? _trackAll;
    /// <summary>StoryList native (détail — ItemList texte plein,
    /// null au repli où <see cref="_detail"/> tient lieu).</summary>
    private ItemList? _story;
    /// <summary>Contenu injecté du ScrollablePane natif (objectifs +
    /// portal/Go + rangée de récompenses), null au repli.</summary>
    private VBoxContainer? _objBox;
    /// <summary>true quand la fenêtre est le layout natif — les
    /// panneaux DejaVu sont parchemin clair → encre sombre (le repli
    /// pose des encres claires sur fond sombre).</summary>
    private bool _native;
    private int _selectedMid;
    private bool _usePortal;

    /// <summary>Dernier état connu : mid → état FMS (1=done, 3/4=en
    /// cours, 5=checked) — reconstitué du 348 + updates 391.</summary>
    private readonly Dictionary<int, int> _states = new();

    /// <summary>Quêtes suivies (slots Trace du 348 + toggles locaux) —
    /// max 10 slots natifs.</summary>
    private readonly HashSet<int> _traced = new();

    /// <summary>Panneau visible (registre <see cref="HudWindows"/>).</summary>
    public Control? Window => _window;
    private Label? _count;
    private RichTextLabel? _objectives;
    private HBoxContainer? _rewardRow;
    /// <summary>Cibles de la quête affichée : (id natif, type, libellé) — index = lien [Go].</summary>
    private readonly List<(int ObjId, MissionObjectives.Kind Kind, string Label)> _goTargets = new();

    /// <summary>Encre sombre des textes posés sur le parchemin natif.</summary>
    private static readonly Color Ink = Color.Color8(30, 30, 28);
    /// <summary>Encre claire des textes posés sur les panneaux sombres
    /// du layout natif (compteur, arbre, titre).</summary>
    private static readonly Color Light = Color.Color8(228, 228, 236);

    public void Build(HudRoot hud)
    {
        if (!TryBuildNative(hud))
            BuildFallback(hud);
    }

    // ------------------- layout natif MissionLog -------------------

    /// <summary>Monte <c>MissionLog.layout</c> via le pipeline CEGUI
    /// et l'habille : compteur, case Track All, arbre, hint shift+clic,
    /// titre/état, StoryList, ScrollablePane d'objectifs, boutons
    /// Share/GiveUp/Exit. Retourne false (repli sur
    /// <see cref="BuildFallback"/>) si le layout manque ou si un
    /// widget attendu n'a pas le type Godot prévu.</summary>
    private bool TryBuildNative(HudRoot hud)
    {
        var nat = hud.Build("MissionLog.layout");
        if (nat == null) return false;
        var tree = TopLeftVitals.Find<Tree>(nat, "TreeStatic_tree");
        var count = TopLeftVitals.Find<Label>(nat, "Control_Text");
        var track = TopLeftVitals.Find<CheckBox>(nat, "Control_traceBtn");
        var title = TopLeftVitals.Find<Label>(nat, "Titletext");
        var status = TopLeftVitals.Find<Label>(nat, "statusText");
        var story = TopLeftVitals.Find<ItemList>(nat, "StoryList");
        var pane = TopLeftVitals.Find<ScrollContainer>(nat,
            "TargetBoard_pane");
        if (tree == null || count == null || track == null ||
            title == null || status == null || story == null ||
            pane == null)
        {
            GD.PrintErr("[questlog] MissionLog : widgets attendus " +
                "absents → habillage manuel");
            nat.Free();
            return false;
        }
        _native = true;
        _tree = tree;
        _count = count;
        _trackAll = track;
        _title = title;
        _status = status;
        _story = story;

        // Coquille au gabarit de la ref QuestLog.png (fenêtre
        // « Quest » 645×620 à 144,40) : le DefaultWindow natif 634×570
        // est la zone cliente — titre + onglets + cadre viennent du
        // chrome natif posé par EnsureTitleBar.
        var shell = new Panel { Name = "QuestWnd" };
        AddChild(shell);
        _window = shell;
        HudRoot.Pin(shell, 0, 0, 0, 0, 144, 40, 789, 660);
        DragHelper.EnsureTitleBar(shell, UiTextEn.Translate("Quest"));
        DragHelper.FillClientArea(shell, EeSkin.WindowBody);
        BuildTabs(shell, hud);
        shell.AddChild(nat);
        HudRoot.Pin(nat, 0, 0, 0, 0, 4, 44, 638, 614);
        DragHelper.Persist(shell, "questlog");

        // Bandeau : compteur, « Track All », bouton aide (masqué —
        // pas de page d'aide câblée), astuce shift+clic.
        _count.AddThemeFontSizeOverride("font_size", 12);
        _count.AddThemeColorOverride("font_color", Light);
        track.AddThemeFontSizeOverride("font_size", 12);
        track.AddThemeIconOverride("unchecked", EeSkin.Diamond(false));
        track.AddThemeIconOverride("checked", EeSkin.Diamond(true));
        track.AddThemeIconOverride("radio_unchecked", EeSkin.Diamond(false));
        track.AddThemeIconOverride("radio_checked", EeSkin.Diamond(true));
        track.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        track.Text = UiTextEn.Translate("Track All");
        track.TooltipText =
            UiTextEn.Translate("Track every quest in progress");
        foreach (var tc in new[] { "font_color", "font_hover_color",
                     "font_pressed_color", "font_hover_pressed_color" })
            track.AddThemeColorOverride(tc, Light);
        // La case reflète « au moins une quête suivie » (EmitTrace) ;
        // tout basculement joue le même rôle que le clic sur le
        // libellé du repli → ToggleTrackAll.
        track.Toggled += _ => ToggleTrackAll();
        if (TopLeftVitals.Find<BaseButton>(nat, "Control_HelpBtn")
                is { } hb)
            hb.Visible = false;
        if (TopLeftVitals.Find<Label>(nat, "TreeStatic_TextDetail")
                is { } hint)
        {
            hint.Text = UiTextEn.Translate(
                "Shift+Left-click to track a quest");
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.AddThemeFontSizeOverride("font_size", 11);
            hint.AddThemeColorOverride("font_color", Ink);
        }

        // Arbre : mêmes signaux + habillage parchemin que le repli.
        _tree.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _tree.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _tree.AddThemeStyleboxOverride("selected", new StyleBoxFlat
            { BgColor = Color.Color8(52, 84, 150) });
        _tree.AddThemeStyleboxOverride("selected_focus", new StyleBoxFlat
            { BgColor = Color.Color8(52, 84, 150) });
        // Panneau sombre natif : encre claire (le parchemin du repli
        // garde l'encre sombre).
        _tree.AddThemeColorOverride("font_color", Light);
        _tree.AddThemeColorOverride("font_selected_color", Colors.White);
        _tree.AddThemeFontSizeOverride("font_size", 12);
        _tree.ItemSelected += OnSelected;
        // Shift+clic = bascule « suivi » de la quête (hint natif sous
        // l'arbre) → C→S 186 ; le losange orange marque les suivies.
        _tree.ItemMouseSelected += OnTreeMouse;

        // Détail.
        _story.AddThemeFontSizeOverride("font_size", 12);
        _story.AddThemeColorOverride("font_color", Ink);
        _title.ClipText = true;
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        // Volet objectifs : le ScrollablePane natif (fond transparent
        // dans le look) reçoit un parchemin + une VBox de lignes
        // reconstruites à chaque sélection.
        pane.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        pane.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
        _objBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _objBox.AddThemeConstantOverride("separation", 2);
        pane.AddChild(_objBox);
        MissionCounters.FreePortalChanged += () =>
        {
            if (GodotObject.IsInstanceValid(_portalUse))
                _portalUse.Text = MissionCounters.PortalLabel(_usePortal);
        };
        // « Quêtes manquées » dépend du niveau.
        World.MobDanger.LevelChanged += () =>
        {
            if (GodotObject.IsInstanceValid(this)) RefreshTree();
        };

        if (TopLeftVitals.Find<BaseButton>(nat, "ShareBtn") is { } sh)
        {
            if (sh is Button sb) sb.Text = UiTextEn.Translate("Share");
            sh.Pressed += OnShare;
        }
        if (TopLeftVitals.Find<BaseButton>(nat, "GiveUpBtn") is { } gu)
        {
            if (gu is Button gb) gb.Text = UiTextEn.Translate("Drop");
            gu.Pressed += OnGiveUp;
        }
        if (TopLeftVitals.Find<BaseButton>(nat, "ExitBtn") is { } ex)
        {
            if (ex is Button eb) eb.Text = UiTextEn.Translate("Close");
            ex.Pressed += () => HudWindows.Close(_window);
        }

        // Re-rend le détail à l'ouverture : les wraps de StoryList/
        // objectifs se mesurent sur la taille réelle (nulle au build).
        shell.VisibilityChanged += () =>
        {
            if (shell.Visible && _tree?.GetSelected() != null)
                OnSelected();
        };
        RefreshTree();
        shell.Visible = false;
        return true;
    }

    /// <summary>Onglets « Quest | Legend » de la ref — Legend resté
    /// désactivé (les quêtes légendaires ne sont pas servies).</summary>
    private void BuildTabs(Control win, HudRoot hud)
    {
        foreach (var (label, i) in new[] {
            (UiTextEn.Translate("Quest"), 0),
            (UiTextEn.Translate("Legend"), 1) })
        {
            var t = new Button
            {
                Text = label, ToggleMode = true, ButtonPressed = i == 0,
                Disabled = i == 1,
                TooltipText = i == 1
                    ? UiTextEn.Translate("Not available yet") : "",
                FocusMode = FocusModeEnum.None,
            };
            HudRoot.Pin(t, 0, 0, 0, 0, 8 + i * 70, 24, 74 + i * 70, 42);
            t.AddThemeFontSizeOverride("font_size", 11);
            t.AddThemeColorOverride("font_color", Color.Color8(40, 28, 64));
            t.AddThemeColorOverride("font_pressed_color",
                Color.Color8(40, 28, 64));
            t.AddThemeColorOverride("font_disabled_color",
                Color.Color8(110, 90, 140));
            if (EeSkin.Tab(hud, i == 0) is { } sb)
                foreach (var st in new[] { "normal", "pressed",
                             "disabled", "hover" })
                    t.AddThemeStyleboxOverride(st, sb);
            win.AddChild(t);
        }
    }

    /// <summary>Description de quête → lignes de la StoryList native
    /// (ItemList sans BBCode — retour à la ligne mesuré sur sa police).
    /// </summary>
    private void FillStory(string? desc)
    {
        if (_story == null) return;
        _story.Clear();
        if (desc is not { Length: > 0 }) return;
        var font = _story.GetThemeFont("font") ?? ThemeDB.FallbackFont;
        var fs = _story.GetThemeFontSize("font_size");
        var width = _story.Size.X > 40 ? _story.Size.X - 24 : 330f;
        foreach (var para in desc.Replace("\r", "").Split('\n'))
        {
            var cur = "";
            foreach (var w in para.Split(' ',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var t = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length > 0 &&
                    font.GetStringSize(t, fontSize: fs).X > width)
                {
                    _story.AddItem(cur);
                    cur = w;
                }
                else cur = t;
            }
            _story.AddItem(cur);
        }
    }

    /// <summary>Volet du bas du layout natif (refs « quest log 1-3 ») :
    /// « Choix de l'environnement : » + catégorie, case « Utiliser Pierre
    /// de Portail », « Choix des cibles : » (une ligne par objectif :
    /// pastille, libellé n/req, bouton « OK/Go »), « Récompense : » (champs
    /// or / EXP), « Sélectionnez une récompense : » (choix) et
    /// « Récompenses disponibles : » (fixes) en icônes.</summary>
    private void FillObjectives(int mid, int st, string? zone,
        MissionTable.MissionDef? m)
    {
        if (_objBox == null) return;
        foreach (var c in _objBox.GetChildren())
        {
            _objBox.RemoveChild(c);
            c.QueueFree();
        }
        _goTargets.Clear();
        var accent = Color.Color8(0x9a, 0x9a, 0x00);   // jaune-vert natif
        var blue = Color.Color8(0x1a, 0x5f, 0xb4);
        Label Lbl(string text, Color col)
        {
            var l = new Label
            {
                Text = text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", col);
            return l;
        }
        Button GoBtn(Action act)
        {
            // Pack FR : « Go » → « OK » (bouton bleu natif).
            var b = EeSkin.Grey(new Button
            {
                Text = UiTextEn.Translate("Go"),
                CustomMinimumSize = new Vector2(30, 20),
                FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            }, 11);
            b.Pressed += act;
            return b;
        }
        Control TargetRow(Control ring, string text, Color col, Action? go)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            ring.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(ring);
            var lab = Lbl(text, col);
            lab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(lab);
            if (go != null) row.AddChild(GoBtn(go));
            return row;
        }
        // Ref : « Choix de l'environnement : Quête spéciale » = le TYPE
        // de quête (principale/quotidienne/répétable/spéciale…).
        var qkind = m != null
            ? QuestCatalog.KindOf(m.Group, m.Category, m.Flags)
            : QuestKind.Other;
        _objBox.AddChild(Lbl(
            UiTextEn.Translate("Environment to take:"), accent));
        _objBox.AddChild(Lbl(UiTextEn.Translate(
            QuestCatalog.LabelKey(qkind)) +
            (zone is { Length: > 0 } ? $" — {zone}" : ""), Ink));
        _portalUse = new Label
        {
            Text = MissionCounters.PortalLabel(_usePortal),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        _portalUse.AddThemeFontSizeOverride("font_size", 12);
        _portalUse.AddThemeColorOverride("font_color", Ink);
        _portalUse.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left }) return;
            _usePortal = !_usePortal;
            _portalUse.Text = MissionCounters.PortalLabel(_usePortal);
        };
        _objBox.AddChild(_portalUse);
        _objBox.AddChild(Lbl(UiTextEn.Translate("Targets to take:"),
            accent));
        foreach (var o in MissionObjectives.ForMission(mid))
        {
            var (_, kind, objId) = MissionObjectives.DecodePmsKey(o.PmsKey);
            var cur = MissionCounters.Current(o);
            var idx = _goTargets.Count;
            _goTargets.Add((objId, kind, o.Label));
            var done = cur >= o.Req;
            _objBox.AddChild(TargetRow(
                new QuestTracker.ObjectiveRing { Done = done },
                QuestObjectiveLogic.ShowsCount(kind)
                    ? $"{o.Label} {cur}/{o.Req}" : o.Label,
                done ? Color.Color8(0x1a, 0x8a, 0x30) : Ink,
                () => OnGoTarget(idx)));
        }
        if (st == 4 && m != null && m.SubmitNpc != 0)
            _objBox.AddChild(TargetRow(
                new QuestTracker.ObjectiveRing { Done = false },
                MissionObjectives.ReturnLine(m.SubmitNpc),
                Color.Color8(0x1a, 0x8a, 0x30), OnGo));
        // Récompenses : champs or/EXP puis icônes.
        long gold = 0, exp = 0;
        var fixedItems = new List<(int Id, int Qty)>();
        var choose = new List<(int Id, int Qty)>();
        if (m != null)
            foreach (var r in m.Rewards)
            {
                int A(int i) => i < r.Args.Count &&
                    int.TryParse(r.Args[i], out var v) ? v : 0;
                switch (r.Name)
                {
                    case "RewardGold": gold += A(0); break;
                    case "RewardExp": exp += A(0); break;
                    case "RewardItem":
                        fixedItems.Add((A(0), Math.Max(1, A(1)))); break;
                    case "RewardChooseItem":
                        choose.Add((A(0), Math.Max(1, A(1)))); break;
                }
            }
        _objBox.AddChild(Lbl(UiTextEn.Translate("Reward:"), Ink));
        var money = new HBoxContainer();
        money.AddThemeConstantOverride("separation", 10);
        money.AddChild(QuestSheetWindow.Field("●", gold.ToString("N0"),
            Color.Color8(0xb0, 0x8a, 0x00)));
        money.AddChild(QuestSheetWindow.Field(
            UiTextEn.Translate("EXP") + ":", exp.ToString("N0")));
        _objBox.AddChild(money);
        if (choose.Count > 0)
        {
            _objBox.AddChild(Lbl(UiTextEn.Translate(
                "Please select a quest reward:"), Ink));
            _objBox.AddChild(QuestSheetWindow.IconRow(choose));
        }
        if (fixedItems.Count > 0)
        {
            _objBox.AddChild(Lbl(UiTextEn.Translate(
                "Available quest rewards:"), Ink));
            _objBox.AddChild(QuestSheetWindow.IconRow(fixedItems));
        }
        _rewardRow = null;   // plus de rangée séparée : tout est ci-dessus
    }

    // -------------------- repli habillage manuel --------------------

    private void BuildFallback(HudRoot hud)
    {
        // Ref QuestLog.png : fenêtre « Quest » 645×620 à (144, 40) —
        // onglets Quest | Legend, colonne gauche (compteur, Track All,
        // arbre parchemin, aide Shift+clic), colonne droite (titre,
        // description, objectifs), Share | Drop | Close.
        var win = EeSkin.MakeWindow(hud, UiTextEn.Translate("Quest"), 144, 40, 645, 620);
        _window = win;
        AddChild(win);
        BuildManualBody(hud, win);
    }

    /// <summary>Corps hand-built (repli quand uidata/layout absent ou
    /// widgets natifs inattendus) — l'ancienne implémentation.</summary>
    private void BuildManualBody(HudRoot hud, Panel win)
    {
        foreach (var (label, i) in new[] { (UiTextEn.Translate("Quest"), 0), (UiTextEn.Translate("Legend"), 1) })
        {
            var t = new Button
            {
                Text = label, ToggleMode = true, ButtonPressed = i == 0,
                Disabled = i == 1, TooltipText = i == 1 ? UiTextEn.Translate("Not available yet") : "",
                FocusMode = FocusModeEnum.None,
            };
            HudRoot.Pin(t, 0, 0, 0, 0, 8 + i * 70, 22, 74 + i * 70, 40);
            t.AddThemeFontSizeOverride("font_size", 11);
            t.AddThemeColorOverride("font_color", Color.Color8(40, 28, 64));
            t.AddThemeColorOverride("font_pressed_color", Color.Color8(40, 28, 64));
            t.AddThemeColorOverride("font_disabled_color", Color.Color8(110, 90, 140));
            if (EeSkin.Tab(hud, i == 0) is { } sb)
                foreach (var st in new[] { "normal", "pressed", "disabled", "hover" })
                    t.AddThemeStyleboxOverride(st, sb);
            win.AddChild(t);
        }

        // Colonne gauche.
        var head = Box(win, 10, 44, 262, 104, EeSkin.InsetBox());
        _count = new Label();
        HudRoot.Pin(_count, 0, 0, 1, 0, 10, 8, -30, 26);
        EeSkin.Shadowed(_count, 12);
        head.AddChild(_count);
        var track = new Label
        {
            Text = UiTextEn.Translate("◇ Track All"),
            TooltipText = UiTextEn.Translate("Track every quest in progress"),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        HudRoot.Pin(track, 0, 0, 1, 0, 10, 30, -10, 50);
        EeSkin.Shadowed(track, 12, Color.Color8(120, 170, 235));
        head.AddChild(track);
        _trackAll = track;
        track.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left })
                ToggleTrackAll();
        };
        var treeBox = Box(win, 14, 112, 262, 560, EeSkin.ParchmentBox());
        _tree = new Tree { HideRoot = true, Columns = 1 };
        HudRoot.Pin(_tree, 0, 0, 1, 1, 3, 3, -3, -3);
        _tree.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _tree.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _tree.AddThemeStyleboxOverride("selected", new StyleBoxFlat
            { BgColor = Color.Color8(196, 210, 232) });
        _tree.AddThemeStyleboxOverride("selected_focus", new StyleBoxFlat
            { BgColor = Color.Color8(196, 210, 232) });
        _tree.AddThemeColorOverride("font_color", Ink);
        _tree.AddThemeColorOverride("font_selected_color", Color.Color8(20, 20, 18));
        _tree.AddThemeFontSizeOverride("font_size", 12);
        _tree.ItemSelected += OnSelected;
        // Shift+clic = bascule « suivi » de la quête (hint natif sous
        // l'arbre) → C→S 186 ; le losange orange marque les suivies.
        _tree.ItemMouseSelected += OnTreeMouse;
        treeBox.AddChild(_tree);
        var hint = Box(win, 14, 566, 262, 590, EeSkin.ParchmentBox());
        var hl = new Label
        {
            Text = UiTextEn.Translate("Shift+Left-click to track a quest"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hl.AddThemeFontSizeOverride("font_size", 11);
        hl.AddThemeColorOverride("font_color", Ink);
        hint.AddChild(hl);

        // Colonne droite : titre, description, objectifs.
        var right = Box(win, 274, 44, 635, 606, EeSkin.InsetBox());
        _title = new Label { ClipText = true };
        HudRoot.Pin(_title, 0, 0, 1, 0, 12, 6, -80, 26);
        EeSkin.Shadowed(_title, 12, Color.Color8(170, 170, 166));
        right.AddChild(_title);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        HudRoot.Pin(_status, 1, 0, 1, 0, -150, 6, -12, 26);
        EeSkin.Shadowed(_status, 11, Color.Color8(235, 210, 60));
        right.AddChild(_status);
        _detail = DarkText(Box(right, 10, 32, 351, 272, EeSkin.ParchmentBox()));
        var objBox = Box(right, 10, 284, 351, 460, EeSkin.ParchmentBox());
        _objectives = new RichTextLabel
        {
            BbcodeEnabled = true, ScrollActive = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        HudRoot.Pin(_objectives, 0, 0, 1, 1, 8, 6, -8, -6);
        _objectives.AddThemeFontSizeOverride("normal_font_size", 12);
        _objectives.AddThemeColorOverride("default_color", Ink);
        objBox.AddChild(_objectives);
        _objectives.MetaClicked += meta =>
        {
            var key = meta.AsString();
            if (key.StartsWith("go:") &&
                int.TryParse(key.AsSpan(3), out var idx))
                OnGoTarget(idx);
        };
        // Ref : « ◆ Use Portal Stone » dans le détail + bouton « Go »
        // en fin de ligne cible — la pierre envoie NCID 160, sinon
        // auto-follow local.
        _portalUse = new Label
        {
            Text = MissionCounters.PortalLabel(false),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        HudRoot.Pin(_portalUse, 0, 0, 0, 0, 12, 462, 190, 482);
        EeSkin.Shadowed(_portalUse, 11, Color.Color8(120, 170, 235));
        right.AddChild(_portalUse);
        _portalUse.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left }) return;
            _usePortal = !_usePortal;
            _portalUse.Text = MissionCounters.PortalLabel(_usePortal);
        };
        MissionCounters.FreePortalChanged += () =>
        {
            if (GodotObject.IsInstanceValid(_portalUse))
                _portalUse.Text = MissionCounters.PortalLabel(_usePortal);
        };
        var go = EeSkin.Grey(new Button { Text = UiTextEn.Translate("Go"), FocusMode =
            FocusModeEnum.None }, 11);
        HudRoot.Pin(go, 0, 0, 0, 0, 300, 460, 351, 482);
        go.Pressed += OnGo;
        right.AddChild(go);
        // M9 : récompenses en icônes + quantités (sous les objectifs).
        _rewardRow = new HBoxContainer();
        _rewardRow.AddThemeConstantOverride("separation", 8);
        HudRoot.Pin(_rewardRow, 0, 0, 0, 0, 12, 482, 351, 520);
        right.AddChild(_rewardRow);
        foreach (var (label, l, r, act) in new (string, int, int, Action?)[]
                 {
                     (UiTextEn.Translate("Share"), 10, 90, OnShare),
                     (UiTextEn.Translate("Drop"), 100, 180, OnGiveUp),
                     (UiTextEn.Translate("Close"), 270, 351, () => HudWindows.Close(_window)),
                 })
        {
            var b = EeSkin.Grey(new Button
            {
                Text = label, Disabled = act == null,
                TooltipText = act == null ? UiTextEn.Translate("Not available yet") : "",
                FocusMode = FocusModeEnum.None,
            }, 11);
            HudRoot.Pin(b, 0, 0, 0, 0, l, 532, r, 554);
            if (act != null) b.Pressed += act;
            right.AddChild(b);
        }
        RefreshTree();
        DragHelper.Persist(win, "questlog");
        win.Visible = false;
    }

    private static Panel Box(Control parent, int l, int t, int r, int b,
        StyleBox style)
    {
        var p = new Panel();
        HudRoot.Pin(p, 0, 0, 0, 0, l, t, r, b);
        p.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(p);
        return p;
    }

    private static Label DarkText(Control box)
    {
        var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        HudRoot.Pin(l, 0, 0, 1, 1, 8, 6, -8, -6);
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
        box.AddChild(l);
        return l;
    }

    /// <summary>Registre 348 complet — reconstruit l'arbre + la liste
    /// de suivi persistée (slots Trace).</summary>
    public void SetMissions(ZoneMissionStates m)
    {
        MissionCounters.FreePortal = m.FreePortal;
        _states.Clear();
        _traced.Clear();
        foreach (var (mid, st) in m.Fms) _states[mid] = st;
        foreach (var (mid, _) in m.InProgress)
            _states.TryAdd(mid, 3);
        foreach (var mid in m.Trace)
            if (mid > 0) _traced.Add(mid);
        RefreshTree();
    }

    /// <summary>Update unitaire 391 — kind=0 seul (état FMS) :
    /// Key=mid, MissionId=état, State=progress (kind=1 = compteur
    /// PMS, affiché par le tracker).</summary>
    public void UpdateMission(ZoneUpdateMissionState m)
    {
        if (m.Op == 1)
        {
            // Compteur d'objectif (même store que le tracker) → relit
            // le détail si la quête affichée est concernée.
            MissionCounters.Values[m.Key] = m.MissionId;
            if (_selectedMid != 0 &&
                MissionObjectives.DecodePmsKey(m.Key).Item1 == _selectedMid)
                OnSelected();
            return;
        }
        if (m.Op != 0) return;
        var mid = (int)m.Key;
        if (mid == 0) return;
        if (m.MissionId == 0) _states.Remove(mid);
        else _states[mid] = m.MissionId;
        RefreshTree();
    }

    /// <summary>Bouton « Go » du détail — « Use Portal Stone » cochée
    /// → NCID 160 (téléportation payée, flag=1 comme le tracker) ;
    /// sinon auto-follow local vers la cible de rendu.</summary>
    private void OnGo()
    {
        var m = MissionTable.Get(_selectedMid);
        var npc = m?.SubmitNpc ?? 0;
        if (_usePortal && Send != null && npc != 0)
            _ = Send(new ZoneServerMissionGoto
            {
                Field1 = (short)_selectedMid, Field2 = 0,
                Field3 = (short)npc, Field4 = 1,
            });
        else
            ObjectiveClicked?.Invoke(_selectedMid,
                TextResolver.MissionTitle(_selectedMid) ?? "");
    }

    /// <summary>Lien [Go] d'une ligne cible : Use Portal Stone cochée →
    /// NCID 160 (npc/monstre cible) ; sinon route à pied
    /// (<see cref="ObjectiveClicked"/> → AutoRoute/QuestRoute).</summary>
    private void OnGoTarget(int idx)
    {
        if (idx < 0 || idx >= _goTargets.Count) return;
        var (objId, kind, label) = _goTargets[idx];
        var portalable = kind != MissionObjectives.Kind.AreaTravel &&
            kind != MissionObjectives.Kind.Unknown && objId != 0;
        if (_usePortal && Send != null && portalable)
            _ = Send(new ZoneServerMissionGoto
            {
                Field1 = (short)_selectedMid, Field2 = 0,
                Field3 = (short)objId, Field4 = 1,
            });
        else
            ObjectiveClicked?.Invoke(_selectedMid, label);
    }

    /// <summary>Shift+clic sur une quête → bascule « suivi ». Différé :
    /// l'arbre interdit Clear/CreateItem pendant un événement souris
    /// (ToggleTrace → EmitTrace → RefreshTree reconstruit tout).</summary>
    private void OnTreeMouse(Vector2 pos, long mouseIndex)
    {
        var item = _tree?.GetSelected();
        if (item == null || !item.HasMeta("mid")) return;
        if (!Input.IsKeyPressed(Key.Shift)) return;
        var mid = item.GetMeta("mid").AsInt32();
        Callable.From(() => ToggleTrace(mid)).CallDeferred();
    }

    /// <summary>Bascule « suivi » d'une quête (shift+clic) — 10 slots
    /// natifs max ; seules les quêtes en cours sont suivables.</summary>
    private void ToggleTrace(int mid)
    {
        if (mid == 0) return;
        if (!_traced.Remove(mid))
        {
            if (_traced.Count >= 10 ||
                _states.GetValueOrDefault(mid) is not (3 or 4))
                return;
            _traced.Add(mid);
        }
        EmitTrace();
    }

    /// <summary>« Track All » — suit toutes les quêtes en cours
    /// (plafonné à 10) ; re-clic = tout désuivre.</summary>
    private void ToggleTrackAll()
    {
        if (_traced.Count > 0) _traced.Clear();
        else
            foreach (var (mid, st) in _states)
                if (st is 3 or 4 && _traced.Count < 10)
                    _traced.Add(mid);
        EmitTrace();
    }

    /// <summary>Pousse la liste courante : C→S 186 (10 slots) +
    /// miroir local du tracker + marqueurs arbre.</summary>
    private void EmitTrace()
    {
        if (_trackAll is BaseButton cb)
            cb.SetPressedNoSignal(_traced.Count > 0);
        else if (_trackAll is Label tl)
            tl.Text = (_traced.Count > 0 ? "◆" : "◇") +
                UiTextEn.Translate(" Track All");
        TraceChanged?.Invoke(_traced.ToList());
        RefreshTree();
    }

    /// <summary>Clic « Give Up » sur la quête sélectionnée — seules
    /// les quêtes en cours (FMS 3/4) sont abandonnables.</summary>
    private void OnGiveUp()
    {
        if (_selectedMid != 0 &&
            _states.GetValueOrDefault(_selectedMid) is 3 or 4)
            GiveUpRequested?.Invoke(_selectedMid);
    }

    /// <summary>Clic « Share » : seules les quêtes en cours (FMS 3/4)
    /// se partagent ; le serveur valide (équipe, éligibilité, quête
    /// partageable) et répond par textes.</summary>
    private void OnShare()
    {
        if (_selectedMid != 0 &&
            _states.GetValueOrDefault(_selectedMid) is 3 or 4)
            ShareRequested?.Invoke(_selectedMid);
    }

    /// <summary>Arbre de la ref : une branche par zone/catégorie
    /// (t_mission col2) avec les quêtes en cours (titre rouge, losange
    /// vert — bleu = suivie) ET terminées (grisées), puis « Quêtes
    /// manquées » (repliée) = quêtes prenables selon le niveau du joueur
    /// (<see cref="QuestCatalog.Missed"/>), triées par niveau.</summary>
    private void RefreshTree()
    {
        if (_tree == null) return;
        _tree.Clear();
        var root = _tree.CreateItem();
        var n = _states.Count(kv => kv.Value is 3 or 4);
        if (_count != null)
            _count.Text = string.Format(
                UiTextEn.Translate("Quests: {0}/25"), n);
        var zones = new Dictionary<string, TreeItem>();
        TreeItem ZoneOf(int mid)
        {
            var z = TextResolver.MissionZone(mid)
                ?? UiTextEn.Translate("Other");
            if (zones.TryGetValue(z, out var zi)) return zi;
            zi = _tree.CreateItem(root);
            zi.SetText(0, z);
            zi.SetSelectable(0, false);
            zi.SetCustomColor(0, Colors.White);
            return zones[z] = zi;
        }
        // En cours d'abord, puis terminées (FMS 1/5) dans leur zone.
        foreach (var (mid, st) in _states
                     .Where(kv => kv.Value is 3 or 4 or 1 or 5 &&
                         TextResolver.MissionTitle(kv.Key) != null &&
                         // répétable terminée = reprenable → « oubliées »
                         !(kv.Value is 1 or 5 &&
                           MissionTable.Get(kv.Key) is { } rm &&
                           QuestCatalog.IsRepeatable(rm)))
                     .OrderBy(kv => kv.Value is 3 or 4 ? 0 : 1)
                     .ThenBy(kv => TextResolver.MissionZone(kv.Key) ?? "")
                     .ThenBy(kv => QuestCatalog.LevelOf(
                         MissionTable.Get(kv.Key) ?? new MissionTable.MissionDef(),
                         TextResolver.MissionTitle(kv.Key)))
                     .ThenBy(kv => kv.Key))
        {
            var active = st is 3 or 4;
            var item = _tree.CreateItem(ZoneOf(mid));
            item.SetText(0, TextResolver.MissionTitle(mid)!);
            item.SetIcon(0, EeSkin.DiamondColor(active
                ? (_traced.Contains(mid) ? Color.Color8(60, 130, 235)
                    : Color.Color8(60, 190, 80))
                : Color.Color8(130, 130, 128)));
            item.SetCustomColor(0, active ? Color.Color8(226, 64, 64)
                : Color.Color8(150, 150, 148));
            item.SetMeta("mid", mid);
        }
        // « Quêtes manquées » : prenables selon le niveau, par niveau.
        var level = World.MobDanger.PlayerLevel;
        if (level > 0)
        {
            var missed = QuestCatalog.Missed(MissionTable.All, level,
                id => _states.GetValueOrDefault(id),
                id => TextResolver.MissionTitle(id),
                minLevel: Math.Max(1, level - 5), max: 300);
            if (missed.Count > 0)
            {
                var mi = _tree.CreateItem(root);
                mi.SetText(0, UiTextEn.Translate("Missed Quests") +
                    $" ({missed.Count})");
                mi.SetSelectable(0, false);
                mi.SetCustomColor(0, Colors.White);
                mi.Collapsed = true;
                foreach (var (def, _, _) in missed)
                {
                    var it = _tree.CreateItem(mi);
                    it.SetText(0, TextResolver.MissionTitle(def.Id)!);
                    it.SetIcon(0, EeSkin.DiamondColor(
                        Color.Color8(235, 190, 60)));
                    it.SetCustomColor(0, Color.Color8(226, 64, 64));
                    it.SetMeta("mid", def.Id);
                }
            }
        }
        // Sélection : la quête déjà choisie sinon la 1re en cours.
        TreeItem? pick = null, firstActive = null;
        foreach (var z in root.GetChildren())
            foreach (var it in z.GetChildren())
            {
                if (!it.HasMeta("mid")) continue;
                var id = it.GetMeta("mid").AsInt32();
                if (id == _selectedMid && _selectedMid != 0) pick = it;
                if (firstActive == null &&
                    _states.GetValueOrDefault(id) is 3 or 4)
                    firstActive = it;
            }
        (pick ?? firstActive)?.Select(0);
    }

    /// <summary>Sélection d'une quête → panneau détail
    /// (titre/zone/description/état).</summary>
    private void OnSelected()
    {
        var item = _tree?.GetSelected();
        if (item == null || !item.HasMeta("mid")) return;
        var mid = item.GetMeta("mid").AsInt32();
        _selectedMid = mid;
        var st = _states.GetValueOrDefault(mid);
        if (_title != null)
        {
            _title.Text = "◆ " + (TextResolver.MissionTitle(mid)
                ?? string.Format(UiTextEn.Translate("Quest #{0}"), mid));
            _title.AddThemeColorOverride("font_color",
                _native ? Light : Colors.White);
        }
        var zone = TextResolver.MissionZone(mid);
        var desc = TextResolver.MissionDescription(mid);
        if (_story != null) FillStory(desc);
        else if (_detail != null) _detail.Text = desc ?? "";
        var m = MissionTable.Get(mid);
        if (_objBox != null)
            FillObjectives(mid, st, zone, m);
        else if (_objectives != null)
        {
            var objs = MissionObjectives.ObjectiveLines(mid);
            var sb = new System.Text.StringBuilder();
            const string head = "#b08a00";
            if (zone is { Length: > 0 })
                sb.Append($"[color={head}]{UiTextEn.Translate("Environment to take:")}[/color]\n{zone}\n\n");
            sb.Append($"[color={head}]{UiTextEn.Translate("Targets to take:")}[/color]\n");
            _goTargets.Clear();
            foreach (var o in MissionObjectives.ForMission(mid))
            {
                var (_, kind, objId) = MissionObjectives.DecodePmsKey(o.PmsKey);
                var cur = MissionCounters.Values.GetValueOrDefault(o.PmsKey);
                var line = $"{o.Label} {Math.Min(cur, o.Req)}/{o.Req}";
                var i = _goTargets.Count;
                _goTargets.Add((objId, kind, o.Label));
                sb.Append($"  ◆ [color=#1a5fb4]{line}[/color] " +
                    $"[url=go:{i}][color=#c06000][lb]Go[rb][/color][/url]\n");
            }
            if (st == 4 && m != null && m.SubmitNpc != 0)
                sb.Append(UiTextEn.Translate($"  ◆ [color=#1a8a30]Return to ") +
                    $"{TextResolver.NpcName(m.SubmitNpc) ?? UiTextEn.Translate("the quest giver")}" +
                    "[/color]\n");
            var rl = MissionObjectives.RewardLines(mid)
                .Where(l => !l.Contains('#') || true).ToList();
            if (rl.Count > 0)
            {
                sb.Append($"\n[color={head}]{UiTextEn.Translate("Rewards:")}[/color]\n");
                foreach (var l in rl)
                {
                    var c = l.Contains("EXP") ? "#2a8a2a"
                        : l.Contains("gold") ? "#b07800"
                        : l.Contains("class") ? "#7a2a9a" : "#222222";
                    sb.Append($"  ◆ [color={c}]{l}[/color]\n");
                }
            }
            _objectives.Text = sb.ToString();
        }
        FillRewards(mid);
        if (_status != null)
        {
            _status.Text = "";
            _status.AddThemeColorOverride("font_color", st == 4
                ? Color.Color8(90, 220, 110)
                : st is 3 ? Color.Color8(235, 210, 60)
                : Color.Color8(170, 170, 166));
        }
    }

    /// <summary>Rangée de récompenses : icône d'objet + ×n, ou texte
    /// (or, EXP, points).</summary>
    private void FillRewards(int mid)
    {
        if (_rewardRow == null) return;
        foreach (var c in _rewardRow.GetChildren()) c.QueueFree();
        var m = MissionTable.Get(mid);
        if (m == null) return;
        foreach (var r in m.Rewards)
        {
            int A(int i) => i < r.Args.Count &&
                int.TryParse(r.Args[i], out var v) ? v : 0;
            string? text = r.Name switch
            {
                "RewardGold" => $"{A(0)} gold",
                "RewardExp" => $"{A(0)} EXP",
                "RewardClassPoint" => $"{A(0)} class pts",
                "RewardSpecialCoin" => $"{A(0)} coins",
                "RewardReputation" => "Reputation",
                _ => null,
            };
            if (r.Name is "RewardItem" or "RewardChooseItem")
            {
                var def = InventoryWindow.LookupDef(A(0));
                var box = new Control
                {
                    CustomMinimumSize = new Vector2(34, 34),
                    TooltipText = TextResolver.ItemName(A(0)) ?? $"#{A(0)}",
                };
                var tex = def is { Code.Length: > 0 }
                    ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png")
                    : null;
                var icon = new TextureRect
                {
                    Texture = tex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                box.AddChild(icon);
                if (r.Name == "RewardItem" && A(1) > 1)
                {
                    var q = new Label { Text = $"{A(1)}",
                        MouseFilter = MouseFilterEnum.Ignore };
                    EeSkin.Shadowed(q, 11, Colors.White);
                    q.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
                    q.GrowHorizontal = GrowDirection.Begin;
                    q.GrowVertical = GrowDirection.Begin;
                    box.AddChild(q);
                }
                _rewardRow.AddChild(box);
            }
            else if (text != null)
            {
                var l = new Label { Text = text,
                    VerticalAlignment = VerticalAlignment.Center };
                EeSkin.Shadowed(l, 12, Color.Color8(235, 210, 60));
                _rewardRow.AddChild(l);
            }
        }
    }

    /// <summary>États FMS natifs (convention serveur
    /// ZoneMissionStates : 1 done, 3/4 en cours, 5 checked).</summary>
    private static string StateName(int st) => st switch
    {
        1 => UiTextEn.Translate("Completed"),
        3 => UiTextEn.Translate("In progress"),
        4 => UiTextEn.Translate("Ready to turn in"),
        5 => UiTextEn.Translate("Checked"),
        _ => $"state {st}",
    };

    /// <summary>--track-test : sonde QA du suivi quête — injecte un
    /// vrai shift+clic sur un item de l'arbre (ItemMouseSelected →
    /// C→S 186), puis « Track All », puis revient à 1 quête suivie
    /// (état laissé pour la preuve de restauration au prochain
    /// login : le 348 ressort les slots Trace persistés).</summary>
    public sealed partial class TrackProbe : Node
    {
        private readonly QuestWindow _w;
        private readonly string _shotsDir;
        private int _frame, _step;

        public TrackProbe(QuestWindow w)
        {
            _w = w; Name = "__track_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            // L'écran de chargement avale les inputs — la sonde
            // attend la fin du fondu ET la fenêtre réellement
            // affichée (hud-open la montre après HudReady).
            if (Screens.LoadingScreen.Active != null ||
                _w._window?.IsVisibleInTree() != true)
                return;
            _frame++;
            // Attend une quête en cours (fournie par --quest-test +
            // --flow-test) — retente chaque 10 frames.
            if (_step == 0 && _frame % 10 == 0 &&
                _w._states.Values.Any(st => st is 3 or 4))
            {
                _step = 1;
                var mid = _w._states.First(kv => kv.Value is 3 or 4).Key;
                ShiftClick(mid);
            }
            else if (_step == 1 && _frame % 20 == 0)
            {
                GD.Print("[track-test] après shift+clic : traced=" +
                    $"[{string.Join(",", _w._traced)}]");
                if (_w._traced.Count == 0) { _step = 0; return; }
                Click(_w._trackAll!);  // « Track All »
                _step = 2;
            }
            else if (_step == 2 && _frame % 20 == 0)
            {
                GD.Print("[track-test] après Track All : traced=" +
                    $"[{string.Join(",", _w._traced)}]");
                Shot("u4_questlog_tracked");
                Click(_w._trackAll!);  // décochage
                _step = 3;
            }
            else if (_step == 3 && _frame % 20 == 0)
            {
                // Resuit 1 quête → état final = 1 slot, persisté.
                var mid = _w._states.First(
                    kv => kv.Value is 3 or 4).Key;
                ShiftClick(mid);
                GD.Print("[track-test] final : traced=" +
                    $"[{string.Join(",", _w._traced)}]");
                Shot("u4_questlog_final");
                SetProcess(false);
            }
        }

        /// <summary>Shift maintenu + clic gauche sur l'item `mid` de
        /// l'arbre — passe par le vrai chemin ItemMouseSelected.</summary>
        private void ShiftClick(int mid)
        {
            var item = FindItem(_w._tree!.GetRoot()!, mid);
            if (item == null || _w._tree == null)
            {
                GD.PrintErr($"[track-test] item mid={mid} absent");
                return;
            }
            var r = _w._tree.GetItemAreaRect(item);
            var at = _w._tree.GlobalPosition + r.GetCenter();
            Input.ParseInputEvent(new InputEventKey
            { Keycode = Key.Shift, Pressed = true });
            Click(at);
            Input.ParseInputEvent(new InputEventKey
            { Keycode = Key.Shift, Pressed = false });
        }

        private static void Click(Control c) =>
            Click(c.GetGlobalRect().GetCenter());

        private static void Click(Vector2 at)
        {
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true,
                Position = at, GlobalPosition = at,
            });
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = false,
                Position = at, GlobalPosition = at,
            });
        }

        private static TreeItem? FindItem(TreeItem it, int mid)
        {
            for (var c = it.GetFirstChild(); c != null;
                 c = c.GetNext())
            {
                if (c.HasMeta("mid") && c.GetMeta("mid").AsInt32() == mid)
                    return c;
                var f = FindItem(c, mid);
                if (f != null) return f;
            }
            return null;
        }

        private void Shot(string tag)
        {
            try
            {
                Directory.CreateDirectory(_shotsDir);
                RenderingServer.ForceDraw();
                GetViewport().GetTexture()?.GetImage()?.SavePng(
                    $"{_shotsDir}/{tag}.png");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[track-test] shot : {e.Message}");
            }
        }
    }

}
