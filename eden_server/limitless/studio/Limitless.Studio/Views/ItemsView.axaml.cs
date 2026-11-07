using Avalonia.Controls;
using Limitless.Studio.Models;
using Limitless.Studio.ViewModels;

namespace Limitless.Studio.Views;

public partial class ItemsView : UserControl
{
    public ItemsView()
    {
        InitializeComponent();
        // Icônes lazy : seules les lignes réalisées par la
        // virtualisation demandent leur bitmap (couverture complète
        // des ~20k items sans pré-chargement massif).
        ItemList.ContainerPrepared += (_, e) =>
        {
            if (e.Container.DataContext is GameItem it
                && DataContext is ItemsViewModel vm)
                vm.EnsureIcon(it);
        };
    }
}
