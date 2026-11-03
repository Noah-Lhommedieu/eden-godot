# Audit de drift de schéma — parseurs `c_*`/`C_*` FR vs oracles `S_*` US

Statut : **verified** — 2026-11-03 — audit lecture seule : headers et lignes
relus directement dans `corpus/fr-full/data/db/` (Big5) et
`eden_server/server_us/Data/db_decoded/`, rejoués avec la sémantique exacte
de `IniTable` (record complet à `N+1` cellules, le `|` final laisse une
cellule vide).

## 1. Qui lit quoi

### Serveur (`eden_server/server/`)

- **Base** : `DATA_DIR`/`EDEN_DATA` → `server_us/Data/db_decoded` (oracle US,
  `start-native.sh:35`, `docker-compose.yml:191`). Toutes les tables `S_*`,
  `T_*`, `Motion.ini`, `EquipType.ini`, `4PHouse.ini`, `balance.ini`,
  `limitless_pass.ini` chargées par `GameDataService.LoadAll`
  (`GameDataService.cs:257-464`) et les `*Service.LoadFromDataDir` sont
  **US → alignées par construction**.
- **Overlay client** : `EDEN_REFORGED_DB` → `corpus/fr-full/data/db`
  (`start-native.sh:39`, passé aux trois services :80/:87/:95) via
  `ReforgedDataLocator` (`FindFile` insensible à la casse). **Seuls 5
  fichiers FR sont lus par le serveur** :

  | Fichier FR | Parseur | Point d'appel |
  |---|---|---|
  | `c_enchant.ini` | `EnchantParser.Parse` | `EnchantService.ApplyClientOverlay` (EnchantService.cs:115) + `GameDataService.MergeFrMounts` (GameDataService.cs:753) |
  | `c_itemmallenchant.ini` | `EnchantParser.Parse` | idem (EnchantService.cs:115, GameDataService.cs:748) |
  | `c_item.ini` | `ItemParser.Parse` | `MergeFrMounts` (GameDataService.cs:771) |
  | `c_itemmall.ini` | `ItemParser.Parse` | idem (GameDataService.cs:767) |
  | `c_talent.ini` | `GameDataParsers.ParseTalents` | `TalentService.LoadFromDataDir` (TalentService.cs:80-82, `FindFile("C_Talent.ini")` matche la casse `c_talent`) |

  - `S_ItemExtra.ini`, `S_ItemComboExtra.ini`, `S_ItemChestExtra.ini`
    (GameDataService.cs:274/308, Program.cs:108) : **absents de fr-full** →
    no-op.
  - Login/Gateway/World ne chargent aucune table (manifest SHA-256
    seulement). Mission = même `GameDataService.LoadAll` que la zone.

### Client (`src/Eden.Core`, compilé dans `godot_project/scripts/`)

Lit les `c_*`/`C_*`/`t_*` de `corpus/fr-full` via `EdenVfs`/`TextResolver`
(résolution insensible à la casse — `C_Transport.ini` → `c_transport.ini`).
`godot_project/scripts/data/tables/*.cs` sont les copies live ; les fichiers
de `src/Eden.Core/Data/tables/` sont quasi identiques (seuls
`EnchantTable.cs` et `ItemTable.cs` divergent — voir §2).

## 2. Table de drift (demande : `fichier | schéma_FR | schéma_US | parseur (fichier:ligne) | colonnes_lues | drift_réel ?`)

