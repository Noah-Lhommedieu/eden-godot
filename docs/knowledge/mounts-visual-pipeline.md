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
Session 2026-11-04 (suite soir) : **sens des anims de flanc monture =
MAPPING CROISÉ** (`MountAnimFor` : A→`move03`, D→`move02`) —
le demandeur décrit le voulou : strafe-A = mouvement « droite vers
gauche » = **penché dans le strafe**. Captures ride-lab 253010
cam90/cam270 : A et D penchés dans le sens du strafe (validé).
⚠️ Historique : le mapping a basculé natif↔croisé plusieurs fois parce
que le client testé tournait des binaires obsolètes ; le verdict final
repose sur les captures ci-dessus, pas sur la parole de l'audit.
Mapping record→action conservé (9→102, 10→103) ; seul le choix de
kf joué est croisé.
**Audit attachement yeux r253** (sonde headless `NifAttachDiag` :
déjà skinné à `Bip01 Head`, 0 bind pendant — aucun patch requis).
Mesures conservées ci-dessous (move02 = flanc droit, move03 = gauche,
espace rendu ET fichier concordants).

## Statut par point

| Point | Statut | Preuve |
|---|---|---|
| Modèles/stem : `rideKey/1000` → `r{key:000}` via `RideList.Stem`, repli natif `param_2/1000` (`FUN_00773d90`, client_game) | verified | `CharVisual.RideStem` ; NetTests `RideTable*` 55 verts |
| Attache cavalier = nœud `L17` animé (natif `CRide::Attach`, rf_game `FUN_0073fc60`) + RideSeat orthonormé + repli RideFit si L17 absent/aberrant | verified | `eden_godot/docs/MONTURES-L17.md` ; `RideSeatMathTests` |
| Textures manquantes = surfaces référençant `nif_tex` sans albedo ; meshes cachés/aides sans texture ≠ trou de données. **Analyse du 2026-11-03 : les 29 FAIL du sweep ont TOUS `notex>0`, aucun ne manque `stand01`** — faux positifs de l'ancien critère (toute surface sans AlbedoTexture, ex. 21 surfaces sur r253) | verified (code + tsv) | `RideLab.Finish` — `notex` ne compte que les références non résolues (`nif_tex` meta copiée par `ToonMaterializer` :414,515), `untx` = info |
| `move01` non requis pour le statut OK (ex. r711 « Tour à Canon » sans marche, natif) | verified (code) | `RideLab.Finish`, note `noMove` |
| Anims directionnelles montées : corps/cavalier ET monture A→`move02`, D→`move03` (mapping natif records 9/10 → actions 102/103), ±45°/±90° facing (`DirectionalLocomotion`, `MountSideAnims`) | verified (natif + tests 2026-11-04) | `DirectionalLocomotionTests.Mount_SideAnims_NativeRecords` ; `artifacts/ride-lab/{cam90,cam270}` — r253/r014 : A = arrière cam90 / face cam270, D = miroir exact → pivot correct ; r091 : modèle symétrique ±90 identique au pixel (pivot prouvé via STAND≠A) ; r711 : aucune anim de marche (donnée native, `noMove`) |
| **Contenu KF des flancs monture** — sondes CPU (`KfPose`+skinning) et rendu (`--probe` ride-lab, repère holder ±90° défait) CONCORDANTES : `move02` cambre la tête `Bip01 Head` vers le **flanc DROIT** (+X rendu / −X fichier, L-bones `Bip01 L*` à −X rendu) et `move03` vers la **GAUCHE**. Les .kf perso penchent dans le strafe (move02 = gauche) ; les .kf monture penchent **hors** du strafe — convention du contenu natif, fidèlement reproduite par le mapping non croisé. `MountAnimFor` : `Left→move02`, `Right→move03`, `Back*→move04`, diagonales/avant→`move01` ; corps `AnimFor`, cavalier `EntityAnimator.RiderAnimFor` (move02→ride05, move03→ride06). Le croisement appliqué le 2026-11-04 matin a été **réverté le soir même** : retour demandeur = « inversé » → attendu = mapping natif | verified (mesure + tests + sonde 2026-11-04) | `PlayerController:1182`, `EntityRegistry:1154`, `RideLab` ; `DirectionalLocomotionTests.Mount_SideAnims_NativeRecords` ; move04 = recul (bassin +0,42 Y, cycle 1,07 s) ; aucun mirror/scale négatif natif (`reindex` « mirror » : aucun usage monture) ; captures `artifacts/ride-lab/fixdir-cam{90,270}/` (r25310 STAND/W/A/D) + `probe-25310/run.log` (X(t) tête/mains) |
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

