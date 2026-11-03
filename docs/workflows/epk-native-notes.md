# EPK natif : notes d'outillage et lectures hors EdenVfs (P1, P2, P3, P5)

Statut : `verified` (selftest synthétique + build réel `artifacts/packs/test-native`), 2026-11-02,
edenpack (dotnet 10.0.402). Aucun script de `godot_project/scripts` n'a été modifié.

## 1. Commandes

```bash
# clés de TEST (jamais de clés de production) — sous artifacts/, hors dépôt de données
dotnet <edenpack>.dll keygen artifacts/packs/test-keys

# P1 + P2 : 4 paquets natifs + provenance.tsv
dotnet <edenpack>.dll build --native artifacts/packs/test-native \
    --sign-key artifacts/packs/test-keys/sign.key \
    --keyfile artifacts/packs/test-keys/keyfile.txt --version 1
#   défauts : --corpus corpus/fr-full, --gamedata eden_godot/godot_project/gamedata,
#   --manifest ~/Téléchargements/EDENFR/GameDataFileList.txt,
#   --verify-tsv/--extract-tsv docs/provenance/edenfr-{verify,extract}-2026-11-02.tsv
#   --prev-dir <release N-1> OBLIGATOIRE pour une v2 chiffrée (cryptoRev)
#   --only eden-core,bootstrap   sous-ensemble ;  --allow-unsigned = test seulement

# preuves
dotnet <edenpack>.dll verify P.epk --pubkey sign.pub --keyfile keyfile.txt --tamper-test
eden_godot/tools/edenpack/tests/native-selftest.sh [workdir]     # synthétique, ~1 min

# P5
dotnet <edenpack>.dll diff old.epk new.epk patch.epk
dotnet <edenpack>.dll apply base.epk patch.epk out.epk --sign-key sign.key   # re-signe
dotnet <edenpack>.dll install <setdir> <gamedata> --sign-key sign.key        # bascule current.json
```

`<edenpack>.dll` : `dotnet build eden_godot/tools/edenpack -c Release -o <dir>`.

## 2. Routage (NativeBuild.Route)

| Paquet | Contenu | Chiffré |
|---|---|---|
| `bootstrap` | `uidata/{schemes,looknfeel,fonts,codepage,imagesets}/**`, `uidata/layouts/login*.layout` et `*worldserverlistwnd.layout`, overlay `client.cfg`, `login_bg.jpg`, `ui_text_*.tsv` | non (signé) |
| `eden-core` | reste de `uidata/`, `data/**`, `ui/**` (dont `UI/`), `map/**` en `.fsm .smp .ini .area`, overlay (`data/db/lang`, `map/sknodemap/fix`, `levels.tsv`, `t_npc_extra.ini`) | oui |
| `eden-assets` | `char monster ride item effect decorate house emblem cutscene`, + le reste de `map/` (`.nif .dds .ifl .kf…`) | oui |
| `eden-audio` | `sound/`, `bgm/` | non (signé) |

