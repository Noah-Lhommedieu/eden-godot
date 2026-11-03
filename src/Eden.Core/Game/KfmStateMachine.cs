using EdenClient.World;

namespace EdenClient.Game;

/// <summary>Clip jouable d'un KFM : une action (id d'événement) liée à
/// une séquence d'un .kf.</summary>
public sealed record KfmClip(int Event, string Name, float Start, float Stop,
    KfCycle Cycle, float Frequency = 1f)
{
    public float Duration => Math.Max(Stop - Start, 0f);

    /// <summary>Boucle indéfiniment (Loop et Reverse) ; Clamp = une fois.</summary>
    public bool Loops => Cycle != KfCycle.Clamp;

    /// <summary>Temps de lecture local (s depuis le début, ×fréquence)
    /// → temps de séquence dans [Start, Stop] (même règle que
    /// <see cref="KfSequence.Wrap"/>).</summary>
    public float SequenceTime(float elapsed)
    {
        var d = Duration;
        if (d <= 1e-6f) return Start;
        var t = elapsed * Frequency;
        switch (Cycle)
        {
            case KfCycle.Loop:
                t %= d; if (t < 0) t += d; break;
            case KfCycle.Reverse:
                t %= 2 * d; if (t < 0) t += 2 * d;
                if (t > d) t = 2 * d - t; break;
            default:
                t = Math.Clamp(t, 0, d); break;
        }
        return Start + t;
    }
}

/// <summary>Transition résolue entre deux clips.</summary>
public readonly record struct KfmBlend(KfmTransitionType Type, float Duration);

/// <summary>Une couche à appliquer à la pose, de la plus ancienne à la
/// plus récente.</summary>
public readonly record struct KfmLayer(int Event, string Name,
    float SequenceTime, float Weight, float ApplyWeight);

/// <summary>Nature d'un événement de la machine.</summary>
public enum KfmEventKind
{
    /// <summary>Un clip démarre (suite à une demande).</summary>
    Started,
    /// <summary>Un clip Clamp a atteint sa fin (une seule fois).</summary>
    Ended,
    /// <summary>Un clip Loop/Reverse a bouclé.</summary>
    Looped,
    /// <summary>Le fondu est terminé : une seule couche reste.</summary>
    BlendDone,
    /// <summary>Demande ignorée : id d'événement absent du KFM.</summary>
    Rejected,
}

public readonly record struct KfmEvent(KfmEventKind Kind, int Event);

/// <summary>Fenêtre de lecture du clip courant pendant le dernier
/// <see cref="KfmStateMachine.Update"/> (pour déclencher les text keys
/// via <see cref="AnimTextKeys.Collect"/>). Temps en secondes de
/// séquence depuis le début du clip, <b>non repliés</b> (une boucle
/// continue au-delà de la durée), facteur de fréquence appliqué.</summary>
public readonly record struct KfmStep(int Event, float From, float To,
    bool Wrapped);

/// <summary>
/// Machine d'états d'animation issue d'un <c>.kfm</c> (pure, sans
/// Godot) : clip courant, demande, choix de la transition, poids de
/// fondu dans le temps, événements de fin/boucle. Remplace les
/// transitions codées en dur (<c>EntityAnimator.Blend</c>/<c>LocoBlend</c>).
///
/// Transitions (mesuré sur les 856 KFM de fr-full) : la matrice
/// complète N×(N−1) ne contient que des entrées de type 5 sans durée ;
/// seuls comptent les défauts du header (non-sync = type 0 « blend »,
/// 0,1 s ; sync = type 1 « morph », 0,1 s) — d'où <see cref="Resolve"/>.
/// Comportement par type : Blend/Crossfade/Chain = fondu linéaire, les
/// deux clips avancent ; Morph = le clip sortant est figé (hypothèse
/// Gamebryo) ; durée 0 = coupure.
///
/// Utilisation : <see cref="Request"/> (ou <see cref="Queue"/>) puis
/// <see cref="Update"/> à chaque frame ; <see cref="Layers"/> donne les
/// clips à appliquer dans l'ordre avec <see cref="KfmLayer.ApplyWeight"/>
/// (poids incrémental pour <c>KfPose.Apply</c> successifs).
/// </summary>
public sealed class KfmStateMachine
{
    private const int MaxLayers = 4;

