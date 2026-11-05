using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Combat;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre « Class » (ref ClassePanel.png, Reforged) — touches K/P et
/// icône classe du menu. Construite en code avec <see cref="EeSkin"/> :
///
///   - colonne gauche « Sort by class » : classes du 489
///     (<see cref="ClassChangeDialog.Classes"/>) — icône, nom, Lv,
///     cadenas si non débloquée, « Transform! » (C→S 168) sur les
///     classes débloquées, classe courante encadrée de vert ;
///     encadré « Class Ability Bonus » en bas ;
///   - onglets Class Skills / License Knowledge / Class News ;
///   - Class Skills : en-tête classe sélectionnée (icône, Lv, nom,
///     « Activate »), « Reg. Skills » (sorts payés en or — C_Spell
///     col42 = 1, bandeau « N Gold N Silver Available ») en 2
///     colonnes et « Class Skills » (col42 = 9, bandeau « N remaining
///     CP ») ; chaque sort : icône, nom, Lv, « +1 » (C→S 12) et
///     « Max » (C→S 623) — réponse 286 → <see cref="PlayerSkills.SetLevel"/>.
/// </summary>
public partial class ClassWindow : Control
{
    private HudRoot? _hud;
    private Panel? _win;
    /// <summary>Panneau visible (registre <see cref="HudWindows"/>).</summary>
    public Control? Window => _win;
    private VBoxContainer? _classList;
    private GridContainer? _regGrid;
    private VBoxContainer? _classGrid;
    private Label? _goldBanner, _cpBanner, _headName;
    private Button? _activate;
    private Control[]? _awakened;     // 3 grandes icônes d'en-tête
    private Label[]? _awakenedLv;
    private Control? _skillsPage, _licensePage, _newsPage;
    private readonly List<Button> _tabs = new();
    private int _selected = -1;     // classId affichée dans l'en-tête
    private int _cp;                // CP restants (489 ou dernier 286)
    private int _lastActive = -1;   // classe active précédente (bandeau switch)

    public void Build(HudRoot hud)
    {
        _hud = hud;
        _lastActive = CurrentClass;
        _win = EeSkin.MakeWindow(hud, "Class", 208, 104, 860, 508);
        AddChild(_win);
        BuildClassColumn(_win);
        BuildTabs(_win);
        BuildSkillsPage(_win);
        // Pages License/News construites à la 1re visite (1.4) — le
        // parse talents + ~40 nœuds de grille ne coûte que si l'onglet
        // est réellement ouvert.
        SelectTab(0);
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--class-mock=")) ApplyMock(a[13..]);
            if (a.StartsWith("--class-tab=") &&
                int.TryParse(a[12..], out var ti)) SelectTab(ti);
            // QA : bascule de classe après l'entrée (preuve du burst).
            if (a.StartsWith("--class-test=") &&
                int.TryParse(a[13..], out var cid))
                ((SceneTree)Engine.GetMainLoop()).CreateTimer(14).Timeout += () =>
                    ClassChangeDialog.RequestChange(cid);
        }

