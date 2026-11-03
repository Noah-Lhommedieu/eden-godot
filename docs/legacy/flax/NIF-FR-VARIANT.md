# NIF-FR-VARIANT — caractérisation des nifs FR « hors spec nif2gltf » (audit 2026-11-02)

Bloquant M4-0.2 : ~1 061 nifs de `corpus/fr-full` produisent des GLB
vides (char 348, map 240, monster 157, effect 132, item 95, ride 67…).

## Verdict

**Ce n'est PAS une variante de format.** Les nifs incriminés sont des
Gamebryo 20.x **syntaxiquement standard** ; le problème est au niveau
**données** : certaines sections géométrie sont tronquées/vides ou
divergentes. Le lecteur `nif2gltf` n'a pas besoin d'adaptation de
format — il faut décider de la **source de vérité des fichiers**.

## 1. Format observé (conforme spec nif.xml)

En-tête identique sur les échantillons testés :

```
"Gamebryo File Format, Version 20.2.0.8\n"   (ou 20.3.1.0)
u32 version    = 0x14020008 / 0x14030100
u8  endian     = 1 (little)
u32 userVersion= 0
u32 numBlocks
u16 numBlockTypes + SizedString types
u16[numBlocks] blockTypeIndex
u32[numBlocks] blockSizes          (présent depuis 20.2.0.5)
u32 numStrings + SizedString[]
u32 numGroups + u32[]
block bodies …
u32 numRoots + i32 roots
```

Ex. `map/model/object/ub124.nif` : 162 blocs, 17 types (NiNode,
NiTriShape, NiTriShapeData, NiTexturingProperty…) — layout valide.
`char/model/b.nif` : 20.3.1.0, 2 451 blocs, skinning complet.

## 2. Le vrai problème : le corpus contient des fichiers dégradés

`corpus/fr-full` = extraction fidèle des pak du client FR live
(`~/Téléchargements/EDENFR/pkg/pkg*.pkg` — revérifié en ré-extrayant :
`pkgfs_unpack` ET `7z` (Deflate64) produisent **bit pour bit** les mêmes
fichiers dégradés). Les pak actuels contiennent bien ces fichiers.

Une **seconde source FR** (`~/Téléchargements/EDENFR/extracted/` —
extraction antérieure/d'autres archives du même client) contient des
versions **complètes et conformes** des mêmes fichiers : 6 417/6 421
nifs couverts, **250/250 échantillonnés décodent des triangles réels**.
Seuls 4 nifs manquent : `s112, s113, s205, s501.nif` (nifs de map).

~1 897 nifs diffèrent entre les deux arbres. Deux patrons de dégradation
sur les versions pak actuelles :

### Patron A — sections attributs/index mises à zéro (ub124, ue013)

Bloc `NiTriShapeData` : header+vertices+normales **identiques** à la
version complète ; puis **tout est 0x00** : vertex colors, UVs,
`Num Triangles`, points, match groups. La taille de bloc déclarée est
inchangée (le zéro-padding la conserve).

`ue013` bloc 10 (36 994 o, 625 v) :

| section | offset | FR pak | extracted/US |
|---|---|---|---|
| gid..flags | +0..9 | OK | identique |
| Vertices 625×12 | +9 | OK | identique |
| Normals 625×12 | +7512 | OK | identique |
| BoundSphere | +15012 | OK | identique |
| VColors 625×16 | +15029 | **12 o non nuls** | 5 012 |
| UV sets 625×8 | +25029 | **tout zéro** | 4 968 |
| tris + groups | +30035.. | **tout zéro** | 4 784 |

→ parse OK, `NumTriangles=0` → **GLB vide** (c'est le « hors spec »).

### Patron B — corps de bloc divergent (p330)

Même table de blocs (13 blocs, tailles identiques) mais le contenu ne
suit pas le layout attendu : `NiTriStripsData` blk9 déclare 49 732 o
alors que le champ-walk en exige ~122 154 → `overrun`. blk12 contient
des floats dupliquant une tranche du blk9. → `_parse_error`, blocs
skippés par taille → 0 tri.

La version `extracted/` du même fichier : 0 warning, ~2 261 tris.

### Patron C — fichier différent mais valide (~60 % des divergents)

252/409 échantillonnés : >50 % d'octets diffèrent, ce sont simplement
d'autres builds de l'asset — ils convertissent normalement.

## 3. Pourquoi le client FR fonctionne quand même

Hypothèse (non vérifiée en runtime) : les pak `pkgNNN` sont des patchs
incrémentaux ; le contenu pak actuel dégrade certains nifs (build de
patch défectueux côté éditeur ?), et le client utilise soit les versions
antérieures encore présentes ailleurs, soit tolère les meshes vides
(props invisibles non remarqués). **À trancher si un prop s'avère
manquant en jeu.**

## 4. Conséquences pour Eden.Core

- `NifFile` (à écrire pour M4) doit **tolérer** :
  - `NumTriangles=0` / `Strip Counts` vides → mesh vide explicite
    (pas d'exception) — patron A ;
  - blocs qui sur-consomment → tronquer à la taille déclarée,
    warning, pas de resync raté — patron B ;
  - garder les `Ref` négatifs (-1) = absent.
- **Pas de fork de format** : nif.xml est correct.
- Recommandation corpus : pour les ~1 897 chemins divergents, préférer
  les versions `EDENFR/extracted/` (toutes spec-valides). C'est un
  changement de données, pas de lecteur.

## 5. Fichiers échantillonnés

| fichier | pak actuel (corpus) | extracted/ |
|---|---|---|
| map/model/object/ub124.nif | 166 v, 0 tri (patron A) | 166 v, 120 tri |
| map/model/object/ue013.nif | 625 v, 0 tri (patron A) | 625 v, ~1 158 tri |
| decorate/model/p330.nif | overrun blk9/12 (patron B) | 949 v, ~2 261 tri |
| char/model/b.nif | 20.3.1.0 OK (identique) | identique |
| monster/model/M001.nif | OK (identique) | identique |
