# D1 — Tables craft / racial / fusion / fortif — `corpus/fr-full/data/db/`

Légende : `c_*`/`C_*` = Big5 (noms mojibake, ids/valeurs lisibles) ; `t_*` =
CP1252 FR (index `1|…|` = numéros de colonnes **1-based** du schéma `c_`
associé). En-têtes `|V.x|N|` (pipe) ou `V.x,N,` (virgule). Comptages = records
IniTable (records multi-lignes réassemblés quand la table en a). Croisé avec
`misc-tables-notes.md`, `enchant-tables.md`, `schema-drift-audit.md`,
`growequip-index-notes.md`, `item-enchants-notes.md`, K1/K2/K4/K5/D2/D4 du
même dossier. **Aucune table `S_*` serveur dans fr-full** (GAP global, cf.
D2 §11) : les schémas `S_` cités viennent des parsers `eden_server` (oracle US)
et du désasm — les versions FR `c_*`/`C_*` divergent parfois de colonnes
(`schema-drift-audit.md` §2, J des misc-notes).

## 1. `c_lifeskill.ini` — `|V.1|36|` — **595 records** (ids 11-5077)

Recettes des **5 artisanats raciaux** (craft personnel, débloqué nv 30).
Schéma [VERIFIED désasm `CLifeSkillQuery::Deserialize` @0x86058d0,
`LifeSkillParser.cs:13-27`] :

| Cols | Rôle |
|---|---|
| 0 | id recette (clé QueryLifeSkill) |
| 1 | **groupe métier** (race) |
| 2 | **item produit** (production) / item consommé (analyse) → jointure `c_item` |
| 3 | nom Big5 |
| 4 | qté produite/consommée (+0x10) |
| 5 | niveau perso requis (+0x12 — Produce exige aussi ≥30 en dur) |
| 6-8 | exp base / var / cap maîtrise (+0x14/+0x18/+0x1c) |
| 9 | coût or (+0x20) |
| 10-11 | chaînage next/prev recette (+0x24/+0x28 — Forget exige prev≥2) |
| 12-20 | ≤3 matériaux `{id,nom,qté}` → `c_item` |
| 21+ | résultats `{id,nom,poids}` = **ids de recettes apprises** à pondération cumulée (pas des items) |

Contenu par groupe (comptage direct, col1) : **g1 = 246** (analyse
joaillerie, matériaux gemmifères 36792-36801 → recettes gemmes 1001-1106),
**g2 = 60**, **g3 = 79**, **g4 = 78**, **g5 = 132**. Mapping groupe↔race
[VERIFIED K1:11-17 + `ZoneEntryFlow.LifeSkill.cs:615-627`] : **1 Humain**
(joaillerie), **2 Zumi** (ingénierie), **3 Anuran** (alchimie), **4 Halfkin**
(broderie), **5 Ursun** (glyphes). Ex. ligne 2 `c_lifeskill.ini:2` :
`11|1|36792|檀香靈菇|7|30|15|10|200|20||1|…|1086|血紅寶石１階|25×4`.
`t_*` : **aucune** — noms Big5, traduction via `t_item` (items cités).
Jointures : c2/mats → `c_item`/`t_item` ; résultats → recettes lifeskill.

## 2. `c_itemcombo.ini` — `|V.4|42|` — **5 413 records** — `t_itemcombo` (index `1|3|` → col2 nom, « de Puissance »…)

**Affixes aléatoires** (préfixes « 力量的 ») + **combos de la Magic Box**
(pas la fusion — K4 §2c). Schéma [VERIFIED `CraftRecipeParser.cs:91-113` +
`ShopAndMiscParsers.cs:170-208`] : c0 id · c1 code groupe `G#####` · c2 nom
préfixe · c3 types d'items `17#18#…` · **c4 AttrType** · c5/c6 lvlMin/max ·
c7 tier · c8 poids · **c10 EnchantId** · c11 rank.
Contenu (col4) : 0 ×1505, 1 ×2123, 2 ×5 (affixes classiques, stats par
colonnes) ; **4 ×570 / 5 ×605 / 6 ×605 = 1 780 combos Magic Box** (arme/
armure/accessoire — `GetRandMagicBoxItemCombo` exige >3, K4 §5b) ; c10
enchant non nul sur **1 785 lignes** (ex. id 3904 `清晰思緒 I` → enchant
43292). Jointures : **c10 → `c_enchant`** ; c3 → kinds `c_item` ; c2 →
`t_itemcombo` (5 399/5 413 nommés, 14 vides 3620-3633).

