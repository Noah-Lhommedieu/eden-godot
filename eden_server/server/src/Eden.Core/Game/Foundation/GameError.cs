namespace Eden.Core.Game.Foundation;

/// <summary>
/// Erreur de gameplay typée : un <see cref="Code"/> stable (jamais une phrase)
/// relié à un texte FR/EN par le <see cref="ErrorCatalog"/>. Tout refus visible
/// par le joueur doit en porter un — plus de refus silencieux.
/// </summary>
public readonly record struct GameError(string Code,
    IReadOnlyList<object>? Args = null)
{
    public static GameError Of(string code, params object[] args) =>
        new(code, args.Length == 0 ? null : args);

    public override string ToString() => Code;
}

/// <summary>Résultat d'une opération qui peut être refusée.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly GameError? _error;

    private Result(T? value, GameError? error)
    {
        _value = value;
        _error = error;
    }

    public bool IsOk => _error is null;
    public T Value => IsOk
        ? _value!
        : throw new InvalidOperationException(
            $"Result en échec ({_error!.Value.Code}) : pas de valeur.");
    public GameError Error => _error
        ?? throw new InvalidOperationException("Result réussi : pas d'erreur.");

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(GameError error) => new(default, error);
    public static Result<T> Fail(string code, params object[] args) =>
        new(default, GameError.Of(code, args));

    public static implicit operator Result<T>(GameError e) => Fail(e);
}

/// <summary>Codes d'erreur du jeu. Chaque constante DOIT avoir un texte FR
/// dans <c>Foundation/errors.tsv</c> (test <c>ErrorCatalogTests</c>).</summary>
public static class GameErrors
{
    public const string GatherOutOfArea = "GATHER_OUT_OF_AREA";
    public const string GatherBagFull = "GATHER_BAG_FULL";
    public const string GatherToolBroken = "GATHER_TOOL_BROKEN";
    public const string GatherMoved = "GATHER_MOVED";
    public const string GatherLevelTooLow = "GATHER_LEVEL_TOO_LOW";
    public const string GatherAlready = "GATHER_ALREADY";
    public const string GatherStartedMining = "GATHER_STARTED_MINING";
    public const string GatherStartedFarming = "GATHER_STARTED_FARMING";
    public const string GatherStartedCrystal = "GATHER_STARTED_CRYSTAL";
    public const string GatherYield = "GATHER_YIELD";
    public const string CraftBuildingLevelTooLow = "CRAFT_BUILDING_LEVEL_TOO_LOW";
    public const string CraftPlanMissing = "CRAFT_PLAN_MISSING";
    public const string CraftBagFull = "CRAFT_BAG_FULL";
    public const string DungeonNoEntriesLeft = "DUNGEON_NO_ENTRIES_LEFT";
    public const string ConfigInvalidValue = "CONFIG_INVALID_VALUE";
    public const string AdminForbidden = "ADMIN_FORBIDDEN";
}
