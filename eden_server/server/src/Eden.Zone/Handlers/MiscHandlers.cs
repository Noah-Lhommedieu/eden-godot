using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Commandes diverses restantes : arènes (colosseum/gladiator/battle
/// royale), événements (greedy island, treasure), licences, boîte
/// magique, sac chanceux, messages, requêtes d'état.
/// </summary>
public static class MiscHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // --- Arènes / PvP instancié ---
        // Colosseum 196-201/219 — interceptés par
        // ZoneEntryFlow.TryHandleOnLoopAsync
        // (ZoneEntryFlow.Colosseum.cs : gates natifs @0x8244d30,
        // files par catégorie, matchs → 527+ téléport node 401,
        // réponses 528/529/530/552/587). Ces handlers-table ne sont
        // que des garde-fous pour un paquet arrivant hors état.
        d.RegisterChar(() => new CNE_CZ_ZoneServerColosseumJoinRequest(),
            (session, msg, charId, token) => Task.CompletedTask);
        d.RegisterChar(() => new CNE_CZ_ZoneServerColosseumJoinAnswer(),
            (session, msg, charId, token) => Task.CompletedTask);
        d.RegisterChar(() => new CNE_CZ_ZoneServerColosseumLeaveJoin(),
            (session, msg, charId, token) => Task.CompletedTask);
        d.RegisterChar(() => new CNE_CZ_ZoneServerColosseumModeQuery(),
            (session, msg, charId, token) => Task.CompletedTask);
        d.RegisterChar(() => new CNE_CZ_ZoneServerColosseumTeamQuery(),
            (session, msg, charId, token) => Task.CompletedTask);

        // GladiatorTeamCreate [string name, byte ?] — MORT EN NATIF :
        // Execute @0x825cd90 = tail-call GetType() ignoré (objdump).
        // GladiatorTeamQuery — idem (même pattern @0x825cxxx).
        d.RegisterChar(() => new CNE_CZ_ZoneServerGladiatorTeamCreate(),
            (session, msg, charId, token) => Task.CompletedTask);
        d.RegisterChar(() => new CNE_CZ_ZoneServerGladiatorTeamQuery(),
            (session, msg, charId, token) => Task.CompletedTask);

        // BattleRoyaleJoinRequest 241 — géré dans
        // ZoneEntryFlow.BattleRoyale.cs (gates natives + file BR).

        // 259 BattleCrusadeJoin [i32 crusadeId] — désasm Execute :
        // check encaged puis CDBAgent::Command "CrusadeJoin
        // crusade_id= %d" vers la mission. L'inscription effective est
        // mission-side (non portée — la commande est loguée).
        d.RegisterChar(() => new CNE_CZ_ZoneServerBattleCrusadeJoin(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"CrusadeJoin crusade_id= {msg.Value}", token);
            });

        // 252 VersusInstanceJoinRequest [i32 v1][i32 v2] — intercepté
        // par ZoneEntryFlow (TryHandleAsync, besoin d'EntryState +
        // NodeDef.Type) : Execute natif @0x82a0670 — v2≠0 + in-versus
        // (scene_type==0x11) → leave ; v1==0 → sortie de file ;
        // v1≠0 → validations (niv. S_Parameter 601=85, pending, état,
        // scène exclue {4-10,12,14-19}, enchant 54486) puis ZMis 194.
        d.RegisterChar(() => new CNE_CZ_ZoneServerVersusInstanceJoinRequest(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Événements ---
        // 233 GreedyIslandJoin [i32 arg] — désasm Execute : arg==0 →
        // CDBAgent::Command "GreedyIslandInfo" (requête d'état) ;
        // arg!=0 → join (encaged + validations event) — le join est
        // sous-système event non porté, seule la requête relaie.
        d.RegisterChar(() => new CNE_CZ_ZoneServerGreedyIslandJoin(),
            async (session, msg, charId, token) =>
            {
                if (msg.Value == 0 && s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync(
                        (uint)charId, "GreedyIslandInfo", token);
            });

        // MORT EN NATIF : Execute @0x825ee00 = guard + tail-call
        // vtable+8 (GetType, résultat ignoré). Aucun traitement.
        d.RegisterChar(() => new CNE_CZ_ZoneServerGreedyIslandRankQuery(),
            (session, msg, charId, token) => Task.CompletedTask);

        // RollTreasure [uint ?, short ?] — roll de loot d'équipe
        // (no-op : pas de loot-roll en solo).
        d.RegisterChar(() => new CNE_CZ_ZoneServerRollTreasure(),
            (session, msg, charId, token) => Task.CompletedTask);

        // TreasurePickup (35) — intercepté par TryHandleAsync
        // (ZoneEntryFlow.Treasure.cs : distance 4u, propriétaire,
        // insertion, 386 + 330). Ce stub ne sert que de garde-fou si
        // le paquet arrive avant l'entrée complète.
        d.RegisterChar(() => new CNE_CZ_ZoneServerTreasurePickup(),
            (session, msg, charId, token) => Task.CompletedTask);

        // TerritoryJoin (190) + TerritoryQuery (189) — interceptés
        // par ZoneEntryFlow.TryHandleOnLoopAsync
        // (ZoneEntryFlow.Territory.cs : gates natifs + codec 515 +
        // join/leave/téléportation via TerritoryWarService).
        d.RegisterChar(() => new CNE_CZ_ZoneServerTerritoryJoin(),
            (session, msg, charId, token) => Task.CompletedTask);
        d.RegisterChar(() => new CNE_CZ_ZoneServerTerritoryQuery(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Mini-jeux / fonctionnalités items ---
        // ChanceLuckyBagGetReward (221) / ReTry (222) — interceptés
        // par ZoneEntryFlow (TryHandleAsync) : machine à état
        // char+0xc70.. native complète dans ZoneEntryFlow.LuckyBag.cs
        // (lancer pondéré fortune_bag, échec cumulé params 452/453,
        // récompense + annonce bulletin). Le corps natif ne lit
        // aucun champ du paquet.
        d.RegisterChar(() => new CNE_CZ_ZoneServerChanceLuckyBagGetReward(),
            (session, msg, charId, token) => Task.CompletedTask);

        d.RegisterChar(() => new CNE_CZ_ZoneServerChanceLuckyBagReTry(),
            (session, msg, charId, token) => Task.CompletedTask);

        // ExecuteMagicBox (257) / VerifyCubeCosts (232) — interceptés
        // par ZoneEntryFlow (TryHandleAsync) : le natif exige l'op
        // 0x58/0x52 (ZoneEntryFlow.Strengthen.cs — cancel 434 sinon).

        // 66 ExchangeItemPin [str][str] — désasm Execute @zone :
        // snprintf "exchange item %d %hd %s %s" (charUid, type node
        // s16, champ1, champ2) envoyé en CNC_ZW_WorldServerTextCommand
        // (ZW 636) → le monde traite Server_TC_ExchageItem @0x80b82f0.
        d.RegisterChar(() => new CNE_CZ_ZoneServerExchangeItemPin(),
            async (session, msg, charId, token) =>
            {
                if (s.World?.Connected == true)
                    await s.World.SendTextCommandAsync(
                        $"exchange item {charId} 1 {msg.Field1} " +
                        $"{msg.Field2}", token);
            });

        // SetSpellStone [short,short,short,int] — pierre de sort
        // MORT EN NATIF : Execute = tail-call GetType() ignoré.
        d.RegisterChar(() => new CNE_CZ_ZoneServerSetSpellStone(),
            (session, msg, charId, token) => Task.CompletedTask);

        // EquipTrainSetItem (230) — implémenté dans
        // EquipTrainHandlers (Mana Infusion : sélection + 570 + 369).

        // EnchantUpdate [i32 targetUid] — le client demande le
        // rafraîchissement des buffs visibles d'une entité (clic sur
        // un joueur/mob). Natif @0x824c5f0 : GetUpdateEnchants →
        // ClientEnchantUpdate(uid, set, flags, byte0x280).
        // Corps natif (Serialize @0x822fb70) :
        //   [i32 uid][u8 n] puis par enchant
        //   [s16 effectId][i32 casterId][i32 restTime][s16 f3][s16 f4]
        //   [4B note] ; queue [4B flags][u8 b280].
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnchantUpdate(),
            async (session, msg, charId, token) =>
            {
                // Champ généré "EnchantFlags" = l'uid d'entité natif
                // (i32 — cible dont le client veut les buffs).
                var buffs = s.Spells.GetBuffs((int)msg.EnchantFlags);
                var reply = new CNC_CZ_ClientEnchantUpdate
                { Field1 = (int)msg.EnchantFlags };       // i32 uid
                foreach (var b in buffs.Take(255))
                    reply.Entries.Add(new()
                    {
                        SpellId = (short)b.SpellId,
                        CasterId = b.CasterId,
                        RestTime = b.RemainingTicks,
                        Stacks = (short)b.Stacks,
                    });
                await session.SendAsync(reply, token);
            });

        // --- Messages (panneau d'affichage des salles) ---
        // 139 MessageWrite [str titre][str contenu] — désasm Execute
        // @0x8269960 : objet focus type 0x2b à portée + carte 0x7ceb
        // (CheckItemNum) exigés → sinon 457 [s16=3]. Succès → 802
        // MissionServerMessageWrite (la mission insère puis la zone
        // PURGE via MessageDelete à la réponse 740 — feature bridée
        // nativement). Le check de l'objet board est non vérifiable
        // sans modèle d'objets monde (le client n'ouvre l'UI qu'au
        // board) — seul le check carte est porté.
        d.RegisterChar(() => new CNE_CZ_ZoneServerMessageWrite(),
            async (session, msg, charId, token) =>
            {
                var items = await s.InventoryRepo
                    .GetItemsByCharacterIdAsync(charId);
                var hasCard = items.Any(i => i.ItemId == 0x7ceb
                    && !i.IsEquipped && i.Quantity > 0);
                if (!hasCard || s.Mission?.Connected != true)
                {
                    await session.SendAsync(
                        new CNC_CZ_ClientMessageWriteResult
                        { Value = 3 }, token);   // s16 : refus natif
                    return;
                }
                await s.Mission.SendMessageWriteAsync((uint)charId,
                    msg.Field1, msg.Field2, token);
            });

        // 140 MessageRead [i32 id] → DBA "MessageRead id = %d"
        // (Execute @0x8268xxx : LPrintf + AgentDBACommand).
        d.RegisterChar(() => new CNE_CZ_ZoneServerMessageRead(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync(
                        (uint)charId, $"MessageRead id = {msg.Value}",
                        token);
            });

        // 138 MessageListQuery [s16 page] → DBA
        // "MessageListQuery page = %hd". Réponse 739 ignorée en natif
        // → le client reçoit toujours une liste vide (456).
        d.RegisterChar(() => new CNE_CZ_ZoneServerMessageListQuery(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync(
                        (uint)charId,
                        $"MessageListQuery page = {msg.Value}", token);
            });

        // 141 MessageDelete [i32 id] → DBA "MessageDelete id = %d".
        d.RegisterChar(() => new CNE_CZ_ZoneServerMessageDelete(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync(
                        (uint)charId, $"MessageDelete id = {msg.Value}",
                        token);
            });

        // 142 MessageExit — Execute natif : clear du focus board
        // (char+0x670 si type 0x2b). Pas d'état à maintenir chez nous.
        d.RegisterChar(() => new CNE_CZ_ZoneServerMessageExit(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Divers état/requêtes ---
        // 202 CoRideInvite [u32 cible][u8 op] — monture duo. Désasm
        // Execute @0x8246130 : op 8 → UnCoRide (quitte) ; sinon la
        // cible doit être en zone (offline → texte 0x5af) puis op 0
        // invite / 1-3 réponses invite / 4 demande / 5-7 réponses
        // demande / 9 UnCoRide de l'autre. Les chemins 0-7 exigent que
        // l'invitant soit monté (+0x670, type selle 0x20-0x2c,
        // CCharacter::GetCoRideEmpty) : le sous-système montures n'est
        // pas porté → réponse native fidèle = texte 0xe19 ("monture
        // requise"), aucune liste de coride n'existe → 8/9 no-op.
        d.RegisterChar(() => new CNE_CZ_ZoneServerCoRideInvite(),
            async (session, msg, charId, token) =>
            {
                if (msg.Field2 == 8 || msg.Field2 == 9) return;
                if (s.Sessions.FindByCharacter((int)msg.Field1) == null)
                {
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x5af, Field2 = "" }, token);
                    return;
                }
                await session.SendAsync(
                    new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0xe19, Field2 = "" }, token);
            });

        // 238 ChangePositionV2 [s16 position] — rôle affiché dans la
        // recherche d'équipe (tank/dps/heal). Désasm Execute @0x8240910
        // → CCharacter::ChangePositionV2 @0x840b190 : positions <3,
        // no-op si inchangée ; pousse "RecruitUpdatePositionV2" à la
        // mission qui écrit ExecutionerData+0x87e — ce champ ressort
        // dans SSearchTeamMemberDataV2+0x12 des réponses SearchTeam.
        // Notre index d'annonces est zone-local → TeamService.
        d.RegisterChar(() => new CNE_CZ_ZoneServerChangePositionV2(),
            (session, msg, charId, token) =>
            {
                s.Teams.SetRecruitPosition(charId, msg.Value);
                return Task.CompletedTask;
            });

        // 250 ChurchReserve [i32 loverId] — désasm Execute @zone :
        // value == -1 → CDBAgent::Command "CloseWedding" ; sinon si
        // != CLover::DefaultCharLoverID → "VisitWeddingInstance
        // LoverID= %d". Les deux passent par la mission.
        d.RegisterChar(() => new CNE_CZ_ZoneServerChurchReserve(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected != true) return;
                var cmd = msg.Value == -1
                    ? "CloseWedding"
                    : $"VisitWeddingInstance LoverID= {msg.Value}";
                await s.Mission.SendAgentDbaCommandAsync(
                    (uint)charId, cmd, token);
            });

        // 43 CloseMissionBrief — le natif relaie à la mission
        // (CNC_ZMis_MissionServerCloseMissionBrief `[i32 charUid]`,
        // désasm Execute @0x8242ff0 / Serialize @0x8243520) : sans ça
        // l'opération dialogue reste pendante côté mission.
        d.RegisterChar(() => new CNE_CZ_ZoneServerCloseMissionBrief(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendCloseMissionBriefAsync(
                        (uint)charId, token);
            });

        // StartParty [s16][s16] — fête de salle perso. Execute natif
        // @0x828bb60 : exige char+0xbf0 == char+0x34 (être DANS sa
        // propre salle — impossible sans instances PlayerRoom_%d),
        // cooldown party_time → 0x1031, <3 occupants → 0x1030, sinon
        // CPlayerRoomManager::StartParty. INATTEIGNABLE en natif
        // hors salle — no-op fidèle.
        d.RegisterChar(() => new CNE_CZ_ZoneServerStartParty(),
            (session, msg, charId, token) => Task.CompletedTask);

        // NodeGroupReset — implémenté dans ZoneEntryFlow.Recruit.cs
        // (case 163 du TryHandleOnLoopAsync : reset des instances de
        // l'équipe, CheckResetAllNode natif).

        // StateCancel [short buffId] — annule un buff manuellement.
        // Natif @0x828b750 : ILifeEntity::RemoveEnchantByState →
        // le retrait émet ClientEnchantDel 300 [i32 char][s16 enchant]
        // (même paquet que DeleteBuff des scripts de scène).
        d.RegisterChar(() => new CNE_CZ_ZoneServerStateCancel(),
            async (session, msg, charId, token) =>
            {
                s.Spells.RemoveBuff(charId, msg.Value);
                await session.SendAsync(new CNC_CZ_ClientEnchantRemove
                {
                    Field1 = charId,            // i32 acteur
                    Field2 = msg.Value,         // s16 enchant retiré
                    Field3 = 0, Field4 = 0,
                }, token);
            });

        // NCID 181/182/183 (certificats de classe) : interceptés par
        // ZoneEntryFlow.License.cs AVANT le dispatcher — leurs
        // formats natifs sont variables et leur logique vit dans
        // LicenseService (player_license + lic_1..7 + combos).

        // DailyAwardOP [short, uint] — intercepté par
        // TryHandleOnLoopAsync (258 → ZoneEntryFlow.DailyAward.cs :
        // calendrier de connexion + claims, réponse 615).
        d.RegisterChar(() => new CNE_CZ_ZoneServerDailyAwardOP(),
            (session, msg, charId, token) => Task.CompletedTask);

        // PayEventSelect [u32 event][s16 choose] — intercepté par
        // TryHandleOnLoopAsync (253 → ZoneEntryFlow.PayEvent.cs :
        // TriggerPayEvent natif porté — conditions, coûts, awards,
        // 604 + textes).
        d.RegisterChar(() => new CNE_CZ_ZoneServerPayEventSelect(),
            (session, msg, charId, token) => Task.CompletedTask);

        // MORT EN NATIF : Execute @0x826e110 = tail-call GetType()
        // ignoré (réponse évent payant désactivée côté serveur US).
        d.RegisterChar(() => new CNE_CZ_ZoneServerPayEventResponse(),
            (session, msg, charId, token) => Task.CompletedTask);

        // NewPlayerGuide [ushort,ushort] — intercepté par
        // TryHandleOnLoopAsync (218 → ZoneEntryFlow.PlayerGuide.cs :
        // trigger/close/skip persistés dans new_player_guide).
        d.RegisterChar(() => new CNE_CZ_ZoneServerNewPlayerGuide(),
            (session, msg, charId, token) => Task.CompletedTask);

        // PlayerCCDataSave — intercepté par TryHandleOnLoopAsync
        // (242 → HandlePlayerCCDataSaveAsync : flux natif complet
        // cartes de classe personnalisée) ; le handler table reste
        // inerte comme pour 221/222.
        d.RegisterChar(() => new CNE_CZ_ZoneServerPlayerCCDataSave(),
            (session, msg, charId, token) => Task.CompletedTask);

        // UpdateBotData [byte,byte] — intercepté par
        // TryHandleOnLoopAsync (164 → PlayerGuide.cs : char+0xaa8/
        // 0xaa9 = deux booléens d'auto-jeu).
        d.RegisterChar(() => new CNE_CZ_ZoneServerUpdateBotData(),
            (session, msg, charId, token) => Task.CompletedTask);

        // ReportProcessInfos — télémétrie client : Execute natif
        // @0x827f7d0 ne fait que formater "[LIFE:nom,uid]" + la
        // liste des process dans une string de log — pure
        // diagnostic anti-triche, aucun effet de jeu ni paquet
        // émis. Inerte fidèle.
        d.Register(() => new CNE_CZ_ZoneServerReportProcessInfos(),
            (session, msg, token) => Task.CompletedTask);

        // 89 BagDueDate [s16 bag][u32 secondes] — extension de durée
        // d'un sac banque. Désasm CPlayerFactory::SetBagDueDate
        // @0x81133f0 : bag < 4 (sinon ClientTextOutput "Index
        // invalid"), durée < 0x278d01 (30 j, sinon silence), base =
        // max(échéance courante, now), cumul au-delà de 0x4f1a00
        // (60 j) → texte 0x27a ; consomme 1× item 0x9d07 sinon
        // 0x9c55 (aucun → texte 0x27b), persiste bank_duedate_<bag+1>,
        // répond 389 ClientBagDueDate [s16 bag][i32 unix].
        d.RegisterChar(() => new CNE_CZ_ZoneServerBagDueDate(),
            async (session, msg, charId, token) =>
            {
                var bag = msg.Field1;
                if ((ushort)bag >= 4)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutput
                    {
                        Field1 = 0x9b,        // u8  : canal système natif
                        Field2 = "Index invalid",
                    }, token);
                    return;
                }
                if (msg.Field2 >= 0x278d01) return;
                var now = (int)DateTimeOffset.UtcNow
                    .ToUnixTimeSeconds();
                var dues = await s.Characters
                    .GetBankDueDatesAsync(charId);
                var due = dues[bag];
                if (now < due && due - now > 0x4f1a00)
                {
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x27a, Field2 = "" }, token);
                    return;
                }
                var newDue = Math.Max(due, now) + (int)msg.Field2;
                if (!await ConsumeItemByIdAsync(s, session, charId,
                        0x9d07, token) &&
                    !await ConsumeItemByIdAsync(s, session, charId,
                        0x9c55, token))
                {
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x27b, Field2 = "" }, token);
                    return;
                }
                await s.Characters.SetBankDueDateAsync(charId, bag,
                    newDue);
                await session.SendAsync(new CNC_CZ_ClientBagDueDate
                {
                    Field1 = bag,             // s16 : sac banque (0-3)
                    Field2 = newDue,          // i32 : échéance unix
                }, token);
            });

        // 134 QueryPlayerCard [u32 targetCharId] — désasm
        // CPlayerFactory::QueryPlayerCard @0x81037d0 : si la carte de
        // la cible n'est pas chargée → CDBAgent::Command
        // "Query PlayerCard player_id = %d" ; la mission répond 104
        // (nom/level/class/classLv/guilde/12 stats/intro) → 491.
        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryPlayerCard(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync(
                        (uint)charId,
                        $"Query PlayerCard player_id = {msg.Value}",
                        token);
            });

        // 169 PlayerCardUpdate [s16 op][s16 val][str intro] — désasm
        // CPlayerFactory::PlayerCardUpdate @0x8103xxx : op 1-12 =
        // stat card+0..22, 0xd = introduct (card+0x1c), 0xe =
        // is_public (card+0x18) ; puis 703 → mission UPDATE
        // player_card. La zone relit la ligne, applique l'op et
        // renvoie la carte complète (le natif garde la carte en
        // mémoire char+0xb64 — chez nous la DB est la source).
        d.RegisterChar(() => new CNE_CZ_ZoneServerPlayerCardUpdate(),
            async (session, msg, charId, token) =>
            {
                var card = await s.Characters.GetCardRowAsync(charId)
                    ?? Eden.Core.Database.Repositories
                        .CharacterRepository.CardRow.Empty;
                var stats = card.Stats;
                var intro = card.Intro;
                var pub = card.IsPublic;
                var op = msg.Field1;
                if (op is >= 1 and <= 12) stats[op - 1] = msg.Field2;
                else if (op == 0xd) intro = msg.Field3;
                else if (op == 0xe) pub = msg.Field2 != 0;
                else return;                    // op inconnu : silence
                if (s.Mission?.Connected == true)
                    await s.Mission.SendUpdatePlayerCardAsync(
                        (uint)charId, stats, intro, pub, token);
            });

        // VictoryDeclaration [byte, string] — déclaration de victoire
        d.RegisterChar(() => new CNE_CZ_ZoneServerVictoryDeclaration(),
            (session, msg, charId, token) =>
            {
                // TODO(phase reverse): la classe CNC de broadcast exacte reste
                // à identifier dans le binaire (probablement via la guerre)
                EdenLog.Write($"[Zone] VictoryDecl char={charId}");
                return Task.CompletedTask;
            });

        // --- Inspection d'équipement ---
        // LookEquip [int targetCharId] — voir l'équipement d'un joueur
        d.RegisterChar(() => new CNE_CZ_ZoneServerLookEquip(),
            async (session, msg, charId, token) =>
            {
                var items = await s.InventoryRepo.GetItemsByCharacterIdAsync(
                    msg.Value);
                var resp = new CNC_CZ_ClientLookEquip { CharId = msg.Value };
                foreach (var item in items.Where(i => i.IsEquipped))
                    resp.Entries.Add(new CNC_CZ_ClientLookEquip.Entry
                    {
                        ItemUid = (ushort)item.ItemId,
                    });
                await session.SendAsync(resp, token);
            });

        // PVPRankLookEquip — intercepté par TryHandleOnLoopAsync
        // (case 231 → ZoneEntryFlow.PvpRankEquip) : résolution du
        // rangé via le cache RankInfos (Cols[4] = player_id) + DB,
        // émission du 571 wire-exact. Ce no-op n'est jamais atteint.
        d.RegisterChar(() => new CNE_CZ_ZoneServerPVPRankLookEquip(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Mail ---
        // MailQuery (96) est enregistré dans SocialHandlers : le natif
        // relaie à la mission via AgentDBACommand (NCID 21) — pas de
        // réponse locale 400 (crash client constaté sur réponse
        // immédiate ; le flux natif attend la réponse mission 68).

        // --- PlayerWork (métier de récolte actif) ---
        // 170/171/172 traités par ZoneEntryFlow.Work (TryHandleAsync
        // court-circuite avant ce dispatcher) — ne pas les réenregistrer
        // ici : un second handler doublerait la logique.

        // --- Requêtes diverses ---
        // QueryBattlefieldData — intercepté par TryHandleOnLoopAsync
        // (162 → PlayerGuide.cs : émet 482 avec les 26 compteurs
        // battlefield_month/career).
        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryBattlefieldData(),
            (session, msg, charId, token) => Task.CompletedTask);

        // 223 QueryFortuneBagCounter [] — intercepté par
        // ZoneEntryFlow (HandleFortuneCounterQueryAsync → 557 avec
        // toute la map char+0x574 ; Execute natif @0x08277f00).
        // Enregistrement inerte (documentation NCID).
        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryFortuneBagCounter(),
            (session, msg, charId, token) => Task.CompletedTask);

        // RankInfoQuery [s16 type][s16 ver] — intercepté par
        // ZoneEntryFlow (HandleRankInfoQueryAsync : 521 depuis le
        // cache rempli par les pushes ZMis 763, check version natif).
        // Enregistrement inerte (documentation NCID).
        d.RegisterChar(() => new CNE_CZ_ZoneServerRankInfoQuery(),
            (session, msg, charId, token) => Task.CompletedTask);

        // 558 QueryRecommendActivities [u8 flag][u16 id] — intercepté
        // par ZoneEntryFlow (TryHandleAsync, besoin d'EntryState) :
        // flag=0 → 559 ; flag!=0 → CheckFinished natif @0x840be30.
        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryRecommendActivities(),
            (session, msg, charId, token) => Task.CompletedTask);

        // 226 SetRecommendActivityTracing [u16 id][u16 flag] — désasm
        // Execute @zone : relais mission
        // CNC_ZMis_MissionServerSetRecommendActivityTracing
        // [i32 charUid][u16][u16] (notre NCID interne 835) → la mission
        // UPDATE recommend_activities.is_tracing (SQL natif
        // GetRecommendActivityTracingSQLCmd @0x832e750).
        d.RegisterChar(() => new CNE_CZ_ZoneServerSetRecommendActivityTracing(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendRecommendActivityTracingAsync(
                        (uint)charId, msg.Field1, msg.Field2, token);
            });

        // --- Recherche LFG ---
        // SearchOpt [s16 v1][s16 v2] — TC_SearchConditionOpt natif :
        // v1 → flag LFG (exec+0xe3), v2 → visible dans la recherche
        // (exec+0xe2, exigé ≠0 pour être listé).
        d.RegisterChar(() => new CNE_CZ_ZoneServerSearchOpt(),
            (session, msg, charId, token) =>
            {
                session.SearchLfg = msg.Field1 != 0;
                session.SearchVisible = msg.Field2 != 0;
                return Task.CompletedTask;
            });

        // SearchCondition [s16 upperLv][s16 lowerLv][s16 ?][str
        // selectClass][s16 option] — TC_SearchCondition natif :
        // parcourt tous les joueurs en ligne, exclut le demandeur,
        // filtre niveau ∈ [lower,upper], option bit0→LFG requis,
        // bit1→carte publique, selectClass = bitmask par class_id,
        // cap 50 → 411 ClientSearchInfo [s16 0][s16 n][records].
        d.RegisterChar(() => new CNE_CZ_ZoneServerSearchCondition(),
            async (session, msg, charId, token) =>
            {
                var upper = msg.Field1;      // s16 +0x12 : level max
                var lower = msg.Field2;      // s16 +0x14 : level min
                var selectClass = msg.Field4;// str +0x18 : bitmask classes
                var option = (ushort)msg.Field5; // s16 +0x1c : bit0=LFG, bit1=public

                var online = s.Sessions.All
                    .Where(x => x.CharacterId is { } id
                                && id != charId
                                && x.SearchVisible)
                    .Select(x => x.CharacterId!.Value)
                    .ToArray();
                var cards = await s.Characters.GetPlayerCardsAsync(online);
                var entries = new List<Eden.Core.Game.Social.SocialPackets.SearchEntry>(
                    Eden.Core.Game.Social.SocialPackets.SearchResultCap);
                foreach (var c in cards)
                {
                    if (entries.Count >= Eden.Core.Game.Social
                            .SocialPackets.SearchResultCap) break;
                    var cand = s.Sessions.FindByCharacter(c.CharId);
                    if (cand == null) continue;
                    if (c.Level > upper || lower > c.Level) continue;
                    if ((option & 1) != 0 && !cand.SearchLfg) continue;
                    // bit1 → carte publique : is_public=1 par défaut
                    // natif (player_card toujours provisionnée).
                    // selectClass natif : class_id <= len ET
                    // (class_id == len OU str[class_id] != '0').
                    var classOk = c.ClassId <= selectClass.Length
                        && (c.ClassId == selectClass.Length
                            || selectClass[c.ClassId] != '0');
                    if (!classOk) continue;
                    entries.Add(new Eden.Core.Game.Social.SocialPackets
                        .SearchEntry(c.CharId, c.Name, c.Level,
                            (ushort)c.ClassId, c.ClassLv, c.NodeId, 1));
                }
                await session.SendPacketAsync(
                    Eden.Core.Game.Social.SocialPackets.NcidClientSearchInfo,
                    Eden.Core.Game.Social.SocialPackets.BuildSearchInfo(entries),
                    token);
            });

        // SearchTeam [u8×5 filtres] — désasm
        // CNE_CZ_ZoneServerSearchTeam::Execute @0x8285060 : le natif
        // relaie à la mission (textex « RecruitTeamSearchTeam »
        // [u32 charUid][u8 ×5]) qui répond ZoneServerSearchInfo (83)
        // → ClientSearchTeamInfo (518). Source native = le store de
        // recrutement mission ; équivalent local =
        // TeamService._recruits → seules les annonces actives, pas
        // toutes les équipes.
        d.RegisterChar(() => new CNE_CZ_ZoneServerSearchTeam(),
            async (session, msg, charId, token) =>
            {
                var resp = new CNC_CZ_ClientSearchTeamInfo();
                foreach (var post in s.Teams.SearchRecruits(0, 0, ""))
                {
                    var t = new CNC_CZ_ClientSearchTeamInfo.TeamEntry
                    {
                        Comment1 = post.Message,
                    };
                    var team = post.TeamId != 0
                        ? s.Teams.GetTeam(post.TeamId) : null;
                    if (team != null)
                        foreach (var m in team.Members)
                            t.Members.Add(
                                new CNC_CZ_ClientSearchTeamInfo.MemberEntry
                                {
                                    CharId = m.CharacterId,
                                    CharName = m.Name,
                                    Level = (short)m.Level,
                                    ClassId = (short)m.ClassId,
                                });
                    else if (await TeamHandlers.SoloMemberAsync(s,
                        post.OwnerCharId) is { } solo)
                        t.Members.Add(
                            new CNC_CZ_ClientSearchTeamInfo.MemberEntry
                            {
                                CharId = solo.CharId,
                                CharName = solo.Name,
                                Level = solo.Level,
                                ClassId = solo.ClassId,
                            });
                    if (t.Members.Count > 0)
                        resp.Teams.Add(t);
                }
                await session.SendAsync(resp, token);
            });

        // --- Team divers ---
        // RecruitInstance — implémenté dans ZoneEntryFlow.Recruit.cs
        // (case 220 du TryHandleOnLoopAsync : file LFG + matching
        // règles natives 3/5/8 + transport aire 21).


        // TeamRadarAttention [s16][s16][s16] — désasm
        // CNE_CZ_ZoneServerTeamRadarAttention::Execute @0x82a6xxx :
        // pour chaque membre IsA==1 (joueur) de la team dont le node
        // (CCharacter+0x1c) égale celui de l'émetteur →
        // NotifyClient ClientTeamRadarAttention (387) avec les 3 s16
        // recopiés (ping radar : type/x/z). L'émetteur reçoit aussi
        // la copie (il est membre et sur le même node).
        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamRadarAttention(),
            async (session, msg, charId, token) =>
            {
                var team = s.Teams.GetTeamOf(charId);
                var me = team?.Members.Find(m => m.CharacterId == charId);
                if (team == null || me == null) return;
                foreach (var m in team.Members)
                    if (m.IsOnline && m.MapId == me.MapId &&
                        s.Sessions.FindByCharacter(m.CharacterId) is { } ms)
                        await ms.SendAsync(
                            new CNC_CZ_ClientTeamRadarAttention
                            {
                                Field1 = msg.Field1, // s16 : type ping
                                Field2 = msg.Field2, // s16 : coord x
                                Field3 = msg.Field3, // s16 : coord z
                            }, token);
            });

        // TeamRecruitMsg [string×4, byte×2, short×2] — message de recrutement
        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamRecruitMsg(),
            (session, msg, charId, token) =>
                s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientTeamRecruitMsg
                {
                    Field1 = msg.Field1,
                    Field2 = msg.Field2,
                }, token));

        // TeamRecruitTransport [] — désasm Execute @0x828xxxx : leader
        // uniquement → CForceTeam::RecruitTransportV2 @0x8475660 :
        // exige l'id d'instance de recrutement (team+0x88, posé par
        // TeamRecruitV2) ; instance normale → team complète requise
        // (size == S_InstanceData+0x74) puis textex mission
        // « RecruitTransportV2 » [u32 leaderUid] ; type 0xc → raid25
        // (CreateRaid25Team / EnterR25Instance, map-type checks).
        // Chez nous : leader-gate + post actif exigé ; le transport
        // d'instance proprement dit exige le sous-système d'instances
        // (NCID 207 EnterRoom…) — non porté → l'annonce est
        // consommée et rien n'est téléporté.
        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamRecruitTransport(),
            (session, msg, charId, token) =>
            {
                var team = s.Teams.GetTeamOf(charId);
                if (team == null || team.LeaderCharacterId != charId)
                    return Task.CompletedTask;
                var post = s.Teams.SearchRecruits(0, 0, "")
                    .FirstOrDefault(p => p.TeamId == team.Id);
                if (post == null) return Task.CompletedTask;
                EdenLog.Write($"[Zone] RecruitTransport char={charId} " +
                    "team={team.Id} — transport d'instance non porté");
                return Task.CompletedTask;
            });

        // --- Aptitudes avancées (S_Talent) ---
        // 148 SetAAPoint / 149 SetAARate : interceptés par
        // ZoneEntryFlow (HandleSetAaPointAsync — AddAAEffectPoint
        // natif @0x841ebb0 : arbres S_Talent, AARequestTable,
        // pool aa_point de player_classlist, 471 + attr 0x31).

        // EnterBattlefieldAnswer [short bfId, short accept] —
        // accept → relaye le join à la mission (op149, même flux
        // que le 144) ; le match/dungeon solo reste le repli si la
        // mission est hors ligne.
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterBattlefieldAnswer(),
            async (session, msg, charId, token) =>
            {
                if (msg.Field2 == 1)
                {
                    if (s.Mission is { Connected: true } mission)
                    {
                        var b = new byte[8];
                        BitConverter.GetBytes(charId).CopyTo(b, 0);
                        BitConverter.GetBytes(msg.Field1).CopyTo(b, 4);
                        BitConverter.GetBytes((short)-1).CopyTo(b, 6);
                        await mission.SendAsync(149, b, token);
                        return;
                    }
                    var pc = await s.Characters.GetCharacterByIdAsync(charId);
                    s.Dungeons.Enter(msg.Field1, new[] { charId },
                        pc?.Level ?? 1);
                }
            });

        // StrengthenClose (120) — intercepté par ZoneEntryFlow
        // (TryHandleAsync) : ResetOperation + 434 si cast en cours
        // (ZoneEntryFlow.Strengthen.cs).
    }

    /// <summary>`CItemFactory::DropItemByItemID(0, id, 1, 0x10)` natif :
    /// consomme 1 exemplaire de `itemId` dans les sacs (premier slot non
    /// équipé trouvé) + notification 275 [s16 sac][s16 loc][s16 n].
    /// Retourne false si le joueur n'en possède pas.</summary>
    internal static async Task<bool> ConsumeItemByIdAsync(
        ZoneServices s, NetSession session,
        int charId, int itemId, CancellationToken token)
    {
        var items = await s.InventoryRepo
            .GetItemsByCharacterIdAsync(charId);
        var it = items.FirstOrDefault(
            i => i.ItemId == itemId && !i.IsEquipped && i.Quantity > 0);
        if (it == null) return false;
        if (!await s.Inventory.RemoveItemAsync(
                charId, it.BagId, it.Slot, 1))
            return false;
        // 275 ClientDropItem [s16 sac][s16 loc][s16 n]
        await session.SendPacketAsync(275,
            Build275(it.BagId, it.Slot, 1), token);
        s.NotifyItemChanged(charId, itemId);
        return true;
    }

    private static byte[] Build275(int bag, int slot, int n)
    {
        var b = new byte[6];
        BitConverter.GetBytes((short)bag).CopyTo(b, 0);
        BitConverter.GetBytes((short)slot).CopyTo(b, 2);
        BitConverter.GetBytes((short)n).CopyTo(b, 4);
        return b;
    }
}
