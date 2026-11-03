using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Eden.Ticket.Services;

/// <summary>Ticket enregistré par ServerGetTicket — les champs repris
/// dans la trame cliente : [u32 id][u32 clientIp][u32 serverIp]
/// [u16 port][u32 t1][u32 t2] = 22 octets (capture MITM 14:44).</summary>
public sealed record IssuedTicket(
    uint Id, uint ClientIp, uint ServerIp, ushort Port, uint T1, uint T2,
    DateTime IssuedAt)
{
    /// <summary>Les 22 octets exacts présentés par le client au
    /// serveur cible puis relayés à ServerUseTicket.</summary>
    public byte[] ToBytes()
    {
        var b = new byte[22];
        BitConverter.GetBytes(Id).CopyTo(b, 0);          // u32 : id compte/perso
        BitConverter.GetBytes(ClientIp).CopyTo(b, 4);    // u32 : IP cliente
        BitConverter.GetBytes(ServerIp).CopyTo(b, 8);    // u32 : IP serveur cible
        BitConverter.GetBytes(Port).CopyTo(b, 12);       // u16 : port serveur
        BitConverter.GetBytes(T1).CopyTo(b, 14);         // u32 : secret 1
        BitConverter.GetBytes(T2).CopyTo(b, 18);         // u32 : secret 2
        return b;
    }
}

/// <summary>Registre des tickets émis — reversé depuis
/// lapis::CTicketInterface / CGlobalInfo::AllSoldTickets du
/// TicketServer natif : ServerGetTicket ENREGISTRE le ticket,
/// ServerUseTicket le valide et le consomme (one-shot, anti-rejeu).
/// TTL par défaut 60 s (le ticket n'a de sens que pendant la
/// migration immédiate login→world→zone).</summary>
public sealed class TicketManager
{
    private readonly ConcurrentDictionary<string, IssuedTicket> _tickets = new();
    private readonly TimeSpan _ttl;

    /// <summary>TTL d'un ticket émis (défaut <see cref="Eden.Core.EdenConfig.TicketTtlSec"/> = 300 s).</summary>
    public TicketManager(TimeSpan? ttl = null) =>
        _ttl = ttl ?? TimeSpan.FromSeconds(
            Math.Max(Eden.Core.EdenConfig.TicketTtlSec, 60));

    /// <summary>Émet un ticket pour ServerGetTicket : t1/t2 tirés au
    /// hasard cryptographique (le natif utilise ses propres compteurs —
    /// seul l'appariement émission/validation compte).</summary>
    public IssuedTicket Issue(uint id, uint clientIp, uint serverIp, ushort port)
    {
        Span<byte> rnd = stackalloc byte[8];
        RandomNumberGenerator.Fill(rnd);
        var ticket = new IssuedTicket(id, clientIp, serverIp, port,
            BitConverter.ToUInt32(rnd), BitConverter.ToUInt32(rnd[4..]),
            DateTime.UtcNow);
        _tickets[Convert.ToHexString(ticket.ToBytes())] = ticket;
        return ticket;
    }

    /// <summary>Valide et CONSOME un ticket présenté par ServerUseTicket
    /// : les 22 octets doivent correspondre bit-à-bit à un ticket émis
    /// non expiré. TryRemove = atomicité one-shot (un ticket ne peut pas
    /// être rejoué, conforme au natif).</summary>
    public bool ValidateAndConsume(byte[] ticket22)
    {
        if (ticket22 is not { Length: 22 }) return false;
        if (!_tickets.TryRemove(Convert.ToHexString(ticket22), out var t))
            return false;
        return DateTime.UtcNow - t.IssuedAt <= _ttl;
    }

    /// <summary>Purge les tickets expirés (appel périodique).</summary>
    public int CleanExpired()
    {
        var now = DateTime.UtcNow;
        var n = 0;
        foreach (var kv in _tickets)
            if (now - kv.Value.IssuedAt > _ttl &&
                _tickets.TryRemove(kv.Key, out _))
                n++;
        return n;
    }
}
