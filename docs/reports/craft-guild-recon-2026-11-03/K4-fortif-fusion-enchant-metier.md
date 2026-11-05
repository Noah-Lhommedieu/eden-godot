# K4 — Fortification, fusion, enchants, Arcane Box, blueprints, gemmes

Périmètre : métier complet. Sources : serveur C# (portage désasm prouvé), corpus/index wiki EN-FR, guides FR, ref-shots. **Le serveur C# est la source la plus fiable** : chaque mécanique y cite son adresse native dans `zone/decompiled.c`.

## 1. FORTIFICATION (+1 → +14) — `Strengthen` / `strengthenwnd`

### Flux et fenêtre [VERIFIED]
- Ouverture : `PlayerAction` code **13** → `CStrengthenOp` (IsA 0x26) + `CNC_CZ_ClientEnterStrengthen` (432) `[i32 npcUid][s16 9]` ; aussi ouvrable via NPC-dialogue `CEC_OpenShop` type 8, npcUid=uid (`ZoneEntryFlow.Strengthen.cs:14-19, 102-103`). Fenêtre=code **9** partout (433/434/435 portent Field1=9).
- Requête : NCID **119** `CNE_CZ_ZoneServerStrengthenItem` : `[s16][i32 combineId][s16 sacCible][s16 slotCible][s16 sacMat][s16 slotMat][s16 sacChance][s16 slotChance][s16 rapide]` (`:196-211`).
- Cast : enchant **13** (StrengthenItemID), **0x14 (2 s) si rapide sinon 0x28 (4 s)** → 433 `[9][unités][0]` ; résolution à l'expiration (`:246-260`). Cancel → 434 `[9]`, fermeture NCID 120 `StrengthenClose` code 9 (`:519-529`).

### Coût et éligibilité [VERIFIED]
- Coût or = **`repairPrice × 0.1 + 10`** (`:222-229`, DAT_087cc618/620 ; repairPrice = S_Item col76).
- Cible : `EnchantLevel < 0xe` → **max +14** ; kind **0x10 exclu** ; combineId **1-10 = armes** (kind 0x11-0x21 + bouclier 0x22/34), **11-20 = armures** (`:474-489`).
- Matériau : **kind 46 ou EquipLoc 46** (les « parchemins » Power/Guard Scrolls — ids 40002-40010 armes, 40011-40019 armures dans les listes SStuff des recettes ; `CraftRecipeParser.cs:80-86`). Pierres **0x9d00/0x9d01 (40192/40193) refusées si cible ≤+9** → réservées au haut renfort (`:502-504`).
- Item de chance : slot optionnel, **kind 8002 (0x1f42)** — « aucun n'existe dans cette version des données → tout autre item rejeté » (`:506-516`).

### Taux de réussite [VERIFIED — désasm `DoStrengthenItem` @0x80a2b00]
Recette = `S_FamilyCombine` id 1-20 ; taux de base = **col13** appliqué seulement si le matériau ∈ SStuff de la recette (`:301-313`). Échelle native col13 ~2048/1024/512/256 (`CraftRecipeParser.cs:74-79`) ramenée au rand 0-100.

| Niveau cible | Formule |
|---|---|
| 0 | **100 %** (`:361` `level==0 → 100.0`) |
| 1-9 | `baseRate / 2^niveau` (`:343-344`) |
| 10-13 | **S_Parameter 517+lvl%10 armes** (kind 0x11-0x21) / **521+lvl%10 armures** (kind<0x11 ou bouclier 0x22) ; autres kinds → 0 (`:336-342`) |
| 14 | `EnchantLevel >= 0xe` refusé en amont → jamais tenté |

**Pitié** : `failCount × EquipAddition[lvl]` (armures kind 1-0x10/0x51) ou `[lvl+1]` (armes+bouclier), table rodata `EquipAddition` @0x87c3720 : `{0,0,0,5,3,1.53,0.5,0.15,0.04,0.01,0.003×5}` (`:42-46, 348-356`). failCount persisté via `TempStrengthenFailTime` mission (`:391-393`), remis à 0 au succès.

