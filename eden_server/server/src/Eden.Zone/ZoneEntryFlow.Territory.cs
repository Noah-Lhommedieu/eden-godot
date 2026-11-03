using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Guerre de territoire + élimination de familles — NCIDs zone
/// 189/190/225, formats natifs prouvés par désassemblage :
/// <list type="bullet">
///   <item>189 <c>CNE_CZ_ZoneServerTerritoryQuery</c> (vide) —
///   Execute zone @0x82944a0 : refuse les "newer towns"
///   (node ∈ {94, 768} → texte 0x2080), rate-limit 500 ms partagé
///   char+0x650, puis <c>TerritoryDataQuery</c> → 515.</item>
///   <item>190 <c>CNE_CZ_ZoneServerTerritoryJoin</c>
///   [i32 terrId][s16 type] — Execute zone @0x8249970 : encagé
///   (node 996) silencieux, rate-limit, lock versus silencieux,
///   pending → 0x1160, team &gt; 10 → 0x1368, IsInTerritory +
///   terrId==-1 → CharacterLeave, level&lt;30/fam==0 → 0xd16,
///   nodes interdits → 0x18cd/0x1d98/0x678 ; mission
///   <c>TC_TerritoryJoin</c> @0x8236860 : phase ∉{1,2} → 0x67a,
///   Join==1 → 0x1177, Join==3 → téléportation (node du territoire)
///   + LeaveFromTeam.</item>
///   <item>225 <c>CNE_CZ_ZoneServerFamilyBattleAskInfo</c>
///   [s16 type] — Execute zone @0x8253740 : type 0 →
///   <c>FamilyBattleJudgeQuery</c> (561 type 0 = classement par
///   <c>familybattle_ranks</c>) ; type 1 → TerritoryDataQuery (515)
///   + <c>FamilyBattleCheckJoin</c> (561 type 1 = phase) ; type 2 →
///   <c>Join</c> zone @0x8253430 (gates + TerritoryJoin type=2 +
///   LeaveFromTeam).</item>
/// </list>
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>189 `ZoneServerTerritoryQuery` — wire vide. Le natif
    /// refuse dans les villes récentes (node 94 夢之聖徽城 / 768
    /// 聖城水脈 → texte 0x2080), rate-limite sur char+0x650 (500 ms)
    /// puis répond 515 (données territoires + fenêtres).</summary>
    private async Task<bool> HandleTerritoryQueryAsync(
        NetSession session, EntryState st, CancellationToken ct)
    {
        // IsInNewerTown natif : nodeData+4 ∈ {0x5e, 0x300} = node_id.
        if (st.NodeId is 94 or 768)
        {
            await SendTextIndexAsync(session, 0x2080, ct);
            return true;
        }
        if (!FamilyOpRateLimit(st)) return true;   // char+0x650 500 ms
        await session.SendPacketAsync(
            (ushort)FamilyBattlePackets.NcidTerritoryQuery, BuildTerritoryQuery(st), ct);
        return true;
    }

    /// <summary>190 `ZoneServerTerritoryJoin` [i32 terrId][s16 type]
    /// — type 0 = guerre de territoire par id (mission type=1),
    /// type 1 = family battle de la famille du joueur (mission
    /// type=2), terrId == -1 en territoire = quitter.</summary>
    private async Task<bool> HandleTerritoryJoinAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 6) return true;
        var reader = new InBinStream(payload);
        var territoryId = reader.ReadInt();   // i32 — territory_id
        var joinType = reader.ReadShort();    // s16 — 0=war, 1=battle

        // IsEncaged natif : node 996 (監牢) → log + silence.
        if (st.NodeId == 996)
        {
            EdenLog.Write($"[Zone-Territory] join refusé (encagé) " +
                $"char={st.CharId}");
            return true;
        }
        if (!FamilyOpRateLimit(st)) return true;
        // char+0x3b & 0x40 — verrou versus/instance : silence natif.
        if (InVersusScene(st) || st.VersusPending)
        {
            if (st.VersusPending)
                await SendTextIndexAsync(session, 0x1160, ct);
            return true;
        }
        // Équipe > 10 membres (team+0x54) → 0x1368.
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team != null && team.Members.Count > 10)
        {
            await SendTextIndexAsync(session, 0x1368, ct);
            return true;
        }
        // IsInTerritory (node type 8) + terrId -1 → CharacterLeave.
        if (NodeType(st) == 8)
        {
            if (territoryId == -1)
            {
                var left = _services.Territory.Leave((int)st.CharId);
                EdenLog.Write($"[Zone-Territory] leave char=" +
                    $"{st.CharId} terr={left?.Id}");
                return await LeaveBattleNodeAsync(session, st, ct);
            }
            // Déjà dans un territoire : le natif laisse passer le
            // join vers un autre node de guerre seulement via leave.
        }
        // LevelLimit 30 + famille requise → 0xd16 (ordre natif :
        // après les checks de scène).
        var guild = _services.Guilds.GetGuildOf((int)st.CharId);
        if (st.Level < TerritoryWarService.LevelLimit || guild == null)
        {
            await SendTextIndexAsync(session, 0xd16, ct);
            return true;
        }
        // Scènes interdites : BR (0xf) → 0x18cd, versus instance
        // (0x11) → 0x1d98, instances/greedy/crusade
        // {4-9,0xa,0xc-0x13} → 0x678.
        var nt = NodeType(st);
        if (nt == 0xf)
        {
            await SendTextIndexAsync(session, 0x18cd, ct);
            return true;
        }
        if (nt == 0x11)
        {
            await SendTextIndexAsync(session, 0x1d98, ct);
            return true;
        }
        if (nt is >= 4 and <= 0x13 and not 0xb)
        {
            await SendTextIndexAsync(session, 0x678, ct);
            return true;
        }
        return await DoTerritoryJoinAsync(session, st, guild,
            territoryId, joinType, ct);
    }

    /// <summary>Chaîne mission <c>TC_TerritoryJoin</c> @0x8236860 :
    /// résolution de la cible (type 0 → GetTerritory, type 1 →
    /// GetFamilyBattle), gate phase ∈{1,2} → 0x67a, inscription des
    /// camps exigée pour le type battle (silence natif sinon), Join :
    /// Full → 0x1177, Success → LeaveFromTeam + téléportation vers le
    /// node de bataille.</summary>
    private async Task<bool> DoTerritoryJoinAsync(NetSession session,
        EntryState st, GuildService.Guild guild, int territoryId, int joinType,
        CancellationToken ct)
    {
        _services.Territory.RefreshPhases(_services.GameData.Schedules,
            DateTimeOffset.UtcNow);
        var terr = joinType == 0
            ? _services.Territory.GetTerritory(territoryId)
            : _services.Territory.GetFamilyBattle(guild.Id);
        if (terr == null || terr.Phase is not (1 or 2))
        {
            // battle+8 ∉ {1,2} → texte 0x67a natif.
            await SendTextIndexAsync(session, 0x67a, ct);
            return true;
        }
        // type battle : la famille doit être inscrite dans un camp
        // (mission native : silence si absente des maps +0x30/+0x48).
        if (joinType == 1 && terr.IsFamilyBattle
            && !terr.DefenderFamilies.Contains(guild.Id)
            && !terr.AttackerFamilies.Contains(guild.Id))
            return true;
        var res = _services.Territory.TryJoin(terr, (int)st.CharId,
            guild.Id, out var camp);
        switch (res)
        {
            case TerritoryWarService.JoinResult.Success:
                break;
            case TerritoryWarService.JoinResult.Full:
                // retour 1 → texte 0x1177 (nom du territoire).
                await SendTextIndexAsync(session, 0x1177, ct);
                return true;
            default:
                // AlreadyIn (2) / NoFamily (0) : silence natif.
                return true;
        }
        EdenLog.Write($"[Zone-Territory] join char={st.CharId} " +
            $"terr={terr.Id} camp={camp} node={terr.BattleNodeId}");
        // LeaveFromTeam natif (Join zone @0x8253430 + 190).
        _services.Teams.LeaveTeam((int)st.CharId);
        // char+0xb70/+0xb74/+0xb9c natifs — posés via exec+0x118/+0x11c
        // à la migration côté natif ; noms/famille retenus pour les
        // émissions 532/562/566 de la bataille.
        st.TerritoryId = terr.Id;
        st.TerritoryCamp = (ushort)camp;
        st.TerritoryKills = 0;
        _services.Territory.NoteJoinInfo(terr, (int)st.CharId, camp,
            st.CharName, guild.Id, guild.Name);
        // Teleport vers le node de bataille (TransportCharacterToNode
        // natif sur terr+0xc) — position d'entrée du node.
        var scene = _services.Scenes.GetScene(
            $"S{terr.BattleNodeId:D3}");
        var (ex, ey, ez) = ResolveInstanceEntryPos(scene,
            Random.Shared);
        await MigrateToNodeAsync(session, st, terr.BattleNodeId,
            ex, ey, ez, ct);
        return true;
    }

    /// <summary>225 `ZoneServerFamilyBattleAskInfo` [s16 type] —
    /// aiguillage natif Execute @0x8253740.</summary>
    private async Task<bool> HandleFamilyBattleAskInfoAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 2) return true;
        var type = BitConverter.ToInt16(payload, 0); // s16 — sous-op
        switch (type)
        {
            case 0: // FamilyBattleJudgeQuery → 561 type 0 (ranks)
            {
                if (_services.Social == null) return true;
                var ranks = await _services.Social
                    .GetFamilyBattleRanksAsync();
                await session.SendPacketAsync(
                    (ushort)FamilyBattlePackets.NcidFamilyBattleInfo,
                    FamilyBattlePackets.FamilyBattleRanks(ranks
                        .Select(r => (r.FamilyName, r.Energy))
                        .ToList()), ct);
                return true;
            }
            case 1: // TerritoryDataQuery + FamilyBattleCheckJoin
            {
                await session.SendPacketAsync(
                    (ushort)FamilyBattlePackets.NcidTerritoryQuery, BuildTerritoryQuery(st), ct);
                var guild = _services.Guilds.GetGuildOf(
                    (int)st.CharId);
                _services.Territory.RefreshPhases(
                    _services.GameData.Schedules, DateTimeOffset.UtcNow);
                var battle = guild != null
                    ? _services.Territory.GetFamilyBattle(guild.Id)
                    : null;
                await session.SendPacketAsync(
                    (ushort)FamilyBattlePackets.NcidFamilyBattleInfo,
                    FamilyBattlePackets.FamilyBattlePhase(
                        battle?.Phase ?? 0), ct);
                return true;
            }
            case 2: // Join @0x8253430 — gates + TerritoryJoin type=2
                return await FamilyBattleJoinAsync(session, st, ct);
            default:
                return true;
        }
    }

    /// <summary><c>CPlayerFactory::Join</c> natif @0x8253430 —
    /// entrée family battle : rate-limit 500 ms, lock versus
    /// silencieux, pending → 0x678, IsInTerritory → leave, famille
    /// requise, nodes interdits {4-9,0xa,0xc-0x13} silencieux (le
    /// natif ne répond que les cas 0x678/0x1160), niveau ≥ 30.</summary>
    private async Task<bool> FamilyBattleJoinAsync(NetSession session,
        EntryState st, CancellationToken ct)
    {
        if (!FamilyOpRateLimit(st)) return true;
        if (InVersusScene(st)) return true;          // +0x3b & 0x40
        if (st.VersusPending)
        {
            await SendTextIndexAsync(session, 0x678, ct);
            return true;
        }
        if (NodeType(st) == 8)
        {
            _services.Territory.Leave((int)st.CharId);
            return await LeaveBattleNodeAsync(session, st, ct);
        }
        var nt = NodeType(st);
        if (nt is >= 4 and <= 0x13 and not 0xb) return true;
        if (st.Level < TerritoryWarService.LevelLimit) return true;
        var guild = _services.Guilds.GetGuildOf((int)st.CharId);
        if (guild == null) return true;      // famille inconnue zone
        // TerritoryJoin territory_id=0 type=2 → GetFamilyBattle.
        return await DoTerritoryJoinAsync(session, st, guild, 0, 1,
            ct);
    }

    /// <summary>Sortie du node de bataille — retour au point
    /// mémorisé (char+0xa98/+0xa9c natif ≡ notre CrossZoneNode),
    /// fallback sur le node rattaché du territoire quitté.</summary>
    private async Task<bool> LeaveBattleNodeAsync(NetSession session,
        EntryState st, CancellationToken ct)
    {
        // char+0xb70=0 / +0xb74=0xffff natifs au leave (@0x0815af00).
        ClearTerritory(st);
        var back = st.CrossZoneNode;
        if (back == 0) back = 1;     // défaut : ville 1 (Aven)
        var scene = _services.Scenes.GetScene($"S{back:D3}");
        var (ex, ey, ez) = ResolveInstanceEntryPos(scene, Random.Shared);
        await MigrateToNodeAsync(session, st, back, ex, ey, ez, ct);
        return true;
    }

    /// <summary>Reset des champs territoire — `char+0xb70=0`,
    /// `+0xb74=0xffff`, `+0xb9c=0` (leave natif @0x0815af00 /
    /// CEventTerritoryClose).</summary>
    private static void ClearTerritory(EntryState st)
    {
        st.TerritoryId = 0;
        st.TerritoryCamp = 0xffff;
        st.TerritoryKills = 0;
    }

    /// <summary>Rate-limit 500 ms du champ partagé char+0x650/0x654
    /// (même horodatage natif que les ops famille — `_familyInstanceOpAt`).
    /// </summary>
    private bool FamilyOpRateLimit(EntryState st)
    {
        var now = DateTimeOffset.UtcNow;
        if (_familyInstanceOpAt.TryGetValue((int)st.CharId,
                out var last)
            && now - last < TimeSpan.FromMilliseconds(500))
            return false;
        _familyInstanceOpAt[(int)st.CharId] = now;
        return true;
    }

    /// <summary>Type du node courant (S_Node col3) — 0 si inconnu.</summary>
    private int NodeType(EntryState st)
        => _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 0;

    /// <summary>Construit le 515 (réponse `TerritoryDataQuery`) :
    /// territoires triés par id + entrées événements 101 (Greedy
    /// Island, sched 501), 102 (Battle Royale, sched 502) — les
    /// versus 27-29 sont des records S_Territory chargés (types
    /// 601-603) ; map de triplets (schedType, beginEpoch, endEpoch)
    /// pour chaque type référencé.</summary>
    private byte[] BuildTerritoryQuery(EntryState st)
    {
        _services.Territory.RefreshPhases(
            _services.GameData.Schedules, DateTimeOffset.UtcNow);
        var entries = new List<FamilyBattlePackets.TerritoryEntry>();
        var scheds = new Dictionary<int,
            FamilyBattlePackets.TerritoryScheduleEntry>();
        var now = DateTimeOffset.UtcNow;
        foreach (var t in _services.Territory.AllTerritories)
        {
            var level = t.OwnFamilyId != 0
                ? (short)(_services.Guilds.GetGuild(t.OwnFamilyId)
                    ?.Level ?? 0)
                : (short)0;
            entries.Add(new FamilyBattlePackets.TerritoryEntry(
                t.Id, t.OwnFamilyId, t.Name, level,
                (uint)t.BeginEpoch, t.Phase, t.Flag));
            AddScheduleFor(scheds,
                TerritoryWarService.ScheduleTypeForTerritoryType(
                    t.Type), now);
        }
        // Entrées événements fixes de TC_TerritoryDataQuery :
        // 101 = Greedy Island (sched 501), 102 = Battle Royale
        // (sched 502) — poussées après les territoires, nom vide.
        entries.Add(new FamilyBattlePackets.TerritoryEntry(
            101, 0, "", 0, (uint)WindowBegin(501, now), 0, 0));
        AddScheduleFor(scheds, 501, now);
        entries.Add(new FamilyBattlePackets.TerritoryEntry(
            102, 0, "", 0, (uint)WindowBegin(502, now), 0, 0));
        AddScheduleFor(scheds, 502, now);
        // Schedules versus 601/602/603 pour les ids 27-29 — déjà
        // couverts par la boucle si les defs portent ces types.
        return FamilyBattlePackets.TerritoryQuery(entries,
            scheds.Values.ToList());
    }

    /// <summary>Ajoute le triplet (schedType, begin, end) de la
    /// fenêtre courante à la map du 515.</summary>
    private void AddScheduleFor(
        Dictionary<int, FamilyBattlePackets.TerritoryScheduleEntry>
            scheds,
        int schedType, DateTimeOffset now)
    {
        if (schedType < 0 || scheds.ContainsKey(schedType)
            || !_services.GameData.Schedules
                .TryGetValue(schedType, out var entries))
            return;
        var begin = WindowBegin(entries, now);
        var end = WindowEnd(entries, now);
        scheds[schedType] = new FamilyBattlePackets
            .TerritoryScheduleEntry(schedType, begin, end);
    }

    /// <summary>Début epoch de la prochaine/courante fenêtre d'un
    /// type de schedule (0 si le type n'existe pas).</summary>
    private int WindowBegin(int schedType, DateTimeOffset now)
        => _services.GameData.Schedules.TryGetValue(schedType,
            out var e) ? WindowBegin(e, now) : 0;

    /// <summary>Fenêtre la plus proche de <paramref name="now"/> pour
    /// une liste d'entrées — début epoch (le natif pousse
    /// GreedyBegin+Duration / BR+0x14+0x10 selon l'état ; on émet le
    /// début de la fenêtre courante ou la prochaine).</summary>
    private static int WindowBegin(
        List<GameDataService.ScheduleEntry> entries,
        DateTimeOffset now)
    {
        var (begin, _) = NearestWindow(entries, now);
        return begin;
    }

    /// <summary>Fin epoch de la fenêtre la plus proche.</summary>
    private static int WindowEnd(
        List<GameDataService.ScheduleEntry> entries,
        DateTimeOffset now)
    {
        var (_, end) = NearestWindow(entries, now);
        return end;
    }

    /// <summary>Projete chaque entrée (jour ISO 1-7 + HHMM) sur les
    /// jours autour de <paramref name="now"/> et retourne la fenêtre
    /// [begin,end) la plus proche (en cours ou à venir).</summary>
    private static (int Begin, int End) NearestWindow(
        List<GameDataService.ScheduleEntry> entries,
        DateTimeOffset now)
    {
        var today = now.UtcDateTime.Date;
        var isoDay = ((int)now.DayOfWeek + 6) % 7 + 1;
        var nowSec = (int)now.ToUnixTimeSeconds();
        var best = (0, 0);
        var bestDist = long.MaxValue;
        foreach (var e in entries)
            for (var d = -7; d <= 7; d++)
            {
                var day = today.AddDays(e.DayId - isoDay + d);
                var begin = (int)new DateTimeOffset(day.AddSeconds(
                    e.BeginHhmm / 100 * 3600 + e.BeginHhmm % 100 * 60),
                    TimeSpan.Zero).ToUnixTimeSeconds();
                var endDay = e.EndHhmm <= e.BeginHhmm
                    ? day.AddDays(1) : day;
                var end = (int)new DateTimeOffset(endDay.AddSeconds(
                    e.EndHhmm / 100 * 3600 + e.EndHhmm % 100 * 60),
                    TimeSpan.Zero).ToUnixTimeSeconds();
                var dist = Math.Abs((long)nowSec - begin);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = (begin, end);
                }
            }
        return best;
    }
}
