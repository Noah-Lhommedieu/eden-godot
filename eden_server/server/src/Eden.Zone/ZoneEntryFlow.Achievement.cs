using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Succès joueur — portage de <c>CGlobalInfo::SetAchievement</c>
/// (mission @0x0808cd70) et du relais zone
/// <c>CNE_ZMis_ZoneServerAchievementUpdate::Execute</c>
/// (zone @0x082fdfa0) → <c>CNC_CZ_ClientAchievementUpdate</c> (480).
///
/// Format wire 480 (désasm Serialize @0x08209080) :
///   [u8 type] puis —
///     type 0 : [u16 n][i32 achievementId][u32 point]×n — synchro
///              initiale (insert-if-absent) des succès COMPLÉTÉS,
///              envoyée par paquets de 315 (SendAllAchievement natif).
///     type 1 : même trame — mise à jour {id → points cumulés}
///              (upsert côté client).
///     type 2 : [u32 total] — cumul catégorie 1 (groupes combinés
///              101-108 / 201-202).
///     type 3 : [u32 total] — cumul catégorie 5 (groupes 501-506).
///     type 4 : [u16 n][i32 id]×n — succès « town » mensuels
///              (player_town_achievement_month — non porté).
///     type 5 : [u16 n][i32 id]×n — liste d'ids divers.
///
/// Sémantique de grant (SetAchievement natif) :
///   • nouvelle entrée : type 4 → success_num=NeedNum + point ;
///     sinon success_num=1, point si NeedNum&lt;2 — INSERT puis cascade
///     de groupes (GetReachedAchievementGroupID @0x080764a0 : les
///     succès-groupe dont tous les membres ont une ligne sont octroyés
///     récursivement avec delta=-1).
///   • entrée existante : success_num++ ; si success_num % NeedNum
///     == 0 → point += (success_num==NeedNum ? def.Point : delta) ;
///     plafonds 99999 (par succès) / 999999999 (total) → texte 0x6ab.
///   • notifié seulement si point gagné OU succès complété —
///     sinon la ligne est persistée silencieusement.
///   • franchissement du seuil col9 → récompenses : appellation
///     (col10), mail item (col11), prestige (col12/col13).
///
/// La mission native recevait les grants via AgentDBACommand
/// « Achievement id = %d point = %hd » (TC_SetAchievement zone) ;
/// chez nous la zone applique la sémantique directement en mémoire —
/// même observable client (480) et persistante (table identique).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Charge paresseuse de la progression succès —
    /// <c>Load Personal Achievement Data</c> natif (SELECT au login,
    /// map exec+0x528). Le total exec+0x53c = somme des points.</summary>
    private async Task EnsureAchievementsLoadedAsync(EntryState st)
    {
        if (st.Achievements != null) return;
        st.Achievements = await _services.AchievementRepo
            .LoadAsync((int)st.CharId);
        long total = 0;
        foreach (var kv in st.Achievements)
            total += kv.Value.Point;
        st.AchievementTotalPoint = total;
    }

    /// <summary>Synchro initiale après migration —
    /// <c>SendAllAchievement</c> natif : tous les succès COMPLÉTÉS
    /// (success_num ≥ NeedNum) en 480 type 0, paquets de 315.</summary>
    private async Task SendAllAchievementsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        await EnsureAchievementsLoadedAsync(st);
        var done = new List<(int Id, uint Point)>();
        foreach (var kv in st.Achievements!)
        {
            var def = _services.Achievements.Get(kv.Key);
            if (def == null) continue;
            if (kv.Value.SuccessNum >= Math.Max(1, def.NeedNum))
                done.Add((kv.Key, (uint)kv.Value.Point));
        }
        for (var i = 0; i < done.Count; i += 315)
        {
            var msg = NewAchievementPairs(0,
                done.Skip(i).Take(315));
            await session.SendAsync(msg, token);
        }
        // Extension Eden (480 kind 6) : compteurs success_num des succès
        // commencés mais non complétés, pour l'affichage « n / requis ».
        var partial = new List<(int Id, uint N)>();
        foreach (var kv in st.Achievements!)
        {
            var def = _services.Achievements.Get(kv.Key);
            if (def == null) continue;
            if (kv.Value.SuccessNum > 0 &&
                kv.Value.SuccessNum < Math.Max(1, def.NeedNum))
                partial.Add((kv.Key, (uint)kv.Value.SuccessNum));
        }
        for (var i = 0; i < partial.Count;
             i += Codecs.AchievementProgressPackets.ChunkSize)
            await session.SendPacketAsync(480,
                Codecs.AchievementProgressPackets.Build(partial
                    .Skip(i).Take(
                        Codecs.AchievementProgressPackets.ChunkSize)),
                token);
    }

    /// <summary>480 type 0/1 : [u8][u16 n][i32 id][u32 point]×n —
    /// Serialize natif zone @0x08209080.</summary>
    private static Eden.Core.Networking.Messages.Generated
        .CNC_CZ_ClientAchievementUpdate NewAchievementPairs(sbyte type,
        IEnumerable<(int Id, uint Point)> entries)
    {
        var msg = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAchievementUpdate { Kind = type };
        foreach (var e in entries)
            msg.Entries.Add(new() { AchievementId = e.Id,
                Counter = e.Point });
        return msg;
    }

    /// <summary>Grant d'un succès — <c>SetAchievement</c> natif.
    /// <paramref name="delta"/> = le s16 du script
    /// (<c>Event_SetAchievement id point flag</c>) ; -1 = valeur de la
    /// donnée (def.Point). Retourne le point gagné (0 = silencieux).</summary>
    private async Task GrantAchievementAsync(NetSession session,
        EntryState st, int achId, int delta, CancellationToken token,
        int depth = 0)
    {
        if (depth > 8 || achId <= 0) return;
        await EnsureAchievementsLoadedAsync(st);
        var def = _services.Achievements.Get(achId);
        if (def == null)
        {
            EdenLog.Write($"[Achievement] char={st.CharId} id={achId} " +
                "inconnu de S_Achievement");
            return;
        }
        var need = Math.Max(1, def.NeedNum);
        var progress = st.Achievements!;
        int successNum, gained, newPoint, oldPoint = 0;
        bool isNew = !progress.TryGetValue(achId, out var cur);
        if (isNew)
        {
            // Nouvelle ligne : type4 → complet d'office ; sinon
            // success_num=1 (+point si NeedNum<2).
            successNum = def.Type == 4 ? need : 1;
            gained = (def.Type == 4 || need < 2) ? def.Point : 0;
            newPoint = gained;
            progress[achId] = (successNum, newPoint);
            await _persistence.EnqueueAsync((int)st.CharId,
                ct => _services.AchievementRepo.UpsertAsync(
                    (int)st.CharId, achId, successNum, newPoint));
        }
        else
        {
            oldPoint = cur.Point;
            if (st.AchievementTotalPoint > 999999999)
            {
                await SendTextIndexAsync(session, 0x6ab, token);
                return;
            }
            successNum = cur.SuccessNum + 1;
            // Natif : le cap 99999 est testé sur (old + delta) AVANT
            // la logique de multiple — le delta est « réservé » même
            // s'il n'est pas appliqué.
            var deltaEff = delta == -1 ? def.Point : delta;
            if (oldPoint + deltaEff > 99999)
            {
                await SendTextIndexAsync(session, 0x6ab, token);
                return;
            }
            gained = 0;
            newPoint = oldPoint;
            if (successNum % need == 0)
            {
                // Multiple de NeedNum : point accordé — def.Point à la
                // complétion exacte, le delta du script aux multiples
                // ultérieurs (succès re-déclenché après complétion).
                gained = successNum == need ? def.Point : deltaEff;
                newPoint = oldPoint + gained;
            }
            progress[achId] = (successNum, newPoint);
            await _persistence.EnqueueAsync((int)st.CharId,
                ct => _services.AchievementRepo.UpsertAsync(
                    (int)st.CharId, achId, successNum, newPoint));
        }
        // Silencieux : pas de point et pas complété — la ligne est
        // persistée mais le client n'est notifié qu'à la complétion
        // (natif : « if (local_19e==0 && success_num != NeedNum)
        // return »). La cascade de groupes est calculée AVANT ce
        // retour dans la branche « nouvelle entrée » : elle se
        // poursuit même pour un grant silencieux.
        if (gained == 0 && successNum != need)
        {
            // Compteur partiel → client (extension 480 kind 6).
            if (need > 1)
                await session.SendPacketAsync(480,
                    Codecs.AchievementProgressPackets.Build(
                        [(achId, (uint)successNum)]), token);
            if (isNew)
                await GrantReachedGroupsAsync(session, st, achId,
                    token, depth);
            return;
        }
        st.AchievementTotalPoint += gained;
        // CumulativeAchievement natif : cumuls par clé col5 (type,
        // SAUF type 5 exclu) et clé combinée col6*100+col7 — la map
        // alimente les totaux 2/3.
        if (gained != 0)
        {
            if (def.Type != 5)
                st.AchievementGroups[(short)def.Type] =
                    st.AchievementGroups.GetValueOrDefault(
                        (short)def.Type) + gained;
            st.AchievementGroups[(short)def.GroupKey] =
                st.AchievementGroups.GetValueOrDefault(
                    (short)def.GroupKey) + gained;
        }
        await session.SendAsync(NewAchievementPairs(1,
            [(achId, (uint)newPoint)]), token);
        // Émissions catégorielles natives : groupe combiné dans
        // 101-108/201-202 → type 2 = map[1] ; 501-506 → type 3 = map[5].
        if (gained != 0)
        {
            var g = def.GroupKey;
            sbyte? t = null;
            if ((g > 200 && g < 0xcb) || (ushort)(g - 0x65) < 8)
                t = 2;
            else if ((ushort)(g - 0x1f5) < 6)
                t = 3;
            if (t != null)
                await session.SendAsync(
                    new Eden.Core.Networking.Messages.Generated
                        .CNC_CZ_ClientAchievementUpdate
                    {
                        Kind = t.Value,
                        Total = (uint)st.AchievementGroups
                            .GetValueOrDefault(
                                (short)(t == 2 ? 1 : 5)),
                    }, token);
        }
        // Màj du total affiché (player_achievement_rank.total_point —
        // best-effort, la ligne complète est tenue par le classement).
        if (gained != 0)
            await _persistence.EnqueueAsync((int)st.CharId,
                ct => _services.AchievementRepo.UpdateRankTotalAsync(
                    (int)st.CharId, st.CharName ?? "", st.Level,
                    (int)Math.Min(st.AchievementTotalPoint,
                        int.MaxValue), st.AchievementGroups));
        // Succès famille — SetAchievement natif appelle
        // CFamily::UpdataAchievement (mission @0x081be030) quand
        // exec+0xb0 (family_id) résout : upsert family_achievement
        // (clé GroupKey), compteurs, spell_point (si elfking),
        // family_log type 4, broadcast 67 flag=0 aux membres.
        // Chez nous le monde applique via cette commande ZW.
        if (gained != 0 && _world.Connected
            && _services.Guilds.GetGuildOf((int)st.CharId) is not null)
            await _world.SendTextCommandAsync(
                $"family_achievement {st.CharId} {achId} {def.Type} " +
                $"{def.GroupKey} {gained}", token);
        // Récompenses au franchissement du seuil col9 (une seule fois
        // par succès : old < seuil <= new).
        if (def.RewardThreshold > 0 && oldPoint < def.RewardThreshold
                && def.RewardThreshold <= newPoint
                && st.AchievementRewarded.Add(achId))
            await RunAchievementRewardsAsync(session, st, def, token);
        // Cascade de groupes — uniquement à la création de ligne
        // (natif : GetReachedAchievementGroupID n'est appelé que dans
        // la branche « nouvelle entrée »).
        if (isNew)
            await GrantReachedGroupsAsync(session, st, achId, token,
                depth);
    }

    /// <summary>Succès de kill — condition native de la boucle
    /// receveurs : absent de la map perso → grant ; présent ET
    /// point ≠ 0 → re-grant (success_num++, point si multiple) ;
    /// présent ET point == 0 → ignoré (rien à ajouter).</summary>
    private async Task GrantMobAchievementAsync(NetSession session,
        EntryState st,
        Eden.Core.Data.Parsers.GameDataParsers.AchievementDef def)
    {
        await EnsureAchievementsLoadedAsync(st);
        if (st.Achievements!.ContainsKey(def.Id) && def.Point == 0)
            return;
        await GrantAchievementAsync(st.Session ?? session, st, def.Id,
            def.Point, CancellationToken.None);
    }

    /// <summary>Succès d'obtention d'item —
    /// <c>CItemFactory::SetItemAchievement</c> @zone:0x080aed40 :
    /// la donnée item porte achId (= template id) + point (+0x1c,
    /// s16) ; le grant n'est émis que si l'id est ABSENT de la map
    /// de succès du perso (char+0xa48) — pas de re-grant sur item
    /// déjà collectionné (contrairement au kill). itemdata+0x14==5
    /// → variante « TownAchievement_Month » mensuelle non portée
    /// (aucune donnée S_Achievement ne la référence).</summary>
    private async Task TryGrantItemAchievementAsync(NetSession session,
        EntryState st, int itemId)
    {
        var def = _services.Achievements.Get(itemId);
        if (def == null) return;
        await EnsureAchievementsLoadedAsync(st);
        if (st.Achievements!.ContainsKey(itemId)) return;
        await GrantAchievementAsync(st.Session ?? session, st, itemId,
            def.Point, CancellationToken.None);
    }

    /// <summary>Grant depuis un event de scène. Pipeline natif :
    /// <c>ParsingEventAchievement</c> réécrit le verbe en
    /// « Event_SetAchievement id def.Point flag », la mission le route
    /// via <c>TC_Event_SetAchievement</c> (@0x080ee2b0) qui émet la
    /// transition zone <c>set_achievement id point (flag != 0xd)</c>,
    /// puis <c>TC_SetAchievement</c> (zone @0x083d51d0) : flag=0 →
    /// exécutant seul, flag≠0 → tous les membres locaux de l'équipe
    /// (<c>CForceTeam::GetLocalMembers</c>). Le flag de script 0xd
    /// (13) = « solo » — valeur dominante dans les scènes.</summary>
    private Task GrantSceneAchievementAsync(NetSession session,
        EntryState st, int achId, int flag, CancellationToken token)
        => GrantAchievementToTargetsAsync(session, st, achId,
            _services.Achievements.Get(achId)?.Point ?? -1,
            flag != 0 && flag != 0xd, token);

    /// <summary>Transition <c>set_achievement id point flag</c> —
    /// <c>TC_SetAchievement</c> zone @0x083d51d0 : flag≠0 + équipe →
    /// tous les membres locaux, sinon l'exécutant seul. Le point est
    /// le delta natif (utilisé uniquement aux multiples de NeedNum
    /// post-complétion ; les nouvelles lignes prennent def.Point).</summary>
    private async Task GrantAchievementToTargetsAsync(
        NetSession session, EntryState st, int achId, int delta,
        bool teamWide, CancellationToken token)
    {
        // Natif : id absent de S_Achievement → ligne supprimée au
        // parsing mission ; le verbe n'exécute jamais.
        if (_services.Achievements.Get(achId) == null) return;
        if (teamWide)
        {
            var team = _services.Teams.GetTeamOf((int)st.CharId);
            if (team != null)
            {
                foreach (var m in LocalTeamMembers(st, team))
                    await GrantAchievementAsync(m.Session ?? session,
                        m, achId, delta, token);
                return;
            }
        }
        await GrantAchievementAsync(session, st, achId, delta, token);
    }

    /// <summary>Succès-groupes nouvellement atteints — cascade native
    /// de <c>SetAchievement</c> (chaque groupe reçu est re-granté avec
    /// delta=-1 → sa propre cascade se poursuit).</summary>
    private async Task GrantReachedGroupsAsync(NetSession session,
        EntryState st, int achId, CancellationToken token, int depth)
    {
        foreach (var gid in _services.Achievements.GroupsOf(achId))
        {
            if (st.Achievements!.ContainsKey(gid)) continue;
            if (!_services.Achievements.GroupReached(gid,
                    st.Achievements))
                continue;
            await GrantAchievementAsync(session, st, gid, -1, token,
                depth + 1);
        }
    }

    /// <summary>Récompenses de seuil — branche native
    /// <c>local_184 &lt; puVar8[8] &lt;= local_170</c> : appellation
    /// (INSERT player_appellation + 398 + refresh stats), mail item
    /// (SendSystemMail textes 0x8f5/0x8f6/0x8f7, %s = nom du succès,
    /// item col11 ×1), prestige (col12/col13).</summary>
    private async Task RunAchievementRewardsAsync(NetSession session,
        EntryState st, Eden.Core.Data.Parsers.GameDataParsers
            .AchievementDef def, CancellationToken token)
    {
        if (def.AppellationId != 0)
        {
            var owned = await _services.Characters
                .GetAppellationsAsync((int)st.CharId);
            if (!owned.Contains(def.AppellationId))
            {
                await _services.Characters.AddAppellationAsync(
                    (int)st.CharId, def.AppellationId);
                await session.SendAsync(
                    new Eden.Core.Networking.Messages.Generated
                        .CNC_CZ_ClientAddAppellation
                    { Value = def.AppellationId }, token);
                await ProfileStatEmitter.SendForCharacterAsync(session,
                    _services, (int)st.CharId, token);
            }
        }
        if (def.ItemReward != 0)
        {
            var texts = _services.GameData.Texts;
            var title = texts.GetValueOrDefault(0x8f5,
                "Achievement Reward");
            var sender = texts.GetValueOrDefault(0x8f6, "System");
            var body = texts.GetValueOrDefault(0x8f7, "%s")
                .Replace("%s", def.Name);
            try
            {
                await _persistence.EnqueueAsync((int)st.CharId,
                    ct => _services.AchievementRepo
                        .InsertRewardMailAsync((int)st.CharId,
                            def.ItemReward, sender, title, body));
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Achievement] mail récompense " +
                    $"char={st.CharId} ach={def.Id} : {ex.Message}");
            }
        }
        if (def.AlignId != 0 && def.AlignExp != 0)
            // GainPrestigeExp natif (mission @0x0808c1b0) : la zone
            // monte « GainExp Prestige id = exp » à la mission qui
            // persiste + broadcast ; chez nous le texte part en ZW 636
            // vers le monde qui applique + renvoie 60/update_prestige.
            await GainPrestigeExpAsync(st, (short)def.AlignId,
                def.AlignExp, token);
    }
}
