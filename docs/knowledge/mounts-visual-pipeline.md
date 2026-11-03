# Montures — pipeline visuel, combat, vitesse (EDENFR)

Version testée : Godot 4.8.beta2.mono, client `eden_godot`, corpus
`corpus/fr-full` uniquement. Date : 2026-11-03 (build + NetTests 828 /
Eden.Core.Tests 966 / Eden.Tests 1471 verts le 2026-11-03 nuit, captures
skinfix-off/on). Graphe + tests Eden.Core à jour au moment de la
rédaction.
Revue visuelle du 2026-11-04 (sweep 58/58 OK, captures cam90+cam270,
correction VfsReadGuardTests) reportée dans le tableau ci-dessous.
Session 2026-11-03 soir : fix boucle caméra ride-debug (`_faceBase`),
diagnostic émetteurs skinnés bind-posés (r014/r253), alpha M609 vérifié.
Session 2026-11-03 nuit : **fix émetteurs skinnés livré** (`EmitterSkin`
+ `NiParticleRuntime.Reskin`, A/B `EDEN_FX_SKIN`), captures skinfix-off/on.
Anomalies tranchées le 2026-11-03 : halos r014 (émissif non texturé →
fix shader `NativeFx`), badge épée type ≠ 0 (type 1 = montures à
sorts), commandes 3024 (heal one-shot) / 2055 (mort natif).

## Statut par point

