# TECH-STACK — client packagé Eden (EPK/VFS)

> Vue d'ensemble technique du système de packaging client : ce que les
> joueurs exécutent, pourquoi ces choix, et la pipeline de construction.
> Spec binaire figée : `EPK-FORMAT.md`. Plan et gates : `PLAN-PACK-EPK.md`.

## Stack

| Couche | Technologie | Rôle |
|---|---|---|
| Moteur | Godot 4.7.2 Mono (`Godot.NET.Sdk`) | rendu, scènes, GLTF |
| Langage | C# / .NET 8 | tout le code client |
| Données | EPK v1 (`scripts/vfs/`) | packs versionnés, accès aléatoire |
| Compression | zstd via `ZstdSharp.Port` | frames indépendantes par chunk 4 Mio |
| Crypto | AES-256-GCM + ECDSA P-256 + HKDF-SHA256 (BCL, zéro dép.) | intégrité + protection au repos |
| GLB | `GltfDocument` natif | modèles ; URI externes matérialisées en cache |

## Pourquoi un format custom et pas `.pck`/`.zip`

- Le `.pck` Godot n'a ni index signé, ni chiffrement par bloc, ni
  diff incrémental versionné → ne répond ni à l'intégrité ni à la
  taille des patchs.
- Chunk 4 Mio = unité de random-access : un fichier de 500 Mio se lit
  sans décompresser le pack entier (chaque chunk = frame zstd +
  contexte GCM indépendants).
- Index sérialisé **une seule fois** (`EpkIndex`, partagé client ⇄
  `edenpack` par `<Compile Include>`) : impossible de diverger.
- `cryptoRev` par entrée : un fichier inchangé garde ses blocs
  chiffrés byte-identiques → les patchs ne transportent que le diff.

## Architecture

```
gamedata/  (layout = espace de clés du pack)
├── bootstrap.epk   clair signé (login/UI de secours)
├── eden-core.epk   uidata/ + data/ + map/ meta (~1,5 Go cible)
├── eden-assets.epk assets/ convertis (GLB slim, textures GPU)
└── current.json    pointeur signé {pack → fichier, version, sha256}
```

`EdenVfs` (`scripts/vfs/EdenVfs.cs`, façade statique) est LE point
d'entrée des lectures de données : `Exists`, `ReadAllBytes`,
`ReadAllText`, `ReadLines`, `OpenRead`, `ListDir`, `Resolve`.
Backends : loose (dev) ⇄ EPK (joueurs) ; multi-packs = overlay,
les packs suivants écrasent les précédents.

- Dev : loose prioritaire, pack en repli → itérer sans rebuild.
- Joueur : `EDEN_ALLOW_LOOSE` ignoré en release → intégrité garantie.
- `EDEN_VFS=0` : kill-switch, retour 100 % loose.
- `EDEN_PACKS=a.epk:b.epk` : liste explicite (overlays/patchs).

Cas spécial GLB : les URI d'images externes exigent un `basePath`
fichier → le VFS matérialise sous `user://cache/epk/<packVersion>/`
(purge par version). `.scn` bakés : même voie (PoC ×17-18, gate passé).

## Pipeline release

```bash
# 1. Tables ini → ITB1 (binaire indexé, équivalence --check)
tools/edenpack/bin/Release/net8.0/edenpack bake-tables gamedata/data --check

# 2. GLB → .scn bakés (compression GPU réelle — DISPLAY requis)
edenpack bake "assets/models/**/*.glb" gamedata/assets/models

# 2b. Textures → variantes bakées (TexBake via client headless).
#     Mesuré corpus réel : webp q85 = 7,9 Go → 1,35 Go (−83 %, choix
#     size) ; BC7 DDS = +52 % mais GPU-ready (decode+mipgen+compress
#     CPU supprimés). Écrit <dst>/tex-baked.excludes = liste des
#     .png convertis → --exclude-file pour le pack assets.
edenpack bake-tex assets dist/tex-webp/assets --fmt webp --jobs 10
edenpack build dist/tex-webp eden-tex.epk \
    --exclude 'assets/tex-baked.excludes'
# runtime : .dds > .webp > .png (LoadTexture + rewrite URI GLB)

# 3. Packs (profil d'exclusions release.excludes : fat GLB, Dark/, kfm…)
edenpack build gamedata eden-core.epk --include 'uidata/**' \
    --include 'data/**' --include 'map/**' --exclude-file tools/edenpack/release.excludes \
    --version 3 --prev eden-core-v2.epk --encrypt --keyfile rel.key \
    --sign-key relsign.der --sign-keyid 1

# 4. Vérif complète (signature + index + TOUS les tags GCM — prod)
edenpack verify eden-core.epk --keyfile rel.key --pubkey relsign.pub

# 5. Installation atomique multi-packs (écrit tout → bascule current.json)
edenpack install dist/packs dist/gamedata
```

## Sécurité — vérité terrain

- Clé = `Kwrap = HKDF(A‖B, salt=packId)` ; `A` obfusqué côté client,
  `B` livré par le serveur dans la session TLS authentifiée.
  **Aucun secret serveur dans un pack.**
- `Kfile = HKDF(Kdata, fileId‖cryptoRev)`, nonce = index de chunk —
  unique par construction ; invariant vérifié au build.
