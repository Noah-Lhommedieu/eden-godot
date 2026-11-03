namespace Eden.Core.Game.Services;

/// <summary>
/// État planifié des territoires — moitié "registry" du
/// <see cref="TerritoryWarService"/> (l'autre moitié porte les scores
/// runtime d'une bataille en cours). Équivaut à la map globale
/// <c>CTerritory*/CFamilyBattle</c> de <c>CGlobalInfo</c> côté mission
/// (<c>TC_TerritoryDataQuery</c> @0x8211860,
/// <c>TC_TerritoryJoin</c> @0x8236860) :
/// <list type="bullet">
///   <item><c>CTerritory+4</c> = territory_id, <c>+8</c> = phase
///   (0 idle / 1 prépare / 2 bataille — posée par
///   <c>TerritoryWarStart</c> zone @0x8158c90), <c>+0xc</c> = node de
///   bataille, <c>+0x14</c> = own_family_id, <c>+0x18</c> = nom,
///   <c>+0x1c/+0x20</c> = begin/end epochs, <c>+0x24</c> = flag,
///   <c>+0x28</c> = own_times, <c>+0x30/+0x48</c> = maps des familles
///   défenseur/attaquant, <c>+0x78/+0x90</c> = persos enrôlés par camp,
///   <c>+0x88/+0xa0</c> = effectifs.</item>
///   <item>Types <c>S_Territory</c> col5 → <c>CTerritoryData+0x10</c> :
///   1-15 = guerres (→ schedule type-1 via
///   <c>GetScheduleTypeByTerritoryType</c> @0x8071ec0),
///   0x12d-0x13b (301-315) = <c>CFamilyBattle</c> (schedule identique),
///   601-603 = instances versus (ids 27-29).</item>
///   <item>Caps du Join natif (@0x81dadb0) : <c>CharMaxNum</c>=350
///   (.data 0x85a56cc) par camp pour les guerres,
///   <c>FB_CharMaxNum</c>=50 pour les family battles.</item>
/// </list>
/// </summary>
public sealed partial class TerritoryWarService
{
    /// <summary>Cap défenseur/attaquant d'une guerre de territoire —
    /// <c>CTerritory::CharMaxNum</c> natif (.data 0x85a56cc).</summary>
    public const int CharMaxNum = 350;

    /// <summary>Cap par camp d'une élimination de famille —
    /// <c>CTerritory::FB_CharMaxNum</c> natif (.data 0x85a56ce).</summary>
    public const int FBCharMaxNum = 50;

    /// <summary>Niveau minimum du join — <c>DF_LevelLimit</c> natif
    /// (rodata 0x88901b8 = 30), vérifié par le 190/225 type 2.</summary>
    public const int LevelLimit = 30;

