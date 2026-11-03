namespace EdenClient.Data;

/// <summary>
/// Icône UI (<c>UI/uiicon/Jxxx</c>) des classes éveillées. Dans
/// <c>C_Class.ini</c> (col 3) la plupart des classes éveillées (21–61)
/// pointent vers les glyphes « ? » génériques J069–J073 (placeholders
/// de classes « non encore illustrées » — c'est ce qu'affiche aussi la
/// ref ClassePanel pour Kageryu/Equilibrian). Les illustrations
/// existent pourtant dans <c>uiicon</c> (J074–J104, J662/J663…) ;
/// cette table associe à chaque placeholder le glyphe thématique le
/// plus proche (assimilation visuelle : couleur de la branche +
/// motif) — shortcut: choix sémantique à valider contre les fichiers
/// d'icônes wiki si l'on retrouve la correspondance native.
/// Clé = id canonique 1–61 (race ignorée).
/// </summary>
public static class ClassIconMap
{
    /// <summary>Codes « ? » génériques de C_Class (placeholders).</summary>
    private static readonly HashSet<string> Placeholders = new(
        StringComparer.OrdinalIgnoreCase)
        { "J069", "J070", "J071", "J072", "J073" };

    private static readonly Dictionary<int, string> Override = new()
    {
        [21] = "J010",   // Berserker — hache dorée
        [23] = "J065",   // Arch-Elementalist — foudre/feu/glace violets
        [25] = "J078",   // Trainer — tête de lion verte
        [28] = "J081",   // Equilibrian — baguette ailée bleue
        [30] = "J090",   // Kage — shuriken orange
        [31] = "J062",   // Battlefield Poet — note bleue
        [34] = "J075",   // Glacier Knight — heaume doré
        [36] = "J060",   // Annihilator — explosion
        [37] = "J076",   // Asura — démon à bras multiples
        [39] = "J082",   // Demon Summoner — démon sombre violet
        [41] = "J056",   // Blade Acrobat — pétales tourbillonnants
        [42] = "J055",   // Elegant Dancer — rose
        [44] = "J096",   // Totem Master — masque tribal
        [45] = "J104",   // Blood Knight — chevalier sombre
        [46] = "J092",   // Paladin — croix/marteau
        [47] = "J097",   // Lethal Arrow — arc vert
        [48] = "J098",   // Celestial Arrow — arc vert (ailé)
        [49] = "J088",   // Demon Tamer — crâne vert
        [52] = "J085",   // Master Fencer — sabre orange
        [53] = "J099",   // Holy Lamentor — orante
        [54] = "J100",   // Litigator — chevalier ailé
        [55] = "J662",   // Dragon Soul — dragon doré
        [56] = "J663",   // Dragon Emperor — dragon sombre
        [57] = "J093",   // Darkness Blade — œil pentagramme
        [58] = "J068",   // Holy Blade — éclat violet
        [61] = "J059",   // Rifleeter — lance-roquette
    };

    /// <summary>Code d'icône à utiliser pour <paramref name="classId"/>
    /// (id composite race×100+n accepté) ; <paramref name="tableCode"/>
    /// = code de C_Class. Un code réel de la table est conservé.</summary>
    public static string Code(int classId, string tableCode)
    {
        var local = classId % 100;
        if (local >= 21 &&
            (tableCode.Length == 0 || Placeholders.Contains(tableCode)) &&
            Override.TryGetValue(local, out var code))
            return code;
        return tableCode;
    }
}
