# K2 — Métier GUILDE + VILLE DE GUILDE (Family / FamilyTown)

Sources primaires : `knowledge/guides-fr/pages/guilde-et-ville-de-guile.md` (= **GV**), `knowledge/wiki-fr/pages/Ville_de_Guilde.md` (= **VG**), `knowledge/wiki-tables/misc/Useful_Info.md` (= **UI**, chiffres Vendetta), `knowledge/wiki-fr/pages/Quêtes_de_guilde.md` (= **QG**), `corpus/fr-full/data/db/{c_buliding,t_buliding,t_item,t_npc,GuildFunction,decorate,4PHouse,c_familycollect}.ini`, `knowledge/wiki-en/pages-index.tsv` (= **TSV**, 105 entrées `decorations-guilde`).

> ⚠️ `knowledge/raw/` absent du dépôt (gitignored) — les pages EN `Guild/Guild_town/Guild_fame` ne sont accessibles que via digest `categories/guides-mecaniques.md:89-91` + TSV. Écart chiffrés **Aeria/FR vs Vendetta** noté partout.

---

## 1. Création de guilde

[VERIFIED] Conditions : **niveau 20 min + 50 po + sans guilde**. PNJ **Heldin** à Aven (X:478 Y:468) — GV:9, `Guilde.md:6` (wiki-fr, même valeurs). La question « 20 po ? » → **50 po** dans les deux sources FR.
[VERIFIED] Nom : 1 mot, ≤16 caractères, caractères spéciaux/underscore autorisés ; les 50 po prélevés à la confirmation — GV:9.
[VERIFIED] Insigne/Logo : débloqué **niv. guilde 3**, bitmap 32×32 px 24/32-bit non optimisé, transparence possible — GV:11.
[VERIFIED] `t_npc.ini:575` confirme `Heldin | Ville de Guilde` ; `t_npc.ini:568-569` `50337 Officier Quête de Guilde | Kaiser | Ville de Guilde`.
[VERIFIED] Changement de chef (désertion) : service cash-shop, membre ≥30 j + chef offline ≥30 j, appliqué à la maintenance hebdo — GV:32.
[VERIFIED] Modèle serveur : `FamilyModels.cs:21-44` — `Level(byte)`, `Exp`, `Exploit`, `Treasury`, `TaxRate`, `BusinessTax`, `Honor`, `Emblem/Uniform/Flag`, `SpellPoint`, `HouseOpen` ; 9 titres de rang (`TitleCount=9`, member+leader+cadre2-8 — FamilyModels.cs:14,48), **14 privilèges** : channel, invite, kickout, editinfo, setcadre, invite25r, fixequip, msgboard, delmsg, treasury, repair, storage01-03 (FamilyModels.cs:51-55). Paquets membres par chunks de **30** (FamilyModels.cs:17-19).
[HYPOTHESIS] `GuildFunction.ini:1-11` (V.1,12) = 10 fonctions de guilde × paliers de niveau (flags 1/3 par colonne) — sémantique exacte des colonnes non décodée.

## 2. Niveaux de guilde 1-12 — seuils et avantages

Deux jeux de chiffres (mêmes **gloire**, **fonds** différents car coût indexé sur le nb de villes du serveur — GV:14) :

| Lv | Gloire (les 2 sources) | Fonds Aeria (GV:17-28) | Fonds Vendetta (UI:5-17) | Avantages |
|---|---|---|---|---|
| 1 | — | — | — | Canal guilde, liste membres, message d'annonce, fonds de guilde ; 3 grades |
| 2 | 2 000 | — | — | Panneau d'affichage, ouverture au public ; +1 grade |
| 3 | 7 000 | — | — | **Ville de guilde + insigne**, 1er entrepôt ; +1 grade |
| 4 | 24 000 | 3 400 | 3 400 | 2 statues de joueurs ; **claim territoires** (UI:9) ; +1 grade |
| 5 | 60 000 | 16 000 | 16 000 | 2ème entrepôt ; +1 grade |
| 6 | 120 000 | 54 000 | 55 000 | 2 statues suppl. (=4), 3ème entrepôt (GV:22) |
| 7 | 140 000 | 80 000 | 55 000 | Capacité +200 membres |
| 8 | 160 000 | 136 000 | 55 000 | +225 |
| 9 | 180 000 | 200 000 | 55 000 | +250 |
| 10 | 200 000 | 300 000 | 55 000 | +300 |
| 11 | 220 000 | 400 000 | 55 000 | +325 |
| 12 | 240 000 | 560 000 | 55 000 | +350 |

