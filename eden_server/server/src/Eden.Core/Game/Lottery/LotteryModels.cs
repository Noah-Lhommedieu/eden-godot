namespace Eden.Core.Game.Lottery;

/// <summary>Identifiant d'autel — valeur du s16 @+0 du NCID 129
/// `CNE_CZ_ZoneServerGoLottery` (Execute @0x825d8c0 : autel 0 →
/// GoLottery, 1 → GoInGameLottery, 2 → GoDiamondLottery).</summary>
public enum AltarKind : short
{
    /// <summary>Autel « lottery » (table edenfr_account.lottery / état
    /// edenfr_db.lottery) — fenêtre `CBaseLotteryOp` (type 0x56, MailOpenBox
    /// op 151). Tickets 0x9d31/0x9cef/0xac63, cristaux 0x9d32/0x9d33.</summary>
    Normal = 0,
    /// <summary>Autel « ingame » (edenfr_account.ingame_lottery /
    /// edenfr_db.in_game_lottery) — fenêtre `CInGameLotteryOp` (type 0x54,
    /// MailOpenBox op 150). Tickets 0x66ac/0x66ab/0x927b, cristaux
    /// 0x66ad/0x66ae.</summary>
    InGame = 1,
    /// <summary>Autel « diamond/Eden Gem » (edenfr_account.new_lottery /
    /// edenfr_db.new_lottery) — même fenêtre que Normal (type 0x56).
    /// Tickets 0x6c91/0x6c90/0x927c, cristal 0x6c92.</summary>
    Diamond = 2,
}

/// <summary>Une ligne de table de récompenses — schéma SQL natif
/// `round, week, item_index, item_id, item_amount, probability,
/// bulletin, reset_bonus, cant_gonext` (chargée par les DBO natifs
/// `SELECT … WHERE week = tm_wday ORDER BY round, item_index` —
/// world/strings.txt). `probability` est un POIDS direct : la
/// sélection native cumule dans un arbre rouge-noir puis tire
/// `random() % total` (InGameDiceLottery @0x8117740).</summary>
public sealed class LotteryReward
{
    public int Round { get; init; }
    public int Week { get; init; }
    public int ItemIndex { get; init; }
    public int ItemId { get; init; }
    public int ItemAmount { get; init; }
    /// <summary>Poids direct (colonne `probability`, float).</summary>
    public float Probability { get; init; }
    /// <summary>Entrée +0x14 natif — gagne une annonce
    /// BroadCastScreenMessage type 8 (bulletin).</summary>
    public bool Bulletin { get; init; }
    /// <summary>Entrée +0x15 natif — recopié dans `reset_bonus` du
    /// résultat (449 u8).</summary>
    public bool ResetBonus { get; init; }
    /// <summary>Entrée +0x16 natif — interdit de relancer
    /// (`can_go_next = !cant_gonext`).</summary>
    public bool CantGoNext { get; init; }
}

/// <summary>Table de récompenses d'un autel, organisée
/// `week → round → entrées triées par item_index` (ordre d'insertion
/// de l'arbre natif). Les paliers hebdomadaires sont les lignes
/// `round = 0` (non filtrées par semaine côté natif — SELECT distinct
/// `WHERE round = 0`).</summary>
public sealed class LotteryRewardTable
{
    private readonly Dictionary<int, Dictionary<int, List<LotteryReward>>>
        _weeks = new();
    private readonly List<LotteryReward> _milestones = new();

    public AltarKind Altar { get; }

    public LotteryRewardTable(AltarKind altar,
        IReadOnlyList<LotteryReward> rows)
    {
        Altar = altar;
        foreach (var r in rows)
        {
            if (r.Round == 0) { _milestones.Add(r); continue; }
            if (!_weeks.TryGetValue(r.Week, out var wk))
                _weeks[r.Week] = wk = new();
            if (!wk.TryGetValue(r.Round, out var list))
                wk[r.Round] = list = new();
            list.Add(r);
        }
        _milestones.Sort((a, b) => a.ItemIndex.CompareTo(b.ItemIndex));
        foreach (var wk in _weeks.Values)
            foreach (var l in wk.Values)
                l.Sort((a, b) => a.ItemIndex.CompareTo(b.ItemIndex));
    }

    /// <summary>Palier hebdo (round 0) — `probability` sert de seuil
    /// de points (`ReceiveWeekCountItem` compare point ≥ seuil).</summary>
    public IReadOnlyList<LotteryReward> Milestones => _milestones;

    /// <summary>Entrées du round pour la semaine courante
    /// (`week = tm_wday`), triées par item_index — null si absent
    /// (→ « lose » natif).</summary>
    public List<LotteryReward>? Round(int week, int round)
        => _weeks.TryGetValue(week, out var wk) &&
           wk.TryGetValue(round, out var l) ? l : null;

    /// <summary>Poids cumulés d'un round — l'arbre natif stocke le
    /// cumul et descend `fVar31 = rand % total`. Retourne les bornes
    /// (cumFrom, cumTo) par entrée + le total.</summary>
    public List<(LotteryReward R, float CumTo)> Cumulative(
        int week, int round, out float total)
    {
        total = 0f;
        var list = Round(week, round);
        if (list == null) return new();
        var cum = new List<(LotteryReward, float)>(list.Count);
        foreach (var r in list)
        {
            total += r.Probability;
            cum.Add((r, total));
        }
        return cum;
    }

