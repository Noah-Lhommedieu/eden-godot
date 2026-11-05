# S2 — Recon serveur C# : guilde (Family) + ville de guilde (VDG)

Date : 2026-11-03. Portée : `eden_server/server/src/Eden.{World,Zone,Core,Mission}`
+ `eden_server/Database`. Guilde = **Family** (CFamily), VDG = **Family Town /
hall** (nodes 501/502, type 4). Statuts : [VERIFIED] = code présent et
branché, [HYPOTHESIS] = approximation documentée, [GAP] = absent.

Sources croisées : `docs/knowledge/protocol-map.md` (world ~l.95-190,
zone ~l.240-440 et ~l.510-780), `eden_server/docs/generated/FEATURE-MATRIX.md`,
désasm cités en commentaires `fichier:ligne`.

## 1. Matrice NCID → handler → état

### 1a. World (:5200) — client → monde

| NCID | Nom natif | Handler | État |
|---:|---|---|---|
| 9 | `CNE_CW_ServerFamilyOp` [u8 op][i32][i32][str][str] | `Eden.World/Handlers/FamilyHandlers.cs:51` (switch :75-99) | [VERIFIED] 23 sous-ops : invite 0x01, cancel 0x02, accept 0x03, reject 0x04, quit 0x05, kick 0x06, leader 0x07, disband 0x08, editinfo 0x0a, cadre title 0x0b, setcadre 0x0c, priv 0x0e, msgboard 0x0f, delmsg 0x11, readmsg 0x12, comment 0x13, tax 0x14/0x15, level 0x16, emblem blob 0x46, recruit 0x4e/0x4f/0x50. Ops natives 0x09/0x0d/0x10 non mappées [GAP] |
| 10 | `CNE_CW_ServerQueryFamilyInfo` [u8 flag] | `FamilyHandlers.cs:53` → `HandleQueryAsync` :522 | [VERIFIED] : 50 type 0 + chunks type 8 ×30 (`Family.MemberChunkSize` FamilyModels.cs:19) + 51 + 68 (type 1) + prestige 60 |
| 73 | `CNE_CW_ServerQueryFamilyEmblem` [i32 fam][i32] | `FamilyHandlers.cs:55-61` | [VERIFIED] → 74 blob `family_emblem` |

### 1b. World — monde → client (codecs `Eden.Core/Game/Family/FamilyPackets.cs`)

| NCID | Nom | Émetteur | État |
|---:|---|---|---|
| 46 | `ClientUpdateFamilyMember` | `FamilyPackets.UpdateFamilyMember` :115 | [VERIFIED] (join/quit/kick/cadre/comment) — protocol-map « absent » = index classes générées seulement |
| 47 | `ClientFamilyOpException` [u8 op] | `FamilyOpException` :63 | [VERIFIED] |
| 48 | `ClientFamilyInvitation` | :69 | [VERIFIED] |
| 49 | `ClientFamilyQuit` (vide) | :59 | [VERIFIED] |
| 50 | `ClientQueryFamilyInfo` | `QueryFamilyInfo` :136 | [VERIFIED] (types 0,3,4,5,6,7,8 + type 2 « absente ») |
| 51 | `ClientFamilySchedule` | `FamilySchedule` :246 | [VERIFIED] trame ; contenu toujours vide — rien n'écrit `family_schedule` [GAP] |
| 60 | `ClientUpdatePrestigeData` | `PrestigePackets.UpdatePrestigeData` :511 via `FamilyHandlers.cs:566` | [VERIFIED] + relais zone `update_prestige` |
| 67 | `ClientFamilyAchievementUpdate` | — | [GAP] aucun émetteur (achievements famille non implémentés) |
| 68 | `ClientFamilyLogUpdate` | `FamilyLogUpdate` :208 | [VERIFIED] (type 1 = logs membres uniquement) |
| 74 | `ClientFamilyEmblem` | `FamilyEmblem` :232 | [VERIFIED] |
| 80 | `ClientQueryFamilyRecruitList` | `QueryFamilyRecruitList` :277 | [VERIFIED] (20/page, recalage natif) |

