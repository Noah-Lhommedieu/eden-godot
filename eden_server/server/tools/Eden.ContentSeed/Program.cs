// Remplit eden_content (PLAN-ACTION-3 B0) depuis les tables du jeu : idempotent (TRUNCATE + INSERT dans une transaction),
// avec une empreinte SHA-256 par table dans history.seed_run. Les noms FR viennent des t_*.ini du corpus EDENFR (cp1252).
// Usage : dotnet run --project tools/Eden.ContentSeed -- <data/db serveur> <scene> <corpus fr-full data/db> "<connexion eden_content>"
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Eden.Core.Data;
using Eden.Core.Game.Content;
using Eden.Core.Game.Maps;
using Eden.Core.Game.Services;
using Npgsql;

if (args.Length < 4) { Console.Error.WriteLine("usage : <data/db> <scene> <corpus db fr> <connexion>"); return 2; }
var gd = new GameDataService(args[0]); gd.LoadAll();
var scenes = new SceneService(MonsterService.LoadFromDataDir(args[0])); scenes.LoadScenes(args[1]);
var scope = ContentScopeBuilder.Build(gd, scenes);

Dictionary<int, string> Names(string file)
{
    var text = Encoding.GetEncoding(1252).GetString(File.ReadAllBytes(Path.Combine(args[2], file))).Replace("\r", "");
    var lines = text.Split('\n').Skip(2);                       // 2 lignes d'en-tête (comptes de colonnes)
    var res = new Dictionary<int, string>();
    foreach (var rec in Regex.Split(string.Join('\n', lines), @"(?m)^(?=\d+\|)"))
    {
        var m = Regex.Match(rec, @"^(\d+)\|([^|]*)\|");
        if (m.Success) res[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value.Replace('\n', ' ').Trim();
    }
    return res;
}
var tItem = Names("t_item.ini"); var tMob = Names("t_monster.ini"); var tNpc = Names("t_npc.ini");
var tQuest = Names("t_mission.ini"); var tNode = Names("t_node.ini");
string Fr(Dictionary<int, string> d, int id, string fallback) => d.TryGetValue(id, out var v) && v.Length > 0 ? v : fallback;
// Périmètre = règle C3 ET nom FR utilisable (pas de placeholder ni de chinois résiduel).
HashSet<int> Scoped(IEnumerable<int> ids, Dictionary<int, string> fr, Func<int, string> fallback) =>
    ids.Where(i => !ContentScope.IsPlaceholderName(Fr(fr, i, fallback(i)))).ToHashSet();
var inMap = Scoped(scope.Maps, tNode, i => gd.Nodes.FirstOrDefault(n => n.Id == i)?.Name ?? "");
var inMob = Scoped(scope.Monsters, tMob, i => gd.Monsters.FirstOrDefault(m => m.Id == i)?.Name ?? "");
var inNpc = Scoped(scope.Npcs, tNpc, i => gd.Npcs.FirstOrDefault(p => p.Id == i)?.Name ?? "");
var inItem = Scoped(scope.Items, tItem, i => gd.Items.FirstOrDefault(x => x.Id == i)?.Name ?? "");
var inQuest = Scoped(scope.Quests, tQuest, i => gd.Missions.FirstOrDefault(q => q.Id == i)?.Name ?? "");

await using var ds = NpgsqlDataSource.Create(args[3]);
await using var conn = await ds.OpenConnectionAsync();
await using var tx = await conn.BeginTransactionAsync();
async Task Load(string table, string cols, IEnumerable<object?[]> rows, int sortKeys = 1)
{
    await using (var t = new NpgsqlCommand($"TRUNCATE {table}", conn, tx)) await t.ExecuteNonQueryAsync();
    var n = cols.Split(',').Length;
    var sql = $"INSERT INTO {table}({cols}) VALUES ({string.Join(',', Enumerable.Range(1, n).Select(i => "$" + i))})";
    var sb = new StringBuilder(); var count = 0;
    foreach (var r in SortRows(rows, sortKeys))
    {
        await using var c = new NpgsqlCommand(sql, conn, tx);
        foreach (var v in r) c.Parameters.Add(new NpgsqlParameter { Value = v ?? DBNull.Value });
        await c.ExecuteNonQueryAsync(); count++;
        sb.AppendJoin('|', r).Append('\n');
    }
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    await using var h = new NpgsqlCommand("INSERT INTO history.seed_run(table_name,row_count,sha256) VALUES ($1,$2,$3)", conn, tx)
    { Parameters = { new() { Value = table }, new() { Value = count }, new() { Value = hash } } };
    await h.ExecuteNonQueryAsync();
    Console.WriteLine($"{table}: {count} lignes, sha256 {hash[..12]}");
}

static IEnumerable<object?[]> SortRows(IEnumerable<object?[]> rows, int keys) =>
    keys == 1 ? rows.OrderBy(r => (int)r[0]!)
    : keys == 2 ? rows.OrderBy(r => (int)r[0]!).ThenBy(r => (int)r[1]!)
    : rows.OrderBy(r => (int)r[0]!).ThenBy(r => (string)r[2]!, StringComparer.Ordinal).ThenBy(r => (int)r[3]!);

await Load("item.item", "item_id,name_fr,item_kind,required_level,max_level,buy_price_silver,icon_code,in_scope",
    gd.Items.GroupBy(i => i.Id).Select(g => g.First()).Select(i => new object?[]
        { i.Id, Fr(tItem, i.Id, i.Name), i.Kind, i.RequiredLevel, i.MaxLevel, i.BuyPrice, i.IconCode, inItem.Contains(i.Id) }));
await Load("creature.monster", "monster_id,name_fr,level,monster_type,family,hp,mp,exp_reward,in_scope",
    gd.Monsters.GroupBy(m => m.Id).Select(g => g.First()).Select(m => new object?[]
        { m.Id, Fr(tMob, m.Id, m.Name), m.Level, m.MonsterType, m.Family, m.HP, m.MP, m.ExpReward, inMob.Contains(m.Id) }));
await Load("npc.npc", "npc_id,name_fr,npc_type,level,role,in_scope,store_id",
    gd.Npcs.GroupBy(p => p.Id).Select(g => g.First()).Select(p => new object?[]
        { p.Id, Fr(tNpc, p.Id, p.Name), p.NpcType, p.Level, p.Role ?? "", inNpc.Contains(p.Id), p.StoreId }));
await Load("quest.quest", "quest_id,name_fr,script_name,min_level,category,is_daily,in_scope",
    gd.Missions.GroupBy(q => q.Id).Select(g => g.First()).Select(q => new object?[]
        { q.Id, Fr(tQuest, q.Id, q.Name), q.ScriptName, ContentScope.MinLevelOf(q), q.Category ?? "", q.DailyType, inQuest.Contains(q.Id) }));
await Load("map.map", "node_id,name_fr,node_type,map_code,min_level,map_kind,in_scope",
    gd.Nodes.GroupBy(n => n.Id).Select(g => g.First()).Select(n =>
    {
        var respawns = scenes.GetScene(n.MapCode)?.Monsters.Select(m => m.RespawnSeconds).ToList() ?? new List<int>();
        var kind = MapClassifier.Classify(new MapFacts(n.Id, n.Type, Fr(tNode, n.Id, n.Name), respawns));
        return new object?[] { n.Id, Fr(tNode, n.Id, n.Name), n.Type, n.MapCode, n.MinLevel, Regex.Replace(kind.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToLowerInvariant(), inMap.Contains(n.Id) };
    }));
var itemName = gd.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => Fr(tItem, g.Key, g.First().Name));
string ItemFr(int id) => itemName.TryGetValue(id, out var v) ? v : "";
var kindOf = gd.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().Kind);
var recipes = gd.WorkCombineById.Values.GroupBy(r => r.Id).Select(g => g.First()).ToList();
await Load("craft.recipe", "recipe_id,category,building_level,duration_units,gold_silver,product_item_id,product_name_fr,product_quantity,success_percent,curse_percent,plan_item_id,plan_name_fr",
    recipes.Select(r =>
    {
        var plan = Eden.Core.Game.Work.CraftPlanRules.FindPlanItemId(r, id => kindOf.GetValueOrDefault(id));
        return new object?[] { r.Id, r.Category, r.BuildingLevel, r.Duration, r.Gold, r.ProductItemId, ItemFr(r.ProductItemId),
            r.ProductQuantity, r.SuccessRate, r.CurseRate, plan, plan == null ? null : ItemFr(plan.Value) };
    }));
