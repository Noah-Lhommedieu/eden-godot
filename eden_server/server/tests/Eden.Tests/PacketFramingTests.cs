using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests du framing réseau : rejet des trames <c>len&lt;2</c>
/// (anti-flood) et anneau de trace <see cref="PacketTrace"/>.
/// </summary>
public class PacketFramingTests
{
    /// <summary>Une trame dont la longueur (varint) est inférieure à 2
    /// ne peut pas contenir de NCID → <see cref="InvalidDataException"/>
    /// (le pair doit être déconnecté, pas ignoré silencieusement).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TryTakePacket_LenBelow2_Throws(int len)
    {
        var buf = new PacketReceiveBuffer();
        buf.Append(new byte[] { (byte)len, 0xAA, 0xBB }); // len 0/1 = 1 octet LEB128
        Assert.Throws<InvalidDataException>(
            () => buf.TryTakePacket(out _));
    }

    /// <summary>Trame valide minimale (2 octets = NCID seul) : extraite
    /// normalement — le rejet ne touche que len&lt;2.</summary>
    [Fact]
    public void TryTakePacket_MinimalValidFrame_Extracted()
    {
        var buf = new PacketReceiveBuffer();
        buf.Append(new byte[] { 2, 0x2D, 0x01 }); // varint len=2, NCID 0x012D
        Assert.True(buf.TryTakePacket(out var payload));
        Assert.Equal(new byte[] { 0x2D, 0x01 }, payload);
    }

    /// <summary>Trame incomplète → false, sans exception.</summary>
    [Fact]
    public void TryTakePacket_Incomplete_ReturnsFalse()
    {
        var buf = new PacketReceiveBuffer();
        buf.Append(new byte[] { 10, 0x2D }); // varint len=10, 1 octet de payload
        Assert.False(buf.TryTakePacket(out _));
    }

    /// <summary>L'anneau conserve les Capacity dernières entrées dans
    /// l'ordre chronologique une fois plein.</summary>
    [Fact]
    public void PacketTrace_RollsOverAndKeepsOrder()
    {
        var trace = new PacketTrace();
        for (var i = 0; i < PacketTrace.Capacity + 5; i++)
            trace.Record(inbound: i % 2 == 0, ncid: i, len: 10);

        var dump = trace.Dump();
        var lines = dump.Split('\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(PacketTrace.Capacity, lines.Length);
        // La première ligne = NCID 5 (les 0-4 ont été écrasés).
        Assert.Contains("NCID=   5", lines[0]);
        Assert.Contains($"NCID={PacketTrace.Capacity + 4,4}",
            lines[^1]);
    }
}