| fichier_table | schéma_FR | schéma_US | parseur (fichier:ligne) | colonnes_lues | drift_réel ? |
|---|---|---|---|---|---|
| `c_enchant` | `\|V.2\|60\|` | `\|V.3\|61\|` (US insère col6) | serveur `EnchantParser.cs:84-118` ; client `tables/EnchantTable.cs:70-86` | 1,3,4,5,6,7,8,9-12,16-20,23,24,37-43,56,59 | **OUI — tous champs ≥6 décalés de −1 ; le correctif `row.Length==60` est mort (rows FR = 61 cellules)** |
| `c_itemmallenchant` | `\|V.2\|60\|` | `\|V.3\|61\|` (US insère col6) | mêmes parseurs | idem | **OUI — idem** |
| `c_achievement` | `\|V.3\|13\|` | `\|V.4\|15\|` (US ajoute cols 11-12 ; membres FR@11→US@13, desc FR@12→US@14) | client `tables/AchievementTable.cs:75-90` ; serveur `GameDataParsers.cs:227-251` | 1-11,13,14 | **OUI côté client — Description et TargetIds toujours vides** (serveur OK sur S_) |
| `c_collection` | `\|V.3\|8\|` | `\|V.4\|9\|` (US ajoute col8 en fin) | client `tables/CollectionTable.cs:63-69` ; serveur `CollectionParser.cs:49-52` | 1,2,3,4,5,6,7,8 | **OUI côté client — `Group`=0 pour toutes les cartes** (col8 = cellule trailing vide ; serveur OK sur S_) |
| `c_transport` | `\|V.9\|57\|` | `\|V.10\|59\|` (US ajoute 2 cols en fin) | client `tables/StoreTable.cs:148-153` ; serveur `WorldParsers.cs:201-204` | client : scan heuristique cols 3+ ; serveur : 1,4-16 | non — positions lues alignées |
| `c_spell` | `\|V.7\|44\|` | `\|V.8\|45\|` (US insère une col à index 34) | client `tables/SpellTable.cs:61-63` + `ClassSkillTree.cs:79-88` ; serveur `SpellParser.cs:216-265` | client : 0,1,18,28 et 17,22,23,41,42 ; serveur : 33-43 | non — client : 0-28 alignés, 41/42 déjà FR-alignés ; serveur lit S_ seulement (**lirait mal FR ≥34** si pointé dessus) |
| `c_classdropitem` | `\|V.2\|81\|` | `\|V.3\|85\|` (US +4 cols ≈ slot de drop vers col 45-64) | serveur `GameDataParsers.cs:68-78` ; client : aucun | scan heuristique | non — scanner indifférent aux insertions |
| `C_Elf` | `V.5,36,` | `V.7,42,` (US ajoute cols 36-41 en fin) | serveur `ElfParser.cs` (S_Elf seul) | 1-6,18,20,29,34 | non — ≤34 alignées ; client : aucun lecteur |
| `C_ElfCollect` | `V.4,37,` | `V.5,25,` (incompatibles : nom FR@35 vs US@23) | aucun | — | n/a — jamais lue |
| `C_ElfCombine` | `V.4,36,` | `V.5,23,` (FR = US + padding vide en queue) | serveur `CraftRecipeParser.cs:123-142` | 2,6,7,9,12,14,18,19-22 | non — positions alignées, scripts absents des deux côtés |
| `C_ElfCombineElf` | `V.3,51,` | `V.4,53,` (US insère 2 cols avant index 14) | aucun | — | n/a — jamais lue |
| `c_item` / `c_itemmall` | `\|V.11\|78\|` | `\|V.11\|78\|` | `ItemParser` (S_ + FR via MergeFrMounts), `ItemTable` | ≤76 | non — même schéma (valeurs divergent = donnée, pas schéma) |
| `c_talent` | `\|V.2\|10\|` | `\|V.2\|10\|` | `ParseTalents` (S_ + FR via TalentService), `TalentTable` | 0-9 | non — même schéma ; row fantôme vide #735 skippée par `TryParse(row[0])` |
| `c_equipset` | `\|V.1\|18\|` | `\|V.1\|18\|` | `EquipSetParser` (S_), `EquipSetTable` (FR) | 3-18, 14+i | non — même schéma ; row fantôme #380 skippée |
| `c_dropitem` | `\|V.3\|176\|` | `\|V.3\|176\|` | `DropAndSpeedParsers` (S_), `ItemSources.Drops` (FR) | 1,3-6,11-90,91,96-175 | non — même schéma ; l'anomalie 24230 (176 cellules) existe **à l'identique dans S_DropItem US** |
| autres `c_*`/`C_*` lues | identiques | identiques | — | — | non — 60+ tables à schéma identique (`c_mission` 28, `c_monster` 62, `c_npc` 21, `c_store` 93, `c_class` 65, `c_partner` 30, `c_battlefield` 59, `c_familycollect` 47, `c_familycombine` 44, `c_level` 27, `c_equiptrain` 17, `c_itemchest` 32, `c_itemcombo` 42, `c_lifeskill` 36, `c_paysystem` 18, `c_rewardscore` 87, `c_recommendactivity` 22, `c_spellreference` 50, `c_title` 6, `c_charcolor` 5, `C_Races` 4, `C_Classes` 5, `C_Lottery` 9, `C_LuckyBag` 92, `c_destiny*` 42/8…) |

## 3. Détail des drifts réels

### 3.1 `c_enchant` / `c_itemmallenchant` — correctif présent mais MORT ⚠

