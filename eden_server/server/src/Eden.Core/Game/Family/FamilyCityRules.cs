using Eden.Core.Game.Services;

namespace Eden.Core.Game.Family;

/// <summary>
/// Règles pures de la « ville de guilde » (halls S501/S502) : quels PNJ
/// de service tiennent chaque bâtiment de production, et où se trouvent
/// les aires de récolte physiques (pioche/gants/boule de cristal).
///
/// Sources (voir docs/temp-plans/2026-11-06-vdg-ressources.md) :
/// <list type="bullet">
/// <item>Le natif ne pose AUCUN PNJ tout seul sauf le housekeeper du
/// hall et celui de chaque bâtiment (<c>InitFamilyNpc</c> zone
/// @0x816f5d0 : housekeeper central + une <c>BornNPC</c> par item
/// 0x2a/0x52 du conteneur 0). Les PNJ de service (fermier, mineur,
/// invocateur…) sont des contrats que le chef pose via le clerc ; le
/// cap par bâtiment est <see cref="FamilyHallRules.ProduceNpcLimit"/>
/// (.rodata @0x888fb6e).</item>
/// <item>Type de bâtiment → service du PNJ : S_Buliding col3 (4 ferme,
/// 5 mine, 6 autel, 7 alchimie, 8 fonderie, 9 mare) ↔ S_Npc
/// <c>#:Service … 13/12/14/15/16/17</c> (TC_Service @0x80ef980).</item>
/// <item>Aires de récolte physiques : [gateway_area] keyId 60/61/62
/// des scènes S501/S502 (« points de récolte cristal / champ /
/// énergie ») ↔ S_Item col32 « 501 502;60|61|62 » des outils kind 44
/// (<c>CEC_AttrGainCollectItem::CanExecute</c> @0x085056b0).</item>
/// </list>
/// </summary>
public static class FamilyCityRules
{
    /// <summary>S_Buliding col3 — types de production.</summary>
    public const int TypeFarm = 4, TypeMine = 5, TypeAltar = 6,
        TypeAlchemy = 7, TypeFoundry = 8, TypePool = 9;

    /// <summary>Hôtel de brique (type 2, aucun PNJ dans les données FR : pas de contrat)
    /// et marché de brique (type 10).</summary>
    public const int TypeHotel = 2, TypeMarket = 10;

    /// <summary>Les 5 bases de groupe du conteneur 0 (loc 0/10/…/40)
    /// ↔ aires [position] 0x33..0x37.</summary>
    public static readonly short[] BuildingBases = { 0, 10, 20, 30, 40 };

    /// <summary>Type S_Buliding → type de service PNJ (12 mining,
    /// 13 planting, 14 summon, 15 alchemy, 16 casting, 17 enchanting),
    /// 0 si le bâtiment n'abrite pas de PNJ d'assignation.</summary>
    public static int ServiceTypeOf(int buildingType) => buildingType switch
    {
        TypeFarm => 13, TypeMine => 12, TypeAltar => 14,
        TypeAlchemy => 15, TypeFoundry => 16, TypePool => 17,
        _ => 0,
    };

    /// <summary>PNJ de service par type de bâtiment, hors housekeeper :
    /// le housekeeper natif d'un bâtiment de production EST le PNJ
    /// d'assignation de rang 1 (S_Buliding col6 → S_Item 35002 / 35012 /
    /// 35022 / 35030 / 35039 / 35068 → col35 = S_Npc 53008 / 53018 /
    /// 53028 / 53036 / 53045 / 53074). Viennent ensuite les rangs
    /// supérieurs (<c>#:Service dlg 12..17</c>) puis des PNJ de
    /// plans/composants (<c>#:Service dlg 24 store</c>). Tronqué au cap
    /// du niveau.</summary>
    private static readonly Dictionary<int, int[]> StationNpcs = new()
    {
        [TypeFarm] = new[] { 53009, 53010, 53011, 53012 },
        [TypeMine] = new[] { 53019, 53020, 53021, 53022 },
        [TypeAltar] = new[] { 53029, 53030, 53031, 53032 },
        [TypeAlchemy] = new[] { 53037, 53038, 53039, 53040 },
        [TypeFoundry] = new[] { 53046, 53047, 53048, 53049 },
        [TypePool] = new[] { 53075, 53076, 53077, 53078 },
        // Marché : les contrats FR de kind 42 qui portent un PNJ marchand (c_item col35) —
        // 34678 commissaire-priseur sanglier (53003, hôtel des ventes : comble le gap G36),
        // 34679 banquier (53004), 27332 marchand agricole célèbre (53625). Hôtel : aucun
        // contrat FR → aucun PNJ (PLAN-ACTION-3 A5).
        [TypeMarket] = new[] { 53003, 53004, 53625 },
    };

