# Base de connaissance Eden Eternal

Dumps de wikis + données tabulaires convertis en markdown — **à
consulter avant d'implémenter quoi que ce soit de gameplay** (classes,
sorts, objets, donjons, crafts, quêtes, mécaniques). C'est la
connaissance « métier » du jeu : le code explique le comment, ici c'est
le quoi.

> **PÉRIMÈTRE STRICT** : ce serveur réplique l'Eden de base —
> contenu jusqu'au **niveau 90 max**. Tout contenu niveau 95+
> (sets éveillés 95+, Trials 95-130, battle maps 95+, classes
> éveillées Vendetta…) est **HORS PÉRIMÈTRE** — conservé mais regroupé
> dans la **section finale** de ce document et signalé dans chaque
> index. Ne jamais implémenter de donnée lvl>90, même si elle figure
> ici.
>
> ⚠️ **Attention au flag `awaken-lvl95+`** du TSV wiki-en : il ne
> signifie *pas* « niveau 95 ». Les 60 pages ainsi marquées sont les
> 12 sets d'armure éveillés **lv70/80** (`Awaken <Nom>`, quêtes Sprite
> Scroll) — du contenu **dans le périmètre** (jeu de base). Le vrai
> contenu >90 vit dans `wiki-tables/` (Vendetta) et `guides-fr/`.

## Où chercher quoi (lire avant de fouiller)

