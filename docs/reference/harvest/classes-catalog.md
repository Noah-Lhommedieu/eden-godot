# Catalogue des classes — Eden Eternal (périmètre lvl ≤ 90)

> Récolte croisée wiki FR/EN + données client. Sert de vérité « wiki » pour
> l'implémentation du système de classes (cross-check `c_class`/`t_class`).
>
> **Sources citées par groupe de lignes.** Statut : **CONFIRMED** = présent
> dans ≥1 source ci-dessous ; **MISSING** = absent des sources récoltées.
>
> Sources :
> - `INI-FR` = `eden_godot/godot_project/gamedata/data/db/lang/fr/t_class.ini`
> - `INI-EN` = `eden_godot/dist/package-linux/gamedata/data/db/T_Class.ini`
> - `WFR` = `knowledge/wiki-fr/pages/` (fandom FR converti)
> - `WEN` = `knowledge/wiki-en/categories/classes.md` + `knowledge/raw/wiki-en/*.html`
> - `GFR` = `knowledge/guides-fr/pages/` (edenfr.wordpress.com, serveur Vendetta)
> - `XLS` = `knowledge/wiki-tables/M/Class_Certificates.md` (classeur Vendetta)

## 1. Vue d'ensemble

- **61 classes** au total dans les données (ids 1–61). Les ids > 61
  (101–120, 201–220, …) sont des **variantes raciales** des mêmes classes
  dans `T_Class.ini` — hors catalogue. *(INI-EN, INI-FR — CONFIRMED)*
- **5 branches** : Défense (tank, certificats jaunes), DPS corps à corps
  (orange), DPS à distance (vert), Soin/Soutien (bleu), DPS magique.
  *(WFR `Classes.md`, XLS — CONFIRMED)*
- **Classes « normales » (non éveillées)** : 21 = les 20 des 5 branches
  (4 chacune) + le Messie hors branche. Le Juge est la 4ᵉ classe DPS
  distance. *(GFR `classes.md` — CONFIRMED)*
- **Classes éveillées** : 2 variations par classe normale sauf Messie.
  Déblocage par quête dédiée + classe de base niv. 80.
  *(GFR `classes.md`, `prerequis-des-classes.md` — CONFIRMED)*
- **Races** : 5 jouables (Humain, Ezelin, Zumi, Anuran, Ursun). Toutes les
  classes sont jouables par toutes les races ; seuls les **traits raciaux
  et modificateurs de stats** diffèrent (table raciale par classe dans
  chaque page WFR/WEN). *(WFR pages classes, WEN — CONFIRMED)*
- Le changement de classe est **libre et gratuit** en jeu ; niveau de
  classe indépendant du niveau personnage (plafonné par celui-ci).
  *(WFR `Classes.md` — CONFIRMED)*
