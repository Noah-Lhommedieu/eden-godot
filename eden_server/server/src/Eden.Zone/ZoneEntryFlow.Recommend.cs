// ZoneEntryFlow.Recommend.cs — activités recommandées hebdomadaires.
//
// Chaîne native prouvée :
//   - Chargement : rows `recommend_activities` +
//     `player_characters.recommend_activities_time` (chargé par
//     CExecutionerDBO::LoadRecommendActivities mission, même base edenfr_db).
//   - Reset hebdo : CPlayerFactory::RefreshRecommendActivities
//     @0x81098a0 → ResetRecommendActivitiesByWeek @0x8108e60 :
//     semaine différente OU map vide → clear + peuplement des defs
//     éligibles (min_lv<=lv<=max_lv, flag==0) + refresh 831.
//   - Progression : 832 M→Z → map ; count>=target → pending +
//     FinishedProcess @0x840c0b0 (DBA « Update RecommendActivity »
//     Finished=0 + client 560 flag 0).
//   - Claim : 558 flag!=0 → CheckFinished @0x840be30 : pending &&
//     !claimed && progress>=def.Target && GiveReward @0x810ba80 →
//     claimed=1 + DBA « Update RecommendActivity » Finished=1 +
//     client 560 flag 1.
//   - Récompenses @0x810ba80 : item (+0x28,+0x2c), enchant
//     (+0x20, durée +0x24×10), exp (+0x30 × level_data+0x58 = col21
//     S_Level), cp (+0x34 × +0x5c = col22), gold (+0x3c),
//     lifelong (+0x38 → DBA « GMGainLifelong Money = %d »).

