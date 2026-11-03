using Godot;

namespace EdenClient.Screens;

/// <summary>
/// Style commun des écrans pré-jeu (login, channel, sélection) :
/// panneaux verre sombre à liseré doré fin (proche des cadres de
/// l'ancien Eden : gris anthracite + liseré clair), boutons
/// primaire/secondaire, champs de saisie, vignette. Une seule source
/// pour que les trois écrans restent cohérents.
/// </summary>
public static class PreGameStyle
{
    public static readonly Color Gold = new(0.93f, 0.84f, 0.58f);
    public static readonly Color Ink = new(0.96f, 0.95f, 0.90f);
    public static readonly Color Dim = new(0.74f, 0.76f, 0.82f);
    public static readonly Color Err = new(1f, 0.42f, 0.38f);
    public static readonly Color Ok = new(0.55f, 0.88f, 0.55f);

    private static StyleBoxFlat Box(Color bg, Color border, int bw,
        int radius, int margin = 0)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = bw, BorderWidthRight = bw,
            BorderWidthTop = bw, BorderWidthBottom = bw,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = margin, ContentMarginRight = margin,
            ContentMarginTop = margin, ContentMarginBottom = margin,
        };
        return s;
    }

    /// <summary>Panneau verre sombre + liseré doré + ombre douce.</summary>
    public static StyleBoxFlat Panel(float alpha = 0.86f, int margin = 22)
    {
        var s = Box(new Color(0.055f, 0.06f, 0.08f, alpha),
            new Color(0.78f, 0.68f, 0.42f, 0.85f), 1, 10, margin);
        s.ShadowColor = new Color(0, 0, 0, 0.55f);
        s.ShadowSize = 18;
        return s;
    }

    /// <summary>Bouton : primaire = doré plein, sinon anthracite.</summary>
    public static void Button(Button b, bool primary = false,
        int fontSize = 15)
    {
        var bg = primary ? new Color(0.62f, 0.50f, 0.22f)
                         : new Color(0.16f, 0.16f, 0.17f, 0.92f);
        var hov = primary ? new Color(0.76f, 0.62f, 0.28f)
                          : new Color(0.27f, 0.27f, 0.28f, 0.96f);
        var bd = primary ? new Color(0.96f, 0.86f, 0.55f)
                         : new Color(0.50f, 0.50f, 0.50f);
        b.AddThemeStyleboxOverride("normal", Box(bg, bd, 1, 6, 8));
        b.AddThemeStyleboxOverride("hover", Box(hov, Gold, 1, 6, 8));
        b.AddThemeStyleboxOverride("pressed",
            Box(bg * 0.8f, Gold, 1, 6, 8));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("disabled",
            Box(new Color(0.12f, 0.12f, 0.12f, 0.8f),
                new Color(0.3f, 0.3f, 0.3f), 1, 6, 8));
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeFontSizeOverride("font_size", fontSize);
    }

    /// <summary>Champ de saisie sombre, liseré doré au focus.</summary>
    public static void Field(LineEdit e)
    {
        e.AddThemeStyleboxOverride("normal",
            Box(new Color(0.02f, 0.02f, 0.03f, 0.85f),
                new Color(0.42f, 0.42f, 0.44f), 1, 5, 9));
        e.AddThemeStyleboxOverride("focus",
            Box(new Color(0.03f, 0.03f, 0.04f, 0.95f), Gold, 1, 5, 9));
        e.AddThemeColorOverride("font_color", Ink);
        e.AddThemeColorOverride("font_placeholder_color",
            new Color(0.5f, 0.52f, 0.58f));
        e.AddThemeColorOverride("caret_color", Gold);
        e.AddThemeFontSizeOverride("font_size", 15);
    }

    /// <summary>Séparateur doré dégradé (titre de fenêtre).</summary>
    public static Control Rule()
    {
        var r = new ColorRect
        {
            Color = new Color(0.78f, 0.68f, 0.42f, 0.55f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        return r;
    }

    public static Label Text(string t, int size, Color c,
        HorizontalAlignment h = HorizontalAlignment.Left)
    {
        var l = new Label { Text = t, HorizontalAlignment = h,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_outline_color",
            new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("outline_size", 3);
        return l;
    }

    /// <summary>Vignette radiale (assombrit les bords) pour fond fixe
    /// ou 3D — texture générée, aucune ressource disque.</summary>
    public static TextureRect Vignette()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0, 0, 0, 0));
        g.SetColor(1, new Color(0, 0, 0, 0.62f));
        g.SetOffset(0, 0.45f);
        g.SetOffset(1, 1.0f);
        var t = new GradientTexture2D
        {
            Gradient = g,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
            Width = 256, Height = 256,
        };
        var r = new TextureRect
        {
            Texture = t,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        r.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return r;
    }
}
