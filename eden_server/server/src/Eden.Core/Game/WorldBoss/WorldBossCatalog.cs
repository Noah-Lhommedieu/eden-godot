namespace Eden.Core.Game.WorldBoss;

/// <summary>Famille d'un monstre de monde (plan futur-feature/12).</summary>
public enum BossKind
{
    /// <summary>Monstre ordinaire.</summary>
    None = 0,
    /// <summary>World boss fixe 58001-58030 (hors 58015) : ligne
    /// <c>[reborn_monsters]</c> de la scène, respawn natif de la
    /// colonne 4 (7200 s, 3600 s pour Luisaka).</summary>
    WorldBoss = 1,
    /// <summary>« Collectionneur d'Énergie » 60770-60779 (donjons 70-S
    /// S628-640) : lignes dormantes, annonce encodée en scène (col19).</summary>
    EnergyCollector = 2,
    /// <summary>Monstre mutant 60761-60766 : aucune ligne reborn,
    /// apparition dynamique système (<see cref="MutantScheduler"/>).</summary>
    Mutant = 3,
}

/// <summary>Registre statique des monstres de monde. Sources :
/// <c>t_monster.ini</c> 58001-58030 + <c>S002-S055.ini</c>
/// <c>[reborn_monsters]</c> (colonne 1 = id, colonne 4 = respawn),
/// guide FR « monstres-mutants » pour les positions des mutants.
/// 58015 = cristal d'événement « Pouvoir de Destruction » (S501/S502/
/// S776, respawn 21600 s) : PAS un boss. 58021/58023/58026 : ids sans
/// ligne de spawn.</summary>
public static class WorldBossCatalog
{
    /// <summary>World boss fixe : id monstre → node (scène Sxxx) et
    /// respawn natif en secondes (donnée de scène, fait foi).</summary>
    public sealed record FixedBoss(int MobId, int NodeId, int RespawnSeconds);

    /// <summary>Les 26 boss fixes (S025 porte 3 lignes de
    /// Luisaka, S027 3 lignes de Zorna : un seul id/node chacun ici).</summary>
    public static readonly IReadOnlyList<FixedBoss> FixedBosses = new FixedBoss[]
    {
        new(58001, 2, 7200), new(58002, 3, 7200), new(58003, 4, 7200),
        new(58004, 5, 7200), new(58005, 6, 7200), new(58006, 7, 7200),
        new(58007, 8, 7200), new(58008, 9, 7200), new(58009, 10, 7200),
        new(58010, 11, 7200), new(58011, 12, 7200), new(58012, 13, 7200),
        new(58013, 14, 7200), new(58014, 15, 7200), new(58016, 22, 7200),
        new(58017, 24, 7200), new(58018, 25, 3600), new(58019, 27, 7200),
        new(58020, 32, 7200), new(58022, 34, 7200), new(58024, 36, 7200),
        new(58025, 38, 7200), new(58027, 51, 7200), new(58028, 53, 7200),
        new(58029, 54, 7200), new(58030, 55, 7200),
    };

    private static readonly HashSet<int> FixedIds =
        FixedBosses.Select(b => b.MobId).ToHashSet();

    /// <summary>Premier/dernier id « Collectionneur d'Énergie ».</summary>
    public const int EnergyCollectorFirst = 60770, EnergyCollectorLast = 60779;

    /// <summary>Mutants : Posa, Daretta, Fule, Donar, Kinéossa, Huroc
    /// (niveaux guide : 65, 65, 70, 70, 75, 75).</summary>
    public static readonly IReadOnlyList<int> MutantIds =
        new[] { 60761, 60762, 60763, 60764, 60765, 60766 };

    /// <summary>Niveau annoncé par le guide, par mutant (même ordre).</summary>
    public static readonly IReadOnlyList<int> MutantLevels =
        new[] { 65, 65, 70, 70, 75, 75 };

    /// <summary>Famille d'un id monstre.</summary>
    public static BossKind KindOf(int mobId)
    {
        if (FixedIds.Contains(mobId)) return BossKind.WorldBoss;
        if (mobId is >= EnergyCollectorFirst and <= EnergyCollectorLast)
            return BossKind.EnergyCollector;
        if (mobId is >= 60761 and <= 60766) return BossKind.Mutant;
        return BossKind.None;
    }

    /// <summary>Vrai pour un world boss fixe (58001-58030 sauf 58015).</summary>
    public static bool IsWorldBoss(int mobId) => FixedIds.Contains(mobId);

    /// <summary>Ligne de boss fixe, null si l'id n'en est pas un.</summary>
    public static FixedBoss? Find(int mobId) =>
        FixedBosses.FirstOrDefault(b => b.MobId == mobId);

    // ------------------------------------------------------------
    //  Mutants : régions et emplacements (guide FR « monstres mutants »)
    // ------------------------------------------------------------

    /// <summary>Régions du continent central où les mutants peuvent
    /// apparaître, dans l'ordre du guide : Baie Bélouga (S006),
    /// Bourbes (S007), Volcan Avila (S009), Vallée des Rois (S008),
    /// Plaines Dorées (S010), Dunes éternelles (S011). Les nodes sont
    /// ceux des world boss correspondants (Sabaster, Kison, Silin,
    /// Ryloth, Gilz, Kekabu — mêmes noms de région dans le guide).</summary>
    public static readonly IReadOnlyList<int> MutantRegionNodes =
        new[] { 6, 7, 9, 8, 10, 11 };

