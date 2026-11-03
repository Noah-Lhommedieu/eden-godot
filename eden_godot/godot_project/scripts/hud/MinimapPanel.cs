using System.Collections.Concurrent;
using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.Ui;
using EdenClient.World;

namespace EdenClient.Hud;

/// <summary>
/// Minimap haut-droite — instancie le layout natif
/// <c>RadarUI_Ver2.layout</c> (cadre <c>Radar:RadarBody</c>, boutons
/// mail/zoom/globe/rank/territory, plaques horloge EdenClock).
///
/// Pilotée par le code : <c>mapImage</c> reçoit la texture
/// <c>UI/map/&lt;code&gt;s.png</c>, <c>TitleText</c> le nom de zone
/// (t_node.ini), <c>CoordText</c> la position, EdenClock l'heure.
///
/// C6 : la carte est clippée par <c>mapFrame</c> et supporte le zoom
/// (×1 → ×4 via <c>ZoomInBtn</c>/<c>ZoomOutBtn</c> natifs) — la vue
/// reste centrée sur le joueur ; un clic sur la carte panne la vue
/// sur le point cliqué pendant ~5 s (coordonnées monde→pixels :
/// <see cref="WorldSize"/> = [globe_data] des sXXX.ini). Marqueurs
/// entités rafraîchis ~4 Hz depuis le nœud <c>NetEntities</c>
/// (<see cref="EntityRegistry"/>) : NPC vert, mob rouge, joueur
/// bleu, trésor or. <c>GlobleBtn</c> ouvre la carte monde.
/// </summary>
public partial class MinimapPanel : Control
{
    /// <summary>Taille du monde (u) couverte par la texture 1024² —
    /// [globe_data] des sXXX.ini : 576.</summary>
    /// <summary>Côté du monde Eden d'une map ([globe_data] sXXX.ini).</summary>
    public const float WorldSize = 576f;

    /// <summary>H11 : côté réel du monde de la map courante
    /// (TilesPerRow × TileWorldSize : S001 = 640, S009 = 576…). La
    /// constante 576 faussait carte, minimap et marqueurs.</summary>
    public float WorldSpan
    {
        // [globe_data] de la scène d'abord (== node+0x28 natif, prouvé
        // sur S001/S002/S009 par recalage texture↔terrain) ; sinon la
        // taille fsm poussée par GameScene.
        get => _scene is { MapHeight: > 1 } sc ? sc.MapHeight : _fsmSpan;
        private set => _fsmSpan = value;
    }
    private float _fsmSpan = WorldSize;

    public void SetWorldSize(float span)
    {
        if (span > 1f) WorldSpan = span;
        UpdateView();
    }

    /// <summary>Marge (px de texture) entre le bord de <c>&lt;S&gt;s.png</c>
    /// (1024², 1 px = 1 unité monde) et l'origine du monde. Natif
    /// <c>FUN_007ef8f0</c> @0x7ef8f0 (rf_game) : centre de vue =
    /// (x + DAT_00ed9868, node.h − y + DAT_00ed9868) avec la constante
    /// ≈ 100 (ajusté par corrélation, pas lu en .rdata) ; vérifié : la texture <c>m</c> (512²) est
    /// exactement le recadrage (100,100,span,span) de la <c>s</c>.</summary>
    public const float TexMargin = 100f;

    /// <summary>Monde Eden (x, y) → coordonnées normalisées [0,1] de la
    /// texture <c>s</c> affichée (axe Y inversé : le nord = +y est en
    /// haut de l'image).</summary>
    public Vector2 WorldToNorm(float x, float y)
    {
        var ts = _map?.Texture is { } t
            ? new Vector2(t.GetWidth(), t.GetHeight())
            : new Vector2(1024f, 1024f);
        return new Vector2((x + TexMargin) / ts.X,
            (WorldSpan - y + TexMargin) / ts.Y);
    }

    /// <summary>Inverse de <see cref="WorldToNorm"/>.</summary>
    public Vector2 NormToWorld(Vector2 n)
    {
        var ts = _map?.Texture is { } t
            ? new Vector2(t.GetWidth(), t.GetHeight())
            : new Vector2(1024f, 1024f);
        return new Vector2(n.X * ts.X - TexMargin,
            WorldSpan - (n.Y * ts.Y - TexMargin));
    }

    /// <summary>Région map (<c>&lt;S&gt;m.png</c>, recadrage exact du
    /// monde) : (x, y) → [0,1]² (y inversé).</summary>
    public static Vector2 WorldToRegionNorm(float x, float y, float span)
        => new(x / span, 1f - y / span);

    /// <summary>Inverse de <see cref="WorldToRegionNorm"/>.</summary>
    public static Vector2 RegionNormToWorld(Vector2 n, float span)
        => new(n.X * span, (1f - n.Y) * span);

