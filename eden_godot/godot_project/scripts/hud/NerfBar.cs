using Godot;

namespace EdenClient.Hud;

/// <summary>
/// Barre de nerfs (debuffs actifs sur le joueur) — fenêtre dédiée
/// juste à gauche de la <see cref="BuffBar"/> (hud-interfaces §51).
/// Réutilise toute la logique de <see cref="BuffBar"/> : slots
/// Debuff1-10 natifs de <c>BuffWnd.layout</c>, timer m:ss, stack ×N,
/// tooltip. Pas de clic-droit ici : un nerf n'est pas annulable
/// (<see cref="BuffBar.OnSlotInput"/> le refuse déjà via IsBuff).
/// </summary>
public partial class NerfBar : BuffBar
{
    protected override bool DebuffsOnly => true;
}
