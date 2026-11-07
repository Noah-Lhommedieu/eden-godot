using Eden.Core;
using System.Buffers;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using System.Threading.Channels;
using Eden.Core.Logging;
using Eden.Core.Networking.Transport;

namespace Eden.Core.Networking.Session;

/// <summary>
/// Session réseau asynchrone reproduisant la couche connexion lapis
/// (CMSTCPChannel / CMSTCPConnection dans les binaires serveur) :
/// file d'envoi bornée + drain unique, framing
/// [varint len][payload], rate-limit, trace — le transport
/// octet-par-octet (handshake, transforms RC4) est délégué à
/// <see cref="ITransport"/>.
///
/// Framing (Phase 4 incrément b : préfixe LEB128 1-5 octets en clair,
/// remplace le u16 historique ; payload inchangé) :
/// <code>
///   [varint len en clair][RC4(u16 NCID + champs)]
///   len = taille du payload chiffré, RC4 stream continu par direction
///   (handshake RSA blob → clé RC4 5 octets : LegacyLapisTransport).
/// </code>
/// </summary>
public sealed partial class NetSession : IAsyncDisposable
{
    private readonly ITransport _transport;
    // File d'envoi bornée — UN seul writer par session : le drain
    // applique le RC4 puis écrit, donc le keystream suit l'ordre réel
    // du fil (l'ordre d'enqueue), même sous envois concurrents
    // (casts temporisés, broadcasts, réponses). Backpressure : file
    // pleine → attente bornée SendTimeout puis fermeture propre —
    // un client lent ne bloque plus les autres senders ni le processus.
    // Mode DropOnFullQueue (sessions zone) : pas d'attente du tout —
    // une trame qui n'entre pas ferme la session, pour qu'aucun
    // destinataire mort ne gèle la boucle acteur.
    /// <summary>Paquets entrants max par seconde et par session —
    /// <c>EDEN_MAX_PACKETS_PER_SEC</c>, 0 = désactivé, défaut 300.</summary>
    private static readonly int MaxPacketsPerSecond =
        EdenConfig.MaxPacketsPerSecond;
    private DateTime _rateWindowStart = DateTime.UtcNow;
    private int _packetsThisWindow;
    private static volatile int _globalQueued;
    private static ObservableGauge<int>? _sendQueueGauge;
    // Trames louées à l'ArrayPool (Len ≤ Buf.Length — le pool rend
    // un buffer arrondi) et retournées par le drain après écriture.
    // TransformOffset/Length = plage du payload à passer par
    // TransformSend dans le drain (-1 = octets bruts, jamais
    // transformés — blob RSA, diag).
    private readonly Channel<(byte[] Buf, int Len, int TransformOffset,
        int TransformLength)> _sendQueue = Channel.CreateBounded<
            (byte[], int, int, int)>(new BoundedChannelOptions(
                SendQueueCapacity)
            { SingleReader = true, SingleWriter = false,
              FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _drainTask;
    private readonly CancellationTokenSource _sendCts = new();
    // volatile : lu par les tâches d'envoi de fond (casts, broadcasts)
    // pendant que DisposeAsync l'écrit sur le thread de lecture.
    private volatile bool _isDisposed;

    /// <summary>Trames max en attente par session avant backpressure.
    /// <c>EDEN_SEND_QUEUE_CAPACITY</c>, défaut 256.</summary>
    internal static readonly int SendQueueCapacity =
        EdenConfig.SendQueueCapacity > 0 ? EdenConfig.SendQueueCapacity : 256;

    /// <summary>Attente max sur file pleine avant fermeture —
    /// <c>EDEN_SEND_TIMEOUT_MS</c>, défaut 5000 ms.</summary>
    public static TimeSpan SendTimeout { get; } = TimeSpan.FromMilliseconds(
        EdenConfig.SendTimeoutMs);

    /// <summary>
    /// Mode envoi non bloquant (sessions de la boucle acteur zone) :
    /// <see cref="SendFrameAsync"/> devient un <c>TryWrite</c> pur —
    /// une trame qui n'entre pas en file ferme la session au lieu
    /// d'attendre <see cref="SendTimeout"/>. Preuve du besoin : burst
    /// de 200 entrées le 2026-10-26, ~70 sessions mortes à file pleine
    /// ont figé la ZoneLoop ~6 min dans
    /// <c>AnnounceSelfToWatchersAsync</c> (~1 cmd/min de vidange).
    /// volatile : posé par le thread d'accept, lu par tout expéditeur.
    /// </summary>
    private int _dropOnFullQueue;
    public bool DropOnFullQueue
    {
        get => Volatile.Read(ref _dropOnFullQueue) != 0;
        set => Volatile.Write(ref _dropOnFullQueue, value ? 1 : 0);
    }

    // Transform d'envoi injectée par les tests (InstallTestCipher) —
    // prioritaire sur celle du transport pour vérifier l'ordre du
    // keystream de la file sans handshake.
    private Action<Span<byte>>? _sendTransformOverride;
    /// POC diag : envoie les paquets sans transform (test "client attend le 1er paquet en clair").
    /// (retiré en phase4-c2 — diag legacy, sans effet sous TLS.)
    public bool ForcePlaintext { get; set; }
    private readonly PacketReceiveBuffer _recvBuffer = new();
    private readonly PacketTrace _trace = new();

    /// <summary>Vrai quand le handshake de transport (RSA/RC4 ou TLS)
    /// est terminé — le drain transforme alors les payloads (legacy).
    /// (retiré en phase4-c2 — informatif, TLS n'a pas de transform.)</summary>
    public bool CryptoReady => _sendTransformOverride != null ||
        _transport.IsEncrypted;

    /// <summary>
    /// Délai max du handshake RSA/RC4 après accept — anti-flood : un pair
    /// qui récupère la clé RSA puis stalle tiendrait sinon un thread de
    /// lecture indéfiniment. <c>EDEN_HANDSHAKE_TIMEOUT_SEC</c>, défaut
    /// 10 s (le client répond en quelques ms en fonctionnement normal).
    /// Appliqué par <see cref="LegacyLapisTransport.StartAsync"/>.
    /// </summary>
    public static TimeSpan HandshakeTimeout { get; } = TimeSpan.FromSeconds(
        EdenConfig.HandshakeTimeoutSec is { } h && h > 0 ? h : 10);

    /// <summary>
    /// Délai max sans aucun octet reçu avant fermeture propre —
    /// nettoie les sessions fantômes (client tué sans FIN, NAT mort).
    /// <c>EDEN_IDLE_TIMEOUT_SEC</c>, défaut 300 s ; ≤ 0 désactive.
    /// </summary>
    public static TimeSpan IdleTimeout { get; } = TimeSpan.FromSeconds(
        EdenConfig.IdleTimeoutSec);

    /// <summary>Liens serveur↔serveur (zone↔monde) : le natif ne coupe
    /// jamais ces canaux sur silence — sans ça la zone tombait toutes
    /// les 5 min (défaut 300 s) puis re-poussait StarWay/FortuneBag en
    /// boucle. Vrai = pas d'idle timeout pour CETTE session.</summary>
    public bool SkipIdleTimeout { get; set; }

    /// <summary>Cause de la fermeture demandée côté serveur
    /// (timeout, trame invalide) — null si déconnexion normale.</summary>
    public string? CloseReason { get; private set; }

    /// <summary>Timestamp <see cref="System.Diagnostics.Stopwatch"/> du
    /// dernier lot de paquets décodés — utilisé pour mesurer le délai
    /// file réseau → dispatch.</summary>
    public long LastPacketsTimestamp { get; private set; }

    /// <summary>Rend la trace des derniers paquets (diag crash/désync).</summary>
    public string DumpPacketTrace() => _trace.Dump();

    /// <summary>Installe un transform d'envoi sans handshake (tests
    /// loopback — vérifie l'ordre RC4 de la file).
    /// (retiré en phase4-c2 — tests legacy.)</summary>
    internal void InstallTestCipher(Rc4 cipher) =>
        _sendTransformOverride = cipher.Process;

    public Guid SessionId { get; } = Guid.NewGuid();
    public int? AccountId { get; set; }
    public int? CharacterId { get; set; }
    public string? Username { get; set; }
    public bool IsAuthenticated { get; set; }
    /// <summary>Nom de personnage validé en attente de création (étape Name/Data).</summary>
    public string? PendingCharName { get; set; }
    /// <summary>Id du shop PNJ actuellement ouvert (0 = aucun).</summary>
    public int CurrentStoreId { get; set; }
    /// <summary>Handle entité du PNJ dont le shop est ouvert — requis
    /// par le 304 ClientEnterShop (native OpenShop émet npcUid) quand
    /// l'onglet « Items » est ré-affiché (185 flag=0).</summary>
    public int CurrentShopNpcHandle { get; set; }
    /// <summary>
    /// Décalage NCID sortant (build client ≠ server_us) : les opcodes
    /// ≥ cette valeur sont émis +1 sur le fil. Le client EasyFun/Reforged
    /// a une commande CNC supplémentaire insérée à l'index 38
    /// (preuve runtime : son GetCharacters répond sur NCID 39).
    /// (retiré en phase4-c2 — quirk mort, Godot seul client.)
    /// </summary>
    public int OutboundNcidShiftAbove { get; set; } = int.MaxValue;

    /// <summary>
    /// Flags de recherche joueur — exec+0xe2/+0xe3 natifs, posés par
    /// <c>CNE_CZ_ZoneServerSearchOpt</c> (mission
    /// <c>TC_SearchConditionOpt</c> : arg1→e3 LFG, arg2→e2 visible).
    /// Le natif exige e2 ≠ 0 pour apparaître dans SearchCondition ;
    /// défaut 1 (visible) tant que le client n'a pas opt-out.
    /// </summary>
    public bool SearchVisible { get; set; } = true;
    /// <summary>Flag « recherche de groupe » (exec+0xe3), requis par
    /// l'option bit0 de SearchCondition.</summary>
    public bool SearchLfg { get; set; }

    public string RemoteEndPoint => _transport.RemoteEndPoint;

    /// <summary>Session sur un socket TCP accepté/connecté — wrappe un
    /// <see cref="TcpTransport"/> en clair (NoDelay + keep-alive
    /// appliqués ici, comme toujours). Les chemins avec handshake
    /// RSA/RC4 passent un <see cref="LegacyLapisTransport"/> au ctor
    /// <see cref="NetSession(ITransport)"/>.</summary>
    public NetSession(TcpClient tcpClient)
        : this(CreatePlaintextTransport(tcpClient))
    {
    }

    /// <summary>Règle le socket puis wrappe le transport clair.</summary>
    private static TcpTransport CreatePlaintextTransport(
        TcpClient tcpClient)
    {
        TcpSocketTuning.Apply(tcpClient);
        return new TcpTransport(tcpClient);
    }

    /// <summary>Session sur un transport déjà construit — le drain
    /// démarre dès la construction : le blob RSA du handshake transite
    /// lui-même par la même file, avant l'installation des
    /// transforms.</summary>
    public NetSession(ITransport transport)
    {
        _transport = transport;
        _drainTask = Task.Run(DrainSendQueueAsync);
        _sendQueueGauge ??= EdenMetrics.ObserveSendQueue(
            () => _globalQueued);
    }

    /// <summary>
    /// Boucle unique d'envoi : dépile les trames, applique le RC4
    /// (stream continu — l'état ne peut être muté que ici, jamais
    /// concurrentiellement), puis écrit sur le socket.
    /// Une erreur d'écriture termine la session : les envois suivants
    /// échouent vite au lieu de s'accumuler.
    /// </summary>
    private async Task DrainSendQueueAsync()
    {
        // Regroupement : les trames déjà en file sont concaténées dans
        // un buffer et partent en UN write — sous broadcast (N
        // destinataires × trames/tick), ça divise le nombre de syscalls
        // par le nombre de trames prêtes. Le RC4 reste appliqué trame
        // par trame, dans l'ordre de la file (keystream = ordre du fil).
        var batch = ArrayPool<byte>.Shared.Rent(SendBatchCapacity);
        try
        {
            while (await _sendQueue.Reader.WaitToReadAsync(
                       _sendCts.Token).ConfigureAwait(false))
            {
                var used = 0;
                while (_sendQueue.Reader.TryRead(out var item))
                {
                    Interlocked.Decrement(ref _globalQueued);
                    try
                    {
                        // Transform payload uniquement (le préfixe
                        // varint reste en clair) — appliquée ici, dans
                        // l'ordre de la file = ordre réel du fil,
                        // jamais au enqueue (keystream continu).
                        if (item.TransformOffset >= 0 && !ForcePlaintext)
                        {
                            var span = item.Buf.AsSpan(
                                item.TransformOffset,
                                item.TransformLength);
                            if (_sendTransformOverride != null)
                                _sendTransformOverride(span);
                            else if (_transport.HasSendTransform)
                                _transport.TransformSend(span);
                        }
                        if (item.Len > batch.Length)
                        {
                            // Trame géante : vider le batch puis écrire
                            // directement (copie inutile).
                            used = await FlushBatchAsync(batch, used);
                            await _transport.WriteAsync(
                                item.Buf.AsMemory(0, item.Len),
                                _sendCts.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            if (used + item.Len > batch.Length)
                                used = await FlushBatchAsync(batch, used);
                            item.Buf.AsSpan(0, item.Len).CopyTo(
                                batch.AsSpan(used));
                            used += item.Len;
                        }
                        EdenMetrics.PacketsOut.Add(1);
                        EdenMetrics.BytesOut.Add(item.Len);
                    }
                    finally
                    {
                        // Rend le buffer loué — sans retour, le pool
                        // ne recycle rien et on paierait l'alloc quand
                        // même (ArrayPool.Return n'est jamais obligé,
                        // seulement la condition du gain).
                        ArrayPool<byte>.Shared.Return(item.Buf);
                    }
                }
                await FlushBatchAsync(batch, used);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Socket mort ou annulation interne — la boucle de lecture
            // conclura la session (CloseReason renseigné pour le log).
            CloseReason ??= $"envoi échoué ({ex.GetType().Name})";
        }
        catch (OperationCanceledException) { }
        finally
        {
            ArrayPool<byte>.Shared.Return(batch);
        }
    }

    /// <summary>Écrit <paramref name="used"/> octets de
    /// <paramref name="batch"/> sur le socket et remet le compteur à
    /// zéro. No-op sur buffer vide.</summary>
    private async Task<int> FlushBatchAsync(byte[] batch, int used)
    {
        if (used <= 0) return 0;
        await _transport.WriteAsync(batch.AsMemory(0, used),
            _sendCts.Token).ConfigureAwait(false);
        return 0;
    }

    /// <summary>Taille du buffer de regroupement du drain — ~64 Ko,
    /// largement au-delà d'un burst de trames de vue/mouvement.</summary>
    private const int SendBatchCapacity = 64 * 1024;

    // ------------------------------------------------------------------
    // Handshake de transport (RSA/RC4 via ITransport.StartAsync)
    // ------------------------------------------------------------------

    private ITransportContext? _transportContext;

    /// <summary>
    /// Exécute le handshake du transport (blob RSA → clé RC4 pour
    /// <see cref="LegacyLapisTransport"/> ; no-op sur
    /// <see cref="TcpTransport"/>). Le contexte fourni au transport
    /// route les octets bruts : envoi par la file bornée (ordre
    /// préservé avec les trames qui suivent), lecture directe
    /// pré-framing. Retourne false sur échec (timeout, clé
    /// indéchiffrable, EOF) avec <see cref="CloseReason"/> renseigné —
    /// comme l'ancien ReceiveRc4KeyAsync.
    /// </summary>
    public async Task<bool> StartTransportAsync(
        CancellationToken cancellationToken = default)
    {
        _transportContext ??= new SessionTransportContext(this);
        try
        {
            await _transport.StartAsync(_transportContext,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Échec handshake (TimeoutException, InvalidDataException,
            // EndOfStreamException) — fermeture propre, cause tracée.
            CloseReason ??= ex.Message;
            return false;
        }
    }

    /// <summary>Pont brut session↔transport pendant
    /// <see cref="ITransport.StartAsync"/> : SendRaw enfile dans la
    /// même file bornée que les trames (jamais transformé) ; ReadRaw
    /// lit le transport directement, pré-framing.</summary>
    private sealed class SessionTransportContext(NetSession session)
        : ITransportContext
    {
        /// <inheritdoc/>
        public Task SendRawAsync(ReadOnlyMemory<byte> data,
            CancellationToken ct) =>
            session.SendBytesAsync(data, ct);

        /// <inheritdoc/>
        public async Task<int> ReadRawAsync(Memory<byte> buffer,
            CancellationToken ct) =>
            await session._transport.ReadAsync(buffer, ct)
                .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Envoi
    // ------------------------------------------------------------------

    /// <summary>
    /// Envoie un paquet de commande sérialisé au client.
    /// Reproduit CNetCommand::_Serialize + NextOutputPacket :
    ///   [varint len][RC4(u16 NCID + payload)]   (crypto active)
    ///   [varint len][u16 NCID + payload]        (pas de crypto — mode clair)
    /// len = taille du payload (NCID inclus) en LEB128 (1-5 octets).
    /// </summary>
    public async Task SendAsync(NetCommand command, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return;

        var opcode = command.Opcode;
        // La table client (EasyFun/Reforged) contient une commande
        // supplémentaire à l'index 38 : tous les CNC_CW_* ≥38 partent +1.
        if (opcode >= OutboundNcidShiftAbove && opcode <= 83) opcode++;
        var outStream = new OutBinStream();
        outStream.WriteShort((short)opcode); // NCID en premier
        command.Serialize(outStream);
        var payload = outStream.ToArray();
        _trace.Record(inbound: false, opcode, payload.Length);

        if (NetDebug.Enabled)
            EdenLog.Write($"[{DateTime.Now:HH:mm:ss.fff}] [PKT-OUT len={payload.Length}] NCID={opcode} {Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 96)))}");

        // Marge de 5 octets max pour le préfixe varint ; le transform
        // démarre après l'en-tête (hdrLen = octets varint écrits).
        var frame = ArrayPool<byte>.Shared.Rent(
            VarintCodec.MaxEncodedLength + payload.Length);
        var hdrLen = VarintCodec.WriteVarint(frame, (uint)payload.Length);
        payload.CopyTo(frame, hdrLen);

        await SendFrameAsync(frame, hdrLen + payload.Length, hdrLen,
            payload.Length, cancellationToken);
    }

    public Task SendRawAsync(byte[] data, CancellationToken cancellationToken = default) =>
        SendBytesAsync(data, cancellationToken);

    /// <summary>Envoie [varint len][RC4(u16 ncid + payload)] — une
    /// seule allocation de trame (varint@0, ncid@hdrLen,
    /// payload@hdrLen+2) au lieu du couple body+frame qui doublait la
    /// copie sur le chemin d'envoi.</summary>
    public async Task SendPacketAsync(ushort ncid, byte[] payload, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return;
        var frame = ArrayPool<byte>.Shared.Rent(
            VarintCodec.MaxEncodedLength + 2 + payload.Length);
        var hdrLen = VarintCodec.WriteVarint(frame,
            (uint)(2 + payload.Length));
        BitConverter.GetBytes(ncid).CopyTo(frame, hdrLen);
        payload.CopyTo(frame, hdrLen + 2);
        _trace.Record(inbound: false, ncid, 2 + payload.Length);
        if (NetDebug.Enabled)
            EdenLog.Write($"[{DateTime.Now:HH:mm:ss.fff}] [PKT-OUT len={2 + payload.Length}] NCID={ncid} {Convert.ToHexString(frame.AsSpan(hdrLen, Math.Min(2 + payload.Length, 96)))}");
        await SendFrameAsync(frame, hdrLen + 2 + payload.Length, hdrLen,
            2 + payload.Length, cancellationToken);
    }

    /// <summary>Enfile une trame pour le drain unique. Le transform
    /// (RC4) est appliqué dans la boucle de drainage (ordre keystream
    /// = ordre réel du fil) — jamais ici : deux envois concurrents
    /// verraient sinon l'état du keystream s'entrelacer (bug live
    /// historique : cast temporisé + boucle principale → client
    /// décodait du bruit et se figeait).</summary>
    private async Task SendFrameAsync(byte[] frame, int frameLen,
        int transformOffset, int transformLength,
        CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            // Le buffer n'entrera jamais en file → on le rend ici.
            ArrayPool<byte>.Shared.Return(frame);
            return;
        }
        if (DropOnFullQueue)
        {
            // Chemin boucle acteur : aucune attente — un destinataire
            // dont la file déborde est un client lent ou mort ; on
            // ferme sa session plutôt que de figer l'expéditeur.
            if (!_sendQueue.Writer.TryWrite(
                    (frame, frameLen, transformOffset, transformLength)))
            {
                ArrayPool<byte>.Shared.Return(frame);
                EdenMetrics.SendQueueDrops.Add(1);
                CloseReason ??=
                    $"backlog d'envoi ({SendQueueCapacity} trames, " +
                    "client lent)";
                AbortSendSide();
            }
            else
            {
                Interlocked.Increment(ref _globalQueued);
            }
            return;
        }
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        try
        {
            await _sendQueue.Writer.WriteAsync(
                (frame, frameLen, transformOffset, transformLength),
                timeout.Token);
            Interlocked.Increment(ref _globalQueued);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            ArrayPool<byte>.Shared.Return(frame);
            // File pleine au-delà du délai : client qui ne lit plus —
            // fermeture propre plutôt que rétention mémoire.
            CloseReason ??=
                $"backlog d'envoi ({SendQueueCapacity} trames, " +
                $"{SendTimeout.TotalMilliseconds}ms)";
            await DisposeAsync();
        }
        catch (ChannelClosedException)
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }

    /// <summary>
    /// Coupe le côté émission d'une session lente/morte SANS attendre :
    /// complète la file (les producteurs suivants échouent vite en
    /// TryWrite), annule le drain (écriture socket potentiellement
    /// bloquée) et fait un Shutdown du socket — le ReadAsync du thread
    /// de lecture reçoit EOF et conclut la session par le chemin
    /// normal (OnDisconnect, retrait du registre, DisposeAsync).
    /// Jamais appelé depuis le drain lui-même.
    /// </summary>
    private void AbortSendSide()
    {
        // Une seule fois : les trames suivantes échouent en TryWrite
        // sur la file complétée sans repasser par Shutdown.
        if (Interlocked.Exchange(ref _sendAborted, 1) != 0) return;
        _sendQueue.Writer.TryComplete();
        _sendCts.Cancel();
        _transport.Abort(); // Shutdown(Both) → EOF côté reader
    }
    private int _sendAborted;

    /// <summary>
    /// Enfile des octets bruts (handshake, trames de diag) — pas de
    /// transform (TransformOffset = -1).
    /// </summary>
    public Task SendBytesAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return Task.CompletedTask;
        _trace.Record(inbound: false, ncid: -1, buffer.Length);
        var frame = ArrayPool<byte>.Shared.Rent(buffer.Length);
        buffer.Span.CopyTo(frame);
        return SendFrameAsync(frame, buffer.Length, -1, 0,
            cancellationToken);
    }

    // ------------------------------------------------------------------
    // Réception — accumulation + framing [varint len][payload]
    // ------------------------------------------------------------------

    /// <summary>
    /// Lit des données entrantes depuis le socket (brut, avant framing).
    /// </summary>
    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return 0;
        return await _transport.ReadAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// Alimente le buffer de réception puis extrait les paquets complets.
    /// Reproduit OnParsingHeader + MakePackets :
    ///   [varint len][len octets] — len en clair, payload RC4-déchiffré
    ///   en place.
    /// Retourne les payloads déchiffrés ([u16 NCID + champs]).
    /// </summary>
    public async Task<List<byte[]>> ReadPacketsAsync(CancellationToken cancellationToken = default)
    {
        var packets = await ReadPacketsCoreAsync(cancellationToken);
        EdenMetrics.PacketsIn.Add(packets.Count);
        return packets;
    }

    private async Task<List<byte[]>> ReadPacketsCoreAsync(CancellationToken cancellationToken)
    {
        var packets = new List<byte[]>();
        // Buffer de lecture loué au pool — un alloc par appel éliminé
        // (les payloads extraits vivent dans leurs propres arrays).
        var tmp = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
        // Rate-limit applicatif : fenêtre d'une seconde.
        if (MaxPacketsPerSecond > 0 &&
            DateTime.UtcNow - _rateWindowStart >= TimeSpan.FromSeconds(1))
        {
            _rateWindowStart = DateTime.UtcNow;
            _packetsThisWindow = 0;
        }
        int n;
        // Idle timeout : borne le temps d'attente d'octets. Lié au CT
        // appelant — seule une expiration propre au timeout positionne
        // CloseReason (une annulation externe reste une fermeture
        // volontaire).
        if (IdleTimeout > TimeSpan.Zero && !SkipIdleTimeout)
        {
            using var idleCts = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            idleCts.CancelAfter(IdleTimeout);
            try
            {
                n = await ReceiveAsync(tmp, idleCts.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                CloseReason = $"idle timeout ({IdleTimeout.TotalSeconds}s)";
                return packets;
            }
        }
        else
        {
            n = await ReceiveAsync(tmp, cancellationToken);
        }
        if (n <= 0) return packets;
        if (NetDebug.Enabled)
            EdenLog.Write($"[RAW-IN {n}B] {Convert.ToHexString(tmp.AsSpan(0, Math.Min(n, 96)))}");
        _recvBuffer.Append(tmp.AsSpan(0, n));

        try
        {
            while (_recvBuffer.TryTakePacket(out var payload))
            {
                // Déchiffre en place (CMSTCPConnection::Decryption
                // vtable+0x30) — no-op sur transport en clair.
                _transport.TransformRecv(payload);
                var ncid = payload[0] | (payload[1] << 8);
                _trace.Record(inbound: true, ncid, payload.Length);
                if (NetDebug.Enabled)
                    EdenLog.Write($"[{DateTime.Now:HH:mm:ss.fff}] [PKT len={payload.Length}] NCID={ncid} {Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 64)))}");
                packets.Add(payload);
            }
        }
        catch (InvalidDataException ide)
        {
            // Trame malformée (len<2, varint overlong ou >cap) :
            // corrompue ou hostile — fermeture propre avec la cause
            // plutôt que d'ignorer silencieusement.
            CloseReason = $"trame invalide ({ide.Message})";
        }
        if (MaxPacketsPerSecond > 0 &&
            _packetsThisWindow + packets.Count > MaxPacketsPerSecond)
        {
            _packetsThisWindow += packets.Count;
            // Flood : déconnexion propre — le client recommençant à
            // flotter est coupé sans affecter les autres sessions.
            CloseReason =
                $"rate limit ({_packetsThisWindow} paquets/s > " +
                $"{MaxPacketsPerSecond})";
            return new List<byte[]>();
        }
        _packetsThisWindow += packets.Count;
        if (packets.Count > 0)
            LastPacketsTimestamp =
                System.Diagnostics.Stopwatch.GetTimestamp();
        return packets;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    /// <summary>
    /// Ferme la session réseau et libère les ressources. Complète la
    /// file d'envoi puis attend la fin du drain (bornée) avant de
    /// disposer le socket — les trames déjà enfilées partent avant la
    /// coupure. Les producteurs concurrents voient
    /// <c>ChannelClosedException</c> absorbée côté enqueue.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        EdenMetrics.AbnormalDisconnects.Add(1,
            new KeyValuePair<string, object?>("reason",
                CloseReason ?? "normale"));

        _sendQueue.Writer.TryComplete();
        // Attente bornée : un peer qui ne lit plus ne doit pas retarder
        // la libération de la session.
        var drained = await Task.WhenAny(_drainTask,
            Task.Delay(TimeSpan.FromSeconds(3)));
        if (drained != _drainTask)
            _sendCts.Cancel(); // aborte le drain (écriture bloquée)
        try { await _drainTask; }
        catch (OperationCanceledException) { }
        catch (Exception) { /* drain déjà en échec */ }

        await _transport.DisposeAsync();
        _sendCts.Dispose();
    }
}
