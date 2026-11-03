using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone.Handlers;

/// <summary>
/// Handlers Mana Infusion — NCID 230
/// <c>CNE_CZ_ZoneServerEquipTrainSetItem</c> : sélection de l'item à
/// entraîner (mirror de <c>CPlayerFactory::SetEquipTrainItem</c>
/// @0x80ee6b0, désasm zone). Réponses émises :
/// <list type="bullet">
///   <item>570 <c>CNC_CZ_ClientEquipTrainItemChanged</c> quand la
///         localisation recalculée diffère (char+0xd58/d5c/d5e) ;</item>
///   <item>369 <c>CNC_CZ_ClientModifyItemInfo</c> mode -1 après
///         <c>SetItemTrainType</c> (types initialisés) — record
///         complet de l'item comme le natif.</item>
/// </list>
/// Le gain d'EXP d'entraînement est traité dans le flux de kill
/// (GrantKillExpAsync → EquipTrainService.GainEquipExpAsync).
/// </summary>
public static class EquipTrainHandlers
{
    /// <summary>Enregistre le handler du NCID 230.</summary>
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // Corps natif variable : [s16 container][s16 index][s16 key]
        // [u16 count][u8 types×count] — désasm Deserialize @0x80ee5xx.
        d.RegisterChar(() => new CNE_CZ_ZoneServerEquipTrainSetItem(),
            (session, msg, charId, token)
                => HandleSetItemAsync(session, msg, charId, s, token));
    }

    /// <summary>Traite une sélection/désélection — validation et
    /// persistance déléguées à <see cref="EquipTrainService"/> ;
    /// émissions limitées aux paquets natifs prouvés (570 + 369).</summary>
    private static async Task HandleSetItemAsync(
        NetSession session, CNE_CZ_ZoneServerEquipTrainSetItem msg,
        int charId, ZoneServices s, CancellationToken token)
    {
        var outcome = await s.EquipTrain.SetItemAsync(charId,
            msg.Container, msg.Index, msg.Key, msg.Types);
        if (!outcome.Ok) return;

        // 570 — émis uniquement si la localisation diffère du dernier
        // état connu (garde natif de SetEquipTrainItemID).
        var prev = s.EquipTrain.SwapLastLocation(charId, outcome.Loc);
        if (prev != outcome.Loc)
        {
            await session.SendAsync(
                new CNC_CZ_ClientEquipTrainItemChanged
                {
                    // [s16] type conteneur : 0 sacs / 1 équipé / -1 aucun
                    ContainerType = outcome.Loc.Type,
                    // [s16] index (loc) dans le conteneur
                    Index = outcome.Loc.Index,
                    // [s16] clé du conteneur (container_index)
                    Key = outcome.Loc.Key,
                }, token);
        }

        // 369 — le natif émet le record complet de l'item après
        // SetItemTrainType + rafraîchit les stats (RefreshEQsAttr).
        if (outcome.InitDone && outcome.Item != null)
        {
            var row = await s.InventoryRepo
                .GetItemFullRowAsync(outcome.Item.Id);
            var def = s.GameData.Items
                .FirstOrDefault(d => d.Id == outcome.Item.ItemId);
            if (row != null)
                await session.SendPacketAsync(369,
                    MagicBoxPackets.BuildModifyItemInfo(row,
                        outcome.ContainerType, outcome.ContainerKey,
                        outcome.VecIndex,
                        (byte)(def != null && Eden.Core.Game.Inlay
                            .InlayService.IsSocketCapable(def)
                            ? 1 : 0)),
                    token);
            await ProfileStatEmitter.SendForCharacterAsync(session,
                s, charId, token);
        }
    }
}
