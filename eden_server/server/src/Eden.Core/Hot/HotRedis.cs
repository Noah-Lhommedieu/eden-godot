using StackExchange.Redis;

namespace Eden.Core.Hot;

/// <summary>
/// Connexion Redis partagée du process (DB-3). Un seul
/// <see cref="ConnectionMultiplexer"/> par process, créé à la demande à
/// partir de <see cref="EdenConfig.RedisConn"/>. <c>AbortOnConnectFail=false</c> :
/// un Redis tardif ne bloque pas le démarrage, StackExchange reconnecte seul.
/// </summary>
public static class HotRedis
{
    private static readonly Lazy<IConnectionMultiplexer?> Shared = new(() =>
        EdenConfig.RedisConn is { } conn ? Connect(conn) : null);

    /// <summary>Multiplexeur du process, ou <c>null</c> si Redis est désactivé.</summary>
    public static IConnectionMultiplexer? Instance => Shared.Value;

    /// <summary>Ouvre un multiplexeur vers <paramref name="conn"/> (tests, outils).</summary>
    public static IConnectionMultiplexer Connect(string conn)
    {
        var o = ConfigurationOptions.Parse(conn);
        o.AbortOnConnectFail = false;
        o.ConnectTimeout = 2000;
        return ConnectionMultiplexer.Connect(o);
    }
}
