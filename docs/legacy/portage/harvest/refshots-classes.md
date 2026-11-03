# Harvest ref-shots — système de classes (UI de référence)

> Extraction visuelle des captures de référence `ref-shots/` liées aux
> classes / talents / certificats / sorts / stats / races. Client de
> référence = **client EN Aeria** (tout le texte UI capturé est en
> anglais — à traduire côté client Godot via `ui_text_en_fr.tsv` +
> `t_*.ini` FR). Résolution des captures pleines : **1410×878**, dont
> ~60 px de barre de titre « Eden Eternal » en haut ; la zone jeu utile
> est donc ~1410×818.
>
> Convention : verbatim UI entre `« … »`. `🔒` = cadenas affiché sur
> l'icône de classe. Coordonnées en px source (1410×878).

## Fichiers de fond lus

| Fichier | Contenu |
|---|---|
| `ClassePanel.png` | Fenêtre Class, onglet Class Skills (Clerc) + tooltip Life Cure |
| `ClassePanel-ClassTalent&Certificat.png` | Onglet License knowledge (arbre de talents + certificats) + tooltip Religious Knowledge |
| `ClassePanel-ClassNews.png` / `…2` / `…3` | Onglet Class News (fiche classe : stats, hebdo, tenues) + tooltip Hammer of Discipline ; variantes de tenue |
| `ClassPanel-ChangementDeClasse.png` | Juste après Switch → Thief : bannière, log de skills apprises, hotbar vidée, vitales re-calculées |
| `HoverToolTipClasse.png` | Tooltip de classe verrouillée (Shaman) + liste complète des skills Clerc |
| `ClassAbilitybonuslogo.png` | Panneau « Class Ability Bonus » + tooltip de totaux |
| `case pour certificat.png` | Grille de certificats remplie (icônes colorées + gemmes de jonction allumées) |
| `NouveauSkillApresLevelUp.png` | Bannière « learned skill » + série de messages système |
| `skill hover info` | Tooltip skill « Encourage » (extrait) |
| `TopLeftVitals.png` | Widget vitales : niveau perso + niveau de classe |
| `DoubleEXPbar.png` | Double barre d'XP (perso / classe) |
| `HotbarPanel.png` | Hotbar avec skills Clerc |
| `profile.png` | Character Info, onglet Character Stats (fiche complète + sets d'armure) |
| `profile-Title.png` | Onglet Title (titres « Legendary * Master » liés aux armes) |
| `TimedCastSkill.png` | Cast bar Earth Shock + barre ULTIMATE + overlays d'état |
| `SkillAnimationTornado(cleric).png` | Sort Clerc Purifying Wind + message d'état acquis |
| `Selection_personnage.png` | Sélection de personnage (aucun écran de race dans le corpus) |
| `CommunityPanel.png` | Liste d'amis : colonne « Class » + état post-switch |
| `party UI.png` | Cadres de groupe : icônes niveau perso + niveau de classe |

---

## `ClassePanel.png` — fenêtre « Class », onglet Class Skills

### Géométrie globale

- Fenêtre « Class » : x≈200–1140, y≈90–705. Barre de titre sombre
  (« Class » + croix X en haut à droite), corps fond bleu-nuit
  (#16202C env.).
- Colonne gauche « Sort by class » : x≈208–497 (losange bleu + titre +
  bouton « ? » en haut à droite de la colonne).
- 3 onglets à y≈193–215 : `« Class Skills »` | `« License knowledge »` |
  `« Class News »` — boutons ~150 px de large ; actif = gris/argent,
  inactifs = bleu foncé.
- Zone contenu : x≈505–1135, y≈215–700.

### Liste des classes (colonne gauche)

Chaque entrée ≈62 px de haut : icône de classe ~50 px + nom + `« Lv.N »`
ou cadenas ; bouton `« Transform! »` (gris foncé, ~70×20 px) pour les
classes débloquées non actives. Scrollbar à droite (triangles bleus +
gemme de défilement bleue).

Entrées visibles (haut → bas) : `« Mimic »`🔒 (icône papillon/mite
bleue), `« Warrior Lv.1 »` [Transform!] (icône or, hache), `« Knight »`🔒
(icône or-verdâtre), `« Templar »`🔒 (bouclier ailé or), `« Dragon Knig… »`🔒
(dragon or), `« Thief Lv.1 »` (icône orange, épée) — liste tronquée,
scrollable.

En bas de colonne (y≈645–700) : panneau **« Class Ability Bonus »**
(icône soleil/médaille or ~40 px + bouton « ? » + bouton `« CE List »`
en dessous). Voir `ClassAbilitybonuslogo.png` pour le contenu.

### En-tête de classe (zone contenu, y≈225–290)

Rangée d'icônes ~55 px à x≈510–660 + nom de classe + bouton
`« Activate »` (gris foncé) à droite :

- **Icône 1** : icône de la classe sélectionnée, entourée de
  **coins dorés animés** (crochets or aux 4 coins — sur 2 captures les
  crochets sont à des positions différentes → animation de sélection)
  + **petit ruban bleu** en bas à droite de l'icône = classe active.
  `« Lv.14 »` sous l'icône (Clerc).
- **Icône 2** : même taille, **désaturée/silhouette** — balance ailée
  gris-bleu pour le Clerc ; dague+cape sombre pour le Thief → icône de
  la classe « suivante »/éveillée de la branche, non débloquée.
- **Icône 3** : carré bleu foncé avec `« ? »` blanc — slot de classe
  inconnue/non révélée.
- À droite : `« Cleric »` + bouton `« Activate »` (aspect inactif quand
  la classe courante est sélectionnée — à vérifier en jeu).

### Section « Reg. Skills » (y≈290–560)

- Barre de titre : `« Reg. Skills »` + `« 550 Gold 9 Silver Available »`
  (icônes de pièces or/argent).
- Grille 2 colonnes × ~7 lignes. Chaque ligne : icône carrée ~40 px,
  barre de nom dégradé gris foncé, `« Lv.1 »`, puis 2 boutons gris
  ~45×18 px : `« +1 »` et `« Max »`.
- Skills Clerc (reconstitué avec `HoverToolTipClasse.png`) :
  - Colonne gauche : **Earth Shock, Cure, Revive, Strategic Retreat,
    Recovery Link, Wind of the Ancestors, Harden**
  - Colonne droite : **Tornado, Prevention, Snub, Health Charm,
    Tribal Counterattack, Ancestral Roar, Spiritual Inspiration**
  - Tous `Lv.1`.

### Colonne « Class Skills » (droite)

- Barre de titre : `« Class Skills »` + `« 4331 remaining CP »`.
- Entrées : `Light's Sanction Lv.9` [+1][Max], `Life Cure Lv.11`
  [+1][Max], `Hard Truth Lv.11` [+1][Max], `Purifying Wind Lv.1`
  (**nom et icône assombris** — pas de boutons visibles = non appris /
  non montable ? à vérifier).

### Tooltip skill (ex. Life Cure)

Boîte noire ~90 % opaque, bordure fine, x≈765–1160, y≈390–700 :
- Titre `« Life Cure »` bleu clair.
- `« Cost: 38 MP »`, `« Cooldown: 30 seconds »` blanc.
- Effet en **vert** : `« For 15 seconds, recovers +84 HP per second. »`
- Séparateur fin.
- `« Next level »` en **rouge**, puis même structure pour le niveau
  suivant (Cost 41 MP, +88 HP).
- En bas, **rouge** : `« Learning the skill's next level requires
  153 Class Points! »`

### HUD visible autour

- Vitales haut-gauche : LV perso 100 (icône couronne), LV classe 11,
  HP `14999/14999` (barre rose), MP `12262/12262` (barre bleue).
- Barre `« ULTIMATE »` haut-centre : `« ULTIMATE Lv.1 EXP 200/400
  Perks 2 »` + fine barre de progression rose.

---

## `ClassePanel-ClassTalent&Certificat.png` — onglet « License knowledge »

### Liste des classes (scrolled autrement)

`Ranger`🔒, `Inquisitor`🔒, `Cleric Lv.14` (**entourée vert vif** =
classe active, sans bouton), `Bard Lv.1` [Transform!], `Shaman`🔒,
`Sage`🔒 → la liste plate mélange les branches et des classes non
standard (Mimic, Dragon Knight, Inquisitor, Sage — éveillées/spéciales,
cadenas).

### Arbre « Class Talent » (x≈505–950)

- Grille d'icônes carrées ~55 px reliées par des **flèches grises →**
  (prérequis). Haut-gauche : 3 icônes allumées (figure terre/brun avec
  auréole, nuage/rayons blancs, figure bleue ailée) ; le reste est en
  silhouettes sombres = non appris.
- Panneau droit « Class Talent » (x≈960–1135) : bouton « ? », barres
  `« 7 KPs Remain »` puis `« 1 KPs Entered »`, boutons `« Confirm »` |
  `« Refresh »`.

### Tooltip talent (Religious Knowledge)

- `« Religious Knowledge »` blanc
- `« Max Level 20 »` bleu clair
- Vert : `« For each level up, WIS +1%. »`
- Citation jaune italique : `« Ingest the spirit of various religions,
  so that I can understand the knowledge of multiple beliefs. »`
- Rouge : `« Learning the next Knowledge level will cost you 1
  Knowledge Points! »`

### Section certificats (y≈430–700)

- Libellé `« Certificates: 9 »` (compte des certificats possédés).
- **Grille 3×3** à x≈540–705, y≈490–650 : grandes cases ~34 px, pas
  ~62 px, chacune gravée d'une **fleur de lys sombre** = case vide.
- Aux **intersections internes** (2×2 points de jonction) : petits
  carrés **dorés ~21 px à 4 panneaux** — visibles à x≈577–600/y≈526–548
  (jonction haut-gauche) et x≈640–663/y≈589–612 (jonction bas-droite).
  → gemmes de combinaison entre 4 cases.
- Inventaire à droite (x≈720–1140) : cadre contenant **1 icône de
  certificat bleue** (~40 px, croix/parchemin ailé sur fond rayonnant
  bleu) + bouton `« Confirm »` en bas à droite + « ? » en haut.
- **Aucun texte de bonus de combo visible** sur cette capture.

---

## `ClassePanel-ClassNews.png` — onglet « Class News » (fiche Clerc)

- Bandeau descriptif (y≈200–245) : icône de classe + texte
  `« Clerics call on holy power to heal wounds and protect allies from
  negative magic. »`
- Colonne « Characteristics » (x≈505–700) : 4 icônes ~45 px — 2
  allumées bleues, 2 grisées (slots de caractéristiques de classe).
- `« Class Stat Bonus »` : `HP 103% · MP 109% · STR 95% · AGI 95% ·
  INT 113% · WIS 135% · LCK 105%` — multiplicateurs de stats par classe.
- « Weekly Performance » (x≈695–870) : 3 lignes [icône dorée ~40 px +
  nom + compteur `0` + `« Stress1 »`] :
  `Territory War Kill` (drapeau rouge), `Arena Victories C…` (troisphée,
  tronqué), `Monster Conque…` (hache+drapeau, tronqué). `« Stress1 »`
  est le libellé verbatim affiché (sens à vérifier — compteur hebdo ?).
- Préview 3D (x≈870–1140) : personnage en tenue Clerc sur socle à
  emblème ailé ; liste numérotée **1–9** sur le bord droit (sélecteur
  de tenue) ; bouton `« Set Outfit »` en bas.
- Rangées du bas : `« Class Skills »` → ligne d'icônes (~9) ;
  `« Class Certificates »` → ligne d'icônes (~6 bleues + fleurs de lys
  sombres pour les slots vides).

### Tooltip Hammer of Discipline (bas droite)

- `« Hammer of Discipline »` bleu clair
- `« Cost: 17 MP »`, `« Cast: 1.5 seconds »` blanc
- Vert : `« Increases M-ATK +186, dealing Holy DMG to all enemies
  within 15 meters of the target. Inflicts 13 Holy DMG per second, and
  reduces Holy Resistance -5 for 10 seconds. Stacks up to 2 times. »`
- Rose/rouge : `« Required weapon: Mace/Guitar/Staff/Grimoire »`
- Rouge : `« Must reach class level 50 »`

## `ClassePanel-ClassNews2.png` / `…3.png`

Même fiche, **tenues différentes** sélectionnées dans le sélecteur 1–9
(robe blanc/vert, puis blanc/or). La rangée `Class Skills` montre ~9
icônes allumées + 2 cases fleur-de-lys sombres ; `Class Certificates`
~6 allumées + 2 sombres.

---

## `ClassPanel-ChangementDeClasse.png` — juste après switch → Thief

- **Bannière géante** translucide sur toute la fenêtre : `« Switched
  Class to Thief »` (grandes lettres blanches à contour) + **décor
  d'ailes de dragon noir** derrière le panneau skills (visuel
  d'animation de changement de classe).
- En-tête : icône Thief orange (épée) + coins dorés + ruban bleu,
  `« Thief »`, `« Lv.1 »` ; icône 2 désaturée (dague+cape), icône 3 « ? ».
- Reg. Skills Thief : **Slam, Encourage, Fury Hit, Hurricane Armor,
  Flash Step, Wind Ninjutsu, Wave Impact** | **Resonating Blow, Fast
  Shadow, Slayer's Reward, Injection, Swampy Land, Bear's Rage, Hidden
  Weapon Thr…** — tous `Lv.1`. Class Skills : `« Ambush Lv.1 »`.
- Vitales : LV perso 100, **LV classe 1** (icône épée orange),
  HP `14999/15750` (rose), MP `17500/11026` → **courant > max affiché**
  (reste de la pool Clerc non re-clampée à l'écran — à vérifier si le
  serveur renvoie les deux valeurs ou si c'est un artefact d'affichage).
- **Hotbar vidée** : 12 slots = cases fleur-de-lys sombres + touches
  `1 2 3 4 5 6 7 8 9 0 ° +` — les skills placés ne survivent pas au
  switch.
- Chat (messages système, préfixe `<System>` gris + texte rouge,
  format exact) :
  `You have learned skill "Light Armor Mastery".` · `"Brisk Rhythm".` ·
  `"Popular Musicians".` puis `Switched Class to Thief` (sans quotes
  ni point) puis `"Jewel Fusion".` · `"Light Armor Mastery".` ·
  `"Dagger Expert".` · `"Ambush".` · `"Quick Means".`

## `NouveauSkillApresLevelUp.png`

- Bannière centre-écran ~30 px : `« You have learned skill Ambush. »`
  (blanc, contour sombre).
- Même série de messages `<System>` que ci-dessus (version lisible
  complète) — format canonique : `You have learned skill "NOM".`
- Joueur sur monture noire ailée ; nom `« [GM]EE???…??? »`.
- Conseil tuto bas : `« How to move »` + texte WASD + NPC `« yellow
  question marks »`.

---

## `HoverToolTipClasse.png` — tooltip classe verrouillée

Survol de `Shaman` (🔒) dans la liste → tooltip sombre :

- `« Shaman »` blanc
- `« Shamans pray to ancient spirits to lend them power, sometimes
  summoning them into battle. »`
- Rouge : `« Class Requirement: * Character Level 45 * Cleric
  Level 30 »`

→ classes verrouillées affichent description + prérequis en rouge au
survol (corrobore le tableau des prérequis `classes-catalog.md` :
Shaman = perso 45 + Clerc 30 en version INI-EN — **la capture montre
bien la version EN**).

## `skill hover info` (243×70) — extrait tooltip « Encourage »

- `« Encourage »` bleu clair ; `« Cost: 382 MP »` ;
  `« Cooldown: 60 seconds »` ; vert : `« For 30 seconds, increases
  Double Hit Rate +15% and STR +244. »` — même gabarit de tooltip que
  Life Cure.

---

## `ClassAbilitybonuslogo.png` (225×144) — « Class Ability Bonus »

Crop du panneau bas-gauche de la fenêtre Class + tooltip violet :

- En-tête : icône soleil or + `« Class Ability Bonus »` + « ? » +
  bouton `« CE List »` (CE = Class Expertise ?).
- Tooltip fond **violet** : `« Class Ability Bonus Total »` puis :
  `HP 10108 points`, `MP 3309 points`, `ATK 7133 points`,
  `Magic 9927 points`, `G-Healing 407 points`.

→ la somme des bonus de classe cumulés (tous niveaux de classe
montés) se matérialise en points bruts affichés dans ce tooltip.

## `case pour certificat.png` (169×200) — grille remplie

Fond **lavande pâle** (≠ fond sombre de la fenêtre → autre
contexte/peau ou fond de panneau différent). Grille 3×3 de grandes
icônes carrées colorées (~50 px) : verts (montagne, vrille/guitare,
baies/croix, aile/feuille), violets (silhouette, vague), bleu clair
(fortification/masque), or (spirale ×2). Aux jonctions internes :
petits carrés **or/brun en forme de « + » ~16 px** (2 visibles, allumés).

→ lecture croisée avec l'onglet License knowledge : les grandes cases
reçoivent les certificats ; les **petites gemmes « + » aux 4
intersections internes s'allument** quand la combinaison des 4
certificats adjacents valide un bonus. 2 gemmes allumées ici, 2 dans
l'autre capture (positions différentes). **Aucun libellé de bonus
visible — nécessite une vérif en jeu** (tooltip de la gemme ? ligne
dans « Class Ability Bonus » ?).

---

## HUD / widgets liés à la classe

### `TopLeftVitals.png` (176×85)

Portrait (cadre orné sombre) ; à droite : icône couronne + `« 100 »`
(niveau perso), icône aile/bleue + `« 11 »` (**niveau de classe**) ;
nom `« EE???…???… »` ; HP rose `14907/14999` ; MP bleue
`11150/12262` (chiffres basse résolution, ±1 chiffre). Grille de buffs
2×6 cases **numérotées 1–12** (placeholders numérotés même vides).
Icône cadeau bleu/ruban rouge sous le portrait.

### `DoubleEXPbar.png` (201×~30)

Deux barres fines bas-gauche : **haut** = icône couronne orange (XP
perso), barre vide sombre `« 0.000% »` ; **bas** = icône classe bleue
(XP de classe), barre **violette** pleine `« 100.000% »`. Format
`« %.3f%% »`.

### `HotbarPanel.png` (307×35)

Flèches nav haut/bas à gauche ; slots 1–6 remplis (skills Clerc) ;
slots 7 8 9 0 ° + = fleurs-de-lys sombres ; flèches/colonne à droite.
Conforme à la vidange après switch (séparée).

### `TimedCastSkill.png`

- Cast bar centrée bas (~450 px de large, finitions gemme aux bouts) :
  remplissage rose + libellé `« Earth Shock »` au-dessus.
- Overlay `« Leaving Combat »` centre-haut (gris translucide) ; texte
  rouge `« You have accomplished the Mind state »` (format
  `You have accomplished the <X> state` ?).
- Barre `« ULTIMATE »` : `« ULTIMATE Lv.1 EXP 200/400 Perks 2 »`
  (système perks/compte ?).

### `SkillAnimationTornado(cleric).png`

Cast Clerc → énorme tornade de feuilles vertes ; texte rouge
`« You have acquired <Purifying Wind> state. »` (format exact avec
chevrons `<>` et point). Le joueur tient un bâton.

### `party UI.png`

Cadres de groupe : par membre — icône verte + niveau perso (`85`/`90`),
icône **magenta** + `« 80 »`/`« 90 »`/`« 83 »` = **niveau de classe**
(même icône magenta pour les 3 → icône générique, pas par classe),
couronne = leader, nom, HP rose, MP bleue, rangée d'icônes de buffs
circulaires ~16 px.

### `CommunityPanel.png`

Liste d'amis — colonnes : `« Ch. | Name | Lv | Class | Location |
Info »` → **colonne Class** dans les listes sociales. Capture faite
après switch : vitales affichent LV classe `1` (icône orange) et les
deux barres d'XP à `0.000%`.

### `Selection_personnage.png`

Écran de sélection de perso : pas de notion de race/classe affichée
hors icône « 13 » (LV ?) sur la fiche ; panneau « Preview » = 10
portraits en 2×5. **Aucun écran de création/race dans tout le corpus.**

### `profile.png` — Character Info, onglet Character Stats

- En-tête : couronne + nom + `« LV 100 »` + icône classe +
  `« Cleric »` + `« LV 11 »` + `« Perks 2 »`.
- Onglets : `Character Stats | Title | Fame | Achievement`.
- Core Stats : STR 122 · AGI 122 · **INT 172 (vert)** · **WIS 217
  (vert)** · LCK 135 · ACC `122 +4.29%` · Move SPD `50 +50%`.
- Physical Offensive : P-ATK `1027` (rouge) · ATK SPD `7.5 +135%` ·
  P-Healing `-` · CRIT Rate `124.7 +5.63%` · CRIT DMG `6.29 +629%`
  (vert).
- Magical Offensive : M-ATK `1757` (rouge) · Cast SPD `15 +2.04%` ·
  G-Healing `-` · M-CRIT Rate `122 +1.5%` · M-CRIT DMG `1.5 +150%`.
- Defensive : DEF `1016` (vert) · EVA `174 +8.07%` · Parry
  `135 +2.4%` · Block `151 +0.98%`.
- → valeurs colorées : **vert = bonusé au-dessus de la base, rouge =
  malus** (P-ATK/M-ATK d'un Clerc en dessous de la base), `+x%` =
  contribution secondaire.
- Centre : préview 3D + colonne de slots d'équipement (gemmes losange
  bleues) ; boutons `« Gear Upgrade »` `« Gear Swap »`.
- Panneau droit « sets » : `« Cloth Armor (Current Gear) »` (5 icônes
  allumées + tooltip `« Cloth Armor Set Waist »`), `« Light Armor »`,
  `« Heavy Armor »` (grisés) ; colonne droite = losanges colorés avec
  compteur `0`.

### `profile-Title.png` — onglet Title

- Champ `« No Title »` + arbre `« List of Titles »` : `No Title`,
  `General Title`, `Honor Title` (déplié : `Simmel Crusader`,
  `Gorefiend's Downfall`, `Nemus Mourner`, `Serfa's Punisher`
  [sélectionné], `Wing of Ruin's Bane`, `Holy Land Master`,
  `Sacred Star`, `Holy Land Eternal Legend`, `Legendary Monster
  Slayer`, `Legendary Summoner`, `Legendary Excavator`, `Legendary
  Cultivator`, `Legendary Jeweler`, `Legendary Necksmith`, `Legendary
  Enchanter`, `Legendary Ringsmith`, `Legendary Dagger Master`,
  `Legendary Rapier Master`, `Legendary One-Handed Sw…`, `Legendary
  Mace Master`, `Legendary Cestus Master`, `Legendary Club Master`,
  `Legendary Shield Master`).
