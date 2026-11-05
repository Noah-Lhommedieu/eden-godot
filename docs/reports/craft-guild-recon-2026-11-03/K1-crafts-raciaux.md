# RAPPORT K1 — Crafts raciaux (LifeSkill) des 5 races — métier

> Périmètre : mécanique joueur des artisanats raciaux. Pivot confirmé : **craft racial = `LifeSkill`** (`docs/reports/craft-guild-recon-2026-11-03/00-PLAN.md:23-24`). Serveur C# déjà implémenté côté wire : `eden_server/server/src/Eden.Zone/ZoneEntryFlow.LifeSkill.cs`.

---

## 1. Modèle commun — vérifié au niveau natif/données

### 1.1 Accès et cadre général
- [VERIFIED] L'artisanat racial se débloque au **niveau de personnage 30**, s'ouvre via une **icône dédiée dans l'inventaire [B]** — pas de NPC, pas de zone obligatoire (`knowledge/guides-fr/pages/artisanat.md:5`, `artisanat-potions-anuran.md:6`). Check natif en dur : `st.Level < 0x1e` → refus (`ZoneEntryFlow.LifeSkill.cs:113`, `:218`, `:401`).
- [VERIFIED] Chaque race a **son** groupe métier, verrouillé côté serveur par `CheckLifeSkillType` (nibble `player_characters.life_skill`, `ZoneEntryFlow.LifeSkill.cs:615-627`) :
  - **groupe 1 = Humain** (analyse joaillerie) → nibbles {0x00,0x10}
  - **groupe 2 = Zumi** (ingénierie) → {0x20,0x30}
  - **groupe 3 = Anuran** (alchimie) → {0x40,0x50}
  - **groupe 4 = Halfkin** (broderie) → {0x80,0x90}
  - **groupe 5 = Ursun** (glyphes) → {0x60,0x70}
  (mapping groupe↔race recoupé par inspection des recettes de base dans `corpus/fr-full/data/db/c_lifeskill.ini` : ligne 10 `27|2|36946` → 5 résultats patchs Zumi ; ligne 11 `28|3|36974` → potions Anuran ; ligne 12 `29|5|20192` → runes Ursun ; ligne 13 `30|4|20288` → étoffes Halfkin ; lignes 2-9 `11|1|…` → analyse gemmes Humain.)
- [VERIFIED] Le pré-requis d'ouverture côté wire : `ShopOpType == 22` (op fenêtre natif `CLifeSkillOp` IsA 0x4c, émis via `ClientEnterStrengthen`) (`ZoneEntryFlow.LifeSkill.cs:74`, commentaire :16-17 ; `eden_server/docs/temp-plans/2025-06-09-lifeskill-metiers.md:24-27`).
- [VERIFIED] Les matériaux d'artisanat racial viennent **exclusivement de la récolte de ville de guilde** (mine / potager / autel-source), jamais des compos classiques forgeron/fermier/invocateur (`artisanat.md:12`).

### 1.2 Protocole (déjà implémenté serveur)
| NCID | Sens | Rôle | Source |
|---|---|---|---|
| 213 | C→S | QueryLifeSkill → 543 | `protocol-map.md:407` ; `ZoneEntryFlow.LifeSkill.cs:52-64` |
| 543 | S→C | `[s16 life_skill][u16 n][u16 ids][u16 n][u32 exps][u16 n][u32 dues]` | `ZoneEntryFlow.LifeSkill.cs:44-50` |
| 214 | C→S | `[u8 op][u16 recipeId][s16 bag][s16 slot]` ; op : 0=analyse, 1/2=produce, 3=forget, 4=apperception, 5=levelup | `ZoneEntryFlow.LifeSkill.cs:71-101` |
| 433 | S→C | cast `[s16 0x68 prod\|0x69 analyse][s16 0x32][s16 0]` | `ZoneEntryFlow.LifeSkill.cs:162-167` |
| 434 | S→C | annulation `[s16 0x68]` | `ZoneEntryFlow.LifeSkill.cs:201-209` |
| 435 | S→C | résultat `[s16 0x68][s16 1][0×4]` | `ZoneEntryFlow.LifeSkill.cs:275-277` |
| 544 | S→C | update `[u8 op][u16 id][u32 val]` : 0=exp, 1=apprise, 2=oubliée, 3=remplacée | `ZoneEntryFlow.LifeSkill.cs:19-23`, `:431-434` |
| 316 | S→C | textes (erreurs + gain exp `0xf99 "%s,%d"`) | `ZoneEntryFlow.LifeSkill.cs:429-430` |