| Tu cherches… | Va à… | Tu obtiens… |
|---|---|---|
| **Un sujet sans connaître le fichier** (classe, donjon, mécanique) | `tools/graph/.venv/bin/graphify query "<sujet>" --graph knowledge/graphify-out/graph.json` | Les pages/sections liées au sujet sur tout `knowledge/` (graphe local, 0 LLM). **Le plus rapide pour se repérer** — ensuite lire les fichiers désignés. |
| **Un item/arme/armure/consommable EN par nom, niveau ou rareté** | `grep -i "<nom>" knowledge/wiki-en/pages-index.tsv` | Ligne TSV : fichier HTML, catégorie, sous-type, niveau, localisation, rareté, résumé, flags. Le texte complet est dans `raw/wiki-en/<fichier>` (HTML, voir pièges). |
| **Un résumé EN par catégorie** (classes, donjons, familiers…) | `knowledge/wiki-en/categories/<cat>.md` (20 fichiers) | Digest markdown : table + liens vers les pages. **Pas** les pages elles-mêmes. |
| **Le nom FR officiel** d'une zone, classe, sort, PNJ, item | `knowledge/wiki-fr/pages/` (243 pages) + `knowledge/wiki-fr/INDEX.md` | Texte FR complet ère Aeria. Indispensable pour `ui_text_en_fr.tsv` et les tables `t_*` FR. |
| **Un guide joueur FR** (donjon, éveil, système) | `knowledge/guides-fr/pages/` (130 pages) + `guides-fr/INDEX.md` | Article détaillé (serveur Vendetta : vérifier le flag périmètre de la page). |
| **Des chiffres en table** (loots, crafts, skills 72/90, familiers, titres) | `knowledge/wiki-tables/{M,C,D,T,R,G,BM,misc}/` + `INDEX.md` + `manifest.json` | Tableau md (source `src_csv/`, classeur `Eden_Eternal_Vendetta_Wiki.xlsx`). **Valeurs Vendetta ≠ officielles** : recouper wiki-en. |
| **Une capture d'écran du vrai client** (UI, panneau, skin, tooltip) | `ref-shots/` (127 PNG, hors knowledge/) + spec **`eden_godot/docs/client-assets/ref-shots-catalog.md`** | Chaque PNG a une entrée « ce qu'on voit / ce que ça spécifie ». Ex. `ClassAbilitybonuslogo.png`, `ClassePanel*.png`, `skin exemple classe chevalier.png`. Chercher le nom dans le catalogue. |
| **Les données brutes du jeu** (tables INI natives) | `corpus/fr-full/data/db/` (lecture seule) | `c_*`/`C_*` = client, `S_*` = serveur, `t_*` = textes. **Encodage Big5 (sauf `t_*` = CP1252)** : un simple `cat` affiche du mojibake, parser via `IniTable`/`EdenEncoding`. |
| **Comment le jeu d'origine calculait X** (formule, paquet, table) | `re/reindex search "<terme>"` / `lookup` / `strings` / `xref` (règles : `re/AGENTS.md`) | Fonction décompilée + adresse (ex. `lapis::CCharacter::RefreshJPAward` zone @0x8417c10 = le calcul des paliers du Class Ability Bonus). Graphe d'appels : `graphify query --graph tools/graph/reagraph.json`. |
| **Une synthèse déjà récoltée** (ne pas re-extraire le wiki) | `docs/reference/harvest/` (6 docs, importés du portage le 2026-11-03) | `classes-catalog.md`, `class-expertise.md`, `expertise-stats.md`, `racial-skills.md`, `refshots-classes.md`, `awakened-refs.md` — mécaniques croisées wiki+natif+INI avec statuts CONFIRMED/MISSING et sources citées. |
| **Un catalogue de fichiers du corpus** (modèles NIF, items, maps) | `docs/reference/` (`MODELS-CATALOG`, `ITEMS`, `MAPS`, `INVENTAIRE-QUETES-MONTURES` + `.tsv`) | 7 991 NIF/KF classés, items, maps, quêtes/montures — généré sur `corpus/fr-full`. |
| **L'inventaire complet de `corpus/fr-full`** (47 639 fichiers, par type) | `docs/reference/corpus/` + `artifacts/corpus-inventory/files.tsv` | Un doc par type (tables, maps, modèles, anims, textures, UI, audio, divers) + `INDEX.md` global ; TSV = 1 ligne/fichier avec métadonnées (dims DDS, version NIF, durée WAV) et rôle. Regénéré par `tools/corpus-inventory/`. |
| **Voir une texture/asset du corpus** | `artifacts/corpus-png/<même chemin>.png` (miroir) + `artifacts/corpus-sheets/` (planches 5×5) + `sheets-index.tsv` | Toutes les images lisibles via `read` ; descriptions déjà rédigées dans `visual-notes-*.md` (tenues de classes tous corps, atlas CEGUI, skins monstres 1 362, icônes). Voir `docs/reference/corpus/VISUEL.md`. |
| **Tenues de classes / équipements visuels** | `artifacts/corpus-inventory/class-outfits.tsv` + `equip-visuals.tsv` | classe×niveau×code→mesh+texture×7 corps (2 960) ; K-codes items→apparence (11k). `docs/knowledge/class-skins-audit.md` = bugs connus de la chaîne. |
| **Class Ability Bonus attendu** | `artifacts/corpus-inventory/class-rankbuff-expected.tsv` + `docs/knowledge/native-constants.md` | 48 classes×22 paliers cumulés ; formule native `RefreshJPAward` vérifiée ; divergences affichage↔enchant connues. |
| **Les skills attendus d'une classe** | `artifacts/corpus-inventory/class-skills-expected.tsv` | 2 701 lignes Reg/Class/Expertise/Certificat × 61 classes avec icônes + noms FR. |
| **Une entité et ses assets** (item→enchant→visuel→son, PNJ→modèle, quête→dialogue) | `artifacts/corpus-inventory/{item-enchants,npc-visuals,mission-index,dialogue-index,map-spawns,model-index,audio-joins}.tsv` | Index maîtres : `model-index` 23 310 codes→nif/kfm/textures ; `mission-index` 2 392 quêtes ; `dialogue-index` 20 362 ; `audio-joins` 61 717. Entrée : `artifacts/corpus-inventory/README.md`. |
| **Une constante de `S_Parameter.ini`** | `artifacts/corpus-inventory/parameter-index.tsv` | Les 678 slots indexés, consommateurs natif+C# par ligne. |
| **Ce qui manque en FR** | `artifacts/corpus-inventory/fr-coverage.tsv` | 67 tables, 8 254 ids sans nom — complète G22-G27 de `EDENFR-GAPS`. |
| **Le protocole** (opcode→handler serveur) | `docs/knowledge/protocol-map.md` | 264 C→S + 364 S→C zone, wire↔natif documenté. |
| **Quel layout CEGUI est implémenté** | `docs/reference/corpus/layout-impl-map.md` | 415 layouts : 55 % couverts, carte du travail UI restant. |
| **Le plan de fix du bug classes** | `docs/knowledge/class-bug-fixplan.md` | 12 fixes ordonnés (F1-F12) + audits : `class-skins-audit`, `class-skills-audit`, `rankbuff-server-audit`, `class-skills-server-audit`, `equip-visuals-audit`. |
| **L'état technique du projet** (versions moteur, perf, pipelines, leçons) | `docs/knowledge/*.md` (≠ `knowledge/` !) | Notes vérifiées avec statut/date — projet, pas gameplay. Ne pas confondre les deux dossiers « knowledge ». |
| **Ce que fr-full ne fournit pas** | `docs/workflows/EDENFR-GAPS.md` | Lacunes connues (G1–G27) : tables absentes, textures jamais livrées — à lire avant d'« inventer » une donnée. |

