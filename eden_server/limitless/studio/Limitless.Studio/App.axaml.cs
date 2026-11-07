using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Limitless.Studio.ViewModels;
using Limitless.Studio.Views;
using SukiUI;
using SukiUI.Models;

namespace Limitless.Studio;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Thème base Dark forcé (RequestedThemeVariant suffit, mais
        // on verrouille via l'API SukiUI pour éviter tout héritage
        // du thème système) + color theme « Eden » maison :
        // Primary = eden-gold, Accent = aurora (DESIGN-WEB.md).
        var theme = SukiTheme.GetInstance();
        theme.ChangeBaseTheme(ThemeVariant.Dark);
        var eden = new SukiColorTheme("Eden",
            Color.Parse("#F2C04D"), Color.Parse("#7BE3C8"));
        theme.AddColorTheme(eden);
        theme.ChangeColorTheme(eden);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            // --page=X : appliqué à l'ouverture pour passer après la
            // sélection initiale « items » du menu latéral.
            if (Program.StartupPage is { } p)
                desktop.MainWindow.Opened += (_, _) => vm.Select(p);
            // --help=S_X.ini:N : ouvre la fenêtre de référence du
            // champ directement au démarrage (aperçu de la doc).
            if (Program.StartupHelp is { } spec)
                desktop.MainWindow.Opened +=
                    (_, _) => ShowFieldHelpPreview(vm, spec);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Ouvre la fenêtre d'aide sur un champ réel —
    /// `--help=S_Item.ini:12` prend la 1re valeur non vide du
    /// fichier pour que l'aperçu soit représentatif.</summary>
    private static void ShowFieldHelpPreview(MainViewModel vm,
        string spec)
    {
        var sep = spec.LastIndexOf(':');
        if (sep <= 0 || !int.TryParse(spec[(sep + 1)..], out var colIdx))
            return;
        var file = spec[..sep];
        var schema = Services.IniSchemas.For(file);
        var col = System.Linq.Enumerable.FirstOrDefault(
            schema.Columns, c => c.Index == colIdx)
            ?? new Services.IniColumn(colIdx, $"col {colIdx}",
                "Undocumented column — check the table parser for " +
                "its meaning.");
        var value = "";
        var path = System.IO.Path.Combine(vm.Ctx.IniDir, file);
        foreach (var rec in vm.Ctx.Ini.Records(path))
        {
            if (rec.Cols.Length > colIdx
                && rec.Cols[colIdx].Length > 0)
            { value = rec.Cols[colIdx]; break; }
        }
        Views.FieldHelpWindow.Present(
            new ViewModels.IniFieldRow(col, value, vm.Ctx, file));
    }
}
