using Eden.Core.Data;
using Eden.Core.Game.Social;
using RankRow = Eden.Core.Game.Social.RankPackets.RankEntry;
using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>
/// Sous-système « classements » — port de la chaîne native :
///   `CEventRankUpdate::Execute` (mission @0x081e7920) +
///   `TC_UpdateRankInfo` (@0x081ed080)
///   → `CMissionDBO::UpdateGenderRankInfo(p ∈ {0,2,4,6,8})`
///     (@0x08197b30 — clés = p*10 + {1,3,4,5})
///   → `CMissionDBO::UpdateOtherRankInfo` (@0x081a21c0 — 100/101/102/200)
///   → `CMissionDBO::UpdatePVPRankInfo` (@0x0819da90 — 301-306,
///     320-322, 340, 360, 361, 400-420, 500)
///   → `CGlobalInfo::UpdateZoneRankInfo` (@0x080857e2) qui diffuse
///     `CNC_ZMis_ZoneServerRankInfo` (763) par type vers les zones.
/// Version (`RankAwardZoneID._2_2_`) = jour_semaine*10000 + h*100 + min
/// (localtime). Rafraîchissement replanifié à la prochaine heure paire.
/// Record = `SRankInfoData` (0x38) : name + 9 i32 (+4..+0x24) +
/// str2 (+0x28) + vec&lt;str&gt; (+0x2c).
/// </summary>
public sealed class RankInfoService
{

    private readonly MissionDb _db;
    private readonly Func<ushort, byte[], Task> _broadcast;
    private readonly Dictionary<short, List<RankRow>> _sets = new();
    private short _version;

    /// <param name="broadcast">ZMis → toutes les zones (763).</param>
    public RankInfoService(MissionDb db,
        Func<ushort, byte[], Task> broadcast)
    {
        _db = db;
        _broadcast = broadcast;
    }

    /// <summary>Boucle de rafraîchissement — équivalent de
    /// `CEventRankUpdate` : exécute immédiatement puis se replanifie à
    /// la prochaine heure paire (formule `*(this+8)` @0x081e7a00).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await UpdateAllAsync(); }
            catch (Exception ex)
            { EdenLog.Write($"[Mission] RankInfo update: {ex.Message}"); }
            var delay = DelayToNextEvenHour();
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Délai jusqu'à la prochaine heure paire — natif :
    /// `(heure paire ? 2h : 1h) - min*60 - sec` (localtime).</summary>
    private static TimeSpan DelayToNextEvenHour()
    {
        var now = DateTime.Now;
        var next = now.Date.AddHours(now.Hour)
            .AddHours((now.Hour & 1) == 0 ? 2 : 1);
        return next - now;
    }

    /// <summary>Rafraîchit tout le cache puis broadcast 763 par type —
    /// ordre des appels de `CEventRankUpdate::Execute`.</summary>
    public async Task UpdateAllAsync()
    {
        var sets = new Dictionary<short, List<RankRow>>();
        foreach (var kv in await LoadGenderRanksAsync())
            sets[kv.Key] = kv.Value;
        foreach (var kv in await LoadOtherRanksAsync())
            sets[kv.Key] = kv.Value;
        foreach (var kv in await LoadPvpRanksAsync())
            sets[kv.Key] = kv.Value;
        sets[600] = await TeamEvaluateRankAsync();
        // RankAwardZoneID._2_2_ = wday*10000 + hour*100 + min.
        var n = DateTime.Now;
        _version = (short)((int)n.DayOfWeek * 10000
            + n.Hour * 100 + n.Minute);
        lock (_sets)
        {
            _sets.Clear();
            foreach (var kv in sets) _sets[kv.Key] = kv.Value;
        }
        EdenLog.Write($"[Mission] RankInfo Ver {_version} " +
            $"({sets.Count} jeux)");
        await BroadcastAllAsync();
    }

    /// <summary>Diffuse chaque type en cache — appelée après chaque
    /// update et pour une zone qui vient de se connecter.</summary>
    public async Task BroadcastAllAsync()
    {
        List<KeyValuePair<short, List<RankRow>>> snap;
        lock (_sets) snap = _sets.ToList();
        foreach (var kv in snap)
            await _broadcast(763, BuildRankInfo(kv.Key, kv.Value));
    }