[VERIFIED] Progression : **fonds de guilde** (taxe 10 % des quêtes rendues par les membres + taxe appliquée aux ventes des PNJ marchands aux étrangers — GV:13) et **gloire** (quêtes de guilde + utilisation des bâtiments de ville — GV:13, QG:5).
[VERIFIED] Grades : nombre débloqué par niveau (3 de base, +1 aux lv2-5), permissions accordées par le chef — GV:29-30. Le chef gère autorisations, statues, bâtiments, PNJ, taxe — GV:31.
[GAP] Table native des seuils : **absente du corpus client** (probablement `S_*` côté serveur natif, ou en dur dans `world`/`mission` — vérifier via `reindex`).

## 3. Fondation d'une ville de guilde (VDG)

[VERIFIED] Conditions : **chef uniquement, niveau 30 min**, guilde niv. 3+, **≥30 membres**, fonds suffisants — GV:35, VG:11-15.
[VERIFIED] Coût de fondation **variable selon le nombre de villes existantes** : 500 po si peu de villes, **10 000 po à partir de ~50 villes** — GV:35 ; VG:17,54 (« 500 po pour les 50 premières villes puis 10 000 »).
[VERIFIED] Pas d'enchère ni d'item « bail/terrain » trouvé : fondation via **Heldin** en choisissant le type de terrain (VG:17). Aucun item `deed/terrain` dans `pages-index.tsv` → le « bulletterrain » du brief n'a pas de correspondance item ; c'est un choix de terrain au dialog Heldin.
[VERIFIED] 2 types de terrain, purement esthétiques : **Forêt** (Forêt Eden) / **Montagne** (Mont Eden) — GV:36, VG:21-33. Confirmé par `familybuilding_forest.layout` + `familybuilding_mountain.layout` et les 2 modèles de Chief Hall (H00101 village / H00301 montagne, `c_buliding.ini:2-17`, `t_buliding.ini:3-34`).
[VERIFIED] Entretien : garder ≥30 membres + **≥10 connexions de membres par semaine** (GV:35) — VG:19 précise « 10 connexions simultanées sur 72 h » (divergence de détail entre sources) ; mail d'alerte aux membres si ville en péril (GV:35).
[VERIFIED] Avantages VDG dès la création : potager + mine + autel exploitables (outils à acheter à Aven : gants de jardinage, pioche, boule de cristal), panneau municipal vendant des quêtes de guilde en plus grand nombre que Kaiser, achats au prix de base (pas de taxe) pour les membres — VG:42-45.

## 4. Bâtiments (c_buliding.ini + t_buliding.ini)

[VERIFIED] **Max 5 bâtiments différents**, achetés au PNJ **Chaussons** à Aven (X:467 Y:484) sur les fonds de guilde ; pas de doublons — GV:41. `t_npc.ini:607` confirme `Chaussons | Récupération/Production`.
[VERIFIED] Chaque bâtiment existe en **niveaux 3→12** (ids 101-220, chaînés par col.6 `prev_id`) ; **niveau bâtiment ≤ niveau guilde** ; **cap travail accepté = niveau du bâtiment** (« peut accepter du travail de niveau N maximum ») — `t_buliding.ini:35-338`, `c_buliding.ini:18-257`. L'EXP de bâtiment se gagne par les commissions de travail (« gagner de l'EXP pour les bâtiments »).
[VERIFIED] Durabilité limitée → réparation via le panneau du **clerc de la ville**, coût prélevé sur fonds de guilde, selon autorisations de grade ; en panne, les PNJ d'assignation refusent les nouvelles commandes (bâtiment non détruit) — GV:51. NCID 180 `ZoneServerFamilyBuildingRepair` — `protocol-map.md:374`.
[VERIFIED] Les 12 gabarits (`t_buliding.ini`) et leur **PNJ novice livré** (col.7 de `c_buliding.ini` = item NPC) :