    /// <summary>État runtime d'un territoire — un objet par définition
    /// <c>S_Territory</c> chargée.</summary>
    public sealed class TerritoryState
    {
        /// <summary>territory_id (<c>CTerritory+4</c>).</summary>
        public int Id { get; init; }
        /// <summary>Type S_Territory col5 (<c>CTerritoryData+0x10</c>).</summary>
        public int Type { get; init; }
        /// <summary>Nom Big5 (<c>CTerritory+0x18</c>).</summary>
        public string Name { get; init; } = "";
        /// <summary>Node rattaché — S_Territory col3 (map monde pour
        /// les guerres, node d'instance 407 pour les versus).</summary>
        public int AttachedNodeId { get; init; }
        /// <summary>Famille propriétaire (<c>+0x14</c>, table
        /// <c>territory_war</c> ; 0 = libre).</summary>
        public int OwnFamilyId { get; set; }
        /// <summary>Victoires consécutives (<c>+0x28</c>).</summary>
        public int OwnTimes { get; set; }
        /// <summary>Phase courante (<c>+8</c> : 0/1/2).</summary>
        public short Phase { get; set; }
        /// <summary>Fenêtre courante en epochs unix
        /// (<c>+0x1c/+0x20</c>).</summary>
        public int BeginEpoch { get; set; }
        public int EndEpoch { get; set; }
        /// <summary>Flag divers (<c>+0x24</c>).</summary>
        public byte Flag { get; set; }
        /// <summary>Node type-8 où se déroule la bataille — choisi au
        /// démarrage parmi les nodes de guerre libres (la mission
        /// native crée l'instance et l'envoie dans TerritoryInfo
        /// paquet+0x30).</summary>
        public int BattleNodeId { get; set; }
        /// <summary>Familles inscrites côté défenseur
        /// (<c>map +0x30</c>) — la propriétaire est défenseure.</summary>
        public HashSet<int> DefenderFamilies { get; } = new();
        /// <summary>Familles inscrites côté attaquant
        /// (<c>map +0x48</c>).</summary>
        public HashSet<int> AttackerFamilies { get; } = new();
        /// <summary>Persos au sol par camp (<c>maps +0x78/+0x90</c> ;
        /// sizes en <c>+0x88/+0xa0</c>).</summary>
        public HashSet<int> DefenderChars { get; } = new();
        public HashSet<int> AttackerChars { get; } = new();
        /// <summary>Compteurs de kills par camp — terr+0x30 (défenseur)
        /// / +0x34 (attaquant) natifs, incrémentés par le handler de
        /// mort et émis dans le 532 périodique.</summary>
        public int DefenderKills;
        public int AttackerKills;
        /// <summary>Kills par perso — base du char+0xb9c natif et du
        /// leaderboard 562 (nom perso/famille conservés au join pour
        /// les entrées rank).</summary>
        public readonly Dictionary<int, int> CharKills = new();
        public readonly Dictionary<int, (string Player, string Family,
            int FamilyId)> CharInfos = new();
        /// <summary>Score par famille (kills + cristaux) — base des
        /// entrées 566 (vecteur terr+0xf4 natif).</summary>
        public readonly Dictionary<int, (string Name, int Side,
            int Score)> FamilyScores = new();
        /// <summary>Transition active→fin détectée — le natif pousse
        /// <c>CEventTerritoryClose</c> ; ici le flag est posé par
        /// <see cref="RefreshPhases"/> et consommé par la zone qui
        /// émet 516-type2/562/566/547 + persistance avant d'appeler
        /// <see cref="FinalizeClose"/> (les scores doivent survivre
        /// le temps des émissions).</summary>
        public bool ClosePending;
        /// <summary>1 si family battle (type 0x12d-0x13b), 0 sinon —
        /// choisit le cap CharMaxNum/FB_CharMaxNum natif.</summary>
        public bool IsFamilyBattle => Type is >= 0x12d and <= 0x13b;
    }

    /// <summary>Résultat du Join — codes de retour de
    /// <c>CTerritory::Join</c> mission @0x81dadb0.</summary>
    public enum JoinResult
    {
        /// <summary>0 — pas de famille (exec+0xb0==0).</summary>
        NoFamily = 0,
        /// <summary>1 — camp plein / pas de place.</summary>
        Full = 1,
        /// <summary>2 — perso déjà enrôlé (dans +0x78 ou +0x90).</summary>
        AlreadyIn = 2,
        /// <summary>3 — succès : camp posé dans exec+0x11c, zone
        /// téléporte vers le node de bataille.</summary>
        Success = 3,
    }

    /// <summary>Territoires chargés, indexés par id (map croissante —
    /// l'ordre du 515 natif).</summary>
    private readonly SortedDictionary<int, TerritoryState>
        _territories = new();

    /// <summary>Nodes type-8 disponibles pour les batailles — choisis
    /// au démarrage par ordre croissant parmi les non utilisés (la
    /// mission native instancie le node et l'annonce ; notre pool est
    /// statique S_Node type 8 : 301,302,303,305,306,307,308).</summary>
    private static readonly int[] BattleNodePool =
        { 301, 302, 303, 305, 306, 307, 308 };

    /// <summary>Charge les définitions S_Territory + l'état persisté
    /// <c>territory_war</c> — équivalent de l'init
    /// <c>CTerritoryQuery::Init/LoadDB</c> mission.</summary>
    public void LoadTerritories(
        IReadOnlyDictionary<int, GameDataService.TerritoryDef> defs,
        IEnumerable<(int TerritoryId, int OwnFamilyId, int OwnTimes)>
            warRows)
    {
        var owners = warRows.ToDictionary(
            r => r.TerritoryId, r => (r.OwnFamilyId, r.OwnTimes));
        foreach (var (id, def) in defs)
        {
            owners.TryGetValue(id, out var own);
            var st = new TerritoryState
            {
                Id = id, Type = def.Type, Name = def.Name,
                OwnFamilyId = own.OwnFamilyId, OwnTimes = own.OwnTimes,
                // col3 S_Territory = node rattaché (map monde ; 407
                // pour les versus).
                AttachedNodeId = def.AttachedNodeId,
            };
            // La famille propriétaire est le camp défenseur natif.
            if (st.OwnFamilyId != 0)
                st.DefenderFamilies.Add(st.OwnFamilyId);
            _territories[id] = st;
        }
    }

