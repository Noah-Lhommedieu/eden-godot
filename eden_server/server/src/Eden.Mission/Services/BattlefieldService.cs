using Eden.Core.Logging;
using Eden.Core.Database.Repositories;

namespace Eden.Mission.Services;

/// <summary>
/// Champ de bataille PvP — V1 du flux natif
/// `CBattlefieldFactory`/`CBattlefield` (mission, decompiled.c
/// @0x0804c2e0+). La mission orchestre : file d'inscription →
/// équilibrage des camps → ouverture du match sur la map bf
/// (instance dédiée) → score → fin + récompenses.
///
/// Flux NCID (design : docs/limitless/BATTLEFIELD_DESIGN.md) :
///   join    : CNE_ZMis_ZoneServerBattlefieldJoinResult (746)
///   enter   : CNE_ZMis_ZoneServerEnterBattlefield (747)
///   start   : CNE_ZMis_ZoneServerStartBattlefield (748)
///   list    : CNE_ZMis_ZoneServerBattlefieldList (749)
///   answer  : CNE_ZMis_ZoneServerEnterBattlefieldAnswer (751)
///   setup   : CNE_ZMis_ZoneServerBattlefieldSetup (752)
///
/// Stats persistées via BattlefieldRepository (battlefield_month +
/// battlefield_career) — déjà en place côté Eden.Core.
/// </summary>
public sealed class BattlefieldService
{
    private readonly BattlefieldRepository _repo;

    /// <summary>Requête d'inscription en file — un joueur se met en
    /// file pour un champ de bataille donné.</summary>
    public record QueueEntry(int CharId, int BfId, int Side,
        DateTime QueuedAt);

    /// <summary>Match en cours — 2 camps, score, timer.</summary>
    public sealed class Match
    {
        public int BfId;
        public readonly List<int> SideA = new();
        public readonly List<int> SideB = new();
        public int ScoreA;
        public int ScoreB;
        public DateTime StartedAt;
        public bool Active;
        /// <summary>Score cible pour gagner (S_Battlefield ou
        /// défaut) — un camp qui l'atteint termine le match.</summary>
        public int KillTarget = 25;
        /// <summary>Durée max du match (défaut 15 min) — à terme
        /// depuis S_Battlefield.ini.</summary>
        public TimeSpan Duration = TimeSpan.FromMinutes(15);
    }

    private readonly List<QueueEntry> _queue = new();
    private readonly Dictionary<int, Match> _matches = new();

    /// <summary>Joueurs requis par camp pour lancer un match —
    /// V1 petit format ; S_Battlefield.ini porte le vrai nombre.</summary>
    public int TeamSize { get; set; } = 5;

    /// <summary>Émis quand un match se termine (kill-target/timer/
    /// close) — la mission diffuse le résultat aux zones.</summary>
    public event Action<Match, int>? MatchEnded;

    /// <summary>Émis quand `TryStart` forme un match — la mission
    /// l'utilise pour notifier les zones (EnterBattlefield) et
    /// téléporter les joueurs dans l'instance bf.</summary>
    public event Action<Match>? MatchFormed;

    public BattlefieldService(BattlefieldRepository repo) =>
        _repo = repo;

    /// <summary>Inscrit un joueur — `side` = camp demandé par le
    /// client ; -1/0 auto → équilibrage sur le camp le moins peuplé,
    /// lance un match si les deux camps sont pleins.</summary>
    public QueueEntry Join(int charId, int bfId, int side = -1)
    {
        _queue.RemoveAll(q => q.CharId == charId &&
            q.BfId == bfId);
        var a = _queue.Count(q => q.BfId == bfId && q.Side == 0);
        var b = _queue.Count(q => q.BfId == bfId && q.Side == 1);
        var pick = side is 0 or 1 ? side : (a <= b ? 0 : 1);
        var entry = new QueueEntry(charId, bfId, pick,
            DateTime.UtcNow);
        _queue.Add(entry);
        EdenLog.Write($"[Battlefield] join char={charId} bf={bfId} " +
            $"side={entry.Side} queue={_queue.Count(q => q.BfId == bfId)}");
        TryStart(bfId);
        return entry;
    }