- [VERIFIED] Chaque production = **cast unique de 5 s** (`LifeSkillCastUnits = 0x32`, ×100 ms, enchant natif `LifeSkillCastID` id 211) puis résolution différée `EndLifeSkill` (`ZoneEntryFlow.LifeSkill.cs:28-30`, `:158-174`). *NB : le plan ancien dit « 2 s » (`2025-06-09-lifeskill-metiers.md:30-31`) — le code actuel fait foi.*
- [VERIFIED] Erreurs texte : `0xeb1` or insuffisant (`:117-121`), `0x9f4` matériau manquant (`:353-358`), `0x4f9` sac plein (`:335-341`), `0xfe1` niveau insuffisant (`:222-225`), `0xf84/0xf83` maîtrise non atteinte (`:457`,`:525`), `0xfe3` résultat déjà appris (`:462-470`), `0xf63` objet non analysable (`:377-380`), `0xfe4` niveau d'objet insuffisant (`:384-388`).

### 1.3 La boucle métier (barre d'XP → apprendre → oublier)
[VERIFIED] D'après `c_lifeskill.ini` + `LifeSkillParser.cs:13-27` + guides FR :
- Recette « **matériau de base** » : produisible à l'infini (opType 1 = sans exp, opType 2 = avec exp). `gain = ExpBase − ExpVar + rand(2·ExpVar+1)` clampé à `ExpCap` (`ZoneEntryFlow.LifeSkill.cs:405-414`). Au cap : texte « 已熟練 »/`FullExpText` + bouton **領悟 « Apprendre/Apperception »** (`lifeskillitem.layout:33-45`).
- **Groupes 2-5** : le tirage pondéré (`Apperception`, pick `lower_bound` sur poids cumulés) apprend **1 recette produit parmi ~5** du palier (544 op1) ; `LevelUp` apprend la recette `NextRecipe` = **matériau de base du palier suivant** (544 op1 ou op3 si groupe 1) (`ZoneEntryFlow.LifeSkill.cs:443-553`).
- **Oublier** (op3) : produit appris supprimé (544 op2) **et l'exp du précurseur remise à 0** → on re-maîtrise le matériau de base pour re-tenter le tirage (544 op0 resync) (`ZoneEntryFlow.LifeSkill.cs:560-602`). Confirme le FR : « on peut réinitialiser chaque barre d'expérience pour obtenir un objet différent » (`artisanat-potions-anuran.md:6` ; `Tissage_(Ezelins).md:14`).
- [VERIFIED] Coût : `recipe.Gold` débité par production (`GainGold(-gold)`, `ZoneEntryFlow.LifeSkill.cs:251-253`). Coûts FR exemplaires : bobine coton 24 pa, laine 52 pa, lin 1,37 po, soie 2,90 po (`Tissage_(Ezelins).md:29,41,53,65`) ; maîtrises : coton/laine 200 pts, lin/soie 350 pts (`:30,42,54,66`) ; rune mage = cap 50 (ini `29|5|…`: ExpBase 15, ExpVar 10, **ExpCap 50**, Gold 50 — `c_lifeskill.ini` ligne 12) ; wiki FR cite rune → « 5 à 30 pts d'exp par craft, objectif 50 puis 100 » (`Décrypteur_de_Glyphes_(Ursuns).md:8,34,37`).
- [VERIFIED] **Pas de taux d'échec** dans LifeSkill lui-même — la production réussit toujours si validations OK. L'aléatoire porte sur *quelle* recette produit est apprise, pas sur le craft. (Les échecs concernent le craft d'équipement via NPC de VDG, ex. `decorations-guilde.md:6` Alchemist « Failure probability decreased by 10 % ».)
- [VERIFIED] Recettes apprises persistées en `player_life_skill` (`life_skill_id, exp, due_date`) (`2025-06-09-lifeskill-metiers.md:23` ; `LifeSkillRepository.cs`).

### 1.4 UI native (layouts corpus, [VERIFIED])
- `corpus/fr-full/uidata/layouts/engineeringstudy.layout` — `Root/EngineeringStudy`, FrameWindow ≈412×640 rel. (l.9) : icône recette `BG/Slot`, `InfoText`, `HelpBtn`, liste scrollable `Box/ScrollPane` (l.132-142), 3 slots matériaux `StuffItem0-2`+`ItemSlot0-2`+`ItemNumText0-2` « 99/99 » (l.50-128), `DemendInfo` « 需求材料 », `ManufactureExpense` « 製作費用 », `CreateBtn` « 製作 » (l.153-171).
- `jewelrystudy.layout` — `Root/JewlryStudy` : **deux zones de slot** — `BG/Slot` (analyse, avec `AnalysisItemNum`) et `BG1..BG3` (création), boutons **`StudyBtn`** (analyser) et **`CreateBtn`** (produire), `StudyEditBox`/`CreateEditBox` (qté), `GoldText/SilverText` (coût), `LevelInfo` (l.34-218). → confirme la double fonction analyse/production du groupe 1.
- `lifeskillitem.layout` — ligne de recette : `ItemSlot`, `ItemDeatilText`, `ExpBar` (ProgressBar step 0.01), **`ForgetBtn` « 遺忘 »**, **`LvUpBtn` « 領悟 »**, `FullExpText` « 已熟練 » rouge, `ExpText` « 9999/9999 » (l.9-63).
- `jobglyph.layout`/`jobglyph_na.layout` — roue des glyphes équipés ; atlas `JobGlyph.png` = « cercle de glyphes doré à **15 emplacements** » (`docs/reference/corpus/ui-atlas-map.md:65` ; `layout-impl-map.md:183-184`).

---

## 2. Récolte de ville de guilde (source unique des compos)

- [VERIFIED] Zones : **mine** (pioche), **potager/ferme** (gants de jardinage), **autel/source d'énergie** (boule de cristal). Présentes dans **toute** VDG quel que soit le niveau de guilde (`artisanat.md:31` ; `Ville_de_Guilde.md:42`).
- [VERIFIED] Outils blancs vendus à **Aven (X638.Y508)** par 3 PNJ : **Phio** (pioches), **Ri** (gants), **Aidey** (boules) — niveaux 30/45/60/70/85/95 à 2/5/10/15/20/25 PO, durabilité 180 ≈ 30 min, non-échangeables, liés au niveau perso (`artisanat.md:15-24`). Qualité **verte** (durabilité 540 ≈ 1 h 30) : coffres aléatoires chez **Hanga** (même spot) contre **améthystes d'Ezur** — peuvent aussi donner compos vertes/oranges 45-60 ou dorées (`:26`). Qualité **bleue « parfaite »** (1440 ≈ 4 h, échangeable mais pas à l'HdV) via autel de cristal/promos (`:27-28`).
- [VERIFIED] Contraintes récolte : **personnage immobilisé**, action interrompue au déplacement ; pas de messagerie/HdV en parallèle ; groupage raid autorisé ; outils empilables consommés en chaîne (`artisanat.md:36-37`). Améthystes d'Ezur (nv30) récoltables avec n'importe quel outil (`:40`).
- [VERIFIED] Table des compos par palier de récolte — `wiki-tables/M/Gathering_Tools.md:5-16` :
  - **Ferme/Gant** : Soul Mushroom, Serene Blue Herb (30) → Sunset Mushroom, Eden Lavender, Eden Spring Fern (45) → Watervine Mushroom, Golden Mugwort, Goddess Red Fern (60) → Branch Hypha, Gilded Flower Fungus, Jasmine Fungus (70) → Crimson Bacteria, Yellow Ginseng, Azure Dragon Ginseng (85) → Poison Bacteria, Eden Stem, Golden Fern (95) → Dried Weed, Wilted Bloom, Moonblossom (100) → Papyrus, Miscanthus, Trachycarpus (110).
  - **Mine/Pioche** : Golden Hard Rock, Dream Slate Stone (30) → Yellow Crystal Quartz, Crystal Slate Stone, Dragon Teeth Fossil (45) → Sunset Quartz, Diamond Slate Stone, Giant Dragon Fossil (60) → Hyperpolar Pyrite, Carbon Feldspar, Moonlight Wolfram (70) → Fruit Flower Stone, Sunrise Globe, Blood Diamond (85) → Clear Crystal Rock, Amethyst Stone, Giant Bone Fossil (95) → Bone Fragments, Smooth Gemstone, Sunstone (100) → Chrysoprase, Onyx, Metal, Unakite (110).
  - **Autel/Boule** : Magic Glitter, Light Effect (30) → Magic Shine, Soul Light Effect, Disturbance Wave (45) → Ultra Light Power, Ion Energy, Disturbance Sunspot (60) → Hadron Energy, Quark Wave, Flameburst Energy (70) → Trance Generator, Oracular Fluid, Quaking Flame (85) → Crystal Energy, Emerald Wave, Disturbance Power (95) → Fizzled Shine, Glistening Effect, Stardrop (100) → Concentrated Energy, Magnetic Orb, Magnetic Effect, Supernatural Power (110).
  - **Pêche** (nouveau canal 100/110) : Goldfish, Bluefin Tuna, Carp, Golden Carp (100) ; Catfish, Bass, Eel, Aqua Shrimp (110).
  - Correspondance FR↔EN des noms : `artisanat.md:44-64` (Pépite de granite doré=Golden Hard Rock, Tuile de pierre de rêve=Dream Slate Stone, Champignon du printemps=Soul Mushroom, Eclat scintillant de mana=Magic Glitter, Source de lumière=Light Effect, etc. — noms officiels FR ère Aeria).
