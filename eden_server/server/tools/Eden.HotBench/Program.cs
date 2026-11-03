// DB-4 — micro-benchmark des opérations chaudes à ~300 CCU simulés :
// PostgreSQL direct (état avant DB-3) vs Redis (DB-3), mêmes workers.
// Chaque « joueur » (worker) enchaîne des opérations ; on mesure la latence
// client de chaque appel et on rapporte p50/p95/p99/max + débit.
//
// Usage : eden-hotbench --db="<conn Npgsql, SearchPath=game,public>" --redis=localhost:6380,defaultDatabase=15
//                       [--players=300] [--ops=200] [--first-char=800000001] [--json=out.json]
// Pré-requis : base clonée avec les personnages botchar* (voir tools/load-test.sh).
using System.Diagnostics;
using System.Text.Json;
using Eden.Core.Database.Repositories;
using Eden.Core.Hot;
using Npgsql;

var o = args.Select(a => a.TrimStart('-').Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
var players = int.Parse(o.GetValueOrDefault("players", "300"));
var ops = int.Parse(o.GetValueOrDefault("ops", "200"));
var first = int.Parse(o.GetValueOrDefault("first-char", "800000001"));
var db = NpgsqlDataSource.Create(o["db"] + ";Maximum Pool Size=100");
var mux = HotRedis.Connect(o["redis"]);
var prefix = $"bench:{Guid.NewGuid():N}:";

var pgRepo = new CharacterRepository(db);
var hotRepo = new CharacterRepository(db, new HotCharacterStore(mux, prefix));
var pgTickets = new TicketRepository(db);
var redisTickets = new RedisTicketStore(mux, prefix);

var results = new List<object>();

async Task<Stats> Run(string name, Func<int, int, Task> op)
{
    var lat = new double[players][];
    var sw = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, players).Select(async p =>
    {
        await Task.Yield();
        var l = new double[ops];
        for (var i = 0; i < ops; i++)
        {
            var t = Stopwatch.GetTimestamp();
            await op(first + p, i);
            l[i] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        }
        lat[p] = l;
    }));
    sw.Stop();
    var all = lat.SelectMany(x => x).OrderBy(x => x).ToArray();
    double P(double q) => all[Math.Min(all.Length - 1, (int)(q * all.Length))];
    var s = new Stats(name, all.Length, all.Length / sw.Elapsed.TotalSeconds, P(.5), P(.95), P(.99), all[^1]);
    Console.WriteLine($"{name,-34} n={s.N,6}  {s.OpsPerSec,9:N0} op/s  p50={s.P50:F2}  p95={s.P95:F2}  p99={s.P99:F2}  max={s.Max:F1} ms");
    results.Add(s);
    return s;
}

Console.WriteLine($"== {players} joueurs × {ops} ops (latence client, ms)");
await Run("or  AddGold      PG direct", (c, i) => pgRepo.AddGoldAsync(c, 1));
await Run("or  AddGold      Redis", (c, i) => hotRepo.AddGoldAsync(c, 1));
await Run("pos UpdateCoords PG direct", (c, i) => pgRepo.UpdateCoordinatesAsync(c, 5, i, i, 0, 1));
await Run("pos UpdateCoords Redis (F&F)", (c, i) => hotRepo.UpdateCoordinatesAsync(c, 5, i, i, 0, 1));
await Run("ticket login Issue+Consume PG", async (c, i) =>
{
    var k = (uint)(c * 100_000 + i);
    await pgTickets.IssueLoginAsync(k, 1, "test123", 0, 0, 0);
    await pgTickets.ConsumeLoginByKeyAsync(k);
});
await Run("ticket login Issue+Consume Redis", async (c, i) =>
{
    var k = (uint)(c * 100_000 + i);
    await redisTickets.IssueLoginAsync(k, 1, "test123", 0, 0, 0);
    await redisTickets.ConsumeLoginByKeyAsync(k);
});

// Flush Redis→PG : tout ce que les runs Redis ci-dessus ont sali (300 persos).
var swf = Stopwatch.StartNew();
var flushed = await hotRepo.FlushAllHotAsync();
swf.Stop();
Console.WriteLine($"flush Redis→PG de {flushed} persos sales : {swf.Elapsed.TotalMilliseconds:F0} ms " +
                  $"({swf.Elapsed.TotalMilliseconds / Math.Max(1, flushed):F2} ms/perso)");
results.Add(new { name = "flush all dirty", chars = flushed, ms = swf.Elapsed.TotalMilliseconds });

if (o.TryGetValue("json", out var jp) && jp.Length > 0)
    File.WriteAllText(jp, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
return 0;

record Stats(string Name, int N, double OpsPerSec, double P50, double P95, double P99, double Max);
