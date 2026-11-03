using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régression byte-level du 289 <c>CNC_CZ_ClientSpellBlastToLife</c> —
/// visuel de blast de sort diffusé aux observateurs
/// (<c>CSpellToLife::NotifyBlastToOthers</c> @0x084f7e40 :
/// CanSee(lanceur) + CanSee(cible)).
/// Format désasm (<c>Serialize</c> @0x081e8870) :
/// [u32 serial][s16 spell][i32 handle lanceur (this+0x18)]
/// [f32 srcX][f32 srcY][f32 srcZ (pos lanceur)]
/// [i32 handle cible (this+0x1c)][f32 dstX][f32 dstY][f32 dstZ
/// (pos cible)][i32 blastIdx][i32 0] — 46 octets.
/// </summary>
public class SpellBlastToLifeTests
{
    private static byte[] Emit(NetCommand cmd)
    {
        var outp = new OutBinStream();
        cmd.Serialize(outp);
        return outp.ToArray();
    }

    /// <summary>Wire exact : ordre et tailles des 12 champs.</summary>
    [Fact]
    public void BlastToLife_ExactWire()
    {
        var b = Emit(new CNC_CZ_ClientSpellBlastToLife
        {
            Field1 = 0x11223344,            // serial
            Field2 = unchecked((short)50022), // spell 0xc366
            Field3 = 0x00123456,            // handle lanceur
            Field4 = 10.5f, Field5 = 20.5f, Field6 = 0f,
            Field7 = unchecked((int)0xffffab0a), // handle cible (mob)
            Field8 = 30.0f, Field9 = 40.0f, Field10 = 1.5f,
            Field11 = 3092, Field12 = 0,
        });
        Assert.Equal(46, b.Length);
        var off = 0;
        Assert.Equal(0x11223344u, BitConverter.ToUInt32(b, off)); off += 4;
        Assert.Equal(unchecked((short)50022),
            BitConverter.ToInt16(b, off)); off += 2;
        Assert.Equal(0x00123456, BitConverter.ToInt32(b, off)); off += 4;
        Assert.Equal(10.5f, BitConverter.ToSingle(b, off)); off += 4;
        Assert.Equal(20.5f, BitConverter.ToSingle(b, off)); off += 4;
        Assert.Equal(0f, BitConverter.ToSingle(b, off)); off += 4;
        Assert.Equal(unchecked((int)0xffffab0a),
            BitConverter.ToInt32(b, off)); off += 4;
        Assert.Equal(30f, BitConverter.ToSingle(b, off)); off += 4;
        Assert.Equal(40f, BitConverter.ToSingle(b, off)); off += 4;
        Assert.Equal(1.5f, BitConverter.ToSingle(b, off)); off += 4;
        Assert.Equal(3092, BitConverter.ToInt32(b, off)); off += 4;
        Assert.Equal(0, BitConverter.ToInt32(b, off));
    }
}
