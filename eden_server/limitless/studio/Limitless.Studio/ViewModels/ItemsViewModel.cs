using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Limitless.Studio.Models;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>Page Items — S_Item.ini (serveur, Big5) + noms EN de
/// t_item.ini (client Reforged). Liste dense avec vraies icônes
/// décodées des assets extraits, fiche d'édition Raw.</summary>
public partial class ItemsViewModel : PageViewModel
{
    /// <summary>Icônes demandées mais pas encore décodées — la vue
    /// appelle <see cref="EnsureIcon"/> quand une ligne est réalisée
    /// (virtualisation : seules les lignes visibles décodent).</summary>
    private readonly ConcurrentDictionary<GameItem, byte> _pendingIcons =
        new();
    private Task? _iconWorker;

    public ItemsViewModel(StudioContext ctx) : base(ctx) { }

    public override string Title => "Items";
    public override string Subtitle =>
        "S_Item.ini — server db, names from t_item.ini (EN)";

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private GameItem? _selectedItem;
    [ObservableProperty] private string _iconInfo = "";
    [ObservableProperty] private string _modelInfo = "";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isLoading;
    /// <summary>Texte Raw éditable — copié depuis/vers
    /// SelectedItem.Raw pour garder l'objet liste intact.</summary>
    [ObservableProperty] private string _editRaw = "";
    [ObservableProperty] private bool _rawMode;
    [ObservableProperty] private int _totalRows;
    [ObservableProperty] private string _impactText = "";

    public ObservableCollection<GameItem> Items { get; } = new();
    /// <summary>Formulaire dynamique de la ligne sélectionnée —
    /// schéma S_Item (colonnes nommées depuis ItemParser.cs).</summary>
    public ObservableCollection<IniFieldGroup> FieldGroups { get; } =
        new();
    private string[] _selectedCols = System.Array.Empty<string>();

    protected override void OnFirstActivate() => Refresh();

    [RelayCommand]
    private void Refresh() => LoadItems();

    /// <summary>Recharge quand la recherche change (Enter/bouton).</summary>
    [RelayCommand]
    private void Search() => LoadItems();

    private void LoadItems()
    {
        _pendingIcons.Clear();
        Items.Clear();
        SelectedItem = null;
        IsLoading = true;
        try
        {
            var path = Path.Combine(Ctx.IniDir, "S_Item.ini");
            if (!Ctx.IniDirExists || !File.Exists(path))
            {
                EmptyMessage =
                    $"S_Item.ini not found under {Ctx.IniDir} — " +
                    "check the decoded db folder.";
                IsEmpty = true;
                Status(EmptyMessage);
                return;
            }
            var total = 0;
            foreach (var rec in Ctx.Ini.Records(path))
            {
                var c = rec.Cols;
                if (c.Length < 2 || !int.TryParse(c[0], out var id))
                    continue;
                total++;
                var it = new GameItem
                {
                    Id = id,
                    IconCode = c[1],
                    ModelCode = c.Length > 3 ? c[3] : "",
                    NameBig5 = EntityIconIndex.ShortName(
                        c.Length > 7 ? c[7] : ""),
                    Raw = rec.Raw,
                };
                it.Name = Ctx.Trans.ItemName(id)
                    ?? (it.NameBig5.Length > 0
                        ? $"[untranslated] {it.NameBig5}"
                        : "(no name)");
                if (!Matches(it)) continue;
                Items.Add(it);
            }
            TotalRows = total;
            IsEmpty = Items.Count == 0;
            EmptyMessage = Items.Count == 0
                ? $"No items match \"{Filter}\" — clear the filter."
                : "";
            Status($"{Items.Count} items loaded ({total} rows in S_Item)");
        }
        finally { IsLoading = false; }
    }

    private bool Matches(GameItem it) =>
        string.IsNullOrWhiteSpace(Filter)
        || it.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
        || it.NameBig5.Contains(Filter, StringComparison.OrdinalIgnoreCase)
        || it.Id.ToString() == Filter;

    /// <summary>Demande le décodage de l'icône d'une ligne — appelé
    /// par la vue quand le container de la ligne est réalisé
    /// (ListBox virtualisé : la couverture complète des ~20k items
    /// ne décode que ce qui défile sous les yeux).</summary>
    public void EnsureIcon(GameItem it)
    {
        if (it.Icon != null || it.IconCode.Length == 0) return;
        if (!_pendingIcons.TryAdd(it, 0)) return;
        _iconWorker ??= Task.Run(DrainIcons);
    }