    /// <summary>Retire un joueur de la file ou du match — relâche
    /// le slot côté mission.</summary>
    public void Leave(int charId, int bfId)
    {
        _queue.RemoveAll(q => q.CharId == charId && q.BfId == bfId);
        if (_matches.TryGetValue(bfId, out var m) && m.Active)
        {
            m.SideA.Remove(charId); m.SideB.Remove(charId);
            if (m.SideA.Count == 0 && m.SideB.Count == 0)
                m.Active = false;
        }
    }

    /// <summary>Liste les champs de bataille ouverts (file + match
    /// actif) pour le NCID 749 BattlefieldList.</summary>
    public IReadOnlyDictionary<int, int> OpenList()
    {
        var d = new Dictionary<int, int>();
        foreach (var q in _queue)
            d[q.BfId] = d.GetValueOrDefault(q.BfId) + 1;
        return d;
    }

    /// <summary>Si les deux camps atteignent `TeamSize`, forme le
    /// match — les joueurs sortent de la file.</summary>
    private void TryStart(int bfId)
    {
        var a = _queue.Where(q => q.BfId == bfId && q.Side == 0)
            .Take(TeamSize).ToList();
        var b = _queue.Where(q => q.BfId == bfId && q.Side == 1)
            .Take(TeamSize).ToList();
        if (a.Count < TeamSize || b.Count < TeamSize) return;
        var m = new Match { BfId = bfId, Active = true,
            StartedAt = DateTime.UtcNow };
        foreach (var e in a) { m.SideA.Add(e.CharId); _queue.Remove(e); }
        foreach (var e in b) { m.SideB.Add(e.CharId); _queue.Remove(e); }
        _matches[bfId] = m;
        EdenLog.Write($"[Battlefield] match bf={bfId} " +
            $"A={string.Join(',', m.SideA)} B={string.Join(',', m.SideB)}");
        try { MatchFormed?.Invoke(m); }
        catch (Exception ex) { EdenLog.Write(
            $"[Battlefield] MatchFormed : {ex.Message}"); }
    }

    /// <summary>Marque un point de kill pour le camp du tueur —
    /// appelé par la zone au kill PvP d'un match actif.</summary>
    public void AddKill(int bfId, int killerCharId, int deadCharId)
    {
        if (!_matches.TryGetValue(bfId, out var m) || !m.Active) return;
        if (m.SideA.Contains(killerCharId)) m.ScoreA++;
        else if (m.SideB.Contains(killerCharId)) m.ScoreB++;
        // Auto-fin : un camp atteint le kill-target ou le timer
        // expire — le match se conclut sans close manuel.
        if (m.ScoreA >= m.KillTarget || m.ScoreB >= m.KillTarget)
            _ = EndAsync(bfId);
    }

    /// <summary>À appeler périodiquement — termine les matches dont
    /// le timer a expiré (vainqueur = meilleur score).</summary>
    public async Task TickAsync()
    {
        foreach (var (bf, m) in _matches.ToList())
            if (m.Active &&
                DateTime.UtcNow - m.StartedAt >= m.Duration)
                await EndAsync(bf);
    }

    /// <summary>Match actif d'un bf (score/timer) pour le client
    /// UpdateScore.</summary>
    public Match? GetMatch(int bfId) =>
        _matches.TryGetValue(bfId, out var m) ? m : null;

    /// <summary>Termine le match — camp vainqueur par score,
    /// persiste les stats month/career et prépare la fin.</summary>
    public async Task<int> EndAsync(int bfId)
    {
        if (!_matches.TryGetValue(bfId, out var m) || !m.Active)
            return -1;
        m.Active = false;
        var winner = m.ScoreA > m.ScoreB ? 0 :
            m.ScoreB > m.ScoreA ? 1 : -1;
        foreach (var cid in m.SideA.Concat(m.SideB))
        {
            var win = (winner == 0 && m.SideA.Contains(cid)) ||
                (winner == 1 && m.SideB.Contains(cid));
            await _repo.RecordMatchAsync(cid, win);
        }
        _matches.Remove(bfId);
        try { MatchEnded?.Invoke(m, winner); }
        catch (Exception ex) { EdenLog.Write(
            $"[Battlefield] MatchEnded : {ex.Message}"); }
        return winner;
    }
}
