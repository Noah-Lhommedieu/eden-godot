namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/T_Class.ini</c> — textes anglais des classes (écran de
/// création de personnage). Format Count : <c>6|</c> puis index
/// <c>1|2|5|6|7|8|</c>, rows <c>id|nom|desc courte|trait|desc
/// détaillée|prérequis|</c> — le champ prérequis contient des CRLF
/// (« Class Requirement:\nBeginning Job ») gérés par IniTable.
///
/// Spécification serveur : <c>ClassDataParser</c>
/// (<c>$EDEN/server/.../Parsers/ClassDataParser.cs</c>) — même fichier,
/// même format. La résolution nom→id des prérequis du parser serveur
/// n'est PAS portée (le client affiche le texte brut).
/// </summary>
public static class ClassTextTable
{
    /// <summary>Textes d'une classe depuis T_Class.ini.</summary>
    public sealed class ClassText
    {
        /// <summary>Col 0 — id de classe (1-60 ; les races déclinent
        /// en 100+/200+/300+/400+ comme C_Class).</summary>
        public int Id { get; init; }
        /// <summary>Col 1 — nom anglais (« Warrior »).</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Col 2 — description courte.</summary>
        public string ShortDescription { get; init; } = string.Empty;
        /// <summary>Col 3 — nom du trait (« Bravery »).</summary>
        public string Trait { get; init; } = string.Empty;
        /// <summary>Col 4 — description détaillée.</summary>
        public string DetailedDescription { get; init; } = string.Empty;
        /// <summary>Col 5 — prérequis brut (multi-lignes, ex.
        /// « Class Requirement:\n* Character Level 40 »).</summary>
        public string RequirementText { get; init; } = string.Empty;
        /// <summary>Niveau de personnage minimal extrait de
        /// « * Character Level N » (1 si absent — même défaut que le
        /// parser serveur).</summary>
        public int MinCharacterLevel { get; init; } = 1;
    }

    /// <summary>Charge T_Class.ini → id → textes.</summary>
    public static Dictionary<int, ClassText> Load(string path)
    {
        var table = IniTable.ParseFile(path);
        var dict = new Dictionary<int, ClassText>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 2 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new ClassText
            {
                Id = id,
                Name = IniTable.GetStr(row, 1).Trim(),
                ShortDescription = IniTable.GetStr(row, 2).Trim(),
                Trait = IniTable.GetStr(row, 3).Trim(),
                DetailedDescription = IniTable.GetStr(row, 4).Trim(),
                RequirementText = IniTable.GetStr(row, 5).Trim(),
                MinCharacterLevel = ExtractCharLevel(IniTable.GetStr(row, 5)),
            };
        }
        return dict;
    }

    /// <summary>Extrait « * Character Level N » du texte de prérequis
    /// (regex du parser serveur, allégée : premier entier après le
    /// marqueur).</summary>
    private static int ExtractCharLevel(string req)
    {
        const string marker = "Character Level";
        var idx = req.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 1;
        var i = idx + marker.Length;
        while (i < req.Length && !char.IsDigit(req[i])) i++;
        var j = i;
        while (j < req.Length && char.IsDigit(req[j])) j++;
        return j > i && int.TryParse(req[i..j], out var lv) ? lv : 1;
    }
}
