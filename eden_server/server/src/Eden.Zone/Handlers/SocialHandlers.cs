using Eden.Core.Database.Repositories;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Social : mail (7 commandes), guilde/famille, couple, chat canal,
/// duels, changement de classe.
/// </summary>
public static class SocialHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // --- Mail : le natif relaie TOUTES les opérations à la couche
        // DBA de la mission via CDBAgent::Command -> AgentDBACommand
        // (zone->mission NCID 21 : [i32 charUid][str cmd][str "command"],
        // désasm CNC_DBA_AgentDBACommand::Serialize @0x807dc40 et
        // MailQuery::Execute @0x8265000). La mission répond par les
        // NCIDs 68-77, routés vers 400-409 dans ZoneEntryFlow.Mission.
        // Les formats de commandes viennent des strings .rodata de la
        // zone native (0x7e37a8..0x822ac8) et des formats d'aide de la
        // mission (0x4a04d0..0x4a51bd).

        // 99 MailSend [str dest][str titre][str corps][s16 bag]
        // [s16 slot][i32 or][i32 COD][u8 flag] — désasm
        // CNE_CZ_ZoneServerMailSend::Execute @0x82657f0 :
        //   - niveau ≥ S_Parameter 377 (AllFormulaParas+0x5e4 = 20)
        //   - item joint (bag/slot ≥ 0) non lié et non équipé
        //   - or joint (Field6) + taxe fixe 4 ≤ or du perso (+0x4b0)
        //   → la mission insère player_mail et déplace l'item ; chez
        //   nous la zone fait le repo local + deltas client.
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailSend(),
            async (session, msg, charId, token) =>
            {
                async Task FailAsync(string why)
                {
                    EdenLog.Write(
                        $"[Zone] MailSend char={charId} refusé : {why}");
                    await session.SendAsync(
                        new CNC_CZ_ClientMailSendResult { Value = 1 },
                        token);
                }

                var target = await s.Characters.GetCharacterByNameAsync(
                    msg.Field1);
                if (target == null)
                {
                    await FailAsync($"'{msg.Field1}' inconnu");
                    return;
                }
                var character = await s.Characters
                    .GetCharacterByIdAsync(charId);
                if (character == null) return;

                // Niveau minimum : S_Parameter id 377 = 20
                // (AllFormulaParas+0x5e4, "使用郵局功能最低等級限制").
                var minLevel = (int)s.GameData.Parameters.Get(377);
                if (minLevel > 0 && character.Level < minLevel)
                {
                    await FailAsync($"niveau {character.Level} < {minLevel}");
                    return;
                }

                // Or : joint + taxe fixe de 4 (natif :
                // attach+4 ≤ char+0x4b0).
                var attach = Math.Max(0, msg.Field6);
                var gold = await s.Characters.GetGoldAsync(charId);
                if (attach + 4 > gold)
                {
                    await FailAsync($"or insuffisant ({gold})");
                    return;
                }

                // Item joint : slot ≥ 0 → item requis, non équipé,
                // non lié (flag bind — natif +0xf0&8 / +0x24&0x10).
                Eden.Core.Game.Inventory.ItemEntity? attachItem = null;
                if (msg.Field5 >= 0)
                {
                    attachItem = await s.InventoryRepo.GetItemAsync(
                        charId, msg.Field4, msg.Field5);
                    if (attachItem == null || attachItem.IsEquipped ||
                        attachItem.IsBound)
                    {
                        await FailAsync("item joint invalide/lié");
                        return;
                    }
                }

                var mail = new MailRow
                {
                    RecipientId = target.Id,
                    SenderId = charId,
                    SenderName = character.GivenName ?? $"#{charId}",
                    Subject = msg.Field2,
                    Body = msg.Field3,
                    AttachedGold = attach,
                    AttachedItemId = attachItem?.ItemId ?? 0,
                    CodAmount = Math.Max(0, msg.Field7),
                    SentAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                };
                // UNE transaction : débit gardé (or joint + taxe 4),
                // INSERT player_mail, move inventory→mailitem de la
                // pièce jointe — avant, les trois écritures étaient
                // éclatées sur autant de connexions (mail orphelin ou
                // item débité sans attachement au crash) et le débit
                // absolu perdait les écritures concurrentes.
                var sent = await s.Mail.SendMailAsync(mail, charId,
                    attachItem != null ? msg.Field4 : -1, msg.Field5,
                    attach + 4);
                if (sent == null)
                {
                    await FailAsync("débit/déplacement refusé en tx");
                    return;
                }
                var (mailId, newGold) = sent.Value;

                // Item joint : 275 ClientDropItem côté client.
                if (attachItem != null)
                    await session.SendAsync(new CNC_CZ_ClientDropItem
                    {
                        Field1 = 0,                    // s16 contType sac
                        Field2 = msg.Field5,           // s16 slot vidé
                        Field3 = (short)msg.Field4,    // s16 contIdx
                    }, token);

                // 280 attr 0x02 — solde réel rendu par la tx.
                await session.SendAsync(new CNC_CZ_ClientUpdateAttr
                {
                    Field1 = (int)charId, Field2 = 2,
                    Field3 = (int)Math.Min(newGold, int.MaxValue),
                    Field4 = 0,
                }, token);

                await session.SendAsync(
                    new CNC_CZ_ClientMailSendResult { Value = 0 }, token);
                EdenLog.Write($"[Zone] Mail #{mailId} {charId}->{target.Id} " +
                    $"gold={attach} cod={mail.CodAmount} item={attachItem?.ItemId ?? 0}");
            });

        // 95 MailOpenBox [s16 op] — désasm CNE_CZ_ZoneServerMailOpenBox::
        // Execute @0x8265510 : dispatch sur l'op s16 :
        //   op=7    -> ResetOperation + NotifyClient ClientMailOpenBox
        //              (399, +0xc=0 -> corps [i32 0] — Serializer
        //              @0x81e3600) ;
        //   op=22/150/151/152 -> ResetOperation + ComposeOutput
        //              CNC_CZ_ClientEnterStrengthen (432, vtbl 0x87c990c,
        //              corps [i32 0][s16 op][u16 0][s16 ?][s16 flag] —
        //              Serializer @0x80c3940 : i32/s16/u16/s16/s16).
        // Désasm : op=22 @0x82657f0 -> 432 +c=-1 ; op=150 @0x8265688 ->
        // 432 +c=-1, +e=0 ; op=151 @0x82658c0 -> 432 +c=2 ; op=152
        // @0x826559a -> 432 +c=-1, +e=octet global 0x89b9880 (flag
        // itemmall — 0 par défaut). Sans le 432 le client attend une
        // réponse à l'ouverture de la boîte et reste instable.
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailOpenBox(),
            async (session, msg, charId, token) =>
            {
                switch (msg.Value)
                {
                    case 7:
                        await session.SendAsync(
                            new CNC_CZ_ClientMailOpenBox { Value = 0 },
                            token);
                        EdenLog.Write(
                            $"[Zone] MailOpenBox char={charId}");
                        break;
                    case 22 or 150 or 151 or 152:
                        await session.SendAsync(
                            new CNC_CZ_ClientEnterStrengthen
                            {
                                Field1 = 0,                 // i32 : 0
                                Field2 = msg.Value,         // s16 : op
                                Field3 = 0,                 // u16 : 0
                                Field4 = (short)(msg.Value == 151 ? 2 : -1),
                                                            // s16 : 2 si op 151
                                Field5 = 0,                 // s16 : flag itemmall
                            }, token);
                        EdenLog.Write(
                            $"[Zone] MailOpenBox char={charId} op={msg.Value} -> 432");
                        break;
                    default:
                        EdenLog.Write(
                            $"[Zone] MailOpenBox char={charId} op={msg.Value} ignoré");
                        break;
                }
            });

        // 96 MailQuery [s16 page][s16 acceptOnlyFriend]
        // Natif : MailQuery::Execute @0x8265000 -> LPrintf("MailQuery
        // page_number = %hd accept_only_friend_mail = %hd") ->
        // ComposeOutput sur le lien mission. AUCUNE réponse locale :
        // la liste (400) arrive via la réponse mission 68.
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailQuery(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailQuery page_number = {msg.Field1} " +
                        $"accept_only_friend_mail = {msg.Field2}", token);
                else
                    EdenLog.Write("[Zone] MailQuery sans lien mission");
            });

        // 97 MailRead [i32 mailId] -> "MailRead mail_id = %d"
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailRead(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailRead mail_id = {msg.Value}", token);
            });

        // 100 MailGetItem [i32 mailId][u16 n][n×i32 ids][s16 freeNeeded]
        // — désasm Execute @0x8264870 : mailId != 0 → commande texte
        // "MailGetItem mail_id = %d inv_loc = -1 con_index = -1
        // b_return = 0 merge = 0" (rodata 0x7e3d58) + mailId empilé
        // dans le vecteur « en cours » du perso ; mailId == 0 → lot :
        // exige freeNeeded slots libres (texte 0x4f9 sinon) puis
        // NotifyMissionCmd("MailPatchGet") [u32 uid][u16 n][n×u32] —
        // notre équivalent : une commande MailGetItem b_return=1 par
        // id, la mission sert 75→405+274 par pièce.
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailGetItem(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected != true) return;
                if (msg.MailId != 0)
                {
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailGetItem mail_id = {msg.MailId} " +
                        "inv_loc = -1 con_index = -1 " +
                        "b_return = 0 merge = 0", token);
                    return;
                }
                foreach (var id in msg.MailIds)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailGetItem mail_id = {id} " +
                        "inv_loc = -1 con_index = -1 " +
                        "b_return = 1 merge = 0", token);
            });

        // 101 MailGetGold [i32 mailId] -> "MailGetGold mail_id = %d"
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailGetGold(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailGetGold mail_id = {msg.Value}", token);
            });

        // 98 MailDelete [i32 mailId][u16 n][n×i32 ids] -> "MailDelete
        // mail_id = %d" par id. Le chemin natif lot (mailId==0) utilise
        // NotifyMissionCmd("MailPatchDelete") [u32 charUid][u16 n]
        // [n×u32] — équivalent local : une commande par id, la mission
        // renvoie un 70→402 par suppression (désasm Execute @0x8263b40).
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailDelete(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected != true) return;
                if (msg.MailId != 0)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailDelete mail_id = {msg.MailId}", token);
                foreach (var id in msg.MailIds)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailDelete mail_id = {id}", token);
            });

        // 102 MailReturn [i32 mailId] -> "MailReturn mail_id = %d"
        d.RegisterChar(() => new CNE_CZ_ZoneServerMailReturn(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MailReturn mail_id = {msg.Value}", token);
            });

        // --- Famille/Guilde : création, donation, emblème ---
        // Création : fil natif Zone->World (CNE_ZW_WorldServerFamily-
        // Creation 143) — le monde possède l'état famille (tables
        // `family` natives) puis pousse NewFamily (ZW 32) en retour ;
        // c'est la zone qui débite les 50 000 or et émet le 346.
        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyCreate(),
            async (session, msg, charId, token) =>
            {
                if (s.World is { Connected: true } w)
                    await w.SendFamilyCreationAsync(charId, msg.Value, token);
                else
                    EdenLog.Write($"[Zone] FamilyCreate '{msg.Value}' " +
                        $"char={charId} — lien monde absent, ignoré");
            });

        // Don trésorerie — Execute natif : [i32 amount] ; vérifs
        // dans l'ordre natif : or insuffisant → texte 0x388, plafond
        // trésorerie → 0x370, sinon GainGold(-amount) + treasury+=.
        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyDonateMoney(),
            async (session, msg, charId, token) =>
            {
                var gold = await s.Characters.GetGoldAsync(charId);
                var err = s.Guilds.CanDonate(charId, msg.Value, gold);
                if (err < 0) return;
                if (err > 0)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                        { Field1 = (short)err, Field2 = string.Empty },
                        token);
                    return;
                }
                await s.Characters.AddGoldAsync(charId, -msg.Value);
                await s.Guilds.DonateAsync(charId, msg.Value);
                // Le monde est l'autorité trésorerie (InstanceCreate
                // la lit) — sans ce relais le gate 0x6ae refuse à tort.
                if (s.World is { Connected: true } w
                    && s.Guilds.GetGuildOf(charId) is { } gd)
                    await w.SendTextCommandAsync(
                        $"family_donate {gd.Id} {msg.Value}", token);
                EdenLog.Write($"[Zone] char={charId} donne {msg.Value} à la famille");
            });

        // Emblème : fil natif zone → texte ZW
        // « set_family_emblem <charId> <emblem> » → monde
        // FamilySetEmblem → `family.emblem` (le cache zone est
        // rafraîchi par le prochain push 346/ChangeFamily).
        d.RegisterChar(() => new CNE_CZ_ZoneServerSetFamilyEmblem(),
            async (session, msg, charId, token) =>
            {
                var guild = s.Guilds.GetGuildOf(charId);
                if (guild == null || guild.LeaderCharacterId != charId) return;
                guild.Emblem = msg.Field1;
                if (s.World is { Connected: true } w)
                    await w.SendTextCommandAsync(
                        $"set_family_emblem {charId} {msg.Field1}", token);
            });

        // --- Couple : invitation (targetId, nom, ?), message, rupture, info ---
        d.RegisterChar(() => new CNE_CZ_ZoneServerLoverInvite(),
            async (session, msg, charId, token) =>
            {
                s.Lovers.Propose(charId, (int)msg.Field1);
                // Si le partenaire avait déjà proposé → couple formé
                if (await s.Lovers.AcceptAsync((int)msg.Field1, charId) != null)
                    EdenLog.Write($"[Zone] Couple formé {charId} <-> {msg.Field1}");
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerLoverMessage(),
            (session, msg, charId, token) =>
            {
                if (s.Lovers.GetPartnerId(charId) is { } partnerId)
                    EdenLog.Write($"[Zone] LoverMessage {charId}->{partnerId}: {msg.Value}");
                return Task.CompletedTask;
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerLoverBreak(),
            async (session, msg, charId, token)
                => await s.Lovers.BreakupAsync(charId));

        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryLoverInfo(),
            (session, msg, charId, token) =>
            {
                if (s.Lovers.GetCouple(charId) is { } couple)
                    EdenLog.Write($"[Zone] QueryLoverInfo char={charId} " +
                        $"partner={s.Lovers.GetPartnerId(charId)} married={couple.IsMarried}");
                return Task.CompletedTask;
            });

        // --- Chat canal : (byte channel, string from, string msg, type, lang, senderId) ---
        d.RegisterChar(() => new CNE_CZ_ZoneServerChannelMsg(),
            async (session, msg, charId, token) =>
            {
                var ch = await s.Characters.GetCharacterByIdAsync(charId);
                // Canal 0 = zone entière ; canaux guilde/groupe à filtrer
                await s.Sessions.BroadcastAllAsync(new CNC_CZ_ClientChannelMsg
                {
                    Field1 = msg.ChannelId,
                    Field2 = ch?.GivenName ?? msg.From,
                    Field3 = msg.Message,
                    Blob = BitConverter.GetBytes(charId),
                }, token);
            });

        // --- Duels : gérés dans ZoneEntryFlow.Duel.cs (cases 135/136)
        // — besoin d'EntryState : pending ops, activation countdown 5 s,
        // résultats natifs (mort/forfait/timeout %HP).

        // --- Mariage ---
        // MarryOperation [short op, short ?] : 1=proposer 2=accepter 3=rompre
        d.RegisterChar(() => new CNE_CZ_ZoneServerMarryOperation(),
            async (session, msg, charId, token) =>
            {
                if (msg.Field1 == 1 && await s.Lovers.MarryAsync(charId))
                    await session.SendAsync(new CNC_CZ_ClientMarryOperation
                    {
                        Value = 1, // succès
                    }, token);
                EdenLog.Write($"[Zone] MarryOp char={charId} op={msg.Field1}");
            });

        // 251 WeddingInfoQuery — liste des cérémonies de mariage.
        // Natif (CNE_CZ_ZoneServerWeddingInfoQuery::Execute) :
        // CDBAgent::Command(charUid, "WeddingInfoQuery 1") → NCID 21,
        // AUCUNE réponse locale — la liste arrive via ZMis 193 → 599.
        d.RegisterChar(() => new CNE_CZ_ZoneServerWeddingInfoQuery(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        "WeddingInfoQuery 1", token);
                else
                    // Sans mission : liste vide — 599 wire-exact
                    // [u8 count=0] (la classe générée est un record
                    // fixe, inutilisable pour une liste).
                    await session.SendPacketAsync(599, new byte[] { 0 },
                        token);
            });

        // --- Dialogues / missions avancés ---
        // ReplyDialog [short choice] — réponse à une boîte de dialogue PNJ
        d.RegisterChar(() => new CNE_CZ_ZoneServerReplyDialog(),
            (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] ReplyDialog char={charId} choice={msg.DialogId}");
                // TODO(phase reverse): résoudre le dialogue actif du joueur
                // et exécuter le script associé (quête/shop/service)
                return Task.CompletedTask;
            });

        // MissionGoto [short node, short x, short y, short ?] — marker de quête
        d.RegisterChar(() => new CNE_CZ_ZoneServerMissionGoto(),
            (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] MissionGoto char={charId} node={msg.Field1} ({msg.Field2},{msg.Field3})");
                return Task.CompletedTask;
            });

        // 87 MissionListOp [s16 op][i32 missionId][u16 npcUid] — clic sur
        // une entrée de la liste missions/dialogue (le client l'émet
        // après ReplyDialog — capture native : `0200 f4010000 ffff`).
        // Natif : CNE_CZ_ZoneServerMissionListOp::Execute @0x826aa00 ->
        // CMissionListOp::DoOperation @0x856e7f0 :
        //   op < 6  → CDBAgent::Command(charUid,
        //             "MissionServiceOp %hd %hd %hd") = (mid, op, npcUid)
        //             relayé wire mission NCID 21 — accept/reward/…
        //   op == 9 → clic sur une quête de la liste « Available quests »
        //             du HUD (client @0x82d570 : envoie {9, mid, 0xffff}) :
        //             ResetOperation(CMissionListOp op=1, mid, cible 0)
        //             puis NotifyClient 388 EnterMissionList
        //             {s16 op=1, i32 mid, i32 0, s16 0} — le client ouvre
        //             la fiche de la quête (offre). L'accept qui suit
        //             revient en op=1 → relayé comme les autres op<6.
        //   op 6/7  → partage de quête en ForceTeam
        //             (ZoneEntryFlow.QuestShare.cs : op 6 sollicite les
        //             membres locaux via MissionServiceOp mid 6 <membre>,
        //             op 7 = réponse du destinataire).
        //   autres  → ignoré natif.
        d.RegisterChar(() => new CNE_CZ_ZoneServerMissionListOp(),
            async (session, msg, charId, token) =>
            {
                var op = msg.Field1;
                // Partage de quête (op 6/7) et garde « quête en cours
                // de partage » des ops < 6 — consommés côté flux.
                if (s.MissionShareOp != null && op != 9 &&
                    await s.MissionShareOp(session, charId, op,
                        (short)msg.Field2, (short)msg.Field3, token))
                {
                    EdenLog.Write($"[Zone] MissionListOp char={charId} " +
                        $"op={op} mid={msg.Field2} arg={msg.Field3} " +
                        "(partage)");
                    return;
                }
                if (op == 9)
                {
                    // 388 [s16 op=1][i32 mid][i32 npcUid=0][s16 0] —
                    // ctor natif dans DoOperation @0x856ec60 :
                    // piVar[1]=1, piVar[2]=mid, piVar[3]=0, piVar[4]=0.
                    await session.SendAsync(
                        new CNC_CZ_ClientEnterMissionList
                        {
                            Field1 = 1, Field2 = msg.Field2,
                            Field3 = 0, Field4 = 0,
                        }, token);
                }
                else if (op < 6 && s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"MissionServiceOp {(short)msg.Field2} " +
                        $"{op} {(short)msg.Field3}", token);
                EdenLog.Write($"[Zone] MissionListOp char={charId} " +
                    $"op={op} mid={msg.Field2} npc={msg.Field3}");
            });
    }
}
