# Plan — Phase 3 : migration vers les codecs générés (22 oct. 2026)

Suite de `2026-10-22-fiabilisation-phases-0-1.md`. Objectif : les corps
de paquets vivent dans les classes `CNC_*` générées (Serialize/Deserialize
prouvés par captures golden), plus dans des `Concat(BitConverter…)`
inline.

## Inventaire (fait)

~130 sites `SendPacketAsync(ncid, …)` inline dans `Eden.Zone/` :
- **30 NCIDs PROUVÉS** — classe générée + capture golden round-trip :
  migration directe vers `SendAsync(new CNC_*)`.
- **68 NCIDs** — classe générée sans capture : migration uniquement
  avec preuve byte-à-byte ajoutée (test d'équivalence sur le layout
  ou nouvelle capture), sinon on attend.
- **18 NCIDs INCOMPLETS** (`known_incomplete.json`) — le codec généré
  est connu partiel : garder l'inline, reprendre le reverse d'abord.
- 0 NCID sans classe.

## Ordre de migration

1. Famille combat/cast prouvée : 271, 272, 279, 280, 281, 292, 293,
   296, 299, 300, 305, 313, 444.
2. Reste des prouvés : 264, 267, 268, 286, 316, 388, 391, 404, 450,
   473, 489, 492, 493, 509, 510, 531, 563.
3. Par la suite : les 18 incomplets → reverse (désasm Serialize) puis
   migration ; les 68 sans capture → au fil des nouvelles captures.

## Règles par paquet migré

- Vérifier que le `Serialize` de la classe produit EXACTEMENT le même
  layout que l'inline (comparaison champ par champ), pas juste qu'il
  existe — un inline peut contenir un format corrigé que la classe
  n'a pas (cf. 444 `[mob][0][char]`).
- Renommer les `FieldN` génériques de la classe en noms sémantiques
  quand le code/la capture les documente (avec commentaire de preuve).
- Les builders comme `CombatPackets.DisplayMonsterBody` sont
  remplacés par la classe quand le layout est identique.
- Broadcasts multi-session : `SendAsync` par session (la classe
  sérialise `[u16 len][u16 ncid][body]` — équivalent trame à
  `SendPacketAsync`).
- Golden tests doivent rester verts ; `known_incomplete.json` ne
  change que quand un codec devient réellement complet.

## Progression (2026-10-24)

### Vague 1 — `acf9edc` : famille combat/cast
271, 279, 281, 292, 293, 299, 300, 305, 313, 444 migrés vers les
classes générées (corps prouvés golden).

### Vague 2 — `0dd242b` : corrections de classes sur preuve capture
- **302** : classe générée 5B fausse → 9B prouvé capture.
- **288** : classe 43B fausse → 35B prouvé (8 trames capturées).
- **326** : classe fixe fausse → corps variable 11B/15B corrigé.
- **268** : blob lié ajouté au codec. **510** : slots 0 exclus.
- Sites migrés : 264×2, 267×3, 268×3, 286×4, 287×2, 288×4, 302×3,
  316×3, 326×4, 388, 438, 444, 450, 473, 509×3, 510, 531×5, 563.
- +24 cas golden actifs (288/302/326 sortis de known_incomplete).

### Vague 3 — `60324e8` : attributs + 297
- 296, 492, 493, 489 migrés (correspondance champ-à-champ vérifiée).
- **297** : le corps inline était une supposition fausse — réécrit
  d'après `CNC_CZ_ClientSpellBlastToLocation::Serialize` @0x081fe6e0
  ([u32][s16][i32][f32×6][i32][u16 n][n×f32][u16 m][m×f32]) —
  bug wire réel corrigé.

### Vague 4 — `edbef7b`
- 280 ClientUpdateAttr ×10, 272 MeleeAttack migrés.
- 391/404/316 restants = relais bruts de trames entrantes (passthrough
  volontaire, pas de migration).

### Vague 5 — `cbd5aeb` : 298 `ClientSeeMob`
- Format natif à **masque de champs optionnels** (u16 bitmask +
  cascade) reproduit fidèlement ; sorti de known_incomplete.

