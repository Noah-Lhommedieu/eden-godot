using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Corrige la cause racine « les panneaux craft / ville de guilde passent
/// DERRIÈRE tout » (PLAN-ACTION-3 A1) : <c>game.tscn</c> posait ces fenêtres
/// dans un <c>CanvasLayer</c> « ZoneWindows » de <c>layer = 9</c>, alors que le
/// HUD est en <c>layer = 10</c>. Un CanvasLayer de rang supérieur est TOUJOURS
/// dessiné au-dessus, quel que soit l'ordre des enfants : <see cref="HudWindows.Front"/>
/// ne pouvait donc rien y changer.
/// <para>Solution : toutes les fenêtres vivent sous la MÊME couche que le HUD.
/// Les enfants de « ZoneWindows » sont détachés AVANT leur entrée dans l'arbre
/// (donc avant leur <c>_Ready</c> : pas de cycle ExitTree/EnterTree qui
/// désabonnerait leurs handlers réseau) puis rattachés au HUD.</para>
/// </summary>
public static class ZoneWindowHost
{
    public const string LayerNodeName = "ZoneWindows";

    /// <summary>À appeler depuis <c>GameScene._EnterTree</c> (les enfants ne sont
    /// pas encore dans l'arbre). Retire le CanvasLayer de la scène et renvoie
    /// ses fenêtres.</summary>
    public static List<Control> Detach(Node scene)
    {
        var windows = new List<Control>();
        if (scene.GetNodeOrNull<CanvasLayer>(LayerNodeName) is not { } layer)
            return windows;
        foreach (var child in layer.GetChildren())
        {
            if (child is not Control c) continue;
            layer.RemoveChild(c);
            c.Owner = null; // évite l'avertissement « owner inconsistent » au rattachement
            windows.Add(c);
        }
        scene.RemoveChild(layer);
        layer.QueueFree();
        return windows;
    }

    /// <summary>Rattache les fenêtres à <paramref name="parent"/> (le HUD, ou un
    /// calque de repli si le HUD est désactivé).</summary>
    public static void Attach(IEnumerable<Control> windows, Node parent)
    {
        foreach (var w in windows) parent.AddChild(w);
    }

    /// <summary>Calque de repli au rang du HUD (mode <c>--nohud</c>).</summary>
    public static CanvasLayer CreateFallbackLayer(Node scene)
    {
        var layer = new CanvasLayer { Name = LayerNodeName, Layer = 10 };
        scene.AddChild(layer);
        return layer;
    }
}
