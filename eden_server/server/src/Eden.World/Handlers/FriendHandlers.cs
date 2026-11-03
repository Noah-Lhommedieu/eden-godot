using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;
using Eden.Core.Game.Social;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.World.Handlers;

/// <summary>
/// Amis côté monde — <c>CNE_CW_ServerAddFriend</c> (12),
/// <c>ServerQueryFriendInfo</c> (13), <c>ServerFriendInvitation</c>
/// (14), <c>ServerRemoveFriend</c> (22) — désasm world.
///
/// Fil natif : le monde relaie vers mission (<c>TC_AddFriend</c> /
/// <c>TC_InviteeFriend</c> / QueryFriendData) qui écrit la table
/// <c>friends</c> puis renvoie
/// <c>CNC_MW_WorldServerUpdateFriendMember</c> ; le monde le traduit
/// en <b>NCID 52 <c>CNC_CW_ClientUpdateFriendMember</c></b> (chunké à
/// 10, 13 champs/record — online dérivé + state brut).
/// Sémantique <c>friend_type</c> natif : 1 = j'ai ajouté, 2 =
/// m'a ajouté (pendante), 3 = mutuel, 0 = blacklist. L'ajout est
/// <b>direct et persistant</b> — pas de popup obligatoire : la cible
/// voit l'entrée type 2 dans son onglet invitations à la prochaine
/// connexion.
/// </summary>
public static class FriendHandlers
{
    /// <summary>Dépendances injectées par Program.cs.</summary>
    public sealed class Services
    {
        public required FriendService Friends { get; init; }
        public required CharacterRepository Characters { get; init; }
        /// <summary>Accès <c>prestige</c> — le QueryFriendInfo natif
        /// (<c>TC_QueryFriend</c> mission @0x081f4d90) pousse aussi
        /// <c>AllPrestigeData</c> (→ 60) et <c>SendSpecialCoinInfo</c>
        /// (→ 78) en plus de la liste d'amis.</summary>
        public required PrestigeRepository Prestige { get; init; }
        /// <summary>Id monde écrit en tête du paquet 52
        /// (<c>CGlobalInfo::WorldServerID</c> natif).</summary>
        public ushort WorldServerId { get; init; } = 1;
    }

    private static Services _svc = null!;

