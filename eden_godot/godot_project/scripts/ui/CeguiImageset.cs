using System.Xml.Linq;

namespace EdenClient.Ui;

/// <summary>
/// Référence à une image d'atlas CEGUI, telle qu'écrite dans les
/// propriétés de layout sous la forme <c>set:DejaVu image:Round</c>
/// (le préfixe <c>set:</c>/<c>image:</c> est obligatoire côté CEGUI).
/// </summary>
public readonly record struct CeguiImageRef(string Set, string Image)
{
    /// <summary>Parse « set:X image:Y ». Retourne <c>null</c> si la
    /// valeur est vide, incomplète (<c>set:</c> seul) ou mal formée.</summary>
    public static CeguiImageRef? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string? set = null, image = null;
        foreach (var tok in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok.StartsWith("set:", StringComparison.OrdinalIgnoreCase))
                set = tok[4..];
            else if (tok.StartsWith("image:", StringComparison.OrdinalIgnoreCase))
                image = tok[6..];
        }
        if (string.IsNullOrEmpty(set) || string.IsNullOrEmpty(image))
            return null;
        return new CeguiImageRef(set, image);
    }

    public override string ToString() => $"set:{Set} image:{Image}";
}

/// <summary>Rectangle d'une image dans l'atlas (pixels).</summary>
public readonly record struct CeguiImageRegion(string Name, int X, int Y, int W, int H);

/// <summary>
/// Parseur des fichiers <c>uidata/imagesets/*.imageset</c> :
/// <c>&lt;Imageset Name Imagefile NativeHorzRes NativeVertRes&gt;</c>
/// suivi de <c>&lt;Image Name XPos YPos Width Height/&gt;</c>.
///
/// Le PNG correspondant est copié hors <c>assets/</c> (qui porte un
/// <c>.gdignore</c>) vers <c>godot_project/ui_textures/imagesets/</c>
/// par <c>tools/textures/copy_ui_png.py</c> — d'où
/// <see cref="TextureResourcePath"/>. La création de
/// <c>AtlasTexture</c> se fait côté Godot dans
/// <c>CeguiToGodot.UiTextureCache</c> (ce fichier reste sans
/// dépendance Godot pour être testable hors moteur).
/// </summary>
public sealed class CeguiImageset
{
    public string Name { get; private set; } = "";
    public string ImageFile { get; private set; } = "";
    public int NativeWidth { get; private set; }
    public int NativeHeight { get; private set; }

    /// <summary>Régions indexées par nom d'image (insensible à la casse).</summary>
    public Dictionary<string, CeguiImageRegion> Regions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Chemin <c>res://</c> du PNG d'atlas copié dans
    /// <c>godot_project/ui_textures/imagesets/</c>.</summary>
    public string TextureResourcePath => $"res://ui_textures/imagesets/{ImageFile}";

    public CeguiImageRegion? Find(string imageName) =>
        Regions.TryGetValue(imageName, out var r) ? r : null;

    public static CeguiImageset Parse(string path)
    {
        using var s = Vfs.EdenVfs.OpenRead(path)
            ?? throw new FileNotFoundException(path);
        var doc = XDocument.Load(s);
        return Parse(doc);
    }

    public static CeguiImageset Parse(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("imageset sans racine");
        var set = new CeguiImageset
        {
            Name = root.Attribute("Name")?.Value ?? "",
            ImageFile = root.Attribute("Imagefile")?.Value ?? "",
            NativeWidth = Int(root.Attribute("NativeHorzRes")?.Value),
            NativeHeight = Int(root.Attribute("NativeVertRes")?.Value),
        };
        foreach (var img in root.Elements("Image"))
        {
            var name = img.Attribute("Name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;
            set.Regions[name] = new CeguiImageRegion(
                name,
                Int(img.Attribute("XPos")?.Value),
                Int(img.Attribute("YPos")?.Value),
                Int(img.Attribute("Width")?.Value),
                Int(img.Attribute("Height")?.Value));
        }
        return set;
    }

    private static int Int(string? s) =>
        int.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <summary>
/// Cache de tous les <c>.imageset</c> d'un répertoire, indexés par
/// <c>Imageset/@Name</c>. Les layouts référencent des sets absents du
/// <c>.scheme</c> (ex. <c>BigWorldSet</c>, <c>CDAni</c>) — on charge
/// donc tout le dossier plutôt que la liste du scheme. Les variantes
/// de thème <c>imagesets/&lt;Theme&gt;/</c> sont ignorées (MVP bleu
/// par défaut).
/// </summary>
public sealed class CeguiImagesetCache
{
    private readonly Dictionary<string, CeguiImageset> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<CeguiImageset> All => _byName.Values;

    /// <summary>Charge tous les <c>*.imageset</c> du niveau courant de
    /// <paramref name="dir"/> (sans récursion dans les thèmes —
    /// <see cref="Vfs.EdenVfs.ListDir"/> non récursif).</summary>
    public void LoadDirectory(string dir)
    {
        foreach (var file in Vfs.EdenVfs.ListDir(dir))
        {
            if (!file.EndsWith(".imageset",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var set = CeguiImageset.Parse(file);
                _byName.TryAdd(set.Name, set);
            }
            catch (Exception)
            {
                // Un imageset malformé ne doit pas bloquer le reste —
                // l'erreur est simplement ignorée (compteur dans le
                // rapport du builder si besoin).
            }
        }
    }

    public void Add(CeguiImageset set) => _byName[set.Name] = set;

    public CeguiImageset? Get(string? setName) =>
        setName != null && _byName.TryGetValue(setName, out var s) ? s : null;

    /// <summary>Résout <c>set:X image:Y</c> → région d'atlas.</summary>
    public CeguiImageRegion? Resolve(CeguiImageRef? r) =>
        r is { } v ? Get(v.Set)?.Find(v.Image) : null;
}
