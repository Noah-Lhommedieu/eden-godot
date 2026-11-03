# Protocol partials — analyse détaillée des opcodes incomplets

Complément de `protocol-map.md` (2025-11). Trois populations analysées :

1. les **9 opcodes C→S « partiels »** du canal zone (7 replay/squelette
   + 2 court-circuités) ;
2. les **105 paquets S→C définis mais jamais émis** (80 zone + 25 monde) ;
3. les **135 handlers serveur « orphelins »** (handlers écrits que le
   client Godot n'appelle jamais).

Lecture statique uniquement — **pas de build/test** (consigne). Chaque
constat est croisé entre le code C# (`eden_server/server/src/`), le
client Godot (`eden_godot/godot_project/scripts/`) et les exports
Ghidra (`re/ghidra/<binaire>/decompiled.c`, via `reindex`).

**Statuts** : `verified` = constat direct (code lu, corps décompilé,
xref `reindex`) ; `hypothesis` = inférence d'usage non prouvée en
runtime. **Date : 2026-11-03.**

## 0. Dispatch zone — l'ordre exact qui rend les partiels « partiels » (verified)

Pour tout paquet C→S zone, `Program.cs:945` appelle
`ZoneEntryFlow.TryHandleAsync` ; s'il retourne `true`, le
`PacketDispatcher` ne voit jamais le paquet. Dans
`TryHandleOnLoopAsync` (`ZoneEntryFlow.cs`) :

1. **switch** `~125 case` à partir de `ZoneEntryFlow.cs:474` ;
2. **replay** `ZoneEntryFlow.cs:1172-1177` : si `st.CharId != 0` et
   `EntryReplayData.Map` contient l'opcode → chaque trame hex est
   envoyée via `SendPatchedAsync` (`ZoneEntryFlow.Lifecycle.cs:389-536`),
   puis `return true` ;
3. **acquittement silencieux** `ZoneEntryFlow.cs:1190` : `opcode is
   104 or 156 or 172 or 204 → return true` ;
4. sinon `return false` → `PacketDispatcher.DispatchAsync`
   (`Eden.Core/Networking/Handlers/PacketDispatcher.cs:55`).

Deux détails structurels :

- `PacketDispatcher._handlers[opcode] = handler` — **la dernière
  inscription gagne** (`PacketDispatcher.cs:30,49`) ; `RegisterChar`
  (`PacketDispatcherExtensions.cs:12-19`) enrobe `Register` et no-op
  si `session.CharacterId == null`. Ordre d'inscription :
  `EntryHandlers` (`Program.cs:796`, inclut `RegisterSkeletons` à
  `EntryHandlers.cs:93`) **avant** `ElfHandlers` (`Program.cs:804`).
- `EntryReplayData.Map` contient 22 clés
  (`EntryReplayData.cs:10-158`) : `{4,24,26,50,57,62,110,113,133,152,
  162,164,165,166,181,195,213,223,243,256,258,558}`. Quinze ont un
  `case` dans le switch (4,50,57,110,152,162,164,165,166,181,213,223,
  243,258,558) → données de replay **mortes** (le `case` gagne) ; les
  7 restantes = les 7 partiels « replay » : **24, 26, 62, 113, 133,
  195, 256**.

Conséquence : les 9 partiels ont **tous** un handler `d.Register*`
écrit — et **aucun ne s'exécute jamais** en pratique (replay ou
silence les intercepte avant le dispatcher). Leurs handlers sont du
code mort ou de la documentation vivante.

Corps replayé (NCID réel → trame stockée, `verified` par décodage hex) :

| opcode C→S | NCID réponse | trame `EntryReplayData` | contenu |
|---:|---:|---|---|
| 24 | 311 | `3701…` (l.60-63) | corps **reconstruit live** à chaque envoi (voir §1.1) |
| 26 | 310 | `3601…` (l.66-69) | 417 B de test3, 11 champs patchés |
| 62 | 358 | `6601 ffff 01 20 00 00 04…` (l.81-84) | type=-1, **1 onglet de 32 slots vide** |
| 113 | 424 | `a801 00` (l.89-92) | **0 conteneur elf** |
| 133 | 490 | `ea01` + 27×`00` (l.93-96) | carte joueur tout-à-zéro |
| 195 | 523 | `0b02` + 51×`00` (l.121-124) | salle joueur zéro-remplie |
| 256 | 609 | 8× `6102…` (l.137-150) | config StarWay figée (capture test3, remark « 2018/05/10 ») |

