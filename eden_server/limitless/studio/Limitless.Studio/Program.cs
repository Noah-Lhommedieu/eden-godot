using Avalonia;
using System;

namespace Limitless.Studio;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    /// <summary>Page de démarrage via `--page=items|monsters|icons|
    /// ini|assets` (deep-link pratique pour les captures/tests).</summary>
    public static string? StartupPage;

    /// <summary>Aperçu de la fenêtre d'aide d'un champ au démarrage
    /// via `--help=S_Item.ini:12` — ouvre directement le panneau de
    /// référence de la colonne (utile pour écrire/vérifier la doc
    /// sans naviguer dans l'UI).</summary>
    public static string? StartupHelp;

    [STAThread]
    public static void Main(string[] args)
    {
        foreach (var a in args)
        {
            if (a.StartsWith("--page="))
                StartupPage = a[7..];
            else if (a.StartsWith("--help="))
                StartupHelp = a[7..];
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
