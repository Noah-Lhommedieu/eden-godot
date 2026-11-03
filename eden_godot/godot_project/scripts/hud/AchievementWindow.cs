using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre des succès — lot Vague 1-D (Phase 6), modèle
/// <c>Achievement_Ver2.layout</c> (LeftPanel : arbre de catégories +
/// 3 totaux ; AchiPanel : liste à slots icône).
///
/// Wire (zone/serveur — preuves dans ZoneEntryFlow.Achievement.cs et
/// ZoneEntryFlow.Titles.cs, désasm zone @0x08209080 / @0x080f2400) :
///   S→C 480 ClientAchievementUpdate → <see cref="PlayerAchievements"/>
///     (kinds 0/1 paires {id,points}, 2/3 totaux chasse/ville,
///     4/5 listes d'ids).
///   S→C 396/397/398 appellation → <see cref="PlayerTitles"/>
///     (binds posés ici : même lot « succès & titres »).
///   C→S 93 émis par <see cref="PlayerTitles.SetTitleSender"/>
///     (sélecteur du ProfileWindow).
///
/// Progression des succès incomplets (A5.6) :
///   - succès « groupe » (légendaires, col13 = ids cibles à 1
///     déclenchement) : done/total déduit du 480 kind 0 (cibles
///     complétées) ;
///   - succès à compteur (NeedNum &gt; 1, 105 lignes) : extension Eden
///     480 kind 6 (success_num de la base, serveur
///     <c>AchievementProgressPackets</c>) ;
///   - légendaires classés par les tables wiki
///     (<see cref="LegendaryWiki"/>), titre lié via col9 → T_Title.
/// Embarqué dans l'onglet Achievement du profil, tente le layout natif
/// <c>Achievement_Ver2.layout</c> (repli : habillage manuel).
/// </summary>
public partial class AchievementWindow : Control
{
    private bool _dirty = true;

    // --- UI ---
    private Panel? _panel;
    private Tree? _tree;
    private Label? _totals, _listHeader;
    private RichTextLabel? _detail;
    private ScrollContainer? _detailScroll;
    private VBoxContainer? _detailBox;
    private int _selectedId;
    private static bool _langHooked;
    private VBoxContainer? _list;
    private ScrollContainer? _scroll;

    // --- données ---
    private static Dictionary<int, AchievementTable.AchievementDef>?
        _defs;
    private static Dictionary<int, TextTable.TextRow>? _texts;
    /// <summary>T_Achievement EN (classement légendaire / niveaux,
    /// indépendant de la langue).</summary>
    private static Dictionary<int, TextTable.TextRow>? _textsEn;
    private static Dictionary<int, ItemTable.ItemDef>? _items;
    private static Dictionary<int, TitleTable.TitleDef>? _titles;

    /// <summary>Filtre courant : -1 tout, -2 centaine
    /// (<see cref="_filterHundred"/>), sinon groupKey exact.</summary>
    private int _filterGroup = -1;
    private int _filterHundred;
    /// <summary>Filtre légendaire (si <c>_filterGroup == -3</c>) :
    /// 0 tous, 1 stats, 2 armes, 3 montures &amp; costumes.</summary>
    private int _filterLegend;

    /// <summary>Instance montée (HudExtensions) — le pont réseau
    /// partagé lui route les trames 480/396-398 via
    /// <see cref="OnAchievementUpdate"/>/<see cref="OnTitleAdded"/>.
    /// </summary>
    public static AchievementWindow? Instance { get; private set; }

    /// <summary>Instance embarquée dans l'onglet Achievement du profil
    /// (ne remplace pas <see cref="Instance"/>).</summary>
    private bool _embedded;
    private Label? _tTotal, _tHunt, _tTown;
    private const int NativeSlots = 8;
    /// <summary>Slots du layout natif (null = habillage manuel).</summary>
    private Control[]? _slots;
    private VScrollBar? _nScroll;
    private int _nOffset;
    private Control? _detailPanel;

    public override void _Ready()
    {
        if (_embedded)
        {
            PlayerAchievements.Changed += OnStoreChanged;
            return;
        }
        Instance = this;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildPanel();
        Visible = true;
        if (_panel != null) _panel.Visible = false;
        PlayerAchievements.Changed += OnStoreChanged;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        PlayerAchievements.Changed -= OnStoreChanged;
    }

    public override void _Process(double delta)
    {
        if (_dirty && _panel != null && _panel.IsVisibleInTree())
        {
            _dirty = false;
            RefreshList();
        }
    }

    // ------------------------- réseau -------------------------
    // Les binds 480/396/397/398 + le sender du 93 sont posés par
    // HudExtensions.BindSession (pont partagé éprouvé en live) —
    // les trames arrivent ici déjà désérialisées, sur le thread
    // principal.

    /// <summary>Applique une trame 480 (appelé par le pont sur le
    /// thread principal) : le store est TOUJOURS mis à jour (les
    /// trames d'entrée arrivent avant le montage de la fenêtre) ;
    /// notification des nouvelles complétions (kind 1 —
    /// AchievementTipWnd_Ver2 natif, ici message système).</summary>
    public static void OnAchievementUpdate(ZoneAchievementUpdate m)
    {
        if (m.Kind == 1)
        {
            EnsureTables();
            foreach (var (id, pts) in m.Entries)
            {
                if (PlayerAchievements.PointsOf(id) >= pts) continue;
                var name = AchievementName(id);
                var gained = pts - PlayerAchievements.PointsOf(id);
                Notify(
                    $"[Achievement] {name} (+{gained} pts, {pts} total)");
            }
        }
        PlayerAchievements.Apply(m);
    }

    /// <summary>398 — titre nouvellement possédé (pont, thread
    /// principal) : store + notification de grant.</summary>
    public static void OnTitleAdded(int titleId)
    {
        var was = PlayerTitles.Owned.Contains(titleId);
        PlayerTitles.ApplyAdd(titleId);
        if (!was)
            Notify($"[Title] Nouveau titre : {TitleName(titleId)}");
    }

    /// <summary>Message système en chat (canal 7).</summary>
    public static void Notify(string text) =>
        HudExtensions.FindHud()?.Chat?.AddSystem(text);

    // ------------------------- événements store -------------------------

    private void OnStoreChanged()
    {
        // Invoqué depuis la pompe HudExtensions — thread principal.
        _dirty = true;
        RefreshTotals();
        if (_panel != null && _panel.IsVisibleInTree())
        {
            _dirty = false;
            RefreshList();
        }
    }

