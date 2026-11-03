using Godot;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>
/// HUDs fixes déplaçables (vitals, minimap, quêtes, chat, hotbar,
/// menu, target, buffs…). Chaque module se déplace par sa <b>bande
/// haute</b> (« l'entête », ~22 px du haut de l'élément) : appui +
/// glisser = déplacer, appui + relâcher sans bouger = clic normal —
/// aucun chrome ajouté, rien de visible, l'interaction existante est
/// préservée. Tous les enfants d'un même panneau forment UN module
/// déplacé solidairement (le chat = onglets + log + saisie + canaux).
/// La position est écrite dans <c>clientSettings.json</c> au drop
/// (fraction du viewport) et restaurée au démarrage, clampée.
/// Fenêtres : drag par leur barre de titre via <see cref="DragHelper"/>.
/// </summary>
public partial class HudLayout : Node
{
    /// <summary>Panneaux permanents dont les enfants directs sont
    /// déplaçables (les fenêtres passent par <see cref="DragHelper"/>).</summary>
    private static readonly string[] Panels =
    {
        "Vitals", "Ultimate", "Minimap", "Quests", "Chat", "Hotbar",
        "MenuIcons", "Banner", "TargetFrame", "PetBar",
        "CastBar",
        // BuffBar/NerfBar : entête de drag (bande haute de la fenêtre),
        // le resize (colonnes) reste géré par BuffBar.cs.
        "BuffBar", "NerfBar",
    };

    /// <summary>Hauteur de la bande « entête » de déplacement (px).</summary>
    private const float HeaderPx = 22f;
    /// <summary>Extension au-dessus du bord haut (px) — zone morte
    /// entre modules : les pièces tout-interactives (ligne de slots,
    /// icônes menu, barre EXP — sans entête libre) restent saisissables
    /// sans que le drag natif des slots ne prenne la main.</summary>
    private const float GrabAbovePx = 10f;
    /// <summary>Seuil px distinguant clic et drag (comme les fenêtres).</summary>
    private const float DragThreshold = 6f;

    /// <summary>Panneaux dont CHAQUE pièce est un module indépendant —
    /// « Hotbar » : chaque barre de skill (FastBar1/2/3, max 3) et la
    /// barre EXP ont leur propre emplacement ; les autres panneaux
    /// bougent comme un bloc unique (le chat reste UN module).</summary>
    private static readonly HashSet<string> SplitPanels = new() { "Hotbar" };

    private const string MetaKey = "__hudlayout_key";
    /// <summary>Meta posée par un panneau sur sa racine CEGUI pour que
    /// <see cref="ScanChildren"/> enregistre ses enfants (pas la racine).</summary>
    public const string DescendMeta = "__hudlayout_descend";
    /// <summary>true tant qu'un watcher a armé un déplacement sur ce
    /// press (posé en _Input, donc déjà juste quand le contrôleur
    /// caméra voit l'événement en _UnhandledInput — les bandes de
    /// grab sur zone morte n'ont aucun Control sous la souris et
    /// passeraient sinon en clic-monde/caméra).</summary>
    public static bool GrabArmed;
    private static HudLayout? _inst;
    private readonly HudRoot _hud;
    private readonly Dictionary<string, (Control C, Vector2 Default)> _items = new();

    public HudLayout(HudRoot hud) { _hud = hud; Name = "__hud_layout__"; _inst = this; }

    public override void _Ready()
    {
        // Rescan périodique idempotent (MetaKey) — couvre les pièces
        // montées tardivement : buffs/status, target frame, extensions
        // post-HudReady.
        var t = GetTree().CreateTimer(0.6);
        t.Timeout += ScanLoop;
        void ScanLoop()
        {
            Scan();
            GetTree().CreateTimer(2.0).Timeout += ScanLoop;
        }
    }

    /// <summary>Enregistre les nouveaux éléments (idempotent), restaure
    /// leur position sauvegardée et leur pose un watcher de drag.
    /// Descend dans les conteneurs plein-écran (racines CEGUI
    /// MouseFilter=Ignore) pour atteindre les vraies pièces — mais
    /// ne descend JAMAIS dans une pièce enregistrée : un enfant
    /// enregistré sous un parent déplacé subirait deux translations.</summary>
    private void Scan()
    {
        foreach (var pn in Panels)
        {
            var panel = _hud.GetNodeOrNull<Control>(pn);
            if (panel == null) continue;
            ScanChildren(pn, panel, 0);
        }
    }

