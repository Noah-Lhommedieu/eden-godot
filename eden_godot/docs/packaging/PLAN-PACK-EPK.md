# Plan d'action — Eden EPK : pack chiffré, allègement, perf chargement

Date : 2026-10-29.
Contexte : `ANALYSE-PORT-ANDROID.md` (moteur), `ANALYSE-DATA-PACKAGING.md`
(carto du chargement + budget poids).

## État d'exécution (2026-10-30 — plan complet)

| Étape | État | Preuve |
|---|---|---|
| 0 Audit/baseline | ✅ | `pack_audit.py` + `AUDIT-PACK-BASELINE.md` (slim 1:1, 0 orphelin, 27,3→18,3 Go hors textures) |
| 1 EPK + VFS | ✅ | `scripts/vfs/` + `tools/edenpack` ; login pack-seul capturé (`EDEN_ALLOW_LOOSE=0`) ; round-trip byte-parfait |
| 2 Allègement | ✅ | `release.excludes` (`!`, fat-vs-slim) + **TexCodec** : `bake-tex` webp **−83 %** (7,9→1,35 Go), `.dds` BC7 dispo (+52 % GPU-ready) ; URI GLB réécrites live (177 prouvées S001) ; set total **~5,5 Go** vs 27 Go source |
| 3 PoC `.scn` | ✅ GO ×17-18 | `POC-SCN-RESULTS.md` ; `ScnBake.cs` + `edenpack bake` ; bake DISPLAY **1156/1156**, ~2,5 Mo/proto S3TC |
| 4 Tables ITB1 | ✅ | `TableBin` + hook `.inib` dans `IniTable` ; 589 tables/300 441 rows équivalents sur corpus réel |
| 5 Crypto/distrib | ✅ impl. | AES-GCM/chunk + ECDSA + `cryptoRev` + diff/apply/install/current.json **signé** (rejet runtime prouvé) + tombstones (`0xFF`) ; tamper 4/4 ; **release-build refuse packs/manifest non signés** ; clés dev-release générées (privée hors dépôt) |
| 6 Docs | ✅ | `TECH-STACK.md` + `DISTRIBUTION.md` (procédure packagé) + règles AGENTS/CLAUDE |

