using EdenClient.Windows;
using Xunit;

namespace EdenClient.NetTests;

public class VendingViewTests
{
    private static readonly Dictionary<int, (float X, float Y, float Radius)> R =
        new() { [600] = (100f, 200f, 10f), [601] = (500f, 500f, 5f) };

    [Fact]
    public void RegionAt_InsideOutsideAndNull()
    {
        Assert.Equal(600, VendingView.RegionAt(R, 105, 205)!.Value.Type);
        Assert.Equal(601, VendingView.RegionAt(R, 500, 504)!.Value.Type);
        Assert.Null(VendingView.RegionAt(R, 0, 0));
        Assert.Null(VendingView.RegionAt(null, 105, 205));
        // bord exact inclus (<=)
        Assert.NotNull(VendingView.RegionAt(R, 110, 200));
    }

    [Theory]
    [InlineData(1, 2, null)]
    [InlineData(0, 0x13, "Stall: stalls are only allowed on map S001.")]
    [InlineData(0, 0x99, "Stall: refused (0x99).")]
    public void StatusKey(byte a, byte d, string? k) =>
        Assert.Equal(k, VendingView.StatusKey(a, d));

    [Theory]
    [InlineData("", 0)]
    [InlineData("1 500", 1500)]
    [InlineData("12,000", 12000)]
    [InlineData("-3", -1)]
    [InlineData("1000000000", -1)]
    [InlineData("abc", -1)]
    public void ParsePrice(string t, int e) =>
        Assert.Equal(e, VendingView.ParsePrice(t));

    [Fact]
    public void Title_And_Line()
    {
        Assert.Equal(90, VendingView.ClampTitle(new string('a', 200)).Length);
        Assert.Equal("x", VendingView.ClampTitle("  x "));
        Assert.Equal("Potion ×5 — 1,000".Replace(",", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator),
            VendingView.GoodLine("Potion", 5, 1000));
        Assert.Equal("Sword — 3", VendingView.GoodLine("Sword", 1, 3));
    }
}
