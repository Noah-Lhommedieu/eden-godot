// Couverture native (G/H) : (1) tout fichier .nif .kf .kfm .dds .ifl .fsm de fr-full est décodé par les
// lecteurs Core ; (2) chaque cellule des tables data/db qui ressemble à une référence de fichier
// (.nif .kf .kfm .dds .ifl .tga) se résout dans le corpus. Sortie : TSV + résumé ; code 1 si échec/manquant.
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using EdenClient.Data;
using EdenClient.World;

var root = args.Length > 0 ? args[0] : throw new ArgumentException("usage: AllCoverage <fr-full> <out.tsv>");
var outPath = args.Length > 1 ? args[1] : "coverage-all.tsv";
if (args.Length > 2 && args[2] == "textures") return TextureAudit.Run(root, outPath);   // --textures : voir TextureAudit*.cs

var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
var byRel = new Dictionary<string, string>();
var byName = new Dictionary<string, string>();
foreach (var f in all)
{
    var rel = Path.GetRelativePath(root, f).Replace('\\', '/').ToLowerInvariant();
    byRel[rel] = f;
    byName.TryAdd(Path.GetFileName(rel), f);
}

// Fichiers authentiques (CRC du manifeste OK) qui ne sont pas du format que leur extension annonce ;
// aucun code ni table ne les référence.
var Exceptions = new HashSet<string>
{
    "map/map.nif",                  // exécutable PE (en-tête MZ) nommé .nif dans les données officielles
    "map/model/effect/effect.fsm",  // pas une carte (aucune map « effect ») ; tailles de chunk incohérentes
};
// Références de tables vers des fichiers absents du manifeste officiel, de extracted/ et de qb_out/ :
// le jeu d'origine ne les livre pas (EDENFR-GAPS G12).
var MissingInGame = new HashSet<string> { "m679.nif", "m680.nif", "m679.kfm", "m680.kfm", "w33001.dds" };
var rows = new ConcurrentBag<string>();
var tally = new ConcurrentDictionary<string, int>();
void T(string k) => tally.AddOrUpdate(k, 1, (_, o) => o + 1);

string[] exts = [".nif", ".kf", ".kfm", ".dds", ".ifl", ".fsm"];
Parallel.ForEach(all.Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())), f =>
{
    var ext = Path.GetExtension(f).ToLowerInvariant();
    var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
    try
    {
        switch (ext)
        {
            case ".nif": var n = NifFile.Load(f); if (n.Errors.Count > 0) throw new InvalidDataException(n.Errors[0]); break;
            case ".kf": KfFile.Load(f); break;
            case ".kfm": KfmFile.Load(f); break;
            case ".ifl": IflFile.Load(f); break;
            case ".fsm": FsmFile.Load(f); break;
            case ".dds": CheckDds(File.ReadAllBytes(f)); break;
        }
        T("decode-ok" + ext);
    }
    catch (Exception e) when (Exceptions.Contains(rel.ToLowerInvariant()))
    {
        T("decode-EXCEPTION" + ext);
    }
    catch (Exception e)
    {
        T("decode-FAIL" + ext);
        rows.Add($"DECODE\t{rel}\t{e.GetType().Name}: {e.Message.Replace('\t', ' ').Replace('\n', ' ')}");
    }
});

var rx = new Regex(@"[A-Za-z0-9_\-./\\]+\.(nif|kf|kfm|dds|ifl|tga)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
var tables = Directory.EnumerateFiles(Path.Combine(root, "data"), "*.ini", SearchOption.AllDirectories).ToArray();
Parallel.ForEach(tables, t =>
{
    string text;
    try { text = System.Text.Encoding.Latin1.GetString(MyCrypt.DecodeFile(t)); }
    catch (Exception e) { T("table-FAIL"); rows.Add($"TABLE\t{Path.GetRelativePath(root, t)}\t{e.Message}"); return; }
    T("table-ok");
    var seen = new HashSet<string>();
    foreach (Match m in rx.Matches(text))
    {
        var r = m.Value.Replace('\\', '/').ToLowerInvariant();
        if (!seen.Add(r)) continue;
        T("ref");
        var name = Path.GetFileName(r);
        if (byRel.ContainsKey(r)) { T("ref-ok"); continue; }
        if (byName.ContainsKey(name)) { T("ref-ok-byname"); continue; }
        // .tga : le client lit le .dds du même nom
        if (name.EndsWith(".tga") && byName.ContainsKey(Path.ChangeExtension(name, ".dds"))) { T("ref-ok-tga->dds"); continue; }
        if (MissingInGame.Contains(name)) { T("ref-EXCEPTION-absent-du-jeu"); continue; }
        T("ref-MISSING");
        rows.Add($"REF\t{Path.GetFileName(t)}\t{r}");
    }
});

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllLines(outPath, rows.Order());
foreach (var kv in tally.OrderBy(k => k.Key)) Console.WriteLine($"{kv.Key} = {kv.Value}");
var bad = tally.Where(k => k.Key.Contains("FAIL") || k.Key.Contains("MISSING")).Sum(k => k.Value);
Console.WriteLine(bad == 0 ? "COUVERTURE 100 %" : $"ÉCARTS : {bad} (voir {outPath})");
return bad == 0 ? 0 : 1;


static void CheckDds(byte[] d) => TextureAudit.CheckDds(d);   // une seule implémentation (TextureAudit.cs)
