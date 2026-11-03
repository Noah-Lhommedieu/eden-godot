using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;
using Eden.Core.Logging;

namespace Eden.Core.Game.Lottery;

/// <summary>Configuration native d'un autel — constantes extraites
/// des `GoXxxLottery` / `XxxLotteryResult` / `XxxDiceLottery` /
/// `XxxReceiveItem` du zone désassemblé (aucune valeur inventée).</summary>
public sealed class AltarConfig
{
    /// <summary>L'autel décrit par cette config.</summary>
    public required AltarKind Altar;
    /// <summary>Type IsA de l'opération requis par GoLottery
    /// (CBaseLotteryOp=0x56 pour normal+diamond, CInGameLotteryOp=0x54).</summary>
    public required int RequiredOp;
    /// <summary>Champ op du paquet MailOpenBox ouvrant la fenêtre.</summary>
    public required short WindowOp;
    /// <summary>Field1 du 433 ClientStrengthenCasting (0x96/0x97).</summary>
    public required short CastWindow;
    /// <summary>Field3 du 433 (2 pour diamond — flag écrit nativement).</summary>
    public required short CastFlag;
    /// <summary>Ids des tickets acceptés à counts==0, dans l'ordre de
    /// priorité `CheckItemNum` natif (premium type3 en tête).</summary>
    public required int[] TicketIds;
    /// <summary>Le ticket « premium » (type3 → items liés) ou 0.</summary>
    public required int PremiumTicketId;
    /// <summary>Id d'item envoyé par le client pour déclencher le
    /// fallback ticket (0x66ac / 0x9d31 / 0x6c91).</summary>
    public required int TicketRequestId;
    /// <summary>Item-crystal B → stage après consommation de 10
    /// (0x66ad→2, 0x9d32→2, 0x6c92→2 ; 0 si aucun).</summary>
    public required int CrystalBId;
    /// <summary>Item-crystal C → stage après consommation de 10
    /// (0x66ae→4, 0x9d33→4 ; 0 si aucun).</summary>
    public required int CrystalCId;
    /// <summary>Stage plafond (`counts < 7` / `counts < 5`).</summary>
    public required int MaxStage;
    /// <summary>Flag du 449 ClientLotteryEnd émis par ReceiveItem
    /// (0 normal, 1 ingame, 2 diamond).</summary>
    public required short ReceiveFlag;
    /// <summary>Textes d'annonce bulletin (single / ×quantité).</summary>
    public required int TextWinSingle;
    public required int TextWinMulti;
    /// <summary>Textes cristal gagné B / C et cap atteint B / C.</summary>
    public required int TextCrystalB;
    public required int TextCrystalC;
    public required int TextCrystalBCap;
    public required int TextCrystalCCap;
    /// <summary>Comptabilise les points hebdo (normal/diamond) et
    /// index S_Parameter du plafond (671 normal→0, 672 diamond→250 ;
    /// -1 = pas de points).</summary>
    public required int WeekCapParam;
    /// <summary>Colonnes point/stage de `player_lottery_point`
    /// (0 = paire normal, 1 = paire diamond ; -1 = ingame, sans).</summary>
    public required int WeekColumn;
}

/// <summary>Service lottery — port du moteur natif des autels
/// (CPlayerFactory::{Go,Dice,Result,Receive}*Lottery +
/// CharacterLotteryCounting + ReceiveWeekCountItem). Charge les
/// tables edenfr_account au démarrage, fournit le tirage pondéré
/// (cumul des `probability` comme l'arbre natif), la sélection du
/// bonus exact, le gain de points hebdo et la décroissance du
/// « plus » (−100/h, plancher 500 — tick natif sur `lottery_update`).
/// Aucun état de session n'y vit : c'est de la logique pure.</summary>
public sealed class LotteryService
{
    /// <summary>Durée du cast natif (`EffectToLife …,10,0x1000` →
    /// 10 unités — affichée telle quelle dans le 433 ; 1 s).</summary>
    public const int CastUnits = 10;
    /// <summary>Pas de décroissance du plus (3600 s natif, 0xe10).</summary>
    public const int PlusDecayPeriodSeconds = 3600;
    /// <summary>Décroissance par période (100) et bornes [500,5000].</summary>
    public const int PlusDecayAmount = 100;
    public const int PlusMin = 500, PlusMax = 5000;
    /// <summary>Plafond des cristaux (textes natifs « max 999 »).</summary>
    public const int CrystalCap = 999;
    /// <summary>Indices S_Parameter natifs (AllFormulaParas /4) :
    /// poids du multiplicateur bonus 2×/3×/4×/5× (590-593), points
    /// hebdo par cas (667-670), plafonds normal/diamond (671/672).</summary>
    public const int ParamMultWeights = 590;
    public const int ParamWeekNormal = 667, ParamWeekCantGo = 668,
        ParamWeekBonus = 669, ParamWeekBulletin = 670;

