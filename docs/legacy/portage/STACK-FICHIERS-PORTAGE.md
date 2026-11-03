# Stack technique & inventaire des fichiers — évaluation portage vers un autre moteur

> **Date** : 2026-10-31 · **But** : document d'handoff pour évaluer un
> portage du client Godot vers un autre moteur de jeu. Décrit la stack,
> chaque famille de fichiers **originaux** vs **convertis**, et le degré
> de couplage au moteur. Lecture complémentaire des docs listées en §9.

---

## 1. Le projet en 30 secondes

Portage complet d'**Eden Eternal** (MMORPG, moteur d'origine Gamebryo +
serveur C++ `lapis`, client officiel Aeria arrêté). Trois sous-projets :

| Sous-projet | Stack | Rôle | Dépendance moteur |
|---|---|---|---|
| `eden_godot/` | **Godot 4.7.2 mono + C#/.NET** (~103 k LoC, 304 fichiers) | Client de jeu | **C'est ce qu'on évalue de porter** |
| `eden_server/` | **C#/.NET 10** (9 projets, 487 .cs) + PostgreSQL | Serveur (Gateway/Login/World/Zone/Mission/Ticket/HttpApi/Admin) | **Aucune** — le serveur ne change pas, quel que soit le client |
| `re/` | Exports Ghidra (`decompiled.c`) + `index.sqlite` + `reindex` | Corpus reverse-engineering, **lecture seule** | Aucune |

Le protocole réseau est **imposé** par les binaires natifs : TCP,
handshake RSA-2048, chiffrement RC4 (clé session 5 o), trames
`[u16 len LE][RC4(u16 NCID + payload)]`. Il est documenté et testé
byte-à-byte — un client sur n'importe quel moteur doit le réimplémenter
(~1 300 layouts de messages connus, cf. §6).

---

## 2. Chaîne de données : original → converti → runtime

```
CLIENT NATIF (Gamebryo, Windows)              PIPELINE (Python/C#)           RUNTIME ACTUEL
─────────────────────────────────────         ──────────────────────────     ─────────────────────────
raw_assets -> corpus/official/  (9,2 Go)
  char|monster|ride|item|house|effect|
  decorate|map/{model,texture}/
    .nif  (mesh+rig, Gamebryo 20.x)  ──nif2gltf (Python maison)──►  .glb  (20 176) ──► assets/models/**
    .kf + .kfm (anims+séquenceur)    ──anim/merge|trim_anim_glb──►  .glb animés (8 581)
                                                                 + .kfm.json (2 029, métadonnées)
    .dds  (textures, ~56k)           ──convert_dds_to_png.py─────►  .png  (58 862) ──► assets/textures/**
  UI/{itemicon,skillicon,uiicon,
      map,loadingframe}/ .dds        ──copy_ui_png.py────────────►  .png ──► assets/textures/UI|uidata
  uidata/*.layout|.imageset|.scheme  (copié tel quel)            ►  XML CEGUI, parsé au runtime
    |.looknfeel|.font|.TTF                                       (pas de conversion offline)
  data/db/*.ini  (Big5, ~590 tables)  (copié tel quel)           ►  .ini — parse au runtime
                                                                (+ .inib « ITB1 » baké pour dist)
  data/db/lang/fr/t_*.ini (cp1252 FR officiel) ──iconv──────────►  .ini UTF-8 ──► gamedata/data/db/lang/fr
  data/scene/sNNN.ini (+.csm compilé) (tel quel)                 ►  .ini parsé ; .csm INUTILISÉ
  map/model/SNNN/SNNN.fsm (terrain)   (tel quel, format reversé) ►  .fsm parsé au runtime (FsmFile.cs)
  map/sknodemap/SNNN.{smp,fsmp,pgp}   (tel quel)                 ►  .smp parsé (collisions) ; fsmp/pgp inutilisés
  map/model/SNNN/SNNN.{ini,area}      (tel quel)                 ►  .ini env (MapEnvIni) + .area bornes
  bgm/*.ogg + sound/*.wav                                    (pas encore utilisé en jeu)
  pkg/pkg*.pkg (archives zlib)        ──unpacker/pkgfs_unpack───►  fichiers lâches (source du corpus)
                                                          │
                                                          ▼  tools/edenpack (C#)
                                              .epk (zstd + AES-256-GCM + ECDSA) = packs joueur
                                              .inib « ITB1 » = tables ini binaires pré-parsées
```

