using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>Champ éditable du formulaire dynamique — une colonne de
/// la ligne .ini. `Value` est texte brut ; `HasError` quand le kind
/// Number/Flags n'est pas un nombre ou que la valeur contient un
/// séparateur. `RefName` affiche le nom EN résolu quand la colonne
/// référence une id traduite (RefTrans du schéma). Le bouton « ? »
/// (<see cref="ShowHelpCommand"/>) ouvre la fenêtre de référence
/// complète du champ (wiki des valeurs + valeurs observées).</summary>
public partial class IniFieldRow : ObservableObject
{
    private readonly TranslationCatalog? _trans;

    public IniFieldRow(IniColumn col, string value,
        StudioContext? ctx, string tableFile)
    {
        Column = col;
        _value = value;
        Ctx = ctx;
        TableFile = tableFile;
        _trans = ctx?.Trans;
        _refName = ResolveRef(value);
    }

    public IniColumn Column { get; }
    /// <summary>Fichier .ini de la colonne (ex. "S_Item.ini") —
    /// clé du catalogue d'aide et des stats observées.</summary>
    public string TableFile { get; }
    /// <summary>Contexte partagé (translations + column stats).</summary>
    public StudioContext? Ctx { get; }
    public int ColumnIndex => Column.Index;
    public string Label => Column.Name;
    /// <summary>Hint court — affiché en tooltip du bouton « ? ».</summary>
    public string Hint => Column.Hint;
    public bool HasHint => Column.Hint.Length > 0;
    public bool IsNameField => Column.Kind == IniFieldKind.Name;

    /// <summary>Ouvre la fenêtre de référence complète du champ —
    /// utilité, table de toutes les valeurs possibles, recettes
    /// d'usage et valeurs réellement distribuées en data.</summary>
    [RelayCommand]
    private void ShowHelp() =>
        Views.FieldHelpWindow.Present(this);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(RefName))]
    private string _value;

    /// <summary>Nom EN résolu (id référencé) ou nom Big5→EN.</summary>
    [ObservableProperty] private string? _refName;

    public bool HasError => Validate(Value);

    /// <summary>Appelé par le parent quand la valeur change —
    /// rafraîchit l'aperçu raw sans passer par un événement.</summary>
    public Action? Edited { get; set; }

    partial void OnValueChanged(string value)
    {
        RefName = ResolveRef(value);
        Edited?.Invoke();
    }

    private bool Validate(string v)
    {
        if (v.IndexOfAny(new[] { '|', ',' }) >= 0) return true;
        if (Column.Kind is IniFieldKind.Number or IniFieldKind.Flags
            && v.Length > 0)
            return !decimal.TryParse(v, NumberStyles.Any,
                CultureInfo.InvariantCulture, out _);
        return false;
    }

    private string? ResolveRef(string v)
    {
        if (Column.RefTrans == null || _trans == null) return null;
        return int.TryParse(v, out var id) && id > 0
            ? _trans.NameFor(Column.RefTrans, id) : null;
    }
}

/// <summary>Groupe de champs du formulaire (section : Identity,
/// Combat, Economy…).</summary>
public sealed class IniFieldGroup
{
    public IniFieldGroup(string name, IEnumerable<IniFieldRow> fields,
        bool startCollapsed = false)
    {
        Name = name;
        Fields = new ObservableCollection<IniFieldRow>(fields);
        StartCollapsed = startCollapsed;
    }
    public string Name { get; }
    /// <summary>Le groupe démarre replié (colonnes non documentées).</summary>
    public bool StartCollapsed { get; }
    public ObservableCollection<IniFieldRow> Fields { get; }
}

/// <summary>Construit le formulaire d'une ligne .ini : colonnes
/// documentées du schéma groupées par section + groupe « Other
/// columns » pour toutes les colonnes non cartographiées (toute la
/// ligne reste éditable). À la sauvegarde, <see cref="Apply"/> écrit
/// les valeurs dans le tableau de colonnes et
/// <see cref="IniColumns.ReplaceFields"/> reconstruit la ligne en
/// préservant les séparateurs.</summary>
public static class IniFormBuilder
{
    /// <summary>Champs groupés pour la ligne `cols` selon le schéma.
    /// `ctx` donne les traductions EN et les stats de colonnes pour
    /// les boutons « ? » ; `onEdited` est appelé à chaque frappe
    /// (live-refresh du raw).</summary>
    public static List<IniFieldGroup> Build(IniTableSchema schema,
        string[] cols, StudioContext? ctx, Action? onEdited)
    {
        var byIndex = schema.Columns
            .GroupBy(c => c.Index)
            .ToDictionary(g => g.Key, g => g.First());
        var named = new Dictionary<string, List<IniFieldRow>>(
            StringComparer.Ordinal);
        var order = new List<string>();
        var other = new List<IniFieldRow>();

        IniFieldRow Field(int i, IniColumn? col) =>
            new(col ?? new IniColumn(i, $"col {i}",
                "Undocumented column — check the table parser for " +
                "its meaning."),
                i < cols.Length ? cols[i] : "", ctx, schema.File)
            { Edited = onEdited };

        for (var i = 0; i < cols.Length; i++)
        {
            if (byIndex.TryGetValue(i, out var col))
            {
                if (!named.TryGetValue(col.Group, out var list))
                {
                    named[col.Group] = list = new();
                    order.Add(col.Group);
                }
                list.Add(Field(i, col));
            }
            else other.Add(Field(i, null));
        }

        var groups = new List<IniFieldGroup>();
        foreach (var g in order)
            groups.Add(new IniFieldGroup(g, named[g]));
        if (other.Count > 0)
            groups.Add(new IniFieldGroup("Other columns (undocumented)",
                other, startCollapsed: true));
        return groups;
    }

    /// <summary>Écrit toutes les valeurs des champs dans `cols`
    /// (indexées par ColumnIndex ; agrandit si besoin).</summary>
    public static string[] Apply(IEnumerable<IniFieldGroup> groups,
        string[] cols)
    {
        var result = cols;
        foreach (var f in groups.SelectMany(g => g.Fields))
        {
            if (f.ColumnIndex >= result.Length)
                Array.Resize(ref result, f.ColumnIndex + 1);
            result[f.ColumnIndex] = f.Value;
        }
        return result;
    }

    /// <summary>Un champ du formulaire est-il en erreur ?</summary>
    public static IniFieldRow? FirstError(
        IEnumerable<IniFieldGroup> groups) =>
        groups.SelectMany(g => g.Fields)
            .FirstOrDefault(f => f.HasError);
}