- ⚠️ **Divergence de version** : les prérequis de `INI-EN` (client Aeria
  d'origine) sont **inférieurs** à ceux de `INI-FR` + wiki FR + guides
  Vendetta. Les deux colonnes sont données ci-dessous.

## 2. Classes normales (ids 1–20 + 59)

| id | EN | FR | Branche | Prérequis INI-FR / wiki FR | Prérequis INI-EN | Type |
|---|---|---|---|---|---|---|
| 1 | Warrior | Guerrier | Défense | aucun (création) | aucun | base |
| 2 | Magician | Magicien | DPS magique | aucun (création) | aucun | base |
| 3 | Hunter | Chasseur | DPS distance | perso 10 | perso 1 (création) | base |
| 4 | Clerc | Clerc | Soin | perso 5 | perso 1 (création) | base |
| 5 | Thief | Voleur | DPS CàC | perso 15 | perso 1 (création) | base |
| 6 | Bard | Barde | Soin | perso 20 | perso 20 | base |
| 7 | Knight | Chevalier | Défense | perso 25 + Guerrier 20 | perso 25 + Warrior 10 | base |
| 8 | Engineer | Ingénieur | DPS distance | perso 30 + Chasseur 25 | perso 30 + Hunter 15 | base |
| 9 | **Martial Artist** | **Assassin** | DPS CàC | perso 40 + Voleur 35 | perso 40 + Thief 25 | base |
| 10 | Illusionist | Illusionniste | DPS magique | perso 35 + Magicien 30 | perso 35 + Magician 20 | base |
| 11 | Blade Dancer | Danselame | DPS CàC | perso 50 + Barde 45 | perso 50 + Bard 35 | base |
| 12 | Shaman | Shaman | Soin | perso 45 + Clerc 40 | perso 45 + Cleric 30 | base |
| 13 | Templar | Templier | Défense | perso 60 + Chevalier 55 + Clerc 55 | perso 60 + Knight 45 + Cleric 45 | base |
| 14 | **Ranger** | **Corsaire** | DPS distance | perso 65 + Chasseur 60 + Assassin 60 | perso 65 + Hunter 50 + Martial Artist 50 | base |
| 15 | Warlock | Démoniste | DPS magique | perso 55 + Magicien 50 + Shaman 50 | perso 55 + Magician 40 + Shaman 40 | base |
| 16 | Samurai | Samouraï | DPS CàC | perso 60 + Guerrier 55 + Assassin 55 | perso 60 + Warrior 45 + Martial Artist 45 | base |
| 17 | Sage | Sage | Soin | quête **« Épreuve du Sage »** (débloquée perso 69–70) | perso 65 | base, déblocage par quête |
| 18 | Dragon Knight | Chevalier Dragon | Défense | quête **« Réveil de l'Âme du Dragon »** (perso ~70) | perso 65 | base, déblocage par quête |
| 19 | **Luminary** | **Chevalier du Chaos** | DPS magique | aucun (« choisir une classe pour commencer ») | aucun (création) | base |
| 20 | **Mimic** | **Messie** | hors branche | quêtes **« Mort et Résurrection »** (perso 60, PNJ Amos à Aven) | idem | base, déblocage par quête |
| 59 | **Inquisitor** | **Juge** | DPS distance | quête principale « Briser les murs III » ; WEN : « Sie Sind Das Essen III » ; GFR : quête du Temple Enchanté / Jungle Mayland + Bassin Shaxia | perso 65 (INI-EN) | base, déblocage par quête |

*Sources : INI-FR + INI-EN (noms, prérequis), WFR `Tableau_des_classes.md`
(prérequis FR), GFR `prerequis-des-classes.md` + `le-messie-mimic.md` +
`la-quete-du-sage.md` (quêtes), WEN `classes.md` (branches, Sage/Mimic/
Inquisitor/Dragon Knight). — **CONFIRMED** pour tout le tableau.*

⚠️ **Collisions / renommages FR↔EN à retenir** :
- id 9 : FR « Assassin » = EN **Martial Artist** (classe arts martiaux,
  griffes). L'« Assassin » EN est la classe éveillée id 29.
- id 14 : FR « Corsaire » = EN **Ranger**. Ids 47/48 = « Ranger 2/3 ».
- id 19 : FR « Chevalier du Chaos » = EN **Luminary**.
- id 20 : FR « Messie » = EN **Mimic**.
- id 59 : FR « Juge » = EN **Inquisitor** (arme : sickleshot / faux-fusil).

## 3. Classes éveillées (ids 21–58, 60–61)

Une quête d'éveil débloque **les deux spécialisations** de la classe de
base (parchemin de quête + classe de base niv. 80). *(GFR — CONFIRMED)*

### Par branche

| Base | id | FR | EN | Quête / prérequis | Statut |
|---|---|---|---|---|---|
| **Défense** |
| Guerrier | 21 | Barbare | Berserker | Guerrier 80 + série « Âme ancestrale du Titan » (5 quêtes, Forêt de Delphes / Tour des Épreuves ; parchemin « Bandeau zébré ») | CONFIRMED (INI + GFR `quetes-des-classes-eveillees-guerrier.md`) |
| Guerrier | 22 | Muraille | Shielder | idem | CONFIRMED |
| Chevalier | 33 | Bourreau | Executioner | Chevalier 80 + série « Chevaliers du Saphir » (7 quêtes, Bourbes → Royaume Vide/Guillotine ; boss Casey) | CONFIRMED (INI + GFR `quete-de-classe-eveillee-chevalier.md`) |
| Chevalier | 34 | Chevalier du Serment | Glacier Knight | idem | CONFIRMED |
| Templier | 45 | Chevalier déchu | Dawnbreaker | Templier 80 (implicite) + série « Chose promise, chose due » (4 quêtes, Dôme Céleste → Pays des Merveilles Diablepourpre/Basel ; parchemin « Rédemption de Morley », bouclier requis) | CONFIRMED (GFR `quete-de-classe-eveillee-templier.md` ; INI prérequis vide) |
| Templier | 46 | Paladin | Paladin | idem | CONFIRMED |
| Chevalier Dragon | 55 | Chevalier Dragon 2 | Dragon Soul (nom d'après GFR/WEN ; INI = placeholder « Dragon Knight 2 ») | WEN : perso 80 + Dragon Knight 80 ; quête non documentée | PARTIEL (WEN `Dragon_Soul.html` ; GFR liste le nom) |
| Chevalier Dragon | 56 | Chevalier Dragon 3 | Dragon Emperor | MISSING (quête non documentée) | PARTIEL (nom via GFR `classes.md` uniquement) |
| **DPS corps à corps** |
| Voleur | 29 | Assassin | Assassin | Voleur 80 (implicite) + série « Frères d'aventures – Ruines » (4 quêtes, Bourbes → Palais des Bourbes/Grey Lynn ; parchemin « Requête de Rann », classe voleur requise) | CONFIRMED (GFR `quete-de-classe-eveillee-voleur.md` ; INI vide) |
| Voleur | 30 | Kageryu | Kage | idem | CONFIRMED |
| Assassin (id 9) | 37 | Asura | Asura | Assassin/Martial Artist 80 + série « Ursun en crise » (5 quêtes, Aven → donjon Écarlate ; boss Asura) | CONFIRMED (INI + GFR `quete-de-classe-eveillee-assassin.md`) |
| Assassin (id 9) | 38 | Rakshasa | Rakshasa | idem | CONFIRMED |
| Danselame | 41 | Lanceur de Lames | Blade Acrobat | Danselame 80 + série « Danse de désespoir » (donjon Fort Loselle/Crocvenin ; parchemin « Papier à musique de Belema ») | CONFIRMED (INI + GFR `quete-de-classe-eveillee-danselame.md`) |
| Danselame | 42 | Danseur Élégant | Elegant Dancer | idem | CONFIRMED |
| Samouraï | 51 | Maître-lame | Blade Master | Samouraï 80 + série « Katana perdu » (4 quêtes, Monts Arides → Tombe du Samouraï/Baltaroi ; 3 lames à récupérer) | CONFIRMED (INI + GFR `quete-de-classe-eveillee-samourai.md`) |
| Samouraï | 52 | Maître Escrimeur | Master Fencer | idem | CONFIRMED |
| **DPS à distance** |
| Chasseur | 25 | Maître des Bêtes | Trainer | Chasseur 80 + série « Une terre contaminée » (8 quêtes, Vallée de Vulcain → Plaines Séraphines ; parchemin « Lettre d'appel à l'aide de Obspho ») | CONFIRMED (GFR `quete-de-classe-eveillee-chasseur.md` ; INI vide) |
| Chasseur | 26 | Chasseur de Démons | Devil Hunter | idem | CONFIRMED |
| Ingénieur | 35 | Mécha Arès | Mecha Ares | Ingénieur 80 + série « Recherches secrètes de Nikola » (4 quêtes, Monts Arides → Caverne de schiste + Eroda 0/10) | CONFIRMED (INI + GFR `quete-de-classe-eveillee-ingenieur.md`) |
| Ingénieur | 36 | Annihilateur | Annihilator | idem | CONFIRMED |
| Corsaire | 47 | Corsaire 2 | Lethal Arrow (GFR) ; INI = « Ranger 2 » | MISSING (quête non documentée ; GFR `competences-des-corsaires-eveilles.md` = sorts seulement) | PARTIEL |
| Corsaire | 48 | Corsaire 3 | Celestial Arrow (GFR) ; INI = « Ranger 3 » | MISSING | PARTIEL |
| Juge | 60 | Pourfendeur | Reaver | éveillé du Juge (GFR `classes.md`) ; INI-FR : « Guerrier 80 + Âme Ancestrale du Titan » = **copier-coller erroné des données** | PARTIEL (appariement CONFIRMED, quête MISSING) |
| Juge | 61 | Fusilier | Rifleteer | idem | PARTIEL |
| **Soin / Soutien** |
| Clerc | 27 | Disciplinaire | Adjudicator | Clerc 80 + série « Temple perdu » (5 quêtes, Aven → Temple de Delphes ; parchemin « Requête de Tobias ») | CONFIRMED (INI + GFR `quete-de-classe-eveillee-clerc.md`) |
| Clerc | 28 | Prêtre de l'Ombre | Equilibrian | idem | CONFIRMED |
| Barde | 31 | Poète de Combat | Battlefield Poet | Barde 80 + série « Partitions secrètes du berger » (Plaines dorées → Fort Loselle/Crocvenin ; parchemin « Partitions du berger », classe barde requise) | CONFIRMED (INI + GFR `quete-de-classe-eveillee-barde.md`) |
| Barde | 32 | Fidèle de l'Essence | Life Worshipper | idem | CONFIRMED |
| Shaman | 43 | Druide | Druid | MISSING (quête non documentée ; GFR `competences-des-shamans-eveilles.md` = sorts seulement) | PARTIEL (appariement GFR CONFIRMED) |
| Shaman | 44 | Shaman (éveillé) | **Archimage** (INI-EN) / Totem Master (GFR) | MISSING | PARTIEL — ⚠️ nom FR = collision avec id 12 |
| Sage | 53 | Sage 2 | Holy Sage (GFR) ; INI = « Sage 2 » | MISSING | PARTIEL |
| Sage | 54 | Sage 3 | Angel of Justice (GFR) ; INI = « Sage 3 » | MISSING | PARTIEL |
| **DPS magique** |
| Magicien | 23 | Archélémentaliste | Arch-Elementalist | Magicien 80 + série « Enquête magique des gardiens » (4 quêtes, Bourbes → Malice/bibliothèque de Garson ; parchemin « Reçu d'Éléa », farm Dianack 0/10) | CONFIRMED (INI + GFR `quete-de-classe-eveillee-magicien.md`) |
| Magicien | 24 | Astromagicien | Stormcaster | idem | CONFIRMED |
| Illusionniste | 39 | Maître des Démons | Demon Tamer (GFR : Demon Summoner) | MISSING (GFR `competences-des-illusionnistes-eveilles.md` = sorts seulement) | PARTIEL (appariement GFR CONFIRMED) |
| Illusionniste | 40 | Voyageur du Temps | Chronologist (GFR : Time Manipulator) | MISSING | PARTIEL |
| Démoniste | 49 | Messager des Enfers | **Demon Tamer** (INI-EN — ⚠️ doublon de nom avec id 39 ; GFR : Dompteur de démons) | Démoniste 80 + série « Maléfice imminent » (4 quêtes, Marais décrépits → Démon Intérieur/Autel du culte de Mara ; Bâton d'esprit démoniaque requis) | CONFIRMED (INI + GFR `quete-de-classe-eveillee-demoniste.md`) |
| Démoniste | 50 | Mage Martial | Conjurer | idem | CONFIRMED |
| Luminary | 57 | Chevalier du Chaos 2 | Darkness Blade (GFR) ; INI = « Luminary 2 » | MISSING | PARTIEL |
| Luminary | 58 | Chevalier du Chaos 3 | Holy Blade (GFR) ; INI = « Luminary 3 » | MISSING | PARTIEL |

**Messie (20)** : aucune classe éveillée *(GFR `classes.md` — CONFIRMED)*.

Objets/réputation d'éveil documentés : parchemins de quête nommés
ci-dessus ; objets-clés : Bandeau zébré, Bâton d'esprit démoniaque,
Pierre Élégamment Sculptée, collier-pleureur, insigne de l'aventurier.
Aucune réputation requise mentionnée. *(GFR — CONFIRMED)*

## 4. Rôle et sorts signature (classes normales)

| Classe (EN/FR) | Rôle (1–2 lignes) | Sorts signature (wiki) | Source |
|---|---|---|---|
| Warrior / Guerrier | Tank armure lourde ; aggro/malice, quelques AoE, buffs def. Insensible à la Peur. | Charge du Guerrier, Provocation, Mur de Titan, Attaque Cyclone, Intimidation, Provocation de Groupe | WFR `Guerrier.md` |
| Magician / Magicien | DPS magique 3 éléments (feu/glace/foudre) ; utilitaires uniques : téléportation de groupe, invisibilité régén mana. | Éclair, Tempête de Glace, Enfer Ardent, Téléportation, Voile du Blizzard, Réaction de Fusion | WFR `Magicien.md` |
| Hunter / Chasseur | DPS distance à l'arc ; invoque un **tigre familier permanent** ; debuffs précision/soin. | Invocation de Tigre, Triple Flèche, Flèche Éblouissante, Blessure Douloureuse (anti-soin), Pluie de Flèches Empoisonnées | WFR `Chasseur.md` |
| Cleric / Clerc | Soigneur principal : HoT, soins de zone, buffs 15 min, retrait de debuff. | Guérison de Vie, Vent Purificateur, Don du Sacré, Rayonnement de Guérison, Lumière Divine | WFR `Clerc.md` |
| Thief / Voleur | DPS CàC poignards ; **furtivité** (ouverteur Embuscade), stuns, esquive. | Furtivité, Embuscade, Attaque Rapide, Confinement Mortel (stun 3 s sous furtif), Galop | WFR `Voleur.md` |
| Bard / Barde | Soutien offensif : chansons = buffs de groupe offensifs + debuff DEF ennemie. | Hymne à la Guerre, Tambour de Guerre, Métal Hurlant, Symphonie Démoniaque | WFR `Barde.md` |
| Knight / Chevalier | Tank esquive/parade, rapière ; pseudo-DPS attaques rapides. | Asile, Contre Bouclier, Tempête Infernale, Silence, Sang-froid | WFR `Chevalier.md` |
| Engineer / Ingénieur | DPS distance AoE ; **mode mécha** (armure portée : +vitalité, -dégâts). | Robotisation, Super Homme de Fer, Grenade Incendiaire, Bombe à Huile, Missile Destructeur | WFR `Ingénieur.md` |
| Martial Artist / Assassin | DPS CàC griffes, arts martiaux/chi ; buffs pour burst. | Charge, Perce-Armure, Faiblesse Critique, Assaut, Contre-Attaque | WFR `Assassin.md` |
| Illusionist / Illusionniste | DPS magique contrôle : buffs groupe, sommeil, **invocation djin mécha**. | Stalactite Glaciale, Pacte d'Outre-monde (djinn), Contrôle de l'Esprit, Œil Hypnotique, Aérolite | WFR `Illusionniste.md` |
| Blade Dancer / Danselame | DPS CàC AoE épées ; charme/séduction, génère beaucoup de malice. | Lame Dansante, Samba magique, Séduction, Ombre Rapide, Coupe Sonique | WFR `Danselame.md` |
| Shaman | Soutien + soin de zone ; **totems** posés ; **seule résurrection en combat** ; invoque esprits (Puma Noir). | Totem Irrité, Totem Gracieux, Gouttes du Paradis, Résurrection, Puma Noir | WFR `Shaman.md` |
| Templar / Templier | Tank sacré : aggro magie sacrée + soins propres ; excelle en solo. | Lumière Sacrée, Coup de Bouclier, Fin des Temps, Bouclier de Lumière | WFR `Templier.md` |
| Ranger / Corsaire | DPS distance ; **aigle chasseur familier**, pluie de projectiles, mobilité (Saut d'Esquive). | Aigle chasseur, Pluie de Météores, Tir renversant, Poursuite de l'Aigle | WFR `Corsaire.md` |
| Warlock / Démoniste | DPS magie noire : **parasites**, pactes démoniaques, invocations. | Parasite Obscur, Pacte, Attaque Cauchemardesque, Porteur de Peste, Convertisseur de Sang | WFR `Démoniste.md` |
| Samurai / Samouraï | DPS CàC katana, armure lourde ; exécutions rapides. | Cri du Faucon, Coupe Asura, Éclair Unique, Purgatoire | WFR `Samouraï.md` |
| Sage | Soin/soutien marteau béni ; **système d'Auras** swapables ; sauvetage. | Marteau guérisseur, Aura : Bouclier protecteur, Aura : Invisibilité, Aura : Singularité, Grain de sable | WFR `Sage.md` |
| Dragon Knight / Chevalier Dragon | Tank pique ; **modes dragon** interchangeables (embrasé/démoniaque/caché). | Revers d'Écaille, Mode Dragon Embrasé, Mode Dragon Démoniaque, Terreur du Dragon | WFR `Chevalier_Dragon.md` |
| Luminary / Chevalier du Chaos | DPS magique double épées ; combine magie sacrée + ténèbres (dispo à la création). | (magie sacrée + sombre couplées ; sorts classe non listés dans WFR) | WFR `Chevalier_du_Chaos.md` + WEN `Luminary.html` |
| Mimic / Messie | Classe hors branche : **combine les sorts des autres classes** en un build libre. | sorts au choix parmi toutes les classes normales | GFR `le-messie-mimic.md` |
| Inquisitor / Juge | DPS distance au **sickleshot** (faux-fusil/gunblade) ; faux du jugement. | Soul Shackle, Garrote, Staggering Blow (noms EN) | WEN `Inquisitor.html` |

Descriptions 1–2 lignes des éveillés : disponibles dans INI-FR/INI-EN
(champ description) — ex. Barbare « délaisse la défense, fonce »,
Astromagicien « maîtrise de la gravité / limite les déplacements »,
Voyageur du Temps « remonter/stopper/accélérer le temps », Druide
« métamorphose en créatures de la forêt ». *(INI — CONFIRMED pour les
descriptions ; sorts signature éveillés = **MISSING** dans les sources,
sauf mentions dans les quêtes GFR : « bélier/corps de titan/chant de
guerre » (Guerrier éveillé), sorts élémentaires Kageryu, balles
foudre/feu/sacré de l'Annihilateur, invocation Ragnas du « Tourment
Éternel » (Démoniste éveillé), compétences « célébrité/danse de
diversion/samba » (Danselame éveillé).)*

## 5. Mécaniques spécifiques documentées

- **Familiers de classe** : Chasseur → tigre (invocation hors combat,
  attaque sur ordre, buffable/soignable) ; Corsaire → aigle chasseur ;
  Shaman → totems posés + esprits (Puma Noir) ; Illusionniste → djin
  mécha via Pacte d'Outre-monde ; Démoniste → parasites/démons
  (invocation Ragnas en éveillé) ; Maître des Bêtes → invoque les
  monstres domptés. *(WFR, GFR — CONFIRMED)*
- **Furtivité** : Voleur (Furtivité/Force Cachée) + Kageryu
  (« Promenade nocturne ») + Sage (Aura : Invisibilité) + Magicien
  (Voile du Blizzard). *(WFR — CONFIRMED)*
- **Mode mécha** : Ingénieur (Robotisation) ; Mécha Arès pousse le
  concept (transformation en arme vivante). *(WFR/INI — CONFIRMED)*
- **Chansons** : Barde = buffs de groupe offensifs (pas de heal),
  « lire le tempo du combat ». *(WFR — CONFIRMED)*
- **Auras Sage** : buffs de posture échangeables (marteau/élévation/
  bouclier/invisibilité/singularité). *(WFR — CONFIRMED)*
- **Modes dragon** : 3 postures du Chevalier Dragon (flamme/démon/
  caché) modifiant les compétences. *(WFR — CONFIRMED)*
- **Messie** : pas de sorts propres — assemble ceux des autres classes.
  *(GFR — CONFIRMED)*
- **Rez en combat** : exclusivité Shaman. *(WFR — CONFIRMED)*
- **Classes KR-only** : « Canonnier » (artillerie, AoE) — annoncé sur
  wiki FR mais jamais sorti en FR ; **aucun id** dans t_class.
  *(WFR `''Canonnier''.md` — hors périmètre)*

## 6. Certificats (classes normales uniquement)

Mécanique : 3 certificats par classe (débloqués niv. 10/20/30, rang II à
40/50/60) ; 7 emplacements équipables **inter-classes** ; couleur par
branche (jaune tank / orange CàC / vert distance / bleu soutien) ;
combinaisons de 2–4 certifs = bonus. *(GFR `certificats-de-classe.md`,
XLS — CONFIRMED)*

| Classe | Certificats (I → II) | Source |
|---|---|---|
| Guerrier | Raillerie (malice +5/8%) · Armure en Fer (DEF +5/10%) · Flexibilité Musculaire (DEG PHY reçus -3/6%) | GFR ; XLS (EN : Taunt / Iron Armor / Muscle Flexibility — effet III = STR, divergence mineure) |
| Chevalier | Élégance (ESQ +4/8%) · Maître de Défense (Parade +4/7% 2M) · Attaque à la Rapière (double attaque +1/2%) | GFR + XLS |
| Templier | Défense de Bouclier Massif (BLOC +3/5%) · Cœur Sacré (résist. élém. +3/5%) · Attaque Décisive (étourdir 1/2%) | GFR + XLS |
| Chevalier Dragon | Armure Écaille de Dragon (DEG -2/4%) · Rugissement du Dragon (échec sort 1/2%) · Dragon Âme sœur (parade → soin 20/40%) | GFR + XLS |
| Voleur | Agilité (ESQ +4/8%) · Main Ultra-rapide (double +1/2%) · Reflet de l'Orage (AGI +10/15%) | GFR + XLS |
| Assassin (MA) | Puissance Musculaire (FOR +10/15%) · Endurance (PV +5/10%) · Énergie Défensive (DEF +5/10%) | GFR + XLS |
| Danselame | Sourire (malice -5/8%) · Corps Dynamique (Parade +4/7% 2M) · Maître de Danse (CRIT +5/10%) | GFR + XLS |
| Samouraï | Sérénité (DEG CRIT +10/20%) · Loyauté (Parade +4/7% 2M) · Téméraire et Sans Reproches (résist. phy +3/5%) | GFR + XLS |
| Chasseur | Cherche Faiblesse (DEG CRIT +10/20%) · Aura Cachée (malice -5/8%) · Chasse au Trésor (CHA +10/15%) | GFR + XLS |
| Ingénieur | Lentilles de Contact (PRÉC +10/15%) · Dynamique des Turbines (CRIT +5/10%) · Étude du Sonar Tremblant (étourdir 1/5%) | GFR + XLS |
| Corsaire | Objectif rapide (VIT ATQ +8/15%) · Passe rapide (VIT DEP +5/10%) · Double tir de flèche (double +1/2%) | GFR + XLS |
| Juge | Mort Ressuscitée (récup. PV 3/5%) · Assaut de la Mort (+ATQ 5/10% proc) · Prisonnier de la Mort (étourdir) | GFR + XLS (EN : Indomitable Strike / Deadly Onslaught…) |
| Clerc | Forte Croyance (SAG +10/15%) · Inspiration Divine (INT +10/15%) · Gentillesse Divine (PM +5/10%) | GFR + XLS |
| Barde | Esprit du Rock (CRIT MAG +5/10%) · Accordeur (coût mana -2/5%) · Star de la Pop (ESQ +4/8%) | GFR + XLS |
| Shaman | Sort de l'Esprit des Anciens (résist. +3/5%) · Totem des Esprits (DEG CRIT MAG +10/20%) · Pensée Concentrée (VIT INV +5/10%) | GFR + XLS |
| Sage | Prière Silencieuse (malice -5/8%) · Bénédiction du Roi Sage (résist. +3/5%) · Dépendance de l'Esprit Saint (DEG CRIT MAG subis -10/20%) | GFR + XLS |
| Magicien | Colère Tranquille (malice -5/8%) · Fusion des Éléments (CRIT MAG +5/10%) · Connaissances en Sorcellerie (INT +10/15%) | GFR + XLS |
| Illusionniste | Maître Avatar (ESQ +4/8%) · Joue avec le Temps (VIT INV +5/10%) · Contrôle de l'Illusion (mana -2/5%) | GFR + XLS |
| Démoniste | Rituel de l'Âme (DEG CRIT MAG +10/20%) · Œil du Démon (PRÉC +10/15%) · Malédictions Multiples (double +1/2%) | GFR + XLS |
| Luminary | Leçon de Magie (récup. PM) · Détermination Chaotique (ATQ MAG +5/10%) · Entaille Démoniaque (VIT INV sur CRIT MAG) | GFR + XLS |

**MISSING** : certificats des classes éveillées (aucune source),
certificats du Messie (la classe n'a pas de certifs propres documentés).

## 7. Lacunes à combler (hors wiki)

- Quêtes d'éveil manquantes : **Shaman (43/44), Sage (53/54),
  Corsaire (47/48), Luminary (57/58), Dragon Knight (55/56), Juge
  (60/61), Illusionniste (39/40)** — appariements connus via GFR
  `classes.md`, quêtes à prendre dans les données ou le client.
- Listes de sorts complètes par classe éveillée : pages GFR
  `competences-des-*-eveilles.md` existent pour 6 classes (corsaire,
  illusionniste, luminary, sage, shaman, chevalier-dragon) — non
  détaillées ici ; les 14 autres manquent.
- Prérequis éveillés absents des INI pour ids 25/26, 29/30, 39/40,
  43–46, 53–58 (`Vide|Vide`) — les quêtes existent en jeu mais ne sont
  pas inscrites dans la table.
- « Canonnier » (artillerie KR) : mentionné, jamais implémenté.
