using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>Page INI files — éditeur piloté par schéma : chaque
/// table .ini connue (IniSchemas, construit depuis les parsers C#
/// du serveur) affiche des champs nommés/groupés au lieu de la ligne
/// brute ; les colonnes non cartographiées restent éditables dans
/// « Other columns ». La ligne raw reste visible en repliable et
/// déblocable pour l'édition directe. Le panneau « copies » audite
/// les .ini présents côté serveur ET client (diff = risque
/// d'affichage incohérent).</summary>
public partial class IniFilesViewModel : PageViewModel
{

    /// <summary>Une ligne de données d'un .ini — colonnes `|`/`,`
    /// + nom EN résolu (T_*.ini) quand la table le permet.</summary>
    public sealed partial class IniRow : ObservableObject
    {
        public int Index { get; set; }
        public string FirstCol { get; set; } = "";
        public string EnName { get; set; } = "";
        public string[] Cols { get; set; } = System.Array.Empty<string>();
        [ObservableProperty] private string _raw = "";
        public string Display => EnName.Length > 0
            ? $"{FirstCol}  {EnName}"
            : $"row {Index} — {FirstCol}";
        /// <summary>Force le rafraîchissement de Display après
        /// édition d'EnName.</summary>
        public void RefreshDisplay() =>
            OnPropertyChanged(nameof(Display));
    }

    /// <summary>Un fichier .ini présent à plusieurs endroits
    /// (serveur db_decoded + client data/db).</summary>
    public sealed class ConflictRow
    {
        public string Name { get; set; } = "";
        public int Copies { get; set; }
        public bool Diverged { get; set; }
        public string Paths { get; set; } = "";
        public string Display => Diverged
            ? $"{Name} — {Copies} copies, DIFFERENT content"
            : $"{Name} — {Copies} identical copies";
    }

    public IniFilesViewModel(StudioContext ctx) : base(ctx) { }

    public override string Title => "INI files";
    public override string Subtitle =>
        "Schema-driven server .ini editor — named fields, EN names";

    public ObservableCollection<string> IniFiles { get; } = new();
    public ObservableCollection<IniRow> IniRows { get; } = new();
    public ObservableCollection<ConflictRow> Conflicts { get; } = new();
    public ObservableCollection<IniFieldGroup> FieldGroups { get; } =
        new();

    /// <summary>Étendue de la liste de fichiers : "S" = tables de
    /// données serveur (défaut — c'est ce qu'on édite), "T" =
    /// fichiers de libellés Big5/EN, "all" = les 160+ fichiers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScopeS))]
    [NotifyPropertyChangedFor(nameof(IsScopeT))]
    [NotifyPropertyChangedFor(nameof(IsScopeAll))]
    private string _fileScope = "S";

    public bool IsScopeS => FileScope == "S";
    public bool IsScopeT => FileScope == "T";
    public bool IsScopeAll => FileScope == "all";

    [ObservableProperty] private string? _selectedIniFile;
    [ObservableProperty] private IniRow? _selectedIniRow;
    [ObservableProperty] private string _fileFilter = "";
    [ObservableProperty] private string _rowFilter = "";
    [ObservableProperty] private string _editRaw = "";
    [ObservableProperty] private bool _rawMode;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _schemaTitle = "";
    [ObservableProperty] private string _schemaPurpose = "";
    [ObservableProperty] private string _impactText = "";
    [ObservableProperty] private string _selectedRowCaption = "";
    [ObservableProperty] private string _conflictSummary =
        "Scan not run — compares server db_decoded against the " +
        "client's data/db copies.";

    private IniTableSchema _schema = IniSchemas.Generic("");

    protected override void OnFirstActivate()
    {
        LoadIniFiles();
        ScanConflicts();
    }