## 3. `c_enchant.ini` + `t_enchant.ini` — hub des effets

- `c_enchant` : `|V.2|60|` — **18 487 records**, ids 11354-61655.
- `t_enchant` : index `1|6|56|57|58|` — ~17 480 lignes (≈94 % couverts).
- Schéma [VERIFIED intégral — `enchant-tables.md:34-99`, noms de colonnes
  issus du designer `Enchant.ini V.1|59`] : c0 id · c1 icône · c3 FX · c5 nom ·
  c6 type (1 buff/2 debuff) · c7 flags bitmask (0x10 can't-abandon, 0x100
  no-recast…) · **c8-35 = 4 blocs `指令 code + 6 vars`** (doubles, ex.
  `2134|rideKey|…|type` monture, `2003|HP`, `2084|ATQ|DÉF|ATQM`, `8007` taux
  fusion, `8020` parchemins enchant, `8022` catalyseur magicbox, `8023`
  transmission) · c36 intervalle · c37/38 poids haut/bas · **c39-55 = bloc
  轉移/proc** (slots déclenchés, événement, chance, durée, FX…) · c55 TIP
  desc · c58 stack max.
- ⚠️ **KB-01** [VERIFIED `schema-drift-audit.md:76-121`] : lignes FR =
  **61 cellules** (60 cols + `|` terminal) ; l'US insère une col elf-pet en
  index 6 → garde historique `row.Length==60` morte, corrigée par
  `EnchantParser.NormalizeRow` (`EnchantParser.cs:80-104`).
