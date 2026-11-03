# raw_assets/ — cartographie des dossiers

`eden_godot/raw_assets/` est un **lien symbolique** vers l'installation du
client Reforged :

```
raw_assets -> /home/ster2lune/Bureau/reverseden/eden_server/reforged/EdenReforged
```

C'est le répertoire racine du client Windows original (Gamebryo / Ni),
utilisé comme **oracle d'assets en lecture seule** pour le portage Godot
(ne pas modifier — voir `eden_godot/AGENTS.md`).

Formats rencontrés :

| Extension | Rôle |
|---|---|
| `.nif` | Modèle 3D Gamebryo (NetImmerse File, v20.3.0.9) |
| `.kf` | Animation Gamebryo (keyframes) |
| `.kfm` | Descripteur d'animations (KF Manager — liste les .kf d'un modèle) |
| `.ifl` | Liste d'images (flipbook / animation de texture) |
| `.dds` | Texture DirectDraw Surface |
| `.ini` | Données/config, **encodage Big5** (chinois traditionnel) |
| `.csm` | Scène compilée (header `[CSM]`, versionné) |
| `.fsm` | Map au format texte (référencée par les .ini de scène) |
| `.pkg` | Archive d'assets compressée (zlib, magic `78 9c`) |
| `.smp`/`.fsmp`/`.pgp`/`.area` | Données de navigation/collision/zones de map |
| `.layout`/`.imageset`/`.looknfeel`/`.scheme`/`.font` | Fichiers CEGUI |
| `.cache`/`.hlsl` | Cache de shaders compilés par machine |

---

## Assets 3D par catégorie

Toutes ces familles suivent la même convention : `model/` = `.nif`,
`texture/` = `.dds`, `animation/` = `.kf` (+ `.kfm` descripteurs,
parfois à la racine du dossier).

### `char/` — personnages joueurs (~7 100 fichiers)

- `model/` — corps, visages, armures, armes des classes (`a6102a.nif`…).
- `texture/` — textures personnages, variantes de couleur (`h52`, `h56`,
  `s52`… = slots de personnalisation).
- `animation/` — `.kf` partagés par squelette : `B.kf`, `C.kf`, `F.kf`,
  `G.kf`, `K.kf`… (une lettre = un rig : body, classes…).
- `.kfm` à la racine — un par rig (`B.kfm`, `C.kfm`, `F.kfm`…).

### `monster/` — monstres et PNJ (~13 200 fichiers)

- `model/` — `am001.nif`… (un .nif par monstre).
- `animation/` — `.kf` nommés comme le modèle (`am001.kf`).
- `texture/` — skins et variantes élémentaires.

### `ride/` — montures (~5 000 fichiers)

- `model/` — `AR013.nif`, `a081.nif`…
- `animation/` — `.kf` de monture (locomotion + passager).
- `texture/` — variantes de couleur des montures.

### `house/` — maisons de joueurs / guildes (~80 fichiers)

- `model/` — bâtiments `H001.nif`… (+ quelques .dds orphelins).
- `animation/` — `.kf`/`.kfm` (portes, meubles animés).
- `texture/` — inclut des dossiers `color`/`colorENG` de teintes.

### `item/` — objets 3D posés/portés (~5 400 fichiers)

- `model/` — accessoires, armes affichées, objets de quête (`a204.nif`,
  `AB01.nif`…).
- `texture/` — textures items + versions `_item.dds` (icône-inventaire
  rendue du modèle).

### `decorate/` — décorations d'intérieur / mobilier (~500 fichiers)

- `model/` — `P001.nif`…, `floor.nif`.
- `texture/` — textures mobilier.

### `effect/` — effets spéciaux / skills (~6 100 fichiers)

- `model/` — meshes d'effets (`aks000141.nif` = skill FX).
- `texture/` — sprites de particules, trails, glows ; `.ifl` = flipbooks
  animés. Contient aussi des sources d'art (`S81114.max`, `.rar`).

---

## `map/` — monde (~18 500 fichiers)

