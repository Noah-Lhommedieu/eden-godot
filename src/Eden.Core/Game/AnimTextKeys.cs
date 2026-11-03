using EdenClient.World;

namespace EdenClient.Game;

/// <summary>Nature d'un événement d'animation issu d'une text key.</summary>
public enum AnimEventKind
{
    /// <summary>« start » : début (borne basse de boucle).</summary>
    Start,
    /// <summary>« end » : fin (borne haute de boucle / fin de clip).</summary>
    End,
    /// <summary>Frame d'impact : « hit », ou « m# » d'une attaque/tir.</summary>
    Hit,
    /// <summary>Pas : « m# » d'une marche/course (m1 = un pied, m2 = l'autre).</summary>
    Footstep,
    /// <summary>Repère de mouvement sans rôle de jeu (« m# » d'une
    /// réaction : hurt/death/condition/stand…).</summary>
    Marker,
    /// <summary>« s# » : son (hypothèse, 1 occurrence : M014 shoot01).</summary>
    Sound,
    /// <summary>« e# » : apparition d'effet (hypothèse, idem).</summary>
    EffectSpawn,
    /// <summary>Métadonnée d'export (« -GlobalRatio… », « morph: unknown »).</summary>
    Metadata,
    /// <summary>Clé non reconnue.</summary>
    Unknown,
}

/// <summary>Événement horodaté ; <see cref="Time"/> en secondes depuis le
/// début de la séquence (Start déjà retranché).</summary>
public readonly record struct AnimEvent(float Time, AnimEventKind Kind,
    string Key, int Index);

/// <summary>
/// Text keys <c>NiTextKeyExtraData</c> des .kf → événements de jeu.
///
/// Familles mesurées sur les 716 .kf / 22 945 séquences de fr-full
/// (toutes les clés, 11 chaînes distinctes) :
/// <list type="bullet">
/// <item><c>start</c> / <c>end</c> : 22 945 chacune (toute séquence) ;
/// « end » = Stop dans 99,97 % des cas (6 anomalies M014).</item>
/// <item><c>m1</c>…<c>m3</c> (53 occurrences, monstres seulement, M014) :
/// repères de mouvement, interprétés selon la famille du nom de
/// séquence : move* → pas, attack/shoot/trigger/magic → impact (sauf
/// s'il existe un « hit » explicite), sinon repère.</item>
/// <item><c>hit</c> (1 : M014 attack02 à 0,315 s, juste après m1 0,301).</item>
/// <item><c>s1</c>, <c>e1</c>, <c>e2</c> (1 séquence : M014 shoot01) :
/// lues comme son / effet (hypothèse).</item>
/// <item>Métadonnées : « -GlobalRatio 100 -GlobalCompressFloats … »
/// (14, séquences vides M613-M624, M134/M136 EMPTY_SEQUENCE_AT_0),
/// « morph: unknown » (1 : m354 battle01).</item>
/// </list>
/// Les .kf des 7 corps de personnages n'ont <b>que</b> start/end : les
/// temps d'impact des joueurs viennent de <c>motion.ini</c>
/// (<see cref="EdenClient.Combat.MotionTable"/>), pas du .kf.
/// </summary>
public static class AnimTextKeys
{
    /// <summary>Famille d'une séquence : lettres du nom avant les
    /// chiffres, minuscules (« attack01 » → « attack »).</summary>
    public static string Family(string seqName)
    {
        var i = 0;
        while (i < seqName.Length && !char.IsDigit(seqName[i])) i++;
        return seqName[..i].ToLowerInvariant();
    }

    private static bool IsCombat(string family) =>
        family is "attack" or "shoot" or "trigger" or "magic" or "rattack"
            or "rshoot" or "rmagic";

    private static bool IsLocomotion(string family) =>
        family is "move" or "run" or "walk" or "rmove";

    /// <summary>Classe une clé seule (sans contexte de séquence : « m# »
    /// → <see cref="AnimEventKind.Marker"/>).</summary>
    public static AnimEventKind Classify(string key, out int index) =>
        Classify("", key, false, out index);

