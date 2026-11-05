# S1 — Serveur C# : craft / work / enchant / fortif / fusion / magicbox / cube / collection / growequip

Reconnaissance 2026-11-03 — lecture seule. Périmètre :
`eden_server/server/src/{Eden.Zone,Eden.Core}`, `eden_server/docs/generated/FEATURE-MATRIX.md`,
`eden_server/server/tests/Eden.Tests`, `docs/knowledge/protocol-map.md` + `protocol-partials.md`.
Légende : [VERIFIED] = lu dans le code (fichier:ligne) ; [HYPOTHESIS] = déduction non prouvée ;
[GAP] = manque confirmé. Statut FEATURE-MATRIX rappelé quand pertinent (GOLDEN/DESASM/PASSTHRU/INCOMPLET/NON PROUVE).

Architecture commune : les NCIDs C→S de ce domaine sont interceptés dans
`ZoneEntryFlow.cs` (switch `case`, ex. `:957`,`:992`,`:1005`,`:1061`,`:1074`,`:1082`,`:1171`)
puis traités dans les partials `ZoneEntryFlow.*.cs` — le `PacketDispatcher`/`Handlers/*.cs`
ne voit que ce que le flow ne consomme pas. Les « ops » natives
(`char+0x670`, COperation) sont modélisées par `st.ShopOpType` /
`st.WorkOpType` + `PendingXxx` + cast schedulé `ScheduleOnLoop` (enchant natif
remplacé par un timer de boucle — fidèle, voir `Strengthen.cs:11-37`).

---

## 1. Work / récolte + plans (métiers 12-17) — IMPLÉMENTÉ [VERIFIED]

| NCID | Sens | Classe | Handler / émetteur | État | Matrice |
|---|---|---|---|---|---|
| 496 | S→C | ClientEnterWorks | `ZoneEntryFlow.Dialogue.cs:328` (service 12-17) | impl | DESASM |
| 170 | C→S | ZoneServerPlayerWork | `ZoneEntryFlow.cs:957` → `Work.cs:55` (ou `CombineStation.cs:31` si op 10) | impl | — |
| 171 | C→S | PlayerWorkResultOK | `ZoneEntryFlow.cs:992` → `Work.cs:264` | impl | — |
| 172 | C→S | PlayerWorkCheckData | `ZoneEntryFlow.cs:996` → `Work.cs:432` | impl | — |
| 497 | S→C | ClientPlayerWorkUpdate | `Work.cs:885` (`WorkPackets.BuildWorkUpdate`) | impl | PASSTHRU |
| 498 | S→C | ClientPlayerWorkResult | `Work.cs:443/622` (`WorkPackets.BuildWorkResult`) | impl | PASSTHRU |
| 500 | S→C | ClientPlayerWorkResultEnd | `Work.cs:357` | impl | DESASM |
| 499 | S→C | ClientPlayerEvent | — (work_event non porté) | absent | — |
| 433/435 | S→C | StrengthenCasting/ItemResult | `Work.cs:213`, `Work.cs:684` (type 14) | impl | DESASM |
| 299/300 | S→C | EnchantAdd/Remove (enchant travail 200+num) | `Work.cs:698/721` | impl | GOLDEN |

Wire 170 [VERIFIED `Work.cs:51-63`] : `[u32 workId][s16 catBag][s16 catSlot][s16 boostBag][s16 boostSlot]` (12 B).
Wire 171 : `[s16 workNum]` ; 172 : corps vide.
Wire 497 [VERIFIED `WorkPackets.cs:40-55`] : `[u16 n][s16 num][s16 type][i32 workId][i32 enchant 200+num][string fm_name]`×n.
Wire 498 [`WorkPackets.cs:63-85`] : `[i32 workId(−id=échec)][s16 type][s16 num][string fm][u16 n]{[u16 13][i32 itemId][i32 amount][i32 sockets][i32 combo][i32 extra×9]}`.

Logique [VERIFIED `WorkOrderService.cs`, `WorkModels.cs`, `Work.cs`] :
- Validation `ValidateStart` (`WorkOrderService.cs:139-166`) : slot libre 0-9 (+10 en secours,
  `FindFreeSlot:172`), kind catalyseur par type (12→0x33, 13→0x32, 14→0x34, 15→0x35, 16→0x36,
  17→0x37 — `WorkModels.cs:152-160`), niveau catalyseur ∈ [ReqLevel, MaxLevel], textes natifs
  (4/0x5a/0xb76/0x249/0x251/0x352/0x4f9/562 — `Texts:40-62`).
