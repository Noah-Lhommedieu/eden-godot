using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Partiel mission — commandes texte de transition
/// mission→zone (item donné/retiré, add-class, transport
/// teleport, ouverture de services) exécutées sur la boucle.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>58 ZoneServerTransitionCommands — la mission ordonne à la
    /// zone d'exécuter des commandes texte pour un perso. C'est le chemin
    /// natif qui ouvre les services NPC : la mission répond aux clics de
    /// dialogue par `shop item &lt;npc&gt; &lt;storeId&gt;`, `Service ...`,
    /// `bank`, `transport`… Wire (Deserialize zone @0x833cc40) :
    ///   [s16 ?][i32 charUid][u16 n][n×(u16 len, string cmd)]
    /// Execute (@0x833c600) : GetCharacterAll(charUid) puis
    /// IZoneServerTextCommand::Execute(char, cmd) pour chaque commande —
    /// TC_Shop dispatche les verbes spell/eq/item/bank/mail/auction/ah/
    /// transport/mission-open/mission-reward/… vers les CShop::OpenShop
    /// (désasm TC_Shop @0x83d9450 + strings '/shop &lt;type&gt; …').</summary>
    private async Task HandleTransitionCommandsAsync(byte[] b)
    {
        if (b.Length < 8) return;
        var charUid = BitConverter.ToInt32(b, 2);       // i32 @+2 = charUid
        var session = _services.Sessions.FindByCharacter(charUid);
        if (session == null)
        {
            EdenLog.Write($"[Zone-M] 58 cmds char#{charUid} sans session");
            return;
        }
        var n = BitConverter.ToUInt16(b, 6);            // u16 : nb commandes
        var o = 8;
        for (var i = 0; i < n && o + 2 <= b.Length; i++)
        {
            var len = BitConverter.ToUInt16(b, o); o += 2;
            if (o + len > b.Length) break;
            var cmd = Encoding.GetEncoding("GBK")
                .GetString(b, o, len).TrimEnd('\0');
            o += len;
            EdenLog.Write($"[Zone-M] 58 cmd char#{charUid} : '{cmd}'");
            if (_states.TryGetValue(session.SessionId, out var st))
                try { await RunZoneTextCommandAsync(session, st, cmd); }
                catch (Exception ex)
                { EdenLog.Write($"[Zone-M] cmd '{cmd}' : {ex.Message}"); }
        }
    }
    /// <summary>Interprète une commande texte venue de la mission
    /// (TransitionCommands 58). Équivalent natif :
    /// IZoneServerTextCommand::Execute → table de commandes zone →
    /// TC_Shop : `shop|Service &lt;verb&gt; &lt;npc&gt; &lt;id&gt;` avec
    /// verb ∈ {item, eq, bank, mail, auction, ah, transport, spell,
    /// mission-open, mission-reward, mission-on-going, mission-finish,
    /// mission-share, mission-share-fail, strengthen, create_family}.
    /// Chaque OpenShop natif émet son CNC (vérifié par vtable) :
    ///   item/eq → CItemShop/CEquipmentShop → 304 EnterShop
    ///   bank    → CBankShop → 357 EnterStorage [i32]
    ///   mail    → CMailShop → 399 MailOpenBox [i32]
    ///   transport → CTransportShop → 393 EnterTransport
    ///               [u16 n][n×s16 routes][i32]
    ///   mission-* → CMissionShop → CMissionListOp + 388 EnterMissionList
    ///   auction → CAuctionShop → ComposeOutput mission (requête liste).
    /// </summary>
    private async Task RunZoneTextCommandAsync(NetSession session,
        EntryState st, string cmd)
    {
        var t = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0) return;
        var verb = t[0].ToLowerInvariant();
        int ai = 1;
        // `service <type> <ref> <extra>` — dispatch numérique direct
        // (émis par le chemin mission TC_Service) → table native
        // type→UI de RunNpcServiceAsync. À distinguer du mot conteneur
        // `service <verbe>` (non numérique).
        if (verb == "service" && t.Length > 1 &&
            int.TryParse(t[1], out var svcType))
        {
            await RunNpcServiceAsync(session, st, svcType, Arg(ai + 1),
                Arg(ai + 2), CancellationToken.None);
            return;
        }
        // `Service v a b` et `shop v a b` : le verbe shop est t[1] quand
        // la tête est un mot conteneur.
        if (verb is "service" or "shop" && t.Length > 1)
        { verb = t[1].ToLowerInvariant(); ai = 2; }
        // Verbes `shop <v>` issus de TC_Service natif (mission) — table
        // complète @0x80ef980 : le verbe texte se remappe sur le type de
        // service numérique puis délègue à la même table d'UI.
        if (ai == 2 && ShopVerbToType(verb) is { } shopType)
        {
            await RunNpcServiceAsync(session, st, shopType, Arg(ai + 1),
                Arg(ai + 2), CancellationToken.None);
            return;
        }
        int Arg(int i) => i < t.Length && int.TryParse(t[i], out var v)
            ? v : 0;
        // Arg nommé `clé = valeur` des commandes WorldTextCommand
        // natives (« UpdateWarCoin ch_id = N coin = N ») — le premier
        // token numérique après la clé.
        int ArgVal(string key)
        {
            var k = Array.FindIndex(t,
                s => s.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (k < 0) return 0;
            for (var j = k + 1; j < t.Length; j++)
                if (int.TryParse(t[j], out var v)) return v;
            return 0;
        }

        switch (verb)
        {
            case "updatewarcoin":
                // « UpdateWarCoin ch_id = %d coin = %d » —
                // Mission_TC_UpdateWarCoin natif @0x83ad270 :
                // char+0xd70 = coin puis 576 ClientUpdateWarCoin
                // [i32 coin] (war_coin = « Battle Merit »).
                await session.SendAsync(
                    new CNC_CZ_ClientUpdateWarCoin
                    { Value = ArgVal("coin") }, CancellationToken.None);
                break;
            case "updatealpacacoin":
                // « UpdateAlpacaCoin ch_id = %d coin = %d » —
                // Mission_TC_UpdateAlpacaCoin natif @0x83ad190 :
                // char+0xd7c = coin puis 580 ClientUpdateAlpacaCoin.
                await session.SendAsync(
                    new CNC_CZ_ClientUpdateAlpacaCoin
                    { Value = ArgVal("coin") }, CancellationToken.None);
                break;
            case "tpa": // tpa <posGroupId> <areaId> — point de sauvegarde
                // TC_TPA natif (mission @0x80f0910) → zone `tpa a b` :
                // téléporte le perso au groupe de positions a de sa
                // scène + mémorise last_normal_area_id = node<<16|b.
                await RunTpaAsync(session, st, Arg(ai), Arg(ai + 1),
                    CancellationToken.None);
                break;
            case "gain_gold": // gain_gold ±n — TC_Gold natif
                {
                    var n = Arg(ai);
                    if (n != 0)
                    {
                        st.Gold = (int)await _services.Characters
                            .AddGoldAsync((int)st.CharId, n);
                        await SendAttrAsync(session, st.CharId, 0x02,
                            st.Gold, 0, CancellationToken.None);
                    }
                }
                break;
            case "eden-scene-event": // Event_TriggerEvent de dialogue
                {
                    var sc = _services.Scenes.GetScene($"S{st.NodeId:D3}");
                    if (sc != null)
                        await RunSceneEventAsync(session, st, sc, Arg(ai),
                            CancellationToken.None);
                }
                break;
            case "eden-debuff": // DeleteBuff de dialogue NPC
                await RemoveEventBuffAsync(session, st, Arg(ai),
                    CancellationToken.None);
                break;
            // set_achievement <id> <point> <flag> — TC_SetAchievement
            // zone natif @0x083d51d0 (AgentDBA "Achievement id/point"
            // → mission SetAchievement ; flag≠0 → membres locaux).
            case "set_achievement":
                await GrantAchievementToTargetsAsync(session, st,
                    Arg(ai), Arg(ai + 1), Arg(ai + 2) != 0,
                    CancellationToken.None);
                break;
            case "open_exploit_rank": // TC_OpenExploitRank @0x083beee0 :
                // 385 ClientOpenExploitRank — paquet VIDE natif
                // (Serialize/Deserialize no-ops @0x081c0520), simple
                // signal d'ouverture du panneau d'exploits PvP.
                await session.SendAsync(new CNC_CZ_ClientOpenExploitRank(),
                    CancellationToken.None);
                break;
            case "item":    // /shop item <npc> <storeId>
            case "eq":      // /shop eq … — même 304 côté client
            case "equipment":
                await SendEnterShopAsync(session, st, Arg(ai + 1),
                    CancellationToken.None);
                break;
            case "bank":    // CBankShop → 357 [i32] (EnterStorage)
                await session.SendAsync(new CNC_CZ_ClientEnterStorage
                { Value = (int)st.DialogNpcHandle },
                    CancellationToken.None);
                break;
            case "repairshop": // TC_RepairShop @0x083bf670 →
                // 350 ClientEnterRepairShop — corps VIDE natif
                // (Serialize no-op @0x081bfbd0).
                await session.SendAsync(new CNC_CZ_ClientEnterRepairShop(),
                    CancellationToken.None);
                break;
            case "identify_shop": // TC_IdentifyShop @0x083bf5b0 →
                // 365 ClientEnterIdentifyShop — corps VIDE natif.
                await session.SendAsync(new CNC_CZ_ClientEnterIdentifyShop(),
                    CancellationToken.None);
                break;
            case "enter_exchange_item_service": // TC_ExchageItemService
                // @0x083befe0 → 366 ClientEnterExchageItemService —
                // corps VIDE natif (le service d'échange est ensuite
                // alimenté par les NCIDs exchange dédiés).
                await session.SendAsync(
                    new CNC_CZ_ClientEnterExchageItemService(),
                    CancellationToken.None);
                break;
            case "select_family_emblem": // TC_SelectFamilyEmblem
                // @0x081d3bf0 → 384 ClientSelectFamilyEmblem —
                // corps VIDE natif (ouvre le sélecteur d'emblème).
                await session.SendAsync(
                    new CNC_CZ_ClientSelectFamilyEmblem(),
                    CancellationToken.None);
                break;
            case "inlay_shop": // TC_InlayShop @0x083c6de0 — arg = npc
                // net id ; CCharacter::ResetOperation (l'op en cours
                // est détruite) puis CInlayShopOp (type 0x23) + émission
                // de 370 ClientEnterInlayShop [i32 npcId].
                st.ShopOpType = InlayShopOpType;
                st.ShopOpNpcUid = Arg(ai);
                st.ShopOpNpcHandle = st.DialogNpcHandle;
                st.WorkOpType = -1;
                CancelPendingStrengthen(st);
                await session.SendAsync(new CNC_CZ_ClientEnterInlayShop
                { Value = st.ShopOpNpcUid }, CancellationToken.None);
                break;
            case "mail":    // CMailShop → 399 [i32 boxType]
                await session.SendAsync(new CNC_CZ_ClientMailOpenBox
                { Value = 0 }, CancellationToken.None);
                break;
            case "transport": // CTransportShop → 393 [u16 n][n×s16][i32]
                await SendEnterTransportAsync(session, st,
                    Arg(ai), Arg(ai + 1));
                break;
            case "auction": // CAuctionShop → ClientAuctionHouse 430 [i32]
            case "ah":      // (ncid_map : CNC_CZ_ClientAuctionHouse=430)
                await session.SendAsync(new CNC_CZ_ClientAuctionHouse
                { Value = (int)st.DialogNpcHandle },
                    CancellationToken.None);
                break;
            case "spell":   // magasin de sorts — 349 [i32][u16][u16]
                await session.SendAsync(new CNC_CZ_ClientEnterSpellShop
                {
                    Field1 = (int)st.DialogNpcHandle,
                    Field2 = (ushort)Arg(ai), Field3 = 0,
                }, CancellationToken.None);
                break;
            case "mission-open":      // CMissionShop → 388 EnterMissionList
            case "mission-reward":    // Désasm CMissionShop::OpenShop
            case "mission-on-going":  // @0x857a570 : compare du verbe
            case "mission-finish":    // (mission-open/reward/on-going/
            case "mission-share":     // finish/share → op 1/2/3/4/7),
                                      // ResetOperation(CMissionListOp)
                                      // puis NotifyClient 388.
                // 388 [s16 op][i32 arg2][i32 npcUid][s16 arg3] —
                // Serialize @0x81e6d50 : +4=s16 op, +8=i32, +c=i32,
                // +10=s16. Capture native `0200 f4030000 54c30000 0000`
                // (op=2, arg=0x3f4, npcUid=50004, term=0).
                var op = verb switch
                {
                    "mission-open" => (short)1,
                    "mission-reward" => (short)2,
                    "mission-on-going" => (short)3,
                    "mission-finish" => (short)4,
                    "mission-share" => (short)7,
                    _ => (short)0,
                };
                await session.SendAsync(new CNC_CZ_ClientEnterMissionList
                {
                    Field1 = op,                        // s16 : op
                    Field2 = Arg(ai + 1),               // i32 : arg2 (+8)
                    Field3 = Arg(ai) != 0
                        ? Arg(ai) : st.DialogNpcUid,    // i32 : npcUid (+c)
                    Field4 = 0,                         // s16 : (+10)
                }, CancellationToken.None);
                break;
            case "mission-share-fail":
                // ShareMission natif (échec d'éligibilité) : seule la
                // ResetOperation de l'op du destinataire — les refus
                // sont déjà dits au partageur par texte 0x619-0x61d /
                // 0xa03. Aucune fiche 388 ne s'ouvre.
                break;
            case "eden-give-item": // remise quête : eden-give-item <itemId> <n>
                await GiveQuestItemAsync(session, st, Arg(ai),
                    Math.Max(1, Arg(ai + 1)));
                break;
            case "eden-text": // texte système à ce client : eden-text <index>
                await SendTextIndexAsync(session, (short)Arg(ai),
                    CancellationToken.None);
                break;
            case "eden-take-item": // consommation quête : eden-take-item <itemId> <n>
                await TakeQuestItemAsync(session, st, Arg(ai),
                    Math.Max(1, Arg(ai + 1)));
                break;
            case "eden-appellation": // MC add_appellation → 398
                // CPlayerFactory::AddAppellation natif (@0x8103a40) :
                // INSERT player_appellation puis 398 ClientAddAppellation
                // `[i32 appellationId]` (zone/decompiled.c + ncid_map).
                {
                    var appId = Arg(ai);
                    if (appId > 0)
                    {
                        await _services.Characters.AddAppellationAsync(
                            (int)st.CharId, appId);
                        await session.SendAsync(
                            new CNC_CZ_ClientAddAppellation
                            { Value = appId }, CancellationToken.None);
                        // EffectToLife(-2,0x3020) natif applique
                        // l'enchant du titre immédiatement →
                        // NotifyRefreshAttr : réémission 296/492/493
                        // (l'étape « titles » du StatPipeline somme
                        // l'enchant du nouveau titre).
                        await ProfileStatEmitter.SendForCharacterAsync(
                            session, _services, (int)st.CharId,
                            CancellationToken.None);
                    }
                }
                break;
            case "eden-forever-gold": // MC ForeverGold → GainForeverGold
                // Natif : char+0x4b4 += n, SavePlayerCharacter
                // ("forever_gold") puis 280 attr=3 = nouveau total.
                {
                    var fg = Arg(ai);
                    if (fg != 0)
                    {
                        var total = await _services.Characters
                            .AddForeverGoldAsync((int)st.CharId, fg);
                        await SendAttrAsync(session, st.CharId, 0x03,
                            (int)Math.Min(int.MaxValue, total), 0,
                            CancellationToken.None);
                    }
                }
                break;
            case "eden-add-class": // MC AddClass → add_class N
                // TC_AddClass natif : classlist insert + sorts du
                // spellmaster de la nouvelle classe (comme
                // HandleChangeClassAsync sans le switch courant).
                {
                    var cls = Arg(ai);
                    var rows = await _services.Classes
                        .GetClassesByCharacterIdAsync((int)st.CharId);
                    if (cls > 0 && rows.All(r => r.ClassId != cls))
                    {
                        await _services.Classes.AddClassAsync(
                            (int)st.CharId, cls);
                        var cdef = _services.GameData.Classes
                            .FirstOrDefault(x =>
                                x.Id == st.RaceId * 100 + cls ||
                                x.Id == cls);
                        var known = (await _services.Classes
                            .GetPlayerSpellsAsync((int)st.CharId))
                            .Select(x => unchecked((ushort)x.Spell))
                            .ToHashSet();
                        foreach (var sm in Eden.Core.Game.Classes
                            .ClassSkillTable.GrantableSpells(cls, 1,
                                _services.GameData.Spells, cdef))
                            if (known.Add(unchecked((ushort)sm)))
                                await _services.Classes.LearnSpellAsync(
                                    (int)st.CharId,
                                    unchecked((short)sm));
                _services.SpellLevelsDirty?.Invoke((int)st.CharId);
                        await HandleQueryClassListAsync(session, st,
                            CancellationToken.None);
                        EdenLog.Write($"[Zone-M] add-class " +
                            $"char={st.CharId} class={cls}");
                    }
                }
                break;
            case "eden-announce": // MC Announce → message système zone
                // Natif : mission → world "Announce <textId>" →
                // broadcast channel. Ici : 316 TextIndex broadcast
                // à tous les persos du node (message système).
                await BroadcastTextIndexAsync(st.NodeId, st.InstanceGroup, (short)Arg(ai),
                    CancellationToken.None);
                break;
            case "eden-class-point": // RewardClassPoint : gain_class_point
                // natif (GainReward @0x808da30 → CDBAgent
                // gain_class_point N). Réutilise le grant complet du
                // kill : class EXP + palier + NP dépensable + refresh
                // 489/attrs — émis par la zone comme le natif.
                await GrantClassPointAsync(session, st, Arg(ai));
                break;
            case "eden-buff": // RewardBuff : eden-buff <enchantId> <durée s>
                // Enchant de récompense posé sur le perso — même 299
                // que Event_GiveBuff (EnchantAdd éprouvé).
                await ApplyEventBuffAsync(session, st, Arg(ai),
                    Arg(ai + 1), CancellationToken.None);
                break;
            case "cleartriggerareaid": // ClearTriggerAreaID ch_id=<uid>
                // Réponse mission au CheckTrigger natif (TC_CheckTrigger
                // @0x8203770 → CNC_MW_WorldTextCommand) : reset de
                // char+0x7a4 (anti-retrigger) — ré-autorise l'aire.
                st.LastGatewayKey = 0;
                break;
            case "setra": // TC_SetReviveArea : setra <node> <areaId>
                // Mission `ReviveArea <node> <id>` → "setra %d %d" — le
                // natif valide l'event-area puis SetReviveAreaID
                // ((node<<16)|id → char+0x638 + UPDATE revive_area_id +
                // 531 ClientReviveInfo au client).
                {
                    var node = (short)Arg(ai);
                    var aid = Arg(ai + 1);
                    var sc = _services.Scenes.GetScene($"S{node:D3}");
                    if (node > 0 && sc?.EventAreas.Any(a => a.Id == aid)
                            == true)
                    {
                        st.ReviveAreaId = node << 16 | aid;
                        await _services.Characters.SetReviveAreaIdAsync(
                            (int)st.CharId, st.ReviveAreaId);
                        // 531 [i32 area] — même paquet que SetReviveAreaID
                        // natif (NotifyClient ClientReviveInfo).
                        await session.SendAsync(new CNC_CZ_ClientReviveInfo
                        {
                            Value = st.ReviveAreaId,
                        }, CancellationToken.None);
                        EdenLog.Write($"[Zone-M] setra char={st.CharId} " +
                            $"revive={st.ReviveAreaId:x}");
                    }
                    else
                        EdenLog.Write($"[Zone-M] setra char={st.CharId} " +
                            $"aire {node}|{aid} invalide — ignoré");
                }
                break;
            case "eden-reward": // récompense quête : eden-reward <gold> <exp>
                // La mission a déjà écrit la DB — ici on met à jour
                // l'état live (attr or via 280 + niveau/XP si palier).
                {
                    var g = Arg(ai);
                    var xp = Arg(ai + 1);
                    // L'exp de quête passe par le vrai grant : 600 +
                    // level-up + refresh vitals — sinon le client ne
                    // voyait jamais l'XP de quête (bug live).
                    if (xp > 0)
                        await GrantCharExpAsync(session, st, xp);
                    // 280 attr 2 = or courant (GainGold natif émet
                    // ClientUpdateAttr attr 2 — pseudo-C zone/
                    // decompiled.c). 0x30 était une autre attr → le
                    // client n'affichait pas l'or gagné.
                    if (g != 0)
                    {
                        st.Gold += g;
                        await SendAttrAsync(session, st.CharId, 0x02,
                            st.Gold, 0, CancellationToken.None);
                    }
                    EdenLog.Write($"[Zone-M] eden-reward char={st.CharId} " +
                        $"+{g}g +{xp}xp");
                }
                break;
            default:
                EdenLog.Write($"[Zone-M] cmd zone non gérée : '{cmd}'");
                break;
        }
    }
    /// <summary>Verbe `shop &lt;v&gt;` de TC_Service natif → type de
    /// service numérique (table désasm @0x80ef980). null = pas un verbe
    /// de service (mission-*, eden-*, spell — gérés par le switch).</summary>
    private static int? ShopVerbToType(string verb) => verb switch
    {
        "eq" => 1, "item" or "prescription_item" => 3,
        "create_family" => 5, "bank" => 6, "mail" => 7,
        "transport" => 8, "strengthen" => 9, "combine" => 10,
        "family_building" => 11, "mining" => 12, "planting" => 13,
        "summon" => 14, "alchemy" => 15, "casting" => 16,
        "enchanting" => 17, "auction" or "ah" => 18,
        "family_storage" => 19, "message_board" => 20,
        "town_create" => 21, "family_town" => 25,
        "create_gladiator_team" => 27, "magic_box" => 31,
        "instance_dialog" => 35, "room_storage" => 50,
        "hotel_playertown" => 51, "hotel_holytown" => 52,
        "hotel_goldencity" => 53, "hotel_party" => 54,
        "sky_tower" => 55, "mission_shop" => 56,
        "church_reserve" => 57, "pay_system" => 58,
        "evaluate" => 59, "battle_crusade" => 60,
        _ => null,
    };
    /// <summary>`tpa &lt;posGroupId&gt; &lt;areaId&gt;` — TC_TPA natif
    /// (mission @0x80f0910) : téléporte le perso au groupe de positions
    /// `posGroupId` de sa scène (point de sauvegarde) et mémorise
    /// `last_normal_area_id = node&lt;&lt;16|areaId` — le « TPA » des
    /// dialogues NPC (`TPA 177 21` dans S_Npc : retour au point
    /// d'enregistrement). Émission 313 comme le refresh revive.</summary>
    private async Task RunTpaAsync(NetSession session, EntryState st,
        int posGroupId, int areaId, CancellationToken token)
    {
        var sc = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        if (sc != null &&
            sc.PositionGroups.TryGetValue(posGroupId, out var ps) &&
            ps.Count > 0)
        {
            var p = ps[0];
            st.PosX = p.X; st.PosZ = p.Y; st.Facing = p.Facing;
            _writeBehind.MarkCoordinates((int)st.CharId,
                st.NodeId, p.X, p.Y, p.Z, p.Facing);
            await session.SendAsync(new CNC_CZ_ClientEnterNodeNodeData
            {
                Field1 = st.NodeId, Field2 = 1, Field3 = st.PosX,
                Field4 = st.PosZ, Field5 = st.Facing,
            }, token);
        }
        if (areaId > 0)
        {
            var comp = st.NodeId << 16 | areaId;
            st.LastNormalAreaId = comp;
            _writeBehind.MarkLastNormalAreaId((int)st.CharId, comp);
        }
        EdenLog.Write($"[Zone-M] tpa char={st.CharId} " +
            $"pos={posGroupId} area={areaId}");
    }
    /// <summary>Donne `count` exemplaires d'un item de quête
    /// (objectif NPCItem — le NPC remet l'item au joueur, natif
    /// `InventoryItem &lt;id&gt; + &lt;n&gt;`). INSERT inventory1 + 274
    /// ClientPutItemIntoInventory pour le rendre visible.</summary>
    private async Task GiveQuestItemAsync(NetSession session,
        EntryState st, int itemId, int count)
    {
        if (itemId <= 0 || count <= 0) return;
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var (ok, _, bag, slot) = await _services.Inventory
            .AddItemAsync((int)st.CharId, itemId, count,
                0, 0, false, touched);
        if (!ok)
        {
            // Inventaire plein → trésor au sol (CloneQuestTreasure
            // natif : le pickup échoué retombe via DropTreasureOnFloor
            // — l'item de quête n'est jamais perdu).
            await SpawnGroundTreasureAsync(st, itemId, count);
            return;
        }
        // Merge → 291, nouveau slot → 274 (règle native, cf.
        // NotifyItemSlotsAsync — un 274 sur slot occupé crashe).
        await NotifyItemSlotsAsync(session, st, touched,
            CancellationToken.None);
        // 146 : la remise peut satisfaire un objectif ItemCollect.
        if (_mission.Connected)
            _ = _mission.SendQuestItemChangedAsync(st.CharId, itemId);
        EdenLog.Write($"[Zone-M] give-item char={st.CharId} " +
            $"item={itemId} x{count} bag={bag} loc={slot}");
    }
    /// <summary>Retire `count` exemplaires d'un item de l'inventaire
    /// (livraison/collect de quête — équivalent natif du RemoveItem
    /// déclenché par TakeItem/RemoveFinishItem). Pour chaque stack
    /// retiré : DELETE DB puis 275 ClientDropItem
    /// `[s16 container][s16 loc][s16 count]` qui vide le slot côté
    /// client.</summary>
    private async Task TakeQuestItemAsync(NetSession session,
        EntryState st, int itemId, int count)
    {
        if (itemId <= 0 || count <= 0) return;
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        var left = count;
        foreach (var it in items.Where(i => i.ItemId == itemId &&
            !i.IsEquipped))
        {
            if (left <= 0) break;
            var take = Math.Min(left, Math.Max(1, it.Quantity));
            var newCount = it.Quantity - take;
            if (!await _services.Inventory.RemoveItemAsync(
                    (int)st.CharId, it.BagId, it.Slot, take))
                continue;
            // Wire natif (DropItemByItemID @0x8090b50) : pile vidée →
            // 275 [s16 contType][s16 loc][s16 contIdx] (pas de qté) ;
            // pile résiduelle → 291 [contType][loc][newCount][maxDur]
            // [contIdx][0]. L'ancien corps [sac][loc][n] décalait les
            // champs — retrait invisible côté client.
            if (newCount > 0)
                await session.SendAsync(new Eden.Core.Networking
                    .Messages.Generated.CNC_CZ_ClientModifyItemDurability
                    {
                        Field1 = 0, Field2 = (short)it.Slot,
                        Field3 = (short)newCount,
                        Field4 = (short)it.MaxDurability,
                        Field5 = (short)it.BagId, Field6 = 0,
                    }, CancellationToken.None);
            else
                await session.SendAsync(new Eden.Core.Networking
                    .Messages.Generated.CNC_CZ_ClientDropItem
                    {
                        Field1 = 0, Field2 = (short)it.Slot,
                        Field3 = (short)it.BagId,
                    }, CancellationToken.None);
            left -= take;
            EdenLog.Write($"[Zone-M] take-item char={st.CharId} " +
                $"item={itemId} bag={it.BagId} loc={it.Slot} x{take}");
        }
        // 146 : le retrait peut casser un objectif ItemCollect déjà
        // rempli (natif : bit clear → SetFMS(3)).
        if (left < count && _mission.Connected)
            _ = _mission.SendQuestItemChangedAsync(st.CharId, itemId);
        if (left > 0)
            EdenLog.Write($"[Zone-M] take-item char={st.CharId} " +
                $"item={itemId} : {left} non trouvés");
    }
    /// <summary>393 ClientEnterTransport — wire désasm Serialize
    /// @0x81e6e10 : `[u16 n][n×s16 routeId][i32]` — la liste des routes
    /// desservies vient du serveur (CTransportShop(u16 npcUid, s16 grp)
    /// → routes du groupe). On envoie les ids d'aires destination du
    /// transporteur, queue = npcUid.</summary>
    private async Task SendEnterTransportAsync(NetSession session,
        EntryState st, int npcArg, int groupId)
    {
        var routes = _services.GameData.TransportRoutes
            .Select(r => (short)r.Id).ToList();
        st.TransportRoutes = routes; // retenu pour résoudre le 88 (index)
        // Ouvre l'opération CTransportShop — le 88 qui suit sera
        // consommé en one-shot (ResetOperation natif).
        st.TransportOpen = true;
        var msg393 = new CNC_CZ_ClientEnterTransport
        { Field3 = st.DialogNpcUid };
        foreach (var r in routes) msg393.Routes.Add((short)r);
        await session.SendAsync(msg393, CancellationToken.None);
        EdenLog.Write($"[Zone-M] 393 transport routes={routes.Count}");
    }
}
