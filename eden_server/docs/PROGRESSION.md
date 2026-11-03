# Progression du Projet - Eden Eternal Server

> **Date de début:** 15 Octobre 2026  
> **Dernière mise à jour:** 24 Octobre 2026  
> **Objectif:** Reconstruction complète du serveur Eden Eternal en C#/.NET 8

---

## 🆕 Session 24 Oct (nuit) — pets invoquables, canaux de buffs, montures, patch client Reforged

Plan : `docs/temp-plans/2026-10-23-buffs-pets-montures-reforged.md`.
**1073/1073 tests verts, build 0 erreur/0 warning.** Zone déployée
:5201 — retest client en attente.

### Pets — « Pets unavailable in this zone! » partout → corrigé
- `S_Node.ini` : le flag pet n'est **pas** à col42 — le natif
  (`CNodeData::Deserialize`) consomme 4 ints par `strtol` manuel
  avant le bloc ushort, et la queue est à longueur variable → ancrage
  sur le code `Sxxx` + 42. `PetFlag` réel `{3:329, 2:14}` — les maps
  autorisent enfin les familiers. Commit `b441b36`.

### Buffs — les stats des statuts appliquées pour de vrai
- `DecodeEffectCommand` (FAILED → objdump) + `CEC_*::Execute` :
  mapping var→stat corrigé commande par commande — 2081=AttrBase flat,
  2082=DEF/Hit/Dodge/PAtk/MAtk, 2083/2085/2086/2087 canaux %/post,
  2095=resist éléments 4-9, 2101=resist 1-3, 2048/2056/2117 crit/block,
  2051 mouvement.
- `StatContribution` étendu (canaux DEF/RES/AtSpd/Hit/Dodge/Crit/…) ;
  formules natives branchées (`Str = (eq+base)×pct×0.01 + post`) ;
  AttrBase mode 1 = `S_SpellReference[level][col18-25] × var × 0.01`.
- **NCID 85 EnchantCancel** routé vers `RemovePlayerEnchantAsync`
  (natif `CancelPlayerEnchant` @0x805fe00 : refuse si col7&1==0 ou
  col8&0x10 — flags de `S_Enchant` chargés). Commit `a0dfbae`.
- 299 : counter `CEnchant+0x30` émis (1 au premier add — golden).
  Commit `d7662c6`.

### Montures
- **NCID 326** : le `u16@0xe` n'est pas la vitesse mais le **facing en
  half-float** (`char+0x58`), bit0 = hasDestination — `MoveSelf` natif
  @0x829a390 + goldens. Mobs : facing = atan2(dx,dz). Commit `094140a`.
- **441 TurnChar** broadcast à l'acteur ET aux observateurs
  (`BroadCastToCanSee` inclut l'acteur).
- **Montures de combat** : anims d'attaque depuis le KFM de la monture
  (`CEC_Ride::Execute` charge `r{key}` en char+0xd60 ; `GetAttackID`
  @0x80e0b90 → 1021/1022, arc kind 0x1c → 1023, gun 0x1d → 1024).
  Gate aligné sur la règle native (`RideType!=0 || buffs combat`).
  Commits `690f490`/`eca2703`.
- **Bind à l'usage** : `DoUseItem` natif appelle `BindItem` même pour
  les items non consommés → montures bindées au premier usage +
  record 369. **1914 ItemUnBind** (parchemins) : UseType 3/5,
  roll `rand%10000<var0`, var1% purge train, var2% réduction
  renforcement. Commit `2ea1de6`.

### Patch client Reforged (`game_.exe`, backup `.bak_castmove`)
- Steering RMB pendant cast : `FUN_0075a8f0` @0x75aa0c `jne` → NOP
  (le bit15 de `ctrl+0x3c` bloquait la copie du heading caméra).
- Micro-stop au cast : `call FUN_0075ad10` (reset→idle) NOPé aux
  sites 0x75c615 et 0x75c9e0. Doc : `docs/PATCH-CLIENT-BINAIRE.md`.
  Commits `451a4b9`/`ccff885`.

---

## Session 27 Oct — audit NCIDs « NON PROUVE » (mail/team/trade/appellation/divers)

Plan : `docs/temp-plans/2026-10-26-audit-ncid-non-prouve.md`.
**733/733 tests verts, build 0 erreur/0 warning.**

### Corrections protocole réelles
- **Layouts mail par lot corrigés** : `CNE_CZ_ZoneServerMailDelete`
  (98) = `[i32 mailId][u16 n][n×i32]` et `MailGetItem` (100) =
  `[i32 mailId][u16 n][n×i32][s16 freeNeeded]` (Deserialize natifs
  @0x8263ec0/@0x8264ec0) — les classes générées lisaient mal le mode
  lot ; handlers batch fidèles (MailPatchDelete/MailPatchGet →
  commandes unitaires).
- **Collision NCIDs maison** : nos codes zone→mission 145/146/147
  occupaient les NCIDs natifs MailSend/SendSystemMail/MessageWrite →
  renumérotés **450/451/452** (ZoneMissionLink + MissionServer).
- **89 BagDueDate** : `CPlayerFactory::SetBagDueDate` @0x81133f0
  transcrit — bag<4, durée<30 j, cumul max 60 j, consomme 1× item
  0x9d07/0x9c55, persiste `bank_duedate_<n+1>` (colonnes natives),
  réponse 389 `[s16 bag][i32 unix]`. + `GetBankDueDatesAsync`/
  `SetBankDueDateAsync`.
- **238 ChangePositionV2** : `ChangePositionV2` @0x840b190 — pos<3,
  no-op si inchangée ; la position ressort dans le dernier s16 de
  `SSearchTeamMemberDataV2` (preuve mission `GetTeamMemberData`
  @0x81c8b40, défaut 2). `TeamService._positions` + M6 des annonces.
- **202 CoRideInvite** : dernier CNE_CZ sans handler — dispatch op
  0-9 fidèle, texte 0x5af si cible hors-ligne, 0xe19 sans monture
  (sous-système mount absent — réponse native exacte).
- **Relais DBA natifs** : TerritoryQuery→"TerritoryDataQuery",
  ColosseumModeQuery→"ColosseumDataQuery",
  BattleCrusadeJoin→"CrusadeJoin crusade_id=%d",
  GreedyIslandJoin arg=0→"GreedyIslandInfo"
  (`CDBAgent::Command` → `SendAgentDbaCommandAsync`).

### 15 NCIDs morts en natif identifiés
Execute = corps vide ou tail-call `vtable+8` (`GetType()`, résultat
ignoré) — objdump confirmé : ElfStoneUse/Update, ElfRevive, ElfCallElf,
ElfTalk, ElfCheckData, GladiatorTeamCreate/Query, GreedyIslandRankQuery,
PayEventResponse, BuySpell, PVPFlagModify, SetSpellStone, SloganUpdate,
StorageWithdraw. **Ne pas implémenter** — commentaires de preuve dans
les handlers.

### Audit — reste
~33 CNE_CZ_ dépendent de sous-systèmes non portés : housing/rooms
(`CPlayerRoomManager` ×12), colosseum (`CColosseumFactory`), territoire
(`CTerritoryWarFactory` Join), fast-dress (`fast_dress` + map
page→slots), destiny-road, roll-window (`CTreasure::SetRoll` —
`CheckRollItem` décodé : dés 0-98 uniques, need>greed, tri desc,
`RollPickUpTreasure`), rank-info, lucky-bag, angel-style.
Disposition complète : plan temp ci-dessus.

---

## 🆕 Sessions 22–24 Oct — fiabilisation (phases 0–2) + Phase 3 codecs + Phase 4 observabilité

Plans : `docs/temp-plans/2026-10-22-fiabilisation-phases-0-1.md`,
`2026-10-22-phase3-codecs-generes.md`, `2026-10-24-phase4-observabilite.md`.
**717/717 tests verts, build 0 warning.**

### Phase 0 — base réseau
- `NetDebug` caché ; anti-flood (rejet trames `len<2`, handshake 10 s,
  idle 300 s, `CloseReason` logué) ; `PacketTrace` ring 32 paquets.
- Retarget **.NET 10** + CPM (`Directory.Packages.props`) ; le retarget
  a exposé une casse critique : C# 14 liait `RC4.Process(byte[])` à
  l'overload copie → chiffrement no-op (fix `ProcessCopy`, intercepté
  par les tests). `NpgsqlDataSource` unique ; encodages CP950 garantis.
