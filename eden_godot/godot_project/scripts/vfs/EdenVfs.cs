using System.Security.Cryptography;
using System.Text;
#if GODOT
using Godot;
#endif

namespace EdenClient.Vfs;

/// <summary>
/// Façade unique devant les fichiers de données — lâches (dev,
/// comportement historique) et packs EPK (<see cref="EpkReader"/>).
/// docs/packaging/PLAN-PACK-EPK.md § Phase 0, espace de clés :
/// docs/packaging/EPK-FORMAT.md § « Espace de clés ».
///
/// Modèle de chemin : les call sites passent des chemins absolus
/// comme avant ; <see cref="Resolve"/> retourne soit le chemin
/// absolu réel (loose) soit une URI <c>epk://&lt;rel&gt;</c> quand
/// l'entrée n'existe que dans un pack. Toutes les API acceptent les
/// deux formes en entrée. Les URI se composent via Path.Combine
/// (« epk:// » + « data/db/x.ini » → « epk://data/db/x.ini »).
///
/// Priorité loose/pack (flag <c>EDEN_ALLOW_LOOSE</c>) :
///   debug/éditeur → loose d'abord, pack en fallback (le flux dev
///   ne change pas) ; release → pack seul, jamais de loose sauf
///   <c>EDEN_ALLOW_LOOSE=1</c> (hotfix) ; <c>EDEN_ALLOW_LOOSE=0</c>
///   force pack-seul même en debug (test du chemin packagé).
/// Kill-switch : <c>EDEN_VFS=0</c> → aucun montage, tout reste loose.
///
/// Thread-safety : lectures positionnelles via EpkReader (sûres) ;
/// <see cref="Mount"/> sous verrou, un log par pack.
/// </summary>
public static partial class EdenVfs
{
    /// <summary>Schéma des URI pack.</summary>
    public const string Scheme = "epk://";

    private static readonly object _lock = new();
    private static EpkReader[] _packs = [];
    /// <summary>Préfixes « dossier » déclarés implicitement par les
    /// entrées — un pack n'a pas d'enregistrement de dossier.</summary>
    private static HashSet<string> _dirs = new(StringComparer.Ordinal);
    private static bool _mounted;

    /// <summary>vrai quand au moins un pack est monté.</summary>
    public static bool HasPacks => Packs.Length > 0;

    /// <summary>Packs montés — montage paresseux au premier accès
    /// (couvre les points d'entrée hors boot.tscn : scènes de diag,
    /// outils) ; idempotent via <see cref="Mount"/>.</summary>
    private static EpkReader[] Packs
    {
        get
        {
            if (!_mounted) Mount();
            return _packs;
        }
    }

    /// <summary>vrai si <paramref name="path"/> est une URI pack.</summary>
    public static bool IsEpk(string path) =>
        path.StartsWith(Scheme, StringComparison.Ordinal);

    /// <summary>Clé pack d'une URI « epk://… » (non normalisée).</summary>
    public static string EpkRel(string path) => path[Scheme.Length..];

    /// <summary>vrai si les fichiers lâches priment (debug ou
    /// EDEN_ALLOW_LOOSE=1 — cf. en-tête).</summary>
    public static bool LooseAllowed { get; } = ComputeLooseAllowed();

    // ============================ montage ============================

