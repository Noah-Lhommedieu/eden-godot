namespace EdenClient.Protocol;

/// <summary>
/// Codec du blob d'apparence 32 octets (CharacterListChunk +0x24,
/// envoyé tel quel à la création via world NCID 5).
///
/// Bitfields natifs — miroir exact de
/// <c>$EDEN/server/src/Eden.Core/Game/AppearanceBlob.cs</c>
/// (désassemblage <c>CCharacterDBO::CreateCharacterIntoDB</c>) :
///   b24 : hair[0:2]&lt;&lt;6
///   b25 : hair[2:6] | face[0:4]&lt;&lt;4
///   b26 : face[4:6] | hair_color&lt;&lt;2
///   b27 : skin_color | eyes_color[0:2]&lt;&lt;6
///   b28 : eyes_color[2:6] | body_code&lt;&lt;4
///   b30 : eyes_color2[0:5]&lt;&lt;3
///   b31 : eyes_color2[5]
///
/// Le champ « gender » natif (b28&gt;&gt;4) n'est PAS binaire : c'est
/// le <b>code de corps</b> qui sélectionne le KFM/GLB de base —
/// switch de <c>CCharKfm</c>/<c>FUN_0073ddc0</c> rf_game :
/// {0:g, 1:b, 2:m, 4:f, 6:k, 8:l, 9:c, défaut:g}
/// (<see cref="EdenClient.CharModelSpec.BodyLetter"/>).
/// </summary>
public static class AppearanceCodec
{
    /// <summary>Attributs cosmétiques décodés du blob.</summary>
    public readonly record struct Appearance(
        int BodyCode, int HairStyle, int FaceStyle, int HairColor,
        int SkinColor, int EyesColor, int EyesColor2);

    /// <summary>Décode un blob 32 octets (formules natives exactes).</summary>
    public static Appearance Decode(byte[] app) => new(
        BodyCode: app[28] >> 4,
        HairStyle: (app[24] >> 6) | ((app[25] & 0xF) << 2),
        FaceStyle: (app[25] >> 4) | ((app[26] & 0x3) << 4),
        HairColor: app[26] >> 2,
        SkinColor: app[27] & 0x3F,
        EyesColor: (app[27] >> 6) | ((app[28] & 0xF) << 2),
        EyesColor2: (app[30] >> 3) | ((app[31] & 0x1) << 5));

    /// <summary>Ré-encodage (création de personnage — même formule
    /// que <c>AppearanceBlob.Encode</c> côté serveur).</summary>
    public static byte[] Encode(Appearance a)
    {
        var b = new byte[32];
        b[24] = (byte)((a.HairStyle & 0x3) << 6);
        b[25] = (byte)(((a.HairStyle >> 2) & 0xF) | ((a.FaceStyle & 0xF) << 4));
        b[26] = (byte)(((a.FaceStyle >> 4) & 0x3) | ((a.HairColor & 0x3F) << 2));
        b[27] = (byte)((a.SkinColor & 0x3F) | ((a.EyesColor & 0x3) << 6));
        b[28] = (byte)(((a.EyesColor >> 2) & 0xF) | ((a.BodyCode & 0xF) << 4));
        b[30] = (byte)((a.EyesColor2 & 0x1F) << 3);
        b[31] = (byte)((a.EyesColor2 >> 5) & 0x1);
        return b;
    }
}
