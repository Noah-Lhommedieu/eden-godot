using System.Text.RegularExpressions;
using EdenClient.Data;

namespace EdenClient.Ui;

/// <summary>Type de Control Godot à instancier pour un nœud.</summary>
public enum UiControlKind
{
    Control, Panel, Label, RichLabel, Button, TextureButton, CheckBox,
    LineEdit, TextEdit, ItemList, Tree, ProgressBar, HSlider, VSlider,
    VScrollBar, HScrollBar, ScrollContainer, TabContainer, OptionButton,
    MenuBar, SpinBox, TextureRect, NinePatchRect, ColorRect,
    SubViewportContainer, ItemSlot, MultiColumnList,
}

public enum UiTextAlign { Left, Centre, Right }
public enum UiVertAlign { Top, Centre, Bottom }
public enum UiStyleKind { None, Colour, Texture, NinePatch, Composite }

/// <summary>Cellule d'une frame 9-patch (corners/edges/centre).</summary>
public enum UiPart
{
    TopLeft, Top, TopRight, Left, Center, Right,
    BottomLeft, Bottom, BottomRight,
}

/// <summary>Style de frame/background extrait du looknfeel.</summary>
public sealed class UiStyleSpec
{
    public UiStyleKind Kind { get; set; }
    public uint Colour { get; set; }              // ARGB si Colour ; teinte si Composite
    public CeguiImageRef? Image { get; set; }     // si Texture/NinePatch
    public int MarginLeft, MarginTop, MarginRight, MarginBottom;

    /// <summary>Parts d'une frame composite (kind Composite) — la
    /// texture finale est synthétisée côté Godot
    /// (<c>UiTextureCache.ComposeNinePatch</c>).</summary>
    public Dictionary<UiPart, CeguiImageRef> Parts { get; } = new();

    public static UiStyleSpec Coloured(uint argb) =>
        new() { Kind = UiStyleKind.Colour, Colour = argb };
}

/// <summary>
/// Arbre de spécification UI — intermédiaire pur C# entre
/// <see cref="CeguiNode"/> et les <c>Control</c> Godot. Instanciable
/// sans runtime Godot (tests) : <c>CeguiToGodot</c> le matérialise.
/// </summary>
public sealed class UiNodeSpec
{
    public UiControlKind Kind { get; set; } = UiControlKind.Control;
    public string Name { get; set; } = "";
    public string CeguiType { get; set; } = "";
    public string? TargetType { get; set; }

    public CeguiAnchors Anchors { get; set; } =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    public string? Text { get; set; }
    public string? BbText { get; set; }           // BBCode Godot ou null
    public bool Visible { get; set; } = true;
    public bool Disabled { get; set; }
    public bool TopLevel { get; set; }
    public bool MouseIgnore { get; set; }
    public float Alpha { get; set; } = 1f;
    public string? Tooltip { get; set; }
    public int? FontSize { get; set; }
    public uint? TextColour { get; set; }         // ARGB
    public UiTextAlign? HorzAlign { get; set; }
    public UiVertAlign? VertAlign { get; set; }
    public bool WordWrap { get; set; }

    /// <summary>Min/max en pixels (partie offset des tailles unifiées ;
    /// la partie scale n'est pas exprimable en Godot — voir doc).</summary>
    public float MinW, MinH, MaxW, MaxH;

    public UiStyleSpec? Frame { get; set; }
    public UiStyleSpec? Background { get; set; }

    /// <summary>Styles par état (boutons) : survol / pressé /
    /// désactivé — extraits des StateImagery Hover/Pushed/Disabled.</summary>
    public UiStyleSpec? Hover { get; set; }
    public UiStyleSpec? Pressed { get; set; }
    public UiStyleSpec? DisabledLook { get; set; }
    /// <summary>Couleurs de label par état (ARGB) du look.</summary>
    public uint? FontHover, FontPressed, FontDisabled;

    /// <summary>Chrome de la barre de titre auto d'un FrameWindow
    /// (composite DejaVu/Titlebar) + hauteur native en px + couleur
    /// de caption + images du bouton ×.</summary>
    public UiStyleSpec? Titlebar { get; set; }
    public int TitlebarHeight { get; set; }
    public uint? CaptionColour { get; set; }
    public CeguiImageRef? CloseNormal, CloseHover, ClosePushed;

    public CeguiImageRef? Image, HoverImage, PushedImage, DisabledImage;

    public int Columns { get; set; }              // MultiColumnList/Tree
    /// <summary>Colonnes d'un MultiColumnList, dans l'ordre de
    /// déclaration des props <c>ColumnHeader</c>
    /// (<c>text:… width:{scale,offset} id:n</c>).</summary>
    public List<MclColumn> MclColumns { get; } = new();
    public bool HasTitleBar { get; set; }
    public bool CloseButton { get; set; }
    public bool Secret { get; set; }              // Editbox MaskText
    public int MaxLength { get; set; }
    public bool ReadOnly { get; set; }
    public bool Selected { get; set; }            // Checkbox
    public float Progress { get; set; }           // 0..1

    /// <summary>Enfants « pane » d'un TabContainer (les autres sont
    /// rangés dans <see cref="SideChildren"/>).</summary>
    public List<UiNodeSpec> Children { get; } = new();
    public List<UiNodeSpec> SideChildren { get; } = new();

    public IEnumerable<UiNodeSpec> AllChildren()
    {
        foreach (var c in Children) yield return c;
        foreach (var c in SideChildren) yield return c;
    }
}

/// <summary>Colonne d'un MultiColumnList native (prop
/// <c>ColumnHeader</c>). <paramref name="WidthScale"/> est la part
/// proportionnelle de la largeur, <paramref name="WidthOffset"/> la
/// part fixe en pixels, <paramref name="Id"/> l'identifiant natif de
/// la colonne (sert de clé de tri côté code).</summary>
public readonly record struct MclColumn(string Text, float WidthScale,
    float WidthOffset, int Id);

