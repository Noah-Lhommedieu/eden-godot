using Godot;

namespace EdenClient.Ui;

/// <summary>
/// Cache de textures d'UI : charge les atlas PNG copiés par
/// <c>tools/textures/copy_ui_png.py</c> dans
/// <c>res://ui_textures/imagesets/</c>. Repli sur
/// <c>Image.LoadFromFile</c> vers <c>raw_assets</c> si l'import Godot
/// n'a pas encore eu lieu (le dossier vit hors <c>assets/</c> qui est
/// <c>.gdignore</c>'d, mais l'import peut être en retard).
/// </summary>
public sealed class UiTextureCache
{
    private readonly CeguiImagesetCache _sets;
    private readonly Dictionary<string, Texture2D?> _atlases = new();
    private readonly Dictionary<(string, string), AtlasTexture?> _regions = new();
    private readonly Dictionary<string, Image?> _atlasImages = new();
    private readonly Dictionary<string, (UiStyleSpec St,
        StyleBoxTexture? Sb)> _composed = new();
    private string _theme = "";

    public UiTextureCache(CeguiImagesetCache sets) => _sets = sets;

    /// <summary>Thème persisté dans client_settings.cfg
    /// (<see cref="Settings.ClientSettings.Theme"/>).</summary>
    public static string SavedTheme() =>
        Settings.ClientSettings.Load().Theme;

    /// <summary>Thème actif ("" = atlas de base). Les variantes vivent
    /// dans <c>ui_textures/imagesets/&lt;Theme&gt;/</c> et
    /// <c>ui_textures/themes/&lt;Theme&gt;/</c> — mêmes fichiers que
    /// l'atlas de base, recolorés, géométrie identique (régions
    /// réutilisables telles quelles).</summary>
    public string Theme => _theme;

    /// <summary>Change le thème : vide les caches et re-mappe les
    /// AtlasTexture déjà matérialisées sur le nouvel atlas — les
    /// régions gardent leurs rects (thèmes = même layout), donc le
    /// swap est visible sans reconstruire les fenêtres pour ce qui
    /// est TextureButton/TextureRect/NinePatchRect. Les nine-patch
    /// composés des panneaux se rafraîchissent à leur reconstruction.
    /// Retourne true si au moins un dossier de thème existe.</summary>
    public bool SetTheme(string theme)
    {
        theme = (theme ?? "").Trim();
        if (theme == _theme) return HasTheme;
        _theme = theme;
        _atlases.Clear();
        _atlasImages.Clear();
        // Re-mappe aussi vers l'atlas de base (thème Irongray / "").
        foreach (var (key, at) in _regions)
            if (at != null) at.Atlas = GetAtlas(key.Item1);
        // Les neuf-patchs déjà émis sont des ImageTexture figées, pas
        // des AtlasTexture : re-composer la texture de chaque
        // StyleBoxTexture en place — tous les contrôles qui la
        // partagent changent de peau sans être reconstruits.
        var recooked = 0;
        foreach (var (st, sb) in _composed.Values.ToArray())
            if (sb != null &&
                ComposeNinePatchUncached(st)?.Texture is { } t2)
            {
                sb.Texture = t2;
                recooked++;
            }
        GD.Print($"[theme] {theme} — dirs={string.Join(",", ThemeDirs())} " +
                 $"regions={_regions.Count} composed={_composed.Count} " +
                 $"recooked={recooked}");
        return HasTheme;
    }

    /// <summary>true si un dossier de thème résolu existe.</summary>
    public bool HasTheme => ThemeDirs().Any();

    /// <summary>Dossiers candidats du thème, ordre de préférence :
    /// imagesets/&lt;T&gt;/ puis themes/&lt;T&gt;/ (contenus identiques
    /// pour les thèmes présents aux deux endroits).</summary>
    private IEnumerable<string> ThemeDirs()
    {
        if (_theme.Length == 0) yield break;
        // imagesets/purple est en minuscules, themes/Purple capitalisé.
        // imagesets/ (EDENFR) avant themes/ (G8 : origine non EDENFR).
        foreach (var root in new[] { "imagesets" })
        {
            foreach (var dir in new[]
            {
                _theme, _theme.ToLowerInvariant(),
                char.ToUpperInvariant(_theme[0]) + _theme[1..],
            })
            {
                var res = $"res://ui_textures/{root}/{dir}";
                if (DirAccess.DirExistsAbsolute(res))
                    yield return $"{root}/{dir}";
            }
        }
    }