| Point | Statut | Preuve |
|---|---|---|
| Modèles/stem : `rideKey/1000` → `r{key:000}` via `RideList.Stem`, repli natif `param_2/1000` (`FUN_00773d90`, client_game) | verified | `CharVisual.RideStem` ; NetTests `RideTable*` 55 verts |
| Attache cavalier = nœud `L17` animé (natif `CRide::Attach`, rf_game `FUN_0073fc60`) + RideSeat orthonormé + repli RideFit si L17 absent/aberrant | verified | `eden_godot/docs/MONTURES-L17.md` ; `RideSeatMathTests` |
| Textures manquantes = surfaces référençant `nif_tex` sans albedo ; meshes cachés/aides sans texture ≠ trou de données. **Analyse du 2026-11-03 : les 29 FAIL du sweep ont TOUS `notex>0`, aucun ne manque `stand01`** — faux positifs de l'ancien critère (toute surface sans AlbedoTexture, ex. 21 surfaces sur r253) | verified (code + tsv) | `RideLab.Finish` — `notex` ne compte que les références non résolues (`nif_tex` meta copiée par `ToonMaterializer` :414,515), `untx` = info |
| `move01` non requis pour le statut OK (ex. r711 « Tour à Canon » sans marche, natif) | verified (code) | `RideLab.Finish`, note `noMove` |
| Anims directionnelles montées : A→`move02`/yaw +90, D→`move03`/yaw −90, WA/WD ±45° (`DirectionalLocomotion`, `MountSideAnims`) | verified (tests + **captures 2026-11-04**) | `DirectionalLocomotionTests` ; `artifacts/ride-lab/{cam90,cam270}` — r253/r014 : A = arrière cam90 / face cam270, D = miroir exact → **non inversés** ; r091 : modèle symétrique ±90 identique au pixel (pivot prouvé via STAND≠A) ; r711 : aucune anim de marche (donnée native, `noMove`) |
| **« Inversion » live des anims directionnelles = boucle de rétroaction caméra debug, PAS le mapping anim** — `_Process` recuisait `_yaw = ModelYaw + _dbgYaw` alors que `ModelYaw` contient déjà le `FaceOffset` ±90° : le cap gonflait à chaque frame (run avant : `modelYaw` 644→1889→2036°, `camYaw` 2036° en ~4 s). Fix `PlayerController.cs` : `_faceBase` = cap hors `FaceOffset` (clavier→`_yaw` hors debug, clic→`targetYaw`, **gelé sous `_dbgYawOn`** car `_yaw` y est dérivé) ; la caméra debug lit `_faceBase + _dbgYaw`. | verified (**run réel input 2026-11-03**) | `tools/ride-script.sh 14010` (touches injectées via `Input.ParseInputEvent`, `DebugCam` actif — même chemin que le live `ride_key.txt`) : camYaw **120° constant**, modelYaw STAND=0 / W=120 / **A=210 (+90)** / **D=30 (−90)** / S=120, anims `move01/02/03/04` correctes (`artifacts/ride-script/14010/run.log` ; le run avant-fix montrait `modelYaw` 644→1889→2036° et `camYaw` 2036° — chiffres consignés ici, PNG écrasés) |
| Vitesse d'anim : monté 100 %→×1.0, 150 %→×1.5, pied 20 %→×1.0, bornes 1–3 | verified | `MoveAnimSpeedTests` (InlineData 100/150/200/35/900, 0/20/30/40) |
| Vitesse distante : dérivée du déplacement snapshot (`EntityRegistry.ObserveSpeed`, base pied 6 u/s, lissage 0.4) — pas de champ protocole ajouté (pas de preuve native) | verified (code) | `ServerMoveSpeedTests` |
| Combat monté : routage `attack01-04`, `magic01/09/19/20`, `shoot*` (natif `FUN_00778660`/`FUN_007785d0`) ; `magic03`/`magic02` NON routés nativement → repli pose cavalier | verified (tests + désasm + **captures 2026-11-04**) | `MountCombatRules.RoutedAction` + NetTests `MountRulesTests` ; ride-lab : `attack01`/`rattack01`/`magic01`/`rmagic01` joués sur montures de combat seules (253020 type 2, 711010 type 1) — non routés sur non-combat (14010, 91010), conforme à l'auto-dismount natif |
| Stats monture combat : enchant 11354 → `2134|253020||1|2` (type 2), `2051|90` → palier 90, `2002|7` = PV +7 %, `2083|2|1` = ATQ +2 %/DÉF +1 % ; stage « ride » du `StatPipeline` (`Program.cs:634`) applique tout sauf 2051 (déjà dans `CombatantSnapshot.MoveSpeedBonusPct` via `RideStates` → 296 Speeds[0]) | verified (données + tests + **chaîne retracée 2026-11-04**) | `c_enchant.ini` l.2 ; `RideSpeedRules.PipelineCommands` ; `LicenseService.ApplyEnchantCommands` ; NetTests `CombatMount_11354_DataChain_RideTypeAndStats` (nouveau) ; commandes AttrSpell* (ex. `2064` de 44469) appliquées via `ExecuteSpellAttrCommandsAsync` à la pose de l'enchant |
| PM des montures de combat : **aucun enchant de ride FR ne porte de bonus PM direct** (2013/1014 absents des blocs 9+7k — audit des 91 enchants type≠0) ; la hausse de PM vient de `2085` (toutes stats +N % → Sag) | verified (audit données + test) | NetTests `RealRides_NoDirectMpBonus` (nouveau, invariant) |
| Badge épée « monture de combat » sur l'icône d'item : `AttackIcon` de `uidata/imagesets/main.imageset` (177,288,18×18), affiché si `UseEnchantId` → 2134 **type ≠ 0** (`MountCombatRules.IsCombatRide`) — couvre les montures à sorts type 1 (10 enchants, toutes avec 2140) comme les type 2 | verified (natif + tests) | `InventoryWindow.ItemCell` ; natif : `CEC_Ride::SetState` (zone @0x0854f330) stocke var3 en char+0x824 relu `≠0` par `ISpell::CastTest` @0x084f08c3 (0 → dismount à l'engage) ; client `FUN_00778660` teste l'octet ride `≠0` ; serveur `RideType != 0` — capture HUD à faire |
| FX monture : les halos visibles (queues dorées r253, flammes r014, lueur rouge r260, néons r262) viennent des **matériaux émissifs + particules du NIF**, `NativeFxHost` compté = 0 sur les 58 montures du sweep. **R260–262 ne sont PAS des nuages noirs** (hypothèse corrigée) : r260 = scorpion rouge, r261/r262 = voitures | verified (captures) | `artifacts/ride-lab/fx/` ; anomalie ci-dessous |
| Sweep complet hors archive | verified (2026-11-04) | `artifacts/mount-sweep-lab2/results.tsv` : 90 lignes TSV = **58 rideKeys uniques**, 58 OK / **0 FAIL**, `notex=0` partout ; seule note `noMove` sur r711 (natif) — les 29 faux FAIL `notex` passent tous |
| Particules émetteurs mesh **skinnés** re-posées par frame (`EmitterSkin` pur + `NiParticleRuntime.Reskin`, pose `Skeleton3D`/`AnimationPlayer`) — émetteurs rigides inchangés | verified (tests + **captures A/B 2026-11-03**) | Eden.Core.Tests +5 (966 verts, coût 3,5–24 µs/psys) ; `artifacts/ride-lab/skinfix-{off,on}/` r014+r253 `STAND:1,W:1,A:1` |
| VfsReadGuardTests | verified (2026-11-04) | `RideLab` (StreamReader/DirectoryInfo) et `RideProbe` (DirectoryInfo) n'utilisent plus les API plafonnées — fichiers de contrôle `/tmp`/TSV, pas de donnée corpus ; test 2/2 vert |

## Points non prouvés / limites

- ~~**Anomalie FX (à investiguer)**~~ **r014 RÉSOLU (2026-11-03)** :
  les halos blancs géants n'étaient **pas** une texture non résolue —
  `[psdiag]` prouve `tex=R00251.dds 128×128 a=0..255` bien chargée, et
  le matériau particule est `dif=1,1,1 emi=1,1,1` en blend additif
  (max=310, ~150 particules vivantes). Le shader ajoutait l'émissif
  **à plat** (`tex·albedo + emissive`) : tout matériau émissif ≥1
  rendait un blanc uniforme qui écrasait la texture. Fix
  `NativeFx.cs` : `ALBEDO = t.rgb × (albedo + emissive) × vcol`
  (émissif DANS le facteur texturé, comme le pipeline fixe Gamebryo
  `tex × (emissive + ambient·Ia)`). Après fix : flammes orange/rouge
  visibles (crinière, pattes, queue) — couleur cohérente avec
  `R00251.dds` qui EST une flamme rouge/orange (pas bleue) ; petit
  cœur blanc saturé résiduel = sommation additive attendue. Statut :
  **visually_validated** (captures `artifacts/ride-lab/r014-fix2/`) ;
  la parité exacte avec le client oracle reste `hypothesis` (pas de
  ref-shot r014 au catalogue). Les queues lumineuses r253 débordent
  aussi le cadre (exposition forte, même famille additive — à
  surveiller, pas creusé).
- Logo épée : implémenté sur `ItemCell` (inventaire) ; non vérifié en
  capture. L'usage natif exact (coin de l'icône ? icône de buff ?) reste
  hypothesis — la source d'image (`AttackIcon`, atlas main) est
  verified.