    /// <summary>Monte les packs *.epk : chaque racine candidate de
    /// DataPaths + &lt;exe&gt;/gamedata (doublons ignorés) + liste
    /// explicite <c>$EDEN_PACKS</c> (séparateur ':', fichiers ou
    /// dossiers). Idempotent — appel unique au boot
    /// (ScreenManager._Ready). Lookup en ordre inverse du montage :
    /// le dernier pack écrase (sémantique overlay).</summary>
    public static void Mount()
    {
        lock (_lock)
        {
            if (_mounted) return;
            _mounted = true;
            if (Env("EDEN_VFS") == "0") return;
            var files = new List<string>();
            void AddDir(string dir)
            {
                try
                {
                    if (Directory.Exists(dir))
                        files.AddRange(Directory.GetFiles(dir, "*.epk")
                            .OrderBy(x => x, StringComparer
                                .OrdinalIgnoreCase));
                }
                catch (Exception) { /* racine partie, droits… */ }
            }
            // Ordre du montage = ordre des sources, nom trié dans
            // chaque dossier ; le dernier écrase au lookup.
            // EDEN_PACKS = override dev explicite : bypass manifest.
            var extra = Env("EDEN_PACKS");
            foreach (var dir in PackDirs())
            {
                if (!string.IsNullOrEmpty(extra)) { AddDir(dir); continue; }
                var man = EpkManifest.TryRead(
                    Path.Combine(dir, "current.json"));
                if (man != null && !VerifyManifest(man))
                {
                    // Pointeur signé mais invalide : anti-rollback —
                    // PAS de repli scan (il monterait les packs
                    // falsifiés). La racine est ignorée entièrement.
                    Log($"[vfs] {dir} : current.json signature " +
                        "INVALIDE — racine ignorée");
                    continue;
                }
                if (man != null)
                    foreach (var pr in man.Packs)
                        files.Add(Path.Combine(dir, pr.File));
                else
                    AddDir(dir);
            }
            if (!string.IsNullOrEmpty(extra))
            {
                foreach (var e in extra.Split(':',
                             StringSplitOptions.RemoveEmptyEntries |
                             StringSplitOptions.TrimEntries))
                {
                    try
                    {
                        if (Directory.Exists(e)) AddDir(e);
                        else if (File.Exists(e)) files.Add(e);
                    }
                    catch (Exception) { }
                }
            }
            var packs = new List<EpkReader>();
            var seen = new HashSet<string>(StringComparer
                .OrdinalIgnoreCase);
            foreach (var f in files)
            {
                string full;
                try { full = Path.GetFullPath(f); }
                catch (Exception) { continue; }
                if (!seen.Add(full)) continue;
                try
                {
                    var p = EpkReader.Open(full);
                    if (!SecurePack(p, full))
                    {
                        p.Dispose();
                        continue;
                    }
                    packs.Add(p);
                    Log($"[vfs] pack monté : {full} " +
                        $"({p.Paths.Count} entrées, v{p.PackVersion})");
                }
                catch (Exception ex)
                {
                    Log($"[vfs] montage impossible {full} : {ex.Message}");
                }
            }
            _packs = packs.ToArray();
            RebuildDirs();
            Log($"[vfs] {_packs.Length} pack(s) — loose " +
                (LooseAllowed ? "autorisé" : "désactivé"));
        }
    }

