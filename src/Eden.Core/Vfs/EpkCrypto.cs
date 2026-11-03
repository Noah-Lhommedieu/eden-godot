using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EdenClient.Vfs;

/// <summary>
/// Cryptographie EPK v1 — spec docs/packaging/EPK-FORMAT.md.
/// Pur .NET (HKDF/AesGcm/ECDsa natifs net8, zéro dépendance) —
/// compilé aussi par tools/edenpack (écriture) et NetTests.
///
/// Schéma :
///   Kwrap = HKDF-SHA256(IKM=A‖B, salt=packId, info="eden-epk-wrap:v1")
///   Kdata wrappée AES-256-GCM(Kwrap) → bloc 60o après le header.
///   Kfile = HKDF-SHA256(Kdata, salt=fileId‖cryptoRev, info file)
///   chunk : nonce 96b = chunkIdx, AAD = fileId‖rev‖idx‖uSize.
///   index signé ECDSA P-256 (r‖s P1363 64o) sur
///   header+wrap+index tels qu'écrits.
///
/// Keyfile edenpack (JAMAIS dans le dépôt — warning si perms > 600) :
/// lignes « clef=valeur » trim, '#' commentaire :
///   A=&lt;hex64&gt;     part A (32o) — côté client, embarquée obfusquée
///   B=&lt;hex64&gt;     part B (32o) — servie par le serveur au login
///   kdata=&lt;hex64&gt;  clé pack (32o) — générée puis écrite si absente
///   sign=&lt;hexDER&gt;  clé privée ECDSA DER (PKCS8 ou SEC1) — option
/// </summary>
public static class EpkCrypto
{
    private static readonly byte[] WrapInfo =
        "eden-epk-wrap:v1"u8.ToArray();
    private static readonly byte[] FileInfo =
        "eden-epk-file:v1"u8.ToArray();

    /// <summary>Chiffreur de chunk : (entry, chunkIdx, uSize, clair)
    /// → cipher‖tag16 (ce qui va sur disque, cSize inclus).
    /// <paramref name="uSize"/> = taille CLAIRE du chunk (champ
    /// header — ≠ plain.Length quand codec=zstd) : lié à l'AAD.</summary>
    public delegate byte[] EpkEncryptor(
        in EpkEntry entry, int chunkIdx, int uSize,
        ReadOnlySpan<byte> plain);

    // ---------- génération / dérivations ----------

    /// <summary>Clé 32o aléatoire (kdata).</summary>
    public static byte[] GenKey() => RandomNumberGenerator.GetBytes(32);

    /// <summary>Part 32o aléatoire (A ou B).</summary>
    public static byte[] GenPart() => RandomNumberGenerator.GetBytes(32);

