using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Game;
using EdenClient.Protocol;
using EdenClient.Screens;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre « Character Info » (ref profile.png / profile-Title.png,
/// touche C) — construite en code avec <see cref="EeSkin"/> :
///
///   - en-tête : couronne + nom + « LV n » | icône de classe + classe
///     + « LV n » ;
///   - onglets Character Stats / Title / Fame / Achievement ;
///   - Character Stats : 4 blocs (Core Stats, Physical Offensive,
///     Magical Offensive, Defensive Attributes) alimentés par le 296
///     (<see cref="PlayerVitals.TotalStats"/>), aperçu 3D du perso et
///     slots d'équipement (<see cref="EquipSlotsPanel"/> intégré — il
///     applique aussi l'équipement au corps 3D) ;
///   - Title : titres possédés (<see cref="PlayerTitles"/>) + « Set
///     Title » (C→S 93) ; Achievement : ouvre la fenêtre des succès.
/// </summary>
public partial class ProfileWindow : Control
{
    /// <summary>Noms de races natives (ordre zone désasm : 1 Human,
    /// 2 Zumi, 3 Anuran, 4 Ursun, 5 Halfkin).</summary>
    private static readonly string[] RaceNames =
        { "?", "Human", "Zumi", "Anuran", "Ursun", "Halfkin" };

    private HudRoot? _hud;
    private Panel? _win;
    /// <summary>Panneau visible (registre <see cref="HudWindows"/>).</summary>
    public Control? Window => _win;
    private Label? _name, _lv, _cls, _classLv;
    private TextureRect? _classIcon;
    private int _level, _classLevel, _classId;
    private string _charName = "", _className = "";
    private int _raceId;
    private readonly Dictionary<string, Label> _values = new();
    private readonly Dictionary<string, Label> _names = new();
    private readonly List<Button> _tabs = new();
    private readonly List<Control> _pages = new();
    private Node3D? _previewHolder;
    private Camera3D? _previewCam;

    public void Build(HudRoot hud)
    {
        _hud = hud;
        _win = EeSkin.MakeWindow(hud, "Character Info", 50, 8, 653, 660);
        AddChild(_win);
        BuildHeader(_win);
        string[] tabs = { "Character Stats", "Title", "Fame", "Achievement" };
        for (var i = 0; i < tabs.Length; i++)
        {
            var b = new Button
            {
                Text = UiTextEn.Translate(tabs[i]), ToggleMode = true,
                FocusMode = FocusModeEnum.None,
            };
            HudRoot.Pin(b, 0, 0, 0, 0, 10 + i * 98, 50, 104 + i * 98, 68);
            b.AddThemeFontSizeOverride("font_size", 11);
            b.AddThemeColorOverride("font_color", Color.Color8(58, 34, 92));
            b.AddThemeColorOverride("font_pressed_color", Color.Color8(40, 28, 64));
            b.AddThemeColorOverride("font_hover_pressed_color", Color.Color8(40, 28, 64));
            b.AddThemeColorOverride("font_hover_color", Color.Color8(20, 10, 40));
            if (EeSkin.Tab(hud, false) is { } n)
                foreach (var st in new[] { "normal", "hover" })
                    b.AddThemeStyleboxOverride(st, n);
            if (EeSkin.Tab(hud, true) is { } sel)
                foreach (var st in new[] { "pressed", "hover_pressed" })
                    b.AddThemeStyleboxOverride(st, sel);
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            var idx = i;
            b.Pressed += () => SelectTab(idx);
            _tabs.Add(b);
            _win.AddChild(b);
        }
        // A5.5 : fenêtre « Statistiques avancées ».
        var adv = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("Advanced Statistics"),
            FocusMode = FocusModeEnum.None,
        }, 11);
        // Remplacé par le clic sur chaque stat (ProfileStatInfo.ShowDetail).
        adv.QueueFree();
        _pages.Add(BuildStatsPage(_win));
        HookStatClicks();
        _pages.Add(BuildTitlePage(_win));
        _pages.Add(InfoPage(_win, "Fame", "Fame data is not sent by the " +
            "server yet."));
        _pages.Add(BuildAchievementPage(_win));
        SelectTab(0);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--profile-tab=") &&
                int.TryParse(a[14..], out var ti)) SelectTab(ti);
        // --profile-equip : tenue de test (Clair de Lune, tête/corps/
        // ceinture/mains/bottes) injectée dans l'état porté — capture
        // des libellés d'armure sans serveur.
        if (OS.GetCmdlineUserArgs().Contains("--profile-equip"))
            PlayerEquipment.ApplyEnterNode(new EquippedItem[]
            {
                new(1, 0, 20162, 0, 1, 0), new(2, 0, 20163, 0, 1, 0),
                new(3, 0, 20164, 0, 1, 0), new(4, 0, 20165, 0, 1, 0),
                new(5, 0, 20166, 0, 1, 0),
            });

        AchievementWindow.TitleLinkClicked += FocusTitle;
        PlayerVitals.StatsChanged += RefreshStats;
        PlayerTitles.Changed += RefreshTitles;
        RefreshStats();
        RefreshTitles();
        DragHelper.Persist(_win, "character");
        _win.Visible = false;
        // Réouverture : re-duplique le corps (tenue/armes changées
        // depuis le dernier SetPreview).
        _win.VisibilityChanged += () =>
        {
            if (_win.Visible && _previewSrc != null &&
                GodotObject.IsInstanceValid(_previewSrc))
                SetPreview(_previewSrc);
        };
    }

    public override void _ExitTree()
    {
        AchievementWindow.TitleLinkClicked -= FocusTitle;
        PlayerVitals.StatsChanged -= RefreshStats;
        PlayerTitles.Changed -= RefreshTitles;
        if (_armorClassHandler != null)
            PlayerVitals.ClassChanged -= _armorClassHandler;
    }

    // ------------------------------------------------------------
    // En-tête / onglets

    private void BuildHeader(Panel win)
    {
        var band = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        HudRoot.Pin(band, 0, 0, 1, 0, 8, 22, -8, 46);
        band.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        win.AddChild(band);
        var crown = new Label { Text = "♛", MouseFilter = MouseFilterEnum.Ignore };
        HudRoot.Pin(crown, 0, 0, 0, 0, 8, 0, 30, 24);
        EeSkin.Shadowed(crown, 14, Color.Color8(240, 170, 40));
        band.AddChild(crown);
        _name = HeaderLabel(band, 36, 260, HorizontalAlignment.Left);
        _lv = HeaderLabel(band, 230, 300, HorizontalAlignment.Right);
        _classIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(_classIcon, 0, 0, 0, 0, 316, 2, 346, 22);
        band.AddChild(_classIcon);
        _cls = HeaderLabel(band, 352, 560, HorizontalAlignment.Center);
        _classLv = HeaderLabel(band, 560, 628, HorizontalAlignment.Right);
    }

    private static Label HeaderLabel(Control band, int l, int r,
        HorizontalAlignment align)
    {
        var lab = new Label
        {
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudRoot.Pin(lab, 0, 0, 0, 1, l, 0, r, 0);
        EeSkin.Shadowed(lab, 12);
        band.AddChild(lab);
        return lab;
    }

    private void SelectTab(int i)
    {
        for (var k = 0; k < _tabs.Count; k++)
        {
            _tabs[k].ButtonPressed = k == i;
            if (k < _pages.Count) _pages[k].Visible = k == i;
        }
        if (i == 3) _achEmbed?.Activate();
    }

    private static Panel Page(Panel win)
    {
        var p = new Panel();
        HudRoot.Pin(p, 0, 0, 1, 1, 8, 68, -8, -8);
        p.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        win.AddChild(p);
        return p;
    }

    private static Control InfoPage(Panel win, string title, string text)
    {
        var p = Page(win);
        var l = new Label
        {
            Text = $"{title}\n\n{text}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        HudRoot.Pin(l, 0, 0, 1, 0, 16, 14, -16, 90);
        EeSkin.Shadowed(l, 12, Color.Color8(210, 210, 206));
        p.AddChild(l);
        return p;
    }

    // ------------------------------------------------------------
    // Character Stats

    /// <summary>Blocs de stats (ref) : (titre, lignes (clé, libellé,
    /// couleur valeur)).</summary>
    private static readonly (string Title, (string Key, string Label)[] Rows)[]
        Blocks =
    {
        ("Core Stats", new[]
        {
            ("str", UiTextEn.Translate("STR")), ("agi", UiTextEn.Translate("AGI")), ("int", UiTextEn.Translate("INT")), ("wis", UiTextEn.Translate("WIS")),
            ("lck", UiTextEn.Translate("LCK")), ("acc", UiTextEn.Translate("ACC")), ("move", "Move SPD"),
        }),
        ("Physical Offensive", new[]
        {
            ("patk", "P-ATK"), ("aspd", UiTextEn.Translate("ATK SPD")), ("crit", "CRIT Rate"),
            ("critdmg", "CRIT DMG"),
        }),
        ("Magical Offensive", new[]
        {
            ("matk", UiTextEn.Translate("M-ATK")), ("cspd", "Cast SPD"), ("mcrit", "M-CRIT Rate"),
            ("mcritdmg", "M-CRIT DMG"),
        }),
        ("Defensive Attributes", new[]
        {
            ("def", UiTextEn.Translate("DEF")), ("eva", UiTextEn.Translate("EVA")), ("parry", "Parry"),
            ("block", UiTextEn.Translate("Block")),
        }),
    };

    private Control BuildStatsPage(Panel win)
    {
        var page = Page(win);
        if (_hud?.Build("EquipmentWnd_new2.layout") is { } nat)
        {
            BuildNativeStats(page, nat);
            return page;
        }
        var y = 6;
        foreach (var (title, rows) in Blocks)
        {
            var h = 26 + rows.Length * 19 + 6;
            var box = new Panel();
            HudRoot.Pin(box, 0, 0, 0, 0, 6, y, 250, y + h);
            box.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
            page.AddChild(box);
            var t = new Label
            {
                Text = UiTextEn.Translate(title), HorizontalAlignment = HorizontalAlignment.Center,
            };
            HudRoot.Pin(t, 0, 0, 1, 0, 0, 3, 0, 21);
            t.AddThemeFontSizeOverride("font_size", 12);
            t.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
            box.AddChild(t);
            var help = new Button
            {
                Text = "?", FocusMode = FocusModeEnum.None,
                TooltipText = title,
            };
            EeSkin.Grey(help, 10);
            HudRoot.Pin(help, 1, 0, 1, 0, -22, 3, -4, 19);
            box.AddChild(help);
            for (var i = 0; i < rows.Length; i++)
            {
                var ry = 24 + i * 19;
                var lab = StatCell(box, 6, 104, ry, Color.Color8(190, 186, 174));
                lab.AddThemeColorOverride("font_color", Color.Color8(25, 25, 22));
                lab.RemoveThemeColorOverride("font_shadow_color");
                lab.Text = rows[i].Label;
                _names[rows[i].Key] = lab;
                ProfileStatInfo.Hint(lab, rows[i].Key);
                var val = StatCell(box, 108, 238, ry, Color.Color8(232, 230, 222));
                val.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
                _values[rows[i].Key] = val;
                ProfileStatInfo.Hint(val, rows[i].Key);
            }
            y += h + 6;
        }

        // Aperçu 3D (sous-viewport plein corps) + équipement porté.
        var view = new Panel();
        HudRoot.Pin(view, 0, 0, 0, 0, 258, 6, 452, 380);
        view.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.Color8(196, 192, 182),
            BorderColor = EeSkin.DarkLine,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        page.AddChild(view);
        MountPreview(view);
        if (_hud != null)
        {
            var equip = new EquipSlotsPanel { Name = "EquipPanel" };
            equip.Build(_hud, embedded: true);
            equip.Position = new Vector2(458, 2);
            page.AddChild(equip);
        }
        BuildGearArea(page);
        return page;
    }

    /// <summary>Taille unique des titres de sets d'armure.</summary>
    private const int ArmorLabelSize = 11;

    private readonly List<Label> _resistVals = new();
    private readonly Dictionary<string, Label> _nat = new();

    private Label? NL(Control root, string path) =>
        TopLeftVitals.Find<Label>(root, path);

    /// <summary>Profil fidèle au jeu d'origine : layout natif
    /// <c>EquipmentWnd_new2</c> (stats en 4 blocs + valeur/pourcentage,
    /// modèle, accessoires, armes/bijoux, 3 sets d'armure, résistances) ;
    /// les libellés sont posés ici, les slots reçoivent des cellules
    /// d'équipement réelles.</summary>
    private void BuildNativeStats(Control page, Control nat)
    {
        page.AddChild(nat);
        HudRoot.Pin(nat, 0, 0, 0, 0, 0, 0, 637, 573);
        var ink = Colors.Black; // TextColours FF000000 du layout
        (string Sec, string Title, (string Name, int NameIdx, int Type, int Pct, string Key)[] Rows)[] secs =
        {
            ("BaseProperty", "Core Stats", new[]
            {
                (UiTextEn.Translate("STR"), 1, 1, 0, "str"), (UiTextEn.Translate("AGI"), 2, 2, 0, "agi"),
                (UiTextEn.Translate("INT"), 3, 3, 0, "int"), (UiTextEn.Translate("WIS"), 4, 4, 0, "wis"),
                (UiTextEn.Translate("LCK"), 5, 5, 0, "lck"),
                (UiTextEn.Translate("ACC"), 6, 6, 8, "acc"), (UiTextEn.Translate("Move SPD"), 7, 7, 9, "move"),
            }),
            ("AtkProperty", "Physical Offensive", new[]
            {
                ("P-ATK", 1, 1, 0, "patk"), (UiTextEn.Translate("ATK SPD"), 2, 2, 5, "aspd"),
                ("P-Healing", 5, 8, 0, "pheal"),
                ("CRIT Rate", 3, 3, 6, "crit"), ("CRIT DMG", 4, 4, 7, "critdmg"),
            }),
            ("MgcProperty", "Magical Offensive", new[]
            {
                (UiTextEn.Translate("M-ATK"), 1, 1, 0, "matk"), ("Cast SPD", 2, 2, 5, "cspd"),
                ("G-Healing", 5, 8, 0, "gheal"),
                ("M-CRIT Rate", 3, 3, 6, "mcrit"), ("M-CRIT DMG", 4, 4, 7, "mcritdmg"),
            }),
            ("DefProperty", "Defensive Attributes", new[]
            {
                (UiTextEn.Translate("DEF"), 1, 1, 0, "def"), (UiTextEn.Translate("EVA"), 2, 2, 5, "eva"),
                ("Parry", 3, 3, 6, "parry"), (UiTextEn.Translate("Block"), 4, 4, 7, "block"),
            }),
        };
        // Cellules : le look natif fait tout (Name* = fond
        // HintFrameBackdrop beige, Type* = inset Static2*), texte en
        // encre noire comme le layout (TextColours FF000000).
        void Cell(Label l)
        {
            l.VerticalAlignment = VerticalAlignment.Center;
            l.HorizontalAlignment = HorizontalAlignment.Center;
            EeSkin.Shadowed(l, 11, ink);
            l.RemoveThemeColorOverride("font_shadow_color");
        }
        foreach (var (sec, title, rows) in secs)
        {
            if (NL(nat, $"{sec}_Title") is { } t)
            {
                t.Text = UiTextEn.Translate(title);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                EeSkin.Shadowed(t, 12, ink);
                t.RemoveThemeColorOverride("font_shadow_color");
            }
            foreach (var (name, nameIdx, type, pct, key) in rows)
            {
                if (NL(nat, $"{sec}_Border_Name{nameIdx}") is { } lbl)
                {
                    lbl.Text = name;
                    Cell(lbl);
                    _names[key] = lbl;
                    ProfileStatInfo.Hint(lbl, key);
                }
                if (NL(nat, $"{sec}_Border_Type{type}") is { } val)
                {
                    Cell(val);
                    ProfileStatInfo.Hint(val, key);
                    _nat[key] = val;
                    _values[key] = val;
                }
                if (pct != 0 && NL(nat, $"{sec}_Border_Type{pct}") is { } pl)
                {
                    Cell(pl);
                    ProfileStatInfo.Hint(pl, key);
                    _nat[key + "%"] = pl;
                    _values[key + "%"] = pl;
                }
            }
        }
        // Cases « afficher » des accessoires : losanges bleus natifs.
        // Chaque case pilote la visibilité de la pièce (helmet =
        // tête d'armure, cloak = cape, clothes = tenue…) — préférence
        // locale client, comme le natif (options.ini, aucun paquet).
        foreach (var cb in nat.FindChildren("*ShowKuso*", "CheckBox", true,
                     false).Cast<CheckBox>())
        {
            foreach (var st in new[] { "normal", "pressed", "hover",
                         "hover_pressed", "focus" })
                cb.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
            cb.AddThemeIconOverride("unchecked", EeSkin.Diamond(false));
            cb.AddThemeIconOverride("checked", EeSkin.Diamond(true));
            var suffix = cb.Name.ToString()["ShowKuso".Length..]
                .ToLowerInvariant();
            cb.ButtonPressed = HudPrefs.KusoVisible(suffix);
            cb.Toggled += on =>
            {
                HudPrefs.SetKusoVisible(suffix, on);
                PlayerEquipment.RaiseChanged();
            };
        }
        // Résistances : Number1..9 (chop, stroke, stuck, fire, lightning,
        // ice, nature, holy, dark).
        for (var i = 1; i <= 9; i++)
            if (NL(nat, $"SpecProperty_Number{i}") is { } n)
            {
                EeSkin.Shadowed(n, 11, ink);
                n.RemoveThemeColorOverride("font_shadow_color");
                n.Text = "0";
                ProfileStatInfo.Hint(n, ProfileStatInfo.ResistKey(i - 1));
                _resistVals.Add(n);
            }
        // Aperçu 3D dans le cadre « Model ».
        if (TopLeftVitals.Find<Control>(nat, "Equio1_Model") is { } model)
            MountPreview(model);
        // Cases d'équipement réelles sur les slots natifs.
        if (_hud != null)
        {
            var eq = new EquipSlotsPanel { Name = "EquipPanel" };
            eq.BuildHeadless(_hud);
            page.AddChild(eq);
            _equipPanel = eq;
            _equipNat = nat;
            void At(string path, int loc, string label)
            {
                if (TopLeftVitals.Find<Control>(nat, path) is { } h)
                    eq.Attach(loc, h, label);
            }
            At("Weapon1_Slot1", 7, "Weapon");
            At("Weapon1_Slot2", 8, "Off-hand");
            At("Weapon1_Slot3", 14, "Honor");
            At("Weapon1_Slot4", 15, "Honor");
            At("Weapon1_Slot5", 11, "Necklace");
            At("Weapon1_Slot6", 12, "Ring");
            At("Weapon1_Slot7", 13, "Ring");
            At("Weapon1_Slot8", 6, UiTextEn.Translate("Back"));
            At("Weapon1_Slot9", 10, "Ranged");
            // Rangées Cloth/Leather/Armor = les 3 pages d'ensemble
            // natives (kind % 3 → conteneurs 2/3/4, fast-dress).
            // La rangée de la licence d'armure de la classe affiche
            // l'équipement PORTÉ (« Current Gear »).
            AttachArmorRows();
            if (!_armorHooked)
            {
                _armorHooked = true;
                // Changement de classe → la rangée « current gear »
                // suit la nouvelle licence d'armure.
                _armorClassHandler = () =>
                    Callable.From(AttachArmorRows).CallDeferred();
                PlayerVitals.ClassChanged += _armorClassHandler;
            }
        }
        foreach (var (path, txt) in new[]
                 { ("Equio2_GrowthBtn", "Gear Upgrade"),
                   ("Equio2_FastDressBtn", "Gear Swap") })
            if (TopLeftVitals.Find<Button>(nat, path) is { } b)
            {
                b.Text = UiTextEn.Translate(txt);
                EeSkin.Grey(b, 11);
                b.Disabled = true;
            }
        // Libellés de sets natifs (TextLab) → titres EN. Le TextLab de
        // la rangée « 布衣 » (Robe de protection) n'a PAS de propriété
        // Font dans le layout (les deux autres : F001-9) → taille par
        // défaut du thème, bien plus grande. Même taille imposée aux
        // trois ; si le TextLab est un conteneur (texte porté par un
        // fils __text), c'est le Label fils qui est réglé.
        RefreshArmorTitles();
        RefreshStats();
    }

    /// <summary>Posé à la construction de la page stats (layout natif)
    /// — panneau d'équipement headless + racine du layout.</summary>
    private EquipSlotsPanel? _equipPanel;
    private Control? _equipNat;
    private bool _armorHooked;
    private Action? _armorClassHandler;

    /// <summary>Rangée d'armure active = licence d'armure de la classe
    /// courante — archétype c_class (cellule certsBase−10, mesuré sur
    /// fr-full : 1 = 鎧甲/heavy pour 戰士/騎士/聖騎士, 2 = 皮甲/light
    /// pour 獵人/盜賊/吟遊詩人/工程師/武鬥家/舞者/遊俠, 3 = 布衣/cloth
    /// pour 魔術師/神官/幻術師/巫醫). 0/inconnu → repli Cloth (ordre
    /// natif de la première rangée).</summary>
    private static string ActiveArmorRow()
    {
        var (cls, _) = CharVisual.LocalClass();
        int arch = 0;
        try
        {
            ClassSkillTree.EnsureLoaded(AssetCatalog.ResolveRawAsset);
            arch = ClassSkillTree.ClassDef(cls)?.Archetype ?? 0;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[profile] archétype classe {cls} : {ex.Message}");
        }
        return EdenClient.Game.CostumeVisual.RowForArchetype(arch).Row;
    }

    /// <summary>Range les 5 cellules d'armure sur la rangée de la
    /// licence de la classe (conteneur 0 = équipement porté) ; les deux
    /// autres rangées affichent leur page d'ensemble sauvegardée
    /// (conteneurs 2/3/4) sous l'overlay natif « Unable ». Rejouable à
    /// un changement de classe : Attach remplace la cellule du host.
    /// Correspondance rangée ↔ page : kind % 3 natif (serveur
    /// ResolveEquipLocAsync — 0→Cloth/2, 2→Leather/3, 1→Armor/4).</summary>
    private void AttachArmorRows()
    {
        if (_equipPanel == null || _equipNat == null) return;
        var nat = _equipNat;
        var active = ActiveArmorRow();
        foreach (var (row, page, unable) in new[]
                 { ("Cloth", 2, "Unable1"),
                   ("Leather", 3, "Unable2"),
                   ("Armor", 4, "Unable3") })
        {
            var container = row == active ? 0 : page;
            void At(string path, int loc, string label)
            {
                if (TopLeftVitals.Find<Control>(nat, path) is { } h)
                    _equipPanel.Attach(loc, h, label, container);
            }
            At($"{row}_Slot1", 1, "Head");
            At($"{row}_Slot2", 2, "Body");
            At($"{row}_Slot3", 3, "Legs");
            At($"{row}_Slot4", 5, "Feet");
            At($"{row}_Slot5", 4, "Hands");
            if (TopLeftVitals.Find<Control>(nat, unable) is { } u)
                u.Visible = row != active;
        }
        RefreshArmorTitles();
    }

    /// <summary>Titres des 3 rangées : « (Current Gear) » suit la
    /// licence d'armure de la classe (un guerrier lit « Heavy Armor
    /// (Current Gear) », pas « Cloth »).</summary>
    private void RefreshArmorTitles()
    {
        if (_equipNat == null) return;
        var nat = _equipNat;
        var active = ActiveArmorRow();
        foreach (var (path, row, label) in new[]
                 { ("Weapon2", "Cloth", "Cloth Armor"),
                   ("Weapon2_Leather", "Leather", "Light Armor"),
                   ("Weapon2_Armor", "Armor", "Heavy Armor") })
        {
            var host = TopLeftVitals.Find<Control>(nat, path + "_TextLab");
            var tl = host as Label
                ?? TopLeftVitals.Find<Label>(host, "__text");
            if (tl == null)
            {
                GD.PrintErr($"[profile] libellé d'armure {path} : " +
                    "Label introuvable");
                continue;
            }
            tl.Text = UiTextEn.Translate(row == active
                ? $"{label} (Current Gear)" : label);
            EeSkin.Shadowed(tl, ArmorLabelSize, Colors.White);
            if (OS.GetCmdlineUserArgs().Contains("--profile-equip"))
                GD.Print($"[profile] armor-label {path} size=" +
                    $"{tl.GetThemeFontSize("font_size")} text={tl.Text}");
        }
    }

    /// <summary>M4 : boutons Gear Upgrade / Gear Swap, sets d'armure
    /// (Cloth current / Light / Heavy) et colonne des 9 résistances
    /// élémentaires (296).</summary>
    private void BuildGearArea(Control page)
    {
        var y = 386;
        foreach (var txt in new[] { "Gear Upgrade", "Gear Swap" })
        {
            var b = EeSkin.Grey(new Button
            {
                Text = UiTextEn.Translate(txt), Disabled = true,
                TooltipText = UiTextEn.Translate("Not available yet"),
                FocusMode = FocusModeEnum.None,
            }, 11);
            HudRoot.Pin(b, 0, 0, 0, 0, 258, y, 452, y + 24);
            page.AddChild(b);
            y += 28;
        }
        // Sets d'armure : 3 rangées de 5 silhouettes.
        var setBox = new Panel();
        HudRoot.Pin(setBox, 0, 0, 0, 0, 258, 446, 520, 598);
        setBox.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        page.AddChild(setBox);
        // « (Current Gear) » sur la rangée de la licence d'armure de
        // la classe (même règle que le layout natif — AttachArmorRows).
        var activeRow = ActiveArmorRow();
        var titles = new[]
        {
            activeRow == "Cloth" ? "Cloth Armor (Current Gear)"
                : "Cloth Armor",
            activeRow == "Leather" ? "Light Armor (Current Gear)"
                : "Light Armor",
            activeRow == "Armor" ? "Heavy Armor (Current Gear)"
                : "Heavy Armor",
        };
        var empty = _hud != null ? EeSkin.EmptySlot(_hud) : null;
        for (var r = 0; r < 3; r++)
        {
            var t = new Label { Text = UiTextEn.Translate(titles[r]) };
            HudRoot.Pin(t, 0, 0, 1, 0, 8, 4 + r * 49, -4, 20 + r * 49);
            EeSkin.Shadowed(t, 11, Color.Color8(220, 218, 210));
            setBox.AddChild(t);
            for (var c = 0; c < 5; c++)
            {
                var cell = new Panel { MouseFilter = MouseFilterEnum.Ignore };
                HudRoot.Pin(cell, 0, 0, 0, 0, 10 + c * 48, 20 + r * 49,
                    42 + c * 48, 50 + r * 49);
                if (empty != null)
                    cell.AddThemeStyleboxOverride("panel", empty);
                setBox.AddChild(cell);
            }
        }
        // Résistances élémentaires — ordre d'affichage natif
        // (PlayerStats.Resists est déjà réordonné depuis le wire).
        var resBox = new Panel();
        HudRoot.Pin(resBox, 0, 0, 0, 0, 526, 446, 636, 598);
        resBox.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        page.AddChild(resBox);
        var cols = new[]
        {
            Color.Color8(220, 70, 40), Color.Color8(70, 130, 230),
            Color.Color8(130, 200, 70), Color.Color8(240, 210, 80),
            Color.Color8(250, 250, 240), Color.Color8(120, 70, 170),
            Color.Color8(170, 120, 70), Color.Color8(90, 200, 200),
            Color.Color8(200, 90, 160),
        };
        for (var i = 0; i < 9; i++)
        {
            var sw = new ColorRect
            {
                Color = cols[i], MouseFilter = MouseFilterEnum.Ignore,
            };
            HudRoot.Pin(sw, 0, 0, 0, 0, 8, 8 + i * 16, 20, 20 + i * 16);
            resBox.AddChild(sw);
            var v = new Label
            {
                Text = "0", MouseFilter = MouseFilterEnum.Ignore,
            };
            ProfileStatInfo.Hint(v, ProfileStatInfo.ResistKey(i));
            HudRoot.Pin(v, 0, 0, 1, 0, 28, 5 + i * 16, -6, 21 + i * 16);
            EeSkin.Shadowed(v, 11);
            resBox.AddChild(v);
            _resistVals.Add(v);
        }
    }

    private static Label StatCell(Control box, int l, int r, int y, Color bg)
    {
        var p = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        HudRoot.Pin(p, 0, 0, 0, 0, l, y, r, y + 17);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = bg });
        box.AddChild(p);
        var lab = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        lab.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        EeSkin.Shadowed(lab, 11);
        p.AddChild(lab);
        return lab;
    }

    private void SetVal(string key, string text, Color? col = null)
    {
        if (!_values.TryGetValue(key, out var l)) return;
        l.Text = text;
        if (col != null) l.AddThemeColorOverride("font_color", col.Value);
    }

    /// <summary>296 : stats totales. Les taux (rates) sont affichés
    /// bruts — l'échelle native du % n'est pas encore décodée.</summary>
    /// <summary>Clic gauche sur le nom ou la valeur d'une stat → détail (formule, naturel + bonus).</summary>
    private void HookStatClicks()
    {
        void Hook(Dictionary<string, Label> d)
        {
            foreach (var (key, lab) in d)
            {
                var k = key;
                lab.MouseFilter = MouseFilterEnum.Stop;
                lab.MouseDefaultCursorShape = CursorShape.PointingHand;
                lab.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                    var s = PlayerVitals.TotalStats;
                    var value = _values.TryGetValue(k, out var v) ? v.Text : lab.Text;
                    int total = 0; int? bse = null;
                    if (s != null && ProfileStatInfo.CoreIndex.TryGetValue(k, out var idx))
                    {
                        total = new[] { s.Str, s.Int, s.Agi, s.Wis, s.Lck }[idx];
                        if (PlayerVitals.BaseStats is { } bs && idx < bs.Stats.Length) bse = bs.Stats[idx];
                    }
                    StatDetailWindow.Open(_hud ?? HudExtensions.FindHud()!, k, _names.TryGetValue(k, out var nl) ? nl.Text : k, value, total, bse,
                        () => _values.TryGetValue(k, out var cur) ? cur.Text : value);
                };
            }
        }
        Hook(_values); Hook(_names);
        // Résistances : clic sur la valeur → détail avec provenances
        // (pas de « naturel » transmis — cf. StatDetailWindow).
        for (var i = 0; i < _resistVals.Count; i++)
        {
            var idx = i;
            var cell = _resistVals[i];
            cell.MouseFilter = MouseFilterEnum.Stop;
            cell.MouseDefaultCursorShape = CursorShape.PointingHand;
            cell.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                var s = PlayerVitals.TotalStats;
                var k = ProfileStatInfo.ResistKey(idx);
                StatDetailWindow.Open(_hud ?? HudExtensions.FindHud()!,
                    k, ProfileStatInfo.ResistTitle(idx), cell.Text,
                    s != null && idx < s.Resists.Length ? s.Resists[idx] : 0,
                    null, () => _resistVals[idx].Text);
            };
        }
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            // --stat-click=<clé>[@secondes] : ouvre le détail d'une stat
            // (défaut 9,5 s après le build). Accepte aussi res_*.
            if (!a.StartsWith("--stat-click=")) continue;
            var k = a[13..].Split('@')[0];
            var ri = ProfileStatInfo.ResistIndex(k);
            if (ri < 0 && !_names.ContainsKey(k)) continue;
            Callable.From(() => GetTree().CreateTimer(
                a.Contains('@') && double.TryParse(a.Split('@')[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var after) ? after : 9.5).Timeout += () =>
            {
                var s = PlayerVitals.TotalStats;
                int total = 0; int? bse = null;
                if (s != null && ProfileStatInfo.CoreIndex.TryGetValue(k, out var idx))
                {
                    total = new[] { s.Str, s.Int, s.Agi, s.Wis, s.Lck }[idx];
                    if (PlayerVitals.BaseStats is { } bs && idx < bs.Stats.Length) bse = bs.Stats[idx];
                }
                else if (s != null && ri >= 0 && ri < s.Resists.Length)
                    total = s.Resists[ri];
                var title = ri >= 0 ? ProfileStatInfo.ResistTitle(ri)
                    : _names.TryGetValue(k, out var t) ? t.Text : k;
                var value = ri >= 0 && ri < _resistVals.Count ? _resistVals[ri].Text
                    : _values.TryGetValue(k, out var v) ? v.Text : "";
                StatDetailWindow.Open(HudExtensions.FindHud()!, k, title, value, total, bse);
            }).CallDeferred();
        }
    }

    private void RefreshStats()
    {
        var s = PlayerVitals.TotalStats;
        if (s == null)
        {
            foreach (var l in _values.Values) l.Text = "-";
            return;
        }
        // M4 : vert = au-dessus de la base (492 : Str, Int, Agi, Wis, Lck).
        Color? Up(int cur, int idx) =>
            PlayerVitals.BaseStats is { } b && cur > b.Stats[idx]
                ? Color.Color8(30, 150, 40) : null;
        SetVal("str", $"{s.Str}", Up(s.Str, 0));
        SetVal("agi", $"{s.Agi}", Up(s.Agi, 2));
        SetVal("int", $"{s.Int}", Up(s.Int, 1));
        SetVal("wis", $"{s.Wis}", Up(s.Wis, 3));
        SetVal("lck", $"{s.Lck}", Up(s.Lck, 4));
        // A5.3 : hover des 5 core stats = « total = naturel + bonus ».
        var totals = new[] { s.Str, s.Int, s.Agi, s.Wis, s.Lck };
        foreach (var (key, idx) in ProfileStatInfo.CoreIndex)
        {
            int? b = PlayerVitals.BaseStats is { } bs && idx < bs.Stats.Length
                ? bs.Stats[idx] : null;
            if (_values.TryGetValue(key, out var v))
                ProfileStatInfo.SplitHint(v, key, totals[idx], b);
            if (_names.TryGetValue(key, out var n))
                ProfileStatInfo.SplitHint(n, key, totals[idx], b);
        }
        for (var i = 0; i < _resistVals.Count && i < s.Resists.Length; i++)
            _resistVals[i].Text = $"{s.Resists[i]}";
        if (_nat.Count > 0)
        {
            if (s.Speeds.Length > 4)
                SetVal("move%", $"{F(s.Speeds[4])}%");
            foreach (var k in new[] { "pheal", "gheal" })
                SetVal(k, "-");
        }
        // Colonne valeur = paliers 493 (f32, ex. CRIT 124.7) ; colonne %
        // = taux 296 (s16 ×100 : 429 → 4.29 %) — calibré sur
        // ref-shots/profile.png (ACC 122/4.29 %, CRIT 124.7/5.63 %,
        // EVA 174/8.07 %, Parry 135/2.4 %, Block 151/0.98 %).
        var lv = PlayerVitals.LevelStats;
        string Lv(int i, int raw) => lv != null ? F(lv[i]) : $"{raw}";
        SetVal("acc", Lv(0, Rate(s, 2)));
        SetVal("move", Speed(s, 4));
        SetVal("patk", $"{s.PhysicalAttack}", Color.Color8(200, 30, 30));
        SetVal("aspd", lv != null ? F(lv[12]) : F(s.AttackSpeedRate));
        SetVal("crit", Lv(1, Rate(s, 4)));
        // CRIT DMG / M-CRIT DMG : valeur = taux/100, % = taux brut.
        SetVal("critdmg", F(Rate(s, 5) / 100f));
        SetVal("matk", $"{s.MagicAttack}", Color.Color8(200, 30, 30));
        SetVal("cspd", lv != null ? F(lv[13]) : F(s.MagicSpeedRate));
        SetVal("mcrit", Lv(2, Rate(s, 6)));
        SetVal("mcritdmg", F(Rate(s, 7) / 100f));
        SetVal("def", $"{s.PhysicalDefence}", Color.Color8(30, 150, 40));
        SetVal("eva", Lv(3, Rate(s, 3)));
        SetVal("parry", Lv(4, Rate(s, 0)));
        SetVal("block", Lv(5, Rate(s, 1)));
        if (_nat.Count > 0)
        {
            void Pct(string k, float v) => SetVal(k + "%", $"{F(v)}%");
            Pct("acc", Rate(s, 2) / 100f);
            Pct("crit", Rate(s, 4) / 100f);
            Pct("critdmg", Rate(s, 5));
            Pct("mcrit", Rate(s, 6) / 100f);
            Pct("mcritdmg", Rate(s, 7));
            Pct("eva", Rate(s, 3) / 100f);
            Pct("parry", Rate(s, 0) / 100f);
            Pct("block", Rate(s, 1) / 100f);
        }
    }

    private static int Rate(PlayerStats s, int i) =>
        i < s.Rates.Length ? s.Rates[i] : 0;

    private static string Speed(PlayerStats s, int i) =>
        i < s.Speeds.Length ? F(s.Speeds[i]) : "-";

    private static string F(float v) => v.ToString("0.##",
        System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------
    // Aperçu 3D

    private void MountPreview(Control host)
    {
        var svc = new SubViewportContainer
        {
            Stretch = true, MouseFilter = MouseFilterEnum.Stop,
            TooltipText = UiTextEn.Translate("Drag to rotate"),
        };
        // Rotation de l'aperçu à la souris (glisser bouton gauche).
        svc.GuiInput += e =>
        {
            if (e is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left }
                    mm && _previewHolder != null)
                _previewHolder.RotateY(mm.Relative.X * 0.015f);
        };
        HudRoot.Pin(svc, 0, 0, 1, 1, 1, 1, -1, -1);
        var vp = new SubViewport
        {
            TransparentBg = true, OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
            Size = new Vector2I(192, 372),
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
                AmbientLightColor = new Color(0.9f, 0.88f, 0.84f),
                AmbientLightEnergy = 0.9f,
            },
        });
        stage.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30f, -20f, 0f),
            LightEnergy = 0.7f,
        });
        _previewCam = new Camera3D { Current = true, Fov = 30f };
        stage.AddChild(_previewCam);
        _previewCam.LookAtFromPosition(new Vector3(0f, 0.95f, 5.4f),
            new Vector3(0f, 0.9f, 0f), Vector3.Up);
        _previewHolder = new Node3D();
        stage.AddChild(_previewHolder);
        host.AddChild(svc);
    }

    /// <summary>Corps du joueur dupliqué dans l'aperçu (même source que
    /// le portrait des vitals).</summary>
    private Node3D? _previewSrc;

    public void SetPreview(Node3D? body)
    {
        _previewSrc = body;
        if (_previewHolder == null) return;
        foreach (var c in _previewHolder.GetChildren()) c.QueueFree();
        if (body?.Duplicate() is not Node3D dup) return;
        dup.RotationDegrees = new Vector3(-90f, 0f, 0f);
        _previewHolder.Position = Vector3.Zero;
        _previewHolder.Rotation = Vector3.Zero;
        _previewHolder.AddChild(dup);
        // Cadrage sur le CORPS (armes/attaches exclues : une épée dépasse d'un
        // côté et décalait tout le perso) : centré en X/Z, pieds au sol, hauteur
        // ajustée à la caméra.
        dup.Position = Vector3.Zero;        // le corps du jeu porte un décalage (monture, snap sol)
        dup.Scale = Vector3.One;
        // Le Duplicate() copie l'AnimationPlayer mais pas l'état privé
        // de l'EntityAnimator (inerte) : sans ceci le perso reste en
        // T-pose. On joue l'idle natif directement sur le player copié.
        foreach (var ap in dup.FindChildren("*", "AnimationPlayer",
                     true, false).Cast<AnimationPlayer>())
        {
            if (!ap.HasAnimation("stand01")) continue;
            ap.Play("stand01");
            break;
        }
    }

    // ------------------------------------------------------------
    // Title / Achievement

    private Tree? _titleTree;
    private Label? _titleBanner, _titleAbility, _titleObtain;
    private CheckBox? _ownedOnly;
    private Button? _setTitle;

    private Control BuildTitlePage(Panel win)
    {
        var page = Page(win);
        // Bandeau du titre affiché (ref profile-Title.png : « No Title »).
        var ban = new Panel();
        HudRoot.Pin(ban, 0, 0, 1, 0, 8, 8, -8, 52);
        ban.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
        page.AddChild(ban);
        _titleBanner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _titleBanner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _titleBanner.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
        _titleBanner.AddThemeFontSizeOverride("font_size", 12);
        ban.AddChild(_titleBanner);

        var body = new Panel();
        HudRoot.Pin(body, 0, 0, 1, 1, 8, 62, -8, -8);
        body.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        page.AddChild(body);
        var head = new Label { Text = UiTextEn.Translate("List of Titles") };
        HudRoot.Pin(head, 0, 0, 0, 0, 14, 10, 200, 28);
        EeSkin.Shadowed(head, 11);
        body.AddChild(head);
        var ab = new Label { Text = UiTextEn.Translate("Ability description") };
        HudRoot.Pin(ab, 0, 0, 0, 0, 240, 10, 440, 28);
        EeSkin.Shadowed(ab, 11);
        body.AddChild(ab);
        var help = new Button { Text = "?", FocusMode = FocusModeEnum.None,
            TooltipText = UiTextEn.Translate("Select a title to read its effect and how to obtain it.") };
        EeSkin.Grey(help, 10);
        HudRoot.Pin(help, 1, 0, 1, 0, -26, 8, -8, 24);
        body.AddChild(help);

        _titleTree = new Tree
        {
            HideRoot = true, SelectMode = Tree.SelectModeEnum.Single,
        };
        HudRoot.Pin(_titleTree, 0, 0, 0, 1, 12, 34, 226, -44);
        _titleTree.AddThemeColorOverride("font_color", Color.Color8(206, 206, 200));
        _titleTree.AddThemeFontSizeOverride("font_size", 11);
        _titleTree.AddThemeConstantOverride("v_separation", 0);
        _titleTree.ItemSelected += OnTitleSelected;
        body.AddChild(_titleTree);

        var box = new Panel();
        HudRoot.Pin(box, 0, 0, 1, 0, 240, 34, -12, 190);
        box.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
        body.AddChild(box);
        _titleAbility = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        HudRoot.Pin(_titleAbility, 0, 0, 1, 1, 8, 6, -8, -6);
        _titleAbility.AddThemeColorOverride("font_color", Color.Color8(30, 30, 28));
        _titleAbility.AddThemeFontSizeOverride("font_size", 11);
        box.AddChild(_titleAbility);
        var moa = new Label { Text = UiTextEn.Translate("Method of Acquisition") };
        HudRoot.Pin(moa, 0, 0, 1, 0, 240, 200, -12, 218);
        EeSkin.Shadowed(moa, 11);
        body.AddChild(moa);
        _titleObtain = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        HudRoot.Pin(_titleObtain, 0, 0, 1, 0, 240, 222, -12, 330);
        EeSkin.Shadowed(_titleObtain, 11, Color.Color8(210, 210, 206));
        body.AddChild(_titleObtain);

        _ownedOnly = new CheckBox
        {
            Text = UiTextEn.Translate("Displaying owned title"), FocusMode = FocusModeEnum.None,
        };
        _ownedOnly.AddThemeIconOverride("unchecked", EeSkin.Diamond(false));
        _ownedOnly.AddThemeIconOverride("checked", EeSkin.Diamond(true));
        _ownedOnly.AddThemeFontSizeOverride("font_size", 11);
        HudRoot.Pin(_ownedOnly, 0, 1, 0, 1, 12, -34, 226, -10);
        _ownedOnly.Toggled += _ => RefreshTitles();
        body.AddChild(_ownedOnly);

        _setTitle = EeSkin.Grey(new Button
        {
            Text = UiTextEn.Translate("Set Title"), FocusMode = FocusModeEnum.None,
            Disabled = true,
        }, 11);
        HudRoot.Pin(_setTitle, 1, 1, 1, 1, -130, -36, -12, -10);
        _setTitle.Pressed += () =>
        {
            if (_titleTree?.GetSelected()?.GetMetadata(0).AsInt32() is int id
                && id >= 0)
                PlayerTitles.RequestSet(id);
        };
        body.AddChild(_setTitle);
        return page;
    }

    /// <summary>Lien « Achievement Title » du détail d'un succès :
    /// ouvre l'onglet Title sur ce titre (A5.6d).</summary>
    private void FocusTitle(int titleId)
    {
        SelectTab(1);
        RefreshTitles();
        if (_titleTree?.GetRoot() is not { } root) return;
        foreach (var cat in root.GetChildren())
            foreach (var it in cat.GetChildren())
                if (it.GetMetadata(0).AsInt32() == titleId)
                {
                    it.Select(0);
                    _titleTree.ScrollToItem(it);
                    return;
                }
    }

    private void OnTitleSelected()
    {
        var it = _titleTree?.GetSelected();
        var id = it == null ? -1 : it.GetMetadata(0).AsInt32();
        if (id < 0)
        {
            _titleAbility!.Text = ""; _titleObtain!.Text = "";
            _setTitle!.Disabled = true;
            return;
        }
        if (id > 0 && AchievementWindow.AllTitles.TryGetValue(id, out var d))
        {
            _titleAbility!.Text = (d.Bonus + "\n" + d.Description).Trim();
            var src = AchievementWindow.AchievementForTitle(id);
            _titleObtain!.Text = src == null ? d.Obtain
                : (d.Obtain + "\n" + UiTextEn.Translate("Achievement") +
                   ": " + src).Trim();
        }
        else { _titleAbility!.Text = ""; _titleObtain!.Text = ""; }
        _setTitle!.Disabled = id != 0 && !PlayerTitles.Owned.Contains(id);
    }

    private void RefreshTitles()
    {
        if (_titleTree == null) return;
        _titleTree.Clear();
        var root = _titleTree.CreateItem();
        var active = PlayerTitles.Active;
        if (_titleBanner != null)
            _titleBanner.Text = active > 0
                ? AchievementWindow.TitleName(active) : UiTextEn.Translate("No Title");
        var none = _titleTree.CreateItem(root);
        none.SetText(0, UiTextEn.Translate("No Title"));
        none.SetMetadata(0, 0);
        var ownedOnly = _ownedOnly?.ButtonPressed == true;
        var cats = new Dictionary<string, TreeItem>();
        foreach (var d in AchievementWindow.AllTitles.Values
                     .OrderBy(t => t.Id))
        {
            var owned = PlayerTitles.Owned.Contains(d.Id);
            if (ownedOnly && !owned) continue;
            var cat = d.Category.Length > 0 ? d.Category : "General Title";
            if (!cats.TryGetValue(cat, out var ci))
            {
                ci = _titleTree.CreateItem(root);
                ci.SetText(0, cat);
                ci.SetMetadata(0, -1);
                ci.SetSelectable(0, false);
                cats[cat] = ci;
            }
            var it = _titleTree.CreateItem(ci);
            it.SetText(0, d.Name + (d.Id == active ? UiTextEn.Translate("  (displayed)") : ""));
            it.SetMetadata(0, d.Id);
            if (!owned)
                it.SetCustomColor(0, Color.Color8(128, 128, 122));
        }
        OnTitleSelected();
    }

    private AchievementWindow? _achEmbed;

    private Control BuildAchievementPage(Panel win)
    {
        var page = Page(win);
        _achEmbed = AchievementWindow.CreateEmbedded(page, _hud);
        return page;
    }

    // ------------------------------------------------------------
    // API (inchangée — HudRoot / GameScene)

    /// <summary>Identité du personnage (nom + niveau).</summary>
    public void SetIdentity(string name, int level)
    {
        _charName = name;
        _level = level;
        Apply();
    }

    /// <summary>Classe/niveau de classe (CharacterSummary).</summary>
    public void SetClass(string className, int classLevel)
    {
        _className = className;
        _classLevel = classLevel;
        Apply();
    }

    /// <summary>Données complètes du 310.</summary>
    public void SetPlayerData(PlayerHudData d)
    {
        _level = d.Level;
        _classLevel = d.ClassLevel;
        _classId = d.ClassId;
        _className = ResolveClassName(d.ClassId);
        Apply();
    }

    /// <summary>Race du personnage (CharacterSummary monde).</summary>
    public void SetRace(int raceId)
    {
        _raceId = raceId;
        Apply();
    }

    /// <summary>Nom de classe anglais (T_Class.ini).</summary>
    public static string ResolveClassName(int classId) =>
        Data.TextResolver.ClassName(classId) ?? $"class {classId}";

    private void Apply()
    {
        if (_name != null) _name.Text = _charName;
        if (_lv != null) _lv.Text = _level > 0 ? UiTextEn.Translate($"LV{_level}") : "";
        if (_cls != null)
        {
            var race = _raceId > 0 && _raceId < RaceNames.Length
                ? RaceNames[_raceId] + " " : "";
            _cls.Text = race + _className;
        }
        if (_classLv != null)
            _classLv.Text = _classLevel > 0 ? UiTextEn.Translate($"LV{_classLevel}") : "";
        var cid = _classId > 0 ? _classId : SessionCarry.Character?.ClassId ?? 0;
        if (_classIcon != null && cid > 0)
            _classIcon.Texture = CharSelectScreen.ClassIcon(cid);
    }
}
