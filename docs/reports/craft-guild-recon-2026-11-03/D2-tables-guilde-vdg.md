# D2 — Cartographie tables Guilde / VDG / Récolte — `corpus/fr-full/data/db/`

Légende : `c_*` = Big5 (mojibake), `t_*` = CP1252 FR lisible. Séparateur `|` (sauf `,` pour GuildFunction/FarmEvent). En-tête type `|V.x|ncols|`. **Aucune table `S_*` (serveur) dans le corpus** — voir GAPs.

## 1. `c_buliding.ini` — bâtiments VDG (orthographe native « buliding »)

- **136 entrées**, 12 cols (`|V.1|12|`), lignes 2-257 (`c_buliding.ini:2-257`). Schéma (croisé `misc-tables.tsv:6`) : `id|modèle H#####|nom Big5|type|niveau guilde requis|rowid prérequis|item coût|p1|p2|||desc…`.
- **Types** (`t_buliding.ini:3-255` pour les noms FR) :
  - `H00101` (type 1) « Salle du Chef du Village » — ids 1-8, niveaux 3-10 (`c_buliding.ini:2-9`)
  - `H00301` « Salle du Chef de la Montagne » — ids 51-58, nv 3-10 (`:10-17`)
  - type 2 = hôtels : `H00401` « Hôtel de Brique », `H00501` « Hôtel Magique », `H00601` « Hôtel Bunker » — ids 107-109/119-121/…
  - type 4 = `H01001` « Ferme » (ids 101,113,125…185,197,209 = nv 3-12)
  - type 5 = `H01101` « Mine », type 6 = `H01201` « Autel » (invocation de créatures), type 7 = `H01301` « Labo d'Alchimie », type 8 = `H01401` « Fonderie » (armes/armures), type 9 = `H01501` « Mare Magique » (accessoires/enchantements)
  - type 10 = marchés : `H00701` « Marché de Brique », `H00801` « Marché Magique », `H00901` « Marché Bunker »
- **Progression** : chaque bâtiment producteur existe aux nv 3-12 (10 niveaux, ids 101-220 en blocs de 12 ; `c_buliding.ini:18-257`). Hotels/marchés n'ont pas de coût (cols 7-9 vides). Chaîne `prérequis` = rowid du niveau précédent (ex. 113→101, `:42`).
- **Coûts** : col7 = item requis (`35001` mairies, `35002` producteurs — cf. FarmEvent §4), col8 = coût croissant 173→3076 (`:18` vs `:210`), col9 = 200 (ferme/mine), 150 (autel), 100 (labo/fonderie/mare) — [HYPOTHESIS] places/population.
- Texte FR confirme : « peut accepter du travail de niveau N max » + « le niveau du bâtiment ne peut pas dépasser le niveau de la Guilde » (`t_buliding.ini:35-39`).

## 2. `GuildFunction.ini` — fonctions par palier de guilde

- **10 entrées** (id 0-9), `,` séparé, 12 cols (`GuildFunction.ini:1-11`). Format : `id|nomBig5|v_lv1…v_lv10|`.
- Noms (Big5 décodé approx.) : 0=村莊 village, 1=一般功能, 2=特殊功能, 3=任務獎勵, 4=怪物?, 5=採集獎勵, 6=功能?, 7=商店, 8=倉庫, 9=佈置/decoration.
- Valeurs = `-1` (verrouillé) ou `1` (débloqué, sauf row9=3 au lv1). Ex. row1 : `1,1,1,-1×7` → fonction dispo nv guilde 1-3 seulement ; row0 : `-1` au lv1 puis `1` dès lv2.
- **Croisé** `knowledge/wiki-tables/misc/Useful_Info.md:5-17` : paliers guilde lv1-12 — lv1 = canal/membres/fonds ; lv2 = Billboard, ouverture public ; lv3 = **Guild Town + Logo + 1er entrepôt** ; lv4 = 2 statues + claim territoires ; lv5 = 2e entrepôt ; lv6 = 2 statues ; lv7-12 = capacité membres +200…+350. Les 10 colonnes = paliers guilde 1-10 [VERIFIED structure ; HYPOTHESIS sémantique exacte de chaque ligne].

## 3. `c_familycollect.ini` — ordres de récolte VDG

