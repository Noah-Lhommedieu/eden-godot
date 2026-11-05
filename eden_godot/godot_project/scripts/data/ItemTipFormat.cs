using System.Globalization;

namespace EdenClient.Data;

/// <summary>
/// Parties pures (sans Godot, testées par NetTests) du tooltip d'item.
/// Chaque règle cite sa preuve : désasm du client Reforged
/// (<c>re/ghidra/rf_game</c>, constructeur de tooltip @0x00897fe0 /
/// @0x00898d70), table de chaînes natives <c>t_textindex.ini</c> et
/// colonnes de <c>c_item.ini</c> recoupées avec les ref-shots
/// <c>item hover *</c>.
/// </summary>
public static class ItemTipFormat
{
    // ---------------------------------------------------------------
    // Résistances
    // ---------------------------------------------------------------

    /// <summary>Noms natifs des 9 résistances, dans l'ordre des
    /// colonnes 63-71 de c_item (textindex 1202-1210 ; ordre des
    /// colonnes prouvé par les gemmes : « Ice Gem » = col 67,
    /// « Thunder Gem » = col 68, « Nature Gem » = col 69, « Holy Light
    /// Gem » = col 70, « Dark Gem » = col 71 ; désasm @0x00898d70 :
    /// +0x8c→0x4b2 Slash … +0x9c→0x4ba Dark). ⚠️ Ce n'est PAS l'ordre
    /// d'affichage du profil (Lightning avant Ice —
    /// <c>PlayerVitals.ResistWire</c> fait la correspondance).</summary>
    public static readonly string[] ResistNames =
    {
        "Slash Resistance", "Strike Resistance", "Pierce Resistance",
        "Fire Resistance", "Ice Resistance", "Lightning Resistance",
        "Nature Resistance", "Holy Resistance", "Dark Resistance",
    };

    /// <summary>Nom natif de la résistance de la colonne 63+i.</summary>
    public static string ResistName(int i) =>
        i >= 0 && i < ResistNames.Length ? ResistNames[i] : "";

    // ---------------------------------------------------------------
    // Flags d'item (c_item col 11) et d'instance
    // ---------------------------------------------------------------

    /// <summary>Bit « Can't be Traded » de c_item col 11 (bas 32 bits).
    /// Preuve données/ref-shots : Holy-Blazing Jet (col11 = 4, ref
    /// « Can't be Traded »), Neondo's Contract (4 ✓), Ancient Stone
    /// Elephant (4 ✓), Lv90 Ore Map (4 ✓), formule Gale Lv7 (4 ✓) ;
    /// contre-exemple : Trophy Enchant Guardian (col11 = 1, aucune
    /// mention dans le ref).</summary>
    public const long FlagCantTrade = 0x4;

    /// <summary>Bit 7 : « se lie au porteur » (BindItem natif, prouvé
    /// côté serveur <c>ItemParser.Bindable</c>) → « Bind on Equip »
    /// (textindex 559) tant que l'instance n'est pas liée.</summary>
    public const long FlagBindOnEquip = 0x80;

    /// <summary>Bit 4 du byte flags de l'instance (clone+0x24 &amp; 0x10 =
    /// lié — serveur <c>ItemEntity.IsBound</c> → flags 0x18/0x08 dans
    /// NativeItemCodec).</summary>
    public const int SlotBoundBit = 0x10;

    public static bool CantTrade(long defFlags) =>
        (defFlags & FlagCantTrade) != 0;

    public static bool BindsOnEquip(long defFlags) =>
        (defFlags & FlagBindOnEquip) != 0;

    public static bool IsBound(int slotFlags) =>
        (slotFlags & SlotBoundBit) != 0;

    /// <summary>Clés (textindex 550 / 560 / 559) des lignes de flags
    /// affichées, dans l'ordre natif : « Can't be Traded » puis
    /// « Already Bound » (instance liée) ou « Bind on Equip ».</summary>
    public static List<string> FlagKeys(long defFlags, int slotFlags)
    {
        var l = new List<string>(2);
        if (CantTrade(defFlags)) l.Add("Can't be Traded");
        if (IsBound(slotFlags)) l.Add("Already Bound");
        else if (BindsOnEquip(defFlags)) l.Add("Bind on Equip");
        return l;
    }

    // ---------------------------------------------------------------
    // Formatage
    // ---------------------------------------------------------------

