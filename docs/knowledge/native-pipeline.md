# Pipeline natif (NIF/DDS/KF) — état

Statut : **verified** pour D (modèles fixes) sur 5 maps, **hypothesis** pour E/F
(skin, anim, fx : pas revalidés visuellement le 2026-11-02). Godot 4.8-beta2,
2026-11-02.

## Bascule

`EDEN_NATIVE` absent ou vide = `all` (tex, mesh, skin, anim, fx). `none` ou
`derived` = ancien chemin GLB/PNG. Liste explicite possible
(`EDEN_NATIVE=tex,mesh`). Code : `scripts/native/NativeMode.cs`. L'ancien
chemin reste jusqu'à la phase G (plan : `docs/architecture/PLAN-ACTION-NATIF-EDENFR.md`).

## Sémantique alpha (verified)

Les NIF ne portent pas de matériau glTF ; `NifMaterials.For` doit rendre ce que
l'import glTF de Godot donnait pour `convert.py` :

| NiAlphaProperty | convert.py | import glTF Godot | natif |
|---|---|---|---|
| flag blend (bit 0) | `BLEND` | `AlphaDepthPrePass` + `depth_draw=OpaqueOnly` | idem |
| sinon flag test (0x200) | `MASK`, seuil/255 | `AlphaScissor` | idem |
| aucune | opaque | opaque | idem |

Piège : `Transparency.Alpha` pur n'est pas équivalent. `ToonMaterializer`
ne convertit que `Alpha` en `AlphaHash` (sinon dither noir/blanc sur les
murs) et `PropBatcher` rejette `Alpha` et `AlphaDepthPrePass` (« alphablend »).
Les compteurs du log `[MapLoader] OBJ :` servent de test de non-régression :
S001 273 GLB / 1102 multimeshes, S002 96 / 709, S009 264 / 884.

## Écarts connus natif vs dérivé (hypothesis)

- 98 PNG dérivés (monster 7, char 86, item 2, ride 3) diffèrent en couleur
  du DDS de `fr-full` (ex. `M08604`). Le natif suit la source EDENFR.
  Mesure : moyenne de couleur 8x8 PNG vs DDS décodé par Pillow ; origine
  du dérivé non élucidée.
- Franges noires sur feuillages à alpha test : DXT1 d'origine (RGB noir sous
  alpha 0) ; le dérivé passe par BC7 qui masque la frange.
- 14 DDS de `map/texture` ont des mips d'artiste très différents de la mip 0
  (`uc32401`, `s1040h01`…) : le natif les affiche tels quels. Test avec mips
  régénérées sur S009 : aucun changement visible.

## Preuves (2026-11-02)

Parité 199/200 ; captures regardées S001, S002, S009, S018, S035 ; bench
S001/S002 voir le plan. Trouver la cause d'une divergence : comparer
`EDEN_NATIVE=mesh` et `EDEN_NATIVE=tex` séparément (S009 : le mesh seul
était identique, la texture seule portait l'écart de couleur).


## Mise à jour 2026-11-02 (phases P/G) — statut `verified`, Godot 4.8-beta2

- Natif seul : `NativeMode` est toujours actif, `godot_project/assets/` n'existe plus.
- Mode EPK strict (`EDEN_ALLOW_LOOSE=0 EDEN_PACK_STRICT=1`, paquets natifs signés/chiffrés) validé sur S001 et S002 : mêmes décomptes que les fichiers libres, frame p50 identique, RSS -1 %.
- Pièges trouvés : un chemin `.glb` virtuel (`TryGetGlb`) sert de clé vers le `.nif` ; l'index DDS de `NifMaterials` doit venir de l'EPK ; `EdenVfs.ListDir(…, recursive: true)` doit dédupliquer par chemin relatif.
- `ui_textures/imagesets` (PNG, copies des imagesets EDENFR) n'est plus lu en mode pack ; l'UI lit `uidata/imagesets/<Thème>/` via le VFS.
- Couverture : `tools/native-coverage/native-coverage.sh --all` (décodage de tous les fichiers + références des tables).

## Mise à jour 2026-11-02 (résolution node→géométrie) — statut `verified`

- `data/scene/s{id}.ini` `[globe_data]` col0 déclare la géométrie du node
  (« S055.fsm », « S403.nif ») — mécanisme EDENFR : **191 nodes sans dossier
  `map/model` propre portent leur scène propre** (portails/zones/PNJ/spawn)
  qui pointe vers une géométrie partagée. `MapLoader` : globe_data pilote
  fsm/nif/smp/env ; scène/entités/spawn = code du node. `MapExists` et
  `LoadingScreen` (préchargement fsm) suivent la même règle.
- Ordre de résolution client : géométrie propre → `globe_data` de scène →
  donneur `NodeGeometry` (c_node col5/minimap, 38 nodes sans scène propre) →
  `local.ini [Scene] ModelName` quand aucune scène (S995 → S402).
- Les 5 nodes ex-« orphelins » (132, 309, 599, 766, 996) sont tous
  scène-déclarés : aucun node nommé n'est une lacune d'archive.
- Validé : S650 (géo S003 + scène propre, titre zone propre), S137 (S055 +
  portails propres — coords minimap ≠ donneur), S996 (S403). Test Core :
  `MapChainM3Tests.S137_ScenePropre_GeometrieDeclareeS055`.
