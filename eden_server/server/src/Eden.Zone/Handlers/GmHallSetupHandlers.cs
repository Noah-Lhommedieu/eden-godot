using Eden.Core.Data.Parsers;
using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Peuplement d'une ville de guilde de test : housekeepers de bâtiment
/// + PNJ de service (fermier, mineur, invocateur…) aux slots natifs.
///
/// Le natif ne fait que ceci (<c>InitFamilyNpc</c> zone @0x816f5d0) :
/// housekeeper central + une <c>BornNPC</c> par item 0x2a/0x52 du
/// conteneur 0, à la position persistée. Les items sont posés par le
/// chef (clerc de ville → conteneur) ; les positions sont celles de
/// <c>SFamily::GetNpcPositionByNpcLoc</c> @0x84662b0 (aire [position]
/// de la scène + offsets 4PHouse rotatés). Les commandes GM
/// <c>give_building</c>/<c>place_npc</c> historiques n'écrivaient ni le
/// housekeeper du bâtiment ni la position native ; cette classe comble
/// le trou pour les guildes de test, avec exactement les formules
/// natives (aucune position inventée).
///
/// Commande : <c>//hall_setup [reset]</c> sur la guilde du hall visité
/// (sinon celle du perso). <c>reset</c> supprime d'abord les NPC
/// (0x2a/0x52) posés dans les groupes de bâtiments.
/// </summary>
public static class GmHallSetupHandlers
{
    /// <summary>Bâtiments de production posés par <c>hall_setup</c>
    /// quand le type manque (S_Buliding col3), dans l'ordre : ferme,
    /// mine, autel — les trois systèmes de ressources — puis
    /// alchimie et fonderie (5 bases de groupe max).</summary>
    private static readonly int[] DefaultTypes =
    {
        FamilyCityRules.TypeFarm, FamilyCityRules.TypeMine,
        FamilyCityRules.TypeAltar, FamilyCityRules.TypeAlchemy,
        FamilyCityRules.TypeFoundry,
    };

    /// <summary>2e ville : mare magique, hôtel de brique, marché
    /// (les 8 types sont répartis sur 2 villes, 5 slots max chacune).</summary>
    private static readonly int[] Set2Types =
    {
        FamilyCityRules.TypePool, 2, 10,
    };

    public static void Register(GmCommandService gm, ZoneServices s)
    {
        gm.RegisterAsync("hall_setup", (ctx, args) => SetupAsync(ctx, s, args.Select(a => int.TryParse(a, out var fid) ? fid : 0).FirstOrDefault(x => x > 0),
            args.Any(a => a.Equals("reset",
                StringComparison.OrdinalIgnoreCase)),
            args.Any(a => a.Equals("rebuild",
                StringComparison.OrdinalIgnoreCase)),
            args.Any(a => a.Equals("set2",
                StringComparison.OrdinalIgnoreCase))));
    }

    private static async Task<string?> SetupAsync(
        GmCommandService.Context ctx, ZoneServices s, int familyArg, bool reset,
        bool rebuild = false, bool set2 = false)
    {
        if (s.Social is null) return "[GM] Social absent";
        await s.Guilds.RefreshHouseOpensAsync();
        GuildService.Guild? guild = null;
        if (ctx.State is ZoneEntryFlow.IGmCharRuntime rt
            && rt.VisitedFamilyInstanceId != 0)
            guild = s.Guilds.GetGuild(rt.VisitedFamilyInstanceId);
        if (familyArg > 0) guild = s.Guilds.GetGuild(familyArg);
        guild ??= s.Guilds.GetGuildOf(ctx.CharId);
        if (guild is null) return "[GM] pas de guilde — //create_guild";
        if (guild.HouseOpen == 0)
            return $"[GM] famille #{guild.Id} sans hall — //open_hall";
        var report = await PopulateAsync(s, guild.Id, guild.Level,
            guild.HouseOpen, includeStationNpcs: true, reset, rebuild, set2);
        return $"[GM] hall #{guild.Id} '{guild.Name}' (node " +
            $"{guild.HouseOpen}, niveau {guild.Level}) : {report} — " +
            "ressortir/rentrer dans la ville (G → Ville guilde)";
    }