**Conséquence structurelle — le « point de sécurité » est émergent** [HYPOTHESIS solide] : avec `rate/2^lvl`, tout palier où `baseRate/2^lvl ≥ 100` est garanti. Le wiki FR donne : Power Scroll (armes) safe +4→+2 selon palier 20→70, Guard Scroll (armures) safe +5→+3 (`wiki-fr/pages/Amélioration_d'équipement.md:10-31`, note « lvl 70 augmentés, +4 à 100 % »). Cohérent avec des baseRate 512-4096. L'Aeria wiki confirme : « until the fifth level it has a perfect durability but later it has a chance of failure » (`pages-index.tsv:6431`).

### Échec : **DESTRUCTION**, pas de downgrade [VERIFIED]
- Sans protection : `EradicateItem` — l'item est **détruit** (remove + `275 ClientDropItem` + `435 [9][0]`) (`:436-455`).
- Avec protecteur kind 8002 : `435 [9][2]` + `failCount++` — item intact (`:439-441`) — **inaccessible en données actuelles** (kind 8002 absent).
- Succès : niveau+1, 436 `[cont][slot][niv][idx][0]` + 435 `[9][1]` + anim `PlayerAction 0x2b` aux voyeurs ; **annonce serveur texte 0x69d à partir de +8** (`%s,+%d,%s`) (`:385-434`).
- **Paliers 6/8/10/12/14 → `AddComboStrengthen`** (combos incrustés ajoutés — non modélisé dans ItemEntity) (`:387-390`).

### Pierres de sécurité [VERIFIED catalogue / GAP données]
- `Safety Stone`, `Halcyon Safety Stone`, `Lucky Safety Stone` (blanc) : « protect an item during fortification, to prevent it from breaking » (`pages-index.tsv:10422, 6535, 8092`).
- `Premium Safety Stone` / `Premium Lucky Safety Stone` (orange, cash shop) : item intact même en échec + **+7 %** taux (`:9748-9749`).
- `Viridian Safety Stone (NT)` : « protection de fortification +3~+6, lie l'objet » — vendue Item Mall (`ref-shots-catalog.md:561-562`).
- `Sanguine Fortunate Safety Stone` : **craftable à la Magic Pool** (commission LV11 : Blueprint ×1 + Sanguine Fragment ×40 + Infinity Core ×50 + Demonic Catalyst ×3, coût 1080 or, 10 s — `ref-shots-catalog.md:586-591`).
- Enchant commande **8002** = « 防爆符 » anti-échec (vars %/paliers, 64 occ.) — `enchant-tables.md:218`.

### ⚠️ Modèle concurrent à flag
`EquipmentEnchantService.cs` implémente un **autre** modèle : enchant +1..+20, +1..+10 garantis, armes +11..+14 param 517-520 (50 %), +15..+20 param 654-659 (25 %), armures 521-524 (100 %)/660-665 (50 %), échec = **downgrade -1** sauf protection (`EquipmentEnchantService.cs:6-51`). **N'est appelé que par des tests** — le modèle prouvé natif est celui de `ZoneEntryFlow.Strengthen.cs` (destruction). À réconcilier : le désasm dit « taux = S_FamilyCombine col13 si matériau ∈ SStuff, 10-13 → S_Parameter 517-520/521-524 » — les params 654-665 du second modèle (+15..+20) pourraient être le palier au-delà du cap 14 ou reliquat d'une autre version. [GAP]

## 2. FUSION — 3 mécaniques distinctes à ne pas confondre

