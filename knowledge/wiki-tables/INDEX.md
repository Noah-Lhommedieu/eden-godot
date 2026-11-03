# Base de connaissance — tableaux du wiki Vendetta (Eden Eternal)

Conversion en markdown des 64 feuilles du classeur Google Sheets
« Eden Eternal Vendetta Wiki » (communauté du serveur privé Vendetta,
éditeurs Paramount/Amaris ; sources Aeria + VGN).

- **Source** : `Eden_Eternal_Vendetta_Wiki.xlsx` (copie locale, ici même)
  + exports CSV bruts dans `src_csv/` (1 CSV = 1 feuille).
- **Conversion** : `csv_to_md.py` (stdlib `csv` uniquement) →
  `manifest.json` (mapping CSV → md + scope `in`/`out`/`partial`/
  `uncertain`). Rejouable via `python3 csv_to_md.py`. Chaque cellule
  non vide du CSV est présente dans le md.
- **Limites communes** : les md **n'ont pas d'en-têtes de colonnes
  propres** (la ligne d'en-tête est une ligne de données) ; les tables
  sont souvent plusieurs sous-tables accolées ; coquilles d'origine
  conservées (« Gale Kngiht », « Sequioa », « Turture », « Neckklace »,
  « Malice Palice »…). Les valeurs sont celles du serveur Vendetta
  (boosts, classes custom, monnaies custom) — pas forcément celles
  du jeu de base.

> **Périmètre** : le serveur réplique l'Eden de base = contenu
> **niv. 90 max**. Marqueurs : `[>90]` = fichier entièrement hors
> périmètre, `[~90]` = partiel (sections ≤90 utilisables), `[?]` =
> niveau à vérifier. Le contenu >90 reste documenté — voir la
> **section finale** de cet index et du `../README.md`.

## Préfixes des feuilles

| Préfixe | Dossier | Nb | Signification |
|---|---|---|---|
| `BM_` | [BM/](BM) | 4 | **Battle Maps** — zones de combat : trophées de boss, capes/anneaux/colliers, potions, bestiaire |
| `C_` | [C/](C) | 8 | **Craft** — blueprints armes/armures craftés, armes & sets éveillés, enchants partiels |
| `D_` | [D/](D) | 24 | **Donjons** — Trials, donjons customs : drops par boss, trophées, blueprints, monnaies |
| `G_` | [G/](G) | 1 | **Gemmes** custom Vendetta de l'Arcane Box (95/105/110) |
| `M_` | [M/](M) | 20 | **Misc / mécaniques** — classes, familiers, raciaux, teintures, meubles, monnaies, titres, guilde |
| `R_` | [R/](R) | 3 | **Crafts raciaux** — gemmes Humain, glyphes Ursun, gadgets Zumi/étoffes Halfkin/alchimie Anuran |
| `T_` | [T/](T) | 1 | **Trophies** — référence complète de tous les trophées (niv. 1-123) |
| (aucun) | [misc/](misc) | 3 | Index du classeur, guides de leveling/farming, infos systèmes |

## C/ — Craft (armes, armures, enchants)

Modèle de recette commun : `(niveau, type, nom) → [mat1 ×N, mat2 ×N,
mat3 ×N]` ; slot 1 = minerai (heavy) ou bois (light/cloth), slot 2 =
drop de zone, slot 3 = pensée/volonté ou **outil de faction** acheté en
fame (sous-table accolée : 9 factions × 4 paliers 1 000-6 000 fame).

| Fichier | Contenu | Périmètre |
|---|---|---|
| [Weapons Lv20-90](C/Weapons_Lv20-90.md) | Blueprints d'armes craftées : **18 types** (dagger, rapier, sword, mace, cestus, club, katana, greatsword, axe, pike, hammer, bow, artillery gun, sickleshot, guitar, grimoire, staff, shield), ~420 entrées, 2 armes/palier 20→90. Versions **`Holy -`** lv90 (mats Night Core, Crimson Diamond…). ⚠️ les types ne sont pas labellés — déduits des noms | ✅ |
| [Armour Lv23-88](C/Armour_Lv23-88.md) | Sets d'armure craftés : **13 paliers** (23/33/43/48/53/58/60/63/68/73/78/83/88) × 3 types (HEAVY/LIGHT/CLOTH) × 2 sets × 5 pièces = 78 sets, 390 pièces. Noms de sets complets (Iron Flame→Unyielding Warlord, Cleaver→Anathema, Manic Mage→Divine Asylum) | ✅ |
| [Lv65-75 Weps](C/Lv65-75_Weps.md) | Armes éveillées 65 (16) & 75 (17, +Pike) : livre chez Librarian Conan (Aven, or 1 875-4 125 g), blueprint de fame 16 000/36 000 (9 factions, PNJ nommés), items Jarroda en CCM, médailles d'expédition (Diana/Grace), drops de boss de trials nommés | ✅ |
| [Lv70-80 Awaken Sets](C/Lv70-80_Awaken_Sets.md) | **6 sets éveillés** 70/80 (King of Eternity, Phase Invader, Canis Stalker, Fantasy Frontier, Aven's Light, Word Storm) : pièces de base (trials 80 ou arène H.Stars chez Tabell), scrolls (Bucco), mats de fame 48 000, CCM Jarroda, boss drops nommés | ✅ |
| [Partial Enchants Lv1-8](C/Partial_Enchants_Lv1-8.md) | Enchants partiels par slot (HEAD/BODY/WAIST/HAND/LEG/1H/2H/OFF-HAND, 34 enchants lv1-8) : sources = bosses de trials nommés, chests I-VII, armour boxes, zone jars, DoD 90, Dragon Trials (lv8) ; table d'upgrade tiers 6→14 (minerai/bois) | ✅ |
| [Lv95 Awaken Sets](C/Lv95_Awaken_Sets.md) | 11 sets éveillés 95 (Elemental Guardian, Bahemoth, Dragon Lord, Flame Cerberus, Evil Necromancer, Thunder Hound, Tempest Enchanter, Voodoo Leader, Infernal King, Divine Lord, Spell Masters) : pièces « Broken », Dimension Soul Token, boss des Dimension Trials | `[>90]` |
| [Lv100 Awaken Weps](C/Lv100_Awaken_Weps.md) | 26 armes éveillées 100 (paires par livre `Legendary <Type> Book` chez Caresha, 6 000 g) : mats CCM, Cass Trial Coins, fame 50 000, boss 100+ (Solaris, Katus, Azula, Zigbar…) | `[>90]` |
| [Lv110 Awaken Sets](C/Lv110_Awaken_Sets.md) | 11 sets éveillés 110 (familles « dragon ») : scrolls Caresha 3 000 g, fame 70 000, CCM, D.T. Coins (Zigglet), Monster Energy Fossil, boss Dragon Trials | `[>90]` |

## D/ — Donjons

**Conventions des feuilles Trials** : le 1er item sous chaque boss = son
trophée ; `CLASS DROP` = classe destinataire ; `MISC` = Safety Stone
(NT), Eden Crystal (NT), scrolls Gleaming, Honor Medals. Enchants par
palier : 50/55 = costumes Lost City ; 60 = Lv1 ; 65 = Lv2 ; S-Trials/70
= Lv3 ; 75 = Formula Lv4 ; 80 = Formula Lv5 ; 85 = Lv6.

| Fichier | Contenu | Périmètre |
|---|---|---|
| [Trials Lv50,55,60,65](D/Trials_Lv50_55_60_65.md) | Trials classiques : Vileshark HQ, Ulta Hall, Angor, Fort Verdure (50) ; Veninfang, Baltaroi Core, Shale Cavern, Tasos (55) ; Crimson, Century, Manor, Morticora, Eroda (60) ; Malice Palace, Slain, Mayor's Dream, Branda Root (65). Trophée + drops par boss + blueprints (sets Star Frost/Giantslayer/Spectral Pinion/Moonlight Simmer/Zues's Rage/Steel Faith au 65) | ✅ |
| [S-Trials Lv50,55,60](D/S-Trials_Lv50_55_60.md) | Versions « S » des mêmes trials (trophées = `Blueprint: X-S` à crafter ; enchants Lv3 ; armes orange BP : Earth Shock, Olympus Codex, Kiku-Ichimonji…) | ✅ |
| [Trials Lv70,75,80,85](D/Trials_Lv70_75_80_85.md) | Sturmfrau, Skleros, Federal Armory, Basel Border (70) ; Colossal Cauldron, Windsnap Lair, Gristle Prison, Century Abyss (75, Luminary présent) ; Guillotine, Rage Cape, Rose Temple, Vilespire Temple (80, **hidden kings** : Guillotine King, Angry Waves, Rose Temple — classe Mimic) ; Tranquil Hill, Uncharted Cave (85). Sets : Warlord Soul/Spirit Melody/Angel Crime/Honor Battle/Forest Violence/Hellfire King (70) ; Lion's Roar/Eternal Gathering/Skyquake/Prosperous Phoenix/Sacred Light Assault/Star Frost (75) ; King of Eternity/Purified Virtue/Regal Battle Bear/Predator Territory/Furious Quake Wolf/Summoned Spirit (80) ; Guardian's Intent/Bloodline Spike/Meteoric/Hymn of Destruction/Glyph Power/Healing Breath (85) | ✅ |
| [Sky Tower](D/Sky_Tower.md) | Glyphes/gemmes d'équipement par étage (HAND, LEG=ét.20, WAIST=30, HEAD=40, BODY=50, SHIELD/1H/2H=70) : recette = `<Nom> Essence ×3 + Demon Crystal ×75 + gemme raciale Lv4-5`. Inclut mats d'éveil d'armes 75 (Destructive Imprint, Marks of Magic, Tenacious Gem, Moonglow Diamond, Gilded Alloy) | ✅ |
| [Temple Arena](D/Temple_Arena.md) | Arène PvE : 9 trophées de boss (Galshira, Krona, Akartis, Hirtok, Serindal, Rylon, Khodran, Vespid, Largoth — Res+5/stat, proc −15 res), versions améliorées ×30 Temple Arena Medals, quêtes journalières rotatives | ✅ |
| [Dreadlore Lab](D/Dreadlore_Lab.md) | Labo de Réno : glyphes 75/85 par slot en 5 rounds : `<Nom> Essence ×3 + Mini Reactor ×75 + gemme raciale Lv5-6` ; bouclier/armes 80 via `Champion … Essence` | ✅ |
| [Daylight Forest](D/Daylight_Forest.md) | Gemmes d'équipement Lv8 : `Great Armor/Weapon Essence ×3 + Daylight Crystal ×75 + gemme raciale Lv8` ; noms mythologiques (Aceso, Tyr, Hel, Odin, Loki…) | ✅ |
| [AT_DF](D/AT_DF.md) | Angelic Temple & Demonic Fortress (donjons jumeaux ~90, custom) : 19 armes angéliques (Empty/Angelic Parchment + Poison Core), pierres de bénédiction/malédiction avec conversion croisée, 10 boss (Lila, Celeste, Rachel, Sally, Sammy / Xinos, Nimir, Alice, Rhexnos, Syntha), 5 combos trophée 2-pièces AT+DF | ✅ |
| [4ToD](D/4ToD.md) | 4 Trials of the Dimension : 20 gemmes par slot (`Gem Extract ×3 + Ether Crystal ×75 + gemme raciale Lv7`) + 12 trophées en 6 paires (boss VL/DD/BTS améliorés, combos STR/INT/WIS/HP +10 %) | ✅ |
| [Dream of Destruction 90 & 95](D/Dream_of_Destruction_90_and_95.md) | Rotation hebdo par donjon : breakpoints `0/10` (trophée `Holy - X` + enchant armure Lv7 + pool armes Unidentified) et `0/3` (enchant arme Lv7). Section 90 = tous les donjons régionaux 8-60 ; section 95 = trials 60-70, armes nommées 95 + Viroona Godstone | `[~90]` |
| [Guardian Colosseum](D/Guardian_Colosseum.md) | Gemmes de skill gardien par classe (×60 Guardian Fragments) + drops (Abyssal Soul, EXP Shell, potions Reality, pet gear). Brackets 80/85/90 OK ; classes/gemmes 95-100 (Angel of Justice→Wyvern Sovereign) hors | `[~90]` |
| [Elemental's Battlefield](D/Elemental_s_Battlefield.md) | Custom Vendetta : 4 trophées (×120 Ocean Elemental Stone), 4 reliques de ville de guilde + 6 fusions (×100-200 Flame Elemental Stone), 6 pets achetables (prières + skills détaillés). Niveau non indiqué | `[?]` |
| [Trials Lv95](D/Trials_Lv95.md) | 4 Dimension Trials (Kahlo Tower, Graylin, Meteor Crater, Rheutear) : 16 boss, 9 sets d'armure 95, ~45 armes de classe, combos world boss (Ladon, Blisterbreeze, Zorna…) | `[>90]` |
| [Trials Lv100](D/Trials_Lv100.md) | 4 trials (Camodor, Dark Tide Cave, Vingot Lab, Devastation Dimension) : 16 boss, 11 sets 100, armes 100, craft Honor Medals + H.Star / Time Core | `[>90]` |
| [Trials Lv110](D/Trials_Lv110.md) | 4 Dragon Trials (Crypticone, Delphi Temple, Dragon's Keep, Palace of Dreams) : 16 boss, blueprints sets dragon, trade Zig (Dragon Trial Coins, Boss Gemstones) | `[>90]` |
| [Trials Lv130](D/Trials_Lv130.md) | 4 trials (Duskmire Crypt, Ruined Bloom, Crystalized Necropolis, Stormcliff Keep) : 16 boss, sets Blessed Mana/Swift Leather/Fiery via coffres, Eden Crystal NT | `[>90]` |
| [Abyss](D/Abyss.md) | ~115 : 5 boss (Sakuya, Esh-Baal, Leonic, Willow One-Eye, Ferelwing), 10 trophées, 14 sets lv115, monnaie Abyssal/Fragmented Abyssal Soul, pets Esh-Baal/Oriel | `[>90]` |
| [Deep Abyss](D/Deep_Abyss.md) | ~125 : 3 boss (Electra, Spike, Glacieron), 15 sets 125 + 15 sets Awaken (Armor Tablets), essences Enhanced | `[>90]` |
| [Celestial Corridor](D/Celestial_Corridor.md) | ~117 : 6 sets zodiaque (Aries, Leo, Sagittarius, Libra, Aquarius, Gemini), combos 6-pièces à statut signature, craft tokens + Zodiac Sparkles | `[>90]` |
| [Celestial Corridor II](D/Celestial_Corridor_II.md) | ~127 : 6 sets zodiaque (Taurus, Scorpio, Cancer, Capricorn, Pisces, Virgo), Spirit Tokens/Sparkles | `[>90]` |
| [Haven of Oblivion](D/Haven_of_Oblivion.md) | HoO 95+ : 11 badges/trophées (Nerokas, Dana, Alice, Cale…), 23 blueprints d'accessoires d'armes (essences thématiques + Blent Essence), 3 pets | `[>90]` |
| [Lv98 GoP](D/Lv98_GoP.md) | Gates of Pandemonium 98 : 6 boss (Zigbar, Hamon/Kur'Zakk/Hakaar, Ladon, Lucchese, Cerberus, Asmodeus), 10 combos trophée/cape + anneau/collier, craft 7 % + Gate Key ×10 | `[>90]` |
| [Lv110 Dragonridge Sanctuary](D/Lv110_Dragonridge_Sanctuary.md) | 5 boss (Nasci, Bushi, Draconem, Eis/Frost Queen, Thunor), 30 items en combos cape+trophée / collier+anneaux | `[>90]` |
| [Viridian Gorge](D/Viridian_Gorge.md) | Échange de gemmes 110-125 : ~72 gemmes arme/armure (gemmes raciales Lv9/10 + Azure Crystal ×250-350, coût Viridian Crystal Fragment) | `[>90]` |

## BM/ — Battle Maps (zones de combat)

| Fichier | Contenu | Périmètre |
|---|---|---|
| [VL_DD_BTS](BM/VL_DD_BTS.md) | Vingot Lab (72), Devastation Dimension (75), Battle of Time & Space (83) : 8 trophées VL (Blade, Kimba, Isolda, Corken, Ultimate Beast, Kequa, Marquis Saad, Maul — paires pour combo 2p HP+1629/res) + 4 BTS (Sonyay, Guff, Sam, Petra) + **18 skills lv72 à 9 000 Merits** (doublon partiel de `M/Lv72_Class_Skills.md` — valeurs différentes, à trancher) | ✅ |
| [Dimension of Souls](BM/Dimension_of_Souls.md) | Battle map 95 : 6 colliers, 6 anneaux, 6 capes, 4 montures (Ostrich/Alpaca/Hyena +90 %, Chubby Bird +75 %), bestiaire 93-95 (Wormmy, Soul King, Kahl Mund, Harkat), potions Enhanced Lv7 | `[>90]` |
| [Demonic Valley](BM/Demonic_Valley.md) | ~100-125 : 4 trophées (Banoleth, Selena, Triton, Azolt), 10 capes Zari (×35 Polished Emerald), potions Lv10 + échanges émeraudes | `[>90]` |
| [Dragonside Ridge](BM/Dragonside_Ridge.md) | 108-110 : rewards (noms absents du CSV — effets seuls : 6 colliers/6 anneaux/6 capes), bestiaire 108-110 (Ko'Harumi, Hak'Zurk, Ka'Nellos, Gal'Lunaburakku) | `[>90]` |

## G/ — Gemmes

| Fichier | Contenu | Périmètre |
|---|---|---|
| [Arcane Box Lv95/105/110](G/Arcane_Box_Lv95_105_110.md) | Gemmes Arcane Box élémentaires : 5 familles (Courage/Intuition/Nimble/Compassion/Fortune) × 6 éléments × 4 variantes, recette `<Famille> <Élément> Lv7/8/9 + gemme base`. ⚠️ défauts de données (section Compassion répétée, typos « Lv89 », « Pus ») | `[>90]` |

## M/ — Misc / mécaniques

| Fichier | Contenu | Périmètre |
|---|---|---|
| [Arcane Box](M/Arcane_Box.md) | **24 recettes secrètes** de la Boîte Mystique : armes fun lv1-71 (Defibrillation Hammer, Invisible Katana, Watermelon Slicesword, Encyclopedia Edenica…) + consommables (Alpaca Transformation Crystal, Gravitational Stone, nourriture lv10-70) — chacune avec stats + 3-5 matériaux | ✅ |
| [Armour Boxes](M/Armour_Boxes.md) | Boîtes d'armure des élites/coffres de zone **lv43-83** : nom de la boîte, sets/pièces oranges contenus, enchants exclusifs (Violence/Devil Soul/Blood Kiss/Crazy Devil/Gun/Shield Lv1-6), localisation — à croiser avec `Phoenix_Maps.md` (mêmes boîtes via phœnix) | ✅ |
| [Aven Fame Vendors](M/Aven_Fame_Vendors.md) | ⚠️ **ANOMALIE** : le fichier contient le bloc Intro du classeur, **pas** les vendeurs. La vraie table des 9 factions × 4 paliers de fame est dans `misc/Useful_Info.md` (section AVEN FAME) | ✅ (voir misc) |
| [Class Certificates](M/Class_Certificates.md) | 19 classes × 6 certificats (3 types × paliers I/II) avec effets + combos par classe + **combos génériques par couleur** (jaune/orange/vert/bleu/violet) | ✅ |
| [Class Expertise](M/Class_Expertise.md) | Bonus d'expertise par niveau de classe 5→130 : bloc 1 classes de base (⚠️ en-tête perdu — ordre des colonnes à reconstituer), bloc 2 **42 classes éveillées** nommées | `[~90]` (lignes 95-130) |
| [Dyes](M/Dyes.md) | ~148 teintures par famille de couleur + Starlight 1-11 (noms seuls — colonne couleur vide après conversion ; certaines exclusives Vendetta) | ✅ |
| [Furniture Guide WIP](M/Furniture_Guide_WIP.md) | Meubles villa : ~50 drops in-game (mobs/cartes/donjons/Territory Chest), 19 items Wee Rowan, ~14 innkeepers, ~75 Item Mall, ~80 event Vendetta ; stats Luxury/Comfort/Rarity | `[~90]` (section event) |
| [Gathering Tools](M/Gathering_Tools.md) | Tables de loot de récolte par niveau 30/45/60/70/85 (champignon/herbe/minerai/pierre/fossile/lumière) ; lv95/100/110 hors (+ pêche) | `[~90]` |
| [Guardian Skills](M/Guardian_Skills.md) | ~55 Guardian skills par classe éveillée (valeurs souvent `+xxx` placeholders) ; **mutuellement exclusifs avec Holy Skills** | ✅ |
| [Holy Skills](M/Holy_Skills.md) | ~55 Holy skills lv90 **chiffrées** (Battle Cry, Rejuvenation = rez 100 % en combat, Shadow Escape…). Note : Lv90 = Lv95 identiques ; gemmes >100 = hors | `[~90]` |
| [Honor Medals & CCM](M/Honor_Medals_and_CCM.md) | Drops de Honor Medals/CCM/Safety Stones par donjon et par boss (classe de la médaille) : cartes 56-85 + trials Aven 60-85 ; cartes 92-102 + Mimic hors | `[~90]` |
| [Legendary Achievements](M/Legendary_Achievements.md) | Hauts faits légendaires 1-90 : 3 tables (stats — trophées/rares/ressources/craft/armure ; armes par type +5-25 % ; montures & costumes : Puma Enflammé, alpagas, Tortue de Cristal, **Fantasy Dragon**, Mirage Dragons). 3 hauts faits 95 hors | `[~90]` |
| [Lv72 Class Skills](M/Lv72_Class_Skills.md) | 17 skills lv72 (1/classe, Ella à Devastation Realm) avec valeurs chiffrées — croiser `BM/VL_DD_BTS.md` (mêmes skills, valeurs parfois différentes) | ✅ |
| [Monster Stones](M/Monster_Stones.md) | Pierres de monstre (craft Zumi) : 9 types × 5 paliers (Basic→Lv4, res +1→+9 + proc 5 %), coûts en Basic/elemental Enchant Crystals ; 1 seule équipable | ✅ |
| [Pet Prays](M/Pet_Prays.md) | ~110 prières de familiers : ~60 Primes (EXP/CP +15-35 %, stats, maîtrises d'armes, cœurs de boss 95) + ~50 Legendaries (Extra EXP/CP, **Super Looter drop+10-35 %**, Master of X…). Matrice de catégories perdue à la conversion | ✅ |
| [Pet Skills](M/Pet_Skills.md) | ~130 skills de familiers par type (Phys ~50, Pet buffs ~15, Holy/Ice/Fire/Lightning/Nature/Dark) : dégâts, DoTs, AoE, CC, debuffs de résistance | ✅ |
| [Phoenix Maps](M/Phoenix_Maps.md) | Coordonnées des armor boxes via Baby Phoenix (Death Valley→Devastation Realm) et Green Bean Phoenix (Devastation→Meeryasha) | ✅ |
| [Racial Passive](M/Racial_Passive.md) | Passifs de classe + raciaux : toutes les classes de base **et éveillées** × 5 races (Human/Halfkin/Zumi/Anuran/Ursun) | ✅ |
| [Racial Skills](M/Racial_Skills.md) | 50 skills raciaux actifs : 5 races × 5 couleurs de classe × 2 skills (lv45+60), effets détaillés | ✅ |
| [Titles](M/Titles.md) | ~300 titres : hauts faits légendaires (~95), titres généraux par carte (+5→+49 stats croissants), ~60 titres Vendetta (events/coins) hors, 8 titres de livre (+3 % res par faction) | `[~90]` |

## R/ — Crafts raciaux

| Fichier | Contenu | Périmètre |
|---|---|---|
| [Human](R/Human.md) | Gemmes raciales Humain : 7 familles (STR/AGI/INT/WIS/LCK/Claret Gore/Mana) × 4 variantes × paliers 1→10, matériaux par palier. **Fin hors** : « Level 95 Racial Crafting » + « Level 105 » = gemmes élémentaires combinées Lv7-9 | `[~90]` |
| [Bear](R/Bear.md) | Glyphes Ursun : 5 glyphes Mimic génériques + **5 glyphes/classe de base** (stat +11-29 + effet sur un sort, mats nommés) + 2 glyphes/classe éveillée (35 classes awakened — contenu Vendetta) | `[~90]` |
| [Zumi_Halfkin_Frog](R/Zumi_Halfkin_Frog.md) | 3 crafts en colonnes : **Zumi** ingénierie (gadgets/montures/consommables paliers 30-110 : Steam Locomotive, Minibot 8000, Fourth Dimension Bag, Safety Stones…) ; **Halfkin** étoffes de trophée Lv1-16 (Guardian/Attacker/Sniper/Healer/Magic Weaver, combos TE 95+) ; **Anuran** alchimie (potions, teintures, Elf Sculptures, charmes EXP/CP, Baby Phoenix, Pet Skill Token). Paliers 95/100/110 hors | `[~90]` |

## T/ — Trophées

| Fichier | Contenu | Périmètre |
|---|---|---|
| [Trophies](T/Trophies.md) | **Référence complète de tous les trophées niv. 1-123**, 5 sections : overworld (par zone, dont trophées « durée de skill » par classe), map dungeons (sets 2p/3p à partir de Colossal Cauldron 62), Trials (50-130, sets par arme), Dream of Destruction (versions `Holy -` 90/95), Miscellaneous (fame chests, pets FWR, Elf Sculptures). Zones/donjons/trials >90 = hors | `[~90]` |

## misc/ — feuilles sans préfixe

| Fichier | Contenu |
|---|---|
| [Intro](misc/Intro.md) | Index du classeur : horaires GvG/Territory War/Guild Arena, boost CP/Loot 500 %, rotations d'autels, maintenance jeudi, description de chaque feuille |
| [Lvling & Farming Guides](misc/Lvling_and_Farming_Guides.md) | Route de leveling 1-130 par donjons, quêtes « V » (meteors Wind/Ice/Fire/Earth), farms CCM/EP/or/EC, timers de world boss, re-roll d'armes (Vendetta) — sections 95+ hors |
| [Useful Info](misc/Useful_Info.md) | Paliers de guilde 1-12 (coûts, déblocages : ville niv.3, territoires niv.4, entrepôts, effectif 200→350), **vraie table AVEN FAME** (9 factions × paliers 100-6 000), buffs utiles (PoD, meteors, star stones, couple/spouse), consommables & items spéciaux |

## Contenu hors périmètre (niv. 95+ / Vendetta) — récapitulatif

Conservé pour référence future, **à ne pas implémenter** :

- **Fichiers entiers `[>90]`** (16) : `C/Lv95_Awaken_Sets`,
  `C/Lv100_Awaken_Weps`, `C/Lv110_Awaken_Sets`, `D/Trials_Lv95/100/110/
  130`, `D/Abyss`, `D/Deep_Abyss`, `D/Celestial_Corridor` & `II`,
  `D/Haven_of_Oblivion`, `D/Lv98_GoP`, `D/Lv110_Dragonridge_Sanctuary`,
  `D/Viridian_Gorge`, `G/Arcane_Box_Lv95_105_110`, `BM/Dimension_of_
  Souls`, `BM/Demonic_Valley`, `BM/Dragonside_Ridge`.
- **Sections `[~90]`** : DoD 95 (`Dream_of_Destruction…`), Colosseum
  95/100, crafts raciaux paliers 95/100/110 (`R/*`), lignes 95+ de
  `Gathering_Tools`, `Class_Expertise`, `Titles`, `Legendary_
  Achievements`, `Furniture_Guide`, `Honor_Medals`, `Holy_Skills`
  (gemmes >100), `T/Trophies` (zones/trials >90), `Lvling` (routes
  95-130, CCM usage 95+, re-roll).
- **Éléments Vendetta propres** : classes custom (Mimic, Agent,
  Inquisitor, Luminary, Wyvern Lord) et éveillées (42 dans
  `Class_Expertise`), monnaies custom (Abyssal Soul, Dragon Trial
  Coins, Boss Gemstones, Zodiac/Spirit Tokens, Elemental Stones,
  Ether Crystal, Gem Extract, Viroona Godstone, Constellation
  Stone/Tablet, Gate Key of Pandemonium, D.T. Coins, H.Stars, Cass
  Trial Coins), mécaniques custom (awaken tablets, breakpoint loot
  tiers, enchant boxes Lv8-9, statues Vendetta, titres events).
- **`[?]`** : `D/Elemental_s_Battlefield` (custom, niveau non indiqué).

## Incohérences et pièges relevés

- `M/Aven_Fame_Vendors.md` = **duplicata d'Intro** — données réelles
  dans `misc/Useful_Info.md` (sans noms de PNJ ni localisations ;
  compléter via wiki-en).
- **Skills lv72** : `BM/VL_DD_BTS.md` et `M/Lv72_Class_Skills.md` =
  même liste, valeurs différentes (ex. Heart Attack DoT) — Vendetta a
  probablement rééquilibré ; garder les deux.
- **Trophées VL** : `T/Trophies.md` et `BM/VL_DD_BTS.md` diffèrent
  (Maul HP +1086 vs +1066).
- `DoD 95` : la 2e entrée « Colossaul Cauldron » est en réalité
  **Sturmfrau** ; « Hank's Calibration Tool » = Hans (Veninfang).
- `R/Human.md` : ligne INT Amplifier T3 corrompue (`:3`) ;
  `G/Arcane_Box` : section Compassion dupliquée sur les éléments.
- `Class_Expertise` : en-tête du bloc classes de base perdu ;
  `Pet_Prays`/`Pet_Skills` : colonnes de catégories vides ;
  `Dragonside_Ridge` : noms d'items absents (effets seuls).
- `Mimic`/`Agent`/`Inquisitor`/`Luminary`/`Wyvern Lord` = classes
  Vendetta : présentes dans des tables taguées « in » (certificats,
  raciaux, medals) — filtrer à l'usage.
