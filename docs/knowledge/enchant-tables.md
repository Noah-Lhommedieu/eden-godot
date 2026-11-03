# Tables d'enchant — `c_enchant.ini`, `t_enchant.ini`, `c_itemmallenchant.ini`

Statut : **verified** pour le schéma (noms de colonnes natifs + recoupements
données) et les codes cités avec preuve ; **hypothesis** pour les codes non
résolus du dispatch. 2026-11-06.

L'**enchant_id** est le hub de tous les effets du jeu : buffs d'expertise
(`c_classrankbuff`), effets d'usage des items (`c_item` col23), procs d'armes,
montures (commande **2134** + `ridelist.ini`), titres, collections, sets,
destinées, sorts résultats, debuffs de mobs… Tout pointe vers une ligne
d'enchant : icône + nom + type + flags + **≤4 blocs de commande** + bloc
« 轉移 » (proc) + textes + plafond de stack.

## 0. Sources lues (toutes en lecture seule)

- `corpus/fr-full/data/db/c_enchant.ini` — **`|V.2|60|`, Big5, 18 487 lignes**.
- `corpus/fr-full/data/db/t_enchant.ini` — format Count `5|`, **CP1252**, ~17 477 lignes.
- `corpus/fr-full/data/db/c_itemmallenchant.ini` — même schéma V.2, **1 399 lignes** (ids 55001-60424, disjoints de c_enchant — 0 recouvrement).
- `corpus/fr-full/data/db/t_itemmallenchant.ini` — 1 257 lignes (couvre 1 208/1 399 ids mall).
- `corpus/fr-full/data/db/ridelist.ini` — rideKey → `DB<n>=r<key>, r<modèle>.kfm, r<modèle+variante>.dds`.
- `eden_server/server_us/Data/db_decoded/S_Enchant.ini` — **`|V.3|61|`, 19 829 lignes** (oracle US).
- `eden_server/server_us/Data/db_decoded/Enchant.ini` — **la clé du schéma** : copie « designer » UTF-16 `V.1|59` (387 lignes) dont la **ligne d'en-tête nomme les 59 colonnes en Big5** (`編號|ICON圖號|…|堆疊上限`).
- `eden_server/server_us/Data/db_decoded/T_Enchant.ini` / `T_ItemMallEnchant.ini` — oracles texte.
- `eden_server/server/src/Eden.Core/Data/Parsers/EnchantParser.cs`,
  `Data/EnchantCommands.cs` (dictionnaire numéro→nom extrait par objdump de
  `CSpellDBO::DecodeEffectCommand` zone @0x8380ad0),
  `Game/Services/EnchantService.cs` (2134/Ride, flags, stacks).
- `eden_godot/godot_project/scripts/data/tables/EnchantTable.cs` (parseur
  client — miroir `src/Eden.Core/Data/tables/EnchantTable.cs`).
- `docs/knowledge/native-constants.md` §1 (rankbuff), `artifacts/corpus-inventory/class-rankbuff-notes.md`.

## 1. Format de ligne — schéma et versions

Trois versions du MÊME schéma (le designer `Enchant.ini` donne les noms) :

| Version | Colonnes | Différence |
|---|---|---|
| `Enchant.ini` designer `V.1|59` | 59 | fichier d'origine : types/flags en **libellés** (`正性`, `陣亡保留`) |
| `c_enchant.ini` **V.2\|60** (FR, Reforged `c_`, `c_itemmallenchant`) | 60 | mêmes positions, **valeurs numériques**, +1 col finale (59) |
| `S_Enchant.ini` **V.3\|61** (serveur US) | 61 | V.2 + **colonne groupe elf-pet insérée en index 6** |

Conséquence : en V.2/designer les positions sont **identiques** ; en V.3 tout
champ ≥6 est décalé de **+1**. Les parseurs gèrent ça en réinsérant une cellule
vide à l'index 6 quand la ligne fait 60 colonnes (`EnchantParser.Parse`,
`EnchantTable.Load` : `s = row.Length == 60 ? -1 : 0`).

### Colonnes (index V.2 = designer ; V.3 = +1 à partir de l'index 6)

