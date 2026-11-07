using System;
using System.IO;

namespace Limitless.Studio.Services;

/// <summary>Contexte partagé des pages — chemins résolus une fois
/// (GamePaths), services communs (ini, icônes, traductions EN),
/// canal de statut vers la barre d'état du shell.</summary>
public sealed class StudioContext
{
    public string RepoRoot { get; } = GamePaths.RepoRoot();
    public string IniDir { get; } = GamePaths.IniDir();
    public string ClientDir { get; } = GamePaths.ClientDir();
    public string PkgDir { get; } = GamePaths.PkgDir();
    public string UiDir { get; } = GamePaths.UiDir();
    public string ClientDbDir { get; } = GamePaths.ClientDbDir();

    public IniService Ini { get; } = new();
    public IniWriter IniWriter { get; } = new();
    public TranslationCatalog Trans { get; }
    public IconService Icons { get; }
    /// <summary>Valeurs réelles distribuées par colonne — la
    /// section « observed » des fenêtres d'aide de champs.</summary>
    public IniColumnStats ColumnStats { get; }

    /// <summary>Écrit dans la barre de statut du shell.</summary>
    public Action<string> Status { get; set; } = _ => { };

    public bool IniDirExists => Directory.Exists(IniDir);
    public bool ClientDirExists => Directory.Exists(ClientDir);
    public bool UiDirExists => Directory.Exists(UiDir);

    public StudioContext()
    {
        Trans = new TranslationCatalog(ClientDbDir);
        Icons = new IconService(UiDir, PkgDir);
        ColumnStats = new IniColumnStats(Ini, Trans);
    }
}
