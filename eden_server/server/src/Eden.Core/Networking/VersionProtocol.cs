using System.Text;

namespace Eden.Core.Networking;

/// <summary>
/// Protocole de handshake de version (phase 4 incrément d —
/// <c>docs/temp-plans/2026-10-26-phase4-transport-protocole.md</c> §d) :
/// extension <b>Godot-only</b> — NCIDs réservés hors de la plage native
/// (&lt;~2000), échangés <b>après</b> l'établissement du transport TLS
/// et avant toute autre trame (ClientHello compris). Jamais émis en
/// mode <c>EDEN_TRANSPORT=legacy</c>.
///
/// Wire (chaque trame = <c>[varint len][u16 ncid][corps]</c>) :
/// <code>
/// C→S 0xFFFF VersionHello : [u32 'EDN1'][u8 protoMaj][u8 protoMin]
///                           [str clientVersion][str manifestHashHex]
/// S→C 0xFFFE VersionAck   : [u32 'EDN1'][u8 status=0][str serverHashHex]
/// S→C 0xFFFD VersionReject: [u32 'EDN1'][u8 reason]
///                           [str serverHashHex][str minClientVersion]
///   reason : 1=UpdateRequired 2=TooNew 3=Malformed
///   str    = u16 len + octets (format InBinStream/OutBinStream)
///   'EDN1' = octets ASCII 45 44 4E 31 (u32 LE 0x314E4445)
/// </code>
/// </summary>
public static class VersionProtocol
{
    /// <summary>NCID client→serveur du hello de version (0xFFFF).</summary>
    public const ushort NcidHello = 0xFFFF;
    /// <summary>NCID serveur→client de l'acceptation (0xFFFE).</summary>
    public const ushort NcidAck = 0xFFFE;
    /// <summary>NCID serveur→client du rejet (0xFFFD).</summary>
    public const ushort NcidReject = 0xFFFD;
    /// <summary>Magic de sous-protocole — octets « EDN1 » sur le fil
    /// (u32 little-endian 0x314E4445).</summary>
    public const uint Magic = 0x314E4445; // 'E','D','N','1'

