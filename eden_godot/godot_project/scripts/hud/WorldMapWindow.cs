using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;

namespace EdenClient.Hud;

/// <summary>
/// Carte du monde (S10) — instancie le layout natif
/// <c>ZoneMapUI.layout</c> (FrameWindow + image
/// <c>set:WorldMap11 image:Background</c> = mapworld011.png 512²,
/// continent 1) + boutons-villes <c>mapBtn1..38</c> + marqueurs
/// CharImg/LoverImg/PlayerImg.
///
/// <b>mapBtn&lt;i&gt; ↔ nodeId i</b> — désasm client_game :
/// <c>FUN_00a50b60</c> itère les boutons d'une page et attache à
/// chacun le record de node d'id = index du bouton (vcall
/// GetNode(id) + userdata relue au clic par <c>FUN_00a55330</c> :
/// <c>*(rec+0x1c4)</c> = nodeId → affiche la map de ce node) ;
/// <c>FUN_00a55410</c> mappe les plages de nodeId → page de fond
/// (0 = nodes 1-16,22-27,32-34,36-38 = ids <c>C_Transport.ini</c>
/// du continent 1 ; 1 = 19-21,55-57 ; 4 = 28-30,51-54). Conséquence
/// exploitée : <c>mapBtn</c>N → nodeId N.
///
/// Enrichissements du lot :
///   - tooltips des boutons = nom de zone réel (<c>t_node.ini</c>
///     via <see cref="TextResolver"/>) + badge « Transport » si la
///     zone est dans <c>C_Transport.ini</c> (zones à portail) ;
///   - <c>MapListCB</c> rempli des zones transport — sélection =
///     aperçu de la zone ;
///   - <c>PlayerImg</c> posé au centre du bouton du node courant
///     (<see cref="MinimapPanel.NodeId"/>) à chaque frame ;
///   - clic mapBtn → aperçu de la zone (texture
///     <c>UI/map/S###s.png</c>) superposé + nom dans le titre ;
///   - <c>ExitBtn</c> natif referme la fenêtre ; OptionUI (filtres
///     natifs non implémentés) masqué.
/// </summary>
public partial class WorldMapWindow : Control
{
    private Control? _window;
    private Control? _bg;              // BackgroundImg (carte 512²)
    private Control? _playerImg;
    private Control? _preview;
    private MinimapPanel? _minimap;

    /// <summary>NodeIds affichés sur la page continent 1 — les boutons
    /// réellement présents dans le layout sont découverts à
    /// l'exécution ; les ids transportables viennent de
    /// <see cref="TransportZones"/>.</summary>
    private static readonly HashSet<int> Page0Nodes = new(
        Enumerable.Range(1, 16)
            .Concat(Enumerable.Range(22, 6))   // 22..27
            .Concat(Enumerable.Range(32, 3))   // 32..34
            .Concat(Enumerable.Range(36, 3))); // 36..38

    public void Build(HudRoot hud)
    {
        _window = hud.Build("ZoneMapUI.layout");
        if (_window == null)
        {
            BuildFallback();
        }
        else
        {
            AddChild(_window);
            // Cadre raisonnable 720p : l'UnifiedAreaRect natif vise
            // ~640×546 sur du 1024 — épinglé centré.
            HudRoot.Pin(_window, 0.5f, 0.5f, 0.5f, 0.5f,
                -330, -280, 330, 280);
            SetTitle(UiTextEn.Translate("World Map"));
            _bg = TopLeftVitals.Find<Control>(_window, "BackgroundImg");
            _playerImg = TopLeftVitals.Find<Control>(_window, "PlayerImg");

            // Boutons régions : id = nodeId (désasm ci-dessus) —
            // tooltip nom réel + badge transport, clic → aperçu zone.
            for (var i = 1; i <= 40; i++)
            {
                var b = TopLeftVitals.Find<Control>(_window, $"mapBtn{i}");
                if (b == null) continue;
                var nodeId = i;
                b.TooltipText = TooltipFor(nodeId);
                if (b is BaseButton btn)
                {
                    var captured = nodeId;
                    btn.Pressed += () => ShowZonePreview(captured);
                }
            }
            // Marqueurs natifs sans données : CharImg (groupe) et
            // LoverImg masqués — PlayerImg est posé par _Process.
            for (var i = 1; i <= 10; i++)
            {
                var c = TopLeftVitals.Find<Control>(_window, $"CharImg{i}");
                if (c != null) c.Visible = false;
            }
            var lover = TopLeftVitals.Find<Control>(_window, "LoverImg");
            if (lover != null) lover.Visible = false;

            // Boutons natifs : ExitBtn referme ; OptionUI = panneau
            // « City Features » du shot de référence — affiché avec
            // libellés anglais ; les cases basculent nativement.
            // shortcut: les filtres n'ont pas encore de marqueurs de
            // carte à masquer — à câbler quand les icônes NPC/shop
            // seront dessinées sur la carte de zone.
            var exit = TopLeftVitals.Find<BaseButton>(_window, "ExitBtn");
            if (exit != null) exit.Pressed += () => Visible = false;
            TranslateOptionUI();
            var npc = TopLeftVitals.Find<Control>(_window, "NPCCB");
            if (npc != null) npc.Visible = false;
            var npcLab = TopLeftVitals.Find<Control>(_window, "NPCLeb");
            if (npcLab != null) npcLab.Visible = false;

            // MapListCB → liste des zones transport (sélection =
            // aperçu) — combo native readonly.
            FillMapList();

            BuildPreviewLayer();
            Ui.CeguiToGodot.SkinUnstyled(_window);
        }
        DragHelper.EnsureTitleBar(_window ?? this, UiTextEn.Translate("World Map"));
        DragHelper.Persist(_window ?? this, "world_map");
        // --hud-open/ToggleWorldMap n'agit que sur ce Control — la
        // visibilité réelle vit sur le cadre intérieur.
        VisibilityChanged += () =>
        {
            if (Visible && _window != null) _window.Visible = true;
        };
        Visible = false;
    }

