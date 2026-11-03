namespace Eden.Core.Data.Parsers;

/// <summary>Parser de <c>S_RewardScore.ini</c> — définitions des
/// instances d'évaluation « gladiator trial » (nœuds type 18,
/// <c>GameData::CRewardScoreData</c> / <c>CEvaluateList</c> natifs).
/// Ligne : <c>id|nodeTid|bossDefId|challengeCmds|timeCmds|gradeCmds|
/// flag|(itemId|name|count|weight)×20</c> — encodage Big5.
/// Les 3 colonnes de commandes sont des DSL <c>#:cmd:cmd…</c> :
/// <list type="bullet">
/// <item><c>KillMonster &lt;2|3-4|5-6&gt; &lt;count&gt; RewardScore +
/// &lt;score&gt;</c> — seuil cumulé : +score si kills ≥ count
/// (<c>CEvaluateList::GetKillMonScore</c> zone @0x845f9e0+).</item>
/// <item><c>TeamDieNum &lt;count&gt; RewardScore + &lt;score&gt;</c> —
/// bonus survie : +score si morts &lt; count
/// (<c>GetTeamDieBonusScore</c> @0x845fb90).</item>
/// <item><c>PassTime &lt;op&gt; &lt;v1&gt; &lt;op2&gt; &lt;v2&gt;
/// RewardScore + &lt;score&gt;</c> — 1re plage de temps matchée
/// (<c>GetTimeBonusScore</c> @0x845fdd0). Opérateurs natifs :
/// <c>&gt;=</c>→1, <c>&lt;=</c>→2, <c>&gt;</c>→3, <c>&lt;</c>→4.</item>
/// <item><c>TotalScore &lt;op&gt; &lt;v1&gt; &lt;op2&gt; &lt;v2&gt;
/// Evaluate &lt;grade&gt; &lt;opens&gt; &lt;buys&gt; &lt;cost&gt;</c> —
/// 1re plage → grade + paramètres loterie
/// (<c>GetEndSetting</c> @0x8461120).</item>
/// </list>
/// Les plafonds de compteurs = seuil max par bucket
/// (<c>GetKillMon/Elite/Boss/TeamDie</c> = max du vecteur natif).
/// Le tirage des 5 boîtes est pondéré sur la somme des poids ;
/// flag==0 → sans remise (<c>GetRewardItems</c> @0x845ff10).</summary>
public static class RewardScoreParser
{
    /// <summary>Règle de score à seuil (KillMonster / TeamDieNum).</summary>
    public readonly record struct ThresholdRule(int Threshold, int Score);

    /// <summary>Règle de plage (PassTime / TotalScore) — ops natifs
    /// 1=&gt;= 2=&lt;= 3=&gt; 4=&lt;.</summary>
    public readonly record struct RangeRule(int Op1, int V1, int Op2,
        int V2)
    {
        /// <summary>Vrai si <paramref name="value"/> satisfait les
        /// deux comparaisons (sémantique <c>BonusCheck</c> native :
        /// op1 et op2 comparent chacun value à leur opérande — natif
        /// <c>GetTimeBonusScore</c> @0x0845fdd0 : op1 sur V1 ET op2
        /// sur V2, « value op operand » pour les deux).</summary>
        public bool Matches(long value) =>
            Compare(Op1, value, V1) && Compare(Op2, value, V2);

        private static bool Compare(int op, long l, long r) => op switch
        {
            1 => l >= r,
            2 => l <= r,
            3 => l > r,
            4 => l < r,
            _ => false,
        };
    }

    /// <summary>Grade de fin (plage TotalScore → nom + paramètres de
    /// loterie natifs +0xbc/+0xc0/+0xc4 de CEvaluate).</summary>
    public sealed class GradeRule
    {
        public required RangeRule Range { get; init; }
        /// <summary>Nom affiché (« C », « B+ », « S+ »…).</summary>
        public required string Name { get; init; }
        /// <summary>Ouvertures de boîtes offertes (eval+0xbc →
        /// SEVLotteryInfo+0x14).</summary>
        public int Opens { get; init; }
        /// <summary>Achats d'ouvertures supplémentaires
        /// (eval+0xc0 → SEVLotteryInfo+0x18).</summary>
        public int Buys { get; init; }
        /// <summary>Coût unitaire ×1000 or (eval+0xc4, GainGold
        /// −cost*1000 natif).</summary>
        public int Cost { get; init; }
    }

    /// <summary>Item du pool de récompense (record natif 16 B :
    /// i32 itemId, u16 count, u16 ?, i32 chosen).</summary>
    public readonly record struct RewardItem(int ItemId, int Count,
        float Weight);