    /// <summary>Complète le conteneur 0 : bâtiments manquants (si
    /// <paramref name="includeStationNpcs"/>), niveau des bâtiments,
    /// housekeepers, PNJ de service. Retourne un compte rendu court.
    /// Les housekeepers seuls (includeStationNpcs=false) sont
    /// l'auto-réparation appelée à l'entrée du hall.</summary>
    internal static async Task<string> PopulateAsync(ZoneServices s,
        int familyId, int guildLevel, short node,
        bool includeStationNpcs, bool reset, bool rebuild = false,
        bool set2 = false)
    {
        var social = s.Social!;
        var gd = s.GameData;
        var scene = s.Scenes.GetScene($"S{node:D3}");
        var rows = await social.GetFamilyBuildingsAsync(familyId, 0);
        var added = new List<string>();

        if (rebuild)
        {
            // Repart de zéro : supprime bâtiments (0x29) et PNJ (0x2a/0x52)
            // hors manoir/slot sacré — pour re-poser au niveau max.
            foreach (var r in rows.Where(r => r.Loc < 50
                         && r.Loc != FamilyHallRules.HolyLoc))
                await social.DeleteFamilyBuildingItemAsync(familyId, 0,
                    r.Loc);
            rows = await social.GetFamilyBuildingsAsync(familyId, 0);
        }
        if (reset)
        {
            foreach (var r in rows.Where(r => r.ItemType is 0x2a or 0x52
                         && r.Loc != FamilyHallRules.HolyLoc
                         && r.Loc < 50
                         && r.Loc != FamilyHallRules.GroupBase(r.Loc)))
                await social.DeleteFamilyBuildingItemAsync(familyId, 0,
                    r.Loc);
            rows = await social.GetFamilyBuildingsAsync(familyId, 0);
        }

        if (includeStationNpcs)
        {
            // Bâtiments manquants : échelon dont le niveau = niveau de
            // famille (comme ResolveBuildingPut pour les types posés).
            var free = FamilyCityRules.BuildingBases
                .Where(b => !rows.Any(r => r.Loc == b)).ToList();
            foreach (var type in set2 ? Set2Types : DefaultTypes)
            {
                if (free.Count == 0) break;
                if (rows.Any(r => r.ItemType == 0x29
                        && gd.Buildings.TryGetValue(r.Tid, out var d)
                        && d.Type == type)) continue;
                var pick = gd.Buildings
                    .Where(kv => kv.Value.Type == type
                        && kv.Value.Level <= guildLevel)
                    .OrderByDescending(kv => kv.Value.Level)
                    .ThenBy(kv => kv.Key)
                    .Select(kv => (kv.Key, kv.Value)).FirstOrDefault();
                if (pick.Value is null) continue;
                var loc = free[0]; free.RemoveAt(0);
                // Contrat d'origine (kind 0x29 dont col35 = tid de la
                // chaîne) — affichage seulement.
                var root = pick.Key;
                while (gd.Buildings.TryGetValue(root, out var rd)
                       && rd.PrevTid != 0) root = rd.PrevTid;
                var contract = gd.Items.FirstOrDefault(i =>
                    i.Kind == 0x29 && i.FamilyBuildingTid == root);
                await social.InsertFamilyBuildingItemAsync(
                    new FamilyBuildingItem
                    {
                        ItemTid = (short)(contract?.Id ?? 0),
                        Tid = pick.Key, ItemType = 0x29,
                        Durability = (short)Math.Min(
                            pick.Value.MaxDurability, 0x7fff),
                        ContainerIndex = 0, Loc = loc,
                        Level = (short)pick.Value.Level,
                    }, familyId);
                added.Add($"bâtiment {pick.Value.Code}@{loc}");
            }
            rows = await social.GetFamilyBuildingsAsync(familyId, 0);

            // Niveau de ligne = niveau du template (les anciennes
            // lignes GM étaient à 1 → cap NPC 0 et 0x392 au travail).
            foreach (var r in rows.Where(r => r.ItemType == 0x29))
                if (gd.Buildings.TryGetValue(r.Tid, out var d)
                    && r.Level < d.Level)
                {
                    await social.SetFamilyBuildingLevelAsync(familyId,
                        r.Loc, (short)d.Level);
                    r.Level = (short)d.Level;
                    added.Add($"niveau@{r.Loc}={d.Level}");
                }
        }

        var c0 = rows.ToDictionary(r => r.Loc);
        GameDataService.BuildingDef? GetDef(int tid) =>
            gd.Buildings.TryGetValue(tid, out var d) ? d : null;
        var plan = FamilyCityRules.PlanPopulate(c0, GetDef,
            itemTid => gd.Items.FirstOrDefault(i => i.Id == itemTid)
                ?.FamilyNpcTid ?? 0,
            npc => gd.Items.FirstOrDefault(i =>
                i.Kind is 0x2a or 0x52 && i.FamilyNpcTid == npc)?.Id
                ?? GenericNpcContractItem);
        foreach (var p in plan)
        {
            var isHousekeeper = p.Loc % 10 == 1;
            if (!includeStationNpcs && !isHousekeeper) continue;
            var baseLoc = FamilyHallRules.GroupBase(p.Loc);
            var code = c0.TryGetValue(baseLoc, out var bi)
                       && gd.Buildings.TryGetValue(bi.Tid, out var bd)
                ? bd.Code : null;
            var pos = scene != null && code != null
                ? ResolveSlot(scene, gd, code, p.Loc) : null;
            if (pos == null)
            {
                EdenLog.Write($"[FamilyHall] hall_setup : slot {p.Loc} " +
                    $"(npc {p.Tid}) sans position native — ignoré");
                continue;
            }
            var npcDef = gd.Npcs.FirstOrDefault(n => n.Id == p.Tid);
            if (npcDef is null) continue;
            var contractDef = gd.Items.FirstOrDefault(i =>
                i.Id == p.ItemTid);
            await social.InsertFamilyBuildingItemAsync(
                new FamilyBuildingItem
                {
                    ItemTid = (short)p.ItemTid, Tid = p.Tid,
                    ItemType = 0x2a,
                    Durability = (short)Math.Min(
                        contractDef?.MaxDurability ?? 0, 0x7fff),
                    ContainerIndex = 0, Loc = p.Loc,
                    X = pos.Value.X, Y = pos.Value.Y,
                    FaceDir = pos.Value.Facing,
                    Level = (short)npcDef.Level, DueDate = 0,
                }, familyId);
            added.Add($"{(isHousekeeper ? "housekeeper" : "pnj")} " +
                $"{p.Tid}@{p.Loc}");
        }
        EdenLog.Write($"[FamilyHall] hall_setup fam={familyId} " +
            $"node={node} ajouts=[{string.Join(", ", added)}]");
        return added.Count == 0 ? "rien à ajouter"
            : $"{added.Count} ajout(s) [{string.Join(", ", added)}]";
    }

    /// <summary>Item « contrat NPC » générique de test (S_Item 39049)
    /// quand aucun contrat dédié n'existe dans les données.</summary>
    private const int GenericNpcContractItem = 39049;

    /// <summary>Position native d'un slot — mêmes formules que
    /// <c>ZoneEntryFlow.ResolveHallSlot</c> (statique ici pour les
    /// GM).</summary>
    internal static (float X, float Y, float Z, float Facing)?
        ResolveSlot(SceneParser.Scene scene, GameDataService gd,
            string houseCode, short loc)
    {
        if (!scene.PositionGroups.TryGetValue(
                FamilyHallRules.PositionArea(loc), out var pg)
            || pg.Count == 0) return null;
        var b = pg[0];
        if (!gd.HouseLayouts.TryGetValue(houseCode, out var offsets))
            return null;
        return FamilyHallRules.ResolveNpcPosition(b.X, b.Y, b.Z,
            b.Facing, loc, offsets);
    }
}
