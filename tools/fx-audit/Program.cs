// Audit textures/clamp des systèmes de particules (Core pur, headless, aucun
// rendu). Pour chaque .nif à particules : source de texture du PSys (fichier
// externe .dds / NiPixelData embarqué / non résolue), mode de blend et de clamp,
// et si le fichier externe existe sous <root>/effect/texture (insensible à la
// casse). Sortie TSV + compteurs.
//   dotnet run -c Release --project tools/fx-audit -- <fr-full> [out.tsv]
//   dotnet run -c Release --project tools/fx-audit -- <fr-full> --bytex s0014102.dds [...]
using EdenClient.World;

var root = args.Length > 0 ? args[0] : throw new ArgumentException("usage: FxAudit <fr-full> [out.tsv|--bytex t.dds ...]");
var rest = args.Skip(1).ToArray();
var files = Directory.EnumerateFiles(root, "*.nif", SearchOption.AllDirectories).Order().ToArray();

// Index des textures présentes (noms en minuscules) — remonter à la racine
// du corpus (<root>/effect/texture) depuis n'importe quel sous-dossier scanné.
var baseDir = Path.GetFullPath(root);
while (!Directory.Exists(Path.Combine(baseDir, "effect", "texture"))
       && Path.GetDirectoryName(baseDir) is { } up) baseDir = up;
var texDir = Path.Combine(baseDir, "effect", "texture");
var present = Directory.Exists(texDir)
    ? Directory.EnumerateFiles(texDir).Select(Path.GetFileName).Where(n => n != null)
        .Select(n => n!.ToLowerInvariant()).ToHashSet()
    : new HashSet<string>();

var stat = new Dictionary<string, int>();
void Tally(string k, int n = 1) => stat[k] = stat.GetValueOrDefault(k, 0) + n;
var rows = new List<string>();
var byTex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
int psTotal = 0, fileCount = 0;

foreach (var f in files)
{
    NifFile nif;
    try { nif = NifFile.Load(f); } catch { Tally("loadfail"); continue; }
    if (nif.ParticleSystems.Count == 0) continue;
    fileCount++;
    foreach (var ps in nif.ParticleSystems)
    {
        psTotal++;
        var tex = ps.Properties.OfType<NifTexturingProperty>().FirstOrDefault();
        var al = ps.Properties.OfType<NifAlphaProperty>().FirstOrDefault();
        int blend = al == null ? 0 : al.Blend ? (al.DstBlend == 0 ? 3 : 2) : al.Test ? 1 : 0;
        var b = tex?.Base;
        string kind = b == null ? "no-base-map"
            : b.File != null ? "ext-file"
            : b.Source?.Pixels != null ? "embedded"
            : b.Source?.External == true ? "src-ext-no-file" : "src-no-pixels";
        Tally("kind:" + kind);
        Tally($"blend:{blend}");
        if (b != null) Tally($"clamp:{b.ClampMode}");
        if (b?.Source?.Pixels is { } px) Tally($"pxfmt:{px.Format}");
        if (b?.File != null)
        {
            var k = Path.GetFileName(b.File).ToLowerInvariant();
            if (!present.Contains(k)) Tally("missing:" + k);
            if (!byTex.TryGetValue(k, out var l)) byTex[k] = l = new();
            l.Add(Path.GetFileName(f));
        }
        if (kind is not ("ext-file" or "embedded"))
            rows.Add($"{Path.GetFileName(f)}\t{ps.Name}\tkind={kind}\tblend={blend}\tclamp={b?.ClampMode.ToString() ?? "-"}\tfile={b?.File ?? "-"}");
        else if (kind == "ext-file" && !present.Contains(Path.GetFileName(b!.File!).ToLowerInvariant()))
            rows.Add($"{Path.GetFileName(f)}\t{ps.Name}\tkind=MISSING\tfile={b.File}");
    }
}

if (rest.FirstOrDefault() == "--bytex")
{
    foreach (var w in rest.Skip(1))
        Console.WriteLine($"{w}: {string.Join(", ", (byTex.GetValueOrDefault(w.ToLowerInvariant()) ?? new List<string>()).Take(20))}");
    return 0;
}

var outPath = rest.FirstOrDefault() ?? "fx-audit.tsv";
var sb = new System.Text.StringBuilder("nif\tpsys\tkind\tblend\tclamp\tfile\n");
foreach (var r in rows) sb.AppendLine(r);
File.WriteAllText(outPath, sb.ToString());
Console.WriteLine($"nif-avec-ps={fileCount} psys={psTotal} anomalies={rows.Count} -> {outPath}");
foreach (var kv in stat.OrderByDescending(k => k.Value).Take(60)) Console.WriteLine($"  {kv.Key} = {kv.Value}");
return 0;
