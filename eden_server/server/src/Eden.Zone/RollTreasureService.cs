using System.Collections.Concurrent;

namespace Eden.Zone;

/// <summary>
/// Port de <c>lapis::RollitemManager</c> natif @0x84bb2a0+ — le
/// registre des rolls de loot d'équipe (need/greed/pass) sur les
/// trésors au sol. Natif : le manager vit sur <c>CForceTeam+0xa4</c>
/// mais la map est indexée par uid de trésor, unique globalement —
/// un dictionnaire global est équivalent.
///
/// Cycle natif (<c>CItemFactory::PlayerPickUpTreasure</c> @0x80b3130 +
/// <c>CheckRollItem</c> @0x84bc220 + <c>StartRollTreasure</c>
/// @0x84bb560 + <c>CEventRollTreasure::Execute</c> @0x819bbb0) :
///   1. un membre d'équipe ramasse un trésor éligible →
///      <c>CreateRollItem</c> (participants = membres sur le node) ;
///   2. chaque participant répond need/greed/pass (NCID 193) →
///      <c>CheckRollItem</c> : jet aléatoire 0-98 dédupliqué entre
///      les participants ayant déjà répondu ;
///   3. tous ont répondu OU timeout 120 s (S_Parameter[341],
///      <c>AllFormulaParas+0x554</c>) OU déconnexion →
///      <c>UpdateRoll</c> : non-répondants = pass, puis
///      <c>StartRollTreasure</c> : tri décroissant (<c>Rollsort</c>
///      @0x84bb300) des "need" d'abord puis des "greed" ;
///   4. aucun need/greed → <c>CTreasure::SetRollGiveUp</c> +
///      texte 0xb7a ; sinon le trésor est donné au gagnant
///      (<c>RollPickUpTreasure</c> @0x80b2210).
/// </summary>
public sealed class RollTreasureService
{
    /// <summary>Fenêtre du roll — S_Parameter[341] = 120 s
    /// (<c>AllFormulaParas+0x554</c> dans CEventRollTreasure).</summary>
    public const double RollSeconds = 120.0;

    /// <summary>Jet d'un participant — `random() % 99` natif.</summary>
    public const int RollMax = 99;

    /// <summary>Entrée `RollTeamPeople` native (12 octets :
    /// charId u32, choice s16 +4, flag byte +6, roll s16 +8).</summary>
    public sealed class RollEntry
    {
        /// <summary>charId du participant.</summary>
        public required int CharId;
        /// <summary>Vrai quand le joueur a répondu (byte +6).</summary>
        public bool Answered;
        /// <summary>Choix : 0=pass, 1=need, 2=greed (s16 +4).</summary>
        public short Choice;
        /// <summary>Jet 0-98 (s16 +8).</summary>
        public short Roll;
    }

    /// <summary>Roll en cours — nœud de la map `pRVar7+0x16`,
    /// nom d'item `pRVar7+0x24` (affiché dans les textes).</summary>
    public sealed class Roll
    {
        /// <summary>Uid du trésor = id du roll.</summary>
        public required uint Id;
        /// <summary>Node du trésor (u16 +0x16 natif).</summary>
        public required int NodeId;
        /// <summary>Instance (groupe) — propre à notre modèle.</summary>
        public required int InstanceGroup;
        /// <summary>Nom affiché de l'item (pRVar7+0x24 natif).</summary>
        public required string ItemName;
        /// <summary>Participants (membres d'équipe présents sur le
        /// node à la création).</summary>
        public required List<RollEntry> Entries;
    }

    /// <summary>Résultat de <see cref="CheckRoll"/>.</summary>
    public enum RollCheckResult
    {
        /// <summary>Roll inconnu ou char non participant.</summary>
        NotParticipant,
        /// <summary>Le char a déjà répondu.</summary>
        AlreadyAnswered,
        /// <summary>Réponse enregistrée — d'autres réponses attendues.</summary>
        Recorded,
        /// <summary>Réponse enregistrée — tous ont répondu.</summary>
        Complete,
    }

    private readonly ConcurrentDictionary<uint, Roll> _rolls = new();

