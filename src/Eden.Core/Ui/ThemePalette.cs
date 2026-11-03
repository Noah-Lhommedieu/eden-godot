namespace EdenClient.Ui;

/// <summary>Couleur RGBA flottante (0..1) — indépendante du moteur.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static Rgba From8(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
}

/// <summary>
/// Palette de thème (11 thèmes EDENFR : Irongray + 10 jeux
/// <c>uidata/imagesets/&lt;thème&gt;/</c>) — port de
/// <c>ThemePalette.cs</c> Godot. Dérive d'Irongray en déplaçant chaque
/// couleur de la différence entre l'échantillon du thème et celui du
/// thème de base (<see cref="ThemePresets"/>, généré par
/// <c>tools/gen_theme_palettes.py</c>).
/// </summary>
public sealed class ThemePalette
{
    public Rgba WindowBody, TitleBand, Parchment, DarkLine,
        InsetBg, InsetLine, Tab, TabSel, TabLine,
        GreyBtn, GreyBtnHover, GreyBtnLine;

    public static ThemePalette Irongray => new()
    {
        WindowBody = Rgba.From8(47, 45, 40),
        TitleBand = Rgba.From8(27, 26, 24),
        Parchment = Rgba.From8(245, 241, 233),
        DarkLine = Rgba.From8(94, 89, 79),
        InsetBg = Rgba.From8(40, 39, 35),
        InsetLine = Rgba.From8(22, 22, 20),
        Tab = Rgba.From8(84, 82, 77),
        TabSel = Rgba.From8(108, 106, 100),
        TabLine = Rgba.From8(140, 138, 130),
        GreyBtn = Rgba.From8(52, 51, 46),
        GreyBtnHover = Rgba.From8(70, 68, 62),
        GreyBtnLine = Rgba.From8(112, 110, 102),
    };

    /// <summary>Indices dans <see cref="ThemePresets.Samples"/>.</summary>
    private const int Static = 0, Listbox = 1, Titlebar = 2, Btn = 3,
        BtnHover = 4, TabN = 5, TabS = 6;

    private static Rgba Shift(Rgba c, (byte R, byte G, byte B) px, (byte R, byte G, byte B) b) =>
        new(Math.Clamp(c.R + (px.R - b.R) / 255f, 0f, 1f),
            Math.Clamp(c.G + (px.G - b.G) / 255f, 0f, 1f),
            Math.Clamp(c.B + (px.B - b.B) / 255f, 0f, 1f), c.A);

    /// <summary>Palette du thème <paramref name="name"/> (insensible à la casse) ;
    /// thème inconnu → Irongray.</summary>
    public static ThemePalette For(string name)
    {
        var p = Irongray;
        var key = ThemePresets.Names.FirstOrDefault(n =>
            string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (key == null || key == "Irongray") return p;
        var s = ThemePresets.Samples[key];
        var b = ThemePresets.Samples["Irongray"];
        p.WindowBody = Shift(p.WindowBody, s[Static], b[Static]);
        p.InsetBg = Shift(p.InsetBg, s[Static], b[Static]);
        p.InsetLine = Shift(p.InsetLine, s[Static], b[Static]);
        p.Parchment = Shift(p.Parchment, s[Listbox], b[Listbox]);
        p.DarkLine = Shift(p.DarkLine, s[Listbox], b[Listbox]);
        p.TitleBand = Shift(p.TitleBand, s[Titlebar], b[Titlebar]);
        p.GreyBtn = Shift(p.GreyBtn, s[Btn], b[Btn]);
        p.GreyBtnLine = Shift(p.GreyBtnLine, s[Btn], b[Btn]);
        p.GreyBtnHover = Shift(p.GreyBtnHover, s[BtnHover], b[BtnHover]);
        p.Tab = Shift(p.Tab, s[TabN], b[TabN]);
        p.TabLine = Shift(p.TabLine, s[TabN], b[TabN]);
        p.TabSel = Shift(p.TabSel, s[TabS], b[TabS]);
        return p;
    }
}
