using Dapper;
using Eden.Core.Database;
using Eden.Core.Database.Repositories;
using Eden.Core.Hot;
using Npgsql;
using StackExchange.Redis;

namespace Eden.Tests;

/// <summary>DB-3 : tickets Redis (usage unique, TTL, contrôles) et store des
/// mutations chaudes (or delta, position, vitals, flush Redis→PG). Les tests
/// utilisent un préfixe de clés aléatoire et s'auto-skippent sans Redis (:6380).</summary>
public class HotRedisTests
{
    private static IConnectionMultiplexer? Mux()
    {
        try
        {
            var m = HotRedis.Connect(Environment.GetEnvironmentVariable("EDEN_TEST_REDIS") ?? "localhost:6380");
            return m.IsConnected ? m : null;
        }
        catch (RedisConnectionException) { return null; }
    }

    private static string Prefix() => $"test:{Guid.NewGuid():N}:";

    [Fact]
    public async Task ZoneTicket_IsSingleUse_AndChecksZoneAddress()
    {
        if (Mux() is not { } mux) return;
        var t = new RedisTicketStore(mux, Prefix());
        await t.IssueAsync(11, 22, 777, 1, zoneIp: 100, zonePort: 5201);
        // Mauvaise adresse : refusé SANS détruire le ticket.
        Assert.Null(await t.ConsumeAsync(11, 22, 999, 5201));
        Assert.Equal(777, await t.ConsumeAsync(11, 22, 100, 5201));
        Assert.Null(await t.ConsumeAsync(11, 22, 100, 5201)); // usage unique
    }

    [Fact]
    public async Task ZoneTicket_WildcardAddress_AcceptsAny()
    {
        if (Mux() is not { } mux) return;
        var t = new RedisTicketStore(mux, Prefix());
        await t.IssueAsync(1, 2, 5, 0, 0, 0);
        Assert.Equal(5, await t.ConsumeAsync(1, 2, 123, 4567));
    }