    /// <summary>Démonte tout (tests).</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            foreach (var p in _packs) p.Dispose();
            _packs = [];
            _dirs = new(StringComparer.Ordinal);
            _mounted = false;
        }
    }

    /// <summary>Dossiers scannés pour les *.epk.</summary>
    private static IEnumerable<string> PackDirs()
    {
        var dirs = new List<string>();
#if GODOT
        dirs.AddRange(DataPaths.CandidateDataRoots());
        dirs.Add(Path.Combine(DataPaths.ExeDir, "gamedata"));
#else
        var env = Env("EDEN_DATA_DIR");
        if (!string.IsNullOrEmpty(env))
        {
            dirs.Add(Path.Combine(env, "raw_assets"));
            dirs.Add(env);
        }
        dirs.Add(Path.Combine(AppContext.BaseDirectory, "gamedata"));
#endif
        return dirs.Distinct();
    }

    /// <summary>Tous les préfixes ancêtres des entrées — l'index
    /// n'enregistre pas les dossiers ; ListDir/Exists en ont besoin.</summary>
    private static void RebuildDirs()
    {
        var d = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in _packs)
        {
            foreach (var p in pack.Paths)
            {
                // Les tombstones n'enregistrent pas de dossier.
                if (pack.TryGetEntry(p, out var e)
                    && e.Codec == EpkFormat.BlobCodec.Tombstone)
                    continue;
                var i = p.IndexOf('/');
                while (i > 0)
                {
                    d.Add(p[..i]);
                    i = p.IndexOf('/', i + 1);
                }
            }
        }
        _dirs = d;
    }

    // ==================== signature & chiffrement ====================

    /// <summary>Clé publique de release embarquée (SPKI DER ECDSA
    /// P-256 — paire dev-release générée 2026-10-30, privée hors
    /// dépôt : <c>~/.config/eden/release/sign.der</c>, à régénérer
    /// en env de release dédié pour la vraie distribution). Override
    /// dev possible via <c>EDEN_PACK_PUBKEY</c> (fichier PEM ou DER
    /// hex). Une signature invalide = pack NON monté (un
    /// bootstrap.epk falsifié récolterait les identifiants).</summary>
    private static readonly byte[] EdenReleasePubKey =
    {
        0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2a, 0x86, 0x48, 0xce,
        0x3d, 0x02, 0x01, 0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d,
        0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04, 0x30, 0xb4, 0xb2,
        0x12, 0x45, 0xc2, 0x9f, 0x99, 0x68, 0x80, 0xc7, 0xcb, 0x5a,
        0xbb, 0xdb, 0xa8, 0x59, 0xcd, 0xb5, 0xbd, 0x5d, 0x91, 0x79,
        0x02, 0x87, 0xc6, 0xbd, 0x76, 0x45, 0x40, 0xba, 0x4d, 0x75,
        0xab, 0x8e, 0xb7, 0x98, 0xc1, 0xd2, 0x6c, 0xcc, 0xc3, 0x61,
        0x87, 0x40, 0x93, 0x81, 0xf5, 0x4c, 0xca, 0xed, 0xea, 0x00,
        0x2f, 0xbe, 0x91, 0x8d, 0x9c, 0x33, 0xc6, 0xea, 0x4b, 0xdb,
        0xa5,
    };

    /// <summary>Part A du wrapping Kdata — réelle (générée
    /// 2026-10-30, cf. keyfile dev hors dépôt). Obfuscation
    /// symbolique : les octets sont stockés XORés
    /// (<see cref="PartAObfKey"/>) pour ne pas laisser la clé en
    /// clair dans le binaire — la protection réelle vient de la
    /// part B servie par le serveur au login.</summary>
    private const byte PartAObfKey = 0xA7;
    private static readonly byte[] _partAObf =
    {
        0x01, 0xfa, 0x62, 0x03, 0xad, 0x40, 0xe8, 0x69, 0xc4, 0x04,
        0x42, 0xe3, 0x91, 0x26, 0x4e, 0xf0, 0xea, 0x12, 0x23, 0x71,
        0x7b, 0x49, 0xac, 0x50, 0xa5, 0x5f, 0x39, 0x3d, 0xa7, 0xe5,
        0x6f, 0x29,
    };

    private static byte[] PartA()
    {
        var b = (byte[])_partAObf.Clone();
        for (var i = 0; i < b.Length; i++) b[i] ^= PartAObfKey;
        return b;
    }

    /// <summary>Part B de dev : env <c>EDEN_PACK_B</c> (hex 64). En
    /// prod elle viendra de la session TLS authentifiée — absent ou
    /// invalide ici ⇒ lecture chiffrée en erreur claire.</summary>
    private static byte[]? PartB(out string why)
    {
        why = "absente";
        var env = Env("EDEN_PACK_B");
        if (string.IsNullOrEmpty(env)) return null;
        try { return EpkCrypto.FromHex(env); }
        catch (Exception) { why = "hex invalide"; return null; }
    }

    private static byte[]? VerifyKeyDer()
    {
        var env = Env("EDEN_PACK_PUBKEY");
        if (!string.IsNullOrEmpty(env))
        {
            try { return EpkCrypto.LoadPublicKeyDer(env); }
            catch (Exception ex)
            {
                Log($"[vfs] EDEN_PACK_PUBKEY : {ex.Message}");
            }
        }
        return EdenReleasePubKey;
    }

    /// <summary>Décrypteur qui lève une erreur explicite — pack
    /// chiffré monté sans clé : lisible en métadonnées, pas en
    /// contenu (pas de crash silencieux).</summary>
    private static EpkReader.EpkDecryptor FailDecryptor(string msg) =>
        (in EpkEntry e, int i, int u, ReadOnlySpan<byte> c,
         ReadOnlySpan<byte> t) =>
            throw new InvalidDataException(msg);

    /// <summary>Gate sécurité d'un pack ouvert : vérif signature
    /// (SIGNED) puis injection du décrypteur (ENCRYPTED).
    /// false = ne pas monter.</summary>
    /// <summary>Vérifie la signature d'un manifest lu : non signé =
    /// accepté (dev) ; signé = la signature ECDSA doit valider sous
    /// la clé publique de release (ou <c>EDEN_PACK_PUBKEY</c>).</summary>
    internal static bool VerifyManifest(EpkManifest man)
    {
        // Release : le pointeur DOIT être signé (un current.json
        // non signé peut détourner le set vers des packs choisis
        // par un tiers). Dev : non signé accepté.
        if (man.Sig == null) return !ReleaseBuild;
        var pub = VerifyKeyDer();
        return pub != null && EpkCrypto.VerifyIndex(
            pub, EpkManifest.Serialize(man), man.Sig);
    }

    private static bool SecurePack(EpkReader p, string path)
    {
        if (!p.Flags.HasFlag(EpkFormat.PackFlags.Signed)
            && ReleaseBuild)
        {
            // Release = contenu signé obligatoire : un pack non
            // signé déposé à côté des vrais injecterait des données
            // arbitraires (bootstrap = écran de login). Dev garde
            // les packs clairs montables.
            Log($"[vfs] {path} : pack NON SIGNÉ — refusé en release");
            return false;
        }
        if (p.Flags.HasFlag(EpkFormat.PackFlags.Signed))
        {
            var pub = VerifyKeyDer();
            var ok = pub != null && p.Signature != null &&
                EpkCrypto.VerifyIndex(pub, p.SignedRegion.Span,
                    p.Signature);
            if (!ok)
            {
                Log($"[vfs] {path} : signature INVALIDE (keyId " +
                    $"{p.KeyId}) — pack NON monté");
                return false;
            }
            Log($"[vfs] {path} : signature OK (keyId {p.KeyId})");
        }
        if (p.Flags.HasFlag(EpkFormat.PackFlags.Encrypted))
        {
            var b = PartB(out var why);
            if (b == null)
            {
                p.Decryptor = FailDecryptor(
                    $"{path} : part B {why} — EDEN_PACK_B requis");
                Log($"[vfs] {path} : chiffré, part B {why} " +
                    "(EDEN_PACK_B) — lectures en erreur");
            }
            else
            {
                try
                {
                    var kd = EpkCrypto.UnwrapKdata(
                        p.WrapBlock.Span, PartA(), b, p.PackId);
                    p.Decryptor = EpkCrypto.MakeDecryptor(kd);
                }
                catch (Exception ex)
                {
                    p.Decryptor = FailDecryptor(
                        $"{path} : unwrap kdata impossible " +
                        $"({ex.Message}) — part B erronée ?");
                    Log($"[vfs] {path} : unwrap impossible : " +
                        ex.Message);
                }
            }
        }
        return true;
    }

    // ============================ lookup pack ============================

    /// <summary>Entrée présente dans un pack (fichier ou dossier
    /// virtuel) — lookup en ordre inverse du montage.</summary>
    public static bool PackExists(string relPath)
    {
        var n = EpkFormat.NormalizePath(relPath);
        var packs = Packs;
        // Dernier pack qui contient le chemin décide : une tombstone
        // (suppression patch) masque les couches inférieures.
        for (var i = packs.Length - 1; i >= 0; i--)
            if (packs[i].TryGetEntry(n, out var e))
                return e.Codec != EpkFormat.BlobCodec.Tombstone;
        return _dirs.Contains(n);
    }

    /// <summary>Lookup « pack seul » : traduit abs→rel si besoin,
    /// retourne l'URI « epk://&lt;clé normalisée&gt; » ou null.</summary>
    public static string? PackResolve(string path)
    {
        var rel = IsEpk(path) ? EpkRel(path)
            : Path.IsPathRooted(path) ? ToRel(path)
            : path;
        if (rel == null || Packs.Length == 0) return null;
        var n = EpkFormat.NormalizePath(rel);
        return PackExists(n) ? Scheme + n : null;
    }

    private static byte[]? PackBytes(string rel)
    {
        var n = EpkFormat.NormalizePath(rel);
        var packs = Packs;
        for (var i = packs.Length - 1; i >= 0; i--)
            if (packs[i].TryGetEntry(n, out var e))
            {
                if (e.Codec == EpkFormat.BlobCodec.Tombstone)
                    return null;   // supprimé par ce pack — ombre
                if (packs[i].ReadAllBytes(n) is { } b) return b;
            }
        return null;
    }

    private static Stream? PackStream(string rel)
    {
        var n = EpkFormat.NormalizePath(rel);
        var packs = Packs;
        for (var i = packs.Length - 1; i >= 0; i--)
            if (packs[i].TryGetEntry(n, out var e))
            {
                if (e.Codec == EpkFormat.BlobCodec.Tombstone)
                    return null;   // supprimé par ce pack — ombre
                if (packs[i].OpenRead(n) is { } s) return s;
            }
        return null;
    }

    /// <summary>Contenu d'un préfixe dans les packs — entrées en
    /// URI « epk://… » (tous les packs, sans dédup : l'appelant
    /// déduplique par nom).</summary>
    private static IEnumerable<string> PackList(string dir,
        bool recursive)
    {
        var rel = IsEpk(dir) ? EpkRel(dir) : ToRel(dir);
        if (rel == null) yield break;
        var prefix = EpkFormat.NormalizePath(rel);
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += '/';
        var subdirs = new HashSet<string>(StringComparer.Ordinal);
        // Packs en priorité décroissante : seen déduplique et une
        // tombstone ombre le même chemin dans les packs inférieurs.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var packs = Packs;
        for (var pi = packs.Length - 1; pi >= 0; pi--)
        {
            foreach (var p in packs[pi].ListDir(prefix))
            {
                if (!seen.Add(p)) continue;
                if (packs[pi].TryGetEntry(p, out var e)
                    && e.Codec == EpkFormat.BlobCodec.Tombstone)
                    continue;
                if (recursive)
                {
                    yield return Scheme + p;
                    continue;
                }
                var rest = p[prefix.Length..];
                var cut = rest.IndexOf('/');
                if (cut < 0)
                    yield return Scheme + p;        // fichier direct
                else if (subdirs.Add(rest[..cut]))
                    yield return Scheme + prefix + rest[..cut];
            }
        }
    }

    // ============================ API publique ============================

    /// <summary>True si une lecture loose de <paramref name="path"/>
    /// est permise : <see cref="LooseAllowed"/>, ou chemin absolu sous
    /// « res:// »/« user:// » HORS des racines de données — les
    /// ressources projet non gdignorées (ui_textures thémés sans
    /// .import, polices, cache pack matérialisé) sont copiées dans
    /// le .pck / userdata en export réel : le mode pack-seul ne doit
    /// pas les masquer. Les racines de données (gamedata, assets)
    /// restent bloquées — elles ne viennent que des packs.</summary>
    private static bool LooseReadable(string path)
    {
        if (LooseAllowed) return true;
#if GODOT
        if (!Path.IsPathRooted(path)) return false;
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return false; }
        var res = ProjectSettings.GlobalizePath("res://");
        var usr = ProjectSettings.GlobalizePath("user://");
        if (!UnderRoot(full, res, out _) &&
            !UnderRoot(full, usr, out _))
            return false;
        if (UnderRoot(full, AssetsRootPath(), out _)) return false;
        foreach (var root in DataRoots())
            if (UnderRoot(full, root, out _)) return false;
        return true;
