# 2026-11-06 — Readiness serveur par système (audit statique)

Audit du code serveur C# avant les tests live. Fichiers:lignes revérifiés.

## Résumé

| # | Système | État | Risque |
|---|---------|------|--------|
| 1 | Ordres de travail VDG (collect/combine) | Implémenté + persistance `player_works` | P1 |
| 2 | Récolte VDG outils (pioche/gant/orbe) | Implémenté via enchant 2933 ; `LifeSkillService.StartGather` = code mort | P1 |
| 3 | Strengthen +14 (NCID 119) | Implémenté + persistance ; **« pierres 100 % » ≠ slot matériau** | P1 |
| 4 | LifeSkill racial (213/214) | Implémenté complet + persistance `player_life_skill` | OK |
| 5 | Cube 0x52 + MagicBox 0x58 | Implémentés, tables au boot | P1 données |
| 6 | Monnaies spéciales | Fame = `special_coins` OK ; **lover coin (priceType 4) sans stockage → refus** | P0 lover |
| 7 | Trade + étals | Implémenté ; étal = S001 régions 600-609 seulement | P1 |
| 8 | Mort/revive (types 0/1/5/6) | Implémenté, savepoint persisté | OK |
| 9 | XP/niveau/stats/sorts | Implémenté, formules natives | OK |
| 10 | Enchant/witchcraft 56/79 (NCID 19) | Implémenté + colonne `witchcraft` | P1 |
| 11 | Ouverture fortification (432 op 9) + close 120 | Confirmé | OK |
| 12 | Contrats NPC VDG 0x2a (NCID 130) | Implémenté + housekeeper auto | P1 |

## Détails par système

### 1. Work orders VDG
- `ZoneEntryFlow.cs` : case 170→`HandlePlayerWorkAsync` (:1002), 171→`HandleWorkResultOKAsync` (:1037), 172→`HandleWorkCheckDataAsync` (:1041).
- Wire `[u32 workId][s16 catBag][s16 catSlot][s16 boostBag][s16 boostSlot]` (`Work.cs:53`) ; op 10 → `HandleCombineItemAsync` (:66).
- `CombineStation.cs:31` : station combine (service NPC 10, op 0x27), recettes cat 6.
- `WorkOrderService.cs` : ValidateStart :141, RollCollectProducts :275, RollCombineProduct :300. Types 12-14 collect / 15-17 combine.
- Ouverture : services NPC 12-17 → 496 `ClientEnterWorks` (`Dialogue.cs:312-339`).
- Persistance `player_works`, timers réarmés au login.
- ⚠️ `work_event` non porté (`Work.cs:31-32`) ; workId = CatalystItemId (col2 c_familycollect) ; `EDEN_DEV_WORK_CAP` à vérifier.

### 2. Récolte VDG outils
- Outil kind 44 → enchant d'usage cmd **2933 `AttrGainCollectItem <toolItemId> <chance%>`** (`EnchantCommands.cs:239`). Usage NCID 19 → `Items.cs:517-584` : résout WorkCollectById par CatalystItemId, gates UseAreaNodeIds/UseAreaSubType → texte 0xe1c hors zone, roll par produit, or notifications slots.
- **P1 : `LifeSkillService.StartGather`/`TickGather` = code mort** (aucun appelant) — récolte = usage direct de l'outil → produits immédiats (modèle natif `CEC_AttrGainCollectItem @0x085061a0`).
- Préparation : outils avec useEnchantId→enchant 2933 arg1=propre id ; vérifier UseAreaNodeIds/UseAreaSubType.

### 3. Strengthen +14
- `Strengthen.cs:199` HandleStrengthenItemAsync (ShopOpType 9, cast), :562 CheckStrengthenItemAsync (EnchantLevel<14, kinds, pierres 0x9d00/0x9d01 refusées si ≤+9, chance item = enchant 0x1f42/8002), :286 DoStrengthenItemAsync (coût, taux recette/2^niv puis S_Parameter 517-520 armes/521-524 armures pour +10-13, pitié, échec→**item détruit** :540-552), :629 close 120.
- **P1 majeur : pas de « pierre 100 % » en slot matériau** — taux recette/S_Parameter seulement. Voie 100 % = slot chance avec enchant kind 8002 `ChanceUpper ≥ level` → total=100 (l.375-377). **`ChanceSafeLevel` (col12 c_enchant) parsé mais JAMAIS utilisé** → vérifier l'encodage FR des pierres garanties.
- `CraftSuccessBonusPct` ne s'applique PAS au strengthen.

### 4. LifeSkill racial
- `LifeSkill.cs` : 213→543 (GOLDEN), 214 ops 0-5 (analyse/production/oubli/apperception/level-up). Ouverture : PlayerAction 16 → 432 op 0x68. Cast enchant 211, 433/434/435+544.
- Persistance `player_life_skill` + seed recettes base au login.
- Vérifier `S_LifeSkill.ini` (~594-595 recettes) dans DATA_DIR.

### 5. Cube + MagicBox
- Cube : PlayerAction 101 → 432 op 0x1c ; NCID 232 `HandleVerifyCubeCostsAsync` (`Cube.cs:65`) ; `CubeService` (CheckCosts, ProductPool, PickItem/Quality, RollSuccess + lucky 8007 + bonus env). Recettes `S_ItemChest`+`S_ItemChestExtra`. Émis 433-435, 572, 274/275, 316.
- MagicBox : service NPC 31 → op 31 + 612 (`Dialogue.cs:439-446`) ; NCID 257 `HandleExecuteMagicBoxAsync` (`MagicBox.cs:70`) types 1/2 ; `MagicBoxService` ; persist `MagicBoxEnchant1-3/Star/Energy` ; 369+613.
- Préparation : NPC avec service type 31 pour magicbox.

