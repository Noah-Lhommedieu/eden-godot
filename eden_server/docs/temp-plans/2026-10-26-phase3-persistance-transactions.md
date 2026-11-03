# Phase 3 — Persistance & transactions économiques

Plan-racine : `eden_godot/docs/plan-refonte_architecture-complet.md` (Phase 3).
Audit d'entrée : chemins d'écriture cartographiés (rapport subagent 2026-10-26) —
kill = 0 requête synchrone (write-behind), mais **aucun chemin économique
transactionnel** et or en double modèle (absolu vs delta).

## État fait (commits `241da7f`, séries ultérieures)

- [x] Kill path déjà en write-behind — confirmé mesuré (Phase 0).
- [x] Trade P2P : réserve item en tx (stackable gardé `durability-@q`,
      non-stackable DELETE ligne) ; exécution : or réglé d'abord
      (`SettleGoldAsync` — 2 deltas gardés, charId croissant), livraison items.
      **Bug trouvé : delegates or jamais branchés → or de trade mort en prod.**
- [x] Vending/étals : `SettleStallPurchaseAsync` (move inventaire gardé +
      débit acheteur + crédit vendeur clampé) — une tx.
- [x] Shops PNJ (buy/sell/buyback) : or en delta (`AddGoldAsync`/delegate
      `addGold` branché dans `Program.cs`).
- [x] Enchères achat : `SettleAuctionPurchaseAsync` (débit+crédit+mail) tx ;
      frais de vente en delta.
- [x] Mail envoi/retrait item : une tx (INSERT SELECT + DELETE gardé) ;
      **bug trouvé : inventory2 jamais lu** ; mission `MailTakeItemAsync` tx.
- [x] Coffre famille dépôt/retrait : une tx chacun.
- [x] Banque dépôt/retrait/swap : tx par opération ; **bug trouvé : serial
      réutilisé par chunk → violation unicité**.
- [x] `ItemSerialAllocator`/`elf`/`player_mail.id` → séquences PG
      (`item_serial`, `elf_serial`, `mail_serial`, migration 012 appliquée,
      idempotente) — collision inter-process zone↔mission éliminée.
- [x] Or atomique en deltas sur les chemins économiques convertis.

## État fait — lot « fermeture des seams transactionnels » (2026-10-26)

- [x] Enchères SELL atomique : `AuctionRepository.CreateListingAsync`
      (DELETE slot inventory1/2 + débit frais gardé + INSERT annonce,
      une tx). Service : `AuctionService.ListPersistedAsync` branche
      SQL/fallback ; `ShopHandlers` AuctionSell l'utilise
      (`ShopHandlers.cs:311`).
- [x] Enchères BUY complète : `MailRepository.SettleAuctionPurchaseAsync`
      étendu — débit acheteur gardé, crédit vendeur delta clampé,
      `active_auctions.status='sold'` gardé par `status='active'`,
      mail + ligne `mailitem` — UNE tx (`MailRepository.cs:396`).
      `AuctionService.BuyAsync` ne touche plus la DB sur repo SQL
      (marquage mémoire = réservation).
- [x] Enchères retour (annulation/expiration) : `SettleAuctionReturnAsync`
      — bascule status + mail + mailitem tx (`MailRepository.cs:463`) ;
      les annonces expirées sont soldées via le même seam
      (`ShopHandlers.cs` cancel + sweep dans AuctionSellQuery).
- [x] Mail envoi joueur : `MailRepository.SendMailAsync` — débit gardé
      (or joint + taxe) + INSERT player_mail + move inventaire→mailitem,
      une tx (`MailRepository.cs:305`, appelé par `SocialHandlers.cs:111`).
- [x] Mail or claim : `MissionDb.MailTakeGoldAsync` — SELECT FOR UPDATE,
      gold=0+returned=1, crédit delta clampé — une tx ; `MissionServer`
      ne re-crédite plus (`MissionDb.cs:501`, `MissionServer.cs:~615`).
- [x] Mail item claim : `MissionDb.MailTakeItemAsync` — INSERT
      inventory1 (SELECT mailitem, serial `id` PRÉSERVÉ) + DELETE +
      UPDATE player_mail, une tx (`MissionDb.cs:559`).
- [x] Trade exécution : `TradeRepository.SettleTradeAsync` — deltas or
      charId croissant + livraison croisée (fusion stacks + lignes
      fraîches slots libres, serial `item_serial` dans la tx) + DELETE
      session, une tx (`TradeRepository.cs:355`) ;
      `TradeService.ExecuteTradeAsync` branche SQL/fallback
      (`TradeService.cs:210`) ; réservation item+UPDATE session déjà
      tx via `ReserveItemAsync` (`TradeRepository.cs:163`).
