using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>La vitesse du joueur vient du serveur (296 Speeds[0]) :
/// le client convertit sans ajouter de bonus (monture incluse).</summary>
public class ServerMoveSpeedTests
{
    [Fact]
    public void NoProfile_IsBaseSpeed()
    {
        Assert.Equal(1f, ServerMoveSpeed.Multiplier(null));
        Assert.Equal(1f, ServerMoveSpeed.Multiplier(new float[0]));
        Assert.Equal(1f, ServerMoveSpeed.Multiplier(new[] { 0f }));
        Assert.Equal(1f, ServerMoveSpeed.Multiplier(new[] { float.NaN }));
    }

    [Fact]
    public void ServerValue_IsAppliedAsIs()
    {
        var foot = ServerMoveSpeed.NativeBase;          // 100 %
        var ride = ServerMoveSpeed.NativeBase * 1.75f;  // monture +75 %
        Assert.Equal(1f, ServerMoveSpeed.Multiplier(new[] { foot }), 3);
        Assert.Equal(1.75f, ServerMoveSpeed.Multiplier(new[] { ride }), 3);
        // Aucun plafond de gameplay côté client (x3 dev + monture = x5.25).
        Assert.Equal(5.25f, ServerMoveSpeed.Multiplier(
            new[] { ServerMoveSpeed.NativeBase * 5.25f }), 3);
    }

    [Fact]
    public void MountedGivesMoreThanFootForSameBase()
    {
        var foot = ServerMoveSpeed.Multiplier(new[] { 28.275f });
        var ride = ServerMoveSpeed.Multiplier(new[] { 28.275f * 1.75f });
        Assert.True(ride > foot);
    }
}