- `model/S001` … `S1xx/` — un dossier par map : `<id>.fsm` (mesh de
  terrain texte), `<id>.area`, `<id>.ini`, `<id>.smp2`, sous-dossier
  `Dark` (lightmaps ?).
- `model/object/` — props réutilisables posés sur les maps
  (`UA001.nif`, `BLOCK.nif`, `nocamerablock02.nif`).
- `model/effect/` — effets ambient de map + `convertdds.bat`.
- `sknodemap/` — par map : `.smp` (hauteurs ?), `.fsmp`, `.pgp`
  (pathgraph/portes ?), index de navigation.
- `texture/` — tilesets terrain, lightmaps, skyboxes.

Correspondance : `data/scene/sXXX.ini` ↔ `map/model/SXXX/` ↔
`map/sknodemap/SXXX.*` ↔ `UI/map/SXXXm.dds`/`SXXXs.dds` (minimap).

---

## `data/` — tables de jeu (Big5)

### `data/db/` (~590 .ini)

La **base de données client** du jeu, un .ini par table : classes
(`Assassin.ini`, `Berserker.ini`, `C_Classes.ini`), items (`c_item.ini`,
`weapon.ini`, `C_EquipSet.ini`), monstres (`c_monster.ini`), sorts
(`C_Spell.ini`, `T_Spell.ini`, `T_Script.ini`), PNJ (`c_npc.ini`,
`t_npc.ini`), textes (`t_textindex.ini`, `_T_TextIndex.ini`), boutique
(`c_itemmall.ini`), housing, achievements, battlefield, etc.
Préfixes : `C_`/`c_` ≈ tables client, `T_`/`t_` ≈ tables partagées
serveur. Équivalent partiel des tables PostgreSQL du serveur.

### `data/scene/` (~230 paires)

Par map : `sXXX.ini` (définition : globe_data → `.fsm`, born_area,
revive_area, **gateway_area** = portails vers d'autres maps avec texte
Big5) + `sXXX.csm` (scène compilée associée).

---

## Interface (CEGUI)

### `uidata/` (~610 fichiers)

Distribution CEGUI standard :

- `layouts/` — ~440 `.layout` : toutes les fenêtres du jeu
  (`Achievement.layout`, `ChatEmojiUI`, `QuestTraceWnd`…).
- `imagesets/` — `.imageset` + `.png` : atlas d'images déclarés pour
  CEGUI.
- `looknfeel/` — `DejaVu.looknfeel` (skin des widgets).
- `schemes/` — `DejaVu.scheme` (lie looknfeel + imagesets + fonts).
- `fonts/` — `.font` CEGUI (F001-xx) + `.TTF` ; `.kr` = variantes
  coréennes.
- `codepage/` — tables de conversion `b2u`/`u2b` pour codepages 936
  (GB), 949 (coréen), 950 (Big5), 1258 (vietnamien).
- `lua_scripts/` — `main.lua`, `demo8.lua` (scripting UI).
- `animations/` — `animation.xml` (animations CEGUI).
- `StandardMaterialNodeLibraryFragments.xml` — shaders CEGUI.

### `UI/` (~21 000 fichiers, quasi tout .dds)

- `itemicon/` — icônes d'inventaire des items.
- `skillicon/` — icônes de sorts/compétences.
- `uiicon/` — icônes génériques UI (boutons, onglets…).
- `map/` — **minimaps** (`S001m.dds`, `S001s.dds`) + fonds PVP.
- `loadingframe/` — écrans de chargement (`Loading_0`..`Loading_100`,
  variantes `_dl`, `_lib`, `_st`).

### `themes/` — `Blue`, `Coffee`, `Green`, `Purple`, `Red`

5 PNG par thème (variantes couleur du skin CEGUI : `main.png`,
`Radar.png`, `DejaVu.png`…). Thème sélectionnable en jeu.

### `fonts/` — 14 `.ttf`

Polices client : `edenRoboto*.ttf`, `edenArial`, `edenDejaVu`,
`edenHuninn`, `edenSarabun` (thaï), `edenGG`.