- [HYPOTHESIS] Les « plans de récolte (work levels) » de la tâche = le système **Work** parallèle (`CWorkManager/CWorkItem`, NCID `PlayerWork`, `00-PLAN.md:20-22` ; `eden_server/.../ZoneEntryFlow.Work.cs`, `WorkOrderService.cs`) — commandes de récolte VDG à distinguer du LifeSkill ; hors scope K1, couvert par agent K3.
- [VERIFIED] Achats en VDG **étrangère** taxés ; dans sa propre ville prix de base (`Ville_de_Guilde.md:45`). NPCs crafteurs VDG : **Alchemist/Master Alchemist** (Alchemy Lab : « accept gem inlay quests, potion-making quests, failure −10 % »), Foundry (Molding Master…), Magic Pool (Craftsman…), Farm/Mining/Altar Houses, Market House (blueprints Lv20-60) — `wiki-en/categories/decorations-guilde.md:6-7,33-34,54-70` ; `Blueprint: Alchemy Lab` = « place for producing gems and potions, work level ≤3 » (`pages-index.tsv:1454`).

---

## 3. HUMAIN — Fusion de bijoux / Joaillerie (groupe 1)

- [VERIFIED] Spécificité : le matériau de base **n'est pas** produit par la barre d'XP — les **gemmes élémentaires** se craftent chez un **PNJ alchimiste** (formules achetées) ; la barre LifeSkill sert à les **améliorer** (`artisanat-gemmes-humain.md:9`). Mode natif = **analyse** (op0, sac+slot) : consomme `Count`×item du slot ciblé (`ZoneEntryFlow.LifeSkill.cs:136-153`, `:284-310`).
- [VERIFIED] Chaque type de gemme élémentaire a **4 améliorations aléatoires** ; la maîtrise en donne une au hasard (544 op3 **remplace** la recette — c'est le `Apperception` groupe 1) ; retry = oublier/remplacer par `prevRecipe` (`artisanat-gemmes-humain.md:10` ; `ZoneEntryFlow.LifeSkill.cs:482-492`, `:574-584`).
- [VERIFIED] Familles : **rouge sang (PV)** et **mana (PM)** dès nv30 ; **FOR, AGI, INT, SAG, CHA** dès nv35 (FR `:14,47,80,113,146,179,212`). Variantes par famille : Bouclier/Visée/Persévérance/Mérite (PV-PM), Coup dur/Amplification/Invocation rapide/Vitesse ultime/Fatalité/Annihilation/Précision/Elimination magique/Diminution/Réhabilitation/Soin selon stat. Paliers Nv1→Nv6 (lvl 30/35 → 80/85) (`:14-242`).
- [VERIFIED] Les tables Vendetta `R/Human.md:5-45` étendent à **10 paliers** et ajoutent les gemmes de tour **Lv95/105** : 6 familles élémentaires (Flame/Lightning/Frost/Nature « Compassion »/Holy/Darkness) = `X Gem Lv7/8/9` + `Basic <stat> Gem` (`:46-66`) — **contenu >90 hors périmètre**.
- [VERIFIED] Gemmes spéciales (artisanat humain) : **Cristal d'aventurier** (EXP+1 %), **du maître** (PC+1 %), **arc-en-ciel** (rés. élémentaires +1 %), **chasseur de trésor** (butin +1 %) — recette : 2× **Gemme enchantée** (produit de l'alchimie anuran nv60 !) + Diamant de soleil ou Energie du trou noir + compos nv60 (`artisanat-gemmes-humain.md:245-250`). → inter-dépendance raciale confirmée.
- [VERIFIED] Pose : gemmes serties sur pièces de set/armes « au moyen de **ciseaux** » (`artisanat-gemmes-humain.md:5`) ; bonus par origine de récolte documentés `Fusion_de_Bijoux_(Humains).md:8-23` (blanc/vert/orange, stat fixe + bonus parmi 4).
- [VERIFIED] Filtre analyse : `AnalysisCheck` exige niveau de l'item ≥ `recipe.Count` (`ZoneEntryFlow.LifeSkill.cs:382-389`).