    /// <summary>Configs par autel — tickets/cristaux/textes vérifiés
    /// dans le désassemblé (GoXxxLottery) et T_TextIndex.</summary>
    public static readonly AltarConfig Normal = new()
    {
        Altar = AltarKind.Normal,
        RequiredOp = 0x56, WindowOp = 151, CastWindow = 0x97,
        CastFlag = 0,
        TicketIds = new[] { 0xac63, 0x9cef, 0x9d31 },
        PremiumTicketId = 0xac63, TicketRequestId = 0x9d31,
        CrystalBId = 0x9d32, CrystalCId = 0x9d33,
        MaxStage = 7, ReceiveFlag = 0,
        TextWinSingle = 0xa2d, TextWinMulti = 0xa2e,
        TextCrystalB = 0xa38, TextCrystalC = 0xa39,
        TextCrystalBCap = 0xa3a, TextCrystalCCap = 0xa3b,
        WeekCapParam = 671, WeekColumn = 0,
    };
    /// <inheritdoc cref="Normal"/>
    public static readonly AltarConfig InGame = new()
    {
        Altar = AltarKind.InGame,
        RequiredOp = 0x54, WindowOp = 150, CastWindow = 0x96,
        CastFlag = 0,
        TicketIds = new[] { 0x927b, 0x66ab, 0x66ac },
        PremiumTicketId = 0x927b, TicketRequestId = 0x66ac,
        CrystalBId = 0x66ad, CrystalCId = 0x66ae,
        MaxStage = 7, ReceiveFlag = 1,
        TextWinSingle = 0x1c3e, TextWinMulti = 0x1c3f,
        TextCrystalB = 0x1c40, TextCrystalC = 0x1c41,
        TextCrystalBCap = 0x1c42, TextCrystalCCap = 0x1c43,
        WeekCapParam = -1, WeekColumn = -1,
    };
    /// <inheritdoc cref="Normal"/>
    public static readonly AltarConfig Diamond = new()
    {
        Altar = AltarKind.Diamond,
        RequiredOp = 0x56, WindowOp = 151, CastWindow = 0x97,
        CastFlag = 2,
        TicketIds = new[] { 0x927c, 0x6c90, 0x6c91 },
        PremiumTicketId = 0x927c, TicketRequestId = 0x6c91,
        CrystalBId = 0x6c92, CrystalCId = 0,
        MaxStage = 5, ReceiveFlag = 2,
        TextWinSingle = 0x1f54, TextWinMulti = 0x1f55,
        TextCrystalB = 0x1f56, TextCrystalC = 0,
        TextCrystalBCap = 0x1f57, TextCrystalCCap = 0,
        WeekCapParam = 672, WeekColumn = 1,
    };

    private readonly ILotteryRepository _repo;
    private readonly ParameterParser.ParameterSet _parameters;
    private readonly Dictionary<AltarKind, LotteryRewardTable> _tables
        = new();

    public LotteryService(ILotteryRepository repo,
        ParameterParser.ParameterSet parameters)
    {
        _repo = repo;
        _parameters = parameters;
    }

    /// <summary>Charge les 3 tables de récompenses (edenfr_account) —
    /// équivalent des DBO de chargement natifs au démarrage.</summary>
    public async Task LoadAsync()
    {
        foreach (var altar in new[]
                 { AltarKind.Normal, AltarKind.InGame, AltarKind.Diamond })
        {
            var rows = await _repo.LoadRewardsAsync(altar);
            _tables[altar] = new LotteryRewardTable(altar, rows);
            EdenLog.Write(
                $"[Lottery] {altar} : {rows.Count} lignes chargées");
        }
    }

    /// <summary>Semaine courante — `tm_wday` natif (0 = dimanche),
    /// clé du `WHERE week = %hd` des SELECT de tables.</summary>
    public static int CurrentWeek => (int)DateTime.Now.DayOfWeek;