- MCP `eden-re` enregistré (`re_lookup`/`re_xref` fonctionnels).

### Phase 1 — harnais wire
- **Golden bytes** : 254 cas `Deserialize→Serialize` sur captures
  réelles (`tests/Eden.Tests/Data/golden/`), `known_incomplete.json`
  documenté. `tools/wire_diff.py` (diff natif-vs-C#) + client headless
  RSA+RC4 validé en live. CI GitHub Actions + PostgreSQL.
- Fix wire réel : NCID 271 `ClientDisplayMonster` — le corps natif
  `[u32 mob][u8 n][s16×n][u8 gen][i32 owner]` restauré (un "fix"
  antérieur émettait le corps de 444 sous le mauvais opcode).

### Phase 2 — modèle acteur par zone
- **`Channel` borné (256)/session** : drain unique, RC4 dans le drain
  (keystream = ordre réel du fil), backlog >5 s → déconnexion propre.
- **Zéro `GetAwaiter().GetResult()`** ; 14 écritures DB fire-and-forget
  migrées vers `PersistenceQueue` (ordonnée par charId) +
  `CharacterWriteBehind` (flush 15 s + déco).
- **`ZoneLoop`** : file de commandes + `PeriodicTimer` 100 ms — respawn,
  trésors, chase des mobs, casts différés, wander, regen, status, works
  tickés sur le fil unique ; handlers routés via `PostAsync` →
  **aucune mutation d'`EntryState` hors boucle** ; `IsOnLoopThread`
  fiable via `AsyncLocal`. Test de charge : un peer lent n'affecte
  pas les autres.

### Phase 3 — codecs générés (12 vagues)
- ~120 NCIDs audités contre `::Serialize` natif (reindex/désasm) et
  captures ; émetteurs inline migrés vers classes `NetCommand` typées.
- Classes corrigées : 271, 284, 288, 297, 298 (masque de champs
  optionnels), 301, 302, 326, 327, 328, 348, 352, 368, 488, 543, 572,
  600, 283, 285… — chaque correction validée round-trip golden.
- Reste volontaire en codecs dédiés : 274/282/283/312/358
  (`NativeItemCodec`), 369/612/613 (`MagicBoxPackets`), 378
  (`VendorPackets`), 412/413 (`NativeElfCodec`), 599
  (`WeddingPackets`), relais bruts 391/404/316/402–409/24/57/62.

### Phase 4 — observabilité
- `EdenLog` (471 sites `Console.WriteLine` migrés), `EdenMetrics`
  (`System.Diagnostics.Metrics`, meter `Eden.Server` — paquets in/out,
  file d'envoi, sessions, tick zone, DB, file persistence),
  rate-limit entrant par session (`EDEN_MAX_PACKETS_PER_SEC`) +
  tentatives login par IP, `EdenConfig` centralisant toutes les
  variables d'environnement.

## 25 Oct — exp de groupe native + loot d'équipe

Plan : `docs/temp-plans/2026-10-25-exp-groupe-loot-natif.md`.
**719/719 tests verts.**

- **Suppression** de `TeamService.ShareExperience` — formule
  `base×(1+0.1(n-1))/n` inventée, sans source native.
- **Formules natives** `NativeCombatFormulas` : `ExpForTeam`
  (@0x083f4470, `ROUND(base×0.01×lv×(n×330+100)/Σlv)` capé à base),
  `ExpDeclineRate` (@0x083f5150, malus ±écarts via params 328/329),
  `ExpForTeamNoShare` (@0x083f5340, coef 331 + déclin).
- **`GiveTeamExp` transcrit** : `ComputeTeamExpShares` — membres
  locaux (même node, migrés, vivants), exclusion des membres à
  `maxLv−lv > 9` (retirés de n ET de Σlv), part partagée ou NoShare
  pour le tueur exclus ; `GrantMobExpToAsync` rejoue la séquence
  native par membre (texte 248 → gear exp share×param 496 → GainExp
  → elf part entière). Solo inchangé.
- **Loot d'équipe réel** : `PickLootReceiver(team, killer,
  isEligible)` — FFA/Taunt→tueur, LeaderOnly→leader, Random→tirage
  parmi éligibles, **RoundRobin→`TeamEntity.LootCursor`** persistant
  qui saute les inéligibles et n'avance que sur attribution réelle.
  `DistributeLootAsync` crédite désormais le receveur désigné
  (session + add item + ground-treasure + ItemCollect) à chaque
  roll, éligibilité = session migrée sur le même node.
- **Flags `CForceTeam` natifs** : `TeamType` (+0x64, dérivé capacité
  5→1/10→2/25→3) — raid25 = exp et or solo ; `ExpNoShare` (+0x56,
  `SetExpRule` wire 1-2→partagé >2→exclusif) ; gates GiveTeamExp
  (capacité 6-10 → maps type 7 ; type 4 → maps 0xc). `GiveGold` :
  or/n entre locaux + fix off-by-one `rand(spread*2)` exclu.
- **LFG (Phase 5)** : `TeamRecruitV2`/`SearchTeamV2` implémentés —
  store d'annonces zone-local, `ClientSearchTeamInfoV2` réécrit au
  format variable natif (vecteur d'annonces × membres), broadcast
  583 avec les vrais champs.
- **Dette qualité** : `ZoneEntryFlow.Combat.cs` 3,7k→1,8k lignes via
  `Combat.Rewards.cs` + `Combat.Spells.cs` ; code mort création perso
  hardcodé supprimé (~1 100 lignes) ; docs agents à jour
  (`server/AGENTS.md` § état post-phases + règle `*.g.cs`).

---

## 🆕 Session 18 Oct (soir) — batch gameplay : inventaire, shop, class skills, exp, regen, HP

Plan : `docs/temp-plans/2025-10-18-inventaire-shop-skills.md`.
Combat confirmé **100 % fonctionnel** côté client par l'utilisateur.

| Symptôme live | Cause | Fix |
|---|---|---|
| Inventaire visuellement vide | réponse au NCID 25 = seul 312 ; le natif émet aussi **280 attr `0x3f0=1`** (« inventaire prêt ») | `EntryHandlers` : 312 (DB réelle) + 280/0x3f0 ; le 490 reste au replay 133 (capture : un seul) |
| Achat NPC muet (ni or ni item) | `SendEnterShopAsync` ne remplissait que `st.OpenShopId`, pas `session.CurrentStoreId` → `GetStore(0)` | synchro `CurrentStoreId` à l'ouverture + reset LeaveShop ; achat émet **274** (item bag/slot) + **308** + **280** attr `0x02` gold |
| Classes 2-5 sans class skill lv1 | aucun octroi au changement — `AddClassAsync` insérait la classe sans sorts | `ClassSkillTable.GrantableSpells` : vecteur S_Class ∪ mask mono-classe (S_Spell **col 23** bit(classId-1)), filtré `RequiredLevel` (**col 22**), dédup par code (chaînes J = paliers certificat) — octroyé à l'entrée (classe active) + changement 168 |
| Mobs = 0 exp | `ExpReward` lisait col 14 (grade) ; jamais appelé dans `FinishMobDeathAsync` | exp = **col 18** ; `GrantKillExpAsync` → `ProgressionService` + **600** `ClientUpdateAttrNewExp` `[i32][i64]` / **284** `ClientLevelUp` `[u32][u8][s16][i64][i32]` |
| Aucune regen | boucle HP-only 2 %/4 s | tick 5 s, HP+MP 5 % ; assis ×4, combat = MP seul ÷4 (modèle `CCharacter` scheduler natif) |
| 120 HP au lv1 | `max_hp` brut persisté (1123/123 = stats d'un autre perso capturé) | `S_Level.MaxHp(251) + CON_eff×(100/20)` ≈ 311 warrior ; `S_Level.MaxMp(195) + WIS_eff×(100/15)` ≈ 261 — entrée + level-up + création |

Découvertes natives intégrées :
- **S_Class non tronqué** : les lignes 3-5 contiennent des LF nus dans
  les descriptions Big5 — le join multi-lignes d'`IniTable` restaure
  les 305 lignes complètes (vecteurs sorts Hunter/Cleric/Thief présents).
- **`CCharFormula::CastTime`** = `SSpellData+0x38` = S_Spell **col 26**
  (décisecondes, plancher 0.4 s) — les sorts test1 ont col 26 vide →
  cast instantané natif ; la latence 1-2 s venait de col 35 lu par erreur.
- **Kill mêlée natif** : 272 type=1 kfId → +300 ms → 272 type=5 −1 →
  305 — l'anim du dernier coup joue avant la mort (`CEventDelayDamage`).

---

## 🆕 Session 18 Oct — décompilation massive + correctifs live

**Exports Ghidra :** les 16 binaires décompilés dans `re/ghidra/`
(voir section dédiée du `MASTER-IMPLEMENTATION-PLAN.md`) — pseudo-C
exploitable directement (`zone/`, `mission/`, `world/`… + client
vanilla + client Reforged/`d3d9.dll`).

**Correctifs protocolaires appliqués (zone C#) :**

| Symptôme live | Cause | Fix |
|---|---|---|
| Gold affiché = 0 | le replay d'entrée porte les attrs du perso capturé | overlay 280 post-MigrationDone : attr `2`=gold, `0x2c`=classe, `0x11/0x12` HP/MP, `0x06` exp + 283 ModelModify + 489 liste (`SendEntryStateOverlayAsync`) |
| Classe affichée fausse (warrior au lieu de thief), "Transform!" manquant | idem — attr `0x2c` du replay | même overlay |
| `NULL????` en 2e fenêtre dialogue | entrée bidon `(0,"")` → `T_Dialogue[0]` inexistant côté client | suppression placeholder + gossip local réutilise le ctx du 56 (`MissionCtx`) + ctx≤0 → pas de 285 (natif @0x831fa50) |
| NPC enchères ouvre la banque | mapping `#6#`/`#18#` inversé | `#6##`=bank→357, `#18##`=auction→430 (table `TC_Service` désasm — vérifié sur noms Big5 des NPC S001) |
| NPC échange (52353/52363, type 58) : clic sans effet | verbe `pay_system` non routé | type 58 → **602 `ClientOpenPaySystem` `[i32 npc][u16 shop]`** (Serialize @0x81e6810) |
| `eden-reward` or non crédité à l'écran | attr `0x30` émise | attr `0x02` (preuve `GainGold`→`ClientUpdateAttr` attr 2) |
| Anims de course fantômes post-combat | `BroadcastMoveAsync`/`SendMobMoveAsync` écrivaient `facing×16384` dans le champ speed du 326 — bit0 (hasDst) aléatoire → désync du stream | format unifié `BuildMovePrediction` 11/15B (Serialize @0x81xxxxx) |
| HP replay écrasant la DB | 280 attr `0x11` du replay relu dans `st.PlayerHp` | fallback seulement si DB n'a rien donné |

**Découvertes natives intégrées :**
- `NodifyFlagChange` (302) = `[u32 uid][u32 flags][u8 state]` — émis
  uniquement quand un FSM d'état est retiré → pas de 302 en mêlée
  pure, le swing vient du 272 (confirme la capture).
- `CNE_ZMis_ZoneServerListDialogue::Execute` : ctx∈{-1,0} →
  `ResetOperation`, aucune fenêtre.
- `ConvertNPCGreeting`/`InsertNPCInitServiceDB` : FuncId direct =
  ouverture de service au talk sans menu.

---

## ✅ Systèmes Complétés

### 1. Système de Classes et Changement de Classe
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/01-class-system.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `ClassEntity.cs` - Entité de classe
- [x] `ClassProgressEntity.cs` - Progression de classe
- [x] `ClassStatsEntity.cs` - Stats par niveau
- [x] `ClassDataParser.cs` - Parser T_Class.ini
- [x] `IClassRepository.cs` et `ClassRepository.cs` - Repository PostgreSQL
- [x] `ClassChangeService.cs` - Service de changement de classe
- [x] `ClassSystemTests.cs` - 4 tests unitaires (✅ tous passants)
- [x] Table `player_classes` dans `Database/001-initial-schema.sql`

**Fonctionnalités:**
- Chargement des 20 classes de base depuis T_Class.ini
- Identification des classes éveillées (IDs 101-120, 201-220)
- Parsing des prérequis (niveau, classe parente, quêtes)
- Validation du changement de classe
- Unlock de nouvelles classes
- Calcul de XP de classe

---

### 2. Création et Suppression de Personnage
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/02-character-creation.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `CreateCharacterRequest.cs` - DTO de création
- [x] `CreateCharacterResult.cs` - DTO de résultat
- [x] `CharacterCreationService.cs` - Service de création
- [x] `CharacterCreationTests.cs` - 3 tests unitaires (✅ tous passants)

**Fonctionnalités:**
- Validation de nom (3-16 caractères)
- Vérification de disponibilité de nom
- Validation des prérequis de classe
- Validation de race (1-5) et genre (1-2)
- Création avec stats initiales
- Soft delete de personnage
- Limitation à 8 personnages par compte

---

### 3. Stats et Profil Joueur
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/03-player-stats.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `PlayerStatsEntity.cs` - Entité de stats
- [x] `StatsCalculationService.cs` - Service de calcul
- [x] Table `character_stats` dans `Database/001-initial-schema.sql`

**Fonctionnalités:**
- 6 attributs: STR, DEX, CON, INT, PSY, AGI
- Stats de base + stats bonus
- Formules de calcul de HP/MP max
- Formules d'attaque/défense physiques et magiques
- Formules de taux critique et esquive

---

### 4. Inventaire et Sacs
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/04-inventory-system.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `ItemEntity.cs` - Entité d'item
- [x] `IInventoryRepository.cs` et `InventoryRepository.cs` - Repository PostgreSQL
- [x] `InventoryService.cs` - Service de gestion
- [x] `InventoryTests.cs` - 2 tests unitaires
- [x] Tables `player_items` et `inventory_slots` dans `Database/001-initial-schema.sql`

**Fonctionnalités:**
- Gestion des sacs et slots (4 sacs par personnage)
- Système de slot rental (expiry timestamps)
- Durabilité des items
- Ajout, suppression, déplacement, split d'items

---

### 5. Banque et Stockage
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/05-bank-storage.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `BankItemEntity.cs` - Entité d'item en banque
- [x] `IBankRepository.cs` et `BankRepository.cs` - Repository PostgreSQL
- [x] `BankService.cs` - Service de gestion
- [x] `BankTests.cs` - 2 tests unitaires
- [x] Tables `player_bank` et `family_bank` dans `Database/001-initial-schema.sql`

**Fonctionnalités:**
- Stockage séparé de l'inventaire
- Banque personnelle (50 slots)
- Banque de famille/guilde
- Dépôt, retrait, déplacement d'items

---

### 6. Système d'Échange P2P (Trade)
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/06-trade-system.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `TradeEntity.cs` - Session de trade
- [x] `ITradeRepository.cs` et `TradeRepository.cs` - Repository PostgreSQL
- [x] `TradeService.cs` - Service de gestion
- [x] `TradeTests.cs` - 2 tests unitaires
- [x] Table `active_trades` dans `Database/001-initial-schema.sql`

**Fonctionnalités:**
- Demande, acceptation, confirmation d'échange
- Validation de distance (< 10m)
- Échange d'items et de gold
- Annulation de trade
- Double confirmation anti-scam

---

### 7. Hôtel des Ventes (Auction)
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/07-auction-system.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `AuctionEntity.cs` - Vente en cours
- [x] `IAuctionRepository.cs` et `AuctionRepository.cs` - Repository PostgreSQL
- [x] `AuctionService.cs` - Service de gestion
- [x] `AuctionTests.cs` - 1 test unitaire
- [x] Tables `active_auctions` et `auction_history` dans `Database/001-initial-schema.sql`

**Fonctionnalités:**
- Création de ventes (prix, durée, quantité)
- Achat direct par autre joueur
- Annulation de vente
- Commission de 5%
- Expiration automatique

---

### 8. Item Mall (AP/EP)
**Status:** ✅ COMPLETED  
**Documentation:** `docs/implementation/08-item-mall.md`  
**Date:** 15 Octobre 2026

**Implémentation:**
- [x] `ItemMallEntities.cs`, `ItemMallService.cs`
- [x] Repositories `IItemMallRepositories.cs` / `ItemMallRepositories.cs`
- [x] `ItemMallTests.cs`
- [x] Schéma Item Mall dans `Database/001-initial-schema.sql`
- [x] `S_ItemMall.ini` en clair — parseable directement

---

### 9. Décodage MyCrypt — ✅ PERCÉE MAJEURE
**Status:** ✅ RÉSOLU (100%)  
**Documentation:** `docs/S_CLASS_DECODING_STATUS.md`

- [x] Algorithme identifié = **ZipCrypto (PKZIP)** via objdump de ZoneServer
- [x] Password extrait: **`ghfdsjdl`** (.rodata @ `0x87cdff0`)
- [x] `tools/mycrypt.py` — décodeur autonome (decode / decode-all)
- [x] `Eden.Core/Data/MyCrypt.cs` — décodeur C# + round-trip testé
- [x] `Eden.Core/Data/Parsers/IniTable.cs` — parser générique `|V.x|N|`
       avec gestion des champs multi-lignes (CRLF embeddes)
- [x] **164 fichiers .ini décodés** → `server_us/Data/db_decoded/`

---

### 10. Parsers de données (toutes les tables clés)
**Status:** ✅ COMPLETED (parsers) / intégration en cours

| Table | Contenu | Parser | Tests |
|-------|---------|--------|-------|
| `S_Class.ini` | 305 defs (61 classes × 5 races) | `ClassStatsParser` | ✅ |
| `S_Item.ini` | 20 426 items (78 cols, clair) | `ItemParser` | ✅ |
| `S_Monster.ini` | 12 896 monstres | `MonsterParser` | ✅ |
| `S_Spell.ini` | 4 485 sorts | `SpellParser` | ✅ |
| `S_Mission.ini` | 2 831 quêtes scriptées | `MissionParser` | ✅ |
| `S_Enchant.ini` | 19 829 enchants/procs | `EnchantParser` | ✅ |
| `S_ElfCombine.ini` | 957 recettes elfe | `CraftRecipeParser` | ✅ |
| `S_FamilyCombine.ini` | 3 275 recettes guilde | `CraftRecipeParser` | ✅ |
| `S_ItemCombo.ini` | combos/sets d'items | `CraftRecipeParser` | ✅ |
| `S_ClassDropItem.ini` | loot par classe de mob | `GameDataParsers` | ✅ |
| `S_DropItem.ini` | 5 049 tables pondérées | `DropAndSpeedParsers` | ✅ |
| `S_MonsterStep.ini` | 375 vitesses monstres | `DropAndSpeedParsers` | ✅ |
| `S_Talent.ini` | 915 arbres de talents | `GameDataParsers` | ✅ |
| `S_Title.ini` | 337 titres + bonus | `GameDataParsers` | ✅ |
| `S_Achievement.ini` | 3 844 succès | `GameDataParsers` | ✅ |
| `S_Node.ini` | 379 maps/zones + POI | `GameDataParsers` | ✅ |
| `S_Elf.ini` | 226 familiers | `ElfParser` | ✅ |
| `S_Battlefield.ini` | donjons + rewards | `BattlefieldParser` | ✅ |

---

### 11. Services de jeu basés sur les vraies données
**Status:** ✅ COMPLETED (v1)

- [x] `CraftService` — craft elfe + guilde, scripts de bonus pondérés
- [x] `EnchantService` — moteur de procs (trigger events, rolls de chance)
- [x] `MonsterService` — spawn/kill/despawn, dégâts, résistances, boss
- [x] `SpellService` — application de sorts, buffs actifs + stacks,
      `ClearAllBuffs` (= `CCharacter::ClearAllTeamSpell`), ticks d'expiration
- [x] `MissionService` — machine d'état des quêtes (27 commandes du langage)
- [x] `ElfService` — familiers: Acquire/Mood/Energy/Friendship/Feed
- [x] `TeamService` — party/raid: create/join/leave/kick, XP share, loot rules
- [x] `GuildService` — guildes: rangs, XP/niveau, trésor, taxes, distribution
- [x] `FriendService` — amis: demandes, block, recherche communauté
- [x] `LoverService` — couple: propose/mariage/divorce/affection/GotoLover
- [x] `MailService` — mails + pièces jointes + COD + TTL + retour expéditeur
- [x] `DungeonService` — instances de donjon (Battlefield) + rewards par palier
- [x] `LootService` — roll tables pondérées + drops de classe
- [x] `GameDataService` — chargeur central de toutes les tables (équiv.
      `GameData::ReadINIFile`)
- [x] `MatchmakingService` — file LFG, groupes équilibrés tank/heal/dps
- [x] `MountService` — montures, bonus vitesse (S_RideStep.ini)
- [x] `TalentService` — points de talents dans les arbres de classe
      (S_Talent.ini 915 arbres)
- [x] `StatsCalculationService` — **vraies formules S_Parameter.ini**
      (677 paramètres: CON→HP=20, crit=AGI/20+5%, dmg crit=150%, hit=95%,
      def mod=0.5, unarmed=1.5s, enchant +3%/niv, durabilité, decay XP...)
- [x] `EquipmentEnchantService` — enchant +1..+20 avec les vraies chances
      (arme +11-14=50%, +15-20=25%; armure +11-14=100%, +15-20=50%)
- [x] `AchievementService` — compteurs, complétion, titre actif
- [x] `RankingService` — classements par catégorie + S_RankAward.ini
- [x] `LifeSkillService` — récolte avec temps de collecte
- [x] `GmCommandService` — dispatcher 332+ commandes GM avec niveaux d'accès
- [x] `AuctionService` — hotel des ventes: mise en vente, recherche,
      depot 5%, commission 8%, expiration, retour items
- [x] `ItemMallService` — boutiques AP/EP: wallets, promos (discount +
      dates), limites d'achat par joueur
- [x] `CombatService` — **pipeline de degats complet** (hit 95% → dodge
      AGI/10 → block → crit AGI/20+5% → dmg atk-def×0.5 ±10% → crit
      ×150% → resistances → procs OnAttack/OnCritical/OnBeAttacked/OnCast)
- [x] `TerritoryWarService` — guerres de territoire: camps rouge/bleu,
      cristaux (points 200/180/160/140/120), kills (+1), rewards par rang

### 12. Nouveaux parsers de données (vrais fichiers)
- [x] `ParameterParser` — S_Parameter.ini (677 formules)
- [x] `ProgressionParsers` — S_Level.ini (XP 1-101), S_Races.ini,
      S_RootCmds.ini/S_GMToolCmds.ini (GM), S_EquipSet.ini (404 sets),
      S_Ai.ini (707 profils IA monstres)
- [x] `WorldParsers` — S_Npc.ini, S_Dialogue.ini, S_Transport.ini,
      S_LifeSkill.ini, S_Partner.ini
- [x] `ShopAndMiscParsers` — S_Store.ini (670 shops NPC), S_ItemChest.ini,
      S_ItemCombo.ini (5 800 affixes), S_ClassRankBuff.ini, S_DailyAward.ini

### 13. Protocole réseau — vrais opcodes extraits ⭐
- [x] `tools/extract_ncids.py` — extraction des NCIDs réels depuis les
      binaires (assignation `mov %ax, <ncid_addr>` dans
      `InitPCCommandTable`, index de la table = opcode)
- [x] `docs/generated/ncid_map.json` — **1338 opcodes réels**:
      ZoneServer=871, WorldServer=204, MissionServer=205,
      LoginServer=35, GatewayServer=23
- [x] **`tools/extract_all_protocols.py` étendu aux CNE_*** —
      1248 classes de messages reversées (CNC_* serveur→client +
      CNE_* client→serveur), 1235 avec opcodes réels
- [x] Classes `.g.cs` régénérées avec vrais `Opcode => NCID`
      (ex: `MeleeAttack`=5, `MoveSelf`=1, `SpellCastToLife`=9)
- [x] Framing confirmé: `[u16 NCID][payload]` — `NetSession.SendAsync`
      écrit désormais l'opcode en premier (fidèle à `_Serialize`)
- [x] **`tools/extract_cne_fields.py`** — extraction des champs de
      `Deserialize(CInBinStream&)` (`operator>>(T&)`)
- [x] **`tools/fill_cne_fields.py`** — remplit les corps des CNE_*:
      **1209 commandes avec champs réels** (ex: `MeleeAttack` =
      int targetId + byte slot)
- [x] `MonsterAiService` — aggro/taunt branché sur les profils S_Ai.ini
      (hate: dégâts×10, heal×1.2, taunt prioritaire)

---

## 📊 Infrastructure

### Docker
- [x] Suppression de `compose.yml` et `docker-compose.stack.yml` (doublons)
- [x] Unification sur `docker-compose.yml` unique
- [x] Ajout de `Database/001-initial-schema.sql` au montage init PostgreSQL

### Base de Données
- [x] `Database/001-initial-schema.sql` - Migration unifiée
  - Table `player_classes` (progression des classes)
  - Table `class_progression` (stats par niveau)
  - Table `class_spells` (sorts par classe)
  - Table `class_talents` (talents/certificats)
  - Table `character_stats` (stats des personnages)
  - Table `player_items` (items dans inventaire)
  - Table `inventory_slots` (capacités de sacs)
  - Table `player_bank` (banque personnelle)
  - Table `family_bank` (banque de famille)
  - Table `active_trades` (sessions de trade)
  - Table `active_auctions` (ventes en cours)
  - Table `auction_history` (historique des ventes)

### Documentation
- [x] `docs/REVERSE-ENGINEERING-GUIDE.md` (383 lignes)
  - Guide d'utilisation de Ghidra
  - Méthodologie d'analyse
  - Ordre prioritaire par phases
  - Patterns de code natif

---

## 📋 Prochaines Étapes

### Court Terme (Phases 4-6 du Master Plan)
- Système 08: Boutiques AP/EP (Item Mall)
- Système 09: Craft de base
- Système 10: Craft de race
- Système 11: Enchantement d'équipement
- Système 12: Fusion et upgrade d'items

### Moyen Terme (Phases 7-9)
- Système 13: Skills et sorts
- Système 14: Proc d'items et enchants
- Système 15: Breakpoints de combat
- Système 16: Party (groupe)
- Système 17: Raid (25+ joueurs)
- Système 18: Matchmaking
- Système 19: Guildes et familles
- Système 20: Donjons solo et multi
- Système 21: Colosseum PvP
- Système 22: Battle Crusade
- Système 23: Battle Royale

---

## 🔄 Session — Réseau/Gameplay : réponses CNC + famille/mail/couple

- `player_mail` (PostgreSQL) + `MailRepository` + handlers mail complets
  (Send/Query/Read/GetGold/GetItem/Delete/Return) avec résolution
  nom→personnage et retour d'items.
- `FamilyBankRepository` (`family_bank`) + handlers `FamilyStoragePut/Get`
  (dépot/retrait inventaire ↔ banque de guilde, slots libres via
  `generate_series`).
- `BankRepository.GetAvailableSlotAsync` corrigé — première place libre
  calculée via `generate_series(0, capacity-1)` au lieu d'une lecture
  impossible sur `player_bank` vide.
- Guilde : `FamilyCreate`, `FamilyDonateMoney`, `SetFamilyEmblem` branchés
  sur `GuildService` ; couple : `LoverInvite/Message/Break/QueryLoverInfo`
  sur `LoverService`.
- `Eden.World` : handlers amis (`AddFriend`/`FriendInvitation`/`RemoveFriend`/
  `QueryFriendInfo`) + `LoginCharacter`/`RemoveCharacter` sur les vrais
  opcodes générés.
- Réponses CNC branchées : `ClientHello`, `EnterNodePlayerData` (+ 8
  squelettes EnterNode*), `UpdateAttrNewHp` (broadcast dégâts),
  `UpdateAttrNewExp` (gain XP), `MailSendResult`, `MailOpenBox`.
- `ZoneEntityRegistry.RegisterPlayer` — le joueur devient une entité vivante
  de la zone (stats depuis `S_Level.ini`), nettoyage à la déconnexion
  (entité + équipe).

## 🎯 Métriques Globales

- **Total systèmes dans le Master Plan:** 44
- **Systèmes complétés:** 17/44 (39%) — 24 en cours partiels
- **Tests unitaires passants:** 196 tests passants (100%)
- **Build status:** ✅ Réussi (0 avertissements, 0 erreurs)
- **Fichiers .ini décodés:** 164 (100% des données serveur)
- **Opcodes réseau réels:** 1338 NCIDs extraits des binaires
- **Messages reversés:** 1248 classes CNC_*/CNE_* générées
  (1235 vrais opcodes, corps remplis via Deserialize pour CNE_*
   et Serialize pour CNC_*, tableaux en boucle détectés,
   noms sémantiques via `docs/generated/field_names.json`)
- **Transport TCP:** ✅ cassé — handshake RSA-2048 → clé RC4 5 octets,
  paquets `[u16 len][RC4(u16 NCID + champs)]`, implémenté dans
  `Rc4.cs` + `NetSession` (Eden.Zone/World actifs)
- **Tables PostgreSQL créées:** 13 (+ player_mail)
- **Décodeur permanent:** `tools/mycrypt.py` + `Eden.Core/Data/MyCrypt.cs`
- **Extracteurs:** `extract_ncids.py`, `extract_all_protocols.py`,
  `extract_cne_fields.py`, `extract_cnc_fields.py`, `fill_cne_fields.py`

---

*Ce fichier sera mis à jour après chaque système complété.*

## Session — 262/262 commandes zone + refactorisation anti-patterns

### Refactorisation (qualité)
- **God file éliminé** : `Eden.Zone/Program.cs` 1207 → 193 lignes
  (config + composition + lifecycle uniquement)
- **15 fichiers handlers domaine** (`Handlers/`) : entry, combat,
  inventory, item-action, trade, shop, social, team, elf, instance,
  player, vendor, family, housing, misc
- **`RegisterChar`** : extension dispatcher qui injecte `CharacterId`
  validé — élimine 54 duplications du check session
- **`ZoneServices`** : conteneur de dépendances partagées
- **Boat anchors retirés** : `HandleEnterZoneAsync`,
  `HandleUpdateStatsAsync` (remplacés par le flux ReceiveTicket réel)

### Couverture commandes : 80 → 262/262 (100%)
Nouveaux handlers avec logique réelle :
- **Elfes** : call/return/rename/talk/revive/work/mix/stone → `ElfService`
- **Réparation/identification** : devis + réparation effective
  (coût = prix_vente × durabilité manquante — approximation documentée)
- **Tri inventaire** (ArrangeItem), split, sertissage, renforcement
- **Sorts AoE** : CastToLocation/Nearby/Nowhere via `FindInRange` +
  pipeline CombatService ; SpellBreak ; StateCancel (RemoveBuff)
- **Battlefield/donjon** : join/leave/list → `DungeonService` +
  `GetInstanceOf`
- **Achat de sorts** chez le maître de classe (or → appris)
- **Trade complet** : + TradeReady / TradeUnConfirm
- **Vending joueur** : stand sell/buy/close (transfer or réel)
- **ItemMall** : ItemMallUseItem → `ItemMallService.Purchase` AP
- **Cycle de vie** : SafeQuit (req/cancel/over→déco), LogoutReq, Echo
- **Présence** : emotes/actions broadcast, TurnSelf (Dir sur entité),
  ciblage, PvP flag, résurrection (Resurrect vs Revive)
- **Titres** : UpdateAppellation → AchievementService
- **Talents** : UseSkillPoint → TalentService.SpendPoints
- **Famille** : conteneur, instance de guilde, ville, statue, logs
- **Arènes/PvP** : colosseum/gladiator/battle royale → MatchmakingService ;
  TerritoryJoin → TerritoryWarService
- **ItemUse** : consommation réelle (quantity-1 + effet)
- **LookEquip, MailQuery, RankInfoQuery** : requêtes d'info

### Helpers ajoutés
- `ZoneSessionManager.SendToAsync(charId, cmd)` — envoi ciblé
- `ZoneEntityRegistry.FindInRange(x, y, r)` — requêtes AoE
- `ZoneEntity.Dir` — orientation
- `SpellService.RemoveBuff` — annulation manuelle de buff
- `DungeonService.GetInstanceOf` — instance d'un joueur
- `ElfInstance.Name` + `Rename` effectif

### État
- Tests : **199/199** ✅ — Build : **0 erreur, 0 warning**
- 38 TODO documentés "phase reverse" (layouts bouclés CNC,
  persistance tables, formules exactes à reverser)

## Session — Payloads bouclés CNC : 18 layouts reversés au désassembleur

### Méthodologie
Désassemblage direct des `CNC_*::Serialize` natifs (objdump) — chaque
classe bouclée a été vérifiée champ par champ au lieu de deviner.
Trois patterns identifiés :
- **SoA** (parallel arrays indexées `esi`) — MailList
- **AoS** (stride fixe `add ebx,0x38`) — TeamAllMembersInfo
- **Boucles imbriquées** vector/map STL — SearchTeamInfo (équipe→membres),
  EnterNodePlayerData (3 boucles Rb_tree), EnterNodeEquipAndSpell
  (diff-encodage conditionnel avec sentinel -1)

### Classes CNC réécrites en vraies listes (`tools/patch_cnc_loops.py`)
| Classe | Layout natif vérifié |
|---|---|
| `ClientMailList` | u16 count + ~70 champs/mail (SoA, 0x8204660→0x8204b39) |
| `ClientPlayerClassList` | [byte×2][u16] + byte+SCharacterClass(byte,byte,int,int,byte,ushort×7) |
| `ClientTeamAllMembersInfo` | [short count]+entrées AoS stride 0x38+[short] |
| `ClientSearchTeamInfo` | u16 équipes → u16 membres(int,string,short×5)+string×2+short |
| `ClientEnterNodeShortcut` | u16 barres → u16 bitmask 10 slots + int/slot + u16 ints |
| `ClientAuctionSell/BuyList` | u16(+short) + record objet+enchère ~50 champs |
| `ClientLookEquip`/`PVPRankLookEquip`/`QueryPlayerCard` | préfixe + tableau ushort équipé |
| `EnterNodeInventory`, `FamilyStorageLog`, `SearchRoomResult` | boucles simples vérifiées |
| `AchievementUpdate`, `UpdateCollection`, `UpdateNPCSymbols` | boucles simples vérifiées |
| `FamilyBattleKilledRank`, `UpdateFortuneBagCounter` | boucles simples vérifiées |

### Handlers branchés sur les vraies données
- `MailQuery` → `CNC_CZ_ClientMailList` peuplée depuis `player_mail`
- `QueryClassList` → `player_classes` réel (classId/level/exp/points)
- `AuctionSellQuery`/`BuyQuery` → `AuctionService` réel
- `LookEquip` → `inventory_slots` (items équipés)
- `SearchTeam` → `TeamService.AllTeams` + `BroadcastTeamInfo` (composition
  complète envoyée aux membres)
- `EnterNodeInventory` → items bag 0 réels
- `TeamInviteAnswer` → `CNC_CZ_ClientTeamAllMembersInfo` broadcast

### Corrections qualité
- `patch_cnc_loops.py` : regex fin de classe corrigé (`}` colonne 0),
  préservation `Opcode`/`Name`, suffixes supportés, `Name`→`PlayerName`
  (collision `NetCommand.Name`), casts unsigned fidèles à BinStream,
  `string` initialisés à `""` (évite ArgumentNullException)
- `CombatHandlers` : warning CS1998 corrigé → **0 warning**
- `TeamService.AllTeams` ajouté
- `TeamNewMemberInfo`/`EnterNodeElfInfo`/`UpdateFriendMember` vérifiés :
  records fixes, pas de boucle (extracteur auto corrigé en conséquence)

### Reste sur les boucles
- `EnterNodePlayerData` : ~30 scalaires + vecteur de records à
  maps imbriquées — structure identifiée, sérialisation complète en cours
- `GetStorageData`/`FamilyStorageData`/`EnterNodeEquipAndSpell` :
  diff-encodage (champ émis seulement si != sentinel -1) — modèle
  de présence par champ requis
- `PutBinary` = octets bruts SANS préfixe de longueur (taille implicite)
- Extracteur `cnc_loop_layout.json` : 77 candidats auto dont faux
  positifs (trailers fixes) — validation manuelle obligatoire

### État
- Tests : **207/207** ✅ (+8 LoopMessageTests round-trip)
- Build : **0 erreur, 0 warning**

## Session — POC Login : handshake RSA→RC4 + auth + liste channels (209/209 tests)

### Reverse engineering (LoginServer natif)
- **Machine à états CMSTCPChannel** (`+0x208c`) : état 1 = envoi blob RSA
  brut à l'accept (`Open`/`OnWrite`), état 2 = `ReceiveRC4Key`
  (256 octets, `RSA_private_decrypt` **PKCS1**, résultat == 5 octets),
  état 3 = paquets `[u16 len][RC4(NCID+champs)]`
- `CCLTCPChannel::OnReady` → `CNE_CL_ServerHello("HELLO CLIENT")` →
  `CNC_CL_ClientHello` (float 9.9) poussé dès la connexion
- `CNC_CL_ClientGetWorldServers::Serialize` désassemblé : `i16 count` +
  itération std::map `(i16,str,str,i16,i32,i16×5)` + `i16 lastWorldId`
- `CNC_CL_ClientReceiveTicketToWorldServer` = **22 octets bruts**
  (boucle `cmp ebx,0x16`), pas 1 byte — classe générée corrigée
- **Auth officielle** : `account_login(mid, md5pwd, clientIp)` sur
  `fnmember.tb_user` via Gateway — **le client envoie md5(password)**
- Schéma réel : `worlds(online_user,maxnum_user)`,
  `character_number(account_id,world_id,"character")`, `fake_worlds`,
  `gm_tool_accounts` — tous dans **fnaccount** (sauf tb_user → fnmember)

### Implémentation POC (docs/implementation/45-login-poc.md)
- `Eden.Login/Program.cs` réécrit : RSA-2048 → RC4 → dispatch NCID
  35/36/37 (remplace le placeholder heuristique complet)
- `LoginPacketHandlers` : vraies classes générées, auth directe
  `accounts` (plaintext OU md5 acceptés), env `POC_ADVERTISE_IP/PORT`
- `WorldRepository` : vraies colonnes `online_user`/`maxnum_user`/`character`
- `Database/login-poc.sql` : compte **admin/adminmdp** (accounts +
  tb_user byauthority=1 GM + gm_tool_accounts=255) + world 1010→127.0.0.1:5200
- docker-compose : seed monté (50-login-poc.sql) + env POC_ADVERTISE_*
- 7 classes placeholder `Messages/Login/*` supprimées (lava flow)

### Vérification
- Smoke wire-level : blob RSA → clé RC4 chiffrée → `ClientHello(38,9.9)`
  → `ServerLoginAccount(36)` → `ClientGetWorldServers(40)` ✅
- Tests : **209/209** ✅ (+2 : auth md5 + échec)
- Build : **0 erreur, 0 warning**

### Reste pour la migration login→world
- Ticket : émis au format CTicket mais le registre partagé et la
  validation côté World (`CNE_CW_ServerReceiveTicket`) restent à brancher
- Chemin officiel Gateway→`account_login()` sur fnmember (POC = direct)

## 2026-10-15 — Entrée en jeu E2E validée (World C# + Zone native)

**Milestone majeur** : `test3` entre en jeu via le World C# — quêtes,
déplacement, combat, session persistante. Chaîne : Login natif → World C#
(:5300/:5301) → Zone native (:10022) + Mission natif (:7654).

**Gate résolu** : la zone n'envoyait pas `CNC_CZ_ClientEnterStarWayData`
(NCID 609) car sa liste StarWay était vide — le monde natif la pousse via
`CNE_ZW_ZoneServerUpdateStarWay` (ZW NCID 29) à l'enregistrement, un paquet
par ligne de `fnaccount.starway`. Sans réponse au
`CNE_CZ_ZoneServerEnterNodeStarWay` (client NCID 256), le client reste en
chargement → "Server connection: Failure" ~45s.

**Implémenté** : `ZoneLinkServer` pousse les 11 entrées StarWay au
`IDReport` de la zone ; format désassemblé (`s16 week, s16 theme, u16 n+n×s16
effects, u16 m+m×s16 items, u16 len+remark GBK`). Capture validée
byte-par-byte : séquence 256→609×11→133/25→490/312/280→… identique au natif.

Détails : `docs/implementation/46-zone-world-link.md`.
Captures : `captures/zsniff_cs_entry_success.bin` (C#), `zsniff_native_entry.bin` (natif).

**Reste** : pushes FortuneBag(26)/Lottery(27), lien World→Mission (:7654)
pour cross-zone (test1 bloqué `cross zone revive area failed`), sorties
propretes des flags online.

---

## Session — correctifs NPC quêtes/banque, anim classe fantôme, états 391 (live test1)

**Banque (50347)** : record `#:Service 5218 6  #` — refId VIDE → le
parseur exigeait 3 nombres → service ignoré → fenêtre texte sans option.
Fix : refId optionnel (`GameDataParsers`), le gossip émet l'option 5218 →
clic → 357 EnterStorage. (50346 `#6##` = ouverture directe — déjà OK.)

**Anim "changement de classe" à chaque entrée/rez** : l'overlay post-
MigrationDone renvoyait 283 ClientModelModify (mask 0x5e00 = transition
de classe) + attr 0x2c + 489 — la capture d'entrée native n'a AUCUN de
ces paquets (classe portée par 310 classId@23). Overlay réduit aux attrs
0x11/0x00/0x12/0x01/0x02 — fin de l'anim fantôme.

**Quêtes (flux décodé capture 17:23:20-25)** :
- Entrées quête du 285 : `displayId=-3`, `E=mid en texte` ("1012") —
  le client résout titre/objectifs via sa table T_Mission (remplace le
  `mid*10+4` bidon).
- Quête prête à rendre → `mission-reward` (388 op 2), PAS mission-finish
  (op 4 = post-claim). Le clic quête ouvrait donc une "fin" inexistante.
- 391 wire : `[u8 keyType=0][i32 mid][s16 state][s16 progress]` — états
  clients : active=3, prête=4, finie=1, retirée=0 (remplace le packing
  interne status|progress<<8).
- Claim (client 87 op 2) → mission termine + envoie `shop mission-finish`
  → 388 op=4 + 391 mid→1 — séquence native complète.
- Client 87 MissionListOp déjà routé (SocialHandlers → MissionServiceOp).

**489 PlayerClassList** : le 2e octet d'entrée = `level` (SCharacterClass
Serialize : [u8 lv][u8 style][i32 exp][i32 point][u8 aa][7×u16 lic]) —
on y écrivait aa_point. Corrigé + champs réels DB.

**Reste à valider live** : fenêtre quête (objectifs/récompenses/boutons),
banquier 50347 → 357, liste classes complète, fin de l'anim à l'entrée.

## Session — inventaire/raccourcis/portal-stone/GM réels (zone)

### Codec item natif — validé byte-pour-byte
- `CNC_CZ_ClientEnterNodeInventory::Serialize` désasm complet : item =
  `[u16 id][s16][s16][raw-str][u8][u24 mask][cond×bits][34B tail]`.
  La string n'a **pas** de préfixe len (raw bytes) ; le mask u24 rend
  les conditions sparse. Parse parfait du replay test3 (5×46B).
- `NativeItemCodec.cs` créé ; le 312 n'est plus le stub généré mais
  construit depuis `player_items` + capacités `inventory_slots`
  (`bag_0..3_slots`). GetBagCapacity ajouté au repo.
- Raccourcis : 54 → `character_shortcuts` upsert ; 55 → 352 (page
  demandée, 11 ints) ; 351 réel depuis DB à l'entrée. Replay [25]/[53]
  retirés (état test3 figé supprimé).

### Portal Stone — NCID 160 MissionGoto
- Wire `[s16 mid][s16 type][s16 npc][s16 flag]` (désasm Execute
  @0x826b970) : flag=1 consomme 0x9c54/0xa476 ; cible NPC résolue
  via SceneParser (NpcId template) → MigrateToNodeAsync.

### GM
- `clevel` : niveau de classe dans player_classlist.
- BuySpell/UseSkillPoint/168/166 vérifiés câblés.

### Restant
- Spawns dynamiques par état de quête (S_SceneEvent), localisation
  Big5, confirmation client des anims auto-attaque (spell 0xC360
  absent de S_Spell).


## Session 25 oct. 2026 — loot/exp d'équipe natifs, LFG, refactor taille

### Distribution de loot native (`GetLooter`)
- `PickLootReceiver` = transcription de `GetTreasureLooter`/
  `GetLooter`/`GetLootOrderMember` : règle 3 / raid25 → tueur garde ;
  règles 1-2 → tirage **uniforme** `random()%n` parmi les éligibles
  locaux en rotation depuis le curseur +0x52 (1/n : 2→50/50, 3→33%…
  pas de paliers pondérés — le natif n'en a pas) ; autre règle →
  séquentiel après curseur ; <2 éligibles → tueur.
- `ChageLootRule` natif écrit aussi +0x56 → `ChangeLootRule` règle 3
  coupe le partage d'exp (`ExpNoShare`), 1-2 le rétablissent.

### LFG (Phase 5)
- Annonces de recrutement (leader-gated) + recherche réelle ;
  `ClientSearchTeamInfoV2` réécrit au format variable natif.

### Refactor taille (règle <1000 lignes/fichier)
- `GameDataParsers.cs` 1299 → 6 fichiers par classe.
- `ZoneEntryFlow.Combat.cs` 3629 → Combat(674)/Mob(783)/Death(396)/
  Rewards(724)/Spells(635)/Spells.Resolution(541).
- `QuestEngine.cs` 1816 → 372 + Dialogue/Objectives/Events partials.
- `InventoryHandlers` 1200 → 885 + Helpers(345) ;
  `InventoryRepository` 1195 → 1000 + Bags(195).
- `ZoneEntryFlow.cs` 1219 → 688 + Lifecycle(531) ;
  `ZoneEntryFlow.Mission.cs` 1238 → 753 + Commands(485).
- Plus aucun fichier source >1000 lignes (hors `*.g.cs` générés).

Tests : 724/724. Build : 0 erreur, 0 warning.

## Session 26 oct. 2026 — guilde débloquée, montures/binds/pets validés

### Guilde — cause racine du freeze/crash trouvée
- `player_characters.create_time` (timestamp PG) → `CharRow.create_time`
  int : `InvalidCastException` dans `FamilyRepository.LoadAllAsync` →
  **WorldServer mourait au boot dès qu'une famille existait** (table
  vide jusqu'ici → bug dormant). Fix `11c9138` : epoch cast aux 2
  SELECTs membres.
- Audit NCID complet : **262/262 CNE_CZ routées** (zone), 35/35
  CNE_CW (monde). Famille couverte bout-en-bout : zone (130/131/174/
  175/176-180/225/239 via flow) + monde (FamilyOp 22 sous-ops, query,
  emblème).
- **Validé headless monde** (`/tmp/eden_world_probe.py`) : ticket T_
  → ReceiveTicket → liste persos 38 → LoginCharacter → séquence
  40/72/37 → QueryFamilyInfo 10 → **50 fiche + 50 membres + 51 + 68
  + 60 prestige** (séquence = handler natif).

### Montures — cycle complet validé headless (zone :5201)
- Mount : 303 ack + 369 bind + 299 enchant + 283 model rideKey=12010.
- **Dismount = NCID 85** (cancel ride enchant 55106), pas un 2e
  ItemUse (refusé par cooldown 506) : 300 + 283 rideKey=0 + 545+296+492+493.
- Facing NCID 326 : champ u16@14 = half-float facing (pas vitesse) —
  `char+0x58` encodé float→half, bit0 = hasDst. Mobs : atan2 dir.
- Combat à dos : kfId depuis le KFM monture (`r{key}`), 1021/1022
  aléatoire, 1023 arc, 1024 gun — `CCharKfm::GetAttackID` branche
  +0xd60.
- Turn 441 : broadcast à soi+observateurs (BroadCastToCanSee natif
  inclut l'acteur à distance 0).

### Bind / unbind (1914)
- Monture bindée à l'usage (`BindItem` natif post-contrôle) + 369.
- `CEC_ItemUnBind` : scrolls mall 45150-45258 (UseType=5, enchants
  60355-60358). Roll rand%10000<var0, FailType/MaxLevel/UseType natifs.
- 🔴 Bug fixé `c9b4bb1` : double dispatch UseType 3/5 — l'unbind était
  dead code derrière l'early-return inlay/witchcraft. Validé live :
  échec → 316 texte 0x23c7 ; succès → 436 + 369 + bind=0 DB.

### Pets
- `S_Node.PetFlag` parsé depuis la mauvaise colonne → gate « Pets
  unavailable » fixé (`b441b36`). Invocation mode 2 → entité ElfPet
  réelle + 513 shortcut + 418 status=2 (BornElfPet natif).

### Buffs / stats
- Canaux natifs mappés : AttrBase post(+0x540)/pct(+0x178)/
  spellref(mode1 = ref[level]×var%), AttrDefence/HitRate/Dodge/etc.
  vars indexées par position (2081-2087, 2095, 2101 — preuves
  objdump DecodeEffectCommand).
- NCID 299 counter=1 natif (CEnchant+0x30++ par Add).

### Client Reforged patché
- Cast en mouvement : micro-stop NOPé (5 call sites FUN_0075ad10),
  steering RMB débloqué (jne→9090 @0x75aa0c), 2 jbe→jmp gates.
  Doc `docs/PATCH-CLIENT-BINAIRE.md`.

### GM / divers
- `//gain_ap_point`, `//gain_ep_point`, `//find_item` validés live
  (AP/EP fnmember.tb_user corrects ; items mall fusionnés).
- Zéro « opcode inconnu » dans les logs live.

### Reste à confirmer côté client réel
- Panneau guilde (le fix est serveur — crash au boot supprimé).
- Visuel pets (spawn OK serveur, entité visible pour observateurs).
- Anims d'attaque monture combat, facing strafe, cast-move Reforged.

## Session 24 oct. 2026 — parité login monde + find_item montures

### Séquence login monde — diff natif vs C# (capture MITM native)
- Le natif émet **4× NCID 69 (`CNC_CW_ClientCaptcha`, ~2745 o)** après
  `LoginCharacter` — anti-bot images ; `CCaptcha::IsA(mgr,id,3)` +
  `CreateCaptcha` retournent SANS `EnterWorld` si requis. **Choix :
  non porté** (la connexion client fonctionne sans — le captcha est
  anti-bot, pas obligatoire).
- `TC_QueryFriend` mission @0x081f4d90 : `QueryFriendData` (52+61)
  **+ `AllPrestigeData` (→60) + `SendSpecialCoinInfo` (→78)** — le
  client attend prestige/mérites en réponse à NCID 13.
- **Fix `dfc6a23`** : QueryFriendInfo pousse désormais 52+61+60+78
  (`SendPrestigeSyncAsync` partagé + `SendSpecialCoinInfoAsync` émet
  78 même à n=0 — capture native `4e000000`). Titres d'honneur /
  prestige alimentés à chaque query ami ET query famille (10).
- Le reste de la séquence (40/72/37 + 57/64 à la demande) était déjà
  conforme.

### find_item — montures Reforged couvertes (live)
- `find_item blizzard` → **43801/43802 Nine Tail Blizzard Fox**,
  48799 Deep Blue Nine Tail, 41790 Goggles (6 résultats).
- `find_item nine tail` → 42872-42876 pets + 43175+ firefox (8).
- `find_item stalwart` → 21450 Sabretooth + stuff (8).
- Le point « monture shop introuvable » est résolu par le merge
  T_ItemMall (nom) + S_ItemMall (def, enchant col24 = ride 55867).

### FastDress — déjà complet (audit)
`ZoneEntryFlow.FastDress.cs` (700+ lignes) : 243→593, 244 apply
(switch équipement + enchant 46690), 245 save, 246 record
(CheckFastDressUseItem + conflits 1M/2M), 247 page, 248 remove —
table `fast_dress` PK (player,page,tar_loc). Stubs HousingHandlers =
gardes pré-entrée seulement.
