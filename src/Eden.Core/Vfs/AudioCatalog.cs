using System.Globalization;

namespace EdenClient.Vfs;

/// <summary>Catalogue audio EDENFR (pur) : musique de zone (colonnes
/// 23/24 de <c>c_node.ini</c>), sons d'animation (<c>motion.ini</c>,
/// colonne « Key Sound Name »), sons d'interface (<c>sound/UI*.wav</c>).
/// Les formats restent natifs Flax : bgm <c>.ogg</c>, sfx <c>.wav</c>.</summary>
public static class AudioCatalog
{
    /// <summary>Nom de bgm (« bgm012 ») d'un node ou d'une scène, null si aucun.</summary>
    public static string? BgmForNode(IEnumerable<string> cNodeLines, int nodeId,
        string sceneCode)
    {
        foreach (var line in cNodeLines)
        {
            var f = line.Split('|');
            if (f.Length < 26) continue;
            var isNode = int.TryParse(f[0], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var id) && id == nodeId;
            var isScene = string.Equals(f[4], sceneCode, StringComparison.OrdinalIgnoreCase);
            if (!isNode && !isScene) continue;
            foreach (var v in new[] { f[23], f[24] })
                if (v.StartsWith("bgm", StringComparison.OrdinalIgnoreCase))
                    return v;
            return null;
        }
        return null;
    }

    /// <summary>Id de node depuis un code de carte (« S002 » → 2).</summary>
    public static int NodeIdOf(string mapCode) =>
        int.TryParse(mapCode.TrimStart('s', 'S'), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>Un son déclenché à <see cref="Time"/> s dans une animation.</summary>
    public sealed record MotionSound(float Time, string Sound);

    /// <summary>
    /// <c>motion.ini</c> → sons par nom de motion. Lignes
    /// <c>DBnnnn=sexe,kfId,temps,clé,lien,son.wav,motion</c> ; « NULL »
    /// ou vide = pas de son.
    /// </summary>
    public static Dictionary<string, List<MotionSound>> ParseMotionSounds(
        IEnumerable<string> lines)
    {
        var res = new Dictionary<string, List<MotionSound>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var eq = raw.IndexOf('=');
            if (eq < 0 || !raw.StartsWith("DB", StringComparison.Ordinal)) continue;
            var f = raw[(eq + 1)..].Split(',');
            if (f.Length < 7) continue;
            var snd = f[5].Trim();
            if (snd.Length == 0 || snd.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                continue;
            var motion = f[6].Trim();
            if (!res.TryGetValue(motion, out var l))
                res[motion] = l = new List<MotionSound>();
            if (!l.Exists(x => x.Sound == snd && Math.Abs(x.Time - t) < 1e-4f))
                l.Add(new MotionSound(t, snd));
        }
        foreach (var l in res.Values) l.Sort((a, b) => a.Time.CompareTo(b.Time));
        return res;
    }

    /// <summary>Chemin relatif sans extension : « atka011.wav » → « sound/atka011 ».</summary>
    public static string SoundKey(string file) =>
        "sound/" + Path.GetFileNameWithoutExtension(file);

    /// <summary>Sons d'interface (clic, ouverture, fermeture) — indices
    /// <c>UI000..UI0nn</c> du dossier <c>sound/</c>.</summary>
    public static class Ui
    {
        public const string Click = "UI000", Open = "UI001", Close = "UI002";
    }
}
