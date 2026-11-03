using Eden.Core.Database.Repositories;
using Eden.Core.Logging;
using Npgsql;

namespace Eden.Core.Hot;

/// <summary>
/// Choix PG vs Redis au démarrage des services (DB-3). Avec
/// <c>EDEN_REDIS_CONN</c> les tickets et les mutations chaudes passent par
/// Redis ; sans, comportement 100 % PostgreSQL d'origine (rollback).
/// </summary>
public static class HotWiring
{
    /// <summary>Registre de tickets : Redis si configuré, sinon table PG.</summary>
    public static ITicketRepository CreateTicketRepository(string service, NpgsqlDataSource gameDb)
    {
        if (HotRedis.Instance is { } mux)
        {
            EdenLog.Write($"[{service}] Tickets : Redis ({EdenConfig.RedisConn})");
            return new RedisTicketStore(mux);
        }
        EdenLog.Write($"[{service}] Tickets : PostgreSQL (Redis désactivé)");
        return new TicketRepository(gameDb);
    }

    /// <summary>Store des mutations chaudes, ou <c>null</c> sans Redis.</summary>
    public static HotCharacterStore? CreateHotStore(string service)
    {
        if (HotRedis.Instance is not { } mux) return null;
        EdenLog.Write($"[{service}] Mutations chaudes (or/position/vitals) : Redis → PG ~{EdenConfig.PersistFlushMs} ms");
        return new HotCharacterStore(mux);
    }
}
