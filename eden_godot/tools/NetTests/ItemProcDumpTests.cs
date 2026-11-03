using EdenClient.Data;
using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Sonde L-items : item équipement → enchant de proc → texte
/// affiché par le tooltip (corpus/fr-full). Sortie : /tmp/claude-1000/proc-dump.txt.</summary>
public class ItemProcDumpTests
{
    [Fact]
    public void Dump()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "corpus", "fr-full", "data", "db", "c_item.ini")))
            dir = dir.Parent;
        if (dir == null) return;
        var fr = Path.Combine(dir.FullName, "corpus", "fr-full");
        TextResolver.ResolveFile = rel => { var p = Path.Combine(fr, rel.ToLowerInvariant()); return File.Exists(p) ? p : null; };
        TextResolver.SetLanguage("fr");
        var items = ItemTable.Load(Path.Combine(fr, "data/db/c_item.ini"));
        var names = TextTable.Load(Path.Combine(fr, "data/db/t_item.ini"));
        var sb = new System.Text.StringBuilder();
        var n = 0;
        foreach (var kv in items.OrderBy(k => k.Key))
        {
            var d = kv.Value;
            if (d.UseEnchantId <= 0 || d.EquipLoc <= 0) continue;
            var t = EnchantTable.DescriptionOf(d.UseEnchantId) ?? "(null)";
            var e = EnchantTable.Get(d.UseEnchantId);
            var raw = e == null ? "(no c_enchant)" : string.Join("|", e.Raw.Skip(36).Take(12));
            sb.AppendLine($"{kv.Key}\t{(names.TryGetValue(kv.Key, out var r) ? r.Name : "?")}\tench={d.UseEnchantId}\tcdDs={d.CooldownDs}\t{t.Replace("\n"," ")}\t[{raw}]");
            n++;
        }
        var bs = new System.Text.StringBuilder();
        foreach (var kv in items.OrderBy(k => k.Key))
            if (kv.Value.BagCapacity > 0 || kv.Value.Kind == 43)
                bs.AppendLine($"{kv.Key}\t{(names.TryGetValue(kv.Key, out var rr) ? rr.Name : "?")}\tkind={kv.Value.Kind}\tloc={kv.Value.EquipLoc}\tcap={kv.Value.BagCapacity}\tuse={kv.Value.UseType}");
        File.WriteAllText("/tmp/claude-1000/bag-dump.txt", bs.ToString());
        sb.Insert(0, $"{n} items equip+enchant\n");
        File.WriteAllText("/tmp/claude-1000/proc-dump.txt", sb.ToString());
    }

    private static readonly int[] ProcItems =
    {
        23891, 23892, 27013, 28486, 23578, 24065, 24436, 26096, 22140,
        21873, 36567, 23886,
    };

    /// <summary>Item → proc affiché par le tooltip (T_Enchant de
    /// c_item col23) : chaque item a SON chance/durée/valeur ; sortie
    /// /tmp/claude-1000/proc-table.txt (chance, durée, valeurs,
    /// cooldown d'item, colonnes c_enchant 39-43).</summary>
    [Fact]
    public void ProcTextIsPerItem()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "corpus", "fr-full", "data", "db", "c_item.ini")))
            dir = dir.Parent;
        if (dir == null) return;
        var fr = Path.Combine(dir.FullName, "corpus", "fr-full");
        TextResolver.ResolveFile = rel => { var p = Path.Combine(fr, rel.ToLowerInvariant()); return File.Exists(p) ? p : null; };
        TextResolver.SetLanguage("fr");
        var items = ItemTable.Load(Path.Combine(fr, "data/db/c_item.ini"));
        var names = TextTable.Load(Path.Combine(fr, "data/db/t_item.ini"));
        var sb = new System.Text.StringBuilder();
        var chances = new HashSet<string>();
        foreach (var id in ProcItems)
        {
            var d = items[id];
            var t = EnchantTable.DescriptionOf(d.UseEnchantId);
            Assert.False(string.IsNullOrEmpty(t), $"item {id} sans texte de proc");
            var ch = System.Text.RegularExpressions.Regex.Matches(t!, @"(\d+) ?%").Select(m => m.Groups[1].Value + "%");
            var dur = System.Text.RegularExpressions.Regex.Match(t!, @"(\d+) ?(sec|s)\b").Groups[1].Value;
            var vals = System.Text.RegularExpressions.Regex.Matches(t!, @"\d[\d ]*").Select(m => m.Value.Trim());
            foreach (var c in ch) chances.Add(c);
            var e = EnchantTable.Get(d.UseEnchantId)!;
            sb.AppendLine($"{id} | {names[id].Name} | ench {d.UseEnchantId} | chance(s) {string.Join("/", ch)} | durée {(dur.Length == 0 ? "-" : dur + "s")} | nombres {string.Join(",", vals)} | cooldown item {(d.CooldownDs > 0 ? ItemTipFormat.Cooldown(d.CooldownDs) : "-")} | c_enchant[39..43]={string.Join("|", e.Raw.Skip(39).Take(5))} | texte: {t!.Replace("\n", " ")}");
        }
        File.WriteAllText("/tmp/claude-1000/proc-table.txt", sb.ToString());
        // 5 % n'est PAS universel : il existe au moins 1 %, 5 % et 12 %.
        Assert.Contains("1%", chances); Assert.Contains("5%", chances); Assert.Contains("12%", chances);
        Assert.Contains("8214", EnchantTable.DescriptionOf(items[23891].UseEnchantId));
        Assert.Contains("174", EnchantTable.DescriptionOf(items[23892].UseEnchantId));
    }
}
