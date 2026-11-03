using Eden.Core;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Game.Services;
using Eden.Mission.Services;
using Eden.Core.Logging;

// -----------------------------------------------------------------------------
//  Eden.Mission — Serveur de Missions C# (.NET 8), remplacement du
//  MissionServer natif (:7654). Protocole ZM : blob RSA → trames
//  [u16 len][u16 ncid][payload] EN CLAIR. Gère les dialogues NPC (56/57),
//  le moteur de quêtes S_Mission.ini, les transitions de services (58),
//  les états quête (65/66/67) et les commandes DBA mail (68-77).
// -----------------------------------------------------------------------------

var port = EdenConfig.MissionPort;
var dataDir = EdenConfig.EdenData ?? EdenConfig.DataDir
              ?? Path.Combine(AppContext.BaseDirectory, "data");

EdenLog.Write("=================================================");
EdenLog.Write("     EDEN ETERNAL - MISSION SERVER (.NET 8)      ");
EdenLog.Write("=================================================");
EdenLog.Write($"[Mission] Port : {port} — data : {dataDir}");

var gameData = new GameDataService(dataDir);
gameData.LoadAll();
if (gameData.MissingFiles.Count > 0)
    EdenLog.Write($"[Mission] {gameData.MissingFiles.Count} .ini absents " +
        $"de '{dataDir}' : {string.Join(", ", gameData.MissingFiles.Take(8))}…");
gameData.RequireLoaded();
EdenLog.Write($"[Mission] {gameData.Missions.Count} missions, " +
    $"{gameData.Npcs.Count} NPCs chargés");

var missionDbSource = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Game);
await EdenObservability.StartAsync("mission", missionDbSource);
var db = new MissionDb(missionDbSource);
var quests = new QuestEngine(gameData, db, dataDir);
var server = new MissionServer(quests, db);
await server.LoadRoomsAsync();
// CEventRankUpdate natif : classements rafraîchis à l'heure paire.
_ = server.RunRankUpdatesAsync(CancellationToken.None);
// Paire RSA du handshake ZM legacy — inutile en mode modern
// (TLS la remplace ; retirée en phase4-c2).
var rsa = EdenConfig.LegacyTransport ? RSA.Create(2048) : null;

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
EdenLog.Write($"[Mission] En écoute sur 0.0.0.0:{port}");

while (true)
{
    var tcp = await listener.AcceptTcpClientAsync();
    var session = new MissionSession(tcp, rsa, server);
    _ = session.RunAsync();
}
