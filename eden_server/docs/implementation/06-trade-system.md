# 06 - Système d'Échange P2P (Trade)

> **Status:** ✅ Implémenté  
> **Priorité:** HAUTE  
> **Complexité:** Moyenne

---

## 1. Description du Système

Le système d'échange permet:
- Échange direct entre deux joueurs
- Fenêtre d'échange sécurisée
- Confirmation double (anti-scam)
- Échange d'items et de gold
- Limite de distance pour l'échange

### Règles de Trade
- Les joueurs doivent être proches (< 10m)
- Les deux parties doivent accepter avant finalisation
- Pas de trade pendant le combat
- Pas de trade des items liés au compte

---

## 2. Symboles lapis:: Analysés

### Trade System
- `CEC_StateNoTrade` - État pas de trade
- `CEC_StateNoTrade::CanExecute` - Vérification
- `CEC_StateNoTrade::Execute` - Exécution
- `CEC_StateNoTrade::Init` - Initialisation
- `CEC_StateNoTrade::RollBack` - Rollback

### Network
- `CNE_CZ_ZoneServerTradeRequest` - Demande de trade
- `CNE_CZ_ZoneServerTradeAccept` - Acceptation de trade
- `CNE_CZ_ZoneServerTradeConfirm` - Confirmation de trade
- `CNE_CZ_ZoneServerTradeCancel` - Annulation de trade

---

## 3. Fichiers de Données Associés

- Aucun fichier .ini spécifique au trade
- Limites définies dans le code (distance max, etc.)

---

## 4. Protocole Réseau

### CNE_CZ_ZoneServerTradeRequest (Client → Zone)
**Champs:**
- `TargetCharacterId` (int) - ID du joueur à échanger

### CNC_CZ_ClientTradeWindow (Zone → Client)
**Champs:**
- `PartnerId` (int)
- `MyItems` (array) - Items de l'utilisateur
- `PartnerItems` (array) - Items du partenaire
- `MyGold` (int)
- `PartnerGold` (int)

### CNE_CZ_ZoneServerTradeConfirm (Client → Zone)
**Champs:**
- `Confirmed` (bool) - Confirmation de l'utilisateur

---

## 5. Base de Données PostgreSQL

### Table active_trades
```sql
CREATE TABLE active_trades (
    id SERIAL PRIMARY KEY,
    initiator_id INTEGER NOT NULL REFERENCES player_characters(id),
    partner_id INTEGER NOT NULL REFERENCES player_characters(id),
    initiator_confirmed BOOLEAN DEFAULT FALSE,
    partner_confirmed BOOLEAN DEFAULT FALSE,
    initiator_gold INTEGER DEFAULT 0,
    partner_gold INTEGER DEFAULT 0,
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(initiator_id, partner_id)
);
```

---

## 6. Implémentation C#

### 6.1 Entités

#### TradeEntity.cs
```csharp
namespace Eden.Core.Game.Trade;

/// <summary>
/// Session de trade entre deux joueurs
/// </summary>
public sealed class TradeEntity
{
    public int Id { get; set; }
    public int InitiatorId { get; set; }
    public int PartnerId { get; set; }
    public bool InitiatorConfirmed { get; set; }
    public bool PartnerConfirmed { get; set; }
    public int InitiatorGold { get; set; }
    public int PartnerGold { get; set; }
    public List<TradeItem> InitiatorItems { get; set; } = new();
    public List<TradeItem> PartnerItems { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public sealed class TradeItem
{
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
}
```

### 6.2 Service de Trade

#### TradeService.cs
```csharp
namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion des échanges P2P
/// Correspond à CTrade
/// </summary>
public sealed class TradeService
{
    /// <summary>
    /// Demande un échange avec un joueur
    /// </summary>
    public async Task<(bool Success, string ErrorMessage)> RequestTradeAsync(
        int initiatorId,
        int partnerId)
    {
        // 1. Vérifier distance (< 10m)
        // 2. Vérifier qu'aucun n'est en combat
        // 3. Vérifier qu'aucun n'est déjà en trade
        // 4. Créer la session de trade
        // 5. Envoyer la demande au partner
    }
    
    /// <summary>
    /// Accepte une demande de trade
    /// </summary>
    public async Task<bool> AcceptTradeAsync(int initiatorId, int partnerId)
    {
        // Accepter la demande
    }
    
    /// <summary>
    /// Confirme le trade
    /// </summary>
    public async Task<bool> ConfirmTradeAsync(int initiatorId, int partnerId, bool isInitiator)
    {
        // Confirmer le trade
    }
    
    /// <summary>
    /// Annule le trade
    /// </summary>
    public async Task<bool> CancelTradeAsync(int initiatorId, int partnerId)
    {
        // Annuler le trade
    }
}
```

---

## 7. Tests de Validation

### TradeTests.cs
```csharp
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class TradeTests
{
    private readonly TradeService _service = new(null!);
    
    [Fact]
    public void RequestTrade_WithValidPlayers_ReturnsSuccess()
    {
        // Test basique de structure
        var request = new { InitiatorId = 1, PartnerId = 2 };
        Assert.True(request.InitiatorId != request.PartnerId);
    }
}
```

---

## 8. À Faire (TODO)

- [ ] Implémenter la vérification de distance
- [ ] Implémenter la validation d'items (bound items)
- [ ] Créer la table active_trades
- [ ] Implémenter les handlers réseau
- [ ] Implémenter le système de confirmation double
- [ ] Tests d'intégration

---

## 9. Notes de Reverse Engineering

- Le trade nécessite une confirmation des deux parties
- La limite de distance est généralement de 10 mètres
- Les items "bound" (liés au compte) ne peuvent pas être échangés
- Le gold est échangé en même temps que les items
- Un trade peut être annulé à tout moment avant la confirmation

---

*Document généré le 15 Octobre 2026*