    private void ScanChildren(string pn, Control parent, int depth)
    {
        var idx = 0;
        var vp = parent.GetViewportRect().Size;
        foreach (var n in parent.GetChildren())
        {
            var i = idx++;
            if (n is not Control c || c.HasMeta(MetaKey)) continue;
            var nm = n.Name.ToString();
            if (nm.StartsWith("__")) continue;
            // Conteneur plein-écran (ancres 0..1 ou taille ≈ viewport,
            // p.ex. la racine CEGUI de ChatPanel) : pas une pièce —
            // on descend chercher les vrais éléments dedans.
            var fullAnch = c.AnchorLeft <= 0 && c.AnchorRight >= 1
                && c.AnchorTop <= 0 && c.AnchorBottom >= 1;
            var fullSize = c.Size.X >= vp.X * 0.9f
                && c.Size.Y >= vp.Y * 0.9f;
            // Racine CEGUI dont la vraie pièce déplaçable est un
            // enfant (MenuIcons : MenuBackgrand épinglé en bas-droite
            // sous la racine 550×385) : le panneau pose DescendMeta.
            if ((fullAnch || fullSize || c.HasMeta(DescendMeta))
                && depth < 4)
            {
                ScanChildren(pn, c, depth + 1);
                continue;
            }
            if (c.Size.X < 8 || c.Size.Y < 8) continue;
            var key = nm.StartsWith("@")
                ? $"{pn}/#{depth}_{i}" : $"{pn}/{nm}";
            Register(key, c);
        }
    }

    private void Register(string key, Control c)
    {
        c.SetMeta(MetaKey, key);
        var def = c.GlobalPosition;
        _items[key] = (c, def);
        if (ClientUiStore.Current.Huds.TryGetValue(key, out var p))
        {
            var vp = c.GetViewportRect().Size;
            Place(c, p.X * vp.X, p.Y * vp.Y);
        }
        c.AddChild(new MoveWatcher(key, c, this));
    }

    private static void Place(Control c, float x, float y)
    {
        var vp = c.GetViewportRect().Size;
        var (cx, cy) = ClientUiSettings.Clamp(x, y, c.Size.X, c.Size.Y, vp.X, vp.Y);
        c.GlobalPosition = new Vector2(cx, cy);
    }

    internal static void SaveItem(string key, Control c)
    {
        var vp = c.GetViewportRect().Size;
        var gp = c.GlobalPosition;
        ClientUiStore.Current.Huds[key] =
            new HudPlacement { X = gp.X / vp.X, Y = gp.Y / vp.Y };
        ClientUiStore.Save();
    }

    /// <summary>Ré-enregistre la position d'un élément déjà inscrit
    /// (après un changement de taille qui a déplacé son coin haut-gauche).</summary>
    public static void Persist(Control c)
    {
        if (c.HasMeta(MetaKey)) SaveItem((string)c.GetMeta(MetaKey), c);
    }

    /// <summary>Remet tous les HUDs fixes à leur position d'origine.</summary>
    public static void ResetAll()
    {
        if (_inst == null) return;
        foreach (var (_, v) in _inst._items)
            if (IsInstanceValid(v.C)) v.C.GlobalPosition = v.Default;
    }

    /// <summary>Les éléments du même module que <paramref name="key"/> :
    /// le panneau entier (même préfixe avant « / ») — sauf les panneaux
    /// <see cref="SplitPanels"/> où chaque pièce est son propre module
    /// (chaque skill bar a son emplacement).</summary>
    private List<KeyValuePair<string, Control>> GroupOf(string key)
    {
        var prefix = key[..(key.IndexOf('/') + 1)];
        if (SplitPanels.Contains(prefix[..^1]))
            return _items.TryGetValue(key, out var one) &&
                   IsInstanceValid(one.C)
                ? new List<KeyValuePair<string, Control>> { new(key, one.C) }
                : new List<KeyValuePair<string, Control>>();
        var g = new List<KeyValuePair<string, Control>>();
        foreach (var kv in _items)
            if (kv.Key.StartsWith(prefix) && IsInstanceValid(kv.Value.C))
                g.Add(new KeyValuePair<string, Control>(kv.Key, kv.Value.C));
        return g;
    }

