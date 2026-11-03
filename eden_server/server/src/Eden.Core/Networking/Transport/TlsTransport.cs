using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Eden.Core.Networking.Transport;

/// <summary>
/// Transport TLS 1.3 (phase 4 incrément c — mode
/// <c>EDEN_TRANSPORT=modern</c>, défaut depuis que le client Godot est
/// le seul client) : wrappe <see cref="TcpClient"/> →
/// <see cref="SslStream"/>. Le handshake
/// <see cref="StartAsync"/> = <see cref="SslStream.AuthenticateAsServerAsync"/>
/// / <see cref="SslStream.AuthenticateAsClientAsync"/> borné par
/// <c>EDEN_HANDSHAKE_TIMEOUT_SEC</c> (même anti-flood que le handshake
/// RSA/RC4 de <see cref="LegacyLapisTransport"/>) et restreint à
/// <see cref="SslProtocols.Tls13"/>.
///
/// Contrairement à <see cref="LegacyLapisTransport"/>, l'authentification
/// ne transite PAS par <see cref="ITransportContext"/> : le
/// <see cref="SslStream"/> lit/écrit le <see cref="NetworkStream"/>
/// sous-jacent directement (<paramref name="ctx"/> de
/// <see cref="StartAsync"/> est donc inutilisé ici). Le chiffrement se
/// fait sous la couche paquet — <see cref="TransformSend"/>/
/// <see cref="TransformRecv"/> restent no-op et
/// <see cref="HasSendTransform"/> vaut toujours false : le drain de
/// <see cref="Session.NetSession"/> n'a plus de keystream à entretenir.
/// </summary>
public sealed class TlsTransport : ITransport
{
    private readonly TcpClient _tcpClient;
    private readonly SslStream _ssl;
    private readonly bool _isServer;
    private readonly SslServerAuthenticationOptions? _serverOptions;
    private readonly SslClientAuthenticationOptions? _clientOptions;
    private readonly TimeSpan _handshakeTimeout;
    private bool _authenticated;

    /// <summary>Délai max du handshake TLS (anti-flood : un pair qui
    /// ouvre puis ne répond jamais au ClientHello ne doit pas retenir
    /// la session). <c>EDEN_HANDSHAKE_TIMEOUT_SEC</c>, défaut 10 s —
    /// même borne que le handshake RSA/RC4 historique.</summary>
    private static TimeSpan DefaultHandshakeTimeout =>
        TimeSpan.FromSeconds(EdenConfig.HandshakeTimeoutSec is { } h
            && h > 0 ? h : 10);

    /// <summary>
    /// <paramref name="isServer"/> : le transport authentifie côté
    /// serveur (<paramref name="serverOptions"/> requis, cert via
    /// <see cref="TlsCertificateProvider"/>) ; sinon côté client
    /// (<paramref name="clientOptions"/> requis — validation par
    /// <paramref name="clientOptions"/>.<c>RemoteCertificateValidationCallback</c>).
    /// <paramref name="handshakeTimeout"/> : surcharge du délai
    /// (tests) ; défaut <c>EDEN_HANDSHAKE_TIMEOUT_SEC</c>.
    /// </summary>
    public TlsTransport(TcpClient tcpClient, bool isServer,
        SslServerAuthenticationOptions? serverOptions = null,
        SslClientAuthenticationOptions? clientOptions = null,
        TimeSpan? handshakeTimeout = null)
    {
        if (isServer && serverOptions == null)
            throw new ArgumentNullException(nameof(serverOptions));
        if (!isServer && clientOptions == null)
            throw new ArgumentNullException(nameof(clientOptions));
        _tcpClient = tcpClient;
        _isServer = isServer;
        _serverOptions = serverOptions;
        _clientOptions = clientOptions;
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        // Même réglage que les autres transports (NoDelay + keep-alive
        // borné) — le SslStream ne change rien aux exigences socket.
        TcpSocketTuning.Apply(tcpClient);
        // leaveInnerStreamOpen:false — disposer le SslStream ferme le
        // NetworkStream, le TcpClient reste disposé par DisposeAsync.
        _ssl = new SslStream(tcpClient.GetStream(),
            leaveInnerStreamOpen: false);
    }

    /// <inheritdoc/>
    public string RemoteEndPoint =>
        _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

    /// <inheritdoc/>
    public bool IsEncrypted => _authenticated;

    /// <inheritdoc/>
    public bool HasSendTransform => false;

    /// <inheritdoc/>
    public void TransformSend(Span<byte> p) { }

    /// <inheritdoc/>
    public void TransformRecv(Span<byte> p) { }

    /// <summary>Authentification TLS bornée — une expiration propre au
    /// timeout est traduite en <see cref="TimeoutException"/> (même
    /// convention que <see cref="LegacyLapisTransport"/> :
    /// <see cref="Session.NetSession.StartTransportAsync"/> la rend en
    /// <c>CloseReason</c> « handshake timeout »).</summary>
    public async Task StartAsync(ITransportContext ctx,
        CancellationToken ct)
    {
        using var hsCts = CancellationTokenSource
            .CreateLinkedTokenSource(ct);
        hsCts.CancelAfter(_handshakeTimeout);
        try
        {
            if (_isServer)
                await _ssl.AuthenticateAsServerAsync(_serverOptions!,
                    hsCts.Token).ConfigureAwait(false);
            else
                await _ssl.AuthenticateAsClientAsync(_clientOptions!,
                    hsCts.Token).ConfigureAwait(false);
            _authenticated = true;
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"handshake timeout ({_handshakeTimeout.TotalSeconds}s)");
        }
    }

    /// <inheritdoc/>
    public ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct) =>
        _ssl.ReadAsync(b, ct);

    /// <inheritdoc/>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> b,
        CancellationToken ct) =>
        _ssl.WriteAsync(b, ct);

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
        await _ssl.DisposeAsync();
        _tcpClient.Dispose();
    }

    /// <summary>Options d'authentification serveur standard : le cert
    /// partagé de <see cref="TlsCertificateProvider"/>, TLS 1.3
    /// uniquement (TLS 1.2 refusé — vérifié par test).</summary>
    public static SslServerAuthenticationOptions ServerOptions() =>
        new()
        {
            ServerCertificate = TlsCertificateProvider.GetCertificate(),
            EnabledSslProtocols = SslProtocols.Tls13,
            ClientCertificateRequired = false,
        };
}
