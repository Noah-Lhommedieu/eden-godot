using System.Globalization;
using System.Xml.Linq;

namespace EdenClient.Ui;

/// <summary>
/// Couleur CEGUI = entier <c>AARRGGBB</c> (hexa, alpha en tête — le
/// format natif CEGUI, à convertir en RGBA côté Godot).
/// </summary>
public static class CeguiColour
{
    /// <summary>Parse <c>"AARRGGBB"</c> (8 hex) ou <c>"RRGGBB"</c>
    /// (6 hex, alpha FF implicite). <c>null</c> si invalide.</summary>
    public static uint? ParseArgb(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        if (!uint.TryParse(s, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var v))
            return null;
        return s.Length <= 6 ? (v | 0xFF000000u) : v;
    }

    /// <summary>ARGB CEGUI → (r,g,b,a) en [0..1].</summary>
    public static (float r, float g, float b, float a) ToRgba(uint argb) =>
        (((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f,
         (argb & 0xFF) / 255f, ((argb >> 24) & 0xFF) / 255f);

    /// <summary>ARGB → chaîne <c>#RRGGBBAA</c> exploitable en BBCode
    /// Godot.</summary>
    public static string ToHtml(uint argb) =>
        $"#{(argb & 0xFFFFFF):X6}{(argb >> 24):X2}";
}

/// <summary>
/// Rectangle de couleurs CEGUI
/// (<c>"tl:AARRGGBB tr:... bl:... br:..."</c>). Le MVP n'utilise que
/// <see cref="TopLeft"/> (Godot n'a pas de dégradé par coin natif sur
/// les styles simples).
/// </summary>
public readonly record struct CeguiColourRect(
    uint TopLeft, uint TopRight, uint BottomLeft, uint BottomRight)
{
    public static CeguiColourRect? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        uint tl = 0, tr = 0, bl = 0, br = 0;
        var any = false;
        foreach (var tok in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = tok.IndexOf(':');
            if (i < 0)
            {
                // Couleur unique sans préfixe → les quatre coins.
                if (CeguiColour.ParseArgb(tok) is { } c)
                {
                    tl = tr = bl = br = c;
                    any = true;
                }
                continue;
            }
            var v = CeguiColour.ParseArgb(tok[(i + 1)..]);
            if (v == null)
                continue;
            any = true;
            switch (tok[..i])
            {
                case "tl": tl = v.Value; break;
                case "tr": tr = v.Value; break;
                case "bl": bl = v.Value; break;
                case "br": br = v.Value; break;
            }
        }
        return any ? new CeguiColourRect(tl, tr, bl, br) : null;
    }
}

/// <summary>&lt;ImageryComponent&gt; : une image (explicite ou via
/// <c>ImageProperty</c>) teintée, affichée dans une Area. La Area
/// (Dim/UnifiedDim/ImageDim/WidgetDim/DimOperator) n'est pas
/// interprétée — comptée dans <c>Ignored</c>.</summary>
public sealed class CeguiImageryComponent
{
    public CeguiImageRef? Image { get; set; }
    public string? ImageProperty { get; set; }
    public CeguiColourRect? Colours { get; set; }
    public string? ColourRectProperty { get; set; }
    public string? ColourProperty { get; set; }
    public string? HorzFormat { get; set; }
    public string? VertFormat { get; set; }
}

/// <summary>&lt;FrameComponent&gt; : cadre composite d'images par
/// bord (<c>LeftEdge</c>, <c>RightEdge</c>, <c>TopEdge</c>,
/// <c>BottomEdge</c>, coins, <c>Background</c>).</summary>
public sealed class CeguiFrameComponent
{
    /// <summary>type de bord → référence image.</summary>
    public List<(string Edge, CeguiImageRef Ref)> Images { get; } = new();
    public CeguiColourRect? Colours { get; set; }
    public string? ColourRectProperty { get; set; }

    public CeguiImageRef? ImageFor(string edge)
    {
        foreach (var (e, r) in Images)
            if (string.Equals(e, edge, StringComparison.OrdinalIgnoreCase))
                return r;
        return null;
    }
}

/// <summary>Une &lt;ImagerySection&gt; nommée d'un WidgetLook.</summary>
public sealed class CeguiImagerySection
{
    public string Name { get; init; } = "";
    public List<CeguiImageryComponent> Components { get; } = new();
    public List<CeguiFrameComponent> Frames { get; } = new();
    /// <summary>La section contient un TextComponent (le texte du
    /// widget est dessiné — utile pour savoir qu'un Static affiche du
    /// texte).</summary>
    public bool HasText { get; set; }
}

/// <summary>&lt;Section&gt; référencée dans un StateImagery —
/// locale ou cross-WidgetLook via l'attribut <c>look</c>. Porte
/// optionnellement une surcharge de couleur.</summary>
public sealed class CeguiSectionRef
{
    public string? Look { get; set; }
    public string Section { get; set; } = "";
    public CeguiColourRect? Colours { get; set; }
    public string? ColourProperty { get; set; }
    public string? ColourRectProperty { get; set; }
}

public sealed class CeguiLayer
{
    public List<CeguiSectionRef> Sections { get; } = new();
}

/// <summary>
/// Un &lt;WidgetLook&gt; Falagard — vision minimaliste suffisante au
/// rendu MVP : propriétés par défaut, couleurs frame/background,
/// images des ImagerySection et composition des StateImagery. Les
/// Areas/NamedArea/Child/Dim* ne sont pas interprétés (comptés).
/// </summary>
public sealed class CeguiWidgetLook
{
    public string Name { get; init; } = "";

