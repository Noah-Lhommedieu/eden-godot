using Eden.Core.Game.Services;

namespace Eden.Zone.Combat.SpellEffects;

/// <summary>Enchant candidat à la dissipation — vue minimale d'un
/// <c>CEnchant</c> actif de l'entité visée.</summary>
/// <param name="EffectId">effect_id S_Enchant (u16 natif).</param>
/// <param name="IsDebuff">S_Enchant col7 bit1 (SEffectData+8 bit1).</param>
/// <param name="Undispellable">S_Enchant col8 bit 0x10 (SEffectData+0xc).</param>
/// <param name="HiddenIcon">enchant sans icône (<c>IsHideIcon</c>).</param>
public readonly record struct DispelCandidate(int EffectId, bool IsDebuff,
    bool Undispellable, bool HiddenIcon);

/// <summary>Règle d'un <c>CEC_EnchantKillRandom(Force)</c> après décodage
/// (champs de l'objet natif : +8 chance, +0xa count, +0xc type,
/// +0xe/+0x10 plage d'ids, +0x12 plage active).</summary>
public sealed record DispelRule(int Chance, int Count, int Type,
    bool Force, bool UseRange, int RangeMin, int RangeMax);

/// <summary>Sélection des enchants dissipés par les commandes
/// EnchantKill* — logique pure (testable sans serveur).
///
/// Sources de preuve (ZoneServer) :
///  - <c>CEC_EnchantKills::Execute</c> @0x0852dc90 : retire chaque id
///    listé (log si l'id n'existe pas) via RemoveAllEnchantIDs.
///  - <c>CEC_EnchantKillRandom::Execute</c> @0x0852ea60 : roll
///    <c>rand%100 &lt; chance</c> ; candidats = enchants avec icône,
///    SEffectData+0xc&amp;0x10 == 0, filtre <c>CCheckBufType</c>
///    @0x0852f2e0, plage d'ids si +0x12, hors ids 55001-56000 et
///    60001-61000 ; mélange Fisher-Yates ; tronque à +0xa.
///  - <c>CEC_EnchantKillRandomForce::Execute</c> @0x0852eed0 : idem
///    sans le bit 0x10, sans plage ni exclusion d'ids.
///  - Décodage des colonnes : <c>CSpellDBO::DecodeEffectCommand</c>
///    @0x8380ad0 — 1821 émet <c>EnchantKillRandom 1 a0 a1 a4 a5</c> si
///    a0&gt;0 et a1&gt;0, puis <c>EnchantKillRandom 2 a2 a3 a4 a5</c>
///    si a2&gt;0 et a3&gt;0 ; 1824 émet
///    <c>EnchantKillRandomForce 2 a0 a1</c> ; 1823 liste a0..a5.</summary>
public static class EnchantDispel
{
    /// <summary>Règles d'une commande 1821 (Random) ou 1824 (Force).
    /// Le drapeau de plage (+0x12) est posé quand a5 (id max) &gt; 0 —
    /// déduit des données : aucune ligne réelle ne renseigne a4/a5.
    /// </summary>
    public static IReadOnlyList<DispelRule> RulesFor(int commandId,
        double[] a)
    {
        double A(int i) => i < a.Length ? a[i] : 0;
        if (commandId == 1824)
            return new[]
            {
                new DispelRule((int)A(0), (int)A(1), 2, true, false, 0, 0),
            };
        var useRange = A(5) > 0;
        var rules = new List<DispelRule>(2);
        if (A(0) > 0 && A(1) > 0)
            rules.Add(new DispelRule((int)A(0), (int)A(1), 1, false,
                useRange, (int)A(4), (int)A(5)));
        if (A(2) > 0 && A(3) > 0)
            rules.Add(new DispelRule((int)A(2), (int)A(3), 2, false,
                useRange, (int)A(4), (int)A(5)));
        return rules;
    }

    /// <summary>Ids listés par un 1823 EnchantKills (a0..a5 non nuls).
    /// </summary>
    public static IReadOnlyList<int> KillList(double[] a)
        => a.Where(v => v > 0).Select(v => (int)v).ToList();

    /// <summary>Ids retirés par une règle Random/Force. <paramref name=
    /// "rollPercent"/> renvoie un entier dans [0,100) ; <paramref
    /// name="shuffle"/> reçoit la liste à mélanger (Fisher-Yates natif).
    /// </summary>
    public static IReadOnlyList<int> Pick(DispelRule rule,
        IEnumerable<DispelCandidate> candidates, Func<int> rollPercent,
        Func<int, int> nextBelow)
    {
        if (rollPercent() >= rule.Chance) return Array.Empty<int>();
        var pool = new List<int>();
        foreach (var c in candidates)
        {
            if (c.HiddenIcon) continue;
            if (c.IsDebuff != (rule.Type == 2)) continue;
            if (!rule.Force)
            {
                if (c.Undispellable) continue;
                if (rule.UseRange && (c.EffectId < rule.RangeMin ||
                    c.EffectId > rule.RangeMax)) continue;
                if (IsProtectedId(c.EffectId)) continue;
            }
            pool.Add(c.EffectId);
        }
        // Fisher-Yates du natif : i de 1 à n-1, j = rand % (i+1).
        for (var i = 1; i < pool.Count; i++)
        {
            var j = nextBelow(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        if (rule.Count < pool.Count)
            pool.RemoveRange(Math.Max(rule.Count, 0),
                pool.Count - Math.Max(rule.Count, 0));
        return pool;
    }

    /// <summary>Ids exclus par le natif : <c>(u16)(id+0x2927) &lt;
    /// 1000</c> (55001-56000) et <c>(u16)(id+0x159f) &lt; 1000</c>
    /// (60001-61000).</summary>
    public static bool IsProtectedId(int id)
        => (ushort)(id + 0x2927) < 1000 || (ushort)(id + 0x159f) < 1000;
}
