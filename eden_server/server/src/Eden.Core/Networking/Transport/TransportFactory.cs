using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Eden.Core.Logging;

namespace Eden.Core.Networking.Transport;

/// <summary>
/// Sélection du transport par accept-path / lien selon
/// <c>EDEN_TRANSPORT</c> (phase 4 incrément c) :
/// <list type="bullet">
/// <item>« <c>modern</c> » (défaut) — <see cref="TlsTransport"/>
/// TLS 1.3 des deux côtés ; le cert serveur vient de
/// <see cref="TlsCertificateProvider"/>.</item>
/// <item>« <c>legacy</c> » — <see cref="LegacyLapisTransport"/>
/// (RSA blob + RC4) reproduisant exactement l'état pré-c, ou
/// <see cref="TcpTransport"/> en clair quand le drapeau
/// <c>*_CRYPTO</c> correspondant est coupé.</item>
/// </list>
/// </summary>
public static class TransportFactory
{
    /// <summary>Transport côté accepteur (sessions client Login/World/
    /// Zone et accept des liens ZoneLink/Ticket/Mission).
    /// <paramref name="legacyKey"/> : paire RSA du handshake legacy —
    /// ignorée en mode modern (peut être null).
    /// <paramref name="legacyCrypto"/> : mode legacy seulement —
    /// false = TCP clair (drapeaux <c>WORLD_CRYPTO</c>/<c>ZONE_CRYPTO</c>).
    /// <paramref name="legacyEncryptPayloads"/> : mode legacy
    /// seulement — false = handshake fait puis trafic clair (lien
    /// ZM historique) ; sans objet en modern (TLS chiffre sous la
    /// couche).</summary>
    public static ITransport ForAcceptedSocket(TcpClient tcp,
        RSA? legacyKey, bool legacyCrypto = true,
        bool legacyEncryptPayloads = true)
    {
        if (!EdenConfig.LegacyTransport)
            return new TlsTransport(tcp, isServer: true,
                serverOptions: TlsTransport.ServerOptions());
        if (!legacyCrypto)
            return new TcpTransport(tcp);
        return new LegacyLapisTransport(tcp, legacyKey,
            encryptPayloads: legacyEncryptPayloads);
    }

    /// <summary>Transport côté connecteur des liens internes
    /// (Zone→World, Zone→Mission, Login/Zone→TicketServer).
    /// <paramref name="targetHost"/> : hôte annoncé dans le
    /// SNI/<c>TargetHost</c> TLS (loopback en dev).
    /// <paramref name="legacyEncryptPayloads"/> : idem
    /// <see cref="ForAcceptedSocket"/> côté legacy.</summary>
    public static ITransport ForLinkClient(TcpClient tcp,
        string targetHost, bool legacyEncryptPayloads = true)
    {
        if (!EdenConfig.LegacyTransport)
            return new TlsTransport(tcp, isServer: false,
                clientOptions: LinkClientOptions(targetHost));
        return new LegacyLapisTransport(tcp, isServer: false,
            encryptPayloads: legacyEncryptPayloads);
    }

    // ------------------------------------------------------------------
    // Validation TLS des liens internes (loopback serveur↔serveur).
    // EDEN_LINK_TLS=pin|insecure ; auto = pin quand une empreinte est
    // disponible (EDEN_TLS_THUMBPRINT ou cert local partagé).
    // ------------------------------------------------------------------

    /// <summary>Options d'authentification cliente des liens internes :
    /// pinning SHA-256 contre <c>EDEN_TLS_THUMBPRINT</c> (ou l'empreinte
    /// du cert local partagé quand il existe — stack mono-hôte), sinon
    /// accept-any dev avec warning unique.
    /// <c>EDEN_LINK_TLS</c> force « pin » (échoue sans empreinte) ou
    /// « insecure ». TLS 1.3 imposé, pair au serveur.</summary>
    public static SslClientAuthenticationOptions LinkClientOptions(
        string targetHost) =>
        LinkClientOptions(targetHost, EdenConfig.LinkTls,
            ExpectedLinkThumbprint());

    /// <summary>Empreinte attendue des pairs de lien :
    /// <c>EDEN_TLS_THUMBPRINT</c> normalisée en priorité, sinon celle
    /// du cert local partagé déjà présent sur disque (les services
    /// d'une même installation se pinnent alors mutuellement sans
    /// configuration). Null quand aucune des deux n'est dispo.</summary>
    public static string? ExpectedLinkThumbprint()
    {
        if (EdenConfig.TlsThumbprint is { } env)
            return TlsCertificateProvider.NormalizeThumbprint(env);
        var local = TlsCertificateProvider.TryLoadExisting();
        return local == null
            ? null
            : TlsCertificateProvider.ThumbprintOf(local);
    }

    /// <summary>Construit les options clientes pour un mode explicite
    /// (<paramref name="mode"/> = « pin »/« insecure »/null=auto) et
    /// une empreinte attendue déjà résolue — séparé de la lecture
    /// config pour être testable sans env vars.</summary>
    internal static SslClientAuthenticationOptions LinkClientOptions(
        string targetHost, string? mode, string? expectedThumbprint)
    {
        var insecure = string.Equals(mode, "insecure",
            StringComparison.OrdinalIgnoreCase);
        var pin = string.Equals(mode, "pin",
            StringComparison.OrdinalIgnoreCase)
            || (!insecure && expectedThumbprint != null);
        if (pin && expectedThumbprint == null)
            throw new InvalidOperationException(
                "EDEN_LINK_TLS=pin sans EDEN_TLS_THUMBPRINT ni " +
                "certificat local partagé à pinner");
        RemoteCertificateValidationCallback validation =
            (sender, cert, chain, errors) => pin
                ? cert is X509Certificate2 c2 &&
                  TlsCertificateProvider.MatchesThumbprint(c2,
                      expectedThumbprint!)
                : AcceptInsecureLink();
        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls13,
            RemoteCertificateValidationCallback = validation,
            // Le cert dev est auto-signé : la révocation n'a pas de
            // sens ici (pas de CRL/OCSP) — éviter les lookups réseau.
            CertificateRevocationCheckMode =
                X509RevocationMode.NoCheck,
        };
    }

    /// <summary>Accept-any dev des liens : avertit une fois par
    /// processus (log, pas spam par connexion) puis accepte tout
    /// certificat — mode <c>EDEN_LINK_TLS=insecure</c>.</summary>
    private static bool AcceptInsecureLink()
    {
        if (Interlocked.Exchange(ref _insecureLinkWarned, 1) == 0)
            EdenLog.Write("[TLS] EDEN_LINK_TLS insecure — validation " +
                "du cert des liens internes désactivée (dev)");
        return true;
    }
    private static int _insecureLinkWarned;
}