    /// <summary>Kwrap = HKDF-Extract(salt=packId, IKM=A‖B) puis
    /// Expand(info="eden-epk-wrap:v1", 32).</summary>
    public static byte[] DeriveKwrap(ReadOnlySpan<byte> partA,
        ReadOnlySpan<byte> partB, ReadOnlySpan<byte> packId)
    {
        var ikm = new byte[partA.Length + partB.Length];
        partA.CopyTo(ikm);
        partB.CopyTo(ikm.AsSpan(partA.Length));
        var prk = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, ikm, packId, prk);
        var okm = new byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, okm, WrapInfo);
        return okm;
    }

    /// <summary>Kfile = HKDF-Extract(salt=fileId‖cryptoRev, IKM=Kdata)
    /// puis Expand(info="eden-epk-file:v1", 32).</summary>
    public static byte[] DeriveKfile(byte[] kdata, ulong fileId,
        uint cryptoRev)
    {
        var salt = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(salt, fileId);
        BinaryPrimitives.WriteUInt32LittleEndian(salt.AsSpan(8),
            cryptoRev);
        var prk = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, kdata, salt, prk);
        var okm = new byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, okm, FileInfo);
        return okm;
    }

    // ---------- wrapping kdata (bloc 60o) ----------

    /// <summary>Wrap Kdata → bloc 60o (cipher32‖nonce12‖tag16) —
    /// nonce aléatoire à chaque appel (volontairement non
    /// déterministe : le bloc est ré-émis à chaque release).</summary>
    public static byte[] WrapKdata(byte[] kdata, byte[] partA,
        byte[] partB, byte[] packId)
    {
        var kwrap = DeriveKwrap(partA, partB, packId);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var blk = new byte[EpkFormat.WrapBlockSize];
        using var gcm = new AesGcm(kwrap, EpkFormat.GcmTagSize);
        gcm.Encrypt(nonce, kdata, blk.AsSpan(0, 32),
            blk.AsSpan(44, EpkFormat.GcmTagSize));
        nonce.CopyTo(blk, 32);
        return blk;
    }

    /// <summary>Unwrap le bloc 60o → Kdata. Tag invalide (mauvaise
    /// part B, corruption) → <see cref="CryptographicException"/>
    /// propagée.</summary>
    public static byte[] UnwrapKdata(ReadOnlySpan<byte> wrapBlock,
        ReadOnlySpan<byte> partA, ReadOnlySpan<byte> partB,
        ReadOnlySpan<byte> packId)
    {
        if (wrapBlock.Length != EpkFormat.WrapBlockSize)
            throw new InvalidDataException(
                $"bloc wrap {wrapBlock.Length} o ≠ " +
                EpkFormat.WrapBlockSize);
        var kwrap = DeriveKwrap(partA, partB, packId);
        var kdata = new byte[32];
        using var gcm = new AesGcm(kwrap, EpkFormat.GcmTagSize);
        gcm.Decrypt(wrapBlock.Slice(32, 12), wrapBlock.Slice(0, 32),
            wrapBlock.Slice(44, EpkFormat.GcmTagSize), kdata);
        return kdata;
    }

    // ---------- chunks ----------

    /// <summary>nonce 96 bits = chunkIdx (compteur LE, unique par
    /// construction sous Kfile).</summary>
    private static byte[] Nonce(int chunkIdx)
    {
        var n = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(n, (uint)chunkIdx);
        return n;
    }

    /// <summary>AAD = fileId(u64)‖cryptoRev(u32)‖chunkIdx(u32)‖
    /// uSize(u32), little-endian — 20o.</summary>
    private static byte[] Aad(ulong fileId, uint cryptoRev,
        int chunkIdx, int uSize)
    {
        var a = new byte[20];
        BinaryPrimitives.WriteUInt64LittleEndian(a, fileId);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(8), cryptoRev);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(12),
            (uint)chunkIdx);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(16),
            (uint)uSize);
        return a;
    }

    /// <summary>Chiffreur de chunks AES-256-GCM — retourne
    /// cipher‖tag16 (déterministe à (Kdata, contenu, rev) fixés :
    /// blobs inchangés byte-identiques entre releases → diff
    /// incrémental réel).</summary>
    public static EpkEncryptor MakeEncryptor(byte[] kdata) =>
        (in EpkEntry e, int idx, int uSize,
         ReadOnlySpan<byte> plain) =>
        {
            var kf = DeriveKfile(kdata, e.FileId, e.CryptoRev);
            var ct = new byte[plain.Length + EpkFormat.GcmTagSize];
            using var gcm = new AesGcm(kf, EpkFormat.GcmTagSize);
            gcm.Encrypt(Nonce(idx), plain, ct.AsSpan(0, plain.Length),
                ct.AsSpan(plain.Length, EpkFormat.GcmTagSize),
                Aad(e.FileId, e.CryptoRev, idx, uSize));
            return ct;
        };

    /// <summary>Déchiffreur à injecter dans
    /// <see cref="EpkReader.Decryptor"/> — tag invalide →
    /// <see cref="CryptographicException"/>.</summary>
    public static EpkReader.EpkDecryptor MakeDecryptor(byte[] kdata) =>
        (in EpkEntry e, int idx, int uSize, ReadOnlySpan<byte> cipher,
         ReadOnlySpan<byte> tag) =>
        {
            var kf = DeriveKfile(kdata, e.FileId, e.CryptoRev);
            var pt = new byte[cipher.Length];
            using var gcm = new AesGcm(kf, EpkFormat.GcmTagSize);
            gcm.Decrypt(Nonce(idx), cipher, tag, pt,
                Aad(e.FileId, e.CryptoRev, idx, uSize));
            return pt;
        };

    // ---------- signature index (bloc 68o) ----------

    /// <summary>Signe la région (header+wrap+index tels qu'écrits) —
    /// retourne (keyId dérivé de la clé publique, sig 64o r‖s P1363).
    /// L'appelant peut écraser keyId (--sign-keyid).</summary>
    public static (uint KeyId, byte[] Sig) SignIndex(
        ReadOnlySpan<byte> privateKeyDer,
        ReadOnlySpan<byte> signedRegion)
    {
        using var ecd = ImportPrivateDer(privateKeyDer);
        var keyId = DefaultKeyId(ecd.ExportSubjectPublicKeyInfo());
        var sig = ecd.SignData(signedRegion, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return (keyId, sig);
    }

    /// <summary>Vérifie la signature (r‖s P1363 64o) — toute
    /// anomalie (clé invalide, sig malformée) = false.</summary>
    public static bool VerifyIndex(ReadOnlySpan<byte> pubKeyDer,
        ReadOnlySpan<byte> signedRegion, ReadOnlySpan<byte> sig)
    {
        if (sig.Length != 64) return false;
        try
        {
            using var ecd = ECDsa.Create();
            ecd.ImportSubjectPublicKeyInfo(pubKeyDer, out _);
            return ecd.VerifyData(signedRegion, sig,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException) { return false; }
    }

    /// <summary>keyId par défaut : 4 premiers octets (LE) du sha256
    /// de la clé publique SPKI — stable, sans registre.</summary>
    public static uint DefaultKeyId(ReadOnlySpan<byte> pubDer) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            SHA256.HashData(pubDer));

    /// <summary>Import clé privée DER : PKCS8 puis SEC1.</summary>
    public static ECDsa ImportPrivateDer(ReadOnlySpan<byte> der)
    {
        var e = ECDsa.Create();
        try { e.ImportPkcs8PrivateKey(der, out _); }
        catch (CryptographicException)
        {
            try { e.ImportECPrivateKey(der, out _); }
            catch (CryptographicException) { e.Dispose(); throw; }
        }
        return e;
    }

    /// <summary>Clé privée depuis fichier : PEM (« -----BEGIN ») ou
    /// hex DER. Retour normalisé PKCS8 DER.</summary>
    public static byte[] LoadPrivateKeyDer(string path)
    {
        var text = File.ReadAllText(path).Trim();
        if (text.StartsWith('-'))
        {
            using var e = ECDsa.Create();
            e.ImportFromPem(text);
            return e.ExportPkcs8PrivateKey();
        }
        var der = FromHex(text);
        using (ImportPrivateDer(der)) { }   // valide tout de suite
        return der;
    }

    /// <summary>Clé publique depuis fichier : PEM ou hex DER
    /// (SPKI). Retour normalisé SPKI DER.</summary>
    public static byte[] LoadPublicKeyDer(string path)
    {
        var text = File.ReadAllText(path).Trim();
        if (text.StartsWith('-'))
        {
            using var e = ECDsa.Create();
            e.ImportFromPem(text);
            return e.ExportSubjectPublicKeyInfo();
        }
        return FromHex(text);
    }

    // ---------- keyfile / hex ----------

    /// <summary>Contenu parsé d'un keyfile (voir en-tête classe).</summary>
    public sealed class Keyfile
    {
        public byte[]? A, B, Kdata, Sign;
    }

    /// <summary>Parse un keyfile + warning si lisible par d'autres
    /// que le propriétaire (check UnixMode 600 best-effort).</summary>
    public static Keyfile LoadKeyfile(string path)
    {
        var kf = new Keyfile();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var val = line[(eq + 1)..].Trim();
            switch (line[..eq].Trim().ToLowerInvariant())
            {
                case "a": kf.A = FromHex(val); break;
                case "b": kf.B = FromHex(val); break;
                case "kdata": kf.Kdata = FromHex(val); break;
                case "sign": kf.Sign = FromHex(val); break;
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var mode = File.GetUnixFileMode(path);
                if ((mode & (UnixFileMode.GroupRead |
                             UnixFileMode.OtherRead)) != 0)
                    Console.Error.WriteLine(
                        $"edenpack: avertissement : {path} lisible " +
                        "par groupe/autres — chmod 600 conseillé");
            }
            catch (Exception) { /* FS sans unix mode — best-effort */ }
        }
        return kf;
    }

    /// <summary>Écrit/remplace un champ « clef=valeur » du keyfile
    /// (le crée chmod 600 s'il n'existe pas).</summary>
    public static void SetKeyfileField(string path, string key,
        string hexValue)
    {
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList() : new List<string>();
        var idx = lines.FindIndex(l => l.TrimStart()
            .StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
        var line = key + "=" + hexValue;
        if (idx >= 0) lines[idx] = line; else lines.Add(line);
        File.WriteAllLines(path, lines);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception) { }
        }
    }

    /// <summary>hex → bytes (espaces tolérés).</summary>
    public static byte[] FromHex(string s)
    {
        var clean = new string(
            s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return Convert.FromHexString(clean);
    }

    /// <summary>bytes → hex minuscule.</summary>
    public static string ToHex(ReadOnlySpan<byte> b) =>
        Convert.ToHexString(b).ToLowerInvariant();
}
