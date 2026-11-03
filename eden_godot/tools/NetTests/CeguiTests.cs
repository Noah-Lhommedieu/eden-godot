using EdenClient.Ui;
using Xunit;
using Xunit.Abstractions;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du portage CEGUI → Control Godot (noyau pur
/// <c>scripts/ui</c>, sans runtime Godot). Les données sont lues
/// directement via le symlink <c>raw_assets/</c> à la racine du
/// portage : <c>tools/NetTests/bin/... → ../../../raw_assets</c> en
/// remontant jusqu'à trouver <c>raw_assets/uidata</c> ; surcharge
/// possible via la variable d'env <c>EDEN_RAW_ASSETS</c>.
/// </summary>
public class CeguiTests
{
    private readonly ITestOutputHelper _out;

    public CeguiTests(ITestOutputHelper output) => _out = output;

    /// <summary>Répertoire <c>raw_assets</c> du portage.</summary>
    private static string RawAssets()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env))
            return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName,
                    "raw_assets", "uidata")))
                return Path.Combine(dir.FullName, "raw_assets");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "raw_assets/uidata introuvable en remontant depuis " +
            AppContext.BaseDirectory);
    }

    private static string UIData() => Path.Combine(RawAssets(), "uidata");

    private static readonly Lazy<CeguiContext> Ctx = new(() =>
        CeguiContext.Load(UIData()));

    // --------------------------------------------------------------

    [Fact]
    public void Imageset_Parse_Regions()
    {
        var set = CeguiImageset.Parse(
            Path.Combine(UIData(), "imagesets", "DejaVu.imageset"));
        Assert.Equal("DejaVu", set.Name);
        Assert.Equal("DejaVu.png", set.ImageFile);
        Assert.True(set.Regions.Count > 50);

        var round = set.Find("Round");
        Assert.NotNull(round);
        Assert.Equal(168, round!.Value.X);
        Assert.Equal(6, round.Value.Y);
        Assert.Equal(64, round.Value.W);
        Assert.Equal(64, round.Value.H);

        Assert.Equal("res://ui_textures/imagesets/DejaVu.png",
            set.TextureResourcePath);
    }

    [Fact]
    public void Scheme_Parse_Mappings()
    {
        var scheme = CeguiScheme.Parse(
            Path.Combine(UIData(), "schemes", "DejaVu.scheme"));
        Assert.Equal("DejaVu", scheme.Name);
        Assert.True(scheme.Mappings.Count >= 60);
        Assert.Equal("DejaVu.looknfeel", scheme.LookNFeelFile);
        Assert.Contains("DejaVu", scheme.Imagesets.Keys);
        Assert.Contains("F001-9", scheme.Fonts.Keys);

        var btn = scheme.Resolve("DejaVu/Button");
        Assert.NotNull(btn);
        Assert.Equal("CEGUI/PushButton", btn!.TargetType);
        Assert.Equal("DejaVu/Button", btn.LookNFeel);

        var fw = scheme.Resolve("DejaVu/FrameWindow");
        Assert.Equal("CEGUI/FrameWindow", fw!.TargetType);

        // Type non mappé.
        Assert.Null(scheme.Resolve("DefaultWindow"));
    }

    [Fact]
    public void LookNFeel_Parse_WidgetLooks()
    {
        var lnf = CeguiLookNFeel.Parse(
            Path.Combine(UIData(), "looknfeel", "DejaVu.looknfeel"));
        Assert.True(lnf.Looks.Count >= 100);

        var button = lnf.GetLook("DejaVu/Button");
        Assert.NotNull(button);
        // État Normal → section "normal" → FrameComponent 3 images.
        Assert.True(button!.States.ContainsKey("Normal"));
        var normal = button.States["Normal"][0].Sections
            .Select(s => lnf.ResolveSection(button, s)).ToList();
        var frame = normal.SelectMany(s => s!.Frames).First();
        Assert.NotNull(frame.ImageFor("Background"));
        Assert.Equal("ButtonMiddleNormal",
            frame.ImageFor("Background")!.Value.Image);
        Assert.NotNull(frame.ImageFor("LeftEdge"));
        Assert.NotNull(frame.ImageFor("RightEdge"));

        // Couleur de texte déclarée via PropertyDefinition.
        Assert.Equal("C8000000", button.GetDefault("NormalTextColour"));

        // Les constructions non implémentées sont comptées, pas perdues.
        Assert.True(lnf.Ignored.Count > 0);
        Assert.True(lnf.Ignored.ContainsKey("NamedArea"));
    }

    [Fact]
    public void FontTable_Sizes()
    {
        var fonts = new CeguiFontTable();
        fonts.LoadDirectory(Path.Combine(UIData(), "fonts"));
        Assert.Equal(9, fonts.GetSize("F001-9"));
        Assert.Equal(12, fonts.GetSize("F001-12"));
        Assert.Equal(24, fonts.GetSize("F001-24"));
        // Heuristique suffixe pour un nom inconnu du registre.
        Assert.Equal(18, fonts.GetSize("F999-18"));
        Assert.Null(fonts.GetSize("F001-inconnu"));
        Assert.Null(fonts.GetSize(null));
    }

    [Fact]
    public void UnifiedAreaRect_Anchors_Simple()
    {
        // {{0,0},{0,0},{0,593},{0,485}} — coin haut-gauche, taille fixe.
        var r = UnifiedRect.Parse("{{0,0},{0,0},{0,593},{0,485}}")!.Value;
        var a = CeguiAnchors.FromUnified(r);
        Assert.Equal(0f, a.AnchorLeft);
        Assert.Equal(0f, a.OffsetLeft);
        Assert.Equal(0f, a.AnchorRight);
        Assert.Equal(593f, a.OffsetRight);
        Assert.Equal(485f, a.OffsetBottom);
    }

    [Fact]
    public void UnifiedAreaRect_Anchors_Centre()
    {
        // Même rect centré : décalage de (parent − size)/2 sur les
        // deux bords (CoordConverter::getBaseXValue natif).
        var r = UnifiedRect.Parse("{{0,0},{0,0},{0,593},{0,485}}")!.Value;
        var a = CeguiAnchors.FromUnified(r, "Centre", "Centre");
        Assert.Equal(0.5f, a.AnchorLeft, 3);
        Assert.Equal(-296.5f, a.OffsetLeft, 3);
        Assert.Equal(0.5f, a.AnchorRight, 3);
        Assert.Equal(296.5f, a.OffsetRight, 3);
        Assert.Equal(0.5f, a.AnchorTop, 3);
        Assert.Equal(-242.5f, a.OffsetTop, 3);
        Assert.Equal(242.5f, a.OffsetBottom, 3);
    }

    [Fact]
    public void UnifiedAreaRect_Anchors_BordsDroiteEtBas()
    {
        // ChannelWnd : {{0,0},{1,-308},{0,302},{1,-198}} — les 3e/4e
        // paires sont les BORDS droit/bas : fenêtre 302 px de large en
        // haut-gauche… ici bas-gauche (top = 1·H − 308, bas =
        // 1·H − 198 → 110 px de haut).
        var r = UnifiedRect.Parse("{{0,0},{1,-308},{0,302},{1,-198}}")!
            .Value;
        var a = CeguiAnchors.FromUnified(r);
        Assert.Equal(0f, a.AnchorLeft);
        Assert.Equal(1f, a.AnchorTop);
        Assert.Equal(-308f, a.OffsetTop);
        Assert.Equal(1f, a.AnchorBottom);
        Assert.Equal(-198f, a.OffsetBottom);
        Assert.Equal(0f, a.AnchorRight);
        Assert.Equal(302f, a.OffsetRight);
    }

    [Fact]
    public void UnifiedAreaRect_Anchors_Right()
    {
        // {{0,0},{0,0},{0.5,10},{0.5,0}} : bords 0 → 50 %W+10.
        // Align Right : décalage de parent − size = (0.5, −10) appliqué
        // aux deux bords → le rect couvre [50 %W, 100 %W].
        var r = UnifiedRect.Parse("{{0,0},{0,0},{0.5,10},{0.5,0}}")!.Value;
        var a = CeguiAnchors.FromUnified(r, "Right");
        Assert.Equal(0.5f, a.AnchorLeft, 3);
        Assert.Equal(-10f, a.OffsetLeft, 3);
        Assert.Equal(1f, a.AnchorRight, 3);
        Assert.Equal(0f, a.OffsetRight, 3);
    }

    [Fact]
    public void UnifiedAreaRect_Anchors_ElementDocke()
    {
        // Preuve sémantique « bords » : {{1,-72},{0.5,0},{1,-38},
        // {0.5,34}} = élément de 34 px docké au bord droit, centré
        // verticalement (pattern scrollbars/boutons des layouts).
        var r = UnifiedRect.Parse("{{1,-72},{0.5,0},{1,-38},{0.5,34}}")!
            .Value;
        var a = CeguiAnchors.FromUnified(r);
        Assert.Equal(1f, a.AnchorLeft);
        Assert.Equal(-72f, a.OffsetLeft);
        Assert.Equal(1f, a.AnchorRight);
        Assert.Equal(-38f, a.OffsetRight);
        Assert.Equal(0.5f, a.AnchorTop);
        Assert.Equal(0.5f, a.AnchorBottom);
        Assert.Equal(34f, a.OffsetBottom);
    }

    // --------------------------------------------------------------

    [Fact]
    public void Layout_Parse_Achievement()
    {
        var root = CeguiLayout.ParseFile(
            Path.Combine(UIData(), "layouts", "Achievement.layout"));
        Assert.Equal("DejaVu/FrameWindow", root.Type);
        Assert.Equal("Root/Achievement", root.Name);
        Assert.Equal(5, root.Children.Count);
        Assert.Equal("DejaVu/Static", root.Children[0].Type);

        var tree = root.Children[0].Children
            .First(c => c.Name.EndsWith("TreeList"));
        Assert.Equal("DejaVu/Tree", tree.Type);

        var point = root.Children[0].Children
            .First(c => c.Name.EndsWith("AchiPoint"));
        Assert.Contains("99999999", point.Get("Text"));
        Assert.Equal("False", point.Get("FrameEnabled"));
    }

    [Fact]
    public void BuildSpec_Achievement()
    {
        var builder = new CeguiBuilder(Ctx.Value);
        var spec = builder.BuildSpec(CeguiLayout.ParseFile(
            Path.Combine(UIData(), "layouts", "Achievement.layout")));

        Assert.Equal(UiControlKind.Panel, spec.Kind);   // FrameWindow
        Assert.True(spec.HasTitleBar);
        Assert.Equal(5, spec.Children.Count);
        // SanitizeName : '/' → '_'.
        Assert.Equal("Root_Achievement", spec.Name);

        var left = spec.Children[0];
        Assert.Equal(UiControlKind.Panel, left.Kind); // DejaVu/Static
        var tree = left.Children.First(c => c.Name.EndsWith("TreeList"));
        Assert.Equal(UiControlKind.Tree, tree.Kind);
        var label = left.Children.First(c => c.Name.EndsWith("AchiPoint"));
        Assert.Equal(UiControlKind.Label, label.Kind);
        Assert.Contains("99999999", label.Text);
    }

    [Fact]
    public void BuildSpec_Login()
    {
        var builder = new CeguiBuilder(Ctx.Value);
        var spec = builder.BuildSpec(CeguiLayout.ParseFile(
            Path.Combine(UIData(), "layouts", "Login.layout")));

        Assert.Equal(UiControlKind.Control, spec.Kind); // DefaultWindow
        var edits = Flatten(spec)
            .Where(n => n.Kind == UiControlKind.LineEdit).ToList();
        Assert.True(edits.Count >= 2);
        // PasswordEdit : MaskText=True.
        Assert.Contains(edits, e => e.Secret);
    }

    [Fact]
    public void BuildSpec_ChannelWnd_AnchoredBottom()
    {
        var builder = new CeguiBuilder(Ctx.Value);
        var spec = builder.BuildSpec(CeguiLayout.ParseFile(
            Path.Combine(UIData(), "layouts", "ChannelWnd.layout")));
        // Fenêtre ancrée bas : ancre top = 1.
        var wnd = Flatten(spec).First(n => n.Name.EndsWith("_Wnd1"));
        Assert.Equal(1f, wnd.Anchors.AnchorTop);
        // AutoWindow → Control.
        var auto = Flatten(spec).FirstOrDefault(
            n => n.CeguiType == "__auto__");
        Assert.NotNull(auto);
        Assert.Equal(UiControlKind.Control, auto!.Kind);
    }

    private static IEnumerable<UiNodeSpec> Flatten(UiNodeSpec n)
    {
        yield return n;
        foreach (var c in n.AllChildren())
        foreach (var d in Flatten(c))
            yield return d;
    }

    [Fact(Skip = "Layout du mod Reforged, hors corpus EDENFR (ADR-0002)")]
    public void Layout_Parse_LimitlessHud()
    {
        // Sous-dossier du mod Reforged — commentaire XML entre la
        // déclaration et la racine (autrefois « parse → null »).
        var root = CeguiLayout.ParseFile(Path.Combine(
            UIData(), "layouts", "limitless", "LimitlessHud.layout"));
        Assert.Equal("DejaVu/BuffWindow", root.Type);
        Assert.Equal("LimitlessHud", root.Name);
        Assert.True(root.Children.Count >= 2);
    }

    [Fact]
    public void Layout_Parse_CommentBeforeDeclaration()
    {
        // Commentaire AVANT <?xml?> — illégal en XML strict : le
        // parseur doit tolérer en retirant la déclaration.
        const string text =
            "<!-- note du mod -->\n" +
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<GUILayout ><Window Type=\"DefaultWindow\" " +
            "Name=\"Root/X\" /></GUILayout>";
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, text);
            var root = CeguiLayout.ParseFile(tmp);
            Assert.Equal("Root/X", root.Name);
        }
        finally { File.Delete(tmp); }
    }

    // --------------------------------------------------------------

    /// <summary>Layouts fr-full en XML mal formé (relevé 2026-11-02).</summary>
    private static readonly string[] KnownMalformedFr = [];  // authentique : plus aucun (2026-11-02)

    /// <summary>Smoke : les layouts doivent se parser et se
    /// construire sans exception — y compris les sous-dossiers
    /// (layouts/limitless/*.layout du mod Reforged). Taux de succès
    /// exigé ≥ 95 % (fr-full), sans échec hors
    /// <see cref="KnownMalformedFr"/>.</summary>
    [Fact]
    public void Smoke_AllLayouts()
    {
        var ctx = Ctx.Value;
        var builder = new CeguiBuilder(ctx);
        var dir = Path.Combine(UIData(), "layouts");
        var files = Directory.GetFiles(dir, "*.layout",
            SearchOption.AllDirectories);
        Assert.True(files.Length > 400);

        int nodes = 0;
        var failures = new List<string>();
        foreach (var f in files)
        {
            var before = builder.Report.LayoutsFailed;
            var spec = builder.BuildLayoutFile(f);
            if (builder.Report.LayoutsFailed > before)
                failures.Add(Path.GetFileName(f));
            if (spec != null)
                nodes += Flatten(spec).Count();
        }

        var rep = builder.Report;
        _out.WriteLine($"layouts: {rep.LayoutsOk} ok / " +
                       $"{rep.LayoutsFailed} ko, specs={nodes} nœuds");
        foreach (var e in rep.Errors)
            _out.WriteLine($"  ERR {e}");
        _out.WriteLine("types inconnus : " +
            string.Join(", ", rep.UnknownTypes
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}×{kv.Value}")));
        _out.WriteLine("props ignorées top : " +
            string.Join(", ", rep.IgnoredProps
                .OrderByDescending(kv => kv.Value).Take(15)
                .Select(kv => $"{kv.Key}×{kv.Value}")));
        _out.WriteLine("lnf ignoré : " +
            string.Join(", ", ctx.LookNFeel.Ignored
                .OrderByDescending(kv => kv.Value).Take(10)
                .Select(kv => $"{kv.Key}×{kv.Value}")));

        // fr-full : 19 layouts natifs sont du XML mal formé (attributs
        // non quotés, « < » dans les valeurs…) — lacune connue d'un
        // parseur tolérant (backlog). Garde-fou : la liste ne grossit pas.
        var regressions = failures.Except(KnownMalformedFr,
            StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(regressions.Count == 0,
            $"{regressions.Count} nouveaux layouts en échec : " +
            string.Join(", ", regressions.Take(10)));
        Assert.True((double)rep.LayoutsOk / files.Length >= 0.95);
        Assert.True(nodes > 5000);
    }
}
