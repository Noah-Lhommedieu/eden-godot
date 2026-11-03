using EdenClient.Ui;
using Xunit;
namespace EdenClient.NetTests;
public class ZzDump
{
    static void D(UiNodeSpec n, int d, System.Text.StringBuilder sb)
    {
        string St(UiStyleSpec? s) => s == null ? "-" : $"{s.Kind}:{s.Image}:{s.Parts.Count}:m{s.MarginLeft}";
        sb.AppendLine($"{new string(' ', d*2)}{n.Kind} {n.Name.Split('_').Last()} [{n.CeguiType}] a=({n.Anchors.AnchorLeft},{n.Anchors.OffsetLeft},{n.Anchors.AnchorTop},{n.Anchors.OffsetTop},{n.Anchors.AnchorRight},{n.Anchors.OffsetRight},{n.Anchors.AnchorBottom},{n.Anchors.OffsetBottom}) fr={St(n.Frame)} bg={St(n.Background)} img={n.Image} hov={St(n.Hover)} fs={n.FontSize} txt={n.Text} vis={n.Visible}");
        foreach (var c in n.AllChildren()) D(c, d+1, sb);
    }
    [Fact]
    public void Dump()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(dir!.FullName, "client_assets", "uidata"))) dir = dir.Parent;
        var ui = Path.Combine(dir.FullName, "client_assets", "uidata");
        var ctx = CeguiContext.Load(ui);
        foreach (var l in new[]{"MiniInfoWnd","FunctionWnd"})
        {
            var sb = new System.Text.StringBuilder();
            var b = new CeguiBuilder(ctx);
            D(b.BuildLayoutFile(EdenClient.Vfs.EdenVfs.ResolveInsensitive(ui, "layouts/"+l+".layout")!)!,0,sb);
            File.WriteAllText("/tmp/claude-1000/dump-"+l+".txt", sb.ToString());
        }
    }
}
