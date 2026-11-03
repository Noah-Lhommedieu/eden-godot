namespace EdenClient.Ui;

/// <summary>Noyau pur du thème UI au démarrage : un nom sauvegardé
/// (casse libre, espaces, vide) devient toujours un nom canonique de
/// <see cref="Settings.ClientSettings.ThemeNames"/>, jamais un
/// dossier inexistant (sensible à la casse sous Linux).</summary>
public static class UiThemeBoot
{
    public static string Normalize(string? theme)
    {
        var t = (theme ?? "").Trim();
        foreach (var n in Settings.ClientSettings.ThemeNames)
            if (n.Equals(t, StringComparison.OrdinalIgnoreCase)) return n;
        return Settings.ClientSettings.ThemeNames[0];
    }

    /// <summary>true tant que le thème ne peut pas être posé : pas de
    /// HUD ou atlas non construit. Les appelants réessaient ensuite
    /// (HudReady / poll) au lieu d'abandonner.</summary>
    public static bool MustWait(bool hudExists, bool atlasReady) =>
        !hudExists || !atlasReady;
}
