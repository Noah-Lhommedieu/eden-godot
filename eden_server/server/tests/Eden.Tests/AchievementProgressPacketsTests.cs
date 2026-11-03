using Xunit;
using Eden.Zone.Codecs;

namespace Eden.Tests;

/// <summary>Extension Eden du 480 : kind 6 = compteurs des succès
/// incomplets <c>[u8 6][u16 n][i32 id][u32 n]×n</c>.</summary>
public class AchievementProgressPacketsTests
{
    [Fact]
    public void Kind6_Layout()
    {
        var body = AchievementProgressPackets.Build(
            [(90101, 7u), (90102, 13u)]);
        Assert.Equal(1 + 2 + 2 * 8, body.Length);
        Assert.Equal(6, body[0]);
        Assert.Equal(2, BitConverter.ToUInt16(body, 1));
        Assert.Equal(90101, BitConverter.ToInt32(body, 3));
        Assert.Equal(7u, BitConverter.ToUInt32(body, 7));
        Assert.Equal(90102, BitConverter.ToInt32(body, 11));
        Assert.Equal(13u, BitConverter.ToUInt32(body, 15));
    }

    [Fact]
    public void Kind6_Empty()
    {
        var body = AchievementProgressPackets.Build([]);
        Assert.Equal(new byte[] { 6, 0, 0 }, body);
    }
}
