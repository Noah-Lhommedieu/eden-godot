# Plan — TradeWindow + VendingWindow (Vague 2 : échange J↔J + étal joueur)

> Agent trade/vending — fichiers propres :
> `scripts/protocol/ZoneTradeMessages.cs`, `ZoneVendingMessages.cs`,
> `scripts/hud/TradeWindow.cs`, `scripts/hud/VendingWindow.cs`,
> `tools/NetTests/ZoneTradeVendingTests.cs`.
> Édition chirurgicale partagée : `HudExtensions.cs` (2 lignes Mount).

## NCIDs audités (serveur C# + natif zone/decompiled.c)

### Trade (échange 2 joueurs)

C→S (handlers `Eden.Zone/Handlers/TradeHandlers.cs`) :

| NCID | Nom | Wire | Note |
|---|---|---|---|
| 36 | TradeRequest | `[u32 cible]` | charId cible (entityId joueur) ; rate-limit 0,5 s, dist ≤ 10 |
| 37 | TradeRequestAccept | `[u32 initiateur]` | → 332 aux deux |
| 38 | TradeAddItem | `[u16 bag][u16 slot][u16 tradeSlot][u16 count]` | ⚠ ordre C# (`bag=F1,slot=F2`) ≠ natif (`tradeSlot,container,loc,count` @Execute 0x82956a0) — on suit le serveur C# |
| 39 | TradeRemoveItem | `[u16 index]` | index dans MON offre (RemoveItemAsync tradeIndex) |
| 40 | TradeModifyGold | `[u32][u32 or]` | serveur lit F2 ; natif émet F1=F2 |
| 41 | TradeConfirm | vide | marque confirmé ; 2e confirm → exécution |
| 42 | TradeCancel | `[u32][s16]` | champs ignorés serveur |
| 56 | TradeReset | vide | → 353 à soi |
| 91 | TradeReady | vide | → 394 au partenaire |
| 92 | TradeUnConfirm | vide | → 395 au partenaire |

S→C (ZoneMessages.g.cs + NotifyClient natifs) :

| NCID | Nom | Wire | Destinataire natif / C# |
|---|---|---|---|
| 331 | ClientTradeRequest | `[u32 demandeur][str nom]` | cible — popup invite |
| 332 | ClientTradeStart | `[u32][str]` | les deux — ouvre la fenêtre |
| 333 | ClientTradeAddItem | `[u16 slot][u16 itemId][s16 count][u16][str nom][… 27 champs]` | natif→partenaire (item complet, format MASQUÉ bitmask : Serialize @0x8139f60) ; C#→demandeur, **tout à 0** (ACK) |
| 334 | ClientTradeRemoveItem | `[u16 index]` | natif→partenaire ; C#→demandeur (ACK, Value=0) |
| 335 | ClientTradeAddItemFail | `[u16][s16]` | demandeur |
| 336 | ClientTradeModifyGold | `[u32][u32 or]` | natif→partenaire ; C#→demandeur (ACK) |
| 337 | ClientTradeModifyGoldFail | `[u32][u32]` | demandeur |
| 338 | ClientTradeConfirm | vide | natif→partenaire ; C#→les deux |
| 339 | ClientTradeCancel | `[u32][s16 raison]` | natif→les deux ; C#→demandeur seul (gap) |
| 340 | ClientTradeComplete | vide | natif→les deux ; C# jamais émis |
| 353 | ClientTradeReset | vide | demandeur |
| 394 | ClientTradeReady | vide | partenaire |
| 395 | ClientTradeUnConfirm | `[u32]` | partenaire |

Gaps serveur documentés (résidu, pas de fix côté client) : le partenaire
ne reçoit JAMAIS les adds/or/removes de l'autre (333/334/336 émis au
demandeur en ACK vide) ; 339 non relayé au partenaire ; 340 jamais émis.
La colonne « son offre » restera donc vide en live jusqu'à fix serveur —
le client est prêt (parser natif des champs F1=slot/F2=itemId/F3=count/
F5=nom pour le format plat serveur + champs clés).

### Vending (étal joueur — ZoneEntryFlow.Vending.cs + VendorPackets.cs)

C→S :

| NCID | Wire | Rôle |
|---|---|---|
| 72 | `[i32 vendeur]` | ouvrir la vue d'étal (réutilise `ZoneAskVendorContainer`) |
| 74 | `[u8 mode][u8]` | fermer MON étal |
| 75 | `[i32 vendeur]` | fermer la vue (acheteur) |
| 76 | `[i32 vendeur][u16 n][n×(s16 qty,u8 idx)]` | acheter (qty=lot complet) |
| 77 | `[i32 region][str titre][u8 n][n×(s16 cont,s16 slot,s16 loc,i32 prix)]` | ouvrir/remplir MON étal — S001, régions 600/601/602 de s001.ini (dist² ≤ rayon², sinon rejet silencieux) |

S→C :

| NCID | Wire | Rôle |
|---|---|---|
| 375 | `[i32 charId][u8 mode]` | mon étal fermé (écho du 74) |
| 376 | `[i32 vendeur]` | l'étal consulté a fermé (+ texte 0xf07) |
| 377 | `[u8 action][u8 detail]` | statut : [1][2] ok ; [0][0x11..0x16] échecs |
| 378 | `[i32 vendeur][str titre][u8 n][n×98o]` | vue complète de l'étal (record VendorPackets.GoodsRecordBytes) |
| 379 | `[i32][u8]` | échec ouverture vue (target-miss) |

## Composants

- [ ] `ZoneTradeMessages.cs` — 10 classes C→S + 13 S→C + `TradeState`
      (offres, statuts, pending invite).
- [ ] `ZoneVendingMessages.cs` — 4 classes C→S (74/75/76/77) + 5 S→C
      (375-379) + `VendingState` (étal consulté + mon étal).
- [ ] `TradeWindow.cs` — `TradeWnd.layout` natif (MySlot1-8 drop
      {bag,slot}, GlodEdit/SilverEdit, Lock/Ok/Cancel, LockOn*Image,
      invite popup 331, bouton « Échange » → 36 sur cible joueur).
- [ ] `VendingWindow.cs` — `StallUI.layout` (vendre : slots drop,
      gold/silver prix, titre EditBox, liste, StallBtn/OutBtn) +
      `StallBuy.layout` (acheter : liste 98o, BuyBtn, CloseBtn) +
      bouton « Étal » persistant (ouvre le mode vente ; mode achat
      sur cible joueur via bouton cible).
- [ ] HudExtensions : Mount TradeWindow + VendingWindow.
- [ ] NetTests : round-trips codecs + layouts fils 378 (98o).
- [ ] Preuve : build 0/0, NetTests vert, live --hud-open TradeWnd/
      StallUI/StallBuy + shots godot-shots/trade-window.png +
      vending-window.png ; round-trip réel si 2e client possible.
