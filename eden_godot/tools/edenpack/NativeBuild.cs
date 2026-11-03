using System.Globalization;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using EdenClient.Vfs;

namespace EdenPack;

/// <summary>CRC32C (Castagnoli) — algorithme de GameDataFileList.txt
/// (cf. tools/edenfr-verify/crc32c.c). SSE4.2 si dispo, table sinon.</summary>
internal static class Crc32c
{
    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (c >> 1) ^ 0x82F63B78u : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> p)
    {
        var c = 0xFFFFFFFFu;
        var i = 0;
        if (Sse42.X64.IsSupported)
        {
            var c64 = (ulong)c;
            for (; i + 8 <= p.Length; i += 8)
                c64 = Sse42.X64.Crc32(c64,
                    BitConverter.ToUInt64(p.Slice(i, 8)));
            c = (uint)c64;
        }
        for (; i < p.Length; i++)
            c = Table[(c ^ p[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// `edenpack build --native` (ADR-0005, plan P1/P2/P5) : corpus/fr-full +
/// gamedata/ du projet -> bootstrap / eden-core / eden-assets / eden-audio,
/// fichiers natifs tels quels, avec contrôle de provenance EDENFR
/// (taille + CRC32C du manifeste officiel) et rapport provenance.tsv.
/// </summary>
internal static class NativeBuild
{
    public const string Bootstrap = "bootstrap";
    public const string Core = "eden-core";
    public const string Assets = "eden-assets";
    public const string Audio = "eden-audio";
    public static readonly string[] AllPacks =
        { Bootstrap, Core, Assets, Audio };

    internal sealed class Options
    {
        public string Corpus = "", Gamedata = "", Manifest = "";
        public string VerifyTsv = "", ExtractTsv = "", OutDir = "";
        public int Level = 12;
        public ulong Version = 1;
        public string? Keyfile, SignKey, PrevDir;
        public uint? SignKeyId;
        public bool AllowUnsigned;
        public HashSet<string> Only = new();
        public HashSet<string> Encrypt = new() { Core, Assets };
    }

    /// <summary>Pack de destination d'un chemin EPK (minuscule, '/'),
    /// ou null = non empaqueté. Corpus et overlay gamedata/ partagent
    /// la table ; les règles sont testées dans l'ordre.</summary>
    internal static string? Route(string rel, bool overlay)
    {
        var ext = Path.GetExtension(rel);
        var name = Path.GetFileName(rel);
        if (overlay)
        {
            if (ext == ".import" || name == "settings.cfg") return null;
            if (rel.StartsWith("themes/") || rel.StartsWith("uipatch/")) return Bootstrap;   // thèmes UI (décision propriétaire, ADR-0006)
            if (name is "client.cfg" or "login_bg.jpg"
                || name.StartsWith("ui_text_")) return Bootstrap;
            return Core;
        }
        var top = rel.Split('/')[0];
        if (rel.StartsWith("uidata/"))
        {
            var seg = rel.Split('/');
            if (seg.Length > 2 && seg[1] is "schemes" or "looknfeel"
                or "fonts" or "codepage" or "imagesets") return Bootstrap;
            if (seg.Length == 3 && seg[1] == "layouts"
                && (name.StartsWith("login")
                    || name.EndsWith("worldserverlistwnd.layout")))
                return Bootstrap;
            return Core;
        }
        switch (top)
        {
            case "data": case "ui":
                return Core;
            case "map":
                return ext is ".fsm" or ".smp" or ".ini" or ".area"
                    ? Core
                    : ext is ".fsmp" or ".pgp" or ".rar" or ".txt"
                        ? null              // inutilisés par le client
                        : Assets;
            case "char": case "monster": case "ride": case "item":
            case "effect": case "decorate": case "house": case "emblem":
            case "cutscene":
                return Assets;
            case "sound": case "bgm":
                return Audio;
            default:
                return null;   // racine (exe/dll/dat), movies/
        }
    }

    /// <summary>DDS à fourcc (BC1-5, DX10, ATI…) = déjà compressé GPU :
    /// stocké raw. Les DDS non compressés restent éligibles zstd.</summary>
    internal static bool IsBlockCompressedDds(string rel, byte[] d)
    {
        if (!rel.EndsWith(".dds") || d.Length < 128
            || d[0] != 'D' || d[1] != 'D' || d[2] != 'S' || d[3] != ' ')
            return false;
        return (BitConverter.ToUInt32(d, 80) & 0x4) != 0;   // DDPF_FOURCC
    }

    private sealed record Official(long Size, uint Crc);

    private static Dictionary<string, List<Official>> LoadManifest(string path)
    {
        var m = new Dictionary<string, List<Official>>();
        foreach (var l in File.ReadLines(path, Encoding.Latin1))
        {
            var p = l.TrimEnd('\r', '\n').Split(':');
            if (p.Length < 4) continue;
            var key = EpkFormat.NormalizePath(p[0]);
            if (!m.TryGetValue(key, out var list))
                m[key] = list = new List<Official>();
            list.Add(new Official(long.Parse(p[2], CultureInfo.InvariantCulture),
                uint.Parse(p[3], CultureInfo.InvariantCulture)));
        }
        return m;
    }

    private static HashSet<string> LoadTsvStatus(string path, string status)
    {
        var s = new HashSet<string>();
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"fichier de provenance introuvable : {path}");
        foreach (var l in File.ReadLines(path, Encoding.Latin1))
        {
            var c = l.Split('\t');
            if (c.Length >= 2 && c[1] == status)
                s.Add(EpkFormat.NormalizePath(c[0]));
        }
        return s;
    }

    public static int Run(Options o)
    {
        if (o.SignKey == null && !o.AllowUnsigned)
            throw new InvalidDataException(
                "--native exige --sign-key (ou --allow-unsigned, test " +
                "uniquement) : le build release refuse un pack non signé");
        foreach (var d in new[] { o.Corpus, o.Gamedata })
            if (!Directory.Exists(d))
                throw new DirectoryNotFoundException(d);
        var official = LoadManifest(o.Manifest);
        // Connus : manifeste dit X, l'installation EDENFR diffère
        // (sound/bgm mis à jour après les .pkg) — voir
        // docs/provenance/edenfr-verify-*.tsv.
        var knownDiff = LoadTsvStatus(o.VerifyTsv, "AUCUNE-SOURCE");
        // Authentiques (extraits des .pkg) mais absents du manifeste.
        var horsManifeste = LoadTsvStatus(o.ExtractTsv,
            "OK-pkg-HORS-MANIFESTE");
        foreach (var k in knownDiff)
            if (!(k.StartsWith("sound/") || k.StartsWith("bgm/")))
                throw new InvalidDataException(
                    $"exception AUCUNE-SOURCE hors sound/bgm : {k}");
        Console.Error.WriteLine(
            $"native: manifeste {official.Count} entrées, " +
            $"{knownDiff.Count} exceptions sound/bgm, " +
            $"{horsManifeste.Count} hors-manifeste (pkg)");

        // Répartition. Les fichiers non-régularisés (liens) sont ignorés.
        var byPack = AllPacks.ToDictionary(p => p,
            _ => new List<(string abs, string rel)>());
        var overlayRel = new HashSet<string>();
        var skipped = new SortedDictionary<string, int>();
        var seen = new Dictionary<string, string>();
        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var (root, overlay) in new[]
                     { (o.Corpus, false), (o.Gamedata, true) })
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", enumOpts))
            {
                var rel = EpkFormat.NormalizePath(
                    Path.GetRelativePath(root, f));
                var pack = Route(rel, overlay);
                if (pack == null)
                {
                    var k = overlay ? "gamedata/" : rel.Contains('/')
                        ? rel[..rel.IndexOf('/')] + "/" : "(racine)";
                    skipped[k] = skipped.GetValueOrDefault(k) + 1;
                    continue;
                }
                if (seen.TryGetValue(rel, out var prev))
                    throw new InvalidDataException(
                        $"collision de chemin normalisé : {rel} " +
                        $"({prev} vs {f})");
                seen[rel] = f;
                byPack[pack].Add((f, rel));
                if (overlay) overlayRel.Add(rel);
            }
        }
        foreach (var l in byPack.Values)
            l.Sort((a, b) => string.CompareOrdinal(a.rel, b.rel));
        foreach (var kv in skipped)
            Console.Error.WriteLine(
                $"native: non empaqueté {kv.Key} : {kv.Value} fichier(s)");

        Directory.CreateDirectory(o.OutDir);
        var provenance = new List<string>
        {
            "pack\tepk_path\tsize\tsha256\tcrc32c\tofficial_crc32c\t"
            + "official_size\tstatus",
        };
        var counts = new SortedDictionary<string, int>();
        var cryptoBase = o.SignKey != null ? ResolveSign(o) : null;

        var warned = 0;
        foreach (var pack in AllPacks)
        {
            if (o.Only.Count > 0 && !o.Only.Contains(pack)) continue;
            var outPath = Path.Combine(o.OutDir, pack + ".epk");
            var crypto = new EpkWriter.CryptoSpec();
            if (o.Keyfile != null && o.Encrypt.Contains(pack))
            {
                var kf = EpkCrypto.LoadKeyfile(o.Keyfile);
                if (kf.A == null || kf.B == null)
                    throw new InvalidDataException(
                        "keyfile : A= et B= requis");
                if (kf.Kdata == null)
                {
                    kf.Kdata = EpkCrypto.GenKey();
                    EpkCrypto.SetKeyfileField(o.Keyfile, "kdata",
                        EpkCrypto.ToHex(kf.Kdata));
                }
                crypto.Kdata = kf.Kdata; crypto.PartA = kf.A;
                crypto.PartB = kf.B;
            }
            if (cryptoBase != null)
            {
                crypto.SignKeyDer = cryptoBase;
                crypto.SignKeyId = o.SignKeyId;
            }
            var rows = new List<string>();
            var hooks = new EpkWriter.NativeHooks
            {
                Files = byPack[pack],
                ForceRaw = (rel, d) => IsBlockCompressedDds(rel, d),
            };
            hooks.OnFile = (rel, abs, data, sha) =>
            {
                var crc = Crc32c.Compute(data);
                string status, oc = "", os = "";
                if (overlayRel.Contains(rel))
                    status = "OVERLAY-PROJET";
                else if (!official.TryGetValue(rel, out var offs))
                {
                    if (!horsManifeste.Contains(rel))
                        throw new InvalidDataException(
                            $"PROVENANCE : {rel} absent de " +
                            "GameDataFileList.txt et non listé " +
                            "OK-pkg-HORS-MANIFESTE");
                    status = "PKG-HORS-MANIFESTE";
                }
                else
                {
                    var hit = offs.FirstOrDefault(x =>
                        x.Size == data.Length && x.Crc == crc);
                    oc = offs[0].Crc.ToString(CultureInfo.InvariantCulture);
                    os = offs[0].Size.ToString(CultureInfo.InvariantCulture);
                    if (hit != null)
                        status = "OK";
                    else if (knownDiff.Contains(rel))
                    {
                        status = "EXCEPTION-AUCUNE-SOURCE";
                        if (++warned <= 3)
                            Console.Error.WriteLine(
                                $"native: AVERTISSEMENT {rel} diffère " +
                                "du manifeste (exception connue " +
                                "sound/bgm" + (warned == 3
                                    ? " ; suite dans provenance.tsv)"
                                    : ")"));
                    }
                    else
                        throw new InvalidDataException(
                            $"PROVENANCE : {rel} non conforme au " +
                            $"manifeste (taille {data.Length} vs " +
                            $"{offs[0].Size}, crc32c {crc} vs " +
                            $"{offs[0].Crc})");
                }
                counts[pack + " " + status] =
                    counts.GetValueOrDefault(pack + " " + status) + 1;
                rows.Add(string.Join('\t', pack, rel,
                    data.Length.ToString(CultureInfo.InvariantCulture),
                    Convert.ToHexString(sha).ToLowerInvariant(),
                    crc.ToString(CultureInfo.InvariantCulture), oc, os,
                    status));
            };
            Console.Error.WriteLine(
                $"native: {pack} : {byPack[pack].Count} fichier(s)");
            try
            {
                EpkWriter.Build(o.Corpus, outPath, o.Level, o.Version,
                    DeterministicId(pack, o.Version), new List<string>(),
                    PrevPack(o, pack), null,
                    crypto.SignKeyDer != null || crypto.Kdata != null
                        ? crypto : null, hooks);
            }
            catch
            {
                try { File.Delete(outPath + ".building"); }
                catch (IOException) { /* fichier déjà absent/verrouillé */ }
                throw;
            }
            provenance.AddRange(rows);
        }

        var provName = o.Only.Count > 0
            ? "provenance." + string.Join('+', o.Only.OrderBy(x => x)) + ".tsv"
            : "provenance.tsv";
        File.WriteAllLines(Path.Combine(o.OutDir, provName), provenance);
        Console.Out.WriteLine("provenance :");
        foreach (var kv in counts)
            Console.Out.WriteLine($"  {kv.Key,-40} {kv.Value}");
        Console.Out.WriteLine(
            $"-> {Path.Combine(o.OutDir, provName)} " +
            $"({provenance.Count - 1} lignes)");
        return 0;
    }

    /// <summary>Pack de la release précédente (--prev-dir) : conserve /
    /// incrémente cryptoRev, indispensable en chiffré (nonce GCM =
    /// index de chunk : un contenu modifié exige une rev différente).</summary>
    private static string? PrevPack(Options o, string pack)
    {
        if (o.PrevDir == null) return null;
        var p = Path.Combine(o.PrevDir, pack + ".epk");
        return File.Exists(p) ? p : null;
    }

    private static byte[] ResolveSign(Options o)
    {
        try { return EpkCrypto.LoadPrivateKeyDer(o.SignKey!); }
        catch (Exception)
        {
            var kf = EpkCrypto.LoadKeyfile(o.SignKey!);
            return kf.Sign ?? throw new InvalidDataException(
                "clé de signature illisible : " + o.SignKey);
        }
    }

    /// <summary>packId stable par (pack, version) -> build bit-à-bit
    /// reproductible.</summary>
    private static Guid DeterministicId(string pack, ulong version)
    {
        var h = SHA256.HashData(
            Encoding.UTF8.GetBytes($"edenpack-native:{pack}:{version}"));
        return new Guid(h.AsSpan(0, 16));
    }
}
