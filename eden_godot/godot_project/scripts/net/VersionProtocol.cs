namespace EdenClient.Net;

/// <summary>
/// Handshake de version post-TLS (phase 4 incrément d — extension
/// <b>Godot-only</b>, miroir du
/// <c>server/src/Eden.Core/Networking/VersionProtocol.cs</c>) :
/// après l'authentification TLS et avant tout autre paquet, le client
/// envoie <c>VersionHello</c> et attend <c>VersionAck</c> ou
/// <c>VersionReject</c>. Jamais émis en transport « legacy » (le
/// protocole natif n'a pas de gate — la première trame y est le
/// paquet métier).
///
/// Wire (chaque trame = <c>[varint len][u16 ncid][corps]</c>) :
/// <code>
/// C→S 0xFFFF VersionHello : [u32 'EDN1'][u8 protoMaj][u8 protoMin]
///                           [str clientVersion][str manifestHashHex]
/// S→C 0xFFFE VersionAck   : [u32 'EDN1'][u8 status=0][str serverHashHex]
/// S→C 0xFFFD VersionReject: [u32 'EDN1'][u8 reason]
///                           [str serverHashHex][str minClientVersion]
///   reason : 1=UpdateRequired 2=TooNew 3=Malformed
///   str    = u16 len + octets Big5 ; 'EDN1' = u32 LE 0x314E4445
/// </code>
/// </summary>
public static class VersionProtocol
{
    /// <summary>NCID client→serveur du hello (0xFFFF).</summary>
    public const ushort NcidHello = 0xFFFF;
    /// <summary>NCID serveur→client de l'acceptation (0xFFFE).</summary>
    public const ushort NcidAck = 0xFFFE;
    /// <summary>NCID serveur→client du rejet (0xFFFD).</summary>
    public const ushort NcidReject = 0xFFFD;
    /// <summary>Magic de sous-protocole — octets « EDN1 » sur le fil.</summary>
    public const uint Magic = 0x314E4445; // 'E','D','N','1' LE

    /// <summary>Version de protocole parlée par ce client — doit rester
    /// ≤ la <c>EDEN_PROTOCOL_VERSION</c> du serveur visé.</summary>
    public static readonly (byte Major, byte Minor) Proto = (1, 0);

    /// <summary>Raisons de rejet du serveur
    /// (<see cref="NcidReject"/>).</summary>
    public enum RejectReason : byte
    {
        /// <summary>Mise à jour du client requise.</summary>
        UpdateRequired = 1,
        /// <summary>Protocole client plus récent que le serveur.</summary>
        TooNew = 2,
        /// <summary>Trame de version illisible.</summary>
        Malformed = 3,
    }

    /// <summary>Réponse serveur décodée.</summary>
    /// <param name="Accepted">0xFFFE reçu.</param>
    /// <param name="Reason">Code de rejet (si <paramref name="Accepted"/>
    /// faux).</param>
    /// <param name="ServerManifestHashHex">Empreinte gamedata du
    /// serveur (hex).</param>
    /// <param name="MinClientVersion">Version minimale annoncée (rejet).
    /// </param>
    public sealed record Reply(bool Accepted, RejectReason Reason,
        string ServerManifestHashHex, string MinClientVersion);

    /// <summary>Construit le corps du VersionHello (le NCID est préfixé
    /// par l'appelant dans la trame).</summary>
    public static byte[] BuildHelloPayload(string clientVersion,
        string manifestHashHex)
    {
        var w = new BinWriter();
        w.WriteUInt(Magic);                     // u32 « EDN1 »
        w.WriteByte(Proto.Major);               // u8 proto majeur
        w.WriteByte(Proto.Minor);               // u8 proto mineur
        w.WriteString(clientVersion);           // str version app
        w.WriteString(manifestHashHex);         // str hash manifest
        return w.ToArray();
    }

    /// <summary>Trame complète [u16 NCID][corps] du VersionHello.</summary>
    public static byte[] BuildHelloFrame(string clientVersion,
        string manifestHashHex)
    {
        var body = BuildHelloPayload(clientVersion, manifestHashHex);
        var w = new BinWriter();
        w.WriteUShort(NcidHello);               // u16 NCID
        w.WriteBytes(body);                     // corps
        return w.ToArray();
    }

    /// <summary>Décode la réponse serveur — le payload doit porter le
    /// u16 NCID en tête. Retourne null si ce n'est ni Ack ni Reject —
    /// l'appelant traite alors la trame comme un paquet normal (serveur
    /// sans gate : repli tolérant).</summary>
    public static Reply? ParseReply(byte[] payload)
    {
        if (payload.Length < 2) return null;
        var ncid = (ushort)(payload[0] | (payload[1] << 8));
        if (ncid is not (NcidAck or NcidReject)) return null;
        var r = new BinReader(payload);
        r.ReadUShort();                          // skip NCID
        if (r.ReadUInt() != Magic)
            throw new InvalidDataException(
                "réponse de version : magic invalide");
        var status = r.ReadByte();
        var serverHash = r.ReadString();
        if (ncid == NcidAck)
            return new Reply(true, 0, serverHash, "");
        return new Reply(false, (RejectReason)status, serverHash,
            r.ReadString());
    }
}

/// <summary>Le serveur exige une version plus récente du client (ou un
/// manifest conforme en mode strict) — l'UI affiche « mise à jour
/// requise ».</summary>
public sealed class UpdateRequiredException : Exception
{
    /// <summary>Version minimale annoncée par le serveur.</summary>
    public string? MinClientVersion { get; }
    /// <summary>Empreinte gamedata du serveur (hex).</summary>
    public string? ServerManifestHash { get; }

    public UpdateRequiredException(string message,
        string? minClientVersion = null,
        string? serverManifestHash = null)
        : base(message)
    {
        MinClientVersion = minClientVersion;
        ServerManifestHash = serverManifestHash;
    }
}
