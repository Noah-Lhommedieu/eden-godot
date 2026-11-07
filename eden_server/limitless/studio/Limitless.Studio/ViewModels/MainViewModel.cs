using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>Shell — nav latérale + page courante + barre de statut.
/// `SelectedNavItem` est le SukiSideMenuItem cliqué (sa `Tag`
/// identifie la page : "items", "monsters", "icons", "ini",
/// "assets"). Le titre de fenêtre embarque l'horodatage du binaire
/// pour confirmer qu'on lance le build courant.</summary>
public partial class MainViewModel : ViewModelBase
{
    public StudioContext Ctx { get; } = new();

    public ItemsViewModel Items { get; }
    public MonstersViewModel Monsters { get; }
    public IconsViewModel Icons { get; }
    public IniFilesViewModel IniFiles { get; }
    public AssetsViewModel Assets { get; }

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private object? _selectedNavItem;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string _pageTitle = "";
    [ObservableProperty] private string _pageSubtitle = "";

    /// <summary>Horodatage du binaire courant — preuve P5.1 que
    /// l'app lancée = le code actuel.</summary>
    public string BuildStamp =>
        File.GetLastWriteTime(Path.Combine(
            AppContext.BaseDirectory, "Limitless.Studio.dll"))
            .ToString("yyyy-MM-dd HH:mm");

    public string RepoPath => Ctx.RepoRoot;

    /// <summary>Icônes de nav/logo = vraies icônes du jeu décodées
    /// des assets extraits (audace visuelle du design). Null →
    /// glyphe PathIcon de repli dans la vue.</summary>
    public Avalonia.Media.Imaging.Bitmap? NavLogo { get; }
    public Avalonia.Media.Imaging.Bitmap? NavIconItems { get; }
    public Avalonia.Media.Imaging.Bitmap? NavIconMonsters { get; }
    public Avalonia.Media.Imaging.Bitmap? NavIconIcons { get; }
    public Avalonia.Media.Imaging.Bitmap? NavIconIni { get; }
    public Avalonia.Media.Imaging.Bitmap? NavIconAssets { get; }

    public MainViewModel()
    {
        Ctx.Status = s => Status = s;
        Items = new ItemsViewModel(Ctx);
        Monsters = new MonstersViewModel(Ctx);
        Icons = new IconsViewModel(Ctx);
        IniFiles = new IniFilesViewModel(Ctx);
        Assets = new AssetsViewModel(Ctx);
        NavLogo = Ctx.Icons.Get("Q00134");
        NavIconItems = Ctx.Icons.Get("Q00134");
        NavIconMonsters = Ctx.Icons.Get("M312");
        NavIconIcons = Ctx.Icons.Get("A00001");
        NavIconIni = Ctx.Icons.Get("9EB1B");
        NavIconAssets = Ctx.Icons.Get("A00002");
    }

    /// <summary>Sélection latérale → page + activation paresseuse.</summary>
    partial void OnSelectedNavItemChanged(object? value)
    {
        var tag = (value as Avalonia.Controls.Control)?.Tag
            as string;
        Select(tag);
    }

    /// <summary>Bascule sur la page `tag` et l'active.</summary>
    public void Select(string? tag)
    {
        var page = tag switch
        {
            "monsters" => (PageViewModel)Monsters,
            "icons" => Icons,
            "ini" => IniFiles,
            "assets" => Assets,
            _ => Items,
        };
        CurrentPage = page;
        PageTitle = page.Title;
        PageSubtitle = page.Subtitle;
        page.Activate();
    }
}
