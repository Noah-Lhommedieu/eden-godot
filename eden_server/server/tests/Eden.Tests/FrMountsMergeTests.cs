using System.Text;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

/// <summary>Montures EDENFR « Hors archive » : items + enchants de ride
/// fusionnés depuis corpus/fr-full pour que le serveur sache les monter.</summary>
public class FrMountsMergeTests
{
    private static readonly string FrDb = Path.GetFullPath(Path.Combine(
        TestPaths.RepoRoot, "..", "corpus", "fr-full", "data", "db"));

    static FrMountsMergeTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void HorsArchiveMounts_AreMountableAfterMerge()
    {
        if (!Directory.Exists(FrDb)) return;   // pas de corpus : sans objet
        var gd = new GameDataService(TestPaths.ServerUsData + "/db_decoded");
        gd.LoadAll();
        // Avant fusion : Kitsune de l'Aube (1290) et Renard blanc (48813)
        // sont inconnus de l'oracle US.
        Assert.DoesNotContain(gd.Items, i => i.Id == 1290);
        gd.MergeFrMounts(FrDb);
        var enchants = new EnchantService(gd.Enchants);
        foreach (var (item, enchant, rideKey) in new[]
                 { (1290, 11354, 253020), (48813, 60419, 268010) })
        {
            var def = gd.Items.Single(i => i.Id == item);
            Assert.Equal(enchant, def.UseEnchantId);
            Assert.Equal(rideKey, enchants.GetRideInfo(enchant)?.RideKey);
        }
    }

    [Fact]
    public void All90HorsArchiveMounts_ResolveToRide()
    {
        var tsv = Path.GetFullPath(Path.Combine(TestPaths.RepoRoot, "..",
            "docs", "reports", "FIX-F-montures-hors-archive.tsv"));
        if (!Directory.Exists(FrDb) || !File.Exists(tsv)) return;
        var gd = new GameDataService(TestPaths.ServerUsData + "/db_decoded");
        gd.LoadAll();
        gd.MergeFrMounts(FrDb);
        var enchants = new EnchantService(gd.Enchants);
        var rows = File.ReadAllLines(tsv).Skip(1)
            .Select(l => l.Split('\t')).ToList();
        Assert.Equal(90, rows.Count);
        var bad = rows.Where(r =>
            gd.Items.FirstOrDefault(i => i.Id == int.Parse(r[0])) is not { } d
            || enchants.GetRideInfo(d.UseEnchantId)?.RideKey
               != int.Parse(r[2])).Select(r => r[0]).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void Merge_NeverReplacesExistingIds()
    {
        if (!Directory.Exists(FrDb)) return;
        var gd = new GameDataService(TestPaths.ServerUsData + "/db_decoded");
        gd.LoadAll();
        var before = gd.Items.First(i => i.Id == 1);
        gd.MergeFrMounts(FrDb);
        Assert.Same(before, gd.Items.Single(i => i.Id == 1));
    }
}