### 1c. Lien Z↔W (`Eden.World/ZoneLinkServer.cs` / `Eden.Zone/Links/ZoneWorldLink.cs`)

| Sens | NCID (compact) | Rôle | État |
|---|---:|---|---|
| Z→W | 34 (natif 143) `FamilyCreation` [i32 charId][str name] | `ZoneLinkServer.cs:557` `HandleFamilyCreationAsync` → `FamilyService.CreateFamilyAsync` (FamilyService.cs:119) | [VERIFIED] : refuse -1 nom pris / -2 autre |
| W→Z | 31/32/33 (natifs 154-156) Change/New/RemoveFamily | zone `ZoneEntryFlow.Family.cs:25-122` | [VERIFIED] |
| W→Z | 35 FamilyDataUpdate [i32 fam][s16 house_open] | `Family.cs:113` | [VERIFIED] |
| Z→W | texte `family_instance_create` / `set_family_emblem` / `gm_family_setup` / `family_set_level` | `ZoneLinkServer.cs:599-750` | [VERIFIED] |

### 1d. Zone (:5201) — client → zone

| NCID | Nom natif | Handler | État |
|---:|---|---|---|
| 49 | `ZoneServerFamilyCreate` [str nom] | `SocialHandlers.cs:284` → ZW 34 | [VERIFIED] (cf. §2) |
| 81 | `ZoneServerUpdateFamily` [u32 cible] | `FamilyHandlers.cs:252` → 346 | [VERIFIED] |
| 82 | `ZoneServerSetFamilyEmblem` | `SocialHandlers.cs:319` (chef) → texte monde | [VERIFIED] |
| 130/131 | `FamilyContainerPutItem/GetItem` | boucle `ZoneEntryFlow.cs:714/719` → `FamilyInstance.cs:649/796` | [VERIFIED] (op dialogue 0x3b, kinds, caps, 484 refresh) |
| 132 | `FamilyInstanceGetData` | `ZoneEntryFlow.cs:696` → `FamilyInstance.cs:115` → 487 | [VERIFIED] |
| 159 | `ZoneServerFamilyDonateMoney` [i32] | `SocialHandlers.cs:297` | [VERIFIED] (0x388 or, 0x370 plafond 999 999 999) |
| 161 | `ZoneServerFamilyStatue` | `ZoneEntryFlow.cs:732` → `FamilyInstance.cs:894` | [VERIFIED] (cf. §6) |
| 173 | `FamilyInstanceCreate` [s16 type][u8 vip] | `ZoneEntryFlow.cs:692` → `FamilyInstance.cs:52` → texte monde | [VERIFIED] |
| 174 | `FamilyTownQuery` | `ZoneEntryFlow.cs:704` → `FamilyInstance.cs:255` → 502 | [VERIFIED] |
| 175 | `FamilyTownMemberQuery` [i32 fam] | `ZoneEntryFlow.cs:709` → `FamilyInstance.cs:376` → 503 | [VERIFIED] |
| 176 | `FamilyTownEnter` [i32 famId] | `ZoneEntryFlow.cs:700` → `FamilyInstance.cs:149` | [VERIFIED] |
| 177/178 | `FamilyStoragePutItem/GetItem` | `InventoryHandlers.cs:486/582` | [VERIFIED] |
| 179 | `FamilyStorageQueryLog` | `FamilyHandlers.cs:186` → 506 | [VERIFIED] |
| 180 | `FamilyBuildingRepair` [s16 loc][u8 all] | `FamilyHandlers.cs:74` | [VERIFIED] (priv 10, coût durabilité, trésorerie → 487) |
| 189/190 | `TerritoryQuery/Join` | `ZoneEntryFlow.cs:737/741` → `Territory.cs:41/60` | [VERIFIED] |
| 225 | `FamilyBattleAskInfo` [s16 type] | `ZoneEntryFlow.cs:745` → `Territory.cs:201` | [VERIFIED] (types 0/1/2) |
| 239 | `FamilyTalkToNpc` [u32 npcId] | `FamilyHandlers.cs:204` → mission ListDialogue | [VERIFIED] |
| 240 | `FixEquipByFamily` | `FamilyHandlers.cs:218` | [VERIFIED] partiel : aucun check privilège `fixequip` (idx 7) [GAP] |