#else
        return false;
#endif
    }

    /// <summary>Résolution complète : loose d'abord quand autorisé,
    /// pack ensuite (ou seul). Retour = chemin absolu réel ou
    /// URI « epk://… », null si introuvable.</summary>
    public static string? Resolve(string path)
    {
        if (IsEpk(path)) return PackResolve(path);
        return LoosePath(path) ?? PackResolve(path);
    }

    /// <summary>Existence fichier ou dossier (loose ou pack).</summary>
    public static bool Exists(string path)
    {
        if (IsEpk(path)) return PackExists(EpkRel(path));
        return LoosePath(path) != null || PackResolve(path) != null;
    }

    /// <summary>Contenu entier — loose direct ou blob pack décodé.</summary>
    public static byte[]? ReadAllBytes(string path)
    {
        if (IsEpk(path)) return PackBytes(EpkRel(path));
        if (LooseFile(path) is { } lf)
            return File.ReadAllBytes(lf);
        var rel = Path.IsPathRooted(path) ? ToRel(path) : path;
        return rel != null ? PackBytes(rel) : null;
    }

    /// <summary>Flux lecture seule — File.OpenRead loose ou stream
    /// positionnel pack (non seekable).</summary>
    public static Stream? OpenRead(string path)
    {
        if (IsEpk(path)) return PackStream(EpkRel(path));
        if (LooseFile(path) is { } lf)
            return File.OpenRead(lf);
        var rel = Path.IsPathRooted(path) ? ToRel(path) : path;
        return rel != null ? PackStream(rel) : null;
    }

    /// <summary>Texte complet (UTF-8 par défaut, BOM snippé).</summary>
    public static string? ReadAllText(string path, Encoding? enc = null)
    {
        enc ??= Encoding.UTF8;
        if (LooseFile(path) is { } lf)
            return File.ReadAllText(lf, enc);
        var b = ReadAllBytes(path);
        if (b == null) return null;
        using var sr = new StreamReader(new MemoryStream(b), enc);
        return sr.ReadToEnd();
    }

    /// <summary>Lignes énumérées — streaming loose, décodage
    /// buffer pack.</summary>
    public static IEnumerable<string> ReadLines(string path,
        Encoding? enc = null)
    {
        if (LooseFile(path) is { } lf)
            return File.ReadLines(lf, enc ?? Encoding.UTF8);
        return UnpackLines(ReadAllBytes(path), enc ?? Encoding.UTF8);
    }

    /// <summary>Lignes en tableau.</summary>
    public static string[] ReadAllLines(string path,
        Encoding? enc = null) => ReadLines(path, enc).ToArray();

    private static IEnumerable<string> UnpackLines(byte[]? bytes,
        Encoding enc)
    {
        if (bytes == null) yield break;
        using var sr = new StreamReader(new MemoryStream(bytes), enc);
        while (sr.ReadLine() is { } line) yield return line;
    }

    /// <summary>Contenu d'un dossier — union loose+packs (le loose
    /// masque par nom en mode loose-first ; pack seul sinon). Les
    /// entrées restent des strings réutilisables : abs loose ou
    /// URI epk://. <paramref name="recursive"/> = toute la
    /// descendance, fichiers seuls ; sinon enfants directs (fichiers
    /// + sous-dossiers virtuels — sémantique
    /// EnumerateFileSystemEntries).</summary>
    public static IEnumerable<string> ListDir(string dir,
        bool recursive = false)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (LoosePath(dir) is { } ld && Directory.Exists(ld))
            foreach (var e in LooseList(ld, recursive))
                if (seen.Add(recursive
                        ? Path.GetRelativePath(ld, e).Replace('\\', '/')
                        : Path.GetFileName(e)))
                    yield return e;
        // Récursif : la clé de dédup est le chemin relatif (des noms
        // identiques existent dans des sous-dossiers distincts).
        var pfx = recursive ? EpkFormat.NormalizePath(
            IsEpk(dir) ? EpkRel(dir) : ToRel(dir) ?? "") : "";
        if (pfx.Length > 0 && !pfx.EndsWith('/')) pfx += '/';
        foreach (var e in PackList(dir, recursive))
            if (seen.Add(recursive
                    ? EpkRel(e)[Math.Min(pfx.Length, EpkRel(e).Length)..]
                    : Path.GetFileName(e)))
                yield return e;
    }

    private static IEnumerable<string> LooseList(string dir,
        bool recursive)
    {
        if (IsEpk(dir)) return [];
        try
        {
            return recursive
                ? Directory.EnumerateFiles(dir, "*",
                    SearchOption.AllDirectories).ToList()
                : Directory.EnumerateFileSystemEntries(dir).ToList();
        }
        catch (Exception) { return []; }
    }

    // ======================= casse (corpus EDENFR) =======================

    /// <summary>Chemin loose réel de <paramref name="path"/> : exact,
    /// sinon insensible à la casse segment par segment (le corpus
    /// EDENFR <c>fr-full</c> est surtout en minuscules —
    /// <c>schemes/dejavu.scheme</c> — alors que le code hérité du
    /// merge official demande <c>DejaVu.scheme</c>), sinon sans
    /// préfixe table <c>C_/T_/S_</c> (<c>C_EquipType.ini</c> →
    /// <c>EquipType.ini</c>). null si absent ou loose interdit.</summary>
    public static string? LoosePath(string path)
    {
        if (IsEpk(path) || !LooseReadable(path)) return null;
        if (File.Exists(path) || Directory.Exists(path)) return path;
        if (!Path.IsPathRooted(path)) return null;
        return CaseResolve(path)
            ?? (StripTablePrefix(path) is { } sp ? CaseResolve(sp) : null);
    }

    /// <summary><see cref="LoosePath"/> restreint aux fichiers.</summary>
    private static string? LooseFile(string path) =>
        LoosePath(path) is { } p && File.Exists(p) ? p : null;

    private static string? StripTablePrefix(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length <= 2 || name[1] != '_' ||
            char.ToUpperInvariant(name[0]) is not ('C' or 'T' or 'S'))
            return null;
        return Path.Combine(Path.GetDirectoryName(path) ?? "", name[2..]);
    }

    /// <summary>Listings de dossiers insensibles à la casse, validés
    /// par la date de modification du dossier (un fichier créé après
    /// coup — cache matérialisé — invalide l'entrée). Une clé peut
    /// porter plusieurs entrées réelles (fr-full : <c>S002/</c> et
    /// <c>s002/</c> coexistent).</summary>
    private static readonly Dictionary<string,
        (DateTime Stamp, Dictionary<string, List<string>> Map)> _caseDirs = new();

    /// <summary>Résolution insensible à la casse d'un chemin absolu,
    /// segment par segment : les casses divergent aussi sur les
    /// dossiers intermédiaires (<c>map/model/s002/s002.fsm</c> pour une
    /// requête <c>map/model/S002/S002.fsm</c>, alors que
    /// <c>map/model/S002/</c> existe aussi mais ne contient que
    /// <c>S002.area</c>) — chaque variante est explorée (DFS).</summary>
    public static string? CaseResolve(string absPath)
    {
        var root = Path.GetPathRoot(absPath)
            ?? Path.DirectorySeparatorChar.ToString();
        var segs = absPath[root.Length..].Split(
            ['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return CaseWalk(root, segs, 0);
    }

    private static string? CaseWalk(string cur, string[] segs, int i)
    {
        if (i == segs.Length)
            return File.Exists(cur) || Directory.Exists(cur) ? cur : null;
        var exact = Path.Combine(cur, segs[i]);
        if ((File.Exists(exact) || Directory.Exists(exact)) &&
            CaseWalk(exact, segs, i + 1) is { } hit)
            return hit;
        if (!CaseListing(cur).TryGetValue(segs[i], out var actual))
            return null;
        foreach (var a in actual)
            if (a != exact && CaseWalk(a, segs, i + 1) is { } h)
                return h;
        return null;
    }

    private static Dictionary<string, List<string>> CaseListing(string dir)
    {
        DateTime stamp;
        try { stamp = Directory.GetLastWriteTimeUtc(dir); }
        catch (Exception) { return new(); }
        lock (_caseDirs)
        {
            if (_caseDirs.TryGetValue(dir, out var c) && c.Stamp == stamp)
                return c.Map;
            var map = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var f in Directory.EnumerateFileSystemEntries(dir))
                {
                    var name = Path.GetFileName(f);
                    if (!map.TryGetValue(name, out var l))
                        map[name] = l = new List<string>(1);
                    l.Add(f);
                }
            }
            catch (IOException) { /* dossier disparu : listing vide */ }
            catch (UnauthorizedAccessException) { }
            _caseDirs[dir] = (stamp, map);
            return map;
        }
    }

    // ============================ traduction ============================

    /// <summary>Traduit un chemin absolu en clé pack relative :
    /// sous AssetsRoot → « assets/… » (AssetsRoot pointe le dossier
    /// assets lui-même ; la clé garde le préfixe) ; sous une racine
    /// candidate → le reste. null hors de toute racine.</summary>
    public static string? ToRel(string absPath)
    {
        if (IsEpk(absPath)) return EpkRel(absPath);
        if (!Path.IsPathRooted(absPath)) return null;
        string full;
        try { full = Path.GetFullPath(absPath); }
        catch (Exception) { return null; }
        if (UnderRoot(full, AssetsRootPath(), out var a))
            return "assets/" + a;
        foreach (var root in DataRoots())
            if (UnderRoot(full, root, out var r))
                return r;
        return null;
    }

    private static bool UnderRoot(string full, string root,
        out string rel)
    {
        rel = "";
        if (root.Length == 0) return false;
        var prefix = root.TrimEnd('/', '\\') +
                     Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        rel = full[prefix.Length..].Replace('\\', '/');
        return true;
    }

    // ============================ plateforme ============================

    private static IReadOnlyList<string> DataRoots()
    {
#if GODOT
        return DataPaths.CandidateDataRoots();
#else
        return TestRoots;
#endif
    }

    private static string AssetsRootPath()
    {
#if GODOT
        return AssetCatalog.AssetsRoot;
#else
        return "";
#endif
    }

