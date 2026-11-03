# V7-TAIL-NOTES — queue non framée des fsm EDENFR (audit 2026-11-02)

Bloquant M4-0.1 : 15 fsm FR (`s002, s003, s004, s005, s010, s011, s021,
s030, s032, s051, s054, s119, s225, s307, s501`) exposent des octets non
framés après `LAY0` (`FsmFile.TrailingBytes`). Hypothèse initiale :
GRL/GRS/OBJ sérialisés sans tags. **Réfutée** — voir §3.

## 1. Faits établis (offsets mesurés)

### s002.fsm (7 454 808 o, fr-full ≡ original-fr byte à byte)

- Header `0x58`, `LAY0` tag `@0x58`, size `0x644d48` → fin `@0x644da0`.
- LAY0 : grille 225×225, scale 2.0, recordCount déclaré = **60** ;
  **49** records tuile valides trouvés (48 kind=0 terrain + 1 kind=1 eau).
- Queue `@0x644da0`, len `0xd72b8` (881 336 o). Entropie **2.18 bits**,
  seulement **5 valeurs d'octet distinctes** `{0x3f, 0xc2, 0x00, 0x80,
  0xc1}` — motif de 10 f32 répété ~22 000 fois :
  `[-0.0, 1.0, -64.0, 0.749, -97.38, -47.81, -24.35, -97.12, 0.0, -0.0]`
  avec dérive lente (champ par champ) — champ scalaire/attribut baké,
  pas des records OBJ/GRS.
- Deux blocs non-tuile **dans** LAY0 : `0x2ecf20..0x358b44` (0x6bc24 o,
  entropie 2.55) et `0x58a648..0x644d94` (0xba74c o, entropie 3.41) —
  vertices `{pos3|normal3|color4}` unitaires (ex. @0x58a64c :
  pos(184,64,-31), n(0,0,1), c(0.62,0.75,0.69,0.8)), noms de textures
  terrain (`dust_01.dds`, `mount_01.dds`, `grass_01.dds`, `road_01.dds`,
  `ter_env.dds`) et refs `27DarkMap`/`28DarkMap`.
- Un en-tête de grille **21×21** valide trouvé `@0x33df50` (offset NON
  aligné-4 !) — cell 2.0, kind 0, 441 vertices pos/norm/col cohérents
  (patch vertical x=0, y 0..26, z -7..55). Scan aligné-4 insuffisant.
- Oracle US s002 : `GRL ` 7 types / `GRS ` 21 597 / `OBJ ` 2 064 — fin
  exacte du fichier. La queue FR **ne contient aucune de ces données**.

### s010.fsm — preuve que la queue continue le flux de tuiles

- LAY0 fin `@0x85bcb0`, queue `0xf3028` o.
- `@tail+0` : rampe u32 descendante (205,203,200,195,… ≈ 12 319 o).
- **6 records tuile 33×33 consécutifs** (stride exact `0x1abf4`) :
  `@0x85eccf, 0x8798c3, 0x8944b7, 0x8af0ab, 0x8c9c9f, 0x8e4893` —
  tuiles (448,128)…(448,448), la colonne x=448 qui manque dans LAY0 FR
  (l'oracle US a les 8 tuiles x=448 dans son LAY0).
- ~325 713 o de suffixe après la dernière tuile (données f32/vertex).

### s501.fsm — blocs auxiliaires hétérogènes

- recordCount 25 ; 16 refs `DarkMap` trouvées dont 4 **à l'intérieur**
  d'un bloc non-tuile (0x1bdaba, 0x1d86ae, 0x1f32a2, 0x20de96) → les
  blocs auxiliaires encapsulent des données par-tuile.
- Motifs observés : tableau u32=254 répété ; données u24-like compactées ;
  séries f32 ~1.0 longues.

### Inventaire queue (15 fsm)

| fsm | tail (o) | fsm | tail (o) |
|---|---|---|---|
| s002 | 0xd72b8 | s032 | 0x1cfa3c |
| s003 | 0xf6008 | s051 | 0x76671 |
| s004 | 0xf8170 | s054 | 0xe15b8 |
| s005 | 0x7b4d0 | s119 | 0x16660 |
| s010 | 0xf3028 | s225 | 0x1e33c |
| s011 | 0x61a44 | s307 | 0xe421c |
| s021 | 0x11b044 | s501 | 0x326c8 |
| s030 | 0x71a9c | | |

## 2. Layouts candidats testés et réfutés

- `count u32 + n×44` puis `count + n×32` puis `count + n×92`
  (ordre GRL→GRS→OBJ, ordre inversé, variantes u16, records bruts sans
  compte) : **aucun** ne produit de noms ASCII ni de typeIds cohérents.
- Recherche exhaustive sur les 15 queues : **0 chaîne `.nif`**, 0 nom
  d'objet `map/model/object/*` — il n'y a **pas** de GRL/OBJ sérialisé.
- Seules chaînes présentes : textures terrain `*.dds` (mount/dust/
  grass/road/snow/water_env/ter_env) + `NN DarkMap` — les TexSlots des
  records tuile.
- zlib : échec direct ; XOR/masque : non.

## 3. Conclusion

La « queue V7 » **n'est pas** la section props/herbe : c'est la
**continuation du flux de records LAY0** — records tuile 33×33
(0x1abf4 o) et records auxiliaires de taille variable (sous-maillages
{pos,normal,color}, darkmaps embarquées, tables de textures) entrelacés,
le tout à **alignement arbitraire** (offsets non multiples de 4).

Les données props/herbe équivalentes à GRL/GRS/OBJ n'ont **pas** été
localisées dans ces fichiers (pas de noms .nif, pas de records 44/32/92,
pas de typeIds). Elles sont soit absentes des fsm V7 (props via un autre
canal — scene.ini ne liste pas de props), soit encodées dans les blocs
auxiliaires sous une forme non identifiée.

## 4. Ce qui est fait côté parseur

- `FsmFile` tolère la queue : `TrailingBytes` > 0 + `TrailingSpan`
  (accès brut sans recopie) ; tuiles standard extraites par scan
  `IsTileRecord` (+4 o) — 49 tuiles s002 OK.
- Limites connues : scan par pas de 4 → un record tuile aligné-impair
  serait raté (aucun cas constaté) ; les records auxiliaires et la
  queue ne sont pas décodés (schéma inconnu).
- Test : `FsmTests.S002_V7Queue_ExposedAsTrailingSpan`.

## 5. Pistes pour la suite (hors M4)

- Parser les records auxiliaires comme records grille généralisés
  ({gw,gh,cell,kind} à offset arbitraire + payload) — le 21×21 de s002
  suggère une famille de records maillage.
- Déterminer si les blocs auxiliaires portent les darkmaps 128×128
  (refs `NN DarkMap` présentes, blobs ~taille plausible).
- Si les props sont confirmés absents du fsm : source alternative à
  identifier (fichier compagnon, ou maps V7 sans props posés — s002 en
  a 2 064 côté US).