`SendPatchedAsync` patche : charId (`RefCharId` = 200000004 → scan LE,
`Lifecycle.cs:555-567`), nom « test3 », puis par NCID : **310** (11
champs prouvés par le ctor natif @0x867240d), **311** (rebuild
complet), **313** (node_id courant — la capture portait node=2),
**302** (mot d'état courant pour ne pas écraser le bit « monté »).

## 1. Les 9 opcodes C→S partiels

Format demandé : `opcode | nom | ce que fait le handler C# (lignes) |
ce que fait le natif attendu | ce qui manque concret | risque gameplay`.

| opcode | nom | handler C# (lignes) | natif attendu | manque concret | risque gameplay |
|---:|---|---|---|---|---|
| 24 | EnterNodeEquipAndSpell | Replay `EntryReplayData.cs:60` + rebuild live `Lifecycle.cs:479-499` ; squelette vide `EntryHandlers.cs:227-229` | `Execute` zone @0x824f1e0 → `CNC_CZ_ClientEnterNodeEquipAndSpell(CCharacter*)` : 6 conteneurs (bitmap locs + records) + sorts | Rien dans le corps (équipement+sorts live) ; reste le détour replay et le squelette mort | **Faible** — contenu conforme ; fragilité si `EntryDirty` |
| 26 | EnterNodePlayerData | Replay `EntryReplayData.cs:66` patché 11 champs `Lifecycle.cs:400-470` ; handler réel `EntryHandlers.cs:67-90` | `Execute` zone @0x82503a0 → ctor @0x86721a0 : ~90 champs CCharacter + `ILifeEntity::Action(…,5)` | Tout le « rest » (signature, appellation, stats, maps, lifelong, évaluations) = valeurs figées de test3 ; `RegisterPlayer` mort → registre d'entités sans joueurs | **Latent/modéré** — invisible client actuel ; gardes `Entities.Find(charId)` silencieusement nulles |
| 62 | GetStorageData | Replay `EntryReplayData.cs:81` → 358 « 1 onglet vide » ; vrai handler `InventoryHandlers.cs:409` → `SendStorageDataAsync` (`InventoryHandlers.Helpers.cs:347-377`) | `Execute` zone @0x825bc20 → contenu réel depuis `s16 type` + map `char+0x510` | Le contenu réel de la banque à l'ouverture — le replay sert **à chaque ouverture**, pas seulement à l'entrée | **Majeur** — banque toujours vide à l'ouverture ; items visibles seulement après une mutation (push 358 live) |
| 104 | SearchOpt | Acquitté `ZoneEntryFlow.cs:1190` ; handler `MiscHandlers.cs:566-572` (`SearchLfg/SearchVisible = Field != 0`) | `Execute` zone @0x82840f0 : `field==-1` ignoré ; sinon `EnableInviteTeam=(field==0)`, `EnableSearch=(field==0)`, persistance `CDBAgent::Query` ; **aucune réponse** | Les deux flags jamais stockés ; le handler mort a la **polarité inversée** (`!=0` vs natif `==0→true`) | Nul aujourd'hui (pas d'UI recherche) ; **bug confirmé** si 103 SearchCondition arrive : liste vide garantie (`x.SearchVisible` jamais vrai) |
| 113 | EnterNodeElfContainer | Replay `EntryReplayData.cs:89` → 424 « 0 conteneur » ; squelette `EntryHandlers.cs:230` écrasé par le vrai handler `ElfHandlers.cs:20-41` (ElfRepo + InventoryRepo → `NativeElfCodec`) | `Execute` zone @0x824ce80 → map `char+0x540` : 1 `CElfContainer` (4 slots) **par elf existant, même vide** | Rien dans le handler — c'est la voie replay qui court-circuite un handler déjà correct | **Modéré** — ElfBook affiche 0 slot d'équipement familier même si le joueur a des elfs (`ElfBridge.cs:198`) |
| 133 | EnterNodePlayerCard | Replay `EntryReplayData.cs:93` → 490 tout-à-zéro ; squelette `EntryHandlers.cs:233-235` | `Execute` zone @0x824fe70 : `char+0xb64` carte → NULL → **silence** ; sinon 12×s16 + str + u8 | La carte réelle (compteurs PvP/exploit, titre) ; écart mineur : le natif se tait sans carte, nous émettons un zéro | **Faible** — fenêtre carte à zéro ; correct tant que le système de cartes n'existe pas |
| 156 | QueryLoverInfo | Acquitté `ZoneEntryFlow.cs:1190` ; handler `SocialHandlers.cs:352-359` = log-only | `Execute` zone @0x8278a50 : `char+0xa8c` loverId → `CLover::UpdateClientData()` + `UpdatePartnerPosition` → pousse les 476/477/479 | La chaîne entière — `Lovers.GetCouple` existe mais aucune réponse couple n'est construite (S→C 475-479 tous absents, cf. §2) | **Modéré** — système couple/mariage muet côté client à l'entrée |
| 195 | EnterNodePlayerRoom | Replay `EntryReplayData.cs:121` → 523 zéro-rempli ; squelette `EntryHandlers.cs:236-238` | `Execute` zone @0x82507a0 : `CPlayerRoomManager::GetPlayerRoom(char+0x34)` → trouvé : 523 plein (~15 args + map déco) ; absent : **1 paquet zéro** — émis dans les deux cas | La salle réelle *si elle existe* — or RentRoom (206) est orphelin → aucune salle créable → la branche zéro = sortie native correcte pour tout état atteignable | **Latent** — correct aujourd'hui ; cassera à l'activation du housing |
| 256 | EnterNodeStarWay | Replay `EntryReplayData.cs:137-150` → 8×609 figés ; squelette `EntryHandlers.cs:239-241` (1 seul 609 vide) | `Execute` zone @0x82511b0 : itère `CPlayerFactory::StarWayData` → **un 609 par entrée** (clés week/theme + vector effects + vector items + remark) + `Action(…,5)` | Le relais live : le monde pousse déjà la config via ZW 29 → `ZoneWorldLink.StarWayData` (table `starway`) — jamais servie aux clients | **Faible/modéré** — config 2018 figée ; divergence si la table tourne ; plomberie prête, relais manquant |