US `|V.3|61|` = FR `|V.2|60|` + **une colonne insérée à l'index 6**
(groupe elf-pet, 102 lignes US non vides — ex. 61293 col6=999, absentes de
FR). Tout champ `i≥6` FR contient ce que l'US appelle `i+1`.

Le garde-fou existe déjà des deux côtés mais **ne s'applique jamais** :

- serveur `EnchantParser.cs:84` : `if (row.Length == 60)` → réinsère un vide
  à l'index 6 ;
- client `godot_project/scripts/data/tables/EnchantTable.cs:75` :
  `var s = row.Length == 60 ? -1 : 0` ;
- `src/Eden.Core/Data/tables/EnchantTable.cs` : **aucun garde-fou**.

Or `IniTable.BuildRows` retourne `joined.Split('|')` sans retirer la
cellule vide du `|` terminal : une ligne FR de 60 colonnes produit **61
cellules**. Mesuré : c_enchant = 18486 lignes à 61 cellules + **1 seule** à
60 (id 46131 — anomalie propre), c_itemmallenchant = 1399/1399 à 61. Le fix
est donc appliqué à la seule ligne qui n'en avait pas besoin.

Effet sur `EnchantService.ApplyClientOverlay` (~18 486 defs FR **remplacent**
les defs US correctes) — tous les champs positionnels ≥6 sont décalés :

- `Enabled` lit FR col7 (flag2 US) au lieu de FR col6 (flag1) ;
- `TriggerType`(8), `TriggerParams`(9-12), `SecondaryBlock`(16-20),
  `SpellId1/2`(23/24), `ProcChance`(37), `AppliedSpellId/Chance`(38/39),
  `TriggerSlotsRaw`(40), `TriggerEvent/Chance/Duration`(41/42/43),
  `Description`(56) : décalés de −1 → `Evaluate`/`GetProc`/`GetUseCommands`/
  `GetAttackCommands` décodent des args comme ids de commande ;
- `_elfEnchantGroups` (ctor EnchantService.cs:30-33, `Raw[6]`) : clés par
  flag1 → groupe « 1 » pollué par ~18k ids ;
- `CanCancelByClient`/`IsDebuff`/`IsUndispellable`/`BlocksReapply`
  (cols 7/8), `GetTypeId`/`GetGrade` (38/39), `GetMaxStack` (59→US 60) :
  mal lus ;
