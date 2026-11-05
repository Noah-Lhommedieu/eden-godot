using Godot;
using EdenClient.Data;
using EdenClient.Hud;

namespace EdenClient.Settings;

/// <summary>Extension de l'onglet « Préférences » de
/// <see cref="SettingsWindow"/> : les deux options complémentaires des
/// valeurs PV/PM — « actuel/max » vs « actuel seul »
/// (<see cref="HudPrefs.ValuesShowMax"/>) et affichage des libellés
/// « PV »/« PM » (<see cref="HudPrefs.ValuesLabels"/>) — injectées juste
/// sous « Valeurs au-dessus des entités » à la première ouverture de la
/// fenêtre (<c>BuildPrefsTab</c> vit dans settings/, hors périmètre
/// d'édition : l'injection se fait ici via partial + notification de
/// visibilité, sans toucher le fichier d'origine).</summary>
public partial class SettingsWindow
{
    private bool _vitalPrefsInjected;

    /// <summary>Injecte les cases dès que la fenêtre devient visible —
    /// <c>BuildPrefsTab</c> a alors déjà tourné (_Ready).</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && Visible)
            InjectVitalValuePrefs();
    }

    /// <summary>Ajoute « Show current/max » + « Show HP/MP labels » sous la
    /// ligne « Values above entities ». Enregistrées dans
    /// <c>_pkChecks</c> via <c>Pref()</c> → SyncPrefs/Reset couverts.</summary>
    private void InjectVitalValuePrefs()
    {
        if (_vitalPrefsInjected) return;
        _vitalPrefsInjected = true;
        // _pkOvVals vit dans une ligne HBox (Opt → Row) ; son parent est
        // la VBox de l'onglet Préférences.
        var row = _pkOvVals?.GetParent();
        if (row?.GetParent() is not Container box) return;
        var idx = row.GetIndex() + 1;
        var cbMax = Pref(box, "Show current/max values",
            () => HudPrefs.ValuesShowMax, v => HudPrefs.ValuesShowMax = v);
        var cbLbl = Pref(box, "Show HP/MP labels",
            () => HudPrefs.ValuesLabels, v => HudPrefs.ValuesLabels = v);
        box.MoveChild(cbMax, idx);
        box.MoveChild(cbLbl, idx + 1);
    }
}
