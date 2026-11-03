using Godot;
using EdenClient.Combat;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Logique de la barre de skills (lot S1) — sans rendu : les slots
/// visibles sont ceux de la FastBar1 native (<see cref="HotbarPanel"/>,
/// ref HotbarPanel.png). Garde la table slot → sort
/// (<see cref="PlayerSkills.Spells"/> dans l'ordre) et les actions
/// <c>eden_slot_1..12</c> (defauts 1-9,0,-,= — rebindables, boutons
/// souris compris) → <see cref="SkillCast.Cast"/> ; le clic droit sur un
/// slot passe par <see cref="CastSlot"/>.
/// </summary>
public partial class SkillBarWindow : Control
{
    private const int SlotCount = 12;

    private readonly ushort[] _spellIds = new ushort[SlotCount];

    /// <summary>Instance montée (HudExtensions) — cible des clics
    /// droits de la hotbar native.</summary>
    public static SkillBarWindow? Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        PlayerSkills.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        PlayerSkills.Changed -= Refresh;
        if (Instance == this) Instance = null;
        base._ExitTree();
    }

    private void Refresh()
    {
        var spells = PlayerSkills.Spells;
        for (var i = 0; i < SlotCount; i++)
            _spellIds[i] = i < spells.Count
                ? (ushort)spells[i].SpellMaster : (ushort)0;
    }

    /// <summary>Active le slot (clic droit hotbar).</summary>
    public void CastSlot(int slot) => TryCast(slot);

    /// <summary>Clic / touche → cast via <see cref="SkillCast"/>.</summary>
    private void TryCast(int slot)
    {
        if (slot < 0 || slot >= _spellIds.Length) return;
        var id = _spellIds[slot];
        if (id == 0) return;
        SkillCast.Instance?.Cast(id);
    }

    /// <summary>Actions <c>eden_slot_i</c> (bindings persistés —
    /// touches ou boutons souris) → slot de la rangée principale ;
    /// Ctrl+action → rangée « Sub » (labels natifs C1…C+). Ignoré
    /// quand un champ de saisie a le focus (chat).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Echo: true }) return;
        if (e is not (InputEventKey or InputEventMouseButton)) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;
        for (var i = 0; i < SlotCount; i++)
            if (e.IsActionPressed($"eden_slot_{i + 1:D2}"))
            {
                var row = e is InputEventWithModifiers { CtrlPressed: true }
                    ? 1 : 0;
                if (HotbarPanel.Instance != null)
                    HotbarPanel.Instance.Activate(row, i);
                else TryCast(i);
                GetViewport().SetInputAsHandled();
                return;
            }
    }
}