**Validation corpus réel (2026-10-29/30)** : core 8 352 entrées/330 Mio,
assets 44 404 entrées/**4,05 Go** (verify 0 échec), tex 32 563 webp/
**1,35 Go** (verify 0 échec) — **login + map complète pack-seul**
(`EDEN_ALLOW_LOOSE=0`) prouvés en captures, dont replay post-manifest
login→char→S009. Pixel-diff loose-vs-pack : scène 3D identique
(diff = langue HUD cfg + timing animations). Tests : 458/458.

Restes connus : paire de clés = dev-release (régénérer en env dédié
pour la vraie distribution, privée jamais dans le dépôt) ; chemin
`ReleaseBuild` (`!OS.IsDebugBuild()`) vérifié par revue — non
exerçable en binaire dev ; `EDEN_PACKS` exige des chemins absolus.

## Objectifs chiffrés

| Cible | Actuel | Visé |
|---|---|---|
| Poids install | ~27 Go assets + ~1,3 Go gamedata | **8-12 Go** (borne 5-15) |
| Chargement map S001 (live) | ~14,5 s | **~4-6 s** |
| Protos GLB preload | ~7,5 s (séquentiel) | **~1-2 s** (parallèle) |
| Hoquets première ouverture | ini lazy 9-80k lignes | **0** (tables bakées) |
| Protection données | fichiers en clair | AES-256-GCM + clé 2 parts + index signé |

## Architecture cible

```
gamedata/
├── eden-core.epk        index signé — uidata, data/, map meta, config
├── eden-assets.epk      textures/models/anims (ou shards par zone)
└── (dev : racines loose — fallback VFS, rien ne change)

tools/pack/edenpack      build | diff | apply | list | verify | extract
                        | bake (GLB→.scn via godot --headless)
                        | bake-tables (.ini→binaire indexé)
```

`EdenVfs` (~300-400 lignes C#) = point d'entrée unique branché dans
`AssetCatalog`/`DataPaths` : `OpenRead`, `ReadAllBytes`, `Exists`,
`ListDir`, `Stat`. Fallback loose → **le flux dev quotidien ne change
pas** : on ne compile que pour packager/tester un export.

---

## Phase 0 — EdenVfs + EPK v1 en clair (socle)

**But** : prouver la lecture packagée end-to-end avant toute crypto.

- `scripts/vfs/EdenVfs.cs` : backends `LooseDir` (actuel) + `EpkFile`.
  Priorité : override loose d'abord (hotfix sans rebuild), puis pack.
- Format v1 : `EPK1` header | index JSON/tables {path-lower → off,
  zsize, size, flags} | blobs zstd (`ZstdSharp.Port`, managé).
- Reroutage des ~8 API : `ResolveFile` (hash lookup O(1) — **meilleur
  que le `_dirCache` actuel**), `GetTexture` → `LoadPng/WebpFromBuffer`,
  `TryGetGlb`+`LoadGlbNode` → `GltfDocument.AppendFromBuffer`,
  `File.*`/`XDocument` → VFS, `Directory.Enumerate*` → `ListDir`.
- `edenpack build` minimal : scan racine → zstd → index.
- **Validation** : boot + S001 identiques (flameshot diff RMSE < 0,5 %),
  `--perf` comparé, NetTests vert.

Livrable : un export qui tourne 100 % sur pack. Effort : moyen (c'est le
chantier de plomberie principal).

## Phase 1 — Chiffrement + signature (spec étape 1 / activation étape 5)

> ⚠️ Cette phase décrit le **design cryptographique** figé à l'étape 1
> (le format ne peut plus changer après) — son **implémentation n'est
> activée qu'à l'étape 5**. Ne pas coder l'AES en P1.

- AES-256-GCM par chunk de 4 Mo (`AesGcm` net8 natif — AES-NI desktop,
  crypto ARM). Index chiffré + signé **ECDSA P-256** (`ECDsa` natif
  net8 — Ed25519 exigerait .NET 9+ ou NSec), compteur de version
  anti-rollback côté handshake.
- Clés : `Kdata` aléatoire/pack, `A` obfusquée embarquée, `B`
  délivrée au login par le serveur (champ `VersionHello`/réponse
  login — le handshake existe déjà). **Wrapping — construction
  unique** :
  `Kwrap = HKDF-SHA256(IKM=A‖B, salt=packId, info="eden-epk-wrap:v1")`
  puis `Kdata_wrapped = AES-256-GCM(Kwrap, Kdata)` dans le header
  (**nonce 12 o + tag 16 o** stockés à côté du ciphertext) —
  `Kdata` ne transite jamais en clair ni n'est dérivable de A seul.
  (Pas de `Kmaster` intermédiaire : une seule dérivation.) `B`
  circule uniquement **à l'intérieur de la session TLS 1.3 déjà
  authentifiée** (transport `modern` existant) — pas de canal
  supplémentaire à sécuriser.
- `edenpack` gère `--keyfile` (hors dépôt, jamais commité).
- **Limite assumée** : pas d'incassable absolu côté client — objectif
  = rendre l'extraction « hors client » impossible et l'extraction
  « via client » coûteuse.

Livrable : pack illisible sans boot légitime. Effort : faible-moyen.

## Phase 2 — Allègement corpus (le gros du −17 Go)

Build-time uniquement, zéro code runtime :

| Étape | Gain | Risque |
|---|---|---|
| Ship `.slim.glb` seuls (drop fat + `.kfm.json` + `Dark/*.png` + backups) | −12,5 Go | Vérifier couverture slim 1:1 (script `find`) |
| Textures → GPU-compressé au build (DDS/BC7 parseur maison — voir r1.1 ; repli WebP q85 natif si bloquant) | ~−6 Go | Visuel : diff captures ; alpha conservé ; normales/UI jamais lossy |
| zstd-19 dans le pack (GLB/ini/xml) | −1,5-2 Go | CPU decode ~1-2 Go/s, négligeable |

Total estimé : **~10-11 Go**. Sous 8 Go : quantization anims
(`trim_anim_glb` v2 → float16/sous-échantillonnage) ou shards
`epk-map-*` + download à la demande.

## Phase 3 — Bake GLB → `.scn` (le levier « parse GLB »)

**Constat** : ~7,5 s de preload sur S001 = parse `GltfDocument`
séquentiel, et le parallélisme GLB est **interdit** (deadlock ObjectDB,
P0.1 documenté). **Conditionnée au gate PoC de r2.A** — le chargement
parallèle de `.scn` depuis l'EPK n'est pas acquis (ResourceLoader ne
lit pas les archives custom ; voies A/C/D à trancher par mesure).

- `edenpack bake` : `godot --headless` rejoue `LoadGlbNode` hors
  runtime (parse GLB + `ToonMaterializer` + culling `PerfCulling` +
  extraction collision helpers + mips) puis `ResourceSaver.Save` en
  `.scn` binaire par proto. ⚠️ `AssetCatalog.IsHeadless` coupe
  `MipmapsEnabled` en headless → le bake doit forcer les mips
  (`EDEN_MIPMAPS=1`) ou tourner sous Xvfb, sinon les `.scn` sortent
  sans la chaîne de mips attendue en runtime.
- Runtime : si voie A validée → extraction vers `user://cache/` +
  `ResourceLoader.LoadThreadedRequest` (thread-safe par design → le
  deadlock n'existe plus). Sinon repli voie C (GLB en mémoire, parse
  inchangé).
- Gain estimé (**à mesurer**, non garanti) : proto load plus rapide +
  parallèle ×N cœurs → cible ~1-2 s sur S001. Cache `_glbCache`
  inchangé (protos `.scn` au lieu de scènes GLB).
- Risques : dérive visuelle (matériaux toon re-bakés différemment) →
  garde-fou = captures RMSE sur 5 maps témoins ; dépendance version
  moteur (`.scn` binaire lié à 4.7.x → rebuild au bump Godot) ;
  anims `.slim.glb` restent en GLB (parseur `GlbAnimFile` maison déjà
  sûr en parallèle — y toucher = risque inutile).

Effort : le plus élevé du plan (le bake rejoue la chaîne de
post-traitement) — à faire APRÈS phase 2, quand le pack v1 est stable.

## Phase 4 — No-lag : tables bakées + prefetch

- `edenpack bake-tables` : les `.ini` chauds (c_item, c_spell,
  t_dialogue, motion, npclist, monster, scene Sxxx…) → format binaire
  indexé (header + hash → offset). Runtime : `IniTable` lit la forme
  binaire si présente, repli ini. Tue les hoquets première-ouverture.
- Index `ListDir` → supprime les scans récursifs (`data/scene`,
  manifest `data/db`, imagesets).
- Option : hint prefetch par map (chunk `Sxxx` groupé séquentiellement
  dans le pack → lecture séquentielle au chargement zone).
- Textures pré-mippées au build : la chaîne de mips est stockée dans
  le blob (DDS la porte nativement) — `GenerateMipmaps` CPU par
  texture disparaît. Codec = celui choisi en r1/r2 (GPU-compressé
  prioritaire, WebP en repli).

## Phase 5 — Tooling release

`edenpack diff old.epk new.epk` → `patch.epk` (blobs changés + index).
`edenpack apply` fusionne sans re-télécharger (chunking content-defined
optionnel si les patchs gonflent). Vérifications à **3 niveaux** :
release = signature + index + **tous les tags GCM de tous les blocs**
(`edenpack verify` — l'échantillon n'est qu'un mode debug) ;
téléchargement = digest du pack vs manifest ; **boot = signatures
seules** (pointeur + index — ms, pas de décryptage de Go) ; **lecture
= tag GCM par bloc au decrypt** (gratuit, intégrité à la demande).
Ajout index : empreinte **blake3 par entrée** dans l'index signé —
lie les blobs à l'index (indispensable pour `bootstrap.epk` clair,
dont les blobs n'ont pas de tag GCM ; utile aussi au `verify` hors
clé sur tous les packs).

## Phase 6 — Documentation projet (livrables obligatoires)

Deux documents à produire une fois le format EPK stabilisé (étape 1+
de la révision r2) — non bloquants pour le code, requis avant de
considérer le chantier « documenté » :

1. **Doc stack technique** — `docs/packaging/TECH-STACK.md` (créer) +
   compléter `DISTRIBUTION.md` (déjà le layout package). Contenu :
   architecture client (Godot 4.7.2 mono, net8.0, VFS), format EPK
   (index signé, blocs zstd+GCM indépendants, wrapping Kdata via
   `Kwrap` HKDF+GCM), pourquoi ce
   choix (vs pck natif cassable, vs fichiers lâches), pipeline de
   build (`edenpack`, slim, bake), protocole de mise à jour. But :
   qu'un lecteur externe comprenne le système packagé sans lire le
   code.
2. **`AGENTS.md` + `CLAUDE.md`** — ajouter la règle : **dev/debug sur
   le client packagé dès que possible** (le pack = ce que les joueurs
   exécutent réellement — loose reste le repli pour itérer un fichier
   sans rebuild). Warning à inclure : pack chiffré ⇒ part B servie
   par le serveur au login ⇒ **serveur local requis** ; flag
   `EDEN_ALLOW_LOOSE` en debug ; packs non chiffrés jusqu'à l'étape 5.

---

## Protocole dev complet

### Cycle quotidien (inchangé)

Dev → fichiers lâches (`raw_assets/` + `assets/`), VFS en mode loose,
run/shot/NetTests comme aujourd'hui. **On ne compile jamais pour
développer.**

### Cycle contenu (quand on touche les data)

```
1. Modification (table ini, texture, anim, map…)
2. tools/pack/edenpack build gamedata/ dist/eden.epk --keyfile ~/.eden/keys
      → re-encode uniquement si source changée (manifest hash par entrée)
3. Test export : EDEN_DATA_DIR=dist ./EdenReborn.x86_64   (pack forcé)
4. diff vs pack précédent → patch-<ver>.epk à publier
```

### Versioning / handshake

- `index.version` (compteur signé) + `manifest.json` (par entrée :
  path, taille, blake3) — alimente le `ManifestHash`/VersionHello
  existant : le serveur peut refuser un client au pack obsolète.
- Bump moteur Godot → `bake` complet obligatoire (`.scn` versionné).

### Gestion des clés

- `Kdata`/`part_A` jamais dans le dépôt ni dans `edenpack` en clair :
  `--keyfile` (chmod 600, hors git) ou `--env EDEN_PACK_KEY`.
- **Clés de signature ECDSA** : privée hors dépôt dans
  l'environnement de release dédié (jamais sur un poste dev) ;
  publique de vérification embarquée dans le client (hardcodée).
  Rotation : `keyid` dans l'index signé → le client accepte un set
  de publiques {courante, next} → révocation = retirer la publique
  compromise du build client + refus serveur des index signés par
  elle.
- Rotation = nouvelle `part_B` côté serveur par release ; anciennes
  versions refusées par le handshake si on veut forcer l'update.
- Compromission supposée : re-pack avec nouvelle `Kdata` + nouvelle
  `part_A` (rebuild client) — procédure documentée, pas de panique.

### Rollback — retour à l'état d'avant (worst case)

Principe : **rien d'irréversible n'entre en prod sans sa porte de
sortie**. Trois niveaux, du plus léger au plus lourd.

**Niveau 1 — kill-switch code (coût quasi nul).** Chaque fonction est
derrière un flag :
- `EDEN_VFS=0` / build sans pack → le client retombe 100 % sur le
  chemin loose actuel (les ~8 API délèguent, l'ancien comportement
  reste compilé).
- Crypto off tant que l'étape 5 n'est pas passée ; bake off → voie C
  (GLB mémoire) ; tables binaires off → repli `.ini` (l'index ne
  remplace jamais le parseur, il le précède — repli toujours présent).
- Abandon total du chantier = supprimer `scripts/vfs/` + réverter les
  ~8 points d'injection : surface limitée, `assets/`/`raw_assets/`
  intacts. C'est le prix de l'architecture à choke points.

**Niveau 2 — rollback données/release.**
- `edenpack apply` n'écrit **jamais** en place : nouveau fichier +
  rename atomique (+ fsync dir) — un pack semi-appliqué n'existe pas.
- **Installation atomique multi-packs** : `eden-core`, `eden-assets`,
  `bootstrap` doivent basculer **ensemble** — pas de rename unique
  possible sur N fichiers. Pattern : `gamedata/current.json` pointeur
  signé listant {nom → fichier, version, digest}. `apply` écrit tous
  les nouveaux `.epk` + le nouveau `current.json`, vérifie, puis
  rename atomique du **pointeur seul** — une update interrompue
  laisse l'ancien pointeur intact (le client lit toujours le set
  cohérent référencé). Anciens `.epk` purgés au prochain boot réussi.
- Rétention N-1 : le launcher/dist garde `eden-assets-<prev>.epk` ;
  rollback = republier l'ancien index — pas de rebuild.
- Le manifest signé porte `previous_version` → `edenpack` peut
  reconstruire l'état N-1 bit-à-bit (build déterministe).
- **Ne jamais supprimer le corpus source** : fat GLB retirés du
  package, pas de `assets/` — tout est re-dérivable (slim 1:1
  bloquant avant toute exclusion).
- Fenêtre de transition clés : pendant un rollout, `part_B` ancienne
  + nouvelle restent acceptées côté serveur (durée bornée) → un
  rollback pack n'exige pas de rollback clé.

**Niveau 3 — rollback joueur (cas réel prod).**
- Pack corrompu au boot → `edenpack verify` côté client → échec
  détecté AVANT usage (tag GCM/signature/digest manifest) → message
  clair + re-téléchargement du manifest; pas de crash silencieux.
- Anti-rollback ≠ anti-downgrade-opérateur : le compteur de version
  bloque les *clients* sur un vieux pack quand le serveur l'exige ;
  l'opérateur qui rollback **baisse aussi** le `min_version` côté
  serveur (les deux se configurent — documenter la paire).
- `.scn` versionnés moteur : bump Godot ⇒ vieux `.scn` invalides →
  le pack N-1 correspondant reste servi jusqu'à re-bake (les deux
  versions du pack coexistent par `packVersion`).

**Ce que le rollback ne couvre pas** : clé `part_A` extraite et
publiée (rotation = re-pack + rebuild client, procédure §Clés) ;
joueurs ayant déjà migré leur cache `user://` (régénéré au boot,
sans état précieux).

Écrit dans la spec v1 : chaque feature a son flag OFF et chaque
release son prédécesseur restaurable — le pire cas = repointer le
serveur + flag off, jamais une migration de données.

---

## Gains concrets récapitulés

| Poste | Avant | Après | Source du gain |
|---|---|---|---|
| Poids | ~28 Go | **~10 Go** (5-7 avec shards) | slim-only, WebP, zstd, morts |
| Preload protos | 7,5 s séquentiel | ~1-2 s parallèle | `.scn` baké + `LoadThreadedRequest` |
| Textures au chargement | decode PNG + mipgen + S3TC CPU | decode WebP (+mips pack) | moins de CPU/texture, VRAM idem ou mieux |
| Résolution chemins | scans `_dirCache` | hash O(1) | index |
| Lazy ini | parse 9 Mo/80k lignes au 1er usage | lookup binaire | bake-tables |
| Sécurité | clair | AES-256-GCM + clé 2 parts + signé | crypto pack |
| Update joueur | 27 Go de fichiers | patch.epk incrémental | diff |

## Ordre d'exécution recommandé

Ordre canonique = révision r2 §F (les phases P0-P6 ci-dessus restent
la description du contenu, pas l'ordonnancement) :

```
Étape 0  audit + mesures de référence (corpus témoin figé)
Étape 1  EPK clair + VFS + spec crypto arrêtée (format figé complet)
Étape 2  allègement corpus (slim 1:1 bloquant, textures pré-compressées)
Étape 3  bake tables ini→binaire + tests d'équivalence
Étape 4  PoC .scn (voie A) → verdict → bake ou repli voie C
Étape 5  chiffrement actif + signature + diff/apply + protocole release
Étape 6  documentation (TECH-STACK + règle agents « tester le packagé »)
```

L'étape 2 peut démarrer en parallèle de l'étape 1 (zéro code runtime,
pur build-side).

## Risques et inconnues

- **`.scn` bake** : fidélité des post-traitements (toon/culling/
  collisions) — mitigation captures RMSE ; sinon garder GLB+zstd
  (le pack marche sans).
- **WebP** : perte visuelle sur quelques textures critiques
  (normales/UI 1-bit) → liste d'exceptions en PNG brut.
- **zstd sous `AppendFromBuffer`** : buffer en mémoire par GLB —
  pics RAM transitoires (identiques à l'actuel).
- **Couverture slim 1:1** à vérifier avant suppression des fat.
- **Android** (si plus tard) : tout ce design est compatible —
  `user://` extraction ou lecture directe du pack ; WebP/ASTC selon
  cible (WebP marche partout).

## Hors périmètre du plan

Streaming monde ouvert, re-slicing gameplay par zone, refonte du
réseau, audio, outils `tools/` (nif2gltf reste le pipeline amont —
`edenpack` consomme sa sortie).

---
Non validé chiffré : gains `.scn` réels (à mesurer P3 sur S001 —
estimation ×3-5 parse + parallélisme), ratio zstd exact sur slim GLB,
RAM pic du decode parallèle. Risque principal : la dérive visuelle
du bake `.scn` — le seul point du plan où le rendu peut changer ;
d'où sa place en dernier, après un socle prouvé en captures.

---

## Révision r1 — revue externe intégrée (2026-10-29)

Points retenus d'une revue tierce, corrigés au vu du code :

1. **Textures : GPU-compressé au build > WebP.** Correction de
   premisse : la VRAM n'explose PAS sous WebP — `CompressImage`
   S3TC s'applique déjà à chaque albedo (PERF.md). Le vrai gain du
   pré-compressé est le **CPU** : fini decode+mipgen+compress par
   texture. Choix : DDS/BC7 parsé maison (~150 lignes,
   `Image.CreateFromData` formats `Dxt*`, mips embarquées) — le
   `.ctex` natif passe par `ResourceLoader`, peu compatible VFS.
   WebP reste le plan B universel (Android : ASTC via même parseur).
   Normales/UI : jamais lossy (déjà exclus du S3TC runtime).
2. **LOH/GC** : buffers chunks via `ArrayPool<byte>.Shared.Rent()` —
   zéro alloc par lecture. La LOH est déjà pressurisée aujourd'hui
   (`FsmFile` 15-20 Mo `ReadAllBytes`, buffers méga-modèles) : la
   VFS peut aussi streamer le fsm par fenêtres.
3. **AES-GCM random-access** : nonce = `hash(fichier) ⊕ index_chunk`
   (dérivé, jamais stocké, jamais réutilisé) ; tag 16 o **inline**
   par chunk (0,4 % overhead) plutôt que dans l'index (~90 Mo
   évités pour ~80k fichiers).
4. **I/O parallèle** : `System.IO.RandomAccess.Read(handle, buf, off)`
   — reads positionnels thread-safe sur UN FileStream unique
   (pas de multi-handles ni seek-lock). **Layout du pack = exigence
   v1** : blobs ordonnés (namespace, préfixe map, path) → chargement
   de zone = lecture quasi séquentielle + prefetch d'un span
   contigu d'un coup.

Ajouts propres au passage de la revue :

5. **Manifest baké** : `ManifestHash` re-scanne `data/db/**/*.ini` +
   hash SHA-256 à la connexion monde — le pack embarque ce digest
   précalculé → scan récursif + hash supprimés du handshake.
6. **Build déterministe** : index trié + nonces dérivés du contenu →
   même entrée = même sortie bit-à-bit (releases vérifiables,
   `edenpack verify` compare les digests).
7. **Flag codec dans l'index v1** : champ `tex_codec` par blob
   (bc7/astc/webp/raw) — l'ajouter plus tard casserait le format ;
   nécessaire pour la variante Android.
8. **Sharding `epk-map-Sxxx`** confirmé comme seule voie sous 8 Go —
   reste une option P2, pas un requis v1.

---

## Révision r2 — revue externe n°2 intégrée (2026-10-29)

Corrections structurelles — trois points changent l'ordre ou le design.

### A. Phase 3 requalifiée : gate de PoC (blocage `.scn`/EPK)

`ResourceLoader.LoadThreadedRequest` ne lit que des chemins moteur —
il ne peut PAS lire dans une archive custom, et un
`ResourceFormatLoader` custom n'y change rien (dispatch exige un
chemin existant). Le chargement parallèle de `.scn` depuis l'EPK
n'est donc PAS acquis : hypothèse à prouver AVANT tout bake.

Trois voies candidates, à trancher par mesure :

| Voie | Perf | Protection | Effort |
|---|---|---|---|
| A. `.scn` extraits déchiffrés → `user://cache/` + `LoadThreadedRequest` | pleine (natif parallèle) | assets clairs en cache — ≈ exposition d'un dump RAM : à assumer | faible |
| B. `.pck` secondaire monté (`LoadResourcePack`) contenant les `.scn`/`.ctex` | pleine | pck extrait clair — hors AES | faible-moyen |
| C. GLB dans l'EPK + `AppendFromBuffer` en mémoire (statu quo parse) | nulle sur le parse (séquentiel, deadlock P0.1 reste) ; gains pack I/O seuls | maximale (rien sur disque) | nul |
| D. `ResourceFormatLoader` custom à chemins virtuels (`epk://`) | théoriquement pleine | maximale (mémoire) | incertain — voir bloqueur |