    /// <summary>Table d'un autel — null tant que LoadAsync n'a pas
    /// tourné (le Go natif vérifie `AllXxxLotterysDBO != 0`).</summary>
    public LotteryRewardTable? Table(AltarKind altar)
        => _tables.TryGetValue(altar, out var t) ? t : null;

    /// <summary>Config par autel.</summary>
    public static AltarConfig Config(AltarKind altar) => altar switch
    {
        AltarKind.Normal => Normal,
        AltarKind.InGame => InGame,
        AltarKind.Diamond => Diamond,
        _ => throw new ArgumentOutOfRangeException(nameof(altar)),
    };

    /// <summary>Tirage pondéré natif : `fVar31 = random() % total`
    /// puis descente de l'arbre de cumuls — équivalent exact de
    /// XxxDiceLottery. Retourne l'entrée ou null (round absent = lose).</summary>
    public LotteryReward? Roll(AltarKind altar, int round,
        out float total, Random? rng = null)
    {
        total = 0f;
        var table = Table(altar);
        if (table == null) return null;
        var cum = table.Cumulative(CurrentWeek, round, out total);
        if (cum.Count == 0 || total <= 0f) return null;
        var r = (rng ?? Random.Shared).NextDouble() * total;
        foreach (var (entry, cumTo) in cum)
            if (r < cumTo) return entry;
        return cum[^1].R;
    }

    /// <summary>Relance du bonus exact quand `stage` devient le
    /// nouveau max : `extra_counts = stage`, item uniforme parmi
    /// les entrées du round, multiplicateur pondéré (S_Parameter
    /// 590-593 → 2/3/4/5). Ne fait rien si pas de nouveau record
    /// (bloc natif `+0x924 < +0x90e`).</summary>
    public bool RefreshBonusAt(AltarKind altar, PlayerLotteryState s,
        int stage, Random? rng = null)
    {
        var table = Table(altar);
        if (table == null || s.ExtraCounts >= stage) return false;
        s.ExtraCounts = (short)stage;
        var round = table.Round(CurrentWeek, stage);
        if (round == null || round.Count == 0) return false;
        var rr = rng ?? Random.Shared;
        s.ExtraItemId = (short)round[rr.Next(round.Count)].ItemId;
        s.ExtraMultiple = RollMultiple(rr);
        return true;
    }

    /// <summary>Multiplicateur bonus 2-5 — poids natifs
    /// `AllFormulaParas[590..593]` (somme → rand ; bornes cumulées ;
    /// au-delà → 5). Données : 100/0/0/0 → toujours 2.</summary>
    public short RollMultiple(Random? rng = null)
    {
        var w2 = _parameters.Get(ParamMultWeights);
        var w3 = _parameters.Get(ParamMultWeights + 1);
        var w4 = _parameters.Get(ParamMultWeights + 2);
        var w5 = _parameters.Get(ParamMultWeights + 3);
        var total = (int)(w2 + w3 + w4 + w5);
        var roll = total > 0
            ? (rng ?? Random.Shared).Next(total) : 0;
        if (roll < (int)w2) return 2;
        if (roll < (int)(w2 + w3)) return 3;
        if (roll < (int)(w2 + w3 + w4)) return 4;
        return (short)(roll < total ? 5 : 2);
    }

    /// <summary>Gain de points hebdo `CharacterLotteryCounting`
    /// (@0x80ed890 — appelé dans `*LotteryReceiveItem` au moment du
    /// CLAIM, pas au tirage) : `bulletin` du tirage → P670 ;
    /// bonus exact (`counts==extra_counts && item==extra_item`) →
    /// P669 ; `!can_go_next` → P668 ; sinon P667. L'autel ingame ne
    /// compte pas (pas d'appel natif).</summary>
    public int WeekPointGain(AltarKind altar, PlayerLotteryState s)
    {
        var cfg = Config(altar);
        if (cfg.WeekColumn < 0) return 0;
        float gain;
        if (s.WonBulletin)
            gain = _parameters.Get(ParamWeekBulletin, 1);
        else if (s.Counts == s.ExtraCounts
                 && s.ItemId == s.ExtraItemId && s.ItemId != 0)
            gain = _parameters.Get(ParamWeekBonus, 1);
        else if (!s.CanGoNext)
            gain = _parameters.Get(ParamWeekCantGo, 1);
        else
            gain = _parameters.Get(ParamWeekNormal, 1);
        return (int)gain;
    }

