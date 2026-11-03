# Compétences et passifs raciaux — harvest

> Harvest des connaissances wiki (FR + EN) et classeur Vendetta sur le
> système racial d'Eden Eternal. **Périmètre : contenu lvl ≤ 90**
> (Eden de base). Les classes éveillées (Berserker, Kage, etc. — lvl 95+)
> sont hors périmètre : la feuille Vendetta ne documente d'ailleurs
> aucun passif racial pour elles (cases vides — probablement héritage
> de la classe de base, **non confirmé**).

## Modèle réel (≠ intuition initiale)

**Il n'existe PAS un « sort racial général » unique par race.** Le
système réel a deux couches :

1. **Passif racial par CLASSE** (« trait racial » / « caractéristique
   raciale ») : chacune des ~20 classes possède, en plus de ses traits
   communs (ex. « Inébranlable » du Guerrier), **un** passif qui dépend
   de la race du personnage. Toujours actif (souvent conditionné à
   l'arme équipée). Visible dans l'interface de classe **[K], 3ᵉ onglet**,
   à côté du type d'armure et du passif commun (source :
   `guides-fr/pages/caracteristiques-raciales-des-humains.md`).
   Chaque race modifie aussi légèrement les **stats de base** de la
   classe (PV/PM/FOR/AGI/INT/SAG/CHA en % — voir tableaux).
2. **Sorts raciaux par BRANCHE de classe** : chaque race a **10 sorts
   actifs**, 2 par branche, appris aux **niveaux de classe 45 et 60**.
   Ils « s'ajoutent au tronc commun des compétences de chaque grande
   catégorie de classe » (source : guides-fr). Toutes les classes d'une
   même branche partagent les mêmes sorts raciaux (la race choisit la
   variante) — ex. Guerrier, Chevalier, Templier et Chevalier Dragon
   Humains ont tous Bouclier protecteur (45) + Cri de survie (60).

Les 5 branches : **Défense** (Tank), **DPS Corps à corps** (Melee DPS),
**DPS à Distance** (Ranged DPS), **Soins/Support** (Healing and
Support), **DPS Magique** (Magic DPS).

## Les 5 races jouables

| FR | EN | Artisanat racial | Zone de départ | Genre |
|---|---|---|---|---|
| Humain | Human | Joaillerie / Fusion de Bijoux (Jewel Fusion) — gemmes arme/armure | Monts Calcaires (Limestone Mountain) | M/F |
| Zumi | Zumi | Ingénierie (Engineering) — objets à bonus | Vallée Feregal (Fegeral Valley) | masculin uniquement |
| Anuran | Anuran (wiki EN : « Anura ») | Alchimie (Alchemy) — potions, teintures | Lagune Enchantée (Graceland Marsh) | masculin uniquement |
| Ursun | Ursun | Décryptage de Glyphes (Engraving) — glyphes de classe | Crocs Brumeux (Ursun Paw Mountains) | masculin uniquement |
| Ézelin (aussi écrit Ezelin ; ex-« Enfants Eternels ») | Halfkin | Tissage d'étoffes pour trophées (Embroidery) | Monts Calcaires (Limestone Mountain) | M/F |

Sources : `raw/wiki-fr/Les_Races.html`, `raw/wiki-fr/Races.html`,
`raw/wiki-en/Race.html`, `guides-fr/pages/caracteristiques-raciales-des-*.md`.
Races PNJ : Torturran, Tuskar, Drakai, Ogre.

Distinctions gameplay : tailles différentes (Zumi/Ézelin petits, Ursun
grands — cosmétique/hitbox non documentée), zone de départ différente,
verrou de genre, artisanat racial exclusif. La race se choisit à la
création du personnage et détermine passif + sorts pour chaque classe.

**Correspondance de classe FR↔EN à connaître** : FR **Assassin** =
EN **Martial Artist** (page FR illustrée `EE_classes_MartialArtist.jpg`,
prérequis identiques : Voleur, requis pour Samouraï et Corsaire).
L'« Assassin » EN est une classe **éveillée (lvl 95+, hors périmètre)**.
FR **Corsaire** = EN **Ranger**. FR **Inquisiteur** = EN **Inquisitor**.

---

## 1. Passifs raciaux par classe (race × classe)

Valeurs ci-dessous = wiki EN + guides FR (version « mature » du jeu).
⚠️ Le vieux tableau `raw/wiki-fr/Les_Races.html` donne des valeurs
inférieures sur plusieurs entrées (ex. expertise armes +10 % au lieu de
+15 %) — version antérieure du patch d'équilibrage ; divergences
relevées en §4. Stats = modificateurs raciaux de la classe, en %
(source : tableaux « Racial traits & stat modifiers » des pages classe
EN `raw/wiki-en/<Classe>.html` ; identiques au comparatif FR sauf notes).

