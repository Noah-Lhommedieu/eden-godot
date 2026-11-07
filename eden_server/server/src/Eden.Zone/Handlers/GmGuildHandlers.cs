using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Zone.Handlers;

/// <summary>
/// Commandes GM de guilde/hall de famille — noms natifs
/// <c>S_RootCmds</c> réutilisés quand ils existent
/// (<c>create_family</c>, <c>set_family_level</c>,
/// <c>disband_family</c>) ; les autres sont des ajouts maison pour
/// peupler un hall de test sans passer par les blueprints
/// (<c>give_building</c>, <c>building_level</c>, <c>place_npc</c>,
/// <c>replace_npc</c>, <c>open_hall</c>).
///
/// Persistance : <c>family</c> (via <see cref="SocialRepository"/>)
/// et <c>family_building_container</c> — item_type 0x29 = bâtiment
/// sur une base de groupe (loc 0/10/20/30/40, tid = clé S_Buliding),
/// 0x2a/0x52 = NPC/déco (loc non-base, tid = template S_Npc,
/// x/y/face_dir). Source de preuve :
/// <c>CFamilyDBO::LoadFamilyBuilding</c> (mission @0x817c2b0),
/// <c>SFamily::InitFamilyNpc</c> (zone @0x816f5d0 : le NPC naît de
/// l'item+0x80 = tid), <c>CheckItemLoc</c> @0x8254d40.
///
/// Chemin natif de création quand le monde est connecté : ZW 34
/// <c>FamilyCreation</c> (push 32 → cache + 346 au client), puis
/// commande texte ZW 636 <c>gm_family_setup</c> qui rejoue
/// niveau/house_open/trésorerie côté monde (bypass des gates natifs
/// d'InstanceCreate : niveau ≥3, trésorerie, 30 membres).
/// </summary>
public static class GmGuildHandlers
{
    /// <summary>Blueprint item (kind 0x29) par code de modèle de
    /// maison — S_Item 35071-35086 (preuve données : 14 blueprints,
    /// voir docs/generated/GUILD.md). item_tid n'est qu'affichage :
    /// la résolution se fait sur tid (clé S_Buliding).</summary>
    private static readonly Dictionary<string, int> BlueprintItems =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["H00101"] = 35071, ["H00301"] = 35073,
            ["H00401"] = 35075, ["H00501"] = 35076,
            ["H00601"] = 35077, ["H00701"] = 35078,
            ["H00801"] = 35079, ["H00901"] = 35080,
            ["H01001"] = 35081, ["H01101"] = 35082,
            ["H01201"] = 35083, ["H01301"] = 35084,
            ["H01401"] = 35085, ["H01501"] = 35086,
        };

    /// <summary>Item « contrat NPC » générique de test
    /// (S_Item 39049 測試用NPC時效合約1M, kind 0x2a) — utilisé comme
    /// item_tid quand aucun contrat dédié ne référence le NPC
    /// (même convention que les données de test des guildes
    /// 900001/900002).</summary>
    private const int GenericNpcContractItem = 39049;

    /// <summary>Bases de groupe du conteneur 0 — emplacements des
    /// bâtiments (CheckItemLoc @0x8254d40 : kind 0x29 → base).</summary>
    private static readonly short[] BuildingBases = { 0, 10, 20, 30, 40 };

    /// <summary>Nodes de hall de famille (type 4) — 501/502 natifs
    /// (<c>CFamily::InstanceCreate</c> : type1→501, type0→502).</summary>
    private static readonly short[] HallNodes = { 501, 502 };

    /// <summary>Enregistre les commandes GM de guilde — appelé une
    /// fois au démarrage de la zone (Program.cs, après
    /// <see cref="GmHandlers.Register"/>).</summary>
    public static void Register(GmCommandService gm, ZoneServices s)
    {
        // create_guild <nom> [niveau] [node] — alias du nom natif
        // create_family (S_RootCmds 34, access 4). Crée la guilde du
        // perso (chef), la monte au niveau demandé et ouvre le hall.
        gm.RegisterAsync("create_guild",
            (ctx, args) => CreateGuildAsync(ctx, s, args));
        gm.RegisterAsync("create_family",
            (ctx, args) => CreateGuildAsync(ctx, s, args));

        // guild_level <n> | guild_level <familyId> <n> — alias du
        // natif set_family_level (S_RootCmds 144). Sans familyId, la
        // cible est le hall visité, sinon la guilde du perso.
        gm.RegisterAsync("guild_level",
            (ctx, args) => SetGuildLevelAsync(ctx, s, args));
        gm.RegisterAsync("set_family_level",
            (ctx, args) => SetGuildLevelAsync(ctx, s, args));

        // open_hall [node 501|502] — pose family.house_open sans les
        // gates natifs (création d'instance GM).
        gm.RegisterAsync("open_hall", async (ctx, args) =>
        {
            var guild = await ResolveTargetGuildAsync(ctx, s, args, 0);
            if (guild.guild is null) return guild.error;
            var node = args.Length > 0 && short.TryParse(args[^1],
                    out var n) && HallNodes.Contains(n) ? n
                : FirstFreeHallNode(s);
            if (node == 0)
                return "[GM] aucun node de hall libre (501/502 pris)";
            await s.Social!.SetGuildHouseOpenAsync(guild.guild.Id, node);
            guild.guild.HouseOpen = node;
            await PushHouseOpenToWorldAsync(s, guild.guild.Id, node,
                ctx.Token);
            return $"[GM] hall famille #{guild.guild.Id} " +
                $"'{guild.guild.Name}' → node {node}";
        });

        // building_level <loc> <n> | <familyId> <loc> <n> — niveau du
        // bâtiment au slot loc (CFamilyItem+0x98 → cap NPCs).
        gm.RegisterAsync("building_level",
            (ctx, args) => SetBuildingLevelAsync(ctx, s, args));

        // give_building <buildingTid> — pose un bâtiment S_Buliding
        // sur la première base de groupe libre du conteneur 0.
        gm.RegisterAsync("give_building",
            (ctx, args) => GiveBuildingAsync(ctx, s, args));

        // place_npc <npcId> — pose un NPC S_Npc à la position du GM
        // dans le hall visité (fallback : guilde du perso).
        gm.RegisterAsync("place_npc",
            (ctx, args) => PlaceNpcAsync(ctx, s, args, replace: false));
        // replace_npc <npcId> — remplace le NPC le plus proche du GM.
        gm.RegisterAsync("replace_npc",
            (ctx, args) => PlaceNpcAsync(ctx, s, args, replace: true));

        // disband_family — nom natif (S_RootCmds 72) : dissolve la
        // guilde du perso (delete_time + sortie des membres).
        gm.RegisterAsync("disband_family", async (ctx, args) =>
        {
            var guild = s.Guilds.GetGuildOf(ctx.CharId);
            if (guild is null || s.Social is null)
                return "[GM] pas de guilde (ou Social absent)";
            var ids = guild.Members.Select(m => m.CharacterId).ToList();
            await s.Social.DisbandGuildAsync(guild.Id);
            foreach (var id in s.Guilds.ApplyRemoveFamily(guild.Id))
            {
                var sess = s.Sessions.FindByCharacter(id);
                if (sess is null) continue;
                await sess.SendAsync(new CNC_CZ_ClientChangeFamily
                {
                    Field1 = id, Field2 = 0, Field3 = 0,
                    Field4 = "", Field5 = -1,
                    Field6 = -1,
                    Field7 = (byte)s.Guilds.GetFamilyRevision(id),
                }, ctx.Token);
            }
            return $"[GM] famille #{guild.Id} dissoute " +
                $"({ids.Count} membres sortis)";
        });
    }

    // ---------------------------------------------------------------
    // create_guild
    // ---------------------------------------------------------------

    /// <summary>Crée la guilde du perso — chemin monde quand le lien
    /// ZW est connecté (FamilyService du monde reste cohérent : les
    /// ops NCID 9 passent par lui), sinon direct DB + cache zone.</summary>
    private static async Task<string?> CreateGuildAsync(
        GmCommandService.Context ctx, ZoneServices s, string[] args)
    {
        // Args : nom (tokens non numériques de tête), puis [niveau]
        // [node] numériques en queue.
        var nameEnd = args.Length;
        var level = 10;
        short node = 0;
        if (nameEnd > 0 && short.TryParse(args[nameEnd - 1], out var n2)
            && HallNodes.Contains(n2)) { node = n2; nameEnd--; }
        if (nameEnd > 0 && int.TryParse(args[nameEnd - 1], out var lv)
            && lv is >= 1 and <= 10) { level = lv; nameEnd--; }
        if (nameEnd < 1)
            return "usage: //create_guild <nom> [niveau 1-12] [node 501|502]";
        var name = string.Join(' ', args[..nameEnd]);
        if (s.Guilds.GetGuildOf(ctx.CharId) is { } cur)
            return $"[GM] déjà dans la guilde #{cur.Id} '{cur.Name}'";
        if (s.Guilds.AllGuilds().Any(g => string.Equals(g.Name, name,
                StringComparison.OrdinalIgnoreCase)))
            return $"[GM] nom '{name}' déjà pris";
        if (node == 0) node = FirstFreeHallNode(s);
        if (node == 0)
            return "[GM] aucun node de hall libre (501/502 pris)";

        if (s.World is { Connected: true } w)
        {
            // Chemin natif : ZW 34 → monde crée + push 32 (cache zone
            // + 346 client automatiques), puis ZW 636 gm_family_setup
            // pour niveau/house_open/trésorerie (bypass gates natifs).
            await w.SendFamilyCreationAsync(ctx.CharId, name, ctx.Token);
            await w.SendTextCommandAsync(
                $"gm_family_setup {ctx.CharId} {level} {node} " +
                $"{GuildService.MaxTreasuryValue}", ctx.Token);
            return $"[GM] famille '{name}' demandée au monde " +
                $"(lv{level}, hall {node})";
        }

        // Hors-ligne : INSERT direct + cache zone + 346 client.
        if (s.Social is null)
            return "[GM] monde déconnecté et Social absent";
        var ch = await s.Characters.GetCharacterByIdAsync(ctx.CharId);
        var id = await s.Social.CreateGuildAsync(name, ctx.CharId);
        await s.Social.UpdateGuildAsync(id, level, 0,
            GuildService.MaxTreasuryValue, -1);
        await s.Social.SetGuildHouseOpenAsync(id, node);
        s.Guilds.ApplyNewFamily(id, name, ctx.CharId,
            ch?.GivenName ?? "");
        if (s.Guilds.GetGuild(id) is { } g)
        {
            g.Level = level; g.HouseOpen = node;
            g.Treasury = GuildService.MaxTreasuryValue;
        }
        await ctx.Session.SendAsync(new CNC_CZ_ClientChangeFamily
        {
            Field1 = ctx.CharId, Field2 = id, Field3 = 0,
            Field4 = name, Field5 = 0, Field6 = -1,
            Field7 = (byte)s.Guilds.GetFamilyRevision(ctx.CharId),
        }, ctx.Token);
        return $"[GM] famille #{id} '{name}' créée " +
            $"(lv{level}, hall {node})";
    }

    // ---------------------------------------------------------------
    // guild_level / building_level
    // ---------------------------------------------------------------

    /// <summary>Set le niveau d'une guilde — DB + cache zone +
    /// commande texte monde <c>family_set_level</c> (cache monde).</summary>
    private static async Task<string?> SetGuildLevelAsync(
        GmCommandService.Context ctx, ZoneServices s, string[] args)
    {
        int familyId, level;
        if (args.Length >= 2 && int.TryParse(args[0], out familyId)
            && int.TryParse(args[1], out level))
        { }
        else if (args.Length >= 1 && int.TryParse(args[0], out level))
            familyId = 0;
        else
            return "usage: //guild_level [familyId] <niveau 1-12>";
        level = Math.Clamp(level, 1, 12);
        var t = await ResolveTargetGuildAsync(ctx, s,
            familyId > 0 ? [familyId.ToString()] : [], 0);
        if (t.guild is null) return t.error;
        var g = t.guild;
        if (s.Social is null) return "[GM] Social absent";
        await s.Social.UpdateGuildAsync(g.Id, level, g.Experience,
            g.Treasury, g.Emblem);
        g.Level = level;
        if (s.World is { Connected: true } w)
            await w.SendTextCommandAsync(
                $"family_set_level {g.Id} {level}", ctx.Token);
        return $"[GM] famille #{g.Id} '{g.Name}' → niveau {level}";
    }

    /// <summary>Set le niveau d'un bâtiment posé — colonne
    /// <c>family_building_container.level</c> (borne du cap NPCs
    /// CheckNpcSizeLimit : index level-1 des tables natifs).</summary>
    private static async Task<string?> SetBuildingLevelAsync(
        GmCommandService.Context ctx, ZoneServices s, string[] args)
    {
        // <loc> <n> sur la cible résolue, ou <familyId> <loc> <n>.
        int familyId = 0, ai = 0;
        if (args.Length >= 3 && int.TryParse(args[0], out familyId))
            ai = 1;
        if (args.Length < ai + 2
            || !short.TryParse(args[ai], out var loc)
            || !short.TryParse(args[ai + 1], out var lv))
            return "usage: //building_level [familyId] <loc> <niveau 1-13>";
        lv = (short)Math.Clamp((int)lv, 1, 13);
        var t = await ResolveTargetGuildAsync(ctx, s,
            familyId > 0 ? [familyId.ToString()] : [], 0);
        if (t.guild is null) return t.error;
        if (s.Social is null) return "[GM] Social absent";
        var rows = await s.Social.GetFamilyBuildingsAsync(t.guild.Id, 0);
        var row = rows.FirstOrDefault(r => r.Loc == loc);
        if (row is null)
            return $"[GM] rien au loc {loc} — " +
                $"slots : {string.Join(',', rows.Select(r => r.Loc))}";
        await s.Social.SetFamilyBuildingLevelAsync(t.guild.Id, loc, lv);
        return $"[GM] famille #{t.guild.Id} loc {loc} " +
            $"(tid {row.Tid}) → niveau {lv}";
    }

    // ---------------------------------------------------------------
    // give_building
    // ---------------------------------------------------------------

    /// <summary>Pose un bâtiment — ligne 0x29 sur la première base de
    /// groupe libre (0/10/20/30/40), tid = clé S_Buliding, item_tid =
    /// blueprint item du modèle (35071-35086).</summary>
    private static async Task<string?> GiveBuildingAsync(
        GmCommandService.Context ctx, ZoneServices s, string[] args)
    {
        var familyId = 0;
        var ai = 0;
        if (args.Length >= 2 && int.TryParse(args[0], out familyId))
            ai = 1;
        if (args.Length < ai + 1
            || !int.TryParse(args[ai], out var tid))
            return "usage: //give_building [familyId] <buildingTid>";
        if (!s.GameData.Buildings.TryGetValue(tid, out var def))
            return $"[GM] bâtiment {tid} hors S_Buliding " +
                "(//list_buildings dans GUILD.md)";
        var t = await ResolveTargetGuildAsync(ctx, s,
            familyId > 0 ? [familyId.ToString()] : [], 0);
        if (t.guild is null) return t.error;
        if (s.Social is null) return "[GM] Social absent";
        if (t.guild.HouseOpen == 0)
            return $"[GM] famille #{t.guild.Id} sans hall " +
                "— //open_hall d'abord";
        var rows = await s.Social.GetFamilyBuildingsAsync(t.guild.Id, 0);
        var used = rows.Select(r => r.Loc).ToHashSet();
        var loc = BuildingBases.FirstOrDefault(b => !used.Contains(b),
            (short)-1);
        if (loc < 0)
            return "[GM] 5 bases de groupe occupées — hall plein";
        BlueprintItems.TryGetValue(def.Code, out var bp);
        await s.Social.InsertFamilyBuildingItemAsync(
            new FamilyBuildingItem
            {
                ItemTid = (short)bp, Tid = tid, ItemType = 0x29,
                Durability = (short)Math.Min(def.MaxDurability, 0x7fff),
                ContainerIndex = 0, Loc = loc, Level = 1,
            }, t.guild.Id);
        return $"[GM] bâtiment {tid} ({def.Code} type {def.Type}) " +
            $"→ famille #{t.guild.Id} loc {loc}";
    }

    // ---------------------------------------------------------------
    // place_npc / replace_npc
    // ---------------------------------------------------------------

    /// <summary>Pose (ou remplace) un NPC de hall — ligne 0x2a dont
    /// le tid = template S_Npc (BornNPC natif lit item+0x80), à la
    /// position du GM pour place_npc, au slot du NPC le plus proche
    /// pour replace_npc. Injecte/échange l'entité rejouée 298 de la
    /// vue pour un effet immédiat.</summary>
    private static async Task<string?> PlaceNpcAsync(
        GmCommandService.Context ctx, ZoneServices s, string[] args,
        bool replace)
    {
        var familyId = 0;
        var ai = 0;
        if (args.Length >= 2 && int.TryParse(args[0], out familyId))
            ai = 1;
        if (args.Length < ai + 1
            || !int.TryParse(args[ai], out var npcTid))
            return $"usage: //{(replace ? "replace_npc" : "place_npc")}" +
                " [familyId] <npcId>";
        var npc = s.GameData.Npcs.FirstOrDefault(n => n.Id == npcTid);
        if (npc is null)
            return $"[GM] NPC {npcTid} hors S_Npc " +
                "(voir docs/generated/GUILD.md)";
        var t = await ResolveTargetGuildAsync(ctx, s,
            familyId > 0 ? [familyId.ToString()] : [], 0);
        if (t.guild is null) return t.error;
        if (s.Social is null) return "[GM] Social absent";
        if (ctx.State is not ZoneEntryFlow.IGmCharRuntime rt)
            return "[GM] état perso indisponible";

        // Contrat dédié (S_Item kind 0x2a/0x52 dont col35 = npcTid) —
        // sinon le contrat générique 39049 comme les données de test.
        var contract = s.GameData.Items.FirstOrDefault(i =>
            i.Kind is 0x2a or 0x52 && i.FamilyNpcTid == npcTid);
        var itemTid = contract?.Id ?? GenericNpcContractItem;
        var rows = await s.Social.GetFamilyBuildingsAsync(t.guild.Id, 0);

        if (replace)
        {
            // NPC posé le plus proche du GM (x/y persistés).
            var near = rows.Where(r => r.ItemType is 0x2a or 0x52)
                .OrderBy(r => (r.X - rt.PosX) * (r.X - rt.PosX)
                    + (r.Y - rt.PosZ) * (r.Y - rt.PosZ))
                .FirstOrDefault();
            if (near is null)
                return $"[GM] aucun NPC posé famille #{t.guild.Id}";
            await s.Social.UpdateFamilyBuildingItemAsync(
                t.guild.Id, near.Loc, itemTid, npcTid);
            // Échange visuel : 269 sur l'ancien handle (s'il était en
            // vue) puis injection du nouveau NPC à la même position.
            var old = s.HallNpcRemove?.Invoke(ctx.State,
                (float)near.X, (float)near.Y, 4f) ?? 0;
            if (old != 0)
                await ctx.Session.SendPacketAsync(269,
                    BitConverter.GetBytes(old), ctx.Token);
            var h2 = s.HallNpcInject?.Invoke(ctx.State, npcTid,
                (float)near.X, (float)near.Y, 0,
                (float)near.FaceDir) ?? 0;
            return $"[GM] loc {near.Loc} : NPC {near.Tid} → {npcTid} " +
                $"({npc.Name})" + (h2 != 0 ? "" : " — relog hall");
        }

        // Premier slot libre 1..50 hors bases de groupe occupées —
        // le natif exige une base 0x29 pour les slots NPC, le GM
        // bypass (comme les données de test à locs 10-22).
        var usedLocs = rows.Select(r => r.Loc).ToHashSet();
        var freeLoc = Enumerable.Range(1, 50)
            .Select(i => (short)i)
            .FirstOrDefault(l => !usedLocs.Contains(l), (short)-1);
        if (freeLoc < 0)
            return "[GM] conteneur plein (50 slots NPC)";
        await s.Social.InsertFamilyBuildingItemAsync(
            new FamilyBuildingItem
            {
                ItemTid = (short)itemTid, Tid = npcTid,
                ItemType = 0x2a, ContainerIndex = 0, Loc = freeLoc,
                X = rt.PosX, Y = rt.PosZ, FaceDir = rt.Facing,
            }, t.guild.Id);
        var h = s.HallNpcInject?.Invoke(ctx.State, npcTid,
            rt.PosX, rt.PosZ, 0, rt.Facing) ?? 0;
        return $"[GM] NPC {npcTid} ({npc.Name}) → loc {freeLoc} " +
            $"@ ({rt.PosX:F0},{rt.PosZ:F0})" +
            (h != 0 ? "" : " — relog hall pour le voir");
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>Guilde cible d'une commande GM : familyId explicite si
    /// passé en premier arg, sinon le hall visité (char+0xaac), sinon
    /// la guilde du perso. Resync DB d'abord — un UPDATE direct
    /// (outillage) est sinon invisible du cache (bug live connu).</summary>
    private static async Task<(GuildService.Guild? guild, string? error)>
        ResolveTargetGuildAsync(GmCommandService.Context ctx,
            ZoneServices s, string[] args, int argIndex)
    {
        await s.Guilds.RefreshHouseOpensAsync();
        if (args.Length > argIndex &&
            int.TryParse(args[argIndex], out var fid) && fid > 0)
        {
            var byId = s.Guilds.GetGuild(fid);
            return byId is null
                ? (null, $"[GM] famille #{fid} introuvable")
                : (byId, null);
        }
        if (ctx.State is ZoneEntryFlow.IGmCharRuntime rt
            && rt.VisitedFamilyInstanceId != 0
            && s.Guilds.GetGuild(rt.VisitedFamilyInstanceId) is { } vis)
            return (vis, null);
        var own = s.Guilds.GetGuildOf(ctx.CharId);
        return own is null
            ? (null, "[GM] pas de guilde — //create_guild <nom>")
            : (own, null);
    }

    /// <summary>Premier node de hall libre (501 préféré) — miroir du
    /// choix natif <c>CFamily::InstanceCreate</c> (type1→501,
    /// type0→502) sans le gate des 49 villes.</summary>
    private static short FirstFreeHallNode(ZoneServices s)
    {
        var taken = s.Guilds.AllGuilds()
            .Select(g => g.HouseOpen).ToHashSet();
        return HallNodes.FirstOrDefault(n => !taken.Contains(n),
            (short)0);
    }

    /// <summary>Relaie le house_open au monde — même push que
    /// <c>family_instance_create</c> : le monde met à jour son cache
    /// famille et rediffuse ZW 35 aux zones.</summary>
    private static async Task PushHouseOpenToWorldAsync(
        ZoneServices s, int familyId, short node,
        CancellationToken token)
    {
        if (s.World is { Connected: true } w)
            await w.SendTextCommandAsync(
                $"family_set_house_open {familyId} {node}", token);
    }
}
