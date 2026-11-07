using System.Text.Json;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Maps;
using Eden.Core.Game.Modifiers;

namespace Eden.Core.Game.Calendar;

/// <summary>Un bonus d'une journée : modificateur du moteur, limité éventuellement à un type de carte.</summary>
public sealed record AlmanaxRule(string KeyId, ModifierMode Mode, double Value, MapKind? When = null);

/// <summary>Modèle d'effet du jour (« Journée du donjon », « Journée d'EXP »…). <c>Theme</c> sert à l'alternance.</summary>
public sealed record AlmanaxTemplate(int Id, string NameFr, string Theme, int Weight, IReadOnlyList<AlmanaxRule> Rules);

/// <summary>Un jour du calendrier : jour de reset, modèle actif, forcé par l'admin ou tiré.</summary>
public sealed record AlmanaxDay(int ResetDay, AlmanaxTemplate Template, bool Forced);

/// <summary>
/// Éphéméride d'Eden (PLAN-ACTION-3 F5) : effet du jour déduit de la DATE (aucune tâche planifiée). Tirage pondéré
/// déterministe (<c>hash(jour, graine)</c>) avec règle d'exclusion « jamais deux jours de suite le même thème » ; l'admin
/// peut forcer un jour. Le calendrier se calcule depuis une ancre fixe, donc deux serveurs avec la même graine
/// voient la même suite.
/// </summary>
public sealed class AlmanaxCalendar
{
    /// <summary>Jour de reset d'ancrage (début de la suite déterministe).</summary>
    public const int AnchorResetDay = 20000;
    public const int WindowDays = 21;

    private readonly IReadOnlyList<AlmanaxTemplate> _templates;
    private readonly long _seed;
    private readonly Dictionary<int, int> _forced = new();
    private readonly Dictionary<int, AlmanaxTemplate> _cache = new();

    public AlmanaxCalendar(IReadOnlyList<AlmanaxTemplate> templates, long seed)
    {
        if (templates.Count == 0) throw new ArgumentException("aucun modèle d'effet");
        _templates = templates; _seed = seed;
    }

    /// <summary>Force un modèle pour un jour de reset (verrouillage admin) ; invalide les jours suivants.</summary>
    public void Force(int resetDay, int templateId)
    {
        if (_templates.All(t => t.Id != templateId)) throw new ArgumentException($"modèle {templateId} inconnu");
        _forced[resetDay] = templateId; _cache.Clear();
    }

    public AlmanaxDay Day(int resetDay)
    {
        var t = TemplateOf(resetDay);
        return new AlmanaxDay(resetDay, t, _forced.ContainsKey(resetDay));
    }

    /// <summary>Les <see cref="WindowDays"/> jours à partir de <paramref name="fromResetDay"/> (panneau « battle pass »).</summary>
    public IReadOnlyList<AlmanaxDay> Window(int fromResetDay) =>
        Enumerable.Range(fromResetDay, WindowDays).Select(Day).ToList();

    private AlmanaxTemplate TemplateOf(int day)
    {
        if (day < AnchorResetDay) day = AnchorResetDay;
        if (_cache.TryGetValue(day, out var hit)) return hit;
        AlmanaxTemplate? prev = null;
        for (var d = AnchorResetDay; d <= day; d++)
        {
            if (!_cache.TryGetValue(d, out var cur))
            {
                cur = _forced.TryGetValue(d, out var fid) ? _templates.First(t => t.Id == fid) : Pick(d, prev);
                _cache[d] = cur;
            }
            prev = cur;
        }
        return prev!;
    }

    private AlmanaxTemplate Pick(int day, AlmanaxTemplate? previous)
    {
        var pool = _templates.Where(t => previous == null || t.Theme != previous.Theme).ToList();
        if (pool.Count == 0) pool = _templates.ToList();         // un seul thème possible : pas d'exclusion
        var total = pool.Sum(t => (double)Math.Max(1, t.Weight));
        var x = Unit(day) * total;
        foreach (var t in pool) { x -= Math.Max(1, t.Weight); if (x < 0) return t; }
        return pool[^1];
    }