Non empaquetés (comptés dans la sortie du build) : fichiers racine (exe, dll, `ACDC.dat`, `game.bin`…),
`movies/`, `Movies/` (swf/ogv, aucun lecteur VFS), `map/**.fsmp .pgp .rar .txt` (inutilisés, cf.
`release.excludes`), overlay `*.import` et `settings.cfg` (écrit par le client à l'exécution).
Le routage est un tableau dans `NativeBuild.Route` : à ajuster ici si le client change.

Codec : DDS à fourcc (BC1-5, DX10…) = `raw` forcé (texCodec raw) ; tout autre fichier = `zstd`, repli
`raw` si le gain est inférieur à 2 % (ogg, wav déjà denses). Aucun bake-tex/ScnBake/réécriture GLB/bake-tables.

## 3. Provenance (P2)

Pour chaque entrée issue du corpus : taille + CRC32C comparés au manifeste (clé minuscule, `\`→`/`).
Statuts de `provenance.tsv` (colonnes : pack, chemin EPK, taille, sha256 EPK, crc32c, crc officiel, taille officielle, statut) :

| Statut | Règle |
|---|---|
| `OK` | taille et CRC conformes |
| `EXCEPTION-AUCUNE-SOURCE` | différence tolérée seulement si le chemin est `AUCUNE-SOURCE` dans `edenfr-verify-2026-11-02.tsv` et sous `sound/` ou `bgm/` (1439 fichiers, avertissement) |
| `PKG-HORS-MANIFESTE` | absent du manifeste mais `OK-pkg-HORS-MANIFESTE` dans `edenfr-extract-2026-11-02.tsv` (38 fichiers extraits des `.pkg` : 34 `data/db/t_*.ini` (core), 3 textures à nom atypique (assets), `S100.txt` non empaqueté) |
| `OVERLAY-PROJET` | provient de `godot_project/gamedata/`, pas de contrôle |

Tout le reste fait échouer le build (message `PROVENANCE : <chemin> …`, `.building` supprimé).

Limites : sha256 EPK = hash du clair ; ECDSA et le bloc wrap AES-GCM utilisent des nonces aléatoires, donc
deux builds identiques ne diffèrent que par ces 60+68 octets (testé).
Patch non signé (`diff`) : à passer par `apply --sign-key` avant installation sur un client release.

## 4. Lectures hors EdenVfs (P3) — `grep` du 2026-11-02

Motifs : `File.(Exists|ReadAll*|OpenRead|ReadLines|Open)`, `Directory.*`, `FileAccess.*`, `DirAccess.*`,
`Image.LoadFromFile`, `AppendFromFile`, `GD.Load`/`ResourceLoader.*` dans `eden_godot/godot_project/scripts`
et `src/`. Classement ; rien ci-dessous n'a été modifié.

### A. À migrer vers EdenVfs (donnée de jeu, contourne le paquet)

| Fichier:ligne | Appel | Problème |
|---|---|---|
| `data/TextResolver.cs:347,354` (+ `src/Eden.Core/Data/TextResolver.cs`) | `File.Exists` sur `raw_assets/` | résolveur par défaut hors paquet ; ne voit jamais l'EPK |
| `data/tables/StoreTable.cs:82,88` (+ copie `src/`) | `File.Exists` sur `raw_assets/` | idem |
| `data/UiTextEn.cs:41` (+ copie `src/`) | `File.Exists` sur `gamedata/` | idem (anglais en pause) |
| `AssetCatalog.cs:397` | `GltfDocument.AppendFromFile` | GLB loose : chemin hors VFS (formats dérivés, supprimé en phase G) |
| `AssetCatalog.cs:704, 793-828` | `File/Directory.Exists`, racine assets | résolution loose ; `LooseAllowed=0` ne couvre que la branche EPK |
| `AssetCatalog.cs:742-755` | `Image.LoadFromFile` (loose) | texture DDS/PNG par chemin |
| `ui/CeguiToGodot.cs:91,104,121,125,131,149` | `DirAccess`, `FileAccess.FileExists`, `GD.Load<Texture2D>`, `File.Exists` | imagesets via `res://ui_textures/` (PNG convertis dans le pck, pas l'EPK) |
| `ui/CeguiImageset.cs:63` | chemin `res://ui_textures/imagesets/` | idem |
| `hud/TransportWindow.cs:369-371` | `ResourceLoader.Exists/Load` sur `res://ui_textures/imagesets/` | fond de carte hors VFS |
| `combat/DamageFloaters.cs:76` | `res://assets/textures/effect/…png` | texture dérivée hors VFS |
| `settings/SettingsApplier.cs:410-437` | `FileAccess.FileExists(Materialize(...))` + `res://ui_textures/<font>.ttf` | polices : `Materialize` légitime (Godot exige un chemin) mais la source est `res://ui_textures` |
| `ClientConfig.cs:179` | `FileAccess.FileExists("res://gamedata/client.cfg")` | repli pck ; en P6 le pck ne doit plus porter de donnée |
| `settings/ClientSettings.cs:558,598-609` (+ `src/Eden.Core/Data/ClientSettings.cs:586`) | `File.Exists`, `res://gamedata/settings.cfg` | template embarqué dans le pck (P6) |

### B. Légitimes (état utilisateur, détection d'environnement, outils)

- `DataPaths.cs:73-125`, `session/EdenSession.cs:303,327` (+ `src/`), `vfs/EdenVfs*.cs`, `vfs/Epk*.cs`,
  `src/Eden.Core/Vfs/*` : détection du dossier gamedata et implémentation du VFS (côté loose).
- `data/ClientUiSettings.cs:64` (+ `src/`), `screens/CharSelectScreen.cs:768,783` (`user://`),
  `perf/PerfProbe.cs:268` (`/proc/self/status`), `hud/RideProbe.cs` (debug `/tmp`) : état utilisateur/diagnostic.
- `debug/TexBake.cs`, `debug/ScnBake.cs`, `debug/PocScn.cs` : outils de bake, retirés avec la phase G.
- `src/Eden.Core/Pkg/PkgArchive.cs`, `EdenfrManifest.cs` : import `.pkg` (ADR-0005, lecture seule, hors client).

Déjà conformes (passent par `EdenVfs.ReadAllBytes/OpenRead`) : `world/FsmFile.cs`, `world/NodeMapFile.cs`,
`ui/CeguiLayout.cs`, `data/IniTable.cs`, `data/MyCrypt.cs`, `AssetCatalog.cs:380,774`, `ClientConfig.cs`.
Les décodeurs `src/Eden.Core/Formats/*` (`NifFile`, `KfFile`, `KfmFile`, `DdsInfo`, `IflFile`) n'appellent
aucun `File.*` : ils reçoivent des octets.

### C. Remarques pour P3/P6

- Aucun code audio (`.ogg`/`.wav`/`AudioStream*`) dans `scripts/` : `eden-audio.epk` n'a pas encore de lecteur.
- Les chemins `res://ui_textures`, `res://assets`, `res://gamedata` désignent des données dans le `.pck` ; ils
  sont à remplacer par des lectures `uidata/…` / `ui/…` depuis `bootstrap`/`eden-core` avant P6.
- Garde-fou proposé : test qui échoue si un nouveau `File.*`/`FileAccess.*` apparaît hors liste B.

## 5. Constat : paquets en clair (corrigé 2026-11-02 : `EpkReader` vérifie le sha256 à la lecture, `ReadAllBytes` et `OpenRead`)

`EpkReader` ne contrôle aucun sha256 de blob. Sur un paquet chiffré le tag GCM refuse un octet falsifié ; sur
`bootstrap` et `eden-audio` (en clair, signés) la signature ne couvre que l'index : un octet de blob modifié
est lu sans erreur (vérifié : `extract` réussit, `verify` échoue sur `sha256 mismatch`). Mesure prise côté
outil : `edenpack install` recalcule le sha256 de chaque entrée des paquets en clair et refuse l'installation.
À décider côté client (`scripts/vfs/EpkReader.cs`, hors périmètre ici) : sha256 à la première lecture des
paquets en clair, ou chiffrer aussi `eden-audio`.

## 6. Non fait

- Pas de vérification que les loaders client lisent bien tous les chemins du routage (P3/P6, autres agents).
- Partie B de la clé fournie par le serveur au login (P4) : le keyfile de test contient A et B localement.
- `emblem/`, `cutscene/` : routés en `eden-assets` sans lecteur confirmé.
- Pas de benchmark chargement/RSS (paquet contre fichiers libres).


## 7. Run strict (2026-11-02)

Clés de test = clés dev de release embarquées dans le client (`~/.config/eden/release/`). Build :
`dotnet artifacts/packs/.edenpack-bin/edenpack.dll build --native <dir> --sign-key ~/.config/eden/release/sign.pem --keyfile ~/.config/eden/release/dev-keyfile.txt --version 1`.
Run : `EDEN_PACKS=<dir> EDEN_ALLOW_LOOSE=0 EDEN_PACK_STRICT=1 EDEN_PACK_B=<champ b= du keyfile> tools/capture.sh S001`.
Les lectures hors VFS restantes sont plafonnées par `tests/Eden.Core.Tests/VfsReadGuardTests.cs` (liste par fichier).
