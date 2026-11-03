# Atlas et attestation des maps (EDENFR, client natif Godot 4.8)

Généré le 2026-11-02 (2e balayage complet, code « globe_data » actif). Données : `docs/reference/maps.tsv`
(1 ligne par node, 446 ids). Outils : `tools/map-attest/` (`build_list.py`, `attest.sh`, `run-all.sh`,
`parse_log.py`, `sheet.py`, `build_docs.py`). Résultats bruts : `artifacts/map-attest/results.tsv`,
captures et logs : `artifacts/map-attest/shots/`.

## 1. Ce que prouve ce document, et ce qu'il ne prouve pas

Prouvé (par lancement réel du client natif, moteur épinglé, mode solo) : la map **se charge**
(ligne `[MapLoader]`), la géométrie résout (propre, `globe_data` de scène ou donneur minimap),
0 placeholder d'objet ou le nombre exact, aucune `ERROR`/`texture absente`/exception **avant**
la capture, spawn résolu, capture non vide **regardée**. La marche réelle est prouvée sur S228
(`--walk-test` : 33,4 u au sol puis blocage net contre un mur) et le blocage-prop sur S001.
**Ce n'est pas « jouable »** : gameplay, quêtes, serveur/live, monstres, combats, portails
entre maps n'ont **pas** été vérifiés. Les `ERROR` émis après `[shot]` (fuites RID à l'arrêt
moteur) sont du bruit de fermeture, ignorés.

## 2. Comment un node devient une géométrie (preuve par le code)

* `GameScene.cs` et `LoadingScreen.cs` : code map = `S{nodeId:D3}` (id du node, **pas** la col. 5 de `c_node`).
* `GameScene.MapExists(code)` — vrai si l'une des trois entrées résout :
  1. `map/model/{code}/{code}.fsm` ou GLB/NIF `map/model/{code}/{code}` (géométrie propre) ;
  2. **`data/scene/{code}.ini` dont `[globe_data]` col0 déclare un modèle existant** — mécanisme
     EDENFR natif : la scène propre du node désigne sa géométrie partagée (191 nodes, ex.
     `s137.ini` → `S055.fsm`, `s996.ini` → `S403.nif`). `MapLoader` charge alors la géométrie
     déclarée (fsm/nif/smp/env suivent le code géométrique) **en gardant la scène propre du node**
     (portails, zones, PNJ, spawn) ;
  3. donneur `NodeGeometry.Resolve` : colonne 5 `c_node` puis minimap `S###M`→`S###` — pour les
     nodes sans scène propre (38 : 30 confirmés par nom, 8 incertains). Ils chargent alors la
     scène **entière** du donneur.
* `MapLoader.LoadMap` : `[globe_data]` de `data/scene/{mapCode}.ini` est autoritaire pour le
  stem géométrique ; `.fsm` → terrain + objets + collisions + `sknodemap/{geo}.smp` ; sinon
  modèle NIF unique ; à défaut de scène, `local.ini [Scene] ModelName` (S995 → S402).
