using System.Text;
using EdenTools.ItemDictionary;

// usage : ItemDictionary <fr-full> <out.tsv> [<rapport.txt>]
var root = args.Length > 0 ? args[0] : throw new ArgumentException("usage: ItemDictionary <fr-full> <out.tsv> [report.txt]");
var outPath = args.Length > 1 ? args[1] : "items.tsv";
var res = ItemDictionaryBuilder.Build(root);
var sb = new StringBuilder(ItemDictionaryBuilder.Header).Append('\n');
foreach (var r in res.Rows) sb.Append(string.Join("\t", ItemDictionaryBuilder.ToCells(r))).Append('\n');
File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
var rep = new StringBuilder();
foreach (var kv in res.Counters.OrderBy(k => k.Key, StringComparer.Ordinal)) rep.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
foreach (var l in res.Report) rep.Append(l).Append('\n');
rep.Append("\n## par type\n");
foreach (var g in res.Rows.GroupBy(r => (r.TypeCode, r.TypeLabel)).OrderBy(g => int.TryParse(g.Key.TypeCode, out var k) ? k : -1))
    rep.Append($"{g.Key.TypeCode}\t{g.Key.TypeLabel}\t{g.Count()}\n");
rep.Append("\n## par famille\n");
foreach (var g in res.Rows.GroupBy(r => r.Family).OrderByDescending(g => g.Count())) rep.Append($"{g.Key}\t{g.Count()}\n");
rep.Append("\n## par tranche de niveau requis\n");
foreach (var g in res.Rows.GroupBy(r => r.RequiredLevel == 0 ? -1 : (r.RequiredLevel - 1) / 10).OrderBy(g => g.Key))
    rep.Append((g.Key < 0 ? "aucun" : $"{g.Key * 10 + 1}-{g.Key * 10 + 10}") + $"\t{g.Count()}\n");
rep.Append("\n## par qualite\n");
foreach (var g in res.Rows.GroupBy(r => (r.QualityCode, r.QualityLabel)).OrderBy(g => g.Key.QualityCode)) rep.Append($"{g.Key.QualityCode}\t{g.Key.QualityLabel}\t{g.Count()}\n");
rep.Append("\n## par table source\n");
foreach (var g in res.Rows.GroupBy(r => r.SourceTables).OrderByDescending(g => g.Count())) rep.Append($"{g.Key}\t{g.Count()}\n");
rep.Append("\n## par emplacement\n");
foreach (var g in res.Rows.Where(r => r.EquipLocCode > 0).GroupBy(r => (r.EquipLocCode, r.EquipSlot)).OrderBy(g => g.Key.EquipLocCode)) rep.Append($"{g.Key.EquipLocCode}\t{g.Key.EquipSlot}\t{g.Count()}\n");
rep.Append("\n## anomalies\n");
foreach (var g in res.Rows.SelectMany(r => r.Anomalies.Split(';', StringSplitOptions.RemoveEmptyEntries)).GroupBy(a => a).OrderByDescending(g => g.Count())) rep.Append($"{g.Key}\t{g.Count()}\n");
rep.Append($"\nrestreint_classe\t{res.Rows.Count(r => r.ClassMask != 0)}\n");
if (args.Length > 2) File.WriteAllText(args[2], rep.ToString(), new UTF8Encoding(false));
else Console.Write(rep);
return 0;
