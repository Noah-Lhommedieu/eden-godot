using EdenClient.Protocol;

namespace EdenClient.Game;

/// <summary>
/// Règles client pures des familiers (plan 18) — sans GodotSharp,
/// testables dans NetTests : grades en lettres, drag&amp;drop du gear
/// pet (SwitchItemLocation dstType 6), choix du catalyseur de fusion,
/// texte de la ligne rouge des livres de prière.
/// </summary>
public static class PetClientRules
{
    // ---------------------------------------------------------------
    // Grades lettres (tooltip œuf + Pet Info)
    // ---------------------------------------------------------------

    /// <summary>Échelle des grades par index de croissance 0-9
    /// (CKFElf+0x2c, plafonné à 9 par la fusion : « got maximum
    /// grow_info »). Le wiki confirme que la lettre ne dépend pas du
    /// niveau du pet. HYPOTHESIS : la table index→lettre n'est pas
    /// extraite du natif (échelle D…S+ à 10 crans, vérifiée seulement
    /// sur le ref-shot « Forgotten Ghostblade » : B, B+, C+, D+).
    /// Centralisée ici pour être corrigée en un point.</summary>
    private static readonly string[] Ladder =
    {
        "D", "D+", "C", "C+", "B", "B+", "A", "A+", "S", "S+",
    };

    /// <summary>Lettre d'un index de croissance (borné 0-9).</summary>
    public static string GradeLetter(int grow) =>
        Ladder[Math.Clamp(grow, 0, Ladder.Length - 1)];

    // ---------------------------------------------------------------
    // Gear pet : conteneur elf (dstType 6), 4 slots
    // ---------------------------------------------------------------

    /// <summary>Kind des items d'équipement de familier
    /// (CItemFactory::ElfEquip @0x808cb20 : GetKind == 0x51).</summary>
    public const int PetGearKind = 81;

    /// <summary>Premier slot de la paire admise pour un EquipLoc :
    /// {7,19} → 0 (slots 0-1, armes), {10,34} → 2 (slots 2-3,
    /// colliers) ; −1 = non équipable.</summary>
    public static int PairStart(int equipLoc) => equipLoc switch
    {
        7 or 19 => 0,
        10 or 34 => 2,
        _ => -1,
    };

    /// <summary>Vrai si l'item peut être posé sur le slot 0-3 du
    /// conteneur d'un pet (même règle que le serveur : kind 81, slot
    /// explicite dans la paire de l'EquipLoc).</summary>
    public static bool CanEquip(int kind, int equipLoc, int elfSlot)
    {
        if (kind != PetGearKind || elfSlot < 0 || elfSlot > 3) return false;
        var p = PairStart(equipLoc);
        return p >= 0 && elfSlot >= p && elfSlot <= p + 1;
    }

    /// <summary>Sac → conteneur du pet : (0, bagSlot, bag) →
    /// (6, elfSlot, elfLoc).</summary>
    public static ZoneSwitchItemLocation EquipMessage(int bag, int bagSlot,
        int elfLoc, int elfSlot) => new()
    {
        SrcType = 0, SrcSlot = (short)bagSlot, SrcIndex = (short)bag,
        DstType = 6, DstSlot = (short)elfSlot, DstIndex = (short)elfLoc,
    };

    /// <summary>Conteneur du pet → sac : (6, elfSlot, elfLoc) →
    /// (0, −1, −1) = premier emplacement libre (serveur : repli
    /// GetAnyAvailableSlot).</summary>
    public static ZoneSwitchItemLocation UnequipMessage(int elfLoc,
        int elfSlot) => new()
    {
        SrcType = 6, SrcSlot = (short)elfSlot, SrcIndex = (short)elfLoc,
        DstType = 0, DstSlot = -1, DstIndex = -1,
    };

    /// <summary>Swap intra-conteneur du même pet (paires adjacentes
    /// 0↔1 / 2↔3 — ElfSwitchEquip @0x808d700).</summary>
    public static ZoneSwitchItemLocation SwapMessage(int elfLoc,
        int fromSlot, int toSlot) => new()
    {
        SrcType = 6, SrcSlot = (short)fromSlot, SrcIndex = (short)elfLoc,
        DstType = 6, DstSlot = (short)toSlot, DstIndex = (short)elfLoc,
    };

