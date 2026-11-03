using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>
/// Moteur de quêtes du MissionServer C# — reconstruit le comportement
/// natif observable : ListDialogue (56) fusionne les #:Talk du NPC
/// (S_Npc.ini) et les entrées de quête (S_Mission.ini : col11 = NPC
/// donneur, col16 = objectif, col17 = NPC de rendu, col20 = récompenses).
/// Le clic (128) renvoie un selId opaque que le moteur encode lui-même
/// (le client ne fait que l'écho) :
///   1..999        = ordinal du #:Talk du NPC → réponse 57 ctx=bodyId
///   1_000_000+    = quête : (selId/10)%100000 = missionId, selId%10 =
///                   action (0=mission-open, 1=mission-finish,
///                   2=mission-on-going) → 58 TransitionCommands
///                   "shop &lt;verb&gt; &lt;npcUid&gt; &lt;mid&gt;" (la zone
///                   traduit en 388 EnterMissionList, flux prouvé).
/// Les quêtes s'acceptent/rendent ensuite via MissionServiceOp (NCID 21
/// DBA relayé du client 87 MissionListOp : op 1=accept, 2=reward,
/// 3=on-going, 4=finish).
/// État persisté : personal_mission_states (index=mid,
/// state = status | progress&lt;&lt;8 ; status 1=active, 2=objectifs OK)
/// + player_mission_complete_num pour les quêtes terminées (If_FMS done).
/// </summary>
/// <summary>Partiel événements — ops de service, abandon,
/// kill de mob, obtention/changement d'items de quête, voyage
/// d'area, envois d'état au client (SendState/SendPms).</summary>
public sealed partial class QuestEngine
{
    /// <summary>État quête attendu par le client (valeurs observées
    /// dans la capture native) : interne 1=active → 3, interne 2=
    /// objectifs remplis → 4 (348 test3 : mid 1009 → 4), finie → 1
    /// (391 post-claim `870100f403000001000000`), retirée → 0.</summary>
    private static int ClientState(int status) => status switch
    {
        1 => 3,
        2 => 4,
        _ => 0,
    };
    /// <summary>67 UpdateMissionState → zone → client 391.
    /// Wire natif décodé (zsniff 17:23:22-25) :
    ///   [i32 charUid][u8 keyType][i32 key][s16 state][s16 progress]
    /// keyType 0 = la clé est l'id de mission (accept → `00 <mid> 3 0`,
    /// finish → `00 <mid> 1 0`) ; keyType 1 = clé d'instance d'objectif
    /// (ids 0x3c52xxxx de personal_mission_states — non reproduite, les
    /// updates par mid suffisent au journal client).</summary>
    private static async Task SendStateAsync(MissionSession s, int charUid,
        int mid, int state, int progress = 0)
    {
        var outp = new List<byte>();
        outp.AddRange(BitConverter.GetBytes(charUid));          // i32 : charUid
        outp.Add(0);                                            // u8 : keyType=mid
        outp.AddRange(BitConverter.GetBytes(mid));              // i32 : mission id
        outp.AddRange(BitConverter.GetBytes((short)state));     // s16 : état client
        outp.AddRange(BitConverter.GetBytes((short)progress));  // s16 : progression
        await s.SendAsync(67, outp.ToArray());
    }
    /// <summary>67 UpdateMissionState kind=1 — compteur d'objectif.
    /// Wire : [i32 charUid][u8 1][u32 pmsKey][u16 count][s16 0] —
    /// relayé par la zone en 391 ; le client fait défiler le « n/req »
    /// du tracker sur CE paquet (natif IncPMS→NotifyClientMissionState
    /// avec kind=1, clé=PMS, u16=count — mission/decompiled.c
    /// @0x80969a0).</summary>
    private static async Task SendPmsAsync(MissionSession s, int charUid,
        uint pmsKey, int count)
    {
        var outp = new List<byte>();
        outp.AddRange(BitConverter.GetBytes(charUid));          // i32 : charUid
        outp.Add(1);                                            // u8 : keyType=PMS
        outp.AddRange(BitConverter.GetBytes(pmsKey));           // u32 : clé PMS
        outp.AddRange(BitConverter.GetBytes((short)count));     // s16 : compteur
        outp.AddRange(BitConverter.GetBytes((short)0));         // s16 : extra
        await s.SendAsync(67, outp.ToArray());
    }
    /// <summary>MissionServiceOp mid op npcUid — ops du panneau quête :
    /// 1 = accept (mission-open), 2 = reward, 3 = on-going,
    /// 4 = finish (mission-finish), 7 = share.</summary>
    public async Task OnServiceOpAsync(MissionSession s, int charUid,
        int mid, int op, int npcUid)
    {
        if (!_chars.TryGetValue(charUid, out var c)) return;
        var m = _data.Missions.FirstOrDefault(x => x.Id == mid);
        switch (op)
        {
            case 1: // accept
            case 7: // accept d'une quête partagée (SetFMS(3) natif)
                await AcceptMissionAsync(s, c, m, mid);
                break;
            case 6: // partage : npcUid = charUid du destinataire
                await OnShareAsync(s, charUid, mid, npcUid);
                break;
            case 2: // reward claim — même validation que finish
            case 4: // finish
                if (m == null || !c.Active.TryGetValue(mid, out var fq) ||
                    fq.Status == 0xFE) return;
                // Validation finale native : TOUS les compteurs PMS
                // remplis (parler seul ne suffit jamais) + les items
                // ItemCollect/NPCItem réellement présents — sinon le
                // client peut « rendre » une quête inachevée (bug live
                // donjon : boss jamais tués, rendu accepté).
                if ((PmsObjectives(m).Count > 0 && !AllPmsDone(fq, m)) ||
                    !await AllItemsDoneAsync(charUid, m))
                {
                    fq.Status = 1;
                    c.FMS[mid] = 3;
                    await _db.SetFMSAsync(charUid, mid, 3);
                    await SendStateAsync(s, charUid, mid,
                        ClientState(1), fq.Progress);
                    EdenLog.Write($"[Mission] char={charUid} finish " +
                        $"{mid} REFUSÉ — objectifs incomplets");
                    return;
                }
                // Sac trop plein pour TOUTES les récompenses d'item
                // (pire cas, piles fusionnées comprises) : remise
                // refusée, la quête reste « à rendre » — le natif
                // interdit de rendre sac plein. 633 = « Pas assez de
                // place dans le sac à dos ».
                if (!await HasRewardRoomAsync(charUid, m, npcUid))
                {
                    await SendTransitionAsync(s, c.NodeId, charUid,
                        "eden-text 633");
                    EdenLog.Write($"[Mission] char={charUid} finish " +
                        $"{mid} REFUSÉ — sac plein (récompenses)");
                    return;
                }
                // Consomme les items collectés/livrés (natif :
                // RemoveFinishItem au SetFMS done).
                foreach (var (itemId, req) in FinishTakeItems(m))
                    await SendTransitionAsync(s, c.NodeId, charUid,
                        $"eden-take-item {itemId} {req}");
                c.Active.Remove(mid);
                var reprenable = false;
                if (m.CompleteLimit > 0 && IsRepeatable(m))
                    // Quête à limite : compteurs +0x224/+0x226 natifs —
                    // reprenable si le FMS final repasse à 0.
                    reprenable =
                        await TrackLimitedCompletionAsync(c, m, mid);
                else
                {
                    c.Completed.Add(mid);
                    // FMS 4→1 = terminée (transition SetFMS native) +
                    // retrait du tracker.
                    c.FMS[mid] = 1;
                    await _db.SetFMSAsync(charUid, mid, 1);
                    await _db.MarkCompletedAsync(charUid, mid);
                }
                await _db.ClearTraceAsync(charUid, mid);
                await _db.DeleteActiveAsync(charUid, mid);
                await _db.DeletePmsAsync(charUid, mid);
                await ApplyRewardsAsync(s, c, m, npcUid);
                await SendStateAsync(s, charUid, mid, 1);
                // Quête à limite reprenable : le natif enchaîne
                // SetFMS(0) juste après la notif « finie » — le
                // client la retire de la liste des terminées.
                if (reprenable)
                    await SendStateAsync(s, charUid, mid, 0);
                // Séquence native post-claim (capture 17:23:22.574) :
                // 388 op=4 mission-finish (ferme l'UI de récompense).
                // 0 en arg npc → la zone substitue DialogNpcUid (pour
                // op=2 le troisième champ wire est l'index de choix,
                // pas un npcUid).
                await SendTransitionAsync(s, c.NodeId, charUid,
                    $"shop mission-finish 0 {mid}");
                await PushNpcSymbolsAsync(s, c);
                var (lg, le) = RewardGoldExp(m);
                EdenLog.Write($"[Mission] char={charUid} finish {mid} " +
                    $"+{lg}g +{le}xp (base)");
                break;
            default:
                EdenLog.Write($"[Mission] ServiceOp char={charUid} " +
                    $"mid={mid} op={op} ignoré");
                break;
        }
    }
    /// <summary>143 GiveUpMission [i32 charUid][s16 nodeId][s16 mid].</summary>
    public async Task OnGiveUpAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 8) return;
        var charUid = BitConverter.ToInt32(b, 0);
        var mid = BitConverter.ToInt16(b, 6);
        if (!_chars.TryGetValue(charUid, out var c) ||
            !c.Active.TryGetValue(mid, out var aq)) return;
        // Abandonnée : le marqueur 0xFE persiste pour que les quêtes
        // auto-grant (tutoriel sans donneur) ne reviennent pas au
        // prochain login — DeleteActive le purgerait sinon.
        aq.Status = 0xFE;
        // FMS → 0 (abandon natif) + retrait du tracker HUD + purge des
        // compteurs PMS (RemovePMS natif).
        c.FMS.Remove(mid);
        await _db.SetFMSAsync(charUid, mid, 0);
        await _db.ClearTraceAsync(charUid, mid);
        await _db.DeletePmsAsync(charUid, mid);
        await _db.SaveActiveAsync(charUid, mid, aq.Packed);
        await SendStateAsync(s, charUid, mid, 0);
        await PushNpcSymbolsAsync(s, c);
        EdenLog.Write($"[Mission] char={charUid} abandon {mid}");
    }
    /// <summary>144 MobDieReport [u16 mobId][s16][s16][f32 x][f32 z]
    /// [u16 n][n×i32 charUid] — crédit de kill par tueur.</summary>
    public async Task OnMobDieAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 18) return;
        var mobId = BitConverter.ToUInt16(b, 0);
        var count = BitConverter.ToUInt16(b, 14);
        var o = 16;
        for (var i = 0; i < count && o + 4 <= b.Length; i++, o += 4)
        {
            var charUid = BitConverter.ToInt32(b, o);
            if (!_chars.TryGetValue(charUid, out var c)) continue;
            foreach (var m in _data.Missions)
            {
                if (!c.Active.TryGetValue(m.Id, out var q) ||
                    q.Status is 2 or 0xFE)
                    continue;
                // Crédit kill par clé PMS : UNIQUEMENT MonsterKilled
                // mobId=n. MonsterTreasureGot progresse à l'obtention
                // de l'item (OnQuestItemGot) — pas au kill. Le client
                // compte PAR objectif : la clé doit matcher le mob tué
                // (natif : GenMonsterKilledMission "MonsterKilled %d
                // %hd" + IncPMS sur la clé pms_id).
                var mid = (uint)m.Id * 1_000_000u;
                foreach (var o2 in m.Actions)
                {
                    if (o2.Name != "MonsterKilled" || o2.Args.Count < 2 ||
                        !int.TryParse(o2.Args[0], out var t) ||
                        t != mobId) continue;
                    var key = mid + 10000u + (uint)(mobId - 55000);
                    var req = Math.Max(1,
                        int.TryParse(o2.Args[^1], out var n) ? n : 1);
                    var cur = q.Pms.GetValueOrDefault(key);
                    if (cur >= req) continue;   // objectif déjà plein
                    await CreditPmsAsync(s, c, q, m, key, cur + 1);
                }
            }
        }
    }
    /// <summary>145 QuestItemGot [i32 charUid][s16 mid][u16 itemId] —
    /// la zone a inséré un item de quête (drop MonsterTreasureGot) dans
    /// l'inventaire du joueur : la progression avance de 1, pas au kill.
    /// Ne crédite que si la mission déclare bien un objectif
    /// MonsterTreasureGot sur cet item.</summary>
    public async Task OnQuestItemGotAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 8) return;
        var charUid = BitConverter.ToInt32(b, 0);
        var mid = BitConverter.ToInt16(b, 4);
        var itemId = BitConverter.ToUInt16(b, 6);
        if (!_chars.TryGetValue(charUid, out var c) ||
            !c.Active.TryGetValue(mid, out var q) ||
            q.Status is 2 or 0xFE) return;
        var m = _data.Missions.FirstOrDefault(d => d.Id == mid);
        if (m == null) return;
        var midU = (uint)mid * 1_000_000u;
        foreach (var o2 in m.Actions)
        {
            if (o2.Name != "MonsterTreasureGot" || o2.Args.Count < 4 ||
                !int.TryParse(o2.Args[2], out var it) || it != itemId ||
                !int.TryParse(o2.Args[0], out var mob)) continue;
            var key = midU + 20000u + (uint)(mob - 55000);
            var req = Math.Max(1,
                int.TryParse(o2.Args[3], out var n) ? n : 1);
            var cur = q.Pms.GetValueOrDefault(key);
            if (cur >= req) continue;
            await CreditPmsAsync(s, c, q, m, key, cur + 1);
            EdenLog.Write($"[Mission] char={charUid} {mid} " +
                $"treasure item={itemId} pms={key}→{cur + 1}/{req}");
        }
    }
    /// <summary>146 QuestItemChanged [i32 charUid][u16 itemId] —
    /// notification interne zone→mission émise à chaque gain/perte
    /// d'item en inventaire (loot, remise quête, achat boutique,
    /// consommation, suppression). Équivalent natif :
    /// <c>CGlobalInfo::UpdateQuestItemStatus</c> @0x8096b10 — à chaque
    /// insert/delete dans le miroir d'inventaire de l'exécuteur, les
    /// missions contenant l'item dans leur liste collect (+0x110)
    /// réévaluent <c>inventory+storage ≥ requis</c> : bit du bitfield
    /// +0x12 mis à jour, <c>SetFMS(3)</c> si un item requis disparaît,
    /// <c>FMSCheckReward→SetFMS(4)</c> si tout est rempli.
    /// Notre miroir = <see cref="MissionDb.CountItemAsync"/> (la base).
    /// Le client surveille la clé pms <c>mid×1e6+100000</c> par mission
    /// (MC_FinishItemCollect côté client : map clé→mission) — on émet
    /// donc le 391 kind=1 sur cette clé pour rafraîchir le tracker.</summary>
    public async Task OnQuestItemChangedAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 6) return;
        var charUid = BitConverter.ToInt32(b, 0);
        var itemId = BitConverter.ToUInt16(b, 4);
        if (!_chars.TryGetValue(charUid, out var c)) return;
        foreach (var m in _data.Missions)
        {
            if (!c.Active.TryGetValue(m.Id, out var q) ||
                q.Status == 0xFE) continue;
            var hit = false;
            foreach (var o in m.Actions)
            {
                // Clé pms « collecte » native : ItemCollect itemId n et
                // MonsterTreasureGot mobId rate% itemId n partagent le
                // même mécanisme — le compteur = min(items détenus,
                // requis), sur la clé déclarée de l'objectif (GetPMS
                // natifs : 100000 pour ItemCollect — client
                // MC_FinishItemCollect — et mid×1e6+20000+(mob−55000)
                // pour MonsterTreasureGot, voir PmsObjectives). Sans le
                // cas MonsterTreasureGot, les compteurs « 0/3 » des
                // drops de quête restaient figés au seed initial.
                int it, need;
                uint key;
                if (o.Name is "ItemCollect" or "AcceptInventoryItem" &&
                    o.Args.Count >= 2 &&
                    int.TryParse(o.Args[0], out it) && it == itemId)
                {
                    need = Math.Max(1,
                        int.TryParse(o.Args[1], out var n) ? n : 1);
                    key = (uint)m.Id * 1_000_000u + 100000u;
                }
                else if (o.Name == "MonsterTreasureGot" &&
                    o.Args.Count >= 4 &&
                    int.TryParse(o.Args[2], out it) && it == itemId &&
                    int.TryParse(o.Args[0], out var mobId))
                {
                    need = Math.Max(1,
                        int.TryParse(o.Args[3], out var n) ? n : 1);
                    key = (uint)m.Id * 1_000_000u + 20000u +
                        (uint)(mobId - 55000);
                }
                else continue;
                hit = true;
                var owned = await _db.CountItemAsync(charUid, itemId,
                    Stackable(itemId));
                var val = Math.Min(owned, need);
                if (q.Pms.GetValueOrDefault(key) == val) continue;
                q.Pms[key] = val;
                await _db.SavePmsAsync(charUid, (int)key, val);
                await SendPmsAsync(s, charUid, key, val);
            }
            if (!hit) continue;
            // Transition FMS dans les deux sens (natif : bit clear →
            // SetFMS(3) quand l'item requis n'est plus détenu ; bit set
            // → FMSCheckReward → SetFMS(4) quand tout est rempli).
            await ReevaluateCompletionAsync(s, c, q, m);
        }
    }
    /// <summary>147 AreaTravelNotify [i32 charUid][s16 nodeId]
    /// [s16 keyId] — la zone signale que le joueur a franchi l'aire
    /// déclencheur <c>node×100+keyId</c> (keyId ∈ [51,59], plage native
    /// des déclencheurs héro — CheckNodeExit @0x8241960 fait
    /// TriggerArea(char, node&lt;&lt;16|key)). Crédite les objectifs
    /// <c>AreaTravel</c> dont le code aire correspond — natif
    /// GetAreaTravelPMS : clé = mid×1e6+70000+((key−0x32)+node×10).</summary>
    public async Task OnAreaTravelAsync(MissionSession s, byte[] b)
    {
        if (b.Length < 8) return;
        var charUid = BitConverter.ToInt32(b, 0);
        var node = BitConverter.ToInt16(b, 4);
        var key = BitConverter.ToInt16(b, 6);
        await CreditAreaTravelAsync(s, charUid, node, key);
    }
}
