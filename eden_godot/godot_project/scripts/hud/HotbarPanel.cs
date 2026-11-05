using Godot;
using System.IO;
using EdenClient.Data;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Hotbar bas-centre — instancie le layout natif
/// <c>FunctionWnd.layout</c> (FastBar1/2/3 = 12 SlotImage sur fond
/// <c>main:FunctionFastBar</c>) et les barres fines EXP/Soul de
/// <c>FucntionBarWnd.layout</c> en bas-gauche.
///
/// Les barres sont repositionnées bas-centre (la géométrie native du
/// layout ancre FastBar1 en haut — repositionnement runtime assumé par
/// le client natif) : barre principale (touches 1-9,0,-,+) en bas,
/// rangée Ctrl (C1…C+) au-dessus, FastBar3/0 cachées. Chaque slot
/// reçoit un label de touche + un bouton transparent. Un slot sans
/// attribution serveur reste vide ; aucune icône de démo n'est inventée.
/// </summary>
public partial class HotbarPanel : Control
{
    private static readonly string[] MainKeys =
        { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "'", "+" };
    private static readonly string[] CtrlKeys =
        { "C1", "C2", "C3", "C4", "C5", "C6",
          "C7", "C8", "C9", "C0", "C'", "C+" };
    private ProgressBar? _expBar, _soulBar;
    private Label? _expText;

