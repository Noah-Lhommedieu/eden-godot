using Avalonia.Controls;
using Limitless.Studio.Models;
using Limitless.Studio.ViewModels;

namespace Limitless.Studio.Views;

public partial class MonstersView : UserControl
{
    public MonstersView()
    {
        InitializeComponent();
        // Icônes lazy — une ligne réalisée = une icône décodée.
        MonsterList.ContainerPrepared += (_, e) =>
        {
            if (e.Container.DataContext is GameMonster m
                && DataContext is MonstersViewModel vm)
                vm.EnsureIcon(m);
        };
    }
}
