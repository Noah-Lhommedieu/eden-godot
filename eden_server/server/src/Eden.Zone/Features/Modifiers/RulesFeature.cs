using Eden.Core.Game.Events;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Rules;
using Eden.Core.Logging;
using Eden.Core.Networking.Handlers;

namespace Eden.Zone.Features.Modifiers;

/// <summary>Compteurs « par jour de reset » alimentés par les règles de données (action <c>counter</c>).</summary>
public static class DailyCounters
{
    private static readonly object Lock = new();
    private static readonly Dictionary<(int Day, int Char, string Name), int> Values = new();

    public static int Add(int resetDay, int charId, string name, int by)
    {
        lock (Lock)
        {
            foreach (var k in Values.Keys.Where(k => k.Day < resetDay).ToList()) Values.Remove(k);   // jours passés : purge
            var key = (resetDay, charId, name);
            return Values[key] = Values.GetValueOrDefault(key) + by;
        }
    }

    public static int Get(int resetDay, int charId, string name)
    {
        lock (Lock) return Values.GetValueOrDefault((resetDay, charId, name));
    }
}

/// <summary>
/// Branche le moteur de règles (Q6) sur le bus d'événements (Q5) : règles lues dans <c>content/rules/triggers.json</c>
/// (ou <c>EDEN_TRIGGERS_FILE</c>), actions <c>log</c> et <c>counter</c> (compteur du jour de reset, ex. éphéméride).
/// </summary>
internal sealed class RulesFeature : IZoneFeature
{
    public string Name => "rules";

    public void Register(PacketDispatcher d, ZoneServices s)
    {
        var engine = new RuleEngine();
        engine.RegisterAction("log", (e, a) => EdenLog.Write($"[Rules] {e.Name} : {(a.TryGetValue("text", out var t) ? t : "")}"));
        engine.RegisterAction("counter", (e, a) =>
        {
            if (!a.TryGetValue("name", out var name) || !e.Facts.TryGetValue("CharId", out var ch)) return;
            var by = a.TryGetValue("by", out var b) && int.TryParse(b, out var n) ? n : 1;
            DailyCounters.Add(GameTime.Clock.ResetDayNow(), (int)ch, name, by);
        });
        var path = Environment.GetEnvironmentVariable("EDEN_TRIGGERS_FILE") ?? Find();
        if (path != null && File.Exists(path))
        {
            try { engine.SetRules(RuleEngine.ParseJson(File.ReadAllText(path))); EdenLog.Write($"[Rules] {engine.Rules.Count} règle(s) lues dans {path}"); }
            catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            { EdenLog.Write($"[Rules] fichier invalide ({e.Message}) : aucune règle"); }
        }
        engine.Attach(DomainEventBus.Shared);
    }

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "content", "rules", "triggers.json");
            if (File.Exists(f)) return f;
        }
        return null;
    }
}