### Branche Défense

**Guerrier (Warrior)** — traits communs : Inébranlable (immun. Peur), armure lourde.

| Race | FR | EN | Effet | PV/PM/FOR/AGI/INT/SAG/CHA % |
|---|---|---|---|---|
| Humain | Expert en Hache | Axe Expert | ATQ PHY & MAG +15 % avec une hache | 123/69/125/114/100/100/104 |
| Zumi | Expert en Gourdin | Club Expert | ATQ PHY & MAG +15 % avec un gourdin | 116/79/122/117/100/100/104 |
| Anuran | Expert en Épée Colossale | Greatsword Expert | ATQ PHY & MAG +15 % avec une épée colossale | 127/64/120/116/100/100/107 |
| Ursun | Élite Ursun | Ursun Elite | 3 % de chance d'infliger Peur 6 s (vieux wiki FR : 2 %) | 131/65/128/112/100/100/103 |
| Ézelin | Expert en Épée | Sword Elite | ATQ PHY & MAG **+30 %** avec une épée | 111/85/119/116/100/100/108 |

**Chevalier (Knight)** — commun : Esprit Clair (immun. Hypnose), armure lourde.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Expert en Rapière | Rapier Expert | ATQ PHY & MAG +15 % avec une rapière | 119/77/118/118/100/100/107 |
| Zumi | Expert en Épée | Art of Sword Play | ATQ PHY & MAG +15 % avec une épée | 112/87/115/121/100/100/107 |
| Anuran | Chevalier de la Forêt | Forest Knight | VIT ATQ +15 % avec une lame à 1 main | 123/72/113/120/100/100/110 |
| Ursun | Grande Barrière | Great Barrier | DEF & blocage +10 % avec un bouclier | 127/73/121/116/100/100/106 |
| Ézelin | Héritage du Chevalier | Knight Lineage | DEG CRIT PHY subis −30 % | 108/93/112/120/100/100/111 |

**Templier (Templar)** — commun : Volonté de Fer (immun. Étourdissement), armure lourde.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Expert en Bouclier | Shield Expert | DEF & blocage +15 % avec un bouclier | 121/73/122/113/100/108/100 |
| Zumi | Expert en Masse | Enforcer | ATQ PHY & MAG +15 % avec une masse | 114/83/119/116/100/108/100 |
| Anuran | Appel Noble | Noble Calling | VIT INV +30 % avec une masse | 125/68/117/114/100/112/100 |
| Ursun | Foi Sacrée | Holy Faith | DEG CRIT subis −30 % | 129/69/125/112/100/106/100 |
| Ézelin | Réconciliation Magique | Magic Reconcile | toutes résistances élémentaires +10 | 109/89/116/117/100/110/100 |

**Chevalier Dragon (Dragon Knight)** — commun : Expert en Pique (ATQ +15 % pique), armure lourde.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Maître Dragon | Dragon Master | 2 % chance : DEG PHY subis +5 % / MAG subis +15 % sur la cible, 10 s | 120/90/100/118/100/117/108 |
| Zumi | Contraste Physique | Physical Contrast | 2 % chance de renvoyer 30 % des DEG PHY | 111/99/100/117/100/118/108 |
| Anuran | Contraste Magique | Magic Contrast | 2 % chance de renvoyer 30 % des DEG MAG | 115/95/100/116/100/115/112 |
| Ursun | Flatterie Sincère | Sincerest Form of Flattery | 5 % chance de retirer 1 buff de la cible (pas les boss) | 118/92/100/116/100/112/115 |
| Ézelin | Dragon de Feu | Drake Fire | 2 % chance : RES Feu cible −20 % (guide FR : −30 %), 10 s | 110/100/100/120/100/113/100 |

### Branche DPS Corps à corps

**Voleur (Thief)** — commun : Moyens rapides (VIT ATQ & ESQ +30 %), armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Expert en Poignard | Dagger Expert | ATQ PHY & MAG +15 % avec un poignard | 109/97/112/124/100/100/107 |
| Zumi | Roues en Folie | Crazy Wheels | VIT DEP +10 % | 105/100/110/126/100/100/107 |
| Anuran | Cachette | Conceal Corner | Mal généré −30 % | 111/98/109/124/100/100/110 |
| Ursun | Lame Rotative | Rotating Blade | 5 % chance : VIT DEP cible −50 %, 6 s (pas les boss) | 116/93/115/121/100/100/107 |
| Ézelin | Concentration | Concentration | taux CRIT PHY +30 % | 100/113/110/125/100/100/108 |

