# EPK v1 — format de pack Eden (spec figée)

Contrat unique entre `tools/edenpack` (écriture) et
`godot_project/scripts/vfs/` (lecture). Toute évolution = nouveau
`formatVersion`, jamais une modification en place.

But : remplacer ~80 000 fichiers lâches par des archives indexées,
compressées par bloc, signables et chiffrables — sans changer le code
métier au-delà des ~8 points d'accès fichier.

## Layout fichier

```
[Header 80o][crypto 60o si ENCRYPTED][sig 68o si SIGNED][blobs...][index]
```

### Header (80 octets, little-endian)

| Off | Champ | Type | Note |
|---|---|---|---|
| 0 | magic | u32 | `"EPK1"` = 0x314B5045 |
| 4 | formatVersion | u32 | 1 |
| 8 | flags | u32 | bit0 ENCRYPTED, bit1 SIGNED, bit2 INDEX_ZSTD |
| 12 | chunkShift | u32 | taille chunk = `1<<chunkShift` (22 = 4 Mio) |
| 16 | packVersion | u64 | compteur monotone (anti-rollback handshake) |
| 24 | packId | u8[16] | GUID par pack — salt HKDF de `Kwrap` |
| 40 | indexOffset | u64 | |
| 48 | indexCSize | u64 | taille index sur disque (compressé si INDEX_ZSTD) |
| 56 | indexUSize | u64 | taille index décompressé |
| 64 | entryCount | u32 | |
| 68 | minClientVersion | u32 | réservé (0) |
| 72 | reserved | u8[8] | zéros |

### Bloc crypto (si ENCRYPTED) — 60 o après le header

| Champ | Taille | Note |
|---|---|---|
| kdataWrapped | 32 | `AES-256-GCM(Kwrap, Kdata)` ciphertext |
| wrapNonce | 12 | nonce GCM du wrapping |
| wrapTag | 16 | tag GCM du wrapping |

`Kwrap = HKDF-SHA256(IKM=A‖B, salt=packId, info="eden-epk-wrap:v1")`.
A = part obfusquée embarquée client ; B = part servie par le serveur
dans la session TLS authentifiée. Construction unique — pas de
`Kmaster` intermédiaire.

### Bloc signature (si SIGNED) — 68 o

`u32 keyId` + `u8[64]` signature ECDSA P-256 (r‖s brut) sur
**header + index tels qu'écrits** (couvre le crypto bloc si présent).

## Index (binaire ; un frame zstd si INDEX_ZSTD)

```
u32 stringTableSize
u8[stringTableSize]   chemins UTF-8 minuscules, '/'-séparés,
                      NUL-terminés, dans l'ordre des entrées (triées)
entryCount × 88o      enregistrements fixes
```

### Entrée index (88 o)

| Off | Champ | Type | Note |
|---|---|---|---|
| 0 | pathOff | u32 | offset dans stringTable |
| 4 | flags | u32 | réservé (0) |
| 8 | fileId | u64 | FNV-1a-64 du chemin normalisé — stable entre releases |
| 16 | blobOffset | u64 | offset absolu du flux de chunks |
| 24 | blobSize | u64 | octets stockés (headers chunks + payloads + tags) |
| 32 | uSize | u64 | taille en clair |
| 40 | cryptoRev | u32 | bumpé uniquement quand le contenu change |
| 44 | codec | u8 | 0=raw, 1=zstd, **0xFF=tombstone** (entrée de suppression — voir § Tombstones) |
| 45 | texCodec | u8 | 0 raw, 1 s3tc, 2 bptc, 3 etc2, 4 astc, 5 webp |
| 46 | reserved | u16 | 0 |
| 48 | sha256 | u8[32] | empreinte du **contenu en clair** — lie les blobs à l'index signé (indispensable pour les packs non chiffrés type bootstrap) |
| 80 | reserved | u8[8] | zéros |

## Flux de chunks d'un blob

`nChunks = ceil(uSize / chunkSize)` ; chaque chunk :

