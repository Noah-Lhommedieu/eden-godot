// Copyright (c) 2026 Noah Lhommedieu. MIT License.

using EdenClient.Data;
using EdenClient.Vfs;

namespace EdenClient.Game;

/// <summary>
/// Résolution fidèle du modèle d'un PNJ : <c>data/db/c_npc.ini</c>
/// (npcId → code « n002030 ») puis <c>data/db/npclist.ini</c>
/// (code → « m002.kfm » + texture variante « m00203.dds » + scale).
/// Port de la table de la référence Godot — pur, chargement via
/// <see cref="EdenVfs"/> sur les racines corpus injectées.
/// </summary>
public static class NpcModelTable
{
    /// <summary>Modèle résolu : stem NIF, texture variante, échelle.</summary>
    public readonly struct Def
    {
        /// <summary>Stem du modèle (« m002 » → monster/model/m002.nif).</summary>
        public string Stem { get; init; }
        /// <summary>Texture variante (« m00203.dds » →
        /// monster/texture/m00203.dds) — "" = texture par défaut.</summary>
        public string Texture { get; init; }
        /// <summary>Échelle du modèle (npclist col2, 1.0 par défaut).</summary>
        public float Scale { get; init; }
    }

    private static Dictionary<int, Def>? _defs;
    private static bool _tried;
    private static readonly object _lock = new();
    private static IReadOnlyList<string> _roots = [];

    /// <summary>Racines corpus pour la résolution des tables
    /// (appelée par le bridge quand la carte est chargée).</summary>
    public static void SetRoots(IReadOnlyList<string> roots)
    {
        lock (_lock)
        {
            _roots = roots;
            _defs = null;
            _tried = false;
        }
    }

    /// <summary>Modèle d'un npcId, ou null si inconnu/fichiers absents.</summary>
    public static Def? Resolve(int npcId)
    {
        EnsureLoaded();
        return _defs != null && _defs.TryGetValue(npcId, out var d)
            ? d : null;
    }

    private static string? Find(string rel)
    {
        foreach (var r in _roots)
            if (EdenVfs.ResolveInsensitive(r, rel) is { } hit)
                return hit;
        return null;
    }

    private static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_tried) return;
            _tried = true;
            try
            {
                // npclist.ini : [Table] DB<n>=code,kfm,scale,...tex,...
                // col0 = code « n002030 », col1 = « m002.kfm »,
                // col2 = scale, col8 = texture variante.
                var listPath = Find("data/db/npclist.ini");
                var npcPath = Find("data/db/c_npc.ini");
                if (listPath == null || npcPath == null)
                    return;
                var byCode = new Dictionary<string, Def>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var raw in EdenVfs.ReadLines(listPath))
                {
                    var eq = raw.IndexOf('=');
                    if (eq <= 0 || !raw.StartsWith("DB",
                            StringComparison.Ordinal))
                        continue;
                    var f = raw[(eq + 1)..].Split(',');
                    if (f.Length < 2 || f[1].Length < 4) continue;
                    var stem = f[1].Trim();
                    if (stem.EndsWith(".kfm",
                            StringComparison.OrdinalIgnoreCase))
                        stem = stem[..^4];
                    var sc = IniTable.GetFloat(f, 2);
                    var tex = f.Length > 8 ? f[8].Trim() : "";
                    if (tex.Equals("null", StringComparison.OrdinalIgnoreCase))
                        tex = "";
                    byCode[f[0].Trim()] = new Def
                    {
                        Stem = stem,
                        Texture = tex,
                        Scale = sc > 0f ? sc : 1f,
                    };
                }
                var npc = IniTable.ParseFile(npcPath, EdenEncoding.Big5);
                var defs = new Dictionary<int, Def>(npc.Rows.Count);
                foreach (var row in npc.Rows)
                {
                    if (row.Length < 2 ||
                        !int.TryParse(row[0], out var id))
                        continue;
                    if (byCode.TryGetValue(row[1].Trim(), out var d))
                        defs[id] = d;
                }
                _defs = defs;
            }
            catch
            {
                // Table absente/corrompue : le registre retombe sur la
                // scène/placeholder — pas de fuite d'exception réseau.
            }
        }
    }
}
