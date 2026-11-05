using Godot;
using EdenClient.Combat;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>
/// Fiche « Inspecter » d'un monstre (bouton « ? » du cadre de cible) : UN
/// panneau à trois onglets — <b>Stats</b> (format mini), <b>Drops</b> (trois
/// catégories dépliables : Normal, Point de rupture, Butin de classe) et
/// <b>Skills</b> (Compétences normales / Compétences à seuil, avec les
/// infobulles d'effets des sorts joueur). Données : <see cref="MobInfoData"/>
/// (tables natives du client — aucun paquet serveur n'est nécessaire, le
/// natif lit les mêmes tables). Termes FR : docs/knowledge/definitions.md.
/// </summary>
public partial class MobInfoWindow : Control
{
    public static MobInfoWindow? Instance { get; private set; }

    private Panel? _panel;
    private Label? _title;
    private TabContainer? _tabs;

    private static readonly Color Gold = new(0.95f, 0.85f, 0.45f);
    private static readonly Color Dim = new(0.65f, 0.68f, 0.62f);

    public override void _Ready()
    {
        Instance = this;
        _panel = new Panel { Name = "__mobinfo" };
        _panel.Position = new Vector2(220, 140);
        _panel.Size = new Vector2(380, 470);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.97f),
            BorderColor = new Color(0.5f, 0.55f, 0.45f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnableBackgroundDrag(_panel);
        DragHelper.Persist(_panel, "mobinfo");

        _title = new Label();
        _title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.OffsetTop = 8;
        _title.AddThemeFontSizeOverride("font_size", 15);
        _title.AddThemeColorOverride("font_color", Gold);
        _panel.AddChild(_title);

        _tabs = new TabContainer
        {
            Position = new Vector2(8, 34),
            Size = new Vector2(364, 390),
        };
        _panel.AddChild(_tabs);

        var close = new Button { Text = "Fermer" };
        close.Position = new Vector2(145, 434);
        close.Size = new Vector2(90, 26);
        close.Pressed += () => Visible = false;
        _panel.AddChild(close);
        Visible = false;
        // --mobinfo=<id>[:tab] : ouvre la fiche (captures de validation).
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--mobinfo=") &&
                int.TryParse(a[10..].Split(':')[0], out var mid))
            {
                var tab = a.Contains(':') ? a[(a.IndexOf(':') + 1)..] : "";
                var t = new Godot.Timer { WaitTime = 1.0, OneShot = true, Autostart = true };
                AddChild(t);
                t.Timeout += () => Open(mid, tab);
            }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        base._ExitTree();
    }

    /// <summary>Ouvre la fiche du monstre <paramref name="mobId"/>.</summary>
    public void Open(int mobId, string tab = "")
    {
        var sheet = MobInfoData.Build(mobId);
        if (sheet == null || _tabs == null || _title == null)
        {
            GD.PrintErr($"[mobinfo] monstre #{mobId} introuvable dans les tables");
            return;
        }
        _title.Text = sheet.Level > 0
            ? $"Niv. {sheet.Level} {TextResolver.NameOnly(sheet.Name)}"
            : TextResolver.NameOnly(sheet.Name);
        foreach (var c in _tabs.GetChildren()) { _tabs.RemoveChild(c); c.QueueFree(); }
        _tabs.AddChild(Page(MobInfoData.TabStats, StatsPage(sheet)));
        _tabs.AddChild(Page(MobInfoData.TabDrops, DropsPage(sheet)));
        _tabs.AddChild(Page(MobInfoData.TabSkills, SkillsPage(sheet)));
        for (var i = 0; i < _tabs.GetChildCount(); i++)
            if (_tabs.GetChild(i).Name == tab) _tabs.CurrentTab = i;
        Visible = true;
        MoveToFront();
    }

    private static ScrollContainer Page(string name, Control content)
    {
        var sc = new ScrollContainer
        {
            Name = name,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sc.AddChild(content);
        return sc;
    }

    private static VBoxContainer Box()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 3);
        return v;
    }

    private static Label Text(string s, Color? c = null, int size = 13)
    {
        var l = new Label { Text = s, AutowrapMode = TextServer.AutowrapMode.Off };
        l.AddThemeFontSizeOverride("font_size", size);
        if (c != null) l.AddThemeColorOverride("font_color", c.Value);
        return l;
    }

    // ---- Stats ---------------------------------------------------------

    private static Control StatsPage(MobInfoData.Sheet s)
    {
        var box = Box();
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 2);
        foreach (var (label, value) in s.Stats)
        {
            grid.AddChild(Text(label, Dim));
            grid.AddChild(Text(value));
        }
        box.AddChild(grid);
        return box;
    }

    // ---- Drops ---------------------------------------------------------

    private static Control DropsPage(MobInfoData.Sheet s)
    {
        var box = Box();
        box.AddChild(Fold(MobInfoData.CatNormal, s.NormalDrops.Count, () => DropRows(s.NormalDrops)));
        box.AddChild(Fold(MobInfoData.CatBreak, s.BreakDrops.Count, () => DropRows(s.BreakDrops)));
        box.AddChild(Fold(MobInfoData.CatClass, s.ClassDrops.Count, () => DropRows(s.ClassDrops)));
        return box;
    }

    /// <summary>Catégorie dépliable : en-tête cliquable + contenu construit
    /// à la demande (déplié par défaut quand non vide).</summary>
    private static Control Fold(string title, int count, System.Func<Control> content)
    {
        var host = Box();
        var body = content();
        body.Visible = count > 0;
        var head = new Button
        {
            Alignment = HorizontalAlignment.Left,
            Flat = true,
            Disabled = count == 0,
        };
        void Refresh() => head.Text =
            $"{(body.Visible ? "▼" : "▶")} {title} ({count})";
        head.Pressed += () => { body.Visible = !body.Visible; Refresh(); };
        head.AddThemeColorOverride("font_color", Gold);
        Refresh();
        host.AddChild(head);
        host.AddChild(body);
        return host;
    }

    private static string Pct(double p) =>
        (p >= 100 ? "100" : p.ToString(p % 1 == 0 ? "0" : "0.##",
            System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))) + " %";

    private static Control DropRows(System.Collections.Generic.List<MobInfoData.DropLine> drops)
    {
        var box = Box();
        foreach (var d in drops)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            row.AddThemeConstantOverride("separation", 6);
            var icon = new TextureRect
            {
                CustomMinimumSize = new Vector2(28, 28),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            var def = InventoryWindow.LookupDef(d.ItemId);
            if (def is { Code.Length: > 0 })
                icon.Texture = AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png");
            row.AddChild(icon);
            var name = TextResolver.ItemName(d.ItemId) ?? def?.Name ?? $"#{d.ItemId}";
            var nl = Text(d.Quantity > 1 ? $"{name} ×{d.Quantity}" : name);
            nl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nl.ClipText = true;
            nl.CustomMinimumSize = new Vector2(220, 0);
            row.AddChild(nl);
            row.AddChild(Text(Pct(d.ChancePct), Dim));
            var id = d.ItemId;
            ItemTooltip.BindId(row, () => id);
            box.AddChild(row);
        }
        return box;
    }

    // ---- Skills --------------------------------------------------------

    private static Control SkillsPage(MobInfoData.Sheet s)
    {
        var box = Box();
        box.AddChild(Fold(MobInfoData.CatNormalSkills, s.NormalSkills.Count, () => SkillRows(s.NormalSkills)));
        box.AddChild(Fold(MobInfoData.CatCountdownSkills, s.CountdownSkills.Count, () => SkillRows(s.CountdownSkills)));
        return box;
    }

    private static Control SkillRows(System.Collections.Generic.List<MobInfoData.SkillLine> skills)
    {
        var box = Box();
        foreach (var sk in skills)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new TextureRect
            {
                CustomMinimumSize = new Vector2(32, 32),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Texture = SpellCatalog.Icon(sk.SpellId),
            });
            var col = Box();
            col.AddChild(Text(SpellCatalog.Name(sk.SpellId)));
            if (sk.Detail.Length > 0) col.AddChild(Text(sk.Detail, Dim, 12));
            row.AddChild(col);
            var id = sk.SpellId;
            // Même infobulle d'effets que les compétences joueur.
            ItemTooltip.BindCustom(row, () =>
                ItemTooltip.BuildSkillTip(id, 1, 0, true, -1));
            box.AddChild(row);
        }
        return box;
    }
}
