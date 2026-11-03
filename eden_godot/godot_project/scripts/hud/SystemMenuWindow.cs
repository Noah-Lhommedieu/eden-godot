using Godot;
using EdenClient.Data;
using EdenClient.Screens;

namespace EdenClient.Hud;

/// <summary>
/// « Main Menu » (ref SystemPanel.png — Échap ou icône système du
/// menu) : 6 boutons empilés Settings / Reset UI / Ch. / Logout /
/// Quit / Continue dans un encadré sombre, fenêtre 130×234 centrée.
/// </summary>
public partial class SystemMenuWindow : Control
{
    private Control? _win;
    /// <summary>Panneau visible (registre <see cref="HudWindows"/>).</summary>
    public Control? Window => _win;

    public void Build(HudRoot hud)
    {
        var win = EeSkin.MakeWindow(hud, "Main Menu", 574, 242, 130, 294);
        _win = win;
        AddChild(win);
        var box = new Panel();
        HudRoot.Pin(box, 0, 0, 1, 1, 8, 26, -8, -8);
        box.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        win.AddChild(box);
        var items = new (string Label, Action? Act)[]
        {
            (UiTextEn.Translate("Settings"), () => { HudWindows.Close(_win);
                Settings.SettingsApplier.Open(); }),
            ("Reset UI", DragHelper.ResetAllPositions),
            (UiTextEn.Translate("Item preview"), () =>
            { HudWindows.Close(_win); ItemPreviewWindow.Toggle(); }),
            (UiTextEn.Translate("Ch."), null),
            (UiTextEn.Translate("Logout"), Logout),
            (UiTextEn.Translate("Quit"), () => GetTree().Quit()),
            ("Continue", () => HudWindows.Close(_win)),
        };
        for (var i = 0; i < items.Length; i++)
        {
            var (label, act) = items[i];
            var b = EeSkin.Grey(new Button
            {
                Text = label, Disabled = act == null,
                TooltipText = act == null ? UiTextEn.Translate("Not available yet") : "",
                FocusMode = FocusModeEnum.None,
            }, 11);
            HudRoot.Pin(b, 0, 0, 0, 0, 13, 14 + i * 30, 101, 34 + i * 30);
            if (act != null) b.Pressed += act;
            box.AddChild(b);
        }
        win.Visible = false;
    }

    public void Toggle() => HudWindows.Toggle(_win);

    /// <summary>Déconnexion de toutes les connexions puis retour à
    /// l'écran de connexion (scène boot → login).</summary>
    private async void Logout()
    {
        HudWindows.Close(_win);
        var s = SessionCarry.Session;
        SessionCarry.Session = null;
        if (s != null)
        {
            try { await s.DisconnectAllAsync(); }
            catch (Exception e) { GD.PrintErr($"[menu] logout : {e.Message}"); }
        }
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
            "res://scenes/boot.tscn");
    }
}
