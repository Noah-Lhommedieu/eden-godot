using System.Collections.Concurrent;

namespace Eden.Zone.Spatial;

/// <summary>
/// Grille de partitionnement spatial (Area of Interest - AOI) gérant la visibilité et la diffusion de proximité.
/// Reversé depuis lapis::CGridMap.
/// </summary>
public sealed class SpatialGrid
{
    public float CellSize { get; }
    private readonly ConcurrentDictionary<(int CellX, int CellY), GridCell> _cells = new();
    private readonly ConcurrentDictionary<int, (int CellX, int CellY)> _entityLocations = new();

    public SpatialGrid(float cellSize = 50.0f)
    {
        CellSize = cellSize > 0 ? cellSize : 50.0f;
    }

    public (int CellX, int CellY) GetCellCoords(float x, float y)
    {
        var cx = (int)MathF.Floor(x / CellSize);
        var cy = (int)MathF.Floor(y / CellSize);
        return (cx, cy);
    }

    public GridCell GetOrCreateCell(int cellX, int cellY)
    {
        return _cells.GetOrAdd((cellX, cellY), k => new GridCell(k.CellX, k.CellY));
    }

    public void UpdateEntityPosition(int entityId, object entity, float x, float y)
    {
        var (newCx, newCy) = GetCellCoords(x, y);

        if (_entityLocations.TryGetValue(entityId, out var oldCoords))
        {
            if (oldCoords.CellX == newCx && oldCoords.CellY == newCy)
            {
                return; // Même cellule
            }

            if (_cells.TryGetValue(oldCoords, out var oldCell))
            {
                oldCell.RemoveEntity(entityId);
            }
        }

        var newCell = GetOrCreateCell(newCx, newCy);
        newCell.AddEntity(entityId, entity);
        _entityLocations[entityId] = (newCx, newCy);
    }

    public void RemoveEntity(int entityId)
    {
        if (_entityLocations.TryRemove(entityId, out var coords))
        {
            if (_cells.TryGetValue(coords, out var cell))
            {
                cell.RemoveEntity(entityId);
            }
        }
    }

    /// <summary>
    /// Récupère l'ensemble des IDs d'entités situées dans la cellule courante et ses 8 voisines (champ de vision).
    /// </summary>
    public HashSet<int> GetNearbyEntityIds(float x, float y)
    {
        var (cx, cy) = GetCellCoords(x, y);
        var nearby = new HashSet<int>();

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (_cells.TryGetValue((cx + dx, cy + dy), out var cell))
                {
                    foreach (var id in cell.GetEntityIds())
                    {
                        nearby.Add(id);
                    }
                }
            }
        }

        return nearby;
    }
}
