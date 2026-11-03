# Analyse — provenance des données et mélange CN/EN/FR

> **Date** : 2026-10-30 · **Type** : analyse statique forensique (aucune modification).
> **Question** : pourquoi le client Godot affiche un mélange chinois / anglais /
> français, pourquoi des items visibles dans l'Archive ne se donnent pas, et
> pourquoi les renards / montures de combat officiels semblent absents.

---

## TL;DR

Le client Godot empile **trois couches de texte de provenances différentes** :

| Couche | Source | Langue |
|---|---|---|
| `data/db/c_*.ini`, `C_*.ini` (données) | Client CN original via Reforged | **Big5 chinois** (repli final) |
| `data/db/t_*.ini` (texte de base) | **EdenReforged verbatim** (`raw_assets` symlink + `cp -aL`) | Anglais (+ customs Reforged) |
| `data/db/lang/fr/t_*.ini` | **Client officiel Aeria FR** transcodé cp1252→UTF-8 | Français officiel |

La résolution FR remplace **la table entière** sans fusion par ID → tout ID
absent du FR retombe sur le nom **Big5 chinois** (pas sur l'anglais).

Le « give impossible » : le serveur ne connaît que `S_Item.ini`+`S_ItemMall.ini`
(**~25 664 items**) alors que le client affiche **~71 000 entrées** `c_item`
Reforged — items visibles mais « hors tables » côté serveur.

Le « manque des renards » a deux volets : **familiers** — ⚠️ **corrigé**
(2026-10-30) : `S_Partner.ini` contient **bien** toutes les defs renards
(572 recs, ids 41939/42446/42872/43175/43582/44882/48770/45054/45135
vérifiés — l'affirmation initiale « 0 renard » était un artefact de grep
sur fichier Big5). Le non-éclosion est probablement le bug « pets spawn
z=0 » déjà logué, pas un trou de table ; **montures de combat** — les
17 IDs fox sont tous `S_ItemMall` (giveables, visibles dans l'Archive,
GLB présents), et les légendaires **1290 « Kitsune de l'Aube »** (renard
doré, ride r253 via enchant 11354) et **48813 « Renard Blanc du blizzard
aux neuf queues »** (l'exclu Aeria `純白狐王(美法特製金品)`, ride r268 via
enchant 60419) sont depuis le 31 oct. **giveables + invocables + rendus**
(`S_ItemExtra.ini` prioritaire + lignes `c_enchant` + cartes archive —
voir RAPPORT-CORPUS-OFFICIEL + captures `godot-shots/white-fox*.png`) ;
elles SONT présentes dans le data EDENFR — Reforged/US n'ont pas leurs
lignes (l'id US 48813 n'est qu'un placeholder `測試用編號`).

---

## 1. Topologie du workspace

```
/home/ster2lune/Bureau/reverseden/
├── portage/                    ← DÉPÔT ACTIF (git, origin eden-portage.git)
│   ├── eden_godot/             ← client Godot 4 C# (seul client supporté)
│   │   ├── godot_project/      ← gamedata/ = overlay (lang/fr, tsv, cfg)
│   │   ├── raw_assets ──────────────→ symlink vers eden_server/reforged/EdenReforged
│   │   ├── dist/package-linux/  ← package À JOUR (base Reforged + lang/fr + .inib)
│   │   ├── dist/package-windows/← package OBSOLÈTE (sans lang/fr ni tsv ni inib)
│   │   └── tools/               ← edenpack, package_dist.sh, import_edenfr_overlay.py…
│   ├── eden_server/            ← serveur C#/.NET 10 (server/Eden.sln) — CELUI QUI TOURNE
│   ├── re/                     ← corpus reverse (exports Ghidra) — lecture seule
│   ├── knowledge/              ← wikis EN/FR + classeur Vendetta
│   └── docs/, ref-shots/
│
├── eden_server/                ← ORACLE (git Noah-Lhommedieu/eden_server.git)
│   ├── client/                 ← client officiel US (locate.ini: Locate=US)
│   ├── reforged/EdenReforged/  ← client serveur privé EN (= source de raw_assets)
│   ├── server_us/              ← binaires natifs + Data/db_decoded/*.ini (Big5)
│   ├── dbdata/                 ← datadir PostgreSQL bind-mounté
│   ├── Database/               ← migrations SQL
│   └── docs/, tools/, captures/, clientmod/, limitless/, re/, ghidra_*
│
├── eden_project/               ← SNAPSHOT de travail (copie importée dans portage, sans .git)
├── edenfr_overlay/             ← symlinks vers ~/Téléchargements/EDENFR/extracted/
└── _wine-artifacts/

/home/ster2lune/Téléchargements/EDENFR/   ← CLIENT OFFICIEL AERIA FR COMPLET
    ├── extracted/data/db/      ← 141 ini : c_* (GBK) + t_* (cp1252 FR)
    ├── GameDataFileList.txt          ← manifeste data : 47 601 entrées (0 t_*)
    ├── GameDataTranslateFileList.txt ← manifeste traduction : EXACTEMENT 34 t_*.ini
    └── packver.txt = 20150504130915 (build 4 mai 2015, Locate=FR)
```

Filiation serveur : `eden_server` (git) → copie `eden_project` → import
`portage/eden_server` (dev continue là). PostgreSQL `fndb1`/`fnaccount`/`fnmember`.

---

## 2. Schéma global des données

```
                        ┌─────────────────────────────────────────────────┐
                        │              SERVEUR C# (portage)               │
                        │                                                 │
  server_us/Data/       │   GameDataService.LoadAll(DATA_DIR)             │
  db_decoded/    ──────►│   ├─ S_Item.ini + S_ItemMall.ini  ≈ 25 664 items│
  S_*.ini  (Big5 CN)    │   ├─ S_Monster / S_Mission / S_Enchant / S_Node │
                        │   ├─ S_Partner.ini   (572 recs, renards OK)     │
  reforged/data/db ────►│   ├─ ItemNames = T_Item+T_ItemMall (EN seul)    │
  (overlay EDEN_        │   └─ overlay enchants Reforged (ApplyClientOv.) │
   REFORGED_DB)         │                                                 │
                        │   PostgreSQL = INSTANCES (inventory1/2,         │
                        │   collection, equipment…) — AUCUN catalogue     │
                        └───────────────┬─────────────────────────────────┘
                                        │ wire (NCID)
                                        │  • items/mobs/quêtes : IDs SEULS
                                        │  • texte serveur : 316/474/266
                                        │    args Big5 (give, fortune…)
                                        │  • chat/étals/mails : brut
                                        ▼
┌───────────────────────────────────────────────────────────────────────┐
│                        CLIENT GODOT (affichage)                       │
│                                                                       │
│  DataPaths.ResolveFile — ordre : EDEN_DATA_DIR → <exe>/gamedata        │
│  → raw_assets/ (dev) → res://gamedata ; TextResolver : raw_assets     │
│  d'abord, puis overlay gamedata (GameScene.cs:181-190)                │
│                                                                       │
│  ┌─ data/db/  (= Reforged via raw_assets / copié dans dist) ─────────┐│
│  │ c_*.ini / C_*.ini : données + noms Big5 CHINOIS (repli final)     ││
│  │ t_*.ini / T_*.ini : texte ANGLAIS Reforged (dont contenu custom)  ││
│  └──────────────────────────────────────────────────────────────────┘│
│  ┌─ gamedata/ (overlay projet) ─────────────────────────────────────┐│
│  │ data/db/lang/fr/t_*.ini : 34 tables FR OFFICIELLES + ajouts      ││
│  │ data/db/t_npc_extra.ini : PNJ events EN maison                   ││
│  │ ui_text_{en,fr,en_fr}.tsv / levels.tsv / *.cfg                   ││
│  └──────────────────────────────────────────────────────────────────┘│
│  ┌─ assets/ (convertis) : GLB (nif2gltf), PNG (dds→png), uidata CEGUI ││
│  └──────────────────────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────────────────────┘
```

**Principe du wire** : le serveur envoie des IDs ; le client résout les noms
dans ses tables. Exceptions = NCID **316** (`ClientTextOutputIndex`),
**474** (`ClientScreenMessage`), **266** (texte libre) : le serveur y injecte
des chaînes formatées contenant le nom **Big5** de `S_Item` (feedback give
`0x89`, annonces fortune/loterie) ou le nom EN de `T_Item`.

---

## 3. Provenance prouvée (preuves directes)

| Élément | Source | Preuve |
|---|---|---|
| `dist/*/gamedata/data/db/` (= base) | **EdenReforged verbatim** | `t_item.ini` md5 `57cbc1b6…` identique Reforged ; `cp -aL $RAW/data` (`package_dist.sh:77`) ; 18 fichiers avec signatures « Reforged » aux mêmes lignes |
| `lang/fr/` (34 fichiers) | **Aeria officiel FR** transcodé cp1252→UTF-8 | `iconv CP1252→UTF-8` de `EDENFR/extracted/data/db/t_item.ini` = **md5 identique** (`01ca2bc3…`) ; idem t_monster/t_npc/t_mission/t_dialogue/t_collection ; les 34 noms = le `GameDataTranslateFileList.txt` officiel |
| Compléments FR | **Traduction maison** | t_spell : 1 021 ids officiels (tous identiques) **+ 2 901 ajouts** ; t_itemmall : 7 815 vs 1 772 officiel |
| `t_npc_extra.ini` | Maison (CN→EN) | PNJ events 52xxx absents de t_npc Reforged |
| `ui_text_*.tsv` | `tools/gen_ui_text.py` | CN→EN (1 039 clés CN), CN→FR (partiel), EN→FR (8 413) |
| Items serveur | `S_Item.ini` + `S_ItemMall.ini` Big5 | 17 257 + 8 407 ≈ **25 664** — pas de table `items` SQL |
| `eden_server/client/` | Officiel **US** (pas FR) | `locate.ini: Locate=US` |

Détails packaging : `dist/package-linux` = base Reforged + overlay `lang/fr/` +
`.inib` bakés (edenpack). **`dist/package-windows` = build obsolète** (avant
l'overlay FR) → s'affiche EN+CN sans aucun FR.

### Mécanisme officiel (référence)

Le client Aeria gérait la langue par **pack séparé choisi à l'install**
(`Locate=FR`, manifeste traduction de 34 fichiers). Le portage reproduit le
même concept en **overlay runtime** `lang/fr/` + repli EN — supérieur
(multilingue à chaud), mais le repli est au niveau **fichier**, pas ID.

---

## 4. Mécanisme de résolution de texte (cause racine du mélange)

`TextResolver.Ensure` (`TextResolver.cs:353-369`) :

```
Language = "fr" ?
  ├─ data/db/lang/fr/<table> EXISTE → il est chargé SEUL (remplacement entier)
  │     ID présent   → FR ✔
  │     ID absent    → null → repli de l'APPELANT :
  │                     nom Big5 du c_*.ini = CHINOIS  ← le piège
  └─ data/db/lang/fr/<table> ABSENT → data/db/<table> (EN Reforged) → EN ✔
```

**Exceptions — fusion par ID** (repli EN propre, jamais CN) :
`AchievementWindow.LoadLang`, `NpcDialogueWindow._dialogueLang`,
`EnchantTable`, `TitleTable`.

### Couverture FR mesurée (lang/fr vs base Reforged)

| Table | Base EN | FR | % | Conséquence des trous |
|---|---|---|---|---|
| t_item | 20 038 | 18 904 | 94 % | items Reforged-only (65xxx…) → **CN** |
| t_itemmall | 9 436 | 7 621 | 81 % | ~1 800 ids mall → CN/EN |
| t_monster | 11 569 | 11 218 | 97 % | mobs Reforged (65068+) → CN |
| t_npc | 3 001 | 2 648 | 88 % | NPC events 549xx → CN |
| t_dialogue | 16 538 | 16 122 | 97 % | trous dispersés (anomalie −291) |
| t_mission | 2 539 | 2 465 | 97 % | quêtes Reforged L85 → CN |
| t_spell | 4 610 | 3 922 | 85 % | ~700 sorts → CN |
| T_Enchant | 20 516 | 17 478 | 85 % | noms de buffs/montures → CN |
| T_PaySystem | 735 | 224 | **30 %** | boutique → CN |
| T_Battlefield | 455 | 3 | **0,7 %** | stub quasi vide |
| T_CharColor | 128 | absent | 0 % | → EN forcé |

### Toutes les sources de texte non-FR à l'écran

1. **Trou FR + repli Big5** → chinois (mécanisme ci-dessus).
2. **Tables sans lang/fr** : T_CharColor, `T_Transport.ini` lu en littéral
   (`StoreTable.cs:158`), `t_npc_extra.ini` (EN-only) → anglais.
3. **Lecture directe bypassant lang** : `CharCreateScreen.cs:61` lit
   `data/db/T_Class.ini` direct → création de perso toujours EN.
4. **Champs Big5 affichés tels quels** : `c_mission` catégories de quêtes,
   `c_title`, `C_EquipType`, repli nom/desc `c_item`/`c_class` → chinois.
5. **Chaînes UI** : trous de `ui_text_fr.tsv`/`ui_text_en_fr.tsv` → anglais ;
   chaîne absente des deux → le chinois du `.layout` CEGUI tel quel ;
   texte chinois cuit dans les PNG (CNDejaVu, CNGuideRB) → intraduisible.
6. **Wire text** : chat, étals, mails, noms joueurs (texte saisi) ;
   args Big5 des 316/474 (feedback give = nom CN serveur).
7. **Panneau déjà ouvert à la bascule de langue** → réouverture requise.

---

## 5. Ce que le client charge (inventaire fonctionnel)

### Tables texte `t_*` (résolues via TextResolver, repli Big5)

| Table | Usage |
|---|---|
| t_item + t_itemmall | Noms/descriptions objets : inventaire, tooltips, boutiques, banque, mail, archive, mall |
| t_monster | Noms mobs (labels, objectifs quêtes) — repli `DisplayName` wire |
| t_npc + t_npc_extra | Noms/titres PNJ, bulles, cibles de quête |
| T_Spell | Sorts/buffs (hotbar, buffbar, tooltips) |
| t_partner | Noms familiers |
| t_dialogue | Corps des dialogues PNJ (displayId serveur) |
| t_mission | Titres/zones/descriptions de quêtes |
| t_node | Noms de zones (chargement, minimap, carte monde) |
| T_Class | Classes (création perso — ⚠ lue en direct = EN) |
| t_textindex | Gabarits messages système (NCID 316) |
| T_Enchant / T_Achievement / T_Title / t_destinyevent | Buffs, hauts faits, titres, destinée |
| T_Transport | Routes transport — **EN seul** (pas de variante lang) |

### Tables données `c_*`/`C_*` (Big5, Reforged verbatim)

`c_item`+`c_itemmall` (icônes/stats/prix — nom Big5 col7 en repli),
`c_monster` (modèles/stats), `c_npc`+`npclist` (modèles PNJ),
`c_spell`/`c_spellreference`/`c_talent`, `c_class`/`C_Classes`/`C_Races`,
`c_mission` (conditions/récompenses + catégories Big5 affichées),
`c_enchant`, `c_ai`, `c_node`, `C_Store`/`C_Transport`, `ridelist`
(**absent du gamedata** — fallback `rNNN`, perte scale/saddle cosmétique),
`C_Partner`/`c_partner`, `c_collection`+`c_collectionpoint` (**ArchiveWindow**),
`c_achievement`, `c_title`, `C_DailyAward`, `C_FamilyCollect`/`c_familycombine`
(craft), `c_dropitem`, `C_EquipSet`, `C_DestinyRoad`/`C_DestinyEvent`,
`C_CharColor`, `4pequip`, `motion`, `effect`/`DynEffect`, `startsetting`.

### Autres données

- `data/scene/sNNN.ini` (Big5) : spawns NPC/mobs/portails par map.
- `map/model/Sxxx/` : `.fsm` placement, `.ini` env, `.smp` collisions.
- `uidata/` CEGUI : layouts (littéraux CN → UiTextEn), imagesets, fonts, thèmes.
- `assets/` convertis : `UI/itemicon/<code>.png`, `monster|char|ride/model/*.glb`,
  `map/texture/`… (source = Reforged via `convert_dds_to_png.py` + `nif2gltf`,
  complétés par `import_edenfr_overlay.py` pour assets officiels absents).
- `gamedata/` overlay : `client.cfg`, `settings.cfg` (`language=en|fr`),
  `levels.tsv`, TSV de traduction, `lang/fr/`, `t_npc_extra.ini`.

---

## 6. Ce que le serveur charge et envoie

### Au boot (`GameDataService.LoadAll`, DATA_DIR = `server_us/Data/db_decoded` + overlay Reforged)

| Propriété | Fichier(s) | Rôle |
|---|---|---|
| `Items` | `S_Item.ini` + `S_ItemMall.ini` ≈ 25 664 | **catalogue autoritaire** (give, drops, shops) |
| `ItemNames` | `T_Item.ini`+`T_ItemMall.ini` EN | noms pour give/feedback — ne rend PAS grantable |
| `Collections` | `S_Collection.ini` (7 893) | cartes d'Archive déposables |
| `Partners` | `S_Partner.ini` | familiers — renards **présents** (corrigé) |
| Missions/NPC/Monsters/Spells/Nodes/Stores/Dialogues | `S_*` + `T_*` | monde & quêtes |
| `Texts` | `T_TextIndex` repli `S_TextIndex` | gabarits 316/474 |

`RequireLoaded` bloque le boot si Items/Monsters/Spells/Nodes vides.
Le service **Mission** charge le même `LoadAll` (quêtes partagées).

### PostgreSQL vs `.ini` — le split exact

**`.ini` = le catalogue du jeu** (ce qui *existe*) ; **PostgreSQL = l'état
dynamique** (ce que les joueurs possèdent). Postgres ne sait jamais ce
qu'est un item : `inventory1.item_id` n'est qu'une référence vers
`S_Item.ini`. **Aucune table `items`/`item_templates` n'existe.**

```
DATA_DIR = server_us/Data/db_decoded/       ← tables NATIVES Big5 (CN)
   S_Item+S_ItemMall (autorité items) / S_Monster / S_Mission / S_Spell
   / S_Level / S_Class / S_Parameter / S_Partner …  ← JAMAIS overlayés
   T_Item+T_ItemMall (EN) → ItemNames (give / find_item / feedback)
        +
EDEN_REFORGED_DB → ../../eden_server/reforged/EdenReforged/data/db
   overlay appliqué à DEUX familles seulement (ReforgedDataLocator) :
   • enchants : c_enchant + c_itemmallenchant (remplace par id + ajouts)
     → EnchantService.ApplyClientOverlay — rend jouables les montures
       de combat 60xxx / « Exalted » Reforged
   • talents : C_Talent Reforged = table PRIMAIRE (S_Talent = compat)
   kill-switch : EDEN_REFORGED_DB=off — ⚠ dépendance croisée : le
   serveur de portage lit le data/db du VIEIL arbre eden_server/
```

| Base | Contenu | Nature |
|---|---|---|
| `fndb1` (`DB_CONN`) | `inventory1/2` (ping-pong natif), `equipment1/2`, `storage1/2`, `mailitem`, `collection`, `bags`, `player_license`, `item_mall_items` (achats = instances), `player_characters` (**privilege**), `player_achievement_rank.collection_point`, `player_items` (legacy morte) | Instances uniquement |
| `fnaccount` (`ACCOUNT_DB_CONN`) | `accounts` (+`itemmall_password`), **`itemmall` = catalogue des articles EN VENTE** (+`itemmall_old`, `itemmall_limit_amount`, `time_itemmall_log`), `fortune_bag` (seed 008 — notes CN jamais affichées) | La **seule** donnée « catalogue » en SQL — restaurée d'un dump natif, non versionnée dans les migrations → **à auditer pour les montures** |
| `fnmember` (`MEMBER_DB_CONN`) | legacy (`fnmember1` droppé par 011) ; `web_itemmall_log` dans les backups | historique/web |

Migrations `Database/` = schéma + seeds d'infra (monde `EternityT`,
classes éveil EN, séquences `item_serial`, `player_license`) — **aucun
seed de contenu de jeu** hors `fortune_bag`. Le seul texte joueur-visible
en base = `fortune_bag.note` chinois (jamais affiché). **Le serveur ne
possède aucun FR** — il est 100 % côté client.

### Wire : qui envoie du texte

| Flux | NCID | Noms ? |
|---|---|---|
| Inventaire / équipement / archive | 274/291/312/472 | **IDs seuls** → résolution client |
| Mobs/PNJ (SeeMob) | 298 | ID + `DisplayName` optionnel (pets) |
| Quêtes/dialogues | 56→client | DisplayId → `t_dialogue`/`t_mission` client |
| Boutique NPC | 304 | storeId → `C_Store` + `t_item` client |
| Messages système | **316** | index + **args Big5 serveur** (give 0x89…) |
| Annonces écran (loterie/fortune) | **474** | gabarit T_TextIndex + **nom Big5 S_Item** |
| Texte libre (find_item…) | 266 | chaîne brute serveur |
| Chat/étals/mails | 268/327 | texte joueur relayé, jamais localisé |

---

## 7. L'Archive et le flux `give`

**« L'archive » = `ArchiveWindow`** (bouton du sac) : grille de toutes les
cartes de `c_collection.ini` (~7 369), possédées en couleur / manquantes
assombries ; noms via `TextResolver.ItemName` → t_item EN/FR, repli `NameBig5`.

```
//give <id|nom> [qty]
  ChatPanel → NCID 3 ZoneTextCommand   ⚠ client encode cp950, serveur lit UTF-8
  → ZoneEntryFlow.HandleTextCommandAsync
      privilege <= 0 → ignoré (texte 0x5d0)
      → GmHandlers.give
          id numérique → GameData.Items.FirstOrDefault
              absent → "[GM] item {id} hors tables S_Item/ItemMall"
          nom → FindItem : Big5 + code + ItemNames EN seulement
              absent → "[GM] aucun item '{nom}' — //find_item"
          kind 40 → player_license (pas en sac)
          sinon → INSERT inventory1 (item_serial) → NCID 274/291
          feedback 316/0x89 = "NomBig5 x qty"  ← affiche le nom CN serveur
```

**Causes d'échec du give, par probabilité :**

1. `//give <nom>` : le lookup serveur n'a que **Big5 + code + EN** — un nom
   FR ne matche jamais ; le CN est intypable et corrompu par le mismatch
   cp950→UTF-8.
2. ID présent côté client (`c_item` Reforged, ~71k entrées) mais absent des
   ~25 664 `S_Item`+`S_ItemMall` serveur → « hors tables ».
3. Items Reforged-only (65xxx…) : **aucune ligne data nulle part côté client**
   (defs serveur Reforged uniquement) — visibles en nom, jamais giveables.
4. Placeholders partagés « CN Ver. Testing ID » → `find_item` = premier match.
5. `privilege=0` → `//` ignoré silencieusement ; sac plein en échec secondaire.

**Workaround** : « Show IDs » (Settings) → `//give <id>` ou `//find_item <EN>`.
⚠ `give` ne vérifie pas `AccessLevel` (handlers async) — seul gate =
`privilege>0`.

---

## 8. Renards et montures de combat

### Familiers renards — data complète (corrigé)

⚠️ **Correction** : l'affirmation initiale « `S_Partner` = 0 renard » était
un **artefact de grep** (fichier Big5 traité comme binaire). Vérifié avec
`grep -a` : `S_Partner.ini` = 572 enregistrements contenant **tous** les ids
renards (41939/42446/42872/43175/43582/44882/48770/45054/45135…).

| Famille | IDs œuf | `S_Partner` | `S_ItemMall` (œuf giveable) | Modèle GLB |
|---|---|---|---|---|
| Kitsu (Ashen/Charcoal/Flame) | 41939–41953 | ✅ | ✅ | M328 |
| Fox Cub | 42446–42450 | ✅ | ✅ | M282 |
| Nine-Tail Fox | 42872–42876 | ✅ | ✅ | M343 |
| Ninetail Firefox | 43175–43179, 48770 | ✅ | ✅ | M344 |
| Inari Fox Spirit | 43582–43586 | ✅ | ✅ | M390 |
| Fox Priestess Rika | 44882–44886 | ✅ | ✅ | M476 |
| Variantes combat | 45054–45135 | ✅ | ✅ | idem |

La chaîne œuf→éclosion (`AddElfFromItemAsync` → `Partners.TryGetValue
(egg.ItemId)`, clé = col1 item_id — présente) est donc complète sur le
papier. **Si les renards n'apparaissent pas en jeu, le bug est ailleurs** :
suspect #1 = « pets spawn sous la map (z=0) » déjà logué
(`docs/temp-plans/2026-10-24-fix-live-lot5-*.md` §4) ; à reproduire en live
avant toute correction de table.

### Montures de combat — vérifié de bout en bout (grep -a, Big5)

```
item → UseEnchantId → enchant cmd 2134|<rideKey> (var3 = flag combat)
     → NCID 283 mask=2 → client : rideKey → ridelist → rNNN → ride/model/*.glb
```

**Les 17 montures renard de combat sont giveables** — toutes présentes dans
`S_ItemMall.ini` serveur (vérifié : 43774/5, 43801/2, 44113/4, 44413/4,
44489/90, 45463, 48799, 44025/6, 48813/5/7), dans `c_itemmall`+`c_collection`
client (→ visibles dans l'Archive), avec noms FR officiels dans
`lang/fr/t_itemmall` (« Renard aux neuf queues (Supérieur) »…), enchants
OK (dont 60xxx via overlay Reforged) et GLB `R253/R254/R255` présents.

**Le cas `1290` — « Kitsune de l'Aube (Légendaire) » / Nine Tail Sunrise
Fox** (corrigé 3ᵉ passe) : ligne `c_item` **absente de Reforged et US** mais
**PRÉSENTE dans `c_item` EDENFR** (ligne 3406, icône `I01488`, noms FR/EN
intacts). Pas de ligne `S_Item`/`S_ItemMall` serveur → **non giveable
quelle que soit la base** (la chaîne craft 1287/1289 + mission 3750 est
aussi absente des tables serveur). Avec une base `c_*` EDENFR elle
redevient **affichable** (icône/tooltip/Archive) ; pour la rendre giveable
il faudrait recréer la ligne serveur + la chaîne — du contenu custom assumé.

**Illusions de « manquant » :**
- `//give <nom FR affiché>` ne matche jamais (lookup serveur = Big5+EN+code
  seulement) → `//give 43774` obligatoire.
- Reforged a **renommé** les montures en EN (`43774` « Sunburned Fox King »
  → « Nine Tail Blazing Fox (Alpha) ») — les noms FR officiels restent
  intacts dans `lang/fr`.
- `48799` absent de `c_collection` → giveable mais invisible dans l'Archive.
- Gate d'obtention légitime = catalogue SQL `fnaccount.itemmall`
  (`SELECT item_id FROM itemmall WHERE sell=1` — non audité hors ligne).
- `ridelist.ini` absent du gamedata : fallback = même stem, perte
  scale/hauteur de selle (cosmétique).

---

## 9. Inventaires chiffrés

### Contenu Reforged-only qui a fuité dans le client

| Type | Volume | Exemples |
|---|---|---|
| Items (t_item) | +1 134 vs officiel | Demonic Stones 65257-66, sets Ascension 65117-50, capes boss 65315-19, Alpaca Knight 23624-27, « Eden Reforged » tokens 65292/26350-53 |
| Sorts (T_Spell) | +123 | famille Holy (60213 Holy Cure…) |
| Quêtes (t_mission) | +596 | « Reforged Class Quest » L85, Memories of the Lost |
| PNJ (c_npc) | +648 | NPC events 54928+ (Halloween 2025, Blight Season) |
| Monstres (c_monster) | +2 110 | Blight Spawn 65068-71… |
| Montures (ridelist) | ~316 entrées | séries TR8xx/9xx, AR8xx, R8xx-9xx |
| Zones (t_node) | +4 | Blighted Incursion 128/138/139/140 |
| Fichiers | — | `Ini-decrypt-only.exe` (33 ko, embarqué), `T_Npc(1).ini` (doublon) |
| Renommages | — | Sword Fox Fox→Phantom Kitsu, Red Fox→Khodran, Clear Sky Jumping Mushroom→Welkin Mushroid, Fantasy Suzaku→Vermilion |

### Contenu officiel manquant / divergent côté client

| Type | Delta | Détail |
|---|---|---|
| c_item | −282 lignes officielles | dont 29226/29227 — ⚠️ mais **pas 1290** (corrigé : présent dans EDENFR, absent de Reforged/US) |
| T_Partner | −127 noms | familiers officiels sans nom dans le corpus |
| T_NPC | −87 | ~20 repêchés dans t_npc_extra |
| T_Mission | −294 | titres de quêtes officiels absents |
| t_dialogue | −291 | anomalie dispersée non élucidée |
| t_item | officiel + ~3 000 lignes desc | descriptions FR officielles plus longues |
| ridelist | 179 officiel → 495 Godot | sur-ensemble complet (aucune officielle absente) |
| c_familyevent | absent | stub 37 octets, seul c_* officiel manquant |

### Serveur

- `S_Partner` : ~~−52 familiers renards~~ **corrigé — renards présents**
  (le vrai problème pets est probablement le spawn z=0, pas les tables).
- Mission 3750 + items 1287/1289/1290 absents de `S_Mission`/`S_Item`.
- `S_ItemMall` : −17 ids officiels (non énumérés, aucun fox/combat dedans).
- Catalogue `fnaccount.itemmall` = restauration dump natif, non audité.

---

## 10. Anomalies adjacentes découvertes

| Anomalie | Détail | Fichier |
|---|---|---|
| **package-windows obsolète** | sans lang/fr, tsv, .inib → EN+CN pur | `dist/package-windows/` |
| **Mismatch encodage NCID 3** | client écrit cp950, serveur décode UTF-8 → tout texte non-ASCII corrompu | `BinWriter.cs` / `ZoneEntryFlow.Admin.cs:33` |
| give sans AccessLevel | handlers async bypassent `S_RootCmds.ini` | `GmCommandService.cs:118-127` |
| check_and_migrate_db.py cassé | nom conteneur obsolète, n'applique que 001 | racine des 3 arbres |
| Feedback give en Big5 | 316/0x89 affiche nom CN serveur | `GmHandlers.cs:538-542` |
| 19 tables lang/fr jamais lues | t_battlefield, t_collection (utile à l'Archive !), t_territory… | `gamedata/data/db/lang/fr/` |
| Dépendance croisée | le serveur de `portage` lit `../../eden_server/reforged` | `EDEN_REFORGED_DB` |
| CharCreateScreen EN forcé | lit `T_Class.ini` en direct | `CharCreateScreen.cs:61` |
| t_npc_extra EN-only | PNJ events affichés EN en mode FR | `gamedata/data/db/` |
| T_Transport EN forcé | chemin littéral sans variante lang | `StoreTable.cs:158` |
| Texte CN cuit dans PNG | CNDejaVu, CNGuideRB, imagesets KR | `uidata/imagesets/` |
| Pets sous la map (bug connu) | spawn z=0 — **suspect #1 du « renards manquants »** | doc temp-plans 2026-10-24 |

---

## 11. Leviers de nettoyage (ordre d'impact)

1. **Fusion par ID dans `TextResolver.Ensure`** (charger EN puis overlay FR
   par ligne, comme `AchievementWindow.LoadLang`) → tout le CN résiduel
   devient EN. **Un seul point de code** corrige la majorité du chinois.
2. **Reproduire le bug familiers renards en live** (spawn z=0 ?) —
   `S_Partner` contient déjà les defs ; pas d'import de table à faire.
3. **Décision stratégique — cible « officiel pur » ou « Reforged assumé »** :
   - Officiel : rebaser les `t_*` sur `eden_server/client` + purger les ids
     Reforged-only (items 65xxx/23624-27/26350-53/39685-99/65117-50, sorts
     6021x, quêtes L85, NPC 549xx, mobs 65068+, Blighted Incursion,
     `Ini-decrypt-only.exe`, `T_Npc(1).ini`) + repêcher les trous officiels
     (T_Partner −127, T_NPC −87, T_Mission −294, c_item −282).
   - Reforged assumé : compléter les trous FR (t_item ~1 100, t_itemmall
     ~1 800, T_Enchant ~3 000, t_spell ~700, T_PaySystem 70 %, T_Battlefield
     stub) + `t_npc_extra` FR.
4. Réparations ponctuelles : `c_item` 1290, `ridelist.ini` dans gamedata,
   `CharCreateScreen` via TextResolver, encodage NCID 3 (cp950 côté serveur
   ou UTF-8 côté client), rebuild `package-windows`, `t_collection` dans
   l'Archive (noms par carte, fichier FR déjà présent), afficher l'id item
   dans l'archive quand admin.
5. Audits SQL : `itemmall.sell=1` (montures combat), `privilege` des comptes
   de test.

---

## 12. Limites de l'analyse

- **Statique uniquement** : aucun build, run, ni accès Postgres —
  `fnaccount.itemmall`, `privilege`, état des conteneurs non vérifiés.
- Diffs par comptage/échantillon, pas exhaustifs ID-par-ID partout
  (c_item↔S_Item, −282 officielles, −291 dialogues non énumérés).
- Couverture des GLB non vérifiable (assets convertis non versionnés).
- `.epk` non inspectés (aucun build packagé présent en loose sur le checkout
  analysé pour lang/fr — `dist/package-linux` est la référence).
- Date d'import initiale de `lang/fr/` non documentée (la provenance EDENFR
  est prouvée par md5, pas par un doc).

---

## Annexe — fichiers clés

| Sujet | Chemin |
|---|---|
| Résolution texte | `portage/eden_godot/godot_project/scripts/data/TextResolver.cs` (`Ensure` :353-369) |
| Racines de données | `…/scripts/DataPaths.cs` (12-115), `GameScene.cs` (181-190) |
| Parser ini + encodage | `…/scripts/data/IniTable.cs` (sniff UTF-8→Big5), `MyCrypt.cs`, `EdenEncoding.cs` |
| Traductions UI | `…/scripts/data/UiTextEn.cs`, `gamedata/ui_text_*.tsv` |
| Archive | `…/scripts/hud/ArchiveWindow.cs`, `…/data/tables/CollectionTable.cs` |
| Packaging | `portage/eden_godot/tools/package_dist.sh` (:77 cp Reforged, :107 overlay) |
| Catalogue serveur | `portage/eden_server/server/src/Eden.Core/Game/Services/GameDataService.cs` (:260-267) |
| Give | `portage/eden_server/server/src/Eden.Zone/Handlers/GmHandlers.cs` (:286-544) |
| Familiers | `…/Eden.Zone/ZoneEntryFlow.Elf.cs` (:1006 TryGetValue) |
| Montures | `…/Eden.Zone/ZoneEntryFlow.Combat.Ride.cs`, `…/scripts/MissionBridge.cs` (:440) |
| Politique overlay serveur | `eden_project/docs/generated/TABLES-DIVERGENCE.md` |
| Audit données | `eden_server/docs/generated/WEB-DATA-AUDIT.md` |
| Officiel FR | `~/Téléchargements/EDENFR/` + `extracted/data/db/` (34 t_* cp1252) |