    /// <summary>Type de schedule d'un territoire —
    /// <c>CGlobalInfo::GetScheduleTypeByTerritoryType</c> @0x8071ec0 :
    /// types 1-15 → type-1 ; 0x12d-0x13b → identique ; sinon -1.</summary>
    public static int ScheduleTypeForTerritoryType(int territoryType)
        => territoryType switch
        {
            >= 1 and <= 15 => territoryType - 1,
            >= 0x12d and <= 0x13b => territoryType,
            _ => -1,
        };

    /// <summary>Lookup natif <c>CGlobalInfo::GetTerritory</c> —
    /// null si l'id n'est pas un territoire chargé.</summary>
    public TerritoryState? GetTerritory(int id)
        => _territories.TryGetValue(id, out var t) ? t : null;

    /// <summary>Tous les territoires par id croissant (ordre de la
    /// map native — le 515 les itère ainsi).</summary>
    public IReadOnlyCollection<TerritoryState> AllTerritories
        => _territories.Values;

    /// <summary>Lookup natif <c>CGlobalInfo::GetFamilyBattle</c>
    /// @0x8073d20 : family battle dont la phase ∈ {1,2} et dont un
    /// des deux camps contient <paramref name="familyId"/>.</summary>
    public TerritoryState? GetFamilyBattle(int familyId)
    {
        foreach (var t in _territories.Values)
            if (t.IsFamilyBattle && t.Phase is 1 or 2
                && (t.DefenderFamilies.Contains(familyId)
                    || t.AttackerFamilies.Contains(familyId)))
                return t;
        return null;
    }

    /// <summary>Recalcule phases/fenêtres depuis le calendrier
    /// <c>S_Schedule</c> — appelé avant chaque réponse (le natif fait
    /// avancer les phases via les événements <c>CEventTerritory</c> ;
    /// ici la phase dérive du créneau : 2 pendant [begin,end],
    /// 1 pendant les 15 min de préparation qui précèdent).</summary>
    public void RefreshPhases(
        IReadOnlyDictionary<int, List<GameDataService.ScheduleEntry>>
            schedules,
        DateTimeOffset now)
    {
        var usedNodes = new HashSet<int>();
        foreach (var t in _territories.Values)
        {
            var schedType = ScheduleTypeForTerritoryType(t.Type);
            if (schedType < 0
                || !schedules.TryGetValue(schedType, out var entries))
            {
                // Même sans créneau, une phase active qui tombe doit
                // passer par le close (CEventTerritoryClose natif).
                if (t.Phase is 1 or 2 && !t.ClosePending)
                    t.ClosePending = true;
                t.Phase = 0;
                continue;
            }
            var (phase, begin, end) = CurrentWindow(entries, now);
            var wasActive = t.Phase is 1 or 2;
            t.Phase = phase;
            t.BeginEpoch = begin;
            t.EndEpoch = end;
            if (t.Phase is 1 or 2)
            {
                if (!wasActive || !usedNodes.Contains(t.BattleNodeId))
                    t.BattleNodeId = PickBattleNode(usedNodes,
                        t.AttachedNodeId);
                usedNodes.Add(t.BattleNodeId);
            }
            else if (wasActive && !t.ClosePending)
            {
                // Transition active→fin : CEventTerritoryClose natif —
                // les données (rosters, scores) survivent jusqu'aux
                // émissions de fin par la zone (FinalizeClose).
                t.ClosePending = true;
            }
        }
    }

