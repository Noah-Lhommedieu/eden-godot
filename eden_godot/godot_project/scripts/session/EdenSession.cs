using System.Security.Cryptography;
using EdenClient.Net;
using EdenClient.Protocol;

namespace EdenClient.Session;

/// <summary>Options de connexion — défauts = serveur local natif.</summary>
public sealed class EdenSessionOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int LoginPort { get; set; } = 6543;
    public int TicketPort { get; set; } = 2067;
    /// <summary>Repli si le ticket monde annonce 0.0.0.0:0.</summary>
    public int WorldPort { get; set; } = 5200;
    /// <summary>Repli si le ticket zone annonce 0.0.0.0:0.</summary>
    public int ZonePort { get; set; } = 5201;
    public string Account { get; set; } = "admin";
    public string Password { get; set; } = "admin";
    /// <summary>Version envoyée dans LoginAccount (capture : "002.003.05.67").</summary>
    public string ClientVersion { get; set; } = "002.003.05.67";
    /// <summary>Chaîne machine envoyée au World (capture : MAC du client).</summary>
    public string MachineId { get; set; } = "BC-EC-A0-43-F3-80";
    /// <summary>null → LastWorldId de la liste, sinon premier monde.</summary>
    public short? WorldId { get; set; }
    /// <summary>null → premier personnage de la liste.</summary>
    public uint? CharId { get; set; }
    /// <summary>Nom de zone demandé au World (capture : "3966").</summary>
    public string ZoneName { get; set; } = "3966";
    /// <summary>Mission trace ids pour SetMissionTrace (capture : 10 slots,
    /// premier = 1009). Vide → n'envoie que le compteur 0.</summary>
    public int[]? MissionTraceIds { get; set; } = { 1009, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    /// <summary>Secondes d'écoute passive après l'entrée en zone.</summary>
    public double EntryListenSeconds { get; set; } = 20;
    /// <summary>« modern » (TLS 1.3, défaut) ou « legacy »
    /// (handshake RSA/RC4 lapis — repli <c>EDEN_TRANSPORT=legacy</c>).</summary>
    public string Transport { get; set; } = "modern";
    /// <summary>Empreinte SHA-256 du cert serveur (pinning dev ;
    /// sinon validation chaîne standard en prod).</summary>
    public string? TlsThumbprint { get; set; }
    /// <summary>Flag dev : accepte n'importe quel cert (warning).</summary>
    public bool TlsInsecure { get; set; }
    /// <summary>Handshake de version post-TLS (phase 4d) — défaut
    /// activé ; jamais émis en transport « legacy ».</summary>
    public bool VersionCheck { get; set; } = true;
    /// <summary>Répertoire du corpus <c>*.ini</c> gamedata client dont
    /// le manifest SHA-256 est envoyé dans le VersionHello — null =
    /// résolution automatique (<c>data/db</c> sous la racine gamedata,
    /// ou <c>$EDEN_DATA_DIR[/raw_assets]/data/db</c> hors Godot).</summary>
    public string? ManifestDir { get; set; }
    /// <summary>Empreinte manifest forcée (32 octets) — prioritaire sur
    /// <see cref="ManifestDir"/> (tests/outils).</summary>
    public byte[]? ManifestHashOverride { get; set; }
}

/// <summary>
/// Résumé de personnage extrait de CharacterListChunk.
/// <paramref name="Appearance"/> = blob 32 octets natif (+0x24 de
/// l'entrée — bitfields <see cref="Protocol.AppearanceCodec"/> ;
/// <paramref name="ClassLv"/> = champ « Feature » natif +0x14
/// (class_lv, désasm PGCharacterDBO) ; <paramref name="RaceId"/> =
/// +0x10.
/// </summary>
public sealed record CharacterSummary(
    int Id, string Name, short ClassId, short Level, short NodeId,
    short RaceId = 0, short ClassLv = 0, byte[]? Appearance = null,
    IReadOnlyDictionary<int, int>? PreviewWorn = null);

/// <summary>
/// Orchestrateur du flux de session Eden :
///   ConnectGateway → Login → SelectWorld → WorldLogin
///   → SelectCharacter → GetZoneTicket → ZoneConnect → ZoneEntry.
///
/// Séquence wire = captures/mitm_native_entry.log + eden_session.py ;
/// le burst d'entrée zone = captures/zsniff_cs_entry_success.bin.
/// </summary>
public sealed class EdenSession
{
    private readonly EdenSessionOptions _opt;

    /// <summary>Trace texte (format zsniff-like posé par NetSmoke).</summary>
    public event Action<string>? Log;

    /// <summary>Chaque trame décodée (direction, NCID, payload brut).</summary>
    public event Action<bool, ushort, byte[]>? Frame; // (sent, ncid, payload)

    /// <summary>Handlers par NCID zone — invoqués à chaque trame
    /// serveur reçue (dans le thread de réception : l'appelant Godot
    /// doit re-poster sur le thread principal, ex. CallDeferred).</summary>
    private readonly Dictionary<int, List<Action<BinReader>>> _zoneDispatch = new();

    /// <summary>Handlers par NCID <b>monde</b> — les trames lues sur la
    /// connexion :5200 (item mall, social, guilde) y sont routées par
    /// <see cref="WorldListenLoopAsync"/> ; la numérotation monde se
    /// chevauche avec celle de la zone (ex. 62 = catalogue mall vs
    /// requête banque) → table séparée obligatoire.</summary>
    private readonly Dictionary<int, List<Action<BinReader>>> _worldDispatch = new();

    /// <summary>Enregistre un handler pour un NCID zone S→C. Le reader
    /// est positionné juste après le u16 NCID.</summary>
    public void OnZone(int ncid, Action<BinReader> handler)
    {
        lock (_zoneDispatch)
        {
            if (!_zoneDispatch.TryGetValue(ncid, out var list))
                _zoneDispatch[ncid] = list = new List<Action<BinReader>>();
            list.Add(handler);
        }
    }

