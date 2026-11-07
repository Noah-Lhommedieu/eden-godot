using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Evaluate;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone;

/// <summary>Partiel gladiator trial — orchestration zone du
/// sous-système <c>CEvaluate</c> natif (preuves citées par méthode) :
/// consentement d'équipe (TeamOperation ops 4/5 → 577), entrée dans
/// l'aire du nœud type 18 (655-674), cycle de vie timed
/// (AllEVStatusTime = 0/10800/300/10 s), scoring kills/morts,
/// loterie 255, persistence player_evaluate/team_evaluate.
/// Tourne entièrement sur la boucle de zone (<see cref="ScheduleOnLoop"/>
/// pour les timers — jamais de Task.Delay dans ce fichier).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Durées du cycle — <c>AllEVStatusTime</c> natif
    /// {0, 10800, 300, 10} : run max 3 h, loterie 5 min, close 10 s.</summary>
    private const int EvalRunSec = 10800;
    private const int EvalLotterySec = 300;
    private const int EvalCloseSec = 10;
    /// <summary>Timeout du dialogue de consentement —
    /// <c>CEventTeamCheck+0x3c</c> natif = 60 s.</summary>
    private static readonly TimeSpan EvalCheckTimeout =
        TimeSpan.FromSeconds(60);
    /// <summary>Clé de l'aire d'entrée du trial — composite natif
    /// <c>nodeTid&lt;&lt;16 | 0x15</c> (EnterEvaluate zone @0x80fa620).
    /// Résolue via <c>[normal_area]</c> de la scène : la clé NORMALE
    /// 0x15 remappe l'event-area réelle (ex. S655 : 1→21).</summary>
    private const int EvalEntryAreaKey = 0x15;
    /// <summary>Types de nœud d'où l'entrée est interdite —
    /// liste native d'EnterEvaluate {4-9, 0xa, 0xc, 0xe-0x13} →
    /// texte 0x17a6 au membre concerné.</summary>
    private static readonly HashSet<int> EvalForbiddenNodeTypes =
        new() { 4, 5, 6, 7, 8, 9, 10, 12, 14, 15, 16, 17, 18, 19 };

    /// <summary>Consentement en cours par teamId —
    /// <c>map&lt;s16,set&lt;u32&gt;&gt; team+0x5c</c> natif
    /// (CForceTeam::SetCheckState mission @0x81ca540) : set[0] =
    /// membres en attente, réponse <b>0</b> → set[3] « State[Yes] »
    /// (accept), réponse <b>≠0</b> → set[1] « State[No] » (refus —
    /// EndCheck exige set[0] et set[1] vides).</summary>
    private sealed class PendingEvalCheck
    {
        /// <summary>Type de check — 2 = tour du ciel (EndCheck branche
        /// tower), 3 = épreuve évaluée.</summary>
        public int Kind;
        public int NodeTid;
        /// <summary>Le nœud a une def S_RewardScore (655-667 en FR) —
        /// sinon l'entrée transporte dans le donjon sans instance
        /// scorée (service 59 refIds 107-113, donjons « MP »).</summary>
        public bool Scored;
        /// <summary>charIds n'ayant pas encore répondu (set[0]).</summary>
        public readonly HashSet<int> Pending = new();
        public bool Done;
    }

    private readonly Dictionary<int, PendingEvalCheck> _evalChecks =
        new();
    private readonly Random _evalRng = new();

    // ============================================================
    //  Dispatch — NCID 79 TeamOperation (ops 4/5) et NCID 255.
    // ============================================================

    /// <summary>Intercepte les ops evaluate du TeamOperation — wire
    /// natif <c>[s16 op][s16 p1][s16 p2]</c> : <c>[4][3][nodeTid]</c>
    /// = lancer/rejoindre un trial, <c>[5][answer]</c> = réponse au
    /// consentement (TeamCheckAnswer). Retourne false → l'op repart
    /// au dispatcher legacy (loot/roll/raid).</summary>
    private async Task<bool> TryHandleEvaluateTeamOpAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken token)
    {
        var op = payload.Length >= 2
            ? BitConverter.ToInt16(payload, 0) : (short)0;
        var p1 = payload.Length >= 4
            ? BitConverter.ToInt16(payload, 2) : (short)0;
        var p2 = payload.Length >= 6
            ? BitConverter.ToInt16(payload, 4) : (short)0;
        if (op == 4 && p1 == 3)
        {
            await HandleEvaluateCheckAsync(session, st, p2, token);
            return true;
        }
        if (op == 4 && p1 == 2)
        {
            await HandleTowerCheckAsync(session, st, token);
            return true;
        }
        if (op == 5)
        {
            await HandleTeamCheckAnswerAsync(session, st, p1, token);
            return true;
        }
        return false;
    }

    /// <summary>NCID 255 <c>CNE_CZ_ZoneServerEvaluateLotteryOpen</c> —
    /// Execute natif @0x8251ff0 : body <c>[i32 value]</c>, exige
    /// char+0xbb4 (EvalId) puis <c>CEvaluate::LotteryOpen</c>. Valeurs :
    /// 0-4 pick, -1 claim, 5 achat d'ouverture, 7 auto-open.</summary>
    private async Task HandleEvaluateLotteryAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || st.EvalId == 0 || payload.Length < 4)
            return;
        var ev = _services.Evaluates.Get(st.EvalId);
        // La loterie n'est accessible qu'en statut 2 (boîtes tirées à End).
        if (ev == null || ev.Status != 2) return;
        var value = BitConverter.ToInt32(payload, 0);
        // Débit d'or inline (GainGold natif — débit avant d'accorder
        // l'ouverture achetée). Le coût est grade.cost×1000.
        var debited = 0;
        var effects = ev.LotteryOpen((int)st.CharId, value, amount =>
        {
            if (st.Gold < amount) return false;
            st.Gold -= amount;
            debited = amount;
            return true;
        });
        if (debited > 0)
        {
            st.Gold = (int)await _services.Characters.AddGoldAsync(
                (int)st.CharId, -debited);
            await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0,
                token);
        }
        foreach (var fx in effects)
            await ApplyLotteryEffectAsync(session, st, ev, fx, token);
    }

    /// <summary>Traduit un effet <see cref="EvaluateInstance.
    /// LotteryEffect"/> en paquet 607 type3 / texte erreur / mail —
    /// séparation domaine↔I/O (le modèle ne connaît pas le réseau).</summary>
    private async Task ApplyLotteryEffectAsync(NetSession session,
        EntryState st, EvaluateInstance ev,
        EvaluateInstance.LotteryEffect fx, CancellationToken token)
    {
        switch (fx.Kind)
        {
            case EvaluateInstance.LotteryEffectKind.Pick:
            case EvaluateInstance.LotteryEffectKind.Unopened:
            case EvaluateInstance.LotteryEffectKind.BuyResult:
                await session.SendPacketAsync(607,
                    EvaluatePackets.BuildLottery(fx.Opens, fx.ItemId,
                        fx.Count, fx.Index), token);
                break;
            case EvaluateInstance.LotteryEffectKind.Finalize:
                // Claim — SendRewardMail natif puis [3][0][0][0][-1].
                await SendEvaluateRewardMailAsync((int)st.CharId,
                    ev.ClaimWonItems((int)st.CharId));
                await session.SendPacketAsync(607,
                    EvaluatePackets.BuildLottery(0, 0, 0, -1), token);
                break;
            case EvaluateInstance.LotteryEffectKind.Error:
                if (fx.ErrorCode != 0)
                    await SendTextIndexAsync(session, (short)fx.ErrorCode,
                        token);
                break;
        }
    }

    // ============================================================
    //  Consentement — CTeamManager::TeamCheck / TeamCheckAnswer /
    //  CEventTeamCheck natifs (mission), implémentés zone-local.
    // ============================================================

    /// <summary><c>TeamOperation [4][3][nodeTid]</c> — demande de
    /// trial : rejoin si l'équipe a déjà un evalId actif (natif : le
    /// membre hérite de l'instance du coéquipier), sinon consentement
    /// 577 à tous les membres en ligne (leader seul initie —
    /// <c>EndCheck</c> n'existe que pour le type 3).</summary>
    private async Task HandleEvaluateCheckAsync(NetSession session,
        EntryState st, int nodeTid, CancellationToken token)
    {
        if (st.CharId == 0 || !st.Migrated) return;
        var scored = _services.GameData.Evaluates.ContainsKey(nodeTid);
        if (!scored &&
            !_services.GameData.Nodes.Any(n => n.Id == nodeTid))
        {
            EdenLog.Write($"[Evaluate] char={st.CharId} node={nodeTid} " +
                "inconnu — ignoré");
            return;
        }
        // Nœud sans def S_RewardScore (107-113, donjons « MP » du
        // service 59) : le natif fait consentement + EnterEvaluate
        // quand même — l'instance scorée n'existe que pour les nœuds
        // couverts par c_rewardscore (655-667).
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null) return;
        // Natif : membre déjà sur un nœud type 18 ≠ nodeTid → 0x230b.
        foreach (var m in team.Members)
        {
            var mst = FindStateByChar(m.CharacterId);
            if (mst == null || mst.NodeId == nodeTid) continue;
            var t = NodeType(mst.NodeId);
            if (t == 18)
            {
                await SendTextIndexAsync(session, 0x230b, token);
                return;
            }
        }
        // Rejoin — l'équipe a déjà une instance (team+0x9c natif).
        if (team.EvalId != 0)
        {
            var ev = _services.Evaluates.Get(team.EvalId);
            if (ev == null)
            {
                team.EvalId = 0;
                team.EvalNodeId = 0;
            }
            else
            {
                await EnterEvaluateMemberAsync(session, st, ev,
                    team.EvalNodeId, token);
                return;
            }
        }
        if ((int)st.CharId != team.LeaderCharacterId) return;
        if (_evalChecks.ContainsKey(team.Id))
        {
            // Un check est déjà en cours — texte 0x166e natif.
            await SendTextIndexAsync(session, 0x166e, token);
            return;
        }
        var check = new PendingEvalCheck
            { Kind = 3, NodeTid = nodeTid, Scored = scored };
        foreach (var m in team.Members)
            if (m.IsOnline && FindStateByChar(m.CharacterId) != null)
                check.Pending.Add(m.CharacterId);
        if (check.Pending.Count == 0) return;
        _evalChecks[team.Id] = check;
        // WorldTeamCheck natif → 577 [3][nodeTid] à chaque membre.
        var (chkOp, chkPayload) = PreparePacket(
            new CNC_CZ_ClientTeamCheckNotify
            {
                Field1 = 3,                   // s16 : check evaluate
                Field2 = (short)nodeTid,      // s16 : nœud du trial
            });
        foreach (var cid in check.Pending)
            if (FindSessionByChar(cid) is { } ms)
                await SendPreparedToAsync(ms, chkOp, chkPayload, token);
        // CEventTeamCheck natif : timeout 60 s — silence = annulation.
        ScheduleOnLoop(EvalCheckTimeout,
            _ => EvalCheckTimeoutAsync(team.Id));
        EdenLog.Write($"[Evaluate] check team={team.Id} " +
            $"node={nodeTid} members={check.Pending.Count}");
    }

    /// <summary><c>TeamOperation [5][answer]</c> — TeamCheckAnswer
    /// natif (mission CTeamManager::TeamCheckAnswer @0x80c6520 →
    /// SetCheckState @0x81ca540) : <b>0 = Oui</b> (sort de set[0],
    /// joint set[3]), <b>≠0 = Non</b> (joint set[1] → EndCheck
    /// échoue). Le client natif n'émet que 0/1 (DragMsgDlgUI,
    /// client FUN_007df8c0).</summary>
    private async Task HandleTeamCheckAnswerAsync(NetSession session,
        EntryState st, int answer, CancellationToken token)
    {
        if (st.CharId == 0) return;
        // Check solo (pas d'équipe — tour du ciel, branche native
        // « no team » de TeamOperation) : 0 → EnterTower, ≠0 →
        // annulation silencieuse ; le pending est levé dans les
        // deux cas (zone @0x82937b0, char+0xd7a).
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null)
        {
            if (st.PendingCheck == 2 && answer == 0)
            {
                st.PendingCheck = 0;
                await EnterTowerAsync(session, st, token);
                return;
            }
            st.PendingCheck = 0;
            return;
        }
        if (!_evalChecks.TryGetValue(team.Id, out var check) ||
            check.Done)
            return;
        // Sémantique native SetCheckState : 0 = « State[Yes] »
        // (set[3], accept) ; ≠0 = « State[No] » (set[1] — un refus
        // fait échouer tout le check).
        if (answer != 0)
        {
            await FailEvalCheckAsync(team.Id, token);
            return;
        }
        check.Pending.Remove((int)st.CharId);
        if (check.Pending.Count == 0)
            await FinishEvalCheckAsync(team.Id, token);
    }

    /// <summary><c>TeamOperation [4][2][0]</c> — TeamCheck tour du
    /// ciel (EnterBtn de towerwnd → zone @0x82937b0). Sans équipe :
    /// 577 [2][étage] au seul requérant + pending char+0xd7a=2.
    /// Avec équipe : même mécanique de consentement que l'épreuve —
    /// 577 [2][étage] à tous les membres en ligne (leader seul
    /// initie). L'étage = le record du perso (char+0xd76 natif, non
    /// persisté ici — entrée = étage 1).</summary>
    private async Task HandleTowerCheckAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.CharId == 0 || !st.Migrated) return;
        const short floor = 1;
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        var (chkOp, chkPayload) = PreparePacket(
            new CNC_CZ_ClientTeamCheckNotify
            {
                Field1 = 2,        // s16 : check tour du ciel
                Field2 = floor,    // s16 : étage proposé
            });
        if (team == null)
        {
            st.PendingCheck = 2;
            await SendPreparedToAsync(session, chkOp, chkPayload,
                token);
            return;
        }
        if ((int)st.CharId != team.LeaderCharacterId) return;
        if (_evalChecks.ContainsKey(team.Id))
        {
            await SendTextIndexAsync(session, 0x166e, token);
            return;
        }
        var check = new PendingEvalCheck { Kind = 2 };
        foreach (var m in team.Members)
            if (m.IsOnline && FindStateByChar(m.CharacterId) != null)
                check.Pending.Add(m.CharacterId);
        if (check.Pending.Count == 0) return;
        _evalChecks[team.Id] = check;
        foreach (var cid in check.Pending)
            if (FindSessionByChar(cid) is { } ms)
                await SendPreparedToAsync(ms, chkOp, chkPayload, token);
        ScheduleOnLoop(EvalCheckTimeout,
            _ => EvalCheckTimeoutAsync(team.Id));
        EdenLog.Write($"[Tower] check team={team.Id} " +
            $"members={check.Pending.Count}");
    }

    /// <summary><c>EnterTower</c> natif (zone @0x80f9080) pour UN
    /// membre : gates (mort → 0x115a, nœud type interdit → 0x17a6,
    /// CheckNodeTimes nœud 280 → 0x146c), puis tirage d'une scène
    /// tour parmi les nœuds type 14 (GetTowerNodeID @0x8171800 —
    /// AllSkyTowerParamMap[étage] = {280-283} en FR, toutes les
    /// variantes valent pour chaque étage) hors nœud courant, et
    /// transport vers <c>node&lt;&lt;16|0x15</c>.</summary>
    private async Task EnterTowerAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.Dead)
        {
            await SendTextIndexAsync(session, 0x115a, token);
            return;
        }
        if (EvalForbiddenNodeTypes.Contains(NodeType(st.NodeId)))
        {
            await SendTextIndexAsync(session, 0x17a6, token);
            return;
        }
        var towerMax = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == 280)?.MaxEntryTimes ?? 0;
        if (towerMax > 0 &&
            !await CheckNodeTimesAsync(st, 280, towerMax))
        {
            await SendTextIndexAsync(session, 0x146c, token);
            return;
        }
        // GetTowerNodeID natif — tirage uniforme dans le set des
        // nœuds type 14, hors nœud courant.
        var pool = _services.GameData.Nodes
            .Where(n => n.Type == 14 && n.Id != st.NodeId)
            .Select(n => n.Id).ToList();
        if (pool.Count == 0)
        {
            EdenLog.Write("[Tower] aucun nœud type 14 dans t_node");
            return;
        }
        var nodeTid = pool[_evalRng.Next(pool.Count)];
        var area = ResolveEvaluateEntryArea(nodeTid);
        if (area == null)
        {
            EdenLog.Write($"[Tower] node={nodeTid} aire " +
                $"{EvalEntryAreaKey} introuvable");
            return;
        }
        await AddNodeTimesAsync(session, st, nodeTid, token);
        st.Facing = area.Facing;
        await MigrateToNodeAsync(session, st, nodeTid,
            area.X, area.Y, area.Z, token);
        EdenLog.Write($"[Tower] char={st.CharId} → S{nodeTid:D3} " +
            $"({area.X:0.#},{area.Y:0.#})");
    }

    /// <summary>Timeout 60 s du check — <c>CEventTeamCheck::Execute</c>
    /// natif : force <c>EndCheck</c> ; les membres silencieux restent
    /// dans set[0] → le check échoue comme un refus.</summary>
    private async Task EvalCheckTimeoutAsync(int teamId)
    {
        if (!_evalChecks.TryGetValue(teamId, out var check) ||
            check.Done)
            return;
        await FailEvalCheckAsync(teamId, CancellationToken.None);
    }

    /// <summary>Annulation du check — 577 <c>[4][0]</c> (fin de
    /// vérification) à tous les membres, comme le natif.</summary>
    private async Task FailEvalCheckAsync(int teamId,
        CancellationToken token)
    {
        if (!_evalChecks.TryGetValue(teamId, out var check))
            return;
        check.Done = true;
        _evalChecks.Remove(teamId);
        var (endOp, endPayload) = PreparePacket(
            new CNC_CZ_ClientTeamCheckNotify
            {
                Field1 = 4,   // s16 : check terminé
                Field2 = 0,   // s16 : annulé/refusé
            });
        foreach (var cid in AllMemberIds(teamId))
            if (FindSessionByChar(cid) is { } ms)
                await SendPreparedToAsync(ms, endOp, endPayload, token);
    }

    /// <summary><c>EndCheck</c> type 3 natif — tous les membres ont
    /// accepté : alloue l'evalId, ferme le dialogue (577 [4][0]) et
    /// fait entrer chaque membre (gates individuels : mort 0x115a,
    /// nœud interdit 0x17a6, compteur 0x146c). Le premier entrant
    /// déclenche StartEvaluate.</summary>
    private async Task FinishEvalCheckAsync(int teamId,
        CancellationToken token)
    {
        if (!_evalChecks.TryGetValue(teamId, out var check))
            return;
        check.Done = true;
        _evalChecks.Remove(teamId);
        var team = _services.Teams.GetTeam(teamId);
        if (team == null) return;
        var memberIds = AllMemberIds(teamId);
        // 577 [4][0] — fin de la fenêtre de consentement.
        var (endOp, endPayload) = PreparePacket(
            new CNC_CZ_ClientTeamCheckNotify { Field1 = 4, Field2 = 0 });
        foreach (var cid in memberIds)
            if (FindSessionByChar(cid) is { } ms)
                await SendPreparedToAsync(ms, endOp, endPayload, token);
        // Branche EndCheck type 2 — tour du ciel : pas d'instance
        // scorée, chaque membre entre via EnterTower (gates
        // individuels identiques au natif).
        if (check.Kind == 2)
        {
            foreach (var cid in memberIds)
            {
                var mst = FindStateByChar(cid);
                var ms = FindSessionByChar(cid);
                if (mst == null || ms == null) continue;
                await EnterTowerAsync(ms, mst, token);
            }
            return;
        }
        var ev = check.Scored
            ? _services.Evaluates.Create(check.NodeTid, memberIds,
                _services.GameData.Evaluates)
            : null;
        if (check.Scored && ev == null) return;
        team.EvalId = ev?.Id ?? 0;
        team.EvalNodeId = check.NodeTid;
        var entered = 0;
        foreach (var cid in memberIds)
        {
            var mst = FindStateByChar(cid);
            var ms = FindSessionByChar(cid);
            if (mst == null || ms == null) continue;
            if (await EnterEvaluateMemberAsync(ms, mst, ev,
                    check.NodeTid, token))
                entered++;
        }
        if (ev == null)
        {
            EdenLog.Write($"[Evaluate] team={teamId} node={check.NodeTid} " +
                $"sans scoring — {entered} membre(s) transporté(s)");
            return;
        }
        if (entered == 0)
        {
            // Aucun membre n'a passé les gates — instance orpheline.
            _services.Evaluates.Remove(ev.Id);
            team.EvalId = 0;
            team.EvalNodeId = 0;
            return;
        }
        // Boîtes tirées à End pour les membres PRÉSENTS (natif : chars
        // du CNodeGroup à ce moment) — les membres qui n'ont pas
        // passé les gates ou sont hors-ligne ne comptent pas.
        ev.MemberCharIds.RemoveAll(id =>
            FindStateByChar(id)?.EvalId != ev.Id);
        // StartEvaluate natif → OnStatusTimeUp(id, 0, false).
        await EvaluateStatusTransitionAsync(ev, 0, false, token);
    }

    /// <summary><c>EnterEvaluate</c> natif (@0x80fa620) pour UN membre :
    /// gates (mort → 0x115a, nœud type interdit → 0x17a6,
    /// CheckNodeTimes → 0x146c) puis AddNodeTimes + transport vers
    /// l'aire <c>nodeTid&lt;&lt;16|0x15</c> + EvalId + 525.</summary>
    private async Task<bool> EnterEvaluateMemberAsync(
        NetSession session, EntryState st, EvaluateInstance? ev,
        int nodeTid, CancellationToken token)
    {
        if (st.EvalId != 0) return false;        // déjà en instance
        if (st.Dead)
        {
            await SendTextIndexAsync(session, 0x115a, token);
            return false;
        }
        if (EvalForbiddenNodeTypes.Contains(NodeType(st.NodeId)))
        {
            await SendTextIndexAsync(session, 0x17a6, token);
            return false;
        }
        var maxTimes = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == nodeTid)?.MaxEntryTimes ?? 0;
        if (maxTimes > 0 &&
            !await CheckNodeTimesAsync(st, nodeTid, maxTimes))
        {
            await SendTextIndexAsync(session, 0x146c, token);
            return false;
        }
        var area = ResolveEvaluateEntryArea(nodeTid);
        if (area == null)
        {
            EdenLog.Write($"[Evaluate] node={nodeTid} aire " +
                $"{EvalEntryAreaKey} introuvable");
            return false;
        }
        st.EvalId = ev?.Id ?? 0;
        if (ev != null && !ev.MemberCharIds.Contains((int)st.CharId))
            ev.MemberCharIds.Add((int)st.CharId);
        await AddNodeTimesAsync(session, st, nodeTid, token);
        // TransportCharacterToArea natif — même node (l'aire est dans
        // la scène du trial) : migration complète 293/563/302/267/313.
        st.Facing = area.Facing;
        await MigrateToNodeAsync(session, st, nodeTid,
            area.X, area.Y, area.Z, token);
        // 525 ClientGladiatorTeamInfo [evalId][point][gold][flag] —
        // assignation de l'evalId (CNE_DBA_ClientGladiatorTeamInfo
        // natif → NotifyClient si flag) ; evalId 0 + flag 0 pour un
        // nœud sans instance scorée.
        var (point, gold) = await _services.EvaluateRepo
            .GetGladiatorCurrenciesAsync((int)st.CharId);
        await session.SendAsync(new CNC_CZ_ClientGladiatorTeamInfo
        {
            Field1 = ev?.Id ?? 0, // i32 : evalId assigné (0 = non scoré)
            Field2 = point,       // i32 : colosseum_point
            Field3 = (uint)gold,  // u32 : colosseum_gold
            Field4 = (byte)(ev != null ? 1 : 0), // u8 : flag en-trial
        }, token);
        // Push statut + compteurs courants — un membre qui rejoint en
        // cours de run voit le même HUD que les autres.
        if (ev != null)
            await SendEvalStatusAsync(session, ev, token);
        return true;
    }

    // ============================================================
    //  Cycle de vie — CEvaluateFactory::OnStatusTimeUp @0x8082bb0.
    // ============================================================

    /// <summary><c>OnStatusTimeUp(id, status, flag=status==1)</c>
    /// natif — machine à états : 0→Start+run 3 h, 1→End (boss) ou
    /// AllLeaveEvaluate (timeout), 2→SendAllReward, 3→CloseEvaluate.
    /// Chaque transition reprogramme un event portant le statut
    /// attendu — le guard <c>ev.Status == expected</c> reproduit le
    /// comportement natif (un event périmé n'agit plus).</summary>
    private async Task EvaluateStatusTransitionAsync(
        EvaluateInstance ev, int status, bool timeout,
        CancellationToken token)
    {
        switch (status)
        {
            case 0:
                await EvaluateStartAsync(ev, token);
                break;
            case 1:
                if (timeout)
                    await EvaluateTimeoutAsync(ev, token);
                else
                    await EvaluateEndAsync(ev, token);
                break;
            case 2:
                await EvaluateSendAllRewardAsync(ev, token);
                break;
            case 3:
                await EvaluateCloseAsync(ev, token);
                break;
        }
    }

    /// <summary>Arme la prochaine transition — CEventEVStatus natif.
    /// L'event ne s'exécute que si l'instance est encore au statut
    /// attendu (évite qu'un timer de run de 3 h déclenche la sortie
    /// pendant la phase loterie après un kill de boss précoce).</summary>
    private void ScheduleEvalTransition(EvaluateInstance ev,
        int expectedStatus, int delaySec)
    {
        ScheduleOnLoop(TimeSpan.FromSeconds(delaySec), async ct =>
        {
            var cur = _services.Evaluates.Get(ev.Id);
            if (cur == null || cur.Status != expectedStatus) return;
            await EvaluateStatusTransitionAsync(ev, expectedStatus,
                expectedStatus == 1, ct);
        });
    }

    /// <summary><c>CEvaluate::Start</c> (@0x845cxx) — statut 1,
    /// horodatage +0x34, texte 0x1d92 au groupe, HUD 607
    /// type0/type1 ; arme le timeout du run (10800 s).</summary>
    private async Task EvaluateStartAsync(EvaluateInstance ev,
        CancellationToken token)
    {
        ev.Status = 1;
        ev.StartedAtUtc = DateTime.UtcNow;
        await BroadcastEvalTextAsync(ev, 0x1d92, token);
        await BroadcastEvalAsync(ev,
            EvaluatePackets.BuildStatus(1, EvalRunSec), token);
        await BroadcastEvalAsync(ev,
            EvaluatePackets.BuildCounters(ev), token);
        ScheduleEvalTransition(ev, 1, EvalRunSec);
        EdenLog.Write($"[Evaluate] start eval={ev.Id} " +
            $"node={ev.Def.NodeTid} members={ev.MemberCharIds.Count}");
    }

    /// <summary><c>CEvaluate::End</c> (@0x845d340) — boss tué : fige
    /// le temps, scores DSL + grade + tirage des boîtes (domaine),
    /// 607 type0 [2][300] + type2 à tous, persistence
    /// player_evaluate (+608 si record) et team_evaluate, puis
    /// arme la fin de loterie (300 s).</summary>
    private async Task EvaluateEndAsync(EvaluateInstance ev,
        CancellationToken token)
    {
        ev.Status = 2;
        var elapsed = Math.Max(1,
            (int)(DateTime.UtcNow - ev.StartedAtUtc).TotalSeconds);
        ev.End(elapsed, _evalRng);
        await BroadcastEvalAsync(ev,
            EvaluatePackets.BuildStatus(2, EvalLotterySec), token);
        await BroadcastEvalAsync(ev,
            EvaluatePackets.BuildResult(ev), token);
        await PersistEvaluateAsync(ev, token);
        ScheduleEvalTransition(ev, 2, EvalLotterySec);
        EdenLog.Write($"[Evaluate] end eval={ev.Id} score=" +
            $"{ev.TotalScore} grade={ev.Grade?.Name} " +
            $"elapsed={elapsed}s");
    }

    /// <summary>Persistence <c>End</c> natif — DBA
    /// <c>UpdatePlayerEvaluateInfo</c> par membre (upsert best,
    /// 608 si amélioré) + <c>UpdateEvaluateRank</c> (insert
    /// team_evaluate → rang 600).</summary>
    private async Task PersistEvaluateAsync(EvaluateInstance ev,
        CancellationToken token)
    {
        var grade = ev.Grade?.Name ?? "";
        // 608 identique pour tous les membres — une seule
        // sérialisation, rejouée par destinataire.
        var (infoOp, infoPayload) = PreparePacket(
            new CNC_CZ_ClientUpdateEvaluateInfo
            {
                Field1 = (uint)ev.Def.NodeTid,       // u32 : nodeTid
                Field2 = (uint)ev.ElapsedSeconds,    // u32 : time
                Field3 = (uint)ev.TotalScore,        // u32 : score
                Field4 = grade,                      // str : grade
            });
        var leaderId = 0;
        foreach (var charId in ev.MemberCharIds)
        {
            var team = _services.Teams.GetTeamOf(charId);
            if (team != null) { leaderId = team.LeaderCharacterId; break; }
        }
        foreach (var charId in ev.MemberCharIds)
        {
            EvaluateRepository.BestRecord? old = null;
            try
            {
                old = (await _services.EvaluateRepo
                        .GetBestRecordsAsync(charId))
                    .FirstOrDefault(r => r.NodeId == ev.Def.NodeTid);
                // Règle DBO native : score ↑, à égalité time ↓.
                await _services.EvaluateRepo.UpdatePlayerEvaluateAsync(
                    charId, ev.Def.NodeTid, ev.TotalScore,
                    ev.ElapsedSeconds, grade);
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Evaluate] persist char={charId} : " +
                    ex.Message);
            }
            // 608 = la règle mémoire zone (score ↑, à égalité time ↑).
            var improved = old is null || ev.TotalScore > old.Score ||
                (ev.TotalScore == old.Score &&
                 ev.ElapsedSeconds > old.Time);
            var ms = improved ? FindSessionByChar(charId) : null;
            if (ms != null &&
                FindStateByChar(charId)?.EvalId == ev.Id)
            {
                await SendPreparedToAsync(ms, infoOp, infoPayload, token);
            }
        }
        try
        {
            await _services.EvaluateRepo.InsertTeamEvaluateAsync(
                ev.Def.NodeTid, ev.TotalScore, ev.ElapsedSeconds,
                grade, leaderId, ev.MemberCharIds);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Evaluate] team_evaluate eval={ev.Id} : " +
                ex.Message);
        }
    }

    /// <summary>Timeout du run (status 1 + flag) —
    /// <c>AllLeaveEvaluate</c> natif : pas de récompenses ; chaque
    /// membre quitte l'instance (reset 607) et est ramené à son nœud
    /// sauvegardé (char+0x638 → CrossZoneNode).</summary>
    private async Task EvaluateTimeoutAsync(EvaluateInstance ev,
        CancellationToken token)
    {
        foreach (var charId in ev.MemberCharIds.ToList())
        {
            var mst = FindStateByChar(charId);
            var ms = FindSessionByChar(charId);
            if (mst == null || ms == null) continue;
            await CharacterLeaveEvaluateAsync(ms, mst, token);
            await ReturnFromEvaluateAsync(ms, mst, token);
        }
        await CloseEvaluateInstanceAsync(ev, token);
        EdenLog.Write($"[Evaluate] timeout eval={ev.Id} — sortie " +
            "sans récompense");
    }

    /// <summary><c>SendAllReward</c> natif — fin de la phase loterie
    /// (300 s) : pour chaque membre présent, auto-open des boîtes
    /// restantes + mail des gains + CharacterLeave (status → 3).</summary>
    private async Task EvaluateSendAllRewardAsync(EvaluateInstance ev,
        CancellationToken token)
    {
        ev.Status = 3;
        await BroadcastEvalAsync(ev,
            EvaluatePackets.BuildStatus(3, EvalCloseSec), token);
        foreach (var charId in ev.MemberCharIds.ToList())
        {
            var mst = FindStateByChar(charId);
            var ms = FindSessionByChar(charId);
            if (mst == null || ms == null || mst.EvalId != ev.Id)
                continue;
            ev.LotteryOpen(charId, 7, _ => true);
            await SendEvaluateRewardMailAsync(charId,
                ev.ClaimWonItems(charId));
            await CharacterLeaveEvaluateAsync(ms, mst, token);
        }
        ScheduleEvalTransition(ev, 3, EvalCloseSec);
    }

    /// <summary><c>CloseEvaluate</c> natif — destruction de
    /// l'instance : purge les evalIds résiduels des membres et libère
    /// l'équipe (team+0x9c = 0).</summary>
    private async Task EvaluateCloseAsync(EvaluateInstance ev,
        CancellationToken token)
    {
        await CloseEvaluateInstanceAsync(ev, token);
    }

    /// <summary>Sortie commune — purge EvalId des états membres,
    /// remet team.EvalId/EvalNodeId à zéro, retire l'instance du
    /// registre (CloseEvaluate natif + ClearPlayerEvaluate).</summary>
    private async Task CloseEvaluateInstanceAsync(EvaluateInstance ev,
        CancellationToken token)
    {
        foreach (var charId in ev.MemberCharIds.ToList())
        {
            var mst = FindStateByChar(charId);
            if (mst != null && mst.EvalId == ev.Id)
            {
                mst.EvalId = 0;
                var ms = FindSessionByChar(charId);
                if (ms != null)
                    await SendEvalResetAsync(ms, mst, token);
            }
            var team = _services.Teams.GetTeamOf(charId);
            if (team != null && team.EvalId == ev.Id)
            {
                team.EvalId = 0;
                team.EvalNodeId = 0;
            }
        }
        ev.Status = -1;
        _services.Evaluates.Remove(ev.Id);
        EdenLog.Write($"[Evaluate] close eval={ev.Id}");
    }

    // ============================================================
    //  Scoring — hooks kills/morts depuis Combat.Death.
    // ============================================================

    /// <summary><c>CharacterKillMonster</c> natif (@0x8083f40) —
    /// appelé depuis FinishMobDeathAsync : crédite le kill à
    /// l'instance du tueur (bucket par MonsterType 2/3-4/5-6, score =
    /// ExpReward du mob), broadcast compteurs 607 type1, et End si le
    /// mob est le boss de la def S_RewardScore.</summary>
    private async Task OnEvaluateMobKilledAsync(NetSession session,
        EntryState st, ZoneLiveWorld.LiveMob mob,
        CancellationToken token)
    {
        if (st.EvalId == 0) return;
        var ev = _services.Evaluates.Get(st.EvalId);
        if (ev == null || ev.Status != 1) return;
        // Natif CharacterKillMonster @0x8083f40 : type = data+0x10
        // (col 4 1-idx = catégorie DSL), score = data+0xc4 (col 51
        // 1-idx = score DoD dédié) — PAS MonsterType/ExpReward.
        var type = mob.Def?.KillType ?? 0;
        var score = mob.Def == null ? 0
            : mob.Def.KillScore > int.MaxValue ? int.MaxValue
            : (int)mob.Def.KillScore;
        if (ev.GainKillScore(type, score))
            await BroadcastEvalAsync(ev,
                EvaluatePackets.BuildCounters(ev), token);
        if (mob.MobId == ev.Def.BossDefId)
            await EvaluateStatusTransitionAsync(ev, 1, false, token);
    }

    /// <summary><c>CharacterDie</c> natif (@0x8081f70) — une mort de
    /// membre = +1 au compteur d'équipe (cappé au seuil DSL) puis
    /// broadcast des compteurs.</summary>
    private async Task OnEvaluatePlayerDiedAsync(EntryState st,
        CancellationToken token)
    {
        if (st.EvalId == 0) return;
        var ev = _services.Evaluates.Get(st.EvalId);
        if (ev == null || ev.Status != 1) return;
        ev.CharacterDie();
        await BroadcastEvalAsync(ev,
            EvaluatePackets.BuildCounters(ev), token);
    }

    // ============================================================
    //  Sortie — CEvaluate::CharacterLeave / transports / mails.
    // ============================================================

    /// <summary><c>CharacterLeave</c> natif (@0x8082310) — quitter
    /// l'instance (migration hors du nœud trial, déconnexion,
    /// AllLeaveEvaluate, SendAllReward) : si la loterie est ouverte
    /// (status 2) les boîtes s'auto-ouvrent et les gains partent par
    /// mail ; EvalId=0 ; packets reset <c>[0][-1][0]</c> +
    /// <c>[3][0][0][0][-2]</c> + 525 [0][point][gold][0]. Si tous
    /// les membres ont quitté en cours de run → close.</summary>
    private async Task CharacterLeaveEvaluateAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.EvalId == 0) return;
        var ev = _services.Evaluates.Get(st.EvalId);
        st.EvalId = 0;
        if (ev != null)
        {
            if (ev.Status == 2)
            {
                ev.LotteryOpen((int)st.CharId, 7, _ => true);
                await SendEvaluateRewardMailAsync((int)st.CharId,
                    ev.ClaimWonItems((int)st.CharId));
            }
            ev.MemberCharIds.Remove((int)st.CharId);
            // Plus personne en cours de run → abandon du trial.
            if (ev.Status is 0 or 1 && ev.MemberCharIds.Count == 0)
            {
                var team = _services.Teams.GetTeamOf((int)st.CharId);
                if (team != null && team.EvalId == ev.Id)
                {
                    team.EvalId = 0;
                    team.EvalNodeId = 0;
                }
                _services.Evaluates.Remove(ev.Id);
            }
        }
        await SendEvalResetAsync(session, st, token);
    }

    /// <summary>Packets de reset natifs de CharacterLeave — 607
    /// <c>[0][-1][0]</c> (statut) puis <c>[3][0][0][0][-2]</c>
    /// (loterie) + 525 <c>[0][point][gold][0]</c> (evalId libéré).</summary>
    private async Task SendEvalResetAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        await session.SendPacketAsync(607,
            EvaluatePackets.BuildStatus(-1, 0), token);
        await session.SendPacketAsync(607,
            EvaluatePackets.BuildLottery(0, 0, 0, -2), token);
        var (point, gold) = await _services.EvaluateRepo
            .GetGladiatorCurrenciesAsync((int)st.CharId);
        await session.SendAsync(new CNC_CZ_ClientGladiatorTeamInfo
        {
            Field1 = 0,           // i32 : evalId libéré
            Field2 = point,       // i32 : colosseum_point
            Field3 = (uint)gold,  // u32 : colosseum_gold
            Field4 = 0,           // u8  : flag hors-trial
        }, token);
    }

    /// <summary>Retour au nœud sauvegardé — char+0x638 natif =
    /// CrossZoneNode/position mémorisés par MigrateToNodeAsync à
    /// l'entrée du trial (CrossZoneData).</summary>
    private async Task ReturnFromEvaluateAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var node = st.CrossZoneNode != 0 ? st.CrossZoneNode : 1;
        await MigrateToNodeAsync(session, st, node, (float)st.CrossZoneX,
            (float)st.CrossZoneY, 0, token);
    }

    /// <summary>Mails de récompense — <c>SendRewardMail</c> natif
    /// (@0x845cd80) : un mail système par item (sender 0x1fd4
    /// « Shaxia Oracle », title 0x1fd2, body 0x1fd4→0x1fd3 —
    /// T_TextIndex), pièce jointe mailitem avec durability=count.</summary>
    private async Task SendEvaluateRewardMailAsync(int charId,
        List<(int ItemId, int Count)> items)
    {
        if (items.Count == 0) return;
        var texts = _services.GameData.Texts;
        var sender = texts.GetValueOrDefault(0x1fd4, "Shaxia Oracle");
        var title = texts.GetValueOrDefault(0x1fd2,
            "Trial: Wonderland Reward");
        var body = texts.GetValueOrDefault(0x1fd3, "");
        foreach (var (itemId, count) in items)
            try
            {
                await _services.EvaluateRepo.InsertRewardMailAsync(
                    charId, itemId, count, sender, title, body);
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Evaluate] reward mail char={charId} " +
                    $"item={itemId} : {ex.Message}");
            }
    }

    // ============================================================
    //  Helpers — compteurs d'entrées, aire d'entrée, broadcasts.
    // ============================================================

    /// <summary><c>CheckNodeTimes</c> natif (@0x8409fe0) — laisse
    /// passer si le compteur <c>player_node_times</c> du nœud est
    /// sous le max du template (+0x8a = MaxEntryTimes). Charge la
    /// map paresseusement (natif : chargée au login).</summary>
    private async Task<bool> CheckNodeTimesAsync(EntryState st,
        int nodeTid, int maxTimes)
    {
        st.NodeTimes ??= await _services.EvaluateRepo
            .GetNodeTimesAsync((int)st.CharId);
        var cur = st.NodeTimes.GetValueOrDefault(nodeTid, 0);
        return cur < maxTimes;
    }

    /// <summary><c>AddNodeTimes</c> natif (@0x841e9a0) — +1 au
    /// compteur d'entrées du nœud (plages natives [100,300)∪[600,900),
    /// cap 499), persisté + push 563
    /// <c>ClientUpdateNodeTimes [s16 node][u16 times]</c>.</summary>
    private async Task AddNodeTimesAsync(NetSession session,
        EntryState st, int nodeTid, CancellationToken token)
    {
        if (nodeTid is < 100 or >= 300 and < 600 or >= 900) return;
        st.NodeTimes ??= await _services.EvaluateRepo
            .GetNodeTimesAsync((int)st.CharId);
        var cur = st.NodeTimes.GetValueOrDefault(nodeTid, 0) + 1;
        if (cur > 499) return;
        st.NodeTimes[nodeTid] = cur;
        try
        {
            await _services.EvaluateRepo.SetNodeTimesAsync(
                (int)st.CharId, nodeTid, cur);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Evaluate] node_times char={st.CharId} " +
                $"node={nodeTid} : {ex.Message}");
        }
        await session.SendAsync(new CNC_CZ_ClientUpdateNodeTimes
        {
            Field1 = (short)nodeTid,   // s16 : nœud comptabilisé
            Field2 = (ushort)cur,      // u16 : entrées cumulées
        }, token);
    }

    /// <summary>Aire d'entrée du trial — <c>InitEventArea</c> natif
    /// remappe les event-areas des aires NORMALES sous leur clé :
    /// <c>[normal_area] ea,key,…</c> → l'aire effective 0x15 =
    /// l'event-area dont NormalLinks vaut 21 (S655 : ea 1 → key 21).
    /// Repli : event-area littérale 21, puis première position.</summary>
    private SceneParser.EventArea? ResolveEvaluateEntryArea(int nodeTid)
    {
        var scene = _services.Scenes.GetScene($"S{nodeTid:D3}");
        if (scene == null) return null;
        var eaId = scene.NormalLinks
            .Where(kv => kv.Value == EvalEntryAreaKey)
            .Select(kv => kv.Key).FirstOrDefault();
        var area = scene.EventAreas.FirstOrDefault(a =>
            a.Id == (eaId != 0 ? eaId : EvalEntryAreaKey));
        return area;
    }

    /// <summary>Type S_Node du nœud (0 si inconnu).</summary>
    private int NodeType(int nodeId) =>
        _services.GameData.Nodes.FirstOrDefault(n => n.Id == nodeId)
            ?.Type ?? 0;

    /// <summary>charIds des membres de l'équipe (tous — online ou
    /// non ; la persistence native les enregistre à l'entrée).</summary>
    private List<int> AllMemberIds(int teamId) =>
        _services.Teams.GetTeam(teamId)?.Members
            .Select(m => m.CharacterId).ToList() ?? new List<int>();

    /// <summary>EntryState du perso en ligne (null si hors zone).</summary>
    private EntryState? FindStateByChar(int charId) =>
        _states.Values.FirstOrDefault(s => s.CharId == (uint)charId);

    /// <summary>Session du perso en ligne (null si hors zone).</summary>
    private NetSession? FindSessionByChar(int charId)
    {
        foreach (var s in _services.Sessions.All)
            if (_states.TryGetValue(s.SessionId, out var st) &&
                st.CharId == (uint)charId)
                return s;
        return null;
    }

    /// <summary>607 à tous les membres présents dans l'instance —
    /// <c>NotifyClient</c> natif itère les chars du nœud ayant
    /// char+0xbb4 == evalId.</summary>
    private async Task BroadcastEvalAsync(EvaluateInstance ev,
        byte[] body, CancellationToken token)
    {
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.EvalId != ev.Id)
                continue;
            try { await s.SendPacketAsync(607, body, token); }
            catch { /* session morte */ }
        }
    }

    /// <summary>316 TextIndex à tous les membres de l'instance —
    /// texte de groupe natif (ex. 0x1d92 « le trial commence »).</summary>
    private async Task BroadcastEvalTextAsync(EvaluateInstance ev,
        short index, CancellationToken token)
    {
        var (op, payload) = PreparePacket(new CNC_CZ_ClientTextOutputIndex
        { Field1 = index, Field2 = "" });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.EvalId != ev.Id)
                continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }

    /// <summary>Push statut + compteurs à UN membre (entrée/rejoin) —
    /// le client affiche le HUD du run en cours.</summary>
    private async Task SendEvalStatusAsync(NetSession session,
        EvaluateInstance ev, CancellationToken token)
    {
        var secondsLeft = ev.Status switch
        {
            1 => Math.Max(0, EvalRunSec -
                (int)(DateTime.UtcNow - ev.StartedAtUtc).TotalSeconds),
            2 => EvalLotterySec,
            3 => EvalCloseSec,
            _ => 0,
        };
        await session.SendPacketAsync(607,
            EvaluatePackets.BuildStatus(ev.Status, secondsLeft), token);
        await session.SendPacketAsync(607,
            EvaluatePackets.BuildCounters(ev), token);
    }
}
