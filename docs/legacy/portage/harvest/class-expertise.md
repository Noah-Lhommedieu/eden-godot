# Harvest — « Class Ability Bonus » / Expertise de classe

> Doc de synthèse pour réimplémenter le **bonus passif de stats par niveau
> de classe** (aussi appelé « expertise de classe » côté FR, *Class
> Ability Bonus* côté client US).
>
> **Source de vérité principale** : la table du serveur natif oracle
> `eden_server/server_us/Data/db_decoded/S_ClassRankBuff.ini` (lecture
> seule) — 5 colonnes de stats (PV / PM / ATQ PHY / ATQ MAG / Soin) ×
> paliers de niveau de classe. Recoupée avec le classeur Vendetta
> `knowledge/wiki-tables/M/Class_Expertise.md`, le wiki EN
> `knowledge/raw/wiki-en/` et les ref-shots `ref-shots/`.
> Périmètre : **lvl ≤ 90**. Données lvl 95+ signalées `[HORS]`.

---

## 1. Terminologie — trois systèmes distincts (ne pas confondre)

| Système | Nom UI (EN) | Mécanique | Statut |
|---|---|---|---|
| **Bonus de classe / expertise de classe** | « Class Ability Bonus » | Bonus **automatique** de stats par palier de niveau de classe, cumulatif entre toutes les classes | **sujet de ce doc** |
| **Certificats de classe** | « Certificates » | Bonus **socketables** dans une grille (3 par classe, débloqués niv. classe 10/20/30, améliorés 40/50/60 ; jusqu'à 7 posés, combos possibles) | système séparé — `knowledge/guides-fr/pages/certificats-de-classe.md`, `knowledge/wiki-tables/M/Class_Certificates.md` |
| **Talents / Connaissances** | « Class Talent » / « License knowledge » / « Class Expertise » (wiki EN) | Arbre de talents où l'on **dépense des Knowledge Points** (1 KP par niveau de classe, non transférables entre classes ; coûts 1 pt niv. 1-5, 2 pts niv. 6-15, 3 pts niv. 16-20) | système séparé — `knowledge/raw/wiki-en/Class_Expertise.html` + pages `*_Class_Expertise.html` (détaillent l'arbre KP, pas le bonus) |

