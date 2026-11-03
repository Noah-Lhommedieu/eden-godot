# Classes éveillées — textes UI, quêtes, ref-shots — harvest

> Données verbatim des classes éveillées (ids 21–61) : noms/traits/
> descriptions/conditions FR+EN, séries de quêtes d'éveil, inventaire
> des captures de référence, divergences de version et anomalies de
> données. Compagnon de `classes-catalog.md` (catalogue) et
> `refshots-classes.md` (géométrie/style).
>
> Statut : **CONFIRMED** = présent dans ≥1 source citée ; **MISSING** =
> absent des données natives ; **PLACEHOLDER** = entrée vide réservée.
>
> ⚠️ **Corrections par rapport au thread précédent** :
> 1. Le mapping FR des ids 33–42 du résumé précédent était faux —
>    le fichier réel dit : 33 `Bourreau`, 34 `Chevalier du Serment`,
>    35 `Mécha Arès`, 36 `Annihilateur`, 37 `Asura`, 38 `Rakshasa`,
>    39 `Maître des Démons`, 40 `Voyageur du Temps`, 41 `Lanceur de
>    Lames`, 42 `Danseur Élégant`.
> 2. La source EN des classes n'est PAS `eden_godot/dist/…` (chemin
>    généré cité dans `classes-catalog.md`) — c'est
>    `eden_server/server_us/Data/db/T_Class.ini` (ids canoniques 1–61).
> 3. Deux captures de quête d'éveil existent en plus de la liste
>    précédente : `QuestTracker .png` (« 85. Awakening Mecha Ares I »)
>    et `quest UI.png` (« L85. Awakening Dawnbreaker II ») +
>    `carte.png`/`shot_0022_235811.png` (« Class Awakening Quest »
>    dans la Region Map).
>
> Sources :
> - `INI-FR` = `eden_godot/godot_project/gamedata/data/db/lang/fr/t_class.ini`
>   (l.56–141 = ids 21–61 ; l.3–55 = ids 1–20 ; l.142+ = variantes
>   raciales 101+)
> - `INI-EN` = `eden_server/server_us/Data/db/T_Class.ini`
>   (l.268–305, 447–478, 607–614, 659–662, 679–682 = canoniques 21–61)
> - `MIS-FR` = `eden_godot/godot_project/gamedata/data/db/lang/fr/t_mission.ini`
>   (l.2079–2118, 2155–2166, 2231–2238, 2281–2285, 2301–2308, 2447–2454,
>   2460–2467 — 66 « Quête d'Éveil de Classe »)
> - `MIS-EN` = `eden_server/server_us/Data/db/T_Mission.ini`
>   (l.2247–2254, 2306–2351, 2462–2480 — 66 « Class Awakening Quest » +
>   livres test 1997–2001)
> - `NPC-FR` = `t_npc.ini` l.1905, 2162, 2257, 2329–2339, 2428–2442,
>   2606–2626, 2765, 2879–2881 — PNJ « Quête d'Éveil de Classe »
>   (Yukari, Samara, Shinfeyz, Tyke, Moe Gerald, Jasmine, Jira, Lanz,
>   Séfiro…)
> - `NODE-FR` = `t_node.ini` ~l.763–768 : 189 « Donjon d'Éveil de
>   Classe - Illusionniste », 190–194 « Class Awakening Dungeon
>   (Place Holder) » ; miroir EN `T_Node.ini` l.3428
> - `SHOTS` = `ref-shots/` (non versionné)
> - `CAT-UI` = `eden_godot/docs/client-assets/ref-shots-catalog.md`
>   (§4 l.162–261 panneau classes, §10 l.489–504 quêtes éveil,
>   §12 l.613–628 carte « Class Awakening Quest »)
> - `GFR` = `knowledge/guides-fr/pages/classes.md` (l.18–20 mécanique
>   de déblocage : Yukari @ Aven X336.Y271, parchemins 1.000 PO, base
>   lv 80, éveillé démarre lv 80, expertise à 85 ; l.26–53 noms de
>   fichiers d'icônes wiki par classe éveillée)
> - `WFR-OUTFIT` = `knowledge/wiki-fr/pages/Apparence_de_classe.md`
>   (l.14–18 : K → onglet liste, preview à droite, numéros = tenues,
>   molette zoom, bouton « Tenue »)

## 1. Inventaire ref-shots (statuts)

Présentes dans `ref-shots/` :

**Éveil direct :**
- `QuestTracker .png` *(espace avant .png — CONFIRMED)* — tracker
  déplié : titre rouge « 85. Awakening Mecha Ares I », note jaune
  « Right-click the quest book for additional information », objectifs
  losanges bleus (1/1, 10/10) et violets (0/1).
- `quest UI.png` *(CONFIRMED)* — dialogue quête « L85. Awakening
  Dawnbreaker II » : lore entre guillemets, « Objective details »,
  « Required items: », « Objectives: » (Talk to Gromkar, Rusty Gear
  1/1, Slate Statue 0/10, Defeat Gromkar 0/1, Ascendancy Token 0/6,
  **Templar Ascendancy - Dawnbreaker 1/1**), boutons Confirm/Close ;
  cadre cible « 81 ★ Archangel of Corruption ».
- `carte.png` + `shot_0022_235811.png` *(CONFIRMED, 2ᵉ = doublon)* —
  Region Map Aven : « Search Listing » contient l'entrée
  **« Class Awakening Quest »** parmi les catégories PNJ.

**Panneau Classes (K) — présentation native :**
- `ClassePanel.png` — fenêtre Class x≈200–1140 y≈90–705, onglet
  « Class Skills » ; liste gauche ~62 px/entrée (Mimic🔒, Warrior Lv.1
  [Transform!], Knight🔒, Templar🔒, Dragon Knig…🔒, Thief Lv.1) ;
  en-tête 3 icônes (active + coins dorés animés + ruban bleu « Lv.14 »,
  icône 2 désaturée = **classe éveillée non débloquée**, icône 3 « ? ») ;
  Reg. Skills + « 550 Gold 9 Silver » ; Class Skills + « 4331
  remaining CP » ; tooltip Life Cure.
- `HoverToolTipClasse.png` + `shot_0048_000033.png` — tooltip classe
  verrouillée Shaman : nom blanc + description + « Class Requirement:
  * Character Level 45 * Cleric Level 30 » **en rouge** = gabarit
  exact du tooltip de classe (à réutiliser pour les éveillées).
- `ClassePanel-ClassTalent&Certificat.png` + `shot_0050/0051/0052` —
  License Knowledge : arbre de talents, « 7 KPs Remain / 1 KPs
  Entered », grille certificats 3×3 fleur-de-lis + gemmes « + »
  dorées aux intersections ; liste montre Ranger🔒, Inquisitor🔒,
  Cleric Lv.14 (bord vert), Bard Lv.1, Shaman🔒, Sage🔒.
- `case pour certificat.png` — crop 169×200 : grille remplie, 2 gemmes
  allumées ; fond lavande pâle (contexte différent).
- `ClassePanel-ClassNews.png`/2/3 — fiche classe : description,
  « Class Stat Bonus » (%), « Weekly Performance », preview 3D +
  sélecteur tenue 1–9 + « Set Outfit » ; tooltip Hammer of Discipline
  (arme requise rose, niveau requis rouge).
- `ClassPanel-ChangementDeClasse.png`, `shot_0056_000116.png`,
  `shot_0057_000122.png` (méta de 0056) — bannière « Switched Class to
  Thief/Bard » + ailes de dragon noir + messages « You have learned
  skill "N". » ; hotbar vidée.
- `NouveauSkillApresLevelUp.png` — « You have learned skill Ambush. »
  plein écran.
- `ClassAbilitybonuslogo.png` — tooltip violet « Class Ability Bonus
  Total » (HP/MP/ATK/Magic/G-Healing en points) + bouton « CE List ».
- `skill hover info` *(PNG sans extension)* — gabarit tooltip skill
  (Encourage).
- HUD liés : `TopLeftVitals.png` (LV perso couronne + LV classe icône
  bleue), `DoubleEXPbar.png` (barre classe violette 100.000 %),
  `HotbarPanel.png`, `party UI.png` (icône classe magenta + niveau),
  `CommunityPanel.png` (colonne « Class »), `profile.png`,
  `profile-Title.png`, `ChatPanel-Peer.png` (« Switched Class to
  Thief » rouge).

**⚠️ Références cataloguées mais fichiers absents du dossier actuel** :
`targetframe (Elite mob).png`, `shot_0085_000355.png`,
`shot_0086_000400.png` — cités dans CAT-UI (l.655–716) ; à marquer
MISSING ou vérifier renommage.

**MISSING (aucune capture)** : aucune entrée de liste ne montre une
**classe éveillée débloquée/active** (toutes les classes listées sont
des classes normales ; les éveillées n'apparaissent que comme
silhouette désaturée dans l'en-tête). Aucune capture du panneau
« Sort by class » montrant le regroupement par branche des éveillées.
Pas de tooltip d'une classe éveillée réellement verrouillée (Shaman
seulement). Pas d'écran de création/race. Pas de capture du parchemin
d'éveil chez Yukari ni de la modale d'achat 1.000 PO.

## 2. Présentation native des classes éveillées dans l'UI

- En-tête classe : icône active (coins dorés **animés** + ruban bleu)
  | silhouette désaturée de l'éveillée | « ? » pour la 2ᵉ éveillée non
  révélée.
- Tooltip classe = fond noir ~85–90 %, bordure grise fine ; nom blanc ;
  description blanche ; « Class Requirement: » + lignes `*` en
  **rouge**. Le champ INI conditions (col. 6, lignes suivantes) est
  verbatim avec ses puces (`*`/`?` dans les données FR).
- États liste : active = bord vert + « Current Clas » ; débloquée non
  active = « Lv.N » + bouton « Transform! » (~70×20 px) ; verrouillée =
  icône désaturée + 🔒 ; inconnue = « ? ».
- Slots vides = fleur-de-lys sombre (skills, certificats, hotbar).
- Éveillée débloquée démarre lv 80, expertise à 85 (GFR l.19).
- Tenues de classe : K → preview droite, numéros = tenues obtenues/à
  obtenir, molette zoom, bouton « Tenue »/« Set Outfit »
  (WFR-OUTFIT l.14–18 + ClassNews shots).
- Icônes éveillées : noms de fichiers wiki GFR disponibles
  (`war_berserker.png`, `cheva_executioner.jpg`, `cheva_glacier.png`,
  `assa_ashura.png`, `shaman_druid.jpg`, `shaman_totem.jpg`,
  `juge_reaper.jpg`, `juge_rifleeter.jpg`, `lumi_darkness.png`,
  `lethalarrow.png`… — liste complète `classes.md` l.26–53) — utile
  pour identifier les icônes dans les atlas natifs.

## 3. Textes par id 21–61 (verbatim)

Format INI : `id | nom | description courte | trait/titre |
description longue (lore) | conditions requises` (le champ conditions
déborde sur les lignes suivantes).

### INI-FR — `t_class.ini` lignes 56–141 (CONFIRMED)

| id | FR | Trait | Desc. courte | Desc. longue | Conditions |
|---|---|---|---|---|---|
| 21 | Barbare | Titan Timbré | « Délaissant la défense, le barbare oublie la prudence ; il attaque et fonce avec force. Le barbare dégaine son arme plus vite que l'éclair pour repousser les ennemis. » | « Avec un entraînement physique intense… transcender les limites de la chair… » | * Guerrier niveau 80 ; * Finissez entièrement la mission Âme ancestrale du Titan |
| 22 | Muraille | Corps de Titan | « C'est au prix de nombreuses vies que la muraille a obtenu ses compétences en défense… » | « Rejoignez le front et prenez la défense de ceux qui cherchent refuge. » | * Guerrier 80 ; * Âme Ancestrale du Titan |
| 23 | Archélémentaliste | Débauche d'Énergie | « …manipuler les forces de la nature. Il s'exprime par la volonté des éléments. » | « Des années d'étude… manier la glace, le feu et la foudre… » | * Magicien 80 ; * Enquête Magique des Gardiens |
| 24 | Astromagicien | Gravité | « Ayant rompu avec la magie traditionnelle des trois éléments, l'astromagicien possède une arme invisible et inéluctable. » | « La maîtrise de la gravité est une pratique instinctive… limiter les déplacements de ses cibles. » | * Magicien 80 ; * Enquête Magique des Gardiens |
| 25 | Maître des Bêtes | Dresseur d'animaux | « …libérer l'immense potentiel des animaux sauvages… » | **Vide** | **Vide** (MISSING) |
| 26 | Chasseur de Démons | Marque Mortelle | « Les attaques astucieuses sont la spécialité des Chasseurs de Démons… » | **Vide** | **Vide** (MISSING) |
| 27 | Disciplinaire | Mot de Pouvoir | « Armés de leur foi en un état supérieur de l'être… » | **Vide** | * Clerc Niv. 80 ; * Finissez la série de quêtes Temple Perdu |
| 28 | Prêtre de l'Ombre | Ténèbres Sacrées | « Le prêtre a reçu le pouvoir de la lumière mais également celui des ténèbres… » | **Vide** | * Clerc 80 ; * Temple Perdu |
| 29 | Assassin | Subtilité | « Tapis dans l'ombre, les assassins maîtrisent l'art de supprimer leurs adversaires… » | **Vide** | **Vide** (MISSING — quête existe : Frères d'aventures-Ruines) |
| 30 | Kageryu | Ninjitsu | « Les Kageryu ont tout appris des ninjitsu… » | **Vide** | **Vide** (MISSING) |
| 31 | Poète de Combat | Épopée | « Le poète de combat est aussi motivant que le son du clairon… » | « Le barde décrit dans ses poèmes la sagesse d'innombrables batailles… » | * Barde 80 ; * Partitions Secrètes du Berger |
| 32 | Fidèle de l'Essence | Psaume | « Ses prières sont aussi douces qu'une nuit d'été au clair de lune… » | « Le barde passe près des corps qui jonchent le champ de bataille et ramène les défunts à la vie. » | * Barde 80 ; * Partitions Secrètes du Berger |
| 33 | Bourreau | Épée d'Exécution | « L'attaque est la plus belle forme de défense… » | **Vide** | * Chevalier 80 ; `?Terminez la suite de quêtes Chevaliers du Saphir` |
| 34 | Chevalier du Serment | Bouclier du Serment | « …aptitudes en défense magique. Son bouclier le guide… » | **Vide** | * Chevalier 80 ; **2ᵉ ligne VIDE** (MISSING partiel) |
| 35 | Mécha Arès | Essai d'Acier | « Le Mécha Arès lie son corps et son âme à l'arme qu'il possède. » | « …transformer le guerrier en arme. » | * Ingénieur 80 ; * Recherches Secrètes de Nikola |
| 36 | Annihilateur | Bombardement Fatal | « L'annihilateur maîtrise tous les types d'armes à feu… » | « Qui a besoin d'une armure… tout brûle ? » | * Ingénieur 80 ; * Recherches Secrètes de Nikola |
| 37 | Asura | Cosmos | « L'Asura est un assassin qui a transformé chaque partie de son corps en arme… » | **Vide** | * Assassin 80 ; `?Terminez la suite de quêtes Ursun en crise` |
| 38 | Rakshasa | Chasse-vent | « …empreintes fantomatiques… compétences diaboliques… » | **Vide** | * Assassin 80 ; **2ᵉ ligne VIDE** (MISSING partiel) |
| 39 | Maître des Démons | Contrôleur des Démons | « Ordonnez aux démons des différentes dimensions… » | **Vide** | **Vide** (MISSING) |
| 40 | Voyageur du Temps | Seigneur du Temps | « A le pouvoir de remonter, stopper, accélérer et ralentir le temps… » | **Vide** | **Vide** (MISSING) |
| 41 | Lanceur de Lames | Rafale de Lames | « Le lanceur de lames ne se produit que sur scène. » | « …manie ses lames comme un dieu… » | * Danselame 80 ; * Danse de Désespoir |
| 42 | Danseur Élégant | Danse Rapide | « La Lame d'Ivoire est terminée… » | « …préférant charmer ses adversaires plutôt que de les combattre. » | * Danselame 80 ; * Danse de Désespoir |
| 43 | Druide | Volonté des Ancêtres | « Le Druide puise sa force des âmes de ses ancêtres… se transformer… » | « Une volonté héritée de ses ancêtres… » | **Vide** (MISSING — quête existe : Rituel du Sacrifice ancestral, MIS-FR 3464–3467) |
| 44 | Shaman | Invocation Spirituelle | « La foi du Shaman est aussi la source de son pouvoir… Totems… » | « En appelle aux esprits… contrat avec votre âme… » | **Vide** (MISSING) — ⚠️ collision nom id 12 |
| 45 | Chevalier déchu | Présence Sanglante | « …veines gangrénées par la folie… convertissent l'énergie du sang… » | **Vide** | **Vide** (MISSING — quête : Chose promise, chose due, MIS-FR 3452–3455) |
| 46 | Paladin | Sanctificateur | « Bouclier, check. Masse de guerre, check !… » | **Vide** | **Vide** (MISSING) |
| 47 | Corsaire 2 | Vide | Vide | Vide | Vide (PLACEHOLDER) |
| 48 | Corsaire 3 | Vide | Vide | Vide | Vide (PLACEHOLDER) |
| 49 | Messager des Enfers | Pacte du Démon | « …assujettir n'importe quel démon. » | **Vide** | * Démoniste Niv. 80 ; * Maléfice imminent |
| 50 | Mage Martial | Conjuration | « Vise les points faibles… malédictions… » | **Vide** | * Démoniste 80 ; * Maléfice imminent |
| 51 | Maître-lame | Art de la guerre triple | « …abattre un ennemi en un seul coup fatal. » | « Un style inspiré des variations de la nature et de la lune… » | `?Samouraï Niv. 80` ; `?Katana Perdu` |
| 52 | Maître Escrimeur | Lune Dansante | « …technique ancienne dite de Lune Dansante… » | « …mélangeant le style des trois plus grandes écoles de Katana… » | « Conditions requises : » puis **lignes VIDES** (MISSING partiel) |
| 53–58 | Sage 2/3, Chevalier Dragon 2/3, Chevalier du Chaos 2/3 | Vide×4 | PLACEHOLDERS | | |
| 59 | Juge | Verdict Final | « Sortis tout droit du Royaume des morts, les Juges envahissent Ezur… » | « Les Inquisiteurs, à l'image du Dieu de la mort Azrael… Verdict Final : domination ultime. » | * Terminez la quête : `<Briser les murs III>` |
| 60 | Pourfendeur | Faux du Jugement | « Les Pourfendeurs préfèrent aller au corps à corps… » | ⚠️ **copier-coller du texte du Barbare (21)** | ⚠️ * Guerrier 80 + Âme Ancestrale du Titan (probable copier-coller — devrait être lié au Juge) |
| 61 | Fusilier | Tribunal | « Les Fusiliers bénéficient d'attaques à distance améliorées… fusil à la main… » | ⚠️ **copier-coller du texte de la Muraille (22)** | ⚠️ idem Guerrier 80 + Titan |

### INI-EN — `T_Class.ini` (CONFIRMED; ligne citée)

| id | EN | Trait | Lignes | Conditions EN |
|---|---|---|---|---|
| 21 | Berserker | Frenzied Titan | 268–270 | Warrior Level 80 ; « Titan's Ancestral Soul » quest series |
| 22 | Shielder | Titan's Body | 271–273 | idem |
| 23 | Arch-Elementalist | Energy Burst | 274–276 | Magician 80 ; « Guardian's Foray into Magic » |
| 24 | Gravity Manipulator | Gravitation | 277–279 | idem |
| 25 | Trainer | Beast Mastery | 447 | **Empty** reqs/long (MISSING) |
| 26 | Devil Hunter | Death Sigil | 448 | **Empty** (MISSING) |
| 27 | Adjudicator | Harsh Sentence | 449–451 | Cleric LV 80 ; « Lost Temple » series. ⚠️ texte contient mojibake `�V` |
| 28 | Equilibrian | Divine Darkness | 452–454 | idem |
| 29 | Assassin | Subtlety | 659 | **Empty** (MISSING) |
| 30 | Kage | Ninjitsu | 660 | **Empty** (MISSING) |
| 31 | Battlefield Poet | Epic Poetry | 280–282 | Bard 80 ; « Shepherd's Secret Musical Scale » |
| 32 | Life Worshipper | Psalm | 283–285 | idem |
| 33 | Executioner | Sword of Execution | 455–457 | Knight 80 ; « Sapphire Knights » |
| 34 | Glacier Knight | Shield of Oath | 458–460 | idem |
| 35 | Mecha Ares | Steel Intent | 286–288 | Engineer 80 ; « Nikola's Secret Research Project » |
| 36 | Annihilator | Fatal Bombardment | 289–291 | idem |
| 37 | Asura | Cosmos | 461–463 | Martial Artist LV 80 ; « Crisis of Ursun » |
| 38 | Rakshasa | Windseeker | 464–466 | idem |
| 39 | **Demon Tamer** | Demon Master | 679 | **Empty** (MISSING) — ⚠️ doublon de nom avec id 49 |
| 40 | Chronologist | Time Lord | 680 | **Empty** (MISSING) |
| 41 | Blade Acrobat | Flurry Blades | 292–294 | Blade Dancer 80 ; « Lost Dance » |
| 42 | Elegant Dancer | Swift Dance | 295–297 | idem |
| 43 | Druid | Ancestor's Will | 681 | long present, reqs **Empty** (MISSING) |
| 44 | Archimage | Spiritual Invocation | 682 | long present, reqs **Empty** (MISSING) — éveillé du Shaman |
| 45 | Blood Knight | Campus Martius | 661 | **Empty** (MISSING) — live/Reforged = « Dawnbreaker » |
| 46 | Paladin | Holy Arrival | 662 | **Empty** (MISSING) |
| 47/48 | Ranger 2 / Ranger 3 | — | 298–299 | Empty placeholders (⚠️ pas « Corsaire », EN base = Ranger) |
| 49 | Demon Tamer | Demon Pact | 467–469 | Warlock LV 80 ; « Imminent Evil » |
| 50 | Conjurer | Conjuring | 470–472 | idem |
| 51 | Blade Master | Three River Style | 473–475 | Samurai LV 80 ; « Lost Katana » |
| 52 | Master Fencer | Dancing Moon | 476–478 | idem |
| 53–58 | Sage 2/3, Dragon Knight 2/3, **Luminary 2/3** | — | 300–305 | Empty placeholders |
| 59 | Inquisitor | Final Verdict | 607–608 | « Complete the quest: "Sie Sind Das Essen III." » |
| 60 | Reaver | Scythe of Judgement | 609–611 | Warrior 80 + Titan's Ancestral Soul (même copier-coller que FR, mais texte long **adapté** : « Intense physical training allows Reavers… ») |
| 61 | Rifleteer | Court of Judgement | 612–614 | idem (texte long adapté aux Rifleteers) |

### Divergences FR↔EN

- id 39 : FR « Maître des Démons » vs EN **Demon Tamer** (= doublon
  interne EN avec id 49 « Demon Tamer » ; GFR : Demon Summoner).
- id 40 : FR « Voyageur du Temps » vs EN **Chronologist** (GFR : Time
  Manipulator).
- id 44 : FR « Shaman » (collision avec base 12) vs EN **Archimage**
  (GFR : Totem Master).
- id 45 : FR « Chevalier déchu » vs INI-EN **Blood Knight** vs
  live/Reforged **Dawnbreaker** (prouvé par `quest UI.png`
  « Awakening Dawnbreaker II » ; GFR/classes-catalog = Dawnbreaker).
- id 24 : EN **Gravity Manipulator** (GFR : Stormcaster).
- id 47/48 : FR « Corsaire 2/3 » vs EN « Ranger 2/3 » (GFR : Lethal
  Arrow / Celestial Arrow).
- id 57/58 : FR « Chevalier du Chaos 2/3 » vs EN « Luminary 2/3 »
  (GFR : Darkness Blade / Holy Blade) — confirme que base FR 19
  « Chevalier du Chaos » = EN Luminary.
- id 53–56 : FR/EN concordants (Sage 2/3, Dragon Knight 2/3 ; GFR :
  Holy Sage/Angel of Justice, Dragon Soul/Dragon Emperor).
- EN 60/61 ont des textes longs adaptés ; FR 60/61 = copier-coller
  brut de Barbare/Muraille → bug de données FR.
- Puces de conditions : `*` normal, mais `?` dans FR 33/37/51
  (marqueur non résolu dans les données).
- Nomenclature des placeholders : EN garde les noms de branche EN
  (Ranger, Luminary) ; FR garde les noms FR (Corsaire, Chevalier du
  Chaos) → confirme le mapping des placeholders.

## 4. Séries de quêtes d'éveil (MIS-FR / MIS-EN — CONFIRMED)

| Classe de base | FR (ids mission) | EN (ids mission) | Nb |
|---|---|---|---|
| Guerrier | Âme Ancestrale du Titan I–V (3107–3111) | Titan's Ancestral Soul I–V | 5 |
| Magicien | Enquête Magique des Gardiens I–IV (3089–3092) | Guardian's Foray into Magic | 4 |
| Chasseur | Une Terre Contaminée I–VIII (3237–3244) | A Tainted Land I–VIII | 8 |
| Clerc | Temple Perdu I–V (3287–3291) | Lost Temple I–V | 5 |
| Voleur | Frères d'aventures - Ruines I–IV (3456–3459) | Super Explorer Bros. I–IV | 4 |
| Barde | Partitions Secrètes du Berger I–IV (3116–3119) | Shepherd's Secret Musical Scale | 4 |
| Chevalier | Chevaliers du Saphir I–VII (3161–3167) | Sapphire Knights I–VII | 7 |
| Ingénieur | Recherches Secrètes de Nikola I–IV (3085–3088) | Nikola's Secret Research Project | 4 |
| Assassin (MA) | Ursun en crise I–V (3168–3172) | The Crisis of Ursun I–V | 5 |
| Danselame | Danse de Désespoir I–IV (3112–3115) | Lost Dance I–IV | 4 |
| Shaman | Rituel du Sacrifice ancestral I–IV (3464–3467) | Ritually Speaking I–IV | 4 |
| Templier | Chose promise, chose due I–IV (3452–3455) | Unfulfilled Promises I–IV | 4 |
| Démoniste | Maléfice imminent I–IV (3307–3310) | Imminent Evil I–IV | 4 |
| Samouraï | Katana Perdu I–IV (3311–3314) | Lost Katana I–IV | 4 |
| — | Livre d'Éveil de Classe - Guerrier/Danselame/Barde/Ingénieur/Magicien `<Test>` (3120–3124) | Test … Class Awakening Book | 5 items test |

**MISSING dans les données mission** : Illusionniste (39/40),
Corsaire (47/48), Sage (53/54), Chevalier Dragon (55/56),
Luminary/Chevalier du Chaos (57/58), Juge (60/61). Le Rituel du
Sacrifice ancestral (Shaman) était marqué MISSING dans
`classes-catalog.md` §7 — il **existe** dans les deux fichiers mission
(à corriger/signaler).

Variante live/Reforged (ref-shots) : quêtes « L85. Awakening ‹Classe›
I/II… » avec objectifs « ‹Base› Ascendancy - ‹Éveillé› » +
« Ascendancy Token » — nomenclature différente des séries INI natives
(L80).

## 5. Lacunes / anomalies

- Quêtes non inscrites : ids 39/40, 47/48, 53–58, 60/61 (voir §4).
- Copier-coller FR 60/61 (textes Barbare/Muraille + conditions
  Guerrier/Titan — probablement des données brouillon).
- `?` bullets dans FR 33/37/51 ; mojibake `�V` dans EN 27 ; doublon
  EN « Demon Tamer » 39/49 ; collision FR « Shaman » 12/44.
- Aucune capture d'éveillée débloquée/active, du « Sort by class »
  groupé, du parchemin Yukari, de la modale 1.000 PO, de
  l'écran création/race, ni du panneau « CE List ».
- Fichiers cités par CAT-UI mais absents : `targetframe (Elite
  mob).png`, `shot_0085_000355.png`, `shot_0086_000400.png`.

> *Doc compilé 2026-10 — verbatim INI + recoupement ref-shots/quêtes ;
> les descriptions de captures sont reprises des catalogues existants
> (CAT-UI, HARV-CLS), non re-vérifiées visuellement.*
