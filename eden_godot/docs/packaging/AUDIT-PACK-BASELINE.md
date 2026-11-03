# Audit pack — baseline Étape 0 (corpus mesuré)

- Date : 2026-10-29 23:08 CET
- Racine auditée : `/home/ster2lune/Bureau/reverseden/portage/eden_godot`
- `raw_assets` → `/home/ster2lune/Bureau/reverseden/eden_server/reforged/EdenReforged`
- Commande : `python3 tools/pack_audit.py --root /home/ster2lune/Bureau/reverseden/portage/eden_godot > docs/packaging/AUDIT-PACK-BASELINE.md`
- Méthode : **stat() uniquement** — tailles et comptes ; aucun contenu asset lu (exception bornée : `.ini` > 500 ko pour lignes/encodage).
- Régénérable, déterministe (listings triés), stdlib only.

> Référence : `docs/packaging/PLAN-PACK-EPK.md` (cible **8-12 Go**, borne 5-15 ; exclusions : fat `.glb` couverts par `.slim.glb`, `*.kfm.json`, `Dark/*`, backups).

## 0. Verdicts

- **Couverture slim 1:1** : **GO** — 4243 paires `.glb`/`.slim.glb` sous `anims/` (8,77 Go de fat excluables), 0 fat orphelin(s) sous anims/, 0 slim sans frère fat. Les fat d'anims sont exclus du pack **sans perte** (le loader préfère `.slim.glb`, repli `.glb` inutile puisque couvert à 100 %).
- **Poids mesuré vs cible** : layout actuel brut = **27,31 Go** ; après exclusions mesurées = **18,30 Go** → encore au-dessus de la cible 8-12 Go : le solde dépend des étapes build-time non mesurées ici (compression GPU textures, zstd). Borne honnête : le pack « exclusions seules » fait **18,30 Go** (cf. §7).
- ⚠️ `map/sknodemap/` = 407 fichiers / 89,1 Mo lus au runtime (`MapLoader` → murs invisibles) mais **absents du layout `package_dist.sh`** (vérifié : le script ne le copie pas) — gap à corriger au packaging.
- ⚠️ Lightmaps `Dark/` en double : PNG convertis sous `assets/textures/map/model/Sxxx/Dark/` (26,0 Mo, 5944 fichiers) **et** DDS sources sous `raw map/model/Sxxx/Dark/` (52,0 Mo) — aucun lecteur `Dark/` dans `godot_project/scripts/` (greppé) ; le plan ne citait que les .png.
- ⚠️ `data/db/Ini-decrypt-only.exe` (33,0 ko) — binaire Windows embarqué dans les données, excluable.
- ⚠️ 5 backup(s)/fichier(s) système (73,5 ko — Thumbs.db sous `uidata/imagesets/`).
- ⚠️ 9/15 tables chaudes non-UTF8 (Big5 probable) — le bake-tables doit respecter l'encodage sniffé par `IniTable`.
- ⚠️ `map/model/` hors Sxxx (effect/object/sky/weather/vgncustom + `.nif`/`.dds` racine) : 15 entrées, 355,5 Mo — hors package (convertis dans `assets/` ou inutilisés), ne PAS embarquer par erreur.

## 1. Catégories (fichiers / octets)