## 4. ZUMI — Ingénierie (groupe 2)

- [VERIFIED] Chaîne matériaux de base (palier → base → ~5 produits) — `R/Zumi_Halfkin_Frog.md` col. 1-5 + `artisanat-ingenierie-zumi.md` :
  - **30** Metal Patch (2 Hard Rock + 1 Dream Slate + 2 Magic Glitter) → cartes de classe ×5 (Strike/Destroyer/Sniper/Frozen/Storm Patch, buffs skill +20 % 2 h) (`R:5,7-11` ; FR `:10-15`).
  - **40** Precise Element → gadgets défensifs/utilitaires 2 j : Invisible Cape, Evasion Cloak, Rocket Boot, Electromagnetic Shield, (Wave Sensor) (`R:18-22` ; FR `:20-25`).
  - **50** Automatic Enhancement → **polymériseurs** : War Stone (1/2 h), Aven Teleport Device, Fluctuwave, Asbee Crystal (1/30 min), Eternal Coin (1/3 h) (`R:30-35` ; FR `:30-35`).
  - **60** Atomic Energy Axel → versions améliorées des gadgets 40 (`R:42-47` ; FR `:40-45`).
  - **65** Spark Board (1 **Electric Energy Metal** ← alchimie anuran !) → Steam Core → **Steam Locomotive** (monture +35 %), **Alpaca DNA Mod Formula** (monture AGM), Premium (Lucky) Safety Stones (`R:48-53` ; FR `:48,51-56`).
  - **70** Continuum Cube → Super Fertilizer (work orders +2 rendement !), Firework, Super Speed Device, Wonder Pocket, Avian Amazement/poussin galopeur (`R:54-59` ; FR `:61-66`).
  - **75** Super Battery → Eden Crystal Fragment Fuzer, Vitality Enhancer (+5 % PV/PM), Light Beam Gun, **Zumi Flying Propeller** (vol 10 min), **Super Engine Set** (monture) (`R:60-65` ; FR `:72-77`).
  - **80** Optical Sensor → Minibot 8000, Smoke Bomb, Announcement Billboard, Twist of Fate (`R:66-70` ; FR `:82-87`).
  - **85** Falling Star Diamond → Fourth Dimension Bag, Remote Control Dice, Poison Powder, Engineer Potion, Happy Powder (`R:74-79` ; FR `:92-94`).
  - **90** Lost Engineering Rack → Mouse Trap, **Arch Iron Robot I**, General Mechanical Loricae, High Quality Pet Food (`R:80-84`).
  - **95** Zumi Powder → Chance Refill Pouch, Fate Dice, Fate EXP Double Up Scroll, Map Reset Tool, Adventure Notebook (`R:86-91`) — *Vendetta*.
  - **100** Mechanical Disk → Arch Iron Robot II + 4 bottes de classe (Wizard/Warrior/Priest/Guardian Boots, base = Enhanced Rocket Boot) ; **100** Stone Fragment → **Basic Monster Stones** (Fire/Ice/Thunder/Nature/Holy/Dark : 45 fragments + 999×Bone Fragments + 500 mat t100) (`R:98-109`) — *Vendetta*.
  - **110** Heavy Stone Fragments → Basic Strike/Slash/Pierce Stones (45 frag. + 999 Chrysoprase + 500 mat t110) (`R:111-115`) — *Vendetta*.
