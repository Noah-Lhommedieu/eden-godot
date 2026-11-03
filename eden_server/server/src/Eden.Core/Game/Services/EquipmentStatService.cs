using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;

namespace Eden.Core.Game.Services;

/// <summary>
/// Somme des bonus d'équipement porté — équivalent serveur de
/// `lapis::CCharacter::RefreshEQsAttr` @0x841abf0 (désasm zone) qui
/// agrège les CItemAttr de chaque pièce équipée dans les champs
/// CCharacter+0x38c..0x3d2. Les valeurs viennent des colonnes S_Item
/// mappées par `CItemDBO::SetItemAttrData` @0x8358200 — aucun bonus
/// n'est codé par item.
/// </summary>
public readonly record struct EquipmentStats(
    int BonusHp, int BonusMp,
    int Str, int Agi, int Int, int Wis, int Lck,
    int PAtk, int MAtk, int Def,
    int Hit, int Dodge, int Crit, int CritDamage,
    int MagicCrit, int MagicCritDamage, int[] Resists,
    int AttackSpeed = 0,
    int CastSpeed = 0, int Parry = 0, int Block = 0,
    int Heal = 0, int BeHealed = 0)
{
    /// <summary>Additionne deux ensembles de bonus (les résistances
    /// s'additionnent élément par élément comme char+0x3d4..+0x3e2).</summary>
    public EquipmentStats Add(EquipmentStats o)
    {
        var r = new int[9];
        for (var i = 0; i < 9; i++)
            r[i] = (Resists?.ElementAtOrDefault(i) ?? 0)
                + (o.Resists?.ElementAtOrDefault(i) ?? 0);
        return new EquipmentStats(
            BonusHp + o.BonusHp, BonusMp + o.BonusMp,
            Str + o.Str, Agi + o.Agi, Int + o.Int, Wis + o.Wis,
            Lck + o.Lck, PAtk + o.PAtk, MAtk + o.MAtk, Def + o.Def,
            Hit + o.Hit, Dodge + o.Dodge, Crit + o.Crit,
            CritDamage + o.CritDamage,
            MagicCrit + o.MagicCrit,
            MagicCritDamage + o.MagicCritDamage, r,
            AttackSpeed + o.AttackSpeed,
            CastSpeed + o.CastSpeed, Parry + o.Parry,
            Block + o.Block, Heal + o.Heal,
            BeHealed + o.BeHealed);
    }
}

/// <summary>Calcule les stats agrégées de l'équipement porté.</summary>
public static class EquipmentStatService
{
    /// <summary>Somme les colonnes stats de S_Item sur toutes les
    /// pièces équipées (equipment1/2 → defs). Les valeurs retournées
    /// sont les valeurs RÉELLES : les stats core (STR/AGI/INT/WIS/LCK)
    /// sont multipliées par `GameBalance.CoreStatDivisor` — le client
    /// affiche 1 point par tranche de 8 points réels (ratio
    /// configurable pour l'équilibrage, EDEN_BAL_CORESTATDIVISOR).
    /// P-ATK/M-ATK/DEF sont des stats finales — pas de ratio.</summary>
    public static EquipmentStats Sum(IEnumerable<ItemEntity> equipped,
        IReadOnlyList<ItemParser.ItemDef> defs)
        => Sum(equipped, defs, null);

