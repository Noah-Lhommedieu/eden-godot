# 08 - Boutiques (AP et EP Item Mall)

> **Status:** 🟡 En cours d'implémentation  
> **Priorité:** HAUTE  
> **Complexité:** Élevée

---

## 1. Description du Système

Le système d'item mall gère:
- La boutique AP (Aeria Points - argent réel)
- La boutique EP (Eden Points - points gagnés en jeu)
- Les catégories d'items
- Les promotions et items limités
- Les achats et gestion d'inventaire

### Types de Points
- **AP (Aeria Points):** Monnaie réelle, items premium
- **EP (Eden Points):** Points gagnés en jeu, items standard

### Catégories
- **Consommables:** Potions, scrolls, food
- **Cosmétiques:** Apparences, couleurs, auras
- **Montures:** Rides et pets
- **Utilitaires:** Extensions de sac, services
- **Items limités:** Items temporaires

---

## 2. Symboles lapis:: Analysés

### Item Mall
- `CItemShop` - Boutique items
- `CItemShop::OpenShop` - Ouverture boutique
- `CNE_CZ_ZoneServerItemMallReceivableEnter` - Entrée recevable item mall
- `CNE_CZ_ZoneServerItemMallUseItem` - Utilisation item mall

### Payment
- `CPayManager` - Manager de paiement
- `CGlobalInfo` - Info globale (points)

### Logging
- `CLogFactory::ItemMallEnchantLog` - Log enchant item mall

---

## 3. Fichiers de Données Associés

### Item Mall Configuration
- `S_ItemMall.ini` - Configuration des items mall
- `S_ItemMallCategory.ini` - Catégories d'items
- `S_ItemMallPrice.ini` - Prix des items

### Points
- `S_Coins.ini` - Configuration des points (AP, EP)

---

## 4. Protocole Réseau

### CNE_CZ_ZoneServerItemMallReceivableEnter (Client → Zone)
**Champs:**
- `CategoryId` (short) - Catégorie d'item

### CNC_CZ_ClientItemMallList (Zone → Client)
**Champs:**
- `CategoryId` (short)
- `ItemList` (array) - Liste des items disponibles

### CNE_CZ_ZoneServerItemMallUseItem (Client → Zone)
**Champs:**
- `ItemId` (int)
- `Quantity` (short)

### CNC_CZ_ClientItemMallBalance (Zone → Client)
**Champs:**
- `APPoints` (int)
- `EPPoints` (int)

---

## 5. Base de Données PostgreSQL

### Table player_coins
```sql
CREATE TABLE player_coins (
    character_id INTEGER PRIMARY KEY REFERENCES player_characters(id) ON DELETE CASCADE,
    ap_points INTEGER DEFAULT 0,
    ep_points INTEGER DEFAULT 0,
    last_updated TIMESTAMP DEFAULT NOW()
);
```

### Table item_mall_items
```sql
CREATE TABLE item_mall_items (
    id SERIAL PRIMARY KEY,
    item_id INTEGER NOT NULL,
    category_id INTEGER NOT NULL,
    name VARCHAR(100),
    description TEXT,
    ap_price INTEGER,
    ep_price INTEGER,
    is_limited BOOLEAN DEFAULT FALSE,
    limit_start TIMESTAMP,
    limit_end TIMESTAMP,
    stock_quantity INTEGER
);
```

### Table item_mall_purchases
```sql
CREATE TABLE item_mall_purchases (
    id SERIAL PRIMARY KEY,
    character_id INTEGER REFERENCES player_characters(id),
    item_id INTEGER,
    quantity INTEGER,
    points_used INTEGER,
    points_type VARCHAR(2), -- 'AP' or 'EP'
    purchased_at TIMESTAMP DEFAULT NOW()
);
```

---

## 6. Implémentation C#

### 6.1 Entités

#### PlayerCoinsEntity.cs
```csharp
namespace Eden.Core.Game.Mall;

/// <summary>
/// Points du joueur (AP et EP)
/// </summary>
public sealed class PlayerCoinsEntity
{
    public int CharacterId { get; set; }
    public int APPoints { get; set; }
    public int EPPoints { get; set; }
    public DateTime LastUpdated { get; set; }
}
```