    /// <summary>Cap NPC natif d'un bâtiment de production selon son
    /// niveau (<c>ProduceNpcLimit[level-1]</c>, 0 hors table).</summary>
    public static int NpcCap(int buildingType, int buildingLevel)
    {
        var table = buildingType switch
        {
            >= 4 and <= 9 => FamilyHallRules.ProduceNpcLimit,
            TypeMarket => FamilyHallRules.MallNpcLimit,
            _ => null,
        };
        var i = buildingLevel - 1;
        if (table == null || i < 0 || i >= table.Length) return 0;
        return table[i];
    }

    /// <summary>PNJ de service à poser dans un bâtiment (housekeeper
    /// exclu), au plus <see cref="NpcCap"/>.</summary>
    public static IReadOnlyList<int> StationNpcsFor(int buildingType,
        int buildingLevel)
        => StationNpcs.TryGetValue(buildingType, out var l)
            ? l.Take(NpcCap(buildingType, buildingLevel)).ToArray()
            : Array.Empty<int>();

    /// <summary>Aire de récolte physique d'une scène de hall.</summary>
    public readonly record struct GatherArea(int KeyId, string Label,
        int VendorNpcTid);

    /// <summary>Les 3 aires de récolte du hall (keyId = col4 de
    /// [gateway_area]) et le PNJ qui vend l'outil correspondant dans
    /// le natif (S_Npc 53199 十字鎬商人 / 53200 採集手套商人 /
    /// 53201 魔力晶球商人 — posés à Aven, S001 [npc] -143/-131/-132 ;
    /// aucune scène de hall ne les porte → déviation EDENFR assumée :
    /// le serveur les injecte près de chaque aire pour que l'outil soit
    /// achetable sur place).</summary>
    public static readonly GatherArea[] GatherAreas =
    {
        new(60, "Mine de cristal (pioche)", 53199),
        new(61, "Champ (gants)", 53200),
        new(62, "Fissure de mana (boule de cristal)", 53201),
    };

    /// <summary>Clé de gateway d'une aire de récolte (60-64 : 63/64 =
    /// aires d'événements 2012 inactives). Le client envoie un
    /// CheckNodeExit en y entrant ; il n'y a aucun node cible.</summary>
    public static bool IsGatherKey(int keyId) => keyId is >= 60 and <= 64;

    /// <summary>Une ligne à poser dans le conteneur 0.</summary>
    public sealed record Placement(short Loc, short ItemType, int ItemTid,
        int Tid, short Level);

    /// <summary>Plan de peuplement d'un hall : pour chaque bâtiment de
    /// production posé, son housekeeper (base+1, si le bâtiment a un
    /// item NPC) puis ses PNJ de service (base+2…) jusqu'au cap.
    /// Les slots déjà occupés ne sont jamais écrasés.</summary>
    /// <param name="container0">Items 0x29/0x2a du conteneur 0.</param>
    /// <param name="getDef">tid S_Buliding → def.</param>
    /// <param name="housekeeperNpc">item NPC du bâtiment → tid S_Npc
    /// (col35 de l'item), 0 si inconnu.</param>
    /// <param name="contractFor">tid S_Npc → item contrat (kind 0x2a)
    /// ou 0.</param>
    public static List<Placement> PlanPopulate(
        IReadOnlyDictionary<short, FamilyBuildingItem> container0,
        Func<int, GameDataService.BuildingDef?> getDef,
        Func<int, int> housekeeperNpc,
        Func<int, int> contractFor)
    {
        var res = new List<Placement>();
        foreach (var b in BuildingBases)
        {
            if (!container0.TryGetValue(b, out var bi)
                || bi.ItemType != 0x29) continue;
            var def = getDef(bi.Tid);
            if (def == null) continue;
            // Housekeeper (base+1) — natif : posé avec le bâtiment.
            var planned = new HashSet<int>();
            if (def.NpcItemTid > 0
                && !container0.ContainsKey((short)(b + 1)))
            {
                var hk = housekeeperNpc(def.NpcItemTid);
                if (hk > 0)
                {
                    res.Add(new((short)(b + 1), 0x2a, def.NpcItemTid, hk,
                        0));   // niveau = celui du template NPC (fixé par l'appelant)
                    planned.Add(hk);
                }
            }
            // PNJ de service : base+2.. (le housekeeper occupe base+1,
            // CheckNpcSizeLimit ne le compte pas).
            var slot = b + 2;
            foreach (var npc in StationNpcsFor(def.Type, bi.Level))
            {
                while (container0.ContainsKey((short)slot)) slot++;
                if (slot > b + 8) break;
                // Déjà présent dans ce groupe (même template) → saute.
                var dup = container0.Values.Any(v =>
                    v.ItemType is 0x2a or 0x52 && v.Tid == npc
                    && FamilyHallRules.GroupBase(v.Loc) == b);
                if (dup || planned.Contains(npc)) continue;
                res.Add(new((short)slot, 0x2a, contractFor(npc), npc, 0));
                slot++;
            }
        }
        return res;
    }
}
