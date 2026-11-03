# PoC `.scn` depuis disque + `LoadThreadedRequest` — voie A (gate Phase 3)

Date : 2026-10-29 · Moteur : Godot 4.7.2.mono · Runner :
`scripts/debug/PocScn.cs` (trigger `--poc-scn[=N]`, hook 1 ligne dans
`ScreenManager._Ready`).

Gate du plan (`PLAN-PACK-EPK.md`, « Révision r2 » §A) : mesurer les
4 métriques (I/O+decode / reconstruction / instanciation / total
visible) de la voie A « `.scn` extraits → `user://cache/` +
`LoadThreadedRequest` » vs le baseline GLB séquentiel.
**Seuil : gain ≥ ×2 → voie A retenue, sinon repli voie C.**

## Méthodologie

- **Échantillon** : 20 `.glb` sous `assets/models/map/model/object/`
  (1159 fichiers au total), pick à pas régulier sur la liste triée par
  taille décroissante — couvre le pire cas (UA112.glb 1300 Ko) jusqu'à
  la longue traîne (UE735_2MN.glb 3 Ko) ; UE013.glb forcée
  (référence connue).
- **Baseline GLB** : `AssetCatalog.ReleaseCaches()` puis
  `PreloadGlbProtos` (séquentiel, maxDegree=1 — le parallélisme GLB
  bloque l'ObjectDB, P0.1) + instanciation via `LoadGlbNode`
  (`DuplicateSanitized`). Parse complet = `GltfDocument` +
  ToonMaterializer + PerfCulling + sanitize, comme en prod.
- **Bake** : `GetProto` (chaîne complète) → revendication d'owner sur
  tout le sous-arbre (obligatoire : `PackedScene.Pack` n'embarque que
  les nœuds dont l'owner est la racine) → `Pack` →
  `ResourceSaver.Save` → `user://poc_scn/<nom>.scn`.
- **Voie .scn** : `LoadThreadedRequest(path, "", useSubThreads,
  CacheMode.Ignore)` puis poll `LoadThreadedGetStatus`, puis
  `LoadThreadedGet` + `Instantiate`. Deux passes : séquentielle
  (référence decode seul) et parallèle (tous les requests lancés,
  poll round-robin).
- **Fidélité** : diff structurel proto GLB vs instance .scn —
  nb MeshInstance3D, surfaces, type de matériau par surface,
  meshes masqués (helpers collision), AnimationPlayer/anims,
  meta `src_tex`.

Deux environnements mesurés :

| Run | Commande | mips | S3TC |
|---|---|---|---|
| headless | `DISPLAY= EDEN_MIPMAPS=1 godot --headless --path godot_project -- --poc-scn` | non¹ | non² |
| réel | `DISPLAY=:0 godot --path godot_project -- --poc-scn` | oui | oui |

¹ `AssetCatalog.MipmapsEnabled` court-circuite en headless
(`AssetCatalog.cs:615`) — `EDEN_MIPMAPS=1` ne force rien : le PoC
headless mesure le parse/reconstruction **sans mips**, acceptable
pour le gate (constaté, documenté).
² `CompressImage` skippe aussi en headless — les `.scn` headless
contiennent des images RGBA8 non compressées.

## Résultats (20 protos)

### Headless (2 runs, écart < 3 %)

| Métrique | GLB baseline | .scn séquentiel | .scn parallèle |
|---|---|---|---|
| I/O + decode (parse) | 306–316 ms (preload) / 365–412 ms (boucle instrumentée) | 53.1–53.6 ms | 15.7–16.5 ms wall |
| Reconstruction (`LoadThreadedGet`) | — | 0.9–1.4 ms | 0.2–0.3 ms |
| Instanciation | 4.5–6.9 ms (`Duplicate`) | 2.2–2.5 ms | 0.9–1.2 ms |
| **Total visible** | **313–320 ms** | 56.6–57.0 ms | **17.2–17.6 ms** |

Médiane/fichier : parse GLB 9.5–11.5 ms ; req .scn 2.2 ms (seq) /
12.8–13.6 ms (parallèle, incl. contention).

### DISPLAY=:0 (mips + S3TC — fidèle prod)

| Métrique | GLB baseline | .scn séquentiel | .scn parallèle |
|---|---|---|---|
| I/O + decode | 618.8 ms (preload) / 1340.9 ms (boucle) | 125.1 ms | 35.5 ms wall |
| Reconstruction | — | 1.0 ms | 0.1 ms |
| Instanciation | 4.2 ms | 2.1 ms | 0.8 ms |
| **Total visible** | **622.9 ms** | 128.2 ms | **36.5 ms** |

## Verdict

| Run | GLB total | .scn parallèle | Gain |
|---|---|---|---|
| headless | 313–320 ms | 17.2–17.6 ms | **×18.2** |
| DISPLAY=:0 | 622.9 ms | 36.5 ms | **×17.1** |

**GO voie A** — le seuil ×2 est dépassé d'un ordre de grandeur
(×17–18 mesuré, stable sur 3 runs). Le gate demandait 5 protos ;
l'échantillon en couvre 20.

- **Bake : 20/20 réussi, 0 échec** (0 nœud non-packable, 0 erreur
  Pack/Save). Coût bake : 50–167 ms pour 20 protos (hors parse GLB)
  — négligeable, c'est un traitement offline.
- **Fidélité : 20/20 MATCH** — MeshInstance3D, surfaces, matériaux
  (type), visibilité des helpers de collision, meta `src_tex` et le
  seul AnimationPlayer de l'échantillon (UD613, 1 anim) survivent
  intacts au round-trip pack/save/load/instantiate.

Extrapolation S001 (~184 protos, ~9× l'échantillon) : GLB
≈ 2,9–5,7 s (cohérent avec les 7,5 s mesurés live, qui incluent
d'autres postes) → `.scn` parallèle ≈ **160–340 ms**. Le poste
« parse GLB » disparaît du chargement map.

## Observations pour l'étape bake (hors gate)

- **`Owner` obligatoire** : `PackedScene.Pack` ignore les fils
  runtime sans owner — le PoC revendique tout le sous-arbre
  (`ClaimOwnership`), 0 échec constaté.
- **Textures embarquées dans le `.scn`** : ~3 Mo/proto en moyenne
  (60–65 Mo pour 20 protos — mips +33 %, compression S3TC des images
  sérialisées non déterminante à cette échelle). Bonus du plan
  confirmé : le decode textures disparaît du chargement. Attention
  stockage EPK : ~184 protos ≈ 0,5–0,6 Go de `.scn` (mesure à refaire
  sur le bake complet ; la dédup textures entre protos reste un
  levier si nécessaire).
- `LoadThreadedGet` + `Instantiate` sont quasi gratuits (< 1 ms/fich)
  — le coût est dans le request (I/O + decode binaire), amorti par
  le parallélisme natif (wall ≈ max, pas somme).
- Fuites RID à la sortie (quit brutal, cache non relâché) — bruit de
  PoC, sans impact sur la mesure.

## Bake production

`scripts/debug/ScnBake.cs` (même hook 1 ligne `ScreenManager`,
trigger `--bake-protos=<spec>,<outdir>`) industrialise le bake :
spec = rel assets d'un proto, fichier liste (un rel/ligne) ou glob
rel assets (`models/.../*`, `**` récursif). Chaque GLB passe la chaîne
`GetProto` complète puis `Pack`/`Save` → `<outdir>/<rel>.scn` ; sortie
machine `BAKE`/`BAKE-FAIL`/`BAKE-DONE` (+`--bake-verify` pour recharger
et compter les nœuds ; `--jobs=N` ignoré — parse séquentiel P0.1).
Les `.scn` embarquent les textures (~3 Mo/proto) — voulu : le decode
texture disparaît au runtime ; dédup inter-protos non faite en v1.

## Limites

- **Headless ≠ render réel** : pas de mips ni S3TC côté headless ;
  la mesure `DISPLAY=:0` compense (×17.1). Ni l'une ni l'autre ne
  mesure le premier rendu GPU (upload textures) — hors périmètre du
  gate, à valider en visuel lors de l'intégration.
- **`.scn` versionné moteur** : lié à Godot 4.7.x → rebake obligatoire
  au bump moteur (risque déjà listé au plan).
- **Cache clair sur disque** (`user://cache/`) : compromission
  d'exposition à documenter/accepter selon la voie A (≈ dump RAM).
- Échantillon = protos map `object/` uniquement ; sky/, char/,
  monster/ à re-mesurer au bake complet (chaîne `GetProto` identique
  mais culling/toon diffèrent).
- Les requêtes parallèles utilisent `CacheMode.Ignore` (mesures à
  froid garanties) — en prod `Reuse` convient.
