# ADR-0005 — Client packagé : EPK (format du portage), pas `.pkg` EDENFR

- Statut : **accepté** — 2026-11-02

## Options

| | `.pkg` EDENFR d'origine (448 paquets) | EPK v1 du portage (`eden_godot/tools/edenpack`, `scripts/vfs/`) |
|---|---|---|
| Protection | Aucune : `7z` l'ouvre (Deflate64), et `pkgfs_unpack` aussi | AES-256-GCM par bloc ; clé enveloppée, avec une partie B donnée par le serveur au login |
| Intégrité | CRC propriétaire dans `GameDataFileList.txt` (algorithme encore inconnu) | SHA-256 par entrée, index signé ECDSA P-256, pointeur `current.json` signé ; un build release refuse les paquets non signés |
| Patch | Paquets incrémentaux, format à rétro-ingénierer (phase A1) | `diff`/`apply`, suppressions par marqueurs (codec `0xFF`), `cryptoRev`, bascule atomique |
| Lecture aléatoire | À établir | Blocs zstd indépendants, `EpkReader` thread-safe |
| État | Lecteur à écrire (A2) | **Implémenté et testé** (étapes 0 à 6 de `PLAN-PACK-EPK.md` ✅, 13 tests crypto, falsification détectée 4/4) |
| Build reproductible | — | Oui, bit à bit (index trié, nonces dérivés) |

## Décision

1. **Distribution et client packagé : EPK.** Les EPK contiennent les
   **fichiers natifs EDENFR tels quels** (`.nif`, `.kf`, `.kfm`, `.dds`,
   `.ini`, `.fsm`, `.layout`…), avec le codec `zstd` ou `raw` et le
   texCodec `raw`. Aucune conversion à l'empaquetage : la règle « formats
   natifs seulement » vaut aussi dans le paquet.
2. **`.pkg` = source d'import en lecture seule.** `PkgFileSystem` (phase
   A2) ne sert qu'à extraire et vérifier le corpus EDENFR contre le
   manifeste. Le client ne lit jamais de `.pkg`.
3. **Traçabilité de bout en bout** : pour chaque entrée, le SHA-256 de
   l'EPK est lié au CRC du manifeste officiel. Un fichier empaqueté est
   donc prouvé EDENFR d'origine. L'index EPK porte l'empreinte, et le
   rapport de build le relie à `GameDataFileList.txt`.
4. **Optimisations dérivées du portage désactivées dans le mode natif** :
   - `bake-tex` webp ;
   - `.dds` BC7 recompressé ;
   - `ScnBake` `.scn` ;
   - réécriture des URI GLB ;
   - tables `.inib` (ITB1).
   
   Elles empaquetaient des formats convertis. On peut en réintroduire une
   par ADR si une mesure le justifie, à condition qu'elle reste une
   **cache** et que le fichier natif reste la source.

## Conséquences

- `edenpack build` doit accepter un arbre natif (`corpus/fr-full` vérifié
  + `gamedata/` du projet), avec un manifeste de paquets
  `eden-core.epk` (data, uidata, map), `eden-assets.epk` (char, monster,
  ride, item, effect, decorate, house) et `bootstrap.epk` (UI de login,
  en clair mais signé).
- `EdenVfs` lit déjà paquet ou fichiers libres, sans tenir compte de la
  casse (clés en minuscules). Les loaders natifs (`NifFile`, `KfFile`,
  `DdsInfo`…) passent par `EdenVfs.ReadAllBytes`/`OpenRead`, donc ils
  marchent tels quels sur un paquet.
- Tests de validation sur le chemin packagé (`EDEN_ALLOW_LOOSE=0`), comme
  l'impose `AGENTS.md`.
