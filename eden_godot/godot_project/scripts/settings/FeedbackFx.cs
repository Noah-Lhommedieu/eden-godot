using Godot;
using EdenClient.Hud;

namespace EdenClient.Settings;

/// <summary>
/// Effets de retour déclenchés par les préférences natives :
/// « Critical Shock » (secousse caméra), « Vibration » (manette) et
/// « Jelly Effect » (rebond élastique à l'ouverture des fenêtres).
/// </summary>
public static class FeedbackFx
{
    /// <summary>Critique subi (<paramref name="taken"/>) ou infligé :
    /// secousse caméra (pref Critical Shock) + vibration manette
    /// (pref Vibration, si une manette est connectée).</summary>
    public static void Crit(bool taken)
    {
        if (HudPrefs.CritShock)
            SettingsApplier.Instance?.Shake(taken ? 0.14f : 0.07f);
        if (HudPrefs.Vibration &&
            Input.GetConnectedJoypads().Count > 0)
            Input.StartJoyVibration(Input.GetConnectedJoypads()[0],
                taken ? 0.6f : 0.3f, taken ? 0.9f : 0.5f,
                taken ? 0.25f : 0.15f);
    }

    /// <summary>Rebond élastique d'une fenêtre qui vient de s'ouvrir
    /// (pref Jelly Effect) : échelle 0,85 → 1 depuis le centre.</summary>
    public static void JellyPop(Control win)
    {
        if (!GodotObject.IsInstanceValid(win)) return;
        // Différé : la taille n'est valide qu'après la passe d'ancres.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(win) || !win.Visible ||
                !win.IsInsideTree()) return;
            if (win.HasMeta("jelly_tw") &&
                win.GetMeta("jelly_tw").AsGodotObject() is Tween old &&
                GodotObject.IsInstanceValid(old))
                old.Kill();
            win.PivotOffset = win.Size / 2f;
            win.Scale = new Vector2(0.85f, 0.85f);
            var tw = win.CreateTween();
            tw.TweenProperty(win, "scale", Vector2.One, 0.45)
                .SetTrans(Tween.TransitionType.Elastic)
                .SetEase(Tween.EaseType.Out);
            tw.Finished += () =>
            {
                if (GodotObject.IsInstanceValid(win))
                    win.Scale = Vector2.One;
            };
            win.SetMeta("jelly_tw", tw);
        }).CallDeferred();
    }
}
