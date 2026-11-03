# Inventaire des assets — client de référence

Corpus analysé : `/home/ster2lune/Bureau/reverseden/eden_server/client` (47739 fichiers).

Fichiers avec description : **47739** / 47739 (reste en cours : 0).

| Document | Contenu | Fichiers |
|---|---|---|
| [00-racine.md](00-racine.md) | Fichiers à la racine du client | 96 |
| [UI-itemicon.md](UI-itemicon.md) | UI/itemicon/ — icônes d'objets et d'équipement | 6117 |
| [UI-loadingframe.md](UI-loadingframe.md) | UI/loadingframe/ — écrans de chargement | 400 |
| [UI-map.md](UI-map.md) | UI/map/ — mini-cartes et fonds de carte | 305 |
| [UI-skillicon.md](UI-skillicon.md) | UI/skillicon/ — icônes de compétences, classes, monstres | 4754 |
| [UI-uiicon.md](UI-uiicon.md) | UI/uiicon/ — icônes UI génériques et armes (W*) | 4563 |
| [UI.md](UI.md) | UI/ — images racine (logos, curseurs, tooltips) | 6 |
| [UIData.md](UIData.md) | UIData/ — définitions CEGUI (layouts, imagesets, fonts, lua) | 582 |
| [audio.md](audio.md) | bgm/ + sound/ — musiques et effets sonores | 2456 |
| [char.md](char.md) | char/ — modèles, textures, animations des personnages | 5270 |
| [data-db.md](data-db.md) | data/db/ — tables de données client (.ini Big5) | 163 |
| [data-scene.md](data-scene.md) | data/scene/ — définitions des cartes/zones | 637 |
| [decorate.md](decorate.md) | decorate/ — décorations et props d'environnement | 516 |
| [effect.md](effect.md) | effect/ — effets visuels (textures et modèles) | 3538 |
| [house.md](house.md) | house/ — maisons et meubles joueur | 79 |
| [item.md](item.md) | item/ — modèles et textures d'objets/armes monde | 2502 |
| [map-divers.md](map-divers.md) | map/ — navigation, objets et autres | 391 |
| [map-model-dark.md](map-model-dark.md) | map/model/*/Dark/ — lightmaps précalculées | 5700 |
| [map-model.md](map-model.md) | map/model/ — modèles, textures et données de scènes 3D | 1689 |
| [map-texture.md](map-texture.md) | map/texture/ — textures de terrain et matériaux | 3557 |
| [media-runtime.md](media-runtime.md) | Autres dossiers (Movies, GLCache, Temp, crash, pkg, mods…) | 74 |
| [monster.md](monster.md) | monster/ — modèles et textures des monstres | 3444 |
| [ride.md](ride.md) | ride/ — montures et véhicules | 875 |
| [themes.md](themes.md) | themes/ — atlas d'interface par couleur | 25 |
| [ref-shots-catalog.md](ref-shots-catalog.md) | `ref-shots/` — captures in-game du client original (comportements UI) | 103 |

## Méthode

- Images (~35k) : planches de contact 10×10 lues par agents, une ligne par fichier.
- Textes (.ini/.layout/.imageset/.lua/.xml) : décodage Big5→UTF-8 puis analyse directe.
- Binaires (.nif/.kf/.wav/.csm/.smp…) : description déduite du nom + rôle du dossier.
- `map/model/*/Dark/` : lightmaps (silhouettes grises) — vérifiées par échantillon, décrites en lot.
