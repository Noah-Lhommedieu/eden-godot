using Eden.Core.Logging;

namespace Eden.Core.Game.Services;

/// <summary>
/// Battle Royale (arène FFA par file d'attente) — miroir de
/// <c>CBattleRoyaleFactory</c> mission natif. Preuves désasm :
/// <list type="bullet">
///   <item><c>Init</c> @0x08049f20 : factory+4 = <b>0x1e (30)</b>
///   joueurs par BR, factory+8 = <b>0x135 (309)</b> node arène
///   (S_Node 309 type 0xf) ; event <c>CEventBRSchedule</c> sur le
///   type S_Schedule <b>502</b> (lun/mer/ven/dim 21h31-22h59).</item>
///   <item><c>JoinWait</c> @0x0804ad70 : file FIFO ; BR fermé →
///   texte 0x18c5, déjà inscrit → 0x18c3 ; à 30 →
///   <c>CreateNewBattleRoyale</c> + <c>NotifyEnterBattleRoyale</c>
///   @0x0804a3a0 (TextCommand EnterBattleRoyale par membre →
///   <c>Mission_TC_EnterBattleRoyale</c> zone @0x083b6030 : 591
///   BattleRoyaleInfo [0][u16 0][10][0…] + char+0xd90=br_id +
///   CEventEnterColosseum → node 309).</item>
///   <item>Execute zone 241 @0x0823da10 : v2≠0 en BR →
///   <c>LeaveBattleRoyale</c> ; v1≠0 → gates (level≥20, pas en
///   versus/BR, unlocked, pas en versus instance, types de scène
///   exclus, pas d'enchant 54486) puis relais JoinWait.</item>
/// </list>
/// </summary>
public sealed class BattleRoyaleService
{
    /// <summary>Type S_Schedule des créneaux BR (502).</summary>
    public const int SchedType = 502;

    /// <summary>Joueurs déclencheurs (factory+4 = 0x1e natif).</summary>
    public const int GroupSize = 30;

    /// <summary>Node arène BR (factory+8 = 0x135 → S309, type 0xf).</summary>
    public const int ArenaNodeId = 309;

    /// <summary>File d'attente FIFO (this+0x1c..0x20 natif).</summary>
    private readonly List<int> _queue = new();

    /// <summary>BRs actifs : brId → membres (factory+0x30 map).</summary>
    private readonly Dictionary<int, List<int>> _instances = new();
    private readonly Dictionary<int, int> _charInstance = new();
    private int _nextId = 1;

    /// <summary>Vrai si le perso attend en file.</summary>
    public bool InQueue(int charId) => _queue.Contains(charId);

    /// <summary>BR courant du perso (char+0xbb4 natif) — id ou 0.</summary>
    public int InstanceOf(int charId)
        => _charInstance.TryGetValue(charId, out var id) ? id : 0;

    /// <summary>Ouverture BR à l'instant donné — créneaux
    /// S_Schedule 502 (factory+0x40 event natif).</summary>
    public static bool IsOpen(
        IReadOnlyDictionary<int, List<GameDataService.ScheduleEntry>>
            schedules, DateTimeOffset now)
        => ColosseumService.IsWindowActive(schedules, SchedType, now);

    /// <summary><c>JoinWait</c> natif : push FIFO ; à 30 → nouveau
    /// BR (CreateNewBattleRoyale + NotifyEnterBattleRoyale).</summary>
    /// <returns>(brId, membres) si un BR démarre, sinon null.</returns>
    public (int Id, IReadOnlyList<int> Members)? Join(int charId)
    {
        if (InQueue(charId) || InstanceOf(charId) != 0) return null;
        _queue.Add(charId);
        EdenLog.Write($"[BR] join char={charId} " +
            $"queue={_queue.Count}/{GroupSize}");
        if (_queue.Count < GroupSize) return null;

        var members = _queue.GetRange(0, GroupSize);
        _queue.RemoveRange(0, GroupSize);
        var id = _nextId++;
        _instances[id] = members;
        foreach (var c in members) _charInstance[c] = id;
        EdenLog.Write($"[BR] instance {id} créée node=309 : " +
            $"{members.Count} joueurs");
        return (id, members);
    }

    /// <summary>Sortie de file (<c>LeaveWait</c> natif).</summary>
    public bool Leave(int charId)
    {
        var removed = _queue.Remove(charId);
        if (removed) EdenLog.Write($"[BR] leave-queue char={charId}");
        return removed;
    }

    /// <summary><c>LeaveBattleRoyale</c> natif : retire le perso du
    /// BR ; le BR est dissous quand il est vide (ou &lt;2 restants —
    /// plus de combat possible).</summary>
    /// <returns>Le brId quitté, ou 0.</returns>
    public int LeaveInstance(int charId)
    {
        var id = InstanceOf(charId);
        if (id == 0) return 0;
        _charInstance.Remove(charId);
        if (_instances.TryGetValue(id, out var members))
        {
            members.Remove(charId);
            if (members.Count < 2)
            {
                // BR à un seul joueur → fin : les survivants sont
                // retirés par l'appelant (LeaveBattleRoyale natif
                // déclenche aussi la fin de round).
                foreach (var c in members) _charInstance.Remove(c);
                _instances.Remove(id);
                EdenLog.Write($"[BR] instance {id} fermée");
            }
        }
        return id;
    }

    /// <summary>Membres restants d'un BR (post-leave).</summary>
    public IReadOnlyList<int> MembersOf(int brId)
        => _instances.TryGetValue(brId, out var m) ? m : Array.Empty<int>();

    /// <summary>Nettoyage déconnexion.</summary>
    public void CharacterLeave(int charId)
    {
        Leave(charId);
        LeaveInstance(charId);
    }
}