    /// <summary>Enregistre les commandes CW d'amis.</summary>
    public static void Register(PacketDispatcher d, Services svc)
    {
        _svc = svc;

        // NCID 12 AddFriend [str name][s16 type] — TC_AddFriend natif :
        // écrit friends() IMMÉDIATEMENT (cible offline incluse —
        // AddFriendData persiste (cible→moi,2)). type 1 :
        //   réciproque type 1 (eux m'ont déjà ajouté) → 3/3 mutuel
        //   sinon → moi→eux 1, eux→moi 2
        // type 0 → blacklist (moi→eux 0) + push 61.
        d.Register(() => new CNE_CW_ServerAddFriend(),
            async (session, msg, token) =>
            {
                if (session.CharacterId is not { } fromId) return;
                var target = await svc.Characters.GetCharacterByNameAsync(
                    msg.Field1);
                if (target == null || target.Id == fromId) return;

                if (msg.Field2 == 0)
                {
                    // Blacklist — natif AddMember(me, target, 0) ;
                    // si la cible m'avait ajouté (type 2 chez elle),
                    // sa ligne est retirée (RemoveMember).
                    await svc.Friends.SetRelationAsync(
                        fromId, target.Id, 0);
                    if (svc.Friends.GetRelationType(target.Id, fromId) == 2)
                        await svc.Friends.RemoveRelationAsync(
                            target.Id, fromId);
                    await PushBlackListAsync(fromId, token);
                    await PushFriendListAsync(fromId, token);
                    return;
                }
                if (msg.Field2 != 1) return;

                // Déjà une relation (natif : erreur 0x1a7 doublon).
                if (svc.Friends.GetRelationType(fromId, target.Id) >= 0)
                    return;

                // Réciprocité : la cible m'avait déjà ajouté (sa
                // ligne pour moi = 1) → mutuel 3/3. Sinon moi→1,
                // eux→2. Lookup DB + mémoire (cible offline ok).
                var reciprocal = await svc.Friends
                    .GetRelationTypeDbAsync(target.Id, fromId);
                if (reciprocal == 1)
                {
                    await svc.Friends.SetRelationAsync(
                        fromId, target.Id, 3);
                    await svc.Friends.SetRelationAsync(
                        target.Id, fromId, 3);
                }
                else
                {
                    await svc.Friends.SetRelationAsync(
                        fromId, target.Id, 1);
                    await svc.Friends.SetRelationAsync(
                        target.Id, fromId, 2);
                }
                EdenLog.Write(
                    $"[World] AddFriend {fromId} -> {target.Id} " +
                    $"(type={(reciprocal == 1 ? 3 : 1)})");
                // UpdateFriendInfo bilatéral natif — 52 poussé aux
                // deux côtés (la cible offline verra la ligne à son
                // prochain login via QueryFriendInfo).
                await PushFriendListAsync(fromId, token);
                await PushFriendListAsync(target.Id, token);
            });

        // NCID 14 FriendInvitation [i32 fromId][s16 accept] —
        // TC_InviteeFriend natif : accept → si l'invitant a bien une
        // ligne type 1 vers moi → 3/3 mutuel ; sinon même règle que
        // l'ajout direct (moi→1, eux→2). Decline → la ligne de
        // l'invitant vers moi (son type 1) et ma ligne type 2
        // sautent.
        d.Register(() => new CNE_CW_ServerFriendInvitation(),
            async (session, msg, token) =>
            {
                if (session.CharacterId is not { } charId) return;
                var inviterId = msg.Field1;
                if (msg.Field2 != 0)
                {
                    var inviterType = await svc.Friends
                        .GetRelationTypeDbAsync(inviterId, charId);
                    if (inviterType == 1)
                    {
                        await svc.Friends.SetRelationAsync(
                            charId, inviterId, 3);
                        await svc.Friends.SetRelationAsync(
                            inviterId, charId, 3);
                    }
                    else
                    {
                        await svc.Friends.SetRelationAsync(
                            charId, inviterId, 1);
                        await svc.Friends.SetRelationAsync(
                            inviterId, charId, 2);
                    }
                    await PushFriendListAsync(charId, token);
                    await PushFriendListAsync(inviterId, token);
                }
                else
                {
                    // Refus : RemoveMember bilatéral natif.
                    await svc.Friends.RemoveRelationAsync(
                        inviterId, charId);
                    await svc.Friends.RemoveRelationAsync(
                        charId, inviterId);
                    await PushFriendListAsync(charId, token);
                    await PushFriendListAsync(inviterId, token);
                }
            });

        // NCID 22 RemoveFriend [i32 friendId] — natif : RemoveMember
        // des deux sens + UpdateFriendInfo bilatéral. Couvre aussi le
        // retrait d'une invitation pendante ; type 0 → la ligne
        // blacklist saute aussi (déblocage).
        d.Register(() => new CNE_CW_ServerRemoveFriend(),
            async (session, msg, token) =>
            {
                if (session.CharacterId is not { } charId) return;
                var type = svc.Friends.GetRelationType(charId, msg.Value);
                if (type < 0) return;
                await svc.Friends.RemoveRelationAsync(charId, msg.Value);
                await svc.Friends.RemoveRelationAsync(msg.Value, charId);
                if (type == 0)
                    await PushBlackListAsync(charId, token);
                else
                    await PushFriendListAsync(charId, token);
                await PushFriendListAsync(msg.Value, token);
            });

        // NCID 13 QueryFriendInfo [] — liste complète. Natif :
        // QueryFriendData → 52 chunké à 10 + 61 blacklist, puis
        // AllPrestigeData → 60 et SendSpecialCoinInfo → 78
        // (TC_QueryFriend mission @0x081f4d90).
        d.Register(() => new CNE_CW_ServerQueryFriendInfo(),
            async (session, msg, token) =>
            {
                if (session.CharacterId is { } charId)
                {
                    await PushFriendListAsync(charId, token);
                    await PushBlackListAsync(charId, token);
                    await FamilyHandlers.SendPrestigeSyncAsync(
                        session, charId, svc.Prestige, token);
                    await SendSpecialCoinInfoAsync(session, charId,
                        token);
                }
            });
    }

