using Eden.Core;
using System.Security.Cryptography;
using Eden.Core.Logging;

namespace Eden.Core.Networking;

/// <summary>
/// Fournit la paire RSA-2048 du handshake lapis.
/// (retiré en phase4-c2 — mode <c>EDEN_TRANSPORT=legacy</c> uniquement,
/// TLS la remplace en modern.)
/// Le client lit un blob FIXE de 266 octets : [u32 nlen=256][u32 elen=2][n][e]
/// — l'exposant doit donc tenir sur 2 octets (le natif logue
/// "RSA size(n) = 256 size(e) = 2"). On utilise e=257 (0x0101).
/// Si un elen=3 (e=65537, 267 octets) est envoyé, l'octet excédentaire
/// reste dans le buffer client et désynchronise son framing → silence.
/// </summary>
public static class EdenCrypto
{
    /// <summary>
    /// Charge la clé RSA e=257 depuis <c>eden_rsa.pem</c> (env EDEN_RSA_PEM,
    /// sinon à côté de l'exécutable / racine repo). Génère une clé standard
    /// en secours (cas où le fichier manque — le handshake resterait cassé).
    /// </summary>
    public static RSA CreateServerRsa()
    {
        var path = EdenConfig.RsaPem;
        if (string.IsNullOrEmpty(path))
        {
            // Cherche eden_rsa.pem : à côté de la dll, puis en remontant.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "eden_rsa.pem");
                if (File.Exists(candidate)) { path = candidate; break; }
                dir = dir.Parent;
            }
        }
        if (path != null && File.Exists(path))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(path));
            var p = rsa.ExportParameters(false);
            EdenLog.Write($"[Crypto] RSA chargée : n={p.Modulus!.Length}o e={p.Exponent!.Length}o ({path})");
            return rsa;
        }
        EdenLog.Write("[Crypto] ATTENTION: eden_rsa.pem introuvable — RSA e=65537 (client ne répondra PAS)");
        return RSA.Create(2048);
    }
}