    /// <summary>Envoie le cache à une seule zone (push boot —
    /// équivalent de la synchro `UpdateZoneRankInfo` au connect).</summary>
    public async Task PushAllToAsync(Func<byte[], Task> send)
    {
        List<KeyValuePair<short, List<RankRow>>> snap;
        lock (_sets) snap = _sets.ToList();
        foreach (var kv in snap)
            await send(BuildRankInfo(kv.Key, kv.Value));
    }

    // ---------------- requêtes (SQL verbatim natif) ----------------

    /// <summary>`UpdateGenderRankInfo(p)` × {0,2,4,6,8} — clés =
    /// p*10 + {1 : total_base_point, 3 : class_point,
    /// 4 : monster_kill, 5 : family_instance} (formule
    /// `(short)(iVar6&lt;&lt;3)+base+param*2` avec iVar6=param,
    /// mission @0x081983xx).</summary>
    private async Task<Dictionary<short, List<RankRow>>>
        LoadGenderRanksAsync()
    {
        var result = new Dictionary<short, List<RankRow>>();
        foreach (var g in new short[] { 0, 2, 4, 6, 8 })
        {
            var cond = GenderCondition(g);
            // ordre natif : 1 (total), 3 (class), 4 (kills), 5 (instance)
            result[(short)(g * 10 + 1)] = await PlayerRankAsync(cond,
                "total_base_point", "class_point");
            result[(short)(g * 10 + 3)] = await PlayerRankAsync(cond,
                "class_point", "total_base_point");
            result[(short)(g * 10 + 4)] = await PlayerRankAsync(cond,
                "monster_kill", "total_base_point");
            result[(short)(g * 10 + 5)] = await PlayerRankAsync(cond,
                "family_instance", "total_base_point");
        }
        return result;
    }

    /// <summary>`CGlobalInfo::GetGenderRankConditionCmd` @0x080741e0 :
    /// p&lt;2 → « gender = 0 OR gender = 1 », p ∈ {8,9} →
    /// « gender = 8 OR gender = 9 », sinon « gender = p ».</summary>
    private static string GenderCondition(short p)
    {
        if (p < 2) return "gender = 0 OR gender = 1";
        if (p is 8 or 9) return "gender = 8 OR gender = 9";
        return $"gender = {p}";
    }