| Type | Bâtiment | Modèle | NPC livré (t_item.ini) | Coût col.8/9 (`c_buliding.ini`) |
|---|---|---|---|---|
| 1 | Salle du Chef du Village (lv3-10, ids 1-8) | H00101 | — | item 35001 Clerc, — |
| 1 | Salle du Chef de la Montagne (lv3-10, ids 51-58) | H00301 | — | item 35001 Clerc, — |
| 4 | **Ferme** (récolte culture) | H01001 | 35002 Fermier Novice | 173→3076 / 200 |
| 5 | **Mine** (récolte minerai) | H01101 | 35012 Apprenti Mineur | 173→3076 / 200 |
| 6 | **Autel** (invocation) | H01201 | 35022 Invocateur | 173→3076 / 150 |
| 7 | **Labo d'Alchimie** (gemmes+potions) | H01301 | 35030 Apprenti Alchimiste | 346→3076 / 100 |
| 8 | **Fonderie** (armes+armures) | H01401 | 35039 Maître du Moulage | 346→3076 / 100 |
| 9 | **Mare Magique** (enchants+accessoires) | H01501 | 35068 Artisan Novice | 346→3076 / 100 |
| 2 | Hôtel ×3 skins : Brique / Magique / Bunker | H004/5/601 | — | pas de col.7 |
| 10 | Marché ×3 skins : Brique / Magique / Bunker | H007/8/901 | — | pas de col.7 |

[HYPOTHESIS] `c_buliding.ini` col.8 = coût **gloire** (173→3076, ×2 pour les 3 bâtiments de craft), col.9 = coût **po** (200/200/150/100/100/100) — à confirmer par désasm `CFamilyBuilding`.
[VERIFIED] **Plans (blueprints)** = items : 35071 Salle Chef (Village), 35073 Salle Chef (Mont.), 35075-77 Hôtels, 35078-80 Marchés, 35081 Ferme, 35082 Mine, 35083 Autel, 35084 Labo, 35085 Fonderie, 35086 Mare — `t_item.ini:57634-57683` ; 14 « Town Blueprint » dans TSV:1454-3315.
[VERIFIED] Rôles FR (GV:42-50) : Autel → invocateur ; Ferme → fermiers ; Fonderie → maîtres du moulage/forgerons ; **Hôtel → jusqu'à 8 slots de marchands** ; Labo → alchimistes ; **Marché → jusqu'à 3 slots de marchands** ; Mare → artisans ; Mine → mineurs. EN confirme bâtiments d'accueil par NPC (`NPC | <Building>` col.4 TSV).
[VERIFIED] `4PHouse.ini:4-18` = 8 points d'ancrage par modèle H001-H015 ; `decorate.ini:4-235` = 235 modèles P001-P618 (+échelles 0.5-3.5) — catalogue visuel des plaçables.

## 5. PNJ de ville (items 35001-35080 + decorations-guilde 105 pages)

[VERIFIED] PNJ achetés principalement chez **Aldata** à Aven (X:461 Y:491) sur fonds de guilde ; le reste via butin BP de boss / coffres d'élite — GV:54. `t_npc.ini:605` `Aldata | Récupération/Production`. Tous « à gérer sur l'écran du chef de guilde » / "managed in the guild leader's UI".

### 5a. PNJ d'assignation (craft/récolte — 3 paliers : base / +2 prod ou −10 % échec / −20 % échec orange)

