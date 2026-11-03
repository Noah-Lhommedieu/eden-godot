using Eden.Core.Data;
using Eden.Core.Logging;
using static Eden.Core.Networking.VersionProtocol;

namespace Eden.Core.Networking.Session;

/// <summary>
/// Barrière de version post-handshake (phase 4 incrément d — extension
/// <b>Godot-only</b>, jamais appliquée en
/// <c>EDEN_TRANSPORT=legacy</c> ni sur les liens internes) :
/// posée sur les accept-paths client-facing (Login :6543, World :5200,
/// Zone :5201) entre <see cref="NetSession.StartTransportAsync"/> et le
/// premier push serveur. La première trame cliente doit être le
/// <c>VersionHello</c> (NCID <see cref="VersionProtocol.NcidHello"/>) ;
/// le serveur répond <see cref="VersionProtocol.NcidAck"/> ou
/// <see cref="VersionProtocol.NcidReject"/> puis, sur rejet, la session
/// se ferme proprement (<c>CloseReason</c> documenté dans le retour).
///
/// Évaluation, dans l'ordre :
/// <list type="number">
/// <item>trame absente/tronquée/mauvais magic → Reject(Malformed) ;
/// première trame d'un autre NCID (client pré-version) →
/// Reject(UpdateRequired).</item>
/// <item>proto client &lt; <c>EDEN_MIN_PROTOCOL_VERSION</c> →
/// Reject(UpdateRequired) ; proto client &gt;
/// <c>EDEN_PROTOCOL_VERSION</c> → Reject(TooNew).</item>
/// <item><c>client_version</c> &lt; <c>EDEN_MIN_CLIENT_VERSION</c>
/// (comparaison numérique pointée) → Reject(UpdateRequired).</item>
/// <item>manifest : égal → ok ; différent → Reject(UpdateRequired) en
/// mode strict (<c>EDEN_MANIFEST_STRICT=1</c>), admis + log sinon.</item>
/// </list>
/// </summary>
public static class VersionGate
{
    /// <summary>Paramètres d'évaluation — injectables pour les tests
    /// (la production lit <see cref="EdenConfig"/>).</summary>
    public sealed record Options(
        ProtocolVersion ServerProto,
        ProtocolVersion MinClientProto,
        Version? MinClientVersion,
        byte[]? ServerManifestHash,
        bool ManifestStrict)
    {
        /// <summary>Lecture standard depuis <see cref="EdenConfig"/>.</summary>
        public static Options FromConfig() => new(
            ServerProto: ProtocolVersion.Parse(EdenConfig.ProtocolVersion)
                ?? new ProtocolVersion(1, 0),
            MinClientProto: ProtocolVersion.Parse(
                EdenConfig.MinProtocolVersion) ?? new ProtocolVersion(1, 0),
            MinClientVersion: ParseVersion(EdenConfig.MinClientVersion),
            ServerManifestHash: GameDataManifest.Current?.Hash,
            ManifestStrict: EdenConfig.ManifestStrict);
    }

    /// <summary>Résultat du gate.</summary>
    /// <param name="Accepted">Vrai → la session peut entrer dans sa
    /// boucle de dispatch.</param>
    /// <param name="Remaining">Trames déjà lues après le hello (un
    /// client qui pipeline ses paquets) — à dispatcher avant de
    /// reprendre <see cref="NetSession.ReadPacketsAsync"/>.</param>
    /// <param name="Reason">Cause du refus (log) — null si accepté.</param>
    /// <param name="Hello">Le hello client décodé (null si absent/
    /// malformé) — utile pour les logs.</param>
    public sealed record Result(bool Accepted,
        IReadOnlyList<byte[]> Remaining, string? Reason, Hello? Hello);

