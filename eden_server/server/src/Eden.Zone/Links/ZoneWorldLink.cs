using System.Net;
using System.Net.Sockets;
using Eden.Core.Networking;
using Eden.Core.Networking.Transport;
using Eden.Core.Logging;

namespace Eden.Zone.Links;

// -----------------------------------------------------------------------------
//  ZoneWorldLink — client du lien interne Zone->World (natif : zone se
//  connecte au monde sur le port ZW, ex. :5710 / façade :5711 / C# :5301).
//
//  Protocole ZW reversé (InitZoneWorldCommandTable + captures) :
//    W->Z NCID 16  vide                       demande d'identification
//    Z->W NCID 0   [u16 zoneId]               IDReport
//    W->Z NCID 26/27/28/29 blobs config       FortuneBag/Lottery/TextExCmd/StarWay
//    W->Z NCID 18  [u32 charId]               CharacterArrivedDest
//    W->Z NCID 23  [u16 len][ascii]           commande texte (update_prestige…)
//    Z->W NCID 5   [u32 charId]               ResetTime (entrée en jeu)
//    Z->W NCID 3   NotifyEntry / NCID 10 Logout
//
//  Crypto : identique aux liens clients — le monde envoie sa clé RSA,
//  on renvoie une clé RC4 5 octets chiffrée, puis trames
//  [varint len][RC4(u16 ncid + payload)] en flux continu.
// -----------------------------------------------------------------------------
public sealed class ZoneWorldLink : IAsyncDisposable
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

    /// <summary>Entrées StarWay poussées par le monde (NCID 29), payload brut.</summary>
    public List<byte[]> StarWayData { get; } = [];
    /// <summary>Autres blobs de config reçus (26/27/28), indexés par NCID.</summary>
    public Dictionary<ushort, byte[]> ConfigData { get; } = [];
    /// <summary>Invoqué quand le monde accuse l'arrivée d'un perso (NCID 18).</summary>
    public event Action<uint>? CharacterArrivedDest;

    /// <summary>Callback de push fortune_bag (W→Z NCID 26) : reçoit la
    /// phase (0/1/2) et les records du chunk — routé vers
    /// <c>FortuneBagRegistry.Apply</c> par Program.cs.</summary>
    public event Action<short,
        List<Eden.Core.Game.Fortune.FortuneBagWire.WireRecord>>?
        FortuneBagUpdate;

    /// <summary>
    /// Callback de livraison item mall (W→Z NCID 24, natif
    /// <c>CNE_ZW_ZoneServerItemMallReceieveAttempt</c>) : reçoit
    /// (receiptId, charId, itemId, itemQuantity, itemColor) et retourne
    /// (result, itemSerial, createTime) renvoyés au monde dans le ZW 12.
    /// Câblé par Program.cs sur <c>ZoneEntryFlow</c>.
    /// </summary>
    public Func<uint, uint, ushort, ushort, long, CancellationToken,
        Task<(short Result, uint Serial, uint CreateTime)>>?
        ItemMallDelivery { get; set; }

    public bool Connected => _transport != null && _tcp?.Connected == true;

    public ZoneWorldLink(string host, int port, ushort zoneId)
    {
        _host = host;
        _port = port;
        _zoneId = zoneId;
    }

    /// <summary>Connexion + handshake RSA/RC4 + boucle de réception (retry auto).</summary>
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
                EdenLog.Write($"[Zone-W] lien monde perdu ({ex.Message}) — retry 3s");
            }
            await ResetAsync();
            try { await Task.Delay(3000, token); } catch { }
        }
    }

    private async Task ConnectAndServeAsync(CancellationToken token)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, token);

        // Handshake client du lien interne : TLS 1.3 (modern, défaut)
        // ou blob RSA du monde → clé RC4 5 octets chiffrée PKCS#1 v1.5
        // (legacy) — voir TransportFactory / TlsTransport /
        // LegacyLapisTransport.
        var transport = TransportFactory.ForLinkClient(tcp, _host);
        var ctx = new DirectTransportContext(transport);
        await transport.StartAsync(ctx, token);

        _tcp = tcp;
        _transport = transport;
        EdenLog.Write($"[Zone-W] Lien monde {_host}:{_port} établi");

        // Trames [varint len en clair][payload] — TryTakePacket rend
        // le payload seul (len octets, u16 ncid inclus) une fois la
        // trame complète reçue ; trame malformée lève
        // InvalidDataException -> le lien est abandonné puis rétabli
        // par RunLoopAsync (désync RC4).
        var buffer = new PacketReceiveBuffer();
        var tmp = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            var read = await transport.ReadAsync(tmp.AsMemory(), token);
            if (read <= 0) throw new EndOfStreamException();
            buffer.Append(tmp.AsSpan(0, read));
            while (buffer.TryTakePacket(out var packet))
            {
                transport.TransformRecv(packet); // RC4 in-place du payload
                await HandlePacketAsync(packet, token);
            }
        }
    }

    private async Task HandlePacketAsync(byte[] packet, CancellationToken token)
    {
        if (packet.Length < 2) return;
        var ncid = BitConverter.ToUInt16(packet, 0);
        var body = packet.AsMemory(2);
        switch (ncid)
        {
            case 16: // demande d'identification -> IDReport [u16 zoneId]
                await SendAsync(0, BitConverter.GetBytes(_zoneId), token);
                EdenLog.Write($"[Zone-W] IDReport envoyé : zoneId={_zoneId}");
                break;
            case 18: // CharacterArrivedDest [u32 charId]
                if (body.Length >= 4)
                {
                    var charId = BitConverter.ToUInt32(body.Span);
                    EdenLog.Write($"[Zone-W] CharacterArrivedDest char=#{charId}");
                    CharacterArrivedDest?.Invoke(charId);
                }
                break;
            case 29: // UpdateStarWay (une entrée par paquet)
                lock (StarWayData) StarWayData.Add(body.ToArray());
                break;
            case 23: // commande texte [u16 len][ascii]
                {
                    var text = body.Length >= 2
                        ? System.Text.Encoding.ASCII.GetString(
                            body.Span[2..])
                        : "";
                    EdenLog.Write($"[Zone-W] TextCmd : {text}");
                    TextCommand?.Invoke(text);
                }
                break;
            case 24: // ItemMallReceieveAttempt — livraison d'un achat
                // Wire natif (désasm Serialize @0x80dd370) :
                // [u32 receipt_id][u32 char_id][u16 item_id][u16 qty]
                // [i64 item_color]. Réponse ZW 12 :
                // [s16 result][u32 receipt][u32 char][u32 serial][u32 ctime].
                if (body.Length >= 18 && ItemMallDelivery != null)
                {
                    var receiptId = BitConverter.ToUInt32(body.Span);
                    var charId = BitConverter.ToUInt32(body.Span[4..]);
                    var itemId = BitConverter.ToUInt16(body.Span[8..]);
                    var qty = BitConverter.ToUInt16(body.Span[10..]);
                    var color = BitConverter.ToInt64(body.Span[12..]);
                    var (res, serial, ctime) = await ItemMallDelivery(
                        receiptId, charId, itemId, qty, color, token);
                    var reply = new byte[18];
                    BitConverter.GetBytes(res).CopyTo(reply, 0);
                    BitConverter.GetBytes(receiptId).CopyTo(reply, 2);
                    BitConverter.GetBytes(charId).CopyTo(reply, 6);
                    BitConverter.GetBytes(serial).CopyTo(reply, 10);
                    BitConverter.GetBytes(ctime).CopyTo(reply, 14);
                    await SendAsync(12, reply, token);
                }
                break;
            case 31: // ChangeFamily — [i32 char][s16 node][i32 family]
                     // [u8 flag][i32 rank][s16 emblem][s16] (natif 645)
                FamilyPush?.Invoke(
                    ZoneEntryFlow.FamilyPushKind.Change, body.ToArray());
                break;
            case 32: // NewFamily — [i32 char][str name][s16 node]
                     // [i32 family][str familyName] (natif 646)
                FamilyPush?.Invoke(
                    ZoneEntryFlow.FamilyPushKind.New, body.ToArray());
                break;
            case 33: // RemoveFamily — [i32 familyId] (natif 647)
                FamilyPush?.Invoke(
                    ZoneEntryFlow.FamilyPushKind.Remove, body.ToArray());
                break;
            case 35: // FamilyDataUpdate — [i32 familyId][s16 house_open]
                // (équivalent zone du broadcast natif
                // CNC_DBA_ClientChangeFamily mission→zones après
                // CFamily::InstanceCreate : house_open dans le cache
                // SFamily de la zone).
                FamilyPush?.Invoke(
                    ZoneEntryFlow.FamilyPushKind.DataUpdate, body.ToArray());
                break;
            case 26: // UpdateFortuneBag — phases 0/1/2
                // (CNE_ZW_ZoneServerUpdateFortuneBag::Execute natif :
                // phase 0 clear+FortuneUpdating=1, 1 records, 2 fin).
                if (body.Length >= 2)
                {
                    var (phase, records) =
                        Eden.Core.Game.Fortune.FortuneBagWire.Parse(
                            body.Span);
                    FortuneBagUpdate?.Invoke(phase, records);
                }
                break;
            case 27 or 28:
                ConfigData[ncid] = body.ToArray();
                break;
            default:
                EdenLog.Write($"[Zone-W] <-- NCID={ncid} len={body.Length} {Convert.ToHexString(body.Span[..Math.Min(body.Length, 48)])}");
                break;
        }
    }

    /// <summary>Z->W NCID 5 : signale l'entrée en jeu du perso -> ClientResetTime client.</summary>
    public Task SendResetTimeAsync(uint charId, CancellationToken token = default) =>
        SendAsync(5, BitConverter.GetBytes(charId), token);

    /// <summary>Z->W NCID 3 : NotifyEntry.</summary>
    public Task SendNotifyEntryAsync(byte[] body, CancellationToken token = default) =>
        SendAsync(3, body, token);

    /// <summary>Z->W NCID 10 : déconnexion du perso.</summary>
    public Task SendLogoutAsync(uint charId, CancellationToken token = default) =>
        SendAsync(10, BitConverter.GetBytes(charId), token);

    /// <summary>Push famille monde→zone (ZW 31/32/33 — natifs
    /// <c>CNE_ZW_ZoneServer{Change,New,Remove}Family</c>). Le
    /// consommateur applique au registre guilde + émet 346.</summary>
    public event Action<ZoneEntryFlow.FamilyPushKind, byte[]>? FamilyPush;

    /// <summary>Commande texte monde→zone (ZW 23 — natif
    /// <c>CNC_ZW_ZoneServerTextCommand</c>, ex. <c>update_prestige</c>
    /// émis par <c>CNE_MW_WorldServerUpdatePrestigeData::Execute</c>
    /// monde @0x0812bff0). Le consommateur doit router sur la boucle.</summary>
    public event Action<string>? TextCommand;

    /// <summary>Z->W NCID 34 : création de famille (natif
    /// <c>CNC_ZW_WorldServerFamilyCreation</c> 634 → monde 143).
    /// Wire natif : [i32 charId][str name]
    /// (<c>Deserialize @ world 0x8103190</c>).</summary>
    public Task SendFamilyCreationAsync(int charId, string name,
        CancellationToken token = default)
    {
        var s = new Eden.Core.Networking.OutBinStream();
        s.WriteInt(charId);     // i32 — charId créateur
        s.WriteString(name);    // str — nom de famille demandé
        return SendAsync(34, s.ToArray(), token);
    }

    /// <summary>Z->W NCID 636 : CNC_ZW_WorldServerTextCommand — texte
    /// de commande zone→monde natif (ExchangeItemPin snprintf
    /// "exchange item %d %hd %s %s" → Server_TC_ExchageItem monde).
    /// Wire : [str].</summary>
    public Task SendTextCommandAsync(string text,
        CancellationToken token = default)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(text);
        var body = new byte[2 + t.Length];
        BitConverter.GetBytes((ushort)t.Length).CopyTo(body, 0); // u16 len
        t.CopyTo(body, 2);                                       // str cmd
        return SendAsync(636, body, token);
    }

    public async Task SendAsync(ushort ncid, byte[] payload, CancellationToken token = default)
    {
        var transport = _transport;
        if (transport == null) return;
        var body = new byte[2 + payload.Length];
        BitConverter.GetBytes(ncid).CopyTo(body, 0);
        payload.CopyTo(body, 2);
        transport.TransformSend(body);           // RC4 in-place du payload
        // [varint len][RC4 payload] — marge de 5 octets en tête.
        var frame = new byte[VarintCodec.MaxEncodedLength + body.Length];
        var hdrLen = VarintCodec.WriteVarint(frame, (uint)body.Length);
        body.CopyTo(frame, hdrLen);
        await _sendLock.WaitAsync(token);
        try
        {
            await transport.WriteAsync(
                frame.AsMemory(0, hdrLen + body.Length), token);
        }
        finally
        {
            _sendLock.Release();
        }
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts?.Cancel();
        await ResetAsync();
        if (_recvTask != null) { try { await _recvTask; } catch { } }
        _cts?.Dispose();
    }
}
