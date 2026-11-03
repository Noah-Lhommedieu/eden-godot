using EdenClient.Net;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du codec varint LEB128 (<see cref="VarintCodec"/>) — miroir
/// des VarintCodecTests serveur (mêmes vecteurs : le codec est le
/// même fichier des deux côtés) — et du <see cref="ReceiveBuffer"/>
/// client : bornes, overlong, préfixe fragmenté, cap MaxFrameSize,
/// trames coalescées.
/// </summary>
public class VarintCodecTests
{
    /// <summary>Vecteurs LEB128 de référence (bornes de chaque taille
    /// de groupe de 7 bits + extremes u32).</summary>
    public static IEnumerable<object[]> KnownVectors()
    {
        yield return new object[] { 0u, new byte[] { 0x00 } };
        yield return new object[] { 1u, new byte[] { 0x01 } };
        yield return new object[] { 127u, new byte[] { 0x7F } };
        yield return new object[] { 128u, new byte[] { 0x80, 0x01 } };
        yield return new object[] { 16383u, new byte[] { 0xFF, 0x7F } };
        yield return new object[] { 16384u, new byte[] { 0x80, 0x80, 0x01 } };
        yield return new object[] { (1u << 28) - 1,
            new byte[] { 0xFF, 0xFF, 0xFF, 0x7F } };
        yield return new object[] { 1u << 28,
            new byte[] { 0x80, 0x80, 0x80, 0x80, 0x01 } };
        yield return new object[] { uint.MaxValue,
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F } };
    }

    [Theory]
    [MemberData(nameof(KnownVectors))]
    public void WriteVarint_ProducesExpectedBytes(uint value, byte[] expected)
    {
        var dst = new byte[VarintCodec.MaxEncodedLength];
        var written = VarintCodec.WriteVarint(dst, value);
        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, dst.AsSpan(0, written).ToArray());
        Assert.Equal(expected.Length, VarintCodec.EncodedSize(value));
    }

    [Theory]
    [MemberData(nameof(KnownVectors))]
    public void TryReadVarint_DecodesExpected(uint expected, byte[] bytes)
    {
        Assert.True(VarintCodec.TryReadVarint(bytes, uint.MaxValue,
            out var value, out var read));
        Assert.Equal(expected, value);
        Assert.Equal(bytes.Length, read);
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var dst = new byte[VarintCodec.MaxEncodedLength];
        foreach (var v in new uint[] { 0, 1, 0x7F, 0x80, 0x3FFF, 0x4000,
                     0xFFFFF, 0x100000, 0xFFFFFFF, 0x10000000,
                     1024 * 1024, uint.MaxValue })
        {
            var n = VarintCodec.WriteVarint(dst, v);
            Assert.True(VarintCodec.TryReadVarint(
                dst.AsSpan(0, n), uint.MaxValue, out var back, out var read));
            Assert.Equal(v, back);
            Assert.Equal(n, read);
        }
    }

    /// <summary>Préfixe coupé → false (attente), pas exception.</summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x80, 0x80 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    public void TryReadVarint_Incomplete_ReturnsFalse(byte[] prefix)
    {
        Assert.False(VarintCodec.TryReadVarint(prefix, uint.MaxValue,
            out _, out _));
    }

    [Fact]
    public void TryReadVarint_Overlong_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            VarintCodec.TryReadVarint(
                new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 },
                uint.MaxValue, out _, out _));
    }

    [Fact]
    public void TryReadVarint_BeyondU32_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            VarintCodec.TryReadVarint(
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F },
                uint.MaxValue, out _, out _));
    }

    [Fact]
    public void TryReadVarint_AboveCap_Throws()
    {
        var bytes = new byte[] { 0x96, 0x01 }; // 150
        Assert.Throws<InvalidDataException>(() =>
            VarintCodec.TryReadVarint(bytes, 100, out _, out _));
    }
}

/// <summary>Tests du <see cref="ReceiveBuffer"/> client — même
/// logique que le PacketReceiveBuffer serveur (miroir des cas
/// PacketFramingTests/VarintCodecTests côté serveur).</summary>
public class ReceiveBufferTests
{
    /// <summary>Préfixe varint fragmenté sur deux appends : attente
    /// puis extraction complète.</summary>
    [Fact]
    public void TryTakePacket_FragmentedVarint_WaitsThenExtracts()
    {
        var buf = new ReceiveBuffer();
        buf.Append(new byte[] { 0xC8 }); // varint 200 — 1er octet seul
        Assert.False(buf.TryTakePacket(out _));
        var rest = new byte[1 + 200];
        rest[0] = 0x01;
        for (var i = 0; i < 200; i++) rest[1 + i] = (byte)(i & 0xFF);
        buf.Append(rest);
        Assert.True(buf.TryTakePacket(out var payload));
        Assert.Equal(200, payload.Length);
        Assert.Equal(rest[1..], payload);
    }

    /// <summary>len&lt;2 → trame sans NCID = malformée.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TryTakePacket_LenBelow2_Throws(int len)
    {
        var buf = new ReceiveBuffer();
        buf.Append(new byte[] { (byte)len, 0xAA, 0xBB });
        Assert.Throws<InvalidDataException>(
            () => buf.TryTakePacket(out _));
    }

    /// <summary>Varint annonçant plus que MaxFrameSize → malformé.</summary>
    [Fact]
    public void TryTakePacket_AboveMaxFrameSize_Throws()
    {
        var buf = new ReceiveBuffer { MaxFrameSize = 100 };
        buf.Append(new byte[] { 0x96, 0x01 }); // 150 > 100
        Assert.Throws<InvalidDataException>(
            () => buf.TryTakePacket(out _));
    }

    /// <summary>Overlong >5 octets → malformé.</summary>
    [Fact]
    public void TryTakePacket_OverlongVarint_Throws()
    {
        var buf = new ReceiveBuffer();
        buf.Append(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 });
        Assert.Throws<InvalidDataException>(
            () => buf.TryTakePacket(out _));
    }

    /// <summary>Trames coalescées extraites dans l'ordre du fil.</summary>
    [Fact]
    public void TryTakePacket_CoalescedFrames_ExtractedInOrder()
    {
        var buf = new ReceiveBuffer();
        var frame = new List<byte>();
        frame.AddRange(new byte[] { 0x02, 0x2D, 0x01 });      // len=2
        frame.AddRange(new byte[] { 0x82, 0x01 });            // len=130
        frame.AddRange(Enumerable.Repeat((byte)0x5A, 130));
        buf.Append(frame.ToArray());

        Assert.True(buf.TryTakePacket(out var a));
        Assert.Equal(new byte[] { 0x2D, 0x01 }, a);
        Assert.True(buf.TryTakePacket(out var b));
        Assert.Equal(130, b.Length);
        Assert.False(buf.TryTakePacket(out _));
    }
}
