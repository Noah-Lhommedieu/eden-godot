namespace EdenClient.Fx;

/// <summary>Catalogue des effets : <c>effect.ini</c> (clé, nif, échelle) et <c>dyneffect.ini</c>
/// (clé dynamique « E… » -> clé effet). Pur : prend le texte des tables. Fichiers fr-full :
/// data/db/effect.ini (DBn=clé,nif|NULL,NULL,0,échelle,…), data/db/dyneffect.ini (DBn=clé,wav,clé effet).</summary>
public sealed class FxCatalog
{
    public readonly record struct Entry(string Key, string Nif, float Scale);

    /// <summary>Colonnes (0-based) des tables qui désignent un effet, constatées sur fr-full (2026-11-02) :
    /// clé « E… » (dyneffect) ou « s… » (effect), selon la colonne.</summary>
    public static readonly (string Table, int Column, string Role)[] ReferenceColumns =
    [
        ("c_spell.ini", 4, "effet de cast/animation (dyn)"), ("c_spell.ini", 5, "effet de lancement (dyn)"),
        ("c_spell.ini", 6, "effet 6 (effect)"), ("c_spell.ini", 7, "effet 7 (effect)"),
        ("c_spell.ini", 10, "effet d'impact (effect)"), ("c_spell.ini", 12, "effet 12 (effect)"),
        ("c_enchant.ini", 3, "lueur d'enchantement (effect)"), ("c_enchant.ini", 1, "effet (dyn)"),
        ("c_item.ini", 5, "effet d'objet (effect/dyn)"), ("c_itemmall.ini", 5, "effet d'objet boutique"),
        ("c_itemmallenchant.ini", 3, "lueur boutique"), ("c_equipset.ini", 2, "effet de set"),
    ];

    readonly Dictionary<string, Entry> _effects = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _dyn = new(StringComparer.OrdinalIgnoreCase);

    public int EffectCount => _effects.Count;
    public int DynCount => _dyn.Count;

    static IEnumerable<string[]> DbRows(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int eq = line.IndexOf('=');
            if (eq < 0 || !line.StartsWith("DB", StringComparison.Ordinal)) continue;
            yield return line[(eq + 1)..].Split(',');
        }
    }

    public static FxCatalog Parse(string effectIni, string dynEffectIni)
    {
        var c = new FxCatalog();
        foreach (var f in DbRows(effectIni))
        {
            if (f.Length < 5) continue;
            float sc = float.TryParse(f[4], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 1f;
            string nif = f[1].Equals("NULL", StringComparison.OrdinalIgnoreCase) ? f[0] : Stem(f[1]);
            c._effects[f[0].Trim()] = new Entry(f[0].Trim(), nif.Trim().ToLowerInvariant(), sc);
        }
        foreach (var f in DbRows(dynEffectIni))
            if (f.Length >= 3) c._dyn[f[0].Trim()] = f[2].Trim();
        return c;
    }

    static string Stem(string nif)
    {
        int d = nif.LastIndexOf('.');
        return d > 0 ? nif[..d] : nif;
    }

    /// <summary>Clé de table (« E10121 », « s10141 », « NULL ») -> effet, ou null. Une clé dyn passe par
    /// dyneffect.ini puis effect.ini ; une clé absente des deux n'est PAS devinée (pas de repli).</summary>
    public Entry? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        if (key.Equals("null", StringComparison.OrdinalIgnoreCase) || key == "0" || key.Length < 2) return null;
        if (_dyn.TryGetValue(key, out var ek)) key = ek;
        return _effects.TryGetValue(key, out var e) ? e : null;
    }

    /// <summary>La clé existe dans effect.ini ou dyneffect.ini.</summary>
    public bool Knows(string? key) => key != null && (_dyn.ContainsKey(key.Trim()) || _effects.ContainsKey(key.Trim()));

    /// <summary>Chemin relatif (fr-full) du nif d'une entrée.</summary>
    /// Le moteur d'origine est insensible à la casse (« S10141.nif » pour la clé s10141) : résoudre sans casse.
    public static string NifPath(Entry e) => "effect/model/" + e.Nif + ".nif";
}