`t_item.ini:57316-57633` + TSV :
- **Ferme** : 35002 Fermier Novice → 35003 Fermier (*Production Culture +2*) → 35004 Maître Fermier (*échec −10 %*)
- **Mine** : 35012 Apprenti → 35013 Mineur (*Minière +2*) → 35014 Maître Mineur (−10 %)
- **Autel** : 35022 Invocateur (accepte requêtes d'invocation ; EN: Summoner, TSV:11472)
- **Labo** : 35030 Apprenti Alchimiste → 35031 Alchimiste (−10 %) → 35032 Maître Alchimiste (**−20 %**, orange — TSV:8811)
- **Fonderie** : 35039 Maître du Moulage → 35040 Maître Vétéran (−10 %) → 35041 Forgeron Renommé (**−20 %**, orange — TSV:5144 Famous Forger)
- **Mare** : 35068 Artisan Novice → 35069 Artisan (−10 %) → 35070 Maître Artisan (**−20 %**, orange — TSV:8814 Master Craftsman)

### 5b. PNJ marchands (vendent plans/formules/cartes par bande de niveau × rareté) — Market House sauf mention

- **Chaîne ferme** (Culture Secrète) : 35005 Marchant Agricole (20-40 */**), 35006 Botaniste (20-40 ***), 35007 Vétéran Agricole (41-50 */**), 35008 Expert Botaniste (41-50 ***/****), 35009 Spécialisé Agricole (51-60 */**), 35010 Maître Botaniste (51-60 ***/****), 35011 Monsieur Mystère (51-60 *****, orange — TSV:9109)
- **Chaîne mine** (Carte de Gisements) : 35015-35021, même structure ×6 paliers (Mineral Merchant/Scholars/Expert/Master/Senior/Supreme + Mr. Mystery — TSV:8977-8980, 10634, 11549)
- **Chaîne autel** (Parchemin d'Invocation) : 35023-35029 (Summon Merchant/Scholar/Expert/Master/Senior/Supreme — TSV:11456-11474, 10638, 11551)
- **Chaîne labo** : 35033 Marchand Sertisseur (20-35), 35034 Pharmaceutique (20-40), 35035 Vét. Sertisseur (40-45), 35036 Vét. Pharma (45-50), 35037 Spéc. Sertisseur (50-55), 35038 Spéc. Pharma (55) — EN ajoute Legendary Pharmaceutical (lv60, TSV:7879)
- **Chaîne mare** : 35072 Marchand d'Enchantements (20-40), 35074 Marchand d'Artisanat/joaillerie (20-40) ; EN : Senior Enchantment (45-50), Supreme (55), Legendary (60) ; Senior Crafts (55), Senior Technology (45-50), Legendary Crafts (60) — TSV:4916,10631-10632,10639,7876-7878,11547
- **Fonderie — designers armes lv20-40** : 35042 Armes Blanches (poignard/hache/épée/épée colossale/rapière/katana40), 35043 Contondantes (masse/bouclier/gourdin/marteau/griffe40), 35044 Projectiles (arc/pistolet), 35045 Armes magiques (guitare/bâton/grimoire) — TSV:1263,3415,9778,12555
- **Armures lv23-43** : 35046 Lourdes, 35047 Légères, 35048 Robes (TSV:6656,7932,3804) ; lv48 : 35065-67 (EN dit Lv48/53)
- **Draftsmen monotype lv45-50/55** : 35049-35064 = poignard/rapière/épée/masse/griffe/gourdin/katana/épée colossale/hache/marteau/arc/artillerie/guitare/grimoire/bâton/bouclier (TSV:4081…11585 — EN : Lv45-50, lv55 pour Axe/Club/Guitar/Hammer/Katana/Mace/Rapier/Staff/Sword/Shield/Bow/Cestus/Dagger/Grimoire/Greatsword/Artillery)
- **Potion Merchant** : potions HP/MP lv1-45 (TSV:9705)

### 5c. PNJ de service

- **Clerc de la Ville** 35001 (Chief's House — infos ville/PNJ, TSV:12018) ; **Bank Commissioner** (Residential House — banque perso, TSV:960) ; **Traveling Boar Auctioneer** (Residential House — HdV, TSV:12045). « Residential House » = les maisons des joueurs en ville.

### 5d. Reliques / statues (catégorie « Holy Relic Decoration », 25 entrées)

Bonus **3 h**, membres uniquement, durabilité 100 (1 pt/utilisation) — GV:62. Items **23817-23841** (`t_item.ini:19372-19502`), paires +5 % (vert) / +7 % (bleu) :
EXP (Cristal d'Eternal 23817 → Cristal de Lumière Sacrée 23829) ; taux butin (Obélisque 23818 → Totem 23830) ; PV+guérison+parade (Rikerine 23819 → Floraison 23831) ; PM+VIT INV+soin (Verre 23820 → Lumière 23832) ; FOR+INT+parade (Scellée 23821 → Noble Guerrier 23833) ; AGI+ATQ+TxCrit (Mystère 23822 → Gravitation 23834) ; SAG+DÉF+ESQ (Arche 23823 → Dorée 23835) ; CHA+dég crit mag (Sagesse 23824 → Vérité 23836) ; VIT DÉP+PRÉC+crit (Guerrière 23825 → Ailée 23837) ; rés Sacré/Glace/Nature (Prophète 23826 → Rossie 23838) ; rés Obscure/Foudre/Feu (Trône Noir 23827 → Cthulhu 23839) ; rés physiques (Terrifiant 23828 → Enragé 23840) ; EXP classe (Arbre de la Paix 23841, Tour Citrouille 23842 event). Obtention : quêtes journalières monstres mutants (tout membre) + quête de guilde du chef au PNJ à l'entrée de la ville, élimination de guilde, events — GV:63.
[VERIFIED] **Statues de joueurs** : max 4 (2 piédestaux lv4 + 2 lv6), **500 po/statue** sur fonds de guilde, capture pose+costume, remplaçables — GV:121.
[VERIFIED] Statues Échecs (7 jours, hors ville — boîte mystique, cristaux purifiés, 11 modèles Bykova/Kamsky/Sophia/Topalov/Kasparov/Pomariov/Steinitz/Pion/Valkyrie/Athéna, verts→bleus→or) — GV:90-111.
[VERIFIED] Statues Vendetta (BaSH/Vivi/Jordan's Blessing, 7 j) : 750× Stormy Mountain Stones du raid 25p Lv95 Dragon Slaying Expedition → PNJ **Arvad** Aven X473 Y390 — UI:27, GV:113.

## 6. Taxe

[VERIFIED] **10 %** des récompenses de quêtes des membres → fonds de guilde (GV:13). [VERIFIED] Taxe appliquée aux joueurs **étrangers** sur les ventes des PNJ marchands, taux fixé par le chef ; membres = prix de base (GV:13, VG:45) ; conseil joueur : viser une ville à taxe 1-10 % (QG:555). [VERIFIED] Champs serveur `TaxRate` + `BusinessTax` (FamilyModels.cs:33-34). [HYPOTHESIS] TaxRate = taxe ventes ville ; BusinessTax = ponction quêtes.

## 7. Quêtes de guilde

[VERIFIED] 3 accès : **Kaiser** officier des quêtes Aven (446,358 — offre limitée), **panneau municipal** de toute VDG (offre élargie, pas besoin d'être membre), **Gardien de l'âme** à l'entrée des donjons (quêtes donjon uniquement) — QG:7-9,556.
[VERIFIED] 2 types : **simples** (tueries, ≤10 pts de gloire ; dégressif 7/5/0 si niveau trop bas) et **donjon** (**20 gloire**, mode illimité seulement) — QG:12-14.
[VERIFIED] Coût d'acceptation en argent (64 pa → 2 po+), gains argent + XP (+ pièces d'Alpaga pour les donjons lv42+) — QG:17-551 (~50 quêtes lv8-65 détaillées). La plus farmée : « Alerte Clochemer : Gardes ! » lv8, 9 pts (QG:27).
[VERIFIED] Items déclencheurs « Battle Alert / Avis de Recherche / Alerte » → « gain Guild Fame » (TSV objets :450,1052-1082,1162-1163,1264-1265 — émis par les villes de chaque région, lv18-58).

## 8. Boss de guilde (worldboss)

[VERIFIED] 1 boss/région, respawn **1 h** (2 h Vendetta), buffs aléatoires, butin commun + **breakpoint** → trophée buffant durée/portée de compétences de classe — `boss-de-guilde-world-boss.md:5`.
[VERIFIED] 27 boss lv8→100 listés avec titre, zone, trophée, bonus 2 classes (Justin Allspore lv8 Monts Calcaires … Kiriyata lv100 Veroona Vale) — même fichier :6-150 ; succès par boss dans `Monstres_de_guilde.md:11-42`.
[VERIFIED] Succès légendaires : collecter tous les trophées boss donjon+guilde ≤65 (PV max +500 ×3 paliers) ; tuer tous les rares+boss guilde ≤65 (XP +3 % ×3 + « Vaincre tout le monde » mana −2 %) — boss-de-guilde:151-165.

## 9. Élimination de guilde (GvG)

[VERIFIED] 16 guildes, classement hebdo **prestige** : top 8 auto + 8 tirées parmi ≥3 000 pts ; guilde **lv4+ requis** — `elimination-de-guilde.md:9`.
[VERIFIED] Prestige via arènes lun-ven : 3v3 & boss arena = 5 vict./2 déf., 10v10 = 10/3 — :10. Recrutement gelé pendant le tournoi — :11.
[VERIFIED] Matchs : 50 joueurs max/guilde (niv. 30+), 30 min, 300 pts de moral de départ, 5 cristaux à tenir, 2 spawns + portails, paire d'élites PNJ par camp — :20-24.
[VERIFIED] Rôle « kamikaze » : cristaux d'Ordre Militaire → PNJ Adréan (élites 15 cristaux ×3 lanes ; escouade suicide 10 ; attraction 10) — :27-35.
[VERIFIED] Récompenses : Couronnes Champion/Argent/Bronze (+3/+2/+1 % stats, 1 sem.), Surprises de la Gloire Sainte (**statue classique aléatoire**), Surprises Glorieuses (pierres de guerre), buff 1 sem. top 4 ; individuel : coffres de pierres de guerre top 20 — :38-46. Horaires sam/dim 16h/20h30 — :15-17 ; Intro.md:9-11 (Vendetta : GvG sam/dim 16h/20h, Guild Arena sam/dim 18h).

## 10. Guild storage (entrepôts)

[VERIFIED] **3 coffres** (layouts `familystoragetab1/2/3`), **40 slots chacun**, débloqués lv **3 / 5 / 6** — `FamilyStorageWindow.cs:18-24`, GV:19-22. Privilèges `storage01-03` (index 11-13 des 14 priv.) ; serveur refuse silencieusement un dépôt non autorisé — FamilyStorageWindow.cs:21-24.
[VERIFIED] NCIDs : 177 put, 178 get, 179 log, 180 repair-bâtiment ; S→C 504 delete, 505 put-notify, 506 log, 507 enter, 508 data — `protocol-map.md:371-374,706-710`. Ouverture via service NPC type 19. Pas d'or dans le coffre (trésorerie = flux guilde 159) — FamilyStorageWindow.cs:38.
[VERIFIED] DB : `eden_server/Database/009-family-building-container.sql`, `010-family-visit.sql`, `001-initial-schema.sql:153-170` ; repo `FamilyBankRepository.cs`.

## 11. Ancres code/layouts (reconstruction)

- **Protocole VDG** : C→S 173 FamilyInstanceCreate, 174 TownQuery, 175 TownMemberQuery, 176 TownEnter ; S→C 501 EnterFamilyInstanceCreation, 502/503 Town(Member)Query, 514 EnterFamilyTownList — `protocol-map.md:367-370,703-716` ; tests `FamilyTownEnterTests.cs`, `FamilyTownQueryTests.cs`, `FamilyPacketsTests.cs`.
- **Layouts VDG/guilde** (`corpus/fr-full/uidata/layouts/`) : `familywnd`, `familyinfownd`, `familyprivilege`, `familyadministration`, `familybuilding{,_forest,_mountain}` (placement bâtiments), `familytownlistwnd` (liste des villes), `familystoragewnd+tab1-3+logwnd`, `familydonatedlg` (dons→fonds), `familybulletindlg`, `familyorderwnd`, `familyjobnamedlg`, `familysetprivilegedlg`, `familyfinding{info,wind}`, `familymemberfindingwnd`, `familyappwnd` (candidatures), `familycommentdlg`, `familymessage{dlg,wnd}`, `familyachievement`, `familycenter`, `familybattle{info,join,judge,killedrank,killedrank2,rank,time}wnd` (GvG).
- **Données commissions** : `c_familycollect.ini` — col.2 type (0=ferme, 1=mine, 2=autel/summon), bandes lv20-70 (+Vendetta 34670-72), gain EXP 122→1 764, produits item 30001-30060 (récolte) / 58901-58929 (invocation) avec poids ; 478 lignes — domaine K3.
- **Items récolte ville** : gants jardinage/pioche/boule de cristal « in Towns » (TSV:3497,3605,4019-4020).
- **Cristal de VDG** : apparaît à 3 spots, annoncé canal guilde ; détruire → coffres bleus (amulettes XP/PC, nourriture familier, cristaux d'Asbi, aides récolte, appels monde, objets villa) — GV:124. `c_familyevent.ini` quasi vide (1 ligne) → événement piloté serveur.

## 12. GAPs

1. **Seuils natifs de niveau guilde** : aucune table `c_*` client ne les stocke → chercher `S_Family*`/`world` dans `re/` (reindex `CFamily::` , `GetFamilyLevel`). Seules valeurs : Aeria (GV) et Vendetta (UI).
2. **`c_buliding.ini` col.8/9** : sémantique gloire vs po non prouvée ([HYPOTHESIS] col8=gloire requise, col9=coût po) ; vérifier via désasm achat/amélioration bâtiment.
3. **Flux natif fondation ville** : dialog Heldin → création instance (NCID 173/501) non tracé ; coût 500→10 000 po est côté serveur, pas en data client.
4. **Slots hôtel/marché** (8/3) et limites de statues : source guide FR uniquement ; capacités non retrouvées dans les tables client (probablement `S_` serveur).
5. **`c_familycombine.ini`** (3 178 lignes) non décodé — probablement chaînes de fusion (statues échecs vert→bleu→or ?) à auditer.
6. **Rémunération/salaire de guilde** mentionné GV:5 (« réparations, salaires ») sans mécanique détaillée — à creuser (privilège `treasury` + `familydonatedlg`).
7. **Mapping `Exploit`/`Exp`/`Honor`** du modèle Family vs « gloire »/« prestige » — colonnes natives présentes mais correspondance métier non prouvée (FamilyModels.cs:25,27,43).
8. **`raw/` absent** → détails EN complets des pages `Guild.html`, `Guild_town.html`, `Guild_fame.html` non vérifiables (digests seulement). Régénérer via `knowledge/raw/wiki-en/_build_index.py` si le dump est récupéré.
9. `FarmEvent.ini` donne `GiveItem 35001-35005 ×5` par palier de niveau de classe (lignes 8-30) — des items-PNJ de ville offerts en récompense ? Mécanisme à confirmer.
