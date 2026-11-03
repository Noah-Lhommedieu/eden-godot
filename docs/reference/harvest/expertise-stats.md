# Bonus d'expertise par palier de classe + modificateurs raciaux de stats — harvest

> Récolte du système « Class Ability Bonus » (EN) / « Expertise » (FR) :
> bonus de stats octroyés par palier de niveau de classe, croisé wiki
> FR/EN + classeur Vendetta + données natives (enchants `t_enchant.ini`,
> `S_ClassRankBuff.ini` via les parsers serveur). **Périmètre : lvl ≤ 90.**
>
> Statut : **CONFIRMED** = présent dans ≥1 source citée (données natives
> = plus forte preuve) ; **PARTIEL** = partiellement documenté ;
> **MISSING** = absent des sources récoltées.
>
> Sources :
> - `ENC-FR` = `eden_godot/godot_project/gamedata/data/db/lang/fr/t_enchant.ini`
>   (noms + gabarits des enchants de bonus de classe : ids 45492-45761,
>   47699-47717, 40835-41090, 46392-46406 — fichier:ligne cités)
> - `SRV` = `eden_server/server/src/Eden.Zone/Program.cs` (étape
>   « classrank », ~l.297-325) +
>   `eden_server/server/src/Eden.Core/Data/Parsers/ShopAndMiscParsers.cs`
>   (parser `S_ClassRankBuff.ini`, l.210-252) +
>   `eden_server/server/tests/Eden.Tests/GameDataTests.cs` (l.38-81,
>   valeurs décodées natives) — preuves `zone @0x8417c10` /
>   `mission @0x85c44xx` citées dans le code
> - `XLS` = `knowledge/wiki-tables/M/Class_Expertise.md` (classeur
>   Vendetta — seule source chiffrée complète, l.5-42)
> - `GFR` = `knowledge/guides-fr/pages/` (`expertise-de-classe.md`,
>   `classes.md`)
> - `WFR` = `knowledge/wiki-fr/pages/` (`Comparatif_des_compétences_raciales.md`,
>   pages de classes)
> - `WEN` = pages classes du wiki EN (via `docs/harvest/racial-skills.md`)
> - `INI` = `S_Class.ini`/`C_Class.ini` (305 defs classe×race, parser
>   `ClassStatsParser` — colonnes 13-21 = `StatPercents`)
> - `UI` = `eden_godot/godot_project/gamedata/ui_text_en_fr.tsv` +
>   `docs/harvest/refshots-classes.md` (`ClassAbilitybonuslogo.png`)

## 1. La mécanique (CONFIRMED native)

- **Règle** (`SRV`, désasm `lapis::CCharacter::RefreshJPAward` zone
  @0x8417c10) : pour **chaque classe possédée** (`player_classlist`),
  `rang = floor(min(niveau_de_classe, S_Parameter 578 = 100) × 0.2)` →
  paliers aux niveaux de classe **5, 10, 15, …** (lv5→rang 1, lv90→rang
  18, lv100→rang 20). Le rang indexe `S_ClassRankBuff.ini` (ligne =
  classId, 20 paliers × 6 champs, **id d'enchant = 6ᵉ champ**) dont
  l'enchant est appliqué en permanent (`EffectToLife` durée -2, flag
  0x1000). **Les bonus de toutes les classes montées se cumulent.**
- **Valeurs = totaux cumulés** au niveau considéré, pas des incréments
  à additionner (`GFR expertise-de-classe.md` — CONFIRMED).
- **UI** (`UI`) : panneau « Class Ability Bonus » en bas de la colonne
  gauche de la fenêtre [K] + bouton `« CE List »` ; tooltip violet
  « Class Ability Bonus Total » (ex. capture : HP 10108 / MP 3309 /
  ATK 7133 / Magic 9927 / G-Healing 407). Libellé FR = « Expertise »
  (`ui_text_en_fr.tsv` l.1586). Alimente les classements mensuels
  « aventurier » et « maître » / « Position Score de Classe » (GFR +
  `ui_text_en_fr.tsv` l.1587/2633).
