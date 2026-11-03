using Eden.Core.Networking.Session;

namespace Eden.Core.Networking.Handlers;

/// <summary>
/// Interface pour les gestionnaires de paquets réseau typés.
/// </summary>
/// <typeparam name="TMessage">Le type de message réseau manipulé.</typeparam>
public interface IPacketHandler<in TMessage> where TMessage : NetCommand
{
    Task HandleAsync(NetSession session, TMessage message, CancellationToken cancellationToken = default);
}
