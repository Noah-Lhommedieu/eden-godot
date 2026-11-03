# Phase 6 — Complétude NCID client Godot (vagues HUD → systèmes → protocole)

> Objectif utilisateur : développer **tous** les NCIDs du client Reforged pas
> encore codés, en commençant par les HUD/interfaces (déplaçables, utilisables,
> fidèles aux `ref-shots/`), puis maps, optimisation client, items/monstres et
> systèmes (succès, statuts, guildes, familiers/elfs…).

## Oracles et sources de preuve

- `ref-shots/*.png` — captures du client Reforged = cible visuelle des fenêtres.
- `portage/re/ghidra/rf_*` — code décompilé Reforged (`rf_game` = game_.exe,
  `rf_neobank`/`rf_itemmall`/`rf_gamefix` = mods). Formats NCID = vérité si le
  serveur C# émet le même layout ; sinon priorité au codec serveur prouvé.
- `eden_server/server` — émetteurs NCID réels (grep `NCID=`/`SendPacket`) ;
  un handler client n'a de sens que si le serveur émet/accepte le message.
- `docs/generated/FEATURE-MATRIX.md` — statut de preuve par NCID serveur.
- Handlers zone client existants : ~45 bindings `OnZone(...)` répartis dans
  `scripts/hud/`, `scripts/combat/`, `scripts/session/`.

## Règles par vague

1. Chaque lot = plan coché + build 0/0 + `dotnet test tools/NetTests` vert +
   preuve runtime (`--hud-open=<clé>`/`--hud-dump`/screenshot/--live).
2. Fenêtres : déplaçables via le mécanisme existant (`DragHelper`), style
   proche Reforged (cf. ref-shots), mutations de scène sur thread principal.
3. Ne pas émettre de paquet spéculatif : envoi C→S seulement si le serveur a
   un handler prouvé ou le désasm Reforged montre le format.
4. Commits par sujet ; jamais `godot-shots/` ni assets oracles modifiés.

## Vague 1 — HUD sociaux & systèmes (agents parallèles, fichiers disjoints)

- [x] **A — Guilde & social** : `GuildWindow` (infos, membres, rangs, annonces),
      amis/blacklist/groupe étendu. Ref `rf_game` (`CGuild*`, NCIDs social) +
      `ZoneSocialMessages.cs` existant. Shots ref-shots guilde si présents.
      **Fait** : fiche guilde réelle `TestGuildHL` Lv10 + 3 membres via monde 50/46 ;
      4 onglets, touche G ; ~15 codecs famille monde+zone ; 361 NetTests.
      Résidu : serveur émet `inviterId=0` sur NCID 48 (bug serveur documenté).
- [x] **B — Familiers / elfs** : `ElfWindow`/`PetWindow` (care, hunger, summon,
      skills) ; NCIDs elf côté serveur (`Eden.Zone/ZoneEntryFlow.Elf.cs`).
      **Fait** : `ElfWindow` layout natif `PetWndV3` + `PetFunctionBar` ; 17 NCIDs
      S→C + 10 C→S ; `C_Partner.ini` 588 familiers ; 4 elfs réels de test3 listés.
- [x] **C — Mail & enchères** : `MailWindow` (lecture, pièces jointes or/item),
      `AuctionWindow` (liste, enchérir, vendre) ; NCIDs `Mail`/`Auction` serveur.
      **Fait** : toutes ops prouvées live (item→sac, +2500 or, retour, delete,
      send ; achat/vente/annulation enchères réels). Bug serveur corrigé :
      `player_mail.item_id` smallint→int (mig 014).
- [x] **D — Succès & titres** : `AchievementWindow`, titres ; NCID achievement/
      `eden-reward` côté serveur ; Reforged `rf_gamefix`/`rf_game` succès.
      **Fait** : `AchievementWindow` (480), sélecteur titres (397/398+93),
      `DailyRewardWindow` (258→615 round-trip live). Tables : 3952 succès /
      359 titres / 32 daily. Fix `HudRoot` timers GC.

## Vague 2 — HUD économie & divers

