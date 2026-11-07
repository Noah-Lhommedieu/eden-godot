using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Trésors au sol — orchestration zone du <c>CTreasure</c> natif :
/// spawn quand l'inventaire déborde (<c>CloneQuestTreasure</c>/
/// <c>PlayerPickUpTreasure</c> → <c>DropTreasureOnFloor</c>), pickup
/// NCID 35 (<c>ZoneServerTreasurePickup::Execute</c> →
/// <c>CItemFactory::PlayerPickUpTreasure</c> @0x80b3130), promotion
/// publique et despawn (CEventTreasure). La donnée vit dans
/// <see cref="GroundTreasureService"/> ; ici : paquets + règles.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Distance de ramassage native — IGameObj::CloseEnough
    /// au trésor : 4.0u (PlayerPickUpTreasure @0x80b3130).</summary>
    private const float TreasurePickupRange = 4.0f;

    /// <summary>Poser un item au sol parce que l'inventaire est plein —
    /// miroir du fallback natif <c>PlayerPickUpTreasure → DropOnFloor
    /// </c> (quête) et du drop AddCloneItemWorld==NULL (loot).
    /// Le trésor n'est d'abord visible que du propriétaire (329 u8=0,
    /// comme CTreasure::OnSeen pendant la fenêtre +0x74).</summary>
    private async Task SpawnGroundTreasureAsync(EntryState st,
        int itemId, int count, int questMissionId = 0)
    {
        var t = _services.GroundTreasure.Spawn(st.NodeId, st.InstanceGroup, (int)st.CharId,
            st.PosX, st.PosZ, itemId, count, DateTime.UtcNow,
            questMissionId);
        if (st.Session is { } s)
            await s.SendAsync(new CNC_CZ_ClientSeeTreasure
            {
                Field1 = t.Id, Field4 = t.X, Field5 = t.Z,
                Field6 = (ushort)t.ItemId,
                Field7 = (short)t.Count, Field8 = (short)t.Count,
                Field11 = t.OwnerCharId,
                Field12 = (byte)(t.IsPublic ? 1 : 0),
            }, CancellationToken.None);
        EdenLog.Write($"[Zone] treasure {t.Id} item={itemId} " +
            $"x{count} owner={t.OwnerCharId} node={t.NodeId} " +
            $"({t.X:F1},{t.Z:F1})");
    }

    /// <summary>Callback enregistré sur <see cref="ZoneServices.
    /// TreasureDrop"/> — les handlers stateless (loot CombatHandlers)
    /// déposent le débordement via ce délégué qui retrouve
    /// l'EntryState par SessionId.</summary>
    private Task TreasureDropCallbackAsync(NetSession session, int itemId,
        int count)
    {
        return _states.TryGetValue(session.SessionId, out var st)
            ? SpawnGroundTreasureAsync(st, itemId, count)
            : Task.CompletedTask;
    }

    /// <summary>NCID 35 ZoneServerTreasurePickup [u32 id] — ramassage.
    /// Règles natives de PlayerPickUpTreasure :
    ///   trésor inexistant/autre node → ignore ;
    ///   distance &gt; 4u → texte 0x60d ;
    ///   fenêtre propriétaire et non-propriétaire → texte 0x625 ;
    ///   inventaire plein → texte 0x4f9 (le trésor RESTE au sol —
    ///   c'est ce qui crée l'itération drop→pickup chez le natif) ;
    ///   succès → insertion + 274 + 386 PickupOK + 330 à tous.</summary>
    private async Task HandleTreasurePickupAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 4) return;
        var id = BitConverter.ToUInt32(payload, 0);
        if (!_services.GroundTreasure.TryGet(id, out var t) ||
            t.NodeId != st.NodeId ||
            t.InstanceKey != st.InstanceGroup)
            return;
        await PickupTreasureCoreAsync(session, st, t, st.PosX, st.PosZ,
            byPet: false, token);
    }

    /// <summary>Cœur du ramassage — appelé par le joueur (NCID 35,
    /// origine = position du joueur) et par le familier en mode loot
    /// (origine = position du pet, <paramref name="byPet"/> : aucun
    /// texte d'échec, pas de roll d'équipe). Le résultat va toujours
    /// dans l'inventaire du propriétaire <paramref name="st"/>.
    /// Retourne vrai si l'item a été ramassé.</summary>
    private async Task<bool> PickupTreasureCoreAsync(NetSession session,
        EntryState st, GroundTreasureService.Treasure t, float fromX,
        float fromZ, bool byPet, CancellationToken token)
    {
        var id = t.Id;
        // IGameObj::CloseEnough 4.0 — plan x/z.
        var dx = t.X - fromX; var dz = t.Z - fromZ;
        if (dx * dx + dz * dz > TreasurePickupRange * TreasurePickupRange)
        {
            if (!byPet) await SendTextIndexAsync(session, 0x60d, token);
            return false;
        }
        // CanPickUp : propriétaire (ou fenêtre publique écoulée).
        if (!t.IsPublic && t.OwnerCharId != 0 &&
            t.OwnerCharId != (int)st.CharId)
        {
            if (!byPet) await SendTextIndexAsync(session, 0x625, token);
            return false;
        }
        // Roll en cours → pickup refusé (GetRollItem != 0 → return 0
        // natif) ; sinon éligible → lancement (520 aux participants).
        if (_services.RollTreasure.TryGet(id, out _)) return false;
        if (TryRollGate(st, t, out var team))
        {
            // Le familier ne lance jamais un roll d'équipe : le joueur
            // décide (HYPOTHESIS — comportement natif non prouvé).
            if (byPet) return false;
            await StartTreasureRollAsync(st, t, team, token);
            return false;
        }
        // Insertion — plein → 0x4f9, le trésor reste au sol (natif :
        // PlayerPickUpTreasure rend 0 et l'item retombe).
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var (ok, _, _, _) = await _services.Inventory.AddItemAsync(
            (int)st.CharId, t.ItemId, t.Count, 0, 0, false, touched);
        if (!ok)
        {
            if (!byPet) await SendTextIndexAsync(session, 0x4f9, token);
            return false;
        }
        // Un seul ramasseur l'emporte (TryRemove atomique).
        if (!_services.GroundTreasure.TryRemove(id, out _))
            return false;
        // Merge → 291, nouveau slot → 274 (règle native, cf.
        // NotifyItemSlotsAsync — un 274 sur slot occupé crashe).
        await NotifyItemSlotsAsync(session, st, touched, token);
        await session.SendAsync(new CNC_CZ_ClientTreasurePickupOK
        {
            Field1 = st.CharId, Field2 = (ushort)t.ItemId,
            Field3 = (short)t.Count,
        }, token);
        // CleanTreasure → BroadCastToCanSee 330.
        await BroadcastToNodeAsync(st.NodeId, st.InstanceGroup, 330,
            Codecs.TreasureCodec.BuildTreasureDisappear(id));
        _services.NotifyItemChanged((int)st.CharId, t.ItemId);
        // Trésor de quête (MonsterTreasureGot déposé au sol sur sac
        // plein) : la progression avance à l'obtention — même op 145
        // que le drop direct en sac (DistributeQuestDropsAsync).
        if (t.QuestMissionId != 0 && _mission.Connected)
            _ = _mission.SendQuestItemGotAsync(st.CharId,
                t.QuestMissionId, t.ItemId);
        EdenLog.Write($"[Zone] treasure {id} ramassé " +
            (byPet ? "par le familier de " : "par ") +
            $"char={st.CharId} item={t.ItemId} x{t.Count}");
        return true;
    }

    /// <summary>Tick du CEventTreasure natif — appelé par le timer de
    /// Program.cs : promotion publique (329 u8=1 broadcast) puis
    /// despawn (330 broadcast).</summary>
    public async Task TickTreasuresAsync()
    {
        var (toPublic, toDespawn) =
            _services.GroundTreasure.Sweep(DateTime.UtcNow);
        foreach (var t in toPublic)
            await BroadcastToNodeAsync(t.NodeId, t.InstanceKey, 329,
                Codecs.TreasureCodec.BuildSeeTreasure(t));
        foreach (var t in toDespawn)
            await BroadcastToNodeAsync(t.NodeId, t.InstanceKey, 330,
                Codecs.TreasureCodec.BuildTreasureDisappear(t.Id));
    }

    /// <summary>Broadcast d'un paquet brut à toutes les sessions dont
    /// l'EntryState est sur <paramref name="nodeId"/> — équivalent de
    /// CZoneServer::BroadCastToCanSee restreint au node.</summary>
    private async Task BroadcastToNodeAsync(int nodeId, int inst,
        ushort ncid, byte[] body)
    {
        foreach (var (_, ost) in _states)
        {
            if (ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Migrated ||
                ost.Session is not { } s) continue;
            try { await s.SendPacketAsync(ncid, body,
                CancellationToken.None); }
            catch { /* session morte */ }
        }
    }
}