        // 489 revient à chaque kill : fenêtre masquée = rien à faire, VisibilityChanged rafraîchit à l'ouverture.
        ClassChangeDialog.ClassesChanged += OnClassesChanged;
        PlayerVitals.ClassChanged += OnActiveClassChanged;
        PlayerSkills.Changed += RefreshSkills;
        PlayerVitals.GoldChanged += RefreshBanners;
        PlayerVitals.PointsChanged += OnPointsChanged;
        MiscPackets.Received += OnMisc;
        _win.VisibilityChanged += () => { if (_win.Visible) Refresh(); };
        DragHelper.Persist(_win, "class");
        _win.Visible = false;
    }

    /// <summary>--class-mock=cur;sel;id:lv,id:lv… — classes possédées
    /// factices (+ quelques sorts appris sur la classe active) pour
    /// vérifier le rendu des skills/talents par classe sans serveur.</summary>
    private void ApplyMock(string spec)
    {
        var parts = spec.Split(';');
        _mockCurrent = int.Parse(parts[0]);
        _selected = int.Parse(parts[1]);
        _mock = new();
        foreach (var kv in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = kv.Split(':');
            _mock.Add(new ZonePlayerClassList.Entry
            {
                ClassId = int.Parse(p[0]), ClassLevel = int.Parse(p[1]),
                ClassPoints = 120, AaPoint = 7,
            });
        }
        ClassSkillTree.EnsureLoaded(AssetCatalog.ResolveRawAsset);
        var n = 0;
        foreach (var s in ClassSkillTree.RegSkills(_mockCurrent, 0).Take(4)
                     .Concat(ClassSkillTree.ClassSkills(_mockCurrent).Take(4)))
            PlayerSkills.SetLevel(s.Id, 2 + n++);
    }

    public override void _ExitTree()
    {
        PlayerVitals.ClassChanged -= OnActiveClassChanged;
        ClassChangeDialog.ClassesChanged -= OnClassesChanged;
        PlayerSkills.Changed -= RefreshSkills;
        PlayerVitals.GoldChanged -= RefreshBanners;
        PlayerVitals.PointsChanged -= OnPointsChanged;
        MiscPackets.Received -= OnMisc;
    }

    // ------------------------------------------------------------
    // Palette : toute couleur de texte est dérivée du fond qu'elle
    // surmonte (aucune couleur de police « en dur » sur un fond de
    // thème — le thème Purple/Blue… ne casse plus le contraste).

    private static float Lum(Color c) =>
        0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

    /// <summary>Encre lisible sur <paramref name="bg"/> (sombre sur fond
    /// clair, claire sur fond sombre).</summary>
    internal static Color InkOn(Color bg) => Lum(bg) > 0.45f
        ? Color.Color8(26, 24, 22) : Color.Color8(240, 238, 232);

    /// <summary>Encre sur parchemin (texte des cadres crème).</summary>
    private static Color Ink => InkOn(EeSkin.Parchment);

    /// <summary>Encre claire pour les fonds sombres de la fenêtre.</summary>
    private static readonly Color Light = Color.Color8(238, 236, 228);

    /// <summary>Pixel central d'une région de l'atlas DejaVu — sert à
    /// choisir l'encre des onglets natifs quel que soit le thème.</summary>
    private static Color RegionColor(HudRoot? hud, string name, Color fallback)
    {
        try
        {
            var r = hud?.Region("DejaVu", name);
            if (r?.Atlas?.GetImage() is { } img)
            {
                var rc = r.Region;
                return img.GetPixel(
                    (int)(rc.Position.X + rc.Size.X / 2),
                    (int)(rc.Position.Y + rc.Size.Y / 2));
            }
        }
        catch { /* atlas non prêt : repli */ }
        return fallback;
    }

    private static ShaderMaterial? _grayMat;

    /// <summary>Désaturation (icône de classe verrouillée = silhouette
    /// grise, ref ClassePanel).</summary>
    private static ShaderMaterial GrayMat()
    {
        if (_grayMat != null) return _grayMat;
        _grayMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = "shader_type canvas_item;\n" +
                    "void fragment(){ vec4 c = texture(TEXTURE, UV);" +
                    " float g = dot(c.rgb, vec3(0.299, 0.587, 0.114));" +
                    " COLOR = vec4(vec3(g * 0.75), c.a) * COLOR; }",
            },
        };
        return _grayMat;
    }

    /// <summary>Bouton « ? » natif (carré noir, point d'interrogation
    /// blanc) avec tooltip.</summary>
    private static Label Help(Control parent, int x, int y, string tip)
    {
        var l = new Label
        {
            Text = "?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Stop,
        };
        l.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = Color.Color8(22, 22, 20),
            BorderColor = Color.Color8(150, 148, 140),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        EeSkin.Shadowed(l, 11, Colors.White);
        HudRoot.Pin(l, 0, 0, 0, 0, x, y, x + 16, y + 16);
        var t = UiTextEn.Translate(tip);
        ItemTooltip.BindCustom(l, () => ItemTooltip.BuildTextTip(null, t));
        parent.AddChild(l);
        return l;
    }

    /// <summary>Texte posé sur un fond crème (encre sombre, sans ombre).</summary>
    private static Label InkLabel(Control parent, string text, int l, int t,
        int r, int b, int size = 12,
        HorizontalAlignment h = HorizontalAlignment.Left)
    {
        var lb = new Label
        {
            Text = text, HorizontalAlignment = h,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
        };
        lb.AddThemeFontSizeOverride("font_size", size);
        lb.AddThemeColorOverride("font_color", Ink);
        HudRoot.Pin(lb, 0, 0, 0, 0, l, t, r, b);
        parent.AddChild(lb);
        return lb;
    }

    /// <summary>Texte clair ombré sur fond sombre.</summary>
    private static Label LightLabel(Control parent, string text, int l, int t,
        int r, int b, int size = 12,
        HorizontalAlignment h = HorizontalAlignment.Left)
    {
        var lb = new Label
        {
            Text = text, HorizontalAlignment = h,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = false,
        };
        EeSkin.Shadowed(lb, size, Light);
        HudRoot.Pin(lb, 0, 0, 0, 0, l, t, r, b);
        parent.AddChild(lb);
        return lb;
    }

    /// <summary>Cadre crème à double filet (Border natif : liseré argent
    /// puis filet sombre intérieur).</summary>
    private static Panel Cream(Control parent, int l, int t, int r, int b)
    {
        var outer = new Panel { MouseFilter = MouseFilterEnum.Pass };
        HudRoot.Pin(outer, 0, 0, 0, 0, l, t, r, b);
        outer.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = EeSkin.Parchment,
            BorderColor = Color.Color8(196, 194, 186),
            BorderWidthTop = 2, BorderWidthBottom = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        parent.AddChild(outer);
        var inner = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        HudRoot.Pin(inner, 0, 0, 1, 1, 4, 4, -4, -4);
        inner.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = Color.Color8(112, 108, 98),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        outer.AddChild(inner);
        return outer;
    }

    /// <summary>Case d'icône carrée : fond fleur-de-lys natif + icône.</summary>
    private Panel IconSlot(Control parent, int x, int y, int size,
        Texture2D? tex, bool lit = true)
    {
        var cell = new Panel { MouseFilter = MouseFilterEnum.Stop };
        HudRoot.Pin(cell, 0, 0, 0, 0, x, y, x + size, y + size);
        if (_hud != null && EeSkin.EmptySlot(_hud) is { } sb)
            cell.AddThemeStyleboxOverride("panel", sb);
        parent.AddChild(cell);
        if (tex != null)
        {
            var ic = new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                Modulate = lit ? Colors.White : new Color(0.34f, 0.34f, 0.38f),
            };
            HudRoot.Pin(ic, 0, 0, 1, 1, 1, 1, -1, -1);
            cell.AddChild(ic);
        }
        return cell;
    }

    // ------------------------------------------------------------
    // Construction

    private void BuildClassColumn(Panel win)
    {
        var col = Box(win, 8, 26, 214, 434, EeSkin.InsetBox());
        var diamond = new TextureRect
        {
            Texture = EeSkin.Diamond(true),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(diamond, 0, 0, 0, 0, 12, 12, 25, 25);
        col.AddChild(diamond);
        LightLabel(col, UiTextEn.Translate("Sort by class"), 30, 6, 170, 30);
        Help(col, 186, 10, "Classes are unlocked by reaching the required levels. " +
            "Awakened classes are chosen from the two dedicated slots above the skills.");
        var list = Box(col, 12, 36, 194, 400, EeSkin.ParchmentBox());
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        HudRoot.Pin(scroll, 0, 0, 1, 1, 3, 3, -3, -3);
        list.AddChild(scroll);
        _classList = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _classList.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_classList);

        var bonus = Box(win, 8, 446, 214, 498, EeSkin.InsetBox());
        var sun = new TextureRect
        {
            Texture = AssetCatalog.GetTexture("UI/uiicon/J001.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(sun, 0, 0, 0, 0, 10, 8, 42, 40);
        bonus.AddChild(sun);
        LightLabel(bonus, UiTextEn.Translate("Class Ability Bonus"), 50, 6, 180, 26,
            12, HorizontalAlignment.Center);
        Help(bonus, 188, 8, "Total of the bonuses granted by class expertise.");
        // Ref ClassAbilitybonuslogo : survol du panneau = tooltip violet
        // « Class Ability Bonus Total » (PV/PM/ATQ/Magie/Soin cumulés de
        // toutes les classes montées).
        ItemTooltip.BindCustom(bonus, BuildExpertiseTip);
        ItemTooltip.BindCustom(sun, BuildExpertiseTip);
        var ce = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("CE List"),
            FocusMode = FocusModeEnum.None,
        }, 11);
        ce.Pressed += ToggleExpertiseList;
        HudRoot.Pin(ce, 0, 0, 1, 0, 52, 28, -28, 48);
        bonus.AddChild(ce);
    }

    // ---- expertise (Class Ability Bonus) — affichage client ----

    private static readonly string[] ExpLabels =
        { "HP", "MP", "ATK", "M-ATK", "G-Healing" };

    private static int[] ExpertiseTotals()
    {
        Data.Tables.ClassRankBuffTable.PathProvider ??= () =>
            AssetCatalog.ResolveRawAsset("data/db/c_classrankbuff.ini");
        var tot = new int[Data.Tables.ClassRankBuffTable.Stats];
        foreach (var e in Classes)
        {
            if (e.ClassLevel <= 0) continue;
            var b = Data.Tables.ClassRankBuffTable.Bonus(e.ClassId, e.ClassLevel);
            for (var i = 0; i < tot.Length; i++) tot[i] += b[i];
        }
        return tot;
    }

    /// <summary>Tooltip violet « Class Ability Bonus Total ».</summary>
    private static Control? BuildExpertiseTip()
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.30f, 0.16f, 0.45f, 0.95f),
            BorderColor = Color.Color8(190, 150, 235),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddChild(box);
        void Line(string t, Color c)
        {
            var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", c);
            box.AddChild(l);
        }
        Line(UiTextEn.Translate("Class Ability Bonus Total"), Colors.White);
        var tot = ExpertiseTotals();
        for (var i = 0; i < tot.Length; i++)
            Line(UiTextEn.Translate(ExpLabels[i]) + " " +
                 UiTextEn.Translate("%s1 points").Replace("%s1", tot[i].ToString()),
                Color.Color8(255, 238, 190));
        return panel;
    }

    private Control? _expList;

    /// <summary>« CE List » : bonus d'expertise par classe (niveau,
    /// palier, 5 stats) dans une petite fenêtre au-dessus du panneau.</summary>
    private void ToggleExpertiseList()
    {
        if (_hud != null) ExpertiseWindows.Toggle(_hud);
    }

    private void BuildTabs(Panel win)
    {
        // Libellés de la ref : « Class Skills | License knowledge | Class News ».
        string[] names =
        {
            UiTextEn.Translate("Class Skills"),
            UiTextEn.Translate("License knowledge"),
            UiTextEn.Translate("Class News"),
        };
        // Encre choisie d'après le pixel réel de l'onglet natif (le thème
        // Purple rend l'onglet clair : texte clair = illisible).
        var inkN = InkOn(RegionColor(_hud, "TabButtonMiddleNormal",
            EeSkin.Palette.Tab));
        var inkS = InkOn(RegionColor(_hud, "TabButtonMiddleSelected",
            EeSkin.Palette.TabSel));
        var x = 228;
        for (var i = 0; i < names.Length; i++)
        {
            var b = new Button
            {
                Text = names[i], ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                ClipText = true,
            };
            b.AddThemeFontSizeOverride("font_size", 11);
            var w = (int)Math.Clamp(b.GetThemeFont("font")
                .GetStringSize(names[i], HorizontalAlignment.Left, -1, 11).X + 28,
                80, 210);
            HudRoot.Pin(b, 0, 0, 0, 0, x, 28, x + w, 48);
            x += w + 2;
            b.AddThemeColorOverride("font_color", inkN);
            b.AddThemeColorOverride("font_hover_color", inkN);
            b.AddThemeColorOverride("font_pressed_color", inkS);
            b.AddThemeColorOverride("font_hover_pressed_color", inkS);
            b.AddThemeColorOverride("font_focus_color", inkN);
            foreach (var st in new[] { "normal", "hover" })
                if (_hud != null && EeSkin.Tab(_hud, false) is { } sb)
                    b.AddThemeStyleboxOverride(st, sb);
            foreach (var st in new[] { "pressed", "hover_pressed" })
                if (_hud != null && EeSkin.Tab(_hud, true) is { } sb)
                    b.AddThemeStyleboxOverride(st, sb);
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            var idx = i;
            b.Pressed += () => SelectTab(idx);
            _tabs.Add(b);
            win.AddChild(b);
        }
    }

    private void BuildSkillsPage(Panel win)
    {
        var page = Box(win, 230, 48, 850, 500, EeSkin.InsetBox());
        _skillsPage = page;

        // En-tête (JobSkill_NA02 Border3 [18,14 246x66]) : 3 grandes
        // icônes 48×48 au pas de 52 (classe de base + 2 éveils), « Lv. »
        // dessous, nom + « Activate » à droite.
        var head = Box(page, 12, 8, 258, 76, EeSkin.InsetBox());
        _awakened = new Control[3];
        _awakenedLv = new Label[3];
        for (var i = 0; i < 3; i++)
        {
            var c = new Control { Name = $"HeadCell_{i}" };
            HudRoot.Pin(c, 0, 0, 0, 0, 8 + i * 52, 3, 8 + i * 52 + 48, 51);
            _awakened[i] = c;
            head.AddChild(c);
            _awakenedLv[i] = LightLabel(head, "", 8 + i * 52 - 2, 52,
                8 + i * 52 + 50, 68, 11, HorizontalAlignment.Center);
        }
        _headName = LightLabel(head, "", 160, 6, 244, 30, 13,
            HorizontalAlignment.Center);
        _activate = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("Activate"), FocusMode = FocusModeEnum.None,
        }, 11);
        HudRoot.Pin(_activate, 0, 0, 0, 0, 164, 38, 240, 61);
        _activate.AddThemeColorOverride("font_disabled_color",
            Color.Color8(176, 174, 166));
        _activate.Pressed += () =>
        {
            if (_selected > 0) ClassChangeDialog.RequestChange(_selected);
        };
        head.AddChild(_activate);
        Help(page, 590, 8, "Spend gold on regular skills and Class Points on class skills " +
            "with the +1 and Max buttons.");

        // Reg. Skills (or) — 2 colonnes (Border2 [18,86 365x355]).
        var reg = Box(page, 12, 80, 377, 440, EeSkin.ParchmentBox());
        SectionTitle(reg, UiTextEn.Translate("Reg. Skills"));
        _goldBanner = Banner(reg);
        _regGrid = new GridContainer { Columns = 2 };
        _regGrid.AddThemeConstantOverride("h_separation", 6);
        _regGrid.AddThemeConstantOverride("v_separation", 4);
        reg.AddChild(SkillScroll(_regGrid));

        // Class Skills (CP) — 1 colonne (Border1 [394,86 215x355]).
        var cls = Box(page, 388, 80, 604, 440, EeSkin.ParchmentBox());
        SectionTitle(cls, UiTextEn.Translate("Class Skills"));
        _cpBanner = Banner(cls);
        _classGrid = new VBoxContainer();
        _classGrid.AddThemeConstantOverride("separation", 4);
        cls.AddChild(SkillScroll(_classGrid));
    }

    private static ScrollContainer SkillScroll(Control content)
    {
        var sc = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        HudRoot.Pin(sc, 0, 0, 1, 1, 6, 64, -4, -4);
        sc.AddChild(content);
        return sc;
    }

    private static Panel Box(Control parent, int l, int t, int r, int b,
        StyleBox style)
    {
        var p = new Panel();
        HudRoot.Pin(p, 0, 0, 0, 0, l, t, r, b);
        p.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(p);
        return p;
    }

    /// <summary>Titre de section sur parchemin (encre sombre).</summary>
    private static void SectionTitle(Control box, string text) =>
        InkLabel(box, text, 8, 4, 220, 24);

    /// <summary>Bandeau gris centré « 550 Gold 9 Silver Available »
    /// (fond gris moyen, encre sombre).</summary>
    private static Label Banner(Control box)
    {
        var bg = Color.Color8(196, 194, 188);
        var p = new Panel();
        HudRoot.Pin(p, 0, 0, 1, 0, 8, 26, -8, 54);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = Color.Color8(120, 118, 110),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        box.AddChild(p);
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        l.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", InkOn(bg));
        p.AddChild(l);
        return l;
    }

    // ------------------------------------------------------------
    // Rafraîchissement

    private void SelectTab(int i)
    {
        for (var k = 0; k < _tabs.Count; k++)
            _tabs[k].ButtonPressed = k == i;
        if (i == 1 && _licensePage == null && _win != null)
        {
            _licensePage = BuildLicensePage(_win);
            RefreshLicense();
        }
        if (i == 2 && _newsPage == null && _win != null)
        {
            _newsPage = BuildNewsPage(_win);
            RefreshNews();
        }
        if (_skillsPage != null) _skillsPage.Visible = i == 0;
        if (_licensePage != null) _licensePage.Visible = i == 1;
        if (_newsPage != null) _newsPage.Visible = i == 2;
    }

    /// <summary>Burst de changement de classe (280 attr 0x2c) : la
    /// classe active, les icônes/niveaux du HUD, la fiche perso et la
    /// liste de sorts suivent.</summary>
    private void OnActiveClassChanged()
    {
        var id = CurrentClass;
        // Ref ClassPanel-ChangementDeClasse : « Switched Class to X »
        // (texte centré + chat système, index t_textindex 2013) —
        // seulement sur un vrai changement, pas au premier burst.
        if (_lastActive > 0 && id > 0 && id != _lastActive)
        {
            var msg = TextResolver.TextIndex(2013,
                TextResolver.ClassName(id) ?? $"Class {id}");
            if (msg != null)
            {
                _hud?.AddChat(7, "", msg);
                if (_hud != null) ClassSwitchFx.Play(_hud, msg);
            }
        }
        _lastActive = id;
        _selected = id;
        var e = Classes.FirstOrDefault(c => c.ClassId == id);
        var icon = CharSelectScreen.ClassIcon(id);
        if (_hud != null)
        {
            _hud.Vitals?.SetClassIcon(icon);
            _hud.Hotbar?.SetClassIcon(icon);
            if (e != null)
            {
                _hud.Vitals?.SetJobLv(e.ClassLevel);
                _hud.Hotbar?.SetLevels(0, e.ClassLevel);
            }
            _hud.Profile?.SetClass(ProfileWindow.ResolveClassName(id),
                e?.ClassLevel ?? 0);
        }
        Refresh();
        RefreshNews();
    }

    /// <summary>Résolution itemId → (Code, ModelCode) pour les armes
    /// de l'aperçu Class News — même convention que
    /// <see cref="EquipSlotsPanel"/> (4pequip/WeaponModelStem).</summary>
    private static (string Code, string ModelCode)? PreviewWeaponLookup(
        int itemId)
    {
        var def = InventoryWindow.LookupDef(itemId);
        return def == null ? null : (def.Code, def.ModelCode);
    }

    private static Dictionary<int, Data.Tables.ClassTable.ClassDef>? _classDefs;

    private static Data.Tables.ClassTable.ClassDef? ClassDefOf(int id)
    {
        try
        {
            if (_classDefs == null)
            {
                var path = AssetCatalog.ResolveRawAsset("data/db/c_class.ini");
                _classDefs = path != null
                    ? Data.Tables.ClassTable.Load(path) : new();
            }
            return _classDefs.TryGetValue(id, out var d) ? d
                : _classDefs.TryGetValue(id % 100, out var b) ? b : null;
        }
        catch { return null; }
    }

    // ---- onglet « Class News » (JobListWnd_NA : fiche de classe) ----
    //
    // Clarification « Class News » : ce n'est PAS un flux d'annonces mais
    // la fiche descriptive de la classe affichée (layout natif
    // JobListWnd_NA : bandeau description, Innate talents + Class Bonus,
    // Weekly Performance, aperçu du personnage + tenues, sorts/
    // certificats propres à la classe).

    private Control? _newsHeadIcon, _newsPreview, _newsSkills, _newsCerts,
        _newsTalents;
    private Label? _newsDesc, _newsPreviewName;
    private readonly Label[] _newsStat = new Label[7];

    private static readonly string[] StatNames =
        { UiTextEn.Translate("HP"), UiTextEn.Translate("MP"), UiTextEn.Translate("STR"), UiTextEn.Translate("AGI"), UiTextEn.Translate("INT"), UiTextEn.Translate("WIS"), UiTextEn.Translate("LCK") };

    private Control BuildNewsPage(Panel win)
    {
        var page = Box(win, 230, 48, 850, 500, EeSkin.InsetBox());
        _newsHeadIcon = new Control();
        HudRoot.Pin(_newsHeadIcon, 0, 0, 0, 0, 24, 8, 74, 58);
        page.AddChild(_newsHeadIcon);
        Cream(page, 80, 10, 566, 58);
        _newsDesc = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(_newsDesc, 0, 0, 0, 0, 92, 13, 554, 55);
        _newsDesc.AddThemeFontSizeOverride("font_size", 11);
        _newsDesc.AddThemeColorOverride("font_color", Ink);
        page.AddChild(_newsDesc);
        Help(page, 588, 12, "Description of the selected class.");

        // Characteristics (talents innés) + Class Stat Bonus.
        Cream(page, 12, 70, 182, 330);
        InkLabel(page, UiTextEn.Translate("Characteristics"), 24, 80, 150, 102);
        Help(page, 160, 82, "Race buff, weapon buff and armor type of the class, and the stat percentages it grants.");
        _newsTalents = new Control();
        HudRoot.Pin(_newsTalents, 0, 0, 0, 0, 26, 106, 170, 142);
        page.AddChild(_newsTalents);
        InkLabel(page, UiTextEn.Translate("Class Stat Bonus"), 24, 146, 170, 164);
        // Cellules de stats : étiquette claire / valeur grise, encre
        // sombre dans les deux cas (vert/rouge foncés pour ±).
        for (var i = 0; i < 7; i++)
        {
            var y = 166 + i * 20;
            var nm = new Label
            {
                Text = StatNames[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var nbg = Color.Color8(222, 220, 212);
            nm.AddThemeStyleboxOverride("normal", new StyleBoxFlat
                { BgColor = nbg });
            nm.AddThemeFontSizeOverride("font_size", 11);
            nm.AddThemeColorOverride("font_color", InkOn(nbg));
            HudRoot.Pin(nm, 0, 0, 0, 0, 26, y, 90, y + 18);
            page.AddChild(nm);
            var v = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            v.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = Color.Color8(184, 182, 174),
                BorderColor = Color.Color8(120, 118, 110),
                BorderWidthTop = 1, BorderWidthBottom = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            v.AddThemeFontSizeOverride("font_size", 11);
            HudRoot.Pin(v, 0, 0, 0, 0, 94, y, 170, y + 18);
            page.AddChild(v);
            _newsStat[i] = v;
        }

        // Weekly Performance (compteurs 0 comme la ref — aucune source
        // serveur pour ces compteurs hebdomadaires).
        Cream(page, 192, 70, 354, 330);
        InkLabel(page, UiTextEn.Translate("Weekly Performance"), 200, 80, 328, 102, 10);
        Help(page, 330, 82, "Weekly results of your class (not tracked yet).");
        string[] perf = { "Territory War Kill", "Arena Victories C", "Monster Conqueror" };
        string[] perfIcon = { "J049", "J052", "J010" };
        for (var i = 0; i < 3; i++)
        {
            var y = 108 + i * 44;
            var row = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            HudRoot.Pin(row, 0, 0, 0, 0, 198, y, 348, y + 40);
            row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Color.Color8(34, 33, 30),
                BorderColor = Color.Color8(120, 100, 60),
                BorderWidthTop = 1, BorderWidthBottom = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            page.AddChild(row);
            var ic = new TextureRect
            {
                Texture = AssetCatalog.GetTexture($"UI/uiicon/{perfIcon[i]}.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            HudRoot.Pin(ic, 0, 0, 0, 0, 3, 3, 37, 37);
            row.AddChild(ic);
            LightLabel(row, UiTextEn.Translate(perf[i]), 42, 2, 148, 19, 11);
            var cnt = new Label
            {
                Text = "0", HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var cbg = Color.Color8(200, 198, 190);
            cnt.AddThemeStyleboxOverride("normal", new StyleBoxFlat
                { BgColor = cbg });
            cnt.AddThemeFontSizeOverride("font_size", 11);
            cnt.AddThemeColorOverride("font_color", InkOn(cbg));
            HudRoot.Pin(cnt, 0, 0, 0, 0, 42, 20, 100, 37);
            row.AddChild(cnt);
        }

        // Aperçu : fond rosé + grande icône de la classe, sélecteur de
        // tenues 1–9 et « Set Outfit » (indisponibles : pas de modèle de
        // tenue par classe côté client).
        var pv = Box(page, 366, 70, 566, 306, new StyleBoxFlat
        {
            BgColor = Color.Color8(52, 46, 48),
            BorderColor = Color.Color8(150, 140, 120),
            BorderWidthTop = 2, BorderWidthBottom = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
        });
        _newsPreview = new Control();
        HudRoot.Pin(_newsPreview, 0.5f, 0, 0.5f, 0, -48, 56, 48, 152);
        pv.AddChild(_newsPreview);
        _newsPreview.Visible = false;     // l'icône de classe cède la place à l'aperçu 3D du joueur
        BuildOutfitPreview(pv);
        _newsPreviewName = LightLabel(pv, "", 4, 206, 196, 230, 12,
            HorizontalAlignment.Center);
        Help(page, 572, 70, "Preview of the class outfit.");
        _outfitBtns.Clear();
        for (var i = 0; i < 9; i++)
        {
            var idx = i;
            var b9 = EeSkin.Grey(new Button
            {
                Text = (i + 1).ToString(),
                FocusMode = FocusModeEnum.None,
            }, 10);
            b9.Pressed += () => { _outfitSel = idx; RefreshOutfits(); };
            HudRoot.Pin(b9, 0, 0, 0, 0, 572, 94 + i * 22, 604, 110 + i * 22);
            page.AddChild(b9);
            _outfitBtns.Add(b9);
        }
        _outfitSet = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("Set Outfit"),
            FocusMode = FocusModeEnum.None,
        }, 11);
        _outfitSet.Pressed += () =>
        {
            // Garde-robe native : le serveur contrôle classe active + niveau de classe.
            Send(new ZoneServerClosetSet { Value = (byte)_outfitSel });
        };
        HudRoot.Pin(_outfitSet, 0, 0, 0, 0, 514, 312, 604, 336);
        page.AddChild(_outfitSet);

        // Rangées du bas : sorts propres + certificats obtenables.
        LightLabel(page, UiTextEn.Translate("Class Talents"), 24, 350, 128, 384, 11)
            .AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _newsSkills = new Control();
        HudRoot.Pin(_newsSkills, 0, 0, 0, 0, 132, 350, 604, 384);
        page.AddChild(_newsSkills);
        LightLabel(page, UiTextEn.Translate("Class Certificates"), 24, 390, 128, 428, 11)
            .AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _newsCerts = new Control();
        HudRoot.Pin(_newsCerts, 0, 0, 0, 0, 132, 392, 604, 426);
        page.AddChild(_newsCerts);
        Help(page, 420, 400, "Certificates that can be obtained with this class.");
        return page;
    }

    /// <summary>Aperçu 3D du joueur (même corps que le jeu) dans le cadre de l'onglet.</summary>
    private void BuildOutfitPreview(Control host)
    {
        var svc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        HudRoot.Pin(svc, 0, 0, 1, 1, 3, 3, -3, -3);
        var vp = new SubViewport
        {
            TransparentBg = true, OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
            Size = new Vector2I(194, 230),
        };
        svc.AddChild(vp);
        var stage = new Node3D();
        vp.AddChild(stage);
        stage.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.9f, 0.88f, 0.84f), AmbientLightEnergy = 0.9f,
            },
        });
        stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30f, -20f, 0f), LightEnergy = 0.8f });
        _outfitCam = new Camera3D { Current = true, Fov = 30f };
        stage.AddChild(_outfitCam);
        _outfitCam.LookAtFromPosition(new Vector3(0f, 0.95f, 5.2f), new Vector3(0f, 0.9f, 0f), Vector3.Up);
        _outfitHolder = new Node3D();
        stage.AddChild(_outfitHolder);
        host.AddChild(svc);
    }

    /// <summary>État des 9 boutons (verrouillés sous le niveau de classe) et aperçu de la tenue choisie.</summary>
    private void RefreshOutfits()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--outfit-sel=") && int.TryParse(a[13..], out var q)) _outfitSel = q;
        var id = _selected > 0 ? _selected : CurrentClass;
        var entry = Classes.FirstOrDefault(c => c.ClassId == id);
        var lvl = entry?.ClassLevel ?? 0;
        var closet = EdenClient.Game.CharModelSpec.ClassCloset(id);
        if (_outfitSel >= closet.Count) _outfitSel = Math.Max(0, closet.Count - 1);
        for (var i = 0; i < _outfitBtns.Count; i++)
        {
            var b = _outfitBtns[i];
            var exists = i < closet.Count;
            var unlocked = exists && closet[i].Level <= lvl;
            b.Visible = exists;
            b.Disabled = !unlocked && i != _outfitSel && false;
            b.Modulate = unlocked ? Colors.White : new Color(1f, 1f, 1f, 0.45f);
            b.ButtonPressed = false;
            b.TooltipText = !exists ? "" : unlocked
                ? UiTextEn.Translate("Outfit") + $" {i + 1}"
                : UiTextEn.Translate("Class Level") + $" {closet[i].Level}";
            if (i == _outfitSel)
                b.AddThemeColorOverride("font_color", Color.Color8(255, 220, 90));
            else b.RemoveThemeColorOverride("font_color");
        }
        var cur = entry?.Style ?? 0;
        var ok = _outfitSel < closet.Count && closet[_outfitSel].Level <= lvl &&
                 id == CurrentClass && _outfitSel != cur;
        if (_outfitSet != null) _outfitSet.Disabled = !ok;
        if (_outfitHolder == null) return;
        if (_outfitShownClass == id && _outfitShownStyle == _outfitSel) return;
        _outfitShownClass = id; _outfitShownStyle = _outfitSel;
        // Libération immédiate : QueueFree diffère la destruction à la
        // fin de frame — le nouveau corps ajouté juste après cohabitait
        // un instant avec l'ancien (tenues superposées visibles).
        foreach (var c in _outfitHolder.GetChildren()) c.Free();
        var app = SessionCarry.Character?.Appearance;
        if (app == null) return;
        var body = EdenClient.Game.CharVisual.BuildBody(app, classId: id, style: _outfitSel);
        if (body == null) return;
        body.RotationDegrees = new Vector3(-90f, 0f, 0f);
        _outfitHolder.AddChild(body);
        // Tenue = celle de la classe et de l'outfit SÉLECTIONNÉS (repli
        // ClassOutfitRow(id, _outfitSel) quand worn ne contient aucune
        // armure) ; armes = celles réellement portées (locs 7/8/10 —
        // jamais l'armure du joueur ni d'armes fictives). Loc 9 filtrée
        // : pas de slot natif, elle dessinerait une 2e arme en Prop1.
        var weapons = new Dictionary<int, int>();
        foreach (var (wloc, wid) in PlayerEquipment.Worn)
            if (wloc is >= 7 and <= 10 and not 9) weapons[wloc] = wid;
        var letter = EdenClient.Game.CharVisual.BodyLetterOf(body);
        EdenClient.Game.CharVisual.ApplyEquipment(body, letter, weapons,
            PreviewWeaponLookup, id, _outfitSel);
        EdenClient.Game.CharVisual.PlayIdle(body);
        if (_newsPreviewName != null)
            _newsPreviewName.Text = $"{TextResolver.ClassInfo(id)?.Name ?? ""}  ·  {_outfitSel + 1}/{Math.Max(1, closet.Count)}";
    }

    private void RefreshNews()
    {
        RefreshOutfits();
        if (_newsDesc == null) return;
        var id = _selected > 0 ? _selected : CurrentClass;
        var info = TextResolver.ClassInfo(id);
        var def = ClassDefOf(id);
        var desc = info?.ShortDescription ?? "";
        _newsDesc.Text = desc is "Empty" or "Vide" ? "" : desc;
        foreach (var c in _newsHeadIcon!.GetChildren()) c.QueueFree();
        IconSlot(_newsHeadIcon, 0, 0, 50, CharSelectScreen.ClassIcon(id));
        foreach (var c in _newsPreview!.GetChildren()) c.QueueFree();
        _newsPreview.AddChild(new TextureRect
        {
            Texture = CharSelectScreen.ClassIcon(id),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Size = new Vector2(96, 96),
        });
        if (_newsPreviewName != null)
            _newsPreviewName.Text = TextResolver.ClassName(id) ?? "";
        // Caractéristiques de classe (3) : buff de race, buff d'arme,
        // type d'armure — portés par ClassSpellIds (icône + tooltip sort).
        foreach (var c in _newsTalents!.GetChildren()) c.QueueFree();
        var chars = (def?.ClassSpellIds ?? Array.Empty<int>())
            .Where(x => x > 0).Take(3).ToArray();
        for (var i = 0; i < 3; i++)
        {
            if (i >= chars.Length) { IconSlot(_newsTalents, i * 36, 0, 34, null); continue; }
            var sid = chars[i];
            var slot = IconSlot(_newsTalents, i * 36, 0, 34,
                SpellCatalog.Icon((ushort)sid));
            ItemTooltip.BindCustom(slot, () =>
                ItemTooltip.BuildSkillTip(sid, 1, (ClassSkillTree.Get(sid)?.Pay ?? 0),
                    false, -1, preview: true));
        }
        for (var i = 0; i < 7; i++)
        {
            var pct = def != null && i + 2 < def.StatPercents.Length
                ? def.StatPercents[i + 2] : 100;
            _newsStat[i].Text = $"{pct}%";
            // Encre sombre sur gris clair : vert/rouge foncés pour ±.
            _newsStat[i].AddThemeColorOverride("font_color",
                pct > 100 ? Color.Color8(20, 100, 24)
                : pct < 100 ? Color.Color8(150, 24, 24)
                : Color.Color8(26, 24, 22));
        }
        // Talents de classe (arbre License Knowledge) : enchants CClassData+0x68.
        FillTalentRow(_newsSkills!, (def?.TalentIds ?? Array.Empty<int>())
            .Where(x => x > 0).Take(12).ToArray());
        var certs = (def?.CertificateIds ?? Array.Empty<int>())
            .Where(x => x > 0).Take(8).ToArray();
        FillIconRow(_newsCerts!, certs, false, 8);
    }

    /// <summary>Rangée de talents de classe : icône d'enchant + tooltip.</summary>
    private void FillTalentRow(Control row, int[] ids)
    {
        foreach (var c in row.GetChildren()) c.QueueFree();
        for (var i = 0; i < 12; i++)
        {
            if (i >= ids.Length)
            {
                IconSlot(row, i * 36, 0, 34, null).MouseFilter =
                    MouseFilterEnum.Ignore;
                continue;
            }
            var tid = ids[i];
            var slot = IconSlot(row, i * 36, 0, 34, TalentIcon(tid));
            var tn = Data.Tables.EnchantTable.NameOf(tid);
            var td = Data.Tables.EnchantTable.DescriptionOf(tid);
            ItemTooltip.BindCustom(slot, () =>
                ItemTooltip.BuildTextTip(tn, td));
        }
    }

    /// <summary>Rangée de cases 34 px (pas 36) : sorts (tooltip de sort)
    /// ou certificats (tooltip de certificat) ; cases fleur-de-lys vides
    /// jusqu'à <paramref name="slots"/>.</summary>
    private void FillIconRow(Control row, int[] ids, bool spells, int slots = 12)
    {
        foreach (var c in row.GetChildren()) c.QueueFree();
        for (var i = 0; i < Math.Max(ids.Length, Math.Min(slots, 12)); i++)
        {
            if (i >= ids.Length)
            {
                IconSlot(row, i * 36, 0, 34, null).MouseFilter =
                    MouseFilterEnum.Ignore;
                continue;
            }
            var x = ids[i];
            Texture2D? tex;
            Control slot;
            if (spells)
            {
                tex = SpellCatalog.Icon((ushort)x);
                slot = IconSlot(row, i * 36, 0, 34, tex);
                ItemTooltip.BindCustom(slot, () =>
                    ItemTooltip.BuildSkillTip(x, 1, (ClassSkillTree.Get(x)?.Pay ?? 0),
                        false, -1, preview: true));
            }
            else
            {
                var d = InventoryWindow.LookupDef(x);
                tex = d is { Code.Length: > 0 }
                    ? AssetCatalog.GetTexture($"UI/itemicon/{d.Code}.png")
                    : null;
                slot = IconSlot(row, i * 36, 0, 34, tex);
                ItemTooltip.BindCustom(slot, () =>
                    ItemTooltip.BuildCertTip(x));
            }
        }
    }

    // ---- onglet « License knowledge » (JobCurrentWnd_NA) ----
    //
    // Positions = layout natif (page 0,0 = coin du client area ; cases
    // 34×34, pas 50/57 pour l'arbre, 62 pour les certificats).

    private Control? _talentGrid;
    private readonly List<Button> _outfitBtns = new();
    private Button? _outfitSet;
    private int _outfitSel;
    private Node3D? _outfitHolder;
    private Camera3D? _outfitCam;
    private int _outfitShownClass = -1, _outfitShownStyle = -1;
    private Label? _kpLabel, _kpEntered;
    private Button? _kpConfirm;
    private Control? _talentCover, _certCover;
    private Button? _licConfirm;

    /// <summary>Boîte grise claire + texte sombre centrée — style des
    /// lignes « N KPs Remain » du panneau talents natif.</summary>
    private static PanelContainer KpBox(out Label label)
    {
        var bg = Color.Color8(196, 194, 188);
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = Color.Color8(120, 118, 112),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", InkOn(bg));
        box.AddChild(label);
        return box;
    }

    /// <summary>Bouton noir arrondi du panneau talents (Confirm / Refresh) :
    /// texte clair actif, gris lisible désactivé.</summary>
    private static Button DarkBtn(string text, int size = 11)
    {
        var b = new Button
        {
            Text = text, FocusMode = FocusModeEnum.None,
        };
        StyleBoxFlat Sb(Color bg, Color border) => new()
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        b.AddThemeStyleboxOverride("normal", Sb(Color.Color8(72, 54, 112), Color.Color8(176, 156, 214)));
        b.AddThemeStyleboxOverride("hover", Sb(Color.Color8(96, 74, 146), Color.Color8(210, 196, 240)));
        b.AddThemeStyleboxOverride("pressed", Sb(Color.Color8(52, 38, 84), Color.Color8(210, 196, 240)));
        b.AddThemeStyleboxOverride("disabled", Sb(Color.Color8(26, 26, 25), Color.Color8(64, 62, 58)));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", Color.Color8(238, 236, 228));
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_disabled_color", Color.Color8(104, 102, 96));
        return b;
    }

    /// <summary>Points saisis non confirmés (spellId → points).</summary>
    private readonly Dictionary<short, int> _pendingAa = new();

    /// <summary>KP disponibles : attr 0x31 vivant pour la classe active,
    /// sinon champ aa du 489.</summary>
    private static int KpPool(ZonePlayerClassList.Entry? e, bool current) =>
        current && PlayerVitals.AaPoint >= 0 ? PlayerVitals.AaPoint
            : e?.AaPoint ?? 0;

    private void ConfirmTalents()
    {
        foreach (var (sid, n) in _pendingAa.ToList())
            for (var i = 0; i < n; i++)
                Send(new Protocol.ZoneServerSetAAPoint
                    { Field1 = sid, Field2 = 0 });
        _pendingAa.Clear();
        RefreshLicense();
    }
    private static Dictionary<int, Data.Tables.TalentTable.Slot?[]>? _talents;

    /// <summary>Parse paresseux de c_talent — appelé aussi par le
    /// warm-up de chargement (tâche de fond, avant le premier open).</summary>
    internal static void WarmTalents()
    {
        if (_talents != null) return;
        var path = AssetCatalog.ResolveRawAsset("data/db/c_talent.ini");
        _talents = path != null
            ? Data.Tables.TalentTable.Load(path) : new();
    }

    /// <summary>Voile « pas encore obtenu » (BorderTextLab Cover1/2 du
    /// layout) : texte centré sur fond sombre translucide.</summary>
    private static Control Cover(Control parent, int l, int t, int r, int b,
        string text)
    {
        var p = new Panel { MouseFilter = MouseFilterEnum.Stop };
        HudRoot.Pin(p, 0, 0, 0, 0, l, t, r, b);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.08f, 0.86f),
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        var lb = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        EeSkin.Shadowed(lb, 13, Colors.White);
        HudRoot.Pin(lb, 0, 0, 1, 1, 24, 8, -24, -8);
        p.AddChild(lb);
        parent.AddChild(p);
        p.Visible = false;
        return p;
    }

    /// <summary>Page « License knowledge » : arbre de talents (3 rangées
    /// × 8 colonnes, flèches de prérequis, KPs Remain/Entered, Confirm/
    /// Refresh) en haut ; certificats (grille 3×3 fleur-de-lys + 2
    /// gemmes, liste possédée, compteur) en bas — layout JobCurrentWnd_NA.</summary>
    private Control BuildLicensePage(Panel win)
    {
        var page = Box(win, 230, 48, 850, 500, EeSkin.InsetBox());
        // ---- talents (Border2 [18,18 592x205]) ----
        Cream(page, 12, 12, 604, 217);
        var tbox = Box(page, 24, 24, 432, 206, EeSkin.ParchmentBox());
        _talentGrid = new Control();
        page.AddChild(_talentGrid);
        InkLabel(page, UiTextEn.Translate("Class Talent"), 444, 24, 570, 48, 12);
        Help(page, 576, 22, "Spend Knowledge Points (KPs) on talents, then press Confirm.");
        var kb1 = KpBox(out _kpLabel);
        HudRoot.Pin(kb1, 0, 0, 0, 0, 444, 63, 592, 90);
        page.AddChild(kb1);
        var kb2 = KpBox(out _kpEntered);
        HudRoot.Pin(kb2, 0, 0, 0, 0, 444, 107, 592, 134);
        page.AddChild(kb2);
        // Ref : Confirm applique les points saisis (un 148 par point),
        // Refresh annule la saisie.
        _kpConfirm = DarkBtn(UiTextEn.Translate("Confirm"));
        HudRoot.Pin(_kpConfirm, 0, 0, 0, 0, 444, 175, 512, 205);
        _kpConfirm.Pressed += ConfirmTalents;
        page.AddChild(_kpConfirm);
        var kpRefresh = DarkBtn(UiTextEn.Translate("Refresh"));
        HudRoot.Pin(kpRefresh, 0, 0, 0, 0, 524, 175, 592, 205);
        kpRefresh.Pressed += () => { _pendingAa.Clear(); RefreshLicense(); };
        page.AddChild(kpRefresh);
        _talentCover = Cover(page, 12, 12, 604, 217, UiTextEn.Translate(
            "Knowledge point not obtained. To get the first knowledge point, this Class must reach Level 2."));

        // ---- certificats (Border1 [18,235 592x205]) ----
        Cream(page, 12, 229, 604, 434);
        var lb = Box(page, 24, 241, 206, 423, EeSkin.ParchmentBox());
        _certGrid = new Control();
        HudRoot.Pin(_certGrid, 0, 0, 0, 0, 24, 241, 206, 423);
        page.AddChild(_certGrid);
        _certCount = InkLabel(page, "", 218, 241, 498, 265, 12);
        Help(page, 576, 239, "Drag certificates onto the slots. Neighbouring certificates " +
            "that form a set light the gem between them and grant a bonus.");
        var box2 = Box(page, 218, 266, 592, 380, EeSkin.ParchmentBox());
        _licCerts = new FlowContainer();
        HudRoot.Pin(_licCerts, 0, 0, 0, 0, 222, 270, 588, 376);
        page.AddChild(_licCerts);
        _licConfirm = DarkBtn(UiTextEn.Translate("Confirm"));
        _licConfirm.TooltipText = UiTextEn.Translate("Changes are applied immediately");
        _licConfirm.Pressed += () =>
        {
            _comboSent[0] = _comboSent[1] = null;
            RefreshLicense();
            var ok = UiTextEn.Translate("Confirm");
            _licConfirm.Text = "✓ " + ok;
            GetTree().CreateTimer(1.2).Timeout += () =>
            {
                if (IsInstanceValid(_licConfirm)) _licConfirm.Text = ok;
            };
        };
        HudRoot.Pin(_licConfirm, 0, 0, 0, 0, 477, 395, 592, 422);
        page.AddChild(_licConfirm);
        _certCover = Cover(page, 12, 229, 604, 434, UiTextEn.Translate(
            "License not obtained. To get the first license, this Class must reach Level 10."));
        PlayerProfileState.Changed += s =>
        {
            if (s is "advability" or "license" or "licenses")
            {
                if (s == "advability") _pendingAa.Clear();
                RefreshLicense();
            }
        };
        return page;
    }

    private FlowContainer? _licCerts;

    /// <summary>Flèche de prérequis (ref : bloc gris biseauté →).</summary>
    private sealed partial class ArrowGlyph : Control
    {
        public override void _Draw()
        {
            var w = Size.X; var h = Size.Y;
            var head = Mathf.Min(w * 0.42f, 10f);
            var pts = new[]
            {
                new Vector2(0, h * 0.32f), new Vector2(w - head, h * 0.32f),
                new Vector2(w - head, 0), new Vector2(w, h * 0.5f),
                new Vector2(w - head, h), new Vector2(w - head, h * 0.68f),
                new Vector2(0, h * 0.68f),
            };
            DrawColoredPolygon(pts, Color.Color8(176, 174, 170));
            DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[4],
                pts[5], pts[6], pts[0] }, Color.Color8(70, 68, 64), 1.5f);
            DrawLine(new Vector2(1, h * 0.4f), new Vector2(w - head, h * 0.4f),
                new Color(1, 1, 1, 0.55f), 1);
        }
    }

    private static Texture2D? TalentIcon(int id)
    {
        var code = Data.Tables.EnchantTable.Get(id)?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return AssetCatalog.GetTexture($"UI/skillicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/itemicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/uiicon/{code}.png");
    }

    private void RefreshLicense()
    {
        if (_talentGrid == null) return;
        foreach (var c in _talentGrid.GetChildren()) c.QueueFree();
        var id = _selected > 0 ? _selected : CurrentClass;
        try
        {
            WarmTalents();
            ClassSkillTree.EnsureLoaded(AssetCatalog.ResolveRawAsset);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[class] tables talents : {e.Message}");
            _talents = new();
        }
        // Arbre propre à la classe (éveil compris), sinon celui de sa base.
        var br = ClassSkillTree.Talents(id);
        var entry = Classes.FirstOrDefault(c => c.ClassId == id);
        var isCurrent = id == CurrentClass;
        var classLevel = entry?.ClassLevel ?? 0;
        if (_talentCover != null)
            _talentCover.Visible = entry != null && classLevel > 0 && classLevel < 2;
        if (_certCover != null)
            _certCover.Visible = entry != null && classLevel > 0 && classLevel < 10;
        if (_kpLabel != null)
        {
            var entered = _pendingAa.Values.Sum();
            _kpLabel.Text = UiTextEn.Translate("%s1 KPs Remain")
                .Replace("%s1", Math.Max(0,
                    KpPool(entry, isCurrent) - entered).ToString());
            if (_kpEntered != null)
                _kpEntered.Text = UiTextEn.Translate("%s1 KPs Entered")
                    .Replace("%s1", entered.ToString());
            if (_kpConfirm != null) _kpConfirm.Disabled = entered == 0;
        }
        // Géométrie : cases 44 px, pas 68×62 (arbre 8 colonnes × 3 branches).
        // 8 colonnes dans la boîte parchemin (408 px) : cases 34, pas 48.
        // Hauteur réduite (retour propriétaire) : 3 rangées pas 54, centrées
        // dans la boîte parchemin de 182 px.
        const int Cell = 34, PitchX = 46, PitchY = 54, OrgX = 34, OrgY = 38;
        // Déblocage : niveau de classe requis + points minimum du prérequis
        // (confirmés + saisis non validés).
        bool Unlocked(Data.Tables.TalentTable.Slot sl)
        {
            if (sl.RequiredLevel > classLevel) return false;
            if (sl.PreSpell == 0) return true;
            PlayerProfileState.Talents.TryGetValue((short)sl.PreSpell, out var a1);
            _pendingAa.TryGetValue((short)sl.PreSpell, out var b1);
            return a1 + b1 >= Math.Max(1, sl.PrePoints);
        }
        for (var b = 0; b < 3; b++)
            for (var c = 0; c < 8; c++)
            {
                var slot = br?[b][c];
                var sx = OrgX + c * PitchX;
                var sy = OrgY + b * PitchY;
                // Flèche de prérequis : de la case PreSpell (même branche, 1 à 5 colonnes
                // plus à gauche, trous inclus) vers cette case — c_talent col « pre ».
                if (slot is { PreSpell: not 0 })
                {
                    var pc = -1;
                    for (var k = c - 1; k >= 0; k--)
                        if (br?[b][k]?.SpellId == slot.PreSpell) { pc = k; break; }
                    if (pc >= 0)
                    {
                        var x0 = OrgX + pc * PitchX + Cell + 1;
                        var ar = new ArrowGlyph
                        {
                            MouseFilter = MouseFilterEnum.Ignore,
                            Position = new Vector2(x0, sy + Cell / 2 - 9),
                            Size = new Vector2(Math.Max(12, sx - x0 - 1), 18),
                        };
                        _talentGrid.AddChild(ar);
                    }
                }
            }
        for (var b = 0; b < 3; b++)
            for (var c = 0; c < 8; c++)
            {
                var slot = br?[b][c];
                var sx = OrgX + c * PitchX;
                var sy = OrgY + b * PitchY;
                var pts = 0;
                var pend = 0;
                var lit = false;
                var unlocked = false;
                if (slot != null)
                {
                    PlayerProfileState.Talents.TryGetValue(
                        (short)slot.SpellId, out var p16);
                    pts = p16;
                    _pendingAa.TryGetValue((short)slot.SpellId, out pend);
                    lit = pts + pend > 0;
                    unlocked = Unlocked(slot);
                }
                // Colonne sans talent : rien n'est dessiné (comme le client natif) — seule la flèche
                // de prérequis peut la traverser.
                if (slot == null) continue;
                var cell = IconSlot(_talentGrid, sx, sy, Cell, TalentIcon(slot.SpellId), lit);
                var maxPts = slot.MaxPoints;
                // Case pas encore accessible : sombre (niveau de classe ou
                // points minimum du prérequis manquants).
                if (!unlocked && pts + pend == 0)
                    cell.Modulate = new Color(0.32f, 0.32f, 0.36f, 1f);
                var sl = slot;
                ItemTooltip.BindCustom(cell, () =>
                {
                    PlayerProfileState.Talents.TryGetValue(
                        (short)sl.SpellId, out var pNow);
                    _pendingAa.TryGetValue((short)sl.SpellId, out var pdNow);
                    PlayerProfileState.Talents.TryGetValue(
                        (short)sl.PreSpell, out var preNow);
                    return ItemTooltip.BuildTalentTip(sl, pNow, pdNow,
                        preNow, entry?.ClassLevel ?? 0);
                });
                // Compteur « pts/max » toujours visible (X/5, X/1), sur
                // pastille sombre ; saisis non validés en vert.
                var lv = new Label
                {
                    Text = $"{pts + pend}/{maxPts}",
                    MouseFilter = MouseFilterEnum.Ignore,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                lv.AddThemeStyleboxOverride("normal", new StyleBoxFlat
                {
                    BgColor = new Color(0, 0, 0, 0.82f),
                    ContentMarginLeft = 1, ContentMarginRight = 1,
                });
                lv.ClipText = true;
                EeSkin.Shadowed(lv, 9, pend > 0
                    ? new Color(0.55f, 1f, 0.55f)
                    : pts + pend >= maxPts ? new Color(1f, 0.85f, 0.3f)
                    : Colors.White);
                // Rect explicite : avec ClipText le min-width du Label
                // vaut 0 → le preset BottomRight le réduisait à un
                // trait de ~2 px au bord droit (compteur illisible).
                HudRoot.Pin(lv, 1, 1, 1, 1, -33, -15, -1, -1);
                cell.AddChild(lv);
                var sid = (short)slot.SpellId;
                if (isCurrent)
                    cell.GuiInput += e =>
                    {
                        if (e is not InputEventMouseButton
                            { Pressed: true } mb) return;
                        _pendingAa.TryGetValue(sid, out var cur);
                        if (mb.ButtonIndex == MouseButton.Left)
                        {
                            var left = KpPool(entry, true) -
                                       _pendingAa.Values.Sum();
                            PlayerProfileState.Talents.TryGetValue(sid, out var p0);
                            if (left > 0 && p0 + cur < maxPts && Unlocked(sl))
                            {
                                _pendingAa[sid] = cur + 1;
                                RefreshLicense();
                            }
                        }
                        else if (mb.ButtonIndex == MouseButton.Right && cur > 0)
                        {
                            // Retire un point non validé ; les talents qui
                            // en dépendent et ne remplissent plus leur
                            // prérequis perdent leurs points saisis.
                            if (cur == 1) _pendingAa.Remove(sid);
                            else _pendingAa[sid] = cur - 1;
                            bool changed;
                            do
                            {
                                changed = false;
                                foreach (var row in br!)
                                    foreach (var dep in row)
                                        if (dep != null && dep.PreSpell != 0 &&
                                            _pendingAa.ContainsKey((short)dep.SpellId) &&
                                            !Unlocked(dep))
                                        {
                                            _pendingAa.Remove((short)dep.SpellId);
                                            changed = true;
                                        }
                            } while (changed);
                            RefreshLicense();
                        }
                    };
            }
        // Ref : « Certificates: N » + icônes des certificats possédés
        // (509 PlayerLicense — licences de la classe active). Chaque
        // icône se glisse vers un slot actif (182) — wiki : clic
        // gauche + déplacement depuis la partie droite.
        List<(short ItemId, byte Loc)> owned;
        lock (typeof(PlayerProfileState))
            owned = PlayerProfileState.Licenses.ToList();
        // Libellé natif « nombre de certificats actuellement équipables » :
        // 7 slots actifs côté serveur (lic_1..lic_7 du 489).
        if (_certCount != null)
            _certCount.Text = UiTextEn.Translate("Certificates: %s1")
                .Replace("%s1", Math.Min(7,
                    World.MobDanger.PlayerLevel / 10 + 1).ToString());
        if (_licCerts != null)
        {
            foreach (var c in _licCerts.GetChildren()) c.QueueFree();
            // Certificats déjà posés sur un slot : ne reviennent pas
            // dans la liste de droite (comme le natif).
            var placed = new HashSet<int>();
            foreach (var l in owned)
                if (l.Loc is >= 1 and <= 7)
                    placed.Add((ushort)l.ItemId);
            foreach (var l in owned)
            {
                var iid = (int)unchecked((ushort)l.ItemId);   // s16 → u16
                if (isCurrent && placed.Contains(iid) && l.Loc is >= 1 and <= 7)
                    continue;
                var d = InventoryWindow.LookupDef(iid);
                var tex = d is { Code.Length: > 0 }
                    ? AssetCatalog.GetTexture($"UI/itemicon/{d.Code}.png")
                    : null;
                var holder = new Control
                    { CustomMinimumSize = new Vector2(36, 36) };
                var cellp = IconSlot(holder, 0, 0, 34, tex);
                var r = (Control)cellp;
                ItemTooltip.BindCustom(r,
                    () => ItemTooltip.BuildCertTip(iid));
                var licId = l.ItemId;
                r.SetDragForwarding(Callable.From((Vector2 _) =>
                {
                    r.SetDragPreview(new TextureRect
                    {
                        Texture = tex,
                        ExpandMode =
                            TextureRect.ExpandModeEnum.IgnoreSize,
                        CustomMinimumSize = new Vector2(30, 30),
                    });
                    return Variant.From(
                        new Godot.Collections.Dictionary
                            { ["licenseId"] = (int)licId });
                }), default, default);
                _licCerts.AddChild(holder);
            }
        }
        FillCertGrid(entry, isCurrent, owned);
    }

    // Positions natives (relatives à Licenseborder) des 7 ComboIcon
    // actifs et des 2 cases additionnelles non gérées par le serveur
    // (ComboIcon11 / ComboIcon13), pas 62 px, case 34 px.
    private static readonly int[] CertX = { 12, 74, 12, 74, 136, 74, 136 };
    private static readonly int[] CertY = { 12, 12, 74, 74, 74, 136, 136 };

    /// <summary>Grille des 7 certificats actifs de la classe sélectionnée
    /// (ComboIcon0-6 natif) + les 2 cases réservées + les 2 gemmes
    /// (SecretIcon0/1). Source : le 509 (locs 1..7) pour la classe
    /// active, <see cref="Protocol.ZonePlayerClassList.Entry.Licenses"/>
    /// (lic_1..lic_7 du 489) pour les autres. Classe possédée → glisser
    /// un certificat depuis la liste le pose (182), clic droit sur le slot
    /// le retire ; les combos par moitié (slots 1-4 / 4-7) allument la
    /// gemme, encadrent les membres en vert et arment le 183 pour la
    /// classe active.</summary>
    private void FillCertGrid(ZonePlayerClassList.Entry? entry, bool isCurrent,
        List<(short ItemId, byte Loc)> owned)
    {
        if (_certGrid == null) return;
        foreach (var c in _certGrid.GetChildren()) c.QueueFree();
        var slotItems = new int[7];
        if (isCurrent)
            foreach (var l in owned)
            {
                if (l.Loc is >= 1 and <= 7)
                    slotItems[l.Loc - 1] = (ushort)l.ItemId;
            }
        else if (entry != null)
            for (var i = 0; i < 7; i++)
                slotItems[i] = entry.Licenses[i];
        var xs = CertX;
        var ys = CertY;
        var classId = isCurrent ? CurrentClass : entry?.ClassId ?? 0;
        var editable = classId > 0;
        // 2 cases supplémentaires (version 9 slots du client) : affichées
        // mais inertes — le serveur ne gère que lic_1..lic_7.
        foreach (var (ex, ey) in new[] { (138, 13), (11, 134) })
        {
            var extra = IconSlot(_certGrid, ex, ey, 34, null);
            extra.Modulate = new Color(1, 1, 1, 0.55f);
            extra.MouseFilter = MouseFilterEnum.Ignore;
        }
        for (var i = 0; i < 7; i++)
        {
            var idx = i;
            var cell = IconSlot(_certGrid, xs[i], ys[i], 34, null);
            Callable drop = default, canDrop = default;
            if (editable)
            {
                canDrop = Callable.From((Vector2 _, Variant d) =>
                    d.VariantType == Variant.Type.Dictionary &&
                    d.AsGodotDictionary().ContainsKey("licenseId"));
                drop = Callable.From((Vector2 _, Variant d) =>
                    SendLicense(classId, slotItems, idx,
                        (short)d.AsGodotDictionary()["licenseId"]
                            .AsInt32()));
                cell.SetDragForwarding(default, canDrop, drop);
            }
            else cell.MouseFilter = MouseFilterEnum.Ignore;
            var iid = slotItems[i];
            if (iid <= 0) continue;
            var d = InventoryWindow.LookupDef(iid);
            var icon = new TextureRect
            {
                Texture = d is { Code.Length: > 0 }
                    ? AssetCatalog.GetTexture($"UI/itemicon/{d.Code}.png")
                    : null,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(xs[i] + 1, ys[i] + 1),
                Size = new Vector2(32, 32),
                MouseFilter = MouseFilterEnum.Stop,
            };
            ItemTooltip.BindCustom(icon,
                () => ItemTooltip.BuildCertTip(iid));
            if (editable)
            {
                // L'icône aussi accepte le drop (sinon le drop sur un
                // slot occupé n'atteint pas la case) ; clic droit =
                // retrait (wiki : le certificat repart en partie
                // droite) ; glisser déplace vers un autre slot.
                var tex = icon.Texture;
                icon.SetDragForwarding(Callable.From((Vector2 _) =>
                    {
                        icon.SetDragPreview(new TextureRect
                        {
                            Texture = tex,
                            ExpandMode =
                                TextureRect.ExpandModeEnum.IgnoreSize,
                            CustomMinimumSize = new Vector2(30, 30),
                        });
                        return Variant.From(
                            new Godot.Collections.Dictionary
                                { ["licenseId"] = (int)(short)iid });
                    }), canDrop, drop);
                icon.GuiInput += e =>
                {
                    if (e is InputEventMouseButton
                        { ButtonIndex: MouseButton.Right, Pressed: true })
                        SendLicense(classId, slotItems, idx, 0);
                };
            }
            _certGrid.AddChild(icon);
        }
        // Moitiés natives : gemme 0 ↔ slots 1-4, gemme 1 ↔ slots 4-7
        // (slot 4 = idx 3 partagé) ; SecretIcon0 (48,48), SecretIcon1
        // (110,110), 24×24.
        CertCombo(0, new[] { 0, 1, 2, 3 }, new Vector2(48, 48),
            xs, ys, slotItems, isCurrent, classId);
        CertCombo(1, new[] { 3, 4, 5, 6 }, new Vector2(110, 110),
            xs, ys, slotItems, isCurrent, classId);
    }

    /// <summary>Combo d'une moitié : meilleur sous-ensemble C_EquipSet
    /// → gemme allumée + carré vert sur les membres + tooltip du bonus
    /// (nom de l'item récompense + description de son enchant) ;
    /// classe active → 183 quand le combo change.</summary>
    private void CertCombo(int flag, int[] idxs, Vector2 gemPos,
        int[] xs, int[] ys, int[] slotItems, bool isCurrent, int classId)
    {
        if (_certGrid == null) return;
        var certs = idxs.Where(i => slotItems[i] > 0)
            .Select(i => (ushort)slotItems[i]).ToArray();
        var combo = Data.Tables.EquipSetTable.BestCombo(certs);
        Gem(gemPos, combo);
        if (combo == null) return;
        // Carré vert sur les slots participants (style « combo actif »).
        foreach (var i in idxs)
        {
            if (!combo.Contains((ushort)slotItems[i])) continue;
            var sq = new Panel
            {
                Position = new Vector2(xs[i] - 1, ys[i] - 1),
                Size = new Vector2(36, 36),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            sq.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),
                BorderColor = Color.Color8(60, 200, 60),
                BorderWidthLeft = 2, BorderWidthRight = 2,
                BorderWidthTop = 2, BorderWidthBottom = 2,
            });
            _certGrid.AddChild(sq);
        }
        // Classe active : le serveur applique l'enchant du combo
        // (InvokeSpellStonesSet) — envoyé quand le set de la moitié
        // change (la classe figure dans la clé : bascule de classe =
        // renvoi, le serveur a purgé +0xb14 au changement de slots).
        if (!isCurrent) return;
        var key = $"{classId}:{string.Join(',', combo)}";
        if (_comboSent[flag] == key) return;
        _comboSent[flag] = key;
        var msg = new Protocol.ZoneServerQueryLicenseSet();
        msg.LicenseIds.AddRange(combo);
        msg.LicenseIds.Add((ushort)flag);
        Send(msg);
    }

    private readonly string?[] _comboSent = new string?[2];

    /// <summary>Envoie le 182 : classe active → la seule sélection
    /// (le serveur résout conflits/retraits) ; autre classe → layout
    /// lic_1..7 complet (le natif le réécrit en entier).</summary>
    private void SendLicense(int classId, int[] slotItems, int slot,
        short licId)
    {
        var msg = new Protocol.ZoneServerPlayerLicense
            { ClassId = (short)classId };
        if (classId == CurrentClass)
        {
            // Retrait natif = (licence à retirer, slot 0) ; (0, slot) serait
            // ignoré par le serveur (licence non possédée).
            if (licId == 0)
            {
                if (slotItems[slot] <= 0) return;
                msg.Selections.Add((unchecked((short)slotItems[slot]), (byte)0));
            }
            else msg.Selections.Add((licId, (byte)(slot + 1)));
        }
        else
        {
            // lic_1..7 est réécrit en entier : le même certificat ne
            // peut rester sur deux slots — on le retire de l'ancien.
            var u = licId & 0xFFFF;
            if (licId != 0)
                for (var i = 0; i < 7; i++)
                    if (slotItems[i] == u) slotItems[i] = 0;
            slotItems[slot] = u;
            foreach (var iid in slotItems.Select((v, i) => (v, i))
                         .Where(x => x.v > 0))
                msg.Selections.Add(
                    (unchecked((short)iid.v), (byte)(iid.i + 1)));
        }
        Send(msg);
    }

    private void Gem(Vector2 pos, List<ushort>? combo)
    {
        var g = new TextureRect
        {
            Texture = _hud?.Region("DejaVuImage",
                combo != null ? "LicenseCombo" : "LicenseNoCombo"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = pos,
            Size = new Vector2(24, 24),
            MouseFilter = combo != null ? MouseFilterEnum.Stop
                : MouseFilterEnum.Ignore,
        };
        if (combo != null)
        {
            // Bonus : tooltip de l'item récompense (38xxx) — son
            // enchant (col23 → t_enchant localisé) décrit le bonus.
            var item = Data.Tables.EquipSetTable.QueryItem(combo);
            ItemTooltip.BindCustom(g,
                () => ItemTooltip.BuildCertTip(item));
        }
        _certGrid?.AddChild(g);
    }

    private Label? _certCount;
    private Control? _certGrid;

    // QA (--class-mock=cur;id:lv,id:lv…) : classes factices pour prouver le
    // rendu des arbres hors serveur (solo n'a pas de 489).
    private static List<ZonePlayerClassList.Entry>? _mock;
    private static int _mockCurrent = -1;

    private static IReadOnlyList<ZonePlayerClassList.Entry> Classes =>
        _mock ?? ClassChangeDialog.Classes;

    private static int CurrentClass =>
        _mockCurrent > 0 ? _mockCurrent :
        PlayerVitals.ActiveClass > 0 ? PlayerVitals.ActiveClass
            : SessionCarry.Character?.ClassId ?? -1;

    private void OnClassesChanged()
    {
        if (_win?.Visible == true) Refresh();
    }

    private void Refresh()
    {
        if (_lastActive <= 0) _lastActive = CurrentClass;
        RefreshNews();
        RefreshLicense();
        RefreshClassList();
        RefreshHeader();
        RefreshBanners();
        RefreshSkills();
    }

    /// <summary>280 attr 0x31 / 0x3ef : KP et CP vivants.</summary>
    private void OnPointsChanged()
    {
        if (PlayerVitals.SkillPoints >= 0) _cp = PlayerVitals.SkillPoints;
        RefreshBanners();
        RefreshLicense();
    }

    /// <summary>Classe « normale » (listée) : 1–20 et le Juge (59). Les
    /// autres (21–58, 60, 61) sont les classes éveil : elles ne vivent
    /// que dans les 2 cases dédiées de l'en-tête.</summary>
    private static bool IsBaseClass(int id) =>
        id % 100 is >= 1 and <= 20 || id % 100 == 59;

    /// <summary>Classe de base d'une classe éveil (21/22 → Guerrier,
    /// 23/24 → Magicien … 57/58 → Épéiste magique, 60/61 → Juge).</summary>
    private static int BaseOf(int id)
    {
        var local = id % 100;
        var race = id - local;
        if (IsBaseClass(id)) return id;
        if (local is >= 21 and <= 58) return race + (local - 19) / 2;
        if (local is 60 or 61) return race + 59;
        return id;
    }

    /// <summary>Première classe éveil de la classe de base locale
    /// <paramref name="local"/> (b → 2b+19, 2b+20 ; Juge 59 → 60/61 ;
    /// Messie 20 : aucune).</summary>
    private static int AwakenedFirst(int local) =>
        local switch
        {
            >= 1 and <= 19 => 2 * local + 19,
            59 => 60,
            _ => -1,
        };

    private void RefreshClassList()
    {
        if (_classList == null) return;
        foreach (var c in _classList.GetChildren()) c.QueueFree();
        var classes = Classes;
        if (_selected <= 0) _selected = CurrentClass;
        // Liste normale = classes de base seulement (les éveils sont
        // dans les 2 cases dédiées).
        foreach (var e in classes.Where(c => IsBaseClass(c.ClassId)))
            _classList.AddChild(ClassRow(e));
        // Classes de base non encore débloquées, grisées avec cadenas
        // (« Class Requirement » au survol). La race se déduit du 489
        // (même préfixe ×100) — CurrentClass peut être encore inconnu
        // si l'attr 280 0x2c n'est pas arrivé.
        var race = CurrentClass > 0 ? CurrentClass / 100
            : classes.Count > 0 ? classes[0].ClassId / 100 : -1;
        if (classes.Count > 0 && race >= 0)
        {
            for (var n = 1; n <= 59; n++)
            {
                var id = race * 100 + n;
                if (!IsBaseClass(id)) continue;
                if (classes.Any(c => c.ClassId == id)) continue;
                if (TextResolver.ClassName(id) == null) continue;
                _classList.AddChild(ClassRow(
                    new ZonePlayerClassList.Entry { ClassId = id }));
            }
        }
        if (classes.Count == 0)
        {
            var l = new Label { Text = UiTextEn.Translate("Waiting for the class list…") };
            l.AddThemeColorOverride("font_color", Ink);
            _classList.AddChild(l);
        }
        var cur = classes.FirstOrDefault(c => c.ClassId == CurrentClass);
        if (cur != null) _cp = cur.ClassPoints;
    }

    /// <summary>Sélectionne une classe (base ou éveil possédée) et
    /// rafraîchit toutes les pages.</summary>
    private void SelectClass(int cid)
    {
        _selected = cid;
        RefreshClassList();
        RefreshHeader();
        RefreshNews();
        RefreshSkills();
        RefreshLicense();
    }

    /// <summary>Ligne de classe (ref : carte sombre 54 px — icône,
    /// nom, Lv, cadenas ou « Transform! » ; courante = bord vert).</summary>
    private Control ClassRow(ZonePlayerClassList.Entry e)
    {
        var unlocked = e.ClassLevel > 0;
        var current = e.ClassId == BaseOf(CurrentClass);
        var selected = e.ClassId == BaseOf(_selected);
        var row = new Panel
        {
            Name = $"ClassRow_{e.ClassId}",
            CustomMinimumSize = new Vector2(0, 54),
            MouseFilter = MouseFilterEnum.Stop,
        };
        row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = selected
                ? Color.Color8(66, 66, 66) : Color.Color8(40, 40, 38),
            BorderColor = current ? Color.Color8(90, 220, 150)
                                  : Color.Color8(20, 20, 20),
            BorderWidthTop = current ? 2 : 1, BorderWidthBottom = current ? 2 : 1,
            BorderWidthLeft = current ? 2 : 1, BorderWidthRight = current ? 2 : 1,
        });
        var icon = new TextureRect
        {
            Texture = CharSelectScreen.ClassIcon(e.ClassId),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = unlocked ? Colors.White : new Color(1, 1, 1, 0.45f),
        };
        if (!unlocked) icon.Material = GrayMat();
        HudRoot.Pin(icon, 0, 0, 0, 0, 6, 8, 44, 46);
        row.AddChild(icon);
        var name = new Label
        {
            Text = TextResolver.ClassName(e.ClassId) ?? UiTextEn.Translate("Class %s1").Replace("%s1", e.ClassId.ToString()),
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true,
        };
        HudRoot.Pin(name, 0, 0, 0, 0, 52, 6, 124, 24);
        EeSkin.Shadowed(name, 12, unlocked ? Light
            : Color.Color8(168, 168, 162));
        row.AddChild(name);
        if (unlocked)
        {
            var lv = new Label
            {
                Text = UiTextEn.Translate("Lv.%s1").Replace("%s1", e.ClassLevel.ToString()), MouseFilter = MouseFilterEnum.Ignore,
            };
            HudRoot.Pin(lv, 0, 0, 0, 0, 126, 6, 180, 24);
            EeSkin.Shadowed(lv, 12, Light);
            row.AddChild(lv);
        }
        if (current)
        {
            var tag = new Label
            {
                Text = UiTextEn.Translate("Current Class"), MouseFilter = MouseFilterEnum.Ignore,
            };
            HudRoot.Pin(tag, 0, 0, 0, 0, 52, 28, 180, 46);
            EeSkin.Shadowed(tag, 11, Color.Color8(150, 240, 170));
            row.AddChild(tag);
        }
        else if (unlocked)
        {
            var tr = EeSkin.Grey(new Button
            {
                Text = UiTextEn.Translate("Transform!"), FocusMode = FocusModeEnum.None,
            }, 11);
            HudRoot.Pin(tr, 1, 0, 1, 0, -92, 28, -8, 48);
            var id = e.ClassId;
            tr.Pressed += () => ClassChangeDialog.RequestChange(id);
            row.AddChild(tr);
        }
        else
        {
            // Cadenas : pastille noire en bas à droite (ref).
            var lockL = new Label
            {
                Text = "🔒", MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            lockL.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = Color.Color8(14, 14, 14),
                BorderColor = Color.Color8(100, 100, 96),
                BorderWidthTop = 1, BorderWidthBottom = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            });
            lockL.AddThemeFontSizeOverride("font_size", 10);
            HudRoot.Pin(lockL, 1, 0, 1, 0, -26, 30, -8, 48);
            row.AddChild(lockL);
        }
        var cid = e.ClassId;
        // Tooltip de classe sur toute la ligne (description +
        // « Class Requirement: … » rouge si verrouillée — ref
        // HoverToolTipClasse/shot_0048_000033).
        ItemTooltip.BindCustom(row,
            () => ItemTooltip.BuildClassTip(cid, unlocked));
        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left })
            {
                // Classe non débloquée : aperçu lecture seule (talents,
                // sorts, éveil) — voir IsPreview.
                SelectClass(cid);
            }
        };
        return row;
    }

    private void RefreshHeader()
    {
        if (_headName != null)
            _headName.Text = (TextResolver.ClassName(_selected) ?? "") +
                (IsPreview ? $" ({PreviewTag})" : "");
        var e = Classes
            .FirstOrDefault(c => c.ClassId == _selected);
        if (_activate != null)
            _activate.Disabled = e is not { ClassLevel: > 0 } ||
                                 _selected == CurrentClass;
        RefreshAwakened();
    }

    private readonly List<Tween> _headTweens = new();

    /// <summary>Classe affichée non possédée : aperçu en lecture seule.</summary>
    private bool IsPreview
    {
        get
        {
            var id = _selected > 0 ? _selected : CurrentClass;
            return id > 0 && !Classes.Any(c =>
                c.ClassId == id && c.ClassLevel > 0);
        }
    }

    private static string PreviewTag =>
        UiTextEn.Translate("Preview") is var t && t != "Preview" ? t : "Aperçu";

    /// <summary>En-tête : 3 grandes icônes — classe de base de la
    /// branche affichée + ses 2 classes éveil. Verrouillée = silhouette
    /// désaturée + cadenas ; sélectionnée = coins dorés animés ; active =
    /// ruban bleu ; possédée = clic pour l'afficher.</summary>
    private void RefreshAwakened()
    {
        if (_awakened == null || _awakenedLv == null) return;
        foreach (var t in _headTweens) t.Kill();
        _headTweens.Clear();
        var baseId = BaseOf(_selected);
        var race = baseId / 100;
        var a0 = AwakenedFirst(baseId % 100);
        var ids = new[] { baseId,
            a0 > 0 ? race * 100 + a0 : -1,
            a0 > 0 ? race * 100 + a0 + 1 : -1 };
        for (var i = 0; i < 3; i++)
        {
            var cell = _awakened[i];
            foreach (var ch in cell.GetChildren()) ch.QueueFree();
            var cid = ids[i];
            cell.Visible = cid > 0;
            _awakenedLv[i].Text = "";
            if (cid <= 0) continue;
            var entry = Classes
                .FirstOrDefault(c => c.ClassId == cid);
            var owned = entry is { ClassLevel: > 0 };
            var ic = new TextureRect
            {
                Texture = CharSelectScreen.ClassIcon(cid),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = owned ? Colors.White : new Color(1, 1, 1, 0.8f),
            };
            if (!owned) ic.Material = GrayMat();
            ic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            cell.AddChild(ic);
            ItemTooltip.BindCustom(ic,
                () => ItemTooltip.BuildClassTip(cid, owned));
            if (owned)
                _awakenedLv[i].Text = UiTextEn.Translate("Lv.%s1")
                    .Replace("%s1", entry!.ClassLevel.ToString());
            else
            {
                var lk = new Label
                {
                    Text = "🔒", MouseFilter = MouseFilterEnum.Ignore,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                lk.AddThemeStyleboxOverride("normal", new StyleBoxFlat
                {
                    BgColor = new Color(0, 0, 0, 0.75f),
                    CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                    CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                });
                lk.AddThemeFontSizeOverride("font_size", 12);
                HudRoot.Pin(lk, 1, 1, 1, 1, -20, -20, -2, -2);
                cell.AddChild(lk);
            }
            if (cid == _selected) AddSelectionCorners(cell);
            if (cid == CurrentClass)
            {
                // Ruban bleu = classe active (ref).
                var rb = new Panel { MouseFilter = MouseFilterEnum.Ignore };
                rb.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = Color.Color8(40, 110, 220),
                    BorderColor = Colors.White,
                    BorderWidthTop = 1, BorderWidthBottom = 1,
                    BorderWidthLeft = 1, BorderWidthRight = 1,
                });
                HudRoot.Pin(rb, 1, 1, 1, 1, -14, -18, -2, -2);
                cell.AddChild(rb);
            }
            // Éveil visible par classe : une éveillée (ou la base) non
            // possédée se prévisualise en lecture seule (sorts, talents,
            // fiche) — aucune montée ni « Activer » (voir IsPreview).
            ic.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton
                    { Pressed: true, ButtonIndex: MouseButton.Left } &&
                    cid != _selected)
                    SelectClass(cid);
            };
        }
    }

    /// <summary>Quatre crochets dorés aux coins de l'icône sélectionnée,
    /// qui « respirent » (ref : coins animés).</summary>
    private void AddSelectionCorners(Control cell)
    {
        var gold = Color.Color8(255, 214, 80);
        var holder = new Control { MouseFilter = MouseFilterEnum.Ignore };
        holder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cell.AddChild(holder);
        const int len = 11, th = 3, off = -2;
        void Bar(float ax, float ay, float w, float h)
        {
            holder.AddChild(new ColorRect
            {
                Color = gold, MouseFilter = MouseFilterEnum.Ignore,
                Size = new Vector2(w, h),
                Position = new Vector2(ax == 0 ? off : 48 - off - w,
                    ay == 0 ? off : 48 - off - h),
            });
        }
        Bar(0, 0, len, th); Bar(0, 0, th, len);
        Bar(1, 0, len, th); Bar(1, 0, th, len);
        Bar(0, 1, len, th); Bar(0, 1, th, len);
        Bar(1, 1, len, th); Bar(1, 1, th, len);
        var tw = holder.CreateTween().SetLoops();
        tw.TweenProperty(holder, "modulate:a", 0.45f, 0.7);
        tw.TweenProperty(holder, "modulate:a", 1f, 0.7);
        _headTweens.Add(tw);
    }

    private void RefreshBanners()
    {
        var money = PlayerVitals.Gold;
        if (_goldBanner != null)
            _goldBanner.Text = UiTextEn.Translate(
                    "%s1 Gold %s2 Silver Available")
                .Replace("%s1", $"{money / 100:#,0}")
                .Replace("%s2", (money % 100).ToString());
        if (_cpBanner != null)
            _cpBanner.Text = UiTextEn.Translate("%s1 remaining CP")
                .Replace("%s1", _cp.ToString());
    }

    /// <summary>Sorts de la classe affichée (tables EDENFR, voir
    /// <see cref="ClassSkillTree"/>) : « Reg. Skills » (or) et « Class
    /// Skills » (CP) listés pour TOUTE classe sélectionnée, active ou non
    /// et éveil comprise. Le niveau vient de player_spells (global) ;
    /// non appris = « Not learned ». Boutons +1/Max et drag seulement sur
    /// la classe active pour un sort appris.</summary>
    private void RefreshSkills()
    {
        if (_regGrid == null || _classGrid == null) return;
        if (_win?.Visible != true) return;   // l'ouverture rappelle Refresh()
        try { RefreshSkillsCore(); }
        catch (Exception ex)
        {
            // KB-15 : jamais de grille vide muette (modèle RefreshLicense).
            GD.PrintErr($"[skill] RefreshSkills : {ex}");
        }
    }

    private void RefreshSkillsCore()
    {
        foreach (var c in _regGrid!.GetChildren()) c.QueueFree();
        foreach (var c in _classGrid!.GetChildren()) c.QueueFree();
        var sel = _selected > 0 ? _selected : CurrentClass;
        if (sel <= 0) return;
        ClassSkillTree.Log ??= m => GD.Print(m);
        ClassSkillTree.EnsureLoaded(AssetCatalog.ResolveRawAsset);
        var known = PlayerSkills.Spells.ToDictionary(k => (int)k.SpellMaster,
            k => (int)k.Level);
        var invest = sel == CurrentClass;
        var race = SessionCarry.Character is { } ch ? ch.RaceId - 1 : -1;
        void Add(ClassSkillTree.Skill s, int pay, Control grid)
        {
            var lv = known.GetValueOrDefault(s.Id, 0);
            grid.AddChild(SkillCell(s.Id, lv, pay, lv > 0, invest && lv > 0));
        }
        foreach (var s in ClassSkillTree.RegSkills(sel, race))
            Add(s, 1, _regGrid);
        foreach (var s in ClassSkillTree.ClassSkills(sel))
            Add(s, 9, _classGrid);
    }

    /// <summary>Coût du niveau <paramref name="level"/>+1 (−1 si
    /// inconnu) — base C_Spell col43 × taux C_SpellReference du niveau
    /// suivant / 100 (formule serveur <c>TrySpellLevelUpAsync</c>).</summary>

    private static Dictionary<int, (int Cp, int Gold)>? _upRates;

    /// <summary>Coût du niveau <paramref name="level"/>+1 (−1 si
    /// inconnu).</summary>
    private static int NextCost(int id, int level, int pay)
    {
        try
        {
            if (_upRates == null)
            {
                _upRates = new();
                var path = AssetCatalog.ResolveRawAsset(
                    "data/db/c_spellreference.ini");
                if (path != null)
                    foreach (var r in IniTable.ParseFile(path).Rows)
                        _upRates[IniTable.GetInt(r, 0)] =
                            (IniTable.GetInt(r, 30), IniTable.GetInt(r, 31));
            }
            var b = ClassSkillTree.Get(id)?.UpgradeBase ?? 0;
            if (b <= 0 || !_upRates.TryGetValue(level + 1, out var rt))
                return -1;
            return b * (pay == 9 ? rt.Cp : rt.Gold) / 100;
        }
        catch { return -1; }
    }

    /// <summary>Case de sort (ref : icône 32, nom sur bandeau gris,
    /// « Lv.N », boutons « +1 » / « Max »).</summary>
    private Control SkillCell(int spellMaster, int level, int pay,
        bool learned, bool invest)
    {
        var cell = new Control
        {
            Name = $"SkillCell_{spellMaster}",
            CustomMinimumSize = new Vector2(166, 38),
        };
        var icon = new TextureRect
        {
            Texture = SpellCatalog.Icon(spellMaster),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = learned ? Colors.White : new Color(1, 1, 1, 0.45f),
        };
        HudRoot.Pin(icon, 0, 0, 0, 0, 0, 2, 34, 36);
        // Tooltip enrichi (coût MP, cast, cooldown, description de
        // l'enchant lié, « Next level », coût de montée en rouge) sur
        // toute la case — ET sur l'icône qui stoppe le survol pour
        // son drag/clic (mouse_entered ne remonte pas aux parents).
        var tipId = spellMaster;
        Func<Control?> build = () => ItemTooltip.BuildSkillTip(
            tipId, level, pay, learned,
            learned ? NextCost(tipId, level, pay) : -1);
        ItemTooltip.BindCustom(cell, build);
        ItemTooltip.BindCustom(icon, build);
        // M3 : l'icône se glisse vers la hotbar (UpdateShortcut) ou se
        // prend par simple clic (mode clic-prendre / clic-poser).
        var spellId = spellMaster;
        if (invest)
        {
            icon.SetDragForwarding(Callable.From((Vector2 _) =>
            {
                icon.SetDragPreview(new TextureRect
                {
                    Texture = icon.Texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    CustomMinimumSize = new Vector2(34, 34),
                });
                return new Godot.Collections.Dictionary
                    { ["spellId"] = spellId };
            }), default, default);
            var press = Vector2.Zero;
            var moved = false;
            icon.GuiInput += e =>
            {
                switch (e)
                {
                    case InputEventMouseButton
                        { ButtonIndex: MouseButton.Left } mb:
                        if (mb.Pressed) { press = mb.GlobalPosition; moved = false; }
                        else if (!moved)
                            HotbarPanel.PickPayload(Variant.From(
                                new Godot.Collections.Dictionary
                                    { ["spellId"] = spellId }),
                                icon.Texture);
                        break;
                    case InputEventMouseMotion mm:
                        if ((mm.GlobalPosition - press).Length() > 4)
                            moved = true;
                        break;
                }
            };
        }
        cell.AddChild(icon);
        var band = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        HudRoot.Pin(band, 0, 0, 0, 0, 38, 2, 166, 18);
        band.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.Color8(150, 148, 140),
            BorderColor = Color.Color8(100, 98, 92),
            BorderWidthBottom = 1,
        });
        cell.AddChild(band);
        var name = new Label
        {
            Text = SpellCatalog.Name(spellMaster), ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(name, 0, 0, 0, 0, 42, 1, 166, 18);
        EeSkin.Shadowed(name, 11);
        cell.AddChild(name);
        var lv = new Label
        {
            Text = learned ? UiTextEn.Translate("Lv.%s1").Replace("%s1", level.ToString()) : UiTextEn.Translate("Not learned"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(lv, 0, 0, 0, 0, 40, 20, 100, 36);
        lv.AddThemeFontSizeOverride("font_size", 11);
        lv.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
        cell.AddChild(lv);
        if (!invest) return cell;
        var id = (short)spellMaster;
        // Montée de sort : seulement pour la classe ACTIVE (le serveur débite ses PC et
        // plafonne au niveau de cette classe).
        var canUp = _selected <= 0 || _selected == CurrentClass;
        var plus = SmallButton("+1", 104, 132);
        plus.Disabled = !canUp;
        plus.Pressed += () => Send(new ZoneUseSkillPoint { SpellId = id });
        cell.AddChild(plus);
        var max = SmallButton(UiTextEn.Translate("Max"), 134, 166);
        max.Disabled = !canUp;
        max.Pressed += () => Send(new ZoneUseSkillPointMax { SpellId = id });
        cell.AddChild(max);
        return cell;
    }

    private static Button SmallButton(string text, int l, int r)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        HudRoot.Pin(b, 0, 0, 0, 0, l, 20, r, 36);
        var sb = new StyleBoxFlat
        {
            BgColor = Color.Color8(36, 35, 32),
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        };
        foreach (var st in new[] { "normal", "hover", "pressed" })
            b.AddThemeStyleboxOverride(st, sb);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontSizeOverride("font_size", 10);
        b.AddThemeColorOverride("font_color", Color.Color8(225, 225, 220));
        return b;
    }

    private static void Send(Net.NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        _ = s.SendZoneAsync(msg, System.Threading.CancellationToken.None)
            .ContinueWith(t => GD.PrintErr($"[class] {msg.Name} : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>286 ClientGetSpell `[s16 spell][s16 req][s16 niveau]
    /// [i32 CP restants]` → niveau du sort + CP.</summary>
    private void OnMisc(Net.NetMessage m)
    {
        if (m is not ZoneClientGetSpell g) return;
        _cp = g.Field4;
        // Field1 = s16 signé (50058 → -15478) : le 311 stocke l'id non signé.
        PlayerSkills.SetLevel(unchecked((ushort)g.Field1), g.Field3);
        RefreshBanners();
        GD.Print($"[class] 286 spell={g.Field1} lv={g.Field3} cp={g.Field4}");
    }
}