- Contenu craft : commandes 8007 (taux réussite fusion, 61 occ.),
  8002 (anti-casse fortif — absent d'item en données, K4 §1), 8020 (582
  parchemins d'enchant kind 56/58), 8022/8023 (transmission/magicbox),
  2134 montures (52), 2055/2132 CC/apprivoisement non résolus.
- Jointures (hub, `enchant-tables.md:230-247`) : `c_item`/`c_itemmall`
  **col23 UseEnchantId** (+ durée c24/25), `c_spell` c39, `c_classrankbuff`
  6e champ/palier, `c_title` c3, `c_itemcombo` c10, `c_equipset`, destiny,
  `c_sceneevent`/`c_party`, enchant↔enchant (1921/2144-2151/1821-1825).
  Résolution FR : `t_enchant` field2 = desc longue (gabarits `#N#`/`#B…$…#`
  résolus via `c_spellreference`).

## 4. `c_equiptrain.ini` — `|V.1|17|` — **100 records** (Mana Infusion)

Schéma [VERIFIED `EquipTrainParser.cs:12-28`, désasm `CEquipTrainQuery` /
`CLevelDBO::LoadEquipTrainExp` @0x83626a0] : c0 **niveau requis de l'item**
(= `c_item` col14, PAS le train level — subtilité native) · c1 EXP cumulée
i64 (décimale tronquée) · **c2-16 = 15 coefficients f32** dans l'ordre
HP,MP,ATK,M-ATK,ACC,ATK SPD,CRIT,Cast SPD,M-CRIT,DEF,EVA,Parry,Block,
G-Heal,P-Heal. Ex. `c_equiptrain.ini:2` `1|0|23.2|46.4|…`.
EXP train = charExp × S_Parameter 496 au kill (`EquipTrainService.cs:26-32`,
K4 §5c). Jointure indirecte : niveau requis item → `c_item` c14.

## 5. `c_itemmall.ini` + `t_itemmall.ini` — **7 461 records**, `|V.11|78|`

Même schéma que `c_item` [VERIFIED `ItemParser.cs:11-35` ; `t_itemmall`
index `1|8|35|` = nom c7 + desc c34 — 7 627 lignes t]. Contenu craft
(repéré via t_itemmall) : **pierres de sécurité fortif** — 40173 Halcyon
Safety Stone (+50 %, EN), 41469 Sublime (+100 %), 42227-29 Lucky Safety
Stone **Blueprints**, 49214/49918/52958-59 packs ; **aides craft** —
40172 Corona's Heartflame (+100 %), 40198 Gear Upgrade Crystal ;
**recettes mall « Fusion window »** — 40187-40191 Blueprints (Eternal
Chisel Lv30/40, Pet Food, Portal Stone, Shattergem) [VERIFIED t_itemmall
:891-997] — ouvrent la fenêtre de fusion (Arcane Box) au clic droit.
Jointures : **col23 → `c_itemmallenchant`** (ou `c_enchant`) ; col75 →
groupes cube ; col72/73 sockets ; col76 repairPrice → coût fortif
(K4 §1, `repairPrice×0.1+10`).

## 6. `c_itemmallenchant.ini` + `t_itemmallenchant.ini` — **1 399 records** (ids 55001-60424), `|V.2|60|`

Même schéma V.2 enchant que §3 [VERIFIED `enchant-tables.md:18`] — ids
**disjoints** de c_enchant, overlay additif obligatoire (sinon items mall
sans effet). Contenu : 305 montures cmd 2134, teintures 9009/9010
(55001+ « 服裝實驗染劑 »), enchants cash-shop. `t_itemmallenchant` index
`1|6|56|57|58|`, 1 208/1 399 couverts. KB-01 : mêmes 61 cellules FR —
même normalisation (`EnchantParser.cs:99-103`). Jointure : id ←
`c_itemmall` col23.

## 7. `c_itemchest.ini` + `t_itemchest.ini` — **1 538 records** (ids 46-10090), `|V.3|32|`

**Cube horadric / Arcane Box + coffres**. Schéma [VERIFIED
`ShopAndMiscParsers.cs:38-168`, désasm `CHoradricCubeQuery::Deserialize`
@0x85dbcd0] : c0 id · **c1 ingrédient principal → `c_item`** · c2 grade ·
c4 nom · c5 qualité aux requise (0 libre/1+exacte) · c6 qté main ·
**c7-18 = 4 sous-ingrédients `{id,nom,qté}` → `c_item`** · c19 or ·
c20 taux % · **c21 produit** (<100000 item direct / ≥100000 groupe →
pool `c_item` **col75** / vide-0 = pool des aux) · c22 lvl requis
(**999 = désactivée, 433 rows**) · c24 qté (cap 3) · c25-29 poids qualité
/10000 clés {0,1,2,4,6} · c30 param RandItemCombo · c31 set applicable.
Contenu [VERIFIED lecture directe] : ids **46-1300** = montées de qualité
équipement (main = item « type » 12xxxx, aux = `22537` 珊瑚晶砂 ×2/4/8 →
vert→bleu→violet, ex. `c_itemchest.ini:2-4`) ; **2796-3349** = fusions
consommables (3 potions → 1 supérieure, cuisine — le « cube » de K4 §2b,
ex. 3371 `29197×3 → 29198`) ; **3432-3434** = extraction d'essences de
classe (127365 → 29515) ; **10001-10090** = montées de costumes via
Cash Item 01-04 (48251-48254, taux 10-100 %). Distribution c21 :
576 groupe / 260 item direct / 702 vide (= pool aux/re-identification).
`t_itemchest` index `1|5|9|12|15|18|24|` — **1 961 orphelins t** (ancien
espace ids 1-45/1301-3402 [GAP documenté misc-tables §B]).

## 8. `c_collection.ini` + `Collection.ini` — archive & album

- `c_collection` : `|V.3|8|` — **7 970 records** : c0 = **item carte →
  `c_item`** · c1 nom · c2 flag spécial (compteur 1001 vs 1000) · c3 onglet
  (3 ×1637, 1 ×1108, 29 ×704, 24, 28, 4, 2…) · c4 ordre · c5 catégorie ·
  c6 locate · c7 points (`CollectionParser.cs:43-55`).
  ⚠️ Drift : FR `V.3|8|` vs US `V.4|9|` (+1 col groupe c8) → client
  `Group=0` partout (`schema-drift-audit.md:137-142`). `t_collection`
  5 225/7 970. **[GAP] `c_collectionpoint.ini` absente de fr-full** —
  enchants de seuils indéterminables (serveur lit S_CollectionPoint US).
- `Collection.ini` : `|V.2|7|` — **728 lignes / 271 ids** : album
  vignettes créatures (`40010|墨蝶|1|1|1|`, texte multiligne `$12$…`) —
  système distinct de c_collection, pas de parseur. [HYPOTHESIS] album
  cartes/familiers.

## 9. `C_Races.ini` — `V.1,4,` — **1 record** : `1,人,12,,`

Une seule race livrée (humain, 12 corps) — les autres races jouables
sont verrouillées côté données (`misc-tables-notes.md:91-93`). Impact
craft : les 4 artisanats non-humains de c_lifeskill existent en données
mais sans race jouable associée [HYPOTHESIS — races débloquées ailleurs
? smallMale/Zumi ont leurs tables corps].

## 10. `c_growequip.ini` — `|V.1|25|` — **1 664 records / 82 ids**

Équipements « Héritage » évolutifs — clé **(item_id, palier c2)**, le
palier = **niveau du porteur** (`CGrowEquipQuery::Deserialize`
@0x085fc720, `growequip-index-notes.md:30-55`) : c3/c4 hp/mp · c5-9
str/agi/int/wis/lck · c10-12 patk/matk/def · c13/14 hit/dodge · c15-18
crit-family · c24 block · **pas de colonne enchant** (enchants = gabarit
`c_item` col23). Contenu : 40055-40077 (mall, paliers 1-30),
41697-41725 (31-60), 48145-48173 (record unique stage 60), 24170
(c_item, épée 魔劍士, 1-75). Jointures : c0 → `c_item`/`c_itemmall` +
`t_item` ; sets `c_equipset` 94-96/187-189/244-246.

## 11. `C_Mask.ini` — `V1.0,14,` — **36 records [GAP parseur]**

Masques de visibilité sous-mailles de tête sous coiffes : c0 id, c1 code
coiffe `B2A…G5F` (B=garçon G=fille), c2-13 = 12 flags zones.
**Header `V1.0` non reconnu par `IniTable`** → 0 row parsée (même défaut
`S_Mask` US — `misc-tables-notes.md:449-458`).

## 12. `C_Lottery.ini` — `V.1,9,` — **280 records / 1 id (35677)**

Roue de loterie `{idLoterie, slot 1-8, groupe, palier 1-7, item prix c4,
qté, poids c6, ?, flag c8}` — **col poids c6 vide en FR** alors que
`S_Lottery` US la remplit [GAP données — `misc-tables-notes.md:176-183`].
Jointure : c4 → `c_item`.

## 13. `C_LuckyBag.ini` — `V.2,92,` — **1 record (32423 « 測試工具 »)**

Sacs cash-shop : ~30×{itemId, nom, poids} — seul un sac de test livré
[GAP contenu — `misc-tables-notes.md:185-188`]. Jointure → `c_item`.

## 14. `c_rewardscore.ini` — `|V.1|87|` — **14 records**

Épreuves « gladiator trial » (nodes `c_node` type 18, 655-674). Schéma
[VERIFIED `RewardScoreParser.cs:3-28`] : c0 id · c1 nodeTid → `c_node` ·
c2 bossDefId → `c_monster` · c3-5 = DSL `#:` (KillMonster/TeamDieNum/
PassTime/TotalScore→grade+loterie) · c6 flag (0 = tirage sans remise) ·
**c7+ = 20×{itemId, nom, qté, poids} → `c_item`**. Pas craft au sens
strict ; récompenses = pool d'items.

## 15. Autres tables craft/équipement repérées dans `ls data/db/`

| Table | Records | Rôle | Statut |
|---|---|---|---|
| `c_familycombine.ini` `\|V.1\|44\|` | 3 178 | recettes métiers guilde 15-17 + **stations renfort ids 1-20** (armes 1-10 / armures 11-20, taux c13 échelle ~2048) | [VERIFIED — **D2 §4** + `WorkOrderParser.cs:61-100`, `CraftRecipeParser.cs:66-89`] — ne pas dupliquer |
| `c_familycollect.ini` `\|V.2\|47\|` | 478 | ordres récolte VDG 12-14 + spots monde | D2 §3 |
| `C_ElfCombine.ini` `V.4,36,` | 859 | recettes craft **elfes** (mat→produit, or, scripts bonus `#id#id#poids#effet##`) | [VERIFIED fichier ; schema FR ≠ oracle `S_ElfCombine V.5,23` — `CraftRecipeParser` S_ **inapplicable** (`misc-tables-notes.md:231-234`)] |
| `C_ElfCombineElf.ini` `V.3,51,` | 457 | fusion elfe×elfe → nouvel elfe (coût 1250, résultat 36032…) | drift vs `V.4,53` US, aucun parseur |
| `C_ElfCollect.ini` `V.4,37,` | 24 | ordres collecte elfe (durée 180 s, liste `{itemId,?,taux}`) | drift vs `V.5,25` US |
| `C_Elf.ini` `V.5,36,` | 183 | templates elfes (stats, sorts c18/22-24, coût c34-35) | drift vs `V.7,42` US |
| `ElfTrain.ini` `V.1,30,` | 519 | entraînement elfes — ordres avec DSL `#:If_ElfMood…:ElfFamiliar + 15:…` | aucun parseur [HYPOTHESIS] |
| `ElfEvent.ini` `V.3,12,` | 283 | événements elfe (`1,1,1,99,99,20,3301,…`) | aucun parseur |
| `FarmEvent.ini` `V.2,3,` | 30 | récompenses elfe/paliers (`#:If_ElfLevel…`, items 35001-35005) | D2 §5 |
| `c_equipset.ini` `\|V.1\|18\|` | 380 ids | bonus de set (c3-13 items `;`, c14+ récompenses) — inclut sets héritage craftés | `misc-tables-notes.md:144-152` ; `t_equipset` 367/380 |
| `GuildFunction.ini` `V.1,12,` | 10 | fonctions par palier guilde 1-10 | D2 §2 |
| `c_store.ini` `\|V.1\|93\|` | 659 | boutiques PNJ (graines de nœud, croquis, parchemins recette 31736-31894) | D2 §8 |
| `lic.ini` | ~246 | **EULA** Big5 (« 第一章 總則 ») — PAS les licences craft | [VERIFIED — homonyme trompeur] |
| `Equip/EquipType/SuitEquip/NewEquip/ElfEquip/ELFSuit/ELFList.ini` | 3-21 | stubs `[INFO] Version=103` (EquipType = 21 slots) | quasi vides |
| `4pequip.ini` | ~12 788 | tenue→mesh/texture par code item (visuel) | hors scope craft |

## GAPs consolidés D1

1. **[GAP] Toutes les `S_*` serveur absentes** de fr-full (S_LifeSkill,
   S_ItemCombo, S_ItemChest, S_Enchant US, S_ItemMallEnchant,
   S_CollectionPoint…) — schémas connus uniquement via parsers/oracle US.
2. **[GAP] `c_collectionpoint.ini` absente** → récompenses archive
   inconnues en EDENFR (`misc-tables-notes.md:469-471`).
3. **[GAP] `C_Mask` inparseable** (header `V1.0`), `C_Lottery` poids
   trimmés, `C_LuckyBag` 1 sac test, `c_pvp` 0 record.
4. **[GAP] `t_itemchest` orphelins ×1961** (espace ids 1-45/1301-3402
   sans ligne `c_` correspondante — libellés de coffres possibles perdus).
5. **[GAP] Schémas `C_Elf*` incompatibles** avec les parsers `S_Elf*`
   serveur — re-décodage nécessaire avant tout usage (elfes = sprites
   crafteurs/récolte).
6. **[GAP] `ElfTrain`/`ElfEvent`/`Collection.ini` sans parseur** —
   layouts non décodés colonne par colonne.
7. **[HYPOTHESIS]** `EnchantParser` labels cols 37-39 encore nommés
   ProcChance/AppliedSpell* alors que le designer dit 作用間隔/高低權位
   (`enchant-tables.md:319-321` — Evaluate() = heuristique) ; codes
   8020/8022/8023/8002/2132/2055 non résolus par désasm.
8. Rappel KB-01 résolu : ne plus jamais comparer `row.Length` à 60 sur
   les tables enchant FR — utiliser `NormalizeRow` (61 cellules).
