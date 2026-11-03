using Eden.Core;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Transport;
using Eden.Login.Handlers;
using Eden.Core.Logging;

// -----------------------------------------------------------------------------
//  Eden.Login — Serveur d'authentification (.NET 8)
//  Protocole fidèle au LoginServer natif (lapis), transport piloté par
//  EDEN_TRANSPORT :
//    modern (défaut) : TLS 1.3 via SslStream (TlsTransport), cert dev
//                     auto-signé ou EDEN_TLS_PFX.
//    legacy          : 1. Serveur → clé publique RSA-2048 brute
//                      [u32 nlen][u32 elen][n][e] (CMSTCPChannel::OnWrite
//                      état 1, MakeRSAKeyData)
//                     2. Client → RSA_size octets =
//                      RSA_public_encrypt(clé RC4 5 octets)
//                      (CMSTCPChannel::ReceiveRC4Key état 2)
//    Puis dans les deux modes : paquets [varint len][u16 NCID + champs]
//    (RC4 en legacy, chiffrés TLS en modern) et CNC_CL_ClientHello (38,
//    float 9.9) poussé à la connexion.
//  NCID entrants (index d'enregistrement natif, capture réelle) :
//  0 ServerHello, 1 ServerLoginAccount, 2 ServerLoginWorld.
// -----------------------------------------------------------------------------

var port = EdenConfig.LoginPort;

// POC : permet de forcer l'IP/port annoncés dans la liste des channels
// (les IP du dump original 47.242.253.210 sont injoignables en local).
var advertiseIp = EdenConfig.AdvertiseIp;
var advertisePort = EdenConfig.AdvertisePort;

EdenLog.Write("=================================================");
EdenLog.Write("        EDEN ETERNAL - LOGIN SERVER (.NET 8)     ");
EdenLog.Write("=================================================");
EdenLog.Write($"[Login] Port d'écoute : {port} (TCP)");
EdenLog.Write($"[Login] Base comptes : {Eden.Core.Database.DbConnectionFactory.Describe(Eden.Core.Database.DbRole.Account)}");
if (advertiseIp != null)
    EdenLog.Write($"[Login] Monde annoncé (POC) : {advertiseIp}:{advertisePort?.ToString() ?? "db"}");

// NpgsqlDataSource unique par base — pool partagé par les repos.
var accountDb = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Account);
await EdenObservability.StartAsync("login", accountDb);
var accountRepository = new AccountRepository(accountDb);
var worldRepository = new WorldRepository(accountDb);
// Registre des tickets Login→World dans edenfr_db (table login_tickets).
var gameDb = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Game);
var ticketRepository = Eden.Core.Hot.HotWiring.CreateTicketRepository("Login", gameDb);
// Client T_ vers le TicketServer — le Login natif émet les tickets
// login→world via ServerGetTicket (le World les valide par
// ServerUseTicket). Sans ce lien les tickets fabriqués localement
// sont refusés («Cannot connect to server» au choix du channel).
var ticketIp = EdenConfig.TicketIp;
var ticketPort = EdenConfig.TicketPort;
var ticketClient = new TicketServerClient(ticketIp, ticketPort);
try { await ticketClient.ConnectAsync(); }
catch (Exception ex)
{
    EdenLog.Write($"[Login] T_ TicketServer injoignable : {ex.Message} " +
        "— tickets locaux");
}
var handlers = new LoginPacketHandlers(accountRepository, worldRepository,
    ticketRepository, advertiseIp, advertisePort, ticketClient);

// CMSSocketManager::Init — paire RSA-2048 à exposant 2 octets (e=257),
// sinon le blob fait 267o au lieu de 266o et désynchronise le client.
// Mode modern : TLS remplace ce handshake — la clé n'est plus
// matérialisée (retirée en phase4-c2).
var rsa = EdenConfig.LegacyTransport ? EdenCrypto.CreateServerRsa() : null;
var rsaSize = rsa?.ExportParameters(false).Modulus!.Length ?? 0;

var dispatcher = new PacketDispatcher();
dispatcher.Register(
    () => new CNC_CL_ServerHello(),
    (s, m, t) => handlers.HandleServerHelloAsync(s, (CNC_CL_ServerHello)m, t));
dispatcher.Register(
    () => new CNC_CL_ServerLoginAccount(),
    (s, m, t) => handlers.HandleLoginAccountAsync(s, (CNC_CL_ServerLoginAccount)m, t));
dispatcher.Register(
    () => new CNC_CL_ServerLoginWorld(),
    (s, m, t) => handlers.HandleLoginWorldAsync(s, (CNC_CL_ServerLoginWorld)m, t));

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
EdenLog.Write($"[Login] Prêt — en attente de connexions sur 0.0.0.0:{port}...\n");

