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

/// <summary>Page Monsters — S_Monster.ini (serveur) + noms EN de
/// t_monster.ini. Icône = `uiicon/{base}.dds` (le code modèle
/// M312040 dégradé en M312 par strip des chiffres de variante).</summary>
public partial class MonstersViewModel : PageViewModel
{
    /// <summary>Icônes demandées mais pas encore décodées — la vue
    /// appelle <see cref="EnsureIcon"/> quand une ligne est réalisée.</summary>
    private readonly ConcurrentDictionary<GameMonster, byte>
        _pendingIcons = new();
    private Task? _iconWorker;

    public MonstersViewModel(StudioContext ctx) : base(ctx) { }

    public override string Title => "Monsters";
    public override string Subtitle =>
        "S_Monster.ini — server db, names from t_monster.ini (EN)";

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private GameMonster? _selectedMonster;
    [ObservableProperty] private string _iconInfo = "";
    [ObservableProperty] private string _modelInfo = "";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _editRaw = "";
    [ObservableProperty] private bool _rawMode;
    [ObservableProperty] private int _totalRows;
    [ObservableProperty] private string _impactText = "";

    public ObservableCollection<GameMonster> Monsters { get; } = new();
    /// <summary>Formulaire dynamique de la ligne sélectionnée —
    /// schéma S_Monster (colonnes nommées depuis MonsterParser.cs).</summary>
    public ObservableCollection<IniFieldGroup> FieldGroups { get; } =
        new();
    private string[] _selectedCols = System.Array.Empty<string>();

    protected override void OnFirstActivate() => Refresh();

    [RelayCommand]
    private void Refresh() => LoadMonsters();

    [RelayCommand]
    private void Search() => LoadMonsters();

    private void LoadMonsters()
    {
        _pendingIcons.Clear();
        Monsters.Clear();
        SelectedMonster = null;
        IsLoading = true;
        try
        {
            var path = Path.Combine(Ctx.IniDir, "S_Monster.ini");
            if (!Ctx.IniDirExists || !File.Exists(path))
            {
                EmptyMessage =
                    $"S_Monster.ini not found under {Ctx.IniDir} — " +
                    "check the decoded db folder.";
                IsEmpty = true;
                Status(EmptyMessage);
                return;
            }
            var total = 0;
            foreach (var rec in Ctx.Ini.Records(path))
            {
                var c = rec.Cols;
                if (c.Length < 3 || !int.TryParse(c[0], out var id))
                    continue;
                total++;
                var m = new GameMonster
                {
                    Id = id,
                    IconCode = c[1],
                    ModelCode = c[1],
                    // Records boss 2 lignes : col2 = « titre\nnom » —
                    // on garde le nom pour le fallback Big5.
                    NameBig5 = EntityIconIndex.ShortName(c[2]),
                    Level = c.Length > 4 && int.TryParse(c[4],
                        out var lv) ? lv : 0,
                    Hp = c.Length > 8 && long.TryParse(c[8],
                        out var hp) ? hp : 0,
                    Raw = rec.Raw,
                };
                m.Name = Ctx.Trans.MonsterName(id)
                    ?? (m.NameBig5.Length > 0
                        ? $"[untranslated] {m.NameBig5}"
                        : "(no name)");
                if (!Matches(m)) continue;
                Monsters.Add(m);
            }
            TotalRows = total;
            IsEmpty = Monsters.Count == 0;
            EmptyMessage = Monsters.Count == 0
                ? $"No monsters match \"{Filter}\" — clear the filter."
                : "";
            Status($"{Monsters.Count} monsters loaded " +
                   $"({total} records in S_Monster)");
        }
        finally { IsLoading = false; }
    }

    private bool Matches(GameMonster m) =>
        string.IsNullOrWhiteSpace(Filter)
        || m.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
        || m.NameBig5.Contains(Filter, StringComparison.OrdinalIgnoreCase)
        || m.Id.ToString() == Filter;

    /// <summary>Demande le décodage de l'icône d'une ligne — appelé
    /// par la vue quand le container est réalisé (virtualisation :
    /// les ~13k mobs ne décodent que les lignes visibles).</summary>
    public void EnsureIcon(GameMonster m)
    {
        if (m.Icon != null || m.IconCode.Length == 0) return;
        if (!_pendingIcons.TryAdd(m, 0)) return;
        _iconWorker ??= Task.Run(DrainIcons);
    }

    private async Task DrainIcons()
    {
        var icons = Ctx.Icons;
        while (_pendingIcons.Keys.FirstOrDefault() is { } m)
        {
            if (_pendingIcons.TryRemove(m, out _))
            {
                var bmp = icons.Get(m.IconCode);
                if (bmp != null) m.Icon = bmp;
            }
            await Task.Delay(1);
        }
        _iconWorker = null;
    }

    partial void OnSelectedMonsterChanged(GameMonster? value)
    {
        EditRaw = value?.Raw ?? "";
        ModelInfo = "";
        FieldGroups.Clear();
        RawMode = false;
        if (value == null) { IconInfo = ""; ImpactText = ""; return; }
        var schema = IniSchemas.For("S_Monster.ini");
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
        if (SelectedMonster == null || RawMode) return;
        var cols = IniFormBuilder.Apply(FieldGroups, _selectedCols);
        _selectedCols = cols;
        EditRaw = IniColumns.ReplaceFields(SelectedMonster.Raw, cols);
    }

    /// <summary>Retour de l'édition raw vers le formulaire.</summary>
    partial void OnRawModeChanged(bool value)
    {
        if (SelectedMonster == null || value) return;
        _selectedCols = EditRaw.Split('|', ',');
        SelectedMonster.Raw = EditRaw;
        FieldGroups.Clear();
        foreach (var g in IniFormBuilder.Build(
                     IniSchemas.For("S_Monster.ini"), _selectedCols,
                     Ctx, RefreshRaw))
            FieldGroups.Add(g);
    }

    /// <summary>Sauvegarde : champs du formulaire (ou raw en mode
    /// raw) réécrits dans S_Monster.ini — Big5 préservé.</summary>
    [RelayCommand]
    private void SaveSelected()
    {
        if (SelectedMonster is not { } m)
        { Status("No monster selected"); return; }
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
            newRaw = IniColumns.ReplaceFields(m.Raw, _selectedCols);
        }
        var path = Path.Combine(Ctx.IniDir, "S_Monster.ini");
        var ok = Ctx.IniWriter.ReplaceRecord(path, m.Id, newRaw);
        if (ok) { m.Raw = newRaw; EditRaw = newRaw; }
        Status(ok
            ? $"Monster {m.Id} saved to S_Monster.ini — restart the " +
              "zone service to apply"
            : $"Monster {m.Id} not found in {path}");
    }

    [RelayCommand]
    private void LoadModel()
    {
        var code = SelectedMonster?.ModelCode;
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
