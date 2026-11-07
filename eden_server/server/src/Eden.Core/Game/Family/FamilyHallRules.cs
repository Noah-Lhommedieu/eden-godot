using Eden.Core.Game.Services;

namespace Eden.Core.Game.Family;

/// <summary>
/// Règles pures du hall de famille — réplique fidèle des helpers
/// <c>SFamily::*</c> du ZoneServer natif :
/// <list type="bullet">
/// <item><see cref="GroupRange"/> — découpage des locs du conteneur 0
/// en groupes de bâtiments (désasm <c>SFamily::GetBuildingLocByTargetLoc</c>
/// + <c>CheckFamilyItemDelete</c> @0x8465fd0) : 0-9→base 0, 10-19→10,
/// 20-29→20, 30-39→30, 40-49→40, 0x33 = slot spécial (item sacré).</item>
/// <item><see cref="CheckFamilyItemDelete"/> — l'item présent à la loc
/// doit être supprimable : le housekeeper (item_tid == npc item du
/// bâtiment) en base+1 est indélétable ; un bâtiment (type 0x29)
/// n'est supprimable que si son groupe est quasi vide
/// (@0x8465fd0).</item>
/// <item><see cref="CheckNpcSizeLimit"/> — cap de NPCs par bâtiment
/// selon son niveau : tables <c>HouseNpcLimit</c> (type 2),
/// <c>ProduceNpcLimit</c> (types 4-9), <c>MallNpcLimit</c> (type 10)
/// lues en .rodata @0x888fb52/0x888fb6e/0x888fb8a.</item>
/// <item><see cref="CheckItemLoc"/> — slot valide pour le type
/// d'item posé (<c>CNE_CZ_ZoneServerFamilyContainerPutItem::
/// CheckItemLoc</c> @0x8254d40).</item>
/// <item><see cref="ResolveNpcPosition"/> — position d'un NPC de hall
/// = slot de base de la scène (<c>[position]</c> col7 = aire
/// 0x32-0x38) + offset de la maison (<c>4PHouse.ini</c>) rotaté par le
/// facing du slot (<c>SFamily::GetNpcPositionByNpcLoc</c>
/// @0x84662b0).</item>
/// </list>
/// </summary>
public static class FamilyHallRules
{
    /// <summary>Loc spéciale du hall — item sacré/de quête
    /// (0x33 = 51, aire de position 0x38).</summary>
    public const short HolyLoc = 0x33;

    /// <summary>Loc du housekeeper (centre du hall, aire 0x32).</summary>
    public const short HouseKeeperLoc = 100;

    /// <summary>Capacité native des conteneurs annexes —
    /// <c>SFamily::IsBuildingStorageFull</c>/<c>IsNpcStorageFull</c>
    /// comparent le nombre d'items à 40.</summary>
    public const int StorageCapacity = 40;

    /// <summary>Cap NPCs du bâtiment-maison (type 2) par niveau de
    /// bâtiment-1 — .rodata <c>HouseNpcLimit</c> @0x888fb52.</summary>
    public static readonly ushort[] HouseNpcLimit =
        { 0, 0, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 };

    /// <summary>Cap NPCs des bâtiments de production (types 4-9) —
    /// .rodata <c>ProduceNpcLimit</c> @0x888fb6e.</summary>
    public static readonly ushort[] ProduceNpcLimit =
        { 0, 0, 4, 5, 6, 8, 8, 8, 8, 8, 8, 8, 8, 8 };

    /// <summary>Cap NPCs du bâtiment mall (type 10) —
    /// .rodata <c>MallNpcLimit</c> @0x888fb8a.</summary>
    public static readonly ushort[] MallNpcLimit =
        { 0, 0, 0, 1, 1, 2, 2, 3, 3, 3, 3, 3, 3, 3 };

    /// <summary>Base et borne haute du groupe d'une loc —
    /// découpage natif de <c>CheckFamilyItemDelete</c> @0x8465fd0 :
    /// 0-9→[0,8], 10-19→[10,0x12], 20-29→[20,0x1c], 30-39→[30,0x26],
    /// 40-49→[40,0x30], hors plage → (-1,7).</summary>
    public static (short Base, short Max) GroupRange(short loc)
    {
        if (loc < 10) return (0, 8);
        if (loc < 20) return (10, 0x12);
        if (loc < 30) return (20, 0x1c);
        if (loc < 40) return (30, 0x26);
        if (loc < 50) return (40, 0x30);
        return (-1, 7);
    }

    /// <summary>Base de groupe native (<c>loc/10*10</c>).</summary>
    public static short GroupBase(short loc) => (short)(loc / 10 * 10);