    /// <summary>Définition d'une instance d'évaluation.</summary>
    public sealed class EvaluateDef
    {
        /// <summary>Id S_RewardScore (1-21).</summary>
        public int Id { get; init; }
        /// <summary>Nœud d'arène S_Node type 18 (655-674).</summary>
        public int NodeTid { get; init; }
        /// <summary>Def id du boss final (S_Monster — sa mort
        /// déclenche End, eval+0x8 natif).</summary>
        public int BossDefId { get; init; }
        /// <summary>Flag colonne 6 — 0 = tirage sans remise
        /// (5 boîtes uniques).</summary>
        public int Flag { get; init; }

        public List<ThresholdRule> MonRules { get; } = new();
        public List<ThresholdRule> EliteRules { get; } = new();
        public List<ThresholdRule> BossRules { get; } = new();
        public List<ThresholdRule> DieRules { get; } = new();
        public List<(RangeRule Range, int Score)> TimeRules { get; } = new();
        public List<GradeRule> Grades { get; } = new();
        public List<RewardItem> RewardItems { get; } = new();

        /// <summary>Plafonds natifs (+0x0c/+0x10/+0x14/+0x18 de
        /// CEvaluate) = seuil max de chaque bucket.</summary>
        public int MaxDeaths => Max(DieRules);
        public int MaxMon => Max(MonRules);
        public int MaxElite => Max(EliteRules);
        public int MaxBoss => Max(BossRules);

        private static int Max(List<ThresholdRule> rules) =>
            rules.Count == 0 ? 0 : rules.Max(r => r.Threshold);

        /// <summary>Σ scores des règles dont le seuil est atteint
        /// (<c>GetKillMonScore</c> : threshold ≤ kills).</summary>
        public int KillScore(List<ThresholdRule> rules, int kills)
        {
            var score = 0;
            foreach (var r in rules)
                if (r.Threshold <= kills) score += r.Score;
            return score;
        }

        /// <summary>Bonus survie (<c>GetTeamDieBonusScore</c> :
        /// deaths &lt; threshold → +score).</summary>
        public int DieBonusScore(int deaths)
        {
            var score = 0;
            foreach (var r in DieRules)
                if (deaths < r.Threshold) score += r.Score;
            return score;
        }

        /// <summary>Bonus temps (<c>GetTimeBonusScore</c> : 1re
        /// plage matchée, 0 sinon).</summary>
        public int TimeBonusScore(int elapsedSeconds)
        {
            foreach (var (range, score) in TimeRules)
                if (range.Matches(elapsedSeconds)) return score;
            return 0;
        }

        /// <summary>Grade de fin (<c>GetEndSetting</c> : 1re plage
        /// TotalScore matchée, null sous la 1re borne).</summary>
        public GradeRule? GetGrade(int totalScore)
        {
            foreach (var g in Grades)
                if (g.Range.Matches(totalScore)) return g;
            return null;
        }

        /// <summary>Tirage pondéré de <paramref name="count"/> boîtes
        /// (<c>GetRewardItems</c> : dé = random % poids total ;
        /// flag==0 → retrait de l'entrée tirée → uniques).</summary>
        public List<RewardItem> DrawRewards(int count, Random rng)
        {
            var pool = new List<RewardItem>(RewardItems);
            var drawn = new List<RewardItem>(count);
            for (var i = 0; i < count && pool.Count > 0; i++)
            {
                var total = pool.Sum(r => r.Weight);
                if (total <= 0f) break;
                // random() % (total*scale) / scale — dé uniforme
                // sur [0,total) comme le modulo natif.
                var dice = (float)(rng.NextDouble() * total);
                var acc = 0f;
                var pick = pool.Count - 1;
                for (var j = 0; j < pool.Count; j++)
                {
                    acc += pool[j].Weight;
                    if (dice < acc) { pick = j; break; }
                }
                drawn.Add(pool[pick]);
                if (Flag == 0) pool.RemoveAt(pick);
            }
            return drawn;
        }
    }

