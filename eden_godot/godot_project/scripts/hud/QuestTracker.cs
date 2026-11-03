using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Panneau quêtes bord droit, sous la minimap (le bloc natif est
/// top-right sous le radar — bandeau translucide sombre, onglets
/// « Use Portal Stone » / « Background » et boutons « Track Quests » /
/// « Unacquired Quests » de la capture Reforged).
///
/// Monté sur <c>QuestTraceWnd.layout</c> (fond « backgrand » alpha
/// 0.5, panes suivi/recommandé, cases AutoTransport/checkBG, onglets
/// ShowMission/ShowRecommend, bouton repli SizeBtn) — repli
/// <see cref="BuildFallback"/> si le layout ou ses widgets manquent.
///
/// Données réelles : <see cref="ZoneMissionStates"/> (NCID 348,
/// réponse du serveur au QueryMissionStates 50 du burst d'entrée) —
/// les slots <see cref="ZoneMissionStates.Trace"/> portent les ids de
/// quêtes suivies ; les titres/zones/objectifs viennent de
/// <c>data/db/t_mission.ini</c> (col1 titre « L02. Awakening IV »,
/// col2 zone, col3 description). Sans quête suivie, les quêtes en
/// cours (FMS 3/4) sont affichées ; sinon « aucune quête suivie ».
/// </summary>
public partial class QuestTracker : Control
{
    private VBoxContainer? _list;

    /// <summary>Quêtes actives retenues (id → titre/objectif).</summary>
    private readonly Dictionary<int, (string Title, string Goal)> _active
        = new();
    /// <summary>Compteurs d'objectifs (clé PMS → dernier count reçu du
    /// 391 kind=1) — affichés « n/req » sous la quête.</summary>
    private static Dictionary<uint, int> _pms => MissionCounters.Values;
    /// <summary>mid → true quand la quête est prête à rendre
    /// (FMS 4 = objectifs remplis, 391 kind=0).</summary>
    private readonly HashSet<int> _ready = new();
    /// <summary>Registre FMS (mid → état) — sert à « Quêtes oubliées ».
    /// </summary>
    private readonly Dictionary<int, int> _fms = new();

    private ScrollContainer? _scroll;
    private const float ScrollH = 190f;
    private bool _usePortal, _bgOn = true, _showUnacquired;

    // --- widgets du layout natif QuestTraceWnd ---
    private ScrollContainer? _recommend;
    private VBoxContainer? _unacqList;
    private Control? _nat, _backgrand;
    private Label? _trackTab, _unacqTab;
    private Control? _trackBg, _unacqBg;
    private TextureButton? _foldTb;
    private Control[] _foldParts = [];
    private Texture2D? _fiN, _fiH, _fiP, _foN, _foH, _foP;
    private bool _folded;
    private const float FullH = 400f, FoldedH = 24f, WinW = 390f;

    /// <summary>Envoi zone (branché par HudRoot) — NCID 160 quand
    /// « Use Portal Stone » est cochée.</summary>
    public Func<NetMessage, Task>? Send { get; set; }

    /// <summary>Clic sur une ligne objectif (mid, libellé) — branché
    /// sur <see cref="AutoRoute.ToMissionTarget"/> par HudRoot.</summary>
    public event Action<int, string>? ObjectiveClicked;

    public void Build(HudRoot hud)
    {
        // Layout natif CEGUI prioritaire ; repli visuel proche sinon.
        if (!TryBuildNative(hud)) BuildFallback();
        // « Use » : le tracker se repeint quand l'inventaire change
        // (item de quête obtenu / consommé). Coalescé à un repeint
        // par frame : un loot lève plusieurs SlotsChanged (mutation +
        // recharge sac) et Refresh reconstruit toute la liste.
        InventoryWindow.SlotsChanged += () =>
        {
            if (!GodotObject.IsInstanceValid(this) || _slotsDirty)
                return;
            _slotsDirty = true;
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(this))
                    FlushSlotsChanged();
            }).CallDeferred();
        };
        World.MobDanger.LevelChanged += () =>
        {
            if (GodotObject.IsInstanceValid(this) && _showUnacquired)
                Refresh();
        };
        // QA : QUEST_FAKE=1 remplit les compteurs des 2 premières
        // quêtes suivies (même chemin que le 391 kind=1) pour la preuve
        // visuelle « rempli = pastille bleue + Terminé + rendu ».
        if (System.Environment.GetEnvironmentVariable("QUEST_FAKE") == "1")
        {
            var t = new Godot.Timer { WaitTime = 8, OneShot = true,
                Autostart = true };
            AddChild(t);
            t.Timeout += () =>
            {
                var n = 0;
                foreach (var (mid, _) in _active.ToList())
                {
                    foreach (var o in MissionObjectives.ForMission(mid))
                        UpdateMission(new ZoneUpdateMissionState
                        {
                            Op = 1, Key = o.PmsKey,
                            MissionId = (ushort)o.Req,
                        });
                    if (n++ == 0)
                        UpdateMission(new ZoneUpdateMissionState
                            { Op = 0, Key = (uint)mid, MissionId = 4 });
                    if (n >= 2) break;
                }
            };
        }
    }

    private static T? ByName<T>(Node root, string suffix) where T : class =>
        root.FindChild("*" + suffix, true, false) as T;

    /// <summary>Monte <c>QuestTraceWnd.layout</c> (ancré à droite,
    /// ~300×400, sous la minimap à 33 % de la hauteur) et le câble :
    /// lignes de quêtes dans le pane « Panel », hint « Unacquired »
    /// dans « RecommendActivitiesPanel », cases losange
    /// AutoTransport/checkBG, onglets ShowMission/ShowRecommend (+BG),
    /// repli SizeBtn (zoom in/out natifs). La racine garde ses ancres
    /// natives ; <see cref="HudLayout"/> la déplace/persiste comme
    /// pièce « Quests ». Retourne false si le layout manque ou si un
    /// widget attendu n'a pas le type Godot prévu.</summary>
    private bool TryBuildNative(HudRoot hud)
    {
        if (hud.Build("QuestTraceWnd.layout") is not { } nat)
            return false;
        var pane = ByName<ScrollContainer>(nat, "_Panel");
        var rec = ByName<ScrollContainer>(nat, "RecommendActivitiesPanel");
        var portal = ByName<CheckBox>(nat, "_AutoTransport");
        var bg = ByName<CheckBox>(nat, "_checkBG");
        var track = ByName<Label>(nat, "_ShowMission");
        var unacq = ByName<Label>(nat, "_ShowRecommend");
        var trackBg = ByName<Control>(nat, "_ShowMissionBG");
        var unacqBg = ByName<Control>(nat, "_ShowRecommendBG");
        var fold = ByName<TextureButton>(nat, "_SizeBtn");
        var back = ByName<Control>(nat, "_backgrand");
        if (pane == null || rec == null || portal == null || bg == null ||
            track == null || unacq == null || trackBg == null ||
            unacqBg == null || fold == null || back == null)
        {
            GD.PrintErr("[quest] QuestTraceWnd : widgets attendus " +
                "absents → habillage manuel");
            nat.Free();
            return false;
        }

        AddChild(nat);
        // backgrand natif = 1.5× la largeur (450 px, déborde à gauche
        // → « énorme bloc vide ») : on le ramène à la fenêtre (300 px).
        HudRoot.Pin(back, 0, 0, 0, 1, 0, 0, WinW, 0);
        // Fenêtre élargie (le natif 300 px est « ultra serré ») : bord
        // droit inchangé, on gagne vers la gauche.
        nat.OffsetLeft = nat.OffsetRight - WinW;
        // Plaque nettement plus opaque : texte lisible sur n'importe
        // quel décor.
        back.Modulate = new Color(1f, 1f, 1f, 0.82f);
        // La fenêtre de 300×400 ne doit pas avaler les clics sol hors
        // widgets (les enfants gardent leur filtre ; le drag/persist
        // HudLayout s'arme sur sa bande haute sans filtre Stop).
        nat.MouseFilter = MouseFilterEnum.Ignore;
        _nat = nat;
        _backgrand = back;
        // channelImage sans texture résolue (son cadre nine-patch ne
        // s'applique pas à un TextureRect) → plaque sombre à plat,
        // l'Alpha 0.5 du layout la rend translucide.
        if (back is TextureRect btr && btr.Texture == null)
        {
            var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            img.Fill(Color.Color8(10, 11, 14));
            btr.Texture = ImageTexture.CreateFromImage(img);
            btr.StretchMode = TextureRect.StretchModeEnum.Scale;
        }

        // Les panes natifs recouvrent l'en-tête (y 20 → bas) : on les
        // redescend sous la bande d'onglets (bas ≈ 74) pour que les
        // lignes de quêtes ne passent pas sous les cases/onglets.
        HudRoot.Pin(pane, 0, 0, 0, 0, 10, 84, WinW - 10, 397);
        HudRoot.Pin(rec, 0, 0, 0, 0, 10, 84, WinW - 10, 397);
        // MousePassThrough natif → Ignore : sans lui la molette ne
        // scrollerait pas (les ancêtres Ignore sont sautés à la
        // propagation gui). Pass laisse filer les clics non traités.
        pane.MouseFilter = MouseFilterEnum.Pass;
        rec.MouseFilter = MouseFilterEnum.Pass;

        const float M = 10f, G = 6f, half = (WinW - 2 * M - G) / 2f;
        // Bande haute répartie sur TOUTE la largeur (le natif était calé
        // sur 300 px : fort vide à droite, marge minuscule à gauche) :
        // marges égales 10 px, deux colonnes de même largeur.
        // Appliqué après le 1er layout : les Y natifs ne sont lisibles qu'alors
        // (offsets relatifs à des ancres proportionnelles).
        Callable.From(() =>
        {
            var parts = new Control[] { portal, bg, trackBg, unacqBg, track, unacq, fold };
            var ys = parts.ToDictionary(c => c,
                c => (T: c.GetGlobalRect().Position.Y - nat.GetGlobalRect().Position.Y,
                      B: c.GetGlobalRect().End.Y - nat.GetGlobalRect().Position.Y));
            void Span(Control c, float l, float r) =>
                HudRoot.Pin(c, 0, 0, 0, 0, l, ys[c].T, r, ys[c].B);
            // Ligne des cases : « Pierre de Portail » (long) 2/3, « Fond » le reste.
            void Apply()
            {
                if (!GodotObject.IsInstanceValid(nat)) return;
                Span(portal, M, M + 236f);
                Span(bg, M + 236f + G, WinW - M - 28f);   // 28 px : bouton de repli
                Span(trackBg, M, M + half);
                Span(unacqBg, M + half + G, WinW - M);
                // Cadres dessinés (copiés des offsets natifs à la création) : mêmes rects.
                if (_trackBox != null)
                    HudRoot.Pin(_trackBox, 0, 0, 0, 0, M, ys[trackBg].T, M + half, ys[trackBg].B);
                if (_unacqBox != null)
                    HudRoot.Pin(_unacqBox, 0, 0, 0, 0, M + half + G, ys[unacqBg].T, WinW - M, ys[unacqBg].B);
                Span(track, M, M + half);
                Span(unacq, M + half + G, WinW - M);
                Span(fold, WinW - 26f, WinW - 4f);
                track.HorizontalAlignment = HorizontalAlignment.Center;
                unacq.HorizontalAlignment = HorizontalAlignment.Center;
            }
            Apply();
            // Le layout / HudLayout réapplique des offsets après coup : ré-épingler.
            foreach (var t in new[] { 0.5, 2.0, 5.0 })
                nat.GetTree().CreateTimer(t).Timeout += Apply;
        }).CallDeferred();

        _scroll = pane;
        _recommend = rec;
        _list = new VBoxContainer { Name = "QuestList" };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.AddThemeConstantOverride("separation", 2);
        // Marge interne : texte décollé de la gauche et de la
        // barre de défilement (droite).
        var padL = new MarginContainer
            { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        padL.AddThemeConstantOverride("margin_left", 6);
        padL.AddThemeConstantOverride("margin_right", 6);
        padL.AddThemeConstantOverride("margin_top", 2);
        padL.AddChild(_list);
        pane.AddChild(padL);
        _unacqList = new VBoxContainer { Name = "RecommendList" };
        _unacqList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var padR = new MarginContainer
            { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        padR.AddThemeConstantOverride("margin_left", 6);
        padR.AddThemeConstantOverride("margin_right", 6);
        padR.AddThemeConstantOverride("margin_top", 2);
        padR.AddChild(_unacqList);
        rec.AddChild(padR);
        rec.Visible = false;

        // Cases à losange du ref : l'état est porté par l'icône, le
        // texte reste bleu liant.
        foreach (var cb in new[] { portal, bg })
        {
            cb.AddThemeIconOverride("unchecked", EeSkin.Diamond(false));
            cb.AddThemeIconOverride("checked", EeSkin.Diamond(true));
            cb.AddThemeIconOverride("radio_unchecked", EeSkin.Diamond(false));
            cb.AddThemeIconOverride("radio_checked", EeSkin.Diamond(true));
            // F001-9 natif ≈ 11 px ; ClipText : le libellé FR (plus
            // long que l'EN) ne déborde pas sur la case voisine.
            cb.AddThemeFontSizeOverride("font_size", 11);
            cb.AddThemeColorOverride("font_color",
                Color.Color8(60, 170, 235));
            cb.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            cb.ClipText = true;
        }
        portal.Text = PortalText();
        portal.TooltipText = portal.Text;
        portal.SetPressedNoSignal(_usePortal);
        portal.Toggled += on => _usePortal = on;
        MissionCounters.FreePortalChanged += () =>
        {
            if (GodotObject.IsInstanceValid(portal))
                portal.TooltipText = portal.Text = PortalText();
        };
        // « ◆ Background » est la clé TSV (« Background » nu = le
        // réglage fps des Settings, autre traduction) — le losange
        // saute, l'état est porté par l'icône de la case.
        var bgText = UiTextEn.Translate("◆ Background");
        bg.Text = bgText.StartsWith("◆ ") ? bgText[2..] : bgText;
        bg.SetPressedNoSignal(_bgOn);
        bg.Toggled += on =>
        {
            _bgOn = on;
            back.Visible = on;
        };

        // Onglets texte (labels + images de fond = zones de clic
        // natives) — l'actif plein, l'inactif estompé.
        _trackTab = track; _unacqTab = unacq;
        _trackBg = trackBg; _unacqBg = unacqBg;
        track.Text = UiTextEn.Translate("Track Quests");
        unacq.Text = UiTextEn.Translate("Unacquired Quests");
        // Ref : onglets en police ~13 px (le layout les laisse grands).
        track.AddThemeFontSizeOverride("font_size", 13);
        unacq.AddThemeFontSizeOverride("font_size", 13);
        void WireTab(Control c, bool recommend)
        {
            c.MouseFilter = MouseFilterEnum.Stop;
            c.MouseDefaultCursorShape = CursorShape.PointingHand;
            c.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true,
                        ButtonIndex: MouseButton.Left })
                    SetTab(recommend);
            };
        }
        // Ref « questtracker le vrai » : deux gros boutons sombres à
        // liseré gris (le layout natif n'a pas de texture résolue).
        _trackBox = TabBox(trackBg, track); _unacqBox = TabBox(unacqBg, unacq);
        WireTab(track, false); WireTab(trackBg, false);
        WireTab(unacq, true); WireTab(unacqBg, true);
        PaintTabs();

        // Repli natif : la fenêtre se réduit à la bande du bouton
        // (24 px) ; icônes CreateZoomIn/CreateZoomOut de l'atlas.
        _foldTb = fold;
        _fiN = fold.TextureNormal; _fiH = fold.TextureHover;
        _fiP = fold.TexturePressed;
        _foN = hud.Region("DejaVu", "CreateZoomOutNor");
        _foH = hud.Region("DejaVu", "CreateZoomOutHover");
        _foP = hud.Region("DejaVu", "CreateZoomOutPress");
        // Déplié au départ : icône « − » (replier).
        if (_foN != null) fold.TextureNormal = _foN;
        if (_foH != null) fold.TextureHover = _foH;
        if (_foP != null) fold.TexturePressed = _foP;
        fold.TooltipText = UiTextEn.Translate("Hide");
        _foldParts = new Control[]
            { pane, rec, track, unacq, trackBg, unacqBg, portal, bg };
        fold.Pressed += () => SetFold(!_folded);

        Refresh();
        return true;
    }

    /// <summary>Libellé de la case portail (« Use Portal Stone » +
    /// suffixe « (free) » quand le 348 la donne).</summary>
    private static string PortalText() =>
        UiTextEn.Translate("Use Portal Stone") +
        (MissionCounters.FreePortal
            ? " " + UiTextEn.Translate("(free)") : "");

    /// <summary>Bascule d'onglet : quêtes suivies ↔ non acquises.</summary>
    private void SetTab(bool recommend)
    {
        _showUnacquired = recommend;
        PaintTabs();
        Refresh();
    }

    /// <summary>Onglet actif plein, inactif estompé (ref : deux
    /// onglets texte côte à côte).</summary>
    private Panel? _trackBox, _unacqBox;

    /// <summary>Plaque sombre à liseré derrière un onglet (copie les
    /// ancres/offsets du fond natif, qui devient transparent).</summary>
    private static Panel TabBox(Control src, Control label)
    {
        var p = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        p.AnchorLeft = src.AnchorLeft; p.AnchorRight = src.AnchorRight;
        p.AnchorTop = src.AnchorTop; p.AnchorBottom = src.AnchorBottom;
        p.OffsetLeft = src.OffsetLeft; p.OffsetRight = src.OffsetRight;
        p.OffsetTop = src.OffsetTop; p.OffsetBottom = src.OffsetBottom;
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.Color8(26, 27, 30, 235),
            BorderColor = Color.Color8(120, 122, 128),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        src.GetParent().AddChild(p);
        // Sous le libellé ET le fond natif (ordre d'affichage).
        src.GetParent().MoveChild(p,
            Math.Min(src.GetIndex(), label.GetIndex()));
        src.SelfModulate = Colors.Transparent;
        return p;
    }

    private void PaintTabs()
    {
        if (_trackTab == null || _unacqTab == null) return;
        if (_trackBox != null)
            _trackBox.Modulate = _showUnacquired
                ? new Color(1, 1, 1, 0.6f) : Colors.White;
        if (_unacqBox != null)
            _unacqBox.Modulate = _showUnacquired
                ? Colors.White : new Color(1, 1, 1, 0.6f);
        var dim = new Color(1f, 1f, 1f, 0.5f);
        _trackTab.Modulate = _showUnacquired ? dim : Colors.White;
        _unacqTab.Modulate = _showUnacquired ? Colors.White : dim;

    }

    /// <summary>Repli/dépli de la fenêtre (SizeBtn) — la bande haute
    /// reste, panes + onglets + cases se masquent.</summary>
    private void SetFold(bool f)
    {
        _folded = f;
        foreach (var c in _foldParts)
            if (GodotObject.IsInstanceValid(c)) c.Visible = !f;
        if (_nat != null)
            _nat.OffsetBottom = _nat.OffsetTop + (f ? FoldedH : FullH);
        if (_foldTb != null)
        {
            // Déplié : « − » (replier) comme la réf ; replié : « + » (déplier).
            _foldTb.TextureNormal = f ? _fiN : _foN;
            _foldTb.TextureHover = f ? _fiH : _foH;
            _foldTb.TexturePressed = f ? _fiP : _foP;
        }
        // Au dépli : Refresh restaure la visibilité des panes selon
        // l'onglet et le contenu.
        if (!f) Refresh();
    }

    /// <summary>Montage de secours — panneau gris translucide à bord
    /// clair, x W-364..W-10, y 272 (ref QuestTracker.png), visuellement
    /// proche du layout natif.</summary>
    private void BuildFallback()
    {
        var panel = new PanelContainer { Name = "TrackerPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        HudRoot.Pin(panel, 1, 0, 1, 0, -364, 272, -10, 358);
        var sb = EeSkin.GreyPanel();
        // 3.2.6 : fond nettement plus sombre — lisible sur fonds clairs.
        sb.BgColor = Color.Color8(20, 21, 24, 225);
        sb.ContentMarginLeft = 8; sb.ContentMarginRight = 8;
        sb.ContentMarginTop = 6; sb.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", sb);
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        panel.AddChild(vbox);

        // En-tête : losanges-liens + bouton « − » de repli.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 0);
        // M6 : cases à cocher (◇ décochée / ◆ cochée).
        var portal = MakeLink(MissionCounters.PortalLabel(false), 172);
        portal.MouseFilter = MouseFilterEnum.Stop;
        portal.MouseDefaultCursorShape = CursorShape.PointingHand;
        portal.GuiInput += e =>
        {
            if (e is not InputEventMouseButton
                { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            _usePortal = !_usePortal;
            portal.Text = MissionCounters.PortalLabel(_usePortal);
        };
        MissionCounters.FreePortalChanged += () =>
        {
            if (GodotObject.IsInstanceValid(portal))
                portal.Text = MissionCounters.PortalLabel(_usePortal);
        };
        head.AddChild(portal);
        var bg = MakeLink(UiTextEn.Translate("◆ Background"), 130);
        bg.MouseFilter = MouseFilterEnum.Stop;
        bg.MouseDefaultCursorShape = CursorShape.PointingHand;
        bg.GuiInput += e =>
        {
            if (e is not InputEventMouseButton
                { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            _bgOn = !_bgOn;
            bg.Text = UiTextEn.Translate(
                _bgOn ? "◆ Background" : "◇ Background");
            panel.AddThemeStyleboxOverride("panel",
                _bgOn ? sb : new StyleBoxEmpty());
        };
        head.AddChild(bg);
        var fold = new Button { Text = "—", CustomMinimumSize = new Vector2(30, 26) };
        fold.AddThemeStyleboxOverride("normal", FoldBox());
        fold.AddThemeStyleboxOverride("hover", FoldBox());
        fold.AddThemeStyleboxOverride("pressed", FoldBox());
        fold.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        fold.AddThemeColorOverride("font_color", Color.Color8(200, 200, 200));
        fold.TooltipText = UiTextEn.Translate("Hide");
        head.AddChild(fold);
        vbox.AddChild(head);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(body);
        fold.Pressed += () => body.Visible = !body.Visible;

        var btns = new HBoxContainer();
        btns.AddThemeConstantOverride("separation", 26);
        var track = MakeButton(UiTextEn.Translate("Track Quests"));
        var unacq = MakeButton(UiTextEn.Translate("Unacquired Quests"));
        // M6 : onglets — « Track Quests » = quêtes suivies (NCID 186
        // côté serveur), « Unacquired Quests » = quêtes non acceptées.
        track.ToggleMode = unacq.ToggleMode = true;
        track.ButtonPressed = true;
        track.Pressed += () =>
        {
            _showUnacquired = false;
            track.ButtonPressed = true; unacq.ButtonPressed = false;
            Refresh();
        };
        unacq.Pressed += () =>
        {
            _showUnacquired = true;
            track.ButtonPressed = false; unacq.ButtonPressed = true;
            Refresh();
        };
        track.AddThemeStyleboxOverride("pressed", EeSkin.DarkButton(true));
        unacq.AddThemeStyleboxOverride("pressed", EeSkin.DarkButton(true));
        btns.AddChild(track); btns.AddChild(unacq);
        body.AddChild(btns);

        // Liste scrollable (barre bleue native à droite).
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _list = new VBoxContainer { Name = "QuestList" };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_list);
        body.AddChild(_scroll);
        Refresh();
    }

    private static StyleBoxFlat FoldBox() => new()
    {
        BgColor = Color.Color8(40, 40, 42, 235),
        BorderColor = Color.Color8(110, 110, 110),
        BorderWidthTop = 1, BorderWidthBottom = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
    };

    /// <summary>Registre de quêtes reçu (348) — reconstruit la liste
    /// des quêtes suivies (Trace ≠ 0) ou en cours (FMS 3/4).</summary>
    public void SetMissions(ZoneMissionStates m)
    {
        MissionCounters.FreePortal = m.FreePortal;
        _active.Clear();
        // État FMS du registre : 4 = objectifs remplis, à rendre ; 1/5 =
        // terminée (ne doit plus s'afficher même si son slot Trace
        // traîne). Avant, _ready n'était jamais rempli au login → le
        // rendu de quête n'apparaissait qu'après un 391 en direct.
        _ready.Clear();
        var fms = m.Fms.ToDictionary(f => (int)f.Mid, f => (int)f.State);
        _fms.Clear();
        foreach (var (k, v) in fms) _fms[k] = v;
        foreach (var (mid, st) in fms)
            if (st == 4) _ready.Add(mid);
        // Slots de suivi du HUD prioritaires ; repli : quêtes en cours.
        // Seuls FMS 3/4 sont « en cours » (un slot Trace périmé ou un
        // FMS 0 ne suffit pas).
        foreach (var mid in QuestStateLogic.Visible(m.Trace,
            m.InProgress.Select(p => (int)p.Mid), _fms))
            _active[mid] = MissionText(mid);
        Refresh();
    }

    /// <summary>Liste de suivi modifiée en jeu (shift+clic journal →
    /// C→S 186 ; le serveur ne renvoie pas d'écho — miroir local, la
    /// persistence arrive au 348 du prochain login).</summary>
    public void SetTrace(IEnumerable<int> ids)
    {
        _active.Clear();
        // Même filtre FMS que SetMissions : un slot Trace peut rester
        // non nul après validation (FMS 1/5/0) — ne pas le ré-afficher.
        foreach (var mid in ids)
            if (mid > 0 && QuestStateLogic.IsOngoing(_fms.GetValueOrDefault(mid)))
                _active[mid] = MissionText(mid);
        Refresh();
    }

    /// <summary>Update unitaire (391) — wire mission→zone relayé :
    /// kind=0 `[u8 0][i32 mid][s16 fms][s16 progress]` (état de
    /// quête) ; kind=1 `[u8 1][u32 pmsKey][u16 count][s16 0]`
    /// (compteur d'objectif — « Defeat mob n/req » du HUD natif).
    /// Dans le codec : Op=kind, Key=mid|pmsKey, MissionId=fms|count,
    /// State=progress|0.</summary>
    public void UpdateMission(ZoneUpdateMissionState m)
    {
        if (m.Op == 1)
        {
            // Compteur d'objectif — rafraîchit la ligne n/req.
            MissionCounters.Values[m.Key] = m.MissionId;
            var (mid2, _, _) = MissionObjectives.DecodePmsKey(m.Key);
            // Chaque avancement d'objectif : ZoneBanner au centre (doc HUD).
            if (MissionObjectives.ProgressLine(m.Key, m.MissionId) is { } pl)
                HudExtensions.FindHud()?.Banner?.ShowSystemText(pl, 3.0);
            // Un compteur n'annule pas une quête déjà terminée (FMS 1/5).
            if (mid2 > 0 && !_active.ContainsKey(mid2) &&
                (!_fms.TryGetValue(mid2, out var st2) ||
                 QuestStateLogic.IsOngoing(st2)))
            {
                _active[mid2] = MissionText(mid2);
            }
            Refresh();
            return;
        }
        var mid = (int)m.Key;
        if (mid == 0) return;
        var fms = m.MissionId;                  // u16 = état FMS
        if (fms == 0) _fms.Remove(mid); else _fms[mid] = fms;
        if (fms is 1 or 5 or 0)                 // done / checked / reset
        {
            _ready.Remove(mid);
            if (_active.Remove(mid)) Refresh();
        }
        else
        {
            if (fms == 4) _ready.Add(mid);
            if (!_active.ContainsKey(mid))
                _active[mid] = MissionText(mid);
            Refresh();
        }
    }

    /// <summary>Titre + objectif localisés d'une quête via
    /// <see cref="TextResolver"/> (t_mission.ini EN : col1 titre,
    /// col2 zone, col3 description) ; repli « Quest #id » hors
    /// table.</summary>
    private static (string, string) MissionText(int mid)
    {
        var title = TextResolver.MissionTitle(mid);
        var goal = TextResolver.MissionDescription(mid)
            ?? TextResolver.MissionZone(mid) ?? "";
        return (title ?? UiTextEn.Translate($"Quest #{mid}"), goal);
    }

    /// <summary>1 = un repeint différé <see cref="SlotsChanged"/> est
    /// déjà posté pour la fin de la frame courante.</summary>
    private bool _slotsDirty;

    /// <summary>Flush du repeint coalescé (hook SlotsChanged).</summary>
    private void FlushSlotsChanged()
    {
        _slotsDirty = false;
        Refresh();
    }

    /// <summary>Repeint la liste des quêtes suivies, puis ajuste la plaque au contenu.</summary>
    private void Refresh()
    {
        RefreshCore();
        // Différé : la taille combinée de la liste n'est fiable
        // qu'après le reflow de la frame.
        CallDeferred(nameof(AdjustPlate));
    }

    /// <summary>La plaque s'ajuste au contenu (en-tête 84 px + corps
    /// borné [56, ScrollH+12]) au lieu du 400 px fixe (gros bloc noir
    /// vide) — min/max gardent le cadre compact et scrollable.</summary>
    private void AdjustPlate()
    {
        if (_backgrand == null || _folded) return;
        var body = 0f;
        if (_showUnacquired || _active.Count > 0)
        {
            var content = (_showUnacquired ? _unacqList : _list)
                ?.GetCombinedMinimumSize().Y ?? ScrollH;
            body = Mathf.Clamp(content + 14f, 56f, ScrollH + 12f);
        }
        _backgrand.AnchorBottom = 0f;
        _backgrand.OffsetBottom = 84f + body;
        // Les panes scroll s'arrêtent au bas de la plaque (sinon leur
        // zone vide déborde du cadre quand la liste est courte).
        if (_scroll != null) _scroll.OffsetBottom = 84f + body - 4f;
        if (_recommend != null)
            _recommend.OffsetBottom = 84f + body - 4f;
    }

    private void RefreshCore()
    {
        if (_list == null) return;
        // RemoveChild avant QueueFree : sinon anciens + nouveaux
        // enfants coexistent jusqu'à la fin de frame → reflow visible.
        foreach (var c in _list.GetChildren())
        {
            _list.RemoveChild(c);
            c.QueueFree();
        }
        if (_unacqList != null)
        {
            // Layout natif : un pane par onglet.
            foreach (var c in _unacqList.GetChildren())
            {
                _unacqList.RemoveChild(c);
                c.QueueFree();
            }
            if (!_folded)
            {
                _recommend!.Visible = _showUnacquired;
                _scroll!.Visible = !_showUnacquired && _active.Count > 0;
            }
            if (_showUnacquired)
            {
                FillMissed(_unacqList);
                return;
            }
        }
        else if (_showUnacquired)
        {
            // Repli : le même pane sert aux deux onglets.
            if (_scroll != null)
            {
                _scroll.Visible = true;
                _scroll.CustomMinimumSize = new Vector2(0, ScrollH);
            }
            FillMissed(_list);
            return;
        }
        // Ref : liste vide = juste les deux boutons (pas de texte).
        else if (_scroll != null)
        {
            _scroll.Visible = _active.Count > 0;
            // ~ 4 quêtes visibles avant scroll (ref : 4 × 2 lignes).
            // Hauteur constante : ne dépend plus du contenu (pas de
            // reflow quand une quête apparaît / change).
            _scroll.CustomMinimumSize = new Vector2(0, ScrollH);
        }
        if (_active.Count == 0) return;
        foreach (var (mid, (title, goal)) in _active)
        {
            var objs = MissionObjectives.ForMission(mid);
            var prog = objs.Select(o => (
                Progress: MissionCounters.Current(o), o.Req)).ToList();
            // « Terminé » : FMS 4 reçu OU tous les compteurs pleins
            // (l'état 4 peut suivre le dernier compteur de quelques ms).
            var ready = _ready.Contains(mid) ||
                QuestObjectiveLogic.AllDone(prog);
            // Titre rouge « Nv. N. Nom » + « Terminé » à droite (ref
            // « questtracker le vrai.png »).
            var head = new HBoxContainer
                { MouseFilter = MouseFilterEnum.Ignore };
            var tl = MakeLine(title, TitleRed);
            tl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            tl.AutowrapMode = TextServer.AutowrapMode.Off;
            tl.ClipText = true;
            head.AddChild(tl);
            if (ready)
            {
                var done = MakeLine(UiTextEn.Translate("Finished"),
                    DoneYellow);
                done.AutowrapMode = TextServer.AutowrapMode.Off;
                done.HorizontalAlignment = HorizontalAlignment.Right;
                head.AddChild(done);
            }
            _list.AddChild(head);
            // Résumé de la quête en vert (ref : paragraphe vert sous le
            // titre, avant les objectifs).
            if (goal.Length > 0)
                _list.AddChild(MakeLine(goal, SummaryGreen));
            for (var i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                var (cur, _) = prog[i];
                var done = cur >= o.Req;
                if (System.Environment.GetEnvironmentVariable("QUEST_DBG") == "1")
                    GD.Print($"[quest-dbg] mid={mid} key={o.PmsKey} item={o.ItemId} " +
                        $"counter={MissionCounters.Values.GetValueOrDefault(o.PmsKey)} " +
                        $"owned={MissionCounters.Owned(o.ItemId)} slots={InventoryWindow.SharedSlots.Count} cur={cur}/{o.Req}");
                var kind = MissionObjectives.DecodePmsKey(o.PmsKey).Type;
                var row = MakeObjective(mid, o.Label, Colors.White, done,
                    QuestObjectiveLogic.ShowsCount(kind)
                        ? $"{cur}/{o.Req}" : null);
                AddUseButton(row.GetChild<Control>(0), o);
                _list.AddChild(row);
            }
            if (objs.Count == 0 && goal.Length == 0)
                _list.AddChild(MakeObjective(mid, UiTextEn.Translate(
                    "Open the quest log for details."), Colors.White,
                    false, null));
            // Quête prête : objectif de rendu natif « Informez X de
            // votre progression. » (FMS 4).
            if (ready)
            {
                var submit = MissionTable.Get(mid)?.SubmitNpc ?? 0;
                _list.AddChild(MakeObjective(mid,
                    MissionObjectives.ReturnLine(submit),
                    new Color(0.55f, 1f, 0.6f), false, null));
            }
        }
    }

    /// <summary>Onglet « Quêtes oubliées » : quêtes prenables au niveau
    /// du joueur (<see cref="QuestCatalog.Missed"/>), ordonnées par
    /// niveau croissant ; PNJ donneur sous le titre (ref « questtracker
    /// quete oublier »), clic = marche vers le PNJ.</summary>
    private void FillMissed(VBoxContainer host)
    {
        var level = World.MobDanger.PlayerLevel;
        var missed = level > 0
            ? QuestCatalog.Missed(MissionTable.All, level,
                id => _fms.GetValueOrDefault(id) is var st && st != 0
                    ? st : _active.ContainsKey(id) ? 3 : 0,
                id => TextResolver.MissionTitle(id), max: 120)
            : new();
        if (missed.Count == 0)
        {
            host.AddChild(MakeLine(UiTextEn.Translate(
                "No forgotten quests at your level."),
                new Color(0.85f, 0.9f, 1f)));
            return;
        }
        foreach (var (def, _, _) in missed)
        {
            host.AddChild(MakeLine(TextResolver.MissionTitle(def.Id)!,
                TitleRed));
            var npc = def.GiverNpc;
            var who = npc != 0 ? TextResolver.NpcName(npc) : null;
            if (who == null) continue;
            var t = TextResolver.NpcTitle(npc);
            var label = t != null ? $"<{t}> {who}" : who;
            var row = MakeObjective(def.Id, label, Colors.White, false,
                null);
            row.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true,
                        ButtonIndex: MouseButton.Left })
                    AutoRoute.ToNpc(this, npc);
            };
            host.AddChild(row);
        }
    }

    /// <summary>Bouton « Use » sur la ligne d'un objectif dont l'item
    /// de quête est en sac ET utilisable (UseType ≠ 0 / enchant
    /// d'usage) — C→S 19 ItemUse sur le stack trouvé (plan 01 étape 7).
    /// Le serveur valide (node requis, texte 0x5c0 sinon) ; l'item de
    /// positionnel (Sound Wave Device…) s'utilise donc depuis le tracker
    /// sans ouvrir l'inventaire.</summary>
    private void AddUseButton(Control row, MissionObjectives.Objective o)
    {
        if (o.ItemId <= 0 || Send == null) return;
        var def = InventoryWindow.LookupDef(o.ItemId);
        if (def == null ||
            !QuestObjectiveLogic.IsUsable(def.UseType, def.UseEnchantId))
            return;
        var stack = QuestObjectiveLogic.FindStack(
            InventoryWindow.SharedSlots.Select(kv =>
                (kv.Key.Item1, kv.Key.Item2, kv.Value.ItemId)),
            o.ItemId);
        if (stack == null) return;
        var (bag, slot) = stack.Value;
        var itemName = TextResolver.ItemName(o.ItemId) ?? $"#{o.ItemId}";
        var tex = def.Code is { Length: > 0 }
            ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png") : null;
        var b = new TextureButton
        {
            TextureNormal = tex, IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(26, 26),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = string.Format(
                UiTextEn.Translate("Use {0}"), itemName),
        };
        b.Pressed += () =>
        {
            if (EdenClient.Combat.PlayerStateLocks.NoUseItem) return;
            _ = Send(new ZoneItemUse
            {
                ContainerType = 0, ContainerIndex = (short)bag,
                Slot = (short)slot,
            });
            GD.Print($"[quest] Use {o.ItemId} (sac{bag},slot{slot})");
        };
        // Ref : l'icône de l'item est à GAUCHE de la pastille.
        row.AddChild(b);
        row.MoveChild(b, 0);
    }

    /// <summary>Ajoute une ligne de quête trackée (texte simple —
    /// mode solo/démo).</summary>
    public void AddTracked(string text)
    {
        _list?.AddChild(MakeLine(text, new Color(0.8f, 0.85f, 0.95f)));
    }

    private static readonly Color TitleRed = Color.Color8(255, 96, 96);
    private static readonly Color SummaryGreen = Color.Color8(120, 240, 110);
    private static readonly Color DoneYellow = Color.Color8(205, 225, 60);

    private static Label MakeLine(string text, Color color,
        bool italic = false)
    {
        var l = new Label { Text = text, AutowrapMode =
            TextServer.AutowrapMode.WordSmart };
        EeSkin.Shadowed(l, 13, color);
        // Contour noir épais + ombre portée : lisible sur fond clair.
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1f));
        l.AddThemeConstantOverride("outline_size", 5);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
        l.MouseFilter = MouseFilterEnum.Ignore;
        return l;
    }

    /// <summary>Ligne objectif (ref « questtracker le vrai 2 ») : pastille
    /// bleue = rempli / grise = à faire, libellé blanc, « n/req » aligné à
    /// droite. Enveloppe PanelContainer : le survol ne change qu'un
    /// StyleBox (même bordure 1 px) → aucune hauteur ne bouge (A5.1).
    /// Clic gauche = route/portail, clic droit = repères carte.</summary>
    private Control MakeObjective(int mid, string text, Color color,
        bool done, string? count)
    {
        var wrap = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        wrap.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        wrap.AddChild(row);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0),
            MouseFilter = MouseFilterEnum.Ignore });
        row.AddChild(new ObjectiveRing
        {
            SizeFlagsVertical = SizeFlags.ShrinkBegin, Done = done,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var l = MakeLine(text, color);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(l);
        if (count != null)
        {
            var c = MakeLine(count, done ? DoneYellow : Colors.White);
            c.AutowrapMode = TextServer.AutowrapMode.Off;
            c.HorizontalAlignment = HorizontalAlignment.Right;
            c.CustomMinimumSize = new Vector2(34, 0);
            row.AddChild(c);
        }
        wrap.GuiInput += e =>
        {
            // Plan 3.2.2 : clic droit = repères de l'objectif sur la
            // minimap / carte (mobs visés en rouge, PNJ en jaune).
            if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Right })
            {
                QuestObjectiveMarks.Toggle(mid, text);
                return;
            }
            if (e is InputEventMouseButton mb && mb.Pressed &&
                mb.ButtonIndex == MouseButton.Left)
            {
                var npc = MissionTable.Get(mid)?.SubmitNpc ?? 0;
                if (_usePortal && Send != null && npc != 0)
                    // NCID 160 [mid][type][npcId][flag=1 : portal stone]
                    _ = Send(new ZoneServerMissionGoto
                    {
                        Field1 = (short)mid, Field2 = 0,
                        Field3 = (short)npc, Field4 = 1,
                    });
                else
                    ObjectiveClicked?.Invoke(mid, text);
            }
        };
        // Survol : simple teinte du texte (pas de rectangle, pas de
        // changement de curseur).
        var hover = new Color(1f, 0.92f, 0.55f);
        wrap.MouseEntered += () =>
            l.AddThemeColorOverride("font_color", hover);
        wrap.MouseExited += () =>
            l.AddThemeColorOverride("font_color", color);
        return wrap;
    }

    /// <summary>Pastille d'objectif (~14 px) : bleue à reflet quand
    /// l'objectif est rempli, anneau gris à cœur sombre sinon (ref).
    /// </summary>
    internal sealed partial class ObjectiveRing : Control
    {
        public ObjectiveRing() =>
            CustomMinimumSize = new Vector2(16, 16);

        public bool Done { get; set; }

        public override void _Draw()
        {
            var c = new Vector2(8, 9f);
            if (Done)
            {
                DrawCircle(c, 7f, new Color(0.78f, 0.9f, 1f, 0.95f));
                DrawCircle(c, 5.6f, Color.Color8(52, 140, 232));
                DrawCircle(c + new Vector2(-1.2f, -1.4f), 2.4f,
                    new Color(0.85f, 0.95f, 1f, 0.9f));
                return;
            }
            DrawCircle(c, 7f, Color.Color8(168, 168, 164));
            DrawCircle(c, 4.6f, Color.Color8(52, 52, 56));
        }
    }

    private static Label MakeLink(string text, int width)
    {
        var l = new Label
        {
            Text = text, CustomMinimumSize = new Vector2(width, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        EeSkin.Shadowed(l, 13, Color.Color8(60, 170, 235));
        return l;
    }

    private static Button MakeButton(string text)
    {
        var b = new Button
        {
            Text = text, CustomMinimumSize = new Vector2(146, 26),
        };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.AddThemeColorOverride("font_color", Color.Color8(235, 235, 235));
        b.AddThemeStyleboxOverride("normal", EeSkin.DarkButton());
        b.AddThemeStyleboxOverride("hover", EeSkin.DarkButton(true));
        b.AddThemeStyleboxOverride("pressed", EeSkin.DarkButton(true));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }
}

/// <summary>Store partagé tracker / journal : compteurs d'objectifs
/// (391 kind=1, clé PMS → count) et état « Portal Stone gratuite »
/// (348 Pms16 mid=0, voir <see cref="ZoneMissionStates.FreePortal"/>).</summary>
public static class MissionCounters
{
    public static Dictionary<uint, int> Values { get; } = new();

    /// <summary>Quantité possédée (sacs connus) d'un item de quête.</summary>
    public static int Owned(int itemId)
    {
        if (itemId <= 0) return 0;
        var def = InventoryWindow.LookupDef(itemId);
        var n = 0;
        foreach (var (_, it) in InventoryWindow.SharedSlots)
            if (it.ItemId == itemId)
                n += Math.Max(1, StorageItemCell.StackCount(it, def));
        return n;
    }

    /// <summary>« n » de « n/req » d'un objectif : compteur 391 ET sac
    /// (<see cref="QuestObjectiveLogic.Progress"/>).</summary>
    public static int Current(MissionObjectives.Objective o)
    {
        var kind = MissionObjectives.DecodePmsKey(o.PmsKey).Type;
        return QuestObjectiveLogic.Progress(kind,
            Values.GetValueOrDefault(o.PmsKey), o.ItemId,
            Owned(o.ItemId), o.Req);
    }

    private static bool _free;
    public static event Action? FreePortalChanged;

    public static bool FreePortal
    {
        get => _free;
        set { if (_free == value) return; _free = value; FreePortalChanged?.Invoke(); }
    }

    /// <summary>« ◇/◆ Use Portal Stone [(gratuit)] ».</summary>
    public static string PortalLabel(bool on) =>
        (on ? "◆" : "◇") + UiTextEn.Translate(" Use Portal Stone") +
        (_free ? " " + UiTextEn.Translate("(free)") : "");
}