    /// <summary>Créneau courant d'un schedule : (phase, begin, end).
    /// Les entrées S_Schedule portent jour de semaine (1-7, ISO) +
    /// bornes HHMM — projetées sur la semaine courante.</summary>
    private static (short Phase, int Begin, int End) CurrentWindow(
        List<GameDataService.ScheduleEntry> entries,
        DateTimeOffset now)
    {
        var today = now.UtcDateTime.Date;
        var nowSec = (int)now.ToUnixTimeSeconds();
        var best = ((short)0, 0, 0);
        foreach (var e in entries)
        {
            // Jour de la semaine ISO : DayOfWeek.Sunday=0 → 7.
            var isoDay = ((int)now.DayOfWeek + 6) % 7 + 1;
            for (var d = -1; d <= 1; d++)
            {
                // On évalue aussi hier/demain pour les créneaux qui
                // traversent minuit (begin>end ex. 2300→0100).
                var day = today.AddDays(e.DayId - isoDay + 7 * d);
                var begin = (int)new DateTimeOffset(
                    day.AddSeconds(e.BeginHhmm / 100 * 3600
                        + e.BeginHhmm % 100 * 60),
                    TimeSpan.Zero).ToUnixTimeSeconds();
                var endDay = e.EndHhmm <= e.BeginHhmm
                    ? day.AddDays(1) : day;
                var end = (int)new DateTimeOffset(
                    endDay.AddSeconds(e.EndHhmm / 100 * 3600
                        + e.EndHhmm % 100 * 60),
                    TimeSpan.Zero).ToUnixTimeSeconds();
                if (nowSec >= begin && nowSec < end)
                    return (2, begin, end);
                if (nowSec >= begin - 900 && nowSec < begin)
                    best = (1, begin, end);
            }
        }
        return best;
    }

    /// <summary>Choix du node de bataille : le node rattaché s'il est
    /// type-8 fixe (versus = 407), sinon premier node type-8 de guerre
    /// libre du pool (le natif crée une instance dédiée ; notre pool
    /// couvre les 7 maps de guerre).</summary>
    private static int PickBattleNode(HashSet<int> used,
        int attachedNodeId)
    {
        if (attachedNodeId is 407) return 407;
        foreach (var n in BattleNodePool)
            if (!used.Contains(n)) return n;
        return BattleNodePool[0];
    }

    /// <summary>Join natif <c>CTerritory::Join</c> @0x81dadb0 :
    /// déjà enrôlé → AlreadyIn ; famille dans le camp défenseur →
    /// camp 1 si place ; attaquant → camp 2 ; famille non inscrite →
    /// équilibrage vers le camp comptant le moins de familles
    /// (+0x40 vs +0x58) s'il reste de la place. Le cap est
    /// CharMaxNum (guerre) ou FB_CharMaxNum (battle).</summary>
    public JoinResult TryJoin(TerritoryState t, int charId,
        int familyId, out int camp,
        string playerName = "", string familyName = "")
    {
        camp = 0;
        if (familyId == 0) return JoinResult.NoFamily;
        if (t.DefenderChars.Contains(charId)
            || t.AttackerChars.Contains(charId))
            return JoinResult.AlreadyIn;
        var cap = t.IsFamilyBattle ? FBCharMaxNum : CharMaxNum;
        if (t.DefenderFamilies.Contains(familyId))
        {
            if (t.DefenderChars.Count >= cap) return JoinResult.Full;
            t.DefenderChars.Add(charId);
            camp = 1;
            return JoinResult.Success;
        }
        if (t.AttackerFamilies.Contains(familyId))
        {
            if (t.AttackerChars.Count >= cap) return JoinResult.Full;
            t.AttackerChars.Add(charId);
            camp = 2;
            return JoinResult.Success;
        }
        // Famille non inscrite → camp le moins peuplé en familles.
        if (t.DefenderFamilies.Count < t.AttackerFamilies.Count)
        {
            if (t.DefenderChars.Count >= cap) return JoinResult.Full;
            t.DefenderChars.Add(charId);
            camp = 1;
        }
        else
        {
            if (t.AttackerChars.Count >= cap) return JoinResult.Full;
            t.AttackerChars.Add(charId);
            camp = 2;
        }
        return JoinResult.Success;
    }

