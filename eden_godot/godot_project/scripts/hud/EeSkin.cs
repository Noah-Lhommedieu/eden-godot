using Godot;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// Briques visuelles partagées du HUD (style « Noble Irongray » des
/// ref-shots) : frames 9-slice composées depuis les régions d'atlas
/// natives, onglets, case vide fleur-de-lys, barres en dégradé.
/// Toutes les tailles sont celles de la ref 1280×720 (échelle 1).
/// </summary>
internal static class EeSkin
{
    /// <summary>Frame 9-slice depuis 9 régions nommées
    /// <c>&lt;prefix&gt;&lt;Part&gt;&lt;suffix&gt;</c>. <paramref name="names"/> =
    /// noms de parts dans l'ordre TL,T,TR,L,C,R,BL,B,BR.</summary>
    public static StyleBox? Nine(HudRoot hud, string set,
        string prefix, string suffix, string[] names)
    {
        if (hud.Tex == null) return null;
        var spec = new UiStyleSpec { Kind = UiStyleKind.Composite };
        var parts = new[]
        {
            UiPart.TopLeft, UiPart.Top, UiPart.TopRight,
            UiPart.Left, UiPart.Center, UiPart.Right,
            UiPart.BottomLeft, UiPart.Bottom, UiPart.BottomRight,
        };
        for (var i = 0; i < 9; i++)
            if (names[i].Length > 0)
                spec.Parts[parts[i]] =
                    new CeguiImageRef(set, prefix + names[i] + suffix);
        return hud.Tex.ComposeNinePatch(spec);
    }

    /// <summary>Case vide fleur-de-lys (DejaVu Slot*) — hotbar.</summary>
    public static StyleBox? EmptySlot(HudRoot hud) => Nine(hud, "DejaVu",
        "Slot", "", new[]
        {
            "TopLeft", "Top", "TopRight", "Left", "Backdrop", "Right",
            "BottomLeft", "Bottom", "BottomRight",
        });

    /// <summary>Onglet natif (TabButton*Normal / *Selected).</summary>
    public static StyleBox? Tab(HudRoot hud, bool selected) => Nine(hud,
        "DejaVu", "TabButton", selected ? "Selected" : "Normal", new[]
        {
            "UpperLeft", "Upper", "UpperRight", "Left", "Middle",
            "Right", "LowerLeft", "Lower", "LowerRight",
        });

    /// <summary>Remplissage de barre en dégradé vertical (reflet clair
    /// en haut, couleur, ombre en bas) — HP rose / MP cyan / EXP.</summary>
    public static StyleBoxTexture GradientFill(Color top, Color mid,
        Color bottom)
    {
        var g = new Gradient();
        g.SetColor(0, top);
        g.SetColor(1, bottom);
        g.AddPoint(0.45f, mid);
        var tex = new GradientTexture2D
        {
            Gradient = g, Width = 4, Height = 16,
            FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
        };
        return new StyleBoxTexture { Texture = tex };
    }

    public static readonly StyleBoxTexture HpFill = GradientFill(
        Color.Color8(250, 218, 225), Color.Color8(236, 128, 150),
        Color.Color8(168, 70, 90));
    public static readonly StyleBoxTexture MpFill = GradientFill(
        Color.Color8(228, 248, 252), Color.Color8(120, 196, 220),
        Color.Color8(45, 115, 137));
    /// <summary>Barre Break Point (BPBackground/Bar) : violet.</summary>
    public static readonly StyleBoxTexture BpFill = GradientFill(
        Color.Color8(238, 214, 252), Color.Color8(168, 96, 214),
        Color.Color8(96, 40, 140));
    /// <summary>EXP perso : vert pomme (hud-interfaces.md).</summary>
    public static readonly StyleBoxTexture ExpFill = GradientFill(
        Color.Color8(226, 252, 200), Color.Color8(140, 214, 70),
        Color.Color8(70, 140, 30));
    /// <summary>EXP de classe : violet (DoubleEXPbar.png).</summary>
    public static readonly StyleBoxTexture ClassExpFill = GradientFill(
        Color.Color8(246, 226, 252), Color.Color8(196, 150, 230),
        Color.Color8(110, 70, 170));

