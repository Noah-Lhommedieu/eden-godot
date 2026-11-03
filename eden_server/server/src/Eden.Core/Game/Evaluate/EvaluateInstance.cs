using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Evaluate;

/// <summary>Instance d'évaluation en cours — équivalent
/// <c>lapis::CEvaluate</c> (zone @0x845bxxx-0x8461xxx). Modèle pur
/// (pas d'I/O) : les mutations produisent des effets que la couche
/// zone traduit en paquets 607/persistence. Tous les offsets natifs
/// sont cités en commentaire.</summary>
public sealed class EvaluateInstance
{
    /// <summary>Boîte de loterie tirée pour un membre — record natif
    /// <c>CRewardScoreData::RewardItem</c> 16 B {i32 itemId, u16
    /// count, u16 ?, i32 chosen}.</summary>
    public sealed class RewardBox
    {
        public int ItemId { get; init; }
        public int Count { get; init; }
        /// <summary>Index choisi (-1 = non ouverte) — natif +0xc.</summary>
        public int Chosen { get; set; } = -1;
    }

    /// <summary><c>SEVLotteryInfo</c> par membre : +0x14 = ouvertures
    /// restantes, +0x18 = achats restants.</summary>
    public sealed class LotteryInfo
    {
        public int Opens { get; set; }
        public int Buys { get; set; }
    }

    /// <summary>Effet produit par <see cref="LotteryOpen"/> —
    /// traduit en paquet 607 type3 / erreur texte / mail par la zone.</summary>
    public readonly record struct LotteryEffect(
        LotteryEffectKind Kind, int Opens, int ItemId, int Count,
        int Index, int ErrorCode);

    public enum LotteryEffectKind
    {
        /// <summary>[3][opens][itemId][count][index] — boîte ouverte.</summary>
        Pick,
        /// <summary>[3][0][0][0][-1] — claim validé, mail envoyé.</summary>
        Finalize,
        /// <summary>[3][0][itemId][count][index] — boîte non ouverte
        /// listée après le -1.</summary>
        Unopened,
        /// <summary>[3][opens][0][0][5|6] — achat validé ; index =
        /// 5 si buys restants, 6 sinon.</summary>
        BuyResult,
        /// <summary>Erreur texte native (0x1fab opens épuisées,
        /// 0x1fac buys épuisés, 0x232 or insuffisant).</summary>
        Error,
        /// <summary>Demande de débit d'or à valider par l'appelant
        /// avant <see cref="CommitBuy"/>.</summary>
        NeedGold,
    }

    /// <summary>evalId (+0x00).</summary>
    public int Id { get; }
    /// <summary>Def S_RewardScore résolue (CEvaluateList* +0xac).</summary>
    public RewardScoreParser.EvaluateDef Def { get; }
    /// <summary>charIds de l'équipe (+0x64) — membres du record
    /// <c>team_evaluate</c>.</summary>
    public List<int> MemberCharIds { get; } = new();
    /// <summary>Statut natif +0x2c : 0 init, 1 run, 2 loterie,
    /// 3 close.</summary>
    public int Status { get; set; }
    /// <summary>Début du run (+0x34).</summary>
    public DateTime StartedAtUtc { get; set; }
    /// <summary>Temps écoulé figé à End (+0x38, secondes).</summary>
    public int ElapsedSeconds { get; set; }

    // Compteurs courants (+0x44..+0x54).
    public int RawScore { get; private set; }
    public int Deaths { get; private set; }
    public int MonKills { get; private set; }
    public int EliteKills { get; private set; }
    public int BossKills { get; private set; }

    // Résultats figés à End (+0x1c..+0x40).
    public int DieBonus { get; private set; }
    public int MonScore { get; private set; }
    public int EliteScore { get; private set; }
    public int BossScore { get; private set; }
    public int TimeBonus { get; private set; }
    public int TotalScore { get; private set; }
    /// <summary>Grade résolu à End (+0xb0..+0xc8).</summary>
    public RewardScoreParser.GradeRule? Grade { get; private set; }

