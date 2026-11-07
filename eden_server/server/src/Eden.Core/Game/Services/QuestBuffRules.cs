using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Règles pures de l'objectif de quête <c>CheckBuff &lt;enchant&gt; &lt;op&gt;
/// &lt;valeur&gt;</c> (131 occurrences col16 de S_Mission : escortes, quêtes
/// « tenir un buff »). Preuves reverse (mission/decompiled.c) :
/// <list type="bullet">
/// <item><c>MC_FinishCheckBuff</c> @0x080e35c0 enregistre la paire
/// (op, valeur) par id d'enchant ; <c>MC_IfCheckBuff</c> @0x080e34f0 fait
/// de même pour la condition d'acceptation <c>If_CheckBuff</c>.</item>
/// <item><c>CGlobalInfo::UpdateQuestCheckBuffStatus</c> @0x08094ef0 :
/// n'agit que pour un id d'enchant dans [45001, 59999]
/// (<c>(ushort)(id+0x5037) &lt; 14999</c>) ; compare le compteur courant du
/// buff chez le joueur (0 si absent) avec la valeur :
/// op 1 → cur ≥ val, op 2 → cur ≤ val, op 3 → cur = val, op 4 → cur ≠ val.
/// Satisfait : bit d'objectif mis, puis <c>SetFMS(4)</c> si tous les
/// autres objectifs (PMS) et le niveau sont remplis ; bit effacé :
/// <c>SetFMS(3)</c>.</item>
/// <item><c>TC_CheckMissionEnchant</c> @0x081f2960 : la zone notifie la
/// mission d'un changement d'enchant par la commande DBA
/// <c>CheckMissionEnchant &lt;id&gt;</c>.</item>
/// </list>
/// </summary>
public static class QuestBuffRules
{
    /// <summary>Bornes natives des enchants suivis par les quêtes.</summary>
    public const int MinEnchantId = 45001, MaxEnchantId = 59999;

    /// <summary>Un objectif CheckBuff décodé.</summary>
    public readonly record struct BuffObjective(int EnchantId, int Op,
        int Value);

    /// <summary>Id d'enchant dans la plage suivie par le natif.</summary>
    public static bool IsTrackedId(int enchantId) =>
        enchantId is >= MinEnchantId and <= MaxEnchantId;

    /// <summary>Objectifs CheckBuff d'une mission (col16).</summary>
    public static List<BuffObjective> Objectives(MissionParser.MissionDef m)
    {
        var list = new List<BuffObjective>();
        foreach (var o in m.Actions)
        {
            if (o.Name != "CheckBuff" || o.Args.Count < 1 ||
                !int.TryParse(o.Args[0], out var id)) continue;
            var op = o.Args.Count > 1 && int.TryParse(o.Args[1], out var p)
                ? p : 1;
            var val = o.Args.Count > 2 && int.TryParse(o.Args[2], out var v)
                ? v : 1;
            list.Add(new BuffObjective(id, op, val));
        }
        return list;
    }

    /// <summary>Comparaison native (op, valeur) ↔ compteur courant.</summary>
    public static bool Satisfied(int op, int value, int current) => op switch
    {
        1 => current >= value,
        2 => current <= value,
        3 => current == value,
        4 => current != value,
        _ => false,   // op inconnu : natif → bit non posé
    };

    /// <summary>Vrai si tous les objectifs CheckBuff de la mission sont
    /// satisfaits par les compteurs de buffs donnés (absent = 0).</summary>
    public static bool AllSatisfied(MissionParser.MissionDef m,
        IReadOnlyDictionary<int, int> buffs)
    {
        foreach (var o in Objectives(m))
            if (!Satisfied(o.Op, o.Value,
                    buffs.TryGetValue(o.EnchantId, out var c) ? c : 0))
                return false;
        return true;
    }

    /// <summary>Ids d'enchants référencés par au moins un CheckBuff de
    /// la liste de missions — le zone ne notifie que ceux-là.</summary>
    public static HashSet<int> TrackedEnchants(
        IEnumerable<MissionParser.MissionDef> missions)
    {
        var set = new HashSet<int>();
        foreach (var m in missions)
            foreach (var o in Objectives(m))
                if (IsTrackedId(o.EnchantId)) set.Add(o.EnchantId);
        // Conditions d'acceptation If_CheckBuff (fatigue des quêtes de
        // ville 54991/58759) : le miroir de compteur doit exister.
        foreach (var m in missions)
            foreach (var c in m.Conditions)
                if (c.Name == "If_CheckBuff" && c.Args.Count > 0 &&
                    int.TryParse(c.Args[0], out var cid) && IsTrackedId(cid))
                    set.Add(cid);
        return set;
    }
}
