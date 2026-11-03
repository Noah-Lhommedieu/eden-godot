# Analyse `re/` — flux de chargement natif (Bloc J)

Sources : `client_game` (strippé, `FUN_*`) — adresses citées.

## Ce que fait le client natif

1. **Chargement DB monolithique.** `FUN_00cf53b0` (« Loading DB -
   Begin/End », variante « ReLoading DB ») parcourt **tous** les
   `.\\data\\db\\*.ini` en une passe synchrone : `C_Level`, `C_Class`,
   `C_Monster`, `C_Ai`, `C_Item`, `C_ItemMall`, `C_DropItem`,
   `C_Title`, `C_ItemCombo`, `C_Enchant`, `C_ItemMallEnchant` + ~60
   helpers (`FUN_00ce…`/`00d…`). Les tables statiques sont donc
   toutes en mémoire avant le monde 3D.
2. **CZone séquentiel.** `FUN_00b07450` (`HighLand.cpp`, logs
   « Log - Load CZone: %s » / `Free CZone begin`) charge la zone
   (fsm/objets/entités) en une fonction unique ; `FUN_00698780`
   libère (`Free CZone`) puis `FUN_00687e10` logge la sortie.
3. **Écran de chargement Win32 pré-D3D.** `FUN_00684310` blitte
   `Loading_%hd.dds` (par id de carte) + `loadingframe00.dds` sur
   le HDC de la fenêtre — l'écran natif vit *avant* le device D3D.
4. **Options ini de stratégie de chargement** (écrites par
   `FUN_0069e290`, section `[Option]`) : `BelowNormalLoading`
   (thread de chargement en priorité basse), `UseMappedFile`
   (fichiers mappés en mémoire pour les IO), `PrecacheKFM`
   (pré-chargement des animations Gamebryo KFM), `UITextureCache`,
   `Instancing`, `OpenMP`, `SceneObjectInstance`, `CharacterTexture`
   / `SceneTexture` (qualité = streaming partiel).

## Notre flux (Godot) — comparaison

| Natif | Eden Limitless |
|---|---|
| DB sync au chargement | Tables `.ini` lazy (`ItemTable.Load` etc. au premier usage) |
| CZone séquentiel | `MapLoader.LoadMap` en `Task.Run`, objects/env/entities parallélisés (S001 ≈ 7,4 s vs 12,9 s séquentiel) |
| `PrecacheKFM` | `AssetCatalog.PreloadGlbProtos` par lots pendant `LoadingScreen` |
| Barre `loadingframe00/01` + alpaca | reproduite (Bloc F) avec progression réelle |
| Textures streamées selon qualité | GLB+textures chargés en entier → contribue à la RAM (Bloc B) |

## Verdict

Le modèle de fond (chargement monde en tâche de fond + barre réelle +
precache protos) est **déjà conforme** à l'architecture native. Écarts
restants, par ordre d'impact :

- **RAM** : nos GLB textures sont toutes résidentes ; le natif streame
  selon `CharacterTexture`/`SceneTexture`. Levier pour Bloc B :
  importer les textures terrain/objets en basse résolution optionnelle.
- **DB lazy** : première ouverture d'une fenêtre peut parser sa table
  (`C_Item.ini` ~ Mo) → micro-hoquet. Non bloquant ; précharger les
  tables chaudes pendant le loading serait un vrai plus.
- **`UseMappedFile`** : pas d'équivalent nécessaire (Godot gère déjà
  ses `.pak`/imports).

## « Autres choses mal refaites » (items Bloc J)

- Saut / interpolation mobs / échelle joueur→monde → couverts par le
  Bloc C (mobs interp proven live, vitesse/échelle corrigées, dev ×5).
- Skins corps par race/genre → Bloc G (caméra par race, masques FACES
  B/D/F teintés — preuves captures).