    /// <summary>Fond de barre sombre (piste vide des ref).</summary>
    public static StyleBoxFlat BarTrack() => new()
    {
        BgColor = Color.Color8(30, 33, 40, 235),
        BorderColor = Color.Color8(12, 12, 14),
        BorderWidthBottom = 1, BorderWidthTop = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1,
    };

    /// <summary>Cellule grise arrondie à chiffre (grille 1-12 des
    /// vitals).</summary>
    public static StyleBoxFlat KeyCell() => new()
    {
        BgColor = Color.Color8(92, 92, 88, 235),
        BorderColor = Color.Color8(30, 30, 28),
        BorderWidthBottom = 2, BorderWidthTop = 1,
        BorderWidthLeft = 1, BorderWidthRight = 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
    };

    /// <summary>Panneau gris translucide (quest tracker, popup
    /// tutoriel) — bord clair d'1 px.</summary>
    public static StyleBoxFlat GreyPanel(byte alpha = 150) => new()
    {
        BgColor = Color.Color8(70, 72, 74, alpha),
        BorderColor = Color.Color8(150, 150, 150, 120),
        BorderWidthBottom = 1, BorderWidthTop = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusBottomRight = 6,
    };

    /// <summary>Bouton gris sombre « Track Quests » des ref.</summary>
    public static StyleBoxFlat DarkButton(bool hover = false) => new()
    {
        BgColor = hover ? Color.Color8(96, 98, 100, 240)
                        : Color.Color8(74, 76, 78, 240),
        BorderColor = Color.Color8(150, 150, 150, 200),
        BorderWidthTop = 2, BorderWidthLeft = 1, BorderWidthRight = 1,
        BorderWidthBottom = 0,
        ContentMarginLeft = 6, ContentMarginRight = 6,
    };

