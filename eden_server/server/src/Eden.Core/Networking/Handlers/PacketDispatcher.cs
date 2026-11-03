using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Core.Networking.Handlers;

/// <summary>
/// Répartiteur de paquets réseau typés. Fait correspondre les opcodes et flux entrants aux gestionnaires associés.
/// </summary>
public sealed class PacketDispatcher
{
    private readonly Dictionary<int, Func<InBinStream, NetCommand>> _factories = new();
    private readonly Dictionary<int, Func<NetSession, NetCommand, CancellationToken, Task>> _handlers = new();

    /// <summary>
    /// Enregistre un gestionnaire de paquet pour un type de commande spécifique.
    /// </summary>
    public void Register<TCommand>(Func<TCommand> factory, IPacketHandler<TCommand> handler)
        where TCommand : NetCommand
    {
        var dummy = factory();
        var opcode = dummy.Opcode;

        _factories[opcode] = stream =>
        {
            var cmd = factory();
            cmd.Deserialize(stream);
            return cmd;
        };

        _handlers[opcode] = (session, command, token) => handler.HandleAsync(session, (TCommand)command, token);
    }

    /// <summary>
    /// Enregistre une fonction déléguée pour traiter un paquet d'un type spécifique.
    /// </summary>
    public void Register<TCommand>(Func<TCommand> factory, Func<NetSession, TCommand, CancellationToken, Task> handleFunc)
        where TCommand : NetCommand
    {
        var dummy = factory();
        var opcode = dummy.Opcode;

        _factories[opcode] = stream =>
        {
            var cmd = factory();
            cmd.Deserialize(stream);
            return cmd;
        };

        _handlers[opcode] = (session, command, token) => handleFunc(session, (TCommand)command, token);
    }

    /// <summary>
    /// Traite un flux binaire entrant en désérialisant la commande et en invoquant son gestionnaire.
    /// </summary>
    public async Task<bool> DispatchAsync(int opcode, InBinStream stream, NetSession session, CancellationToken cancellationToken = default)
    {
        if (!_factories.TryGetValue(opcode, out var factory) ||
            !_handlers.TryGetValue(opcode, out var handler))
        {
            return false;
        }

        var command = factory(stream);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await handler(session, command, cancellationToken);
        EdenMetrics.PacketHandleMs.Record(
            System.Diagnostics.Stopwatch.GetElapsedTime(started)
                .TotalMilliseconds,
            new KeyValuePair<string, object?>("ncid", opcode),
            new KeyValuePair<string, object?>("path", "dispatcher"));
        return true;
    }
}
