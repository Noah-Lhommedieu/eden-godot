using System;
using System.Collections.Generic;
using Godot;
using EdenClient.Data;

namespace EdenClient.World;

/// <summary>
/// Taille et skin natifs d'un modèle de monstre : <c>data/db/monsterlist.ini</c>
/// (<c>DB&lt;n&gt;=code,kfm,scale,…,texture,…</c>), clé = code modèle de
/// <c>c_monster</c> col 1 (« M025031 »). Le client natif charge cette table
/// (<c>FUN_00cff380</c> : col 2 → échelle, col 8 → texture) ; les variantes
/// d'un même NIF y portent leur propre échelle et texture — ex. m025 : 3
/// textures (rouge/brune/verte) ; la variante « 013 » des world boss est 2 à
/// 4 fois plus grande que la « 010 » (m083 0,40 → 0,90, m105 0,95 → 3,80,
/// m405 1,40 → 4,00). C'est la source de la taille des world boss (aucun
/// facteur codé en dur n'est nécessaire) et de la variété des skins d'un même
/// modèle (sans elle tous les champignoïdes gardaient la texture du GLB).
/// Chargement paresseux, thread-safe.
/// </summary>
public static class MonsterModelTable
{
    /// <summary>Entrée monsterlist.</summary>
    public readonly struct Def
    {
        /// <summary>Échelle du modèle (col 2, 1 si absente).</summary>
        public float Scale { get; init; }
        /// <summary>Texture (col 8, « m02503.dds »), "" = celle du GLB.</summary>
        public string Texture { get; init; }
    }

    private static Dictionary<string, Def>? _defs;
    private static bool _tried;
    private static readonly object _lock = new();

    /// <summary>Entrée d'un code modèle c_monster (insensible à la casse).</summary>
    public static Def? Resolve(string modelCode)
    {
        if (modelCode.Length == 0) return null;
        EnsureLoaded();
        return _defs != null &&
               _defs.TryGetValue(modelCode.Trim(), out var d) ? d : null;
    }

    /// <summary>Parse une ligne <c>DBn=…</c> (testable hors moteur).</summary>
    internal static (string Code, Def Def)? ParseLine(string raw)
    {
        var eq = raw.IndexOf('=');
        if (eq <= 0 || !raw.StartsWith("DB", StringComparison.Ordinal))
            return null;
        var f = raw[(eq + 1)..].Split(',');
        if (f.Length < 3 || f[0].Trim().Length == 0) return null;
        var sc = IniTable.GetFloat(f, 2);
        return (f[0].Trim(), new Def
        {
            Scale = sc > 0f ? sc : 1f,
            Texture = f.Length > 8 ? f[8].Trim() : "",
        });
    }

    private static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var path = AssetCatalog.ResolveRawAsset("data/db/monsterlist.ini");
                if (path == null)
                {
                    GD.PrintErr("[mob] monsterlist.ini absent — modèles de mobs " +
                        "sans échelle/skin natifs");
                    return;
                }
                var d = new Dictionary<string, Def>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var raw in Vfs.EdenVfs.ReadLines(path))
                    if (ParseLine(raw) is { } e) d[e.Code] = e.Def;
                _defs = d;
                GD.Print($"[mob] monsterlist : {d.Count} codes modèle");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[mob] monsterlist : {ex.Message}");
            }
        }
    }
}
