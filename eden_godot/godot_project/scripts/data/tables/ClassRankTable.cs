namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/C_ClassRankBuff.ini</c> (<c>|V.3|134|</c>) — buffs de
/// rang de classe par palier. Miroir du parser serveur
/// <c>ShopAndMiscParsers.ParseClassRankBuffs</c> ($EDEN) :
/// col0 = classId (clé), col1 = classId bis, puis 20 paliers par
/// groupes de 6 champs depuis col2 — seul le 6ᵉ (id enchant) est
/// consommé serveur (RefreshJPAward @0x8417c10 : rang =
/// floor(min(classLv, 100) × 0.2), lookup exact → EffectToLife) ;
/// les 5 autres sont les métadonnées d'affichage client (seuil de
/// palier, score requis…). Utilisé par la fenêtre de changement de
/// classe pour afficher les récompenses de rang.
/// </summary>
public static class ClassRankTable
{
    /// <summary>Les paliers d'une classe (index 1..20 → rang).</summary>
    public sealed class RankDef
    {
        /// <summary>Col 0 — id de ligne = classId.</summary>
        public int ClassId { get; init; }
        /// <summary>Palier → enchant permanent (id col i+5 du
        /// groupe).</summary>
        public List<RankTier> Tiers { get; } = new();
    }

    /// <summary>Un palier de rang (groupe de 6 cellules).</summary>
    public readonly record struct RankTier(int Rank, int Threshold,
        int Score, int EnchantId);

    /// <summary>Charge C_ClassRankBuff.ini → classId → paliers.</summary>
    public static Dictionary<int, RankDef> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, RankDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 8 || !int.TryParse(row[0], out var id))
                continue;
            var def = new RankDef { ClassId = id };
            var rank = 0;
            for (var i = 2; i + 5 < row.Length; i += 6)
            {
                rank++;
                var enchantId = IniTable.GetInt(row, i + 5);
                if (enchantId > 0)
                    def.Tiers.Add(new RankTier(rank,
                        IniTable.GetInt(row, i),       // seuil affiché
                        IniTable.GetInt(row, i + 3),   // score affiché
                        enchantId));
            }
            dict[id] = def;
        }
        return dict;
    }
}
