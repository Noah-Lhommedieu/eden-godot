using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;
using EdenClient.Screens;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre « Portal » (cristal de téléportation) — ouverte par le 393
/// EnterTransport (<see cref="TransportState"/>). Reproduit
/// <c>ref-shots/TransportationArray (TP changement carte).png</c> :
/// layout natif <c>TransportWnd.layout</c> = carte du continent + une
/// icône cristal par destination (<c>Btn&lt;nodeId&gt;</c> : l'id de route
/// = nodeId, cf. <see cref="WorldMapWindow"/>), combo continent, grandes
/// flèches page précédente/suivante, bouton Close. Survol = infobulle
/// « nom + coût » (<c>TipWnd</c>) ; clic = confirmation « You need to pay
/// … to teleport to … » puis <see cref="RouteChosen"/> →
/// <see cref="ZoneTransport"/> (C→S 88, id de route).
///
/// Page 0 « Central Continent » : positions natives du layout
/// (nodes 1-16, 22-27, 32-34, 36-38). Les autres destinations sont
/// réparties sur des pages supplémentaires (les boutons du layout sont
/// recyclés en grille — les positions natives de ces pages ne sont pas
/// dans le layout).
/// </summary>
public partial class TransportWindow : EdenWindow
{
    /// <summary>Émis à la confirmation — argument = routeId.</summary>
    public event Action<int>? RouteChosen;

    private sealed record PageDef(string Name, string Bg, List<int> Routes);

    private sealed class Pin
    {
        public Control Btn = null!;
        public float L, T, R, B, oL, oT, oR, oB;
        public int NativeId;
        public int Route;
    }

    private static readonly HashSet<int> Page0Nodes = new(
        Enumerable.Range(1, 16)
            .Concat(Enumerable.Range(22, 6))
            .Concat(Enumerable.Range(32, 3))
            .Concat(Enumerable.Range(36, 3)));

    private static readonly string[] ExtraBgs =
        { "mapworld02.png", "mapworld03.png", "mapworld031.png",
          "mapworld00.png" };
    private static readonly string[] ExtraNames =
        { "Northern Continent", "Western Continent", "Eastern Continent",
          "Outer Lands" };

    private readonly List<Pin> _pins = new();
    private readonly List<PageDef> _pages = new();
    private int _page;
    private TextureRect? _map;
    private Texture2D? _page0Tex;
    private Control? _tip;
    private Label? _tipName, _tipMoney;
    private Control? _left, _right;
    private OptionButton? _combo;
    private Control? _myPos;
    private ConfirmationDialog? _confirm;
    private int _pendingRoute;

    public void Build(HudRoot hud)
    {
        BuildWindow(hud, "TransportWnd.layout", UiTextEn.Translate("Portal"),
            "transport", 0.5f, 0.5f, 0.5f, 0.5f, -230, -275, 230, 275);
        Visible = false;
        TransportState.Changed += Refresh;
    }

    private Control? Leaf(string leaf)
    {
        if (Window == null) return null;
        foreach (var n in Window.FindChildren("*_" + leaf, "Control",
                     true, false))
            if (n is Control c && n.Name.ToString().EndsWith("_" + leaf))
                return c;
        return null;
    }

