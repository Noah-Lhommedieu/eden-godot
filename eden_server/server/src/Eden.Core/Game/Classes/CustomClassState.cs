namespace Eden.Core.Game.Classes;

/// <summary>
/// État d'une carte de classe personnalisée (« angel style » /
/// custom class) — record natif `CCustomClass` de 0x14 octets
/// (char+0xd9c zone / exec+0x890 mission) :
/// <code>[+4 u8 equip_type][+5 u8 weapon_type][+8 vec&lt;i32&gt; sorts]</code>
/// Persisté dans `custom_class_data` (player_id, index, equip_type,
/// weapon_type, spell_1..8) — LoadCustomClassData/SaveCustomClassData
/// natifs (`CExecutionerDBO` mission).
/// </summary>
public sealed class CustomClassSlot
{
    /// <summary>Type d'équipement sauvegardé (EClassEquipType natif,
    /// 1..3) — pilote le stash de `SwitchClassEquip`.</summary>
    public byte EquipType;
    /// <summary>Type d'arme sauvegardé (&lt; 2) — bit0 → `char+0x4ad`
    /// bit2 renvoyé par ModelModify mask 0x8000.</summary>
    public byte WeaponType;
    /// <summary>Sorts mémorisés de la carte (≤ 8, i32 chacun —
    /// `custom_class_data.spell_1..8`).</summary>
    public readonly List<int> Spells = new();

    /// <summary>Carte vierge du seed natif : `equip_type=1,
    /// weapon_type=0`, 8 sorts à 0 (INSERT par défaut de
    /// LoadCustomClassData @mission ~0x08153dxx).</summary>
    public static CustomClassSlot Default() => new() { EquipType = 1 };
}

/// <summary>Constantes du sous-système custom-class —
/// <c>CNE_CZ_ZoneServerPlayerCCDataSave::Execute</c> zone
/// @0x08272bc0.</summary>
public static class CustomClass
{
    /// <summary>Nombre de cartes — les records 0x14 du vecteur
    /// char+0xd9c (et le seed DB jusqu'à 3 lignes).</summary>
    public const int SlotCount = 3;
    /// <summary>Cap sorts par carte — gate natif `(end-begin)&gt;&gt;2 &lt; 9`.</summary>
    public const int MaxSpells = 8;
    /// <summary>`CEffectFactory::AngelChangeStyleID` = u16 signée
    /// -18931 (donnée statique @0x087b4b0c) = 46605 — enchant posé par
    /// EffectToLife après la sauvegarde (rest 0x32 ticks).</summary>
    public const int AngelStyleEnchantId = 46605;
    /// <summary>rest_time natif du EffectToLife (param_4 = 0x32 =
    /// 50 dixièmes = 5 s — convertir via EnchantRestSeconds).</summary>
    public const int AngelStyleRestDs = 0x32;

    /// <summary>Texte 0x193d — sauvegarde refusée en combat
    /// (`char+0x3c &amp; 4`).</summary>
    public const short ErrorInCombat = 0x193d;
    /// <summary>Texte 0x193e — enchant 46605 déjà actif
    /// (`FindEnchant(AngelChangeStyleID)`).</summary>
    public const short ErrorAlreadyActive = 0x193e;
    /// <summary>Texte 0x193f — riding (`char+0x3b &amp; 0x20`, objet
    /// contrôlé char+0x94).</summary>
    public const short ErrorRiding = 0x193f;

    /// <summary>Clé du conteneur `equipment.container_index` qui stocke
    /// la tenue d'un type d'équipement — `SwitchClassEquip` @0x080f1f90 :
    /// type 1 → conteneur 4, type 2 → 3, type 3 → 2
    /// (clé = 5 − type).</summary>
    public static int StashContainerKey(int equipType) => 5 - equipType;
}
