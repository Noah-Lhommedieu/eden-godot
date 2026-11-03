namespace Eden.Core.Game;

/// <summary>
/// Bitfields du blob d'apparence 32 octets envoyé par le client à la
/// création de personnage (wire NCID 5 `CNE_CW_ServerCreateCharacterData`).
///
/// Source de preuve : désassemblage de `CCharacterDBO::CreateCharacterIntoDB`
/// — les 8 derniers octets portent tous les attributs cosmétiques :
///   b24 : hair[0:2] &lt;&lt;6
///   b25 : hair[2:6] | face[0:4]&lt;&lt;4
///   b26 : face[4:6] | hair_color&lt;&lt;2
///   b27 : skin_color | eyes_color[0:2]&lt;&lt;6
///   b28 : eyes_color[2:6] | gender&lt;&lt;4
///   b30 : eyes_color2[0:5]&lt;&lt;3
///   b31 : eyes_color2[5]
/// Les octets 0-23 sont libres (extensions client, 0 par défaut).
/// </summary>
public static class AppearanceBlob
{
    /// <summary>Attributs cosmétiques décodés du blob.</summary>
    public readonly record struct Appearance(
        int Gender, int HairStyle, int FaceStyle, int HairColor,
        int SkinColor, int EyesColor, int EyesColor2);

    /// <summary>Décode un blob d'apparence (formules natives exactes).</summary>
    public static Appearance Decode(byte[] app) => new(
        Gender: app[28] >> 4,
        HairStyle: (app[24] >> 6) | ((app[25] & 0xF) << 2),
        FaceStyle: (app[25] >> 4) | ((app[26] & 0x3) << 4),
        HairColor: app[26] >> 2,
        SkinColor: app[27] & 0x3F,
        EyesColor: (app[27] >> 6) | ((app[28] & 0xF) << 2),
        EyesColor2: (app[30] >> 3) | ((app[31] & 0x1) << 5));

    /// <summary>
    /// Ré-encodage inverse de <see cref="Decode"/> — utilisé par
    /// Eden.Admin pour construire un blob équivalent à celui que le
    /// client UI aurait produit.
    /// </summary>
    public static byte[] Encode(Appearance a)
    {
        var b = new byte[32];
        b[24] = (byte)((a.HairStyle & 0x3) << 6);
        b[25] = (byte)(((a.HairStyle >> 2) & 0xF) | ((a.FaceStyle & 0xF) << 4));
        b[26] = (byte)(((a.FaceStyle >> 4) & 0x3) | ((a.HairColor & 0x3F) << 2));
        b[27] = (byte)((a.SkinColor & 0x3F) | ((a.EyesColor & 0x3) << 6));
        b[28] = (byte)(((a.EyesColor >> 2) & 0xF) | ((a.Gender & 0xF) << 4));
        b[30] = (byte)((a.EyesColor2 & 0x1F) << 3);
        b[31] = (byte)((a.EyesColor2 >> 5) & 0x1);
        return b;
    }
}
