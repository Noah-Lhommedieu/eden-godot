using EdenClient.Data.Tables;
using EdenClient.Vfs;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Chaîne de chargement M3 sur les vraies données EDENFR
/// (<c>data/scene/s002.ini → map/model/s002/s002.fsm + s002.ini →
/// map/sknodemap/s002.smp → map/model/S002/S002.area</c>) — casse mixte
/// résolue par <see cref="EdenVfs.ResolveInsensitive"/> comme au
/// runtime. Skippés si le corpus est absent.
/// </summary>
public class MapChainM3Tests
{
    private static string? FindCorpus()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "client_assets");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    private static string? At(string root, string rel) =>
        EdenVfs.ResolveInsensitive(root, rel);

    [Fact]
    public void S002_SceneIni_PointeVersFsm()
    {
        var root = FindCorpus();
        if (root == null) return;
        var p = At(root, "data/scene/s002.ini");
        Assert.NotNull(p);
        var scene = SceneTable.Load(p!);
        Assert.Equal("S002.fsm", scene.FsmFile);
        Assert.Equal("V7", scene.MapVersion);
        Assert.True(scene.EventAreas.Count > 0);
        // [position] est vide dans S002 FR — le spawn se résout via
        // les event areas (REVIVE → GATEWAY, ordre natif).
        var revive = scene.EventAreas.First(e => e.Type == "REVIVE");
        Assert.True(revive.X > 0 && revive.Y > 0);
        // S002 = capitale : PNJ + monstres présents dans la scène FR.
        Assert.True(scene.Npcs.Count > 0, $"Npcs={scene.Npcs.Count}");
    }

    [Fact]
    public void S002_Fsm_TerrainParsé()
    {
        var root = FindCorpus();
        if (root == null) return;
        var scene = SceneTable.Load(At(root, "data/scene/s002.ini")!);
        var p = At(root, $"map/model/s002/{scene.FsmFile}");
        Assert.NotNull(p);
        var f = FsmFile.Load(p!)!;
        Assert.NotNull(f);
        Assert.True(f.Tiles.Count > 0);
        Assert.Contains(f.Tiles, t => t.Kind == FsmFile.TileKindTerrain);
        // Le S002.fsm authentique (CRC du manifeste) est entièrement framé :
        // la « queue V7 » n'existait que dans l'ancienne copie dégradée.
        Assert.Equal(0, f.TrailingBytes);
    }

    [Fact]
    public void S002_NodeMap_WalkableRéel()
    {
        var root = FindCorpus();
        if (root == null) return;
        var p = At(root, "map/sknodemap/s002.smp");
        Assert.NotNull(p);
        var m = NodeMapFile.Load(p!);
        Assert.NotNull(m);
        // Cellule au point de spawn natif (première REVIVE event area).
        var scene = SceneTable.Load(At(root, "data/scene/s002.ini")!);
        var born = scene.EventAreas.First(e => e.Type == "REVIVE");
        Assert.False(m!.IsBlocked(born.X, born.Y),
            $"spawn ({born.X},{born.Y}) bloqué par le .smp EDENFR");
    }

    [Fact]
    public void S002_AreaFile_Grille()
    {
        var root = FindCorpus();
        if (root == null) return;
        var p = At(root, "map/model/S002/S002.area");
        Assert.NotNull(p);
        var a = MapAreaFile.Load(p!);
        Assert.True(a.GridWidth > 0 && a.GridHeight > 0);
    }

    [Fact]
    public void S002_EnvIni_PhasesCiel()
    {
        var root = FindCorpus();
        if (root == null) return;
        var p = At(root, "map/model/s002/s002.ini");
        Assert.NotNull(p);
        var env = MapEnvIni.Load(p!);
        Assert.True(env.Sections.Count > 0);
        var day = env.GetPhase(MapEnvIni.Phase.Day);
        Assert.NotNull(day);
    }

    [Fact]
    public void S002_HauteurTerrain_AuSpawn()
    {
        var root = FindCorpus();
        if (root == null) return;
        var f = FsmFile.Load(At(root, "map/model/s002/s002.fsm")!)!;
        var scene = SceneTable.Load(At(root, "data/scene/s002.ini")!);
        var born = scene.EventAreas.First(e => e.Type == "REVIVE");
        Assert.True(TerrainSampler.TryGetHeight(f, born.X, born.Y,
            out var z), $"pas de hauteur à ({born.X},{born.Y})");
        // Cohérence : le z terrain ≈ le z du point de spawn (±2 u).
        Assert.True(MathF.Abs(z - born.Z) < 2f,
            $"terrain z={z} vs spawn z={born.Z}");
    }

    [Fact]
    public void S137_ScenePropre_GeometrieDeclareeS055()
    {
        var root = FindCorpus();
        if (root == null) return;
        // Node sans dossier map/model propre dont la scène déclare sa
        // géométrie partagée ([globe_data] col0) — mécanisme EDENFR
        // utilisé par 206 nodes : MapLoader charge la géométrie
        // déclarée en gardant la scène propre du node.
        var scene = SceneTable.Load(At(root, "data/scene/s137.ini")!);
        Assert.Equal("S055.fsm", scene.FsmFile);
        Assert.NotNull(At(root, "map/model/s055/s055.fsm"));
        // La scène du node est distincte de celle du donneur :
        // portails/zones propres à S137.
        var donor = SceneTable.Load(At(root, "data/scene/s055.ini")!);
        Assert.NotEmpty(scene.EventAreas);
        Assert.NotEqual(
            donor.EventAreas.Select(a => (a.Type, a.X, a.Y, a.Z)),
            scene.EventAreas.Select(a => (a.Type, a.X, a.Y, a.Z)));
    }
}
