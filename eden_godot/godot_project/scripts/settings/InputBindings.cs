#if GODOT
using Godot;
#endif

namespace EdenClient.Settings;

/// <summary>
/// Passerelle bindings sauvegardés → <c>InputMap</c> Godot (D2).
///
/// La moitié haute est <b>pure</b> (plan action → noms de touches,
/// testable dans NetTests) ; <see cref="Apply"/> et la conversion
/// nom → <c>InputEvent</c> sont compilés uniquement sous Godot.
///
/// Les actions <c>move_*</c> sont celles lues par
/// <c>PlayerController</c> (<c>Input.GetVector</c>) : le remap
/// d'une touche de déplacement prend effet dès l'application, sans
/// toucher player/ (<c>EnsureInputActions</c> ne pose les défauts
/// que sur des actions vides — l'autoload applique avant le jeu).
/// Les actions <c>eden_*</c> sont dispatchées par
/// <see cref="SettingsApplier"/>.
/// </summary>
public static class InputBindings
{
    /// <summary>Plan pur : paires (action, nom d'événement) issues des
    /// bindings sauvegardés — ce que <see cref="Apply"/> poserait dans
    /// l'InputMap. Testable sans runtime Godot.</summary>
    public static List<(string Action, string Event)> BuildPlan(
        ClientSettings s)
    {
        var plan = new List<(string, string)>();
        foreach (var (action, keys) in s.Bindings)
            foreach (var k in keys)
                plan.Add((action, k));
        return plan;
    }

    /// <summary>true si <paramref name="name"/> est un nom d'événement
    /// reconnu (Key enum Godot ou bouton souris Mouse*/Mouse4/Mouse5 —
    /// la molette n'est PAS bindable, demande spec) — pur, utilisé par
    /// les tests et la validation de la fenêtre.</summary>
    public static bool IsValidEventName(string name)
    {
        if (name.StartsWith("Wheel", StringComparison.Ordinal))
            return false;
        if (name.StartsWith("Mouse", StringComparison.Ordinal))
            return MouseButtonNumber(name) > 0;
#if GODOT
        return Enum.TryParse<Key>(name, true, out _);
#else
        // Hors Godot : répliquer la liste des noms Key usuels serait
        // fragile — on accepte tout nom non vide ([A-Z0-9], F1-F35…)
        // et la validation réelle se fait côté Godot.
        return name.Length > 0;
#endif
    }

#if GODOT
    /// <summary>Applique tous les bindings sauvegardés à l'InputMap :
    /// chaque action est vidée puis re-remplie — appelé au boot par
    /// <see cref="SettingsApplier"/> et après chaque remap.</summary>
    public static void Apply(ClientSettings s)
    {
        foreach (var (action, keys) in s.Bindings)
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionEraseEvents(action);
            foreach (var k in keys)
                if (ParseEvent(k) is { } ev)
                    InputMap.ActionAddEvent(action, ev);
        }
    }

    /// <summary>Nom sauvegardé → InputEvent (Key enum, ou bouton
    /// souris : MouseLeft/MouseRight/MouseMiddle/MouseXButton1/2
    /// (alias Mouse4/Mouse5). La molette n'est pas bindable).
    /// null si le nom est inconnu.</summary>
    public static InputEvent? ParseEvent(string name)
    {
        var mb = MouseButtonNumber(name);
        if (mb > 0)
            return new InputEventMouseButton
            {
                ButtonIndex = (MouseButton)mb,
            };
        if (Enum.TryParse<Key>(name, true, out var key))
            return new InputEventKey { PhysicalKeycode = key };
        return null;
    }

    /// <summary>InputEvent → nom sauvegardé (réciproque de
    /// <see cref="ParseEvent"/>) — null si non représentable.</summary>
    public static string? EventName(InputEvent e) => e switch
    {
        InputEventKey k => k.PhysicalKeycode != Key.None
            ? k.PhysicalKeycode.ToString()
            : k.Keycode != Key.None ? k.Keycode.ToString() : null,
        InputEventMouseButton mb => MouseButtonName(mb.ButtonIndex),
        _ => null,
    };

    /// <summary>Libellé compact d'un binding pour l'UI
    /// (« W, ↑ » — jointure des noms).</summary>
    public static string Describe(IEnumerable<string> keys) =>
        string.Join(", ", keys);
#endif

    /// <summary>MouseButton numérique depuis un nom
    /// (MouseLeft=1, MouseRight=2, MouseMiddle=3,
    /// MouseXButton1/2=8/9 — alias Mouse4/Mouse5 des souris
    /// « gamer ») — 0 si non souris. La molette (4-7) n'est pas
    /// bindable.</summary>
    private static int MouseButtonNumber(string name) =>
        name.ToLowerInvariant() switch
        {
            "mouseleft" => 1,
            "mouseright" => 2,
            "mousemiddle" => 3,
            "mousexbutton1" or "mbx1" or "mouse4" => 8,
            "mousexbutton2" or "mbx2" or "mouse5" => 9,
            _ => 0,
        };

#if GODOT
    private static string? MouseButtonName(MouseButton b) => b switch
    {
        MouseButton.Left => "MouseLeft",
        MouseButton.Right => "MouseRight",
        MouseButton.Middle => "MouseMiddle",
        MouseButton.Xbutton1 => "MouseXButton1",
        MouseButton.Xbutton2 => "MouseXButton2",
        _ => null,
    };
#endif
}
