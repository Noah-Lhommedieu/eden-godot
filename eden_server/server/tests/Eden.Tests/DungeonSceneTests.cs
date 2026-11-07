using Eden.Core.Data.Parsers;
using Xunit;

namespace Eden.Tests;

/// <summary>Donjon S101 (Carrière d'Angor) : la scène EDENFR porte les
/// spawns fixes, les 2 boss et les events de fin ; le serveur ne les
/// invente pas.</summary>
public class DungeonSceneTests
{
    private static readonly string S101 = Path.Combine(TestPaths.RepoRoot,
        "..", "corpus", "fr-full", "data", "scene", "s101.ini");

    [Fact]
    public void S101_SpawnsBossesAndEvents()
    {
        if (!File.Exists(S101)) return; // corpus absent (CI sans données)
        var scene = SceneParser.Parse(S101);
        // 13 groupes de mobs + 2 boss + 1 minuteur d'event (60647,
        // vie 1 s → event 99) actifs au boot ; 60575 dort jusqu'à l'event.
        var active = scene.Monsters.Where(m => m.InitiallyActive).ToList();
        Assert.Equal(16, active.Count);
        Assert.Contains(active, m => m.MonsterId == 59001);
        Assert.Contains(active, m => m.MonsterId == 59002);
        Assert.Contains(scene.Monsters,
            m => !m.InitiallyActive && m.MonsterId == 60575);
        // Sortie vers S002 + événements 1 (esprit gardien) et 99 (boss).
        Assert.Contains(scene.Gateways, g => g.EventAreaId == 2);
        Assert.True(scene.Events.ContainsKey(1));
        Assert.True(scene.Events.ContainsKey(99));
    }

    /// <summary>Données par (donjon, intensité) : S101 (multi) et S701
    /// (solo) sont deux scènes aux monstres distincts ; les boss ont une
    /// route à un seul point (immobiles), les packs une vraie route.</summary>
    [Fact]
    public void SoloAndMulti_ScenesDiffer_PatrolsAndBosses()
    {
        if (!File.Exists(S101)) return;
        var dir = Path.GetDirectoryName(S101)!;
        var multi = SceneParser.Parse(S101);
        var solo = SceneParser.Parse(Path.Combine(dir, "s701.ini"));
        Assert.Empty(multi.Monsters.Select(m => m.MonsterId)
            .Intersect(solo.Monsters.Select(m => m.MonsterId)));
        var boss = multi.Monsters.First(m => m.MonsterId == 59001);
        Assert.Single(multi.Patrols[boss.PatrolId].Waypoints);
        var pack = multi.Monsters.First(m => m.MonsterId == 59617);
        Assert.True(multi.Patrols[pack.PatrolId].Waypoints.Count >= 3);
    }

    /// <summary>Colonne « fixe » des routes [mob_patrol_mode] : S101 = packs
    /// et boss sur emplacements fixes, 4 golems sur un vrai chemin (~10 %
    /// de mobs mobiles, ~85-90 % immobiles comme en jeu natif).</summary>
    [Fact]
    public void S101_PatrolFlags_StationaryVsPath()
    {
        if (!File.Exists(S101)) return;
        var sc = SceneParser.Parse(S101);
        Assert.True(sc.Patrols[1].IsStationary);     // pack de 3 vers
        Assert.True(sc.Patrols[14].IsStationary);    // boss
        Assert.False(sc.Patrols[9].IsStationary);    // golem en chemin
        var mob = sc.Monsters.Where(m => m.InitiallyActive).ToList();
        var moving = mob.Where(m => sc.Patrols.TryGetValue(m.PatrolId, out var r)
            && !r.IsStationary).Sum(m => Math.Max(1, m.Count));
        Assert.InRange(moving, 1, 8);
    }
}