    public void Build(HudRoot hud)
    {
        var root = hud.Build("FunctionWnd.layout");
        if (root != null)
        {
            AddChild(root);
            // La racine est plein-écran sans MousePassThroughEnabled :
            // sans Ignore elle avalerait tous les clics monde.
            root.MouseFilter = MouseFilterEnum.Ignore;
            // Barres recadrées bas-centre : la sérialisation native
            // place FastBar1 en haut (y=0) — le client natif les
            // repositionne à l'exécution ; on épingle explicitement
            // (marge basse 4 px — le -1 d'avant rendait la barre
            // clippée au bord de l'écran).
            // Ref HotbarPanel.png : ancre x native 0.297852×W (la
            // barre n'est PAS centrée), rangée principale au bord bas,
            // rangée Ctrl juste au-dessus, 3e rangée au-dessus encore
            // (ouverte par le « + » de la rangée principale).
            _hud = hud;
            PlaceBar(root, "FastBar1", -45, 0, MainKeys, 0);
            PlaceBar(root, "FastBar2", -90, -45, CtrlKeys, 1);
            PlaceBar(root, "FastBar3", -135, -90, CtrlKeys, 2);
            // H7 : les slots FastBar1 affichent les VRAIS sorts du
            // perso (311 → PlayerSkills → SpellCatalog icônes
            // C_Spell), pas des placeholders.
            Protocol.PlayerSkills.Changed += RefreshAllRows;
            RefreshAllRows();
            // FastBar1 native : ▲/▼ = page courante (11 pages × 10
            // slots, ids plats page*10+slot — FUN_00884810 rf_game),
            // PageText = numéro affiché. Page non reçue au 351 →
            // GetShortcutOfPage (55) → 352.
            var bar1 = TopLeftVitals.Find<Control>(root, "FastBar1");
            _pageText = TopLeftVitals.Find<Label>(bar1, "PageText");
            if (_pageText != null) _pageText.Text = "1";
            var up = TopLeftVitals.Find<BaseButton>(bar1, "UpBtn");
            if (up != null) up.Pressed += () => ChangePage(1);
            var down = TopLeftVitals.Find<BaseButton>(bar1, "DownBtn");
            if (down != null) down.Pressed += () => ChangePage(-1);
            // FastBar3 (3e rangée native) et FastBar0 (barre auxiliaire
            // à la géométrie sérialisée incohérente — s'étendait hors
            // écran) restent cachées en v1.
            var bar0 = TopLeftVitals.Find<Control>(root, "FastBar0");
            if (bar0 != null) bar0.Visible = false;
            var bar3 = TopLeftVitals.Find<Control>(root, "FastBar3");
            if (bar3 != null) bar3.Visible = false;
            // « + » (OpenBtn) de la rangée principale : ouvre la
            // PROCHAINE rangée cachée dans l'ordre (barre 2 puis 3 —
            // max 3 rangées, comportement natif) ; ✕ (CloseBtn) de
            // chaque rangée auxiliaire la replie.
            var bar2 = TopLeftVitals.Find<Control>(root, "FastBar2");
            var open = TopLeftVitals.Find<BaseButton>(
                TopLeftVitals.Find<Control>(root, "FastBar1"), "OpenBtn");
            if (open != null)
                open.Pressed += () =>
                {
                    if (bar2 != null && !bar2.Visible)
                        bar2.Visible = true;
                    else if (bar3 != null)
                        bar3.Visible = true;
                };
            foreach (var name in new[] { "FastBar2", "FastBar3" })
            {
                var bar = TopLeftVitals.Find<Control>(root, name);
                var close = TopLeftVitals.Find<BaseButton>(bar, "CloseBtn");
                if (close != null && bar != null)
                    close.Pressed += () => bar.Visible = false;
            }
        }
        else
        {
            BuildFallbackBar();
        }

        var funcBar = hud.Build("FucntionBarWnd.layout");
        if (funcBar != null)
        {
            // Racine 302×44 sérialisée en haut-gauche (0,0) : épinglée
            // en bas-gauche sous la colonne chat (natif : mini-barres
            // EXP/Soul au-dessus du bord bas).
            // Ref DoubleEXPbar.png : x 2..298, y H-44..H-2.
            HudRoot.Pin(funcBar, 0, 1, 0, 1, 2, -44, 304, -2);
            AddChild(funcBar);
            _expBar = TopLeftVitals.Find<ProgressBar>(funcBar, "bar");
            _soulBar = _expBar != null
                ? TopLeftVitals.Find<ProgressBar>(
                    TopLeftVitals.Find<Control>(funcBar, "SoulBackGrand"),
                    "bar")
                : null;
            _expText = TopLeftVitals.Find<Label>(_expBar, "text");
            _soulText = TopLeftVitals.Find<Label>(_soulBar, "text");
            // EXP perso vert pomme / EXP classe violet, piste sombre.
            StyleExpBar(_expBar, EeSkin.ExpFill);
            StyleExpBar(_soulBar, EeSkin.ClassExpFill);
            // Bouton Repair All natif (image absente) : pas sur la ref.
            var repair = TopLeftVitals.Find<Control>(funcBar, "RepairAllBtn");
            if (repair != null) repair.Visible = false;
            // Clic droit sur la double barre → choix de précision.
            // Les ProgressBar héritent MouseFilter.Stop : Pass laisse
            // le hover (tooltip natif « cur / max ») et fait remonter
            // le clic droit à funcBar, comme la fenêtre CEGUI native.
            funcBar.MouseFilter = MouseFilterEnum.Stop;
            foreach (var b in new[] { _expBar, _soulBar })
                if (b != null) b.MouseFilter = MouseFilterEnum.Pass;
            funcBar.GuiInput += e =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed &&
                    mb.ButtonIndex == MouseButton.Right)
                    ShowPrecisionMenu(mb.GlobalPosition);
            };
            // Slots icônes natifs (désasm rf_game FUN_008961b0 :
            // setImage sur +0xe8/+0xec) — SoulBackGrand/icon = icône
            // de classe (JoBIamge{classe}) ; expBackGrand/icon = trait
            // héroïque (NatrueIamge{feature}, feature absente
            // serveur/DB → vide, même blocage que CharIcon U1).
            _soulIcon = TopLeftVitals.Find<TextureRect>(
                TopLeftVitals.Find<Control>(funcBar, "SoulBackGrand"),
                "icon");
            if (_soulIcon != null) _soulIcon.Texture = _pendingClassIcon;
            // A4.5 : expBackGrand/icon = trait héroïque (HeroTrait).
            _expIcon = TopLeftVitals.Find<TextureRect>(
                TopLeftVitals.Find<Control>(funcBar, "expBackGrand"),
                "icon");
            ApplyTrait();
            HeroTrait.Changed += () =>
                Callable.From(ApplyTrait).CallDeferred();
            RenderExp();
        }
    }

    private TextureRect? _soulIcon;
    private TextureRect? _expIcon;

    private void ApplyTrait()
    {
        if (_expIcon != null && GodotObject.IsInstanceValid(_expIcon))
            _expIcon.Texture = HeroTrait.Icon();
    }
    private Texture2D? _pendingClassIcon;

    public void SetClassIcon(Texture2D? tex)
    {
        _pendingClassIcon = tex;
        if (_soulIcon != null) _soulIcon.Texture = tex;
    }

    private HudRoot? _hud;
    private Label? _soulText;
    private double? _expRatio, _classRatio;
    private long _rawExp;
    /// <summary>Décimales du pourcentage (0..3, natif 3 → 0.000%).</summary>
    private int _precision = 3;
    private PopupMenu? _precisionMenu;

    private static void StyleExpBar(ProgressBar? bar, StyleBox fill)
    {
        if (bar == null) return;
        bar.AddThemeStyleboxOverride("fill", fill);
        bar.AddThemeStyleboxOverride("background", EeSkin.BarTrack());
        var t = TopLeftVitals.Find<Label>(bar, "text");
        if (t != null)
        {
            EeSkin.Shadowed(t, 11);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    /// <summary>Mini-menu de précision XX / XX.X / XX.XX / XX.XXX
    /// (hud-interfaces.md — clic droit sur la barre EXP).</summary>
    private void ShowPrecisionMenu(Vector2 at)
    {
        if (_precisionMenu == null)
        {
            _precisionMenu = new PopupMenu();
            string[] labels = { "XX%", "XX.X%", "XX.XX%", "XX.XXX%" };
            for (var i = 0; i < labels.Length; i++)
                _precisionMenu.AddRadioCheckItem(labels[i], i);
            _precisionMenu.IdPressed += id =>
            {
                _precision = (int)id;
                RenderExp();
            };
            AddChild(_precisionMenu);
        }
        for (var i = 0; i < 4; i++)
            _precisionMenu.SetItemChecked(i, i == _precision);
        _precisionMenu.Position = (Vector2I)at;
        _precisionMenu.Popup();
    }

    private string Pct(double r) =>
        (Math.Clamp(r, 0, 1) * 100).ToString("F" + _precision,
            System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>Tooltip natif « %s: %lld / %lld »
    /// (rf_game FUN_008959d0/FUN_00895b80).</summary>
    private static string LevelTip(string label, long? threshold, long cur) =>
        threshold is { } t ? $"{label}: {cur} / {t}" : "";

    private void RenderExp()
    {
        if (_expBar != null)
        {
            _expBar.Value = _expRatio ?? 0;
            _expBar.TooltipText = LevelTip(
                UiTextEn.Translate("EXP"), Data.LevelTable.ExpForNext(_level), _rawExp);
        }
        if (_soulBar != null)
        {
            _soulBar.Value = _classRatio ?? 0;
            _soulBar.TooltipText = LevelTip(UiTextEn.Translate("Class EXP"),
                Data.LevelTable.ClassExpForNext(_classLevel),
                Protocol.PlayerVitals.ClassExp);
        }
        if (_expText != null)
            _expText.Text = _expRatio is { } e ? Pct(e) : $"{_rawExp}";
        if (_soulText != null)
            _soulText.Text = _classRatio is { } c ? Pct(c) : "";
    }

    /// <summary>XP courante (NCID 600 i64) — affichée en brut, la
    /// table c_level n'étant pas encore jointe (HYPOTHESIS ratio).</summary>
    public void SetExp(long exp)
    {
        _rawExp = exp;
        Recompute();
    }

    private int _level, _classLevel;
    private bool _classHooked;

    /// <summary>Niveaux perso / classe (H8) : base des ratios.</summary>
    public void SetLevels(int level, int classLevel)
    {
        if (level > 0) _level = level;
        if (classLevel > 0) _classLevel = classLevel;
        if (!_classHooked)
        {
            _classHooked = true;
            Protocol.PlayerVitals.ClassExpChanged += Recompute;
        }
        Recompute();
    }

    private void Recompute()
    {
        _expRatio = _level > 0
            ? Data.LevelTable.ExpRatio(_level, _rawExp) : null;
        _classRatio = _classLevel > 0
            ? Data.LevelTable.ClassRatio(_classLevel,
                Protocol.PlayerVitals.ClassExp) : null;
        RenderExp();
    }

    /// <summary>Repositionne une FastBar native bas-centre (450 px
    /// centrés, ancre 0.297852) et décore ses slots : label touche,
    /// clic droit = activation, drag/drop = raccourcis (slots 0-9 —
    /// 10 par page en wire ; 11/12 = extras non bindés).</summary>
    private const float SlotGrowTop = 2f, SlotGrowBottom = 3f;

    private void PlaceBar(Control root, string barName, int topOffset,
        int bottomOffset, string[] keys, int row)
    {
        var bar = TopLeftVitals.Find<Control>(root, barName);
        if (bar == null) return;
        HudRoot.Pin(bar, 0.297852f, 1, 0.297852f, 1,
            0, topOffset, 450, bottomOffset);
        var empty = _hud != null ? EeSkin.EmptySlot(_hud) : null;
        // Verrou natif (FunctionLock*) à droite de chaque rangée —
        // ShortcutLock1/2/3 du client, bloque drop/drag de sa rangée.
        if (_hud != null)
            EeSkin.ImageCheck(_hud, TopLeftVitals.Find<Control>(bar,
                "LockBtn"), "main", "FunctionLockNormal",
                "FunctionLockPress");
        if (TopLeftVitals.Find<BaseButton>(bar, "LockBtn") is { } lk)
        {
            var r = row;
            lk.Toggled += on => _locked[r] = on;
        }
        for (var i = 0; i < 12; i++)
        {
            var slot = bar.FindChild($"*_slot{i + 1}", true, false)
                as TextureRect;
            if (slot == null) continue;
            slot.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            slot.StretchMode = TextureRect.StretchModeEnum.Scale;
            // Retour propriétaire : cases trop petites/rectangulaires
            // (~37×32 natif) → +5 px de haut, la rangée de 45 px les contient.
            slot.OffsetTop -= SlotGrowTop;
            slot.OffsetBottom += SlotGrowBottom;
            // Case vide fleur-de-lys native sous l'icône (ref).
            if (empty != null)
            {
                var bg = new Panel
                {
                    Name = "__empty",
                    MouseFilter = MouseFilterEnum.Ignore,
                    ShowBehindParent = true,
                };
                bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                bg.AddThemeStyleboxOverride("panel", empty);
                slot.AddChild(bg);
            }
            // Ref : libellé de touche blanc en haut-gauche du slot.
            var num = new Label
            {
                Text = keys[i],
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            num.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            num.OffsetLeft = 2;
            EeSkin.Shadowed(num, 11);
            slot.AddChild(num);

            _rowSlots[row].Add(slot);
            slot.Texture = null;

            // hud-interfaces.md : clic droit = activer le slot (clic
            // gauche réservé au déplacement des raccourcis).
            var hit = new HitSlot { Name = $"__hit{i + 1}", Text = "" };
            hit.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            hit.Flat = true;
            hit.FocusMode = FocusModeEnum.None;
            hit.ButtonMask = MouseButtonMask.Right;
            var n = i;
            var r2 = row;
            {
                // Forwarding sur « hit » — pas « slot » : hit est le
                // contrôle STOP le plus haut, la chaîne drag&drop
                // Godot s'arrête sur lui avant d'atteindre slot.
                hit.SetDragForwarding(
                    Callable.From((Vector2 _) =>
                        DragShortcut(r2, n, hit) is { } dd
                            ? Variant.From(dd) : default),
                    Callable.From((Vector2 _, Variant d) =>
                        CanDropShortcut(r2, n, d)),
                    Callable.From((Vector2 _, Variant d) =>
                        DropShortcut(r2, n, d)));
                // Drag relâché hors cible = retrait du raccourci (EE :
                // glisser l'icône dans le vide la supprime de la barre).
                hit.DragEnded = ok =>
                {
                    if (!ok && _dragFlat >= 0) RemoveFlat(_dragFlat);
                    _dragFlat = -1;
                };
                // Clic-prendre / clic-poser : hit reçoit press+release
                // (focus souris) — un clic sans déplacement = prendre
                // le contenu du slot ou poser ce qu'on tient.
                hit.GuiInput += e =>
                {
                    switch (e)
                    {
                        case InputEventMouseButton
                            { ButtonIndex: MouseButton.Left } mb:
                            if (mb.Pressed)
                            {
                                _pressHit = hit;
                                _pressPos = mb.GlobalPosition;
                            }
                            else if (_pressHit == hit)
                            {
                                ClickSlot(r2, n);
                            }
                            break;
                        case InputEventMouseMotion mm:
                            if (_pressHit == hit &&
                                (mm.GlobalPosition - _pressPos)
                                    .Length() > 4)
                                _pressHit = null;  // drag Godot
                            break;
                    }
                };
            }
            hit.Pressed += () => Activate(r2, n);
            // Overlay de cooldown (objet : groupe S_Item ; sort : 368 kind 1)
            // sous la zone de clic, + vibration à l'activation.
            var cd = CooldownOverlay.Attach(slot);
            cd.Source = () => n >= SlotCount ? null : FlatCooldown(
                _flat[RowBase(r2) + n]);
            _overlays[(r2, n)] = cd;
            slot.AddChild(hit);
        }
    }

    /// <summary>Rafraîchit les 3 rangées depuis l'espace d'ids plat.
    /// Rangée 0 = page courante, 1/2 = pages fixes 6/7 (déduites du
    /// désasm FUN_00884810 : id 10+ → +50).</summary>
    private readonly Dictionary<(int, int), CooldownOverlay> _overlays = new();

    /// <summary>Cooldown (restant, total ms) de la valeur plate d'un raccourci.</summary>
    private static (long, long)? FlatCooldown(int v)
    {
        if (v == 0 || v == -1) return null;
        if ((v & ItemTag) != 0)
        {
            var d = InventoryWindow.LookupDef(v & ~ItemTag);
            return d is { CooldownDs: > 0 } && Protocol.CooldownTracker.Item(
                d.CooldownGroup, out var r, out var t) ? (r, t) : null;
        }
        return Protocol.CooldownTracker.Spell(v, out var rs, out var ts)
            ? (rs, ts) : null;
    }

    private void RefreshAllRows()
    {
        for (var r = 0; r < 3; r++) RefreshRow(r);
    }

    private void RefreshRow(int row)
    {
        var slots = _rowSlots[row];
        for (var n = 0; n < slots.Count && n < 12; n++)
        {
            var slot = slots[n];
            if (!GodotObject.IsInstanceValid(slot)) continue;
            var v = _flat[FlatId(row, n)];
            var hitBtn = slot.GetNodeOrNull<Control>($"__hit{n + 1}") ?? slot;
            ItemTooltip.BindId(hitBtn, () => 0);
            if (v != 0 && v != -1)
            {
                if ((v & ItemTag) != 0)
                {
                    var id = v & ~ItemTag;
                    var def = InventoryWindow.LookupDef(id);
                    slot.Texture = def is { Code.Length: > 0 }
                        ? AssetCatalog.GetTexture(
                            $"UI/itemicon/{def.Code}.png")
                        : null;
                    slot.TooltipText = "";
                    ItemTooltip.BindId(hitBtn, () => id);
                }
                else
                {
                    slot.Texture =
                        Combat.SpellCatalog.Icon((ushort)v);
                    slot.TooltipText = "";
                    // Survol : fiche de sort (nom, coût, recharge,
                    // description) — ref « skill hover info ».
                    var sid = v;
                    ItemTooltip.BindCustom(hitBtn, () =>
                    {
                        var lv = 1;
                        foreach (var sp in Protocol.PlayerSkills.Spells)
                            if (sp.SpellMaster == sid) { lv = sp.Level; break; }
                        return ItemTooltip.BuildSkillTip(sid, lv,
                            Combat.SpellCatalog.PayType(sid), true, -1,
                            preview: true);
                    });
                }
                continue;
            }
            slot.Texture = null;
            slot.TooltipText = "";
        }
    }

    // ---- Raccourcis : espace d'ids plat natif (U6) ----
    // Wire : page 0-10 × slot 0-9 → id plat = page*10+slot ; la rangée
    // principale est pagée (▲/▼ + PageText), les rangées 2/3 sont les
    // pages fixes 6/7 (id visuel 10-19 → +50 → 60-69 ; 20-29 → 70-79 —
    // FUN_00884810 rf_game). Les extras (ids 100-109, Extras du 351)
    // occupent les slots 11/12 — non rendus en v1.

    /// <summary>Slots raccourci par page (wire).</summary>
    private const int SlotCount = 10;
    /// <summary>Base des ids « extras » (slots 11/12 visuels) — la
    /// page wire 10 est inutilisée par les barres normales (0-9) et
    /// acceptée par le handler serveur (page ≤ 10) : rangée r,
    /// slot 10/11 → flat 100+r*2+(n-10) → page 10 slot 0..5.</summary>
    private const int ExtraBase = 100;
    /// <summary>Marque « objet » dans la valeur i32 d'un raccourci
    /// (champ opaque côté serveur).</summary>
    private const int ItemTag = 0x40000000;
    private readonly int[] _flat = new int[110];
    private int _page;
    private Label? _pageText;
    private readonly bool[] _locked = new bool[3];
    private readonly List<TextureRect>[] _rowSlots =
        { new(), new(), new() };
    public static HotbarPanel? Instance { get; private set; }

    /// <summary>Base d'ids plat de la rangée (0 = page courante).</summary>
    private int RowBase(int row) =>
        row == 0 ? _page * SlotCount : 50 + row * SlotCount;

    /// <summary>▲/▼ de FastBar1 — page 0..10 ; demande la page au
    /// serveur (55) si absente du 351.</summary>
    private void ChangePage(int delta)
    {
        _page = Math.Clamp(_page + delta, 0, 10);
        if (_pageText != null) _pageText.Text = $"{_page + 1}";
        if (Protocol.PlayerProfileState.ShortcutBars.Count <= _page)
            _ = _hud?.SendZoneAsync?.Invoke(
                new Protocol.ZoneServerGetShortcutOfPage
                { Field1 = (short)_page, Field2 = 0, Field3 = 0 });
        RefreshAllRows();
    }

    /// <summary>Id plat du slot visuel (row, n) : n&lt;10 → page +
    /// slot ; n∈{10,11} → extras 100+ (page wire 10).</summary>
    private int FlatId(int row, int n) =>
        n < SlotCount ? RowBase(row) + n
                      : ExtraBase + row * 2 + (n - SlotCount);

    /// <summary>Payload drag d'un slot : valeur serveur réelle
    /// ({fromFlat} pour le move) ; icône de repli rangée 0 = copie du
    /// sort connu ({spellId} sans fromFlat — la source reste).</summary>
    private Godot.Collections.Dictionary? SlotPayload(int row, int n)
    {
        if (_locked[row]) return null;
        var flat = FlatId(row, n);
        var v = _flat[flat];
        if (v != 0 && v != -1)
        {
            var d = new Godot.Collections.Dictionary
                { ["fromFlat"] = flat };
            if ((v & ItemTag) != 0) d["itemId"] = v & ~ItemTag;
            else d["spellId"] = v;
            return d;
        }
        return null;
    }

    /// <summary>Id plat du raccourci en cours de drag (fromFlat
    /// seulement — le repli n'a rien à retirer). -1 = pas de drag
    /// réel en cours.</summary>
    private int _dragFlat = -1;

    private Godot.Collections.Dictionary? DragShortcut(
        int row, int n, Control source)
    {
        var d = SlotPayload(row, n);
        if (d == null) return null;
        _dragFlat = d.ContainsKey("fromFlat") ? FlatId(row, n) : -1;
        var slot = _rowSlots[row][n];
        if (slot.Texture != null)
            source.SetDragPreview(new TextureRect
            {
                Texture = slot.Texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = new Vector2(34, 34),
            });
        return d;
    }

    private bool CanDropShortcut(int row, int n, Variant d) =>
        !_locked[row] && d.VariantType == Variant.Type.Dictionary &&
        (d.AsGodotDictionary().ContainsKey("itemId") ||
         d.AsGodotDictionary().ContainsKey("spellId"));

    /// <summary>Touche / clic droit : valeur du raccourci (objet →
    /// ItemUse/équipe, sort → cast) ; slot vide → no-op.</summary>
    public void Activate(int row, int n)
    {
        if (n >= 12) return;
        var v = _flat[FlatId(row, n)];
        if (v != 0 && v != -1)
        {
            if (_overlays.TryGetValue((row, n), out var ov)) ov.Shake();
            if ((v & ItemTag) != 0)
                _hud?.Inventory?.UseByItemId(v & ~ItemTag);
            else Combat.SkillCast.Instance?.Cast((ushort)v);
            return;
        }
    }

    private void DropShortcut(int row, int n, Variant d)
    {
        var dd = d.AsGodotDictionary();
        var value = dd.ContainsKey("spellId")
            ? dd["spellId"].AsInt32()
            : ItemTag | dd["itemId"].AsInt32();
        if (dd.ContainsKey("spellId")) Combat.SpellFxWarmup.Warm(dd["spellId"].AsInt32());
        var dst = FlatId(row, n);
        var src = dd.ContainsKey("fromFlat")
            ? dd["fromFlat"].AsInt32() : -1;
        var replaced = _flat[dst];
        // Déplacement : la valeur quitte son slot source ; dst occupé
        // → échange natif (l'occupant repart au slot source).
        if (src >= 0 && src != dst)
            _flat[src] = replaced is 0 or -1 ? 0 : replaced;
        _flat[dst] = value;
        // OldValue : move → la valeur déplacée (le serveur la retire
        // partout — désasm CCharacter::UpdateShortcut) ; drop externe
        // → le contenu remplacé à destination.
        var oldV = src >= 0 ? value
            : (replaced != 0 && replaced != -1 ? replaced : 0);
        _ = _hud?.SendZoneAsync?.Invoke(
            new Protocol.ZoneServerUpdateShortcut
            {
                Page = (short)(dst / SlotCount),
                Slot = (short)(dst % SlotCount),
                Value = value, OldValue = oldV,
            });
        // Échange : l'occupant de dst repart au slot source
        // (set direct — old_field=0 pour ne rien effacer d'autre).
        if (src >= 0 && src != dst && replaced != 0 && replaced != -1)
            _ = _hud?.SendZoneAsync?.Invoke(
                new Protocol.ZoneServerUpdateShortcut
                {
                    Page = (short)(src / SlotCount),
                    Slot = (short)(src % SlotCount),
                    Value = replaced, OldValue = 0,
                });
        RefreshAllRows();
    }

    /// <summary>Retire un raccourci (drag dans le vide / clic-poser
    /// dans le vide) : slot ← 0 + old_field = la valeur retirée
    /// (ClearShortcutValue natif — la valeur disparaît partout).</summary>
    private void RemoveFlat(int flat)
    {
        var v = flat is >= 0 and < 110 ? _flat[flat] : 0;
        if (v == 0 || v == -1) return;
        _flat[flat] = 0;
        _ = _hud?.SendZoneAsync?.Invoke(
            new Protocol.ZoneServerUpdateShortcut
            {
                Page = (short)(flat / SlotCount),
                Slot = (short)(flat % SlotCount),
                Value = 0, OldValue = v,
            });
        RefreshAllRows();
    }

    private void OnProfileChanged(string section)
    {
        if (section != "shortcuts") return;
        var bars = Protocol.PlayerProfileState.ShortcutBars;
        // Changement de classe : la nouvelle série peut être plus
        // courte — la page courante est clampée à ce qui existe.
        if (bars.Count > 0 && _page > bars.Count - 1)
        {
            _page = bars.Count - 1;
            if (_pageText != null) _pageText.Text = $"{_page + 1}";
        }
        Array.Clear(_flat);
        for (var p = 0; p < bars.Count && p < 11; p++)
            for (var i = 0; i < SlotCount && i < bars[p].Length; i++)
            {
                _flat[p * SlotCount + i] = bars[p][i];
                // Sort (pas un objet) posé sur la barre : préchauffe son effet hors thread principal.
                if (bars[p][i] > 0 && (bars[p][i] & ItemTag) == 0) Combat.SpellFxWarmup.Warm(bars[p][i]);
            }
        RefreshAllRows();
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        Instance = this;
        Protocol.PlayerProfileState.Changed += OnProfileChanged;
    }

    public override void _ExitTree()
    {
        Protocol.PlayerProfileState.Changed -= OnProfileChanged;
        Protocol.PlayerSkills.Changed -= RefreshAllRows;
        if (_classHooked)
            Protocol.PlayerVitals.ClassExpChanged -= Recompute;
        base._ExitTree();
    }

    // ---- Clic-prendre / clic-poser (alternative au glisser) ----

    /// <summary>Payload « en main » : {itemId}, {spellId} ou
    /// {itemId|spellId, fromFlat} — posée au clic sur un slot.</summary>
    public static Variant? HeldPayload { get; private set; }
    private TextureRect? _heldIcon;
    private Control? _pressHit;
    private Vector2 _pressPos;
    private Control? _ghosted;

    /// <summary>Prend un payload (icône suivant le curseur) —
    /// une seule chose « en main » : l'éventuel item tenu du sac
    /// est reposé.</summary>
    public static void PickPayload(Variant payload, Texture2D? icon)
    {
        InventoryWindow.CancelHeld();
        HeldPayload = payload;
        GD.Print("[dnd-click] pick");
        if (Instance == null) return;
        if (Instance._heldIcon == null)
        {
            Instance._heldIcon = new TextureRect
            {
                TopLevel = true, ZIndex = 4000,
                MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode =
                    TextureRect.StretchModeEnum.KeepAspectCentered,
                Size = new Vector2(34, 34),
            };
            Instance.AddChild(Instance._heldIcon);
        }
        Instance._heldIcon.Texture = icon;
        Instance._heldIcon.Visible = true;
    }

    /// <summary>Pose/vide la main (sans drop).</summary>
    public static void ClearPayload()
    {
        HeldPayload = null;
        GD.Print("[dnd-click] clear");
        if (Instance?._heldIcon != null)
            Instance._heldIcon.Visible = false;
        if (Instance?._ghosted != null)
        {
            if (GodotObject.IsInstanceValid(Instance._ghosted))
                Instance._ghosted.Modulate = Colors.White;
            Instance._ghosted = null;
        }
    }

    /// <summary>Prend le contenu d'un slot (réel ou icône de repli)
    /// comme payload ; la source réelle est grisée en attendant.</summary>
    private Variant? PickSlot(int row, int n)
    {
        var d = SlotPayload(row, n);
        if (d == null) return null;
        if (d.ContainsKey("fromFlat"))
        {
            _ghosted = _rowSlots[row][n];
            _ghosted.Modulate = new Color(1, 1, 1, 0.35f);
        }
        return Variant.From(d);
    }

    public override void _Process(double delta)
    {
        if (_heldIcon is { Visible: true })
            _heldIcon.GlobalPosition = GetGlobalMousePosition()
                + new Vector2(6, 6);
    }

    /// <summary>Clic gauche sans déplacement sur un slot (depuis
    /// <c>hit.GuiInput</c>) : pose la « main » (payload ou item du
    /// sac), sinon prend le contenu du slot.</summary>
    private void ClickSlot(int row, int n)
    {
        _pressHit = null;
        GD.Print($"[dnd-click] slot={row},{n} held={InventoryWindow.Held} " +
                 $"payload={HeldPayload != null}");
        if (HeldPayload is { } p)
        {
            DropShortcut(row, n, p);
            ClearPayload();
            return;
        }
        if (InventoryWindow.Held is { } h)
        {
            var it = Protocol.PlayerBag.Items.FirstOrDefault(
                i => i.Bag == h.Bag && i.Slot == h.Slot);
            if (it != null)
                DropShortcut(row, n,
                    new Godot.Collections.Dictionary
                    { ["itemId"] = it.ItemId });
            InventoryWindow.CancelHeld();
            return;
        }
        if (PickSlot(row, n) is { } pv)
            PickPayload(pv, _rowSlots[row][n].Texture);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Clic droit n'importe où : repose la « main ».
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right,
                Pressed: true } && HeldPayload != null)
            ClearPayload();
        // Clic-poser relâché dans le vide (pas de GUI sous le curseur)
        // : un raccourci « en main » venant d'un slot est retiré ;
        // un sort/item pris ailleurs est simplement reposé.
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left,
                Pressed: false } && HeldPayload is { } p &&
            GetViewport().GuiGetHoveredControl() == null)
        {
            if (p.VariantType == Variant.Type.Dictionary &&
                p.AsGodotDictionary().TryGetValue("fromFlat",
                    out var f))
                RemoveFlat(f.AsInt32());
            ClearPayload();
        }
    }

    /// <summary>Bouton de hit d'un slot : reçoit
    /// <see cref="Control.NotificationDragEnd"/> (relâchement du drag
    /// — <see cref="Viewport.GuiIsDragSuccessful"/> = drop accepté).</summary>
    private sealed partial class HitSlot : Button
    {
        /// <summary>Appelé à la fin d'un drag né sur ce slot
        /// (true = posé sur une cible).</summary>
        public Action<bool>? DragEnded;

        public override void _Notification(int what)
        {
            if (what == NotificationDragEnd)
                DragEnded?.Invoke(
                    GetViewport().GuiIsDragSuccessful());
        }
    }

    /// <summary>Repli : barre de slots vide bas-centre.</summary>
    private void BuildFallbackBar()
    {
        var box = new HBoxContainer();
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        box.Position = new Vector2(-225, -46);
        for (var i = 0; i < 12; i++)
        {
            var cell = new Panel
            {
                CustomMinimumSize = new Vector2(32, 40),
            };
            cell.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.116f, 0.112f, 0.1f, 0.7f),
                BorderColor = new Color(0.468f, 0.45f, 0.401f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            var num = new Label
            {
                Text = MainKeys[i],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            num.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            num.AddThemeFontSizeOverride("font_size", 8);
            cell.AddChild(num);
            box.AddChild(cell);
        }
        AddChild(box);
    }

    /// <summary>Sonde <c>--exp-test</c> : clic droit synthétique réel
    /// sur la barre classe (passe par le hit-test viewport — prouve le
    /// MouseFilter.Pass) → menu précision, puis bascule XX.X% —
    /// même injection que <see cref="HudWindows.EscProbe"/>.</summary>
    public sealed partial class ExpProbe : Node
    {
        private readonly HotbarPanel _h;
        private readonly string _shotsDir;
        private int _frame;

        public ExpProbe(HotbarPanel h)
        {
            _h = h; Name = "__exp_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            _frame++;
            // Retenter tant que le menu n'est pas ouvert (loading).
            if (_frame >= 80 && _frame <= 160 && _frame % 10 == 0 &&
                (_h._precisionMenu == null || !_h._precisionMenu.Visible))
                RightClickBar();
            switch (_frame)
            {
                case 170:
                    GD.Print($"[exp-test] menu ouvert=" +
                        $"{_h._precisionMenu?.Visible}, ratios=" +
                        $"{_h._expRatio?.ToString("0.###")}/" +
                        $"{_h._classRatio?.ToString("0.###")}, " +
                        $"textes «{_h._expText?.Text}»/«{_h._soulText?.Text}»");
                    Shot("u2_exp_precision");
                    // Bascule XX.X% — vérifie le reformatage live.
                    _h._precisionMenu?.EmitSignal(
                        PopupMenu.SignalName.IdPressed, 1);
                    break;
                case 200:
                    GD.Print($"[exp-test] après bascule : " +
                        $"«{_h._expText?.Text}»");
                    _h._precisionMenu?.Hide();
                    Shot("u2_exp_bars");
                    SetProcess(false);
                    break;
            }
        }

        private void RightClickBar()
        {
            var bar = _h._soulBar ?? _h._expBar;
            if (bar == null || !bar.IsVisibleInTree())
            {
                GD.PrintErr("[exp-test] barre introuvable");
                return;
            }
            var at = bar.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right, Pressed = true,
                Position = at, GlobalPosition = at,
            });
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right, Pressed = false,
                Position = at, GlobalPosition = at,
            });
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
                GD.PrintErr($"[exp-test] shot : {e.Message}");
            }
        }
    }

    /// <summary><c>--shortcut-test</c> : sonde QA U6 — drop interne
    /// réel (même chemin que le drag) sur rangées 0/1 → C→S 54 ;
    /// activation clic droit → sort (9/17) ou objet (19) ; ▲ → 55 →
    /// 352 ; lock bloque le drop. Capture u6_hotbar.png.</summary>
    public sealed partial class ShortcutProbe : Node
    {
        private readonly HotbarPanel _h;
        private readonly string _shotsDir;
        private int _frame, _step;
        private int _itemId, _spellId;

        public ShortcutProbe(HotbarPanel h)
        {
            _h = h; Name = "__shortcut_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            if (Screens.LoadingScreen.Active != null) return;
            _frame++;
            var bars = Protocol.PlayerProfileState.ShortcutBars;
            switch (_step)
            {
                case 0 when _frame > 120 && Protocol.PlayerBag.Items
                        .Count > 0 && _h._rowSlots[0].Count >= 10:
                    _itemId = Protocol.PlayerBag.Items
                        .First().ItemId;
                    var sp = Protocol.PlayerSkills.Spells
                        .FirstOrDefault();
                    _spellId = sp?.SpellMaster ?? 0;
                    GD.Print($"[shortcut-test] {bars.Count} pages au " +
                        $"351 ; drop item #{_itemId} + sort #{_spellId}");
                    var dict = new Godot.Collections.Dictionary
                        { ["itemId"] = _itemId };
                    _h.DropShortcut(0, 0, dict);
                    _h.DropShortcut(1, 0, dict);   // rangée 2 → page 6
                    if (_spellId > 0)
                        _h.DropShortcut(0, 1,
                            new Godot.Collections.Dictionary
                            { ["spellId"] = _spellId });
                    _step = 1;
                    break;
                case 1 when _frame % 60 == 0:
                    GD.Print($"[shortcut-test] flat0={_h._flat[0]} " +
                        $"flat1={_h._flat[1]} flat60={_h._flat[60]} — " +
                        "activation rangée0 slot1 (sort)");
                    _h.Activate(0, 1);
                    _h.Activate(1, 0);
                    _step = 2;
                    break;
                case 2 when _frame % 60 == 0:
                    // Vérif persistance serveur : 55 pages 0 et 6 →
                    // les 352 doivent refléter nos drops.
                    _ = _h._hud?.SendZoneAsync?.Invoke(
                        new Protocol.ZoneServerGetShortcutOfPage
                        { Field1 = 0, Field2 = 0, Field3 = 0 });
                    _ = _h._hud?.SendZoneAsync?.Invoke(
                        new Protocol.ZoneServerGetShortcutOfPage
                        { Field1 = 6, Field2 = 0, Field3 = 0 });
                    _step = 3;
                    break;
                case 3 when _frame % 60 == 0:
                    GD.Print($"[shortcut-test] post-352 : " +
                        $"bars={bars.Count} " +
                        $"p0s0={(bars.Count > 0 ? bars[0][0] : -99)} " +
                        $"p6s0={(bars.Count > 6 ? bars[6][0] : -99)}");
                    // ▲ = page suivante (55 si absente).
                    _h.ChangePage(1);
                    GD.Print($"[shortcut-test] ▲ → page {_h._page} " +
                        $"texte «{_h._pageText?.Text}»");
                    _h.ChangePage(-1);
                    // Lock : bloque le drop de sa rangée.
                    _h._locked[0] = true;
                    var blocked = !_h.CanDropShortcut(0, 2,
                        new Godot.Collections.Dictionary
                        { ["itemId"] = _itemId });
                    _h._locked[0] = false;
                    GD.Print($"[shortcut-test] lock bloque drop=" +
                        $"{blocked}");
                    Shot("u6_hotbar");
                    _h.ChangePage(0);   // restore display
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
                GD.PrintErr($"[shortcut-test] shot : {e.Message}");
            }
        }
    }
}