- [x] Trade (fenêtre d'échange 2 joueurs), coffre famille, item mall (`rf_itemmall`),
      neobank (`rf_neobank`), daily reward (shot `daily reward.png`).
      **Prouvé live** : trade J↔J complet (340 des deux côtés, item
      transféré en DB, aucune dupe/perte — bugs serveur corrigés :
      items de trade jamais persistés → migration 015 `trade_items`,
      trades zombies `ORDER BY id DESC`, 340 jamais émis) ; vending
      vendeur/acheteur (étal ouvert, achat −1 or +1 item, règlement
      DB) ; banque + coffre famille (357/358, dépôt/retrait) ;
      **item mall : achat live** (catalogue 472 entrées, buy → 63 OK,
      pvalues 10375→10374 en DB, item en sac) — driver `--mall-test`.
      Binds monde mall déplacés en précoces (`HudExtensions`) +
      `ItemMallState` thread-safe (`Apply*Data`/`RaiseChanged`).
- [x] Buffs/status bar étendue, résistances, destiny/fortune windows.
      Destiny road live (557 désormais capté en bind précoce —
      `DestinyState` hors fenêtre, `ApplyData`/`RaiseChanged`) ;
      buff bar montée (`BuffWnd.layout`).

## Vague 3 — Carte du monde & contenu

- [x] Vérifier que TOUTES les maps chargent — audit headless en boucle
      (`scenes/game.tscn --map=Sxxx --solo`, timeout 60 s, critère
      `[GameScene] map=` + compte GLB/objets) : **139/139 chargées**,
      zéro échec. Fixes en route : spam « end quaternion must be
      normalized » (~11 k erreurs/chargement) résolu par sanitisation
      des clés de track + rest poses + lib retargetée
      (`EntityAnims`, 42 clés NaN `(-0,-0,-0,-0)` corrigées) —
      `--quat-audit` : `animKeys=0 boneRests=0 bonePoses=0 nodes=0`.
- [x] Items/monstres : audit des modèles+animations résolus.
      Monstres : 13 033 lignes c_monster / 722 stems — 22 sans GLB
      mais **non spawnés** ; scènes : 433 ini, 206 stems spawnés,
      seuls `M0M01` (typo scène S039 → fallback c_monster par
      MonsterId dans `ResolveMonster`, résolu M363) et `M912`
      (template statue/marqueur 54910, aucun asset nulle part, bénin).
      Items : 21 485 lignes — 51 codes sans icône, dont 45 récupérés
      depuis `UI/skillicon/` Reforged (pool d'icônes partagé, DDS→PNG
      convertis dans `itemicon/`) ; 6 introuvables partout (bénin,
      défaut natif). Armes : 0 modèle manquant.

## Vague 4 — Systèmes NCID restants

- [ ] PVP/arena/battlefield, mariage/couple, warehouse étendu, classe éveillée,
      limitless, quêtes avancées, transports, instance/donjons…
      (couple/mariage monté : `CoupleWindow` + binds ; limitless HUD
      monté.)
- [x] Balayage `FEATURE-MATRIX` : NCIDs serveur→client non traités du
      flux d'entrée live désormais captés — parseurs ajoutés dans
      `ZoneEntryDataMessages.cs`/`WorldEntryMessages.cs` (shortcuts,
      playroom, playercard, starway, collections, bf data, class
      custom, advanced ability, closets, licenses, life skills,
      recommandations, sysvars, cooldowns, team recruit, guide,
      revive, migration done, node times, command fail, fast dress,
      enchant 299/301, destiny 534/535/557/589/590, monde 40/46/48/
      54/60/78, item mall 56/57/58/62/63/64/79, 302 ClientFlagModify).
      **Entrée live : zéro « non géré ».** Règle retenue : binds
      précoces dans `HudExtensions` + mutation `*Data` thread-safe /
      `RaiseChanged` posté — corrige la race qui perdait les paquets
      du burst d'entrée (557 destiny, 377/378 vending, 62 mall…).

## Vague 4 bis — Complétude protocolaire exhaustive

- [x] Balayage des émissions serveur **réelles** (`new CNC_C*`, hors
      `.g.cs` déclaratifs) : 213 NCIDs S→C émis par le serveur C#.
      Avant vague : 19 non bindés côté client.
- [x] Parseurs générés depuis les codecs serveur (layout prouvé natif)
      : `ZoneMiscMessages.cs` (~170 classes séquentielles),
      `WorldMiscMessages.cs` (14 classes), `ZoneComplexMessages.cs`
      (boucles écrites à la main : 352 hotbar, 460/571 look-equip,
      572 cube, 297 blast-location, 491 player-card bitvecs, 518/582
      team-search, 537 rooms, 562 killed-rank, 72 apex-buffer).
- [x] `MiscPackets` (`ZoneMiscBinds.cs`) : bind générique parse +
      `Latest`/`Seen` + événement `Received` posté sur le thread
      principal — plus aucun drop silencieux ; chaque paquet fautif
      est isolé par `EdenSession.DispatchReceived`.
- [x] Routeur d'état `GameScene.OnMiscPacket` : whispers/textes monde
      → chat ; 270 ResetCoord → téléportation ; 606 HP cible →
      vitals/barres ; 441 TurnChar → orientation ; 341/273 → revive ;
      620/614 → re-notify sac ; 554/555/576/580 → `PlayerCoins`.
- [x] NCIDs résiduels comblés : 278/289/473 (binds précoces ajoutés),
      284/560/570 (nouvelles classes), le reste déjà routé dans
      `PlayerProfileState`/`DestinyState`.
- [x] Fenêtres des NCIDs nouvellement couverts : `DuelDialog`
      (451-454 + C→S 135/136 — interactif, déplaçable),
      `MessageBoardWindow` (455-459 + C→S 138-142 — liste, lire,
      écrire, supprimer), `FeatureDialogWindow` (magicbox 612/613,
      instance 564/553, loterie 448/511, battlefield 462-465, hôtel,
      église, boutique mission, pay-event, renfort).
- [x] Senders C→S : 161 NCIDs entrants traités par le serveur ;
      70 sans sender → 68 classes générées depuis les codecs CNE_CZ
      (`ZoneClientToServerMessages.cs`) + 10 écrites main
      (`ZoneDialogMessages.cs`). Câblage UI seulement là où la
      sémantique est prouvée — aucun payload spéculatif.
- [ ] NCIDs parsés mais non liés à un HUD dédié : loterie complète,
      magicbox combine, battlefield/colosseum, hôtel/chambres,
      territoire/family battle, mariage/église, fast dress, closets,
      collections, lifeskill… → `MiscPackets.Latest` les garde ;
      fenêtres dédiées = travail HUD ultérieur (senders prêts).

## Vague 5 — Optimisation client continue

- [ ] `EdenGltfMipmaps` fait ; reste DDS BC7 pipeline (VRAM ÷4), batching
      entités distantes, LOD, occlusion culling simple — chacun mesuré A/B.

## Critère de fin de phase

- Tout NCID émis par le serveur est soit traité, soit logué `[ncid] non géré`
  explicitement une fois par session (pas de drop silencieux).
- Toutes les fenêtres majeures du HUD Reforged présentes et utilisables.
- Maps serveur : 100 % chargées ou liste d'échecs documentée avec cause.
