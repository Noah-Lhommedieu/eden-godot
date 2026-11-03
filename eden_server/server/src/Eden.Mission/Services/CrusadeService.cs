using Eden.Core.Logging;

namespace Eden.Mission.Services;

/// <summary>
/// Crusade (siège guild-vs-guild) — cycle de vie natif
/// `CBattleCrusadeFactory`/`CBattleCrusade` (mission, decompiled.c) :
/// les guildes s'inscrivent pendant la fenêtre « checkin » puis
/// s'affrontent sur la map croisade pour le contrôle du territoire.
///
/// Preuves natives :
/// <list type="bullet">
/// <item><c>CreateNewBattleCrusade(id, flag)</c> @0x8048640 :
///   `new CBattleCrusade(id, 600, tpl+0x3c * 60)` — checkin 600 s,
///   bataille `durée_minutes × 60` ; état crusade+0x28 : 1 = joinable
///   (checkin), 2 = bataille.</item>
/// <item><c>CBattleCrusade::Join</c> @0x81ac540 — codes : 2 = déjà
///   inscrit, 1 = plein (`+0x24 >= s16(+0xc)`), 3 = mauvais état,
///   4 = OK.</item>
/// <item><c>TC_BattleCrusadeJoin</c> @0x81ebc40 : Join==4 →
///   `NotifyEnterBattleCrusade` → EnterBattleCrusadeAnswer
///   [i32 char][s16][s16 crusade][s16 node] vers la zone hôte —
///   la zone stocke `char+0xdfc = crusade` et
///   <c>TransportCharacterToNode(node)</c>.</item>
/// <item>Maps croisade : S_Crusade 1-4 ↔ S_Node 310-313 (type 19,
///   時空裂縫•異域灰岩山/榮譽高地/寧靜之丘/腐朽沼澤).</item>
/// </list>
///
/// V2 : cycle Checkin(600 s)→Battle(durée cfg)→Closed, join relayé
/// de la zone (NCID 259 → « CrusadeJoin »), migration du joueur via
/// la réponse 201 étendue [char][crusade][res][node][0], scoring
/// des kills PvP relayé par la zone (« CrusadeKill »).
/// </summary>
public sealed class CrusadeService
{
    /// <summary>Phases crusade+0x28 natif : 1 = checkin (joinable),
    /// 2 = bataille en cours.</summary>
    public const int PhaseCheckin = 1, PhaseBattle = 2;

    /// <summary>Durée de la fenêtre checkin — constante 600 s du
    /// ctor natif `CBattleCrusade(id, 600, …)`.</summary>
    private static readonly TimeSpan CheckinDuration =
        TimeSpan.FromSeconds(600);

    /// <summary>Durée de bataille — tpl+0x3c × 60 natif. La fenêtre
    /// S_Schedule des croisades fait ~90 min (2131→2259) ; valeur
    /// surchargée par <c>EDEN_CRUSADE_BATTLE_MIN</c>.</summary>
    private static readonly TimeSpan BattleDuration =
        TimeSpan.FromMinutes(
            int.TryParse(
                Environment.GetEnvironmentVariable(
                    "EDEN_CRUSADE_BATTLE_MIN"), out var m) ? m : 90);

    /// <summary>Capacité de la file d'un crusade — s16(tpl+0xc)
    /// natif (capacité de la zone croisade) ; valeur de service.</summary>
    private const int MaxMembers = 200;

    /// <summary>CrusadeId → node de la map de siège (S_Crusade ↔
    /// S_Node type 19).</summary>
    private static readonly IReadOnlyDictionary<int, int> Nodes =
        new Dictionary<int, int> { { 1, 310 }, { 2, 311 },
            { 3, 312 }, { 4, 313 } };

