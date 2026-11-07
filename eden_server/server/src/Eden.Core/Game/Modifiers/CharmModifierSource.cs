namespace Eden.Core.Game.Modifiers;

/// <summary>
/// Charmes et enchants actifs du joueur (natif <c>ItemExpRateUp</c> 2911, <c>ItemWakenNpRateUp</c> 2912/2914) :
/// bonus % additifs, fournis par le contexte (la zone somme les statuts actifs). Le jeu SÉPARE
/// l'EXP des classes normales (bonus <c>CharmClassPercent</c>) et des classes éveillées
/// (<c>CharmAwakenedPercent</c>) : un charme de classe ne profite pas à une classe éveillée.
/// </summary>
public sealed class CharmModifierSource : IModifierSource
{
    public string Name => "charm";

    public void Collect(ModifierKey key, in ModifierContext ctx, List<ModifierContribution> sink)
    {
        var pct = key.Id switch
        {
            "exp.character" => ctx.CharmExpPercent,
            "exp.class" => ctx.CharmClassPercent,
            "exp.awakened_class" => ctx.CharmAwakenedPercent,
            _ => 0,
        };
        if (pct != 0) sink.Add(new ModifierContribution(Name, ModifierMode.AddPercent, pct));
    }
}