- **478 entrées**, 47 cols (`|V.2|47|`). Schéma [VERIFIED via `WorkOrderParser.cs:21-45,106-145` — ordre = `CCollectQuery::Deserialize` natif] :
  `workId|type|itemCatalyseur|nom|qtéCatal|nivBâtiment|familyExp|bâtimentExp|expJoueur|instant|durée|tauxUsure|usure|coûtOr| puis {itemId|nom|qté|taux%}×N produits`.
- **col1 type** : 0 = « 公會採集點 » ferme (items 30001-30030), 1 = « 礦區礦點 » mine (30031-30060), 2 = « 神秘水晶礦床 » autel/cristal (58901-58929, 56711-56715, 56821-56825).
- **Paliers** : noeuds 20/30/40/45/50/55/60/65/70/75/80級 ; ids catalyseur (col2) 30136-30245, 34670-34672 (tutoriel « 示範 », lignes 163-165), 36198-36296, 22038-22117. Le catalyseur = **item « graine de nœud » vendu en boutique** (cf. §10) — nivBâtiment (col5) 1-10.
- Paramètres typiques : exp 2723→14500, durée 20-30 (col10) / 25 pour cristal, usure 100/1, coût or 122→1764 (`c_familycollect.ini:2-357`).
- **+33 lignes non-VDG en fin de fichier** (rowid 245-276+) : spots de récolte monde `…礦石30/45/60/75`, `…草藥`, `…釣魚` + quests (`:246-276`) — items 36791-36817, 37103-37113, drops à taux décimal (0.2-1.3%). Donc la table couvre aussi le harvesting hors guilde [VERIFIED].
- Le reste du fichier (478 rows) prolonge le motif à d'autres paliers/recettes [VERIFIED via `misc-tables.tsv:20`].

## 4. `c_familycombine.ini` — recettes craft guilde

- **3178 entrées**, 44 cols (`|V.1|44|`) — j'ai lu les ~436 premières ; le compte total vient de `misc-tables.tsv:21`.
- Schéma [VERIFIED via `WorkOrderParser.cs:61-100,147-190` + `CraftRecipeParser.cs:33-40`] : `id|catégorie|nivBât|familyExp|bâtExp|instant|durée|tauxUsure|usure|or|itemProduit|nomProduit|qté|tauxSuccès|tauxMaléd| puis mats {id|nom|qté}×N`.
- **Catégories (col1)** : `7` = ordres d'atelier → résultat 40002-40193 « 公會寶箱/卷軸 » + flag col13 (256/512/1024/2048) (`c_familycombine.ini:2-11`) ; `3` = potions/cuisine guilde (résultat 30937-31095 « 料理/藥水 », catalyseur = parchemin de recette 31736-31894 consommé) (`:22-131`) ; `4` = plans « 設計圖：… » (résultat 30297-30596+ croquis/armes, blueprint item 31096-31395) (`:132-436+`).
- Taux succès 100→20% selon palier ; « taux malédiction » col14 vide la plupart du temps.

## 5. `c_familyevent.ini` / `FarmEvent.ini`

- `c_familyevent.ini` : **stub** — 1 row `1|||||2||…|2|` (`:2`, 24 cols) [VERIFIED quasi-vide].
- `FarmEvent.ini` (`,`, 30 rows) : **événements elfe + paliers de récompense récolte**, PAS le farm VDG : `#:If_ElfLevel/ElfEnergy → ElfEnergy +N` (ids 1-6) et `#:If_CharClassLevel → GiveItem 35001/35002/35003/35005 ×5` par tranches 1-15/16-30/31-45/46-60 (`:2-31`). Items 35001-35005 = matériaux d'EXP de bâtiment (mêmes ids que `c_buliding` col7) [VERIFIED lien].

## 6. `4PHouse.ini` / `decorate.ini` / `boundinfo.ini`

- `4PHouse.ini` (format `key=value`) : **15 bâtiments** `DB0-14` = `H00101-H01501 → dossier h001-h015, modèle h#####01, 8 points d'ancrage xyz` (`:4-18`) — slots de placement des décorations/reliques [VERIFIED].
- `decorate.ini` : **232 décorations** `DB0-231` = `P#####0 → p###, p#####0, scale` (`:4-235`) ; familles P001-066 (mobilier), P301-318, P501-506, P601-618 (reliques/statues ?), échelles 0.50-3.50.
- `boundinfo.ini` : ~700+ lignes, modèles `m#####0 → m###.nif + 6 floats` (bounds/scale) (`:1-248+`) — table globale de bounding boxes, pas spécifique VDG [VERIFIED].