- **Classes éveillées** : débloquées classe de base 80, **pas
  d'expertise avant le niveau de classe 85** — les seuls enchants
  éveillés existent aux paliers 17-20 (`ENC-FR` l.1356-1515 +
  `GFR classes.md` l.19 — CONFIRMED, cohérent : lv80 = rang 16 sans
  enchant dédié).
- **Messie (id 20)** : **aucun enchant de bonus** dans t_enchant.ini et
  absent de la feuille XLS → **pas d'expertise** (CONFIRMED par
  absence des deux sources).
- **⚠️ Collisions de nom — ne pas confondre** :
  - Les pages EN « `<Classe> Class Expertise` » (wiki-en
    `pages-index.tsv`, ex. `Warrior_Class_Expertise.html`) décrivent
    l'**arbre de talents de classe** (Knowledge Points, onglet
    « License knowledge » = « Compétences Bonus » FR) — mécanique
    différente, hors de ce doc.
  - Les objets `21179-21197` « Expertise de Classe Nv. 1-19 »
    (`t_item.ini` l.8581-8599) + enchants 54380+/50997-98 = la
    **relique** « Class Expertise Relic » (`t_textindex.ini` 4904) —
    item, pas le système.
  - « Expert en Hache/Gourdin… » = passifs raciaux d'arme →
    `docs/harvest/racial-skills.md`.

## 2. Bonus par classe — paires de stats (CONFIRMED par noms/gabarits d'enchants natifs)

Chaque classe de base donne **2 stats** (un enchant = 2 effets) ;
chaque classe éveillée donne **1 stat** (CONFIRMED `ENC-FR` + `XLS`).
Ids d'enchants : `« nom » 1-15 / 16 / 17-20` (gabarit texte EN non
localisé pour les 15 premiers paliers des 18 classes d'origine —
`ENC-FR` l.6614-6883 ; palier 16 = 47699-47717 l.9311-9329 ; paliers
17-20 + Juge + éveillés = 40835-41090 l.1260-1515, noms FR).

| id | FR | EN | Stat 1 | Stat 2 | Enchants (t1-15 / t16 / t17-20) | Statut |
|---|---|---|---|---|---|---|
| 1 | Guerrier | Warrior | PV | ATQ | 45492-506 / 47699 / 40835-38 | CONFIRMED |
| 7 | Chevalier | Knight | PV | PM (mana) | 45507-521 / 47705 / 40839-42 | CONFIRMED |
| 13 | Templier | Templar | PV | Guérison Gén. | 45522-536 / 47711 / 40843-46 | CONFIRMED |
| 18 | Chevalier Dragon | Dragon Knight | PV | ATQ MAG | 45537-551 / 47716 / 40847-50 | CONFIRMED |
| 5 | Voleur | Thief | PM (mana) | ATQ ⚠¹ | 45552-566 / 47703 / 40851-54 | CONFIRMED ⚠¹ |
| 9 | Assassin | Martial Artist | PV | ATQ | 45567-581 / 47707 / 40855-58 | CONFIRMED |
| 11 | Danselame | Blade Dancer | ATQ | Guérison Gén. | 45582-596 / 47709 / 40859-62 | CONFIRMED |
| 16 | Samouraï | Samurai | ATQ | ATQ MAG | 45597-611 / 47714 / 40863-66 | CONFIRMED |
| 3 | Chasseur | Hunter | ATQ | ATQ MAG | 45612-626 / 47701 / 40867-70 | CONFIRMED |
| 8 | Ingénieur | Engineer | PV | ATQ MAG | 45627-641 / 47706 / 40871-74 | CONFIRMED |
| 14 | Corsaire | Ranger | PM (mana) | ATQ ⚠¹ | 45642-656 / 47712 / 40875-78 | CONFIRMED ⚠¹ |
| 59 | Juge | Inquisitor | ATQ | ATQ MAG | 40911-40930 (20 paliers) | CONFIRMED |
| 4 | Clerc | Cleric | PV | Guérison Gén. | 45657-671 / 47702 / 40879-82 | CONFIRMED |
| 6 | Barde | Bard | PM (mana) | Guérison Gén. | 45672-686 / 47704 / 40883-86 | CONFIRMED |
| 12 | Shaman | Shaman | ATQ MAG | Guérison Gén. | 45687-701 / 47710 / 40887-90 | CONFIRMED |
| 17 | Sage | Sage | ATQ | Guérison Gén. | 45702-716 / 47715 / 40891-94 | CONFIRMED |
| 2 | Magicien | Magician | PM (mana) | ATQ MAG | 45717-731 / 47700 / 40895-98 | CONFIRMED |
| 10 | Illusionniste | Illusionist | PV | ATQ MAG | 45732-746 / 47708 / 40899-902 | CONFIRMED |
| 15 | Démoniste | Warlock | ATQ MAG | Guérison Gén. | 45747-761 / 47713 / 40903-06 | CONFIRMED |
| 19 | Chevalier du Chaos | Luminary | ATQ | ATQ MAG ⚠² | 46392-406 (t1-15) / 47717 / 40907-10 | CONFIRMED ⚠² |
| 20 | Messie | Mimic | — aucun — | — aucun — | aucun enchant | CONFIRMED (absence) |

