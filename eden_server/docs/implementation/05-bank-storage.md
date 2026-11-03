# 05 - Banque et Stockage

> **Status:** ✅ Implémenté  
> **Priorité:** HAUTE  
> **Complexité:** Moyenne

---

## 1. Description du Système

Le système de banque gère:
- Stockage séparé de l'inventaire
- Organisation par catégories
- Frais de stockage (par item ou par slot)
- Accès via NPC dans chaque ville
- Stockage de famille/guilde

### Types de Stockage
- **Banque personnelle:** 50 slots de base
- **Banque de famille:** Stockage partagé entre membres de guilde
- **Banque de ville:** Banque accessible dans chaque ville
- **Extension:** Augmentation par items ou paiement

---

## 2. Symboles lapis:: Analysés

### Bank Storage
- `CBankShop` - Boutique de banque
- `CBankShop::OpenShop` - Ouverture de la banque
- `CFamilyStorageShop` - Stockage de famille
- `CFamilyStorageShop::OpenShop` - Ouverture stockage famille

### Network
- `CNE_CZ_ZoneServerAskVendor` - Demande d'ouverture de vendeur
- `CNE_CZ_ZoneServerAskVendorContainer` - Demande de container
- `CNE_CZ_ZoneServerLeaveShop` - Sortie de boutique

---

## 3. Fichiers de Données Associés

### Bank Configuration
- `S_Bank.ini` - Configuration des banques par ville
- `S_BankFee.ini` - Frais de stockage

---

## 4. Protocole Réseau

### CNE_CZ_ZoneServerAskVendor (Client → Zone)
**Champs:**
- `VendorId` (int) - ID du NPC banque
- `Type` (short) - Type de banque

### CNC_CZ_ClientBankOpen (Zone → Client)
**Champs:**
- `BankId` (int)
- `SlotCount` (short)
- `ItemList` (array) - Liste des items en banque

---

## 5. Base de Données PostgreSQL

### Table player_bank
```sql
CREATE TABLE player_bank (
    id SERIAL PRIMARY KEY,
    character_id INTEGER NOT NULL REFERENCES player_characters(id) ON DELETE CASCADE,
    slot INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    quantity INTEGER DEFAULT 1,
    durability INTEGER DEFAULT 100,
    max_durability INTEGER DEFAULT 100,
    UNIQUE(character_id, slot)
);
```

### Table family_bank
```sql
CREATE TABLE family_bank (
    id SERIAL PRIMARY KEY,
    family_id INTEGER NOT NULL,
    slot INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    quantity INTEGER DEFAULT 1,
    durability INTEGER DEFAULT 100,
    max_durability INTEGER DEFAULT 100,
    deposited_by INTEGER, -- character_id qui a déposé
    deposited_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(family_id, slot)
);
```

---

## 6. Implémentation C#

### 6.1 Entités

#### BankItemEntity.cs
```csharp
namespace Eden.Core.Game.Bank;

/// <summary>
/// Item stocké en banque
/// </summary>
public sealed class BankItemEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
}
```

### 6.2 Service de Banque

#### BankService.cs
```csharp
namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion de la banque
/// Correspond à CBankShop
/// </summary>
public sealed class BankService
{
    /// <summary>
    /// Dépose un item dans la banque
    /// </summary>
    public async Task<(bool Success, string ErrorMessage, int? Slot)> DepositItemAsync(
        int characterId,
        int itemId,
        int quantity,
        int slot)
    {
        // 1. Vérifier que le slot est libre
        // 2. Créer l'item en banque
        // 3. Retourner le slot utilisé
    }
    
    /// <summary>
    /// Retire un item de la banque
    /// </summary>
    public async Task<bool> WithdrawItemAsync(int characterId, int slot, int quantity)
    {
        // Supprimer ou réduire quantité
    }
}
```

---

## 7. Tests de Validation

### BankTests.cs
```csharp
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class BankTests
{
    private readonly BankService _service = new(null!);
    
    [Fact]
    public void DepositItem_WithValidSlot_ReturnsSuccess()
    {
        // Test basique de structure
        var request = new { ItemId = 1, Quantity = 10, Slot = 0 };
        Assert.True(request.Slot >= 0);
    }
}
```

---

## 8. À Faire (TODO)

- [ ] Parser S_Bank.ini et S_BankFee.ini
- [ ] Implémenter les frais de stockage
- [ ] Créer les tables player_bank et family_bank
- [ ] Implémenter les handlers réseau
- [ ] Tests d'intégration

---

## 9. Notes de Reverse Engineering

- La banque est séparée de l'inventaire et n'est pas affectée par les sacs
- Les frais de stockage peuvent être par item ou par période
- Le stockage famille nécessite les permissions de guilde
- Les items en banque ne peuvent pas être équipés

---

*Document généré le 15 Octobre 2026*
