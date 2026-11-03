# Plan — rebaser toutes les données sur l'officiel (FR/EN)

> **Date** : 2026-10-30 · **Type** : analyse de faisabilité + plan d'action
> (aucun code). Complète `ANALYSE-DONNEES-PROVENANCE.md`.
> **Question posée** : remplacer toutes les données Reforged + chinoises par
> « uniquement EDENFR » pour FR/EN — implications client, `.ini` serveur,
> PostgreSQL.
>
> **Verdict final (3ᵉ passe, audit exhaustif 8 canaux)** : **OUI, faisable** —
> EDENFR couvre à ~100 % le jeu de base **atteignable** ; les trous sont du
> contenu post-FR (serveur CN plus récent que les clients occidentaux) ou des
> points de compatibilité énumérables. Détail §1.

---

## 1. Verdict final — EDENFR est utilisable pour TOUT le contenu de base

> Audit exhaustif par **atteignabilité** (8 canaux : fichiers chargés, items,
> PNJ/monstres/zones/familiers, schémas de toutes les tables, uidata,
> assets, texte, couverture EN). La question « EDENFR était complet à
> l'époque » est vraie — **les trous résiduels sont du contenu post-ère-Aeria
> que le serveur CN exécute**, pas des manques du client FR.

### Ce qui est prouvé complet (atteignabilité lvl ≤ 90)

| Canal | Résultat vérifié |
|---|---|
| PNJ spawnés (toutes scènes serveur) | **0 manquant** dans `c_npc` FR |
| Monstres spawnés réels | **0 manquant** (les 648 absents = dummies invisibles 21xxx « event switch ») |
| Monstres ciblés par `MonsterKilled` des missions FR | **0 manquant** |
| Items référencés par missions FR (`<10000`) | **0 manquant** (union c_item+c_itemmall+c_itemchest) |
| Dialogues référencés par PNJ couverts | **1 seul id** : `32768` = sentinelle « pas de dialogue », pas un trou |
| Montures (clés ride des enchants serveur) | **100 %** dans `ridelist` FR |
| Sorts classes principales (561xx/562xx) | **100 %** |
| Familiers renards standards | présents sauf 2 formes combat 45054/45135 |
| **`1290` « Kitsune de l'Aube »** | **PRÉSENT dans `c_item` FR** (ligne 3406, icône I01488) — corrige le doc : absent de Reforged/US, pas d'EDENFR → **avec base EDENFR il devient affichable** (reste non-giveable côté serveur) |
| Scène S098 | présente chez FR, **absente chez US** |

### Les vrais trous atteignables (liste exhaustive à compléter)