using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using EdenLog = Eden.Core.Logging.EdenLog;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Charge l'état activités à l'entrée du perso : lignes
    /// persistées + reset hebdo éventuel (appelé depuis Lifecycle).</summary>
    private async Task InitRecommendActivitiesAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var charId = (int)st.CharId;
        st.RecommendTime = await _characters
            .GetRecommendActivitiesTimeAsync(charId);
        var rows = await _characters.GetRecommendActivitiesAsync(charId);
        foreach (var (id, progress, finished, tracing) in rows)
        {
            st.RecommendProgress[id] = progress;
            var def = _services.GameData.GetRecommendActivity(id);
            // Pending natif (map +0xca8) = reconstruit à la charge :
            // objectif atteint et non réclamé.
            if (finished != 0)
                st.RecommendFinished[id] = 1;
            else if (def != null && progress >= (uint)def.Target)
                st.RecommendFinished[id] = 0;
            if (tracing != 0) st.RecommendTracing.Add(id);
        }

        // Reset hebdo (RefreshRecommendActivities @0x81098a0) : la
        // semaine (année+semaine ISO) du timestamp stocké diffère de
        // la semaine courante, ou la map est vide → ResetByWeek.
        if (!RecommendResetNeeded(st)) return;
        st.RecommendProgress.Clear();
        st.RecommendFinished.Clear();
        // ResetByWeek @0x8108e60 : eligible = byte(+0x05)<=lv &&
        // (byte(+0x04)==0 || lv<=byte(+0x04)) && byte(+0x08)==0.
        foreach (var def in _services.GameData.RecommendActivities)
        {
            if (def.Flag != 0) continue;
            if (st.Level < def.MinLevel) continue;
            if (def.MaxLevel != 0 && st.Level > def.MaxLevel) continue;
            st.RecommendProgress[(ushort)def.Id] = 0;
        }
        st.RecommendTime = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // 831 refresh → mission : DELETE+INSERT recommend_activities +
        // UPDATE recommend_activities_time (SaveRecommendActivities +
        // GetRAUdateTimeUpdateSQLCmd natifs).
        if (_services.Mission is { } ml)
            await ml.SendRefreshRecommendActivitiesAsync(
            st.CharId, st.RecommendTime,
            st.RecommendProgress
                .Select(kv => (kv.Key, kv.Value,
                    (byte)(st.RecommendFinished.ContainsKey(kv.Key) ? 1 : 0)))
                .ToList(),
            st.RecommendTracing, token);
    }

    /// <summary>La semaine ISO du timestamp stocké diffère de la
    /// semaine courante, ou aucune activité chargée — même condition
    /// d'agrégat que le natif (année/jour comparés + map vide).</summary>
    private static bool RecommendResetNeeded(EntryState st)
    {
        if (st.RecommendProgress.Count == 0) return true;
        var now = DateTime.UtcNow;
        var last = DateTimeOffset.FromUnixTimeSeconds(st.RecommendTime)
            .UtcDateTime;
        // Équivalent du natif (tm_year + tm_yday comparés) : le reset
        // est hebdomadaire — on compare (année, semaine ISO).
        var weekNow = System.Globalization.ISOWeek.GetWeekOfYear(now);
        var weekLast = System.Globalization.ISOWeek.GetWeekOfYear(last);
        return last.Year != now.Year || weekLast != weekNow;
    }

    /// <summary>832 ZoneServerUpdateRecommendActivities [i32 char]
    /// [u16 id][u32 count] — la mission pousse une progression ; si la
    /// cible est atteinte → pending + FinishedProcess.</summary>
    private async Task OnMissionRecommendUpdateAsync(NetSession session,
        EntryState st, byte[] b, CancellationToken token)
    {
        if (b.Length < 8) return;
        var id = (ushort)BitConverter.ToUInt16(b, 4);   // u16 : recommend_id
        var count = (uint)BitConverter.ToInt32(b, 6);   // u32 : progression
        st.RecommendProgress[id] = count;
        var def = _services.GameData.GetRecommendActivity(id);
        if (def == null || count < (uint)def.Target) return;
        if (st.RecommendFinished.ContainsKey(id)) return;
        // Objectif atteint → map pending (+0xca8, flag 0 = non réclamé)
        // puis RecommendActivitiesFinishedProcess @0x840c0b0.
        st.RecommendFinished[id] = 0;
        // DBA « Update RecommendActivity ActivityID = %hu
        // Parameter = %u Finished = %hd » (UpdateRecommendActivity
        // @0x8344380 → CDBAgent::Command).
        if (_services.Mission is { } ml0)
            await ml0.SendAgentDbaCommandAsync(st.CharId,
            $"Update RecommendActivity ActivityID = {id} " +
            $"Parameter = {count} Finished = 0", token);
        // 560 ClientUpdateRecommendActivities [u16 id][i32 count][u16 0]
        await session.SendAsync(
            new CNC_CZ_ClientUpdateRecommendActivities
            { Field1 = id, Field2 = count, Field3 = 0 }, token);
    }

    /// <summary>558 QueryRecommendActivities [u8 flag][u16 id] —
    /// flag==0 : liste 559 ; sinon CheckFinished (réclamation).</summary>
    private async Task OnQueryRecommendActivitiesAsync(NetSession session,
        EntryState st, byte flag, ushort id, CancellationToken token)
    {
        if (flag == 0)
        {
            // 559 ClientRecommendActivities :
            // [s16 0][i32 ts][u16 n][n×(u16,u32,u8)][u16 m][m×u16] —
            // Serialize zone : u8 = présence dans la map terminées.
            var msg = new CNC_CZ_ClientRecommendActivities
            { Field1 = 0, Field2 = st.RecommendTime };
            foreach (var kv in st.RecommendProgress)
                msg.Entries.Add((kv.Key, kv.Value,
                    (byte)(st.RecommendFinished.ContainsKey(kv.Key)
                        ? 1 : 0)));
            foreach (var t in st.RecommendTracing)
                msg.Tracing.Add(t);
            await session.SendAsync(msg, token);
            return;
        }

        // flag!=0 → CCharacter::RecommendActivitiesCheckFinished(id)
        // @0x840be30 : pending requis, non réclamé, progression ≥ cible.
        if (!st.RecommendFinished.TryGetValue(id, out var claimed)
            || claimed != 0)
            return;
        if (!st.RecommendProgress.TryGetValue(id, out var count))
            return;
        var def = _services.GameData.GetRecommendActivity(id);
        if (def == null || count < (uint)def.Target)
            return;
        if (!await GiveRecommendRewardAsync(session, st, def, token))
            return;
        st.RecommendFinished[id] = 1;
        if (_services.Mission is { } ml1)
            await ml1.SendAgentDbaCommandAsync(st.CharId,
            $"Update RecommendActivity ActivityID = {id} " +
            $"Parameter = {count} Finished = 1", token);
        await session.SendAsync(
            new CNC_CZ_ClientUpdateRecommendActivities
            { Field1 = id, Field2 = count, Field3 = 1 }, token);
    }

    /// <summary>GiveRecommendActivitiesReward @0x810ba80 — ordre natif :
    /// item (échec si inventaire plein → texte 0x4f9, return 0), enchant,
    /// puis exp/cp/gold/lifelong.</summary>
    private async Task<bool> GiveRecommendRewardAsync(NetSession session,
        EntryState st, ProgressionParsers.RecommendActivityDef def,
        CancellationToken token)
    {
        if (def.RewardItemId != 0)
        {
            var touched = new List<(int BagId, int Slot, bool Merged)>();
            var (ok, _, _, _) = await _services.Inventory.AddItemAsync(
                (int)st.CharId, def.RewardItemId,
                Math.Max(1, def.RewardItemCount), 0, 0, false, touched);
            if (!ok)
            {
                // Natif : aucun slot libre → TextOutput 0x4f9 + échec.
                await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                { Field1 = 0x4f9, Field2 = "" }, token);
                return false;
            }
        }
        if (def.EnchantId != 0 && def.EnchantDuration != 0)
            // EffectToLife(char, char, enchant, durée×10, lv1) natif.
            await AddPlayerEnchantAsync(session, st, (int)st.CharId,
                (short)def.EnchantId, def.EnchantDuration * 10, 1, token);
        if (def.ExpMultiplier != 0)
        {
            // base = CLevelData+0x58 du niveau du perso (col 21 S_Level
            // — champ exposé DefenseMin par le parser historique).
            var lvRow = _services.GameData.Levels
                .FirstOrDefault(l => l.Level == st.Level);
            if (lvRow != null)
                await GrantCharExpAsync(session, st,
                    (long)def.ExpMultiplier * lvRow.DefenseMin);
        }
        // Ordre natif @0x810ba80 : GainExp → GainGold → GainClassPoint.
        if (def.Gold != 0)
        {
            st.Gold = (int)await _services.Characters.AddGoldAsync(
                (int)st.CharId, def.Gold);
            await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0,
                token);
        }
        if (def.ClassPointMultiplier != 0)
        {
            var lvRow = _services.GameData.Levels
                .FirstOrDefault(l => l.Level == st.Level);
            if (lvRow != null)
                // base cp = CLevelData+0x5c (col 22 S_Level).
                await GrantClassPointAsync(session, st,
                    def.ClassPointMultiplier * lvRow.DefenseMax);
        }
        if (def.LifelongGold != 0)
            // « GMGainLifelong Money = %d » — DBA @0x810ba80.
            if (_services.Mission is { } ml2)
                await ml2.SendAgentDbaCommandAsync(st.CharId,
                $"GMGainLifelong Money = {def.LifelongGold}", token);
        return true;
    }

    /// <summary>Émet 559 complet — appelé après chargement login et
    /// après chaque 832 pour rafraîchir la liste chez le client
    /// (le natif ne réémet pas spontanément sur 832 ; seul 560 part).</summary>
    private Task SendRecommendListAsync(NetSession session, EntryState st,
        CancellationToken token) =>
        OnQueryRecommendActivitiesAsync(session, st, 0, 0, token);
}