    /// <summary><c>SFamily::CheckFamilyItemDelete</c> @0x8465fd0 —
    /// l'item posé à <paramref name="loc"/> (s'il existe) doit être
    /// retirable : le housekeeper du bâtiment (item_tid ==
    /// template+0x18) en base+1 est indélétable ; un bâtiment 0x29
    /// exige que les slots liés (base, max] soient quasi vides
    /// (≤1, ou ≤0 pour un bâtiment type 10 sans npc item).</summary>
    /// <param name="container0">Items du conteneur 0 indexés par loc.</param>
    /// <param name="getDef">Résolution tid → <see cref="GameDataService.BuildingDef"/>.</param>
    public static bool CheckFamilyItemDelete(
        IReadOnlyDictionary<short, FamilyBuildingItem> container0,
        short loc, Func<int, GameDataService.BuildingDef?> getDef)
    {
        if (loc == HolyLoc) return true;
        if (!container0.TryGetValue(loc, out var item)) return true;
        var (gb, gmax) = GroupRange(loc);
        if (gb < 0) return false;
        if (!container0.TryGetValue(gb, out var building)) return false;
        var bdef = getDef(building.Tid);
        if (bdef == null) return false;
        if (item.ItemType == 0x2a)
            // NPC : l'assistant du bâtiment (son npc item) en base+1
            // est indélétable ; tout autre NPC l'est.
            return item.ItemTid != bdef.NpcItemTid || loc != gb + 1;
        if (item.ItemType == 0x29)
        {
            // Bâtiment : supprimable seulement si les slots de son
            // groupe (base, max] contiennent peu d'items attachés.
            var attached = container0.Keys.Count(k => k > gb && k <= gmax);
            var max = bdef.Type == 10 ? (bdef.NpcItemTid != 0 ? 1 : 0) : 1;
            return attached <= max;
        }
        return false;
    }

    /// <summary><c>SFamily::CheckNpcSizeLimit</c> @0x8464ea0 — le
    /// nombre de NPCs du groupe (slots base+1..base+8, base+0 incluse
    /// si le bâtiment n'a pas de npc item) doit rester sous le cap du
    /// niveau de bâtiment (<c>item+0x94</c>-1, borné à 13).</summary>
    public static bool CheckNpcSizeLimit(
        IReadOnlyDictionary<short, FamilyBuildingItem> container0,
        short loc, Func<int, GameDataService.BuildingDef?> getDef)
    {
        if (loc == GroupBase(loc) || loc == HolyLoc) return true;
        var gb = GroupBase(loc);
        if (!container0.TryGetValue(gb, out var building)) return false;
        var bdef = getDef(building.Tid);
        if (bdef == null) return false;
        var npcs = container0.Keys.Count(k =>
            k > gb + (bdef.NpcItemTid != 0 ? 1 : 0) && k <= gb + 8);
        var lvl = building.Level - 1;
        if (lvl > 13) return false;
        var limit = bdef.Type switch
        {
            2 => HouseNpcLimit[Math.Max(lvl, 0)],
            >= 4 and <= 9 => ProduceNpcLimit[Math.Max(lvl, 0)],
            10 => MallNpcLimit[Math.Max(lvl, 0)],
            _ => -1,
        };
        return limit >= 0 && npcs < limit;
    }

    /// <summary><c>CheckItemLoc</c> @0x8254d40 — le slot cible doit
    /// correspondre au type d'item posé (conteneur 0 uniquement) :
    /// 0x33 toujours valide ; un bâtiment (0x29) ne va que sur une base
    /// de groupe ; un NPC (0x2a) exige un slot non-base d'un groupe
    /// dont la base porte un bâtiment 0x29.</summary>
    public static bool CheckItemLoc(short itemType, short loc,
        IReadOnlyDictionary<short, FamilyBuildingItem> container0)
    {
        if (loc == HolyLoc) return true;
        if (itemType == 0x29) return loc == GroupBase(loc);
        if (itemType == 0x2a && loc != GroupBase(loc))
            return container0.TryGetValue(GroupBase(loc), out var b)
                   && b.ItemType == 0x29;
        return false;
    }

    /// <summary>Tid S_Buliding posé par un contrat 0x29 — walk natif
    /// de <c>CItemFactory::FamilyBuildingPutItemFromInventroy</c>
    /// @0x808a770 : <c>GetBuildingData(item+0x124)</c> donne l'échelon
    /// de base ; pour les types dont <c>type-4 (unsigned) &gt; 5</c>
    /// (types {1,2,10} en EDENFR), le natif remonte les <c>next</c>
    /// (lien construit par inversion des col5 prev) jusqu'à trouver
    /// l'échelon dont le niveau égale le niveau de la famille —
    /// « PutBuildingItem - No Data » si la chaîne s'épuise. Les types
    /// 4-9 posent l'échelon du contrat tel quel (la montée se fait
    /// ensuite par l'EXP de bâtiment). <paramref name="nextTid"/>
    /// = <c>GetBuildingData(def+0x30)</c> natif.</summary>
    /// <returns>null si le contrat ne résout pas un bâtiment posable
    /// (rejet muet natif).</returns>
    public static (int Tid, GameDataService.BuildingDef Def)?
        ResolveBuildingPut(int buildingTid, int familyLevel,
            IReadOnlyDictionary<int, GameDataService.BuildingDef>
                buildings,
            Func<int, int> nextTid)
    {
        if (!buildings.TryGetValue(buildingTid, out var def))
            return null;
        if ((uint)(def.Type - 4) <= 5)     // types 4-9 : échelon direct
            return (buildingTid, def);
        for (var guard = 0; def.Level != familyLevel;)
        {
            buildingTid = nextTid(buildingTid);
            if (buildingTid == 0 || ++guard > 16
                || !buildings.TryGetValue(buildingTid, out def))
                return null;
        }
        return (buildingTid, def);
    }

