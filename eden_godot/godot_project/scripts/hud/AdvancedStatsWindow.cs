using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>Fenêtre « Statistiques avancées » (A5.5), ouverte depuis la
/// fiche perso. Deux sections alimentées par l'extension serveur
/// 32001/32002 (<see cref="ZoneAdvStatsInfo"/>) : Formules (clé →
/// expression avec valeurs injectées → résultat) et Multi-hit (chance /
/// facteur par palier, avec provenance : talent, certificat, buff,
/// équipement). Le multi-hit n'existe pas sur le fil natif 296.</summary>
public static class AdvancedStatsWindow
{
    /// <summary>Posé par GameScene : envoie le 32001 au serveur.</summary>
    public static Action? Request;

    private static Panel? _win;
    private static VBoxContainer? _col;
    private static ZoneAdvStatsInfo? _last;
    private static Action? _statsHook;

    private static Color Ink => new(0.96f, 0.95f, 0.92f);
    private static Color Dim => new(0.75f, 0.75f, 0.72f);
    private static Color Gold => new(1f, 0.93f, 0.62f);

    /// <summary>Réception du 32002 (thread principal).</summary>
    public static ZoneAdvStatsInfo? Last => _last;
    public static event Action? Updated;

    /// <summary>Docs (rôle, formule, note) d'une formule serveur — réutilisés par la fenêtre par stat.</summary>
    public static (string Role, string Formula, string Note)? DocOf(string key)
    {
        foreach (var d in Docs) if (d.Key == key) return (d.Role, d.Formula, d.Note);
        return null;
    }

    public static void Apply(ZoneAdvStatsInfo m)
    {
        _last = m;
        Updated?.Invoke();
        if (_win != null && GodotObject.IsInstanceValid(_win)) Rebuild();
    }

