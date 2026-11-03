using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Layout du corps 265 <c>ClientSeeChar</c> — offsets vérifiés contre
/// le Serialize natif zone @0x0819ec20 (voir l'en-tête de
/// <see cref="ClientSeeCharCodec"/>). Pas de capture golden (les
/// dumps existants sont solo) : le test fige la structure octet par
/// octet pour détecter toute régression d'ordre/largeur.
/// </summary>
public class ClientSeeCharCodecTests
{
    private static ClientSeeCharCodec.SeeCharSnapshot Sample(
        byte[]? flags = null) => new(
        CharId: 0x11223344, X: 1234.5f, Z: -678.25f, Facing: 1.5f,
        MoveSpeed: 9.425f, AttackSpeedLevel: 7,
        Flags: flags ?? ClientSeeCharCodec.LifeFlags(
            dead: false, mounted: false, invincible: false),
        Level: 60, ClassLevel: 45, Hp: 123456, MaxHp: 200000,
        MaxMp: 8000, ModelRev: 9, CastingSpell: 0,
        StallTitle: "");

    [Fact]
    public void Build_HeaderFields_AtNativeOffsets()
    {
        var b = ClientSeeCharCodec.Build(Sample());
        // 85 octets : 83 fixes + u16 len (chaîne vide).
        Assert.Equal(85, b.Length);
        Assert.Equal(0x11223344u, BitConverter.ToUInt32(b, 0));  // charId
        Assert.Equal(1234.5f, BitConverter.ToSingle(b, 4));      // x
        Assert.Equal(-678.25f, BitConverter.ToSingle(b, 8));     // z
        Assert.Equal(1.5f, BitConverter.ToSingle(b, 12));        // dir
        Assert.Equal(9.425f, BitConverter.ToSingle(b, 16));      // move
        Assert.Equal(7, b[20]);                                 // atkSpd
        Assert.Equal(7, b[21]);
    }

    [Fact]
    public void Build_LifeFlags_IsCharBit_AlwaysSet()
    {
        var b = ClientSeeCharCodec.Build(Sample());
        // +0x3b bit2 = "vrai personnage" — le gate natif du 265.
        Assert.Equal(0x04, b[23] & 0x04);
    }

    [Fact]
    public void LifeFlags_Bitset()
    {
        var f = ClientSeeCharCodec.LifeFlags(dead: true,
            mounted: true, invincible: true);
        Assert.Equal(0x01, f[0]);       // dead (state 0)
        Assert.Equal(0x06, f[1]);       // mounted (state 9 = word
                                        // 0x0200) | is-char gate
        Assert.Equal(0x80, f[2]);       // invincible
        Assert.Equal(0, f[3]);
    }

    [Fact]
    public void LifeFlags_Mounted_WritesWordBit9_NotByte0Bit6()
    {
        // Le bit ride natif = state 9 → flag index 9 → byte1 bit1 =
        // word 0x0200 (S_StateDependence ligne -1, CEC_Ride::SetState
        // teste (byte)char+0x3b & 2). L'ancien byte0 0x40 écrivait le
        // state 6 « using an item » — bug : les observateurs ne
        // voyaient jamais le perso monté.
        var f = ClientSeeCharCodec.LifeFlags(dead: false,
            mounted: true, invincible: false);
        Assert.Equal(0x00, f[0]);
        Assert.Equal(0x02, f[1] & 0x02);
        Assert.Equal(0x0200,
            (f[1] << 8 | f[0]) & 0x0200); // word bit9 posé
    }

    [Fact]
    public void Build_Vitals_AreI64()
    {
        var b = ClientSeeCharCodec.Build(Sample());
        Assert.Equal(60, BitConverter.ToUInt16(b, 26));       // level
        Assert.Equal(45, BitConverter.ToUInt16(b, 28));       // classLv
        Assert.Equal(123456L, BitConverter.ToInt64(b, 30));   // HP i64
        Assert.Equal(200000L, BitConverter.ToInt64(b, 38));   // MaxHP i64
        Assert.Equal(200000, BitConverter.ToInt32(b, 46));    // MaxHP i32
        Assert.Equal(8000, BitConverter.ToInt32(b, 50));      // MaxMP
        Assert.Equal(9, b[54]);                               // modelRev
    }

    [Fact]
    public void Build_Trailer_TerritoryTeamFloatState()
    {
        var b = ClientSeeCharCodec.Build(Sample());
        Assert.Equal(0, BitConverter.ToInt16(b, 59));         // casting
        Assert.Equal(0, BitConverter.ToUInt16(b, 61));        // len=0
        Assert.Equal(unchecked((short)0xffff),
            BitConverter.ToInt16(b, 63));                     // terrTeam
        Assert.Equal(0f, BitConverter.ToSingle(b, 65));       // float h
        Assert.Equal(0f, BitConverter.ToSingle(b, 69));       // remain
        Assert.Equal(0f, BitConverter.ToSingle(b, 73));       // dur
        Assert.Equal(0, BitConverter.ToInt16(b, 77));         // state
        Assert.Equal(0, BitConverter.ToInt16(b, 79));         // +0xd8c
        Assert.Equal(0, BitConverter.ToInt32(b, 81));         // +0xdb0
    }

    [Fact]
    public void Build_StallTitle_GrowsBody()
    {
        var s = Sample() with { StallTitle = "shop" };
        var b = ClientSeeCharCodec.Build(s);
        Assert.Equal(85 + 4, b.Length);                       // +4 bytes
        Assert.Equal(4, BitConverter.ToUInt16(b, 61));        // len=4
        Assert.Equal((byte)'s', b[63]);
        // Le trailer glisse de 4 octets.
        Assert.Equal(unchecked((short)0xffff),
            BitConverter.ToInt16(b, 67));
    }
}
