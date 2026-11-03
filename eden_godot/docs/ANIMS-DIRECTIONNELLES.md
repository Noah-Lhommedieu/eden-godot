# Anims directionnelles (A / D / S) et cap du personnage

Preuves tirées de `re/ghidra/rf_game/decompiled.c` (client natif
`game_.exe`, base 0x400000), du désassemblage de `.text` (objdump sur le
`.exe`) et des tables `.data`. Code client : `scripts/game/
DirectionalLocomotion.cs` (logique pure, testée par
`tools/NetTests/DirectionalLocomotionTests.cs`).

## 1. Résultat

| Touche(s) | Record natif (= MoveType / Flag 326) | Action KFM | Anim (motion.ini) | Cap du corps |
|---|---|---|---|---|
| W | 7 | 101 | `move01` | cap |
| A | 3 | **102** | `move02` | cap (ne tourne pas) |
| D | 5 | **103** | `move03` | cap (ne tourne pas) |
| S | 1 | **104** | `move04` | cap (ne tourne pas) |
| W+A / W+D | 6 / 8 | 101 | `move01` | ±45° (vers le côté) |
| S+A / S+D | 0 / 2 | 104 | `move04` | corps tourné de 45° pour que le déplacement soit « droit arrière » relatif au corps |
| A / D monté | 9 / 10 (variantes monture, action 3 / 5) | 102 / 103 | `move02` / `move03` (cavalier : `ride05` / `ride06`) | pivot ±90° |

L'hypothèse précédente (02 arrière, 03 gauche, 04 droite) était
FAUSSE : **gauche = 02, droite = 03, arrière = 04**.

## 2. Preuves

### 2.1 Records de direction (`DAT_00fe49a8`, 9 × 0x38 octets)

