using System.Numerics;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du parseur <see cref="FsmFile"/> sur les vraies données
/// (<c>raw_assets/map/model/Sxxx/Sxxx.fsm</c> — symlink vers le client
/// Reforged). Skippés si raw_assets est absent.
/// </summary>
public class FsmTests
{
    private static string? FindRawAssets()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "raw_assets");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    private static FsmFile? Load(string map)
    {
        var root = FindRawAssets();
        if (root == null) return null;
        var p = Path.Combine(root, "map", "model", map, $"{map}.fsm");
        return File.Exists(p) ? FsmFile.Load(p) : null;
    }

    [Fact]
    public void S009_HeaderAndCounts()
    {
        var f = Load("S009");
        if (f == null) return; // raw_assets absent — skip silencieux
        Assert.Equal("V20100824", f.Version);
        Assert.Equal(3u, f.FormatRevision);
        Assert.Equal(289u, f.Header.GridVerticesX);
        Assert.Equal(289u, f.Header.GridVerticesY);
        Assert.Equal(2f, f.Header.Scale);
        Assert.Equal(134, f.Header.RecordCount);
        Assert.Equal(134, f.Tiles.Count);
        Assert.Equal(9, f.TilesPerRow);
        Assert.Equal(64f, f.TileWorldSize);
        // kind=0 d'abord (9×9), puis kind=1 (eau).
        Assert.Equal(81, f.Tiles.Count(t => t.Kind == FsmFile.TileKindTerrain));
        Assert.Equal(53, f.Tiles.Count(t => t.Kind == FsmFile.TileKindWater));
        Assert.All(f.Tiles.Take(81),
            t => Assert.Equal(FsmFile.TileKindTerrain, t.Kind));
        Assert.All(f.Tiles.Skip(81),
            t => Assert.Equal(FsmFile.TileKindWater, t.Kind));
        Assert.Equal(2, f.GrassTypes.Count);
        Assert.Equal("UE704M.nif", f.GrassTypes[0].ModelName);
        Assert.Equal("UE704MN.nif", f.GrassTypes[1].ModelName);
        Assert.Equal(3292, f.GrassInstances.Count);
        // Spot-check record GRS 0 (hex) : typeId 0, pos, scale.
        var g0 = f.GrassInstances[0];
        Assert.Equal(0u, g0.TypeId);
        Assert.Equal(66.27f, g0.Position.X, 1e-3f);
        Assert.Equal(69.79f, g0.Position.Y, 1e-3f);
        Assert.Equal(15.549f, g0.Position.Z, 1e-3f);
        Assert.Equal(1.16f, g0.Scale, 1e-3f);
        Assert.Equal(2321, f.SceneObjects.Count);
        Assert.Equal("UE013", f.SceneObjects[0].Name);
        // Spot-check OBJ 0 : pos + yaw ~π (matrice quasi -identité xy).
        var o0 = f.SceneObjects[0];
        Assert.Equal(9.728f, o0.Position.X, 1e-3f);
        Assert.Equal(418.116f, o0.Position.Y, 1e-3f);
        Assert.Equal(84.498f, o0.Position.Z, 1e-3f);
        Assert.Equal(0.85f, o0.Scale, 1e-3f);
    }

    [Fact]
    public void S001_HeaderAndCounts()
    {
        var f = Load("S001");
        if (f == null) return;
        Assert.Equal("V20100329", f.Version);
        Assert.Equal(2u, f.FormatRevision);
        Assert.Equal(321u, f.Header.GridVerticesX);
        Assert.Equal(177, f.Tiles.Count);
        Assert.Equal(10, f.TilesPerRow);
        Assert.Equal(100, f.Tiles.Count(t => t.Kind == 0));
        Assert.Equal(3, f.GrassTypes.Count);
        Assert.Equal(5160, f.GrassInstances.Count);
        Assert.Equal(3769, f.SceneObjects.Count);
        Assert.Equal("UB124", f.SceneObjects[0].Name);
    }

    [Fact]
    public void S009_FirstTile_VertexAndSlots()
    {
        var f = Load("S009");
        if (f == null) return;
        var t = f.Tiles[0];
        Assert.Equal(33, t.GridW);
        Assert.Equal(33, t.GridH);
        Assert.Equal(2f, t.CellSize);
        var v0 = t.GetVertex(0, 0);
        // Positions absolues — v0 = origine de la tuile.
        Assert.Equal(0f, v0.Position.X);
        Assert.Equal(0f, v0.Position.Y);
        Assert.Equal(52.922f, v0.Position.Z, 1e-3f);
        // Attr #2 = normale quasi-unitaire.
        Assert.True(MathF.Abs(v0.Normal.Length() - 1f) < 5e-3f,
            $"|n|={v0.Normal.Length()}");
        // Vertex voisin : x+=cellSize, même y.
        var v1 = t.GetVertex(1, 0);
        Assert.Equal(2f, v1.Position.X);
        var v33 = t.GetVertex(0, 1);
        Assert.Equal(0f, v33.Position.X);
        Assert.Equal(2f, v33.Position.Y);
        // TexSlots (spec §3 + vérif hex).
        Assert.Equal("mount_04.dds", t.GetTexSlot(0).Name);
        Assert.Equal("dust_04.dds", t.GetTexSlot(1).Name);
        Assert.Equal("dust_08.dds", t.GetTexSlot(2).Name);
        Assert.Equal("grass_05.dds", t.GetTexSlot(3).Name);
        Assert.True(t.GetTexSlot(0).Used);
        Assert.Equal(9f, t.GetTexSlot(0).UvScale);
        // Slot 4 inutilisé sur la tuile 0.
        Assert.False(t.GetTexSlot(4).Used);
        // …mais utilisé (road_04) sur la tuile (4,1) — constaté au dump.
        var road = f.GetTerrainTile(4, 1);
        Assert.NotNull(road);
        Assert.Equal("road_04.dds", road!.GetTexSlot(4).Name);
    }

    [Fact]
    public void S002_ObjMatrix_ColumnsFollowParapetPath()
    {
        var f = Load("S002");
        if (f == null) return;
        var rail = f.SceneObjects.Single(o => o.Name == "UB107" &&
            MathF.Abs(o.Position.X - 294.02655f) < 1e-3f &&
            MathF.Abs(o.Position.Y - 318.68466f) < 1e-3f);
        var next = f.SceneObjects.Single(o => o.Name == "UB107" &&
            MathF.Abs(o.Position.X - 298.47287f) < 1e-3f &&
            MathF.Abs(o.Position.Y - 324.15244f) < 1e-3f);
        var tangent = Vector3.Normalize(next.Position - rail.Position);
        // Validé sur S002 : les colonnes sont les axes locaux ; local +X
        // suit la courbe des parapets. La ligne 0 (ancienne transposée)
        // le désoriente fortement.
        Assert.True(Vector3.Dot(rail.Rotation.Column0, tangent) > 0.99f,
            $"column0·tangent={Vector3.Dot(rail.Rotation.Column0, tangent)}");
        var row0 = new Vector3(rail.Rotation.M11, rail.Rotation.M12,
            rail.Rotation.M13);
        Assert.True(MathF.Abs(Vector3.Dot(row0, tangent)) < 0.4f,
            $"row0·tangent={Vector3.Dot(row0, tangent)}");
    }

    [Fact]
    public void S009_GetHeight_VertexAndBilinear()
    {
        var f = Load("S009");
        if (f == null) return;
        // Pile sur un vertex.
        Assert.True(TerrainSampler.TryGetHeight(f, 0f, 0f, out var z));
        Assert.Equal(52.922f, z, 1e-3f);
        // Milieu du premier quad → moyenne des 4 coins.
        var t = f.GetTerrainTile(0, 0)!;
        var expected = (t.GetVertex(0, 0).Position.Z +
                        t.GetVertex(1, 0).Position.Z +
                        t.GetVertex(0, 1).Position.Z +
                        t.GetVertex(1, 1).Position.Z) / 4f;
        Assert.True(TerrainSampler.TryGetHeight(f, 1f, 1f, out var zm));
        Assert.Equal(expected, zm, 1e-3f);
        // Hors carte.
        Assert.False(TerrainSampler.TryGetHeight(f, -10f, 0f, out _));
    }

    [Fact]
    public void S009_GetHeight_BorderContinuity()
    {
        var f = Load("S009");
        if (f == null) return;
        // Bord de tuile à y=64 : la tuile (0,0) et (0,1) partagent la
        // rangée de vertices — hauteurs identiques des deux côtés.
        Assert.True(TerrainSampler.TryGetHeight(f, 10f, 63.999f, out var a));
        Assert.True(TerrainSampler.TryGetHeight(f, 10f, 64.001f, out var b));
        Assert.True(MathF.Abs(a - b) < 0.05f,
            $"discontinuité au bord : {a} vs {b}");
    }

    [Fact]
    public void S009_WaterTile_HasWaterTextures()
    {
        var f = Load("S009");
        if (f == null) return;
        var water = f.Tiles.Last();
        Assert.Equal(FsmFile.TileKindWater, water.Kind);
        var names = Enumerable.Range(0, 5)
            .Select(i => water.GetTexSlot(i).Name).ToArray();
        Assert.Contains(names, n => n.Contains("water"));
    }

    [Fact]
    public void S009_Raycast_VerticalRay()
    {
        var f = Load("S009");
        if (f == null) return;
        // Rayon vertical descendant au-dessus du point (10, 10).
        Assert.True(TerrainSampler.TryGetHeight(f, 10f, 10f, out var gz));
        var hit = TerrainSampler.RaycastHeight(f,
            new Vector3(10f, 10f, gz + 100f),
            new Vector3(0f, 0f, -1f),
            out var p);
        Assert.True(hit);
        Assert.Equal(gz, p.Z, 0.1f);
    }
}