⚠️ **Piège de vocabulaire** : le wiki EN appelle « Class Expertise »
l'arbre de talents KP, tandis que le guide FR appelle « expertise de
classe » le bonus automatique (= le « Class Ability Bonus » de l'UI). Le
bouton `« CE List »` du panneau = probablement *Class Expertise List*
(la liste du bonus automatique). Sources :
`knowledge/guides-fr/pages/expertise-de-classe.md` (FR, bonus auto),
`knowledge/raw/wiki-en/Class_Expertise.html` (EN, arbre KP),
`docs/harvest/refshots-classes.md` § ClassAbilitybonuslogo.

## 2. La mécanique — CONFIRMÉ

- Chaque classe possède son propre « rang » (niveau de classe, XP de
  classe **séparée** de l'XP personnage ; le niveau de classe ne peut pas
  dépasser le niveau du personnage — `knowledge/raw/wiki-en/Class.html` /
  `Class_Branches.html`).
- Le bonus est découpé en **paliers de 5 niveaux de classe** : les
  valeurs affichées pour le palier `lvl N` couvrent les niveaux
  **[N, N+4]** (CONFIRMÉ : table Hunter du wiki EN « 5–9 → +47/+33,
  10–14 → +78/+48… », identique aux lignes 5/10/15… de
  `S_ClassRankBuff.ini`).
- **Aucun bonus aux niveaux de classe 1-4** (CONFIRMÉ :
  `knowledge/raw/wiki-en/Hunter.html` « 1–4 No bonus »).
- **Le bonus s'applique quelle que soit la classe active et se cumule entre
  toutes les classes montées** (CONFIRMÉ : `Hunter.html` — « These
  bonuses apply regardless of the character's current class, and stack
  with bonuses from other classes » ; cohérent avec le tooltip
  « Class Ability Bonus **Total** » du ref-shot).
- Chaque **classe de base donne exactement 2 stats** parmi 5 :
  **PV, PM, ATQ (P-ATK), ATQ M. (M-ATK), Soin (G-HEAL)** (CONFIRMÉ :
  `S_ClassRankBuff.ini`, colonnes 1-5). Ce sont les 5 stats du tooltip UI.
- Chaque **classe éveillée donne exactement 1 stat**, et uniquement sur
  4 paliers (bandes 17-20 = lvl 85/90/95/100) (CONFIRMÉ :
  `S_ClassRankBuff.ini` lignes ids 21-58,60,61).
- Les valeurs sont des **cumuls** (total au niveau considéré, pas à
  additionner entre paliers) — `knowledge/guides-fr/pages/expertise-de-classe.md`.
- L'expertise (le score de classe) compte dans deux **classements
  mensuels** : aventurier (score classe + succès) et maître (score
  classe) — `knowledge/guides-fr/pages/expertise-de-classe.md`.
- **Mimic (id 20) : aucune ligne dans `S_ClassRankBuff`** → vraisemblablement
  aucun bonus de classe (CONFIRMÉ par absence de ligne, à revalider en jeu).

## 3. UI — « Class Ability Bonus » (logo)

- Emplacement : fenêtre **Classe `[K]`**, en **bas de la colonne de
  gauche** (sous la liste des classes) — ref-shots `ClassePanel.png`,
  `ClassePanel-ClassTalent&Certificat.png` + guide FR (« interface de
  classe [K], en bas de la colonne de gauche »).
- Éléments : **icône soleil/médaille or** (le logo —
  `ref-shots/ClassAbilitybonuslogo.png`), bouton `« ? »` d'aide,
  bouton `« CE List »`.
- Survol du panneau → **tooltip violet** `« Class Ability Bonus Total »`
  listant les 5 totaux cumulés : `HP`, `MP`, `ATK`, `Magic`,
  `G-Healing` (ex. capturé : HP 10108 / MP 3309 / ATK 7133 / Magic 9927 /
  G-Healing 407 points).
- `« CE List »` : contenu non capturé — probablement la liste des bonus
  par classe (**MISSING** — à vérifier en jeu).
- Détail géométrie/verbatim : `docs/harvest/refshots-classes.md`
  (§ ClassePanel + § ClassAbilitybonuslogo).

## 4. Valeurs — classes de base (NATIF CONFIRMÉ, `S_ClassRankBuff.ini`)

Les valeurs ne dépendent que de la **stat et de sa grandeur** : il n'y a
que **8 séries** possibles. Une classe = une « stat forte » + une
« stat faible » (ou deux fortes pour certaines).

### 4.1 Séries par stat (paliers = début de bande, valable lvl N→N+4)

| Palier classe | PV+ | PV− | PM+ | PM− | ATQ+ | ATQ− | ATQM+ | ATQM− | Soin+ | Soin− |
|---|---|---|---|---|---|---|---|---|---|---|
| 5  | 41 | 30 | 58 | 29 | 47 | 33 | 47 | 33 | 13 | 7 |
| 10 | 74 | 46 | 98 | 49 | 78 | 48 | 78 | 48 | 14 | 10 |
| 15 | 107 | 62 | 138 | 69 | 109 | 63 | 109 | 63 | 15 | 13 |
| 20 | 140 | 78 | 178 | 89 | 140 | 78 | 140 | 78 | 16 | 16 |
| 25 | 173 | 94 | 218 | 109 | 171 | 93 | 171 | 93 | 17 | 19 |
| 30 | 206 | 110 | 258 | 129 | 202 | 108 | 202 | 108 | 18 | 22 |
| 35 | 239 | 126 | 298 | 149 | 233 | 123 | 233 | 123 | 19 | 25 |
| 40 | 272 | 142 | 338 | 169 | 264 | 138 | 264 | 138 | 20 | 28 |
| 45 | 305 | 158 | 378 | 189 | 295 | 153 | 295 | 153 | 21 | 31 |
| 50 | 371 | 190 | 458 | 229 | 357 | 183 | 357 | 183 | 22 | 37 |
| 55 | 404 | 206 | 498 | 249 | 388 | 198 | 388 | 198 | 23 | 40 |
| 60 | 503 | 254 | 618 | 309 | 481 | 243 | 481 | 243 | 24 | 49 |
| 65 | 536 | 270 | 658 | 329 | 512 | 258 | 512 | 258 | 27 | 52 |
| 70 | 635 | 318 | 778 | 389 | 605 | 303 | 605 | 303 | 28 | 61 |
| 75 | 668 | 334 | 818 | 409 | 636 | 318 | 636 | 318 | 31 | 64 |
| 80 | 735 | 367 | 900 | 450 | 700 | 350 | 700 | 350 | 32 | 70 |
| 85 | 772 | 385 | 945 | 473 | 735 | 368 | 735 | 368 | 35 | 74 |
| 90 | 849 | 424 | 1040 | 520 | 809 | 405 | 809 | 405 | 37 | 81 |
| 95 `[HORS]` | 891 | 445 | 1092 | 546 | 849 | 425 | 849 | 425 | 41 | 85 |
| 100 `[HORS]` | 980 | 490 | 1201 | 601 | 934 | 468 | 934 | 468 | 47 | 94 |

> « + » = série forte (47/58/41/13 au palier 5) ; « − » = série faible
> (33/29/30/7 au palier 5). Source : `S_ClassRankBuff.ini` — les séries
> ATQ+ et ATQM+ sont numériquement identiques (47→934), idem ATQ− et
> ATQM− (33→468) ; la table ci-dessus les garde pour lisibilité.

### 4.2 Matrice classe → stats

Source : `S_ClassRankBuff.ini` (col.2 = id de classe, croisé avec
`T_Class.ini` / `Class.ini` — noms US officiels). Le classeur Vendetta
(`knowledge/wiki-tables/M/Class_Expertise.md`) donne **les mêmes paires
et les mêmes valeurs** pour toutes les classes communes (sauf Ranger, voir §6).

| ID | Classe (US) | Branche | Stat 1 | Stat 2 |
|---|---|---|---|---|
| 1 | Warrior / Guerrier | Défense | PV+ | ATQ− |
| 7 | Knight / Chevalier | Défense | PV+ | PM− |
| 13 | Templar / Templier | Défense | PV+ | Soin+ |
| 18 | Dragon Knight / Chevalier-dragon | Défense | PV+ | ATQM− |
| 5 | Thief / Voleur | DPS mêlée | PM+ | ATQ− |
| 9 | Martial Artist / Artiste martial | DPS mêlée | PV− | ATQ+ |
| 11 | Blade Dancer / Danselame | DPS mêlée | ATQ+ | Soin+ |
| 16 | Samurai / Samouraï | DPS mêlée | ATQ+ | ATQM− |
| 3 | Hunter / Chasseur | DPS distance | ATQ+ | ATQM− |
| 8 | Engineer / Ingénieur | DPS distance | PV− | ATQM+ |
| 14 | Ranger / Rôdeur | DPS distance | PM+ | ATQ− |
| 59 | Inquisitor / Inquisiteur | DPS distance | ATQ+ | ATQM− |
| — | Agent *(Vendetta uniquement)* | DPS distance | PM+ | ATQ− |
| 4 | Cleric / Clerc | Soin & support | PV− | Soin− |
| 6 | Bard / Barde | Soin & support | PM− | Soin− |
| 12 | Shaman / Chamane | Soin & support | ATQM− | Soin− |
| 17 | Sage | Soin & support | ATQ− | Soin− |
| 2 | Magician / Magicien | DPS magique | PM− | ATQM+ |
| 10 | Illusionist / Illusionniste | DPS magique | PV− | ATQM+ |
| 15 | Warlock / Démoniste | DPS magique | ATQM+ | Soin+ |
| 19 | Luminary / Luminaire | DPS magique | ATQ− | ATQM+ |
| — | Wyvern Lord *(Vendetta uniquement)* | DPS magique | PV− | ATQM+ |
| 20 | Mimic / Mimet | (spécial) | — aucun — | — aucun — |

Vérifications croisées (CONFIRMÉ) :
- `knowledge/raw/wiki-en/Hunter.html` — table « class ability bonuses »
  officielle : ATQ+47/ATQM+33 au palier 5 … ATQ+700/ATQM+350 au palier
  80 → identique à la ligne id 3 native, au point près.
- `knowledge/wiki-tables/M/Class_Expertise.md` — chaque paire de
  colonnes Vendetta = la paire native correspondante (toutes valeurs
  lvl ≤ 80 identiques).
- `ref-shots/ClassAbilitybonuslogo.png` — les 5 stats du tooltip =
  les 5 colonnes de la table native.

## 5. Classes éveillées (ids natifs 21-61)

- L'oracle US contient les classes éveillées en ids **21-61**
  (`T_Class.ini`) ; certaines n'ont que des noms provisoires
  (« Ranger 2 », « Sage 3 », « Dragon Knight 2 »…). Correspondance avec
  les noms Vendetta via stats + branche (`S_ClassRankBuff.ini` col.2 → id).
- **1 seule stat par classe éveillée** (vs 2 pour une classe de base),
  sur les **paliers 85 / 90** (+ 95/100 `[HORS]`). Les paliers natifs
  sont les bandes 17-20 de la grille commune → couvre les niveaux de
  classe éveillée 85-89, 90-94, 95-99, 100-104.
- L'éveillée est une **classe à part** (son propre niveau) : le bonus de
  la classe de base reste acquis en plus (cumul global).

| Classe de base | Éveillée (oracle US) | Nom Vendetta | Stat | 85 | 90 | 95 `[HORS]` | 100 `[HORS]` |
|---|---|---|---|---|---|---|---|
| Warrior | Berserker | Berserker | ATQ | 736 | 810 | 850 | 936 |
| Warrior | Shielder | Shielder | PV | 1544 | 1698 | 1782 | 1960 |
| Knight | Executioner | Executioner | PV | 1544 | 1698 | 1782 | 1960 |
| Knight | Glacier Knight | Glacier Knight | PM | 946 | 1040 | 1092 | 1202 |
| Templar | Blood Knight | Bloodknight | PV | 1544 | 1698 | 1782 | 1960 |
| Templar | Paladin | Paladin | Soin | **74** ⚠ | **82** ⚠ | 86 | 94 |
| Dragon Knight | Dragon Knight 2 | Dragon Soul | PV | 1544 | 1698 | 1782 | 1960 |
| Dragon Knight | Dragon Knight 3 | Dragon Emperor | ATQM | 736* | 810 | 850 | 936 |
| Thief | Assassin | Assassin | ATQ | **1470** ⚠ | **1618** ⚠ | 1698 | 1868 |
| Thief | Kage | Kage | PM | **946** ⚠ | **1040** ⚠ | 1092 | 1202 |
| Martial Artist | Asura | Asura | PV | 770 | 848 | 890 | 980 |
| Martial Artist | Rakshasa | Rakshasa | ATQ | 1470 | 1618 | 1698 | 1868 |
| Blade Dancer | Blade Acrobat | Blade Acrobat | ATQ | 1470 | 1618 | 1698 | 1868 |
| Blade Dancer | Elegant Dancer | Elegant Dancer | Soin | 74 | 82 | 86 | 94 |
| Samurai | Blade Master | Blade Master | ATQ | 1470 | 1618 | 1698 | 1868 |
| Samurai | Master Fencer | Master Fencer | ATQM | 736 | 810 | 850 | 936 |
| Hunter | Trainer | Trainer | ATQ | 1470 | 1618 | 1698 | 1868 |
| Hunter | Devil Hunter | Devil Hunter | ATQM | 736 | 810 | 850 | 936 |
| Engineer | Mecha Ares | Mecha Ares | PV | 770 | 848 | 890 | 980 |
| Engineer | Annihilator | Annihilator | ATQM | 1470 | 1618 | 1698 | 1868 |
| Ranger | Ranger 3 (id47) | Lethal Arrow ? | PM | 1890 | 2080 | 2184 | 2402 |
| Ranger | Ranger 2 (id48) | Celestial Arrow ? | ATQ | 736 | 810 | 850 | 936 |
| Inquisitor | Reaver | Reaver | ATQ | 1470 | 1618 | 1698 | 1868 |
| Inquisitor | Rifleteer | Rifleteer | ATQM | 736 | 810 | 850 | 936 |
| Cleric | Adjudicator | Adjudicator | Soin | 148 | 162 | 170 | 188 |
| Cleric | Equilibrian | Equilibrian | PV | 770 | 848 | 890 | 980 |
| Bard | Battlefield Poet | Battlefield Poet | PM | 946 | 1040 | 1092 | 1202 |
| Bard | Life Worshipper | Life Worshipper | Soin | 148 | 162 | 170 | 188 |
| Shaman | Druid | Druid | ATQM | 736 | 810 | 850 | 936 |
| Shaman | Archimage (id44) | Totem Master | Soin | 148 | 162 | 170 | 188 |
| Sage | Sage 2 (id53) | Holy Sage ? | ATQ | 736 | 810 | 850 | 936 |
| Sage | Sage 3 (id54) | Angel of Justice ? | Soin | 148 | 162 | 170 | 188 |
| Magician | Arch-Elementalist | Arch-Elementalist | PM | 946 | 1040 | 1092 | 1202 |
| Magician | Gravity Manipulator | Gravity Manipulator | ATQM | 1470 | 1618 | 1698 | 1868 |
| Illusionist | Demon Tamer (id39) | Demon Summoner | PV | 770 | 848 | 890 | 980 |
| Illusionist | Chronologist (id40) | Time Manipulator | ATQM | 1470 | 1618 | 1698 | 1868 |
| Warlock | Demon Tamer (id49) | Demon Tamer | ATQM | 1470 | 1618 | 1698 | 1868 |
| Warlock | Conjurer | Conjurer | Soin | 74 | 82 | 86 | 94 |
| Luminary | Luminary 2 (id57) | Holy Blade ? | ATQM | 1470* | 810* | 850* | 936* |
| Luminary | Luminary 3 (id58) | Darkness Blade ? | (vide) | 1618* | 1698* | 1868* | — |
| Agent (Vendetta) | — | Special Agent | PM | 736 | 810 | 850 | 936 |
| Agent (Vendetta) | — | Gunslinger | ATQ | 1890 | 2080 | 2184 | 2402 |
| Wyvern Lord (Vendetta) | — | — | pas d'éveillée listée | | | | |

⚠ / `*` anomalies et écarts natif↔Vendetta :
- **Assassin** : natif ATQ+1470@85 / Vendetta `ATK+1890` — Vendetta a
  vraisemblablement rebalancé (ATT+1890 = tier des « gros » offensifs).
- **Kage** : natif PM+946 / Vendetta `MP+736`.
- **Paladin** : natif Soin+74 / Vendetta `G-HEAL+148`.
- **Dragon Emperor** (id56) : le palier 85 est stocké dans la colonne
  ATQ (`ATK+736`) au lieu de ATQM — *valeur* identique au MATK+736
  Vendetta → probable typo de colonne dans la donnée d'origine.
- **Holy Blade / Darkness Blade** (ids 57/58) : les bandes 18-20 des
  lignes 57/58 semblent **interverties** dans le fichier source —
  lecture corrigée probable : Holy Blade = ATQM+1470/1618/1698/1868
  (= tier Vendetta `M-ATK+1470` ✓) et Darkness Blade = ATQ+?/810/850/936
  (palier 85 vide ; Vendetta = `ATK+736`). À revalider.
- Noms divergents oracle↔Vendetta : `Chronologist` = Time Manipulator,
  `Archimage` = Totem Master, `Demon Tamer` (id39) = Demon Summoner ;
  Ranger/Sage/DK/Luminary = noms provisoires « X 2/3 » dans l'oracle.

## 6. Écarts Vendetta vs natif (classes de base)

Le classeur Vendetta (`knowledge/wiki-tables/M/Class_Expertise.md`)
reprend les valeurs natives mais avec quelques modifications :

- **Ranger** : Vendetta `MP+68@5` (augmente jusqu'à `MP+1310@105`) vs
  natif **PM+58** — rebalanced Vendetta.
- **Classes custom Vendetta** : `Agent` (double pistolets → Special
  Agent/Gunslinger) et `Wyvern Lord` (nunchaku, pas d'éveillée) —
  absentes de l'oracle US ; stats déduites de leurs colonnes du
  classeur (resp. PM+/ATQ− et PV−/ATQM+).
- Vendetta prolonge les paliers **jusqu'à lvl 130** (`[HORS]` > 90 ;
  natif s'arrête à 100 — bands 95/100 listées ci-dessus en `[HORS]`).
- Ordre des colonnes Vendetta par branche :
  Défense (W/K/T/DK) · Mêlée (Voleur/AM/Danselame/Samouraï) ·
  Distance (Chasseur/Ingénieur/Rôdeur/Inquisiteur/Agent) ·
  Soin (Clerc/Barde/Chamane/Sage) · Magie (Magicien/Illusionniste/
  Démoniste/Luminaire/Wyvern Lord).

## 7. Restes à confirmer (MISSING)

- **Contenu de `« CE List »`** — aucune capture de la fenêtre ouverte ;
  probablement la liste par classe des bonus acquis.
- **Tables « class ability bonuses » du wiki EN pour les classes autres
  que Hunter** — seules `Hunter.html`, `Warrior/Cleric/Engineer/
  Thief_Class_Expertise.html` (arbre KP) ont été dumpées ; les valeurs
  des autres classes de base reposent sur l'oracle natif + Vendetta
  (déjà concordants).
- **Plancher des classes éveillées** : les bandes natifs 17-20 =
  lvl 85-100 ; l'éveillée se débloque ~lvl 80 mais aucune donnée sur un
  éventuel bonus aux paliers 80-84 (le fichier n'en a pas).
- **Mimic** : absence de ligne dans la table = aucun bonus présumé,
  à vérifier en jeu.
- Nom final US des éveillées ids 47/48/53-58 (oracle = placeholders).

## Sources citées

| Source | Usage |
|---|---|
| `eden_server/server_us/Data/db_decoded/S_ClassRankBuff.ini` | **Table native CONFIRMÉE** (valeurs par classe/palier, ids) |
| `eden_server/server_us/Data/db_decoded/T_Class.ini`, `Class.ini`, `S_Classes.ini` | Ids↔noms de classes, conditions de déblocage, 7 branches |
| `knowledge/raw/wiki-en/Hunter.html` | Mécanique + table Hunter officielle (appliqué quelle que soit la classe, cumulatif) |
| `knowledge/raw/wiki-en/Class_Expertise.html`, `Knowledge_Points.html`, `*_Class_Expertise.html` | Système KP/talents (distinct) |
| `knowledge/raw/wiki-en/Class.html`, `Class_Branches.html` | Niveau de classe ≤ niveau perso, 5 branches, 20 classes |
| `knowledge/guides-fr/pages/expertise-de-classe.md` | Terminologie FR, cumuls, classements mensuels, position UI |
| `knowledge/guides-fr/pages/certificats-de-classe.md` | Système certificats (distinct) |
| `knowledge/wiki-tables/M/Class_Expertise.md` (+ `src_csv/M_ Class Expertise.csv`) | Table Vendetta complète (base + éveillées 85-130) |
| `knowledge/wiki-tables/M/Guardian_Skills.md`, `Racial_Passive.md` | Correspondances classe de base ↔ éveillées, roster Vendetta (Agent, Wyvern Lord) |
| `ref-shots/ClassAbilitybonuslogo.png`, `ClassePanel*.png`, `HoverToolTipClasse.png` | UI : panneau, logo, tooltip, CE List |
| `docs/harvest/refshots-classes.md` | Détail géométrie UI déjà extrait |
