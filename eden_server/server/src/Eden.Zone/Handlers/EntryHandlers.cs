using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Entrée en zone : ticket de migration, hello, blocs EnterNode*
/// et fin de chargement (MigrationDone).
/// </summary>
public static class EntryHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // CTicket natif 22 octets : [u32 k1][u32 clientIp][u32 zoneIp]
        // [u16 zonePort][u32 microtime][u32 random] — émis par WorldServer,
        // validé contre la table zone_tickets (usage unique, TTL 5 min).
        d.Register(() => new CNE_CZ_ZoneServerReceiveTicket(),
            async (session, msg, token) =>
            {
                var ticket = msg.Ticket;
                var key1 = BitConverter.ToUInt32(ticket, 0);
                var key2 = BitConverter.ToUInt32(ticket, 14); // microtime sert de k2
                var clientIp = BitConverter.ToUInt32(ticket, 4);
                var zoneIp = BitConverter.ToUInt32(ticket, 8);
                var zonePort = BitConverter.ToUInt16(ticket, 12);

                var charId = await s.Tickets.ConsumeAsync(key1, key2, zoneIp, zonePort);
                if (charId == null)
                {
                    EdenLog.Write($"[Zone] ReceiveTicket INVALIDE k1={key1:x8} " +
                        $"zone={zoneIp:x8}:{zonePort} — rejet");
                    await session.SendAsync(new CNC_CZ_ClientTicketInvalid(), token);
                    return;
                }

                s.Sessions.BindCharacter(session, charId.Value);
                // Charge l'état social persistant du joueur (amis, couple,
                // familiers, raccourcis) — attaché via SocialRepository.
                if (s.Social != null)
                {
                    await s.Friends.LoadForAsync(charId.Value);
                    await s.Lovers.LoadForAsync(charId.Value);
                }
                EdenLog.Write($"[Zone] ReceiveTicket OK char={charId} " +
                    $"clientIp={clientIp:x8} — session migrée");
            });

        // MigrationDone : le client a fini de charger — fin du flux d'entrée
        d.Register(() => new CNE_CZ_ZoneServerMigrationDone(),
            async (session, msg, token) =>
            {
                if (session.CharacterId is { } charId)
                    EdenLog.Write($"[Zone] MigrationDone char={charId} — en jeu");
                await session.SendAsync(new CNC_CZ_ClientMigrationDone(), token);
            });

        // Hello post-ticket : le serveur répond CNC_CZ_ClientHello (float)
        d.Register(() => new CNE_CZ_ZoneServerHello(),
            async (session, msg, token) =>
                await session.SendAsync(new CNC_CZ_ClientHello { Value = 0f }, token));

        // Blocs d'entrée en zone — le client demande chaque nœud de données.
        // EnterNodePlayerData : (charId, name, ?, ?, ?, ?, level?, exp?, ...)
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterNodePlayerData(),
            async (session, msg, charId, token) =>
            {
                var ch = await s.Characters.GetCharacterByIdAsync(charId);
                if (ch == null) return;
                // Enregistre le joueur comme entité vivante de la zone
                var levelDef = s.GameData.Levels.FirstOrDefault(l => l.Level == ch.Level);
                s.Entities.RegisterPlayer(charId, new CombatService.CombatStats
                {
                    Level = ch.Level,
                    AttackPower = levelDef?.AttackMax ?? 50,
                    MagicPower = levelDef?.AttackMax ?? 50,
                    Defense = levelDef?.DefenseMax ?? 10,
                }, maxHp: levelDef?.MaxHp ?? 100,
                   x: (float)ch.PositionX, y: (float)ch.PositionY, z: (float)ch.PositionZ);
                await session.SendAsync(new CNC_CZ_ClientEnterNodePlayerData
                {
                    Field1 = (uint)ch.Id,
                    Field2 = ch.GivenName,
                    Field3 = string.Empty,
                    Field7 = (ushort)ch.Level,
                    Field8 = (int)ch.Experience,
                }, token);
            });

        // EnterNode* : squelettes vides en attendant le layout complet.
        RegisterSkeletons(d);
        RegisterInventory(d, s);
    }

    private static void RegisterInventory(PacketDispatcher d, ZoneServices s)
    {
        // EnterNodeInventory — sac principal, vrai format natif (désasm
        // Serialize @0x8233xxx + capture : bitmap + records items 46B+)
        // Source : la VUE native `inventory` (UNION inventory1/2) — les
        // items réels des persos natifs, pas player_items (vide).
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterNodeInventory(),
            async (session, msg, charId, token) =>
            {
                // Réponse native à la requête 25 (capture 17:22:12.907) :
                // 490 → 312 → 280(attr 0x3f0=1). Le 490
                // ClientEnterNodePlayerCard (27 octets à zéro) est déjà
                // émis par le replay de la requête 133 — le client envoie
                // 133 et 25 dans le même burst et le natif ne répond
                // qu'un seul 490. Sans le flag 0x3f0 ci-dessous, le
                // client ignore le 312 : inventaire visuellement vide.
                var items = await s.InventoryRepo.GetLiveItemsAsync(
                    charId);
                // Rattrapage items à durée insérés avant le support de
                // due_date (montures "0 sec. remain") : le natif arme
                // item+0x18 = now + CItemData+0x70 au PutIn/DoUseItem.
                // Durée = DurationSeconds (unit col26 × count col27 —
                // CItemQuery::Deserialize @0x8602f80), PAS Max() des
                // deux colonnes. due_date déjà armé (même expiré) →
                // skip : un item expiré EST expiré côté natif aussi.
                var nowTs = (int)DateTimeOffset.UtcNow
                    .ToUnixTimeSeconds();
                foreach (var i in items)
                {
                    if (i.DueDate > 0) continue;
                    var def = s.GameData.Items
                        .FirstOrDefault(d => d.Id == i.ItemId);
                    if (def?.DurationSeconds is not > 0) continue;
                    i.DueDate = nowTs + def.DurationSeconds;
                    await s.InventoryRepo.UpdateDueDateAsync(i.Id,
                        i.DueDate);
                }
                var capacities = await s.InventoryRepo
                    .GetAllBagCapacitiesAsync(charId);
                var equippedBags = await s.InventoryRepo
                    .GetEquippedBagsAsync(charId);
                foreach (var b in equippedBags)
                    b.Capacity = s.GameData.Items
                        .FirstOrDefault(d => d.Id == b.ItemId)
                        ?.BagCapacity ?? 0;
                var body = Eden.Zone.Codecs.NativeItemCodec
                    // Tous les sacs contigus 0..maxBag sont émis — sinon
                    // les items de container_index>0 sont invisibles et
                    // l'UI client pointe sur des slots fantômes. Les
                    // capacités viennent de inventory_slots (ouvertes
                    // par les sacs équipés) + le bloc porte-sacs liste
                    // les sacs équipés (désasm Serialize @0x8233xxx).
                    .BuildEnterNodeInventory(items, capacities,
                        equippedBags);
                var bagSummary = string.Join(",",
                    capacities.OrderBy(kv => kv.Key).Select(kv =>
                        $"{kv.Key}:" +
                        $"{items.Count(i => i.BagId == kv.Key && i.Slot >= 0 && i.Slot < kv.Value)}" +
                        $"/{kv.Value}"));
                var invalidSlots = items.Count(i => i.Slot < 0 ||
                    !capacities.TryGetValue(i.BagId, out var cap) ||
                    i.Slot >= cap);
                EdenLog.Write($"[Inventory312] char={charId} " +
                    $"rows={items.Count} bags=[{bagSummary}] " +
                    $"invalidSlots={invalidSlots} " +
                    $"body={body.Length}");
                await session.SendPacketAsync(312, body, token);

                // 280 ClientUpdateAttr attr 0x3f0 = 1 — "inventaire prêt"
                // (EnterNodeInventory::Execute natif émet UpdateAttr
                // (0x3f0,1) ; capture 17:22:12.908 :
                // `02c2eb0b f003 01000000 00000000 00000000`).
                await session.SendAsync(new CNC_CZ_ClientUpdateAttr
                {
                    Field1 = (int)charId,       // u32 : handle joueur
                    Field2 = 0x3f0,             // s16 : attrId
                    Field3 = 1, Field4 = 0,     // i32 cur = 1, i64 p2 = 0
                }, token);
            });

        // EnterNodeShortcut — barres de raccourcis persistées
        // (bitmask 10 slots + vecteur int, layout vérifié 0x8202200)
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterNodeShortcut(),
            async (session, msg, charId, token) =>
            {
                var resp = new CNC_CZ_ClientEnterNodeShortcut();
                var rows = s.Social != null
                    ? await s.Social.GetShortcutsAsync(charId)
                    : (IReadOnlyList<SocialRepository.ShortcutRow>)
                        Array.Empty<SocialRepository.ShortcutRow>();
                var pages = rows.GroupBy(r => r.Page)
                    .ToDictionary(g => (int)g.Key);
                // Natif : 2 barres minimum (page 0 et 1) vues en capture.
                var nBars = Math.Max(2,
                    pages.Count > 0 ? pages.Keys.Max() + 1 : 0);
                for (var p = 0; p < nBars; p++)
                {
                    var bar = new CNC_CZ_ClientEnterNodeShortcut
                        .ShortcutBar();
                    for (var i = 0; i < 10; i++) bar.Slots.Add(-1);
                    if (pages.TryGetValue(p, out var group))
                        foreach (var r in group)
                            if (r.Slot is >= 0 and <= 9)
                                bar.Slots[r.Slot] = r.RefId;
                    resp.Bars.Add(bar);
                }
                await session.SendAsync(resp, token);
            });

        // EnterNodeElfInfo — familier invoqué (record fixe 40 champs)
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterNodeElfInfo(),
            async (session, msg, charId, token) =>
            {
                var elf = s.Elfs.GetSummoned(charId);
                var resp = new CNC_CZ_ClientEnterNodeElfInfo();
                if (elf != null)
                {
                    resp.Field1 = (ushort)elf.Definition.Id;
                    resp.Field2 = (short)elf.InstanceId;
                    resp.Field3 = elf.Mood;
                    resp.Field4 = elf.Energy;
                    resp.Field5 = elf.Friendship;
                    resp.Field12 = elf.Name; // seul champ string du record
                }
                await session.SendAsync(resp, token);
            });
    }

    private static void RegisterSkeletons(PacketDispatcher d)
    {
        d.Register(() => new CNE_CZ_ZoneServerEnterNodeEquipAndSpell(),
            async (session, msg, token) =>
                await session.SendAsync(new CNC_CZ_ClientEnterNodeEquipAndSpell(), token));
        d.Register(() => new CNE_CZ_ZoneServerEnterNodeElfContainer(),
            async (session, msg, token) =>
                await session.SendAsync(new CNC_CZ_ClientEnterNodeElfContainer(), token));
        d.Register(() => new CNE_CZ_ZoneServerEnterNodePlayerCard(),
            async (session, msg, token) =>
                await session.SendAsync(new CNC_CZ_ClientEnterNodePlayerCard(), token));
        d.Register(() => new CNE_CZ_ZoneServerEnterNodePlayerRoom(),
            async (session, msg, token) =>
                await session.SendAsync(new CNC_CZ_ClientEnterNodePlayerRoom(), token));
        d.Register(() => new CNE_CZ_ZoneServerEnterNodeStarWay(),
            async (session, msg, token) =>
                await session.SendAsync(new CNC_CZ_ClientEnterStarWayData(), token));
    }
}
