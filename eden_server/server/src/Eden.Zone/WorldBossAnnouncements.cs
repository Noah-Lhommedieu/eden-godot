using Eden.Core.Game.WorldBoss;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Zone;

/// <summary>Constructeurs purs (testables) des annonces monde des
/// world boss / mutants (plan futur-feature/12). Tout passe par
/// <c>CNC_CZ_ClientTextOutputIndex</c> (NCID 316, <c>[s16 index]
/// [str arg]</c>) — le chemin natif de
/// <c>CZoneServer::BroadCastMessageToAllCharacter(short,…)</c>
/// @0x08177b50 — pour que le client résolve le texte dans SA langue
/// (FR/EN) via <c>t_textindex</c> / gabarits <c>UiTextEn</c>.</summary>
public static class WorldBossAnnouncements
{
    /// <summary>Annonce d'apparition lue en scène (col19 de la ligne
    /// reborn_monsters, ex. 6044 « Le Pouvoir de Destruction a infecté
    /// … »). Null si la ligne n'annonce rien (world boss fixes).</summary>
    public static CNC_CZ_ClientTextOutputIndex? Spawn(int announceTextId) =>
        announceTextId > 0
            ? new CNC_CZ_ClientTextOutputIndex
            { Field1 = (short)announceTextId, Field2 = "" }
            : null;

    /// <summary>Annonce de mort d'un world boss fixe (extension Eden
    /// 30001) : arg = « mobId TAB nom du tueur ». Null si l'id n'est
    /// pas un boss fixe.</summary>
    public static CNC_CZ_ClientTextOutputIndex? Kill(int mobId,
        string killerName) =>
        WorldBossCatalog.IsWorldBoss(mobId)
            ? new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = WorldBossCatalog.ExtKillAnnounce,
                Field2 = mobId + WorldBossCatalog.ArgSeparator.ToString()
                    + killerName,
            }
            : null;

    /// <summary>Annonce monde d'apparition d'un mutant — « sans
    /// précision de canal ni de zone » (guide) : arg = id seul.</summary>
    public static CNC_CZ_ClientTextOutputIndex? MutantSpawn(int mobId) =>
        WorldBossCatalog.MutantIdIndex(mobId) >= 0
            ? new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = WorldBossCatalog.ExtMutantSpawn,
                Field2 = mobId.ToString(),
            }
            : null;

    /// <summary>Annonce de mort d'un mutant : arg = « mobId TAB tueur ».</summary>
    public static CNC_CZ_ClientTextOutputIndex? MutantKilled(int mobId,
        string killerName) =>
        WorldBossCatalog.MutantIdIndex(mobId) >= 0
            ? new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = WorldBossCatalog.ExtMutantKilled,
                Field2 = mobId + WorldBossCatalog.ArgSeparator.ToString()
                    + killerName,
            }
            : null;

    /// <summary>Avertissement « %s1 explosera à la fin du compte à
    /// rebours » (index natif 1647) pour un sort long de boss — arg =
    /// id du sort (le client en résout le nom FR/EN). Null sous le seuil de cast ou hors boss.</summary>
    public static CNC_CZ_ClientTextOutputIndex? CastCountdown(int mobId,
        string spellArg, int castTimeMs, int thresholdMs) =>
        castTimeMs >= thresholdMs &&
        WorldBossCatalog.KindOf(mobId) != BossKind.None
            ? new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = WorldBossCatalog.NativeCountdownText,
                Field2 = spellArg,
            }
            : null;

    /// <summary>Un récepteur de courrier de mutant : charId, item,
    /// quantité (durability du mailitem).</summary>
    public sealed record MutantMail(int CharId, int ItemId, int Amount);

    /// <summary>Courriers de récompense d'un mutant tué : chaque
    /// frappeur reçoit des « Broken Red Crystal Shard » (guide :
    /// « les joueurs qui les frappent reçoivent par messagerie des
    /// petits lots », quantité fonction du niveau). Vide si l'id
    /// n'est pas un mutant.</summary>
    public static IReadOnlyList<MutantMail> MutantRewards(int mobId,
        IEnumerable<int> attackers)
    {
        var amount = WorldBossCatalog.MutantShardAmount(mobId);
        if (amount <= 0) return Array.Empty<MutantMail>();
        return attackers.Distinct()
            .Select(c => new MutantMail(c,
                WorldBossCatalog.MutantShardItemId, amount))
            .ToList();
    }
}