/// <summary>Rapport agrégé de conversion — types non mappés,
/// propriétés ignorées, erreurs par layout.</summary>
public sealed class UiBuildReport
{
    public Dictionary<string, int> UnknownTypes { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Types résolus par préfixe seulement (ex.
    /// <c>DejaVu/Embed2</c> → <c>DejaVu/Embed</c>).</summary>
    public Dictionary<string, int> PrefixTypes { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> IgnoredProps { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int LayoutsOk { get; set; }
    public int LayoutsFailed { get; set; }
    public List<string> Errors { get; } = new();

    public void Bump(Dictionary<string, int> map, string key)
    {
        map.TryGetValue(key, out var n);
        map[key] = n + 1;
    }
}

/// <summary>Contexte chargé depuis <c>uidata/</c> : scheme +
/// looknfeel + tous les imagesets + fonts.</summary>
public sealed class CeguiContext
{
    public required CeguiScheme Scheme { get; init; }
    public CeguiLookNFeel LookNFeel { get; } = new();
    public CeguiImagesetCache Imagesets { get; } = new();
    public CeguiFontTable Fonts { get; } = new();

    /// <summary>Charge le jeu de données UI complet depuis
    /// <c>raw_assets/uidata</c>.</summary>
    public static CeguiContext Load(string uidataDir)
    {
        var scheme = CeguiScheme.Parse(
            Path.Combine(uidataDir, "schemes", "DejaVu.scheme"));
        var ctx = new CeguiContext { Scheme = scheme };

        var lnfFile = scheme.LookNFeelFile ?? "DejaVu.looknfeel";
        var lnfPath = Path.Combine(uidataDir, "looknfeel", lnfFile);
        if (Vfs.EdenVfs.Exists(lnfPath))
        {
            var lnf = CeguiLookNFeel.Parse(lnfPath);
            foreach (var kv in lnf.Looks)
                ctx.LookNFeel.Looks[kv.Key] = kv.Value;
            foreach (var kv in lnf.Ignored)
                ctx.LookNFeel.Ignored[kv.Key] = kv.Value;
        }

        ctx.Imagesets.LoadDirectory(Path.Combine(uidataDir, "imagesets"));
        ctx.Fonts.LoadDirectory(Path.Combine(uidataDir, "fonts"));
        return ctx;
    }
}

/// <summary>
/// Conversion <see cref="CeguiNode"/> → <see cref="UiNodeSpec"/>.
/// Mapping par TargetType du scheme puis par renderer ; types
/// inconnus → <c>Control</c> + compteur <see cref="UiBuildReport"/>.
/// </summary>
public sealed class CeguiBuilder
{
    private readonly CeguiContext _ctx;

    public CeguiBuilder(CeguiContext ctx) => _ctx = ctx;
    public UiBuildReport Report { get; } = new();

    /// <summary>Propriétés prises en compte par le MVP — tout le
    /// reste est comptabilisé dans <c>IgnoredProps</c>.</summary>
    private static readonly HashSet<string> HandledProps =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "UnifiedAreaRect", "HorizontalAlignment", "VerticalAlignment",
            "Text", "Visible", "Disabled", "AlwaysOnTop", "Alpha",
            "Tooltip", "Font", "TextColours", "TextColour",
            "HorzFormatting", "VertFormatting",
            "UnifiedMinSize", "UnifiedMaxSize",
            "FrameEnabled", "BackgroundEnabled",
            "Image", "NormalImage", "HoverImage", "PushedImage",
            "DisabledImage", "Selected", "ReadOnly", "MaxTextLength",
            "MaskText", "CurrentProgress", "ID", "ClippedByParent",
            "MousePassThroughEnabled", "MouseInputPropagationEnabled",
            "TitlebarEnabled", "TitlebarFont", "TitlebarTextColour",
            "CloseButtonEnabled", "HelpButtonEnabled", "SizingEnabled",
            "TextParsingEnabled", "ColumnHeader", "GroupID",
            "MultiSelect", "ZOrderChangeEnabled", "TextFrameColour",
            "InheritsAlpha", "DestroyedByParent", "DragMovingEnabled",
        };

    /// <summary>Parse + construit un fichier .layout.</summary>
    public UiNodeSpec? BuildLayoutFile(string path)
    {
        try
        {
            var root = CeguiLayout.ParseFile(path);
            var spec = BuildSpec(root);
            Report.LayoutsOk++;
            return spec;
        }
        catch (Exception e)
        {
            Report.LayoutsFailed++;
            Report.Errors.Add($"{Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    public UiNodeSpec BuildSpec(CeguiNode node) => BuildNode(node);

    // ----------------------------------------------------------------

    private UiNodeSpec BuildNode(CeguiNode node)
    {
        var spec = new UiNodeSpec
        {
            Name = SanitizeName(node.Name),
            CeguiType = node.Type,
        };
        var mapping = _ctx.Scheme.Resolve(node.Type);
        if (mapping == null)
        {
            // Variante d'un type connu : <c>DejaVu/Embed2</c> →
            // <c>DejaVu/Embed</c> (plus long préfixe gagne).
            mapping = ResolvePrefix(node.Type);
            if (mapping != null)
                Report.Bump(Report.PrefixTypes,
                    $"{node.Type}→{mapping.WindowType}");
        }
        spec.TargetType = mapping?.TargetType;
        var look = _ctx.LookNFeel.GetLook(
            mapping?.LookNFeel ?? node.Type);

        ApplyCommonProps(spec, node, look);
        spec.Kind = KindFor(node, mapping);
        // Texte avec codes [colour='…'] → RichTextLabel BBCode.
        if (spec.Kind == UiControlKind.Label && spec.BbText != null)
            spec.Kind = UiControlKind.RichLabel;
        ApplyVisual(spec, node, look, mapping);
        CountIgnoredProps(node);

        // FrameWindow : titre = propriété Text.
        if (spec.HasTitleBar && spec.Text == null &&
            node.Get("Text") == null)
        {
            spec.Text = ShortName(node.Name);
        }

        foreach (var child in node.Children)
        {
            var childSpec = BuildNode(child);
            // TabContainer : seuls les panes deviennent des onglets ;
            // les boutons/panes auxiliaires partent sur le côté.
            if (spec.Kind == UiControlKind.TabContainer &&
                childSpec.Kind != UiControlKind.Control &&
                childSpec.Kind != UiControlKind.Panel)
            {
                spec.SideChildren.Add(childSpec);
            }
            else
            {
                spec.Children.Add(childSpec);
            }
        }
        return spec;
    }

    /// <summary>Plus long <c>WindowType</c> du scheme préfixe de
    /// <paramref name="wt"/> (variantes numérotées de widgets).</summary>
    private FalagardMapping? ResolvePrefix(string wt)
    {
        FalagardMapping? best = null;
        foreach (var m in _ctx.Scheme.Mappings)
            if (wt.StartsWith(m.WindowType, StringComparison.OrdinalIgnoreCase) &&
                (best == null || m.WindowType.Length > best.WindowType.Length))
                best = m;
        return best;
    }

    private static string SanitizeName(string name)
    {
        // Les noms CEGUI « Root/Wnd/Child » contiennent '/' — interdit
        // dans Godot (séparateur de NodePath) → '_' .
        return name.Replace('/', '_');
    }

    private static string ShortName(string name)
    {
        var i = name.LastIndexOf('/');
        return i >= 0 ? name[(i + 1)..] : name;
    }

    // ---------------------- mapping des types ----------------------

    private UiControlKind KindFor(CeguiNode node, FalagardMapping? m)
    {
        if (node.IsAutoWindow)
            return UiControlKind.Control;

        var wt = node.Type;
        var leaf = wt[(wt.LastIndexOf('/') + 1)..];
        var target = m?.TargetType ?? "";

        switch (target)
        {
            case "CEGUI/FrameWindow": return UiControlKind.Panel;
            case "CEGUI/Titlebar": return UiControlKind.Panel;
            case "CEGUI/PushButton":
                return node.Get("NormalImage") != null
                    ? UiControlKind.TextureButton
                    : UiControlKind.Button;
            case "CEGUI/Checkbox":
            case "CEGUI/RadioButton":
                return UiControlKind.CheckBox;
            case "CEGUI/Editbox": return UiControlKind.LineEdit;
            case "CEGUI/MultiLineEditbox": return UiControlKind.TextEdit;
            case "CEGUI/Listbox":
            case "CEGUI/ComboDropList":
            case "CEGUI/ItemListbox":
                return UiControlKind.ItemList;
            case "CEGUI/MultiColumnList":
                return UiControlKind.MultiColumnList;
            case "CEGUI/Tree": return UiControlKind.Tree;
            case "CEGUI/Slider": return UiControlKind.HSlider;
            case "CEGUI/Scrollbar":
                return leaf.Contains("Horizontal",
                    StringComparison.OrdinalIgnoreCase)
                    ? UiControlKind.HScrollBar
                    : UiControlKind.VScrollBar;
            case "CEGUI/Thumb": return UiControlKind.Button;
            case "CEGUI/TabControl": return UiControlKind.TabContainer;
            case "CEGUI/TabButton": return UiControlKind.Button;
            case "CEGUI/Combobox": return UiControlKind.OptionButton;
            case "CEGUI/Menubar": return UiControlKind.MenuBar;
            case "CEGUI/PopupMenu": return UiControlKind.Panel;
            case "CEGUI/MenuItem": return UiControlKind.Button;
            case "CEGUI/Spinner": return UiControlKind.SpinBox;
            case "CEGUI/Tooltip": return UiControlKind.Panel;
            case "CEGUI/ProgressBar": return UiControlKind.ProgressBar;
            case "CEGUI/ScrollablePane": return UiControlKind.ScrollContainer;
            case "CEGUI/GroupBox": return UiControlKind.Panel;
            case "CEGUI/ItemEntry": return UiControlKind.Label;
            case "CEGUI/ListHeader": return UiControlKind.Control;
            case "CEGUI/ListHeaderSegment": return UiControlKind.Button;
            case "CEGUI/SlotImageItem":
                return UiControlKind.ItemSlot;
            case "CEGUI/RoundImage":
                return UiControlKind.TextureRect;
        }

        // Slot item natif : DejaVu/SlotImage* est mappé sur
        // DefaultWindow+Falagard/StaticImage dans le scheme — c'est le
        // widget « case item » des fenêtres craft (icône + qté +
        // drag&drop), pas une simple image. Vérifié AVANT le repli
        // renderer qui le rendrait en TextureRect.
        if (leaf.StartsWith("SlotImage", StringComparison.OrdinalIgnoreCase) ||
            leaf.StartsWith("ItemSlot", StringComparison.OrdinalIgnoreCase) ||
            leaf.Contains("ItemSlotbox", StringComparison.OrdinalIgnoreCase) ||
            leaf.Contains("dragitem", StringComparison.OrdinalIgnoreCase) ||
            leaf.Contains("ItemIcon", StringComparison.OrdinalIgnoreCase))
            return UiControlKind.ItemSlot;

        if (m != null)
        {
            // Types DejaVu/* mappés sur DefaultWindow : le renderer
            // Falagard décide.
            return m.Renderer switch
            {
                "Falagard/StaticText" => UiControlKind.Label,
                "Falagard/StaticImage" => UiControlKind.TextureRect,
                "Falagard/Static" => UiControlKind.Panel,
                "Falagard/ItemEntry" => UiControlKind.Label,
                _ => UiControlKind.Control,
            };
        }
        if (leaf.Contains("RenderTarget", StringComparison.OrdinalIgnoreCase) ||
            leaf.Contains("RenderPicture", StringComparison.OrdinalIgnoreCase))
            return UiControlKind.SubViewportContainer;

        if (wt != "DefaultWindow")
            Report.Bump(Report.UnknownTypes, wt);
        return UiControlKind.Control;
    }

    // ------------------ propriétés communes -----------------------

    private void ApplyCommonProps(UiNodeSpec spec, CeguiNode node,
        CeguiWidgetLook? look)
    {
        // Géométrie
        if (UnifiedRect.Parse(node.Get("UnifiedAreaRect")) is { } rect)
        {
            spec.Anchors = CeguiAnchors.FromUnified(rect,
                node.Get("HorizontalAlignment"),
                node.Get("VerticalAlignment"));
        }

        // Texte + BBCode — les valeurs chinoises littérales des
        // .layout passent par le dictionnaire EN (le client natif
        // les écrase via setText(index) ; on reproduit par
        // correspondance de contenu).
        var rawText = node.Get("Text");
        if (rawText != null)
        {
            rawText = UiTextEn.Translate(rawText);
            spec.Text = StripTags(rawText);
            var bb = ToBbCode(rawText);
            spec.BbText = bb != spec.Text ? bb : null;
        }

        spec.Visible = node.GetBool("Visible", true);
        spec.Disabled = node.GetBool("Disabled", false);
        spec.TopLevel = node.GetBool("AlwaysOnTop", false);
        spec.MouseIgnore = node.GetBool("MousePassThroughEnabled", false);
        // Statics Falagard = décor pur (border/fond déclarés APRÈS les
        // boutons dans les CommonDlg) : MouseFilter.Ignore sinon ils
        // avalent les clics des contrôles qu'ils recouvrent.
        if (!spec.MouseIgnore && spec.CeguiType
                .Split('/')[^1].StartsWith("Static",
                    StringComparison.OrdinalIgnoreCase))
            spec.MouseIgnore = true;
        spec.Alpha = Math.Clamp(node.GetFloat("Alpha", 1f), 0f, 1f);
        var tip = node.Get("Tooltip");
        spec.Tooltip = tip != null ? UiTextEn.Translate(tip) : null;
        spec.FontSize = _ctx.Fonts.GetSize(node.Get("Font"));

        if (CeguiColourRect.Parse(node.Get("TextColours")) is { } tc)
            spec.TextColour = tc.TopLeft;
        else if (CeguiColour.ParseArgb(node.Get("TextColour")) is { } tc2)
            spec.TextColour = tc2;

        spec.HorzAlign = node.Get("HorzFormatting") switch
        {
            "CentreAligned" or "HorzCentred" or "WordWrapCentred" =>
                UiTextAlign.Centre,
            "RightAligned" or "HorzRightAligned" or "WordWrapRightAligned" =>
                UiTextAlign.Right,
            null => null,
            _ => UiTextAlign.Left,
        };
        spec.WordWrap = node.Get("HorzFormatting")?.StartsWith("WordWrap") == true;
        spec.VertAlign = node.Get("VertFormatting") switch
        {
            "CentreAligned" or "VertCentred" => UiVertAlign.Centre,
            "BottomAligned" => UiVertAlign.Bottom,
            null => null,
            _ => UiVertAlign.Top,
        };

        if (CeguiLayout.ParseUnifiedSize(node.Get("UnifiedMinSize")) is { } min)
        {
            // Convention : seule la partie offset (pixels) est
            // exprimable en custom_minimum_size Godot.
            spec.MinW = min.wo;
            spec.MinH = min.ho;
        }
        if (CeguiLayout.ParseUnifiedSize(node.Get("UnifiedMaxSize")) is { } max)
        {
            spec.MaxW = max.wo;
            spec.MaxH = max.ho;
        }

        // FrameWindow
        spec.HasTitleBar = spec.TargetType == "CEGUI/FrameWindow" &&
            node.GetBool("TitlebarEnabled",
                BoolOf(look?.GetDefault("TitlebarEnabled"), true));
        spec.CloseButton = spec.HasTitleBar &&
            node.GetBool("CloseButtonEnabled",
                BoolOf(look?.GetDefault("CloseButtonEnabled"), true));

        // Édition
        spec.Secret = node.GetBool("MaskText", false);
        spec.MaxLength = (int)node.GetFloat("MaxTextLength", 0);
        spec.ReadOnly = node.GetBool("ReadOnly", false);
        spec.Selected = node.GetBool("Selected", false);
        spec.Progress = Math.Clamp(node.GetFloat("CurrentProgress", 0f), 0f, 1f);

        // MultiColumnList : nb de colonnes = nb de props ColumnHeader.
        if (node.PropLists.TryGetValue("ColumnHeader", out var cols))
        {
            spec.Columns = cols.Count;
            foreach (var c in cols)
                spec.MclColumns.Add(ParseColumnHeader(c));
        }

        // Images explicites
        spec.Image = CeguiImageRef.Parse(node.Get("Image"));
        spec.HoverImage = CeguiImageRef.Parse(node.Get("HoverImage"));
        spec.PushedImage = CeguiImageRef.Parse(node.Get("PushedImage"));
        spec.DisabledImage = CeguiImageRef.Parse(node.Get("DisabledImage"));
        if (spec.Image == null &&
            CeguiImageRef.Parse(node.Get("NormalImage")) is { } ni)
        {
            spec.Image = ni;
        }
    }

    private static bool BoolOf(string? v, bool fallback) =>
        v == null ? fallback
        : v.Equals("True", StringComparison.OrdinalIgnoreCase) || v == "1";

    /// <summary>Parse une prop <c>ColumnHeader</c> native
    /// (<c>text:&lt;titre&gt; width:{&lt;scale&gt;,&lt;offset&gt;} id:&lt;n&gt;</c>
    /// — text/id optionnels, width UDim {scale,offset}).</summary>
    private static readonly Regex ColumnHeaderRe =
        new(@"(\w+):(\{[^}]*\}|[^{]*?)(?=\s+\w+:|\s*$)",
            RegexOptions.Compiled);
    private static readonly Regex UDimRe =
        new(@"\{([^,]+),([^}]*)\}", RegexOptions.Compiled);

    private static MclColumn ParseColumnHeader(string value)
    {
        string text = "";
        float scale = 0f, offset = 0f;
        int id = 0;
        foreach (Match kv in ColumnHeaderRe.Matches(value))
        {
            var key = kv.Groups[1].Value;
            var val = kv.Groups[2].Value;
            if (key.Equals("text", StringComparison.OrdinalIgnoreCase))
                text = UiTextEn.Translate(StripTags(val));
            else if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                int.TryParse(val, out id);
            else if (key.Equals("width", StringComparison.OrdinalIgnoreCase))
            {
                var m = UDimRe.Match(val);
                if (m.Success)
                {
                    float.TryParse(m.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out scale);
                    float.TryParse(m.Groups[2].Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out offset);
                }
            }
        }
        return new MclColumn(text, scale, offset, id);
    }

    private void CountIgnoredProps(CeguiNode node)
    {
        foreach (var k in node.Props.Keys)
            if (!HandledProps.Contains(k))
                Report.Bump(Report.IgnoredProps, k);
    }

    // ------------------ visuel (looknfeel MVP) ---------------------

    private void ApplyVisual(UiNodeSpec spec, CeguiNode node,
        CeguiWidgetLook? look, FalagardMapping? mapping)
    {
        // Défaut CEGUI Falagard : frame + background ACTIVÉS (les
        // layouts qui les désactivent le font explicitement, ex.
        // Root/ChannelWnd BackgroundEnabled="False" — un repli à
        // false dessinerait l'imagerie partout).
        bool frameEnabled = node.GetBool("FrameEnabled",
            BoolOf(look?.GetDefault("FrameEnabled"), true));
        bool bgEnabled = node.GetBool("BackgroundEnabled",
            BoolOf(look?.GetDefault("BackgroundEnabled"), true));

        if (look != null)
        {
            spec.Frame = ExtractFrame(look, node);
            spec.Background = ExtractBackground(look, node, frameEnabled);
            // Chrome composite (nine-patch natif) : parts par coin /
            // bord / centre issues des ImagerySection du look —
            // remplace le style simple quand il trouve des images.
            if (ExtractChrome(look, spec, frameEnabled, bgEnabled)
                    is { } chrome)
            {
                spec.Frame = chrome;
                // Le composite porte déjà le centre (ClientBrush /
                // Backdrop) : un Background séparé ferait double
                // emploi sous la frame.
                if (chrome.Parts.ContainsKey(UiPart.Center))
                    spec.Background = null;
            }
            if (spec.TargetType == "CEGUI/FrameWindow")
                ExtractTitlebarChrome(spec);
            ExtractStateStyles(spec, look);
        }

        // FrameEnabled=False + fond activé : le composite de la frame
        // disparaît, mais son centre (ex. HintFrameBackdrop des
        // *TextLab) reste le fond natif — il serait perdu sinon.
        if (!frameEnabled && bgEnabled &&
            spec.Frame is { Kind: UiStyleKind.Composite } f &&
            f.Parts.TryGetValue(UiPart.Center, out var cc))
            spec.Background = new UiStyleSpec
                { Kind = UiStyleKind.Texture, Image = cc };
        // Les drapeaux du layout coupent le rendu — quel que soit le
        // kind de style (un fond image doit aussi disparaître, sinon
        // un conteneur plein écran opaque recouvre le monde 3D).
        if (!frameEnabled)
            spec.Frame = null;
        if (!bgEnabled)
            spec.Background = null;

        // Couleur de texte par défaut du look (boutons : NormalTextColour).
        if (spec.TextColour == null && look != null)
        {
            spec.TextColour =
                CeguiLookNFeel.ColourOfProp(look, "NormalTextColour") ??
                CeguiLookNFeel.ColourOfProp(look, "TextColour");
        }
    }

    /// <summary>Itère les (section, sref) d'un état en résolvant les
    /// références croisées <c>look=</c>.</summary>
    private IEnumerable<(CeguiImagerySection sec, CeguiSectionRef sref)>
        Sections(CeguiWidgetLook look, params string[] states)
    {
        foreach (var st in states)
        {
            if (!look.States.TryGetValue(st, out var layers))
                continue;
            foreach (var layer in layers)
            foreach (var sref in layer.Sections)
            {
                var sec = _ctx.LookNFeel.ResolveSection(look, sref);
                if (sec != null)
                    yield return (sec, sref);
            }
        }
    }

    /// <summary>Style de frame : priorité aux FrameComponent avec
    /// image de fond → NinePatch (marges = tailles des bords dans
    /// l'imageset) ; sinon couleur FrameColours.</summary>
    private UiStyleSpec? ExtractFrame(CeguiWidgetLook look, CeguiNode node)
    {
        foreach (var (sec, sref) in Sections(look,
            "EnabledFrame", "Normal", "Enabled", "WithFrameEnabledBackground"))
        {
            foreach (var fc in sec.Frames)
            {
                var style = FrameFromComponent(fc, look, sref);
                if (style != null)
                    return style;
            }
        }
        // Fallback : couleur déclarée.
        if (CeguiColourRect.Parse(look.GetDefault("FrameColours")) is { } cr)
            return UiStyleSpec.Coloured(cr.TopLeft);
        return null;
    }

    private UiStyleSpec? FrameFromComponent(CeguiFrameComponent fc,
        CeguiWidgetLook look, CeguiSectionRef sref)
    {
        if (fc.Images.Count == 0)
            return null;
        var bg = fc.ImageFor("Background");
        var style = new UiStyleSpec();
        if (bg != null)
        {
            style.Kind = UiStyleKind.NinePatch;
            style.Image = bg;
            // Marges = dimensions des images de bord dans l'atlas.
            style.MarginLeft = RegionSize(fc.ImageFor("LeftEdge")).w;
            style.MarginRight = RegionSize(fc.ImageFor("RightEdge")).w;
            style.MarginTop = RegionSize(fc.ImageFor("TopEdge")).h;
            style.MarginBottom = RegionSize(fc.ImageFor("BottomEdge")).h;
        }
        else
        {
            // Coins/bords seuls : pas de fond → couleur.
            var col = sref.Colours?.TopLeft ?? fc.Colours?.TopLeft;
            if (col == null && fc.ColourRectProperty != null)
                col = CeguiLookNFeel.ColourOfProp(look, fc.ColourRectProperty);
            if (col == null)
                return null;
            style.Kind = UiStyleKind.Colour;
            style.Colour = col.Value;
        }
        if (sref.Colours is { } tint)
            style.Colour = tint.TopLeft;
        return style;
    }

    private (int w, int h) RegionSize(CeguiImageRef? r)
    {
        var reg = _ctx.Imagesets.Resolve(r);
        return reg is { } v ? (v.W, v.H) : (0, 0);
    }

    /// <summary>Style de background : ImageryComponent/FrameComponent
    /// de la section background de l'état Enabled selon qu'il y a
    /// frame ou non ; sinon BackgroundColours.</summary>
    private UiStyleSpec? ExtractBackground(CeguiWidgetLook look,
        CeguiNode node, bool frameEnabled)
    {
        var primary = frameEnabled
            ? "WithFrameEnabledBackground"
            : "NoFrameEnabledBackground";
        foreach (var (sec, sref) in Sections(look, primary,
            "EnabledBackground", "Enabled", "Normal"))
        {
            foreach (var fc in sec.Frames)
            {
                var style = FrameFromComponent(fc, look, sref);
                if (style != null)
                    return style;
            }
            foreach (var ic in sec.Components)
            {
                if (ic.Image is { } img)
                    return new UiStyleSpec
                    {
                        Kind = UiStyleKind.Texture, Image = img,
                    };
                var col = sref.Colours?.TopLeft ?? ic.Colours?.TopLeft;
                var propName = sref.ColourRectProperty ??
                    ic.ColourRectProperty ?? sref.ColourProperty;
                if (col == null && propName != null)
                    col = CeguiLookNFeel.ColourOfProp(look, propName);
                if (col != null)
                    return UiStyleSpec.Coloured(col.Value);
            }
        }
        if (CeguiColourRect.Parse(look.GetDefault("BackgroundColours"))
            is { } bg)
            return UiStyleSpec.Coloured(bg.TopLeft);
        return null;
    }

    // ------------------ chrome composite (nine-patch) --------------

    /// <summary>Types <c>&lt;Image type="…"&gt;</c> des FrameComponent
    /// Falagard → cellule 9-patch.</summary>
    private static UiPart? PartOfFrameEdge(string type) =>
        type.ToLowerInvariant() switch
        {
            "topleftcorner" => UiPart.TopLeft,
            "topedge" => UiPart.Top,
            "toprightcorner" => UiPart.TopRight,
            "leftedge" => UiPart.Left,
            "background" => UiPart.Center,
            "rightedge" => UiPart.Right,
            "bottomleftcorner" => UiPart.BottomLeft,
            "bottomedge" => UiPart.Bottom,
            "bottomrightcorner" => UiPart.BottomRight,
            _ => null,
        };

    /// <summary>Devine la cellule d'un <c>&lt;Image&gt;</c> d'un
    /// ImageryComponent (pas de type explicite) d'après le nom de la
    /// région — convention DejaVu : <c>*TopLeft</c>, <c>*LeftEdge</c>,
    /// <c>ClientBrush</c>/<c>*Backdrop</c>/<c>*Middle</c> pour le
    /// centre. Les coins sont testés AVANT les bords
    /// (« WindowTopLeft » finit aussi par « Left »). Les images
    /// <c>SysArea*</c> (zone des boutons système de la titlebar) sont
    /// volontairement ignorées — elles recouvriraient la middle.</summary>
    private static UiPart? ClassifyImageName(string name)
    {
        if (name.Contains("SysArea", StringComparison.OrdinalIgnoreCase))
            return null;
        if (Ends(name, "TopLeft") || Ends(name, "UpperLeft") ||
            Ends(name, "LeftTop"))
            return UiPart.TopLeft;
        if (Ends(name, "TopRight") || Ends(name, "UpperRight") ||
            Ends(name, "RightTop"))
            return UiPart.TopRight;
        if (Ends(name, "BottomLeft") || Ends(name, "LowerLeft") ||
            Ends(name, "LeftBottom"))
            return UiPart.BottomLeft;
        if (Ends(name, "BottomRight") || Ends(name, "LowerRight") ||
            Ends(name, "RightBottom"))
            return UiPart.BottomRight;
        if (Ends(name, "TopEdge") || Ends(name, "Top") ||
            Ends(name, "Upper"))
            return UiPart.Top;
        if (Ends(name, "BottomEdge") || Ends(name, "Bottom") ||
            Ends(name, "Lower"))
            return UiPart.Bottom;
        if (Ends(name, "LeftEdge") || Ends(name, "Left"))
            return UiPart.Left;
        if (Ends(name, "RightEdge") || Ends(name, "Right"))
            return UiPart.Right;
        if (Ends(name, "ClientBrush") || Ends(name, "Backdrop") ||
            Ends(name, "Middle") || Ends(name, "Background") ||
            Ends(name, "Center") || Ends(name, "Centre") ||
            Ends(name, "Body"))
            return UiPart.Center;
        return null;

        static bool Ends(string s, string suf) =>
            s.EndsWith(suf, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Accumule les parts 9-patch d'une liste d'états :
    /// FrameComponent (types explicites) puis ImageryComponent (rôle
    /// déduit du nom d'image — <see cref="ClassifyImageName"/>).
    /// Premier gagnant par cellule ; la teinte retenue est la
    /// première couleur rencontrée (Colours ou ColourRectProperty).</summary>
    private void CollectParts(CeguiWidgetLook look,
        Dictionary<UiPart, CeguiImageRef> parts, ref uint tint,
        params string[] states)
    {
        foreach (var (sec, sref) in Sections(look, states))
        {
            foreach (var fc in sec.Frames)
            {
                foreach (var (edge, r) in fc.Images)
                    if (PartOfFrameEdge(edge) is { } p)
                        parts.TryAdd(p, r);
                if (tint == 0)
                    tint = sref.Colours?.TopLeft ?? fc.Colours?.TopLeft
                        ?? (fc.ColourRectProperty != null
                            ? CeguiLookNFeel.ColourOfProp(
                                look, fc.ColourRectProperty) ?? 0
                            : 0);
            }
            foreach (var ic in sec.Components)
            {
                var r = ic.Image;
                if (r == null && ic.ImageProperty != null)
                    r = CeguiImageRef.Parse(look.GetDefault(ic.ImageProperty));
                if (r is { } img &&
                    ClassifyImageName(img.Image) is { } p)
                    parts.TryAdd(p, img);
                if (tint == 0)
                    tint = sref.Colours?.TopLeft ?? ic.Colours?.TopLeft
                        ?? (CeguiLookNFeel.ColourOfProp(look,
                                sref.ColourRectProperty ??
                                ic.ColourRectProperty ??
                                sref.ColourProperty) ?? 0);
            }
        }
    }

    /// <summary>Chrome complet d'un widget (cadre + client) sous
    /// forme de style Composite ; null si le look ne déclare aucune
    /// image classifiable (texte seul, props sans images…).</summary>
    private UiStyleSpec? ExtractChrome(CeguiWidgetLook look,
        UiNodeSpec spec, bool frameEnabled, bool bgEnabled)
    {
        var parts = new Dictionary<UiPart, CeguiImageRef>();
        uint tint = 0;

        if (spec.TargetType == "CEGUI/FrameWindow")
        {
            // États natifs FrameWindow du look Eden :
            //   {Active|Inactive|Disabled}{WithTitle|NoTitle}
            //   {WithFrame|NoFrame}
            // (ex. « ActiveWithTitleWithFrame » → section
            // « withtitle_frame » : bords latéraux + bas + ClientBrush).
            var suffix = (spec.HasTitleBar ? "WithTitle" : "NoTitle")
                       + (frameEnabled ? "WithFrame" : "NoFrame");
            CollectParts(look, parts, ref tint,
                "Active" + suffix, "Inactive" + suffix,
                "Disabled" + suffix);
        }
        if (frameEnabled)
            CollectParts(look, parts, ref tint,
                "EnabledFrame", "Normal", "Enabled", "Active",
                "ReadOnly");
        if (bgEnabled)
            CollectParts(look, parts, ref tint,
                frameEnabled ? "WithFrameEnabledBackground"
                             : "NoFrameEnabledBackground",
                "EnabledBackground");

        if (parts.Count == 0)
            return null;
        var st = new UiStyleSpec { Kind = UiStyleKind.Composite };
        foreach (var kv in parts)
            st.Parts[kv.Key] = kv.Value;
        st.Colour = tint;
        return st;
    }

    /// <summary>Chrome d'un état donné (boutons : Hover/Pushed/
    /// Disabled) — même collecte, restreinte aux états nommés.</summary>
    private UiStyleSpec? ExtractStateChrome(CeguiWidgetLook look,
        params string[] states)
    {
        var parts = new Dictionary<UiPart, CeguiImageRef>();
        uint tint = 0;
        CollectParts(look, parts, ref tint, states);
        if (parts.Count == 0)
            return null;
        var st = new UiStyleSpec { Kind = UiStyleKind.Composite };
        foreach (var kv in parts)
            st.Parts[kv.Key] = kv.Value;
        st.Colour = tint;
        return st;
    }

    /// <summary>Styles d'état pour les contrôles « bouton-like » :
    /// Hover/Pushed/Disabled + couleurs de label associées
    /// (<c>HoverTextColour</c>… du look).</summary>
    private void ExtractStateStyles(UiNodeSpec spec,
        CeguiWidgetLook look)
    {
        switch (spec.TargetType)
        {
            case "CEGUI/PushButton":
            case "CEGUI/TabButton":
            case "CEGUI/Thumb":
            case "CEGUI/ListHeaderSegment":
            case "CEGUI/MenuItem":
                spec.Hover = ExtractStateChrome(look,
                    "Hover", "EnabledHover", "SelectedHover",
                    "TopHover", "BottomHover");
                spec.Pressed = ExtractStateChrome(look,
                    "Pushed", "EnabledPushed", "SelectedNormal",
                    "TopSelected", "BottomSelected", "TopPushed",
                    "BottomPushed");
                spec.DisabledLook = ExtractStateChrome(look,
                    "Disabled", "DisabledNormal", "SelectedDisabled",
                    "TopDisabled", "BottomDisabled");
                spec.FontHover = CeguiLookNFeel.ColourOfProp(
                    look, "HoverTextColour");
                spec.FontPressed = CeguiLookNFeel.ColourOfProp(
                    look, "PushedTextColour");
                spec.FontDisabled = CeguiLookNFeel.ColourOfProp(
                    look, "DisabledTextColour");
                break;
        }
    }

    // ---------------- FrameWindow : titlebar + close ---------------

    /// <summary>Look de la titlebar auto (<c>DejaVu/Titlebar</c> via
    /// le scheme — les FrameWindow l'instancient en enfant
    /// <c>__auto_titlebar__</c>).</summary>
    private CeguiWidgetLook? TitlebarLook()
    {
        var m = _ctx.Scheme.Resolve("DejaVu/Titlebar");
        return _ctx.LookNFeel.GetLook(m?.LookNFeel ?? "DejaVu/Titlebar");
    }

    /// <summary>Chrome de la barre de titre native : sections de
    /// l'état Active (NewTitlebarLeft/Middle/Right) + ligne
    /// <c>NewTitlebarDivider</c> en bord bas (hauteur du Child
    /// titlebar du look FrameWindow = middle + divider).
    /// Retourne le style + la hauteur native + la couleur caption.</summary>
    public UiStyleSpec? TitlebarStyle(out int height,
        out uint captionArgb)
    {
        height = 0;
        captionArgb = 0xD8FFFFFF;
        var look = TitlebarLook();
        if (look == null)
            return null;
        var parts = new Dictionary<UiPart, CeguiImageRef>();
        uint tint = 0;
        CollectParts(look, parts, ref tint,
            "Active", "Inactive", "Enabled", "Normal");
        // Divider : utilisé par la Dim de hauteur du Child
        // __auto_titlebar__ du look FrameWindow (middle + divider).
        var divider = new CeguiImageRef("DejaVu", "NewTitlebarDivider");
        if (!parts.ContainsKey(UiPart.Bottom) &&
            _ctx.Imagesets.Resolve(divider) != null)
            parts[UiPart.Bottom] = divider;
        if (parts.Count == 0)
            return null;

        foreach (var p in new[] { UiPart.Left, UiPart.Center,
                     UiPart.Right, UiPart.TopLeft, UiPart.Top,
                     UiPart.TopRight })
            if (parts.TryGetValue(p, out var r) &&
                _ctx.Imagesets.Resolve(r) is { } reg)
                height = Math.Max(height, reg.H);
        if (parts.TryGetValue(UiPart.Bottom, out var b) &&
            _ctx.Imagesets.Resolve(b) is { } breg)
            height += breg.H;
        captionArgb = CeguiLookNFeel.ColourOfProp(look, "CaptionColour")
            ?? captionArgb;

        var st = new UiStyleSpec { Kind = UiStyleKind.Composite };
        foreach (var kv in parts)
            st.Parts[kv.Key] = kv.Value;
        st.Colour = tint;
        return st;
    }

    /// <summary>Images du bouton × natif — propriétés du Child
    /// <c>__auto_closebutton__</c> du look FrameWindow
    /// (NormalImage/HoverImage/PushedImage DejaVu).</summary>
    public (CeguiImageRef Normal, CeguiImageRef Hover,
            CeguiImageRef Pushed)? CloseButtonImages()
    {
        var n = new CeguiImageRef("DejaVu", "NewCloseButtonNormal");
        var h = new CeguiImageRef("DejaVu", "NewCloseButtonHover");
        var p = new CeguiImageRef("DejaVu", "NewCloseButtonPressed");
        // Validation : les trois régions doivent exister dans
        // l'imageset (sinon on garde le bouton texte « × »).
        return _ctx.Imagesets.Resolve(n) != null &&
               _ctx.Imagesets.Resolve(h) != null &&
               _ctx.Imagesets.Resolve(p) != null ? (n, h, p) : null;
    }

    /// <summary>Chrome complet d'une fenêtre look FrameWindow —
    /// exposé pour <c>DragHelper</c> (fenêtres construites en code
    /// sans layout, ex. SocialWindow).</summary>
    public UiStyleSpec? WindowFrameStyle(string lookName,
        bool withTitle, bool withFrame)
    {
        var look = _ctx.LookNFeel.GetLook(lookName);
        if (look == null) return null;
        var spec = new UiNodeSpec
        {
            TargetType = "CEGUI/FrameWindow",
            HasTitleBar = withTitle,
        };
        return ExtractChrome(look, spec, withFrame, true);
    }

    /// <summary>Renseigne sur <paramref name="spec"/> la barre de
    /// titre native (style composite + hauteur + caption) et les
    /// images du bouton de fermeture.</summary>
    private void ExtractTitlebarChrome(UiNodeSpec spec)
    {
        spec.Titlebar = TitlebarStyle(out var h, out var caption);
        spec.TitlebarHeight = h;
        spec.CaptionColour = caption;
        if (spec.CloseButton &&
            CloseButtonImages() is { } imgs)
        {
            spec.CloseNormal = imgs.Normal;
            spec.CloseHover = imgs.Hover;
            spec.ClosePushed = imgs.Pushed;
        }
    }

    // --------------------- texte / BBCode --------------------------

    private static readonly Regex ColourOpen = new(
        @"\[colour='([0-9A-Fa-f]{6,8})'\]", RegexOptions.Compiled);
    private static readonly Regex TagStrip = new(
        @"\[/?[a-zA-Z]+(?:='[^']*')?\]", RegexOptions.Compiled);

    /// <summary>Texte CEGUI → texte brut (tags <c>[colour='…']</c>,
    /// <c>[image]</c> etc. supprimés).</summary>
    public static string StripTags(string raw) =>
        TagStrip.Replace(raw, "");

    /// <summary>Texte CEGUI → BBCode Godot approximatif :
    /// <c>[colour='AARRGGBB']</c> → <c>[color=#RRGGBBAA]</c>.</summary>
    public static string ToBbCode(string raw)
    {
        var s = ColourOpen.Replace(raw, m =>
        {
            var argb = CeguiColour.ParseArgb(m.Groups[1].Value) ?? 0xFFFFFFFF;
            return $"[color={CeguiColour.ToHtml(argb)}]";
        });
        s = s.Replace("[/colour]", "[/color]", StringComparison.OrdinalIgnoreCase);
        // Échapper les '[' restants pour ne pas casser le BBCode.
        s = TagStrip.Replace(s, "");
        return s;
    }
}
