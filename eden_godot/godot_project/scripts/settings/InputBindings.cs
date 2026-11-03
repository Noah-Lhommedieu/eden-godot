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
    /// reconnu (Key enum Godot, éventuellement préfixé de modificateurs
    /// « Shift+ », « Ctrl+ », « Alt+ », « Meta+ » — ex. « Shift+S » —
    /// ou bouton souris Mouse*/Mouse4/Mouse5 — la molette n'est PAS
    /// bindable, demande spec) — pur, utilisé par les tests et la
    /// validation de la fenêtre.</summary>
    public static bool IsValidEventName(string name)
    {
        var parts = name.Split('+');
        // Modificateurs : tous les segments sauf le dernier.
        for (var i = 0; i < parts.Length - 1; i++)
            if (!IsModifierName(parts[i])) return false;
        name = parts[^1];
        if (name.StartsWith("Wheel", StringComparison.Ordinal))
            return false;
        if (name.StartsWith("Mouse", StringComparison.Ordinal))
            // Pas de modificateur sur les boutons souris (ParseEvent
            // le refuse aussi — les deux restent cohérents).
            return parts.Length == 1 && MouseButtonNumber(name) > 0;
#if GODOT
        return Enum.TryParse<Key>(name, true, out _);
#else
        // Hors Godot : répliquer la liste des noms Key usuels serait
        // fragile — on accepte tout nom non vide ([A-Z0-9], F1-F35…)
        // et la validation réelle se fait côté Godot.
        return name.Length > 0;
#endif
    }

    /// <summary>« Shift », « Ctrl », « Alt », « Meta » —
    /// préfixes autorisés des bindings composés (« Shift+S »).</summary>
    private static bool IsModifierName(string n) =>
        n.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("Meta", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Nom sauvegardé → InputEvent (Key enum, éventuellement
    /// composé « Mod+Touche » ex. « Shift+S », ou bouton souris :
    /// MouseLeft/MouseRight/MouseMiddle/MouseXButton1/2 (alias
    /// Mouse4/Mouse5). La molette n'est pas bindable).
    /// null si le nom est inconnu.</summary>
    public static InputEvent? ParseEvent(string name)
    {
        var parts = name.Split('+');
        var keyName = parts[^1];
        var mb = MouseButtonNumber(keyName);
        if (mb > 0)
            return parts.Length == 1
                ? new InputEventMouseButton
                {
                    ButtonIndex = (MouseButton)mb,
                }
                : null;   // pas de modificateur sur les boutons souris
        if (Enum.TryParse<Key>(keyName, true, out var key))
        {
            var ev = new InputEventKey { PhysicalKeycode = key };
            for (var i = 0; i < parts.Length - 1; i++)
                switch (parts[i].ToLowerInvariant())
                {
                    case "shift": ev.ShiftPressed = true; break;
                    case "ctrl": ev.CtrlPressed = true; break;
                    case "alt": ev.AltPressed = true; break;
                    case "meta": ev.MetaPressed = true; break;
                    default: return null;
                }
            return ev;
        }
        return null;
    }

    /// <summary>InputEvent → nom sauvegardé (réciproque de
    /// <see cref="ParseEvent"/>) — modificateurs émis en tête
    /// (« Shift+S ») ; null si non représentable.</summary>
    public static string? EventName(InputEvent e) => e switch
    {
        InputEventKey k => KeyName(k),
        InputEventMouseButton mb => MouseButtonName(mb.ButtonIndex),
        _ => null,
    };

    private static string? KeyName(InputEventKey k)
    {
        var key = k.PhysicalKeycode != Key.None
            ? k.PhysicalKeycode
            : k.Keycode;
        if (key == Key.None) return null;
        // Touche modificateur seule (« Shift » pour move_run) : son
        // propre flag est posé dans l'événement — ne pas produire
        // « Shift+Shift ».
        if (key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta)
            return key.ToString();
        var s = "";
        if (k.ShiftPressed) s += "Shift+";
        if (k.CtrlPressed) s += "Ctrl+";
        if (k.AltPressed) s += "Alt+";
        if (k.MetaPressed) s += "Meta+";
        return s + key;
    }

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