- Durée : instant → col durée brute ; chronométré → col×600 unités de 100 ms (`:82-84`,
  `:214-216`,`:245-246`). Perk Limitless `craft_time` appliqué (`Work.cs:189-196`).
  **[GAP] `DevWorkDurationUnits = 100` (10 s) plafonne TOUTES les durées**
  (`Work.cs:45`,`:200`,`:480-487`) — natif = minutes (ex. mine 30 min) ; TODO documenté.
- Booster : kinds natifs CChanceItem 0x1f43-0x1f48 absents des données → déviation :
  Corona's Essence 37006/37012/37015/37018 et Essence Powder 22433/22439/22445 acceptés,
  +30 ext_success (`Work.cs:244-251`) [VERIFIED déviation documentée].
- Consommation : immédiate si recette chronométrée, différée à l'échéance si instantanée
  (`ConsumeWorkInputsAsync:784`, `MaterializeWorkAsync:557-579`). Or via `AddGoldAsync`.
- Tirage : collecte = jet indépendant par produit, succès si `rand%100 < (100-max(0,(100-rate)
  ajusté decline)) + ext_success + ServerConfig.CraftSuccessBonusPct`
  (`WorkOrderService.cs:273-289`) ; combine = produit unique + malédiction `rand(1e8) ≤
  curse_rate` + sockets roulées `InlayService.RollSockets` (`:298-323`) ; type 14 summon =
  dé cumulatif → `SpawnHostileMob` ×qté hostile sur l'invocateur (`Work.cs:635-686`).
- Échec → `work_id` négatif (SaveWork(-id,0) natif, `Work.cs:608-609`).
- Claim (171) : capacité `SlotsNeededFor` (`:344-360`), remise stackable (merge 274) ou
  copie/unité avec sockets/combo/curse (`CreateWorkItemAsync:365`), EXP joueur collecte
  col8 (`Work.cs:330-334`), **EXP famille/guilde** collecte col6 / combine col3 →
  `Guilds.AddExperienceAsync` (`Work.cs:339-351`) [VERIFIED — lien guilde],
  reset + 500.
- Persistance : `player_works` (11 lignes work_num 0-10 + colonne maison `finish_at`)
  — `WorkRepository.cs:8-15`,`:29`,`:53`,`:67`,`:75`,`:84`,`:97`. Rechargement à l'entrée +
  matérialisation des ordres échus (`Work.cs:459-506`).

Ouverture : dialogue service 12-17 → `st.WorkOpType` + 496 `[i32 npcUid][s16 workType]`
(`Dialogue.cs:312-328`) ; combine UI = PlayerAction act 14 → op 10 + 432 (`Strengthen.cs:106-107`).

Tests : `WorkOrderTests.cs` (18 cas). [GAP] work_event/PlayerEvent non porté ; RandItemCombo
des produits non porté (combo/extra_combos = 0 — `WorkOrderService.cs:32-35`) ;
bâtiments/famille fm_*/building_* = 0 (NPC only) ; `extra_combo` 10-11 non persistés
(fidélité native, `WorkModels.cs:101-104`).

## 2. Station combine (service 10, CCombineOp) — IMPLÉMENTÉ [VERIFIED]

NCID 170 dispatché sur `ShopOpType==10` (`Work.cs:68-71`) → `CombineStation.cs:31`.
- `CheckCombine` (`CombineStation.cs:67-106`) : recette `WorkCombineById` catégorie 6,
  exactement 4 matériaux, catalyseur = matériau[0] au loc, booster kind 0x1f41
  (`BoosterKind:28`), or col9, slot libre sauf si une pile vidée (bVar4 natif).
- Cast 0x28×100 ms → 433 `[10][0x28][0]` (`CombineStation.cs:75-80`) ; résolution
  `DoCombineItemAsync:125` : consommation, roll `rand%100 < col13 + boosterBonus(0 non
  mappé) + CraftSuccessBonusPct` (`CombineStation.cs:110-112`), succès → produit col10 ×1 +
  435[10][1] + PlayerAction 0x2c ; échec → 435[10][0] + 0x2b (`:165-187`).
