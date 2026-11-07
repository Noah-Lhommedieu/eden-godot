using Eden.Core;
using Eden.Core.Networking.Messages.Generated;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Transport;
using Eden.Core.Logging;

namespace Eden.World;

// -----------------------------------------------------------------------------
//  ZoneLinkServer — lien interne World<->Zone (natif : port 5710).
//
//  Protocole ZW reversé (InitZoneWorldCommandTable, commun aux deux binaires) :
//    S>C NCID 16  vide                       World->Zone : demande d'identification
//    C>S NCID 0   [u16 zoneId]               Zone->World : IDReport
//    S>C NCID 26/27/28/29 blobs config       World->Zone : FortuneBag/Lottery/TextExCmd/StarWay
//    C>S NCID 4   [22B ticket]               Zone->World : CharacterArrived
//    S>C NCID 18  [u32 charId]               World->Zone : CharacterArrivedDest
//    C>S NCID 3   [...]                      Zone->World : NotifyEntry
//    C>S NCID 10  [...]                      Zone->World : Logout
//
//  La zone se connecte, le monde pousse NCID 16 puis répond
//  CharacterArrivedDest à l'arrivée du joueur — sans cette réponse le client
//  reste bloqué sur l'écran de chargement (la zone valide le ticket mais attend
//  l'accusé du monde avant de charger les données joueur via son lien DBA).
// -----------------------------------------------------------------------------
public sealed class ZoneLinkServer
{
    public const ushort NcidZoneServerReportId = 16;      // W->Z (vide)
    public const ushort NcidWorldServerIdReport = 0;      // Z->W [u16 zoneId]
    public const ushort NcidWorldServerNotifyEntry = 3;   // Z->W
    public const ushort NcidWorldServerCharacterArrived = 4;   // Z->W [ticket 22B]
    public const ushort NcidWorldServerResetTime = 5;     // Z->W
    public const ushort NcidWorldServerReturnToCharList = 6;   // Z->W
    public const ushort NcidWorldServerChannelMsg = 8;    // Z->W
    public const ushort NcidWorldServerLogout = 10;       // Z->W
    public const ushort NcidZoneServerCharacterArrivedDest = 18; // W->Z [u32 charId]
    public const ushort NcidZoneServerTextCommand = 23;   // W->Z [u16 len][ascii]
    public const ushort NcidZoneServerItemMallReceieveAttempt = 24; // W->Z livraison mall
    public const ushort NcidWorldServerItemMallReceieveAttempt = 12; // Z->W résultat livraison
    public const ushort NcidZoneServerUpdateFortuneBag = 26;  // W->Z
    public const ushort NcidZoneServerUpdateLottery = 27;     // W->Z
    public const ushort NcidZoneServerUpdateStarWay = 29;     // W->Z
    public const ushort NcidZoneServerChangeFamily = 31;      // W->Z (natif 154/645)
    public const ushort NcidZoneServerNewFamily = 32;         // W->Z (natif 155/646)
    public const ushort NcidZoneServerRemoveFamily = 33;      // W->Z (natif 156/647)
    public const ushort NcidWorldServerFamilyCreation = 34;   // Z->W (natif 143/634)
    // 35 W->Z FamilyDataUpdate — données famille non-membre
    // (natif : broadcast CNC_DBA_ClientChangeFamily mission→zones ;
    // chez nous [i32 familyId][s16 house_open] pour le gate
    // FamilyInstanceCreate/GetData côté zone).
    public const ushort NcidZoneServerFamilyDataUpdate = 35;  // W->Z
    // 30 Z->W UpdateSpecialCoin — extension interne : le natif envoie
    // CNC_MW_WorldServerUpdateSpecialCoin mission→monde directement,
    // notre mission n'a pas de lien monde ; la zone relaie le corps
    // [i32 charId][u16 n][n×(s16 type, i32 total)] et le monde pousse
    // CNC_CW_ClientUpdateSpecialCoin (78) `[u16 n][n×(s16,i32)]`.
    public const ushort NcidWorldServerUpdateSpecialCoin = 30; // Z->W
    public const ushort NcidWorldServerTextCommand = 636; // Z->W [u16 len][str]

    private readonly TcpListener _listener;
    private readonly RSA? _rsa;
    private readonly Npgsql.NpgsqlDataSource _accountDb;
    private readonly Npgsql.NpgsqlDataSource? _gameDb;
    private readonly Eden.Core.Database.Repositories.ItemMallRepository _mall;

    // Session de la zone enregistrée (une seule zone dans le setup actuel).
    // Permet au monde de pousser des commandes texte (ex. `update_prestige`)
    // — comportement natif : le monde émet NCID 23 vers la zone quand le
    // client interroge son info famille.
    public static NetSession? ActiveZoneSession { get; private set; }

    // Registre charId -> session client monde. Rempli par Program.cs à
    // ServerLoginCharacter ; utilisé pour pousser ClientResetTime au client
    // quand la zone signale l'entrée (comportement natif de
    // CNE_ZW_WorldServerResetTime::Execute -> CNC_CW_ClientResetTime).
    public static ConcurrentDictionary<int, NetSession> ClientSessions { get; } = new();

