using Eden.Core.Networking;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests du codec varint LEB128 (<see cref="VarintCodec"/>) — préfixe
/// de trame phase-4b — et de son intégration dans
/// <see cref="PacketReceiveBuffer"/> : bornes d'encodage, overlong,
/// fragmentation du préfixe sur plusieurs lectures, cap MaxFrameSize,
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

    /// <summary>Écriture : chaque valeur produit le LEB128 attendu et
    /// <see cref="VarintCodec.EncodedSize"/> donne sa taille.</summary>
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

    /// <summary>Lecture : chaque vecteur décode sa valeur et consomme
    /// exactement ses octets.</summary>
    [Theory]
    [MemberData(nameof(KnownVectors))]
    public void TryReadVarint_DecodesExpected(uint expected, byte[] bytes)
    {
        Assert.True(VarintCodec.TryReadVarint(bytes, uint.MaxValue,
            out var value, out var read));
        Assert.Equal(expected, value);
        Assert.Equal(bytes.Length, read);
    }

    /// <summary>Round-trip sur un balayage de valeurs (bornes de
    /// changement de taille comprises).</summary>
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

    /// <summary>Un préfixe coupé (bit7 sur le dernier octet dispo)
    /// rend false — pas une erreur : il faut plus d'input.</summary>
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

    /// <summary>5 octets tous avec bit7 = overlong → malformé.</summary>
    [Fact]
    public void TryReadVarint_Overlong_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            VarintCodec.TryReadVarint(
                new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 },
                uint.MaxValue, out _, out _));
    }

    /// <summary>Bits 4-6 du 5e octet = valeur > u32.MaxValue →
    /// malformé.</summary>
    [Fact]
    public void TryReadVarint_BeyondU32_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            VarintCodec.TryReadVarint(
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F },
                uint.MaxValue, out _, out _));
    }

    /// <summary>Valeur décodée > cap passé en paramètre → malformé
    /// (la borne appartient à la couche framing).</summary>
    [Fact]
    public void TryReadVarint_AboveCap_Throws()
    {
        var bytes = new byte[] { 0x96, 0x01 }; // 150
        Assert.Throws<InvalidDataException>(() =>
            VarintCodec.TryReadVarint(bytes, 100, out _, out _));
        // ≤ cap : accepté.
        Assert.True(VarintCodec.TryReadVarint(bytes, 150,
            out var value, out _));
        Assert.Equal(150u, value);
    }

    /// <summary>Préfixe varint fragmenté sur deux appends : le premier
    /// rend false (attente) puis la trame complète est extraite.</summary>
    [Fact]
    public void TryTakePacket_FragmentedVarint_WaitsThenExtracts()
    {
        var buf = new PacketReceiveBuffer();
        buf.Append(new byte[] { 0xC8 }); // varint 200 — 1er octet seul
        Assert.False(buf.TryTakePacket(out _)); // préfixe incomplet
        var rest = new byte[1 + 200];
        rest[0] = 0x01;                        // suite du varint
        for (var i = 0; i < 200; i++) rest[1 + i] = (byte)(i & 0xFF);
        buf.Append(rest);
        Assert.True(buf.TryTakePacket(out var payload));
        Assert.Equal(200, payload.Length);
        Assert.Equal(rest[1..], payload);
    }

    /// <summary>Préfixe complet mais payload partiel : false, puis
    /// extraction quand le reste arrive.</summary>
    [Fact]
    public void TryTakePacket_PartialPayload_WaitsThenExtracts()
    {
        var buf = new PacketReceiveBuffer();
        buf.Append(new byte[] { 0xC8, 0x01 });        // varint 200 complet
        buf.Append(new byte[150]);                    // 150/200 octets
        Assert.False(buf.TryTakePacket(out _));
        buf.Append(new byte[50]);
        Assert.True(buf.TryTakePacket(out var payload));
        Assert.Equal(200, payload.Length);
    }

    /// <summary>Le cap MaxFrameSize du buffer rejette un préfixe
    /// annonçant une trame géante — malformé, pas attente.</summary>
    [Fact]
    public void TryTakePacket_AboveMaxFrameSize_Throws()
    {
        var buf = new PacketReceiveBuffer { MaxFrameSize = 100 };
        buf.Append(new byte[] { 0x96, 0x01 });       // 150 > 100
        Assert.Throws<InvalidDataException>(
            () => buf.TryTakePacket(out _));
    }

    /// <summary>Varint overlong en tête de buffer → malformé.</summary>
    [Fact]
    public void TryTakePacket_OverlongVarint_Throws()
    {
        var buf = new PacketReceiveBuffer();
        buf.Append(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 });
        Assert.Throws<InvalidDataException>(
            () => buf.TryTakePacket(out _));
    }

    /// <summary>Plusieurs trames coalescées dans un même append sont
    /// extraites une par une, dans l'ordre du fil.</summary>
    [Fact]
    public void TryTakePacket_CoalescedFrames_ExtractedInOrder()
    {
        var buf = new PacketReceiveBuffer();
        var frame = new List<byte>();
        // Trame A : len=2 [0x2D,0x01] ; trame B : len=130 (varint 2B)
        frame.AddRange(new byte[] { 0x02, 0x2D, 0x01 });
        frame.AddRange(new byte[] { 0x82, 0x01 });
        frame.AddRange(Enumerable.Repeat((byte)0x5A, 130));
        buf.Append(frame.ToArray());

        Assert.True(buf.TryTakePacket(out var a));
        Assert.Equal(new byte[] { 0x2D, 0x01 }, a);
        Assert.True(buf.TryTakePacket(out var b));
        Assert.Equal(130, b.Length);
        Assert.All(b, x => Assert.Equal(0x5A, x));
        Assert.False(buf.TryTakePacket(out _));
    }
}