### 2a. Station combine `combinewnd` (service 10, `CCombineOp` op 0x27) [VERIFIED]
- NCID **170** `PlayerWork` routé vers `CombineItem` quand op 10 ouverte : `[u32 recipeId][s16 catSac][s16 catSlot][s16 boostSac][s16 boostSlot]` (`ZoneEntryFlow.CombineStation.cs:31-39`).
- Recette : `S_FamilyCombine` **catégorie 6**, **exactement 4 matériaux** ; catalyseur = matériau[0] au loc exact ; booster optionnel **kind 0x1f41** (CChanceItem — aucun en données) ; or ≥ col9 ; ≥1 slot libre sauf si une pile consommée se vide (`CombineStation.cs:67-105`).
- Cast `CombineItemID` 0x28×100 ms → **roll rand%100 < col13 + bonus booster** ; succès → produit col10 ×1 + `435[10][1]` + anim 0x2c ; échec → `435[10][0]` + anim 0x2b ; **consommation dans tous les cas** (or + catalyseur qté mat[0] + matériaux[1..3] + booster ×1) (`CombineStation.cs:108-112`, `ZoneEntryFlow.CombineStation.cs:146-187`).

### 2b. Fusion boîte mystique (outil 1 « amélioration/fusion ») [VERIFIED guide FR]
`guides-fr/pages/la-boite-mystique.md:8-16` :
- 5 équipements bleus + cristal corail → 1 violet même niveau ;
- 3 ou 30 potions → 1 ou 10 potions niveau supérieur ;
- 5 ou 25 parchemins de garde/puissance → 1 ou 5 du niveau supérieur ;
- 100 potions + « Sac Vide de potion » (artisanat anuran) → Sac de Potion (1000) ;
- 2 cartes d'animaux même niveau → 1 supérieure ; esprits gardiens → statues supérieures ;
- prérequis : niveau requis du produit ; ≥3 slots libres ; multi-craft via « Produit ».

### 2c. `S_ItemCombo.ini` (|V.4|42|) — affixes aléatoires [VERIFIED schéma]
Ce n'est **pas** la fusion : c'est la table des combos/aléas d'équipement. Colonnes : `id | G-code (G00003) | préfixe nom | types items "17#18#19#" | AttrType (4 arme, 5 armure, 6 accessoire — `>3` exigé par `GetRandMagicBoxItemCombo`) | minLv | maxLv | tier | valeur(poids) | EnchantId col10 | rank col11` (`CraftRecipeParser.cs:91-113, 191-226`). `RandItemCombo` (roll à la création d'item) non porté (`WorkOrderService.cs:32-35`).

## 3. ENCHANTS D'ÉQUIPEMENT

### Application (sertissage par scroll) [VERIFIED — `UseItemToItemControl` cases 0x38/0x4f]
- Scroll **kind 56** (equip) / **kind 79** (costume) glissé sur cible : classe ∈ {arme, armure}, kind ≠0x51 ; **compatibilité par EquipLoc** : cible kind 0x10 → EquipLoc 6 ; kind 0x27 → EquipLoc 0xc ; paires **EquipLoc 7 → kind 0x13 (collier), 0xb → 0x14 (anneau), 9 → 0x23 (trophée)** ; EquipLoc 0 = universel ; sinon EquipLoc == kind cible ; kind 79 : EquipLoc 0 ou == EquipLoc cible (`InlayService.cs:197-235`).
- **Un seul enchant par pièce** : `witchcraft` clone+0x4c stocke le scrollId → même id déjà posé = refus (`:231-232`).
- Niveau : `charLevel < scroll.RequiredLevel` → texte 2552 ; textes 2550 « cannot be Enchanted » / 2551 « Enchantment added » (`:39-48`).
- Effet = `c_enchant` enchant_id cité par le scroll (hub : ≤4 blocs cmd+6 vars + bloc proc 轉移 ; `enchant-tables.md:7-12`).

