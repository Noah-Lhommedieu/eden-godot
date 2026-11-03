namespace EdenClient.World;

/// <summary>
/// Plan de placement de l'herbe — classe PURE (pas de Godot) qui
/// groupe les instances GRS d'un <see cref="FsmFile"/> par
/// (typeId, cellule grille) et applique le plafond d'instances.
///
/// Cellules : carrés de <c>cellSize</c> unités monde (64 u = la
/// tuile terrain) — une MultiMesh par cellule pour un frustum culling
/// utile (le AABB de la cellule borne la multimesh).
///
/// Plafond : si le total dépasse <paramref name="maxTotal"/> (maps
/// denses : S025 ≈ 31 000 instances), chaque cellule conserve un
/// ratio proportionnel d'instances, échantillonnées à pas régulier
/// (répartition spatiale uniforme, déterministe).
///
/// HYPOTHESIS GRS : les champs <c>ParamA</c>/<c>ParamB</c> (+0x18/
/// +0x1c) ne sont pas prouvés (rotation ? couleur ?) — ignorés ; la
/// rotation de chaque instance est un hash déterministe de son index.
/// </summary>
public static class GrassLayout
{
    /// <summary>Une cellule d'instances d'un même type d'herbe.</summary>
    public readonly record struct Cell(
        uint TypeId, int CellX, int CellY, GrassInstance[] Instances);

    /// <summary>Groupe + plafonne les instances d'herbe du fsm.
    /// <paramref name="cellSize"/> = taille de cellule monde (64 u
    /// typique) ; <paramref name="maxTotal"/> = plafond global.</summary>
    public static List<Cell> Plan(FsmFile fsm, float cellSize,
        int maxTotal)
    {
        // Groupage stable par (typeId, cellule) — l'ordre du fichier
        // est conservé au sein d'une cellule (échantillonnage
        // déterministe).
        var buckets = new Dictionary<(uint, int, int), List<GrassInstance>>();
        var total = 0;
        foreach (var g in fsm.GrassInstances)
        {
            var cx = (int)Math.Floor(g.Position.X / cellSize);
            var cy = (int)Math.Floor(g.Position.Y / cellSize);
            var key = (g.TypeId, cx, cy);
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<GrassInstance>();
            list.Add(g);
            total++;
        }

        var cells = new List<Cell>(buckets.Count);
        if (total == 0 || maxTotal <= 0) return cells;
        var keepRatio = Math.Min(1.0, (double)maxTotal / total);
        foreach (var (key, list) in buckets)
        {
            var keep = keepRatio >= 1.0
                ? list.Count
                : Math.Clamp((int)Math.Round(list.Count * keepRatio),
                    1, list.Count);
            var sel = keep >= list.Count
                ? list.ToArray()
                : StrideSample(list, keep);
            cells.Add(new Cell(key.Item1, key.Item2, key.Item3, sel));
        }
        return cells;
    }

    /// <summary>Stem de modèle pour un typeId GRL (« UE704M » depuis
    /// « UE704M.nif ») — null si le type est inconnu.</summary>
    public static string? ModelStem(FsmFile fsm, uint typeId)
    {
        foreach (var t in fsm.GrassTypes)
            if (t.TypeId == typeId)
            {
                var n = t.ModelName;
                var dot = n.LastIndexOf('.');
                return dot > 0 ? n[..dot] : n;
            }
        return null;
    }

    /// <summary>Hash déterministe d'index → yaw [0, 2π) (ParamA/B
    /// natifs non prouvés — rotation pseudo-aléatoire stable).</summary>
    public static float Yaw(int index)
    {
        unchecked
        {
            var h = (uint)(index * 2654435761u);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            return (h & 0xFFFF) * (MathF.PI * 2f) / 65536f;
        }
    }

    /// <summary>Échantillonnage à pas régulier : garde
    /// <paramref name="keep"/> éléments uniformément répartis.</summary>
    private static GrassInstance[] StrideSample(List<GrassInstance> src,
        int keep)
    {
        var dst = new GrassInstance[keep];
        var step = (double)src.Count / keep;
        for (var i = 0; i < keep; i++)
            dst[i] = src[(int)(i * step)];
        return dst;
    }
}
