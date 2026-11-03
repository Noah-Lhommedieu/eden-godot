# 04 - Système d'Inventaire et Sacs

> **Status:** ✅ Implémenté (auto-stack, swap, split, enchant — fidèle à CheckAutoStackItem)  
> **Priorité:** MAX (Fondation du jeu)  
> **Complexité:** Élevée

---

## 1. Description du Système

Le système d'inventaire gère:
- Les sacs et slots d'inventaire du joueur
- Les types d'items (équipement, consommable, matériaux, etc.)
- Le stack/stack splitting des items
- Le tri et auto-stack
- La location de slots (slot rental)
- Les sacs de différentes tailles
- L'équipement (weapon, armor, accessories)

### Types de Slots
- **Equipement:** Weapon, Helmet, Armor, Gloves, Boots, Ring, Necklace
- **Consommable:** Potions, scrolls, food
- **Matériaux:** Crafting materials
- **Quêtes:** Quest items
- **Misc:** Autres items

### Capacités de Sac
- **Sac de base:** 20 slots
- **Sac small:** 30 slots
- **Sac medium:** 40 slots
- **Sac large:** 50 slots
- **Slot rental:** Extension temporaire

---

## 2. Symboles lapis:: Analysés

### Inventory Management
- `CInventory` - Classe principale d'inventaire
- `CInventory::AddItem` - Ajout d'item
- `CInventory::RemoveItem` - Suppression d'item
- `CInventory::MoveItem` - Déplacement d'item
- `CInventory::SplitItem` - Split de stack
- `CInventory::MergeItem` - Merge de stacks
- `CInventory::SortItems` - Tri d'items

### Containers
- `CBagContainer` - Conteneur de sac
- `CBagContainer::GetSlotCount` - Nombre de slots
- `CBagContainer::GetItemBySlot` - Item par slot
- `CBagContainer::SetItem` - Set item dans slot

### Item Operations
- `CAddItemControl` - Contrôle d'ajout d'item
- `CAddItemControl::OnInventoryFull` - Quand inventaire plein
- `CCloneItem` - Item cloné (instance unique)
- `CCloneItem::GetMaxDurability` - Durabilité max
- `CCloneItem::GetRepairPrice` - Prix de réparation

### Network
- `CNE_CZ_ZoneServerArrangeItem` - Arrangement d'item
- `CNE_CZ_ZoneServerSplitItem` - Split d'item
- `CNE_CZ_ZoneServerMergeItem` - Merge d'item
- `CNE_CZ_ZoneServerUseItem` - Utilisation d'item
- `CNE_CZ_ZoneServerDropItem` - Drop d'item
- `CNE_CZ_ZoneServerDestroyItem` - Destruction d'item

---

## 3. Fichiers de Données Associés

### Items
- `T_Item.ini` - Liste des items (noms, descriptions)
- `S_Item.ini` - Stats des items (dégâts, stats, etc.)
- `S_Weapon.ini` - Stats des armes
- `S_Armor.ini` - Stats des armures
- `S_Accessory.ini` - Stats des accessoires

### Inventory
- `S_Bag.ini` - Configuration des sacs
- `S_BagSlot.ini` - Slots de sac

---

## 4. Protocole Réseau

### CNE_CZ_ZoneServerArrangeItem (Client → Zone)
**Champs:**
- `FromSlot` (short)
- `ToSlot` (short)
- `FromBagId` (short)
- `ToBagId` (short)

### CNC_CZ_ClientInventoryUpdate (Zone → Client)
**Champs:**
- `BagId` (short)
- `Slot` (short)
- `ItemId` (int)
- `Quantity` (short)
- `Durability` (short)

---

## 5. Base de Données PostgreSQL

### Table player_items
```sql
CREATE TABLE player_items (
    id SERIAL PRIMARY KEY,
    character_id INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
    bag_id INTEGER DEFAULT 0,
    slot INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    quantity INTEGER DEFAULT 1,
    durability INTEGER DEFAULT 100,
    max_durability INTEGER DEFAULT 100,
    is_equipped BOOLEAN DEFAULT FALSE,
    UNIQUE(character_id, bag_id, slot)
);
```

