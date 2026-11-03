using Eden.Core.Logging;
namespace Eden.Core.Game;

/// <summary>
/// Réglages d'équilibrage du serveur — chargés depuis
/// `<dataDir>/balance.ini` (clé=valeur par ligne, # commentaire)
/// puis surchargés par les variables d'environnement `EDEN_BAL_*`.
/// Toutes les valeurs sont éditables à chaud au déploiement : il suffit
/// de changer le .ini et relancer le service.
///
/// Clés reconnues :
///   CoreStatDivisor   = 8    — 1 point de stat AFFICHÉ (STR/AGI/INT/
///                            WIS/LCK) = 8 points réels (désasm client :
///                            l'UI divise par 8 ; équilibrage serveur).
///   MobWanderMinMs    = 2500 — délai min entre deux wander de mob.
///   MobWanderMaxMs    = 6000 — délai max.
///   MobWanderRadius   = 8    — rayon de dérive autour du spawn.
///   SkillCostDivisor  = 100  — coût upgrade = base × taux / diviseur
///                            (natif : 0.01 exact — ne changer que pour
///                            modder l'économie).
///   MaxSpellLevel     = 100  — cap d'upgrade de sort (formule native
///                            AllFormulaParas+0x908 ; borne serveur).
///   param.<id>        = f    — surcharge un coefficient S_Parameter
///                            (ex. `param.305=80` → 1 STR = 80 P-ATK).
///                            Les formules lisent d'abord ici, sinon
///                            la valeur native du fichier.
///   AgiToMoveSpeed    = 0.08 — % de vitesse de déplacement par point
///                            d'AGI (custom serveur — inexistant natif).
///   MoveSpeedCapPct   = 300  — cap du multiplicateur de vitesse en %
///                            (natif DAT_087b273c = 200% — custom 300).
///   BlockRateCapPct   = 50   — cap du BlockDamageRate en % (natif :
///                            100 — custom serveur 50%).
/// </summary>
public static class GameBalance
{
    public static int CoreStatDivisor { get; private set; } = 8;
    public static int MobWanderMinMs { get; private set; } = 2500;
    public static int MobWanderMaxMs { get; private set; } = 6000;
    public static int MobWanderRadius { get; private set; } = 8;
    public static int SkillCostDivisor { get; private set; } = 100;
    public static int MaxSpellLevel { get; private set; } = 100;
    public static float AgiToMoveSpeed { get; private set; }
    public static float MoveSpeedCapPct { get; private set; } = 200f;
    public static float BlockRateCapPct { get; private set; } = 100f;

    /// <summary>Surcharges `param.<id>=<valeur>` de balance.ini et
    /// `EDEN_BAL_PARAM_<id>` — prioritaires sur S_Parameter.ini pour
    /// les coefficients de formules (ratios custom serveur).</summary>
    private static readonly Dictionary<int, float> _paramOverrides = new();

    /// <summary>Surcharge d'un coefficient S_Parameter, ou null si
    /// l'id n'est pas redéfini — à utiliser en priorité dans
    /// `ParameterSet.Get` par les formules.</summary>
    public static float? Param(int id)
        => _paramOverrides.TryGetValue(id, out var v) ? v : null;

    /// <summary>Charge `balance.ini` du répertoire de données, puis les
    /// surcharges `EDEN_BAL_*`. Tolérant : fichier absent → défauts.</summary>
    public static void Load(string dataDir)
    {
        var path = Path.Combine(dataDir, "balance.ini");
        if (File.Exists(path))
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') ||
                    line.StartsWith(';')) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Apply(line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
        }
        // Surcharges env — prioritaires sur le fichier.
        foreach (var key in new[]
        {
            "CoreStatDivisor", "MobWanderMinMs", "MobWanderMaxMs",
            "MobWanderRadius", "SkillCostDivisor", "MaxSpellLevel",
            "AgiToMoveSpeed", "MoveSpeedCapPct", "BlockRateCapPct",
        })
        {
            var env = Environment.GetEnvironmentVariable(
                "EDEN_BAL_" + key.ToUpperInvariant());
            if (env is not null) Apply(key, env);
        }
        // `EDEN_BAL_PARAM_<id>` — surcharge de coefficient S_Parameter.
        foreach (var kv in Environment.GetEnvironmentVariables())
        {
            var de = (System.Collections.DictionaryEntry)kv;
            var name = de.Key?.ToString() ?? "";
            if (name.StartsWith("EDEN_BAL_PARAM_") &&
                int.TryParse(name["EDEN_BAL_PARAM_".Length..], out var pid))
                ApplyParam(pid, de.Value?.ToString() ?? "");
        }
        EdenLog.Write($"[Balance] CoreStatDivisor={CoreStatDivisor} " +
            $"Wander={MobWanderMinMs}-{MobWanderMaxMs}ms " +
            $"r={MobWanderRadius} SkillCostDiv={SkillCostDivisor} " +
            $"MaxSpellLv={MaxSpellLevel} AgiMoveSpd={AgiToMoveSpeed} " +
            $"MoveCap%={MoveSpeedCapPct} BlockCap%={BlockRateCapPct} " +
            $"paramOverrides={_paramOverrides.Count}");
    }

    private static void Apply(string key, string value)
    {
        // `param.<id>` — surcharge d'un coefficient S_Parameter.
        if (key.StartsWith("param.", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key[6..], out var pid))
        {
            ApplyParam(pid, value);
            return;
        }
        if (int.TryParse(value, out var v))
            switch (key)
            {
                case "CoreStatDivisor": if (v > 0) CoreStatDivisor = v; break;
                case "MobWanderMinMs": if (v >= 200) MobWanderMinMs = v; break;
                case "MobWanderMaxMs": if (v >= MobWanderMinMs) MobWanderMaxMs = v; break;
                case "MobWanderRadius": if (v > 0) MobWanderRadius = v; break;
                case "SkillCostDivisor": if (v > 0) SkillCostDivisor = v; break;
                case "MaxSpellLevel": if (v > 0) MaxSpellLevel = v; break;
            }
        if (!float.TryParse(value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var f))
            return;
        switch (key)
        {
            case "AgiToMoveSpeed": if (f >= 0) AgiToMoveSpeed = f; break;
            case "MoveSpeedCapPct": if (f >= 100) MoveSpeedCapPct = f; break;
            case "BlockRateCapPct": if (f is >= 0 and <= 100) BlockRateCapPct = f; break;
        }
    }

    /// <summary>Enregistre une surcharge `param.<id>` (balance.ini ou
    /// env) — valeur flottante InvariantCulture.</summary>
    private static void ApplyParam(int id, string value)
    {
        if (id > 0 && float.TryParse(value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v))
            _paramOverrides[id] = v;
    }
}