    /// <summary>Toutes les entrées des rounds 1..N de la semaine —
    /// la liste d'affichage du 511 (`*LotteryItemIds` deques natifs :
    /// tous les item_index de chaque round, triés).</summary>
    public IReadOnlyList<LotteryReward> DisplayEntries(int week)
    {
        if (!_weeks.TryGetValue(week, out var wk))
            return Array.Empty<LotteryReward>();
        var all = new List<LotteryReward>();
        foreach (var round in wk.Keys.OrderBy(k => k))
            all.AddRange(wk[round]);
        return all;
    }

    /// <summary>Round maximum configuré (pour les gardes de stage).</summary>
    public int MaxRound(int week)
        => _weeks.TryGetValue(week, out var wk) && wk.Count > 0
            ? wk.Keys.Max() : 0;
}

/// <summary>État lottery d'un perso — équivalent des blocs natifs
/// CCharacter +0x8e0 (normal) / +0x908 (ingame) / +0x930 (diamond) et
/// des lignes edenfr_db `lottery` / `in_game_lottery` / `new_lottery`.
/// Les champs « input » (item/slot/bag) sont transitoires natifs —
/// jamais persistés (absents des UPDATE DBO) : ils vivent entre le
/// Go et le Result du cast.</summary>
public sealed class PlayerLotteryState
{
    /// <summary>Type de paiement retenu (3 = ticket premium
    /// 0x927b/0xac63/0x927c → items liés ; 1 = autre variante payante
    /// → plus +1/+2 au lieu de +10/+20).</summary>
    public short LotteryType;
    /// <summary>Item gagné en attente de récupération (0 = aucun).</summary>
    public short ItemId;
    /// <summary>Quantité de l'item gagné (`durability` en DB).</summary>
    public short ItemAmount;
    /// <summary>Stage courant (1..7 / 1..5) — indexe le round du
    /// prochain tirage.</summary>
    public short Counts;
    /// <summary>Compteur pitié [500,5000] — décroît de 100/heure
    /// (tick natif), +1/10 en win, +2/20 en lose.</summary>
    public int Plus = 500;
    /// <summary>Timestamp de dernière décroissance de `Plus`
    /// (unix secondes — `lottery_update` natif).</summary>
    public int PlusUpdate;
    /// <summary>`event_flag` natif — toujours 0 dans les flux
    /// implémentés (flag d'événement serveur).</summary>
    public int EventFlag;
    /// <summary>Cristaux « B » (0x66ad/0x9d32/0x6c92) — 10 par
    /// tirage, cap 999.</summary>
    public short CrystalB;
    /// <summary>Cristaux « C » (0x66ae/0x9d33) — normal/ingame
    /// seulement.</summary>
    public short CrystalC;
    /// <summary>Item bonus courant — relancé à chaque nouveau stage
    /// max (`extra_item_id`).</summary>
    public short ExtraItemId;
    /// <summary>Stage max atteint (`extra_counts`) — condition
    /// « counts == extra && item == extra_item » du bonus exact.</summary>
    public short ExtraCounts;
    /// <summary>Multiplicateur du bonus exact (2-5, poids
    /// S_Parameter 590-593).</summary>
    public short ExtraMultiple;
    /// <summary>`reset_bonus` du dernier tirage / flag « bonus
    /// réclamé » posé par ReceiveItem.</summary>
    public bool ResetBonus;
    /// <summary>Le dernier tirage gagné portait `bulletin` — requis par
    /// `CharacterLotteryCounting` (gain hebdo param 670). Transitoire
    /// comme chez le natif : la colonne n'existe pas dans la table
    /// joueur (un relog perd le flag exactement comme le serveur
    /// original).</summary>
    public bool WonBulletin;
    /// <summary>`can_go_next` — vrai sauf si le dernier tirage avait
    /// `cant_gonext`.</summary>
    public bool CanGoNext = true;

    // -- Champs transitoires (non persistés — +0x900/+0x902/+0x904) --
    /// <summary>Id de l'item de paiement choisi (ticket résolu ou
    /// id cristal) — consommé à la fin du cast.</summary>
    public short InputItemId;
    /// <summary>Slot/sac de l'item de paiement (informational).</summary>
    public short InputSlot = -1;
    public short InputBag = -1;

    /// <summary>La ligne existe-t-elle déjà en base ? (native :
    /// INSERT à stage&lt;2, UPDATE sinon).</summary>
    public bool Persisted;
}

/// <summary>Points/paliers hebdomadaires par COMPTE — ligne edenfr_db
/// `player_lottery_point` (SELECT natif `WHERE account_id = %u`,
/// UPDATE/INSERT des DBO). L'autel ingame ne compte pas.</summary>
public sealed class LotteryWeekPoints
{
    /// <summary>Points hebdo autel normal (+0xe00 natif).</summary>
    public int NormalPoint;
    /// <summary>Palier courant autel normal (+0xe04).</summary>
    public int NormalStage;
    /// <summary>Points hebdo autel diamond (+0xe08).</summary>
    public int DiamondPoint;
    /// <summary>Palier courant autel diamond (+0xe0c).</summary>
    public int DiamondStage;
    /// <summary>Ligne déjà présente (INSERT vs UPDATE natif).</summary>
    public bool Persisted;
}
