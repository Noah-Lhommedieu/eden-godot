using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EdenClient.Ui;

/// <summary>Une entrée <c>&lt;FalagardMapping&gt;</c> du scheme :
/// associe le type écrit dans les layouts (<see cref="WindowType"/>,
/// ex. <c>DejaVu/ImageButton</c>) au type CEGUI réel
/// (<see cref="TargetType"/>, ex. <c>CEGUI/PushButton</c>), au
/// renderer Falagard et au <c>WidgetLook</c> du looknfeel.</summary>
public sealed class FalagardMapping
{
    public string WindowType { get; init; } = "";
    public string TargetType { get; init; } = "";
    public string Renderer { get; init; } = "";
    public string LookNFeel { get; init; } = "";
}

/// <summary>
/// Parseur de <c>uidata/schemes/DejaVu.scheme</c>. Retient les
/// références d'imagesets/fonts/looknfeel et surtout la table
/// <see cref="Mappings"/> — tous les <c>FalagardMapping</c> sont
/// chargés, pas seulement <c>DejaVu/*</c> (certains layouts utilisent
/// <c>DefaultWindow</c> directement, sans mapping).
/// </summary>
public sealed class CeguiScheme
{
    public string Name { get; private set; } = "";

    /// <summary>Imagesets déclarés : Name → Filename.</summary>
    public Dictionary<string, string> Imagesets { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fonts déclarées : Name → Filename.</summary>
    public Dictionary<string, string> Fonts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? LookNFeelFile { get; private set; }
    public string? WindowRendererSet { get; private set; }

    public List<FalagardMapping> Mappings { get; } = new();

    /// <summary>Résout un <c>Window/@Type</c> de layout. Retourne
    /// <c>null</c> pour les types non mappés (ex.
    /// <c>DefaultWindow</c>, types custom).</summary>
    public FalagardMapping? Resolve(string? windowType)
    {
        if (windowType == null)
            return null;
        foreach (var m in Mappings)
            if (string.Equals(m.WindowType, windowType, StringComparison.OrdinalIgnoreCase))
                return m;
        return null;
    }

    public static CeguiScheme Parse(string path)
    {
        using var s = Vfs.EdenVfs.OpenRead(path)
            ?? throw new FileNotFoundException(path);
        var doc = XDocument.Load(s);
        var root = doc.Root ?? throw new InvalidDataException("scheme sans racine");
        var scheme = new CeguiScheme { Name = root.Attribute("Name")?.Value ?? "" };
        foreach (var el in root.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "Imageset":
                    if (el.Attribute("Name") is { } n)
                        scheme.Imagesets[n.Value] = el.Attribute("Filename")?.Value ?? "";
                    break;
                case "Font":
                    if (el.Attribute("Name") is { } f)
                        scheme.Fonts[f.Value] = el.Attribute("Filename")?.Value ?? "";
                    break;
                case "LookNFeel":
                    scheme.LookNFeelFile = el.Attribute("Filename")?.Value;
                    break;
                case "WindowRendererSet":
                    scheme.WindowRendererSet = el.Attribute("Filename")?.Value;
                    break;
                case "FalagardMapping":
                    scheme.Mappings.Add(new FalagardMapping
                    {
                        WindowType = el.Attribute("WindowType")?.Value ?? "",
                        TargetType = el.Attribute("TargetType")?.Value ?? "",
                        Renderer = el.Attribute("Renderer")?.Value ?? "",
                        LookNFeel = el.Attribute("LookNFeel")?.Value ?? "",
                    });
                    break;
            }
        }
        return scheme;
    }
}

/// <summary>
/// Table des polices CEGUI <c>uidata/fonts/*.font</c> : chaque fichier
/// est un XML d'une ligne, ex.
/// <c>&lt;Font Name="F001-9" Filename="FONT01.TTF" Size="9" AutoScaled="false"/&gt;</c>.
/// <see cref="GetSize"/> sert à approximer <c>font_size</c> Godot.
/// Les <c>*.font.kr</c> (variantes coréennes) sont ignorées si le nom
/// existe déjà.
/// </summary>
public sealed class CeguiFontTable
{
    public sealed record Entry(string Name, string? File, int Size, bool AutoScaled);

    private readonly Dictionary<string, Entry> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public void LoadDirectory(string dir)
    {
        foreach (var file in Vfs.EdenVfs.ListDir(dir))
        {
            if (!file.EndsWith(".font",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                using var fs = Vfs.EdenVfs.OpenRead(file);
                var el = fs == null ? null : XDocument.Load(fs).Root;
                if (el?.Attribute("Name") is not { } n)
                    continue;
                int size = 0;
                if (el.Attribute("Size") is { } s)
                    int.TryParse(s.Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out size);
                _byName.TryAdd(n.Value, new Entry(
                    n.Value,
                    el.Attribute("Filename")?.Value,
                    size,
                    string.Equals(el.Attribute("AutoScaled")?.Value, "true",
                        StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception)
            {
                // Fichier .font malformé : ignoré, la heuristique de
                // nom prend le relais dans GetSize.
            }
        }
    }

    public void Add(Entry e) => _byName[e.Name] = e;

    /// <summary>Taille en points de la police nommée. Fallback :
    /// dernier entier du nom (<c>F001-12</c> → 12,
    /// <c>F001-scale</c> → <c>null</c>, pas d'override).</summary>
    public int? GetSize(string? fontName)
    {
        if (fontName == null)
            return null;
        if (_byName.TryGetValue(fontName, out var e) && e.Size > 0)
            return e.Size;
        var m = Regex.Match(fontName, @"(\d+)$");
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) && v > 0 ? v : null;
    }

    /// <summary>Fichier TTF associé à la police (ex.
    /// <c>FONT01.TTF</c>), utilisé par la démo pour charger la fonte.</summary>
    public string? GetFile(string? fontName) =>
        fontName != null && _byName.TryGetValue(fontName, out var e) ? e.File : null;
}