#if !GODOT
    /// <summary>Racines injectées en tests hors Godot — équivalent
    /// de DataPaths.CandidateDataRoots() pour NetTests.</summary>
    public static readonly List<string> TestRoots = new();
#endif

    private static bool ComputeLooseAllowed()
    {
        var env = Env("EDEN_ALLOW_LOOSE");
        if (env is "1" or "true" or "yes") return true;
        if (env is "0" or "false") return false;
#if GODOT
        return OS.IsDebugBuild();
#else
        return true;   // hors Godot = outils/tests — loose toujours
#endif
    }

    /// <summary>Build exportée release (non-debug) — durcit les
    /// gates sécurité : packs et pointeur non signés refusés.
    /// <c>EDEN_PACK_STRICT=1</c> force le comportement release en dev
    /// (sens unique : un env var peut durcir, jamais desserrer) —
    /// permet d'exercer le chemin sans export release.</summary>
    private static bool ReleaseBuild
    {
#if GODOT
        get => Env("EDEN_PACK_STRICT") is "1" or "true" or "yes"
               || !OS.IsDebugBuild();
#else
        get => false;  // hors Godot = outils/tests — jamais release
#endif
    }

    private static string? Env(string name) =>
        System.Environment.GetEnvironmentVariable(name);

    private static void Log(string msg)
    {
#if GODOT
        GD.Print(msg);
#else
        Console.Out.WriteLine(msg);
#endif
    }
}
