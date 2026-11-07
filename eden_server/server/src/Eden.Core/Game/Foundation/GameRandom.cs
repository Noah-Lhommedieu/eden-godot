using System.Security.Cryptography;
using System.Text;

namespace Eden.Core.Game.Foundation;

/// <summary>
/// Source d'aléatoire injectable. Le code de jeu n'utilise jamais
/// <c>Random.Shared</c> directement : les tirages (récolte, loot, craft,
/// rotation de l'éphéméride) deviennent reproductibles en test avec
/// <see cref="SeededRandom"/>.
/// </summary>
public interface IRandom
{
    /// <summary>Réel uniforme dans [0 ; 1[.</summary>
    double NextDouble();

    /// <summary>Entier uniforme dans [minIncl ; maxExcl[.</summary>
    int Next(int minIncl, int maxExcl);

    /// <summary>Flux dérivé et indépendant, déterministe pour un nom donné
    /// (ex. <c>Fork("almanax:2026-11-07")</c>).</summary>
    IRandom Fork(string stream);
}

/// <summary>Opérations dérivées communes à toute <see cref="IRandom"/>.</summary>
public static class RandomExtensions
{
    /// <summary>Vrai avec la probabilité <paramref name="percent"/> % (0-100).</summary>
    public static bool Chance(this IRandom r, double percent) =>
        percent > 0 && r.NextDouble() * 100.0 < percent;

    /// <summary>Choisit un élément selon des poids ≥ 0 ; -1 si aucun poids positif.</summary>
    public static int PickIndex(this IRandom r, IReadOnlyList<double> weights)
    {
        double total = 0;
        for (var i = 0; i < weights.Count; i++)
            total += Math.Max(0, weights[i]);
        if (total <= 0) return -1;
        var roll = r.NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0) return i;
        }
        return weights.Count - 1;
    }

    /// <summary>Élément au hasard dans une liste non vide.</summary>
    public static T Pick<T>(this IRandom r, IReadOnlyList<T> items) =>
        items[r.Next(0, items.Count)];
}

/// <summary>Aléatoire de production : le seul endroit autorisé à utiliser
/// <see cref="Random.Shared"/>.</summary>
public sealed class SystemRandom : IRandom
{
    public double NextDouble() => Random.Shared.NextDouble();
    public int Next(int minIncl, int maxExcl) =>
        Random.Shared.Next(minIncl, maxExcl);
    public IRandom Fork(string stream) =>
        new SeededRandom(Random.Shared.Next(), stream);
}

/// <summary>Aléatoire déterministe (tests, éphéméride) : même graine et
/// même flux ⇒ même séquence.</summary>
public sealed class SeededRandom : IRandom
{
    private readonly Random _rng;
    private readonly int _seed;

    public SeededRandom(int seed, string stream = "")
    {
        _seed = seed;
        _rng = new Random(Mix(seed, stream));
    }

    public double NextDouble() => _rng.NextDouble();
    public int Next(int minIncl, int maxExcl) => _rng.Next(minIncl, maxExcl);
    public IRandom Fork(string stream) => new SeededRandom(_seed, stream);

    private static int Mix(int seed, string stream)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{seed}|{stream}"));
        return BitConverter.ToInt32(bytes, 0);
    }
}