## 7. `npclist.ini` / `c_npc.ini` / `t_npc.ini` — PNJ VDG

- `npclist.ini` : mapping visuel `n#####0 → m###.kfm, scale, weapon W#####, dds, …` (`:4-40+`).
- `c_npc.ini` (`|V.2|21|`) : `npcId|modèle n#####|nom|titre|flags|…|dlgIds|scripts #…#|…|shop|`. Scripts `#:Service <textId> 24 <storeId> #` = ouverture boutique (`c_npc.ini:3430-3483`).
- **PNJ VDG identifiés** (`t_npc.ini`) :
  - Outils : **53199 « Marchand de Pioches »** Phio, **53200 « Vendeur de Gants de Jardinage »** Ri, **53201 « Marchand de Boules de Cristal »** Aidey (`:3585-3590`)
  - Plans : **53202-53239** Experts/Antiquaires « Modèles/Croquis » par type d'arme & armure (`:3591-3628`), boutiques 619-658 ; **53353-53373 « Marchand de Plans de X »** (`t_npc.ini:3810-3830`)
  - Consommables : 53240-53243 enchantements/accessoires/marqueterie, **53244-53245 marchands ferme/plantes**, 53246/53249/53252 « Marchand Mystique », **53247-53248 « Marchand de Minerai »**, 53250-53251 invocations, **53254 « Marchand d'Élixirs »** (`:3629-3644`)
  - 53285 « Marchand Enchant. à la Mode », 53286 « Pharmacien Branché »
  - Gestion : **50604 « Agent Immobilier de Ville de Guilde »** Bungalow (`:1025`), 50337 Officier Quête Guilde, 50340/51567/51642 Chef(s) de Guilde Heldin (`:568-575,2644,2750`), 53345 « Envoyé de Guilde »
  - Reliques : **53083-53106 « Relique de Guilde »** (Cristal d'Eternal, Obélisque, statues — 24 items), 53268 Sapin Halloween, 53583-53595 (boss reliques : Kamsky/Sofia/Kasparov/Pomariov/Steinitz/Topalov/Bykova = **boss échecs**), 54901-54905 « Statue de Guilde »
  - Fermiers : 53008-53009, 50350, 51645 ; 53572/53626/53345 marchands plantes/héritage.
- Mapping shops confirmé dans `c_npc.ini:3430-3483` : chaque NPC VDG → 1-6 services `Service <id> 24 <storeId>`.

## 8. `c_store.ini` — boutiques (659 entrées, `|V.1|93|`)

- Format : `storeId|||{itemId|nomBig5|qté}×≤30` (`c_store.ini:2+`). [VERIFIED `misc-tables.tsv:48` — « réponse 304 EnterShop »]
- **Boutiques VDG clés** :
  - 501-507 = graines de nœuds ferme (30136-30187), 508-514 = mine (30191-30242), 515-521 = cristal (30246-30293) (`:301-321`)
  - 522-527 = parchemins de recettes cuisine/potions (31736-31894) (`:322-327`)
  - 532-534, 551-553, 583-585, 603-605, 619-637, 640-658 = croquis armes/armures 31396-31615, enchantements 31664-31732, élixirs… (`:332-359`)
  - 725-733 = nœuds 75/80級 (22038-22117), 740 = 65級 (`:524-540`), 741-742 = items déco/utiles 48177-48227 (`:540-541`)
  - 695 : boutique « catalogue » d'items faibles ids (19-451) [HYPOTHESIS boutique admin/legacy]
- **Chaîne complète** : `c_npc.Service → storeId → items` ; les graines de nœud achetées (col2 de `c_familycollect`) sont placées dans les bâtiments ferme/mine/autel.

## 9. Autres tables

- `c_territory.ini` : **28 rows** — guerres de territoire (group001-010, AC023 arène), descriptions + rewards `54493-54502` + slots `60007-60009` (`:2-593`). PAS les bâtiments VDG.
- `c_transport.ini` : **39 rows**, `|V.9|57|` — matrice coûts TP par destination ; `c1` = indisponible (`:2-40`). Row 19 `�O��|�s��` = [HYPOTHESIS] « Ville de Guilde » (nom cassé en 2 champs). Pas de row explicitement nommée VDG ailleurs [GAP partiel].
- `c_sceneevent.ini` : 258 rows, événements programmés nodes 280-283 type 14 (fenêtres horaires + enchants 57101-57379 + spawns) (`:2-40`) — « Tour des Cieux » selon TSV, pas VDG.
- `c_schedule.ini` : 125 rows `id|type|jour|HHMM début|HHMM fin` (types 1-7 + 200-203 + 601) (`:2-40`).
- `c_node.ini` : scènes `id|nom|lv|…|S###|…` ; **S774 = « Ville de Guilde : Couloir de l'Esprit Gardien »** (`t_node.ini:2027-2035`) — donjon relié aux Reliques (boss Kamsky/Sofia/… spawns 60992-61001, `map-spawns.tsv:4485-4494`). **Aucun node « ville de guilde » elle-même** — la VDG est instanciée dynamiquement [HYPOTHESIS].
- `c_parameter.ini` : 129 params ; guild-adjacent : `362|100|公會上限` (`:11`), 565/578-580 職業-related, 409-411 小屋 storage (`:20-22`), 489-496 廣告/貢獻, 609-618 coûts.
- `Collection.ini`, `C_ElfCollect.ini` : présents mais système **elfe/sprite** (ordres de collecte confiés à un elfe — `misc-tables.tsv:15`), ≠ récolte VDG.

## 10. Chaîne complète NPC→shop→items (résumé)

`c_npc.ini` NPC → `#:Service <textId> 24 <storeId>` → `c_store.ini` storeId → `{itemId, nom, qté}` → `c_familycollect` (catalyseur→nœud+produits) / `c_familycombine` (parchemin→recette).

## 11. Parsers serveur existants

- `WorkOrderParser.cs` — **S_FamilyCollect.ini / S_FamilyCombine.ini** (schémas désasm documentés ci-dessus, `:21-100`) ; métiers 12-14 (collect) / 15-17 (combine).
- `CraftRecipeParser.cs` — `ParseFamilyCombine` (catégories 7 : `:33-40,151`).
- Aucun parser c_buliding/GuildFunction/4PHouse/decorate/c_territory/c_store trouvé côté Eden.Core [GAP].

## GAPs

- **[GAP] Tables `S_*` serveur absentes** du corpus (`find **/[Ss]_*.ini` = 0) : `S_FamilyCollect.ini`, `S_FamilyCombine.ini`, et probablement `S_buliding`, `S_Family`, `S_FamilyEvent`, `S_Store`… référencées par les parsers. Schémas partiellement connus via désasm (`WorkOrderParser.cs:5-18`).
- **[GAP] Pas de table de spawn VDG** : aucun fichier scene/spawn pour la ville de guilde instanciée ; seul le donjon S774 est mappé. Position des PNJ VDG inconnue dans les données (probablement côté serveur/scripts).
- **[GAP] Pas de table quêtes de guilde** (`c_familyquest`/`familymission` absents) ; quêtes guilde probablement dans `c_mission.ini` (non audité ici).
- **[GAP] Pas de table « level guilde » dédiée** (paliers 1-12 dans Useful_Info.md wiki, colonnes dans GuildFunction ; coût EXP/renommée introuvable en table).
- **[GAP] Schémas non décodés col-par-col** : `c_sceneevent` (20 cols), `c_territory` (22 cols), `c_transport` (57 cols — signification des paliers 75→625 + 'c1'), `c_node` (78 cols), `GuildFunction` (sémantique par ligne), `boundinfo` (8 floats).
- **[GAP] `c_familyevent`** = stub 1 row inutilisé.
- **[GAP] `4pequip.ini`/`4pequipmap.ini`** non ouverts (probablement équipement déco 4P ?) — à vérifier si besoin décorations.
- **[GAP] Accès VDG** : comment le joueur entre en ville (item/NPC/portail) non repéré dans c_transport ; hypothèse via NPC 50604 « Agent Immobilier » ou panneau guilde.
- Notes encodage : noms c_* en Big5 à décoder pour confirmer libellés exacts des boutiques 532-658 et des catégories c_familycombine (3/4/7).
