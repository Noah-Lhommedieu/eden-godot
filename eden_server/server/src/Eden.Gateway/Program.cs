using Eden.Core;
using System.Net;
using System.Net.Sockets;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Gateway;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

// -----------------------------------------------------------------------------
//  Eden.Gateway — Passerelle Réseau C# (.NET 8)
//  Reversé fidèlement depuis lapis::GatewayServer (Port 5560)
//  Gère l'aiguillage, le keepalive (Heartbeat) et l'accès sécurisé.
// -----------------------------------------------------------------------------

var port = EdenConfig.GatewayPort;
await EdenObservability.StartAsync("gateway");
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

EdenLog.Write("=================================================");
EdenLog.Write("       EDEN ETERNAL - GATEWAY SERVER (.NET 8)    ");
EdenLog.Write("=================================================");
EdenLog.Write($"[Gateway] À l'écoute sur 0.0.0.0:{port} (TCP)");

var dispatcher = new PacketDispatcher();

// Enregistrement des gestionnaires de paquets Gateway
dispatcher.Register(
    () => new GatewayHeartbeatMessage(),
    async (session, msg, token) =>
    {
        EdenLog.Write($"[Gateway] [HEARTBEAT] Ping reçu à {msg.Timestamp} depuis {session.RemoteEndPoint}");
        var reply = new GatewayHeartbeatMessage { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await session.SendAsync(reply, token);
    });

dispatcher.Register(
    () => new GatewayAccountLoginRequest(),
    async (session, msg, token) =>
    {
        EdenLog.Write($"[Gateway] [LOGIN] Tentative login passerelle : Utilisateur='{msg.Username}', Version={msg.ClientVersion} ({session.RemoteEndPoint})");
        var reply = new GatewayAccountLoginResponse
        {
            Success = true,
            AccountId = 1,
            Message = "Gateway connection accepted."
        };
        await session.SendAsync(reply, token);
    });

while (true)
{
    var tcpClient = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        await using var session = new NetSession(tcpClient);
        EdenLog.Write($"[Gateway] [CONNEXION] Client connecté : {session.RemoteEndPoint}");

        var buffer = new byte[8192];
        try
        {
            // NOTE : lecture brute sans framing [u16 len][payload] —
            // TCP ne garantit pas read==paquet. Serveur de démo hors du
            // chemin client réel ; à réécrire avec PacketReceiveBuffer
            // quand le gateway natif (auth launcher) sera porté.
            int bytesRead;
            while ((bytesRead = await session.ReceiveAsync(buffer)) > 0)
            {
                var packetData = buffer[..bytesRead];
                EdenLog.Write($"[Gateway] <-- Reçu {bytesRead} octets depuis {session.RemoteEndPoint}");

                var inStream = new InBinStream(packetData);
                
                // Si la trame commence par un opcode identifié, on route
                // Sinon on inspecte et traite les commandes standard
                try
                {
                    if (bytesRead >= 8) // Taille minimale d'un timestamp heartbeat
                    {
                        var heartbeat = new GatewayHeartbeatMessage();
                        heartbeat.Deserialize(inStream);
                        var reply = new GatewayHeartbeatMessage { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                        await session.SendAsync(reply);
                    }
                }
                catch (Exception ex)
                {
                    EdenLog.Write($"[Gateway] Erreur parsing paquet : {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Gateway] Erreur client ({session.RemoteEndPoint}) : {ex.Message}");
        }
        finally
        {
            EdenLog.Write($"[Gateway] [DECONNEXION] Client {session.RemoteEndPoint} déconnecté");
        }
    });
}