**Assassin FR = Martial Artist EN** — commun : Expert en Griffe/Ceste, armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Impact Renversant | Concussive Impact | 2 % chance d'étourdir 3 s | 117/81/123/115/100/100/105 |
| Zumi | Concentration de la Force | Strong Build | VIT ATQ +10 % avec arme contondante 1 main | 110/91/121/117/100/100/105 |
| Anuran | Style du Marais | Marsh Style | double attaque +2 %, DEG décroissants par coup | 121/76/120/115/100/100/108 |
| Ursun | Coup de Pompe | Way of Heavy Punch | 2 % chance d'infliger « Trivial » (EN : *Mundane*) 6 s (pas les boss) | 125/77/126/112/100/100/105 |
| Ézelin | Talent Martial | Martial Art Talent | PREC +30 % | 106/97/121/116/100/100/106 |

**Danselame (Blade Dancer)** — commun : Gymnast (VIT DEP +5 %), armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Expert en Épée | Sword Expert | ATQ PHY & MAG +15 % avec une épée | 115/85/115/121/100/100/107 |
| Zumi | Expert en Arme à 1 Main | Temperament | VIT ATQ +15 % avec arme à 1 main | 108/95/113/123/100/100/107 |
| Anuran | Danse Anura | Anura Dance | taux CRIT PHY +30 % | 120/79/112/121/100/100/110 |
| Ursun | Danse Héroïque | Hero's Dance | DEG CRIT PHY +30 % | 123/81/118/119/100/100/106 |
| Ézelin | Innocence | Innocence | Mal généré −30 % | 105/101/113/122/100/100/108 |

**Samouraï (Samurai)** — commun : Expert en Katana (+10 %), armure lourde.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Battojutsu | Battojutsu | taux CRIT PHY +20 % | 119/76/117/120/100/100/106 |
| Zumi | Action Spontanée | Accident Response | double attaque +2 %, DEG décroissants | 112/86/112/125/100/100/106 |
| Anuran | Méditation Sereine | Quiet Meditation | DEF +10 % | 124/70/114/119/100/100/110 |
| Ursun | Ursun Martial | Martial Ursun | PREC +30 % | 128/72/120/117/100/100/106 |
| Ézelin | Épée Valeureuse | Confidence Sword | parade +10 % avec arme à 2 mains | 108/92/115/121/100/100/107 |

### Branche DPS à Distance

**Chasseur (Hunter)** — commun : Expert en Arc (EN +30 % / vieux FR +10 %), armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Œil d'Aigle | Eagle Eye | PREC +30 % | 105/105/107/115/100/100/121 |
| Zumi | Tir en Douceur | Sweet Shooting | 5 % chance : repousse la cible de ~3 m (10 ft ; pas les boss) | 98/115/105/115/100/100/123 |
| Anuran | Survie en milieu sauvage | Wilderness Survival | RES Nature, Glace, Lumière +10 % | 110/99/107/119/100/100/117 |
| Ursun | Tir Double | Double Shot | double attaque +2 %, DEG décroissants | 111/101/110/115/100/100/118 |
| Ézelin | Archer Furieux | Fusion Archery | VIT ATQ +20 % | 97/121/105/116/100/100/122 |

**Ingénieur (Engineer)** — commun : Expert en Pièce d'Artillerie (+15 %), armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Asile de Métal | Metal Asylum | DEG PHY subis −5 % | 107/101/107/112/100/100/124 |
| Zumi | Cache du Rideau Magique | Magic Iron Hide | DEG MAG subis −5 % | 100/111/105/112/100/100/126 |
| Anuran | Ancienne Turbine | Ancient Turbo | VIT DEP +5 % (⚠ le guide FR écrit « −5 % », typo probable) | 112/95/107/116/100/100/120 |
| Ursun | Contrôle du Barrillet | Cannon Control | 2 % chance : RES Percée cible −10 pts, 6 s | 114/97/110/112/100/100/121 |
| Ézelin | Petit Avantage | Petite Advantage | ESQ +30 % | 98/117/105/113/100/100/125 |

**Corsaire (Ranger)** — commun : Bombardier (taux CRIT +20~30 %), armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Résistance aux Éléments | Elements Resistant | toutes résistances élémentaires +10 % | 113/89/112/112/100/100/119 |
| Zumi | Poursuite | Pursue | double attaque +2 %, DEG décroissants | 106/99/110/112/100/100/121 |
| Anuran | Flèche Lourde | Heavy Iron Arrow | 2 % chance d'étourdir 3 s | 118/83/112/116/100/100/115 |
| Ursun | Grand Arc | Strong Bow | VIT ATQ +20 % | 120/85/115/112/100/100/116 |
| Ézelin | Tir de Dissuasion | Deterrence Shooting | 2 % chance d'immobiliser 6 s | 103/105/110/112/100/100/121 |

