using System.Collections.Concurrent;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Social;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.World.Handlers;

/// <summary>
/// Recherche « entraide » (onglet communauté) — <c>CNE_CW_ServerPlayerState</c>
/// (NCID 27) enregistre l'état du joueur ; <c>CNE_CW_ServerMutuallyHelpSearch</c>
/// (NCID 33) renvoie <c>CNC_CW_ClientMutuallyHelpUpdate</c> (NCID 81).
///
/// Fil natif (désasm world/mission) :
/// - 27 <c>[u8 state]</c> → <c>CPlayerEntity::SetPlayerState</c> écrit le
///   byte à SCharacter+0x80 ; la mission indexe l'exécutant dans
///   <c>AllMutuallyHelpSection[monde×10+secteur]</c> tant que le flag
///   exec+0xe2 (état non nul) tient (<c>UpdateMutuallyHelpState</c>).
/// - 33 <c>[s16 secteur]</c> → le monde relaie
///   <c>WorldMutuallyHelpSearchList</c> à la mission, qui pioche jusqu'à
///   50 candidats <b>aléatoires</b> en ligne (<c>random() % count</c>,
///   cap <c>local_a8 = 0x32</c>) et renvoie le 81.
/// Ici monde et mission partagent le même processus : le registre vit
/// directement dans ce handler, pas besoin du relais texte.
/// </summary>
public static class MutuallyHelpHandlers
{
    /// <summary>charId → byte d'état non nul = inscrit à l'entraide
    /// (équivalent du bit exec+0xe2 natif).</summary>
    private static readonly ConcurrentDictionary<int, byte> _registered =
        new();

    /// <summary>Dépendances injectées par Program.cs.</summary>
    public sealed class Services
    {
        /// <summary>Cartes joueurs (nom, niveau, classe, node…).</summary>
        public required CharacterRepository Characters { get; init; }
        /// <summary>Id monde écrit dans le champ channel du 81
        /// (<c>CGlobalInfo::WorldServerID</c> natif).</summary>
        public ushort WorldServerId { get; init; } = 1;
    }

    /// <summary>Retire le perso du registre — appelé à la déconnexion
    /// monde (natif : UpdateMutuallyHelpState(char,-1) dans SetOnline /
    /// RemoveCharacter).</summary>
    public static void Unregister(int charId) => _registered.TryRemove(charId, out _);

    /// <summary>Enregistre les deux commandes CW du panneau entraide.</summary>
    public static void Register(PacketDispatcher d, Services svc)
    {
        // NCID 27 PlayerState [u8] — l'état complet du joueur ; non nul
        // = candidat entraide (exec+0xe2 != 0 côté mission natif).
        // Aucune réponse client (le natif relaie juste à la mission).
        d.Register(() => new CNE_CW_ServerPlayerState(),
            (session, msg, token) =>
            {
                if (session.CharacterId is not { } charId)
                    return Task.CompletedTask;
                if (msg.Value == 0) _registered.TryRemove(charId, out _);
                else _registered[charId] = msg.Value;
                return Task.CompletedTask;
            });

        // NCID 33 MutuallyHelpSearch [s16 secteur] — le natif ignore le
        // secteur côté monde (GetMutuallyHelpLevelSector → 0/3 seuls) :
        // il sert jusqu'à 50 inscrits en ligne au hasard, requérant exclu.
        d.Register(() => new CNE_CW_ServerMutuallyHelpSearch(),
            async (session, msg, token) =>
            {
                if (session.CharacterId is not { } me) return;
                var candidates = _registered.Keys
                    .Where(id => id != me &&
                                 ZoneLinkServer.ClientSessions.ContainsKey(id))
                    .ToList();
                // Tirage natif random() % count, cap 50 (local_a8=0x32).
                if (candidates.Count > SocialPackets.SearchResultCap)
                {
                    var rng = new Random();
                    candidates = candidates
                        .OrderBy(_ => rng.Next())
                        .Take(SocialPackets.SearchResultCap).ToList();
                }
                var cards = await svc.Characters.GetPlayerCardsAsync(
                    candidates.ToArray());
                var entries = cards.Select(c => new SocialPackets.MutuallyHelpEntry(
                    c.CharId, c.Name, c.Level,
                    (byte)c.ClassId, (byte)c.ClassLv, c.NodeId,
                    (short)svc.WorldServerId, 1)).ToList();
                await session.SendPacketAsync(
                    SocialPackets.NcidClientMutuallyHelpUpdate,
                    SocialPackets.BuildMutuallyHelpUpdate(entries), token);
            });
    }
}