    /// <summary>Liste les .ini de db_decoded — scopés (S_ = tables
    /// de données, T_ = libellés, all = tout) + filtre texte. Les
    /// S_/T_ d'un même nom restent deux fichiers distincts.</summary>
    [RelayCommand]
    private void LoadIniFiles()
    {
        IniFiles.Clear();
        if (!Ctx.IniDirExists)
        {
            EmptyMessage =
                $"Ini folder missing: {Ctx.IniDir}";
            IsEmpty = true;
            Status(EmptyMessage);
            return;
        }
        bool ScopeMatch(string n) => FileScope switch
        {
            "S" => n.StartsWith("S_", StringComparison.OrdinalIgnoreCase),
            "T" => n.StartsWith("T_", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
        foreach (var f in Directory.GetFiles(Ctx.IniDir, "*.ini")
                     .Select(Path.GetFileName)
                     .Where(n => n != null && ScopeMatch(n))
                     .Where(n => string.IsNullOrWhiteSpace(FileFilter)
                         || n!.Contains(FileFilter,
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x))
            IniFiles.Add(f!);
        IsEmpty = IniFiles.Count == 0;
        EmptyMessage = IsEmpty
            ? $"No .ini files match \"{FileFilter}\" in this scope."
            : "";
        Status($"{IniFiles.Count} .ini files " +
               $"(scope {(FileScope == "all" ? "all" : FileScope + "_*")})");
    }

    /// <summary>Bascule d'étendue — boutons S_/T_/All de la vue.</summary>
    [RelayCommand]
    private void SetFileScope(string scope)
    {
        FileScope = scope;
        LoadIniFiles();
    }

    [RelayCommand]
    private void SearchFiles() => LoadIniFiles();

    [RelayCommand]
    private void SearchRows() => LoadRows();

    /// <summary>Audit des doublons serveur/client — un .ini lu des
    /// deux côtés dont le contenu diffère affiche des valeurs
    /// différentes en jeu vs ce que le serveur utilise.</summary>
    [RelayCommand]
    private void ScanConflicts()
    {
        Conflicts.Clear();
        var found = IniSourceService.FindConflicts(
            Ctx.IniDir, Ctx.ClientDbDir);
        // Divergés d'abord — ce sont les seuls qui causent des
        // incohérences serveur/client (le reste est informationnel).
        foreach (var c in found.OrderByDescending(x =>
                     x.Copies.Select(k => k.Sha256).Distinct().Count()))
        {
            var diverged = c.Copies
                .Select(x => x.Sha256).Distinct().Count() > 1;
            Conflicts.Add(new ConflictRow
            {
                Name = c.Name, Copies = c.Copies.Count,
                Diverged = diverged,
                Paths = string.Join("\n",
                    c.Copies.Select(x => x.Path)),
            });
        }
        var div = Conflicts.Count(c => c.Diverged);
        ConflictSummary = found.Count == 0
            ? "No .ini exists in both the server and client folders."
            : $"{found.Count} files exist on both sides — " +
              $"{div} differ in content (client shows different " +
              "data than the server uses).";
        Status(ConflictSummary);
    }

    partial void OnSelectedIniFileChanged(string? value)
    {
        IniRows.Clear();
        SelectedIniRow = null;
        FieldGroups.Clear();
        EditRaw = "";
        RawMode = false;
        if (value == null)
        {
            SchemaTitle = "";
            SchemaPurpose = "";
            ImpactText = "";
            return;
        }
        _schema = IniSchemas.For(value);
        SchemaTitle = $"{_schema.Title} — {value}";
        SchemaPurpose = _schema.Purpose;
        ImpactText = _schema.Impact;
        LoadRows();
    }

    /// <summary>Charge les lignes de données du fichier — filtrées
    /// par id ou nom EN quand RowFilter est renseigné.</summary>
    private void LoadRows()
    {
        IniRows.Clear();
        SelectedIniRow = null;
        FieldGroups.Clear();
        EditRaw = "";
        if (SelectedIniFile == null) return;
        var path = Path.Combine(Ctx.IniDir, SelectedIniFile);
        var i = 0;
        foreach (var rec in Ctx.Ini.Records(path))
        {
            var c = rec.Cols;
            var enName = ResolveEnName(c);
            if (!RowMatches(c, enName)) { i++; continue; }
            IniRows.Add(new IniRow
            {
                Index = i++, EnName = enName,
                FirstCol = c.Length > 0 ? c[0] : "",
                Cols = c, Raw = rec.Raw,
            });
        }
        Status(RowFilter.Length > 0
            ? $"{IniRows.Count} matching rows — {SelectedIniFile}"
            : $"{IniRows.Count} rows — {SelectedIniFile}");
    }

    /// <summary>Nom EN de la ligne : id (col 0) → T_*.ini, sinon le
    /// contenu de la colonne nom quand ce n'est pas du Big5
    /// déchiffrable.</summary>
    private string ResolveEnName(string[] c)
    {
        if (SelectedIniFile == null || c.Length == 0) return "";
        if (int.TryParse(c[0], out var id))
        {
            var en = Ctx.Trans.NameForIniRow(SelectedIniFile, id);
            if (en != null) return en;
        }
        var nameCol = _schema.NameColumn;
        if (nameCol >= 0 && nameCol < c.Length
            && c[nameCol].Length > 0)
            return $"[untranslated] {c[nameCol]}";
        return "";
    }

    private bool RowMatches(string[] c, string enName) =>
        string.IsNullOrWhiteSpace(RowFilter)
        || enName.Contains(RowFilter, StringComparison.OrdinalIgnoreCase)
        || c.Any(x => x.Equals(RowFilter,
            StringComparison.OrdinalIgnoreCase));

    partial void OnSelectedIniRowChanged(IniRow? value)
    {
        FieldGroups.Clear();
        EditRaw = value?.Raw ?? "";
        RawMode = false;
        SelectedRowCaption = value == null ? ""
            : value.EnName.Length > 0
                ? $"#{value.FirstCol} — {value.EnName}"
                : $"data row #{value.Index}";
        if (value == null) return;
        foreach (var g in IniFormBuilder.Build(
                     _schema, value.Cols, Ctx, RefreshRaw))
            FieldGroups.Add(g);
    }

    /// <summary>Live-refresh de l'aperçu raw quand un champ change
    /// (séparateurs d'origine préservés).</summary>
    private void RefreshRaw()
    {
        if (SelectedIniRow is not { } r || RawMode) return;
        var cols = IniFormBuilder.Apply(FieldGroups, r.Cols);
        EditRaw = IniColumns.ReplaceFields(r.Raw, cols);
    }

    /// <summary>Bascule édition par champs ↔ édition raw (case
    /// « Edit the raw line » ou bouton). Au retour en mode champs,
    /// la ligne raw est reparsée dans le formulaire.</summary>
    partial void OnRawModeChanged(bool value)
    {
        if (SelectedIniRow == null || value) return;
        var cols = EditRaw.Split('|', ',');
        SelectedIniRow.Cols = cols;
        SelectedIniRow.Raw = EditRaw;
        FieldGroups.Clear();
        foreach (var g in IniFormBuilder.Build(
                     _schema, cols, Ctx, RefreshRaw))
            FieldGroups.Add(g);
    }

    [RelayCommand]
    private void ToggleRawMode() => RawMode = !RawMode;

    /// <summary>Sauvegarde : les champs sont appliqués aux colonnes
    /// puis la ligne est réécrite en place (Big5 préservé). En raw
    /// mode, le texte raw est écrit tel quel.</summary>
    [RelayCommand]
    private void SaveIniRow()
    {
        if (SelectedIniFile == null || SelectedIniRow is not { } r)
        { Status("No row selected"); return; }
        string newRaw;
        if (RawMode)
        {
            newRaw = EditRaw;
        }
        else
        {
            var bad = IniFormBuilder.FirstError(FieldGroups);
            if (bad != null)
            {
                Status($"Field \"{bad.Label}\" (col {bad.ColumnIndex}) " +
                       "is invalid — fix it before saving");
                return;
            }
            var cols = IniFormBuilder.Apply(FieldGroups, r.Cols);
            r.Cols = cols;
            newRaw = IniColumns.ReplaceFields(r.Raw, cols);
        }
        var path = Path.Combine(Ctx.IniDir, SelectedIniFile);
        var ok = Ctx.IniWriter.ReplaceRecordByIndex(
            path, r.Index, newRaw);
        if (ok)
        {
            r.Raw = newRaw;
            EditRaw = newRaw;
            r.EnName = ResolveEnName(r.Cols);
            r.RefreshDisplay();
        }
        Status(ok
            ? $"Row {r.Index} saved in {SelectedIniFile} — restart " +
              "the zone service to apply"
            : $"Write failed in {path}");
    }
}
