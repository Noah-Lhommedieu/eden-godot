using Eden.Core.Networking.Session;

namespace Eden.Core.Networking.Handlers;

public static class PacketDispatcherExtensions
{
    /// <summary>
    /// Comme <see cref="PacketDispatcher.Register{TCommand}(Func{TCommand}, Func{NetSession, TCommand, CancellationToken, Task})"/>,
    /// mais n'appelle le handler que si la session est liée à un personnage
    /// (ticket de migration consommé) et injecte son charId.
    /// </summary>
    public static void RegisterChar<TCommand>(this PacketDispatcher dispatcher,
        Func<TCommand> factory,
        Func<NetSession, TCommand, int, CancellationToken, Task> handle)
        where TCommand : NetCommand
        => dispatcher.Register(factory, (session, msg, token) =>
            session.CharacterId is { } charId
                ? handle(session, msg, charId, token)
                : Task.CompletedTask);
}