- [VERIFIED] **Monster Stones** : pierres de résistance élémentaire/physique à équiper (« bind on equip, zumi racial craft »), upgrade Lv1→Lv4 via **Enchant Crystals** (2000/6000) + or (100/300/600/800 g) ; Lv3-4 ajoutent un proc 5 % (debuff DEF/vit/atq…) — `wiki-tables/M/Monster_Stones.md:9-53`.
- [HYPOTHESIS] Officiel (Aeria) couvre 30-85/90 ; 95/100/110 + Monster Stones + robots = extension Vendetta (paliers absents du wiki FR ère Aeria ; guides FR coupent à 85).

## 5. ANURAN — Alchimie (groupe 3)

- [VERIFIED] « il faut alimenter l'artisanat avec des compos de récolte et remplir la barre jusqu'au bout… un objet **aléatoire parmi cinq** » (`artisanat-potions-anuran.md:6`). Chaîne base → produits (`R/Zumi_Halfkin_Frog.md` col. 13-17 + `artisanat-potions-anuran.md`) :
  - **30** Muddy/Unidentified Potion → 10× potions de classe Lv1 : Tank (DEF/bloc/mal +3 %), Bourrin/War (ATQ/crit/dég +3 %), Magician, Healer/Soin (+3 % 10 min) + **Essence de couronne sup. Lv35** (+30 % réussite craft ≤35) (`R:5,7-11` ; FR `:9-21`).
  - **35** Experimental Ointment → State Removal Potion + **teintures éclatantes coiffe** A-D (Mossy, Antique, Misty Lilac, Summer Leaf) (`R:12-17` ; FR `:24-36`).
  - **40** Fluorescent Enigma → potions classe Lv2 (+5 %) + Essence Lv45 (`R:18-23` ; FR `:39-51`).
  - **45** Soul Sand Ointment → **teintures habit** A-D/F + Cortal Kiss (`R:24-29` ; FR `:54-66`).
  - **50** Demon-Sealing Piece → **Charme XP/PC I amélioré** (+30 % 1 h), sculptures lutin feu/glace (anuran-only, rés +9 & élem +25 %), Essence Lv50 (`R:30-35` ; FR `:69-81`).
  - **55** Spiritual Fire Extract → charmes XP/PC II (+60 %), sculptures nature/foudre (`R:36-41` ; FR `:84-96`).
  - **60** Glaze Essence → **Enchanted Gem** (→ artisanat humain !), **Electric Energy Metal** (→ ingénierie zumi !), sculptures ombre/lumière, Essence Lv60 (`R:42-47` ; FR `:99-111`).
  - **65** Elemental Fiber → Hidden Detection, All Purpose Potion (+16 all), Four Seasons Hibernation ×10, Princely Toad, Essence Lv65 (`R:48-53` ; FR `:114-124`).
  - **70** Mysterious Rejuvenation Essence → Anuran Energy Drink (SAG+52, 204 MP/s), Frozen In Time Capsule (invincible 2 s), sacs potions PV/PM IV (`R:54-59` ; FR `:127-139`).
  - **75** Vive Power → sacs potions IV éternels, Upgrade Skill Reset Scroll V, Baby Phoenix (`R:60-65` ; FR `:142-154`).
  - **80** Muscle Cream → **cosmétiques par race** : Lavender Perfume (humaines), Baby Powder (ézelins), Royal Bath Salt (zumis), Oak Moss Steroil (humains), Teddy Bear Shampoo (ursuns) (`R:66-73` ; FR `:157-171`).
  - **85** Slippery Slime → sacs PV/PM V, charmes XP/PC III (+85 %) (`R:74-79` ; FR `:174-186`).
  - **90** Mythic Beast Slime → Carnage Potion Bag, **Pet Skill Token**, HP/MP Little Sprite (`R:80-85` ; FR `:189-201`).
  - **95/100/110** Alchemist Powder/MK2/Potion of Slime/Concoction → Iron Skin/Swiftness/Nature's Call/Ultimate HP/MP Lv1-2, Warrior/Healer/Magician/Tank Lv3, Green Bean Phoenix, En. Awakening CP Charm I-III, State Removal Lv2 — **explicite « que sur Vendetta »** (`artisanat-potions-anuran.md:204` ; `R:86-115`).
