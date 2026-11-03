# 07 - Hôtel des Ventes (Auction)

> **Status:** 🟡 En cours d'implémentation  
> **Priorité:** HAUTE  
> **Complexité:** Élevée

---

## 1. Description du Système

Le système d'hôtel des ventes permet:
- Mise en vente d'items par les joueurs
- Recherche et filtres par type, qualité, prix
- Durée de vente limitée
- Frais de vente (commission)
- Récupération des items/gold non vendus
- Catégories d'items (armes, armures, matériaux, etc.)

### Fonctionnalités Clés
- **Listing:** Création d'une vente avec prix, durée, quantité
- **Recherche:** Filtres par nom, type, qualité, prix
- **Achat:** Achat direct par autre joueur
- **Annulation:** Récupération de l'item avant vente
- **Commission:** Pourcentage du prix pris par le système

---

## 2. Symboles lapis:: Analysés

### Auction System
- `CAuctionShop` - Boutique enchère
- `CAuctionShop::OpenShop` - Ouverture boutique enchère
- `CNE_CZ_ZoneServerAuctionBuy` - Achat enchère
- `CNE_CZ_ZoneServerAuctionBuyQuery` - Query achat enchère
- `CNE_CZ_ZoneServerAuctionCancel` - Annulation enchère
- `CNE_CZ_ZoneServerAuctionSell` - Vente enchère
- `CNE_CZ_ZoneServerAuctionSellQuery` - Query vente enchère

### Logging
- `CLogFactory::ItemMallEnchantLog` - Log enchant item mall

---

## 3. Fichiers de Données Associés

- Aucun fichier .ini spécifique à l'auction
- Les prix sont déterminés par les joueurs
- Les commissions sont définies dans le code

---

## 4. Protocole Réseau

### CNE_CZ_ZoneServerAuctionSell (Client → Zone)
**Champs:**
- `ItemId` (int)
- `Quantity` (short)
- `Price` (int) - Prix de vente en gold
- `Duration` (short) - Durée en heures

### CNC_CZ_ClientAuctionList (Zone → Client)
**Champs:**
- `AuctionId` (int)
- `ItemId` (int)
- `SellerName` (string)
- `Price` (int)
- `Quantity` (short)
- `TimeLeft` (short) - Temps restant en minutes

### CNE_CZ_ZoneServerAuctionBuy (Client → Zone)
**Champs:**
- `AuctionId` (int)

---

## 5. Base de Données PostgreSQL

### Table active_auctions
```sql
CREATE TABLE active_auctions (
    id SERIAL PRIMARY KEY,
    seller_id INTEGER NOT NULL REFERENCES player_characters(id),
    item_id INTEGER NOT NULL,
    quantity INTEGER DEFAULT 1,
    price INTEGER NOT NULL,
    duration_hours INTEGER DEFAULT 24,
    created_at TIMESTAMP DEFAULT NOW(),
    expires_at TIMESTAMP NOT NULL,
    status VARCHAR(20) DEFAULT 'active' -- 'active', 'sold', 'cancelled', 'expired'
);
```

### Table auction_history
```sql
CREATE TABLE auction_history (
    id SERIAL PRIMARY KEY,
    auction_id INTEGER REFERENCES active_auctions(id),
    seller_id INTEGER REFERENCES player_characters(id),
    buyer_id INTEGER REFERENCES player_characters(id),
    item_id INTEGER,
    quantity INTEGER,
    price INTEGER,
    sold_at TIMESTAMP DEFAULT NOW()
);
```

---

## 6. Implémentation C#

### 6.1 Entités

#### AuctionEntity.cs
```csharp
namespace Eden.Core.Game.Auction;

/// <summary>
/// Vente en cours dans l'hôtel des ventes
/// </summary>
public sealed class AuctionEntity
{
    public int Id { get; set; }
    public int SellerId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public int DurationHours { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "active"; // 'active', 'sold', 'cancelled', 'expired'
}
```

### 6.2 Service d'Auction

#### AuctionService.cs
```csharp
namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion de l'hôtel des ventes
/// Correspond à CAuctionShop
/// </summary>
public sealed class AuctionService
{
    private readonly IAuctionRepository _auctionRepository;
    private const int AuctionFeePercent = 5;
    
    public AuctionService(IAuctionRepository auctionRepository)
    {
        _auctionRepository = auctionRepository;
    }
    
    /// <summary>
    /// Crée une vente
    /// Correspond à CNE_CZ_ZoneServerAuctionSell
    /// </summary>
    public async Task<(bool Success, string ErrorMessage, int? AuctionId)> CreateAuctionAsync(
        int sellerId,
        int itemId,
        int quantity,
        int price,
        int durationHours)
    {
        // 1. Vérifier que le joueur a l'item
        // 2. Calculer la commission (5%)
        // 3. Créer l'auction
        // 4. Retirer l'item de l'inventaire
        
        var auction = new AuctionEntity
        {
            SellerId = sellerId,
            ItemId = itemId,
            Quantity = quantity,
            Price = price,
            DurationHours = durationHours,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(durationHours),
            Status = "active"
        };
        
        var auctionId = await _auctionRepository.CreateAuctionAsync(auction);
        return (true, string.Empty, auctionId);
    }
    
    /// <summary>
    /// Achète un item
    /// Correspond à CNE_CZ_ZoneServerAuctionBuy
    /// </summary>
    public async Task<(bool Success, string ErrorMessage)> BuyAuctionAsync(int buyerId, int auctionId)
    {
        // 1. Vérifier que l'auction existe et est active
        // 2. Vérifier que le buyer a assez de gold
        // 3. Calculer la commission
        // 4. Transférer le gold
        // 5. Transférer l'item
        // 6. Marquer l'auction comme sold
        
        return (true, string.Empty);
    }
    
    /// <summary>
    /// Annule une vente
    /// Correspond à CNE_CZ_ZoneServerAuctionCancel
    /// </summary>
    public async Task<bool> CancelAuctionAsync(int sellerId, int auctionId)
    {
        // 1. Vérifier que le seller est bien le vendeur
        // 2. Vérifier que l'auction est encore active
        // 3. Remettre l'item dans l'inventaire
        // 4. Marquer l'auction comme cancelled
        
        return true;
    }
}
```

---

## 7. Tests de Validation

### AuctionTests.cs
```csharp
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class AuctionTests
{
    private readonly AuctionService _service = new(null!);
    
    [Fact]
    public void CreateAuction_WithValidItem_ReturnsSuccess()
    {
        // Test basique de structure
        var request = new { ItemId = 1, Price = 1000, DurationHours = 24 };
        Assert.True(request.Price > 0);
        Assert.True(request.DurationHours > 0);
    }
}
```

---

## 8. À Faire (TODO)

- [ ] Implémenter la recherche et filtres
- [ ] Implémenter les catégories d'items
- [ ] Créer les tables active_auctions et auction_history
- [ ] Implémenter les handlers réseau
- [ ] Implémenter la commission
- [ ] Implémenter l'expiration automatique
- [ ] Tests d'intégration

---

## 9. Notes de Reverse Engineering

- La commission est généralement de 5% du prix de vente
- La durée de vente est limitée (24h, 48h, 72h)
- Les items expirés sont retournés au vendeur
- L'acheteur paie le prix demandé, le vendeur reçoit le prix moins la commission

---

*Document généré le 15 Octobre 2026*
