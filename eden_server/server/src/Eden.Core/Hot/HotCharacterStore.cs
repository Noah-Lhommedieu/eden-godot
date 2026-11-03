using System.Globalization;
using StackExchange.Redis;

namespace Eden.Core.Hot;

/// <summary>Position persistable d'un perso (node + coordonnées + cap).</summary>
public readonly record struct HotPosition(int NodeId, double X, double Y, double Z, double Dir);

/// <summary>Vitals persistables (hp/mp courants).</summary>
public readonly record struct HotVitals(int Hp, int Mp);

/// <summary>Contenu extrait de Redis pour un flush vers PostgreSQL.</summary>
public sealed record HotSnapshot(long GoldDelta, HotPosition? Position, HotVitals? Vitals)
{
    /// <summary>Rien à écrire.</summary>
    public bool IsEmpty => GoldDelta == 0 && Position is null && Vitals is null;
}

/// <summary>
/// Mutations chaudes d'un personnage en RAM Redis (DB-3) — liste BORNÉE :
/// <b>or</b>, <b>position</b> (node/x/y/z/dir) et <b>vitals</b> (hp/mp).
/// Tout le reste (inventaire, quêtes, exp, level…) reste PostgreSQL direct.
/// <para><b>Or = base + delta.</b> <c>gold_base</c> est la dernière valeur
/// PG connue, <c>gold_delta</c> les mouvements non flushés. Les
/// écrivains PG directs restants (courrier, échange, enchères, Mission)
/// font <c>gold = gold + d</c> : les deltas commutent, donc le flush
/// (<c>gold = clamp(gold + delta)</c>) reste exact ; le flush resynchronise
/// <c>gold_base</c> sur le gold PG renvoyé, bornant l'obsolescence des
/// lectures à une période de flush (~15 s).</para>
/// <para><b>Position/vitals</b> : dernier état gagne, drapeau dirty ; un
/// seul écrivain (la zone qui possède le perso).</para>
/// <para>Écritures de position/vitals en <c>FireAndForget</c> (non
/// bloquantes pour la boucle de zone) ; l'or est attendu (retour du solde).
/// Le flush extrait atomiquement (<see cref="TakeAsync"/>) puis, si PG
/// échoue, réinjecte (<see cref="RestoreAsync"/>) — rien n'est perdu.</para>
/// </summary>
public sealed class HotCharacterStore
{
    /// <summary>Plafond d'or natif (<c>CCharacter::GainGold</c>).</summary>
    public const long MaxGold = 999_999_999;

    private const string GoldLua = @"
        local b = tonumber(redis.call('HGET', KEYS[1], 'gold_base'))
        if not b then return redis.error_reply('NOBASE') end
        local d = tonumber(redis.call('HGET', KEYS[1], 'gold_delta') or '0')
        local eff = b + d
        local target
        if ARGV[1] == 'add' then target = eff + tonumber(ARGV[2]) else target = tonumber(ARGV[2]) end
        if target < 0 then target = 0 end
        if target > tonumber(ARGV[4]) then target = tonumber(ARGV[4]) end
        local applied = target - eff
        if applied ~= 0 then
            redis.call('HINCRBY', KEYS[1], 'gold_delta', applied)
            redis.call('SADD', KEYS[2], ARGV[3])
            redis.call('PERSIST', KEYS[1])
        end
        return target";

    // ARGV[1]=champ (pos|vit) ARGV[2]=valeur packée ARGV[3]=id
    private const string MarkLua = @"
        redis.call('HSET', KEYS[1], ARGV[1], ARGV[2], ARGV[1] .. '_dirty', '1')
        redis.call('SADD', KEYS[2], ARGV[3])
        redis.call('PERSIST', KEYS[1])
        return 1";

    // Extrait delta + pos/vit sales. base += delta pour conserver la valeur effective pendant l'écriture PG.
    private const string TakeLua = @"
        local k = KEYS[1]
        local d = tonumber(redis.call('HGET', k, 'gold_delta') or '0')
        local out = { tostring(d), '', '' }
        if d ~= 0 then
            redis.call('HINCRBY', k, 'gold_delta', -d)
            redis.call('HINCRBY', k, 'gold_base', d)
        end
        if redis.call('HGET', k, 'pos_dirty') == '1' then
            out[2] = redis.call('HGET', k, 'pos')
            redis.call('HDEL', k, 'pos_dirty')
        end
        if redis.call('HGET', k, 'vit_dirty') == '1' then
            out[3] = redis.call('HGET', k, 'vit')
            redis.call('HDEL', k, 'vit_dirty')
        end
        redis.call('SREM', KEYS[2], ARGV[1])
        return out";