    /// <summary>Position sol Eden du joueur (x, y).</summary>
    public Vector2 SelfPos => new(_x, _y);
    /// <summary>Code de la map courante (« S002 »).</summary>
    public string MapCode { get; private set; } = "";
    /// <summary>Scène de la map courante (PNJ, monstres, portails).</summary>
    public SceneTable.Scene? Scene => _scene;

    /// <summary>Durée du pan manuel (clic) avant retour au joueur.</summary>
    private const float PanSeconds = 5f;

    /// <summary>Paliers de zoom (× taille de la texture affichée).</summary>
    private static readonly float[] ZoomSteps = { 1f, 1.5f, 2.25f, 3.5f };

    private Control? _frame;          // mapFrame (clip) ou panel repli
    private TextureRect? _map;
    private TextureRect? _arrow;
    private Label? _title, _coord, _clockLocal, _clockSrv;
    private HudRoot? _hud;
    private float _x, _y, _yaw;
    /// <summary>Ref : vue rapprochée du quartier par défaut (×2.25).</summary>
    private int _zoomIdx = 2;
    private Vector2? _panNorm;        // vue pannée (clic) — null = joueur
    private double _panUntil;
    private double _nextMarkerScan;

    /// <summary>Node courant (numéro du code Sxxx — alimente la carte
    /// monde <see cref="WorldMapWindow"/> et les marqueurs 392).</summary>
    public int NodeId { get; private set; }

    // Pool de marqueurs handle→TextureRect (enfants de _map).
    private readonly Dictionary<uint, TextureRect> _markers = new();
    private EntityRegistry? _registry;

    // ---- lot S9/S10 : binds réseau + marqueurs avancés -------------
    private EdenSession? _session;
    private readonly ConcurrentQueue<Action> _pending = new();
    /// <summary>Symboles quête par npcUid (345 — mêmes données que les
    /// billboards 3D de <see cref="MissionBridge"/>, bind indépendant).</summary>
    private readonly Dictionary<int, (short S1, short S2)> _npcSymbols = new();
    /// <summary>Positions coéquipiers (392) : charId → (node, x, y).</summary>
    private readonly Dictionary<int, (short Node, float X, float Y)>
        _partyPos = new();
    /// <summary>Marqueurs portails (aires GATEWAY de la scène).</summary>
    private readonly List<TextureRect> _portalMarkers = new();
    private readonly Dictionary<int, TextureRect> _partyMarkers = new();
    private SceneTable.Scene? _scene;

