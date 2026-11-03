using Godot;
using EdenClient.Data;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// Drag & persistance des fenêtres HUD (C2 du master plan).
///
/// Le déplacement lui-même est assuré par <see cref="CeguiTitleBar"/>
/// (barre générée automatiquement pour les FrameWindow CEGUI par
/// <see cref="CeguiToGodot"/>) — cet helper ajoute :
///
///   - <see cref="EnsureTitleBar"/> : une barre de titre draggable
///     (+ label + bouton ×) pour les layouts racine
///     <c>DefaultWindow</c> dépourvus de TitlebarEnabled (ex.
///     <c>NewSkillList</c>, <c>MissionLog</c>) ou pour les fenêtres
///     de repli construites en code.
///   - <see cref="Persist"/> : sauvegarde de la position dans
///     <c>user://clientSettings.json</c> (section windows, clé de
///     fenêtre) <b>au relâchement du drag</b>, restaurée à
///     l'attachement (clampée dans le viewport — un fichier de
///     config issu d'une autre résolution ne doit jamais poser la
///     fenêtre hors écran).
/// </summary>
public static partial class DragHelper
{
    /// <summary>Fichier de layout utilisateur (portable —
    /// <c>user://</c> suit le profil Godot, pas l'install).</summary>

    /// <summary>Ajoute une barre de titre draggable à
    /// <paramref name="window"/> si elle n'en possède pas déjà une
    /// (descendant <see cref="CeguiTitleBar"/> ou nommé
    /// « __auto_titlebar__ »). Retourne la barre (existante ou
    /// créée) — le × masque la fenêtre. Installe aussi le drag « par
    /// n'importe quel point de fond » (<see cref="HudWindowDrag"/>)
    /// et, si le HUD est trouvé, skinne la fenêtre + sa barre avec le
    /// chrome natif DejaVu (frame 9-patch, titlebar, bouton ×).</summary>
    public static CeguiTitleBar EnsureTitleBar(Control window,
        string title, int fontSize = 12)
    {
        title = UiTextEn.Translate(title);
        // Drag par le fond — indépendant de la barre (filet pour les
        // fenêtres dont la barre serait recouverte par du contenu).
        // Pas sur une racine plein écran : la déplacer décalerait
        // tout le panneau.
        if (!IsFullRect(window))
        {
            EnableBackgroundDrag(window);
            // Registre central : premier plan au clic, Échap, croix.
            HudWindows.Register(window);
        }
        var hud = HudExtensions.FindHud();
        if (hud?.Ctx != null && hud.Tex != null &&
            !IsFullRect(window))
            ApplyNativeChrome(window, hud);
        // Listes natives : parchemin à texte sombre (même rendu partout).
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(window)) EeSkin.SkinLists(window);
        }).CallDeferred();
        // Godot's `FindChildren(..., typeName)` resolves native class
        // names, not arbitrary C# script types. Match by runtime type
        // to reuse the generated layout bar instead of stacking a
        // second auto-titlebar on the same rect.
        var found = window.FindChildren("*", "", true, false)
            .OfType<CeguiTitleBar>().FirstOrDefault();
        if (found != null)
        {
            // Barre déjà générée par CeguiToGodot : on rafraîchit
            // juste le libellé si l'appelant en fournit un, et on la
            // garde au sommet du z-order (recouverte sinon par un
            // contenu injecté après coup — cf. ClassWindow).
            if (found.GetIndex() < window.GetChildCount() - 1)
                window.MoveChild(found, window.GetChildCount() - 1);
            // Défensif : les Labels de la barre doivent être
            // MouseFilter.Ignore sinon ils avalent le clic du drag.
            foreach (var lc in found
                         .FindChildren("*", "Label", true, false)
                         .Cast<Label>())
                lc.MouseFilter = Control.MouseFilterEnum.Ignore;
            var l = found
                .FindChildren("*", "Label", true, false)
                .Cast<Label>().FirstOrDefault();
            if (l != null)
            {
                if (title.Length > 0) l.Text = title;
                CenterTitle(l);
            }
            return found;
        }

        // Barre créée : skin native DejaVu/Titlebar si le contexte
        // CEGUI est disponible, sinon bande sombre de repli.
        var bar = new CeguiTitleBar { Name = "__auto_titlebar__" };
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        var builder = hud?.Ctx != null ? new CeguiBuilder(hud.Ctx) : null;
        int tbh = 0;
        uint caption = 0xD8FFFFFF;
        var tbs = builder?.TitlebarStyle(out tbh, out caption);
        var skinned = tbs != null && hud?.Tex != null
            ? CeguiToGodot.MakeStyleBox(tbs, hud.Tex) : null;
        bar.CustomMinimumSize = new Vector2(0,
            skinned != null && tbh > 0 ? tbh : fontSize + 10);
        bar.AddThemeStyleboxOverride("panel", skinned ?? new StyleBoxFlat
        {
            BgColor = new Color(0.236f, 0.227f, 0.202f, 0.9f),
        });
        var label = new Label
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = 8;
        label.OffsetRight = -26;
        CenterTitle(label);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color",
            skinned != null
                ? CeguiToGodot.GodotColor(caption)
                : new Color(0.95f, 0.95f, 0.95f));
        bar.AddChild(label);
        if (hud?.Tex != null && builder?.CloseButtonImages() is { } imgs)
        {
            var close = new TextureButton
            {
                Name = "__auto_closebtn__",
                TextureNormal = hud.Tex.GetRegion(imgs.Normal),
                TextureHover = hud.Tex.GetRegion(imgs.Hover),
                TexturePressed = hud.Tex.GetRegion(imgs.Pushed),
                StretchMode =
                    TextureButton.StretchModeEnum.KeepAspectCentered,
            };
            close.SetAnchorsAndOffsetsPreset(
                Control.LayoutPreset.CenterRight);
            // Zone de clic 24×24 (l'image native fait 12 px) : la
            // croix doit se toucher du premier coup.
            close.OffsetRight = -2;
            close.OffsetLeft = -26;
            close.OffsetTop = -12;
            close.OffsetBottom = 12;
            close.CustomMinimumSize = new Vector2(24, 24);
            close.Pressed += () => HudWindows.Close(window);
            bar.AddChild(close);
        }
        else
        {
            var close = new Button { Text = "×",
                Name = "__auto_closebtn__" };
            close.SetAnchorsAndOffsetsPreset(
                Control.LayoutPreset.CenterRight);
            close.CustomMinimumSize = new Vector2(24, 0);
            close.Pressed += () => HudWindows.Close(window);
            bar.AddChild(close);
        }
        // AddChild = dernier enfant → dessinée et hit-testée AU-DESSUS
        // du fond de la fenêtre (sinon le Background plein cadre des
        // DefaultWindow masque la barre et le drag est mort).
        window.AddChild(bar);
        return bar;
    }

    /// <summary>Titre de fenêtre centré sur la bande violette (la barre
    /// native a une marge basse : le texte était calé en bas à gauche).</summary>
    private static void CenterTitle(Label l)
    {
        l.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        l.OffsetLeft = 28;
        l.OffsetRight = -28;
        l.OffsetTop = -4;
        l.OffsetBottom = -4;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.ClipText = true;
    }

    /// <summary>Applique le chrome natif DejaVu/FrameWindow (frame
    /// 9-patch + client) à une fenêtre construite en code — ignoré
    /// si la fenêtre porte déjà une frame composite de layout.</summary>
    private static void ApplyNativeChrome(Control window, HudRoot hud)
    {
        if (hud.Tex != null && hud.Ctx != null)
            ApplyNativeChrome(window, hud.Ctx, hud.Tex);
    }

    /// <summary>Skinne <paramref name="window"/> avec le cadre natif
    /// DejaVu/FrameWindow (nine-patch du looknfeel — thème courant
    /// via <paramref name="tex"/>). Également utilisable hors HUD
    /// (écrans login/channel — <c>ScreenManager.Ui/Tex</c>).</summary>
    public static void ApplyNativeChrome(Control window,
        CeguiContext ctx, UiTextureCache tex)
    {
        // Les fenêtres issues d'un layout FrameWindow ont déjà leur
        // chrome (meta posée par CeguiToGodot).
        if (window.HasMeta("cegui_target") &&
            window.GetMeta("cegui_target").AsString()
                == "CEGUI/FrameWindow")
            return;
        var builder = new CeguiBuilder(ctx);
        var st = builder.WindowFrameStyle("DejaVu/FrameWindow",
            withTitle: true, withFrame: true);
        if (st == null) return;
        var sb = CeguiToGodot.MakeStyleBox(st, tex);
        if (sb == null) return;
        window.AddThemeStyleboxOverride("panel", sb);
    }

    /// <summary>Fond opaque de la client-area d'une fenêtre chromée —
    /// le centre du nine-patch FrameWindow est translucide : les
    /// écrans hand-built (login, channel) posent un fond sombre
    /// inscrit dans les marges du stylebox derrière leur contenu.</summary>
    public static void FillClientArea(Control window, Color color)
    {
        if (window.GetThemeStylebox("panel") is not StyleBox sb)
            return;
        var fill = new ColorRect
        {
            Name = "__client_fill",
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fill.OffsetLeft = sb.ContentMarginLeft;
        fill.OffsetTop = sb.ContentMarginTop;
        fill.OffsetRight = -sb.ContentMarginRight;
        fill.OffsetBottom = -sb.ContentMarginBottom;
        window.AddChild(fill);
        window.MoveChild(fill, 0);
    }

    /// <summary>Installe <see cref="HudWindowDrag"/> si absent.</summary>
    public static void EnableBackgroundDrag(Control window)
    {
        if (window.GetNodeOrNull("__hud_drag__") != null)
            return;
        window.AddChild(new HudWindowDrag(window));
    }

    /// <summary>true si <paramref name="c"/> est ancré plein écran
    /// (racine de panneau montée par HudRoot.Mount) — ni skinné ni
    /// draggé : ce n'est pas la fenêtre mais son conteneur.</summary>
    private static bool IsFullRect(Control c) =>
        c.AnchorLeft == 0 && c.AnchorTop == 0 &&
        c.AnchorRight == 1 && c.AnchorBottom == 1;

    /// <summary>
    /// Drag « par n'importe quel point de fond » : LMB pressé sur un
    /// descendant non interactif de la fenêtre (fond, cadre, label,
    /// titre, image) la déplace. Les contrôles interactifs
    /// (boutons, champs, listes, sliders, scrolls) restent exclus ;
    /// un drag-and-drop Godot (item, skill…) annule le déplacement.
    /// Complète — ne remplace pas — le drag de
    /// <see cref="CeguiTitleBar"/> (même math, résultat idempotent).
    /// </summary>
    private sealed partial class HudWindowDrag : Node
    {
        private static readonly bool DragDiag =
            System.Environment.GetEnvironmentVariable("EDEN_DRAG_DIAG") ==
            "1";
        private readonly Control _win;
        private bool _armed, _dragging;
        private Vector2 _pressPos, _grab;

        public HudWindowDrag(Control win)
        {
            _win = win;
            Name = "__hud_drag__";
        }

        /// <summary>true si <paramref name="c"/> est un point
        /// « décoratif » (fond/cadre/label/image) — pas un contrôle
        /// qui consomme le clic ou initie un drag propre.</summary>
        private static bool IsStaticChrome(Control c) => c switch
        {
            BaseButton or LineEdit or TextEdit or RichTextLabel
                or ItemList or Tree or Godot.Range or TabContainer
                or ScrollContainer or MenuBar
                or SubViewportContainer => false,
            _ => true,
        };

        /// <summary>Hauteur de la bande de titre pour les fenêtres
        /// sans <see cref="CeguiTitleBar"/>.</summary>
        private const float TitleBandPx = 26f;

        /// <summary>true si le clic tombe sur la barre de titre de la
        /// fenêtre (CeguiTitleBar ou bande haute de la fenêtre).</summary>
        private bool InTitleBar(Control hov, Vector2 global)
        {
            for (Node? n = hov; n != null && n != _win; n = n.GetParent())
                if (n is CeguiTitleBar) return true;
            return global.Y - _win.GlobalPosition.Y < TitleBandPx;
        }

        public override void _Input(InputEvent e)
        {
            if (!GodotObject.IsInstanceValid(_win) ||
                !_win.IsInsideTree())
            {
                QueueFree();
                return;
            }
            if (!_win.IsVisibleInTree())
            {
                _armed = _dragging = false;
                return;
            }
            if (e is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    var hov = _win.GetViewport()?.GuiGetHoveredControl();
                    // Drag par la barre de titre uniquement (plan v4
                    // H3) : un clic dans le contenu (libellé, case,
                    // fond de liste) ne déplace plus la fenêtre.
                    _armed = hov != null &&
                        (_win == hov || _win.IsAncestorOf(hov)) &&
                        IsStaticChrome(hov) &&
                        InTitleBar(hov, mb.GlobalPosition);
                    if (DragDiag)
                        GD.Print($"[drag-diag] background press " +
                            $"win={_win.GetPath()} hov={hov?.GetPath()} " +
                            $"armed={_armed} mouse={mb.GlobalPosition}");
                    if (_armed)
                    {
                        _pressPos = mb.GlobalPosition;
                        _grab = mb.GlobalPosition - _win.GlobalPosition;
                        _win.MoveToFront();
                    }
                }
                else
                {
                    _armed = _dragging = false;
                }
            }
            else if (_armed && e is InputEventMouseMotion mm)
            {
                // Un drag Godot (item d'inventaire…) a commencé → on
                // laisse faire, la fenêtre ne bouge pas.
                if (_win.GetViewport()?.GuiIsDragging() == true)
                {
                    _armed = _dragging = false;
                    return;
                }
                if (!_dragging)
                {
                    // Seuil anti micro-glissements (clic simple,
                    // double-clic, début de drag item).
                    if (mm.GlobalPosition.DistanceTo(_pressPos) < 6f)
                        return;
                    _dragging = true;
                    if (DragDiag)
                        GD.Print($"[drag-diag] background start " +
                            $"{_win.GetPath()} pos0={_win.GlobalPosition}");
                }
                var pos = mm.GlobalPosition - _grab;
                var vp = _win.GetViewport()?.GetVisibleRect().Size
                         ?? new Vector2(1280, 720);
                var size = _win.Size;
                pos.X = Mathf.Clamp(pos.X, -size.X + 48f, vp.X - 48f);
                pos.Y = Mathf.Clamp(pos.Y, 0f, vp.Y - 24f);
                _win.GlobalPosition = pos;
                if (DragDiag)
                    GD.Print($"[drag-diag] background move " +
                        $"pos={_win.GlobalPosition} mouse={mm.GlobalPosition}");
            }
        }
    }

    /// <summary>Rend la position de <paramref name="window"/>
    /// persistante sous la clé <paramref name="key"/> : restore au
    /// premier frame (la géométrie d'ancres n'est valable qu'une fois
    /// l'arbre posé), sauvegarde à chaque fin de drag et à la sortie.
    /// Retourne le watcher créé (tests/debug).</summary>
    public static Node Persist(Control window, string key)
    {
        var w = new PositionWatcher(window, key);
        window.AddChild(w);
        return w;
    }

    /// <summary>Watcher interne : un par fenêtre persistée. Détecte
    /// le changement de <see cref="Control.Position"/> produit par le
    /// drag de la barre de titre, puis écrit le fichier quand le
    /// bouton gauche est relâché (fin de drag réelle — pas à chaque
    /// pixel).</summary>
    private static readonly List<PositionWatcher> _watchers = new();

    /// <summary>« Reset UI » (menu système) : efface clientSettings.json (positions) et
    /// remet chaque fenêtre persistée à sa position par défaut.</summary>
    public static void ResetAllPositions()
    {
        ClientUiStore.Current.ResetPositions();
        ClientUiStore.Save();
        foreach (var w in _watchers.ToList()) w.ResetToDefault();
        HudLayout.ResetAll();
    }

    private sealed partial class PositionWatcher : Node
    {
        private readonly Control _win;
        private readonly string _key;
        private Vector2 _default;
        private Vector2 _last;
        private bool _dirty;
        private bool _restored;

        public PositionWatcher(Control win, string key)
        {
            _win = win;
            _key = key;
            Name = $"__dragpersist_{key}";
        }

        public override void _Process(double delta)
        {
            if (!GodotObject.IsInstanceValid(_win))
            {
                QueueFree();
                return;
            }
            // Restore différé : Position n'est significative qu'après
            // la première passe d'ancres de la fenêtre.
            if (!_restored)
            {
                _restored = true;
                _default = _win.Position;
                _last = _win.Position;
                if (LoadPosition(_key) is { } saved)
                {
                    // Restauration stricte : fenêtre entière à l'écran
                    // quand elle tient dans le viewport — une position
                    // d'une autre résolution (-612 px → liseré de 48 px
                    // hors champ) ne doit jamais être restaurée telle
                    // quelle.
                    _win.Position = ClampFullyVisible(_win, saved);
                    _last = _win.Position;
                }
                return;
            }
            if (_win.Position != _last)
            {
                _last = _win.Position;
                _dirty = true;
            }
            // Fin de drag : la position a bougé et LMB est relâché.
            if (_dirty && !Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _dirty = false;
                SavePosition(_key, _last);
            }
        }

        public override void _EnterTree() => _watchers.Add(this);

        public void ResetToDefault()
        {
            if (!_restored || !GodotObject.IsInstanceValid(_win)) return;
            _win.Position = _default;
            _last = _default;
            _dirty = false;
        }

        public override void _ExitTree()
        {
            _watchers.Remove(this);
            // Filet de sécurité : sauvegarde un drag jamais relâché
            // (changement de scène en plein drag).
            if (_dirty)
                SavePosition(_key, _last);
        }
    }

    /// <summary>Position sauvegardée pour <paramref name="key"/>,
    /// null si absente ou fichier illisible.</summary>
    public static Vector2? LoadPosition(string key) =>
        ClientUiStore.Current.Windows.TryGetValue(key, out var p)
            ? new Vector2(p.X, p.Y) : null;

    /// <summary>Écrit la position de la fenêtre (merge — les autres
    /// clés du fichier sont préservées).</summary>
    private static void SavePosition(string key, Vector2 pos)
    {
        ClientUiStore.Current.Windows[key] =
            new Data.WindowPlacement { X = pos.X, Y = pos.Y };
        ClientUiStore.Save();
    }

    /// <summary>Repositionne <paramref name="window"/> entièrement
    /// dans le viewport si elle déborde (position par défaut ou
    /// restaurée d'une autre résolution) : décalée pour que le cadre
    /// complet reste visible tant que possible, puis clampé au
    /// minimum visible.</summary>
    public static void ClampIntoView(Control window)
    {
        var vp = window.GetViewport()?.GetVisibleRect().Size
                 ?? new Vector2(1280, 720);
        var pos = window.Position;
        var size = window.Size;
        if (size.X <= 0 || size.Y <= 0) return;
        // D'abord essayer de garder la fenêtre entière à l'écran,
        // puis le minimum « 48 px visibles + barre accessible ».
        pos.X = Mathf.Clamp(pos.X,
            Math.Min(-size.X + 48f, vp.X - size.X), vp.X - 48f);
        pos.Y = Mathf.Clamp(pos.Y,
            0f, Math.Max(vp.Y - 24f, 0f));
        if (pos.Y + size.Y > vp.Y)
            pos.Y = Math.Max(0f, vp.Y - size.Y);
        window.Position = pos;
    }

    /// <summary>Clamp de restauration : fenêtre entièrement visible si
    /// elle tient dans le viewport ; sinon le minimum « 48 px +
    /// barre accessible » habituel.</summary>
    private static Vector2 ClampFullyVisible(Control win, Vector2 pos)
    {
        var vp = win.GetViewport()?.GetVisibleRect().Size
                 ?? new Vector2(1280, 720);
        var size = win.Size;
        var x = size.X <= vp.X && size.X > 0
            ? Mathf.Clamp(pos.X, 0f, vp.X - size.X)
            : Mathf.Clamp(pos.X, -size.X + 48f, vp.X - 48f);
        var y = size.Y <= vp.Y && size.Y > 0
            ? Mathf.Clamp(pos.Y, 0f, vp.Y - size.Y)
            : Mathf.Clamp(pos.Y, 0f, vp.Y - 24f);
        return new Vector2(x, y);
    }

    /// <summary>Clamp : la fenêtre doit garder ≥ 48 px visibles à
    /// l'écran et sa barre de titre accessible (y ≥ 0).</summary>
    private static Vector2 ClampToViewport(Control win, Vector2 pos)
    {
        var vp = win.GetViewport()?.GetVisibleRect().Size
                 ?? new Vector2(1280, 720);
        var size = win.Size;
        var x = Mathf.Clamp(pos.X, -size.X + 48f, vp.X - 48f);
        var y = Mathf.Clamp(pos.Y, 0f, vp.Y - 24f);
        return new Vector2(x, y);
    }

    // -------------------- sonde de validation live -----------------

    /// <summary>Nœud de diagnostic <c>--drag-test=&lt;clé&gt;</c> :
    /// ouvre la fenêtre, simule un drag réel (événements injectés via
    /// <see cref="Input.ParseInputEvent"/> sur la barre de titre puis
    /// sur le fond), capture avant/après et vérifie la persistance.
    /// Sert de preuve H5 sans action manuelle.</summary>
    public sealed partial class DragProbe : Node
    {
        private readonly HudRoot _hud;
        private readonly string _key;
        private Control? _win;
        private Control? _bar;
        private Vector2 _pos0;
        private int _frame;
        private int _retries;
        private Vector2 _pressAt;
        private string? _persistKey;
        private string _shotsDir;

        public DragProbe(HudRoot hud, string key)
        {
            _hud = hud;
            _key = key;
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
            Name = "__drag_probe__";
        }

        public override void _Process(double delta)
        {
            _frame++;
            switch (_frame)
            {
                case 45: Locate(); break;
                case 60: Aim(); break;
                case 61:
                    // Le fondu du loading overlay (~1 s) mange encore
                    // le hover : ré-aimer (frame 60) tant que le point
                    // visé ne tombe pas dans la fenêtre — Godot ne
                    // recalcule le hover que sur nouvel événement.
                    if (!HoverInWindow()) { _frame = 59; break; }
                    Shot("before"); PressButton(); break;
                case 62: case 63: case 64: case 65: case 66:
                case 67: case 68: case 69: case 70: case 71:
                    Step(_frame - 61); break;
                case 73: Release(); break;
                case 90: Shot("after"); Report(); break;
                case 95: Aim(background: true); break;
                case 96:
                    if (!HoverInWindow()) { _frame = 94; break; }
                    PressButton(); break;
                case 97: case 98: case 99: case 100: case 101:
                case 102: case 103: case 104: case 105: case 106:
                    Step(_frame - 96); break;
                case 108: Release(); break;
                case 125: Shot("after_bg"); Report(final: true); break;
            }
        }

        private static void Inject(InputEvent e) =>
            Input.ParseInputEvent(e);

        private void Locate()
        {
            // key → nœud racine du panneau (noms de HudRoot.BuildAll
            // / HudExtensions.MountAll) puis fenêtre = parent de la
            // première CeguiTitleBar descendante.
            var names = new Dictionary<string, string[]>
            {
                ["inventory"] = new[] { "Inventory" },
                ["profile"] = new[] { "Profile" },
                ["skills"] = new[] { "Skills" },
                ["quests"] = new[] { "QuestLog" },
                ["map"] = new[] { "WorldMap" },
                ["dialogue"] = new[] { "NpcDialogue" },
                ["target"] = new[] { "TargetFrame" },
                ["shop"] = new[] { "ShopWnd" },
                ["bank"] = new[] { "BankWnd" },
                ["social"] = new[] { "SocialWnd" },
            };
            Control? root = null;
            if (names.TryGetValue(_key, out var cand))
                foreach (var n in cand)
                    if (_hud.FindChild(n, true, false) is Control c)
                    { root = c; break; }
            if (root == null)
            {
                GD.PrintErr($"[drag-test] fenêtre «{_key}» absente");
                SetProcess(false);
                return;
            }
            root.Visible = true;
            foreach (var d in root.FindChildren("*", "Panel", true, false)
                         .Cast<Control>())
                if (d.Name.ToString().EndsWith("Panel"))
                    d.Visible = true;   // ex. SocialPanel
            // Barres : recherche par NOM (« __auto_titlebar__* ») —
            // FindChildren par type C# ne matche pas les classes
            // non [GlobalClass] (bug historique d'EnsureTitleBar).
            _bar = root.FindChildren("__auto_titlebar__*", "", true,
                    false).Cast<Control>().FirstOrDefault()
                ?? root.FindChildren("*", "", true, false)
                    .Cast<Control>()
                    .FirstOrDefault(c => c is CeguiTitleBar);
            // La fenêtre réelle = l'enfant « Root_* » le plus grand,
            // pas la racine plein écran.
            Control? win = _bar?.GetParent() as Control;
            if (win == null)
                win = root.GetChildren().Cast<Control>()
                    .Where(c => !IsFullRect(c))
                    .OrderByDescending(c => c.Size.X * c.Size.Y)
                    .FirstOrDefault();
            _win = win ?? root;
            // Clé de persistance réelle (le watcher la porte en nom —
            // ex. « backpack » pour l'inventaire), sinon le rapport lit
            // toujours une entrée absente.
            _persistKey = _win
                .FindChildren("__dragpersist_*", "", true, false)
                .Cast<Node>()
                .Select(n => n.Name.ToString()["__dragpersist_".Length..])
                .FirstOrDefault();
            // Montre le panneau lui-même (pas seulement l'enveloppe) :
            // une fenêtre Visible=false n'est ni dessinée ni
            // pickable — le hover resterait null.
            HudWindows.Open(_win);
            DumpTree(root, 0, 2);
            GD.Print($"[drag-test] {_key} : win={_win.GetPath()} " +
                     $"rect={_win.GetGlobalRect()} " +
                     $"bar={_bar?.GetPath().ToString() ?? "ABSENTE"} " +
                     $"barRect={_bar?.GetGlobalRect().ToString() ?? "-"}");
        }

        private static void DumpTree(Node node, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            var indent = new string(' ', depth * 2);
            if (node is Control control)
                GD.Print($"[drag-test] {indent}{node.Name} " +
                    $"{node.GetType().Name} rect={control.GetGlobalRect()} " +
                    $"filter={control.MouseFilter}");
            else
                GD.Print($"[drag-test] {indent}{node.Name} " +
                    node.GetType().Name);
            foreach (var child in node.GetChildren())
                DumpTree(child, depth + 1, maxDepth);
        }

        private void Aim(bool background = false)
        {
            if (_win == null) return;
            _pos0 = _win.GlobalPosition;
            _pressAt = background
                // Point de fond : centre de la fenêtre, là où aucun
                // contrôle interactif n'est garanti — on choisit le
                // bord gauche bas du cadre (au-dessus du fond, hors
                // contenu usuel).
                ? _win.GetGlobalRect().Position +
                    new Vector2(6, _win.Size.Y - 6)
                : _bar?.GetGlobalRect().GetCenter()
                  ?? _win.GetGlobalRect().GetCenter();
            // Motion d'abord : enregistre le hover dans le viewport.
            Inject(new InputEventMouseMotion
            {
                Position = _pressAt, GlobalPosition = _pressAt,
            });
        }

        /// <summary>true si le contrôle survolé est la fenêtre ou un
        /// de ses descendants (overlay de chargement passé). Abandonne
        /// après ~10 s de retries.</summary>
        private bool HoverInWindow()
        {
            var hov = GetViewport().GuiGetHoveredControl();
            if (hov != null && (hov == _win || _win!.IsAncestorOf(hov)))
                return true;
            if (++_retries > 600)
            {
                GD.PrintErr($"[drag-test] {_key} : hover hors " +
                    $"fenêtre ({hov?.GetPath().ToString() ?? "null"})");
                SetProcess(false);
            }
            return false;
        }

        private void PressButton()
        {
            if (_win == null) return;
            CallDeferred(nameof(LogHover));
            Inject(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true,
                Position = _pressAt, GlobalPosition = _pressAt,
            });
        }

        private void LogHover()
        {
            var hov = GetViewport().GuiGetHoveredControl();
            GD.Print($"[drag-test] hover @{_pressAt} → " +
                $"{hov?.GetPath()} type={hov?.GetType().Name} " +
                $"filter={hov?.MouseFilter}");
        }

        private void Step(int i)
        {
            var p = _pressAt + new Vector2(i * 12f, i * 9f);
            Inject(new InputEventMouseMotion
            {
                Position = p, GlobalPosition = p,
                Relative = new Vector2(12, 9),
            });
        }

        private void Release()
        {
            var p = _pressAt + new Vector2(120, 90);
            Inject(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = false,
                Position = p, GlobalPosition = p,
            });
        }

        private void Report(bool final = false)
        {
            if (_win == null) return;
            var pos1 = _win.GlobalPosition;
            GD.Print($"[drag-test] {_key} " +
                     (final ? "(fond) " : "(titre) ") +
                     $"{_pos0} → {pos1} " +
                     $"moved={_pos0 != pos1} " +
                     $"persist={LoadPosition(_persistKey ?? _key)}");
        }

        private void Shot(string tag)
        {
            try
            {
                Directory.CreateDirectory(_shotsDir);
                RenderingServer.ForceDraw();
                GetViewport().GetTexture()?.GetImage()?.SavePng(
                    $"{_shotsDir}/h5_{_key}_{tag}.png");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[drag-test] shot : {e.Message}");
            }
        }
    }
}
