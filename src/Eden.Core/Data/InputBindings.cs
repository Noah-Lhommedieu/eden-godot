
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
        // Hors Godot : répliquer la liste des noms Key usuels serait
        // fragile — on accepte tout nom non vide ([A-Z0-9], F1-F35…)
        // et la validation réelle se fait côté Godot.
        return name.Length > 0;
    }


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

}
