# Rapport — corpus officiel assemblé (nuit du 31/10)

> État : **corpus `corpus/official/` construit, audité, testé en jeu**
> (client live sur S009, FR affiché, HUD natif complet).

## Ce qui a été FAIT cette nuit

| Étape | Résultat |
|---|---|
| Extraction `EDENFR/pkg` | **45 075/45 075 entrées** — dont 6 233 erreurs Deflate64 comblées par `extracted/` + 2 `.dds` copiées depuis US |
| Harnais `tools/audit/ini_audit.py` | parse tous formats (pipe `|V.x|`, `[INFO]`, CSV, `Talk`/`Service`) — orphan-set exhaustif JSON |
| Corpus `corpus/official/` (9,2 Go) | EDENFR base + **30 tables fusionnées** + 5 schémas US + 2 196 fichiers modèles/index + 5 scènes US (S057/S188/S192/s744/s745) |
| `c_collection.ini` | **fusion base US + 42 cartes FR-only** (8 558 au total, paddées à 10 cols, groupe=0 — le schéma FR 9-col n'a pas de col8) — avant : table US entière → les cartes d'archive FR (séries LV 23223+…) n'apparaissaient pas |
| Fusion collisions de casse | 31 dossiers `s003/`+`S003/` unifiés (l'union pkg/extracted les avait scindés) |
| Encodage `t_*` | transcode CP1252→UTF-8 avec détection Big5 par ligne (scorer : un vrai CN reste CN, `é`+lettre ne devient pas du CJK) — **0 U+FFFD** |
| Remplissage EN→base FR | **19 005 lignes** : tout id complété a un texte lisible (EN) au lieu de Big5/`#id` |
| `TextResolver` | **fusion par id** (base + overlay langue) — commit `d612fac` |
| Suppléments FR maison | **176 lignes** : 21 quêtes éveil L80/L90, 5 PNJ, 14 items (noms CN→FR), 136 partenaires 45xxx — commit `d66e5b3` |
| `raw_assets` repointé | → `corpus/official` (le lien est modifiable à volonté) |
| **Test live** | login→canal→perso OK, **0 erreur**, map S009 chargée, quêtes FR visibles, Flameshot `godot-shots/corpus-official-s009.png` |
| Conversion nif→glb | en cours (7 995 sources, reprise auto) |

## Orphan-set final (serveur → corpus official)

**0 orphelin atteignable** sur : items, missions, monstres (réels + cibles de quêtes),
PNJ, dialogues, partenaires, sorts, drops, coffres, combos, life-skill,
boutiques (stock + définitions), collections, daily awards, family combine,
lottery, lucky bag, rank award, reward score, nodes, titres, succès,
transports, scènes atteignables.

### Résiduel non comblable (post-client, partout absent sauf serveur)

| Canal | Ids | Nature |
|---|---|---|
| enchants | 1 459 | noms Big5 seuls — l'enchant **fonctionne** (stats serveur), affichage `#id`/CN |
| items | 7 (44492–44498) | contenu post-client |
| `Service` NPC | 0 | `400454` était un **dialogueId** (1er champ `Service <dlg> 24 <store>`) — les 289 storeIds existent tous ; le dialogue manquant n'appartient qu'au PNJ 53626, non spawné → inatteignable |
| scènes serveur | 268 | donjons/zones CN jamais portés — dormants à lvl 90 |
| boundinfo | ~20 codes m4xx | bornes absentes partout → repli cosmétique |

### Points notables découverts

- `1290` Kitsune de l'Aube — la monture « renard doré du soleil » EDENFR —
  **fonctionnelle** (31 oct., preuve live : `//clone_item 1290` → clic
  droit → ride key `253020`, modèle r253 renard 9 queues rendu, vitesse
  +90 %). Mise en place : `c_enchant.ini` += ligne 11354 (remplace
  l'entrée `S_Enchant` native — mêmes stats +90 %/+7 % PV/+2-1 % ATQ-DÉF,
  rideKey `253020` confirmé par le `c_enchant` FR, type combat conservé),
  `S_ItemExtra.ini`, `c_collection.ini` += carte 1290, nom EN. Rejoué par
  l'étape EXTRAS de `build_official.py`. Le reste de la chaîne
  (1287-1289, 1291) reste dormant faute de données.
- `48813` **Renard Blanc du blizzard aux neuf queues (Légendaire)** —
  la monture exclu Aeria `純白狐王(美法特製金品)` — **fonctionnelle**
  (31 oct., preuve live : `//clone_item 48813` → utiliser → ride key
  `268010`, modèle r268 renard blanc monté, +90 %/+7 % PV/+2 % ATQ-MAG,
  descente propre, captures `godot-shots/white-fox*.png`). L'id 48813
  n'était qu'un placeholder `測試用編號` dans `S_ItemMall` US →
  `S_ItemExtra.ini` est passé en fusion **prioritaire**
  (`GameDataService.MergeItemExtra` — remplace les collisions).
  `c_itemmall`/`t_itemmall` FR+EN existaient déjà ; ajoutés : enchant
  60419 (`c_enchant.ini`), carte archive (`Montures` → 38), ligne serveur.
  La famille « roi renard » complète existe dans EDENFR (43774/5 feu,
  43801/2 givre → r254 via 55868 déjà natif, 44113/4 ombre, 48799 givre
  bleu, 48815 or sombre, 48817 violet, 44413/4 soleil).
- `t_mission`/`t_npc` FR couvraient déjà les ids US-complétés (la localisation
  Aeria dépassait le `c_*` livré) — seuls **21 missions** manquaient vraiment.
- `S_Npc` lie dialogues via `Talk <ids>` et boutiques via `Service <id>`
  (pas `4001###` direct).
- Aucun audio/vidéo chargé par Godot → `sound/`/`movies/` non extraits = sans impact.
- Renards familiers : defs présentes partout → le bug est **runtime** (probablement
  spawn z=0 déjà logué), pas data.

## Validation runtime (mesurée, 31 oct.)

| Vérification | Résultat |
|---|---|
| Conversion nif→glb | **7 786 / 7 995** — 209 échecs qualifiés : `.kf` anims dont le `.glb` existe déjà + `map/map.nif` legacy inatteignable → 0 perte atteignable |
| `[glb-miss]` M023/M024 | **bruit de timing** — le log précède la construction différée de l'animator ; les GLB existent et se montent |
| Collisions de casse | `ResolveFile` ne traitait que le dernier segment → ~100 maps `sNNN` en minuscules repliaient sur S009. **Fix segment-par-segment** (commit 628d0d2) |
| FR `.fsm` variante | 15 fichiers FR utilisent une sérialisation inconnue du parseur → remplacés par les fsm US officiels (0 échec au rescan) |
| Live S002 | `map=S002 … live=True` + reposition ; `--service-test` = **20 NPC visités, noms FR** ; `EnterShop` réels : store 46→3 art., store 1→10, store 2→15 |
| Live S009 packagé | `EDEN_ALLOW_LOOSE=0` + 4 packs → login, map, HUD OK |
| Serveur overlay | `EDEN_REFORGED_DB=corpus/official/data/db` (start-native.sh) — talents/enchants validés contre la même table que le client ; `off` = natives S_Talent(915)/S_Enchant(26k) complètes, fallback sain |
| `//clone_item` | 571/572/21252/39223/20051/37019/**1290** OK — 1290 via `S_ItemExtra.ini` (chargé additif par `GameDataService`, oracle `S_Item` intact) |
| Renards | defs `S_Partner` présentes (20/20), modèles GLB OK, FR traduit — **aucune voie d'obtention** (pas d'item d'invocation `S_Item`, pas de mission, pas d'itemmall, pas de cmd GM) = écart natif, décision contenu requise si on les veut jouables |
| `IniTable` jonction records | **~36 000 entrées avalées** (client : moitié de `c_item`, `c_npc`, `c_monster`, `c_dialogue`, `c_itemmall` ; serveur : 13 `S_*`) — les lignes courtes absorbaient le record suivant. Fix `IsRecordBoundary` (client commit f9fa202, serveur f6cea5c) : une ligne `<id><sep>` ouvre un nouveau record |
| Archive « Montures » | **8 → 20 cartes** après fix IniTable (vérifié en jeu, Flameshot) — les 20 montures `ridelist` du corpus listées |
| nifs FR variante | **1061 nifs FR** ont leurs blocs géom hors spec nif2gltf (char 348, map 240, monster 157, effect 132, item 95, ride 67…) → GLB vides = entités invisibles. Substitués par les **nifs US officiels** (extraits pkg Reforged, parse 0 échec) ; 10 sans équivalent US (montures r222-r226, r258, r260, r266, r267 + ub019_3 = FR-only, restent invisibles) |
| Monture en jeu | `21252` Ormormu donné → clic droit « Utiliser » → `[mount] key=503010`, selle L17, vitesse ×3,5 serveur, **dragon rendu en vol** (Flameshot `mounted-final.png`) |

## Ce qui reste à faire

1. Validation live élargie : accepter une quête L80 en jeu (donneur + niveau), transitions de map.
2. Décision produit sur les renards (ajouter une voie d'obtention = ajout de contenu, pas alignement corpus).
3. ~20 lignes `t_*` résiduelles avec octets bizarres préservés latin-1 (cosmétique, listées).
4. 10 modèles sans équivalent US invisibles (montures FR-only — cf. ligne « nifs FR variante »).

## Rejouer l'audit

```bash
python3 tools/audit/ini_audit.py                 # vs corpus/official
python3 tools/audit/ini_audit.py <autre_corpus>  # vs un corpus donné
python3 tools/audit/build_official.py            # régénère corpus/official
python3 tools/audit/gen_fr_supplements.py        # ré-applique les FR maison
```