    public void Build(HudRoot hud)
    {
        _hud = hud;
        var root = hud.Build("RadarUI_Ver2.layout");
        if (root == null)
        {
            // Repli : cadre + texture carrée + titre.
            var p = new Panel();
            p.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
            p.Position = new Vector2(-210, 40);
            p.Size = new Vector2(210, 180);
            p.ClipContents = true;
            AddChild(p);
            _frame = p;
            _map = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            _map.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _map.OffsetTop = 20;
            p.AddChild(_map);
            _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            p.AddChild(_title);
            _coord = new Label();
            _coord.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
            p.AddChild(_coord);
        }
        else
        {
            AddChild(root);
            // Racine sérialisée à x=0 (haut-GAUCHE) : la minimap native
            // est haut-droite — épinglée explicitement (240 px, marge
            // 8 px des bords ; élargie légèrement vs la largeur native
            // 210 px qui rendait la rangée de boutons serrée).
            HudRoot.Pin(root, 1, 0, 1, 0, -270, 8, -8, 218);
            _frame = TopLeftVitals.Find<Control>(root, "mapFrame");
            _map = TopLeftVitals.Find<TextureRect>(root, "mapImage");
            _title = TopLeftVitals.Find<Label>(root, "TitleText");
            _coord = TopLeftVitals.Find<Label>(root, "CoordText");
            _clockLocal = TopLeftVitals.Find<Label>(root, "Row1_Text");
            _clockSrv = TopLeftVitals.Find<Label>(root, "Row2_Text");

            if (_frame != null) _frame.ClipContents = true;
            // Le cadre radar (RadarBody, décor) est un sibling
            // AU-DESSUS de mapFrame : en MouseFilter.Stop il avale
            // les clics avant mapImage → Ignore (ses boutons enfants
            // gardent leur propre filtre et restent cliquables).
            var bg = TopLeftVitals.Find<Control>(root, "BackGrand");
            if (bg != null) bg.MouseFilter = MouseFilterEnum.Ignore;
            var attflg = TopLeftVitals.Find<Control>(root, "attflg");
            if (attflg != null) attflg.MouseFilter =
                MouseFilterEnum.Ignore;
            // Boutons natifs : zoom +/− et globe → carte monde.
            WireButton(root, "ZoomInBtn", () => Zoom(1), "Zoom in");
            WireButton(root, "ZoomOutBtn", () => Zoom(-1), "Zoom out");
            WireButton(root, "GlobleBtn",
                () => _hud?.ToggleWorldMap(), "World map");
            // Rangée services (hud-interfaces §Minimap) : chaque bouton
            // bascule la fenêtre montée par HudExtensions (Visible —
            // convention ToggleByName du menu bas-droite).
            WireButton(root, "mailBtn",
                () => ToggleNode("MailWnd"), "Mail");
            // M7 (plan v4, mapping Radar.imageset) : Lty bleu = autels
            // Star Diamond / Eden Crystal ; IGLty rose = autel Eden Gem.
            WireButton(root, "InGameBtn",
                () => ToggleNode("AltarGemWnd"), UiTextEn.Translate("Eden Gem Altar"));
            WireButton(root, "FtyBtn",
                () => ToggleNode("AltarStarWnd"),
                UiTextEn.Translate("Star Diamond / Eden Crystal Altar"));
            WireButton(root, "ALBtn",
                () => ToggleNode("LotteryWnd"), "Eden Altar");
            WireButton(root, "BattleBtn",
                () => ToggleNode("ColosseumWnd"),
                "Arena (3v3, 10v10, Monster Arena)");
            WireButton(root, "RankBtn",
                () => ToggleNode("RankWnd"), UiTextEn.Translate("Rankings"));
            WireButton(root, "TerritoryBtn",
                () => ToggleNode("BattlefieldWnd"),
                "Battlefields (GvG, Territory War…)");
            WireButton(root, "TeamBtn",
                () =>
                {
                    // SocialWnd = racine FullRect toujours visible :
                    // on bascule son panneau interne (TogglePanel).
                    if (GetTree().Root.FindChild("SocialWnd", true,
                            false) is SocialWindow sw) sw.TogglePanel();
                }, "Party / Social");
            WireButton(root, "CooperationBtn",
                () => ToggleNode("RecruitWnd"),
                "Cooperation — party recruitment");
            // HideBtn natif : replie la rangée de boutons services.
            WireButton(root, "HideBtn", ToggleExtraButtons,
                "Hide / show buttons");
            // Ref : « œil » rouge (OpenHiding) au lieu du carré sombre.
            EeSkin.ImageCheck(hud, TopLeftVitals.Find<Control>(root,
                "HideBtn"), "Radar", "OpenHidingNormal",
                "CloseHidingNormal");
            // Bouton « Events (daily rewards) » : déplacé dans
            // TopLeftVitals (sous la grille de slots) ; l'auto-tri du
            // sac est dans l'inventaire.
        }
        // Marqueur joueur : triangle cyan pointant dans la direction de
        // la caméra (repli : yaw du modèle). L'ArrowImage natif n'a pas
        // d'image dans le layout (assignée par le code natif), on fait
        // pareil en procédural.
        if (_map != null)
        {
            // Positions manuelles (zoom) : ancres TopLeft + offsets.
            _map.SetAnchorsPreset(LayoutPreset.TopLeft);
            _arrow = new TextureRect
            {
                Texture = ArrowTexture(),
                CustomMinimumSize = new Vector2(12, 12),
                Size = new Vector2(12, 12),
                PivotOffset = new Vector2(6, 6),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _map.AddChild(_arrow);
            // Clic sur la carte : pan de la vue sur le point monde
            // correspondant pendant <see cref="PanSeconds"/> s.
            _map.GuiInput += OnMapInput;
        }

        // Lot S9 : la minimap est le composant HUD déjà monté par
        // HudRoot pour ce lot — on y accroche le PartyHud et la
        // SocialWindow (même convention que HudRoot.Mount : FullRect
        // + Ignore, les fenêtres gèrent leur propre filtre interne).
        var party = new PartyHud { Name = "PartyHud" };
        party.Build(hud);
        hud.AddChild(party);
        party.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var social = new SocialWindow { Name = "SocialWnd" };
        social.Build(hud);
        hud.AddChild(social);
        social.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Vague 1-A : fenêtre guilde (touche G) — même convention de
        // montage ; ses données passent par la pompe monde du
        // SocialWindow ci-dessus (GuildState).
        var guild = new GuildWindow { Name = "GuildWnd" };
        guild.Build(hud);
        hud.AddChild(guild);
        guild.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Branche un bouton natif du layout (BaseButton) +
    /// tooltip explicite.</summary>
    private static void WireButton(Control root, string leaf,
        Action act, string? tip = null)
    {
        var b = TopLeftVitals.Find<BaseButton>(root, leaf);
        if (b == null) return;
        if (tip != null) b.TooltipText = tip;
        b.Pressed += act;
    }

    /// <summary>Bascule la visibilité d'un nœud monté (fenêtre
    /// HudExtensions) — équivalent de <c>HudRoot.ToggleByName</c>
    /// pour les boutons radar.</summary>
    private void ToggleNode(string nodeName)
    {
        if (GetTree().Root.FindChild(nodeName, true, false)
                is Control c)
            HudWindows.ToggleIn(c);
    }

    /// <summary>HideBtn natif : replie la rangée des boutons services
    /// (tous sauf mail/zoom/globe/hide).</summary>
    private void ToggleExtraButtons()
    {
        _extraHidden = !_extraHidden;
        var root = GetChildOrNull<Control>(0);
        if (root == null) return;
        foreach (var leaf in ExtraButtons)
            if (TopLeftVitals.Find<Control>(root, leaf) is { } b)
                b.Visible = !_extraHidden;
        foreach (var b in _addedButtons)
            if (GodotObject.IsInstanceValid(b))
                b.Visible = !_extraHidden;
    }

    private bool _extraHidden;
    private readonly List<TextureButton> _addedButtons = new();
    private static readonly string[] ExtraButtons =
    {
        "InGameBtn", "FtyBtn", "ALBtn", "BattleBtn", "RankBtn",
        "TerritoryBtn", "TeamBtn", "CooperationBtn",
    };

    /// <summary>Zoom palier ±1 (×1 → ×3.5), reclampé.</summary>
    public void Zoom(int dir)
    {
        _zoomIdx = Mathf.Clamp(_zoomIdx + dir, 0, ZoomSteps.Length - 1);
        UpdateView();   // M7 : sinon le palier n'apparaît qu'au prochain pas
    }

    /// <summary>Zone affichée : texture <c>&lt;code&gt;s.png</c> + nom
    /// anglais t_node.ini via <see cref="TextResolver"/> (id = numéro
    /// du code Sxxx) — repli = code. Recharge aussi la scène pour les
    /// marqueurs portails (aires GATEWAY).</summary>
    public void SetZone(string mapCode, string? nameOverride = null)
    {
        var name = nameOverride ?? ZoneName(mapCode);
        MapCode = mapCode;
        if (_title != null) _title.Text = name;
        if (_map != null)
        {
            var tex = AssetCatalog.GetTexture($"UI/map/{mapCode}s.png");
            if (tex != null) _map.Texture = tex;
        }
        // S<d3> → nodeId ; recharge les portails de la map.
        if (mapCode.Length > 1 &&
            int.TryParse(mapCode[1..], out var nodeId) &&
            nodeId != NodeId)
        {
            NodeId = nodeId;
            LoadScenePortals(nodeId);
            _partyPos.Clear();
        }
        UpdateCoord();
    }

    /// <summary>Charge <c>data/scene/sNNN.ini</c> pour les marqueurs
    /// d'aires : les aires REVIVE (statues de résurrection) portent
    /// l'icône native map010 (« Revival Point » de la légende ref).
    /// Les aires GATEWAY sont des triggers invisibles de quête/donjon —
    /// jamais dessinées par le natif (le ref ne montre aucune porte
    /// dessus ; les « Portal » de la légende sont les PNJ 傳送陣).</summary>
    private void LoadScenePortals(int nodeId)
    {
        _scene = null;
        foreach (var m in _portalMarkers)
            if (GodotObject.IsInstanceValid(m)) m.QueueFree();
        _portalMarkers.Clear();
        var path = AssetCatalog.ResolveRawAsset(
            $"data/scene/s{nodeId:D3}.ini");
        if (path == null) return;
        try { _scene = SceneTable.Load(path); }
        catch (Exception e)
        {
            GD.PrintErr($"[minimap] scene s{nodeId:D3} : {e.Message}");
        }
        if (_scene == null || _map == null) return;
        foreach (var a in _scene.EventAreas)
        {
            if (!string.Equals(a.Type, "REVIVE",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            var dot = new TextureRect
            {
                Texture = IconTex("map010", new Color(0.4f, 0.9f, 1f)),
                CustomMinimumSize = new Vector2(16, 16),
                Size = new Vector2(16, 16),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Pass,
                TooltipText = a.Description.Length > 0
                    ? a.Description : UiTextEn.Translate("Revival Point"),
            };
            _map.AddChild(dot);
            _portalMarkers.Add(dot);
            // Position en coords normalisées monde.
            dot.SetMeta("wx", a.X);
            dot.SetMeta("wy", a.Y);
        }
    }

    /// <summary>Position Eden (x,y monde) + yaw du modèle.</summary>
    public void SetSelfPos(float x, float y, float yaw)
    {
        _x = x; _y = y; _yaw = yaw;
        UpdateCoord();
    }

    /// <summary>Région d'étal joueur contenant la position self
    /// courante — miroir de <c>CheckStallRegion</c> natif (dist² X/Y
    /// monde ≤ rayon², regions = [position] col7 ∈ 600-609 de
    /// sNNN.ini). Null hors scène/hors toute aire → l'ouverture
    /// d'étal (C→S 77) serait rejetée silencieusement serveur.</summary>
    public (int RegionType, float X, float Y, float Radius)?
        StallRegionAtSelf()
    {
        if (_scene == null) return null;
        foreach (var (type, r) in _scene.StallRegions)
        {
            var dx = _x - r.X;
            var dy = _y - r.Y;   // coords Eden x,y du plan du sol
            if (dx * dx + dy * dy <= r.Radius * r.Radius)
                return (type, r.X, r.Y, r.Radius);
        }
        return null;
    }

    /// <summary>Clic carte → panne la vue sur le point monde cliqué
    /// (coords écran _map → normalisées → Eden).</summary>
    private void OnMapInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } w &&
            w.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Zoom(w.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
            _map?.AcceptEvent();
            return;
        }
        if (e is not InputEventMouseButton mb || !mb.Pressed ||
            mb.ButtonIndex != MouseButton.Left || _map == null)
            return;
        var norm = new Vector2(
            mb.Position.X / Math.Max(_map.Size.X, 1f),
            mb.Position.Y / Math.Max(_map.Size.Y, 1f));
        _panNorm = norm;
        _panUntil = Time.GetTicksMsec() / 1000.0 + PanSeconds;
    }

    private void UpdateCoord()
    {
        if (_coord != null)
            _coord.Text = UiTextEn.Translate($"X:{(int)_x}, Y:{(int)_y}");
    }

    /// <summary>Vue : carte dimensionnée frame×zoom, centrée sur le
    /// joueur (ou le point panné) ; flèche et marqueurs enfants de
    /// _map en coords normalisées.</summary>
    private void UpdateView()
    {
        if (_map == null || _frame == null) return;
        var fs = _frame.Size;
        if (fs.X <= 0 || fs.Y <= 0) return;
        var zoom = ZoomSteps[_zoomIdx];
        var ms = fs * zoom;
        _map.Size = ms;
        // Centre de vue : joueur par défaut, point cliqué pendant le
        // pan ; clamp pour ne jamais montrer hors de la texture.
        var norm = _panNorm ?? WorldToNorm(_x, _y);
        var pos = fs * 0.5f - norm * ms;
        pos.X = Mathf.Clamp(pos.X, fs.X - ms.X, 0f);
        pos.Y = Mathf.Clamp(pos.Y, fs.Y - ms.Y, 0f);
        _map.Position = pos;
        if (_arrow != null)
        {
            var an = WorldToNorm(_x, _y);
            _arrow.Position = new Vector2(
                an.X * ms.X - 6f, an.Y * ms.Y - 6f);
            _arrow.Rotation = ArrowYaw();
        }
        // Portails : positions figées en coords normalisées (meta wx/wy).
        foreach (var m in _portalMarkers)
        {
            if (!GodotObject.IsInstanceValid(m)) continue;
            var wx = (float)m.GetMeta("wx", 0f).AsSingle();
            var wy = (float)m.GetMeta("wy", 0f).AsSingle();
            var pn = WorldToNorm(wx, wy);
            m.Position = new Vector2(pn.X * ms.X - 8f, pn.Y * ms.Y - 8f);
        }
    }

    /// <summary>Yaw de la flèche : direction caméra si dispo (forward
    /// -Z → Eden (dx, -dz)), repli = yaw du modèle (_yaw). Le triangle
    /// de <see cref="ArrowTexture"/> pointe vers le haut (-Y écran) à
    /// rotation 0 ; un yaw Eden de 0 (vers -Y monde) doit donner 0.
    /// </summary>
    private float ArrowYaw()
    {
        if (GetViewport().GetCamera3D() is { } cam)
        {
            var f = -cam.GlobalTransform.Basis.Z;   // forward caméra
            // Eden (ex,ey) = (gx,-gz) ; écran : +ex → droite, +ey → bas.
            // Angle depuis +X écran (atan2 y,x) + 90° car la texture
            // pointe vers -Y.
            return Mathf.Atan2(-f.Z, f.X) + Mathf.Pi / 2f;
        }
        return _yaw;
    }

    /// <summary>Triangle blanc→cyan pointant « nord » (-Y) dans une
    /// texture 16² — cache statique (une allocation).</summary>
    private static ImageTexture? _arrowTex;
    private static ImageTexture ArrowTexture()
    {
        if (_arrowTex != null) return _arrowTex;
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        // Triangle : sommet haut-centre, base bas — remplissage par
        // scanline (moitié de largeur croissante avec y).
        var c = new Color(0.2f, 0.9f, 1f);
        var edge = new Color(0.05f, 0.25f, 0.35f);
        for (var y = 1; y < 15; y++)
        {
            var half = (int)(6f * y / 14f);
            for (var x = 8 - half; x <= 8 + half; x++)
                img.SetPixel(x, y,
                    (x == 8 - half || x == 8 + half) ? edge : c);
        }
        _arrowTex = ImageTexture.CreateFromImage(img);
        return _arrowTex;
    }

    /// <summary>Marqueurs entités : position monde→normalisée→pixels
    /// de la texture zoomée (enfants de _map, suivent zoom+pan).</summary>
    private void RefreshMarkers()
    {
        if (_map == null) return;
        _registry ??= GetTree().Root
            .FindChild("NetEntities", true, false) as EntityRegistry;
        var ms = _map.Size;
        var seen = new HashSet<uint>();
        if (_registry != null)
        {
            foreach (var (handle, e) in _registry.Entities)
            {
                if (!GodotObject.IsInstanceValid(e)) continue;
                // Plan 3.2.2 : AUCUN mob en rouge en général. Un mob
                // n'apparaît (en rouge) que s'il est la cible d'un
                // objectif de quête désigné par clic droit dans le
                // tracker (QuestObjectiveMarks.MobIds).
                if (e.Kind == 12 &&
                    !QuestObjectiveMarks.MobIds.Contains(e.EntityId))
                    continue;
                var (ex, ey) = WorldConv.ToEdenGround(e.GlobalPosition);
                if (ex < 0 || ey < 0 || ex > WorldSpan || ey > WorldSpan)
                    continue;
                if (!_markers.TryGetValue(handle, out var dot))
                {
                    dot = new TextureRect
                    {
                        CustomMinimumSize = new Vector2(16, 16),
                        Size = new Vector2(16, 16),
                        StretchMode =
                            TextureRect.StretchModeEnum.KeepAspectCentered,
                        // Pass : tooltip survolable ET clic relayé à
                        // la carte (pan) dessous.
                        MouseFilter = MouseFilterEnum.Pass,
                        TooltipText = e.DisplayName,
                    };
                    _markers[handle] = dot;
                    _map.AddChild(dot);
                }
                dot.Texture = MarkerTexture(e);
                // Marqueur quête « ! »/« ? » natif au-dessus de l'icône
                // NPC (symboles du 345 — npcUid = EntityId des kind 8).
                var mark = dot.GetNodeOrNull<TextureRect>("QMark");
                if (e.Kind == 8 &&
                    _npcSymbols.TryGetValue(e.EntityId, out var sym))
                {
                    if (mark == null)
                    {
                        mark = new TextureRect
                        {
                            Name = "QMark",
                            CustomMinimumSize = new Vector2(20, 20),
                            Size = new Vector2(20, 20),
                            StretchMode = TextureRect.StretchModeEnum
                                .KeepAspectCentered,
                            Position = new Vector2(-2, -18),
                            MouseFilter = MouseFilterEnum.Ignore,
                        };
                        dot.AddChild(mark);
                    }
                    // Convention MissionBridge : 1 = « ! » offre
                    // (map021 orange), 2 = « ? » à rendre (map023),
                    // sinon « ? » gris suivi (map024).
                    var f = sym.S1 switch
                    {
                        1 => "map021", 2 => "map023", _ => "map024",
                    };
                    mark.Texture = AssetCatalog.GetTexture(
                        $"UI/uiicon/{f}.png");
                    mark.Visible = mark.Texture != null;
                }
                else if (mark != null) mark.Visible = false;
                dot.TooltipText = e.DisplayName;
                var en = WorldToNorm(ex, ey);
                dot.Position = new Vector2(
                    en.X * ms.X - 8f, en.Y * ms.Y - 8f);
                seen.Add(handle);
            }
        }
        // Retire les marqueurs des entités despawnées.
        foreach (var h in _markers.Keys)
        {
            if (seen.Contains(h)) continue;
            if (GodotObject.IsInstanceValid(_markers[h]))
                _markers[h].QueueFree();
            _pendingRemove.Add(h);
        }
        foreach (var h in _pendingRemove) _markers.Remove(h);
        _pendingRemove.Clear();
        RefreshQuestMarks(ms);
        RefreshPartyMarkers(ms);
    }

    private readonly List<TextureRect> _questDots = new();

    /// <summary>Repères d'objectif de quête (clic droit tracker) :
    /// points de spawn des mobs visés en ROUGE, PNJ visés en jaune —
    /// seulement ceux du node courant (<see cref="QuestObjectiveMarks"/>).</summary>
    private void RefreshQuestMarks(Vector2 ms)
    {
        if (_map == null) return;
        var marks = QuestObjectiveMarks.Marks
            .Where(m => m.NodeId == NodeId).ToList();
        while (_questDots.Count < marks.Count)
        {
            var d = new TextureRect
            {
                CustomMinimumSize = new Vector2(12, 12),
                Size = new Vector2(12, 12),
                MouseFilter = MouseFilterEnum.Pass,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            _map.AddChild(d);
            _questDots.Add(d);
        }
        for (var i = 0; i < _questDots.Count; i++)
        {
            var d = _questDots[i];
            if (i >= marks.Count) { d.Visible = false; continue; }
            var mk = marks[i];
            d.Visible = true;
            d.Texture = mk.Mob
                ? IconTex("map053", new Color(1f, 0.15f, 0.1f))
                : MarkerTexture(new Color(1f, 0.85f, 0.2f));
            d.TooltipText = mk.Label;
            var n = WorldToNorm(mk.X, mk.Y);
            d.Position = new Vector2(n.X * ms.X - 6f, n.Y * ms.Y - 6f);
        }
    }

    /// <summary>Marqueurs coéquipiers (392) : points blanc-rose pour
    /// les membres sur le même node — pool charId→dot.</summary>
    private void RefreshPartyMarkers(Vector2 ms)
    {
        if (_map == null) return;
        var seen = new HashSet<int>();
        foreach (var (charId, p) in _partyPos)
        {
            if (p.Node != NodeId) continue;
            if (p.X < 0 || p.Y < 0 || p.X > WorldSpan || p.Y > WorldSpan)
                continue;
            if (!_partyMarkers.TryGetValue(charId, out var dot))
            {
                dot = new TextureRect
                {
                    Texture = MarkerTexture(new Color(1f, 0.6f, 0.85f)),
                    CustomMinimumSize = new Vector2(6, 6),
                    Size = new Vector2(6, 6),
                    MouseFilter = MouseFilterEnum.Pass,
                };
                _partyMarkers[charId] = dot;
                _map.AddChild(dot);
            }
            dot.TooltipText = PartyState.Members
                .TryGetValue((uint)charId, out var tm)
                ? UiTextEn.Translate($"{tm.CharName} (party)") : UiTextEn.Translate($"party #{charId}");
            var qn = WorldToNorm(p.X, p.Y);
            dot.Position = new Vector2(qn.X * ms.X - 3f, qn.Y * ms.Y - 3f);
            dot.Visible = true;
            seen.Add(charId);
        }
        foreach (var cid in _partyMarkers.Keys.ToList())
        {
            if (seen.Contains(cid)) continue;
            if (GodotObject.IsInstanceValid(_partyMarkers[cid]))
                _partyMarkers[cid].QueueFree();
            _partyMarkers.Remove(cid);
        }
        RefreshRadarPing(ms);
    }

    private TextureRect? _pingDot;
    private ulong _pingUntil;

    /// <summary>Ping radar équipe (387) : orbe orange clignotant ~8 s
    /// à la coordonnée reçue.</summary>
    private void RefreshRadarPing(Vector2 ms)
    {
        var active = PartyState.RadarPing is { } ping &&
            Time.GetTicksMsec() < _pingUntil &&
            ping.X >= 0 && ping.X <= WorldSpan &&
            ping.Z >= 0 && ping.Z <= WorldSpan;
        if (!active)
        {
            if (_pingDot != null &&
                GodotObject.IsInstanceValid(_pingDot))
                _pingDot.QueueFree();
            _pingDot = null;
            return;
        }
        var p = PartyState.RadarPing!.Value;
        if (_pingDot == null || !GodotObject.IsInstanceValid(_pingDot))
        {
            _pingDot = new TextureRect
            {
                Texture = MarkerTexture(new Color(1f, 0.55f, 0.15f)),
                CustomMinimumSize = new Vector2(8, 8),
                Size = new Vector2(8, 8),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _map!.AddChild(_pingDot);
        }
        _pingDot.TooltipText = UiTextEn.Translate($"party ping @{(int)p.X},{(int)p.Z}");
        var gn = WorldToNorm(p.X, p.Z);
        _pingDot.Position = new Vector2(gn.X * ms.X - 4f, gn.Y * ms.Y - 4f);
        _pingDot.Visible = (Time.GetTicksMsec() / 400) % 2 == 0;
    }

    private readonly List<uint> _pendingRemove = new();

    /// <summary>Textures 1×1 par couleur de marqueur (cache —
    /// <see cref="AssetCatalog.MakeSolid"/> alloue une texture à
    /// chaque appel, le scan 4 Hz fuirait sinon).</summary>
    private static readonly Dictionary<string, ImageTexture> _dotTex =
        new();

    /// <summary>Icône du marqueur par type d'entité — textures natives
    /// <c>UI/uiicon</c> (RadarUI, table rf_game FUN_007ed830) : PNJ →
    /// icône de fonction <c>mapNNN</c> (<see cref="NpcCategoryTable.IconFile"/>,
    /// défaut = emblème map005 dominant dans le ref), mob → orbe rouge
    /// map053, joueur → orbe bleue map002 ; trésor garde le point or
    /// (pas d'icône native prouvée). Repli point coloré si la texture
    /// manque.</summary>
    private static Texture2D? MarkerTexture(RemoteEntity e) => e.Kind switch
    {
        8 => IconTex(NpcCategoryTable.IconOf(e.EntityId & 0xFFFF),
            new Color(0.6f, 0.4f, 0.9f)),
        12 => IconTex("map053", new Color(1f, 0.3f, 0.25f)),
        _ => e.EntityId > 0
            ? MarkerTexture(new Color(1f, 0.85f, 0.3f))
            : IconTex("map002", new Color(0.4f, 0.7f, 1f)),
    };

    /// <summary>Texture <c>UI/uiicon/{file}.png</c>, repli point de la
    /// couleur donnée si l'icône native est absente.</summary>
    private static Texture2D IconTex(string file, Color fallback) =>
        AssetCatalog.GetTexture($"UI/uiicon/{file}.png")
            ?? MarkerTexture(fallback);

    /// <summary>Texture point d'une couleur arbitraire (portails,
    /// coéquipiers — même cache).</summary>
    private static ImageTexture MarkerTexture(Color c)
    {
        var key = c.ToHtml();
        if (!_dotTex.TryGetValue(key, out var t))
            _dotTex[key] = t = AssetCatalog.MakeSolid(c);
        return t;
    }

    // ------------------------------------------------------------------
    // Binds réseau (345 symboles NPC, 392 positions coéquipiers,
    // 293 reset à la migration) — lazy-bind SessionCarry, même pattern
    // que HudNetBridge/MissionBridge. Les handlers sont multi-abonnés
    // (MissionBridge a son propre 345 — coexistence voulue).
    // ------------------------------------------------------------------

    private void BindSession(EdenSession s)
    {
        _session = s;
        s.OnZone(345, r =>
        {
            var m = new ZoneUpdateNpcSymbols();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                _npcSymbols.Clear();
                foreach (var e in m.Entries)
                    _npcSymbols[e.NpcUid] = (e.Sym1, e.Sym2);
            });
        });
        s.OnZone(392, r =>
        {
            var m = new ZoneTeamMemberPosition();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                _partyPos[m.CharId] = (m.NodeId, m.X, m.Y);
                PartyState.ApplyPosition(m);
            });
        });
        s.OnZone(387, r =>
        {
            var m = new ZoneClientTeamRadarAttention();
            m.Deserialize(r);
            _pending.Enqueue(() =>
            {
                PartyState.ApplyRadarNotice(m);
                _pingUntil = Time.GetTicksMsec() + 8000;
            });
        });
        s.OnZone(293, r =>
        {
            _pending.Enqueue(() =>
            {
                _npcSymbols.Clear();
                _partyPos.Clear();
            });
        });
        GD.Print("[minimap] binds 345/392/387/293 posés");
    }

