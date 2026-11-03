using Eden.Core.Data;
using Eden.Core.Game.Inventory;

namespace Eden.Zone.Codecs;

/// <summary>
/// Corps NCID 571 `CNC_CZ_ClientPVPRankLookEquip` — réponse au 231
/// (désassemblage Serialize @0x08205af0 + Execute @0x0826edb0 de
/// lapis::CNE_CZ_ZoneServerPVPRankLookEquip, zone/decompiled.c) :
/// <code>
///   [i32 uid][u16 len][str name]
///   [u16 class_id(+0x47c)][u16 char_lv(+0x470)]
///   [u16 class_lv(+0x47e)][u16 hero_feature(GetHeroFeature &amp; 0xff)]
///   [i32 score = Σ T(niveau de classe) sur la classlist (char+0xad8)]
///   [u8  page d'équipement (char+0xd9c[page*0x14+4] — 0 hors live)]
///   [32B apparence (char+0x48e..+0x4aa = appearance_data)]
///   [u16 n][n×s16 clone+0xc par slot conteneur 0 (0 = slot vide)]
///   [u16 n][n×s16 clone+0xe par slot conteneur 0]
///   [i32 mask slots conteneur 0][popcount × records WriteEquipViewItem]
///   [i32 mask slots conteneur 1][popcount × records WriteLightItem]
/// </code>
/// Les deux vecs s16 sont indexés par loc — même longueur que le plus
/// haut slot occupé +1 (slots vides → 0).
/// </summary>
public static class PvpRankEquipPackets
{
    /// <summary>Construit le corps du 571 pour un rangé. `set0` =
    /// items du conteneur 0 (équipement principal), `set1` = conteneur
    /// 1 (2ᵉ set — records légers). Les `ItemEntity.Slot` portent les
    /// locs natifs.</summary>
    public static byte[] Build571(int uid, string name,
        short classId, short charLevel, short classLevel,
        short heroFeature, int classLevelScore, byte pageFlag,
        byte[] appearance,
        IReadOnlyList<ItemEntity> set0,
        IReadOnlyList<ItemEntity> set1)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(uid);                          // i32 — uid du rangé
        var nb = EdenEncoding.Big5.GetBytes(name ?? "");
        w.Write((ushort)nb.Length);            // u16 — len nom
        w.Write(nb);                           // str — given_name
        w.Write(classId);                      // u16 — char+0x47c classe
        w.Write(charLevel);                    // u16 — char+0x470 niveau
        w.Write(classLevel);                   // u16 — char+0x47e lv.classe
        w.Write(heroFeature);                  // u16 — GetHeroFeature&0xff
        w.Write(classLevelScore);              // i32 — Σ T(lv) classlist
        w.Write(pageFlag);                     // u8  — page d'équipement
        var app = new byte[0x20];
        Array.Copy(appearance, app,
            Math.Min(appearance.Length, 0x20));
        w.Write(app);                          // 32B — appearance_data
        // Vecs parallèles clone+0xc/+0xe indexés par loc (conteneur 0).
        var dedup = set0.Where(i => i.Slot >= 0 && i.Slot < 32)
            .GroupBy(i => i.Slot)
            .Select(g => g.First())
            .OrderBy(i => i.Slot).ToList();
        var slots = dedup.Count == 0 ? 0 : dedup[^1].Slot + 1;
        w.Write((ushort)slots);                // u16 — taille vec +0x3c
        for (var loc = 0; loc < slots; loc++)
            w.Write((short)(dedup.FirstOrDefault(i => i.Slot == loc)
                ?.Durability ?? 0));           // s16 — clone+0xc du loc
        w.Write((ushort)slots);                // u16 — taille vec +0x48
        for (var loc = 0; loc < slots; loc++)
            w.Write((short)(dedup.FirstOrDefault(i => i.Slot == loc)
                ?.MaxDurability ?? 0));        // s16 — clone+0xe du loc
        // Conteneur 0 — mask de locs + records chaînés complets.
        var mask0 = 0u;
        foreach (var i in dedup) mask0 |= 1u << i.Slot;
        w.Write(mask0);                        // i32 — mask slots
        foreach (var i in dedup)
            NativeItemCodec.WriteEquipViewItem(w, i);
        // Conteneur 1 (2ᵉ set) — mask + records légers 20B.
        var light = set1.Where(i => i.Slot >= 0 && i.Slot < 32)
            .GroupBy(i => i.Slot)
            .Select(g => g.First())
            .OrderBy(i => i.Slot).ToList();
        var mask1 = 0u;
        foreach (var i in light) mask1 |= 1u << i.Slot;
        w.Write(mask1);                        // i32 — mask slots
        foreach (var i in light)
            NativeItemCodec.WriteLightItem(w, i);
        return ms.ToArray();
    }
}
