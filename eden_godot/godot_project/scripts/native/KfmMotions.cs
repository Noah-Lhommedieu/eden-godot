using EdenClient.Game;
using EdenClient.World;
using Godot;

namespace EdenClient.Native;

/// <summary>
/// KFM natif d'un modèle (E4) : remplace les <c>.kfm.json</c> dérivés et les durées de fondu codées en dur.
/// Donne, par nom de séquence, l'id d'événement KFM et la transition (type + durée) entre deux animations via
/// <see cref="KfmStateMachine.Resolve"/> (entrée du KFM, sinon défaut non-sync/sync du header). Le fondu lui-même
/// reste porté par l'AnimationPlayer (<c>Play(name, blend)</c>) : même courbe linéaire que Blend/Crossfade/Chain ;
/// seul Morph (sortant figé) n'est pas reproduit.
/// </summary>
public sealed class KfmMotions
{
    public readonly KfmStateMachine Machine;
    readonly Dictionary<string, int> _event = new(StringComparer.OrdinalIgnoreCase);

    KfmMotions(KfmStateMachine m)
    {
        Machine = m;
        foreach (var c in m.Clips) _event.TryAdd(c.Name, c.Event);
    }

    static readonly Dictionary<string, KfmMotions?> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>KFM d'un chemin d'anim (« monster/animation/M025 » → « monster/M025.kfm »). Null si absent.</summary>
    public static KfmMotions? For(string animRelPath)
    {
        lock (s_cache)
            if (s_cache.TryGetValue(animRelPath, out var c)) return c;
        KfmMotions? r = null;
        var i = animRelPath.LastIndexOf('/');
        var dir = i < 0 ? "" : animRelPath[..i];
        var stem = animRelPath[(i + 1)..];
        if (dir.EndsWith("/animation", StringComparison.OrdinalIgnoreCase) || dir.Equals("animation", StringComparison.OrdinalIgnoreCase))
        {
            var kdir = dir.Length > 10 ? dir[..^10] : "";
            var abs = AssetCatalog.ResolveRawAsset((kdir.Length > 0 ? kdir + "/" : "") + stem + ".kfm");
            if (abs != null)
            {
                try
                {
                    var kfm = KfmFile.Load(abs);
                    var kdirRel = kdir;
                    r = new KfmMotions(new KfmStateMachine(kfm, p =>
                    {
                        var rel = NativeBoneNames.KfmSibling(kdirRel, p);
                        var a = AssetCatalog.ResolveRawAsset(rel);
                        return a == null ? null : KfSource.GetAbs(a)?.Kf;
                    }));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
                {
                    GD.PrintErr($"[kfm] {abs} : {ex.Message}");
                }
            }
        }
        lock (s_cache) s_cache[animRelPath] = r;
        return r;
    }

    public bool Has(string name) => _event.ContainsKey(name);

    /// <summary>Durée de fondu (s) de <paramref name="from"/> vers <paramref name="to"/> ; <paramref name="fallback"/>
    /// si l'une des deux n'est pas une action du KFM.</summary>
    public float BlendSeconds(string from, string to, float fallback)
    {
        if (!_event.TryGetValue(to, out var t)) return fallback;
        return Machine.Resolve(_event.GetValueOrDefault(from, int.MinValue), t).Duration;
    }
}
