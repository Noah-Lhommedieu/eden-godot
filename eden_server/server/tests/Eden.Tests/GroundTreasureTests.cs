using Eden.Zone;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>Plan 03 : cycle de vie des trésors au sol (CEventTreasure
/// natif : propriétaire → public → despawn) et format du 329/330.</summary>
public class GroundTreasureTests
{
    [Fact]
    public void Sweep_OwnerWindow_ThenPublic_ThenDespawn()
    {
        var svc = new GroundTreasureService();
        var t0 = DateTime.UtcNow;
        var t = svc.Spawn(1, 0, 42, 10f, 10f, 5001, 3, t0);
        Assert.False(t.IsPublic);

        var (p0, d0) = svc.Sweep(t0.AddSeconds(10));
        Assert.Empty(p0); Assert.Empty(d0);

        var (p1, d1) = svc.Sweep(t0.AddSeconds(
            GroundTreasureService.OwnerSeconds + 1));
        Assert.Single(p1); Assert.Empty(d1);
        Assert.True(t.IsPublic);

        var (p2, d2) = svc.Sweep(t0.AddSeconds(
            GroundTreasureService.OwnerSeconds +
            GroundTreasureService.PublicSeconds + 1));
        Assert.Empty(p2); Assert.Single(d2);
        Assert.False(svc.TryGet(t.Id, out _));
    }

    [Fact]
    public void TryRemove_OnlyOnePickerWins()
    {
        var svc = new GroundTreasureService();
        var t = svc.Spawn(1, 0, 42, 0f, 0f, 5001, 1, DateTime.UtcNow);
        Assert.True(svc.TryRemove(t.Id, out _));
        Assert.False(svc.TryRemove(t.Id, out _));
    }

    [Fact]
    public void SeeTreasure_Codec_34Bytes_ItemCountOwnerPublic()
    {
        var svc = new GroundTreasureService();
        var t = svc.Spawn(1, 0, 777, 5f, 6f, 4321, 9, DateTime.UtcNow);
        var b = TreasureCodec.BuildSeeTreasure(t);
        Assert.Equal(34, b.Length);
        Assert.Equal(t.Id, BitConverter.ToUInt32(b, 0));
        Assert.Equal(4321, BitConverter.ToUInt16(b, 20));
        Assert.Equal(9, BitConverter.ToInt16(b, 22));
        Assert.Equal(777, BitConverter.ToInt32(b, 29));
        Assert.Equal(0, b[33]);
        t.IsPublic = true;
        Assert.Equal(1, TreasureCodec.BuildSeeTreasure(t)[33]);
    }

    [Fact]
    public void Spawn_IdsInNativeRange_AndScatteredAroundDropper()
    {
        var svc = new GroundTreasureService();
        var t = svc.Spawn(1, 0, 1, 100f, 100f, 1, 1, DateTime.UtcNow);
        Assert.True(t.Id >= GroundTreasureService.FirstTreasureId);
        var d = Math.Sqrt((t.X - 100) * (t.X - 100) + (t.Z - 100) * (t.Z - 100));
        Assert.InRange(d, 1.4, 3.1);
    }
}
