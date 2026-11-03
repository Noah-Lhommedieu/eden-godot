using System.Globalization;
using System.Text;

namespace EdenClient.Data.Tables;

/// <summary>
/// <c>map/model/Sxxx/Sxxx.ini</c> — paramètres d'environnement d'une
/// map (cycle jour/nuit, couleurs de lumière/brouillard, ciel, bloom,
/// météo). Format ini classique — PAS le format table : sections
/// <c>[Version]</c>/<c>[Scene]</c> (parfois [Weather], [WeatherParticle],
/// [WeatherCloud], [ColorCorrection], [BackLight]) puis lignes
/// <c>key=value</c>, commentaires <c>//…</c> en Big5.
///
/// Constaté sur les données Reforged :
///   - le même nom de section peut apparaître plusieurs fois
///     ([WeatherParticle] ×2 dans s1102 — un par ParticleType) → les
///     sections sont une LISTE ordonnée, pas un dictionnaire ;
///   - les couleurs sont des entiers décimaux 0xRRGGBB (ex.
///     LitColorSunrise=7294804 = 0x6F3FD4) ;
///   - les vecteurs sont '#' -séparés (BloomDay=0.29#0.06#0.35#0.90) ;
///   - les ciels sont des codes (SkyDay=N201).
///
/// Quelques fichiers map/model/*/*.ini sont en fait des scènes
/// (sections [globe_data] etc. — ex. S018/S101.ini) ; ce parser reste
/// générique et les stocke comme sections ordinaires.
/// Pas d'équivalent serveur (tables purement client).
/// </summary>
public sealed class MapEnvIni
{
    /// <summary>Une section [Nom] et ses paires key=value, dans
    /// l'ordre du fichier. Les clés dupliquées dans une même section
    /// écrasent (non observé sur les données).</summary>
    public sealed class Section
    {
        public required string Name { get; init; }
        public Dictionary<string, string> Values { get; } =
            new(StringComparer.Ordinal);
        /// <summary>Valeur de `key` ou null.</summary>
        public string? Get(string key) =>
            Values.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>Sections dans l'ordre du fichier (doublons possibles).</summary>
    public List<Section> Sections { get; } = new();

    /// <summary>Première section du nom donné, ou null.</summary>
    public Section? Find(string name) =>
        Sections.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Toutes les sections du nom donné (doublons météo).</summary>
    public IEnumerable<Section> FindAll(string name) =>
        Sections.Where(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parse un fichier env ini (Big5, commentaires //).</summary>
    public static MapEnvIni Load(string path)
    {
        var env = new MapEnvIni();
        Section? current = null;
        foreach (var rawLine in Vfs.EdenVfs.ReadLines(path,
                     EdenEncoding.Big5))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new Section { Name = line[1..^1] };
                env.Sections.Add(current);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || current == null) continue;
            current.Values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return env;
    }

    // ---------- helpers typés ----------

    /// <summary>Lit un entier (0 si absent/invalide).</summary>
    public static int Int(Section? s, string key) =>
        s != null && s.Values.TryGetValue(key, out var v) &&
        int.TryParse(v, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>Lit un float invariant (0 si absent/invalide).</summary>
    public static float Float(Section? s, string key) =>
        s != null && s.Values.TryGetValue(key, out var v) &&
        float.TryParse(v, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var f) ? f : 0f;

    /// <summary>Couleur décimale 0xRRGGBB → (r,g,b) en 0-255.</summary>
    public static (byte R, byte G, byte B) Color(Section? s, string key)
    {
        var n = Int(s, key);
        return ((byte)(n >> 16), (byte)(n >> 8), (byte)n);
    }

    /// <summary>Vecteur '#' -séparé → floats (vide si absent).</summary>
    public static float[] Vector(Section? s, string key)
    {
        var v = s?.Get(key);
        if (string.IsNullOrEmpty(v)) return Array.Empty<float>();
        var parts = v.Split('#');
        var res = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            float.TryParse(parts[i], NumberStyles.Float,
                CultureInfo.InvariantCulture, out res[i]);
        return res;
    }

    // ---------- vue typée [Scene] ----------

    /// <summary>Phase du cycle jour/nuit — suffixe des clés [Scene].</summary>
    public enum Phase { Sunrise, Day, Sunset, Night }

    /// <summary>Paramètres d'une phase du cycle (lumière, brouillard,
    /// ciel, bloom) lus depuis la section [Scene].</summary>
    public sealed class PhaseEnv
    {
        /// <summary>Heure de début de la phase (HourSunrise…).</summary>
        public int Hour { get; init; }
        /// <summary>Couleur de lumière ambiante (LitColor*).</summary>
        public (byte R, byte G, byte B) LitColor { get; init; }
        /// <summary>Couleur lumière diffuse (LitDifColor*).</summary>
        public (byte R, byte G, byte B) LitDifColor { get; init; }
        /// <summary>Couleur de brouillard (FogColor*).</summary>
        public (byte R, byte G, byte B) FogColor { get; init; }
        /// <summary>Opacité max du brouillard (FogMax*).</summary>
        public float FogMax { get; init; }
        /// <summary>Portée de vue sous brouillard (FogSightRang* —
        /// orthographe native, sans 'e').</summary>
        public float FogSightRange { get; init; }
        /// <summary>Code ciel (Sky* : N102, N201…).</summary>
        public string Sky { get; init; } = string.Empty;
        /// <summary>Vecteur bloom RGBA (Bloom*).</summary>
        public float[] Bloom { get; init; } = Array.Empty<float>();
    }

    /// <summary>Version du fichier ([Version] Version=20100531).</summary>
    public string Version => Find("Version")?.Get("Version") ?? string.Empty;

    /// <summary>Paramètres de la phase `phase` de la section [Scene]
    /// (Sunset s'écrit « Sunset » pour Hour/Fog/Lit mais « SunSet » pour
    /// Sky/Bloom dans les données — les deux casses sont essayées).</summary>
    public PhaseEnv GetPhase(Phase phase)
    {
        var s = Find("Scene");
        var suf = phase.ToString(); // Sunrise, Day, Sunset, Night
        var alt = phase == Phase.Sunset ? "SunSet" : suf;
        string Sky() => s?.Get("Sky" + suf) ?? s?.Get("Sky" + alt) ?? string.Empty;
        float[] Bloom() =>
            Vector(s, "Bloom" + suf) is { Length: > 0 } b ? b
            : Vector(s, "Bloom" + alt);
        return new PhaseEnv
        {
            Hour = Int(s, "Hour" + suf),
            LitColor = Color(s, "LitColor" + suf),
            LitDifColor = Color(s, "LitDifColor" + suf),
            FogColor = Color(s, "FogColor" + suf),
            FogMax = Float(s, "FogMax" + suf),
            FogSightRange = Float(s, "FogSightRang" + suf),
            Sky = Sky(),
            Bloom = Bloom(),
        };
    }
}
