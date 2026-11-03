using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

public class Rc4Tests
{
    [Fact]
    public void Rc4_KnownVector_KeyPlaintext()
    {
        // Vecteur classique : RC4("Key")("Plaintext") = BBF316E8D940AF0AD3
        var rc4 = new Rc4("Key"u8);
        var ct = rc4.ProcessCopy("Plaintext"u8);
        Assert.Equal("BBF316E8D940AF0AD3", Convert.ToHexString(ct));
    }

    [Fact]
    public void Rc4_StreamIsContinuous()
    {
        // Deux appels successifs == un seul appel concaténé.
        var a = new Rc4("secret"u8);
        var b = new Rc4("secret"u8);
        var data = "hello world, this is a continuous stream"u8.ToArray();
        var joined = a.ProcessCopy(data);
        var part1 = b.ProcessCopy(data[..10]);
        var part2 = b.ProcessCopy(data[10..]);
        Assert.Equal(joined, part1.Concat(part2).ToArray());
    }
}

public class BinStreamTests
{
    [Fact]
    public void RoundTrip_AllTypes()
    {
        var w = new BinWriter();
        w.WriteByte(0xAB);
        w.WriteShort(-1234);
        w.WriteInt(-0x1234567);
        w.WriteLong(0x0102030405060708);
        w.WriteFloat(6.666f);
        w.WriteDouble(9.9);
        w.WriteString("admin");
        var bytes = w.ToArray();

        var r = new BinReader(bytes);
        Assert.Equal(0xAB, r.ReadByte());
        Assert.Equal(-1234, r.ReadShort());
        Assert.Equal(-0x1234567, r.ReadInt());
        Assert.Equal(0x0102030405060708, r.ReadLong());
        Assert.Equal(6.666f, r.ReadFloat());
        Assert.Equal(9.9, r.ReadDouble());
        Assert.Equal("admin", r.ReadString());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void String_IsU16LengthPrefixed()
    {
        var w = new BinWriter();
        w.WriteString("admin");
        Assert.Equal(new byte[] { 5, 0, 0x61, 0x64, 0x6D, 0x69, 0x6E }, w.ToArray());
    }
}

public class HandshakeTests
{
    [Fact]
    public void RsaBlob_Is256Bytes_Pkcs1Type2()
    {
        // Clé RSA-2048 de test — reproduit le format du handshake client :
        // blob = RSA PKCS#1 v1.5 type 2 de la clé RC4 (5 octets).
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(true);
        var key = new byte[] { 1, 2, 3, 4, 5 };

        using var pub = RSA.Create();
        pub.ImportParameters(new RSAParameters { Modulus = p.Modulus, Exponent = p.Exponent });
        var blob = pub.Encrypt(key, RSAEncryptionPadding.Pkcs1);

        Assert.Equal(p.Modulus!.Length, blob.Length); // 256 octets

        // Déchiffrement brut (modPow d) → bloc EM : 00 02 PS(non-zero) 00 key
        var m = new BigInteger(blob.AsEnumerable().Reverse().Append((byte)0).ToArray());
        var n = new BigInteger(p.Modulus!.AsEnumerable().Reverse().Append((byte)0).ToArray());
        var d = new BigInteger(p.D!.AsEnumerable().Reverse().Append((byte)0).ToArray());
        var em = BigInteger.ModPow(m, d, n).ToByteArray().Reverse().ToArray();
        // em peut être plus court que k (leading zeros tronqués) → pad à gauche
        var k = p.Modulus.Length;
        if (em.Length < k) em = new byte[k - em.Length].Concat(em).ToArray();
        Assert.Equal(0x00, em[0]);
        Assert.Equal(0x02, em[1]);
        var sep = Array.IndexOf(em, (byte)0, 2);
        Assert.True(sep >= 10); // PS >= 8 octets
        for (var i = 2; i < sep; i++) Assert.NotEqual(0, em[i]);
        Assert.Equal(key, em[(sep + 1)..]);
    }
}

public class GoldenMessageTests
{
    private static byte[] FromHex(string s) => Convert.FromHexString(s);

    [Fact]
    public void LoginAccount_MatchesCapture()
    {
        // mitm_native_entry.log ligne 2 : NCID=1 admin + hash + version.
        var msg = new LoginAccount
        {
            Account = "admin",
            PasswordHash = "17c725af38777cf3ac4bf34e39f44caa",
            Version = "002.003.05.67",
        };
        var expected = FromHex(
            "0100050061646d696e2000313763373235616633383737376366336163" +
            "34626633346533396634346361610d003030322e3030332e30352e3637");
        Assert.Equal(expected, msg.ToPayload());
    }

