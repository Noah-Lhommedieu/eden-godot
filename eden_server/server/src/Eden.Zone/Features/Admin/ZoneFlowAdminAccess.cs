using Eden.Core.Game.Inventory;

namespace Eden.Zone;

/// <summary>
/// ADAPTATEUR DE MODULE (pas du code de ZoneEntryFlow) : le module Admin a besoin de l'état de session privé du flow.
/// Points d'entrée de l'administration partagée (PLAN-ACTION-3 D5) : ils exécutent sur la boucle de zone les actions
/// que <c>Eden.Core.Game.Admin</c> demande pour un joueur CONNECTÉ (jamais d'écriture directe en base pour lui : le sac
/// affiché doit suivre). Pour un joueur hors ligne, l'hôte passe par la base.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    internal EntryState? AdminStateOf(Guid sessionId) => _states.TryGetValue(sessionId, out var st) ? st : null;

    /// <summary>Personnages connectés et entrés en zone (id, nom).</summary>
    internal IReadOnlyList<(int CharId, string Name)> AdminOnline() =>
        _states.Values.Where(s => s.Migrated && s.CharId != 0).Select(s => ((int)s.CharId, s.CharName)).ToList();

    /// <summary>Donne un objet à un connecté (même chemin que la remise de quête : 274/291, trésor au sol si sac plein). Faux si hors ligne.</summary>
    internal async Task<bool> AdminGiveOnlineAsync(int charId, int itemId, int quantity)
    {
        if (FindSessionByChar(charId) is not { } session || !_states.TryGetValue(session.SessionId, out var st)) return false;
        await RunOnLoopAsync(_ => GiveQuestItemAsync(session, st, itemId, quantity));
        return true;
    }

    /// <summary>Retire des exemplaires d'un objet à un connecté (consommation notifiée au client). Renvoie null si hors ligne, sinon vrai si la quantité était disponible.</summary>
    internal async Task<bool?> AdminRemoveOnlineAsync(int charId, int itemId, int quantity)
    {
        if (FindSessionByChar(charId) is not { } session || !_states.TryGetValue(session.SessionId, out var st)) return null;
        var def = _services.GameData.Items.FirstOrDefault(d => d.Id == itemId);
        if (def == null) return false;
        var owned = (await _services.InventoryRepo.GetItemsByCharacterIdAsync(charId)).Where(i => i.ItemId == itemId && !i.IsEquipped).ToList();
        if (owned.Sum(i => i.Quantity) < quantity) return false;
        await RunOnLoopAsync(async ct =>
        {
            for (var left = quantity; left > 0;)
            {
                var leaf = (await _services.InventoryRepo.GetItemsByCharacterIdAsync(charId)).FirstOrDefault(i => i.ItemId == itemId && !i.IsEquipped);
                if (leaf == null) break;
                await Handlers.ItemActionHandlers.ConsumeUsedItemAsync(session, _services, charId, leaf, def, 0, (short)leaf.BagId, (short)leaf.Slot, ct);
                left--;
            }
        });
        return true;
    }
}