    /// <summary>Vitesse d'attaque d'arme : col 49 en 1/10 s → « 2.0 »
    /// (ref : col49 = 20 ↔ « ATK SPD 2.0 Seconds » ; textindex 1285
    /// « ATK SPD » + 1286 « Seconds »).</summary>
    public static string AtkSpeed(int raw) =>
        (raw / 10.0).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>« 1 min. 30 sec. » (col 21 en 1/10 s ; ref Light Beam
    /// Gun « Cooldown: 1 min. 30 sec. »).</summary>
    public static string Cooldown(int ds)
    {
        var sec = (int)Math.Round(ds / 10.0);
        return sec >= 60
            ? $"{sec / 60} min." + (sec % 60 > 0 ? $" {sec % 60} sec." : "")
            : $"{Math.Max(1, sec)} sec.";
    }

    /// <summary>« +12 » / « -8 » / « 0 ».</summary>
    public static string Signed(int v) =>
        v > 0 ? "+" + v.ToString(CultureInfo.InvariantCulture)
              : v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Bonus de fortification : base × niveau × pct/niveau
    /// (formule serveur <c>EquipmentStatService</c> / params 55-56 =
    /// +3 %/niveau — HYPOTHESIS côté exactitude native, cf.
    /// ETAT-SYSTEME-STATS §4 ; reste cohérent avec les stats réelles du
    /// serveur).</summary>
    public static int Fortify(int baseValue, int level, int pctPerLevel = 3)
    {
        if (baseValue <= 0 || level <= 0) return 0;
        return (int)(baseValue * (long)level * pctPerLevel / 100);
    }

    /// <summary>Ligne « Pet Level: Lv1 0 % » (textindex 5716
    /// « Pet Level: Lv%s1 %s2% »).</summary>
    public static string PetLevel(string template, int lv, int pct) =>
        template.Replace("%s1", lv.ToString(CultureInfo.InvariantCulture))
                .Replace("%s2", pct.ToString(CultureInfo.InvariantCulture));

    // ---------------------------------------------------------------
    // Références d'item dans les descriptions (« &23552& »)
    // ---------------------------------------------------------------

    /// <summary>Ligne de description réduite à « &amp;id&amp; » = item
    /// produit à incruster (blueprints/formules/cartes de minerai :
    /// « Finished Item: &amp;28936&amp; » dans t_item.ini).</summary>
    public static bool TryItemRef(string line, out int id)
    {
        id = 0;
        var t = line.Trim();
        if (t.Length < 3 || t[0] != '&' || t[^1] != '&') return false;
        return int.TryParse(t.AsSpan(1, t.Length - 2),
            NumberStyles.None, CultureInfo.InvariantCulture, out id)
            && id > 0;
    }

    // ---------------------------------------------------------------
    // Libellés de type (désasm @0x00897fe0 : kind → id textindex)
    // ---------------------------------------------------------------

    /// <summary>Libellé natif d'un kind (c_item col 8), clé EN de
    /// t_textindex ; « » = pas de libellé (default natif). Table
    /// reprise du switch @0x00897fe0 : 1-15 armures par tiers
    /// Heavy/Light/Cloth (0x486-0x488), 16 Back, 17-22 Dagger…Axe,
    /// 23 Pikes, 24-29 Mace…Artillery Gun, 30 Sickleshot, 31-33
    /// Guitar/Staff/Grimoire, 34 Shield, 37-39 Accessory, 40
    /// Certificate, 41 Building, 42 NPC, 44 Item, 45 Gem, 46 Scroll,
    /// 47 Full Set, 48 Reward Bag, 49-57 matériaux/formules, 60 Pet,
    /// 61-68 Costume, 69-76 mobilier, 78 Class Glyph, 79 Costume
    /// Enchanting, 81 Pet Gear, 82 Guardian Spirit.</summary>
    public static string KindLabel(int kind)
    {
        switch (kind)
        {
            case 1 or 4 or 7 or 10 or 13: return "Heavy Armor";
            case 2 or 5 or 8 or 11 or 14: return "Light Armor";
            case 3 or 6 or 9 or 12 or 15: return "Cloth Armor";
            case 16: return "Back";
            case 17: return "Dagger";
            case 18: return "Rapier";
            case 19: return "Sword";
            case 20: return "Katana";
            case 21: return "Greatsword";
            case 22: return "Axe";
            case 23: return "Pikes";
            case 24: return "Mace";
            case 25: return "Cestus";
            case 26: return "Club";
            case 27: return "Hammer";
            case 28: return "Bow";
            case 29: return "Artillery Gun";
            case 30: return "Sickleshot";
            case 31: return "Guitar";
            case 32: return "Staff";
            case 33: return "Grimoire";
            case 34: return "Shield";
            case 37 or 38 or 39: return "Accessory";
            case 40: return "Certificate";
            case 41: return "Building";
            case 42: return "NPC";
            case 44: return "Item";
            case 45: return "Gem";
            case 46: return "Scroll";
            case 47: return "Full Set";
            case 48: return "Reward Bag";
            case 49: return "Fusion Material";
            case 50: return "Farm Material";
            case 51: return "Mining Material";
            case 52: return "Summon Material";
            case 53: return "Alchemy Material";
            case 54: return "Foundry Material";
            case 55: return "Magic Pool Material";
            case 56: return "Enchanting Formula";
            case 57: return "Talent Item";
            case 60: return "Pet";
            // Costumes : libellés natifs t_textindex 8499-8506 (« Arme de
            // Costume à 1 main », « Griffes de Costume »… « Costume »).
            case >= 61 and <= 68:
                return TextResolver.TextIndex(8499 + kind - 61) is
                    { Length: > 0 } cl ? cl : "Costume";
            case 69: return "Small Furniture";
            case 70: return "Large Furniture";
            case 71: return "Carpet";
            case 72: return "Wall Decor";
            case 73: return "Hanging Decor";
            case 74: return "Wallpaper";
            case 75: return "Living Room Floor";
            case 76: return "Bedroom Floor";
            case 78: return "Class Glyph";
            case 79: return "Costume Enchanting";
            case 81: return "Pet Gear";
            case 82: return "Guardian Spirit (Limited time)";
            default: return "";
        }
    }

    /// <summary>Kinds d'armes (arme principale/secondaire, hors
    /// bouclier) : 17-33 d'après la table native.</summary>
    public static bool IsWeaponKind(int kind) => kind is >= 17 and <= 33;

    /// <summary>Armures + dos + bouclier + accessoires (kinds 1-16, 34,
    /// 37-39) : pièces dont les stats c_item s'affichent en vert.</summary>
    public static bool IsGearKind(int kind) =>
        kind is >= 1 and <= 16 or 34 or >= 37 and <= 39 || IsWeaponKind(kind);

    /// <summary>Ligne « Durability » du tooltip, ou null. Les champs
    /// durabilité du wire servent de compteur de pile sur les stackables :
    /// on ne montre la durabilité que si la définition c_item en déclare
    /// une (<paramref name="defMaxDur"/> &gt; 0 : équipement, ou
    /// consommable à usure type Light Beam Gun). Sans valeur d'instance,
    /// repli sur le max de la définition pour les pièces d'équipement.</summary>
    public static (int Cur, int Max)? DurabilityLine(int defMaxDur, int kind,
        int equipLoc, int instCur, int instMax)
    {
        if (defMaxDur <= 0) return null;
        if (instMax > 0) return (instCur, instMax);
        if (IsGearKind(kind) && equipLoc > 0) return (defMaxDur, defMaxDur);
        return null;
    }

    /// <summary>Slot d'équipement (c_item col 9) → libellé : l'ordre des
    /// chaînes textindex 1047-1066 suit les locs 1..20 (ancres : loc 1 =
    /// « Head » ref Saint Blessing, loc 5 = « Legs » ref Gale Lv7,
    /// loc 9 = « 2-Handed » ref Heritage Spear, loc 12 = « Trophy
    /// Exclusive » ref Neondo). Au-delà de 20 : pas de libellé
    /// (ref Blazing Jet loc 35 : aucune mention).</summary>
    public static string LocLabel(int loc, int kind)
    {
        // Costumes (kind 68) : les locs 4/5 ne sont PAS Mains/Jambes mais
        // les accessoires de visage (lunettes, masques, boucles) et
        // spéciaux (moustache, cigarette, tétine…) — t_textindex 3451
        // « Accessoire Facial », 3452 « Accessoire Spécial » (vérifié sur
        // les 145 + 182 items kind 68 loc 4/5 de c_item/c_itemmall FR).
        if (kind == 68)
        {
            if (loc == 4 && TextResolver.TextIndex(3451) is { Length: > 0 } f)
                return f;
            if (loc == 5 && TextResolver.TextIndex(3452) is { Length: > 0 } sp)
                return sp;
        }
        return LocLabel(loc);
    }

    public static string LocLabel(int loc) => loc switch
    {
        1 => "Head", 2 => "Body", 3 => "Belt", 4 => "Hands",
        5 => "Legs", 6 => "Back", 7 => "1-Handed", 8 => "Off-Hand",
        9 => "2-Handed", 10 => "Accessory", 11 => "Ring",
        12 => "Trophy Exclusive", 13 => "Certificate Slot",
        14 => "Talent A", 15 => "Talent B", 16 => "Bracelet",
        17 => "Talent D", 18 => "Backpack", 19 => "Unique 1-Handed",
        20 => "Unique Ring",
        _ => "",
    };

    // ---------------------------------------------------------------
    // Qualité (c_item col 19)
    // ---------------------------------------------------------------

    /// <summary>Index de teinte de la qualité : 0 = aucune (blanc),
    /// 1 grey, 2 green, 3 orange, 4 blue, 5 gold, ≥6 purple. Ordre =
    /// chaînes natives 1152-1157 « Grey, Green, Orange, Blue, Gold,
    /// Purple » ; ancres ref-shots : col19 = 3 → titre orange (Light
    /// Beam Gun, Gale Lv7, Saint Blessing, Trophy Enchant), col19 = 5
    /// → titre doré (Blazing Jet, Neondo).</summary>
    public static int QualityTier(int q) => q <= 0 ? 0 : Math.Min(q, 6);
}