**Inquisiteur (Inquisitor)** — commun : Expert en Faucille (Sickleshot +30 %).

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Voile Mortel | Mortal Veil | DEG reçus −10 % | 115/85/114/120/92/92/127 |
| Zumi | Rugissement Sauvage | Savage Roar | taux CRIT PHY +20 % | idem (EN : mêmes stats toutes races) |
| Anuran | Barrière Mortelle | Mortal Barrier | toutes résistances élémentaires +10 % | idem |
| Ursun | Égide Mortelle | Mortal Aegis | résistances physiques +10 %, DEF +10 % | idem |
| Ézelin | Assaut Aveuglant | Blinding Assault | DEG infligés +10 % | idem |

### Branche Soins / Support

**Clerc (Cleric)** — commun : Maître en Incantation (VIT INV +20 % masse), robe.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Dissipeur de Magie | Magic Dissipate | DEG MAG subis −10 % | 103/109/100/100/113/125/105 |
| Zumi | Bouclier Saint | Temple Shield | DEF & blocage bouclier +5 % | 96/119/100/100/113/122/108 |
| Anuran | Prophète | Oracular | Guérison générale (G-Heal) +10 % | 108/103/95/95/113/137/103 ⚠¹ |
| Ursun | Bénédiction du Front | Frontline Saint | ESQ +20 % | 109/105/100/100/110/123/110 |
| Ézelin | Bonté | Kind-hearted | taux CRIT MAG +20 % | 95/125/100/100/113/125/105 |

⚠¹ stats wiki EN (FOR/AGI 95, SAG 137) ; le comparatif FR dit 100/100/127.

**Barde (Bard)** — commun : Musicien en Vogue (Guitare +30 %), armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Rythme Rapide | Brisk Rhythm | double attaque +2 %, DEG décroissants | 111/93/100/114/112/117/100 |
| Zumi | Musique Élégante | Wonderful Voice | puissance de soins (G-Heal) +10 % | 104/103/100/117/112/114/100 |
| Anuran | Mélodie Divine | Lingering Echo | durée des chansons +2 s | 111/93/95/112/117/126/100 ⚠² |
| Ursun | Écho | Echo Sound | DEG CRIT MAG +20 % | 118/89/100/117/109/117/100 ⚠³ |
| Ézelin | Don Inné du Musicien | Gifted Musician | VIT INV +5 % | 101/109/100/114/112/117/100 |

⚠² stats EN ; comparatif FR : 116/87/100/114/110/119/100.
⚠³ stats EN ; comparatif FR : 118/89/100/100/110/123/110.

**Shaman (Shaman)** — commun : Expert en Bâton (VIT INV +20 %), robe.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Bouclier Naturel | Shield of Nature | DEG PHY subis −5 % | 101/113/100/100/116/122/105 |
| Zumi | Nature Sauvage | One with Nature | RES Nature, Feu, Foudre +10 % | 95/123/100/100/116/119/108 |
| Anuran | [sans nom FR] | Wild Sync | DEG CRIT MAG +20 % | 110/102/95/95/116/129/107 ⚠⁴ |
| Ursun | Invocation de la Tribu | Tribe Summon | durée des totems +30 s | 107/109/100/100/113/124/106 |
| Ézelin | Faveur des Ancêtres | Ancestors Favorite | Guérison générale +10 % | 93/129/100/100/116/122/105 |

⚠⁴ stats EN ; comparatif FR : 105/107/100/100/116/124/102.

**Sage (Sage)** — commun : Expert en Marteau (+10 %), armure légère.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Descendant du Sage | Descendant of the Sage | DEG PHY subis −5 % | 113/89/100/114/110/119/100 |
| Zumi | Âme du Sage | Soul of the Sage | VIT DEP +5 % | 106/99/100/116/110/117/100 |
| Anuran | Pouvoir Destructeur | Suppress Power | DEG MAG subis −5 % | 118/86/100/114/109/120/100 |
| Ursun | Guide Spirituel | Spirit Guide | parade +5 % avec arme à 2 mains | 120/85/100/113/108/122/100 |
| Ézelin | Esprit Pur | Pure Spirit | taux CRIT MAG +20 % | 103/105/100/114/110/119/100 |

### Branche DPS Magique

**Magicien (Magician)** — commun : Expert en Bâton (+15 %), robe.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | ABC du Jet de Sorts | Essentials of Casting | VIT INV +5 % | 97/121/100/106/125/100/112 |
| Zumi | Magie Spatiale | Magical Reflexes | ESQ +30 % | 91/131/100/109/122/100/112 ⚠⁵ |
| Anuran | Bagarreurs Tribaux | Tribal Melee | parade +10 % | 101/114/100/106/128/100/109 |
| Ursun | Pelage Divin | Godly Skin | résistance aux dégâts élémentaires +5 | 103/117/100/106/122/100/115 |
| Ézelin | Affinité Magique | Affinity Magic | DEG CRIT MAG +20 % | 90/137/100/106/125/100/112 |

