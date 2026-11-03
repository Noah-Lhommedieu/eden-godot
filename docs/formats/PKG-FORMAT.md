# Format pkg EDENFR (`pkg/pkg.idx` + `pkgNNN.pkg`)

Statut : `verified` — 2026-11-02, client FR de `~/Téléchargements/EDENFR`
(448 paquets). Code : `src/Eden.Core/Pkg/`, extracteur : `tools/edenfr-extract`.

## Index `pkg.idx`

En-tête 292 octets, puis des records de 592 octets (45 075 entrées, **une
seule par chemin** : pas de patch par delta à rejouer, l'index désigne la
bonne version) :

| Offset | Taille | Champ |
|---|---|---|
| +4 | u32 | offset dans `pkgNNN.pkg` |
| +12 | u32 | taille compressée |
| +16 | u64 | **CRC32C du contenu décompressé** (= CRC du manifeste) |
| +24 | u64 | flags : `0x20` compressé, `0x40` brut |
| +56 | u32 | taille décompressée |
| +60 | char[260] | nom de fichier |
| +320 | char[260] | dossier |
| +584 | u32 | numéro de paquet |

Chemin = dossier + fichier, `\` → `/`, `UIData/` → `uidata/`.
`pkg.pkg` (261 octets) et `pkg.sp` ne contiennent pas d'entrées de données.

## Compression

Chaque entrée (flags `0x20`) est un flux autonome :

- **< 64 Ko** : zlib standard (`78 xx`) ;
- **≥ 64 Ko** : en-tête `88 1c` (fenêtre 64 Ko), puis deflate brut
  **dont les codes de distance 30 et 31 sont permis** (bases 32769 et
  49153, 14 bits extra). Les longueurs restent celles de zlib (le code 285
  vaut 258). **Ce n'est pas Deflate64** : 7z et `zipfile_deflate64`
  échouent sur ~400 entrées, d'où les fichiers « dégradés » de l'ancien
  extracteur.
- `zsize == size` ne veut pas dire « brut » (`effect/texture/S1762108.ifl` :
  28 octets compressés en 28). Seul le flag `0x40` signale du brut.

Implémentation de référence : `src/Eden.Core/Pkg/Inflate64.cs` (port de
`puff.c`), miroir C `tools/edenfr-verify/inflate64.c`.

## Résultat mesuré

Avec ce décodeur, **45 037 entrées sur 45 037 ont la bonne taille et le bon
CRC32C** (dont les ~1 900 NIF, les 37 KF de montures et les 19 layouts que
l'ancien corpus donnait dégradés). Rapport :
`docs/provenance/edenfr-extract-2026-11-02.tsv`.

## Hors paquet

Les 2 564 fichiers du manifeste absents de l'index (sons `sound/*.wav`,
musiques `bgm/*.ogg`, `Movies/`, exécutables et ini à la racine) viennent du
dossier d'installation. 1 125 ont le bon CRC. **1 439 (sons et bgm) ont la
bonne taille mais un CRC différent**, et aucune autre copie n'existe : ils
sont gardés tels que le client d'origine les lit (statut
`INSTALL-CRC-DIFFERENT`). Cela ne touche aucun format natif de rendu.

## Ce qui n'a pas été fait par désassemblage

L'ordre de montage des 448 paquets n'a pas à être rejoué : l'index fixe
paquet et offset de chaque chemin. Le montage dans `game.bin`
(`%s\%s%03d.pkg`) correspond à `pkg{N:D3}.pkg`. `delFileHistory.txt` ne
contient pas de suppression utile (`0 l`…).