| Trou | Canal | Source de complément |
|---|---|---|
| ~40-50 items quêtes classe/event (564-591, 629-782) | `S_Mission` actives L80+ | `S_Item` Big5 → lignes `c_item` (+ noms à traduire — **absents même de l'US**, serveur plus récent) |
| ~100 blueprints life-skill (1001-1099) | `S_LifeSkill` | idem `S_Item` |
| ~14 items marchands (1076-1094, 1236-1238) | `S_Store` | idem |
| ~20 items `S_Collection` (29762+, 1193-1195) | archive serveur | idem |
| **136 familiers évolution 45xxx** | `S_Partner` (œufs giveables) | **`c_partner` US** (présents) |
| 66 sorts (584xx familiers, 56050-99 éveil) | `S_Spell`/S_Partner | **`c_spell` US** (de toute façon requis — schéma) |
| `54990` + `61001-61613` enchants | référencés par items | **`c_enchant` US** (requis — schéma) |
| `51461-51467` daily award + `48052/49868` + `51663` | `S_DailyAward`/FamilyCombine/Spell | `S_Item*` |
| S057 + transport 57 (Candara, LV91-100) | portails serveur | US (optionnel — hors périmètre) |
| ~1 100 items mall 44xxx-51xxx | `//give` | ignoré (« itemmall je m'en fou ») |
| sorties de coffres | `S_ItemChest` (3× plus grand) | extraction Python requise — zones 90+ |
| ~95 missions 2xxx-34xx | `S_Mission` | dormantes/events — à classifier par le harnais phase 0 |

### Points de compatibilité (confirmés colonne par colonne)

| Problème | Détail exact | Fix |
|---|---|---|
| `c_spell` V.7→V.8 | colonne vide insérée à **idx38** → `ResultEnchantId`, `PayType`, `UpgradeBase` décalés → **arbres de sorts ClassWindow vides** | prendre `C_Spell` US, ou padder idx38 |
| `c_enchant` V.2→V.3 | colonne insérée à **idx6** → `IsBuff`/`NoAbandon`/cmds décalés | prendre `c_enchant` US, ou padder idx6 |
| `c_achievement` V.3→V.4 | cols 12-13 absentes → méta-succès muets | prendre US, ou tolérer |
| `c_collection` V.3→V.4 | col8 (groupe) absente → totaux par groupe Archive cassés | prendre US |
| `c_itemmallenchant` | même insertion idx6 — non lu par le client | ignorer |
| `t_*` = **CP1252**, pas UTF-8 | sniff UTF-8 échoue → repli Big5 → accents en hanzi | transcoder (déjà fait pour `lang/fr`) |
| `uidata` noms **minuscules** | lookups case-sensitives → `DejaVu.scheme` manque → **100 % fallback UI** en mode loose | garder Reforged uidata (contenu identique de toute façon) |
| Fichiers absents FR | `c_collectionpoint`, `C_DailyAward`, `t_level`, `t_charcolor`, `C_EquipType` (nom) | copier depuis US |
| `extracted/` **partielle** | map/model ~90 scènes extraites vs ~145 du manifeste ; `UI/map` 80/299 ; audio non extrait | tout est dans `pkg/` — **`pkgfs_unpack.py` existe déjà** (`eden_godot/tools/unpacker/`) |
| `lang/fr` : ~20 tables **jamais chargées** | `t_transport`, `t_destinyevent`, `t_collection`, `t_equipset`… présentes mais non lues → perte de FR gratuit | câbler le chargement (1 ligne chacun) |

### Non-bloquants confirmés

- **Audio/vidéo : jamais chargés** par le client Godot (aucune référence
  `.ogg/.wav/bgm/sound/movies` dans `scripts/`) → les manques d'extraction
  sont sans impact ; `bgm/` est d'ailleurs complet en loose (86 ogg).
- **uidata EDENFR = littéraux CN identiques** à US/Reforged (la loc vivait
  dans les tables) → remplacer n'apporte rien, casse la casse et perd
  emoji/limitless → **garder Reforged** (+ `arial.ttf` EDENFR en bonus).
- **S_* serveur inchangés** — le surplus 95+ est dormant à lvl 90.
- **PostgreSQL** — suit sans refonte ; migrations de resync déjà planifiées.

### Couverture EN sur base EDENFR (repli obligatoire FR **et** EN)

`T_*` US couvre ~100 % des ids EDENFR sauf : **dialogues ≈72-74 %** (bloc
25-39k ≈ 5 200 ids jamais portés US — zones FR tardives), itemmall ~184
(41-44xxx), 89 items (1100-1183 invocations + 1287-1291 dont **1290**),
10 familiers (48800-48809). → EN viable en surcouche **avec fusion par ID
et repli final FR** (un dialogue FR vaut mieux qu'un vide EN).

Conséquence architecturale (option recommandée) : inverser la couche texte —
**base `data/db/t_*` = EDENFR FR** (couche native complète), `lang/en/` =
US `T_*` (repli par id vers FR). La même fusion par ID de `TextResolver`
sert dans les deux sens ; le FR devient la couche toujours-complète.

---

## 2. Matrice des corpus (recensement)

| | Serveur `S_*` | US officiel | EDENFR | Reforged |
|---|---|---|---|---|
| Items | 17 257 | 17 180 | 17 309 | 17 027 (+customs) |
| Monstres | 12 638 | 12 896 | 10 923 | 13 033 (+customs) |
| Missions | 2 790 | 2 831 | **2 392** | 2 988 (+customs) |
| PNJ | 2 890 | 3 083 | **2 486** | 3 134 (+549xx) |
| Sorts | 4 231 | 4 485 | 4 165 | — |
| Familiers | 573 | 572 | **446** | — |
| Item mall | 8 407 | 8 404 | 7 461 | — |
| Ère | CN officiel tardif (~2018) | global tardif (contenu 95+, reliquats KR) | **data → 2018-09, trad → 2019-02** | US + dérives privées |

Alignement des comptages : **serveur ≈ US > EDENFR** — mais l'écart EDENFR
est entièrement constitué de contenu **non atteignable à lvl ≤90** (test,
désactivé `>=199`, events régionaux, L100, évolutions familiers) : cf. §1.
Reforged = base US + ajouts (+1 134 items texte, +123 sorts, +596 missions,
+648 PNJ, +316 montures, renommages).

---

## 3. Architecture cible « 100 % officiel »

```
CLIENT GODOT                          SERVEUR C#
────────────────                      ──────────
raw_assets → curated « official/ »     DATA_DIR = server_us/Data/db_decoded
  c_* base = EDENFR                      S_* natifs (INCHANGÉS — déjà officiel,
  + compléments id depuis US               contenu 95+ dormant à lvl 90)
    (4 tables schéma + trous §1)         EDEN_REFORGED_DB=off  ← couper l'overlay
  t_* base = EDENFR FR (option A)        → enchants/talents redeviennent natifs
  OU T_* EN = US (option B actuelle)     PostgreSQL = instances + migrations
  lang/<autre>/ = surcouche                de resync (itemmall, orphelins)
  ui_text_*.tsv (inchangés)
  t_npc_extra.ini (inchangé)
uidata : Reforged (inchangé — layouts
  identiques CN + casse EDENFR casse
  loose + perdrait emoji/limitless ;
  + arial.ttf EDENFR en bonus fonte)
scenes/maps : corpus actuel (cohérent
  serveur ; dropper les scènes Reforged
  S128/S138-143 si purge complète)
assets GLB/PNG : existants (≈ officiels,
  les modèles Reforged r8xx+/customs
  deviennent inertes — purge optionnelle)
```

> **Option A (recommandée, FR-primaire)** : `data/db/t_*` = tables FR EDENFR
> natives + `lang/en/` = `T_*` US → FR toujours complet (100 % natif),
> EN = surcouche avec repli par id vers FR. Aligne avec l'usage réel
> (joueurs FR, EN secondaire) et rend les 5 200 dialogues sans EN affichés
> en FR plutôt que vides.
> **Option B (structure actuelle)** : base EN US + `lang/fr` overlay →
> couverture FR ~95 % + repli EN par id. Moins bon sur les dialogues
> (les 25-39k n'ont pas d'EN → repli sur Big5 si fusion absente).
> Dans les deux cas la **fusion par ID** de `TextResolver` est requise —
> seul le sens de la fusion change (EN→FR vs FR→EN).

Ce qui restera chinois après la purge (résiduel, masqué ou cosmétique) :
noms Big5 dans les `c_*` (repli seul), args Big5 des paquets 316/474,
littéraux CN des layouts (traduits par TSV), noms de scènes Big5 (jamais
affichés), notes `fortune_bag` en base (jamais affichées).

---

## 4. Détail par couche

### 4.1 Données client (`data/db`) — **base EDENFR + compléments US par id**

- `c_*` EDENFR = Big5 identique octet-pour-octet aux champs non-ASCII
  (vérifié : item 1 `匕首的奧義祕傳書` identique US/EDENFR/Reforged). Même
  pipeline `IniTable`, même encodage, mêmes schémas `|V.x|N|`. Aucune
  conversion.
- `T_*` = **US** (EN officiel — EDENFR n'a pas de couche EN ; couvre tous
  les ids FR puisque US ⊃ EDENFR sur les ids atteignables).
- `lang/fr` = inchangé (EDENFR officiel, déjà en place).
- ⚠️ **4 tables en version US, jamais EDENFR** (dérive de schéma — les
  parsers C# attendent le schéma récent) :
  | Table | EDENFR | US/code | Casse si EDENFR injecté |
  |---|---|---|---|
  | `c_collection.ini` | V.3 (8 col) | V.4 (9 col) | **Archive = 0 carte** (garde `row<9`) |
  | `C_Spell.ini` | V.7 (44) | V.8 (45) | boutique sorts classe vide, stats décalées |
  | `c_enchant.ini` | V.2 (60) | V.3 (61) | valeurs gemmes/enchants décalées |
  | `c_achievement.ini` | V.3 (13) | V.4 (15) | perte objectifs/descriptions HF |
- ⚠️ Tables absentes d'EDENFR, à prendre chez US : `C_DailyAward` (sinon
  daily rewards mort), `c_collectionpoint` (récompenses archive),
  `s057` + `map/model/S057` + c_node:57 + transport 57 (LV91-100,
  optionnel), `T_CharColor`, `t_level`, `C_EquipType` (nom à adapter).
- **Compléments par id** (les vrais trous atteignables — liste §1) :
  `c_partner` +136 lignes (évolutions 45xxx dont renards 45054/45135 —
  US les a), `c_item`/`c_itemmall` depuis **US puis `S_Item`** pour les
  ids absent même des clients occidentaux (~40-50 objets de quêtes
  classe L80+, ~100 blueprints life-skill, ~14 stock marchands, ~20
  items collection, `51461-51467`, `48052/49868`, `51663` — noms Big5
  serveur à traduire FR), `c_spell`/`c_enchant` = versions US entières
  (schéma requis de toute façon, couvre les 66 sorts + 54990 + 61xxx).
- `ridelist.ini` : présent et complet côté FR (toutes clés serveur) —
  bonus vs Reforged qui ne l'avait pas.
- Aucun id Reforged n'est codé en dur dans le C# → le swap retire proprement
  tout le custom (65xxx, 549xx, Blighted Incursion, quêtes L85…).
- `IniTable` ne force pas le nb de colonnes ; loaders absents → vide gracieux
  (rien ne plante).
- ⚠️ `extracted/` est partielle — produire la base depuis `pkg/` via
  `eden_godot/tools/unpacker/pkgfs_unpack.py` (map/model ~145 scènes,
  UI/map 299, sound/bg m récupérables), pas depuis `extracted/` nu.

### 4.2 uidata — **garder Reforged** (ou US + extras)

- Les layouts officiels (EDENFR **et** US) contiennent les **mêmes littéraux
  chinois** que Reforged — la localisation native passait par les tables,
  jamais les layouts. Swapper n'apporte **rien** en FR et casse :
  casse minuscule des fichiers EDENFR (FileNotFound → 100 % fallback),
  font `font01.ttf` = Arial Latin-seul (tofu CJK), perte Coffee/limitless.
- Option « officiel pur » : US `UIData/` (434 layouts, casse correcte) +
  réinjecter depuis Reforged : `layouts/limitless/`, `FONT01.ttf` Sarasa,
  thème Coffee, 7 imagesets privés (AutoSellBg, Emoji…).
- `ui_text_*.tsv` restent nécessaires dans tous les cas (littéraux CN +
  chaînes C# en dur).

### 4.3 Scènes/maps — **garder le corpus aligné serveur**

- Spawn truth = serveur (`server_us/Data/scene`, ~250 fichiers, sur-ensemble).
- Reforged scene/map ≈ serveur (+ scènes privées droppables : S138-143,
  Blighted).
- EDENFR : manque S057, S128, S232, S285, S310-313, S407 + ~30 `map/model`
  (probable trou d'extraction) → entités serveur invisibles côté client.
- `.smp`/NodeMap : format partagé, inventaires quasi identiques (±S057,
  S744/745, s128) — garder ceux du corpus serveur.

### 4.4 `.ini` serveur — **conserver les natifs, couper l'overlay Reforged**

- `S_*` natifs = génération officielle la plus récente. **Ne PAS régénérer
  depuis EDENFR** — et le surplus 95+ qu'ils contiennent (missions L100,
  familiers évolution, events CN) est **dormant** à lvl 90 : trim optionnel
  de propreté, pas nécessaire au fonctionnement.
- Le schéma S_/c_ est identique (Big5, mêmes `|V.x|N|`) → une régénération
  partielle reste *possible* par id au cas par cas (ex. combler un trou
  précis), mais pas de remplacement global.
- **`EDEN_REFORGED_DB=off`** retire les deux seuls apports Reforged :
  enchants (→ retour `S_Enchant`+`S_ItemMallEnchant` natifs, adieu montures
  60xxx/Exalted — voulu) et talents (`C_Talent` Reforged primaire → retour
  `S_Talent` compat — **vérifier sa complétude avant**, cf. risques).
- Ids codés en dur à valider avant la coupure : enchant `65001`
  (DevMoveSpeedBuff — probablement Reforged-only → remplacer ou accepter la
  perte), `46605`, `54486`, plage quêtes `45001-59999`, procs `1031/2031…`,
  `S_Parameter` (jamais overlay — reste natif).
- `find_item`/`give` : `ItemNames` = `T_Item`+`T_ItemMall` du DATA_DIR
  (EN, déjà officielles — inchangé).
- **Correction** : `S_Partner` contient bien les renards (572 recs, ids
  41939/42446/42872/43175/43582/44882/48770/45054/45135 vérifiés) — le
  bug « œufs qui n'éclosent pas » n'est PAS un trou de table (cf. §6).

### 4.5 PostgreSQL — **suit sans refonte, 3-4 migrations de resync**

Aucun catalogue items en SQL (les defs = `.ini`). Schémas inchangés.
`item_serial` jamais rembobinée. Le rebase ne touche que des **valeurs
d'ids logiques** (pas de FK dures) :

| # | Migration | Contenu |
|---|---|---|
| 016 | `itemmall-resync` | `sell=0`/purge des 515 lignes `fnaccount.itemmall` dont `item_id` ∉ catalogue final ; purge `itemmall_limit_amount` orpheline ; annuler/rembourser `item_receivable` orphelines |
| 017 | `reward-tables-resync` | purge `item_id` orphelins dans `fortune_bag` (fndb1, 86 833 lignes — déjà officiel CN), `lottery`/`new_lottery`/`ingame_lottery`, `starway.item_id1-8` ; reset `player_fortune_bag`, `player_lottery_point` |
| 018 | `orphan-item-quarantine` | ~20 tables d'instances (inventory/equipment/storage/mailitem/collection/auction/fast_dress/shortcut_menu/trade…) → déplacer les `item_id` disparus dans `archive.*` (précédent migration 010) ; solder trades/enchères en vol avant |
| 019 | (optionnel) `quest-monster-resync` | si rebase missions/mobs : `mission_trace`, `*_mission_state*`, `statistics_monster` |

**Limites inhérentes** :
- **Aucune source officielle du catalogue boutique n'existe** — `itemmall`
  (mise en vente/prix) était serveur-side chez Aeria ; le script CN chargé
  (`server_us/Data/itemmall/itemmall`, 515 INSERTs) est la seule source →
  on ne peut que **filtrer**, pas « retrouver » un catalogue FR.
- `fortune_bag`/`lottery`/`starway` : pareil — sources CN officielles déjà
  en place, à conserver en filtrant les ids morts.
- Items orphelins en inventaire joueur : affichage `#id`/Big5, usages
  refusés proprement — la quarantaine est cosmétique, pas urgente.

### 4.6 Assets (GLB/PNG/audio)

- `assets/` convertis depuis Reforged : ~95 % = fichiers officiels →
  **rien à faire** ; les modèles Reforged-only (R8xx-9xx, TR*, JEFF.glb…)
  deviennent inertes (plus référencés) → purge optionnelle de disque.
- EDENFR a tous les assets bruts (nif/kf/dds → même pipeline) sauf
  **`bgm/`+`sound/`+`movies/` non extraits** (~2 561 fichiers) — Reforged
  les a ; le code n'y touche pas encore → garder le corpus Reforged.
- Renards/montures : `M282-M476`, `R253/254/255.glb` déjà présents.

---

## 5. Plan d'action (phases)

```
Phase 0 — Harnais de diff (1-2 j, LE chantier technique)
  parser ini robuste (binaire, champs multi-lignes, MyCrypt) produisant
  par table : version header, nb lignes, diff d'ids, diff de valeurs.
  Produit l'orphan-set « ids que le serveur atteint vs ids EDENFR » —
  liste attendue §1 (≈136 c_partner, ≈40-100+ c_item dont ids absents
  même de l'US → complément S_Item + traduction FR à produire).
  + extraction complète EDENFR : pkgfs_unpack.py sur EDENFR/pkg/
    (map/model ~145 scènes, UI/map 299, sound/bgm/movies).

Phase 1 — Base client officielle (quelques h)
  raw_assets → copie curated « official/ » = EDENFR c_* (depuis pkg, pas extracted/)
  + compléments par id (orphan-set phase 0)
  + 4 tables US : c_spell, c_enchant, c_achievement, c_collection
  + garde-fous : C_DailyAward, c_collectionpoint, T_CharColor, t_level,
    C_EquipType (nom), s057/S057/c_node:57/transport:57 (optionnel)
  + couche texte : option A (t_* FR base + lang/en US) ou B (inchangé)
  + câbler les ~20 tables lang/fr jamais lues (t_transport…)
  → build + run loose : vérifier Archive, fenêtre sorts, création perso.

Phase 2 — Serveur : retirer l'overlay Reforged (quelques h)
  EDEN_REFORGED_DB=off → vérifier S_Talent complet, enchants hardcodés
  (65001, 46605, 54486, plage quêtes), tests golden 254 verts.
  Purge scènes Reforged dans le corpus client si souhaitée.

Phase 3 — SQL resync (demi-journée + review)
  migrations 016-018 (+019 si missions/mobs rebasés) sur orphan-set calculé.

Phase 4 — Packaging + ménage (quelques h)
  rebuild package-linux/windows + .epk ; dropper Ini-decrypt-only.exe,
  T_Npc(1).ini, scènes/fichiers Reforged-only ; doc de périmètre.

Phase 5 — Validation live (la preuve)
  give d'items officiels (43774 fox mount ✔, 1290 ✘ attendu), Archive
  remplie, monture fonctionnelle, familier renard éclos, daily rewards,
  panneaux FR — captures Flameshot vs ref-shots.
```

Ordre de grandeur total : **~1 semaine** dont la moitié = harnais de diff
(phase 0) et validation live. Le swap de fichiers lui-même = trivial.

---

## 6. Points de vigilance / décisions à prendre

1. **Objectif réel ?** Si c'est « zéro CN à l'écran » : le swap officiel n'y
   suffit pas — il reste les littéraux CN des layouts (TSV), les args Big5
   wire, les repli `c_*`. Le levier #1 reste la **fusion par ID dans
   TextResolver** (FR→EN au lieu de FR→CN) — orthogonal au rebase.
2. **Renards familiers** : `S_Partner` a les defs ET `S_ItemMall` les œufs ET
   `c_partner` FR a les modèles (sauf 45054/45135 → compléments US) →
   le non-éclosion est probablement le **bug spawn z=0** déjà logué
   (`docs/temp-plans/2026-10-24-fix-live-lot5-*.md` §4) — à reproduire avant
   toute correction de table. Le rebase ne le corrige ni ne l'aggrave.
3. **1290 Kitsune de l'Aube** : présent dans `c_item` EDENFR (corrigé —
   absent de Reforged/US seulement) → **redevient affichable** en base
   EDENFR. Non-giveable quand même (pas de `S_Item` serveur) ; la chaîne
   craft/quest reste à recréer en custom si voulue.
4. **Contenu perdu volontairement** : daily rewards (C_DailyAward = US),
   talents Reforged, montures Exalted/60xxx, tout le contenu L85+/Blighted —
   confirmer que « Eden de base lvl 90 » = bien la cible (cohérent avec
   knowledge/ hors-périmètre lvl 95+). Le choix EDENFR vs US comme base
   `c_*` tranche justement ce point : EDENFR = purge nette du 95+, US =
   tout garder affichable.
5. **`fnaccount.itemmall`** : ignoré (hors sujet à ta demande) — la resync
   des ids morts reste recommandée un jour, pas urgente.
6. **Dépendance croisée** : couper `EDEN_REFORGED_DB` rend le serveur
   indépendant du vieil arbre `eden_server/` — c'est aussi un nettoyage
   structurel.
7. **Font CJK** : si uidata passe officiel un jour, garder `FONT01.ttf`
   Sarasa (noms CN de joueurs/chat restent possibles).

---

## 7. Non vérifié / limites

- Canaux non couverts par l'audit : `S_ItemChest` (sorties exactes, 4 627
  vs 1 538 lignes), `S_ItemCombo`, `S_ClassDropItem`, `S_LuckyBag`,
  `S_Lottery` (formats binaires/CSV — extraction Python requise) ;
  classifications lvl des missions 2xxx-34xx manquantes (dormant/event
  présumé) ; `MonsterKilled 3xxxx`.
- Le client US pourrait contenir ses propres dérives locales (DJ_127.0.0.1 =
  install dev, reliquats KR) — à auditer comme Reforged l'a été.
- `fnaccount.itemmall`, `privilege`, volumes fndb1 réels : pas d'accès live.
- Comportement runtime réel post-swap : aucun test effectué (analyse
  statique) ; mission 1011 `MonsterKilled` 3 serveur vs 6 FR (missions
  révisées côté serveur — divergence de valeurs, pas d'ids).
- L'option A (t_* FR en base) nécessite la même fusion par ID que l'option B
  + vérifier les 34 fichiers attendus au bon emplacement/casse.

---

## Annexe — sources de preuve

| Fait | Preuve |
|---|---|
| Serveur ≈ génération US > EDENFR | comptages tables §2 + S_Partner 573≈US 572, S_ItemMall 8 407≈US 8 404 |
| Trous EDENFR = contenu non atteignable | 0 PNJ/0 monstre réel spawné manquant (scan toutes scènes S*) ; 648 « monstres » = dummies 21xxx ; missions manquantes = `>=199`/« 測試用 »/L100 ; 0 item quête <10000 manquant |
| EDENFR data ≈ 2018-09 | `GameDataFileList.txt` : max `:20180920163027:` ; trad `:20190222115131:` |
| EDENFR = Big5 (pas GBK) | bytes nom item 1 identiques `A4 50 AD BA…` = `匕首的奧義祕傳書` Big5 |
| S_Partner contient les renards | `grep -a` ids 41939/42446/42872/43175/43582/44882/48770/45054/45135 = 1 chacun (572 recs) |
| 4 tables schéma-dérivées | headers V.3/V.7/V.2/V.3 EDENFR vs V.4/V.8/V.3/V.4 code |
| uidata layouts = littéraux CN | 10 398 occurrences Han dans layouts EDENFR, 0 mot FR |
| itemmall = script CN officiel | 515 INSERTs identiques `server_us/Data/itemmall/itemmall` ↔ backup fnaccount |
| 6 ids itemmall déjà morts | 51973/52291/51994/51990/52204/52277 absents S_Item+S_ItemMall |
| Aucun id Reforged hardcodé C# | grep 549xx/65xxx/6021x/41939+ = 0 hit data |
| Loaders graceful-on-missing | tous les loaders try/except ou early-return |