    /// <summary>Watcher de drag d'un élément : arme sur appui LMB dans
    /// la bande haute de l'élément (le contrôle survolé doit être
    /// l'élément ou un descendant — sinon un voisin superposé
    /// l'emporterait). Pression simple = clic (rien n'est consommé) ;
    /// mouvement > seuil = déplacement du module entier. Un drag
    /// Godot natif (icône de hotbar…) annule et restaure les
    /// positions de départ. Sauvegarde au relâchement.</summary>
    private sealed partial class MoveWatcher : Node
    {
        private readonly string _key;
        private readonly Control _c;
        private readonly HudLayout _layout;
        private bool _armed, _drag;
        private Vector2 _press;
        private readonly List<KeyValuePair<string, Control>> _group = new();
        private readonly Dictionary<Control, Vector2> _start = new();

        public MoveWatcher(string key, Control c, HudLayout layout)
        {
            _key = key; _c = c; _layout = layout; Name = "__move_watch__";
        }

        /// <summary>Rect de la bande haute : les <see cref="HeaderPx"/>
        /// premiers px de l'élément PLUS <see cref="GrabAbovePx"/> px
        /// au-dessus de son bord — la zone morte de l'entête des
        /// modules tout-interactifs (slots, icônes, barres fines).</summary>
        private Rect2 HeaderRect() =>
            new(_c.GlobalPosition - new Vector2(0, GrabAbovePx),
                new Vector2(_c.Size.X,
                    Math.Min(HeaderPx, _c.Size.Y) + GrabAbovePx));

        public override void _Input(InputEvent e)
        {
            if (!GodotObject.IsInstanceValid(_c) || !_c.IsInsideTree())
            {
                QueueFree();
                return;
            }
            if (!_c.IsVisibleInTree())
            {
                _armed = _drag = false;
                GrabArmed = false;
                return;
            }

            if (e is InputEventMouseButton
                { ButtonIndex: MouseButton.Left } mb)
            {
                if (mb.Pressed)
                {
                    var hov = _c.GetViewport()?.GuiGetHoveredControl();
                    // hov==null : bande sur une zone morte (rien sous
                    // la souris) → le header sert à déplacer ; hov
                    // appartenant à un autre module → il garde le clic.
                    _armed = HeaderRect().HasPoint(mb.GlobalPosition) &&
                        (hov == null || hov == _c || _c.IsAncestorOf(hov));
                    if (_armed)
                    {
                        GrabArmed = true;
                        GD.Print($"[drag] arm {_key} hov={hov?.Name}");
                    }
                    _press = mb.GlobalPosition;
                    _drag = false;
                }
                else
                {
                    if (_drag)
                        foreach (var kv in _group)
                            SaveItem(kv.Key, kv.Value);
                    _armed = _drag = false;
                    GrabArmed = false;
                }
            }
            else if (_armed && e is InputEventMouseMotion mm)
            {
                // Un vrai drag&drop Godot (slot hotbar, item) a pris
                // la main → on restaure et on s'efface.
                if (_c.GetViewport()?.GuiIsDragging() == true)
                {
                    foreach (var (t, p) in _start)
                        if (IsInstanceValid(t)) t.GlobalPosition = p;
                    _armed = _drag = false;
                    GrabArmed = false;
                    _start.Clear();
                    return;
                }
                if (!_drag)
                {
                    if (mm.GlobalPosition.DistanceTo(_press)
                        < DragThreshold) return;
                    _drag = true;
                    _group.Clear();
                    _group.AddRange(_layout.GroupOf(_key));
                    foreach (var kv in _group)
                        _start[kv.Value] = kv.Value.GlobalPosition;
                }
                var delta = mm.GlobalPosition - _press;
                foreach (var (t, p) in _start)
                    if (IsInstanceValid(t))
                        Place(t, p.X + delta.X, p.Y + delta.Y);
            }
        }
    }
}
