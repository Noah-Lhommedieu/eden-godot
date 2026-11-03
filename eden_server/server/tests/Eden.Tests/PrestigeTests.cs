using Xunit;
using Eden.Core.Game.Family;
using Eden.Core.Database.Repositories;

namespace Eden.Tests;

/// <summary>
/// Prestige — format fil NCID 60 (<c>CNC_CW_ClientUpdatePrestigeData::
/// Serialize</c> monde @0x0812c8b0 : <c>[u16 n][n×(s16 id, s16 level,
/// i32 exp)]</c>) et sémantique <c>GainPrestigeExp</c> mission
/// @0x0808c1b0 (gate d'ids natif + clamp [0, S_Parameter 361=75000]).
/// </summary>
public class PrestigeTests
{
    /// <summary>NCID 60 vide — juste le compteur.</summary>
    [Fact]
    public void EmptyList_IsCountZero()
        => Assert.Equal(new byte[] { 0, 0 },
            PrestigePackets.UpdatePrestigeData([]));

    /// <summary>NCID 60 — une entrée : id/level s16 + exp i32, LE.
    /// Reproduit les bytes du stub observé en capture native
    /// (id=1, level=1, exp=300 → 01 00 01 00 01 00 2c 01 00 00).</summary>
    [Fact]
    public void SingleEntry_MatchesNativeBytes()
        => Assert.Equal(
            new byte[] { 0x01, 0x00, 0x01, 0x00, 0x01, 0x00,
                         0x2c, 0x01, 0x00, 0x00 },
            PrestigePackets.UpdatePrestigeData([(1, 1, 300)]));

    /// <summary>NCID 60 — plusieurs entrées concaténées.</summary>
    [Fact]
    public void MultiEntries_Concatenate()
    {
        var b = PrestigePackets.UpdatePrestigeData(
            [(1, 1, 300), (0x51, 2, 75000)]);
        Assert.Equal(2 + 8 * 2, b.Length);
        Assert.Equal(new byte[] { 2, 0 }, b[..2]);
        // Entrée 2 : id=0x51, level=2, exp=75000 (0x124f8).
        Assert.Equal(new byte[] { 0x51, 0x00, 0x02, 0x00,
                                  0xf8, 0x24, 0x01, 0x00 }, b[10..]);
    }

    /// <summary>Gate natif <c>AddPrestige</c> @0x0807cb90 : ids
    /// 1-38, 51-59, 81-85 uniquement.</summary>
    [Theory]
    [InlineData(1, true)] [InlineData(38, true)]
    [InlineData(39, false)] [InlineData(50, false)]
    [InlineData(51, true)] [InlineData(59, true)]
    [InlineData(60, false)] [InlineData(80, false)]
    [InlineData(81, true)] [InlineData(85, true)]
    [InlineData(86, false)] [InlineData(0, false)]
    [InlineData(100, false)]
    public void IdGate_MatchesNativeRanges(int id, bool expected)
        => Assert.Equal(expected,
            PrestigeRepository.IsValidPrestigeId(id));

    /// <summary>Clamp natif <c>GainPrestigeExp</c> : plancher 0,
    /// plafond 75000 (S_Parameter #361).</summary>
    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(300, -100, 200)]
    [InlineData(300, -1000, 0)]
    [InlineData(74990, 100, 75000)]
    [InlineData(75000, 1, 75000)]
    public void ExpDelta_ClampsToNativeBounds(int cur, int delta,
        int expected)
        => Assert.Equal(expected,
            PrestigeRepository.ApplyExpDelta(cur, delta));

}
