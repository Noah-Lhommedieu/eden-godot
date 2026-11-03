using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Eden.Core.Logging;

namespace Eden.Core.Networking.Transport;

/// <summary>
/// Fournit le certificat TLS serveur des accept-paths et des liens
/// internes (phase 4 incrément c — mode <c>EDEN_TRANSPORT=modern</c>).
///
/// Deux sources, dans l'ordre :
/// <list type="number">
/// <item><c>EDEN_TLS_PFX</c> (+ <c>EDEN_TLS_PFX_PASSWORD</c>) : PFX de
/// production chargé tel quel.</item>
/// <item>Sinon : certificat dev auto-signé <b>persistant</b>
/// <c>&lt;racine-server&gt;/certs/eden-dev.pfx</c> — généré une fois
/// (<c>CertificateRequest("CN=eden-dev")</c>, SAN
/// localhost/127.0.0.1/::1, RSA-2048, validité ±10 ans) puis rechargé
/// à chaque boot. Tous les services de la même installation
/// présentent donc la même empreinte — c'est elle que les liens
/// internes et le client Godot pinnent en dev
/// (<c>EDEN_TLS_THUMBPRINT</c>/<c>tls_thumbprint</c>).</item>
/// </list>
/// Le certificat est caché en mémoire par processus ; le thumbprint
/// SHA-256 est logué au premier chargement
/// (<c>[TLS] thumbprint=&lt;sha256&gt;</c>).
/// </summary>
public static class TlsCertificateProvider
{
    private static readonly object _gate = new();
    private static X509Certificate2? _cached;

    /// <summary>Empreinte SHA-256 hex (majuscules, sans séparateurs)
    /// d'un certificat — format attendu par
    /// <c>EDEN_TLS_THUMBPRINT</c> / <c>tls_thumbprint</c>.</summary>
    public static string ThumbprintOf(X509Certificate2 cert) =>
        cert.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>Normalise une empreinte saisie (supprime tout ce qui
    /// n'est pas un chiffre hexadécimal, met en majuscules) — « AA:BB »,
    /// « aa bb » et « AABB » désignent la même empreinte.</summary>
    public static string NormalizeThumbprint(string value)
    {
        var chars = new char[value.Length];
        var n = 0;
        foreach (var c in value)
            if (Uri.IsHexDigit(c))
                chars[n++] = char.ToUpperInvariant(c);
        return new string(chars, 0, n);
    }

    /// <summary>Vrai quand <paramref name="cert"/> porte l'empreinte
    /// SHA-256 attendue (comparaison normalisée).</summary>
    public static bool MatchesThumbprint(X509Certificate2? cert,
        string normalizedExpected) =>
        cert != null && string.Equals(ThumbprintOf(cert),
            normalizedExpected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Certificat serveur effectif : <c>EDEN_TLS_PFX</c> si
    /// défini, sinon le dev auto-signé persistant (généré au besoin).
    /// Cache mémoire : un seul chargement/génération par processus ;
    /// le thumbprint est logué une fois.</summary>
    public static X509Certificate2 GetCertificate()
    {
        lock (_gate)
        {
            _cached ??= EdenConfig.TlsPfx is { } pfx
                ? LoadPfx(pfx, EdenConfig.TlsPfxPassword)
                : LoadOrCreate(DefaultPfxPath());
            return _cached;
        }
    }

    /// <summary>Certificat local <b>existant</b> sans génération —
    /// utilisé par les liens clients pour pinner le cert partagé :
    /// quand le fichier est là (même hôte, stack commune) son
    /// empreinte sert d'attendu ; sinon null (le lien retombe sur le
    /// mode insecure averti, ou échoue en mode pin explicite).</summary>
    public static X509Certificate2? TryLoadExisting()
    {
        lock (_gate)
        {
            if (_cached != null) return _cached;
            var path = EdenConfig.TlsPfx ?? DefaultPfxPath();
            if (!File.Exists(path)) return null;
            return _cached = LoadPfx(path, EdenConfig.TlsPfxPassword);
        }
    }

    /// <summary>Charge ou crée le PFX auto-signé à
    /// <paramref name="pfxPath"/> : si le fichier existe il est
    /// rechargé (empreinte stable entre les boots), sinon un
    /// certificat <c>CN=eden-dev</c> est généré puis exporté
    /// (écriture atomique via fichier temporaire — deux services qui
    /// démarrent ensemble ne lisent jamais un PFX à moitié écrit).
    /// Exposé pour les tests (répertoire temporaire arbitraire).</summary>
    public static X509Certificate2 LoadOrCreate(string pfxPath,
        string? password = null)
    {
        if (File.Exists(pfxPath))
        {
            var loaded = LoadPfx(pfxPath, password);
            EdenLog.Write(
                $"[TLS] cert chargé {pfxPath} " +
                $"thumbprint={ThumbprintOf(loaded)}");
            return loaded;
        }
        var created = CreateSelfSigned();
        var bytes = created.Export(X509ContentType.Pfx, password);
        var dir = Path.GetDirectoryName(Path.GetFullPath(pfxPath));
        if (dir != null) Directory.CreateDirectory(dir);
        var tmp = pfxPath + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, pfxPath, overwrite: true);
        EdenLog.Write(
            $"[TLS] cert dev auto-signé généré {pfxPath} " +
            $"thumbprint={ThumbprintOf(created)}");
        return created;
    }

    /// <summary>Charge un PFX depuis le disque (mot de passe optionnel)
    /// et logue son empreinte — les options éphémères sont conservées
    /// via <see cref="X509KeyStorageFlags.EphemeralKeySet"/> quand la
    /// plateforme le permet (pas de fuite de clé dans le store).</summary>
    public static X509Certificate2 LoadPfx(string pfxPath,
        string? password)
    {
        var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath,
            password, X509KeyStorageFlags.Exportable |
                      X509KeyStorageFlags.EphemeralKeySet);
        EdenLog.Write(
            $"[TLS] thumbprint={ThumbprintOf(cert)} ({pfxPath})");
        return cert;
    }

    /// <summary>Chemin du PFX dev par défaut :
    /// <c>&lt;racine&gt;/certs/eden-dev.pfx</c> où la racine est le
    /// dossier contenant <c>Eden.sln</c> (remontée depuis
    /// <see cref="AppContext.BaseDirectory"/> — bin/Debug/net10.0 →
    /// src/Eden.X → src → server). Repli : <c>certs/</c> à côté de
    /// l'exécutable.</summary>
    private static string DefaultPfxPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Eden.sln")))
                return Path.Combine(dir.FullName, "certs", "eden-dev.pfx");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory,
            "certs", "eden-dev.pfx");
    }

    /// <summary>Génère le certificat dev auto-signé : CN=eden-dev,
    /// SAN DNS localhost + IP 127.0.0.1 + ::1 (couvre les cibles
    /// loopback des liens et du client Godot), RSA-2048, validité
    /// 1 jour avant maintenant → +10 ans (dev : jamais de rotation à
    /// gérer, l'empreinte reste stable).</summary>
    private static X509Certificate2 CreateSelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=eden-dev", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);       // 127.0.0.1
        san.AddIpAddress(IPAddress.IPv6Loopback);   // ::1
        req.CertificateExtensions.Add(san.Build());
        // Basic constraints non-CA + usages serveur : le pinning ne se
        // soucie pas des extensions mais une validation chaîne prod le
        // ferait — autant rester corrects.
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature |
            X509KeyUsageFlags.KeyEncipherment, false));
        var eku = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }; // serverAuth
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            eku, false));
        var now = DateTimeOffset.UtcNow;
        return req.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
    }
}