| dossier | fichiers | octets |
|---|---|---|
| `assets/textures` | 58770 | 9,22 Go |
| 　└ `textures/UI` | 20969 | 465,1 Mo |
| 　└ `textures/char` | 5050 | 1 023,3 Mo |
| 　└ `textures/decorate` | 229 | 51,8 Mo |
| 　└ `textures/effect` | 3805 | 185,5 Mo |
| 　└ `textures/house` | 32 | 19,4 Mo |
| 　└ `textures/item` | 4580 | 638,0 Mo |
| 　└ `textures/map` | 13672 | 2,65 Go |
| 　└ `textures/monster` | 7349 | 3,26 Go |
| 　└ `textures/ride` | 2967 | 971,5 Mo |
| 　└ `textures/themes` | 25 | 3,0 Mo |
| 　└ `textures/uidata` | 92 | 23,4 Mo |
| **`assets/models`** | 11546 | 3,27 Go |
| 　└ `models/char/animation` | 1 | 548,4 ko |
| 　└ `models/char/model` | 2563 | 330,7 Mo |
| 　└ `models/char/texture` | 1 | 561,4 ko |
| 　└ `models/decorate/model` | 144 | 3,0 Mo |
| 　└ `models/effect/model` | 2244 | 279,7 Mo |
| 　└ `models/house/model` | 16 | 5,1 Mo |
| 　└ `models/house/texture` | 1 | 321,0 ko |
| 　└ `models/item/model` | 811 | 92,0 Mo |
| 　└ `models/map/model` | 3954 | 1,61 Go |
| 　└ `models/monster (racine)` | 1 | 95,5 ko |
| 　└ `models/monster/model` | 1449 | 805,7 Mo |
| 　└ `models/ride (racine)` | 1 | 1,4 Mo |
| 　└ `models/ride/animation` | 1 | 1,1 Mo |
| 　└ `models/ride/model` | 359 | 178,8 Mo |
| **`assets/anims`** | 10495 | 13,74 Go |
| 　└ `anims/char (racine)` | 8 | 59,7 Mo |
| 　└ `anims/char/animation` | 31 | 2,66 Go |
| 　└ `anims/char/model` | 3 | 6,5 Mo |
| 　└ `anims/decorate (racine)` | 144 | 41,6 ko |
| 　└ `anims/house (racine)` | 15 | 5,7 ko |
| 　└ `anims/house/animation` | 30 | 510,0 ko |
| 　└ `anims/map/model` | 6 | 301,7 ko |
| 　└ `anims/monster (racine)` | 1469 | 59,8 Mo |
| 　└ `anims/monster/animation` | 5838 | 9,05 Go |
| 　└ `anims/ride (racine)` | 365 | 45,3 Mo |
| 　└ `anims/ride/animation` | 2581 | 1,86 Go |
| 　└ `anims/ride/model` | 5 | 234,5 ko |
| **raw `uidata/`** | 610 | 81,0 Mo |
| **raw `data/`** | 823 | 64,9 Mo |
| 　└ `data/db` | 157 | 53,6 Mo |
| 　└ `data/scene` | 666 | 11,3 Mo |
| **raw `map/` (arbre complet)** | 18464 | 4,98 Go |
| 　└ `map/(racine)` | 2 | 24,1 Mo |
| 　└ `map/model` | 10331 | 2,37 Go |
| 　└ `map/sknodemap` | 407 | 89,1 Mo |
| 　└ `map/texture` | 7724 | 2,50 Go |
| 　└ dont `map/model/Sxxx` meta (.fsm/.ini/.area) | 390 | 891,4 Mo |
| 　└ dont `map/model/Sxxx/Dark/` (dds source) | 5944 | 52,0 Mo |
| **projet `gamedata/`** | 42 | 18,1 Mo |

**Total corpus mesuré** : 100750 fichiers, 31,36 Go (dont assets = 26,23 Go).

## 2. Couverture `.slim.glb` 1:1 — **critère bloquant du plan**