### Typologie constatée [VERIFIED index EN]
- **Basic Enchants** (`Basic Enchant: X`, vert, plans-artisanat/Enchantment Formula) : familles Comprehension, Fast Singing, Muscle Explosion, Optimize, Recovery, Rehab, Slightness (+ autres) en Lv1-4+ ; consigne « items of Level N or above » (`pages-index.tsv:991-1023`).
- **Partial Enchants Lv1-8** (par slot) — `wiki-tables/C/Partial_Enchants_Lv1-8.md` : 20 familles par slot — HEAD: Sniper, Ancient Soul, Concentration, Titan ; BODY: Iron Wall, Healing, Steel Blade, Anti-Devil ; WAIST: Ice Flame, Nature, Chaos, Purification ; HAND: Beast Blood, Tree Soul, Broken Shell, Blessing ; LEG: Gale, Hide, Cloud Shadow, God Healing. Sources : Lv1 `One-Eyed Roger (Aven)` ou coffres `Partial Enchantment Chest I-VII` (Taen), Lv2-7 boss de trials/donjons nommés (par ex. Tuka Tufa Crimson T., Kassadin Slain T.), **Lv8 = Dragon Trials** (`:5-162`).
- **Enchants avancés craftés** (moitié droite de la même table + ref-shot `Formula: Leg Enchant - Gale Lv7`) : Work Level 6→14, matériaux `Obsidian Steel ×88 / Ice Storm Platinum ×94 / Fire Platinum ×100 / Golden Rock Ore ×106 / Abstruse Crystal Ore ×113 / Malleable Ore ×118 / Red Spar Ore ×124 / Crackling Russet ×149` + enchant de palier inférieur + encre (`Partial_Enchants_Lv1-8.md:5-162`, `ref-shots-catalog.md:275-284`). Guide FR : formules élaborées = enchant de base + compos mine/ferme + **gloire de la Curie** (`le-craft.md:10`).
- **Trophy Enchants Lv1-13** — 5 familles : **Attacker, Guardian, Healer, Magic Weaver, Sniper** (65 pages, `pages-index.tsv:12077-12141`) ; gate : Lv1 → trophée Lv30+, Lv13 → trophée **Lv90+** (pas +5/Lv). **Bonus verts ET malus rouges** : ex. Guardian Lv13 → DEF +1365, HP +463, Block +93, résist +9, All Elem Res +3, Malice +10 % **contre** ACC -8 %, G-Healing -83 (`ref-shots-catalog.md:332-338`).

## 4. CRAFT PAR BLUEPRINTS (bâtiments)

### Work types et tables [VERIFIED — `CWorkManager` porté]
- Types combine **15 = Alchemy Lab** (gemmes/potions, catalyseur kind 0x35), **16 = Foundry/Casting** (armes/armures, kind 0x36), **17 = Magic Pool/Enchanting** (anneaux/colliers/enchants, kind 0x37) ; collectes 12 mining (0x33), 13 planting (0x32), 14 summon (0x34) (`WorkModels.cs:139-160`).
- Recette `S_FamilyCombine`/`c_familycombine` (44 cols, 3275 recettes) : catégorie col1, **niveau bâtiment col2**, instant col5, durée col6 (min ×600 unités, ou brute si instant), usure col7/8, or col9, **produit col10**, qté col12, **taux col13**, malédiction col14 (toujours 0), matériaux triplets dès col15 — le parchemin/blueprint est lui-même un matériau (`WorkOrderParser.cs:61-100, 147-190`).
- Slots de travail : **10 + 1 secours** (index 0-10, CWork char+0xb20), 10 max collectes 12/13 (`WorkModels.cs:17-30`, `WorkOrderService.cs:168-186`).
- Roll succès : `rand%100 < (100 - max(0, (100-rate)×(1-decline%))) + ext_success` ; boosters ext_success kind **0x1f45/0x1f47/0x1f48**, ext_building **0x1f44/0x1f48** ; produit : sockets roulées (`RollSockets`), malédiction `rand()%1e8+1 ≤ curse_rate` (`WorkOrderService.cs:270-323`).

