using Eden.Core.Database.Repositories;
using StackExchange.Redis;

namespace Eden.Core.Hot;

/// <summary>
/// <see cref="ITicketRepository"/> adossé à Redis (DB-3) : remplace les
/// tables PostgreSQL <c>login_tickets</c> / <c>zone_tickets</c>.
/// <para>Un ticket = une clé à TTL natif (<c>SET … PX</c>, 5 min comme
/// l'ancien <c>issued_at &gt; NOW() - 5 minutes</c>) : plus de purge.
/// La consommation est atomique côté serveur (script Lua : contrôles
/// puis <c>DEL</c>) — deux consommateurs concurrents du même ticket ne
/// peuvent pas réussir tous les deux (usage unique). Un contrôle qui
/// échoue (nom, ip/port zone) ne détruit pas le ticket, comme l'ancien
/// <c>UPDATE … WHERE</c>.</para>
/// <para>Clés : <c>{prefix}tk:l:{key1}</c> = <c>accountId|accountName</c> ;
/// <c>{prefix}tk:z:{key1}:{key2}</c> = <c>charId|zoneIp|zonePort</c>.</para>
/// </summary>
public sealed class RedisTicketStore : ITicketRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    // KEYS[1]=clé ; ARGV[1]=nom attendu ('' = pas de contrôle). Retourne la valeur ou false.
    private const string ConsumeLoginLua = @"
        local v = redis.call('GET', KEYS[1])
        if not v then return false end
        if ARGV[1] ~= '' then
            local name = string.sub(v, string.find(v, '|', 1, true) + 1)
            if name ~= ARGV[1] then return false end
        end
        redis.call('DEL', KEYS[1])
        return v";

    // ARGV[1]=zoneIp ARGV[2]=zonePort ; ticket valide si zone_ip/port stockés == 0 ou égaux.
    private const string ConsumeZoneLua = @"
        local v = redis.call('GET', KEYS[1])
        if not v then return false end
        local c, ip, port = string.match(v, '^(%-?%d+)|(%d+)|(%d+)$')
        if ip ~= '0' and ip ~= ARGV[1] then return false end
        if port ~= '0' and port ~= ARGV[2] then return false end
        redis.call('DEL', KEYS[1])
        return c";

    private readonly IDatabase _db;
    private readonly string _prefix;

    public RedisTicketStore(IConnectionMultiplexer mux, string prefix = "eden:")
    {
        _db = mux.GetDatabase();
        _prefix = prefix;
    }

    private RedisKey LoginKey(uint key1) => $"{_prefix}tk:l:{key1}";
    private RedisKey ZoneKey(uint key1, uint key2) => $"{_prefix}tk:z:{key1}:{key2}";

    public async Task IssueAsync(uint key1, uint key2, int characterId,
        uint clientIp, uint zoneIp, ushort zonePort)
        => await _db.StringSetAsync(ZoneKey(key1, key2),
            $"{characterId}|{zoneIp}|{zonePort}", Ttl);

    public async Task<int?> ConsumeAsync(uint key1, uint key2, uint zoneIp, ushort zonePort)
    {
        var r = await _db.ScriptEvaluateAsync(ConsumeZoneLua,
            new[] { ZoneKey(key1, key2) },
            new RedisValue[] { zoneIp.ToString(), ((int)zonePort).ToString() });
        return r.IsNull ? null : int.Parse((string)r!);
    }

    /// <summary>Le TTL natif expire les tickets : rien à purger.</summary>
    public Task<int> PurgeExpiredAsync() => Task.FromResult(0);

    public async Task IssueLoginAsync(uint key1, int accountId, string accountName,
        uint clientIp, uint worldIp, ushort worldPort)
        => await _db.StringSetAsync(LoginKey(key1), $"{accountId}|{accountName}", Ttl);

    public async Task<(int accountId, string accountName)?> ConsumeLoginAsync(
        uint key1, string accountName)
        => await ConsumeLoginCoreAsync(key1, accountName);

    public async Task<(int accountId, string accountName)?> ConsumeLoginByKeyAsync(uint key1)
        => await ConsumeLoginCoreAsync(key1, "");

    private async Task<(int, string)?> ConsumeLoginCoreAsync(uint key1, string name)
    {
        var r = await _db.ScriptEvaluateAsync(ConsumeLoginLua,
            new[] { LoginKey(key1) }, new RedisValue[] { name });
        if (r.IsNull) return null;
        var v = (string)r!;
        var i = v.IndexOf('|');
        return (int.Parse(v[..i]), v[(i + 1)..]);
    }
}
