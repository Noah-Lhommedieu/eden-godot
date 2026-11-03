// Portée : purge des items à durée expirée à l'entrée en zone —
// CPlayerFactory::CheckItemDueDate natif @0x08120550, appelé par
// CNE_CZ_ZoneServerMigrationDone::Execute @0x0826a330.
// Le natif balaie 3 maps de conteneurs du perso (sacs flag 0,
// conteneur auxiliaire flag 4, équipement flag 1 — le flag 1 est
// prouvé par DropItem param_2==1 : map char+0x4fc, loc ≤ 0x15,
// log "A:EQU"), collecte les clones dont +0x18 (due_date) ≠ 0
// et ≤ now, puis pour chacun :
//   1. notifie le texte système 0x5a4 (« Your %s1 is expired.
//      Unable to use it again. » — S_TextIndex 1444) avec le nom
//      d'item en paramètre ;
//   2. DropItem(char, flag, loc, containerIndex, "SYS", false) —
//      EradicateItem + ClientDropItem 275 [s16 flag][s16 loc]
//      [s16 index] qui vide le slot côté client.
// Sans cette purge les montures « N日 » périmées restent dans le
// sac, affichent « 0 sec. remain » et le clic droit est refusé
// (CheckUseItemCondition rejette due_date < now).

using Eden.Core.Game.Inventory;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Purge des items expirés (see CheckItemDueDate natif).</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Supprime les items dont due_date est échue dans les
    /// sacs et l'équipement, notifie texte 0x5a4 + 275 par item.
    /// Appelé dans MigrationDone AVANT le replay d'inventaire — le
    /// dump 312/274 ne doit pas transporter des lignes mortes.</summary>
    private async Task PurgeExpiredItemsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var charId = (int)st.CharId;

        // Sacs (flag 0 natif) : toutes les poches du perso.
        var bags = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync(charId);
        foreach (var item in bags)
        {
            if (item.DueDate <= 0 || item.DueDate > now) continue;
            await _services.InventoryRepo.RemoveItemAsync(charId,
                item.BagId, item.Slot);
            await NotifyExpiredItemAsync(session, st, item,
                0, token);
        }

        // Équipement (flag 1 natif — map char+0x4fc, param_4 =
        // containerIndex de la page) : sets 0..3.
        var eqPurged = false;
        for (var page = 0; page < 4; page++)
        {
            var eq = await _services.InventoryRepo
                .GetEquipmentEntitiesAsync(charId, page);
            foreach (var item in eq)
            {
                if (item.DueDate <= 0 || item.DueDate > now) continue;
                await _services.InventoryRepo.RemoveEquippedItemAsync(
                    charId, page, item.Slot);
                await NotifyExpiredItemAsync(session, st, item,
                    1, token);
                eqPurged = true;
            }
        }
        // Un équipement expiré change le profil — rejoué une fois
        // (RefreshEQsAttr + NotifyRefreshAttr natifs dans DropItem).
        if (eqPurged)
            await ProfileStatEmitter.SendForCharacterAsync(session,
                _services, charId, token);
    }

    /// <summary>Notifications natives d'expiration d'un item :
    /// 316 TextOutputIndex(0x5a4, nom) puis 275 ClientDropItem
    /// [s16 flag][s16 loc][s16 index] (DropItem "SYS" natif).</summary>
    private async Task NotifyExpiredItemAsync(NetSession session,
        EntryState st, ItemEntity item, short flag,
        CancellationToken token)
    {
        var name = _services.GameData.ItemNames
            .GetValueOrDefault(item.ItemId, "");
        await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = 0x5a4,                 // s16 : texte « item expiré »
            Field2 = name,                  // str : %s1 = nom Big5
        }, token);
        await session.SendAsync(new CNC_CZ_ClientDropItem
        {
            Field1 = flag,                  // s16 : 0 sac / 1 équipement
            Field2 = (short)item.Slot,      // s16 : loc dans le conteneur
            Field3 = (short)item.BagId,     // s16 : index du conteneur
        }, token);
        EdenLog.Write($"[Zone] due_date purge char={st.CharId} " +
            $"item={item.ItemId} «{name}» flag={flag} " +
            $"loc={item.Slot} cont={item.BagId}");
    }
}
