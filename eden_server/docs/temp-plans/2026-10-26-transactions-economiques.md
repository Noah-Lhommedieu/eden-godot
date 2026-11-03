# Transactions économiques — anti dupe/perte au crash

Mission : rendre transactionnels les chemins économiques critiques.
Pattern : une `NpgsqlConnection` + `BeginTransactionAsync` par
opération, `await` avant commit, `try/commit`/`catch/rollback`,
connexions créées dans la méthode, ordre de tables déterministe.

## Contrainte de fichiers

`InventoryRepository*.cs`, `CharacterRepository.cs`,
`FamilyBankRepository.cs`, `SocialHandlers.cs`,
`MissionServer.cs`, `ZoneEntryFlow.{cs,State,Lifecycle}.cs` sont hors
périmètre → les opérations qui traversent `InventoryService`/
`IInventoryRepository`/`ICharacterRepository` ne peuvent pas rejoindre
ma connexion. Stratégie :

- Méthodes transactionnelles portées par MES repositories
  (TradeRepository, MailRepository, BankRepository, SocialRepository)
  avec le SQL brut des deux tables jointes (inventaire inclus — même
  base fndb1).
- Services : `is ConcreteRepo` → chemin tx ; sinon fallback legacy
  (tests in-memory inchangés — signatures publiques conservées).
- Or en delta atomique partout où je touche :
  `gold = LEAST(999999999, GREATEST(0, gold + @d))` ; pour les services
  à delegates (`ShopService`/`TradeService`), `AddGoldAsync` via
  delegate dédié ou `_setGold.Target is ICharacterRepository`,
  fallback absolu pour les lambdas de test.

## Travaux par chemin

- [x] `TradeService.AddItemAsync` (réservation) : débit inventaire
  gardé (`durability - @q` si `>= @q`, DELETE sinon) + UPDATE
  `active_trades` en UNE tx via `TradeRepository.ReserveItemAsync`.
  Fallback legacy repos non-SQL.
- [x] `TradeService.ExecuteTradeAsync` : or réglé AVANT livraison des
  items via `TradeRepository.SettleGoldAsync` (tx : 2 UPDATE
  player_characters en delta, ordre charId croissant, guard solde),
  puis items (InventoryService — seam indisponible, statements
  unitaires), puis `DeleteTradeAsync`. Le règlement or n'exige plus
  les delegates (fail-closed sans eux sur repo non-SQL).
- [x] `TradeRepository.SettleStallPurchaseAsync` : UPDATE
  inventory1/2 (transfert serial) + 2 deltas or dans UNE tx —
  consommé par `ZoneEntryFlow.Vending` achat d'étal ; 23505 → rejet.
- [x] `ShopService` Buy/Sell/BuyBack : or en delta via helper
  `ApplyGoldDeltaAsync` (delegate `addGold` explicite —
  `characterRepository.AddGoldAsync` branché dans Program.cs —
  sinon `AddGoldAsync` quand `setGold.Target` est le repo, sinon
  fallback absolu test).
- [x] `MailRepository.MoveItemToMailAsync` : INSERT mailitem +
  DELETE inventory en tx — les DEUX buffers ping-pong inventory1/2
  tentés (fix : l'item pouvait vivre dans inventory2 → moved=0).
- [x] `MailRepository.SettleAuctionPurchaseAsync` : débit acheteur
  guardé (gold+@d >= 0) + crédit vendeur clampé + INSERT player_mail
  en tx — consommé par `ShopHandlers` enchères achat.
- [x] `MissionDb.MailTakeItemAsync` : INSERT inventory1 (SELECT
  mailitem) + DELETE mailitem + UPDATE player_mail + relecture en tx
  (connexion explicitement ouverte avant BeginTransactionAsync).
- [x] `SocialRepository` (section coffre famille) :
  `MoveItemToFamilyStorageAsync` (DELETE inventory1/2 + INSERT
  family_storage) et `MoveFamilyStorageItemToInventoryAsync`
  (DELETE family_storage + INSERT inventory1) en tx — consommés par
  `InventoryHandlers` coffre famille dépôt/retrait.
- [x] `BankRepository.ExecuteDepositAsync` : merges storage +
  inserts storage1 (slots résolus dans la tx) + débit inventaire en
  tx — consommé par `BankService.DepositFromInventoryAsync`
  (plan→exécute, fallback legacy non-SQL).
- [x] `BankRepository.ExecuteWithdrawAsync` : merges inventory +
  inserts inventory1 + débit storage en tx. Serial de la ligne source
  réutilisé pour le 1er chunk seulement (miroir du déplacement natif)
  — les chunks suivants prennent `ItemSerialAllocator.NextAsync`
  (fix : réinsérer le même id violait l'unicité). `identify=1`
  comme l'AddItemAsync historique.
- [x] `BankRepository.ExecuteSwapAsync` + `BankService.
  SwitchBankItemsAsync` : move/fusion/swap persistés en UNE tx
  (id croissant), 23505 → false. Avant : 2-3 UPDATE/DELETE éclatés
  → crash = count dupliqué à la fusion.
- [x] `ShopHandlers` enchères : achat via `SettleAuctionPurchaseAsync` ;
  frais de dépôt (sell) passé en `AddGoldAsync` delta.

## Caller hors liste modifié

- `Eden.Zone/Program.cs` : delegates or branchés sur `ShopService`
  (`addGold` explicite) et `TradeService` (get/set — auparavant
  jamais branchés : l'or des trades ne bougeait pas en prod ; la tx
  `SettleGoldAsync` n'en a pas besoin mais ModifyGoldAsync en a pour
  le check d'or). Justifié : wiring de delegates, aucune logique.

## Suivi restant (hors périmètre fichiers)

- Enchères : mise en vente (ListAsync→AddGold→RemoveItem) encore
  non-transactionnelle — l'annonce vit dans le repo auctions
  (hors liste) ; le frais est désormais en delta. Résidu : un crash
  entre ListAsync et RemoveItem laisse une annonce sans item retiré
  (l'achat reste atomique, l'item vendu existe encore en inventaire
  → le mail livré duplique). À reprendre via une seam sur
  AuctionRepository.
- Mail send (`SocialHandlers:105-125`) : `InsertMail` +
  `MoveItemToMail` + `SetGold` absolu éclatés — mail sans pièce
  jointe si crash entre INSERT mail et move item ; or débité en
  absolu (race get→set). Fichier hors liste.
- Trade exécution : livraison des items via InventoryService hors tx
  (seam indisponible — fichiers gelés) ; l'or est réglé avant, la
  fenêtre résiduelle restante = items partiellement livrés au crash,
  or déjà réglé (pas de dupe d'or, items réservés déjà débités).
- Bank deposit/withdraw fallback non-SQL : séquence legacy conservée
  (repos en mémoire des tests).
- Quest rewards / strengthen / cube / combine / lifelong / payevent /
  expand-storage : `GetGold→SetGold` absolus dans des fichiers hors
  liste (`InventoryHandlers:415` StorageExpand, handlers quêtes…).
