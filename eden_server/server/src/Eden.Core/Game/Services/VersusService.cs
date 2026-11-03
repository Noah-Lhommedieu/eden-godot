using Eden.Core.Logging;
using Eden.Core.Data;

namespace Eden.Core.Game.Services;

/// <summary>
/// Instance versus (arène PvP 5v5 par file d'attente) — miroir de
/// <c>CVersusInstanceFactory</c> mission natif. Preuves désasm :
/// <list type="bullet">
///   <item><c>Init</c> @0x080d7320 : factory+4 = <b>10</b> (taille du
///   groupe qui déclenche un match) ; events
///   <c>CEventVISchedule</c> pour les types S_Schedule 601-603 —
///   seul le type <b>602</b> existe dans les données (lun/mer/ven
///   19h-20h, sam 14h01-15h, dim 19h-20h).</item>
///   <item><c>JoinWait</c> @0x080d8ad0 : file FIFO des charIds ;
///   quand size == 10 → <c>CreateVersusInstance</c> @0x080d7e10
///   répartit la file en <b>2 camps</b> (map camp→charIds), puis
///   <c>CNC_ZMis_ZoneServerStartVersusInstance</c> à la zone versus
///   + TextCommand « EnterVersusInstance id node_tid camp area_id »
///   par membre (<c>Mission_TC_EnterVersusInstance</c> zone
///   @0x083b5e70 → 601 + transport).</item>
///   <item><c>LeaveVersusInstance</c> zone @0x0815e0d0 : status
///   membre=2, revive si mort, sortie d'équipe, 356 CanLogout,
///   retour aux coords cross-zone char+0xa98/+0xa9c/+0xaa0 ; le
///   chemin de sortie applique l'enchant <b>54486</b> (-0x2b2a)
///   900 s d'exclusion (zone @0x0815e4xx, EffectToLife).</item>
///   <item>Arènes : nodes <b>406/407</b> (S_Node type 0x11) ; spawn
///   par camp via <c>AllSpawnAreaIDs[char+0xdb4]</c> natif — chez
///   nous : aires NORMAL de la scène réparties par moitié ouest/est
///   (S406 : x&lt;~330 ouest, x&gt;~400 est).</item>
/// </list>
/// </summary>
public sealed class VersusService
{
    /// <summary>Type S_Schedule des créneaux versus (col 2 = 602).</summary>
    public const int SchedType = 602;

    /// <summary>Taille du groupe déclencheur (factory+4 = 10 natif).</summary>
    public const int GroupSize = 10;

    /// <summary>Enchant d'exclusion appliqué à la sortie
    /// (-0x2b2a = 54486, durée native 9000 ds = 900 s).</summary>
    public const int ExclusionEffectId = 54486;
    public const int ExclusionDurationSec = 900;

    /// <summary>Nombre de camps d'un match (map 2 entrées native).</summary>
    public const int CampCount = 2;

    /// <summary>Nodes d'arène versus (S_Node type 0x11) — le natif
    /// résout node_tid depuis l'instance ; chez nous alternance
    /// 406/407 par instance.</summary>
    public static readonly int[] ArenaNodes = { 406, 407 };

    /// <summary>File d'attente FIFO (this+0x20 natif).</summary>
    private readonly List<int> _queue = new();

    /// <summary>Instances actives : instId → match (this+0x48 map).</summary>
    private readonly Dictionary<int, VersusMatch> _instances = new();
    private readonly Dictionary<int, int> _charInstance = new();
    private int _nextInstanceId = 1;

    /// <summary>Un match versus : 2 camps de charIds + node arène.</summary>
    public sealed record VersusMatch(int Id, int NodeId,
        IReadOnlyList<int> CampA, IReadOnlyList<int> CampB)
    {
        /// <summary>Tous les membres (camp → charIds).</summary>
        public IEnumerable<(int Camp, int CharId)> Members()
        {
            foreach (var c in CampA) yield return (0, c);
            foreach (var c in CampB) yield return (1, c);
        }
    }

    /// <summary>Vrai si le perso attend en file (exec+0x684 flag
    /// natif posé par JoinWait).</summary>
    public bool InQueue(int charId) => _queue.Contains(charId);

    /// <summary>Instance courante du perso (char+0xbb4 natif).</summary>
    public VersusMatch? MatchOf(int charId)
        => _charInstance.TryGetValue(charId, out var id)
            && _instances.TryGetValue(id, out var m) ? m : null;

    /// <summary>Ouverture versus à l'instant donné — créneaux
    /// S_Schedule type 602 (même évaluation paresseuse que
    /// <see cref="ColosseumService.IsWindowActive"/>).</summary>
    public static bool IsOpen(
        IReadOnlyDictionary<int, List<GameDataService.ScheduleEntry>>
            schedules, DateTimeOffset now)
        => ColosseumService.IsWindowActive(schedules, SchedType, now);

    /// <summary><c>JoinWait</c> natif : push FIFO ; à 10 inscrits →
    /// <c>CreateVersusInstance</c> — répartition alternée en 2 camps
    /// de 5 (la boucle native itère iVar24 0..2 en piochant la file —
    /// distribution paire/impaire retenue, camps équilibrés).</summary>
    /// <returns>Le match formé, ou null si le perso attend.</returns>
    public VersusMatch? Join(int charId)
    {
        if (InQueue(charId) || MatchOf(charId) != null) return null;
        _queue.Add(charId);
        EdenLog.Write($"[Versus] join char={charId} " +
            $"queue={_queue.Count}/{GroupSize}");
        if (_queue.Count < GroupSize) return null;

        var members = _queue.GetRange(0, GroupSize);
        _queue.RemoveRange(0, GroupSize);
        var campA = new List<int>();
        var campB = new List<int>();
        for (var i = 0; i < members.Count; i++)
            (i % 2 == 0 ? campA : campB).Add(members[i]);
        var instId = _nextInstanceId++;
        var match = new VersusMatch(instId,
            ArenaNodes[(instId - 1) % ArenaNodes.Length],
            campA, campB);
        _instances[match.Id] = match;
        foreach (var (_, c) in match.Members())
            _charInstance[c] = match.Id;
        EdenLog.Write($"[Versus] instance {match.Id} créée sur " +
            $"node {match.NodeId} : A=[{string.Join(',', campA)}] " +
            $"B=[{string.Join(',', campB)}]");
        return match;
    }

    /// <summary>Sortie de file (<c>LeaveWait</c> natif — retire le
    /// charId de this+0x20).</summary>
    public bool Leave(int charId)
    {
        var removed = _queue.Remove(charId);
        if (removed)
            EdenLog.Write($"[Versus] leave-queue char={charId}");
        return removed;
    }

    /// <summary>Sortie d'instance (<c>LeaveVersusInstance</c> natif) —
    /// retire le perso du match ; l'instance est dissoute quand elle
    /// est vide (<c>CloseVersusInstance</c>).</summary>
    /// <returns>Le match quitté, ou null.</returns>
    public VersusMatch? LeaveInstance(int charId)
    {
        var match = MatchOf(charId);
        if (match == null) return null;
        _charInstance.Remove(charId);
        var remaining = match.Members().Where(m => m.CharId != charId)
            .Select(m => m.CharId).ToList();
        if (remaining.Count == 0)
        {
            _instances.Remove(match.Id);
            EdenLog.Write($"[Versus] instance {match.Id} fermée " +
                $"(vide)");
        }
        return match;
    }

    /// <summary>Nettoyage déconnexion (CharacterLeave natif).</summary>
    public void CharacterLeave(int charId)
    {
        Leave(charId);
        LeaveInstance(charId);
    }
}