    /// <summary>Enregistre les infos du perso au join (noms pour les
    /// entrées 562/566) — appelé par le flux zone après un TryJoin
    /// réussi.</summary>
    public void NoteJoinInfo(TerritoryState t, int charId, int camp,
        string playerName, int familyId, string familyName)
    {
        t.CharInfos[charId] = (playerName, familyName, familyId);
        t.CharKills.TryAdd(charId, 0);
        if (!t.FamilyScores.ContainsKey(familyId))
            t.FamilyScores[familyId] = (familyName, camp, 0);
    }

    /// <summary>Kill pendant la bataille — le natif incrémente les
    /// compteurs par camp terr+0x30/+0x34 et le kills perso du tueur
    /// (char+0xb9c), puis CEventTerritoryKilledInfo broadcast le 532
    /// quand les compteurs ont changé. KillPoint = 1 natif
    /// (S_Parameter 488).</summary>
    /// <returns>Nouveau total du camp du tueur.</returns>
    public int RecordKill(TerritoryState t, int killerCharId,
        int camp)
    {
        t.CharKills[killerCharId] =
            t.CharKills.TryGetValue(killerCharId, out var k) ? k + 1 : 1;
        if (t.CharInfos.TryGetValue(killerCharId, out var info)
            && t.FamilyScores.TryGetValue(info.FamilyId, out var fs))
            t.FamilyScores[info.FamilyId] =
                (fs.Name, fs.Side, fs.Score + 1);
        if (camp == 1) return ++t.DefenderKills;
        return ++t.AttackerKills;
    }

    /// <summary>Leaderboard 562 — entrées triées par kills
    /// décroissants, rank = position (1-based, natif rank séquentiel).</summary>
    public List<(string Player, string Family, short Kills,
        short Rank)> BuildKillRank(TerritoryState t)
    {
        var rows = new List<(string, string, short, short)>();
        short rank = 0;
        foreach (var kv in t.CharKills
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value))
        {
            rank++;
            var (player, family, _) =
                t.CharInfos.TryGetValue(kv.Key, out var i)
                    ? i : ("", "", 0);
            rows.Add((player, family, (short)kv.Value, rank));
        }
        return rows;
    }

    /// <summary>Vainqueur de la bataille — famille au score maximal
    /// du camp qui a le plus de kills (approximation documentée :
    /// le natif décide du vainqueur via HandleRABattlefield +
    /// points cristaux ; égalité → défenseur conservé).</summary>
    /// <returns>familyId vainqueur ou 0 si indéterminé.</returns>
    public int ResolveWinnerFamily(TerritoryState t)
    {
        var winSide = t.DefenderKills >= t.AttackerKills ? 1 : 2;
        var best = t.FamilyScores
            .Where(f => f.Value.Side == winSide)
            .OrderByDescending(f => f.Value.Score)
            .Select(f => f.Key).FirstOrDefault();
        return best != 0 ? best
            : (winSide == 1 ? t.OwnFamilyId : 0);
    }

    /// <summary>Termine la fermeture — vide rosters/compteurs après
    /// les émissions de fin (complément de la transition posée par
    /// <see cref="RefreshPhases"/>).</summary>
    public void FinalizeClose(TerritoryState t)
    {
        t.DefenderChars.Clear();
        t.AttackerChars.Clear();
        t.AttackerFamilies.Clear();
        t.CharKills.Clear();
        t.CharInfos.Clear();
        t.FamilyScores.Clear();
        t.DefenderKills = 0;
        t.AttackerKills = 0;
        t.BattleNodeId = 0;
        t.ClosePending = false;
    }

    /// <summary>Retrait d'un perso — <c>CTerritoryWarFactory::
    /// CharacterLeave</c> natif (mappe charId → territoire via la
    /// présence dans les sets, retire du camp).</summary>
    public TerritoryState? Leave(int charId)
    {
        foreach (var t in _territories.Values)
        {
            if (t.DefenderChars.Remove(charId)
                || t.AttackerChars.Remove(charId))
                return t;
        }
        return null;
    }

    /// <summary>Inscrit une famille à un camp (équivalent de la map
    /// +0x30/+0x48 remplie par l'inscription native —
    /// <c>familybattle_ranks</c> / registration).</summary>
    public void RegisterFamily(int territoryId, int familyId,
        bool defender)
    {
        if (_territories.TryGetValue(territoryId, out var t))
            (defender ? t.DefenderFamilies : t.AttackerFamilies)
                .Add(familyId);
    }
}