- [VERIFIED] Teintures en jeu = système à part (clic droit teinture hors combat, 3 zones par tenue, éclatantes = 2 couleurs alternées — `wiki-fr/pages/Teintures.md:14-42`) ; ~148 teintures référencées `wiki-tables/M/Dyes.md:14-315` (note « some dyes are Vendetta exclusive » :7).

## 6. URSUN — Gravure / Décrypteur de Glyphes (groupe 5)

- [VERIFIED] Mécanique : **rune de classe** (matériau de base, 8×3 ou 16×3 compos) maîtrisée → tirage pondéré → **1 glyphe parmi 5** de la classe (`Décrypteur_de_Glyphes_(Ursuns).md:6-11` ; `artisanat-glyphes-ursun.md:5`). La rune est le `prevRecipe` des 5 glyphes.
- [VERIFIED] **Durée de vie des glyphes : 14 jours** (`Décrypteur_de_Glyphes_(Ursuns).md:5`) — colonne `due_date` de `player_life_skill` (`2025-06-09-lifeskill-metiers.md:23`).
- [VERIFIED] Paliers de classes (officiel) : **30** Magicien, Guerrier ; **35** Chasseur, Clerc ; **40** Voleur, Barde ; **45** Chevalier, Ingénieur ; **50** Illusionniste, Martial Artist ; **55** Shaman, Danselame ; **60** Démoniste, Templier ; **65** Samouraï, Corsaire ; **70** Sage ; **75** Chevalier Dragon ; **80** Luminary + **Messie/Mimic** (glyphes stat +1 % corps entier) ; **85** glyphes éveillés ; **90** Juge (`artisanat-glyphes-ursun.md:9-18`). Liste complète recettes+stats+effets : `wiki-tables/R/Bear.md:10-164` (Vendetta, incl. éveillées, Inquisiteur, Agent, Wyvern Lord).
- [VERIFIED] Glyphe = **2 stats + 1 modificateur de skill** (ex. Magicien « explosif » : INT+11, M-CRIT DMG+5, Enfer Ardent +5 pieds — `Décrypteur:11,17` ; `R/Bear.md:132`). Équipé dans la roue `jobglyph` (15 slots, §1.4) ; rarity orange, texte EN « Can only be used in a LvNN \<Class\> glyph socket » (`wiki-en/categories/glyphes.md`, 120 pages).
- [VERIFIED] **Glyphes éveillés (nv85)** : mécanique à 2 étages — la barre donne un « glyphe d'énergie éveillée » + glyphe de classe non-identifié ; clic droit → 1 des **2** variantes éveillées ; normal+éveillé mutuellement exclusifs (`artisanat-glyphes-ursun.md:189-206`). Recettes Vendetta : 3 runes de base (`Awaken Energy/Power/Ancient Glyph`) couvrant les classes éveillées (`artisanat-glyphes-ursun-vendetta.md:64,89,114,156,181,206,248,273,315`) — **custom Vendetta**, rééquilibré au fil des patches (`:5-6`).
- [VERIFIED] Maîtrise : 50 pts (classes 30-40) / 100 pts (≥45) selon wiki FR (`Décrypteur:15,34,37`) ; craft d'une rune rapporte ~5-30 pts (`:8`) — cohérent avec ExpBase 15/ExpVar 10 de l'ini (rune 29).

