using Godot;

namespace EdenClient.Game;

/// <summary>
/// Cache de corps de personnages pour les écrans pré-jeu : les protos
/// (<see cref="CharVisual.BuildBody"/> ≈ 1-2 s à froid — ~390 meshes
/// + teintes) sont conservés DÉTACHÉS, indexés par empreinte
/// d'apparence ; chaque utilisation en retourne un
/// <c>Duplicate()</c> (~50 ms, meshes/materials partagés).
///
/// Thread : lecture/écriture sous verrou ; les nodes restent hors
/// arbre (même convention que BuildBody — construction supportée en
/// worker). Le proto n'est jamais attaché — le Duplicate est le corps
/// affiché ; CharDressing s'applique donc toujours sur une copie.
/// </summary>
public static class CharBodyCache
{
    // Un proto par perso de liste + quelques variantes de création —
    // borné par cap (pas d'éviction : le volume utile est petit).
    private const int MaxProtos = 16;
    private static readonly Dictionary<string, Node3D> _protos = new();
    // Builds en vol par clé : sans ça le prewarm et le clic sur le même
    // perso construisaient le corps en double (~5 s de GLB+teintes ×2).
    private static readonly Dictionary<string, SemaphoreSlim>
        _building = new();

    /// <summary>Clé de cache = empreinte hex du blob apparence.</summary>
    private static string KeyOf(byte[]? app) =>
        app == null ? "" : Convert.ToHexString(app);

    /// <summary>Corps prêt pour une apparence : Duplicate du proto
    /// en cache ou BuildBody complet (le résultat devient alors le
    /// proto et un Duplicate est retourné). Aucune animation
    /// attachée (attachAnims:false) — l'appelant rattache
    /// l'animator sur le thread principal comme avant.</summary>
    public static Node3D? GetOrBuild(byte[]? app)
    {
        var (n, cached) = BuildCore(app);
        return n == null ? null
            : cached ? n.Duplicate() as Node3D : n;
    }

    /// <summary>Construit le proto si absent — À APPELER depuis un
    /// worker (rien n'est retourné ni affiché : c'est le cache qui
    /// compte).</summary>
    public static void Prewarm(byte[]? app)
    {
        var (n, cached) = BuildCore(app);
        if (n != null && !cached) n.Free();   // hors cap : rien à montrer
    }

    /// <summary>Vide le cache à la sortie de la scène de jeu —
    /// les protos sont des Node3D détachés : <c>Free()</c> explicite
    /// sinon ObjectDB les compte fuités à l'arrêt.</summary>
    public static void ReleaseCache()
    {
        lock (_protos)
        {
            foreach (var n in _protos.Values)
                if (GodotObject.IsInstanceValid(n)) n.Free();
            _protos.Clear();
        }
    }

    // Retourne (proto caché, true) ou (corps frais hors cap, false) ou
    // (null, false). Tous les workers d'une même clé convergent sur UN
    // build : les suivants attendent le portail puis relisent le proto
    // (ou prennent le relais si le build a échoué). Workers uniquement
    // — gate.Wait() bloquerait le thread principal.
    private static (Node3D? node, bool cached) BuildCore(byte[]? app)
    {
        var key = KeyOf(app);
        while (true)
        {
            SemaphoreSlim? gate; bool owner;
            lock (_protos)
            {
                if (_protos.TryGetValue(key, out var p) &&
                    GodotObject.IsInstanceValid(p))
                    return (p, true);
                owner = !_building.TryGetValue(key, out gate);
                if (owner) _building[key] = gate = new SemaphoreSlim(0);
            }
            if (!owner) { gate!.Wait(); continue; }
            try
            {
                var body = CharVisual.BuildBody(app, attachAnims: false);
                if (body == null) return (null, false);
                lock (_protos)
                {
                    if (_protos.Count >= MaxProtos)
                        return (body, false);
                    _protos[key] = body;
                    return (body, true);
                }
            }
            finally
            {
                lock (_protos) _building.Remove(key);
                gate!.Release(int.MaxValue);
            }
        }
    }
}
