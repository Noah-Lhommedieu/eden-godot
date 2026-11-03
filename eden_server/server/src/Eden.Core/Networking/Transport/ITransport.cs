namespace Eden.Core.Networking.Transport;

/// <summary>
/// Abstraction de transport : un stream d'octets + transforms de
/// payload + handshake de transport. <see cref="Session.NetSession"/>
/// garde la file bornée / drain unique / métriques / trace /
/// rate-limit ; le framing [u16 len] reste côté session — le
/// transport ne voit que des octets bruts.
///
/// Implémentations : <see cref="TcpTransport"/> (clair, transforms
/// no-op), <see cref="LegacyLapisTransport"/> (handshake RSA blob +
/// RC4 lapis, <c>encryptPayloads:false</c> pour le lien ZM qui échange
/// la clé puis parle clair). Incrément (a) du plan
/// docs/temp-plans/2026-10-26-phase4-transport-protocole.md : aucun
/// changement wire — le RC4 reste appliqué par le drain unique, dans
/// l'ordre réel du fil.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Endpoint distant (« host:port ») pour les logs.</summary>
    string RemoteEndPoint { get; }

    /// <summary>Vrai quand le transport chiffre les payloads
    /// (handshake crypto négocié). Informatif — le drain se fie à
    /// <see cref="HasSendTransform"/>.</summary>
    bool IsEncrypted { get; }

    /// <summary>
    /// Handshake de transport (auth TLS ou RSA/RC4 lapis) — utilise
    /// <see cref="ITransportContext.SendRawAsync"/> /
    /// <see cref="ITransportContext.ReadRawAsync"/> pour les octets
    /// bruts pré-framing. No-op pour un transport sans handshake.
    /// </summary>
    Task StartAsync(ITransportContext ctx, CancellationToken ct);

    /// <summary>Lit des octets bruts du fil (pré-framing) — le u16 len
    /// reste en clair, le déchiffrement passe par
    /// <see cref="TransformRecv"/> sur le payload extrait.</summary>
    ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct);

    /// <summary>Écrit des octets bruts sur le fil — appelé
    /// UNIQUEMENT par le drain d'envoi (writer unique).</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct);

    /// <summary>Vrai quand <see cref="TransformSend"/> altère les
    /// octets — évite l'appel quand le transport est en clair.</summary>
    bool HasSendTransform { get; }

    /// <summary>Transforme en place un payload sortant (RC4 keystream
    /// continu côté émission ; no-op en clair). Appelé par le drain
    /// seul, donc jamais concurrentiellement.</summary>
    void TransformSend(Span<byte> p);

    /// <summary>Transforme en place un payload entrant (RC4 keystream
    /// continu côté réception ; no-op en clair). Appelé par la boucle
    /// de lecture seule.</summary>
    void TransformRecv(Span<byte> p);

    /// <summary>Coupe la connexion sans attente (Shutdown(Both)) —
    /// chemin d'abandon des sessions lentes/mortes.</summary>
    void Abort();
}
