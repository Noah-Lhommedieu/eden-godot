namespace Eden.Core.Game.Modifiers;

/// <summary>Règle de modificateur lue dans <c>server.config.txt</c> (<c>Set./Mul./AddPct./AddFlat./Cap.&lt;clé&gt; = valeur</c>).</summary>
public sealed record ConfigModifierRule(string KeyId, ModifierMode Mode, double Value);

/// <summary>
/// Source « configuration serveur » : (1) les taux historiques (<c>ExpRate</c>, <c>ClassExpRate</c>,
/// <c>WakenClassExpRate</c> + bonus d'événements actifs) exprimés en multiplicateurs des clés de kill ;
/// (2) les règles génériques du fichier, qui permettent de régler N'IMPORTE QUELLE clé du registre
/// sans coder (ex. <c>Set.dungeon.entries.solo = 12</c>, <c>Mul.craft.duration = 0.5</c>).
/// Lue à chaque résolution : le rechargement à chaud du fichier s'applique immédiatement.
/// </summary>
public sealed class ServerConfigModifierSource : IModifierSource
{
    private readonly double _envExpRate, _envClassRate, _envWakenRate;

    /// <param name="envExpRate">Taux de repli (variables d'environnement) quand le fichier ne fixe rien.</param>
    public ServerConfigModifierSource(double envExpRate, double envClassRate, double envWakenRate)
    {
        _envExpRate = envExpRate; _envClassRate = envClassRate; _envWakenRate = envWakenRate;
    }

    public string Name => "config";

    public void Collect(ModifierKey key, in ModifierContext ctx, List<ModifierContribution> sink)
    {
        var rate = key.Id switch
        {
            "exp.kill.character" => ServerConfig.EffectiveExpRate(_envExpRate),
            "exp.kill.class" => ServerConfig.EffectiveClassExpRate(_envClassRate, false),
            "exp.kill.awakened_class" => ServerConfig.EffectiveClassExpRate(_envWakenRate, true),
            _ => double.NaN,
        };
        if (!double.IsNaN(rate)) sink.Add(new ModifierContribution(Name, ModifierMode.Multiply, rate));
        var snap = ServerConfig.Current;
        foreach (var r in snap.ModifierRules)
            if (r.KeyId == key.Id) sink.Add(new ModifierContribution(Name, r.Mode, r.Value));
        foreach (var (evName, ev) in snap.Events)
            if (ev.Active)
                foreach (var r in ev.Rules)
                    if (r.KeyId == key.Id) sink.Add(new ModifierContribution($"event:{evName}", r.Mode, r.Value));
    }
}