    /// <summary>
    /// À l'entrée d'un perso dans le monde (ServerLoginCharacter) :
    /// pousse la liste 52 aux contacts déjà connectés — leur badge
    /// « en ligne » pour ce perso bascule. Inverse du comportement
    /// natif <c>WorldServerFriendNotify</c> au login.
    /// </summary>
    public static async Task NotifyFriendsOnlineStateAsync(
        int characterId, CancellationToken token = default)
    {
        foreach (var friendId in _svc.Friends.GetFriends(characterId))
            await PushFriendListAsync(friendId, token);
    }

    /// <summary>
    /// Charge les cartes des contacts (types 1/2/3) et émet le/les
    /// paquet(s) 52 vers la session du perso s'il est connecté au
    /// monde — les entrées type 2 alimentent l'onglet invitations.
    /// </summary>
    private static async Task PushFriendListAsync(
        int characterId, CancellationToken token)
    {
        if (!ZoneLinkServer.ClientSessions.TryGetValue(characterId,
                out var session))
            return;
        var friendIds = _svc.Friends.GetFriends(characterId);
        var cards = await _svc.Characters.GetPlayerCardsAsync(
            friendIds.ToArray());
        var byId = cards.ToDictionary(c => c.CharId);
        var entries = new List<SocialPackets.FriendEntry>(friendIds.Count);
        foreach (var fid in friendIds)
        {
            // Carte absente (perso supprimé) → le natif pousse le
            // record avec les détails à −1 (branche « données pas
            // chargées » de UpdateFriendInfo).
            var online = ZoneLinkServer.ClientSessions.ContainsKey(fid);
            var type = (short)_svc.Friends.GetRelationType(characterId, fid);
            if (!byId.TryGetValue(fid, out var c))
            {
                entries.Add(new SocialPackets.FriendEntry(
                    fid, string.Empty, type,
                    -1, -1, -1, -1, -1, string.Empty,
                    Online: 0, ServerId: 0xffff, State: 0xff,
                    IsPublic: 1));
                continue;
            }
            entries.Add(new SocialPackets.FriendEntry(
                c.CharId, c.Name,
                FriendType: type,
                c.Level, c.NodeId, c.RaceId, c.ClassId, c.ClassLv,
                c.FamilyName,
                Online: (byte)(online ? 1 : 0),
                ServerId: (ushort)(online ? _svc.WorldServerId : 0xffff),
                State: (byte)(online ? 0 : 0xff),
                IsPublic: 1));              // défaut natif player_card.is_public=1
        }
        foreach (var body in SocialPackets.BuildFriendMemberPackets(
                     _svc.WorldServerId, entries))
            await session.SendPacketAsync(
                SocialPackets.NcidClientUpdateFriendMember, body, token);
    }

    /// <summary>Émet le paquet 61 — blacklist complète du perso
    /// (ids résolus en noms via les cartes joueurs).</summary>
    private static async Task PushBlackListAsync(
        int characterId, CancellationToken token)
    {
        if (!ZoneLinkServer.ClientSessions.TryGetValue(characterId,
                out var session))
            return;
        var ids = _svc.Friends.GetBlocked(characterId);
        var cards = await _svc.Characters.GetPlayerCardsAsync(
            ids.ToArray());
        await session.SendPacketAsync(
            SocialPackets.NcidClientUpdateBlackList,
            SocialPackets.BuildBlackList(
                cards.Select(c => (c.CharId, c.Name)).ToList()), token);
    }

    /// <summary>Push des mérites spéciaux — équivalent de
    /// <c>CGlobalInfo::SendSpecialCoinInfo</c> natif (mission
    /// @0x08086960, appelé par <c>TC_QueryFriend</c>) : NCID 78
    /// <c>[u16 n][n×(s16 type,i32 total)]</c>, émis même à n=0
    /// (capture native <c>4e000000</c>).</summary>
    private static async Task SendSpecialCoinInfoAsync(
        NetSession session, int charId, CancellationToken token)
    {
        var coins = await _svc.Characters.GetSpecialCoinsAsync(charId);
        var body = new List<byte>(2 + coins.Count * 6);
        body.AddRange(BitConverter.GetBytes(
            (ushort)coins.Count));                    // u16 : nb d'entrées
        foreach (var (type, total) in coins)
        {
            body.AddRange(BitConverter.GetBytes(type));   // s16 : type
            body.AddRange(BitConverter.GetBytes(total));  // i32 : total
        }
        await session.SendPacketAsync(78, body.ToArray(), token);
    }
}
