using System.Net;
using System.Net.Sockets;
using System.Text;
using Eden.Core.Networking;
using Eden.Core.Networking.Transport;
using Eden.Core.Logging;

namespace Eden.Zone.Links;

// -----------------------------------------------------------------------------
//  ZoneMissionLink — client du lien interne Zone->MissionServer natif (:7654).
//
//  Même transport lapis que le lien ZW : blob RSA du serveur, clé RC4 5 octets
//  chiffrée PKCS#1, puis trames [varint len][RC4(u16 ncid + payload)].
//
//  Wire ids (table MissionServer — extract_ncids) :
//    zone->mission :
//      53  IDReport [s16 zoneId]
//      55  NodeCreate [u16][s16][u32][i32]
//      142 MigrationDone [i32 charId][s16 nodeId]
//      127 ListDialogue [i32 charId][s16][u8][i32 npcId][i32][i32]
//      128 OnDialogueSelected [u16][i32][u16][s16][i32][s16][s16][i32][s16]
//      144 MobDieReport
//    mission->zone :
//      52  ReportID (demande d'identification)
//      56  ZoneServerListDialogue — liste d'options de dialogue NPC
//      57  ZoneServerReplyDialogue — corps d'option sélectionnée
//      65  UpdateNPCSymbols [i32][s16][u16][i32][s16] — marqueurs quête
//      66/59 TextOutputIndex/TextOutput
//      67  UpdateMissionState
//      113 NPCTalkCheck [u32]
// -----------------------------------------------------------------------------
public sealed class ZoneMissionLink : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly ushort _zoneId;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _tcp;
    private ITransport? _transport;
    private CancellationTokenSource? _cts;
    private Task? _recvTask;
    private int _disposed;

    /// <summary>Paquet mission -> zone à router vers un client.
    /// (ncid, corps complet sans ncid).</summary>
    public event Action<ushort, byte[]>? PacketReceived;

    public bool Connected => _transport != null && _tcp?.Connected == true;

    public ZoneMissionLink(string host, int port, ushort zoneId)
    {
        _host = host;
        _port = port;
        _zoneId = zoneId;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _recvTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                EdenLog.Write($"[Zone-M] lien mission perdu ({ex.Message}) — retry 3s");
            }
            await ResetAsync();
            try { await Task.Delay(3000, token); } catch { }
        }
    }

    private async Task ConnectAndServeAsync(CancellationToken token)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, token);

        // Handshake client du lien interne : TLS 1.3 (modern, défaut —
        // TLS chiffre sous la couche, le « plaintext post-handshake »
        // historique disparaît) ou blob RSA → clé RC4 chiffrée avec
        // encryptPayloads:false (legacy : la clé est envoyée mais le
        // lien ZM reste EN CLAIR — vérifié live).
        var transport = TransportFactory.ForLinkClient(tcp, _host,
            legacyEncryptPayloads: false);
        var ctx = new DirectTransportContext(transport);
        await transport.StartAsync(ctx, token);

        _tcp = tcp;
        _transport = transport;
        EdenLog.Write($"[Zone-M] Lien mission {_host}:{_port} établi");
        // Ne pas annoncer les nodes ici : la mission traite l'IDReport
        // après la connexion — un NodeCreate avant enregistrement de
        // l'ID zone échoue avec "zone (0) No this Server ID".

        // Trames [varint len en clair][payload] — TryTakePacket rend
        // le payload seul (len octets, u16 ncid inclus) une fois la
        // trame complète reçue.
        var buffer = new PacketReceiveBuffer();
        var tmp = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            var read = await transport.ReadAsync(tmp.AsMemory(), token);
            if (read <= 0) throw new EndOfStreamException();
            buffer.Append(tmp.AsSpan(0, read));
            try
            {
                while (buffer.TryTakePacket(out var packet))
                {
                    var ncid = BitConverter.ToUInt16(packet, 0); // u16 ncid
                    var body = packet[2..];                      // corps (len-2 B)
                    EdenLog.Write($"[Zone-M] <-- NCID={ncid} len={body.Length} " +
                        Convert.ToHexString(body[..Math.Min(body.Length, 48)]));
                    if (ncid == 16 || ncid == 52) // ReportID -> IDReport 53
                    {
                        await SendAsync(53,
                            BitConverter.GetBytes((short)_zoneId), token);
                        // Ordre natif : la mission enregistre l'ID de la zone
                        // en traitant l'IDReport — les NodeCreate ne sont
                        // acceptés qu'APRÈS ("Zone Server [id] connected").
                        Reconnected?.Invoke();
                    }
                    else
                        // Un handler qui jette ne doit pas tuer le lien —
                        // le paquet est loggé puis abandonné.
                        try { PacketReceived?.Invoke(ncid, body); }
                        catch (Exception ex)
                        { EdenLog.Write(
                            $"[Zone-M] handler ncid={ncid} : {ex.Message}"); }
                }
            }
            catch (InvalidDataException ide)
            {
                // Trame trop courte pour contenir un ncid (len<2) — le
                // buffer ne permet pas de sauter des octets arbitraires,
                // on conserve le log puis on abandonne le lien
                // (RunLoopAsync retente après 3 s).
                EdenLog.Write($"[Zone-M] {ide.Message} " +
                    $"({buffer.Pending}B en attente) — abandon du lien");
                throw;
            }
        }
    }

    // ------------------------------------------------------------- envois --

    /// <summary>Paquet émis quand le lien mission vient d'être (ré)établi —
    /// la zone doit alors rejouer IDReport + NodeCreate (l'état côté
    /// mission est perdu à chaque reconnexion).</summary>
    public event Action? Reconnected;

    /// <summary>NodeCreate (55) : la zone déclare un node à la mission.
    /// Wire (désasm CNC_...NodeCreate::Serialize zone + CNE_...Deserialize
    /// mission) : [u16 nodeId][s16 nodeType][u32 charUid][i32 parent] = 12B.
    /// Forme globale (CNodeGroup::CreateGlobalNode, au démarrage pour les
    /// nodes de type 1-3) : charUid=0, parent=-1.
    /// Forme par-perso (CZoneServer::OpenNode->CreateNode, nodes
    /// instanciés) : charUid=char->0x34, parent=SelectNodeGroup.out.
    /// La mission résout "quel serveur détient l'exécuteur" via
    /// player_characters.node_id (DB) -> instance de node créée ici.</summary>
    public Task SendNodeCreateAsync(int nodeId, int nodeType, uint charUid,
        int parent, CancellationToken token = default)
        => SendAsync(55, Concat(
            BitConverter.GetBytes((ushort)nodeId),
            BitConverter.GetBytes((short)nodeType),
            BitConverter.GetBytes(charUid),
            BitConverter.GetBytes(parent)), token);

    /// <summary>MigrationDone (142) : perso arrivé sur un node.
    /// Wire (Deserialize mission @0x813e1f0) : [i32 charUid] = 4B seul.
    /// Execute : GetExecutionerData(charUid) -> MigrationDone -> pousse
    /// node_id (lu en DB) dans player_node_times. N'enregistre PAS la
    /// zone : le routage passe par NodeCreate + player_characters.node_id.</summary>
    public Task SendMigrationDoneAsync(uint charId, CancellationToken token = default)
        => SendAsync(142, BitConverter.GetBytes(charId), token);

    /// <summary>ListDialogue (127) : demande la liste d'options d'un NPC
    /// pour un perso — la mission répond 56.
    /// Wire (Deserialize mission @0x813de40, 6 champs dans l'ordre) :
    ///   [i32 charUid][s16 nodeId][u8][i32 npcUid][i32][i32] = 19B
    /// charUid = char+0x34 (uid perso) ; nodeId = node->data+0x4 ;
    /// npcUid = monster->0x324->0x4 = le template id du NPC.
    /// Le dernier i32 manquant provoquait un read-past-end côté mission.</summary>
    public Task SendListDialogueAsync(uint charUid, int nodeId, int npcUid,
        CancellationToken token = default) =>
        SendListDialogueAsync(charUid, nodeId, npcUid, 1, token);

    /// <summary>Variante avec type explicite — le natif envoie type=8
    /// pour les livres/parchemins (<c>CEC_ItemTriggerEvent::Execute</c>
    /// @0x85327xx : type u8=8, npcUid = texteDebut&lt;&lt;16|nbPages).</summary>
    public Task SendListDialogueAsync(uint charUid, int nodeId, int npcUid,
        byte type, CancellationToken token = default) =>
        SendAsync(127, Concat(
            BitConverter.GetBytes(charUid),
            BitConverter.GetBytes((short)nodeId),
            new byte[] { type },
            BitConverter.GetBytes(npcUid),
            BitConverter.GetBytes(0),
            BitConverter.GetBytes(0)), token);

    /// <summary>OnDialogueSelected (128) : choix d'option (réponse 57).
    /// Wire (désasm CNE_...OnDialogueSelected::Deserialize + ctor zone
    /// CNE_CZ_ZoneServerReplyDialog::Execute : le client n'envoie qu'un
    /// s16 index ; la zone pioche dans ses CMissionDialog stockées les
    /// champs {i32 id, u16 a, u16 b} et les émet ici) :
    /// [u16 n1][n1 × i32][u16 n2][n2 × s16][i32 dlgId][s16 a][s16 b]
    /// [i32 charUid][s16 nodeId].
    /// Les deux vecteurs sont vides pour un choix simple.</summary>
    public Task SendDialogueSelectedAsync(uint charUid, int nodeId,
        int dialogId, int flagA, int flagB,
        CancellationToken token = default) =>
        SendAsync(128, Concat(
            BitConverter.GetBytes((ushort)0),              // n1 = 0 -> pas de i32[]
            BitConverter.GetBytes((ushort)0),              // n2 = 0 -> pas de s16[]
            BitConverter.GetBytes(dialogId),               // i32 : id du CMissionDialog choisi
            BitConverter.GetBytes((short)flagA),           // s16 : champ u16 @+4
            BitConverter.GetBytes((short)flagB),           // s16 : champ u16 @+6
            BitConverter.GetBytes(charUid),                // i32 : uid perso
            BitConverter.GetBytes((short)nodeId)), token); // s16 : node courant

    /// <summary>AgentDBACommand (21 zone->mission) : la zone relaie une
    /// commande texte exécutée par la couche DBA de la mission
    /// (IDBACommand::Execute @0x8128770 dans MissionServer).
    /// Wire (Serialize zone @0x807dc40 + construction dans
    /// MailQuery::Execute @0x8265105) :
    ///   [i32 charUid][str cmdText][str "command"].
    /// cmdText suit les formats .rodata de la zone, ex :
    ///   "MailQuery page_number = %hd accept_only_friend_mail = %hd"
    ///   "MailRead mail_id = %d" / "MailDelete mail_id = %d"
    ///   "MailGetItem mail_id = %d inv_loc = %hd con_index = %hd b_return = 0 merge = 0"
    ///   "MailGetGold mail_id = %d" / "MailReturn mail_id = %d"
    ///   "MissionServiceOp %hd %hd %hd"  (CMissionListOp::DoOperation
    ///   @0x856e7f0 -> CDBAgent::Command @0x83421b0).</summary>
    public Task SendAgentDbaCommandAsync(uint charUid, string command,
        CancellationToken token = default)
    {
        // Big5 (comme les strings réseau) : BroadCastScreenMessage
        // transporte des noms d'items chinois — ASCII les réduirait
        // en '?' (Big5 ⊃ ASCII, les verbes restent inchangés).
        var cmd = Eden.Core.Data.EdenEncoding.Big5.GetBytes(command);
        return SendAsync(21, Concat(
            BitConverter.GetBytes(charUid),                    // i32 : charUid (+0x4)
            BitConverter.GetBytes((ushort)cmd.Length), cmd,    // str : texte (+0x8)
            BitConverter.GetBytes((ushort)7),
            Encoding.ASCII.GetBytes("command")), token);       // str : "command" (+0xc)
    }

    /// <summary>MissionServerMessageWrite (802) : écriture panneau
    /// d'affichage. Wire (CNE_CZ_ZoneServerMessageWrite::Execute
    /// @0x8269960 — piVar4[1]=char+0x34, +2/+3 = les deux strings du
    /// CNE) : [i32 charUid][str titre][str contenu].
    /// La mission répond 740 → la zone consomme la carte 0x7ceb et
    /// purge le post (feature native bridée).</summary>
    public Task SendMessageWriteAsync(uint charUid, string title,
        string content, CancellationToken token = default)
    {
        var t = Eden.Core.Data.EdenEncoding.Big5.GetBytes(title);
        var c = Eden.Core.Data.EdenEncoding.Big5.GetBytes(content);
        return SendAsync(802, Concat(
            BitConverter.GetBytes(charUid),                    // i32 : +4 charUid
            BitConverter.GetBytes((ushort)t.Length), t,        // str : +0x14 titre
            BitConverter.GetBytes((ushort)c.Length), c),       // str : +0x18 contenu
            token);
    }

    /// <summary>MissionServerUpdatePlayerCard (703) : persistance
    /// carte joueur — wire natif (PlayerCardUpdate @0x8103xxx :
    /// [i32 charUid][12×s16 stats][str intro][u8 is_public] ; la
    /// mission UPDATE player_card via GetPlayerCardUpdateSQLCmd).
    /// Ordre des 12 stats = colonnes player_card : gender, blood_type,
    /// birth_month, birth_day, player_sign, region, mood, like_1..3,
    /// hate_1, hate_2.</summary>
    public Task SendUpdatePlayerCardAsync(uint charUid, short[] stats,
        string intro, bool isPublic, CancellationToken token = default)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(charUid));       // i32 : +4 charUid
        foreach (var st in stats)                            // 12×s16 : stats
            body.AddRange(BitConverter.GetBytes(st));
        var e = Eden.Core.Data.EdenEncoding.Big5.GetBytes(intro);
        body.AddRange(BitConverter.GetBytes((ushort)e.Length));
        body.AddRange(e);                                    // str : introduct
        body.Add((byte)(isPublic ? 1 : 0));                  // u8  : is_public
        return SendAsync(703, body.ToArray(), token);
    }

    /// <summary>MissionServerSetRecommendActivityTracing (835) :
    /// le client bascule le suivi d'une activité (226 CZ → relais
    /// zone @Execute : [i32 charUid][u16 id][u16 flag]).
    /// Mission : UPDATE recommend_activities.is_tracing.</summary>
    public Task SendRecommendActivityTracingAsync(uint charUid,
        ushort activityId, ushort tracing, CancellationToken token = default) =>
        SendAsync(835, Concat(
            BitConverter.GetBytes(charUid),          // i32 : charUid
            BitConverter.GetBytes(activityId),       // u16 : recommend_id
            BitConverter.GetBytes(tracing)),         // u16 : 0/1
            token);

    /// <summary>MissionServerRefreshRecommendActivities (831) : la zone
    /// pousse l'état complet des activités d'un perso (reset hebdo /
    /// changement) — wire natif Deserialize mission @0x8141c30 :
    /// [i32 charUid][i32 ts][u16 n][n×(u16,u32,u8)][u16 m][m×u16].
    /// Mission : SaveRecommendActivities → DELETE+INSERT batch +
    /// UPDATE recommend_activities_time.</summary>
    public Task SendRefreshRecommendActivitiesAsync(uint charUid, int ts,
        IReadOnlyList<(ushort Id, uint Count, byte Flag)> entries,
        IReadOnlyList<ushort> tracing, CancellationToken token = default)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(charUid));       // i32 : charUid
        body.AddRange(BitConverter.GetBytes(ts));            // i32 : timestamp
        body.AddRange(BitConverter.GetBytes((ushort)entries.Count)); // u16 n
        foreach (var (id, count, flag) in entries)
        {
            body.AddRange(BitConverter.GetBytes(id));        // u16 : recommend_id
            body.AddRange(BitConverter.GetBytes(count));     // u32 : progression
            body.Add(flag);                                  // u8  : flag
        }
        body.AddRange(BitConverter.GetBytes((ushort)tracing.Count)); // u16 m
        foreach (var t in tracing)
            body.AddRange(BitConverter.GetBytes(t));         // u16 : id tracé
        return SendAsync(831, body.ToArray(), token);
    }

    /// <summary>MobDieReport (144) : un monstre est mort (kill credit).
    /// Wire (Serialize zone @0x82d1820 + Deserialize mission @0x8140bc0) :
    /// [u16 mobId][s16][s16][f32 x][f32 z][u16 count][count × i32].
    /// Execute : ScriptEntry par élément + GetExecutionerData(i32) — le
    /// vecteur final = les uids des tueurs (crédit de kill quête).</summary>
    public Task SendMobDieAsync(uint charId, int mobId, float x, float z,
        CancellationToken token = default) =>
        SendAsync(144, Concat(
            BitConverter.GetBytes((ushort)mobId),          // u16 : template mob
            BitConverter.GetBytes((short)0),               // s16
            BitConverter.GetBytes((short)0),               // s16
            BitConverter.GetBytes(x),                      // f32 : position x
            BitConverter.GetBytes(z),                      // f32 : position z
            BitConverter.GetBytes((ushort)1),              // u16 : 1 exécuteur
            BitConverter.GetBytes(charId)), token);        // i32 : uid du tueur

    /// <summary>QuestItemGot (450 — code interne (hors table native : les NCIDs ZMis natifs 145-147 = MailSend/SendSystemMail/MessageWrite) zone→mission, hors
    /// protocole natif) : un item de quête vient d'entrer dans
    /// l'inventaire du joueur (drop MonsterTreasureGot roulé zone-side).
    /// Wire maison : [i32 charUid][s16 missionId][u16 itemId] —
    /// la mission incrémente la progression et notifie le client.</summary>
    public Task SendQuestItemGotAsync(uint charId, int missionId,
        int itemId, CancellationToken token = default) =>
        SendAsync(450, Concat(
            BitConverter.GetBytes(charId),               // i32 : uid perso
            BitConverter.GetBytes((short)missionId),     // s16 : mission
            BitConverter.GetBytes((ushort)itemId)),      // u16 : item obtenu
            token);

    /// <summary>QuestItemChanged (451 — interne zone→mission) : un item
    /// a gagné/perdu des exemplaires dans l'inventaire du joueur —
    /// équivalent de l'insert/delete dans le miroir d'inventaire de
    /// l'exécuteur natif (qui déclenche UpdateQuestItemStatus @0x8096b10).
    /// Wire maison : [i32 charUid][u16 itemId]. La mission recompte en
    /// base (CountItemAsync) — pas besoin de delta.</summary>
    public Task SendQuestItemChangedAsync(uint charId, int itemId,
        CancellationToken token = default) =>
        SendAsync(451, Concat(
            BitConverter.GetBytes(charId),               // i32 : uid perso
            BitConverter.GetBytes((ushort)itemId)),      // u16 : item touché
            token);

    /// <summary>AreaTravelNotify (452 — interne zone→mission) : le joueur
    /// a franchi une aire déclencheur keyId ∈ [51,59] (CheckNodeExit →
    /// TriggerArea natif @0x8241960, trigger id = node&lt;&lt;16|key).
    /// Wire maison : [i32 charUid][s16 nodeId][s16 keyId] — la mission
    /// crédite les objectifs AreaTravel code node*100+keyId.</summary>
    public Task SendAreaTravelAsync(uint charId, int nodeId, int keyId,
        CancellationToken token = default) =>
        SendAsync(452, Concat(
            BitConverter.GetBytes(charId),               // i32 : uid perso
            BitConverter.GetBytes((short)nodeId),        // s16 : node courant
            BitConverter.GetBytes((short)keyId)),        // s16 : clé d'aire
            token);

    /// <summary>CloseMissionBrief (141) : le client ferme le panneau de
    /// quête — la zone relaie à la mission qui nettoie l'opération
    /// pendante. Wire (CNC_ZMis_MissionServerCloseMissionBrief::
    /// Serialize @0x8243520 + Execute @0x8242ff0 : champ +0x4 =
    /// char+0x34) : [i32 charUid] seul, 4B.</summary>
    public Task SendCloseMissionBriefAsync(uint charUid,
        CancellationToken token = default) =>
        SendAsync(141, BitConverter.GetBytes(charUid), token);

    /// <summary>GiveUpMission (143) : abandon de quête — relayé à la
    /// mission qui détient l'état de quête. Wire
    /// (CNC_ZMis_MissionServerGiveUpMission::Serialize @0x825d7e0 +
    /// Execute @0x825d240 : +4=charUid, +8=s16 nodeId
    /// (char+0x1c→0x28→0x4), +0xa=s16 missionId) :
    /// [i32 charUid][s16 nodeId][s16 missionId] = 8B.</summary>
    public Task SendGiveUpMissionAsync(uint charUid, int nodeId,
        int missionId, CancellationToken token = default) =>
        SendAsync(143, Concat(
            BitConverter.GetBytes(charUid),                // i32 : uid perso
            BitConverter.GetBytes((short)nodeId),          // s16 : node courant
            BitConverter.GetBytes((short)missionId)), token); // s16 : quête

    public async Task SendAsync(ushort ncid, byte[] payload,
        CancellationToken token = default)
    {
        var transport = _transport;
        if (transport == null) return;
        var body = new byte[2 + payload.Length];
        BitConverter.GetBytes(ncid).CopyTo(body, 0);
        payload.CopyTo(body, 2);
        // [varint len][payload] — lien ZM en clair (marge 5B en tête).
        var frame = new byte[VarintCodec.MaxEncodedLength + body.Length];
        var hdrLen = VarintCodec.WriteVarint(frame, (uint)body.Length);
        body.CopyTo(frame, hdrLen);
        await _sendLock.WaitAsync(token);
        try
        {
            await transport.WriteAsync(
                frame.AsMemory(0, hdrLen + body.Length), token);
        }
        finally { _sendLock.Release(); }
    }

    private async Task ResetAsync()
    {
        if (_transport != null)
        {
            try { await _transport.DisposeAsync(); } catch { }
            _transport = null;
        }
        try { _tcp?.Dispose(); } catch { }
        _tcp = null;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var b = new byte[parts.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts?.Cancel();
        await ResetAsync();
        if (_recvTask != null) { try { await _recvTask; } catch { } }
        _cts?.Dispose();
    }
}
