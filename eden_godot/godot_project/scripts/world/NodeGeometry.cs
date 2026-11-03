using EdenClient.Data;

namespace EdenClient.World;

/// <summary>Node (id de zone) → dossier de géométrie <c>map/model/&lt;code&gt;</c>. Le client ne connaissait que
/// <c>S{id:D3}</c> : 224 nodes nommés (655-684, 843-858, 601-640…) n'ont pas de dossier sous leur id et restaient
/// injouables (docs/reference/MAPS.md §6, C1). Ordre : <c>S{id}</c>, puis la colonne « code de map » de <c>c_node.ini</c>,
/// puis la colonne minimap (<c>S###M</c> → <c>S###</c>) — le jeu d'origine réutilise ces modèles pour des variantes.</summary>
public static class NodeGeometry
{
    static Dictionary<int, string[]>? s_codes;
    static readonly object s_lock = new();

    static Dictionary<int, string[]> Load()
    {
        lock (s_lock)
        {
            if (s_codes != null) return s_codes;
            var d = new Dictionary<int, string[]>();
            try
            {
                var path = AssetCatalog.ResolveRawAsset("data/db/c_node.ini");
                if (path != null)
                    foreach (var row in IniTable.ParseFile(path).Rows)
                    {
                        if (row.Length < 6 || !int.TryParse(row[0], out var id)) continue;
                        var mini = row[5].Trim();
                        if (mini.EndsWith('M') || mini.EndsWith('m')) mini = mini[..^1];
                        d[id] = new[] { row[4].Trim(), mini };
                    }
            }
            catch (Exception e) { Godot.GD.PrintErr($"[node] c_node.ini : {e.Message}"); }
            return s_codes = d;
        }
    }

    /// <summary>Code de géométrie jouable pour <paramref name="nodeId"/>, ou null si aucun dossier n'existe.</summary>
    public static string? Resolve(int nodeId, Func<string, bool> exists)
    {
        var own = $"S{nodeId:D3}";
        if (exists(own)) return own;
        if (Load().TryGetValue(nodeId, out var alt))
            foreach (var c in alt)
                if (c.Length > 1 && exists(c)) return c.ToUpperInvariant();
        return null;
    }
}