    /// <summary>Applique un move 277 réussi impliquant le conteneur
    /// elf (type 6) à la liste d'équipement <paramref name="equip"/> —
    /// sémantique serveur (MoveTo/FromElfContainerAsync,
    /// SwitchElfContainerSlotsAsync) : l'occupant de la destination
    /// prend la place libérée à la source. Retourne le nouvel itemId
    /// (0 = vide) des slots SAC src et dst.</summary>
    public static (int SrcBagItem, int DstBagItem) ApplySwitch(
        List<ZoneEnterNodeElfContainer.ElfEquipSlot> equip,
        short srcType, short srcSlot, short srcIdx,
        short dstType, short dstSlot, short dstIdx,
        int srcBagItem, int dstBagItem)
    {
        ZoneEnterNodeElfContainer.ElfEquipSlot? At(int loc, int slot) =>
            equip.FirstOrDefault(i => i.ElfLoc == loc && i.Slot == slot);
        void Put(int loc, int slot, int itemId)
        {
            equip.RemoveAll(i => i.ElfLoc == loc && i.Slot == slot);
            if (itemId > 0)
                equip.Add(new ZoneEnterNodeElfContainer.ElfEquipSlot
                {
                    ElfLoc = (sbyte)loc, Slot = slot, ItemId = itemId,
                    Amount = 1,
                });
        }
        var result = (srcBagItem, dstBagItem);
        if (srcType == 0 && dstType == 6)
        {
            var occ = At(dstIdx, dstSlot);
            Put(dstIdx, dstSlot, srcBagItem);
            result = (occ?.ItemId ?? 0, dstBagItem);
        }
        else if (srcType == 6 && dstType == 0)
        {
            var it = At(srcIdx, srcSlot);
            Put(srcIdx, srcSlot, dstBagItem);
            result = (srcBagItem, it?.ItemId ?? 0);
        }
        else if (srcType == 6 && dstType == 6)
        {
            var a = At(srcIdx, srcSlot)?.ItemId ?? 0;
            var b = At(dstIdx, dstSlot)?.ItemId ?? 0;
            Put(srcIdx, srcSlot, b);
            Put(dstIdx, dstSlot, a);
        }
        return result;
    }

    // ---------------------------------------------------------------
    // Emotes (470 ClientPlayElfPetAction, cmds 6-8)
    // ---------------------------------------------------------------

    /// <summary>Candidats d'animation KFM d'un emote pet (cmd 6-8 =
    /// SetEventAction natif). HYPOTHESIS : le mapping cmd→kfId du
    /// natif n'est pas extrait ; on tente des noms d'idle alternatifs
    /// puis l'attaque — <c>PlayOnceAny</c> ignore les absents, donc
    /// sans effet sur un modèle qui n'a aucun de ces clips. Vide hors
    /// 6-8 (cmds 1/101-105 = combat, déjà portés par le 272).</summary>
    public static string[] EmoteAnims(int cmd) => cmd switch
    {
        6 => new[] { "stand02", "idle02", "emote01", "happy01" },
        7 => new[] { "stand03", "idle03", "emote02", "happy02" },
        8 => new[] { "stand04", "skill01", "attack02", "attack01" },
        _ => Array.Empty<string>(),
    };

    // ---------------------------------------------------------------
    // Fusion : catalyseur
    // ---------------------------------------------------------------

    /// <summary>Pierres de fusion natives (DoElfUpgrade @0x8079b60).
    /// </summary>
    public static bool IsFusionCatalyst(int itemId) =>
        itemId is 0x1790 or 0x1791 or 0x1792;

    /// <summary>Premier catalyseur du sac (bag, slot) ou (−1, −1) :
    /// le natif accepte −1/−1 (fusion sans pierre).</summary>
    public static (int Bag, int Slot) PickCatalyst(
        IEnumerable<(int Bag, int Slot, int ItemId)> bagItems)
    {
        foreach (var it in bagItems)
            if (IsFusionCatalyst(it.ItemId)) return (it.Bag, it.Slot);
        return (-1, -1);
    }

    // ---------------------------------------------------------------
    // Livres de sorts pet : ligne rouge « remplace le Nᵉ slot »
    // ---------------------------------------------------------------

    /// <summary>Commandes d'enchant des livres : 6035/6037 = slot de
    /// combat, 6036/6038 = slot de prière (ZoneEntryFlow.Elf.Spell).
    /// </summary>
    public static bool TryParseBook(IReadOnlyList<string> enchantRaw,
        out bool pray, out int slot)
    {
        pray = false; slot = 0;
        // 4 blocs [cmd + 6 args] à partir de la colonne 9.
        for (var k = 0; k < 4; k++)
        {
            var at = 9 + k * 7;
            if (at + 1 >= enchantRaw.Count ||
                !int.TryParse(enchantRaw[at], out var cmd)) continue;
            if (cmd == 160 && int.TryParse(enchantRaw[at + 1], out var inner))
                cmd = inner;
            if (cmd is < 6035 or > 6038) continue;
            // Args[0] = slot 1-5 (hors wrapper 160 : at+1 ; avec : at+2).
            var argAt = int.TryParse(enchantRaw[at], out var c0) && c0 == 160
                ? at + 2 : at + 1;
            if (argAt >= enchantRaw.Count ||
                !double.TryParse(enchantRaw[argAt],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var s)) continue;
            slot = (int)s;
            if (slot < 1 || slot > 5) continue;
            pray = cmd is 6036 or 6038;
            return true;
        }
        return false;
    }

    /// <summary>Clé EN de l'ordinal d'un slot 1-5.</summary>
    public static string OrdinalKey(int slot) => slot switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth",
        _ => "fifth",
    };
}