**Point clé pour le portage** : tout le côté gauche (assets originaux,
formats, corpus) est **indépendant du moteur**. Tout le milieu (GLB,
PNG, kfm.json, .inib, .epk) est **portable tel quel** — ce sont des
formats standard ou documentés. Seul le côté droit (le code qui les
consomme) est couplé au moteur, et encore : partiellement.

---

## 3. Inventaire des fichiers ORIGINAUX (consommés tels quels)

Ces fichiers du client natif sont lus **directement au runtime** (dev :
via `raw_assets` symlink ; joueurs : via packs `.epk`). Aucun n'est
Godot-spécifique — un autre moteur devra juste réimplémenter leur parseur
(déjà écrit en C# pur, voir §5).

| Format | Volume | Contenu | Parseur actuel |
|---|---|---|---|
| `.ini` data/db `c_*`/`C_*` | ~590 tables, Big5/UTF-8 mixte | Catalogue jeu complet : items, monstres, PNJ, sorts, classes, quêtes, boutiques, collections, enchants… | `scripts/data/IniTable.cs` + `tables/*.cs` (38 DTOs) |
| `.ini` data/db `t_*`/`T_*` | ~34+ tables | Textes localisés EN (base) + overlay `lang/fr/` (34 tables FR officielles cp1252→UTF-8) | `TextResolver.cs` |
| `.ini` data/scene `sNNN` | ~230 | Spawns PNJ/mobs/portails par map | `SceneTable.cs` |
| `.fsm` | ~65+35 variantes | Terrain tuilé (grille vertices, splatmap RGBA 128², 5 slots textures), eau, herbe (GRL/GRS), objets posés (OBJ) | `scripts/world/FsmFile.cs` — **spec complète dans `eden_godot/docs/FSM-FORMAT.md`** |
| `.smp` | ~145 | Carte d'obstacles/murs invisibles par map | `scripts/world/NodeMapFile.cs` |
| `.area` | ~146 | Bornes logiques de map (grille ×96 u) | `scripts/data/tables/MapAreaFile.cs` |
| `.ini` map `SNNN.ini` | par map | Environnement : heures, fog, lumière, skybox | `scripts/data/tables/MapEnvIni.cs` |
| `.ifl` | ~dizaines | Flipbooks d'animation de texture (eau) | `scripts/world/IflFile.cs` |
| `.layout` CEGUI | ~440 | Toutes les fenêtres du jeu (XML) | `scripts/ui/CeguiLayout.cs` + `CeguiToGodot.cs` |
| `.imageset` CEGUI | ~85 + thèmes | Découpage d'atlas PNG en régions nommées | `scripts/ui/CeguiImageset.cs` |
| `.scheme`/`.looknfeel`/`.font` | ~7 | Skin/widgets Falagard + descripteurs font | `CeguiScheme.cs`, `CeguiLookNFeel.cs` |
| `.csm` | ~233 | Scène compilée — **non lu** (le .ini/.fsm suffisent) | — |
| `.fsmp`/`.pgp` | ~145/map | Navmesh/pathgraph natif — **non lu** (pathfinding maison `Pathfinder.cs` sur .smp) | — |
| `.nif`/`.kf`/`.kfm`/`.dds`/`.pkg` | ~78k fichiers | Sources de conversion uniquement — jamais lus en jeu | outils offline |
| `uidata/codepage/b2u.*` | 8 | Tables Big5↔Unicode — non lues (CP950 géré par .NET) | — |

---

## 4. Inventaire des fichiers CONVERTIS (`godot_project/`)

| Sortie | Volume | Depuis | Outil | Portabilité |
|---|---|---|---|---|
| `assets/models/**/*.glb` | 11 595 | `.nif` | `tools/nif2gltf` (parseur NIF Python maison, spec `nif.xml`) | **Universel** — glTF binaire standard, lu par tout moteur |
| `assets/anims/**/*.glb` | 8 581 animés | `.kf`+`.kfm` | `tools/anim/{merge,trim}_anim_glb.py` | Universel |
| `assets/anims/**/*.kfm.json` | 2 029 | `.kfm` | nif2gltf | Universel (métadonnées séquenceur : liste des anims par rig) |
| `assets/textures/**/*.png` | 58 862 | `.dds` | `tools/textures/convert_dds_to_png.py`, `copy_ui_png.py` | Universel |
| `gamedata/data/db/lang/fr/t_*.ini` | 34 | client officiel FR cp1252 | `tools/import_edenfr_overlay.py` | Universel |
| `gamedata/data/db/t_npc_extra.ini` | 1 | maison (CN→EN) | — | Universel |
| `gamedata/ui_text_{en,fr,en_fr}.tsv` | 3 | `tools/gen_ui_text.py` | CN→EN/FR | Universel |
| `gamedata/levels.tsv`, `client.cfg`, `settings.cfg` | 4 | maison | — | Universel |
| `gamedata/map/sknodemap/fix/s*.smp` | 9 | `.smp` corrigés | `tools/fix_nodemaps.py` | Universel |
| `*.inib` « ITB1 » | ~803 (dist) | `.ini` | `edenpack bake-tables` | Format maison documenté (`TableBin.cs`) — re-générable |
| `*.epk` | 3 packs | tout `gamedata/`+`assets/` | `tools/edenpack` | Format maison, **spec complète** `docs/packaging/EPK-FORMAT.md` |
| `ui_textures/` (PNG + `.tres` + `.import`) | ~120 | imagesets PNG CEGUI | copie + import Godot | PNG portables ; `.tres`/`.import` **Godot-seul** |
| `shaders/*.gdshader` | 5 | maison | — | **Godot-seul** — à réécrire (voir §7) |
| `scenes/*.tscn` | 15 | maison | — | **Godot-seul** — majoritairement des scènes de test/debug ; les vraies UI sont construites en code |

`assets/` = **~27 Go**, non versionné (régénéré par les outils).

### Audio — non porté
`bgm/` (116 ogg) et `sound/` (~2 400 wav) existent dans le corpus mais
aucun code client ne les référence encore. À prendre en compte dans le
périmètre d'un portage (fichiers standard, zéro conversion nécessaire).

---

## 5. Code client : couplage moteur par dossier

`godot_project/scripts/` = 304 fichiers C#, ~103 k LoC.

| Dossier | LoC | Contenu | Couplage Godot |
|---|---|---|---|
| `protocol/` | ~25 900 | ~1 300 layouts de messages NCID (codecs purs, `NetMessage.Serialize/Deserialize`) | **Nul** — C# pur, portable à l'identique |
| `net/` | ~1 100 | TCP async, RC4, RSA handshake, BinReader/Writer (Big5/CP950) | **Nul** — `System.Net` seul |
| `data/` | ~2 600 + tables/ 38 fichiers | Parseurs ini/ITB1, TextResolver, encodage CP950, TSV | **Nul** — C# pur |
| `vfs/` | ~2 000 | EdenVfs : lecteur .epk (index, zstd, AES-GCM, ECDSA) | Faible — un seul fichier `EdenVfs.Godot.cs` touche l'API Godot |
| `world/` | ~7 000 | FsmFile, NodeMapFile, Pathfinder, TerrainBuilder, GrassRenderer, SkyDome, WaterAnimator, CollisionBuilder | **Fort** — construit des MeshInstance3D/MultiMesh Godot ; mais les **parseurs** (FsmFile, NodeMapFile, IflFile, MapLoader en partie) sont du C# pur qu'on peut extraire |
| `hud/` | ~40 400 | 92 fenêtres UI (inventaire, chat, quêtes, archive, boutiques…) | **Fort** — Controls Godot ; une partie passe par `CeguiToGodot` (traducteur CEGUI→Control) |
| `ui/` | ~3 100 | CeguiLayout/Imageset/Scheme/LookNFeel (parseurs XML CEGUI) + CeguiToGodot | Parseurs = **nuls**, CeguiToGodot = **fort** (émet des Controls) |
| `screens/` | ~4 400 | Login, channel, sélection/création perso, loading | Fort |
| `game/` | ~3 400 | GameScene orchestre, boucle | Fort |
| `player/` | ~2 200 | Contrôleur mouvement/caméra | Fort |
| `combat/` | ~3 200 | Combat, casts, effets | Moyen |
| `session/` | ~1 100 | EdenSession : enchaîne login→world→zone | Faible (orchestration net+protocol) |
| `settings/` | ~2 800 | Réglages client | Moyen |
| `input/`, `scene/`, `perf/`, `debug/` | ~4 300 | — | Moyen/fort |

**Bilan portage** : ≈ **35 k LoC directement réutilisables** (protocol +
net + data + vfs + parseurs monde/CEGUI — du C# sans référence moteur).
≈ 55-60 k LoC à réécrire ou adapter (tout ce qui instancie des
nœuds/scènes/Controls — principalement `hud/`).

Côté **serveur** : rien à porter. Le protocole est la seule interface.

---

## 6. Le contrat réseau (la vraie spec à réimplémenter)

Indépendant du moteur — c'est **la** pièce maîtresse d'un portage :

- **Transport** : TCP. Handshake : serveur envoie clé RSA-2048
  `[u32 n_len][u32 e_len][n BE][e BE]` → client répond 256 o RSA PKCS#1
  v1.5 type 2 d'une clé RC4 5 octets → trames `[u16 len LE][RC4(payload)]`,
  payload = `[u16 NCID][corps]`.
- **Sérialisation** : little-endian ; string = `u16 len` + octets Big5
  (CP950). ⚠️ Encodage à reproduire (fournisseur de codepage .NET ou table).
- **4 services** enchaînés : Login :6543 → Ticket :2067 → World :5200 →
  Zone :5201 (tickets 22 o passés de service en service).
- **~1 300 NCIDs connus** : lexique dans
  `eden_server/docs/generated/ncid_map.json` + `constants.md` ;
  statut de preuve par message dans `FEATURE-MATRIX.md` ; les classes
  C# `protocol/Zone*.cs` + `Eden.Zone/Messages/Generated/*.g.cs`
  (~31 k lignes générées) sont la **base de données des layouts**
  commentée champ par champ avec sources de preuve.
- **Goldens** : 254 cas byte-à-byte `eden_server/tests/Eden.Tests/Data/golden/`
  + `tools/NetTests` côté client (RC4 vecteur, RSA blob, trames de login).
- **Détails** : `eden_godot/docs/NETWORKING.md` (wire complet par service).

Un portage minimal viable = net + protocol + data + un renderer monde
(fsm+smp) + une UI. Tout le reste du client est du contenu par-dessus.

---

## 7. Ce qui est 100 % Godot-spécifique (à réécrire sur un autre moteur)

| Élément | Fichiers | Équivalent à produire |
|---|---|---|
| Scènes | `scenes/*.tscn` (15, surtout debug/test) | Équivalent scènes/prefabs du moteur — faible valeur, les vraies vues sont en code |
| Shaders | `shaders/{terrain_splat,water,sky_dome,fog_ceiling,toon_lit,toon_lit_2s}.gdshader` | **Les plus importants** : terrain_splat = blend 5 couches par splatmap RGBA + vertex color éclairage baké ; water = flipbook .ifl ; toon = éclairage cel ; skydome = dégradé heure ; fog_ceiling = nappe de brouillard |
| Thème UI | `ui_textures/eden_default_theme.tres` + `.import` | Recréer skin/thème — les PNG sources CEGUI restent utilisables |
| Autoloads/lifecycle | `project.godot` (2 autoloads), `.uid` | Équivalents DI/singleton du moteur |
| Nœuds/Controls | tout le code `hud/`, `screens/`, `world/` qui touche `Node3D`/`Control`/`MultiMesh`/`AnimationPlayer` | Réécriture par panneau/système ; les **layouts .layout CEGUI + imagesets** fournissent la spec visuelle de chaque fenêtre |
| Appels Godot épars | `scripts/vfs/EdenVfs.Godot.cs`, ResourceLoader, `FileAccess` res:// | Points d'accès fichier — ~8 endroits (déjà isolés derrière `DataPaths.cs`/`EdenVfs`) |

### Points techniques piégeux pour un autre moteur

1. **Animations** : les `.glb` convertis portent squelettes+clips ;
   le `.kfm.json` dit quel clip jouer par rig (convention lettre :
   `b.kfm` = corps humain…). Le mapping rig→clips est dans
   `game/GlbAnimFile.cs` + `ANIMS-DIRECTIONNELLES.md`.
2. **Skinning multi-meshes personnage** : corps/visage/cheveux/tenue sont
   des GLB séparés assemblés sur un squelette partagé
   (`skinning-matrix-convention` doc archive).
3. **Terrain** : pas de heightmap externe — la géométrie est **dans le
   .fsm** (vertices monde + splatmap par tuile). Height sampling =
   `TerrainSampler.cs` (collision joueur = raycast ou sample, murs =
   .smp + box colliders consolidés `ColliderConsolidator`).
4. **Herbe** : instances GRL/GRS du .fsm → MultiMesh
   (`GrassRenderer.cs`).
5. **UI hybride** : panneaux refaits en code (`hud/*.cs`) + traducteur
   CEGUI→Control runtime (`ui/CeguiToGodot.cs`) pour les layouts
   originaux. Sur un autre moteur : soit porter le traducteur vers le
   système UI cible (la spec est complète : layout+imageset+looknfeel),
   soit recoder les panneaux — les `ref-shots/` (105 captures
   cataloguées dans `ref-shots-catalog.md`) donnent la cible visuelle.
6. **Texte Big5/CP950** : wire + tables c_*. Il faut un décodeur CP950
   (dans .NET : `CodePagesEncodingProvider` ; ailleurs : ICU/table).
   Les `uidata/codepage/b2u.950` du client natif servent de secours.
7. **Coordonnées** : monde Eden Z-up, Godot converti — convention
   documentée dans `WorldConv.cs` / FSM-FORMAT (matrices 3×3 row-major).

---

## 8. Corpus et oracles (sources, hors moteur)

| Chemin | Contenu | Statut |
|---|---|---|
| `corpus/official/` (9,2 Go) | Client officiel **Aeria FR** extrait des pkg + 30 tables fusionnées US | `raw_assets` pointe ici actuellement |
| `corpus/fr-full/` (9,0 Go) | Variante FR complète | bascule via `eden_godot/switch-corpus.sh` |
| `corpus/original-fr/` (352 Mo) | Sous-ensemble FR d'origine | archive |
| `eden_server/reforged/` (hors dépôt, ~29 Go) | Client privé EN complet (= ancienne base raw_assets) | oracle lecture seule |
| `~/Téléchargements/EDENFR/` | Client officiel FR + manifestes | source des t_* FR |
| `eden_server/server_us/` (hors dépôt) | Binaires natifs C++ + `Data/db_decoded/S_*.ini` (tables serveur Big5) | oracle serveur |
| `re/ghidra/<binaire>/decompiled.c` | Pseudo-C de TOUS les binaires (serveurs + clients) | spec exécutable |
| `knowledge/` | Wikis EN (12 840 pages)/FR + 64 feuilles Vendetta en md | doc métier (lvl ≤90) |
| `ref-shots/` | 105 captures du client natif cataloguées | cible visuelle UI |

---

## 9. Docs existantes à transmettre à l'IA d'évaluation

| Sujet | Fichier |
|---|---|
| Stack serveur détaillée | `eden_server/RAPPORT-STACK-SERVEUR-CLIENT.md` |
| Protocole/wire | `eden_godot/docs/NETWORKING.md`, `eden_server/docs/protocole/`, `docs/generated/ncid_map.json` |
| Formats fichiers | `eden_godot/docs/FSM-FORMAT.md` (.fsm complet), `eden_godot/docs/packaging/EPK-FORMAT.md`, `TableBin.cs` (.inib), `PLAN.md` §faits (index .pkg) |
| Assets originaux | `eden_godot/docs/RAW-ASSETS.md` + `RAW-ASSETS-UI-UIDATA.md` (inventaire visuel) |
| Provenance données | `docs/ANALYSE-DONNEES-PROVENANCE.md` (résolution texte, CN/EN/FR) |
| Corpus officiel | `docs/RAPPORT-CORPUS-OFFICIEL.md` + `tools/audit/` |
| État/UI plan | `eden_godot/docs/MASTER-PLAN.md`, `PLAN-UI-GAMEPLAY.md` |
| Chargement | `eden_godot/docs/NATIVE-LOADING-ANALYSIS.md` |
| Stats/combat | `eden_godot/docs/ETAT-SYSTEME-STATS.md`, `AUDIT-ANIMS-SORTS.md` |
| Reverse | `re/AGENTS.md`, `reindex` CLI |
| Features manquantes | `futur-feature/*.md` (22 docs par système) |

---

## 10. Stack du client natif (EDENFR / Reforged / US)

Référence si l'on veut partir du **client original** plutôt que du portage
Godot. Client Windows 32 bits, version `7.4.1`, processus `game_.exe`.

| Couche | Technologie | Preuve / fichiers |
|---|---|---|
| Moteur 3D | **Gamebryo** (NetImmerse), formats `.nif` v20.2–20.3.1, anims `.kf`, séquenceur `.kfm` | chaîne `"Gamebryo File Format, Version 20.3.1.0"` dans le binaire |
| Rendu | **Direct3D 9** + shaders HLSL compilés par machine (cache `Temp/` + `GLCache/`) | options `client.ini` : FXAA, BloomShader, OutlineShader, SSAO, GodRay, DepthOfField, WaterEffect, BackLight, PPMonochrome/PPSepia, OcclusionQuery, Instancing, OpenMP |
| UI | **CEGUI** (ère Falagard, rendu Gamebryo : `CEGUIGamebryoTexture.cpp`) | `uidata/` : 440 `.layout`, `.imageset`, `.scheme`, `.looknfeel`, `.font`, `lua_scripts/` (handlers XML→lua) |
| Audio | **ALAudio.dll** (wrapper OpenAL, `alaudio.ini Mode=2`) | `bgm/*.ogg`, `sound/*.wav` |
| Vidéo | `.ogv` + `.swf` (écrans d'intro) | `Movies/` |
| Données | `.ini` CSV **Big5** (`data/db`), `.fsm` terrain, `.smp` collisions, `.csm` scènes compilées, `.area` | cf. §3 |
| Packaging | `pkgNNN.pkg` zlib + `pkg.idx` (index 592 o/entrée) ; mode « loose files » si idx vide | spec dans `eden_godot/docs/PLAN.md` |
| Réseau | TCP + RSA-2048 + RC4-40, trames `[u16 len][NCID][payload]` | identique au §6 — c'est la même spec |
| Locale | `locate.ini` (`Locate=US|FR`), `locale.info`, manifestes `GameDataFileList.txt` / `GameDataTranslateFileList.txt` (34 `t_*` cp1252 pour le FR) | EDENFR `packver.txt=20150504130915` |
| Mods (Reforged seul) | **`d3d9.dll` proxy hook** + DLLs de features (`mods/_testing/` : Limitless, NeoInventoryBank, ItemMall, gameFix) + `eden_patch.ini` (~40 toggles QoL) | layouts `uidata/layouts/limitless/`, imagesets dédiés |
| Divers | `CrashRpt.dll` (crash reporter → `NoName_*.zip`), `WebBrowser.dll`, `SteamS.dll`, `User.ini` (layout fenêtres), `UserSetting/<charId>_10.ini` | **pas d'anti-cheat** dans les copies présentes |

### Flux de chargement natif (prouvé par désassemblage)

1. `Launcher.exe` (patch) → `game_.exe` ; lecture `client.ini`,
   `connect.ini` (IP), `locate.ini`, `mustdata.ini` (fichiers requis).
2. **Écran de chargement Win32 pré-D3D** : `Loading_%d.dds` blitté sur le
   HDC avant même le device D3D.
3. **Chargement DB monolithique synchrone** : **tous** les `data/db/*.ini`
   en une passe avant le monde 3D.
4. Login (gateway→login→world→zone, tickets 22 o) puis **CZone
   séquentiel** : `.fsm` (terrain/objets) + textures streamées selon
   `CharacterTexture`/`SceneTexture` ; options `PrecacheKFM`,
   `UseMappedFile`, `Instancing`, `OpenMP`, `BelowNormalLoading`.
5. UI : CEGUI charge `DejaVu.scheme` → fonts/imagesets/looknfeel ;
   positions de fenêtres par perso dans `UserSetting/`.
6. Shaders HLSL recompilés/cachés par machine (`Temp/*.hlsl`, `GLCache`).

Source : `eden_godot/docs/NATIVE-LOADING-ANALYSIS.md`, `RAW-ASSETS.md`,
strings `re/ghidra/rf_game`, `client.ini`.

### Partir de EDENFR vs du corpus converti

Pour un portage sur un autre moteur, **repartir des assets convertis
(GLB/PNG/kfm.json) + données originales (.ini/.fsm/.smp/.layout)**, pas
des `.nif`/`.dds` bruts : les conversions nif2gltf et dds→png sont déjà
faites, validées en jeu, et GLB/PNG sont lus par tout moteur. Refaire le
pipeline depuis EDENFR n'apporterait que du risque. La seule chose à
reprendre de EDENFR en plus = le contenu officiel (tables `t_*` FR,
cartes manquantes) — déjà fusionné dans `corpus/official/`.

---

## 11. Verdict rapide pour l'évaluation

- **Réutilisable tel quel, tout moteur** : 20k GLB + 59k PNG + 2k
  kfm.json + toutes les données .ini/tsv/fsm/smp + la spec protocole +
  les codecs C# (~35 k LoC) + le serveur complet + EPK si le moteur est
  .NET-compatible.
- **À réécrire** : rendu monde (shaders+builders ~7k LoC), UI (~46k LoC),
  contrôleurs/écrans (~10k LoC) — la spec visuelle existe (ref-shots +
  layouts CEGUI), la spec comportementale existe (binaires décompilés).
- **Moteur cible idéal** : tout moteur **C#** (Unity, Flax, Stride,
  Godot reste) réutilise protocol/net/data/vfs **à l'identique**. Un
  moteur C++/Rust/C++-script réimpose de porter les codecs — envisageable
  car formats documentés + goldens byte-à-byte + `reindex` pour les
  zones d'ombre, mais c'est le surcoût majeur.
- **Non-bloquant mais à savoir** : audio pas encore branché ; ~30 % des
  NCIDs zone non implémentés (dispatch brut) ; ridelist.ini absent du
  gamedata ; ui FR couvre ~85-97 % selon tables.
