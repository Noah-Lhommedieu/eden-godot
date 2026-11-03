using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone.Handlers;

/// <summary>
/// Commandes GM de zone — les noms natifs de RootCmds.ini sont
/// réutilisés quand ils existent (<c>clone_item</c>, <c>clone_items</c>,
/// <c>gain_gold</c>, <c>gain_aa_point</c>, <c>set_level</c>,
/// <c>gain_exp</c>) ; <c>gain_ep_point</c> est un ajout maison (la
/// monnaie EP/bonus n'a pas de commande native équivalente).
/// Chaque handler applique l'effet serveur + rejoue les paquets
/// d'effet natifs (274 PutItem, 280 attr 0x02 gold, texte 0x89).
/// </summary>
public static class GmHandlers
{
    /// <summary>Enregistre les handlers async sur le service GM —
    /// appelé une fois au démarrage de la zone.</summary>
    public static void Register(GmCommandService gm, ZoneServices s)
    {
        // clone_item <itemId> — crée 1 item dans l'inventaire
        // (AddCloneItemWorld/PutIntoContainer natifs) + 274 + texte.
        gm.RegisterAsync("clone_item",
            (ctx, args) => GiveItemAsync(ctx, s, args, 1));
        // clone_items <itemId> <count>
        gm.RegisterAsync("clone_items",
            (ctx, args) => GiveItemAsync(ctx, s, args, 0));
        // clone_item_to <itemId> <count> — même effet chez nous.
        gm.RegisterAsync("clone_item_to",
            (ctx, args) => GiveItemAsync(ctx, s, args, 0));

        // gain_gold <n> — or NORMAL : `TC_GainGold` @0x083db800 →
        // `CCharacter::GainGold` @0x842a7f0 (champ char+0x4b0, cap
        // 999999999) → 280 ClientUpdateAttr attr 2 = nouveau total.
        // Bug live corrigé : l'ancien code créditait `forever_gold`
        // (monnaie boutique, attr 3) et renvoyait ce total en attr 2
        // sans toucher `st.Gold` — l'or affiché était faux et le
        // prochain achat écrasait le gain en DB.
        gm.RegisterAsync("gain_gold", async (ctx, args) =>
        {
            if (args.Length < 1 || !long.TryParse(args[0], out var amount))
                return "usage: //gain_gold <montant>";
            var gold = await s.Characters.AddGoldAsync(
                ctx.CharId, amount);
            if (ctx.State is ZoneEntryFlow.IGmCharRuntime rs)
                rs.Gold = (int)Math.Min(gold, int.MaxValue);
            // 280 attr 0x02 = gold (GainGold → ClientUpdateAttr natif).
            await SendGoldAttrAsync(ctx.Session, ctx.CharId, gold,
                ctx.Token);
            return $"[GM] gold += {amount} → {gold}";
        });

        // gain_forever_gold <n> — commande native S_RootCmds 238 :
        // `TC_GainForeverGold` @0x083ca6f0 → `GainForeverGold`
        // @0x840c3f0 (champ char+0x4b4) → 280 attr **3** = total.
        gm.RegisterAsync("gain_forever_gold", async (ctx, args) =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var amount))
                return "usage: //gain_forever_gold <montant>";
            var total = await s.Characters.AddForeverGoldAsync(
                ctx.CharId, amount);
            // 280 attr 0x03 = forever gold (désasm : s16 = 3).
            await SendAttrAsync(ctx.Session, ctx.CharId, 0x03,
                (int)total, ctx.Token);
            return $"[GM] forever_gold += {amount} → {total}";
        });

        // date / version / ping / users — commandes natives d'info.
        gm.Register("date", (gm, _) =>
            $"[GM] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        gm.Register("version", (gm, _) =>
            "[GM] Eden port C# (.NET) — build dev");
        gm.Register("ping", (gm, _) => "[GM] pong");
        gm.RegisterAsync("users", (ctx, _) =>
            Task.FromResult<string?>(
                $"[GM] sessions zone : {s.Sessions.All.Count()}"));
        gm.RegisterAsync("allusers", (ctx, _) =>
            Task.FromResult<string?>(
                $"[GM] sessions zone : {s.Sessions.All.Count()}"));

        // gain_aa_point <n> — commande native « /gain aa point »
        // (@0x0888242a) = points de TALENT (advanced ability) :
        // GainAAPoint @0x840c7f0 crédite player_characters.aa_point
        // (cap 120) + émet ClientUpdateAttr attr=0x31.
        gm.RegisterAsync("gain_aa_point", async (ctx, args) =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var amount))
                return "usage: //gain_aa_point <montant>";
            var total = await s.Classes.AddAaPointAsync(ctx.CharId,
                amount);
            if (total < 0) return "[GM] perso introuvable";
            await SendAttrAsync(ctx.Session, ctx.CharId, 0x31, total,
                ctx.Token);
            return $"[GM] aa_point += {amount} → {total}";
        });
        // gain_ap_point <n> — pvalues/AP item mall (ajout maison — pas
        // de commande native AP ; « aa » natif = talent, pas boutique).
        gm.RegisterAsync("gain_ap_point",
            (ctx, args) => GivePointsAsync(ctx, s, args, 1, "AP"));
        // gain_ep_point <n> — bonus/EP (pas de commande native —
        // ajout maison pour tester le item mall EP).
        gm.RegisterAsync("gain_ep_point",
            (ctx, args) => GivePointsAsync(ctx, s, args, 2, "EP"));

        // //limitless — commande JOUEUR (Plan B du design
        // docs/limitless/LIMITLESS_SYSTEM_DESIGN.md §5.1 : tant que
        // le hook d3d9 qui rend la barre/fenêtre du pass n'est pas
        // livré, l'état Limitless s'affiche ici en chat système).
        // Les handlers async bypass le check d'accès ini → accessible
        // à tous les joueurs.
        gm.RegisterAsync("limitless", async (ctx, args) =>
        {
            var svc = s.Limitless;
            if (svc is null) return "[Limitless] service inactif";
            var p = await svc.GetProgressAsync(ctx.CharId);
            var perks = await svc.GetActivePerksAsync(ctx.CharId);
            var list = perks.Count == 0 ? "aucun"
                : string.Join(", ", perks.Select(
                    k => $"{k.Code}={k.Value}"));
            return $"[Limitless] Lv {p.Level} — Exp Ultime {p.Exp}" +
                (p.NextExp < int.MaxValue ? $"/{p.NextExp}" : "") +
                $" — perks: {list}";
        });
        // ll_exp <n> — grant admin d'Exp Ultime (test du pass sans
        // attendre des kills au cap).
        gm.RegisterAsync("ll_exp", async (ctx, args) =>
        {
            var svc = s.Limitless;
            if (svc is null) return "[Limitless] service inactif";
            // Le canal async bypass le check ini — gate explicite :
            // grant de monnaie = admin seulement.
            var who = await s.Characters.GetCharacterByIdAsync(
                ctx.CharId);
            if ((who?.Privilege ?? 0) < 1)
                return "[Limitless] accès refusé";
            if (args.Length < 1 || !int.TryParse(args[0], out var pts))
                return "usage: //ll_exp <points>";
            var g = await svc.AddExpAsync(ctx.CharId, pts);
            // Ré-émission du NCID 2032 : le grant admin ne passe pas
            // par le pipeline de récompense — sans ce push le HUD
            // client ne se rafraîchit qu'à la prochaine entrée.
            await LimitlessEmitter.EmitAsync(ctx.Session, svc,
                ctx.CharId, ctx.Token);
            // 2033 : dédupliqué par révision — réémis seulement si la
            // config des paliers a changé depuis la dernière émission.
            await LimitlessEmitter.EmitPassAsync(ctx.Session, svc,
                ctx.CharId, ctx.Token);
            return $"[Limitless] exp += {pts} → Lv {g.Level} " +
                $"({g.Exp} total)";
        });
        // ll_reload — hot-reload des perks BDD + du .ini des
        // récompenses (limitless_pass.ini) : ajouter/retirer/modifier
        // un buff de palier sans restart. Bump PassRevision → la liste
        // des paliers (2033) est re-poussée à la prochaine émission.
        gm.RegisterAsync("ll_reload", async (ctx, args) =>
        {
            var svc = s.Limitless;
            if (svc is null) return "[Limitless] service inactif";
            var who = await s.Characters.GetCharacterByIdAsync(
                ctx.CharId);
            if ((who?.Privilege ?? 0) < 1)
                return "[Limitless] accès refusé";
            try { await svc.ReloadAsync(); }
            catch (Exception ex)
            { return $"[Limitless] reload KO : {ex.Message}"; }
            await LimitlessEmitter.EmitPassAsync(ctx.Session, svc,
                ctx.CharId, ctx.Token);
            return $"[Limitless] perks+courbe rechargés " +
                $"(rev {svc.PassRevision})";
        });

        // //crusade <join|start|close> <id> — join = équivalent du
        // NCID 259 client (relay « CrusadeJoin » vers la mission,
        // ouvert à tous : les gates guilde/état sont mission-side) ;
        // start/close pilotent le CBattleCrusadeFactory (admin-only).
        gm.RegisterAsync("crusade", async (ctx, args) =>
        {
            if (args.Length < 2 || !int.TryParse(args[1], out var cid))
                return "usage: //crusade <join|start|battle|close> <id 1-4>";
            if (s.Mission is not { Connected: true })
                return "[Crusade] mission déconnectée";
            if (args[0] == "join")
            {
                await s.Mission.SendAgentDbaCommandAsync(
                    (uint)ctx.CharId,
                    $"CrusadeJoin crusade_id= {cid}", ctx.Token);
                return $"[Crusade] join {cid} demandé";
            }
            var who = await s.Characters.GetCharacterByIdAsync(
                ctx.CharId);
            if ((who?.Privilege ?? 0) < 1) return "[Crusade] accès refusé";
            var verb = args[0] == "close" ? "CrusadeClose"
                : args[0] == "battle" ? "CrusadeBattle" : "CrusadeStart";
            await s.Mission.SendAgentDbaCommandAsync(0,
                $"{verb} crusade_id= {cid}", ctx.Token);
            return $"[Crusade] {verb} {cid} envoyé";
        });

        // gain_exp <n> — exp via le pipeline de progression (level-ups
        // et refresh 296 compris).
        gm.RegisterAsync("gain_exp", async (ctx, args) =>
        {
            if (args.Length < 1 || !long.TryParse(args[0], out var exp))
                return "usage: //gain_exp <montant>";
            var ch = await s.Characters.GetCharacterByIdAsync(ctx.CharId);
            if (ch == null) return "[GM] perso introuvable";
            var (lvl, gained) = await s.Progression.GrantExpAsync(
                ctx.CharId, exp, ch.Level, ch.Experience);
            await ProfileStatEmitter.SendForCharacterAsync(
                ctx.Session, s, ctx.CharId, ctx.Token);
            return $"[GM] exp += {exp} → level {lvl}" +
                (gained > 0 ? $" (+{gained} level-up)" : "");
        });

        // set_level <n> — positionne le niveau directement (stats
        // dérivées level×class — le refresh 296 rend le nouveau
        // profil visible).
        gm.RegisterAsync("set_level", async (ctx, args) =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var lvl)
                || lvl < 1)
                return "usage: //set_level <niveau>";
            var max = s.Progression.MaxLevel;
            lvl = Math.Min(lvl, max);
            await s.Characters.UpdateExpAsync(ctx.CharId,
                s.Progression.ExpForLevel(lvl - 1), lvl);
            await ProfileStatEmitter.SendForCharacterAsync(
                ctx.Session, s, ctx.CharId, ctx.Token);
            return $"[GM] level = {lvl}";
        });

        // Tickets des 3 autels (S_Lottery/CheckItemNum natifs) —
        // items S_ItemMall donnés via le pipeline //give standard :
        //   gain_crystal  → 40241 幻化水晶 (autel normal, TicketRequestId)
        //   gain_gem      → 26283 幻化寶石 (autel in-game)
        //   gain_diamond  → 27792 幻化星鑽 (autel diamant)
        // Les versions AP【限定】(44131/37499/37500) restent donnables
        // par //give <id>.
        gm.RegisterAsync("gain_crystal", async (ctx, args) =>
            await GiveItemAsync(ctx, s,
                new[] { "40241", args.ElementAtOrDefault(0) ?? "1" }, 0));
        gm.RegisterAsync("gain_gem", async (ctx, args) =>
            await GiveItemAsync(ctx, s,
                new[] { "26283", args.ElementAtOrDefault(0) ?? "1" }, 0));
        gm.RegisterAsync("gain_diamond", async (ctx, args) =>
            await GiveItemAsync(ctx, s,
                new[] { "27792", args.ElementAtOrDefault(0) ?? "1" }, 0));

        // gain_special_gold <type> <n> — commande GM native
        // "GMGainSpecialCoin Type = %hd Money = %d" (zone rodata
        // 0x87ced94, relayée à la mission via AgentDBACommand) :
        // monnaies de mérites spéciaux 0-5,9-11 → special_coins.coinNN.
        // La mission met à jour + pousse le total (78 via monde).
        gm.RegisterAsync("gain_special_gold", async (ctx, args) =>
        {
            if (args.Length < 2 ||
                !int.TryParse(args[0], out var coinType) ||
                !int.TryParse(args[1], out var money))
                return "usage: //gain_special_gold <type 0-5|9-11> <n>";
            if (coinType is < 0 or > 11 ||
                (coinType >= 6 && coinType <= 8))
                return "[GM] type invalide — natif : 0-5, 9-11";
            if (s.Mission is not { Connected: true } link)
                return "[GM] mission non connectée";
            await link.SendAgentDbaCommandAsync((uint)ctx.CharId,
                $"GMGainSpecialCoin Type = {coinType} Money = {money}");
            return $"[GM] gain_special_gold type={coinType} " +
                $"money={money} → mission";
        });

        // gain_skill_point <n> — points de classe/talents.
        gm.RegisterAsync("gain_skill_point", async (ctx, args) =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var pts))
                return "usage: //gain_skill_point <n>";
            var ch = await s.Characters.GetCharacterByIdAsync(ctx.CharId);
            if (ch == null) return "[GM] perso introuvable";
            await s.Characters.SetSkillPointAsync(ctx.CharId,
                ch.SkillPoints + pts);
            return $"[GM] skill_point += {pts} → {ch.SkillPoints + pts}";
        });

        // give <itemId|nom> [qty] — clone_item avec résolution par
        // nom (le nom S_Item est en Big5 → matcher sur sous-chaîne
        // insensible à la casse).
        gm.RegisterAsync("give", async (ctx, args) =>
        {
            if (args.Length < 1)
                return "usage: //give <itemId|nom> [qty]";
            if (int.TryParse(args[0], out var itemId))
                return await GiveItemAsync(ctx, s,
                    new[] { itemId.ToString(), args.ElementAtOrDefault(1)
                        ?? "1" }, 0);
            // Nom multi-mots : le dernier token numérique = qty, tout
            // le reste = le nom (« Potion of Life 5 »).
            var qty = 1;
            var nameEnd = args.Length;
            if (args.Length > 1 &&
                int.TryParse(args[^1], out var q)) { qty = q; nameEnd--; }
            var name = string.Join(' ', args[..nameEnd]);
            var found = FindItem(s, name);
            if (found == null)
                return $"[GM] aucun item '{name}' — //find_item";
            return await GiveItemAsync(ctx, s,
                new[] { found.Id.ToString(), qty.ToString() }, 0);
        });

        // find_item <mot-clé|préfixe id> — liste les items S_Item
        // correspondants (id + nom) pour retrouver les ids à donner.
        gm.RegisterAsync("find_item", async (ctx, args) =>
        {
            if (args.Length < 1)
                return "usage: //find_item <mot-clé|id>";
            // Le nom recherché = TOUS les tokens joints — les noms
            // d'items ont des espaces (« Potion of Life »), prendre
            // args[0] seul ne trouvait rien (bug live).
            var kw = string.Join(' ', args);
            // Noms anglais T_Item (« Adventurer Alpaca ») en plus des
            // noms Big5 S_Item — les deux tables couvrent les 2 clients.
            string EnglishName(int id) =>
                s.GameData.ItemNames.TryGetValue(id, out var n) ? n : "";
            var words = kw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool Match(ItemParser.ItemDef i)
            {
                var hay = i.Name + ' ' + i.Code + ' ' + EnglishName(i.Id);
                // Tous les mots doivent matcher — « eden amethyst »
                // trouve « Eden Amethyst » alors qu'une recherche de
                // chaîne entière ne matchait rien (bug live).
                return words.All(w => hay.Contains(w,
                    StringComparison.OrdinalIgnoreCase));
            }
            var matches = int.TryParse(kw, out var prefix)
                ? s.GameData.Items.Where(i => i.Id == prefix ||
                    i.Id.ToString().StartsWith(kw)).Take(8).ToList()
                : s.GameData.Items.Where(i => Match(i)).Take(8).ToList();
            if (matches.Count == 0)
                return $"[GM] aucun item pour '{kw}'";
            foreach (var i in matches)
            {
                var en = EnglishName(i.Id);
                await ctx.Session.SendAsync(
                    new CNC_CZ_ClientTextOutput
                    { Field1 = 155, Field2 =
                        $"{i.Id} = {(en.Length > 0 ? en : i.Name)} " +
                        $"({i.Code}) lv{i.RequiredLevel}" },
                    ctx.Token);
            }
            return $"[GM] {matches.Count} résultat(s) '{kw}'";
        });

        // find_spell <mot-clé|id> — id + nom + mask des spellmasters.
        gm.RegisterAsync("find_spell", async (ctx, args) =>
        {
            if (args.Length < 1)
                return "usage: //find_spell <mot-clé|id>";
            var kw = string.Join(' ', args); // noms multi-mots
            var matches = int.TryParse(kw, out var prefix)
                ? s.GameData.Spells.Where(sp => sp.Id == prefix ||
                    sp.Id.ToString().StartsWith(kw)).Take(8).ToList()
                : s.GameData.Spells.Where(sp =>
                    sp.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .Take(8).ToList();
            if (matches.Count == 0)
                return $"[GM] aucun sort pour '{kw}'";
            foreach (var sp in matches)
                await ctx.Session.SendAsync(
                    new CNC_CZ_ClientTextOutput
                    { Field1 = 155, Field2 =
                        $"{sp.Id} = {sp.Name} mask=0x{sp.ClassMask:x} " +
                        $"lv{sp.RequiredLevel} cd={sp.CooldownDs / 10f:F1}s" },
                    ctx.Token);
            return $"[GM] {matches.Count} sort(s) '{kw}'";
        });

        // learn_spell <spellId> — appris persisté (player_spells)
        // — miroir natif InsertPlayerSpell.
        gm.RegisterAsync("learn_spell", async (ctx, args) =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var sm))
                return "usage: //learn_spell <spellId>";
            var known = await s.Classes.GetPlayerSpellsAsync(ctx.CharId);
            if (known.Any(k => (ushort)k.Spell == (ushort)sm))
                return $"[GM] spellmaster {sm} déjà appris";
            await s.Classes.LearnSpellAsync(ctx.CharId, (short)sm);
            s.SpellLevelsDirty?.Invoke(ctx.CharId);
            return $"[GM] spellmaster {sm} appris — re-log pour le refresh 286";
        });

        // list_class — id + nom des classes (pour add_class/
        // change_class — id 1-61 + 100*racial).
        gm.RegisterAsync("list_class", async (ctx, args) =>
        {
            var defs = s.GameData.Classes
                .Where(d => d.RaceIndex == 0)   // ids 1xx = race 0
                .OrderBy(d => d.Id).Take(24).ToList();
            if (defs.Count == 0) return "[GM] table S_Class vide";
            var line = "";
            foreach (var d in defs)
            {
                var cell = $" {d.Id % 100}:{d.Name}";
                if ((line + cell).Length > 70)
                {
                    await ctx.Session.SendAsync(
                        new CNC_CZ_ClientTextOutput
                        { Field1 = 155, Field2 = line }, ctx.Token);
                    line = "";
                }
                line += cell;
            }
            if (line.Length > 0)
                await ctx.Session.SendAsync(
                    new CNC_CZ_ClientTextOutput
                    { Field1 = 155, Field2 = line }, ctx.Token);
            return $"[GM] {defs.Count} classes (race 0) — id = colonne";
        });

        // del_item <sac> <slot> — DELETE natif de la ligne +
        // 275 ClientDropItem (même paire que le drop en jeu).
        // Outil de nettoyage des sacs remplis de clones de test.
        gm.RegisterAsync("del_item", async (ctx, args) =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out var bag) ||
                !int.TryParse(args[1], out var slot))
                return "usage: //del_item <sac 0-4> <slot>";
            var item = await s.InventoryRepo.GetItemAsync(ctx.CharId,
                bag, slot);
            if (item == null)
                return $"[GM] sac {bag} slot {slot} vide";
            var removed = await s.Inventory.RemoveItemAsync(ctx.CharId,
                bag, slot, int.MaxValue);
            if (!removed) return "[GM] suppression échouée";
            await ctx.Session.SendAsync(new CNC_CZ_ClientDropItem
            {
                Field1 = 0,
                Field2 = (short)slot,
                Field3 = (short)bag,
            }, ctx.Token);
            return $"[GM] item {item.ItemId} sac{bag} slot{slot} " +
                "supprimé";
        });

        // clear_bag <sac> — vide tout le conteneur d'un coup
        // (275 par slot vidé) — pour purger un sac rempli de tests.
        gm.RegisterAsync("clear_bag", async (ctx, args) =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var bag))
                return "usage: //clear_bag <sac 0-4>";
            var items = (await s.InventoryRepo.GetLiveItemsAsync(
                    ctx.CharId)).Where(i => i.BagId == bag).ToList();
            foreach (var i in items)
            {
                await s.Inventory.RemoveItemAsync(ctx.CharId, i.BagId,
                    i.Slot, int.MaxValue);
                await ctx.Session.SendAsync(new CNC_CZ_ClientDropItem
                {
                    Field1 = 0,
                    Field2 = (short)i.Slot,
                    Field3 = (short)i.BagId,
                }, ctx.Token);
            }
            return $"[GM] sac {bag} vidé — {items.Count} item(s) " +
                "supprimés";
        });
    }

    /// <summary>Premier item dont le nom Big5, le code ou le nom
    /// anglais T_Item contient TOUS les mots de <paramref name="kw"/>
    /// — matcher //give par nom (noms à espaces + suffixes de rareté
    /// « (Prime) » ; un code exact matche aussi seul).</summary>
    private static ItemParser.ItemDef? FindItem(ZoneServices s, string kw)
    {
        var words = kw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return s.GameData.Items.FirstOrDefault(i =>
            i.Code.Equals(kw, StringComparison.OrdinalIgnoreCase) ||
            words.All(w => (i.Name + ' ' +
                (s.GameData.ItemNames.TryGetValue(i.Id, out var en)
                    ? en : "")).Contains(w,
                StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Donne un item — serial natif via
    /// <c>AddItemAsync</c> (AddCloneItemWorld/PutIntoContainer +
    /// stack merge), 274 avec l'entité relue, notify mission et
    /// texte natif 0x89 « %s x %d ».</summary>
    private static async Task<string?> GiveItemAsync(
        GmCommandService.Context ctx, ZoneServices s, string[] args,
        int fixedCount)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var itemId))
            return $"usage: //clone_item{{s}} <itemId> [count]";
        var qty = fixedCount > 0 ? 1
            : args.Length > 1 && int.TryParse(args[1], out var q)
                ? Math.Max(q, 1) : 1;
        var def = s.GameData.Items.FirstOrDefault(i => i.Id == itemId);
        if (def == null)
            return $"[GM] item {itemId} hors tables S_Item/ItemMall";
        // Certificats de classe (S_Item kind 40) : JAMAIS dans
        // l'inventaire — le natif les stocke dans le vecteur licences
        // +0xaf0 (table player_license). Un cert posé en inventory1
        // était sérialisé comme item normal dans le 312/274 → le
        // client l'affichait en anneau et crashait à la moindre
        // manipulation (bug live /give 33272). GrantLicense natif +
        // écho 509 {s16 id, u8 loc=0} comme CPlayerFactory::GetLicense.
        if (def.Kind == 40)
        {
            for (var i = 0; i < qty; i++)
            {
                var licId = unchecked((short)itemId);
                var already = s.Licenses.StateOf(ctx.CharId)
                    .Owned.Any(o => o.ItemId == licId);
                await s.Licenses.GrantLicenseAsync(ctx.CharId, licId);
                if (!already)
                {
                    await ctx.Session.SendAsync(
                        new CNC_CZ_ClientPlayerLicense
                        {
                            Licenses = { (licId, 0) },  // {s16 id, u8 loc}
                        }, ctx.Token);
                }
            }
            return $"[GM] certificat {itemId} ({def.Name}) → " +
                "player_license (pas en sac — kind 40)";
        }
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var (ok, error, bag, slot) = await s.Inventory.AddItemAsync(
            ctx.CharId, itemId, qty, 0, 0, false, touched);
        if (!ok) return $"[GM] don impossible : {error}";
        // Merge → 291, nouveau slot → 274 (règle native — un 274 sur
        // slot occupé matérialise un clone sur l'existant → crash).
        await NativeItemCodec.EmitItemSlotsAsync(ctx.Session,
            s.InventoryRepo, ctx.CharId, touched, ctx.Token);
        s.NotifyItemChanged(ctx.CharId, itemId);
        // Texte natif 0x89 « %s x %d » — même retour que l'achat
        // boutique/item mall.
        await ctx.Session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = 0x89,
            Field2 = $"{def.Name} x {qty}",
        }, ctx.Token);
        return $"[GM] item {itemId} ×{qty} → bag={bag} slot={slot}";
    }

    /// <summary>Crédite pvalues (AP) ou bonus (EP) sur le compte
    /// edenfr_member du perso — le solde item mall est lu côté monde à
    /// l'ouverture de la boutique, pas de paquet à pousser ici.</summary>
    private static async Task<string?> GivePointsAsync(
        GmCommandService.Context ctx, ZoneServices s, string[] args,
        int currency, string label)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var amount))
            return $"usage: //gain_{label.ToLowerInvariant()}_point <n>";
        if (s.Users == null)
            return "[GM] base edenfr_member non configurée (MEMBER_DB_CONN)";
        var ch = await s.Characters.GetCharacterByIdAsync(ctx.CharId);
        var mid = ch?.AccountName;
        if (string.IsNullOrEmpty(mid))
            return "[GM] compte du perso introuvable";
        var ok = await s.Users.AddItemMallPointsAsync(mid, currency,
            amount, ctx.Token);
        return ok ? $"[GM] {label} += {amount} sur '{mid}'"
                  : $"[GM] crédit {label} impossible (mid '{mid}' absent)";
    }

    /// <summary>Paquet 280 ClientUpdateAttr générique — miroir du
    /// <c>NotifyClient(ClientUpdateAttr)</c> natif
    /// (GainGold/GainAAPoint…).</summary>
    private static Task SendAttrAsync(NetSession session, int charId,
        short attr, int value, CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)charId,           // u32 : handle
            Field2 = attr,                  // s16 : id d'attribut
            Field3 = value,                 // i32 : valeur absolue
            Field4 = 0,                     // i64 : p2/p3
        }, token);

    /// <summary>Paquet 280 attr 0x02 = gold — miroir du
    /// <c>GainGold → ClientUpdateAttr</c> natif
    /// (CPlayerFactory::GainForeverGold).</summary>
    private static Task SendGoldAttrAsync(NetSession session, int charId,
        long gold, CancellationToken token) =>
        SendAttrAsync(session, charId, 0x02, (int)gold, token);
}
