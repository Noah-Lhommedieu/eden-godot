# Guilde / Hall de famille — référence

Doc de référence du système de guilde : tables, NPCs posables,
bâtiments, commandes GM, contenu de test, NCIDs. Généré à la main
(lot 19, 2026-10-26) — preuves : `S_Item.ini`, `S_Buliding.ini`,
`S_Npc.ini`, `S_RootCmds.ini`, désasm zone/mission/monde cités
inline.

## 1. Tables fndb1

| Table | Rôle |
|---|---|
| `family` | Guilde : `id, name, leader_id, lv, exp, treasury, emblem, house_open` (0/501/502 = node du hall), `business_tax`, `*_privilege` ×14, `*_title` ×9, `delete_time` (dissolution = timestamp) |
| `player_characters.family_id` | Appartenance + `add_family_time`, `family_comment`, `family_contribution_point`, `family_msgboard_state`, `quit` |
| `family_cadre_members` | Rangs 1-7 (0 = membre, leader = `family.leader_id`) |
| `family_building_container` | **Contenu du hall** : `family_id, id, item_tid, tid, item_type, durability, container_index, loc, x, y, face_dir, level, exp, due_date` |
| `family_msgboard` / `family_schedule` / `family_log` / `family_emblem` | Board, agenda, journal, blob d'emblème |
| `family_bank` / `family_storage` | Coffres de guilde |
| `statues_scenes` | Statues de membres (NCID 161) — posées via le housekeeper |

### `family_building_container` — sémantique des colonnes

Mapping prouvé : `CFamilyDBO::LoadFamilyBuilding` (mission @0x817c2b0)
+ `CNC_DBA_ClientFamilyContainerData::Serialize` (@0x8113a00) →
`CNE_DBA_ClientFamilyContainerData::Deserialize` (zone @0x82eda40).

| Colonne | Sens |
|---|---|
| `container_index` | 0 = bâtiments/NPCs posés (la map externe `SFamily+0x7c`), 1 = stockage bâtiments (cap 40), 2 = stockage NPCs (cap 40) |
| `loc` | Slot : **base de groupe 0/10/20/30/40 = bâtiment** ; base+1 = assistant du bâtiment ; base+1..+8 = NPCs ; 100 = housekeeper ; 0x33 (51) = slot sacré |
| `item_type` | 0x29 = bâtiment, 0x2a = NPC, 0x52 = décoration, 0x16 = item sacré |
| `tid` | 0x29 → **clé S_Buliding** ; 0x2a/0x52 → **template S_Npc** (lu par `BornNPC` dans `SFamily::InitFamilyNpc` zone @0x816f5d0 : `item+0x80`) |
| `item_tid` | Item d'inventaire dont la ligne provient (blueprint / contrat NPC) — affichage dans les paquets 484/487 |
| `x, y, face_dir` | Position du NPC dans le hall (posés par `GetNpcPositionByNpcLoc` @0x84662b0 dans le flux normal) |
| `level` | Niveau du bâtiment → borne des caps NPCs (`CheckNpcSizeLimit` @0x8464ea0, index `level-1`, tables House/Produce/MallNpcLimit) |
| `durability` | Durabilité bâtiment (max = S_Buliding col8 ; réparation = NCID 159, coût col7/point) |

## 2. NPCs posables en guilde

Deux sources : les **contrats natifs** (S_Item kind 0x2a/0x52 dont la
col35 `FamilyNpcTid` référence le NPC) et les **NPCs de service
utilisés dans les halls** (l'item contrat n'existe pas toujours —
dans ce cas `item_tid` = 39049 « contrat test » et `tid` = template
S_Npc, convention des guildes de test 900001/900002).

### 2a. Contrats natifs (S_Item kind 0x2a → NPC)