## 7. HALFKIN/ÉZELIN — Tissage / Broderie (groupe 4)

- [VERIFIED] Chaîne : **bobine/fil** (base) → maîtrise → **1 étoffe de trophée parmi 5** (Guardian/Attaquant/Sniper/Prêtre-Healer/Sorcier-Magic Weaver) par niveau, 16 niveaux (`artisanat-etoffes-pour-trophee-ezelin.md:5,9-24` ; `R/Zumi_Halfkin_Frog.md` col. 6-10).
- [VERIFIED] Bobines officielles (FR) : coton 30, laine 35, lin 40, soie 45, cachemire 50, mohair 55, soie d'alpaga 60, fil énergie éternelle 65, fil arcanique 70, fil énergie infinie 75, câble or fondu 80, moustaches de dragon 85, soie bête divine tribale 90 (`artisanat-etoffes…:10-23`) — recettes EN : `R:5,12,18,24,30,36,42,48,54,60,66,74,80`.
- [VERIFIED] Stats par étoffe — complètes Lv1-14 dans `artisanat-etoffes…:27-179` (ex. Gardien 14 : DEF+1501, PV+509, Bloc+102, rés mag +10, mal +4 % ; Sorcier 14 : ATQ MAG+1804, PV+509, crit mag+68, deg crit+4, parade+102) ; tables EN 1-16 : `R:7-121`.
- [VERIFIED] Étoffes **combinées** (Vendetta, palier 95/100/110) : Warrior/The Protector/Ancient Warrior/Magical Protector/Magical Guardian — 10× Amethyst/Purple/Magic Purple Thread + 2 étoffes TE Lv14/15/16 (`R:92-97,105-121`).
- [VERIFIED] Application : étoffe posée sur **trophée** (slot trophée, pas l'armure) — « ajouter des bonus de statistiques sur vos trophées » (`Tissage_(Ezelins).md:5`) ; l'étoffe est consommée directement sans barre d'XP (`:15`).

## 8. Données corpus & couverture FR

- [VERIFIED] `corpus/fr-full/data/db/c_lifeskill.ini` — header `|V.1|36|`, ~594 recettes (`LifeSkillParser.cs:8`) ; comptage par groupe : g1=246 lignes, g2=60, g3=79, g4=78, g5=132 (inspection directe). Schéma colonnes : `id|group|itemId|name(Big5)|count|reqLevel|expBase|expVar|expCap|gold|next|prev|3×(matId,name,qty)|N×(resId,name,poids)` (`LifeSkillParser.cs:13-27`).
- [VERIFIED] Noms Big5 dans l'ini (mojibake attendu hors décodage — `c_lifeskill.ini` extrait : `嚇人炸彈試作品`…). Le serveur charge le pendant serveur `S_LifeSkill.ini` depuis le dossier décodé (`LifeSkillService.cs:29-30` ; `LifeSkillParser.cs:8`). [HYPOTHESIS] `c_lifeskill.ini` et `S_LifeSkill.ini` = même contenu (le client lit sa copie via C_LifeSkill ; le wiki FR confirme les mêmes chiffres).
- [GAP] Les noms FR officiels des recettes/items métier **ne sont pas dans `t_*` du corpus pour tout** : guides FR écrivent « Crimson Thread », « Poison Bacteria » en EN pour les paliers 95+ (le client FR s'arrête à ~90 — `artisanat-etoffes…:24`, `artisanat-potions-anuran.md:207`).

## 9. Officiel vs Vendetta — tri

- [VERIFIED] Officiel/Aeria : paliers 30-90 partout, humain gemmes Nv1-6 ; guides `artisanat-*.md` = référence officielle (le site précise « version Aeria Games », `artisanat.md:6`).
- [VERIFIED] Custom Vendetta : paliers 95/100/110 (tous les crafts), glyphes éveillés nv85+, classes post-90 (Inquisiteur/Juge, Agent, Wyvern Lord, éveillées ×2), étoffes combinées, Monster Stones t100/110, potions Lv95-110, `Crimson/Amethyst/White/Purple Cotton`. Explicite : `artisanat-etoffes…:6`, `artisanat-potions-anuran.md:204`, `artisanat-glyphes-ursun.md:6`, `Dyes.md:7`. Tables `R/*` et `M/*` = source Vendetta (header de chaque fichier).
- [VERIFIED] Périmètre projet = ≤90 (`knowledge/README.md:9-15`) → les paliers >90 sont documentés ici mais hors implémentation.

## 10. GAPs

1. **[GAP] `knowledge/raw/` absent du checkout** (gitignored, ~121 Mo non versionnés — `knowledge/README.md:60,218-225`) : le texte complet des 5 pages-guides EN (`Alchemy`, `Engineering`, `Engraving`, `Embroidery`, `Jewel_Fusion`, `Racial_Crafting`, `Gathering`, `Alchemy_lab`) n'est pas lisible ; seuls les résumés 1-ligne du TSV sont exploités (`pages-index.tsv:214,220,…`). → régénérer `raw/` ou se contenter des digests.
2. **[GAP] Prix exacts de production par recette** : `recipe.Gold` existe dans l'ini (col. 9) mais n'a pas été décodé ligne par ligne (Big5 — la colonne est lisible mais je n'ai pas extrait la table complète). `Tissage` FR donne 4 exemples.
3. **[GAP] Poids exacts du tirage produit** (`Results` cumulWeight) : présents dans l'ini (col. 21+, poids ~25 par résultat sur les lignes inspectées, ex. `25` ×4-5 = total 100-125) mais pas extraits en masse → déterminer si les ~5 issues sont équiprobables (25/125 ≈ 20 % — cohérent FR « 1 parmi 5 »).
4. **[GAP] Règle exacte du `due_date` (14 j glyphes)** : mécanique d'expiration (re-appris ? disparaît de l'équipement ?) non tracée dans les sources lues — vérifier `CLifeSkillFactory` natif + `LifeSkillRepository` due handling.
5. **[GAP] Recettes « ciseaux » / gem-inlay** (sertissage sur équipement) : item ciseaux + fenêtre d'inlay référencés (`artisanat-gemmes-humain.md:5`, decorations `Alchemist` « gem inlay quests ») mais le NCID/layout dédié (≠ 214) non localisé — probablement Magic Pool / CombineStation (`ZoneEntryFlow.CombineStation.cs`) → agent K4/D4.
6. **[GAP] Qui écrit le nibble `life_skill` (choix métier)** : la race au character-create détermine le groupe, mais l'écriture initiale/`x+1` tier avancé (nibbles paires {0x20,0x30}…) n'est pas tracée — cf. `PlayerCharacterEntity.LifeSkill` + entry flow.
7. **[GAP] Recettes NPC-alchimiste des gemmes élémentaires** (Human) : « formules auprès d'un PNJ alchimiste » (`artisanat-gemmes-humain.md:9`) — vendeur + table de formules non localisés dans les sources (probablement shop classique/`Gem` EN 287 pages, `pages-index.tsv`).
8. **[GAP] Cast barre d'exp op1 vs op2** : op1 « produce sans exp » vs op2 « avec exp » confirmé dans le code (`:228-231`) mais le déclencheur UI (bouton ×1 vs ×N ?) non confirmé dans les layouts lus — `StudyEditBox/CreateEditBox` suggère une quantité saisie.
9. **[GAP] `jobglyph` équipement des glyphes** : NCID d'inlay/outlay de glyphe (≠ 214) non identifié ici — texte item « left-click and select the class glyph socket » (`glyphes.md`) → à tracer dans le décompilé.
10. **[GAP] Monster Stones upgrade** : la montée Lv1-4 par « Enchant Crystals » (M/Monster_Stones) — canal exact (fusion `c_itemcombo` ? NPC ? LifeSkill ?) non confirmé.
11. **[GAP] Noms FR officiels des items métier** : `t_item`/tradcomposarti.pdf cité mais non lu ; la concordance FR↔EN complète n'existe que partiellement (`artisanat.md:41-64`).

**Actions non réalisables ici** : décodage massif de `c_lifeskill.ini` (Big5) en table exploitable (faire via `IniTable`/`EdenEncoding` côté code, pas à la main) ; lecture des pages EN `raw/` (régénérer le dossier) ; traçage `re/reindex` des opcodes glyph-inlay et due-expiry (recommandé : `reindex search "LifeSkill" -b zone`, `reindex search "Glyph" -b zone` — agent R2 du plan).
