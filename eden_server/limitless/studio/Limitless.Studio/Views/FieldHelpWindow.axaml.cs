using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Limitless.Studio.ViewModels;

namespace Limitless.Studio.Views;

/// <summary>Fenêtre de référence « ? » d'un champ de formulaire —
/// instance unique réutilisée : un clic sur un autre « ? » met à
/// jour le contenu au lieu d'empiler les fenêtres.</summary>
public partial class FieldHelpWindow : Window
{
    private static FieldHelpWindow? _instance;

    public FieldHelpWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _instance = null;
    }

    /// <summary>Ouvre (ou recycle) la fenêtre d'aide pour le champ
    /// `row`. Non modale — l'utilisateur peut éditer le champ en
    /// gardant la référence ouverte.</summary>
    public static void Present(IniFieldRow row)
    {
        var stats = row.Ctx?.ColumnStats ?? FallbackStats();
        var vm = new FieldHelpViewModel(row.TableFile, row.Column,
            row.Value, stats);
        var win = _instance ??= new FieldHelpWindow();
        win.DataContext = vm;
        win.Title = $"? {row.TableFile} — {row.Label}";
        if (!win.IsVisible)
        {
            if (Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime d
                && d.MainWindow != null)
                win.Show(d.MainWindow);
            else
                win.Show();
        }
        win.Activate();
    }

    private static Services.IniColumnStats? _fallbackStats;

    /// <summary>Stats de secours quand le champ n'a pas de
    /// contexte — catalogue de traduction paresseux, léger.</summary>
    private static Services.IniColumnStats FallbackStats() =>
        _fallbackStats ??= new Services.IniColumnStats(
            new Services.IniService(),
            new Services.TranslationCatalog(
                Services.GamePaths.ClientDbDir()));

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
