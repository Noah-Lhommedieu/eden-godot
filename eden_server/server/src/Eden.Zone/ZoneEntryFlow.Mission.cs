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

public sealed partial class ZoneEntryFlow
{
    /// <summary>Route une réponse MissionServer vers le client du perso.
    /// Les paquets clés : 56 ListDialogue→285, 57 ReplyDialogue→285,
    /// 65 UpdateNPCSymbols→345, 67 UpdateMissionState→391.
    /// Traitement SÉRIALISÉ via <see cref="_missionChain"/> : le fire-and-
    /// forget laissait les envois se croiser — la mission émet 68→400
    /// puis 72→404 mais le client recevait le 404 AVANT le 400, ce qui
    /// n'arrive jamais dans le flux natif.</summary>
    private Task _missionChain = Task.CompletedTask;
    private readonly object _missionGate = new();

    private void OnMissionPacket(ushort ncid, byte[] body)
    {
        // Boucle branchée : les mutations mission tournent sur le fil
        // unique (la file FIFO garantit l'ordre des paquets).
        if (_services.Loop != null)
        {
            _ = RunOnLoopAsync(ct => ForwardSafeAsync(ncid, body));
            return;
        }
        lock (_missionGate)
        {
            // Chaque paquet attend la fin du traitement du précédent.
            // ForwardSafeAsync avale ses propres exceptions : la chaîne
            // ne peut jamais finir en faute.
            _missionChain = _missionChain
                .ContinueWith(_ => ForwardSafeAsync(ncid, body),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>Enveloppe de ForwardMissionPacketAsync qui isole les
    /// erreurs de routage : un paquet malformé ou un handler en échec
    /// est loggé puis oublié sans casser la file.</summary>
    private async Task ForwardSafeAsync(ushort ncid, byte[] body)
    {
        try { await ForwardMissionPacketAsync(ncid, body); }
        catch (Exception ex)
        { EdenLog.Write($"[Zone-M] route fail ncid={ncid}: {ex.Message}"); }
    }

    /// <summary>Cache crusadeId → état (+0x28 natif : 1 = checkin,
    /// 2 = bataille, 0 = fermé) — rempli par les broadcasts 202 de la
    /// mission et lu par le dialog « battle_crusade » (617).</summary>
    private readonly Dictionary<int, int> _crusadeStatus = new();

    /// <summary>CNE_MW_WorldTextCommand::Execute natif (@0x82f9180) —
    /// exécute la commande texte mission. Seul
    /// <c>BroadCastScreenMessage Type= N Msg= txt</c> est implémenté
    /// (Mission_TC_BroadCastScreenMessage @0x83b5c50 : type 10 → 2,
    /// puis 474 <c>CNC_CZ_ClientScreenMessage [s16 type][str]</c>
    /// émis à TOUS les clients de la zone). Les verbes de scène
    /// (<c>BornNpcByTID</c>…) sont routés vers
    /// <see cref="HandleSceneTextCommandAsync"/>.</summary>
    private async Task HandleWorldTextCommandAsync(byte[] body)
    {
        if (body.Length < 2) return;
        var len = BitConverter.ToUInt16(body, 0);
        if (2 + len > body.Length) return;
        var cmd = Eden.Core.Data.EdenEncoding.Big5
            .GetString(body, 2, len).TrimEnd('\0');
        var w = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Verbes de scène (plan 02 étape 3) : BornNpcByTID / KillNpcByTID
        // / MonsterActionByHandle / PlaySound — exécutés sur la boucle
        // de zone (mutation du monde vivant), jamais sur le fil réseau.
        if (w.Length > 0 && w[0] is "BornNpcByTID" or "KillNpcByTID"
                or "MonsterActionByHandle" or "PlaySound" or "PlayEffect" or "NpcWalkToByTID")
        {
            await RunOnLoopAsync(ct => HandleSceneTextCommandAsync(cmd, ct));
            return;
        }
        if (w.Length < 5
            || w[0] != "BroadCastScreenMessage") return;
        // "Type= N" puis "Msg= …" (le message contient des espaces —
        // on reprend le suffixe brut après « Msg= »).
        short type = 0;
        var mi = cmd.IndexOf("Msg=", StringComparison.Ordinal);
        var ti = cmd.IndexOf("Type=", StringComparison.Ordinal);
        if (ti >= 0)
        {
            var te = cmd.IndexOf(' ', ti + 5);
            var tv = cmd[(ti + 5)..(te > ti ? te : cmd.Length)].Trim();
            short.TryParse(tv, out type);
        }
        var msg = mi >= 0 ? cmd[(mi + 4)..].Trim() : "";
        if (type == 10) type = 2;                  // quirk natif
        if (msg.Length == 0) return;
        await _services.Sessions.BroadcastAllAsync(
            new CNC_CZ_ClientScreenMessage
            { Field1 = type, Field2 = msg });
    }

    private async Task ForwardMissionPacketAsync(ushort ncid, byte[] body)
    {
        // 58 TransitionCommands : routage SPÉCIAL — le charUid est à
        // l'offset 2 (wire [s16][i32 charUid][vec<string>], désasm
        // Deserialize @0x833cc40), pas en tête comme les autres ZMis.
        if (ncid == 58)
        {
            await HandleTransitionCommandsAsync(body);
            return;
        }
        // 20 CNC_MW_WorldTextCommand [u16 len][str cmd] — broadcast
        // mission→zones sans charUid : la zone exécute la commande
        // texte (CMissionCmds::ExecuteCommand natif). Intercepté avant
        // l'extraction du charUid (le corps n'en porte pas).
        if (ncid == 20)
        {
            await HandleWorldTextCommandAsync(body);
            return;
        }
        // 763 ZoneServerRankInfo / 779 ZoneServerUpdateRoom —
        // broadcasts mission→zones SANS charUid en tête (le natif les
        // route sur le type, pas sur un destinataire : ils remplissent
        // les caches zone CZoneServer+0x5e0/+0x564). Passer sous le
        // gate FindByCharacter les droppait toutes — vérifié en live
        // ("[Zone-M] ncid=763 char#xxxx sans session" ×8, fenêtres de
        // classement vides).
        if (ncid == 763)
        {
            OnMissionRankInfoAsync(body);
            return;
        }
        if (ncid == 779)
        {
            await OnMissionRoomUpdateAsync(body, CancellationToken.None);
            return;
        }
        // 530 crusade-close — broadcast mission→zones SANS charUid
        // (comme 763/779) : [s16 crusadeId] → remise à 0 de
        // char+0xdfc (CrusadeId) chez tous les joueurs locaux.
        // 202 ZoneServerBattleCrusadeInfo — broadcast mission→zones
        // SANS charUid, 2 variantes natives (Serialize/Deserialize
        // croisés @0x804xxx/@0x830xxx) : flag==0 → update unitaire
        // [i32 crusade][i32 status][i32 membres][i32][u8] ;
        // flag==1 → sync-all [i32 n][n×(s16 crusade,s16 status)].
        // Alimente le cache d'état crusade → sets du 617.
        if (ncid == 202)
        {
            if (body.Length >= 2)
            {
                var flag = BitConverter.ToInt16(body, 0);
                if (flag == 0 && body.Length >= 10)
                    _crusadeStatus[BitConverter.ToInt32(body, 2)] =
                        BitConverter.ToInt32(body, 6);
                else if (flag == 1 && body.Length >= 6)
                {
                    var n = BitConverter.ToInt32(body, 2);
                    for (var i = 0; i < n; i++)
                    {
                        var at = 6 + i * 4;
                        if (at + 4 > body.Length) break;
                        _crusadeStatus[
                            BitConverter.ToInt16(body, at)] =
                                BitConverter.ToInt16(body, at + 2);
                    }
                }
            }
            return;
        }
        if (ncid == 530)
        {
            if (body.Length >= 2)
            {
                var cid = BitConverter.ToInt16(body, 0);
                foreach (var ost in _states.Values)
                {
                    if (ost.CrusadeId != cid) continue;
                    ost.CrusadeId = 0;
                    // Rapatriement au point de retour sauvé à
                    // l'entrée (leave/close natif).
                    var back = ost.CrusadeReturnNode;
                    ost.CrusadeReturnNode = 0;
                    if (ost.Session != null && back > 0 &&
                        back != ost.NodeId)
                    {
                        var sc = _services.Scenes.GetScene(
                            $"S{back:D3}");
                        await MigrateToNodeAsync(ost.Session, ost,
                            (short)back, ost.CrusadeReturnX,
                            ost.CrusadeReturnY, 0,
                            CancellationToken.None);
                    }
                }
            }
            return;
        }
        if (body.Length < 4) return;
        var charId = BitConverter.ToUInt32(body, 0);
        // Les diffusions globales (charUid=0 : horloge serveur, loterie,
        // starway…) ne sont pas routables vers un perso — on les ignore
        // silencieusement au lieu de spammer le log.
        if (charId == 0) return;
        var session = _services.Sessions.FindByCharacter((int)charId);
        if (session == null)
        {
            EdenLog.Write($"[Zone-M] ncid={ncid} char#{charId} sans session");
            return;
        }
        _states.TryGetValue(session.SessionId, out var est);
        if (est != null && (ncid == 56 || ncid == 57))
            est.DialogAcked = est.DialogSeq;
        switch (ncid)
        {
            case 56: // ListDialogue -> ClientDisplayDialogView (285)
                await ForwardListDialogueAsync(session, body);
                break;
            case 57: // ReplyDialogue -> 285 (corps d'option)
                await ForwardReplyDialogueAsync(session, body);
                break;
            case 65: // UpdateNPCSymbols -> client 345
                // Wire mission→zone (natif ZMis Deserialize @0x081192e0) :
                // [i32 charUid][s16 node][u16 n][n×(i32 npcUid, s16 sym)].
                // Wire client 345 : [s16 n][n×(i32 npcUid, s16 sym1,
                // s16 sym2)] — le natif ajoute un SECOND s16 par NPC
                // (état zone local, 0 = aucun). La mission envoie l'état
                // de TOUS les PNJ du monde : on ne garde que ceux de la
                // scène du joueur (le client efface les autres).
                {
                    var scene65 = est != null
                        ? _services.Scenes.GetScene($"S{est.NodeId:D3}")
                        : null;
                    var out345 = NpcSymbolRelay.ToClient345(body,
                        scene65?.Npcs.Select(n => n.NpcId).ToHashSet());
                    if (out345 != null)
                        await session.SendPacketAsync(345, out345,
                            CancellationToken.None);
                }
                break;
            case 67: // UpdateMissionState -> client 391
                await session.SendPacketAsync(391, body[4..],
                    CancellationToken.None);
                break;
            case 66: // TextOutputIndex -> client 316
                await session.SendPacketAsync(316, body[4..],
                    CancellationToken.None);
                break;
            case 59: // TextOutput -> client 266
                await session.SendPacketAsync(266, body[4..],
                    CancellationToken.None);
                break;

            // 91 BattlefieldJoinResult — accusé mission du join :
            // [i32 char][str name][s16 bf][s16 side][s16 res][s16]
            // → 464 ClientBattlefieldJoinResult (Field1=bf,
            // Field4=res/instance) au client à l'origine.
            case 91:
                if (body.Length >= 8)
                {
                    var bfRes = BitConverter.ToInt16(body,
                        body.Length - 4);
                    var bfId = BitConverter.ToInt16(body,
                        body.Length - 8);
                    var r = new List<byte>();
                    r.AddRange(BitConverter.GetBytes(bfId));   // bf
                    r.AddRange(BitConverter.GetBytes(bfRes)); // résultat
                    await session.SendPacketAsync(464,
                        r.ToArray(), CancellationToken.None);
                }
                break;

            // 464 BattlefieldSetup — match formé côté mission :
            // [s16 bfId][s16 nA][s16 nB][s16 nTotal][nTotal×i32 charId]
            // → entre chaque joueur LOCAL dans la map bf
            // (MigrateToNodeAsync vers le MapId S_Battlefield, pattern
            // BattleRoyale EnterInstance).
            case 464:
                if (body.Length >= 8)
                {
                    var bfId = BitConverter.ToInt16(body, 0);
                    var nTotal = BitConverter.ToInt16(body, 6);
                    var map = _services.GameData.Battlefields
                        .FirstOrDefault(x => x.Id == bfId)?.MapId ?? 0;
                    for (var i = 0; i < nTotal; i++)
                    {
                        var at = 8 + i * 4;
                        if (at + 4 > body.Length) break;
                        var cid = BitConverter.ToInt32(body, at);
                        var mst = _states.Values.FirstOrDefault(
                            z => z.CharId == (uint)cid);
                        var sess = _services.Sessions
                            .FindByCharacter(cid);
                        if (mst == null || sess == null || map <= 0)
                            continue;
                        mst.BattlefieldId = bfId;
                        var sc = _services.Scenes.GetScene(
                            $"S{map:D3}");
                        var (ex, ey, ez) = ResolveInstanceEntryPos(
                            sc, Random.Shared);
                        await MigrateToNodeAsync(sess, mst,
                            (short)map, ex, ey, ez,
                            CancellationToken.None);
                    }
                    EdenLog.Write($"[Zone] battlefield {bfId} " +
                        $"match -> map {map}");
                }
                break;

            // 466 BattlefieldUpdateScore — score live d'un match
            // [s16 bf][s16 scoreA][s16 scoreB] → forward 466 au client
            // des joueurs en match (miroir natif UpdateScore).
            case 466:
                if (body.Length >= 6)
                {
                    var bf = BitConverter.ToInt16(body, 0);
                    var out2 = new byte[6];
                    Array.Copy(body, out2, 6);
                    foreach (var ost in _states.Values)
                        if (ost.BattlefieldId == bf &&
                            ost.Session != null)
                            await ost.Session.SendPacketAsync(466,
                                out2, CancellationToken.None);
                }
                break;

            // 465 BattlefieldResult — fin de match mission :
            // [s16 bf][s16 winner][s16 nW][nW×i32 charId] → 465 au
            // client des joueurs + reward S_Battlefield aux
            // gagnants locaux + nettoyage BattlefieldId.
            case 465:
                if (body.Length >= 6)
                {
                    var bf = BitConverter.ToInt16(body, 0);
                    var nW = BitConverter.ToInt16(body, 4);
                    var winners = new HashSet<int>();
                    for (var i = 0; i < nW; i++)
                    {
                        var at = 6 + i * 4;
                        if (at + 4 > body.Length) break;
                        winners.Add(BitConverter.ToInt32(body, at));
                    }
                    var rb = new byte[4];
                    Array.Copy(body, rb, 4);
                    foreach (var ost in _states.Values)
                        if (ost.BattlefieldId == bf)
                        {
                            if (ost.Session != null)
                                await ost.Session.SendPacketAsync(
                                    465, rb, CancellationToken.None);
                            if (winners.Contains((int)ost.CharId))
                                await GrantBattlefieldRewardAsync(
                                    ost, bf);
                            ost.BattlefieldId = 0;
                        }
                }
                break;

            // 201 EnterBattleCrusadeAnswer — accusé mission du join
            // crusade : [i32 char][s16 res][s16 crusade][s16 node].
            // Execute natif zone @0x8313b90 : gate encaged,
            // char+0xdfc = crusade, TransportCharacterToNode(node) —
            // migration vers la map de siège (S_Crusade 1-4 ↔ nodes
            // 310-313, type 19). L'accusé 616 client conserve le
            // format historique [crusade][res][0][0].
            case 201:
                if (body.Length >= 10)
                {
                    var cres = BitConverter.ToInt16(body, 4);   // résultat Join (1 = ok)
                    var ccid = BitConverter.ToInt16(body, 6);   // crusadeId
                    var cnode = BitConverter.ToInt16(body, 8);  // node de siège
                    // 616 ClientUpdateBattleCrusadeInfo natif :
                    // Serialize @0x81c3fc0 VIDE — le wire n'est que
                    // le NCID (rafraîchissement de la fenêtre).
                    await session.SendPacketAsync(616,
                        Array.Empty<byte>(), CancellationToken.None);
                    if (cres == 1 && cnode > 0 &&
                        _states.TryGetValue(session.SessionId,
                            out var cst))
                    {
                        cst.CrusadeId = ccid;
                        // Sauvegarde du point de retour avant la
                        // migration (exec+0xf0..f8 natifs).
                        cst.CrusadeReturnNode = cst.NodeId;
                        cst.CrusadeReturnX = cst.PosX;
                        cst.CrusadeReturnY = cst.PosZ;
                        var sc = _services.Scenes.GetScene(
                            $"S{cnode:D3}");
                        var (ex, ey, ez) = ResolveInstanceEntryPos(
                            sc, Random.Shared);
                        await MigrateToNodeAsync(session, cst,
                            cnode, ex, ey, ez,
                            CancellationToken.None);
                        EdenLog.Write($"[Zone] crusade join " +
                            $"char#{charId} → node {cnode}");
                    }
                }
                break;

            // --- Mail : réponses DBA de la mission (zone->mission 21
            // AgentDBACommand émis par les handlers client 96-102) ---
            // 68 ZoneServerMailList -> 400 : le paquet mission est un
            // RÉSUMÉ plat (17 champs, désasm Deserialize @0x8327190) —
            // le natif reconstruit les vecteurs SoA depuis l'objet
            // personnage ; on sert les entrées depuis player_mail.
            case 68:
                await ForwardMailListAsync(session, body);
                break;
            // 69 MailRead -> 401 : le corps record suit l'en-tête
            // [i32 charUid][s16] — miroir direct comme 65->345.
            case 69:
                if (body.Length > 6)
                    await session.SendPacketAsync(401, body[6..],
                        CancellationToken.None);
                break;
            // 70 MailDeleteResult [i32 char][s16][s16 res][i32 id]
            //    -> 402 [s16][i32] (désasm Execute @0x831f5bb).
            case 70:
                if (body.Length >= 12)
                    await session.SendPacketAsync(402, body[6..],
                        CancellationToken.None);
                break;
            // 71 MailSendResult [i32 char][s16][s16 res][...]
            //    -> 403 [s16 res] (désasm Execute @0x832872c).
            case 71:
                if (body.Length >= 8)
                    await session.SendPacketAsync(403, body[6..8],
                        CancellationToken.None);
                break;
            // 72 MailReceive [i32 char][s16][s16][s16][s16]
            //    -> 404 [s16][s16] (champs +0x1a/+0x1c de l'event).
            // Le crash à l'ouverture venait du 400 (manquait s8+u16
            // terminaux -> over-read côté client) — 404 wire-exact,
            // réactivé.
            case 72:
                if (body.Length >= 10)
                    await session.SendPacketAsync(404, body[6..10],
                        CancellationToken.None);
                break;
            // 75 MailGetItemOK [i32 char][s16][i32 res][i32 itemId]
            //    [i32 bag][i32 slot][i32 dur][i32 maxDur][i32 bind]
            //    [i32 strengthen] -> 405 [i32] + 274 PutItemIntoInventory
            //    (le client doit voir l'item débarquer dans le sac).
            case 75:
                if (body.Length >= 10)
                    await session.SendPacketAsync(405, body[6..10],
                        CancellationToken.None);
                if (body.Length >= 38 &&
                    BitConverter.ToInt32(body, 6) == 1)
                {
                    var mItem = BitConverter.ToInt32(body, 10);
                    var mBag = BitConverter.ToInt32(body, 14);
                    var mSlot = BitConverter.ToInt32(body, 18);
                    var mDur = BitConverter.ToInt32(body, 22);
                    var mMaxDur = BitConverter.ToInt32(body, 26);
                    var mBind = BitConverter.ToInt32(body, 30);
                    var mStr = BitConverter.ToInt32(body, 34);
                    // 274 — ordre natif [id][bag][slot][dur][maxdur]
                    // (PutIntoContainer @0x809ea00).
                    await session.SendPacketAsync(274,
                        Codecs.NativeItemCodec.BuildPutItem(mItem,
                            (short)mBag, (short)mSlot, (short)mDur,
                            (short)mMaxDur,
                            (byte)(mBind != 0 ? 0x48 : 0x08),
                            (short)mStr),
                        CancellationToken.None);
                }
                break;
            // 76 MailGetGoldOK [i32 char][s16][i32 res][i32 goldDelta]
            //    [i32] -> 406 [i32] + 280 attr 0x02 (or — GainGold natif).
            case 76:
                if (body.Length >= 10)
                    await session.SendPacketAsync(406, body[6..10],
                        CancellationToken.None);
                if (body.Length >= 14 &&
                    BitConverter.ToInt32(body, 6) == 1)
                {
                    var gold = await _services.Characters
                        .GetGoldAsync((int)charId);
                    // Le crédit a eu lieu côté mission (tx
                    // MailTakeGoldAsync) : le miroir live doit suivre,
                    // sinon les checks or de la zone (étal, enchères)
                    // travaillaient sur une valeur périmée.
                    if (est != null)
                        await RunOnLoopAsync(ct =>
                        {
                            est.Gold = (int)Math.Min(gold,
                                int.MaxValue);
                            return Task.CompletedTask;
                        });
                    await session.SendAsync(new CNC_CZ_ClientUpdateAttr
                    {
                        Field1 = (int)charId,       // u32 : handle
                        Field2 = 2,                 // s16 : attr or
                        Field3 = (int)gold,         // i32 : valeur
                        Field4 = 0,                 // i64 : p2 (0 natif)
                    }, CancellationToken.None);
                }
                break;
            // 77 MailReturnResult [i32 char][s16][s16] -> 409 [s16]
            case 77:
                if (body.Length >= 8)
                    await session.SendPacketAsync(409, body[6..8],
                        CancellationToken.None);
                break;

            // --- Panneau d'affichage (réponses aux relais 138-142) ---
            // 739 ZoneServerMessageList — Execute natif @0x83295b0
            // IGNORE le payload : le client reçoit un 456 toujours
            // vide [u16 n=0][3×s16 0] (feature bridée nativement).
            case 739:
                await session.SendPacketAsync(456,
                    new byte[8], CancellationToken.None);
                break;
            // 740 ZoneServerMessageWriteResult
            // [i32 char][s16 node][i32 msgId][s16 res] — Execute
            // @0x8329xxx : res==0 && DropItemByItemID(0x7ceb,1) →
            // renvoie "MessageDelete id = msgId" au DBA (le post est
            // purgé sitôt créé) + client 457 [s16=2] ; sinon 457 [res].
            case 740:
                if (body.Length >= 14)
                {
                    var msgId = BitConverter.ToInt32(body, 6);
                    var res = BitConverter.ToInt16(body, 12);
                    if (res == 0 &&
                        await Handlers.MiscHandlers
                            .ConsumeItemByIdAsync(_services, session,
                                (int)charId, 0x7ceb,
                                CancellationToken.None))
                    {
                        if (_services.Mission?.Connected == true)
                            await _services.Mission
                                .SendAgentDbaCommandAsync(charId,
                                    $"MessageDelete id = {msgId}",
                                    CancellationToken.None);
                        await session.SendPacketAsync(457,
                            BitConverter.GetBytes((short)2),
                            CancellationToken.None);
                    }
                    else
                        await session.SendPacketAsync(457,
                            BitConverter.GetBytes(res),
                            CancellationToken.None);
                }
                break;
            // 741 ZoneServerMessageRead [i32 char][s16][i32 id]
            // [str contenu][u8] -> 458 [i32 id][str][u8] (body[6..]).
            case 741:
                if (body.Length > 6)
                    await session.SendPacketAsync(458, body[6..],
                        CancellationToken.None);
                break;
            // 742 ZoneServerMessageDeleteResult [i32 char][s16][s16]
            // -> 459 [s16].
            case 742:
                if (body.Length >= 8)
                    await session.SendPacketAsync(459, body[6..8],
                        CancellationToken.None);
                break;

            // ---- Salles personnelles (CPlayerRoomManager natif) ----
            // 779 intercepté avant le gate charUid (broadcast).
            // 778 ZoneServerSearchRoomResult : 7 vecteurs parallèles
            // → 537 ClientSearchRoomResult entrelacé au requester.
            case 778:
                await OnMissionSearchRoomResultAsync(body,
                    CancellationToken.None);
                break;
            // 781 ZoneServerUpdateRoomItem → 542 ClientUpdateRoomItem
            // (index/loc d'un décor) au proprio connecté.
            case 781:
                await OnMissionUpdateRoomItemAsync(body,
                    CancellationToken.None);
                break;

            // 763 intercepté avant le gate charUid (broadcast —
            // remplit le cache type→RankSet ; requête 194 → 521).

            // 104 ZoneServerQueryPlayerCard — Execute @0x832d820 :
            // CPlayerFactory::ClientPlayerCard → 491. Wire ZMis :
            // [i32 requester][i32 tgt][str nom][3×s16][str guilde]
            // [12×s16][str intro][vec][vec] — le corps client 491 est
            // identique moins le requester → miroir body[4..].
            case 104:
                if (body.Length > 4)
                    await session.SendPacketAsync(491, body[4..],
                        CancellationToken.None);
                break;

            // 832 ZoneServerUpdateRecommendActivities — wire
            // [i32 charUid][u16 recommend_id][u32 count] : la mission
            // pousse la progression (UpdateRecommendActivities
            // mission→zone, PTR__…_084a2f8c). Sur la boucle :
            // mutation RecommendProgress + FinishedProcess natif.
            case 832:
                if (est != null && body.Length >= 8)
                    await RunOnLoopAsync(ct =>
                        OnMissionRecommendUpdateAsync(session, est,
                            body, ct));
                break;

            // 173 ZoneServerUpdateLifelongGold — réponse de la
            // mission au DBA « GMGainLifelong » (TC_GMGainLifelongGold
            // natif → CNC_ZMis_ZoneServerUpdateLifelongGold
            // [i32 char][i32 or total]) : la zone aligne
            // st.LifelongGold et pousse 555 au client (récompenses
            // recommend-activities + quêtes boutique).
            case 173:
                if (est != null && body.Length >= 8)
                    await RunOnLoopAsync(async ct =>
                    {
                        est.LifelongGold =
                            BitConverter.ToInt32(body, 4); // i32 : or
                        await session.SendAsync(
                            new CNC_CZ_ClientUpdateLifelongGold
                            { Value = est.LifelongGold }, ct);
                    });
                break;

            // 80 UpdateSpecialCoin (interne — le natif diffuse
            // CNC_MW_WorldServerUpdateSpecialCoin mission→monde, notre
            // mission n'a pas de lien monde) : corps
            // [i32 charUid][u16 n][n×(s16 type, i32 total)] — la zone
            // relaie au monde (ZW 30) qui pousse le 78
            // ClientUpdateSpecialCoin `[u16 n][n×(s16,i32)]` au client.
            case 80:
                if (body.Length >= 6 && _world.Connected)
                    await _world.SendAsync(30, body,
                        CancellationToken.None);
                break;

            // 193 ZoneServerWeddingsInfo — liste des cérémonies de
            // mariage (CWeddingFactory::WeddinginfoQuery). Corps ZMis :
            // [i32 requester][u8 n][n×(i32,str,str,u8,i32,i32)] ; la
            // zone calcule leftTime = +0x18 - (now - +0x14) puis émet
            // 599 [u8 n][n×(i32,str,str,u8,i32)] (désasm Execute
            // @0x833ff00 + Serialize @0x80db220).
            case 193:
            {
                var recs = Codecs.WeddingPackets.ParseWeddingsInfo(body);
                if (recs != null)
                    await session.SendPacketAsync(599,
                        Codecs.WeddingPackets.BuildClientWeddingsInfo(
                            recs,
                            (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                        CancellationToken.None);
                break;
            }

            default:
                break; // déjà loggé par le lien
        }
    }

    /// <summary>56 ZoneServerListDialogue — layout exact du désassemblage
    /// (Deserialize zone @0x8320a60, Execute @0x831fa50, ctor
    /// CNC_CZ_ClientDisplayDialogView @0x822e450) :
    /// [i32 charUid][s16 nodeId][u8 type][i32 npcUid]
    /// vec&lt;i32&gt;  A (+0x20) — selId, indexé par i
    /// vec&lt;s16&gt;  B (+0x2c) — u16 a, indexé par i
    /// vec&lt;s16&gt;  C (+0x38) — statuts, non repris dans les entrées
    /// vec&lt;vec&lt;i32&gt;&gt;  D (+0x44) — D[i][j] = id affiché (dialog+0x8)
    /// vec&lt;vec&lt;Ss&gt;&gt;   E (+0x50) — E[i][j] = nom brut envoyé au client
    /// vec&lt;vec&lt;s16&gt;&gt;  F (+0x5c) — F[i][j] = s16 flag (dialog+0x10)
    /// vec&lt;vec&lt;s16&gt;&gt;  G (+0x68) — G[i][j] = u16 b (dialog+0x6) ;
    ///                          la boucle interne itère sur |G[i]|
    /// [i32 ctx] (+0x74) — T_Dialogue id du texte PARLÉ par le NPC.
    /// Execute construit un CMissionDialog par couple (i,j) :
    ///   {+0x0 = A[i], +0x4 = B[i], +0x6 = G[i][j], +0x8 = D[i][j],
    ///    +0x10 = F[i][j], +0x14 = E[i][j]}
    /// Le ctor trie par +0x8 (less_sort) puis émet le 285 :
    ///   [i32 ctx][u16 n][n×{i32 dispId][u16 len][nom][s16 flag}]
    ///   [u8 type][i32 npcUid].
    /// Le client ne renvoie qu'un index — le clic (128) doit renvoyer
    /// {+0x0,+0x4,+0x6} du dialog stocké, PAS l'id affiché.</summary>
    private async Task ForwardListDialogueAsync(NetSession session, byte[] b)
    {
        var dialogs = new List<MissionDialog>();
        var ctx = 0; var type = b[6]; var npcUid = BitConverter.ToInt32(b, 7);
        var o = 11;                                     // header i32+s16+u8+i32
        try
        {
            var a = ReadVecI32(b, ref o);               // selId par i
            var vB = ReadVecS16(b, ref o);              // u16 a par i
            ReadVecS16(b, ref o);                       // C — statuts (non utilisés)
            var d = ReadVecVecI32(b, ref o);            // ids affichés [i][j]
            var e = ReadVecVecStr(b, ref o);            // noms [i][j]
            var f = ReadVecVecS16(b, ref o);            // flags [i][j]
            var g = ReadVecVecS16(b, ref o);            // u16 b [i][j]
            ctx = BitConverter.ToInt32(b, o); o += 4;
            EdenLog.Write($"[Zone-M] 56 A={a.Count} B={vB.Count}"
                + $" D={d.Count} E={e.Count} F={f.Count}"
                + $" G={g.Count} ctx={ctx} npc={npcUid} len={b.Length} off={o}");
            var ni = Math.Min(a.Count,
                Math.Min(vB.Count, Math.Min(d.Count,
                    Math.Min(e.Count, Math.Min(f.Count, g.Count)))));
            for (var i = 0; i < ni; i++)
            {
                var nj = Math.Min(g[i].Length,
                    Math.Min(d[i].Length,
                        Math.Min(e[i].Length, f[i].Length)));
                for (var j = 0; j < nj; j++)
                    dialogs.Add(new MissionDialog(a[i], vB[i], g[i][j],
                        d[i][j], f[i][j], e[i][j]));
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-M] parse56 fail @{o}: {ex.Message}");
        }

        // Tri natif : less_sort compare dialog+0x8 (id affiché) — le ctor
        // du 285 réordonne missionDialogs ; l'index cliqué par le client
        // suit cet ordre.
        dialogs.Sort((x, y) => x.DisplayId.CompareTo(y.DisplayId));

        // Mémorise les options (ordre trié) pour OnDialogueSelected.
        if (_states.TryGetValue(session.SessionId, out var est))
        {
            est.MissionDialogs = dialogs;
            est.MissionCtx = ctx > 0 ? ctx : null;
        }

        // Comportement natif exact (CNE_ZMis_ZoneServerListDialogue::
        // Execute @0x831fa50) : ctx ∈ {-1, 0} et npcUid != 0 → la zone
        // fait ResetOperation(char) et N'ENVOIT PAS de 285 — pas de
        // fenêtre. ctx=-1 = greeting absent (NPC à service direct).
        if (unchecked((uint)(ctx + 1)) < 2 && npcUid != 0)
        {
            EdenLog.Write($"[Zone-M] 56 ctx={ctx} npc={npcUid} " +
                "-> reset op, pas de 285 (natif)");
            if (dialogs.Count == 0 &&
                _states.TryGetValue(session.SessionId, out var rst))
            {
                var rnpc = _services.GameData.Npcs
                    .FirstOrDefault(n => n.Id == rst.DialogNpcId);
                if (rnpc != null && (!string.IsNullOrEmpty(rnpc.TalkScript) ||
                    rnpc.Services.Count > 0))
                    await SendLocalGossipAsync(session, rst, rnpc,
                        rst.DialogNpcHandle);
            }
            return;
        }
        var msg285 = new CNC_CZ_ClientDisplayDialogView
        {
            Field1 = ctx,                               // i32 : texte parlé
            Field6 = type,                              // u8 : type de vue
            Field7 = npcUid,                            // i32 : star id NPC
        };
        foreach (var dlg in dialogs)
            msg285.Entries.Add(new() { DisplayId = dlg.DisplayId,
                Name = dlg.Name, Flag = dlg.Flag });
        await session.SendAsync(msg285, CancellationToken.None);
        EdenLog.Write($"[Zone-M] 56->285 npc={npcUid}" +
            $" entries={dialogs.Count} ctx={ctx}");

        // Mission muette (0 entrée = pas de quête dispo pour ce perso) :
        // le NPC conserve ses options LOCALES — #:Talk gossip + services.
        // Sans ça le client n'affichait que le texte parlé sans aucune
        // option cliquable ("texte seul" constaté sur les NPC quête).
        if (dialogs.Count == 0 &&
            _states.TryGetValue(session.SessionId, out var zst))
        {
            var npc = _services.GameData.Npcs
                .FirstOrDefault(n => n.Id == zst.DialogNpcId);
            if (npc != null && (!string.IsNullOrEmpty(npc.TalkScript) ||
                npc.Services.Count > 0))
                await SendLocalGossipAsync(session, zst, npc,
                    zst.DialogNpcHandle);
        }
    }

    /// <summary>68 ZoneServerMailList -&gt; 400 ClientMailList : la mission
    /// envoie un résumé plat ; le natif (Execute @0x8322de0) construit le
    /// 400 à partir des ~46 vecteurs SoA stockés sur le personnage. Chez
    /// nous player_mail est la source — on émet le codec complet
    /// (CNC_CZ_ClientMailList, boucle du Serialize natif @0x8204620) —
    /// la boîte vide donne exactement [u16 0] comme le natif.</summary>
    private async Task ForwardMailListAsync(NetSession session, byte[] b)
    {
        var charId = BitConverter.ToInt32(b, 0);
        var list = new CNC_CZ_ClientMailList();
        try
        {
            var inbox = await _services.Mail.GetInboxAsync(charId);
            foreach (var mail in inbox)
            {
                var e = new CNC_CZ_ClientMailList.Entry
                {
                    MailId = mail.Id,
                    SenderName = mail.SenderName ?? "",
                    SendTime = (int)new DateTimeOffset(
                        mail.SentAt).ToUnixTimeSeconds(),
                    Subject = mail.Subject ?? "",
                    Content = mail.Body ?? "",
                    Flag = (byte)(mail.IsRead ? 1 : 0),
                    AttachGold = (uint)Math.Clamp(
                        mail.AttachedGold, 0, uint.MaxValue),
                    ExpireTime = mail.ExpiresAt.HasValue
                        ? new DateTimeOffset(
                            mail.ExpiresAt.Value).ToUnixTimeSeconds()
                        : 0,
                };
                // Premier item joint "id:qty,…" dans les champs dédiés.
                var first = (mail.AttachedItems ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (first.Length > 0)
                {
                    var kv = first[0].Split(':');
                    if (int.TryParse(kv[0], out var iid)) e.AttachItemId = iid;
                    if (kv.Length > 1 && int.TryParse(kv[1], out var q))
                        e.AttachQty = q;
                }
                list.Entries.Add(e);
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-M] mail list char#{charId}: {ex.Message}");
        }
        await session.SendAsync(list, CancellationToken.None);
        EdenLog.Write($"[Zone-M] 68->400 char#{charId} " +
            $"entries={list.Entries.Count}");
    }

    /// <summary>Octroie le reward de victoire battlefield au
    /// joueur — or de base S_Battlefield.RewardTiers1[0] ajouté à
    /// la bourse + attr gold (280) au client. V1 : or seulement ;
    /// l'item-récompense passe par AddItemAsync quand le mapping
    /// des tiers sera confirmé natif.</summary>
    private async Task GrantBattlefieldRewardAsync(EntryState ost,
        int bfId)
    {
        var def = _services.GameData.Battlefields
            .FirstOrDefault(x => x.Id == bfId);
        var gold = def?.RewardTiers1 is { } r && r.Length > 0
            ? r[0] : 0;
        if (gold <= 0 || ost.Session == null) return;
        ost.Gold = (int)await _services.Characters.AddGoldAsync(
            (int)ost.CharId, gold);
        await SendAttrAsync(ost.Session, ost.CharId, 0x02,
            (int)Math.Min(ost.Gold, int.MaxValue), 0,
            CancellationToken.None);
        EdenLog.Write($"[Zone] bf {bfId} reward {gold} or " +
            $"-> char={(int)ost.CharId}");
    }

    /// <summary>50 QueryMissionStates → 348 ClientMissionStates, bâti
    /// depuis les tables natives — le natif copie les maps de mission
    /// stockées SUR le personnage zone (Execute @0x827a860 : char+0x828
    /// map&lt;u16,u16&gt;, +0x840 map&lt;u16,s16&gt;…). Serialize @0x8202790 :
    ///   [s16 n][n×(u16 mid, u16 state)]   quêtes EN COURS (FMS 3/4)
    ///   [s16 n][n×(u16 mid, s16)]       progression par quête
    ///   [s16 n][n×(u16 mid, s16)]       personal_mission_states
    ///   [s16 n][n×(u32 key, u16)]       clés d'instance 0x3c52xxxx
    ///   [s16 n][n×u8]                   AllFMSIndex (bitvec)
    ///   [s16 n][n×s16]                  FMS==5 ("checked" — 266 capturés)
    ///   [u16 n][n×i32]                  mission_trace (10 slots)
    /// Capture test1 (17:22:16.260) : {1012:3,1537:0} + {1012:0} +
    /// {3749:0} + {0x3c52a854:1} + 354 u8 + 266 s16 + {1012,0×9}.
    /// CORRECTIF (bug live "plein de quêtes haut level") : l'ancien
    /// code versait TOUTES les lignes personal_mission_states dans la
    /// map1 avec état 3/4 — le client les affichait comme quêtes
    /// actives. La vraie map1 = les FMS 3/4 de final_mission_state.</summary>
    private async Task HandleQueryMissionStatesAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.CharId == 0) return;
        // FMS/PMS/trace préchargés par le pipeline d'entrée (Phase 2)
        // — valides jusqu'au premier MigrationDone (Emitted) : la
        // déclaration du perso à la mission n'a lieu qu'à ce point,
        // aucun write quête n'est donc possible entre l'install et
        // cette émission ; ensuite les requêtes live reprennent
        // (état mutable en session via les pushes mission).
        var fresh = st.Entry is { Emitted: false, MissionFms: not null,
            MissionPms: not null, MissionTrace: not null } e ? e : null;
        var fms = fresh?.MissionFms ?? await _services.MissionRepo
            .GetFMSAsync((int)st.CharId, token);
        var states = fresh?.MissionPms ?? await _services.MissionRepo
            .GetPersonalMissionStatesAsync((int)st.CharId, token);
        var trace = fresh?.MissionTrace ?? await _services.MissionRepo
            .GetMissionTraceAsync((int)st.CharId, token);
        // Trace = quêtes EN COURS seulement (FMS 3/4) : une trace périmée (quête faite ou
        // abandonnée jamais purgée) réapparaissait dans le tracker et était réécrite par le client.
        trace = trace.Select(m => m != 0 &&
            fms.GetValueOrDefault(m) is 3 or 4 ? m : 0).ToArray();

        // Map 1 — registre FMS COMPLET (mid → état 0..7). Le natif
        // copie la map du perso zone qui est elle-même la copie de la
        // map exec mission — chaque SavePersonalFMS laisse une ligne,
        // y compris les états remis à 0 (capture : {1012:3,1537:0} —
        // une quête reset 0 apparaît). Le client lit l'ÉTAT par mid,
        // pas la présence : les FMS 1 (faites) et 5 (checked) doivent
        // aussi figurer, sinon il croit les quêtes jamais vues et les
        // ré-affiche comme disponibles (bug live : marqueurs « ! »
        // sur des quêtes terminées).
        var ongoing = fms.OrderBy(kv => kv.Key).ToList();
        var msg = new CNC_CZ_ClientMissionStates();
        foreach (var kv in ongoing)
            msg.Fms.Add(new() { Mid = (ushort)kv.Key,
                State = (ushort)kv.Value });

        // Map 2 — progression par quête EN COURS (FMS 3/4 uniquement :
        // le natif n'insère dans cette map que les quêtes actives à
        // l'init — état 3 → 0, état 4 → 0xffff, désasm mission loader
        // @0x819ae30+). La ligne PMS packed status|progress<<8 fournit
        // le compteur ; la valeur native d'init est 0 (3) / 0xffff (4).
        var inProgress = ongoing.Where(kv => kv.Value is 3 or 4)
            .ToList();
        foreach (var kv in inProgress)
        {
            var pms = states.FirstOrDefault(s => s.Index == kv.Key);
            // s16 : progression — le loader natif initialise 0 pour
            // FMS 3 et 0xffff pour FMS 4 ; la progression PMS persistée
            // a priorité quand elle existe.
            var prog = pms.Index != 0 ? (short)(pms.State >> 8)
                : kv.Value == 4 ? (short)-1 : (short)0;
            msg.InProgress.Add(new() { Mid = (ushort)kv.Key,
                Progress = prog });
        }

        // Map 3 — personal_mission_states à clé ≤ 0xFFFF {mid:state}
        // (registre auxiliaire natif — capture : {3749:0}).
        var pms16 = states.Where(s => s.Index is >= 0 and <= 0xFFFF)
            .OrderBy(s => s.Index).ToList();
        foreach (var s in pms16)
            msg.Pms16.Add(new() { Mid = (ushort)s.Index,
                State = (short)s.State });

        // Signal client minimal (B3) : Pms16 mid=0 (jamais une vraie
        // quête) state=1 = Portal Stone gratuite — flag GM ou état
        // natif débutant (enchant 55869 posé pour level ≤ 80, même
        // condition que SyncBeginnerPortalStateAsync).
        if (st.Privilege > 0 || st.Level <= BeginnerPortalMaxLevel)
            msg.Pms16.Add(new() { Mid = 0, State = 1 });

        // Map 4 — clés d'instance > 0xFFFF {u32:u16} (0x3c52xxxx natif).
        var pms32 = states.Where(s => s.Index > 0xFFFF)
            .OrderBy(s => s.Index).ToList();
        foreach (var s in pms32)
            msg.Pms32.Add(new() { Key = (uint)s.Index,
                State = (ushort)s.State });

        // vec<u8> — AllFMSIndex : bitvec des quêtes TERMINÉES, indexé
        // par ORDINAL de la mission dans la liste triée par mid (PAS
        // par mid !). Désasm mission PGMissionDBO @0x819b290 :
        //   taille = (missionCount>>3)+1 − (missionCount%8==0 ? 1 : 0)
        //   for i, m in sorted(missions): if GetFMS(mid)==1 →
        //       vec[i>>3] |= 1<<(i&7)
        // Le client évalue `If_FMS <mid> done` LOCALEMENT avec ce
        // bitvec (condition col13 de T_Mission) : à zéro, aucune quête
        // n'est « done » → les quêtes finies restent marquées comme
        // disponibles (« ! » faux) et les suites de chaînes n'apparaissent
        // pas (bug live constaté). Exception native ignorée : les quêtes
        // flag col+0x24&4 sans reprise (+0x224<=0) ont leur FMS reset —
        // cas rare, non implémenté.
        var missions = _services.GameData.Missions;
        var fmsSize = (missions.Count >> 3) + 1 -
            (missions.Count % 8 == 0 ? 1 : 0);
        var fmsVec = new byte[fmsSize];
        var ord = 0;
        foreach (var m in missions.OrderBy(m => m.Id))
        {
            if (fms.TryGetValue(m.Id, out var fv) && fv == 1)
                fmsVec[ord >> 3] |= (byte)(1 << (ord & 7));
            ord++;
        }
        msg.FmsBitVec.AddRange(fmsVec);

        // vec<s16> — missions marquées "checked" (FMS==5) — la capture
        // y transportait 266 ids, alignés sur le FMS natif.
        var checkedIds = fms.Where(kv => kv.Value == 5)
            .OrderBy(kv => kv.Key).ToList();
        foreach (var kv in checkedIds)
            msg.CheckedIds.Add((short)kv.Key);

        // vec<i32> — mission_trace : les 10 slots de suivi du HUD
        // (natif : {1012, 0×9} — la quête trackée en slot 0).
        foreach (var mid in trace)
            msg.Trace.Add(mid);

        await session.SendAsync(msg, token);
        EdenLog.Write($"[Zone-M] 50->348 char=#{st.CharId} " +
            $"ongoing={ongoing.Count} pms={states.Count} " +
            $"checked={checkedIds.Count} trace={trace.FirstOrDefault()}");
    }

    /// <summary>Option de dialogue — miroir du CMissionDialog natif :
    /// SelId/SelA/SelB = les 3 champs renvoyés au serveur au clic (128) ;
    /// DisplayId/Flag/Name = ce que le client affiche dans le 285.</summary>
    internal sealed record MissionDialog(int SelId, short SelA, short SelB,
        int DisplayId, short Flag, string Name);

    private static List<int> ReadVecI32(byte[] b, ref int o)
    {
        var n = BitConverter.ToUInt16(b, o); o += 2;
        var v = new List<int>(n);
        for (var i = 0; i < n; i++) { v.Add(BitConverter.ToInt32(b, o)); o += 4; }
        return v;
    }

    private static List<short> ReadVecS16(byte[] b, ref int o)
    {
        var n = BitConverter.ToUInt16(b, o); o += 2;
        var v = new List<short>(n);
        for (var i = 0; i < n; i++) { v.Add(BitConverter.ToInt16(b, o)); o += 2; }
        return v;
    }

    /// <summary>vector&lt;vector&lt;i32&gt;&gt; : [u16 n][n×(u16 m, m×i32)] —
    /// format des membres +0x34 du paquet 56 (helper rsISt6vectorIiSaIiEE
    /// 0x8320640 : u16 outer, u16 inner, i32 elements).</summary>
    private static List<int[]> ReadVecVecI32(byte[] b, ref int o)
    {
        var n = BitConverter.ToUInt16(b, o); o += 2;
        var v = new List<int[]>(n);
        for (var i = 0; i < n; i++)
        {
            var m = BitConverter.ToUInt16(b, o); o += 2;
            var inner = new int[m];
            for (var j = 0; j < m; j++)
            { inner[j] = BitConverter.ToInt32(b, o); o += 4; }
            v.Add(inner);
        }
        return v;
    }

    /// <summary>vector&lt;vector&lt;s16&gt;&gt; : [u16 n][n×(u16 m, m×s16)] —
    /// membres +0x4c/+0x58 (helper rsISt6vectorIsSaIsEE 0x8320490).</summary>
    private static List<short[]> ReadVecVecS16(byte[] b, ref int o)
    {
        var n = BitConverter.ToUInt16(b, o); o += 2;
        var v = new List<short[]>(n);
        for (var i = 0; i < n; i++)
        {
            var m = BitConverter.ToUInt16(b, o); o += 2;
            var inner = new short[m];
            for (var j = 0; j < m; j++)
            { inner[j] = BitConverter.ToInt16(b, o); o += 2; }
            v.Add(inner);
        }
        return v;
    }

    /// <summary>vector&lt;Ss&gt; plat : [u16 n][n×(u16 len,bytes)] — helper
    /// rsISs 0x8280000 du paquet 57.</summary>
    private static List<string> ReadVecStr(byte[] b, ref int o)
    {
        var n = BitConverter.ToUInt16(b, o); o += 2;
        var v = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            var len = BitConverter.ToUInt16(b, o); o += 2;
            v.Add(Encoding.GetEncoding("GBK")
                .GetString(b, o, len).TrimEnd('\0'));
            o += len;
        }
        return v;
    }

    /// <summary>vector&lt;vector&lt;std::string&gt;&gt; : [u16 n][u16 m][m×(u16 len,bytes)].</summary>
    private static List<string[]> ReadVecVecStr(byte[] b, ref int o)
    {
        var n = BitConverter.ToUInt16(b, o); o += 2;
        var v = new List<string[]>(n);
        for (var i = 0; i < n; i++)
        {
            var m = BitConverter.ToUInt16(b, o); o += 2;
            var ss = new string[m];
            for (var j = 0; j < m; j++)
            {
                var len = BitConverter.ToUInt16(b, o); o += 2;
                ss[j] = Encoding.GetEncoding("GBK")
                    .GetString(b, o, len).TrimEnd('\0');
                o += len;
            }
            v.Add(ss);
        }
        return v;
    }

    /// <summary>57 ZoneServerReplyDialogue — layout désassemblé
    /// (Deserialize zone @0x8333770, Execute @0x83330c0) :
    /// [i32 charUid][s16 nodeId][i32 npcUid]
    /// vec&lt;s16&gt; +0x20 — u16 a (dialog+0x4)     vec&lt;i32&gt; +0x2c — selId (+0x0)
    /// vec&lt;i32&gt; +0x38 — id affiché (+0x8)      vec&lt;Ss&gt;  +0x44 — nom (+0x14)
    /// vec&lt;s16&gt; +0x50 — s16 flag (+0x10)       vec&lt;s16&gt; +0x5c — u16 b (+0x6)
    /// [i32 ctx] +0x68 — texte parlé.
    /// Execute : un CMissionDialog par j sur |vec+0x5c|, type 285 = 0xff,
    /// arg4 = +0x1c. Comme pour 56, le ctor trie par +0x8.</summary>
    private async Task ForwardReplyDialogueAsync(NetSession session, byte[] b)
    {
        var dialogs = new List<MissionDialog>();
        var ctx = 0; var npcUid = BitConverter.ToInt32(b, 6);
        var o = 10;                                     // header i32+s16+i32
        try
        {
            var v20 = ReadVecS16(b, ref o);             // u16 a
            var v2c = ReadVecI32(b, ref o);             // selId
            var v38 = ReadVecI32(b, ref o);             // id affiché
            var e = ReadVecStr(b, ref o);               // noms
            var v50 = ReadVecS16(b, ref o);             // s16 flag
            var v5c = ReadVecS16(b, ref o);             // u16 b
            ctx = BitConverter.ToInt32(b, o); o += 4;
            EdenLog.Write($"[Zone-M] 57 v20={v20.Count} v2c={v2c.Count}"
                + $" v38={v38.Count} E={e.Count}"
                + $" v50={v50.Count} v5c={v5c.Count} ctx={ctx}"
                + $" npc={npcUid} off={o}/{b.Length}");
            var n = Math.Min(v5c.Count,
                Math.Min(v2c.Count, Math.Min(v20.Count,
                    Math.Min(v38.Count, Math.Min(e.Count, v50.Count)))));
            for (var j = 0; j < n; j++)
                dialogs.Add(new MissionDialog(v2c[j], v20[j], v5c[j],
                    v38[j], v50[j], e[j]));
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-M] parse57 fail @{o}: {ex.Message}");
        }

        dialogs.Sort((x, y) => x.DisplayId.CompareTo(y.DisplayId));
        if (_states.TryGetValue(session.SessionId, out var est))
            est.MissionDialogs = dialogs;

        // Même règle que 56 : le 285 part même avec 0 entrée / ctx=-1.
        var outp = new List<byte>();
        outp.AddRange(BitConverter.GetBytes(ctx));       // i32 : texte parlé
        outp.AddRange(BitConverter.GetBytes((ushort)dialogs.Count));
        foreach (var dlg in dialogs)
        {
            var text = Encoding.GetEncoding("GBK").GetBytes(dlg.Name);
            outp.AddRange(BitConverter.GetBytes(dlg.DisplayId));
            outp.AddRange(BitConverter.GetBytes((ushort)text.Length));
            outp.AddRange(text);
            outp.AddRange(BitConverter.GetBytes(dlg.Flag));
        }
        outp.Add(0xff);                                 // u8 : type = -1 (désasm)
        outp.AddRange(BitConverter.GetBytes(npcUid));   // i32 : arg4 (+0x1c)
        await session.SendPacketAsync(285, outp.ToArray(),
            CancellationToken.None);
    }







}