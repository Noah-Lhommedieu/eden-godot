namespace EdenClient.Data.Tables;

/// <summary>
/// Définitions des titres (appellations) — <c>data/db/T_Title.ini</c>
/// (format Count 5| : noms anglais Reforged) avec repli
/// <c>data/db/c_title.ini</c> (<c>|V.3|6|</c>, Big5).
///
/// T_Title.ini (index « 1|2|3|5|6 » → colonnes c_title 1,2,3,5,6) :
///   id | nom | catégorie | « bonus\r\ndescription » | obtention
/// La cellule 3 porte le bonus court (« AGI +5 ») en première ligne,
/// suivi du texte descriptif. c_title.ini ajoute col4 = id d'enchant
/// (bonus appliqué tant que le titre est actif — cf. serveur
/// ZoneEntryFlow.Titles.cs).
/// </summary>
public static class TitleTable
{
    /// <summary>Un titre joueur.</summary>
    public sealed class TitleDef
    {
        public int Id { get; init; }
        /// <summary>Nom affiché (EN si T_Title présente, sinon Big5).</summary>
        public string Name { get; init; } = "";
        /// <summary>Catégorie (« General Title »…).</summary>
        public string Category { get; init; } = "";
        /// <summary>Bonus court (« AGI +5 ») — 1re ligne de la cellule
        /// texte du titre.</summary>
        public string Bonus { get; init; } = "";
        /// <summary>Texte descriptif (lignes suivantes de la cellule).</summary>
        public string Description { get; init; } = "";
        /// <summary>Condition d'obtention (« Obtained by… »).</summary>
        public string Obtain { get; init; } = "";
        /// <summary>Id d'enchant (c_title col4) — bonus du titre actif.</summary>
        public int EnchantId { get; init; }
    }

    /// <summary>Charge T_Title.ini (EN prioritaire) complétée par
    /// c_title.ini (Big5 — noms/catégories manquants, enchant id).
    /// <paramref name="cPath"/> peut être null (T_Title seule).</summary>
    public static Dictionary<int, TitleDef> Load(string tPath,
        string? cPath)
    {
        var dict = new Dictionary<int, TitleDef>();
        // La T_ anglaise prime : on la charge en premier, puis on
        // complète par le Big5 et l'enchant id de c_title.
        var table = IniTable.ParseFile(tPath);
        foreach (var row in table.Rows)
        {
            if (row.Length < 4 || !int.TryParse(row[0], out var id))
                continue;
            var cell3 = IniTable.GetStr(row, 3);
            var nl = cell3.IndexOf('\n');
            dict[id] = new TitleDef
            {
                Id = id,
                Name = TextResolver.Clean(IniTable.GetStr(row, 1)),
                Category = TextResolver.Clean(IniTable.GetStr(row, 2)),
                Bonus = TextResolver.Clean(
                    nl < 0 ? cell3 : cell3[..nl]),
                Description = TextResolver.Clean(
                    nl < 0 ? "" : cell3[(nl + 1)..]),
                Obtain = TextResolver.Clean(IniTable.GetStr(row, 4)),
            };
        }
        if (cPath == null) return dict;
        var cTable = IniTable.ParseFile(cPath);
        foreach (var row in cTable.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id))
                continue;
            if (dict.TryGetValue(id, out var t))
            {
                dict[id] = new TitleDef
                {
                    Id = id,
                    Name = t.Name.Length > 0
                        ? t.Name : IniTable.GetStr(row, 1),
                    Category = t.Category.Length > 0
                        ? t.Category : IniTable.GetStr(row, 2),
                    Bonus = t.Bonus,
                    Description = t.Description,
                    Obtain = t.Obtain,
                    EnchantId = IniTable.GetInt(row, 3),
                };
            }
            else
            {
                dict[id] = new TitleDef
                {
                    Id = id,
                    Name = IniTable.GetStr(row, 1),
                    Category = IniTable.GetStr(row, 2),
                    EnchantId = IniTable.GetInt(row, 3),
                    Bonus = IniTable.GetStr(row, 4),
                    Description = IniTable.GetStr(row, 5),
                };
            }
        }
        return dict;
    }
}