    // Après PG OK : base := goldPg - delta courant ; clé expirable si plus rien de sale.
    private const string AckLua = @"
        local k = KEYS[1]
        local d = tonumber(redis.call('HGET', k, 'gold_delta') or '0')
        if ARGV[1] ~= '' then redis.call('HSET', k, 'gold_base', tonumber(ARGV[1]) - d) end
        if d == 0 and redis.call('HGET', k, 'pos_dirty') ~= '1' and redis.call('HGET', k, 'vit_dirty') ~= '1' then
            redis.call('EXPIRE', k, 600)
        end
        return 1";

    // Échec PG : remet delta (et base) ; pos/vit seulement si rien de plus récent n'est arrivé.
    private const string RestoreLua = @"
        local k = KEYS[1]
        local d = tonumber(ARGV[1])
        if d ~= 0 then
            redis.call('HINCRBY', k, 'gold_delta', d)
            redis.call('HINCRBY', k, 'gold_base', -d)
        end
        if ARGV[2] ~= '' and redis.call('HGET', k, 'pos_dirty') ~= '1' then
            redis.call('HSET', k, 'pos', ARGV[2], 'pos_dirty', '1')
        end
        if ARGV[3] ~= '' and redis.call('HGET', k, 'vit_dirty') ~= '1' then
            redis.call('HSET', k, 'vit', ARGV[3], 'vit_dirty', '1')
        end
        redis.call('SADD', KEYS[2], ARGV[4])
        redis.call('PERSIST', k)
        return 1";

    // Libération du lease seulement par son propriétaire.
    private const string ReleaseLua = @"
        if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end
        return 0";

    private readonly IDatabase _db;
    private readonly string _prefix;

    public HotCharacterStore(IConnectionMultiplexer mux, string prefix = "eden:")
    {
        _db = mux.GetDatabase();
        _prefix = prefix;
    }

    private RedisKey Key(int id) => $"{_prefix}hot:c:{id}";
    private RedisKey DirtySet => $"{_prefix}hot:dirty";

    // ---------------------------------------------------------------- or

    /// <summary>Or effectif (base + delta). <paramref name="loadFromDb"/> n'est appelé qu'au premier accès.</summary>
    public async Task<long> GetGoldAsync(int id, Func<Task<long>> loadFromDb)
    {
        await EnsureBaseAsync(id, loadFromDb);
        var f = await _db.HashGetAsync(Key(id), new RedisValue[] { "gold_base", "gold_delta" });
        return (long)f[0] + (long)f[1];
    }

    /// <summary>Delta atomique borné [0, <see cref="MaxGold"/>] — retourne le nouveau total.</summary>
    public Task<long> AddGoldAsync(int id, long amount, Func<Task<long>> loadFromDb)
        => GoldOpAsync(id, "add", amount, loadFromDb);

    /// <summary>Fixe le total d'or (converti en delta atomique) — retourne le total appliqué.</summary>
    public Task<long> SetGoldAsync(int id, long gold, Func<Task<long>> loadFromDb)
        => GoldOpAsync(id, "set", gold, loadFromDb);

    private async Task<long> GoldOpAsync(int id, string mode, long v, Func<Task<long>> load)
    {
        for (var attempt = 0; ; attempt++)
        {
            await EnsureBaseAsync(id, load);
            try
            {
                var r = await _db.ScriptEvaluateAsync(GoldLua,
                    new[] { Key(id), DirtySet },
                    new RedisValue[] { mode, v, id, MaxGold });
                return (long)r;
            }
            catch (RedisServerException e) when (attempt == 0 && e.Message.Contains("NOBASE"))
            { /* clé expirée entre-temps : recharge la base et réessaie une fois */ }
        }
    }

    private async Task EnsureBaseAsync(int id, Func<Task<long>> load)
    {
        if (await _db.HashExistsAsync(Key(id), "gold_base")) return;
        var pg = await load();
        // HSETNX : si deux appelants chargent en même temps, le premier gagne.
        await _db.HashSetAsync(Key(id), "gold_base", pg, When.NotExists);
        await _db.HashSetAsync(Key(id), "gold_delta", 0, When.NotExists);
    }

    // ------------------------------------------------- position / vitals

