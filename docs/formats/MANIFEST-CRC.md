# CRC du manifeste `GameDataFileList.txt`

Statut : `verified` — 2026-11-02.

Lignes `chemin:date:taille:CRC` (47 601 fichiers). Le CRC est un **CRC32C**
(Castagnoli, polynôme réfléchi `0x82F63B78`, init `0xFFFFFFFF`, xorout
`0xFFFFFFFF`) du contenu non compressé. Le champ `crc` de `pkg.idx` porte
la même valeur.

Retrouvé par essai des paramètres CRC32 courants sur `char/model/B1001.nif`
(manifeste `3722075921` = `0xDDDA5F11`), puis confirmé sur les 45 037
fichiers du paquet.

## Vecteurs de test

| Entrée | CRC32C |
|---|---|
| ASCII `123456789` | `0xE3069283` |
| `char/model/B1001.nif` (5 787 o) | `0xDDDA5F11` |
| `effect/texture/S1762108.ifl` (28 o) | `1012737455` |

Code : `ManifestCrc` (C#), `tools/edenfr-verify/crc32c.c` (SSE4.2).
Test : `tests/Eden.Core.Tests/PkgTests.cs`.
