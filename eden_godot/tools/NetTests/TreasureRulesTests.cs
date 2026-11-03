using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Plan 03 : règles pures des coffres au sol.</summary>
public class TreasureRulesTests
{
    [Theory]
    [InlineData("G00001", "G00001")]
    [InlineData("G00006", "G00006")]
    [InlineData("g00010", "G00010")]
    [InlineData("", "G00001")]
    [InlineData(null, "G00001")]
    [InlineData("G00011", "G00001")]
    [InlineData("K30108", "G00001")]
    [InlineData("G0000X", "G00001")]
    public void Family_ValidOrFallback(string? code, string expected) =>
        Assert.Equal(expected, TreasureRules.Family(code));

    [Fact]
    public void ScaleFor_NormalizesToTarget()
    {
        Assert.Equal(1f, TreasureRules.ScaleFor(0f));
        Assert.Equal(1f, TreasureRules.ScaleFor(TreasureRules.TargetHeight));
        Assert.Equal(1f, TreasureRules.ScaleFor(1.05f));
        Assert.Equal(TreasureRules.TargetHeight / 0.4f,
            TreasureRules.ScaleFor(0.4f), 3);
        Assert.True(TreasureRules.ScaleFor(5f) < 1f);
    }

    [Fact]
    public void Label_ShowsCountOnlyForStacks()
    {
        Assert.Equal("Potion", TreasureRules.Label("Potion", 1));
        Assert.Equal("Potion ×5", TreasureRules.Label("Potion", 5));
    }

    [Fact]
    public void PickupTemplate_HasFrenchEntries()
    {
        var tsv = Path.Combine(AppContext.BaseDirectory, "..", "..", "..",
            "..", "..", "godot_project", "gamedata", "ui_text_en_fr.tsv");
        var lines = File.ReadAllLines(tsv);
        foreach (var k in new[] { TreasureRules.PickupTemplate(1),
                                  TreasureRules.PickupTemplate(2) })
            Assert.Contains(lines, l => l.StartsWith(k + "\t"));
    }
}