```
u32 cSize   octets stockés du payload (hors ce header)
u32 uSize   octets en clair du chunk (≤ chunkSize)
u8[cSize]   payload : frame zstd indépendante (codec=1) ou octets
            bruts (codec=0), + u8[16] tag GCM en fin si ENCRYPTED
```

Chaque chunk compresse ET chiffre **indépendamment** — le
random-access reste possible sans déchiffrer le fichier entier.
Lecture séquentielle = parcours des headers ; allocation bornée par
`uSize` annoncé (un index corrompu ne peut pas provoquer d'alloc
géante). Contenu vide = 0 chunk, blobSize=0.

## Tombstones (codec = 0xFF)

Une entrée **tombstone** marque un fichier supprimé par un patch :
`codec=0xFF`, `blobOffset`/`blobSize`/`uSize` = 0, aucun payload —
`fileId`, `path`, `cryptoRev`, `sha256` conservés pour identification.

- `edenpack diff` émet une tombstone par chemin présent dans `old`
  et absent de `new`.
- `edenpack apply` : une tombstone du patch **retire** l'entrée de la
  base fusionnée (le pack résultant ne porte pas de tombstone).
- VFS/overlay : le dernier pack qui contient le chemin décide — une
  tombstone **ombre définitivement** l'entrée des packs inférieurs
  (`Exists`/`ReadAllBytes`/`OpenRead`/`ListDir` → absent).
- `verify` : aucune donnée à contrôler — la tombstone est couverte
  par l'index signé comme toute entrée.

## Cryptographie des chunks (ENCRYPTED)

```
Kfile = HKDF-SHA256(Kdata, fileId ‖ cryptoRev)
nonce 96 bits = chunkIdx (compteur — unique par construction)
AAD   = fileId ‖ cryptoRev ‖ chunkIdx ‖ uSize   (little-endian)
```

- Fichier inchangé entre releases → même `cryptoRev` → blocs
  byte-identiques → les patchs ne transportent que le diff réel.
- Fichier modifié → `cryptoRev+1` → nouvelle `Kfile` → espace de
  nonces neuf.
- `Kdata` stable entre releases ordinaires (`keyEpoch`) ; rotation
  (`--new-key`) = rechiffrement complet assumé.
- **Invariant** : jamais deux contenus sous `(Kdata, fileId,
  cryptoRev)` — `edenpack build` le vérifie contre la release
  précédente.

## Vérifications (3 niveaux)

- **Release** : signature + index + tous les tags GCM de tous les
  blocs + sha256 de chaque contenu (`edenpack verify`).
- **Boot client** : signatures du pointeur `current.json` + index —
  millisecondes, pas de décryptage de Go.
- **Lecture** : tag GCM vérifié par chunk au decrypt (intégrité à la
  demande) ; sha256 contrôlé en mode verify/diag.

## Builds déterministes

Index trié par chemin normalisé, offsets séquentiels, zstd niveau
fixe, nonces dérivés → même entrée + même clé = même sortie bit-à-bit.
Deux modes : `--keyfile` figée (reproductible) vs `--new-key`
(volontairement différent). Les champs d'en-tête volatils (dates)
n'existent pas — rien à neutraliser.

## Espace de clés (chemins relatifs)

Racine du pack = la racine `gamedata/` de `tools/package_dist.sh` :
`uidata/`, `data/`, `map/`, `assets/`, `client.cfg`, `*.tsv`…
Normalisation : `/`-séparés, minuscules, sans préfixe.
`fileId` = FNV-1a-64 du chemin normalisé.

## Multi-packs

`eden-core.epk` (data/uidata/map meta), `eden-assets.epk`
(textures/models/anims), `bootstrap.epk` (clair signé : login UI) —
basculés ensemble via `current.json` pointeur signé
`{name → fichier, version, digest}` ; activation = rename atomique du
pointeur seul. Les packs montés forment un namespace fusionné :
un pack plus tardif écrase l'entrée d'un pack antérieur (overlays
signés = hotfix).
