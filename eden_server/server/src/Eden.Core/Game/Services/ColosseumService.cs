using Eden.Core.Logging;

namespace Eden.Core.Game.Services;

/// <summary>
/// Arène PvP « Colosseum » — transcription du couple
/// <c>CColosseumFactory</c> zone (mode +4, matchs par id via
/// char+0xbb4) et mission (files par catégorie +0x30, schedId
/// courant +100, flag ouvert +8, durée +6).
/// <para>Preuves natives :
/// <list type="bullet">
///   <item><c>CColosseumFactory::TuneMode</c> mission @0x8056c50 —
///   4 events <c>CEventCS{Challenge,Normal,Twenty,ClassPK}Schedule</c>
///   liés aux types de <c>S_Schedule</c> 201/200/202/203 ; le schedId
///   courant (200→0 Normal, 201→1 Challenge, 202→2 Twenty, 203→3
///   ClassPK) est broadcasté aux zones par
///   <c>CNC_ZMis_ZoneServerColosseumModeUpdate</c> [s16] →
///   <c>factory+4</c> zone (init 0, <c>Init</c> @0x805aa90).</item>
///   <item><c>GetCategory</c> mission @0x8056a80 : schedId 1 →
///   catégorie fixe 1 ; schedId 3 → classe via gamedata ; sinon
///   level/10 plafonné à 8.</item>
///   <item>Gates de join zone @0x8244d30 : mode 2 → level&gt;59 ;
///   sinon level≥30 ; BR(0xf)→0x18cd ; versus(0x11)→0x1d98 ;
///   crusade(0x13)→0x225f ; pendings/team≥11/lock→528[1] ;
///   node type interdit {4-9,0xa,0xb,0xc,0xe-0x13}→528[5] ;
///   greedy(0xd)→0x1661 ; mode 3 → SLV(char+0x47e)&gt;49 sinon
///   0x16d7.</item>
///   <item>Match → <c>NotifyEnterColosseum</c> @0x80575b0 : commande
///   monde <c>EnterColosseum id=%d node_id=%hd</c> → zone
///   <c>Mission_TC_EnterColosseum</c> @0x83b61c0 : 527 [u8 1] +
///   <c>CEventEnterColosseum</c> (téléport vers le node arène).</item>
///   <item>Arène = node type 10 (<c>IsInColosseum</c> @0x8409a00),
///   nodes 401-404 (炎鷹競技場).</item>
/// </list></para>
/// Le sous-système est volontairement zone-local : le serveur C#
/// est monolithique par zone, il n'y a pas de mission dédiée — les
/// files par catégorie et les matchs runtime vivent ici.
/// </summary>
public sealed class ColosseumService
{
    /// <summary>Premier type de schedule colosseum (S_Schedule 200).</summary>
    public const int SchedTypeBase = 200;

    /// <summary>schedId natif du mode Twenty (level &gt; 59 requis).</summary>
    public const int ModeTwenty = 2;
    /// <summary>schedId natif du mode ClassPK (SLV &gt; 49 requis).</summary>
    public const int ModeClassPk = 3;

    /// <summary>Niveau minimum natif hors mode Twenty (char+0x470 &lt;30
    /// → texte 0xfc9 + JoinResult 1).</summary>
    public const int MinLevel = 30;
    /// <summary>Seuil du mode Twenty : <c>0x3b &lt; level</c> natif —
    /// level &gt; 59 exigé (≥60).</summary>
    public const int TwentyMinLevel = 60;
    /// <summary>Seuil du mode ClassPK : <c>0x31 &lt; SLV</c> natif —
    /// niveau de classe &gt; 49 exigé (≥50).</summary>
    public const int ClassPkMinClassLevel = 50;

    /// <summary>Node d'arène par défaut (S_Node 401, type 10).
    /// Les 4 nodes 401-404 correspondent aux 4 modes natifs —
    /// 401 = Normal/Challenge, 402 = Twenty, 403 = ClassPK… on
    /// choisit 401 pour tous les matchs (le mapping exact
    /// sched→node n'est pas prouvé).</summary>
    public const int ArenaNodeId = 401;

