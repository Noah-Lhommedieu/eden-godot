// Copyright (c) 2026 Noah Lhommedieu. MIT License.

using EdenClient.Data;

namespace EdenClient.Game;

/// <summary>
/// Teinte des textures-masque du personnage — port des formules de
/// <c>CharVisual.Tinted</c>/<c>TintMeshes</c> du client de référence :
/// les peaux <c>S&lt;L&gt;*</c> (<c>&lt;L&gt;X01AS01A</c>) et cheveux
/// <c>#&lt;L&gt;1xx</c> (<c>&lt;L&gt;1H*S01</c>) sont des cartes
/// d'intensité dans le canal R (rouge pur en DXT1 — vérifié
/// GX01AS01A/G1H00S01) que le natif recolore via
/// <see cref="CharColorTable"/> (<c>C_CharColor.ini</c>,
/// désasm FUN_0073e970/8d0) ; la teinte est appliquée au buffer
/// RGBA8 avant upload.
/// </summary>
public static class CharTint
{
    /// <summary>Classe de teinte d'un mesh du méga-modèle.</summary>
    public enum Kind { None = 0, Skin, Hair }

    /// <summary>Plancher de teinte cheveux — réf. Godot : le fond noir
    /// de l'atlas (cap interne, dessous des mèches) garde un ombrage
    /// teinté au lieu de sortir noir pur (0.7 mesuré).</summary>
    public const float HairFloor = 0.7f;

    /// <summary>Classification par nom de shape — patterns exacts du
    /// tint de référence : « S&lt;lettre&gt;* » → peau,
    /// « #&lt;l&gt;1&lt;chiffre&gt;* » → cheveux. La lettre n'est pas
    /// vérifiée (comme l'original — un shape « S* » = peau).</summary>
    public static Kind ForShape(string name)
    {
        if (name.Length > 2 && name[0] == 'S' && char.IsLetter(name[1]))
            return Kind.Skin;
        if (name.Length >= 4 && name[0] == '#' && name[2] == '1' &&
            char.IsDigit(name[3]))
            return Kind.Hair;
        return Kind.None;
    }

    /// <summary>Vrai pour un mesh tenue « #&lt;l&gt;2&lt;chiffre&gt;* »
    /// (masque dye par canaux — pattern dye de la référence).</summary>
    public static bool IsOutfit(string name) =>
        name.Length >= 4 && name[0] == '#' && name[2] == '2' &&
        char.IsDigit(name[3]);

    /// <summary>Code corps natif pour une lettre de modèle — inverse
    /// de <see cref="CharModelSpec.BodyLetter"/> (inconnu → 0 « g »,
    /// même repli que le natif).</summary>
    public static int BodyCode(char letter) =>
        char.ToLowerInvariant(letter) switch
        {
            'b' => 1, 'm' => 2, 'f' => 4, 'k' => 6, 'l' => 8,
            'c' => 9, _ => 0,
        };

    /// <summary>Teinte de la classe via <see cref="CharColorTable"/> :
    /// base de section par code corps + sélecteur du blob d'apparence
    /// (peau : 201/401/501/601 ; cheveux : 101/701).</summary>
    public static CharColorTable.Rgb Color(Kind kind, int bodyCode,
        int selector) => kind == Kind.Hair
        ? CharColorTable.Hair(bodyCode, selector)
        : CharColorTable.Skin(bodyCode, selector);

    /// <summary>Candidats de texture visage par ordre de préférence —
    /// port de <c>CharVisual.FaceTexture</c> : fichier
    /// « &lt;L&gt;FACES&lt;style+1:D2&gt;&lt;variante&gt; » où variante
    /// = 'A'+peau clampée 0..5 ; pour peau 0 la version sans lettre
    /// (artwork complet, pas un calque) est préférée.</summary>
    public static List<string> FaceTextureCandidates(char letter,
        int faceStyle, int skinColor)
    {
        var stem = char.ToUpperInvariant(letter) +
                   $"FACES{faceStyle + 1:D2}";
        var variant = (char)('A' + Math.Clamp(skinColor, 0, 5));
        var list = new List<string>(4);
        void Add(string n)
        {
            if (!list.Contains(n, StringComparer.OrdinalIgnoreCase))
                list.Add(n);
        }
        if (skinColor == 0)
            Add(stem + ".dds");
        Add(stem + variant + ".dds");
        Add(stem + ".dds");
        if (variant != 'A')
            Add(stem + "A.dds");
        return list;
    }

    /// <summary>Variante artwork « *…A » d'un masque dye « *…B »
    /// (tenue <c>G202AS01B</c> → <c>G202AS01A</c>) — null si le nom ne
    /// finit pas par « b » avant l'extension.</summary>
    public static string? ArtworkForMask(string texName)
    {
        var dot = texName.LastIndexOf('.');
        var end = dot < 0 ? texName.Length : dot;
        return end > 0 && (texName[end - 1] == 'B' || texName[end - 1] == 'b')
            ? texName[..(end - 1)] + 'A' + texName[end..]
            : null;
    }

    /// <summary>Teinte un buffer RGBA8 en place — formule de
    /// référence : <c>k = floor + (1−floor)·r/rMax</c>,
    /// <c>out = teinte·k</c> (le max du canal R normalise le masque ;
    /// l'alpha est préservé — masque de découpe cheveux).</summary>
    public static void Apply(byte[] rgba, float r, float g, float b,
        float floor = 0f)
    {
        var max = 1;
        for (var i = 0; i + 3 < rgba.Length; i += 4)
            if (rgba[i] > max) max = rgba[i];
        var inv = 1f / max;
        var span = 1f - floor;
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            var k = floor + span * (rgba[i] * inv);
            rgba[i] = (byte)Math.Clamp((int)(255f * r * k), 0, 255);
            rgba[i + 1] = (byte)Math.Clamp((int)(255f * g * k), 0, 255);
            rgba[i + 2] = (byte)Math.Clamp((int)(255f * b * k), 0, 255);
        }
    }
}