    /// <summary>Chemin res:// thémé pour un fichier d'atlas, ou null
    /// si aucune variante n'existe pour ce fichier.</summary>
    private string? ThemedResPath(string imageFile)
    {
        foreach (var dir in ThemeDirs())
        {
            var p = $"res://ui_textures/{dir}/{imageFile}";
            if (Godot.FileAccess.FileExists(p))
                return p;
        }
        return null;
    }

    /// <summary>Texture complète d'un imageset (null si PNG absent).</summary>
    public Texture2D? GetAtlas(string setName)
    {
        if (_atlases.TryGetValue(setName, out var cached))
            return cached;
        Texture2D? tex = null;
        var set = _sets.Get(setName);
        if (set != null) tex = LoadFromVfs(setName, set.ImageFile);
        if (tex == null && set != null && Vfs.EdenVfs.LooseAllowed)
        {
            var resPath = set.TextureResourcePath;
            var themed = ThemedResPath(set.ImageFile);
            if (themed != null && Godot.FileAccess.FileExists(themed))
                resPath = themed;
            if (ResourceLoader.Exists(resPath))
            {
                tex = GD.Load<Texture2D>(resPath);
            }
            else
            {
                // Pas encore importé : lecture directe du PNG.
                var fs = ProjectSettings.GlobalizePath(resPath);
                if (!File.Exists(fs))
                {
                    // Repli : racines de données (gamedata du package,
                    // raw_assets en dev…). imagesets vit sous uidata/,
                    // themes/ à la racine.
                    var rel = resPath["res://ui_textures/".Length..];
                    fs = DataPaths.ResolveFile(
                        rel.StartsWith("imagesets/")
                            ? $"uidata/{rel}" : rel)
                        ?? DataPaths.ResolveFile(
                            $"uidata/imagesets/{set.ImageFile}") ?? fs;
                }
                // Loose ou URI epk:// — LoadImage gère les deux.
                var img = Vfs.EdenVfs.LoadImage(fs);
                if (img != null)
                {
                    AssetCatalog.TallyTexBytesPublic(
                        $"ui/{setName}", img);
                    tex = ImageTexture.CreateFromImage(img);
                }
            }
        }
        _atlases[setName] = tex;
        return tex;
    }

    /// <summary>Atlas lu via le VFS : variante du thème (imagesets/&lt;T&gt;/ puis
    /// themes/&lt;T&gt;/) puis uidata/imagesets/.</summary>
    private Texture2D? LoadFromVfs(string setName, string imageFile)
    {
        var cands = new List<string>();
        if (_theme.Length > 0)
            foreach (var dir in new[]
            {
                _theme, _theme.ToLowerInvariant(),
                char.ToUpperInvariant(_theme[0]) + _theme[1..],
            })
            {
                // Thèmes du propriétaire (themes/, gamedata overlay) d'abord ; sinon thème EDENFR.
                cands.Add($"themes/{dir}/{imageFile}");
                cands.Add($"uidata/imagesets/{dir}/{imageFile}");
            }
        cands.Add($"uipatch/{imageFile}");
        cands.Add($"uidata/imagesets/{imageFile}");
        foreach (var c in cands)
        {
            var path = DataPaths.ResolveFile(c);
            if (path == null) continue;
            var img = Vfs.EdenVfs.LoadImage(path);
            if (img == null) continue;
            if (System.Environment.GetEnvironmentVariable("EDEN_UI_TRACE") == "1") GD.Print($"[ui-atlas] {setName} <- {c}");
            AssetCatalog.TallyTexBytesPublic($"ui/{setName}", img);
            return ImageTexture.CreateFromImage(img);
        }
        return null;
    }

    /// <summary>AtlasTexture pour une région nommée d'un imageset.</summary>
    public AtlasTexture? GetRegion(CeguiImageRef? r)
    {
        if (r is not { } v)
            return null;
        var key = (v.Set, v.Image);
        if (_regions.TryGetValue(key, out var cached))
            return cached;
        AtlasTexture? at = null;
        var reg = _sets.Resolve(v);
        var atlas = GetAtlas(v.Set);
        if (reg is { } rr && atlas != null)
        {
            at = new AtlasTexture
            {
                Atlas = atlas,
                Region = new Rect2(rr.X, rr.Y, rr.W, rr.H),
            };
        }
        _regions[key] = at;
        return at;
    }

