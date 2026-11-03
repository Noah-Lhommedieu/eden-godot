namespace Eden.Core.Networking.Transport;

/// <summary>
/// Pont brut fourni par le possesseur du transport pendant
/// <see cref="ITransport.StartAsync"/> : le handshake émet/reçoit des
/// octets pré-framing sans connaître le socket.
/// Pour <see cref="Session.NetSession"/>, <c>SendRawAsync</c> passe par
/// la file d'envoi bornée (ordre préservé avec les trames suivantes) ;
/// pour les liens hors NetSession, <see cref="DirectTransportContext"/>
/// écrit directement sur le transport.
/// </summary>
public interface ITransportContext
{
    /// <summary>Envoie des octets bruts (blob RSA, réponse chiffrée) —
    /// jamais transformés.</summary>
    Task SendRawAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>Lit des octets bruts (≤ <c>buffer.Length</c>) —
    /// retourne le nombre d'octets lus, 0/EOF = pair parti.</summary>
    Task<int> ReadRawAsync(Memory<byte> buffer, CancellationToken ct);
}

/// <summary>
/// <see cref="ITransportContext"/> direct pour les liens qui n'ont pas
/// de file d'envoi (TicketSession, MissionSession, ZoneWorldLink,
/// ZoneMissionLink, TicketServerClient) : SendRaw/ReadRaw tapent le
/// transport lui-même.
/// </summary>
public sealed class DirectTransportContext(ITransport transport)
    : ITransportContext
{
    /// <inheritdoc/>
    public Task SendRawAsync(ReadOnlyMemory<byte> data,
        CancellationToken ct) =>
        transport.WriteAsync(data, ct).AsTask();

    /// <inheritdoc/>
    public async Task<int> ReadRawAsync(Memory<byte> buffer,
        CancellationToken ct) =>
        await transport.ReadAsync(buffer, ct).ConfigureAwait(false);
}
