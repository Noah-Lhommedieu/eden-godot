using EdenClient.World;
using Godot;

namespace EdenClient.Combat;

/// <summary>Préchauffe, hors thread principal, ce qu'un sort coûte à sa première utilisation : lecture + décodage du .nif
/// (cache <see cref="AssetCatalog.GetNifCached"/>) et décodage/upload des textures d'effet. Mesuré (balayage de 2 345 effets
/// à particules) : ~20 ms par effet froid sur le thread principal — une image perdue à chaque nouveau sort.</summary>
public static class SpellFxWarmup
{
    static readonly HashSet<string> s_done = new(StringComparer.OrdinalIgnoreCase);
    static readonly System.Collections.Concurrent.BlockingCollection<string> s_queue = new();
    static int s_worker;

    /// <summary>À appeler quand un sort devient utilisable (barre de raccourcis, apprentissage, entrée en jeu).</summary>
    public static void Warm(int spellId)
    {
        foreach (var fx in new[] { SpellFxTable.CastFx(spellId), SpellFxTable.HitFx(spellId) })
            if (fx is { } f) Enqueue(f.Model);
    }

    static void Enqueue(string model)
    {
        lock (s_done) if (!s_done.Add(model)) return;
        s_queue.Add(model);
        if (System.Threading.Interlocked.Exchange(ref s_worker, 1) == 0)
            _ = System.Threading.Tasks.Task.Run(Work);
    }

    static void Work()
    {
        foreach (var model in s_queue.GetConsumingEnumerable())
        {
            try
            {
                var p = AssetCatalog.ResolveRawAsset($"effect/model/{model}.nif");
                if (p == null) continue;
                var nif = AssetCatalog.GetNifCached(p);
                if (nif == null) continue;
                foreach (var prop in nif.Properties)
                    if (prop is NifTexturingProperty t)
                        foreach (var m in t.AllMaps())
                            if (m.File is { Length: > 0 } f)
                                AssetCatalog.GetTexture("effect/texture/" + f);
            }
            catch (Exception e) { GD.PrintErr($"[fxwarm] {model} : {e.Message}"); }
        }
    }
}
