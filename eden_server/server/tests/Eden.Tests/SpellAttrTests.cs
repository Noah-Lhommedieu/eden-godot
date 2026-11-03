using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>548 <c>ClientUpdateSpellAttr</c> + formule
/// <c>CCharFormula::CoolDownTime</c> — preuve :
/// <c>CEC_AttrSpellCoolDown::Execute</c> @0x8510f90 (corps
/// [s16 kind][s16 spellId][i32 valeur]) et désasm de la formule
/// <c>round(base × (mod×0.01 + 1.0))</c>, min 5 ds.</summary>
public sealed class SpellAttrTests
{
    [Fact]
    public void UpdateSpellAttr_ExactWire()
    {
        var m = new CNC_CZ_ClientUpdateSpellAttr
        {
            Field1 = 2,          // kind CoolDown
            Field2 = unchecked((short)50176), // spell_id (s16 signé)
            Field3 = 75,         // cooldown recomputé
        };
        var s = new OutBinStream();
        m.Serialize(s);
        // [s16 2][s16 50176=0xC3D0][i32 75]
        Assert.Equal(new byte[]
        {
            0x02, 0x00, 0x00, 0xC4, 0x4B, 0x00, 0x00, 0x00,
        }, s.ToArray());
    }

    [Theory]
    [InlineData(-25, 100, 75)]   // -25 % → 75 ds
    [InlineData(0, 100, 100)]
    [InlineData(10, 100, 110)]
    [InlineData(-50, 8, 5)]      // garde native : min 5 ds
    [InlineData(50, 0, 0)]       // base nulle → inchangé
    public void CoolDownTime_NativeFormula(int mod, int baseDs,
        int expected)
        => Assert.Equal(expected,
            ZoneEntryFlow.SpellAttrCooldownDs(mod, baseDs));
}
