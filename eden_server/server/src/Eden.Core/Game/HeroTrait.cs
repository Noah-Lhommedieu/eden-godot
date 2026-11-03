namespace Eden.Core.Game;

/// <summary>
/// Trait héroïque (8 passifs de création). Sémantique native prouvée
/// (désasm WorldServer <c>CCharacterDBO::CreateCharacterIntoDB</c>
/// @0x080a2170) : le 3e octet du wire NCID 5 = « Feature » ∈ [0,7]
/// (≥ 8 → « Creating Character: invaild.Feature », création refusée) ;
/// le natif insère <c>player_spells.spellmaster_id = 0xc351 + feature</c>
/// (50001-50008, passifs S_Spell « J00n » → enchant 52001-52008) et
/// n'écrit PAS de colonne create_flag. <c>CCharacter::GetHeroFeature</c>
/// (zone @0x840ace0) renvoie id enchant − 52000 (1..8, 0 = aucun).
/// Reborn : colonne <c>create_flag</c> = feature + 1 (0 = persos
/// antérieurs au sélecteur) ; l'enchant est appliqué par l'étape
/// stats « herotrait ».
/// </summary>
public static class HeroTrait
{
    public const int Count = 8;
    public const int EnchantBase = 52000;

    /// <summary>Feature wire (0..7) → valeur stockée/portée (1..8) ;
    /// null si invalide (refus natif).</summary>
    public static int? FromWireFeature(byte feature)
        => feature < Count ? feature + 1 : null;
}