    /// <summary>Position sale — non bloquant (FireAndForget).</summary>
    public void SetPosition(int id, HotPosition p)
        => Mark(id, "pos", string.Create(CultureInfo.InvariantCulture,
            $"{p.NodeId}|{p.X:R}|{p.Y:R}|{p.Z:R}|{p.Dir:R}"));

    /// <summary>Vitals sales — non bloquant (FireAndForget).</summary>
    public void SetVitals(int id, HotVitals v)
        => Mark(id, "vit", $"{v.Hp}|{v.Mp}");

    private void Mark(int id, string field, string packed)
        => _ = _db.ScriptEvaluateAsync(MarkLua, new[] { Key(id), DirtySet },
            new RedisValue[] { field, packed, id }, CommandFlags.FireAndForget);

    /// <summary>Oublie position/vitals sales — après une sauvegarde PG directe plus récente.</summary>
    public Task DropPositionAndVitalsAsync(int id)
        => _db.HashDeleteAsync(Key(id), new RedisValue[] { "pos_dirty", "vit_dirty" });

    // ------------------------------------------------------------- flush

    /// <summary>Ids ayant des champs sales.</summary>
    public async Task<int[]> DirtyIdsAsync()
        => (await _db.SetMembersAsync(DirtySet)).Select(v => (int)v).ToArray();

    /// <summary>Extrait atomiquement tout ce qui est sale pour <paramref name="id"/>.</summary>
    public async Task<HotSnapshot> TakeAsync(int id)
    {
        var r = (RedisResult[])(await _db.ScriptEvaluateAsync(TakeLua,
            new[] { Key(id), DirtySet }, new RedisValue[] { id }))!;
        var gold = long.Parse((string)r[0]!, CultureInfo.InvariantCulture);
        var pos = (string?)r[1];
        var vit = (string?)r[2];
        return new HotSnapshot(gold, ParsePos(pos), ParseVit(vit));
    }

    /// <summary>PG a accepté le flush : resynchronise la base sur <paramref name="goldFromPg"/> (null = pas d'écriture d'or).</summary>
    public Task AckAsync(int id, long? goldFromPg)
        => _db.ScriptEvaluateAsync(AckLua, new[] { Key(id) },
            new RedisValue[] { goldFromPg?.ToString(CultureInfo.InvariantCulture) ?? "" });

    /// <summary>PG a échoué : réinjecte le snapshot.</summary>
    public Task RestoreAsync(int id, HotSnapshot s)
        => _db.ScriptEvaluateAsync(RestoreLua, new[] { Key(id), DirtySet },
            new RedisValue[]
            {
                s.GoldDelta,
                s.Position is { } p ? PackPos(p) : "",
                s.Vitals is { } v ? $"{v.Hp}|{v.Mp}" : "",
                id,
            });

    private static string PackPos(HotPosition p) => string.Create(CultureInfo.InvariantCulture,
        $"{p.NodeId}|{p.X:R}|{p.Y:R}|{p.Z:R}|{p.Dir:R}");

    private static HotPosition? ParsePos(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var f = s.Split('|');
        var c = CultureInfo.InvariantCulture;
        return new HotPosition(int.Parse(f[0], c), double.Parse(f[1], c),
            double.Parse(f[2], c), double.Parse(f[3], c), double.Parse(f[4], c));
    }

    private static HotVitals? ParseVit(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var f = s.Split('|');
        return new HotVitals(int.Parse(f[0]), int.Parse(f[1]));
    }

    // ------------------------------------------------------------- lease

    /// <summary>
    /// Verrou/lease <c>SET NX PX</c> : un seul propriétaire à la fois pour
    /// <paramref name="name"/> (ex. <c>flush:42</c>, <c>session:42</c>).
    /// Retourne <c>true</c> si acquis ; à libérer avec <see cref="ReleaseAsync"/>.
    /// </summary>
    public Task<bool> TryAcquireAsync(string name, string owner, TimeSpan ttl)
        => _db.StringSetAsync($"{_prefix}lease:{name}", owner, ttl, When.NotExists);

    /// <summary>Libère le lease s'il appartient encore à <paramref name="owner"/>.</summary>
    public async Task<bool> ReleaseAsync(string name, string owner)
        => (long)await _db.ScriptEvaluateAsync(ReleaseLua,
            new RedisKey[] { $"{_prefix}lease:{name}" }, new RedisValue[] { owner }) == 1;
}
