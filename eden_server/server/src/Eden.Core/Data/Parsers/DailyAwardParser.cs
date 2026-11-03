namespace Eden.Core.Data.Parsers;

/// <summary>Parser de <c>S_DailyAward.ini</c> — récompenses du
/// calendrier de connexion. Layout <c>GameData::CDailyAwardData</c>
/// natif : <c>+0 u32 id(jour)[+4 i32 item_id][+8 s16 amount]
/// [+0xa s16][+0xc i32 required_seconds][+0x10 s16]</c> — ligne
/// ini <c>id|item_id|amount|?|required|?</c> (ex.
/// <c>1|51464|1|1|600|2</c>).</summary>
public static class DailyAwardParser
{
    public sealed class DailyAwardDef
    {
        public int Id;            // index de jour (1..N)
        public int ItemId;        // +0x04 : item récompense
        public int Amount;        // +0x08 : quantité
        public int RequiredSeconds; // +0x0c : temps en ligne requis
    }

    public static Dictionary<int, DailyAwardDef> Parse(
        string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var map = new Dictionary<int, DailyAwardDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id))
                continue;
            map[id] = new DailyAwardDef
            {
                Id = id,
                ItemId = IniTable.GetInt(row, 1),
                Amount = IniTable.GetInt(row, 2),
                RequiredSeconds = IniTable.GetInt(row, 4),
            };
        }
        return map;
    }
}