## Contenu du dossier

| Entrée | Qu'est-ce | Taille / échelle |
|---|---|---|
| `wiki-en/` | Wiki fandom **anglais** (ère X-Legend/Aeria) | `pages-index.tsv` (12 841 lignes) + `categories/` (20 digests) + `INDEX.md`. **Pas de pages md** — le texte complet est le HTML de `raw/wiki-en/` |
| `wiki-fr/` | Wiki fandom **français** (ère Aeria, ~2012-2017) | 243 pages md + `INDEX.md` (rapport de couverture FR↔EN) |
| `guides-fr/` | Site WordPress **edenfr** (2015-2018, communauté Vendetta) | 130 articles md + `images/` (17 fichiers locaux) + `INDEX.md` (flags périmètre par page) |
| `wiki-tables/` | Classeur Google Sheets communauté **Vendetta** | 65 md + `src_csv/` + `Eden_Eternal_Vendetta_Wiki.xlsx` (source) + `manifest.json` + `csv_to_md.py` + `INDEX.md` |
| `raw/` | HTML bruts des 3 sites (`.gitignore`, régénérable) | ~121 Mo : `wiki-en/` 97 Mo, `guides-fr/` 21 Mo, `wiki-fr/` 4,1 Mo |
| `tools/` | Scripts de conversion rejouables | `extract_fr.py`, `build_index.py`, `index_data.json` |
| `graphify-out/` | **Graphe graphify de tout knowledge/** (2 139 nœuds) | Requêtes : voir table routage. `raw/` exclu par `.graphifyignore` → le graphe couvre le md, pas le HTML |
| `Eden Eternal_ An Informational Guide (By KoroFish).pdf` | Guide joueur exhaustif **anglais** (17 Mo) | Non indexé (binaire) : ni graphe ni grep n'y voient — l'ouvrir ou `pdftotext` si besoin d'une mécanique générale |

## Ce qu'on y trouve, par sujet

### Classes, skills, races

- **Classes** : 25-26 classes en 5 branches (Défense, Mêlée, Distance,
  Magie, Soin) avec niveaux de déblocage, armures/armes associées —
  `wiki-en/categories/classes.md` (tableau + pages par classe),
  `wiki-fr/pages/Classes.md` + `Tableau_des_classes.md` (FR officiel),
  `guides-fr/pages/classes/` + `classes.md` (mixte base/Vendetta).
  Synthèse complète ids/prérequis/variantes : `docs/reference/harvest/
  classes-catalog.md`.
- **Tenues de classe (skins)** : le perso n'affiche que la tenue de
  classe, pas l'armure portée — `wiki-fr/pages/Apparence_de_classe.md`
  (structure par classe × race × genre) ; codes tenue `A0xxxx` par
  palier de niveau dans `corpus/…/c_class.ini` → meshes via
  `4pequip.ini` ; visuel dans `ref-shots/skin exemple classe
  chevalier.png` + `ClassePanel-ClassNews*.png`.
- **« Class Ability Bonus » / Expertise** (bonus de stats par palier de
  niveau de classe) : mécanique + table + piège de vocabulaire (≠
  certificats ≠ talents KP) → `docs/reference/harvest/class-
  expertise.md` et `expertise-stats.md` ; données natives
  `corpus/…/c_classrankbuff.ini` + `c_rankaward.ini` + `t_rankaward.ini`
  ; capture `ref-shots/ClassAbilitybonuslogo.png` ; chiffres Vendetta
  `wiki-tables/M/Class_Expertise.md` ; natif `reindex search
  "RefreshJPAward"`.
- **Compétences de classe** : ~22 pages `X_Skills` EN (par classe) ;
  skills lv72 à 9 000 Merits (`wiki-tables/M/Lv72_Class_Skills` +
  `BM/VL_DD_BTS`) ; Holy skills lv90 chiffrées
  (`wiki-tables/M/Holy_Skills`) ; Guardian skills
  (`M/Guardian_Skills`) ; expertise de classe
  (`wiki-tables/M/Class_Expertise`, `guides-fr/…/expertise-de-classe`).
- **Certificats & combos** : 88 certificats EN +
  `wiki-tables/M/Class_Certificates` (combos génériques par couleur) +
  `wiki-fr/pages/Les_Certificats.md` +
  `guides-fr/…/certificats-de-classe` + `…/combinaisons-…`.
- **Races** : 5 jouables (Humain→fusion bijoux, Zumi→ingénierie,
  Anuran→alchimie, Ursun→glyphes, Halfkin/Ézelin→tissage) + races PNJ
  (Drakai, Torturran, Tuskar…) ; passifs & skills raciaux lv45/60
  (`wiki-tables/M/Racial_Passive`, `M/Racial_Skills`,
  `wiki-fr/pages/Comparatif_des_compétences_raciales.md`,
  `Sorts_de_race_(par_classe).md`, `guides-fr/pages/caracteristiques-
  raciales/`). Modèle réel (passif par classe + sorts par branche) :
  `docs/reference/harvest/racial-skills.md`.
- **Glyphes** : 120 glyphes EN (par classe/slot) + craft Ursun
  (`wiki-tables/R/Bear`, `wiki-fr/pages/Décrypteur_de_Glyphes_
  (Ursuns).md`).

### Équipement (armes, armures, accessoires, trophées)

- **wiki-en** = référence exhaustive : 2 092 armes (18 types),
  2 464 armures (~209 sets de 5 pièces, lv1-90), 1 083 accessoires
  (~570 trophées de boss), 112 costumes, 6 sacs — raretés
  white/green/blue/orange/gold, slots, stats, sources (drop, craft,
  fame, quête). Familles notables : sets craftés 23-88, `Accursed`
  lv80/85, `Holy -` lv90 (armes/trophées), `Temple Knights` lv50-80
  (éveil), sets éveillés lv70/80 (Sprite Scrolls), séries `<Boss>'s X`
  en bleu.
- **wiki-tables** = complément chiffré : `T/Trophies` (tous trophées
  1-123 avec combos), `C/` (blueprints craft 20-90, armes éveillées
  65/75, sets éveillés 70/80, enchants 1-8), `M/Armour_Boxes`,
  `M/Dyes` (~148 teintures).
- **FR** : `wiki-fr/pages/` (armes/armures listées par type, armes
  éveillées lv65), `guides-fr/pages/equipements/` (éveil 65/75/80,
  boîtes d'armure 68/73, gemmes, enchants, costumes, montures).
- **Fortification/amélioration** : Power/Guard Scrolls lv20-100
  (EN), enchants de trophées Lv1-13 (`plans-artisanat`), enchants
  partiels (`wiki-tables/C/Partial_Enchants_Lv1-8`), `Amélioration_
  d'équipement` (FR), `Succes_rune_vers_les_etoiles` (enchants 5-7).

### Artisanat, récolte, gemmes

- **Crafts raciaux** : ingénierie Zumi (paliers 30-110, gadgets +
  montures + Monster Stones), glyphes Ursun, étoffes de trophées
  Halfkin Lv1-16, alchimie Anuran (potions, teintures, charmes),
  fusion de gemmes Humain (paliers 1-10) — `wiki-tables/R/*` (table
  des matériaux), `wiki-fr/pages/` (texte + noms FR), EN
  `Alchemy/Engineering/Engraving/Embroidery/Jewel_Fusion` +
  `M/Monster_Stones`.
- **Plans de craft** : 2 828 pages EN (`plans-artisanat`) —
  Foundry (blueprints armes/armures oranges→gold), Magic Pool
  (anneaux/colliers/enchants), Alchemy Lab (gemmes/potions),
  work levels 1-12, ~20 vendeurs de blueprints, 14 town blueprints.
- **Récolte** : filons/fermes/autels par work level —
  `wiki-tables/M/Gathering_Tools` (tables de loot), EN
  `Mining/Farm/Summon Material` (141/139/132 items).
- **Gemmes d'équipement** : EN `Gem` (287), recettes Sky Tower /
  Dreadlore / Daylight Forest / 4ToD (`wiki-tables/D/`), guides
  `guides-fr/pages/gemmes-*`. **Gemmes raciales** (classe) : `R/Human`.

### Monde : zones, donjons, boss, quêtes

- **Zones** : ~26 régions du continent central + continents SO/SE —
  `wiki-fr/pages/Géographie.md` (noms FR + paliers), EN
  `categories/zones.md` (15 pages détaillées : portails, PNJ, quêtes,
  mobs), `guides-fr/…/geographie-du-monde-dezur`.
- **Donjons classiques** : liste complète par zone (normal 3-5j +
  héroïque 5-8j) — `wiki-fr/pages/Donjons.md` + `Boss_de_Donjons.md`
  (~80 boss avec faiblesses), EN `categories/donjons.md` (30 pages),
  `guides-fr/pages/donjons-*` (runs détaillés par tranche 1-90).
- **Trials** : tables de loot complètes dans `wiki-tables/D/` (50-85
  dans périmètre : trophées, drops par boss, blueprints de sets,
  enchants, médailles d'honneur).
- **Boss** : 147 EN (world boss, boss de guilde, boss de donjon) +
  `wiki-fr/pages/Monstres_Élites.md`, `Monstres_de_Guilde.md`
  (coordonnées + succès), `guides-fr/…/boss-de-guilde-world-boss`.
- **Quêtes** : mécanique générale EN (`Quest`) + séries détaillées FR
  (tutoriel Monts Calcaires chaîné, ~20 quêtes Hautes-terres, quêtes
  de guilde ~50, livres de gloire) + quêtes d'éveil (armes 65, sets
  70/80 — `wiki-fr/pages/Quêtes.md`, EN `Awakening_Quest`).

### Guildes, PvP, social, économie

- **Guilde** : création (20 po, niv.20), paliers 1-12 chiffrés
  (`wiki-tables/misc/Useful_Info`), ville de guilde (bâtiments,
  récolte raciale, reliques, blueprints de ville — EN
  `Guild`/`Guild_town`/`decorations-guilde` 105 pages), quêtes de
  guilde (FR ~50 détaillées), boss de guilde.
- **PvP** : arène 1v1/3v3/10v10/boss (`wiki-fr/pages/Arène.md` +
  horaires), Territory War / GvG / élimination
  (`guides-fr/pages/guerres-de-territoire`, `elimination-de-guilde`,
  horaires Vendetta dans `wiki-tables/misc/Intro`).
- **Social** : couple (`Couple` FR/EN), familiers (46 EN : obtention,
  combat, gear, pray skills — + `wiki-tables/M/Pet_Skills` ~130,
  `M/Pet_Prays` ~110), montures (liste complète `wiki-fr/pages/
  Montures.md` + `guides-fr/…/montures`), villa (`Housing` EN,
  `systeme-de-villa` + `Furniture_Guide` tables), Centre d'amitié /
  cristaux d'Asbi.
- **Économie/fame** : 9 factions d'Aven (tables fame/prix —
  `wiki-tables/misc/Useful_Info` + EN `Amber_Eye`…`Silver_Needle` +
  FR `gloires-daven`), monnaies (or/argent/cuivre, Eden Crystal,
  Crystal Cross Medals, Honor Stars/Medals, Eternal Coins, Alpaca
  Coins, AP — `guides-fr/…/monnaies-et-devises`), HdV, boutique,
  Arcane Box (`M/Arcane_Box` recettes + mécanique EN `Arcane_Box`).

### Mécaniques transverses

Break Point (types de faiblesse, loot), Malice/aggro, mort (-5 % XP),
Warps (table de coûts), banque/archive, inventaire, cartes au trésor,
Boîte Mystique (5 fonctions), métamorphoses (gemmes/cartes), météores/
étoiles/sphères, titres (`M/Titles` ~300 + `Liste_des_Titres` FR),
succès/hauts faits (`M/Legendary_Achievements`, `Monstres_Élites` FR),
médailles d'honneur & CCM (`M/Honor_Medals_and_CCM`), leveling routes
(`misc/Lvling_and_Farming_Guides`), patch notes X-Legend 2023.

### Traduction FR↔EN

Les noms officiels FR (zones, donjons, boss, quêtes, PNJ, items,
sorts) sont dans `wiki-fr/` — indispensable pour `ui_text_en_fr.tsv`
et les tables `t_*` FR. Les correspondances FR↔EN principales sont
documentées dans `wiki-fr/INDEX.md` (rapport de couverture).

## Limites et pièges connus

- **wiki-en n'a pas de pages markdown.** Seulement `pages-index.tsv`
  (métadonnées + résumés) et `categories/` (digests). Le texte complet
  est le HTML de `raw/wiki-en/*.html` (~97 Mo, non versionné — peut
  manquer sur un clone, et **exclu du graphe** : `graphify query` ne le
  voit pas). ~22 pages de quêtes vides, ~14 fiches monstre-stub, zones
  documentées surtout ≤60, pas de table d'expertise de classe, pages
  d'armes « de monstre » non jouables, `raw/` non versionné (sell
  price/stack parfois absents du TSV).
- **wiki-fr** : arrêté ~2017 — couvre ≤70-80, aucun contenu 76-90,
  tables trophées 30-60 quasi vides, doublons de pages, sorts raciaux
  Ézelin absents, noms FR parfois instables (Champignoïde…). Les images
  des pages sont des **URLs distantes** `static.wikia.nocookie.net`
  (non téléchargées — un agent ne peut pas les « lire » hors ligne).
- **guides-fr** : site Vendetta — mélange base + custom dans les
  mêmes pages (classes « 61 » inclut les éveillées VGN ; équipements
  incluent 95) ; doublons d'articles ; seules **17 images locales**
  (`guides-fr/images/`), le reste en URLs distantes ; le périmètre est
  indiqué par page dans `guides-fr/INDEX.md`.
- **wiki-tables** : valeurs **Vendetta** (rééquilibrages, classes
  custom, monnaies custom) ≠ valeurs officielles ; md sans en-têtes
  propres ; coquilles d'origine conservées ; `M/Aven_Fame_Vendors`
  = duplicata d'Intro (vraie table dans `misc/Useful_Info`) ;
  incohérences internes listées dans `wiki-tables/INDEX.md`.
- **Tables natives ≠ wiki** : pour une valeur de référence (stats,
  bonus, drops), la source de vérité est `corpus/fr-full/data/db/*.ini`
  (Big5/CP1252, lecture seule) croisée avec le décompilé (`re/reindex`),
  pas le wiki.
- **Le graphe knowledge ne couvre que le markdown** : `raw/`, le PDF et
  les CSV en sont exclus — pour ces sources, chercher directement dans
  les fichiers.

## Contenu niveau 95+ / Vendetta (hors périmètre — référence)

Tout ce qui suit existe dans le corpus mais **ne doit pas être
implémenté** (serveur = cap 90). Détail complet dans les index.

**wiki-tables** (le gros du contenu >90) :
- **Sets/armes éveillés** : `C/Lv95_Awaken_Sets` (11 sets),
  `C/Lv100_Awaken_Weps` (26 armes), `C/Lv110_Awaken_Sets` (11 sets).
- **Trials** : `D/Trials_Lv95` (4 Dimension Trials, 9 sets, ~45
  armes), `D/Trials_Lv100` (4 trials, 11 sets), `D/Trials_Lv110`
  (4 Dragon Trials), `D/Trials_Lv130` (4 trials).
- **Donjons endgame** : `D/Abyss` (~115), `D/Deep_Abyss` (~125,
  + sets Awaken par tablets), `D/Celestial_Corridor` I/II (~117/127,
  12 sets zodiaque), `D/Haven_of_Oblivion` (95+, badges + blueprints
  accessoires), `D/Lv98_GoP` (Gate of Pandemonium), `D/Lv110_
  Dragonridge_Sanctuary`, `D/Viridian_Gorge` (échange gemmes 110-125).
- **Battle maps** : `BM/Dimension_of_Souls` (95), `BM/Demonic_Valley`
  (~100-125), `BM/Dragonside_Ridge` (108-110).
- **Gemmes** : `G/Arcane_Box_Lv95_105_110` (gemmes élémentaires
  combinées).
- **Sections >90 dans fichiers `[~90]`** : crafts raciaux paliers
  95/100/110, expertise classes éveillées, DoD 95, Colosseum 95/100,
  titres Vendetta, loots zones >90 dans `T/Trophies`.
- **Classes Vendetta** : Mimic, Agent, Inquisitor, Luminary, Wyvern
  Lord + 42 classes éveillées (Dragon Soul/Emperor, Blade Acrobat,
  Arch-Elementalist…) — stats dans `M/Class_Expertise`, sorts dans
  `guides-fr/pages/competences-des-*`.
- **Monnaies custom** : Abyssal/Fragmented Abyssal Soul, Dragon
  Trial Coins, Boss Gemstones, Zodiac/Spirit Tokens+Sparkles,
  Elemental Stones (Ocean/Flame), Ether Crystal, Gem Extract,
  Viroona Godstone, Constellation Stone/Tablet, Gate Key of
  Pandemonium, D.T. Coins, Cass Trial Coins, Temple Arena Medals.
- **`[?]`** : `D/Elemental_s_Battlefield` (custom, niveau à vérifier).

**guides-fr** (pages entières hors périmètre, ~25) :
- HQ95/100 : `donjons-heroiques-hq-95`, `…-hq-95-worldboss`,
  `…-hq-100`, `donjons-heroiques-dod-95`.
- Équipement : `armures-eveillees-lvl-95`, `gemmes-du-4trial-vendetta`,
  `armes-eveillees-75-2` (section 95), `zones-de-combat` (Dimension
  of Soul niv.90 VGN).
- Donjons VGN : `4trial` (4 Trials of the Dimension), `gates-of-
  pandemonium`, `dragon-slaying-expedition`, `expedition-anti-dragon`
  (version Vendetta).
- Classes : `competences-des-*` (classes éveillées VGN : Dragon Soul,
  Arch-Elementalist, Adjudicator…), `classes.md` sections éveillées.
- Patch notes : `vi-patchnotes-vgn` + toutes les sous-pages (VGN 1.x).
- Détail par page : `guides-fr/INDEX.md` (flags « hors périmètre » /
  « partiellement hors périmètre »).

**wiki-en** : aucun item >90 en base (le dump est ère X-Legend cap
90 ; seuls 2 parchemins « lv100 » couvrent l'équipement ≤100, restant
dans périmètre). Les flags `awaken-lvl95+` = lv70/80, voir le
warning en tête.

**wiki-fr** : aucun contenu >90 (wiki arrêté avant).

## Regénération

| Cible | Commande |
|---|---|
| `wiki-en/pages-index.tsv`, `INDEX.md`, `categories/` | `python3 knowledge/raw/wiki-en/_build_index.py` (nécessite `raw/` présent) |
| `wiki-fr/INDEX.md`, `guides-fr/INDEX.md` | `python3 knowledge/tools/build_index.py` (lit `tools/index_data.json`) |
| `wiki-fr/pages/`, `guides-fr/pages/`, `index_data.json` | `python3 knowledge/tools/extract_fr.py` (lit `raw/`) |
| `wiki-tables/**/*.md` | `python3 knowledge/wiki-tables/csv_to_md.py` (lit `src_csv/`) |
| `graphify-out/` (graphe du knowledge) | `tools/graph/.venv/bin/graphify update knowledge` (depuis la racine du dépôt, ~local, sans LLM) |
