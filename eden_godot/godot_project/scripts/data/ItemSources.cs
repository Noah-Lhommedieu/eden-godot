using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Data;

/// <summary>
/// « Comment obtenir cet item » — index inversé construit depuis les
/// tables client : drops monstres/coffres (<c>c_dropitem.ini</c>, même
/// grammaire que le parseur serveur), boutiques PNJ (<c>C_Store</c>),
/// récompenses de quêtes (<c>c_mission</c>) et recettes
/// (<c>c_familycombine</c>).
/// </summary>
public static class ItemSources
{
    private static Dictionary<int, List<string>>? _idx;

    /// <summary>Lignes lisibles (déjà localisées) pour un item.</summary>
    public static IReadOnlyList<string> Of(int itemId)
    {
        _idx ??= Build();
        return _idx.TryGetValue(itemId, out var l)
            ? l : (IReadOnlyList<string>)Array.Empty<string>();
    }

    private static void Add(Dictionary<int, List<string>> d, int item,
        string line)
    {
        if (item <= 0) return;
        if (!d.TryGetValue(item, out var l)) d[item] = l = new();
        if (l.Count < 12 && !l.Contains(line)) l.Add(line);
    }

    private static Dictionary<int, List<string>> Build()
    {
        var d = new Dictionary<int, List<string>>();
        try { Drops(d); } catch (Exception e) { TextResolver.Log?.Invoke($"[sources] drops : {e.Message}"); }
        try
        {
            foreach (var s in StoreTable.All)
                foreach (var it in s.Items)
                    Add(d, it.ItemId, UiTextEn.Translate("Shop") + $" #{s.Id}");
        }
        catch (Exception e) { TextResolver.Log?.Invoke($"[sources] store : {e.Message}"); }
        try
        {
            foreach (var m in MissionTable.All)
                foreach (var r in m.Rewards)
                    if (r.Name is "RewardItem" or "RewardChooseItem" &&
                        r.Args.Count > 0 && int.TryParse(r.Args[0], out var id))
                        Add(d, id, UiTextEn.Translate("Quest") + " : " +
                            (TextResolver.MissionTitle(m.Id) ?? $"#{m.Id}"));
        }
        catch (Exception e) { TextResolver.Log?.Invoke($"[sources] mission : {e.Message}"); }
        try
        {
            var path = TextResolver.ResolveFile?.Invoke("data/db/c_familycombine.ini");
            if (path != null)
                foreach (var r in CraftTable.LoadCombine(path).Values)
                    Add(d, r.ProductItemId, UiTextEn.Translate("Craft") + " : " +
                        (TextResolver.ItemName(r.ProductItemId) ?? r.ProductName));
        }
        catch (Exception e) { TextResolver.Log?.Invoke($"[sources] craft : {e.Message}"); }
        return d;
    }

    private static void Drops(Dictionary<int, List<string>> d)
    {
        var path = TextResolver.ResolveFile?.Invoke("data/db/c_dropitem.ini");
        if (path == null) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        foreach (var row in table.Rows)
        {
            if (row.Length < 14 || !int.TryParse(row[0], out var id)) continue;
            var mobName = TextResolver.MonsterName(id);
            var src = mobName != null
                ? UiTextEn.Translate("Dropped by") + " : " + mobName
                : UiTextEn.Translate("Opened from") + " : " +
                  (TextResolver.ItemName(id) ?? $"#{id}");
            var i = 10;
            while (i + 3 < row.Length)
            {
                var bNum = int.TryParse(row[i + 1], out var itemB);
                var aNum = double.TryParse(row[i], System.Globalization.NumberStyles.Float, inv, out var fa);
                var cNum = double.TryParse(row[i + 2], System.Globalization.NumberStyles.Float, inv, out _);
                var dNum = double.TryParse(row[i + 3], System.Globalization.NumberStyles.Float, inv, out _);
                if (aNum && bNum && itemB > 0 && !cNum)
                { Add(d, itemB, src); i += 4; }
                else if (aNum && !bNum && cNum && dNum)
                { Add(d, (int)fa, src); i += 4; }
                else i++;
            }
        }
    }
}