### 1e. Zone — zone → client

| NCID | Nom | Émetteur | État |
|---:|---|---|---|
| 346 | `ClientChangeFamily` | `FamilyHandlers.cs:259`, `ZoneEntryFlow.Family.cs:127` | [VERIFIED] (u8 révision char+0x4de) |
| 347/483/501/507/514 | Enter* (dialogues PNJ) | `ZoneEntryFlow.Dialogue.cs:275/308/368/342/375` (services 5/11/19/21/25) | [VERIFIED] |
| 384 | `ClientSelectFamilyEmblem` | `ZoneEntryFlow.Mission.Commands.cs:217` (cmd mission `select_family_emblem`) | [VERIFIED] |
| 484 | `ClientFamilyContainerData` | `FamilyPackets.FamilyContainerData` :425 | [VERIFIED] |
| 485/486 | `ClientFamilyContainerDelete/PutItem` | — | [GAP] deltas jamais émis — remplacés par réémission du 484 complet (commentaire `FamilyInstance.cs:772-777`) [HYPOTHESIS] |
| 487 | `ClientFamilyInstanceData` | `FamilyPackets.FamilyInstanceData` :471 | [VERIFIED] |
| 502/503 | `ClientFamilyTownQuery/MemberQuery` | `FamilyPackets` :327/:391 | [VERIFIED] (17/6 vecteurs parallèles) |
| 504/505/506/508 | `ClientFamilyStorage*` | `Codecs/FamilyStoragePackets.cs` | [VERIFIED] (505/515/561 marqués « absent » dans protocol-map mais émis via `SendPacketAsync` — entrées périmées) |
| 515 | `ClientTerritoryQuery` | `FamilyBattlePackets.TerritoryQuery` :102 | [VERIFIED] |
| 516/532/547/562/565/566 | Territoire/battle | `Territory.War.cs`, `ZoneEntryFlow.Family.cs:152` | [VERIFIED] |
| 561 | `ClientFamilyBattleInfo` | `FamilyBattlePackets` types 0/1 (:39/:56) | [VERIFIED] types 0/1 ; type 3 camps :68 écrit, jamais émis [GAP] |

## 2. Création de guilde

Fil : zone 49 → ZW 34 → monde (`ZoneLinkServer.cs:557`). Gates monde :
perso existant, pas déjà en famille, nom unique (case-insensitive)
→ `FamilyService.CreateFamilyAsync` (FamilyService.cs:119 : INSERT family
lv=1, leader membre). Réponse W→Z 32 → `ZoneEntryFlow.Family.cs:50` :
**coût 50 000 or débité côté zone** (`DebitFamilyCreationGoldAsync` :177),
346 au client, 565 à l'équipe. Erreur -1 → texte 0x913. [VERIFIED]
UI : service PNJ 5 → 347 `EnterFamilyCreation` (Dialogue.cs:273).

## 3. Membres, droits/privilèges

- Membres : `player_characters.family_id` + rangs `family_cadre_members`
  (0-7). Jointure via ops monde 3 (accept) / 0x50 (recruit join) :
  `FamilyHandlers.cs:164-199,496-512`.
- Titres : 9 (`Family.TitleCount`), privilèges : 14 colonnes
  `*_privilege` (ordre : channel, invite, kickout, editinfo, setcadre,
  invite25r, fixequip, msgboard, delmsg, treasury, repair, storage01-03 —
  `FamilyRepository.cs:254`).