    /// <summary><see cref="Sum"/> enrichi des contributions Mana
    /// Infusion — <paramref name="trainAdds"/> retourne pour un item
    /// les 15 valeurs du clone entraîné (indexé par type 0-14, voir
    /// <c>EquipTrainService.ItemTrainAdds</c> — natif
    /// <c>UpdateCloneItemAddAttr</c> case <c>*(this_02+0x30)&amp;0x40
    /// </c> : addattr alimenté par CEquipTrainAttrs).</summary>
    public static EquipmentStats Sum(IEnumerable<ItemEntity> equipped,
        IReadOnlyList<ItemParser.ItemDef> defs,
        Func<ItemEntity, ItemParser.ItemDef, int[]>? trainAdds)
    {
        var byId = new Dictionary<int, ItemParser.ItemDef>(defs.Count);
        foreach (var d in defs) byId.TryAdd(d.Id, d);
        var total = new EquipmentStats();
        foreach (var e in equipped)
        {
            // RefreshEQsAttr natif : seuls les conteneurs clé <2 ou
            // ==5 contribuent — la map char+0x4fc les saute pour les
            // autres clés (2/3 = sets fashion : affichés, zéro stat).
            if (e.BagId >= 2 && e.BagId != 5) continue;
            // RefreshEQAttr : stats+effects gatés par clone+0x24 bit3
            // = identifié — un équipement non identifié ne donne rien.
            if (e.Identify == 0) continue;
            if (!byId.TryGetValue(e.ItemId, out var def)) continue;
            var t = trainAdds?.Invoke(e, def);
            int T(int type) => t != null && type < t.Length
                ? t[type] : 0;
            total = total.Add(new EquipmentStats(
                // types 0/1 : HP/MP (addattr +0x00/+0x02 natifs)
                def.BonusHp + T(0), def.BonusMp + T(1),
                def.BonusStr * GameBalance.CoreStatDivisor,
                def.BonusAgi * GameBalance.CoreStatDivisor,
                def.BonusInt * GameBalance.CoreStatDivisor,
                def.BonusWis * GameBalance.CoreStatDivisor,
                def.BonusLck * GameBalance.CoreStatDivisor,
                // types 2/3 : P-ATK/M-ATK (+0x3c/+0x40), 9 : DEF
                // (+0x34→char+0x396), 4 : ACC, 10 : EVA, 6 : CRIT,
                // 8 : M-CRIT
                def.PhysicalAttack + T(2), def.MagicAttack + T(3),
                def.Defence + T(9),
                def.Hit + T(4), def.Dodge + T(10), def.Critical + T(6),
                def.CriticalDamage,
                def.MagicCritical + T(8), def.MagicCriticalDamage,
                def.Resistances,
                // type 5 : ATK SPD (+0x3c8), 7 : cast spd, 11 : parry
                // (+0x3d0), 12 : block (+0x3d2), 13 : G-Heal (+0x3cc),
                // 14 : P-Heal (+0x3ce)
                def.AttackSpeed + T(5),
                T(7), T(11), T(12), T(13), T(14)));
        }
        return total;
    }

    // ===== Sources additionnelles (stages gems / fortify / costumes) =====

    /// <summary>Pièces qui comptent pour les stats « équipement »
    /// (RefreshEQsAttr natif : clés &lt;2 ou ==5, identifiées). Les
    /// pages costume 2-4 sont exclues.</summary>
    public static bool CountsForEquipment(ItemEntity e)
        => (e.BagId < 2 || e.BagId == 5) && e.Identify != 0;

    /// <summary>Pièces costume portées (conteneurs 2/3/4).</summary>
    public static bool IsCostumePage(ItemEntity e)
        => e.BagId is >= 2 and <= 4;

    private static EquipmentStats FromDef(ItemParser.ItemDef def)
        => new(def.BonusHp, def.BonusMp,
            def.BonusStr * GameBalance.CoreStatDivisor,
            def.BonusAgi * GameBalance.CoreStatDivisor,
            def.BonusInt * GameBalance.CoreStatDivisor,
            def.BonusWis * GameBalance.CoreStatDivisor,
            def.BonusLck * GameBalance.CoreStatDivisor,
            def.PhysicalAttack, def.MagicAttack, def.Defence,
            def.Hit, def.Dodge, def.Critical, def.CriticalDamage,
            def.MagicCritical, def.MagicCriticalDamage, def.Resistances,
            def.AttackSpeed);

    /// <summary>Gemmes incrustées (embedded_id1-6 = item_id TEMPLATE
    /// de la gemme, 0/−1 = socket vide/fermée) : somme des colonnes
    /// stats S_Item de chaque gemme sur les pièces qui comptent.</summary>
    public static EquipmentStats SumGems(IEnumerable<ItemEntity> equipped,
        IReadOnlyList<ItemParser.ItemDef> defs)
    {
        var byId = new Dictionary<int, ItemParser.ItemDef>(defs.Count);
        foreach (var d in defs) byId.TryAdd(d.Id, d);
        var total = new EquipmentStats();
        foreach (var e in equipped)
        {
            if (!CountsForEquipment(e)) continue;
            var sockets = Math.Min(e.EmbeddedAmount & 7, 6);
            for (var i = 0; i < sockets; i++)
            {
                var gem = e.GetEmbeddedId(i);
                if (gem <= 0 || !byId.TryGetValue(gem, out var gd))
                    continue;
                total = total.Add(FromDef(gd));
            }
        }
        return total;
    }

