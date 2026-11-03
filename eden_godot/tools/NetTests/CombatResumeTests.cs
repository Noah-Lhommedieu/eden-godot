using EdenClient.Combat;
using EdenClient.Settings;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Règles natives cast → auto-attaque (<see cref="PostCastRules"/>) :
///   1. après un sort, l'auto-attaque ne reprend QUE si la cible est
///      déjà à portée — hors de portée : engagement armé mais AUCUN
///      déplacement vers elle (AutoAttack._holdAfterCast) ;
///   2. pendant le cast, aucune auto-attaque n'est émise — une attaque
///      casserait le cast (natif MeleeAttack → ISpell::Break) ;
///   3. « annuler le cast » est un bind configurable, Shift+S par
///      défaut, qui émet le NCID 13 (ZoneSpellBreak).
/// </summary>
public class CombatResumeTests
{
    // ------------------------------------------------ règle 1 ------

    [Fact]
    public void Decide_InRange_Resumes()
    {
        Assert.Equal(PostCastAttack.Resume,
            PostCastRules.Decide(true, 2.9f, 3.0f));
    }

    [Fact]
    public void Decide_ExactlyAtReach_Resumes()
    {
        // À portée pile (dist == reach) la reprise est licite.
        Assert.Equal(PostCastAttack.Resume,
            PostCastRules.Decide(true, 3.0f, 3.0f));
    }

    [Fact]
    public void Decide_OutOfRange_Holds()
    {
        // Hors de portée : armée mais jamais de poursuite.
        Assert.Equal(PostCastAttack.Hold,
            PostCastRules.Decide(true, 8f, 3.0f));
    }

    [Fact]
    public void Decide_NoTarget_Nothing()
    {
        Assert.Equal(PostCastAttack.None,
            PostCastRules.Decide(false, 0f, 3.0f));
    }

    // ------------------------------------------------ règle 2 ------

    [Fact]
    public void AutoAttackFrozen_DuringCast()
    {
        // Swing ET approche gelés pendant tout le vol du cast.
        Assert.True(PostCastRules.AutoAttackFrozen(true));
        Assert.False(PostCastRules.AutoAttackFrozen(false));
    }

    // ------------------------------------------------ règle 3 ------

    [Fact]
    public void CancelCast_DefaultBinding_IsShiftS()
    {
        var defs = ClientSettings.DefaultBindings();
        Assert.True(defs.ContainsKey("eden_cancel_cast"));
        Assert.Equal(new List<string> { "Shift+S" },
            defs["eden_cancel_cast"]);
        // La ligne existe aussi dans la table affichée par la fenêtre
        // (RebuildKeyRows itère BindingDefs directement).
        Assert.Contains(ClientSettings.BindingDefs,
            d => d.Action == "eden_cancel_cast" &&
                 d.Defaults.Length == 1 && d.Defaults[0] == "Shift+S");
    }

    [Fact]
    public void CancelCast_Binding_SurvivesRoundTrip()
    {
        // Sérialisation settings.cfg : « keys.eden_cancel_cast=Shift+S ».
        var s = new ClientSettings();
        s.ResetBindings();
        var text = s.Serialize();
        Assert.Contains("eden_cancel_cast=Shift+S", text);
        var back = ClientSettings.Parse(text);
        Assert.Equal(new List<string> { "Shift+S" },
            back.GetBinding("eden_cancel_cast"));
    }

    [Fact]
    public void CancelCast_Binding_NameIsValid()
    {
        Assert.True(InputBindings.IsValidEventName("Shift+S"));
        Assert.True(InputBindings.IsValidEventName("Ctrl+Shift+F1"));
        Assert.True(InputBindings.IsValidEventName("Shift"));   // modif seul
        // Modificateur sur un bouton souris : refusé.
        Assert.False(InputBindings.IsValidEventName("Shift+MouseLeft"));
        // Modificateur inconnu en préfixe : refusé.
        Assert.False(InputBindings.IsValidEventName("Super+S"));
        // Nom vide après modificateur : refusé (hors Godot).
        Assert.False(InputBindings.IsValidEventName("Shift+"));
    }
}
