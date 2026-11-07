using Eden.Core.Game.Foundation;

namespace Eden.Core.Game.Loot;

/// <summary>Une entrée d'un tirage pondéré : l'objet, son poids (taux %) et la
/// fourchette de quantité.</summary>
public readonly record struct PoolEntry<T>(T Item, double Weight, int QtyMin, int QtyMax);

/// <summary>Résultat d'un tirage.</summary>
public readonly record struct PoolDraw<T>(T Item, int Quantity);

/// <summary>
/// Tirage pondéré unique du jeu (PLAN-ACTION-3 §10 Q7) : récolte, drops, coffres,
/// loteries partagent ce modèle. Le hasard est injecté (<see cref="IRandom"/>) :
/// les tirages sont reproductibles en test.
/// </summary>
public sealed class WeightedPool<T>
{
    private readonly PoolEntry<T>[] _entries;
    private readonly double[] _weights;

    public WeightedPool(IEnumerable<PoolEntry<T>> entries)
    {
        _entries = entries.Where(e => e.Weight > 0 && e.QtyMax >= e.QtyMin && e.QtyMin >= 1)
            .ToArray();
        _weights = _entries.Select(e => e.Weight).ToArray();
    }

    public int Count => _entries.Length;

    /// <summary>Un tirage ; null si le pool est vide.</summary>
    public PoolDraw<T>? Draw(IRandom rng)
    {
        var i = rng.PickIndex(_weights);
        if (i < 0) return null;
        var e = _entries[i];
        var qty = e.QtyMin == e.QtyMax ? e.QtyMin : rng.Next(e.QtyMin, e.QtyMax + 1);
        return new PoolDraw<T>(e.Item, qty);
    }

    /// <summary>Probabilité (0-1) de chaque entrée, pour l'admin et les tests.</summary>
    public IReadOnlyList<(T Item, double Probability)> Probabilities()
    {
        var total = _weights.Sum();
        return _entries.Select(e => (e.Item, total <= 0 ? 0 : e.Weight / total)).ToArray();
    }
}