    private sealed class Layer
    {
        public KfmClip Clip = null!;
        public float Elapsed;
        public float Share = 1f;       // poids hérité au lancement du suivant
        public float Weight = 1f;      // poids effectif courant
        public bool Frozen;            // Morph : clip sortant figé
        public bool EndedFired;
    }

    private readonly Dictionary<int, KfmClip> _clips = new();
    private readonly KfmFile? _kfm;
    private readonly KfmBlend _defaultSync, _defaultNonSync;
    private readonly Func<int, int, KfmBlend>? _resolver;
    private readonly List<Layer> _layers = new();
    private readonly List<KfmEvent> _events = new();
    private readonly List<KfmLayer> _out = new();
    private readonly List<KfmStep> _steps = new();
    private KfmBlend _blend;
    private float _blendT;             // temps écoulé dans le fondu courant
    private int _queued = int.MinValue;

    /// <summary>Crée la machine depuis un KFM et un chargeur de .kf
    /// (chemin relatif du KFM, ex. <c>.\animation\B.kf</c> → fichier).</summary>
    public KfmStateMachine(KfmFile kfm, Func<string, KfFile?> loadKf)
    {
        _kfm = kfm;
        _defaultSync = new KfmBlend((KfmTransitionType)kfm.DefaultSyncType,
            Math.Max(kfm.DefaultSyncDuration, 0f));
        _defaultNonSync = new KfmBlend((KfmTransitionType)kfm.DefaultNonSyncType,
            Math.Max(kfm.DefaultNonSyncDuration, 0f));
        var files = new Dictionary<string, KfFile?>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in kfm.Anims)
        {
            if (!files.TryGetValue(a.KfPath, out var kf))
                files[a.KfPath] = kf = loadKf(a.KfPath);
            var info = kf?.Info(a.Index);
            if (info is not { } i) continue;      // séquence absente du .kf
            _clips.TryAdd(a.Event, new KfmClip(a.Event, i.Name, i.Start,
                i.Stop, i.Cycle, i.Frequency));
        }
    }

    /// <summary>Machine sur des clips fournis (tests, KFM synthétiques).
    /// <paramref name="resolver"/> (de, vers) → fondu ; null = défauts.</summary>
    public KfmStateMachine(IEnumerable<KfmClip> clips, KfmBlend defaultNonSync,
        Func<int, int, KfmBlend>? resolver = null)
    {
        foreach (var c in clips) _clips.TryAdd(c.Event, c);
        _defaultNonSync = _defaultSync = defaultNonSync;
        _resolver = resolver;
    }

    /// <summary>Événement joué en fin de clip Clamp quand rien n'est en
    /// file (ex. 1 = stand01) ; <c>null</c> = on garde la dernière pose.</summary>
    public int? ReturnEvent { get; set; }

    /// <summary>Facteur de vitesse global (ex. cadence de marche).</summary>
    public float Speed { get; set; } = 1f;

    public bool Has(int ev) => _clips.ContainsKey(ev);
    public KfmClip? Clip(int ev) => _clips.GetValueOrDefault(ev);
    public IEnumerable<KfmClip> Clips => _clips.Values;

    /// <summary>Id d'événement du clip courant (le plus récent), ou
    /// <c>int.MinValue</c> avant le premier <see cref="Request"/>.</summary>
    public int Current => _layers.Count == 0 ? int.MinValue : _layers[^1].Clip.Event;

    /// <summary>Dernier événement demandé avec succès.</summary>
    public int Requested { get; private set; } = int.MinValue;

    /// <summary>Un fondu est en cours.</summary>
    public bool Blending => _layers.Count > 1;

    /// <summary>Progression du fondu courant, 0..1 (1 = terminé).</summary>
    public float BlendProgress => !Blending ? 1f
        : _blend.Duration <= 1e-6f ? 1f : Math.Clamp(_blendT / _blend.Duration, 0f, 1f);

    /// <summary>Temps de lecture local du clip courant (s).</summary>
    public float CurrentElapsed => _layers.Count == 0 ? 0f : _layers[^1].Elapsed;

    /// <summary>Le clip courant est un Clamp terminé.</summary>
    public bool CurrentFinished => _layers.Count > 0 && _layers[^1].EndedFired;

    // ----------------------------- transitions -------------------------

    /// <summary>Transition de <paramref name="from"/> vers <paramref name="to"/> :
    /// entrée du KFM si elle porte une durée, sinon défaut non-sync
    /// (type 5 / absente) ou sync (type 4).</summary>
    public KfmBlend Resolve(int from, int to)
    {
        if (_resolver != null) return _resolver(from, to);
        var t = _kfm?.ByEvent(from)?.To(to);
        if (t == null) return _defaultNonSync;
        return t.Kind switch
        {
            KfmTransitionType.DefaultSync => _defaultSync,
            KfmTransitionType.DefaultNonSync => _defaultNonSync,
            _ => new KfmBlend(t.Kind, Math.Max(t.Duration, 0f)),
        };
    }

    // ------------------------------ demandes ---------------------------

    /// <summary>Demande l'animation <paramref name="ev"/>. Retourne faux
    /// (+ événement <see cref="KfmEventKind.Rejected"/>) si l'id est absent
    /// du KFM. Même clip déjà courant : ignoré sauf <paramref name="restart"/>.
    /// <paramref name="startFraction"/> (0..1) démarre en cours de clip
    /// (synchro de phase entre cycles de marche).</summary>
    public bool Request(int ev, bool restart = false, float startFraction = 0f)
    {
        if (!_clips.TryGetValue(ev, out var clip))
        {
            _events.Add(new KfmEvent(KfmEventKind.Rejected, ev));
            return false;
        }
        _queued = int.MinValue;
        if (!restart && _layers.Count > 0 && Current == ev && !CurrentFinished)
        {
            Requested = ev;
            return true;
        }
        var nl = new Layer
        {
            Clip = clip,
            Elapsed = Math.Clamp(startFraction, 0f, 1f) * clip.Duration
                      / Math.Max(clip.Frequency, 1e-6f),
        };
        if (_layers.Count == 0)
        {
            _layers.Add(nl);
            nl.Weight = nl.Share = 1f;
        }
        else
        {
            var from = Current;
            _blend = Resolve(from, ev);
            _blendT = 0f;
            if (_blend.Duration <= 1e-6f)
            {
                _layers.Clear();
                _layers.Add(nl);
            }
            else
            {
                foreach (var l in _layers)
                {
                    l.Share = l.Weight;
                    l.Frozen = _blend.Type == KfmTransitionType.Morph;
                }
                nl.Weight = 0f; nl.Share = 0f;
                _layers.Add(nl);
                while (_layers.Count > MaxLayers) _layers.RemoveAt(0);
            }
        }
        Requested = ev;
        _events.Add(new KfmEvent(KfmEventKind.Started, ev));
        return true;
    }

    /// <summary>Joue <paramref name="ev"/> à la fin du clip courant s'il
    /// est Clamp (sinon immédiatement). Remplace une file précédente.</summary>
    public bool Queue(int ev)
    {
        if (!_clips.ContainsKey(ev))
        {
            _events.Add(new KfmEvent(KfmEventKind.Rejected, ev));
            return false;
        }
        if (_layers.Count == 0 || _layers[^1].Clip.Loops || CurrentFinished)
            return Request(ev);
        _queued = ev;
        return true;
    }

    // ------------------------------ avancement -------------------------

    /// <summary>Événements accumulés (demandes + <see cref="Update"/>)
    /// depuis le dernier <see cref="DrainEvents"/>.</summary>
    public IReadOnlyList<KfmEvent> Events => _events;

    /// <summary>Copie les événements accumulés dans
    /// <paramref name="into"/> puis les vide.</summary>
    public int DrainEvents(List<KfmEvent> into)
    {
        var n = _events.Count;
        into.AddRange(_events);
        _events.Clear();
        return n;
    }

    /// <summary>Fenêtres de lecture du dernier <see cref="Update"/>.</summary>
    public IReadOnlyList<KfmStep> Steps => _steps;

    /// <summary>Avance de <paramref name="dt"/> secondes.</summary>
    public void Update(float dt)
    {
        _steps.Clear();
        if (_layers.Count == 0 || dt <= 0f) { RebuildLayers(); return; }
        dt *= Speed;

        // fondu
        if (_layers.Count > 1)
        {
            _blendT += dt;
            var s = BlendProgress;
            var top = _layers[^1];
            top.Weight = s;
            for (var i = 0; i < _layers.Count - 1; i++)
                _layers[i].Weight = _layers[i].Share * (1f - s);
            if (s >= 1f)
            {
                _layers.RemoveRange(0, _layers.Count - 1);
                top.Weight = top.Share = 1f;
                _events.Add(new KfmEvent(KfmEventKind.BlendDone, top.Clip.Event));
            }
        }

        // lecture
        foreach (var l in _layers)
        {
            if (l.Frozen) continue;
            var before = l.Elapsed;
            l.Elapsed += dt;
            var clip = l.Clip;
            var d = clip.Duration / Math.Max(clip.Frequency, 1e-6f);
            var isTop = l == _layers[^1];
            if (clip.Loops && d > 1e-6f)
            {
                var wraps = (int)MathF.Floor(l.Elapsed / d) - (int)MathF.Floor(before / d);
                if (isTop)
                {
                    _steps.Add(new KfmStep(clip.Event, before * clip.Frequency,
                        l.Elapsed * clip.Frequency, wraps > 0));
                    if (wraps > 0) _events.Add(new KfmEvent(KfmEventKind.Looped, clip.Event));
                }
            }
            else
            {
                if (l.Elapsed > d) l.Elapsed = d;
                if (isTop)
                {
                    _steps.Add(new KfmStep(clip.Event, before * clip.Frequency,
                        l.Elapsed * clip.Frequency, false));
                    if (!l.EndedFired && l.Elapsed >= d - 1e-6f)
                    {
                        l.EndedFired = true;
                        _events.Add(new KfmEvent(KfmEventKind.Ended, clip.Event));
                    }
                }
            }
        }

        // enchaînement après la fin d'un clip Clamp
        var cur = _layers[^1];
        if (cur.EndedFired)
        {
            if (_queued != int.MinValue)
            {
                var q = _queued;
                Request(q);
            }
            else if (ReturnEvent is { } r && r != cur.Clip.Event && _clips.ContainsKey(r))
                Request(r);
        }
        RebuildLayers();
    }

    private void RebuildLayers()
    {
        _out.Clear();
        var cum = 0f;
        foreach (var l in _layers)
        {
            var w = Math.Max(l.Weight, 0f);
            cum += w;
            var apply = cum <= 1e-6f ? 1f : _out.Count == 0 ? 1f : w / cum;
            _out.Add(new KfmLayer(l.Clip.Event, l.Clip.Name,
                l.Clip.SequenceTime(l.Elapsed), w, apply));
        }
    }

    /// <summary>Couches à appliquer, ancienne → récente. Les poids
    /// effectifs somment à 1 ; <see cref="KfmLayer.ApplyWeight"/> sert à
    /// des <c>KfPose.Apply</c> successifs (la 1re couche remplace).</summary>
    public IReadOnlyList<KfmLayer> Layers
    {
        get
        {
            RebuildLayers();
            return _out;
        }
    }
}