Voie D : l'API expose `_exists`/`_recognize_path`/`_load` — le routing
de chemins virtuels est possible ; le **bloqueur réel** est la
conversion bytes→`PackedScene` (le loader `.scn` natif n'accepte que
des chemins, aucune API publique « parse depuis buffer »). PoC
court : si `_load` peut déléguer au loader natif sur un `user://`
staging, on retombe sur la voie A — sinon D est morte.

Gate : PoC voie A sur 5 protos → mesure les 4 métriques (I/O+decode /
reconstruction / instanciation / total visible). Si gain ≥ ×2, la
compromission « cache clair dérivé » est documentée et acceptée ;
sinon repli voie C. Le parse GLB séquentiel reste le poste dominant
— ne pas investir dans le bake avant ce verdict.

**Règle transverse** : quelle que soit la voie retenue, aucun secret
serveur ni donnée dont la confidentialité est requise ne va dans un
pack client — le chiffrement protège la distribution, pas la RAM.

Bonus si voie A/B retenue : le bake peut **embarquer les textures
pré-compressées dans le `.scn`** → le poste decode textures disparaît
aussi pour les protos bakés.

### B. Format de bloc — indépendance complète

Chaque chunk de 4 Mo (taille **décompressée**) est compressé en frame
zstd propre ET chiffré GCM propre — sinon le random-access casse
(déchiffrer un bloc ≠ pouvoir le décompresser). Métadonnées par
bloc : offset, zsize, usize, codec, fileId, chunkIdx (le tag 16 o
reste inline). Contrôles : bornes d'offset/taille + allocation
plafonnée par `usize` annoncé — un index corrompu ne peut provoquer
d'allocations abusives. Coût : ~5-15 % de ratio zstd perdu vs flux
continu (compenser éventuellement par chunks 8-16 Mo ou dictionnaire
zstd par fichier).

