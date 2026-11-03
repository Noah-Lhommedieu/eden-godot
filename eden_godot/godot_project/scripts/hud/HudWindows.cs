using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Registre central des fenêtres du HUD (plan v4 H3/H4).
///
/// Une « fenêtre » est le panneau visible (cadre + titre), jamais son
/// enveloppe plein écran (<c>HudRoot.Mount</c>). Toutes les actions
/// passent par ici : ouvrir / fermer / basculer agissent sur le
/// panneau ; l'enveloppe est simplement rendue visible à l'ouverture.
/// La fenêtre cliquée passe au premier plan (enveloppe déplacée en
/// dernier enfant du CanvasLayer) ; Échap ferme la plus haute.
/// Inscription automatique par <see cref="EeSkin.MakeWindow"/> et
/// <see cref="DragHelper.EnsureTitleBar"/>.
/// </summary>
public static partial class HudWindows
{
    private static readonly List<Control> _wins = new();
    private static readonly Dictionary<ulong, long> _front = new();
    private static long _stamp;

    public static void Register(Control win)
    {
        if (_wins.Contains(win)) return;
        _wins.Add(win);
        win.TreeExiting += () =>
        {
            _wins.Remove(win);
            _front.Remove(win.GetInstanceId());
        };
        win.VisibilityChanged += () =>
        {
            if (!win.Visible || !win.IsInsideTree()) return;
            Front(win);
            if (HudPrefs.JellyEffect)
                EdenClient.Settings.FeedbackFx.JellyPop(win);
            // Recadrage après la passe d'ancres (taille/position
            // valides la frame suivante).
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(win) && win.Visible)
                    DragHelper.ClampIntoView(win);
            }).CallDeferred();
        };
    }

    private static bool Valid(Control? w) =>
        w != null && GodotObject.IsInstanceValid(w) && w.IsInsideTree();

    public static bool IsOpen(Control? w) => Valid(w) && w!.IsVisibleInTree();

    /// <summary>Affiche la fenêtre et ses ancêtres (enveloppe).</summary>
    public static void Open(Control? w)
    {
        if (!Valid(w)) return;
        for (var p = w!.GetParent(); p is Control c; p = c.GetParent())
            c.Visible = true;
        w.Visible = true;
        Front(w);
    }

    public static void Close(Control? w)
    {
        if (Valid(w)) w!.Visible = false;
    }

    public static void Toggle(Control? w)
    {
        if (IsOpen(w)) Close(w); else Open(w);
    }

    /// <summary>Bascule les fenêtres inscrites contenues dans
    /// <paramref name="container"/> (enveloppe d'un panneau) : ferme
    /// celles qui sont ouvertes, sinon ouvre la première. Sans fenêtre
    /// inscrite, bascule simplement le conteneur.</summary>
    public static void ToggleIn(Control? container)
    {
        if (!Valid(container)) return;
        var inside = _wins.Where(w => Valid(w) &&
            (w == container || container!.IsAncestorOf(w))).ToList();
        if (inside.Count == 0)
        {
            container!.Visible = !container.Visible;
            return;
        }
        if (inside.Any(IsOpen))
            foreach (var w in inside) Close(w);
        else
            Open(inside[0]);
    }

    /// <summary>Premier plan : la fenêtre devient la plus récente et
    /// son nœud de plus haut niveau passe en dernier enfant du
    /// CanvasLayer (dessiné et cliqué au-dessus des autres).</summary>
    public static void Front(Control w)
    {
        if (!Valid(w)) return;
        _front[w.GetInstanceId()] = ++_stamp;
        Node n = w;
        while (n.GetParent() is Node p && p is not CanvasLayer) n = p;
        if (n.GetParent() is CanvasLayer layer &&
            n.GetIndex() != layer.GetChildCount() - 1)
            layer.MoveChild(n, layer.GetChildCount() - 1);
    }

    /// <summary>Clic dans le HUD : la fenêtre inscrite qui contient
    /// le contrôle survolé passe au premier plan.</summary>
    public static void OnPress(Control hovered)
    {
        for (Node? n = hovered; n != null; n = n.GetParent())
            if (n is Control c && _wins.Contains(c))
            {
                Front(c);
                return;
            }
    }

    public static bool AnyOpen => _wins.Any(IsOpen);

    /// <summary>Échap : ferme la fenêtre ouverte la plus récente au
    /// premier plan — false s'il n'y en a aucune.</summary>
    public static bool CloseTopmost()
    {
        var top = _wins.Where(IsOpen)
            .OrderByDescending(w =>
                _front.GetValueOrDefault(w.GetInstanceId()))
            .FirstOrDefault();
        if (top == null) return false;
        Close(top);
        return true;
    }

    public static void CloseAll()
    {
        foreach (var w in _wins.ToList())
            if (IsOpen(w)) Close(w);
    }

    /// <summary>Noms des fenêtres inscrites actuellement ouvertes —
    /// diagnostic (<c>--esc-test</c>).</summary>
    public static string DescribeOpen() =>
        string.Join(" + ",
            _wins.Where(IsOpen).Select(w => w.Name.ToString()));

    /// <summary>Nœud de diagnostic <c>--esc-test</c> : ouvre Backpack
    /// + Character Info, injecte Échap puis vérifie l'ordre de
    /// fermeture (la plus récente d'abord), l'ouverture du Main Menu
    /// sur pile vide, et le clic sur la croix — preuve H4/H3 en
    /// runtime sans action manuelle (même injection que
    /// <see cref="DragHelper.DragProbe"/>).</summary>
    public sealed partial class EscProbe : Node
    {
        private readonly HudRoot _hud;
        private readonly string _shotsDir;
        private int _frame;
        private Vector2 _pressAt;

        public EscProbe(HudRoot hud)
        {
            _hud = hud;
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
            Name = "__esc_probe__";
        }

        public override void _Process(double delta)
        {
            _frame++;
            switch (_frame)
            {
                case 45:
                    _hud.OpenWindow("inventory");
                    _hud.OpenWindow("profile");
                    Log("open");
                    Shot("h4_two_windows");
                    break;
                case 60: Esc(); break;
                case 75: Log("esc#1"); Shot("h4_after_esc1"); break;
                case 90: Esc(); break;
                case 105: Log("esc#2"); break;
                case 120: Esc(); break;
                case 135: Log("esc#3"); Shot("h4_menu"); break;
                case 150: Esc(); break;
                case 165: Log("esc#4"); break;
                case 180: AimClose(); break;
                case 182: Click(true); break;
                case 184: Click(false); break;
                case 200:
                    Log("close-btn");
                    Shot("h4_closed_by_x");
                    SetProcess(false);
                    break;
            }
        }

        private void Esc()
        {
            Input.ParseInputEvent(new InputEventKey
            {
                Keycode = Key.Escape, Pressed = true,
            });
            Input.ParseInputEvent(new InputEventKey
            {
                Keycode = Key.Escape, Pressed = false,
            });
        }

        private void Log(string tag) =>
            GD.Print($"[esc-test] {tag} : open=[{DescribeOpen()}]");

        /// <summary>Ré-ouvre l'inventaire et vise le centre de sa
        /// croix native (<c>__auto_closebtn__</c>).</summary>
        private void AimClose()
        {
            _hud.OpenWindow("inventory");
            var inv = _hud.FindChild("Inventory", true, false);
            var btn = inv?.FindChildren("__auto_closebtn__*", "", true,
                    false).OfType<BaseButton>().FirstOrDefault();
            if (btn == null)
            {
                GD.PrintErr("[esc-test] croix introuvable");
                return;
            }
            _pressAt = btn.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion
            {
                Position = _pressAt, GlobalPosition = _pressAt,
            });
        }

        private void Click(bool down) =>
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = down,
                Position = _pressAt, GlobalPosition = _pressAt,
            });

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
                GD.PrintErr($"[esc-test] shot : {e.Message}");
            }
        }
    }
}