    /// <summary>Pixel central d'une région nommée de l'atlas courant
    /// (thème déjà appliqué) — sert à dériver les couleurs des surfaces
    /// hand-built (<see cref="EdenClient.Hud.ThemePalette"/>).
    /// Null si la région ou l'image ne se résout pas.</summary>
    public Color? SamplePixel(CeguiImageRef r)
    {
        var reg = _sets.Resolve(r);
        var img = GetAtlasImage(r.Set);
        if (reg is not { } rr || img == null || rr.W <= 0 || rr.H <= 0)
            return null;
        var x = Math.Clamp(rr.X + rr.W / 2, 0, img.GetWidth() - 1);
        var y = Math.Clamp(rr.Y + rr.H / 2, 0, img.GetHeight() - 1);
        return img.GetPixel(x, y);
    }

    /// <summary>L'image CPU de l'atlas (pour le compositing
    /// nine-patch). Null si l'atlas n'est pas chargeable.</summary>
    public Image? GetAtlasImage(string setName)
    {
        if (_atlasImages.TryGetValue(setName, out var cached))
            return cached;
        var img = GetAtlas(setName)?.GetImage();
        if (img != null && img.GetFormat() != Image.Format.Rgba8)
            img.Convert(Image.Format.Rgba8);
        _atlasImages[setName] = img;
        return img;
    }

    /// <summary>Synthétise une texture nine-patch à partir des parts
    /// extraites du looknfeel (coins/bords/centre — chaque région
    /// d'atlas est étirée dans sa cellule, la frame résultante est
    /// jouée par <see cref="StyleBoxTexture"/> avec les marges des
    /// bords). Cache par combinaison de parts.</summary>
    public StyleBoxTexture? ComposeNinePatch(UiStyleSpec st)
    {
        if (st.Parts.Count == 0)
            return null;
        var key = string.Join("|", st.Parts
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{(int)kv.Key}:{kv.Value.Set}:{kv.Value.Image}"));
        if (st.Colour != 0)
            key += $"#{st.Colour:X8}";
        if (_composed.TryGetValue(key, out var cached))
            return cached.Sb;

        var result = ComposeNinePatchUncached(st);
        _composed[key] = (st, result);
        return result;
    }

    private StyleBoxTexture? ComposeNinePatchUncached(UiStyleSpec st)
    {
        // Résolution des régions + images sources par part.
        var src = new Dictionary<UiPart, (Image img, Rect2I rect)>();
        foreach (var (part, r) in st.Parts)
        {
            var reg = _sets.Resolve(r);
            var img = GetAtlasImage(r.Set);
            if (reg is { } rr && img != null)
                src[part] = (img, new Rect2I(rr.X, rr.Y, rr.W, rr.H));
        }
        if (src.Count == 0)
            return null;

        // Marges = taille max des images de chaque bord.
        int ML = Math.Max(W(UiPart.TopLeft),
            Math.Max(W(UiPart.Left), W(UiPart.BottomLeft)));
        int MR = Math.Max(W(UiPart.TopRight),
            Math.Max(W(UiPart.Right), W(UiPart.BottomRight)));
        int MT = Math.Max(H(UiPart.TopLeft),
            Math.Max(H(UiPart.Top), H(UiPart.TopRight)));
        int MB = Math.Max(H(UiPart.BottomLeft),
            Math.Max(H(UiPart.Bottom), H(UiPart.BottomRight)));
        // Centre : taille naturelle bornée (stretched ensuite).
        int cw = src.TryGetValue(UiPart.Center, out var c)
            ? Math.Clamp(c.rect.Size.X, 4, 64) : 8;
        int ch = src.TryGetValue(UiPart.Center, out c)
            ? Math.Clamp(c.rect.Size.Y, 4, 64) : 8;
        if (ML + MR <= 0 && MT + MB <= 0)
            return null;    // centre seul → pas une frame

        var dst = Image.CreateEmpty(ML + cw + MR, MT + ch + MB,
            false, Image.Format.Rgba8);
        void Blit(UiPart p, int dx, int dy, int dw, int dh)
        {
            if (!src.TryGetValue(p, out var s) || dw <= 0 || dh <= 0)
                return;
            var sub = s.img.GetRegion(s.rect);
            if (sub.GetWidth() != dw || sub.GetHeight() != dh)
                sub.Resize(dw, dh, Image.Interpolation.Bilinear);
            dst.BlitRect(sub, new Rect2I(0, 0, dw, dh),
                new Vector2I(dx, dy));
        }
        Blit(UiPart.TopLeft, 0, 0, ML, MT);
        Blit(UiPart.Top, ML, 0, cw, MT);
        Blit(UiPart.TopRight, ML + cw, 0, MR, MT);
        Blit(UiPart.Left, 0, MT, ML, ch);
        Blit(UiPart.Center, ML, MT, cw, ch);
        Blit(UiPart.Right, ML + cw, MT, MR, ch);
        Blit(UiPart.BottomLeft, 0, MT + ch, ML, MB);
        Blit(UiPart.Bottom, ML, MT + ch, cw, MB);
        Blit(UiPart.BottomRight, ML + cw, MT + ch, MR, MB);

        var sb = new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(dst),
            TextureMarginLeft = ML,
            TextureMarginRight = MR,
            TextureMarginTop = MT,
            TextureMarginBottom = MB,
            DrawCenter = src.ContainsKey(UiPart.Center),
        };
        if (st.Colour != 0)
            sb.ModulateColor = CeguiToGodot.GodotColor(st.Colour);
        return sb;

        int W(UiPart p) => src.TryGetValue(p, out var s)
            ? s.rect.Size.X : 0;
        int H(UiPart p) => src.TryGetValue(p, out var s)
            ? s.rect.Size.Y : 0;
    }
}