### Table inventory_slots
```sql
CREATE TABLE inventory_slots (
    character_id INTEGER PRIMARY KEY REFERENCES player_characters(id) ON DELETE CASCADE,
    bag_0_slots INTEGER DEFAULT 20,
    bag_1_slots INTEGER DEFAULT 0,
    bag_2_slots INTEGER DEFAULT 0,
    bag_3_slots INTEGER DEFAULT 0,
    bag_0_rental_expiry TIMESTAMP,
    bag_1_rental_expiry TIMESTAMP,
    bag_2_rental_expiry TIMESTAMP,
    bag_3_rental_expiry TIMESTAMP
);
```

---

## 6. Implémentation C#

### 6.1 Entités

#### ItemEntity.cs
```csharp
namespace Eden.Core.Game.Inventory;

/// <summary>
/// Instance d'item dans l'inventaire
/// Correspond à CCloneItem
/// </summary>
public sealed class ItemEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int BagId { get; set; }
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
    public bool IsEquipped { get; set; }
}
```

### 6.2 Service d'Inventaire

#### InventoryService.cs
```csharp
namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion de l'inventaire
/// Correspond à CInventory
/// </summary>
public sealed class InventoryService
{
    /// <summary>
    /// Ajoute un item à l'inventaire
    /// Correspond à CInventory::AddItem
    /// </summary>
    public async Task<(bool Success, string ErrorMessage, int? Slot)> AddItemAsync(
        int characterId,
        int itemId,
        int quantity,
        int preferredBagId = 0)
    {
        // 1. Chercher slot disponible
        // 2. Si item stackable, chercher stack existant
        // 3. Ajouter l'item
        // 4. Retourner le slot utilisé
    }
    
    /// <summary>
    /// Supprime un item de l'inventaire
    /// Correspond à CInventory::RemoveItem
    /// </summary>
    public async Task<bool> RemoveItemAsync(int characterId, int bagId, int slot, int quantity)
    {
        // Supprimer ou réduire quantité
    }
    
    /// <summary>
    /// Déplace un item d'un slot à un autre
    /// Correspond à CInventory::MoveItem
    /// </summary>
    public async Task<bool> MoveItemAsync(int characterId, int fromBagId, int fromSlot, int toBagId, int toSlot)
    {
        // Déplacement avec validation
    }
    
    /// <summary>
    /// Split un stack d'item
    /// Correspond à CInventory::SplitItem
    /// </summary>
    public async Task<bool> SplitItemAsync(int characterId, int bagId, int slot, int quantity, int toBagId, int toSlot)
    {
        // Split de stack
    }
}
```

---

## 7. Tests de Validation

### InventoryTests.cs
```csharp
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class InventoryTests
{
    [Fact]
    public void AddItem_WithAvailableSlot_ReturnsSlot()
    {
        // Test ajout item avec slot disponible
    }
    
    [Fact]
    public void SplitItem_WithValidQuantity_CreatesNewStack()
    {
        // Test split de stack
    }
}
```

---

## 8. À Faire (TODO)

- [ ] Parser T_Item.ini et S_Item.ini
- [ ] Implémenter les types d'items
- [ ] Implémenter le stack splitting
- [ ] Implémenter le système de slot rental
- [ ] Créer les tables player_items et inventory_slots
- [ ] Implémenter les handlers réseau
- [ ] Tests d'intégration

---

## 9. Notes de Reverse Engineering

- Chaque item a un ID unique (CloneItemID) pour éviter duplication
- La durabilité diminue avec l'utilisation
- Les items stackables ont une quantité max par stack
- Les slots sont indexés de 0 à capacité-1
- Les sacs peuvent être loués temporairement (rental)

---

*Document généré le 15 Octobre 2026*
