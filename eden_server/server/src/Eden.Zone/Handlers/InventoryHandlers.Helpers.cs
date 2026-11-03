using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone.Codecs;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Inventaire : drop, déplacement/swap, équipement, split —
/// plus banque personnelle et stockage de famille.
/// </summary>
/// <summary>Partiel helpers — utilitaires d'inventaire :
/// slots libres, résolution equip-loc, conflits d'armes,
/// coûts réparation/identification, envoi storage, broadcast
/// d'équipement. Aucun dispatch NCID ici.</summary>
public static partial class InventoryHandlers
{
    private static async Task<long> RepairCostAsync(ZoneServices s,
        int charId, short slot)
    {
        var items = await s.InventoryRepo.GetItemsByCharacterIdAsync(charId);
        long cost = 0;
        foreach (var it in items.Where(i =>
                     slot < 0 ? i.IsEquipped : i.Slot == slot))
            cost += ItemRepairCost(s, it);
        return cost;
    }

    /// <summary>Vrai si le sac d'inventaire bagId est vide — check
    /// natif `GetEmptySlotNum == GetContainerSize` de
    /// SwitchBagToContainer/FromContainer (erreur 0x306 sinon).</summary>
    private static async Task<bool> BagIsEmptyAsync(ZoneServices s,
        int charId, int bagId)
    {
        var items = await s.InventoryRepo
            .GetItemsByCharacterIdAsync(charId);
        return !items.Any(i => i.BagId == bagId);
    }
    /// <summary>Premier loc libre du sac — borné par la capacité
    /// RÉELLE (inventory_slots, 32 pour le sac de base, 0 pour un sac
    /// non ouvert). AVANT : fallback 64 → des items étaient placés
    /// aux locs 20-63 d'un sac affiché à 20 → le 277/274 écho du loc
    /// hors limites crashait le client (PutIn OOB, bug live).</summary>
    private static async Task<short> FirstFreeSlotAsync(ZoneServices s,
        int charId, short bagId)
    {
        var items = await s.InventoryRepo.GetItemsByCharacterIdAsync(charId);
        var used = items.Where(i => i.BagId == bagId)
            .Select(i => (int)i.Slot).ToHashSet();
        var cap = await s.InventoryRepo.GetBagCapacity(charId, bagId)
            ?? (bagId == 0 ? 32 : 0);
        for (var i = 0; i < cap; i++)
            if (!used.Contains(i)) return (short)i;
        return -1;
    }
    /// <summary>Loc d'équipement résolu — traduction native
    /// `CItemFactory::Equip` @0x80b5810 : la col10 de S_Item est un
    /// CODE TYPE, pas le slot final :
    ///   type 9  (2H)        → loc 7 (partage le slot main) ;
    ///   type 7  (1H)        → loc 7, ou 8 si 7 occupé par un autre
    ///                         type 7/19 et 8 libre (dual wield) ;
    ///   type 19             → loc 7/8 (même règle dual) ;
    ///   type 35 (distance)  → loc 10 ;
    ///   type 10             → loc 11 ;
    ///   types 11/20         → paire 12/13 (premier libre) ;
    ///   type 12             → paire 14/15 ;
    ///   types &gt;20 sauf 35 → REFUS natif (items non équipables).
    /// Sans cette traduction une arme de type 9/35 était enregistrée
    /// dans la loc brute, hors des slots CEquipment natifs.
    /// dstSlot explicite : le natif force le loc canonique (slot 8
    /// n'accepte que les 1H de type 7/19).
    /// dstContainer = conteneur d'équipement cible (map char+0x4fc) :
    /// 0 = tenue active, 1 = panneau pet (kinds 61-68/81, locs
    /// {1,2,4,5,6,7,8}), 2-4 = pages d'ensemble par poids d'armure
    /// (kind%3 → cont2/3/4, locs 1-5 seulement), 5 = glyphes —
    /// désasm switch `GetContainerIndex(param_3)` @0x80b5b68.</summary>
    internal static async Task<short> ResolveEquipLocAsync(ZoneServices s,
        int charId, int itemId, short dstSlot, short dstContainer = 0)
    {
        var def = s.GameData.Items.FirstOrDefault(i => i.Id == itemId);
        if (def == null) return -1;

        // Gates natifs par conteneur cible (switch Equip @0x80b5b68) :
        // les kinds non-équipement (44 usage, 45 gemme, 56 enchant,
        // 60 œuf, 78 glyphe) n'ont PAS de type joueur — leur col9 est
        // leur propre slot (socket/panneau/cible). Sans ce filtre un
        // poisson-pet (type 7) partait dans le slot arme et une gemme
        // (type 1-9) dans l'armure — bug live.
        if (dstContainer == 1)
        {
            // Conteneur pet : kinds 0x3d-0x44 + 0x51, locs autorisés
            // natifs {1,2,4,5,6,7,8} (case 1 du switch). Le col9 des
            // items pet EST le slot panneau — pas de traduction type.
            if (!Eden.Core.Game.Inventory.NativeEquipmentSlots
                    .IsPetEquipKind(def.Kind))
                return -1;
            var petLoc = def.EquipLoc;
            if (petLoc is not (1 or 2 or 4 or 5 or 6 or 7 or 8)) return -1;
            return dstSlot is -1 ? (short)petLoc
                : dstSlot == petLoc ? dstSlot : (short)-1;
        }
        if (dstContainer is >= 2 and <= 4)
        {
            // Pages d'ensemble : kinds mod3 par page (cont2→0,
            // cont3→2, cont4→1), locs 1-5 (armures uniquement —
            // switch cases 2/3/4 natifs).
            var pageMod = dstContainer == 2 ? 0 : dstContainer == 3 ? 2 : 1;
            if (!Eden.Core.Game.Inventory.NativeEquipmentSlots
                    .IsPlayerEquippableKind(def.Kind) ||
                def.Kind % 3 != pageMod || def.EquipLoc is < 1 or > 5)
                return -1;
            if (dstSlot == -1) return (short)def.EquipLoc;
            return dstSlot == def.EquipLoc ? dstSlot : (short)-1;
        }
        if (dstContainer == 5)
            // Conteneur glyphe : kinds 0x4e uniquement — slots glyph
            // (GetClassGlyphLoc) non portés → refus propre plutôt
            // qu'un mauvais loc.
            return -1;

        // Conteneur 0 — kinds joueur uniquement (rejects natifs :
        // 60 œuf → 0x9e, 61-68 pet → 0xa0, 81 pet mall → 0x9e,
        // 78 glyphe → conteneur 5 ; plus les kinds sans classe
        // CEquipItemData joueur).
        if (!Eden.Core.Game.Inventory.NativeEquipmentSlots
                .IsPlayerEquippableKind(def.Kind))
            return -1;
        var t = def.EquipLoc;
        // La table de locs vient du désasm Equip @0x80b5810 :
        // les consommables/objets de quête (EquipLoc=0) et tout type
        // non-équipable sont refusés avant les déplacements DB.
        if (Eden.Core.Game.Inventory.NativeEquipmentSlots
                .AllowedLocations(t).Length == 0)
            return -1;

        // Paires dual-wield natives (slot conteneur indexé par loc).
        async Task<bool> SlotFree(int loc) =>
            !(await s.InventoryRepo.GetEquippedItemEntitiesAsync(charId))
                .Any(e => e.Slot == loc);
        if (t is 11 or 20)
        {
            if (dstSlot is 12 or 13) return dstSlot;
            if (dstSlot != -1) return -1;
            // natif : slot12 libre OU slot13 occupé → 12, sinon 13.
            return await SlotFree(12) || !await SlotFree(13)
                ? (short)12 : (short)13;
        }
        if (t == 12)
        {
            if (dstSlot is 14 or 15) return dstSlot;
            if (dstSlot != -1) return -1;
            return await SlotFree(14) || !await SlotFree(15)
                ? (short)14 : (short)15;
        }
        if (t == 10)
            return dstSlot is 11 or -1 ? (short)11 : (short)-1;

        // Types génériques : 19→7, 35→10, 9→7 (2H partage la main),
        // autres = loc direct (1-6 armure, 8 off-hand).
        var canonical = t switch { 19 => 7, 35 => 10, 9 => 7, _ => t };
        // Le reroutage dual-wield vers la loc 8 n'existe que pour les
        // vraies 1H (types 7/19) — une 2H (type 9) partage le loc 7 et
        // ne doit JAMAIS aller en 8 (sinon 1H en 7 + 2H en 8 pouvaient
        // coexister — bug live).
        if (canonical == 7 && dstSlot == -1 && t is 7 or 19)
        {
            // Dual wield : loc 7 occupé par une autre 1H (type 7/19)
            // et loc 8 libre → loc 8 (natif Equip @0x80b5fxx).
            var occ = (await s.InventoryRepo
                    .GetEquippedItemEntitiesAsync(charId))
                .FirstOrDefault(e => e.Slot == 7);
            if (occ != null)
            {
                var occType = s.GameData.Items
                    .FirstOrDefault(i => i.Id == occ.ItemId)?.EquipLoc;
                if (occType is 7 or 19 && await SlotFree(8))
                    canonical = 8;
            }
        }
        // dst explicite : 8 admis pour les types 7/19 (1H), sinon loc
        // forcé — une 2H (type 9) demandée en 8 repart en 7 (natif).
        if (t is 7 or 19 && dstSlot == 8)
        {
            // Off-hand 1H seulement en dual-wield : main absente ou
            // tenue à deux mains (type 9) → la 1H remplace la main
            // (sinon une 2H éjectée laissait la 1H en off-hand et la
            // main vide — client qui prédit 8 car 7 est occupé).
            var mainItem = (await s.InventoryRepo
                    .GetEquippedItemEntitiesAsync(charId))
                .FirstOrDefault(e => e.Slot == 7 && e.BagId == 0);
            var mainType = mainItem == null ? 0 : s.GameData.Items
                .FirstOrDefault(i => i.Id == mainItem.ItemId)?.EquipLoc ?? 0;
            return mainType is 7 or 19 ? (short)8 : (short)7;
        }
        return (short)canonical;
    }
    /// <summary>Applique les auto-déséquipements de
    /// `CItemFactory::DoWeaponUnEquip` @0x8092e70 avant l'équipe. Les
    /// slots 7/8/10 sont natifs; la classe 0x14 conserve les armes de la
    /// main avec le slot distance. Chaque item déplacé garde son serial et
    /// reçoit son propre NCID 277 avant l'ack de l'équipe demandée.</summary>
    internal static async Task<bool> ClearWeaponConflictsAsync(
        ZoneServices s, int charId, int itemId, short loc, int equipGroup,
        Eden.Core.Networking.Session.NetSession session,
        CancellationToken token)
    {
        if (equipGroup != 0) return true;
        var def = s.GameData.Items.FirstOrDefault(i => i.Id == itemId);
        if (def is null) return false;
        var equipped = (await s.InventoryRepo
                .GetEquippedItemEntitiesAsync(charId))
            .Where(e => e.BagId == equipGroup).ToList();
        var main = equipped.FirstOrDefault(e => e.Slot == 7);
        var off = equipped.FirstOrDefault(e => e.Slot == 8);
        var ranged = equipped.FirstOrDefault(e => e.Slot == 10);
        var mainIs2H = main != null && s.GameData.Items
            .FirstOrDefault(i => i.Id == main.ItemId)?.EquipLoc == 9;
        var character = await s.Characters.GetCharacterByIdAsync(charId);
        var keepsMainAndDistance =
            character?.ClassId ==
            Eden.Core.Game.Inventory.NativeEquipmentSlots
                .WeaponSlotExceptionClassId;
        var displaced = new List<Eden.Core.Game.Inventory.ItemEntity>();

        if (def.EquipLoc == 9 && loc == 7 && off != null)
            displaced.Add(off); // New two-hander consumes the off-hand.
        if (loc == 8 && mainIs2H && main != null)
            displaced.Add(main); // An off-hand cannot coexist with a 2H.
        if (!keepsMainAndDistance && loc == 10)
        {
            if (off != null) displaced.Add(off);
            if (main != null) displaced.Add(main);
        }
        else if (!keepsMainAndDistance && loc is 7 or 8 && ranged != null)
            displaced.Add(ranged);

        displaced = displaced.DistinctBy(e => e.Id).ToList();
        if (displaced.Count == 0) return true;
        if (await CountFreeInventorySlotsAsync(s, charId) < displaced.Count)
        {
            EdenLog.Write($"[Zone] equip-conflict char={charId} " +
                $"needs {displaced.Count} inventory slots; refusing item={itemId}");
            // Texte natif 0x4f9 (1273) « Pas assez de place dans le sac
            // à dos » — jamais de refus silencieux ni de destruction.
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = 0x4f9, Field2 = string.Empty,
            }, token);
            return false;
        }

        foreach (var entity in displaced)
        {
            var free = await s.InventoryRepo.GetAnyAvailableSlotAsync(charId);
            if (free == null || !await s.InventoryRepo.MoveToInventoryAsync(
                    charId, entity.BagId, entity.Slot, free.Value.BagId,
                    free.Value.Slot))
                return false;

            // NCID 277 auto-déséquipement prouvé par DoWeaponUnEquip
            // @0x8092e70 : source équipement 1, destination inventaire 0.
            await session.SendAsync(new CNC_CZ_ClientSwitchItemLocationOK
            {
                Field1 = 1, // s16 : source container type (equipment).
                Field2 = (short)entity.Slot, // s16 : source equipment loc.
                Field3 = (short)entity.BagId, // s16 : source equipment group.
                Field4 = 0, // s16 : destination container type (inventory).
                Field5 = (short)free.Value.Slot, // s16 : resolved dest slot.
                Field6 = (short)free.Value.BagId, // s16 : open bag index.
                Field7 = 1, // u8 : native success result.
            }, token);
            // Les observateurs doivent aussi voir l'arme disparaître
            // (276 slot vidé — soi exclu, le 277 couvre l'acteur) : sans
            // ça un arc auto-déséquipé restait affiché chez les autres.
            await EmitChangeEquipAsync(s, session, charId,
                (short)entity.Slot, (short)entity.BagId, null, token);
            // Pose explicite de la pièce déséquipée côté client (le 277
            // seul laissait le client deviner l'item via l'état porté).
            var unequipped = await s.InventoryRepo.GetItemAsync(charId,
                free.Value.BagId, free.Value.Slot);
            if (unequipped != null)
                await session.SendPacketAsync(274,
                    NativeItemCodec.BuildPutItem(unequipped), token);
        }
        return true;
    }
    /// <summary>Compte les places libres dans tous les sacs ouverts pour
    /// refuser avant de déplacer un premier équipement si le lot de
    /// déséquipements natifs ne peut pas tenir.</summary>
    private static async Task<int> CountFreeInventorySlotsAsync(
        ZoneServices s, int charId)
    {
        var capacities = await s.InventoryRepo
            .GetAllBagCapacitiesAsync(charId);
        var items = await s.InventoryRepo.GetLiveItemsAsync(charId);
        return capacities.Sum(kv => Math.Max(0, kv.Value -
            items.Count(i => i.BagId == kv.Key &&
                i.Slot >= 0 && i.Slot < kv.Value)));
    }
    /// <summary>
    /// Coût de réparation natif — désasm
    /// `CItemFactory::GetItemRepairPrice` @0x808c480 et
    /// `GetItemRepairAllEquipPrice` @0x808c570 :
    ///   éligible = type lapis &lt; 2 (arme = kinds 17-29 / 0x11-0x1d,
    ///   armure = kinds 1-16 et 0x25-0x28 — sélection GetAllWeapons/
    ///   GetAllArmors), durabilité actuelle ≠ max ;
    ///   coût = (int)((int)(repairPrice × facteur) / maxDur × missingDur),
    ///   min 1 — repairPrice = S_Item col 76 (CItemData+0x114 → lapis
    ///   CItem+0x14 ; l'échelle addattr +0xe0 n'est pas encore portée),
    ///   facteur = S_Parameter 347 = 0.2 (AllFormulaParas+0x56c).
    /// </summary>
    internal static long ItemRepairCost(ZoneServices s,
        Eden.Core.Game.Inventory.ItemEntity it)
    {
        var def = s.GameData.Items.FirstOrDefault(d => d.Id == it.ItemId);
        if (def == null || it.MaxDurability <= 0 ||
            it.Durability >= it.MaxDurability ||
            !IsRepairableKind(def.Kind)) return 0;
        var factor = s.GameData.Parameters.Get(
            ParameterParser.RepairCostFactor);
        var baseCost = (int)(def.RepairPrice * factor);
        var cost = (int)(baseCost / (double)it.MaxDurability *
            (it.MaxDurability - it.Durability));
        return cost > 0 ? cost : 1;
    }
    /// <summary>Kinds réparables natifs — GetAllArmors : kinds 1-16 +
    /// 0x25-0x28 (type lapis 1) ; GetAllWeapons : kinds 0x11-0x1d
    /// (type 0). GetItemRepairPrice n'accepte que type &lt; 2.</summary>
    private static bool IsRepairableKind(int kind)
        => kind is >= 1 and <= 29 or >= 37 and <= 40;

    /// <summary>Coût d'identification natif —
    /// `CItemFactory::GetItemIdentifyPrice` @0x808c3f0 :
    /// `max(1, floor(SysPrice × 0.05))`, constante 0.05f codée en dur
    /// (`_DAT_087cc628` — PAS le param 346). Éligible : type lapis
    /// &lt; 2 (mêmes kinds qu'ItemRepairCost — l'identification ne
    /// concerne que les équipements arme/armure).</summary>
    internal static long ItemIdentifyCost(ZoneServices s, int itemId)
    {
        var def = s.GameData.Items.FirstOrDefault(d => d.Id == itemId);
        if (def == null || !IsRepairableKind(def.Kind)) return 0;
        var cost = (long)(def.SellPrice * 0.05f);
        return cost > 0 ? cost : 1;
    }
    /// <summary>Envoie le 358 ClientGetStorageData — corps désassemblé
    /// lapis::CNC_CZ_ClientGetStorageData::Serialize @0x82366b0 :
    /// [s16 type][u8 nContainers] puis par conteneur [s16 cap][u8 flag]
    /// [s16 len][bitmap MSB][item-records]. La map native char+0x510
    /// est indexée par onglet (container_index, -1 → 0). Utilisé à
    /// l'ouverture (62) et en refresh après dépôt/retrait.</summary>
    private static async Task SendStorageDataAsync(
        Eden.Core.Networking.Session.NetSession session, ZoneServices s,
        int charId, short type, CancellationToken token)
    {
        var items = await s.Bank.GetBankItemsAsync(charId);
        var capacity = (short)await s.Bank.GetStorageCapacityAsync(charId);
        var containers = items
            .GroupBy(i => Math.Max(i.ContainerIndex, 0))
            .OrderBy(g => g.Key)
            .Select(g => (capacity, (byte)0,
                (IReadOnlyList<NativeItemCodec.StorageItem>)g
                    .Select(i => new NativeItemCodec.StorageItem(
                        i.Slot, i.ItemId, i.Durability,
                        i.MaxDurability, i.IsBound,
                        i.EnchantLevel, i.CreateTime))
                    .ToList()))
            .ToList();
        // Banque vide : un conteneur déclaré pour que le client
        // affiche les slots (count=0 → fenêtre sans onglet).
        if (containers.Count == 0)
            containers.Add((capacity, (byte)0,
                (IReadOnlyList<NativeItemCodec.StorageItem>)
                Array.Empty<NativeItemCodec.StorageItem>()));
        await session.SendPacketAsync(358,
            NativeItemCodec.BuildGetStorageData(type, containers), token);
    }
    /// <summary><c>CCharacter::NotifyChangeEquipment</c> natif
    /// @0x843df10 : 276 <c>ClientChangeEquip</c> (delta d'un slot)
    /// broadcasté aux observateurs (<c>BroadCastToCanSee</c>, soi
    /// exclu). Appelé par Equip/UnEquip/EquipForSwitch/DropItem — le
    /// natif n'émet PAS de 282/283 sur le chemin normal (282 n'est
    /// que la réponse à UpdateEquipment 8). La révision char+0x4dc
    /// (<see cref="ZoneServices.ModelVersions"/>) est incrémentée à
    /// chaque appel comme le natif.</summary>
    internal static async Task EmitChangeEquipAsync(ZoneServices s,
        Eden.Core.Networking.Session.NetSession self, int charId,
        short slot, short container,
        Eden.Core.Game.Inventory.ItemEntity? item,
        CancellationToken token)
    {
        var rev = s.ModelVersions.AddOrUpdate(charId, (byte)1,
            (_, v) => (byte)(v + 1));
        var body = NativeItemCodec.BuildChangeEquip(charId,
            (ushort)slot, container, rev, item);
        foreach (var other in s.Sessions.All)
        {
            if (other.SessionId == self.SessionId) continue;
            try { await other.SendPacketAsync(276, body, token); }
            catch { /* session morte */ }
        }
    }

    /// <summary>Refresh côté acteur après une mutation d'équipement —
    /// <c>CCharacter::RefreshEQsAttr</c> natif → 280 vitals/stats à
    /// soi (ProfileStatEmitter) + invalidation du cache d'anims d'arme
    /// (CCharKfm rebuild ses pools single/dual au changement d'arme).
    /// </summary>
    internal static async Task EmitSelfEquipRefreshAsync(
        ZoneServices s, Eden.Core.Networking.Session.NetSession session,
        int charId, CancellationToken token)
    {
        s.InvalidateWeaponAnim?.Invoke(session.SessionId);
        await ProfileStatEmitter.SendForCharacterAsync(
            session, s, charId, token);
    }

    /// <summary>Locs où le client a pu peindre l'icône avant la
    /// réponse 277 : il applique SON routage EquipLoc→slot au drag
    /// (même table que `CItemFactory::Equip` @0x80b5810, sans les
    /// gates de kind — un item pet avec EquipLoc=2 est peint en slot
    /// body par le client puis refusé par le serveur → fantôme).
    /// dstSlot explicite prioritaire ; sinon la loc canonique et les
    /// paires dual (7↔8, 12↔13, 14↔15) où le client prend le premier
    /// libre.</summary>
    private static IEnumerable<short> GhostLocCandidates(
        ZoneServices s, int itemId, short dstSlot)
    {
        if (dstSlot >= 0)
        {
            yield return dstSlot;
            yield break;
        }
        var def = s.GameData.Items.FirstOrDefault(i => i.Id == itemId);
        if (def == null) yield break;
        var t = def.EquipLoc;
        var canonical = t switch { 19 => 7, 35 => 10, 9 => 7, _ => t };
        if (canonical is >= 1 and <= 21)
            yield return (short)canonical;
        if (t is 7 or 19) yield return 8;
        if (t is 11 or 20) { yield return 12; yield return 13; }
        if (t == 12) { yield return 14; yield return 15; }
    }

    /// <summary>Purge un loc fantôme du panneau équipement via un
    /// 275 ClientDropItem (type=1) — paquet traité en jeu (ack de
    /// suppression normal), contrairement au 311 qui n'est rechargé
    /// qu'à l'entrée de zone. Jamais sur un loc occupé en DB : un
    /// vrai item conserverait son icône sans purge. grp = groupe
    /// d'équipement (0 tenue, 1 panneau pet, 2-4 pages d'ensemble).
    /// </summary>
    internal static async Task PurgeEquipGhostAsync(ZoneServices s,
        Eden.Core.Networking.Session.NetSession session, int charId,
        short loc, short grp, CancellationToken token)
    {
        if (loc < 0) return;
        if (await s.InventoryRepo.GetEquipmentItemAsync(charId, grp,
                loc) != null)
            return;
        await session.SendAsync(new CNC_CZ_ClientDropItem
        {
            Field1 = 1,   // type de conteneur équipement (dstType 1)
            Field2 = loc, // loc à vider dans le panneau
            Field3 = grp, // groupe d'équipement (index conteneur)
        }, token);
    }

    /// <summary>Purge tous les locs candidats d'une équipe refusée —
    /// le client peint avant la réponse et nos gates de kind (pet,
    /// œuf, glyphe, consommable) refusent là où son routage local
    /// avait trouvé un slot → l'icône reste posée sur un loc vide.
    /// </summary>
    internal static async Task PurgeEquipGhostsAsync(ZoneServices s,
        Eden.Core.Networking.Session.NetSession session, int charId,
        int itemId, short dstSlot, short grp, CancellationToken token)
    {
        foreach (var gl in GhostLocCandidates(s, itemId, dstSlot))
            await PurgeEquipGhostAsync(s, session, charId, gl, grp,
                token);
    }

    /// <summary>Refresh équipement complet après une mutation du
    /// conteneur équipé (UpdateEquipment 8, drop type=1) : broadcast
    /// 282 ClientUpdateEquipment + 283 modification de modèle à la
    /// vue, puis NotifyRefreshAttr natif → ProfileStatEmitter au
    /// demandeur. Invalidation du cache d'anims d'arme (CCharKfm
    /// rebuild ses pools single/dual au changement d'arme).</summary>
    internal static async Task BroadcastEquipmentAsync(ZoneServices s,
        Eden.Core.Networking.Session.NetSession session, int charId,
        CancellationToken token)
    {
        // Natif : le ctor du 282 itère UNIQUEMENT le conteneur
        // d'équipement d'index 0 (lower_bound(0) sur la map char+0x4fc)
        // — les groupes 1-5 sont des pages stockées, pas la tenue
        // affichée. Avant : tous les groupes étaient fusionnés par loc
        // → le client voyait des items d'autres pages comme portés.
        var equipped = (await s.InventoryRepo
                .GetEquippedItemEntitiesAsync(charId))
            .Where(i => i.BagId == 0 && i.Slot is >= 0 and < 32)
            .GroupBy(i => i.Slot)
            .Select(g => (Loc: g.Key, Item: g.First()))
            .ToList();
        var body282 = NativeItemCodec.BuildUpdateEquipment(
            charId, equipped);
        var locMap = equipped.ToDictionary(e => e.Loc,
            e => e.Item.ItemId);
        var body283 = NativeItemCodec.BuildModelModify(charId, locMap);
        foreach (var other in s.Sessions.All)
        {
            try
            {
                await other.SendPacketAsync(282, body282, token);
                await other.SendPacketAsync(283, body283, token);
            }
            catch { /* session morte */ }
        }
        s.InvalidateWeaponAnim?.Invoke(session.SessionId);
        await ProfileStatEmitter.SendForCharacterAsync(
            session, s, charId, token);
    }
}
