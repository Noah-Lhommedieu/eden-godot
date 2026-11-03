using EdenClient.Ui;
using Xunit;

namespace EdenClient.NetTests;

public class HudModelTests
{
    private static string UIData()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, "client_assets", "uidata");
            if (Directory.Exists(p))
                return p;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("client_assets/uidata");
    }

    private static readonly Lazy<CeguiContext> Ctx = new(() =>
        CeguiContext.Load(UIData()));

    [Fact]
    public void Formats()
    {
        Assert.Equal("14607/14690", HudBinder.Bar(14607, 14690));
        Assert.Equal(0f, HudBinder.Fraction(5, 0));
        Assert.Equal(1f, HudBinder.Fraction(9, 3));
        Assert.Equal("1:30", HudBinder.Duration(90));
        Assert.Equal("12s", HudBinder.Duration(12));
    }

    private static UiNodeSpec? Find(UiNodeSpec n, string name)
    {
        if (n.Name == name) return n;
        foreach (var c in n.AllChildren())
            if (Find(c, name) is { } f) return f;
        return null;
    }

    [Fact]
    public void AllP0Layouts_Build_And_Bound_Nodes_Exist()
    {
        var data = new MockHudData();
        foreach (var p in HudBinder.Panels)
        {
            var path = EdenClient.Vfs.EdenVfs.ResolveInsensitive(UIData(),
                $"layouts/{p.Layout}.layout");
            Assert.NotNull(path);
            var b = new CeguiBuilder(Ctx.Value);
            var spec = b.BuildLayoutFile(path!);
            Assert.NotNull(spec);
            foreach (var v in HudBinder.Compute(p.Id, data))
            {
                var full = HudBinder.FullName(spec!.Name, v.Node);
                Assert.True(Find(spec, full) != null,
                    $"{p.Id}: nœud absent {full}");
            }
        }
    }
}