    /// <summary>Version de protocole « major.minor » — comparable par
    /// tuple (lexicographique numérique).</summary>
    /// <param name="Major">Version majeure — changement = protocole
    /// incompatible.</param>
    /// <param name="Minor">Version mineure — évolutions compatibles.</param>
    public readonly record struct ProtocolVersion(byte Major, byte Minor)
        : IComparable<ProtocolVersion>
    {
        /// <inheritdoc/>
        public int CompareTo(ProtocolVersion other) =>
            Major != other.Major
                ? Major.CompareTo(other.Major)
                : Minor.CompareTo(other.Minor);

        public static bool operator <(ProtocolVersion a,
            ProtocolVersion b) => a.CompareTo(b) < 0;
        public static bool operator >(ProtocolVersion a,
            ProtocolVersion b) => a.CompareTo(b) > 0;
        public static bool operator <=(ProtocolVersion a,
            ProtocolVersion b) => a.CompareTo(b) <= 0;
        public static bool operator >=(ProtocolVersion a,
            ProtocolVersion b) => a.CompareTo(b) >= 0;

        /// <summary>Parse « major.minor » — null si mal formé ou
        /// composante &gt; 255.</summary>
        public static ProtocolVersion? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var dot = text.IndexOf('.');
            if (dot <= 0) return null;
            if (!byte.TryParse(text[..dot], out var maj) ||
                !byte.TryParse(text[(dot + 1)..], out var min))
                return null;
            return new ProtocolVersion(maj, min);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Major}.{Minor}";
    }

    /// <summary>Raisons de rejet émises dans
    /// <see cref="NcidReject"/>.</summary>
    public enum RejectReason : byte
    {
        /// <summary>Client trop vieux ou manifest en écart — mise à
        /// jour requise.</summary>
        UpdateRequired = 1,
        /// <summary>Protocole client plus récent que le serveur.</summary>
        TooNew = 2,
        /// <summary>Trame de version illisible.</summary>
        Malformed = 3,
    }

    /// <summary>Hello client décodé.</summary>
    /// <param name="Proto">Version de protocole parlée.</param>
    /// <param name="ClientVersion">Version applicative
    /// (« 002.003.05.67 »).</param>
    /// <param name="ManifestHashHex">SHA-256 du manifest gamedata
    /// client en hex minuscule (vide = non calculé).</param>
    public sealed record Hello(ProtocolVersion Proto,
        string ClientVersion, string ManifestHashHex);

    // ------------------------------------------------------------------
    // Sérialisation C→S
    // ------------------------------------------------------------------

    /// <summary>Construit le corps d'une trame VersionHello (le NCID
    /// <see cref="NcidHello"/> est préfixé par l'appelant).</summary>
    public static byte[] BuildHello(ProtocolVersion proto,
        string clientVersion, string manifestHashHex)
    {
        var s = new OutBinStream();
        s.WriteInt(unchecked((int)Magic));        // u32 « EDN1 »
        s.WriteByte(proto.Major);                 // u8 proto majeur
        s.WriteByte(proto.Minor);                 // u8 proto mineur
        s.WriteString(clientVersion);             // str version app
        s.WriteString(manifestHashHex);           // str hash manifest
        return s.ToArray();
    }

    /// <summary>Décode le corps d'un VersionHello (après le u16 NCID).
    /// Retourne null si le magic ne correspond pas ou si la trame est
    /// tronquée — l'appelant répond Malformed.</summary>
    public static Hello? ParseHello(byte[] body)
    {
        try
        {
            var r = new InBinStream(body);
            if ((uint)r.ReadInt() != Magic) return null;
            var proto = new ProtocolVersion(r.ReadByte(), r.ReadByte());
            var clientVersion = r.ReadString();
            var hash = r.ReadString();
            return new Hello(proto, clientVersion, hash);
        }
        catch (Exception)
        {
            return null; // trame tronquée → Malformed
        }
    }

    // ------------------------------------------------------------------
    // Sérialisation S→C
    // ------------------------------------------------------------------

    /// <summary>Corps d'une trame VersionAck (status 0 = accepté).</summary>
    public static byte[] BuildAck(string serverManifestHashHex)
    {
        var s = new OutBinStream();
        s.WriteInt(unchecked((int)Magic));        // u32 « EDN1 »
        s.WriteByte(0);                           // u8 status (0=ok)
        s.WriteString(serverManifestHashHex);     // str hash serveur
        return s.ToArray();
    }

    /// <summary>Corps d'une trame VersionReject —
    /// <paramref name="minClientVersion"/> = borne annoncée au client
    /// (chaîne vide si aucune).</summary>
    public static byte[] BuildReject(RejectReason reason,
        string serverManifestHashHex, string minClientVersion)
    {
        var s = new OutBinStream();
        s.WriteInt(unchecked((int)Magic));        // u32 « EDN1 »
        s.WriteByte((byte)reason);                // u8 reason
        s.WriteString(serverManifestHashHex);     // str hash serveur
        s.WriteString(minClientVersion);          // str version requise
        return s.ToArray();
    }

    /// <summary>Réponse serveur décodée (payload NCID inclus) —
    /// utilisée par les outils/tests (bots) ; le client Godot a son
    /// miroir <c>scripts/net/VersionProtocol.cs</c>.</summary>
    /// <param name="Accepted">0xFFFE reçu.</param>
    /// <param name="Reason">Code de rejet (si non accepté).</param>
    /// <param name="ServerManifestHashHex">Empreinte du serveur.</param>
    /// <param name="MinClientVersion">Version minimale annoncée.</param>
    public sealed record Reply(bool Accepted, RejectReason Reason,
        string ServerManifestHashHex, string MinClientVersion);

    /// <summary>Décode une trame Ack/Reject — null si le NCID n'est ni
    /// <see cref="NcidAck"/> ni <see cref="NcidReject"/> ;
    /// <see cref="InvalidDataException"/> si le corps est illisible
    /// (magic faux, tronqué).</summary>
    public static Reply? ParseReply(byte[] payload)
    {
        if (payload.Length < 2) return null;
        var ncid = BitConverter.ToUInt16(payload, 0);
        if (ncid is not (NcidAck or NcidReject)) return null;
        var r = new InBinStream(payload);
        r.ReadUShort();                            // skip NCID
        if ((uint)r.ReadInt() != Magic)
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
