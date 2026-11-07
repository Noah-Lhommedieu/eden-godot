using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;

namespace Limitless.Studio.Models;

/// <summary>Item du jeu (S_Item.ini : col0=id, col1=icône `Q00134`,
/// col3=modèle `G00005`, col7=nom Big5). `Name` = traduction EN de
/// t_item.ini quand elle existe, sinon le nom Big5 préfixé
/// « [untranslated] » (design : jamais de Big5 brut).</summary>
public partial class GameItem : ObservableObject
{
    public int Id { get; set; }
    public string IconCode { get; set; } = "";
    public string ModelCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameBig5 { get; set; } = "";
    public int ModelId { get; set; }
    public string Raw { get; set; } = "";
    public string Display => $"{Id}  {Name}";
    /// <summary>Icône décodée (chargée en tâche de fond).</summary>
    [ObservableProperty] private Bitmap? _icon;
}

/// <summary>Monstre (S_Monster.ini : col0=id, col1=code modèle
/// `M312040`, col2=nom Big5). Icône = `uiicon/{base}.dds`.</summary>
public partial class GameMonster : ObservableObject
{
    public int Id { get; set; }
    public string IconCode { get; set; } = "";
    public string ModelCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameBig5 { get; set; } = "";
    public int ModelId { get; set; }
    public int Level { get; set; }
    public long Hp { get; set; }
    public string Raw { get; set; } = "";
    public string Display => $"{Id}  {Name}";
    [ObservableProperty] private Bitmap? _icon;
}
