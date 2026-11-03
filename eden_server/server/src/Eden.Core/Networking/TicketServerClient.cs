using System.Net;
using System.Net.Sockets;
using Eden.Core.Networking;
using Eden.Core.Networking.Transport;
using Eden.Core.Logging;

namespace Eden.Core.Networking;

/// <summary>
/// Client du protocole T_ (lien serveur→TicketServer natif :2067), reversé
/// depuis les captures MITM du couple WorldServer/ZoneServer ↔ TicketServer.
///
/// Transport : identique aux liens clients lapis —
///   1. serveur → client : blob RSA [u32 nlen][u32 elen][n][e]
///   2. client → serveur : 256 octets = RSA_public_encrypt(clé RC4 5 octets)
///   3. trames [varint len][u16 ncid][payload] chiffrées RC4 (stream
///      continu).
///
/// Protocole (capture 14:44, /tmp/mitm.log) :
///   C>S NCID=0 ServerGetTicket [u32 id][u32 id2][u32 clientIp][u32 serverIp][u16 port]
///   S>C NCID=2 ClientGetTicket [mêmes champs][u32 t1][u32 t2]
///       → le ticket remis au client = 22 octets = [id][cip][sip][port][t1][t2]
///   C>S NCID=1 ServerUseTicket [u32 id][u32 id2][ticket 22B]
///   S>C NCID=3 ClientUseTicket [u32 id][u8 result]   (result: 1=ok, 0=invalide)
///
/// GetTicket ENREGISTRE le ticket côté TicketServer ; UseTicket le valide
/// et le consomme. C'est ce lien qui rend un ticket zone acceptable par la
/// ZoneServer native (elle fait UseTicket de son côté).
/// </summary>
public sealed class TicketServerClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private TcpClient? _tcp;
    private ITransport? _transport;
    private int _disposed;

    public bool Connected => _transport != null;

    public TicketServerClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>Connexion + handshake RSA/RC4 côté client. Idempotent.</summary>
    public async Task ConnectAsync(CancellationToken token = default)
    {
        if (_transport != null) return;
        var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, token);

        // Handshake client du lien interne : TLS 1.3 (modern, défaut)
        // ou blob RSA → clé RC4 5 octets chiffrée PKCS#1 v1.5 (legacy)
        // — voir TransportFactory / TlsTransport / LegacyLapisTransport.
        var transport = TransportFactory.ForLinkClient(tcp, _host);
        var ctx = new DirectTransportContext(transport);
        await transport.StartAsync(ctx, token);

        _tcp = tcp;
        _transport = transport;
        EdenLog.Write($"[World] Lien T_ TicketServer {_host}:{_port} établi");
    }

    /// <summary>NCID 0 → NCID 2 : crée+enregistre un ticket. Retourne les 22B client.</summary>
    public async Task<byte[]?> GetTicketAsync(uint id, uint clientIp, uint serverIp,
        ushort serverPort, CancellationToken token = default)
    {
        var req = new byte[18]; // sans le ncid
        BitConverter.GetBytes(id).CopyTo(req, 0);
        BitConverter.GetBytes(id).CopyTo(req, 4);         // id2 = id (captures)
        BitConverter.GetBytes(clientIp).CopyTo(req, 8);
        BitConverter.GetBytes(serverIp).CopyTo(req, 12);
        BitConverter.GetBytes(serverPort).CopyTo(req, 16);

        var resp = await RoundtripAsync(0, req, 2, token);
        if (resp == null || resp.Length < 26) return null;

        // Ticket client = [id][cip][sip][port][t1][t2] = resp[4..26] (id2 omis)
        var ticket = new byte[22];
        BitConverter.GetBytes(BitConverter.ToUInt32(resp, 0)).CopyTo(ticket, 0);
        Array.Copy(resp, 8, ticket, 4, 18);
        return ticket;
    }

    /// <summary>NCID 1 → NCID 3 : valide+consomme un ticket présenté par un client.</summary>
    public async Task<bool> UseTicketAsync(uint id, byte[] ticket22,
        CancellationToken token = default)
    {
        var req = new byte[4 + 22];
        BitConverter.GetBytes(id).CopyTo(req, 0);
        ticket22.CopyTo(req, 4);
        var resp = await RoundtripAsync(1, req, 3, token);
        return resp is { Length: >= 5 } && resp[4] != 0;
    }

    /// <summary>Envoie une trame et attend la réponse du NCID attendu (une requête à la fois).</summary>
    private async Task<byte[]?> RoundtripAsync(ushort reqNcid, byte[] payload,
        ushort expectNcid, CancellationToken token)
    {
        await _requestLock.WaitAsync(token);
        try
        {
            await EnsureConnectedAsync(token);
            if (_transport == null) return null;

            var plain = new byte[2 + payload.Length];
            BitConverter.GetBytes(reqNcid).CopyTo(plain, 0);
            payload.CopyTo(plain, 2);
            _transport.TransformSend(plain);     // RC4 in-place du payload
            // [varint len][RC4 payload] — marge de 5 octets en tête.
            var frame = new byte[VarintCodec.MaxEncodedLength +
                plain.Length];
            var hdrLen = VarintCodec.WriteVarint(frame,
                (uint)plain.Length);
            plain.CopyTo(frame, hdrLen);
            await _transport.WriteAsync(
                frame.AsMemory(0, hdrLen + plain.Length), token);

            // Préfixe varint : lu octet par octet (≤5) jusqu'au
            // groupe sans bit de continuation — overlong/>cap lève
            // InvalidDataException → reset du lien par le catch.
            var len = await ReadVarintAsync(_transport, token);
            var enc = await ReadExactAsync(_transport, (int)len, token);
            _transport.TransformRecv(enc);
            var respNcid = BitConverter.ToUInt16(enc, 0);
            if (respNcid != expectNcid)
            {
                EdenLog.Write($"[World] T_ NCID inattendu : {respNcid} (attendu {expectNcid})");
                return null;
            }
            return enc[2..];
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[World] T_ erreur : {ex.Message}");
            await ResetAsync();
            return null;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken token)
    {
        if (_transport != null && _tcp?.Connected == true) return;
        await ResetAsync();
        await ConnectAsync(token);
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

    /// <summary>Lit le préfixe varint d'une trame — octet par octet
    /// jusqu'à un groupe sans bit7 (borne cap MaxFrameSize) ; EOF ou
    /// varint malformé = exception (lien abandonné par l'appelant).</summary>
    private static async Task<uint> ReadVarintAsync(ITransport t,
        CancellationToken token)
    {
        var buf = new byte[VarintCodec.MaxEncodedLength];
        var got = 0;
        while (true)
        {
            var r = await t.ReadAsync(buf.AsMemory(got, 1), token);
            if (r <= 0) throw new EndOfStreamException();
            got += r;
            if (VarintCodec.TryReadVarint(buf.AsSpan(0, got),
                    (uint)EdenConfig.MaxFrameSize, out var len, out _))
                return len;
        }
    }

    private static async Task<byte[]> ReadExactAsync(ITransport t, int n, CancellationToken token)
    {
        var buf = new byte[n];
        var off = 0;
        while (off < n)
        {
            var r = await t.ReadAsync(buf.AsMemory(off, n - off), token);
            if (r <= 0) throw new EndOfStreamException();
            off += r;
        }
        return buf;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await ResetAsync();
        _requestLock.Dispose();
    }
}
