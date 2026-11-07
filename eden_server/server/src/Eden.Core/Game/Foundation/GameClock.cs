namespace Eden.Core.Game.Foundation;

/// <summary>
/// Horloge de jeu injectable. Le jeu connaît DEUX notions de « jour » :
/// <list type="bullet">
/// <item><b>jour de reset</b> : le jour de jeu bascule à <see cref="GameClockOptions.ResetHour"/>
/// (06:00 par défaut) heure serveur — entrées de donjon, quêtes journalières,
/// éphéméride, tout compteur « par jour » ;</item>
/// <item><b>jour réel</b> : durée écoulée IRL (24 h), indépendante du reset —
/// VIP, échéance des contrats PNJ, objets à durée.</item>
/// </list>
/// Aucun code de jeu n'appelle <c>DateTime.Now</c> : il reçoit un
/// <see cref="IGameClock"/> (voir le test d'architecture).
/// </summary>
public interface IGameClock
{
    /// <summary>Instant courant.</summary>
    DateTimeOffset Now { get; }

    /// <summary>Paramètres de bascule (heure de reset, fuseau).</summary>
    GameClockOptions Options { get; }
}

/// <summary>Heure de reset (0-23) et fuseau du serveur.</summary>
public sealed record GameClockOptions(int ResetHour, TimeZoneInfo Zone)
{
    /// <summary>06:00 heure locale du serveur.</summary>
    public static GameClockOptions Default { get; } =
        new(6, TimeZoneInfo.Local);
}

/// <summary>Horloge de production : le seul endroit autorisé à lire
/// l'heure système.</summary>
public sealed class SystemGameClock : IGameClock
{
    public SystemGameClock(GameClockOptions? options = null) =>
        Options = options ?? GameClockOptions.Default;

    public DateTimeOffset Now => DateTimeOffset.UtcNow;
    public GameClockOptions Options { get; }
}

/// <summary>Horloge de test : le temps n'avance que sur demande.</summary>
public sealed class FakeGameClock : IGameClock
{
    public FakeGameClock(DateTimeOffset start, GameClockOptions? options = null)
    {
        Now = start;
        Options = options ?? new GameClockOptions(6, TimeZoneInfo.Utc);
    }

    public DateTimeOffset Now { get; private set; }
    public GameClockOptions Options { get; }

    public void Advance(TimeSpan by) => Now += by;
    public void Set(DateTimeOffset to) => Now = to;
}

/// <summary>Calculs purs sur les deux systèmes de jours.</summary>
public static class GameClockExtensions
{
    private static readonly DateTime Epoch =
        new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Numéro du jour de reset contenant <paramref name="t"/>
    /// (jours depuis 1970, bascule à l'heure de reset locale).</summary>
    public static int ResetDay(this IGameClock c, DateTimeOffset t)
    {
        var local = TimeZoneInfo.ConvertTime(t, c.Options.Zone).DateTime;
        var shifted = local.AddHours(-c.Options.ResetHour);
        return (int)Math.Floor((shifted - Epoch).TotalDays);
    }

    /// <summary>Jour de reset courant.</summary>
    public static int ResetDayNow(this IGameClock c) => c.ResetDay(c.Now);

    /// <summary>Prochain instant de reset strictement après <paramref name="t"/>.</summary>
    public static DateTimeOffset NextReset(this IGameClock c, DateTimeOffset t)
    {
        var zone = c.Options.Zone;
        var local = TimeZoneInfo.ConvertTime(t, zone).DateTime;
        var candidate = local.Date.AddHours(c.Options.ResetHour);
        if (candidate <= local) candidate = candidate.AddDays(1);
        return ToOffset(candidate, zone);
    }

    /// <summary>Prochain reset après l'instant courant.</summary>
    public static DateTimeOffset NextResetNow(this IGameClock c) =>
        c.NextReset(c.Now);

    /// <summary>Jours RÉELS (24 h) entiers écoulés entre deux instants.</summary>
    public static int RealDaysBetween(DateTimeOffset from, DateTimeOffset to) =>
        (int)Math.Floor((to - from).TotalDays);

    private static DateTimeOffset ToOffset(DateTime unspecifiedLocal,
        TimeZoneInfo zone)
    {
        // Heure inexistante (saut d'été) : avancer jusqu'à la première valide.
        while (zone.IsInvalidTime(unspecifiedLocal))
            unspecifiedLocal = unspecifiedLocal.AddMinutes(1);
        return new DateTimeOffset(unspecifiedLocal,
            zone.GetUtcOffset(unspecifiedLocal));
    }
}
