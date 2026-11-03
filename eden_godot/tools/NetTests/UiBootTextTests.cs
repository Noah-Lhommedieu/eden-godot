using EdenClient.Ui;
using Xunit;

namespace EdenClient.NetTests;

public class UiBootTextTests
{
    [Theory]
    [InlineData("Purple", "Purple")]
    [InlineData("purple", "Purple")]
    [InlineData("  BLUE ", "Blue")]
    [InlineData("", "Irongray")]
    [InlineData(null, "Irongray")]
    [InlineData("Neon", "Irongray")]
    public void Theme_Normalize_IsCanonical(string? raw, string want) =>
        Assert.Equal(want, UiThemeBoot.Normalize(raw));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void Theme_WaitsUntilHudAndAtlasExist(bool hud, bool atlas,
        bool wait) =>
        Assert.Equal(wait, UiThemeBoot.MustWait(hud, atlas));

    [Fact]
    public void Chat_ReplacesCjkItemNameKeepsRest()
    {
        var map = new Dictionary<string, string>
        {
            ["獲得"] = "Obtenu :", ["小紅藥水"] = "Petite potion rouge",
        };
        var r = ChatLocalizer.Localize("獲得 小紅藥水 x 3",
            c => map.GetValueOrDefault(c));
        Assert.Equal("Obtenu : Petite potion rouge x 3", r);
    }

    [Fact]
    public void Chat_UnknownCjkAndAsciiUntouched()
    {
        Assert.Equal("Obtenu : 神秘", ChatLocalizer.Localize("Obtenu : 神秘",
            _ => null));
        Assert.Equal("EXP : 12", ChatLocalizer.Localize("EXP : 12",
            _ => throw new InvalidOperationException()));
    }
}
