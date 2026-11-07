# E4 — Cycle de vie des bâtiments de famille (usure / EXP / level-up / due_date)

Date : 2026-11-05. Lot E4 de `SYNTHESE.md` (phase E). Cible : serveur C#
(`eden_server/server`), données `corpus/fr-full` uniquement.

## Preuves natives (toutes vérifiées sur les exports `re/ghidra/`)

### Chaîne NPC → bâtiment (résolution de la station)

- `CZoneServer::InitFamilyNpc` (zone @0x0816f5d0) : ne spawn comme NPCs que
  les items du conteneur 0 de type **0x2a/0x52** (jamais les bâtiments 0x29).
  Le net id du NPC spawné est écrit dans `item+0x9c` (clone zone).
- `SFamily::GetBuildingItemByNpcNetID` (zone @0x08465880) : trouve l'item
  0x2a dont `+0x9c == npcNetId`, prend sa loc (clé de map), calcule la
  **base de groupe** (10-19→10, 20-29→0x14, 30-39→0x1e, 40-49→0x28) et
  rend l'item à cette loc = le bâtiment 0x29.
- `CMiningShop::OpenShop` (zone @0x0857a080, idem Planting/Summon/…) :
  - `GetNpcItemByNpcNetID` → l'item NPC ; **`item+0x18` (due_date) ≠ 0 et
    < now → refus texte 0xc56** (contrat expiré).
  - `GetBuildingItemByNpcNetID` → bâtiment ; `item+4` = db id → op+0x10,
    `item+0x94` = level → champ du 496.
  - taxe : `IsInFamilyInstance && char+0x5c != visitFmId` → `sVar6 =
    family+0x32` (BusinessTax), sinon 0 → op+0x18 et champ du 496.
- Wire 496 `ClientEnterWorks` : `[u32 npc][s16 workType][s16 taxRate]
  [u16 bldLevel][s16 0]` (12 octets = nos Field1..Field5).

### Gates au démarrage (CheckToCombine/CheckToCollect — zone)

- `GetBuildingItemByItemNetID` introuvable → texte **0x251**.
- `building.Level < recipe.BuildingLevel` → **0x392**.
- `building.Durability < 1` → **0xa09**.
- `fm_name` = nom de la famille visitée, `fm_id` = son id (work+0x28),
  `building_item_id` = uid DB (work+0x2c).
- **Usure au DÉMARRAGE** pour les recettes **non-instantanées**
  (`combine+0x14==0` / `collect+0x24==0`) : `rand%100 < chance` →
  `-amount` (zone @~230900 combine, ~231910 collect).
- `UpdateBuildingRevenue` — compteur écrit jamais lu → inerte, non porté.

### Fin d'ordre (EndCombine @0x08165510 / EndCollect @0x08167110 / EndSummon @0x08163110)

Bloc exécuté **même en cas d'échec** (hors `bVar7`), si `work+0x28 != 0`
et la SFamily est trouvée dans les maps d'instances (0x460 + 0x47c) :

1. `work+0x2c != 0` → `GainBuildingExpByItemID` avec
   `gain = (int)((float)(work+0x34 × recipeBldExp) × 0.01) + recipeBldExp`
   (`DAT_087cc690` = 0.01 ; `work+0x34` = ext_building_rate — booster
   kinds 0x1f44/0x1f48, absents des données → 0 en pratique).
2. `SFamily::GainExp(family, recipeFamilyExp)` — vers la famille
   **propriétaire de la ville** (work.FmId), pas la guilde du joueur.
3. Recettes **instantanées** (`combine+0x14==1`) → usure à la FIN :
   `rand ∈ [0,99] ; usure iff rand <= chance` → `-amount`.
4. Collect/EndSummon : champs analogues (chance col11/+0x2c, amount
   col12/+0x30) ; EndSummon usure iff `rand <= +0xb` du def collect.

### `CFamily::UpdateBuildingExp` (mission @~0x81bf8xx, via TC_UpdateBuildingExp)

- item = conteneur 0, `item_type == 0x29` ; itemdef de `item_tid` :
  `itemdef+0x28` = **col9 de c_item** = type de station **0x18-0x1d**
  (ferme 24, mine 25, autel 26, alchimie 27, fonderie 28, mare 29) —
  chez nous `ItemDef.EquipLoc` (même colonne, sémantique par kind).
- seuil = `c_level[level+1]` cols **12-17** indexées par (station-0x18).
- `exp += gain` ; si `exp >= seuil && level < family.Level && next
  template existe` → `exp -= seuil`, `tid = next template`,
  `level = next.Level` (col4 du template suivant), persist + broadcast.
- `CFamily::CheckBuildingLevelUp` (mission @0x081b1f40, appelé par
  `SetLevel`) : pour chaque 0x29, si `c_level[level+1]` existe et seuil
  ≤ exp → re-évalue ; si la ligne n'existe plus (max) et `tid != next` →
  synchronise `tid/level` sur le template suivant.
- `c_level` : `CLevelData+0x30` = **col11** = seuil XP famille
  (courbe 2000/7000/24000…) ; cols 12-17 = seuils bâtiment.
- `CFamily::GainExp` (mission @0x081beec0) : level-up seulement
  `level < 3` (une fois par appel : `exp -= seuil; SetLevel(+1)`) ;
  `level ≥ 3` → `exp = min(exp, seuil)` (cap).