    /// <summary>Une bataille crusade en cours (CBattleCrusade).</summary>
    public sealed class Battle
    {
        public int CrusadeId;
        public int NodeId;
        /// <summary>État natif +0x28 : 1 = checkin, 2 = bataille.</summary>
        public int Phase;
        public DateTime PhaseEndsAt;
        /// <summary>charId → familyId — le set +0x10 natif.</summary>
        public readonly Dictionary<int, int> Members = new();
        /// <summary>familyId → kills (score du siège).</summary>
        public readonly Dictionary<int, int> Scores = new();
        /// <summary>charId → kills perso.</summary>
        public readonly Dictionary<int, int> Kills = new();
    }

    private readonly Dictionary<int, Battle> _battles = new();
    /// <summary>crusadeId → familyId détentrice du territoire
    /// (ownership posée à la clôture — persistée en mémoire V2).</summary>
    private readonly Dictionary<int, int> _owners = new();

    /// <summary>Codes retour de <see cref="Join"/> — sémantique
    /// native : 1 = plein, 2 = déjà inscrit, 3 = mauvais état,
    /// 4 = OK.</summary>
    public const int JoinFull = 1, JoinDup = 2,
        JoinBadState = 3, JoinOk = 4;

    /// <summary>Battle d'un crusade, ou null si aucune en cours.</summary>
    public Battle? Get(int crusadeId) =>
        _battles.TryGetValue(crusadeId, out var b) ? b : null;

    /// <summary>Crusade auquel appartient un char (0 = aucun).</summary>
    public int CrusadeOf(int charId)
    {
        foreach (var b in _battles.Values)
            if (b.Members.ContainsKey(charId)) return b.CrusadeId;
        return 0;
    }

    /// <summary>Node de siège d'un crusade (0 si inconnu).</summary>
    public static int NodeOf(int crusadeId) =>
        Nodes.TryGetValue(crusadeId, out var n) ? n : 0;

    /// <summary>Guilde détentrice du territoire crusade
    /// (0 = aucune).</summary>
    public int OwnerOf(int crusadeId) =>
        _owners.TryGetValue(crusadeId, out var f) ? f : 0;

    /// <summary>Démarre le cycle d'un crusade — phase checkin
    /// 600 s puis bataille (<see cref="Tick"/> fait la transition).
    /// Créé par Init au boot (tpl+0x10≠0 natif) ou par commande.</summary>
    public Battle? Start(int crusadeId)
    {
        if (!Nodes.ContainsKey(crusadeId) ||
            _battles.ContainsKey(crusadeId))
            return null;
        var b = new Battle
        {
            CrusadeId = crusadeId,
            NodeId = Nodes[crusadeId],
            Phase = PhaseCheckin,
            PhaseEndsAt = DateTime.UtcNow + CheckinDuration,
        };
        _battles[crusadeId] = b;
        EdenLog.Write($"[Crusade] start crusade={crusadeId} " +
            $"node={b.NodeId} checkin 600s → battle " +
            $"{BattleDuration.TotalMinutes:0}min");
        return b;
    }

    /// <summary>Inscription d'un char — gates de
    /// <c>CBattleCrusade::Join</c> natif : état joinable (phase 1),
    /// capacité, dédoublonnage par charId.</summary>
    public int Join(int crusadeId, int charId, int familyId)
    {
        var b = Get(crusadeId);
        if (b == null || b.Phase != PhaseCheckin)
            return JoinBadState;
        if (b.Members.ContainsKey(charId)) return JoinDup;
        if (b.Members.Count >= MaxMembers) return JoinFull;
        b.Members[charId] = familyId;
        EdenLog.Write($"[Crusade] join crusade={crusadeId} " +
            $"char={charId} family={familyId} n={b.Members.Count}");
        return JoinOk;
    }

    /// <summary>Sortie — le char quitte le set (leave natif).</summary>
    public void Leave(int charId)
    {
        foreach (var b in _battles.Values)
            b.Members.Remove(charId);
    }