- [x] Or absolu → deltas : tous les chemins métier zone/mission passent
      désormais par `AddGoldAsync` (deltas SQL gardés, cap 999999999) —
      strengthen, cube, combine, lifeskill, work, payevent, destiny,
      recommend, room, evaluate, elf, items GainGold, mission.commands
      gain_gold, combat rewards, battlefield, family create, services
      transport, donation famille, storage expand, repair, identify,
      spell level-up, transports 393, vending fallback. `st.Gold` live
      resynchronisé depuis le `RETURNING` du delta (ou `GetGoldAsync`
      pour le relais mission `MailGetGold` — `Mission.cs:472`).
- [x] `SaveFullStateAsync` : gold paramétré `-1` à la déco/autosave —
      plus d'écrasement du solde frais par un miroir périmé
      (`Lifecycle.cs:100`).
- [x] Lifelong : `LifelongRepository.AddGoldAsync` (delta cap 9999999,
      `RETURNING`) — utilisé par GainLifelongGold, GM gain_lifelong,
      paliers lifelong (`Items.cs`, `Admin.cs`, `Lifelong.cs`) ;
      `MissionDb.AddLifelongGoldAsync` était déjà en delta.
- [x] Serials : `ItemSerialAllocator.NextAsync` =
      `SELECT nextval('item_serial')` cross-process (connexion+tx
      acceptées, appelé dans les tx) — vérifié ; `MailIdAllocator` =
      `nextval('mail_serial')` ; le déplacement d'items existants
      (inventaire→mailitem, mailitem→inventaire, vente d'étal)
      PRÉSERVE le serial `id` — seules les lignes neuves allouent.
- [x] `mailitem.unbind` ajouté aux colonnes de copie
      (MailItemColumns + SELECT) — la pièce jointe copie les 53
      colonnes natives.

## Résiduel (suivi)

- [ ] `PersistenceQueue.EnqueueAsync` retournant `Task<T>` pour les chemins
      critiques qui doivent attendre le résultat.
- [ ] `set_money` GM (Admin.cs:115) : absolu conservé VOLONTAIREMENT —
      commande admin qui pose une valeur exacte (pas un delta métier).
      `LifelongRepo.SetGoldAsync` gardé pour le même cas GM.
- [ ] Fallback non-SQL : les repos in-memory (tests) gardent le chemin
      historique multi-étapes — acceptable : pas de seam de connexion
      partageable sur des mocks ; le service choisit la tx sur
      `TradeRepository`/`AuctionRepository`/`MailRepository` concrets.
- [ ] `SettleTradeAsync` livre en lignes fraîches (serial neuf) plutôt
      que de déplacer les lignes réservées — la réservation débite la
      ligne source, la renaissance en nouvelle ligne perd les
      attributs d'instance (identique à l'ancien AddItemAsync — écart
      natif documenté, à revoir si le natif prouve le déplacement).
- [ ] `MailTakeItemAsync` slot choisi hors tx (lecture indicative) —
      l'index unique rejette une collision → exception → rollback
      (comportement safe, pas de dupe).
- [ ] `eden-reward` zone (`Mission.Commands.cs:437`) : `st.Gold += g`
      en mémoire seulement — la DB est écrite par la mission
      (`AddGoldExpAsync`, delta). Cohérent.
- [ ] `Data/Repositories/CharacterRepository.cs` (table `characters`)
      : repo mort sans consommateur — non converti, hors scope.
- [ ] Autosave périodique (`Combat.cs:749`) écrit coords hors ordre avec les
      dirty en vol → fenêtre régression position (non économique).
- [ ] `SaveCharacterAsync` déco = N stmts sans tx (partiel si crash entre
      SaveFullState et enchants).
- [ ] Autres max+1 : `player_characters.id`, `accounts.id`, `family.id`,
      `family_msgboard`, `family_log` — espaces mono-writer a priori.
- [ ] `elf` demix `DeleteEnchantAsync` en série → Enqueue.
- [ ] Graceful shutdown : traîne si sessions actives (SIGTERM testé avec 50
      bots connectés → drain attend les sessions ; vérifier la borne).
- [ ] Autosave périodique (`Combat.cs:749`) écrit coords hors ordre avec les
      dirty en vol → fenêtre régression position (non économique).
- [ ] `SaveCharacterAsync` déco = N stmts sans tx (partiel si crash entre
      SaveFullState et enchants).
- [ ] Autres max+1 : `player_characters.id`, `accounts.id`, `family.id`,
      `family_msgboard`, `family_log` — espaces mono-writer a priori.
- [ ] `elf` demix `DeleteEnchantAsync` en série → Enqueue.
- [ ] Graceful shutdown : traîne si sessions actives (SIGTERM testé avec 50
      bots connectés → drain attend les sessions ; vérifier la borne).

## Vérification

- [x] Build sln 0/0 ; suite 1118 verts ; 33 tests économiques ciblés verts.
- [x] **Post-lot seams : build sln 0 erreur / 0 avertissement ;
      suite 1186/1186 verts** (incluant tests enchères, mail,
      trade, stall, persistence).
- [x] Séquences appliquées 3× idempotentes, `nextval` live vérifié.
- [ ] Charge 200 bots post-transactions (trade/shop non exercés par bots —
      couvert par tests + revue ; à instrumenter si scénario ajouté).
