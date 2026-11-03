using Godot;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// Palette chromée du thème courant (M1) : les couleurs des surfaces
/// hand-built (corps de fenêtre, parchemin, onglets, boutons, encadrés
/// en creux) suivent le thème comme dans le client natif.
///
/// Méthode : nos couleurs de base sont déjà calées sur les ref-shots
/// Irongray ; pour chaque rôle on applique le même delta que la région
/// native correspondante de l'atlas DejaVu thémé (pixel central —
/// <see cref="UiTextureCache.SamplePixel"/>). Irongray (atlas de base)
/// → delta nul, couleurs inchangées. Coffee partage l'atlas de base
/// dans les assets copiés → même rendu qu'Irongray, comme le natif.
/// </summary>
public enum PaletteSlot
{
    None, WindowBody, TitleBand, Parchment, DarkLine,
    InsetBg, InsetLine, Tab, TabSel, TabLine,
    GreyBtn, GreyBtnHover, GreyBtnLine,
}

public sealed class ThemePalette
{
    public Color WindowBody, TitleBand, Parchment, DarkLine,
        InsetBg, InsetLine, Tab, TabSel, TabLine,
        GreyBtn, GreyBtnHover, GreyBtnLine;

    /// <summary>Couleurs de base (Irongray) — celles calées sur les
    /// ref-shots ; point de départ des deltas par thème.</summary>
    public static readonly ThemePalette Irongray = new()
    {
        WindowBody = Color.Color8(47, 45, 40),
        TitleBand = Color.Color8(27, 26, 24),
        Parchment = Color.Color8(245, 241, 233),
        DarkLine = Color.Color8(94, 89, 79),
        InsetBg = Color.Color8(40, 39, 35),
        InsetLine = Color.Color8(22, 22, 20),
        Tab = Color.Color8(84, 82, 77),
        TabSel = Color.Color8(108, 106, 100),
        TabLine = Color.Color8(140, 138, 130),
        GreyBtn = Color.Color8(52, 51, 46),
        GreyBtnHover = Color.Color8(70, 68, 62),
        GreyBtnLine = Color.Color8(112, 110, 102),
    };

    public ThemePalette Clone() => (ThemePalette)MemberwiseClone();

    public Color ColorOf(PaletteSlot s) => s switch
    {
        PaletteSlot.WindowBody => WindowBody,
        PaletteSlot.TitleBand => TitleBand,
        PaletteSlot.Parchment => Parchment,
        PaletteSlot.DarkLine => DarkLine,
        PaletteSlot.InsetBg => InsetBg,
        PaletteSlot.InsetLine => InsetLine,
        PaletteSlot.Tab => Tab,
        PaletteSlot.TabSel => TabSel,
        PaletteSlot.TabLine => TabLine,
        PaletteSlot.GreyBtn => GreyBtn,
        PaletteSlot.GreyBtnHover => GreyBtnHover,
        PaletteSlot.GreyBtnLine => GreyBtnLine,
        _ => Colors.White,
    };

    /// <summary>Pixel central de chaque région dans l'atlas DejaVu de
    /// base (mesuré une fois — l'asset ne change pas). Sert de zéro
    /// auquel le delta du thème actif se rapporte.</summary>
    private static readonly Dictionary<string, Color> _basePx = new()
    {
        ["StaticBackdrop"] = Color.Color8(24, 23, 20),
        ["ListboxBackdrop"] = Color.Color8(246, 242, 234),
        ["NewTitlebarMiddle"] = Color.Color8(78, 76, 72),
        ["ButtonNormal"] = Color.Color8(50, 48, 43),
        ["ButtonHover"] = Color.Color8(64, 63, 58),
        ["TabButtonMiddleNormal"] = Color.Color8(74, 72, 68),
        ["TabButtonMiddleSelected"] = Color.Color8(95, 94, 90),
    };

    /// <summary>Palette dérivée de l'atlas thémé chargé dans
    /// <paramref name="tex"/> (Irongray si l'appelant passe le cache
    /// non thémé — le delta est alors nul).</summary>
    public static ThemePalette FromAtlas(UiTextureCache tex)
    {
        var p = Irongray.Clone();
        Shift(tex, "StaticBackdrop", ref p.WindowBody);
        Shift(tex, "StaticBackdrop", ref p.InsetBg);
        Shift(tex, "StaticBackdrop", ref p.InsetLine);
        Shift(tex, "ListboxBackdrop", ref p.Parchment);
        Shift(tex, "ListboxBackdrop", ref p.DarkLine);
        Shift(tex, "NewTitlebarMiddle", ref p.TitleBand);
        Shift(tex, "ButtonNormal", ref p.GreyBtn);
        Shift(tex, "ButtonNormal", ref p.GreyBtnLine);
        Shift(tex, "ButtonHover", ref p.GreyBtnHover);
        Shift(tex, "TabButtonMiddleNormal", ref p.Tab);
        Shift(tex, "TabButtonMiddleNormal", ref p.TabLine);
        Shift(tex, "TabButtonMiddleSelected", ref p.TabSel);
        return p;
    }

    /// <summary>Décale <paramref name="c"/> du delta
    /// pixel_thème − pixel_base mesuré sur la région
    /// <paramref name="region"/> — inchangé si l'atlas ou la région
    /// ne se résout pas.</summary>
    private static void Shift(UiTextureCache tex, string region,
        ref Color c)
    {
        if (tex.SamplePixel(new CeguiImageRef("DejaVu", region))
                is not { } px || !_basePx.TryGetValue(region, out var b))
            return;
        c = new Color(
            Mathf.Clamp(c.R + px.R - b.R, 0f, 1f),
            Mathf.Clamp(c.G + px.G - b.G, 0f, 1f),
            Mathf.Clamp(c.B + px.B - b.B, 0f, 1f), c.A);
    }
}
