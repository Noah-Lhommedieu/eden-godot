using EdenClient.Game;
using EdenClient.World;
using Godot;
using GDict = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;

namespace EdenClient.Native;

/// <summary>
/// .kf EDENFR → <see cref="Animation"/> Godot (EDEN_NATIVE=anim, E2/E3/E9). Les pistes visent les os/nœuds par
/// leur nom NIF (aucun retarget) ; chargement <b>paresseux par séquence</b> : un .kf de 24 Mo (char/animation/b.kf,
/// 391 séquences) n'est qu'indexé à l'ouverture (~20 ms), chaque séquence est décodée + cuite à 30 Hz (~1-5 ms) à
/// la première demande puis mise en cache (<see cref="KfSource"/>). Le mode de boucle vient du cycle de la séquence
/// (Loop → Linear, Reverse → PingPong, Clamp → aucun). Fréquence ≠ 1 : temps de clés divisés par la fréquence.
/// Text keys → pistes d'appel <c>OnAnimTextKey</c> sur le nœud « EntityAnimator » (Hit / Footstep / Sound / Effect).
/// </summary>
public static class KfAnimation
{
    /// <summary>Nom du nœud cible des pistes d'appel (frère du AnimationPlayer, enfant de la racine modèle).</summary>
    public const string EventNode = "EntityAnimator";
    public const string EventMethod = "OnAnimTextKey";

    public static bool Enabled => NativeMode.Anim && NativeMode.Skin;

    /// <summary>Séquence cuite → Animation. <paramref name="resolve"/> : nom de cible NIF → chemins de piste
    /// (un par squelette/nœud homonyme), null si absent du modèle.</summary>
    public static Animation Build(KfBakedSequence seq, Func<string, IReadOnlyList<string>?> resolve, out int dropped)
    {
        dropped = 0;
        var anim = new Animation();
        var k = 1f / Math.Max(seq.Frequency, 1e-6f);
        anim.Length = Math.Max(seq.Duration * k, 0.001f);
        anim.LoopMode = seq.Cycle switch
        {
            KfCycle.Loop => Animation.LoopModeEnum.Linear,
            KfCycle.Reverse => Animation.LoopModeEnum.Pingpong,
            _ => Animation.LoopModeEnum.None,
        };
        foreach (var tr in seq.Tracks)
        {
            var paths = resolve(tr.NodeName);
            if (paths == null) { dropped++; continue; }
            foreach (var p in paths)
            {
                if (tr.Rot.Length > 0) AddRot(anim, p, tr, k);
                if (tr.Pos.Length > 0) AddPos(anim, p, tr, k);
                if (HasScale(tr)) AddScl(anim, p, tr, k);
            }
        }
        AddEvents(anim, seq, k);
        return anim;
    }

    static bool HasScale(KfBakedTrack t)
    {
        if (t.Scl.Length == 0) return false;
        foreach (var s in t.Scl) if (MathF.Abs(s - 1f) > 1e-4f) return true;
        return false;                         // échelle constante 1 : rien à animer
    }

    static void AddRot(Animation a, string path, KfBakedTrack t, float k)
    {
        var i = a.AddTrack(Animation.TrackType.Rotation3D);
        a.TrackSetPath(i, path);
        var last = Quaternion.Identity;
        for (var n = 0; n < t.Rot.Length; n++)
        {
            var q = new Quaternion(t.Rot[n].X, t.Rot[n].Y, t.Rot[n].Z, t.Rot[n].W);
            if (!float.IsFinite(q.X + q.Y + q.Z + q.W) || q.LengthSquared() < 1e-6f) q = last;
            else { q = q.Normalized(); last = q; }
            a.RotationTrackInsertKey(i, t.RotTimes[n] * k, q);
        }
    }

    static void AddPos(Animation a, string path, KfBakedTrack t, float k)
    {
        var i = a.AddTrack(Animation.TrackType.Position3D);
        a.TrackSetPath(i, path);
        var last = Vector3.Zero;
        for (var n = 0; n < t.Pos.Length; n++)
        {
            var v = new Vector3(t.Pos[n].X, t.Pos[n].Y, t.Pos[n].Z);
            if (!float.IsFinite(v.X + v.Y + v.Z) || v.LengthSquared() > 1e8f) v = last; else last = v;
            a.PositionTrackInsertKey(i, t.PosTimes[n] * k, v);
        }
    }

