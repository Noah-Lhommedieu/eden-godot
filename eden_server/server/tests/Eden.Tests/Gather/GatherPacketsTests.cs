using Eden.Zone.Features.Gather;

namespace Eden.Tests.Gather;

public class GatherPacketsTests
{
    [Fact]
    public void Layout_IsFixed15Bytes_LittleEndian()
    {
        var b = GatherPackets.Build(GatherPackets.State.Tick, 1, 30, 36819, 36801, 2, 0);
        Assert.Equal(15, b.Length);
        Assert.Equal(2, b[0]);                               // état = tick
        Assert.Equal(1, b[1]);                               // kind = minage
        Assert.Equal(30, BitConverter.ToUInt16(b, 2));       // secondes entre ticks
        Assert.Equal(36819, BitConverter.ToInt32(b, 4));     // outil
        Assert.Equal(36801, BitConverter.ToInt32(b, 8));     // ressource
        Assert.Equal(2, BitConverter.ToUInt16(b, 12));       // quantité
        Assert.Equal(0, b[14]);
    }

    [Fact]
    public void Stop_CarriesReason() =>
        Assert.Equal(7, GatherPackets.Build(GatherPackets.State.Stop, 2, 0, 1, stopReason: 7)[14]);

    [Fact]
    public void Ncid_IsCustomReborn() => Assert.Equal(9063, GatherPackets.Ncid);
}
