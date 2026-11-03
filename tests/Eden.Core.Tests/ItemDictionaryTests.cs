using EdenTools.ItemDictionary;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Dictionnaire d'items EDENFR (tools/item-dictionary) construit sur corpus/fr-full s'il est présent :
/// ids uniques, nom non vide, libellé de type non vide, aucun tab/retour ligne dans les champs.
/// </summary>
public class ItemDictionaryTests
{
    private static string? FindFrFull()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, "corpus", "fr-full");
            if (File.Exists(Path.Combine(p, "data", "db", "c_item.ini"))) return p;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Dictionary_FrFull_IdsUniques_NomsEtTypesNonVides()
    {
        var root = FindFrFull();
        if (root == null) return; // corpus absent : rien à vérifier
        var res = ItemDictionaryBuilder.Build(root);

        Assert.True(res.Rows.Count > 20000, $"{res.Rows.Count} items");
        Assert.Empty(res.Rows.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(res.Rows.Where(r => string.IsNullOrWhiteSpace(r.Name)).Select(r => r.Id));
        Assert.Empty(res.Rows.Where(r => string.IsNullOrWhiteSpace(r.TypeLabel)).Select(r => r.Id));
        foreach (var r in res.Rows)
        {
            var cells = ItemDictionaryBuilder.ToCells(r);
            Assert.Equal(ItemDictionaryBuilder.Header.Split('\t').Length, cells.Length);
            Assert.DoesNotContain(cells, c => c.Contains('\t') || c.Contains('\n') || c.Contains('\r'));
        }
    }
}
