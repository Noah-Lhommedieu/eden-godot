# VDG : matrice bâtiments / PNJ / plans (2026-11-06)

Méthode : sources autorisées uniquement (code, décompilé `re/reindex`,
`corpus/fr-full`, guides-fr). Chaque ligne cite sa preuve. Les rapports
`docs/reports/craft-guild-recon-2026-11-03/` et anciens `temp-plans`
n'ont PAS été utilisés.

## 1. Parcours joueur (guide `guilde-et-ville-de-guile.md` + `le-craft.md`)

1. Fondation guilde : niveau perso >= 20, 50 po, sans guilde, PNJ Heldin
   (Aven 478,468). Serveur : `ZoneEntryFlow.Family.cs:175` `GainGold(-50000)`.
2. Guilde niv. 3 (gloire 7 000, `c_level` col11) = débloque ville + insigne + 1er entrepôt.
3. Fondation ville : chef seul, perso niv. >= 30, guilde niv. >= 3, >= 30
   membres, trésorerie >= 500 po (10 000 po si 50 villes ; VIP 10 M).
   Dialogue NPC service 21 -> 501 -> `GuildWindow.ShowTownCreateDlg` -> C->S 173
   -> `HandleFamilyInstanceCreateAsync` (0x599 niv. perso, 0x598 chef, 0x91a
   déjà ville, 0x59a niv. guilde, 0x6ae fonds) -> relais monde (30 membres, cap 49 villes).
