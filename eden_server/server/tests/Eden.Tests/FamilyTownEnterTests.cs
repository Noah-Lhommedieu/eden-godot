using Eden.Core.Data.Parsers;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests du helper de position d'arrivée du transport vers un node
/// instancié — <see cref="ZoneEntryFlow.ResolveInstanceEntryPos"/> :
/// reproduction de <c>CZoneServer::TransportCharacterToNode</c>
/// (@zone:0x8176530) qui choisit une NormalArea aléatoire du template
/// puis <c>CNodeTemplate::SetValidCoordInArea</c> (point dans le
/// rayon), avec replis Positions[0] puis première aire GATEWAY.
/// </summary>
public class FamilyTownEnterTests
{
    /// <summary>La scène de hall S501 du client : une seule aire
    /// NORMALE (id 1 à 115.267, 29.4186, r=4 — la place d'entrée du
    /// hall), plusieurs GATEWAY.</summary>
    private static SceneParser.Scene HallScene()
    {
        var s = new SceneParser.Scene { MapCode = "S501" };
        s.EventAreas.Add(new SceneParser.EventArea
        {
            Id = 1, X = 115.267f, Y = 29.4186f, Z = 0.0064f,
            Radius = 4f, Type = "NORMAL",
        });
        s.EventAreas.Add(new SceneParser.EventArea
        {
            Id = 2, X = 119f, Y = 21.4f, Z = 0.55f, Radius = 3f,
            Type = "GATEWAY",
        });
        return s;
    }

    /// <summary>L'aire NORMALE du hall est choisie — le point reste
    /// dans son rayon (SetValidCoordInArea natif).</summary>
    [Fact]
    public void EntryPos_NormalArea_InsideRadius()
    {
        var scene = HallScene();
        for (var i = 0; i < 64; i++)
        {
            var (x, y, z) = ZoneEntryFlow.ResolveInstanceEntryPos(
                scene, new Random(42 + i));
            var dx = x - 115.267f; var dy = y - 29.4186f;
            Assert.True(dx * dx + dy * dy <= 4f * 4f + 1e-4f,
                $"point ({x},{y}) hors aire NORMALE r=4");
            Assert.Equal(0.0064f, z, 4);
        }
    }

    /// <summary>Sans aire NORMALE : repli sur la première position
    /// de la scène (Positions[0]).</summary>
    [Fact]
    public void EntryPos_Fallback_FirstPosition()
    {
        var s = new SceneParser.Scene { MapCode = "S999" };
        s.Positions.Add((44.3f, 117.8f, 15f));
        s.Positions.Add((1f, 2f, 3f));
        var (x, y, z) = ZoneEntryFlow.ResolveInstanceEntryPos(
            s, new Random(1));
        Assert.Equal((44.3f, 117.8f, 15f), (x, y, z));
    }

    /// <summary>Ni NORMALE ni positions : repli sur la première aire
    /// GATEWAY.</summary>
    [Fact]
    public void EntryPos_Fallback_FirstGateway()
    {
        var s = new SceneParser.Scene { MapCode = "S999" };
        s.EventAreas.Add(new SceneParser.EventArea
        {
            Id = 7, X = 50f, Y = 60f, Z = 1.5f, Radius = 6f,
            Type = "GATEWAY",
        });
        var (x, y, z) = ZoneEntryFlow.ResolveInstanceEntryPos(
            s, new Random(1));
        Assert.Equal((50f, 60f, 1.5f), (x, y, z));
    }

    /// <summary>Scène absente (node inconnu) → point sûr
    /// (100,100,0).</summary>
    [Fact]
    public void EntryPos_NullScene_SafePoint()
    {
        var (x, y, z) = ZoneEntryFlow.ResolveInstanceEntryPos(
            null, new Random(1));
        Assert.Equal((100f, 100f, 0f), (x, y, z));
    }

    /// <summary>S501 réelle du client — la résolution par le vrai
    /// parser retourne un point de l'aire NORMALE du hall.</summary>
    [Fact]
    public void EntryPos_RealS501_InNormalArea()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (dir != null && path == null)
        {
            var candidate = Path.Combine(
                dir.FullName, "server_us/Data/scene/S501.ini");
            if (File.Exists(candidate)) path = candidate;
            dir = dir.Parent;
        }
        Assert.NotNull(path);
        var scene = SceneParser.Parse(path);
        var (x, y, z) = ZoneEntryFlow.ResolveInstanceEntryPos(
            scene, new Random(7));
        var dx = x - 115.267f; var dy = y - 29.4186f;
        Assert.True(dx * dx + dy * dy <= 4f * 4f + 1e-4f,
            $"point ({x},{y}) hors aire NORMALE S501");
    }
}
