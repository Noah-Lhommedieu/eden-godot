namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/C_EquipType.ini</c> — types d'équipement (format comma
/// <c>V.1,6,</c>) : <c>kind,nom,catégorie,loc,loc2,label,</c>.
///
/// Données réelles : kind 1-6 = pièces d'armure (cat 1), 7-20 = armes
/// et munitions (cat 2) ; <c>loc</c> = emplacement d'équipement natif
/// (−1 = non équipable / dummy). La ligne <c>-1,dummy,…</c> est un
/// placeholder.
///
/// Spécification serveur : <c>GameDataService.ParseEquipLocs</c>
/// (<c>$EDEN/server/.../Services/GameDataService.cs</c>) — « CSV
/// kind,nom,?,loc,?,nom ; loc = champ 3, −1 = non équipable ».
/// Jointure : <see cref="ItemTable.ItemDef.EquipLoc"/> = kind.
/// </summary>
public static class EquipTypeTable
{
    /// <summary>Un type d'équipement depuis C_EquipType.ini.</summary>
    public sealed class EquipTypeDef
    {
        /// <summary>Col 0 — kind (1-6 armure, 7-20 armes/munitions).</summary>
        public int Id { get; init; }
        /// <summary>Col 1 — nom Big5 (ex. 單手劍 = épée à une main).</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Col 2 — catégorie (1 = armure, 2 = arme/munition).</summary>
        public int Category { get; init; }
        /// <summary>Col 3 — emplacement d'équipement (7,2,9,10,13… ;
        /// −1 = non équipable — filtré par le serveur).</summary>
        public int EquipLoc { get; init; }
        /// <summary>Col 4 — second loc (21/22 pour arc/fusil, −1 sinon ;
        /// rôle exact non prouvé — HYPOTHESIS : loc main gauche/munition).</summary>
        public int AltLoc { get; init; }
        /// <summary>Col 5 — label Big5 (duplique le nom sur les données
        /// actuelles).</summary>
        public string Label { get; init; } = string.Empty;
        /// <summary>Équipable (loc > 0 — convention serveur).</summary>
        public bool IsEquipable => EquipLoc > 0;
    }

    /// <summary>Charge C_EquipType.ini → kind → définition. La ligne
    /// dummy (−1) est conservée — les consommateurs filtrent via
    /// <see cref="EquipTypeDef.IsEquipable"/>.</summary>
    public static Dictionary<int, EquipTypeDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, EquipTypeDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 4 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new EquipTypeDef
            {
                Id = id,
                Name = IniTable.GetStr(row, 1),
                Category = IniTable.GetInt(row, 2),
                EquipLoc = IniTable.GetInt(row, 3),
                AltLoc = IniTable.GetInt(row, 4),
                Label = IniTable.GetStr(row, 5),
            };
        }
        return dict;
    }
}
