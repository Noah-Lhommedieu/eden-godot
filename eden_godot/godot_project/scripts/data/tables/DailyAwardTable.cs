namespace EdenClient.Data.Tables;

/// <summary>
/// Récompenses du calendrier de connexion —
/// <c>data/db/C_DailyAward.ini</c> (<c>|V.1|6|</c>) :
/// ligne <c>jour|itemId|amount|?|requiredSeconds|?</c>
/// (ex. <c>1|51464|1|1|600|2</c>). Jours 1-28 réguliers + 29-32
/// « milestones » (items 49704-49707, cf. rf_game LoginReward :
/// Border2/Frame%d/Block%d = jour sVar2-28).
///
/// Même mapping que le parser serveur
/// (<c>DailyAwardParser</c> — CDailyAwardData natif +0x04 item,
/// +0x08 amount, +0x0c required_seconds).
/// </summary>
public static class DailyAwardTable
{
    /// <summary>Une case du calendrier.</summary>
    public sealed class DailyAwardDef
    {
        /// <summary>Index du jour (1..32).</summary>
        public int Day { get; init; }
        /// <summary>Item récompensé.</summary>
        public int ItemId { get; init; }
        /// <summary>Quantité.</summary>
        public int Amount { get; init; }
        /// <summary>Temps en ligne requis (secondes) pour marquer le
        /// jour complété.</summary>
        public int RequiredSeconds { get; init; }
    }

    /// <summary>Charge C_DailyAward.ini → jour → définition.</summary>
    public static Dictionary<int, DailyAwardDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, DailyAwardDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new DailyAwardDef
            {
                Day = id,
                ItemId = IniTable.GetInt(row, 1),
                Amount = IniTable.GetInt(row, 2),
                RequiredSeconds = IniTable.GetInt(row, 4),
            };
        }
        return dict;
    }
}
