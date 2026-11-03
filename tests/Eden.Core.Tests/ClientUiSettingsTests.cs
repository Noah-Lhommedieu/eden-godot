using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

public class ClientUiSettingsTests
{
    [Fact]
    public void RoundTrip_KeepsHudsWindowsOptions()
    {
        var s = new ClientUiSettings();
        s.Huds["Minimap/Root"] = new HudPlacement { X = 0.25f, Y = 0.5f };
        s.Windows["Inventory"] = new WindowPlacement { X = 100, Y = 50 };
        s.Options["note"] = "x";
        var r = ClientUiSettings.FromJson(s.ToJson());
        Assert.Equal(0.25f, r.Huds["Minimap/Root"].X);
        Assert.Equal(50, r.Windows["Inventory"].Y);
        Assert.Equal("x", r.Options["note"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    public void BadInput_GivesEmpty(string json)
    {
        var r = ClientUiSettings.FromJson(json);
        Assert.Empty(r.Huds);
    }

    [Fact]
    public void SaveLoad_Atomic_And_Reset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eden-ui-" + Guid.NewGuid());
        var path = Path.Combine(dir, ClientUiSettings.FileName);
        var s = new ClientUiSettings();
        s.Huds["A/B"] = new HudPlacement { X = 0.1f, Y = 0.2f };
        s.Options["k"] = "v";
        s.Save(path);
        Assert.False(File.Exists(path + ".tmp"));
        var r = ClientUiSettings.Load(path);
        Assert.Equal(0.2f, r.Huds["A/B"].Y);
        r.ResetPositions();
        Assert.Empty(r.Huds);
        Assert.Equal("v", r.Options["k"]);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Clamp_KeepsInsideViewport()
    {
        Assert.Equal((0f, 0f), ClientUiSettings.Clamp(-50, -9, 100, 50, 1280, 720));
        Assert.Equal((1180f, 670f), ClientUiSettings.Clamp(5000, 5000, 100, 50, 1280, 720));
        // plus grand que l'écran : reste >= 24 px visibles
        var (x, _) = ClientUiSettings.Clamp(5000, 0, 2000, 50, 1280, 720);
        Assert.Equal(1256f, x);
    }
}
