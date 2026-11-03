// Copyright (c) 2026 Noah Lhommedieu. MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace EdenClient.Data;

/// <summary>
/// <c>data/db/ridelist.ini</c> — code « rNNNxxx » (la rideKey portée
/// par la commande enchant 2134 / le 283 mask=2) → stem du modèle
/// (col2 « rNNN.kfm ») et scale (col3). Format des lignes :
/// <c>DBn=r041010,r041.kfm,1.00,…</c>. Col9 (« f5 ») = classe de
/// taille 1-5, RÉFUTÉE comme hauteur de selle (réf. MONTURES-L17.md)
/// — inutilisée ici. Chargement paresseux via
/// <see cref="TextResolver.ResolveFile"/>.
/// </summary>
public static class RideList
{
    /// <summary>Une entrée ridelist : stem kfm (sans extension) +
    /// scale du modèle de monture.</summary>
    public readonly record struct Entry(string Stem, float Scale);

    private static readonly object _lock = new();
    private static Dictionary<string, Entry>? _byCode;
    private static bool _tried;

    /// <summary>Stem modèle d'une rideKey : code « r{key:D6} » puis
    /// repli « r{key/1000:D3} » (les 3 derniers chiffres du code sont
    /// la variante — convention npclist/monsterlist). Null si la clé
    /// est invalide.</summary>
    public static string? StemFor(int rideKey)
    {
        if (rideKey <= 0) return null;
        var code = $"r{rideKey:D6}";
        return TryGet(code, out var e)
            ? e.Stem
            : $"r{rideKey / 1000:D3}";
    }

    /// <summary>Scale du modèle (col3 ridelist) — 1 si absent.</summary>
    public static float ScaleFor(int rideKey) =>
        rideKey > 0 && TryGet($"r{rideKey:D6}", out var e) &&
        e.Scale > 0f
            ? e.Scale : 1f;

    /// <summary>Lookup par code ridelist (« r041010 », insensible
    /// casse).</summary>
    public static bool TryGet(string code, out Entry e)
    {
        e = default;
        return Ensure().TryGetValue(code, out e);
    }

    /// <summary>Parse les lignes brutes du ini — pur, testable sans
    /// VFS.</summary>
    public static Dictionary<string, Entry> Parse(
        IEnumerable<string> lines)
    {
        var d = new Dictionary<string, Entry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var f = line[(eq + 1)..].Trim().Split(',');
            if (f.Length < 2 || !f[0].Trim().StartsWith('r'))
                continue;
            var scale = 1f;
            if (f.Length > 2)
                float.TryParse(f[2], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out scale);
            var stem = f[1].Trim();
            var dot = stem.LastIndexOf('.');
            if (dot > 0) stem = stem[..dot];
            if (stem.Length > 0)
                d[f[0].Trim()] = new Entry(stem,
                    scale > 0f ? scale : 1f);
        }
        return d;
    }

    private static Dictionary<string, Entry> Ensure()
    {
        if (_byCode != null) return _byCode;
        lock (_lock)
        {
            if (_tried) return _byCode ?? Empty;
            _tried = true;
            try
            {
                var p = TextResolver.ResolveFile?.Invoke(
                    "data/db/ridelist.ini");
                _byCode = p != null && EdenClient.Vfs.EdenVfs
                    .ReadLines(p) is { } lines
                    ? Parse(lines) : new();
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke(
                    $"[ride] ridelist.ini : {e.Message}");
                _byCode = new();
            }
            return _byCode;
        }
    }

    private static readonly Dictionary<string, Entry> Empty = new();
}