/// <summary>Barre de titre draggable générée pour les FrameWindow.
/// Panel (pas PanelContainer) pour positionner librement label et
/// bouton de fermeture.</summary>
public partial class CeguiTitleBar : Panel
{
    // Drag ownership lives in DragHelper.HudWindowDrag, which covers
    // title bars and decorative frame areas through one event path.
    // Keeping a second per-titlebar _Process drag fed by the OS cursor
    // raced injected/input-test motion and reset windows on release.
}

/// <summary>
/// Matérialisation Godot des <see cref="UiNodeSpec"/> produits par
/// <see cref="CeguiBuilder"/>. Toute la logique de parsing/mapping
/// testable vit dans le noyau pur ; ici on ne fait qu'instancier des
/// Controls et poser ancres/styles/textures.
/// </summary>
public static class CeguiToGodot
{
    /// <summary>Charge un .layout et construit l'arbre de Controls.</summary>
    public static Control? BuildLayoutFile(string path, CeguiContext ctx,
        UiTextureCache textures)
    {
        var builder = new CeguiBuilder(ctx);
        var spec = builder.BuildLayoutFile(path);
        return spec == null ? null : BuildTree(spec, textures);
    }

    /// <summary>Construit un arbre depuis un nœud de layout parsé.</summary>
    public static Control BuildTree(CeguiNode root, CeguiContext ctx,
        UiTextureCache textures)
    {
        var spec = new CeguiBuilder(ctx).BuildSpec(root);
        return BuildTree(spec, textures);
    }

    /// <summary>Instancie récursivement l'arbre de spec.</summary>
    public static Control BuildTree(UiNodeSpec spec, UiTextureCache textures)
    {
        var c = Create(spec, textures);
        ApplyCommon(spec, c, textures);
        foreach (var childSpec in spec.Children)
            c.AddChild(BuildTree(childSpec, textures));
        // La barre de titre auto est posée dans ApplyCommon AVANT les
        // enfants : un enfant plein cadre (Backgrand Static → Panel,
        // MouseFilter.Stop) la recouvrirait et avalerait les clics —
        // elle repasse en dernier enfant (dessinée + hit-testée en
        // premier par l'ordre inverse du rendu).
        var tb = c.GetNodeOrNull<Control>("__auto_titlebar__");
        if (tb != null && c.GetChildCount() > 1)
            c.MoveChild(tb, c.GetChildCount() - 1);
        // Enfants « hors onglets » d'un TabContainer : conteneur
        // satellite plein-format sous les panes.
        if (spec.Kind == UiControlKind.TabContainer &&
            spec.SideChildren.Count > 0)
        {
            var side = new Control { Name = "__side" };
            side.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            foreach (var s in spec.SideChildren)
                side.AddChild(BuildTree(s, textures));
            c.AddChild(side);
            side.MoveToFront();
        }
        return c;
    }

    // -------------------- instanciation ---------------------------

