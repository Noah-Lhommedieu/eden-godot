using System.Security.Cryptography;
using EdenClient.Vfs;
using EdenPack;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Couche crypto/distribution EPK (étape 5) : dérivations HKDF,
/// wrap/unwrap Kdata, chunks GCM, signature index, build
/// chiffré+signé end-to-end, diff/apply, install atomique.
/// EpkWriter/PackOps sont liés au projet de test (internal → même
/// assembly) — tests sur le vrai chemin d'écriture.
/// </summary>
public class EpkCryptoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "epkcrypto-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly byte[] _a = EpkCrypto.GenPart();
    private readonly byte[] _b = EpkCrypto.GenPart();
    private readonly byte[] _kdata = EpkCrypto.GenKey();
    private readonly byte[] _packId = EpkCrypto.GenPart()[..16];

    public EpkCryptoTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    private static EpkEntry Entry(string path, uint rev = 0,
        ulong uSize = 100) =>
        new(Path: path, FileId: EpkFormat.FileId(path), BlobOffset: 0,
            BlobSize: 0, USize: uSize, CryptoRev: rev,
            Codec: EpkFormat.BlobCodec.Zstd,
            TexCodec: EpkFormat.TexCodec.Raw,
            Sha256: new byte[32]);

    /// <summary>Copie le pack avec 1 octet flippé — retourne le
    /// chemin de la copie mutée.</summary>
    private static string FlipCopy(string src, long off, string dst)
    {
        File.Copy(src, dst, true);
        using var fs = File.Open(dst, FileMode.Open,
            FileAccess.ReadWrite);
        fs.Position = off;
        var b = fs.ReadByte();
        fs.Position = off;
        fs.WriteByte((byte)(b ^ 0xFF));
        return dst;
    }

    // ---------- dérivations / wrap ----------

    [Fact]
    public void Hkdf_Deterministe_EtDistinct()
    {
        var k1 = EpkCrypto.DeriveKfile(_kdata, 42, 0);
        Assert.Equal(k1, EpkCrypto.DeriveKfile(_kdata, 42, 0));
        Assert.NotEqual(k1, EpkCrypto.DeriveKfile(_kdata, 43, 0));
        Assert.NotEqual(k1, EpkCrypto.DeriveKfile(_kdata, 42, 1));
        Assert.NotEqual(k1, EpkCrypto.DeriveKfile(
            EpkCrypto.GenKey(), 42, 0));
        var w1 = EpkCrypto.DeriveKwrap(_a, _b, _packId);
        Assert.Equal(w1, EpkCrypto.DeriveKwrap(_a, _b, _packId));
        Assert.NotEqual(w1,
            EpkCrypto.DeriveKwrap(_a, _b, EpkCrypto.GenPart()[..16]));
        Assert.NotEqual(w1, EpkCrypto.DeriveKwrap(_b, _a, _packId));
    }

    [Fact]
    public void Wrap_Unwrap_RoundTrip()
    {
        var blk = EpkCrypto.WrapKdata(_kdata, _a, _b, _packId);
        Assert.Equal(EpkFormat.WrapBlockSize, blk.Length);
        Assert.Equal(_kdata,
            EpkCrypto.UnwrapKdata(blk, _a, _b, _packId));
    }

    [Fact]
    public void Unwrap_MauvaisePartB_Jette()
    {
        var blk = EpkCrypto.WrapKdata(_kdata, _a, _b, _packId);
        Assert.ThrowsAny<CryptographicException>(() =>
            EpkCrypto.UnwrapKdata(blk, _a, EpkCrypto.GenPart(),
                _packId));
        blk[10] ^= 0xFF;   // bloc corrompu → tag invalide
        Assert.ThrowsAny<CryptographicException>(() =>
            EpkCrypto.UnwrapKdata(blk, _a, _b, _packId));
    }

    // ---------- chunks GCM ----------

    [Fact]
    public void ChunkCrypto_RoundTrip_EtDistincts()
    {
        var enc = EpkCrypto.MakeEncryptor(_kdata);
        var dec = EpkCrypto.MakeDecryptor(_kdata);
        var e = Entry("data/x.ini");
        var plain = RandomNumberGenerator.GetBytes(5000);
        const int chunkU = 4096;   // taille claire déclarée (AAD)

        var c0 = enc(in e, 0, chunkU, plain);
        var c1 = enc(in e, 1, chunkU, plain);
        Assert.Equal(plain.Length + EpkFormat.GcmTagSize, c0.Length);
        // nonce = compteur par chunk : même clair → cipher différent
        Assert.NotEqual(c0, c1);
        // GCM déterministe à (kdata,fileId,rev,idx) fixés → blobs
        // byte-identiques entre releases (base du diff incrémental)
        Assert.Equal(c0, enc(in e, 0, chunkU, plain));
        // cryptoRev+1 = nouvelle Kfile → sortie différente (jamais 2
        // contenus sous la même combinaison (kdata,fileId,rev))
        var e1 = e with { CryptoRev = 1 };
        Assert.NotEqual(c0, enc(in e1, 0, chunkU, plain));

        var tag = c0.AsSpan(c0.Length - EpkFormat.GcmTagSize);
        var cipher = c0.AsSpan(0, c0.Length - EpkFormat.GcmTagSize);
        Assert.Equal(plain, dec(in e, 0, chunkU, cipher, tag));
    }

    [Fact]
    public void ChunkDecrypt_TamperTagCipherAad_Jettent()
    {
        var enc = EpkCrypto.MakeEncryptor(_kdata);
        var dec = EpkCrypto.MakeDecryptor(_kdata);
        var e = Entry("data/x.ini");
        var plain = RandomNumberGenerator.GetBytes(1024);
        const int chunkU = 4096;
        var c0 = enc(in e, 0, chunkU, plain);
        var ct = c0[..^EpkFormat.GcmTagSize];
        var tag = c0[^EpkFormat.GcmTagSize..];

        var badTag = (byte[])tag.Clone();
        badTag[3] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() =>
            dec(in e, 0, chunkU, ct, badTag));
        var badCt = (byte[])ct.Clone();
        badCt[0] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() =>
            dec(in e, 0, chunkU, badCt, tag));
        // AAD : uSize / chunkIdx / fileId liés au tag
        Assert.ThrowsAny<CryptographicException>(() =>
            dec(in e, 0, chunkU + 1, ct, tag));
        Assert.ThrowsAny<CryptographicException>(() =>
            dec(in e, 1, chunkU, ct, tag));
        var other = Entry("data/y.ini");
        Assert.ThrowsAny<CryptographicException>(() =>
            dec(in other, 0, chunkU, ct, tag));
    }

    // ---------- signature ----------

    [Fact]
    public void Sign_Verify_EtRejets()
    {
        using var ecd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privDer = ecd.ExportPkcs8PrivateKey();
        var pubDer = ecd.ExportSubjectPublicKeyInfo();
        var region = RandomNumberGenerator.GetBytes(200);

        var (keyId, sig) = EpkCrypto.SignIndex(privDer, region);
        Assert.Equal(64, sig.Length);
        Assert.Equal(EpkCrypto.DefaultKeyId(pubDer), keyId);
        Assert.True(EpkCrypto.VerifyIndex(pubDer, region, sig));

        var bad = (byte[])region.Clone();
        bad[5] ^= 0xFF;
        Assert.False(EpkCrypto.VerifyIndex(pubDer, bad, sig));
        using var ecd2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(EpkCrypto.VerifyIndex(
            ecd2.ExportSubjectPublicKeyInfo(), region, sig));
        var badSig = (byte[])sig.Clone();
        badSig[0] ^= 0xFF;
        Assert.False(EpkCrypto.VerifyIndex(pubDer, region, badSig));
        Assert.False(EpkCrypto.VerifyIndex(pubDer, region, sig[..32]));
        Assert.False(
            EpkCrypto.VerifyIndex(new byte[91], region, sig));
    }

    // ---------- build chiffré+signé end-to-end ----------

    private (string outP, EpkWriter.CryptoSpec crypto, ECDsa sign,
        string keyfile) BuildEncryptedSigned(
            string name, Dictionary<string, byte[]> corpus,
            ulong version = 1, string? prev = null)
    {
        var src = Path.Combine(_dir, name + "-src");
        Directory.CreateDirectory(src);
        foreach (var (rel, data) in corpus)
        {
            var f = Path.Combine(src,
                rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllBytes(f, data);
        }
        var keyfile = Path.Combine(_dir, name + ".keys");
        File.WriteAllText(keyfile,
            $"A={EpkCrypto.ToHex(_a)}\nB={EpkCrypto.ToHex(_b)}\n" +
            $"kdata={EpkCrypto.ToHex(_kdata)}\n");
        var sign = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var outP = Path.Combine(_dir, name + ".epk");
        var crypto = new EpkWriter.CryptoSpec
        {
            PartA = _a, PartB = _b, Kdata = _kdata,
            SignKeyDer = sign.ExportPkcs8PrivateKey(),
        };
        EpkWriter.Build(src, outP, 3, version,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            new List<string>(), prev, null, crypto);
        return (outP, crypto, sign, keyfile);
    }

    private static Dictionary<string, byte[]> Corpus(int seed = 7)
    {
        var rnd = new Random(seed);
        byte[] R(int n)
        {
            var b = new byte[n];
            rnd.NextBytes(b);
            return b;
        }
        return new()
        {
            ["uidata/login.cfg"] = System.Text.Encoding.UTF8.GetBytes(
                "[login]\nbg=temple\r\n"),
            ["data/db/t_x.ini"] = R(70_000),
            ["data/a.bin"] = R(333),
            ["vide.txt"] = Array.Empty<byte>(),   // 0 chunk
        };
    }

    [Fact]
    public void Build_ChiffreSigne_VerifyEtLecture()
    {
        var corpus = Corpus();
        var (outP, crypto, sign, _) =
            BuildEncryptedSigned("rt", corpus, version: 9);
        var pub = sign.ExportSubjectPublicKeyInfo();

        using var r = EpkReader.Open(outP);
        Assert.True(r.Flags.HasFlag(EpkFormat.PackFlags.Encrypted));
        Assert.True(r.Flags.HasFlag(EpkFormat.PackFlags.Signed));
        Assert.True(r.Flags.HasFlag(EpkFormat.PackFlags.IndexZstd));
        Assert.Equal(9ul, r.PackVersion);
        Assert.NotNull(r.Signature);
        Assert.Equal(EpkCrypto.DefaultKeyId(pub), r.KeyId);
        Assert.True(EpkCrypto.VerifyIndex(pub,
            r.SignedRegion.Span, r.Signature));

        // ENCRYPTED sans decryptor → erreur claire, pas de crash muet
        Assert.Throws<InvalidDataException>(
            () => r.ReadAllBytes("data/a.bin"));

        // unwrap (bonnes parts) → lecture == source
        var kd = EpkCrypto.UnwrapKdata(r.WrapBlock.Span,
            crypto.PartA!, crypto.PartB!, r.PackId);
        Assert.Equal(_kdata, kd);
        r.Decryptor = EpkCrypto.MakeDecryptor(kd);
        foreach (var (rel, data) in corpus)
            Assert.Equal(data, r.ReadAllBytes(rel));

        Assert.ThrowsAny<CryptographicException>(() =>
            EpkCrypto.UnwrapKdata(r.WrapBlock.Span, crypto.PartA!,
                EpkCrypto.GenPart(), r.PackId));
    }

    [Fact]
    public void PackClair_OctetBlobFlippe_LectureRefusee()
    {
        var src = Path.Combine(_dir, "tp-src");
        Directory.CreateDirectory(src);
        var data = RandomNumberGenerator.GetBytes(3000);
        File.WriteAllBytes(Path.Combine(src, "a.bin"), data);
        var p = Path.Combine(_dir, "tp.epk");
        EpkWriter.Build(src, p, 19, 1, Guid.NewGuid(), new List<string>(),
            null);
        long off;
        using (var r = EpkReader.Open(p))
        {
            Assert.Equal(data, r.ReadAllBytes("a.bin"));
            r.TryGetEntry("a.bin", out var e);
            off = (long)e.BlobOffset + EpkFormat.ChunkHeaderSize + 5;
        }
        var bad = FlipCopy(p, off, Path.Combine(_dir, "tp-bad.epk"));
        using var rb = EpkReader.Open(bad);
        Assert.ThrowsAny<Exception>(() => rb.ReadAllBytes("a.bin"));
        Assert.ThrowsAny<Exception>(() =>
        {
            using var s = rb.OpenRead("a.bin")!;
            s.CopyTo(Stream.Null);
        });
    }

    [Fact]
    public void Build_Clair_Deterministe_SansFlagsCrypto()
    {
        var corpus = Corpus();
        var src = Path.Combine(_dir, "cl-src");
        Directory.CreateDirectory(src);
        foreach (var (rel, data) in corpus)
        {
            var f = Path.Combine(src,
                rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllBytes(f, data);
        }
        var pid = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var p1 = Path.Combine(_dir, "cl1.epk");
        var p2 = Path.Combine(_dir, "cl2.epk");
        EpkWriter.Build(src, p1, 19, 4, pid, new List<string>(),
            null);
        EpkWriter.Build(src, p2, 19, 4, pid, new List<string>(),
            null);
        // même entrée + même pack-id → même sortie bit-à-bit
        Assert.Equal(File.ReadAllBytes(p1), File.ReadAllBytes(p2));
        using var r = EpkReader.Open(p1);
        Assert.Equal(EpkFormat.PackFlags.IndexZstd, r.Flags);
        foreach (var (rel, data) in corpus)
            Assert.Equal(data, r.ReadAllBytes(rel));
    }

    [Fact]
    public void Tamper_PackChiffreSigne_TousDetectes()
    {
        var corpus = Corpus();
        var (outP, _, sign, _) = BuildEncryptedSigned("tp", corpus);
        var pub = sign.ExportSubjectPublicKeyInfo();
        var fileLen = new FileInfo(outP).Length;
        var packBytes = File.ReadAllBytes(outP);

        // 1. tag GCM du chunk 0 → CryptographicException au read
        using (var r0 = EpkReader.Open(outP))
        {
            r0.TryGetEntry("data/db/t_x.ini", out var e);
            var cSize = BitConverter.ToUInt32(
                packBytes[(int)e.BlobOffset..]);
            var copy = FlipCopy(outP, (long)e.BlobOffset
                + EpkFormat.ChunkHeaderSize + cSize - 1,
                outP + ".tagflip");
            using var rf = EpkReader.Open(copy);
            rf.Decryptor = EpkCrypto.MakeDecryptor(_kdata);
            Assert.ThrowsAny<CryptographicException>(
                () => rf.ReadAllBytes("data/db/t_x.ini"));
            File.Delete(copy);
        }

        // 2. corps du blob → idem (cipher altéré, tag ne passe plus)
        using (var r0 = EpkReader.Open(outP))
        {
            r0.TryGetEntry("data/db/t_x.ini", out var e);
            var copy = FlipCopy(outP, (long)e.BlobOffset
                + EpkFormat.ChunkHeaderSize + 1, outP + ".blobflip");
            using var rf = EpkReader.Open(copy);
            rf.Decryptor = EpkCrypto.MakeDecryptor(_kdata);
            Assert.ThrowsAny<CryptographicException>(
                () => rf.ReadAllBytes("data/db/t_x.ini"));
            File.Delete(copy);
        }

        // 3. octet d'index → open KO ou signature invalide
        {
            using var ri = EpkReader.Open(outP);
            var copy = FlipCopy(outP, fileLen - ri.RawIndex.Length,
                outP + ".idxflip");
            var detected = false;
            try
            {
                using var rj = EpkReader.Open(copy);
                detected = !EpkCrypto.VerifyIndex(pub,
                    rj.SignedRegion.Span, rj.Signature!);
            }
            catch (Exception) { detected = true; }
            File.Delete(copy);
            Assert.True(detected, "index flip non détecté");
        }

        // 4. octet de signature → VerifyIndex false
        {
            var copy = FlipCopy(outP, EpkFormat.HeaderSize
                + EpkFormat.WrapBlockSize + 8, outP + ".sigflip");
            using var rs = EpkReader.Open(copy);
            Assert.False(EpkCrypto.VerifyIndex(pub,
                rs.SignedRegion.Span, rs.Signature!));
            File.Delete(copy);
        }
    }

    // ---------- diff / apply / install ----------

    [Fact]
    public void DiffApply_ReproduitNew_ShaParEntree()
    {
        var v1 = Corpus();
        var v2 = Corpus();
        v2["data/a.bin"] = RandomNumberGenerator.GetBytes(333);
        v2.Remove("vide.txt");                     // suppression
        v2["data/nouveau.bin"] =
            RandomNumberGenerator.GetBytes(2048);  // ajout

        var (baseP, _, _, _) = BuildEncryptedSigned("b1", v1, 1);
        var (newP, _, _, _) =
            BuildEncryptedSigned("b2", v2, 2, baseP);

        var patchP = Path.Combine(_dir, "patch.epk");
        PackOps.Diff(baseP, newP, patchP);
        using var patch = EpkReader.Open(patchP);
        // b modifiée + ajout + tombstone (vide.txt absent de v2)
        Assert.Equal(
            new[] { "data/a.bin", "data/nouveau.bin", "vide.txt" },
            patch.Paths.ToArray());
        Assert.True(patch.Flags.HasFlag(EpkFormat.PackFlags.Encrypted));
        patch.TryGetEntry("data/a.bin", out var pa);
        Assert.Equal(1u, pa.CryptoRev);   // modifié → rev+1
        patch.TryGetEntry("vide.txt", out var pt);
        Assert.Equal(EpkFormat.BlobCodec.Tombstone, pt.Codec);

        var outP = Path.Combine(_dir, "merged.epk");
        PackOps.Apply(baseP, patchP, outP);
        using var merged = EpkReader.Open(outP);
        using var nouveau = EpkReader.Open(newP);
        Assert.Equal(nouveau.PackVersion, merged.PackVersion);
        Assert.Equal(nouveau.PackId, merged.PackId);
        merged.Decryptor = EpkCrypto.MakeDecryptor(_kdata);
        nouveau.Decryptor = EpkCrypto.MakeDecryptor(_kdata);
        foreach (var p in nouveau.Paths)
        {
            nouveau.TryGetEntry(p, out var en);
            merged.TryGetEntry(p, out var em);
            Assert.Equal(en.Sha256, em.Sha256);
            Assert.Equal(en.CryptoRev, em.CryptoRev);
            Assert.Equal(nouveau.ReadAllBytes(p),
                merged.ReadAllBytes(p));
            Assert.Equal(v2[p], merged.ReadAllBytes(p));
        }
        // Tombstone appliquée : vide.txt n'existe plus dans merged.
        Assert.False(merged.TryGetEntry("vide.txt", out _));
        Assert.Null(merged.ReadAllBytes("vide.txt"));
    }

    [Fact]
    public void Install_SetAtomique_EtPurgeN1()
    {
        var corpus = Corpus();
        var (p1, _, _, _) = BuildEncryptedSigned("s1", corpus, 1);
        var src1 = Path.Combine(_dir, "set1");
        Directory.CreateDirectory(src1);
        File.Copy(p1, Path.Combine(src1, "eden-core.epk"));

        var dst = Path.Combine(_dir, "gamedata");
        PackOps.Install(src1, dst);
        var manPath = Path.Combine(dst, "current.json");
        var man = EpkManifest.TryRead(manPath);
        Assert.NotNull(man);
        Assert.Single(man!.Packs);
        Assert.Equal("eden-core", man.Packs[0].Name);
        Assert.Equal("eden-core.epk", man.Packs[0].File);
        Assert.True(File.Exists(Path.Combine(dst, "eden-core.epk")));
        Assert.False(Directory.Exists(Path.Combine(dst, ".tmp-1")));
        var sha = SHA256.HashData(
            File.ReadAllBytes(Path.Combine(dst, "eden-core.epk")));
        Assert.Equal(EpkCrypto.ToHex(sha), man.Packs[0].Sha256);

        // set N+1 : N-1 conservé (pack listé par l'ancien manifest)
        var src2 = Path.Combine(_dir, "set2");
        Directory.CreateDirectory(src2);
        File.Copy(p1, Path.Combine(src2, "eden-assets.epk"));
        PackOps.Install(src2, dst);
        Assert.Single(EpkManifest.TryRead(manPath)!.Packs);
        Assert.True(File.Exists(Path.Combine(dst, "eden-assets.epk")));
        Assert.True(File.Exists(Path.Combine(dst, "eden-core.epk")));

        // set N+2 : purge des packs hors N ∪ N-1
        var src3 = Path.Combine(_dir, "set3");
        Directory.CreateDirectory(src3);
        File.Copy(p1, Path.Combine(src3, "bootstrap.epk"));
        PackOps.Install(src3, dst);
        Assert.True(File.Exists(Path.Combine(dst, "bootstrap.epk")));
        Assert.True(File.Exists(Path.Combine(dst, "eden-assets.epk")));
        Assert.False(File.Exists(Path.Combine(dst, "eden-core.epk")));
    }

    // ---------- tombstones (suppressions via patch) ----------

    [Fact]
    public void Diff_Tombstone_ApplySupprimeEtOmbre()
    {
        // base : a + b + c ; new : a + b' (c supprimé, a identique)
        var dirA = Path.Combine(_dir, "tb-a");
        var dirB = Path.Combine(_dir, "tb-b");
        foreach (var (d, files) in new (string, (string, string)[])[]
        {
            (dirA, new[] { ("data/a.ini", "SAME"),
                           ("data/b.ini", "OLD"),
                           ("data/c.ini", "DEAD") }),
            (dirB, new[] { ("data/a.ini", "SAME"),
                           ("data/b.ini", "NEW") }),
        })
            foreach (var (rel, txt) in files)
            {
                var f = Path.Combine(d,
                    rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                File.WriteAllText(f, txt);
            }
        var baseP = Path.Combine(_dir, "tb-base.epk");
        var newP = Path.Combine(_dir, "tb-new.epk");
        EpkWriter.Build(dirA, baseP, 19, 1, null, new(), null);
        EpkWriter.Build(dirB, newP, 19, 2, null, new(), null);

        // diff : b changée + tombstone sur c
        var patch = Path.Combine(_dir, "tb-patch.epk");
        PackOps.Diff(baseP, newP, patch);
        using (var rp = EpkReader.Open(patch))
        {
            Assert.True(rp.TryGetEntry("data/c.ini", out var tc));
            Assert.Equal(EpkFormat.BlobCodec.Tombstone, tc.Codec);
            Assert.Equal(0ul, tc.BlobSize);
            Assert.Null(rp.ReadAllBytes("data/c.ini"));
            Assert.Null(rp.OpenRead("data/c.ini"));
        }

        // apply : c retirée du merge, b = version patch, a intacte
        var merged = Path.Combine(_dir, "tb-merged.epk");
        PackOps.Apply(baseP, patch, merged);
        using var rm = EpkReader.Open(merged);
        Assert.False(rm.TryGetEntry("data/c.ini", out _));
        Assert.Equal("NEW",
            System.Text.Encoding.UTF8.GetString(
                rm.ReadAllBytes("data/b.ini")!));
        Assert.Equal("SAME",
            System.Text.Encoding.UTF8.GetString(
                rm.ReadAllBytes("data/a.ini")!));
    }

    // ---------- manifest signé (pointeur anti-rollback) ----------

    [Fact]
    public void Install_ManifestSigne_VerifieEtRejette()
    {
        var corpus = Corpus();
        var (p1, _, sign, _) = BuildEncryptedSigned("ms", corpus, 1);
        var src = Path.Combine(_dir, "set-ms");
        Directory.CreateDirectory(src);
        File.Copy(p1, Path.Combine(src, "eden-core.epk"));
        var dst = Path.Combine(_dir, "gamedata-ms");

        // Install signé → sig+keyid présents dans current.json.
        PackOps.Install(src, dst, sign.ExportPkcs8PrivateKey(), 7);
        var man = EpkManifest.TryRead(
            Path.Combine(dst, "current.json"))!;
        Assert.NotNull(man.Sig);
        Assert.Equal(7u, man.KeyId);

        // Signature valide sous la bonne clé (via VerifyManifest
        // + EDEN_PACK_PUBKEY = chemin de la clé de test).
        var pubPath = Path.Combine(_dir, "ms-pub.der");
        File.WriteAllText(pubPath, EpkCrypto.ToHex(
            sign.ExportSubjectPublicKeyInfo()));
        var prev = Environment.GetEnvironmentVariable(
            "EDEN_PACK_PUBKEY");
        try
        {
            Environment.SetEnvironmentVariable(
                "EDEN_PACK_PUBKEY", pubPath);
            Assert.True(EdenVfs.VerifyManifest(man));
            // Manifest altéré (pack retiré du pointeur) → rejeté.
            var bad = EpkManifest.TryRead(
                Path.Combine(dst, "current.json"))!;
            bad.Packs.RemoveAt(0);
            Assert.False(EdenVfs.VerifyManifest(bad));
            // Sans clé publique → rejet (jamais de repli permissif).
            Environment.SetEnvironmentVariable(
                "EDEN_PACK_PUBKEY", "");
            Assert.False(EdenVfs.VerifyManifest(man));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "EDEN_PACK_PUBKEY", prev);
        }
    }
}
