using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EdenClient.Data;

/// <summary>Paliers d'EXP (gamedata/levels.tsv, tiré de S_Level.ini :
/// col 1 = EXP pour atteindre le niveau, col 10 = EXP de classe pour
/// atteindre le niveau de classe). Le seuil du niveau L est la ligne
/// L+1, comme <c>ProgressionService.ExpForLevel</c> côté serveur.</summary>
public static class LevelTable
{
    public static Func<string, string?>? ResolveFile;
    private static Dictionary<int, (long Exp, long ClassExp)>? _rows;

    private static Dictionary<int, (long Exp, long ClassExp)> Ensure()
    {
        if (_rows != null) return _rows;
        var d = new Dictionary<int, (long Exp, long ClassExp)>();
        try
        {
            var path = ResolveFile?.Invoke("levels.tsv");
            if (path != null && Vfs.EdenVfs.Exists(path))
                foreach (var line in Vfs.EdenVfs.ReadLines(path))
                {
                    if (line.StartsWith('#')) continue;
                    var c = line.Split('\t');
                    if (c.Length >= 3 &&
                        int.TryParse(c[0], out var lv) &&
                        long.TryParse(c[1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var e) &&
                        long.TryParse(c[2], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var ce))
                        d[lv] = (e, ce);
                }
        }
        catch { /* table absente : ratios inconnus */ }
        return _rows = d;
    }

    /// <summary>Ratio 0..1 EXP perso, null si palier inconnu.</summary>
    public static double? ExpRatio(int level, long exp) =>
        Ensure().TryGetValue(level + 1, out var r) && r.Exp > 0
            ? Math.Clamp((double)exp / r.Exp, 0, 1) : null;

    /// <summary>Ratio 0..1 EXP de classe, null si palier inconnu.</summary>
    public static double? ClassRatio(int classLevel, long exp) =>
        Ensure().TryGetValue(classLevel + 1, out var r) && r.ClassExp > 0
            ? Math.Clamp((double)exp / r.ClassExp, 0, 1) : null;

    /// <summary>Seuil EXP pour passer au niveau suivant (tooltip
    /// natif « EXP: cur / max »), null si palier inconnu.</summary>
    public static long? ExpForNext(int level) =>
        Ensure().TryGetValue(level + 1, out var r) && r.Exp > 0
            ? r.Exp : null;

    /// <summary>Seuil EXP de classe pour le niveau de classe
    /// suivant, null si palier inconnu.</summary>
    public static long? ClassExpForNext(int classLevel) =>
        Ensure().TryGetValue(classLevel + 1, out var r) && r.ClassExp > 0
            ? r.ClassExp : null;
}