    private static Control Create(UiNodeSpec s, UiTextureCache tex)
    {
        Control c = s.Kind switch
        {
            // Panel « transparent » par défaut : le thème Godot peint
            // un fond sombre, alors qu'un Static/DefaultWindow CEGUI
            // sans FrameEnabled/BackgroundEnabled ne dessine rien —
            // sinon un conteneur plein écran (ex. Root/ChannelWnd)
            // masque tout le viewport 3D.
            UiControlKind.Panel => EmptyPanel(),
            UiControlKind.Label => new Label(),
            UiControlKind.RichLabel => new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
            },
            UiControlKind.Button => new Button { Text = s.Text ?? "" },
            UiControlKind.TextureButton => new TextureButton
            {
                TextureNormal = tex.GetRegion(s.Image),
                TextureHover = tex.GetRegion(s.HoverImage),
                TexturePressed = tex.GetRegion(s.PushedImage),
                TextureDisabled = tex.GetRegion(s.DisabledImage),
                StretchMode = TextureButton.StretchModeEnum.Scale,
            },
            UiControlKind.CheckBox => new CheckBox { Text = s.Text ?? "" },
            UiControlKind.LineEdit => new LineEdit
            {
                Text = s.Text ?? "",
                Secret = s.Secret,
                Editable = !s.ReadOnly,
                MaxLength = s.MaxLength > 0 ? s.MaxLength : 0,
            },
            UiControlKind.TextEdit => new TextEdit
            {
                Text = s.Text ?? "",
                Editable = !s.ReadOnly,
            },
            UiControlKind.ItemList => new ItemList(),
            UiControlKind.Tree => new Tree
            {
                Columns = Math.Max(s.Columns, 1),
                HideRoot = true,
            },
            // ShowPercentage Godot par défaut superposait « NN% » au
            // label texte natif (HP « 2366/2364 » doublé).
            UiControlKind.ProgressBar => new ProgressBar
            {
                MinValue = 0, MaxValue = 1, Value = s.Progress,
                ShowPercentage = false,
            },
            UiControlKind.HSlider => new HSlider(),
            UiControlKind.VSlider => new VSlider(),
            UiControlKind.VScrollBar => new VScrollBar(),
            UiControlKind.HScrollBar => new HScrollBar(),
            UiControlKind.ScrollContainer => new ScrollContainer(),
            UiControlKind.TabContainer => new TabContainer(),
            UiControlKind.OptionButton => new OptionButton(),
            UiControlKind.MenuBar => new MenuBar(),
            UiControlKind.SpinBox => new SpinBox(),
            UiControlKind.TextureRect => new TextureRect
            {
                Texture = tex.GetRegion(s.Image),
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            },
            UiControlKind.NinePatchRect => new NinePatchRect
            {
                Texture = tex.GetRegion(s.Image),
            },
            UiControlKind.ColorRect => new ColorRect
            {
                Color = GodotColor(s.Background?.Colour ?? 0x00000000),
            },
            UiControlKind.SubViewportContainer => new SubViewportContainer(),
            _ => new Control(),
        };

        // Un bouton cliqué ne garde pas le focus clavier (sinon Espace =
        // ui_accept qui re-déclenche le bouton + saut). Les champs de
        // saisie/listes gardent le focus par défaut.
        if (c is BaseButton)
            c.FocusMode = Control.FocusModeEnum.None;

