# Dictionnaire d'items EDENFR

Dictionnaire des items du jeu construit **uniquement** depuis `corpus/fr-full/data/db` (aucune donnée EN/US).
Il contient 26735 lignes, une par identifiant d'item présent dans au moins une des quatre tables d'items.

- Données : `docs/reference/items.tsv` (UTF-8, une ligne d'en-tête, séparateur tabulation, aucun tab/retour ligne dans un champ ; les retours ligne des descriptions sont remplacés par ` ¶ `).
- Générateur : `tools/item-dictionary/ItemDictionary` (constructeur dans `ItemDictionaryBuilder.cs`).
- Test : `tests/Eden.Core.Tests/ItemDictionaryTests.cs` (ids uniques, nom et libellé de type non vides, pas de tab/retour ligne).

Régénérer (environ 3 s) :

```
dotnet run -c Release --project tools/item-dictionary/ItemDictionary -- corpus/fr-full docs/reference/items.tsv [rapport.txt]
dotnet test tests/Eden.Core.Tests --filter "FullyQualifiedName~ItemDictionaryTests"
```

Sans 3e argument, le programme imprime sur la sortie standard le rapport de comptages (types, niveaux, qualités, références croisées) dont les chiffres ci-dessous sont tirés.

## Méthode

1. Lecture avec les classes de `src/Eden.Core` plutôt que des suppositions : `IniTable.ParseFile` (MyCrypt si besoin, puis UTF-8 strict sinon Latin-1 pour les `t_*` / Big5 pour les `c_*`), `ItemTable.Load` (colonnes de `c_item.ini` / `c_itemmall.ini`), `TextTable.Load` (tables `t_*` : col 1 nom, col 2 description), `CraftTable.LoadCombine/LoadCollect`, `MissionTable.ParseScript`.
2. Le sens des colonnes vient des commentaires de preuve de ces lecteurs et de `ItemTipFormat` (désassemblage du constructeur de tooltip), puis est recoupé sur des objets connus (voir « Colonnes »).
3. L'ensemble d'ids est l'union de `c_item`, `t_item`, `c_itemmall`, `t_itemmall`. Les deux familles ne se recouvrent presque pas (un seul id, 40175, est dans `t_item` et `t_itemmall`). Pour un id présent dans les deux tables texte, le nom et la description du mall l'emportent (règle de `TextResolver.ItemName`).
4. Le nom et la description FR viennent des tables `t_*` ; `TextResolver.Clean` retire les marqueurs `$N$` (couleur/format). Aucun code `$..$` ne subsiste dans le TSV. Les références `&id&` des descriptions sont laissées telles quelles (item produit à incruster, voir `ItemTipFormat.TryItemRef`).
5. Les libellés de type, d'emplacement et de qualité sont lus dans `t_textindex.ini` FR, avec les correspondances kind -> id de la table native `ItemTipFormat.KindLabel`. Quand aucune entrée n'existe, le libellé est **dérivé** des noms d'items observés et la colonne `type_libelle_source` vaut `derive` (sinon `t_textindex:<id>`).
6. Les références croisées (boutiques, butin, quêtes, recettes, collectes, partenaires) sont calculées sur les tables elles-mêmes avec la grammaire déjà utilisée par `StoreTable`, `ItemSources` et `MissionTable`.

## Colonnes de `items.tsv`

| Colonne | Contenu | Source et preuve |
|---|---|---|
| `id` | identifiant d'item | col 0 de toutes les tables d'items |
| `nom` | nom FR nettoyé ; `(sans nom)` si absent | `t_itemmall` col 1, sinon `t_item` col 1 (`TextResolver.ItemName`) |
| `famille` | regroupement lisible (Armure, Arme, Objet, Costume...) | calculé depuis `type_code` (table native des kinds) |
| `type_code` | kind brut | `c_item`/`c_itemmall` col 8 (`ItemTable.ItemDef.Kind`, CItemData+0x20) ; vide si l'id n'a pas de ligne `c_*` |
| `type_libelle` | libellé FR du kind | `t_textindex` (ex. 1158 « Armure Lourde », 1034 « Gemme », 3837 « Petit Meuble ») ou dérivé |
| `niveau_requis` | niveau minimal pour utiliser/équiper ; vide = aucun | `c_item` col 14 (`RequiredLevel`). Recoupé : Arc Invocateur de Tornades (20675) = 65 |
| `niveau_max` | niveau maximal d'utilisation ; vide = pas de plafond | col 15 (`MaxLevel`) |
| `qualite_code` / `qualite_libelle` | rareté 1 à 6 ; vide = aucune | col 19 ; libellés `t_textindex` 1152 à 1157 (Gris, Vert, Orange, Bleu, Doré, Violet), ancres de `ItemTipFormat.QualityTier` |
| `emplacement_code` / `emplacement_libelle` | slot d'équipement | col 9 (`EquipLoc`) ; libellés `t_textindex` 1047 à 1066 pour les codes 1 à 20 (ancres de `ItemTipFormat.LocLabel`). Codes 22, 23, 30, 32, 33, 34, 35 : libellé dérivé du contenu ; 21, 24 à 29, 31 : « non documenté » |
| `classes_ids` / `classes_noms` | restriction de classe ; vide = aucune restriction connue | col 77 lue comme masque 64 bits, bit (idClasse - 1), noms dans `t_class.ini` col 1. **Déduit des données, pas du désassemblage** : même convention que `S_Spell` col 23 (`ClassSkillTable`), et tous les arcs listent des classes de la famille Chasseur, les bâtons des classes de Magicien |
| `taille_pile` | taille de pile maximale ; vide = non précisée | col 29 (`MaxStack`) |
| `liaison` | `echange_interdit` (bit 0x4 de col 11), `lie_a_l_equipement` (bit 0x80) | `ItemTipFormat.FlagCantTrade` / `FlagBindOnEquip` |
| `prix_systeme` | prix système (achat PNJ, base de revente) | col 31 (`SellPrice`) |
| `durabilite_max` | durabilité d'équipement | col 74 (`MaxDurability`) |
| `stats` | bonus d'équipement en clair (ATQ PHY, DÉF, FOR..., résistances, sertissages max) | cols 37 à 72, noms `t_textindex` 1180 à 1210 |
| `effet_usage` | texte de l'effet appliqué à l'usage (potions, certificats, gemmes) | col 23 (`UseEnchantId`) joint à `t_enchant.ini` puis `t_itemmallenchant.ini` |
| `description` | description FR nettoyée | `t_itemmall` col 2, sinon `t_item` col 2 |
| `code_item` / `icone` | code (W10102, A10037, I00359...) et icône UI | cols 1 et 4 |
| `tables_source` | tables où l'id existe, séparées par `+` | calculé |
| `reference_par` | tags `boutique`, `butin`, `quete`, `recette_materiau`, `recette_produit`, `collecte_catalyseur`, `collecte_produit`, `partenaire` | références croisées (voir plus bas) |
| `nom_source` | `t_itemmall`, `t_item` ou `absent` | calculé |
| `type_libelle_source` | `t_textindex:<id>` ou `derive` | calculé |
| `nom_big5_c_item` | nom Big5 (chinois) de `c_item` col 7, conservé pour mémoire | non utilisé comme nom FR |
| `anomalies` | `nom_absent`, `nom_test`, `sans_c_item`, `sans_description`, `enchant_usage_sans_texte` | calculé |

Colonnes volontairement absentes : le **niveau d'objet distinct** (aucune colonne de `c_item` ne le prouve ; col 35 et col 36 prennent des valeurs liées au niveau sur l'équipement mais servent aussi d'id de sort ou d'enchant, donc non exploitées) et la durée de vie des objets temporaires (cols 26 et 27, présentes dans `ItemParser` serveur mais sans lecteur côté Core).