⚠⁵ stats EN ; comparatif FR : 91/131/100/106/128/100/109.

**Illusionniste (Illusionist)** — commun : Lettré (Grimoire +30 %), robe.

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Changement | Changing | Mal généré par les attaques −10 % | 95/125/100/108/120/100/115 |
| Zumi | Angle Mort Invisible | Critical Guard | DEG CRIT subis −15 % | 89/135/100/110/117/100/115 |
| Anuran | Dur à cuire | Thick Skin | DEG MAG subis −5 % | 99/118/100/108/123/100/112 |
| Ursun | Malédiction Profonde | Strong Curse | taux CRIT MAG +20 % | 100/121/100/110/117/100/116 |
| Ézelin | Flash Dimensionnel | Dimension Flash | double attaque +2 %, DEG décroissants | 89/141/100/108/120/100/115 |

**Démoniste (Warlock)** — commun : immun. Peur ; Grimoire (VIT INV +10 %).

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Cape de Crâne | Skull Cloak | taux CRIT MAG +20 % | 99/117/100/112/122/100/109 |
| Zumi | Enchantement Démoniaque | Critical Enchantment | DEG CRIT MAG subis −15 % | 93/127/100/115/119/100/109 |
| Anuran | Tatouage de Sang | Blood Tattoo | RES Foudre, Feu, Obscurité +10 % | 103/110/100/112/125/100/106 |
| Ursun | [sans nom FR] | Body Surrounded | parade +10 % avec arme à 2 mains | 105/113/100/113/119/100/111 |
| Ézelin | Contrôle de l'Esprit | Spirit Controller | VIT DEP +5 % | 92/133/100/112/122/100/109 |

**Luminary (Luminary)** — commun : taux CRIT MAG +20 % + « Chaos Incarnate ».
(Classe lvl ~80, présente dans le wiki officiel FR/EN — l'index guides-fr
la cite aussi parmi le contenu « Vendetta » : à revalider côté serveur.)

| Race | FR | EN | Effet | Stats % |
|---|---|---|---|---|
| Humain | Pensée Chaotique | Chaotic Mindset | ATQ MAG +5 % | 101/113/100/110/118/100/115 |
| Zumi | Lumière Éclair | Fleeting Light | VIT DEP +5 % | 97/123/100/113/115/100/115 |
| Anuran | Cyclone du Chaos | Chaos Cyclone | VIT INV +5 % | 105/106/100/110/121/100/112 |
| Ursun | Coupâme | Spirit Cutaway | DEG CRIT MAG +25 % | 107/109/100/112/115/100/116 |
| Ézelin | Tournelame | Blade Reversal | chance de combo de compétences +5 % | 93/129/100/110/118/100/115 |

Sources passifs : `wiki-fr/pages/Comparatif_des_compétences_raciales.md`,
`raw/wiki-fr/Les_Races.html`, `raw/wiki-en/<Classe>.html` (20 pages),
`wiki-tables/M/Racial_Passive.md`, `guides-fr/pages/caracteristiques-raciales-des-{humains,zumis,anuran,ursun,ezelins}.md`.

---

## 2. Sorts raciaux par branche (race × branche)

