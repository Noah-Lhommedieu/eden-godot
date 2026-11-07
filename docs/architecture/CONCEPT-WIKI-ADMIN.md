# CONCEPT — Wiki (consultation) + Admin (édition) Eden Eternal EDENFR

**Statut : conception (aucun code modifié) · 2026-11-07 · auteur : agent de conception**
Légende : **⭐ = ajout de l'agent, non demandé par le propriétaire** (à valider ou rejeter).
Chemins `fichier:ligne` relatifs à la racine `eden-godot/`. Préfixes courts :
`SRV` = `eden_server/server/src`, `CLI` = `eden_godot/godot_project/scripts`.
Statut des constats : *verified* = lu dans le code/DB ce jour ; *hypothesis* = déduit, à prouver.

> **MISE À JOUR 2026-11-07 — ce document est une annexe d'analyse.** Le plan d'action à suivre est
> `docs/architecture/PLAN-ACTION-3.md`. Il remplace §1.1 (D1 : le service HTTP est **Limitless.Api** modernisé, pas un nouveau `Eden.Panel` ;
> D4 : admin par **compte**, plus par personnage), §11 (phases) et §12 (questions Q1-Q4 tranchées : option A + panneau en jeu ; FR source de vérité ;
> définitions « donjon » et « plan de craft » figées au §4 du nouveau plan). Les §2, §5, §8, §9 restent valables sauf contradiction.

---------------------------------------------------------------------------
## 1. Synthèse & recommandations
---------------------------------------------------------------------------

### 1.1 Les 6 décisions structurantes

| # | Décision recommandée | Pourquoi (1 ligne) |
|---|---|---|
| D1 | **Hébergement : option A** — nouveau service C# `Eden.Panel` (ASP.NET Core minimal API + SPA sans build), même solution que le serveur | Seul choix qui réutilise les parsers `Eden.Core`, lit la DB live (guildes) et appelle Zone/World ; `Eden.Core` référence déjà `Microsoft.AspNetCore.App` (`SRV/Eden.Core/Eden.Core.csproj:11`) |
| D2 | **Source de vérité wiki = tables EDENFR `c_*/t_*`** (ce que le joueur voit), **avec rapport d'écart vs tables serveur `S_*`** | Le serveur tourne sur les `S_*` US + 5 overlays FR partiels (§2.1) : wiki et comportement serveur divergent aujourd'hui |
| D3 | **Édition = couche « overlay » de changesets** (JSONL versionné) compilée en fichiers INI *sidecar* (`*.overlay.ini` + tombstones) ; jamais d'écriture dans `corpus/` ni en base pour les données de jeu | Compatible EPK multi-packs « dernier pack gagne » (`CLI/vfs/EdenVfs.cs:74`), patch `edenpack diff/apply`, rollback = pointeur `current.json` |
| D4 | **Admin = grants par personnage** (`account.admin_grants`) + niveaux 1-5, 2FA ⭐, audit append-only chaîné ⭐ ; `Privilege` en jeu synchronisé à la connexion | `privilege=5` n'est posé qu'**à la création** du perso (`SRV/Eden.Core/Database/Repositories/CharacterRepository.cs:170-172`) : pas révocable aujourd'hui |
| D5 | **Écritures sur joueur connecté : jamais en DB directe** → canal interne HTTP loopback vers Zone/World ; hors-ligne : DB + courrier en jeu ⭐ | État chaud Redis (or/pos/vitals, `SRV/Eden.Core/Hot/HotCharacterStore.cs:128`) + caches World/Zone : une écriture DB serait écrasée |
| D6 | **Propagation données : v1 = redémarrage coordonné** de Zone+Mission pour appliquer une release ; hot-reload en v2 | Aucun hot-reload de tables n'existe (§2.1), des listes sont capturées par référence au boot (`SRV/Eden.Zone/Program.cs:95`) |

### 1.2 Faisabilité résumée

| Chantier | Faisabilité | Bloquant principal |
|---|---|---|
| Wiki items/mobs/PNJ/quêtes/cartes | **Haute** | Aucun : données et parsers existent |
| Lien item → source (drop, boutique, craft, quête, coffre) | **Haute** sauf « plan de craft » et « donjon » (heuristiques) | Classification donjon/plan à curer (Q3, Q4) |
| Live view guildes (bâtiments, niveaux, PNJ actifs) | **Moyenne-haute** | Cache `FamilyService` World ≠ DB (lecture DB = cohérence éventuelle) |
| Give / remove joueur connecté | **Moyenne** | Canal Zone interne à créer ; inventaire en mémoire Zone |
| Éditeur de données de jeu (items/mobs/PNJ) | **Moyenne** | Hook overlay côté serveur ET client ; textes FR hors Big5 |
| Hot-reload sans redémarrage | **Basse en v1** | Listes publiques mutables capturées par d'autres services |

### 1.3 Ce qu'il ne faut PAS faire
- Éditer `corpus/fr-full/**` (lecture seule, AGENTS.md) ou `server_us/Data/db_decoded` en place.
- Passer le panneau admin dans le client Godot (option B) : le client n'a que les tables FR chiffrées/packagées, pas la DB, et chaque joueur y aurait le code d'admin.
- Stocker la clé de signature de release dans le panel (§8.5, §9).

---------------------------------------------------------------------------
## 2. Analyse de l'existant (preuves)
---------------------------------------------------------------------------

### 2.1 Données côté serveur (verified)

| Fait | Preuve |
|---|---|
| Le serveur charge **tout au boot, une seule fois** : `LoadAll()` enchaîne ~60 `Load("S_*.ini")`, tolère l'absence (liste `MissingFiles`) | `SRV/Eden.Core/Game/Services/GameDataService.cs:278-476, 747-752` |
| Répertoire = `DATA_DIR` = `eden_server/server_us/Data/db_decoded` (**tables US `S_*`**, 166 fichiers) | `eden_server/start-native.sh:35` ; `SRV/Eden.Zone/Program.cs:42-43` |
| Overlay FR **partiel** par-dessus : `S_ItemExtra.ini` (items), montures (`c_item/c_itemmall/c_enchant`), monstres de donjon (`c_monster`), boutiques (`c_store`), ordres de travail (`c_familycollect/combine`) | `GameDataService.cs:311` (+`MergeFrMounts` :862), `:767`, `:849`, `:803` ; appels `SRV/Eden.Zone/Program.cs:67-76, 706-709` |
| Localisation du dossier FR : `EDEN_REFORGED_DB` (jetons `off/none/-/0` = désactivé) sinon remontée de parents | `SRV/Eden.Core/Data/ReforgedDataLocator.cs:48-72` ; posé à `corpus/fr-full/data/db` par `start-native.sh:39,80,87,95` |
| Les tables FR et US diffèrent partout (lignes `c_*` vs `S_*`) : drop 5606/6344, item 81331/87534, npc 4082/5158, mission 2402/2850, monster 13174/15547, node 3491/3620, familycombine 3178/3275, itemcombo 5413/5858 | `wc -l` ce jour sur `corpus/fr-full/data/db` vs `eden_server/server_us/Data/db_decoded` ; aucun fichier identique (`cmp`) |
| ⇒ **Les drops, PNJ, quêtes et recettes réellement appliqués par le serveur sont ceux de `S_*` (US)**, pas ceux du client FR. `S_DropItem`/`S_Npc`/`S_Mission`/`S_ItemCombo` n'ont aucun overlay FR | `GameDataService.cs:324-355, 360` (pas d'appel FR correspondant) |
| **Pas de hot-reload de tables**. Seuls : `server.config.txt` (poll 2 s, taux/événements) et `ll_reload` (limitless) | `SRV/Eden.Core/Game/ServerConfig.cs:145-151` ; `SRV/Eden.Zone/Handlers/GmHandlers.cs:158` ; `eden_server/server.config.txt:1-3` |
| Listes **capturées par référence** : `new InventoryService(inventoryRepo, gameData.Items)` ⇒ remplacer `Items` après coup ne propage pas | `SRV/Eden.Zone/Program.cs:95` |
| **Deux processus** chargent `GameDataService` : Zone et Mission (World non) | `SRV/Eden.Zone/Program.cs:58` ; `SRV/Eden.Mission/Program.cs:26` |
| Encodage : INI `c_*` = **Big5** ; `t_*` FR = **CP1252** ; parsers serveur lisent Big5 via `EdenEncoding.Big5` | `SRV/Eden.Core/Data/EdenEncoding.cs:7-16` ; `file`/décodage ce jour (`t_npc.ini` « Contrema褾re » = cp1252 lu en Big5) |
| Manifest = SHA-256 de tous les `*.ini` du `DATA_DIR`, envoyé dans le handshake ; mismatch = warning, rejet seulement si `EDEN_MANIFEST_STRICT` | `SRV/Eden.Core/Data/GameDataManifest.cs:43-73` ; `SRV/Eden.Core/Networking/Session/VersionGate.cs:158-180` ; `SRV/Eden.Core/EdenConfig.cs:216` |
| ⇒ le manifest compare l'arbre serveur (S_* US) à l'arbre client (c_* FR) : **ne peut pas servir de contrôle de synchro** en l'état (*hypothesis*, à mesurer) | idem + `CLI/net/EdenConnection.cs:226-235` |

### 2.2 Données côté client (verified)

| Fait | Preuve |
|---|---|
| Lecture des tables : `IniTable.ParseFile` ; forme *bakée* `<f>.ini.inib` préférée si présente ; sinon `MyCrypt.DecodeFile`, sniff UTF-8 strict puis repli (CP1252 pour `t_*`, Big5 sinon) | `CLI/data/IniTable.cs:69-100` ; `CLI/data/EdenEncoding.cs:41-50` |
| Chargement paresseux + cache statique par table (`_item`, `_npc`, `_mission`…), `ItemTable.Load(path)` | `CLI/data/TextResolver.cs:27-45` ; `CLI/data/tables/ItemTable.cs:156-160` |
| Résolution fichier : racines candidates (`EDEN_DATA_DIR`, `<exe>/gamedata`, `res://gamedata`) puis packs EPK | `CLI/DataPaths.cs` (`ResolveFile`, `BuildCandidates`) |
| Packs EPK : montage de tous les `*.epk` ; `current.json` signé (anti-rollback) ; **`EDEN_PACKS` = override dev qui contourne le manifest** ; « dernier pack monté écrase » | `CLI/vfs/EdenVfs.cs:56-130` ; `CLI/vfs/EpkManifest.cs` ; `eden_godot/docs/packaging/EPK-FORMAT.md:153-161` |
| `edenpack` : `build --native`, `diff`/`apply` (patch + tombstones), `install`, `bake-tables` | `eden_godot/tools/edenpack/Program.cs:18-27, 113-125` |
| Précédents d'*extras* : `t_npc_extra.ini` côté client, `S_ItemExtra.ini` côté serveur | `CLI/data/TextResolver.cs:108` ; `GameDataService.cs:287-298` |
| Dictionnaire UI `ui_text_{en,fr,en_fr}.tsv` (généré, « ne pas éditer à la main ») + overrides manuels | `CLI/data/UiTextEn.cs:1-30` ; `eden_godot/tools/gen_ui_text.py`, `eden_godot/tools/ui_text_overrides.tsv` |
| **« Comment obtenir cet item » existe déjà côté client mais est heuristique et pauvre** : devine la grammaire de `c_dropitem`, boutique = « Shop #id » (pas de PNJ), quête = titre, craft = nom | `CLI/data/ItemSources.cs:33-97` |
| Pas de launcher dans le code (seulement `LauncherMod.ini` du corpus) ; packaging = `tools/package.sh` + `edenpack` + export Godot | `tools/package.sh:1-20` |
| Build packagé strict : clés dev dans `~/.config/eden/release/` (pubkey embarquée dans le client) ; `EDEN_PACKS`, `EDEN_ALLOW_LOOSE=0`, `EDEN_PACK_STRICT=1`, `EDEN_PACK_B` | mémoire `strict-epk-run.md` |