| Item | NPC tid | Nom Big5 | Rôle / services (S_Npc) |
|---|---|---|---|
| 35001 | **53007** | 城鎮書記官 | **Town Clerk / housekeeper** — `Service 4521 11` (administration guilde, `If_FamilyCadre OwnerOnly`), `Service 4568 19` (gestion bâtiments) |
| 34678 | **53003** | 旅豬拍賣員 | **Commissaire-priseur** — fonction directe `#18##` (enchères, dialog 430) |
| 34679 | **53004** | 銀行專員 | **Banquier** — fonction directe `#6##` (banque perso, dialog 357) |
| 27332 | 53625 | 農產威名商人 | Marchand renom **ferme** — stores `24 505/609/788/825` |
| 27333 | 53626 | 起源植物商人 | Marchand plantes origine — stores `24 506/610/789/826` |
| 27334 | 53627 | 神秘少女 | Fille mystérieuse (ferme) — stores `24 507/611/790/827` |
| 27335 | 53628 | 礦產威名商人 | Marchand renom **mine** — stores `24 512/612/791/828` |
| 27336 | 53629 | 起源礦物商人 | Marchand minerais origine — stores `24 513/613/792/829` |
| 27337 | 53630 | 神秘少女 | Fille mystérieuse (mine) — stores `24 514/614/793/830` |
| 27338 | 53631 | 召喚威名商人 | Marchand renom **autel** — stores `24 519/615/794/831` |
| 27339 | 53632 | 起源召喚商人 | Marchand invocations origine — stores `24 520/616/795/832` |
| 27340 | 53633 | 神秘少女 | Fille mystérieuse (autel) — stores `24 521/617/796/833` |
| 34111 | — | 布衣圖樣收藏家 | Collectionneur de blueprints tissu (col35 vide — NPC dédié non résolu par S_Item) |
| 34112 | — | 傳奇附魔商人 | Marchand enchants légendaires (idem) |
| 34113 | — | 傳奇工藝商人 | Marchand artisanat légendaire (idem) |
| 39020 | 54991 | 測試聖物 | Relique de test (`Event_TriggerEvent`, `TerritoryAward`) |
| 39049 | 53009 | 專業農夫 | **Contrat test 1M** — paysan pro : `Service 4515 13 2` (ordre travail ferme lv2) |
| 39050 | 53009 | 專業農夫 | Contrat test 3M (même NPC) |

### 2b. NPCs de service posés dans les halls de test (pas de contrat dédié → `item_tid` 39049)

| NPC tid | Nom Big5 | Services |
|---|---|---|
| 53010 | 大師農夫 | **Fermier maître** — `Service 4516 13 10` (ordres ferme ≤ lv10) |
| 53020 | 大師礦工 | **Mineur maître** — `Service 4519 12 10` (ordres mine ≤ lv10) |
| 53028 | 召喚士 | **Invocateur** — `Service 4520 14` (ordres invocation) |
| 53038 | 大師煉金士 | **Alchimiste maître** — `Service 4526 15 20` (combinaisons alchimie ≤ 20) |
| 53047 | 名匠鍛造師 | **Fondeur renommé** — `Service 4526 16 20` (combinaisons forge ≤ 20) |
| 53076 | 大師巧匠 | **Enchanteur maître** — `Service 4526 17 20` (combinaisons enchant ≤ 20) |
| 53244 | 精緻農產商人 | Vendeur **graines/catalyseurs ferme** — shops `24 501/503/505/609` |
| 53247 | 精緻礦產商人 | Vendeur **cartes/catalyseurs mine** — shops `24 508/510/512/612` |
| 53237 | 鎧甲圖樣古董商 | Antiquaire blueprints **plaque** — shops `24 532/551/583/603/635/656` |
| 53238 | 皮甲圖樣古董商 | Antiquaire blueprints **cuir** — shops `24 533/552/584/604/636/657` + stores 116/117 |
| 53239 | 布衣圖樣古董商 | Antiquaire blueprints **tissu** — shops `24 534/553/585/605/637/658` + store 118 |
| 50717 | 倉庫 | **Banque de guilde** — `Service 5990 50` (family storage) + `Service 5991 6` (banque perso) |

Ordres de travail (S_FamilyCollect/Combine) : types 12 mine / 13
plantation / 14 invocation ; combinaisons 15 alchimie / 16 forge /
17 enchant — les refIds des services ci-dessus (10/20) sont les
work_id max acceptés.

## 3. Bâtiments (`S_Buliding.ini` — faute de frappe native)

`GameDataService.Buildings` : `tid → {Type, Code, Level, PrevTid,
NpcItemTid, RepairCostPerPoint, MaxDurability}` (mapping
`CBuildingQuery::Deserialize` mission @0x82aca20 : col0=id, col1=code
4PHouse, col3=type, col4=niveau famille requis, col5=tid précédent,
col6=item NPC assistant, col7=coût réparation/point, col8=durabilité
max).

Types : **1** = hall administratif (la « maison » du hall, chaîne de
niveaux 3-10) · **2** = auberge · **4** = ferme · **5** = mine ·
**6** = autel d'invocation · **7** = alchimie · **8** = forge ·
**9** = bassin de mana · **10** = centre commercial.