- Annulation : StrengthenClose 10 → 434 [10] (`Strengthen.cs:542-547`).
Tests : `CombineStationTests.cs` (8 cas).

## 3. LifeSkill (craft racial, S_LifeSkill) — IMPLÉMENTÉ, C→S 214 orphelin [VERIFIED]

| NCID | Classe | Handler | État | Matrice |
|---|---|---|---|---|
| 213 | QueryLifeSkill | `ZoneEntryFlow.cs:1082` → `LifeSkill.cs:52` | impl | **GOLDEN** |
| 214 | LifeSkill | `ZoneEntryFlow.cs:1086` → `LifeSkill.cs:71` | impl · ⟂ orphelin | NON PROUVE |
| 543 | ClientPlayerLifeSkill | `LifeSkill.cs:55` (corps manuel — généré plat faux) | impl | GOLDEN |
| 544 | ClientLifeSkillUpdate | `LifeSkill.cs:432` ×8 | impl | DESASM |
| 433/434/435 | cast/cancel/result métier | `LifeSkill.cs:162/207/276/308` | impl | DESASM |

Wire 543 [`LifeSkill.cs:44-50`] : `[s16 life_skill][u16 n][u16 recipeIds][u16 n][u32 exps][u16 n][u32 dues]`.
Wire 214 : `[u8 op][u16 recipeId][s16 bag][s16 slot]` — op 0=analyse bijou, 1/2=production
(2=avec exp), 3=oubli, 4=apperception manuelle, 5=level-up (`LifeSkill.cs:80-100`).
Logique : gate op 22 (PlayerAction 16 → CLifeSkillOp + 432 [0x68], `Strengthen.cs:109-110`) ;
niveau ≥30 ; or/reqLevel/matériaux (`ProduceCheckAsync:318`) ; cast 0x32×100 ms = 5 s
(`:30`,`:158-174`) ; `EndLifeSkill` → `DoProduceAsync:215` (consommation + or + produit +
achievement + 435) ou `DoJewelryAnalysisAsync:284` ; gain d'exp randomisé `base−var+rand(2var+1)`
clamp cap (`GainLifeSkillExpAsync:397-436`) ; apperception groupe 1 = remplacement pondéré
(`ApperceptionLifeSkillAsync:443`, 544 op3), groupes 2-5 = insertion (544 op1) ; level-up
chaîne col10 (`:511`) ; oubli : groupe 1 → col11 prev, autres → delete + reset exp prev
(`:560-603`) ; compatibilité profession `CheckLifeSkillType` par nibble (`:615-627`).
Persistance `player_life_skill` (`LifeSkillRepository.cs:15-77` — Get/Insert/InsertMany/
UpdateExp/Replace/Delete) ; chargement + auto-insert des recettes de base
(`EntryPipeline.Load.cs:94-116`). Tables : `LifeSkillParser` (S_LifeSkill |V.1|36|,
594 recettes) → `GameDataService.LifeSkillById/LifeSkillByItemId` (`GameDataService.cs:37-43`).
[GAP] aucun test dédié ; 214 NON PROUVE (client Godot n'émet pas — `protocol-partials.md:322`).

## 4. Strengthen / fortification — IMPLÉMENTÉ, C→S orphelins [VERIFIED]

| NCID | Classe | Handler | État | Matrice |
|---|---|---|---|---|
| 119 | ZoneServerStrengthenItem | `ZoneEntryFlow.cs:1061` → `Strengthen.cs:198` | impl · ⟂ | — |
| 120 | ZoneServerStrengthenClose | `ZoneEntryFlow.cs:1065` → `Strengthen.cs:532` | impl · ⟂ | — |
| 432 | ClientEnterStrengthen | `Strengthen.cs:86` (+ `SocialHandlers.cs:172`, Lottery/LuckyBag) | impl | DESASM |
| 433 | StrengthenCasting | `Strengthen.cs:250` `[9][0x14/0x28][0]` | impl | DESASM |
| 434 | StrengthenItemCancel | `Strengthen.cs:214/292` `[s16 9]` | impl | DESASM |
| 435 | StrengthenItemResult | `Strengthen.cs:426/449` `[9][ok][0×4]` | impl | DESASM |
| 436 | ClientModifyItemStrengthen | `Strengthen.cs:417` `[cont][slot][lvl][idx][prot]` | impl | DESASM |
| 290 | PlayerAction 0x2b broadcast | `Strengthen.cs:400-414` | impl | DESASM |

Wire 119 [VERIFIED `Strengthen.cs:197-212`] : `[s16 ?][i32 combineId][s16 cibleBag]
[s16 cibleSlot][s16 matBag][s16 matSlot][s16 chanceBag][s16 chanceSlot][s16 quick]` (20 B).
Logique : recette `FamilyRecipes` id 1-10 armes / 11-20 armures (`CheckStrengthenItemAsync:471-527`)
; cible kind ≠ 0x10, niveau < 14 ; pierre kind/equipLoc 46, interdites 0x9d00/0x9d01 si ≤ +9 ;
item de chance kind 0x1f42 **absent des données** (toujours rejeté, `:516-526`).
Coût `repairPrice×0.1+10` (`:230`,`:320`). Taux : lvl 0 → 100 %, 1-9 → `rate/2^lvl`,
10-13 → S_Parameter 517+lvl%10 (armes 0x11-0x21) / 521+lvl%10 (armures+bouclier 0x22)
(`:334-345`) ; pitié `failCount × EquipAddition` rodata [lvl] armures / [lvl+1] armes
(`:43-47`,`:347-357`). Cast 0x14 rapide / 0x28 (`:249`). Succès → EnchantLevel+1, persist,
436+435[9][1], anim 0x2b, refresh profil, annonce 0x69d si > +7 (`:384-441`).
Échec → **item détruit** + 435[9][0] + 275 (`:443-465`) ; variante 435[9][2] (protection)
non active — item protecteur inexistant.
[GAP] `AddComboStrengthen` aux paliers +6/8/10/12/14 non modélisé (`:388-390` commentaire) ;
variante coût addattr+0xe0 non portée (`:225`). Pas de test dédié.

## 5. Inlay / sertissage — IMPLÉMENTÉ (voie item-sur-item) ; 67 = stub fidèle [VERIFIED]

- 67 `ZoneServerInlayItem` → `Strengthen.cs:582-599` : gate op 0x23 + portée NPC → répond
  371 `[s16 0]` — le natif `CItemFactory::InlayItem` est un **stub retournant 0**
  (feature NPC morte, commentaire `:574-581`). ⟂ orphelin.
- Voie réelle : NCID 19 `ZoneServerItemUse`, item kind 45 → case 0x2d
  (`ItemActionHandlers.cs:1009-1130`) : `InlayService.ValidateInlay` → `ApplyInlay`
  (`embedded_id[idx]=gemId`) → `UpdateEmbeddedAsync` + texte 0xc1 + refresh profil +
  consommation (291/275). `InlayService.cs` : kinds armes 17-34, armures {1-16,37-40,78,
  81@loc∈{10,34}}, add-attr {45,46,47,56,59,79} (`:55-68`) ; `RollSockets:89` =
  ChangeEmbeddedAmount (réutilisé pour les produits de craft). Tests `InlayServiceTests.cs` (22).
- 370 EnterInlayShop → `ZoneEntryFlow.Mission.Commands.cs:229` (DESASM) ; 371 DESASM.

## 6. Enchants — IMPLÉMENTÉ (système complet) [VERIFIED]

### 6a. Scrolls d'enchantement équipement (witchcraft)
NCID 19 ItemUse kinds 56/79 → `ItemActionHandlers.ApplyWitchcraftAsync:1180` : validation
`InlayService.ValidateWitchcraft` (équipement, EquipLoc↔kind, pas même enchant, niveau —
textes 0x9f6/0x9f8) → `witchcraft = itemId scroll` + `UpdateWitchcraftAsync` + texte 0x9f7
+ 369 record complet + refresh profil + consommation. [GAP] kinds 59 (teinture) et 84
(transmutation) non portés — ack neutre (`ItemActionHandlers.cs:1010-1011`).

### 6b. Moteur EnchantService (hub) [VERIFIED]
`Eden.Core/Game/Services/EnchantService.cs` (613 lignes) : S_Enchant (~19,8 k) +
S_ItemMallEnchant + **overlay Reforged c_enchant/c_itemmallenchant canonique**
(`ApplyClientOverlay:99-138`, désactivable `EDEN_REFORGED_DB=off`).
**Le bug `row.Length==60` d'`EnchantParser.cs:84` (schema-drift-audit) est CORRIGÉ** :
`NormalizeRow` réinsère la colonne elf-pet index 6 pour les lignes FR 61/56/62 cellules
(`EnchantParser.cs:80-90`, appel `:104`) [VERIFIED].
API : `GetUseCommands` (4 blocs cmd+6 vars cols 9+7k, cmd 160 dépliée — `:397-429`),
`GetRideInfo` (2134/2140/2051 — `:200-242`), `CanCancelByClient` (col7 bit0 + col8 0x10 —
`:250`), `IsDebuff`/`IsUndispellable`/`GetTypeId` (col38)/`GetGrade` (col39),
`TypeAddAllowed` (`:314`), `BlocksReapply` (col8 0x100), `GetMaxStack` (col59),
`GetElfEnchantGroup` (col6), `Evaluate` procs (`:157`).

### 6c. Cycle de vie player enchants
- `AddPlayerEnchantAsync` (`Status.cs:354-630`) : conflit type/grade → remplacement 300,
  max-stack, exécution des use-commands, hook ride 2134, persistance `player_enchants`
  (`UpsertEnchantAsync:562`), map session, 299 + 302 (+372 team).
- `RemovePlayerEnchantAsync` (`Status.cs:638`) : 300 + can-see + `DeleteEnchantAsync`.
- Persistance : `PlayerEnchantRepository.cs:16-70` (Get/Upsert=delete+insert/Delete/
  DeleteAll) ; rechargement login `Program.cs:583`.
- NCID 85 EnchantCancel `[s16 effectId]` → `Status.cs:713` (garde icône +
  `CanCancelByClient` → retrait réel). INCOMPLET.
- NCID 18 EnchantUpdate `[i32 handle]` → `MobEnchants.cs:350` : mob (registre Live) ou
  joueur (statuts session) → 301 entrées [effectId][caster][rest][stacks][level].
  301 GOLDEN. [HYPOTHESIS] le handler doublon `MiscHandlers.cs:158` (dispatch) est mort :
  `case 18` intercepte avant (`ZoneEntryFlow.cs:1171`).
- NCID 127/128 EnchantStart/Pause → `CombatHandlers.cs:175-190` : **écho seulement**
  (445/446 renvoyés, aucune logique de proc côté serveur). ⟂ orphelins ; DESASM.
- 315 EnchantReset : absent. 261 ElfCastEnchant : absent (classe morte native).
- Collection awards → enchants permanents `rest=-2` (`Collection.cs:233-253`) ;
  elf mix → `integrate_spell_*` permanents (`Elf.Work.cs:176-194`) ; passifs pet
  (`ApplyPetPassivesAsync:201`).

## 7. ItemCombo / fusion — PARTIEL [VERIFIED]

- **Fusion d'elfs** : NCID 107 `ZoneServerElfWork [s16 loc][s16 mode]` →
  `ZoneEntryFlow.cs:545-570` : gate carte (S_Node col42 → bits 0x20/0x40, textes
  0x209e-0x20a0 — `Elf.Work.cs:71-99`) ; mode 1 = ToMix (slot intégration, cooldown 60 s
  texte 0x1472, enchants intégrés → 567 `Elf.Work.cs:104-156`) ; modes 0/2 →
  `HandleElfWorkAsync` (`Elf.cs:722`) ; 7/8/9 → dispatcher legacy `ElfHandlers.cs:100-145`
  (écho + énergie — chemin loterie elf). 111 ElfWorkResultOK → 416 (`ElfHandlers.cs:151`,
  orphelin). NCID 227 `ZoneServerElfMix` → `Elf.Mix.cs:66` (CPetUpgradeOp : 2 elfs +
  matériau, cast 40 ticks ≈4 s, 574/575/568, star max 4, matériaux whitelist `:47-53`).
- **RandItemCombo** (affixes produits) : [GAP] non porté — `WorkOrderService.cs:32-35`.
- **S_ItemCombo.ini** : parsée DEUX fois — `CraftRecipeParser.ItemCombo`
  (`GameDataService.cs:305-315` + overlay S_ItemComboExtra) utilisée par
  `MagicBoxService.PickComboId` et `EquipmentStatService.MagicBoxEnchantIds` (col10
  EnchantId — `:196-211`) ; `ShopAndMiscParsers.ParseItemCombos` (`ItemComboDef`,
  schéma affixes G-codes) = [GAP] **parser orphelin** (aucun appelant trouvé).
- `CraftService.cs` (CraftElfAsync/CraftFamilyAsync/GetCombosForItemType) =
  [GAP] **service orphelin** — jamais instancié, `ConsumeMaterialAsync` est un
  placeholder retournant `true` (`:126-132`).

## 8. MagicBox (Arcane Box) — IMPLÉMENTÉ, C→S orphelin [VERIFIED]

| NCID | Classe | Handler | État | Matrice |
|---|---|---|---|---|
| 612 | ClientOpenMagicBoxDialog | `Dialogue.cs:392-399` (service NPC 31 → op 0x58) | impl | PASSTHRU |
| 257 | ZoneServerExecuteMagicBox | `ZoneEntryFlow.cs:1074` → `MagicBox.cs:70` | impl · ⟂ | NON PROUVE |
| 613 | ClientMagicBoxResult | `MagicBox.cs:369` (`MagicBoxPackets.BuildMagicBoxResult`) | impl | PASSTHRU |
| 433/434 | cast/cancel | `MagicBox.cs:113/217` `[0x1f][0xf]` | impl | DESASM |
| 369 | ClientModifyItemInfo | `MagicBox.cs:357` (mode −1 record complet) | impl | PASSTHRU |

Wire 257 [VERIFIED `MagicBox.cs:15-19`,`:123-159`] : `[u16 type][u16 n][s16 sacs][u16 n]
[s16 locs][s16 mainBag][s16 mainSlot][u16 n][s16 comboIds][s16 mallBag][s16 mallSlot]` ;
type 1 = conversion, 2 = scouring. Bornes strictes (n ≤ 64, combos ≤ 3).
Logique `MagicBoxService.cs` : `CheckCosts:141` (type item via flags col11 bits 8/0x20/0x10
→ `MagicBoxTables.MainType:91`) ; conversion : star ≤ 4, chaque sacrifice validé par bucket
qualité `QualityNeed[type*5+star]` → taux S_Parameter 624-638 (`ConversionRate:103`) ;
énergie arme `itemLv×rate+0.5` sinon `rate+0.5` (`:230-234`), seuil `ConverEnergyAmount`
params 609-623 → star+1, combo roulé aux stars 1/3/5 (`PickComboId:321` = attrType==type+4,
kind ∈ ApplicableItemTypes, MinLv≤reqLv≤MaxLv, Tier==star, pondéré Value) ; scouring :
star 1-5, ≤1 combo (ou d4+1 mall), `ScouringNeedEquipAmount` params 639-653, reroll
`level=min(oldTier+1,star)` (`Scour:296`). Catalyseur mall = enchant cmd **8022**
(args {énergie,%,stars,%,nbCombos,wild} — `MagicBox.cs:191-210`, `MallParams:50-82`).
Tables rodata extraites : `MagicBoxTables.cs` (QualityNeed/ScouringQualityNeed = u16
directs, ids S_Parameter). Persistance `UpdateMagicBoxDataAsync` (`MagicBox.cs:307-311`).
Cast 0xf×100 ms. StrengthenClose 31 → 434 `[0x1f]` (`Strengthen.cs:556-559`).
Tests `MagicBoxServiceTests.cs` (12). [GAP] 614 `ClientBackItem` (DoMagicBoxShift)
non émis — `protocol-partials.md:226`.

## 9. Cube Horadric — IMPLÉMENTÉ, C→S orphelin [VERIFIED]

PlayerAction 101 → op 28 (0x52 CCubeOp) + 432 `[0][0x1c]` (`Strengthen.cs:112-113`).
232 `ZoneServerVerifyCubeCosts` → `Cube.cs:65` : wire `[u32 recipeId][u16 n][s16 sacs]
[u16 n][s16 slots][s16 luckyBag][s16 luckySlot]` (`Cube.cs:13-19`,`:70-84`).
- `CubeService.CheckCosts:96` : map requis {main+subs}, satisfaction directe ou par
  `CubeGroupIds` (col75), qualité aux col5 (0x15af/0x21c4 lucky, 0x232 or, 0x4f9 place),
  cube chanceux = enchant cmd **8007** {bonus,cap} (`LuckyInfo:316`), propagation bind.
- Cast 0xf → 433 `[0x1c]` ; exec `ExecuteCubeAfterCastAsync:189` : DeductItem par slot
  (275/291 par pile), or, roll `rand100 < rate + luckyBonus + CraftSuccessBonusPct`
  (`CubeService.cs:288-290`), pool produit (aux / direct <100000 / groupe col75 /
  ApplicableSet col31 — `ProductPool:209`), picks non-stack ×amount ou stacks ≤3
  (`SplitStackAmount:296`), qualité `PickQuality:260` (poids cumul ×100),
  broadcast 0x2b succès / '-''/''*' échec, 435 + 572 vecteurs manuels
  (`ProduceCubeItemsAsync:310-416`).
- Tables : `ShopAndMiscParsers.ParseItemChests` (S_ItemChest.ini + overlay
  S_ItemChestExtra — `Program.cs:100-117`).
Tests `CubeServiceTests.cs` (12).

## 10. Collection (cartes) — IMPLÉMENTÉ [VERIFIED]

| NCID | Handler | Wire | Matrice |
|---|---|---|---|
| 152 QueryCollection | `Collection.cs:33` | → 472 type 0 `[s16 0][i32 pts][u16 n][s16 ids]` | GOLDEN |
| 150 SaveCollection | `Collection.cs:55` | flag 0 → `[s16 cont][s16 loc]` ; sinon 2 vecteurs parallèles | impl |
| 151 GetCollection | `Collection.cs:174` | `[s16 itemId]` | impl |
| 472 UpdateCollection | `Collection.cs:36/96/207` | types 0 liste / 1 ajout / 2 retrait | GOLDEN |

Logique : rate-limit 500 ms (`:59-61`) ; gates def `S_Collection` (0x67e), masque locate
serveur bit0 (`:133`), déjà déposé (0x680) ; dépôt → move table `collection` + points +
compteurs de groupe (groupe + clés 1000/1001 special — `:148-153`) + persist
`player_achievement_rank.collection_point` (`:258-269`) + 275 + texte 0x67c ;
retrait → slot libre (0x4f9), 472 type 2 AVANT 274, texte 0x67d, points clampés ≥0.
Awards : `RefreshCollectionAwardAsync:233` — S_CollectionPoint (toutes paires clé≥seuil)
→ enchants permanents (rest −2) + refresh stats. Parsers `CollectionParser.cs`
(S_Collection :16, S_CollectionPoint :30) ; repo `InventoryRepository.Collection.cs`.
Tests `CollectionTests.cs` (3).

## 11. GrowEquip = Mana Infusion (EquipTrain) — IMPLÉMENTÉ, C→S orphelin [VERIFIED]

- 230 `ZoneServerEquipTrainSetItem` → `EquipTrainHandlers.cs:30-83` →
  `EquipTrainService.SetItemAsync:246`. Wire : `[s16 container][s16 index][s16 key]
  [u16 n][u8 types×n]` (`EquipTrainHandlers.cs:28-29`). container −1 = désélection ;
  0 sacs / 1 équipé ; éligibilité flags<0x80 ou lié (`:235-236`) ; types ≤14, masque
  `S_Item col10`, sans doublon (`:217-230`) ; init `SetItemTrainType` = 3 types + level 1
  (`:270-295`) → 570 `ClientEquipTrainItemChanged` (si loc changée) + 369 record +
  refresh stats. 230 NON PROUVE (orphelin) ; 570 DESASM.
- Gain au kill : `GainEquipExpAsync:325` — `gearExp = charExp × S_Parameter 496` (`:143-144`),
  gates (équipé, serial, durabilité, train_level < RequiredLevel < niveau joueur,
  archétype `kind%3 == archetype%3` pour locs 1-5 — `:354-356`), banque + boucle paliers
  au seuil `S_EquipTrain[level+1].Exp`, slot pondéré params 504/505/506 (0.5/0.3/0.2 —
  `PickSlot:428`), textes 5494/5495, 369 à chaque crédit.
- Contribution stats : `slotLevel × LocMultiplier(EquipLoc → params 497-503 : 1H×1.3,
  2H×2.6, armures 1.2/0.8/1) × row.Coefs[type]` (`:128-158`), 15 canaux d'attributs.
- Table `S_EquipTrain.ini` |V.1|17| → `EquipTrainParser` (`GameDataService.cs:324`).
[GAP] aucun test dédié (grep `EquipTrain` dans Eden.Tests : 0 fichier).

## 12. Adjacent guilde/économie — rappel rapide

- **Loterie** (family altar + mall) : 129 GoLottery / 184 CheckUpdateLottery /
  224 UpdateLotteryBonus / 255 EvaluateLotteryOpen / fenêtres 95 MailOpenBox(150/151)
  → `ZoneEntryFlow.Lottery.cs` (émet 448/449/450/511) + `LotteryService` +
  `LotteryRepository` (tables `in_game_lottery`/`new_lottery`, week points).
  450 GOLDEN, 448/449 DESASM, 511 PASSTHRU ; C→S 184/224/255 ⟂ orphelins.
- **FortuneBag** : 223 QueryFortuneBagCounter GOLDEN → 557 (`FortuneBag.cs:146`) ;
  `FortuneBagRepository` (`player_fortune_bag` par account_id) ; 83 monde
  (liste contenus) absent — NON PROUVE.
- **EXP guilde** : ordres de travail alimentent `Guilds.AddExperienceAsync`
  (S_FamilyCollect col6 / S_FamilyCombine col3 — `Work.cs:339-351`).
- Services **orphelins confirmés** : `CraftService`, `LifeSkillService`
  (Game/Services — gather-node, schéma différent de `LifeSkillParser`), 
  `EquipmentEnchantService` (params 654-665 +15..20 jamais utilisés — le vrai
  renfort est `Strengthen.cs`), `ShopAndMiscParsers.ParseItemCombos`.

## 13. Couverture tests (`eden_server/server/tests/Eden.Tests`)

| Domaine | Fichier | Cas |
|---|---|---|
| Work/ordres | `WorkOrderTests.cs` | 18 |
| Station combine | `CombineStationTests.cs` | 8 |
| Cube | `CubeServiceTests.cs` | 12 |
| MagicBox | `MagicBoxServiceTests.cs` | 12 |
| Inlay | `InlayServiceTests.cs` | 22 |
| Collection | `CollectionTests.cs` | 3 |
| Parsers craft | `CraftParserTests.cs` | 36 |
| FortuneBag | `FortuneBagTests.cs` | (présent) |
| Golden wire | `Data/golden/ncid_{213,223,152,172,301(?),316}.json` | byte-à-byte |
| **Absents** | EquipTrain, LifeSkill (214), Strengthen (119/120), EnchantCancel, ElfMix | 0 |

## 14. GAPs consolidés

1. **[GAP] Durées de travail plafonnées à 10 s** (`Work.cs:45 DevWorkDurationUnits`) —
   natif = minutes ; à remettre à ×600 avant mise en production.
2. **[GAP] `work_event`/PlayerEvent (499)** non porté — ordres NPC seulement.
3. **[GAP] `RandItemCombo`** — les produits de craft n'ont pas d'affixes
   (combo/extra_combos = 0 ; vecteur 498 émis vide de combos).
4. **[GAP] `AddComboStrengthen`** — paliers +6/8/10/12/14 du renfort sans combos incrustés.
5. **[GAP] Item de chance/protection** kinds 0x1f41/0x1f42/0x1f43-48 absents des données
   FR → substitutions documentées (boosters +30) ou rejet systématique.
6. **[GAP] MagicBox 614 `ClientBackItem`** (DoMagicBoxShift) jamais émis.
7. **[GAP] Scrolls kind 59 (teinture) / 84 (transmutation)** non portés — ack neutre.
8. **[GAP] Orphelins serveur** : `CraftService` (placeholder), `LifeSkillService`
   (gather-node non câblé), `EquipmentEnchantService`, `ParseItemCombos`
   (ShopAndMiscParsers), doublon EnchantUpdate `MiscHandlers.cs:158`.
9. **[GAP] Tests manquants** : EquipTrain (230), LifeSkill (214), Strengthen (119/120),
   EnchantCancel (85), ElfMix (227) — aucun fichier de test ; les NCIDs correspondants
   sont NON PROUVE dans FEATURE-MATRIX car le client Godot n'émet pas ces paquets.
10. **[GAP] Sockets de bâtiments/ville de famille** : fm_id/building_item_id = 0
    (ordres NPC uniquement) — la voie « ville de guilde » des works n'existe pas.
11. **[HYPOTHESIS] Lottery elf (107 modes 7/8/9)** : chemin dispatcher `ElfHandlers.cs`
    = écho simplifié (énergie + IsWorking) — fidélité native invérifiée.