    /// <summary>Vidange la file d'icônes en tâche de fond —
    /// IconService.Get est mis en cache (un code = une bitmap).</summary>
    private async Task DrainIcons()
    {
        var icons = Ctx.Icons;
        while (_pendingIcons.Keys.FirstOrDefault() is { } it)
        {
            if (_pendingIcons.TryRemove(it, out _))
            {
                var bmp = icons.Get(it.IconCode);
                if (bmp != null) it.Icon = bmp;
            }
            // Laisse respirer l'UI entre les lots.
            await Task.Delay(1);
        }
        _iconWorker = null;
    }

    partial void OnSelectedItemChanged(GameItem? value)
    {
        EditRaw = value?.Raw ?? "";
        ModelInfo = "";
        FieldGroups.Clear();
        RawMode = false;
        if (value == null) { IconInfo = ""; ImpactText = ""; return; }
        var schema = IniSchemas.For("S_Item.ini");
        ImpactText = schema.Impact;
        _selectedCols = value.Raw.Split('|', ',');
        foreach (var g in IniFormBuilder.Build(
                     schema, _selectedCols, Ctx, RefreshRaw))
            FieldGroups.Add(g);
        IconInfo = value.Icon != null
            ? $"{value.IconCode} — decoded from extracted assets"
            : Ctx.Icons.TryLocate(value.IconCode, out var f)
                ? $"{f} — found but not displayable"
                : $"{value.IconCode}.* not in UI dirs or pkg.idx";
    }

    /// <summary>Live-refresh du raw quand un champ change.</summary>
    private void RefreshRaw()
    {
        if (SelectedItem == null || RawMode) return;
        var cols = IniFormBuilder.Apply(FieldGroups, _selectedCols);
        _selectedCols = cols;
        EditRaw = IniColumns.ReplaceFields(SelectedItem.Raw, cols);
    }

    /// <summary>Retour de l'édition raw vers le formulaire — la
    /// ligne raw est reparsée dans les champs.</summary>
    partial void OnRawModeChanged(bool value)
    {
        if (SelectedItem == null || value) return;
        _selectedCols = EditRaw.Split('|', ',');
        SelectedItem.Raw = EditRaw;
        FieldGroups.Clear();
        foreach (var g in IniFormBuilder.Build(
                     IniSchemas.For("S_Item.ini"), _selectedCols,
                     Ctx, RefreshRaw))
            FieldGroups.Add(g);
    }

    /// <summary>Sauvegarde : les champs du formulaire (ou le raw en
    /// mode raw) sont réécrits dans S_Item.ini — séparateurs
    /// d'origine préservés, Big5 conservé.</summary>
    [RelayCommand]
    private void SaveSelected()
    {
        if (SelectedItem is not { } it)
        { Status("No item selected"); return; }
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
            _selectedCols = IniFormBuilder.Apply(
                FieldGroups, _selectedCols);
            newRaw = IniColumns.ReplaceFields(it.Raw, _selectedCols);
        }
        var path = Path.Combine(Ctx.IniDir, "S_Item.ini");
        var ok = Ctx.IniWriter.ReplaceRecord(path, it.Id, newRaw);
        if (ok) { it.Raw = newRaw; EditRaw = newRaw; }
        Status(ok
            ? $"Item {it.Id} saved to S_Item.ini — restart the " +
              "zone service to apply"
            : $"Item {it.Id} not found in {path}");
    }

    /// <summary>Résout le code modèle (G00005…) vers la famille
    /// .nif/.kf/.dds du pkg.idx.</summary>
    [RelayCommand]
    private void LoadModel()
    {
        var code = SelectedItem?.ModelCode;
        if (string.IsNullOrWhiteSpace(code))
        { ModelInfo = "No model code on this row"; return; }
        var m = new ModelService(
            new PkgIndexService(Ctx.PkgDir)).Resolve(code);
        ModelInfo = m == null
            ? $"{code}.* — no .nif in pkg.idx"
            : $"{m.Base} → {m.Nif} | {m.Kf.Count} .kf anims" +
              (m.Kf.Count > 0
                  ? $" ({string.Join(", ", m.Kf.Take(4))}…)" : "") +
              $" | {m.Dds.Count} .dds textures";
    }
}
