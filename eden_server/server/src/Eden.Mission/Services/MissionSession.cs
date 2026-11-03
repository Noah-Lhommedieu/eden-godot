using Eden.Core;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Networking;
using Eden.Core.Networking.Transport;
using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>
/// Session MissionServer C# — côté serveur du lien Zone↔Mission (:7654),
/// reversé depuis lapis::MissionServer. Transport observé (legacy) :
///   1. mission → zone : blob RSA [i32 nlen][i32 elen][modulus][exponent]
///   2. zone → mission : RSA_public_encrypt(clé RC4 5 octets)
///   3. trames [varint len][u16 ncid][payload] EN CLAIR (vérifié live —
///      la clé RC4 est échangée mais le trafic ZM reste plaintext).
/// En mode <c>EDEN_TRANSPORT=modern</c> (défaut) : TLS 1.3 remplace
/// tout ça — le « plaintext post-handshake » natif disparaît sous le
/// tunnel (cert partagé <see cref="TlsCertificateProvider"/>).
///
/// NCIDs zone→mission : 53 IDReport, 55 NodeCreate, 127 ListDialogue,
/// 128 OnDialogueSelected, 141 CloseMissionBrief, 142 MigrationDone,
/// 143 GiveUpMission, 144 MobDieReport, 21 AgentDBACommand.
/// NCIDs mission→zone : 52 ReportID, 56/57 dialogues, 58 TransitionCmds,
/// 65/66/67 états quête, 68-77 mail, 113 NPCTalkCheck.
/// </summary>
public sealed class MissionSession
{
    private readonly TcpClient _tcp;
    private readonly RSA? _rsa;
    private readonly MissionServer _server;
    private ITransport? _transport;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>Trace par paquet — gated : Console.WriteLine est un lock
    /// global qui serialiserait tous les threads sous charge ; activer
    /// par EDEN_MISSION_TRACE=1.</summary>
    private static readonly bool TraceInbound =
        EdenConfig.MissionTrace;

    /// <summary>ID de zone déclaré par le 53 IDReport (0 = non identifié).</summary>
    public int ZoneId;

    /// <summary>Nodes globaux déclarés par cette zone (55 charUid=0).</summary>
    public readonly HashSet<int> GlobalNodes = new();

    /// <summary><paramref name="rsa"/> : paire RSA du handshake legacy
    /// (null en mode modern — TLS utilise le cert partagé).</summary>
    public MissionSession(TcpClient tcp, RSA? rsa, MissionServer server)
    {
        _tcp = tcp;
        _rsa = rsa;
        _server = server;
    }

    /// <summary>Handshake de transport (TLS 1.3 modern, ou blob RSA
    /// legacy dont la réponse RC4 est lue puis ignorée — le lien ZM
    /// restait plaintext post-handshake) puis boucle de trames.
    /// En mode modern le « encryptPayloads:false » historique devient
    /// inutile : TLS chiffre sous la couche.</summary>
    public async Task RunAsync()
    {
        // Transport côté serveur choisi par EDEN_TRANSPORT :
        // TlsTransport (modern) ou lapis en mode clair (legacy :
        // blob RSA part, réponse lue puis ignorée).
        await using var transport =
            TransportFactory.ForAcceptedSocket(_tcp, _rsa,
                legacyEncryptPayloads: false);
        var ctx = new DirectTransportContext(transport);
        try
        {
            await transport.StartAsync(ctx, CancellationToken.None);
            _transport = transport;
            _server.Register(this);
            EdenLog.Write($"[Mission] lien zone {_tcp.Client.RemoteEndPoint}");
            // Registre des salles perso → la nouvelle zone (substitut
            // du push CNE_DBA_ClientAllPlayerRoom natif au boot).
            await _server.PushRoomsToAsync(this);
            // Cache des classements (763) — UpdateZoneRankInfo natif.
            await _server.PushRankInfoToAsync(this);
            // État des croisades (202) — les zones connectées après
            // le start doivent quand même peupler leurs sets du 617.
            await _server.PushCrusadeStatusToAsync(this);

            // 3. ReportID (52) — demande l'ID de zone ; la zone répond 53.
            await SendAsync(52, Array.Empty<byte>());

            var buffer = new Eden.Core.Networking.PacketReceiveBuffer();
            var tmp = new byte[8192];
            while (true)
            {
                var read = await transport.ReadAsync(tmp,
                    CancellationToken.None);
                if (read <= 0) throw new EndOfStreamException();
                buffer.Append(tmp.AsSpan(0, read));
                while (buffer.TryTakePacket(out var packet))
                {
                    if (packet.Length < 2) continue;
                    var ncid = BitConverter.ToUInt16(packet, 0);
                    var body = packet[2..];
                    if (TraceInbound)
                        EdenLog.Write($"[Mission] --> NCID={ncid} " +
                            $"len={body.Length} " +
                            Convert.ToHexString(body[..Math.Min(body.Length, 40)]));
                    try { await _server.DispatchAsync(this, ncid, body); }
                    catch (Exception ex)
                    { EdenLog.Write($"[Mission] dispatch {ncid} : {ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Mission] fin de lien zone : {ex.Message}");
        }
        finally
        {
            _server.Unregister(this);
        }
    }

    /// <summary>Émet une trame [varint len][u16 ncid][payload] — une
    /// seule allocation (varint@0 marge 5B, ncid@hdrLen,
    /// payload@hdrLen+2).</summary>
    /// <summary>Observateur de trames sortantes (tests) — appelé avant
    /// l'écriture transport ; null en production.</summary>
    internal Func<ushort, byte[], Task>? Tap { get; set; }

    public async Task SendAsync(ushort ncid, byte[] payload)
    {
        if (Tap != null) await Tap(ncid, payload);
        var transport = _transport;
        if (transport == null) return;
        var frame = new byte[
            VarintCodec.MaxEncodedLength + 2 + payload.Length];
        var hdrLen = VarintCodec.WriteVarint(frame,
            (uint)(2 + payload.Length));
        BitConverter.GetBytes(ncid).CopyTo(frame, hdrLen);
        payload.CopyTo(frame, hdrLen + 2);
        await _sendLock.WaitAsync();
        try
        {
            await transport.WriteAsync(
                frame.AsMemory(0, hdrLen + 2 + payload.Length),
                CancellationToken.None);
        }
        finally { _sendLock.Release(); }
    }
}
