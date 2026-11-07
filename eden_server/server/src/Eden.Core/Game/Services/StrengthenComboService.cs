using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using System;
using System.Collections.Generic;

namespace Eden.Core.Game.Services;

/// <summary>Miroir de <c>lapis::CItemFactory::AddComboStrengthen</c>
/// (zone @0x080a2160) — le tirage de combo bonus déclenché par
/// <c>DoStrengthenItem</c> (@0x080a2b80) après un renfort réussi aux
/// paliers 6/8/10/12/14 (et kind ≠ 0x51, vérifié par l'appelant).
///
/// Séquence native prouvée par désassemblage :
/// <list type="number">
/// <item>Slot = premier index LIBRE dans le vecteur natif
/// <c>clone+0x34</c> (u16[11] = extra_combo_id1..11) aux positions
/// 6..10 ; tous occupés → retour 0.</item>
/// <item>Candidats : kind de l'item (<c>CItemData+0x20</c>) présent dans
/// la liste col3 du combo ET niveau requis (<c>CItemLimit+0x22</c>) dans
/// [MinLevel, MaxLevel].</item>
/// <item>Si le flag col9 (+0x1b) == 0 : exclusion si l'id est déjà posé
/// dans les slots 6..10, si le groupe col7 (+0x18) égale le groupe d'un
/// combo déjà posé (ou 0 — le vecteur natif est complété par des 0), ou
/// si la col4 (+0x14, pool/tier) &gt; 3.</item>
/// <item>Poids cumulatif <c>total += (s8)col8</c> dans un arbre ordonné,
/// puis <c>random() % total</c> → premier cumul &gt; roll.</item>
/// <item>Écrit l'id du combo au slot choisi ; la persistance
/// (<c>CItemDBO::ModifyItemCombo</c>, 11 u16) et l'émission du 369
/// <c>CNC_CZ_ClientModifyItemInfo</c> sont laissées à l'appelant.</item>
/// </list></summary>
public static class StrengthenComboService
{
    /// <summary>Premier index du vecteur extra-combo réservé aux paliers
    /// de fortification (offsets +0xc..+0x14 natifs → u16[6..10]).</summary>
    public const int FirstSlot = 6;
    /// <summary>Dernier index du vecteur extra-combo de fortification.</summary>
    public const int LastSlot = 10;

    /// <summary>Roll natif : choisit et pose un combo sur
    /// <paramref name="item"/>. Retourne l'id du combo posé, 0 si aucun
    /// slot libre ou aucun candidat valide.</summary>
    /// <param name="item">Item cible (vecteur extra-combo lu/écrit).</param>
    /// <param name="kind">Type d'item (col8 S_Item → CItemData+0x20).</param>
    /// <param name="requiredLevel">Niveau requis de l'item
    /// (CItemLimit+0x22 natif).</param>
    /// <param name="combos">Table S_ItemCombo complète.</param>
    /// <param name="next">RNG injectable : <c>next(total)</c> doit rendre
    /// un entier dans [0,total[ ; défaut <see cref="Random.Shared"/>.</param>
    public static int Roll(ItemEntity item, int kind, int requiredLevel,
        IReadOnlyList<CraftRecipeParser.ItemCombo> combos,
        Func<int, int>? next = null)
    {
        // Premier slot libre 6..10 — natif retourne 0 si tous occupés.
        var slot = -1;
        for (var i = FirstSlot; i <= LastSlot; i++)
            if (item.GetExtraCombo(i) == 0) { slot = i; break; }
        if (slot < 0) return 0;

        // Ids déjà posés (slots 6..10) et groupes des combos existants.
        var usedIds = new HashSet<int>();
        var usedGroups = new HashSet<int>();
        for (var i = FirstSlot; i <= LastSlot; i++)
        {
            var id = item.GetExtraCombo(i);
            if (id == 0) continue;
            usedIds.Add(id);
            var rec = Lookup(combos, id);
            if (rec != null) usedGroups.Add(rec.Tier);
        }

        var total = 0;
        var pool = new List<(int Id, int Cum)>();
        foreach (var c in combos)
        {
            if (c.ApplicableItemTypes.Count == 0 ||
                !c.ApplicableItemTypes.Contains(kind)) continue;
            if (requiredLevel < c.MinLevel || requiredLevel > c.MaxLevel)
                continue;
            // flag col9 == 0 → toutes les exclusions natives.
            if (c.Flag == 0)
            {
                if (usedIds.Contains(c.Id)) continue;
                if (c.Tier == 0 || usedGroups.Contains(c.Tier)) continue;
                if (c.AttrType > 3) continue;
            }
            total += c.Value;
            // natif : map<cum,combo> — cum égal (poids 0) → le dernier
            // combo écrase l'entrée existante à cette clé.
            if (pool.Count > 0 && pool[^1].Cum == total)
                pool[^1] = (c.Id, total);
            else
                pool.Add((c.Id, total));
        }
        if (total <= 0) return 0;

        var roll = (next ?? (t => Random.Shared.Next(t)))(total);
        foreach (var (id, cum) in pool)
            if (roll < cum) { item.SetExtraCombo(slot, id); return id; }
        var last = pool[^1].Id;
        item.SetExtraCombo(slot, last);
        return last;
    }

    private static CraftRecipeParser.ItemCombo? Lookup(
        IReadOnlyList<CraftRecipeParser.ItemCombo> combos, int id)
    {
        foreach (var c in combos)
            if (c.Id == id) return c;
        return null;
    }
}
