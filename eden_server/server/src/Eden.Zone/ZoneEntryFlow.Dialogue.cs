using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    private static byte[] EncodeDialogueText(string value)
    {
        var bytes = EdenEncoding.Big5.GetBytes(value ?? string.Empty);
        return bytes.Length == 0 ? new byte[] { 0 } : bytes;
    }

    private byte[] EncodeDialogueTextForId(int id)
    {
        return EncodeDialogueText(DialogueText(id));
    }

    /// <summary>TalkToNPC → CNC_CZ_ClientDisplayDialogView (285) construit
    /// depuis le TalkScript du NPC ("#:Talk titre corps ...").</summary>
    private async Task HandleTalkToNpcAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4 || st.CharId == 0) return;
        var handle = BitConverter.ToUInt32(payload, 0);
        if (!st.HandleToEntity.TryGetValue(handle, out var entityId))
        {
            EdenLog.Write($"[Zone-Entry] TalkToNPC handle inconnu 0x{handle:x8}");
            return;
        }
        // entityId = npcId du template (eid du 298, u16) — la mission
        // attend ce même id comme StarID.
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        var templateId = entityId;

        // Portée d'interaction (TargetInTalkingRange natif) : le client
        // s'approche DE LUI-MÊME puis envoie NCID=10 — la zone ne fait
        // jamais marcher le perso. Hors de portée, on refuse comme le
        // natif (le client renverra la requête une fois arrivé).
        if (st.EntityPos.TryGetValue(handle, out var npos))
        {
            var ddx = npos.X - st.PosX; var ddz = npos.Y - st.PosZ;
            var dist = MathF.Sqrt(ddx * ddx + ddz * ddz);
            if (dist > 8f)
            {
                EdenLog.Write(
                    $"[Zone-Entry] TalkToNPC npc={templateId} " +
                    $"hors portée ({dist:F0}u) — refusé");
                return;
            }
        }
        var npc = _services.GameData.Npcs.FirstOrDefault(n => n.Id == templateId);
        st.DialogBodyIds = Array.Empty<int>();
        st.DialogNpcHandle = (int)handle;
        st.DialogNpcId = templateId;
        st.DialogNpcUid = templateId;
        st.MissionCtx = null;

        // NPC à fonction directe (S_Npc `#fn#arg#` — vérifié : aucun
        // n'a de #:Talk) : le clic ouvre DIRECTEMENT l'UI du service
        // (shop 304, transport 393, banque 357, enchères 430…) — jamais
        // de fenêtre de dialogue. La mission renvoyait 0 entrées pour
        // eux → le 285 vide produisait la "page blanche".
        if (npc is { FuncId: > 0 })
        {
            EdenLog.Write($"[Zone-Entry] TalkToNPC npc={templateId} " +
                $"fonction #{npc.FuncId}#{npc.FuncArg}# — service direct");
            await RunNpcFunctionAsync(session, st, npc.FuncId,
                npc.FuncArg, token);
            return;
        }

        // La mission identifie le NPC par son TEMPLATE id (champ
        // monster->0x324->0x4 côté natif — vérifié par sonde live : les
        // StarID=50002/50297/50362 renvoient les vraies listes, les
        // LocalId scène -1..-N ne renvoient rien).
        st.DialogNpcUid = templateId;

        // Pipeline natif : la zone demande au MissionServer la liste de
        // dialogues du NPC pour ce perso (réponse 56 → 285). Si la mission
        // reste muette (~800 ms), repli sur le gossip statique #:Talk pour
        // ne jamais laisser le client sans interface.
        if (_mission.Connected)
        {
            var seq = ++st.DialogSeq;
            await _mission.SendListDialogueAsync(
                st.CharId, st.NodeId, st.DialogNpcUid, token);
            EdenLog.Write(
                $"[Zone-Entry] TalkToNPC npc={templateId} -> mission 127");
            // Fallback gossip local si la mission ne répond pas sous
            // 800 ms — schedulé sur la boucle (lit st.DialogAcked).
            ScheduleOnLoop(TimeSpan.FromMilliseconds(800), async _ =>
            {
                if (st.DialogAcked < seq && st.DialogNpcId == templateId)
                    await SendLocalGossipAsync(session, st, npc,
                        (int)handle);
            });
            return;
        }
        await SendLocalGossipAsync(session, st, npc, (int)handle);
    }

    /// <summary>285 ClientDisplayDialogView construit depuis le #:Talk local.
    /// Wire natif : [i32 ctx][s16 count][×(i32 id, u16len str, s16)][u8][i32].</summary>
    private async Task SendLocalGossipAsync(NetSession session, EntryState st,
        WorldParsers.NpcDef? npc, int handle)
    {
        var pairs = ParseTalkPairs(npc?.TalkScript);
        st.DialogBodyIds = pairs.Select(p => p.body).ToArray();
        // Les options viennent maintenant du gossip LOCAL — la liste
        // mission précédente ne doit plus intercepter ReplyDialog
        // (sinon un 2e talk renvoyait un index vers des options obsolètes
        // → page blanche).
        st.MissionDialogs.Clear();
        // Les services du NPC (shop/transport/…) deviennent des options
        // locales : DialogServices[index] = (type, refId, dlgId).
        st.DialogServices.Clear();
        var entries = new List<(int id, string text)>();
        foreach (var (title, _) in pairs)
            entries.Add((title, DialogueText(title)));
        if (npc != null)
            foreach (var svc in npc.Services)
            {
                st.DialogServices[entries.Count] =
                    (svc.Type, svc.RefId, svc.DialogId, svc.Extra);
                entries.Add((svc.DialogId, DialogueText(svc.DialogId)));
            }

        // AUCUNE entrée bidon : displayId=0 se résout en "NULL????" dans
        // la T_Dialogue client (bug live confirmé). Le 285 natif accepte
        // 0 entrée — la fenêtre montre le texte parlé seul.
        if (entries.Count == 0 && pairs.Count == 0 &&
            st.MissionCtx is null && npc?.Services.Count is null or 0)
            return;

        // ctx = texte PARLÉ par le NPC (résolu client via T_Dialogue —
        // capture : 4004). Priorité au ctx que la mission vient d'émettre
        // (texte "bonjour" du NPC, ex. 102827) — sinon le 2e 285
        // remplacerait le texte par le corps NULL des NPC sans #:Talk.
        var ctxId = st.MissionCtx ??
            (pairs.Count > 0 ? pairs[0].body : 0);
        var msg = new CNC_CZ_ClientDisplayDialogView
        {
            Field1 = ctxId,                             // i32 : texte parlé
            Field6 = 0,                                 // u8 : type
            Field7 = st.DialogNpcUid,                   // i32 : star id
        };
        foreach (var (id, _) in entries)
            // Nom vide : le client résout le titre via l'id (T_Dialogue),
            // comme les entrées mission dont E est vide.
            msg.Entries.Add(new() { DisplayId = id });
        await session.SendAsync(msg, CancellationToken.None);
        EdenLog.Write($"[Zone-Entry] gossip local npc -> {entries.Count} options ctx={ctxId}");
    }

    /// <summary>ReplyDialog [s16 optionIndex ou id] : relayé au MissionServer
    /// (OnDialogueSelected 128 → réponse 57). Fallback local sur #:Talk.</summary>
    private async Task HandleReplyDialogAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2) return;
        var idx = BitConverter.ToInt16(payload, 0);
        // Flux natif (CNE_CZ_ZoneServerReplyDialog::Execute) : le client
        // n'envoie qu'un index dans missionDialogs ; la zone renvoie à la
        // mission les champs {+0x0 selId, +0x4 a, +0x6 b} de l'entrée
        // choisie — PAS l'id affiché.
        if (_mission.Connected && st.MissionDialogs.Count > 0)
        {
            if (idx < 0 || idx >= st.MissionDialogs.Count) return;
            var dlg = st.MissionDialogs[idx];
            // Option service (S_Npc #:Service <dlgId> <type> <refId>) :
            // matché sur l'id AFFICHÉ — la zone ouvre l'UI correspondante.
            var npcSvc = _services.GameData.Npcs
                .FirstOrDefault(n => n.Id == st.DialogNpcId);
            var svc = npcSvc?.Services
                .FirstOrDefault(s => s.DialogId == dlg.DisplayId);
            if (svc is { DialogId: > 0 } sv)
            {
                await RunNpcServiceAsync(session, st, sv.Type, sv.RefId,
                    sv.Extra, token);
                return;
            }
            await _mission.SendDialogueSelectedAsync(st.CharId, st.NodeId,
                dlg.SelId, dlg.SelA, dlg.SelB, token);
            return;
        }
        // Service local (gossip de repli) : l'option ouvre shop/transport…
        if (st.DialogServices.TryGetValue(idx, out var ls))
        {
            await RunNpcServiceAsync(session, st, ls.Type, ls.Param,
                ls.Extra, token);
            return;
        }
        if (st.DialogBodyIds.Length == 0) return;
        if (idx < 0 || idx >= st.DialogBodyIds.Length) return;

        // Corps de réponse : ctx = le texte dit (T_Dialogue id du corps),
        // une entrée vide, tail = star id du NPC.
        await session.SendAsync(new CNC_CZ_ClientDisplayDialogView
        {
            Field1 = st.DialogBodyIds[idx],
            Field7 = st.DialogNpcUid,
        }, token);
    }

    /// <summary>Envoie les 484 des conteneurs de hall non vides —
    /// <c>CFamilyBuildingShop::OpenShop</c> @0x8578c90 itère la map
    /// <c>fam+0x7c</c> et n'émet que les conteneurs dont la map interne
    /// n'est pas vide (<c>node+0x28 != 0</c>), chacun avec type=1.</summary>
    private async Task SendFamilyContainersAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (_services.Social == null) return;
        var guild = _services.Guilds.GetGuildOf((int)st.CharId);
        if (guild == null) return;
        for (short c = 0; c <= 3; c++)
        {
            var items = await _services.Social
                .GetFamilyBuildingsAsync(guild.Id, c);
            if (items.Count == 0) continue;
            await session.SendPacketAsync(484,
                Eden.Core.Game.Family.FamilyPackets
                    .FamilyContainerData(1, c, items), token);
        }
    }

    /// <summary>Exécute un service PNJ — table complète type→verbe
    /// désasm <c>TC_Service</c> @0x80ef980 (mission) + handler zone
    /// <c>shop &lt;verbe&gt;</c> de RootCmds.cc @0x83d9xxx :
    ///   1/2 → "eq" (CEquipmentShop→304), 3/4/23/26/29 → "item" (304),
    ///   5 → "create_family" (347), 6 → "bank" (357), 7 → "mail" (399),
    ///   8 → "transport" (393), 9 → "strengthen" (432 op=9),
    ///   10 → "combine" (432 op=10), 11 → "family_building" (483),
    ///   12-17 → "mining/planting/summon/alchemy/casting/enchanting"
    ///     (496 EnterWorks, s16 = même numéro que le type),
    ///   18 → "auction" (430), 19 → "family_storage" (507),
    ///   20 → "message_board" (455), 21 → "town_create" (501),
    ///   24 → "prescription_item" (304), 25 → "family_town" (514),
    ///   27 → "create_gladiator_team" (524), 31 → "magic_box" (612),
    ///   35 → "instance_dialog" (564 [s16 ref][s16 extra]),
    ///   50 → "room_storage" (539 s16=0), 51 → "hotel_playertown"
    ///   (539 s16=3), 52 → "hotel_holytown" (s16=1), 53 →
    ///   "hotel_goldencity" (s16=2), 54 → "hotel_party" (s16=4),
    ///   55 → "sky_tower" (579 OpenAreaDialog s16=0xe),
    ///   56 → "mission_shop" (585), 57 → "church_reserve" (aucun
    ///   paquet natif — réservation interne), 58 → "pay_system" (602),
    ///   59 → "evaluate" (579 s16=0x12), 60 → "battle_crusade"
    ///   (ClientOpenBattleCrusadeDialog — sets complexes, non émis).
    /// ATTENTION — 6 et 18 sont INVERSES de l'intuition : les NPC
    /// `#6##` sont les banquiers (金閃閃銀行) et les `#18##` les
    /// assistants d'enchères (水晶駱駝拍賣行 助理).</summary>
    private async Task RunNpcServiceAsync(NetSession session, EntryState st,
        int type, int refId, int extra, CancellationToken token)
    {
        List<byte> body;
        switch (type)
        {
            case 1: case 2:               // "eq"   → CEquipmentShop → 304
            case 3: case 4:               // "item" → CItemShop → 304
            case 23: case 26: case 29:    // "item" → 304
            case 24:                      // "prescription_item" → 304
                await SendEnterShopAsync(session, st,
                    Math.Max(refId, 0), token);
                return;
            case 5: // "create_family" → 347 ClientEnterFamilyCreation
                    // [i32 npcUid] (Serialize : 1 int).
                await session.SendAsync(new CNC_CZ_ClientEnterFamilyCreation
                { Value = st.DialogNpcUid }, token);
                return;
            case 6: // "bank" → 357 ClientEnterStorage [i32 npcUid]
                await session.SendAsync(new CNC_CZ_ClientEnterStorage
                { Value = st.DialogNpcUid }, token);
                return;
            case 7: // "mail" → 399 ClientMailOpenBox [i32 npcUid]
                    // (CMailShop::OpenShop — piVar6[1] = npc net id).
                await session.SendAsync(new CNC_CZ_ClientMailOpenBox
                { Value = st.DialogNpcUid }, token);
                return;
            case 8: // "transport" → 393 liste de routes du groupe
                await SendEnterTransportAsync(session, st,
                    st.DialogNpcUid, Math.Max(refId, 0));
                return;
            case 9: case 10:
                // "strengthen"(9) / "combine"(10) → crée l'opération
                // (CStrengthenOp 0x26 / CCombineOp 0x27) puis 432
                // ClientEnterStrengthen [i32 npc][s16 op][u16 0]
                // [s16 -1][s16 0].
                await OpenShopOpAsync(session, st, type, type,
                    st.DialogNpcUid, st.DialogNpcHandle, token);
                return;
            case 11: // "family_building" → op CFamilyBuildingOp
                     // (subtype 0x3b — exigée par les conteneurs
                     // 130/131) + 483 ClientEnterFamilyBuilding
                     // [i32 npcTid] puis un 484 par conteneur non vide
                     // (CFamilyBuildingShop::OpenShop @0x8578c90).
                st.ShopOpType = 0x3b;
                st.ShopOpNpcUid = st.DialogNpcUid;
                st.ShopOpNpcHandle = st.DialogNpcHandle;
                await session.SendAsync(
                    new CNC_CZ_ClientEnterFamilyBuilding
                    { Value = st.DialogNpcUid }, token);
                await SendFamilyContainersAsync(session, st, token);
                return;
            case 12: case 13: case 14: case 15: case 16: case 17:
                // Métiers → 496 ClientEnterWorks
                // [i32 npc][s16 workType][s16 bldId][s16 ?][u16 0] ;
                // workType natif = le type de service lui-même
                // (12 mining, 13 planting, 14 summon, 15 alchemy,
                // 16 casting, 17 enchanting) ; bldId/? = bâtiment
                // familial — 0 hors ville de famille. Le type est
                // mémorisé comme op works (ToWork exige op+4 == type) ;
                // op unique char+0x670 : l'op shop/station précédente
                // est détruite.
                st.WorkOpType = type;
                st.ShopOpType = -1;
                st.ShopOpNpcUid = 0;
                st.ShopOpNpcHandle = 0;
                CancelPendingStrengthen(st);
                body = new List<byte>();
                await session.SendAsync(new CNC_CZ_ClientEnterWorks
                {
                    Field1 = st.DialogNpcUid, Field2 = (short)type,
                }, token);
                return;
            case 18: // "auction" → 430 ClientAuctionHouse [i32 npcUid]
                await session.SendAsync(new CNC_CZ_ClientAuctionHouse
                { Value = st.DialogNpcUid }, token);
                return;
            case 19: // "family_storage" → 507 ClientEnterFamilyStorage
                     // [i32 npcUid] puis 508 ClientFamilyStorageData
                     // par coffre non vide (natif
                     // CFamilyStorageShop::OpenShop @0x08579440).
                await session.SendAsync(
                    new CNC_CZ_ClientEnterFamilyStorage
                    { Value = st.DialogNpcUid }, token);
                if (_services.Guilds.GetGuildOf((int)st.CharId)
                        is { } storageGuild)
                    for (var idx = 0;
                         idx < Eden.Core.Database.Repositories
                             .FamilyBankRepository.StorageCount; idx++)
                    {
                        var items = await _services.FamilyBank
                            .GetItemsAsync(storageGuild.Id, idx);
                        if (items.Count > 0)
                            await session.SendPacketAsync(508,
                                Eden.Zone.Codecs.FamilyStoragePackets
                                    .BuildStorageData(idx, items), token);
                    }
                return;
            case 20: // "message_board" → 455 ClientEnterMessageBoard
                     // [i32 npcUid]
                await session.SendAsync(
                    new CNC_CZ_ClientEnterMessageBoard
                    { Value = st.DialogNpcUid }, token);
                return;
            case 21: // "town_create" → 501
                     // ClientEnterFamilyInstanceCreation
                     // [i32 npcUid][s16 refId]
                await session.SendAsync(
                    new CNC_CZ_ClientEnterFamilyInstanceCreation
                    { Field1 = st.DialogNpcUid,
                      Field2 = (short)refId }, token);
                return;
            case 25: // "family_town" → 514 ClientEnterFamilyTownList
                     // [i32 npcUid]
                await session.SendAsync(
                    new CNC_CZ_ClientEnterFamilyTownList
                    { Value = st.DialogNpcUid }, token);
                return;
            case 27: // "create_gladiator_team" → 524
                     // ClientEnterGladiatorTeamCreation
                     // [i32 npcUid][s16 refId]
                await session.SendAsync(
                    new CNC_CZ_ClientEnterGladiatorTeamCreation
                    { Field1 = st.DialogNpcUid,
                      Field2 = (short)refId }, token);
                return;
            case 31: // "magic_box" → CMagicBoxOp (0x58) + 612
                     // ClientOpenMagicBoxDialog (corps vide).
                st.ShopOpType = 31;
                st.ShopOpNpcUid = st.DialogNpcUid;
                st.ShopOpNpcHandle = st.DialogNpcHandle;
                CancelPendingStrengthen(st);
                await session.SendPacketAsync(612,
                    Array.Empty<byte>(), token);
                return;
            case 35: // "instance_dialog" → 564
                     // ClientOpenInstanceDialog [s16 refId][s16 extra]
                     // (RootCmds : 2 args s16 = ref + extra du
                     // #:Service dlg type ref extra).
                await session.SendAsync(
                    new CNC_CZ_ClientOpenInstanceDialog
                    { Field1 = (short)refId,
                      Field2 = (short)extra }, token);
                return;
            case 50: case 51: case 52: case 53: case 54:
                // "room_storage"(50→0) "hotel_holytown"(52→1)
                // "hotel_goldencity"(53→2) "hotel_playertown"(51→3)
                // "hotel_party"(54→4) → 539 ClientOpenHotel
                // [s16 hotelType][i32 npcUid].
                var hotelType = type switch
                {
                    52 => (short)1, 53 => (short)2,
                    51 => (short)3, 54 => (short)4,
                    _ => (short)0,
                };
                await session.SendAsync(new CNC_CZ_ClientOpenHotel
                { Field1 = hotelType, Field2 = st.DialogNpcUid },
                    token);
                return;
            case 55: // "sky_tower" → 579 ClientOpenAreaDialog
                     // [s16 0xe][s16 0]
                await session.SendAsync(new CNC_CZ_ClientOpenAreaDialog
                { Field1 = 0xe, Field2 = 0 }, token);
                return;
            case 56: // "mission_shop" → 585 ClientOpenMissionShop
                     // [i32 npcUid][s16 shopId]
                await session.SendAsync(
                    new CNC_CZ_ClientOpenMissionShop
                    { Field1 = st.DialogNpcUid,
                      Field2 = (short)refId }, token);
                return;
            case 58: // "pay_system" → 602 ClientOpenPaySystem
                     // [i32 npcUid][u16 shopId] (CPayShop natif).
                await session.SendAsync(new CNC_CZ_ClientOpenPaySystem
                { Field1 = st.DialogNpcUid,
                  Field2 = (ushort)Math.Max(refId, 0) }, token);
                return;
            case 59: // "evaluate" → 579 ClientOpenAreaDialog
                     // [s16 0x12][s16 refId]
                await session.SendAsync(new CNC_CZ_ClientOpenAreaDialog
                { Field1 = 0x12, Field2 = (short)refId }, token);
                return;
            case 57: // "church_reserve" — branche native sans paquet.
                return;
            case 60:
                // "battle_crusade" → 617 ClientOpenBattleCrusadeDialog
                // — Serialize natif @0x81ec980 : [i32 npcUid]
                // [u16 n1][n1×i32 crusades checkin][u16 n2]
                // [n2×i32 crusades en bataille] (les 2 sets sont remplis
                // par BattleCrusadeStatus @0x80492e0 : état+0x28==1 →
                // set1, ==2 → set2 — cache alimenté par les 202).
                {
                    var pk = new List<byte>();
                    pk.AddRange(BitConverter.GetBytes(st.DialogNpcUid)); // i32 : npcUid
                    var s1 = _crusadeStatus.Where(
                        kv => kv.Value == 1).Select(kv => kv.Key).ToList();
                    var s2 = _crusadeStatus.Where(
                        kv => kv.Value == 2).Select(kv => kv.Key).ToList();
                    pk.AddRange(BitConverter.GetBytes((ushort)s1.Count)); // u16 : n checkin
                    foreach (var c in s1)
                        pk.AddRange(BitConverter.GetBytes(c));            // i32 : crusadeId
                    pk.AddRange(BitConverter.GetBytes((ushort)s2.Count)); // u16 : n bataille
                    foreach (var c in s2)
                        pk.AddRange(BitConverter.GetBytes(c));            // i32 : crusadeId
                    await session.SendPacketAsync(617, pk.ToArray(),
                        token);
                }
                return;
            default:
                EdenLog.Write(
                    $"[Zone-Entry] service npc type={type} " +
                    $"ref={refId} extra={extra} non géré");
                return;
        }
    }

    /// <summary>Fonction directe `#fn#arg#` de S_Npc : la mission native
    /// transforme la colonne fonction en `Service &lt;fn&gt; &lt;arg&gt;`
    /// (InsertNPCInitServiceDB) — le code de fonction EST le type de
    /// service TC_Service, pas une enum séparée. On délègue donc à la
    /// même table.</summary>
    private Task RunNpcFunctionAsync(NetSession session, EntryState st,
        int fn, int arg, CancellationToken token) =>
        RunNpcServiceAsync(session, st, fn, arg, 0, token);

    /// <summary>"#:Talk 4620 4621#" -> [(4620,4621)] (paires titre/corps).</summary>
    private static List<(int title, int body)> ParseTalkPairs(string? script)
    {
        var res = new List<(int, int)>();
        if (string.IsNullOrEmpty(script)) return res;
        var m = System.Text.RegularExpressions.Regex.Match(
            script, @"#:Talk ([0-9 ]+)#");
        if (!m.Success) return res;
        var ids = m.Groups[1].Value.Split(' ',
            StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        for (var i = 0; i + 1 < ids.Length; i += 2)
            res.Add((ids[i], ids[i + 1]));
        if (ids.Length % 2 == 1) res.Add((ids[^1], ids[^1]));
        return res;
    }

    private Dictionary<int, string>? _dialogues;

    private string DialogueText(int id)
    {
        _dialogues ??= LoadDialogues();
        return _dialogues.TryGetValue(id, out var t) ? t : $"#{id}";
    }

    private Dictionary<int, string> LoadDialogues()
    {
        var map = new Dictionary<int, string>();
        foreach (var f in new[] { "T_Dialogue.ini", "S_Dialogue.ini" })
        {
            var p = Path.Combine(_dataDir, f);
            if (!File.Exists(p)) continue;
            foreach (var (k, v) in WorldParsers.ParseDialogues(p))
                map.TryAdd(k, v); // T_ (anglais) prioritaire
        }
        return map;
    }
}