| V.2 | V.3 | Nom designer | Rôle |
|---|---|---|---|
| 0 | 0 | 編號 | **enchant_id** (clé primaire, jointure partout) |
| 1 | 1 | ICON圖號 | code icône (`I01488`, `E0302`, `J515`… → `UI/skillicon/`, repli itemicon/uiicon) |
| 2 | 2 | BUFF動態 | dyn-effect de l'icône de buff (ex. 507) |
| 3 | 3 | BUFF特效 | effet visuel du buff (réf. `s01151`…) |
| 4 | 4 | BUFF位置 | ancre de l'effet |
| 5 | 5 | 名稱 | nom (Big5) |
| — | 6 | *(ajout V.3)* | **groupe elf-pet** (`enchant+0x94`, `ElfPetEnchantGroupMap` — groupes 6-11 = livres 祈願 ; absent de tout fichier V.2) |
| 6 | 7 | BUFF類型 | **type** : `1` = 正性 (buff), `2` = 負性 (debuff) → `SEffectData+8` ; natif : bit0 annulable par clic droit, bit1 = filtre dispel (`CancelPlayerEnchant` @0x805fe00, `CCheckBufType` @0x0852f2e0) |
| 7 | 8 | BUFF旗標 | **flags** (bitmask — §1.2) → `SEffectData+0xc/+0xd` |
| 8-14 | 9-15 | 指令1編號 + 變數1-6 | **bloc commande 1** : code effet + 6 vars (doubles) |
| 15-21 | 16-22 | 指令2… | bloc commande 2 |
| 22-28 | 23-29 | 指令3… | bloc commande 3 |
| 29-35 | 30-36 | 指令4… | bloc commande 4 (**4 blocs max**, `CEnchantQuery::Deserialize` @0x85ecf00 : `reserve(4)`) |
| 36 | 37 | 作用間隔 | intervalle d'action (tick des effets périodiques — ex. `20`) |
| 37 | 38 | 高權位 | **type/poids haut** → `SEffectData+0x112`, clé de `CEnchantMgr::FindByType` (dédoublonnage par type — `EnchantService.GetTypeId`) |
| 38 | 39 | 低權位 | **grade/poids bas** → `+0x114`, priorité de remplacement (`GetGrade`, `RemoveEnchantByType`) |
| 39 | 40 | 轉移指令 | **slots déclenchés au proc** : liste `;` 1-based des blocs 指令N déplacés du set passif vers le set déclencheur (designer écrit « 指令1 », le fichier livré « 1 ») |
| 40 | 41 | 轉移方式 | **événement déclencheur** : 1/2 attaque (self/opponent), 3/4 coup reçu, 5 cible, 6/7 kill, 8/9 être tué (designer « 打人對方 » → 2) |
| 41 | 42 | 轉移機率 | chance du proc % → `SEffectData+0x115` |
| 42 | 43 | 轉移時間 | durée du sous-buff déclenché (s) → `+0x120` |
| 43 | 44 | 轉移間隔 | intervalle du sous-buff |
| 44 | 45 | 轉移ICON | icône du buff déclenché (code J513…) |
| 45 | 46 | 轉移類型 | type du buff déclenché (`負性`→2) |
| 46 | 47 | 轉移旗標 | flags du buff déclenché (même vocabulaire que col7 — ex. `80` = 0x50) |
| 47 | 48 | 轉移動態 | dyn-effect du buff déclenché |
| 48 | 49 | 轉移特效 | FX du buff déclenché (`s01151`) |
| 49 | 50 | 轉移位置 | ancre |
| 50 | 51 | 轉續特效 | FX continu |
| 51 | 52 | 轉續位置 | ancre FX continu |
| 52 | 53 | 轉移冷卻 | cooldown |
| 53 | 54 | 轉移高位 | poids haut du buff déclenché |
| 54 | 55 | 轉移低位 | poids bas du buff déclenché |
| 55 | 56 | **TIP說明** | **description longue** (Big5 + gabarits `#N#`/`#B…$…#`) — ce que `t_enchant` localise |
| 56 | 57 | 轉移TIP | TIP du buff déclenché → `t_enchant` **field3** (« P-CRIT +189 ») |
| 57 | 58 | 轉移名稱 | nom du buff déclenché → `t_enchant` **field4** (« 昏迷狀態 », « Radiant Phoenix ») |
| 58 | 59 | 堆疊上限 | **MaxStack** (défaut 1 — `LoadAllEffectData` @0x08394b10, `+0x12e`) |
| 59 | 60 | *(hors V.1)* | champ ajouté post-designer — 106 lignes remplies en FR (valeurs 1-10), rôle non décodé |

