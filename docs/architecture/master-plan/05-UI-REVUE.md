# 05 — Revue UI guilde / craft / donjon (état au 2026-11-07, à poursuivre)

Source de départ : `docs/temp-plans/2026-11-07-ui-guilde-craft-inventaire.md` (inventaire des 22 fenêtres + défauts). Captures de la session : `/tmp/ui/*.png` (éphémères — refaire).

## Méthode imposée pour la passe suivante
1. Redémarrer un client PROPRE par fenêtre testée si `@closeall` a servi (il masque les wrappers `*Window`).
2. Ouvrir par le chemin joueur réel : touche G / menu / clic droit PNJ + option de dialogue / clic droit objet. N'utiliser `@open <clé>` que pour une capture rapide et vérifier ensuite par le chemin réel (ForceOpenExt force `Visible=true` sur tous les sous-panneaux).
3. Par fenêtre : `@shot` global, `@cjk <nom>` (zéro chinois, `[i18n-miss]` dans /tmp/client.log), comparer au layout natif (`corpus/fr-full/uidata/layouts/<x>.layout`) et à `ref-shots/` + catalogue, vérifier : texte qui dépasse/tronqué, non traduit, case de slot peu évidente, ordre d'empilement, déplacement/fermeture (X, bouton Fermer, Échap), un seul dialogue à la fois, états vides/pleins, polices lisibles.
4. Corriger dans le code (pas dans les layouts du corpus, en lecture seule) puis recapturer (2 tours).

## Exigences transverses (R3, U1)
- **Z-order** : registre unique de fenêtres ; `Front()` à l'ouverture ET au clic pour TOUTE fenêtre/sous-dialogue (TownListWindow, FamilyBuildingWindow, FamilyStorageWindow, dialogs de GuildWindow, CraftWindow détail/liste d'ordres/résultat, StrengthenWindow, ItemTooltip au-dessus de tout). Voir `HudWindows.cs` (Register/Open/Front/OnPress), `DragHelper.cs`, `HudRoot.cs` (ForceOpenExt, Mount). Une 1re tentative (appels `HudWindows.Front(_window)` ajoutés dans Combine/Strengthen/FamilyStorage/FamilyBuilding/TownList/LifeSkill) n'a PAS suffi d'après le propriétaire : cause à établir (voir 01).
- **Échap** ferme la fenêtre au premier plan (pas encore tranché/implémenté) ; un seul dialogue modal de guilde à la fois.
- **Cases de slots** : toute case d'objet doit être repérable à vide (liseré + fond) — fait pour `StorageItemCell` ; vérifier `ItemSlotCell` (sac, craft, combine, strengthen, vending, auction, bank).
- **Textes** : zéro CJK, FR par défaut ; tout nouveau texte passe par `UiTextEn`/tsv (clé stable).

## Fenêtres : état et reste à faire
| UI | Fichier / layout | Déjà fait | Reste |
|---|---|---|---|
| Guilde (4 onglets) | GuildWindow.cs / familywnd_na, familyinfownd_na, familymessagewnd_na, familyorderwnd_na | « Entrer/Quitter ville », « Villes de guilde » → TownList, unité d'or /1000, « 招募中公會 » FR | onglet **Ville** vide (taxes + 3 cases « Urbanisme » : natif = liste/commandes de ville, vérifier layout `familycenter`) ; Messages, Rangs, privilèges ; texte « Fonds » (coin or/argent minuscule) ; polices ; contrôler 12 niveaux (grades +1/niveau jusqu'à 6, capacité 200→350) |
| Dialogues guilde | GuildWindow.cs ShowDonateDlg/ShowBulletinDlg/ShowCommentDlg/ShowEmblemDlg/rank/privilege/ShowTownCreateDlg/join | don en or×1000 | ouvrir et capturer UN PAR UN ; titres FR ; boutons Confirmer/Annuler ; Échap ; blason (aucun éditeur) |
| **Liste des villes (Find Town)** | TownListWindow.cs / familytownlistwnd | z-order partiel, ouverture depuis « Villes de guilde » | **R4** : afficher bâtiments actifs (type+niveau+durabilité), PNJ actifs (nom, rang, expiration), vendeurs présents, emplacements libres, niveau de guilde, membres, réputation, type forêt/montagne ; filtres natifs (Ferme/Mine/Autel/Alchimie/Forge/Mana Pool, Niv Min/Max, Rechercher, Entrer) ; extension protocole serveur (voir 01 §4) ; libellés « Min/Max » qui se chevauchent |
| Bâtiments de la ville | FamilyBuildingWindow.cs / familybuilding(_forest/_mountain) | titres raccourcis, `Front` | légendes de slots PNJ tronquées/minuscules (« Apprenti Alchimiste ») ; cadres d'aire (layouts sans image) ; cellule vide en tête ; noms de plans FR |
| Coffre de guilde + journal | FamilyStorageWindow.cs, StorageItemCell.cs / familystorage* | cases visibles | style gris non natif des onglets/Journal ; libellé « Inventaire » ; vérifier retrait/dépôt |
| Craft : détail ordre (Ferme/Mine/Invocation/Alchimie/Fonderie/Mare) | CraftWindow.cs (collectwnd/combinewnd/worklistwnd/workresultwnd) | `PolishDetailStatics` (FR) | liste d'ordres + résultat : fenêtres qui s'empilent et ne se ferment pas (`@closewin`) ; cases formule/charme évidentes ; durée/coût ; messages (0x9f1 « Cet objet ne peut être placé ici » = texte natif trompeur) |
| Fusion (Combine) | CombineWindow.cs | FR via `PolishDetailStatics` | champ « fois » qui recouvre la quantité (Artisanat racial) |
| Amélioration | StrengthenWindow.cs | bandeau raccourci, « Pal. » | sous-fenêtre StrengthenFactory (artefact ?) ; colonnes ; boutons |
| Hôtel des ventes | AuctionWindow.cs | colonne « Niv. » | cases qualité (« Orange », « Violet » dépassent), champs niveau superposés, prix |
| Territoire / croisade | SystemServiceWindows.cs, BattlefieldWindow.cs | « Guilde » | zone gauche vide (carte absente) ; HUD de guerre inexistant ; croisade |
| Cartes | MinimapPanel.cs, RegionMapWindow.cs, WorldMapWindow.cs | bâtiments + niveaux + PNJ en ville (minicarte + régionale) | carte du monde ; icônes natives/légende ; liste de recherche de la régionale (« Other (4) ») ; rafraîchir niveaux après montée de bâtiment |
| Donjon / tour | DungeonUi.cs, TowerWindow.cs / instancedungeoncontrol | — | non revus du tout : fenêtre d'entrée, listes, textes FR |
| Sac / tooltips | InventoryWindow.cs, ItemSlotCell.cs, ItemTooltip.cs | or/argent /1000 | tooltip durabilité en retard sur le serveur (176 vs 174) ; infobulle outil de récolte |
| Autres | BankWindow, VendingWindow, MagicBoxWindow, MagicCubeWindow, LifeSkillWindow, MailWindow, MessageBoardWindow, FeatureDialogWindow | — | revue complète |
