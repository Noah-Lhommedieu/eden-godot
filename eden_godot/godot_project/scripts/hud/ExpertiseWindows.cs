using Godot;
using EdenClient.Data;
using EdenClient.Screens;

namespace EdenClient.Hud;

/// <summary>Panneaux d'expertise de classe (ref-shots « panel primaire
/// d'expertise » / « second panel expertise ») : deux fenêtres HUD
/// SÉPARÉES du panneau Classes, chacune déplaçable. La première liste
/// les classes (icône, « X Bonus Skill N », « Class Level - L », bouton
/// CE List) ; le bouton ouvre la seconde, qui détaille les paliers de
/// 5 niveaux de la classe choisie (bonus PV/PM/ATQ/ATQ M./Soin).</summary>
public static class ExpertiseWindows
{
    private static readonly string[] Labels =
        { "HP", "MP", "ATK", "M-ATK", "G-Healing" };

    private static Panel? _list;
    private static Panel? _detail;

    private static Color Ink => new(0.96f, 0.95f, 0.92f);

    public static void Toggle(HudRoot hud)
    {
        if (_list != null && GodotObject.IsInstanceValid(_list))
        {
            _list.QueueFree(); _list = null;
            if (_detail != null && GodotObject.IsInstanceValid(_detail))
                _detail.QueueFree();
            _detail = null;
            return;
        }
        Data.Tables.ClassRankBuffTable.PathProvider ??= () =>
            AssetCatalog.ResolveRawAsset("data/db/c_classrankbuff.ini");
        var win = EeSkin.MakeWindow(hud,
            UiTextEn.Translate("Full Class Ability Bonus List"), 330, 120,
            380, 460);
        hud.AddChild(win);
        DragHelper.Persist(win, "ExpertiseList");
        _list = win;

        var sc = new ScrollContainer
            { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        HudRoot.Pin(sc, 0, 0, 1, 1, 8, 30, -8, -8);
        win.AddChild(sc);
        var col = new VBoxContainer
            { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);
        sc.AddChild(col);

        var rows = ClassChangeDialog.Classes
            .Where(e => e.ClassLevel > 0)
            .OrderByDescending(e => Data.Tables.ClassRankBuffTable.Rank(e.ClassLevel) > 0)
            .ThenBy(e => e.ClassId % 100).ToList();
        foreach (var e in rows)
        {
            var rank = Data.Tables.ClassRankBuffTable.Rank(e.ClassLevel);
            var cls = e.ClassId;
            var name = TextResolver.ClassName(cls) ?? $"#{cls}";
            var cur = StatsText(Data.Tables.ClassRankBuffTable
                .Bonus(cls, e.ClassLevel), false);
            col.AddChild(Row(CharSelectScreen.ClassIcon(cls),
                rank > 0
                    ? $"{name} " + UiTextEn.Translate("Bonus Skill") + $" {rank}"
                    : $"{name} - " + UiTextEn.Translate("No Bonus"),
                UiTextEn.Translate("Class Level") + $" - {e.ClassLevel}" +
                    (cur.Length > 0 ? "\n" + cur : ""),
                rank > 0, () => ShowDetail(hud, cls, win)));
        }
        if (rows.Count == 0)
            col.AddChild(Row(null, UiTextEn.Translate("No Bonus"), "", false, null));
    }

    private static Control Row(Texture2D? icon, string title, string sub,
        bool active, Action? onList)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", EeSkin.InsetCard(6, 5));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        p.AddChild(h);
        var ic = new TextureRect
        {
            Texture = icon, CustomMinimumSize = new Vector2(36, 36),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = active ? Colors.White : new Color(1, 1, 1, 0.4f),
        };
        h.AddChild(ic);
        var v = new VBoxContainer
            { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddChild(v);
        var t = new Label
        {
            Text = title, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(190, 0),
        };
        EeSkin.Shadowed(t, 12, active ? Ink : new Color(0.75f, 0.2f, 0.2f));
        v.AddChild(t);
        var s = new Label
        {
            Text = sub, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(190, 0),
        };
        EeSkin.Shadowed(s, 12, active ? Ink : new Color(0.6f, 0.6f, 0.6f));
        v.AddChild(s);
        if (onList != null)
        {
            var b = EeSkin.Grey(new Button
            {
                Text = UiTextEn.Translate("CE List"),
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(70, 22),
                SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            }, 11);
            b.Pressed += onList;
            h.AddChild(b);
        }
        return p;
    }

    private static void ShowDetail(HudRoot hud, int classId, Control anchor)
    {
        if (_detail != null && GodotObject.IsInstanceValid(_detail))
            _detail.QueueFree();
        var name = TextResolver.ClassName(classId) ?? $"#{classId}";
        var win = EeSkin.MakeWindow(hud, $"{name} " +
            UiTextEn.Translate("Ability Bonus List"),
            (int)anchor.Position.X + 374, (int)anchor.Position.Y, 340, 460);
        hud.AddChild(win);
        DragHelper.Persist(win, "ExpertiseDetail");
        _detail = win;
        var sc = new ScrollContainer
            { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        HudRoot.Pin(sc, 0, 0, 1, 1, 8, 30, -8, -8);
        win.AddChild(sc);
        var col = new VBoxContainer
            { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);
        sc.AddChild(col);
        var icon = CharSelectScreen.ClassIcon(classId);
        var cur = ClassChangeDialog.Classes
            .FirstOrDefault(e => e.ClassId == classId)?.ClassLevel ?? 0;
        var prev = new int[Labels.Length];
        for (var rank = 1; rank <= 18; rank++)
        {
            var lv = rank * 5;
            var b = Data.Tables.ClassRankBuffTable.Bonus(classId, lv);
            // Valeurs = totaux cumulés au palier (table native) ; le
            // delta entre parenthèses = gain de ce palier.
            var stats = StatsText(b, false);
            var gain = StatsDelta(b, prev);
            prev = b;
            var reached = cur >= lv;
            col.AddChild(Row(icon,
                $"{name} " + UiTextEn.Translate("Bonus Skill") + $" {rank}",
                UiTextEn.Translate("Class Level") + $" - {lv}" +
                    (stats.Length > 0 ? "\n" + UiTextEn.Translate("Total") +
                        ": " + stats : "") +
                    (gain.Length > 0 ? "\n" + UiTextEn.Translate("Gain") +
                        ": " + gain : ""),
                reached, null));
        }
    }

    private static string StatsText(int[] b, bool _) =>
        string.Join("  ", Enumerable.Range(0, b.Length)
            .Where(i => b[i] > 0)
            .Select(i => $"{UiTextEn.Translate(Labels[i])} +{b[i]}"));

    private static string StatsDelta(int[] b, int[] prev) =>
        string.Join("  ", Enumerable.Range(0, b.Length)
            .Where(i => b[i] - prev[i] > 0)
            .Select(i => $"{UiTextEn.Translate(Labels[i])} +{b[i] - prev[i]}"));
}