        // Control nu avec un fond couleur → Panel stylé.
        if (c is Control && s.Kind == UiControlKind.Control &&
            s.Background is { Kind: UiStyleKind.Colour })
        {
            var p = EmptyPanel();
            c = p;
        }
        return c;
    }

    /// <summary>Panel à fond vide — <see cref="ApplyPanelStyle"/>
    /// remplacera le stylebox si le look fournit frame/background.</summary>
    private static Panel EmptyPanel()
    {
        var p = new Panel();
        p.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        return p;
    }

    // ------------------- propriétés communes -----------------------

    private static void ApplyCommon(UiNodeSpec s, Control c,
        UiTextureCache tex)
    {
        c.Name = string.IsNullOrEmpty(s.Name) ? "cegui" : s.Name;
        var a = s.Anchors;
        c.AnchorLeft = a.AnchorLeft;
        c.AnchorRight = a.AnchorRight;
        c.AnchorTop = a.AnchorTop;
        c.AnchorBottom = a.AnchorBottom;
        c.OffsetLeft = a.OffsetLeft;
        c.OffsetRight = a.OffsetRight;
        c.OffsetTop = a.OffsetTop;
        c.OffsetBottom = a.OffsetBottom;

        c.Visible = s.Visible;
        // NB : AlwaysOnTop CEGUI n'est PAS mappé sur TopLevel — en
        // Godot un Control TopLevel ignore le rect du parent et ses
        // ancres se résolvent sur le viewport (ex. Root/Radar/
        // BackGrand recouvrait tout l'écran). AlwaysOnTop n'est qu'un
        // indice de z-order dans CEGUI — on l'ignore volontairement.
        if (s.MouseIgnore)
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (s.Alpha < 1f)
            c.Modulate = new Color(1, 1, 1, s.Alpha);
        if (s.Tooltip != null)
            c.TooltipText = s.Tooltip;
        if (s.MinW > 0 || s.MinH > 0)
            c.CustomMinimumSize = new Vector2(s.MinW, s.MinH);
        // UnifiedMaxSize CEGUI = borne haute. Godot n'a pas de
        // CustomMaximumSize : la Size n'est libre que si les ancres
        // opposées sont égales — avec des ancres non égales c'est le
        // parent qui dimensionne et l'affectation déclenche le warning
        // moteur « non-equal opposite anchors » (ex. ZoneMapUI.layout
        // AlphaSilderBar : max 194×15 avec rect scale). La borne est
        // alors ignorée — le layout natif borne rarement la taille
        // réelle à l'écran.
        if (s.MaxW > 0 && Mathf.IsEqualApprox(a.AnchorLeft, a.AnchorRight))
            c.Size = new Vector2(s.MaxW, c.Size.Y);
        if (s.MaxH > 0 && Mathf.IsEqualApprox(a.AnchorTop, a.AnchorBottom))
            c.Size = new Vector2(c.Size.X, s.MaxH);

        c.SetMeta("cegui_type", s.CeguiType);
        if (s.TargetType != null)
            c.SetMeta("cegui_target", s.TargetType);

        ApplyText(s, c);
        ApplyStyles(s, c, tex);

        if (s.HasTitleBar)
            c.AddChild(MakeTitleBar(s, tex));
        else if (s.Text != null && c is Control &&
                 c is not Label and not RichTextLabel and not Button
                 and not LineEdit and not TextEdit and not CheckBox)
        {
            // Static/Panel avec texte : label fils plein-format.
            var l = new Label { Text = s.Text, Name = "__text" };
            l.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            l.HorizontalAlignment = s.HorzAlign switch
            {
                UiTextAlign.Centre => HorizontalAlignment.Center,
                UiTextAlign.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            };
            l.VerticalAlignment = s.VertAlign switch
            {
                UiVertAlign.Centre => VerticalAlignment.Center,
                UiVertAlign.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            };
            if (s.WordWrap)
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (s.FontSize is { } fs2)
                l.AddThemeFontSizeOverride("font_size", fs2);
            if (s.TextColour is { } tc2)
                l.AddThemeColorOverride("font_color", GodotColor(tc2));
            c.AddChild(l);
        }
        if (s.Disabled)
            c.Modulate = new Color(0.7f, 0.7f, 0.7f, s.Alpha * 0.8f);
    }

    private static void ApplyText(UiNodeSpec s, Control c)
    {
        switch (c)
        {
            case Label l:
                if (s.Text != null)
                    l.Text = s.Text;
                l.HorizontalAlignment = s.HorzAlign switch
                {
                    UiTextAlign.Centre => HorizontalAlignment.Center,
                    UiTextAlign.Right => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                };
                l.VerticalAlignment = s.VertAlign switch
                {
                    UiVertAlign.Centre => VerticalAlignment.Center,
                    UiVertAlign.Bottom => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Top,
                };
                if (s.WordWrap)
                    l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                break;
            case RichTextLabel r:
                r.Text = s.BbText ?? s.Text ?? "";
                break;
            case Button b:
                b.Text = s.Text ?? "";
                break;
        }
        if (s.FontSize is { } fs)
            c.AddThemeFontSizeOverride("font_size", fs);
        if (s.TextColour is { } tc)
        {
            var col = GodotColor(tc);
            // Le chrome monté est sombre partout : un TextColours
            // quasi-noir du layout natif (pensé pour un fond clair)
            // devient illisible sur les labels — clair. Les boutons
            // gardent leur encre (faces claires, ex. ok/cancel).
            if (c is Label && col.A > 0.5f && col.V < 0.35f)
                col = new Color(0.9f, 0.92f, 0.95f);
            c.AddThemeColorOverride("font_color", col);
        }
        if (s.Disabled && c is Button btn)
            btn.Disabled = true;
    }

    private static void ApplyStyles(UiNodeSpec s, Control c,
        UiTextureCache tex)
    {
        if (s.Background is { } bg && bg.Kind != UiStyleKind.None)
            ApplyPanelStyle(c, bg, tex, PanelThemeKey(c));
        if (s.Frame is { } fr && fr.Kind != UiStyleKind.None)
        {
            // Sur un Button la frame devient le stylebox normal ;
            // ailleurs on l'ajoute au panel.
            ApplyPanelStyle(c, fr, tex,
                c is BaseButton ? "normal" : PanelThemeKey(c));
            if (c is Button b && s.HoverImage == null &&
                fr.Image != null)
            {
                var sb = MakeStyleBox(fr, tex);
                if (sb != null)
                {
                    b.AddThemeStyleboxOverride("hover", sb);
                    b.AddThemeStyleboxOverride("pressed", sb);
                }
            }
        }
        // États bouton natifs (Hover/Pushed/Disabled du looknfeel) +
        // couleurs de label associées.
        if (c is Button bb)
        {
            if (s.Hover is { } hv &&
                MakeStyleBox(hv, tex) is { } hsb)
                bb.AddThemeStyleboxOverride("hover", hsb);
            if (s.Pressed is { } pr &&
                MakeStyleBox(pr, tex) is { } psb)
                bb.AddThemeStyleboxOverride("pressed", psb);
            if (s.DisabledLook is { } di &&
                MakeStyleBox(di, tex) is { } dsb)
                bb.AddThemeStyleboxOverride("disabled", dsb);
            // Anneau de focus Godot — absent du skin natif.
            bb.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            if (s.FontHover is { } fh)
                bb.AddThemeColorOverride("font_hover_color",
                    GodotColor(fh));
            if (s.FontPressed is { } fp)
                bb.AddThemeColorOverride("font_pressed_color",
                    GodotColor(fp));
            if (s.FontDisabled is { } fd)
                bb.AddThemeColorOverride("font_disabled_color",
                    GodotColor(fd));
        }
        if (c is ItemList il)
            il.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        // Fond réellement dessiné sur un label *TextLab : le texte
        // déclaré sombre (FF000000 — pensé pour une cellule claire)
        // redevient valide ; ApplyText l'avait éclairci faute de fond.
        if (c is Label && s.TextColour is { } tc3)
        {
            var col = GodotColor(tc3);
            if (col.A > 0.5f && col.V < 0.35f &&
                c.HasThemeStyleboxOverride("normal"))
                c.AddThemeColorOverride("font_color", col);
        }
        // TextureRect avec image absente → placeholder teinté.
        if (c is TextureRect tr && tr.Texture == null &&
            s.Image != null)
        {
            tr.Modulate = new Color(0.6f, 0.6f, 0.6f, s.Alpha);
        }
    }

    /// <summary>Item de thème portant le fond du contrôle
    /// (<c>panel</c> pour les panneaux/listes, <c>normal</c> pour les
    /// champs, <c>background</c>/<c>scroll</c>/<c>slider</c> pour les
    /// Range…).</summary>
    private static string PanelThemeKey(Control c) => c switch
    {
        // Label lit « normal » : sans lui les looks *TextLab natifs
        // (HintFrameBackdrop, Static2*) étaient appliqués sur « panel »
        // et ignorés par le moteur.
        Label => "normal",
        LineEdit or TextEdit or SpinBox => "normal",
        OptionButton or MenuButton => "normal",
        ProgressBar => "background",
        VScrollBar or HScrollBar => "scroll",
        HSlider or VSlider => "slider",
        _ => "panel",
    };

    private static void ApplyPanelStyle(Control c, UiStyleSpec st,
        UiTextureCache tex, string themeKey)
    {
        var sb = MakeStyleBox(st, tex);
        if (sb == null)
            return;
        c.AddThemeStyleboxOverride(themeKey, sb);
    }

    /// <summary>Passe « peau sombre » sur les contrôles d'un cadre
    /// natif restés au thème Godot par défaut (région d'imageset
    /// absente → fond blanc) : n'écrase JAMAIS un stylebox déjà
    /// posé par le skin CEGUI. À appeler après injection du contenu
    /// fonctionnel sur un layout natif.</summary>
    public static void SkinUnstyled(Control root)
    {
        foreach (var c in root.FindChildren("*", "Control", true, false)
                     .Cast<Control>())
        {
            var key = PanelThemeKey(c);
            if (c is Panel or ItemList or ScrollContainer
                    or LineEdit or TextEdit or OptionButton)
            {
                // Skin texture blanche (listbox DejaVu) → teintée
                // sombre ; fond plat/absent → stylebox sombre.
                if (c.GetThemeStylebox(key) is StyleBoxTexture st)
                {
                    var d = (StyleBoxTexture)st.Duplicate();
                    d.ModulateColor =
                        new Color(0.305f, 0.293f, 0.261f, 0.95f);
                    c.AddThemeStyleboxOverride(key, d);
                }
                else
                    c.AddThemeStyleboxOverride(key, DarkPanel());
            }
            if (c is Label l && !l.HasThemeColorOverride("font_color"))
                l.AddThemeColorOverride("font_color",
                    new Color(0.9f, 0.92f, 0.95f));
            if (c is ItemList il &&
                !il.HasThemeColorOverride("font_color"))
                il.AddThemeColorOverride("font_color",
                    new Color(0.9f, 0.92f, 0.95f));
        }
    }

    private static StyleBoxFlat DarkPanel() => new()
    {
        BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
        BorderColor = new Color(0.468f, 0.45f, 0.401f),
        BorderWidthBottom = 1, BorderWidthTop = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1,
        ContentMarginLeft = 6, ContentMarginTop = 4,
        ContentMarginRight = 6, ContentMarginBottom = 4,
    };

    /// <summary>UiStyleSpec → StyleBox (public pour
    /// <c>DragHelper</c> — skin des fenêtres construites en code).</summary>
    public static StyleBox? MakeStyleBox(UiStyleSpec st,
        UiTextureCache tex)
    {
        switch (st.Kind)
        {
            case UiStyleKind.Colour:
                return new StyleBoxFlat { BgColor = GodotColor(st.Colour) };
            case UiStyleKind.Composite:
                return tex.ComposeNinePatch(st);
            case UiStyleKind.Texture:
            case UiStyleKind.NinePatch:
                var t = tex.GetRegion(st.Image);
                if (t == null)
                    return null;
                return new StyleBoxTexture
                {
                    Texture = t,
                    TextureMarginLeft = st.MarginLeft,
                    TextureMarginRight = st.MarginRight,
                    TextureMarginTop = st.MarginTop,
                    TextureMarginBottom = st.MarginBottom,
                };
            default:
                return null;
        }
    }

    private static Control MakeTitleBar(UiNodeSpec s,
        UiTextureCache tex)
    {
        var bar = new CeguiTitleBar { Name = "__auto_titlebar__" };
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        // Hauteur native DejaVu/Titlebar (NewTitlebar* + divider),
        // sinon heuristique sur la taille de fonte.
        var h = s.TitlebarHeight > 0 ? s.TitlebarHeight
            : (s.FontSize ?? 12) + 10;
        bar.CustomMinimumSize = new Vector2(0, h);
        if (s.Titlebar is { } tbs &&
            MakeStyleBox(tbs, tex) is { } skinned)
            bar.AddThemeStyleboxOverride("panel", skinned);
        else
            bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.236f, 0.227f, 0.202f, 0.9f),
            });
        var label = new Label
        {
            Text = s.Text ?? "",
            VerticalAlignment = VerticalAlignment.Center,
            // CRITIQUE pour le drag : sans Ignore, le label plein
            // cadre (MouseFilter.Stop par défaut) couvre toute la
            // barre et avale les clics — _GuiInput du Panel ne
            // recevait jamais l'input → fenêtres impossibles à
            // déplacer.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = 8;
        label.OffsetRight = -26;
        if (s.FontSize is { } fs)
            label.AddThemeFontSizeOverride("font_size", fs);
        label.AddThemeColorOverride("font_color",
            s.CaptionColour is { } cap
                ? GodotColor(cap)
                : new Color(0.95f, 0.95f, 0.95f));
        bar.AddChild(label);
        if (s.CloseButton)
        {
            if (s.CloseNormal is { } cn)
            {
                // Bouton × natif (NewCloseButton* du look FrameWindow).
                var close = new TextureButton
                {
                    Name = "__auto_closebtn__",
                    TextureNormal = tex.GetRegion(cn),
                    TextureHover = s.CloseHover is { } chv
                        ? tex.GetRegion(chv) : null,
                    TexturePressed = s.ClosePushed is { } cp
                        ? tex.GetRegion(cp) : null,
                    StretchMode =
                        TextureButton.StretchModeEnum.KeepAspectCentered,
                };
                close.SetAnchorsAndOffsetsPreset(
                    Control.LayoutPreset.CenterRight);
                close.OffsetRight = -6;
                close.OffsetLeft = -26;
                close.CustomMinimumSize = new Vector2(20, 20);
                close.Pressed += () =>
                {
                    if (bar.GetParent() is Control win)
                        win.Visible = false;
                };
                bar.AddChild(close);
            }
            else
            {
                var close = new Button
                {
                    Text = "×",
                    Name = "__auto_closebtn__",
                };
                close.SetAnchorsAndOffsetsPreset(
                    Control.LayoutPreset.CenterRight);
                close.CustomMinimumSize = new Vector2(20, 0);
                close.Pressed += () =>
                {
                    if (bar.GetParent() is Control win)
                        win.Visible = false;
                };
                bar.AddChild(close);
            }
        }
        return bar;
    }

    /// <summary>ARGB CEGUI → <see cref="Color"/> Godot.</summary>
    public static Color GodotColor(uint argb)
    {
        var (r, g, b, a) = CeguiColour.ToRgba(argb);
        return new Color(r, g, b, a);
    }
}