    /// <summary>Requête achievement par genre —
    /// `SELECT player_name, player_id, player_lv, A, B
    /// FROM player_achievement_rank WHERE cond
    /// ORDER BY A DESC, player_lv DESC, B DESC LIMIT 100`.
    /// Record : name, +4=id, +8=lv, +0xc=A, +0x10=B.</summary>
    private async Task<List<RankRow>> PlayerRankAsync(string cond,
        string primary, string secondary)
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT player_name, player_id, player_lv, " +
            $"{primary}, {secondary} FROM player_achievement_rank " +
            $"WHERE {cond} ORDER BY {primary} DESC, player_lv DESC, " +
            $"{secondary} DESC LIMIT 100");
        return rows.Select(r => new RankRow
        {
            Name = Str(r, 0),
            F = { [0] = Num(r, 1), [1] = Num(r, 2),
                  [2] = Num(r, 3), [3] = Num(r, 4) },
        }).ToList();
    }

    /// <summary>`UpdateOtherRankInfo` — clés 100/101 (familles),
    /// 102 (gladiator), 200 (familybattle).</summary>
    private async Task<Dictionary<short, List<RankRow>>>
        LoadOtherRanksAsync()
    {
        return new Dictionary<short, List<RankRow>>
        {
            [100] = await FamilyRankAsync("monster_kill"),
            [101] = await FamilyRankAsync("family_instance"),
            [102] = await GladiatorRankAsync(),
            [200] = await FamilyBattleRankAsync(),
        };
    }

    /// <summary>Familles — `SELECT family_name, family_lv, metric,
    /// memmber_size FROM family_achievement_rank ORDER BY metric DESC,
    /// family_lv DESC, memmber_size DESC LIMIT 100`.
    /// Record : +4=family_lv, +8=metric, +0xc=memmber_size.</summary>
    private async Task<List<RankRow>> FamilyRankAsync(string metric)
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT family_name, family_lv, " + metric +
            ", memmber_size FROM family_achievement_rank ORDER BY " +
            metric + " DESC, family_lv DESC, memmber_size DESC " +
            "LIMIT 100");
        return rows.Select(r => new RankRow
        {
            Name = Str(r, 0),
            F = { [0] = Num(r, 1), [1] = Num(r, 2), [2] = Num(r, 3) },
        }).ToList();
    }

    /// <summary>Gladiator (102) — `SELECT p.given_name,
    /// p.colosseum_point, q.class_point FROM player_characters p,
    /// player_achievement_rank q WHERE p.colosseum_point &gt; 0 AND
    /// q.player_id = p.id ORDER BY p.colosseum_point DESC,
    /// q.class_point DESC LIMIT 100`. Record : +4=point,
    /// +8=class_point.</summary>
    private async Task<List<RankRow>> GladiatorRankAsync()
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT p.given_name, p.colosseum_point, q.class_point " +
            "FROM player_characters p, player_achievement_rank q " +
            "WHERE p.colosseum_point > 0 AND q.player_id = p.id " +
            "ORDER BY p.colosseum_point DESC, q.class_point DESC " +
            "LIMIT 100");
        return rows.Select(r => new RankRow
        {
            Name = Str(r, 0),
            F = { [0] = Num(r, 1), [1] = Num(r, 2) },
        }).ToList();
    }

    /// <summary>Familybattle (200) — `SELECT f.name, f.id, f.honor,
    /// p.given_name FROM family f, player_characters p WHERE
    /// f.lv &gt;= 4 AND p.id = f.leader_id ORDER BY f.honor DESC,
    /// f.exp DESC LIMIT 25`. Record : +4=family_id, +8=honor,
    /// str2 (+0x28) = nom du chef.</summary>
    private async Task<List<RankRow>> FamilyBattleRankAsync()
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT f.name, f.id, f.honor, p.given_name " +
            "FROM family f, player_characters p WHERE f.lv >= 4 " +
            "AND p.id = f.leader_id ORDER BY f.honor DESC, f.exp DESC " +
            "LIMIT 25");
        return rows.Select(r => new RankRow
        {
            Name = Str(r, 0),
            F = { [0] = Num(r, 1), [1] = Num(r, 2) },
            Str2 = Str(r, 3),
        }).ToList();
    }

    /// <summary>`UpdatePVPRankInfo` — clés natives
    /// (`CGlobalInfo::GetPVPRankCode` @0x080720f0) :
    ///   (1, 1..6)  → 301-306  colosseum_type=1, tranches de niveau
    ///   (2, 4..6)  → 320-322  colosseum_type=2
    ///   (3, 0)     → 340      colosseum_type=3
    ///   (4, 0)     → 360      greedy_island_rank
    ///   (5, 0)     → 361      greedy_island_yesterday (LIMIT 20)
    ///   (6, 7..27) → 400-420  classpk_rank, class_id = b-6
    ///   (7, 0)     → 500      battle_royale_rank</summary>
    private async Task<Dictionary<short, List<RankRow>>>
        LoadPvpRanksAsync()
    {
        var result = new Dictionary<short, List<RankRow>>();
        for (short b = 1; b <= 6; b++)
            result[(short)(300 + b)] = await ColosseumRankAsync(1, b);
        for (short b = 4; b <= 6; b++)
            result[(short)(0x13c + b)] = await ColosseumRankAsync(2, b);
        result[340] = await ColosseumRankAsync(3, 0);
        result[360] = await GreedyRankAsync(false);
        result[361] = await GreedyRankAsync(true);
        for (short b = 7; b <= 0x1b; b++)
            result[(short)(0x189 + b)] = await ClassPkRankAsync(b);
        result[500] = await BattleRoyaleRankAsync();
        return result;
    }

    /// <summary>`GetLevelBracketConditionCmd` @0x08072460 : b=0 →
    /// « p.level &gt; 0 », b=1..6 → tranches 30-39…80-100,
    /// b=7..26 → « c.class_id = b-6 », b=27 → « c.class_id = 59 ».</summary>
    private static string BracketCondition(short b)
    {
        return b switch
        {
            1 => "p.level >= 30 AND p.level <= 39",
            2 => "p.level >= 40 AND p.level <= 49",
            3 => "p.level >= 50 AND p.level <= 59",
            4 => "p.level >= 60 AND p.level <= 69",
            5 => "p.level >= 70 AND p.level <= 79",
            6 => "p.level >= 80 AND p.level <= 100",
            >= 7 and <= 26 => $"c.class_id = {b - 6}",
            27 => "c.class_id = 59",
            _ => "p.level > 0",
        };
    }

    /// <summary>Colosseum — `SELECT p.given_name, p.level,
    /// p.gender_id, c.colosseum_wins, c.colosseum_kills, c.player_id
    /// FROM colosseum_ranking c, player_characters p WHERE
    /// p.id = c.player_id AND c.colosseum_type = t AND cond ORDER BY
    /// wins DESC, kills DESC, level DESC LIMIT 100`.
    /// Record : +4=level, +8=gender_id, +0xc=wins, +0x10=kills,
    /// +0x14=player_id.</summary>
    private async Task<List<RankRow>> ColosseumRankAsync(short type,
        short bracket)
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT p.given_name, p.level, p.gender_id, " +
            "c.colosseum_wins, c.colosseum_kills, c.player_id " +
            "FROM colosseum_ranking AS c, player_characters AS p " +
            $"WHERE p.id = c.player_id AND c.colosseum_type = {type} " +
            $"AND {BracketCondition(bracket)} " +
            "ORDER BY c.colosseum_wins DESC, c.colosseum_kills DESC, " +
            "p.level DESC LIMIT 100");
        return rows.Select(PvpRow).ToList();
    }

    /// <summary>Record PVP à 6 colonnes :
    /// name, +4=level, +8=gender_id, +0xc=col3, +0x10=col4,
    /// +0x14=player_id.</summary>
    private static RankRow PvpRow(object?[] r) => new()
    {
        Name = Str(r, 0),
        F = { [0] = Num(r, 1), [1] = Num(r, 2), [2] = Num(r, 3),
              [3] = Num(r, 4), [4] = Num(r, 5) },
    };

    /// <summary>ClassPK (clés 400-420) — `SELECT p.given_name,
    /// p.level, p.gender_id, c.wins, c.levels, c.player_id FROM
    /// classpk_rank c, player_characters p WHERE p.id = c.player_id
    /// AND cond ORDER BY wins DESC, levels DESC, level DESC
    /// LIMIT 100`.</summary>
    private async Task<List<RankRow>> ClassPkRankAsync(short bracket)
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT p.given_name, p.level, p.gender_id, c.wins, " +
            "c.levels, c.player_id FROM classpk_rank AS c, " +
            "player_characters AS p WHERE p.id = c.player_id AND " +
            $"{BracketCondition(bracket)} ORDER BY c.wins DESC, " +
            "c.levels DESC, p.level DESC LIMIT 100");
        return rows.Select(PvpRow).ToList();
    }

    /// <summary>Greedy island — courant (360) :
    /// `SELECT p.given_name, p.level, p.gender_id, g.kills, g.scores,
    /// g.player_id FROM greedy_island_rank g, player_characters p
    /// ORDER BY kills DESC, scores DESC, level DESC LIMIT 100` ;
    /// hier (361) : `greedy_island_yesterday`, 4 colonnes,
    /// `LIMIT 20`.</summary>
    private async Task<List<RankRow>> GreedyRankAsync(bool yesterday)
    {
        if (yesterday)
        {
            var rows = await _db.QueryRankRowsAsync(
                "SELECT p.given_name, p.level, g.kills, g.scores " +
                "FROM greedy_island_yesterday AS g, " +
                "player_characters AS p WHERE p.id = g.player_id " +
                "ORDER BY g.kills DESC, g.scores DESC, p.level DESC " +
                "LIMIT 20");
            // 4 colonnes : +4=level, +8=kills, +0xc=scores.
            return rows.Select(r => new RankRow
            {
                Name = Str(r, 0),
                F = { [0] = Num(r, 1), [1] = Num(r, 2),
                      [2] = Num(r, 3) },
            }).ToList();
        }
        var cur = await _db.QueryRankRowsAsync(
            "SELECT p.given_name, p.level, p.gender_id, g.kills, " +
            "g.scores, g.player_id FROM greedy_island_rank AS g, " +
            "player_characters AS p WHERE p.id = g.player_id " +
            "ORDER BY g.kills DESC, g.scores DESC, p.level DESC " +
            "LIMIT 100");
        return cur.Select(PvpRow).ToList();
    }

    /// <summary>Battle royale (500) — `SELECT p.given_name, p.level,
    /// p.gender_id, b.wins, b.kills, b.player_id FROM
    /// battle_royale_rank b, player_characters p WHERE
    /// p.id = b.player_id ORDER BY wins DESC, kills DESC
    /// LIMIT 100`.</summary>
    private async Task<List<RankRow>> BattleRoyaleRankAsync()
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT p.given_name, p.level, p.gender_id, b.wins, " +
            "b.kills, b.player_id FROM battle_royale_rank AS b, " +
            "player_characters AS p WHERE p.id = b.player_id " +
            "ORDER BY b.wins DESC, b.kills DESC LIMIT 100");
        return rows.Select(PvpRow).ToList();
    }

    /// <summary>Team evaluate (600, bloc final de
    /// `UpdateGenderRankInfo` @0x08198bb8) — `SELECT p.given_name,
    /// e.node_id, e.score, e.time, e.evaluate, e.team_mem_id1..10
    /// FROM team_evaluate e, player_characters p WHERE
    /// e.leader_id = p.id ORDER BY e.score DESC, e.time ASC
    /// LIMIT 50`. Record : +4=node_id, +8=score, +0xc=time,
    /// +0x10=evaluate, +0x2c = membres (10 chaînes).</summary>
    private async Task<List<RankRow>> TeamEvaluateRankAsync()
    {
        var rows = await _db.QueryRankRowsAsync(
            "SELECT p.given_name, e.node_id, e.score, e.time, " +
            "e.evaluate, e.team_mem_id1, e.team_mem_id2, " +
            "e.team_mem_id3, e.team_mem_id4, e.team_mem_id5, " +
            "e.team_mem_id6, e.team_mem_id7, e.team_mem_id8, " +
            "e.team_mem_id9, e.team_mem_id10 FROM team_evaluate e, " +
            "player_characters p WHERE e.leader_id = p.id " +
            "ORDER BY e.score DESC, e.time ASC LIMIT 50");
        return rows.Select(r => new RankRow
        {
            Name = Str(r, 0),
            F = { [0] = Num(r, 1), [1] = Num(r, 2), [2] = Num(r, 3),
                  [3] = Num(r, 4) },
            StrList = Enumerable.Range(5, 10)
                .Select(i => Str(r, i)).ToList(),
        }).ToList();
    }

    // ---------------- sérialisation 763 ----------------

    /// <summary>`CNC_ZMis_ZoneServerRankInfo::Serialize`
    /// (@0x080b78b0 mission → NCID 763 zone) — délégué à
    /// <see cref="RankPackets.Build763"/>.</summary>
    private byte[] BuildRankInfo(short type, List<RankRow> rows)
        => RankPackets.Build763(type, _version, rows);

    /// <summary>Valeur texte d'une colonne (GetData natif).</summary>
    private static string Str(object?[] r, int i)
        => i < r.Length ? r[i]?.ToString() ?? "" : "";

    /// <summary>Valeur numérique d'une colonne (sscanf %d natif).</summary>
    private static int Num(object?[] r, int i)
    {
        if (i >= r.Length || r[i] == null) return 0;
        try { return Convert.ToInt32(r[i]); }
        catch { return 0; }
    }
}
