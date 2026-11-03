using System.Security.Cryptography;
using EdenClient.Vfs;

namespace EdenPack;

/// <summary>CLI edenpack — build/list/verify/extract/info/diff/
/// apply/install de packs EPK v1 (spec docs/packaging/EPK-FORMAT.md).</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            return Usage(args.Length == 0 ? 2 : 0);
        try
        {
            return args[0] switch
            {
                "build" => CmdBuild(args[1..]),
                "list" => CmdList(args[1..]),
                "verify" => CmdVerify(args[1..]),
                "extract" => CmdExtract(args[1..]),
                "info" => CmdInfo(args[1..]),
                "diff" => CmdDiff(args[1..]),
                "apply" => CmdApply(args[1..]),
                "install" => CmdInstall(args[1..]),
                "keygen" => CmdKeygen(args[1..]),
                "bake-tables" => CmdBakeTables(args[1..]),
                "bake" => CmdBake(args[1..]),
                "bake-tex" => CmdBakeTex(args[1..]),
                _ => Usage(2, $"commande inconnue : {args[0]}"),
            };
        }
        catch (Exception ex) when (ex is not UsageException)
        {
            Console.Error.WriteLine(
                Environment.GetEnvironmentVariable("EDENPACK_DEBUG") != null
                    ? ex.ToString()
                    : $"edenpack: erreur : {ex.Message}");
            return 1;
        }
        catch (UsageException ex)
        {
            return Usage(2, ex.Message);
        }
    }

    private sealed class UsageException(string msg) : Exception(msg);

    private static int Usage(int code, string? err = null)
    {
        if (err != null)
            Console.Error.WriteLine($"edenpack: {err}");
        var tw = code == 0 ? Console.Out : Console.Error;
        tw.WriteLine("""
            edenpack — packs EPK v1 (spec docs/packaging/EPK-FORMAT.md)

            usage :
              edenpack build <srcdir> <out.epk> [options]
                  --level N        niveau zstd 1-22 (défaut 19)
                  --version N      packVersion u64 (défaut 1)
                  --pack-id GUID   id du pack ; fixe + mêmes entrées =
                                   build bit-à-bit déterministe. Sans cette
                                   option un GUID aléatoire est généré ->
                                   sortie NON déterministe.
                  --include G      glob d'inclusion, répétable (seules les
                                   entrées matchées entrent — ex. bootstrap)
                  --exclude G      glob d'exclusion, répétable
                                   ('*' dans un segment, '**' traverse,
                                   insensible à la casse)
                  --exclude-file F fichier de globs (une règle/ligne,
                                   '#' = commentaire)
                  --prev OLD.epk   conserve cryptoRev si sha256 identique,
                                   rev+1 si contenu modifié
                  --encrypt --keyfile F
                                   chiffre les chunks (AES-256-GCM) ;
                                   keyfile : A= B= (requis) kdata=
                                   (générée et ré-écrite si absente)
                  --sign-key F     signe l'index — clé privée ECDSA
                                   P-256, PEM ou DER hex (un keyfile
                                   avec sign= marche aussi)
                  --sign-keyid N   keyId forcé (sinon dérivé de la clé)
              edenpack build --native <outdir> [options]   (ADR-0005, mode natif)
                  corpus/fr-full + godot_project/gamedata -> bootstrap.epk,
                  eden-core.epk, eden-assets.epk, eden-audio.epk (fichiers
                  natifs tels quels ; DDS BC* raw, le reste zstd) +
                  <outdir>/provenance.tsv. ÉCHOUE si une entrée du corpus
                  n'est pas conforme à GameDataFileList.txt (taille+CRC32C).
                  --corpus D       défaut <repo>/corpus/fr-full
                  --gamedata D     défaut <repo>/eden_godot/godot_project/gamedata
                  --manifest F     défaut ~/Téléchargements/EDENFR/GameDataFileList.txt
                  --verify-tsv F   défaut <repo>/docs/provenance/edenfr-verify-2026-11-02.tsv
                  --extract-tsv F  défaut <repo>/docs/provenance/edenfr-extract-2026-11-02.tsv
                  --sign-key F [--sign-keyid N]   requis (--allow-unsigned = test)
                  --keyfile F      chiffre eden-core et eden-assets
                  --encrypt-packs a,b   packs chiffrés (défaut eden-core,eden-assets)
                  --prev-dir D     release précédente (<D>/<pack>.epk) : cryptoRev
                                   conservé/incrémenté (requis pour une v2 chiffrée)
                  --only a,b       ne construit que ces packs
                  --version N  --level N (défaut 12)
              edenpack keygen <dir>   clés de TEST (keyfile.txt A/B, sign.key,
                                      sign.pub en hex DER) — jamais de prod
              edenpack list <pack.epk> [--prefix P]
              edenpack verify <pack.epk> [--keyfile F] [--pubkey F]
                                        [--tamper-test]
                  complet : structure+index (+ signature si --pubkey,
                  + chaque tag GCM et sha256 si --keyfile sur ENCRYPTED ;
                  sans keyfile = structure+index seulement)
                  --tamper-test : 4 mutations doivent toutes être
                  détectées (index, tag, blob, signature)
              edenpack extract <pack.epk> <outdir> [--prefix P]
                                   [--keyfile F]
              edenpack info <pack.epk>
              edenpack diff <old.epk> <new.epk> <patch.epk>
                  patch = entrées sha256/cryptoRev différentes, blobs
                  copiés byte-à-byte (pas de recompression)
              edenpack apply <base.epk> <patch.epk> <out.epk>
                  [--sign-key F --sign-keyid N]
                  fusion base+patch (patch gagne), blobs recopiés ;
                  --sign-key re-signe l'index fusionné (sinon non signé)
              edenpack install <srcDir> <gamedataDir>
                  [--sign-key F --sign-keyid N]
                  installe les *.epk de srcDir + current.json,
                  bascule atomique du pointeur, purge hors N/N-1 ;
                  --sign-key signe le pointeur (ECDSA DER)
              edenpack bake-tables <dir|file.ini> [--check] [--delete]
                  bake chaque .ini en <f>.ini.inib (format ITB1) ;
                  --check = équivalence cellule par cellule (gate)
              edenpack bake <spec> <outdir> [--godot P] [--project D]
                  bake GLB → .scn via le client headless
                  (godot --headless -- --bake-protos=spec,outdir)
              edenpack bake-tex <srcDir> <dstDir> [--fmt bc7|webp]
                  [--jobs N] [--godot P] [--project D]
                  bake textures PNG → DDS BC7+mips (ou webp) via le
                  client headless (--bake-tex=src,dst,fmt,jobs)
            """);
        return code;
    }

    // ---------- build ----------

    private static int CmdBuild(string[] a)
    {
        if (a.Contains("--native")) return CmdBuildNative(a);
        var pos = new List<string>();
        var excludes = new List<string>();
        var includes = new List<string>();
        var level = 19;
        ulong version = 1;
        Guid? packId = null;
        string? prev = null, keyfile = null, signKey = null;
        uint? signKeyId = null;
        var encrypt = false;
        for (var i = 0; i < a.Length; i++)
        {
            var v = Opt(a, ref i, "--level");
            if (v != null)
            {
                if (!int.TryParse(v, out level) || level < 1 || level > 22)
                    throw new UsageException("--level : entier 1-22");
                continue;
            }
            if ((v = Opt(a, ref i, "--version")) != null)
            {
                if (!ulong.TryParse(v, out version))
                    throw new UsageException("--version : u64");
                continue;
            }
            if ((v = Opt(a, ref i, "--pack-id")) != null)
            {
                if (!Guid.TryParse(v, out var g))
                    throw new UsageException("--pack-id : GUID invalide");
                packId = g;
                continue;
            }
            if ((v = Opt(a, ref i, "--include")) != null)
            {
                includes.Add(v);
                continue;
            }
            if ((v = Opt(a, ref i, "--exclude")) != null)
            {
                excludes.Add(v);
                continue;
            }
            if ((v = Opt(a, ref i, "--exclude-file")) != null)
            {
                foreach (var line in File.ReadLines(v))
                {
                    var t = line.Trim();
                    if (t.Length > 0 && !t.StartsWith('#'))
                        excludes.Add(t);
                }
                continue;
            }
            if ((v = Opt(a, ref i, "--prev")) != null)
            {
                prev = v;
                continue;
            }
            if (a[i] == "--encrypt")
            {
                encrypt = true;
                continue;
            }
            if ((v = Opt(a, ref i, "--keyfile")) != null)
            {
                keyfile = v;
                continue;
            }
            if ((v = Opt(a, ref i, "--sign-key")) != null)
            {
                signKey = v;
                continue;
            }
            if ((v = Opt(a, ref i, "--sign-keyid")) != null)
            {
                if (!uint.TryParse(v, out var kid))
                    throw new UsageException("--sign-keyid : u32");
                signKeyId = kid;
                continue;
            }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 2)
            throw new UsageException(
                "build attend <srcdir> <out.epk>");

        EpkWriter.CryptoSpec? crypto = null;
        if (encrypt)
        {
            if (keyfile == null)
                throw new UsageException(
                    "--encrypt exige --keyfile F");
            var kf = EpkCrypto.LoadKeyfile(keyfile);
            if (kf.A == null || kf.B == null)
                throw new UsageException(
                    "--encrypt : le keyfile doit contenir " +
                    "A=<hex64> et B=<hex64>");
            if (kf.Kdata == null)
            {
                kf.Kdata = EpkCrypto.GenKey();
                EpkCrypto.SetKeyfileField(keyfile, "kdata",
                    EpkCrypto.ToHex(kf.Kdata));
                Console.Error.WriteLine(
                    $"edenpack: kdata générée -> ajoutée à {keyfile}");
            }
            crypto = new EpkWriter.CryptoSpec
            {
                Kdata = kf.Kdata, PartA = kf.A, PartB = kf.B,
            };
        }
        if (signKey != null || signKeyId != null)
        {
            if (signKey == null)
                throw new UsageException(
                    "--sign-keyid exige --sign-key");
            crypto ??= new EpkWriter.CryptoSpec();
            crypto.SignKeyDer = ResolveSignKey(signKey);
            crypto.SignKeyId = signKeyId;
        }
        EpkWriter.Build(pos[0], pos[1], level, version, packId,
            excludes, prev, includes, crypto);
        return 0;
    }

    private static int CmdBuildNative(string[] a)
    {
        var o = new NativeBuild.Options();
        var repo = FindRepoRoot();
        o.Corpus = Path.Combine(repo, "corpus", "fr-full");
        o.Gamedata = Path.Combine(repo, "eden_godot", "godot_project",
            "gamedata");
        o.Manifest = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile), "Téléchargements",
            "EDENFR", "GameDataFileList.txt");
        o.VerifyTsv = Path.Combine(repo, "docs", "provenance",
            "edenfr-verify-2026-11-02.tsv");
        o.ExtractTsv = Path.Combine(repo, "docs", "provenance",
            "edenfr-extract-2026-11-02.tsv");
        var pos = new List<string>();
        for (var i = 0; i < a.Length; i++)
        {
            string? v;
            if (a[i] == "--native") continue;
            if (a[i] == "--allow-unsigned") { o.AllowUnsigned = true; continue; }
            if ((v = Opt(a, ref i, "--corpus")) != null) { o.Corpus = v; continue; }
            if ((v = Opt(a, ref i, "--gamedata")) != null) { o.Gamedata = v; continue; }
            if ((v = Opt(a, ref i, "--manifest")) != null) { o.Manifest = v; continue; }
            if ((v = Opt(a, ref i, "--verify-tsv")) != null) { o.VerifyTsv = v; continue; }
            if ((v = Opt(a, ref i, "--extract-tsv")) != null) { o.ExtractTsv = v; continue; }
            if ((v = Opt(a, ref i, "--keyfile")) != null) { o.Keyfile = v; continue; }
            if ((v = Opt(a, ref i, "--prev-dir")) != null) { o.PrevDir = v; continue; }
            if ((v = Opt(a, ref i, "--sign-key")) != null) { o.SignKey = v; continue; }
            if ((v = Opt(a, ref i, "--sign-keyid")) != null)
            { o.SignKeyId = uint.Parse(v); continue; }
            if ((v = Opt(a, ref i, "--version")) != null)
            { o.Version = ulong.Parse(v); continue; }
            if ((v = Opt(a, ref i, "--level")) != null)
            { o.Level = int.Parse(v); continue; }
            if ((v = Opt(a, ref i, "--only")) != null)
            { o.Only = new(v.Split(',')); continue; }
            if ((v = Opt(a, ref i, "--encrypt-packs")) != null)
            { o.Encrypt = new(v.Split(',')); continue; }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 1)
            throw new UsageException("build --native attend <outdir>");
        foreach (var n in o.Only.Concat(o.Encrypt))
            if (!NativeBuild.AllPacks.Contains(n))
                throw new UsageException($"pack inconnu : {n}");
        o.OutDir = pos[0];
        return NativeBuild.Run(o);
    }

    /// <summary>Racine du dépôt (dossier portant AGENTS.md) en
    /// remontant depuis le cwd, puis depuis l'exécutable.</summary>
    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(d.FullName, "corpus")))
                    return d.FullName;
        return Directory.GetCurrentDirectory();
    }

    /// <summary>Clés de TEST : keyfile (A, B, kdata), sign.key (PKCS8
    /// DER hex), sign.pub (SPKI DER hex).</summary>
    private static int CmdKeygen(string[] a)
    {
        if (a.Length != 1 || a[0].StartsWith("--"))
            throw new UsageException("keygen attend <dir>");
        Directory.CreateDirectory(a[0]);
        var kf = Path.Combine(a[0], "keyfile.txt");
        if (File.Exists(kf))
            throw new InvalidDataException($"{kf} existe déjà");
        EpkCrypto.SetKeyfileField(kf, "A", EpkCrypto.ToHex(EpkCrypto.GenPart()));
        EpkCrypto.SetKeyfileField(kf, "B", EpkCrypto.ToHex(EpkCrypto.GenPart()));
        EpkCrypto.SetKeyfileField(kf, "kdata", EpkCrypto.ToHex(EpkCrypto.GenKey()));
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var priv = Path.Combine(a[0], "sign.key");
        File.WriteAllText(priv, EpkCrypto.ToHex(ec.ExportPkcs8PrivateKey()));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(priv,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(Path.Combine(a[0], "sign.pub"),
            EpkCrypto.ToHex(ec.ExportSubjectPublicKeyInfo()));
        Console.Out.WriteLine($"clés de TEST écrites dans {a[0]}");
        return 0;
    }

    /// <summary>Clé privée de signature : PEM ou DER hex direct, ou
    /// keyfile portant un champ sign=.</summary>
    private static byte[] ResolveSignKey(string path)
    {
        try { return EpkCrypto.LoadPrivateKeyDer(path); }
        catch (Exception)
        {
            var kf = EpkCrypto.LoadKeyfile(path);
            if (kf.Sign != null) return kf.Sign;
            throw;
        }
    }

    /// <summary>Lit la valeur d'une option `--nom V` ou `--nom=V` ;
    /// null si le token courant n'est pas cette option.</summary>
    private static string? Opt(string[] a, ref int i, string name)
    {
        if (a[i] == name)
        {
            if (i + 1 >= a.Length)
                throw new UsageException($"{name} : valeur manquante");
            return a[++i];
        }
        return a[i].StartsWith(name + "=", StringComparison.Ordinal)
            ? a[i][(name.Length + 1)..]
            : null;
    }

    private static string? PrefixOpt(string[] a, int from)
    {
        for (var i = from; i < a.Length; i++)
        {
            var v = Opt(a, ref i, "--prefix");
            if (v != null) return v;
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
        }
        return null;
    }

    private static IEnumerable<string> Select(EpkReader r, string? p) =>
        p == null ? r.Paths : r.ListDir(p);

    /// <summary>Injecte le décrypteur d'un pack ENCRYPTED depuis un
    /// keyfile (A+B → unwrap ; kdata seul accepté en repli).</summary>
    private static void InjectKeyfile(EpkReader r, string keyfile)
    {
        var kf = EpkCrypto.LoadKeyfile(keyfile);
        var kd = kf.A != null && kf.B != null
            ? EpkCrypto.UnwrapKdata(r.WrapBlock.Span, kf.A, kf.B,
                r.PackId)
            : kf.Kdata ?? throw new InvalidDataException(
                "keyfile sans A/B ni kdata");
        r.Decryptor = EpkCrypto.MakeDecryptor(kd);
    }

    // ---------- list ----------

    private static int CmdList(string[] a)
    {
        if (a.Length < 1 || a[0].StartsWith("--"))
            throw new UsageException("list attend <pack.epk>");
        using var r = EpkReader.Open(a[0]);
        var n = 0;
        foreach (var p in Select(r, PrefixOpt(a, 1)))
        {
            Console.Out.WriteLine(p);
            n++;
        }
        Console.Error.WriteLine($"{n} entrée(s)");
        return 0;
    }

    // ---------- verify ----------

    private static int CmdVerify(string[] a)
    {
        var pos = new List<string>();
        string? keyfile = null, pubkey = null;
        var tamper = false;
        for (var i = 0; i < a.Length; i++)
        {
            string? v;
            if ((v = Opt(a, ref i, "--keyfile")) != null)
            { keyfile = v; continue; }
            if ((v = Opt(a, ref i, "--pubkey")) != null)
            { pubkey = v; continue; }
            if (a[i] == "--tamper-test") { tamper = true; continue; }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 1)
            throw new UsageException("verify attend <pack.epk>");
        var fails = VerifyPack(pos[0], keyfile, pubkey, Console.Out);
        if (tamper)
            fails += TamperTest(pos[0], keyfile, pubkey);
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Vérification complète du pack : structure+index,
    /// signature si --pubkey, puis CHAQUE entrée (tags GCM vérifiés
    /// au decrypt + sha256 du clair) — jamais d'échantillon.
    /// Retourne le nombre d'échecs.</summary>
    private static int VerifyPack(string path, string? keyfile,
        string? pubkey, TextWriter log)
    {
        var fails = new List<string>();
        EpkReader? r = null;
        try { r = EpkReader.Open(path); }
        catch (Exception ex)
        {
            log.WriteLine($"ÉCHEC structure : {ex.Message}");
            return 1;
        }
        using (r)
        {
            var enc = r.Flags.HasFlag(EpkFormat.PackFlags.Encrypted);
            if (r.Flags.HasFlag(EpkFormat.PackFlags.Signed))
            {
                if (pubkey != null)
                {
                    var pub = EpkCrypto.LoadPublicKeyDer(pubkey);
                    if (r.Signature != null && EpkCrypto.VerifyIndex(
                            pub, r.SignedRegion.Span, r.Signature))
                        log.WriteLine(
                            $"signature OK (keyId {r.KeyId})");
                    else
                        fails.Add("signature INVALIDE");
                }
                else
                {
                    log.WriteLine(
                        "SIGNED sans --pubkey : signature " +
                        "non vérifiée");
                }
            }
            if (enc)
            {
                if (keyfile != null)
                {
                    try { InjectKeyfile(r, keyfile); }
                    catch (Exception ex)
                    {
                        fails.Add($"unwrap/decryptor : {ex.Message}");
                    }
                }
                else
                {
                    log.WriteLine(
                        "ENCRYPTED sans --keyfile : structure+index " +
                        "vérifiés seulement — contenu non vérifié");
                }
            }
            if (!enc || r.Decryptor != null)
            {
                var ok = 0;
                foreach (var p in r.Paths)
                {
                    try
                    {
                        r.TryGetEntry(p, out var e);
                        // Tombstone = suppression patch : pas de blob
                        // à vérifier (l'index/la signature la couvrent).
                        if (e.Codec == EpkFormat.BlobCodec.Tombstone)
                        { ok++; continue; }
                        var data = r.ReadAllBytes(p)
                            ?? throw new InvalidDataException(
                                "entrée absente");
                        if (SHA256.HashData(data).AsSpan()
                            .SequenceEqual(e.Sha256))
                            ok++;
                        else
                            fails.Add($"{p} : sha256 mismatch");
                    }
                    catch (Exception ex)
                    {
                        fails.Add($"{p} : {ex.Message}");
                    }
                }
                log.WriteLine(
                    $"{r.Paths.Count} entrées, {ok} OK, " +
                    $"{r.Paths.Count - ok} échec(s)");
            }
            else
            {
                log.WriteLine(
                    $"{r.Paths.Count} entrées — contenu non vérifié");
            }
        }
        foreach (var f in fails)
            log.WriteLine($"  ÉCHEC {f}");
        return fails.Count;
    }

    /// <summary>Mutations ciblées sur une copie : chacune doit être
    /// détectée par VerifyPack. Retourne le nb de cas NON détectés.
    /// Un cas sans prérequis de détection (tag/blob d'un pack
    /// chiffré sans --keyfile, sig sans --pubkey) est listé « n/a »
    /// plutôt que compté raté.</summary>
    private static int TamperTest(string path, string? keyfile,
        string? pubkey)
    {
        var fileLen = new FileInfo(path).Length;
        using var r = EpkReader.Open(path);
        var enc = r.Flags.HasFlag(EpkFormat.PackFlags.Encrypted);
        var sgn = r.Flags.HasFlag(EpkFormat.PackFlags.Signed);
        var canContent = !enc || keyfile != null;
        var cases = new List<(string nom, long off)>();
        var indexOff = fileLen - r.RawIndex.Length;

        // 1. index : frame zstd → flip dans le magic = structure KO
        //    à coup sûr ; index nu → champ sha256 de l'entrée 0
        //    (détecté au sha du contenu ou à la signature).
        if (r.Flags.HasFlag(EpkFormat.PackFlags.IndexZstd))
            cases.Add(("index", indexOff));
        else if (sgn || canContent)
            cases.Add(("index", indexOff + 4 + (r.Paths.Count > 0
                ? (long)ReadU32At(path, indexOff) + 48 : 0)));
        else
            Console.Out.WriteLine(
                "tamper index : n/a (non signé, contenu non vérifié)");

        // 2-3. blob + tag GCM : 1re entrée à blob non vide — le
        // contenu doit être vérifiable pour qu'un flip soit vu.
        EpkEntry first = default;
        var hasBlob = false;
        foreach (var p in r.Paths)
            if (r.TryGetEntry(p, out var e)
                && e.BlobSize > (ulong)(EpkFormat.ChunkHeaderSize
                    + EpkFormat.GcmTagSize))
            { first = e; hasBlob = true; break; }
        if (hasBlob && canContent)
        {
            var cSize = ReadU32At(path, (long)first.BlobOffset);
            cases.Add(("blob", (long)first.BlobOffset
                + EpkFormat.ChunkHeaderSize + 1));
            if (enc)
                cases.Add(("tag GCM", (long)first.BlobOffset
                    + EpkFormat.ChunkHeaderSize + cSize - 1));
        }
        else if (enc)
            Console.Out.WriteLine(
                "tamper blob/tag : n/a (pas de --keyfile)");

        // 4. signature : octet dans le bloc signature.
        if (sgn && pubkey != null)
            cases.Add(("signature", EpkFormat.HeaderSize
                + (enc ? EpkFormat.WrapBlockSize : 0) + 8));
        else if (sgn)
            Console.Out.WriteLine(
                "tamper signature : n/a (pas de --pubkey)");

        var missed = 0;
        foreach (var (nom, off) in cases)
        {
            var tmp = path + $".tamper-{nom.Replace(' ', '_')}";
            try
            {
                File.Copy(path, tmp, true);
                Flip(tmp, off);
                var n = VerifyPack(tmp, keyfile, pubkey, Console.Out);
                Console.Out.WriteLine(
                    $"tamper {nom} @{off} : " +
                    (n > 0 ? "détecté" : "NON DÉTECTÉ"));
                if (n == 0) missed++;
            }
            finally
            {
                try { File.Delete(tmp); } catch (Exception) { }
            }
        }
        return missed;
    }

    private static uint ReadU32At(string path, long off)
    {
        var b = new byte[4];
        using var fs = File.OpenRead(path);
        fs.Position = off;
        fs.Read(b);
        return BitConverter.ToUInt32(b);
    }

    /// <summary>Flip 1 octet à la position donnée.</summary>
    private static void Flip(string path, long off)
    {
        using var fs = File.Open(path, FileMode.Open,
            FileAccess.ReadWrite);
        fs.Position = off;
        var b = fs.ReadByte();
        fs.Position = off;
        fs.WriteByte((byte)(b ^ 0xFF));
    }

    // ---------- extract ----------

    private static int CmdExtract(string[] a)
    {
        var pos = new List<string>();
        string? keyfile = null, prefix = null;
        for (var i = 0; i < a.Length; i++)
        {
            string? v;
            if ((v = Opt(a, ref i, "--keyfile")) != null)
            { keyfile = v; continue; }
            if ((v = Opt(a, ref i, "--prefix")) != null)
            { prefix = v; continue; }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 2)
            throw new UsageException(
                "extract attend <pack.epk> <outdir>");
        using var r = EpkReader.Open(pos[0]);
        if (r.Flags.HasFlag(EpkFormat.PackFlags.Encrypted))
        {
            if (keyfile == null)
                throw new UsageException(
                    "pack ENCRYPTED : extract exige --keyfile F");
            InjectKeyfile(r, keyfile);
        }
        var outdir = pos[1];
        var n = 0;
        foreach (var p in Select(r, prefix))
        {
            var rel = p.Replace('/', Path.DirectorySeparatorChar);
            if (rel.StartsWith("..", StringComparison.Ordinal)
                || rel.Contains($"{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}")
                || Path.IsPathRooted(rel))
                throw new InvalidDataException(
                    $"chemin hors outdir refusé : {p}");
            var dst = Path.Combine(outdir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllBytes(dst,
                r.ReadAllBytes(p) ?? Array.Empty<byte>());
            n++;
        }
        Console.Out.WriteLine($"{n} fichier(s) extrait(s) -> {outdir}");
        return 0;
    }

    // ---------- diff / apply / install ----------

    private static int CmdDiff(string[] a)
    {
        if (a.Length != 3 || a.Any(t => t.StartsWith("--")))
            throw new UsageException(
                "diff attend <old.epk> <new.epk> <patch.epk>");
        PackOps.Diff(a[0], a[1], a[2]);
        return 0;
    }

    private static int CmdApply(string[] a)
    {
        var pos = new List<string>();
        string? signKey = null;
        uint? signKeyId = null;
        for (var i = 0; i < a.Length; i++)
        {
            string? v;
            if ((v = Opt(a, ref i, "--sign-key")) != null)
            { signKey = v; continue; }
            if ((v = Opt(a, ref i, "--sign-keyid")) != null)
            { signKeyId = uint.Parse(v); continue; }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 3)
            throw new UsageException(
                "apply attend <base.epk> <patch.epk> <out.epk>");
        PackOps.Apply(pos[0], pos[1], pos[2],
            signKey != null ? ResolveSignKey(signKey) : null, signKeyId);
        return 0;
    }

    private static int CmdInstall(string[] a)
    {
        var pos = new List<string>();
        string? signKey = null;
        uint? signKeyId = null;
        for (var i = 0; i < a.Length; i++)
        {
            string? v;
            if ((v = Opt(a, ref i, "--sign-key")) != null)
            { signKey = v; continue; }
            if ((v = Opt(a, ref i, "--sign-keyid")) != null)
            { signKeyId = uint.Parse(v); continue; }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 2)
            throw new UsageException(
                "install attend <srcDir> <gamedataDir>");
        byte[]? der = signKey != null ? ResolveSignKey(signKey) : null;
        PackOps.Install(pos[0], pos[1], der, signKeyId);
        return 0;
    }

    // ---------- info ----------

    private static int CmdInfo(string[] a)
    {
        if (a.Length != 1 || a[0].StartsWith("--"))
            throw new UsageException("info attend <pack.epk>");
        var fileLen = new FileInfo(a[0]).Length;
        using var r = EpkReader.Open(a[0]);
        var flags = new List<string>();
        if (r.Flags.HasFlag(EpkFormat.PackFlags.Encrypted))
            flags.Add("ENCRYPTED");
        if (r.Flags.HasFlag(EpkFormat.PackFlags.Signed))
            flags.Add("SIGNED");
        if (r.Flags.HasFlag(EpkFormat.PackFlags.IndexZstd))
            flags.Add("INDEX_ZSTD");
        var indexC = r.RawIndex.Length;
        Console.Out.WriteLine($"""
            {a[0]} — {fileLen / 1048576.0:F1} Mio
            flags        : {(uint)r.Flags} ({string.Join("|", flags)})
            packVersion  : {r.PackVersion}
            packId       : {new Guid(r.PackId)}
            chunkShift   : {r.ChunkShift} (chunk = {1 << r.ChunkShift} o)
            entrées      : {r.Paths.Count}
            index        : @{fileLen - indexC} ({indexC} o sur disque)
            """);
        if (r.Flags.HasFlag(EpkFormat.PackFlags.Signed))
            Console.Out.WriteLine($"signature    : keyId {r.KeyId}");

        var top = new List<EpkEntry>(r.Paths.Count);
        long rawN = 0, rawB = 0, zN = 0, zB = 0;
        foreach (var p in r.Paths)
        {
            if (!r.TryGetEntry(p, out var e)) continue;
            top.Add(e);
            if (e.Codec == EpkFormat.BlobCodec.Zstd) { zN++; zB += (long)e.BlobSize; }
            else { rawN++; rawB += (long)e.BlobSize; }
        }
        Console.Out.WriteLine(
            $"codec        : zstd {zN} entrées ({zB / 1048576.0:F1} Mio stockés), " +
            $"raw {rawN} entrées ({rawB / 1048576.0:F1} Mio)");
        Console.Out.WriteLine("top-10 plus grosses entrées :");
        foreach (var e in top.OrderByDescending(e => (long)e.USize)
                     .Take(10))
            Console.Out.WriteLine(
                $"  {e.USize / 1048576.0,10:F2} Mio  {e.Codec,-4}  {e.Path}");
        return 0;
    }

    // ---------- bake-tables (.ini → .inib ITB1) ----------

    private static int CmdBakeTables(string[] a)
    {
        var check = a.Contains("--check");
        var delete = a.Contains("--delete");
        var targets = a.Where(t => !t.StartsWith("--")).ToList();
        if (targets.Count != 1)
            throw new UsageException(
                "bake-tables attend <dir|file.ini>");
        List<string> files;
        if (Directory.Exists(targets[0]))
            files = Directory.GetFiles(targets[0], "*.ini",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                }).ToList();
        else if (File.Exists(targets[0]))
            files = new List<string> { targets[0] };
        else
            throw new UsageException($"introuvable : {targets[0]}");
        files.Sort(StringComparer.Ordinal);

        int ok = 0, ko = 0, deleted = 0;
        long iniB = 0, inibB = 0, rows = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var f in files)
        {
            var inib = f + ".inib";
            try
            {
                if (delete)
                {
                    if (File.Exists(inib)) { File.Delete(inib); deleted++; }
                    continue;
                }
                var table = EdenClient.Data.IniTable.ParseFile(f);
                using (var fs = File.Create(inib))
                    EdenClient.Data.TableBin.Write(fs, table);
                iniB += new FileInfo(f).Length;
                inibB += new FileInfo(inib).Length;
                rows += table.Rows.Count;
                if (check)
                {
                    var diff = BakeDiff(table,
                        EdenClient.Data.TableBin.Read(
                            File.ReadAllBytes(inib)));
                    if (diff != null)
                    {
                        ko++;
                        Console.Out.WriteLine(
                            $"KO {Path.GetFileName(f)} : {diff}");
                        continue;
                    }
                }
                ok++;
            }
            catch (Exception ex)
            {
                ko++;
                Console.Out.WriteLine(
                    $"KO {Path.GetFileName(f)} : {ex.Message}");
            }
        }
        Console.Out.WriteLine(delete
            ? $"--delete : {deleted} .inib supprimés"
            : $"{files.Count} fichiers : {ok} OK, {ko} KO, {rows} " +
              $"rows, ini {iniB / 1048576.0:F1} → " +
              $"inib {inibB / 1048576.0:F1} Mio " +
              $"en {sw.ElapsedMilliseconds} ms");
        return ko > 0 ? 1 : 0;
    }

    private static string? BakeDiff(EdenClient.Data.IniTable t,
        EdenClient.Data.TableBin.Baked b)
    {
        if (t.Version != b.Version) return $"version {t.Version}≠{b.Version}";
        if (t.ColumnCount != b.ColCount)
            return $"colCount {t.ColumnCount}≠{b.ColCount}";
        if (t.Separator != b.Sep) return $"sep {t.Separator}≠{b.Sep}";
        if (t.Rows.Count != b.Rows.Count)
            return $"rowCount {t.Rows.Count}≠{b.Rows.Count}";
        for (var i = 0; i < t.Rows.Count; i++)
        {
            var x = t.Rows[i]; var y = b.Rows[i];
            if (x.Length != y.Length)
                return $"row {i} : cellCount {x.Length}≠{y.Length}";
            for (var c = 0; c < x.Length; c++)
                if (x[c] != y[c])
                    return $"row {i} cell {c} diffère";
        }
        return null;
    }

    // ---------- bake GLB → .scn via client headless ----------

    private static int CmdBake(string[] a)
    {
        var pos = new List<string>();
        string godot = Environment.GetEnvironmentVariable("EDEN_GODOT")
            ?? "godot";
        string? project = null;
        for (var i = 0; i < a.Length; i++)
        {
            string? v;
            if ((v = Opt(a, ref i, "--godot")) != null)
            { godot = v; continue; }
            if ((v = Opt(a, ref i, "--project")) != null)
            { project = v; continue; }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 2)
            throw new UsageException("bake attend <spec> <outdir>");
        project ??= Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "godot_project"));
        if (!Directory.Exists(project))
            throw new UsageException(
                $"projet introuvable : {project} (--project)");
        var psi = new System.Diagnostics.ProcessStartInfo(godot,
            $"--headless --path \"{project}\" -- " +
            $"--bake-protos={pos[0]},{pos[1]}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["EDEN_MIPMAPS"] = "1";
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Console.Out.Write(stdout);
        if (stderr.Length > 0)
            Console.Error.Write(stderr);
        return p.ExitCode;
    }

    // ---------- bake textures PNG → DDS/webp via client headless ----------

    private static int CmdBakeTex(string[] a)
    {
        var pos = new List<string>();
        string godot = Environment.GetEnvironmentVariable("EDEN_GODOT")
            ?? "godot";
        string? project = null;
        var fmt = "bc7";
        var jobs = 0;
        for (var i = 0; i < a.Length; i++)
        {
            string? v;
            if ((v = Opt(a, ref i, "--godot")) != null)
            { godot = v; continue; }
            if ((v = Opt(a, ref i, "--project")) != null)
            { project = v; continue; }
            if ((v = Opt(a, ref i, "--fmt")) != null)
            { fmt = v; continue; }
            if ((v = Opt(a, ref i, "--jobs")) != null)
            { jobs = int.Parse(v); continue; }
            if (a[i].StartsWith("--"))
                throw new UsageException($"option inconnue : {a[i]}");
            pos.Add(a[i]);
        }
        if (pos.Count != 2)
            throw new UsageException("bake-tex attend <srcDir> <dstDir>");
        project ??= Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "godot_project"));
        if (!Directory.Exists(project))
            throw new UsageException(
                $"projet introuvable : {project} (--project)");
        var spec = $"{Path.GetFullPath(pos[0])}," +
            $"{Path.GetFullPath(pos[1])},fmt={fmt}" +
            (jobs > 0 ? $",jobs={jobs}" : "");
        var psi = new System.Diagnostics.ProcessStartInfo(godot,
            $"--headless --path \"{project}\" -- --bake-tex={spec}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Console.Out.Write(stdout);
        if (stderr.Length > 0)
            Console.Error.Write(stderr);
        return p.ExitCode;
    }
}