    // Timestamp de démarrage du monde (CGlobalInfo::WorldStartTime natif).
    /// <summary>État famille monde — injecté par Program.cs, utilisé
    /// par le flux de création ZW (NCID 34, natif 143/634).</summary>
    public static Eden.Core.Game.Family.FamilyService? Families { get; set; }

    /// <summary>Diffuse l'état de ville d'une famille (house_open) à la
    /// zone (ZW 35) et à ses membres en ligne (fiche famille).</summary>
    public static async Task PushHouseOpenAsync(Eden.Core.Game.Family.Family fam)
    {
        var s = new Eden.Core.Networking.OutBinStream();
        s.WriteInt(fam.Id);
        s.WriteShort(fam.HouseOpen);
        if (ActiveZoneSession is { } z)
            await z.SendPacketAsync(NcidZoneServerFamilyDataUpdate, s.ToArray());
        foreach (var id in fam.Members.Keys.ToList())
            if (ClientSessions.TryGetValue(id, out var ms))
                await ms.SendPacketAsync(50,
                    Eden.Core.Game.Family.FamilyPackets.QueryFamilyInfo(fam, 0, 1));
    }

    /// <summary>Accès à la table native <c>prestige</c> — les commandes
    /// ZW <c>gain_prestige</c>/<c>set_prestige</c> y écrivent
    /// (équivalent des textes mission <c>GainExp Prestige</c>/
    /// <c>SetLevel Prestige</c>).</summary>
    public static Eden.Core.Database.Repositories.PrestigeRepository?
        Prestige { get; set; }

