using Avalonia.Controls;
using Limitless.Studio.ViewModels;

namespace Limitless.Studio.Views;

public partial class IconsView : UserControl
{
    public IconsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireClipboard();
        AttachedToVisualTree += (_, _) => WireClipboard();
    }

    /// <summary>Le presse-papier vit côté vue (TopLevel.Clipboard) —
    /// on l'injecte dans le VM pour le bouton « Copy code ».</summary>
    private void WireClipboard()
    {
        if (DataContext is IconsViewModel vm)
            vm.CopyText = text =>
                TopLevel.GetTopLevel(this)?.Clipboard?
                    .SetTextAsync(text)
                ?? System.Threading.Tasks.Task.CompletedTask;
    }
}