- ~~`RideTypeOf` lit var3 du 2134~~ **TRANCHÉ (2026-11-03)** : le
  natif teste le type `≠0`, pas `==2` — `CEC_Ride::SetState` (zone
  @0x0854f330) stocke var3 en char+0x824, relu `≠0` par
  `ISpell::CastTest` @0x084f08c3 et `CEC_Chaos::SetState` @0x0851cac0
  (0 → `RemoveEnchantByState(9)`, dismount à l'engage/transform) ;
  côté client `FUN_00778660` route les anims de combat sur l'octet
  ride `≠0`. Corpus : type 1 = 10 enchants (58997, 50114, 51369,
  59033-59038…), TOUS avec `2140` RideSpell ; type 2 = combat
  ordinaire. `IsCombatRide` aligné sur `!= 0` (badge épée + serveur
  déjà `RideType != 0`). NetTests `CombatBadge_Type1SpellMount_IsCombat`
  + invariant `CombatBadge_Type1Invariants_AndNonCombat`. `verified`.
- Commandes d'enchant de ride non couvertes par `ApplyEnchantCommands`
  — **tranché 2026-11-03** : `3024` = `CEC_HPPlusRate` (zone : Init
  @0x08533a90, Execute @0x08532b80) = **soin one-shot à la pose**
  (heal = MaxHP × 0.01 × rand[var0..var1] ; `3024|25|25` = 25 % PV max
  à l'invocation de 44606) — PAS une stat : hors du stage « ride » à
  juste titre. L'écart réel est « commandes instantanées à l'apply
  d'enchant » (heal/téléport), trou pipeline global non porté — hors
  périmètre monture. `2055` = **absent du dispatch natif**
  (`DecodeEffectCommand`, extraction objdump → `EnchantCommands`) :
  ~997 enchants le portent en position de bloc (vars quasi toutes `1`,
  motif vecteur de classes-cibles designer-era) → le natif l'ignore
  aussi ; ignoré à juste titre. `2064` OK via SpellAttr ;
  `2152`/`7003`/`2034` hors stats (chaîne runtime / tag formule).
  `verified` pour 3024/2055 (sémantique native tracée), `not done`
  pour le port du heal-à-la-pose.
- ~~**Particules monture « mal placées » (r014, r253)**~~ **CORRIGÉ
  (2026-11-03)** : l'émetteur `NiPSysMeshEmitter` prélève des meshes
  **skinnés** que `NifParticleMapper.BuildEmitter` cuisait une fois en
  pose de bind. Mesures d'écart bind→posé (CPU `KfPose`, 4 poids) :
  r014 mesh #114 moy. 0,47 m / max 2,21 m en `stand01` ; r253 pattes
  0,24–1,06 m, queue 0,95–2,12 m, corps 0,67–2,23 m. **Pas un bug
  d'attache** (psys sous gizmo statique `PArrayNN-Emitter`, `L17` =
  selle). **Fix livré** :
  - `NifParticleMapper` émet `Result.Skin` (`EmitterSkin`) pour tout
    shape d'émetteur `sh.Skin != null` : verts/normales locaux skin,
    top-4 joints/poids, `BoneSkin` (espace skin→os, NiSkinData),
    `BaseVertex` dans `MeshPoints`. Émetteurs rigides : `Skin = null`,
    chemin historique inchangé.
  - `EmitterSkin.Reskin(bw, pts, nrms)` (pur C#) re-pose par frame :
    `v = lv × BoneSkin × bw` en row-vector, identique à `KfPose.Skin` ;
    `bw[k]` = transform monde de l'os exprimé dans l'espace du nœud
    psys. Entrée `null` → le sous-mesh garde la pose de bind cuite.
  - `NiParticleRuntime` : `BindSkin`/`ResolveSkin` mappe `Part.MeshBlock`
    → `MeshInstance3D` caché → son `Skin`/`Skeleton3D` (ordre des binds
    = `BoneBlocks`, exact sous noms NIF dupliqués), repli os-par-nom ;
    `Reskin()` par frame avant `Sim.Step` calcule
    `bw[k] = Rm(monde os × inv monde psys)` via `GetBoneGlobalPose` —
    pose lue du vrai `Skeleton3D` animé par `AnimationPlayer`, thread
    principal. Os non résolu : `bw = BindWorld × inv(ps.BindWorld)` =
    repli exact de la pose de bind.
  - A/B : `EDEN_FX_SKIN=0` = comportement avant fix ;
    `EDEN_FX_DIAG=1` = diag `[psdiag] émetteur skinné : N sommets`.
  - **Preuves** : tests Eden.Core (+5, 966 verts) — blend 2 os, sous-mesh
    null → bind, bind-pose NIF r014+r253 retombe sur les sommets cuits
    (écart max ≤ 9 mm, résidu NiSkinData global transform),
    naissances `ParticleSim` suivent un mesh posé à +100 Y, coût
    3,5–24 µs/psys (r253, 81–409 verts, min-of-batches — ~73 µs total
    par monture) ≪ budget 500 µs. Captures A/B `tools/ride-lab.sh`
    `STAND:1,W:1,A:1` : `artifacts/ride-lab/skinfix-{off,on}/` —
    r014 (14010) W/A : le nuage de flamme détaché au sol disparaît,
    les flammes suivent queue relevée + pattes ; r253 (25310) : écart
    visible en W/A (sous-pattes/poitrine), `stand01` ≈ bind donc subtil.
  - Statut : `VISUALLY_VALIDATED` + `PERFORMANCE_VALIDATED` (bench
    intégré au test). Limite : non vérifié sur montures à skin non
    résolu au runtime (repli bind prouvé par test, pas en capture) ;
    parité oracle client natif reste `hypothesis`.
- **Alpha « Germe Fétide » (mob 58160 → `M609`) — déjà correct, aucun
  patch requis (2026-11-03)** : chaîne `t_monster` 58160 → `c_monster`
  M609010 → `monsterlist` `m609.kfm`/M60901.dds. L'unique shape
  `Object02` porte `NiAlphaProperty` 0x12EC = **alpha-test pur, seuil
  125** (pas de blend) → `NifMaterial.For` (`scripts/native/NifMaterial.cs:234-238`)
  rend `AlphaScissor` + `AlphaScissorThreshold=125/255≈0,49` ;
  `ToonMaterializer` préserve le StandardMaterial (alphaish), désactive
  le cull. Captures `/tmp/m609*.png` : fond visible entre les brins de
  la toile, **0 pixel noir** dans la silhouette → le « rectangle noir »
  signalé n'est pas reproductible sur le chemin natif actuel. `verified`
  (flags NIF + captures) ; la comparaison pixel-perfect avec le client
  oracle reste `hypothesis`.
- Caméra ride-lab : `--cam=…,dist` est un facteur de la hauteur
  (×h/6, plancher 3) — pour les grosses montures boîte (r091), dist
  < ~8 place la caméra DANS la coque ; les runs cam90/cam270 du
  2026-11-04 à dist 3 sont inutilisables pour r091 (refaites à 16).