    /// <summary>Enregistre un handler pour un NCID <b>monde</b> S→C.
    /// Mêmes règles que <see cref="OnZone"/> : parse sur le thread de
    /// réception, puis re-poster vers le thread principal Godot.</summary>
    public void OnWorld(int ncid, Action<BinReader> handler)
    {
        lock (_worldDispatch)
        {
            if (!_worldDispatch.TryGetValue(ncid, out var list))
                _worldDispatch[ncid] = list = new List<Action<BinReader>>();
            list.Add(handler);
        }
    }

    /// <summary>NCIDs déjà signalés « non géré » (une fois par
    /// session — règle phase 6 : tout NCID émis est traité ou
    /// logué explicitement).</summary>
    private readonly HashSet<int> _unhandledLogged = new();

    /// <summary>Vrai si au moins un handler est enregistré pour
    /// <paramref name="ncid"/> dans <paramref name="dispatch"/> (miroir
    /// du lookup de <see cref="DispatchReceived"/>).</summary>
    private static bool HasHandlers(
        Dictionary<int, List<Action<BinReader>>> dispatch, ushort ncid)
    {
        lock (dispatch)
            return dispatch.ContainsKey(ncid);
    }

    /// <summary>Dispatch d'une trame serveur reçue vers les handlers
    /// d'une table (<see cref="_zoneDispatch"/> ou
    /// <see cref="_worldDispatch"/> selon la connexion lue). Les
    /// handlers inconnus sont simplement ignorés.</summary>
    private void DispatchReceived(ushort ncid, byte[] payload,
        Dictionary<int, List<Action<BinReader>>> dispatch)
    {
        List<Action<BinReader>>? handlers;
        lock (dispatch)
            dispatch.TryGetValue(ncid, out handlers);
        if (handlers == null) return;
        foreach (var h in handlers)
        {
            try
            {
                var r = new BinReader(payload);
                r.ReadUShort(); // skip NCID
                h(r);
            }
            catch (Exception ex)
            {
                Trace($"[session] handler NCID {ncid} en échec : {ex.Message}");
            }
        }
    }

    public List<CharacterSummary> Characters { get; } = new();
    public byte[]? WorldTicketBytes { get; private set; }
    public byte[]? ZoneTicketBytes { get; private set; }
    public (string ip, ushort port) WorldEndPoint { get; private set; }
    public (string ip, ushort port) ZoneEndPoint { get; private set; }
    public int AccountId { get; private set; }
    public uint SelectedCharId { get; private set; }

    private EdenConnection? _login;
    private EdenConnection? _world;
    private EdenConnection? _zone;
    private CancellationTokenSource? _keepAliveCts;

    /// <summary>Émis quand la connexion zone ou monde se coupe de
    /// manière inattendue (P0.2 — retour propre à l'écran de connexion
    /// avec message clair, jamais un crash).</summary>
    public event Action<string>? Disconnected;
    /// <summary>Dernière raison de déconnexion à afficher sur le
    /// LoginScreen.</summary>
    public static string? LastDisconnectReason { get; set; }

    /// <summary>Connexion zone active après <see cref="ZoneConnectAsync"/>.</summary>
    public EdenConnection? Zone => _zone;

    public EdenSession(EdenSessionOptions? options = null)
    {
        _opt = options ?? new EdenSessionOptions();
        // A4.4 : pong custom Reborn (S→C 9057, corps vide) renvoyé par
        // le serveur à chaque Echo (57) — fin du RTT.
        OnZone(EchoPongNcid, _ => OnEchoPong());
#if GODOT
        OnZone(Protocol.HeroTraitWire.Ncid,
            r => Hud.HeroTrait.Set(Protocol.HeroTraitWire.Read(r)));
#endif
    }

    /// <summary>NCID S→C custom (hors protocole natif) : réponse
    /// serveur à l'Echo C→S 57, sert à mesurer le RTT.</summary>
    public const int EchoPongNcid = 9057;
    /// <summary>Intervalle des sondes Echo (RTT) — les autres
    /// keepalives (login/monde) restent à 45 s.</summary>
    private const int EchoProbeSeconds = 5;
    private long _echoSentTicks;
    private static float _pingMs = -1f;
    /// <summary>RTT zone lissé (EMA, ms) ; &lt; 0 = pas encore mesuré
    /// (hors ligne / pas de pong). Lu depuis le thread Godot.</summary>
    public static float PingMs => _pingMs;

    private void OnEchoPong()
    {
        var t0 = Interlocked.Exchange(ref _echoSentTicks, 0);
        if (t0 == 0) return;
        var ms = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        var cur = _pingMs;
        _pingMs = cur < 0 ? ms : cur * 0.7f + ms * 0.3f;
    }