    public override void _Process(double delta)
    {
        if (_session == null && SessionCarry.Session is { } sess)
            BindSession(sess);
        while (_pending.TryDequeue(out var act))
        {
            try { act(); }
            catch (Exception ex) { GD.PrintErr($"[minimap] {ex}"); }
        }
        var now = DateTime.Now;
        // Ref : ligne 1 heure locale « 23:57 », ligne 2 heure serveur
        // à la seconde « 17:57:22 ». Pas de paquet d'heure serveur :
        // le serveur C# tourne sur l'horloge locale.
        if (_clockLocal != null) _clockLocal.Text = now.ToString("HH:mm");
        if (_clockSrv != null) _clockSrv.Text = now.ToString("HH:mm:ss");

        // Fin du pan : retour au suivi du joueur.
        if (_panNorm != null &&
            Time.GetTicksMsec() / 1000.0 > _panUntil)
            _panNorm = null;

        UpdateView();

        // Marqueurs ~4 Hz (scan dictionnaire du registre).
        _nextMarkerScan -= delta;
        if (_nextMarkerScan <= 0)
        {
            _nextMarkerScan = 0.25;
            RefreshMarkers();
        }
    }

    /// <summary>Nom de zone EN (t_node.ini col1 via
    /// <see cref="TextResolver.NodeName"/>) ; repli = code Sxxx.</summary>
    private static string ZoneName(string mapCode)
    {
        if (mapCode.Length > 1 &&
            int.TryParse(mapCode[1..], out var id) &&
            TextResolver.NodeName(id) is { } n)
            return n;
        return mapCode;
    }