    /// <summary>Libellés Big5 → anglais du panneau OptionUI (ordre =
    /// shot de référence « City Features »).</summary>
    private void TranslateOptionUI()
    {
        var labels = new Dictionary<string, string>
        {
            ["CityBtn"] = "City Features",
            ["NormalMerchBtn"] = "General Merchant",
            ["WeapMerchBtn"] = "Weapon Merchant",
            ["AmrMerchBtn"] = "Armor Merchant",
            ["RiderMerchBtn"] = "Fame Shop",
            ["AutionBtn"] = "Auction",
            ["ReBornBtn"] = UiTextEn.Translate("Revival Point"),
            ["CityTpBtn"] = "Warp Portal",
            ["TpBtn"] = UiTextEn.Translate("Portal"),
            ["GetMissionBtn"] = "New Quest",
            ["ReplyMissionBtn"] = "Quest NPC",
        };
        foreach (var (name, text) in labels)
            if (TopLeftVitals.Find<CheckBox>(_window, name) is { } cb)
                cb.Text = text;
    }

    /// <summary>Tooltip d'un bouton : nom t_node + « Transport » si la
    /// zone a une route de transport (C_Transport.ini).</summary>
    private static string TooltipFor(int nodeId)
    {
        var name = TextResolver.NodeName(nodeId) ?? $"S{nodeId:D3}";
        return TransportZones.Contains(nodeId)
            ? $"{name}  ·  portal" : name;
    }

    /// <summary>Remplit MapListCB avec les zones transport (id=nodeId
    /// dans l'OptionButton).</summary>
    private void FillMapList()
    {
        var cb = TopLeftVitals.Find<OptionButton>(_window, "MapListCB");
        if (cb == null) return;
        cb.Clear();
        cb.AddItem(UiTextEn.Translate("— zones —"), -1);
        foreach (var nodeId in TransportZones.All.OrderBy(x => x))
        {
            var name = TextResolver.NodeName(nodeId) ?? $"S{nodeId:D3}";
            cb.AddItem(name, nodeId);
        }
        cb.ItemSelected += idx =>
        {
            var id = cb.GetItemId((int)idx);
            if (id > 0) ShowZonePreview(id);
        };
    }