    /// <summary>schedId courant (factory+4 zone natif) — 0-3 selon
    /// l'event schedule actif ; 0 à froid comme <c>Init</c>.</summary>
    public int Mode { get; private set; }

    /// <summary>Colosseum ouvert (factory+8 mission natif — un event
    /// schedule 200-203 actif).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Durée du round courant (factory+6 natif, param
    /// formula par mode) — 0 quand fermé.</summary>
    public int DurationSec { get; private set; }

    /// <summary>Files d'inscription par catégorie (mission
    /// factory+0x30 = map&lt;cat, queues&gt;).</summary>
    private readonly Dictionary<int, List<int>> _queues = new();

    /// <summary>Matchs actifs : matchId → match (factory+0xc zone
    /// = map&lt;id, CColosseum*&gt;, indexé par char+0xbb4).</summary>
    private readonly Dictionary<int, Match> _matches = new();
    /// <summary>charId → matchId (char+0xbb4 natif).</summary>
    private readonly Dictionary<int, int> _charMatch = new();
    /// <summary>Compteur d'id de match (factory+0x28 mission).</summary>
    private int _nextMatchId = 1;

    /// <summary>Un match d'arène formé — miroir minimal de
    /// <c>CColosseum</c> natif (id +0x4, node +0x10, camps vec
    /// +0x20 = vec&lt;vec&lt;int&gt;&gt; par équipe, statut +0xa2).</summary>
    public sealed class Match
    {
        /// <summary>Id du match (clé de la map factory+0xc).</summary>
        public required int Id { get; init; }
        /// <summary>Node d'arène du match (CColosseum+0x10 natif).</summary>
        public required int NodeId { get; init; }
        /// <summary>Catégorie du match (schedId/level bracket).</summary>
        public required int Category { get; init; }
        /// <summary>Équipes : liste de listes de charId — camp 0 vs
        /// camp 1 (vec&lt;vec&lt;int&gt;&gt; natif à CColosseum+0x20).</summary>
        public required List<List<int>> Teams { get; init; }
        /// <summary>Statut (CColosseum+0xa2 natif — SetCSStatus).</summary>
        public short Status { get; set; }
    }

    /// <summary>Résultat d'un join — les codes renvoyés dans le 528
    /// (<c>CNC_CZ_ClientColosseumJoinResult</c> [s16]).</summary>
    public enum JoinCode : short
    {
        /// <summary>Inscription prise en compte.</summary>
        Ok = 0,
        /// <summary>1 = échec générique (level, BR/versus/crusade,
        /// greedy, SLV, pendings) — envoyé avec ou sans texte.</summary>
        Failed = 1,
        /// <summary>5 = dans une scène interdite (node type
        /// {4-9,0xa,0xb,0xc,0xe-0x13}) — sans texte natif.</summary>
        BadScene = 5,
    }

    /// <summary>Statut d'inscription (char+0xbb0 natif).</summary>
    public bool InQueue(int charId)
        => _queues.Values.Any(q => q.Contains(charId));

    /// <summary>Match courant du personnage (char+0xbb4 natif → 0
    /// si aucun).</summary>
    public Match? MatchOf(int charId)
        => _charMatch.TryGetValue(charId, out var id)
            && _matches.TryGetValue(id, out var m) ? m : null;

    /// <summary>Recode <c>TuneMode</c> @0x8056c50 : évalue les events
    /// dans l'ordre natif (Challenge 201 → Normal 200 → Twenty 202 →
    /// ClassPK 203 — le dernier actif écrase), fixe Mode=schedId et
    /// IsOpen. Appelé sur les requêtes mode/join (les events temps
    /// réel natifs sont remplacés par une évaluation paresseuse du
    /// calendrier).</summary>
    public void RefreshMode(
        IReadOnlyDictionary<int, List<GameDataService.ScheduleEntry>>
            schedules, DateTimeOffset now)
    {
        // Ordre d'évaluation natif : chaque event actif écrit le
        // schedId — le dernier gagne : Challenge→1, Normal→0,
        // Twenty→2, ClassPK→3 (priorité croissante).
        var open = false;
        foreach (var (schedType, mode) in new[]
            { (201, 1), (200, 0), (202, 2), (203, 3) })
        {
            if (!IsWindowActive(schedules, schedType, now)) continue;
            Mode = mode;
            open = true;
        }
        IsOpen = open;
    }

