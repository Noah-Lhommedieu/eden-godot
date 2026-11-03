# Interface — CEGUI (`uidata/`) + textures UI (`ui/`, `UI/`)

## `uidata/` — l'UI en fichiers CEGUI

| Type | Fichiers | Rôle |
|---|---|---|
| `.layout` | 415 | fenêtres/panneaux (XML) — **19 FR mal formés** (G4) |
| `.imageset` | 34 | atlas : nom → PNG + régions nommées |
| `.font` / `.font.kr` | 5 / 6 | polices logiques CEGUI (XML) |
| `.scheme` / `.looknfeel` | 1 / 1 | skin CEGUI |
| `.ufo` | 1 | conteneur OLE2 (données fontes) |
| `.lua` | 2 | scripts UI |

## Imagesets → atlas

| Imageset | Nom | Atlas | Régions |
|---|---|---|---|
| `bigworld.imageset` | BigWorld | `mapworld00.png` | 1 |
| `bigworldset.imageset` | BigWorldSet | `worldmap1.png` | 5 |
| `cdani.imageset` | CDAni | `CoolDownSets.png` | 172 |
| `cndejavu.imageset` | CNDejaVu | `CNDejaVu.png` | 62 |
| `cndejavu2.imageset` | CNDejaVu2 | `CNDejaVu2.png` | 21 |
| `cnguiderb.imageset` | CNGuideRB | `CNGuideRB.png` | 1 |
| `competitionschedule.imageset` | CompetitionSchedule | `CompetitionSchedule.png` | 1 |
| `cutscene.imageset` | cutScene | `cutScenePlay.png` | 13 |
| `dejavu.imageset` | DejaVu | `DejaVu.png` | 437 |
| `dejavuimage.imageset` | DejaVuImage | `DejaVuImage.png` | 133 |
| `injured.imageset` | Injured | `Injured.png` | 1 |
| `jobglyph.imageset` | JobGlyph | `JobGlyph.png` | 1 |
| `krconsideration.imageset` | KRConsideration | `KRConsideration.png` | 1 |
| `krpaywarning.imageset` | KRPayWarning | `KRPayWarning.png` | 1 |
| `magiccube.imageset` | MagicCube | `MagicCube.png` | 50 |
| `main.imageset` | main | `main.png` | 115 |
| `map.imageset` | map | `mapworld01.png` | 1 |
| `map2.imageset` | WorldMap2 | `mapworld02.png` | 1 |
| `map3.imageset` | WorldMap3 | `mapworld03.png` | 1 |
| `map31.imageset` | WorldMap31 | `mapworld031.png` | 1 |
| `newhand.imageset` | newhand | `newhand.png` | 44 |
| `newknowledge.imageset` | NewKnowledge | `NewKnowledge.png` | 1 |
| `newlicence.imageset` | NewLicence | `NewLicence.png` | 1 |
| `newskilllist.imageset` | NewSkillList | `NewSkillList.png` | 1 |
| `promotion.imageset` | promotion | `promotion.png` | 11 |
| `radar.imageset` | Radar | `Radar.png` | 102 |
| `roundicon.imageset` | Roundicon | `Roundicon.png` | 1 |
| `teamscorelevel.imageset` | TeamScoreLevel | `TeamScoreLevel.png` | 8 |
| `tooltips.imageset` | ToolTips | `ToolTips.png` | 4 |
| `worldmap.imageset` | WorldMap | `mapworld01.png` | 1 |
| `worldmap11.imageset` | WorldMap11 | `mapworld011.png` | 1 |
| `worldmap12.imageset` | WorldMap12 | `mapworld012.png` | 1 |
| `worldmapcity.imageset` | worldmapcity | `worldmap.png` | 4 |
| `worldmapcity2.imageset` | worldmapcity2 | `worldmap2.png` | 4 |

## Textures UI (`ui/` + `UI/`, ~16 000 DDS)

| Sous-dossier | Rôle |
|---|---|
| `itemicon/` | icônes d'items (code `UI…` dans `c_item.ini`) |
| `skillicon/` | icônes de sorts |
| `uiicon/` | icônes d'interface |
| `map/` | cartes région `S###s.dds` 1024² + minimaps `S###m.dds` 512² |
| `loadingframe/` | écrans de chargement `loading_###.dds` |
| `UI/` racine | `title`, `angel`, `SystemCursors`… (casse jumelle) |

Thèmes custom `ui_textures/themes/` = non-EDENFR (G8).
Catalogue des captures UI : `eden_godot/docs/client-assets/
ref-shots-catalog.md`.

## Sous-catégories

| Sous-catégorie | Fichiers |
|---|---|
| itemicon | 6070 |
| skillicon | 4742 |
| uiicon | 4550 |
| layouts | 415 |
| loadingframe | 393 |
| map | 299 |
| imagesets | 117 |
| fonts | 13 |
| emblème | 9 |
| codepage | 8 |
| . | 7 |
| lua_scripts | 2 |
| animations | 1 |
| looknfeel | 1 |
| schemes | 1 |
