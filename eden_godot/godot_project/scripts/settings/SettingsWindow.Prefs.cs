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
        var c = Check(parent, T(label), get(), set);
        c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pkChecks.Add((c, get));
        return c;
    }

    private (HSlider, Label)? _pkMouseRow;
    private OptionButton? _pkTgtVals, _pkOvVals;

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

    private void BuildPrefsTab(VBoxContainer box)
    {

        _pkMouseRow = SliderRow(box, T("Mouse"),
            0.25, 2.5, 0.05, HudPrefs.MouseSpeed, "{0:0.00}x",
            v => HudPrefs.MouseSpeed = (float)v);

        var g = PrefGrid(box, "My character");
        Pref(g, "My name", () => HudPrefs.ShowOwnName, v => HudPrefs.ShowOwnName = v);
        Pref(g, "My guild", () => HudPrefs.ShowGuild, v => HudPrefs.ShowGuild = v);
        Pref(g, "My title", () => HudPrefs.ShowOwnTitle, v => HudPrefs.ShowOwnTitle = v);

        g = PrefGrid(box, "Other players");
        Pref(g, "Names", () => HudPrefs.ShowNames, v => HudPrefs.ShowNames = v);
        var og = Pref(g, "Guilds", () => HudPrefs.ShowOthersGuild, v => HudPrefs.ShowOthersGuild = v);
        og.TooltipText = T("Only shown when the server sends the guild of other players.");
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

        string[] valItems = { UiTextEn.Translate("None"),
            UiTextEn.Translate("HP"), UiTextEn.Translate("MP"),
            UiTextEn.Translate("HP + MP") };
        _pkTgtVals = Opt(box, UiTextEn.Translate("Target frame values"),
            valItems, (int)HudPrefs.TargetValues,
            i => HudPrefs.TargetValues = (HudPrefs.ValuesMode)i);
        _pkOvVals = Opt(box, UiTextEn.Translate("Values above entities"),
            valItems, (int)HudPrefs.OverheadValues,
            i => HudPrefs.OverheadValues = (HudPrefs.ValuesMode)i);

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
            T("Delay before drop (s)"), 0.1, 3.0, 0.05,
            HudPrefs.LossDelay, "{0:0.00}", v => HudPrefs.LossDelay = (float)v);
        (_pkDur, _pkDurLbl) = SliderRow(box,
            T("Drop duration (s)"), 0.05, 2.0, 0.05,
            HudPrefs.LossDuration, "{0:0.00}", v => HudPrefs.LossDuration = (float)v);

        var reset = new Button
        {
            Text = T("Reset preferences"),
            CustomMinimumSize = new Vector2(0, 22),
        };
        reset.Pressed += () => { HudPrefs.ResetPrefs(); RefreshAll(); };
        box.AddChild(reset);
    }

    /// <summary>En-tête de groupe : titre doré aligné à gauche + filet.
    /// Utilisé par tous les onglets (extensible : un nouveau groupe =
    /// un SubHeader puis ses lignes).</summary>
    private void SubHeader(VBoxContainer box, string title)
    {
        var l = new Label
        {
            Text = T(title),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        l.AddThemeFontSizeOverride("font_size", 14);
        _headers.Add(l);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        box.AddChild(l);
        box.AddChild(new HSeparator());
    }

    private void SyncPrefs()
    {
        foreach (var (b, get) in _pkChecks) b.ButtonPressed = get();
        if (_pkTgtVals != null) _pkTgtVals.Selected = (int)HudPrefs.TargetValues;
        if (_pkOvVals != null) _pkOvVals.Selected = (int)HudPrefs.OverheadValues;
        if (_pkMouseRow is { } mr) mr.Item1.Value = HudPrefs.MouseSpeed;
        if (_pkDelay != null) _pkDelay.Value = HudPrefs.LossDelay;
        if (_pkDur != null) _pkDur.Value = HudPrefs.LossDuration;
    }
}
