# Inventaire UI client liées guilde / craft (2026-11-07)

Dossier : `eden_godot/godot_project/scripts/` (layouts natifs : `corpus/fr-full/uidata/layouts/`, réf. : `ref-shots/`,
catalogue `eden_godot/docs/client-assets/ref-shots-catalog.md`).

## Guilde / ville de guilde
| Fenêtre | Fichier | Layout(s) natif(s) | Clé `@open` |
|---|---|---|---|
| Guilde (onglets Guilde/Messages/Rangs/Ville) + dialogues (don, annonce, commentaire, emblème, rangs, privilèges, création ville, candidature, recherche) | hud/GuildWindow.cs (1867 l.) | familywnd_na, familyinfownd_na, familymessagewnd_na, familyorderwnd_na, familycenter, familydonatedlg, familybulletindlg, familycommentdlg, familyjobnamedlg, familyprivilege, familysetprivilegedlg, cnfamilyemblem, familyappwnd, familyfindingwind/info, familymemberfindingwnd, familyadministration, familyachievement | guild |
| Liste des villes | hud/TownListWindow.cs | familytownlistwnd | townlist |
| Bâtiments / PNJ de la ville | hud/FamilyBuildingWindow.cs | familybuilding(_forest/_mountain) | familybuilding |
| Coffre de guilde (+ log) | hud/FamilyStorageWindow.cs, StorageItemCell.cs | familystoragewnd, familystoragetab1-3, familystoragelogwnd | familystorage |
| Hôtel/chambre de ville | hud/HotelWindow.cs | (hotel) | — |
| Territoire / croisade / guerre de guilde | hud/SystemServiceWindows.cs, BattlefieldWindow.cs | territory*, battlecrusadewnd, familybattle* | territory |
| Tableau de messages | hud/MessageBoardWindow.cs | familymessagewnd | — |
| Cartes (bâtiments, PNJ de ville) | hud/MinimapPanel.cs, RegionMapWindow.cs, WorldMapWindow.cs | zonemapui* | map |
| Donjons / tour | hud/DungeonUi.cs, TowerWindow.cs | instancedungeoncontrol | — |
| Ordres de travail / résultat | hud/CraftWindow.cs (+ liste), `worklistwnd`, `workresultwnd` | worklistwnd, workresultwnd | — |

## Craft / objets
CraftWindow (récolte/invocation/fabrication via PNJ de ville), CombineWindow (fusion/compo), StrengthenWindow (fortification),
LifeSkillWindow (métiers), InventoryWindow + ItemSlotCell + ItemTooltip (cases d'objets), BankWindow, AuctionWindow, VendingWindow,
MagicBoxWindow, MagicCubeWindow, FeatureDialogWindow (dialogues de service PNJ), ClassWindow (or/argent), ShopWindow.
Données : data/tables/CraftTable.cs, HouseTable.cs, StoreTable.cs, ItemTable.cs, data/ItemSources.cs, TextResolver.cs, UiTextEn.cs.

## Revue visuelle (captures `/tmp/ui/*.png`, avant) — défauts et état
Attention : `@open` force `Visible=true` sur tous les Panels enfants (`HudRoot.ForceOpenExt`) : certains sous-panneaux visibles
(ex. « StrengthenFactory » dans Amélioration) sont des artefacts de capture, pas du jeu réel (à revérifier via PNJ).
| UI | Défaut | État |
|---|---|---|
| Toutes fenêtres ouvertes via bouton | s'ouvraient DERRIÈRE la fenêtre guilde | CORRIGÉ : `HudWindows.Front` (TownList, FamilyBuilding, FamilyStorage, Combine, Strengthen, LifeSkill) |
| Guilde : bouton ville | « Ville guilde » / jamais « Entrer en ville » | CORRIGÉ (« Entrer en ville » / « Quitter ville ») |
| Guilde : « Villes de guilde » | ouvrait la recherche de guilde | CORRIGÉ → liste des villes |
| Fusion (Combine) | anglais « Time Required », « Chance to receive », « Test999 », placeholders « 123456 / 999 » | CORRIGÉ (`PolishDetailStatics` partagé) |
| Bâtiments | titres « Collection de bâtiments / contrats de personnages » débordaient | CORRIGÉ (« Bâtiments », « Contrats de PNJ ») |
| Coffre familial | cases quasi invisibles (gris sur gris) | CORRIGÉ (fond violet + liseré) |
| Amélioration | bandeau d'avertissement débordant, colonne « Palie » | CORRIGÉ (texte raccourci, « Pal. ») |
| Hôtel des ventes | colonne « Niv. » tronquée « Ni » | CORRIGÉ (largeurs) |
| Guerre de territoire | « Guilde propriét… » tronqué | CORRIGÉ (« Guilde ») |
| Tooltip/annonce | « 招募中公會 » | CORRIGÉ (« Guildes en recrutement ») |
NON corrigé (constaté) : légendes de slots PNJ du panneau Bâtiments (« Apprenti Alchimiste » coupé, police minuscule) ; Territoire : grande zone
vide à gauche (carte absente) ; Artisanat racial : « fois » recouvre le champ quantité ; Hôtel des ventes : « Orange/Violet » dépassent
de la boîte Qualité, champs « NV [ ]:NV [ ] » superposés ; onglet « Ville » de la guilde vide ; aucune revue de : Messages/Rangs, dialogues
(donation, annonce…), liste de candidatures, croisade, hôtel, donjon (DungeonUi/Tower), craft via PNJ réel (CraftWindow ferme/mine/autel/alchimie).

## Données préparées (2026-11-07 02:10)
- GM étendu : `//hall_setup [familyId] [reset] [rebuild] [set2]` (rebuild = repart de zéro au niveau du template max ; set2 = mare/hôtel/marché).
- Guilde niv. 12 pour 5001 et 5000 (`//guild_level`). Ville A (QAForge #5001, hall 502) : ferme, mine, autel, alchimie, fonderie, tous niv. 12,
  housekeepers + 4 PNJ de service par bâtiment. Ville B (livetest #5000, hall 501) : mare magique, hôtel de brique, marché, niv. 12 ; mare : housekeeper + 4 PNJ.
  Hôtel/marché : AUCUN PNJ posé (pas de mapping contrat → PNJ marchand dans les données FR ; 18 contrats seulement).
- Sac du perso : 25 cases libres (outils pioche/gants/boule conservés).
