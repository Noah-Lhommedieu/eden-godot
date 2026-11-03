using Xunit;
using Eden.Core.Networking;

namespace Eden.Tests;

/// <summary>
/// Tests du RC4 compatible OpenSSL (lapis @0x86a16a0) et du framing
/// [varint len][RC4(u16 NCID + payload)] du transport Eden.
/// </summary>
public class Rc4Tests
{
    [Fact]
    public void Rc4_KnownVector_RfcTestSuite()
    {
        // Vecteur classique : RC4("Key") appliqué à "Plaintext" donne
        // le ciphertext BBF316E8D940AF0AD3 — déchiffrer doit rendre "Plaintext".
        var cipher = new Rc4("Key"u8.ToArray());
        var data = new byte[] { 0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 };
        cipher.Process(data);
        Assert.Equal("Plaintext", System.Text.Encoding.ASCII.GetString(data));
    }

    [Fact]
    public void Rc4_RoundTrip_PreservesData()
    {
        var key = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }; // clé 5 octets comme Eden
        var enc = new Rc4(key);
        var dec = new Rc4(key);
        var original = new byte[] { 0x01, 0x02, 0x00, 0x00, 0xFF, 0x10 };
        var data = original.ToArray();
        enc.Process(data);
        Assert.NotEqual(original, data);
        dec.Process(data);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Rc4_ContinuousState_MatchesSinglePass()
    {
        // Le RC4 doit être un stream continu : chiffrer en 2 appels ==
        // chiffrer en 1 appel du même buffer.
        var key = new byte[] { 1, 2, 3, 4, 5 };
        var a = new Rc4(key);
        var b = new Rc4(key);
        var data = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

        var single = b.ProcessCopy((ReadOnlySpan<byte>)data);
        var p1 = a.ProcessCopy((ReadOnlySpan<byte>)data.AsSpan(0, 20));
        var p2 = a.ProcessCopy((ReadOnlySpan<byte>)data.AsSpan(20));
        Assert.Equal(single.AsSpan(0, 20).ToArray(), p1);
        Assert.Equal(single.AsSpan(20).ToArray(), p2);
    }

    [Fact]
    public void Frame_LenField_IsPayloadSize()
    {
        // Simule le framing [varint len][payload] : len = NCID + champs
        var stream = new OutBinStream();
        stream.WriteShort(5);        // NCID
        stream.WriteInt(0x12345678); // payload
        var payload = stream.ToArray();
        var frame = new byte[VarintCodec.MaxEncodedLength + payload.Length];
        var hdrLen = VarintCodec.WriteVarint(frame, (uint)payload.Length);
        payload.CopyTo(frame, hdrLen);

        Assert.True(VarintCodec.TryReadVarint(frame, uint.MaxValue,
            out var len, out var read));
        Assert.Equal(hdrLen, read);
        Assert.Equal((uint)payload.Length, len);
        Assert.Equal(6u, len); // 2 (NCID) + 4 (int)
    }
}