4. Maintien : 30 membres + 10 connexions/semaine (guide, non vérifié côté serveur : doute).
5. Entrer en ville : (a) bouton `EnterTerritoryBtn` de la fiche guilde (176 avec
   son family id), (b) liste `TownListWindow` (514 -> 174 -> 502 ; 175 -> 503 ;
   176 avec l'id choisi), (c) PNJ. Sortir : même bouton, 176 avec -1.
6. Dans la ville : le chef pose bâtiments (conteneur 1) et contrats PNJ
   (conteneur 2) via `FamilyBuildingWindow` (130/131), max 5 bâtiments
   (groupes de locs 0/10/20/30/40) + relique (loc 0x33).
7. Craft : parler au PNJ fabricant -> 496 `ClientEnterWorks` -> commande 170.
   Niveau de travail requis de la recette <= niveau du bâtiment ; durabilité >= 1 ;
   taxe de ville si guilde étrangère. Aven : niveaux 1-2 seulement.

## 2. Bâtiments (`c_buliding.ini`, identique à `S_Buliding.ini`, 136 lignes)

NB : l'en-tête `|V.1|12|` = 12 COLONNES, pas 12 types. Types réels : 1, 2, 4-10.

| type | modèles | rôle (col12 CN) | niveaux | durabilité max (col8) | réparation/pt (col7, niv 3->12) | PNJ attaché (col6) |
|---|---|---|---|---|---|---|
| 1 manoir | H00101 (forêt), H00301 (montagne) | mairie / clerc | 3-10 (suit niv. guilde) | - | - | 35001 Clerc de la Ville |
| 2 maison ("hôtel de brique" du guide ?) | H00401/501/601 | repos des PNJ marchands | 3-... suit niv. guilde | - | - | aucun |
| 4 Ferme | H01001 | cultures, ordres niv. travail <= N | 3-12 | 200 | 173,219,238,256,441,706,1065,1538,3076,3076 | 35002 |
| 5 Mine | H01101 | minerais | 3-12 | 200 | idem | 35012 |
| 6 Autel | H01201 | invocations | 3-12 | 150 | idem | 35022 |
| 7 Labo alchimique | H01301 | gemmes, potions | 3-12 | 100 | 346,438,475,513,882,1413,2131,3076,3076,3076 | 35030 |
| 8 Fonderie | H01401 | armes, armures | 3-12 | 100 | idem | 35039 |
| 9 Mare magique | H01501 | bijoux, enchantements | 3-12 | 100 | idem | 35068 |
| 10 Marché | H00701/801/901 | marchands externes | 3-... suit niv. guilde | - | - | aucun |

Preuves : colonnes lues dans `c_buliding.ini` (script de dépouillement) ; description CN
col12 "可接受工作等級 N 以下的委託" = « accepte les commandes de niveau de travail <= N ».

Montée de niveau d'un bâtiment de production : EXP de bâtiment (recette `BuildingExp`)
>= seuil `c_level` cols 12-17 de la ligne niveau+1, ET niveau bâtiment < niveau de guilde
(`FamilyBuildingWorkRules.ApplyBuildingExp`, natif `CFamily::UpdateBuildingExp`).
Seuils (identiques pour les 6 stations) : 3->4 1 440 ; 4->5 4 800 ; 5->6 12 000 ;
6->7 16 800 ; 7->8 22 400 ; 8->9 28 800 ; 9->10 36 000 ; 10->11 44 000 ; 11->12 52 800.
Fonds de guilde pour niveau de guilde (col18) : lv4 3 400 000, lv5 16 M, lv6 54 M, lv7 80 M... soit
3 400 po, 16 000 po, 54 000 po... = tableau du guide : trésorerie en unités de 1/1000 po.

Capacité PNJ par niveau de bâtiment (`FamilyHallRules`, .rodata natif) :
production (4-9) lv3=4, lv4=5, lv5=6, lv6+=8 ; maison (2) lv3=2, lv4+=3 ; marché (10) lv3=0,
lv4-5=1, lv6-7=2, lv8+=3 (guide : marché 3 emplacements max : concordant).

## 3. PNJ de service (contrats S_Item kind 0x2a, dump serveur)

| station | PNJ fabricant (contrat -> npc) | durée contrat |
|---|---|---|
| Ferme (13) | 35002 Fermier Novice (permanent), 35003 Fermier (3 j), 35004 Maître Fermier (3 j) | 0 / 259 200 s |
| Mine (12) | 35012 Apprenti Mineur, 35013 Mineur, 35014 Maître Mineur | 0 / 3 j / 3 j |
| Autel (14) | 35022 Invocateur (un seul) | 0 |
| Labo (15) | 35030 Apprenti Alchimiste, 35031 Alchimiste, 35032 Maître Alchimiste | 0 / 3 j / 3 j |
| Fonderie (16) | 35039 Maître du Moulage, 35040 Maître Vétéran du Moulage, 35041 Forgeron Renommé | 0 / 3 j / 3 j |
| Mare (17) | 35068 Artisan Novice, 35069 Artisan, 35070 Maître Artisan | 0 / 3 j / 3 j |

Les tiers 2 et 3 réduisent le taux d'échec de 10 % et 20 % (guide `le-craft.md`).
PNJ marchands de plans : 273 contrats avec service type 24 (boutique de prescriptions),
durée 7 j (604 800 s) ou 3 j. Leur niveau de plan n'est PAS limité par le niveau du bâtiment
(guide : « les marchands de plans et formules peuvent être d'un niveau supérieur »).
Aucun contrôle type-de-bâtiment/type-de-PNJ au natif : `CAlchemyShop::OpenShop` @0x08577880
ne lit que le NIVEAU du bâtiment (`+0x94`) et l'expiration du contrat (0xc56). Un PNJ
fabricant posé dans n'importe quelle aire hérite donc du niveau de CETTE aire.

## 4. Plans acceptés selon le niveau du bâtiment (`c_familycollect` / `c_familycombine`)

Nombre de recettes dont le niveau de travail requis = N (col5 collect, col2 combine) ;
une recette est faisable ssi niveau requis <= niveau du bâtiment. Station déterminée par
le kind du catalyseur (0x32 ferme, 0x33 mine, 0x34 autel, 0x35 labo, 0x36 fonderie, 0x37 mare).

| Niv. bât. | Ferme | Mine | Autel | Labo | Fonderie | Mare |
|---|---|---|---|---|---|---|
| 1 | 5 | 6 | 5 | 9 | 49 | 8 |
| 2 | 4 | 4 | 4 | 20 | 62 | 16 |
| 3 | 10 | 10 | 10 | 37 | 98 | 32 |
| 4 | 10 | 10 | 10 | 17 | 141 | 28 |
| 5 | 14 | 14 | 12 | 29 | 155 | 20 |
| 6 | 14 | 14 | 12 | 29 | 262 | 73 |
| 7 | 18 | 18 | 14 | 30 | 201 | 73 |
| 8 | 14 | 14 | 12 | 38 | 229 | 76 |
| 9 | 14 | 14 | 12 | 45 | 209 | 100 |
| 10 | 14 | 14 | 12 | 21 | 202 | 71 |
| 11 | 14 | 14 | 12 | 5 | 206 | 63 |
| 12 | 14 | 14 | 12 | 0 | 140 | 71 |
| total | 145 | 146 | 127 | 280 | 1954 | 631 |

- Niveau 1-2 : faisables à Aven (zone industrielle) ; niveau >= 3 : ville de guilde seulement.
- Collecte ferme/mine : durée 1-20 min (niv. 1) puis 25 min (niv. 2), 30 min (niv. >= 3) ; autel :
  instantané (cast 2,5 s FR ; 5 s dans l'oracle US avant correctif) ; combine : cast 5-10 s.
- Usure bâtiment par ordre : cols `BuildingDamage/Chance` de chaque recette.

## 5. Application côté serveur

| règle | preuve native | code serveur | état |
|---|---|---|---|
| bâtiment du PNJ résolu (0x251 sinon) | `CheckToCombine` @0x08163cd0 | `ZoneEntryFlow.Work.cs` (bloc "Gates famille") | OK (+ cas "dans un hall sans bâtiment lié" ajouté) |
| niveau bâtiment < niveau requis -> 0x392 | idem `+0x94 < byte(param_4[8])` | idem | OK |
| durabilité < 1 -> 0xa09 | idem `+0xc < 1` | idem | OK |
| HORS hall : niveau requis > 2 -> 0x9f1 | `CheckToCombine` `2 < (byte)param_4[8]` ; `CheckToCollect` @0x08162a00 `2 < param_4+0x14` | `ZoneEntryFlow.Work.cs` `inHall` | CORRIGÉ (absent avant : on craftait niv. 3+ à Aven) |
| contrat PNJ expiré -> 0xc56 | `CAlchemyShop::OpenShop` | `ZoneEntryFlow.Dialogue.cs` cases 12-17 | OK |
| taxe : gold + round(taux x gold/100) | `CheckToCombine` | `FamilyBuildingWorkRules.TaxedGold` | OK |
| taxe créditée à la ville visitée (0xd15) | `CCharacter::FamilyLevyBusinessTaxes` @0x0840c960 | `GuildService.CreditBusinessTaxAsync` + `ConsumeWorkInputsAsync` | CORRIGÉ (le joueur payait la taxe mais la guilde ne recevait rien) |
| usure au démarrage (non instant.) / à la fin (instant.) | `ToCombine`/`End*` | `Work.Family.cs` | OK |
| EXP bâtiment, level-up borné au niveau de guilde | `UpdateBuildingExp` | `Work.Family.cs` | OK |
| durées/catégories = tables du client FR | politique `ReforgedDataLocator` | `GameDataService.OverlayFrWorkOrders` | CORRIGÉ : 100 durées autel (50 -> 25) et 130 catégories divergeaient (S_FamilyCollect/Combine US) |
| cap PNJ par niveau (0x8ef), pose (0x535, texte 1) | `CheckNpcSizeLimit` @0x8464ea0 | `FamilyHallRules` | OK |
| coffres de guilde lv 3/5/6 | guide (table niveaux) | `GuildService.CheckPrivilege` (11->lv3, 12->lv5, 13->lv6) | OK |

## 6. Corrections client

- `GuildWindow` bouton « Ville guilde » (`EnterTerritoryBtn`) : natif `FamilyInfoWnd` @0x00a98b10
  = grisé si `house_open` (famille+0xd8) == 0 ; libellé « Ville guilde » (texte 0x2f) hors ville,
  « Quitter ville » (0xb53) quand le node est de type 4 (`FUN_0071ef30`). Avant : toujours actif,
  envoyait le 176 de SA famille même dans la ville, aucune sortie possible par le panneau.
  Désormais : 176 avec -1 dans un hall (serveur `HandleFamilyTownEnterAsync`), désactivé sans ville.
- `TownListWindow` : message si 0 résultat / aucune sélection ; ferme la liste à l'entrée.
- `FamilyBuildingWindow` : layout montagne (`familybuilding_mountain.layout`) pour house_open 502
  (toujours forêt avant).
- `ShowTownCreateDlg` : conditions de fondation du guide (chef, niv. 30, guilde niv. 3, 30 membres).

## 7. Doutes

- Unité de la trésorerie : le natif (`FamilyInfoWnd` @0x00a98b10) affiche `tresor/1000` en or et
  `tresor%1000` en argent ; le guide donne 3 400 po pour 3 400 000 (c_level col18). `GuildWindow`
  affiche /10000 et le don multiplie par 10000 : écart x10 non corrigé (preuve de l'unité des
  PIÈCES du joueur manquante) ; coût de fondation affiché « 50 or » vs 500 po du guide.
- Maintien de ville (30 membres, 10 connexions/semaine, mail d'alerte) non trouvé côté serveur.
- Type 2 : le guide parle d'« hôtel de brique : 8 emplacements » mais `HouseNpcLimit` plafonne à 3.
- Texte 0xd15 : argument or non formaté comme `NormalizeGoldString` natif (valeur brute).
- Rien n'a été lancé en jeu (client non compilé par moi).
