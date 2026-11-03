using Eden.Core.Game;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Roundtrip <see cref="AppearanceBlob"/> — garantit que le ré-encodage
/// utilisé par Eden.Admin produit un blob que le décodage natif
/// (CCharacterDBO::CreateCharacterIntoDB) lit à l'identique.
/// </summary>
public class AppearanceBlobTests
{
    [Fact]
    public void EncodeDecode_Roundtrip_AllFields()
    {
        var a = new AppearanceBlob.Appearance(
            Gender: 1, HairStyle: 37, FaceStyle: 45, HairColor: 7,
            SkinColor: 2, EyesColor: 33, EyesColor2: 44);
        var blob = AppearanceBlob.Encode(a);
        var d = AppearanceBlob.Decode(blob);
        Assert.Equal(a.Gender, d.Gender);
        Assert.Equal(a.HairStyle, d.HairStyle);
        Assert.Equal(a.FaceStyle, d.FaceStyle);
        Assert.Equal(a.HairColor, d.HairColor);
        Assert.Equal(a.SkinColor, d.SkinColor);
        Assert.Equal(a.EyesColor, d.EyesColor);
        Assert.Equal(a.EyesColor2, d.EyesColor2);
    }

    [Fact]
    public void EncodeDecode_Roundtrip_Extremes()
    {
        // Valeurs limites de chaque champ (max de sa largeur de bits).
        var a = new AppearanceBlob.Appearance(
            Gender: 15, HairStyle: 63, FaceStyle: 63, HairColor: 63,
            SkinColor: 63, EyesColor: 63, EyesColor2: 63);
        var d = AppearanceBlob.Decode(AppearanceBlob.Encode(a));
        Assert.Equal(15, d.Gender);
        Assert.Equal(63, d.HairStyle);
        Assert.Equal(63, d.FaceStyle);
        Assert.Equal(63, d.HairColor);
        Assert.Equal(63, d.SkinColor);
        Assert.Equal(63, d.EyesColor);
        Assert.Equal(63, d.EyesColor2);
    }

    [Fact]
    public void Decode_KnownBytes_MatchesNativeFormulas()
    {
        // Blob construit octet par octet à la main — indépendant de
        // Encode — vérifie les formules natives elles-mêmes.
        var b = new byte[32];
        b[24] = 0x40;                       // hair[0:2]=1
        b[25] = 0x52;                       // hair[2:6]=2, face[0:4]=5
        b[26] = (byte)(0x02 | (0x0A << 2)); // face[4:6]=2, hair_color=10
        b[27] = (byte)(0x06 | (0x03 << 6)); // skin=6, eyes[0:2]=3
        b[28] = (byte)(0x04 | (0x01 << 4)); // eyes[2:6]=4, gender=1
        b[30] = (byte)(0x09 << 3);          // eyes2[0:5]=9
        b[31] = 0x01;                       // eyes2[5]=1
        var d = AppearanceBlob.Decode(b);
        Assert.Equal(1, d.Gender);           // 1
        Assert.Equal(9, d.HairStyle);        // 1 | 2<<2
        Assert.Equal(37, d.FaceStyle);       // 5 | 2<<4
        Assert.Equal(10, d.HairColor);
        Assert.Equal(6, d.SkinColor);
        Assert.Equal(19, d.EyesColor);       // 3 | 4<<2
        Assert.Equal(41, d.EyesColor2);      // 9 | 1<<5
    }

    [Fact]
    public void Encode_ZeroesUpperBytes()
    {
        var blob = AppearanceBlob.Encode(new AppearanceBlob.Appearance(0, 0, 0, 0, 0, 0, 0));
        Assert.Equal(32, blob.Length);
        for (var i = 0; i < 24; i++) Assert.Equal(0, blob[i]);
    }
}
