// Génère content/rules/scope.json (PLAN-ACTION-3 C3) : contenu « présent en jeu et ≤ Lv90 ».
// Usage : dotnet run --project tools/Eden.ContentScope -- <dossier data/db> <dossier scene> <sortie.json>
using System.Text.Json;
using Eden.Core.Game.Content;
using Eden.Core.Game.Services;

if (args.Length < 3) { Console.Error.WriteLine("usage : <data/db> <scene> <sortie.json>"); return 2; }
var gd = new GameDataService(args[0]);
gd.LoadAll();
var scenes = new SceneService(MonsterService.LoadFromDataDir(args[0]));
scenes.LoadScenes(args[1]);

var res = ContentScopeBuilder.Build(gd, scenes);
var json = JsonSerializer.Serialize(new
{
    rule = "présent en jeu (source atteignable) et niveau <= 90 — PLAN-ACTION-3 §4.3",
    maxLevel = ContentScope.MaxLevel,
    counts = new { maps = res.Maps.Count, monsters = res.Monsters.Count, npcs = res.Npcs.Count, items = res.Items.Count, quests = res.Quests.Count },
    maps = res.Maps, monsters = res.Monsters, npcs = res.Npcs, items = res.Items, quests = res.Quests,
}, new JsonSerializerOptions { WriteIndented = false });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllText(args[2], json);
Console.WriteLine($"scope : {res.Maps.Count} maps, {res.Monsters.Count} monstres, {res.Npcs.Count} PNJ, {res.Items.Count} objets, {res.Quests.Count} quêtes -> {args[2]}");
return 0;