- **Deux modèles de privilège coexistent** :
  - monde `FamilyService.HasPrivilege` (FamilyService.cs:75) : `rank >=
    priv[index-1]` (comparaison de valeur).
  - zone `GuildService.CheckPrivilege` (GuildService.cs:421) : masque de
    bits rank→bit (1→1 … 9→0x100, membre→0x80) + **barrières de niveau**
    (priv 9-11 → fam lv≥3, 12 → lv≥5, 13 → lv≥6) — fidèle au
    `CheckPrivilige` mission natif. [VERIFIED]
- Kick : priv 3 + rang cible < rang acteur (sauf chef) :262. Quit : le
  chef ne peut pas (47/op5) :229. Disband : chef, `delete_time` + sortie
  de tous :295.

## 4. Trésorerie, donations, niveaux

- Don 159 (`SocialHandlers.cs:297` + `GuildService.CanDonate` :343) :
  or≥amount→0x388, `treasury+amount ≤ 999 999 999`→0x370 (cap
  `MaxTreasuryValue` GuildService.cs:336), débit or + UPDATE
  `family.treasury`. La colonne `family_contribution_point` existe mais
  n'est PAS créditée par le don [GAP? natif à vérifier].
- Taxes : `tax_rate`/`business_tax` (ops 0x14/0x15, priv 10) ; prélèvement
  `FamilyLevyTaxes` = amount/10 → trésorerie (GuildService.cs:373).
- Niveau : op 0x16 `RefreshLevelAsync` = `1 + exp/1000` clamp 1-10
  (FamilyHandlers.cs:443 — commentaire « palier simple » [HYPOTHESIS]) ;
  `GuildService.AddExperience` :296 = formule placeholder `lv*10 000`
  [HYPOTHESIS] ; mission `RewardFamilyExp` → `MissionDb.AddFamilyExpAsync`
  (MissionDb.cs:481). Deux formules distinctes [GAP à trancher].
- Prestige : `prestige` → NCID 60 + `update_prestige` zone
  (FamilyHandlers.cs:566-580, PrestigeRepository).

## 5. Storage de guilde (coffres)

- 3 coffres ×40 slots (`FamilyBankRepository.StorageCount/StorageSlotCapacity` :66-70), table `family_storage` (schéma items complet).
- Put 177 / Get 178 (`InventoryHandlers.cs:486/582`) : priv `index+11` avec
  niveaux fam (coffre 0→lv3, 1→lv5, 2→lv6), flags restriction item
  (0x20a3/0x7eb), loc -1 → premier libre, **transaction unique**
  `MoveItemToFamilyStorageAsync`/`MoveFamilyStorageItemToInventoryAsync`
  (SocialRepositories.cs:840/948 — DELETE+INSERT atomique). Push 505/504
  aux membres en ligne, log `family_storage_log` types 0/1 → 506.
- UI : service 19 → 507 + 508 par coffre non vide (Dialogue.cs:337-356).
- Log query 179 → 506 (50 dernières, `FamilyBankRepository.cs:293`).

## 6. Ville de guilde (VDG) : achat / entrée / instance

- **Achat** : service PNJ 21 → 501 ; envoi 173 → gates zone
  `FamilyInstance.cs:52-107` : type∈{0,1}, rate-limit 500 ms, lv≥30
  →0x599, chef→0x598, `house_open==0`→0x91a, fam lv≥3→0x59a, trésorerie
  ≥500 k (vip=0) / 10 M (vip=1)→0x6ae → texte ZW
  `family_instance_create` → monde `FamilyService.InstanceCreateAsync`
  (FamilyService.cs:212) rejouant les gates mission
  (`InstanceCreateGate` :192 : + villes>49→0x59c, <30 membres→0x59b,
  recyclée→0xa4b) → `house_open`=501 (type 1) / 502 (type 0), débit
  trésorerie, log type 2, push ZW 35 + fiche 50 aux membres, erreurs via
  NCID 43 monde. [VERIFIED]