`FUN_00781640` (decompiled l.731995) : `&DAT_00fe49a8 + (LR + FB*3) * 0x38`
(`_nLR`, `_nFB` < 3 ; chaînes d'assert `DirectionControl.cpp`), plus
`DAT_00fe4ba0` (param_3 == 1) et `DAT_00fe4bd8` (param_3 == 2) pour la
monture. Initialisés par `FUN_00eb77a0` (l.2470050+) ; le record 0 est
en `.data` initialisé (lu dans le fichier). Champs (déjà décrits dans
`eden_server/docs/PATCH-CLIENT-BINAIRE.md` § B2) : `+0x04` offset de
déplacement, `+0x08` facteur vitesse, `+0x0c` offset de FACING,
`+0x10` callback d'anim, `+0x14` id d'action, `+0x18` bits.
Extraction du `.exe` (angles en degrés, négatif = gauche) :

```
rec action  LR/FB            move   speed face   callback
 0    0     gauche/arrière   225    0.5   +45    FUN_00741d30 (arrière)
 1    1     centre/arrière   180    0.5   0      FUN_00741d30
 2    2     droite/arrière   135    0.5   -45    FUN_00741d30
 3    3     gauche           -90    1.0   0      FUN_00741d70 (gauche)
 4    4     (aucun)          -      -     -      FUN_006bd800 (no-op)
 5    5     droite           +90    1.0   0      FUN_00741db0 (droite)
 6    6     gauche/avant     -45    1.0   -45    FUN_00740a90 (avant)
 7    7     avant            0      1.0   0      FUN_00740a90
 8    8     droite/avant     +45    1.0   +45    FUN_00740a90
 9    3     monture gauche   -90    1.0   -90    FUN_00741d70
10    5     monture droite   +90    1.0   +90    FUN_00741db0
```

Conséquence « le perso ne se tourne pas » : pour A, D, S et W le
`+0x0c` (facing) vaut 0 → `FUN_00740ac0` (l.`SetDir(heading + record+0xc)`)
garde le cap ; seules les diagonales décalent de ±45°. Le cap = yaw
caméra (`SetDir(camYaw)` au départ du déplacement, `FUN_0075d9f0`).

### 2.2 Callbacks → slots de vtable → action KFM

Les callbacks appellent un slot de vtable de l'objet « DirectionControl »
(vtable `0xf07648`, aussi `0xf08058`) :

* `FUN_00740a90` (avant) → `vfunc+0x30` = `0x00774010`
* `FUN_00741d30` (arrière) → `vfunc+0x34` = `0x007738d0`
* `FUN_00741d70` (gauche) → `vfunc+0x38` = `0x007738f0` (`FUN_007738f0`)
* `FUN_00741db0` (droite) → `vfunc+0x3c` = `0x00773970` (`FUN_00773970`)

Désassemblage des slots (`.text`, `FUN_00735690(id)` = « l'acteur KFM
possède l'action id ») :

* `0x7738d0` (arrière) : `has(0x68) ? 0x68 : 0x65` → **104**, repli 101.
* `0x7738f0` (gauche) : base `DAT_00f080b8[stance]` ; si `[+0xa6]==0`
  (pas en mode combat) → **0x66 = 102** ; repli 101.
* `0x773970` (droite) : base `DAT_00f08108[stance]` ; `[+0xa6]==0` →
  **0x67 = 103** ; repli 101.
* `0x774010` (avant) : `DAT_00f08570[stance]` ; `[+0xa6]==0` ou monté
  (`+0x3c` bits 9/17) → **0x65 = 101**.

Tables `.data` lues dans le fichier (22 stances, index = type d'arme,
`FUN_007375b0`) : avant `101 101 101 111 111 114 101 108 101 114 117
120 123 126 129 108 135 138 141`, gauche `102 102 102 112 112 115
102 109 102 115 118 121 124 127 130 109 136 139 142`, droite
`103 103 103 113 113 116 103 110 103 116 119 122 125 128 131 110 137
140 143` → par stance : avant = base, gauche = base+1, droite = base+2
(mode combat `[+0xa6]` = flag armé, posé par `FUN_00738xxx` via le bit
0x40000 de `+0x23c`). Cas `[+0xa5]` (arme secondaire) : 132-137, et
`[+0xa7]` : 105-107 (non implémentés : le client Godot n'a pas de mode
combat/stance de course ; seul le mode hors combat est porté).

Nom d'anim = `move` + (id − 100) : `motion.ini`
`DB154..DB157=b,101..104,4,move01..move04` (idem c, f, g, k, l, m).

### 2.3 Cavalier

`FUN_007785d0` (l.724289) : action 0x65..0x68 (101-104) → 0xfa4..0xfa7
(4004-4007 = `ride04..ride07`). Donc **ride04 avant, ride05 gauche,
ride06 droite, ride07 arrière** ; la table
`EntityAnimator.RiderAnimFor` s'y conforme déjà. Tous les KFM de monture
(`assets/anims/ride`, 358/363) possèdent 101-104.

### 2.4 Réseau (entités distantes)

`MoveSelf` (C→S, NCID 1) se termine par `s16 type` (zsniff `0700` = 7 =
record « avant »). Le serveur le stocke (`st.MoveFlag`) et le relaie en
octet `Flag` du 326 (`ZoneEntryFlow.Movement.cs:117`,
`(byte)st.MoveFlag`). `Flag` = record 0..8 ; autres valeurs (ex. 0x6a
de mob) = avant. Le client Godot envoyait toujours 7 : il envoie
maintenant `PlayerController.MoveRecord`, et `EntityRegistry.
HandleMovePrediction` en déduit `MoveAnim` (cap reçu = cap du corps).

### 2.5 Patchs de `game_.exe`

`cmp -l` contre `game_.exe` actuel : `bak_strafe3` = 0 diff,
`bak_mountstrafe` = 0 diff (état vanilla des records, NOP de sélection
absents), `bak_strafe4` = 4 octets (les deux `74 38`/`74 11` en
0x35CF0C / 0x35CF33, NOPés puis restaurés : forçait rec9/10 aussi à
pied), `bak_strafeface` = 8 octets (swap L/R des records monture,
reverté). Le `.exe` actuel est donc vanilla côté records : ces patchs ne
changent PAS le mapping avant/gauche/droite/arrière, seulement la
sélection des records monture (voir `PATCH-CLIENT-BINAIRE.md` § B2-B4).

## 3. Clips GLB

`B.glb` (assets/anims/char/animation) : `move01..move04` sont des clips
DISTINCTS (hash des courbes différents ; `move04` dure 1.333 s contre
0.8 s : marche arrière) ; KFM `B.kfm.json` : 101→index 164, 102→165,
103→166, 104→133. La conversion `nif2gltf` est donc correcte (aucune
reconversion nécessaire).

## 4. Non porté / non prouvé

* Vitesse arrière ×0.5 (record +0x08) : non appliquée côté client.
* Mode combat (stances 111-143) et `+0xa5/+0xa7` : non portés.
* Monté, A/D : anim monture `move02/03` + pivot ±90° (records 9/10),
  fidèle aux données ; le rendu visuel du natif n'a pas été capturé.
  Interrupteur : `DirectionalLocomotion.MountSideAnims`.

## 5. Bug « mauvaise anim en reculant » (corrigé)

Cause : `tools/anim/trim_anim_glb.py` (génère les `*.slim.glb` lus en
priorité par le client) n'avait ni `move04` ni `move03` côté entités, et
`move04` manquait aux `CHAR_WANTED` : aucun GLB perso slim ne contenait
`move04` → `EntityAnimator.MoveOrFallback` retombait sur `move01` (marche
avant) en reculant et aux diagonales arrière. Corrigé (wanted sets +
slims régénérés : `trim_anim_glb.py B.glb C.glb F.glb G.glb K.glb L.glb
M.glb` dans `assets/anims/char/animation`). Les assets sont non versionnés
(.gitignore) : régénérer les slim/packs EPK après un clone.
Vérification : scène `scenes/anim_matrix.tscn` (`AnimMatrix.cs`) — les 7
corps (b g m f k l c = race × sexe), 10 phases (idle, W, A, D, S, W+A,
W+D, S+A, S+D, saut) ; le log `[matrix] clips` liste durée/pistes de
`stand01/move01-04/jump01` par corps et `[matrix] MANQUE` signale tout
clip absent.

## 6. Convention de cap (dir) des entités

`dir` (wire 298 float3, 326, 441, MoveSelf) = `3π/2 − atan2(Δy, Δx)` :
`ILifeEntity::ChangeFaceDir` (zone @0x0847a3b0), constante double
@0x08877088 = 4.71238898 lue dans `ZoneServer` ; transformations
locales→monde par `sincosf(3π/2 − dir)`. L'ancien code client/serveur
utilisait `dir = atan2(Δy, Δx)` → tous les PNJ/mobs tournés de
(3π/2 − 2α) (donc « mauvais cap » partout). Code : `scripts/game/EdenDir.cs`
(client, testé `EdenDirTests`), `ZoneEntryFlow.NativeDir` (serveur).