| Chaîne | tids | Code | Type | Blueprint item (kind 0x29) |
|---|---|---|---|---|
| Hall rural | 1-8 | H00101 | 1 | 35071 |
| Hall montagne | 51-58 | H00301 | 1 | 35073 |
| Auberge brique | 107,119,131,… | H00401 | 2 | 35075 |
| Auberge magique | 108,120,… | H00501 | 2 | 35076 |
| Auberge fort | 109,121,… | H00601 | 2 | 35077 |
| Mall brique | 110,122,… | H00701 | 10 | 35078 |
| Mall magique | 111,123,… | H00801 | 10 | 35079 |
| Mall fort | 112,124,… | H00901 | 10 | 35080 |
| **Ferme** | 101,113,125,… | H01001 | 4 | 35081 |
| **Mine** | 102,114,126,… | H01101 | 5 | 35082 |
| **Autel** | 103,115,127,… | H01201 | 6 | 35083 |
| **Alchimie** | 104,116,128,… | H01301 | 7 | 35084 |
| **Forge** | 105,117,129,… | H01401 | 8 | 35085 |
| **Mana pool** | 106,118,130,… | H01501 | 9 | 35086 |

Chaque chaîne monte de niveau en niveau via `PrevTid` (ex. ferme
101→113→125→…, un tid par palier de niveau famille 3→10). Le
housekeeper visible du hall = NPC de la chaîne du hall admin (base
1 pour node 501, base 51 pour node 502) montée tant que le niveau
suivant ≤ `family.lv` (`GetCenterBuildingHouseKeeperPos` natif).

Caps de NPCs par niveau de bâtiment (`.rodata` zone) :
maison `HouseNpcLimit` = {0,0,2,3,3,3,3,3,3,3,3,3,3,3} ;
production `ProduceNpcLimit` = {0,0,4,5,6,8,8,8,8,8,8,8,8,8} ;
mall `MallNpcLimit` = {0,0,0,1,1,2,2,3,3,3,3,3,3,3}.

## 4. Commandes GM (handlers `Eden.Zone/Handlers/GmGuildHandlers.cs`)

Préfixe `//` ou `/` — accès privilège natif (privilege > 0).

| Commande | Effet |
|---|---|
| `//create_guild <nom> [niveau] [node]` (alias `create_family`) | Crée la guilde du perso (chef), lv 1-10 (défaut 10), `house_open` 501 ou 502 libre, trésorerie max. Monde connecté : ZW 34 `FamilyCreation` natif + `gm_family_setup` ; sinon direct DB + cache + 346 |
| `//guild_level [familyId] <n>` (alias `set_family_level`) | Niveau de la guilde cible (hall visité > familyId > propre guilde) — `family.lv` + caches zone/monde |
| `//open_hall [familyId] [node]` | `family.house_open` = 501/502 libre — bypass gates natifs (lv3, trésorerie, 30 membres) |
| `//give_building [familyId] <buildingTid>` | Insère une ligne 0x29 sur la 1re base de groupe libre (0/10/20/30/40) — `tid` = clé S_Buliding, `item_tid` = blueprint du modèle, durabilité max, niveau 1 |
| `//building_level [familyId] <loc> <n>` | Niveau (1-13) de l'item au slot `loc` — colonne `level` (cap NPCs) |
| `//place_npc [familyId] <npcId>` | Insère une ligne 0x2a (`tid` = template S_Npc, contrat dédié si existant sinon 39049) à la position/orientation du GM, 1er slot libre 1-50 — **injecte l'entité 298 dans la vue** (visible sans relog) |
| `//replace_npc [familyId] <npcId>` | Remplace le NPC posé le plus proche (update `item_tid`/`tid`, conserve loc/position) — swap visuel 269+298 immédiat |
| `//disband_family` | Dissout la guilde du perso (`delete_time` + sortie membres + 346) |

Commandes natives guilde présentes dans S_RootCmds mais **non portées**
(pas demandées) : `family_level_up`, `gain_family_exp`,
`gain_building_exp`, `gain_family_treasury`,
`gain_building_durability`, `gain_family_honor`,
`visit_family_instance`, `select_family_leader`,
`set_family_emblem`, `select_family_emblem`, `gain_family_cp`,
`family_battle_*`, `change_family_leader`, `remove_family_emblem`.

Commandes texte monde (ZW 636) ajoutées : `gm_family_setup
<charId> <level> <node> <treasury>` (setup GM post-création),
`family_set_level <familyId> <level>`, `family_set_house_open
<familyId> <node>` — miroirs des relais TC natifs.

## 5. Contenu de test existant (fndb1)