|| **Scroll UV des textures animées** (flammes coulant le long des queues r253, `R25351.dds` sur les rubans, 4 `NiTextureTransformController` op=1 = trans V) : le sens dépend de `NativeFxOptions.InvertV` → `MatState.Apply` envoie `±Tv` dans `uv_offset`. Il était `true` par défaut (calibré sur `DemoTree` qui flippe V) alors que `NifScene` copie les UV bruts → motif pointe→base au lieu de base→pointe, sur **tous** les modèles NIF (montures + effets de sorts). Fix : `InvertV=false` par défaut, `true` seulement dans `NativeFxDemo` | verified (données : V=0 à la pointe des rubans + in-game 2026-11-04) | `NativeFx.cs`, `NativeFxDemo.cs` |
|| **Meshes émetteurs masqués** : `NiParticleRuntime` rendait `Visible=false` TOUT mesh référencé par un `NiPSysMeshEmitter` (« les géométries émettrices ne se dessinent pas ») — faux : Gamebryo les dessine si non cachés. r502 (Dragon Illusoire) émet depuis R502:0/1 = **tout le corps** → dragon invisible (rider + particules seules). 48/159 montures + 38/531 mobs avaient des émetteurs = meshes visibles. Fix : bloc supprimé — le flag `Hidden` du NIF décide (déjà appliqué par `NifScene`) | verified (données + captures r502/r151/r253 2026-11-04) | `NiParticleRuntime.cs` |

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
- **« Bloc noir » sur la toile — cause réelle trouvée et corrigée
  (2026-11-04)** : ce n'était ni l'alpha ni l'outline mais le **blob
  shadow `Decal`** (`BlobShadow.Make`, volume 3 m posé sous chaque
  entité — `EntityRegistry` ~1818, `PlayerController` ~179,
  `MapLoader` ~1052). Sans `CullMask`, le Decal se projetait sur
  TOUS les meshes dans sa boîte, dont le modèle de l'entité
  elle-même : ellipse noire plaquée sur le quad alpha-test de la
  toile → « bloc noir de texture ». Reproductible uniquement en jeu
  réel (les bancs ne posent pas de blob — flag `--blob` de VisTest
  ajouté pour ça). Fix : visuels d'entités passés sur la couche
  visuelle 2 (`BlobShadow.ExemptVisuals`, appelé depuis
  `RelayoutOverhead` — couvre corps/monture/équipement/labels), et
  `Decal.CullMask = toutes couches sauf 2`. Preuve : capture VisTest
  `--mob=M609 --blob` — toile translucide, 0 bloc noir ; ombre au sol
  conservée. Le même mécanisme assombrissait le capot des
  champignoïdes (« skin presque mort » à full HP, D4). `verified`
  (capture).
- **Yeux r253 « parentés à la racine » — déjà attachés via le skin,
  aucun patch requis (2026-11-04, sonde headless)** : `eye light`
  (NiTriShape blk 483, tex `R25353.dds`, matériau émissif additif
  natif `dst=ONE` → `BlendModeEnum.Add`) a bien son nœud AV sous
  `Scene Root` — **mais il est skinné à `Bip01 Head`** : `NifScene`
  le place sous le `Skeleton3D` avec un bind résolu, donc il suit
  l'os tête (le parenting du graphe est ignoré pour les meshes
  skinnés, comme dans Gamebryo). Mesures (`NifAttachDiag`,
  `--headless`, même assemblage que `BuildRideVisual`) :
  - repos : centroïde œil (−0,00 ; 2,66 ; −1,86) vs tête
    (−0,00 ; 2,56 ; −1,37) → sur la face, 0,5 m devant le pivot
    (museau) ;
  - crâne (verts w>0,5 sur tête) : AABB Y −2,34…−1,07 — l'œil
    (Y −1,98…−1,68, X ±0,32 vs crâne ±0,33) est **dans** l'enveloppe,
    affleurant la surface ;
  - tête pivotée +45° : l'œil suit (−1,86→−1,19, +0,37 haut) →
    **attaché à l'os** ;
  - audit complet : 1 squelette/100 os, **0 bind pendant, 24
    BoneAttachment3D OK** (Bip01 Ponytail1, Ear Bone 01/02, Bone04-82) ;
    billboard œil `Plane` (blk 37, 4 verts) sous `Plane04`→`Bip01 Head`
    → attachment résolu ;
  - corpus : **0** mesh rigide près-tête hors chaîne osseuse sur tous
    les `ride/model/*.nif` → aucun vrai cas « œil racine » ;
  - NiSkinData `m_transform` (global) : non-identitaire sur la plupart
    des shapes r253 **y compris les corrects** → vérifié inutile
    (ignoré au parsing, `NifFile.cs:1190` — les offsets encodent déjà
    skin→os sur les verts bruts ; appliquer G dégrade tout, mesuré
    4–7 m d'erreur). L'écart skinné-vs-`s.Vertices` des `PlaneNN`
    (0,4–1,3 m, ailerons dorés ARRIÈRE) est une métadonnée de transform
    de nœud périmée, pas un décalage de rendu — le natif rend les verts
    skinnés.
  - Limite : si un défaut visuel persiste in-game il est
    matériau/visibilité, pas attachement — le point de vérif est
    `NifAttachDiag` (0 bind pendant). **Captures ride-lab refaites
    2026-11-04** (`artifacts/ride-lab/fixdir-cam{90,270}/` r25310,
    STAND/W/A/D aux deux azimuts + sonde `--probe`
    `artifacts/ride-lab/probe-25310/`) : la bande tête inspectée ne
    montre **aucun quad œil détaché** (œil dans l'enveloppe crâne
    confirmée : centroïde (−0,00 ; 2,66 ; −1,86) sur le museau) —
    aucune anomalie d'attachement visible au rendu ; reste à
    confirmer en live client.
- Caméra ride-lab : `--cam=…,dist` est un facteur de la hauteur
  (×h/6, plancher 3) — pour les grosses montures boîte (r091), dist
  < ~8 place la caméra DANS la coque ; les runs cam90/cam270 du
  2026-11-04 à dist 3 sont inutilisables pour r091 (refaites à 16).
