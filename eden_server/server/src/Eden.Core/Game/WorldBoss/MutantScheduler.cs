namespace Eden.Core.Game.WorldBoss;

/// <summary>Planificateur d'apparition des 6 monstres mutants
/// (60761-60766) — logique pure, sans I/O ni horloge implicite, testée
/// unitairement. Natif : « aucune ligne reborn → spawn dynamique
/// système (région aléatoire, annonce canal monde) » (guide FR
/// monstres-mutants) ; les cadences exactes ne sont pas dans les
/// exports (HYPOTHESIS : intervalle et durée de vie configurables,
/// défauts 30-90 min / 60 min).
///
/// Un mutant est soit en attente (<c>NextSpawnAt</c>), soit actif dans
/// une région. <see cref="Tick"/> renvoie les actions à exécuter ;
/// <see cref="NotifyKilled"/> / <see cref="NotifyGone"/> replanifient.
/// Canal : un process Zone = un canal ; le tirage de « canal aléatoire »
/// est donc porté par chaque process indépendamment.</summary>
public sealed class MutantScheduler
{
    /// <summary>Paramètres de cadence.</summary>
    public sealed record Options(TimeSpan MinInterval, TimeSpan MaxInterval,
        TimeSpan Lifetime, int MaxConcurrent)
    {
        /// <summary>Défauts : 30-90 min d'attente, 60 min de présence,
        /// 3 mutants simultanés au plus.</summary>
        public static Options Default { get; } = new(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(90),
            TimeSpan.FromMinutes(60), 3);
    }

    /// <summary>Type d'action demandée à la zone.</summary>
    public enum ActionKind { Spawn, Expire }

    /// <summary>Action : faire naître / retirer un mutant.</summary>
    public sealed record Action(ActionKind Kind, int MutantMobId,
        int NodeId, float X, float Y);

    /// <summary>Mutant présent dans le monde.</summary>
    public sealed record ActiveMutant(int MutantMobId, int NodeId,
        float X, float Y, DateTime ExpiresAt);

    private readonly Options _opt;
    private readonly Random _rng;
    private readonly DateTime[] _next;
    private readonly ActiveMutant?[] _active;

    /// <summary>Planificateur démarré à <paramref name="start"/> : les
    /// 6 mutants sont décalés (première apparition dans [0, Max]).</summary>
    public MutantScheduler(Options opt, Random rng, DateTime start)
    {
        _opt = opt;
        _rng = rng;
        var n = WorldBossCatalog.MutantIds.Count;
        _next = new DateTime[n];
        _active = new ActiveMutant?[n];
        for (var i = 0; i < n; i++)
            _next[i] = start + Scale(opt.MaxInterval, rng.NextDouble());
    }

    private static TimeSpan Scale(TimeSpan t, double f) =>
        TimeSpan.FromTicks((long)(t.Ticks * f));

    /// <summary>Mutants actuellement présents.</summary>
    public IReadOnlyList<ActiveMutant> Active =>
        _active.Where(a => a != null).Select(a => a!).ToList();

    /// <summary>Date de la prochaine apparition d'un mutant (index 0-5)
    /// — DateTime.MaxValue tant qu'il est actif.</summary>
    public DateTime NextSpawnAt(int mutantIndex) =>
        _active[mutantIndex] != null ? DateTime.MaxValue : _next[mutantIndex];

    /// <summary>Avance l'horloge : expire les mutants trop vieux puis
    /// fait naître ceux dont l'attente est écoulée (dans la limite de
    /// <see cref="Options.MaxConcurrent"/>). La région est tirée au
    /// hasard parmi les 6 ; deux mutants peuvent partager une région
    /// (« il peut y avoir plusieurs boss dans une même région, ou
    /// aucun »).</summary>
    public IReadOnlyList<Action> Tick(DateTime now)
    {
        var actions = new List<Action>();
        for (var i = 0; i < _active.Length; i++)
        {
            if (_active[i] is { } a && now >= a.ExpiresAt)
            {
                actions.Add(new Action(ActionKind.Expire, a.MutantMobId,
                    a.NodeId, a.X, a.Y));
                _active[i] = null;
                Reschedule(i, now);
            }
        }
        var live = _active.Count(a => a != null);
        // Ordre d'examen décalé pour que ce ne soit pas toujours Posa
        // qui passe en premier quand plusieurs sont dus.
        var offset = _rng.Next(_active.Length);
        for (var k = 0; k < _active.Length && live < _opt.MaxConcurrent; k++)
        {
            var i = (k + offset) % _active.Length;
            if (_active[i] != null || now < _next[i]) continue;
            var act = SpawnAction(i, _rng.Next(
                WorldBossCatalog.MutantRegionNodes.Count));
            _active[i] = new ActiveMutant(act.MutantMobId, act.NodeId,
                act.X, act.Y, now + _opt.Lifetime);
            actions.Add(act);
            live++;
        }
        return actions;
    }

    /// <summary>Force l'apparition d'un mutant (commande GM) dans une
    /// région donnée — ignore l'attente et le plafond. Null si déjà
    /// actif ou id inconnu.</summary>
    public Action? ForceSpawn(int mutantMobId, int regionIndex, DateTime now)
    {
        var i = WorldBossCatalog.MutantIdIndex(mutantMobId);
        if (i < 0 || _active[i] != null) return null;
        regionIndex = Math.Clamp(regionIndex, 0,
            WorldBossCatalog.MutantRegionNodes.Count - 1);
        var act = SpawnAction(i, regionIndex);
        _active[i] = new ActiveMutant(act.MutantMobId, act.NodeId,
            act.X, act.Y, now + _opt.Lifetime);
        return act;
    }

    private Action SpawnAction(int mutantIndex, int regionIndex)
    {
        var (x, y) = WorldBossCatalog.MutantSpot(mutantIndex, regionIndex);
        return new Action(ActionKind.Spawn,
            WorldBossCatalog.MutantIds[mutantIndex],
            WorldBossCatalog.MutantRegionNodes[regionIndex], x, y);
    }

    /// <summary>Le mutant a été tué : attente aléatoire avant le suivant.</summary>
    public void NotifyKilled(int mutantMobId, DateTime now)
    {
        var i = WorldBossCatalog.MutantIdIndex(mutantMobId);
        if (i < 0 || _active[i] == null) return;
        _active[i] = null;
        Reschedule(i, now);
    }

    /// <summary>Le mutant a disparu sans être tué (node vidé, retrait
    /// GM) : même replanification qu'une mort.</summary>
    public void NotifyGone(int mutantMobId, DateTime now) =>
        NotifyKilled(mutantMobId, now);

    private void Reschedule(int i, DateTime now) =>
        _next[i] = now + _opt.MinInterval + Scale(
            _opt.MaxInterval - _opt.MinInterval, _rng.NextDouble());
}