### due_date (item+0x18 / colonne due_date)

- `CFamily::InsertItemToBuilding` (mission @0x081b0d80) : conteneur 0,
  `item_type ∈ {0x2a,0x52}` et `itemdef+0x70 != 0` →
  `due_date = now + itemdef+0x70` ; sinon 0.
- `itemdef+0x70` = `DurationSeconds` (col26 unité × col27, parser existant).
- EDENFR : les NPCs 0x2a ont des durées (3 j / 7 j) → expiration réelle.
- Lu par `OpenShop` (refus 0xc56). Les bâtiments 0x29 restent due_date=0.

## Implémentation

### Eden.Core

- `ProgressionParsers.LevelRow` : + `FamilyExpNeeded` (col11) +
  `BuildingExps` (`int[6]`, cols 12-17).
- `Eden.Core/Game/Family/FamilyBuildingWorkRules.cs` (pur) :
  `RollWear(rng, chance, inclusive)`, `BuildingExpGain(extRate, recipeExp)`,
  `TaxedGold(gold, taxRate)`, `ApplyBuildingExp(item, stationType,
  familyLevel, getThreshold, nextDef)`, `EvaluateLevelUps(items, guild,
  …)` (CheckBuildingLevelUp), `DueDateFor(itemType, durationSeconds, now)`,
  `GroupBase` déjà dans `FamilyHallRules`.
- `SocialRepository` : `UpdateFamilyBuildingDurabilityByIdAsync(familyId,
  id, durability)` + `UpdateFamilyBuildingProgressAsync(familyId, id,
  tid, level, exp)`.

### Eden.Zone

- `EntryState` : `WorkOpBuildingItemId`, `WorkOpTaxRate`,
  `HallNpcItems` (`ConcurrentDictionary<uint, FamilyBuildingItem>` —
  handle → item 0x2a/0x52) ; purge dans Migration (à côté de Replayed).
- `FamilyInstance` : `InjectHallNpc` enregistre l'item dans
  `HallNpcItems` ; put (130) écrit `DueDate` natif pour 0x2a/0x52.
- `Dialogue` (case 12-17) : en hall, résolution NPC→due_date (0xc56)
  →bâtiment (base de groupe) → `WorkOpBuildingItemId`, taxe
  (`BusinessTax` si famille visitée ≠ guilde du joueur), 496 avec
  `Field3=tax Field4=bldLevel`.
- `Work` : gates famille après `ValidateStart` (0x251/0x392/0xa09) ;
  `FmId/FmName/BuildingItemId` armés ; coût taxé (op+0x18 figé dans
  `WorkDeferredLocs.TaxRate` pour la consommation différée) ; usure au
  start pour les non-instantanés ; bloc famille dans `Materialize*`
  (même sur échec) : EXP bâtiment + level-up + persistance + push 484,
  EXP famille vers `work.FmId`, usure fin pour les instantanés ;
  `CheckBuildingLevelUp` après une hausse de niveau famille dans ce
  chemin. **[FAIT — `ZoneEntryFlow.Work.Family.cs`]**
- `GuildService.AddExperience` : port de `CFamily::GainExp` (mission
  @0x081beec0) — seuil `c_level[level+1]` col11 injecté via
  `AttachFamilyExpCurve` (Program.cs) ; level<3 → un seuil soustrait +
  level-up ; level≥3 → clamp [0, seuil] ; ligne absente → aucun gain.
  Remplace la formule placeholder `level*10000`. **[FAIT]**

## Tests

- `FamilyBuildingWorkRulesTests` : gain EXP (rate 0/100), usure
  0/100 chance (les deux bornes), `ApplyBuildingExp` (sous-seuil,
  franchissement, cap niveau famille, sans template suivant, seuil 0),
  `EvaluateLevelUps`, `DueDateFor`, `TaxedGold`. **[22 verts]**
- `FamilyHandlerTests.GainExp_*` : level-up <3, rejet au seuil, clamp
  ≥3, ligne absente, courbe non branchée, négatif. **[6 verts]**
- `GuildServiceTests.Guild_LevelUpAndTaxes` : mis à jour pour la
  courbe native (dépendait du placeholder).
- Suite complète `dotnet test` — échec pré-existant
  `GrantExp_Level30Repro` à ignorer (non lié).

## Hors périmètre / limites

- `UpdateBuildingRevenue` : compteur inerte natif — non porté.
- Cap famille `AllFormulaParas+0x718` : la table formula_parameters
  n'existe pas dans `corpus/fr-full` (c_parameter = 129 lignes ≠ 0x2a6)
  → gap documenté ; la borne effective est la fin de c_level (seuils
  col11 vides dès le niveau 15).
- ExtBuildingRate : kinds boosters natifs absents des données → reste 0.
- `WorkDeferredLocs.TaxRate` : mémoire session (non persisté) — après
  une reconnexion, la consommation différée utilise le coût de base ;
  limite acceptée (le natif perd aussi le contexte op au relog).
- Broadcast family-exp (ChangeFamily type 10 + texte 0xb7e au donneur)
  non repoussé aux membres en ligne — gap mineur d'affichage.
- Validation live (hall réel, station qui use) : Phase G.