    protected override void OnBuilt(Control window)
    {
        var close = Leaf("closeBtn") as Button;
        if (close != null)
        {
            close.Text = UiTextEn.Translate("Close");
            close.Pressed += () => Visible = false;
        }
        var title = window
            .FindChildren("*", "Control", true, false)
            .OfType<Ui.CeguiTitleBar>().FirstOrDefault();
        var lbl = title?.FindChildren("*", "Label", true, false)
            .Cast<Label>().FirstOrDefault();
        if (lbl != null) lbl.Text = UiTextEn.Translate("Portal");

        _map = Leaf("mapImage") as TextureRect;
        _page0Tex = _map?.Texture;
        _tip = Leaf("TipWnd");
        _tipName = Leaf("NameText") as Label;
        _tipMoney = Leaf("MoneyText") as Label;
        _left = Leaf("BtnLeft");
        _right = Leaf("BtnRight");
        _combo = Leaf("combobox") as OptionButton;
        _myPos = Leaf("myposition");
        // Aperçu « grand monde » (sélecteur continent natif) : remplacé
        // par le combo continent du shot de référence.
        if (Leaf("BigWorldImage") is { } big) big.Visible = false;
        if (_myPos != null) _myPos.Visible = false;
        if (_tip != null) _tip.Visible = false;

        if (_map != null)
            foreach (var n in _map.GetChildren())
            {
                if (n is not Control c) continue;
                var name = c.Name.ToString();
                var i = name.LastIndexOf("_Btn", StringComparison.Ordinal);
                if (i < 0 || !int.TryParse(name[(i + 4)..], out var id))
                    continue;
                _pins.Add(new Pin
                {
                    Btn = c, NativeId = id,
                    L = c.AnchorLeft, T = c.AnchorTop,
                    R = c.AnchorRight, B = c.AnchorBottom,
                    oL = c.OffsetLeft, oT = c.OffsetTop,
                    oR = c.OffsetRight, oB = c.OffsetBottom,
                });
                c.Visible = false;
                var pin = _pins[^1];
                if (c is BaseButton bb)
                {
                    bb.Pressed += () => OnPinPressed(pin);
                    bb.MouseEntered += () => ShowTip(pin);
                    bb.MouseExited += HideTip;
                }
            }
        if (_left is BaseButton lb) lb.Pressed += () => SetPage(_page - 1);
        if (_right is BaseButton rb) rb.Pressed += () => SetPage(_page + 1);
        if (_combo != null)
        {
            // Le Combobox natif déclare la hauteur de sa liste déroulante
            // (≈ 100 px) : en bouton Godot il faut une hauteur de ligne.
            _combo.AnchorBottom = _combo.AnchorTop;
            _combo.OffsetTop = 32;
            _combo.OffsetBottom = 58;
            _combo.ItemSelected += i => SetPage((int)i);
        }
        // Flèches pleinement opaques (le layout natif les met à 0.3).
        if (_left != null) _left.Modulate = Colors.White;
        if (_right != null) _right.Modulate = Colors.White;
    }

    private void OnPinPressed(Pin p)
    {
        if (p.Route <= 0) return;
        HideTip();
        var level = SessionCarry.Character?.Level ?? 1;
        var cost = TransportTable.RouteCost(p.Route, level);
        var name = TransportState.Routes
            .FirstOrDefault(r => r.RouteId == p.Route)?.Destination
            ?? $"#{p.Route}";
        _pendingRoute = p.Route;
        _confirm?.QueueFree();
        _confirm = new ConfirmationDialog
        {
            DialogText = UiTextEn.Translate(
                    "You need to pay {0} to teleport to {1}.")
                .Replace("{0}", FormatMoney(cost))
                .Replace("{1}", name),
            OkButtonText = UiTextEn.Translate("Confirm"),
            CancelButtonText = UiTextEn.Translate("Cancel"),
            Exclusive = true,
        };
        _confirm.Confirmed += () =>
        {
            var r = _pendingRoute;
            _confirm?.QueueFree();
            _confirm = null;
            RouteChosen?.Invoke(r);
            Visible = false;
        };
        _confirm.Canceled += () =>
        {
            _confirm?.QueueFree();
            _confirm = null;
        };
        AddChild(_confirm);
        _confirm.PopupCentered();
    }

    /// <summary>Monnaie brute → « 1 Gold 20 Silver » (1000 Silver =
    /// 1 Gold, FAQ wiki ; statue 500 po = 500000), « Free » si 0.</summary>
    public static string FormatMoney(int copper)
    {
        if (copper <= 0) return UiTextEn.Translate("Free");
        var g = copper / 1000;
        var s = copper % 1000;
        var parts = new List<string>();
        if (g > 0) parts.Add($"{g} {UiTextEn.Translate("Gold")}");
        if (s > 0) parts.Add($"{s} {UiTextEn.Translate("Silver")}");
        return string.Join(" ", parts);
    }