    [Fact]
    public void LoginWorldSelect_MatchesCapture()
    {
        // mitm_native_entry.log ligne 4 : NCID=2 worldId=1010 + "admin" + md5("").
        var msg = new LoginWorldSelect { WorldId = 1010, Account = "admin" };
        var expected = FromHex(
            "0200f203050061646d696e2000643431643863643938663030623230" +
            "3465393830303939386563663834323765");
        Assert.Equal(expected, msg.ToPayload());
    }

    [Fact]
    public void WorldLoginTicket_Structure()
    {
        // mitm ligne 8 : NCID=1 [22B ticket][str machineId].
        var ticket = FromHex("020000007f0000017f0000015014ca676e9cc6e0030b");
        var msg = new WorldLoginTicket { Ticket = ticket, MachineId = "BC-EC-A0-43-F3-80" };
        var expected = FromHex(
            "0100020000007f0000017f0000015014ca676e9cc6e0030b" +
            "110042432d45432d41302d34332d46332d3830");
        Assert.Equal(expected, msg.ToPayload());
    }

    [Fact]
    public void CharacterListChunk_ParsesCapture()
    {
        // mitm ligne 13 : chunk header=1, 3 persos test1..test3.
        // Hex verbatim de la capture (530 octets).
        var payload = FromHex(
            "260003010002c2eb0b05007465737431010002000100020003000000" +
            "00000000000000000000000000000000000000000000000000000010" +
            "00000000020000000407009b7c0000ffffffffffffffffffffffff00" +
            "0000000000000000000200c37c0000ffffffffffffffffffffffff00" +
            "0000000000000000000400c47c0000ffffffffffffffffffffffff00" +
            "0000000000000000000500c57c0000ffffffffffffffffffffffff00" +
            "00000000000000000001000003c2eb0b050074657374320100010001" +
            "00020001000000000000000000000000000000000000000000000000" +
            "0000000000001000000000020000000407009b7c0000ffffffffffff" +
            "ffffffffffff000000000000000000000200c37c0000ffffffffffff" +
            "ffffffffffff000000000000000000000400c47c0000ffffffffffff" +
            "ffffffffffff000000000000000000000500c57c0000ffffffffffff" +
            "ffffffffffff0000000000000000000001000004c2eb0b0500746573" +
            "74330100010001000200010000000000000000000000000000000000" +
            "000000000000000000000000001000000000020000000407009b7c00" +
            "00ffffffffffffffffffffffff000000000000000000000200c37c00" +
            "00ffffffffffffffffffffffff000000000000000000000400c47c00" +
            "00ffffffffffffffffffffffff000000000000000000000500c57c00" +
            "00ffffffffffffffffffffffff00000000000000000000010000");
        var msg = new CharacterListChunk();
        var r = new BinReader(payload);
        Assert.Equal(38, r.ReadUShort());
        msg.Deserialize(r);
        Assert.Equal(0, r.Remaining); // consommé jusqu'au bout
        Assert.Equal(1, msg.Header);
        Assert.Equal(3, msg.Characters.Count);
        Assert.Equal("test1", msg.Characters[0].Name);
        Assert.Equal(200000002, msg.Characters[0].Id);
        // Round-trip : re-sérialisation == payload capturé
        Assert.Equal(payload, msg.ToPayload());
    }

    [Fact]
    public void WorldCharPassword_WireLayout()
    {
        // CNE_CW_ServerCharPassword (Eden.World MiscHandlers) :
        // NCID 31 [i32 charId][s16 op][str pw] → réponse 76 [s16].
        var msg = new WorldCharPassword
            { CharId = 200000002, Op = 3, Password = "abc" };
        Assert.Equal(FromHex("1f0002c2eb0b03000300616263"),
            msg.ToPayload());

        var resp = new WorldCharPasswordResult();
        var rr = new BinReader(FromHex("4c000300"));
        Assert.Equal(76, rr.ReadUShort());
        resp.Deserialize(rr);
        Assert.Equal(3, resp.Value);

        // NCID 30 [i32 charId] → réponse 75 [i32 flag].
        var check = new WorldCheckCharPassword { CharId = 7 };
        Assert.Equal(FromHex("1e0007000000"), check.ToPayload());
        var has = new WorldCheckCharPasswordResult();
        var hr = new BinReader(FromHex("4b0001000000"));
        Assert.Equal(75, hr.ReadUShort());
        has.Deserialize(hr);
        Assert.Equal(1, has.Value);
    }
}
