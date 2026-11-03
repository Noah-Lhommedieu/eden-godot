using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;
using EdenClient.Screens;

namespace EdenClient.Hud;

/// <summary>
/// Bandeau de buffs/debuffs du joueur — instancie le layout natif
/// <c>BuffWnd.layout</c> (15 <c>SlotImage</c> Buff1-15 + 10 Debuff1-10,
/// remplissage droite→gauche) épinglé au bout droit de la barre
/// ULTIMATE top-centre (position ref : rangée d'icônes alignée à la
/// fin de la barre — le rect sauvegardé du layout est ancré hors écran).
///
/// Données : <see cref="EntityBuffs"/> pour le handle self (charId —
/// le « handle » joueur du wire), rempli par <see cref="BuffBridge"/>
/// depuis les NCIDs 299/300/301/315/445/446.
///
/// Remplissage : buffs (S_Enchant col7 bit0 — Data.Tables.EnchantTable.IsBuff)
/// en slots 1-10 (deux rangées natives), debuffs en slots 11-15
/// (liseré rouge ajouté — la native distingue par la couleur du
/// cadre). Chaque icône : texture <c>UI/skillicon/&lt;code&gt;.png</c>
/// (repli itemicon/uiicon), Text = durée « m:ss » ou « ×N » du stack,
/// tooltip = nom EN + description + reste + palier + effect id.
///
/// Clic droit sur un buff abandonnable (col8 bit 0x10 non posé) →
/// délégué <see cref="CancelSender"/> (NCID 85 — réponse réelle
/// serveur : 300).
/// </summary>
public partial class BuffBar : Control
{
    /// <summary>Dernière instance construite (accès des bridges et du
    /// --hud-open). Toujours la barre de buffs — <see cref="NerfBar"/>
    /// ne s'y enregistre pas (elle suit via <see cref="_bars"/>).</summary>
    public static BuffBar? Instance { get; private set; }

    /// <summary>Toutes les barres montées (buffs + nerfs) — le handle
    /// « self » et le refresh se propagent à l'ensemble.</summary>
    private static readonly List<BuffBar> _bars = new();

    /// <summary>true → barre de nerfs (debuffs seuls) à gauche de la
    /// BuffBar — <see cref="NerfBar"/>.</summary>
    protected virtual bool DebuffsOnly => false;

    private const int SlotCount = 25;
    private const int BuffSlotCount = 15; // Buff1-15 natifs, Debuff1-10 ensuite

    private readonly TextureRect?[] _slots = new TextureRect?[SlotCount];
    // Couche TipRect par slot : tooltip stylé B5 + clic droit (les
    // SlotImage convertis en TextureRect simple n'ont que le tooltip
    // Godot par défaut).
    private readonly TipRect?[] _tips = new TipRect?[SlotCount];
    private readonly Label?[] _labels = new Label?[SlotCount];
    private readonly Control?[] _marks = new Control?[SlotCount];
    private readonly int[] _slotEffect = new int[SlotCount];

    private Control? _window;
    private int _selfHandle;
    private double _textTimer;

    /// <summary>Envoi d'un abandon d'enchant (NCID 85) — branché par
    /// <see cref="BuffBridge"/> quand une session zone est liée.</summary>
    public Func<short, Task>? CancelSender;

    public virtual void Build(HudRoot hud)
    {
        if (DebuffsOnly)
            BuildNerfWindow(hud);
        else
            BuildBuffWindow(hud);
        if (!DebuffsOnly)
            Instance = this;
        _bars.Add(this);
        EntityBuffs.Changed += Refresh;
        Gridify();
        SetupMove();
        Refresh();
    }