/// <summary>Tests math purs de <see cref="EdenClient.Player.InterpBuffer"/>.</summary>
public class InterpBufferTests
{
    [Fact]
    public void Empty_ReturnsFalse()
    {
        var b = new EdenClient.Player.InterpBuffer();
        Assert.False(b.Sample(1.0, out _, out _, out _, out _));
    }

    [Fact]
    public void SingleSample_Clamp()
    {
        var b = new EdenClient.Player.InterpBuffer();
        b.Push(1.0, 10f, 0f, 0f, 1f);
        Assert.True(b.Sample(2.0, out var x, out _, out _, out var yaw));
        Assert.Equal(10f, x);
        Assert.Equal(1f, yaw);
    }

    [Fact]
    public void TwoSamples_LerpsAtRenderTime()
    {
        var b = new EdenClient.Player.InterpBuffer
        {
            RenderDelay = 0.15,
        };
        b.Push(1.0, 0f, 0f, 0f, 0f);
        b.Push(1.2, 10f, 0f, 0f, 0f);
        // renderTime = now - delay = 1.1 → moitié de l'intervalle.
        Assert.True(b.Sample(1.25, out var x, out _, out _, out _));
        Assert.Equal(5f, x, 1e-4f);
    }

    [Fact]
    public void YawWraps_ShortestPath()
    {
        var b = new EdenClient.Player.InterpBuffer();
        var a = MathF.PI * 0.95f;   // ~171°
        var c = -MathF.PI * 0.95f;  // ~-171° → +18° de delta court
        var d = EdenClient.Player.InterpBuffer.DeltaYaw(a, c);
        Assert.True(d > 0f && d < 0.4f, $"delta yaw={d}");
    }