⚠¹ **Divergence de version** : les gabarits anciens (enchants 45552-566
pour Voleur, 45642-656 pour Corsaire, + palier 16 : 47703 / 47712)
indiquent « **M-ATK** » en 2ᵉ stat ; les nouveaux (paliers 17-20 :
40851-54 / 40875-78) et la feuille `XLS` (tous niveaux) indiquent
« **ATK** ». À trancher sur les args réels de `S_Enchant.ini`
(non versionné — `server_us/Data/db/`). Hypothèse probable : stat
corrigée M-ATK→ATK par un patch, textes anciens obsolètes.

⚠² Luminary : le gabarit FR dit « **Limite d'ATQ** / **ATQ Max** +#10# »
(variante de rendu de « ATK » — pas « PV Max » ; `ENC-FR` 46392 l.7750,
47717 l.9329, 40907 l.1332). `XLS` confirme ATK + M-ATK.

### Classes éveillées — 1 stat, paliers 17-20 (lv 85/90/95/100)

| id | FR (ini/enchant) | EN (Vendetta) | Stat | Enchants t17-20 | Statut |
|---|---|---|---|---|---|
| 21 | Barbare | Berserker | ATQ | 40931-34 | CONFIRMED |
| 22 | Muraille | Shielder | PV | 40935-38 | CONFIRMED |
| 23 | Archélémentaliste | Arch-Elementalist | PM (mana) | 40939-42 | CONFIRMED |
| 24 | Astromagicien | Stormcaster (XLS : Gravity Manipulator) | ATQ MAG | 40943-46 | CONFIRMED |
| 25 | Maître des Bêtes | Trainer | ATQ | 40947-50 | CONFIRMED |
| 26 | Chasseur de Démons | Devil Hunter | ATQ MAG | 40951-54 | CONFIRMED |
| 27 | Disciplinaire | Adjudicator | Guérison Gén. | 40955-58 | CONFIRMED |
| 28 | Prêtre de l'Ombre | Equilibrian | PV | 40959-62 | CONFIRMED |
| 29 | Assassin | Assassin | ATQ | 40963-66 | CONFIRMED |
| 30 | Kageryu | Kage | PM (mana) | 40967-70 | CONFIRMED |
| 31 | Poète de Combat | Battlefield Poet | PM (mana) | 40971-74 | CONFIRMED |
| 32 | Fidèle de l'Essence | Life Worshipper | Guérison Gén. | 40975-78 | CONFIRMED |
| 33 | Bourreau | Executioner | PV | 40979-82 | CONFIRMED |
| 34 | Chevalier du Serment | Glacier Knight | PM (mana) | 40983-86 | CONFIRMED |
| 35 | Mécha Arès | Mecha Ares | PV | 40987-90 | CONFIRMED |
| 36 | Annihilateur | Annihilator | ATQ MAG | 40991-94 | CONFIRMED |
| 37 | Asura | Asura | PV | 40995-98 | CONFIRMED |
| 38 | Rakshasa | Rakshasa | ATQ | 40999-1002 | CONFIRMED |
| 39 | Illusionniste 2 | Demon Summoner (XLS) | PV | 41003-06 | CONFIRMED |
| 40 | Illusionniste 3 | Time Manipulator (XLS) | ATQ MAG | 41007-10 | CONFIRMED |
| 41 | Lanceur de Lames | Blade Acrobat | ATQ | 41011-14 | CONFIRMED |
| 42 | Danseur Élégant | Elegant Dancer | Guérison Gén. | 41015-18 | CONFIRMED |
| 43 | Shaman 2 | Druid | ATQ MAG | 41019-22 | CONFIRMED |
| 44 | Shaman 3 | Totem Master | Guérison Gén. | 41023-26 | CONFIRMED |
| 45 | Chevalier déchu | Dawnbreaker / Blood Knight (XLS) | PV | 41027-30 | CONFIRMED |
| 46 | Paladin | Paladin | Guérison Gén. | 41031-34 | CONFIRMED |
| 47 | Corsaire 2 | Lethal Arrow | PM (mana) | 41035-38 | CONFIRMED |
| 48 | Corsaire 3 | Celestial Arrow | ATQ | 41039-42 | CONFIRMED |
| 49 | Dompteur de Démon | Demon Tamer | ATQ MAG | 41043-46 | CONFIRMED |
| 50 | Mage Martial | Conjurer | Guérison Gén. | 41047-50 | CONFIRMED |
| 51 | Maître-lame | Blade Master | ATQ | 41051-54 | CONFIRMED |
| 52 | Maître Escrimeur | Master Fencer | ATQ MAG | 41055-58 | CONFIRMED |
| 53 | Sage 2 | Holy Sage | ATQ | 41059-62 | CONFIRMED |
| 54 | Sage 3 | Angel of Justice | Guérison Gén. | 41063-66 | CONFIRMED |
| 55 | Chevalier Dragon 2 | Dragon Soul | PV | 41067-70 | CONFIRMED |
| 56 | Chevalier Dragon 3 | Dragon Emperor | ATQ MAG | 41071-74 | CONFIRMED |
| 57 | Chevalier du Chaos 2 | Darkness Blade | ATQ | 41075-78 | CONFIRMED |
| 58 | Chevalier du Chaos 3 | Holy Blade | ATQ MAG | 41079-82 | CONFIRMED |
| 60 | Juge 2 | Reaver | ATQ | 41083-86 | CONFIRMED |
| 61 | Juge 3 | Rifleteer | ATQ MAG | 41087-90 | CONFIRMED |