    /// <summary>Parse le fichier — indexé par <b>nodeTid</b> (le
    /// lookup natif <c>CreateEvaluate</c> cherche la
    /// <c>CEvaluateList</c> par node_id).</summary>
    public static Dictionary<int, EvaluateDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var map = new Dictionary<int, EvaluateDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 7 || !int.TryParse(row[0], out var id))
                continue;
            var def = new EvaluateDef
            {
                Id = id,
                NodeTid = IniTable.GetInt(row, 1),
                BossDefId = IniTable.GetInt(row, 2),
                Flag = IniTable.GetInt(row, 6),
            };
            ParseChallengeCmds(row[3], def);
            ParseTimeCmds(row[4], def);
            ParseGradeCmds(row[5], def);
            ParseRewardItems(row, def);
            map[def.NodeTid] = def;
        }
        return map;
    }

    /// <summary>Découpe un champ DSL : <c>#:</c> initial puis
    /// commandes séparées par <c>:</c> (le padding de fin produit
    /// des segments vides, ignorés).</summary>
    private static IEnumerable<string> Cmds(string field)
    {
        var s = field.AsSpan();
        if (s.StartsWith("#:")) s = s[2..];
        else if (s.StartsWith("#")) s = s[1..];
        foreach (var raw in s.ToString().Split(':'))
        {
            var cmd = raw.Trim();
            if (cmd.Length != 0) yield return cmd;
        }
    }

    /// <summary><c>KillMonster &lt;cat&gt; &lt;n&gt; RewardScore +
    /// &lt;s&gt;</c> → bucket mon (cat 2) / elite (3-4) / boss (5-6)
    /// — même mapping que <c>GainKillScore</c> natif ; et
    /// <c>TeamDieNum &lt;n&gt; RewardScore + &lt;s&gt;</c> → DieRules.</summary>
    private static void ParseChallengeCmds(string field, EvaluateDef def)
    {
        foreach (var cmd in Cmds(field))
        {
            var t = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 5) continue;
            var score = SignedScore(t[^1], t[^2]);
            if (t[0] == "KillMonster" && int.TryParse(t[1], out var cat)
                && int.TryParse(t[2], out var n))
            {
                var rule = new ThresholdRule(n, score);
                if (cat == 2) def.MonRules.Add(rule);
                else if (cat is 3 or 4) def.EliteRules.Add(rule);
                else if (cat is 5 or 6) def.BossRules.Add(rule);
            }
            else if (t[0] == "TeamDieNum"
                && int.TryParse(t[1], out var dieN))
                def.DieRules.Add(new ThresholdRule(dieN, score));
        }
    }

    /// <summary><c>PassTime &lt;op&gt; &lt;v1&gt; &lt;op2&gt; &lt;v2&gt;
    /// RewardScore + &lt;s&gt;</c>.</summary>
    private static void ParseTimeCmds(string field, EvaluateDef def)
    {
        foreach (var cmd in Cmds(field))
        {
            var t = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 8 || t[0] != "PassTime") continue;
            def.TimeRules.Add((new RangeRule(Op(t[1]), Int(t[2]),
                Op(t[3]), Int(t[4])), SignedScore(t[^1], t[^2])));
        }
    }

    /// <summary><c>TotalScore &lt;op&gt; &lt;v1&gt; &lt;op2&gt; &lt;v2&gt;
    /// Evaluate &lt;grade&gt; &lt;opens&gt; &lt;buys&gt; &lt;cost&gt;</c>.</summary>
    private static void ParseGradeCmds(string field, EvaluateDef def)
    {
        foreach (var cmd in Cmds(field))
        {
            var t = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 10 || t[0] != "TotalScore") continue;
            def.Grades.Add(new GradeRule
            {
                Range = new RangeRule(Op(t[1]), Int(t[2]), Op(t[3]),
                    Int(t[4])),
                Name = t[6],
                Opens = Int(t[7]),
                Buys = Int(t[8]),
                Cost = Int(t[9]),
            });
        }
    }

    /// <summary>20 tuples <c>(itemId|name|count|weight)</c> à partir
    /// de la colonne 7 — name en Big5 est ignoré (décoratif).</summary>
    private static void ParseRewardItems(string[] row, EvaluateDef def)
    {
        for (var c = 7; c + 3 < row.Length; c += 4)
        {
            if (!int.TryParse(row[c], out var itemId) || itemId <= 0)
                continue;
            var count = IniTable.GetInt(row, c + 2);
            float.TryParse(row[c + 3],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var weight);
            def.RewardItems.Add(new RewardItem(itemId, count, weight));
        }
    }

    private static int Op(string s) => s switch
    {
        ">=" => 1, "<=" => 2, ">" => 3, "<" => 4,
        "1" => 1, "2" => 2, "3" => 3, "4" => 4,
        _ => 0,
    };

    /// <summary>« RewardScore + 482 » / « - 12 » — signe sur le
    /// token précédent la valeur.</summary>
    private static int SignedScore(string value, string sign) =>
        (sign == "-" ? -1 : 1) * Int(value);

    private static int Int(string s) =>
        int.TryParse(s, out var v) ? v : 0;
}