    [Fact]
    public void Extrapolation_Bounded()
    {
        var b = new EdenClient.Player.InterpBuffer
        {
            RenderDelay = 0.0,
            MaxExtrapolation = 0.25,
        };
        b.Push(1.0, 0f, 0f, 0f, 0f);
        b.Push(1.1, 10f, 0f, 0f, 0f);
        // Dans la fenêtre d'extrapolation (0.05 au-delà).
        Assert.True(b.Sample(1.15, out var x1, out _, out _, out _));
        Assert.True(x1 > 10f && x1 <= 20f);
        // Au-delà : clamp sur le dernier état.
        Assert.True(b.Sample(2.0, out var x2, out _, out _, out _));
        Assert.Equal(10f, x2);
    }

    [Fact]
    public void PushMovement_WalksToDst_NotSnaps()
    {
        // Sémantique 326 : cur@now + dst@now+travel — le mob doit
        // MARCHER vers dst (vitesse 5 u/s, dist 10 → arrive à t=3).
        var b = new EdenClient.Player.InterpBuffer
        {
            RenderDelay = 0.0,
        };
        b.PushMovement(1.0, 0f, 0f, 0f, 0f, 3.0, 10f, 0f, 0f);
        Assert.True(b.SampleAt(2.0, out var x, out _, out _, out _));
        Assert.Equal(5f, x, 1e-4f);
        Assert.True(b.SampleAt(3.5, out x, out _, out _, out _));
        Assert.Equal(10f, x, 1e-4f);
    }