### 2.3 Où vit chaque entité du wiki (verified sauf mention)

Volumes (`corpus/fr-full/data/db`, comptage de lignes `^id|`) : items 18 794 (+ mall 7 461), monstres 10 923, PNJ 2 486, quêtes 2 392, boutiques 659, nodes/cartes 374, drops 4 479, recettes craft de guilde 3 178, coffres 1 538, titres 280, succès 3 531. 574 fichiers de scène dans `data/scene`.

| Entité | Table(s) FR | Parser serveur (réutilisable) | Remarque |
|---|---|---|---|
| Item | `c_item.ini` `|V.11|78|`, `c_itemmall.ini`, texte `t_item.ini`, `t_itemmall.ini` | `ItemParser.Parse` (`GameDataService.cs:281`) | `S_ItemMall` fusionné aux items (`:286`) |
| PNJ | `c_npc.ini` (2 lignes/PNJ : tête + corps), `t_npc.ini` (« Titre\nNom ») | `WorldParsers.ParseNpcs` (`WorldParsers.cs:71`) | `NpcDef.StoreId`/`FuncId`/`Services` (`:51`) donnent la **boutique liée** ; `#1/2/3#id#` = boutique direct, `Service … 3 <store>` = dialogue |
| Boutique | `c_store.ini` | `ShopAndMiscParsers.ParseStores` (`GameDataService.cs:849`) | `|id|||item|nom|qté|…` |
| Placement PNJ | `data/scene/sNNN.ini` section `[npc]` : `-n,npcTid,x,y,dir,modèle,nom,…` | client `SceneTable.NpcSpawn` (`CLI/data/tables/SceneTable.cs:48`) | pas de parser serveur Core partagé identifié : à extraire (Q10) |
| Mob (stats/nom) | `c_monster.ini`, `t_monster.ini` | `MonsterParser.Parse` (`:299`) | nom FR via `t_monster` (`TextResolver.MonsterName`, `TextResolver.cs:83`) |
| Placement mob | scène `[reborn_monsters]` : `id,monsterTid,areaId,respawn,…,x,y,…,modèle,nom` | `SceneTable.MonsterSpawn` (`SceneTable.cs:23, 471`) | |
| Drops | `c_dropitem.ini` `|V.3|176|` : id = monstre **ou item-coffre**, pools normal cols 11-91 / BP cols 96-176 | `DropAndSpeedParsers.ParseDropItem` (`DropAndSpeedParsers.cs:54-78`) | `c_classdropitem` pour drops BP/classe |
| Coffres | `c_itemchest.ini` (1 538), `C_LuckyBag`, `C_Lottery` | `S_ItemChest` (non listé dans `LoadAll`) | à vérifier (Q10) |
| Quêtes | `c_mission.ini`, `t_mission.ini` ; récompenses = scripts `#:RewardItem …` | `MissionParser.Parse` (`:301`) | client déjà : `ItemSources.cs:56-67` |
| Cartes/donjons | `c_node.ini` (type col 3 : 1 normale, ≥4 instance), `t_node.ini`, `data/scene` | `GameDataParsers.NodeDef` (`GameDataParsers.cs:259-262`) | distribution type : 5→237, 1→57, 7→29, 18→13… ; critère « donjon » non défini |
| Recettes de guilde | `c_familycombine.ini` (col10 produit, mats groupes de 3 dès col15), `c_familycollect.ini` | `WorkOrderParser` (`GameDataService.cs:803`) ; client `CraftTable.cs:1-60` | **le parchemin de recette (« plan ») est listé parmi les matériaux** (`CraftTable.cs` commentaire CombineRecipe) |
| Guildes | **DB** `game.family` (9 lignes) + `family_building_container` (`tid`,`level`,`loc`,`x,y`) + `family_cadre_members` | `FamilyRepository` (`SRV/Eden.Core/Game/Family/FamilyRepository.cs:658`) | PNJ de ville = stations par type/niveau de bâtiment (`FamilyCityRules.cs:56-62`) |
| Classes/sorts | `c_class`, `c_spell`, `t_spell`, `c_talent` | `SpellParser`, `ClassStatsParser` | |
| Titres/succès | `c_title`, `c_achievement` (+`t_*`) | `GameDataParsers.ParseTitles/Achievements` | |

### 2.4 Qualité des liens item → source (feasibility)

