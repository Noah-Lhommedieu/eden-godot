using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Networking;
using Eden.Core.Networking.Transport;
using Eden.Core.Logging;

namespace Eden.Ticket.Services;

/// <summary>
/// Session TicketServer C# — reversée depuis le protocole T_ natif
/// (TicketServer :2067), preuve : captures MITM World/Zone↔Ticket +
/// Eden.Core/Networking/TicketServerClient.cs (le client C# qui parle
/// déjà ce protocole au binaire natif).
///
/// Transport, piloté par <c>EDEN_TRANSPORT</c> :
///   modern (défaut) : TLS 1.3 — le cert partagé
///                     <see cref="TlsCertificateProvider"/> est
///                     présenté, les pairs pinnent son empreinte.
///   legacy          : 1. serveur → client : blob RSA
///                        [u32 nlen][u32 elen][modulus][exponent]
///                     2. client → serveur : RSA_public_encrypt
///                        (clé RC4 5 octets) — 256B
///   Puis dans les deux modes : trames
///   [varint len][u16 ncid][payload] (RC4 en legacy, chiffrées TLS
///   en modern).
///
/// NCIDs (capture 14:44) :
///   C>S 0 ServerGetTicket [u32 id][u32 id2][u32 cip][u32 sip][u16 port]
///   S>C 2 ClientGetTicket [mêmes champs][u32 t1][u32 t2]
///   C>S 1 ServerUseTicket [u32 id][ticket 22B]
///   S>C 3 ClientUseTicket [u32 id][u8 result]   (1=ok, 0=invalide)
/// </summary>
public sealed class TicketSession
{
    private readonly TcpClient _tcp;
    private readonly TicketManager _tickets;
    private readonly RSA? _rsa;

    /// <summary><paramref name="rsa"/> : paire RSA du handshake legacy
    /// (null en mode modern — TLS utilise le cert partagé).</summary>
    public TicketSession(TcpClient tcp, TicketManager tickets, RSA? rsa)
    {
        _tcp = tcp;
        _tickets = tickets;
        _rsa = rsa;
    }

    /// <summary>Handshake de transport (TLS 1.3 modern ou RSA/RC4
    /// legacy selon <c>EDEN_TRANSPORT</c>) puis boucle de trames
    /// jusqu'à la fin de connexion.</summary>
    public async Task RunAsync()
    {
        // Transport côté serveur choisi par EDEN_TRANSPORT :
        // TlsTransport (modern) ou blob RSA → clé RC4 5B (legacy).
        await using var transport =
            TransportFactory.ForAcceptedSocket(_tcp, _rsa);
        var ctx = new DirectTransportContext(transport);
        try
        {
            await transport.StartAsync(ctx, CancellationToken.None);
            EdenLog.Write($"[Ticket] handshake OK {_tcp.Client.RemoteEndPoint}");

            // Boucle de trames [varint len][u16 ncid][payload]
            // chiffrées RC4 — TryTakePacket distingue préfixe
            // incomplet (attendre) de malformé (InvalidDataException
            // → fin de session via le catch englobant).
            var buffer = new PacketReceiveBuffer();
            var tmp = new byte[8192];
            while (true)
            {
                var read = await transport.ReadAsync(tmp,
                    CancellationToken.None);
                if (read <= 0) throw new EndOfStreamException();
                buffer.Append(tmp.AsSpan(0, read));
                while (buffer.TryTakePacket(out var body))
                {
                    transport.TransformRecv(body);
                    var ncid = BitConverter.ToUInt16(body, 0);
                    var payload = body[2..];
                    var resp = Dispatch(ncid, payload);
                    if (resp is { } r)
                    {
                        // [varint len][RC4(u16 ncid + corps)] — une
                        // seule allocation (marge 5 octets en tête).
                        var frame = new byte[
                            VarintCodec.MaxEncodedLength + r.Length];
                        var hdrLen = VarintCodec.WriteVarint(frame,
                            (uint)r.Length);
                        transport.TransformSend(r);
                        r.CopyTo(frame, hdrLen);
                        await transport.WriteAsync(
                            frame.AsMemory(0, hdrLen + r.Length),
                            CancellationToken.None);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Ticket] fin de session : {ex.Message}");
        }
    }

    /// <summary>Dispatch par NCID — retourne le corps de réponse
    /// (ncid inclus) ou null si inconnu.</summary>
    private byte[]? Dispatch(ushort ncid, byte[] payload)
    {
        switch (ncid)
        {
            // ServerGetTicket — [u32 id][u32 id2][u32 cip][u32 sip][u16 port]
            // → ClientGetTicket : mêmes champs + [u32 t1][u32 t2].
            case 0:
            {
                if (payload.Length < 18) return null;
                var id = BitConverter.ToUInt32(payload, 0);      // u32 : id compte/perso
                var cip = BitConverter.ToUInt32(payload, 8);     // u32 : IP cliente
                var sip = BitConverter.ToUInt32(payload, 12);    // u32 : IP serveur cible
                var port = BitConverter.ToUInt16(payload, 16);   // u16 : port serveur
                var t = _tickets.Issue(id, cip, sip, port);
                var r = new byte[2 + 26];
                BitConverter.GetBytes((ushort)2).CopyTo(r, 0);   // ncid 2 ClientGetTicket
                payload[..18].CopyTo(r, 2);                      // écho des 18B de requête
                BitConverter.GetBytes(t.T1).CopyTo(r, 20);       // u32 : secret 1
                BitConverter.GetBytes(t.T2).CopyTo(r, 24);       // u32 : secret 2
                EdenLog.Write($"[Ticket] émis id={id} t1={t.T1:x8} t2={t.T2:x8}");
                return r;
            }
            // ServerUseTicket — [u32 id][ticket 22B] → ClientUseTicket [u32 id][u8 res]
            case 1:
            {
                if (payload.Length < 26) return null;
                var id = BitConverter.ToUInt32(payload, 0);      // u32 : id
                var ticket22 = payload[4..26];                   // 22B : ticket client
                var ok = _tickets.ValidateAndConsume(ticket22);
                var r = new byte[2 + 5];
                BitConverter.GetBytes((ushort)3).CopyTo(r, 0);   // ncid 3 ClientUseTicket
                BitConverter.GetBytes(id).CopyTo(r, 2);          // u32 : id
                r[6] = (byte)(ok ? 1 : 0);                       // u8 : 1=valide, 0=invalide
                EdenLog.Write($"[Ticket] use id={id} -> {(ok ? "OK" : "REFUSÉ")}");
                return r;
            }
            default:
                EdenLog.Write($"[Ticket] NCID {ncid} inconnu len={payload.Length}");
                return null;
        }
    }
}