* La résolution des dossiers est insensible à la casse (`s204/` et `S204/` fusionnent leurs fichiers).
* Spawn (`GetSpawnPoint`) : REVIVE → GATEWAY → position born → première position → premier
  `event_area` (portail d'entrée, ex. S406/S620) → `local.ini [Player] pos` → case libre `.smp`
  la plus proche du centre (S994) → centre terrain.
* Preuve d'absence d'archive : les dossiers `map/model/*` du corpus sont **exactement** ceux de
  `GameDataFileList.txt` — un stem absent n'a jamais été livré.

## 3. Règles de classification (appliquées par `build_docs.py`)

| Classe | Règle | Nb |
|---|---|---|
| `JOUABLE_NOMMEE` | ligne `c_node`, nom FR dans `t_node`, géométrie propre `S{id}` | 126 |
| `JOUABLE_SANS_NOM_FR` | idem sans nom FR (403, 620-623) | 5 |
| `SANS_GEO_SCENE_DECLAREE` | pas de dossier propre ; `data/scene` déclare la géométrie (`globe_data`) | 191 |
| `SANS_GEO_DONNEUR_CONFIRME` | pas de scène propre ; minimap vers un node existant de même nom | 30 |
| `SANS_GEO_DONNEUR_INCERTAIN` | idem, nom différent | 8 |
| `T_ONLY_GEO` | pas de `c_node`, nom FR + géométrie (S228..S231 `<Mode Multijoueur>`) | 4 |
| `DEV_TEST_GEO` / `DEV_TEST_NOGEO` | marqueurs dev `測試/暫名/暫用/格子世界/Place Holder/Test Cottage` | 7 + 7 |
| `LABEL_UI` | id seulement dans `t_node` (libellés de listes UI de donjon) | 63 |
| `VIDE` | id sans `c_node` ni nom | 5 |
| `LACUNE_*` | **0** — tout node nommé résout une géométrie | 0 |

Géométries orphelines (dossier `map/model` sans node `c_node`/`t_node`) : S128, S299, S405
(+ scènes hors-table S756-758, S908 — chargeables via `--map`).

## 4. Attestation (389 codes, mode loose, 0 FAIL)

Des 446 ids : 389 codes ont été lancés en solo (376 du balayage + 13 scène-déclarés hors liste).
Comptes `results.tsv` : **174 OK, 215 WARN, 0 FAIL**. `OK` = chargé, 0 placeholder, 0 erreur,
image non plate ; `WARN` = chargé avec réserve listée — toutes les réserves restantes sont des
**lacunes authentiques EDENFR** (§5), pas des régressions.

Causes des WARN :
* `texture absente : UI/map/S###s.png` (211 codes) : images de minimap jamais livrées pour ces
  nodes — lacune authentique (le HUD replie sur la carte de la géométrie).
* `texture absente : map/texture/water_env_5.dds` (S055, S137 — G20) : absente du manifeste.
* `6 placeholders` (S033 — G19) : `ue758_2.nif` (variante buisson) jamais livré.
* `image sombre/plate` (S127, S710, S793) : grottes/abîmes authentiquement noirs — géométrie
  visible à l'inspection.

### Anomalies précédentes — état après corrections

* **Caméras NIF-unique** (ex-~80 anomalies) : **corrigées** — scan descendant/montant des helpers
  de collision (sols `up-facing`, priorité floor puis shell), spawn replié event_area → `.smp`,
  `globe_data`/`ModelName` pour le stem, `FogCeiling` sur le chemin modèle-unique. S228-S231,
  S198/199, S401, S406, S620, S621, S994, S995 vérifiées visuellement au sol.
* **`add_child` NativeFxAttach** (S001/S006/S027) : **corrigé** (création différée) — 0 erreur
  au re-test.
* **Warnings `[toon]` faux positifs** (~35 maps) : **corrigés** — les helpers `no_collision` et
  les nœuds cachés du NIF ne comptent plus ; re-attestés OK.
* Surfaces plates résiduelles : aucune restante hormis les noirs authentiques ci-dessus.

## 5. Lacunes authentiques (archive) restantes

Toutes documentées dans `docs/workflows/EDENFR-GAPS.md` — données absentes d'EDENFR, jamais
remplacées par un bouche-trou :

* **G19** `map/model/object/ue758_2.nif` : 6 objets de S033 (variante de `ue758.nif`, jamais livrée).
* **G20** `map/texture/water_env_5.dds` : 56 tuiles d'eau de S055 (héritée par S137 via globe_data).
* `UI/map/S###s.png` ×211 : minimaps des nodes scène-déclarés/donneurs — l'archive ne les a pas.
* `map/map.nif` (G12) : fichier PE illisible — seul NIF non parsé (couverture native 6420/6421).

## 6. Constats et corrections (état 2026-11-02)

* **C1 — résolu** : `MapExists`/`LoadMap` acceptent la géométrie déclarée par la scène
  (`globe_data`) — 191 nodes scène-déclarés + les 5 ex-« orphelins » (132, 309, 599, 766, 996)
  chargent leur vraie map avec **leur scène propre** ; 38 nodes sans scène passent par le
  donneur minimap/`NodeGeometry`. Validé visuellement (S650 titre propre + géométrie S003,
  S137 portails propres sur géométrie S055).
* **C2 — corrigé** : `add_child` pendant le setup remplacé par création différée dans
  `NativeFxAttach`/`NifScene` — 0 erreur au re-test (S001/S006/S027).
* **C3 — résolu** : collisions NIF-unique (helpers `true wall`/sols → corps consolidés), spawn
  au sol par scan des surfaces `up-facing`, replis event_area/`.smp`/`local.ini`.
* Déplacement réel : `--walk-test` (pipeline `Input.ParseInputEvent`) prouvé sur S228 (marche
  + suivi de pente + blocage mur) et S001 (blocage contre barrière prop).

## 7. Non vérifié

Gameplay/live : quêtes, PNJ interactifs, monstres, combats, portails entre maps, serveur,
audio, météo, performances, session multi-heures. La partie *scene* des nodes scène-déclarés
(portails propres) n'est validée qu'en statique — traversée réelle non testée.

## 8. Table par map attestée

Colonnes : code, nom FR, classe, statut, loader, objets collision, NPC de scène, note.

| code | nom FR | classe | statut | loader | coll | npc | note |
|---|---|---|---|---|---|---|---|
| S001 | Aven | JOUABLE_NOMMEE | OK | fsm | 234 | 205 |  |
| S002 | Monts Calcaires | JOUABLE_NOMMEE | OK | fsm | 68 | 51 |  |
| S003 | Colline Tranquille | JOUABLE_NOMMEE | OK | fsm | 158 | 59 |  |
| S004 | Forêt Arc-en-Ciel | JOUABLE_NOMMEE | OK | fsm | 90 | 30 |  |
| S005 | Monts Arides | JOUABLE_NOMMEE | OK | fsm | 111 | 31 |  |
| S006 | Baie Bélouga | JOUABLE_NOMMEE | OK | fsm | 85 | 62 |  |
| S007 | Bourbes | JOUABLE_NOMMEE | OK | fsm | 65 | 36 |  |
| S008 | Vallée des Rois | JOUABLE_NOMMEE | OK | fsm | 107 | 29 |  |
| S009 | Volcan Avila | JOUABLE_NOMMEE | OK | fsm | 224 | 36 | caméra proche d'une plante géante au spawn (position authentique de la scène) |
| S010 | Plaines Dorées | JOUABLE_NOMMEE | OK | fsm | 118 | 82 |  |
| S011 | Terres des Dunes Éternelles | JOUABLE_NOMMEE | OK | fsm | 192 | 42 |  |
| S012 | Forêt de Delphes | JOUABLE_NOMMEE | OK | fsm | 138 | 36 |  |
| S013 | Pic de Noireflamme | JOUABLE_NOMMEE | OK | fsm | 225 | 60 |  |
| S014 | Marais Décrépits | JOUABLE_NOMMEE | OK | fsm | 104 | 30 |  |
| S015 | Hautes-terres | JOUABLE_NOMMEE | OK | fsm | 60 | 73 |  |
| S016 | Crocs Brumeux | JOUABLE_NOMMEE | OK | fsm | 66 | 19 |  |
| S018 | Cabinet du Maire | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | intérieur NIF unique : spawn au sol restauré (scan descendant helpers, 2026-11-02) |
| S019 | Vallée Feregal | JOUABLE_NOMMEE | OK | fsm | 61 | 32 |  |
| S020 | Désert du Couchant | JOUABLE_NOMMEE | OK | fsm | 64 | 33 |  |
| S021 | Jungle Céleste | JOUABLE_NOMMEE | OK | fsm | 62 | 29 |  |
| S022 | Forêt Ensorcelée | JOUABLE_NOMMEE | OK | fsm | 322 | 47 |  |
| S023 | Crête Diamantée | JOUABLE_NOMMEE | OK | fsm | 239 | 37 |  |
| S024 | Pic du Maëlstrom | JOUABLE_NOMMEE | OK | fsm | 454 | 41 |  |
| S025 | Vallée de la Mort | JOUABLE_NOMMEE | OK | fsm | 206 | 57 |  |
| S026 | Plaines Fleuries | JOUABLE_NOMMEE | OK | fsm | 117 | 67 |  |
| S027 | Falaise des Grands Vents | JOUABLE_NOMMEE | OK | fsm | 241 | 61 |  |
| S028 | Lagune Enchantée | JOUABLE_NOMMEE | OK | fsm | 238 | 44 |  |
| S029 | Côte de Hurlevent | JOUABLE_NOMMEE | OK | fsm | 239 | 77 |  |
| S030 | Jungle du Dieu Serpent | JOUABLE_NOMMEE | OK | fsm | 270 | 37 |  |
| S031 | Île de Sakura | JOUABLE_NOMMEE | OK | fsm | 235 | 59 |  |
| S032 | Vallée de Vulcain | JOUABLE_NOMMEE | OK | fsm | 132 | 59 |  |
| S033 | Plaines du Levant | JOUABLE_NOMMEE | WARN(6 placeholders) | fsm | 127 | 128 | 6 placeholders ue758_2 : variante jamais livrée dans EDENFR — lacune authentique G19 |
| S034 | Royaume Dévasté | JOUABLE_NOMMEE | OK | fsm | 268 | 79 |  |
| S035 | Palais Heldentod | JOUABLE_NOMMEE | OK | nif-unique |  | 26 | intérieur NIF unique : spawn au sol restauré (scan descendant helpers) |
| S036 | Plaines d'Elos | JOUABLE_NOMMEE | OK | fsm | 88 | 236 |  |
| S037 | Marina d'Edouard | JOUABLE_NOMMEE | OK | fsm | 293 | 141 |  |
| S038 | Royaume Durango | JOUABLE_NOMMEE | OK | fsm | 239 | 35 |  |
| S039 | Meteor's Landing | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | ANOMALIE corrigée : spawn (0,0,0) → repli event_area puis case libre .smp |
| S040 | Scenic Aurapoint | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | intérieur NIF unique : spawn au sol restauré |
| S041 | Forteresse d'Éternité | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S042 | Bill's Training Facility | JOUABLE_NOMMEE | OK | fsm | 194 | 2 |  |
| S043 | Braider Army Base | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | intérieur NIF unique : spawn au sol restauré (plus de caméra sous terrain) |
| S044 | Tente 2 | DEV_TEST_GEO | OK | nif-unique |  | 0 | map dev (Tente 2) : géométrie unique, rendue |
| S045 | Tunnel de l'Union de Valence | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S046 | Profondeurs du Mont Gowen | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S047 | Faubourg Populaire de la Marina d'Edouard | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S048 | Domaine de la Noblesse de la Marina d'Edouard | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S049 | Congrès de Braider | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S050 | Tour Kahlo | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S051 | Dôme Céleste | JOUABLE_NOMMEE | OK | fsm | 79 | 43 |  |
| S052 | Hauteurs Feudegivre | JOUABLE_NOMMEE | OK | fsm | 182 | 24 |  |
| S053 | Meeryasha | JOUABLE_NOMMEE | OK | fsm | 172 | 58 |  |
| S054 | Jungle Mayland | JOUABLE_NOMMEE | OK | fsm | 191 | 172 |  |
| S055 | Bassin Shaxia | JOUABLE_NOMMEE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 107 | 13 | lacune authentique G20 : water_env_5.dds référencée par 56 tuiles, absente d'EDENFR (rendu correct malgré tout) |
| S093 | Errea | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 234 | 155 |  |
| S094 | Aven rêvée | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 234 | 149 |  |
| S095 | Vieil Entrepôt | JOUABLE_NOMMEE | OK | nif-unique |  | 4 | intérieur NIF unique : spawn au sol restauré |
| S096 | Île aux Cerisiers | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 235 | 65 |  |
| S097 | Île du Mariage | JOUABLE_NOMMEE | OK | nif-unique |  | 3 | crépuscule authentique restauré (FogCeiling) |
| S098 | Arena Dungeon Place Holder | DEV_TEST_GEO | OK | fsm | 219 |  | map dev : îles flottantes, rendue |
| S099 | Création de Personnage | JOUABLE_NOMMEE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 239 | 0 | création de personnage : minimap S099s.png absente = lacune authentique |
| S100 | Mine Andrew | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | intérieur NIF unique : spawn au sol restauré |
| S101 | Carrière Angor | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | cave bleue authentique restaurée (FogCeiling ajouté au chemin modèle-unique) |
| S102 | Fort Verdure | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | intérieur NIF unique : spawn au sol restauré |
| S103 | Canal Tassos | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | intérieur NIF unique : spawn au sol restauré |
| S104 | Caverne de Schiste | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | intérieur NIF unique : spawn au sol restauré |
| S105 | Éroda | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | intérieur NIF unique : spawn au sol restauré |
| S106 | Morticora | JOUABLE_NOMMEE | OK | fsm | 4 | 0 |  |
| S107 | Racine Branda | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | intérieur NIF unique : spawn au sol restauré |
| S108 | Palais des Malices | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | intérieur NIF unique : spawn au sol restauré |
| S109 | Pourriture | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | intérieur NIF unique : spawn au sol restauré |
| S110 | Abîme de Baram | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | sombre authentique : fog noir + parois Editable Poly non texturées (abîme par conception) |
| S111 | Coeur Baltaroï | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S112 | Rêve du Maire | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S113 | Fléau | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S114 | Chambre de Baïra | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S115 | Crypticône | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S116 | Temple de Delphes | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S117 | Veine Tordue | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S118 | Autel du Culte de Mara | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S119 | Chutes des Âmes en Peine | JOUABLE_NOMMEE | OK | fsm | 16 | 0 |  |
| S120 | Vortek | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S121 | Arsenal Fédéral | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S122 | Sturmfrau | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S123 | Abysse Glacée de Skleros | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S124 | Cratère des Géants | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S125 | Antre des Échos | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S126 | Sanctuaire du Mal à l'État Pur | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S127 | Grotte de la Marée des Ombres | JOUABLE_NOMMEE | WARN(image sombre/plate (auto)) | nif-unique |  | 0 | grotte sombre authentique (Marée des Ombres) : géométrie et racines visibles |
| S128 | (orphelin) | ORPHELIN_GEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  |  |  |
| S129 | Lieu du rituel (niv. 80 - Vérité et Illusion IV) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S130 | Orphidée | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S131 | Banque Meeryasha | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S132 | Maison de Marjorie | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S133 | Pays des souvenirs | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S134 | Tunnel éboulé | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S135 | Jungle Mystique | JOUABLE_NOMMEE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 134 | 0 |  |
| S136 | Palais Heldentod | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 24 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S137 | Bassin Shaxia | SANS_GEO_SCENE_DECLAREE | WARN(2 texture absente; 2 ligne(s) ERROR/absent) | fsm | 107 | 0 | Bassin Shaxia : géométrie S055 + scène propre S137 (globe_data) — water_env_5.dds = G20 |
| S176 | Tour des Épreuves | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S177 | Caverne de Schiste | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S178 | Palais des Malices | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S179 | Fort Loselle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 0 |  |
| S180 | Écarlate | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S181 | Royaume Vide | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 235 | 2 |  |
| S182 | Temple Perdu | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 43 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S183 | Plaines Séraphines | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S184 | Domaine Démoniaque Intérieur | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S185 | Tombe du Samouraï | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 3 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S186 | Pays des Merveilles Diablepourpre | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 81 | 2 |  |
| S187 | Palais des Bourbes | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S188 | Vallée du Sacrifice | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 16 | 1 |  |
| S189 | Donjon d'Éveil de Classe - Illusionniste | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 141 | 0 |  |
| S190 | Class Awakening Dungeon (Place Holder) | DEV_TEST_NOGEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 159 | 1 |  |
| S191 | Class Awakening Dungeon (Place Holder) | DEV_TEST_NOGEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S192 | Class Awakening Dungeon (Place Holder) | DEV_TEST_NOGEO | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S193 | Class Awakening Dungeon (Place Holder) | DEV_TEST_NOGEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S194 | Class Awakening Dungeon (Place Holder) | DEV_TEST_NOGEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S195 | Version de la Cabane n°1 | JOUABLE_NOMMEE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S196 | Version de la Cabane n°2 | JOUABLE_NOMMEE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S197 | Nouvelle Cabane n°3 | JOUABLE_NOMMEE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S198 | Test Cottage : Océan | DEV_TEST_GEO | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S199 | Test Cottage : Chaleur | DEV_TEST_GEO | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S201 | Base des Vilrequins | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S202 | Salle Ulta | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S203 | Crocvenin | JOUABLE_NOMMEE | OK | fsm | 74 | 1 |  |
| S204 | Écarlate | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S205 | Siècle | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S206 | Manoir | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S207 | Frontière de Basel | JOUABLE_NOMMEE | OK | fsm | 81 | 11 |  |
| S208 | Prison Ossuaire | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S209 | Guillotine | JOUABLE_NOMMEE | OK | fsm | 235 | 1 |  |
| S210 | Temple Spirenfer | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S211 | Temple de la Rose | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S212 | Labo Vingot | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S213 | Cap Hurlant | JOUABLE_NOMMEE | OK | fsm | 141 | 1 |  |
| S214 | Abysse de Siècle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 3 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S215 | Grotte Non Répertoriée | JOUABLE_NOMMEE | OK | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S216 | Dimension Dévastée | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S217 | Cratère Météoritique <Mode Multijoueur> | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S218 | Tour Kahlo <Mode Multijoueur> | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S219 | Iyernafalo <Multijoueur> | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S220 | Terre sacrée de Kamodo (multijoueur) | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S221 | Temple aquatique <multijoueur> | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S222 | Labo Antigravité <multijoueur> | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S223 | Grey Lynn <multijoueur> | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S224 | Bataille Spatiotemporelle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 60 | 1 |  |
| S225 | Vallée Batelo (Groupe) | JOUABLE_NOMMEE | OK | fsm | 1 | 0 |  |
| S226 | Allolans (DJN multijoueur) | JOUABLE_NOMMEE | OK | fsm | 159 | 0 |  |
| S227 | Temple enchanté (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 159 | 2 |  |
| S228 | Fort Verdure <Mode Multijoueur> <Évaluation> | T_ONLY_GEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  |  | sonde --walk-test : 33,4 u puis blocage net contre mur — collisions vérifiées |
| S229 | Canal Tassos <Mode Multijoueur> <Évaluation> | T_ONLY_GEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  |  | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S230 | Caverne de Schiste <Mode Multijoueur> <Évaluation> | T_ONLY_GEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  |  | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S231 | Éroda <Mode Multijoueur> <Évaluation> | T_ONLY_GEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  |  | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S280 | Tour des Cieux Hall du Purgatoire | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S281 | Tour des Cieux Autel Sinistre | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S282 | Tour des Cieux Salle Fleurépine | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S283 | Tour des Cieux Zone de la Mélodie Sacrée | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S284 | Laboratoire de Réno | JOUABLE_NOMMEE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S299 | (orphelin) | ORPHELIN_GEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  |  |  |
| S301 | Bataille de la Forêt | JOUABLE_NOMMEE | OK | fsm | 74 | 8 |  |
| S302 | Bataille du Désert | JOUABLE_NOMMEE | OK | fsm | 58 | 10 |  |
| S303 | Bataille de l'Océan | JOUABLE_NOMMEE | OK | fsm | 128 | 13 |  |
| S304 | Champ de Bataille T.4 | JOUABLE_NOMMEE | OK | fsm | 218 | 15 |  |
| S305 | Zone de Guerre d'Ezur | JOUABLE_NOMMEE | OK | fsm | 22 | 89 |  |
| S306 | Île du Boucanier | JOUABLE_NOMMEE | OK | fsm | 25 | 70 |  |
| S307 | Toundra Hostile | JOUABLE_NOMMEE | OK | fsm | 123 | 55 |  |
| S308 | Arène Sacrée : Le Crucifix | JOUABLE_NOMMEE | OK | nif-unique |  | 14 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S309 | Survie sur l'Île Déserte | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 235 | 0 | géométrie S031 (globe_data) + scène propre — fog de distance authentique |
| S401 | Arène Phénix | JOUABLE_NOMMEE | OK | nif-unique |  | 2 | arène Phénix : spawn z=7,6 authentique restauré (globe_data propre S401.nif) |
| S402 | Arène des Boss | JOUABLE_NOMMEE | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S403 |  | JOUABLE_SANS_NOM_FR | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S404 | Prison du Fantôme Enflammé | JOUABLE_NOMMEE | OK | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S405 | (orphelin) | ORPHELIN_GEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  |  |  |
| S406 | Temple Antique du Chaos | JOUABLE_NOMMEE | OK | nif-unique |  | 4 | spawn replié sur event_area (pas de born/revive/position) |
| S501 | Forêt d'Eden | JOUABLE_NOMMEE | OK | fsm | 83 | 4 |  |
| S502 | Mont Eden | JOUABLE_NOMMEE | OK | fsm | 43 | 4 |  |
| S599 | Villa Personnelle | SANS_GEO_SCENE_DECLAREE | OK | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S601 | Base des Vilrequins Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S602 | Salle Ulta Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S603 | Carrière Angor Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S604 | Fort Verdure Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S605 | Coeur Baltaroï Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S606 | Crocvenin Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 0 |  |
| S607 | Canal Tassos Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S608 | Carrière de Schiste Héroïque | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S609 | Épreuve : Écarlate | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S610 | Épreuve : Siècle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S611 | Épreuve : Manoir | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S612 | Épreuve : Éroda | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S613 | Épreuve : Morticora | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 4 | 0 |  |
| S614 | Épreuve : Palais des Malices | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S615 | Épreuve : Fléau | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S616 | Épreuve : Racine Branda | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S617 | Épreuve : Rêve du Maire | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S619 | Expédition Anti Dragon - Autel Volcanique | JOUABLE_NOMMEE | OK | nif-unique |  | 11 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S620 |  | JOUABLE_SANS_NOM_FR | OK | nif-unique |  | 0 | spawn replié sur event_area (id. S406) |
| S621 |  | JOUABLE_SANS_NOM_FR | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S622 |  | JOUABLE_SANS_NOM_FR | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S623 |  | JOUABLE_SANS_NOM_FR | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S624 | Épreuve : Frontière de Basel | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 81 | 0 |  |
| S625 | Épreuve : Arsenal Fédéral | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S626 | Épreuve : Sturmfrau | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S627 | Épreuve : Abysse Glacée de Skleros | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 8 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S628 | Épreuve: Base des Vilrequins S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S629 | Épreuve: Salle Ulta S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S630 | Épreuve : Angor S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S631 | Épreuve : Fort Verdure S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S632 | Épreuve : Cœur Baltaroï S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S633 | Épreuve: Crocvenin S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 0 |  |
| S634 | Épreuve: Canal Tassos S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S635 | Épreuve: Caverne de Schiste S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S636 | Épreuve: Écarlate S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S637 | Épreuve: Siècle S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S638 | Épreuve: Manoir S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S639 | Épreuve: Éroda S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S640 | Épreuve: Morticora S | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 4 | 0 |  |
| S641 | Épreuve : Antre des Échos | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 23 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S642 | Épreuve : Prison Ossuaire | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S643 | Épreuve : Cratère des Géants | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S644 | Épreuve : Vieux Siècle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S645 | Épreuve : Cratère des Géants | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 235 | 1 |  |
| S646 | Épreuve : Abysse de Siècle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 141 | 0 |  |
| S647 | Épreuve : Temple Spirenfer | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S648 | Épreuve : Temple de la Rose | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S649 | Épreuve des Héros du Chemin d'Occultus | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 3 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S650 | Épreuve des Héros de la Colline Tranquille | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 158 | 0 | Épreuve des Héros : géométrie S003 + scène propre (globe_data) — validé visuellement |
| S651 | Trésor : Cœur Baltaroï | LABEL_UI | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S652 | Trésor : Crocvenin | LABEL_UI | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 0 |  |
| S653 | Trésor : Canal Tassos | LABEL_UI | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S654 | Trésor : Caverne de Schiste | LABEL_UI | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S655 | Illusion Destructrice : Carrière Angor (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S656 | Illusion Destructrice : Fort Verdure (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S657 | Illusion Destructrice : Canal Tassos (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S658 | Illusion Destructrice : Caverne de Schiste (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S659 | Illusion Destructrice : Éroda (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S660 | Illusion Destructrice : Morticora (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 4 | 0 |  |
| S661 | Illusion Destructrice : Base des Vilrequins (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S662 | Illusion Destructrice : Racine Branda (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S663 | Illusion Destructrice : Palais des Malices (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S664 | Illusion Destructrice : Salle Ulta (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S665 | Illusion Destructrice : Cœur Baltaroï (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S666 | Illusion Destructrice : Rêve du Maire (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S667 | Illusion Destructrice : Crocvenin (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 0 |  |
| S668 | Illusion Destructrice : Fléau (DJN multijoueur) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S669 | Illusion Destructrice : Écarlate (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S670 | Illusion Destructrice : Siècle (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S671 | Illusion Destructrice : Frontière de Basel (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | fsm | 81 | 11 |  |
| S672 | Illusion Destructrice : Arsenal Fédéral (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S673 | Illusion Destructrice : Manoir (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S674 | Illusion Destructrice : Cratère des Géants (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S675 | Illusion Destructrice : Sturmfrau (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S676 | Illusion Destructrice : Antre des Échos (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S677 | Illusion Destructrice : Abysse Glacée de Skleros (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S678 | Illusion Destructrice : Guillotine (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | fsm | 235 | 1 |  |
| S679 | Illusion Destructrice : Prison Ossuaire (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S680 | Illusion Destructrice : Cap Hurlant (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | fsm | 141 | 1 |  |
| S681 | Illusion Destructrice : Abysse de Siècle (DJN multijoueur) | SANS_GEO_DONNEUR_INCERTAIN | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S682 | Illusion Destructrice : Grotte Non Répertoriée (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S683 | Illusion Destructrice : Temple de la Rose (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S684 | Illusion Destructrice : Temple Spirenfer (DJN multijoueur) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S701 | Carrière Angor | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S702 | Fort Verdure | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S703 | Canal Tassos | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S704 | Caverne de Schiste | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S705 | Éroda | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S706 | Morticora | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 4 | 0 |  |
| S707 | Racine Branda | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S708 | Palais des Malices | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S709 | Pourriture | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S710 | Abîme de Baram | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent; image sombre/plate (auto)) | nif-unique |  | 0 | Abîme de Baram : sombre authentique (parois organiques visibles) |
| S711 | Coeur Baltaroï | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S712 | Rêve du Maire | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S713 | Fléau | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S714 | Chambre de Baïra | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S715 | Crypticône | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S716 | Temple de Delphes | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S717 | Veine Tordue | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S718 | Autel du Culte de Mara | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S719 | Chutes des Âmes en Peine | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 16 | 0 |  |
| S720 | Vortek | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S721 | Arsenal Fédéral | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S756 | (orphelin) | ORPHELIN_GEO | OK | nif-unique |  |  |  |
| S757 | (orphelin) | ORPHELIN_GEO | OK | nif-unique |  |  |  |
| S758 | (orphelin) | ORPHELIN_GEO | OK | nif-unique |  |  |  |
| S764 | Bataille d'Hélios [Groupe] | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 85 | 10 |  |
| S765 | Sortir des méandres du Fort (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S766 | Passage spatiotemporel | SANS_GEO_SCENE_DECLAREE | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S767 | Une cave où l'on entrepose de la bière. | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S768 | Canal d'Aven | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S769 | Arène du Démon des Enfers - Assemblée | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S770 | Courez ! Des vaches ! Courez ! | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S771 | Dimension du chaos <Groupe> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S772 | Purgatoire : Arène des Chevaliers Templiers | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 4 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S773 | Course d'Alpagas | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 66 | 26 |  |
| S774 | Ville de Guilde : Couloir de l'Esprit Gardien | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S775 | Aventure fabuleuse : Cœur de Dragon | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 6 |  |
| S776 | Terrains d'Entraînement | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 58 | 40 |  |
| S777 | Forêt Camoufle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 4 | 0 |  |
| S778 | Gardien de l'Arbre de Vie  <1 joueur> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 161 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S779 | Garde du Cimetière <Event de groupe> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S780 |  | SANS_GEO_DONNEUR_INCERTAIN | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S781 |  | SANS_GEO_DONNEUR_INCERTAIN | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S782 |  | SANS_GEO_DONNEUR_INCERTAIN | OK | fsm | 74 | 8 |  |
| S783 | Conte de légende : les voleurs de trésor et le lutin légendaire. | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S784 | Les 5 Animaux : La légende de Bonka <Event 1 joueur> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S785 | Les 5 Animaux : La légende de Yula <Event 1 joueur> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 61 | 5 |  |
| S786 | Les 5 Animaux : La légende de Barlilu <Event 1 joueur> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S787 | Les 5 Animaux : La légende de Ron <Event 1 joueur> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 128 | 2 |  |
| S788 | Les 5 Animaux : La légende de Maschatta <Event 1 joueur> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S789 | Défi des Boss <Groupe> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S790 | Le Pouvoir du Cristal d'Eternal <Groupe> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S791 | Génération Future <Groupe> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 10 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S792 | Profondeurs du Canal Tassos | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S793 | Abîme de Baram | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent; image sombre/plate (auto)) | nif-unique |  | 1 | Abîme de Baram : sombre authentique (géométrie partagée avec S710) |
| S794 | Mine du Village Andrew | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 3 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S795 | Temple de Delphes | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 3 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S796 | Les Profondeurs de l'Abysse Glacée de Skleros | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S797 | Ancien Autel | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S798 | Refuge des Sang Noir | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S799 | Salle Ulta | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S801 | Base des Vilrequins | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S802 | Salle Ulta | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S803 | Crocvenin | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 1 |  |
| S804 | Écarlate | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S805 | Siècle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S806 | Manoir | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S807 | Frontière de Basel | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 81 | 11 |  |
| S808 | Sturmfrau | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S809 | Abysse Glacée de Skleros | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S810 | Cratère des Géants | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S811 | Antre des Échos | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S812 | Prison Ossuaire | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S813 | Guillotine | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 235 | 1 |  |
| S814 | Cap Hurlant | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 141 | 1 |  |
| S815 | Abysse de Siècle | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 3 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S816 | Temple Spirenfer | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S817 | Temple de la Rose | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S818 | Grotte Non Répertoriée | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S819 | Cratère Météoritique <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S820 | Grotte de la Marée des Ombres <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S821 | Tour Kahlo <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S822 | Iyernafalo <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S823 | Terre sacrée de Kamodo <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S824 | Temple aquatique <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S825 | Labo Antigravité <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S826 | Grey Lynn <Solo> | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S827 | Vallée Batelo (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 1 | 0 |  |
| S828 | Allolans (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 159 | 1 |  |
| S829 | Illusion Destructrice : Carrière Angor (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S830 | Illusion Destructrice : Fort Verdure (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S831 | Illusion Destructrice : Canal Tassos (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S832 | Illusion Destructrice : Caverne de Schiste (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S833 | Illusion Destructrice : Éroda (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S834 | Illusion Destructrice : Morticora (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 4 | 0 |  |
| S835 | Illusion Destructrice : Base des Vilrequins (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S836 | Illusion Destructrice : Racine Branda (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S837 | Illusion Destructrice : Palais des Malices (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S838 | Illusion Destructrice : Salle Ulta (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S839 | Illusion Destructrice : Cœur Baltaroï (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S840 | Illusion Destructrice : Rêve du Maire (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S841 | Illusion Destructrice : Crocvenin (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | fsm | 74 | 0 |  |
| S842 | Illusion Destructrice : Fléau (Solo) | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S843 | Illusion Destructrice : Écarlate (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S844 | Illusion Destructrice : Siècle (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S845 | Illusion Destructrice : Frontière de Basel (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | fsm | 81 | 11 |  |
| S846 | Illusion Destructrice : Arsenal Fédéral (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S847 | Illusion Destructrice : Manoir (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S848 | Illusion Destructrice : Cratère des Géants (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S849 | Illusion Destructrice : Sturmfrau (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S850 | Illusion Destructrice : Antre des Échos (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S851 | Illusion Destructrice : Abysse Glacée de Skleros (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S852 | Illusion Destructrice : Guillotine (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | fsm | 235 | 1 |  |
| S853 | Illusion Destructrice : Prison Ossuaire (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S854 | Illusion Destructrice : Cap Hurlant (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | fsm | 141 | 1 |  |
| S855 | Illusion Destructrice : Abysse de Siècle (Solo) | SANS_GEO_DONNEUR_INCERTAIN | OK | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S856 | Illusion Destructrice : Grotte Non Répertoriée (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S857 | Illusion Destructrice : Temple de la Rose (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S858 | Illusion Destructrice : Temple Spirenfer (Solo) | SANS_GEO_DONNEUR_CONFIRME | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S901 |  | SANS_GEO_DONNEUR_INCERTAIN | OK | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S902 |  | SANS_GEO_DONNEUR_INCERTAIN | OK | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S903 |  | SANS_GEO_DONNEUR_INCERTAIN | OK | nif-unique |  | 1 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S908 |  | VIDE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 2 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S994 | Zone déclenchant automatiquement les quêtes | DEV_TEST_GEO | OK | nif-unique |  |  | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S995 |  | DEV_TEST_GEO | OK | nif-unique |  |  | géométrie S402 via local.ini ModelName + fog blanc authentique de s995.ini |
| S996 |  | SANS_GEO_SCENE_DECLAREE | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | géométrie S403 via globe_data — ex-« orphelin » sauvé par la résolution de scène |
| S997 | Emplacement de l'Event | DEV_TEST_NOGEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S998 | Emplacement d'Événement | DEV_TEST_NOGEO | WARN(1 texture absente; 1 ligne(s) ERROR/absent) | nif-unique |  | 0 | NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies) |
| S999 | Plan de Grille | DEV_TEST_GEO | OK | fsm | 0 | 0 |  |