await Load("craft.recipe_material", "recipe_id,position,item_id,item_name_fr,quantity",
    recipes.SelectMany(r => r.Materials.Select((m, i) => new object?[] { r.Id, i, m.ItemId, ItemFr(m.ItemId), m.Quantity })), 2);
var mobName = gd.Monsters.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => Fr(tMob, g.Key, g.First().Name));
await Load("creature.monster_drop", "monster_id,monster_name_fr,pool,position,item_id,item_name_fr,quantity,chance_percent",
    gd.DropTables.GroupBy(d => d.Id).Select(g => g.First()).SelectMany(d =>
        d.Entries.Select((e, i) => new object?[] { d.Id, mobName.GetValueOrDefault(d.Id, ""), "normal", i, e.ItemId, ItemFr(e.ItemId), e.Quantity, e.ChancePercent })
        .Concat(d.BreakEntries.Select((e, i) => new object?[] { d.Id, mobName.GetValueOrDefault(d.Id, ""), "break", i, e.ItemId, ItemFr(e.ItemId), e.Quantity, e.ChancePercent }))), 3);
await Load("npc.shop_item", "store_id,position,item_id,item_name_fr,stock_count",
    gd.Stores.GroupBy(st => st.Id).Select(g => g.First()).SelectMany(st =>
        st.Items.Select((it, i) => new object?[] { st.Id, i, it.ItemId, ItemFr(it.ItemId), it.Count })), 2);