## Tables utilisées

| Table (`corpus/fr-full/data/db`) | Rôle |
|---|---|
| `c_item.ini` (18 794 lignes) | attributs numériques des items de base |
| `t_item.ini` (18 902) | noms et descriptions de base |
| `c_itemmall.ini` (7 461) | attributs des items de la boutique d'objets |
| `t_itemmall.ini` (7 619) | noms et descriptions de la boutique |
| `t_textindex.ini` | libellés FR de type, emplacement, qualité, stats |
| `t_class.ini` | noms des classes (ids 1 à 61 pour les masques) |
| `t_enchant.ini`, `t_itemmallenchant.ini` | texte de l'effet d'usage |
| `c_partner.ini`, `t_partner.ini` | familiers (indexés par id d'item) : enrichit `tables_source` et `reference_par` |
| `c_store.ini`, `c_dropitem.ini`, `c_mission.ini`, `c_familycombine.ini`, `c_familycollect.ini` | références croisées uniquement |

Tables d'équipement examinées mais **non sources d'items** : `Equip.ini`, `NewEquip.ini`, `SuitEquip.ini`, `ElfEquip.ini` sont vides (en-tête `[INFO]` / `[Table...]` seulement). `weapon.ini` (1 669 entrées), `4pequip.ini` (2 491), `ridelist.ini` (179) sont des tables de modèles/textures keyées par code : 2 009 des 2 010 armes du dictionnaire ont leur `code_item` dans `weapon.ini` (ce qui confirme col 1 = code de modèle), mais les codes d'armures de `c_item` ne se retrouvent pas dans `4pequip.ini` (16 sur 2 519) ; aucun attribut d'item n'y figure. Les montures n'ont donc pas de colonne dédiée : ce sont des objets de type « Objet » (kind 44) dont l'effet d'usage porte la clé de monture (`c_itemmallenchant`) ; seuls leurs noms et descriptions les identifient. `c_itemchest.ini`, `c_itemcombo.ini`, `C_Lottery.ini`, `C_LuckyBag.ini`, `c_equipset.ini` ne sont pas croisés (leur colonne d'item n'est pas prouvée dans le code existant).

## Comptages

Items : **26735**. Familles :

| Famille | Items |
|---|---|
| Objet | 6819 |
| Matériau/Formule | 4837 |
| Costume | 3615 |
| Coffre/Panoplie | 3063 |
| Armure | 2519 |
| Arme | 2010 |
| Accessoire | 1095 |
| Gemme | 576 |
| Familier | 559 |
| Autre | 481 |
| Bâtiment/PNJ/Sac | 383 |
| Mobilier | 273 |
| Certificat | 156 |
| Classe | 142 |
| Bouclier | 138 |
| Invitation | 25 |
| Parchemin | 25 |
| Esprit gardien | 18 |
| Teinture | 1 |

Par type (kind brut, libellé FR) ; les kinds d'un même libellé sont regroupés (armures : 1 à 15 par pièce et par tiers, costumes : 61 à 68) :

| Kinds | Libellé | Items |
|---|---|---|
| — | Type inconnu (sans c_item) | 480 |
| 0 | Sans type (kind 0) | 1 |
| 1, 4, 7, 10, 13 | Armure Lourde | 673 |
| 2, 5, 8, 11, 14 | Armure Légère | 882 |
| 3, 6, 9, 12, 15 | Robe de Protection | 772 |
| 16 | Retour | 192 |
| 17 | Poignard | 118 |
| 18 | Rapière | 145 |
| 19 | Épée | 139 |
| 20 | Katana | 99 |
| 21 | Épée Colossale | 139 |
| 22 | Hache | 112 |
| 23 | Lances | 63 |
| 24 | Masse | 131 |
| 25 | Griffe | 97 |
| 26 | Gourdin | 100 |
| 27 | Marteau | 147 |
| 28 | Arc | 139 |
| 29 | Pièce d'Artillerie | 113 |
| 30 | Faucille-fusil | 13 |
| 31 | Guitare | 159 |
| 32 | Bâton | 140 |
| 33 | Grimoire | 156 |
| 34 | Bouclier | 138 |
| 37 | Collier | 224 |
| 38 | Anneau | 338 |
| 39 | Trophée | 533 |
| 40 | Certificat | 156 |
| 41 | Bâtiment | 14 |
| 42 | PNJ | 352 |
| 43 | Sac à dos | 17 |
| 44 | Objet | 6819 |
| 45 | Gemme | 576 |
| 46 | Parchemin | 25 |
| 47 | Panoplie | 763 |
| 48 | Sac de Récompenses | 2300 |
| 49 | Matériel de Fusion | 275 |
| 50 | Matériel Agricole | 149 |
| 51 | Matériel Minier | 150 |
| 52 | Matériel d'Invocation | 129 |
| 53 | Matériel d'Alchimie | 295 |
| 54 | Matériel de Fonderie | 1975 |
| 55 | Matériel de Mare Magique | 639 |
| 56 | Formule d'Enchantement | 614 |
| 58 | Parchemin de compétence (type 58, dérivé des noms) | 611 |
| 59 | Teinture (mall) | 1 |
| 60 | Familier | 449 |
| 61, 62, 63, 64, 65, 66, 67, 68 | Costume | 3593 |
| 69 | Petit Meuble | 72 |
| 70 | Grand Meuble | 63 |
| 71 | Tapis | 18 |
| 72 | Décorations Murales | 88 |
| 73 | Décorations Suspendues | 2 |
| 74 | Papier Peint | 8 |
| 75 | Sol : Salon | 10 |
| 76 | Sol : Chambre | 12 |
| 77 | Invitation de banquet | 23 |
| 78 | Glyphe de Classe | 123 |
| 79 | Enchantement de costume | 8 |
| 80 | Expertise de classe | 19 |
| 81 | Équipement de familier | 110 |
| 82 | Esprit Gardien (Temps Limité) | 18 |
| 83 | Invitation de mariage | 2 |
| 84 | Parchemin de costume (mall) | 14 |

Par tranche de niveau requis (vide = pas de niveau requis ; le niveau maximal observé est 100) :

| Tranche | Items |
|---|---|
| aucun | 18064 |
| 1-10 | 191 |
| 11-20 | 275 |
| 21-30 | 467 |
| 31-40 | 591 |
| 41-50 | 1011 |
| 51-60 | 1536 |
| 61-70 | 1699 |
| 71-80 | 1546 |
| 81-90 | 1299 |
| 91-100 | 56 |

Parmi les 18 064 items sans niveau requis, seuls 53 sont des pièces d'équipement (armure, arme, bouclier, accessoire) ; 5 709 équipements ont un niveau. 169 items ont un niveau maximal.

Par qualité :

| Code | Libellé | Items |
|---|---|---|
| 0 | Aucune (blanc) | 9722 |
| 0 | (sans c_item) | 480 |
| 1 | Gris | 7 |
| 2 | Vert | 3672 |
| 3 | Orange | 5413 |
| 4 | Bleu | 2951 |
| 5 | Doré | 4459 |
| 6 | Violet | 31 |

Par table source :

| Tables | Items |
|---|---|
| c_item+t_item | 18749 |
| c_itemmall+t_itemmall | 6851 |
| c_itemmall+t_itemmall+c_partner/t_partner | 397 |
| t_itemmall | 364 |
| c_itemmall | 212 |
| t_item | 110 |
| c_item+t_item+c_partner/t_partner | 42 |
| t_itemmall+c_partner/t_partner | 6 |
| c_item | 3 |
| t_item+c_itemmall+t_itemmall | 1 |

Par emplacement d'équipement :

| Code | Libellé | Items |
|---|---|---|
| 1 | Tête | 1202 |
| 2 | Corps | 2127 |
| 3 | Ceinture | 501 |
| 4 | Mains | 701 |
| 5 | Jambes | 726 |
| 6 | Retour | 646 |
| 7 | 1 main | 432 |
| 8 | Main Secondaire | 209 |
| 9 | 2 mains | 1314 |
| 10 | Accessoire | 265 |
| 11 | Anneau | 90 |
| 12 | Trophée Unique | 600 |
| 13 | Emplacement de Certificat | 156 |
| 18 | Sac à dos | 17 |
| 19 | Arme Unique à 1 main | 584 |
| 20 | Anneau Unique | 290 |
| 21 | Emplacement 21 (non documenté) | 3 |
| 22 | Esprit gardien | 71 |
| 23 | Bâtiment | 5 |
| 24 | Emplacement 24 (non documenté) | 6 |
| 25 | Emplacement 25 (non documenté) | 4 |
| 26 | Emplacement 26 (non documenté) | 4 |
| 27 | Emplacement 27 (non documenté) | 4 |
| 28 | Emplacement 28 (non documenté) | 4 |
| 29 | Emplacement 29 (non documenté) | 4 |
| 30 | Bâtiment | 279 |
| 31 | Emplacement 31 (non documenté) | 11 |
| 32 | Outil de récolte | 139 |
| 33 | Expertise de classe | 19 |
| 34 | Équipement de familier | 78 |
| 35 | 2 mains (projectile) | 260 |

Autres chiffres : 3 674 items portent une restriction de classe (1 922 armures, 1 642 armes, 110 boucliers, aucun autre type) ; 13 593 sont non échangeables, 6 907 se lient à l'équipement.

## Écarts connus

Anomalies relevées :

| Anomalie | Items |
|---|---|
| sans_description | 5459 |
| nom_test | 983 |
| nom_absent | 745 |
| sans_c_item | 480 |
| enchant_usage_sans_texte | 236 |

Détail :

- **Ids sans doublon** : aucune des quatre tables n'a d'id en double.
- **`c_item` sans texte** : 3 ids (29515, 29516, 29517, des « essences » à nom Big5) n'ont aucune ligne `t_item`/`t_itemmall`. **`c_itemmall` sans texte** : 212 ids (premier : 44138). Ils reçoivent le nom `(sans nom)` ; leur nom Big5 est dans `nom_big5_c_item`.
- **Texte sans `c_*`** : 110 ids de `t_item` (564, 565, 566...) et 370 ids de `t_itemmall` (44047, 44048, 44077...) n'ont aucune ligne `c_item`/`c_itemmall`. Ils ont nom et description mais ni type, ni niveau, ni qualité (`type_libelle` = « Type inconnu (sans c_item) »). Le type n'est donc pas connu pour 480 items.
- **Noms absents** : 745 items sans nom FR : 215 sans ligne texte (ci-dessus) et 530 dont la ligne `t_*` existe mais dont la colonne nom est vide (452 dans `c_item`/`t_item` : objets de service, gemmes et formules jamais renseignés en FR). L'un d'eux seulement a une description.
- **Descriptions absentes** : 5 459 items (dont 1 711 armures et 1 249 armes : l'équipement n'a pas de texte d'ambiance). 2 137 d'entre eux ont néanmoins un `effet_usage` qui sert de description. Leur absence est dans les tables : la colonne description de `t_item`/`t_itemmall` est vide.
- **Noms de test et reliquats non FR** : 983 items ont un nom du type « Testing ID », « Placeholder » ou commençant par « Test » (tag `nom_test`) ; quelques noms mall restent en anglais dans la table FR (« Singaporea/Malaysian Ver. ID », « Elegant Ballroom Hat Capsule »). Ils sont conservés tels quels, sans correction inventée.
- **Types non documentés** : le libellé du kind 58 (611 items, noms du type « Parchemin entier : ... »), 30 (« faucille-fusil »), 59, 75 à 77, 79 à 81, 83, 84 est dérivé des noms, faute d'entrée `t_textindex` ; le kind 0 ne concerne qu'un item. Les emplacements 21, 24 à 29, 31 n'ont pas de libellé.
- **Enchants d'usage sans texte** : 236 items ont un `UseEnchantId` absent de `t_enchant` et `t_itemmallenchant`.
- **Libellé « Retour »** : l'emplacement 6 et le kind 16 s'affichent « Retour » dans `t_textindex` FR (1052 et 1161) ; c'est le texte du jeu (traduction de « Back »), conservé.

Items référencés par une autre table mais absents des tables d'items (comptés sur les ids distincts de chaque table) :

| Référence | Ids distincts | Absents des tables d'items | Exemples d'ids absents |
|---|---|---|---|
| boutique | 4714 | 0 |  |
| butin | 5455 | 0 |  |
| collecte_catalyseur | 478 | 0 |  |
| collecte_produit | 257 | 60 | 56711,56712,56713,56714,56715,56821,56822,56823,56824,56825,56893,56894 |
| partenaire | 446 | 1 | 29629 |
| quete | 1638 | 0 |  |
| recette_materiau | 4928 | 0 |  |
| recette_produit | 2863 | 0 |  |

Les boutiques, le butin, les quêtes et les recettes n'ont aucune référence orpheline. Les 60 produits de collecte absents (56711 et suivants) et l'id de familier 29629 sont référencés mais n'ont aucune ligne dans `c_item`/`t_item`/`c_itemmall`/`t_itemmall` (liste exacte : relancer le programme sans 3e argument pour les exemples ; les 60 sont dans `c_familycollect`). 12 370 items ne sont référencés par aucune de ces tables (objets de test, de mall ou d'événement, équipements donnés par script).

## Limites

- Le dictionnaire reflète uniquement ce que contiennent ces tables ; il ne prétend pas couvrir des items créés par script serveur, par coffre (`c_itemchest`) ou tirés d'autres tables non croisées.
- `classes_*` repose sur une déduction (col 77), à confirmer par désassemblage avant usage en jeu.
- Les libellés « dérivés » ne sont pas des textes du jeu.
- Le test référence le constructeur par un `<Compile Include>` ajouté à `tests/Eden.Core.Tests/Eden.Core.Tests.csproj` (même schéma que les fichiers `edenpack`).

## 30 lignes d'exemple

Colonnes : id, nom, type (kind), niveau requis, qualité, emplacement, classes, pile, tables source, description (tronquée).

| id | nom | type (kind) | niv. | qualité | emplacement | classes | pile | tables | description |
|---|---|---|---|---|---|---|---|---|---|
| 20742 | Tunique du Saint Guerrier | Armure Lourde (4) | 63 | Bleu | Corps | Guerrier, Chevalier, Templier, Muraille… | — | c_item+t_item | « On dit que cette tunique était portée par un saint le jour de sa mort. Résultat : l'arm… |
| 21634 | Ceinture de l'Étoile de Feu | Armure Légère (8) | 60 | Orange | Ceinture | Barde, Sage, Chevalier du Chaos, Discip… | 1 | c_item+t_item | L'effet spécial ne peut être activé que dans les Arènes 3 et 10 joueurs. ¶ Vous pouvez aj… |
| 27212 | Robe du rituel Mashfu | Robe de Protection (6) | 81 | Bleu | Corps | Clerc, Shaman, Disciplinaire, Druide, S… | — | c_item+t_item | Robe portée par le prêtre délivrant le nectar ¶ pendant le Festival de la Forêt chaque an… |
| 30344 | Serment de Gel | Épée (19) | 60 | Doré | Arme Unique à 1 main | Guerrier, Chevalier, Danselame, Chevali… | — | c_item+t_item | Serment de Givre |
| 31921 | Arc Traçant | Arc (28) | 30 | Vert | 2 mains (projectile) | Chasseur, Corsaire, Maître des Bêtes, C… | — | c_item+t_item | Traqueur |
| 33665 | Bâton de Rubis | Bâton (32) | 4 | Vert | 2 mains | Magicien, Shaman, Démoniste, Archélémen… | — | c_item+t_item | "Un rubis brillant est incrusté au bout de ce bâton. On peut sentir sa chaleur. Idéal pou… |
| 32630 | Défenseur Déchu | Bouclier (34) | 60 | Doré | Main Secondaire | Guerrier, Assassin, Danselame, Barbare,… | — | c_item+t_item | Défenseur Déchu |
| 28874 | Collier d'Écailles de Bephamoth | Collier (37) | 85 | Bleu | Accessoire | — | 1 | c_item+t_item | Collier réalisé avec les écailles de Bephamoth, abritant le pouvoir d'une Bête divine. |
| 33563 | Anneau de Juge | Anneau (38) | 58 | Bleu | Anneau Unique | — | 1 | c_item+t_item | "Cet anneau carré contient une gemme précieuse portant des écritures en une très vieille … |
| 33998 | Cœur Champi. Séché d'Allspore | Trophée (39) | 8 | Doré | Trophée Unique | — | 1 | c_item+t_item | Sécher le cœur de Justin Allspore l'empêchera de ressusciter. Il est ridé et mou. |
| 28052 | Assaut de la Mort II | Certificat (40) | — | Aucune (blanc) | Emplacement de Certificat | — | 1 | c_item+t_item | Après avoir enfilé l'Armure en Fer, je ne me soucie plus que de l'attaque. Cela va grande… |
| 464 | Parchemin du Roi Immortel Éveillé, Partie I | Objet (44) | 80 | Doré | — | — | 1 | c_item+t_item | Ce parchemin recouvert de poussière est très ancien. ¶ Le pouvoir qu'il dégage est immens… |
| 504 | Viande de Crabe de Glace | Objet (44) | — | Aucune (blanc) | — | — | 200 | c_item+t_item | Fournitures ¶ Zone : Hauteurs Feudegivre ¶ PNJ lié : ¶ Mick - Maire de Flammeville ¶ Fend… |
| 36926 | Gemme de Malédiction Rouge Sang Nv. 3 | Gemme (45) | 50 | Orange | — | — | 999 | c_item+t_item | Utilisation : Pour sertir cette gemme, faites un clic-droit dessus et sélectionnez un obj… |
| 40014 | Parchemin de Puissance Nv. 50 | Parchemin (46) | — | Aucune (blanc) | — | — | 250 | c_itemmall+t_itemmall | Ce parchemin permet d'ajouter des ¶ bonus d'ATT (M) à vos armes préférées ! ¶ Utilisation… |
| 38294 | Jugement du Templier | Panoplie (47) | — | Aucune (blanc) | — | — | 1 | c_item+t_item | DÉG de Coup Infernal +8 % ¶ À noter : quand deux Certificats sont activés, un seul est va… |
| 36963 | Polymériseur de Pièces d'Ezur | Sac de Récompenses (48) | 50 | Orange | — | — | 1 | c_item+t_item | Attention. Ces pièces ne sont pas encore utilisées dans le jeu. Il est donc conseillé de … |
| 36972 | Formule de Modification d'ADN Alpaga | Matériel de Fusion (49) | 60 | Vert | — | — | 1 | c_item+t_item | Matériaux requis : ¶ Alpaga x 1 ¶ Permet de produire ¶ &37019& ¶ « Les alpagas génétiquem… |
| 29187 | Formule : Guitare | Matériel de Fonderie (54) | 80 | Aucune (blanc) | — | — | 1 | c_item+t_item | Travail de niveau 1 ¶ Matériaux requis : ¶ Branche Sèche x1 ¶ Fourrure d'Animal Sacré x1 … |
| 28933 | Enchantement « Main » - Âme Sylvestre Niv. 7 | Formule d'Enchantement (56) | 88 | Orange | Mains | — | 999 | c_item+t_item | Lorsque vous lancez un enchantement, maintenez le bouton droit de la souris et sélectionn… |
| 23721 | Tibiscuit Malin | Familier (60) | — | Vert | — | — | 1 | c_item+t_item+c_partner/t_partner | Attributs d'Attaque : Magie Sacrée ¶ Bonus : Taux de CRIT Mag +5% ¶ « Tibiscuit Malin. Ce… |
| 40882 | Combinaison de Pilote Planétaire (Supérieure) | Costume (68) | — | Vert | Corps | — | 1 | c_itemmall+t_itemmall | "L'uniforme des pilotes d'élite. Il ne peut être porté que par une infime partie de la po… |
| 40929 | Costume Ninja de Talent (Supérieur) | Costume (68) | — | Vert | Corps | — | 1 | c_itemmall+t_itemmall | "C'est qui les meilleurs, entre pirates et ninjas ? Les ninjas bien sûr !" ¶ Attention : … |
| 41135 | Petite Statue de Déesse | Petit Meuble (69) | — | Bleu | — | — | 1 | c_itemmall+t_itemmall | "Il s'agit d'une déesse très anciennes, révérée par les esprits de la forêt. Elle est trè… |
| 22450 | Glyphe Aura : Bouclier Protecteur | Glyphe de Classe (78) | — | Orange | — | — | 1 | c_item+t_item | Pour sertir cet objet, faites un clic-droit dessus et sélectionnez l'emplacement de glyph… |
| 21192 | Expertise de Classe Nv. 14 | Expertise de classe (80) | — | Doré | Expertise de classe | — | 1 | c_item+t_item |  |
| 21816 | <Familier> Collier de la Colline Tranquille | Équipement de familier (81) | 30 | Vert | Équipement de familier | — | 1 | c_item+t_item | Ce collier peut augmenter la PRÉC de votre familier. |
| 26264 | Roi Topalov | Esprit Gardien (Temps Limité) (82) | — | Bleu | Esprit gardien | — | 1 | c_item+t_item | Échangez des Cristaux Purifiés contre cet Esprit Gardien. Une fois invoqué, cet Esprit Ga… |
| 40031 | Sac à 10 Emplacements | Sac à dos (43) | — | Aucune (blanc) | Sac à dos | — | 1 | c_itemmall+t_itemmall | De la taille d'un poing, ce sac est pourtant très grand ! ¶ Sac à Dos ou Banque : +10 emp… |
| 20038 | Invitation au Banquet : Danse Guerrière | Invitation de banquet (77) | — | Bleu | — | — | 100 | c_item+t_item | Luxe Requis : 190 ¶ Confort Requis : 350 ¶ Effet Temporaire : ¶ Les Personnages reçoivent… |

