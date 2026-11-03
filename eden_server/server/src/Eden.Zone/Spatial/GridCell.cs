using System.Collections.Concurrent;

namespace Eden.Zone.Spatial;

/// <summary>
/// Cellule spatiale contenant les entités présentes dans une partition géographique de la carte.
/// </summary>
public sealed class GridCell
{
    public int CellX { get; }
    public int CellY { get; }
    private readonly ConcurrentDictionary<int, object> _entities = new();

    public GridCell(int cellX, int cellY)
    {
        CellX = cellX;
        CellY = cellY;
    }

    public void AddEntity(int entityId, object entity)
    {
        _entities[entityId] = entity;
    }

    public bool RemoveEntity(int entityId)
    {
        return _entities.TryRemove(entityId, out _);
    }

    public IReadOnlyCollection<int> GetEntityIds() => _entities.Keys.ToList();
}