## 3. Valeurs par palier — classes de base (XLS, croisé enchants)

Valeurs = **total au niveau de classe indiqué** (rang = lv/5). Toutes
les classes partagent **8 pistes de croissance** — le tableau des
correspondances est en §2. Source unique chiffrée : `XLS`
(`Class_Expertise.md` l.5-23). Contrôle natif : palier 1 décodé =
Guerrier PV+41/ATQ+33 et Magicien ATQ MAG+47/PM+29 (`SRV`
`GameDataTests.cs` l.60-80) — identique à `XLS` → les pistes sont les
valeurs natives, pas un rebalance Vendetta (CONFIRMED aux paliers
testés, PARTIEL ailleurs sauf preuve `S_Enchant.ini`).

| Niv classe | PV-haut | PV-bas | PM-haut | PM-bas | ATQ/ATQMAG-haut | ATQ/ATQMAG-bas | Guér-haut | Guér-bas |
|---|---|---|---|---|---|---|---|---|
| 5 | 41 | 30 | 58 | 29 | 47 | 33 | 13 | 7 |
| 10 | 74 | 46 | 98 | 49 | 78 | 48 | 14 | 10 |
| 15 | 107 | 62 | 138 | 69 | 109 | 63 | 15 | 13 |
| 20 | 140 | 78 | 178 | 89 | 140 | 78 | 16 | 16 |
| 25 | 173 | 94 | 218 | 109 | 171 | 94 | 17 | 19 |
| 30 | 206 | 110 | 258 | 129 | 202 | 110 | 18 | 22 |
| 35 | 239 | 126 | 298 | 149 | 233 | 126 | 19 | 25 |
| 40 | 272 | 142 | 338 | 169 | 264 | 142 | 20 | 28 |
| 45 | 305 | 158 | 378 | 189 | 295 | 158 | 21 | 31 |
| 50 | 371 | 190 | 458 | 229 | 357 | 190 | 22 | 37 |
| 55 | 404 | 206 | 498 | 249 | 388 | 206 | 23 | 40 |
| 60 | 503 | 254 | 618 | 309 | 481 | 254 | 24 | 49 |
| 65 | 536 | 270 | 658 | 329 | 512 | 270 | 27 | 52 |
| 70 | 635 | 318 | 778 | 389 | 605 | 318 | 28 | 61 |
| 75 | 668 | 334 | 818 | 409 | 636 | 334 | 31 | 64 |
| 80 | 735 | 367 | 900 | 450 | 700 | 367 | 32 | 70 |
| 85 | 772 | 385 | 945 | 473 | 735 | 385 | 35 | 74 |
| 90 | 849 | 424 | 1040 | 520 | 809 | 405 | 41 | 81 |

