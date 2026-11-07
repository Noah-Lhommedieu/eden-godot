using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Zone.Handlers;
using Xunit;

namespace Eden.Tests;

/// <summary>Catalogue Solo/Multi des donjons (c_node EDENFR), règles
/// d'entrées journalières et wire des messages custom 9060/9061.</summary>
public class DungeonEntryTests
{
    private static readonly string NodeIni = Path.Combine(TestPaths.RepoRoot,
        "..", "corpus", "fr-full", "data", "db", "c_node.ini");

    [Fact]
    public void Catalog_PairsAngorMultiAndSolo()
    {
        if (!File.Exists(NodeIni)) return;
        var cat = new DungeonCatalog(GameDataParsers.ParseNodes(NodeIni));
        var d = cat.ByNode(101)!;
        Assert.Equal(701, d.SoloNode);
        Assert.Same(d, cat.ByNode(701));
        Assert.True(cat.IsMulti(101));
        Assert.False(cat.IsMulti(701));
        // Illusion Destructrice (type 18) et héroïque hors catalogue.
        Assert.Null(cat.ByNode(655));
        Assert.Null(cat.ByNode(603));
        Assert.True(cat.All.Count() >= 45);
    }

    [Fact]
    public void Remaining_ResetsOnNewDay()
    {
        Assert.Equal(10, DungeonEntryRules.Remaining(5, 0, 5));
        Assert.Equal(3, DungeonEntryRules.Remaining(5, 7, 5));
        Assert.Equal(0, DungeonEntryRules.Remaining(5, 12, 5));
        Assert.Equal(10, DungeonEntryRules.Remaining(4, 10, 5));
        Assert.Equal(1, DungeonEntryRules.DayIndex(
            new DateTime(1970, 1, 2, 23, 0, 0)));
    }

    [Fact]
    public void Offer_RoundTrips()
    {
        var o = new CNC_CZ_ClientDungeonOffer
        {
            SoloNode = 701, MultiNode = 101, SoloLeft = 4, MultiLeft = 10,
            Max = 10, Unlimited = true,
        };
        var os = new Eden.Core.Networking.OutBinStream();
        o.Serialize(os);
        var b = new CNC_CZ_ClientDungeonOffer();
        b.Deserialize(new Eden.Core.Networking.InBinStream(os.ToArray()));
        Assert.Equal((701, 101, 4, 10, 10, true),
            (b.SoloNode, b.MultiNode, b.SoloLeft, b.MultiLeft, b.Max,
             b.Unlimited));
    }
}