    /// <summary>Classe une clé dans le contexte du nom de séquence ;
    /// <paramref name="seqHasHit"/> = la séquence a déjà un « hit » explicite.</summary>
    public static AnimEventKind Classify(string seqName, string key,
        bool seqHasHit, out int index)
    {
        index = 0;
        var k = key.Trim();
        if (k.Equals("start", StringComparison.OrdinalIgnoreCase))
            return AnimEventKind.Start;
        if (k.Equals("end", StringComparison.OrdinalIgnoreCase))
            return AnimEventKind.End;
        if (k.Equals("hit", StringComparison.OrdinalIgnoreCase))
            return AnimEventKind.Hit;
        if (k.StartsWith("-Global", StringComparison.Ordinal) ||
            k.StartsWith("morph:", StringComparison.OrdinalIgnoreCase))
            return AnimEventKind.Metadata;
        if (k.Length >= 2 && char.IsAsciiLetter(k[0]) &&
            int.TryParse(k.AsSpan(1), out index) && index >= 0)
        {
            switch (char.ToLowerInvariant(k[0]))
            {
                case 'm':
                    var fam = Family(seqName);
                    if (IsLocomotion(fam)) return AnimEventKind.Footstep;
                    if (IsCombat(fam) && !seqHasHit) return AnimEventKind.Hit;
                    return AnimEventKind.Marker;
                case 's': return AnimEventKind.Sound;
                case 'e': return AnimEventKind.EffectSpawn;
            }
        }
        index = 0;
        return AnimEventKind.Unknown;
    }

    /// <summary>Événements d'une séquence, triés par temps (stable),
    /// temps relatifs à <see cref="KfSequence.Start"/>.</summary>
    public static List<AnimEvent> Build(KfSequence seq) =>
        Build(seq.Name, seq.TextKeys, seq.Start);

    public static List<AnimEvent> Build(string seqName,
        IReadOnlyList<KfTextKey> keys, float start = 0f)
    {
        var hasHit = keys.Any(k =>
            k.Value.Trim().Equals("hit", StringComparison.OrdinalIgnoreCase));
        var l = new List<(AnimEvent e, int ord)>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            var kind = Classify(seqName, keys[i].Value, hasHit, out var idx);
            l.Add((new AnimEvent(keys[i].Time - start, kind, keys[i].Value, idx), i));
        }
        return l.OrderBy(x => x.e.Time).ThenBy(x => x.ord)
            .Select(x => x.e).ToList();
    }

    /// <summary>Premier événement du genre demandé ou null.</summary>
    public static AnimEvent? First(IReadOnlyList<AnimEvent> events,
        AnimEventKind kind)
    {
        foreach (var e in events)
            if (e.Kind == kind) return e;
        return null;
    }

    /// <summary>Ajoute à <paramref name="into"/> les événements franchis
    /// dans la fenêtre <c>(from, to]</c> (temps de séquence non repliés,
    /// cf. <see cref="KfmStep"/>) ; pour un clip bouclant, les
    /// événements se répètent à chaque tour de <paramref name="duration"/>.
    /// Les événements au-delà de la durée ne se déclenchent jamais.
    /// <c>Start</c> à t=0 se déclenche à la fenêtre qui part de 0 exclu :
    /// utiliser <paramref name="includeFrom"/> au 1er pas d'un clip.</summary>
    public static void Collect(IReadOnlyList<AnimEvent> events,
        float duration, bool loops, float from, float to,
        List<AnimEvent> into, bool includeFrom = false)
    {
        if (to < from || events.Count == 0) return;
        if (!loops || duration <= 1e-6f)
        {
            Scan(events, 0f, duration, from, to, includeFrom, into);
            return;
        }
        var k0 = (int)MathF.Floor(from / duration);
        var k1 = (int)MathF.Floor(to / duration);
        for (var k = k0; k <= k1; k++)
            Scan(events, k * duration, duration, from, to,
                includeFrom && k == k0, into);
    }

    private static void Scan(IReadOnlyList<AnimEvent> events, float offset,
        float duration, float from, float to, bool includeFrom,
        List<AnimEvent> into)
    {
        foreach (var e in events)
        {
            if (e.Time > duration + 1e-6f || e.Time < 0f) continue;
            var t = offset + e.Time;
            if ((t > from || (includeFrom && t >= from)) && t <= to)
                into.Add(e with { Time = t });
        }
    }
}