Correspondance piste ↔ classe (récap. §2) :
- **PV-haut** = Guerrier, Chevalier, Templier, Chevalier Dragon (tous
  les tanks) ; **PV-bas** = Assassin(9), Ingénieur, Illusionniste, Clerc.
- **PM-haut** = Voleur, Corsaire (PM ⚠¹ — cellule lv5 `XLS` lit 68 au
  lieu de 58, écart isolé, typo probable) ; **PM-bas** = Chevalier,
  Barde, Magicien.
- **ATQ/ATQMAG-haut** = Assassin(9), Danselame, Samouraï (ATQ), Chasseur
  (ATQ), Juge (ATQ), Ingénieur, Magicien, Illusionniste, Démoniste,
  Chevalier du Chaos (ATQ MAG) ; **bas** = Guerrier, Samouraï (ATQ MAG),
  Chasseur (ATQ MAG), Juge (ATQ MAG), Chevalier Dragon (ATQ MAG), Sage
  (ATQ), Voleur (ATQ), Shaman (ATQ MAG), Luminary (ATQ).
- **Guér-haut** = Templier, Danselame, Démoniste ; **Guér-bas** = Clerc,
  Barde, Shaman, Sage.
- Classes Vendetta-only (hors périmètre, non présentes dans
  `t_class.ini` ids 1-61) : **Agent** (PM-haut + ATQ-bas) et
  **Wyvern Lord** (PV-bas + ATQ MAG-haut) — présentes dans `XLS` et
  `R/Bear.md`, à ignorer.
- La feuille `XLS` continue à 130 (paliers 19-26 — lv95+, **hors
  périmètre**) : `M/Class_Expertise.md` l.24-31.

## 4. Valeurs par palier — classes éveillées (XLS, lv 85/90 in-scope)

Paliers 17-20 = lv 85/90/95/100 ; seuls 85/90 sont dans le périmètre
(95+ : `XLS` l.35-42, hors périmètre). Une seule stat par classe (§2).

| Niv classe | ATQ-bas | ATQ-mil | ATQ-haut | PV-bas | PV-haut | PM-bas | PM-haut | ATQMAG-mil | ATQMAG-bas | Guér-bas | Guér-haut |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 85 | 736 | 1470 | 1890 | 770 | 1544 | 736 | 946 | 1470 | 736 | 74 | 148 |
| 90 | 810 | 1618 | 2080 | 848 | 1698 | 810 | 1040 | 1618 | 810 | 82 | 162 |

