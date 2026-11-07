namespace EdenClient.Data.Tables;

/// <summary>
/// Événements « pay » (échanges items → récompenses, fenêtre
/// PaySystemWnd) — <c>data/db/c_paysystem.ini</c> (<c>|V.1|18|</c>) :
/// <c>id|type|nom|desc|cond_cmd|4×(itemId,nom,count)|award_cmd|</c>
/// (def natif <c>CPaySystemData</c> : vecteur +0x14 = 4 coûts — même
/// mapping que <c>PaySystemParser</c> serveur).
///
/// Le nom/description EDENFR viennent de <c>t_paysystem.ini</c>
/// (cols 1/2 — <see cref="TextTable"/>), avec repli sur les champs
/// Big5 de c_paysystem. L'award_cmd est une chaîne d'opérations
/// (« #:GiveItem id n:ChooseItem id n… ») — parsée ici juste assez
/// pour l'affichage (items + or + choix).
/// </summary>
public static class PaySystemTable
{
    /// <summary>Un item de coût (id 0 = slot vide — ignoré).</summary>
    public readonly record struct CostItem(int ItemId, int Count);

    /// <summary>Un événement pay.</summary>
    public sealed class Def
    {
        /// <summary>Col 0 — clé envoyée au serveur (253 u32).</summary>
        public int Id { get; init; }
        /// <summary>Col 1 — catégorie : filtre la liste par le shopId
        /// du paquet 602.</summary>
        public int Type { get; init; }
        /// <summary>Col 2 / t_paysystem col1 — titre.</summary>
        public string Name { get; set; } = "";
        /// <summary>Col 3 / t_paysystem col2 — description.</summary>
        public string Desc { get; set; } = "";
        /// <summary>Cols 5..16 — 4 triplets (itemId, nom, count).</summary>
        public CostItem[] Cost { get; init; } = new CostItem[4];
        /// <summary>Col 17 — chaîne de récompenses brute.</summary>
        public string AwardCmd { get; init; } = "";
        /// <summary>GiveItem (id,count) fixes décodés d'award_cmd.</summary>
        public List<(int ItemId, int Count)> GiveItems { get; } = new();
        /// <summary>ChooseItem (id,count) — récompense au choix,
        /// index envoyé en s16 du 253 (-1 si aucun).</summary>
        public List<(int ItemId, int Count)> ChooseItems { get; } = new();
        /// <summary>GiveGold — total des ops or.</summary>
        public int GiveGold { get; set; }
    }

    /// <summary>Charge c_paysystem.ini (+ t_paysystem.ini pour les
    /// textes EDENFR) → liste ordonnée des événements.</summary>
    public static List<Def> Load(string path, string? tPath = null)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        // Textes EDENFR : hook de résolution posé au boot, ou
        // <paramref name="tPath"/> explicite (tests) — pas
        // d'AssetCatalog (GODOT seul).
        tPath ??= TextResolver.ResolveFile
            ?.Invoke("data/db/t_paysystem.ini");
        var texts = tPath != null ? TextTable.Load(tPath)
                                  : new Dictionary<int, TextTable.TextRow>();
        var list = new List<Def>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 18 || !int.TryParse(row[0], out var id))
                continue;
            var def = new Def
            {
                Id = id,
                Type = IniTable.GetInt(row, 1),
                Name = row[2].Trim(),
                Desc = row[3].Trim(),
                AwardCmd = row[17],
            };
            if (texts.TryGetValue(id, out var tr))
            {
                var n = tr.Field(1).Trim();
                var d = tr.Field(2).Trim();
                if (n.Length > 0) def.Name = n;
                if (d.Length > 0) def.Desc = d;
            }
            for (var i = 0; i < 4; i++)
            {
                var c = 5 + i * 3;
                def.Cost[i] = new CostItem(
                    IniTable.GetInt(row, c), IniTable.GetInt(row, c + 2));
            }
            ParseAward(def.AwardCmd, def);
            list.Add(def);
        }
        return list;
    }

    /// <summary>Découpe la chaîne award native « #:Op args:Op args »
    /// en listes pour l'affichage — mêmes ops que
    /// <c>PayEventCmdParser</c> côté serveur.</summary>
    private static void ParseAward(string cmd, Def def)
    {
        foreach (var op in cmd.Split(':',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var t = op.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t[0] == "#") t = t.Skip(1).ToArray();
            if (t.Length < 3) continue;
            var id = int.TryParse(t[1], out var v) ? v : 0;
            var n = int.TryParse(t[2], out var c) ? c : 0;
            switch (t[0])
            {
                case "GiveItem": def.GiveItems.Add((id, n)); break;
                case "ChooseItem": def.ChooseItems.Add((id, n)); break;
                case "GiveGold": def.GiveGold += id; break;
            }
        }
    }
}