---

## Audio

- `bgm/` — 116 `.ogg` : musiques de zones (`bgm001.ogg`,
  `aridwilds.ogg`, `aven_halloween.ogg`, `aven_xmas*`…).
- `sound/` — ~2 400 `.wav` : SFX (UI, combat, footsteps, skills ;
  `add*.wav` = sons additionnels).

---

## Données utilisateur / runtime (générés, pas des assets)

- `UserSetting/` — réglages **par personnage** (`<charId>_10.ini` =
  positions de fenêtres/mission trace ; `AI_*` = auto-item, `AS_*` =
  auto-spell).
- `Temp/` — **cache de shaders compilés** par machine+compte
  (`STER2NOBARA.ster2lune/` : `.hlsl` NiSpecificMaterial, `.cache`
  DX9) + caches de fonts `F001-*.cache`. Régénéré à chaque lancement.
- `ScreenCapture/` — **vide** : dossier des screenshots in-game.
- `emblem/` — **vide** : emblèmes de guilde téléchargés/posés par les
  joueurs.
- `eden_crash/` — rapports du **crash reporter** du mod Limitless :
  un `s_<date>_<pid>/` par session crashée avec `session.json`
  (breadcrumbs, features actives, map, personnage), logs copiés,
  marqueurs `uploaded`/`tries`.
- `mods/_testing/` — DLLs de mods en test : `Limitless.dll` (le mod
  principal QoL), `NeoInventoryBank.dll`, `ItemMall.dll`, `gameFix.dll`
  + leur config.

---

## `pkg/` — archives d'origine (~320 fichiers)

`pkg001.pkg`…`pkg321.pkg` : archives assets compressées zlib
(distribution initiale / patchs du client officiel). `pkg.idx` = index
(vide/à zéro — mode « loose files » actif), `pkg.sp` = manifeste JSON
des chemins d'installation d'origine (`D:\Jogos\Eden…`).

## `CutScene/` — 3 scripts caméra

`Login.txt`, `CreateCharacter.txt`, `SelectCharacter.txt` :
mouvements de caméra des écrans d'intro.

---

## Fichiers racine (hors dossiers)

| Fichier | Rôle |
|---|---|
| `game_.exe` (+9 `.bak_*`) | Client patché Limitless ; les .bak = variantes de patchs (strafe, castmove, camcast, mountstrafe…) |
| `d3d9.dll` (+`.bak_forcedoff`) | Proxy/hook DirectX 9 du mod |
| `ALAudio.dll`, `alaudio.ini`, `alaudio.log` | Lib audio + config loader (`Mode=2`, `Logging=1`) |
| `ACDC.dat`, `cltgp.dat`, `clal.xmf`, `cltax.xmf` | Data client packagée ; `clal.xmf` = manifeste audio (check anti-symlink) |
| `client.ini` | Options graphiques (résolution, FXAA, view range…) en Big5 |
| `connect.ini` | Serveur cible (`127.0.0.1,6543`) |
| `locate.ini` | Locale + URLs (`Locate=US`, edenreforged.com) |
| `locale.info` | Blob binaire de locale |
| `version.txt` | `7.4.1` |
| `User.ini` | Layout sauvegardé des fenêtres CEGUI (encodé) |
| `eden_patch.ini` | Config du mod Limitless : `[Features]` = ~40 toggles QoL, tailles inventaire/banque |
| `cert_ext_slots.bin` | État slots certificats (feature Limitless) |
| `eden-logo.ico`, `dbghelp.dll`, `ftrace.dos` | Icône, utilitaires debug/crash |
| `NoName_<user>_<date>.zip` | Archives de crash uploadées par le reporter |
| `*.log` | Logs runtime : `eden_hook*` (mod), `main`, `error`, `CEGUI`, `UI`, `alaudio`, `eden_d3d_*`, `limitless_mod`, `GameTemp` |

---

*Document généré par analyse des fichiers le 2026-10-28.*