### 1.1 Opcode 24 — EnterNodeEquipAndSpell (verified)

Le corps 311 du replay est **entièrement reconstruit** à chaque envoi
(`Lifecycle.cs:479-499`) : `st.Entry.EquipSpellBody` prebuilt par le
pipeline Phase 2, sinon requête live
`InventoryRepo.GetAllEquipmentEntitiesAsync` + `Classes.GetPlayerSpellsAsync`
→ `NativeItemCodec.BuildEnterNodeEquipAndSpell`. Le statut « partiel »
de la map reflète la plomberie (détour par une trame capturée dont le
corps est remplacé), pas le contenu. Le squelette `EntryHandlers.cs:227`
(envoie le 311 généré vide) n'est joignable que si un client demande 24
avant tout ticket — jamais en pratique — et serait de toute façon
court-circuité par le replay dès `CharId != 0`.

### 1.2 Opcode 26 — EnterNodePlayerData (verified)

- **Côté fil** : le client Godot ne lit que `charId + name` +
  `NativePlayerDataParser` (`ZoneHudMessages.cs:519-541`) qui consomme
  **exactement les 11 offsets patchés** (level@23→Rest@12, HP@25→@14,
  MP@33→@22, MaxHP@41→@30, MaxMP@45→@34, class@49→@38, appearance@51→@40,
  classLv@83→@72, classExp@85→@74, CP@105→@94). Aucun champ figé de
  test3 n'est visible aujourd'hui ; chaque extension future du parseur
  lira des données de test3 (signature « Losing is practice for a
  future victory. », appellation « Obtain the Proof of Power », séries
  `f401…` de stats, maps d'évaluation, lifelong, bloc PvP).
- **Côté serveur** : le handler mort `EntryHandlers.cs:67-90` contient
  le **seul appel** à `s.Entities.RegisterPlayer` du dépôt (grep
  confirmé : définition `ZoneEntityRegistry.cs:79`, appel unique
  `EntryHandlers.cs:74`). Conséquence : `ZoneEntityRegistry` ne
  contient jamais de joueurs →
  - `TradeHandlers.cs:38-46` : `self`/`partnerEnt` null → `distance=0`
    → le garde « trop loin » (`> MaxTradeDistance` → texte 0x333) ne
    peut jamais se déclencher ;
  - `PlayerHandlers.cs:79` : `e.Dir` jamais mis à jour (le 441
    TurnChar du flow compense côté wire) ;
  - `PlayerHandlers.cs:272-279` : `e.IsDead` jamais vrai → le
    broadcast `CNC_CZ_ClientResurrect` du handler 44 mort ; le vrai
    chemin de rez vit dans le flow (`case 20` :1145, `case 44` :1146).
- **Détail** : le handler mort enverrait un 310 *simplifié*
  (Field1 charId, Field2 name, Field7 level, Field8 exp) qui est de
  toute façon un format différent du natif (~90 champs). Le garder
  documente l'intention ; le réactiver casserait le client.

### 1.3 Opcode 62 — GetStorageData (verified)

Le point le plus grave de la liste : `EntryReplayData.Map[62]` n'a
**aucun garde de phase** — chaque `ZoneGetStorageData` (émis à chaque
ouverture de la banque PNJ, `GameScene.cs:3036` `EnterStorage`)
reçoit le même 358 figé : `type=-1, 1 onglet cap=32, bitmap=0` →
banque vide. Le handler réel (`InventoryHandlers.cs:409` →
`SendStorageDataAsync`, `InventoryHandlers.Helpers.cs:347-377`)
construit le 358 depuis le dépôt banque — correct mais jamais
atteint. Le contenu n'apparaît que via les pushes 358 directs après
dépôt/retrait (`InventoryHandlers.cs:308-414`), qui ne passent pas
par le replay.

### 1.4 Opcode 104 — SearchOpt (verified)

- Natif `zone @0x82840f0` : pour chaque champ `!= -1` →
  `EnableInviteTeam = (field+0x12 == 0)` et `EnableSearch =
  (field+0x14 == 0)` ; persistance via `CDBAgent::Query`. Le natif
  **ne répond rien** → notre acquittement muet est protocolairement
  conforme, mais les flags ne sont jamais stockés.
- Le client Godot envoie `ZoneSearchOpt { 0, 0 }` au burst
  (`protocol/ZoneMessages.cs:147-164`) — sémantique native : les deux
  options **activées**. Le handler mort fait `Field != 0` → il aurait
  inversé le sens (envoyer 0,0 aurait mis les deux flags à `false`).
  **Bugs superposés** : interception (flags jamais écrits) + polarité
  (handler faux s'il était atteint).
- Consommateur : `SearchCondition` (103, `MiscHandlers.cs:580-620`)
  filtre `x.SearchVisible` — jamais vrai → toute recherche renvoie la
  liste vide. Orphelin aussi : le client n'émet jamais 103.

### 1.5 Opcode 113 — EnterNodeElfContainer (verified)

Cas particulier : il existe **deux** inscriptions. Le squelette
`EntryHandlers.cs:230` (envoie le 424 généré vide) est écrasé par
`ElfHandlers.cs:20-41` (inscription postérieure, `Program.cs:804` >
796) qui produit un 424 correct — un conteneur 4 slots par elf de
`ElfRepo`, items de `InventoryRepo.GetElfContainerItemsAsync`,
`NativeElfCodec.BuildEnterNodeElfContainer`. Le replay `a801 00`
(« 0 conteneur ») masque ce handler déjà bon : le fix consiste à
router l'opcode vers le dispatcher (ou supprimer la clé 113 du Map),
pas à réécrire le handler.

### 1.6 Opcode 156 — QueryLoverInfo (verified)

Le natif ne répond pas par un paquet direct : `Execute @0x8278a50`
résout `char+0xa8c` → `CLover` puis `UpdateClientData()` (qui émet
477 LoverInfo + 476 LoverUpdate — xrefs natifs confirmés) et
`UpdatePartnerPosition` (479). Notre handler mort se contente de
logger le couple (`SocialHandlers.cs:352-359`). La dépendance est
double : même en routant 156 vers le handler, il faudrait construire
les S→C couple (tous absents, §2 — famille « Couple/mariage »).

### 1.7 Interférences connues hors liste des partiels (verified)

- **Opcode 20 Revive** : `case 20` (`ZoneEntryFlow.cs:1145`, respawn
  réel) gagne sur l'inscription `CombatHandlers.cs:164`
  (`CNE_CZ_ZoneServerRevive`, opcode 20 — stub log+TODO). Le handler
  est mort même si le paquet arrive : même mécanisme d'ombre.
- **Silent list redondante** : `172` (`case 172` PlayerWorkCheckData
  → :983) et `204` (`case 204` QueryDestinyRoad → :830) ont déjà des
  `case` — leurs entrées dans `opcode is … or 172 or 204`
  (`:1190`) sont du code mort (la ligne s'exécute après le switch).

## 2. Les 105 S→C définis jamais émis

Méthode : pour chaque classe `CNC_CZ_*`/`CNC_CW_*` absente du code
serveur (colonne `handler_serveur` vide dans `protocol-map.md`),
`reindex xref PTR__<classe>` sur le binaire serveur natif compte les
sites d'instanciation (`operator_new` + `ComposeOutput`, y compris
les fonctor `operator()`). Verdict par famille :

- **« Émis par le natif »** → fonctionnalité que le natif pousse
  réellement → **gap d'implémentation** côté C#.
- **« Jamais émis par le natif non plus »** → classe protocole morte
  dans ce build natif : le paquet n'a pas à être émis par le serveur
  (legacy, flow passant par un autre service/canal, ou commande côté
  client). **Ne pas implémenter sans preuve supplémentaire.**

⚠️ Limite de la mesure (verified) : `reindex xref` ne couvre que le
binaire interrogé (`zone` ou `world`). Un push via le lien mission
(ZMis), le monde (ZW) ou un autre binaire échappe au comptage —
signalé en `hypothesis` quand c'est vraisemblable.

### 2.1 Zone — 80 absents : 57 émis par le natif (gaps) + 23 jamais émis nulle part

#### Groupe A — émis par le natif : fonctionnalités non implémentées (57)

| Famille | NCIDs (nb de sites xref natifs) | verdict |
|---|---|---|
| Combat / sorts / monstres | 315 EnchantReset(op), 323 DisplayCharSimple(4), 354 UseSkillPointOK(op), 361 SpellLoopCastStop(op), 362 QueryNpc(1), 367 ResetSpell(5), 380 MobTransform(2), 390 UpdateInteraction(op), 407 UseItemTime(5), 408 UseItemTimeBreak(2), 437 SetSpellStone(op), 440 MobChangeAI(6), 447 PVPTime(1), 499 PlayerEvent(2), 536 UpdateZoneMonster(7), 546 SpellFlyToLocation(2), 581 LifeFloat(2) | **Gaps réels** — respec sorts (315/367), montée de sort (354), timers d'objets (407/408), transformations/IA mobs (380/440/536), PvP (447), interaction (390), texte flottant de vie (581) |
| Familier (elf) | 415 ElfWorkResult(1), 417 ElfEvent(op), 419 ElfUpdateSkill(op), 420 ElfUpdateSoulAbility(op), 421 ElfGoToWork(2), 422 ElfDecompose(op), 423 ElfStoneUse(op), 442 ElfAction(2), 443 ElfReturnEffect(op), 569 LookElfPetEquip(op) | **Gap système entier** — tout le push elf travail/skills/action est natif mais absent du C# (seule l'entrée container 424 existe, et encore court-circuitée, cf. §1.5) |
| Inventaire / conteneurs | 359 PutItemIntoStorage(1 — site : `CNE_CZ_ZoneServerStorageDeposit::Execute` zone @0x828dd50), 360 PutItemIntoContainer(3), 614 BackItem(2 — `DoMagicBoxShift`) | **Déviation assumée** pour 359 : le natif émet un delta par dépôt, le C# repousse le 358 complet (`SendStorageDataAsync`) — fonctionnellement suffisant si 62 est corrigé. 360 (container famille/elf) et 614 (retour MagicBox) manquent |
| Couple / mariage | 476 LoverUpdate(4), 477 LoverInfo(4), 478 LoverBreak(3), 479 LoverPosition(3) | **Gap** — `Lovers` service existe (`SocialHandlers`) mais aucune émission client ; bloque 156 (§1.6). Nb : 475 LoverInvite est en groupe B (0 ref zone — passe probablement par le lien mission, `hypothesis`) |
| Famille (zone) | 485 FamilyContainerDeleteItem(1), 486 FamilyContainerPutItem(1), 505 FamilyStoragePutItem(1), 561 FamilyBattleInfo(1) | **Gap** — deltas conteneurs/stockage famille + infos guerre de territoire famille |
| PvP / compétitif | 468 BattleMemberPosition(2), 469 BattlefieldMemberInfo(4), 526 DisbandGladiatorTeam(op), 528 ColosseumJoinResult(2), 530 ColosseumLeaveJoinResult(3), 552 ColosseumModeQueryResult(2), 586 TerritoryPlayerInfo(1) | **Gap** — battlefield/colosseum/territoire : handlers C→S déjà écrits (196-201, 219, 189/190 — orphelins §3) mais leurs réponses/pushs manquent |
| Social divers | 533 CoRideInvite(7) | **Gap** — co-ride (monture à deux) : le natif émet l'invite depuis 7 sites ; handler C→S 202 déjà écrit (orphelin) |
| Events / loterie / pay | 549 UpdateHealthOnlineTime(2 — anti-addiction), 573 GreedyIslandInfo(5), 578 UpdateFloorInfo(2), 595 InGameLotteryNotify(1), 596 DiamondLotteryNotify(1), 603 PayEvent(1), 605 SlotFlicker(2), 610 UIControlNotify(2), 618 PetLotteryResult(3), 619 PetLotteryEnd(1) | **Gap** — île aux trésors, loteries/diamond/pet lottery, pay-event, notifications UI natives ; handlers C→S correspondants orphelins (184, 221-224, 233/234, 253-255) |
| Texte | 481 TextOutputItem(2) | **Gap mineur** — texte lié à un item |

#### Groupe B — jamais émis par le natif non plus (23)

| NCID | nom | analyse |
|---:|---|---|
| 261 | ElfCastEnchant | Aucun site d'émission natif — classe morte (hypothesis : réservé/version ultérieure) |
| 294 | LeaveNode | 0 ref — la sortie de nœud se fait par migration/ticket, pas par ce paquet |
| 295 | DestShutdown / 314 CanCloseConnection / 322 NonSafeQuit | 0 ref — shutdown serveur géré hors protocole client dans ce build |
| 319 | TeamInviteResult / 325 TeamNewMemberInfo | 0 ref — l'équipe natif se construit par 317/318/320 ; résultats probablement poussés par d'autres NCIDs (hypothesis) |
| 374 | SloganUpdate | 0 ref zone — le slogan vit côté carte/famille ; handler C→S 73 orphelin cohérent |
| 379 | VendorContainer | 0 ref — l'ouverture vendeur passe par 378/377 (déjà implémentés) |
| 411 | SearchInfo | 0 ref **mais** le handler C# `SearchCondition` (MiscHandlers.cs:617-619) l'émet déjà (`SocialPackets.NcidClientSearchInfo`) — « absent » de la map = statut stale ; site jamais déclenché car 103 orphelin + 104 §1.4 |
| 463 | EnterBattlefield / 467 AltarState | 0 ref — l'entrée BF passe par migration/nœud dans ce build |
| 475 | LoverInvite | 0 ref zone — invitation couple via lien mission (hypothesis : `CNE_ZMis_*` natif) |
| 515 | TerritoryQuery / 517 CanJoinTeam / 522 HateReport | 0 ref — queries côté client/mission ; HateReport probablement interne |
| 527 | EnterColosseum / 529 ColosseumTeamInfo / 587 ColosseumQuery | 0 ref — l'entrée colosseum passe par la file/join (528/530 émis, groupe A) |
| 540 | SetRoomResult | 0 ref — réponse au 210 RoomGetData assurée par 541 (implémenté) |
| 550 | ResetDailyMission | 0 ref — reset quotidien poussé ailleurs (mission) ou non implémenté natif dans ce build |
| 584 | CheckTeamTransport | 0 ref — transport d'équipe non exercé par ce build natif |
| 598 | OpenChurchDialog | 0 ref — la réservation d'église (C→S 250 orphelin) n'ouvre pas de dialog côté serveur dans ce build |

**Verdict famille B** : ce sont les équivalents « login/legacy » —
des classes générées dans le protocole sans site d'émission prouvé.
À traiter comme *paquets que le serveur n'a pas à émettre* jusqu'à
preuve du contraire (capture, autre binaire).

### 2.2 Monde — 25 absents : 12 émis par le natif + 13 jamais émis

| verdict | NCIDs (sites xref `world`) |
|---|---|
| **Émis natif → gaps** (12) | 36 ClientTextOutput(13 — annonces système), 46 UpdateFamilyMember(4), 47 FamilyOpException(3), 48 FamilyInvitation(3), 49 FamilyQuit(4), 51 FamilySchedule(6), 52 UpdateFriendMember(3), 53 FriendInvitation(3), 54 RepeatedLogin(4 — kick double connexion), 61 UpdateBlackList(3), 80 QueryFamilyRecruitList(3), 83 UpdateFortuneBagContentList(4) |
| **Jamais émis natif** (13) | 0 ClientForceKick, 16 ServerItemMallReceivableList, 42 ClientTell, 44 ClientChannelMsg, 55 ClientException, 67 FamilyAchievementUpdate, 68 FamilyLogUpdate, 69 ClientCaptcha, 70 ClientCrossZone, 74 FamilyEmblem, 77 SelectCharEnableBtn, 81 MutuallyHelpUpdate, 82 NotifyChangeWorld |

Familles monde :

- **Pushs sociaux famille/amis** (46-53, 61, 80, 67/68/74/81) : les
  pushs de mutations guilde/amis/blacklist/recrutement sont émis par
  le natif → **gaps** si l'on veut la parité (client Godot : les
  fenêtres famille/amis ne les attendent pas encore ; 48 et 54 sont
  déjà *parsés* côté client — `ZoneEntryDataMessages.cs:378-388` —
  mais jamais reçus). 67/68/74/81 ont 0 xref monde — leur push peut
  vivre côté mission/zone (`hypothesis`) ou être du legacy.
- **Annonces/texte monde** (36, 44, 55) : TextOutput natif = canal
  d'annonce (13 sites) → gap modéré ; ChannelMsg/Exception 0 ref →
  le chat monde passe par le canal zone 34/327 dans ce build →
  legacy.
- **Session/sécurité** (0 ForceKick, 42 Tell, 54 RepeatedLogin, 69
  Captcha, 70 CrossZone, 77 EnableBtn, 82 NotifyChangeWorld) :
  RepeatedLogin **est émis** par le natif (4 sites) → gap concret
  (double-connexion jamais signalée au client, alors que
  `ZoneEntryDataMessages.cs:385` le parse déjà). ForceKick/Captcha/
  CrossZone/EnableBtn/NotifyChangeWorld 0 ref → flows absents de ce
  build natif (captcha/anti-bot retiré ?) → ne pas implémenter.
  ClientTell 0 ref monde : le whisper natif passe probablement zone
  ou mission (`hypothesis`).
- **ItemMall** (16 ReceivableList, 83 FortuneBag) : 16 = 0 ref
  (artefact — la liste arrive par 57), 83 = émis → gap mineur.

## 3. Les handlers orphelins — dead code ou wire-id différent ?

### 3.1 Réconciliation du comptage (verified)

`protocol-map.md` annonce **135** (zone 121 + monde 14, `l.851-853`).
Trois faux positifs prouvés — le client **émet** bien :

| entrée map | preuve d'émission client |
|---|---|
| zone 2 WhoIs | `world/EntityRegistry.cs:1678` `SendZoneAsync(new ZoneWhoIs…)` |
| zone 5 MeleeAttack | `combat/AutoAttack.cs:100,150,165` + `GameScene.cs:2342,2389` |
| monde 15 ItemMallAnythingReceivable | `hud/ItemMallWindow.cs:731` `new WorldItemMallAnythingQuery()` (opcode 15, `ZoneItemMallMessages.cs:103-108`) |

→ **132 orphelins réels** (zone 119 + monde 13), cohérent avec le
comptage « real » du scan statique ultérieur (137 candidats dont 5
faux positifs côté zone — 0/1/2/5 — dont seuls 2 figuraient dans la
liste map ; la map ne liste pas zone 0/1 orphelins car ils sont dans
le switch d'entrée, pas dans la catégorie `⟂`). Ajouter les 2
orphelins **ticket** (0 GetTicket, 1 UseTicket — `TicketSession.cs:
112,129`, jamais appelés car les tickets arrivent inline depuis
login/monde).

### 3.2 Verdict global (verified)

**Aucun wire-id mismatch réel.** Chaque handler orphelin est lié à
son NCID natif correct (wire zone = NCID natif ; les renumérotations
monde/login sont déjà comptabilisées dans la map). Les orphelins se
répartissent en quatre causes :

| cause | volume | exemples |
|---|---|---|
| **Feature/UI cliente absente** — handler correct, le client Godot n'a pas l'écran ou le flow qui l'émet | ~105 zone + ~8 monde | housing 205-217, colosseum 196-201/219, fast-dress 244-248, famille-ville 161/173-176/180/239/240, recherche LFG 103/134/191/192/235-238, loterie/events 184/221-226/233/234/253-255, strengthen/inlay/cube/magicbox 119/120/67/232/257/230, vendor 71/78/80, safe-quit 27/32/33, shortcuts 54/55, licences 182/183, elf-stones 105/106/111/112, sorts procs 127/128, guild-ops 9 (monde), LFG monde 8/11, exploit rank 19 (monde), mutually-help 33 (monde), family emblem 73 (monde) |
| **Flow natif-only / session** — l'implémentation Godot déplace le flow ailleurs | ~8 zone + ~5 monde + 2 ticket | zone 153 Hello (le client attend 473 post-ticket), zone 58 LogoutReq (le client ferme TCP), zone 44 Resurrect (Reforged-only — Godot utilise 20 Revive), zone 2/5 (retirés), zone 59 SpellCastToNearby, 79 TeamOperation, 123 TurnSelf, 125 GetSelectLifeTarget, 126 PVPFlagModify, 163 NodeGroupReset, 216 StartParty, 218 NewPlayerGuide, 68 ReportProcessInfos, 89 BagDueDate ; monde 2 KeepAlive (Echo 57 zone suffit), 20/21 ChangeWorld(Ack) (choix de monde au login), 27 PlayerState (native 92), 28 IllPluginInfo (report anti-cheat), 29 UpdateWorldServers ; ticket 0/1 |
| **Ombre de dispatch** — un `case`/replay/silence passe avant | 9 partiels + 1 | les 9 de §1 + zone 20 (Revive : `case 20` :1145 gagne sur le stub `CombatHandlers.cs:164`) |
| **Écart volontaire / remap** | 2 | zone 172/204 dans la silent list alors qu'un `case` existe (entrées mortes) ; monde 26 `WorldSelectZone`→`CNE_CW_ServerCaptchaReturn` = remap assumé (émis — pas orphelin) |

Détail famille « feature absente » : ces handlers sont pour la
plupart **corrects et testables** — ils attendent l'UI correspondante
(housing, colosseum, LFG, fast-dress). Les supprimer serait une
perte ; les classer « dead code » est un abus — ce sont des
*implémentations en avance*. En revanche les orphelins « flow
natif-only » (Hello zone, KeepAlive monde, SafeQuit*) sont de la
vraie dead code pour ce client : le client Godot n'émettra jamais
ces paquets par design (son cycle de session diffère).

### 3.3 Chaînes orphelines cohérentes (verified)

Lecture croisée §2+§3 — les sous-systèmes dont **les deux sens** sont
absents forment des gaps complets à traiter ensemble :

- **Couple/mariage** : C→S 156 court-circuité + S→C 475-479 absents +
  598 jamais émis + 250 ChurchReserve orphelin → tout le système.
- **Colosseum/gladiator** : C→S 196-201/219 orphelins (handlers
  `ZoneEntryFlow.cs:738-753` écrits !) + S→C 526-530/552/587 absents.
- **Housing** : C→S 205-217 orphelins + S→C 540 jamais émis + 195
  replay zéro.
- **Loterie/pay-event/greedy island** : C→S 184/221-226/233/234/253-255
  orphelins + S→C 573/595/596/603/618/619 absents.
- **LFG/recherche** : C→S 103/104 (§1.4)/134/191/192/235-238 orphelins
  + S→C 411 « absent » mais déjà émis par le handler 103 (statut map
  stale).
- **Famille complète** : C→S 130-132/161/173-176/180/239/240 orphelins
  + S→C 485/486/505/561 émis par le natif mais absents.

## 4. Divergences notables à corriger dans une prochaine itération (verified)

1. **Map statuts « orphelin » stale** : zone 2, zone 5, monde 15
   émis par le client (§3.1) — 135 → 132 réels.
2. **411 SearchInfo** marqué « absent » alors que
   `MiscHandlers.cs:617-619` émet `NcidClientSearchInfo` — absent des
   faits (jamais déclenché) mais présent dans le code.
3. **104 SearchOpt** : double bug — interception (jamais exécuté) +
   polarité du handler mort (`!= 0` vs natif `== 0 → true`).
4. **ZoneEntityRegistry sans joueurs** : seul `RegisterPlayer`
   (`EntryHandlers.cs:74`) mort → gardes trade/dead-check silencieuses
   (§1.2).
5. **62 banque** : replay appliqué à toutes les requêtes, pas
   seulement l'entrée → contenu toujours vide à l'ouverture (§1.3).
6. **256 StarWay** : config live déjà stockée côté zone
   (`ZoneWorldLink.StarWayData`, poussée ZW 29) — seul le relais
   client manque ; le replay sert une config figée de 2018.
7. **310** : 11 champs patchés alignés exactement sur le parseur
   client — toute extension de parse lira des données test3 figées ;
   le payload natif complet (~90 champs, ctor @0x86721a0) reste la
   cible.
8. **Silent list** `ZoneEntryFlow.cs:1190` : entrées 172/204 mortes
   (cases existants), 104/156 à documenter comme « acquittement
   conforme au natif mais état jamais stocké ».

---
*Sources : `protocol-map.md` (inventaire), `ZoneEntryFlow*.cs`,
`EntryReplayData.cs`, `Eden.Zone/Handlers/*.cs`,
`Eden.World/Handlers/*.cs`, `PacketDispatcher*.cs`, clients
`protocol/*.cs` + `GameScene.cs`/`hud/` (émissions réelles), exports
`re/ghidra/zone|world/decompiled.c` via `reindex` (adresses et xrefs
cités inline). Aucune exécution ; statuts `verified`/`hypothesis`
par ligne.*
