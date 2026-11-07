using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Limitless.Studio.Services;

/// <summary>Une valeur réellement distribuée dans une colonne
/// .ini — le texte brut, son nombre d'occurrences, et un exemple
/// (id de l'enregistrement + nom EN résolu via T_*.ini).</summary>
public sealed record ObservedValue(
    string Value, int Count, string ExampleId, string? ExampleName);

/// <summary>Statistiques de valeurs réelles par colonne d'un
/// fichier .ini — « valeurs observées » du panneau d'aide :
/// pour chaque colonne, le compteur de chaque valeur distincte
/// et le premier enregistrement qui la porte (exemple vivant).
/// Calcul en une passe sur le fichier à la première demande,
/// mis en cache par chemin + date de modification (le fichier
/// réédité depuis le Studio est rechargé au prochain « ? »).</summary>
public sealed class IniColumnStats
{
    private sealed class ColumnStat
    {
        public int TotalRows;
        public readonly Dictionary<int, Dictionary<string, int>>
            Counts = new();
        /// <summary>Premier enregistrement portant chaque valeur —
        /// (id col0, nom EN résolu).</summary>
        public readonly Dictionary<int, Dictionary<string,
            (string Id, string? Name)>> Examples = new();
    }

    private readonly IniService _ini;
    private readonly TranslationCatalog _trans;
    private readonly Dictionary<string,
        (DateTime Stamp, ColumnStat Stats)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public IniColumnStats(IniService ini, TranslationCatalog trans)
    {
        _ini = ini;
        _trans = trans;
    }

    /// <summary>Top `take` valeurs de la colonne `column` du
    /// fichier `iniFile` (nom de fichier dans db_decoded), triées
    /// par fréquence décroissante puis valeur. Vide si le fichier
    /// est absent.</summary>
    public IReadOnlyList<ObservedValue> Top(
        string iniFile, int column, int take = 14)
    {
        var st = StatsFor(iniFile);
        if (st == null || !st.Counts.TryGetValue(column, out var c))
            return Array.Empty<ObservedValue>();
        var ex = st.Examples[column];
        return c.OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(take)
                .Select(kv => new ObservedValue(
                    kv.Key, kv.Value,
                    ex.TryGetValue(kv.Key, out var e) ? e.Id : "",
                    e.Name))
                .ToArray();
    }

    /// <summary>Nombre de valeurs distinctes de la colonne.</summary>
    public int DistinctCount(string iniFile, int column)
    {
        var st = StatsFor(iniFile);
        return st != null
            && st.Counts.TryGetValue(column, out var c) ? c.Count : 0;
    }

    /// <summary>Nombre d'enregistrements scannés.</summary>
    public int RowCount(string iniFile) =>
        StatsFor(iniFile)?.TotalRows ?? 0;

    private ColumnStat? StatsFor(string iniFile)
    {
        var path = Path.Combine(GamePaths.IniDir(), iniFile);
        if (!File.Exists(path)) return null;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_cache.TryGetValue(path, out var hit)
            && hit.Stamp == stamp)
            return hit.Stats;
        var st = Build(path, iniFile);
        _cache[path] = (stamp, st);
        return st;
    }

    /// <summary>Une passe : pour chaque colonne, compte les
    /// valeurs et mémorise le premier record par valeur (id +
    /// nom EN). Les valeurs multi-lignes sont tronquées à la
    /// première ligne ; les valeurs >80 car. sont coupées.</summary>
    private ColumnStat Build(string path, string iniFile)
    {
        var st = new ColumnStat();
        foreach (var rec in _ini.Records(path))
        {
            var c = rec.Cols;
            st.TotalRows++;
            var id = c.Length > 0 ? c[0] : "";
            for (var i = 0; i < c.Length; i++)
            {
                var v = c[i];
                if (v.Length > 80)
                    v = v[..80];
                if (!st.Counts.TryGetValue(i, out var counts))
                {
                    counts = new();
                    st.Counts[i] = counts;
                    st.Examples[i] = new();
                }
                counts[v] = counts.TryGetValue(v, out var n) ? n + 1 : 1;
                var ex = st.Examples[i];
                if (ex.ContainsKey(v)) continue;
                // Nom EN du record exemple — résolu via le fichier
                // T_*.ini associé quand l'id est numérique.
                string? name = null;
                if (int.TryParse(id, out var idNum))
                    name = _trans.NameForIniRow(iniFile, idNum);
                ex[v] = (id, name);
            }
        }
        return st;
    }
}