    /// <summary>Kill PvP dans la bataille — +1 score à la famille du
    /// tueur (pattern scores natifs) ; ignoré hors phase 2.</summary>
    public void OnKill(int crusadeId, int killerChar, int victimChar)
    {
        var b = Get(crusadeId);
        if (b == null || b.Phase != PhaseBattle) return;
        if (!b.Members.TryGetValue(killerChar, out var fam) || fam <= 0)
            return;
        // Kill entre guildes adverses uniquement — une victime sans
        // guilde inscrite (ou de la même) ne marque pas.
        if (!b.Members.TryGetValue(victimChar, out var vfam) ||
            vfam == fam)
            return;
        b.Scores[fam] = b.Scores.GetValueOrDefault(fam) + 1;
        b.Kills[killerChar] = b.Kills.GetValueOrDefault(killerChar) + 1;
        EdenLog.Write($"[Crusade] kill crusade={crusadeId} " +
            $"killer={killerChar} victim={victimChar} " +
            $"family={fam} score={b.Scores[fam]}");
    }

    /// <summary>Événement de transition du cycle —
    /// <paramref name="NewPhase"/> = 2 (bataille ouverte) ou 0
    /// (clôturé, <paramref name="WinnerFamily"/> = guilde au plus
    /// haut score, 0 si aucune).</summary>
    public record struct TickEvent(int CrusadeId, int NewPhase,
        int WinnerFamily);

    /// <summary>Tick du cycle — checkin → bataille, bataille →
    /// clôture ; retourne les transitions à annoncer.</summary>
    public List<TickEvent> Tick()
    {
        var events = new List<TickEvent>();
        var now = DateTime.UtcNow;
        foreach (var b in _battles.Values.ToList())
        {
            if (now < b.PhaseEndsAt) continue;
            if (b.Phase == PhaseCheckin)
            {
                b.Phase = PhaseBattle;
                b.PhaseEndsAt = now + BattleDuration;
                events.Add(new(b.CrusadeId, PhaseBattle, 0));
                EdenLog.Write($"[Crusade] crusade={b.CrusadeId} " +
                    $"→ BATTLE ({b.Members.Count} inscrits)");
            }
            else
            {
                var winner = b.Scores.Count == 0 ? 0
                    : b.Scores.MaxBy(kv => kv.Value).Key;
                if (winner > 0) _owners[b.CrusadeId] = winner;
                events.Add(new(b.CrusadeId, 0, winner));
                _battles.Remove(b.CrusadeId);
                EdenLog.Write($"[Crusade] crusade={b.CrusadeId} " +
                    $"close — winner family={winner} " +
                    $"scores={string.Join(',', b.Scores.Select(
                        kv => $"{kv.Key}:{kv.Value}"))}");
            }
        }
        return events;
    }

    /// <summary>Force la transition checkin → bataille (chemin admin
    /// équivalent au TC_BattleCrusadeStart natif) — même effet que le
    /// tick à échéance. false si pas en checkin.</summary>
    public bool ForceBattle(int crusadeId)
    {
        var b = Get(crusadeId);
        if (b == null || b.Phase != PhaseCheckin) return false;
        b.Phase = PhaseBattle;
        b.PhaseEndsAt = DateTime.UtcNow + BattleDuration;
        EdenLog.Write($"[Crusade] crusade={b.CrusadeId} " +
            $"→ BATTLE forcée ({b.Members.Count} inscrits)");
        return true;
    }

    /// <summary>Clôture forcée (TC_BattleCrusadeClose natif).</summary>
    public (int CrusadeId, int WinnerFamily)? Close(int crusadeId)
    {
        var b = Get(crusadeId);
        if (b == null) return null;
        var winner = b.Scores.Count == 0 ? 0
            : b.Scores.MaxBy(kv => kv.Value).Key;
        if (winner > 0) _owners[crusadeId] = winner;
        _battles.Remove(crusadeId);
        return (crusadeId, winner);
    }
}
