namespace Eden.Core.Game.Inventory;

/// <summary>
/// Résolution des EquipType S_Item vers les locs du conteneur
/// d'équipement natif — désasm `CItemFactory::Equip` @0x80b5810.
/// `CCharacter::GetWeaponItem` @0x840cea0 lit les locs canoniques
/// 7 (main), 8 (off-hand), 10 (distance/arme longue).
/// </summary>
public static class NativeEquipmentSlots
{
    /// <summary>Class code 0x14 (20) exempted by native
    /// `CItemFactory::DoWeaponUnEquip` from clearing locs 7/8/10.</summary>
    public const int WeaponSlotExceptionClassId = 0x14;

    /// <summary>Retourne les locs que l'EquipType natif peut occuper.
    /// Empty signifie que l'item n'est pas équipable.</summary>
    public static int[] AllowedLocations(int equipType) =>
        equipType switch
        {
            >= 1 and <= 6 => [equipType],
            7 or 19 => [7, 8],  // 1H/dague, dual-wield
            8 => [8],             // off-hand/shield
            9 => [7],              // 2H -> main
            10 => [11],            // accessoire -> loc 11
            11 or 20 => [12, 13],  // paire accessoire
            12 => [14, 15],        // paire accessoire
            >= 13 and <= 18 => [equipType],
            35 => [10],             // arc/arme longue
            _ => Array.Empty<int>(),
        };

    /// <summary>Kinds équipables sur le PERSONNAGE (conteneur 0) —
    /// désasm `CItemFactory::Equip` @0x80b5810 : kinds 0x3c (60 œuf)
    /// et 0x51 (81 pet mall) rejetés globalement ; kinds 0x3d-0x44
    /// (61-68 pet gear) refusés conteneur 0 (erreur 0xa0, conteneur
    /// 1 = panneau pet) ; kind 0x4e (78 glyphe) → conteneur glyphe
    /// uniquement. Les kinds 1-34 (armure/arme) et 37-40 (collier,
    /// anneau, boucle, licence) sont les seules classes
    /// CEquipItemData dont le champ col9 est un TYPE de slot joueur —
    /// pour les autres (gemme 45, enchant 56, usage 44, pet…) col9
    /// désigne LEUR propre slot (socket gemme, panneau pet, cible
    /// d'enchant) et ne doit jamais devenir un loc joueur.</summary>
    public static bool IsPlayerEquippableKind(int kind) =>
        kind is >= 1 and <= 34 or >= 37 and <= 40;

    /// <summary>Kinds du panneau costume (conteneur 1 natif du switch
    /// Equip @0x80b5b68) — kinds 0x3d-0x44 (61-68). Le 0x51 (81) est
    /// listé pour mémoire : c'est l'équipement de FAMILIER routé par
    /// le drag vers le conteneur elf dstType 6 (ElfEquip @0x808cb20),
    /// jamais par le panneau 1 — son EquipLoc (7/19/10/34) le
    /// disqualifie de toute façon des locs panneau {1,2,4,5,6,7,8}.</summary>
    public static bool IsPetEquipKind(int kind) =>
        kind is >= 61 and <= 68 or 81;

    /// <summary>Kind équipement de familier (0x51 = 81) — seul kind
    /// admis par <c>CItemFactory::ElfEquip</c> natif (conteneur
    /// dstType 6, 4 slots : EquipLoc {7,19}→0-1, {10,34}→2-3).</summary>
    public static bool IsElfEquipKind(int kind) => kind == 81;

    /// <summary>Kind glyphe/classe (0x4e = 78) — le natif le route
    /// vers le conteneur glyphe (case 5) via GetClassGlyphLoc,
    /// jamais vers un loc d'équipement ordinaire.</summary>
    public static bool IsGlyphKind(int kind) => kind == 78;
}
