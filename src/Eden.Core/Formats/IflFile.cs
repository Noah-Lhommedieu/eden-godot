namespace EdenClient.World;

/// <summary>
/// Parseur des <c>.ifl</c> Gamebryo (Image Frame List) — liste texte
/// CRLF des frames d'une texture animée :
/// <code>
/// caust00.dds
/// caust01.dds
/// R20353.dds 2        ← répétition optionnelle (suffixe « N »)
/// </code>
/// Tolérances constatées sur les 48 .ifl Reforged : quelques lignes
/// portent un chemin Windows absolu en tête (« C:\…\WaterairAni-01.dds »
/// — artefact d'export, on ne garde que le nom de fichier) et des
/// lignes vides. Chaque ligne = 1 frame ; le suffixe entier N est le
/// nombre de répétitions de la frame (Gamebryo ifl spec).
///
/// Classe PURE — testable dans NetTests.
/// </summary>
public sealed class IflFile
{
    /// <summary>Noms de frames dans l'ordre (répétitions expansées) —
    /// noms de fichiers nus (chemins réduits au basename).</summary>
    public List<string> Frames { get; } = new();

    /// <summary>Parse un contenu .ifl.</summary>
    public static IflFile Parse(string content)
    {
        var f = new IflFile();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // « name.ext N » optionnel ; chemins → basename.
            var sp = line.LastIndexOfAny(new[] { ' ', '\t' });
            var repeat = 1;
            var name = line;
            if (sp >= 0 &&
                int.TryParse(line[(sp + 1)..], out var n) && n > 0)
            {
                name = line[..sp].Trim();
                repeat = n;
            }
            var sep = Math.Max(name.LastIndexOf('\\'),
                name.LastIndexOf('/'));
            if (sep >= 0) name = name[(sep + 1)..];
            if (name.Length == 0) continue;
            for (var i = 0; i < repeat; i++)
                f.Frames.Add(name);
        }
        return f;
    }

    /// <summary>Charge un fichier .ifl.</summary>
    public static IflFile Load(string path) =>
        Parse(Vfs.EdenVfs.ReadAllText(path)
              ?? throw new FileNotFoundException(path));
}