    /// <summary>Onglet profil : crée une instance embarquée dans
    /// <paramref name="page"/> (layout de ref Profile-Achievements.png).
    /// </summary>
    public static AchievementWindow CreateEmbedded(Panel page,
        HudRoot? hud = null)
    {
        var w = new AchievementWindow { _embedded = true };
        page.AddChild(w);
        if (!w.TryBuildNative(page, hud)) w.BuildEmbedded(page);
        return w;
    }

    // ------------------- layout natif Achievement_Ver2 -------------------

    private static T? ByName<T>(Node root, string suffix) where T : class =>
        root.FindChild("*" + suffix, true, false) as T;

    /// <summary>Monte <c>Achievement_Ver2.layout</c> (637×573 = la page
    /// du profil, ref Profile-Achievements.png) via le pipeline CEGUI et
    /// l'habille : arbre, 3 totaux, 8 slots + scrollbar, titre de liste,
    /// bouton Progress, Victory/Defeat Quote. Retourne false (repli sur
    /// <see cref="BuildEmbedded"/>) si le layout manque ou si un widget
    /// attendu n'a pas le type Godot prévu.</summary>
    private bool TryBuildNative(Panel page, HudRoot? hud)
    {
        if (hud?.Build("Achievement_Ver2.layout") is not { } nat)
            return false;
        var tree = ByName<Tree>(nat, "LeftPanel_TreeList");
        var slots = new Control[NativeSlots];
        var ok = tree != null;
        for (var i = 0; i < NativeSlots && ok; i++)
        {
            slots[i] = ByName<Control>(nat, $"_slot{i + 1}")!;
            ok = slots[i] != null &&
                ByName<Label>(nat, $"_slot{i + 1}_pointText") != null &&
                ByName<Label>(nat, $"_slot{i + 1}_pointText1") != null;
        }
        if (!ok)
        {
            GD.PrintErr("[achievements] Achievement_Ver2 : widgets " +
                "attendus absents → habillage manuel");
            nat.Free();
            return false;
        }
        _panel = page;
        page.AddChild(nat);
        HudRoot.Pin(nat, 0, 0, 0, 0, 0, 0, 637, 573);
        _tree = tree;
        _tree!.ItemSelected += OnTreeSelected;
        _tree.AddThemeFontSizeOverride("font_size", 11);
        void Txt(string suffix, string text)
        {
            if (ByName<Label>(nat, suffix) is { } l)
                l.Text = UiTextEn.Translate(text);
        }
        Txt("Altogether_HintTextLab", "Total Achievement");
        Txt("Altogether_MonsterHintLab", "Monster Conquest");
        Txt("Altogether_TownHintLab", "Town Achievement");
        Txt("vLeftBar_Title", "Victory Quote");
        Txt("vRightBar_Title", "Defeat Quote");
        Txt("vLeftBar_setupBtn", "Set Quote");
        Txt("vRightBar_setupBtn", "Set Quote");
        _tTotal = ByName<Label>(nat, "LeftPanel_AltogetherAmount");
        _tHunt = ByName<Label>(nat, "LeftPanel_MonsterAmount");
        _tTown = ByName<Label>(nat, "LeftPanel_TownAmount");
        _listHeader = ByName<Label>(nat, "AchiPanel_Title");
        _slots = slots;
        for (var i = 0; i < NativeSlots; i++)
        {
            var idx = i;
            slots[i].MouseFilter = MouseFilterEnum.Stop;
            slots[i].GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true,
                        ButtonIndex: MouseButton.Left })
                    SelectSlot(idx);
            };
            if (ByName<BaseButton>(nat, $"_slot{i + 1}_HelpBtn") is { } hb)
                hb.Pressed += () => SelectSlot(idx);
            if (ByName<Control>(nat, $"_slot{i + 1}_newImage") is { } ni)
                ni.Visible = false;
        }
        void Wheel(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true } mb &&
                mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                _nOffset += mb.ButtonIndex == MouseButton.WheelUp ? -2 : 2;
                PopulateSlots();
                nat.AcceptEvent();
            }
        }
        foreach (var sl in slots) sl.GuiInput += Wheel;
        nat.GuiInput += Wheel;
        nat.MouseFilter = MouseFilterEnum.Pass;
        _nScroll = ByName<VScrollBar>(nat, "ContentBar_Scroll");
        if (_nScroll != null)
            _nScroll.ValueChanged += v =>
            {
                _nOffset = (int)v;
                PopulateSlots();
            };
        // Détail (objectifs / progression) : volet sous la liste, ouvert
        // au clic d'un slot ou sur le bouton Progress.
        var dp = new Panel { Visible = false };
        dp.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        HudRoot.Pin(dp, 0, 0, 0, 0, 226, 318, 632, 520);
        _detailPanel = dp;
        _detailScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        HudRoot.Pin(_detailScroll, 0, 0, 1, 1, 6, 6, -26, -6);
        _detailBox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _detailBox.AddThemeConstantOverride("separation", 4);
        _detailScroll.AddChild(_detailBox);
        dp.AddChild(_detailScroll);
        var close = new Button { Text = "x", FocusMode = FocusModeEnum.None };
        EeSkin.Grey(close, 10);
        HudRoot.Pin(close, 1, 0, 1, 0, -22, 4, -4, 22);
        close.Pressed += () => dp.Visible = false;
        dp.AddChild(close);
        page.AddChild(dp);
        if (ByName<BaseButton>(nat, "AchiPanel_ReportBtn") is { } rb)
        {
            if (rb is Button b) b.Text = UiTextEn.Translate("Progress");
            rb.Pressed += () => { RefreshDetail(); dp.Visible = !dp.Visible; };
        }
        return true;
    }

    private void SelectSlot(int slot)
    {
        var i = _nOffset + slot;
        if (i >= 0 && i < _rows.Count) SelectAchievement(_rows[i].Id);
    }

    /// <summary>Remplit les 8 slots natifs depuis <c>_rows</c> à partir
    /// de <c>_nOffset</c>.</summary>
    private void PopulateSlots()
    {
        if (_slots == null) return;
        _nOffset = Math.Clamp(_nOffset, 0,
            Math.Max(0, _rows.Count - NativeSlots));
        if (_nScroll != null)
        {
            _nScroll.MinValue = 0;
            _nScroll.MaxValue = Math.Max(NativeSlots, _rows.Count);
            _nScroll.Page = NativeSlots;
            _nScroll.SetValueNoSignal(_nOffset);
        }
        for (var i = 0; i < NativeSlots; i++)
        {
            var slot = _slots[i];
            var r = _nOffset + i;
            slot.Visible = r < _rows.Count;
            if (r >= _rows.Count) continue;
            var d = _rows[r];
            var completed = PlayerAchievements.IsCompleted(d.Id);
            var oos = IsLegendary(d) && IsOutOfScope(d);
            var prog = ProgressOf(d);
            if (slot.FindChild($"*_slot{i + 1}_pointText", true, false)
                    is Label name)
            {
                name.Text = AchievementName(d.Id) + (oos
                    ? " " + UiTextEn.Translate("[Lv95+ out of scope]") : "");
                name.TooltipText = RowTooltip(d);
                name.ClipText = true;
                name.AutowrapMode = TextServer.AutowrapMode.Off;
                if (slot.FindChild($"*_slot{i + 1}_pointText1", true, false)
                        is Label stl && stl.GetParent() == name.GetParent())
                {
                    var room = stl.Position.X - name.Position.X - 6;
                    if (room > 60) name.CustomMinimumSize = new Vector2(0, 0);
                    name.Size = new Vector2(Mathf.Min(name.Size.X, room), name.Size.Y);
                }
            }
            if (slot.FindChild($"*_slot{i + 1}_pointText1", true, false)
                    is Label st)
                st.Text = completed
                    ? $"✓ {PlayerAchievements.PointsOf(d.Id)}/{d.Point}"
                    : prog.Done > 0
                        ? $"{UiTextEn.Translate("Incomplete")} {prog.Done}/{prog.Total}"
                        : UiTextEn.Translate("Incomplete");
            if (slot.FindChild($"*_slot{i + 1}_icon", true, false)
                    is TextureRect ic)
            {
                ic.Texture = AchievementIcon(d) ?? ic.Texture;
                ic.Modulate = completed && !oos
                    ? Colors.White : new Color(1, 1, 1, 0.45f);
            }
        }
    }

    /// <summary>Onglet rendu visible : (re)charge arbre + totaux + liste.
    /// </summary>
    public void Activate()
    {
        EnsureTables();
        BuildTree();
        RefreshTotals();
        _dirty = true;
    }

    private void BuildEmbedded(Panel page)
    {
        _panel = page;
        var y = 8;
        foreach (var (txt, set) in new (string, Action<Label>)[]
        {
            ("Total Achievement", l => _tTotal = l),
            ("Monster Conquest", l => _tHunt = l),
            ("Town Achievement", l => _tTown = l),
        })
        {
            var n = new Label { Text = txt };
            n.AddThemeFontSizeOverride("font_size", 11);
            HudRoot.Pin(n, 0, 0, 0, 0, 10, y, 110, y + 22);
            page.AddChild(n);
            var v = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            v.AddThemeFontSizeOverride("font_size", 11);
            HudRoot.Pin(v, 0, 0, 0, 0, 112, y, 170, y + 22);
            page.AddChild(v);
            set(v);
            y += 30;
        }
        _tree = new Tree { HideRoot = true };
        HudRoot.Pin(_tree, 0, 0, 0, 1, 8, 104, 176, -8);
        _tree.AddThemeFontSizeOverride("font_size", 11);
        _tree.ItemSelected += OnTreeSelected;
        page.AddChild(_tree);
        _listHeader = new Label();
        _listHeader.AddThemeFontSizeOverride("font_size", 11);
        HudRoot.Pin(_listHeader, 0, 0, 1, 0, 188, 8, -30, 28);
        page.AddChild(_listHeader);
        var help = new Button { Text = "?", FocusMode = FocusModeEnum.None,
            TooltipText = UiTextEn.Translate("Completed achievements and their rewards. ") +
                UiTextEn.Translate("Achievements already unlocked are highlighted.") };
        EeSkin.Grey(help, 10);
        HudRoot.Pin(help, 1, 0, 1, 0, -26, 6, -8, 24);
        page.AddChild(help);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
        };
        HudRoot.Pin(_scroll, 0, 0, 1, 1, 184, 32, -8, -8);
        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _list.AddThemeConstantOverride("separation", 4);
        _scroll.AddChild(_list);
        page.AddChild(_scroll);
    }

    /// <summary>Ouvre/masque la fenêtre (menu, --hud-open).</summary>
    public void Toggle()
    {
        if (_panel == null) return;
        _panel.Visible = !_panel.Visible;
        if (_panel.Visible)
        {
            EnsureTables();
            BuildTree();
            RefreshTotals();
            _dirty = true;
        }
    }

    /// <summary>Ouvre la fenêtre (sans basculer — <c>Show</c> est pris
    /// par CanvasItem).</summary>
    public void Open()
    {
        if (_panel != null && !_panel.Visible) Toggle();
    }

    /// <summary>Données de démo hors session (<c>--hud-open=
    /// achievements</c>) — injectées par les VRAIS handlers
    /// (<see cref="PlayerAchievements.Apply"/> /
    /// <see cref="PlayerTitles"/>), pas de chemin privé.</summary>
    public void EnsureDemo()
    {
        EnsureTables();
        if (_defs != null && !PlayerAchievements.IsCompleted(
                _defs.Keys.Min()))
        {
            // Une trame kind 0 « synchro initiale » réaliste : les
            // 12 premiers succès de la table complétés + totaux.
            var sync = new ZoneAchievementUpdate { Kind = 0 };
            var i = 0;
            foreach (var (id, d) in _defs.OrderBy(kv => kv.Key))
            {
                if (i++ >= 12) break;
                sync.Entries.Add((id, (uint)Math.Max(1, d.Point)));
            }
            PlayerAchievements.Apply(sync);
            PlayerAchievements.Apply(
                new ZoneAchievementUpdate { Kind = 2, Total = 42 });
            PlayerAchievements.Apply(
                new ZoneAchievementUpdate { Kind = 3, Total = 17 });
            PlayerTitles.ApplyAdd(1);
            PlayerTitles.ApplyUpdate(1);
        }
        BuildTree();
        RefreshTotals();
        RefreshList();
    }

    // ------------------------- tables -------------------------

    private static void EnsureTables()
    {
        if (_defs != null) return;
        _defs = LoadOr("data/db/c_achievement.ini",
            AchievementTable.Load);
        HookLanguage();
        _textsEn = LoadOr("data/db/T_Achievement.ini", TextTable.Load);
        _texts = LoadLang("T_Achievement.ini", _textsEn);
        var ipath = AssetCatalog.ResolveRawAsset("data/db/c_item.ini");
        _items = ipath != null
            ? ItemTable.Load(ipath)
            : new Dictionary<int, ItemTable.ItemDef>();
        // Récompenses item mall éventuelles (même format que c_item).
        var mpath = AssetCatalog.ResolveRawAsset(
            "data/db/c_itemmall.ini");
        if (mpath != null)
            foreach (var (id, def) in ItemTable.Load(mpath))
                _items.TryAdd(id, def);
        var tp = LangPath("T_Title.ini")
            ?? AssetCatalog.ResolveRawAsset("data/db/T_Title.ini");
        var cp = AssetCatalog.ResolveRawAsset("data/db/c_title.ini");
        _titles = tp != null
            ? TitleTable.Load(tp, cp)
            : new Dictionary<int, TitleTable.TitleDef>();
    }

    /// <summary>Chemin <c>data/db/lang/&lt;lang&gt;/&lt;file&gt;</c> si la
    /// langue active n'est pas EN et que le fichier existe (casse
    /// minuscule des exports lang/ ou nom d'origine).</summary>
    private static string? LangPath(string file)
    {
        if (TextResolver.Language == "en") return null;
        var dir = $"data/db/lang/{TextResolver.Language}/";
        return AssetCatalog.ResolveRawAsset(dir + file.ToLowerInvariant())
            ?? AssetCatalog.ResolveRawAsset(dir + file);
    }

    /// <summary>Table texte lang/&lt;lang&gt; avec repli EN par id.</summary>
    private static Dictionary<int, TextTable.TextRow> LoadLang(
        string file, Dictionary<int, TextTable.TextRow> en)
    {
        var lp = LangPath(file);
        if (lp == null) return en;
        try
        {
            var t = TextTable.Load(lp);
            foreach (var (id, row) in en) t.TryAdd(id, row);
            return t;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[achievements] {file} ({TextResolver.Language}) : {e.Message}");
            return en;
        }
    }

    private static void HookLanguage()
    {
        if (_langHooked) return;
        _langHooked = true;
        TextResolver.LanguageChanged += () =>
        {
            _texts = null; _titles = null;
            Instance?.OnLanguageChanged();
        };
    }

    private void OnLanguageChanged()
    {
        EnsureTables();
        BuildTree();
        RefreshTotals();
        _dirty = true;
    }

    private static Dictionary<int, T> LoadOr<T>(string rel,
        Func<string, Dictionary<int, T>> loader)
    {
        var p = AssetCatalog.ResolveRawAsset(rel);
        try
        { return p != null ? loader(p) : new(); }
        catch (Exception e)
        {
            GD.PrintErr($"[achievements] {rel} : {e.Message}");
            return new();
        }
    }

    private static string AchievementName(int id)
    {
        if (_texts != null && _texts.TryGetValue(id, out var t) &&
            t.Name.Length > 0)
            return TextResolver.Clean(t.Name);
        if (_defs != null && _defs.TryGetValue(id, out var d) &&
            d.Name.Length > 0)
            return TextResolver.Clean(d.Name);
        return $"#{id}";
    }

    private static string AchievementDesc(int id)
    {
        var desc = _texts != null &&
            _texts.TryGetValue(id, out var t)
                ? TextResolver.Clean(t.Description) : "";
        if (desc.Length == 0 && _defs != null &&
            _defs.TryGetValue(id, out var d))
            desc = TextResolver.Clean(d.Description);
        return desc;
    }

    // ------------------------- légendaires -------------------------

    /// <summary>Succès légendaires (wiki Legendary_Achievements) = ids
    /// 5001-5092 portant des cibles (col13) ou rangés dans les groupes
    /// 104/107/110 (92 entrées, chacune avec un titre « actif même non
    /// équipé »).</summary>
    private static bool IsLegendary(AchievementTable.AchievementDef d) =>
        d.Id >= 5001 && d.Id <= 5092 &&
        (d.TargetIds.Length > 0 || d.GroupKey == 104 ||
         d.GroupKey == 107 || d.GroupKey == 110);

    private static readonly Dictionary<int, LegendaryWiki.Result>
        _legendCache = new();

    private static string EnDesc(int id) =>
        _textsEn != null && _textsEn.TryGetValue(id, out var t)
            ? t.Description : "";

    /// <summary>Classement par les tables du wiki (texte EN, stable
    /// quelle que soit la langue) — voir <see cref="LegendaryWiki"/>.
    /// </summary>
    private static LegendaryWiki.Result Legend(
        AchievementTable.AchievementDef d)
    {
        if (!_legendCache.TryGetValue(d.Id, out var r))
            _legendCache[d.Id] = r = LegendaryWiki.Classify(
                EnDesc(d.Id), d.ItemCol10 != 0);
        return r;
    }

    /// <summary>1 stats, 2 armes, 3 montures &amp; costumes.</summary>
    private static int LegendKind(AchievementTable.AchievementDef d) =>
        Legend(d).Kind;

    /// <summary>Niveau visé &gt; 90 : hors périmètre (Eden de base =
    /// lvl 90).</summary>
    private static bool IsOutOfScope(AchievementTable.AchievementDef d) =>
        Legend(d).OutOfScope;

    /// <summary>Titre lié (A5.6d) : col9 = appellation sur les
    /// légendaires et les succès de type 7 (T_Title) ; 0 sinon (col9
    /// est un seuil ailleurs).</summary>
    private static int TitleFor(AchievementTable.AchievementDef d) =>
        (IsLegendary(d) || d.Type == 7) && _titles != null &&
        _titles.ContainsKey(d.TitleCol9) ? d.TitleCol9 : 0;

    /// <summary>Nom du succès qui octroie un titre (réciproque de
    /// <see cref="TitleFor"/>) — affiché dans l'onglet Title.</summary>
    public static string? AchievementForTitle(int titleId)
    {
        EnsureTables();
        if (_defs == null) return null;
        foreach (var d in _defs.Values)
            if (TitleFor(d) == titleId) return AchievementName(d.Id);
        return null;
    }

    /// <summary>Progression (fait, requis) : cibles complétées pour un
    /// succès groupe, sinon compteur (480 kind 6) / NeedNum.</summary>
    private static (int Done, int Total) ProgressOf(
        AchievementTable.AchievementDef d)
    {
        var completed = PlayerAchievements.IsCompleted(d.Id);
        if (d.TargetIds.Length > 0)
            return (completed ? d.TargetIds.Length
                : AchievementProgress.CountDone(d.TargetIds,
                    PlayerAchievements.IsCompleted), d.TargetIds.Length);
        if (completed) return (d.NeedNum, d.NeedNum);
        return ((int)Math.Min(PlayerAchievements.CounterOf(d.Id),
            (uint)d.NeedNum), d.NeedNum);
    }

    /// <summary>Nom d'un nœud de catégorie (groupKey) — T_Achievement
    /// nomme les groupes 101-310 (« Blue Crystal Chronicles »…) ; les
    /// autres retombent sur « Groupe NNN ».</summary>
    private static string GroupName(int groupKey)
    {
        if (_texts != null && _texts.TryGetValue(groupKey, out var t) &&
            t.Name.Length > 0)
            return TextResolver.Clean(t.Name);
        return $"Group {groupKey}";
    }

    private static Texture2D? AchievementIcon(
        AchievementTable.AchievementDef d)
    {
        if (d.Code.Length == 0) return null;
        // Codes c_achievement (M0xx, J0xx, script0x) vivent dans
        // UI/uiicon/ — skillicon/itemicon en repli.
        return AssetCatalog.GetTexture($"UI/uiicon/{d.Code}.png")
            ?? AssetCatalog.GetTexture($"UI/skillicon/{d.Code}.png")
            ?? AssetCatalog.GetTexture($"UI/itemicon/{d.Code}.png");
    }

    // ------------------------- arbre + liste -------------------------

    /// <summary>Construit l'arbre de catégories : « Tous » puis, par
    /// centaine de groupKey présente, un parent « Catégorie Nxx »
    /// regroupant chaque groupKey (nommé quand T_Achievement le
    /// fournit).</summary>
    private void BuildTree()
    {
        if (_tree == null || _defs == null) return;
        _tree.Clear();
        var root = _tree.CreateItem();
        root.SetText(0, UiTextEn.Translate("Achievements"));
        var all = _tree.CreateItem(root);
        all.SetText(0, UiTextEn.Translate($"All ({_defs.Count})"));
        all.SetMetadata(0, -1);

        // Catégorie dédiée « Legendary Achievements » + 3 natures.
        var legend = _defs.Values.Where(IsLegendary).ToList();
        if (legend.Count > 0)
        {
            var lg = _tree.CreateItem(root);
            lg.SetText(0, UiTextEn.Translate("Legendary Achievements") +
                $" ({legend.Count})");
            lg.SetMetadata(0, -1000);
            foreach (var (k, label) in new[]
            {
                (1, "Stat Increases"), (2, "Weapon Stat Increases"),
                (3, "Mounts & Costumes"),
            })
            {
                var c = _tree.CreateItem(lg);
                c.SetText(0, UiTextEn.Translate(label) +
                    $" ({legend.Count(x => LegendKind(x) == k)})");
                c.SetMetadata(0, -1000 - k);
            }
        }

        var byHundred = new SortedDictionary<int,
            SortedDictionary<int, int>>();
        foreach (var d in _defs.Values)
        {
            if (IsLegendary(d)) continue;
            var h = d.GroupKey / 100;
            if (!byHundred.TryGetValue(h, out var m))
                byHundred[h] = m = new();
            m[d.GroupKey] = m.GetValueOrDefault(d.GroupKey) + 1;
        }
        foreach (var (h, groups) in byHundred)
        {
            var total = 0;
            foreach (var c in groups.Values) total += c;
            var parent = _tree.CreateItem(root);
            var hn = h <= 2 ? UiTextEn.Translate("Monster Conquest")
                : h == 5 ? UiTextEn.Translate("Town Achievement")
                : $"{UiTextEn.Translate("Category")} {h}xx";
            parent.SetText(0, $"{hn} ({total})");
            parent.SetMetadata(0, -2 - h);   // centaine : filtre large
            parent.Collapsed = h > 3;
            foreach (var (gk, count) in groups)
            {
                var child = _tree.CreateItem(parent);
                child.SetText(0, $"{GroupName(gk)} ({count})");
                child.SetMetadata(0, gk);
            }
        }
    }

    private void OnTreeSelected()
    {
        var item = _tree?.GetSelected();
        if (item == null) return;
        var meta = item.GetMetadata(0).AsInt32();
        if (meta == -1) { _filterGroup = -1; }
        else if (meta <= -1000)
        {
            _filterGroup = -3;
            _filterLegend = -1000 - meta;
        }
        else if (meta <= -2)
        {
            _filterGroup = -2;
            _filterHundred = -2 - meta;
        }
        else _filterGroup = meta;
        RefreshList();
    }

    private void RefreshTotals()
    {
        if (_tTotal != null)
        {
            _tTotal.Text = $"{PlayerAchievements.TotalPoints}";
            _tHunt!.Text = $"{PlayerAchievements.HuntPoints}";
            _tTown!.Text = $"{PlayerAchievements.TownPoints}";
        }
        if (_totals != null)
            _totals.Text =
                $"Total : {PlayerAchievements.TotalPoints} pts\n" +
                UiTextEn.Translate($"Hunt  : {PlayerAchievements.HuntPoints} pts\n") +
                UiTextEn.Translate($"Town  : {PlayerAchievements.TownPoints} pts");
    }

    private void RefreshList()
    {
        if (_defs == null || (_list == null && _slots == null)) return;
        var done = PlayerAchievements.Points;
        _rows = _defs.Values.Where(Matches)
            .OrderBy(d => PlayerAchievements.IsCompleted(d.Id) ? 0 : 1)
            .ThenBy(d => d.GroupKey).ThenBy(d => d.SortIndex)
            .ThenBy(d => d.Id).ToList();
        if (_slots != null) PopulateSlots();
        else if (_list != null)
        {
            foreach (var c in _list.GetChildren()) c.QueueFree();
            var shown = 0;
            foreach (var d in _rows)
            {
                _list.AddChild(MakeRow(d));
                if (++shown >= 300)
                {
                    var more = new Label
                    { Text = string.Format(UiTextEn.Translate(
                        "… {0} more (filter the category to narrow)"),
                        _rows.Count - shown) };
                    more.AddThemeFontSizeOverride("font_size", 10);
                    _list.AddChild(more);
                    break;
                }
            }
        }
        RefreshDetail();
        if (_listHeader != null)
        {
            var n = _rows.Count(r => done.ContainsKey(r.Id));
            _listHeader.Text = _filterGroup == -3
                ? string.Format(UiTextEn.Translate("{0} {1}% Complete"),
                    UiTextEn.Translate("Legendary Achievement"),
                    AchievementProgress.Percent(n, _rows.Count))
                : string.Format(UiTextEn.Translate(
                    "{0} achievements — {1} complete"), _rows.Count, n);
        }
    }

    private List<AchievementTable.AchievementDef> _rows = new();

    private bool Matches(AchievementTable.AchievementDef d)
    {
        var leg = IsLegendary(d);
        return _filterGroup switch
        {
            -1 => true,
            -3 => leg && (_filterLegend == 0 ||
                LegendKind(d) == _filterLegend),
            -2 => !leg && d.GroupKey / 100 == _filterHundred,
            _ => !leg && d.GroupKey == _filterGroup,
        };
    }

    private Control MakeRow(AchievementTable.AchievementDef d)
    {
        var completed = PlayerAchievements.IsCompleted(d.Id);
        var pts = PlayerAchievements.PointsOf(d.Id);

        var oos = IsLegendary(d) && IsOutOfScope(d);
        var row = new PanelContainer
        {
            TooltipText = RowTooltip(d),
        };
        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left })
                SelectAchievement(d.Id);
        };
        row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = completed
                ? new Color(0.10f, 0.16f, 0.10f, 0.85f)
                : new Color(0.07f, 0.08f, 0.11f, 0.7f),
            BorderColor = completed
                ? new Color(0.45f, 0.65f, 0.35f)
                : new Color(0.25f, 0.28f, 0.38f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        });

        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        row.AddChild(h);

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(36, 36),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = AchievementIcon(d) ?? AssetCatalog.White,
        };
        icon.MouseFilter = MouseFilterEnum.Ignore;
        if (!completed || oos) icon.Modulate = new Color(1, 1, 1, 0.35f);
        h.AddChild(icon);

        var mid = new VBoxContainer { SizeFlagsHorizontal =
            Control.SizeFlags.ExpandFill };
        h.AddChild(mid);
        var name = new Label
        {
            Text = AchievementName(d.Id) + (oos
                ? " " + UiTextEn.Translate("[Lv95+ out of scope]") : ""),
        };
        name.AddThemeFontSizeOverride("font_size", 12);
        name.AddThemeColorOverride("font_color", completed
            ? new Color(1f, 0.9f, 0.5f) : new Color(0.8f, 0.8f, 0.85f));
        mid.AddChild(name);
        var desc = new Label
        {
            Text = AchievementDesc(d.Id),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        desc.AddThemeFontSizeOverride("font_size", 10);
        desc.AddThemeColorOverride("font_color",
            new Color(0.65f, 0.65f, 0.7f));
        mid.AddChild(desc);

        var right = new VBoxContainer();
        right.CustomMinimumSize = new Vector2(86, 0);
        h.AddChild(right);
        var ptsLabel = new Label
        {
            Text = $"{(completed ? pts : 0)}/{d.Point} " +
                UiTextEn.Translate("pts"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ptsLabel.AddThemeFontSizeOverride("font_size", 11);
        right.AddChild(ptsLabel);
        var prog = ProgressOf(d);
        var state = new Label
        {
            Text = completed ? UiTextEn.Translate("✓ completed")
                : prog.Done > 0 ? $"{prog.Done} / {prog.Total}"
                : UiTextEn.Translate("in progress"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        state.AddThemeFontSizeOverride("font_size", 10);
        state.AddThemeColorOverride("font_color", completed
            ? new Color(0.55f, 0.9f, 0.5f) : new Color(0.5f, 0.5f, 0.6f));
        right.AddChild(state);
        if (d.ItemCol10 != 0 || TitleFor(d) != 0)
        {
            var rw = new Label
            {
                Text = RewardText(d),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            rw.AddThemeFontSizeOverride("font_size", 9);
            rw.AddThemeColorOverride("font_color",
                new Color(0.7f, 0.6f, 0.9f));
            right.AddChild(rw);
        }
        return row;
    }

    /// <summary>Tooltip natif : points / titre / effet / « actif même
    /// non équipé » (texte T_Achievement complet).</summary>
    private static string RowTooltip(AchievementTable.AchievementDef d)
    {
        var full = _texts != null && _texts.TryGetValue(d.Id, out var t)
            ? TextResolver.Clean(t.Description) : "";
        if (full.Length == 0) full = AchievementDesc(d.Id);
        return $"{AchievementName(d.Id)}\n{full}".Trim();
    }

    private void SelectAchievement(int id)
    {
        _selectedId = id;
        RefreshDetail();
        if (_detailPanel != null) _detailPanel.Visible = true;
    }

    /// <summary>Mini-panel détail (A5.6c) : objectifs listés (texte du
    /// jeu + ligne wiki canonique), progression <c>fait / requis</c>,
    /// cibles (col13), points, titre lié (col9 → T_Title, lien cliquable)
    /// et récompense.</summary>
    private void RefreshDetail()
    {
        if (_detailBox != null) { RefreshDetailRows(); return; }
        if (_detail == null || _defs == null ||
            !_defs.TryGetValue(_selectedId, out var d))
        {
            if (_detail != null)
                _detail.Text = UiTextEn.Translate(
                    "Click an achievement to see its objectives.");
            return;
        }
        var done = PlayerAchievements.IsCompleted(d.Id);
        var legend = IsLegendary(d);
        var sb = new System.Text.StringBuilder();
        sb.Append("[b]").Append(AchievementName(d.Id)).Append("[/b]");
        if (legend && IsOutOfScope(d))
            sb.Append(" [color=#e0a040]")
              .Append(UiTextEn.Translate("[Lv95+ out of scope]"))
              .Append("[/color]");

        // Objectifs : lignes d'objectif du texte localisé (avant le lore).
        var raw = _texts != null && _texts.TryGetValue(d.Id, out var tr)
            ? tr.Description : d.Description;
        var objectives = LegendaryWiki.ObjectiveLines(
            TextResolver.Clean(raw));
        sb.Append("\n\n[b]").Append(UiTextEn.Translate("Objectives"))
          .Append(":[/b]");
        foreach (var o in objectives)
            sb.Append("\n  • ").Append(o);
        var prog = ProgressOf(d);
        if (d.TargetIds.Length > 0)
        {
            sb.Append("\n  ").Append(string.Format(UiTextEn.Translate(
                "{0} of {1} targets completed ({2}%)"), prog.Done,
                prog.Total, AchievementProgress.Percent(prog.Done,
                    prog.Total)));
            // Premières cibles non complétées (aperçu).
            var left = d.TargetIds.Where(t =>
                !PlayerAchievements.IsCompleted(t)).Take(6).ToList();
            foreach (var t in left)
                sb.Append("\n    ○ ").Append(AchievementName(t));
            if (prog.Total - prog.Done > left.Count)
                sb.Append("\n    …");
        }
        sb.Append("\n[b]").Append(UiTextEn.Translate("Progress"))
          .Append(":[/b] ").Append(prog.Done).Append(" / ")
          .Append(prog.Total)
          .Append(done ? "  [color=#8fe080]✓[/color]" : "");
        sb.Append("\n[b]").Append(UiTextEn.Translate("Achievement Points"))
          .Append(":[/b] ").Append(d.Point);
        var tid = TitleFor(d);
        if (tid != 0)
        {
            sb.Append("\n[b]").Append(UiTextEn.Translate("Achievement Title"))
              .Append(":[/b] [url=title:").Append(tid).Append(']')
              .Append(TitleName(tid)).Append("[/url]");
            if (_titles != null && _titles.TryGetValue(tid, out var td) &&
                td.Bonus.Length > 0)
                sb.Append(" — ").Append(td.Bonus.Trim())
                  .Append("\n  ").Append(UiTextEn.Translate(
                      "Active even when not equipped."));
        }
        if (d.ItemCol10 != 0)
            sb.Append("\n[b]").Append(UiTextEn.Translate("Reward"))
              .Append(":[/b] ").Append(ItemName(d.ItemCol10));
        if (legend && Legend(d).Wiki is { } w)
        {
            sb.Append("\n[color=#9fb4d8][b]Wiki:[/b] ")
              .Append(w.Objectives.Replace("\n", "; ")).Append(" → ")
              .Append(w.Reward);
            if (w.Total.Length > 0)
                sb.Append(" (").Append(w.Total).Append(')');
            sb.Append("[/color]");
        }
        _detail.Text = sb.ToString();
    }

    private static RichTextLabel Rich(string bb, int size = 11)
    {
        var r = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        r.AddThemeFontSizeOverride("normal_font_size", size);
        r.AddThemeFontSizeOverride("bold_font_size", size);
        r.Text = bb;
        return r;
    }

    /// <summary>Ligne d'objectif : icône + nom (+ état) + « comment
    /// l'obtenir » ; le survol montre l'item/le succès.</summary>
    private static Control ObjectiveRow(Texture2D? icon, string name,
        string sub, bool done, Func<Control?>? tip)
    {
        var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        row.AddThemeStyleboxOverride("panel", EeSkin.InsetCard(6, 4));
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 8);
        row.AddChild(h);
        var ic = new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(32, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = done ? Colors.White : new Color(1, 1, 1, 0.75f),
        };
        h.AddChild(ic);
        var v = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        h.AddChild(v);
        v.AddChild(Rich((done ? "[color=#8fe080]✓[/color] " : "○ ") + name));
        if (sub.Length > 0)
            v.AddChild(Rich("[color=#b8b8b0]" + sub + "[/color]", 10));
        if (tip != null) ItemTooltip.BindCustom(row, tip);
        return row;
    }

    /// <summary>Nom entre ＜＞ d'un objectif « Vaincre ＜X＞ ».</summary>
    private static string? Bracketed(string s)
    {
        foreach (var (o, c) in new[] { ('＜', '＞'), ('<', '>') })
        {
            var a = s.IndexOf(o);
            var b = a < 0 ? -1 : s.IndexOf(c, a + 1);
            if (a >= 0 && b > a) return s[(a + 1)..b];
        }
        return null;
    }

    /// <summary>Détail scrollable : en-tête (nom/points/titre/récompense)
    /// puis une ligne par objectif — icône, nom, état, source d'obtention
    /// (monstre, boutique, quête, fabrication) et survol item/succès.</summary>
    private void RefreshDetailRows()
    {
        var box = _detailBox!;
        foreach (var c in box.GetChildren()) c.QueueFree();
        if (_defs == null || !_defs.TryGetValue(_selectedId, out var d))
        {
            box.AddChild(Rich(UiTextEn.Translate(
                "Click an achievement to see its objectives.")));
            return;
        }
        var done = PlayerAchievements.IsCompleted(d.Id);
        var legend = IsLegendary(d);
        var head = new System.Text.StringBuilder();
        head.Append("[b]").Append(AchievementName(d.Id)).Append("[/b]");
        if (legend && IsOutOfScope(d))
            head.Append(" [color=#e0a040]")
                .Append(UiTextEn.Translate("[Lv95+ out of scope]"))
                .Append("[/color]");
        var raw = _texts != null && _texts.TryGetValue(d.Id, out var tr)
            ? tr.Description : d.Description;
        var objectives = LegendaryWiki.ObjectiveLines(TextResolver.Clean(raw));
        foreach (var o in objectives) head.Append("\n• ").Append(o);
        var prog = ProgressOf(d);
        head.Append("\n[b]").Append(UiTextEn.Translate("Progress"))
            .Append(":[/b] ").Append(prog.Done).Append(" / ").Append(prog.Total)
            .Append(done ? "  [color=#8fe080]✓[/color]" : "");
        head.Append("\n[b]").Append(UiTextEn.Translate("Achievement Points"))
            .Append(":[/b] ").Append(d.Point);
        var tid = TitleFor(d);
        if (tid != 0)
        {
            head.Append("\n[b]").Append(UiTextEn.Translate("Achievement Title"))
                .Append(":[/b] ").Append(TitleName(tid));
            if (_titles != null && _titles.TryGetValue(tid, out var td) &&
                td.Bonus.Length > 0)
                head.Append(" — ").Append(td.Bonus.Trim());
        }
        box.AddChild(Rich(head.ToString()));
        if (d.ItemCol10 != 0)
            box.AddChild(ObjectiveRow(ItemIcon(d.ItemCol10),
                ItemName(d.ItemCol10),
                UiTextEn.Translate("Reward") + SourceSuffix(d.ItemCol10),
                false, () => ItemTooltip.BuildForId(d.ItemCol10)));
        box.AddChild(Rich("[b]" + UiTextEn.Translate("Objectives") + ":[/b]"));
        if (d.TargetIds.Length == 0)
        {
            var line = objectives.Any() ? objectives.First()
                : TextResolver.Clean(raw);
            box.AddChild(ObjectiveRow(AchievementIcon(d), AchievementName(d.Id),
                line + (d.NeedNum > 1 ? $"  ({prog.Done}/{d.NeedNum})" : ""),
                done, () => ItemTooltip.BuildTextTip(AchievementName(d.Id),
                    TextResolver.Clean(raw))));
            return;
        }
        foreach (var t in d.TargetIds)
        {
            if (_defs.TryGetValue(t, out var td))
            {
                var tdone = PlayerAchievements.IsCompleted(t);
                var tdesc = TextResolver.Clean(_texts != null &&
                    _texts.TryGetValue(t, out var tt) ? tt.Description
                    : td.Description);
                var sub = tdesc;
                box.AddChild(ObjectiveRow(AchievementIcon(td), AchievementName(t),
                    sub, tdone, () => ItemTooltip.BuildTextTip(
                        AchievementName(t), tdesc)));
            }
            else
            {
                var tdone = PlayerAchievements.IsCompleted(t);
                box.AddChild(ObjectiveRow(ItemIcon(t), ItemName(t),
                    SourceSuffix(t).TrimStart(' ', '·'), tdone,
                    () => ItemTooltip.BuildForId(t)));
            }
        }
    }

    private static string SourceSuffix(int itemId)
    {
        var src = ItemSources.Of(itemId);
        return src.Count == 0 ? "" : " · " + string.Join(" ; ", src.Take(4));
    }

    private static Texture2D? ItemIcon(int itemId)
    {
        var def = InventoryWindow.LookupDef(itemId);
        return def is { Code.Length: > 0 }
            ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png") : null;
    }

    /// <summary>Clic sur le lien titre (« title:ID ») — l'onglet Title du
    /// profil s'y abonne.</summary>
    public static event Action<int>? TitleLinkClicked;

    private void OnDetailMeta(Variant meta)
    {
        var m = meta.AsString();
        if (m.StartsWith("title:") && int.TryParse(m[6..], out var id))
            TitleLinkClicked?.Invoke(id);
    }

    /// <summary>Tous les titres (T_Title/c_title) — pour l'onglet Title.</summary>
    public static IReadOnlyDictionary<int, TitleTable.TitleDef> AllTitles
    {
        get { EnsureTables(); return _titles ??
            new Dictionary<int, TitleTable.TitleDef>(); }
    }

    /// <summary>Nom d'un titre (T_Title EN / c_title Big5) — public
    /// pour ProfileWindow.</summary>
    public static string TitleName(int titleId)
    {
        EnsureTables();
        if (_titles != null &&
            _titles.TryGetValue(titleId, out var t) &&
            t.Name.Length > 0)
            return t.Name;
        return $"#{titleId}";
    }

    private static string RewardText(AchievementTable.AchievementDef d)
    {
        var parts = new List<string>();
        if (TitleFor(d) != 0)
            parts.Add(UiTextEn.Translate("Title") + " " +
                TitleName(TitleFor(d)));
        if (d.ItemCol10 != 0)
            parts.Add(UiTextEn.Translate("Item") + " " +
                ItemName(d.ItemCol10));
        return "→ " + string.Join(", ", parts);
    }

    private static string ItemName(int itemId) =>
        TextResolver.ItemName(itemId)
        ?? (_items != null && _items.TryGetValue(itemId, out var d) &&
            d.Name.Length > 0 ? d.Name : $"#{itemId}");

    // ------------------------- panneau -------------------------

    private void BuildPanel()
    {
        _panel = new Panel { Name = "AchievementPanel" };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        // Décalé à droite : la fenêtre daily (centrée-gauche) reste
        // visible quand les deux sont ouvertes.
        _panel.Position = new Vector2(-100, -290);
        _panel.Size = new Vector2(660, 580);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = EeSkin.WindowBody,
            BorderColor = EeSkin.DarkLine,
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, UiTextEn.Translate("Achievements"));
        DragHelper.Persist(_panel, "achievements");

        _tree = new Tree
        {
            Position = new Vector2(10, 32),
            Size = new Vector2(200, 420),
            HideRoot = true,
        };
        _tree.ItemSelected += OnTreeSelected;
        _panel.AddChild(_tree);

        _totals = new Label
        {
            Position = new Vector2(10, 458),
            Size = new Vector2(200, 66),
        };
        _totals.AddThemeFontSizeOverride("font_size", 11);
        _totals.Text = UiTextEn.Translate(
            "Total : —\nHunt  : —\nTown  : —");
        _panel.AddChild(_totals);

        _listHeader = new Label
        {
            Position = new Vector2(220, 32),
            Size = new Vector2(430, 20),
        };
        _listHeader.AddThemeFontSizeOverride("font_size", 10);
        _panel.AddChild(_listHeader);

        _scroll = new ScrollContainer
        {
            Position = new Vector2(220, 54),
            Size = new Vector2(430, 340),
            HorizontalScrollMode =
                ScrollContainer.ScrollMode.ShowNever,
        };
        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _list.AddThemeConstantOverride("separation", 4);
        _scroll.AddChild(_list);
        _panel.AddChild(_scroll);

        _detail = new RichTextLabel
        {
            Position = new Vector2(220, 400),
            Size = new Vector2(430, 124),
            BbcodeEnabled = true,
            ScrollActive = true,
            FitContent = false,
        };
        _detail.AddThemeFontSizeOverride("normal_font_size", 11);
        _detail.AddThemeFontSizeOverride("bold_font_size", 11);
        _detail.AddThemeStyleboxOverride("normal", EeSkin.InsetBox());
        _detail.MetaClicked += OnDetailMeta;
        _panel.AddChild(_detail);

        var daily = new Button
        {
            Text = UiTextEn.Translate("Daily Reward"),
            Position = new Vector2(520, 28),
            Size = new Vector2(130, 24),
        };
        daily.AddThemeFontSizeOverride("font_size", 10);
        daily.Pressed += () => DailyRewardWindow.ToggleStatic();
        _panel.AddChild(daily);

        var hint = new Label
        {
            Text = UiTextEn.Translate(
                "Progress of started achievements is synced by the server (480)."),
            Position = new Vector2(10, 552),
            Size = new Vector2(640, 20),
        };
        hint.AddThemeFontSizeOverride("font_size", 9);
        hint.AddThemeColorOverride("font_color",
            new Color(0.5f, 0.5f, 0.6f));
        _panel.AddChild(hint);
    }
}
