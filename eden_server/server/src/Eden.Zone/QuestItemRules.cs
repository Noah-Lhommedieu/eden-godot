using Eden.Core.Data.Parsers;

namespace Eden.Zone;

/// <summary>
/// Règles pures (sans I/O) des items de quête « usage positionnel » —
/// plan futur-feature/01. Preuve reverse (zone/decompiled.c) :
/// <list type="bullet">
/// <item><c>CEC_SummonInEventArea::Init</c> @0x0855a590 — les vars du bloc
/// S_Enchant sont, dans l'ordre : mob (+0xc, ≠0), compte (+0x10,
/// 1..10), node (+0x14, 0..999), clé de position (+0x18, ≥0), id
/// d'effet (+0x1c), échelle d'effet (+0x20), puis un mot optionnel
/// <c>quest</c> (+0x22, absent de toutes les données S_Enchant).</item>
/// <item><c>CEC_SummonInEventArea::CanExecute</c> @0x08559980 — node &gt; 0
/// et ≠ node du lanceur → texte 0x5c0 (refus, item NON consommé) ;
/// node &gt; 0 sans clé de position résolue dans ce node → log + refus ;
/// la clé résout une ligne <c>[position]</c> de la scène (col7).</item>
/// <item><c>Execute</c> @0x08559b70 — spawn à la position résolue (x,y,
/// facing, largeur/hauteur de la ligne) ou à la position du lanceur si
/// aucune clé ; le mob invoqué prend le lanceur pour cible
/// (BecomeFightingCandicate) ; <c>ClientPlayEffect</c> broadcast.</item>
/// </list>
/// Il n'existe PAS de contrôle de coordonnée côté natif au-delà du node :
/// les coordonnées « (349,341) » des tooltips wiki sont l'emplacement de
/// la ligne <c>[position]</c> (ex. S010 clé 160 = (355.6, 341.8)).
/// </summary>
public static class QuestItemRules
{
    /// <summary>Texte natif de refus « mauvaise map » (CanExecute).</summary>
    public const short TextWrongNode = 0x5c0;

    /// <summary>Spécification décodée d'un bloc SummonInEventArea.</summary>
    public sealed record SummonSpec(int MobId, int Count, int NodeId,
        int PositionKey, int EffectId, int EffectScale);

    /// <summary>Décode les vars d'un bloc SummonInEventArea (ids
    /// 1074/1075/2045). Retourne null si le bloc est invalide pour le
    /// natif (mob 0 → Init renvoie 0). Le compte est borné à 1..10
    /// comme <c>Init</c> (hors plage → bloc rejeté, ramené ici à 1
    /// pour les données à 0 qui valent « un »).</summary>
    public static SummonSpec? ParseSummon(IReadOnlyList<int> a)
    {
        var mob = a.Count > 0 ? a[0] : 0;
        if (mob <= 0) return null;
        var count = a.Count > 1 && a[1] > 0 ? a[1] : 1;
        count = Math.Clamp(count, 1, 10);
        var node = a.Count > 2 ? Math.Clamp(a[2], 0, 999) : 0;
        var key = a.Count > 3 ? Math.Max(0, a[3]) : 0;
        return new SummonSpec(mob, count, node, key,
            a.Count > 4 ? a[4] : 0, a.Count > 5 ? a[5] : 0);
    }

    /// <summary>Résultat de <see cref="CheckSummon"/>.</summary>
    public enum Refusal { None, WrongNode, PositionMissing }

    /// <summary>CanExecute natif : refus si node requis ≠ node du
    /// lanceur ou si une clé de position est donnée mais absente de la
    /// scène. <paramref name="positionExists"/> = la clé résout une
    /// ligne [position] du node.</summary>
    public static Refusal CheckSummon(SummonSpec s, int casterNode,
        bool positionExists)
    {
        if (s.NodeId > 0 && s.NodeId != casterNode)
            return Refusal.WrongNode;
        if (s.NodeId > 0 && s.PositionKey > 0 && !positionExists)
            return Refusal.PositionMissing;
        return Refusal.None;
    }

    /// <summary>Résout la clé de position (col7 de [position]) en un
    /// point de la scène — première ligne du groupe. Null si absent.</summary>
    public static (float X, float Y, float Z, float Facing)?
        ResolvePosition(SceneParser.Scene? scene, int key)
    {
        if (scene == null || key <= 0) return null;
        return scene.PositionGroups.TryGetValue(key, out var list) &&
               list.Count > 0 ? list[0] : null;
    }

    /// <summary>Vrai si une commande d'enchant est un summon de
    /// quête positionnel (1074/1075/2045 → nom SummonInEventArea).</summary>
    public static bool IsSummonCommand(string name) =>
        name == "SummonInEventArea";

    /// <summary>Nombre de mobs invoqués encore vivants pour ce lanceur
    /// et ce gabarit — garde « une invocation à la fois » du mode
    /// <c>quest</c> natif (texte 0x5c1). Non branché sur les données
    /// (le mot « quest » n'existe dans aucune ligne S_Enchant), gardé
    /// testé pour le jour où une donnée l'utilise.</summary>
    public static int CountLiveSummons(IEnumerable<ZoneLiveWorld.LiveMob> mobs,
        int casterCharId, int mobId) =>
        mobs.Count(m => m.Alive && m.MobId == mobId &&
                        m.SummonedByCharId == casterCharId);
}