    /// <summary>Emplacements possibles : [mutant][région] = (X, Y), tels
    /// que listés par le guide (« Emplacements possibles des mutants »).</summary>
    private static readonly (float X, float Y)[][] MutantSpots =
    {
        // Posa
        new[] { (185f, 330f), (175f, 280f), (85f, 155f), (370f, 440f), (398f, 120f), (470f, 470f) },
        // Daretta
        new[] { (512f, 117f), (350f, 290f), (295f, 100f), (250f, 410f), (90f, 390f), (195f, 355f) },
        // Fule
        new[] { (230f, 75f), (60f, 230f), (100f, 380f), (565f, 60f), (230f, 425f), (96f, 76f) },
        // Donar
        new[] { (525f, 417f), (295f, 65f), (435f, 420f), (325f, 303f), (270f, 310f), (250f, 75f) },
        // Kinéossa
        new[] { (250f, 500f), (445f, 465f), (520f, 490f), (75f, 110f), (140f, 265f), (280f, 245f) },
        // Huroc
        new[] { (110f, 215f), (105f, 430f), (230f, 230f), (90f, 500f), (306f, 61f), (500f, 255f) },
    };

    /// <summary>Position d'apparition d'un mutant dans une région
    /// (indice 0-5 dans <see cref="MutantRegionNodes"/>).</summary>
    public static (float X, float Y) MutantSpot(int mutantIndex,
        int regionIndex) => MutantSpots[mutantIndex][regionIndex];

    /// <summary>Quantité de « Broken Red Crystal Shard » (item 38992)
    /// envoyée par courrier à chaque frappeur d'un mutant. Le guide
    /// dit « petits lots, quantité en fonction du niveau du boss » ;
    /// formule retenue (HYPOTHESIS faute de donnée native) :
    /// 1 + (niveau − 65) / 5 → 1 / 1 / 2 / 2 / 3 / 3.</summary>
    public static int MutantShardAmount(int mutantMobId)
    {
        var idx = MutantIdIndex(mutantMobId);
        return idx < 0 ? 0 : 1 + (MutantLevels[idx] - 65) / 5;
    }

    /// <summary>Item remis par courrier (« Broken Red Crystal Shard »,
    /// rendu 5 par 5 pour la quotidienne 2879, <c>ItemCollect 38992 5</c>).</summary>
    public const int MutantShardItemId = 38992;

    /// <summary>Indice 0-5 d'un id mutant, -1 sinon.</summary>
    public static int MutantIdIndex(int mobId)
    {
        for (var i = 0; i < MutantIds.Count; i++)
            if (MutantIds[i] == mobId) return i;
        return -1;
    }

    // ------------------------------------------------------------
    //  Indices texte « extension Eden » (kill broadcast, mutants)
    // ------------------------------------------------------------

    /// <summary>Base des indices <c>CNC_CZ_ClientTextOutputIndex</c>
    /// propres au portage : le client Godot les résout via
    /// <c>UiTextEn</c> (gabarits FR/EN). Hors plage des
    /// <c>t_textindex</c> natifs (max ~10 700), donc sans collision.</summary>
    public const short ExtKillAnnounce = 30001;
    /// <summary>Apparition d'un mutant : arg = id monstre.</summary>
    public const short ExtMutantSpawn = 30002;
    /// <summary>Mort d'un mutant : arg = id monstre.</summary>
    public const short ExtMutantKilled = 30003;
    /// <summary>Compte à rebours d'un sort long de boss : arg = nom du sort.
    /// Réutilise l'index natif 1647 (« %s1 explosera à la fin du compte à
    /// rebours »).</summary>
    public const short NativeCountdownText = 1647;

    /// <summary>Séparateur des arguments multiples dans le texte du 316.</summary>
    public const char ArgSeparator = '\t';

    /// <summary>Statut d'un boss fixe pour le compte à rebours de respawn.</summary>
    public enum BossState { Unloaded, Alive, Dead }

    /// <summary>Ligne de statut (admin / futur panneau).</summary>
    public sealed record StatusRow(int MobId, int NodeId, BossState State,
        DateTime? RespawnAt);

    /// <summary>Construit le statut des 26 boss fixes à partir de
    /// l'état vivant fourni (mobId, vivant, échéance de respawn).
    /// Un boss absent de <paramref name="live"/> (node jamais peuplé)
    /// est <see cref="BossState.Unloaded"/>.</summary>
    public static IReadOnlyList<StatusRow> BuildStatus(
        IEnumerable<(int MobId, bool Alive, DateTime? RespawnAt)> live)
    {
        var byId = new Dictionary<int, (bool Alive, DateTime? At)>();
        foreach (var (id, alive, at) in live)
        {
            // Plusieurs lignes pour un même id (Luisaka ×3, Zorna ×3) :
            // « vivant » dès qu'une instance l'est, sinon l'échéance
            // la plus proche.
            if (!byId.TryGetValue(id, out var cur)) byId[id] = (alive, at);
            else byId[id] = (cur.Alive || alive,
                cur.At is { } a && at is { } b ? (a < b ? a : b) : cur.At ?? at);
        }
        return FixedBosses.Select(b =>
        {
            if (!byId.TryGetValue(b.MobId, out var s))
                return new StatusRow(b.MobId, b.NodeId, BossState.Unloaded, null);
            return s.Alive
                ? new StatusRow(b.MobId, b.NodeId, BossState.Alive, null)
                : new StatusRow(b.MobId, b.NodeId, BossState.Dead, s.At);
        }).ToList();
    }
}
