using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Échange P2P : request/accept/add/remove/gold/confirm/reset/cancel —
/// notifications croisées aux deux parties.
/// </summary>
public static class TradeHandlers
{
    /// <summary>Rate-limit natif `char+0x650` : 0.5 s entre deux
    /// demandes d'échange par personnage.</summary>
    private static readonly System.Collections.Concurrent
        .ConcurrentDictionary<int, DateTime> _lastTradeRequest = new();

    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // TradeRequest — désasm CPlayerFactory::RequestTrade
        // (zone:138188) : rate-limit 0.5 s (char+0x650), cible sur le
        // même node, CloseEnough(cible, 10.0). Trop loin →
        // ClientTradeCancel [u32 partenaire][s16 1] au demandeur +
        // texte 0x333. Succès → ClientTradeRequest
        // [u32 demandeur][str nom] envoyé à la CIBLE.
        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeRequest(),
            async (session, msg, charId, token) =>
            {
                var now = DateTime.UtcNow;
                if (_lastTradeRequest.TryGetValue(charId, out var last) &&
                    now - last < TimeSpan.FromMilliseconds(500))
                    return;
                _lastTradeRequest[charId] = now;

                var partnerId = (int)msg.Value;
                var self = s.Entities.Find(charId);
                var partnerEnt = s.Entities.Find(partnerId);
                double distance = 0;
                if (self != null && partnerEnt != null)
                {
                    var dx = self.X - partnerEnt.X;
                    var dy = self.Y - partnerEnt.Y;
                    distance = Math.Sqrt(dx * dx + dy * dy);
                }
                var (success, err) = await s.Trade.RequestTradeAsync(
                    charId, partnerId, distance);
                if (!success)
                {
                    await session.SendAsync(new CNC_CZ_ClientTradeCancel
                    {
                        Field1 = (uint)partnerId, // u32 : partenaire visé
                        Field2 = 1,               // s16 : échec (1 natif)
                    }, token);
                    if (distance >
                        Eden.Core.Game.Services.TradeService
                            .MaxTradeDistance)
                        await session.SendAsync(
                            new CNC_CZ_ClientTextOutputIndex
                            {
                                Field1 = 0x333,   // s16 : « trop loin »
                                Field2 = string.Empty,
                            }, token);
                    EdenLog.Write($"[Zone] TradeRequest refusé : {err}");
                    return;
                }
                if (s.Sessions.FindByCharacter(partnerId) is { } partner)
                {
                    var name = (await s.Characters
                            .GetCharacterByIdAsync(charId))?.GivenName
                        ?? string.Empty;
                    await partner.SendAsync(new CNC_CZ_ClientTradeRequest
                    {
                        Field1 = (uint)charId, // u32 : demandeur
                        Field2 = name,         // str : son nom affiché
                    }, token);
                }
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeRequestAccept(),
            async (session, msg, charId, token) =>
            {
                var initiatorId = (int)msg.Value;
                if (await s.Trade.AcceptTradeAsync(initiatorId, charId))
                    await NotifyBoth(s, session, initiatorId,
                        new CNC_CZ_ClientTradeStart(), token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeAddItem(),
            async (session, msg, charId, token) =>
            {
                // (ushort bag, ushort slot, ushort tradeSlot, ushort count)
                if (await s.Trade.AddItemAsync(charId, 0, msg.Field1, msg.Field2, msg.Field4))
                    await session.SendAsync(new CNC_CZ_ClientTradeAddItem(), token);
                else
                    await session.SendAsync(new CNC_CZ_ClientTradeAddItemFail(), token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeRemoveItem(),
            async (session, msg, charId, token) =>
            {
                if (await s.Trade.RemoveItemAsync(charId, 0, msg.Value))
                    await session.SendAsync(new CNC_CZ_ClientTradeRemoveItem(), token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeModifyGold(),
            async (session, msg, charId, token) =>
            {
                if (await s.Trade.ModifyGoldAsync(charId, 0, msg.Field2))
                    await session.SendAsync(new CNC_CZ_ClientTradeModifyGold(), token);
                else
                    await session.SendAsync(new CNC_CZ_ClientTradeModifyGoldFail(), token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeConfirm(),
            async (session, msg, charId, token) =>
            {
                var partnerId = await s.Trade.GetTradePartnerAsync(charId);
                if (await s.Trade.ConfirmTradeAsync(charId, 0))
                {
                    await NotifyBoth(s, session, partnerId ?? 0,
                        new CNC_CZ_ClientTradeConfirm(), token);
                    // Double confirm → exécution (la ligne
                    // active_trades est supprimée) : le natif émet
                    // alors 340 ClientTradeComplete aux deux —
                    // ferme les fenêtres. Sans lui elles restaient
                    // ouvertes avec les offres figées (gap documenté).
                    if (partnerId != null &&
                        await s.Trade.GetTradePartnerAsync(charId)
                            == null)
                        await NotifyBoth(s, session, partnerId.Value,
                            new CNC_CZ_ClientTradeComplete(), token);
                }
            });

        // TradeReady — les deux parties ont confirmé, exécution effective
        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeReady(),
            async (session, msg, charId, token) =>
            {
                var partnerId = await s.Trade.GetTradePartnerAsync(charId);
                if (partnerId == null) return;
                var ok = await s.Trade.ConfirmTradeAsync(charId, partnerId.Value);
                if (ok)
                    await s.Sessions.SendToAsync(partnerId.Value,
                        new CNC_CZ_ClientTradeReady(), token);
                EdenLog.Write($"[Zone] TradeReady char={charId} <-> {partnerId}");
            });

        // TradeUnConfirm — annule sa confirmation avant le Ready
        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeUnConfirm(),
            async (session, msg, charId, token) =>
            {
                var partnerId = await s.Trade.GetTradePartnerAsync(charId);
                if (partnerId == null) return;
                await s.Sessions.SendToAsync(partnerId.Value,
                    new CNC_CZ_ClientTradeUnConfirm(), token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeCancel(),
            async (session, msg, charId, token) =>
            {
                if (await s.Trade.CancelTradeAsync(charId, 0))
                    await session.SendAsync(new CNC_CZ_ClientTradeCancel(), token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTradeReset(),
            async (session, msg, charId, token) =>
            {
                if (await s.Trade.ResetTradeAsync(charId, 0))
                    await session.SendAsync(new CNC_CZ_ClientTradeReset(), token);
            });
    }

    /// <summary>Envoie un paquet à la session et à l'autre partie du trade.</summary>
    private static async Task NotifyBoth(ZoneServices s, NetSession session,
        int partnerCharId, NetCommand packet, CancellationToken token)
    {
        await session.SendAsync(packet, token);
        if (partnerCharId > 0 &&
            s.Sessions.FindByCharacter(partnerCharId) is { } partner)
            await partner.SendAsync(packet, token);
    }
}