    public static int WorldStartTime { get; } =
        (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary><paramref name="rsa"/> : paire RSA du handshake legacy
    /// (null en mode modern — <see cref="TlsTransport"/> utilise le
    /// cert de <see cref="TlsCertificateProvider"/>).</summary>
    public ZoneLinkServer(int port, RSA? rsa, Npgsql.NpgsqlDataSource accountDb,
        Npgsql.NpgsqlDataSource? gameDb = null)
    {
        _rsa = rsa;
        _accountDb = accountDb;
        _gameDb = gameDb;
        _mall = new Eden.Core.Database.Repositories.ItemMallRepository(accountDb);
        if (gameDb != null)
            Prestige = new Eden.Core.Database.Repositories
                .PrestigeRepository(gameDb);
        _listener = new TcpListener(IPAddress.Any, port);
    }

    /// <summary>Relais prestige monde→zone — texte natif
    /// <c>update_prestige &lt;ch&gt; &lt;node&gt; &lt;id&gt; &lt;lv&gt;
    /// &lt;exp&gt;</c> (sprintf monde @0x0812c185, préfixe
    /// <c>"update_prestige "</c> string world @0x08482610) enveloppé
    /// dans <c>CNC_ZW_ZoneServerTextCommand</c> (NCID 23). La zone
    /// applique <c>CCharacter::UpdatePrestige</c> (miroir map+0x6c4) ;
    /// le node est ignoré par <c>Server_TC_UpdatePrestige</c>
    /// (@0x083b9930) — émis à 0.</summary>
    public static async Task PushPrestigeToZoneAsync(NetSession zone,
        int charId, short prestigeId, short level, int exp,
        CancellationToken token = default)
    {
        var text = System.Text.Encoding.ASCII.GetBytes(
            $"update_prestige {charId} 0 {prestigeId} {level} {exp}");
        var payload = new byte[2 + text.Length];
        BitConverter.GetBytes((ushort)text.Length).CopyTo(payload, 0); // u16 len
        text.CopyTo(payload, 2);                                        // str cmd
        await zone.SendPacketAsync(NcidZoneServerTextCommand, payload,
            token);
    }

    /// <summary>Émission prestige complète monde : NCID 60 au client
    /// (si en ligne) + <c>update_prestige</c> à la zone — équivalent
    /// du <c>Execute</c> natif @0x0812bff0 (relais MW→CW + ZW).</summary>
    public static async Task EmitPrestigeUpdateAsync(int charId,
        short prestigeId, short level, int exp,
        CancellationToken token = default)
    {
        if (ClientSessions.TryGetValue(charId, out var client))
            // 60 CNC_CW_ClientUpdatePrestigeData
            // [u16 n][n×(s16 id, s16 level, i32 exp)]
            // (Serialize natif monde @0x0812c8b0).
            await client.SendPacketAsync(60,
                Eden.Core.Game.Family.PrestigePackets.UpdatePrestigeData(
                    [(prestigeId, level, exp)]), token);
        if (ActiveZoneSession is { } zone)
            await PushPrestigeToZoneAsync(zone, charId, prestigeId,
                level, exp, token);
    }

    public async Task RunAsync(CancellationToken token)
    {
        _listener.Start();
        EdenLog.Write($"[ZW] Écoute du lien World<->Zone sur :{_listener.LocalEndpoint}");
        while (!token.IsCancellationRequested)
        {
            var tcp = await _listener.AcceptTcpClientAsync(token);
            _ = Task.Run(() => HandleZoneAsync(tcp), token);
        }
    }

    private async Task HandleZoneAsync(TcpClient tcp)
    {
        // Lien serveur↔serveur : jamais d'idle timeout (le natif ne
        // coupe pas la zone sur silence — sinon déco/repush FortuneBag
        // toutes les 300 s). Transport par EDEN_TRANSPORT : TLS 1.3
        // (modern, défaut) ou blob RSA + RC4 (legacy).
        await using var session = new NetSession(
            TransportFactory.ForAcceptedSocket(tcp, _rsa))
        {
            SkipIdleTimeout = true,
        };
        var zoneId = -1;
        try
        {
            // Côté accepteur : le serveur authentifie d'abord
            // (transport.StartAsync — TLS ou blob RSA selon le mode).
            if (!await session.StartTransportAsync())
            {
                EdenLog.Write($"[ZW] Handshake échoué : {session.CloseReason}");
                return;
            }
            EdenLog.Write($"[ZW] Zone connectée depuis {session.RemoteEndPoint} — crypto OK ({EdenConfig.Transport})");

            // Comportement natif : le monde pousse NCID 16 (vide) dès la crypto prête.
            await session.SendPacketAsync(NcidZoneServerReportId, []);

            while (true)
            {
                var packets = await session.ReadPacketsAsync();
                if (packets.Count == 0) break;
                foreach (var payload in packets)
                {
                    if (payload.Length < 2) continue;
                    var ncid = BitConverter.ToUInt16(payload, 0);
                    var body = payload.AsMemory(2);
                    switch (ncid)
                    {
                        case NcidWorldServerIdReport:
                            if (body.Length >= 2)
                            {
                                zoneId = BitConverter.ToUInt16(body.Span);
                                ActiveZoneSession = session;
                                EdenLog.Write($"[ZW] ZoneServer enregistré : id={zoneId}");
                                // Comportement natif (CGlobalInfo::UpdateZoneStarWayData,
                                // appelé depuis CNE_ZW_WorldServerIDReport::Execute) :
                                // le monde pousse la config StarWay à chaque zone qui
                                // s'enregistre — un paquet NCID 29 par ligne de la table
                                // edenfr_account.starway. Sans ces données, la zone ne répond
                                // rien à CNE_CZ_ZoneServerEnterNodeStarWay (client NCID
                                // 256) et le client reste bloqué en chargement.
                                _ = PushStarWayDataAsync(session);
                                // fortune_bag → ZW 26 (3 phases,
                                // FortuneUpdating natif).
                                _ = PushFortuneBagDataAsync(session);
                                // Claims item-mall orphelins (state=2
                                // sans verdict) : une zone qui se
                                // ré-enregistre a pu perdre des
                                // livraisons en vol → remise en file.
                                _ = RequeueOrphanedClaimsAsync();
                            }
                            break;

                        case NcidWorldServerCharacterArrived:
                            // body = ticket 22B : [u32 charId][u32 clientIp][u32 zoneIp][u16 port][u32 t1][u32 t2]
                            if (body.Length >= 4)
                            {
                                var charId = BitConverter.ToUInt32(body.Span);
                                EdenLog.Write($"[ZW] CharacterArrived : char=#{charId} (zone {zoneId})");
                                var reply = BitConverter.GetBytes(charId);
                                await session.SendPacketAsync(NcidZoneServerCharacterArrivedDest, reply);
                                EdenLog.Write($"[ZW] -> CharacterArrivedDest char=#{charId}");
                            }
                            break;

                        case NcidWorldServerResetTime:
                            // Z->W [u32 charId] : la zone signale que le perso
                            // est entré. Le natif répond CNC_CW_ClientResetTime
                            // (NCID 40) sur la connexion monde du client — le
                            // signal qui termine l'écran de chargement.
                            if (body.Length >= 4)
                            {
                                var rtChar = BitConverter.ToInt32(body.Span);
                                EdenLog.Write($"[ZW] ResetTime : char=#{rtChar}");
                                if (ClientSessions.TryGetValue(rtChar, out var client))
                                {
                                    _ = client.SendAsync(
                                        new CNC_CW_ClientResetTime
                                        { Field1 = WorldStartTime,
                                          Field2 = -1 });
                                    EdenLog.Write($"[ZW] -> ClientResetTime au client #{rtChar}");
                                }
                                else EdenLog.Write($"[ZW] char #{rtChar} sans session client");
                            }
                            break;

                        case NcidWorldServerNotifyEntry:
                            EdenLog.Write($"[ZW] NotifyEntry (zone {zoneId}) : {Convert.ToHexString(body.Span)}");
                            break;

                        case NcidWorldServerItemMallReceieveAttempt:
                            // Z->W NCID 12 : résultat de la livraison
                            // item mall (désasm zone
                            // CNE_ZW_ZoneServerItemMallReceieveAttempt::
                            // Execute @0x82ac7f0). Le monde finalise la
                            // claim puis envoie le 58 au client.
                            if (Eden.Core.Game.Mall.ItemMallPackets
                                    .ParseZwDeliveryResult(body.Span) is
                                { } delRes)
                                await HandleItemMallDeliveryResultAsync(delRes);
                            break;

                        case NcidWorldServerUpdateSpecialCoin:
                            // Z->W NCID 30 [i32 charId][u16 n]
                            // [n×(s16 type, i32 total)] — relayé au
                            // client en CNC_CW_ClientUpdateSpecialCoin
                            // (78) `[u16 n][n×(s16,i32)]` — Serialize
                            // natif @0x80ccb?? (world/decompiled.c).
                            if (body.Length >= 6)
                            {
                                var coinChar = BitConverter.ToInt32(body.Span);
                                if (ClientSessions.TryGetValue(coinChar,
                                        out var coinClient))
                                    await coinClient.SendPacketAsync(78,
                                        body.Slice(4).ToArray());
                            }
                            break;

                        case NcidWorldServerLogout:

                        case NcidWorldServerTextCommand:
                            // 636 CNC_ZW_WorldServerTextCommand — texte
                            // de commande zone→monde natif.
                            if (body.Length >= 2)
                            {
                                var tlen = BitConverter.ToUInt16(body.Span);
                                var text = tlen <= body.Length - 2
                                    ? System.Text.Encoding.ASCII.GetString(
                                        body.Slice(2, tlen).Span)
                                    : "";
                                if (await HandleZwTextCommandAsync(text))
                                    break;
                                EdenLog.Write($"[ZW] text command : {text}");
                            }
                            break;

                        case NcidWorldServerFamilyCreation:
                            // Z->W 34 (natif 143 CNE_ZW_WorldServerFamilyCreation)
                            // [i32 charId][str name] → le monde crée la famille
                            // (fil natif : DBA "CreateFamily name = %s" → mission)
                            // puis pousse NewFamily (32) à la zone émettrice.
                            await HandleFamilyCreationAsync(session, body);
                            break;

                        default:
                            EdenLog.Write($"[ZW] <-- NCID={ncid} len={body.Length} {Convert.ToHexString(body.Span[..Math.Min(body.Length, 48)].ToArray())}");
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[ZW] Erreur lien zone ({session.RemoteEndPoint}) : {ex.Message}");
        }
        finally
        {
            if (ActiveZoneSession == session) ActiveZoneSession = null;
            EdenLog.Write($"[ZW] Zone {zoneId} déconnectée");
        }
    }

    /// <summary>
    /// Finalise une livraison item mall — désasm monde
    /// <c>CNE_ZW_WorldServerItemMallReceieveAttempt::Execute</c>
    /// (@0x8104150) : result==0 → <c>ItemMallReceieveSuccess</c>
    /// (receipt + delete), sinon → <c>ItemMallReceieveFailure</c>
    /// (state 2→1) ; dans les deux cas le 58 {result, receiptId} part
    /// à la session client du perso.
    /// </summary>
    private async Task HandleItemMallDeliveryResultAsync(
        (short Result, uint ReceiptId, uint CharId, uint Serial,
            uint CreateTime) r)
    {
        try
        {
            // La ligne est en state=2 ; le nom de compte est retrouvé
            // via la session client du perso (toujours en ligne côté
            // monde quand la zone livre).
            if (ClientSessions.TryGetValue((int)r.CharId,
                    out var client) && client.Username is { } account)
            {
                if (r.Result == 0)
                    await _mall.CompleteReceivableAsync((int)r.ReceiptId,
                        account, (int)r.Serial, (int)r.CreateTime);
                else
                    await _mall.RequeueReceivableAsync((int)r.ReceiptId);
                // 58 CNC_CW_ClientItemMallReceieveAttempt
                // [s16 result][u32 receipt_id].
                var body = new byte[6];
                BitConverter.GetBytes(r.Result).CopyTo(body, 0);
                BitConverter.GetBytes(r.ReceiptId).CopyTo(body, 2);
                await client.SendPacketAsync(58, body);
                EdenLog.Write($"[ZW] ItemMall delivery receipt=" +
                    $"{r.ReceiptId} char={r.CharId} result={r.Result} " +
                    $"serial={r.Serial}");
            }
            else
            {
                // Session client introuvable : le compte est retrouvé
                // via edenfr_db.player_characters — indispensable quand le
                // résultat est 0 (la zone A livré l'item : il faut
                // insérer item_receipt, sinon la ligne reste bloquée
                // en state=2). Sans ligne perso : remise en file.
                var accountByChar = await LookupAccountNameAsync(
                    (int)r.CharId);
                if (accountByChar != null)
                {
                    if (r.Result == 0)
                        await _mall.CompleteReceivableAsync(
                            (int)r.ReceiptId, accountByChar,
                            (int)r.Serial, (int)r.CreateTime);
                    else
                        await _mall.RequeueReceivableAsync(
                            (int)r.ReceiptId);
                }
                else if (r.Result != 0)
                {
                    await _mall.RequeueReceivableAsync((int)r.ReceiptId);
                }
                EdenLog.Write($"[ZW] ItemMall delivery char=" +
                    $"{r.CharId} sans session client (result={r.Result}" +
                    $", account={accountByChar ?? "?"})");
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[ZW] ItemMall delivery result erreur : {ex.Message}");
        }
    }

    /// <summary>Remet en file les <c>item_receivable</c> restés en
    /// state=2 — claims dont le verdict ZW 12 n'est jamais revenu
    /// (redémarrage zone/world entre-temps). Appelé à chaque
    /// (ré)enregistrement de zone ; sans lui ces lignes restent
    /// invisibles à la liste (filtre state=1) et irrécupérables.</summary>
    private async Task RequeueOrphanedClaimsAsync()
    {
        try
        {
            var n = await _mall.RequeueClaimedReceivablesAsync();
            if (n > 0)
                EdenLog.Write($"[ZW] ItemMall : {n} claim(s) orphelin(s) remis en file");
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[ZW] Requeue orphaned claims : {ex.Message}");
        }
    }

    /// <summary>Retrouve le compte d'un perso via
    /// <c>edenfr_db.player_characters.account_name</c> — fallback quand le
    /// verdict ZW 12 arrive sans session client monde (déco entre le
    /// claim et la livraison).</summary>
    private async Task<string?> LookupAccountNameAsync(int charId)
    {
        if (_gameDb == null)
            return null;
        try
        {
            await using var conn = _gameDb.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT account_name FROM player_characters " +
                "WHERE id = @cid;", conn);
            cmd.Parameters.AddWithValue("cid", charId);
            return await cmd.ExecuteScalarAsync() as string;
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[ZW] LookupAccountName char={charId} : {ex.Message}");
            return null;
        }
    }

    // Charge edenfr_account.starway et pousse un CNC_ZW_ZoneServerUpdateStarWay
    // (NCID 29) par entrée — ordre std::map : trié par (week_type, theme_type).
    // Format fil (CNC_ZW_ZoneServerUpdateStarWay::Serialize) :
    //   s16 week_type | s16 theme_type
    //   u16 n | n×s16 effect_ids
    //   u16 m | m×s16 item_ids
    //   u16 len | remark bytes (GBK, stocké Latin-1→UTF8 en base)
    private async Task PushStarWayDataAsync(NetSession session)
    {
        try
        {
            await using var conn = _accountDb.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT week_type, theme_type, effect_id1, effect_id2, effect_id3," +
                " item_id1, item_id2, item_id3, item_id4, item_id5, item_id6," +
                " item_id7, item_id8, remark FROM starway ORDER BY week_type, theme_type", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var sent = 0;
            while (await reader.ReadAsync())
            {
                using var ms = new MemoryStream();
                void W16(int v)
                {
                    ms.WriteByte((byte)(v & 0xff));
                    ms.WriteByte((byte)((v >> 8) & 0xff));
                }
                W16(reader.GetInt32(0));
                W16(reader.GetInt32(1));
                W16(3);
                for (var i = 2; i <= 4; i++) W16(reader.GetInt32(i) & 0xffff);
                W16(8);
                for (var i = 5; i <= 12; i++) W16(reader.GetInt32(i) & 0xffff);
                var remark = reader.IsDBNull(13) ? "" : reader.GetString(13);
                var bytes = System.Text.Encoding.Latin1.GetBytes(remark);
                W16(bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
                await session.SendPacketAsync(NcidZoneServerUpdateStarWay, ms.ToArray());
                sent++;
            }
            EdenLog.Write($"[ZW] StarWay poussé : {sent} entrées (NCID {NcidZoneServerUpdateStarWay})");
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[ZW] PushStarWayData échoué : {ex.Message}");
        }
    }

    // Charge edenfr_db.fortune_bag et pousse CNC_ZW_ZoneServerUpdateFortuneBag
    // (ZW 26) en 3 phases — CGlobalInfo::UpdateZoneFortuneBagData natif
    // (@world 0x080598b0) : phase 0 = début (zone pose FortuneUpdating
    // + vide ses maps), phase 1 = records, phase 2 = fin.
    // Record fil (Serialize monde, 33 o) : [u16 bag_id][u16 f2]
    // [u16 slots][u16 set][u16 holder][u16 seq][f32 cumProb]
    // [u16 item_id][u16 item_num][u8 bulletin][f32 prob][f32 f12]
    // [s16 embedded][s16 bind].
    private async Task PushFortuneBagDataAsync(NetSession session)
    {
        if (_gameDb == null) return;
        try
        {
            var rows = new List<Eden.Core.Game.Fortune.FortuneBagRow>();
            await using var conn = _gameDb.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(
                // SELECT natif (PGAccountDBO world) :
                "SELECT id, sequence, set, item_id, item_num," +
                " probability, item_counter, bulletin, embedded, bind" +
                " FROM fortune_bag ORDER BY id, set, sequence", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new Eden.Core.Game.Fortune.FortuneBagRow(
                    reader.GetInt32(0), reader.GetInt16(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetFloat(5),
                    reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                    reader.GetInt32(9)));

            var records = Eden.Core.Game.Fortune.FortuneBagWire
                .RecordsFromRows(rows);
            await session.SendPacketAsync(NcidZoneServerUpdateFortuneBag,
                Eden.Core.Game.Fortune.FortuneBagWire.Build(0, []));
            // Découpage par TAILLE — la trame [u16 len] plafonne à
            // ~64 Ko : 30000 records × 33 o = ~990 Ko cassaient le
            // length-field → désync du lien zone (NCIDs garbage,
            // flap observé en live). ~1500 records ≈ 49 500 o.
            const int chunk = 1500;
            for (var i = 0; i < records.Count; i += chunk)
                await session.SendPacketAsync(NcidZoneServerUpdateFortuneBag,
                    Eden.Core.Game.Fortune.FortuneBagWire.Build(1,
                        records.GetRange(i,
                            Math.Min(chunk, records.Count - i))));
            await session.SendPacketAsync(NcidZoneServerUpdateFortuneBag,
                Eden.Core.Game.Fortune.FortuneBagWire.Build(2, []));
            EdenLog.Write($"[ZW] FortuneBag poussé : {records.Count} lots" +
                $" (NCID {NcidZoneServerUpdateFortuneBag})");
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[ZW] PushFortuneBagData échoué : {ex.Message}");
        }
    }

    /// <summary>ZW 34 — création de famille (natif
    /// <c>CNE_ZW_WorldServerFamilyCreation::Execute</c> →
    /// <c>CGlobalInfo::FamilyCreation</c> → DBA "CreateFamily").
    /// Réponse = push 32 NewFamily [i32 charId][str charName]
    /// [s16 node][i32 familyId][str familyName] vers la zone — la zone
    /// natif applique le coût de 50 000 or et émet 346 au client.
    /// familyId = -1 → nom déjà pris (texte 0x913 natif), -2 → autre
    /// refus (déjà membre).</summary>
    private static async Task HandleFamilyCreationAsync(
        NetSession zone, ReadOnlyMemory<byte> body)
    {
        if (body.Length < 6 || Families is null) return;
        var charId = BitConverter.ToInt32(body.Span);
        var nameLen = BitConverter.ToUInt16(body.Span[4..]);
        if (nameLen > body.Length - 6) return;
        var name = System.Text.Encoding.UTF8.GetString(
            body.Slice(6, nameLen).Span);

        var leader = await Families.Repo.LoadMemberSummaryAsync(charId);
        Eden.Core.Game.Family.Family? fam = null;
        var error = 0;
        if (leader is null) error = -2;
        else if (Families.GetFamilyOf(charId) is not null) error = -2;
        else if (Families.Families.Values.Any(f =>
            string.Equals(f.Name, name,
                StringComparison.OrdinalIgnoreCase))) error = -1;
        else
        {
            leader.Online = true;
            fam = await Families.CreateFamilyAsync(name, leader);
            if (fam is null) error = -2;
        }

        var s = new Eden.Core.Networking.OutBinStream();
        s.WriteInt(charId);                        // i32 — charId créateur
        s.WriteString(leader?.Name ?? "");         // str — nom du perso
        s.WriteShort(0);                           // s16 — node_id (0 = toutes zones)
        s.WriteInt(fam?.Id ?? error);              // i32 — family_id (>0 ok, <0 erreur)
        s.WriteString(fam?.Name ?? name);          // str — nom de famille
        await zone.SendPacketAsync(NcidZoneServerNewFamily, s.ToArray());
        EdenLog.Write(fam is not null
            ? $"[ZW] Famille '{fam.Name}' (#{fam.Id}) créée par char={charId}"
            : $"[ZW] Création famille refusée ({error}) : '{name}' char={charId}");
    }

    /// <summary>Commandes texte zone→monde reconnues (le natif route
    /// les TC_* de la même façon). Retourne true si traitée —
    /// sinon l'appelant se contente de logger (fidélité réception).</summary>
    private static async Task<bool> HandleZwTextCommandAsync(string text)
    {
        // « set_family_emblem <charId> <emblem> » — zone 82
        // CNE_CZ_ZoneServerSetFamilyEmblem → monde persiste
        // `family.emblem` (FamilySetEmblem natif) ; le chef seul
        // peut changer l'emblème.
        if (text.StartsWith("set_family_emblem ", StringComparison.Ordinal)
            && Families is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 3
                && int.TryParse(parts[1], out var charId)
                && int.TryParse(parts[2], out var emblem)
                && Families.GetFamilyOf(charId) is { } fam
                && fam.LeaderId == charId)
            {
                fam.Emblem = emblem;
                await Families.Repo.SetEmblemIndexAsync(fam.Id, emblem);
            }
            return true;
        }
        // « family_achievement <charId> <achId> <type> <groupKey>
        // <gained> » — la zone appelle ça quand un succès personnel
        // rapporte des points (SetAchievement natif →
        // CFamily::UpdataAchievement mission @0x081be030). Le monde
        // applique : family_achievement upsert (clé GroupKey),
        // compteurs, spell_point (si elfking), log type 4, rank
        // snapshot, puis broadcast NCID 67 flag=0 + log 68 aux membres.
        if (text.StartsWith("family_achievement ", StringComparison.Ordinal)
            && Families is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 6
                && int.TryParse(parts[1], out var charId)
                && int.TryParse(parts[2], out var achId)
                && short.TryParse(parts[3], out var achType)
                && short.TryParse(parts[4], out var groupKey)
                && int.TryParse(parts[5], out var gained))
            {
                var fam = Families.GetFamilyOf(charId);
                var r = await Families.ApplyFamilyAchievementAsync(
                    charId, achId, achType, groupKey, gained);
                if (fam is not null && r is { } res)
                {
                    // NCID 67 flag=0 : [u8 0][u16 1][s16 groupKey]
                    // [u32 newPoint][i32 charId][u32 channel=0] —
                    // monde case 0x14 de CNE_DBA_ClientChangeFamily.
                    var pkt = Eden.Core.Game.Family.FamilyPackets
                        .FamilyAchievementUpdate(0, 0,
                            [(res.GroupKey, res.GroupPoint, charId, 0u)]);
                    var log = Eden.Core.Game.Family.FamilyPackets
                        .FamilyLogUpdate(4, 0, [res.Log]);
                    foreach (var id in fam.Members.Keys.ToList())
                        if (ClientSessions.TryGetValue(id, out var ms))
                        {
                            await ms.SendPacketAsync(67, pkt);
                            await ms.SendPacketAsync(68, log);
                        }
                }
            }
            return true;
        }
        // « family_instance_create <charId> <type> <vip> » — relais de
        // la commande texte DBA native « FamilyInstanceCreate fm_id=
        // %d type=%hd vip=%hhd » (zone → CDBAgent → TC_FamilyInstanceCreate
        // mission). Le monde rejoue les gates natifs de
        // CFamily::InstanceCreate puis persiste house_open, débite la
        // trésorerie et pousse l'état aux zones (ZW 35).
        if (text.StartsWith("family_instance_create ", StringComparison.Ordinal)
            && Families is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 4
                && int.TryParse(parts[1], out var charId)
                && short.TryParse(parts[2], out var type)
                && byte.TryParse(parts[3], out var vip))
            {
                var level = Families.GetFamilyOf(charId) is { } f
                    && f.Members.TryGetValue(charId, out var me)
                        ? (int)me.Level : 0;
                var r = await Families.InstanceCreateAsync(
                    charId, type, vip, level);
                if (r.TextIndex < 0) return true;     // type invalide — silencieux natif
                if (r.TextIndex != 0)
                {
                    var fr = Families.GetFamilyOf(charId);
                    EdenLog.Write($"[FamilyInstance] refus monde : " +
                        $"char={charId} gate=0x{r.TextIndex:x} " +
                        $"fam={fr?.Id} treasury={fr?.Treasury} " +
                        $"members={fr?.Members.Count}");
                    // Erreur — équivalent de TextOutputIndex mission :
                    // ClientTextOutputIndex monde→client (NCID 43).
                    if (ClientSessions.TryGetValue(charId, out var cs))
                        await cs.SendPacketAsync(43, FamilyTextIndex(r.TextIndex));
                    return true;
                }
                var fam = Families.GetFamilyOf(charId);
                if (fam is null) return true;
                // Broadcast natif CNC_DBA_ClientChangeFamily → zones :
                // mise à jour du house_open dans le cache famille zone.
                var s = new Eden.Core.Networking.OutBinStream();
                s.WriteInt(fam.Id);           // i32 — family_id
                s.WriteShort(fam.HouseOpen);  // s16 — node de hall (501/502)
                if (ActiveZoneSession is { } zone2)
                    await zone2.SendPacketAsync(
                        NcidZoneServerFamilyDataUpdate, s.ToArray());
                // Refresh de la fiche famille (type 0 — house_open lu
                // par le client dans le bloc famille).
                foreach (var id in fam.Members.Keys.ToList())
                    if (ClientSessions.TryGetValue(id, out var ms))
                        await ms.SendPacketAsync(50,
                            Eden.Core.Game.Family.FamilyPackets
                                .QueryFamilyInfo(fam, 0, 1));
            }
            return true;
        }
        // « gm_family_setup <charId> <level> <node> <treasury> » —
        // outillage GM (//create_guild zone) : bypass des gates
        // natifs d'InstanceCreate pour une guilde fraîchement créée.
        // Persistance directe + push ZW 35 aux zones.
        if (text.StartsWith("gm_family_setup ", StringComparison.Ordinal)
            && Families is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 5
                && int.TryParse(parts[1], out var charId)
                && int.TryParse(parts[2], out var level)
                && short.TryParse(parts[3], out var node)
                && int.TryParse(parts[4], out var treasury)
                && Families.GetFamilyOf(charId) is { } gf)
            {
                await Families.LockedAsync(async () =>
                {
                    gf.Level = (byte)Math.Clamp(level, 1, 12);
                    gf.HouseOpen = node;
                    gf.Treasury = Math.Clamp(treasury, 0, 999999999);
                    await Families.Repo.SetLevelAsync(gf.Id, gf.Level,
                        gf.Exp);
                    await Families.Repo.SetHouseOpenAsync(gf.Id, node);
                    await Families.Repo.AddTreasuryAsync(gf.Id,
                        gf.Treasury);
                });
                var s = new Eden.Core.Networking.OutBinStream();
                s.WriteInt(gf.Id);           // i32 — family_id
                s.WriteShort(gf.HouseOpen);  // s16 — node de hall
                if (ActiveZoneSession is { } z3)
                    await z3.SendPacketAsync(
                        NcidZoneServerFamilyDataUpdate, s.ToArray());
                foreach (var id in gf.Members.Keys.ToList())
                    if (ClientSessions.TryGetValue(id, out var gm2))
                        await gm2.SendPacketAsync(50,
                            Eden.Core.Game.Family.FamilyPackets
                                .QueryFamilyInfo(gf, 0, 1));
            }
            return true;
        }
        // « family_set_level <familyId> <level> » — miroir de la
        // commande GM native set_family_level (S_RootCmds 144) :
        // cache monde + UPDATE family.lv.
        if (text.StartsWith("family_set_level ", StringComparison.Ordinal)
            && Families is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 3
                && int.TryParse(parts[1], out var famId)
                && int.TryParse(parts[2], out var fLevel)
                && Families.GetFamily(famId) is { } lf)
            {
                lf.Level = (byte)Math.Clamp(fLevel, 1, 12);
                await Families.Repo.SetLevelAsync(famId, lf.Level,
                    lf.Exp);
            }
            return true;
        }
        // « family_donate <familyId> <amount> » — relais du don zone
        // (CNE_CZ_ZoneServerFamilyDonateMoney) : le monde est
        // l'autorité trésorerie — InstanceCreate lit fam.Treasury.
        if (text.StartsWith("family_donate ", StringComparison.Ordinal)
            && Families is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 3
                && int.TryParse(parts[1], out var dfamId)
                && int.TryParse(parts[2], out var damount)
                && Families.GetFamily(dfamId) is { } df)
                df.Treasury = await Families.Repo
                    .AddTreasuryAsync(dfamId, damount);
            return true;
        }
        // « family_set_house_open <familyId> <node> » — //open_hall :
        // house_open direct (sans gates InstanceCreate) + ZW 35.
        if (text.StartsWith("family_set_house_open ",
                StringComparison.Ordinal) && Families is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 3
                && int.TryParse(parts[1], out var hfamId)
                && short.TryParse(parts[2], out var hnode)
                && Families.GetFamily(hfamId) is { } hf)
            {
                hf.HouseOpen = hnode;
                await Families.Repo.SetHouseOpenAsync(hfamId, hnode);
                var s = new Eden.Core.Networking.OutBinStream();
                s.WriteInt(hfamId);
                s.WriteShort(hnode);
                if (ActiveZoneSession is { } z4)
                    await z4.SendPacketAsync(
                        NcidZoneServerFamilyDataUpdate, s.ToArray());
            }
            return true;
        }
        // « gain_prestige <charId> <prestigeId> <delta> » — équivalent
        // du texte mission natif « GainExp Prestige %hd = %d »
        // (CPlayerFactory::AddPrestigeExp zone @0x080f3120 →
        // TC_GainPrestigeExp mission @0x083cd0d0). Le monde applique
        // GainPrestigeExp (clamp 0..75000, level=1, UPDATE prestige)
        // puis relaie : NCID 60 au client + update_prestige à la zone
        // (Execute monde @0x0812bff0).
        if (text.StartsWith("gain_prestige ", StringComparison.Ordinal)
            && Prestige is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 4
                && int.TryParse(parts[1], out var charId)
                && short.TryParse(parts[2], out var prestigeId)
                && int.TryParse(parts[3], out var delta)
                && delta != 0
                && await Prestige.GainExpAsync(charId, prestigeId,
                    delta) is { } e)
                await EmitPrestigeUpdateAsync(charId, prestigeId,
                    e.Level, e.Exp);
            return true;
        }
        // « set_prestige <charId> <prestigeId> <level> » — équivalent
        // du texte natif « SetLevel Prestige %hd = %hd »
        // (TC_SetPrestigeLevel mission @0x081f???? / zone @0x083cd160).
        if (text.StartsWith("set_prestige ", StringComparison.Ordinal)
            && Prestige is not null)
        {
            var parts = text.Split(' ');
            if (parts.Length >= 4
                && int.TryParse(parts[1], out var charId)
                && short.TryParse(parts[2], out var prestigeId)
                && short.TryParse(parts[3], out var level)
                && await Prestige.SetLevelAsync(charId, prestigeId,
                    level) is { } e)
                await EmitPrestigeUpdateAsync(charId, prestigeId,
                    e.Level, e.Exp);
            return true;
        }
        return false;
    }

    /// <summary>Payload [s16 textIndex][str ""] du
    /// <c>ClientTextOutputIndex</c> natif (type 0x97 mission).</summary>
    private static byte[] FamilyTextIndex(short index)
    {
        var s = new Eden.Core.Networking.OutBinStream();
        s.WriteShort(index);      // s16 — index T_StringIndex
        s.WriteString("");        // str — paramètre optionnel (vide)
        return s.ToArray();
    }
}