⚠️ Les libellés C# historiques divergent du schéma designer sur cols 36-39
(V.3) : `EnchantParser` nomme `ProcChance`/`AppliedSpellId`/`AppliedChance`
ce que le designer appelle 作用間隔/高權位/低權位, et `SpellId1`/`SpellId2`
(cols 23/24 V.3) = le bloc 指令3編號+變數1 (souvent `1823|id` = EnchantKills).
Les champs déclencheur (cols 40-43 V.3 = 轉移指令/方式/機率/時間) sont en
revanche alignés et datovés natif.

**Pas de colonne « durée » de l'enchant lui-même** : la durée de l'enchant
actif vient de l'appliquant — `EffectToLife(durée −2 = permanent)` pour les
rankbuff, `c_item` cols 24/25 pour les effets d'usage d'item, 轉移時間 pour le
sous-buff d'un proc. 作用間隔 (36) = la période de tick des effets récurrents.

### 1.1 Blocs commande : `指令N編號` + 6× `指令N變數`

- 4 blocs aux positions 8/15/22/29 (V.2) — **un slot vide au milieu n'est pas
  fatal** (le natif itère la carte complète ; `GetUseCommands` saute le slot).
- Les vars sont des **doubles** (« 0.5 », « -2.5 » existent — jamais de
  troncature int).
- **Cmd 160** = wrapper : `160|sousId|a1..a5` se relit comme `sousId` + 5 args
  (observé : `160|2085`, `160|2066`, `160|2150`).
- Deux lectures coexistent : positions strictes (`9+7k` V.3 — usages, procs)
  et **scan libre** des cellules (`RideKeyOf`/`GetRideInfo` cherchent le code
  2134 partout, params = cellules suivantes — même règle natif).

### 1.2 Flags (col7 V.2 / col8 V.3) — bitmask

| Bit | Sens | Preuve |
|---|---|---|
| 0x10 (16) | **can't-abandon** — pas de retrait clic droit | natif `CancelPlayerEnchant` @0x805fe00 |
| 0x100 (256) | **no-recast** — recast refusé silencieusement | natif `CEnchantMgr::Add` @0x08569a50 |
| 0x20 (32) | 陣亡保留 — survit à la mort | designer (libellé ↔ numérique, 83 lignes) |
| 0x40 (64) | 王怪無效 — sans effet sur boss | designer |
| 0x80 (128) | 擊中失效 — expire au toucher | designer |
| 0x08, 0x200, 0x400, 0x2000… | 攻擊/受傷失效 et autres — non mappés 1:1 | valeurs observées (8192, 8208, 8448, 8848…) |