### C. Cryptographie — clé par fichier, nonce = compteur

Remplace la dérivation `hash ⊕ idx` : **`Kfile = HKDF(Kdata, fileId
‖ cryptoRev)`** (cryptoRev défini ci-dessous) → nonce = `chunkIdx`
sur 96 bits, unique **par construction** (clé distincte par fichier,
compteur monotone par bloc). AAD = fileId ‖ cryptoRev ‖ chunkIdx ‖
usize. Pas de check de collision nécessaire.

**Anti-réutilisation inter-releases — `cryptoRev` par entrée** :
`fileId` est stable entre versions → deux builds partageant `Kdata`
réutiliseraient `Kfile` + nonce sur des contenus différents (faute
GCM). Mais binder `packVersion` rechiffrerait *tous* les fichiers à
chaque release → patchs incrémentaux ≈ pack complet. Construction
retenue : chaque entrée de l'index porte un `cryptoRev` (compteur,
bumpé **uniquement** quand le contenu change) et

```
Kfile = HKDF(Kdata, fileId ‖ cryptoRev)
```

- Fichier inchangé entre N et N+1 → même `cryptoRev` → même `Kfile`
  → **blocs chiffrés byte-identiques** → le diff ne transporte que
  les fichiers réellement modifiés.
- Fichier modifié → `cryptoRev+1` → nouvelle `Kfile` → espace de
  nonces neuf → pas de réutilisation.
