using Limitless.Studio.ViewModels;
using SukiUI.Controls;

namespace Limitless.Studio.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // Sélection par défaut — si le binding SelectedItem n'a pas
        // encore propagé le Tag « items », on force la page.
        Opened += (_, _) =>
        {
            if (DataContext is MainViewModel vm
                && vm.CurrentPage is null)
                vm.Select("items");
        };
    }
}
