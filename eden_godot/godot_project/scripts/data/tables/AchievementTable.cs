namespace EdenClient.Data.Tables;

/// <summary>
/// Définitions des succès — <c>data/db/c_achievement.ini</c>
/// (<c>|V.4|15|</c>, ~3 900 entrées) croisées avec
/// <c>data/db/T_Achievement.ini</c> (format Count 3| : noms et
/// descriptions anglais, couvre aussi les groupKey 101-150 = noms de
/// sections de quêtes).
///
/// Mapping positionnel identique au parser serveur
/// (<c>GameDataParsers.ParseAchievements</c>) :
///   col0 id, col1 needNum (vide→1), col2 sortIndex, col3 code icône
///   (M0xx → <c>UI/itemicon/&lt;code&gt;.png</c>), col4 nom Big5,
///   col5 type (4 = complétion immédiate au 1er grant ; 5 = exclu des
///   totaux cumulés natifs), col6*100+col7 = groupKey (plages
///   101-108/201-202 → total 480 kind 2 ; 501-506 → kind 3), col8
///   points, col9 seuil de récompense, col10 appellation accordée,
///   col11 item de récompense (mail), col12 align, col13 exp/membres,
///   col14 description.
/// </summary>
public static class AchievementTable
{
    /// <summary>Un succès de c_achievement.ini.</summary>
    public sealed class AchievementDef
    {
        public int Id { get; init; }
        /// <summary>Déclenchements requis pour compléter (col1).</summary>
        public int NeedNum { get; init; } = 1;
        /// <summary>Col2 — tri à l'intérieur de la catégorie.</summary>
        public int SortIndex { get; init; }
        /// <summary>Col3 — code icône (M0xx) → UI/itemicon/.</summary>
        public string Code { get; init; } = "";
        /// <summary>Col4 — nom Big5 (repli si T_Achievement absente).</summary>
        public string Name { get; init; } = "";
        /// <summary>Col5 — type natif (4 = complétion immédiate).</summary>
        public int Type { get; init; }
        /// <summary>col6*100+col7 — clé de groupe (catégorie).</summary>
        public int GroupKey { get; init; }
        /// <summary>Col8 — points de succès à la complétion.</summary>
        public int Point { get; init; }
        /// <summary>Col9 — seuil déclenchant les récompenses.</summary>
        public int RewardThreshold { get; init; }
        /// <summary>Col10 — titre (appellation) accordé en récompense.</summary>
        public int AppellationId { get; init; }
        /// <summary>Col11 — item envoyé par mail de récompense.</summary>
        public int ItemReward { get; init; }
        /// <summary>Col13 — ids cibles (succès membres d'un succès
        /// « groupe » : légendaires = 14 à 205 cibles à 1 déclenchement).
        /// </summary>
        public int[] TargetIds { get; init; } = Array.Empty<int>();
        /// <summary>Col9 relue comme id de titre (T_Title) : sur les
        /// légendaires 5001-5092 c'est l'appellation « Honor Title »
        /// (106 = Legendary Monster Slayer…, 104/109 valeurs sur 109
        /// résolvent dans T_Title) ; le serveur la lit comme seuil.</summary>
        public int TitleCol9 { get; init; }
        /// <summary>Col10 relue comme item de récompense (monture/costume :
        /// 20335, 20051…) — col11/12 = 85/10 constants (prestige).</summary>
        public int ItemCol10 { get; init; }
        /// <summary>Col14 — description Big5.</summary>
        public string Description { get; init; } = "";
    }

    /// <summary>Charge c_achievement.ini → id → définition.</summary>
    public static Dictionary<int, AchievementDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, AchievementDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new AchievementDef
            {
                Id = id,
                NeedNum = Math.Max(1, IniTable.GetInt(row, 1)),
                SortIndex = IniTable.GetInt(row, 2),
                Code = IniTable.GetStr(row, 3),
                Name = IniTable.GetStr(row, 4),
                Type = IniTable.GetInt(row, 5),
                GroupKey = IniTable.GetInt(row, 6) * 100
                    + IniTable.GetInt(row, 7),
                Point = IniTable.GetInt(row, 8),
                RewardThreshold = IniTable.GetInt(row, 9),
                AppellationId = IniTable.GetInt(row, 10),
                ItemReward = IniTable.GetInt(row, 11),
                TargetIds = AchievementProgress.ParseTargets(
                    IniTable.GetStr(row, 13)),
                TitleCol9 = IniTable.GetInt(row, 9),
                ItemCol10 = IniTable.GetInt(row, 10),
                Description = IniTable.GetStr(row, 14),
            };
        }
        return dict;
    }
}
