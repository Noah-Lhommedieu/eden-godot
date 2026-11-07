using Eden.Core.Logging;

namespace Eden.Core.Game.Events;

/// <summary>
/// Bus d'événements de domaine, synchrone et en mémoire. Un abonné qui lève une exception est journalisé et
/// n'empêche NI les autres abonnés NI le système qui publie : l'ajout d'une règle ne peut jamais casser le combat.
/// </summary>
public sealed class DomainEventBus
{
    /// <summary>Instance partagée du serveur ; les tests créent leur propre bus.</summary>
    public static DomainEventBus Shared { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<Type, List<Delegate>> _typed = new();
    private readonly List<Action<IDomainEvent>> _all = new();

    /// <summary>S'abonne à un type d'événement ; <c>Dispose</c> se désabonne.</summary>
    public IDisposable Subscribe<T>(Action<T> handler) where T : IDomainEvent
    {
        lock (_lock)
        {
            if (!_typed.TryGetValue(typeof(T), out var l)) _typed[typeof(T)] = l = new List<Delegate>();
            l.Add(handler);
        }
        return new Unsub(() => { lock (_lock) _typed[typeof(T)].Remove(handler); });
    }

    /// <summary>S'abonne à TOUS les événements (moteur de règles, audit).</summary>
    public IDisposable SubscribeAll(Action<IDomainEvent> handler)
    {
        lock (_lock) _all.Add(handler);
        return new Unsub(() => { lock (_lock) _all.Remove(handler); });
    }

    public void Publish<T>(T evt) where T : IDomainEvent
    {
        Delegate[] typed; Action<IDomainEvent>[] all;
        lock (_lock)
        {
            typed = _typed.TryGetValue(typeof(T), out var l) ? l.ToArray() : Array.Empty<Delegate>();
            all = _all.ToArray();
        }
        foreach (var h in typed) Run(() => ((Action<T>)h)(evt), evt.Name);
        foreach (var h in all) Run(() => h(evt), evt.Name);
    }

    private static void Run(Action a, string evt)
    {
        try { a(); }
        catch (Exception e) { EdenLog.Write($"[Events] abonné en erreur sur {evt} : {e.GetType().Name} {e.Message}"); }
    }

    private sealed class Unsub : IDisposable
    {
        private Action? _a;
        public Unsub(Action a) => _a = a;
        public void Dispose() { _a?.Invoke(); _a = null; }
    }
}