    /// <summary>Réel déterministe dans [0 ; 1[ pour (jour, graine) : FNV-1a 64 bits.</summary>
    private double Unit(int day)
    {
        var h = 14695981039346656037UL;
        foreach (var v in new[] { (ulong)day, (ulong)_seed })
            for (var i = 0; i < 8; i++) { h ^= (v >> (i * 8)) & 0xff; h *= 1099511628211UL; }
        return (h >> 11) / (double)(1UL << 53);
    }

    /// <summary>Modèles par défaut (valeurs d'équilibrage initiales, éditables via <c>content/rules/almanax.json</c>).</summary>
    public static IReadOnlyList<AlmanaxTemplate> DefaultTemplates { get; } = new AlmanaxTemplate[]
    {
        new(1, "Journée du donjon", "donjon", 3, new[] { new AlmanaxRule("dungeon.entries.solo", ModifierMode.AddFlat, 1), new AlmanaxRule("dungeon.entries.multi", ModifierMode.AddFlat, 1) }),
        new(2, "Journée d'EXP", "exp", 3, new[] { new AlmanaxRule("exp.kill.character", ModifierMode.AddPercent, 25, MapKind.Field) }),
        new(3, "Journée du bâtiment", "ville", 2, new[] { new AlmanaxRule("craft.duration", ModifierMode.Multiply, 0.8) }),
        new(4, "Journée de récolte", "recolte", 2, new[] { new AlmanaxRule("gather.tick_seconds", ModifierMode.Multiply, 0.75) }),
        new(5, "Journée de butin", "butin", 2, new[] { new AlmanaxRule("exp.kill.class", ModifierMode.AddPercent, 25) }),
        new(6, "Journée d'entrées bonus", "donjon", 1, new[] { new AlmanaxRule("dungeon.entries.multi", ModifierMode.AddFlat, 2) }),
    };

    /// <summary>Lit un calendrier JSON : <c>[{"id":1,"name":"…","theme":"…","weight":3,"rules":[{"key":"exp.kill.character","mode":"AddPercent","value":25,"when":"Field"}]}]</c>.</summary>
    public static IReadOnlyList<AlmanaxTemplate> ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => new AlmanaxTemplate(
            e.GetProperty("id").GetInt32(), e.GetProperty("name").GetString()!, e.GetProperty("theme").GetString()!,
            e.TryGetProperty("weight", out var w) ? w.GetInt32() : 1,
            e.GetProperty("rules").EnumerateArray().Select(r => new AlmanaxRule(
                r.GetProperty("key").GetString()!, Enum.Parse<ModifierMode>(r.GetProperty("mode").GetString()!),
                r.GetProperty("value").GetDouble(),
                r.TryGetProperty("when", out var wh) ? Enum.Parse<MapKind>(wh.GetString()!) : null)).ToList())).ToList();
    }
}

/// <summary>Source du moteur de modificateurs : les règles de l'effet du jour (jour de reset de l'horloge de jeu).</summary>
public sealed class AlmanaxModifierSource : IModifierSource
{
    private readonly AlmanaxCalendar _calendar;
    public AlmanaxModifierSource(AlmanaxCalendar calendar) => _calendar = calendar;
    public string Name => "almanax";

    public void Collect(ModifierKey key, in ModifierContext ctx, List<ModifierContribution> sink)
    {
        var day = _calendar.Day(GameTime.Clock.ResetDayNow());
        foreach (var r in day.Template.Rules)
            if (r.KeyId == key.Id && (r.When == null || r.When == ctx.MapKind))
                sink.Add(new ModifierContribution($"almanax:{day.Template.NameFr}", r.Mode, r.Value));
    }
}