- Tout est signé (y compris `bootstrap.epk`, dont les blobs clairs
  sont liés par sha256 par entrée dans l'index signé).
- Limites assumées : cela protège la distribution et élève le coût
  d'extraction — ce n'est **pas** une protection contre un attaquant
  qui contrôle le client en RAM (anti-rollback offline = borne,
  enforcement serveur en ligne).
- Vérif à 3 niveaux : release = tous les blocs ; boot = signatures
  seules (ms) ; lecture = tag GCM du chunk (gratuit au decrypt).
- Clés provisionnées (dev-release 2026-10-30) : publique embarquée
  (`EdenReleasePubKey`), part A réelle obfusquée ; privée + keyfile
  hors dépôt `~/.config/eden/release/` — à régénérer en env dédié
  pour la distribution réelle.
- Durcissement release : build non-debug → packs non signés et
  `current.json` non signé refusés au mount ; debug = permissif
  (dev). `EDEN_PACK_PUBKEY`/`EDEN_PACK_B` = overrides dev.

## Mises à jour / rollback

- Patch : `edenpack diff` (entrées changées/ajoutées) → `apply`
  reconstruit le pack complet → `verify` → `install`.
- Install = write-all → rename atomique du `current.json` signé :
  jamais de mix de versions ; rétention N-1 pour rollback opérateur.
- Suppressions : `diff` émet une entrée **tombstone** (`codec=0xFF`)
  par fichier disparu ; `apply` la retire du merge et, en overlay,
  elle ombre définitivement la couche de base (cf. EPK-FORMAT § Tombstones).
- Replis : `EDEN_VFS=0` (loose), GLB mémoire (si `.scn` absent),
  parse ini (si `.inib` absent) — chaque couche dégrada­ble seule.

## Dev-only présent dans le client packagé — à rectifier avant distribution

Ce qui fonctionne en test aujourd'hui mais qui ne doit PAS partir
tel quel en production. Chaque item indique où il vit.

**Configuration embarquée (packs)**

- `client.cfg` (dans `eden-core.epk`) : `host=127.0.0.1`,
  `tls_thumbprint` du cert **local dev**, `machine_id` codé de la
  machine dev → régénérer pour la prod (hôte réel, thumbprint prod,
  `machine_id` par install ou vide).
- `settings.cfg` (core pack) : preset dev shipé — windowed
  1600×900, `preset=low`, `language=fr`. Volontaire ; à revoir si
  les défauts joueurs changent.
- `cfgfix.epk` : overlay de test (thumbprint TLS avant que
  client.cfg complet soit packagé) — désormais redondant ; sert
  d'exemple de mécanisme overlay, pas de livrable.

**Clés / crypto**

- `EdenReleasePubKey` + part A (`scripts/vfs/EdenVfs.cs`) : paire
  **dev-release** — privée hors dépôt `~/.config/eden/release/`.
  Régénérer en env dédié pour la vraie distribution.
- `EDEN_PACK_B` : part B passée en clair par env var — dev only ;
  la prod livre B via la session serveur.

**Sécurité permissive en debug**

- `OS.IsDebugBuild()` → packs et `current.json` **non signés
  acceptés** (`SecurePack`, `VerifyManifest`) et `LooseAllowed`
  true par défaut. Le binaire godot dev est un build debug → tout
  ceci est actif en test. Le rejet est exercable via
  `EDEN_PACK_STRICT=1` (force la sémantique release en dev, sens
  unique — vérifié : packs non signés refusés, 0 monté). Seul
  l'export release prouve le flag lui-même — API Godot standard.
- `EDEN_PACKS` : bypass du manifest par env var (chemins **absolus**,
  `:`-séparés, dernier gagne) — dev seulement ; décider si une env
  var doit pouvoir monter des packs arbitraires en release.

**Toggles env dev**

`EDEN_VFS=0` (kill VFS), `EDEN_ALLOW_LOOSE`, `EDEN_DATA_DIR`,
`EDEN_ASSETS_ROOT`, `EDEN_PACK_PUBKEY`, `EDEN_GODOT` (bake),
`EDEN_PACKS`, `EDEN_PACK_B`, `EDEN_PACK_STRICT` (force release).

**Loose-readable `res://`/`user://`**

`EdenVfs.LooseReadable` autorise la lecture disque sous `res://` et
`user://` même en pack-seul — reflète l'export réel (ces fichiers
sont dans le .pck / userdata). Conséquence : le test pack-seul dev
n'interdit pas 100 % du disque — un fichier projet oublié dans
res:// serait lu alors qu'un export propre ne l'aurait pas (les
données sous `gamedata/`/`assets/`/`raw_assets/` restent bloquées).

**Outils dev compilés dans le binaire client**

Args `ScreenManager` : `--bake-protos`, `--bake-textures`,
`--shot`, `--map`, `--solo`, `--login` (`admin:adminmdp` en test),
`--live`, tests HUD — présents dans le build, à envisager de
stripper à l'export.

**Corpus / artefacts de test**

- `dist/packs-test/` : set de packs local, **pas** le livrable
  (pas de `current.json` — montage via `EDEN_PACKS` ou scan legacy ;
  la voie release = `edenpack install --sign-key` → pointeur signé).
- `.scn` protos bakés : produits/mesurés (1156 protos, ~2,5 Mo)
  mais non intégrés aux packs de test — le repli GLB mémoire reste
  le chemin actif, prévu dans la stack.
- `assets/` (`godot_project/`) : `.gdignore`d → hors .pck, remplacé
  par les packs. `ui_textures/` non gdignoré → embarqué .pck **et**
  présent sous `uidata/imagesets/` dans le core pack (doublon
  ~85 Mo assumé — les deux chemins servent selon debug/export).