    /// <summary>
    /// Exécute la barrière sur une session dont le transport vient
    /// d'être établi. Lit la première trame (borné par
    /// <paramref name="ct"/> — l'appelant passe typiquement un CTS de
    /// <see cref="NetSession.HandshakeTimeout"/>) puis répond et rend
    /// le verdict + les trames excédentaires.
    /// </summary>
    public static async Task<Result> HandshakeAsync(NetSession session,
        Options options, CancellationToken ct)
    {
        var serverHashHex = options.ServerManifestHash is { } h
            ? Convert.ToHexString(h).ToLowerInvariant()
            : string.Empty;
        var minVersionText = options.MinClientVersion?.ToString() ?? "";

        List<byte[]> packets;
        try
        {
            packets = await session.ReadPacketsAsync(ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Reject("timeout handshake version",
                RejectReason.Malformed);
        }

        // EOF/timeout/close pendant l'attente — pair parti, rien à
        // répondre.
        if (packets.Count == 0)
            return new Result(false, [],
                session.CloseReason ?? "connexion fermée avant le hello",
                null);

        var first = packets[0];
        var remaining = packets.Skip(1).ToArray();

        // 1. La première trame DOIT être le VersionHello — une trame
        //    d'un autre NCID = client pré-version → UpdateRequired.
        if (BitConverter.ToUInt16(first, 0) != NcidHello)
        {
            await RejectAsync(session, RejectReason.UpdateRequired,
                serverHashHex, minVersionText);
            return new Result(false, [],
                "première trame non-VersionHello (client pré-version)",
                null);
        }

        // 2. Trame dédiée mais illisible → Malformed.
        var hello = ParseHello(first[2..]);
        if (hello == null)
        {
            await RejectAsync(session, RejectReason.Malformed,
                serverHashHex, minVersionText);
            return new Result(false, [], "VersionHello malformé", null);
        }

        // 3. Protocole : trop vieux → UpdateRequired ; trop récent →
        //    TooNew (le client parle un proto plus récent que le
        //    serveur — il faut mettre à jour le serveur).
        if (hello.Proto < options.MinClientProto ||
            hello.Proto.Major < options.ServerProto.Major)
        {
            await RejectAsync(session, RejectReason.UpdateRequired,
                serverHashHex, minVersionText);
            return new Result(false, [],
                $"proto {hello.Proto} < min {options.MinClientProto}",
                hello);
        }
        if (hello.Proto > options.ServerProto)
        {
            await RejectAsync(session, RejectReason.TooNew,
                serverHashHex, minVersionText);
            return new Result(false, [],
                $"proto {hello.Proto} > serveur {options.ServerProto}",
                hello);
        }

        // 4. Version applicative (chaîne pointée « 002.003.05.67 » —
        //    comparaison numérique, absente = pas de borne).
        var clientVer = ParseVersion(hello.ClientVersion);
        if (options.MinClientVersion != null &&
            (clientVer == null || clientVer < options.MinClientVersion))
        {
            await RejectAsync(session, RejectReason.UpdateRequired,
                serverHashHex, minVersionText);
            return new Result(false, [],
                $"client_version {hello.ClientVersion} < min " +
                $"{options.MinClientVersion}", hello);
        }

        // 5. Manifest gamedata : un serveur sans manifest local ne
        //    peut pas comparer (log une fois, admis) ; en non-strict
        //    l'écart est admis mais tracé (télémétrie de dérive).
        var manifestNote = "";
        if (options.ServerManifestHash == null)
        {
            manifestNote = "manifest serveur absent (comparaison ignorée)";
        }
        else if (!HashEquals(hello.ManifestHashHex,
                     options.ServerManifestHash))
        {
            if (options.ManifestStrict)
            {
                await RejectAsync(session, RejectReason.UpdateRequired,
                    serverHashHex, minVersionText);
                return new Result(false, [],
                    $"manifest client {hello.ManifestHashHex} != " +
                    $"serveur {serverHashHex} (strict)", hello);
            }
            manifestNote =
                $"manifest mismatch client={hello.ManifestHashHex} " +
                $"serveur={serverHashHex} (admis, non-strict)";
        }
        else
        {
            manifestNote = "manifest ok";
        }

        await session.SendPacketAsync(NcidAck,
            BuildAck(serverHashHex), ct).ConfigureAwait(false);
        return new Result(true, remaining, manifestNote, hello);

        Result Reject(string reason, RejectReason code) =>
            new(false, [], $"rejet version ({code}) : {reason}", null);
    }

    /// <summary>Émet la trame VersionReject — le drain de
    /// <see cref="NetSession"/> la flush avant la fermeture de la
    /// session par l'appelant.</summary>
    private static async Task RejectAsync(NetSession session,
        RejectReason reason, string serverHashHex,
        string minClientVersion)
    {
        try
        {
            await session.SendPacketAsync(NcidReject,
                BuildReject(reason, serverHashHex, minClientVersion),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Socket déjà mort — le refus reste correct sans la trame.
        }
    }

    /// <summary>Compare le hash client (hex) au manifest serveur —
    /// chaîne vide côté client = « pas de manifest » = toujours un
    /// écart.</summary>
    private static bool HashEquals(string clientHex, byte[] serverHash)
    {
        if (clientHex.Length == 0) return false;
        return GameDataManifest.TryParseHex(clientHex, out var h) &&
            h.AsSpan().SequenceEqual(serverHash);
    }

    /// <summary>Parse une version pointée en <see cref="System.Version"/>
    /// (« 002.003.05.67 » → 2.3.5.67) — null si mal formée.</summary>
    private static Version? ParseVersion(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        Version.TryParse(text, out var v) ? v : null;
}
