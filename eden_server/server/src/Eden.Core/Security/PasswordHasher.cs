using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Eden.Core.Security;

/// <summary>
/// Hachage des mots de passe de comptes en Argon2id, format PHC
/// (<c>$argon2id$v=19$m=65536,t=3,p=1$&lt;b64 sel&gt;$&lt;b64 hash&gt;</c>).
///
/// Ce qui est haché/vérifié n'est pas le mot de passe saisi mais le
/// <b>credential wire canonique</b> = ce que le client met dans
/// <c>CNC_CL_ServerLoginAccount.Field2</c> : le client Godot envoie
/// toujours <c>md5(mdp)</c> (32 hex minuscule). La normalisation
/// (<see cref="CanonicalWireCredential"/>) ramène un émetteur plaintext
/// vers cette forme canonique, donc le hash stocké couvre toujours
/// <c>md5(mdp_saisi)</c>.
///
/// Paramètres (recommandation OWASP « Argon2id ») : m=64 Mio, t=3, p=1,
/// sel 16 octets aléatoires, empreinte 32 octets.
/// La bibliothèque Konscious (netstandard1.3) ne fournit ni encodage PHC
/// ni <c>Verify</c> : le parse/encode/comparaison à temps constant sont
/// faits ici.
/// </summary>
public static class PasswordHasher
{
    /// <summary>Mémoire Argon2 en Kio — 64 Mio (OWASP).</summary>
    public const int MemoryKiB = 64 * 1024;
    /// <summary>Itérations Argon2 (OWASP t=3).</summary>
    public const int Iterations = 3;
    /// <summary>Parallélisme Argon2 (OWASP p=1).</summary>
    public const int DegreeOfParallelism = 1;
    /// <summary>Taille du sel aléatoire en octets.</summary>
    public const int SaltSize = 16;
    /// <summary>Taille de l'empreinte dérivée en octets.</summary>
    public const int HashSize = 32;
    /// <summary>Préfixe du format PHC argon2id stocké en base.</summary>
    public const string PhcPrefix = "$argon2id$";

    /// <summary>Vrai si la chaîne stockée est un PHC argon2id
    /// (commute legacy ↔ moderne sans parser).</summary>
    public static bool IsArgon2Phc(string? value)
        => value?.StartsWith(PhcPrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// md5 hex minuscule — même convention que le client
    /// (<c>Field2</c> de <c>ServerLoginAccount</c>).
    /// </summary>
    public static string Md5Hex(string input)
    {
        var bytes = MD5.HashData(Encoding.ASCII.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Credential wire canonique : si <paramref name="received"/> est déjà
    /// un hex md5 (32 chars) → forme Godot, normalisé en minuscules ;
    /// sinon c'est un plaintext → <c>md5(received)</c> (= ce que Godot
    /// aurait envoyé). Le PHC stocké couvre donc toujours la forme md5.
    /// </summary>
    public static string CanonicalWireCredential(string received)
        => IsMd5Hex(received) ? received.ToLowerInvariant() : Md5Hex(received);

    /// <summary>
    /// Argon2id(credential) → PHC complet
    /// <c>$argon2id$v=19$m=65536,t=3,p=1$sel$hash</c>
    /// (sel 16B aléatoire ; base64 standard sans padding, convention PHC).
    /// </summary>
    public static string HashWireCredential(string wireCredential)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(wireCredential, salt, MemoryKiB, Iterations,
            DegreeOfParallelism, HashSize);
        // PhcPrefix se termine déjà par '$' → "$argon2id$v=19$m=…".
        return $"{PhcPrefix}v=19$m={MemoryKiB},t={Iterations}," +
            $"p={DegreeOfParallelism}${B64(salt)}${B64(hash)}";
    }

    /// <summary>
    /// Cas création de compte : l'admin saisit le <b>plaintext</b> que
    /// taperont les joueurs — le credential wire sera <c>md5(plaintext)</c>,
    /// c'est cette forme qui est hachée.
    /// </summary>
    public static string HashPlaintextPassword(string plaintext)
        => HashWireCredential(Md5Hex(plaintext));

    /// <summary>
    /// Vérifie un credential wire reçu contre un PHC argon2id stocké.
    /// Retourne <c>false</c> (sans lever) sur PHC null/vide/malformé —
    /// jamais d'exception sur du contenu DB.
    /// </summary>
    public static bool Verify(string wireCredential, string? phc)
    {
        if (!TryParsePhc(phc, out var p))
            return false;
        var derived = Derive(wireCredential, p.Salt, p.MemoryKiB,
            p.Iterations, p.Parallelism, p.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(derived, p.Hash);
    }

    // ------------------------------------------------------------------
    // Interne
    // ------------------------------------------------------------------

    /// <summary>Dérivation Argon2id via Konscious
    /// (<c>Argon2id.GetBytes</c>). Le buffer du credential est effacé
    /// après usage.</summary>
    private static byte[] Derive(string credential, byte[] salt,
        int memoryKiB, int iterations, int parallelism, int hashSize)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(credential);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = memoryKiB,          // Konscious : Kio
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
            };
            return argon2.GetBytes(hashSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>Parse strict d'un PHC argon2id
    /// (<c>$argon2id$v=19$m=…,t=…,p=…$sel$hash</c>, base64 sans
    /// padding). <c>false</c> sur tout écart — le login retombe alors
    /// sur le chemin legacy/échec sans crash.</summary>
    private static bool TryParsePhc(string? phc, out Phc parsed)
    {
        parsed = default;
        if (string.IsNullOrEmpty(phc))
            return false;
        // Split('$') sur "$argon2id$v=19$m=…$sel$hash" :
        // ["", "argon2id", "v=19", "m=…,t=…,p=…", sel, hash]
        var parts = phc.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0
            || parts[1] != "argon2id" || parts[2] != "v=19")
            return false;
        var prm = parts[3].Split(',');
        if (prm.Length != 3
            || !TryParseParam(prm[0], "m=", out var m)
            || !TryParseParam(prm[1], "t=", out var t)
            || !TryParseParam(prm[2], "p=", out var p)
            || m <= 0 || t <= 0 || p <= 0)
            return false;
        var salt = UnB64(parts[4]);
        var hash = UnB64(parts[5]);
        if (salt == null || salt.Length == 0 || hash == null || hash.Length == 0)
            return false;
        parsed = new Phc(salt, hash, m, t, p);
        return true;
    }

    private static bool TryParseParam(string field, string prefix, out int value)
    {
        value = 0;
        return field.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(field.AsSpan(prefix.Length), out value);
    }

    /// <summary>Base64 standard SANS padding (encodage PHC).</summary>
    private static string B64(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=');

    /// <summary>Décodage base64 PHC (re-padding sur multiple de 4 ;
    /// <c>null</c> si encodage invalide).</summary>
    private static byte[]? UnB64(string s)
    {
        try
        {
            return Convert.FromBase64String(s.PadRight(
                (s.Length + 3) & ~3, '='));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsMd5Hex(string s)
    {
        if (s.Length != 32) return false;
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private readonly struct Phc(byte[] salt, byte[] hash, int memoryKiB,
        int iterations, int parallelism)
    {
        public byte[] Salt { get; } = salt;
        public byte[] Hash { get; } = hash;
        public int MemoryKiB { get; } = memoryKiB;
        public int Iterations { get; } = iterations;
        public int Parallelism { get; } = parallelism;
    }
}