- **Entrée** : 176 (`FamilyInstance.cs:149`) : newer-towns 94/768→0x207f,
  sortie (-1) → node `CrossZoneNode`, team≥11→0x1430, versus→0xd1f,
  mort→0xb80, node type∉{1,3,4}→0xb89, guilde inconnue→266 « Can't find
  family », `house_open==0` silence → `VisitedFamilyInstanceId` +
  `MigrateToNodeAsync` vers le hall. Persistance `visit_family_id` +
  `cross_zone_*` (migration 010 ; CharacterRepository.cs:353-356,945-959 ;
  Lifecycle.cs:108). **Instance** : `ZoneEntryFlow.Instancing.cs:34`
  (InstanceGroup = familyId visité). [VERIFIED]
- **Recherche** : 174 → 502 (candidats `house_open≠0`, filtres niveau/
  nom NPC/bâtiment, chunks ×10) ; 175 → 503 (membres, quit==1 et
  SearchVisible=0 exclus, rang chef=1). [VERIFIED]
- Cache : `GuildService` zone resync `RefreshHouseOpensAsync` :120
  (répare les UPDATE directs — bug live 2026-10-26 documenté).

## 7. Bâtiments et conteneurs de hall

- Table `family_building_container` (migration 009) ; items :
  bâtiments kind 0x29 sur bases de groupe (locs 0/10/20/30/40),
  NPC/déco 0x2a/0x52, item sacré kind 0x16 @ loc 0x33.
- Put 130 / Get 131 : op dialogue 0x3b ouverte par service 11
  (Dialogue.cs:299), règles `FamilyHallRules` (loc/groupes, delete
  0x535, cap NPC/level 0x8ef, annexes 40 →0xa01/0xa02) → DELETE+INSERT +
  484 aux membres du hall. [VERIFIED]
- Repair 180 : priv 10 (treasury→900), repairAll = production types 4-9,
  coût `(max−cur)×RepairCostPerPoint` du template S_Buliding, textes
  0x8f0/0x8f1/0x8f2 → durabilité max + 487. [VERIFIED]
- [GAP] pas de consommation/décroissance de durabilité dans le temps,
  pas d'exp/upgrade de bâtiment hors GM `building_level`
  (`SetFamilyBuildingLevelAsync` repo utilisé GM-only), `due_date`
  chargée mais aucune expiration des contrats NPC.

## 8. Statues

- 161 (`FamilyInstance.cs:894`) : chef seul→0x382, dans SON hall→0x38e ;
  pos 0 = retrait par nom (0x38f « %s » absent, 0x38b ok) ; pos 1-4 =
  pose : fam lv≥4 (≤2 statues si lv<6), cible en ligne→0x38e, 500 k
  trésorerie→0x38c → `statues_scenes` + `InjectHallStatue` (handle
  0x88F0xxxx, CSV BornStatue `BuildStatueCsv` FamilyHallRules.cs:222,
  templates 0xd675-0xd678) + 0xb4e famille + 0x38a demandeur. [VERIFIED]
- Spawn à l'entrée : `SpawnFamilyHallNpcsAsync` (FamilyInstance.cs:459,
  déclenché Sight.cs:208) — housekeeper (chaîne bâtiment-maison tid 1/51
  ≤ lv famille), NPCs du conteneur 0, statues nommées.

## 9. Bataille de familles / territoires

