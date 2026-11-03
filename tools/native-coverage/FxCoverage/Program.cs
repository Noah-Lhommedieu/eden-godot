// Couverture FX (F5) : charge chaque .nif de fr-full, mappe et simule les
// particules 2 s (headless, Core pur), recense les contrôleurs animés et
// leurs limites. Sortie TSV : une ligne par NIF (artifacts/fx-sheets/coverage.tsv).
using System.Collections.Concurrent;
using System.Text;
using EdenClient.Fx;
using EdenClient.World;

var root = args.Length > 0 ? args[0] : throw new ArgumentException("usage: FxCoverage <fr-full> <out.tsv> [seconds]");
var outPath = args.Length > 1 ? args[1] : "coverage.tsv";
float seconds = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 2f;

var files = Directory.EnumerateFiles(root, "*.nif", SearchOption.AllDirectories).Order().ToArray();
var rows = new ConcurrentDictionary<string, string>();
var tally = new ConcurrentDictionary<string, int>();
void Tally(string k, int n = 1) => tally.AddOrUpdate(k, n, (_, o) => o + n);

Parallel.ForEach(files, f =>
{
    string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
    NifFile nif;
    try { nif = NifFile.Load(f); }
    catch (Exception e) { rows[rel] = $"{rel}\t0\t0\t0\t0\t0\t0\t0\tLOADFAIL:{e.GetType().Name}"; Tally("loadfail"); return; }

    // contrôleurs de matériau / nœud (hors particules)
    var kinds = new Dictionary<string, int>();
    var notes = new SortedSet<string>();
    foreach (var c in nif.Controllers)
    {
        if (c.Type.StartsWith("NiPSys")) continue;
        kinds[c.Type] = kinds.GetValueOrDefault(c.Type) + 1;
        bool ok = c.Type switch
        {
            "NiTextureTransformController" or "NiAlphaController" or "NiVisController"
                => NifCurves.FloatOf(c.Interpolator) != null,
            "NiMaterialColorController" => NifCurves.Vec3Of(c.Interpolator) != null,
            "NiTransformController" => NifCurves.Track(c.Interpolator, "") != null,
            "NiFlipController" => c.FlipFiles.Length > 0,
            "NiGeomMorpherController" => c.MorphData != null,
            _ => true,
        };
        if (!ok) { notes.Add("unsupported:" + c.Type + "/" + (c.Interpolator?.Type ?? "null")); }
    }

    int nps = 0, maxCount = 0, endCount = 0, totalBirths = 0; float dur = 0; string err = "";
    foreach (var ps in nif.ParticleSystems)
    {
        nps++;
        try
        {
            var m = NifParticleMapper.Map(nif, ps);
            foreach (var n in m.Notes) notes.Add(n);
            var sim = new ParticleSim(m.Def, 12345u);
            int peak = 0; float t = 0; const float dt = 1f / 30f;
            for (; t < seconds; t += dt) { sim.Step(dt); peak = Math.Max(peak, sim.Count); }
            maxCount = Math.Max(maxCount, peak); endCount += sim.Count;
            if (m.Def.Curves.TryGetValue(PsParam.BirthRate, out var br)) dur = Math.Max(dur, br.Timing.Duration);
            foreach (var p in sim.Particles)
                if (float.IsNaN(p.Position.X) || float.IsNaN(p.Size)) { err = "NaN"; Tally("nan"); break; }
            if (peak == 0) notes.Add("never-emits");
            totalBirths += peak;
        }
        catch (Exception e) { err = e.GetType().Name + ":" + e.Message.Replace('\t', ' '); Tally("simfail"); }
    }
    if (nps > 0) Tally("psfiles");
    Tally("ps", nps);
    foreach (var k in kinds) Tally("ctl:" + k.Key, k.Value);
    foreach (var n in notes) Tally("note:" + n);
    rows[rel] = string.Join('\t', rel, nps, maxCount, endCount, dur.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        string.Join(",", kinds.Select(k => k.Key.Replace("Ni", "").Replace("Controller", "") + k.Value)),
        nif.Errors.Count, nif.Warnings.Count, (err + " " + string.Join(";", notes)).Trim());
});

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
var sb = new StringBuilder("nif\tparticle_systems\tpeak_particles\tparticles_at_end\tbirthrate_ctl_duration_s\tcontrollers\tnif_errors\tnif_warnings\tnotes_errors\n");
foreach (var k in rows.Keys.Order()) sb.AppendLine(rows[k]);
File.WriteAllText(outPath, sb.ToString());
Console.WriteLine($"fichiers={files.Length} -> {outPath}");
foreach (var kv in tally.OrderBy(k => k.Key)) Console.WriteLine($"  {kv.Key} = {kv.Value}");
