// Audit des textures (--textures) : TOUTE référence de texture de TOUT .nif de fr-full + les combinaisons que
// le jeu peut demander à l'exécution (visages/cheveux/tenues des corps joueur, tables d'équipement) doit se
// résoudre dans le corpus ET se décoder. Sortie : textures.tsv (une ligne par écart) + tableau par catégorie.
// Docs : docs/workflows/TEXTURE-AUDIT.md.
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using EdenClient.Data;
using EdenClient.World;

static partial class TextureAudit
{
    // ---- une référence à vérifier ----------------------------------------------------------------------
    sealed record Ref(string Category, string Source, string Slot, string Requested, bool Exact = false);

    // Écarts authentiques : fichiers absents du manifeste officiel (GameDataFileList.txt), de extracted/ et de qb_out/ ET de
    // fr-full, cherchés par nom de base sur TOUT l'arbre. Clé = nom de base sans extension en minuscules (« .nif » ajouté pour
    // un modèle) ; valeur = ligne de docs/workflows/EDENFR-GAPS.md. Preuve : docs/workflows/TEXTURE-AUDIT.md.
    static readonly Dictionary<string, string> AuthenticMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // G12 (déjà documenté) : tables -> fichiers jamais livrés
        ["m679.nif"] = "G12", ["m680.nif"] = "G12", ["m167.nif"] = "G17", ["n050.nif"] = "G17", ["n115.nif"] = "G17", ["n637.nif"] = "G17", ["w33001"] = "G12",
        // G13 : NIF de corps/tenues/effets/cartes/monstres qui nomment une texture que le jeu ne livre pas
        ["g299as01a"] = "G13", ["g299as01b"] = "G13", ["b202as03c"] = "G13", ["s3615201"] = "G13",
        ["s1615104"] = "G13", ["s1661101"] = "G13", ["s4221101"] = "G13", ["s4903101"] = "G13",
        ["dd00104"] = "G13", ["s212108"] = "G13", ["s210_ci"] = "G13", ["ub50356"] = "G13", ["dust_01_normal"] = "G13", ["n15701"] = "G13",
        // G14 : 4pequip.ini -> meshes / peaux d'équipement jamais livrés
        ["f1208.nif"] = "G14", ["f1213.nif"] = "G14", ["k1208.nif"] = "G14", ["k1213.nif"] = "G14", ["m1208.nif"] = "G14", ["m1213.nif"] = "G14",
        ["x628a81a"] = "G14", ["x628a81b"] = "G14",
        // G15 : decorate.ini -> modèles/textures p612, p614, p616 jamais livrés
        ["p612.nif"] = "G15", ["p614.nif"] = "G15", ["p616.nif"] = "G15",
        ["p61201"] = "G15", ["p61202"] = "G15", ["p61203"] = "G15", ["p61204"] = "G15", ["p61205"] = "G15", ["p61206"] = "G15",
        ["p61207"] = "G15", ["p61208"] = "G15", ["p61209"] = "G15", ["p61210"] = "G15", ["p61401"] = "G15",
        ["p61601"] = "G15", ["p61602"] = "G15", ["p61603"] = "G15",
        // G16 : variante de skin d'arme citée par c_item.ini, absente (le NIF de l'arme garde sa texture de base)
        ["w63101"] = "G16",
    };
    static bool IsAuthentic(string baseName) => AuthenticMap.ContainsKey(baseName);

    static string root = "";
    static readonly ConcurrentBag<Ref> refs = new();
    static readonly ConcurrentDictionary<string, string?> decodeMemo = new(StringComparer.OrdinalIgnoreCase);

    public static int Run(string rootDir, string outPath)
    {
        root = rootDir;
        BuildIndex();
        ScanNifs();
        ScanIfls();
        ScanCharacterCombos();
        ScanTables();
        return Report(outPath);
    }

    // =====================================================================================================
    // Résolveur : PORT FIDÈLE de eden_godot/godot_project/scripts/native/NifMaterial.cs (NifMaterials.EnsureIndex /
    // ResolveCore). Godot-dépendant donc recopié ici ; GARDER EN SYNCHRO : native-coverage.sh --textures compare
    // le corps de ResolveCore des deux fichiers (au `!` de nullabilité près) et échoue s'ils divergent. L'index couvre les .dds sous TexTops uniquement.
    // =====================================================================================================
    static readonly string[] TexTops = { "map", "char", "monster", "ride", "item", "effect", "house", "decorate" };
    static Dictionary<string, List<string>> s_byBase = new();
    static Dictionary<string, string> s_byRel = new();
    static string[] s_keys = Array.Empty<string>();
    static readonly ConcurrentDictionary<string, string?> s_memo = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tous les .dds du corpus (hors TexTops compris) : nom de base → rel, pour la preuve d'existence.</summary>
    static readonly Dictionary<string, string> allDdsByBase = new(StringComparer.OrdinalIgnoreCase);

    static void BuildIndex()
    {
        var byBase = new Dictionary<string, List<string>>();
        var byRel = new Dictionary<string, string>();
        void Add(string rel)
        {
            var b = Path.GetFileNameWithoutExtension(rel).ToLowerInvariant();
            if (!byBase.TryGetValue(b, out var l)) byBase[b] = l = new List<string>();
            l.Add(rel);
            byRel[rel[..^4].ToLowerInvariant()] = rel;
        }
        foreach (var top in Directory.EnumerateDirectories(root))
        {
            var tn = Path.GetFileName(top);
            var inTop = Array.IndexOf(TexTops, tn.ToLowerInvariant()) >= 0;
            foreach (var f in Directory.EnumerateFiles(top, "*.dds", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive }))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                allDdsByBase.TryAdd(Path.GetFileNameWithoutExtension(rel), rel);
                if (inTop) Add(rel);
            }
        }
        foreach (var l in byBase.Values) l.Sort(StringComparer.OrdinalIgnoreCase);
        s_keys = byBase.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        s_byRel = byRel;
        s_byBase = byBase;
        Console.WriteLine($"index DDS : {byRel.Count} chemins, {byBase.Count} noms");
    }

    static readonly Regex SufRx = new(@"^(.+?)(_\d+|_nrm|_n|_s)$", RegexOptions.Compiled);
    static readonly Regex PfxRx = new(@"^(?:ts|ak|dm|dvgn|a)([a-z]+\d{4,}.*)$", RegexOptions.Compiled);

    /// <summary>NifMaterials.ResolveTexture (mémoïsé).</summary>
    static string? ResolveTexture(string nifName, string prefix)
    {
        if (string.IsNullOrEmpty(nifName)) return null;
        return s_memo.GetOrAdd(prefix + "|" + nifName, _ => ResolveCore(nifName, prefix));
    }

    // ---- NifMaterials.ResolveCore, recopié tel quel (s_byRel!/s_byBase! sans '!') ----
    static string? ResolveCore(string nifName, string prefix)
    {
        var p = nifName.Replace('\\', '/').Trim();
        while (p.StartsWith("./") || p.StartsWith("/")) p = p.StartsWith("./") ? p[2..] : p[1..];
        var low = p.ToLowerInvariant();
        var dot = low.LastIndexOf('.');
        var stem = dot > 0 ? low[..dot] : low;
        if (s_byRel.TryGetValue(stem, out var exact)) return exact;
        var bstem = stem[(stem.LastIndexOf('/') + 1)..];
        var byBase = s_byBase;
        List<string>? hits = byBase.GetValueOrDefault(bstem);
        if (hits == null)
        {
            var m0 = SufRx.Match(bstem);
            if (m0.Success && m0.Groups[1].Length >= 4)
            {
                bstem = m0.Groups[1].Value;
                hits = byBase.GetValueOrDefault(bstem);
            }
        }
        if (hits == null && bstem.Length >= 5 && char.IsDigit(bstem[^1]) && char.IsDigit(bstem[^2]))
            foreach (var v in new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09", "51", "52", "53", "54", "55" })
                if ((hits = byBase.GetValueOrDefault(bstem[..^2] + v)) != null) break;
        if (hits == null && bstem.Length >= 3)
        {
            var l = new List<string>();
            foreach (var k in s_keys.Where(k => k.StartsWith(bstem, StringComparison.Ordinal))
                         .OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal))
                l.AddRange(byBase[k]);
            if (l.Count > 0) hits = l;
        }
        if (hits == null && bstem.Length >= 4)
        {
            var l = new List<string>();
            foreach (var k in s_keys.Where(k => k.EndsWith(bstem, StringComparison.Ordinal) && k.Length - bstem.Length <= 4)
                         .OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal))
                l.AddRange(byBase[k]);
            if (l.Count > 0) hits = l;
        }
        if (hits == null && bstem.Length > 5 && char.IsDigit(bstem[^1]) && char.IsDigit(bstem[^2]))
        {
            var rx = new Regex("^[a-z]{0,4}" + Regex.Escape(bstem[..^2]) + @"(\d\d).*$");
            var want = int.Parse(bstem[^2..]);
            var best = s_keys.Select(k => (k, m: rx.Match(k))).Where(x => x.m.Success)
                .OrderBy(x => Math.Abs(int.Parse(x.m.Groups[1].Value) - want)).ThenBy(x => x.k.Length)
                .ThenBy(x => x.k, StringComparer.Ordinal).FirstOrDefault();
            if (best.k != null) hits = byBase[best.k];
        }
        if (hits == null && bstem.Length >= 7)
        {
            var m2 = PfxRx.Match(bstem);
            if (m2.Success) hits = byBase.GetValueOrDefault(m2.Groups[1].Value);
        }
        if (hits == null || hits.Count == 0) return null;
        if (prefix.Length > 0)
        {
            var pref = prefix.ToLowerInvariant().TrimEnd('/') + "/texture/";
            foreach (var h in hits)
                if (h.StartsWith(pref, StringComparison.OrdinalIgnoreCase)) return h;
        }
        foreach (var h in hits)
            if (!h.StartsWith("ui/", StringComparison.OrdinalIgnoreCase)) return h;
        return hits[0];
    }

    /// <summary>Le nom demandé existe-t-il tel quel (chemin ou nom de base exact) ? Sinon le résolveur a « deviné ».</summary>
    static bool IsExact(string requested, string resolvedRel)
    {
        var n = Path.GetFileNameWithoutExtension(requested.Replace('\\', '/')).ToLowerInvariant();
        return n == Path.GetFileNameWithoutExtension(resolvedRel).ToLowerInvariant();
    }

    // ---- décodage (même contrat que DdsTexture.Load) ----------------------------------------------------
    public static void CheckDds(byte[] d)
    {
        if (DdsInfo.TryParse(d, out var info))
        {
            int total = 0, w = info.Width, h = info.Height;
            for (int i = 0; i < info.MipLevels; i++) { total += info.MipSize(w, h); w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); }
            if (info.DataOffset + total > d.Length) throw new InvalidDataException("DDS tronqué");
            return;
        }
        if (d.Length < 0x80 || BitConverter.ToUInt32(d, 0) != 0x20534444) throw new InvalidDataException("pas un DDS");
        int hh = BitConverter.ToInt32(d, 0x0c), ww = BitConverter.ToInt32(d, 0x10), mips = Math.Max(1, BitConverter.ToInt32(d, 0x1c));
        uint pf = BitConverter.ToUInt32(d, 0x50), bits = BitConverter.ToUInt32(d, 0x58);
        if ((pf & 0x40) == 0 || (bits != 16 && bits != 24) || ww <= 0 || hh <= 0) throw new InvalidDataException($"DDS non géré (pf={pf:x} bits={bits})");
        long need = 0x80; int cw = ww, ch = hh;
        for (int m = 0; m < mips; m++) { need += (long)cw * ch * (bits / 8); cw = Math.Max(1, cw >> 1); ch = Math.Max(1, ch >> 1); }
        if (need > d.Length) throw new InvalidDataException("DDS tronqué");
    }

    /// <summary>null = décodable, sinon la raison.</summary>
    static string? DecodeProblem(string rel) => decodeMemo.GetOrAdd(rel, r =>
    {
        try { CheckDds(File.ReadAllBytes(Path.Combine(root, r))); return null; }
        catch (Exception e) { return "illisible: " + e.Message; }
    });
}