    [Fact]
    public void PushMovement_DropsStalePrediction()
    {
        var b = new EdenClient.Player.InterpBuffer
        {
            RenderDelay = 0.0,
        };
        // Première prédiction vers x=100 (t=21) — dépassée avant
        // d'arriver par une nouvelle trame.
        b.PushMovement(1.0, 0f, 0f, 0f, 0f, 21.0, 100f, 0f, 0f);
        // Nouvelle trame : cur à 2 u (écart < 20 u de la prédiction
        // locale 0,5 → continuité, cf. MaxCurCorrectionSq), nouvelle
        // dst à x=4 — l'ancienne prédiction (x=100) ne doit plus
        // tirer l'échantillonnage.
        b.PushMovement(1.1, 2f, 0f, 0f, 0f, 1.5, 4f, 0f, 0f);
        Assert.True(b.SampleAt(1.3, out var x, out _, out _, out _));
        Assert.Equal(2.25f, x, 1e-4f); // moitié de 0,5→4 sur [1.1,1.5]
        Assert.True(b.SampleAt(10.0, out x, out _, out _, out _));
        Assert.Equal(4f, x, 1e-4f); // clamp sur dst, jamais 100
    }

    [Fact]
    public void PushMovement_MidFlight_KeepsContinuity()
    {
        // Le serveur C# pose dst dans sa position logique dès
        // l'émission (MobWander) : le cur du 326 suivant devance le
        // rendu — un cur pris tel quel snapait le mob à l'ancienne
        // dst (« téléport malgré l'anim de marche »).
        var b = new EdenClient.Player.InterpBuffer
        {
            RenderDelay = 0.0,
        };
        // t=0 : marche 0 → 10 (arrivée t=5).
        b.PushMovement(0.0, 0f, 0f, 0f, 0f, 5.0, 10f, 0f, 0f);
        // t=2 : cur serveur = 10 (pos logique) alors que la
        // prédiction locale n'est qu'à x≈4 → la re-prédiction repart
        // de x≈4, pas de 10.
        b.PushMovement(2.0, 10f, 0f, 0f, 0f, 5.5, 15f, 0f, 0f);
        Assert.True(b.SampleAt(2.1, out var x, out _, out _, out _));
        Assert.InRange(x, 3.5f, 5.5f); // pas de snap à x=10
        Assert.True(b.SampleAt(5.5, out x, out _, out _, out _));
        Assert.Equal(15f, x, 1e-4f); // arrive bien à la nouvelle dst
    }

    [Fact]
    public void PushMovement_FarCur_HonorsTeleport()
    {
        var b = new EdenClient.Player.InterpBuffer
        {
            RenderDelay = 0.0,
        };
        b.PushMovement(0.0, 0f, 0f, 0f, 0f, 5.0, 10f, 0f, 0f);
        // t=2 : cur à 200 u — vraie téléportation (au-delà de 20 u),
        // le cur serveur est honoré.
        b.PushMovement(2.0, 200f, 0f, 0f, 0f, 3.0, 210f, 0f, 0f);
        Assert.True(b.SampleAt(2.05, out var x, out _, out _, out _));
        Assert.InRange(x, 195f, 210f);
    }

    [Fact]
    public void PushMovement_FutureDst_DoesNotPurgeHistory()
    {
        var b = new EdenClient.Player.InterpBuffer
        {
            RenderDelay = 0.0,
        };
        b.Push(0.5, -5f, 0f, 0f, 0f);
        // dstTime=21 : la purge interne de Push ne doit pas jeter
        // l'historique (cutoff calculé sur le temps du dst futur).
        b.PushMovement(1.0, 0f, 0f, 0f, 0f, 21.0, 100f, 0f, 0f);
        Assert.True(b.Count >= 3);
        Assert.True(b.SampleAt(0.8, out var x, out _, out _, out _));
        Assert.True(x < 2f, $"x={x} — l'échantillon doit rester près de cur");
    }
}
