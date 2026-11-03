using Godot;

namespace EdenClient.Screens;

/// <summary>Styles communs aux écrans hand-built : bouton sombre à
/// liseré (palette EE — gold pour les actions principales) au lieu
/// du style Godot par défaut.</summary>
public static class ScreenStyle
{
    public static void DarkButton(Button b, Color? border = null)
    {
        StyleBoxFlat S(Color bg) => new()
        {
            BgColor = bg,
            BorderColor = border ?? new Color(0.45f, 0.45f, 0.43f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        b.AddThemeStyleboxOverride("normal",
            S(new Color(0.18f, 0.18f, 0.17f)));
        b.AddThemeStyleboxOverride("hover",
            S(new Color(0.30f, 0.30f, 0.28f)));
        b.AddThemeStyleboxOverride("pressed",
            S(new Color(0.12f, 0.12f, 0.11f)));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color",
            new Color(0.96f, 0.94f, 0.87f));
    }

    /// <summary>Liseré doré des actions principales (Confirm/Create).</summary>
    public static readonly Color GoldBorder = new(0.86f, 0.7f, 0.39f);
}
