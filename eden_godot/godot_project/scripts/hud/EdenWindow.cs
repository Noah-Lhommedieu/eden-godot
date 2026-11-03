using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Classe de base des fenêtres HUD — factorise la chorégraphie que
/// chaque fenêtre répétait en ligne :
///   <c>hud.Build(layout)</c> → repli <see cref="BuildFallback"/> →
///   <see cref="HudRoot.Pin"/> → <see cref="OnBuilt"/> (câblage natif) →
///   <c>SkinUnstyled</c> → <see cref="DragHelper.EnsureTitleBar"/>
///   (titre + × + drag fond + chrome natif) →
///   <see cref="DragHelper.Persist"/> (position dans ui_layout.cfg).
///
/// Une fenêtre simple n'a qu'à appeler <see cref="BuildWindow"/> ; les
/// spécialisations passent par les overrides
/// <see cref="BuildFallback"/>/<see cref="OnBuilt"/>. Les panneaux
/// construits en code (dialogs sans layout natif) obtiennent la même
/// finition via <see cref="FinishWindow"/>.
/// </summary>
public abstract partial class EdenWindow : Control
{
    /// <summary>Racine matérialisée (layout natif ou repli).</summary>
    protected Control? Window;
    /// <summary>HUD hôte (posé par <see cref="BuildWindow"/>).</summary>
    protected HudRoot? Hud;

    /// <summary>Monte la fenêtre : layout natif épinglé + câblé,
    /// repli code sinon ; titlebar/drag/persist dans tous les cas.
    /// Retourne la racine (null si ni layout ni repli).</summary>
    protected Control? BuildWindow(HudRoot hud, string? layout,
        string title, string persistKey,
        float pinL, float pinT, float pinR, float pinB,
        float offL = 0, float offT = 0, float offR = 0, float offB = 0)
    {
        Hud = hud;
        Window = layout != null ? hud.Build(layout) : null;
        if (Window == null)
        {
            Window = BuildFallback();
            if (Window != null) AddChild(Window);
        }
        else
        {
            AddChild(Window);
            HudRoot.Pin(Window, pinL, pinT, pinR, pinB,
                offL, offT, offR, offB);
            OnBuilt(Window);
            Ui.CeguiToGodot.SkinUnstyled(Window);
        }
        if (Window != null)
        {
            DragHelper.EnsureTitleBar(Window, title);
            DragHelper.Persist(Window, persistKey);
        }
        return Window;
    }

    /// <summary>Finition commune des panneaux construits en code :
    /// titlebar (× + drag fond + chrome natif) + persistance.</summary>
    protected static T FinishWindow<T>(T window,
        string title, string persistKey) where T : Control
    {
        DragHelper.EnsureTitleBar(window, title);
        DragHelper.Persist(window, persistKey);
        return window;
    }

    /// <summary>Repli construit en code quand le layout natif est
    /// absent — retourne la racine à attacher (null → pas de
    /// fenêtre). Ne doit PAS ajouter la titlebar (gérée par
    /// <see cref="BuildWindow"/>).</summary>
    protected virtual Control? BuildFallback() => null;

    /// <summary>Câblage spécifique après Pin du layout natif
    /// (bouton × natif, contenus injectés, titre…).</summary>
    protected virtual void OnBuilt(Control window) { }
}