    /// <summary>Plafond hebdo de l'autel (S_Parameter 671/672 ; 0 =
    /// le point reste à 0 comme le natif quand MAX vaut 0 — le code
    /// natif borne `min(old+gain, cap)`).</summary>
    public int WeekCap(AltarKind altar)
    {
        var cfg = Config(altar);
        return cfg.WeekCapParam < 0
            ? 0 : (int)_parameters.Get(cfg.WeekCapParam);
    }

    /// <summary>Décroissance paresseuse du plus — le tick natif
    /// (LotteryUpdate @0x8100400) retire 100 par tranche de 3600 s
    /// écoulée depuis `lottery_update`, plancher 500, puis émet 450.
    /// Timestamp natif recopié fidèlement : `update = now + reste`
    /// (le reste elapsed%3600 est reporté — la période suivante ne
    /// dérive pas). Retourne vrai si quelque chose a bougé (à
    /// persister + notifier).</summary>
    public static bool DecayPlus(PlayerLotteryState s, long nowUnix)
    {
        var elapsed = nowUnix - s.PlusUpdate;
        if (elapsed < PlusDecayPeriodSeconds) return false;
        var periods = (int)(elapsed / PlusDecayPeriodSeconds);
        s.Plus = Math.Max(PlusMin, s.Plus - periods * PlusDecayAmount);
        s.PlusUpdate = (int)(nowUnix + elapsed % PlusDecayPeriodSeconds);
        return true;
    }

    /// <summary>Clamp du plus natif [500,5000].</summary>
    public static int ClampPlus(int plus)
        => Math.Clamp(plus, PlusMin, PlusMax);

    /// <summary>Paliers hebdo — les lignes `round = 0` de
    /// `edenfr_account.new_lottery`, partagées par les autels normal et
    /// diamond (vecteurs `LotteryStageAmounts`/`LotteryCountItem*` et
    /// `DiamondLottery*` du natif, remplis depuis la même table ;
    /// `probability` sert de seuil de points). L'autel ingame n'a
    /// pas de round 0 → liste vide.</summary>
    public IReadOnlyList<LotteryReward> WeekMilestones(AltarKind altar)
        => altar == AltarKind.InGame
            ? Array.Empty<LotteryReward>()
            : Table(AltarKind.Diamond)?.Milestones
              ?? (IReadOnlyList<LotteryReward>)Array.Empty<LotteryReward>();

    /// <summary>Corps du 511 `CNC_CZ_ClientUpdateLottery` — format
    /// variable vérifié dans `::Serialize` @0x813e4a0 (zone décompilé) ;
    /// le wrapper généré n'aplanit que le préfixe :
    /// <code>
    /// [s16 autel][u16 version][u16 0]
    /// [u16 n][n × u32 itemIds]      — pool d'affichage de la semaine
    /// [u16 n][n × s16 amounts]
    /// [u16 n][n × s16 seuils]       — *LotteryStageAmounts (paliers)
    /// [u16 n][n × u32 itemIds]      — *LotteryCountItemIds
    /// [u16 n][n × s16 amounts]      — *LotteryCountItemAmounts
    /// </code>
    /// `version` = la semaine courante : le client redemande la table
    /// quand sa version diffère (comportement InGameLotteryVersionUpdate
    /// natif). Retourne null si la table n'est pas chargée.</summary>
    public byte[]? BuildUpdateBody(AltarKind altar)
    {
        var table = Table(altar);
        if (table == null) return null;
        var display = table.DisplayEntries(CurrentWeek);
        var milestones = WeekMilestones(altar);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)altar);                          // s16 — autel
        w.Write((ushort)CurrentWeek);                   // u16 — version
        w.Write((ushort)0);                             // u16 — 0 natif
        w.Write((ushort)display.Count);                 // u16 — nb items
        foreach (var r in display)
            w.Write((uint)r.ItemId);                    // u32 — item affiché
        w.Write((ushort)display.Count);
        foreach (var r in display)
            w.Write((short)r.ItemAmount);               // s16 — quantité
        w.Write((ushort)milestones.Count);              // u16 — paliers
        foreach (var m in milestones)
            w.Write((short)m.Probability);              // s16 — seuil points
        w.Write((ushort)milestones.Count);
        foreach (var m in milestones)
            w.Write((uint)m.ItemId);                    // u32 — item palier
        w.Write((ushort)milestones.Count);
        foreach (var m in milestones)
            w.Write((short)m.ItemAmount);               // s16 — quantité
        return ms.ToArray();
    }
}
