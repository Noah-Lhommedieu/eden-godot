# Plan de migration complet — base EDENFR (client + serveur + PostgreSQL)

> **État 31/10 soir** : phases 0-1 **exécutées** — corpus `corpus/official/`
> assemblé (45 075 fichiers pkg + compléments US), audit **0 orphelin
> atteignable**, client lancé live dessus (S009, FR ok). Rapport complet :
> `RAPPORT-CORPUS-OFFICIEL.md`.

> **Date** : 2026-10-31 · **Statut** : audit exhaustif terminé, plan d'action.
> Consolidation finale de `ANALYSE-DONNEES-PROVENANCE.md` +
> `PLAN-REBASE-OFFICIEL.md` (ce document fait foi).
>
> **Objectif** : remplacer l'intégralité du contenu en jeu par le corpus
> officiel — base **EDENFR** (client FR Aeria) pour les données client,
> compléments US uniquement là où le serveur exécute du post-ère-FR —
> et aligner serveur + base de données en conséquence.

---

## 0. État vérifié (audit — tout est mesuré, rien d'estimé)

### PostgreSQL — accès réel confirmé et audité

`eden-postgres` (postgres:12) tourne sur `:5432`. `fnaccount`, `fndb1`,
`fnmember` présents. Résultats live :

| Table | État réel | Verdict |
|---|---|---|
| `fnaccount.itemmall` | 515 lignes (498 `sell=1`) — **0 orphelin** vs `S_Item`∪`S_ItemMall` ; **6 ids absents des tables FR** (44982, 51609, 51664, 51689, 51772, 51773 — tous dans US `c_item`) ; **aucune monture renard en vente** (give-only) | sain ; 6 ids à complémenter |
| `fndb1.fortune_bag` | 86 833 lignes / **7 474 ids distincts** — **0 orphelin serveur**, mais **682 ids absents des tables FR** (~9 % des loots possibles s'afficheraient `#id`) | complément US requis pour l'affichage |
| `player_characters` | **5 persos `privilege>0`** | give/admin OK |
| Séquences | `item_serial` partagée (migration 012) | ne pas rembobiner |

**Conséquence majeure : la DB n'a besoin d'aucune migration structurelle
pour le rebase** — le catalogue items reste dans les `.ini` serveur qui ne
changent pas. Les seuls travaux SQL = resync opportuniste déjà planifiée
(ids morts) + audits de cohérence, pas une refonte.

### Serveur — live

`Eden.Login :6543`, `Eden.World :5200`, `Eden.Zone :5201` tournent
(C#/.NET actif). Validation live possible **maintenant**.

### EDENFR — extraction complète possible

- `extracted/` est **partielle** (~90/145 scènes `map/model`, 80/299
  `UI/map`, audio non extrait) → **ne jamais l'utiliser comme source**.
- `pkg/` (448 fichiers, 9 Go) + `pkg.idx` → **`pkgfs_unpack.py` du dépôt
  fonctionne** (45 064 entrées listées) → corpus FR complet récupérable.
- `bgm/` complet en loose (86 ogg, 356 M) ; Godot ne charge **aucun**
  audio/vidéo → sans impact.

### Couverture EDENFR — vérifiée par atteignabilité (§1 du plan rebase)

0 PNJ spawné manquant, 0 monstre réel manquant, 0 monstre de quête, 0 item
de quête <10000, montures 100 %, sorts de classe 100 %, `1290` Kitsune
présent dans `c_item` FR. Tous les trous atteignables ci-dessous sont
couverts par US (vérifié id par id).

### Tables « binaires » — en fait du texte, parsées

| Table serveur | Refs items | Manquants FR | Nature | Couvert US ? |
|---|---|---|---|---|
| `S_ItemChest` | 3 094 | **378** | essences 45xxx/29xxx, items mall, `1421` | ✅ tous |
| `S_ItemCombo` | 658 | **0** | — | — |
| `S_LuckyBag` | ~1 | **0** | — | — |
| `S_Lottery` (CSV) | 56 | **0** | — | — |
| `S_ClassDropItem` | 538 | **0** | — | — |

### Missions <3500 absentes FR (95) — classification exacte

- **~70 dormantes** : `If_CharLevel >= 199` (désactivées), « 測試用 »
  (test dev), chaînes `L15.` événementielles quotidiennes, titres
  Coupe du Monde/HK — **ignorer**.
- **25 réellement atteignables = chaînes d'éveil de classe `L80.`**
  (3464-3467 巫醫 Witch Doctor, 3475-3478 幻術師 Illusionist,
  3480-3483 遊俠 Ranger, 3484-3487 賢者 Sage, 3488-3491 龍騎士 Dragon
  Knight, 3496-3499 魔劍士 Magic Knight) + **3479 `L90.` 研究室的繼承者**.
  Donneurs **spawnés dans des maps de base** (S001, S014, S015, S024,
  S025, S027, S028, S033, S053). FR possède déjà les chaînes L80
  Paladin/Voleur (3452-3459) — ce sont les **6 autres classes éveillées**
  qui manquent.
- **Toutes couvertes par US** : 25/25 missions, 6/6 PNJ intermédiaires
  (51902, 51996, 51999, 52023, 52031, 52032), items de quêtes (564+),
  scènes S188/S192 (absentes FR, présentes US).
- Divergence de valeur : mission 1011 `MonsterKilled 3` serveur vs `6`
  FR — le serveur a révisé des missions existantes (diff de valeurs, pas
  d'ids — la table c_mission US est la bonne version).

---

## 1. Décisions d'architecture à trancher

| # | Décision | Options | Recommandation |
|---|---|---|---|
| D1 | Couche texte | **A** : `t_*` FR en base (`data/db/`), `lang/en` = US en surcouche ; **B** : base EN US, `lang/fr` overlay (statu quo) | **A** — FR natif 100 %, EN repli→FR par id ; les 5 200 dialogues sans EN affichent du FR au lieu de CN/vide |
| D2 | `uidata` | EDENFR vs Reforged vs US | **Reforged** — layouts EDENFR = littéraux CN identiques + noms minuscules cassent les lookups loose + perte emoji/limitless ; bonus : copier `arial.ttf` EDENFR |
| D3 | S057/Candara (LV91-100) + transport 57 | inclure depuis US / exclure | **Exclure** — hors périmètre lvl 90 ; laisser documenté |
| D4 | Contenu serveur 95+ dormant | garder tel quel / purger `S_*` | **Garder** — non atteignable à lvl 90, purge = risque sans gain |
| D5 | Overlay serveur Reforged | `EDEN_REFORGED_DB=off` / garder | **off** — mais seulement après vérification talents + enchants hardcodés (risque ci-dessous) |
| D6 | Items post-FR sans noms FR/EN (loot-tables, gear L93-98) | traduire / laisser Big5 / masquer | **Repli EN puis FR** via la fusion par id ; les ~40 items de quêtes L80 ont des noms US → traduire les ~40 noms FR en plus |
| D7 | `fnaccount.itemmall` | auditer/filtrer / ignorer | Ignoré par l'utilisateur ; resync cosmetic seulement (0 orphelin déjà) |

---

## 2. Architecture cible

```
raw_assets → corpus/official/            SERVEUR (inchangé sauf overlay)
  data/db/                               DATA_DIR = server_us/Data/db_decoded
    c_*,C_* = EDENFR (pkg, pas extracted)  S_* natifs CN (~2018, autorité)
    + compléments US (orphan-set §3)     EDEN_REFORGED_DB=off (phase 4)
    + 4 tables US (schéma)               → enchants/talents natifs
    t_* = EDENFR FR (option A)           PostgreSQL = instances (inchangé)
    lang/en/ = US T_* (option A)           + resync opportuniste (phase 5)
    gamedata/ overlays projet (inchangé)
uidata/ = Reforged (+ arial.ttf)
map/model, data/scene = EDENFR pkg + US S057/S188/S192 (compléments)
assets/ = conversion pipeline existante (seuls les nouveaux fichiers)
```

---

## 3. Liste des compléments (orphan-set consolidé — tout vérifié)

### 3a. Depuis le client **US** (`eden_server/client/data/db/` + assets)

| Quoi | Ids/fichiers | Pourquoi |
|---|---|---|
| `c_mission` | +25 lignes 3464-3499, 3479 (+ ~70 dormantes si choix « tout copier ») | chaînes éveil L80 atteignables |
| `c_npc` | +6 : 51902, 51996, 51999, 52023, 52031, 52032 (+option : les 183 dormant 51xxx/52xxx pour complétude) | PNJ mid-chain L80 |
| `c_item` | items de quêtes 564-591, 629-782 (~40-50) ; blueprints 1001-1099 (~100) ; store 1076-1094, 1236-1238 ; collection 29762+, 1193-1195 ; daily 51461-51467 ; family 48052/49868 ; spell-cost 51663 ; chest 29xxx/44xxx/45xxx/1421 (378) ; itemmall-vente 44982, 51609, 51664, 51689, 51772, 51773 ; fortune_bag ids (682) | tout ce que le serveur envoie et que FR n'a pas |
| `c_partner` | +136 lignes 45xxx (incl. renards 45054/45135) | familiers giveables |
| `C_Spell` | **version US entière** (V.8 — schéma requis) + couvre les 66 sorts manquants | idx38 — sinon arbres ClassWindow vides |
| `c_enchant` | **version US entière** (V.3) + 54990, 61001-61613 | idx6 — sinon flags buff cassés |
| `c_achievement` | **version US entière** (V.4) | cols 12-13 méta-succès |
| `c_collection` | **version US entière** (V.4) | col8 groupe Archive |
| `C_DailyAward` | fichier entier | absent FR, requis daily rewards |
| `c_collectionpoint` | fichier entier | absent FR |
| `T_CharColor`, `t_level` | fichiers | absents FR |
| `C_EquipType` | renommer `EquipType.ini` FR → `C_EquipType.ini` | mismatch de nom |
| Scènes | `data/scene/` + `map/model/` : **S057, S188, S192** (+ S098 si voulu — FR l'a, pas US) | PNJ de quêtes L80 / optionnel hors périmètre |
| `ridelist` | version FR (complète) — garder FR | bonus vs Reforged qui ne l'a pas |

### 3b. Depuis **`S_Item`/Big5 serveur** (absents même de l'US — post-clients)

Ids de loot-tables et gear L93-98 (994-1068…) — **hors périmètre** :
ne les injecter que si le cap monte un jour. Les 40-50 items de quêtes
L80 ont des noms US — traduction FR des ~40 noms à produire dans `t_item`.

### 3c. Code client (C#)

| Changement | Effort | Effet |
|---|---|---|
| **Fusion par ID dans `TextResolver.Ensure`** (charger base puis surcharger lang par id — les deux sens option A/B) | ~30 lignes | **le levier #1** — plus aucun repli Big5 en texte affiché |
| Câbler ~20 tables `lang/<lang>` jamais lues (`t_transport`, `t_destinyevent`, `t_collection`, `t_equipset`, `t_title` fusion EN, dialogue parlé repli, `t_enchant` repli EN…) | ~1 ligne chacune | FR/EN gratuits déjà livrés |
| `EdenVfs` fallback case-insensitive via `AssetCatalog.ResolveFile` | ~10 lignes | immunise la casse EDENFR (minuscules) en loose |
| `CharCreateScreen` via TextResolver (au lieu de lecture directe `T_Class`) | petit | création perso traduite |

### 3d. Serveur C#

| Changement | Note |
|---|---|
| `EDEN_REFORGED_DB=off` | après vérif `S_Talent` natif complet + enchants hardcodés `65001`/`46605`/`54486` + plage quêtes `45001-59999` (cf. §4) |
| Encodage NCID 3 | client écrit CP950, serveur lit UTF-8 (`ZoneEntryFlow.Admin.cs:33`) — corrige le give de noms accentués — **bug orthogonal, à plier** |
| `S_*` | **inchangés** — c'est l'autorité qui rend l'audit valide |

### 3e. PostgreSQL

| Action | Détail |
|---|---|
| Aucune migration requise | catalogue = `.ini` inchangés ; `itemmall`/`fortune_bag` = 0 orphelin serveur (vérifié live) |
| Optionnel : resync cosmétique | migrations 016-018 déjà rédigées (orphans, `itemmall_limit_amount`, `item_receivable`) — indépendantes du rebase |
| `itemmall` vente : 6 ids hors FR | corrigés par le complément `c_item` US ci-dessus (affichage boutique) |

---

## 4. Plan d'action par phases

### Phase 0 — Corpus complet + harnais de diff *(1-2 j — le chantier technique)*

1. `python3 eden_godot/tools/unpacker/pkgfs_unpack.py extract
   Téléchargements/EDENFR/pkg/pkg.idx → corpus/fr-full/` (45 064 entrées).
2. Extraire `corpus/us/` = `eden_server/client/` (déjà loose).
3. Écrire `tools/audit/ini_diff.py` : parser robuste (Big5, `$7$`
   multi-lignes, headers `|V.x|N|`/`V.x,N,`/`[INFO]`, CSV) → par table :
   header, colonnes, set d'ids, diff ids, diff valeurs. **Réutiliser pour
   produire `orphan-set.json`** : tous les ids que le serveur référence
   (missions+spawns+shops+drops+chests+combos+lifeskill+collection+
   partner-eggs+ride-keys+daily) vs ids corpus final.
4. Critère de sortie : `orphan-set` vide après compléments ; liste des
   missions 2xxx-34xx classifiées dormant/reachable.

### Phase 1 — Corpus curated `corpus/official/` *(quelques h)*

1. `data/db/` = EDENFR `c_*`/`C_*`/`t_*` depuis `corpus/fr-full/`.
2. Appliquer §3a : compléments US par id (script merge — pas de copie
   aveugle : ajouter les lignes manquantes par id, jamais écraser une
   ligne FR existante sauf les 4 tables schéma).
3. Scènes : `data/scene/` + `map/model/` EDENFR + S057/S188/S192 US.
4. Rejouer le harnais → **0 orphelin atteignable**.
5. Basculer `raw_assets` → `corpus/official/` (lien symbolique —
   Reforged reste intact en archive).

### Phase 2 — Texte & code client *(1 j)*

1. Décision D1 (recommandée A) : `data/db/t_*` = FR ; `lang/en/` = US
   `T_*` transcodés UTF-8 ; garder `lang/fr/` comme surcouche pour les
   compléments maison (Reforged-ids éventuels conservés, sinon vide).
2. `TextResolver` : fusion par ID (base→lang), tests `NetTests` sur les
   deux modes (id FR-seul → FR en mode en ; id EN-seul → EN en mode fr).
3. Câbler les ~20 tables non lues + fallbacks (§3c).
4. Patch `EdenVfs` casse (~10 lignes) — filet de sécurité.
5. Build `0 erreur` + `dotnet test tools/NetTests` verts.

### Phase 3 — Assets *(quelques h)*

1. Diff `corpus/official/` vs `assets/` convertis : seuls les nouveaux
   chemins (scènes US, modèles 45xxx familiers `m4xxxx`, montures ride
   manquantes) passent la pipeline existante — la majorité est déjà
   convertie (mêmes fichiers sources).
2. `UI/itemicon/` : icônes des nouveaux items ; `UI/map/` complet
   (299 fichiers pkg vs 80 extracted).
3. `arial.ttf` EDENFR → `uidata/fonts/` (option fonte FR-accentuée).

### Phase 4 — Serveur *(quelques h)*

1. Pré-vérifs avant `EDEN_REFORGED_DB=off` : `S_Talent` natif = subset
   complet de `C_Talent` Reforged ? enchants `65001` (DevMoveSpeedBuff),
   `46605`, `54486`, plage quêtes `45001-59999`, procs `1031/2031` :
   grep code serveur → classer Reforged-only vs natif.
2. `EDEN_REFORGED_DB=off` → `dotnet test` + golden 254 verts.
3. Fix encodage NCID 3 (cp950 serveur côté `ZoneEntryFlow.Admin.cs`).
4. Rien d'autre ne change : `S_*` restent l'autorité.

### Phase 5 — PostgreSQL *(demi-journée)*

1. Rejouer les audits live post-compléments : `itemmall`, `fortune_bag`,
   instances (`inventory1/2`, `equipment1/2`, `storage`, `collection`,
   `mailitem`) — orphelins attendus : 0 nouveau.
2. Appliquer les migrations de resync opportunistes (016-018) si désiré
   — **indépendantes**, peuvent attendre.
3. Vérifier `privilege` des comptes de test (5 GM existants).

### Phase 6 — Packaging & ménage *(quelques h)*

1. `tools/package_dist.sh` : pointer sur `corpus/official/` (adapter le
   `cp -aL $RAW/data`) ; rebake `.inib` ; rebuild linux + windows + epk.
2. Dropper : `Ini-decrypt-only.exe`, `T_Npc(1).ini`, `T_Spell - Copie`,
   scènes Reforged-only (S128, S138-143, Blighted), ids 65xxx/549xx.
3. Mettre à jour `docs/INDEX.md` + `AGENTS.md` (nouvelle source de
   données, conventions `corpus/official/`).

### Phase 7 — Validation live *(la preuve — serveur déjà UP)*

Matrice de test live (give + scénarios) :

| Test | Commande | Attendu |
|---|---|---|
| Monture renard | `//give 43774` | monture Blazing Fox FR, ride OK |
| Kitsune 1290 | `//give 1290` | « hors tables » attendu (non giveable) |
| Œuf renard | `//give 41939` + éclosion | familier visible (→ vérifie bug z=0 !) |
| Familier 45xxx | `//give 45054` | modèle affiché (complément US) |
| Quête L80 classe | interroger NPC 50127/50774… | mission éveil affichée FR/EN |
| Item quête L80 | `//give 564` | nom EN/FR + icône |
| Archive | ouvrir | cartes groupées (c_collection V.4) |
| Arbres sorts | fenêtre classe | remplie (C_Spell V.8) |
| Daily | ouvrir | récompenses (C_DailyAward) |
| Dialogues | NPC zones tardives | FR ou EN, jamais vide/CN |
| Création perso | relancer | textes FR |
| `//give` nom FR | `//give Renard` | échec attendu (lookup = Big5/EN) — doc |

Captures Flameshot vs `ref-shots/` pour chaque surface modifiée.

### Phase 8 — Bascule & rollback

- Rollback opérationnel : `eden_godot/switch-corpus.sh reforged` +
  `EDEN_CLIENT_DB=off` côté serveur — procédure complète dans
  `docs/ROLLBACK-CORPUS.md`. L'ancien contenu reste intact dans
  `eden_server/reforged/EdenReforged/` (oracle).
- `EDEN_CLIENT_DB` = override env (défaut `corpus/official/data/db`) ;
  `off` = natives `S_*` complètes.
- DB : aucune écriture destructive (audits lecture seule) — rien à
  annuler côté PostgreSQL.

---

## 5. Risques

| Risque | Probabilité | Mitigation |
|---|---|---|
| `S_Talent` natif incomplet → `EDEN_REFORGED_DB=off` casse les talents | moyen | vérifier phase 4.1 ; sinon garder overlay talents seul |
| Enchant `65001`/quêtes `45001-59999` hardcodés = Reforged-only | moyen | remap vers natif ou accepter la perte documentée |
| Missions L80 complétées US mais PNJ intermédiaires spawnés dans scènes hors-FR (S188/S192) | faible | complément scènes US inclus §3a |
| `c_spell`/`c_enchant` US = +95+ lignes visibles dans la boutique de sorts | faible | acceptable (affichage dormant) ou filtrage par id de classe |
| `pkgfs_unpack` : extraction jamais testée en exécution sur ce pkg précis | faible | vérifier `verify`/`extract` échantillon en phase 0.1 avant de tout extraire |
| Fichiers `t_*` EDENFR CP1252 copiés bruts → repli Big5 → accents cassés | moyen | jamais de copie brute : transcodage obligatoire (déjà fait pour lang/fr) |
| Bascule de la couche texte (option A) casse des chemins de lookup | faible | NetTests + matrice phase 7 ; option B reste disponible |
| Z=0 pet-spawn bug : les renards « réparés » restent invisibles | connu | reproduire en phase 7 — bug indépendant déjà logué |

---

## 6. Definition of done

- [x] `orphan-set` = vide (harnais) pour tout id atteignable lvl ≤90 —
      rejoué sur `corpus/official` assemblé
- [x] Zéro texte chinois visible dans les surfaces testées (S002 live :
      HUD, quêtes, noms NPC tout FR ; repli Big5 documenté seulement)
- [ ] Chaînes L80 éveil jouables FR+EN sur les 6 classes — tables +
      textes prêts, acceptation live non testée
- [ ] Archive groupée, boutique sorts remplie, daily rewards visibles —
      daily rewards vu ouvert en HUD ; archive/sorts non ouverts live
- [ ] `//give 43774` → monture renard fonctionnelle ; œuf renard éclos
      visible (ou bug z=0 confirmé et ticketé)
- [x] `dotnet build` 0 erreur/0 warning (client + serveur)
- [x] `EDEN_REFORGED_DB` repointé `corpus/official` ; `off` = natives
      complètes (fallback sain, pas l'état visé)
- [x] Pack `cfgfix.epk` reconstruit + run `EDEN_ALLOW_LOOSE=0` OK
- [ ] `docs/INDEX.md` + AGENTS mis à jour

---

## 7. Non vérifié (limites honnêtes)

- `MonsterKilled 3xxxx` non vérifié (0 hit <21000 déjà).
- Missions 2xxx-34xx « dormantes » : classification par gate `>=199`/
  titre event — non confirmée par activation serveur réelle (un event
  admin peut toujours les lancer).
- Acceptation live d'une quête L80 (donneur + niveau requis) non testée
  — tables serveur (35 missions) + client complétées vérifiées statique.
- Ouverture live item chest / lucky bag / lottery non testée (tables
  parsées, 0 orphelin ; l'interaction UI reste à vérifier).
- `fnaccount.itemmall` prix/contenu non audités (hors sujet demandé).

## 8. Runtime mesuré (31 oct.)

- `orphan-set` rejoué **sur le corpus assemblé** = 0 orphelin atteignable
  (restes listés §3 : enchants post-client, scènes CN dormantes…).
- `AssetCatalog.ResolveFile` : résolution casse **par segment** (bug
  réel : ~100 maps `sNNN` tombaient en repli S009) — commit 628d0d2.
- 15 `.fsm` FR = variante de sérialisation inconnue du parseur →
  remplacés par les fsm US officiels, rescan propre.
- Live S002 : chargée, spawn/reposition OK, `--service-test` 20 NPC FR,
  `EnterShop` réels (stores 46/1/2). Capture `godot-shots/s002-live.png`.
- Packagé : `cfgfix.epk` reconstruit (11 627 fichiers), run
  `EDEN_ALLOW_LOOSE=0` OK.
- Serveur : `EDEN_REFORGED_DB` → `corpus/official/data/db` via
  `start-native.sh` (« overlay client »), talents+enchants alignés sur
  les tables que Godot affiche. `off` = fallback natif complet.
- `IniTable` frontière de records (client `f9fa202` + serveur
  `f6cea5c`) : les records courts ne dévorent plus la ligne suivante —
  items 20 432, montres 12 896, archive **Montures 8 → 20** en live.
- Montures (live) : give `//clone_item`, invocation via sac
  (clic droit → « Utiliser »), tooltips FR (« Monture Héroïque
  Ormormu »), visuels r503/r501 rendus avec os de selle `L17`,
  vitesse ×3,5 serveur.
- **Montures de combat** : monture 39202 « Test : Dragon de Bataille »
  (enchant 59033, `2134` type=1 + `2140` 4 sorts) engagée en combat —
  survit l'engagement, barre 545 `[50038,50097,50035,50066]` ; le
  contre-cas (20051 puma, type 0) est auto-démonté à l'engagement.
  Test `Enchant_BattleMountParsesCombatFlagOnOfficialCorpus` (183b8d2).
- 1 061 nifs FR hors spec convertisseur → substitués nifs US officiels
  + reconversion (commit `ab8d69b`) ; overlay `fix-models.epk` (45 Mo)
  monté **après** `cfgfix.epk` (dernier gagne) — dragon visible en
  run packagé.
- Bascule FR/EN live : `language=fr|en` dans `settings.cfg` —
  archive « Montures (20) » ↔ « Mounts (20) » vérifiée dans les deux
  sens (hot-switch via `TextResolver.SetLanguage` + `LanguageChanged`).

---

## Annexe — preuves clés de cette passe

| Fait | Preuve |
|---|---|
| Postgres accessible | `docker exec eden-postgres psql` OK ; `itemmall`=515 (498 sell), 0 orphelin serveur, 6 hors FR ; `fortune_bag` 7 474 ids, 682 hors FR ; 5 `privilege>0` |
| Montures renard non vendues | `item_id ∉ itemmall` pour les 17 ids renard → give-only |
| Tables « binaires » lisibles | `S_ItemChest`=`|V.3|32|` texte Big5 ; `S_Lottery` CSV ASCII |
| Missions L80 = vrai trou | 25 ids 3464-3499, donneurs spawnés S001-S053, 100 % couverts par US (missions+NPC+items+scènes) |
| Reste dormant | missions `>=199`, « 測試用 », L15-events, titres Coupe du Monde — non atteignables à lvl 90 |
| pkg extractable | `pkgfs_unpack.py list pkg.idx` → 45 064 entrées |
| Extracted incomplète | `map/model` ~90/145 scènes, `UI/map` 80/299 — **ne pas sourcer depuis extracted/** |
| Serveur plus récent que tout client | items manquants FR sans nom même en US (`994-1068` gear L93-98, `56002` loot-tables) — post-Aeria |
| `1290` dans EDENFR | `c_item` ligne 3406, icône `I01488` — affichable en base EDENFR (non giveable : absent serveur) |