    /// <summary>Fortification +N (S_Parameter 56 arme / 55 armure :
    /// +<paramref name="weaponPct"/>/<paramref name="armorPct"/> % par
    /// niveau de la valeur de base de l'item) : armes → P-ATK/M-ATK,
    /// autres pièces → DEF.</summary>
    public static EquipmentStats SumFortify(IEnumerable<ItemEntity> equipped,
        IReadOnlyList<ItemParser.ItemDef> defs,
        double weaponPct = 3, double armorPct = 3)
    {
        var byId = new Dictionary<int, ItemParser.ItemDef>(defs.Count);
        foreach (var d in defs) byId.TryAdd(d.Id, d);
        int pAtk = 0, mAtk = 0, def = 0;
        foreach (var e in equipped)
        {
            if (!CountsForEquipment(e) || e.EnchantLevel <= 0) continue;
            if (!byId.TryGetValue(e.ItemId, out var d)) continue;
            var isWeapon = d.Kind is >= 0x11 and <= 0x21
                or >= 0x3d and <= 0x42;
            if (isWeapon)
            {
                pAtk += (int)(d.PhysicalAttack * e.EnchantLevel
                    * weaponPct / 100.0);
                mAtk += (int)(d.MagicAttack * e.EnchantLevel
                    * weaponPct / 100.0);
            }
            else
                def += (int)(d.Defence * e.EnchantLevel * armorPct / 100.0);
        }
        return new EquipmentStats(0, 0, 0, 0, 0, 0, 0, pAtk, mAtk, def,
            0, 0, 0, 0, 0, 0, new int[9]);
    }

    /// <summary>Ids S_Enchant portés par les combos de boîte magique
    /// (magicbox_enchant1-3 = ids S_ItemCombo — le log natif de
    /// l'insertion d'item les nomme « magicbox_combo1-3 ») des pièces
    /// qui comptent pour l'équipement. Table combo→effet = S_ItemCombo
    /// col 10 (<see cref="CraftRecipeParser.ItemCombo.EnchantId"/>).
    /// </summary>
    public static IEnumerable<int> MagicBoxEnchantIds(
        IEnumerable<ItemEntity> equipped,
        IReadOnlyList<CraftRecipeParser.ItemCombo> combos)
    {
        var byId = new Dictionary<int, int>(combos.Count);
        foreach (var c in combos)
            if (c.EnchantId > 0) byId.TryAdd(c.Id, c.EnchantId);
        foreach (var e in equipped)
        {
            if (!CountsForEquipment(e)) continue;
            foreach (var cid in new[] { e.MagicBoxEnchant1,
                         e.MagicBoxEnchant2, e.MagicBoxEnchant3 })
                if (cid > 0 && byId.TryGetValue(cid, out var eid))
                    yield return eid;
        }
    }

    /// <summary>Ids S_Enchant posés par <c>gold_enchant</c> (u16 clone+0x6c
    /// ; log natif <c>ModifyItemTransGold</c> @0x08356020 :
    /// « Update Item GoldEnchant … enchant_id=%hd » → la colonne EST un
    /// id S_Enchant) des pièces qui comptent pour l'équipement. 0 = rien.
    /// </summary>
    public static IEnumerable<int> GoldEnchantIds(
        IEnumerable<ItemEntity> equipped)
    {
        foreach (var e in equipped)
            if (e.GoldEnchant > 0 && CountsForEquipment(e))
                yield return e.GoldEnchant;
    }

    /// <summary>Ids S_Enchant des scrolls d'enchantement (witchcraft =
    /// item_id du scroll, kind 56 équipement / kind 79 costume) posés
    /// sur les pièces sélectionnées par <paramref name="costume"/>.
    /// </summary>
    public static IEnumerable<int> WitchcraftEnchantIds(
        IEnumerable<ItemEntity> equipped,
        IReadOnlyList<ItemParser.ItemDef> defs, bool costume)
    {
        var byId = new Dictionary<int, ItemParser.ItemDef>(defs.Count);
        foreach (var d in defs) byId.TryAdd(d.Id, d);
        foreach (var e in equipped)
        {
            if (e.Witchcraft <= 0) continue;
            if (costume ? !IsCostumePage(e) : !CountsForEquipment(e))
                continue;
            if (byId.TryGetValue(e.Witchcraft, out var scroll) &&
                scroll.UseEnchantId > 0)
                yield return scroll.UseEnchantId;
        }
    }
}