    /// <summary>PropertyDefinition : name → initialValue.</summary>
    public Dictionary<string, string> Defaults { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>PropertyLinkDefinition : name → initialValue (lien
    /// vers un sous-widget auto, ex. <c>__auto_titlebar__</c>).</summary>
    public Dictionary<string, string> Links { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>&lt;Property&gt; figées dans le look.</summary>
    public Dictionary<string, string> Properties { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CeguiImagerySection> Sections { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>StateImagery : nom d'état → couches.</summary>
    public Dictionary<string, List<CeguiLayer>> States { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Valeur par défaut d'une propriété : Property (fixe)
    /// puis PropertyDefinition puis PropertyLinkDefinition.</summary>
    public string? GetDefault(string prop) =>
        Properties.TryGetValue(prop, out var p) ? p
        : Defaults.TryGetValue(prop, out var d) ? d
        : Links.TryGetValue(prop, out var l) ? l
        : null;
}

/// <summary>
/// Parseur minimaliste de <c>uidata/looknfeel/DejaVu.looknfeel</c>
/// (Falagard XML, ~13 500 lignes, 109 WidgetLooks). N'implémente PAS
/// le moteur Falagard complet : les expressions d'Area
/// (<c>UnifiedDim</c>/<c>DimOperator</c>/<c>ImageDim</c>/<c>WidgetDim</c>),
/// les <c>NamedArea</c>, les <c>Child</c> et les alignements de
/// composants sont comptés dans <see cref="Ignored"/> plutôt
/// qu'interprétés.
/// </summary>
public sealed class CeguiLookNFeel
{
    public Dictionary<string, CeguiWidgetLook> Looks { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Compteur des constructions Falagard rencontrées mais
    /// non interprétées par le rendu MVP (clé = nom d'élément).</summary>
    public Dictionary<string, int> Ignored { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    private void Bump(string feature)
    {
        Ignored.TryGetValue(feature, out var n);
        Ignored[feature] = n + 1;
    }

    public CeguiWidgetLook? GetLook(string? name) =>
        name != null && Looks.TryGetValue(name, out var l) ? l : null;

    public static CeguiLookNFeel Parse(string path)
    {
        using var s = Vfs.EdenVfs.OpenRead(path)
            ?? throw new FileNotFoundException(path);
        var doc = XDocument.Load(s);
        var lnf = new CeguiLookNFeel();
        foreach (var wl in (doc.Root?.Elements("WidgetLook") ??
                 Enumerable.Empty<XElement>()))
            lnf.ParseLook(wl);
        return lnf;
    }

    private void ParseLook(XElement el)
    {
        var look = new CeguiWidgetLook { Name = el.Attribute("name")?.Value ?? "" };
        if (look.Name.Length == 0)
            return;
        foreach (var child in el.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "PropertyDefinition":
                    Set(look.Defaults, child);
                    break;
                case "PropertyLinkDefinition":
                    Set(look.Links, child);
                    break;
                case "Property":
                    Set(look.Properties, child, valueAttr: "value");
                    break;
                case "ImagerySection":
                    ParseSection(look, child);
                    break;
                case "StateImagery":
                    ParseState(look, child);
                    break;
                case "NamedArea":
                case "Child":
                case "FalagardLink":
                    Bump(child.Name.LocalName);
                    break;
                default:
                    Bump(child.Name.LocalName);
                    break;
            }
        }
        Looks[look.Name] = look;
    }

    private static void Set(Dictionary<string, string> map, XElement el,
        string valueAttr = "initialValue")
    {
        var name = el.Attribute("name")?.Value;
        if (name != null)
            map[name] = el.Attribute(valueAttr)?.Value ?? "";
    }

    private void ParseSection(CeguiWidgetLook look, XElement el)
    {
        var section = new CeguiImagerySection
        {
            Name = el.Attribute("name")?.Value ?? "",
        };
        foreach (var comp in el.Elements())
        {
            switch (comp.Name.LocalName)
            {
                case "ImageryComponent":
                    section.Components.Add(ParseImagery(comp));
                    break;
                case "FrameComponent":
                    section.Frames.Add(ParseFrame(comp));
                    break;
                case "TextComponent":
                    section.HasText = true;
                    Bump("TextComponent.Area"); // position du texte ignorée
                    break;
                default:
                    Bump(comp.Name.LocalName);
                    break;
            }
        }
        look.Sections[section.Name] = section;
    }

    private CeguiImageryComponent ParseImagery(XElement el)
    {
        var c = new CeguiImageryComponent();
        foreach (var sub in el.Elements())
        {
            switch (sub.Name.LocalName)
            {
                case "Area":
                    Bump("Area");
                    break;
                case "Image":
                    c.Image = new CeguiImageRef(
                        sub.Attribute("imageset")?.Value ?? "",
                        sub.Attribute("image")?.Value ?? "");
                    break;
                case "ImageProperty":
                    c.ImageProperty = sub.Attribute("name")?.Value;
                    break;
                case "Colours":
                    c.Colours = ReadColours(sub);
                    break;
                case "ColourProperty":
                    c.ColourProperty = sub.Attribute("name")?.Value;
                    break;
                case "ColourRectProperty":
                    c.ColourRectProperty = sub.Attribute("name")?.Value;
                    break;
                case "VertFormat":
                    c.VertFormat = sub.Attribute("value")?.Value;
                    break;
                case "HorzFormat":
                    c.HorzFormat = sub.Attribute("value")?.Value;
                    break;
                case "VertFormatProperty":
                case "HorzFormatProperty":
                    // nom de propriété → valeur inconnue ici, ignorée
                    Bump(sub.Name.LocalName);
                    break;
                default:
                    Bump(sub.Name.LocalName);
                    break;
            }
        }
        return c;
    }

    private CeguiFrameComponent ParseFrame(XElement el)
    {
        var c = new CeguiFrameComponent();
        foreach (var sub in el.Elements())
        {
            switch (sub.Name.LocalName)
            {
                case "Area":
                    Bump("Area");
                    break;
                case "Image":
                    c.Images.Add((
                        sub.Attribute("type")?.Value ?? "Background",
                        new CeguiImageRef(
                            sub.Attribute("imageset")?.Value ?? "",
                            sub.Attribute("image")?.Value ?? "")));
                    break;
                case "Colours":
                    c.Colours = ReadColours(sub);
                    break;
                case "ColourRectProperty":
                    c.ColourRectProperty = sub.Attribute("name")?.Value;
                    break;
                default:
                    Bump(sub.Name.LocalName);
                    break;
            }
        }
        return c;
    }

    private void ParseState(CeguiWidgetLook look, XElement el)
    {
        var name = el.Attribute("name")?.Value ?? "";
        var layers = new List<CeguiLayer>();
        foreach (var layerEl in el.Elements("Layer"))
        {
            var layer = new CeguiLayer();
            foreach (var sec in layerEl.Elements("Section"))
            {
                var sref = new CeguiSectionRef
                {
                    Look = sec.Attribute("look")?.Value,
                    Section = sec.Attribute("section")?.Value ?? "",
                };
                foreach (var sub in sec.Elements())
                {
                    switch (sub.Name.LocalName)
                    {
                        case "Colours":
                            sref.Colours = ReadColours(sub);
                            break;
                        case "ColourProperty":
                            sref.ColourProperty = sub.Attribute("name")?.Value;
                            break;
                        case "ColourRectProperty":
                            sref.ColourRectProperty = sub.Attribute("name")?.Value;
                            break;
                        default:
                            Bump(sub.Name.LocalName);
                            break;
                    }
                }
                layer.Sections.Add(sref);
            }
            layers.Add(layer);
        }
        look.States[name] = layers;
    }

    /// <summary>Lit <c>&lt;Colours topLeft topRight bottomLeft
    /// bottomRight/&gt;</c> (attributs directs, format AARRGGBB).</summary>
    private static CeguiColourRect? ReadColours(XElement el)
    {
        uint? tl = CeguiColour.ParseArgb(el.Attribute("topLeft")?.Value);
        if (tl == null)
            return null;
        return new CeguiColourRect(
            tl.Value,
            CeguiColour.ParseArgb(el.Attribute("topRight")?.Value) ?? tl.Value,
            CeguiColour.ParseArgb(el.Attribute("bottomLeft")?.Value) ?? tl.Value,
            CeguiColour.ParseArgb(el.Attribute("bottomRight")?.Value) ?? tl.Value);
    }

    /// <summary>
    /// Résout une section référencée par un StateImagery — gère la
    /// référence croisée <c>look="DejaVu/XShared"</c>.
    /// </summary>
    public CeguiImagerySection? ResolveSection(CeguiWidgetLook look,
        CeguiSectionRef sref)
    {
        var owner = sref.Look != null ? GetLook(sref.Look) ?? look : look;
        return owner.Sections.TryGetValue(sref.Section, out var s) ? s : null;
    }

    /// <summary>Couleur (topLeft) associée à un nom de propriété de
    /// look : cherche dans Properties/Defaults/Links puis parse le
    /// ColourRect ou la couleur simple.</summary>
    public static uint? ColourOfProp(CeguiWidgetLook look, string? propName)
    {
        if (propName == null)
            return null;
        var raw = look.GetDefault(propName);
        if (raw == null)
            return null;
        var rect = CeguiColourRect.Parse(raw);
        if (rect != null)
            return rect.Value.TopLeft;
        return CeguiColour.ParseArgb(raw);
    }
}