    /// <summary>A4.3 : regroupe les 25 cases (unités reparentées) dans
    /// UNE grille de cellules fixes <see cref="CellPx"/> ancrée à
    /// droite (grandit vers la gauche, remplissage gauche→droite : le premier
    /// status est à GAUCHE) ; redimensionner = changer <see cref="_cols"/>.</summary>
    private void Gridify()
    {
        if (_window == null) return;
        var max = DebuffsOnly ? 10 : BuffSlotCount;
        _cols = Math.Clamp((int)OptF($"hud.{Key}.cols", DebuffsOnly ? 5 : 8),
            1, max);
        _rowsCap = Math.Clamp((int)OptF($"hud.{Key}.rows", MaxRows), 1, MaxRows);
        var grid = new GridContainer
        {
            Name = "__cellgrid__",
            Columns = _cols,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        grid.AddThemeConstantOverride("h_separation", (int)CellSep);
        grid.AddThemeConstantOverride("v_separation", (int)CellSep);
        for (var i = 0; i < SlotCount; i++)
        {
            if (_slots[i] is not { } slot) continue;
            var unit = _units[i] ?? slot;
            var cell = new Control
            {
                Name = $"__cell{i}",
                CustomMinimumSize = new Vector2(CellPx, CellPx),
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = slot.Visible,
            };
            grid.AddChild(cell);
            var oldParent = unit.GetParent();
            if (oldParent != null) unit.Reparent(cell, false);
            else cell.AddChild(unit);
            unit.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            unit.CustomMinimumSize = Vector2.Zero;
            if (unit is TextureRect tr)
                tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            var c = cell;
            slot.VisibilityChanged += () => c.Visible = slot.Visible;
        }
        // Anciennes grilles de repli (vides) retirées.
        foreach (var n in _window.GetChildren())
            if (n is GridContainer g && g != grid && g.GetChildCount() == 0)
                g.QueueFree();
        // Grille dans un ScrollContainer (scroll interne si > MaxRows
        // rangées) sous une bande d'entête de drag (HudLayout).
        grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer
        {
            Name = "__scroll__",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = MouseFilterEnum.Pass,
        };
        scroll.AddChild(grid);
        _window.AddChild(scroll);
        scroll.SetAnchorsPreset(LayoutPreset.TopRight);
        scroll.GrowHorizontal = GrowDirection.Begin;
        scroll.OffsetTop = HeaderH;
        var header = new Control
        {
            Name = "__header__",
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 5,
        };
        header.SetAnchorsPreset(LayoutPreset.TopWide);
        header.OffsetBottom = HeaderH;
        header.Draw += () =>
        {
            header.DrawRect(new Rect2(Vector2.Zero, header.Size),
                new Color(0.05f, 0.05f, 0.08f, 0.55f));
            var cx = header.Size.X * 0.5f;
            for (var k = -1; k <= 1; k++)
                header.DrawCircle(new Vector2(cx + k * 6, HeaderH * 0.5f),
                    1.4f, new Color(1f, 0.85f, 0.35f, 0.85f));
        };
        header.Visible = false;   // visible seulement au survol du HUD
        _header = header;
        _window.AddChild(header);
        _scroll = scroll;
        _cellGrid = grid;
        Relayout();
    }

    /// <summary>Hauteur (px) de la bande d'entête de drag.</summary>
    private const float HeaderH = 12f;
    /// <summary>Rangées visibles max avant scroll interne.</summary>
    private const int MaxRows = 6;
    /// <summary>Rangées visibles choisies par la poignée (axe vertical).</summary>
    private int _rowsCap = MaxRows;
    private ScrollContainer? _scroll;
    private Control? _header;
    private bool _hover;

    /// <summary>Dimensionne fenêtre + scroll d'après le nombre de
    /// colonnes et de cases affichées ; cache la fenêtre si vide.
    /// Le bord droit reste fixe (ancrage droit).</summary>
    private void Relayout()
    {
        if (_window == null || _scroll == null || _cellGrid == null) return;
        var n = 0;
        for (var i = 0; i < SlotCount; i++)
            if (_slots[i] is { Visible: true }) n++;
        _window.Visible = n > 0;
        var rows = Math.Max(1, (n + _cols - 1) / _cols);
        var vis = Math.Min(rows, _rowsCap);
        var pitch = CellPx + CellSep;
        var overflow = rows > _rowsCap;
        var sbw = overflow ? _scroll.GetVScrollBar().GetCombinedMinimumSize().X : 0f;
        var w = _cols * pitch - CellSep + sbw;
        var h = vis * pitch - CellSep;
        _scroll.CustomMinimumSize = new Vector2(w, h);
        _scroll.OffsetLeft = -w; _scroll.OffsetRight = 0;
        _scroll.OffsetTop = HeaderH; _scroll.OffsetBottom = HeaderH + h;
        _window.OffsetLeft = _window.OffsetRight - w;
        _window.OffsetBottom = _window.OffsetTop + HeaderH + h;
        PlaceGrip();
    }

    // ------------------------------------------------------------
    // 3.2.5 — déplacement (glisser une icône), redimensionnement
    // (poignée du coin bas-droit), timer agrandi. Persisté dans
    // clientSettings.json (options hud.<buff|nerf>.{dx,dy,s}).

    private string Key => DebuffsOnly ? "nerf" : "buff";
    private Control? _grip;
    private int _mode;           // 0 rien, 2 redim.
    private bool _moved;
    private Vector2 _press;
    // A4.3 : le resize ne change plus l'échelle mais le nombre de
    // colonnes de la grille de cases (taille de case fixe CellPx).
    private const float CellPx = 34f;
    private const float CellSep = 2f;
    private readonly Control?[] _units = new Control?[SlotCount];
    private GridContainer? _cellGrid;
    private int _cols;

    private static float OptF(string k, float d) =>
        ClientUiStore.Current.Options.TryGetValue(k, out var v) &&
        float.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? f : d;

    private void SetupMove()
    {
        if (_window == null) return;
        // Déplacement = entête (HudLayout, clés BuffBar/NerfBar) ;
        // ici seulement la poignée de redimensionnement (colonnes).
        _grip = new Control
        {
            Name = "__grip__",
            Size = new Vector2(14, 14),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Fdiagsize,
            Visible = false,
            ZIndex = 10,
            TooltipText = UiTextEn.Translate("Drag to resize"),
        };
        _grip.Draw += () =>
        {
            _grip.DrawColoredPolygon(new[]
            {
                new Vector2(0, 0), new Vector2(14, 14), new Vector2(0, 14),
            }, new Color(1f, 0.85f, 0.35f, 0.9f));
            _grip.DrawPolyline(new[]
            {
                new Vector2(0, 0), new Vector2(14, 14), new Vector2(0, 14),
                new Vector2(0, 0),
            }, new Color(0, 0, 0, 0.9f), 1f);
        };
        _window.AddChild(_grip);
    }

    private void SaveXform()
    {
        var o = ClientUiStore.Current.Options;
        o[$"hud.{Key}.cols"] = _cols.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        o[$"hud.{Key}.rows"] = _rowsCap.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        ClientUiStore.Save();
        // Le redim. déplace le bord gauche (ancrage droit) : on
        // ré-enregistre la position d'entête.
        if (_window != null) HudLayout.Persist(_window);
    }

    private void PlaceGrip()
    {
        if (_grip == null || _window == null) return;
        _grip.Visible = _window.Visible && (_hover || _mode == 2);
        // Coin bas-GAUCHE (grille ancrée à droite, grandit vers la gauche).
        _grip.Position = new Vector2(-5,
            _window.OffsetBottom - _window.OffsetTop - 9);
    }

    public override void _Input(InputEvent e)
    {
        if (_window == null || !IsVisibleInTree()) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed && _mode == 0)
            {
                var onGrip = _grip != null && _grip.Visible &&
                    _grip.GetGlobalTransform() *
                        new Rect2(Vector2.Zero, _grip.Size) is var gr &&
                    gr.Grow(3).HasPoint(mb.GlobalPosition);
                if (onGrip)
                {
                    _mode = 2;
                    _press = mb.GlobalPosition;
                    _moved = false;
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (!mb.Pressed && _mode != 0)
            {
                if (_moved) SaveXform();
                _mode = 0;
                GetViewport().SetInputAsHandled();
            }
        }
        else if (e is InputEventMouseMotion mm && _mode == 2)
        {
            var d = mm.GlobalPosition - _press;
            if (!_moved && d.Length() < 4f) return;
            _moved = true;
            if (_cellGrid != null)
            {
                var right = _window.GetGlobalRect().End.X;
                var max = DebuffsOnly ? 10 : BuffSlotCount;
                _cols = Ui.GridResizeMath.Cols(right, mm.GlobalPosition.X,
                    CellPx, CellSep, max);
                _rowsCap = Ui.GridResizeMath.Rows(
                    _window.GetGlobalRect().Position.Y + HeaderH,
                    mm.GlobalPosition.Y, CellPx, CellSep, MaxRows);
                _cellGrid.Columns = _cols;
                Relayout();
            }
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Fenêtre de buffs : layout BuffWnd natif
    /// (Buff1-15 + Debuff1-10 — les debuffs natifs restent présents
    /// comme chez EE) ou grille de repli.</summary>
    private void BuildBuffWindow(HudRoot hud)
    {
        _window = hud.Build("BuffWnd.layout");
        if (_window == null)
        {
            BuildFallback();
            return;
        }
        AddChild(_window);
        // Le rect sauvegardé ({1.6,…}) sort l'écran : re-épinglé
        // au bout droit de la barre ULTIMATE (centre+140), rangée
        // d'icônes top-centre comme chez EE. Le layout remplit
        // Buff1→Buff15 de droite à gauche — le bord droit de la
        // fenêtre reste ~100 px après le bout de la barre.
        // hud-interfaces.md + shot_0045 : icônes juste à gauche de la
        // minimap (bord gauche radar à W-248), haut y≈68 ; Buff1 natif
        // est la case la plus à droite (remplissage droite→gauche).
        HudRoot.Pin(_window, 1f, 0f, 1f, 0f,
            -254 - 172, 58, -254, 58 + 190);
        var back = TopLeftVitals.Find<Control>(_window, "Background");
        if (back != null) back.Visible = false;
        for (var i = 0; i < SlotCount; i++)
        {
            // Slots natifs : Buff1-15 (buffs) + Debuff1-10 (debuffs).
            // Noms CEGUI aplatis (« Root/BuffWnd/Buff1/Text » →
            // « Root_BuffWnd_Buff1_Text ») ; « *Buff{i} » en
            // fin de chaîne ne matche pas Buff10-15.
            var leaf = i < BuffSlotCount
                ? $"Buff{i + 1}"
                : $"Debuff{i - BuffSlotCount + 1}";
            var slot = TopLeftVitals.Find<TextureRect>(
                _window, leaf);
            _units[i] = TopLeftVitals.Find<Control>(_window, leaf);
            if (slot == null)
            {
                // SlotImage mappé autrement (Control/Panel) — la
                // texture ira sur un enfant TextureRect posé ici.
                var host = TopLeftVitals.Find<Control>(
                    _window, leaf);
                if (host != null)
                {
                    slot = new TipRect
                    {
                        ExpandMode = TextureRect
                            .ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect
                            .StretchModeEnum.KeepAspectCentered,
                    };
                    slot.SetAnchorsAndOffsetsPreset(
                        LayoutPreset.FullRect);
                    host.AddChild(slot);
                }
            }
            if (slot != null)
                AttachSlot(i, slot,
                    TopLeftVitals.Find<Label>(_window, $"{leaf}_Text"));
        }
    }

    /// <summary>Branche un slot sur le remplissage (clic droit,
    /// liseré debuff, index des icônes/textes).</summary>
    private void AttachSlot(int i, TextureRect slot, Label? label)
    {
        slot.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        slot.Visible = false;
        var idx = i;
        // TipRect pleine case au-dessus de l'icône : porte le tooltip
        // B5 (titre blanc / desc rouge) et le clic droit, quel que
        // soit le type du slot converti.
        var tip = new TipRect { MouseFilter = MouseFilterEnum.Stop };
        tip.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        tip.GuiInput += e => OnSlotInput(idx, e);
        slot.AddChild(tip);
        _tips[i] = tip;
        _slots[i] = slot;
        _labels[i] = label;
        if (label != null)
        {
            // Ref : stack/durée en petits chiffres blancs bas-gauche.
            // 3.2.5 : temps restant plus gros et lisible (contour noir).
            EeSkin.Shadowed(label, 15);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            label.AddThemeColorOverride("font_color",
                new Color(1f, 1f, 0.8f));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            label.MouseFilter = MouseFilterEnum.Ignore;
        }
        // Liseré debuff (cadre natif bleu → rouge).
        var mark = new ReferenceRect
        {
            BorderColor = new Color(0.9f, 0.15f, 0.15f),
            BorderWidth = 1.4f,
            EditorOnly = false,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        mark.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        slot.AddChild(mark);
        _marks[i] = mark;
    }

    /// <summary>NerfBar : fenêtre compacte juste à gauche de la
    /// BuffBar, peuplée des 10 slots Debuff natifs reparentés
    /// (mêmes frames/timer/tooltip ; grille de repli sans uidata).</summary>
    private void BuildNerfWindow(HudRoot hud)
    {
        var host = new Control { Name = "NerfWnd" };
        // Icônes plaquées au bord droit de la fenêtre — juste à gauche
        // de la BuffBar comme chez EE (remplissage vers la gauche en
        // ajoutant des debuffs).
        var grid = new GridContainer { Columns = 5 };
        grid.SetAnchorsPreset(LayoutPreset.TopRight);
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        var src = hud.Build("BuffWnd.layout");
        for (var i = 0; i < 10; i++)
        {
            var leaf = $"Debuff{i + 1}";
            var slotHost = src != null
                ? TopLeftVitals.Find<Control>(src, leaf) : null;
            if (slotHost != null)
            {
                slotHost.Reparent(grid);
                slotHost.CustomMinimumSize = new Vector2(34, 34);
                var slot = slotHost as TextureRect;
                if (slot == null)
                {
                    slot = new TipRect
                    {
                        ExpandMode = TextureRect
                            .ExpandModeEnum.IgnoreSize,
                    };
                    slot.SetAnchorsAndOffsetsPreset(
                        LayoutPreset.FullRect);
                    slotHost.AddChild(slot);
                }
                _units[i] = slotHost;
                AttachSlot(i, slot,
                    TopLeftVitals.Find<Label>(slotHost, $"{leaf}_Text"));
            }
            else
            {
                var slot = new TipRect
                {
                    CustomMinimumSize = new Vector2(34, 34),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                };
                var lab = new Label
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                lab.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                lab.AddThemeFontSizeOverride("font_size", 8);
                slot.AddChild(lab);
                grid.AddChild(slot);
                AttachSlot(i, slot, lab);
            }
        }
        src?.QueueFree();
        host.AddChild(grid);
        // Juste à gauche de la BuffBar (x 72..244, y 40).
        // Juste à gauche de la BuffBar (W-426..W-254).
        HudRoot.Pin(host, 1f, 0f, 1f, 0f, -432 - 178, 58, -432, 58 + 72);
        AddChild(host);
        _window = host;
    }

    public override void _ExitTree()
    {
        _bars.Remove(this);
        EntityBuffs.Changed -= Refresh;
        base._ExitTree();
    }

    /// <summary>Handle « self » alimenté (charId du perso choisi ;
    /// négatif = démo hors session) — propagé à toutes les barres
    /// (BuffBar + NerfBar).</summary>
    public void SetSelfHandle(int handle)
    {
        _selfHandle = handle;
        foreach (var b in _bars)
            b._selfHandle = handle;
        Refresh();
        foreach (var b in _bars)
            if (b != this) b.Refresh();
    }

    public override void _Process(double delta)
    {
        // Entête (déplacer) + poignée (agrandir) : visibles seulement
        // quand le curseur est dans la zone du HUD status.
        if (_window != null && _header != null)
        {
            var inside = _window.Visible && _window.GetGlobalRect()
                .Grow(4).HasPoint(GetViewport().GetMousePosition());
            if (inside != _hover)
            {
                _hover = inside;
                _header.Visible = inside;
                PlaceGrip();
            }
        }
        // Décompte des durées : le Text des icônes est rafraîchi à
        // ~4 Hz sans re-dessiner les slots.
        _textTimer += delta;
        if (_textTimer < 0.25) return;
        _textTimer = 0;
        RefreshTexts();
    }

    /// <summary>Repeint les slots depuis l'état courant
    /// (EntityBuffs.Changed).</summary>
    public virtual void Refresh()
    {
        var list = EntityBuffs.Snapshot(_selfHandle)
            .Where(b => Displayable(b.EffectId)).ToList();
        if (DebuffsOnly)
        {
            var d = list.Where(b => !IsBuff(b)).ToList();
            for (var i = 0; i < SlotCount; i++)
                Fill(i, i < d.Count ? d[i] : null, true);
            Relayout();
            return;
        }
        var buffs = list.Where(IsBuff).ToList();

        for (var i = 0; i < SlotCount; i++)
        {
            EntityBuffs.BuffInst? b = null;
            var debuff = false;
            if (i < BuffSlotCount)
            {
                if (i < buffs.Count) b = buffs[i];
            }
            // Les debuffs vont à la NerfBar (hud-interfaces §51) : les
            // cases Debuff natives de cette fenêtre restent vides.
            Fill(i, b, debuff);
        }
        Relayout();
    }

    /// <summary>Un statut n'est affiché que s'il est connu de
    /// c_enchant et porte une icône (les effets sans visuel ne sont
    /// pas des « status »).</summary>
    internal static bool Displayable(int effectId) =>
        Data.Tables.EnchantTable.Get(effectId) != null &&
        IconTexture(effectId) != null;

    /// <summary>Rafraîchit uniquement les textes durée/stack des
    /// slots visibles (tick 4 Hz de <see cref="_Process"/>).</summary>
    private void RefreshTexts()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            if (_slots[i] is not { Visible: true } slot) continue;
            var b = FindShown(i);
            if (b != null && _labels[i] != null)
                _labels[i]!.Text = SlotText(b);
            else
                Refresh(); // un slot est arrivé à 0 — reprise complète
        }
    }

    /// <summary>Retrouve l'instance affichée sur le slot
    /// <paramref name="i"/> (par effectId mémorisé au Fill).</summary>
    private EntityBuffs.BuffInst? FindShown(int i)
    {
        var eid = _slotEffect[i];
        if (eid <= 0) return null;
        return EntityBuffs.Snapshot(_selfHandle)
            .FirstOrDefault(b => b.EffectId == eid);
    }

    private void Fill(int i, EntityBuffs.BuffInst? b, bool debuff)
    {
        var slot = _slots[i];
        if (slot == null) return;
        _slotEffect[i] = b?.EffectId ?? 0;
        if (_marks[i] != null) _marks[i]!.Visible = b != null && debuff;
        if (b == null)
        {
            slot.Visible = false;
            slot.Texture = null;
            if (_tips[i] != null) _tips[i]!.TooltipText = "";
            if (_labels[i] != null) _labels[i]!.Text = "";
            return;
        }
        slot.Visible = true;
        slot.Texture = IconTexture(b.EffectId);
        if (_tips[i] != null) _tips[i]!.TooltipText = Tooltip(b);
        if (_labels[i] != null) _labels[i]!.Text = SlotText(b);
    }

    /// <summary>Texte bas-droite du slot : durée « m:ss » /
    /// « h:mm » ; « ×N » si stackable au-delà de 1.</summary>
    private static string SlotText(EntityBuffs.BuffInst b)
    {
        var stack = b.StackCount > 1 ? $"×{b.StackCount} " : "";
        var rem = b.Remaining;
        if (rem < 0) return stack.Length > 0 ? stack.TrimEnd() : "";
        var sec = (int)Math.Ceiling(rem);
        var t = sec >= 3600
            ? $"{sec / 3600}:{sec % 3600 / 60:D2}:{sec % 60:D2}"
            : $"{sec / 60}:{sec % 60:D2}";
        return stack + t;
    }

    /// <summary>Tooltip complet de l'icône : nom EN, palier, stacks,
    /// durée restante, description EN, effect id.</summary>
    private static string Tooltip(EntityBuffs.BuffInst b)
    {
        var name = Data.Tables.EnchantTable.NameOf(b.EffectId);
        var kind = IsBuff(b) ? "Buff" : "Debuff";
        var rem = b.Remaining;
        var dur = rem < 0 ? "permanent"
            : $"{(int)rem / 60}:{(int)rem % 60:D2} left";
        var s = $"{name}  [{kind}]\nLv{b.Level}" +
            (b.StackCount > 1 ? $"  ×{b.StackCount}" : "") +
            $"  ·  {dur}";
        var desc = Data.Tables.EnchantTable.ActiveTextOf(b.EffectId);
        if (desc != null) s += $"\n{desc}";
        s += $"\n#{b.EffectId}";
        var def = Data.Tables.EnchantTable.Get(b.EffectId);
        if (def is { NoAbandon: true }) s += "  (can't cancel)";
        return s;
    }

    /// <summary>Icône de l'enchant — code c_enchant col1 →
    /// UI/skillicon puis itemicon/uiicon (fichiers vérifiés).</summary>
    private static Texture2D? IconTexture(int effectId)
    {
        var code = Data.Tables.EnchantTable.Get(effectId)?.Code;
        if (code == null || code.Length == 0) return null;
        return AssetCatalog.GetTexture($"UI/skillicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/itemicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/uiicon/{code}.png");
    }

    /// <summary>Classification buff/debuff — S_Enchant col7 bit0
    /// (EnchantService natif). Inconnu → buff par défaut.</summary>
    private static bool IsBuff(EntityBuffs.BuffInst b) =>
        Data.Tables.EnchantTable.Get(b.EffectId) is not { IsBuff: false };

    /// <summary>Clic droit → abandon (NCID 85) si l'enchant n'est pas
    /// « can't-abandon » (col8 bit 0x10).</summary>
    private void OnSlotInput(int i, InputEvent e)
    {
        // La TipRect (Stop) avale la molette : on la relaie au scroll.
        if (e is InputEventMouseButton { Pressed: true } w &&
            w.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown
            && _scroll != null)
        {
            _scroll.ScrollVertical += (int)((w.ButtonIndex ==
                MouseButton.WheelUp ? -1 : 1) * (CellPx + CellSep));
            AcceptEvent();
            return;
        }
        if (e is not InputEventMouseButton { Pressed: true } mb ||
            mb.ButtonIndex != MouseButton.Right) return;
        var eid = _slotEffect[i];
        if (eid <= 0) return;
        var def = Data.Tables.EnchantTable.Get(eid);
        // Toute monture se retire au clic droit (même « can't abandon »).
        if (!Data.Tables.EnchantTable.CanCancelByRightClick(def)) return;
        var send = CancelSender?.Invoke((short)eid);
        if (send != null)
            _ = send.ContinueWith(t => GD.PrintErr(
                    $"[buff] cancel {eid} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        AcceptEvent();
    }

    /// <summary>Repli sans uidata : 25 emplacements génériques en
    /// grille au bout de la barre ULTIMATE.</summary>
    private void BuildFallback()
    {
        var host = new Panel { Name = "BuffWnd" };
        HudRoot.Pin(host, 0.5f, 0f, 0.5f, 0f, 72, 40, 244, 230);
        var grid = new GridContainer { Columns = 5 };
        for (var i = 0; i < SlotCount; i++)
        {
            var slot = new TipRect
            {
                CustomMinimumSize = new Vector2(30, 30),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum
                    .KeepAspectCentered,
                Visible = false,
            };
            var lab = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            lab.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            lab.AddThemeFontSizeOverride("font_size", 8);
            slot.AddChild(lab);
            grid.AddChild(slot);
            _slots[i] = slot;
            _labels[i] = lab;
        }
        host.AddChild(grid);
        AddChild(host);
        _window = host;
    }

    /// <summary>Données de démonstration pour --hud-open=buff hors
    /// session : deux buffs et un debuff factices poussés dans le
    /// store sous le handle démo −1.</summary>
    public void EnsureDemo()
    {
        var h = -1;
        EntityBuffs.ClearAll();
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        {
            Handle = h, EffectId = 10005, RestSeconds = 3600,
            StackCount = 1, Level = 11,
        });
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        {
            Handle = h, EffectId = 10002, RestSeconds = 1205,
            StackCount = 3, Level = 4,
        });
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        {
            // EffectId wire = u16 (court-circuité en short côté codec).
            Handle = h, EffectId = unchecked((short)51007),
            RestSeconds = 42, StackCount = 1, Level = 1,
        });
        SetSelfHandle(h);
    }

    /// <summary>Sonde <c>--enchant-test</c> (preuve U8) : pose un buff
    /// et un debuff RÉELS via <c>//self_use_effect</c> (TC natif —
    /// S→C 299), attend le remplissage BuffBar + NerfBar, injecte un
    /// vrai clic droit sur le slot buff (C→S 85 → S→C 300) et vérifie
    /// sa disparition pendant que le debuff reste. Capture finale :
    /// <c>godot-shots/u8_buffs.png</c>.</summary>
    public sealed partial class BuffProbe : Node
    {
        private readonly HudRoot _hud;
        private readonly string _shotsDir;
        private BuffBar? _bar;
        private int _frame, _step, _wait;
        private ulong _t0;
        // E0455 « Soul of the Sea King Stingray » — buff col8 bit0x10
        // NON posé → clic droit natif autorisé. E0302 — debuff
        // PERSISTANT (commandes stats 3101/2055/1004 ; les procs à
        // l'attaque comme 11163 sont instant-only et ne créent pas
        // de status → pas de 299).
        private const int BuffEid = 10052, NerfEid = 11195;

        public BuffProbe(HudRoot hud)
        {
            _hud = hud; Name = "__buff_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            if (Screens.LoadingScreen.Active != null) return;
            _frame++;
            // Les barres sont montées par HudExtensions après
            // HudRoot.BuildAll — découverte paresseuse.
            _bar ??= BuffBar.Instance;
            if (_bar == null) return;
            var self = _bar._selfHandle;
            switch (_step)
            {
                case 0 when _frame > 150 && self != 0 &&
                        _hud.SendZoneAsync != null:
                    _ = _hud.SendZoneAsync(new ZoneTextCommand
                    { Text = $"//self_use_effect {BuffEid} 600" });
                    _ = _hud.SendZoneAsync(new ZoneTextCommand
                    { Text = $"//self_use_effect {NerfEid} 25" });
                    GD.Print($"[enchant-test] self_use_effect " +
                        $"{BuffEid}+{NerfEid} → self 0x{self:x8}");
                    _step = 1;
                    break;
                case 1 when ++_wait >= 10:
                    var snap = EntityBuffs.Snapshot(self);
                    var hasB = snap.Any(b => b.EffectId == BuffEid);
                    var hasD = snap.Any(b => b.EffectId == NerfEid);
                    if (!hasB || !hasD)
                    {
                        if (_wait > 200)
                        {
                            GD.PrintErr("[enchant-test] TIMEOUT " +
                                $"buff={hasB} nerf={hasD}");
                            _step = 9;
                        }
                        break;
                    }
                    var bSlot = SlotOf(_bar, BuffEid);
                    var nerf = _bars.FirstOrDefault(b => b.DebuffsOnly);
                    var dSlot = nerf == null ? -1 : SlotOf(nerf, NerfEid);
                    GD.Print($"[enchant-test] 299 reçu — buff slot " +
                        $"{bSlot} vis={bSlot >= 0 && _bar._slots[bSlot]!.Visible}" +
                        $" ; nerf slot {dSlot} vis=" +
                        $"{nerf != null && dSlot >= 0 && nerf._slots[dSlot]!.Visible}");
                    GetViewport().GetTexture()?.GetImage()?.SavePng(
                        $"{_shotsDir}/u8_buffs.png");
                    if (bSlot < 0) { _step = 9; break; }
                    // Laisse 5 s pour la capture Flameshot réelle.
                    _wait = 0; _step = 5;
                    break;
                case 5 when ++_wait >= 40:
                    _wait = 0; _step = 6;
                    break;
                case 6:
                    var bSlot2 = SlotOf(_bar, BuffEid);
                    if (bSlot2 < 0) { _step = 9; break; }
                    var c0 = _bar._slots[bSlot2]!.GetGlobalRect().GetCenter();
                    // Clic droit réel au centre du slot buff →
                    // GuiInput → OnSlotInput → C→S 85.
                    var c = c0;
                    Input.ParseInputEvent(new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Right, Pressed = true,
                        Position = c, GlobalPosition = c,
                    });
                    Input.ParseInputEvent(new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Right, Pressed = false,
                        Position = c, GlobalPosition = c,
                    });
                    GD.Print("[enchant-test] clic droit buff injecté");
                    _wait = 0; _step = 2;
                    break;
                case 2 when ++_wait >= 10:
                    var snap2 = EntityBuffs.Snapshot(self);
                    if (snap2.Any(b => b.EffectId == BuffEid))
                    {
                        if (_wait > 200)
                        {
                            GD.PrintErr("[enchant-test] TIMEOUT — " +
                                "le buff survit au clic droit");
                            _step = 9;
                        }
                        break;
                    }
                    GD.Print("[enchant-test] buff retiré (300) ; " +
                        $"debuff restant=" +
                        $"{snap2.Any(b => b.EffectId == NerfEid)}");
                    GetViewport().GetTexture()?.GetImage()?.SavePng(
                        $"{_shotsDir}/u8_after.png");
                    _wait = 0; _step = 3;
                    break;
                case 3:
                    // Expiration du debuff (25 s serveur) → 300 → retiré.
                    if (_t0 == 0) _t0 = Time.GetTicksMsec();
                    ++_wait;
                    if (Time.GetTicksMsec() - _t0 > 60000) { GD.PrintErr("[enchant-test] TIMEOUT expiration nerf"); _step = 9; break; }
                    if (EntityBuffs.Snapshot(self).Any(b => b.EffectId == NerfEid)) break;
                    GD.Print($"[enchant-test] nerf expiré, retiré après {(Time.GetTicksMsec() - _t0) / 1000.0:F1}s (client)");
                    GD.Print("[enchant-test] terminé");
                    _step = 9;
                    break;
            }
        }

        /// <summary>Index du slot affichant cet effet (−1).</summary>
        private static int SlotOf(BuffBar bar, int eid)
        {
            for (var i = 0; i < SlotCount; i++)
                if (bar._slotEffect[i] == eid) return i;
            return -1;
        }
    }
}

/// <summary>B5 : icône à tooltip stylé — titre (1re ligne) blanc,
/// description en rouge, comme shot_0045.</summary>
public partial class TipRect : TextureRect
{
    public override GodotObject _MakeCustomTooltip(string forText)
    {
        var parts = forText.Split('\n', 2);
        var esc = (string t) => t.Replace("[", "[lb]");
        var rtl = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(180, 0),
            Text = $"[color=#ffffff]{esc(parts[0])}[/color]" +
                   (parts.Length > 1
                       ? $"\n[color=#ff4a3d]{esc(parts[1])}[/color]" : ""),
        };
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.08f, 0.94f),
            BorderColor = new Color(0.65f, 0.65f, 0.7f),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 5, ContentMarginBottom = 5,
        });
        panel.AddChild(rtl);
        return panel;
    }
}