    /// <summary>Sonde <c>--zoom-test</c> : paliers de zoom réels
    /// (<see cref="Zoom"/>) + captures par palier — prouve le zoom
    /// natif et les icônes mapNNN (U3/M8).</summary>
    public sealed partial class ZoomProbe : Node
    {
        private readonly MinimapPanel _m;
        private readonly string _shotsDir;
        private int _frame;

        public ZoomProbe(MinimapPanel m)
        {
            _m = m; Name = "__zoom_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            _frame++;
            switch (_frame)
            {
                case 220:
                    GD.Print($"[zoom-test] palier={_m._zoomIdx} " +
                        $"marqueurs={_m._markers.Count} " +
                        $"portails={_m._portalMarkers.Count}");
                    Shot("u3_minimap_z2");
                    _m.Zoom(1);   // ×3.5
                    break;
                case 250:
                    Shot("u3_minimap_z4");
                    _m.Zoom(-1); _m.Zoom(-1); _m.Zoom(-1);   // ×1
                    break;
                case 280:
                    GD.Print($"[zoom-test] palier={_m._zoomIdx} " +
                        $"mapPos={_m._map?.Position} " +
                        $"mapSize={_m._map?.Size}");
                    Shot("u3_minimap_z1");
                    _m.Zoom(1); _m.Zoom(1);   // retour défaut
                    SetProcess(false);
                    break;
            }
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
                GD.PrintErr($"[zoom-test] shot : {e.Message}");
            }
        }
    }
}