    /// <summary>Boîtes tirées à End par membre (+0x74).</summary>
    public Dictionary<int, List<RewardBox>> Boxes { get; } = new();
    /// <summary>État loterie par membre (+0x98).</summary>
    public Dictionary<int, LotteryInfo> Lottery { get; } = new();
    /// <summary>Items gagnés (boîtes choisies) par membre (+0xd0).</summary>
    public Dictionary<int, List<RewardBox>> Won { get; } = new();

    public EvaluateInstance(int id, RewardScoreParser.EvaluateDef def,
        IEnumerable<int> memberCharIds)
    {
        Id = id;
        Def = def;
        MemberCharIds.AddRange(memberCharIds);
    }

    /// <summary><c>GainKillScore</c> @0x845c1e0 — bucket par catégorie
    /// de kill (2=mon, 3/4=elite, 5/6=boss — S_Monster col 4 1-idx),
    /// cappé au seuil max du DSL ; +0x44 += score brut (S_Monster
    /// col 51 1-idx — le score DoD dédié, pas l'exp). Score 0 →
    /// return natif immédiat : le kill n'incrémente même pas le
    /// compteur.</summary>
    public bool GainKillScore(int killType, int score)
    {
        if (score == 0) return false;
        switch (killType)
        {
            case 2:
                MonKills = Math.Min(MonKills + 1, Def.MaxMon);
                break;
            case 3 or 4:
                EliteKills = Math.Min(EliteKills + 1, Def.MaxElite);
                break;
            case 5 or 6:
                BossKills = Math.Min(BossKills + 1, Def.MaxBoss);
                break;
            default:
                return false;
        }
        RawScore += score;
        return true;
    }

    /// <summary><c>CharacterDie</c> @0x8081f70 — +0x48 cappé +0xc.</summary>
    public void CharacterDie()
        => Deaths = Math.Min(Deaths + 1, Def.MaxDeaths);

    /// <summary><c>CEvaluate::End</c> @0x845d340 — fige le temps,
    /// calcule les scores DSL, le grade, et tire 5 boîtes par membre.
    /// Formule native : total = monScore×2 + dieBonus + eliteScore +
    /// bossScore + rawScore + timeBonus.</summary>
    public void End(int elapsedSeconds, Random rng)
    {
        if (ElapsedSeconds == 0) ElapsedSeconds = elapsedSeconds;
        MonScore = Def.KillScore(Def.MonRules, MonKills);
        EliteScore = Def.KillScore(Def.EliteRules, EliteKills);
        BossScore = Def.KillScore(Def.BossRules, BossKills);
        DieBonus = Def.DieBonusScore(Deaths);
        TimeBonus = Def.TimeBonusScore(ElapsedSeconds);
        TotalScore = MonScore * 2 + DieBonus + EliteScore + BossScore
            + RawScore + TimeBonus;
        Grade = Def.GetGrade(TotalScore);
        // Tirage des boîtes — natif : un jeu de 5 par charactère
        // présent dans le nœud d'équipe ; log ERROR + abort si la
        // def n'en produit pas 5 (pool trop petit en sans-remise).
        foreach (var charId in MemberCharIds)
        {
            if (Boxes.ContainsKey(charId)) continue;
            Boxes[charId] = Def.DrawRewards(5, rng)
                .Select(r => new RewardBox
                    { ItemId = r.ItemId, Count = r.Count })
                .ToList();
        }
    }

    /// <summary><c>SEVLotteryInfo</c> du membre — créée au 1er appel
    /// avec opens=grade.a / buys=grade.b (natif local_1c/local_18).</summary>
    public LotteryInfo LotteryOf(int charId)
    {
        if (!Lottery.TryGetValue(charId, out var info))
            Lottery[charId] = info = new LotteryInfo
            {
                Opens = Grade?.Opens ?? 0,
                Buys = Grade?.Buys ?? 0,
            };
        return info;
    }