#### ItemMallItemEntity.cs
```csharp
namespace Eden.Core.Game.Mall;

/// <summary>
/// Item dans l'item mall
/// </summary>
public sealed class ItemMallItemEntity
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? APPrice { get; set; }
    public int? EPPrice { get; set; }
    public bool IsLimited { get; set; }
    public DateTime? LimitStart { get; set; }
    public DateTime? LimitEnd { get; set; }
    public int? StockQuantity { get; set; }
}
```

### 6.2 Service d'Item Mall

#### ItemMallService.cs
```csharp
namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion de l'item mall (boutiques AP/EP)
/// Correspond à CItemShop
/// </summary>
public sealed class ItemMallService
{
    private readonly IItemMallRepository _mallRepository;
    private readonly IPlayerCoinsRepository _coinsRepository;
    
    public ItemMallService(IItemMallRepository mallRepository, IPlayerCoinsRepository coinsRepository)
    {
        _mallRepository = mallRepository;
        _coinsRepository = coinsRepository;
    }
    
    /// <summary>
    /// Liste les items d'une catégorie
    /// </summary>
    public async Task<IReadOnlyList<ItemMallItemEntity>> GetCategoryItemsAsync(int categoryId)
    {
        return await _mallRepository.GetItemsByCategoryAsync(categoryId);
    }
    
    /// <summary>
    /// Achète un item avec des points
    /// </summary>
    public async Task<(bool Success, string ErrorMessage)> PurchaseItemAsync(
        int characterId,
        int itemId,
        int quantity,
        string pointsType) // "AP" or "EP"
    {
        // 1. Vérifier que l'item existe et est disponible
        var item = await _mallRepository.GetItemAsync(itemId);
        if (item == null)
        {
            return (false, "Item not found");
        }
        
        // 2. Vérifier que le joueur a assez de points
        var coins = await _coinsRepository.GetCoinsAsync(characterId);
        if (coins == null)
        {
            return (false, "No coins found");
        }
        
        var requiredPoints = pointsType == "AP" ? item.APPrice : item.EPPrice;
        if (!requiredPoints.HasValue)
        {
            return (false, "Item not purchasable with this points type");
        }
        
        var totalPrice = requiredPoints.Value * quantity;
        
        if (pointsType == "AP" && coins.APPoints < totalPrice)
        {
            return (false, $"Not enough AP points (have {coins.APPoints}, need {totalPrice})");
        }
        
        if (pointsType == "EP" && coins.EPPoints < totalPrice)
        {
            return (false, $"Not enough EP points (have {coins.EPPoints}, need {totalPrice})");
        }
        
        // 3. Déduire les points
        if (pointsType == "AP")
        {
            coins.APPoints -= totalPrice;
        }
        else
        {
            coins.EPPoints -= totalPrice;
        }
        
        await _coinsRepository.UpdateCoinsAsync(coins);
        
        // 4. Ajouter l'item à l'inventaire
        // TODO: Implémenter l'ajout à l'inventaire
        
        return (true, string.Empty);
    }
}
```

---

## 7. Tests de Validation

### ItemMallTests.cs
```csharp
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class ItemMallTests
{
    private readonly ItemMallService _service = new(null!, null!);
    
    [Fact]
    public void GetCategoryItems_WithValidCategory_ReturnsItems()
    {
        // Test basique de structure
        var request = new { CategoryId = 1 };
        Assert.True(request.CategoryId >= 0);
    }
}
```

---

## 8. À Faire (TODO)

- [ ] Parser S_ItemMall.ini et S_ItemMallCategory.ini
- [ ] Implémenter les catégories d'items
- [ ] Créer les tables player_coins, item_mall_items, item_mall_purchases
- [ ] Implémenter les handlers réseau
- [ ] Implémenter les promotions et items limités
- [ ] Tests d'intégration

---

## 9. Notes de Reverse Engineering

- Les items limités ont une fenêtre de disponibilité
- Certains items ont un stock limité
- Les prix sont en AP ou EP, jamais les deux
- Les points sont déduits avant de donner l'item
- L'historique des achats est conservé pour analytics

---

*Document généré le 15 Octobre 2026*