    [Fact]
    public async Task LoginTicket_NameCheck_AndAtomicSingleConsumer()
    {
        if (Mux() is not { } mux) return;
        var t = new RedisTicketStore(mux, Prefix());
        await t.IssueLoginAsync(42, 9, "bob|x", 0, 0, 0);
        Assert.Null(await t.ConsumeLoginAsync(42, "alice"));
        // 20 consommateurs concurrents : exactement un gagnant.
        var wins = (await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => t.ConsumeLoginByKeyAsync(42)))).Count(r => r is not null);
        Assert.Equal(1, wins);
        await t.IssueLoginAsync(43, 9, "bob|x", 0, 0, 0);
        Assert.Equal((9, "bob|x"), await t.ConsumeLoginAsync(43, "bob|x"));
    }

    [Fact]
    public async Task Ticket_ExpiresViaNativeTtl()
    {
        if (Mux() is not { } mux) return;
        var p = Prefix();
        await new RedisTicketStore(mux, p).IssueLoginAsync(7, 1, "a", 0, 0, 0);
        var ttl = await mux.GetDatabase().KeyTimeToLiveAsync($"{p}tk:l:7");
        Assert.InRange(ttl!.Value.TotalSeconds, 250, 300);
    }

    [Fact]
    public async Task Gold_DeltaIsAtomicAndClamped()
    {
        if (Mux() is not { } mux) return;
        var h = new HotCharacterStore(mux, Prefix());
        Task<long> Load() => Task.FromResult(1000L);
        Assert.Equal(1000, await h.GetGoldAsync(1, Load));
        // 50 ajouts concurrents de +10 → aucun perdu.
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => h.AddGoldAsync(1, 10, Load)));
        Assert.Equal(1500, await h.GetGoldAsync(1, Load));
        Assert.Equal(0, await h.AddGoldAsync(1, -99999, Load));                    // plancher 0
        Assert.Equal(HotCharacterStore.MaxGold, await h.SetGoldAsync(1, long.MaxValue / 4, Load)); // plafond
    }

    [Fact]
    public async Task Take_ThenRestore_LosesNothing()
    {
        if (Mux() is not { } mux) return;
        var h = new HotCharacterStore(mux, Prefix());
        Task<long> Load() => Task.FromResult(100L);
        await h.AddGoldAsync(5, 25, Load);
        h.SetPosition(5, new(3, 1.5, 2.5, 0, 0.25));
        h.SetVitals(5, new(80, 40));
        await Task.Delay(100); // FireAndForget
        Assert.Contains(5, await h.DirtyIdsAsync());
        var snap = await h.TakeAsync(5);
        Assert.Equal(25, snap.GoldDelta);
        Assert.Equal(new HotPosition(3, 1.5, 2.5, 0, 0.25), snap.Position);
        Assert.Equal(new HotVitals(80, 40), snap.Vitals);
        Assert.True((await h.TakeAsync(5)).IsEmpty);              // vidé
        Assert.Equal(125, await h.GetGoldAsync(5, Load));          // valeur effective inchangée
        await h.RestoreAsync(5, snap);                             // échec PG simulé
        var again = await h.TakeAsync(5);
        Assert.Equal(25, again.GoldDelta);
        Assert.Equal(snap.Position, again.Position);
    }

    [Fact]
    public async Task Lease_NxPx_SingleOwner()
    {
        if (Mux() is not { } mux) return;
        var h = new HotCharacterStore(mux, Prefix());
        Assert.True(await h.TryAcquireAsync("session:1", "zoneA", TimeSpan.FromSeconds(5)));
        Assert.False(await h.TryAcquireAsync("session:1", "zoneB", TimeSpan.FromSeconds(5)));
        Assert.False(await h.ReleaseAsync("session:1", "zoneB"));  // pas le propriétaire
        Assert.True(await h.ReleaseAsync("session:1", "zoneA"));
        Assert.True(await h.TryAcquireAsync("session:1", "zoneB", TimeSpan.FromSeconds(5)));
    }

    private const int TestChar = 987_001;

    [Fact]
    public async Task CharacterRepository_FlushesRedisToPg()
    {
        if (Mux() is not { } mux) return;
        var pg = NpgsqlDataSource.Create(TestDb.ConnStr("edenfr_db"));
        try { await using var probe = await pg.OpenConnectionAsync(); }
        catch (NpgsqlException) { return; }
        await using (var c = await pg.OpenConnectionAsync())
        {
            await c.ExecuteAsync("DELETE FROM player_characters WHERE id=@i", new { i = TestChar });
            await c.ExecuteAsync("INSERT INTO player_characters (id, account_id, given_name, gold, x, y, z, node_id, hp, mp) " +
                "VALUES (@i, 1, 'hot_test', 500, 0, 0, 0, 1, 10, 10)", new { i = TestChar });
        }
        try
        {
            var repo = new CharacterRepository(pg, new HotCharacterStore(mux, Prefix()));
            Assert.Equal(500, await repo.GetGoldAsync(TestChar));
            Assert.Equal(560, await repo.AddGoldAsync(TestChar, 60));
            // Écrivain PG direct concurrent (courrier/échange) : le delta commute.
            await using (var c = await pg.OpenConnectionAsync())
                await c.ExecuteAsync("UPDATE player_characters SET gold = gold + 40 WHERE id=@i", new { i = TestChar });
            await repo.UpdateCharacterLocationAndStatsAsync(TestChar, 7, 11.5, 22.5, 0, 1.0, 33, 44);
            await Task.Delay(100);

            // Avant flush : PG n'a que l'écriture directe.
            await using (var c = await pg.OpenConnectionAsync())
                Assert.Equal(540, await c.ExecuteScalarAsync<long>("SELECT gold FROM player_characters WHERE id=@i", new { i = TestChar }));

            await repo.FlushHotAsync(TestChar);
            await using (var c = await pg.OpenConnectionAsync())
            {
                var r = await c.QuerySingleAsync("SELECT gold::bigint g, node_id n, x, hp, mp FROM player_characters WHERE id=@i", new { i = TestChar });
                Assert.Equal(600L, (long)r.g);      // 500 + 40 (direct) + 60 (delta flushé)
                Assert.Equal(7, (int)r.n);
                Assert.Equal(33, (int)r.hp);
                Assert.Equal(44, (int)r.mp);
            }
            Assert.Equal(600, await repo.GetGoldAsync(TestChar)); // base resynchronisée sur PG
            Assert.Equal(0, await repo.FlushAllHotAsync() is var n && n >= 0 ? 0 : 1);
        }
        finally
        {
            await using var c = await pg.OpenConnectionAsync();
            await c.ExecuteAsync("DELETE FROM player_characters WHERE id=@i", new { i = TestChar });
        }
    }
}
