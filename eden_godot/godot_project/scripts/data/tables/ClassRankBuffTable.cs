namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/C_ClassRankBuff.ini</c> (<c>|V.3|134|</c>) — bonus
/// d'expertise « Class Ability Bonus » par classe et par palier de 5
/// niveaux de classe. Une ligne = classe (id canonique 1–61, sans race),
/// 20 paliers × 6 champs <c>[PV, PM, ATQ, ATQ M., Soin, enchant]</c>
/// (valeurs = totaux cumulés au palier, pas des incréments). Palier
/// appliqué = <c>floor(min(niveau de classe, 100) × 0.2)</c> (natif
/// RefreshJPAward ; le serveur applique l'enchant du 6ᵉ champ — étape
/// « classrank »). Le client n'en fait que l'affichage (tooltip
/// « Class Ability Bonus Total » + « CE List »).
/// </summary>
public static class ClassRankBuffTable
{
    /// <summary>PV, PM, ATQ, ATQ M., Soin.</summary>
    public const int Stats = 5;

    /// <summary>Résolution du fichier (posée par le client Godot — la
    /// table elle-même ne dépend pas d'AssetCatalog).</summary>
    public static Func<string?>? PathProvider;

    private static Dictionary<int, int[][]>? _rows;
    private static readonly object _lock = new();

    private static Dictionary<int, int[][]> Rows()
    {
        lock (_lock)
        {
            if (_rows != null) return _rows;
            var res = new Dictionary<int, int[][]>();
            try
            {
                var path = PathProvider?.Invoke();
                if (path != null)
                    foreach (var row in IniTable.ParseFile(path,
                                 EdenEncoding.Big5).Rows)
                    {
                        if (row.Length < 8 ||
                            !int.TryParse(row[0], out var id)) continue;
                        var tiers = new List<int[]>();
                        for (var i = 2; i + 5 < row.Length; i += 6)
                        {
                            var t = new int[Stats];
                            for (var s = 0; s < Stats; s++)
                                t[s] = IniTable.GetInt(row, i + s);
                            tiers.Add(t);
                        }
                        res[id] = tiers.ToArray();
                    }
            }
            catch { /* table absente : bonus nuls */ }
            return _rows = res;
        }
    }

    /// <summary>Palier (1..20) pour un niveau de classe (0 = aucun
    /// bonus : niveaux 1–4).</summary>
    public static int Rank(int classLevel) =>
        (int)(Math.Min(classLevel, 100) * 0.2f);

    /// <summary>Bonus [PV, PM, ATQ, ATQ M., Soin] de la classe
    /// <paramref name="classId"/> (race ignorée) au niveau donné.</summary>
    public static int[] Bonus(int classId, int classLevel)
    {
        var res = new int[Stats];
        var rank = Rank(classLevel);
        if (rank <= 0) return res;
        if (Rows().TryGetValue(classId % 100, out var tiers) &&
            rank <= tiers.Length)
            Array.Copy(tiers[rank - 1], res, Stats);
        return res;
    }
}
