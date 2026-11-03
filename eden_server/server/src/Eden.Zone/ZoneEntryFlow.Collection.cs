using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone;

/// <summary>Collection de cartes de monstres — NCIDs 150
/// (SaveCollection → dépôt), 151 (GetCollection → retrait) et
/// 152 (QueryCollection → 472). Références natives :
/// <c>CNE_CZ_ZoneServerSaveCollection::Execute</c> @0x282820d0
/// (rate-limit ~500 ms, drop silencieux),
/// <c>CItemFactory::PutItemToCollection</c> @0x80976e0,
/// <c>CItemFactory::GetItemFromCollection</c> @0x80a0590,
/// <c>CItemFactory::UpdateCollectionPoint</c> @0x8095a70.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Compteur de groupe ±delta — UpdateCollectionPoint
    /// natif : crée la clé absente à l'ajout, ne descend que si la
    /// clé existe au retrait.</summary>
    private static void AddCollectionGroupPoints(EntryState st,
        short key, int delta)
    {
        if (st.CollectionGroupPoints.TryGetValue(key, out var v)
            || delta > 0)
            st.CollectionGroupPoints[key] = v + delta;
    }

    /// <summary>152 QueryCollection — 472 type 0 : liste complète
    /// des cartes déposées. Wire natif <c>[s16 type][i32 points]
    /// [u16 n][n×s16 item_ids]</c> — le type 0 est le mode
    /// « rafraîchissement total » de l'UI.</summary>
    private Task HandleQueryCollectionAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var msg = new CNC_CZ_ClientUpdateCollection
        {
            BookId = 0,                    // s16 : type 0 = liste complète
            U1 = st.CollectionPoints,      // i32 : points totaux (char+0xb68)
        };
        foreach (var itemId in st.CollectedItemIds.OrderBy(i => i))
            msg.Entries.Add(new CNC_CZ_ClientUpdateCollection.Entry
            { CardId = (short)itemId });   // s16 : item_id de la carte
        return session.SendAsync(msg, token);
    }

    /// <summary>150 SaveCollection — dépôt de cartes. Wire natif
    /// (<c>Deserialize</c> @0x282825c0) : flag u8 ; 0 →
    /// <c>[s16 container][s16 loc]</c> unique ; ≠0 →
    /// <c>[u16 n][n×s16 containers][u16 m][m×s16 locs]</c> (paires
    /// indexées). Rate-limit natif : un traitement par ~500 ms par
    /// perso, les demandes plus rapides sont silencieusement
    /// ignorées (vecteurs purgés). RefreshCollectionPointAward
    /// n'est appelé que sur cette voie.</summary>
    private async Task HandleSaveCollectionAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 1 || st.CharId == 0) return;
        if (DateTime.UtcNow - st.LastCollectionSaveAt
                < TimeSpan.FromMilliseconds(500))
            return;                        // drop silencieux natif
        st.LastCollectionSaveAt = DateTime.UtcNow;

        var pairs = new List<(short Container, short Loc)>();
        if (payload[0] == 0)
        {
            if (payload.Length < 5) return;
            pairs.Add((BitConverter.ToInt16(payload, 1),
                       BitConverter.ToInt16(payload, 3)));
        }
        else
        {
            var off = 1;
            var n = ReadCount(payload, ref off);   // u16 nb conteneurs
            var containers = ReadShorts(payload, ref off, n);
            var m = ReadCount(payload, ref off);   // u16 nb locs
            var locs = ReadShorts(payload, ref off, m);
            for (var i = 0; i < containers.Count
                            && i < locs.Count; i++)
                pairs.Add((containers[i], locs[i]));
        }

        var added = new List<short>();
        foreach (var (container, loc) in pairs)
        {
            var id = await PutCardToCollectionAsync(session, st,
                container, loc, token);
            if (id != 0) added.Add((short)id);
        }

        // 472 type 1 — incrémental : ids des cartes déposées
        // (PutItemToCollection émet un 472 par carte ; un unique
        // paquet groupé porte la même information pour l'UI).
        if (added.Count > 0)
        {
            var msg = new CNC_CZ_ClientUpdateCollection
            {
                BookId = 1,                // s16 : type 1 = ajout
                U1 = st.CollectionPoints,  // i32 : nouveau total
            };
            foreach (var id in added)
                msg.Entries.Add(
                    new CNC_CZ_ClientUpdateCollection.Entry
                    { CardId = id });
            await session.SendAsync(msg, token);
            await RefreshCollectionAwardAsync(session, st, token);
        }
    }

    /// <summary>Dépose une carte du sac (container, loc) dans
    /// <c>collection</c> — gates de <c>PutItemToCollection</c> natif :
    /// item présent, def S_Collection (0x67e sinon), masque locate
    /// (locate≠0 → bit (serverLocate&amp;31) requis, 0x67e), pas déjà
    /// collecté (0x680). Émet le 275 retrait <c>[s16 0][s16 loc]
    /// [s16 container]</c> et le texte succès 0x67c.</summary>
    /// <returns>item_id déposé, 0 si refusé.</returns>
    private async Task<int> PutCardToCollectionAsync(
        NetSession session, EntryState st, short container,
        short loc, CancellationToken token)
    {
        if (container is < 0 or > 4) return 0;
        var item = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, container, loc);
        if (item == null) return 0;        // warning natif, muet
        if (!_services.GameData.Collections.TryGetValue(item.ItemId,
                out var def))
        {
            await SendTextIndexAsync(session, 0x67e, token);
            return 0;
        }
        // Gate locate natif : bitmask indexé par CGlobalInfo::Locate
        // (serveur US = 0 → bit 0 requis quand locate ≠ 0).
        if (def.Locate != 0 && (def.Locate & 1) == 0)
        {
            await SendTextIndexAsync(session, 0x67e, token);
            return 0;
        }
        if (st.CollectedItemIds.Contains(item.ItemId))
        {
            await SendTextIndexAsync(session, 0x680, token);
            return 0;
        }

        var moved = await _services.InventoryRepo
            .MoveItemToCollectionAsync((int)st.CharId, container, loc);
        if (moved == null) return 0;

        st.CollectedItemIds.Add(item.ItemId);
        st.CollectionPoints += def.Points;
        AddCollectionGroupPoints(st, (short)def.Group, def.Points);
        AddCollectionGroupPoints(st,
            (short)(def.Special ? 1001 : 1000), def.Points);
        await SaveCollectionPointAsync(st);

        // 275 ClientDropItem natif : [s16 0][s16 loc][s16 container]
        // (PutItemToCollection — NotifyClient après EradicateItem).
        await session.SendPacketAsync(275, Concat(
            BitConverter.GetBytes((short)0),
            BitConverter.GetBytes(loc),
            BitConverter.GetBytes(container)), token);
        // 0x67c « %s a été enregistré » — arg = nom de l'item.
        await SendTextIndexArgsAsync(session, 0x67c,
            ItemName(item.ItemId), token);
        return item.ItemId;
    }

    /// <summary>151 GetCollection <c>[s16 collection_id]</c> — rend
    /// la carte au premier emplacement libre (GetEmptySlotNum/
    /// GetContainerWithEmptySlot natifs). 0x4f9 si sac plein, 0x67e
    /// si la carte n'est pas déposée. Ordre natif : 472 type 2
    /// AVANT le 274 de remise en sac, puis texte 0x67d.
    /// UpdateCollectionPoint mode 2 — PAS de refresh award (le natif
    /// ne le fait que sur la voie dépôt).</summary>
    private async Task HandleGetCollectionAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2 || st.CharId == 0) return;
        var collId = BitConverter.ToInt16(payload, 0);  // s16 : item_id
        if (!st.CollectedItemIds.Contains(collId)
            || !_services.GameData.Collections.TryGetValue(collId,
                out var def))
        {
            await SendTextIndexAsync(session, 0x67e, token);
            return;
        }
        var free = await _services.InventoryRepo
            .GetAnyAvailableSlotAsync((int)st.CharId);
        if (free == null)
        {
            await SendTextIndexAsync(session, 0x4f9, token);
            return;
        }
        var moved = await _services.InventoryRepo
            .MoveItemFromCollectionAsync((int)st.CharId, collId,
                free.Value.BagId, free.Value.Slot);
        if (moved == null) return;

        st.CollectedItemIds.Remove(collId);
        st.CollectionPoints = Math.Max(0,
            st.CollectionPoints - def.Points);   // clamp <0 natif
        AddCollectionGroupPoints(st, (short)def.Group, -def.Points);
        AddCollectionGroupPoints(st,
            (short)(def.Special ? 1001 : 1000), -def.Points);
        await SaveCollectionPointAsync(st);

        // 472 type 2 = retrait — émis AVANT le 274 (ordre natif).
        var msg = new CNC_CZ_ClientUpdateCollection
        {
            BookId = 2,
            U1 = st.CollectionPoints,
        };
        msg.Entries.Add(new CNC_CZ_ClientUpdateCollection.Entry
        { CardId = collId });
        await session.SendAsync(msg, token);

        // 274 ClientPutItemIntoInventory — clone complet natif
        // (l'entité relue porte les champs d'instance restaurés).
        var restored = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, free.Value.BagId, free.Value.Slot);
        if (restored != null)
            await session.SendPacketAsync(274,
                NativeItemCodec.BuildPutItem(restored), token);
        await SendTextIndexArgsAsync(session, 0x67d,
            ItemName(collId), token);
    }

    /// <summary>RefreshCollectionPointAward natif (zone @0x822…) :
    /// retire les enchants accordés (vec char+0xe28), réévalue
    /// chaque S_CollectionPoint sur la map de compteurs (toutes les
    /// paires clé→seuil non nulles doivent être atteintes), applique
    /// les enchants permanents (EffectToLife rest=-2) puis
    /// RefreshEQsAttr.</summary>
    private async Task RefreshCollectionAwardAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        foreach (var eid in st.CollectionAwardIds)
            await RemovePlayerEnchantAsync(session, st, eid, token);
        st.CollectionAwardIds.Clear();
        foreach (var award in _services.GameData.CollectionPointAwards)
        {
            if (award.Enchant == 0
                || award.Conds.Any(c =>
                    !st.CollectionGroupPoints.TryGetValue(c.Key,
                        out var v) || v < c.Threshold))
                continue;
            await AddPlayerEnchantAsync(session, st, (int)st.CharId,
                award.Enchant, -2, 1, token);
            st.CollectionAwardIds.Add(award.Enchant);
        }
        if (st.CollectionAwardIds.Count > 0)
            await SendStatBlocksAsync(session, st, st.ClassId,
                st.ClassLevel, token);
    }

    /// <summary>Persiste <c>player_achievement_rank.collection_point</c>
    /// — cible du <c>SavePlayerCharacter(.., "collection_point")</c>
    /// natif (best-effort : la ligne peut être absente).</summary>
    private async Task SaveCollectionPointAsync(EntryState st)
    {
        try
        {
            await _services.InventoryRepo.SetCollectionPointAsync(
                (int)st.CharId, st.CollectionPoints);
        }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[Collection] save point charId={st.CharId} : {ex.Message}");
        }
    }

    /// <summary>Nom localisé d'un item pour les textes 0x67c/0x67d
    /// (arg %s du TextOutputIndex — le client formate côté UI).</summary>
    private string ItemName(int itemId) =>
        _services.GameData.ItemNames.TryGetValue(itemId, out var n)
            ? n : itemId.ToString();

    /// <summary>TextOutputIndex avec argument de format —
    /// <c>TextOutputIndex(idx, channel, "%s", name)</c> natif : la
    /// chaîne formatée part dans le champ string du 316.</summary>
    private static Task SendTextIndexArgsAsync(NetSession session,
        short index, string arg, CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = index,
            Field2 = arg ?? "",
        }, token);

    private static int ReadCount(byte[] p, ref int off)
    {
        if (off + 2 > p.Length) return 0;
        var n = BitConverter.ToUInt16(p, off);
        off += 2;
        return n;
    }

    private static List<short> ReadShorts(byte[] p, ref int off, int n)
    {
        var list = new List<short>(n);
        for (var i = 0; i < n && off + 2 <= p.Length; i++, off += 2)
            list.Add(BitConverter.ToInt16(p, off));
        return list;
    }
}
