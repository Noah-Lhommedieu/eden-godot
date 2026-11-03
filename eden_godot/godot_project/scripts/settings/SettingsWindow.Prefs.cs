using Godot;
using EdenClient.Data;
using EdenClient.Hud;

namespace EdenClient.Settings;

/// <summary>Catégorie « Préférences » (3.2.9) : sous-catégories
/// triées, ici « Cible » (barres de PV au-dessus des entités) et
/// « Animation de perte de PV » (TargetFrame + barres 3D).
/// Stockage : <see cref="HudPrefs"/> (clientSettings.json).</summary>
public partial class SettingsWindow
{
    private HSlider? _pkDelay, _pkDur;
    private Label? _pkDelayLbl, _pkDurLbl;
    // Cases à synchroniser : (case, lecture de la pref).
    private readonly List<(CheckBox Box, Func<bool> Get)> _pkChecks = new();

    /// <summary>Case losange de préférence ; <paramref name="parent"/>
    /// = grille 2 colonnes du groupe.</summary>
    private CheckBox Pref(Control parent, string label, Func<bool> get,
        Action<bool> set)
    {
        var c = Check(parent, UiTextEn.Translate(label), get(), set);
        c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pkChecks.Add((c, get));
        return c;
    }

    private (HSlider, Label)? _pkMouseRow;

    private GridContainer PrefGrid(VBoxContainer box, string header, int cols = 2)
    {
        SubHeader(box, header);
        var g = new GridContainer { Columns = cols };
        g.AddThemeConstantOverride("h_separation", 8);
        g.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        g.AddThemeConstantOverride("v_separation", 2);
        box.AddChild(g);
        return g;
    }

    private void BuildPrefsTab()
    {
        var sc = Tab("Preferences");
        _tabs!.SetTabTitle(_tabs.GetTabCount() - 1,
            UiTextEn.Translate("Preferences"));
        var box = TabBox(sc);

        _pkMouseRow = SliderRow(box, UiTextEn.Translate("Mouse"),
            0.25, 2.5, 0.05, HudPrefs.MouseSpeed, "{0:0.00}x",
            v => HudPrefs.MouseSpeed = (float)v);

        var g = PrefGrid(box, "My character");
        Pref(g, "My name", () => HudPrefs.ShowOwnName, v => HudPrefs.ShowOwnName = v);
        Pref(g, "My guild", () => HudPrefs.ShowGuild, v => HudPrefs.ShowGuild = v);
        Pref(g, "My title", () => HudPrefs.ShowOwnTitle, v => HudPrefs.ShowOwnTitle = v);

        g = PrefGrid(box, "Other players");
        Pref(g, "Names", () => HudPrefs.ShowNames, v => HudPrefs.ShowNames = v);
        var og = Pref(g, "Guilds", () => HudPrefs.ShowOthersGuild, v => HudPrefs.ShowOthersGuild = v);
        og.TooltipText = UiTextEn.Translate("Only shown when the server sends the guild of other players.");
        Pref(g, "Titles", () => HudPrefs.ShowTitle, v => HudPrefs.ShowTitle = v);

        g = PrefGrid(box, "Display");
        Pref(g, "NPC Name", () => HudPrefs.ShowNpcNames, v => HudPrefs.ShowNpcNames = v);
        Pref(g, "Vitals", () => HudPrefs.ShowVitals, v => HudPrefs.ShowVitals = v);
        Pref(g, "Damage", () => HudPrefs.ShowDamage, v => HudPrefs.ShowDamage = v);
        Pref(g, "FPS / Ping", () => HudPrefs.ShowPerf, v => HudPrefs.ShowPerf = v);
        Pref(g, "Show IDs", () => HudPrefs.ShowIds, v => HudPrefs.ShowIds = v);
        Pref(g, "Jelly Effect", () => HudPrefs.JellyEffect, v => HudPrefs.JellyEffect = v);

        g = PrefGrid(box, "Combat");
        Pref(g, "Self-Cast", () => HudPrefs.SelfCast, v => HudPrefs.SelfCast = v);
        Pref(g, "Critical Shock", () => HudPrefs.CritShock, v => HudPrefs.CritShock = v);
        Pref(g, "Vibration", () => HudPrefs.Vibration, v => HudPrefs.Vibration = v);

        g = PrefGrid(box, "Target", 1);
        Pref(g, "Show HP bar above monsters", () => HudPrefs.BarMobs, v => HudPrefs.BarMobs = v);
        Pref(g, "Show HP bar above myself", () => HudPrefs.BarSelf, v => HudPrefs.BarSelf = v);
        Pref(g, "Show HP bar above other players", () => HudPrefs.BarOthers, v => HudPrefs.BarOthers = v);

        g = PrefGrid(box, "Camera");
        Pref(g, "Invert Camera", () => HudPrefs.InvertCamera, v => HudPrefs.InvertCamera = v);
        Pref(g, "Camera Follow", () => HudPrefs.CameraFollow, v => HudPrefs.CameraFollow = v);
        Pref(g, "Left-click to route", () => HudPrefs.LeftClickRoute, v => HudPrefs.LeftClickRoute = v);

        g = PrefGrid(box, "Social");
        Pref(g, "Refuse Trade", () => HudPrefs.RefuseTrade, v => HudPrefs.RefuseTrade = v);
        Pref(g, "No Duel", () => HudPrefs.NoDuel, v => HudPrefs.NoDuel = v);
        Pref(g, "No Party", () => HudPrefs.NoParty, v => HudPrefs.NoParty = v);

        SubHeader(box, "HP loss animation");
        Pref(box, "Animate HP loss (orange)", () => HudPrefs.LossAnim, v => HudPrefs.LossAnim = v);
        (_pkDelay, _pkDelayLbl) = SliderRow(box,
            UiTextEn.Translate("Delay before drop (s)"), 0.1, 3.0, 0.05,
            HudPrefs.LossDelay, "{0:0.00}", v => HudPrefs.LossDelay = (float)v);
        (_pkDur, _pkDurLbl) = SliderRow(box,
            UiTextEn.Translate("Drop duration (s)"), 0.05, 2.0, 0.05,
            HudPrefs.LossDuration, "{0:0.00}", v => HudPrefs.LossDuration = (float)v);

        var reset = new Button
        {
            Text = UiTextEn.Translate("Reset preferences"),
            CustomMinimumSize = new Vector2(0, 22),
        };
        reset.Pressed += () => { HudPrefs.ResetPrefs(); RefreshAll(); };
        box.AddChild(reset);
    }

    private void SubHeader(VBoxContainer box, string title)
    {
        var l = new Label
        {
            Text = "— " + UiTextEn.Translate(title) + " —",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _headers.Add(l);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        box.AddChild(l);
    }

    private void SyncPrefs()
    {
        foreach (var (b, get) in _pkChecks) b.ButtonPressed = get();
        if (_pkMouseRow is { } mr) mr.Item1.Value = HudPrefs.MouseSpeed;
        if (_pkDelay != null) _pkDelay.Value = HudPrefs.LossDelay;
        if (_pkDur != null) _pkDur.Value = HudPrefs.LossDuration;
    }
}
