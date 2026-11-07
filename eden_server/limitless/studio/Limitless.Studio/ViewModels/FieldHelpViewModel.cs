using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>ViewModel de la fenêtre de référence « ? » d'un champ —
/// fusionne la doc du catalogue (utilité, table de valeurs,
/// recette, liens) avec les valeurs réellement distribuées en data
/// (compteurs + exemple id/nom EN via IniColumnStats).</summary>
public sealed class FieldHelpViewModel : ObservableObject
{
    /// <summary>Une ligne de la table de valeurs — valeur brute,
    /// signification, nombre d'occurrences observées et exemple
    /// réel (id + nom EN du record qui porte la valeur).</summary>
    public sealed record ValueRow(
        string Value, string Meaning, string Seen, string Example);

    /// <summary>Un exemple réel d'utilisation — id du record,
    /// nom EN, valeur portée.</summary>
    public sealed record ExampleRow(
        string Value, int Count, string Record);

    /// <summary>Cap de lignes dans la section « observed » —
    /// assez grand pour couvrir toutes les valeurs distinctes des
    /// colonnes énumérées (kind = 76, type monstre = 178 — au-delà
    /// on tronque au top fréquence et le bandeau l'indique).</summary>
    private const int ObservedTake = 80;

    public string Header { get; }
    public string FieldTitle { get; }
    public string CurrentValue { get; }
    public string Purpose { get; }
    public bool HasPurpose => Purpose.Length > 0;
    public string? ValuesNote { get; }
    public bool HasValuesNote => ValuesNote is { Length: > 0 };
    public string? Recipe { get; }
    public bool HasRecipe => Recipe is { Length: > 0 };
    public ObservableCollection<ValueRow> Values { get; } = new();
    public bool HasValues => Values.Count > 0;
    public ObservableCollection<string> SeeAlso { get; } = new();
    public bool HasSeeAlso => SeeAlso.Count > 0;
    public ObservableCollection<ExampleRow> Observed { get; } = new();
    public bool HasObserved => Observed.Count > 0;
    public string ObservedHeader { get; }

    public FieldHelpViewModel(string iniFile, IniColumn column,
        string currentValue, IniColumnStats stats)
    {
        FieldTitle = $"{column.Name}  —  col {column.Index}";
        Header = $"{iniFile}  ·  {column.Name} (col {column.Index})";
        CurrentValue = currentValue.Length > 0
            ? $"current value: \"{Truncate(currentValue, 60)}\""
            : "current value: (empty)";

        var entry = FieldHelpCatalog.For(iniFile, column.Index)
            ?? FieldHelpEntry.Generic(column.Hint);
        Purpose = entry.Purpose;
        ValuesNote = entry.ValuesNote;
        Recipe = entry.Recipe;
        foreach (var s in entry.SeeAlso ??
                    System.Array.Empty<string>())
            SeeAlso.Add(s);

        var known = entry.Values
            ?? System.Array.Empty<FieldValueCase>();
        var distinct = stats.DistinctCount(iniFile, column.Index);
        // Toutes les valeurs réelles quand la colonne est une
        // énumération bornée, sinon le top fréquence.
        var take = distinct > 0 && distinct <= ObservedTake
            ? distinct : ObservedTake;
        var observed = stats.Top(iniFile, column.Index, take);
        var observedByValue = observed.ToDictionary(
            o => o.Value, o => o, System.StringComparer.Ordinal);

        // 1) Les cas documentés, enrichis du compteur + exemple observés.
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var v in known)
        {
            seen.Add(v.Value);
            var found = observedByValue.TryGetValue(
                v.Value, out var o)
                || TryObserved(v.Value, observedByValue, out o);
            Values.Add(new ValueRow(v.Value, v.Meaning,
                found ? $"×{o!.Count}" : "",
                found ? FormatRecord(o!) : ""));
        }
        // 2) Les valeurs réelles non documentées.
        foreach (var o in observed)
        {
            if (seen.Contains(o.Value)) continue;
            Values.Add(new ValueRow(Truncate(o.Value, 40), "—",
                $"×{o.Count}", FormatRecord(o)));
        }

        var rows = stats.RowCount(iniFile);
        var trunc = distinct > observed.Count
            ? $" — showing top {observed.Count} by frequency"
            : "";
        ObservedHeader = rows > 0
            ? $"Observed in data — {distinct} distinct value" +
              $"{(distinct != 1 ? "s" : "")} over {rows} rows{trunc}"
            : "Observed in data — file not found";
        foreach (var o in observed)
            Observed.Add(new ExampleRow(
                Truncate(o.Value, 40), o.Count, FormatRecord(o)));
    }

    /// <summary>Libellé « #id — nom EN » d'un record exemple.</summary>
    private static string FormatRecord(ObservedValue o) =>
        o.ExampleName is { Length: > 0 }
            ? $"#{o.ExampleId} — {o.ExampleName}"
            : o.ExampleId.Length > 0 ? $"#{o.ExampleId}" : "";

    /// <summary>Le cas documenté peut être une famille ("21-24",
    /// "7-13", "0 or ≥7", "hi-word 8") — on tente une correspondance
    /// souple entre le libellé et la valeur observée.</summary>
    private static bool TryObserved(string docValue,
        Dictionary<string, ObservedValue> observed,
        out ObservedValue? hit)
    {
        // Cas « famille » : la doc regroupe plusieurs valeurs —
        // on affiche la somme des compteurs du premier match
        // numérique de la plage.
        var parts = docValue.Split('-');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var lo)
            && int.TryParse(parts[1], out var hi))
        {
            var total = 0;
            string id = "";
            string? name = null;
            foreach (var kv in observed)
            {
                if (!int.TryParse(kv.Key, out var n)
                    || n < lo || n > hi) continue;
                total += kv.Value.Count;
                if (id.Length == 0) { id = kv.Value.ExampleId;
                    name = kv.Value.ExampleName; }
            }
            if (total > 0)
            {
                hit = new ObservedValue(docValue, total, id, name);
                return true;
            }
        }
        hit = null;
        return false;
    }

    private static string Truncate(string v, int max) =>
        v.Length <= max ? v : v[..max] + "…";
}
