using System.Text.Json;
using Eden.Bots;

BotOptions options;
try { options = BotOptions.Parse(args); }
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var entryFrames = options.NoEntry
    ? Array.Empty<byte[]>()
    : EntryReplay.Load(options.CapturePath);
var characters = await CharacterDirectory.ResolveAsync(
    options.DbConnection, options.NamePrefix, options.Sessions,
    CancellationToken.None);
if (characters.Count == 0)
{
    Console.Error.WriteLine(
        $"Aucun personnage '{options.NamePrefix}*' actif en base.");
    return 3;
}
if (characters.Count < options.Sessions)
    Console.Error.WriteLine(
        $"Prévenu: {characters.Count}/{options.Sessions} personnages trouvés");

Console.WriteLine($"eden-bots scenario={options.Scenario} " +
    $"transport={options.Transport} sessions={characters.Count} " +
    $"entryFrames={entryFrames.Count}");
var runStart = DateTimeOffset.UtcNow;
var tasks = new List<Task<BotResult>>();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

for (var i = 0; i < characters.Count; i++)
{
    var bot = new BotSession(options, characters[i], entryFrames);
    tasks.Add(bot.RunAsync(cts.Token));
    if (options.StaggerMs > 0 && i + 1 < characters.Count)
        await Task.Delay(options.StaggerMs, cts.Token);
}
var results = await Task.WhenAll(tasks);
var elapsed = DateTimeOffset.UtcNow - runStart;

var entered = results.Count(r => r.Entered);
var errors = results.Count(r => r.Error != null);
var summary = new
{
    scenario = options.Scenario,
    requestedSessions = options.Sessions,
    actualSessions = results.Length,
    entered,
    errors,
    durationSeconds = elapsed.TotalSeconds,
    totals = new
    {
        sent = results.Sum(r => r.Sent),
        received = results.Sum(r => r.Received),
        moves = results.Sum(r => r.Moves),
        attacks = results.Sum(r => r.Attacks),
        spells = results.Sum(r => r.Spells),
        lootAttempts = results.Sum(r => r.LootAttempts),
        mobsSeen = results.Sum(r => r.MobsSeen),
        treasuresSeen = results.Sum(r => r.TreasuresSeen),
    },
    entryMs = new
    {
        min = results.Where(r => r.EntryMs > 0).Select(r => r.EntryMs)
            .DefaultIfEmpty(0).Min(),
        avg = results.Where(r => r.EntryMs > 0).Select(r => r.EntryMs)
            .DefaultIfEmpty(0).Average(),
        max = results.Where(r => r.EntryMs > 0).Select(r => r.EntryMs)
            .DefaultIfEmpty(0).Max(),
    },
    inboundNcids = results
        .SelectMany(r => r.InboundNcids)
        .GroupBy(kv => kv.Key)
        .OrderByDescending(g => g.Sum(kv => kv.Value))
        .ToDictionary(g => g.Key.ToString(), g => g.Sum(kv => kv.Value)),
    failures = results.Where(r => r.Error != null)
        .Select(r => new { r.CharacterId, r.CharacterName, r.Error })
        .ToArray(),
    sessions = results.Select(r => new
    {
        r.CharacterId, r.CharacterName, r.Entered, r.Sent, r.Received,
        r.Moves, r.Attacks, r.Spells, r.LootAttempts, r.MobsSeen,
        r.TreasuresSeen, r.EntryMs, r.VersionRejected, r.Error,
    }).ToArray(),
};

var json = JsonSerializer.Serialize(summary,
    new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (options.JsonPath != null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(options.JsonPath)!);
    await File.WriteAllTextAsync(options.JsonPath, json);
}
return errors == 0 ? 0 : 4;
