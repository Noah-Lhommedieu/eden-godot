namespace EdenClient.Data;

/// <summary>
/// Règles pures (sans Godot, testables) des coffres au sol (plan 03) :
/// famille de modèle <c>G000NN</c> d'après c_item col3 <c>TypeCode</c>,
/// échelle normalisée, libellé et gabarit du message de ramassage.
///
/// Preuves : <c>ItemMakeField</c> zone @0x080b5800 enregistre
/// <c>G%05d</c> (10 familles G00001-G00010) ; le champ modèle est
/// local au client (jamais dans le 329). Items à TypeCode vide
/// (≈1 400 dans c_item) : le natif n'a pas de modèle propre
/// documenté — repli <see cref="FallbackFamily"/> + teinte rareté
/// (hypothèse du plan 03, non tranchée par le reverse).
/// </summary>
public static class TreasureRules
{
    /// <summary>Famille de repli (items sans TypeCode valide).</summary>
    public const string FallbackFamily = "G00001";

    /// <summary>Hauteur cible d'un coffre au sol (unités monde) — un
    /// mob moyen fait ~1.9 u ; l'ancien 0.8 u était « trop petit ».</summary>
    public const float TargetHeight = 1.1f;

    /// <summary>Famille modèle valide (G00001-G00010) pour un
    /// TypeCode c_item ; repli <see cref="FallbackFamily"/>.</summary>
    public static string Family(string? typeCode)
    {
        if (typeCode is { Length: 6 } t &&
            (t[0] == 'G' || t[0] == 'g') &&
            int.TryParse(t.AsSpan(1), out var n) && n is >= 1 and <= 10)
            return $"G{n:00000}";
        return FallbackFamily;
    }

    /// <summary>Facteur d'échelle ramenant un modèle de hauteur
    /// <paramref name="height"/> à <see cref="TargetHeight"/> ; 1 si
    /// la hauteur mesurée est inexploitable ou déjà à ±10 %.</summary>
    public static float ScaleFor(float height)
    {
        if (!(height > 0.01f)) return 1f;
        var k = TargetHeight / height;
        return k is > 0.9f and < 1.1f ? 1f : k;
    }

    /// <summary>Libellé 3D : nom + « ×n » si pile &gt; 1.</summary>
    public static string Label(string name, int count) =>
        count > 1 ? $"{name} ×{count}" : name;

    /// <summary>Gabarit EN du message de ramassage (clé TSV).</summary>
    public static string PickupTemplate(int count) =>
        count > 1 ? "Obtained {0} ×{1}." : "Obtained {0}.";
}