    public static void Toggle(HudRoot hud)
    {
        if (_win != null && GodotObject.IsInstanceValid(_win))
        {
            Close();
            return;
        }
        var win = EeSkin.MakeWindow(hud,
            UiTextEn.Translate("Advanced Statistics"), 120, 60, 560, 560);
        hud.AddChild(win);
        DragHelper.Persist(win, "AdvancedStats");
        _win = win;
        var sc = new ScrollContainer
            { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        HudRoot.Pin(sc, 0, 0, 1, 1, 8, 30, -8, -8);
        win.AddChild(sc);
        _col = new VBoxContainer
            { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _col.AddThemeConstantOverride("separation", 3);
        sc.AddChild(_col);
        // Les sources changent avec les buffs : chaque 296 re-demande.
        _statsHook = () => Request?.Invoke();
        PlayerVitals.StatsChanged += _statsHook;
        win.TreeExiting += () =>
        {
            if (_statsHook != null) PlayerVitals.StatsChanged -= _statsHook;
            _statsHook = null;
        };
        Rebuild();
        Request?.Invoke();
    }

    private static void Close()
    {
        _win?.QueueFree();
        _win = null; _col = null;
    }

    private static Label Line(string text, Color col, int size = 12,
        bool wrap = true)
    {
        var l = new Label
        {
            Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart
                : TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(300, 0),
        };
        EeSkin.Shadowed(l, size, col);
        return l;
    }

    private static Control Card(params Control[] kids)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", EeSkin.InsetCard(6, 4));
        var v = new VBoxContainer();
        foreach (var k in kids) v.AddChild(k);
        p.AddChild(v);
        return p;
    }

    private static readonly (string Key, string Section, string Role, string Formula, string Note)[] Docs =
    {
        ("P-ATK", "Offense", "Base physical damage of your attacks. Strength, gear and weapon are added, then multiplied by your % bonuses.", "(100 + ATK%) x (STR x 5 + gear + weapon) / 100 + flat bonus", "Each STR point gives +5 physical attack."),
        ("M-ATK", "Offense", "Base magic damage of your spells. Intelligence, gear and weapon are added, then multiplied by your % bonuses.", "(100 + M-ATK%) x (INT x 5 + gear + weapon) / 100 + flat bonus", "Each INT point gives +5 magic attack."),
        ("Weapon Variance", "Offense", "Random spread of the weapon: every hit deals between (100 - variance)% and (100 + variance)% of its base damage.", "damage x (100 +/- variance)%", ""),
        ("Hit Rate", "Accuracy & Evasion", "Your chance to hit, compared with the target's Dodge. The higher it is, the fewer misses.", "((gear + weapon + buff + LCK x factor) x (1 + bonus%)) / level divisor + base", "Hit chance cap: 300%."),
        ("Dodge Rate", "Accuracy & Evasion", "Your chance to avoid a physical hit, compared with the attacker's Hit Rate.", "((gear + buff + AGI x factor) x (1 + bonus%)) / level divisor + base + flat", "Cap: 100%."),
        ("Critical Rate", "Critical hits", "Chance that a physical hit is critical (extra damage).", "((gear + buff + LCK x 0.8) x (1 + bonus%)) / level divisor + base", "LCK gives +0.8 critical rating per point."),
        ("Magic Critical Rate", "Critical hits", "Chance that a spell is critical (extra damage).", "((gear + buff + LCK x 1) x (1 + bonus%)) / level divisor + base", "LCK gives +1 magic critical rating per point."),
        ("Critical Damage", "Critical hits", "Damage multiplier of a physical critical hit.", "base 150% + gear + buff", "Cap: 300%."),
        ("Magic Critical Damage", "Critical hits", "Damage multiplier of a magic critical hit.", "base 150% + gear + buff", "Cap: 300%."),
        ("Critical Damage Reduction", "Defence", "Reduces the extra damage taken from critical hits.", "clamp(DEF / (Lv^1.6 + 30), 0, 70)", "Cap: 70%."),
        ("Parry Rate", "Defence", "Chance to parry a hit and halve its damage. Two-handed weapons only.", "(gear + buff + AGI x 0.8) / level divisor", "Cap: 70%."),
        ("Block Rate", "Defence", "Chance to block a hit and cancel its damage. Shield only.", "(gear + buff + STR x 1) / level divisor", "Cap: 50%."),
        ("Damage Reduction", "Defence", "Share of physical damage absorbed by your Defence.", "clamp(DEF x factor / (Lv^1.6 + 30), 0, 70)", "Cap: 70%."),
        ("Attack Speed", "Speed", "How much faster you attack. Agility, gear and buffs are added then divided by the level divisor.", "((gear + buff + AGI x 0.5) x (1 + bonus%)) / level divisor", ""),
        ("Cast Speed", "Speed", "How much faster you cast spells (shorter cast time).", "((gear + buff + AGI x factor) x (1 + bonus%)) / level divisor", ""),
        ("Move Speed", "Speed", "Your running speed relative to the base speed, including mount and buffs.", "base x min(1 + (mount/buff % + AGI x factor %), cap)", "Cap: 200%."),
    };

    private static Control Doc(string label, string text, Color col)
    {
        var rt = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(300, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rt.AddThemeFontSizeOverride("normal_font_size", 11);
        rt.AddThemeFontSizeOverride("bold_font_size", 11);
        rt.AddThemeColorOverride("default_color", col);
        rt.Text = $"[b][color=#e8c860]{UiTextEn.Translate(label)} :[/color][/b] {text}";
        return rt;
    }

    private static void Rebuild()
    {
        if (_col == null) return;
        foreach (var c in _col.GetChildren()) c.QueueFree();
        if (_last == null)
        {
            _col.AddChild(Line(UiTextEn.Translate("Loading..."), Dim));
            return;
        }
        var m = _last;

        _col.AddChild(Line(UiTextEn.Translate("Formulas"), Gold, 14));
        _col.AddChild(Line(UiTextEn.Translate(
            "How to read: gear = bonus from equipment, buff = bonus from buffs, talents and certificates, Lv = your level, level divisor = (Lv x 0.4 + 1) which scales ratings with level."),
            Dim, 11));
        var byKey = m.Formulas.ToDictionary(f => f.Key);
        string? lastSection = null;
        foreach (var d in Docs)
        {
            if (!byKey.TryGetValue(d.Key, out var f)) continue;
            if (d.Section != lastSection)
            {
                lastSection = d.Section;
                _col.AddChild(Line(UiTextEn.Translate(d.Section), Gold, 13));
            }
            var val = f.Value.ToString("0.##",
                System.Globalization.CultureInfo.InvariantCulture) + f.Unit;
            var kids = new List<Control>
            {
                Line($"{UiTextEn.Translate(f.Key)} = {val}", Ink, 13),
                Doc("Why", UiTextEn.Translate(d.Role), Ink),
                Doc("Formula", UiTextEn.Translate(d.Formula), Dim),
                Doc("Your values", f.Expr, Dim),
            };
            if (d.Note.Length > 0)
                kids.Add(Doc("Note", UiTextEn.Translate(d.Note), Dim));
            _col.AddChild(Card(kids.ToArray()));
        }
        // Formules que le serveur envoie sans fiche explicative.
        foreach (var f in m.Formulas.Where(f => !Docs.Any(d => d.Key == f.Key)))
            _col.AddChild(Card(
                Line($"{UiTextEn.Translate(f.Key)} = {f.Value:0.##}{f.Unit}", Ink, 13),
                Doc("Your values", f.Expr, Dim)));

        _col.AddChild(Line(UiTextEn.Translate("Multi-Hit"), Gold, 14));
        if (m.Sources.Count == 0 || m.MaxHits == 0)
        {
            _col.AddChild(Line(UiTextEn.Translate("No multi-hit source"), Dim));
            return;
        }
        double expect = 0;
        foreach (var s in m.Sources)
        {
            var origin = s.Origin switch
            {
                0 => UiTextEn.Translate("Equipment"),
                2 => UiTextEn.Translate("Proc") + $" {s.Gate:0.##}%",
                _ => UiTextEn.Translate("Talent / Certificate / Buff"),
            };
            var name = EnchantTable.NameOf(s.EnchantId) +
                (s.Stacks > 1 ? $" x{s.Stacks}" : "");
            var lines = new List<Control>
            {
                Line($"{name}  [{origin}]", Ink),
                Line(UiTextEn.Translate("Double Hit") +
                    $": {s.C1:0.##}% -> {s.F1:0.##}% " +
                    UiTextEn.Translate("damage"), Dim, 11),
            };
            if (m.MaxHits >= 2 && s.C2 > 0)
                lines.Add(Line(UiTextEn.Translate("Triple Hit") +
                    $": {s.C2:0.##}% -> {s.F2:0.##}% " +
                    UiTextEn.Translate("damage"), Dim, 11));
            _col.AddChild(Card(lines.ToArray()));
            // Roll séquentiel : le 2e coup n'est tenté que si le 1er passe.
            var gate = Math.Min(s.Gate, 100f) / 100.0;
            var p1 = Math.Min(s.C1, 100f) / 100.0 * gate;
            var p2 = m.MaxHits >= 2 ? p1 * Math.Min(s.C2, 100f) / 100.0 : 0;
            expect += p1 * s.F1 + p2 * s.F2;
        }
        _col.AddChild(Line(UiTextEn.Translate("Expected extra damage per hit") +
            $": +{expect:0.#}%  (x{m.ChanceMult:0.##} / x{m.DamageMult:0.##})",
            Gold));
    }
}