### Sources des plans [VERIFIED guide FR `le-craft.md`]
- **Plans orange + gemmes** : PNJ ville de guilde / Aven zone industrielle ; durabilité 3-7 jours posés par la guilde, taxe guilde (`:16-19`) ; requièrent 2 compos récolte (mine/ferme/invocation) + 1 compo gloire/drop (`:8`).
- **Plans gold** : donjons héroïques + PNJ de donjons + PNJ d'arène ; requièrent l'équipement orange crafté + compo récolte gold + drops gold (noyaux, cœurs) ; arène → médailles de classe, capes → pierres de guerre (`:9`).
- **Enchants** : base à Aven/VDG ; élaborés en donjons/maps/coffres de Taen ; élaboré = base + récolte + gloire Curie (`:10`).
- Index EN : 2 828 plans-artisanat ; préfixes `Blueprint:` (~2600, orange/gold, `Work Level 1-12` + matériaux), `Artisan Blueprint:` (gold avancé : base orange ×1 + drop + noyaux/cœurs ×N), `Formula:` (enchants, gemmes, potions), `Fusion Material`, `Town Blueprint`, `NPC`. Work Level max = **12** (0 occurrence de « Work Level 13 ») (`pages-index.tsv`).

### Bâtiments et PNJ [VERIFIED]
- **14 Town Blueprints** : Alchemy Lab, Altar, Brick/Bunker/Magic Hotel, Brick/Bunker/Magic Market, Farm, Foundry, Magic Pool, Mine, Mountain Chief Hall, Village Chief Hall — « Only guild leaders can use » ; production buildings « Accepts requests of Work Level 3 or lower » au rang de base (`pages-index.tsv:1454-3315`).
- **20 vendeurs de blueprints** (NPCs `decorations-guilde/plans-artisanat`, managed par le chef de guilde) : 17 collectors par type d'arme (Pistol/Axe/Bow/Cestus/Club/Dagger/Greatsword/Grimoire/Guitar/Hammer/Katana/Mace/Rapier/Shield/Staff/Sword + Dagger Merchant) + Cloth/Light/Heavy Armor collectors — produisent « Lv55-60 Blueprints » (`pages-index.tsv:521-11583`).
- **PNJ crafteurs à 3 rangs** par bâtiment — base / échec −10 % / échec −20 % : Foundry `Maître vétéran de moulage`/`Forgeron renommé` ; Magic Pool `Artisan`/`Maître artisan` ; Alchemy `Alchimiste`/`Maître alchimiste` (`le-craft.md:29-34` ; EN : `Alchemist Trainee`/`Alchemist` −10 %, `pages-index.tsv:212-213`). **Le % du PNJ s'applique sur celui du plan (multiplicatif, pas additif)** : plan 50 % → 55 % à −10 %, 60 % à −20 % (`:35`).
- **Aides craft** : essences +25 %, poussières +50 %, extraits +75 %, cœurs de flamme 100 % ; niveaux 35-95, utilisables sur craft de niveau ≤ ; cœurs de couronne = sans restriction (rares) (`:37` ; `wiki-fr/Craft.md:7`).
- Gate : bâtiment ≥ « niveau de travail requis » du plan ; durabilité /100, « assignation impossible » = réparation requise ; taxe de guilde variable (`le-craft.md:18-19`).
- Production gold : peut avoir **sockets + taux base >100 %**, aléatoire (`:25`).
- Compos : table complète work 1-12 (LVL20=1 … LVL90=12) ferme/mine/invocation + pensées/cendres/sangs/cœurs/noyaux par palier 30-90 (`compos-de-craft.md:39-137`).

## 5. ARCANE BOX / MAGIC CUBE — 2 fenêtres natives distinctes