### Vague 6 — `e11c5e5` : corrections depuis captures (6 codecs)
- **284** `ClientLevelUp` : Field4 int→i64 maxHp (corps 19B).
- **328** `ClientModifyAttr` : +Field4 i64 aux (corps 18B, forme 280).
- **368** `ClientUpdateCoolDown` : réécrit variable
  `[u8 n][n×(u8 kind, s16 id, u16 aux, i32 ds)]`.
- **488** `ClientPlayerClosets` : réécrit `[u8 type][u16 n][n×27B]`.
- **543** `ClientPlayerLifeSkill` : réécrit `[s16][u16 n][n×u16]
  [u16 n][n×u32][u16 n][n×u32]`.
- **600** `ClientUpdateAttrNewExp` : Field2 int→i64 (corps 12B).
- Émetteurs migrés : 284×2, 328, 368×2, 488, 543, 600. 704/704 verts.

### Reste (après vague 6)
- Golden + known_incomplete encore émis en brut :
  - **274, 312, 358** : codec de production = `NativeItemCodec`
    (format variable validé live) — la classe générée plate reste
    fausse ; conserver en known_incomplete tant que la classe n'est
    pas réécrite en format imbriqué.
  - **412** : idem avec `NativeElfCodec`.
  - **283, 285, 301, 348** : structures variables non trivial —
    audit `::Serialize` natif requis (reindex) avant correction.
  - **24, 57, 62** : liens inter-serveur (ZW/ZMis/ZM), corps
    variables ; captures trop maigres (0B/2B) → garder incomplet.
- ~60 NCIDs sans capture → preuve désasm au cas par cas.

### Vagues 7–9 (antérieures) — voir `e11c5e5`..`cac5206`
- Audits `::Serialize` natifs : 283 (`[i32][u8][i32][u16 n][n×u32]
  [u16 m][m×u32]`), 285 (`[i32][s16 n][n×(i32,str,s16)][u8][i32]`),
  301 (`[i32][u8 n][n×18B][4B][u8]`), 348 (4 maps + 2 vecteurs),
  298 (masque de champs optionnels) — classes corrigées, sorties
  de known_incomplete, émetteurs migrés.
- NCIDs conformes validés désasm : 60/266/269/275/278/290/304/327
  (blob ajouté)/329/330/347/349/357/383/386/410/418/430/432/433/
  439/441/470/495/496/539 — émetteurs migrés vers classes typées.

### Vague 11 — `ac1a3a4` : 23 NCIDs conformes migrés
- 375/376/377 vendor, 398, 399×2, 455, 483, 500, 501, 507, 513×3,
  514, 524, 564, 579×2, 585, 602, 40 (CNC_CW_ClientResetTime ZW).

### Vague 12 — `b8941ce` : réécritures variables
- **352** `ClientReceiveShortcutOfPage` : `[s16 page][u16 mask]
  [i32×flags][u16 n][n×i32]` (@0x08202540) — classe réécrite.
- **572** `ClientLoadCubeItems` : 3 × `[u16 n][n×s16]`
  (@0x080c96f0) — classe réécrite.

### Bilan — reste volontairement en codecs dédiés
Formats variables/listes déjà couverts par des codecs focalisés
(séparation wire/gameplay respectée) :
- **282/283/274/312/358** : `NativeItemCodec`.
- **345** : relais corps de mission avec réécriture de champs.
- **369/612/613** : `MagicBoxPackets`.
- **378** : `VendorPackets.BuildUpdateView`.
- **393** : helper `SendEnterTransportAsync`.
- **413/412** : `NativeElfCodec`.
- **599** : `WeddingPackets`.
- **391/404/316, 402/403/405/406/409, 24/57/62** : relais bruts de
  corps entrants (passthrough volontaire).
Classes générées laissées en `known_incomplete` quand leur layout
plat ne représente pas le format variable natif — les codecs
dédiés sont la voie de production.