- **`keyEpoch`** : `Kdata` stable entre les releases ordinaires.
  `--new-key` = rotation réelle (compromission, politique) →
  rechiffrement complet assumé, documenté comme tel.
- Rotation de `part_B` par release reste **gratuite** : elle ne
  change que `Kwrap` → seul le `Kdata_wrapped` du header est
  re-émis, pas les blocs. (B change sans rechiffrer les données.)

**Invariant obligatoire dans la spec** : jamais deux contenus
différents sous la même combinaison `(Kdata, fileId, cryptoRev)` —
toute modification de fichier exige `cryptoRev+1`. `edenpack build`
refuse de publier un index dont un `fileId` pointe un contenu
différent à `cryptoRev` égal (vérifié contre la release précédente).

Déterminisme vs clé aléatoire : incompatibles — deux modes explicites.
`edenpack build --keyfile figée` → sortie bit-à-bit reproductible ;
`--new-key` → nouvelle `Kdata` (sortie différente, attendue).

### D. Bootstrap et intégrité loose

- **Problème bootstrap** : la part B n'arrive qu'au login, or uidata/
  fonts/login_bg/temple sont dans le pack chiffré → l'écran de login
  ne pourrait pas démarrer. Solution : `bootstrap.epk` **en clair
  mais signé** (~15 Mo — sous-ensemble uidata + login_bg + GLB
  temple) : non chiffré ≠ non authentifié — l'index porte la même
  signature ECDSA vérifiée avant usage, sinon un `bootstrap.epk`
  déposé par un tiers pourrait falsifier l'écran de login (récolte
  d'identifiants — c'est le contenu le plus sensible du pack).