Correspondance (récap. §2) :
- **ATQ-haut** : Assassin(29) ; **ATQ-mil** : Rakshasa, Lanceur de Lames,
  Maître-lame, Maître des Bêtes, Pourfendeur(60) ; **ATQ-bas** :
  Barbare, Corsaire 3 (Celestial Arrow), Sage 2 (Holy Sage), Chevalier
  du Chaos 2 (Darkness Blade).
- **PV-haut** : Muraille, Bourreau, Chevalier déchu, Chevalier Dragon 2
  (Dragon Soul) ; **PV-bas** : Prêtre de l'Ombre, Mécha Arès, Asura,
  Illusionniste 2 (Demon Summoner).
- **PM-haut** : Chevalier du Serment, Poète de Combat, Corsaire 2
  (Lethal Arrow), Archélémentaliste ; **PM-bas** : Kageryu.
- **ATQMAG-mil** : Astromagicien, Illusionniste 3 (Time Manipulator),
  Dompteur de Démon, Chevalier du Chaos 3 (Holy Blade), Annihilateur ;
  **ATQMAG-bas** : Chasseur de Démons, Maître Escrimeur, Juge 3
  (Rifleteer), Chevalier Dragon 3 (Dragon Emperor), Shaman 2 (Druid).
- **Guér-haut** : Disciplinaire, Fidèle de l'Essence, Shaman 3 (Totem
  Master), Sage 3 (Angel of Justice), Paladin ; **Guér-bas** : Danseur
  Élégant, Mage Martial (Conjurer).
- Vendetta-only : Special Agent (PM-bas), Gunslinger (ATQ-haut) —
  `XLS` l.33-42, hors périmètre.
- Les valeurs éveillées ≈ **2× la piste de base au même niveau** (ex.
  ATQ-bas : 368→736 au lv85) — une stat unique à taux doublé.

## 5. Modificateurs de stats par race × classe

