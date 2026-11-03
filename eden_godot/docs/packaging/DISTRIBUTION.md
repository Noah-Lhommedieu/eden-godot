# Distribution — client Eden Reborn autonome

Comment produire et lancer un export autonome du client Godot
(`dist/package-linux/`, `dist/package-windows/`).

## Construire

```bash
# 1. Exports Godot (presets déjà définis dans export_presets.cfg)
cd godot_project
godot --headless --export-release "Linux/X11"       # → dist/linux/
godot --headless --export-release "Windows Desktop" # → dist/windows/

# 2. Assemblage des packages autonomes
cd ..
tools/package_dist.sh            # assets = symlink dev-local
tools/package_dist.sh --full     # copie réelle des assets (~22 Go)
tools/package_dist.sh --target linux   # une seule cible
```

`package_dist.sh` recopie le binaire + `.pck` + `data_*` puis remplit
`gamedata/` depuis `raw_assets/` (symlink vers le client Reforged
extrait — **déréférencé**, le package contient de vrais fichiers) :

```
package-linux/
├── EdenReborn.x86_64                 # binaire exporté
├── EdenReborn.pck                    # scènes, scripts, ui_textures, shader
├── data_EdenClientReborn_linuxbsd_x86_64/   # assemblies .NET
└── gamedata/
    ├── client.cfg                    # connexion (éditable, voir plus bas)
    ├── uidata/                       # CEGUI natif : layouts, imagesets,
    │                                 #   looknfeel, schemes, fonts, *.ttf
    ├── data/
    │   ├── db/                       # tables ini (c_monster, T_Class…)
    │   └── scene/                    # spawns/NPC/events par map
    ├── map/model/Sxxx/
    │   ├── Sxxx.{fsm,ini,area}       # terrain, env, grille
    │   └── Dark/                     # minimaps d'ombre
    │   (pas les .nif — remplacés par les GLB de assets/)
    └── assets/                       # contenu converti nif2gltf/dds2png
        ├── textures/  models/  anims/
        └── symlink → godot_project/assets  (défaut)
            ou copie réelle avec --full
```

Tailles : **~1,3 Go** par package sans les assets convertis
(105 Mo binaire+runtime, 30 Mo pck, 83 Mo uidata, 67 Mo data,
968 Mo map dont 74 Mo Dark/), **~23 Go** avec `--full`.

## Résolution des chemins (scripts/DataPaths.cs)

Chaque donnée runtime est cherchée dans les racines candidates, par
ordre :

1. `$EDEN_DATA_DIR` — la racine contient soit `raw_assets/`, soit
   directement `uidata/`, `data/`, `map/`, `assets/` ;
2. `<dossier de l'exécutable>/gamedata/` — le cas package ;
3. dev : `raw_assets/` en remontant depuis `res://` et le CWD (le
   symlink `portage/raw_assets` est trouvé à `res://../raw_assets`) ;
4. `res://gamedata/` — dossier du projet.

Les assets convertis ajoutent `res://assets` (dossier `.gdignore`'d de
godot_project) à la chaîne ; `EDEN_ASSETS_ROOT` et `EDEN_RAW_ASSETS`
restent des overrides prioritaires par racine.
`client.cfg` : `gamedata/client.cfg` à côté de l'exécutable l'emporte
sur la copie embarquée dans le pck (dernier repli).

## Lancer

Linux :

```bash
cd dist/package-linux
./EdenReborn.x86_64
# ou en pointant ailleurs les données :
EDEN_DATA_DIR=/chemin/vers/gamedata ./EdenReborn.x86_64
```

Windows (via Wine) :

```bash
cd dist/package-windows
wine EdenReborn.exe
```

Prérequis Wine : Vulkan fonctionnel (pilote GPU exposé au préfixe,
idéalement DXVK installé — le renderer Forward+ exige Vulkan 1.x).
Tester avec `wine64 vulkaninfo` ou `winetricks dxvk` en cas d'échec.

## Config serveur

`gamedata/client.cfg` (format `key=value`, `#`/`;` = commentaires) :

```ini
host=127.0.0.1
login_port=6543
ticket_port=2067
world_port=5200
zone_port=5201
account=
client_version=002.003.05.67
machine_id=BC-EC-A0-43-F3-80
```

Le champ host:port est aussi éditable dans l'écran de login.

## Notes / limites

- `gamedata/assets` en symlink = pratique pour tester en local depuis
  le dépôt ; **un vrai package destiné à être distribué exige
  `--full`** (copie des ~22 Go de textures/models/anims convertis).
- `map/model/` ne contient volontairement pas les `.nif` (5 Go) : les
  modèles/terrains/objets passent par les GLB de `assets/models/`.
  `data/scene/*.csm` et le reste de `data/` sont copiés tels quels.