    /// <summary>Vrai si une fenêtre du type de schedule couvre
    /// l'instant <paramref name="now"/> (entrée du jour courant —
    /// natif : entry+0x16 == tm_wday, begin ≤ now &lt; end).</summary>
    public static bool IsWindowActive(
        IReadOnlyDictionary<int, List<GameDataService.ScheduleEntry>>
            schedules, int schedType, DateTimeOffset now)
    {
        if (!schedules.TryGetValue(schedType, out var entries))
            return false;
        var nowHhmm = now.Hour * 100 + now.Minute;
        // tm_wday : 0=dim..6=sam ; DayId natif est indexé pareil
        // (AllSchedules filtre sur entry+0x16 == wday).
        var wday = (int)now.DayOfWeek;
        foreach (var e in entries)
        {
            if (e.DayId != wday && e.DayId != wday + 1
                && e.DayId != ((wday + 6) % 7) + 1)
                continue;
            // Fenêtre couvrant minuit : end ≤ begin → end le
            // lendemain.
            var active = e.EndHhmm > e.BeginHhmm
                ? nowHhmm >= e.BeginHhmm && nowHhmm < e.EndHhmm
                : nowHhmm >= e.BeginHhmm || nowHhmm < e.EndHhmm;
            if (active) return true;
        }
        return false;
    }

    /// <summary><c>GetCategory</c> @0x8056a80 : catégorie de file du
    /// personnage pour le mode courant — schedId 1 → 1 ; schedId 3 →
    /// regroupement par classe (gamedata native non portée : bucket
    /// par tranche de SLV /10 pour rester déterministe) ; sinon
    /// level/10 plafonné à 8 (level &gt; 0x50=80 → 8).</summary>
    public int CategoryOf(int level, int classLevel)
    {
        return Mode switch
        {
            1 => 1,
            3 => classLevel > 80 ? 8 : Math.Max(1, classLevel / 10),
            _ => level > 0x50 ? 8 : Math.Max(0, level / 10),
        };
    }

    /// <summary><c>Join</c> mission @0x8058xxx : inscrit le perso
    /// dans la file de sa catégorie puis tente de former un match
    /// (natif : les rounds <c>CEventColosseumTeamJoin</c> démarrent
    /// cycliquement pendant l'ouverture ; approximation documentée —
    /// un match est formé dès que la file de la catégorie atteint 2
    /// inscrits, camps 1v1).</summary>
    /// <returns>La liste des charId admis dans un nouveau match, ou
    /// null si le perso attend en file.</returns>
    public List<int>? Join(int charId, int level, int classLevel)
    {
        if (InQueue(charId) || MatchOf(charId) != null) return null;
        var cat = CategoryOf(level, classLevel);
        if (cat == 0) return null;   // cat 0 → texte natif (Join log)
        if (!_queues.TryGetValue(cat, out var q))
            _queues[cat] = q = new List<int>();
        q.Add(charId);
        EdenLog.Write($"[Colosseum] join char={charId} cat={cat} " +
            $"queue={q.Count} mode={Mode}");
        if (q.Count < 2) return null;   // attente d'un adversaire
        // Formation du match : deux premiers inscrits, camps 1v1
        // (MemberSize natif par formula — non prouvé, 1 par camp).
        var a = q[0]; var b = q[1];
        q.RemoveRange(0, 2);
        var match = new Match
        {
            Id = _nextMatchId++,
            NodeId = ArenaNodeId,
            Category = cat,
            Teams = new List<List<int>> { new() { a }, new() { b } },
            Status = 0,
        };
        _matches[match.Id] = match;
        _charMatch[a] = match.Id;
        _charMatch[b] = match.Id;
        EdenLog.Write($"[Colosseum] match {match.Id} formé : " +
            $"{a} vs {b} (cat {cat})");
        return new List<int> { a, b };
    }

