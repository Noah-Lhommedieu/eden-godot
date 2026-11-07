using Eden.Core.Data.Parsers;
using Eden.Core.Game.Maps;

namespace Eden.Tests;

public class MapClassifierTests
{
    private static MapFacts F(int type, string name = "", params int[] respawn) =>
        new(1, type, name, respawn);

    [Theory]
    [InlineData(4, MapKind.GuildCity)]
    [InlineData(18, MapKind.Trial)]
    [InlineData(5, MapKind.Dungeon)]
    [InlineData(7, MapKind.Dungeon)]
    [InlineData(11, MapKind.SharedInstance)]
    [InlineData(14, MapKind.SpecialMode)]
    [InlineData(10, MapKind.SpecialMode)]
    [InlineData(8, MapKind.SpecialMode)]
    [InlineData(3, MapKind.Field)]
    [InlineData(99, MapKind.Unknown)]
    public void ByNodeType(int type, MapKind expected) =>
        Assert.Equal(expected, MapClassifier.Classify(F(type)));

    [Fact]
    public void IllusionSolo_IsTrial_EvenWhenType5() =>
        Assert.Equal(MapKind.Trial,
            MapClassifier.Classify(F(5, "Illusion Destructrice : Carrière Angor (Solo)")));

    [Fact]
    public void Type1_FastRespawnMedian20to90_IsSharedInstance() =>
        Assert.Equal(MapKind.SharedInstance,
            MapClassifier.Classify(F(1, "Labo Vingot", Enumerable.Repeat(20, 100).ToArray())));

    [Fact]
    public void Type1_NormalField_StaysField() =>
        Assert.Equal(MapKind.Field,
            MapClassifier.Classify(F(1, "Plaines", Enumerable.Repeat(5, 100).ToArray())));

    [Fact]
    public void Type1_FewSpawns_StaysField() =>
        Assert.Equal(MapKind.Field, MapClassifier.Classify(F(1, "x", 20, 20, 20)));

    [Theory]
    [InlineData(MapKind.Field, KillerKind.Monster, true)]
    [InlineData(MapKind.Field, KillerKind.Player, false)]
    [InlineData(MapKind.Field, KillerKind.Environment, false)]
    [InlineData(MapKind.Dungeon, KillerKind.Monster, false)]
    [InlineData(MapKind.SharedInstance, KillerKind.Monster, false)]
    [InlineData(MapKind.Trial, KillerKind.Monster, false)]
    [InlineData(MapKind.GuildCity, KillerKind.Monster, false)]
    [InlineData(MapKind.SpecialMode, KillerKind.Player, false)]
    public void ExpLossOnlyOnFieldPveDeath(MapKind m, KillerKind k, bool loses) =>
        Assert.Equal(loses, DeathRules.LosesExp(m, k));

    [Fact]
    public void NeverLosesGold() =>
        Assert.All(Enum.GetValues<MapKind>(), m =>
            Assert.All(Enum.GetValues<KillerKind>(), k => Assert.False(DeathRules.LosesGold(m, k))));

    /// <summary>Preuve sur le corpus : BTS 224, DD 216 et Labo Vingot 212 sont
    /// les SEULES instances partagées de type 1 ; Angor 101/603/630 = donjons ;
    /// 655 et 829 = épreuves ; 501/502 = villes de guilde ; les maps normales restent des champs.</summary>
    [Fact]
    public void RealCorpus_ClassifiesKnownMaps()
    {
        var db = Path.Combine(TestPaths.RepoRoot, "..", "corpus", "fr-full", "data");
        var nodeIni = Path.Combine(db, "db", "c_node.ini");
        if (!File.Exists(nodeIni)) return;
        var nodes = GameDataParsers.ParseNodes(nodeIni);
        MapKind Kind(int id)
        {
            var n = nodes.First(x => x.Id == id);
            var scene = Path.Combine(db, "scene", $"s{id:D3}.ini");
            var respawn = File.Exists(scene)
                ? SceneParser.Parse(scene).Monsters.Select(m => m.RespawnSeconds).ToArray()
                : Array.Empty<int>();
            return MapClassifier.Classify(new MapFacts(id, n.Type, n.Name, respawn));
        }
        // Le nom FR vient de t_node ; ici NodeDef.Name est en Big5, donc 655/829 passent par type 18 / nom.
        Assert.Equal(MapKind.SharedInstance, Kind(224));
        Assert.Equal(MapKind.SharedInstance, Kind(216));
        Assert.Equal(MapKind.SharedInstance, Kind(212));
        Assert.Equal(MapKind.Dungeon, Kind(101));
        Assert.Equal(MapKind.Dungeon, Kind(603));
        Assert.Equal(MapKind.Dungeon, Kind(630));
        Assert.Equal(MapKind.Trial, Kind(655));
        Assert.Equal(MapKind.Trial, Kind(829)); // illusion solo (type 5, nom Big5 毀滅幻境)
        Assert.Equal(MapKind.GuildCity, Kind(501));
        Assert.Equal(MapKind.GuildCity, Kind(502));
        Assert.Equal(MapKind.Field, Kind(2));
        Assert.Equal(MapKind.Field, Kind(55));
        Assert.Equal(MapKind.SpecialMode, Kind(96)); // Île de Sakura : mêlée PvP
        var shared = nodes.Where(n => n.Type == 1 && Kind(n.Id) == MapKind.SharedInstance)
            .Select(n => n.Id).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 212, 216, 224 }, shared);
    }
}