- **intacts** : `Id`, `GroupCode`(1), `SpellRef`(3), `Level`(4), `Name`(5),
  et les **scanners bruts** : `Raw.Contains("2134")` (MergeFrMounts:759),
  `GetRideInfo` (scan 2134/2051/2140 — c'est pourquoi les montures marchent).

La doc `EnchantService.cs:93-94` (« Schéma identique (mêmes 61 colonnes) —
vérifié octet à octet ») était vraie pour `reforged/EdenReforged/data/db`
(61 cols) ; **fausse pour fr-full** pointé par `EDEN_REFORGED_DB` actuel.

Client : `IsBuff`(col7), `NoAbandon`/`NoReapply`(col8), `PetGroup`(col6),
`MaxStack`(col59→US60, quasi vide→1), `Description`(col56→US57) mal lus ;
`RideKeyOf` intact (scan). Impact live à vérifier (tooltips, annulation de
buff, stacks).

### 3.2 `c_achievement` — US +2 colonnes à 11-12

FR col11 = liste membres `id;id;…`, col12 = description ; US col11/12 =
paire prestige (ex. 85/10), col13 = membres, col14 = description.

- Client `AchievementTable.cs:84-90` : `ItemReward`(11) lit les membres → 0
  ; `TargetIds`(13) et `Description`(14) hors bornes → **toujours vides** —
  la fenêtre de succès perd descriptions et sous-succès des groupes
  (légendaires 5001-5092, `AchievementWindow`). `TitleCol9`(9),
  `ItemCol10`(10) restent corrects.
- Serveur `ParseAchievements` (GameDataParsers.cs:241-247) : mêmes indices
  US sur S_Achievement → **correct en prod**, identiquement cassé si pointé
  sur FR.

### 3.3 `c_collection` — US +1 colonne en fin

`CollectionTable.cs:69` lit `Group` en col8 ; la row FR n'a que 9 cellules
(8 cols + trailing vide) → `Group=0` pour les ~3 000 cartes →
`GroupPoints`/`ArchiveWindow` agrègent tout en groupe 0 (les compteurs
1000/1001 survivent via col2). Serveur `CollectionParser` correct sur S_.

## 4. Anomalies de données (tolérées ou hors portée)

| Table | Anomalie | Traitement actuel |
|---|---|---|
| `c_dropitem` | row 24230 : 176 cellules (séparateur mangé dans un nom Big5 — trail byte 0x7C) — **identique dans S_DropItem US** | `DropAndSpeedParsers.ParsePool` a une resynchro heuristique ; `ItemSources.Drops` borne `i+3 < row.Length` → toléré des deux côtés |
| `c_activity` | row 118 : 17 cellules (16 attendues) | aucun lecteur des deux côtés |
| `c_talent` | row #735 fantôme (11 cellules vides, suite de la ligne 4613) | `TryParse(row[0])` échoue → skippée |
| `c_equipset` | row #380 fantôme vide | idem |
| `c_growequip` | aucune row non-numérique détectée au parse IniTable ; pas de lecteur | n/a |
| `C_Mask` | header `V1.0,14,` : ne matche ni `|V.` ni `V.` (c'est `V1.`) ni le format Count → `IniTable` = 0 rows — **même défaut dans S_Mask US** | aucun lecteur ; documenté comme inparseable |

## 5. Tables attendues mais absentes de fr-full (trou, pas drift)

| Fichier | Consommateur | Effet |
|---|---|---|
| `c_collectionpoint.ini` | `CollectionTable.Awards()` (CollectionTable.cs:87) | récompenses de points vides côté client (serveur lit S_CollectionPoint US) |
| `C_DailyAward.ini` | `DailyAwardTable` (aucun `*dailyaward*` dans fr-full) | calendrier de connexion vide côté client (serveur lit S_DailyAward US) |
| `S_ItemExtra`/`S_ItemComboExtra`/`S_ItemChestExtra.ini` | GameDataService.cs:274/308, Program.cs:108 | extras EDENFR inopérants (FindFile → null) |
| `Collection.ini` (`\|V.2\|7\|`) existe mais format différent | ne couvre pas le besoin collectionpoint | — |

## 6. Parseurs alignés US qui liraient mal le FR (risque si repointés)

| Parseur | Indices à risque | Première col divergente FR |
|---|---|---|
| `EnchantParser` (déjà pointé sur FR — **bug actif**) | ≥6 | 6 |
| `SpellParser` | 33-43 (`ElfGroup`, `DamagePower`, `HealPower`, `Variance`, `Result*`, `PayType`, `UpgradeBase`) | 34 |
| `GameDataParsers.ParseAchievements` | 11-14 | 11 |
| `CollectionParser` | 8 (groupe→0) | 8 |
| `WorldParsers.ParseTransport` | aucun (lit ≤16, diffs en fin) | — |
| `ElfParser` | aucun (lit ≤34, diffs ≥36) | — |
| `CraftRecipeParser.ParseElfCombine` | aucun (lit ≤22, diffs en queue) | — |
| `GameDataParsers.ParseClassDropItem` | aucun (scan heuristique) | — |
| `DropAndSpeedParsers.ParseDropItem` | aucun structurel (même schéma, même row cassée) | — |
| `ItemParser`, `ParseTalents`, `ParseItemChests`, `EquipSetParser`, `WorkOrderParser`, `MissionParser`, `MonsterParser`, `PartnerParser`, `DestinyDataParser`, `ProgressionParsers`, `DailyAwardParser`, `RewardScoreParser`, `PaySystemParser`, `BattlefieldParser`, `ShopAndMiscParsers`, `LifeSkillParser`, `EquipTrainParser`, `ClassStatsParser`, `ParameterParser` | — (schémas identiques ou tables sans pendant FR lu) | — |

## 7. Limites

- Analyse statique : l'impact runtime exact (quels champs mal lus se
  traduisent en bugs visibles) n'est pas établi — à confirmer en jeu
  (tooltips d'enchant, fenêtre succès, archive).
- `S_ItemChest` (`\|V.3\|32\|`) lu via `ParseItemChests` n'a été vérifié que
  sur le header ; la position d'insertion exacte des 4 colonnes US de
  `c_classdropitem` reste floue (padding vide — zone ~45-64) mais sans
  conséquence pour le scanner heuristique.
- La row 46131 de `c_enchant` (60 cellules, seule à déclencher le fix) a
  une anomalie propre non investiguée.
- `tools/graph` non utilisé : l'inventaire des chargeurs a été fait par
  grep direct (`ReforgedDataLocator`, `LoadFromDataDir`, `"*.ini"`).