    /// <summary>Texte blanc à ombre portée (labels HUD).</summary>
    public static void Shadowed(Label l, int size, Color? col = null)
    {
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col ?? Colors.White);
        l.AddThemeColorOverride("font_shadow_color",
            new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    /// <summary>ImageOnlyCheckbox natif (CheckBox Godot sans skin →
    /// carré sombre) : icônes off/on depuis l'atlas, fond vide.</summary>
    public static void ImageCheck(HudRoot hud, Control? c, string set,
        string offImage, string onImage)
    {
        if (c is not CheckBox cb) return;
        var off = hud.Region(set, offImage);
        var on = hud.Region(set, onImage) ?? off;
        if (off == null) return;
        foreach (var st in new[] { "normal", "hover", "pressed",
                     "hover_pressed", "focus", "disabled" })
            cb.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
        cb.AddThemeIconOverride("unchecked", off);
        cb.AddThemeIconOverride("checked", on!);
        cb.AddThemeIconOverride("unchecked_disabled", off);
        cb.AddThemeIconOverride("checked_disabled", on!);
        cb.AddThemeConstantOverride("h_separation", 0);
        cb.Text = "";
    }

    // ------------------------------------------------------------
    // Fenêtres (phase 2 — ref shot_0017 « Backpack ») : corps
    // anthracite chaud, liseré argenté natif DejaVu Window*, bandeau
    // de titre sombre à libellé gris, croix native.

    // -------------------- palette de thème (M1) --------------------
    //
    // Les couleurs chromées ci-dessous suivent le thème courant :
    // <see cref="ThemePalette.FromAtlas"/> les dérive des pixels de
    // l'atlas DejaVu thémé (mêmes deltas que le client natif). Les
    // StyleBoxFlat produites par les fabriques ParchmentBox/InsetBox/
    // PanelTab/GreyButton sont enregistrées dans <see cref="_themed"/>
    // et mutées en place au changement de thème — les fenêtres déjà
    // ouvertes se re-teintent sans être reconstruites.

    private static ThemePalette _palette = ThemePalette.Irongray;

    /// <summary>Palette chromée courante (thème actif).</summary>
    public static ThemePalette Palette => _palette;

    public static Color WindowBody => _palette.WindowBody;
    public static Color TitleBand => _palette.TitleBand;
    public static Color Parchment => _palette.Parchment;
    public static Color DarkLine => _palette.DarkLine;

    /// <summary>Styleboxes thémées encore vivantes (référence faible —
    /// une fenêtre reconstruite libère les anciennes).</summary>
    private static readonly List<(WeakReference<StyleBoxFlat> Sb,
        PaletteSlot Bg, PaletteSlot Border)> _themed = new();

    /// <summary>Enregistre <paramref name="sb"/> pour re-teintage au
    /// prochain changement de thème, puis la retourne.</summary>
    private static StyleBoxFlat Themed(StyleBoxFlat sb, PaletteSlot bg,
        PaletteSlot border)
    {
        _themed.Add((new WeakReference<StyleBoxFlat>(sb), bg, border));
        return sb;
    }

    /// <summary>Applique le thème courant : recalcule la palette depuis
    /// l'atlas thémé de <paramref name="tex"/> (null → Irongray), mute
    /// en place toutes les styleboxes enregistrées et recolore les
    /// fonds de client-area (<c>__client_fill</c>) déjà posés.</summary>
    public static void ApplyThemePalette(UiTextureCache? tex)
    {
        _palette = tex != null ? ThemePalette.FromAtlas(tex)
                               : ThemePalette.Irongray;
        foreach (var t in _themed.ToArray())
        {
            if (!t.Sb.TryGetTarget(out var sb) ||
                !GodotObject.IsInstanceValid(sb))
            {
                _themed.Remove(t);
                continue;
            }
            sb.BgColor = _palette.ColorOf(t.Bg);
            if (t.Border != PaletteSlot.None)
                sb.BorderColor = _palette.ColorOf(t.Border);
        }
        if (Engine.GetMainLoop() is SceneTree tree)
            foreach (var f in tree.Root
                         .FindChildren("__client_fill", "ColorRect",
                             true, false).Cast<ColorRect>())
                f.Color = _palette.WindowBody;
    }

    /// <summary>Fenêtre de panel au gabarit ref : <paramref name="x"/>,
    /// <paramref name="y"/> = coin haut-gauche en 1280×720. Chrome
    /// natif de la ref (cadre DejaVu/FrameWindow à liseré argenté,
    /// barre de titre DejaVu/Titlebar, croix native), corps sombre ;
    /// inscrite au registre <see cref="HudWindows"/> (premier plan,
    /// Échap, drag par la barre de titre).</summary>
    public static Panel MakeWindow(HudRoot hud, string title, int x,
        int y, int w, int h)
    {
        var win = new Panel { Name = title.Replace(" ", "") + "Wnd" };
        HudRoot.Pin(win, 0, 0, 0, 0, x, y, x + w, y + h);
        DragHelper.EnsureTitleBar(win, title, 12);
        DragHelper.FillClientArea(win, WindowBody);
        return win;
    }

    /// <summary>Listes natives (ItemList) d'une fenêtre issue d'un
    /// layout : parchemin à texte sombre, sélection grise (ref :
    /// CommunityPanel, PetsPanel, ItemMallPanel).</summary>
    public static void SkinLists(Control root)
    {
        var ink = Color.Color8(30, 28, 26);
        foreach (var l in root.FindChildren("*", "ItemList", true, false)
                     .Cast<ItemList>())
        {
            if (l.HasMeta("eden_skinned")) continue;
            l.SetMeta("eden_skinned", true);
            l.AddThemeStyleboxOverride("panel", ParchmentBox());
            var sel = new StyleBoxFlat { BgColor = Color.Color8(184, 182, 174) };
            l.AddThemeStyleboxOverride("selected", sel);
            l.AddThemeStyleboxOverride("selected_focus", sel);
            l.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            foreach (var c in new[] { "font_color", "font_hovered_color",
                         "font_selected_color" })
                l.AddThemeColorOverride(c, ink);
        }
    }

    /// <summary>Zone liste/grille parchemin à liseré sombre.</summary>
    public static StyleBoxFlat ParchmentBox() => Themed(new()
    {
        BgColor = Parchment,
        BorderColor = DarkLine,
        BorderWidthTop = 1, BorderWidthBottom = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1,
    }, PaletteSlot.Parchment, PaletteSlot.DarkLine);

    /// <summary>Encadré sombre en creux (compteurs d'or, champs).</summary>
    public static StyleBoxFlat InsetBox() => Themed(new()
    {
        BgColor = _palette.InsetBg,
        BorderColor = _palette.InsetLine,
        BorderWidthTop = 2, BorderWidthLeft = 2,
        BorderWidthBottom = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
    }, PaletteSlot.InsetBg, PaletteSlot.InsetLine);

    /// <summary>Carte sombre thémée (InsetBox + marges internes) pour les
    /// lignes/cartes des fenêtres construites en code.</summary>
    public static StyleBoxFlat InsetCard(int mx, int my)
    {
        var sb = InsetBox();
        sb.BorderWidthTop = sb.BorderWidthBottom = 1;
        sb.BorderWidthLeft = sb.BorderWidthRight = 1;
        sb.ContentMarginLeft = sb.ContentMarginRight = mx;
        sb.ContentMarginTop = sb.ContentMarginBottom = my;
        return sb;
    }

    /// <summary>Onglet de panel (ref : pastilles grises à coins hauts
    /// arrondis, l'actif plus clair).</summary>
    public static StyleBoxFlat PanelTab(bool selected) => Themed(new()
    {
        BgColor = selected ? _palette.TabSel : _palette.Tab,
        BorderColor = _palette.TabLine,
        BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
    }, selected ? PaletteSlot.TabSel : PaletteSlot.Tab,
       PaletteSlot.TabLine);

    /// <summary>Bouton gris de bas de fenêtre (« Archive All »,
    /// « Confirm | Cancel »…).</summary>
    public static StyleBoxFlat GreyButton(bool hover = false) => Themed(new()
    {
        BgColor = hover ? _palette.GreyBtnHover : _palette.GreyBtn,
        BorderColor = _palette.GreyBtnLine,
        BorderWidthTop = 1, BorderWidthLeft = 1,
        BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
        CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = 6, ContentMarginRight = 6,
    }, hover ? PaletteSlot.GreyBtnHover : PaletteSlot.GreyBtn,
       PaletteSlot.GreyBtnLine);

    /// <summary>Applique le style bouton gris + texte clair.</summary>
    public static T Grey<T>(T b, int fontSize = 12) where T : Button
    {
        b.AddThemeStyleboxOverride("normal", GreyButton());
        b.AddThemeStyleboxOverride("hover", GreyButton(true));
        b.AddThemeStyleboxOverride("pressed", GreyButton(true));
        b.AddThemeStyleboxOverride("disabled", GreyButton());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeColorOverride("font_color", Color.Color8(232, 232, 228));
        b.AddThemeColorOverride("font_disabled_color", Color.Color8(130, 130, 126));
        return b;
    }

    private static Texture2D? _diamondOn, _diamondOff;

    /// <summary>Case à cocher en losange (ref : « jamais carré ») —
    /// 13×13 générée : contour gris, cœur bleu si cochée.</summary>
    private static readonly Dictionary<uint, Texture2D> _diamondCol = new();

    /// <summary>Losange coloré (arbre de quêtes : bleu en cours, vert
    /// à rendre, gris terminé).</summary>
    public static Texture2D DiamondColor(Color fill)
    {
        var key = fill.ToRgba32();
        if (_diamondCol.TryGetValue(key, out var t)) return t;
        const int n = 13;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var c = n / 2;
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Math.Abs(x - c) + Math.Abs(y - c);
                if (d > c) continue;
                img.SetPixel(x, y, d >= c - 1 ? Color.Color8(40, 40, 38)
                    : d <= c - 3 ? fill : fill.Lightened(0.6f));
            }
        return _diamondCol[key] = ImageTexture.CreateFromImage(img);
    }