2 sorts par race et par branche : débloqués aux **niveaux de classe
45 et 60**. Effets « X » : montants non documentés dans les wikis
(varient selon le niveau de la compétence — mécanique d'upgrade non
documentée). CD = temps de recharge (incant = temps d'invocation).

Sources : `raw/wiki-fr/Sorts_de_race_(par_classe).html` (FR, 4 races —
Ézelin absent), `raw/wiki-en/{Tank_(class_type),Melee_DPS,Ranged_DPS,Healing_and_Support,Magic_DPS}.html`,
`wiki-tables/M/Racial_Skills.md`, guides-fr.

### Défense / Tank

| Race | Niv | FR | EN | Effet | Incant | CD | Notes |
|---|---|---|---|---|---|---|---|
| Humain | 45 | Bouclier protecteur | Protective Shield | DEG PHY reçus −10 % pour le lanceur et le groupe (rayon ~5 m / 15 ft), 12 s ; réduit aussi les DEG PHY de X | instant | 30 s | bouclier requis |
| Humain | 60 | Cri de survie | Survival Scream | ATQ PHY +X ; repousse les ennemis à ~3 m ; le lanceur récupère X PV | instant | 10 s | |
| Zumi | 45 | Bouclier auto-guérissant | Mechanical Healing Shield | rend X PV toutes les 0,5 s pendant 12 s ; 5 % de chance d'annulation si attaqué | instant | 20 s | bouclier requis |
| Zumi | 60 | Attraction magnétique | Draw In | ATQ PHY +X, attire la cible et VIT DEP −40 % pendant 5 s (pas les boss) | instant | 15 s | |
| Anuran | 45 | Bouclier d'épées | Shield of Swords | ESQ +X pendant 12 s ; l'attaquant subit le statut Destruction 5 s | instant | 20 s | bouclier requis |
| Anuran | 60 | Crash de météorite | Meteor Crash | ATQ PHY +X ; charge ~9 m vers la cible, DEG aux ennemis sur le passage, mal généré ↑ | instant | 8 s | arme CàC requise |
| Ursun | 45 | Rugissement guerrier | Vanguard Roar | PV max +X du groupe dans un rayon de ~7,5–15 m, 30 s | instant | 30 s | |
| Ursun | 60 | Crochet fraternel | Brotherhood Punch | ATQ PHY +X, DEG cible ; soigne le groupe X PV à ~5 m | instant | 6 s | arme CàC requise |
| Ézelin | 45 | Bouclier de l'Aurore | Aurora Shield | DEG MAG reçus −10 % lanceur+groupe (~5 m), 12 s ; réduit aussi les DEG PHY de X | instant | 30 s | bouclier requis |
| Ézelin | 60 | Coup Décisif | Victory Slam | ATQ PHY +X ; si PV cible < 30 % : ATQ +X et mal +3 %, 8 s, cumul ×10 | instant | 1 s | arme CàC requise |

### DPS Corps à corps

| Race | Niv | FR | EN | Effet | Incant | CD | Notes |
|---|---|---|---|---|---|---|---|
| Humain | 45 | Récompense du tueur | Slayer's Reward | chaque CRIT rend X PV, pendant 60 s | instant | 120 s (wiki FR : 240 s) | |
| Humain | 60 | Armure tempête | Hurricane Armor | X DEG aux ennemis à ~3 m toutes les 0,5 s, 8 s | instant | 30 s | arme CàC requise |
| Zumi | 45 | Injection | Injection | ATQ PHY +X et double attaque 100 % pendant 15 s | instant | 60 s | |
| Zumi | 60 | Flash Gordon | Flash Step | téléporte ~30 m dans la direction choisie ; taux CRIT PHY +X, 15 s ; aggro les monstres sur le passage | instant | 30 s | hors combat uniquement |
| Anuran | 45 | Marécages | Swampy Land | ATQ PHY +X ; immobilise la cible 6 s et les ennemis à 3 m autour 3 s | instant | 15 s | arme CàC requise |
| Anuran | 60 | Vent Ninja | Wind Ninjitsu | X DEG à ~6 m autour de la cible toutes les 0,4 s, 5 s ; aucune autre action possible (silencieux) | instant | 24 s | |
| Ursun | 45 | Colère de l'ours | Bear's Rage | ATQ PHY +X ; assomme la cible 4 s | 1 s | 12 s | |
| Ursun | 60 | Impact océanique | Wave Impact | ATQ PHY +X ; DEG sur ligne droite de ~18 m (60 ft) | instant | 6 s | |
| Ézelin | 45 | Arme de Jet | Throw Weapon | ATQ PHY +X ; statut « Frappé » : RES PHY −10 %, 8 s | instant | 6 s | arme CàC requise |
| Ézelin | 60 | Poison Virulent | Virulent Poison | X DEG/s à la cible et ennemis à ~3 m, 15 s | instant | 6 s | arme CàC requise |

### DPS à Distance

| Race | Niv | FR | EN | Effet | Incant | CD | Notes |
|---|---|---|---|---|---|---|---|
| Humain | 45 | Flèche ravageuse | Soul Destruction Arrow | ATQ PHY +X ; ATQ MAG cible −X et RES Percée −10 %, 10 s | instant | 6–7 s (FR : 7 s) | arme à distance requise |
| Humain | 60 | Tir incendiaire | Flame Bomb | ATQ MAG +X ; DEG Feu cible + X DEG Feu/s aux cibles à ~3 m, 6 s | instant | 6 s | arme à distance requise |
| Zumi | 45 | Attaque Kung-Fu | Swift Attack Stance | VIT ATQ +X ; 50 % de chance de triple attaque, 12 s | instant | 30 s | arme à distance requise |
| Zumi | 60 | Char d'assaut | Battery and Assault | pose une batterie : ATQ PHY +X, lanceur ancré 60 s ; remplace les barres par « Coup de canon » | instant | 10 s | immobile, pas de potions/sorts |
| Anuran | 45 | Badge sanguinaire | Bloodthirsty Badge | VIT ATQ +X ; soigne 4 % des DEG infligés, 20 s | 1 s | 60 s | ⚠ wiki FR le place à 60 — voir §4 |
| Anuran | 60 | Invocation obscure | Dark Summons | invoque un Esprit du Marécage (niv. = niv. de classe) 2 min ; VIT DEP ennemis −70 % à ~5 m | 2 s | 20 s | incompatible avec autres invocations |
| Ursun | 45 | Onde de choc | Spread Shot | ATQ PHY +X ; DEG en éventail ~5 m, repousse ~3 m (pas les boss) | instant | 8 s | arme à distance requise |
| Ursun | 60 | Tir Dragonvent | Wind Dragon Shot | ATQ PHY +X ; DEG en ligne droite (~18 m / 60 ft) | instant | 6 s | arme à distance requise |
| Ézelin | 45 | Tireur d'Élite | Sniper Shot | ATQ PHY +X ; DEG cible unique à ~18 m ; étourdit 6 s | 3 s | 20 s | |
| Ézelin | 60 | L'Heure de la Mitraillette | Machine Gun Emplacement | invoque une mitrailleuse (niv. = niv. de classe) 2 min ; triple attaque auto | 2 s | 20 s | incompatible avec autres invocations |

### Soins / Support

| Race | Niv | FR | EN | Effet | Incant | CD | Notes |
|---|---|---|---|---|---|---|---|
| Humain | 45 | Affront | Snub | réduit le Mal de l'allié ciblé (ou du lanceur) de X à chaque attaque/compétence, 15 min | 1,5 s | aucun | |
| Humain | 60 | Retraite stratégique | Strategic Retreat | quitte le combat, +X PV/s pendant 6 s | instant | 90 s | |
| Zumi | 45 | Charme guérisseur | Health Charm | pendant 3 min, chaque CRIT rend X PV | 2 s | 60 s | |
| Zumi | 60 | Lien réparateur | Repair Linkage | soigne l'allié ciblé X PV + X PV/s au groupe à ~3 m, 10 s | 2 s | 8 s | ciblable sur tout allié |
| Anuran | 45 | Contre-attaque tribale | Tribal Counterattack | pendant 3 min : après un blocage/parade, renvoie X (Vendetta : 100 %) des DEG, 5 s | 2 s | 60 s | ⚠ wiki FR le place à 60 — voir §4 |
| Anuran | 60 | Vent des ancêtres | Wind of the Ancestors | soigne le groupe X PV toutes les 0,4–0,5 s à ~4–5 m, 12 s | instant | 20 s | |
| Ursun | 45 | Invocation bénie | Blessed Invocation | ATQ MAG +X ; DEG Lumière/sacrés + repousse à ~5 m | instant | 12 s | non animé (wiki FR) |
| Ursun | 60 | Renforcement | Harden | DEF +X ; DEG MAG subis −30 %, 15 s | instant | 60 s | |
| Ézelin | 45 | Inspiration Spirituelle | Spiritual Inspiration | ATQ MAG +X pendant 12 s ; **réinitialise tous les cooldowns** | instant | 120 s | |
| Ézelin | 60 | Bouclier Sacré | Sacred Shield | +X PV/s et DEG reçus −X, 12 s | instant | 30 s | |

### DPS Magique

| Race | Niv | FR | EN | Effet | Incant | CD | Notes |
|---|---|---|---|---|---|---|---|
| Humain | 45 | Échappée belle | Quick Escape | VIT DEP +40 % pendant 6 s, puis rend X PV | instant | 30 s | |
| Humain | 60 | Purification | Purification | annule les effets négatifs ; +X MP/s pendant 6 s | instant | 20 s | |
| Zumi | 45 | Explosion spirituelle | Soul Explosion | ATQ MAG +X ; DEG Feu à ~5 m, repousse/assomme, VIT DEP −30 %, 3 s | instant | 12 s | |
| Zumi | 60 | Déguisement parfait | Perfect Disguise | transforme en boîte : DEF +X, DEG PHY subis −50 %, VIT DEP −20 %, 10 s ; annulé si attaque | instant | 10 s | |
| Anuran | 45 | Sirocco | Sirocco | ATQ MAG +X ; tornade en ligne droite ~18,5 m, DEG Nature, étourdit 5 s | 2 s | 8 s | |
| Anuran | 60 | Drain d'énergie | Energy Drain | rend X MP à chaque attaque/compétence, 20 s | instant | 60 s | |
| Ursun | 45 | Régénération terrifiante | Terrifying Regeneration | +X PV/s pendant 10 s ; attaquants de niveau inférieur subissent Peur 3 s | instant | 60 s | |
| Ursun | 60 | Bouclier gravitationnel | Gravity Shield | aura : VIT ATQ −X et ATQ PHY −X aux ennemis à ~4–5 m, 15 s | instant | 45 s | |
| Ézelin | 45 | Bouclier Glacial | Icicle Shield | pendant 2 min, inflige « Froid Hivernal » (Winter's Chill) 10–12 s aux attaquants | 2 s | 180 s | |
| Ézelin | 60 | Évanescence | Fade Away | Mal −X à chaque attaque/compétence, 30 s | instant | 60 s | |

---

## 3. Fonctionnement récapitulé

- **Passif racial** : défini par (race, classe) — change en changeant de
  classe. Toujours actif, non dissipable ; beaucoup sont conditionnés à
  l'arme équipée (« si équipé d'une hache… »). Affiché dans la fenêtre
  de classe **K → 3ᵉ onglet** avec le passif commun et la maîtrise
  d'armure (guides-fr). La race modifie aussi les stats de base de la
  classe (% — tableaux §1).
- **Sort racial** : défini par (race, branche de classe) ; appris aux
  niveaux de classe 45 et 60 ; utilisable par toute classe de la branche.
  Ce sont de vraies compétences (barre de sorts), souvent des actifs à
  cooldown, parfois soumis à un type d'arme. Les montants « X » montent
  avec le niveau de la compétence (mécanique d'amélioration non
  documentée dans les sources).
- **Statuts cités** : Peur, Silence/« Trivial » (EN *Mundane*),
  Étourdissement, Immobilisation, Destruction, Frappé (Struck),
  Froid Hivernal (Winter's Chill) — à mapper sur le système de buffs.

## 4. Divergences entre sources (à trancher via les binaires/données)

- **Vieilles valeurs FR** (`raw/wiki-fr/Les_Races.html` et parfois
  `Comparatif_des_compétences_raciales.md`) < valeurs EN wiki/guides FR :
  expertise d'armes +10 % → +15 % ; Ursun Guerrier Peur 2 % → 3 % ;
  Ézelin Chevalier −15 % → −30 % ; Voleur Ézelin +20 % → +30 % ; Anuran
  Cachette −10 % → −30 % ; Templier Ursun −15 % → −30 %, etc.
  → probables changements de patch d'équilibrage ; **valeurs ci-dessus =
  version récente (EN wiki + guides FR concordants)**.
- **Niveaux 45/60 inversés** dans `Sorts_de_race_(par_classe).html` pour
  Anuran distance (Invocation obscure / Badge sanguinaire) et Anuran
  soins (Vent des ancêtres / Contre-attaque tribale) : le guide FR
  (captures jeu) + wiki EN + Vendetta placent Badge sanguinaire et
  Contre-attaque tribale à 45. Adopté ici, signalé.
- **Cooldown Récompense du tueur** : 240 s (wiki FR) vs 120 s (EN).
- **Dragon de Feu** (Ézelin Chevalier Dragon) : guide FR −30 % RES Feu
  (« Vendetta : 20 % »), wiki EN −20 %.
- **Stats Anuran** Clerc/Barde/Shaman diffèrent (EN : FOR/AGI 95, SAG
  126–137 — patch de redistribution) — voir notes ⚠¹-⁴.
- **Ancienne Turbine** (Anuran Ingénieur) : guide FR écrit « VIT DEP
  −5 % », EN dit +5 % — typo probable du guide.
- **Templier Zumi** : comparatif FR ancien « RES MAG +5 %, parade +5 % »
  vs récent « ATQ +15 % masse » (EN/guide).

## 5. Données MANQUANTES (non trouvées dans les wikis)

- **Valeurs numériques « X »** de tous les sorts raciaux (dégâts, soins,
  montants) — les wikis ne donnent que la formule. → `t_skill`/`t_buff`
  des données client, ou captures serveur natif.
- **Progression des sorts raciaux** (montent-ils de niveau ? coût ?
  automatique au niveau de classe ?) — non documenté.
- Passifs raciaux des classes **éveillées** (cases vides du tableau
  Vendetta — mécanique d'héritage à confirmer, hors périmètre).
- Noms FR exacts de quelques passifs signalés « sans nom » (Shaman
  Anuran « Wild Sync », Démoniste Ursun « Body Surrounded »).
- Icônes/ids internes des sorts et passifs (nommage assets, id
  `ClassAbilitybonuslogo` observé dans ref-shots — le « Class ability
  bonus » des pages EN est un **autre** mécanisme : bonus ATQ par palier
  de niveau de classe, non racial).
- Effet réel des hitboxes/tailles de race — cosmétique supposé, non prouvé.

## Hors périmètre (lvl 95+ / Vendetta)

- Classes **Agent** (dual pistols) et **Wyvern Lord** : passifs raciaux
  connus uniquement via `wiki-tables/M/Racial_Passive.md`
  (ex. Agent : Humain ESQ+20 %, Ézelin PREC+20 %, Zumi DEG+5 %, Anuran
  DEG subis −5 %, Ursun double attaque +2 %) — classes post-90.
- Toutes les classes éveillées listées dans `Racial_Passive.md`
  (Berserker, Kage, Asura, Blade Master, Reaver, Adjudicator, etc.).