| ensemble | fichiers | octets |
|---|---|---|
| `*.glb` fat total | 15789 | 12,04 Go |
| `*.slim.glb` total | 4243 | 4,81 Go |
| paires fat↔slim | 4243 | 8,77 Go (fat) |
| fat SANS slim sous `anims/` | 0 | 0 o |
| fat sans slim hors `anims/` (attendu : `models/` n'a pas de pipeline slim) | 11546 | 3,27 Go |
| slim SANS fat (déjà autonome) | 0 | 0 o |

**Aucun orphelin** — chaque `.slim.glb` a son `.glb` frère et réciproquement sous `anims/`. Les `.glb` hors `anims/` (modèles) n'ont pas de variante slim et restent indispensables.

**Verdict : GO** — on peut exclure du pack les fat `.glb` couverts par un slim **sans perte** (8,77 Go économisables ; `EntityAnimator.ResolveAnimPath` préfère `.slim.glb`).

## 3. Exclusions mesurées (octets économisables)

| exclusion | fichiers | octets | où |
|---|---|---|---|
| fat `.glb` couverts par `.slim.glb` | 4243 | 8,77 Go | `assets/anims/` |
| `*.kfm.json` (sidecars de conversion) | 2009 | 165,1 Mo | `assets/anims/*` |
| `Dark/` convertis (.png) | 5944 | 26,0 Mo | `assets/textures/map/model/Sxxx/Dark/` |
| `Dark/` sources (.dds, copiés par package_dist.sh) | 5944 | 52,0 Mo | `map/model/Sxxx/Dark/` |
| backups `Thumbs.db`/`.DS_Store`/`*.bak`/`*.old`/`*~` | 5 | 73,5 ko | uidata/… |
| binaires embarqués `.exe`/`.dll` (bonus) | 1 | 33,0 ko | data/db |

**Total excluable mesuré : 9,01 Go** (vs −12,5 Go estimés au plan pour la seule ligne slim — le plan comptait large : les slim pèsent 4,81 Go contre 12,04 Go de fat totaux, mais seuls les fat d'`anims/` sont couverts).

## 4. Textures — histogramme extensions (`assets/textures/`)

| extension | fichiers | octets |
|---|---|---|
| `.png` | 58770 | 9,22 Go |

Corpus 100 % `.png` — aucun WebP/DDS converti : tout le gain « textures pré-compressées » du plan reste à faire au build.

## 5. Tables chaudes — `.ini` > 500 ko sous `data/` (24 au total, top 15)

| fichier | taille | lignes (≈wc) | encodage |
|---|---|---|---|
| data/db/t_dialogue.ini | 5,4 Mo | 82 609 | UTF-8 |
| data/db/c_item.ini | 5,0 Mo | 91 084 | non-UTF8 (1 058 584 car. indécodables — Big5 probable) |
| data/db/c_dialogue.ini | 4,6 Mo | 93 083 | non-UTF8 (2 290 176 car. indécodables — Big5 probable) |
| data/db/c_itemmall.ini | 3,4 Mo | 62 622 | non-UTF8 (940 332 car. indécodables — Big5 probable) |
| data/db/t_item.ini | 3,3 Mo | 75 147 | non-UTF8 (672 car. indécodables — Big5 probable) |
| data/db/motion.ini | 3,3 Mo | 82 080 | UTF-8 |
| data/db/c_enchant.ini | 3,1 Mo | 27 597 | non-UTF8 (597 967 car. indécodables — Big5 probable) |
| data/db/t_itemmall.ini | 2,5 Mo | 44 324 | UTF-8 |
| data/db/T_Enchant.ini | 2,1 Mo | 29 488 | UTF-8 |
| data/db/c_monster.ini | 2,1 Mo | 15 726 | non-UTF8 (84 074 car. indécodables — Big5 probable) |
| data/db/c_dropitem.ini | 2,0 Mo | 6 468 | non-UTF8 (369 780 car. indécodables — Big5 probable) |
| data/db/C_Spell.ini | 1,2 Mo | 21 907 | non-UTF8 (292 188 car. indécodables — Big5 probable) |
| data/db/T_Spell.ini | 1,1 Mo | 22 416 | UTF-8 |
| data/db/c_mission.ini | 1 018,4 ko | 3 024 | non-UTF8 (117 437 car. indécodables — Big5 probable) |
| data/db/T_Achievement.ini | 953,7 ko | 27 875 | UTF-8 |

## 6. Top 30 plus gros fichiers du corpus packageable

| fichier | cat. | octets |
|---|---|---|
| `assets/anims/char/animation/L.glb` | anims/.glb fat | 172,5 Mo → **exclu** |
| `assets/anims/char/animation/G.glb` | anims/.glb fat | 151,8 Mo → **exclu** |
| `assets/anims/char/animation/G2.glb` | anims/.glb fat | 151,8 Mo → **exclu** |
| `assets/anims/char/animation/L2.glb` | anims/.glb fat | 128,4 Mo → **exclu** |
| `assets/anims/char/animation/F.glb` | anims/.glb fat | 116,3 Mo → **exclu** |
| `assets/anims/char/animation/F2.glb` | anims/.glb fat | 116,3 Mo → **exclu** |
| `assets/anims/char/animation/K.glb` | anims/.glb fat | 116,2 Mo → **exclu** |
| `assets/anims/char/animation/K2.glb` | anims/.glb fat | 116,2 Mo → **exclu** |
| `assets/anims/char/animation/G.slim.glb` | anims/.slim.glb | 112,5 Mo |
| `assets/anims/char/animation/G2.slim.glb` | anims/.slim.glb | 112,5 Mo |
| `assets/anims/char/animation/L.slim.glb` | anims/.slim.glb | 107,5 Mo |
| `assets/anims/char/animation/L2.slim.glb` | anims/.slim.glb | 95,1 Mo |
| `assets/anims/char/animation/C.glb` | anims/.glb fat | 92,3 Mo → **exclu** |
| `assets/anims/char/animation/C2.glb` | anims/.glb fat | 92,3 Mo → **exclu** |
| `assets/anims/char/animation/F.slim.glb` | anims/.slim.glb | 85,1 Mo |
| `assets/anims/char/animation/F2.slim.glb` | anims/.slim.glb | 85,1 Mo |
| `assets/anims/char/animation/K.slim.glb` | anims/.slim.glb | 84,9 Mo |
| `assets/anims/char/animation/K2.slim.glb` | anims/.slim.glb | 84,9 Mo |
| `assets/anims/char/animation/B.glb` | anims/.glb fat | 84,8 Mo → **exclu** |
| `assets/anims/char/animation/B2.glb` | anims/.glb fat | 84,8 Mo → **exclu** |
| `assets/anims/char/animation/M.glb` | anims/.glb fat | 78,1 Mo → **exclu** |
| `assets/anims/char/animation/M2.glb` | anims/.glb fat | 78,1 Mo → **exclu** |
| `assets/anims/char/animation/C.slim.glb` | anims/.slim.glb | 67,0 Mo |
| `assets/anims/char/animation/C2.slim.glb` | anims/.slim.glb | 67,0 Mo |
| `assets/models/map/model/S218/S218.glb` | assets/models | 60,9 Mo |
| `assets/anims/char/animation/B.slim.glb` | anims/.slim.glb | 60,5 Mo |
| `assets/anims/char/animation/B2.slim.glb` | anims/.slim.glb | 60,5 Mo |
| `assets/anims/char/animation/M.slim.glb` | anims/.slim.glb | 58,1 Mo |
| `assets/anims/char/animation/M2.slim.glb` | anims/.slim.glb | 58,1 Mo |
| `assets/anims/ride/animation/JEFF_all.glb` | anims/.glb fat | 50,3 Mo → **exclu** |

## 7. Estimation pack cible (mesurée, hors compression)

| poste | fichiers | octets |
|---|---|---|
| `assets/textures` | 58770 | 9,22 Go |
| `anims/.glb fat` | 4243 | 8,77 Go |
| `anims/.slim.glb` | 4243 | 4,81 Go |
| `assets/models` | 11546 | 3,27 Go |
| `map-meta` | 390 | 891,4 Mo |
| `anims/.kfm.json` | 2009 | 165,1 Mo |
| `uidata` | 610 | 81,0 Mo |
| `data` | 823 | 64,9 Mo |
| `map-dark` | 5944 | 52,0 Mo |
| `gamedata` | 42 | 18,1 Mo |
| **TOTAL layout actuel (brut)** | 88620 | 27,31 Go |
| − fat `.glb` couverts par slim |  | −8,77 Go |
| − `*.kfm.json` |  | −165,1 Mo |
| − `Dark/` (png convertis + dds sources) |  | −78,0 Mo |
| − backups/système |  | −73,5 ko |
| − `.exe` embarqué |  | −33,0 ko |
| **= pack « exclusions appliquées »** |  | **18,30 Go** |

Borne honnête : **18,30 Go** en stat brut après exclusions — la cible 8-12 Go suppose en plus la compression GPU des textures (9,22 Go de PNG aujourd'hui, ~−6 Go visés au plan) et zstd (−1,5-2 Go estimés), **non mesurables ici**. `map/sknodemap/` (89,1 Mo) n'est PAS dans ce total (gap du layout actuel — à ajouter, pas à exclure).

## 8. Baseline perf existante (référence « avant »)

Lignes extraites verbatim de `docs/PERF.md` (mesures S001, machine dev RTX — à rejouer après packaging) :

```
PERF.md:172: | char→« EN JEU »                   | ~30 s      | **~14,5 s**|
PERF.md:173: | dont loadmap S001                 | 13,6 s     | 5,8-6 s    |
PERF.md:174: | dont objects+env+entities         | 12,9 s seq | ~5 s (parallèle) |
PERF.md:175: | dont entities (corps 263 NPC)     | ~7,5 s     | streamés post-entrée (~3/frame, proches d'abord) |
PERF.md:176: | dont glb preload (184 protos)     | —          | ~6 s (dominé par le méga-modèle perso ~5,5 s) |
PERF.md:177: | VRAM textures                     | 4 421 MB   | **1 173 MB** (−73 %) |
PERF.md:179: | fps en jeu (post-stream)          | 11-12      | **55-66**  |
PERF.md:219: | **moy 15s** | **89,8** | **11,1 ms** | — | **1 848** | **12 498** (vs 36 513 avant) |
PERF.md:240: | VRAM textures | 620 MB | **4 513 MB** |
PERF.md:319: | `glb preload` (184 protos) | 7 476 ms |
PERF.md:322: | `objects+env+entities` | 13 581 ms |
PERF.md:323: | `loadmap S001` | 14 538 ms |
PERF.md:324: | `player+visual` en jeu | 60 ms |
```