    public static Texture2D Diamond(bool on)
    {
        if (on && _diamondOn != null) return _diamondOn;
        if (!on && _diamondOff != null) return _diamondOff;
        const int n = 13;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var c = n / 2;
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Math.Abs(x - c) + Math.Abs(y - c);
                if (d > c) continue;
                Color col = d >= c - 1 ? Color.Color8(70, 70, 66)
                    : on ? (d <= c - 3 ? Color.Color8(60, 140, 235)
                                       : Color.Color8(225, 235, 245))
                         : Color.Color8(238, 236, 230);
                img.SetPixel(x, y, col);
            }
        var tex = ImageTexture.CreateFromImage(img);
        if (on) _diamondOn = tex; else _diamondOff = tex;
        return tex;
    }

    /// <summary>Thème « panneau parchemin » (Settings…) : textes
    /// sombres, onglets DejaVu, losanges, listes/boutons gris.</summary>
    public static Theme PanelTheme(HudRoot? hud)
    {
        var t = new Theme();
        var dark = Color.Color8(30, 30, 28);
        t.SetColor("font_color", "Label", dark);
        t.SetFontSize("font_size", "Label", 11);
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            t.SetColor("font_color", type, dark);
            t.SetColor("font_pressed_color", type, dark);
            t.SetColor("font_hover_color", type, dark);
            t.SetColor("font_hover_pressed_color", type, dark);
            t.SetIcon("checked", type, Diamond(true));
            t.SetIcon("unchecked", type, Diamond(false));
            t.SetIcon("radio_checked", type, Diamond(true));
            t.SetIcon("radio_unchecked", type, Diamond(false));
            t.SetStylebox("normal", type, new StyleBoxEmpty());
            t.SetStylebox("hover", type, new StyleBoxEmpty());
            t.SetStylebox("pressed", type, new StyleBoxEmpty());
            t.SetStylebox("hover_pressed", type, new StyleBoxEmpty());
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetFontSize("font_size", type, 11);
            t.SetConstant("check_v_offset", type, 0);
        }
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            t.SetStylebox("normal", type, GreyButton());
            t.SetStylebox("hover", type, GreyButton(true));
            t.SetStylebox("pressed", type, GreyButton(true));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Color.Color8(232, 232, 228));
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetFontSize("font_size", type, 11);
        }
        // Tout vient de la palette courante (styleboxes « Themed » mutées
        // en place au changement de thème) — rien en dur.
        t.SetStylebox("panel", "TabContainer", ParchmentBox());
        t.SetStylebox("tab_selected", "TabContainer", PanelTab(true));
        t.SetStylebox("tab_unselected", "TabContainer", PanelTab(false));
        t.SetStylebox("tab_hovered", "TabContainer", PanelTab(true));
        t.SetStylebox("tab_focus", "TabContainer", new StyleBoxEmpty());
        t.SetColor("font_selected_color", "TabContainer", Color.Color8(40, 28, 64));
        t.SetColor("font_unselected_color", "TabContainer", Color.Color8(58, 34, 92));
        t.SetColor("font_hovered_color", "TabContainer", Color.Color8(20, 10, 40));
        t.SetFontSize("font_size", "TabContainer", 11);
        t.SetConstant("side_margin", "TabContainer", 0);
        // Listes déroulantes (OptionButton) et sliders lisibles.
        t.SetStylebox("panel", "PopupMenu", InsetBox());
        t.SetStylebox("hover", "PopupMenu", GreyButton(true));
        t.SetColor("font_color", "PopupMenu", Color.Color8(232, 232, 228));
        t.SetFontSize("font_size", "PopupMenu", 11);
        t.SetStylebox("slider", "HSlider", new StyleBoxFlat
        {
            BgColor = _palette.InsetBg, ContentMarginTop = 2,
            ContentMarginBottom = 2,
        });
        t.SetStylebox("grabber_area", "HSlider", new StyleBoxFlat
            { BgColor = Color.Color8(60, 140, 235) });
        t.SetStylebox("grabber_area_highlight", "HSlider", new StyleBoxFlat
            { BgColor = Color.Color8(90, 165, 245) });
        return t;
    }
}
