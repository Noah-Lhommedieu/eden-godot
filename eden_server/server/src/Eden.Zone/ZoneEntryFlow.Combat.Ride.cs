// Portée : annulation du ride à l'engagement — règle native du
// désasm @0x84f08c3 (chemin d'attaque) : un perso monté qui entre
// en combat perd son enchant de ride SAUF si la monture est
// « de combat » — marqueur natif char+0x824 ≠ 0 (arg4 de la
// commande 2134, « 戰鬥座騎 ») ou barre de ride-spells non vide
// (2140). RemoveEnchantByState(9) natif → notre séquence de
// dismount complète (EnchantRemove + 283 mask2 + 545 vide +
// refresh stats via l'étape « ride » du pipeline).

using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Handlers;

namespace Eden.Zone;

/// <summary>Dismount automatique à l'entrée en combat + notification
/// du mot d'état (302 ride bit9).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Mot d'état complet du joueur (natif <c>char+0x3a</c>)
    /// recomposé depuis les flags suivis côté serveur — lecture
    /// équivalente de celle de <c>NodifyFlagChange</c>.</summary>
    private uint PlayerStateWord(EntryState st) =>
        StateFlagWord.Compose(
            dead: st.Dead,
            mounted: _services.RideStates.ContainsKey((int)st.CharId),
            inBattle: st.InCombat,
            actionChain: st.ActionChain);

    /// <summary>302 <c>ClientFlagModify</c> du mot d'état courant —
    /// miroir de <c>CStateController::NodifyFlagChange</c> natif
    /// @0x084e3c00 : <b>BroadCastToCanSee</b> (témoins ayant le char
    /// en vue) <b>+ NotifyClient</b> — le cavalier reçoit SON propre
    /// mot ; c'est ce 302 qui pose <c>player+0x3c</c> bit9 chez
    /// l'acteur et déclenche la sélection des records de strafe
    /// monture (pivot ±90°) côté client. À appeler APRÈS la mutation
    /// de <see cref="ZoneServices.RideStates"/> — le mot reflète
    /// l'état courant, monté ou non.</summary>
    private async Task NotifyFlagChangeAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var cmd = StateFlagWord.BuildCommand(st.CharId,
            PlayerStateWord(st), st.NextAction());
        await session.SendAsync(cmd, token);            // NotifyClient
        await BroadcastToNodeAsync(session, st, cmd, token); // CanSee
    }

    /// <summary>Montée — point d'entrée boucle de
    /// <see cref="ZoneServices.RideApply"/> : l'état de ride natif
    /// (char+0x7f4 = serial de l'item monture) est posé, puis le
    /// CEnchant de ride est appliqué comme un vrai statut
    /// (persisté, restauré à la migration — `EffectToLife` de
    /// l'enchant 2134 de l'item, DoUseItem @0x080b84xx). Le 299 et
    /// les notifications ride (302 bit9 / 283 / 545) sont émis par
    /// <see cref="AddPlayerEnchantAsync"/> via le hook 2134.</summary>
    private async Task ApplyRideEnchantAsync(NetSession session,
        EntryState st, int itemSerial, int enchantId,
        CancellationToken token)
    {
        _services.RideStates[(int)st.CharId] = (itemSerial, enchantId);
        // rest -1 = infini (CEnchant+0x20 des montures) ; persisté
        // dans player_enchants comme tout CEnchant natif → le statut
        // survit à la migration et à la reconnexion.
        await AddPlayerEnchantAsync(session, st, (int)st.CharId,
            enchantId, -1, 1, token, rideItemSerial: itemSerial);
    }

    /// <summary>Politique de restauration d'un enchant persisté à
    /// l'entrée. Un enchant de ride (commande 2134) n'est rejoué que
    /// si le perso est DÉJÀ monté côté serveur (RideStates, qui survit
    /// au portail/changement de map dans le process) ; à une connexion
    /// à froid (RideStates vidé à la déconnexion) le perso arrive à
    /// pied — le natif ne remonte pas après un login.</summary>
    internal static bool ShouldRestoreEnchant(bool isRideEnchant,
        bool mountedInSession) => !isRideEnchant || mountedInSession;

    /// <summary>true = restaurer ; sinon la ligne ride est purgée.</summary>
    private async Task<bool> FilterRideRestoreAsync(int charId, int eid)
    {
        var isRide = _services.Enchants.GetRideInfo(eid) != null;
        if (ShouldRestoreEnchant(isRide,
                _services.RideStates.ContainsKey(charId)))
            return true;
        await _services.PlayerEnchants.DeleteEnchantAsync(charId, eid);
        EdenLog.Write($"[Ride] char={charId} enchant={eid} : connexion " +
            "à froid — monture non restaurée (ligne purgée)");
        return false;
    }

    /// <summary>Ré-application de l'état de ride quand un enchant
    /// portant la commande 2134 est (re)posé — `CEC_Ride::Execute`
    /// natif → `SetState` @0x0854f330 : peuple char+0x7f0 (rideKey)
    /// /+0x824 (type combat), LifeEntityDo(9) (302 self+cansee),
    /// ClientModelModify mask=2 (283) puis la barre RideSpell (545).
    /// Chez nous : <see cref="ZoneServices.RideStates"/> + mêmes
    /// paquets. Le serial de l'item (char+0x7f4) est fourni au
    /// montage ; à la restauration (migration/relog) il est résolu
    /// en scannant l'inventaire — item absent → pas de ride (le
    /// CheckRide natif retire le ride quand l'item quitte son sac).
    /// </summary>
    private async Task ReapplyRideStateAsync(NetSession session,
        EntryState st, int eid, EnchantService.RideInfo info,
        int itemSerial, CancellationToken token)
    {
        var charId = (int)st.CharId;
        if (itemSerial <= 0)
            itemSerial = await ResolveRideItemSerialAsync(charId, eid);
        if (itemSerial <= 0)
        {
            // L'item monture n'est plus en sac — l'enfant restauré
            // ne doit PAS remonter le perso (CheckRide natif :
            // char+0x7f4 = 0 quand le clone n'existe plus).
            _services.RideStates.TryRemove(charId, out _);
            EdenLog.Write($"[Ride] char={charId} enchant={eid} : " +
                "item monture introuvable — état ride non restauré");
            return;
        }
        _services.RideStates[charId] = (itemSerial, eid);
        // 302 : bit9 « mounted » (LifeEntityDo(9) → NodifyFlagChange
        // natif @0x084e3c00) — l'acteur et le CanSee reçoivent le mot.
        await NotifyFlagChangeAsync(session, st, token);
        // 283 mask=2 — modèle de monture (SetState natif émet
        // NotifyClient + BroadCastToCanSee dans le même ordre).
        await ItemActionHandlers.SendRideModelAsync(_services,
            session, charId, info.RideKey, info.Flag & 1, info.Seats,
            token);
        // 545 — barre de sorts de monture (CEC_RideSpell::Execute
        // natif @0x8550a80 : spellmasters de la commande 2140).
        if (info.CombatBuffs.Count > 0)
        {
            var rs = new OutBinStream();
            rs.WriteShort((short)info.CombatBuffs.Count);
            foreach (var sid in info.CombatBuffs)
                rs.WriteShort((short)sid);
            await session.SendPacketAsync(545, rs.ToArray(), token);
        }
    }

    /// <summary>Renvoie au seul client migrant le modèle de monture
    /// (283 mask=2) et la barre de ride-spells (545) — le ride lui-même
    /// (RideStates, enchant persisté) survit au changement de node.
    /// Sans effet à pied.</summary>
    private async Task ResendRideAfterMigrationAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var charId = (int)st.CharId;
        if (!_services.RideStates.TryGetValue(charId, out var ride) ||
            _services.Enchants.GetRideInfo(ride.EnchantId) is not { } info)
            return;
        await ItemActionHandlers.SendRideModelAsync(_services, session,
            charId, info.RideKey, info.Flag & 1, info.Seats, token,
            selfOnly: true);
        if (info.CombatBuffs.Count > 0)
        {
            var rs = new OutBinStream();
            rs.WriteShort((short)info.CombatBuffs.Count);
            foreach (var sid in info.CombatBuffs)
                rs.WriteShort((short)sid);
            await session.SendPacketAsync(545, rs.ToArray(), token);
        }
    }

    /// <summary>Serial de l'item monture en sac — recherche par
    /// `useEnchant == eid` dans les sacs du perso (le natif stocke
    /// clone+0x4 à l'usage ; à la restauration on retrouve l'item
    /// par son enchant d'usage).</summary>
    private async Task<int> ResolveRideItemSerialAsync(int charId,
        int eid)
    {
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync(charId);
        foreach (var it in items)
        {
            if (it.IsEquipped) continue;
            var def = _services.GameData.Items
                .FirstOrDefault(i => i.Id == it.ItemId);
            if (def?.UseEnchantId == eid) return it.Id;
        }
        return 0;
    }

    /// <summary>Séquence complète de démontage — miroir du retrait
    /// natif du ride (CancelPlayerEnchant → RemoveEnchantByID →
    /// ClientEnchantRemove + `CEC_Ride::SetState(0)` + vidage de la
    /// barre RideSpell). Appelée par l'auto-dismount au combat et par
    /// l'annulation clic-droit (NCID 85, chemin CheckRideSpellBuff —
    /// l'enchant de ride n'est annulable que pendant qu'on le monte).
    /// Ne fait rien si le perso n'est pas monté. Quand le ride vit
    /// comme un vrai statut (cas normal), tout passe par
    /// <see cref="RemovePlayerEnchantAsync"/> : 300 + cansee + 372 +
    /// delete DB + rollback verrous/stats.</summary>
    private async Task DismountAsync(NetSession session, EntryState st,
        int charId, CancellationToken token)
    {
        if (!_services.RideStates.TryRemove(charId, out var ride))
            return;
        var eid = (int)(short)ride.EnchantId & 0xffff;
        if (st.Statuses.ContainsKey(eid))
        {
            // RemoveEnchantByID natif : 300 + broadcast + purge
            // player_enchants + refresh stats (l'étape « ride » du
            // pipeline ne compte plus — RideStates déjà vidé).
            await RemovePlayerEnchantAsync(session, st, eid, token);
        }
        else
        {
            // Ride hérité d'une session pré-statut : 300 manuel +
            // purge DB (no-op si rien n'a été écrit).
            var rm = new CNC_CZ_ClientEnchantRemove
            {
                Field1 = charId,
                Field2 = (short)ride.EnchantId, // s16 : enchant ride
                Field3 = 0, Field4 = 0, Field5 = 0,
            };
            await session.SendAsync(rm, token);
            await _services.Sessions.BroadcastExceptAsync(session, rm,
                token);
            await _services.PlayerEnchants.DeleteEnchantAsync(charId,
                ride.EnchantId);
            await ProfileStatEmitter.SendForCharacterAsync(session,
                _services, charId, token);
        }
        // 302 : bit9 « mounted » retiré du mot d'état — natif
        // `LifeEntityRemoveDo(9,1)` → NodifyFlagChange AVANT le 283.
        await NotifyFlagChangeAsync(session, st, token);
        // 283 mask=2 rideKey=0 — retrait du modèle de monture
        // (CEC_Ride::SetState(0) natif @0x854f4c0).
        await ItemActionHandlers.SendRideModelAsync(_services,
            session, charId, 0, 0, 0, token);
        // Barre de sorts de monture vidée (RideSpell::RollBack).
        await session.SendPacketAsync(545, new byte[] { 0, 0 }, token);
    }

    /// <summary>Retire le ride si le perso attaque sur une monture
    /// NON-combat — mêlée ou sort hostile. Ne fait rien si le perso
    /// n'est pas monté ou si sa monture est combat-flaggée.</summary>
    private async Task TryAutoDismountForCombatAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var charId = (int)st.CharId;
        if (!_services.RideStates.TryGetValue(charId, out var ride))
            return;
        var info = _services.Enchants.GetRideInfo(ride.EnchantId);
        // Monture de combat native : type≠0 (char+0x824) ou sorts de
        // monture (char+0x8c+0x160..0x164 non vide) → le ride survit
        // à l'engagement.
        if (info != null &&
            (info.RideType != 0 || info.CombatBuffs.Count > 0))
            return;

        await DismountAsync(session, st, charId, token);
        EdenLog.Write($"[Zone] auto-dismount char={charId} " +
            $"enchant={ride.EnchantId} (monture non-combat)");
    }
}