    private void ShowTip(Pin p)
    {
        if (_tip == null || p.Route <= 0) return;
        var level = SessionCarry.Character?.Level ?? 1;
        var cost = TransportTable.RouteCost(p.Route, level);
        var name = TransportState.Routes
            .FirstOrDefault(r => r.RouteId == p.Route)?.Destination ?? "";
        if (_tipName != null) _tipName.Text = name;
        if (_tipMoney != null)
            _tipMoney.Text = cost > 0 ? FormatMoney(cost) : "";
        // Cadre natif minuscule : on lui donne la taille du texte
        // (nom en haut, coût dessous) — shot de référence « Rainbowfall
        // Forest 100 ».
        _tip.CustomMinimumSize = new Vector2(
            Mathf.Max(110f, name.Length * 7.5f + 20f), 40f);
        _tip.Size = _tip.CustomMinimumSize;
        foreach (var l in new[] { _tipName, _tipMoney })
        {
            if (l == null) continue;
            l.AnchorLeft = 0; l.AnchorRight = 1;
            l.OffsetLeft = 6; l.OffsetRight = -6;
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.AutowrapMode = TextServer.AutowrapMode.Off;
        }
        if (_tipName != null)
        {
            _tipName.AnchorTop = 0; _tipName.AnchorBottom = 0;
            _tipName.OffsetTop = 3; _tipName.OffsetBottom = 21;
        }
        if (_tipMoney != null)
        {
            _tipMoney.AnchorTop = 0; _tipMoney.AnchorBottom = 0;
            _tipMoney.OffsetTop = 21; _tipMoney.OffsetBottom = 38;
        }
        if (_tip.GetNodeOrNull<Panel>("__bg") == null)
        {
            var bg = new Panel { Name = "__bg",
                MouseFilter = Control.MouseFilterEnum.Ignore };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.07f, 0.05f, 0.12f, 0.92f),
                BorderColor = new Color(0.62f, 0.5f, 0.85f),
                BorderWidthTop = 1, BorderWidthBottom = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            });
            _tip.AddChild(bg);
            _tip.MoveChild(bg, 0);
        }
        _tipName?.AddThemeColorOverride("font_color", Colors.White);
        _tipMoney?.AddThemeColorOverride("font_color",
            new Color(1f, 0.85f, 0.35f));
        _tip.Visible = true;
        _tip.MoveToFront();
        // Posé juste sous l'icône (coordonnées locales de la carte).
        if (_map != null)
        {
            var r = p.Btn.GetGlobalRect();
            var m = _map.GetGlobalRect();
            _tip.SetAnchorsPreset(LayoutPreset.TopLeft);
            _tip.Position = new Vector2(
                Mathf.Clamp(r.Position.X - m.Position.X - 20, 0,
                    Mathf.Max(0, m.Size.X - _tip.Size.X)),
                Mathf.Clamp(r.End.Y - m.Position.Y + 2, 0,
                    Mathf.Max(0, m.Size.Y - _tip.Size.Y)));
        }
    }

    private void HideTip()
    {
        if (_tip != null) _tip.Visible = false;
    }

    /// <summary>Découpe les routes proposées en pages (continent).</summary>
    private void BuildPages()
    {
        _pages.Clear();
        var all = TransportState.Routes.Select(r => (int)r.RouteId).ToList();
        var central = all.Where(Page0Nodes.Contains).ToList();
        var rest = all.Where(i => !Page0Nodes.Contains(i)).OrderBy(i => i)
            .ToList();
        if (central.Count > 0 || rest.Count == 0)
            _pages.Add(new PageDef("Central Continent", "", central));
        var k = 0;
        for (var i = 0; i < rest.Count; i += 12, k++)
            _pages.Add(new PageDef(
                ExtraNames[Math.Min(k, ExtraNames.Length - 1)],
                ExtraBgs[Math.Min(k, ExtraBgs.Length - 1)],
                rest.Skip(i).Take(12).ToList()));
        if (_combo != null)
        {
            _combo.Clear();
            foreach (var p in _pages)
                _combo.AddItem(UiTextEn.Translate(p.Name));
        }
    }

    private void SetPage(int page)
    {
        if (_pages.Count == 0) return;
        _page = (page % _pages.Count + _pages.Count) % _pages.Count;
        _combo?.Select(_page);
        var def = _pages[_page];
        if (_map != null)
        {
            _map.Texture = def.Bg.Length == 0 ? _page0Tex
                : LoadBg(def.Bg) ?? _page0Tex;
        }
        var visible = _pages.Count > 1;
        if (_left != null) _left.Visible = visible;
        if (_right != null) _right.Visible = visible;
        HideTip();
        // Réattribue les boutons du layout.
        foreach (var p in _pins) { p.Route = 0; p.Btn.Visible = false; }
        if (_page == 0 && def.Bg.Length == 0)
        {
            foreach (var r in def.Routes)
            {
                // Boutons « placeholder » du layout (ancres nulles) exclus.
                var pin = _pins.FirstOrDefault(x => x.NativeId == r &&
                    x.R - x.L > 0.01f);
                if (pin == null) continue;
                Place(pin, pin.L, pin.T, pin.R, pin.B,
                    pin.oL, pin.oT, pin.oR, pin.oB);
                pin.Route = r;
                pin.Btn.Visible = true;
            }
            return;
        }
        // Pages supplémentaires : grille 4×3 de cristaux.
        var pool = _pins.Where(x => x.Route == 0).ToList();
        for (var i = 0; i < def.Routes.Count && i < pool.Count; i++)
        {
            var col = i % 4; var row = i / 4;
            var cx = 0.17f + col * 0.22f;
            var cy = 0.22f + row * 0.24f;
            Place(pool[i], cx - 0.033f, cy - 0.035f, cx + 0.033f,
                cy + 0.035f, 0, 0, 0, 0);
            pool[i].Route = def.Routes[i];
            pool[i].Btn.Visible = true;
        }
    }

    private static void Place(Pin p, float l, float t, float r, float b,
        float oL, float oT, float oR, float oB)
    {
        p.Btn.AnchorLeft = l; p.Btn.AnchorTop = t;
        p.Btn.AnchorRight = r; p.Btn.AnchorBottom = b;
        p.Btn.OffsetLeft = oL; p.Btn.OffsetTop = oT;
        p.Btn.OffsetRight = oR; p.Btn.OffsetBottom = oB;
    }

    private static readonly Dictionary<string, Texture2D?> BgCache = new();
    private static Texture2D? LoadBg(string file)
    {
        if (BgCache.TryGetValue(file, out var t)) return t;
        Texture2D? tex = null;
        var path = "res://ui_textures/imagesets/" + file;
        var img = Vfs.EdenVfs.LoadImage("uidata/imagesets/" + file);
        if (img != null) tex = ImageTexture.CreateFromImage(img);
        else if (Vfs.EdenVfs.LooseAllowed && ResourceLoader.Exists(path))
            tex = ResourceLoader.Load<Texture2D>(path);
        // Les atlas sont 512² ; la carte native n'occupe que la
        // région Background (0,0,512,512) — texture entière.
        BgCache[file] = tex;
        return tex;
    }

    /// <summary>(Re)peint selon <see cref="TransportState"/>.</summary>
    private void Refresh()
    {
        if (!TransportState.IsOpen)
        {
            Visible = false;
            return;
        }
        BuildPages();
        SetPage(0);
        Visible = true;
        Window?.MoveToFront();
    }

    /// <summary>Repli sans uidata.</summary>
    protected override Control? BuildFallback()
    {
        var p = new Panel { Name = "TransportWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-230, -275);
        p.Size = new Vector2(460, 550);
        var close = new Button { Text = UiTextEn.Translate("Close") };
        close.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        close.Position = new Vector2(-100, -40);
        close.Size = new Vector2(90, 30);
        close.Pressed += () => Visible = false;
        p.AddChild(close);
        return p;
    }
}