Multiplicateurs % des stats de base (100 = neutre), appliqués selon la
race du personnage **par classe** — visibles en « Class Stat Bonus »
de l'onglet Class News (`UI` `ClassePanel-ClassNews.png` : `HP 103% ·
MP 109% · STR 95% · AGI 95% · INT 113% · WIS 135% · LCK 105%` pour un
Clerc Humain — recoupe la ligne Humain/Clerc ci-dessous). Source wiki :
tables « Caractéristiques raciales » des pages classes `WFR` + pages
EN (déjà harvestées dans `docs/harvest/racial-skills.md` §1 — valeurs
reprises, divergences EN/FR notées). Source machine : `S_Class.ini`
cols 13-21 → `ClassStatsParser.StatPercents` (9 cellules, 305 lignes
classe×race — `INI`, autorité serveur).

Format cellule : `PV/PM/FOR/AGI/INT/SAG/CHA`.

### Défense

| Classe | Humain | Zumi | Anuran | Ursun | Ézelin |
|---|---|---|---|---|---|
| Guerrier (1) | 123/69/125/114/100/100/104 | 116/79/122/117/100/100/104 | 127/64/120/116/100/100/107 | 131/65/128/112/100/100/103 | 111/85/119/116/100/100/108 |
| Chevalier (7) | 119/77/118/118/100/100/107 | 112/87/115/121/100/100/107 | 123/72/113/120/100/100/110 | 127/73/121/116/100/100/106 | 108/93/112/120/100/100/111 |
| Templier (13) | 121/73/122/113/100/108/100 | 114/83/119/116/100/108/100 | 125/68/117/114/100/112/100 | 129/69/125/112/100/106/100 | 109/89/116/117/100/110/100 |
| Chevalier Dragon (18) | 120/90/100/118/100/117/108 | 111/99/100/117/100/118/108 | 115/95/100/116/100/115/112 | 118/92/100/116/100/112/115 | 110/100/100/120/100/113/100 |

### DPS corps à corps

| Classe | Humain | Zumi | Anuran | Ursun | Ézelin |
|---|---|---|---|---|---|
| Voleur (5) | 109/97/112/124/100/100/107 | 105/100/110/126/100/100/107 | 111/98/109/124/100/100/110 | 116/93/115/121/100/100/107 | 100/113/110/125/100/100/108 |
| Assassin = Martial Artist (9) | 117/81/123/115/100/100/105 | 110/91/121/117/100/100/105 | 121/76/120/115/100/100/108 | 125/77/126/112/100/100/105 | 106/97/121/116/100/100/106 |
| Danselame (11) | 115/85/115/121/100/100/107 | 108/95/113/123/100/100/107 | 120/79/112/121/100/100/110 | 123/81/118/119/100/100/106 | 105/101/113/122/100/100/108 |
| Samouraï (16) | 119/76/117/120/100/100/106 | 112/86/112/125/100/100/106 | 124/70/114/119/100/100/110 | 128/72/120/117/100/100/106 | 108/92/115/121/100/100/107 |

### DPS à distance

| Classe | Humain | Zumi | Anuran | Ursun | Ézelin |
|---|---|---|---|---|---|
| Chasseur (3) | 105/105/107/115/100/100/121 | 98/115/105/115/100/100/123 | 110/99/107/119/100/100/117 | 111/101/110/115/100/100/118 | 97/121/105/116/100/100/122 |
| Ingénieur (8) | 107/101/107/112/100/100/124 | 100/111/105/112/100/100/126 | 112/95/107/116/100/100/120 | 114/97/110/112/100/100/121 | 98/117/105/113/100/100/125 |
| Corsaire = Ranger (14) | 113/89/112/112/100/100/119 | 106/99/110/112/100/100/121 | 118/83/112/116/100/100/115 | 120/85/115/112/100/100/116 | 103/105/110/112/100/100/121 |
| Juge = Inquisitor (59) | 115/85/114/120/92/92/127 | idem — stats identiques toutes races (WEN) | idem | idem | idem |

### Soins / Soutien

| Classe | Humain | Zumi | Anuran | Ursun | Ézelin |
|---|---|---|---|---|---|
| Clerc (4) | 103/109/100/100/113/125/105 | 96/119/100/100/113/122/108 | 108/103/95/95/113/137/103 ⚠³ | 109/105/100/100/110/123/110 | 95/125/100/100/113/125/105 |
| Barde (6) | 111/93/100/114/112/117/100 | 104/103/100/117/112/114/100 | 111/93/95/112/117/126/100 ⚠⁴ | 118/89/100/117/109/117/100 ⚠⁵ | 101/109/100/114/112/117/100 |
| Shaman (12) | 101/113/100/100/116/122/105 | 95/123/100/100/116/119/108 | 110/102/95/95/116/129/107 ⚠⁶ | 107/109/100/100/113/124/106 | 93/129/100/100/116/122/105 |
| Sage (17) | 113/89/100/114/110/119/100 | 106/99/100/116/110/117/100 | 118/86/100/114/109/120/100 | 120/85/100/113/108/122/100 | 103/105/100/114/110/119/100 |

⚠³ Clerc Anuran : stats `WEN` ; le comparatif `WFR` dit 108/103/100/100/113/127/103.
⚠⁴ Barde Anuran : `WEN` ; `WFR` : 116/87/100/114/110/119/100.
⚠⁵ Barde Ursun : `WEN` ; `WFR` : 118/89/100/100/110/123/110.
⚠⁶ Shaman Anuran : `WEN` ; `WFR` : 105/107/100/100/116/124/102.
(Cellules vides `WFR` — Samouraï Anuran, Corsaire Zumi, Sage
Zumi/Ursun/Ézelin — complétées via `WEN`, voir `racial-skills.md` §1.)

### DPS magique

| Classe | Humain | Zumi | Anuran | Ursun | Ézelin |
|---|---|---|---|---|---|
| Magicien (2) | 97/121/100/106/125/100/112 | 91/131/100/109/122/100/112 ⚠⁷ | 101/114/100/106/128/100/109 | 103/117/100/106/122/100/115 | 90/137/100/106/125/100/112 |
| Illusionniste (10) | 95/125/100/108/120/100/115 | 89/135/100/110/117/100/115 | 99/118/100/108/123/100/112 | 100/121/100/110/117/100/116 | 89/141/100/108/120/100/115 |
| Démoniste (15) | 99/117/100/112/122/100/109 | 93/127/100/115/119/100/109 | 103/110/100/112/125/100/106 | 105/113/100/113/119/100/111 | 92/133/100/112/122/100/109 |
| Chevalier du Chaos = Luminary (19) | 101/113/100/110/118/100/115 | 97/123/100/113/115/100/115 | 105/106/100/110/121/100/112 | 107/109/100/112/115/100/116 | 93/129/100/110/118/100/115 |

⚠⁷ Magicien Zumi : `WEN` ; `WFR` : 91/131/100/106/128/100/109.

Statut : **CONFIRMED** pour les 21 classes ci-dessus (WFR + WEN +
`ClassNews` UI pour Clerc/Humain). Correspondance `INI` : ces 7 %
= 7 des 9 cellules `StatPercents` de `S_Class.ini` (ordre HP, MP,
STR, INT, CON, AGI, WIS, LUK, divers — mapping exact des 2 colonnes
en trop à vérifier sur les données, le wiki n'en montre que 7).

**MISSING (wiki)** :
- **Messie (20)** : aucune table raciale documentée — `S_Class.ini`
  a bien les 5 lignes raciales (201-220/301-320/401-420/501-520…) →
  valeurs à lire en données, absentes des wikis.
- **Classes éveillées (21-58, 60-61)** : aucune table raciale dans les
  wikis (`racial-skills.md` note aussi les cases vides Vendetta —
  héritage de la classe de base plausible, non confirmé). `S_Class.ini`
  contient les 5 variantes par classe → source à exploiter.
- **Noms de race ↔ ids** : lignes `S_Class.ini` 1-61 (race 1), 101-161,
  201-261, 301-361, 401-461 (`ClassStatsParser` l.12-16).

## 6. Lacunes / à vérifier

- **Valeurs natives hors palier 1** : seule la feuille `XLS` les donne
  (cohérente avec les 2 enchants décodés). Vérité = args de
  `S_Enchant.ini` (ids cités §2) + seuils de `S_ClassRankBuff.ini` —
  fichiers non versionnés (`server_us/Data/db/`), décodables via
  `tools/mycrypt.py` + `ShopAndMiscParsers`.
- **Voleur/Corsaire** : trancher M-ATK (t1-16 textes) vs ATK (t17-20 +
  XLS) sur les args réels des enchants — voir ⚠¹ §2.
- **Champ 2-5 des paliers** `S_ClassRankBuff` : non consommés serveur
  (métadonnées d'affichage client `C_ClassRankBuff.ini` — dont
  `reqLevel`, cf. `export_web_data.py` l.1335).
- **Cap** : `S_Parameter 578` = 100 natif ; à niveau de classe 90 on
  plafonne à rang 18 → les enchants t19/20 existent (40835-40910) mais
  restent inaccessibles en jeu de base ≤90 (données quand même
  présentes, utiles si cap relevé).
- **Messie** : aucune expertise — à confirmer en jeu (le bonus total
  ignore la classe).
- **「CE List」** : panneau liste des bonus par classe (probable
  « Class Expertise List ») — contenu non capturé dans ref-shots.

> *Doc compilé le 2026 — croisement `XLS` × `ENC-FR` × `SRV` ; le
> layout de colonnes de la feuille Vendetta (en-tête de bloc non
> conservé par la conversion CSV→md) a été re-déduit et validé à 100 %
> par les noms d'enchants natifs pour les 40 classes éveillées et par
> 2 enchants décodés pour les classes de base.*