    private void StartKeepAlive()
    {
        if (_keepAliveCts != null) return;
        _keepAliveCts = new CancellationTokenSource();
        var ct = _keepAliveCts.Token;
        _ = Task.Run(async () =>
        {
            var tick = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(EchoProbeSeconds),
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                tick++;
                // A4.4 : sonde RTT zone toutes les 5 s (Echo → pong).
                if (_zone is { IsConnected: true } zp)
                {
                    try
                    {
                        Interlocked.Exchange(ref _echoSentTicks,
                            System.Diagnostics.Stopwatch.GetTimestamp());
                        await SendAsync(zp, ZoneEmptyRequest.Echo, ct)
                            .ConfigureAwait(false);
                    }
                    catch { /* connexion fermée */ }
                }
                else _pingMs = -1f;
                // Keepalives login/monde : cadence d'origine (45 s).
                if (tick % (45 / EchoProbeSeconds) != 0) continue;
                // P0.2 : ping périodique sur les connexions ouvertes
                // avant que EDEN_IDLE_TIMEOUT_SEC (300 s) ne les coupe.
                if (_login is { IsConnected: true } l && _world == null)
                {
                    try
                    {
                        await SendAsync(l, new LoginServerHello
                            { Value = _opt.ClientVersion }, ct)
                            .ConfigureAwait(false);
                    }
                    catch { /* connexion fermée */ }
                }
                if (_world is { IsConnected: true } w && _zone == null)
                {
                    try
                    {
                        await SendAsync(w, new WorldServerHello(), ct)
                            .ConfigureAwait(false);
                    }
                    catch { /* connexion fermée */ }
                }
            }
        }, ct);
    }

    /// <summary>Construit une <see cref="EdenConnection"/> aux options
    /// transport de la session (TLS 1.3 + pinning/insecure ou legacy
    /// RSA/RC4 — propagé aux trois liens login/monde/zone), y compris
    /// les paramètres du handshake de version (phase 4d).</summary>
    private EdenConnection NewConnection() => new()
    {
        Transport = _opt.Transport,
        TlsThumbprint = _opt.TlsThumbprint,
        TlsInsecure = _opt.TlsInsecure,
        ClientVersion = _opt.ClientVersion,
        VersionCheck = _opt.VersionCheck,
        ManifestHashOverride = _opt.ManifestHashOverride,
        ManifestDir = _opt.ManifestDir ?? ResolveDefaultManifestDir(),
    };

    /// <summary>Répertoire <c>data/db</c> du corpus gamedata client :
    /// <c>gamedata_dir</c> de client.cfg s'il est posé, puis sous Godot
    /// la racine résolue par <c>DataPaths.FindSubdir("data")</c>, puis
    /// hors Godot (outils/tests) <c>$EDEN_DATA_DIR/data/db</c> et
    /// <c>$EDEN_DATA_DIR/raw_assets/data/db</c>. Null = manifest vide —
    /// toléré en non-strict.</summary>
    private static string? ResolveDefaultManifestDir()
    {
        var cfgDir = LazyConfig().GamedataDir;
        if (!string.IsNullOrEmpty(cfgDir) && Directory.Exists(cfgDir))
            return cfgDir;
#if GODOT
        // Sous le runtime Godot, DataPaths connaît la chaîne de
        // résolution complète (EDEN_DATA_DIR → <exe>/gamedata →
        // raw_assets/ dev → res://gamedata). scripts/session ne peut
        // pas référencer DataPaths en tests : le bloc est compilé
        // seulement sous GODOT.
        var data = DataPaths.FindSubdir("data");
        if (data != null)
        {
            var db = Path.Combine(data, "db");
            // Loose ou « epk://data/db » en mode pack-seul.
            if (Vfs.EdenVfs.Exists(db)) return db;
        }
#endif
        var env = Environment.GetEnvironmentVariable("EDEN_DATA_DIR");
        if (!string.IsNullOrEmpty(env))
        {
            foreach (var candidate in new[]
            {
                Path.Combine(env, "data", "db"),
                Path.Combine(env, "raw_assets", "data", "db"),
            })
                if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>client.cfg relu paresseusement — les écrans propagent
    /// déjà ses valeurs connues ; seules les clés nouvelles (ex.
    /// <c>gamedata_dir</c>) passent par ce repli tant que les écrans
    /// ne les câblent pas.</summary>
    private static ClientConfig LazyConfig() =>
        _lazyConfig ??= ClientConfig.Load();
    private static ClientConfig? _lazyConfig;

    private void Trace(string msg) => Log?.Invoke(msg);
    /// <summary>Émet une trame au log + dispatch. Les trames reçues
    /// sont routées vers la table de <paramref name="dispatch"/>
    /// (<see cref="_zoneDispatch"/> par défaut — historique : avant la
    /// séparation monde, tout passait par la table zone).
    /// <paramref name="world"/> = trames lues sur la connexion monde
    /// → <see cref="_worldDispatch"/>.</summary>
    private void Emit(bool sent, ushort ncid, byte[] payload,
        bool world = false)
    {
        if (!sent)
            DispatchReceived(ncid, payload,
                world ? _worldDispatch : _zoneDispatch);
        Frame?.Invoke(sent, ncid, payload);
    }

    private static string Md5Hex(string input)
    {
        var bytes = MD5.HashData(System.Text.Encoding.ASCII.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Envoie un message + émet l'évènement Frame.</summary>
    private async Task SendAsync(EdenConnection conn, NetMessage msg, CancellationToken ct)
    {
        var payload = msg.ToPayload();
        await conn.SendPayloadAsync(payload, ct).ConfigureAwait(false);
        Emit(true, (ushort)msg.Opcode, payload);
    }

    // ---------------------------------------------------------------
    // 1) Gateway/Login :6543 — hello, auth, liste mondes, ticket monde
    // ---------------------------------------------------------------

    /// <summary>Connexion TCP + handshake RSA/RC4 au LoginServer.</summary>
    public async Task<EdenConnection> ConnectGatewayAsync(CancellationToken ct = default)
    {
        _login = NewConnection();
        await _login.ConnectAsync(_opt.Host, _opt.LoginPort, ct).ConfigureAwait(false);
        Trace($"[session] handshake login OK {_opt.Host}:{_opt.LoginPort}");
        StartKeepAlive();
        // Le serveur envoie ClientHello (NCID 3) juste après le handshake.
        return _login;
    }

    /// <summary>Authentification : attend ClientHello, envoie LoginAccount,
    /// retourne la liste des mondes (NCID 5). Lance LoginFail si NCID 4.</summary>
    public async Task<WorldList> LoginAsync(CancellationToken ct = default)
    {
        var conn = RequireLogin();
        var hello = await WaitForAsync<LoginClientHello>(conn, 3, ct).ConfigureAwait(false);
        Trace($"[session] ClientHello {hello.Value}");

        await SendAsync(conn, new LoginAccount
        {
            Account = _opt.Account,
            PasswordHash = Md5Hex(_opt.Password),
            Version = _opt.ClientVersion,
        }, ct).ConfigureAwait(false);

        // Réponse attendue : NCID 5 (WorldList) ou NCID 4 (LoginFail).
        while (true)
        {
            var f = await conn.ReadFrameAsync(ct).ConfigureAwait(false)
                ?? throw new EndOfStreamException("login : connexion fermée");
            Emit(false, f.Ncid, f.Payload);
            if (f.Ncid == 4)
            {
                var fail = Parse<LoginFail>(f.Payload);
                throw new InvalidOperationException(
                    $"Login refusé (code {fail.Code}) pour '{_opt.Account}'");
            }
            if (f.Ncid == 5)
            {
                var list = Parse<WorldList>(f.Payload);
                LastWorldList = list;
                Trace($"[session] {list.Worlds.Count} monde(s), lastWorld={list.LastWorldId}");
                return list;
            }
        }
    }

    /// <summary>Sélection du monde → attend le ticket monde (NCID 6).</summary>
    public async Task<WorldTicket> SelectWorldAsync(short worldId, CancellationToken ct = default)
    {
        var conn = RequireLogin();
        await SendAsync(conn, new LoginWorldSelect
        {
            WorldId = worldId,
            Account = _opt.Account,
        }, ct).ConfigureAwait(false);

        while (true)
        {
            var f = await conn.ReadFrameAsync(ct).ConfigureAwait(false)
                ?? throw new EndOfStreamException("login : connexion fermée");
            Emit(false, f.Ncid, f.Payload);
            switch (f.Ncid)
            {
                case 2:
                    var reply = Parse<LoginWorldReply>(f.Payload);
                    Trace($"[session] WorldReply id={reply.WorldId} addr={reply.Address}");
                    break;
                case 4:
                    var fail = Parse<LoginFail>(f.Payload);
                    throw new InvalidOperationException($"SelectWorld refusé (code {fail.Code})");
                case 6:
                    var ticket = Parse<WorldTicket>(f.Payload);
                    WorldTicketBytes = ticket.Ticket;
                    WorldEndPoint = ticket.WorldEndPoint();
                    if (WorldEndPoint.port == 0 || WorldEndPoint.ip == "0.0.0.0")
                        WorldEndPoint = (_opt.Host, (ushort)_opt.WorldPort);
                    AccountId = BitConverter.ToInt32(ticket.Ticket, 0);
                    Trace($"[session] ticket monde → {WorldEndPoint.ip}:{WorldEndPoint.port} " +
                          $"(account={AccountId})");
                    return ticket;
            }
        }
    }

    // ---------------------------------------------------------------
    // 2) World :5200 — login ticket, persos, sélection, ticket zone
    // ---------------------------------------------------------------

    /// <summary>Connexion au WorldServer + envoi du ticket (NCID 1).</summary>
    public async Task<EdenConnection> WorldLoginAsync(CancellationToken ct = default)
    {
        if (WorldTicketBytes == null)
            throw new InvalidOperationException("SelectWorldAsync d'abord");
        _world = NewConnection();
        await _world.ConnectAsync(WorldEndPoint.ip, WorldEndPoint.port, ct).ConfigureAwait(false);
        Trace($"[session] handshake world OK {WorldEndPoint.ip}:{WorldEndPoint.port}");
        await SendAsync(_world, new WorldLoginTicket
        {
            Ticket = WorldTicketBytes,
            MachineId = _opt.MachineId,
        }, ct).ConfigureAwait(false);
        return _world;
    }

    /// <summary>Lit le hello + les chunks NCID 38 jusqu'au chunk de fin
    /// (Header == 2). Remplit <see cref="Characters"/>.</summary>
    public async Task<int> ReceiveCharacterListAsync(CancellationToken ct = default)
    {
        var conn = RequireWorld();
        var done = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!done && DateTime.UtcNow < deadline)
        {
            var f = await conn.ReadFrameAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (f == null) break;
            Emit(false, f.Ncid, f.Payload, world: true);
            switch (f.Ncid)
            {
                case 34:
                    Trace($"[session] world hello {Parse<WorldClientHello>(f.Payload).Value}");
                    break;
                case 38:
                    var chunk = Parse<CharacterListChunk>(f.Payload);
                    AddChunkEntries(chunk);
                    if (chunk.Header == 2) done = true;
                    break;
            }
        }
        return Characters.Count;
    }

    /// <summary>Rafraîchit <see cref="Characters"/> depuis les chunks
    /// NCID 38 (sections natives : 0 début, 1 données, 2 fin) —
    /// utilisé après une création/suppression : la liste est remplacée
    /// par les entrées de la section 1. Un NCID 39 (rejet du serveur —
    /// compte plein, nom non réservé…) interrompt l'attente : sans ça
    /// le rejet passait pour une liste vide → « rien ne se passe ».
    /// Retourne (code de rejet, 0) ou (null, count).</summary>
    private async Task<(short? Reject, int Count)> RefreshCharacterListAsync(
        CancellationToken ct)
    {
        var conn = RequireWorld();
        var fresh = new List<CharacterSummary>();
        var done = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!done && DateTime.UtcNow < deadline)
        {
            var f = await conn.ReadFrameAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (f == null) break;
            Emit(false, f.Ncid, f.Payload, world: true);
            if (f.Ncid == 39)
            {
                var rej = Parse<WorldVerifyNameResult>(f.Payload);
                Trace($"[session] refresh persos rejeté code={rej.Code}");
                return (rej.Code, 0);
            }
            if (f.Ncid != 38) continue;
            var chunk = Parse<CharacterListChunk>(f.Payload);
            if (chunk.Header == 1)
                foreach (var c in chunk.Characters)
                    fresh.Add(ToSummary(c));
            Trace($"[session] chunk persos header={chunk.Header} +{chunk.Characters.Count}");
            if (chunk.Header == 2) done = true;
        }
        Characters.Clear();
        Characters.AddRange(fresh);
        return (null, Characters.Count);
    }

    private void AddChunkEntries(CharacterListChunk chunk)
    {
        foreach (var c in chunk.Characters)
            Characters.Add(ToSummary(c));
        Trace($"[session] chunk persos header={chunk.Header} +{chunk.Characters.Count}");
    }

    private static CharacterSummary ToSummary(CharacterListChunk.Entry c)
    {
        // Preview clé 0 = items portés (loc→itemId, conteneur 0) —
        // l'équipement réel du perso pour la tenue du sélecteur
        // (serveur : Eden.World/Program.cs SendCharacterListAsync).
        Dictionary<int, int>? worn = null;
        foreach (var kv in c.Preview)
            if (kv.Key == 0)
                foreach (var it in kv.Value)
                    (worn ??= new Dictionary<int, int>())[it.Loc] =
                        it.ItemId;
        return new(c.Id, c.Name, c.ClassId, c.Level, c.NodeId,
            c.RaceId, c.Feature, c.Appearance, worn);
    }

    // ------------------------------------------------------------------
    // Création / suppression de personnage (Eden.World/Program.cs —
    // wire : 32 NameCheck / 4 CreateName / 5 CreateData / 6 Remove /
    // 7 Cancel → 39 VerifyNameResult, 38 liste fraîche, 41 CancelResp).
    // ------------------------------------------------------------------

    /// <summary>Envoie une requête nom et attend le NCID 39.
    /// Retourne (code, flag) — voir <see cref="WorldVerifyNameResult"/>.</summary>
    private async Task<(short Code, byte Flag)> NameResultAsync(
        NetMessage req, CancellationToken ct)
    {
        var conn = RequireWorld();
        await SendAsync(conn, req, ct).ConfigureAwait(false);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var f = await conn.ReadFrameAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (f == null) break;
            Emit(false, f.Ncid, f.Payload, world: true);
            if (f.Ncid == 39)
            {
                var r = Parse<WorldVerifyNameResult>(f.Payload);
                Trace($"[session] name result code={r.Code} flag={r.Flag}");
                return (r.Code, r.Flag);
            }
        }
        throw new TimeoutException("pas de réponse nom (NCID 39)");
    }

    /// <summary>NCID 32 — teste la disponibilité d'un nom
    /// (0 = disponible).</summary>
    public Task<(short Code, byte Flag)> VerifyCharacterNameAsync(
        string name, CancellationToken ct = default) =>
        NameResultAsync(new WorldCreateNameCheck { Value = name }, ct);

    /// <summary>NCID 4 — réserve le nom pour la création à venir
    /// (0 = réservé ; appeler ensuite <see cref="CreateCharacterDataAsync"/>).</summary>
    public Task<(short Code, byte Flag)> CreateCharacterNameAsync(
        string name, CancellationToken ct = default) =>
        NameResultAsync(new WorldCreateCharacterName { Value = name }, ct);

    /// <summary>NCID 5 — envoie les données du perso puis relit la
    /// liste (NCID 38) que le serveur repousse. Retourne le nouveau
    /// compte de personnages.</summary>
    /// <returns>(code de rejet serveur, 0) ou (0, count).</returns>
    public async Task<(short Code, int Count)> CreateCharacterDataAsync(
        byte race, byte classId, byte flag, byte[] appearance32,
        CancellationToken ct = default)
    {
        var conn = RequireWorld();
        var app = new byte[32];
        if (appearance32 is { Length: > 0 })
            Array.Copy(appearance32, app, Math.Min(32, appearance32.Length));
        await SendAsync(conn, new WorldCreateCharacterData
        {
            Race = race, ClassId = classId, Flag = flag, Appearance = app,
        }, ct).ConfigureAwait(false);
        var (reject, n) = await RefreshCharacterListAsync(ct).ConfigureAwait(false);
        Trace($"[session] création envoyée → {n} perso(s) rej={reject}");
        return (reject ?? 0, n);
    }

    /// <summary>NCID 6 — supprime le perso puis relit la liste.</summary>
    public async Task<int> RemoveCharacterAsync(
        int charId, string name, CancellationToken ct = default)
    {
        var conn = RequireWorld();
        await SendAsync(conn, new WorldRemoveCharacter
        {
            CharId = charId, CharName = name,
        }, ct).ConfigureAwait(false);
        var (reject, n) = await RefreshCharacterListAsync(ct).ConfigureAwait(false);
        Trace($"[session] suppression #{charId} → {n} perso(s) rej={reject}");
        return n;
    }

    /// <summary>NCID 7 — annule une création en cours (réponse 41,
    /// tolérante à l'absence).</summary>
    public async Task CancelCreateCharacterAsync(CancellationToken ct = default)
    {
        var conn = RequireWorld();
        await SendAsync(conn, new WorldCancelCreateCharacter(), ct)
            .ConfigureAwait(false);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var f = await conn.ReadFrameAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (f == null) break;
            Emit(false, f.Ncid, f.Payload, world: true);
            if (f.Ncid == 41) return;
        }
    }

    /// <summary>Sélection du perso → lit les chunks de données NCID 69.
    /// Le serveur C# envoie aussi le ticket zone (NCID 37) dès la sélection
    /// — on le met en cache pour <see cref="GetZoneTicketAsync"/>.</summary>
    public async Task<int> SelectCharacterAsync(uint charId, CancellationToken ct = default)
    {
        var conn = RequireWorld();
        SelectedCharId = charId;
        await SendAsync(conn, new WorldSelectCharacter
        {
            CharId = charId,
            MachineId = _opt.MachineId,
            Flag = 1,
        }, ct).ConfigureAwait(false);

        var chunks = 0;
        var done = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!done && DateTime.UtcNow < deadline)
        {
            var f = await conn.ReadFrameAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (f == null) break;
            Emit(false, f.Ncid, f.Payload, world: true);
            switch (f.Ncid)
            {
                case 69:
                    var d = Parse<CharacterDataChunk>(f.Payload);
                    chunks++;
                    Trace($"[session] char data chunk {d.ChunkIndex}/{d.ChunkTotal} ({d.Blob.Length}B)");
                    if (d.ChunkTotal > 0 && d.ChunkIndex >= d.ChunkTotal) done = true;
                    break;
                case 37:
                    // Le serveur C# anticipe le ticket zone (le natif attend
                    // le NCID 26 du client) — on le garde sous le coude et
                    // la sélection est réputée terminée.
                    CacheZoneTicket(Parse<ZoneTicket>(f.Payload));
                    done = true;
                    break;
            }
        }
        return chunks;
    }

    /// <summary>Choix de zone (NCID 26 "3966") → attend le ticket zone
    /// (NCID 37). Envoie ensuite les requêtes QoL du client natif
    /// (17/25/13/10 — ordre de la capture).</summary>
    public async Task<ZoneTicket> GetZoneTicketAsync(string? zoneName = null, CancellationToken ct = default)
    {
        var conn = RequireWorld();
        await SendAsync(conn, new WorldSelectZone
        {
            Zone = zoneName ?? _opt.ZoneName,
            Flag = 0,
        }, ct).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (ZoneTicketBytes == null && DateTime.UtcNow < deadline)
        {
            var f = await conn.ReadFrameAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (f == null) break;
            Emit(false, f.Ncid, f.Payload, world: true);
            if (f.Ncid == 37)
                CacheZoneTicket(Parse<ZoneTicket>(f.Payload));
        }
        if (ZoneTicketBytes == null)
            throw new TimeoutException("pas de ticket zone (NCID 37)");

        // Requêtes post-sélection du client natif (mitm lignes 30-33).
        await SendAsync(conn, new WorldItemMallReceivableQuery(), ct).ConfigureAwait(false);
        await SendAsync(conn, new WorldItemMallPointInfoQuery(), ct).ConfigureAwait(false);
        await SendAsync(conn, new WorldQueryFriendInfo(), ct).ConfigureAwait(false);
        await SendAsync(conn, new WorldQueryFamilyInfo(), ct).ConfigureAwait(false);

        // Les réponses éventuelles (57/60/64/78…) sont loguées via un
        // drain court — non bloquant pour l'entrée en zone.
        foreach (var f in await conn.DrainAsync(TimeSpan.FromMilliseconds(800), ct).ConfigureAwait(false))
            Emit(false, f.Ncid, f.Payload, world: true);

        var result = new ZoneTicket { Ticket = ZoneTicketBytes };
        return result;
    }

    /// <summary>Met en cache un ticket zone reçu (NCID 37).</summary>
    private void CacheZoneTicket(ZoneTicket ticket)
    {
        ZoneTicketBytes = ticket.Ticket;
        ZoneEndPoint = ticket.ZoneEndPoint();
        if (ZoneEndPoint.port == 0 || ZoneEndPoint.ip == "0.0.0.0")
            ZoneEndPoint = (_opt.Host, (ushort)_opt.ZonePort);
        Trace($"[session] ticket zone → {ZoneEndPoint.ip}:{ZoneEndPoint.port}");
    }

    // ---------------------------------------------------------------
    // 3) Zone :5201 — UseTicket, hello, burst d'entrée
    // ---------------------------------------------------------------

    /// <summary>Connexion au ZoneServer + envoi UseTicket (NCID 0).</summary>
    public async Task<EdenConnection> ZoneConnectAsync(CancellationToken ct = default)
    {
        if (ZoneTicketBytes == null)
            throw new InvalidOperationException("GetZoneTicketAsync d'abord");
        _zone = NewConnection();
        await _zone.ConnectAsync(ZoneEndPoint.ip, ZoneEndPoint.port, ct).ConfigureAwait(false);
        Trace($"[session] handshake zone OK {ZoneEndPoint.ip}:{ZoneEndPoint.port}");
        await SendAsync(_zone, new ZoneUseTicket { Ticket = ZoneTicketBytes }, ct)
            .ConfigureAwait(false);
        return _zone;
    }

    /// <summary>
    /// Burst d'entrée zone — ordre exact de la capture zsniff (dernière
    /// session @18:26) : attend ClientHello 473 puis envoie les requêtes
    /// EnterNode*/Query*, SearchOpt, PlayerAction, …, MigrationDone (4),
    /// SetMissionTrace (186), PlayerWorkCheckData (172).
    /// </summary>
    public async Task ZoneEntryAsync(CancellationToken ct = default)
    {
        var conn = RequireZone();
        var hello = await WaitForAsync<ZoneClientHello>(conn, 473, ct).ConfigureAwait(false);
        Trace($"[session] zone hello {hello.Value}");

        // Ordre capture : 57,26,24,53,110,113,195,243,256,133,25 puis
        // 104,14,164,62,50,156,152,162,166,165,181,204,213,558,223,
        // 258,4,186,172.
        NetMessage[] burst =
        {
            ZoneEmptyRequest.Echo,
            ZoneEmptyRequest.EnterNodePlayerData,
            ZoneEmptyRequest.EnterNodeEquipAndSpell,
            ZoneEmptyRequest.EnterNodeShortcut,
            ZoneEmptyRequest.EnterNodeElfInfo,
            ZoneEmptyRequest.EnterNodeElfContainer,
            ZoneEmptyRequest.EnterNodePlayerRoom,
            ZoneEmptyRequest.EnterNodeFastDress,
            ZoneEmptyRequest.EnterNodeStarWay,
            ZoneEmptyRequest.EnterNodePlayerCard,
            ZoneEmptyRequest.EnterNodeInventory,
            new ZoneSearchOpt(),                                        // 104
            new ZonePlayerAction { Action = 8, Param1 = -1, Param2 = 827, Param3 = 0 }, // 14
            new ZoneUpdateBotData(),                                    // 164
            new ZoneGetStorageData(),                                   // 62
            ZoneEmptyRequest.QueryMissionStates,                        // 50
            ZoneEmptyRequest.QueryLoverInfo,                            // 156
            ZoneEmptyRequest.QueryCollection,                           // 152
            ZoneEmptyRequest.QueryBattlefieldData,                      // 162
            ZoneEmptyRequest.QueryClassList,                            // 166
            ZoneEmptyRequest.QueryClosets,                              // 165
            ZoneEmptyRequest.QueryLicense,                              // 181
            ZoneEmptyRequest.QueryDestinyRoad,                          // 204
            ZoneEmptyRequest.QueryLifeSkill,                            // 213
            new ZoneQueryRecommendActivities(),                         // 558
            ZoneEmptyRequest.QueryFortuneBagCounter,                    // 223
            new ZoneDailyAwardOp { Field2 = 2 },                        // 258
            new ZoneMigrationDone(),                                    // 4
            BuildMissionTrace(),                                        // 186
            ZoneEmptyRequest.PlayerWorkCheckData,                       // 172
        };
        foreach (var msg in burst)
        {
            await SendAsync(conn, msg, ct).ConfigureAwait(false);
            await Task.Delay(30, ct).ConfigureAwait(false); // pacing ~capture
        }
        Trace("[session] burst d'entrée envoyé (MigrationDone inclus)");
    }

    private ZoneSetMissionTrace BuildMissionTrace()
    {
        var msg = new ZoneSetMissionTrace();
        if (_opt.MissionTraceIds != null)
            msg.MissionIds.AddRange(_opt.MissionTraceIds);
        return msg;
    }

    /// <summary>Écoute passive : log toutes les trames zone pendant
    /// <paramref name="seconds"/> (dispatch par défaut = raw).</summary>
    public async Task<Dictionary<int, int>> ListenZoneAsync(
        double seconds, CancellationToken ct = default)
    {
        var conn = RequireZone();
        var counts = new Dictionary<int, int>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var f = await conn.ReadFrameAsync(deadline - DateTime.UtcNow).ConfigureAwait(false);
            if (f == null) break;
            Emit(false, f.Ncid, f.Payload);
            counts[f.Ncid] = counts.GetValueOrDefault(f.Ncid) + 1;
        }
        return counts;
    }

    /// <summary>Enchaîne toute la séquence login → entrée zone.</summary>
    public async Task<EdenConnection> RunFullAsync(CancellationToken ct = default)
    {
        await ConnectGatewayAsync(ct).ConfigureAwait(false);
        var worlds = await LoginAsync(ct).ConfigureAwait(false);
        var worldId = _opt.WorldId
            ?? (worlds.LastWorldId != 0 ? worlds.LastWorldId
                : worlds.Worlds.Count > 0 ? worlds.Worlds[0].Id : (short)0);
        await SelectWorldAsync(worldId, ct).ConfigureAwait(false);
        await WorldLoginAsync(ct).ConfigureAwait(false);
        await ReceiveCharacterListAsync(ct).ConfigureAwait(false);
        var charId = _opt.CharId
            ?? (Characters.Count > 0 ? (uint)Characters[0].Id
                : throw new InvalidOperationException("aucun personnage"));
        Trace($"[session] perso choisi #{charId}");
        await SelectCharacterAsync(charId, ct).ConfigureAwait(false);
        await GetZoneTicketAsync(ct: ct).ConfigureAwait(false);
        await ZoneConnectAsync(ct).ConfigureAwait(false);
        await ZoneEntryAsync(ct).ConfigureAwait(false);
        return RequireZone();
    }

    /// <summary>
    /// API haut niveau pour la scène de jeu : enchaîne la séquence
    /// complète login → world → zone et laisse la connexion zone
    /// active (<see cref="Zone"/>). <paramref name="charId"/> null →
    /// premier personnage de la liste. Les autres paramètres
    /// surchargent les options si fournis.
    /// Après retour, appeler <see cref="ZoneListenLoopAsync"/> pour
    /// consommer le flux (les handlers <see cref="OnZone"/> sont
    /// invoqués sur chaque trame reçue).
    /// </summary>
    public async Task<EdenConnection> EnterZoneAsync(
        string? host = null, string? account = null, string? password = null,
        uint? charId = null, short? worldId = null,
        CancellationToken ct = default)
    {
        if (host != null) _opt.Host = host;
        if (account != null) _opt.Account = account;
        if (password != null) _opt.Password = password;
        if (charId.HasValue) _opt.CharId = charId;
        if (worldId.HasValue) _opt.WorldId = worldId;
        return await RunFullAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Envoie un message sur la connexion zone
    /// (après <see cref="ZoneEntryAsync"/>).</summary>
    public Task SendZoneAsync(NetMessage msg, CancellationToken ct = default)
        => SendAsync(RequireZone(), msg, ct);

    /// <summary>Envoie un message sur la connexion monde
    /// (après <see cref="WorldLoginAsync"/>).</summary>
    public Task SendWorldAsync(NetMessage msg, CancellationToken ct = default)
        => SendAsync(RequireWorld(), msg, ct);

    /// <summary>Connexion monde active (null avant WorldLoginAsync).</summary>
    public EdenConnection? World => _world;
    /// <summary>Connexion login active.</summary>
    public EdenConnection? Login => _login;

    /// <summary>Liste des mondes de la dernière authentification.</summary>
    public WorldList? LastWorldList { get; private set; }

    /// <summary>Ferme la connexion monde (retour à la sélection de
    /// channel/perso) — la connexion login reste ouverte.</summary>
    public async Task DisconnectWorldAsync()
    {
        if (_world != null)
        {
            try { await _world.DisposeAsync().ConfigureAwait(false); }
            catch { /* best effort */ }
            _world = null;
        }
        Characters.Clear();
        WorldTicketBytes = null;
        ZoneTicketBytes = null;
        SelectedCharId = 0;
    }

    /// <summary>Ferme toutes les connexions (login, monde, zone).</summary>
    public async Task DisconnectAllAsync()
    {
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _keepAliveCts = null;
        await DisconnectWorldAsync().ConfigureAwait(false);
        if (_zone != null)
        {
            try { await _zone.DisposeAsync().ConfigureAwait(false); }
            catch { }
            _zone = null;
        }
        if (_login != null)
        {
            try { await _login.DisposeAsync().ConfigureAwait(false); }
            catch { }
            _login = null;
        }
    }

    /// <summary>
    /// Boucle d'écoute zone permanente : lit les trames et dispatche
    /// via <see cref="OnZone"/> + <see cref="Frame"/> jusqu'à
    /// annulation ou fermeture. Retourne le compte par NCID.
    /// </summary>
    public async Task<Dictionary<int, int>> ZoneListenLoopAsync(
        CancellationToken ct = default)
    {
        var conn = RequireZone();
        var counts = new Dictionary<int, int>();
        try
        {
            while (await conn.ReadFrameAsync(ct).ConfigureAwait(false)
                is { } f)
            {
                Emit(false, f.Ncid, f.Payload);
                // Audit phase 6 : un NCID zone sans handler est
                // logué une fois par session — ni tu, ni spam.
                if (!HasHandlers(_zoneDispatch, f.Ncid) &&
                    _unhandledLogged.Add(f.Ncid))
                    Trace($"[ncid] {f.Ncid} non géré " +
                          $"({f.Payload.Length} o)");
                counts[f.Ncid] = counts.GetValueOrDefault(f.Ncid) + 1;
            }
        }
        catch (OperationCanceledException) { }
        if (!ct.IsCancellationRequested)
        {
            LastDisconnectReason = "Disconnected from server.";
            Disconnected?.Invoke(LastDisconnectReason);
        }
        Trace("[session] boucle zone terminée");
        return counts;
    }

    /// <summary>
    /// Boucle d'écoute <b>monde</b> permanente : la connexion :5200
    /// reste ouverte pendant le jeu (push social/guilde + item mall —
    /// le serveur pousse 62/63/64/79 hors du flux zone). Dispatche via
    /// <see cref="OnWorld"/> + <see cref="Frame"/> jusqu'à annulation
    /// ou fermeture. UN SEUL lecteur : à lancer en parallèle de
    /// <see cref="ZoneListenLoopAsync"/> (GameScene), jamais deux fois.
    /// </summary>
    public async Task<Dictionary<int, int>> WorldListenLoopAsync(
        CancellationToken ct = default)
    {
        var conn = RequireWorld();
        var counts = new Dictionary<int, int>();
        try
        {
            while (await conn.ReadFrameAsync(ct).ConfigureAwait(false)
                is { } f)
            {
                Emit(false, f.Ncid, f.Payload, world: true);
                if (!HasHandlers(_worldDispatch, f.Ncid) &&
                    _unhandledLogged.Add(-f.Ncid)) // clé négative = monde
                    Trace($"[ncid monde] {f.Ncid} non géré " +
                          $"({f.Payload.Length} o)");
                counts[f.Ncid] = counts.GetValueOrDefault(f.Ncid) + 1;
            }
        }
        catch (OperationCanceledException) { }
        Trace("[session] boucle monde terminée");
        return counts;
    }

    // ---------------------------------------------------------------

    private static T Parse<T>(byte[] payload) where T : NetMessage, new()
    {
        var msg = new T();
        var r = new BinReader(payload);
        r.ReadUShort(); // skip NCID
        msg.Deserialize(r);
        return msg;
    }

    /// <summary>NCID 30 — le perso a-t-il un mot de passe ? → flag
    /// (réponse 75 [i32]).</summary>
    public async Task<int> CheckCharPasswordAsync(
        int charId, CancellationToken ct = default)
    {
        var conn = RequireWorld();
        await SendAsync(conn, new WorldCheckCharPassword
            { CharId = charId }, ct).ConfigureAwait(false);
        var r = await WaitForAsync<WorldCheckCharPasswordResult>(
            conn, 75, ct, world: true).ConfigureAwait(false);
        return r.Value;
    }

    /// <summary>NCID 31 — op mot de passe perso → code serveur
    /// (réponse 76 [s16]). op 0 = vérifie (0 ok / -1 échec),
    /// op 3 = définit (3 ; <paramref name="password"/> vide = retire).</summary>
    public async Task<short> CharPasswordOpAsync(
        int charId, short op, string password,
        CancellationToken ct = default)
    {
        var conn = RequireWorld();
        await SendAsync(conn, new WorldCharPassword
            { CharId = charId, Op = op, Password = password }, ct)
            .ConfigureAwait(false);
        var r = await WaitForAsync<WorldCharPasswordResult>(
            conn, 76, ct, world: true).ConfigureAwait(false);
        return r.Value;
    }

    /// <summary>Lit des trames jusqu'à celle du NCID voulu — les autres
    /// sont émises au log mais ignorées.</summary>
    private async Task<T> WaitForAsync<T>(EdenConnection conn, int ncid,
        CancellationToken ct, bool world = false)
        where T : NetMessage, new()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var f = await conn.ReadFrameAsync(ct).ConfigureAwait(false)
                ?? throw new EndOfStreamException($"attente NCID {ncid} : connexion fermée");
            Emit(false, f.Ncid, f.Payload, world);
            if (f.Ncid == ncid) return Parse<T>(f.Payload);
        }
        throw new TimeoutException($"attente NCID {ncid} expirée");
    }

    private EdenConnection RequireLogin()
        => _login ?? throw new InvalidOperationException("ConnectGatewayAsync d'abord");
    private EdenConnection RequireWorld()
        => _world ?? throw new InvalidOperationException("WorldLoginAsync d'abord");
    private EdenConnection RequireZone()
        => _zone ?? throw new InvalidOperationException("ZoneConnectAsync d'abord");
}