    /// <summary>Aperçu de zone : TextureRect superposé à la carte
    /// monde affichant <c>UI/map/S###s.png</c> + nom — reclic/Échap
    /// referme.</summary>
    private void BuildPreviewLayer()
    {
        if (_bg == null) return;
        var p = new PanelContainer { Name = "ZonePreview" };
        p.SetAnchorsPreset(LayoutPreset.FullRect);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.083f, 0.08f, 0.071f, 0.92f),
        });
        var v = new VBoxContainer();
        v.SetAnchorsPreset(LayoutPreset.FullRect);
        var lbl = new Label
        {
            Name = "ZonePreviewTitle",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        v.AddChild(lbl);
        var img = new TextureRect
        {
            Name = "ZonePreviewImg",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        v.AddChild(img);
        var hint = new Label
        {
            Text = UiTextEn.Translate("(click to close)"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0.5f),
        };
        v.AddChild(hint);
        p.AddChild(v);
        p.Visible = false;
        p.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed)
                p.Visible = false;
        };
        _bg.AddChild(p);
        _preview = p;
    }

    /// <summary>Clic région / sélection liste → aperçu de la carte de
    /// la zone + nom dans le titre de la fenêtre.</summary>
    private void ShowZonePreview(int nodeId)
    {
        if (_preview == null) return;
        var name = TextResolver.NodeName(nodeId) ?? $"S{nodeId:D3}";
        var img = TopLeftVitals.Find<TextureRect>(_preview,
            "ZonePreviewImg");
        if (img != null)
            img.Texture = AssetCatalog.GetTexture(
                $"UI/map/S{nodeId:D3}s.png");
        var lbl = TopLeftVitals.Find<Label>(_preview, "ZonePreviewTitle");
        if (lbl != null) lbl.Text = $"{name} (S{nodeId:D3})";
        SetTitle(UiTextEn.Translate($"World Map — {name}"));
        _preview.Visible = true;
    }

    /// <summary>Affiche/masque (GlobleBtn minimap, touche M).</summary>
    public void Toggle() => Visible = !Visible;

    public override void _Process(double delta)
    {
        if (!Visible || _playerImg == null) return;
        _minimap ??= GetTree().Root
            .FindChild("Minimap", true, false) as MinimapPanel;
        var nodeId = _minimap?.NodeId ?? 0;
        if (nodeId <= 0) { _playerImg.Visible = false; return; }
        // Marqueur joueur au centre du bouton de son node — la
        // position native exacte (coords monde du node dans le record
        // client) n'est pas dans les tables : approximation prouvée
        // = centre du mapBtn (le bouton EST la position de la zone).
        var btn = _window != null
            ? TopLeftVitals.Find<Control>(_window, $"mapBtn{nodeId}")
            : null;
        if (btn == null) { _playerImg.Visible = false; return; }
        // PlayerImg et mapBtnN sont siblings (enfants de BackgroundImg)
        // → coordonnées dans le même espace, pas de décalage parent.
        var r = btn.GetRect();
        HudRoot.Pin(_playerImg, 0, 0, 0, 0,
            r.Position.X + r.Size.X / 2f - 8f,
            r.Position.Y + r.Size.Y / 2f - 8f,
            r.Position.X + r.Size.X / 2f + 8f,
            r.Position.Y + r.Size.Y / 2f + 8f);
        _playerImg.Visible = true;
    }

    private void SetTitle(string title)
    {
        var lbl = _window?
            .FindChildren("*", "CeguiTitleBar", true, false)
            .Cast<Ui.CeguiTitleBar>().FirstOrDefault()?
            .FindChildren("*", "Label", true, false)
            .Cast<Label>().FirstOrDefault();
        if (lbl != null) lbl.Text = title;
    }

    /// <summary>Repli sans uidata : panneau + grande texture monde
    /// mapworld01.png (assets/uidata/imagesets).</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "ZoneMapUI" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-330, -280);
        p.Size = new Vector2(660, 560);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var tex = AssetCatalog.GetTexture(
            "uidata/imagesets/mapworld01.png");
        var img = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        img.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        img.OffsetTop = 24;
        p.AddChild(img);
        AddChild(p);
        _window = p;
        DragHelper.EnsureTitleBar(p, UiTextEn.Translate("World Map"));
    }
}

/// <summary>
/// Zones à portail/transport — ids de <c>data/db/C_Transport.ini</c>
/// (row id = nodeId : vérif. nom↔nom t_node — Aven=1, Avila=9…).
/// Chargement paresseux ; repli = ensemble vide.
/// </summary>
public static class TransportZones
{
    private static HashSet<int>? _ids;
    private static bool _tried;

    /// <summary>Ids chargés (usage <c>Contains(id)</c>).</summary>
    public static HashSet<int> All
    {
        get
        {
            Ensure();
            return _ids ??= new HashSet<int>();
        }
    }

    /// <summary>Membership test.</summary>
    public static bool Contains(int nodeId) => All.Contains(nodeId);

    private static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        var path = (TextResolver.ResolveFile
                ?? StoreTable.ResolveDefault)("data/db/C_Transport.ini");
        if (path == null) return;
        try
        {
            var table = IniTable.ParseFile(path, EdenEncoding.Big5);
            _ids = new HashSet<int>();
            foreach (var row in table.Rows)
                if (row.Length > 0 && int.TryParse(row[0], out var id))
                    _ids.Add(id);
        }
        catch (Exception e)
        {
            TextResolver.Log?.Invoke(
                $"[worldmap] C_Transport.ini : {e.Message}");
        }
    }
}