| Source | Lien construit | Qualité | Trou connu |
|---|---|---|---|
| Drop mob | `c_dropitem[mobId].pool → item` ; mob → spawns scène → map/coords | **Bonne** | Grammaire pool à réutiliser du parser serveur (pas l'heuristique client `ItemSources.cs:70-93`) ; écart vs `S_DropItem` réel serveur (§2.1) |
| Drop coffre | `c_dropitem[chestItemId]` + `c_itemchest` | Moyenne | Deux grammaires (ItemSources distingue mob/item par `MonsterName(id)==null`, `:78-82`) |
| PNJ boutique | PNJ.`StoreId/FuncId` → `c_store[id].items` ; PNJ → `[npc]` scène → (map, x, y) | **Bonne** | Plusieurs scènes pour un même PNJ ; PNJ à `LocateMask` régional (`WorldParsers.cs:31-35`, exclure `VisibleAtLocate(FR)` — valeur locate FR à confirmer, Q10) |
| PNJ de guilde | PNJ de station ∈ `FamilyCityRules.StationNpcs` ∪ housekeeper ; présence = bâtiment posé dans `family_building_container` | Moyenne | Table `StationNpcs` en dur dans le code (`FamilyCityRules.cs:56`) : à exposer comme donnée |
| Plan de craft | produit `c_familycombine.col10` ; matériaux col15+ | Moyenne | Identifier *quel* matériau est le « plan » : heuristique `c_item.Kind/UseType` ou liste curée (Q4) |
| Quête | scripts `RewardItem`/`RewardChooseItem` | Bonne | scripts à conditions (`If_*`) |
| Donjon | mobs dont scène a `NodeDef.Type>=4` et n'est pas un hall de guilde (501/502) | **À curer** | pas de liste officielle « donjon » ; `player_dungeon_entries` + `docs/knowledge/donjons-balayage-2026-11-05.md` aident |
| Itemmall / événements / loteries | `c_itemmall`, `C_Lottery`, `C_LuckyBag`, `c_paysystem` | Faible | hors v1 |

### 2.5 Infrastructure serveur existante

| Élément | État | Preuve |
|---|---|---|
| HTTP | **Pas d'API métier.** Seul un mini-Kestrel `/metrics`+`/health` par service (`EDEN_OBSERVABILITY_PORT`, bind loopback par défaut) | `SRV/Eden.Core/Observability/EdenObservability.cs:36-60` ; `SRV/Eden.Core/EdenConfig.cs:333-345` |
| `Eden.Core` embarque déjà ASP.NET | `FrameworkReference Microsoft.AspNetCore.App` | `SRV/Eden.Core/Eden.Core.csproj:11` |
| Services | Gateway 5560, Login 6543, Ticket 2067, Mission 7654, World 5200 (+ZW 5301), Zone 5201 | `eden_server/start-native.sh:64-100` |
| GM texte | Zone NCID 3 : gate `st.Privilege <= 0` ⇒ refus ; sinon commandes locales (`give`, `set_money`, `tele`, `kill`, `clone_monster`…) + registre `GmCommandService` (332 cmds `S_RootCmds`, `AccessLevel`) | `SRV/Eden.Zone/ZoneEntryFlow.Admin.cs:33-50, 201` ; `SRV/Eden.Core/Game/Services/GmCommandService.cs:101-130` ; handlers `SRV/Eden.Zone/Handlers/GmHandlers.cs:289 (give), 423 (del_item), 77 (users)` |
| Privilège | `player_characters.privilege` ; **5 si `accounts.role='Admin'` à la création**, copié dans `EntryState.Privilege` à l'entrée | `CharacterRepository.cs:170-172` ; `SRV/Eden.Zone/ZoneEntryFlow.EntryPipeline.cs:486`, `…EntryPipeline.Install.cs:113`, `…State.cs:574` |
| Comptes/persos admin (DB locale ce jour) | `accounts`: `admin`,`test123` role=`Admin` ; `player_characters`: 6 persos priv 5, 33 priv 0 | `psql edenfr` (SELECT) |
| Auth | mots de passe **Argon2** (`PasswordHasher`) ; Gateway/Login TCP binaire | `SRV/Eden.Core/Security/PasswordHasher.cs:25` |
| CLI admin | `Eden.Admin` : `account create/list`, `char create/list/name-check` via repositories (même INSERT que le flux natif) | `SRV/Eden.Admin/Program.cs:1-30` |
| Audit | `account.gm_tool_log(gm_id, cmd_type, cmd_detail varchar(256), reason, time)` + `gm_tool_accounts` : hérités, **trop pauvres** (pas d'acteur perso/IP/avant-après), usage code non constaté | `psql \d account.gm_tool_log` |
| État chaud | Redis (`:6380`) : or (`gold_base`+`gold_delta`), position, vitals ; flush ~15 s ; verrous `lease:` | `SRV/Eden.Core/Hot/HotCharacterStore.cs:20-40, 128-158, 253` ; `SRV/Zone/CharacterWriteBehind.cs` ; `start-native.sh:31-34` |
| Guildes : deux caches | World `FamilyService` (chargé au boot depuis PG, mutations ops NCID 9, verrou global) ; Zone `GuildService` (instance propre) ; relais zone→monde `family_donate` | `SRV/Eden.Core/Game/Family/FamilyService.cs:15-60` ; `SRV/Eden.Zone/Program.cs:171` ; `SRV/Eden.World/ZoneLinkServer.cs:787-790` |
| Ids items | sérial par séquence PG ; courrier par `MailIdAllocator` ; table `game.mailitem` | `SRV/Eden.Core/Database/ItemSerialAllocator.cs:22` ; `…/MailIdAllocator.cs:15` ; `Database/012-item-serial-sequence.sql` |

---------------------------------------------------------------------------
## 3. Exigences numérotées
---------------------------------------------------------------------------

### 3.1 Wiki (W)

| ID | Exigence | Origine |
|---|---|---|
| W-01 | Référencer **tous** les items (c_item + c_itemmall) : id, nom FR (EN si dispo), description (codes `$N$` rendus), icône, type/niveau/stats | demandé |
| W-02 | « Moyens d'obtention » **précis** par item, une ligne par source (voir W-03…W-09) | demandé |
| W-03 | Source PNJ : **nom du PNJ, map, coordonnées** (x,y) | demandé |
| W-04 | Source mob : **nom du mob + map** (taux de drop ⭐, niveau du mob ⭐) | demandé (+⭐ taux) |
| W-05 | Source donjon : libellé exact **« Donjon : <nom du donjon> »** | demandé |
| W-06 | PNJ de ville de guilde : afficher en **live view** quelles guildes possèdent ce PNJ pour l'item (bâtiment posé + niveau suffisant) | demandé |
| W-07 | Item issu d'un **plan de craft** : indiquer l'**item référent (plan)**, **cliquable** | demandé |
| W-08 | Source quête (récompense) cliquable vers la quête | ⭐ déduit du code existant (`ItemSources.cs:56`) |
| W-09 | Source coffre/lot (« ouvert depuis : item X », cliquable) | ⭐ |
| W-10 | Navigation : **arborescence de recherche + fil d'Ariane** pour revenir en arrière ; chaque entité = URL stable | demandé |
| W-11 | Référencer **toutes les quêtes** (chaîne, prérequis, récompenses, PNJ donneur/rendu, zone) | demandé |
| W-12 | Référencer **tous les donjons** (niveau, node/map, boss, mobs, drops, entrée) | demandé |
| W-13 | **Guildes actuelles** : infos « publiques » autorisées (nom, niveau, nb membres, emblème, bâtiments, niveaux de bâtiment, PNJ actifs) ; exclure trésorerie, membres nominatifs, messages | demandé |
| W-14 | Pages ⭐ : classes & sorts, monstres, PNJ & boutiques, cartes (avec liste PNJ/mobs/portails), recettes (guilde, elfes, vie), titres & succès, événements (`server.config.txt`), montures/familiers | ⭐ (demandé « autres, propose ») |
| W-15 | Recherche globale plein texte (nom FR/EN/Big5 d'origine, id) avec filtres (type, niveau, rareté) | ⭐ |
| W-16 | Liens **inverses** partout (« utilisé dans… » : item → recettes/quêtes qui le consomment) | ⭐ |
| W-17 | Bascule de langue FR/EN par page ; repli propre si absent | ⭐ (contexte FR/EN futur) |
| W-18 | Indicateur « écart serveur » : badge si la donnée affichée (FR) diffère de ce que le serveur applique (S_*) | ⭐ (§2.1) |
| W-19 | Code maintenable : un `EntityKind` par type (registre), pas de logique spécifique dans les routes ; ajout d'un type = une classe + un schéma | demandé (« code propre, MAJ ultérieures ») |
| W-20 | Export statique du wiki (snapshot HTML/JSON) publiable hors serveur | ⭐ |

### 3.2 Admin (A)

| ID | Exigence | Origine |
|---|---|---|
| A-01 | Panel admin d'ergonomie voisine du wiki (mêmes pages, bouton d'action en plus) | demandé |
| A-02 | **Give** item(s) à soi, à un joueur nommé, à un ensemble | demandé |
| A-03 | Cibles ⭐ : « personnage connecté aléatoire », « tous les connectés », « tous les persos d'une guilde », « give all », liste importée | ⭐ |
| A-04 | **Remove** item/or/quête d'un joueur ; ⭐ retrait par sérial précis | demandé (remove) |
| A-05 | Modifier/ajouter/supprimer : **joueurs** (niveau, classe, or, position, quêtes), **guildes**, **donjons**, **mobs**, **quêtes** | demandé |
| A-06 | **Système admin obligatoire** : certains PERSONNAGES sont administrateurs (grants par perso) | demandé |
| A-07 | Niveaux de privilège (1 lecture … 5 root) ⭐ ; chaque endpoint déclare son niveau minimal | ⭐ (demandé : « des personnages admin ») |
| A-08 | L'éditeur agit **vers le serveur** ; le serveur = API (reçoit, valide, modifie) | demandé |
| A-09 | Édition de données de jeu en **brouillon → relecture (diff) → publication** ⭐ | ⭐ |
| A-10 | Analyse des implications (formats, FR/EN, client, cohérence) | demandé (§8, §10) |
| A-11 | L'admin **joue pendant qu'il édite** avec un client **packagé** ; voir ses modifs sans repasser par le launcher (mode dev-session) | demandé |
| A-12 | Joueur normal : mise à jour via launcher (release signée) | demandé |
| A-13 | Garde-fous : dry-run, plafond de destinataires, confirmation saisie, liste d'items interdits, idempotence ⭐ | ⭐ |
| A-14 | Intégrité référentielle : refuser/avertir la suppression d'une entité référencée ; suppression douce (tombstone) | ⭐ |
| A-15 | Annulation (undo) par changeset inverse ; rollback d'une release | ⭐ |
| A-16 | Verrou optimiste si 2 admins éditent la même entité | ⭐ |
| A-17 | Journal d'audit immuable consultable (qui, quoi, avant/après, IP, raison) | ⭐ |

### 3.3 Transverses (T)

| ID | Exigence | Origine |
|---|---|---|
| T-01 | EDENFR pur : aucune donnée `corpus/official`/EN-US ; tout contenu créé est FR (EN optionnel) | AGENTS.md |
| T-02 | `corpus/`, `re/`, `portage/` jamais modifiés ; overlay dans un dossier dédié | AGENTS.md |
| T-03 | Parsers **partagés** avec le serveur de jeu (Eden.Core), pas de seconde implémentation | ⭐ |
| T-04 | Lecture (wiki) séparée de l'écriture (admin) : processus/rôles DB distincts ⭐ | ⭐ |
| T-05 | Preuves : tests `tools/test.sh`, captures, commandes `curl` consignées (« fait = prouvé ») | AGENTS.md |
| T-06 | Panel lié à `127.0.0.1` par défaut ; exposition distante via proxy TLS ⭐ | ⭐ |
| T-07 | Aucune donnée perso (mots de passe, IP) dans le wiki public | ⭐ |
| T-08 | Un changement d'interface partagée (handshake, ZW, schéma DB) = ADR dans `docs/decisions/` | AGENTS.md |

---------------------------------------------------------------------------
## 4. Architecture cible
---------------------------------------------------------------------------

### 4.1 Comparatif des hébergements (point 4 de la demande)

| Critère | **A. Service C# + SPA** | B. Panel dans client Godot | C. Outil desktop séparé | D. Artifact / web statique |
|---|---|---|---|---|
| Réutilise parsers `Eden.Core` | **Oui** (même solution) | Non (parsers client dupliqués : `IniTable` portage) | Possible (.NET) mais hors solution | Non |
| Live view guildes (DB) | **Oui** | Non (pas de DB) | Oui si droits DB (dangereux) | Non |
| Appel Zone/World | **Oui** (loopback) | Via paquets GM (limité) | Idem A | Non |
| Sécurité | Une surface, auth serveur | Code admin livré à tous les joueurs ✗ | Identifiants DB sur poste ✗ | Aucune écriture |
| Maintenance | 1 codebase serveur | Couplé au cycle client (releases) | 2 déploiements | Publication manuelle |
| Accès multi-admin | **Navigateur** | Doit lancer le jeu | Installer | Lecture seule |
| Verdict | **RECOMMANDÉ** | Rejeté | Rejeté | Utile ⭐ seulement comme **export wiki statique** (W-20) |

**Recommandation unique : A.** Variante d'implémentation : `Eden.Panel` = nouveau projet exécutable de la solution (`Eden.sln`), pas un ajout à Gateway/Login (qui sont des services de transport binaires exposés aux joueurs : mêler panel admin et port joueur augmenterait la surface d'attaque).

### 4.2 Schéma

```
                         navigateur (admin / public)
                                   │ HTTPS (proxy) → 127.0.0.1:8480
                          ┌────────▼─────────┐
                          │    Eden.Panel     │  ASP.NET Core minimal API + SPA statique
                          │ ┌──────────────┐  │
                          │ │ /api/wiki/*  │──┼──► Eden.Catalog (lib) ── lit ──► corpus/fr-full (c_*/t_*/scene)
                          │ │              │  │         │                          + overlay publié (sidecars)
                          │ │              │  │         └─ SourceGraph (index inversé item↔source)
                          │ │ /api/admin/* │──┼──► ChangesetStore ──► PG schéma `admin` (changesets, releases, audit, grants)
                          │ │              │  │         │ compile
                          │ │              │  │         ▼
                          │ │              │  │   data-overlay/ (JSONL) ─► build/overlay/*.overlay.ini ─► edenpack ─► .epk
                          │ └──────┬───────┘  │
                          └────────┼──────────┘
          lecture seule (rôle PG ro)│        │ commandes (HTTP interne loopback + secret HMAC)
                 ┌─────────────────┘        ▼
        PostgreSQL `edenfr` (game/account)   Eden.Zone (:5201)  ── joueurs connectés (EntryState, Redis hot)
                                            Eden.World (:5200)  ── FamilyService (guildes)
                                            Eden.Mission        ── quêtes
```

### 4.3 Composants

| Composant | Rôle | Dépend de |
|---|---|---|
| `Eden.Catalog` (lib, **sans HTTP**) | Charge tables FR (+overlay), construit `EntityKind`s, `SourceGraph`, recherche (index mémoire) | `Eden.Core` (parsers) |
| `Eden.Panel` (exe) | API REST + SPA, auth, audit, orchestration changesets | `Eden.Catalog`, Npgsql |
| `Eden.Panel.Web` (statique) | SPA ES-modules **sans étape de build** (Preact+htm ou lit-html chargés localement, router hash) | — |
| Canal interne Zone/World | Endpoints `/internal/admin/*` hébergés par le mini-Kestrel existant (`EdenObservability`), loopback + HMAC | Zone, World |
| `tools/overlay/*` | `overlay-compile`, `overlay-pack`, `overlay-validate` (CLI, mêmes libs) | `Eden.Catalog`, `edenpack` |

Choix techniques : minimal API (cohérent avec l'existant .NET 10) ; JSON `System.Text.Json` source-generated ; SPA sans build pour rester éditable par l'équipe actuelle (⭐ choix, alternative Vite/TS si le propriétaire préfère, Q8) ; recherche en mémoire (≈ 60 k entités : suffisant, pas de moteur externe ⭐).

### 4.4 Structure de dossiers proposée (nouveaux fichiers uniquement)

```
eden_server/server/src/
  Eden.Catalog/                  lib : lecture + graphe
    Kinds/            ItemKind.cs MobKind.cs NpcKind.cs QuestKind.cs DungeonKind.cs GuildKind.cs …
    Sources/          DropSource.cs ShopSource.cs CraftSource.cs QuestRewardSource.cs ChestSource.cs DungeonSource.cs
    Graph/            SourceGraph.cs ReferenceIndex.cs
    Overlay/          OverlayLoader.cs  Changeset.cs  Compiler.cs
    Schema/           columns/*.json   (métadonnées de colonnes par table, §4.6)
  Eden.Panel/
    Program.cs  Auth/ Api/Wiki/ Api/Admin/ Audit/ Internal/(client des canaux Zone/World)
    wwwroot/ (SPA)    index.html app.js pages/ components/ i18n/
  Eden.Zone/Handlers/AdminBridge.cs    (nouveau fichier : endpoints /internal/admin côté Zone)
  Eden.World/AdminBridge.cs
eden_server/Database/027-admin-schema.sql      (schéma `admin` : grants, changesets, releases, audit)
data-overlay/                    (racine repo, hors corpus/)
  changesets/0001-….jsonl  releases/R0007.json  curation/dungeons.json plans.json
docs/architecture/wiki-admin/    (sous-docs si besoin)
```

### 4.5 Conventions de code (maintenabilité)

1. **Un `EntityKind` = une classe** (`ItemKind : IEntityKind`) : `Id`, `Title`, `Load(ctx)`, `Search`, `Links`, `Columns` (métadonnées), `Editor` (optionnel). Aucune route spécifique : `GET /api/wiki/{kind}/{id}` générique.
2. **Registre par DI** : `services.AddEntityKind<ItemKind>()` ; découverte par assembly scan ⭐ en test (un test échoue si un `*Kind` n'est pas enregistré).
3. **Sources de données = plugins** : `IItemSourceProvider { IEnumerable<SourceEdge> Edges(CatalogContext) }` ; chaque provider (drop, shop, craft…) indépendant ; ajouter « loterie » = un fichier.
4. **Pas de numéro de colonne magique hors `Schema/columns/*.json`** (index → nom/type/ref) ; les parsers Core gardent les leurs mais le catalogue lit les noms via ce fichier (⭐ réduit le risque « schema drift » déjà vu : `docs/knowledge/schema-drift-audit.md`).
5. Tests : un test de **comptage de lignes parsées par table** (garde-fou drift) ; un test golden par `EntityKind` (id connu → JSON attendu) ; tests de graphe (item 40002 a une source craft…).
6. DTO immuables (`record`), erreurs = `ProblemDetails` RFC 9457, pas de `catch {}` muet (AGENTS.md).

### 4.6 Métadonnées de colonnes (clé de voûte ⭐)

`Schema/columns/c_item.json` : `[{"idx":0,"name":"id","type":"int","key":true},{"idx":7,"name":"name_big5","type":"text","lang":"zh"},{"idx":17,"name":"buy_price","type":"int"}, {"idx":…,"name":"use_enchant","type":"int","ref":"enchant"}]`. Le champ `ref` génère automatiquement : (a) liens cliquables wiki, (b) index inverse d'intégrité (A-14), (c) formulaires d'édition + validation (type, bornes). On commence par les ~25 colonnes utiles de `c_item`, puis `c_monster`, `c_npc`, `c_store`, `c_dropitem`, `c_mission`, `c_familycombine`.

---------------------------------------------------------------------------
## 5. Modèle de données & API
---------------------------------------------------------------------------

### 5.1 Graphe de liens item ↔ source

```
 Item ──(dropped_by)──► Mob ──(spawns_in)──► Map(node) ──(is_dungeon)──► Donjon
  │  ──(sold_by)─────► Npc ──(placed_at)──► Map + (x,y)
  │                        └─(guild_station_of)──► BuildingType+Level ──(live)──► Guilds[]
  │  ──(crafted_from)► Recipe ──(plan)──► Item(plan)   [cliquable]
  │                        └─(materials)─► Item[]
  │  ──(quest_reward)► Quest ──(given_by)► Npc
  │  ──(opened_from)─► Item(coffre/lot)
  └──(used_in)◄ inverses (recettes, quêtes, boutiques…)
```
Arête : `SourceEdge { kind, itemId, qty?, rate?, from:{kind,id,label}, where?:{mapId,mapLabel,x,y}, via?:[...], dungeon?:{id,label}, plan?:{itemId}, live?:bool, quality: "exact|heuristic" }`. `quality` rend visible le degré de confiance (⭐, W-18).

Libellés rendus (règle propriétaire) :
- PNJ : `PNJ : <nom> — <map> (x, y)` ; mob : `Mob : <nom> — <map> (niv. n, taux %)` ; donjon : `Donjon : <nom>` ; plan : `Craft — plan : [<item référent>]` (lien) ; guilde : `PNJ de ville de guilde : <nom> — guildes : A, B (live)`.

### 5.2 Conventions API

| Sujet | Règle |
|---|---|
| Base | `/api/v1` ; JSON UTF-8 ; ids = entiers du jeu |
| Auth | wiki : public ⭐ (option `Wiki:Public=false`) ; admin : cookie `HttpOnly; SameSite=Strict` + header anti-CSRF `X-Eden-Csrf` |
| Pagination | `?page=1&size=50` (max 200) → `{items:[…], total, page, size}` |
| Recherche | `GET /search?q=&kind=&lvlMin=&lvlMax=&lang=fr` → `{hits:[{kind,id,label,snippet,score}]}` ; préfixe `id:123` |
| Tri/filtre | `?sort=level:desc&filter[rarity]=4` |
| Langue | `?lang=fr|en` ou `Accept-Language` ; repli : fr → en → big5 ; réponse inclut `lang`, `fallback:true` si repli |
| Erreurs | `application/problem+json` : `{type,title,status,detail,code,traceId}` ; codes : `E_NOT_FOUND`, `E_FORBIDDEN_LEVEL`, `E_VALIDATION`, `E_REFERENCED` (suppression bloquée), `E_CONFLICT` (rev), `E_TARGET_OFFLINE`, `E_RATE_LIMIT` |
| Idempotence | écritures : header `Idempotency-Key` obligatoire (A-13) |
| Concurrence | écritures d'entité : `If-Match: <rev>` ; sinon 409 `E_CONFLICT` |

### 5.3 Endpoints wiki (lecture) — niveau 0 (public) sauf mention

| Méthode | Route | Retour |
|---|---|---|
| GET | `/wiki/kinds` | liste des types, comptes, schéma de navigation |
| GET | `/wiki/{kind}` | liste paginée/filtrée (kind ∈ item, mob, npc, quest, dungeon, map, guild, class, spell, recipe, title, achievement, event) |
| GET | `/wiki/{kind}/{id}` | fiche complète + `links` |
| GET | `/wiki/item/{id}/sources` | `SourceEdge[]` groupés par type + `live` (guildes) |
| GET | `/wiki/item/{id}/used-in` | inverses |
| GET | `/wiki/quest/{id}/chain` | prérequis/suites |
| GET | `/wiki/dungeon/{id}` | infos, boss, mobs, drops, entrée |
| GET | `/wiki/map/{id}` | PNJ (x,y), mobs, portails |
| GET | `/wiki/guilds` · `/wiki/guilds/{id}` | vue **publique** : nom, niveau, nb membres, bâtiments (type, niveau), PNJ actifs |
| GET | `/wiki/breadcrumb?path=…` | chemin canonique (arbre) d'une entité |
| GET | `/search` | recherche globale |
| GET | `/meta/version` | `{dataRev, overlayRelease, corpusHash, divergence:{items:n,…}}` ⭐ |

Fiche item (extrait) :
```json
{ "kind":"item","id":40002,"rev":"c3f1…","lang":"fr",
  "name":"Parchemin de défense niv. 1","desc":"…","icon":"UI012",
  "stats":{"level":1,"buy":0,"sell":12,"stack":99},
  "sources":[
   {"kind":"npc","from":{"id":50174,"label":"Gina, marchande"},"where":{"mapId":2,"mapLabel":"Monts Calcaires","x":342.7,"y":321.6},"quality":"exact"},
   {"kind":"mob","from":{"id":56001,"label":"Champignon tacheté"},"where":{"mapId":2,"mapLabel":"Monts Calcaires"},"rate":0.12,"quality":"exact"},
   {"kind":"dungeon","label":"Donjon : Caverne des Ombres","dungeon":{"id":104}},
   {"kind":"craft","recipeId":1,"plan":{"itemId":38001,"label":"Plan : …"},"quality":"heuristic"},
   {"kind":"guildNpc","npc":{"id":53009,"label":"…"},"building":{"type":4,"minLevel":1},
    "guilds":[{"id":5002,"name":"devinguild","buildingLevel":3}],"live":true} ],
  "serverDivergence":{"differs":true,"fields":["buy_price"]} }
```

### 5.4 Endpoints admin — tous authentifiés, niveau minimal entre crochets

| Méthode | Route | Niveau | Effet |
|---|---|---|---|
| POST | `/admin/login` · `/admin/logout` | 0 | compte+mot de passe (Argon2) puis TOTP ⭐ → session |
| GET | `/admin/me` | 1 | perso(s) admin, niveau, expiration |
| GET | `/admin/online` | 1 | joueurs connectés (via Zone `users`) |
| GET | `/admin/players?q=` · `/admin/players/{charId}` | 1 | fiche (inventaire, or, quêtes, position, guilde) |
| POST | `/admin/give` | 2 | `{targets:[…]|selector, items:[{id,qty}], gold?, mode:"direct|mail", reason, dryRun}` |
| POST | `/admin/remove` | 2 | `{target, item:{id|serial,qty}|gold|quest, reason}` |
| PATCH | `/admin/players/{id}` | 3 | niveau, classe, position, titre… (en ligne ⇒ via Zone) |
| POST | `/admin/players/{id}/ban` ⭐ | 3 | suspension (pas de suppression dure) |
| PATCH/POST/DELETE | `/admin/guilds/{id}` | 3 | via World uniquement (cache) |
| POST | `/admin/bulk/preview` · `/admin/bulk/execute` | 3 | actions de masse en 2 temps ⭐ |
| GET/POST/PUT/DELETE | `/admin/draft/{kind}/{id}` | 3 | brouillon d'entité (overlay) |
| GET | `/admin/draft/changes` · `/admin/draft/diff` | 3 | liste + diff avant/après |
| GET | `/admin/refs/{kind}/{id}` | 3 | « utilisé par » (intégrité) |
| POST | `/admin/releases` | 4 | compile + valide + pack (non signé) ⭐ |
| POST | `/admin/releases/{n}/activate` · `/rollback` | 4 | applique / revient (redémarrage coordonné v1) |
| POST | `/admin/server/config` | 4 | édite `server.config.txt` (déjà hot) |
| GET | `/admin/audit?actor=&from=&to=` | 3 (lecture) | journal |
| POST/DELETE | `/admin/grants` | 5 | accorder/révoquer droits admin à un perso |

### 5.5 Schéma PG proposé (schéma `admin`, migration `027`)

| Table | Colonnes clés |
|---|---|
| `admin.grants` | `char_id` PK, `account_id`, `level` 1-5, `granted_by`, `granted_at`, `revoked_at`, `totp_secret_enc` ⭐ |
| `admin.changesets` | `id`, `parent`, `author_char`, `kind`, `entity_id`, `op` (upsert/delete), `payload jsonb`, `before jsonb`, `status` (draft/released), `rev`, `created_at` |
| `admin.releases` | `n`, `changeset_ids int[]`, `overlay_sha256`, `pack_sha256`, `state` (built/active/rolledback), `activated_at` |
| `admin.audit` | `id bigserial`, `ts`, `actor_char`, `actor_acct`, `ip`, `action`, `target`, `request jsonb`, `result`, `reason`, `prev_hash`, `hash` ⭐ (chaîne SHA-256) |
| `admin.idempotency` | `key`, `actor`, `response`, `ts` ⭐ |

Rôles PG ⭐ : `eden_panel_ro` (SELECT game/account sauf colonnes secrètes), `eden_panel_admin` (INSERT/SELECT sur `admin.*`, **sans UPDATE/DELETE sur `admin.audit`**).

---------------------------------------------------------------------------
## 6. Wiki : écrans, navigation, live view
---------------------------------------------------------------------------

### 6.1 Arborescence de navigation (fil d'Ariane)

```
Accueil
├─ Objets ──► Armes ► Épées ► 1 main ► [item]          (arbre = EquipType/Kind/niveau)
│            Consommables · Ressources · Montures · Costumes · Plans de craft
├─ Quêtes ──► Principales (par zone) · Secondaires · Guilde · Événements ► [quête]
├─ Donjons ─► par niveau ► [donjon] ► Boss / Mobs / Drops
├─ Monstres ► par zone / famille ► [mob]
├─ PNJ & Boutiques ► par zone ► [PNJ]
├─ Cartes ──► villes · champs · donjons · villes de guilde ► [carte]
├─ Guildes (live)
├─ Classes & sorts · Recettes · Titres & succès · Événements
```
Le fil d'Ariane est calculé par `GET /wiki/breadcrumb`; **chaque lien interne conserve le chemin d'origine** dans l'URL (`#/item/40002?from=quest/1009`) pour que « Retour » restitue le contexte (W-10). Historique de navigation de session (pile de 20 entités) ⭐ affiché en haut.

### 6.2 Wireframe — fiche item

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Accueil › Objets › Consommables › Parchemin de défense niv. 1     [FR|EN]│
│ ◄ Retour : Quête « L02… »                           🔎 [____________ ]   │
├──────────────────────────────────────────────────────────────────────────┤
│ [icône]  Parchemin de défense niv. 1        #40002   Niv. req 1  ⚠écart  │
│ Description …                                                            │
│ Stats : …           Achat 0 · Vente 12 · Pile 99                         │
├──────────────────────────────────────────────────────────────────────────┤
│ OBTENTION                                                                │
│  PNJ     Gina, marchande          Monts Calcaires (342,321)              │
│  Mob     Champignon tacheté       Monts Calcaires · niv 1 · 12 %         │
│  Donjon  Donjon : Caverne des Ombres                                     │
│  Craft   Plan : [Plan de défense I] ← cliquable                          │
│  Guilde  PNJ de ville : Alchimiste n°1 — guildes : devinguild(L3) ● live │
│  Quête   [L05 Les mines] récompense                                      │
├──────────────────────────────────────────────────────────────────────────┤
│ UTILISÉ DANS   Recette #12 · Quête #1105                                 │
│ (admin) [Give à moi] [Give à…] [Éditer] [Retirer]   ← visible niveau ≥2  │
└──────────────────────────────────────────────────────────────────────────┘
```

### 6.3 Wireframe — recherche à facettes (arborescence)
```
┌ Filtres ───────────┐ ┌ Résultats (1 243)                  tri: niveau ▾ ┐
│ ▾ Type             │ │ [ic] Épée de fer       Arme·1M  niv 12  #1204   │
│   ▸ Armes (412)    │ │ [ic] Épée du gardien   Arme·1M  niv 30  #1377   │
│   ▸ Armures (903)  │ │ …                                                │
│ Niveau [1]──[90]   │ │                                                  │
│ Rareté ☑☑☐☐        │ │ 1 2 3 … 25 ›                                     │
│ Source ☐ Drop ☐ PNJ│ └──────────────────────────────────────────────────┘
└────────────────────┘
```

### 6.4 Live view guildes
- Source : lecture PG `game.family`, `family_building_container` (`tid`, `level`, `loc`), `family_cadre_members` ; PNJ actifs = `FamilyCityRules.StationNpcsFor(type, level)` + housekeeper (`FamilyCityRules.cs:56-70`) et PNJ réellement posés (`loc` base+1…).
- Rafraîchissement : **polling 10 s** (ETag) ⭐ ou SSE ; badge « donnée DB, ≤ 15 s de retard ».
- Cohérence : DB peut précéder/suivre le cache World (§10-R4) ; en admin, bouton « Voir état mémoire World » (appel interne).
- Vue publique : nom, niveau, nb membres (`player_characters.family_id`, pas `family_cadre_members` — cf. `docs/knowledge/craft-guild-systems.md`), emblème, bâtiments+niveaux, PNJ. **Exclus** : trésorerie, membres nominatifs, messages, journal.
- Pour W-06 : requête inverse `npcTid → guildes` = jointure des bâtiments dont `StationNpcs` contient ce PNJ et `NpcCap(level)` ≥ rang.

---------------------------------------------------------------------------
## 7. Admin : écrans, workflows, actions de masse, garde-fous
---------------------------------------------------------------------------

### 7.1 Écrans
```
Admin ▸ Tableau de bord   Connectés(12) · Release active R0007 · Brouillons(3) · Alertes
      ▸ Joueurs           recherche → fiche (onglets: Inventaire | Or | Quêtes | Classe | Position | Guilde | Audit)
      ▸ Give / Remove     assistant en 3 étapes
      ▸ Données           mêmes pages que le wiki + bouton [Éditer] → brouillon
      ▸ Brouillons        liste de changesets, diff, [Publier…]
      ▸ Releases          R0007 actif · [Rollback]
      ▸ Guildes (admin)   édition via World
      ▸ Audit             filtre acteur/cible/date, export
      ▸ Droits (niv 5)    grants par personnage
```

### 7.2 Workflow Give (assistant)
```
1 Cibles  ─ ○ moi  ○ joueur(s) nommé(s)  ○ connecté aléatoire ⭐  ○ tous les connectés ⭐
            ○ guilde X ⭐  ○ « give all » ⭐ (niveau 4 + double confirmation)
2 Contenu ─ items (recherche wiki intégrée, qty) · or · [mode: direct | courrier ⭐]
3 Revue   ─ dry-run : N destinataires, liste, alertes (item « draft » inconnu du client ⚠, lié/inéchangeable ⚠)
            raison (obligatoire) · taper « GIVE 12 » pour confirmer ⭐
→ exécution : Idempotency-Key, audit avant/après, résultat par destinataire (ok / sac plein→trésor au sol / hors-ligne→courrier)
```
Mode `direct` (en ligne) = `GiveQuestItemAsync` côté Zone (`ZoneEntryFlow.Admin.cs:201-212` : AddItem + paquets 274/291 + trésor au sol si sac plein). Mode `courrier` (tout joueur) = ligne `game.mailitem` + `MailIdAllocator` + `ItemSerialAllocator` ⭐ (jamais d'INSERT direct dans `inventory1` hors-ligne en v1 : sérial/slots/bag = risque de corruption).

### 7.3 Actions de masse (⭐ toutes)

| Action | Sélecteur | Garde-fou |
|---|---|---|
| Joueur connecté aléatoire | `random(online, n=1)` seed journalisée | niveau 2 ; item non-lié ; plafond qty |
| Tous les connectés | `online` | niveau 3 ; plafond 500 destinataires ; confirmation saisie |
| Give all (tous les persos) | `all` | niveau 4 ; **mode courrier obligatoire** ; limite items/valeur ; second admin ⭐ (règle des 4 yeux) |
| Par guilde / par classe / par niveau | filtre | idem 3 |
| Retrait en masse (rollback d'un give) | `auditId` | génère l'inverse à partir du journal |

### 7.4 Workflow édition de données
```
Wiki fiche ► [Éditer] ► formulaire généré depuis columns/*.json (types, bornes, ref→sélecteur)
          ► [Enregistrer brouillon] (changeset draft, rev++)  — visible seulement des admins
          ► Brouillons ► diff ► [Publier] (niv 4) : validation (intégrité, encodage, comptes de colonnes)
          ► Release Rn (overlay compilé + pack) ► activation (§8) ► client dev-session voit les changements
```
Création = `op:upsert` avec id alloué dans une **plage réservée aux contenus admin** (ex. ≥ 900 000 000 pour items ⭐, à fixer Q6) pour ne jamais collisionner avec le corpus. Suppression = `op:delete` (tombstone), refusée si `refs>0` sauf `force` niveau 5.

### 7.5 Garde-fous (transversal)
Dry-run par défaut sur toute action de masse ; plafonds (`Admin:MaxGiveQty`, `MaxRecipients`) configurables ; liste d'items non donnables (`data-overlay/curation/nogive.json`) ; idempotence ; rate limit (§9) ; raison obligatoire ; journal immuable ; bouton « annuler » = changeset inverse tant que non purgé.

---------------------------------------------------------------------------
## 8. Pipeline de données
---------------------------------------------------------------------------

### 8.1 Comparatif modèle d'édition

| Option | Pour | Contre | Verdict |
|---|---|---|---|
| **Overlay delta** (changesets → sidecars) | Aucune écriture dans corpus ; diff lisible ; rollback ; compatible EPK/`edenpack diff` ; peu de code à toucher | Hook de fusion client+serveur à écrire ; textes hors Big5 | **RECOMMANDÉ** |
| Édition directe des INI | Zéro nouveau mécanisme | Viole `corpus/` read-only ; Big5/CP1252 cassants ; pas d'historique ; 18 794 lignes en une pièce ; conflits | Rejeté |
| Base de données comme source | Transactions, requêtes | Le client packagé hors-ligne ne lit pas PG ; migration énorme des 60+ parsers ; casse la chaîne `bake-tables`/EPK | Rejeté pour les *données de jeu* ; **retenu pour les métadonnées admin** (§5.5) |

### 8.2 Format overlay

- **Entrée (source de vérité)** : `data-overlay/changesets/*.jsonl`, une ligne = un changeset `{id, table, op, key, cells:{"7":"…"}, i18n:{"fr":{…},"en":{…}}, author, ts}`.
- **Sortie compilée** (dans `build/overlay/data/db/`) :
  - `c_item.overlay.ini` : lignes complètes au format natif de la table (même `|V.11|78|`), **UTF-8** ⭐ ;
  - `c_item.overlay.del` : ids supprimés (une valeur par ligne) ;
  - `lang/fr/t_item.overlay.ini`, `lang/en/t_item.overlay.ini` : textes.
- **Fusion** : `base[key] := overlay[key]` puis retrait des tombstones. Un seul point d'entrée par côté :
  - **Client** : dans `IniTable.ParseFile` (`CLI/data/IniTable.cs:69`), après lecture du base (inib ou ini), chercher `path.Replace(".ini",".overlay.ini")` via `DataPaths.ResolveFile`/EdenVfs — couvre `ItemTable`, `TextResolver`, `CraftTable`, `ItemSources`…
  - **Serveur** : même hook dans `Eden.Core.Data.Parsers.IniTable.ParseFile` (utilisé par `DropAndSpeedParsers.cs:56`). ⚠ `WorldParsers.ParseNpcs` lit les octets lui-même (`WorldParsers.cs:71-75`, MyCrypt + Big5) : à brancher séparément (risque R9).
- Pourquoi UTF-8 : le client sniffe déjà UTF-8 strict avant Big5/CP1252 (`IniTable.cs:84-96`), donc un sidecar UTF-8 est lu correctement sans toucher aux fichiers bakés ; le serveur, lui, n'a que Big5 → le chargeur overlay serveur doit lire UTF-8 explicitement (nouveau code).
- **Textes FR/EN d'un contenu créé** : col « nom » de `c_*` = clé ASCII (ex. `ADM:item:900000001`) ; vrai texte dans `t_*` overlay par langue. Le client lit déjà `t_*` en priorité (`TextResolver.ItemName`, `TextResolver.cs:47-52`) → rien de nouveau côté affichage. Le serveur n'affiche pas de noms d'items aux clients (ils les résolvent localement) sauf `T_Item` pour les commandes GM (`GameDataService.cs:365`).
- Préférence future FR/EN : même mécanisme, un dossier `lang/<xx>/` par langue (précédent `gamedata/data/db/lang/fr`).

### 8.3 Unification serveur ← FR (recommandation de fond, ⭐)
Tant que le serveur lit `S_*` US, **éditer le wiki/overlay ne modifie pas le gameplay réel** pour drops/PNJ/quêtes. Trois paliers :
1. **P-A (v1)** : l'overlay est appliqué *par-dessus ce que chaque côté charge déjà* (serveur : `S_*`+overlay FR existants ; client : `c_*`) ⇒ le panel affiche « écart » (W-18) et l'admin sait.
2. **P-B** : migrer table par table le serveur vers `c_*` FR (comme déjà fait pour 5 tables, `GameDataService.cs:767-870`), chacune protégée par un test « serveur et client voient la même ligne ». Ordre conseillé : `c_dropitem` → `c_npc` → `c_mission` → `c_itemcombo`.
3. **P-C** : l'overlay devient la seule couche d'édition des deux côtés.
Impact « remaniement du format » : **le format INI n'est pas à changer** ; ce qui change = (a) le hook de fusion, (b) la déclaration explicite des tables partagées (manifeste `shared-tables.json` : table → lue par serveur ? client ? chiffrée/packée ?).

### 8.4 Tables partagées / packagées (état)

| Table | Serveur lit | Client lit | Packée EPK (strict) | Overlay v1 |
|---|---|---|---|---|
| items (`c_item`/`S_Item`) | S_Item+extra+FR montures | c_item | oui | **oui** (les deux) |
| `t_item` | T_Item (noms GM) | t_item | oui | oui (lang) |
| monstres | S_Monster+FR donjons | c_monster | oui | oui |
| PNJ | S_Npc | c_npc | oui | oui (+ scène) |
| boutiques | **c_store** (FR) | C_Store | oui | **oui** (déjà aligné) |
| drops | S_DropItem | c_dropitem | oui | oui seulement après P-B |
| quêtes | S_Mission | c_mission | oui | après P-B |
| scènes `[npc]`/`[reborn_monsters]` | `SCENE_DIR` (liens `s*.ini`) | `data/scene` | oui | v2 (spawns) |
| `server.config.txt` | oui (hot) | non | non | direct, hot |
| `ui_text_*.tsv` | non | oui | `gamedata/` | non (généré) |
Les packs étant **chiffrés/signés** (`edenpack build --native` + clés), toute donnée overlay destinée au client passe par un pack signé ; **le serveur lit des fichiers clairs** (pas de chiffrement côté serveur).

### 8.5 Versioning, release, propagation client

```
 brouillons ─► Release Rn (overlay compilé + manifest {rev, sha256, tables touchées})
   ├─ SERVEUR : applique Rn (v1: redémarrage coordonné Zone+Mission ; v2: hot swap) ; ENV EDEN_OVERLAY_DIR + rev
   ├─ CLIENT dev-session : EDEN_PACKS=<packs-release>:<overlay-dev.epk>  (contourne manifest, EdenVfs.cs:74)
   └─ CLIENT joueur : overlay-Rn.epk SIGNÉ (clé release, hors panel) ajouté à current.json via `edenpack install`
```
- **Dev-session admin** : script `tools/admin-dev-overlay.sh` ⭐ : `overlay-pack --dev` compile le dossier brouillon en `overlay-dev.epk` signé avec la clé *dev* (`~/.config/eden/release/…`, cf. mémoire strict-epk-run) puis relance le client avec `EDEN_PACKS`. Version « live » ⭐ : commande client debug `//reload_data` qui `EdenVfs.Reset()` + remet à `null` les caches statiques (`TextResolver._item`…, `ItemSources._idx`) — **hypothesis**, à valider : les caches sont statiques privés (`TextResolver.cs:30-41`).
- **Joueur normal** : la release active est signée **hors panel** (`tools/publish-overlay.sh` avec la clé de release) ; le panel ne détient jamais cette clé.
- **Désynchronisation** (item créé côté serveur inconnu du client) : (1) `dataRev` ⭐ ajouté au handshake (`VersionProtocol.BuildHelloFrame`, `SRV/Eden.Core/Networking/VersionProtocol.cs:106`) : le serveur refuse/avertit un client dont `dataRev < release active` ; (2) garde panel : **give d'un item de statut `draft` refusé** (étape 3 du wizard) ; (3) un item inconnu du client : comportement non vérifié → à tester (Q9). Changement de handshake = **ADR obligatoire** (T-08).
- **Rollback** : activer `Rn-1` = remettre le pointeur (`current.json` atomique, `EPK-FORMAT.md:157`) + redémarrage serveur.

### 8.6 Hot-reload serveur (progressif)
| Palier | Mécanisme | Contrainte |
|---|---|---|
| v1 | `server.config.txt` déjà hot ; tables : **redémarrage coordonné** annoncé en jeu (broadcast existant) | ~secondes de coupure ; sessions rejouent à la reconnexion |
| v2 | `GameDataService.ReloadOverlay(rev)` **mute en place** les `List<>` existantes (Items/Monsters…) sous verrou lecture/écriture ; interdit pour tables indexées dérivées (`LifeSkillById`, `PayEvents`…) sans reconstruction | auditer tous les itérateurs ; tests de charge |
| v3 | Snapshot immuable `DataSnapshot` swappé atomiquement | refactor large (consommateurs passent `snapshot` au lieu de `gameData.X`) |

### 8.7 Intégrité, transactions, concurrence
- **Références croisées** : `ReferenceIndex` (issu des `ref` de `columns/*.json`, §4.6) : item → recettes (mats/produit), boutiques, drops, récompenses de quête, coffres, `c_equipset`, **et inventaires joueurs** (`SELECT count(*) FROM game.inventory1 WHERE item_id=…`) ; supprimer = tombstone + alerte « N joueurs possèdent l'item » (jamais de purge d'inventaires).
- **Transaction de publication** : validation → écriture release → activation en une transition d'état PG (`admin.releases.state`) ; échec = pas d'effet. Écritures joueur = transaction PG unique + audit dans la même transaction.
- **Concurrence** : `rev` par entité (If-Match) ; verrou d'édition indicatif 5 min ⭐ ; deux admins sur la même table = changesets distincts fusionnés à la publication (conflit sur même clé ⇒ rejet).
- **Prod vs dev** : `Panel:Environment=dev|prod` ; en prod, publication = niveau 5 + 2e approbation ⭐ ; en dev, activation immédiate autorisée.
- **Sauvegardes** : `pg_dump` ciblé des tables touchées avant toute action de masse ⭐ ; releases conservées (immuables).

---------------------------------------------------------------------------
## 9. Sécurité & audit
---------------------------------------------------------------------------

### 9.1 Modèle admin

| Niveau | Nom | Droits |
|---|---|---|
| 0 | public | wiki lecture (désactivable) |
| 1 | viewer | + live view complète, fiches joueurs en lecture |
| 2 | support | give/remove unitaire (plafonds), kick/ban temporaire |
| 3 | content | brouillons, actions de masse bornées, guildes |
| 4 | publisher | releases, give all (courrier), config serveur |
| 5 | root | grants, rollback, bypass `force` |

- **Grants par personnage** (`admin.grants.char_id`) : un perso = un niveau. Le compte propriétaire doit en outre avoir `accounts.role='Admin'` ⭐ (ceinture + bretelles). Connexion panel = compte + mot de passe (`PasswordHasher` Argon2) + **TOTP** ⭐ ; session 30 min glissantes, cookie `HttpOnly/Secure/SameSite=Strict`, CSRF header.
- **Synchronisation avec le jeu** ⭐ : à l'entrée de zone, `Privilege` := `max(privilege DB, niveau grant)` ; révoquer un grant retire aussi `privilege` (sinon `//give` en jeu reste possible — aujourd'hui non révocable : `CharacterRepository.cs:170-172`). Ajouter dans l'audit les commandes `//` jouées (aujourd'hui seulement `EdenLog.Write`, `ZoneEntryFlow.Admin.cs:49`).
- **Validation serveur** : tout paramètre revalidé (id existant dans catalogue, qty ≤ plafond, cible existante) ; le client SPA n'est jamais de confiance.
- **Rate limit** ⭐ : `Microsoft.AspNetCore.RateLimiting` : login 5/min/IP, écritures 30/min/admin, masse 2/h.
- **Séparation lecture/écriture** : rôles PG distincts, routes `/admin/*` sur un second `Listener` (port différent ⭐, loopback) ; secret HMAC pour `/internal/admin` (Zone/World), rotation par env.
- **Secrets** : clé de signature de release **hors panel** (§8.5) ; clés dev comme aujourd'hui (`~/.config/eden/release/`).

### 9.2 Audit immuable ⭐
`admin.audit` append-only (pas de GRANT UPDATE/DELETE), chaîne `hash = SHA256(prev_hash ‖ ligne canonique)` ; vérificateur `tools/audit-verify` ; export quotidien hors DB (fichier signé). Contenu : acteur (perso+compte), IP, route, requête (secrets masqués), **avant/après**, résultat par destinataire, raison, `Idempotency-Key`. `account.gm_tool_log` conservé tel quel (compat).

### 9.3 Risques sécurité spécifiques (⭐ tous)

| # | Risque | Mitigation |
|---|---|---|
| S1 | Give d'items interdits (lié compte, événement, mall payant, admin-only) | `nogive.json` + flag auto depuis `c_item` (bind/mall) ; refus serveur |
| S2 | **Duplication** (double clic, rejeu HTTP, retry réseau) | `Idempotency-Key` ; sérial unique `ItemSerialAllocator` ; résultat mis en cache |
| S3 | Édition d'un joueur **connecté** en DB directe → écrasée par flush Redis→PG (`gold_base+delta`, `HotCharacterStore.cs:134-158`) ou invisible | en ligne : uniquement canal Zone (`SetGoldAsync`/`AddItem`) ; DB directe seulement si **hors-ligne vérifié** (`lease`/session) |
| S4 | Incohérence guildes : DB modifiée alors que World garde `FamilyService` en mémoire (`FamilyService.cs:15-60`) puis réécrit | éditer via World (opération NCID 9 ou endpoint interne), jamais `UPDATE game.family` direct |
| S5 | Élévation de privilège (grant forgé, cookie volé) | 2FA, session courte, grants niveau 5 seulement par root, audit des grants |
| S6 | Fuite wiki public (taux de drop, mails, noms) | wiki = tables de jeu uniquement ; guildes = liste blanche de champs ; pas de PII |
| S7 | Injection INI/pipe (`|`, CRLF dans un champ édité) | échappement/refus à la compilation ; test de round-trip parse(emit(x))==x |
| S8 | Clé de release volée via le panel | clé jamais sur l'hôte panel (§8.5) |

---------------------------------------------------------------------------
## 10. Risques & implications
---------------------------------------------------------------------------

| ID | Risque / implication | Impact | Mitigation |
|---|---|---|---|
| R1 | **Écart données serveur (US) vs client (FR)** : wiki dit X, le serveur applique Y (drops, PNJ, quêtes) | **Élevé** : fausse la confiance | rapport de divergence (W-18) dès lot 1 ; migration P-B |
| R2 | Pas de hot-reload, listes capturées (`Program.cs:95`) | Moyen | v1 = redémarrage coordonné ; v2 mutation en place |
| R3 | Désync serveur/client (item créé inconnu du client, `dataRev`) | Élevé | statut draft/published, `dataRev` au handshake (ADR), blocage du give |
| R4 | Caches World (`FamilyService`) / Zone (`GuildService`) / Redis hot ≠ DB | Élevé | écritures par canal propriétaire ; lectures DB marquées « ≤ 15 s » |
| R5 | Big5 : un nom FR avec accent/œ ne passe pas dans `c_*` | Moyen | clé ASCII dans `c_*` + vrai texte dans `t_*`/UTF-8 |
| R6 | CP1252 vs UTF-8 : fichiers `t_*` bakés `.inib` | Faible | l'overlay reste sidecar UTF-8 non bakée ; baker l'overlay en release (`bake-tables`) |
| R7 | `schema-drift` : colonnes décalées (déjà un garde-fou mort `EnchantParser.cs:84`) | Moyen | `columns/*.json` + test de comptage |
| R8 | Identifiants créés : collision ids / ids hors plage client | Moyen | plage réservée, validation (Q6) |
| R9 | `ParseNpcs` hors `IniTable` (octets directs) ⇒ hook overlay absent | Moyen | adapter ce parser (lot dédié) |
| R10 | Contenu EN : 2 langues à entretenir ; l'anglais est « en pause » (AGENTS.md) | Faible | `lang/<xx>/` prêt, EN optionnel, repli fr |
| R11 | Mises à jour client : release overlay signée hors panel ⇒ goulot humain | Faible | script `publish-overlay.sh`, doc ; pas de launcher à écrire en v1 |
| R12 | Perf : charger 60 k entités + graphe au démarrage du panel | Faible | mesuré en lot 1 (cible < 5 s) ; cache binaire optionnel |
| R13 | Édition de scènes (spawns) : fichiers `sNNN.ini` lus par client ET zone, grandes | Moyen | v2, sidecar `[reborn_monsters]` par scène |
| R14 | Suppression d'un joueur / personnage : irréversible | Élevé | pas de suppression dure : ban + `delete_time`, hard-delete = niveau 5 + sauvegarde |
| R15 | Portée « tous les items du jeu » : ids `Testing ID` / placeholders US (`測試用編號`) | Faible | filtre « items valides » + badge ; liste d'exclusion curée |

Implications structurantes à retenir :
1. **Le panel est lui-même un client des données du serveur** : il doit voir exactement ce que Zone voit ⇒ même `OverlayLoader` que Zone (lib partagée), sinon deux vérités.
2. **Créer un contenu = 3 artefacts** (c_* overlay, t_* overlay FR/EN, éventuellement scène) + un déploiement client ; l'UI doit le rendre explicite (état `draft / compilé / publié client / actif serveur`).
3. **Le joueur normal ne voit jamais un draft** ; l'admin en dev-session oui (EDEN_PACKS).

---------------------------------------------------------------------------
## 11. PLAN D'ACTION par phases
---------------------------------------------------------------------------

Estimation relative : **S** ≈ ≤ 1 jour, **M** ≈ 2-4 jours, **L** ≈ 1-2 semaines. Aucune tâche ne modifie `corpus/`, `re/`, `portage/`. Les tâches qui modifient du code **existant** sont marquées `[MOD]` (à valider par le propriétaire, ADR si interface partagée).

### Phase 0 — Fondations & décisions (S-M)

| Lot | Tâches | Fichiers | Critères d'acceptation | Dép. |
|---|---|---|---|---|
| 0.1 ADR | ADR-A « hébergement Eden.Panel », ADR-B « overlay + sidecars », ADR-C « handshake `dataRev` » | `docs/decisions/00xx-*.md` | 3 ADR relus et acceptés par le propriétaire | Q1-Q3 |
| 0.2 Rapport de divergence | Outil lecture seule : compare `c_*` vs `S_*` (lignes/ids/champs) par table partagée | `tools/overlay/divergence/` (nouveau) | `tools/overlay/divergence --table c_dropitem` sort un TSV ; chiffres cités §2.1 reproduits | — |
| 0.3 `shared-tables.json` | Déclarer table → serveur/client/packée | `data-overlay/shared-tables.json` | Test : toute table lue par `GameDataService.LoadAll` figure au manifeste | 0.2 |

### Phase 1 — Catalogue + Wiki MVP items & sources (L)

| Lot | Tâches | Fichiers | Critères d'acceptation | Dép. | Taille |
|---|---|---|---|---|---|
| 1.1 Projet `Eden.Catalog` | Créer lib, charger `c_item`,`c_itemmall`,`t_item`,`t_itemmall` (lecture sur chemin `EDEN_REFORGED_DB`) ; décodage Big5/CP1252 | `src/Eden.Catalog/**`, `Eden.sln` | `dotnet test` : 18 794 items + 7 461 mall parsés, 0 exception, noms FR présents ≥ 95 % | 0.x | M |
| 1.2 Registre `EntityKind` + `columns/*.json` | `IEntityKind`, DI, schéma colonnes `c_item` | `Kinds/`, `Schema/` | Test d'enregistrement vert ; `GET` fiche item id 1 conforme golden | 1.1 | M |
| 1.3 Mobs/PNJ/scènes | `MobKind`, `NpcKind` (via `WorldParsers`), lecture `[npc]`/`[reborn_monsters]` (extraire un parseur de scène partagé `[MOD]` ou recopier minimal côté Catalog) | `Kinds/`, `Sources/` | Mob 56001 → spawns avec (map,x,y) ; PNJ 50174 → store + map/coord | 1.2 | M |
| 1.4 `SourceGraph` | providers : drop (grammaire `DropAndSpeedParsers`), shop, quête, coffre | `Graph/`, `Sources/` | Tests : item connu → arête drop/shop/quête attendues ; `quality` renseigné | 1.3 | L |
| 1.5 Plans de craft | provider craft + `curation/plans.json` | `Sources/CraftSource.cs` | Un produit de `c_familycombine` affiche son plan cliquable ; échantillon 20 recettes vérifié à la main | 1.4, Q4 | M |
| 1.6 Donjons | provider donjon + `curation/dungeons.json` ; libellé « Donjon : … » | `Sources/DungeonSource.cs` | Mob de S104 → « Donjon : <nom> » ; liste de donjons validée vs `donjons-balayage` | 1.4, Q3 | M |
| 1.7 `Eden.Panel` squelette + `/api/v1/wiki/*` (items, mobs, pnj, search) | Program, endpoints génériques, ProblemDetails, pagination | `src/Eden.Panel/**` | `curl /api/v1/wiki/item/40002/sources` renvoie JSON conforme §5.3 ; recherche < 100 ms | 1.4 | M |
| 1.8 SPA wiki MVP | pages item/mob/pnj, recherche, fil d'Ariane, retour contexte | `Eden.Panel/wwwroot/**` | Parcours manuel : item → plan → item référent → Retour restitue la page d'origine ; capture jointe | 1.7 | L |
| 1.9 Badge écart serveur | branche rapport 0.2 dans `/meta` et fiches | `Api/Wiki/` | Item divergent affiche ⚠ ; compteur cohérent avec 0.2 | 0.2, 1.7 | S |

### Phase 2 — Wiki complet (L)

| Lot | Tâches | Critères d'acceptation | Dép. |
|---|---|---|---|
| 2.1 Quêtes | `QuestKind` (chaîne, prérequis, récompenses, PNJ) + liens item↔quête | 2 392 quêtes listées ; chaîne « L01→L02 » correcte (1009→1010) | 1.4 |
| 2.2 Donjons & cartes | pages donjon/map (boss, mobs, drops, PNJ) | Fiche S104 complète, boss HP = `c_monster` | 1.6 |
| 2.3 Classes/sorts/titres/succès/recettes ⭐ | kinds supplémentaires | chaque kind : liste + fiche + recherche | 1.2 |
| 2.4 Live view guildes | lecture PG `eden_panel_ro`, vue publique, PNJ de station, W-06 | Avec guilde 5002 (L3 ferme) : PNJ 53009 liste « devinguild » ; champs interdits absents (test) | 1.7, rôle PG |
| 2.5 Export statique ⭐ | `overlay-export-wiki` → dossier HTML/JSON | Dossier ouvrable hors serveur ; ≤ taille cible | 2.3 |
| 2.6 i18n FR/EN | bascule langue, repli | `?lang=en` renvoie EN si dispo sinon FR + `fallback:true` | 1.2 |

### Phase 3 — Système admin, sécurité, audit (L)

| Lot | Tâches | Fichiers | Critères d'acceptation | Dép. |
|---|---|---|---|---|
| 3.1 Schéma `admin` | migration 027 : grants, changesets, releases, audit, idempotency ; rôles PG | `eden_server/Database/027-admin-schema.sql` | Migration appliquée sur base de test ; `UPDATE admin.audit` refusé | ADR |
| 3.2 Auth | login Argon2 + TOTP ⭐, sessions, CSRF, niveaux par endpoint | `Eden.Panel/Auth/` | Test : niveau 1 reçoit 403 `E_FORBIDDEN_LEVEL` sur `/admin/give` ; mauvais TOTP rejeté | 3.1 |
| 3.3 Audit chaîné ⭐ | écriture transactionnelle, `audit-verify` | `Audit/`, `tools/audit-verify` | Altérer une ligne ⇒ `audit-verify` échoue | 3.1 |
| 3.4 Sync privilège `[MOD]` | `EntryState.Privilege` ← `max(db, grant)` ; révocation effective | `ZoneEntryFlow.EntryPipeline.cs:486` | Perso révoqué : `//give` refusé en jeu au relog (test live `tools/run.sh`) | 3.1 (ADR) |
| 3.5 Rate limit | middleware | `Program.cs` | 6e login/min ⇒ 429 | 3.2 |

### Phase 4 — Admin joueurs : give/remove (L)

| Lot | Tâches | Critères d'acceptation | Dép. |
|---|---|---|---|
| 4.1 Canal interne Zone `[MOD]` | `/internal/admin/*` sur mini-Kestrel + HMAC (nouveau `AdminBridge.cs`), liste connectés | `curl` interne sans HMAC ⇒ 401 ; `online` = résultat de `//users` | 3.x |
| 4.2 Give direct | via `GiveQuestItemAsync` ; idempotence | Give 2× même clé ⇒ 1 seul item en DB (`inventory1`) ; capture de l'item en jeu | 4.1 |
| 4.3 Give courrier ⭐ | `mailitem` + allocateurs ; hors-ligne | Perso hors-ligne reçoit le courrier à la connexion (preuve live) | 4.1 |
| 4.4 Remove | par id/sérial/or/quête ; audit avant/après | Remove puis `psql` : ligne absente, audit présent | 4.2 |
| 4.5 Actions de masse ⭐ | sélecteurs, dry-run, plafonds, confirmation | `bulk/preview` renvoie N ; `execute` > plafond ⇒ `E_RATE_LIMIT` ; random connecté journalise la seed | 4.2-4.3 |
| 4.6 Joueur : édition niveau/classe/or/position | via Zone (or via `HotCharacterStore`) | Modifier l'or d'un connecté survit au flush 15 s (test) | 4.1 |
| 4.7 Guildes (admin) | via World | Renommer une guilde ⇒ visible en jeu sans redémarrage ; `FamilyService` cohérent | 4.1 World |

### Phase 5 — Éditeur de données (overlay) (L, risque élevé)

| Lot | Tâches | Fichiers | Critères d'acceptation | Dép. |
|---|---|---|---|---|
| 5.1 Format changeset + compilateur | JSONL → `*.overlay.ini` (UTF-8) + `.del` + `lang/` ; round-trip | `Eden.Catalog/Overlay/` | Test round-trip `parse(emit(x))==x` sur 1 000 lignes `c_item` ; refus de `|` non échappé | 1.2 |
| 5.2 Hook fusion **serveur** `[MOD]` | `IniTable.ParseFile` serveur + `ParseNpcs` | `SRV/Eden.Core/Data/Parsers/IniTable.cs` | `tools/test.sh` vert ; ligne overlay remplace base ; tombstone retire | 5.1, ADR |
| 5.3 Hook fusion **client** `[MOD]` | `IniTable.ParseFile` client | `CLI/data/IniTable.cs` | NetTests : ligne overlay lue ; `.inib` base + overlay OK | 5.1 |
| 5.4 Draft/diff/publication | endpoints §5.4, intégrité `ReferenceIndex` | `Api/Admin/` | Supprimer item référencé ⇒ `E_REFERENCED` avec liste ; `force` niveau 5 | 5.1, 3.x |
| 5.5 Dev-session admin | `tools/admin-dev-overlay.sh` : overlay-dev.epk + `EDEN_PACKS` | `tools/` | Client strict affiche un item créé (capture, **vue réelle**) ; joueur normal (sans EDEN_PACKS) ne le voit pas | 5.3, mémoire strict-epk |
| 5.6 Release + activation serveur v1 | build overlay, `EDEN_OVERLAY_DIR`, redémarrage coordonné, rollback | `tools/overlay-release.sh` | R0001 active : item créé donnable ; rollback R0000 : item absent | 5.2, 5.4 |
| 5.7 Handshake `dataRev` `[MOD]` | ajout champ au hello + VersionGate | `VersionProtocol.cs`, `VersionGate.cs`, `EdenConnection.cs` | Client `dataRev` < actif ⇒ message « mise à jour requise » ; NetTests verts | ADR-C |
| 5.8 Spawns/scènes (v2) | sidecar `[reborn_monsters]` | — | hors v1 | 5.x |

### Phase 6 — Durcissement & hot-reload (M-L)
| Lot | Tâches | Critères |
|---|---|---|
| 6.1 Hot-reload v2 | `GameDataService.ReloadOverlay` en place + tests de charge | Release appliquée sans redémarrage ; 0 `InvalidOperationException` sous charge (`docs/DB-4-LOAD-TEST`) |
| 6.2 Migration P-B | `c_dropitem`, `c_npc`, `c_mission` côté serveur, un par un | Test « mêmes lignes serveur/client » par table |
| 6.3 Publication prod | 2e approbation, signature hors panel, `publish-overlay.sh` | Release signée acceptée par client strict ; panel sans accès à `sign.pem` |

**Ordre global** : 0 → 1 → (2 ‖ 3) → 4 → 5 → 6. Le wiki (1-2) est livrable seul et utile sans admin. La phase 5 est la plus risquée (3 `[MOD]` sur code existant) : ne la démarrer qu'après ADR validés.

**Preuves exigées par lot** (AGENTS.md) : build vert (`tools/build.sh`), `tools/test.sh`, sortie `curl`/`psql` collée dans la livraison, captures **de la vue réelle** (`tools/run.sh -- --login=test123:test123 --channel --char`) pour tout ce qui touche l'affichage en jeu ; régénérer le graphe (`tools/graph/graph.sh`) en fin de tâche.

---------------------------------------------------------------------------
## 12. Questions ouvertes (priorisées)
---------------------------------------------------------------------------

| P | # | Question | Reco de l'agent |
|---|---|---|---|
| 1 | Q1 | Valider l'**option A** (service `Eden.Panel` séparé) ? | Oui |
| 1 | Q2 | **Source de vérité** : wiki = tables FR `c_*` et on accepte l'écart avec le serveur US jusqu'à migration P-B ? Ou migrer d'abord le serveur ? | Wiki FR + badge d'écart, migrer ensuite |
| 1 | Q3 | Définition de « **donjon** » : liste curée (qui la fournit ?) ou règle `NodeDef.Type>=4` hors halls 501/502 ? | Règle + fichier de curation |
| 1 | Q4 | Comment repère-t-on le **plan de craft** (item référent) : un type d'item (Kind) précis, ou liste curée ? | Kind si identifiable, sinon curée |
| 2 | Q5 | Le wiki est-il **public** (hors connexion) ? Quels champs de guilde sont « publics » ? | Public lecture seule ; liste blanche §6.4 |
| 2 | Q6 | Plage d'**ids réservée** aux contenus créés (items/mobs/PNJ/quêtes) ? | ≥ 900 000 000 items ; à vérifier côté client (types int32 ?) |
| 2 | Q7 | Qui détient la **clé de signature de release** et comment publie-t-on aux joueurs (launcher inexistant) ? | Hors panel ; script ; launcher plus tard |
| 3 | Q8 | SPA sans build (Preact+htm) ou chaîne Vite/TS ? | Sans build |
| 3 | Q9 | Comportement client face à un **item inconnu** (non vérifié) : test à faire avant phase 5 | Test dédié lot 5.5 |
| 3 | Q10 | `ItemChest/LuckyBag/Lottery/ItemMall` et `LocateMask` FR : inclus en v1 ? Valeur `locate` FR du serveur ? | Hors v1 sauf coffres |
| 3 | Q11 | 2FA TOTP obligatoire dès v1 ou plus tard (usage local) ? | Obligatoire dès exposition réseau |
| 3 | Q12 | Règle des 4 yeux (2 admins) pour « give all » et publication prod ? | Oui en prod |
| 4 | Q13 | Contenu EN : à créer en parallèle ou FR seul (EN « en pause ») ? | FR seul, structure prête |

---------------------------------------------------------------------------
## Résumé (15 lignes)
---------------------------------------------------------------------------
1. Recommandation : service C# **`Eden.Panel`** (minimal API + SPA sans build) + lib **`Eden.Catalog`** réutilisant les parsers `Eden.Core` ; panel client Godot rejeté, Artifact seulement en export statique ⭐.
2. Découverte majeure : le serveur lit les tables **US `S_*`** (+5 overlays FR partiels) ; le client lit **FR `c_*`**. Drops/PNJ/quêtes réels ≠ ceux affichés → badge d'écart ⭐ et migration progressive du serveur vers FR.
3. Aucun hot-reload de tables (seulement `server.config.txt`) ; listes capturées par référence → v1 = redémarrage coordonné, v2 = mutation en place.
4. Édition = **overlay de changesets** → sidecars INI UTF-8 + tombstones, fusion via un hook unique `IniTable.ParseFile` des deux côtés ; EPK « dernier pack gagne » donne dev-session (`EDEN_PACKS`) et releases signées.
5. Textes FR hors Big5 : clé ASCII dans `c_*`, vrai texte dans `t_*` overlay par langue.
6. Graphe item↔source : drop/boutique/quête/coffre = bonne qualité ; PNJ→(map,x,y) via scènes ; plan de craft et donjon = heuristiques à curer ; guildes = live DB (PNJ de station via `FamilyCityRules`).
7. Admin par **personnage** (`admin.grants`, niveaux 1-5) ; aujourd'hui `privilege=5` figé à la création → sync à l'entrée de zone ⭐.
8. Écriture sur joueur connecté : jamais en DB (Redis hot, caches World/Zone) → canal interne Zone/World HMAC ; hors-ligne : courrier ⭐.
9. Garde-fous ⭐ : dry-run, plafonds, idempotence, audit chaîné immuable, 2FA, règle des 4 yeux, tombstones, rollback de release.
10. Désync client/serveur : `dataRev` au handshake (ADR requis), give de contenu `draft` bloqué.
11. Plan : 6 phases (Fondations → Wiki MVP → Wiki complet ‖ Admin sécurité → Give/Remove → Éditeur overlay → Durcissement) ; wiki livrable seul.
12. 3 modifications de code existant à valider (`[MOD]` : hook INI serveur/client, sync privilège, handshake).
13. 13 questions ouvertes, 4 prioritaires : option A, source de vérité FR/US, définition « donjon », identification du « plan de craft ».
14. Tout ajout de l'agent est marqué ⭐ dans le document.
15. Fichier : `docs/architecture/CONCEPT-WIKI-ADMIN.md`.
