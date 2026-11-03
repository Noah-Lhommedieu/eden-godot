using System.Collections.Concurrent;
using System.Text;

static partial class TextureAudit
{

    /// <summary>Verdicts calculés au scan (ex. un shape absent d'un méga-modèle) : (référence, statut, raison).</summary>
    static readonly ConcurrentBag<(Ref r, St st, string why)> pre = new();

    enum St { Ok, OkFuzzy, Missing, Undecodable, Exception, Info }

    static (St, string?, string) Eval(Ref r)
    {
        string? rel = null;
        var alt = 0;
        if (r.Exact)
        {
            // « a|b|c » : chaîne de repli du runtime (premier existant gagne) ; sinon chemin exact.
            var cands = r.Requested.Split('|');
            for (; alt < cands.Length && rel == null; alt++)
            {
                var key = cands[alt].Replace('\\', '/').ToLowerInvariant();
                rel = key.EndsWith(".nif") ? (allFiles.Contains(key) ? key : null)
                    : s_byRel.GetValueOrDefault(key.EndsWith(".dds") ? key[..^4] : key);
            }
            alt--;
            if (rel != null && rel.EndsWith(".nif")) return (St.Ok, rel, "");
        }
        else rel = ResolveTexture(r.Requested, PrefixOf(r.Source));
        if (rel == null)
        {
            var b = Path.GetFileNameWithoutExtension(r.Requested.Replace('\\', '/').Split('|')[0]);
            if (r.Requested.EndsWith(".nif") ? IsAuthentic(b + ".nif") : IsAuthentic(b)) return (St.Exception, null, "absent du jeu (EDENFR-GAPS)");
            if (r.Category.EndsWith(",info)")) return (St.Info, null, "info : hors bornes de création, jamais demandé");
            return (St.Missing, null, "introuvable dans l'index (aucun repli ne correspond)");
        }
        var bad = DecodeProblem(rel);
        if (bad != null) return (St.Undecodable, rel, bad);
        if (r.Exact) return (alt > 0 ? St.OkFuzzy : St.Ok, rel, alt > 0 ? "repli de variante" : "");
        return (!IsExact(r.Requested, rel) ? St.OkFuzzy : St.Ok, rel, "");
    }

    // Préfixe de dossier de textures comme NifScene (dossier de tête du .nif). Sans effet sur l'existence.
    static string PrefixOf(string source)
    {
        var s = source.Replace('\\', '/');
        var i = s.IndexOf('/');
        return i > 0 ? s[..i] : "";
    }

    static int Report(string outPath)
    {
        var all = refs.ToArray();
        var res0 = new (Ref r, St st, string? rel, string why)[all.Length];
        Parallel.For(0, all.Length, i => { var (s, rel, w) = Eval(all[i]); res0[i] = (all[i], s, rel, w); });
        // Références identiques (même .nif, slot, nom) comptées une fois : plusieurs shapes partagent la même texture.
        var res = res0.Concat(pre.Select(p => (r: p.r, st: p.st, rel: (string?)null, why: p.why)))
            .GroupBy(x => (x.r.Category, x.r.Source, x.r.Slot, x.r.Requested)).Select(g => g.First()).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("statut\tcategorie\tsource\tslot\tdemande\traison");
        foreach (var x in res.Where(x => x.st is St.Missing or St.Undecodable).OrderBy(x => x.r.Category).ThenBy(x => x.r.Source, StringComparer.Ordinal).ThenBy(x => x.r.Slot).ThenBy(x => x.r.Requested))
            sb.AppendLine($"{x.st}\t{x.r.Category}\t{x.r.Source}\t{x.r.Slot}\t{x.r.Requested}\t{x.why}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
        // Replis « devinés » (le nom demandé n'existe pas tel quel) : informatif, jamais un échec.
        File.WriteAllLines(Path.ChangeExtension(outPath, ".fuzzy.tsv"),
            res.Where(x => x.st == St.OkFuzzy).Select(x => $"{x.r.Category}\t{x.r.Source}\t{x.r.Slot}\t{x.r.Requested}\t{x.rel}").Distinct().Order());

        // Exceptions documentées (absentes du jeu, EDENFR-GAPS) : tracées pour audit, jamais un échec.
        File.WriteAllLines(Path.ChangeExtension(outPath, ".exceptions.tsv"),
            res.Where(x => x.st == St.Exception).OrderBy(x => x.r.Category).ThenBy(x => x.r.Source, StringComparer.Ordinal)
               .Select(x => { var b = Path.GetFileNameWithoutExtension(x.r.Requested.Split('|')[0].Replace('\\', '/')); var k = x.r.Requested.EndsWith(".nif") ? b + ".nif" : b;
                              return $"{AuthenticMap.GetValueOrDefault(k, "?")}\t{x.r.Category}\t{x.r.Source}\t{x.r.Slot}\t{x.r.Requested}"; }));

        Console.WriteLine($"{"categorie",-24}{"refs",9}{"ok",9}{"repli",9}{"manquant",10}{"illisible",10}{"exception",10}{"info",8}  distincts-manquants");
        int tot = 0, bad = 0;
        foreach (var g in res.GroupBy(x => x.r.Category).OrderBy(g => g.Key))
        {
            int n = g.Count(), ok = g.Count(x => x.st == St.Ok), fz = g.Count(x => x.st == St.OkFuzzy), mi = g.Count(x => x.st == St.Missing),
                un = g.Count(x => x.st == St.Undecodable), ex = g.Count(x => x.st == St.Exception), inf = g.Count(x => x.st == St.Info);
            var dm = g.Where(x => x.st == St.Missing).Select(x => x.r.Requested.ToLowerInvariant()).Distinct().Count();
            Console.WriteLine($"{g.Key,-24}{n,9}{ok,9}{fz,9}{mi,10}{un,10}{ex,10}{inf,8}  {dm}");
            tot += n; bad += mi + un;
        }
        Console.WriteLine($"{"TOTAL",-24}{tot,9}{res.Count(x => x.st == St.Ok),9}{res.Count(x => x.st == St.OkFuzzy),9}{res.Count(x => x.st == St.Missing),10}{res.Count(x => x.st == St.Undecodable),10}{res.Count(x => x.st == St.Exception),10}{res.Count(x => x.st == St.Info),8}");
        Console.WriteLine($"NIF lus : {NifsRead}, illisibles : {NifsUnreadable}");
        Console.WriteLine("par slot (références de NIF, hors tables/personnage) : " + string.Join(", ",
            res.Where(x => x.r.Source.Contains(".nif") && !x.r.Exact).GroupBy(x => x.r.Slot.Split(' ')[0]).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}")));
        Console.WriteLine(bad == 0 ? "TEXTURES 100 %" : $"TEXTURES ÉCARTS : {bad} (voir {outPath})");
        return bad == 0 ? 0 : 1;
    }
}
