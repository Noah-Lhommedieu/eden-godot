using System.Net.Sockets;

namespace Eden.Core.Networking.Transport;

/// <summary>
/// Réglages socket partagés des transports TCP — code déplacé tel quel
/// depuis l'ancien ctor <c>NetSession(TcpClient)</c> :
/// NoDelay (latence interactive, un serveur de jeu émet beaucoup de
/// petites trames que Nagle retiendrait ~40-200 ms) et keep-alive TCP
/// borné (une connexion fantôme — client tué sans FIN, NAT expiré —
/// serait sinon invisible jusqu'à l'idle-timeout applicatif).
/// <c>EDEN_TCP_KEEPALIVE_*</c>. Appelé par <see cref="TcpTransport"/>
/// et <see cref="LegacyLapisTransport"/> à la construction.
/// </summary>
internal static class TcpSocketTuning
{
    /// <summary>Applique NoDelay + keep-alive borné ; les options non
    /// supportées par la plateforme sont ignorées (la session reste
    /// viable, l'idle-timeout applicatif garde le rôle de détecteur
    /// de fantômes).</summary>
    public static void Apply(TcpClient tcpClient)
    {
        tcpClient.NoDelay = true;
        try
        {
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.KeepAlive, true);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp,
                SocketOptionName.TcpKeepAliveTime,
                EdenConfig.TcpKeepAliveTimeSec);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp,
                SocketOptionName.TcpKeepAliveInterval,
                EdenConfig.TcpKeepAliveIntervalSec);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp,
                SocketOptionName.TcpKeepAliveRetryCount,
                EdenConfig.TcpKeepAliveRetryCount);
        }
        catch (SocketException)
        {
            // Options non supportées par la plateforme — non bloquant.
        }
    }
}

/// <summary>
/// Transport TCP en clair : wrappe <see cref="TcpClient"/>/
/// <see cref="NetworkStream"/>, transforms no-op, pas de handshake.
/// Le socket est supposé déjà réglé par l'appelant (NoDelay/keep-alive
/// via <see cref="TcpSocketTuning.Apply"/> — fait par le ctor
/// <c>NetSession(TcpClient)</c> qui wrappe ce transport).
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;

    /// <summary>Wrappe un client TCP déjà connecté (et déjà réglé).</summary>
    public TcpTransport(TcpClient tcpClient)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
    }

    /// <inheritdoc/>
    public string RemoteEndPoint =>
        _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

    /// <inheritdoc/>
    public bool IsEncrypted => false;

    /// <inheritdoc/>
    public bool HasSendTransform => false;

    /// <inheritdoc/>
    public Task StartAsync(ITransportContext ctx, CancellationToken ct) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    public ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct) =>
        _stream.ReadAsync(b, ct);

    /// <inheritdoc/>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> b,
        CancellationToken ct) =>
        _stream.WriteAsync(b, ct);

    /// <inheritdoc/>
    public void TransformSend(Span<byte> p) { }

    /// <inheritdoc/>
    public void TransformRecv(Span<byte> p) { }

    /// <inheritdoc/>
    public void Abort()
    {
        try
        {
            _tcpClient.Client.Shutdown(SocketShutdown.Both);
        }
        catch (Exception)
        {
            // Socket déjà clos/défaillant — le reader conclura quand
            // même la session via sa propre erreur ou l'idle timeout.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _tcpClient.Dispose();
    }
}
