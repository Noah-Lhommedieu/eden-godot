using Eden.Core;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Ticket.Services;
using Eden.Core.Logging;

// -----------------------------------------------------------------------------
//  Eden.Ticket — Serveur de Billetterie C# (.NET 8)
//  Reversé depuis lapis::TicketServer (:2067). Protocole T_ :
//  blob RSA → clé RC4 5B → trames [u16 len][u16 ncid][payload] RC4.
//  NCIDs : 0 ServerGetTicket→2 ClientGetTicket (émission+enregistrement),
//          1 ServerUseTicket→3 ClientUseTicket (validation one-shot).
// -----------------------------------------------------------------------------

var port = EdenConfig.TicketPort;
await EdenObservability.StartAsync("ticket");

// Paire RSA 2048 bits (modulus 256B, exposant 3B 01 00 01) générée au
// démarrage — le client TicketServerClient C# consomme le blob public
// et chiffre sa clé RC4 avec, donc n'importe quelle paire valide suffit.
// Mode modern (défaut) : TLS la remplace — null (retirée en phase4-c2).
var rsa = EdenConfig.LegacyTransport ? RSA.Create(2048) : null;
var tickets = new TicketManager();

EdenLog.Write("=================================================");
EdenLog.Write("       EDEN ETERNAL - TICKET SERVER (.NET 8)     ");
EdenLog.Write("=================================================");
EdenLog.Write($"[Ticket] Port d'écoute : {port} (TCP)");

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

// Purge périodique des tickets expirés.
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(5));
        var n = tickets.CleanExpired();
        if (n > 0) EdenLog.Write($"[Ticket] {n} tickets expirés purgés");
    }
});

while (true)
{
    var tcp = await listener.AcceptTcpClientAsync();
    var session = new TicketSession(tcp, tickets, rsa);
    _ = session.RunAsync();
}
