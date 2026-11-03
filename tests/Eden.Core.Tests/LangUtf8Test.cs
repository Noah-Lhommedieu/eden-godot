using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

public class LangUtf8Test
{
    private static string FindProject()
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10; i++)
        {
            var p = Path.Combine(dir, "gamedata");
            if (Directory.Exists(p)) return p;
            p = Path.Combine(dir, "godot_project");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        throw new DirectoryNotFoundException("godot_project");
    }

    [Fact]
    public void LangFrItemDecodesUtf8()
    {
        var root = FindProject();
        var p = Path.Combine(root, "data/db/lang/fr/t_item.ini");
        if (!File.Exists(p))
            p = Path.Combine(root, "gamedata/data/db/lang/fr/t_item.ini");
        var t = IniTable.ParseFile(p, EdenEncoding.Big5);
        var row = t.FindById(302);
        Assert.NotNull(row);
        Assert.Contains("Énergie", row![1]);
    }
}