- **Loose override** : priorité loose = trou d'intégrité en prod.
  `EDEN_ALLOW_LOOSE` honoré uniquement en build debug ; release =
  pack seul + overlays **signés** (hotfix = `patch.epk` signé, pas
  de fichier lâche).
- **Anti-rollback** : le refus serveur couvre le online ; hors ligne
  un vieux pack reste lisible — garantie bornée au handshake, le dire.

### E. Corrections de détail

- **Codec enum** = formats Godot réels : `s3tc`(BC1-3), `bptc`(BC7),
  `etc2`, `astc`, `webp`, `raw` — BC7 n'est PAS un `Dxt*`.
- **Budget poids** : les estimations ne se somment pas (gains
  chevauchants) → `edenpack build` produit un **rapport par
  catégorie** (source / converti / packagé) qui devient la mesure
  officielle ; la table §6 reste une borne, pas une somme.
- **Vérification slim 1:1 bloquante** dans le build prod (pas un
  contrôle d'appoint).

### F. Ordre révisé

```
Étape 0  audit + mesures de référence (corpus témoin figé)
Étape 1  EPK clair + VFS + spec crypto arrêtée (format figé AVEC
         bloc/codec/index — implémentation crypto plus tard)
Étape 2  allègement corpus (slim 1:1 bloquant, textures pré-compressées)
Étape 3  bake tables ini→binaire + tests d'équivalence
Étape 4  PoC .scn (voie A) → verdict → bake ou repli voie C
Étape 5  chiffrement actif + signature + diff/apply + protocole release
```

La spec crypto est figée à l'étape 1 (le format ne peut pas changer
après) ; son activation peut attendre l'étape 5 — pas en prod avant.

### G. Critères de validation étendus

- **Visuel** : RMSE < 0,5 % insuffisant → + tests ciblés transparence,
  animations, collisions, matériaux sur zones critiques.
- **Perf** : médianes ET percentiles, à froid ET à chaud, ventilation
  I/O / décompression / parse / prépa rendu.
- **Intégrité** : altérer volontairement tag GCM, octet d'index,
  blob, signature — chaque cas doit être détecté avant usage.
- **Reproductibilité** : deux builds mêmes entrées+clé = sortie
  identique ; nouvelle clé = sortie différente.
- **Patch** : mesurer taille ET coût d'application (temps, disque
  temporaire, rollback).
- **Compat** : version Godot exacte + matrice renderer×plateforme
  (ASTC mobile vs BC7 desktop).
