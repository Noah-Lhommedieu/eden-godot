using Eden.Zone;

namespace Eden.Tests;

/// <summary>Pas de monture à la connexion à froid ; conservée au
/// portail (RideStates encore peuplé).</summary>
public class RideLoginTests
{
    [Fact] public void ColdLogin_RideEnchant_NotRestored() =>
        Assert.False(ZoneEntryFlow.ShouldRestoreEnchant(true, false));

    [Fact] public void Portal_RideEnchant_Restored() =>
        Assert.True(ZoneEntryFlow.ShouldRestoreEnchant(true, true));

    [Theory, InlineData(false), InlineData(true)]
    public void NonRideEnchant_AlwaysRestored(bool mounted) =>
        Assert.True(ZoneEntryFlow.ShouldRestoreEnchant(false, mounted));
}
