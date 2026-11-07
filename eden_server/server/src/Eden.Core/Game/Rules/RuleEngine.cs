using System.Text.Json;
using Eden.Core.Game.Events;
using Eden.Core.Logging;

namespace Eden.Core.Game.Rules;

/// <summary>Condition sur un fait : <c>field op value</c> avec op ∈ eq, ne, gt, ge, lt, le, in (valeurs séparées par « , »).</summary>
public sealed record Condition(string Field, string Op, string Value)
{
    public bool Matches(IReadOnlyDictionary<string, long> facts)
    {
        if (!facts.TryGetValue(Field, out var f)) return false;
        if (Op == "in") return Value.Split(',').Any(v => long.TryParse(v.Trim(), out var n) && n == f);
        if (!long.TryParse(Value, out var x)) return false;
        return Op switch
        {
            "eq" => f == x, "ne" => f != x, "gt" => f > x, "ge" => f >= x, "lt" => f < x, "le" => f <= x,
            _ => false,
        };
    }
}

/// <summary>Action d'une règle : nom d'une action enregistrée + arguments texte.</summary>
public sealed record RuleAction(string Name, IReadOnlyDictionary<string, string> Args);

/// <summary>Règle de données : QUAND <see cref="On"/> arrive, SI toutes les conditions sont vraies, ALORS les actions s'exécutent.</summary>
public sealed record TriggerRule(string Id, string On, IReadOnlyList<Condition> If, IReadOnlyList<RuleAction> Then);

/// <summary>
/// Moteur Déclencheur → Condition → Action (PLAN-ACTION-3 Q6). Pas d'interpréteur : les règles sont des données
/// (JSON), les actions des noms résolus dans un registre de code testé. Brancher un nouveau comportement = enregistrer
/// une action (un fichier), jamais modifier le moteur.
/// </summary>
public sealed class RuleEngine
{
    public delegate void ActionHandler(IDomainEvent evt, IReadOnlyDictionary<string, string> args);

    private readonly Dictionary<string, ActionHandler> _actions = new();
    private readonly List<TriggerRule> _rules = new();

    public void RegisterAction(string name, ActionHandler handler) => _actions[name] = handler;
    public void SetRules(IEnumerable<TriggerRule> rules) { _rules.Clear(); _rules.AddRange(rules); }
    public IReadOnlyList<TriggerRule> Rules => _rules;

    /// <summary>Abonne le moteur à tous les événements du bus.</summary>
    public IDisposable Attach(DomainEventBus bus) => bus.SubscribeAll(e => Evaluate(e));

    /// <summary>Exécute les règles correspondant à l'événement ; renvoie le nombre de règles déclenchées.</summary>
    public int Evaluate(IDomainEvent evt)
    {
        var facts = evt.Facts; var fired = 0;
        foreach (var r in _rules)
        {
            if (r.On != evt.Name || !r.If.All(c => c.Matches(facts))) continue;
            fired++;
            foreach (var a in r.Then)
            {
                if (_actions.TryGetValue(a.Name, out var h)) h(evt, a.Args);
                else EdenLog.Write($"[Rules] règle {r.Id} : action inconnue '{a.Name}'");
            }
        }
        return fired;
    }

    /// <summary>
    /// Lit des règles JSON : <c>[{"id":"r1","on":"MobKilled","if":[{"field":"MonsterId","op":"eq","value":"5"}],
    /// "then":[{"action":"counter","args":{"name":"angor"}}]}]</c>. Une règle invalide lève <see cref="FormatException"/>.
    /// </summary>
    public static IReadOnlyList<TriggerRule> ParseJson(string json)
    {
        var res = new List<TriggerRule>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string Req(JsonElement x, string p) => x.TryGetProperty(p, out var v) && v.GetString() is { Length: > 0 } s
                ? s : throw new FormatException($"règle sans '{p}'");
            var conds = e.TryGetProperty("if", out var ci)
                ? ci.EnumerateArray().Select(c => new Condition(Req(c, "field"), Req(c, "op"), Req(c, "value"))).ToList()
                : new List<Condition>();
            var acts = e.TryGetProperty("then", out var ti)
                ? ti.EnumerateArray().Select(a => new RuleAction(Req(a, "action"),
                    a.TryGetProperty("args", out var ar)
                        ? ar.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString())
                        : new Dictionary<string, string>())).ToList()
                : new List<RuleAction>();
            res.Add(new TriggerRule(Req(e, "id"), Req(e, "on"), conds, acts));
        }
        return res;
    }
}