    /// <summary>Aire de position d'une loc — mapping natif de
    /// <c>SFamily::GetNpcPositionByNpcLoc</c> @0x84662b0 : loc 100 →
    /// 0x32, 0x33/≥50 → 0x38, sinon 0x33 + loc/10.</summary>
    public static int PositionArea(short loc) => loc switch
    {
        HouseKeeperLoc => 0x32,
        HolyLoc => 0x38,
        >= 50 => 0x38,
        _ => 0x33 + loc / 10,
    };

    /// <summary><c>SFamily::GetNpcPositionByNpcLoc</c> @0x84662b0 —
    /// position finale d'un NPC de hall :
    /// <code>
    /// slot = loc&lt;10 ? max(1,loc) : loc==10 ? 100 : (loc%10==0 ? 1 : loc%10)
    /// off  = houseOffsets[slot-1]            // 4PHouse.ini
    /// x    = baseX + off.dx·cos(f) + off.dy·sin(f)
    /// y    = baseY + off.dy·cos(f) − off.dx·sin(f)
    /// face = baseFacing + off.df
    /// </code>
    /// La base vient de la ligne <c>[position]</c> de la scène dont
    /// col7 == <see cref="PositionArea"/>(loc) ; les offsets du modèle
    /// de maison (code S_Buliding col1, ex. H00101).</summary>
    /// <returns>null si le slot dépasse la table d'offsets.</returns>
    public static (float X, float Y, float Z, float Facing)?
        ResolveNpcPosition(float baseX, float baseY, float baseZ,
            float baseFacing, short loc,
            IReadOnlyList<(float Dx, float Dy, float Df)> offsets)
    {
        int slot = loc < 10 ? (loc == 0 ? 1 : loc)
            : loc == 10 ? 100
            : (loc % 10 == 0 ? 1 : loc % 10);
        if (slot < 1 || slot > offsets.Count) return null;
        var (dx, dy, df) = offsets[slot - 1];
        var sin = MathF.Sin(baseFacing);
        var cos = MathF.Cos(baseFacing);
        return (baseX + dx * cos + dy * sin,
            baseY + dy * cos - dx * sin,
            baseZ, baseFacing + df);
    }

    // ---- Statues de famille (NCID 161) — Execute @0x8257f10,
    // CPlayerFactory::SetStatue @0x81099a0, CNode::BornStatue
    // @0x84af050, CStatueDBO::GetStatueInThisServer @0x8370ca0 ----

    /// <summary>Template du slot statue pour la position demandée
    /// (1-4 → 0xd675-0xd678 — switch natif de l'Execute).</summary>
    public static int StatueModelForPos(int pos) => 0xD675 + pos - 1;

    /// <summary>Templates statue natifs (0xd675-0xd679) — les records
    /// [npc] négatifs de la scène avec ces tids sont des slots, pas
    /// des NPCs (<c>GetStatueInThisServer</c> filtre
    /// <c>(u16)(tid + 0x298b) &lt; 5</c>).</summary>
    public static bool IsStatueTemplate(int tid) =>
        tid is >= 0xD675 and <= 0xD679;

    /// <summary>Coût natif de pose d'une statue — trésorerie
    /// (FamilyDonateMoney −500000, erreur 0x38c).</summary>
    public const int StatueCost = 500_000;

    /// <summary>Nom du 298 SeeMob pour une statue — la chaîne
    /// construite par <c>CNode::BornStatue</c> @0x84af050 :
    /// <c>"%s,%hd,×8"</c> = nom, genre, visage, coiffure,
    /// weapon_active, champ +0x14 (param de la scène, 4000),
    /// class_id, style, hide_equip. Le client crée un objet CStatue
    /// dès que l'EntityId ∈ [54901, 54905] (désasm client
    /// @0x70ce18) et parse ce CSV.</summary>
    public static string BuildStatueCsv(string name, int gender,
        int face, int hair, int weaponActive, int param14,
        int classId, int style, int hideEquip) =>
        string.Join(',', name, gender, face, hair, weaponActive,
            param14, classId, style, hideEquip);
}