    /// <summary><c>LeaveJoin</c> mission @0x8056xxx : retire le perso
    /// de la file (le leave pendant un match est refusé zone-side,
    /// texte 0xd1f).</summary>
    /// <returns>Vrai si le perso était en file.</returns>
    public bool Leave(int charId)
    {
        var removed = false;
        foreach (var q in _queues.Values)
            removed |= q.Remove(charId);
        if (removed)
            EdenLog.Write($"[Colosseum] leave char={charId}");
        return removed;
    }

    /// <summary>Dissout un match (fin de round / joueur parti) —
    /// retire les charId de la map char→match.</summary>
    public void EndMatch(int matchId)
    {
        if (!_matches.Remove(matchId, out var m)) return;
        foreach (var team in m.Teams)
            foreach (var c in team)
                _charMatch.Remove(c);
        EdenLog.Write($"[Colosseum] match {matchId} terminé");
    }

    /// <summary>Nettoyage déconnexion : retire le perso des files et
    /// dissout le match le cas échéant
    /// (<c>CColosseumFactory::CharacterLeave</c> natif).</summary>
    public void CharacterLeave(int charId)
    {
        Leave(charId);
        var m = MatchOf(charId);
        if (m != null) EndMatch(m.Id);
    }

    /// <summary>Créneaux du jour courant pour les types 200-203 —
    /// alimente la réponse 587 (<c>TC_ColosseumDataQuery</c>
    /// @0x81fa770 : pour chaque sched, les entrées du jour
    /// (entry+0x16 == wday) → schedType, begin/end epoch du jour,
    /// flag actif, HHMM bruts).</summary>
    public List<TodaySlot> TodaySlots(
        IReadOnlyDictionary<int, List<GameDataService.ScheduleEntry>>
            schedules, DateTimeOffset now)
    {
        var wday = (int)now.DayOfWeek;
        var today = now.UtcDateTime.Date;
        var slots = new List<TodaySlot>();
        for (var t = SchedTypeBase; t <= SchedTypeBase + 3; t++)
        {
            if (!schedules.TryGetValue(t, out var entries))
                continue;
            foreach (var e in entries)
            {
                // entry+0x16 == jour courant (le natif compare au
                // wday local ; on tolère les index 0-6 et 1-7).
                if (e.DayId != wday && e.DayId != wday + 1
                    && e.DayId != ((wday + 6) % 7) + 1)
                    continue;
                var begin = new DateTimeOffset(today.AddSeconds(
                    e.BeginHhmm / 100 * 3600 + e.BeginHhmm % 100 * 60),
                    TimeSpan.Zero);
                var endDay = e.EndHhmm <= e.BeginHhmm
                    ? today.AddDays(1) : today;
                var end = new DateTimeOffset(endDay.AddSeconds(
                    e.EndHhmm / 100 * 3600 + e.EndHhmm % 100 * 60),
                    TimeSpan.Zero);
                slots.Add(new TodaySlot(t, (int)begin.ToUnixTimeSeconds(),
                    (int)end.ToUnixTimeSeconds(),
                    (short)e.BeginHhmm, (short)e.EndHhmm,
                    IsOpen && Mode == t - SchedTypeBase));
            }
        }
        return slots;
    }

    /// <summary>Un créneau du jour pour la réponse 587.</summary>
    /// <param name="SchedType">Type de schedule (200-203).</param>
    /// <param name="BeginEpoch">Début epoch du créneau (aujourd'hui).</param>
    /// <param name="EndEpoch">Fin epoch.</param>
    /// <param name="BeginHhmm">Heure brute HHMM (local_1a8+0x18 natif).</param>
    /// <param name="EndHhmm">Fin brute HHMM (+0x1a).</param>
    /// <param name="Active">Créneau actuellement actif (flag u8 du
    /// vecteur — natif : 1 si schedType == 200+factory+100 et ouvert).</param>
    public sealed record TodaySlot(int SchedType, int BeginEpoch,
        int EndEpoch, short BeginHhmm, short EndHhmm, bool Active);
}