### 6. Monnaies spéciales
- `ShopHandlers.cs:480-607` : priceType→coin map natif. 5→lifelong ; 0xa→trésorerie ; 0xb→colosseum ; 0x13→war_coin (576) ; 0x14→alpaca (580) ; 0xc-0x11/0x15-0x17→`special_coins` 0-5/9-11 + push ZW30→client 78.
- **P0 lover coin (priceType 4) : pas de stockage → achat refusé (cohérent natif/G32).**
- Gains : enchants 2942/2943, DBA WarCoin/AlpacaCoin, GainColosseumGold.

### 7. Trade + étal
- `TradeHandlers.cs` : 36/37 request/accept (≤10u, 0x333), add/remove/gold/confirm/ready(91)/unconfirm(92)/cancel/reset(56). `active_trades`+`trade_items` tx.
- `Vending.cs` : 71/72/74/75/76/77/78/80 ; régions stall 600-609 de S001, ≤13 items, titre ≤91o ; achat atomique ; pas de broadcast 265 assis (désactivé — corps non prouvé).
- **Préparation : 2 sessions, items non liés, S001 région stall.**

### 8. Mort/revive
- `Combat.Death.cs` : PlayerDeathAsync :275-336 (305, 531 GOLDEN, 302) ; HandleReviveAsync :349-502 — types 0 savepoint (−10% exp), 1 sur place (−10%), 5 Locus leaf, 6 Life leaf ; HP/MP 10%, invincibilité Para349, désaggro ; pénalité désactivée en donjon ; savepoint `revive_area_id` persisté.
- **Préparation : `revive_area_id` non nul pour char test.**

### 9. XP/niveau/stats/sorts
- `Combat.Rewards.cs:470` GrantKillExpAsync → `ProgressionService.GrantExpAsync` (persist, cap S_Level), 284 flag 0 + vitals/stats refresh.
- `GrantClassPointAsync` :277-461 : player_classlist, +1AA/niv cap120, 284 flag 1, spells palier → player_spells, certificats /10, 489, RefreshJPAward.
- Livres : kinds 22/25 → `ItemActionHandlers.cs:432` LearnSpellAsync.
- Formules `NativeCombatFormulas.cs` testées. KB-04 : classes éveillées insérées lv1 au lieu de lv80.

### 10. Witchcraft 56/79
- NCID 19 : branche item-sur-item si `UseType 3|5` (col12) ; Field6 cible 0=sac/1=équipé/4=coffre.
- `ApplyWitchcraftAsync` :1075-1079,1230-1312 : ValidateWitchcraft (`InlayService.cs:197`), écrit `witchcraft`=itemId, 0x9f7+369, refresh si équipé, scroll consommé.
- KB-01 corrigé (NormalizeRow col6 FR).

### 11. Fenêtres métier
- PlayerAction 14 codes : 13→432[0][9] (fortify), 14→op10, 16→op0x68 (lifeskill), 101→op0x1c (cube) (`Strengthen.cs:98-150`).
- NPC services : 9/10→432+npcUid ; 11→483+0x3b ; 12-17→496 ; 31→612.
- 120 close : code==ShopOpType requis.

### 12. Contrats 0x2a
- `FamilyInstance.cs:691-739` HandleFamilyContainerPutAsync : gates op 0x3b + HouseOpen ; conteneur 1→0x29, conteneur 2→0x2a/0x52, conteneur 0 loc 0x33→subtype 0x16 ; insert `family_building_container`.
- Housekeeper auto :872-938 (NpcItemTid + FamilyNpcTid + template + ResolveHallSlot → insert 0x2a loc+1 + InjectHallNpc + 298).
- NCID 131 retrait.

## Risques consolidés

**P0**
1. Boutique lover coin (priceType 4) : aucun stockage → refus (cohérent natif).
2. Récolte par nœud : code mort — la voie = usage de l'outil.

**P1**
1. +14 : voie 100 % = chance-item 8002 ChanceUpper≥13 (pas le matériau) ; ChanceSafeLevel inutilisé ; échec détruit.
2. Vérifier présence des .ini dans DATA_DIR (S_FamilyCombine/Collect, S_LifeSkill, S_ItemChest/Combo, S_Enchant, S_Parameter 517-524, S_Store, S_Item, S_Buliding, 4PHouse).
3. Work : work_event non porté ; workId=CatalystItemId ; bâtiment hall requis.
4. Étal : S001 régions 600-609 uniquement ; pas d'apparence assis broadcast.
5. Trade : 2 sessions requises.
6. Contrats 0x2a : spawn housekeeper conditionné aux données.
7. Witchcraft : scrolls 56/79 requièrent UseType 3/5.
8. Fame shop : vérifier le type coin + créditer.
9. NCID 172 MW INCOMPLET (push ZW30→78 à valider monde connecté).

**OK** : lifeskill craft, cube, magicbox, mort/revive, XP/stats/sorts, fenêtres 432/483/496/612, EnchantParser KB-01.

## Checklist préparation live

- [ ] `player_characters.revive_area_id` renseigné ; `life_skill` du perso.
- [ ] `special_coins` crédité pour le fame shop visé.
- [ ] Items : pierres 40002-40019 (+40192/40193), chance-items 8002 ChanceUpper≥13, scrolls 56/79 UseType3/5, outil kind44 enchant 2933, catalyseurs c_familycollect/combine, ingrédients cube, combos magicbox, contrat 0x2a + bâtiment 0x29 non liés.
- [ ] Guilde HouseOpen=1 + hall instancié.
- [ ] `EDEN_DEV_WORK_CAP` / rates vérifiés.
