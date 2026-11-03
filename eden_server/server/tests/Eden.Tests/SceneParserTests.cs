using Eden.Core.Data.Parsers;
using Xunit;

namespace Eden.Tests;

public class SceneParserTests
{
    private static readonly string SceneDir = FindSceneDir();

    private static string FindSceneDir()
    {
        // Remonte depuis le répertoire de sortie jusqu'au repo racine
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "server_us/Data/scene");
            if (Directory.Exists(candidate)) return candidate;
            var candidate2 = Path.Combine(dir.FullName, "../server_us/Data/scene");
            if (Directory.Exists(candidate2)) return Path.GetFullPath(candidate2);
            dir = dir.Parent;
        }
        return TestPaths.RepoRoot + "/server_us/Data/scene";
    }

    [Fact]
    public void Parse_S001_LoadsAllSections()
    {
        var path = Path.Combine(SceneDir, "S001.ini");
        var scene = SceneParser.Parse(path);

        Assert.Equal("S001", scene.MapCode);
        Assert.Equal(640, scene.MapWidth);
        Assert.NotEmpty(scene.Monsters);
        Assert.NotEmpty(scene.Npcs);
        Assert.NotEmpty(scene.Gateways);
        Assert.NotEmpty(scene.EventAreas);
        Assert.NotEmpty(scene.Patrols);
        Assert.NotEmpty(scene.ReviveLinks);
    }

    [Fact]
    public void Parse_S001_MonsterSpawnHasPosition()
    {
        var scene = SceneParser.Parse(Path.Combine(SceneDir, "S001.ini"));
        var spawn = scene.Monsters.First();

        Assert.True(spawn.MonsterId > 0);
        Assert.True(spawn.X != 0 || spawn.Y != 0);
        Assert.Contains(scene.Monsters, m => m.RespawnSeconds > 0);
        // col18/col19 : drapeau + textindex d'annonce (3997 « Pirate
        // Scouts… » sur les lignes d'événement dormantes de S001).
        Assert.Contains(scene.Monsters, m =>
            m.AnnounceFlag == 1 && m.AnnounceTextId == 3997);
    }

    [Fact]
    public void Parse_S001_NpcSpawnDecodesBig5Name()
    {
        var scene = SceneParser.Parse(Path.Combine(SceneDir, "S001.ini"));

        Assert.Contains(scene.Npcs, n => n.NpcId == 50360 && n.LocalId == -1);
        Assert.All(scene.Npcs, n => Assert.NotEqual(0, n.NpcId));
    }

    [Fact]
    public void Parse_S100_NpcZResolvedFromNearestScenePoint()
    {
        // La ligne [npc] n'a pas de colonne z : post-parse, Z =
        // hauteur du point de scène le plus proche — S100 (mine
        // intérieure) a [position] z=15.9942 à ~0.8 u du NPC 50004 ;
        // avant, z=0 posait PNJ et téléportés sous la map.
        var scene = SceneParser.Parse(Path.Combine(SceneDir, "S100.ini"));
        var npc = Assert.Single(scene.Npcs, n => n.NpcId == 50004);
        Assert.InRange(npc.Z, 15f, 17f);
    }

    [Fact]
    public void Parse_AllScenes_NoCrash()
    {
        var dir = SceneDir;

        var files = Directory.GetFiles(dir, "*.ini");
        Assert.NotEmpty(files);
        int monsterCount = 0;
        foreach (var f in files)
        {
            var s = SceneParser.Parse(f);
            monsterCount += s.Monsters.Count;
        }
        Assert.True(monsterCount > 100);
    }
}