    /// <summary><c>CEvaluate::LotteryOpen</c> @0x845dfd0 — valeur 0-4
    /// pick, -1 claim, 5 achat d'ouverture, 7 auto-open. Le débit d'or
    /// est délégué à <paramref name="chargeGold"/> (coût =
    /// grade.cost×1000). Retourne les effets à traduire en paquets.</summary>
    public List<LotteryEffect> LotteryOpen(int charId, int value,
        Func<int, bool> chargeGold)
    {
        var fx = new List<LotteryEffect>();
        var info = LotteryOf(charId);
        if (value is >= 0 and <= 4)
        {
            // opens-- puis clamp — <0 → erreur 0x1fab + restore.
            if (--info.Opens < 0)
            {
                info.Opens = 0;
                fx.Add(new(LotteryEffectKind.Error, 0, 0, 0, 0,
                    0x1fab));
                return fx;
            }
            if (!Boxes.TryGetValue(charId, out var boxes)
                || value >= boxes.Count)
            {
                fx.Add(new(LotteryEffectKind.Error, info.Opens, 0, 0,
                    value, 0));
                return fx;
            }
            var box = boxes[value];
            if (box.Chosen >= 0) return fx;      // déjà ouverte
            box.Chosen = value;
            WonFor(charId).Add(box);
            fx.Add(new(LotteryEffectKind.Pick, info.Opens,
                box.ItemId, box.Count, value, 0));
            return fx;
        }
        switch (value)
        {
            case -1:
                if (info.Opens > 0) return fx;   // reste des ouvertures
                // Items gagnés à mailler — l'appelant envoie le mail
                // (SendRewardMail natif) puis videra la liste.
                fx.Add(new(LotteryEffectKind.Finalize, 0, 0, 0, -1,
                    0));
                if (Boxes.TryGetValue(charId, out var boxes))
                    for (var i = 0; i < boxes.Count; i++)
                        if (boxes[i].Chosen < 0)
                            fx.Add(new(LotteryEffectKind.Unopened, 0,
                                boxes[i].ItemId, boxes[i].Count, i,
                                0));
                return fx;
            case 5:
                if (info.Buys < 1)
                {
                    fx.Add(new(LotteryEffectKind.Error, 0, 0, 0, 0,
                        0x1fac));
                    return fx;
                }
                var cost = Grade?.Cost ?? 0;
                if (cost < 1 || !chargeGold(cost * 1000))
                {
                    fx.Add(new(LotteryEffectKind.Error, info.Opens,
                        0, 0, 0, 0x232));
                    return fx;
                }
                info.Buys--;
                info.Opens++;
                fx.Add(new(LotteryEffectKind.BuyResult, info.Opens,
                    0, 0, info.Buys != 0 ? 5 : 6, 0));
                return fx;
            case 7:
                // Auto-open : ouvre les boîtes restantes dans l'ordre
                // tant que des ouvertures restent (SendAllReward /
                // CharacterLeave natif).
                if (Boxes.TryGetValue(charId, out var auto))
                {
                    var i = 0;
                    while (info.Opens > 0 && i < auto.Count)
                    {
                        info.Opens--;
                        if (auto[i].Chosen < 0)
                        {
                            auto[i].Chosen = i;
                            WonFor(charId).Add(auto[i]);
                        }
                        else info.Opens++;
                        i++;
                    }
                    // Natif : les ouvertures non consommables restent
                    // — la boucle sort quand opens==0 ou fin de liste.
                    if (i >= auto.Count && info.Opens > 0)
                        info.Opens = 0;
                }
                return fx;
            default:
                return fx;
        }
    }

    /// <summary>Items gagnés par le membre (vec +0xd0 natif) —
    /// consommés par SendRewardMail.</summary>
    public List<RewardBox> WonFor(int charId)
    {
        if (!Won.TryGetValue(charId, out var list))
            Won[charId] = list = new List<RewardBox>();
        return list;
    }

    /// <summary><c>SendRewardMail</c> @0x845cd80 — extrait et vide la
    /// liste des items gagnés (natif : vector vidé après envoi).</summary>
    public List<(int ItemId, int Count)> ClaimWonItems(int charId)
    {
        if (!Won.TryGetValue(charId, out var list) || list.Count == 0)
            return new List<(int, int)>();
        var items = list.Where(b => b.ItemId != 0)
            .Select(b => (b.ItemId & 0xffff, b.Count)).ToList();
        list.Clear();
        return items;
    }
}