- `bgm/`, `CutScene/` et autres dossiers du client natif ne sont pas
  lus par le code actuel → absents du package.
- **Écart connu (lot G4, vérifié)** : au démarrage le package logue
  `Can't open file from path 'res://ui_textures/FONT01.ttf'` — les
  .ttf du projet ne sont pas embarqués dans le .pck (pas importés,
  ui_textures hors pipeline d'import). Le repli
  `gamedata/uidata/fonts/FONT01.ttf` de `SettingsApplier.ApplyFont`
  prend le relais → fonte chargée, message bénin. Fix possible : copier
  les .ttf dans `gamedata/` ou les importer dans le pck.
- Vérifié lot G4 : `dist/package-linux/EdenReborn.x86_64` boote sous
  X11 jusqu'à l'écran de login (uidata résolu depuis le package,
  capture `godot-shots/pkg_g4_login.png`) ; `package-windows` boote
  sous `wine` (même point, log seul — pas de capture). Sortie par
  kill/timeout OK ; le quit sauvage logue des warnings « RID leaked »
  (bruit moteur).

## Étape EPK — le layout `gamedata/` devient des packs

L'assemblage ci-dessus produit le `gamedata/` source. Pour le
**client joueurs**, ce même layout se packe en `.epk` (voir
`TECH-STACK.md` pour la stack complète et `release.excludes` du
profil d'exclusions) :

```bash
EP=tools/edenpack/bin/Release/net8.0/edenpack
$EP bake-tables dist/package-linux/gamedata/data --check   # ini → ITB1
$EP bake "assets/models/**/*.glb" .../gamedata/assets/models  # .scn
$EP build .../gamedata eden-core.epk \
    --include 'uidata/**' --include 'data/**' --include 'map/**' \
    --exclude-file tools/edenpack/release.excludes \
    --encrypt --keyfile rel.key --sign-key relsign.der --sign-keyid 1
$EP verify eden-core.epk --keyfile rel.key --pubkey relsign.pub
$EP install dist/packs .../gamedata      # bascule atomique current.json
```

`EDEN_ALLOW_LOOSE` n'est honoré qu'en debug — le package joueur ne
lit que les packs signés.

## Tester le client packagé (dev)

Chemin rapide — sans toucher `godot_project/gamedata/` :

```bash
# 1. Packs déjà construits : dist/packs-test/ (core + assets +
#    tex webp) ou reconstruire via les commandes ci-dessus.
# 2. Lancer le client DEV en mode pack-seul :
cd eden_godot
DISPLAY=:0 EDEN_ALLOW_LOOSE=0 \
  EDEN_PACKS="$PWD/dist/packs-test/eden-core.epk:$PWD/dist/packs-test/eden-assets.epk:$PWD/dist/packs-test/eden-tex.epk" \
  godot --path godot_project
# attendu dans le log : « [vfs] N pack(s) — loose désactivé »
```

- `EDEN_PACKS` (séparateur `:`) — chemins **absolus** ; contournement
  dev explicite, ignore `current.json`. Ordre = ordre de montage,
  le dernier gagne au lookup (overlay : un `cfgfix.epk` listé en
  dernier masque les fichiers des autres).
- `EDEN_ALLOW_LOOSE=0` simule le comportement release : aucun
  fichier lâche lu — un trou du pack devient visible au lieu
  d'être masqué par le disque. Sans la variable, un build debug
  lit le loose d'abord (dev normal inchangé).
- Le vrai layout joueur = `current.json` dans `gamedata/` :
  `edenpack install <dir-packs> godot_project/gamedata` installe +
  bascule atomique le pointeur — à utiliser pour tester le chemin
  réel (penser à retirer les `.epk`/`current.json` après).
- Crypto : `--keyfile` à générer (voir TECH-STACK §crypto) ;
  `EDEN_PACK_B` fournit la part B en dev — en prod elle vient de la
  session TLS. Packs non signés = dev seulement (refusés en export
  release).
- Inventaire complet des éléments dev-only encore présents dans le
  client packagé (cfg locale, clés dev-release, toggles env, args de
  debug…) : `TECH-STACK.md` § « Dev-only présent dans le client
  packagé » — la liste des choses à rectifier avant une vraie
  distribution.

Vérifications : `edenpack verify` sur chaque pack, capture
Flameshot du login/monde, `--map=S001 --solo` pour le offline.
Un run sain affiche l'UI **en français** (lang/ + tsv packagés)
avec le thème actif — l'anglais forcé ou des panneaux plats =
trou de contenu packagé, pas un bug moteur.