    /// <summary>`CreateRollItem` — enregistre le roll pour les
    /// participants listés (membres de la team présents sur le
    /// node au moment du pickup).</summary>
    public void Create(uint treasureId, int nodeId, int inst,
        string itemName, IEnumerable<int> participantCharIds)
    {
        var entries = participantCharIds
            .Select(c => new RollEntry { CharId = c })
            .ToList();
        _rolls[treasureId] = new Roll
        {
            Id = treasureId, NodeId = nodeId, InstanceGroup = inst,
            ItemName = itemName, Entries = entries,
        };
    }

    /// <summary>Lecture.</summary>
    public bool TryGet(uint id, out Roll roll) =>
        _rolls.TryGetValue(id, out roll!);

    /// <summary>`CheckRollItem(rollId, charId, choice)` — enregistre
    /// la réponse : jet 0-98 tiré en évitant les valeurs déjà prises
    /// par les participants ayant répondu (la boucle `goto` du natif
    /// re-tire le dé). Retourne l'état + le jet tiré.</summary>
    public (RollCheckResult Result, short RollValue) CheckRoll(
        uint rollId, int charId, short choice)
    {
        if (!_rolls.TryGetValue(rollId, out var r)) return (default, 0);
        var entry = r.Entries.FirstOrDefault(e => e.CharId == charId);
        if (entry == null) return (default, 0);
        if (entry.Answered) return (RollCheckResult.AlreadyAnswered, 0);

        // Jet dédupliqué — le natif re-tire tant qu'un autre
        // participant répondu a la même valeur (sVar19 = rand % 99).
        var taken = new HashSet<short>(
            r.Entries.Where(e => e.Answered).Select(e => e.Roll));
        short roll;
        var guard = 0;
        do
        {
            roll = (short)Random.Shared.Next(RollMax);
        } while (taken.Contains(roll) && ++guard < 200);

        entry.Answered = true;
        entry.Choice = choice;
        entry.Roll = roll;
        var complete = r.Entries.All(e => e.Answered);
        return (complete ? RollCheckResult.Complete
            : RollCheckResult.Recorded, roll);
    }

    /// <summary>`UpdateRoll` — les non-répondants passent (choice=0,
    /// answered) puis résolution. Retourne le charId du gagnant ou
    /// null quand tout le monde a passé (all-pass → SetRollGiveUp
    /// natif). Tri : need décroissant puis greed décroissant
    /// (<c>Rollsort</c> : `a.roll &gt; b.roll`).</summary>
    public int? Resolve(uint rollId)
    {
        if (!_rolls.TryGetValue(rollId, out var r)) return null;
        foreach (var e in r.Entries.Where(e => !e.Answered))
        {
            e.Answered = true;   // non-répondant → pass
            e.Choice = 0;
        }
        return WinnerOf(r);
    }

    /// <summary>Gagnant d'un roll déjà résolu (need &gt; greed,
    /// décroissant par jet).</summary>
    public static int? WinnerOf(Roll r)
        => r.Entries.Where(e => e.Choice is 1 or 2)
            .OrderByDescending(e => e.Choice == 1 ? 1 : 0)
            .ThenByDescending(e => e.Roll)
            .Select(e => (int?)e.CharId).FirstOrDefault();

    /// <summary>`RemoveRollItem` — libère le roll.</summary>
    public void Remove(uint rollId) => _rolls.TryRemove(rollId, out _);

    /// <summary>`UserDisconnect` — marque pass les participations non
    /// répondues du char et retourne les rolls devenus complets (le
    /// natif résout immédiatement ceux-là).</summary>
    public uint[] UserDisconnect(int charId)
    {
        var completed = new List<uint>();
        foreach (var (id, r) in _rolls)
        {
            var entry = r.Entries.FirstOrDefault(e =>
                e.CharId == charId && !e.Answered);
            if (entry == null) continue;
            entry.Answered = true;
            entry.Choice = 0;
            if (r.Entries.All(e => e.Answered)) completed.Add(id);
        }
        return completed.ToArray();
    }
}