// Manifest gamedata (phase 4d) — résolu depuis EDEN_GAMEDATA_DIR /
// EDEN_GAMEDATA_HASH : le login ne charge pas de données, l'empreinte
// sert uniquement à comparer au manifest client.
EdenLog.Write(Eden.Core.Data.GameDataManifest.Current is { } m
    ? $"[Login] gamedata manifest sha256={m.Hex} ({m.FileCount} .ini)"
    : "[Login] gamedata manifest : absent (EDEN_GAMEDATA_DIR non résolu)");

while (true)
{
    var tcpClient = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClientAsync(tcpClient));
}

async Task HandleClientAsync(TcpClient tcpClient)
{
    // Transport choisi par EDEN_TRANSPORT : TLS 1.3 (modern, défaut)
    // ou handshake RSA blob → clé RC4 5 octets (legacy) — voir
    // TlsTransport / LegacyLapisTransport.
    await using var session = new NetSession(
        TransportFactory.ForAcceptedSocket(tcpClient, rsa));
    EdenLog.Write($"[Login] [CONNEXION] {session.RemoteEndPoint} ({EdenConfig.Transport})");
    try
    {
        // Handshake de transport : TLS auth (modern) ou blob RSA +
        // clé RC4 chiffrée (legacy, exactement RSA_size octets).
        if (EdenConfig.LegacyTransport)
            EdenLog.Write($"[Login] --> RSA pubkey ({rsaSize}o modulus) → {session.RemoteEndPoint}");
        if (!await session.StartTransportAsync())
        {
            EdenLog.Write($"[Login] [CRYPTO_FAIL] Handshake échoué ({session.RemoteEndPoint}) : {session.CloseReason}");
            return;
        }
        EdenLog.Write($"[Login] [CRYPTO_OK] {(EdenConfig.LegacyTransport ? "RC4" : "TLS 1.3")} activé pour {session.RemoteEndPoint}");

        // Handshake de version (phase 4d) — transport modern seulement :
        // la première trame doit être VersionHello (0xFFFF) ; le gate
        // répond Ack (0xFFFE) ou Reject (0xFFFD) puis ferme. Avant tout
        // push serveur (ClientHello). Trames pipelinées = leftovers.
        List<byte[]> pending;
        if (!EdenConfig.LegacyTransport && EdenConfig.VersionCheck)
        {
            using var gateCts = new CancellationTokenSource(
                NetSession.HandshakeTimeout);
            var gate = await VersionGate.HandshakeAsync(session,
                VersionGate.Options.FromConfig(), gateCts.Token);
            if (!gate.Accepted)
            {
                EdenLog.Write($"[Login] [VERSION_REJECT] {session.RemoteEndPoint} : {gate.Reason}");
                return;
            }
            var h = gate.Hello!;
            EdenLog.Write($"[Login] [VERSION_OK] {session.RemoteEndPoint} " +
                $"client={h.ClientVersion} proto={h.Proto} " +
                $"manifest={h.ManifestHashHex} ({gate.Reason})");
            pending = gate.Remaining.ToList();
        }
        else
            pending = new List<byte[]>();

        // ClientHello(9.9f) — OnReady natif pousse le CNE_CL_ServerHello
        // dont Execute envoie CNC_CL_ClientHello.
        // EDEN_PLAIN_HELLO : diag legacy — retiré en phase4-c2.
        if (EdenConfig.PlainHello)
        {
            session.ForcePlaintext = true;
            await session.SendAsync(new CNC_CL_ClientHello { Value = LoginPacketHandlers.ServerVersion });
            session.ForcePlaintext = false;
            EdenLog.Write("[Login] --> ClientHello EN CLAIR (diag)");
        }
        else
            await session.SendAsync(new CNC_CL_ClientHello { Value = LoginPacketHandlers.ServerVersion });

        // Étape 4 : boucle paquets [u16 len][RC4(u16 NCID + champs)] —
        // les trames pipelinées derrière le hello passent d'abord.
        while (true)
        {
            var packets = pending.Count > 0
                ? pending
                : await session.ReadPacketsAsync();
            pending = new List<byte[]>();
            // n<=0 = EOF — Connected reste vrai sur socket mort et
            // ferait spinner la boucle (cf. pattern Zone/World).
            if (packets.Count == 0) break;
            foreach (var payload in packets)
            {
                if (payload.Length < 2) continue;
                var ncid = payload[0] | (payload[1] << 8);
                try
                {
                    var handled = await dispatcher.DispatchAsync(
                        ncid, new InBinStream(payload[2..]), session);
                    if (!handled)
                        EdenLog.Write($"[Login] [NCID {ncid}] Aucun handler ({session.RemoteEndPoint})");
                }
                catch (Exception ex)
                {
                    EdenLog.Write($"[Login] [ERREUR] NCID {ncid} ({session.RemoteEndPoint}) : {ex.Message}");
                }
            }
        }
    }
    catch (Exception ex)
    {
        EdenLog.Write($"[Login] Erreur client ({session.RemoteEndPoint}) : {ex.Message}");
    }
    finally
    {
        EdenLog.Write($"[Login] [DECONNEXION] {session.RemoteEndPoint}");
    }
}
