namespace Eden.Core.Game.Inventory;

/// <summary>
/// Instance d'item dans l'inventaire
/// Correspond à CCloneItem
/// </summary>
public sealed class ItemEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int BagId { get; set; }
    public int Slot { get; set; }
    public int ItemId { get; set; }
    /// <summary>Nom du fabriquant (colonne `maker` — string clone+0x20
    /// dans les records item stockés).</summary>
    public string Maker { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
    public bool IsEquipped { get; set; }
    /// <summary>Niveau de fortification (+0 à +20, S_Parameter.ini).</summary>
    public int EnchantLevel { get; set; }
    /// <summary>Item lié au personnage (non échangeable).</summary>
    public bool IsBound { get; set; }
    /// <summary>Flag d'identification natif (colonne `identify`,
    /// clone+0x24 bit 3) : 1 = identifié (défaut colonne), 0 = à
    /// identifier — stats cachées tant que 0.</summary>
    public int Identify { get; set; } = 1;
    /// <summary>Horodatage de création natif (inventory.create_time) —
    /// émis via le bit 8 du mask du codec item (u32 item+0x18).</summary>
    public int CreateTime { get; set; }
    /// <summary>Expiration unix (due_date natif) — 0 = permanent.
    /// Les items à durée (montures 3 jours) sans due_date apparaissent
    /// "0 sec remain" côté client et sont inutilisables.</summary>
    public int DueDate { get; set; }
    /// <summary>Qualité/couleur d'instance (clone+0x2c natif — colonne
    /// `quality` de inventory1) : 0 = qualité de base de l'item
    /// (S_Item col19), sinon la couleur roulée (cube Horadric
    /// PickAColor, magic box). Persistée ; lue par CheckCubeCosts pour
    /// la contrainte qualité aux (S_ItemChest col5).</summary>
    public int Quality { get; set; }
    /// <summary>Champ magique d'instance (clone+0x6c natif — colonne
    /// `magic`) : ≠0 bascule l'item dans le bucket qualité 0x10 de
    /// CheckMagicBoxCosts/DoMagicBox (@0x8088200/@0x80a8b90).</summary>
    public int Magic { get; set; }
    /// <summary>Combos boîte magique (clone+0x70..+0x74 natif —
    /// colonnes magicbox_enchant1-3) : ids S_ItemCombo rollés aux
    /// stars 1/3/5 ou rerollés en scouring.</summary>
    public int MagicBoxEnchant1 { get; set; }
    public int MagicBoxEnchant2 { get; set; }
    public int MagicBoxEnchant3 { get; set; }
    /// <summary>Énergie accumulée de conversion (clone+0x7c —
    /// magicbox_energy, u16).</summary>
    public int MagicBoxEnergy { get; set; }
    /// <summary>Niveau d'étoile 0-5 (clone+0x7e —
    /// magicbox_starlevel, u16).</summary>
    public int MagicBoxStarLevel { get; set; }
    /// <summary>Nombre de sockets OUVERTS (clone+0x24 bits 0-2 natif —
    /// colonne `embedded_amount`, passée masquée &7 à
    /// CItemDBO::ModifyItemEmbedded). Roulé à la création par
    /// ChangeEmbeddedAmount @0x809baf0 ; 0 = aucun socket utilisable
    /// même si l'item est socket-capable.</summary>
    public int EmbeddedAmount { get; set; }
    /// <summary>Id du template de la gemme incrustée en socket i
    /// (clone+0x40 vecteur de 6×s16 — colonnes `embedded_id1..6`).
    /// 0 = socket ouverte et VIDE (choisie par le scan natif
    /// CheckExecute case 0x2d), −1 = socket jamais ouverte (le scan
    /// s'arrête au premier −1 → erreur 0xc3). Défaut −1 comme les
    /// items existants en base.</summary>
    public int EmbeddedId1 { get; set; } = -1;
    public int EmbeddedId2 { get; set; } = -1;
    public int EmbeddedId3 { get; set; } = -1;
    public int EmbeddedId4 { get; set; } = -1;
    public int EmbeddedId5 { get; set; } = -1;
    public int EmbeddedId6 { get; set; } = -1;
    /// <summary>combo_id natif (clone+0x10 — colonne `combo_id`) :
    /// id S_ItemCombo principal rollé par SelectWorkItemCombo
    /// (produits player_works) ou l'enchantement. Émis sur le wire
    /// mask bit7 (+0x24 record).</summary>
    public int Combo { get; set; }
    /// <summary>extra_combo_id1..9 natifs (clone+0x34 vecteur u16 —
    /// colonnes `extra_combo_id1..9` ; le clone en porte 11 mais la
    /// DB inventory1 n'en stocke que 9). Wire mask bits 10-18.</summary>
    public int[] ExtraCombos { get; } = new int[9];
    /// <summary>extra_combo_id10/11 (clone+0x34[9]/[10] natif —
    /// émis dans la queue fixe du record item, après le bloc train).
    /// </summary>
    public int ExtraComboId10 { get; set; }
    public int ExtraComboId11 { get; set; }
    /// <summary>Lecture du vecteur natif `extra_combo_id1..11`
    /// (clone+0x34 u16[11]) : index 0-8 → <see cref="ExtraCombos"/>,
    /// 9 → <see cref="ExtraComboId10"/>, 10 →
    /// <see cref="ExtraComboId11"/>. Les paliers de fortif
    /// (AddComboStrengthen @0x080a2160) écrivent aux index 6-10.</summary>
    public int GetExtraCombo(int index) => index switch
    {
        >= 0 and <= 8 => ExtraCombos[index],
        9 => ExtraComboId10,
        10 => ExtraComboId11,
        _ => 0,
    };
    /// <summary>Écriture du même vecteur natif — voir
    /// <see cref="GetExtraCombo"/>.</summary>
    public void SetExtraCombo(int index, int value)
    {
        switch (index)
        {
            case >= 0 and <= 8: ExtraCombos[index] = value; break;
            case 9: ExtraComboId10 = value; break;
            case 10: ExtraComboId11 = value; break;
        }
    }
    /// <summary>gold_enchant natif (colonne `gold_enchant`, clone
    /// +0x64 de la queue fixe u16 — enchant doré d'instance).</summary>
    public int GoldEnchant { get; set; }
    /// <summary>plus_parameter natif (colonne `plus_parameter`,
    /// clone+0x2e — paramètre des items "+N").</summary>
    public int PlusParameter { get; set; }
    /// <summary>color natif (colonne `color`, clone+0x50 i64 —
    /// émis en bit21 du mask conditionnel).</summary>
    public long Color { get; set; }

    // --- Mana Infusion (CEquipTrainAttrs natif, clone+0x58) --------
    /// <summary>Niveau d'entraînement courant (clone+0x58, colonne
    /// `train_level`) — 0 = jamais entraîné ; mis à 1 au premier
    /// `SetItemTrainType` natif.</summary>
    public int TrainLevel { get; set; }
    /// <summary>EXP d'entraînement dans le palier courant (clone+0x5c,
    /// colonne `train_exp` u32).</summary>
    public long TrainExp { get; set; }
    /// <summary>Type d'attribut choisi en slot i (clone+0x60/62/64 —
    /// colonnes `train_slotN_type`, 0-14, 0 = slot vide).</summary>
    public int TrainSlot1Type { get; set; }
    public int TrainSlot2Type { get; set; }
    public int TrainSlot3Type { get; set; }
    /// <summary>Niveau du slot i (clone+0x66/68/6a — colonnes
    /// `train_slotN_level`, incrémenté au level-up pondéré).</summary>
    public int TrainSlot1Level { get; set; }
    public int TrainSlot2Level { get; set; }
    public int TrainSlot3Level { get; set; }
    /// <summary>Flag « maudit » (colonne `witchcraft` — "Update Item
    /// WitchCraft" natif). Posé par les produits player_works maudits
    /// (SWorkItem+0x08, combine+0x3c — toujours 0 en données).</summary>
    public int Witchcraft { get; set; }

    /// <summary>embedded_idN par index 0-5 (ordre du vecteur natif
    /// clone+0x40).</summary>
    public int GetEmbeddedId(int index) => index switch
    {
        0 => EmbeddedId1, 1 => EmbeddedId2, 2 => EmbeddedId3,
        3 => EmbeddedId4, 4 => EmbeddedId5, 5 => EmbeddedId6,
        _ => -1,
    };

    /// <summary>Écrit embedded_idN par index 0-5.</summary>
    public void SetEmbeddedId(int index, int value)
    {
        switch (index)
        {
            case 0: EmbeddedId1 = value; break;
            case 1: EmbeddedId2 = value; break;
            case 2: EmbeddedId3 = value; break;
            case 3: EmbeddedId4 = value; break;
            case 4: EmbeddedId5 = value; break;
            case 5: EmbeddedId6 = value; break;
        }
    }

    /// <summary>Type d'attribut du slot train i (clone+0x60+2i).</summary>
    public int GetTrainType(int i) => i switch
    {
        0 => TrainSlot1Type, 1 => TrainSlot2Type, _ => TrainSlot3Type,
    };

    /// <summary>Niveau du slot train i (clone+0x66+2i).</summary>
    public int GetTrainSlotLevel(int i) => i switch
    {
        0 => TrainSlot1Level, 1 => TrainSlot2Level, _ => TrainSlot3Level,
    };

    /// <summary>Écrit le type du slot train i (0-2).</summary>
    public void SetTrainType(int i, int value)
    {
        switch (i)
        {
            case 0: TrainSlot1Type = value; break;
            case 1: TrainSlot2Type = value; break;
            default: TrainSlot3Type = value; break;
        }
    }

    /// <summary>Écrit le niveau du slot train i (0-2).</summary>
    public void SetTrainSlotLevel(int i, int value)
    {
        switch (i)
        {
            case 0: TrainSlot1Level = value; break;
            case 1: TrainSlot2Level = value; break;
            default: TrainSlot3Level = value; break;
        }
    }
}
