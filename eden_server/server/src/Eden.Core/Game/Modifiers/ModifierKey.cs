namespace Eden.Core.Game.Modifiers;

/// <summary>Unité d'une valeur modifiable (documentation + validation).</summary>
public enum ModifierUnit { Ratio, Seconds, Count, Percent, Points }

/// <summary>
/// Clé d'une valeur de gameplay modifiable (EXP, temps de craft, entrées de
/// donjon…). Déclarative : ajouter une clé = ajouter une ligne dans
/// <see cref="ModifierKeys"/>, jamais un <c>case</c> dans un système de jeu.
/// </summary>
/// <param name="Id">Identifiant stable, ex. <c>exp.character</c>.</param>
/// <param name="Default">Valeur de base quand aucune source ne répond
/// (1 pour un taux/multiplicateur, 10 pour un nombre d'entrées…).</param>
/// <param name="Min">Plancher appliqué en dernier.</param>
/// <param name="Max">Plafond appliqué en dernier.</param>
/// <param name="VipOverridable">Liste blanche : seules ces clés peuvent être
/// surchargées par un profil VIP.</param>
public sealed record ModifierKey(string Id, ModifierUnit Unit, double Default,
    double Min, double Max, bool VipOverridable, string Description);

/// <summary>Registre des clés connues.</summary>
public static class ModifierKeys
{
    public static readonly ModifierKey ExpCharacter = new(
        "exp.character", ModifierUnit.Ratio, 1, 0, 1000, true,
        "Taux d'EXP du personnage (kills et quêtes)");
    public static readonly ModifierKey ExpClass = new(
        "exp.class", ModifierUnit.Ratio, 1, 0, 1000, true,
        "Taux d'EXP des classes normales (1-20)");
    public static readonly ModifierKey ExpAwakenedClass = new(
        "exp.awakened_class", ModifierUnit.Ratio, 1, 0, 1000, true,
        "Taux d'EXP des classes éveillées (21-60)");
    // Taux de serveur appliqués aux GAINS DE MOBS seulement (config EXP_RATE, ClassExpRate…, événements).
    public static readonly ModifierKey ExpKillCharacter = new(
        "exp.kill.character", ModifierUnit.Ratio, 1, 0, 100000, false,
        "Taux serveur d'EXP personnage sur les kills (ExpRate + événements)");
    public static readonly ModifierKey ExpKillClass = new(
        "exp.kill.class", ModifierUnit.Ratio, 1, 0, 100000, false,
        "Taux serveur d'EXP de classe normale sur les kills");
    public static readonly ModifierKey ExpKillAwakenedClass = new(
        "exp.kill.awakened_class", ModifierUnit.Ratio, 1, 0, 100000, false,
        "Taux serveur d'EXP de classe éveillée sur les kills");
    public static readonly ModifierKey DropRate = new(
        "drop.rate", ModifierUnit.Ratio, 1, 0, 1000, true,
        "Taux de drop des monstres");
    public static readonly ModifierKey GoldRate = new(
        "gold.rate", ModifierUnit.Ratio, 1, 0, 1000, true,
        "Taux d'or lâché");
    public static readonly ModifierKey CraftCastSeconds = new(
        "craft.cast_seconds", ModifierUnit.Ratio, 1, 0.01, 100, true,
        "Multiplicateur du temps de cast de craft");
    public static readonly ModifierKey CraftDuration = new(
        "craft.duration", ModifierUnit.Ratio, 1, 0.01, 100, true,
        "Multiplicateur de la durée des ordres de craft et de plans de récolte");
    public static readonly ModifierKey GatherTickSeconds = new(
        "gather.tick_seconds", ModifierUnit.Ratio, 1, 0.01, 100, true,
        "Multiplicateur de l'intervalle entre deux tirages de récolte");
    public static readonly ModifierKey GatherDurabilityCost = new(
        "gather.durability_cost", ModifierUnit.Ratio, 1, 0.01, 100, true,
        "Multiplicateur de la durabilité consommée par temps de récolte");
    public static readonly ModifierKey DungeonEntriesSolo = new(
        "dungeon.entries.solo", ModifierUnit.Count, 10, 0, 999, true,
        "Entrées de donjon solo par jour de reset");
    public static readonly ModifierKey DungeonEntriesMulti = new(
        "dungeon.entries.multi", ModifierUnit.Count, 10, 0, 999, true,
        "Entrées de donjon multijoueur par jour de reset");

    public static IReadOnlyList<ModifierKey> All { get; } = new[]
    {
        ExpCharacter, ExpClass, ExpAwakenedClass, ExpKillCharacter, ExpKillClass,
        ExpKillAwakenedClass, DropRate, GoldRate,
        CraftCastSeconds, CraftDuration, GatherTickSeconds,
        GatherDurabilityCost, DungeonEntriesSolo, DungeonEntriesMulti,
    };

    public static ModifierKey? Find(string id) =>
        All.FirstOrDefault(k => k.Id == id);

    /// <summary>Clé d'EXP de classe selon le type de classe : le jeu sépare
    /// réellement classes normales et éveillées.</summary>
    public static ModifierKey ExpKillFor(ClassKind kind) => kind switch
    {
        ClassKind.Awakened => ExpKillAwakenedClass,
        ClassKind.Normal => ExpKillClass,
        _ => ExpKillCharacter,
    };

    public static ModifierKey ExpFor(ClassKind kind) => kind switch
    {
        ClassKind.Awakened => ExpAwakenedClass,
        ClassKind.Normal => ExpClass,
        _ => ExpCharacter,
    };
}