- **900001 « TestGuildHL »** — chef test1 (200000002) + test2/[GM], lv10, hall **501**, trésorerie max ; 12 lignes conteneur 0 : locs 10-18 (alchimiste 53038, fondeur 53047, enchanteur 53076, fermier 53010, mineur 53020, invocateur 53028, vendeur graines 53244, vendeur cartes 53247, banque 50717) + locs 20-22 (antiquaires BP 53237/53238/53239).
- **900002 « TestGuild2 »** — chef test3 (200000004), lv10, hall **502**, trésorerie 500M ; mêmes NPCs sauf invocateur 53028.
- Toutes les lignes : `item_tid = 39049` (contrat test), `item_type = 42` (0x2a), `tid` = template S_Npc, `container_index = 0`.
- Boosters craft (pas de store — `//give`) : Corona's Essence 37006/37012/37015/37018, Essence Powder 22433/22439/22445 (+30 ext_success — déviation documentée, slot booster natif mort dans ce build).
- Catalyseurs work orders : graines kind 0x32 (30136+) et cartes kind 0x33 (30191+) vendus par 53244/53247.

## 6. NCIDs guilde implémentés

| NCID | Sens | Statut |
|---|---|---|
| **Monde** 9 | `CNE_CW_ServerFamilyOp` — 23 sous-ops (invite/cancel/accept/reject/quit/kick/leader/disband/editinfo/cadre/titres/privilèges/msgboard/commentaire/taxes/niveau/emblème/recrutement) — désasm `CNE_CW_ServerFamilyOp @0x80d9ea0` | ✅ |
| **Monde** 10 | `ClientQueryFamilyInfo` — fiche guilde (types 0/3/4/8) | ✅ |
| **Monde** 73 | `ServerQueryFamilyEmblem` — blob d'emblème | ✅ |
| **Monde** 80 | `ServerFamilyUpdate`? (emblème index) | ✅ |
| **Zone** 49 | `ClientChangeFamily` (346) — affectation/nom/rang/emblème/révision | ✅ |
| **Zone** 81/82 | UpdateFamily / SetFamilyEmblem | ✅ |
| **Zone** 130/131 | `FamilyContainerPutItem` / `GetItem` — pose/retrait conteneurs hall | ✅ |
| **Zone** 132 | `FamilyInstanceGetData` → 487 conteneur 0 | ✅ |
| **Zone** 158 | `FamilyStorage*` — coffres de guilde | ✅ |
| **Zone** 159 | `FamilyDonateMoney` — don trésorerie (cap 999 999 999) | ✅ |
| **Zone** 161 | `FamilyStatue` — statues des membres (54901-905) | ✅ |
| **Zone** 173 | `FamilyInstanceCreate` — création de hall (gates relayés au monde) | ✅ |
| **Zone** 174/175 | `FamilyTownQuery` / `FamilyTownMemberQuery` → 502/503 | ✅ |
| **Zone** 176 | `FamilyTownEnter` — entrée/sortie du hall | ✅ |
| **Zone** 177-180 | Autres ops famille zone (voir FEATURE-MATRIX) | ✅ |
| **Zone** 189/190 | Guerres de territoire | ✅ |
| **Zone** 225 | Family battle | ✅ |
| **Zone** 239/240 | Recrutement/recherche guilde | ✅ |
| **Zone** 259 | Crusade (`//crusade`) | ✅ |
| Chat canal 3 | Broadcast guilde (`TC_ChannelGuild` @0x83c9210) | ✅ |
| Exp famille | Ordres de travail (`S_FamilyCollect col6`/`S_FamilyCombine col3` → `GuildService.AddExperienceAsync`) + `RewardFamilyExp` quêtes | ✅ |
| 346/47/48/50/51/52/53 | Pushes famille monde→client (change/op exception/invitation/query info/schedule/town query/town members) | ✅ |

Détail des preuves par NCID : `docs/generated/FEATURE-MATRIX.md`.

## 7. Flux réseau (mémo)

- **Création joueur** : client NCID 87 op? → zone → **ZW 34**
  `SendFamilyCreationAsync` → monde `HandleFamilyCreationAsync`
  (nom unique, pas de guilde) → `FamilyService.CreateFamilyAsync`
  (INSERT `family` + `family_id`) → push **ZW 32 NewFamily** → zone
  `ApplyNewFamily` + débit 50 000 or + **346** au client.
- **Ouverture hall joueur** : client 173 → gates zone (lv30, chef,
  lv≥3, trésorerie 500 k/10 M) → ZW 636 `family_instance_create` →
  monde `InstanceCreateAsync` (gates : 30 membres, cap 49 villes) →
  `family.house_open` + push **ZW 35** + 50 refresh.
- **Entrée hall** : client 176 (search 174 → 502, members 175 → 503)
  → `house_open` → migration node 501/502 type 4 + `VisitedFamilyInstanceId`
  → spawn NPCs `InitFamilyNpc` (298 rejoués, vue privée).
- **Pose conteneur** : 130 put / 131 get (op dialogue 0x3b requise) —
  gates `CheckItemLoc`/`CheckFamilyItemDelete`/`CheckNpcSizeLimit`.