### 5a. Boîte mystique classique = **Cube** (op 0x52, fenêtre 0x1c) [VERIFIED]
- Ouverture `PlayerAction 101` ('e') → `CCubeOp` + 432 `[0][0x1c]` ; débloquée par quête annexe lvl 30, icône boîte bleue du sac (`ZoneEntryFlow.Cube.cs:10-11`, `la-boite-mystique.md:5`).
- NCID **232** `VerifyCubeCosts` : `[u32 recipe][u16 n][sacs][u16 n][slots][s16 luckySac][s16 luckySlot]` ; cast `CubeCrafting` 0xf×100 ms (1,5 s) → `ExecuteCubeOp` (`:13-23, 65-131`).
- Recettes = `S_ItemChest.ini` (32 cols) : id, **ingrédient principal col1**, grade col2, nom col4, **qualité aux requise col5** (0 libre / 1 accepte qualité 0 / sinon exacte), qté main col6, **4 sous-ingrédients {id,nom,qté} cols 7-18**, or col19, **taux col20**, produit col21 (<100000 item direct / ≥100000 **groupe via S_Item col75** / 0 = pool = ids des aux), **niveau requis col22 (999 = désactivée)**, nom col23, qté col24 (**cap 3 stacks**), **poids qualité cols 25-29 ×10000 clés {0,1,2,4,6}** (0 = hérite qualité d'un aux random, 1 = blanc), param col30, set applicable col31 (`ShopAndMiscParsers.cs:38-168`).
- Les **aux non requis** (items posés hors recette) définissent le pool quand product==0 et donnent la qualité héritée — c'est le mécanisme « ré-identification » et « fusion 5 bleus → violet » : les 5 bleus = aux, produit = pool de même niveau, qualité roulée (`CubeService.cs:206-235, 260-283`).
- **Cube chanceux** : item dont UseEnchant = commande **8007** → params `{bonus %, levelCap}` ; levelCap ≥ niveau recette sinon texte 0x15af ; recettes 4567-4590 exigent bonus ≥1 sinon 0x21c4 ; toujours consommé (`CubeService.cs:168-183, 313-325`).
- Produit **lié** sauf si un input est libre ET sans flag no-trade 0x80 (`:152-157, 190-192`).
- Émis : 433/434/435 (0x1c), **572 ClientLoadCubeItems** (vecteurs sac/slot/qté), 274/275, textes 0x232/0x4f9 ; succès anim 0x2b, échec anims `-`/`/`/`*` (`ZoneEntryFlow.Cube.cs:21-31, 288-304`).
- Recettes documentées : 24+ formules « mystérieuses » (consommables PV/PM, pierre gravitationnelle, cristal alpaga, 15 équipements légendaires à taux 17-65 %) (`la-boite-mystique.md:33-177`, `wiki-tables/M/Arcane_Box.md:5-51`).

### 5b. Magic Box (op 0x58, fenêtre 0x1f — conversion/scouring) [VERIFIED — Vendetta]
- Service NPC 31 → `CMagicBoxOp` + `ClientOpenMagicBoxDialog` (612) ; NCID **257** `ExecuteMagicBox` : `[u16 type(1 conversion/2 scouring)][sacs][locs][mainBag][mainSlot][comboIds][mallBag][mallSlot]` ; cast `MagicBoxGoing` 1,5 s (`ZoneEntryFlow.MagicBox.cs:11-28`).
- **Conversion** : sacrifier des équipements éligibles (flags col11 : bit8 arme / 0x20 armure / 0x10 accessoire) accumule de l'énergie par bucket qualité `{0→0, 2→1, 4→2, 3→3, 5/magic→4}` ; énergie par type×bucket = S_Param 624-638 : armes `{0,0.1,0.3,0.4,0.6} ×niveau item`, armures `{1,3,6,0,20}`, accessoires `{1,3,6,0,20}` ; **seuils par étoile** S_Param 609-623 : armes `{500,1500,3600,7500,20000}`, armures `{8,32,128,405,1350}`, accessoires `{6,25,102,270,900}` ; masques qualité admise par type×étoile `QualityNeed[15]` (`MagicBoxTables.cs:23-60`, `MagicBoxService.cs:103-137, 223-259`).
- Au seuil → **star+1 (max 5)** ; aux étoiles **1/3/5** un **combo roulé** depuis S_ItemCombo (même kind, attrType=type+4, minLv≤reqLv≤maxLv, tier==star, poids `Value` cumulatif) — 3 slots combos max sur l'item (`MagicBoxService.cs:246-258, 321-339`).
- **Scouring** : reroll des combos choisis à `level=min(oldTier+1, star)` ; coût = N équipements valides : armes `{5,5,7,7,8}`, armures `{2,2,4,5,6}`, accessoires `{1,1,3,4,5}` (S_Param 639-653) ou catalyseur « wild » item-mall (`MagicBoxTables.cs:42-49`, `MagicBoxService.cs:292-315`).
- **Catalyseur mall** : enchant commande **8022**, 6 args `{énergie, %, nbStars, %, nbCombosScour, wild}` — boost énergie (dice<% → +montant), boost stars (dice<% → N level-ups), scouring multi-combos ou wild (`MagicBoxService.cs:47-82`).

### 5c. Mana Infusion / EquipTrain (système adjacent)
- `c_equiptrain.ini` (17 cols, 100 lignes, présent dans fr-full — `provenance/edenfr-verify:8700`) : EXP cumulée col1 + 15 coefficients d'attributs (HP/MP/ATK/M-ATK/ACC/ATK SPD/CRIT/Cast/M-CRIT/DEF/EVA/Parry/Block/G-Heal/P-Heal) **indexés par niveau requis de l'item**, pas par train_level (`EquipTrainParser.cs:12-28`).
- NCID 230 = sélection item entraîné, 570 = localisation ; **EXP train = charExp × S_Parameter 496** au kill (`EquipTrainService.cs:26-32`). Bonus d'infusion Aeria (armes gold 90 / costumes légendaires, 3 étoiles, 2 bonus, ~70 effets) : `guides-fr/infusion-de-mana.md:5-17` — **« n'existe que sur le serveur Aeria Games »** (NB ligne 6) → flag.

## 6. GEMMES

### Gemmes d'équipement (sertissage) [VERIFIED]
- Gemme **kind 45** (Spellstone) glissée sur arme/armure via NCID 19 `ItemUse` (UseItemToItemControl case **0x2d**) : cible classe {0,1}, `gem.GemLevel ≤ target.RequiredLevel`, **première socket libre gagne** ; textes 0xc1 succès / 0xc2 cannot inlay / 0xc3 no slots / 0xcce level too low ; `embedded[i] = item_id` template (`InlayService.cs:120-172`).
- **Sockets à la création** (`ChangeEmbeddedAmount` @0x809baf0) : armes toujours, armures kinds 1-16 seulement ; `max = MaxSocket&7` (clamp 7→6) ; jets **séquentiels** `rand%100 < SocketRate`, stop au premier échec ; modèle `embedded_amount` bits0-2 + `embedded_id1..6` (0 vide / −1 scellée) (`InlayService.cs:79-118`).
- Le roll `InlayItem` NPC (NCID 67, op 0x23 `inlay_shop`) est un **stub natif retournant 0** — feature morte ; le vrai sertissage passe par 0x2d (`ZoneEntryFlow.Strengthen.cs:564-588`).

### Typologie des gemmes [VERIFIED]
- **Artisanat humain** (Jewel Fusion) : débloqué lvl 30 ; gemmes « élémentaires » craftées chez un **alchimiste** par formule, puis barre d'XP pour améliorations **aléatoires 1/4 par type** (écrase le niveau précédent ; reset = recommencer toute la série) : 7 types (PV rouge sang, PM mana, FOR, AGI, INT, SAG, CHA) × Nv1-6 (lvl 30/35 → 80/85) ; variantes Bouclier(+Blocage)/Visée(+Parade)/Persévérance(+Déf)/Mérite(stat pure) + spécifiques (AGI→VitInv/VitAtq/Précision ; INT→crit mag ; SAG→soins ; CHA→crit phy/mag/précision) ; **gemmes spéciales** Nv1 (Cristal aventurier EXP+1 %, maître CP+1 %, arc-en-ciel rés+1 %, trésor butin+1 %) via Gemme enchantée (artisanat anuran 60) ; sacs de gemmes Nv1-3 via chemin de la destinée (`artisanat-gemmes-humain.md:5-250`).
- EN index : familles `STAT Plus/Match/Fast Cast/Ultimate Speed/Curse Gem Lv1-6` → Lv1=items 35+, Lv6=85+ (`pages-index.tsv:25-51`).
- **Monster Stones** (artisanat zumi) : 9 éléments (Fire/Ice/Thunder/Nature/Holy/Dark/Pierce/Slash/Strike), Basic + Lv1-4 ; résistance +1/+3/+5/+7/+9, Lv3-4 ajoutent proc 5 % (debuff déf/vitesses/ATK/ACC/crit/block 10 s) ; coût Enchant Crystals 2000/6000 + or 100-800 ; **Bind on equip** (`wiki-tables/M/Monster_Stones.md:9-53`).
- **Gemmes de métamorphose** : transformation joueur, qualité bleue/gold, non cumulables, debuffables (`gemmes-et-cartes-de-metamorphose.md:5-8`).
- Autres familles de gemmes (guides) : Tour des Cieux, Labo de Réno, 4Trial — `equipements.md:26-29` — 4trial = **flag Vendetta**.

## GAPs

1. **Modèle fortification double** : `EquipmentEnchantService` (+1..+20, downgrade) ≠ `ZoneEntryFlow.Strengthen` (destruction, max +14 prouvé désasm). Réconcilier : les params 654-665 pourraient être +15..+20 d'une autre version ; le code zone est la référence prouvée.
2. **Protection fortification introuvable en données** : kind 8002 (CChanceItem) et les pierres de sécurité (Viridian/Sanguine) sont décrites côté wiki/mall mais aucun item kind 8002/0x1f42 dans les données chargées → chemin « échec protégé » mort. Vérifier si les pierres EN itemmall (c_itemmall) existent en fr-full.
3. **combineId 1-20 ↔ palier de scroll** : la correspondance exacte recette ↔ (tier de scroll, cible de niveau) n'est pas décodée — les listes SStuff par recette définissent les scrolls admis mais le mapping « Lv20 scroll → recettes 1-4 » n'est pas extrait des données (probablement 10 tiers d'arme × +10 tiers d'armure).
4. **Bonus chiffrés de fortification** (+N → stats cyan) : non localisé (probablement table addattr/param non nommée — à chercher dans S_Parameter ou `c_item` addattr).
5. **Safe point** : émergent de `rate/2^lvl` (hypothèse forte, non confirmée par désasm direct du seuil « no-fail ») — le désasm montre juste `rand<total` sans branche « safe point » séparée.
6. **Mana infusion Aeria** : le système « 3 étoiles / 2 bonus » FR est flaggé **Aeria-only** par la source FR elle-même ; le Magic Box serveur natif (conversion/scouring/5 étoiles/3 combos) diffère → à trancher selon version EDENFR.
7. **Enchant 8022 mall catalyst / cube chanceux 8007 / booster 0x1f41/0x1f44-48** : ids d'items concrets non extraits des tables (kind connus, items absents ?).
8. **Fusion métier 2b (5 bleus→violet)** : présentée par la boîte mystique — implémentée via recettes S_ItemChest côté serveur (product=group+aux pool) ; l'id exacte de la recette 5 bleus→violet n'est pas citée nominativement.
9. `S_ItemCombo` — consommateurs au-delà du MagicBox (`RandItemCombo` à la création d'item, affixes aléatoires) non portés.
10. `guides-fr/pages/recolter-les-coffres-darmures-68-et-73.md` — page mentionnée dans le brief absente de `guides-fr/pages/` (la plus proche : `boites-darmures-des-elites.md` — coffres d'armures = élites régionales, contiennent sets oranges + formules d'enchantement, `equipements.md:14`).

**Actions non réalisées par l'agent** : lire `raw/wiki-en/*.html` (inexistant dans ce repo — seul l'index TSV existe), extraire les ids de pierres de sécurité depuis `c_item.ini` (faisable via IniTable Big5), lancer `reindex` (pas d'exec pour cet agent).