    static void AddScl(Animation a, string path, KfBakedTrack t, float k)
    {
        var i = a.AddTrack(Animation.TrackType.Scale3D);
        a.TrackSetPath(i, path);
        var last = 1f;
        for (var n = 0; n < t.Scl.Length; n++)
        {
            var s = t.Scl[n];
            if (!float.IsFinite(s) || s <= 0f) s = last; else last = s;
            a.ScaleTrackInsertKey(i, t.SclTimes[n] * k, Vector3.One * s);
        }
    }

    static void AddEvents(Animation a, KfBakedSequence seq, float k)
    {
        if (seq.TextKeys.Count == 0) return;
        int track = -1;
        foreach (var ev in AnimTextKeys.Build(seq.Name, seq.TextKeys))
        {
            if (ev.Kind is not (AnimEventKind.Hit or AnimEventKind.Footstep or AnimEventKind.Sound
                                or AnimEventKind.EffectSpawn)) continue;
            if (track < 0)
            {
                track = a.AddTrack(Animation.TrackType.Method);
                a.TrackSetPath(track, EventNode);
            }
            a.TrackInsertKey(track, ev.Time * k, new GDict
            {
                ["method"] = EventMethod,
                ["args"] = new GArray { (int)ev.Kind, ev.Index, ev.Key },
            });
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Table nom de cible NIF → chemins de piste, depuis l'arbre natif (NifScene) d'un modèle.

    public sealed class PathMap
    {
        readonly Dictionary<string, List<string>> _bones = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<string>> _nodes = new(StringComparer.OrdinalIgnoreCase);
        string? _sig;

        /// <summary>Un os l'emporte sur un nœud homonyme (comme l'import glTF).</summary>
        public IReadOnlyList<string>? Resolve(string target) =>
            _bones.TryGetValue(target, out var b) ? b
            : _nodes.TryGetValue(target, out var n) ? n : null;

        public string Signature => _sig ??= string.Join('\x1e',
            _bones.OrderBy(k => k.Key, StringComparer.Ordinal).Select(kv => "b" + kv.Key + string.Join("|", kv.Value))
                .Concat(_nodes.OrderBy(k => k.Key, StringComparer.Ordinal)
                    .Select(kv => "n" + kv.Key + string.Join("|", kv.Value))));

        public static PathMap Build(Node3D root)
        {
            var m = new PathMap();
            Walk(root, root, m);
            return m;
        }

        static void Add(Dictionary<string, List<string>> d, string name, string path)
        {
            if (!d.TryGetValue(name, out var l)) d[name] = l = new List<string>(1);
            l.Add(path);
        }

        static void Walk(Node root, Node cur, PathMap m)
        {
            foreach (var c in cur.GetChildren())
            {
                if (c is AnimationPlayer || c.Name == EventNode) continue;
                var rel = Rel(root, c);
                if (c is Skeleton3D sk)
                {
                    for (var i = 0; i < sk.GetBoneCount(); i++)
                    {
                        var bn = sk.GetBoneName(i);
                        Add(m._bones, bn, rel + ":" + bn);
                    }
                }
                else if (!(c is BoneAttachment3D && cur is Skeleton3D ps && ps.FindBone(c.Name) >= 0))
                    Add(m._nodes, c.Name, rel);
                Walk(root, c, m);
            }
        }

        static string Rel(Node root, Node n)
        {
            var parts = new List<string>();
            for (var c = n; c != root && c != null; c = c.GetParent()) parts.Add(c.Name.ToString());
            parts.Reverse();
            return string.Join('/', parts);
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Bibliothèque paresseuse partagée par tous les modèles de même signature.

    static readonly Dictionary<string, PathMap> s_maps = new();
    static readonly Dictionary<string, KfAnimSet?> s_sets = new();

    /// <summary>Jeu d'animations d'un .kf pour un modèle (null si pas de .kf). Mis en cache par
    /// (animRelPath, modelKey ou signature de l'arbre) : N instances partagent la même librairie.</summary>
    public static KfAnimSet? GetSet(string animRelPath, Node3D modelRoot, string? modelKey)
    {
        var src = KfSource.Get(animRelPath);
        if (src == null) return null;
        PathMap? map = null;
        string? mapKey = modelKey == null ? null : animRelPath + "\x1f" + modelKey;
        if (mapKey != null) s_maps.TryGetValue(mapKey, out map);
        if (map == null)
        {
            map = PathMap.Build(modelRoot);
            if (mapKey != null) s_maps[mapKey] = map;
        }
        var key = animRelPath + "\x1f" + map.Signature;
        if (!s_sets.TryGetValue(key, out var set) || set == null)
            s_sets[key] = set = new KfAnimSet(src, map);
        return set;
    }
}

/// <summary>Librairie d'un (kf, modèle) : <see cref="Ensure"/> décode/ajoute une séquence à la demande.</summary>
public sealed class KfAnimSet
{
    public readonly AnimationLibrary Lib = new();
    public readonly KfSource Source;
    readonly KfAnimation.PathMap _map;
    readonly HashSet<string> _tried = new(StringComparer.OrdinalIgnoreCase);
    public int Dropped;
    public KfAnimSet(KfSource src, KfAnimation.PathMap map) { Source = src; _map = map; }

    readonly System.Collections.Concurrent.ConcurrentQueue<string> _warmReady = new();
    readonly HashSet<string> _warmAsked = new(StringComparer.OrdinalIgnoreCase);
    int _warmPending;

    /// <summary>Vrai tant qu'un préchauffage a des séquences cuites à construire (voir <see cref="PumpWarm"/>).</summary>
    public bool WarmPending => System.Threading.Volatile.Read(ref _warmPending) > 0;

    /// <summary>Préchauffe <paramref name="names"/> : la cuisson (décodage .kf + 30 Hz, pur C# thread-safe) part hors
    /// thread principal, dans l'ordre donné ; la construction de l'<see cref="Animation"/> Godot reste sur le thread
    /// principal, une séquence par appel de <see cref="PumpWarm"/>. Sans ça, la 1re attaque paie cuisson + construction
    /// d'un coup (image perdue, animation en retard sur le 272).</summary>
    public void WarmAsync(IEnumerable<string> names)
    {
        var todo = names.Where(n => _warmAsked.Add(n)).ToArray();
        if (todo.Length == 0) return;
        System.Threading.Interlocked.Add(ref _warmPending, todo.Length);
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var n in todo)
            {
                try { Source.Baked(n); }
                finally { _warmReady.Enqueue(n); }
            }
        });
    }

    /// <summary>Thread principal : construit au plus une séquence déjà cuite (coût ~ordre de la ms).</summary>
    public void PumpWarm()
    {
        if (!_warmReady.TryDequeue(out var n)) return;
        System.Threading.Interlocked.Decrement(ref _warmPending);
        Ensure(n);
    }

    /// <summary>Vrai si l'animation <paramref name="name"/> est (désormais) dans la librairie.</summary>
    public bool Ensure(string name)
    {
        if (Lib.HasAnimation(name)) return true;
        if (!_tried.Add(name)) return false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var baked = Source.Baked(name);
        if (baked == null) return false;
        var bakeMs = sw.Elapsed.TotalMilliseconds;
        var anim = KfAnimation.Build(baked, _map.Resolve, out var dropped);
        Dropped += dropped;
        Combat.AttackProbe.Ensure(System.IO.Path.GetFileName(Source.Path), name, bakeMs, sw.Elapsed.TotalMilliseconds - bakeMs);
        if (System.Environment.GetEnvironmentVariable("EDEN_ANIM_TRACE") == "1")
            GD.Print($"[kfanim] {System.IO.Path.GetFileName(Source.Path)}:{name} pistes={anim.GetTrackCount()} perdues={dropped} durée={anim.Length:F2}s boucle={anim.LoopMode} cycle={baked.Cycle} freq={baked.Frequency}");
        if (System.Environment.GetEnvironmentVariable("EDEN_ANIM_TRACE") == "1")
            for (var ti = 0; ti < anim.GetTrackCount(); ti++)
                if (anim.TrackGetType(ti) == Animation.TrackType.Position3D && anim.TrackGetKeyCount(ti) > 1)
                {
                    var a0 = (Vector3)anim.TrackGetKeyValue(ti, 0); var a1 = (Vector3)anim.TrackGetKeyValue(ti, anim.TrackGetKeyCount(ti) - 1);
                    if ((a1 - a0).Length() > 0.05f) GD.Print($"[kfanim]   dérive {anim.TrackGetPath(ti)} {a0} → {a1}");
                }
        Lib.AddAnimation(name, anim);
        return true;
    }
}

/// <summary>Un .kf ouvert (indexé), séquences cuites mises en cache. Thread-safe (préchargement parallèle).</summary>
public sealed class KfSource
{
    readonly KfFile _kf;
    readonly Dictionary<string, KfBakedSequence?> _baked = new(StringComparer.OrdinalIgnoreCase);
    public string Path { get; }
    public KfFile Kf => _kf;
    public static double TotalOpenMs, TotalBakeMs;
    public static int Opened, Baked_;