- 189→515 (territoires + fenêtres schedules), 190→join (type 0 guerre /
  1 battle), 225→561 : type 0 = classement `familybattle_ranks`,
  type 1 = 515 + phase, type 2 = join. Camps battle : familles inscrites
  requises ; **inscription dérivée au boot de `familybattle_ranks`**
  (`Program.cs:724-736`, `TerritoryWarService.RegisterFamily` :444)
  [HYPOTHESIS — pas de flux d'inscription live].
- Cycle : 516 phases, 532 kill-info, close = 566 scores + 516 t2 +
  562 ranks + 547 + rapatriement + `territory_war` persisté
  (Territory.War.cs:113-159). Cap FB 50 joueurs (`FBCharMaxNum`).
- [GAP] 561 type 3 (camps) codec prêt, jamais émis ; croisade =
  relais DBA `CrusadeKill` (War.cs:245).

## 10. NPCs de ville, emblème, divers

- NPCs ville : spawnés dans la vue du visiteur (§8) ; dialogue 239 →
  mission `SendListDialogueAsync`. [VERIFIED]
- Emblème : index (82→texte monde→`family.emblem` + 346) ; blob custom
  (op 0x46 → `family_emblem`, stocké **base64 en colonne text** —
  FamilyRepository.cs:358 [HYPOTHESIS stockage]) ; requête 73→74 ;
  dialogue `select_family_emblem` → 384.
- Achievements famille : [GAP] — seul le classement
  `family_achievement_rank` est lu (mission `RankInfoService.cs:185`,
  rank type 200) ; aucune progression ni émission 67.
- Agenda `family_schedule` chargé, jamais écrit [GAP] ; `recruit_message`
  repo présent, aucune op ne l'écrit [GAP].
- GM (`GmGuildHandlers.cs`) : `create_guild`/`create_family`,
  `guild_level`/`set_family_level`, `open_hall`, `building_level`,
  `disband_family`, `give_building`, `place_npc`, `replace_npc`,
  `gm_family_setup`. [VERIFIED outillage]

## 11. Tables DB

Natives utilisées : `family` (43 cols, FamilyRepository.cs:25),
`player_characters` (family_id, add_family_time, family_comment,
family_contribution_point, family_msgboard_state, quit, visit_family_id,
cross_zone_*), `family_cadre_members`, `family_msgboard`,
`family_schedule`, `family_log`, `family_emblem`, `family_storage` +
`family_storage_log`, `family_building_container` (migr. 009),
`statues_scenes`, `territory_war`, `familybattle_ranks`, `prestige`,
`family_achievement_rank`. Mortes/archives : `family_bank`, `guilds`,
`guild_members` (migr. 016 — créées par 001 mais jamais utilisées par le
code actuel).

## 12. Tests (`eden_server/server/tests/Eden.Tests/`)

FamilyPacketsTests (25), FamilyHallRulesTests (13), FamilyHandlerTests (8),
FamilyStorageTests (6), FamilyTownEnterTests (5), FamilyTownQueryTests (8),
TerritoryPacketsTests, GuildMailTests (6) + goldens byte-à-byte.
[Lacune] aucun test statue/battle-join/instance-create gates monde
(`InstanceCreateGate` pur et testable mais non testé repéré).

## 13. GAPs consolidés

1. **Achievements famille** : aucun flux (67 jamais émis, pas de
   progression, `family_achievement_rank` lecture seule).
2. **Agenda/schedule** : `family_schedule` jamais écrite ; 51 toujours vide.
3. **Ops monde 0x09/0x0d/0x10** du switch natif non implémentées ;
   `recruit_message` sans op d'écriture.
4. **Vie des bâtiments** : pas de décroissance durabilité, pas
   d'upgrade/exp en jeu (GM-only), `due_date` des contrats NPC inerte.
5. **Inscription family battle** : camps dérivés de
   `familybattle_ranks` au boot [HYPOTHESIS] ; 561 type 3 jamais émis.
6. **240 FixEquipByFamily** sans check privilège fixequip (idx 7) ;
   don 159 ne crédite pas `family_contribution_point` (à confirmer natif).
7. **Niveau famille** : deux formules placeholders distinctes
   (monde exp/1000 vs zone lv×10 000) — à unifier sur preuve native.
8. **Deltas conteneur 485/486** non émis (repli 484 complet documenté) —
   wire différent du natif.
9. Protocol-map : 46-51/68/74/80/505/515/561 listés « absent » alors
   qu'émis via codecs dédiés/`SendPacketAsync` — entrées à corriger.