Les flags du buff déclenché (col46) partagent le même vocabulaire (`80` =
0x50 = can't-abandon + boss-immune).

Type (col6 V.2) : `1` = 正性/buff, `2` = 負性/debuff (designer + natif ; une
ligne FR anormale porte `512` — ex. id 46131 qui a aussi 59 colonnes au lieu
de 60, bizarrerie de donnée).

### 1.3 Exemple complet — enchant 11354 (monture 戰鬥座騎)

Ligne V.2 (`|` séparé, cellules vides omises) :

```
11354|I01488||||尊貴永日狐王戰鬥座騎|1|0|2134|253020||1|2|||2051|90||||||2002|7||||||2083|2|1|…|0|…|騎乘狀態下，跑速提升90%，HP+7%、攻擊值+2%、防禦值+1%。|…
```

| Col | Valeur | Lecture |
|---|---|---|
| 0 | 11354 | id — cité par `c_item` col23 de l'item monture |
| 1 | I01488 | icône |
| 5 | 尊貴永日狐王戰鬥座騎 | nom (FR : « Kitsune de l'Aube (Légendaire) » via t_enchant) |
| 6 | 1 | BUFF類型 = buff |
| 7 | 0 | flags : aucun |
| **8-14** | `2134|253020||1|2||` | **指令1 = Ride** : var0 rideKey=**253020**, var1 flag=0, var2 sièges=1, var3 type=**2** (monture de combat — le ride survit à l'engage ; 0 → `RemoveEnchantByState(9)`) |
| **15-21** | `2051|90|||||` | 指令2 = AttrMovement : **vitesse +90 %** |
| **22-28** | `2002|7|||||` | 指令3 = AttrMaxHPRate : **PV max +7 %** |
| **29-35** | `2083|2|1|||||` | 指令4 = AttrPhysicoDamageRate : var0 **ATQ +2 %**, var1 **DÉF +1 %** (var2 = M-ATQ, vide) |
| 46 | 0 | 轉移旗標 |
| 55 | 騎乘狀態下，跑速提升90%，HP+7%、攻擊值+2%、防禦值+1%。 | TIP說明 — décrit exactement les 3 blocs |

`rideKey 253020` → `ridelist.ini` : `DB131=r253020, r253.kfm, 1.00, 1.00,
r25302.dds, …, 2.5` — modèle `r253.kfm`, texture `r25302.dds`, échelle 2.5.
Le clic droit sur l'item **bascule la monture** au lieu de consommer l'objet
(côté client : `EnchantTable.RideKeyOf` ; côté serveur : `EnchantService.GetRideInfo`
— sinon la monture était **mangée**, bug live corrigé). 52 enchants 2134 dans
c_enchant FR + **305 dans c_itemmallenchant** (montures cash shop, ex. 55850 =
`2134|253010`).

## 2. Dictionnaire des codes effet (指令編號)

Table numéro→nom extraite par **objdump** du dispatch de
`CSpellDBO::DecodeEffectCommand` (zone @0x8380ad0) — `EnchantCommands.Names`.
189 codes distincts en EDENFR ; les plus fréquents :

### Codes clés (verified désasm sauf mention)

| Code | Nom | Sens / vars |
|---|---|---|
| 2003 | **AttrMaxHP** | var0 = PV flat (rankbuff) |
| 2014 | **AttrMaxMP** | var0 = PM flat |
| 2002 / 2013 | AttrMaxHPRate / AttrMaxMPRate | % plafond PV / PM |
| 2001 / 2011 | AttrMaxHPPlus / AttrMaxMPPlus | plafonds + |
| **2047** | **HealPlus** (arg 4/5) | **soin** — var0 flat (rankbuff « Soin ») ; 2038/2039 = soins prodigués/reçus |
| **2084** | **AttrPhysicoDamage** (attr selon var) | var0 = ATQ, var1 = DÉF, var2 = ATQM (rankbuff : `2084|78` ou `2084|||809`) |
| 2083 | AttrPhysicoDamageRate | var0 ATQ%, var1 DÉF%, var2 ATQM% |
| 2082 | AttrDefence | var0 DÉF, var1 PRÉC, var2 EVD, var3/4 dégâts |
| 2086 / 2085 / 2081 | AttrBase (flat/%/par-niveau) | vars 0-4 = FOR, AGI, INT, SAG, CHA — titres/rankbuff |
| 2087 / 2088 | AttrHitRate / AttrHitRateRate | var0 PRÉC, var1 EVD |
| 2048 / 2040 | AttrCriticalRate | crit plat / % ; 2043/2044 = crit dmg |
| 2051 | **AttrMovement** | % vitesse de déplacement (montures) |
| 2056/2058/2068 | AttrAttackSpeedPlus 0/1/2 | vitesse d'attaque |
| 2053 | AttrCastTime / AttrWeaponCastSpeedRate | vitesse d'incantation |
| 2095 / 2101 | AttrResist | résistances élémentaires / physiques (par var) |
| 2102-2104 | AttrWeaponDamageRate | dégâts par type d'arme (maîtrises) |
| 1031/2031/2032 | MultiStroke | multi-coups (`[chance1,coef1,chance2,coef2]`) |
| 2033/2034/2036 | DamageAddPlus(+) | bonus dégâts lus par la formule |
| **2134** | **Ride** | `rideKey|flag|sièges|type` → `ridelist.ini` |
| 2140 / 1122 | RideSpell | buffs monture de combat (≤6 ids) |
| 2131 / 2136 | Transforms / TransformPlus | transformation |
| 2133 | ModelAlpha | alpha du modèle |
| 2135/2137/2138 | LevelCheck(Plus) | conditions de niveau |
| 1921 | EffectNextID | `enchantId|chance` — chaîne vers un autre enchant (procs) |
| 1821-1825 | EnchantKill(Random[Force])/EnchantKills/EnchantIncrease | dispels |
| 3101/3102/3103 | EnchantCheckByType / EnchantCheckBuffStack | conditions sur enchants |
| 2144-2151 | OnAttackBuff*/OnBuffEndBuff | procs à l'attaque/crit/blocage |
| 7003 | Effect2Target | effet reporté sur la cible |
| 1074/1075/2045 | SummonInEventArea | invocations |
| 2902-2945 | ItemDropRateUp, AttrGainExp, GainCoin… | drops/XP/monnaies |
| 3002/3011/3012/3013/3024/3025 | HP/MP(+)(Rate/Attr) | variantes restauration |
| 4032 | AttrSkillPoint | points de compétence |
| 5001-5012 | ElfCollect*/ElfCombineRate | elfes |
| 6001-6038 | MusouCheck, ItemReCombo, LifeSkillGain, ChangePet*Spell | vie/pet |
| 7001-7006 | SummonEquipPet, ItemElfExpOnce… | pet/équipement |
| 8016/8018 | EquipReCombo / GoldItemPlus | équipement |
| 9022/9023/9101 | OpenShop / RoomExtend / UseTwoWeapon | UI/fonctions |

### Codes rencontrés NON résolus (absents du dispatch objdump) — hypothesis

| Code | Occ. | Indices d'usage (FR) |
|---|---|---|
| **2055** | 997 | **états négatifs / CC** : 6 vars = un état par position (« Paralyse », « Étourdit et empêche toute action », « empêche la magie » var3). Familles candidates : `CEC_Stun`, `CEC_Sleep`, `CEC_Root`, `CEC_StateNoMove/NoAttack/NoUseSkill/…` |
| **2132** | 423 | apprivoisement : 6 vars = ids d'apparence monstre (`025014`…) — « Monstre apprivoisé ». Candidat `CEC_MobTransform`/`CEC_Transform(s)` |
| 9019 | 129 | quête accomplie : `1|6945` (« 完成任務 L12.… », 6945 = id mission ?) |
| 2046 | 90 | immunités : vars ±999999 par canal (« Immunités aux attaques ») |
| 9009/9010/9014/9015 | ~250 | **teintures** (vars = indices couleur — « 染劑 » costume/chapeau/corps) |
| 8002 | 64 | protection raffinement (« 防爆符 » anti-échec, vars %/paliers) |
| 8007 | 61 | **taux de réussite fusion/craft** (« Fusion +30 % », `30|75`) — lu par le service de fusion (ShopAndMiscParsers) |
| 8023/8022 | ~100 | parchemins de transmission d'équipement (« 傳承卷 ») |
| 2118 | 44 | aura défensive combinée (« Parade +#B41$200# et DÉF +#B26$100# ») |
| 2139 | 29 | vars 60-170 ×4 (« 大頭電波器 » gadget taille ?) |
| **2147** | 29 | **proc sur blocage** : `chance|enchantId|%` (« Quand Blocage est déclenché… ») — candidat `CEC_OnAttackBuffBlock` |
| 2057 | 24 | vitesse d'incantation (« VIT COMP +#B30$200# », famille 2056/2058) |
| 6006 | 23 | bonus groupe maison/fête (« 茶會/居家宴會 ») |
| 2062 | 19 | **anti-soin** (« la cible ne peut recevoir aucune guérison », var0=%) |
| 2154 | 19 | **changement de taille** (« Réduit de 50 % », « Grandit de 200 % ») |
| 9018 | 46 | déblocage série légende/quête (var0 = id série) |

## 3. Jointures — qui cite un `enchant_id`

| Table | Champ | Usage | Preuve |
|---|---|---|---|
| `c_classrankbuff.ini` | **6ᵉ champ de chaque palier** (×22) | Class Ability Bonus — `RefreshJPAward` applique l'enchant en durée −2 ; les 5 champs stats = affichage seul | zone @0x085c4430, `native-constants.md` §1 |
| `c_item.ini` / `S_Item.ini` | **col23** (`UseEnchantId`) + durée col24/25 | effet d'usage/proc de l'item — **montures, certificats, parchemins** | `ItemParser` (20051→58991) ; tooltip `BuildCertTip` |
| `c_itemmallenchant.ini` | — | **même schéma**, ids disjoints : enchants des items cash shop (305 montures 2134) ; sans elle l'item mall est mangé | `EnchantService.LoadFromDataDir`, `EnchantTable.EnsureDefs` (overlay additif) |
| `c_spell.ini` | col39 `ResultEnchantId` (+ col40 chance) | enchant appliqué à l'impact du sort + texte du tooltip | `SpellParser`, `SkillCast.ResultEnchantId` |
| `c_title.ini` | col3 | bonus du titre actif (ex. 59465 = 2002\|3+2013\|3) | `TitleTable` |
| `c_collection.ini` | seuils 5×(key,threshold) + enchant | enchant accordé à seuil de collection | `CollectionParser` (+0x0c) |
| `c_equipset.ini` | récompenses (item, enchant) | bonus de set — enchant résolu par `QueryEquipSetEnchant` | `EquipSetParser` |
| `c_destinyevent/road` | type 5 | effet destinée = enchant | `DestinyDataParser` |
| `S_CraftRecipe`/magicbox | col10 + `magicbox_enchant1-3` | enchant porté par les combos | `CraftRecipeParser` |
| progression (level-up) | col12 + durée col13 | cadeau de niveau | `ProgressionParsers` (+0x20/+0x24) |
| `c_monster` / `MonsterList` | cols 40-49 | refs brutes sorts/enchants/classdrops des mobs | `MonsterParser` → `MonsterService`, registre `ZoneEntryFlow.MobEnchants` |
| enchant↔enchant | vars des commandes | `1921|enchantId|chance`, `2147|pct|enchantId|…`, 1822-1825 (dispel par id/type), 3101-3103 (conditions), col6 V.3 (groupes elf-pet), bloc 轉移 (icône/type/flags du sous-buff) | désasm cité |
| **licenses / certificats** | via `c_item` col23 | les « certificats » (page License Knowledge) sont des **items** — pas de table enchant dédiée | `BuildCertTip` → `UseEnchantId` |

L'overlay côté serveur : `S_Enchant` + `S_ItemMallEnchant`, puis les fichiers
client `c_enchant`/`c_itemmallenchant` **remplacent** les lignes de même id et
ajoutent les nouveaux (`EnchantService.ApplyClientOverlay` — politique
« canonique client », désactivable `EDEN_REFORGED_DB=off`). Côté client Godot,
`c_itemmallenchant` est **additif** à `c_enchant` (les montures tombent sinon
en « Item »).

## 4. `t_enchant.ini` — gabarits et rendu

Format Count : en-tête `5|`, ligne d'index **`1|6|56|57|58|`** = numéros de
colonnes **1-based du schéma designer** : col1 = id (jointure), puis les
textes des colonnes 6 (名稱), 56 (TIP說明), 57 (轉移TIP), 58 (轉移名稱) :

| Champ t_enchant | Colonne designer | Contenu | Lecture client |
|---|---|---|---|
| field1 | col6 名稱 | nom affiché | `NameOf` |
| field2 | col56 TIP說明 | **description longue** (gabarit) | `DescriptionOf` — texte vert « effet » des tooltips item/cert |
| field3 | col57 轉移TIP | texte du buff/proc **actif** (« P-CRIT +189, EVA +189 ») | `ActiveTextOf` — tooltip de l'icône de buff |
| field4 | col58 轉移名稱 | nom du buff déclenché (« Radiant Phoenix », « Simurgh's Claw ») | (rare — 1 349 lignes) |

(Même convention pour toutes les `t_*` : `t_item` indexe `1|8|35` = c_item
cols 7/34 en 0-based — vérifié.)

### Gabarits dans field2

- **`#N#`** = valeur à la **colonne N (numérotation designer 1-based)** de la
  ligne enchant = `raw[N-1]` en V.2. Ex. 45495 « HP capacity increases #17#
  points, and ATK increases #10# points. » : col17 = `2003`→var1 = **140** HP,
  col10 = `2084`→var1 = **78** ATQ. 40896 : « PM Max +#10#, PM +#19# » →
  col10 = 520 (2014), col19 = var2 de `2084` = 809 (ATQM). 1 734 lignes en
  contiennent.
- **`#B<n>$<v>#`** = valeur mise à l'échelle : `v × coef(niveau, col n)/100`,
  coef lu dans `c_spellreference.ini` (vérifié : 52018 « STR +#B21$200# » →
  244 au palier 90, coef col21 = 122). 907 lignes.
- **`$N$`** = code couleur/format en tête de ligne (`$15$` = texte de proc,
  `$13$` = en-tête) — retiré par `TextResolver.Clean`, interprété par
  `ItemTooltip.SkillTip.ParseDescription/CodeColor`.

### Trous constatés

1. **`#N#` rendu littéralement** : `DescriptionOf`/`ActiveTextOf` retournent
   le gabarit brut ; ni le tooltip item (`ItemTooltip` : `$N$` nettoyé,
   `#17#` affiché tel quel) ni le tooltip de buff ne résolvent les références
   de colonnes enchant. Seul `#B…$…#` est résolu — et uniquement via le
   chemin tooltip-de-sort (`ResolveSpellTemplate` → `ResolveScaledValues`,
   contre les champs du sort — les `#N#` de l'enchant restent littéraux).
2. **Mixte EN/FR** (~370 descriptions à dominante anglaise sur 17 417) :
   gabarits **anglais non traduits** pour les paliers 1-15 des classes 1-18
   (`45492-45761` « Warrior Bonus Skill N » / « HP capacity increases #17#
   points… ») ; FR pour classe 19, Juge, paliers 16-20 et toutes les
   éveillées — déjà consigné dans `class-rankbuff-notes.md` et
   `EDENFR-GAPS.md` (G24). Restes EN aussi dans field3 (« ATK SPD +252 and
   Skill MP Cost -129 », 40501) et field4 (noms de procs « Radiant Phoenix »).
3. **Couverture** : t_enchant couvre 17 417/18 487 ids c_enchant (94 %) —
   les ~1 070 manquants retombent sur le nom Big5 chinois puis « enchant #id ».
   19 ids de t_enchant sont **orphelins** (41146, 45095-45107 « Zone de Buff
   Tour Céleste », 49996-8 : texte sans ligne d'effet). t_itemmallenchant :
   1 208/1 399 couverts.

## 5. Limites / à vérifier

- La sélection « commande par args » du dispatch (ex. 2084 var→attribut) est
  datovée pour les codes listés dans `EnchantCommands.cs` ; les **codes non
  résolus (§2 bas)** sont documentés par usage FR, pas par désasm —
  `DecodeEffectCommand` @0x8380ad0 est en timeout Ghidra, les ids dispatchés
  indirectement attendent un passage objdump ciblé (2055 et 2132 = 1 420
  lignes à eux deux).
- Les bits de flag au-delà de 0x10/0x100/0x20/0x40/0x80 (valeurs 0x200…
  0x8000, ex. 8208, 8848, 8448) sont non mappés — le vocabulaire designer
  (擊中/受傷/攻擊失效…) ne colle pas bit-à-bit avec les numériques livrés.
- V.2 col59 (champ post-designer, 106 lignes remplies) : rôle inconnu.
- `EnchantParser` (serveur) nomme encore cols 37-39 « ProcChance/
  AppliedSpell* » — à relire en 作用間隔/高權位/低權位 (schéma designer) :
  `Evaluate()` est une heuristique de proc, pas une projection fidèle.
- Une ligne FR (46131) a 59 colonnes au lieu de 60 — parseur tolérant, donnée
  à consigner si elle devient significative.
