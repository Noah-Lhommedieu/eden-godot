// ============================================================
//  ZoneEntryFlow.ItemUse.cs — canalisation d'usage d'item
//  (407 ClientUseItemTime / 408 ClientUseItemTimeBreak)
//
//  Portage de lapis::CItemFactory::DoUseItem (branche temporisée)
//  + lapis::CEventUseItemTime + rupture CPlayerFactory::CheckCastBreak
//  (désasm zone @0x80b8a10 / @0x819cb30 / @0x80f5790).
//
//  S_Item col20 → CItemData+0x54 (use-time, décisecondes).
//  == 0 : usage instantané (DoUseItemAfterCheck immédiat).
//  != 0 : si char+0x90 == 0 →
//    407 [u32 itemId][s16 contIdx][s16 slot] au client,
//    control mémorisé en char+0x90 (→ EntryState.PendingItemUse),
//    CEventUseItemTime planifié à +low16(col20)×100 ms,
//    LifeEntityDo état 6 (canalisation).
//  À l'échéance (CEventUseItemTime::Execute @0x819cb30) :
//    si char+0x90 toujours le même control → re-fetch slot :
//      NULL → texte 0x5f7 ; serial changé → release silencieuse ;
//      identique → DoUseItemAfterCheck (l'usage réel).
//  CheckCastBreak (mouvement* / mêlée / mort / migration) → cancel +
//  408 corps vide + release. (*la rupture au mouvement est
//  volontairement off — cast mobile maison, cf. Movement.cs.)
//
//  Data : 102 items col20≠0, tous UseType 2 (kind 44/48), 3-100 ds.
// ============================================================
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Canalisation d'usage d'item en cours — miroir du
    /// UseItemControl stocké en char+0x90 natif : le slot surveillé
    /// (conteneur sac + loc), le serial attendu (le pointeur clone
    /// natif — si le slot re-pointe sur un autre item à l'échéance,
    /// l'usage est relâché) et le CTS d'annulation.</summary>
    internal sealed class PendingItemUse
    {
        public int ItemId;
        public long Serial;
        public short Bag;
        public short Slot;
        public CancellationTokenSource? Cts;
    }

    /// <summary>Branche temporisée de <c>DoUseItem</c> @0x80b8a10 :
    /// émet le 407 ClientUseItemTime, pose char+0x90 et planifie le
    /// UseItemTimeEvent. Appelée par le handler ItemUse via
    /// <see cref="ZoneServices.ItemUseChannel"/> (routage boucle).
    /// Retour : true = canalisation en place (démarrée ou déjà
    /// occupée — le natif ne répond rien quand char+0x90 ≠ 0).</summary>
    private async Task<bool> StartItemUseChannelAsync(NetSession session,
        EntryState st, int itemId, long serial, short bag, short slot,
        int useTimeDs,
        Func<ItemEntity, CancellationToken, Task> complete,
        CancellationToken token)
    {
        // char+0x90 ≠ 0 → return 0 natif : aucun paquet, l'item reste.
        if (st.PendingItemUse != null) return true;
        var p = new PendingItemUse
        { ItemId = itemId, Serial = serial, Bag = bag, Slot = slot };
        var cts = new CancellationTokenSource();
        p.Cts = cts;
        st.PendingItemUse = p;
        // 407 ClientUseItemTime — ctor natif DoUseItem @0x80b8a10 :
        // [u32 item_id][s16 GetContainerIndex][s16 slot].
        await session.SendAsync(new CNC_CZ_ClientUseItemTime
        {
            Field1 = (uint)itemId,          // u32 : template id de l'item
            Field2 = bag,                   // s16 : index du sac
            Field3 = slot,                  // s16 : loc dans le sac
        }, token);
        // CEventUseItemTime — le ctor natif (zone @0x819cd10) découpe
        // le use-time u16 (low16 de +0x54) en p/10 s + (p%10)×100000 µs
        // → délai total = useTimeDs × 100 ms.
        var delayMs = Math.Max(1, useTimeDs & 0xffff) * 100;
        ScheduleOnLoop(TimeSpan.FromMilliseconds(delayMs),
            _ => CompleteItemUseChannelAsync(session, st, p, complete,
                cts.Token), cts.Token);
        EdenLog.Write($"[Zone] UseItemTime char={st.CharId} " +
            $"item={itemId} bag={bag} slot={slot} ds={useTimeDs} " +
            "— canalisation armée");
        return true;
    }

    /// <summary>CEventUseItemTime::Execute @0x819cb30 : à l'échéance,
    /// re-validation du slot (item toujours présent, même serial)
    /// puis DoUseItemAfterCheck — ici la continuation passée par le
    /// handler (UseItemFinalAsync).</summary>
    private async Task CompleteItemUseChannelAsync(NetSession session,
        EntryState st, PendingItemUse p,
        Func<ItemEntity, CancellationToken, Task> complete,
        CancellationToken token)
    {
        // char+0x90 ne pointe plus ce control (cassé/remplacé) →
        // release natif, rien d'autre.
        if (st.PendingItemUse != p) return;
        st.PendingItemUse = null;
        var cur = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, p.Bag, p.Slot);
        if (cur == null || cur.Quantity <= 0)
        {
            // Item parti pendant la canalisation — texte 0x5f7 natif
            // (branche `slot vide` de l'Execute).
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x5f7, Field2 = "" }, token);
            return;
        }
        if (cur.Id != p.Serial)
        {
            // Le slot re-pointe sur un autre item — le natif loggue
            // un assert et relâche le control sans consommer.
            EdenLog.Write($"[Zone] UseItemTime char={st.CharId} " +
                $"serial attendu={p.Serial} trouvé={cur.Id} — " +
                "canalisation relâchée");
            return;
        }
        await complete(cur, token);
    }

    /// <summary>Moitié +0x90 de <c>CheckCastBreak</c> @0x80f5790 :
    /// canalisation d'item en cours → cancel event, release, 408
    /// corps vide au client. Appelée depuis BreakCastAsync (mêlée,
    /// mort, migration, annulation manuelle).</summary>
    private async Task CancelItemUseChannelAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.PendingItemUse == null) return;
        st.PendingItemUse.Cts?.Cancel();
        st.PendingItemUse = null;
        await session.SendAsync(new CNC_CZ_ClientUseItemTimeBreak(),
            token);
        EdenLog.Write($"[Zone] UseItemTimeBreak char={st.CharId} " +
            "— canalisation d'usage interrompue");
    }
}
