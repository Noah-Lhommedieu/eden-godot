using System;

namespace EdenClient.Data;

/// <summary>Texte des annonces monde world boss / mutants (plan
/// futur-feature/12), côté client. Le serveur envoie un 316
/// <c>ClientTextOutputIndex</c> <c>[s16 index][str arg]</c> :
/// <list type="bullet">
/// <item>index natifs <c>t_textindex</c> 6044-6053 (« Le Pouvoir de
///   Destruction a infecté … ») et 1647 (« %s1 explosera à la fin du
///   compte à rebours ») : texte résolu par <see cref="TextResolver"/>
///   (FR prioritaire, repli EN) ;</item>
/// <item>indices d'extension 30001-30003 (kill boss, apparition/mort
///   d'un mutant) : gabarits <c>UiTextEn</c> FR/EN, arguments séparés
///   par TAB (id monstre, nom du tueur).</item>
/// </list>
/// Les annonces sont aussi affichées en bandeau d'écran (en plus du
/// chat système).</summary>
public static class WorldBossText
{
    /// <summary>Kill d'un world boss fixe — arg « mobId TAB tueur ».</summary>
    public const short ExtKillAnnounce = 30001;
    /// <summary>Apparition d'un mutant — arg « mobId ».</summary>
    public const short ExtMutantSpawn = 30002;
    /// <summary>Mort d'un mutant — arg « mobId TAB tueur ».</summary>
    public const short ExtMutantKilled = 30003;
    /// <summary>Index natif « %s1 explosera à la fin du compte à rebours ».</summary>
    public const short CountdownIndex = 1647;
    /// <summary>Premier/dernier index natif d'annonce « Collectionneur
    /// d'Énergie » (S628-640, col19 de reborn_monsters).</summary>
    public const short EnergyFirst = 6044, EnergyLast = 6053;

    /// <summary>Gabarits EN canoniques (clés du TSV FR).</summary>
    public const string KillTemplate = "{0} has been slain by {1}!";
    /// <summary>Gabarit d'apparition d'un mutant.</summary>
    public const string MutantSpawnTemplate =
        "A mutant monster, {0}, has appeared somewhere in the world!";
    /// <summary>Gabarit de mort d'un mutant.</summary>
    public const string MutantKilledTemplate =
        "The mutant monster {0} has been defeated by {1}!";

    /// <summary>Vrai si l'index relève des annonces monde (bandeau).</summary>
    public static bool IsAnnouncement(short index) =>
        index is ExtKillAnnounce or ExtMutantSpawn or ExtMutantKilled
            or CountdownIndex ||
        index is >= EnergyFirst and <= EnergyLast;

    /// <summary>Vrai pour un index d'extension Eden (hors t_textindex).</summary>
    public static bool IsExtension(short index) =>
        index is >= ExtKillAnnounce and <= ExtMutantKilled;

    /// <summary>Compose le texte final. <paramref name="monsterName"/>
    /// résout un id monstre (nom court FR/EN), <paramref name="spellName"/>
    /// un id de sort, <paramref name="textIndex"/> un index natif +
    /// argument ; <paramref name="translate"/> applique le gabarit
    /// UiTextEn. Retourne null si l'index n'est pas géré ici.</summary>
    public static string? Compose(short index, string arg,
        Func<int, string?> monsterName, Func<int, string?> spellName,
        Func<int, string, string?> textIndex, Func<string, string> translate)
    {
        switch (index)
        {
            case ExtKillAnnounce:
            {
                var (id, who) = Split(arg);
                return string.Format(translate(KillTemplate),
                    MonsterLabel(id, monsterName), who);
            }
            case ExtMutantSpawn:
                return string.Format(translate(MutantSpawnTemplate),
                    MonsterLabel(Split(arg).Id, monsterName));
            case ExtMutantKilled:
            {
                var (id, who) = Split(arg);
                return string.Format(translate(MutantKilledTemplate),
                    MonsterLabel(id, monsterName), who);
            }
            case CountdownIndex:
            {
                // arg = id de sort (le serveur n'a pas les noms FR) ;
                // texte libre accepté tel quel.
                var spell = int.TryParse(arg, out var sid)
                    ? spellName(sid) ?? arg : arg;
                return textIndex(CountdownIndex, spell);
            }
            default:
                return textIndex(index, arg);
        }
    }

    private static (int Id, string Who) Split(string arg)
    {
        var p = arg.Split('\t', 2);
        _ = int.TryParse(p[0], out var id);
        return (id, p.Length > 1 ? p[1] : "?");
    }

    private static string MonsterLabel(int id, Func<int, string?> name) =>
        name(id) is { Length: > 0 } n ? n : "#" + id;
}