- `« Ability description »` (Serfa's Punisher) : `« Two-handed strike
  weapon P-ATK & M-ATK +3% »` + lore. `« Method of Acquisition »` +
  case `« Displaying owned title »` + bouton `« Set Title »`.

---

## Points de style récurrents (à reproduire dans Godot)

- Tooltips : fond noir ~85–90 %, bordure fine grise ; titre bleu
  clair ; coûts/CD blancs ; **effets en vert** ; section « Next level »
  avec label rouge ; **prérequis/coûts d'apprentissage en rouge** ;
  « Required weapon » rose/rouge ; citation lore en jaune italique.
- États : classe active = icône + **coins dorés animés** + ruban bleu +
  entrée liste **bordée vert** ; classe débloquée non active = bouton
  `« Transform! »` ; verrouillée = icône désaturée + **cadenas** +
  tooltip description+prérequis rouge ; slot inconnu = `« ? »`.
- Slots vides partout (skills, certificats, hotbar) = **fleur de lys
  sombre** en relief sur case sombre.
- Messages : `<System>` gris + texte rouge ; compétence apprise =
  `You have learned skill "NOM".` ; switch = `Switched Class to X`
  + bannière plein écran ; état = `You have acquired <NOM> state.`.
- EXP : barre perso (couronne, orange) + barre classe (icône classe,
  violette), format `0.000%`.

## Reste flou / à vérifier en jeu

- **Races** : aucune capture de création de perso ni de sélection de
  race → tout le visuel race reste à documenter autrement (INI/wiki).
- **Classes éveillées** : seulement des silhouettes (icône 2 de
  l'en-tête + entrées 🔒 Sage/Inquisitor/Dragon Knight/Mimic dans la
  liste) — aucun visuel « éveillé » allumé dans le corpus.
- Signification exacte de `« Stress1 »` (Weekly Performance) et du
  compteur `« Certificates: 9 »` (possédés ? slots ?).
- Règle d'allumage des gemmes « + » de certificats et **texte du bonus
  de combo** (absent des captures).
- MP `17500/11026` post-switch : vrai état serveur ou non-refresh UI.
- `« Activate »` vs `« Transform! »` : deux boutons, rôles exacts à
  confirmer (activer la classe affichée vs basculer depuis la liste ?).
- « CE List » = probablement Class Expertise list (non ouvert sur les
  captures).
- Barre `« ULTIMATE … Perks 2 »` : système de perks/titre de compte,
  rapport avec `« Perks 2 »` de la fiche perso à confirmer.
