namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser de <c>S_PaySystem.ini</c> — événements « pay » (échanges
/// d'items de quête contre récompenses, fenêtre PaySystemWnd côté
/// client qui possède sa propre copie T_PaySystem.ini).
/// Mapping <c>GameData::CPaySystemQuery::Deserialize</c> natif
/// @0x08616ff0 : 18 colonnes
/// <c>[u32 id][i32 type][str nom][str desc][str cond "#:"]
/// [4×(i32 item_id, str nom_item, s16 count)][str award "#:"]</c>.
/// Les champs texte contiennent des CRLF embarqués — traités par
/// <see cref="IniTable"/> (records multi-lignes).
/// </summary>
public static class PaySystemParser
{
    /// <summary>Item de coût — <c>CPaySystemData::sPayItems</c>
    /// (12 octets : i32 id @+0, string nom @+4, s16 count @+8).</summary>
    public readonly struct PayCostItem
    {
        public readonly int ItemId;
        public readonly string Name;
        public readonly short Count;
        public PayCostItem(int itemId, string name, short count)
        { ItemId = itemId; Name = name; Count = count; }
    }

    /// <summary>Définition d'un événement pay —
    /// <c>GameData::CPaySystemData</c> (vecteur +0x14 = 4 coûts).</summary>
    public sealed class PayEventDef
    {
        public int Id;                       // col0 : clé de la map native
        public int Type;                     // col1 : catégorie UI client
        public string Name = "";
        public string Desc = "";
        /// <summary>Chaîne de conditions « #:If_CharLevel 79 100:… »
        /// (def+0x10 → ConvertCmd → vecteurs +0x00/+0x0c/+0x18).</summary>
        public string CondCmd = "";
        /// <summary>4 items de coût fixes (def+0x14) — id 0 = slot
        /// vide ignoré par TriggerPayEvent.</summary>
        public PayCostItem[] CostItems = new PayCostItem[4];
        /// <summary>Chaîne de récompenses « #:GiveItem… » (def+0x20 →
        /// ConvertCmd → vecteurs +0x24…+0x78).</summary>
        public string AwardCmd = "";
    }

    /// <summary>Parse le fichier en map id → def (même sémantique que
    /// la map native AllPaySystem / CBaseQuery&lt;uint,CPaySystemData&gt;).
    /// </summary>
    public static Dictionary<int, PayEventDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var map = new Dictionary<int, PayEventDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 18 || !int.TryParse(row[0], out var id))
                continue;
            var def = new PayEventDef
            {
                Id = id,
                Type = IniTable.GetInt(row, 1),
                Name = row[2],
                Desc = row[3],
                CondCmd = row[4],
                AwardCmd = row[17],
            };
            // Coûts : 4 triplets (item_id, nom, count) — Deserialize
            // natif lit i32 + str + s16 à chaque itération.
            for (var i = 0; i < 4; i++)
            {
                var c = 5 + i * 3;
                def.CostItems[i] = new PayCostItem(
                    IniTable.GetInt(row, c), row[c + 1],
                    (short)IniTable.GetInt(row, c + 2));
            }
            map[id] = def;
        }
        return map;
    }
}
