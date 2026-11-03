# raw_assets/UI + uidata — inventaire fichier par fichier

Inventaire **visuel** (images ouvertes et décrites) de tous les fichiers
des dossiers `UI/` et `uidata/` contenant **moins de 100 fichiers**
(hors sous-dossiers). Complète `RAW-ASSETS.md`.

Dossiers exclus car >100 fichiers : `UI/itemicon/` (8 897),
`UI/skillicon/` (5 732), `UI/uiicon/` (5 558), `UI/loadingframe/` (417),
`UI/map/` (319), `uidata/layouts/` (440 — sauf `layouts/limitless/`).

---

## `UI/` — racine (14 fichiers)

Logos, écrans de titre et curseurs — le « branding » du client.

| Fichier | Contenu visuel |
|---|---|
| `title.dds` | Logo **Eden Reforged** : marteau stylé dans un hexagone doré sur fond or/vert, texte fantasy |
| `title.png` | Logo **« THE BLIGHT SEASON »** — champignon violet sombre avec racines tentaculaires (titre de l'évent/saison en cours) |
| `title1.dds` | Artwork anniversaire : gâteau à étages, bougies, texte **« ONE YEAR ! »** (1 an du serveur Reforged) |
| `title2.dds` | Artwork **« Midsummer Festival »** — palmier, cocktails, soleil (évent d'été) |
| `title - Copia.dds` | Copie (« Copia » = copie en espagnol/portugais) d'un titre précédent |
| `angel.dds` | Petite statue d'ange (192×128) — élément de décor UI |
| `easyfun_log.dds` | Texte légal blanc sur fond bleu nuit : *« We are not affiliated with Aeria Games or X-Legend… »* (disclaimer EasyFun/reforged) |
| `HeaderID.dds` | Sprites de **tooltip** : coins arrondis sombres + barres colorées (header d'infobulle) |
| `HeaderID-Original.dds` | Même sprite, version d'origine avant retouche |
| `HeaderID - copia.dds` | Autre copie du même sprite |
| `HeaderID1.dds` | Header de tooltip orné, bandeau **vert** (item qualité uncommon) |
| `HeaderID2.dds` | Même header, bandeau **rouge** (item rare) |
| `map001.dds` | Mini-icône 32×32 (petit symbole de map) |
| `SystemCursors.dds` | **Planche de curseurs** du jeu : main, épées croisées (attaque), bulle de dialogue (PNJ), sac (loot), loupe, touches de clavier, marteaux, flèches — chaque état de curseur in-game |

---

## `uidata/` — racine (2 fichiers)

| Fichier | Contenu |
|---|---|
| `FONT01.ttf` | **Sarasa Gothic TC** (24 Mo) — police CJK complète (chinois traditionnel). C'est la police chargée par les `.font` CEGUI |
| `StandardMaterialNodeLibraryFragments.xml` | Bibliothèque de **fragments de shaders** CEGUI (HLSL/Cg, targets DX9/PS3) : `TransformNormal`, `TransformNBT`… code source des transforms des matériaux |

## `uidata/animations/` (1 fichier)

| Fichier | Contenu |
|---|---|
| `animation.xml` | Définitions d'**animations CEGUI** : `Fadeout`/`Fadein` (alpha sur MouseLeaves/Enters), `ShakeX` (secousses ±5 px à l'affichage), `Move`/`Moveto` (translations) — keyframes + événements déclencheurs |

## `uidata/schemes/` (1 fichier)

| Fichier | Contenu |
|---|---|
| `DejaVu.scheme` | **Schéma CEGUI principal** : charge l'imageset DejaVu, les fonts F001-*, le looknfeel, et mappe ~80 types de widgets `DejaVu/*` (Button, FrameWindow, ChatFrameWnd, BuffWindow, ProgressBar, Scrollbar, TabControl…) vers les renderers Falagard |

## `uidata/looknfeel/` (1 fichier)

| Fichier | Contenu |
|---|---|
| `DejaVu.looknfeel` | **Skin Falagard** (585 Ko) : définition visuelle de chaque widget `DejaVu/*` — sections d'imagerie (états normal/hover/pressed/disabled), propriétés de couleurs, paddings, alignements |

## `uidata/lua_scripts/` (2 fichiers)

| Fichier | Contenu |
|---|---|
| `main.lua` | Handlers CEGUI du jeu : `ScrollChangeHandler` (sliders vidéo char/scene/effect → labels), `CloseFrame`, bindings de fenêtres `Root/*` |
| `demo8.lua` | Script de **démo CEGUI d'origine** (Demo8) : slider alpha, panneau scrollable, sélecteur de couleur RGB — reste du SDK, contient aussi `CloseFrame` |

## `uidata/codepage/` (8 fichiers)

Tables de conversion binaires Big5/Unicode (128–256 Ko, lookup tables) :

| Fichier | Codepage |
|---|---|
| `b2u.936` / `u2b.936` | Chinois simplifié (GB) ↔ Unicode |
| `b2u.949` / `u2b.949` | Coréen ↔ Unicode |
| `b2u.950` / `u2b.950` | Chinois traditionnel (Big5 — locale du jeu) ↔ Unicode |
| `b2u.1258` / `u2b.1258` | Vietnamien ↔ Unicode |

## `uidata/fonts/` (14 fichiers)

| Fichier | Contenu |
|---|---|
| `F001-9.font` → `F001-24.font` | Descripteurs XML CEGUI : `FONT01.TTF` FreeType aux tailles 9/10/12/24, `NativeRes 800×600`, `AutoScaled=false` |
| `F001-scale.font` | Même chose avec `AutoScaled=true` (mise à l'échelle auto) |
| `*.font.kr` (×6) | **Doublons strictement identiques** aux `.font` (variantes coréennes non modifiées) |
| `FONT01.ttf` (23 Mo) | **Arial Unicode MS** — police Unicode complète d'origine |
| `FONT01 - Copia.TTF` (4,6 Mo) | **Huninn** — police chinoise arrondie (style mignon) |
| `FONT01 - Copia (2).TTF` (82 Ko) | **Sarabun SemiBold** — police thaïlandaise |

## `uidata/layouts/limitless/` (2 fichiers)

Layouts ajoutés par le **mod Limitless** (pas d'origine) :

| Fichier | Contenu |
|---|---|
| `LimitlessHud.layout` | **HUD « Ultimate EXP »** (NCID 2032) : `BuffWindow` sombre sans titlebar au centre-haut de l'écran, texte « ULTIMATE Lv - » + `ProgressBar` dans un wrapper casting-bar — drag géré par le mod |
| `LimitlessPass.layout` | Fenêtre **« Limitless Pass »** (Ctrl+L) : FrameWindow déplaçable, info niveau, barre de progression, liste des tiers de pass (DONE/NEXT) + texte explicatif EN du système d'Ultimate EXP |

---

## `uidata/imagesets/` — racine (85 fichiers)

Les `.imageset` sont des XML qui découpent les `.png` en sprites nommés
(ex. `RepairAllBtn.imageset` → `FixAllNormal/Press/Hover/Disable`).
Description ci-dessous = **contenu visuel réel** du PNG.

### Fonds « EdenReforged » (features Limitless)

| Fichier | Contenu visuel |
|---|---|
| `AutoSellBg.png` | Motif **parchemin EdenReforged** (logo, coffres, pièces, sacs d'or, balance, étiquettes de prix, cristaux) — fond de la fenêtre auto-sell ; découpé en `manager`/`review`/`shop`/`panel`/`rule` |
| `AutoSellUI.png` | Même motif parchemin trésor — cadres/widgets de la fenêtre auto-sell |
| `BulkUse.png` | Fenêtre parchemin avec slot « + » (utilisation d'items en masse) + 3 petites icônes d'état |
| `ChatEmojiUI.png` | Parchemin décoré d'emojis (cœurs, notes de musique, bulles, ailes, potions) — fond du panneau emoji |
| `EdenAltarBatch.png` | Parchemin + **slots d'items dorés arrondis** — invocation d'autel en masse |

### Atlas UI principaux

| Fichier | Contenu visuel |
|---|---|
| `main.png` | **Atlas du HUD principal** : cadres chat/fenêtres, barres HP/BP/MP, 5 grandes orbes circulaires colorées (icônes classes : key vert, épée violet, tête-or, mâchoire orange, œil rouge), grille de petites icônes d'état, boutons menu |
| `DejaVu.png` | **Atlas de widgets génériques** : scrollbars, flèches (noires/orange), checkboxes, boutons carrés noirs, icônes système (chat, X, ?, !), sélecteurs de couleur, grandes flèches rouges, sliders + un tampon « 韓國 » (coréen) |
| `DejaVuImage.png` | Atlas **achievements/notifications** : icônes grises (trophée, power, boucliers, épée, patte dragon, caméra, médaille), icônes bleues (marteaux, montagnes, meubles), bandeaux orange « NEW TITLE / NEW ACHIEVEMENT / NEW JOB / LEVEL UP / JOB SPELL / NEW AVATAR / LICENCE / STORY », badge « NEW », barres de progression |
| `Radar.png` | **Atlas radar/minimap** : cadre de fenêtre orné + grille de boutons colorés (boîte mail, smiley, inventaire, étoile, parchemins, cœurs, poignée de main, cubes, drapeaux…) |
| `CNDejaVu.png` | Atlas UI **version chinoise** : coins de dialogues PNJ en filigrane argenté, icônes (téléchargement, lampes, drapeaux, épées, livres, trèfles, châteaux, engrenages, chats) |
| `CNDejaVu2.png` | Sprites navigation chinois : chevrons «‹ ›» bleus, boutons-scroll «活动» (activités), barres de scroll, curseurs-flèches |

### Cartes du monde (worldmap)

| Fichier | Contenu visuel |
|---|---|
| `mapworld00.png` | **Carte du monde entière** — continents style aquarelle, lignes de grille, rose des vents |
| `mapworld01.png` | Continent principal avec **nœuds de routes** (points de voyage téléport + chemins pointillés) |
| `mapworld011.png` | Quasi-identique à `mapworld01` (copie/variante) |
| `mapworld012.png` | Même continent + nœud de route supplémentaire en bas-droite (icône plume) |
| `mapworld02.png` | Second continent, quelques nœuds de route |
| `mapworld03.png` | Îles reculées, **sans** nœuds |
| `mapworld031.png` | Mêmes îles **avec** nœuds de route |
| `worldmap1.png` | Les **5 continents en sprites séparés** (fond transparent) — écran de sélection worldmap |
| `worldmap.png` | 4 gemmes/gouttes (bleues + dorées) — marqueurs de carte |
| `worldmap2.png` | 4 gemmes (rouge, rose, grise, blanche) — autres marqueurs |
| `map.imageset`, `map2/3/31.imageset`, `WorldMap*.imageset`, `worldmapCity*.imageset`, `bigworld.imageset`, `Bigworldset.imageset` | XML pointant ces PNG (`worldmapcity` découpe les gemmes en `CityNormal/CityHover`) |

### Features Limitless (QoL)

| Fichier | Contenu visuel |
|---|---|
| `RepairAllBtn.png` | Icône **marteau** ×4 états : bleu normal, pressé, survol, gris désactivé |
| `HideEffect.png` | 2 petits **yeux** (afficher/masquer) — toggle du filtre d'effets |
| `Emoji.png` (8,7 Mo) | **Immense planche d'emojis de chat** : centaines de sprites meme-style (chats, grenouilles, visages, HAHAXD/LOL/FEELS/TRENDING/KPOP/TAPATALK, nourriture, cœurs, drapeaux, animaux) — lignes = frames d'animation (feature `chat_emoji`) |
| `EdenQualityTier.png` | Rangées de **cadres carrés colorés** (teal/jaune/rouge/violet/arc-en-ciel), chevrons losange, barres dégradées — bordures de qualité d'item |
| `JobGlyph.png` | Panneau avec **cercle magique lumineux + runes** et 16 slots carrés sombres — UI du système de glyphes |
| `MagicCube.png` | Atlas du mini-jeu **Magic Cube** : rangées d'icônes (S, clé, vagues, ?), 4 moutons/alpagas, tuiles roses, feux d'artifice, stickers tête d'ours + tigre |
| `TeamScoreLevel.png` | Badges de **rank DoD (Dream of Destruction)** : S+ A+ B+ C+ et S A B C (lettrage cartoon rouge/gris) |
| `CoolDownSets.png` | Sprites arène/cooldown : flèches, logo « VS », couronne, gemmes, checkboxes, mini-icônes de classes |

### Panneaux d'info / guides

| Fichier | Contenu visuel |
|---|---|
| `NewKnowledge.png` | Cadre-carte avec photo de la **statue d'ange** — écran « connaissances » |
| `NewLicence.png` | Cadre-carte avec **cristal bleu** — écran licences |
| `NewSkillList.png` | Cadre-carte avec **château + cercle magique** — liste des skills |
| `newhand.png` | Icônes du **tutoriel débutant** : flèches dorées de guidage, boussole, pin, échelle |
| `CNGuideRB.png` | **Mascotte guide** : alpaga/mouton kawaii avec trèfle dans une bulle-pensée nuageuse |
| `CompetitionSchedule.png` | **Arbre de tournoi** PvP (bracket avec dragons en fond) |
| `KRConsideration.png` | Écran de **classification coréenne** : « 12세이용가 » (12+), mentions violence/jeu de hasard, logo fantasy |
| `KRPayWarning.png` | Écran **WARNING paiement coréen** avec artwork d'une héroïne aux cheveux violets et chapeau orange |
| `promotion.png` | 4 **coins jaunes** de surbrillance (sélection/promotion) |
| `Roundicon.png` | Bouton rond jaune glossy générique |
| `ToolTips.png` | Cadre de tooltip sombre à bord violet + 3 icônes (pièce grise, pièce d'or, gemme) |
| `Injured.png` | **Vignette rouge sang** sur tout le pourtour de l'écran — overlay « blessé/HP bas » |
| `cutScenePlay.png` | Contrôles **lecteur de cutscene** : boutons play/stop/pause en états normal/vert/survol |
| `CDAni.imageset` | XML de l'animation cooldown (pas de PNG associé visible — référence CoolDownSets) |
| `Injured.imageset`, `JobGlyph.imageset`, `promotion.imageset`, … | XML de découpage des PNG correspondants |

### Divers

| Fichier | Contenu |
|---|---|
| `DejaVu.ufo` | Fichier **OLE Composite** (358 Ko) contenant du PNG embarqué — projet d'éditeur d'imageset laissé dans le dossier |
| `Thumbs.db` | Cache de miniatures Windows (fichier parasite) |

### `.imageset` sans PNG listé séparément

`AutoSellBg`, `AutoSellUI`, `bigworld`, `Bigworldset`, `CDAni`,
`ChatEmojiUI`, `CNDejaVu`, `CNDejaVu2`, `CNGuideRB`,
`CompetitionSchedule`, `cutScene`, `DejaVu`, `DejaVuImage`,
`EdenAltarBatch`, `EdenQualityTier`, `Emoji`, `Injured`, `JobGlyph`,
`KRConsideration`, `KRPayWarning`, `MagicCube`, `main`, `map*`,
`newhand`, `NewKnowledge`, `NewLicence`, `NewSkillList`, `promotion`,
`Radar`, `RepairAllBtn`, `Roundicon`, `TeamScoreLevel`, `ToolTips`,
`WorldMap*`, `worldmapCity*` — chacun déclare des régions nommées dans
son PNG pour que les `.layout` les référencent par `set:Name image:xxx`.

---

## `uidata/imagesets/<Thème>/` — 10 dossiers couleur

`Blue`, `Green`, `Grapes`, `Lemon`, `Orange`, `purple`, `Red`,
`Strawberry`, `Water`, `Yellow` — les **variantes couleur du skin**
sélectionnables en jeu.

Chaque dossier contient les **mêmes fichiers recolorés** (vérifié
visuellement sur `main.png` Red vs Lemon : l'atlas passe du teal au
rouge/vert-khaki, orbes et icônes ré-teintées) :

| Fichier | Rôle |
|---|---|
| `main.png` | Atlas HUD recoloré à la teinte du thème |
| `DejaVu.png` | Atlas widgets recoloré |
| `DejaVuImage.png` | Atlas achievements recoloré |
| `Radar.png` | Atlas radar recoloré |
| `CNDejaVu2.png` | Sprites navigation recolorés |
| `Thumbs.db` | (Blue/Grapes/Strawberry/Yellow) cache Windows parasite |

---

*Document généré par lecture directe des images le 2026-10-28.*