    KfSource(string path, KfFile kf) { Path = path; _kf = kf; }

    static readonly Dictionary<string, KfSource?> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ouvre le .kf <c>&lt;animRelPath&gt;.kf</c> sous la racine de données (insensible à la casse).</summary>
    public static KfSource? Get(string animRelPath)
    {
        lock (s_cache)
            if (s_cache.TryGetValue(animRelPath, out var c)) return c;
        KfSource? s = null;
        var abs = AssetCatalog.ResolveRawAsset(animRelPath + ".kf");
        if (abs != null) s = Open(abs);
        lock (s_cache)
        {
            if (s_cache.TryGetValue(animRelPath, out var raced)) return raced;
            s_cache[animRelPath] = s;
            if (abs != null) s_byAbs[abs] = s;
        }
        return s;
    }

    static readonly Dictionary<string, KfSource?> s_byAbs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Par chemin absolu (KFM : .kf cités en relatif au KFM).</summary>
    public static KfSource? GetAbs(string abs)
    {
        lock (s_cache) if (s_byAbs.TryGetValue(abs, out var c)) return c;
        var s = Open(abs);
        lock (s_cache) { if (s_byAbs.TryGetValue(abs, out var r)) return r; s_byAbs[abs] = s; }
        return s;
    }

    static KfSource? Open(string abs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var kf = KfFile.Load(abs);
            lock (s_cache) { TotalOpenMs += sw.Elapsed.TotalMilliseconds; Opened++; }
            return new KfSource(abs, kf);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            GD.PrintErr($"[kfanim] {abs} : {ex.Message}");
            return null;
        }
    }

    readonly object _kfLock = new();
    readonly Dictionary<string, Lazy<KfBakedSequence?>> _lazy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Séquence décodée + cuite à 30 Hz (cache), null si absente/corrompue.
    /// Verrou PAR SÉQUENCE (Lazy) : l'ancien verrou global tenu pendant les ~100 ms de cuisson
    /// bloquait le thread principal dès qu'un worker préchauffait → freeze après chaque kill.</summary>
    public KfBakedSequence? Baked(string name)
    {
        Lazy<KfBakedSequence?> lz;
        lock (_baked)
        {
            if (!_lazy.TryGetValue(name, out lz!))
                _lazy[name] = lz = new Lazy<KfBakedSequence?>(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    KfSequence? seq;
                    lock (_kfLock) seq = _kf.Get(name);          // décodage : non thread-safe
                    var b = seq?.Bake(30f, measure: false);      // cuisson : hors verrou
                    lock (s_cache) { TotalBakeMs += sw.Elapsed.TotalMilliseconds; Baked_++; }
                    return b;
                }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        }
        return lz.Value;
    }

    public static string Stats() =>
        $"kf ouverts={Opened} ({TotalOpenMs:F0} ms) séquences cuites={Baked_} ({TotalBakeMs:F0} ms)";
}