IEnumerable<object?[]> QuestRewards(Eden.Core.Data.Parsers.MissionParser.MissionDef q)
{
    var pos = 0;
    foreach (var cmd in q.Rewards.Where(r => r.Name is "RewardItem" or "RewardChooseItem"))
        for (var i = 0; i + 1 < cmd.Args.Count + 1; i += 2)
        {
            if (!int.TryParse(cmd.Args[i], out var item) || item <= 0) break;
            var qty = i + 1 < cmd.Args.Count && int.TryParse(cmd.Args[i + 1], out var n) ? Math.Max(1, n) : 1;
            yield return new object?[] { q.Id, pos++, item, ItemFr(item), qty, cmd.Name == "RewardChooseItem" };
        }
}
await Load("quest.quest_reward", "quest_id,position,item_id,item_name_fr,quantity,is_choice",
    gd.Missions.GroupBy(q => q.Id).Select(g => g.First()).SelectMany(QuestRewards), 2);
await tx.CommitAsync();
// --- Traductions (lot E) : args[5] = translations.tsv, args[6] = glossary.tsv ; contrôles TranslationChecks, lignes refusées listées ---
if (args.Length > 6 && File.Exists(args[5]))
{
    var glossRows = File.ReadAllLines(args[6]).Skip(1).Select(l => l.Split('\t')).Where(p => p.Length >= 3).ToList();
    var strict = glossRows.Where(p => p[2] == "yes").ToDictionary(p => p[0], p => p[1]);
    var frOf = new Dictionary<(string, int), string>();
    foreach (var (t, sql) in new[] { ("item", "SELECT item_id, name_fr FROM item.item"), ("monster", "SELECT monster_id, name_fr FROM creature.monster"),
        ("npc", "SELECT npc_id, name_fr FROM npc.npc"), ("quest", "SELECT quest_id, name_fr FROM quest.quest"), ("map", "SELECT node_id, name_fr FROM map.map") })
    {
        await using var q = new NpgsqlCommand(sql, conn); await using var rd = await q.ExecuteReaderAsync();
        while (await rd.ReadAsync()) frOf[(t, rd.GetInt32(0))] = rd.GetString(1);
    }
    await using (var tx2 = await conn.BeginTransactionAsync())
    {
        await using (var c = new NpgsqlCommand("TRUNCATE text.glossary", conn, tx2)) await c.ExecuteNonQueryAsync();
        foreach (var g in glossRows)
            await using (var c = new NpgsqlCommand("INSERT INTO text.glossary(fr_term, lang, term, note) VALUES ($1,'en',$2,$3)", conn, tx2)
                { Parameters = { new() { Value = g[0] }, new() { Value = g[1] }, new() { Value = g.Length > 3 ? g[3] : "" } } })
                await c.ExecuteNonQueryAsync();
        int ok = 0, bad = 0;
        foreach (var line in File.ReadAllLines(args[5]).Skip(1).Where(l => l.Length > 0))
        {
            var p = line.Split('\t'); var (type, id, field, value, status) = (p[0], int.Parse(p[1]), p[2], p[3], p.Length > 4 ? p[4] : "auto");
            if (!frOf.TryGetValue((type, id), out var fr)) { bad++; Console.WriteLine($"  refusé {type} {id} : entité hors contenu"); continue; }
            var issues = TranslationChecks.Check(fr, value, 80, strict);
            if (issues.Count > 0) { bad++; Console.WriteLine($"  refusé {type} {id} « {fr} » → « {value} » : {string.Join(" ; ", issues.Select(i => i.Message))}"); continue; }
            await using var ins = new NpgsqlCommand(
                "INSERT INTO text.translation(entity_type, entity_id, field, lang, value, status, fr_hash) VALUES ($1,$2,$3,'en',$4,$5,$6) " +
                "ON CONFLICT (entity_type, entity_id, field, lang) DO UPDATE SET value = EXCLUDED.value, fr_hash = EXCLUDED.fr_hash, translated_at = now() " +
                "WHERE text.translation.status = 'auto'", conn, tx2)
            { Parameters = { new() { Value = type }, new() { Value = id }, new() { Value = field }, new() { Value = value }, new() { Value = status }, new() { Value = TranslationChecks.FrHash(fr) } } };
            await ins.ExecuteNonQueryAsync(); ok++;
        }
        await tx2.CommitAsync();
        Console.WriteLine($"traductions EN : {ok} importées, {bad} refusées");
    }
}
var scopePath = args.Length > 4 ? args[4] : null;
if (scopePath != null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(scopePath))!);
    File.WriteAllText(scopePath, System.Text.Json.JsonSerializer.Serialize(new
    {
        rule = "présent en jeu (source atteignable), niveau <= 90, nom FR utilisable — PLAN-ACTION-3 §4.3",
        maxLevel = ContentScope.MaxLevel,
        counts = new { maps = inMap.Count, monsters = inMob.Count, npcs = inNpc.Count, items = inItem.Count, quests = inQuest.Count },
        maps = inMap.Order(), monsters = inMob.Order(), npcs = inNpc.Order(), items = inItem.Order(), quests = inQuest.Order(),
    }));
    Console.WriteLine($"scope.json : {inMap.Count} cartes, {inMob.Count} monstres, {inNpc.Count} PNJ, {inItem.Count} objets, {inQuest.Count} quêtes");
}
return 0;
